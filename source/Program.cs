using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

static class Program {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static int Tests(string path){
        string result="";Guid before=Power.Active(),test=Guid.NewGuid();bool clone=false;
        try{
            Check(!File.Exists(Scheduler.Journal),"An existing recovery record must be recovered before testing.");
            var p=new Preferences();Check(Scheduler.Next(false,19,-1,p)==2,"Low entry");Check(Scheduler.Next(false,23,2,p)==2,"Hysteresis");Check(Scheduler.Next(false,25,2,p)==1,"Low exit");Check(Scheduler.Next(true,10,2,p)==0,"AC priority");p.AutoLow=false;Check(Scheduler.Next(false,5,2,p)==1,"Low disabled");
            Check(Math.Abs(ProcessSampler.CpuPercent(1,2,2,4)-12.5)<.00001,"Normalized CPU formula");Check(ProcessSampler.CpuPercent(3,1,1,4)==0,"Negative delta");
            var sampler=new ProcessSampler();sampler.Sample();Thread.Sleep(1100);var sampled=sampler.Sample();Check(sampled.Any(x=>x.Pid==Process.GetCurrentProcess().Id&&x.Cpu.HasValue),"Live process delta");Check(sampled.All(x=>!x.Cpu.HasValue||(x.Cpu>=0&&x.Cpu<=100)),"CPU range");
            var capabilities=Power.Detect(before);uint oldScreen=capabilities.ScreenTimeout?Power.Read(before,Power.Video,Power.Screen):0;var oldEpp=capabilities.EppSettings.Select(x=>Power.Read(before,Power.Cpu,x)).ToArray();
            Power.Run("/duplicatescheme "+before+" "+test);clone=true;
            foreach(uint epp in new uint[]{50,60,75,80}){var configured=Power.Configure(test,epp,180);Check(configured.EppApplied==capabilities.EppSettings.Length,"Supported EPP count");foreach(var setting in capabilities.EppSettings){Check(Power.Read(test,Power.Cpu,setting)==epp,"EPP readback");Check(Power.ReadAC(test,Power.Cpu,setting)==Power.ReadAC(before,Power.Cpu,setting),"AC preserved");}if(capabilities.ScreenTimeout)Check(Power.Read(test,Power.Video,Power.Screen)==180,"Screen readback");}
            Power.Run("/delete "+test);clone=false;
            var scheduler=new Scheduler();scheduler.Enable();Check(File.Exists(Scheduler.Journal),"Journal persisted");scheduler.Restore();Check(!File.Exists(Scheduler.Journal),"Journal removed");Check(!scheduler.Running,"Stopped");
            Check(Power.Active()==before,"Active scheme unchanged");if(capabilities.ScreenTimeout)Check(Power.Read(before,Power.Video,Power.Screen)==oldScreen,"Original screen preserved");for(int i=0;i<oldEpp.Length;i++)Check(Power.Read(before,Power.Cpu,capabilities.EppSettings[i])==oldEpp[i],"Original EPP preserved");
            result="PASS\r\nPolicy thresholds, hysteresis, AC priority, disabled low mode.\r\nCPU delta normalization and live process sampling ("+sampled.Count+" readable processes).\r\nFour profiles tested on "+capabilities.EppSettings.Length+" supported EPP settings; AC settings preserved.\r\nCreate/restore journal lifecycle; original plan unchanged.\r\nNo active-plan switching performed in this test.";
        }catch(Exception ex){result="FAIL: "+ex;}
        finally{if(clone)try{Power.Run("/delete "+test);}catch(Exception ex){result+="\r\nFAIL cleanup: "+ex.Message;}}
        File.WriteAllText(path,result);return result.StartsWith("PASS")&&!result.Contains("FAIL")?0:1;
    }
    static int RestoreAndExit(){try{new Scheduler().Restore();return 0;}catch(Exception ex){try{Directory.CreateDirectory(Preferences.Folder);File.WriteAllText(Path.Combine(Preferences.Folder,"restore-error.txt"),ex.ToString());}catch{}return 1;}}
    [STAThread] static int Main(string[] args){
        bool restoreOnly=args.Length>0&&args[0]=="--restore-and-exit";
        bool created;using(var mutex=new Mutex(true,"Local\\BatteryPilot.SingleInstance",out created)){
            if(!created){if(restoreOnly)return 2;MessageBox.Show("续航助手已在运行，请双击系统托盘图标。");return 1;}
            if(restoreOnly)return RestoreAndExit();
            if(args.Length>0&&args[0]=="--self-test")return Tests(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test.txt"));
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try{var app=new System.Windows.Application();var pilot=new WpfPilot(args.Length>1?args[0]:null,args.Length>1?args[1]:null);app.Run(pilot.Window);return 0;}catch(Exception ex){string folder=args.Length>1?args[1]:Preferences.Folder;Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"startup-error.txt"),ex.ToString());if(args.Length==0)MessageBox.Show(ex.Message,"续航助手启动失败");return 1;}
        }
    }
}


