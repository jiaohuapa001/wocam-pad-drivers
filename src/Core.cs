using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Xml;

namespace WacomLite {
public sealed class Profile {
    public int schemaVersion { get; set; }
    public string name { get; set; }
    public string vendorId { get; set; }
    public string productId { get; set; }
    public int referenceMaximum { get; set; }
    public int press { get; set; }
    public int release { get; set; }
    public int[] pressureCurve { get; set; }
    public bool doubleClick { get; set; }
    public bool windowsInk { get; set; }
    public void Validate() {
        if (schemaVersion!=1 || vendorId!="056A" || productId!="037A") throw new Exception("仅支持 CTL-472 的版本 1 预设。");
        if (referenceMaximum<1 || referenceMaximum>65535 || release<0 || press<=release || press>referenceMaximum*.95) throw new Exception("阈值需满足：0 ≤ 抬起 < 按下 ≤ 满量程的 95%。");
        if (pressureCurve==null || pressureCurve.Length!=6 || pressureCurve.Any(x=>x<0 || x>referenceMaximum)) throw new Exception("压力曲线需包含 6 个有效整数。");
        for(int i=0;i<4;i++) if(pressureCurve[i]>pressureCurve[i+2]) throw new Exception("压力曲线必须单调递增。");
        if(String.IsNullOrWhiteSpace(name) || name.Length>100) throw new Exception("预设名称需为 1 至 100 个字符。");
    }
    public string Json() { return new JavaScriptSerializer().Serialize(this); }
    public Profile Clone() { return Parse(Json()); }
    public static Profile Parse(string text) {
        var js=new JavaScriptSerializer(); js.MaxJsonLength=65536;
        var obj=js.DeserializeObject(text) as Dictionary<string,object>;
        if(obj==null) throw new Exception("预设不是 JSON 对象。");
        foreach(string key in new[]{"schemaVersion","name","vendorId","productId","referenceMaximum","press","release","pressureCurve","doubleClick","windowsInk"})
            if(!obj.ContainsKey(key)) throw new Exception("预设缺少字段："+key);
        foreach(string key in new[]{"schemaVersion","referenceMaximum","press","release"})
            if(!(obj[key] is int)) throw new Exception("预设字段必须是整数："+key);
        foreach(string key in new[]{"doubleClick","windowsInk"}) if(!(obj[key] is bool)) throw new Exception("预设开关必须是布尔值："+key);
        foreach(string key in new[]{"name","vendorId","productId"}) if(!(obj[key] is string)) throw new Exception("预设字段必须是文本："+key);
        var curve=obj["pressureCurve"] as object[];
        if(curve==null || curve.Any(x=>!(x is int))) throw new Exception("曲线必须为整数数组。");
        var p=js.Deserialize<Profile>(text); p.Validate(); return p;
    }
    public static Profile Load(string path) { return Parse(File.ReadAllText(path,Encoding.UTF8)); }
    public static Profile Default() {
        using(var s=typeof(Profile).Assembly.GetManifestResourceStream("fast-writing.json"))
        using(var r=new StreamReader(s,Encoding.UTF8)) return Parse(r.ReadToEnd());
    }
}
public static class Files {
    public static void Atomic(string path,string text) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(tmp,text,new UTF8Encoding(false));
        if(File.Exists(path)) File.Replace(tmp,path,null); else File.Move(tmp,path);
    }
}
public sealed class Driver {
    public readonly string Data, Live;
    public string Preferred { get { return Path.Combine(Data,"preferred.json"); } }
    public string Confirmed { get { return Path.Combine(Data,"confirmed.json"); } }
    public readonly string Utility;
    public Action<string,string> Invoke;
    public Driver():this(ResolveData(),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"WTablet","Wacom_Tablet.dat")) {}
    static string ResolveData() {
        string portable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
        string probe=Path.Combine(portable,"write-test-"+Guid.NewGuid().ToString("N")+".tmp");
        try {
            Directory.CreateDirectory(portable);File.WriteAllText(probe,"readback");
            if(File.ReadAllText(probe)!="readback")throw new IOException("Readback mismatch");
            return portable;
        } catch(Exception e) {
            throw new Exception("程序旁 data 目录不可读写。请把轻笔移动到可写文件夹后重试。\n"+portable+"\n"+e.Message);
        } finally {
            try {if(File.Exists(probe))File.Delete(probe);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetShortPathName(string path,StringBuilder result,uint size);
    public Driver(string data,string live) {
        Data=data; Live=live;
        Utility=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Tablet","Wacom","PrefUtil.exe");
        Invoke=RunUtility;
    }
    public string Version { get { return File.Exists(Utility)?FileVersionInfo.GetVersionInfo(Utility).ProductVersion:"未安装官方驱动"; } }
    public static XmlDocument Read(string path) {
        var d=new XmlDocument(); d.XmlResolver=null; d.PreserveWhitespace=true;
        var settings=new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null };
        using(var r=XmlReader.Create(path,settings)) d.Load(r); return d;
    }
    public static XmlNode Pen(XmlDocument d) {
        var nodes=d.SelectNodes("//TabletArray/ArrayElement[contains(TabletCommInterface/CommPort,'VID_056A&') and contains(TabletCommInterface/CommPort,'PID_037A&')]/TabletTransducerArray/ArrayElement[ApplicationAssociated='0']");
        if(nodes.Count!=1) throw new Exception("未找到唯一的 CTL-472 全局配置。请连接设备并先用官方面板初始化。");
        var pen=nodes[0];
        foreach(string field in Fields.Concat(new[]{"TransducerTipButtonSettings/PressureResolution"}))
            if(pen.SelectSingleNode(field)==null) throw new Exception("驱动配置结构已变化，需适配后才能应用。缺少："+field);
        return pen;
    }
    public static readonly string[] Fields={"TransducerTipButtonSettings/UpperPressureThreshold","TransducerTipButtonSettings/LowerPressureThreshold","TransducerTipButtonSettings/PressureCurveControlPoint","DoubleClickOnOff","WinUseInk"};
    public static readonly string[] Labels={"按下阈值","抬起阈值","压力曲线","笔尖双击","Windows Ink"};
    public static int Maximum(XmlNode pen) {
        int max=Int32.Parse(pen.SelectSingleNode("TransducerTipButtonSettings/PressureResolution").InnerText);
        if(max<1 || max>65535) throw new Exception("驱动压力满量程无效。"); return max;
    }
    public static string[] Expected(Profile p,int max) {
        p.Validate();
        if(max<1 || max>65535) throw new Exception("压力满量程无效。");
        Func<int,int> scale=x=>(int)Math.Round((double)x*max/p.referenceMaximum);
        if(scale(p.press)<=scale(p.release)) throw new Exception("换算后阈值重合，请增大间隔。");
        return new[]{scale(p.press).ToString(),scale(p.release).ToString(),String.Join(" ",p.pressureCurve.Select(x=>scale(x).ToString())),p.doubleClick.ToString().ToLowerInvariant(),p.windowsInk.ToString().ToLowerInvariant()};
    }
    public static string[] Values(XmlDocument d) { var pen=Pen(d); return Fields.Select(x=>pen.SelectSingleNode(x).InnerText).ToArray(); }
    public static bool Matches(XmlDocument d,Profile p) { return Values(d).SequenceEqual(Expected(p,Maximum(Pen(d)))); }
    public static void Patch(XmlDocument d,Profile p) {
        var pen=Pen(d); var expected=Expected(p,Maximum(pen));
        for(int i=0;i<Fields.Length;i++) pen.SelectSingleNode(Fields[i]).InnerText=expected[i];
    }
    public void RunUtility(string operation,string path) {
        if(!File.Exists(Utility)) throw new Exception("请先安装官方 Wacom 驱动。");
        if(operation!="/backup"&&operation!="/restore")throw new Exception("不支持的配置操作。");
        string dir=Path.GetFullPath(Path.GetDirectoryName(path));
        // This driver's parser rejects quoted file names. Use a temporary simple name
        // and an 8.3 directory alias when needed, without changing caller-owned files.
        string argDir=dir;
        if(dir.Contains(" ")) {
            var shortPath=new StringBuilder(32768);uint length=GetShortPathName(dir,shortPath,32768);
            if(length==0||length>=32768||shortPath.ToString().Contains(" ")) throw new Exception("官方配置工具不接受此含空格路径。请将程序放到无空格且可写的目录。");
            argDir=shortPath.ToString();
        }
        string file="transfer-"+Guid.NewGuid().ToString("N")+".wacomprefs";
        string stage=Path.Combine(dir,file),arg=Path.Combine(argDir,file);
        try {
            if(operation=="/restore")File.Copy(path,stage);
            var info=new ProcessStartInfo(Utility,"/silent "+operation+" "+arg) { UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden };
            using(var process=Process.Start(info)) {
                // Do not kill a driver operation midway or start a competing restore.
                process.WaitForExit();
                if(process.ExitCode!=0) throw new Exception("官方配置工具返回错误："+process.ExitCode);
            }
            if(operation=="/backup")File.Copy(stage,path,false);
        } finally {
            if(File.Exists(stage))File.Delete(stage);
        }
    }
    private bool WaitMatch(Profile p) {
        for(int i=0;i<15;i++) {
            try { if(Matches(Read(Live),p)) return true; } catch(IOException) {} catch(XmlException) {}
            Thread.Sleep(200);
        }
        return false;
    }
    public string Apply(Profile p) {
        p.Validate(); Pen(Read(Live));
        string dir=Path.Combine(Data,"backups"); Directory.CreateDirectory(dir);
        string id=DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N");
        string before=Path.Combine(dir,"before-"+id+".wacomprefs"),after=Path.Combine(dir,"applied-"+id+".wacomprefs");
        Invoke("/backup",before);
        var original=Read(before); var oldValues=Values(original);
        var edited=(XmlDocument)original.CloneNode(true); Patch(edited,p); edited.Save(after);
        // Persist recovery pointer before mutation, even if the app is interrupted.
        Files.Atomic(Path.Combine(Data,"last-backup.txt"),before);
        try {
            Invoke("/restore",after);
            if(!WaitMatch(p)) throw new Exception("应用后读回的设置与预设不一致。");
        } catch(Exception error) {
            try {
                Invoke("/restore",before);
                if(!Values(Read(Live)).SequenceEqual(oldValues)) throw new Exception("回退后读回不一致。");
            } catch(Exception rollback) { throw new Exception("应用失败："+error.Message+"\n回退也未确认成功："+rollback.Message+"\n恢复文件："+before); }
            throw new Exception("应用失败，已恢复应用前参数："+error.Message);
        }
        Files.Atomic(Path.Combine(Data,"verified.json"),p.Json());
        return before;
    }
    public void Undo() {
        string path=File.ReadAllText(Path.Combine(Data,"last-backup.txt")).Trim();
        string root=Path.GetFullPath(Path.Combine(Data,"backups"))+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(path).StartsWith(root,StringComparison.OrdinalIgnoreCase)) throw new Exception("恢复文件不在本机备份目录中。");
        var expected=Values(Read(path));
        // Also save the current state before restoring a full backup.
        string safety=Path.Combine(root,"before-undo-"+Guid.NewGuid().ToString("N")+".wacomprefs");
        Invoke("/backup",safety);
        try {
            Invoke("/restore",path);
            if(!Values(Read(Live)).SequenceEqual(expected)) throw new Exception("恢复后的管理参数未通过读回检查。");
        } catch(Exception e) {
            try {Invoke("/restore",safety);if(!Values(Read(Live)).SequenceEqual(Values(Read(safety))))throw new Exception("回退读回不一致。");}
            catch(Exception rollback) {throw new Exception("恢复失败："+e.Message+"\n回退未确认："+rollback.Message+"\n备份："+safety);}
            throw new Exception("恢复失败，已回退到恢复前参数："+e.Message);
        }
    }
}
}
