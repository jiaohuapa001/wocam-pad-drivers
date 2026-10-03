using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace WacomLite {
public static class Native {
    [StructLayout(LayoutKind.Sequential)] public struct Pt { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Pointer {
        public uint Type,Id,Frame,Flags;
        public IntPtr Device,Target;
        public Pt Pixel,Himetric,RawPixel,RawHimetric;
        public uint Time,History;
        public int InputData;
        public uint Keys;
        public ulong Performance;
        public uint ButtonChange;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Pen { public Pointer Info; public uint Flags,Mask,Pressure,Rotation; public int TiltX,TiltY; }
    [DllImport("user32.dll",SetLastError=true)] public static extern bool GetPointerPenInfo(uint id,out Pen pen);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool GetPointerType(uint id,out uint type);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool GetPointerPenInfoHistory(uint id,ref uint count,[Out] Pen[] pen);
    [DllImport("user32.dll")] public static extern IntPtr GetMessageExtraInfo();
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    public static bool PromotedMouse() { return ((ulong)GetMessageExtraInfo().ToInt64() & 0xffffff00UL)==0xff515700UL; }
}
public sealed class TraceSample {
    public string Source,Kind;
    public uint Id,Flags,Time,Frame,Mask,PenFlags;
    public ulong Performance;
    public Point Point;
    public int Pressure=-1;
    public bool Contact;
}
public sealed class StrokeState {
    public bool Active; public Point Last; public uint Id; public string Source;
    public int Strokes,Downs,Ups,Breaks;
    // Return true only for a segment within the same uninterrupted contact.
    public bool Feed(TraceSample s) {
        bool down=(s.Flags&0x10000)!=0,up=(s.Flags&0x40000)!=0,cancel=(s.Flags&0x8000)!=0;
        if(down) Downs++; if(up) Ups++;
        bool draw=s.Contact&&!up&&!cancel;
        bool join=Active&&draw&&!down&&s.Id==Id&&s.Source==Source;
        if(draw&&!join) Strokes++;
        if(Active&&!draw) Breaks++;
        Active=draw; Last=s.Point; Id=s.Id; Source=s.Source;
        return join;
    }
    public void Break() { if(Active) Breaks++; Active=false; }
}
public sealed class InkPad:Control {
    public readonly List<TraceSample> Samples=new List<TraceSample>();
    public StrokeState State=new StrokeState();
    public TraceSample Latest;
    public int PenSamples,MouseSamples,ReadFailures,HistoryFallbacks;
    public bool Full;
    public const int Limit=50000;
    private Bitmap paper;
    private bool mouseDown;
    private long sequence;
    public InkPad() { DoubleBuffered=true; BackColor=Color.White; Cursor=Cursors.Cross; SetStyle(ControlStyles.Selectable,true); TabStop=true; }
    protected override void OnResize(EventArgs e) {
        base.OnResize(e);
        if(Width<1||Height<1) return;
        var next=new Bitmap(Width,Height);
        using(var g=Graphics.FromImage(next)) { g.Clear(Color.White); if(paper!=null) g.DrawImageUnscaled(paper,0,0); }
        if(paper!=null) paper.Dispose(); paper=next;
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); if(paper!=null) e.Graphics.DrawImageUnscaled(paper,0,0);
        using(var pen=new System.Drawing.Pen(Color.FromArgb(229,237,246))) for(int y=65;y<Height;y+=65) e.Graphics.DrawLine(pen,0,y,Width,y);
        if(Samples.Count==0) TextRenderer.DrawText(e.Graphics,"在这里快速写字；每次抬笔后再落笔。",Font,new Point(20,20),Color.SlateGray);
    }
    public void Feed(TraceSample s) {
        if(Full) return;
        if(Samples.Count>=Limit) { Full=true; State.Break(); return; }
        Point last=State.Last; bool join=State.Feed(s);
        Latest=s; Samples.Add(s);
        if(s.Source=="pen") PenSamples++; if(s.Source=="mouse") MouseSamples++;
        if(State.Active && paper!=null) {
            float width=s.Pressure<0?2:1+4*Math.Min(1024,s.Pressure)/1024f;
            using(var g=Graphics.FromImage(paper)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var pen=new System.Drawing.Pen(Color.FromArgb(24,63,104),width)) {
                    pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;
                    if(join) g.DrawLine(pen,last,s.Point);
                    else using(var brush=new SolidBrush(pen.Color)) g.FillEllipse(brush,s.Point.X-width/2,s.Point.Y-width/2,width,width);
                }
            }
        }
        Invalidate();
    }
    public void End(string reason) {
        mouseDown=false;
        Feed(new TraceSample { Source="system",Kind=reason,Point=State.Last,Time=unchecked((uint)Environment.TickCount) });
        State.Break();
    }
    private void PenInput(Native.Pen p) {
        bool down=(p.Info.Flags&0x10000)!=0,up=(p.Info.Flags&0x40000)!=0,cancel=(p.Info.Flags&0x8000)!=0;
        bool contact=(p.Info.Flags&4)!=0;
        string kind=cancel?"CANCEL":up?"UP":down?"DOWN":contact?"CONTACT":"HOVER";
        Feed(new TraceSample { Source="pen",Kind=kind,Id=p.Info.Id,Flags=p.Info.Flags,Time=p.Info.Time,Frame=p.Info.Frame,Performance=p.Info.Performance,
            Mask=p.Mask,PenFlags=p.Flags,Point=PointToClient(new Point(p.Info.Pixel.X,p.Info.Pixel.Y)),Pressure=(p.Mask&1)!=0?(int)p.Pressure:-1,Contact=contact });
    }
    protected override void WndProc(ref Message m) {
        if(m.Msg==0x245||m.Msg==0x246||m.Msg==0x247) {
            uint id=unchecked((uint)m.WParam.ToInt64())&0xffff;
            uint type;
            if(Native.GetPointerType(id,out type)&&type!=3) {base.WndProc(ref m);return;}
            Native.Pen p;
            if(Native.GetPointerPenInfo(id,out p)) {
                if(m.Msg==0x246) Focus();
                // History comes newest first; preserve chronological contact boundaries.
                uint n=Math.Max(1,Math.Min(p.Info.History,1024));
                var history=new Native.Pen[n];
                if(n>1&&Native.GetPointerPenInfoHistory(id,ref n,history)) {
                    for(int i=(int)n-1;i>=0;i--) PenInput(history[i]);
                } else {if(p.Info.History>1) HistoryFallbacks++;PenInput(p);}
                m.Result=IntPtr.Zero; return;
            }
            ReadFailures++; End("POINTER_READ_FAILED");
        }
        if(m.Msg==0x24c) End("CAPTURE_CHANGED");
        if(m.Msg==0x24a) End("POINTER_LEAVE");
        base.WndProc(ref m);
    }
    private void MouseSample(MouseEventArgs e,string kind,uint flags,bool contact) {
        if(Native.PromotedMouse()) return;
        Feed(new TraceSample {Source="mouse",Kind=kind,Flags=flags,Contact=contact,Point=e.Location,Frame=unchecked((uint)++sequence),Time=unchecked((uint)Environment.TickCount)});
    }
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e); if(e.Button!=MouseButtons.Left||Native.PromotedMouse()) return;
        Focus();mouseDown=true;Capture=true;MouseSample(e,"DOWN",0x10004,true);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if(mouseDown) MouseSample(e,"CONTACT",0x20004,true); }
    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e); if(e.Button!=MouseButtons.Left||Native.PromotedMouse()) return;
        MouseSample(e,"UP",0x40000,false);mouseDown=false;Capture=false;
    }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if(!Capture&&mouseDown) End("MOUSE_CAPTURE_LOST"); }
    public void ClearInk() {
        Samples.Clear(); State=new StrokeState(); Latest=null;PenSamples=MouseSamples=ReadFailures=HistoryFallbacks=0;Full=false;mouseDown=false;
        if(paper!=null) using(var g=Graphics.FromImage(paper)) g.Clear(Color.White); Invalidate();
    }
    public void Export(string path) {
        using(var w=new StreamWriter(path,false,new UTF8Encoding(true))) {
            w.WriteLine("source,event,os_time_ms,performance_count,frame,pointer_id,x_client_px,y_client_px,pressure_0_1024_or_minus1,contact,pointer_flags,pen_mask,pen_flags");
            foreach(var s in Samples) w.WriteLine(String.Join(",",new[]{s.Source,s.Kind,s.Time.ToString(),s.Performance.ToString(),s.Frame.ToString(),s.Id.ToString(),s.Point.X.ToString(),s.Point.Y.ToString(),s.Pressure.ToString(),s.Contact?"1":"0",s.Flags.ToString(),s.Mask.ToString(),s.PenFlags.ToString()}));
        }
    }
    public void SaveImage(string path) { if(paper!=null) paper.Save(path,System.Drawing.Imaging.ImageFormat.Png); }
    protected override void Dispose(bool disposing) { if(disposing&&paper!=null) paper.Dispose();base.Dispose(disposing); }
}
public sealed class PressureChart:Control {
    public InkPad Pad;
    public PressureChart() { DoubleBuffered=true; BackColor=Color.FromArgb(242,247,252); }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); if(Pad==null) return;
        using(var pen=new System.Drawing.Pen(Color.SteelBlue,2)) {
            PointF? prev=null;int count=Pad.Samples.Count,start=Math.Max(0,count-160);
            for(int i=start;i<count;i++) {
                var s=Pad.Samples[i];
                if(s.Pressure<0) { prev=null;continue; }
                var point=new PointF((i-start)*(Width-1)/159f,Height-12-Math.Min(1024,s.Pressure)*(Height-24)/1024f);
                if(prev.HasValue) e.Graphics.DrawLine(pen,prev.Value,point); prev=point;
            }
        }
        TextRenderer.DrawText(e.Graphics,"Windows 压力 0–1024 · 最近 160 个事件",Font,new Point(8,4),Color.SlateGray);
    }
}
}
