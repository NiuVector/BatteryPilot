using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

public class Preferences {
    public int Mode=1,LowThreshold=20,ScreenMinutes=5;public bool AutoLow=true;public bool Glass=true,Motion=true;
    public static string Folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BatteryPilot");
    public static Preferences Load(){try{using(var f=File.OpenRead(Path.Combine(Folder,"settings.xml"))){var p=(Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(f);p.Mode=Math.Max(0,Math.Min(2,p.Mode));p.LowThreshold=Math.Max(10,Math.Min(40,p.LowThreshold));p.ScreenMinutes=Math.Max(1,Math.Min(30,p.ScreenMinutes));return p;}}catch{return new Preferences();}}
    public void Save(){Directory.CreateDirectory(Folder);string file=Path.Combine(Folder,"settings.xml"),temp=file+".tmp";using(var f=File.Create(temp))new XmlSerializer(typeof(Preferences)).Serialize(f,this);if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}
}
public class Scheduler {
    public bool Running {get;private set;}public string State="仅监测";public string Compatibility="尚未探测";public int Level=-1;Guid original,custom;Guid expected;string signature="";
    public static string Journal=Path.Combine(Preferences.Folder,"recovery.txt");
    public static int Next(bool ac,double pct,int previous,Preferences p){if(ac)return 0;if(!p.AutoLow)return 1;if(pct<=p.LowThreshold)return 2;if(pct>=p.LowThreshold+5)return 1;return previous==2?2:1;}
    public void Enable(){
        if(File.Exists(Journal))Restore();original=Power.Active();expected=original;custom=Guid.NewGuid();Directory.CreateDirectory(Preferences.Folder);
        File.WriteAllLines(Journal,new[]{original.ToString(),custom.ToString()});
        try{Power.Run("/duplicatescheme "+original+" "+custom);Power.Run("/changename "+custom+" BatteryPilot");Compatibility=Power.Detect(custom).Description;Running=true;Level=-1;signature="";}
        catch{Restore();throw;}
    }
    public void Apply(bool ac,double pct,Preferences p){
        if(!Running)return;
        if(Power.Active()!=expected){Restore();State="外部已切换电源方案，调度已暂停";return;}
        int next=Next(ac,pct,Level,p);int epp=next==2?80:p.Mode==0?50:p.Mode==1?60:75;int screen=next==2?Math.Min(2,p.ScreenMinutes):p.ScreenMinutes;
        string key=next+":"+epp+":"+screen;
        if(signature!=key){
            if(next==0){Power.Run("/setactive "+original);expected=original;}
            else{var configured=Power.Configure(custom,(uint)epp,(uint)(screen*60));Power.Run("/setactive "+custom);expected=custom;Compatibility="CPU 能效偏好已应用 "+configured.EppApplied+"/"+Power.Epp.Length+" · 关屏策略 "+(configured.ScreenApplied?"已应用":"不可用");}
            signature=key;Level=next;
        }
        State=(next==0?"插电 · 使用原方案":next==2?"低电量 · 加强节能":p.Mode==0?"流畅优先":p.Mode==1?"均衡续航":"续航优先")+(next!=0&&Compatibility.StartsWith("CPU 能效偏好已应用 0")?" · 仅关屏策略":"");
    }
    public void Restore(){
        Running=false;Level=-1;signature="";
        if(File.Exists(Journal)){
            var lines=File.ReadAllLines(Journal);if(lines.Length!=2)throw new Exception("恢复记录损坏，请保留 recovery.txt 以便修复。");Guid a=Guid.Parse(lines[0]),b=Guid.Parse(lines[1]);if(a==b)throw new Exception("恢复记录无效。");
            if(Power.Active()==b)Power.Run("/setactive "+a);
            if(Power.Run("/list").IndexOf(b.ToString(),StringComparison.OrdinalIgnoreCase)>=0)Power.Run("/delete "+b);
            File.Delete(Journal);
        }
        State="仅监测 · 已恢复";
    }
}

