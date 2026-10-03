using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml;

namespace WacomLite {
static class Tests {
    static void Assert(bool yes,string reason){if(!yes)throw new Exception("TEST FAILED: "+reason);}
    static void Reject(Action a,string reason){bool rejected=false;try{a();}catch{rejected=true;}Assert(rejected,reason);}
    public static void Run() {
        var p=Profile.Default();p.Validate();Assert(p.press==512&&p.release==491,"exact baseline");
        Assert(Profile.Parse(p.Json()).Json()==p.Json(),"preset roundtrip");
        Reject(()=>Profile.Parse(p.Json().Replace("\"windowsInk\":true","\"windowsInk\":\"true\"")),"strict boolean");
        Reject(()=>Profile.Parse(p.Json().Replace("\"press\":512","\"press\":512.5")),"strict integer");
        Reject(()=>Profile.Parse("{}"),"missing fields");
        var bad=p.Clone();bad.productId="9999";Reject(bad.Validate,"wrong tablet");
        bad=p.Clone();bad.release=bad.press;Reject(bad.Validate,"threshold order");
        bad=p.Clone();bad.pressureCurve=new[]{409,0,300,266,2047,2047};Reject(bad.Validate,"monotonic curve");
        Assert(Driver.Expected(p,4094)[0]=="1024"&&Driver.Expected(p,4094)[1]=="982","resolution scaling");
        Assert((int)Math.Round((double)Decimal.Round((decimal)p.press*100/p.referenceMaximum,3)*p.referenceMaximum/100)==512,"UI preserves original integer");
        string xml="<root><TabletArray><ArrayElement><TabletCommInterface><CommPort>HID#VID_056A&amp;PID_037A&amp;COL02</CommPort></TabletCommInterface><Keep>mapping</Keep><TabletTransducerArray><ArrayElement><ApplicationAssociated>0</ApplicationAssociated><DoubleClickOnOff>true</DoubleClickOnOff><WinUseInk>false</WinUseInk><TransducerTipButtonSettings><UpperPressureThreshold>409</UpperPressureThreshold><LowerPressureThreshold>393</LowerPressureThreshold><PressureResolution>2047</PressureResolution><PressureCurveControlPoint>0 0 1024 1024 2047 2047</PressureCurveControlPoint></TransducerTipButtonSettings></ArrayElement><ArrayElement><ApplicationAssociated>3</ApplicationAssociated><Keep>application</Keep></ArrayElement></TabletTransducerArray></ArrayElement></TabletArray></root>";
        var d=new XmlDocument();d.LoadXml(xml);Driver.Patch(d,p);Assert(Driver.Matches(d,p),"patch and readback");
        Assert(d.SelectSingleNode("//Keep").InnerText=="mapping"&&d.SelectSingleNode("//ArrayElement[ApplicationAssociated='3']/Keep").InnerText=="application","preserve unrelated settings");
        var wrong=new XmlDocument();wrong.LoadXml(xml.Replace("037A","037B"));Reject(()=>Driver.Pen(wrong),"wrong device config");
        wrong.LoadXml(xml.Replace("PressureResolution","NewField"));Reject(()=>Driver.Pen(wrong),"changed driver schema");
        string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string live=Path.Combine(dir,"live.xml");File.WriteAllText(live,xml);
        var driver=new Driver(dir,live);int restores=0;bool ignore=false;
        driver.Invoke=(op,path)=> {if(op=="/backup")File.Copy(live,path);else{restores++;if(!(ignore&&restores==1))File.Copy(path,live,true);}};
        string backup=driver.Apply(p);Assert(File.Exists(backup)&&Driver.Matches(Driver.Read(live),p),"apply transaction");
        driver.Undo();Assert(Driver.Values(Driver.Read(live))[0]=="409","undo");
        driver.Apply(p);restores=0;ignore=true;
        Reject(()=>driver.Undo(),"reject ignored undo");Assert(restores==2&&Driver.Matches(Driver.Read(live),p),"undo failure restores safety snapshot");
        File.WriteAllText(live,xml);restores=0;ignore=true;
        Reject(()=>driver.Apply(p),"reject ignored restore");Assert(restores==2&&Driver.Values(Driver.Read(live))[0]=="409","rollback confirmed");
        string store=Path.Combine(dir,"profile.json");Files.Atomic(store,p.Json());Files.Atomic(store,p.Json());Assert(Profile.Load(store).press==512,"atomic overwrite");
        Assert(Marshal.SizeOf(typeof(Native.Pointer))==96&&Marshal.SizeOf(typeof(Native.Pen))==120,"64-bit pointer ABI layout");
        var state=new StrokeState();
        Func<uint,bool,TraceSample> sample=(flags,contact)=>new TraceSample{Source="pen",Id=1,Point=new Point(20,20),Flags=flags,Contact=contact};
        Assert(!state.Feed(sample(0x10004,true)),"down starts stroke");Assert(state.Feed(sample(0x20004,true)),"contact joins");
        Assert(!state.Feed(sample(0x40000,false)),"up ends stroke");Assert(!state.Feed(sample(0x10004,true)),"second down cannot bridge");
        Assert(state.Downs==2&&state.Ups==1&&state.Strokes==2,"event counters");
        state.Feed(sample(0x8004,true));Assert(!state.Active,"cancel wins over contact");
        Assert(!state.Feed(sample(0x20004,true)),"contact after cancel starts new stroke");
        state.Break();Assert(!state.Feed(sample(0x20004,true)),"capture break separates strokes");
        var other=sample(0x20004,true);other.Id=2;Assert(!state.Feed(other),"different pointer cannot bridge");
        other.Source="mouse";Assert(!state.Feed(other),"different source cannot bridge");
        using(var pad=new InkPad()) {
            pad.Size=new Size(300,200);pad.Feed(sample(0x10004,true));pad.Feed(sample(0x40000,false));
            string csv=Path.Combine(dir,"trace.csv");pad.Export(csv);Assert(File.ReadAllLines(csv).Length==3,"trace export");
            pad.ClearInk();Assert(pad.Samples.Count==0&&pad.State.Downs==0,"clear trace");
            for(int i=0;i<=InkPad.Limit;i++)pad.Feed(sample(0x20000,false));Assert(pad.Full&&pad.Samples.Count==InkPad.Limit,"bounded trace");
        }
    }
}
}
