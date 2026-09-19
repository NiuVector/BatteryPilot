using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using WF=System.Windows.Forms;

public class ProcessView:INotifyPropertyChanged {
    public string Name{get;set;}public string IdText{get;set;}public string CpuText{get;set;}public string MemoryText{get;set;}public string IoText{get;set;}public string Activity{get;set;}public List<int> Pids;public string Key;
    public event PropertyChangedEventHandler PropertyChanged;
    public void Update(ProcessView p){bool changed=Name!=p.Name||IdText!=p.IdText||CpuText!=p.CpuText||MemoryText!=p.MemoryText||IoText!=p.IoText||Activity!=p.Activity;Name=p.Name;IdText=p.IdText;CpuText=p.CpuText;MemoryText=p.MemoryText;IoText=p.IoText;Activity=p.Activity;Pids=p.Pids;Key=p.Key;if(changed&&PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs(null));}
}
class TrendView:FrameworkElement {
    public List<HistoryPoint> Points;static readonly Pen GridPen=MakePen(Color.FromArgb(26,105,122,140),1);static readonly Pen LinePen=MakePen(Color.FromRgb(0,122,255),2.3);
    static Pen MakePen(Color c,double w){var p=new Pen(new SolidColorBrush(c),w);p.Freeze();return p;}
    void Text(DrawingContext dc,string t,double x,double y){dc.DrawText(new FormattedText(t,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),11,Brushes.SlateGray,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));}
    protected override void OnRender(DrawingContext dc){base.OnRender(dc);double w=ActualWidth-48,h=ActualHeight-58;if(w<10||h<10)return;DateTime end=DateTime.Now;var all=Points.Where(p=>p.Time>=end.AddMinutes(-30)).ToList();var valid=all.Where(p=>p.Watts.HasValue).ToList();double max=valid.Count==0?15:Math.Max(10,Math.Ceiling(valid.Max(p=>p.Watts.Value)/5)*5);for(int i=0;i<=4;i++){double y=24+h*i/4;dc.DrawLine(GridPen,new Point(40,y),new Point(40+w,y));Text(dc,(max*(4-i)/4).ToString("0.#"),0,y-7);}Text(dc,"W",0,0);double seconds=all.Count==0?60:Math.Min(1800,Math.Max(60,(end-all[0].Time).TotalSeconds));Text(dc,"最近 "+Math.Ceiling(seconds/60)+" 分钟",40,ActualHeight-22);Text(dc,"现在",ActualWidth-32,ActualHeight-22);if(valid.Count<2){Text(dc,"等待连续放电样本…",70,h/2);return;}var geometry=new StreamGeometry();using(var c=geometry.Open()){bool connected=false;DateTime last=DateTime.MinValue;foreach(var p in all){if(!p.Watts.HasValue){connected=false;continue;}var point=new Point(40+w*(1-(end-p.Time).TotalSeconds/seconds),24+h*(1-p.Watts.Value/max));if(!connected||(p.Time-last).TotalSeconds>60)c.BeginFigure(point,false,false);else c.LineTo(point,true,false);connected=true;last=p.Time;}}geometry.Freeze();dc.DrawGeometry(null,LinePen,geometry);}
}
class WpfPilot {
    public readonly Window Window;readonly Scheduler scheduler=new Scheduler();readonly ProcessSampler sampler=new ProcessSampler();Preferences prefs=Preferences.Load();
    readonly ObservableCollection<ProcessView> displayed=new ObservableCollection<ProcessView>();readonly List<HistoryPoint> history=new List<HistoryPoint>();List<ProcessRow> rows=new List<ProcessRow>();readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
    readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)},searchTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
    readonly WF.NotifyIcon tray=new WF.NotifyIcon();readonly TrendView chart=new TrendView();FrameworkElement[] pages;int page=-1;bool sampling,closing,allowClose,busy;DateTime lastBattery=DateTime.MinValue,lastProcesses=DateTime.MinValue;BatterySample latest;int screen,low;int backdropResult=-1,backdropRead=-1;string testMode,testPath;bool benchmarks;
    [StructLayout(LayoutKind.Sequential)] struct Margins{public int Left,Right,Top,Bottom;}
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr h,int attr,ref int value,int size);
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attr,out int value,int size);
    [DllImport("dwmapi.dll")]static extern int DwmExtendFrameIntoClientArea(IntPtr h,ref Margins m);
    public WpfPilot(string mode,string path){
        testMode=mode;testPath=path;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))Window=(Window)XamlReader.Load(stream);
        Window.Width=Math.Min(1180,SystemParameters.WorkArea.Width-30);Window.Height=Math.Min(790,SystemParameters.WorkArea.Height-30);Window.MinWidth=Math.Min(970,Window.Width);Window.MinHeight=Math.Min(670,Window.Height);
        WindowChrome.SetWindowChrome(Window,new WindowChrome{CaptionHeight=88,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(-1),UseAeroCaptionButtons=false,CornerRadius=new CornerRadius(20)});
        string iconFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"BatteryPilot.ico");using(var icon=File.Exists(iconFile)?new System.Drawing.Icon(iconFile):System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location)){Window.Icon=Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());tray.Icon=(System.Drawing.Icon)icon.Clone();}
        pages=new[]{F<FrameworkElement>("OverviewPage"),F<FrameworkElement>("ProcessPage"),F<FrameworkElement>("PolicyPage"),F<FrameworkElement>("DevicePage")};
        F<Grid>("ChartHost").Children.Add(chart);chart.Points=history;F<DataGrid>("ProcessGrid").ItemsSource=displayed;
        for(int i=0;i<4;i++){int n=i;Click("Nav"+i,()=>Navigate(n));}Click("TrayButton",Hide);Click("MinimizeButton",Hide);Click("CloseButton",()=>Window.Close());
        // Let Windows handle the whole caption band, not only title text.
        WindowChrome.SetIsHitTestVisibleInChrome(F<Button>("MinimizeButton"),true);
        WindowChrome.SetIsHitTestVisibleInChrome(F<Button>("CloseButton"),true);
        F<Grid>("Header").ToolTip="拖动顶部区域移动窗口；双击可最大化或还原";
        F<Button>("StartButton").Click+=async(s,e)=>await Action(()=>{scheduler.Enable();Apply();});F<Button>("StopButton").Click+=async(s,e)=>await Action(()=>scheduler.Restore());Click("ExportButton",Export);
        F<Button>("SaveButton").Click+=async(s,e)=>{prefs=new Preferences{Mode=Checked("Mode0")?0:Checked("Mode2")?2:1,ScreenMinutes=screen,LowThreshold=low,AutoLow=Checked("AutoLow"),Glass=Checked("GlassEnabled"),Motion=Checked("MotionEnabled")};await Action(()=>{prefs.Save();Apply();});};
        Click("ScreenMinus",()=>{screen=Math.Max(1,screen-1);Text("ScreenValue",screen.ToString());});Click("ScreenPlus",()=>{screen=Math.Min(30,screen+1);Text("ScreenValue",screen.ToString());});Click("LowMinus",()=>{low=Math.Max(10,low-1);Text("LowValue",low.ToString());});Click("LowPlus",()=>{low=Math.Min(40,low+1);Text("LowValue",low.ToString());});
        screen=prefs.ScreenMinutes;low=prefs.LowThreshold;Text("ScreenValue",screen.ToString());Text("LowValue",low.ToString());F<RadioButton>("Mode"+prefs.Mode).IsChecked=true;F<CheckBox>("AutoLow").IsChecked=prefs.AutoLow;F<CheckBox>("GlassEnabled").IsChecked=prefs.Glass;F<CheckBox>("MotionEnabled").IsChecked=prefs.Motion;
        F<CheckBox>("GlassEnabled").Checked+=(s,e)=>SetBackdrop();F<CheckBox>("GlassEnabled").Unchecked+=(s,e)=>SetBackdrop();
        F<TextBox>("SearchBox").TextChanged+=(s,e)=>{searchTimer.Stop();searchTimer.Start();};searchTimer.Tick+=(s,e)=>{searchTimer.Stop();RenderProcesses();};foreach(string key in new[]{"SortCpu","SortMemory","SortIo"})F<RadioButton>(key).Checked+=(s,e)=>RenderProcesses();F<CheckBox>("GroupProcesses").Checked+=(s,e)=>RenderProcesses();F<CheckBox>("GroupProcesses").Unchecked+=(s,e)=>RenderProcesses();
        F<DataGrid>("ProcessGrid").MouseDoubleClick+=(s,e)=>{var r=F<DataGrid>("ProcessGrid").SelectedItem as ProcessView;if(r!=null)MessageBox.Show(Window,r.Name+"\nPID: "+string.Join(", ",r.Pids)+"\nCPU: "+r.CpuText+"%\n内存工作集: "+r.MemoryText+" MB\nI/O: "+r.IoText+" MB/s","进程活动详情");};
        Click("TaskManagerButton",()=>Launch("taskmgr.exe"));Click("SettingsButton",()=>Launch("ms-settings:powersleep"));F<Button>("ReportButton").Click+=async(s,e)=>{try{Directory.CreateDirectory(Preferences.Folder);string file=Path.Combine(Preferences.Folder,"battery-report.html");await Task.Run(()=>Power.Run("/batteryreport /output \""+file+"\""));Launch(file);}catch(Exception ex){Error(ex.Message);}};
        tray.Text="续航助手";tray.Visible=mode==null;tray.DoubleClick+=(s,e)=>Window.Dispatcher.BeginInvoke(new System.Action(Show));var menu=new WF.ContextMenuStrip();menu.Items.Add("打开续航助手",null,(s,e)=>Window.Dispatcher.BeginInvoke(new System.Action(Show)));menu.Items.Add("恢复并退出",null,(s,e)=>Window.Dispatcher.BeginInvoke(new System.Action(()=>Window.Close())));tray.ContextMenuStrip=menu;
        timer.Tick+=async(s,e)=>await Poll();Window.SourceInitialized+=(s,e)=>SetBackdrop();Window.Closing+=Closing;Window.Loaded+=async(s,e)=>{await Action(()=>{if(File.Exists(Scheduler.Journal))scheduler.Restore();});var deviceTask=LoadDevice();await Poll();await deviceTask;timer.Start();if(testMode!=null)await Test();};Navigate(0);
    }
    T F<T>(string name)where T:class{return Window.FindName(name)as T;}bool Checked(string name){var t=F<System.Windows.Controls.Primitives.ToggleButton>(name);return t!=null&&t.IsChecked==true;}
    void Text(string name,string value){var t=F<TextBlock>(name);if(t.Text!=value)t.Text=value;}void Click(string name,System.Action action){F<Button>(name).Click+=(s,e)=>action();}
    void SetBackdrop(){
        IntPtr handle=new WindowInteropHelper(Window).Handle;if(handle==IntPtr.Zero)return;int enabled=Checked("GlassEnabled")?3:1;int light=0,round=2;DwmSetWindowAttribute(handle,20,ref light,4);DwmSetWindowAttribute(handle,33,ref round,4);
        var source=HwndSource.FromHwnd(handle);source.CompositionTarget.BackgroundColor=Colors.Transparent;var margins=new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1};int frame=DwmExtendFrameIntoClientArea(handle,ref margins);backdropResult=DwmSetWindowAttribute(handle,38,ref enabled,4);DwmGetWindowAttribute(handle,38,out backdropRead,4);
        bool glass=enabled==3&&backdropResult==0&&frame==0;Window.Background=glass?Brushes.Transparent:new SolidColorBrush(Color.FromRgb(241,245,249));Text("RenderInfo",(glass?"Windows Desktop Acrylic 已请求":"实色材质")+"\nWPF 渲染等级 "+(RenderCapability.Tier>>16)+" / 2 · 页面常驻 · 列表虚拟化\n系统设置或节能状态可能让 Acrylic 回退为实色。");
    }
    void Navigate(int next){if(page==next)return;int old=page;page=next;for(int i=0;i<pages.Length;i++)pages[i].Visibility=i==next?Visibility.Visible:Visibility.Hidden;Text("PageTitle",new[]{"电量与续航","进程活动","电源策略","设备与电池"}[next]);for(int i=0;i<4;i++)F<Button>("Nav"+i).Foreground=i==next?new SolidColorBrush(Color.FromRgb(0,122,255)):Brushes.DimGray;
        var transform=(TranslateTransform)F<Border>("NavIndicator").RenderTransform;bool motion=Checked("MotionEnabled")&&!benchmarks&&old>=0;transform.BeginAnimation(TranslateTransform.YProperty,null);transform.Y=next*56;if(motion)transform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(old*56,next*56,TimeSpan.FromMilliseconds(150)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
        pages[next].BeginAnimation(UIElement.OpacityProperty,null);pages[next].Opacity=1;if(motion)pages[next].BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.6,1,TimeSpan.FromMilliseconds(100)){FillBehavior=FillBehavior.Stop});
    }
    void Hide(){Window.Hide();timer.Interval=TimeSpan.FromSeconds(15);}void Show(){Window.Show();Window.WindowState=WindowState.Normal;Window.Activate();timer.Interval=TimeSpan.FromSeconds(5);}
    async Task LoadDevice(){
        var device=await Task.Run(()=>DeviceSnapshot.Read());Text("DeviceName",device.Name);Text("DeviceHardware",device.Hardware);Text("DeviceSystem",device.System);Text("DeviceBattery",device.Battery);
        try{var capabilities=await Task.Run(()=>Power.Detect(Power.Active()));Text("CompatibilityInfo","调度兼容性："+capabilities.Description+"\n"+(capabilities.EppSettings.Length==0?"CPU 能效偏好不可用时，应用只调整支持的关屏策略。":"应用只会修改本机实际支持并可回读的设置。"));}catch(Exception ex){Text("CompatibilityInfo","调度兼容性探测失败："+ex.Message);}
    }
    void Apply(){var p=WF.SystemInformation.PowerStatus;if(p.PowerLineStatus!=WF.PowerLineStatus.Unknown&&p.BatteryLifePercent>=0&&p.BatteryLifePercent<=1)scheduler.Apply(p.PowerLineStatus==WF.PowerLineStatus.Online,p.BatteryLifePercent*100,prefs);}
    async Task Action(System.Action action){await gate.WaitAsync();busy=true;UpdateState();try{string error=null;try{await Task.Run(action);}catch(Exception ex){error=ex.Message;}if(error!=null){try{await Task.Run(()=>scheduler.Restore());}catch(Exception ex){error+="\n恢复失败："+ex.Message;}Error(error);}}finally{busy=false;UpdateState();gate.Release();}}
    void UpdateState(){Text("StateText","● "+scheduler.State);F<Button>("StartButton").IsEnabled=!busy&&!scheduler.Running;F<Button>("StopButton").IsEnabled=!busy;F<Button>("SaveButton").IsEnabled=!busy;F<Button>("StartButton").Content=new[]{"开启流畅调度","开启均衡调度","开启续航调度"}[prefs.Mode];}
    async Task Poll(){if(sampling||closing)return;sampling=true;try{
        if((DateTime.Now-lastBattery).TotalSeconds>=15){lastBattery=DateTime.Now;latest=await Task.Run(()=>BatterySample.Read());if(closing)return;if(scheduler.Running)await Action(Apply);Battery();}
        if(Window.IsVisible&&(DateTime.Now-lastProcesses).TotalSeconds>=(page==1?5:15)){lastProcesses=DateTime.Now;rows=await Task.Run(()=>sampler.Sample());if(closing)return;RenderProcesses();var top=rows.Where(r=>r.Cpu.HasValue).GroupBy(r=>r.Name).Select(g=>new{Name=g.Key,Cpu=g.Sum(r=>r.Cpu.Value)}).OrderByDescending(r=>r.Cpu).Take(3).ToList();Text("TopProcesses",top.Count==0?"正在建立采样基线，下一轮显示 CPU 活跃进程。":string.Join("    ·    ",top.Select(r=>r.Name+" "+r.Cpu.ToString("0.0")+"%")));}
        Text("Footer","更新于 "+DateTime.Now.ToString("HH:mm:ss")+"  ·  电池 15 秒 / 进程页 5 秒采样  ·  "+(scheduler.Running?"调度已开启":"仅监测"));
    }catch(Exception ex){Text("Footer","采样失败："+ex.Message);}finally{sampling=false;}}
    void Battery(){var p=WF.SystemInformation.PowerStatus;double pct=p.BatteryLifePercent*100;bool known=pct>=0&&pct<=100;Text("Charge",known?pct.ToString("0")+"%":"—");Text("PowerSource",p.PowerLineStatus==WF.PowerLineStatus.Online?"已接电源":p.PowerLineStatus==WF.PowerLineStatus.Offline?"电池供电":"状态未知");Text("Watts",latest.Watts.HasValue?latest.Watts.Value.ToString("0.0")+" W":"—");history.Add(new HistoryPoint{Time=latest.Time,Percent=known?pct:double.NaN,Watts=latest.Watts,Policy=scheduler.State});while(history.Count>2880)history.RemoveAt(0);var window=history.Where(h=>h.Time>=DateTime.Now.AddMinutes(-5)).ToList();var recent=window.Skip(window.FindLastIndex(h=>!h.Watts.HasValue)+1).ToList();double? avg=recent.Count>=2?(double?)recent.Average(h=>h.Watts.Value):null;Text("Average",avg.HasValue?avg.Value.ToString("0.0")+" W":"—");Text("Samples",recent.Count+" 个连续放电样本");double? hours=avg.HasValue&&avg>0&&latest.Remaining.HasValue?latest.Remaining/avg:null;Text("Remaining",hours.HasValue&&hours<100?hours.Value.ToString("0.0")+" 小时":"—");chart.InvalidateVisual();}
    void RenderProcesses(){
        string query=F<TextBox>("SearchBox").Text.Trim();bool grouped=Checked("GroupProcesses");var filtered=rows.Where(r=>r.Name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||r.Pid.ToString().Contains(query));
        var aggregates=filtered.GroupBy(r=>grouped?r.Name:r.Key).Select(g=>new{Key=g.Key,Name=g.First().Name,Cpu=g.Any(r=>r.Cpu.HasValue)?(double?)g.Sum(r=>r.Cpu??0):null,Io=g.Any(r=>r.Io.HasValue)?(double?)g.Sum(r=>r.Io??0):null,Memory=g.Sum(r=>r.Memory),Pids=g.Select(r=>r.Pid).ToList()});
        var sorted=Checked("SortMemory")?aggregates.OrderByDescending(r=>r.Memory):Checked("SortIo")?aggregates.OrderByDescending(r=>r.Io??-1):aggregates.OrderByDescending(r=>r.Cpu??-1);
        var data=sorted.ThenBy(r=>r.Name).Select(r=>new ProcessView{Key=r.Key,Name=r.Name,IdText=grouped?r.Pids.Count+" 个进程":r.Pids[0].ToString(),CpuText=r.Cpu.HasValue?r.Cpu.Value.ToString("0.0"):"—",MemoryText=r.Memory.ToString("0"),IoText=r.Io.HasValue?r.Io.Value.ToString("0.00"):"—",Activity=!r.Cpu.HasValue?"待采样":r.Cpu>=8?"CPU 较高":r.Io>=2?"读写活跃":r.Cpu>=1?"CPU 活跃":"较低",Pids=r.Pids}).ToList();
        string selection=(F<DataGrid>("ProcessGrid").SelectedItem as ProcessView)==null?null:((ProcessView)F<DataGrid>("ProcessGrid").SelectedItem).Key;
        for(int i=0;i<data.Count;i++){if(i<displayed.Count)displayed[i].Update(data[i]);else displayed.Add(data[i]);}while(displayed.Count>data.Count)displayed.RemoveAt(displayed.Count-1);if(selection!=null)F<DataGrid>("ProcessGrid").SelectedItem=displayed.FirstOrDefault(r=>r.Key==selection);
        Text("ProcessSummary",data.Count+" 项 / "+rows.Count+" 个可读进程 · "+sampler.Inaccessible+" 个不可访问 · 双击查看 PID");
    }
    void Export(){var d=new Microsoft.Win32.SaveFileDialog{Filter="CSV 文件|*.csv",FileName="续航记录-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".csv"};if(d.ShowDialog(Window)==true)try{var s=new StringBuilder("Time,BatteryPercent,DischargeWatts,Policy\r\n");foreach(var h in history)s.AppendLine(h.Time.ToString("o")+","+(double.IsNaN(h.Percent)?"":h.Percent.ToString("0.0",CultureInfo.InvariantCulture))+","+(h.Watts.HasValue?h.Watts.Value.ToString("0.000",CultureInfo.InvariantCulture):"")+",\""+h.Policy.Replace("\"","\"\"")+"\"");File.WriteAllText(d.FileName,s.ToString(),new UTF8Encoding(true));}catch(Exception ex){Error(ex.Message);}}
    void Launch(string path){try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception ex){Error(ex.Message);}}void Error(string msg){MessageBox.Show(Window,msg,"续航助手",MessageBoxButton.OK,MessageBoxImage.Warning);}
    async void Closing(object sender,CancelEventArgs e){if(allowClose)return;e.Cancel=true;if(closing)return;closing=true;timer.Stop();searchTimer.Stop();await gate.WaitAsync();try{await Task.Run(()=>scheduler.Restore());allowClose=true;tray.Dispose();Window.Close();}catch(Exception ex){closing=false;timer.Start();Error("恢复失败，已保留恢复记录：\n"+ex.Message);}finally{gate.Release();}}
    async Task Test(){
        timer.Stop();benchmarks=true;Directory.CreateDirectory(testPath);Navigate(1);await Task.Delay(1200);rows=await Task.Run(()=>sampler.Sample());RenderProcesses();
        F<TextBox>("SearchBox").Text="BatteryPilot";searchTimer.Stop();RenderProcesses();bool filter=displayed.Count>0&&displayed.All(r=>r.Name.IndexOf("BatteryPilot",StringComparison.OrdinalIgnoreCase)>=0);F<TextBox>("SearchBox").Text="";searchTimer.Stop();F<CheckBox>("GroupProcesses").IsChecked=false;RenderProcesses();bool ungroup=displayed.Count==rows.Count;F<CheckBox>("GroupProcesses").IsChecked=true;F<RadioButton>("SortMemory").IsChecked=true;RenderProcesses();bool sorted=displayed.Zip(displayed.Skip(1),(a,b)=>double.Parse(a.MemoryText)>=double.Parse(b.MemoryText)).All(x=>x);F<RadioButton>("SortCpu").IsChecked=true;RenderProcesses();
        var times=new List<double>();for(int i=0;i<20;i++){var watch=Stopwatch.StartNew();Navigate(i%4);Window.UpdateLayout();await Window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);watch.Stop();times.Add(watch.Elapsed.TotalMilliseconds);await Task.Delay(60);}
        File.WriteAllLines(Path.Combine(testPath,"navigation-ms.txt"),times.Select(x=>x.ToString("0.00",CultureInfo.InvariantCulture)));
        File.WriteAllText(Path.Combine(testPath,"ui-test.txt"),"Filter="+filter+"; Ungroup="+ungroup+"; Sort="+sorted+"\nDWM HRESULT="+backdropResult+"; backdrop readback="+backdropRead+"; WPF tier="+(RenderCapability.Tier>>16)+"\nVirtualized list; rows="+rows.Count+"; UI dispatch timings only, not display latency or FPS.");
        foreach(int i in new[]{0,1,2,3}){Navigate(i);Window.UpdateLayout();await Window.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);var bitmap=new RenderTargetBitmap((int)Window.ActualWidth,(int)Window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(Window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(testPath,"page-"+i+".png")))encoder.Save(file);}
        Window.Close();
    }
}



