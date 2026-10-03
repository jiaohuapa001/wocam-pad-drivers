using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WacomLite {
public sealed class MainForm:Form {
    readonly Driver driver=new Driver();
    Profile draft,saved;
    readonly TabControl tabs=new TabControl();
    readonly Label status=new Label(),detail=new Label(),inkStatus=new Label();
    readonly TextBox name=new TextBox(),curve=new TextBox();
    readonly NumericUpDown press=new NumericUpDown(),release=new NumericUpDown();
    readonly CheckBox dbl=new CheckBox(),ink=new CheckBox();
    readonly DataGridView grid=new DataGridView();
    readonly InkPad pad=new InkPad();
    readonly PressureChart chart=new PressureChart();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    bool loading,busy;int ticks;
    string notice="";
    public MainForm() {
        Text="轻笔 · Wacom 控制面板 0.3";
        Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Font=new Font("Microsoft YaHei UI",10);
        AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(1000,760);MinimumSize=new Size(920,760);
        StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(246,248,252);
        draft=Profile.Default();
        try { if(File.Exists(driver.Preferred)) draft=Profile.Load(driver.Preferred); }
        catch(Exception e) { notice="已保存预设无法读取，已载入默认快写预设；原文件未改动。"+e.Message; }
        saved=draft.Clone();
        if(driver.Data.StartsWith(AppDomain.CurrentDomain.BaseDirectory,StringComparison.OrdinalIgnoreCase)) notice="使用程序旁 data 目录保存预设和备份。"+(notice.Length>0?" "+notice:"");
        var header=new Panel {Dock=DockStyle.Top,Height=88,Padding=new Padding(22,12,22,8)};
        var title=new Label {Text="轻笔   /   快写与配置保护",AutoSize=true,Font=new Font(Font.FontFamily,19,FontStyle.Bold),Location=new Point(22,12)};
        var sub=new Label {Text="CTL-472 · 独立预设 · 按需运行，关闭后不驻留",AutoSize=true,Location=new Point(24,52),ForeColor=Color.SlateGray};
        header.Controls.Add(title);header.Controls.Add(sub);
        var footer=new Panel {Dock=DockStyle.Bottom,Height=82,Padding=new Padding(20,8,20,8)};
        status.Dock=DockStyle.Fill;status.Text="正在读取配置…";footer.Controls.Add(status);
        tabs.Dock=DockStyle.Fill;tabs.Padding=new Point(24,8);
        var settings=new TabPage("设置与保护") {Padding=new Padding(18)};
        var writing=new TabPage("试写与诊断") {Padding=new Padding(18)};
        tabs.TabPages.Add(settings);tabs.TabPages.Add(writing);
        Controls.Add(tabs);Controls.Add(footer);Controls.Add(header);
        BuildSettings(settings);BuildWriting(writing);
        LoadFields();RefreshState();
        timer.Interval=150;timer.Tick+=delegate { UpdateInkStatus();chart.Invalidate();if(++ticks%67==0&&!busy) RefreshState(); };timer.Start();
        Deactivate+=delegate { if(pad.State.Active) pad.End("WINDOW_DEACTIVATED"); };
        FormClosing+=delegate(object s,FormClosingEventArgs e) { if(busy) {e.Cancel=true;status.Text="正在等待官方配置工具完成，请稍候再关闭。";} };
        FormClosed+=delegate {timer.Dispose();};
    }
    static Label L(string text) {return new Label {Text=text,AutoSize=true,Margin=new Padding(0,7,15,4)};}
    Button B(string text,Action action) {
        var b=new Button {Text=text,AutoSize=true,Height=36,MinimumSize=new Size(110,36),Margin=new Padding(0,3,10,3),Padding=new Padding(8,2,8,2)};
        b.Click+=delegate {try {action();} catch(Exception e) {Error(e);} };return b;
    }
    void Error(Exception e) {status.ForeColor=Color.Firebrick;status.Text=e.Message;MessageBox.Show(this,e.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    void BuildSettings(Control page) {
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,160));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,93));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        detail.Dock=DockStyle.Fill;detail.ForeColor=Color.SlateGray;layout.Controls.Add(detail,0,0);
        var fields=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=4};
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        foreach(var n in new[]{press,release}) {n.DecimalPlaces=3;n.Maximum=95;n.Increment=1;n.Dock=DockStyle.Fill;n.Margin=new Padding(0,2,22,4);n.ValueChanged+=delegate{DraftChanged();};}
        name.Dock=DockStyle.Fill;name.MaxLength=100;name.TextChanged+=delegate{DraftChanged();};
        curve.Dock=DockStyle.Fill;curve.TextChanged+=delegate{DraftChanged();};
        dbl.Text="启用笔尖双击";dbl.AutoSize=true;dbl.CheckedChanged+=delegate{DraftChanged();};
        ink.Text="启用 Windows Ink";ink.AutoSize=true;ink.CheckedChanged+=delegate{DraftChanged();};
        fields.Controls.Add(L("预设名称"),0,0);fields.Controls.Add(name,1,0);fields.SetColumnSpan(name,3);
        fields.Controls.Add(L("按下阈值（%）"),0,1);fields.Controls.Add(press,1,1);fields.Controls.Add(L("抬起阈值（%）"),2,1);fields.Controls.Add(release,3,1);
        fields.Controls.Add(L("压力曲线"),0,2);fields.Controls.Add(curve,1,2);fields.SetColumnSpan(curve,3);
        fields.Controls.Add(dbl,1,3);fields.Controls.Add(ink,3,3);layout.Controls.Add(fields,0,1);
        grid.Dock=DockStyle.Fill;grid.ReadOnly=true;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.RowHeadersVisible=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
        grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        foreach(string column in new[]{"管理参数","编辑中（未必保存）","已保存 / 内置预设","驱动当前值"}) grid.Columns.Add(column,column);
        layout.Controls.Add(grid,0,2);
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=true};
        buttons.Controls.Add(B("保存并应用",ApplyDraft));
        buttons.Controls.Add(B("仅保存预设",delegate {var p=Fields();Files.Atomic(driver.Preferred,p.Json());saved=p.Clone();draft=p;notice="预设已保存，驱动设置未修改。";RefreshState();}));
        buttons.Controls.Add(B("导入预设",Import));buttons.Controls.Add(B("导出预设",Export));buttons.Controls.Add(B("重新检查",delegate {notice="";RefreshState();}));
        buttons.Controls.Add(B("确认试写有效",ConfirmGood));buttons.Controls.Add(B("恢复有效预设",RestoreGood));buttons.Controls.Add(B("撤销上次应用",Undo));
        buttons.Controls.Add(B("打开数据目录",delegate {System.Diagnostics.Process.Start("explorer.exe",driver.Data);}));
        layout.Controls.Add(buttons,0,3);
        var hint=L("提高阈值可能丢失轻笔画；应用会短暂重启驱动。\n驱动更新后可重新检查并恢复；配置结构不兼容时会停止应用。");hint.Dock=DockStyle.Fill;hint.ForeColor=Color.SlateGray;layout.Controls.Add(hint,0,4);
        page.Controls.Add(layout);
    }
    void BuildWriting(Control page) {
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,88));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        var hint=L("记录 Windows 笔事件，不是原始硬件数据，也不代表 Xournal++ 的事件路径。\n不添加断笔过滤；接触中且无抬笔事件时仍连续绘制。鼠标试画不提供压力。");hint.Dock=DockStyle.Fill;hint.ForeColor=Color.SlateGray;layout.Controls.Add(hint,0,0);
        inkStatus.Dock=DockStyle.Fill;layout.Controls.Add(inkStatus,0,1);
        pad.Dock=DockStyle.Fill;pad.Font=Font;layout.Controls.Add(pad,0,2);
        chart.Pad=pad;chart.Dock=DockStyle.Fill;chart.Font=Font;layout.Controls.Add(chart,0,3);
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill};
        buttons.Controls.Add(B("清空试写",delegate {if(pad.Samples.Count>0 && MessageBox.Show(this,"清空当前笔迹和事件记录？需要保留请先导出。","清空试写",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;pad.ClearInk();}));
        buttons.Controls.Add(B("导出事件 CSV",delegate {using(var d=new SaveFileDialog {Filter="CSV 事件记录|*.csv",FileName="pen-events-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv",InitialDirectory=ExportDirectory("logs")}) if(d.ShowDialog(this)==DialogResult.OK) {pad.Export(d.FileName);notice="事件已导出；压力 -1 表示无压力数据。";RefreshState();}}));
        buttons.Controls.Add(B("保存笔迹 PNG",delegate {using(var d=new SaveFileDialog {Filter="PNG 图片|*.png",FileName="pen-test.png",InitialDirectory=ExportDirectory("logs")}) if(d.ShowDialog(this)==DialogResult.OK) pad.SaveImage(d.FileName);}));
        layout.Controls.Add(buttons,0,4);page.Controls.Add(layout);
    }
    void LoadFields() {
        loading=true;name.Text=draft.name;press.Value=(decimal)draft.press*100/draft.referenceMaximum;release.Value=(decimal)draft.release*100/draft.referenceMaximum;
        curve.Text=String.Join(" ",draft.pressureCurve);dbl.Checked=draft.doubleClick;ink.Checked=draft.windowsInk;loading=false;
    }
    Profile Fields() {
        var p=draft.Clone();p.name=name.Text.Trim();p.press=(int)Math.Round((double)press.Value*p.referenceMaximum/100);p.release=(int)Math.Round((double)release.Value*p.referenceMaximum/100);
        p.pressureCurve=curve.Text.Split(new[]{' ',',','，',';','\t'},StringSplitOptions.RemoveEmptyEntries).Select(Int32.Parse).ToArray();p.doubleClick=dbl.Checked;p.windowsInk=ink.Checked;p.Validate();return p;
    }
    void DraftChanged() {if(!loading&&!busy) RefreshState();}
    void RefreshState() {
        if(busy) return;
        try {
            var edit=Fields();var edited=Driver.Expected(edit,edit.referenceMaximum);var stored=Driver.Expected(saved,edit.referenceMaximum);
            string[] actual=null;int max=edit.referenceMaximum;string readError="";
            try {var doc=Driver.Read(driver.Live);max=Driver.Maximum(Driver.Pen(doc));actual=Driver.Values(doc);edited=Driver.Expected(edit,max);stored=Driver.Expected(saved,max);} catch(Exception e){readError=e.Message;}
            grid.Rows.Clear();
            for(int i=0;i<Driver.Fields.Length;i++) {
                int row=grid.Rows.Add(Driver.Labels[i],edited[i],stored[i],actual==null?"不可读取":actual[i]);
                if(actual!=null&&actual[i]!=stored[i]) grid.Rows[row].Cells[3].Style.BackColor=Color.FromArgb(255,239,207);
                if(edited[i]!=stored[i]) grid.Rows[row].Cells[1].Style.BackColor=Color.FromArgb(227,239,255);
            }
            detail.Text="官方驱动 "+driver.Version+"  ·  全局配置  ·  压力满量程 "+max+"  ·  关闭程序不会重置参数";
            bool dirty=edit.Json()!=saved.Json(),drift=actual!=null&&!actual.SequenceEqual(stored);
            string text=actual==null?"当前配置不可读取："+readError:drift?"检测到驱动与已保存预设不同，可重新应用。":"驱动当前设置与已保存预设一致。";
            if(dirty) text+="  界面有未保存修改。";
            status.ForeColor=actual==null?Color.Firebrick:drift?Color.DarkOrange:Color.FromArgb(25,111,89);
            status.Text=text+(String.IsNullOrEmpty(notice)?"":"\n"+notice);
        } catch(Exception e) {status.Text="编辑中的参数无效："+e.Message;status.ForeColor=Color.Firebrick;}
    }
    async void Work(Action action,Action success) {
        if(busy)return;busy=true;tabs.Enabled=false;UseWaitCursor=true;status.ForeColor=Color.SteelBlue;status.Text="正在执行并校验，请稍候。官方驱动响应较慢时不会强行中断。";
        try {await Task.Run(action);success();}
        catch(Exception e) {Error(e);}
        finally {busy=false;tabs.Enabled=true;UseWaitCursor=false;RefreshState();}
    }
    void ApplyDraft() {
        var p=Fields();
        Work(delegate {driver.Apply(p);Files.Atomic(driver.Preferred,p.Json());},delegate {saved=p.Clone();draft=p;notice="应用并读回校验成功。请到试写区验证，再点击【确认试写有效】。";});
    }
    void ConfirmGood() {
        var p=Fields();if(!Driver.Matches(Driver.Read(driver.Live),p)) throw new Exception("当前驱动与编辑参数不同，请先应用再试写。");
        Files.Atomic(driver.Confirmed,p.Json());notice="已记录你确认有效的预设；以后可一键恢复。";RefreshState();
    }
    void RestoreGood() {
        var p=File.Exists(driver.Confirmed)?Profile.Load(driver.Confirmed):Profile.Default();
        string text=File.Exists(driver.Confirmed)?"恢复你上次确认试写有效的预设？":"尚未记录新的有效预设，恢复最初已验证的 CTL-472 快写预设？";
        if(MessageBox.Show(this,text,"恢复有效预设",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        Work(delegate {driver.Apply(p);Files.Atomic(driver.Preferred,p.Json());},delegate {saved=p.Clone();draft=p;LoadFields();notice="有效预设已恢复并读回确认。";});
    }
    void Undo() {
        if(MessageBox.Show(this,"将恢复上次应用前的完整本机 Wacom 配置，包含其他官方设置。继续？","撤销上次应用",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        Work(delegate {driver.Undo();},delegate {notice="应用前完整配置已恢复，管理参数已读回确认。独立预设保留。";});
    }
    void Import() {
        using(var d=new OpenFileDialog {Filter="独立预设 JSON|*.json"}) if(d.ShowDialog(this)==DialogResult.OK) {var p=Profile.Load(d.FileName);draft=p;LoadFields();notice="已载入界面，尚未保存或应用。";RefreshState();}
    }
    void Export() {
        var p=Fields();using(var d=new SaveFileDialog {Filter="独立预设 JSON|*.json",FileName="my-pen-preset.json",InitialDirectory=ExportDirectory("presets")}) if(d.ShowDialog(this)==DialogResult.OK) {Files.Atomic(d.FileName,p.Json());notice="独立预设已导出，不包含本机完整驱动配置。";RefreshState();}
    }
    string ExportDirectory(string name) {string path=Path.Combine(driver.Data,name);Directory.CreateDirectory(path);return path;}
    void UpdateInkStatus() {
        var s=pad.Latest;
        string current=s==null?"等待笔输入":s.Source=="mouse"?"鼠标试画 · 无压力数据":s.Source=="system"?"事件："+s.Kind:"笔 · "+s.Kind+" · 压力 "+(s.Pressure<0?"未提供":s.Pressure+" / 1024");
        inkStatus.Text=current+"  |  落笔事件 "+pad.State.Downs+" / 抬笔事件 "+pad.State.Ups+" / 笔画 "+pad.State.Strokes+"\n笔样本 "+pad.PenSamples+" · 鼠标样本 "+pad.MouseSamples+" · 读取失败 "+pad.ReadFailures+" · 历史回退 "+pad.HistoryFallbacks+(pad.Full?" · 记录已满（50000），请导出并清空":"");
    }
    public void CaptureScreens(string directory) {
        Directory.CreateDirectory(directory);
        for(int i=0;i<tabs.TabCount;i++) {tabs.SelectedIndex=i;Refresh();Application.DoEvents();using(var b=new Bitmap(Width,Height)){DrawToBitmap(b,new Rectangle(0,0,Width,Height));b.Save(Path.Combine(directory,i==0?"settings.png":"writing.png"));}}
        tabs.SelectedIndex=0;
    }
}
static class Program {
    [STAThread] static int Main(string[] args) {
        try {
            Native.SetProcessDPIAware();Native.SetCurrentProcessExplicitAppUserModelID("WacomLite.ControlPanel");
            if(args.Length>0&&args[0]=="--selftest") {Tests.Run();File.WriteAllText(args[1],"PASS: all native tests\n");return 0;}
            if(args.Length>0&&args[0]=="--check") {
                var driver=new Driver();var p=File.Exists(driver.Preferred)?Profile.Load(driver.Preferred):Profile.Default();
                File.WriteAllText(args[1],"Matches="+Driver.Matches(Driver.Read(driver.Live),p)+"\nDriver="+driver.Version+"\nData="+driver.Data+"\n"+String.Join("\n",Driver.Values(Driver.Read(driver.Live))),System.Text.Encoding.UTF8);return 0;
            }
            bool created;using(var mutex=new Mutex(true,"Local\\WacomLite.Native",out created)) {
                if(!created){MessageBox.Show("轻笔已经运行，请切换到现有窗口。");return 0;}
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                var form=new MainForm();
                if(args.Length>1&&args[0]=="--screenshots") form.Shown+=delegate {form.BeginInvoke(new Action(delegate {form.CaptureScreens(args[1]);}));};
                Application.Run(form);
            }
            return 0;
        } catch(Exception e) {
            if(args.Length>1) File.WriteAllText(args[1],e.ToString());else MessageBox.Show(e.Message,"轻笔启动失败");return 1;
        }
    }
}
}
