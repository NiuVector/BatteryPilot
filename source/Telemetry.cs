using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

public class ProcessRow {
    public string Name; public int Pid; public long Started; public double? Cpu,Io; public double Memory;
    public string Key {get{return Pid+":"+Started;}}
}
class CounterSample {public double Cpu,Stamp;public ulong Io;public bool HasIo;}
public class ProcessSampler {
    [StructLayout(LayoutKind.Sequential)] struct IoCounters {public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetProcessIoCounters(IntPtr h,out IoCounters counters);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    Dictionary<string,CounterSample> previous=new Dictionary<string,CounterSample>();
    public int Inaccessible {get;private set;}
    public static double CpuPercent(double oldCpu,double cpu,double elapsed,int cores){return Math.Min(100,Math.Max(0,(cpu-oldCpu)/Math.Max(.001,elapsed)/Math.Max(1,cores)*100));}
    public List<ProcessRow> Sample(){
        var result=new List<ProcessRow>();var next=new Dictionary<string,CounterSample>();int unavailable=0;
        foreach(var p in Process.GetProcesses())using(p){
            try{
                if(p.Id==0)continue;
                var row=new ProcessRow{Pid=p.Id,Name=p.ProcessName,Memory=p.WorkingSet64/1048576.0};
                try{row.Started=p.StartTime.ToUniversalTime().Ticks;}catch{row.Started=0;}
                double cpu=p.TotalProcessorTime.TotalSeconds,stamp=(double)Stopwatch.GetTimestamp()/Stopwatch.Frequency;
                IoCounters io=new IoCounters();IntPtr handle=OpenProcess(0x1000,false,p.Id);bool hasIo=false;
                if(handle!=IntPtr.Zero)try{hasIo=GetProcessIoCounters(handle,out io);}finally{CloseHandle(handle);}
                ulong bytes=io.ReadTransferCount+io.WriteTransferCount;CounterSample old;
                if(row.Started!=0&&previous.TryGetValue(row.Key,out old)&&stamp>old.Stamp){
                    row.Cpu=CpuPercent(old.Cpu,cpu,stamp-old.Stamp,Environment.ProcessorCount);
                    if(hasIo&&old.HasIo&&bytes>=old.Io)row.Io=(bytes-old.Io)/(stamp-old.Stamp)/1048576.0;
                }
                next[row.Key]=new CounterSample{Cpu=cpu,Stamp=stamp,Io=bytes,HasIo=hasIo};result.Add(row);
            }catch{unavailable++;}
        }
        previous=next;Inaccessible=unavailable;return result;
    }
}
public class BatterySample {
    public DateTime Time=DateTime.Now;public double? Watts,Remaining,Full;public string Error;
    public static BatterySample Read(){
        var b=new BatterySample();try{
            using(var q=new ManagementObjectSearcher("root\\wmi","SELECT Discharging,RemainingCapacity,DischargeRate FROM BatteryStatus")){
                q.Options.Timeout=TimeSpan.FromSeconds(5);
                using(var rows=q.Get())foreach(ManagementObject item in rows)using(item){
                    double capacity=Convert.ToDouble(item["RemainingCapacity"]),rate=Convert.ToDouble(item["DischargeRate"]);
                    if(capacity>0&&capacity<1000000)b.Remaining=capacity/1000;
                    if(Convert.ToBoolean(item["Discharging"])&&rate>0&&rate<1000000)b.Watts=rate/1000;break;
                }
            }
            using(var q=new ManagementObjectSearcher("root\\wmi","SELECT FullChargedCapacity FROM BatteryFullChargedCapacity")){
                q.Options.Timeout=TimeSpan.FromSeconds(5);using(var rows=q.Get())foreach(ManagementObject item in rows)using(item){double n=Convert.ToDouble(item["FullChargedCapacity"]);if(n>0&&n<1000000)b.Full=n/1000;break;}
            }
        }catch(Exception ex){b.Error=ex.Message;}return b;
    }
}
public class DeviceSnapshot {
    public string Name="Windows 设备",Hardware="硬件信息不可用",Battery="电池详细信息不可用",System="Windows";
    static List<Dictionary<string,object>> All(string scope,string query,params string[] properties){
        var result=new List<Dictionary<string,object>>();try{using(var q=new ManagementObjectSearcher(scope,query)){q.Options.Timeout=TimeSpan.FromSeconds(5);using(var rows=q.Get())foreach(ManagementObject item in rows)using(item){var values=new Dictionary<string,object>();foreach(var property in properties)try{values[property]=item[property];}catch{}result.Add(values);}}}catch{}return result;
    }
    static Dictionary<string,object> First(string scope,string query,params string[] properties){var rows=All(scope,query,properties);return rows.Count>0?rows[0]:new Dictionary<string,object>();}
    static string S(Dictionary<string,object> values,string key,string fallback){object value;return values.TryGetValue(key,out value)&&value!=null&&!string.IsNullOrWhiteSpace(value.ToString())?value.ToString().Trim():fallback;}
    static ulong U(Dictionary<string,object> values,string key){object value;ulong number;return values.TryGetValue(key,out value)&&value!=null&&ulong.TryParse(value.ToString(),out number)?number:0;}
    static string Size(ulong bytes){if(bytes==0)return "容量未知";double gb=bytes/1073741824.0;return (gb>=900?Math.Round(gb/1024).ToString("0")+" TB":Math.Round(gb).ToString("0")+" GB");}
    static double? BatteryValue(string className,string property){var data=First("root\\wmi","SELECT "+property+" FROM "+className,property);ulong value=U(data,property);return value>0&&value<1000000?(double?)(value/1000.0):null;}
    public static DeviceSnapshot Read(){
        var result=new DeviceSnapshot();
        var system=First("root\\cimv2","SELECT Manufacturer,Model,TotalPhysicalMemory FROM Win32_ComputerSystem","Manufacturer","Model","TotalPhysicalMemory");
        string manufacturer=S(system,"Manufacturer","");string model=S(system,"Model",Environment.MachineName);result.Name=(manufacturer+" "+model).Trim();
        var cpu=First("root\\cimv2","SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor","Name","NumberOfCores","NumberOfLogicalProcessors");
        string cpuName=S(cpu,"Name","处理器未知");ulong cores=U(cpu,"NumberOfCores"),logical=U(cpu,"NumberOfLogicalProcessors");
        var disk=First("root\\cimv2","SELECT Model,Size FROM Win32_DiskDrive","Model","Size");var video=All("root\\cimv2","SELECT Name FROM Win32_VideoController","Name");var gpu=video.FirstOrDefault(v=>{string n=S(v,"Name","").ToLowerInvariant();return !n.Contains("idd")&&!n.Contains("remote")&&!n.Contains("basic display")&&!n.Contains("mirror")&&!n.Contains("virtual");})??(video.Count>0?video[0]:new Dictionary<string,object>());
        result.Hardware=cpuName+(cores>0?" · "+cores+" 核 / "+logical+" 逻辑处理器":"")+"\n"+Size(U(system,"TotalPhysicalMemory"))+" 内存 · "+Size(U(disk,"Size"))+" 存储\n"+S(gpu,"Name","显示适配器未知");
        var os=First("root\\cimv2","SELECT Caption,Version,OSArchitecture FROM Win32_OperatingSystem","Caption","Version","OSArchitecture");result.System=S(os,"Caption","Windows")+" · "+S(os,"OSArchitecture","")+" · "+S(os,"Version","");
        double? designed=BatteryValue("BatteryStaticData","DesignedCapacity"),full=BatteryValue("BatteryFullChargedCapacity","FullChargedCapacity");var cycleData=First("root\\wmi","SELECT CycleCount FROM BatteryCycleCount","CycleCount");ulong cycles=U(cycleData,"CycleCount");
        if(designed.HasValue||full.HasValue){var parts=new List<string>();if(designed.HasValue)parts.Add("设计容量 "+designed.Value.ToString("0.###")+" Wh");if(full.HasValue)parts.Add("当前满充 "+full.Value.ToString("0.###")+" Wh");if(designed>0&&full.HasValue)parts.Add("健康度约 "+Math.Min(999,full.Value/designed.Value*100).ToString("0")+"%");if(cycles>0)parts.Add("循环 "+cycles+" 次");result.Battery=string.Join(" · ",parts);}
        else result.Battery="系统未提供电池设计容量或满充容量；实时电量与功耗仍会单独读取。";
        return result;
    }
}
public class HistoryPoint {public DateTime Time;public double? Watts;public double Percent;public string Policy;}
