using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public class PowerCapabilities {
    public Guid[] EppSettings=new Guid[0];
    public bool ScreenTimeout;
    public string Description { get { return "CPU 能效偏好 "+EppSettings.Length+"/"+Power.Epp.Length+" · 关屏策略 "+(ScreenTimeout?"可用":"不可用"); } }
}
public class PowerConfigurationResult {
    public int EppApplied;
    public bool ScreenApplied;
    public string[] Errors=new string[0];
}
static class Power {
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr key, out IntPtr scheme);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
    [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr key, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr key, ref Guid scheme, ref Guid group, ref Guid setting, out uint value);
    public static Guid Active() { IntPtr p; uint e=PowerGetActiveScheme(IntPtr.Zero,out p); if(e!=0) throw new Exception("读取电源方案失败："+e); try {return (Guid)Marshal.PtrToStructure(p,typeof(Guid));} finally{LocalFree(p);} }
    public static string Run(string args) {
        var si=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"powercfg.exe"),args) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using(var p=Process.Start(si)) {var o=p.StandardOutput.ReadToEndAsync(); var e=p.StandardError.ReadToEndAsync(); if(!p.WaitForExit(15000)){p.Kill();throw new Exception("电源命令超时");} Task.WaitAll(o,e); if(p.ExitCode!=0)throw new Exception(o.Result+e.Result+"\n如提示拒绝访问，请右键以管理员身份运行。"); return o.Result;}
    }
    public static readonly Guid Cpu=new Guid("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid Video=new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
    public static readonly Guid Screen=new Guid("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    public static readonly Guid[] Epp={new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863"),new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6864"),new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6865")};
    public static uint Read(Guid scheme,Guid group,Guid setting){uint v;uint e=PowerReadDCValueIndex(IntPtr.Zero,ref scheme,ref group,ref setting,out v);if(e!=0)throw new Exception("读取设置失败："+e);return v;}
    public static uint ReadAC(Guid scheme,Guid group,Guid setting){uint v;uint e=PowerReadACValueIndex(IntPtr.Zero,ref scheme,ref group,ref setting,out v);if(e!=0)throw new Exception("读取交流设置失败："+e);return v;}
    public static bool TryRead(Guid scheme,Guid group,Guid setting,out uint value){return PowerReadDCValueIndex(IntPtr.Zero,ref scheme,ref group,ref setting,out value)==0;}
    public static PowerCapabilities Detect(Guid scheme){
        var supported=new System.Collections.Generic.List<Guid>();uint value;
        foreach(var setting in Epp)if(TryRead(scheme,Cpu,setting,out value))supported.Add(setting);
        return new PowerCapabilities{EppSettings=supported.ToArray(),ScreenTimeout=TryRead(scheme,Video,Screen,out value)};
    }
    static void Set(Guid scheme,Guid group,Guid setting,uint v){Run("/setdcvalueindex "+scheme+" "+group+" "+setting+" "+v);if(Read(scheme,group,setting)!=v)throw new Exception("设置回读不一致");}
    public static PowerConfigurationResult Configure(Guid scheme,bool low){return Configure(scheme,low?70u:60u,low?120u:300u);}
    public static PowerConfigurationResult Configure(Guid scheme,uint epp,uint screen){
        var result=new PowerConfigurationResult();var errors=new System.Collections.Generic.List<string>();var capabilities=Detect(scheme);
        foreach(var setting in capabilities.EppSettings)try{Set(scheme,Cpu,setting,epp);result.EppApplied++;}catch(Exception ex){errors.Add("CPU "+setting.ToString("D")+"："+ex.Message);}
        if(capabilities.ScreenTimeout)try{Set(scheme,Video,Screen,screen);result.ScreenApplied=true;}catch(Exception ex){errors.Add("关屏时间："+ex.Message);}
        result.Errors=errors.ToArray();
        if(result.EppApplied==0&&!result.ScreenApplied)throw new Exception("此设备没有可写的 CPU 能效偏好或关屏设置。"+(errors.Count>0?"\n"+string.Join("\n",errors):""));
        return result;
    }
}


