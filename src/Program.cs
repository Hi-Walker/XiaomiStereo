using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class StereoProgram {
    [STAThread]
    static void Main(string[] args) {
        try {Run(ParseArguments(args));}
        catch(Exception e){MessageBox.Show("小米立体声启动失败。\r\n"+BoundedDiagnostics.Limit(e.Message,320),"小米立体声");}
    }
    internal sealed class LaunchArguments {
        internal bool Startup;
        internal string Directory;
    }
    internal static LaunchArguments ParseArguments(string[] args) {
        var launch=new LaunchArguments();
        for(int i=0;i<args.Length;i++) {
            if(args[i]=="--startup"){launch.Startup=true;continue;}
            if(args[i]=="--state-dir" && i+1<args.Length){launch.Directory=Path.GetFullPath(args[++i]);continue;}
            throw new ArgumentException("未知启动参数。支持 --startup 和 --state-dir <目录>。");
        }
        return launch;
    }
    internal static string ResolveStateDirectory(string[] args) {
        return ParseArguments(args).Directory ?? ConfigStore.DefaultDirectory;
    }
    static void Run(LaunchArguments launch) {
        bool first;
        // Create the signal before acquiring the mutex. A second manual launch
        // can then leave a request even before the first UI loop is ready.
        using(var activation=new ActivationSignal())
        using(var guard=new Mutex(true,@"Local\XiaomiUsbStereo",out first)) {
            if(!first){ActivateExisting(launch,activation);return;}
            string directory=launch.Directory ?? ConfigStore.DefaultDirectory;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try {
                using(var worker=new Coordinator(directory))
                using(var tray=new StereoTray(worker,activation)) {
                    EventHandler exit=(s,e)=>worker.Dispose();
                    // SessionEnding can be cancelled. Release only after Windows confirms the session ended.
                    SessionEndedEventHandler ended=(s,e)=>worker.Dispose();
                    PowerModeChangedEventHandler power=(s,e)=>{
                        if(e.Mode==PowerModes.Suspend || e.Mode==PowerModes.Resume)
                            worker.SleepChanged(e.Mode==PowerModes.Suspend);
                    };
                    AppDomain.CurrentDomain.ProcessExit+=exit;
                    SystemEvents.SessionEnded+=ended;
                    SystemEvents.PowerModeChanged+=power;
                    try {
                        if(!launch.Startup)tray.OpenControlPanel();
                        Application.Run();
                    }
                    finally {
                        SystemEvents.PowerModeChanged-=power;
                        SystemEvents.SessionEnded-=ended;
                        AppDomain.CurrentDomain.ProcessExit-=exit;
                    }
                }
            } catch(Exception e){MessageBox.Show(BoundedDiagnostics.Limit(e.Message,320),"小米立体声");}
        }
    }
    internal static void ActivateExisting(LaunchArguments launch,ActivationSignal activation) {
        if(!launch.Startup)activation.Request(true);
    }
}

// One fixed kernel handle, polled by the existing UI timer. No worker or listener thread.
internal sealed class ActivationSignal : IDisposable {
    const string DefaultName=@"Local\XiaomiUsbStereo.OpenControlPanel";
    readonly EventWaitHandle signal;
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    internal ActivationSignal(string name=DefaultName) {
        signal=new EventWaitHandle(false,EventResetMode.AutoReset,name);
    }
    internal bool Consume(){return signal.WaitOne(0);}
    internal void Request(bool grantForeground=false) {
        if(grantForeground) {
            try {GrantForeground();}
            catch(System.ComponentModel.Win32Exception) {} catch(InvalidOperationException) {}
        }
        signal.Set();
    }
    static void GrantForeground() {
        using(var current=Process.GetCurrentProcess()) {
            string path=current.MainModule.FileName;
            foreach(var process in Process.GetProcessesByName(current.ProcessName))using(process) {
                try {
                    if(process.Id!=current.Id && process.SessionId==current.SessionId &&
                        String.Equals(process.MainModule.FileName,path,StringComparison.OrdinalIgnoreCase))
                        AllowSetForegroundWindow(process.Id);
                } catch(System.ComponentModel.Win32Exception) {} catch(InvalidOperationException) {}
            }
        }
    }
    public void Dispose(){signal.Dispose();}
}

internal sealed class StereoTray : IDisposable {
    static readonly Color Background=Color.FromArgb(245,246,248), Ink=Color.FromArgb(32,36,43),
        Muted=Color.FromArgb(103,111,123), Line=Color.FromArgb(226,230,235), Orange=Color.FromArgb(255,105,0);
    readonly Coordinator coordinator;
    readonly ActivationSignal activation;
    readonly ContextMenuStrip menu=new ContextMenuStrip();
    readonly NotifyIcon icon=new NotifyIcon();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly Stream artwork;
    readonly Icon logo;
    Form controlWindow,deviceWindow,statusWindow;
    TextBox statusText;
    bool pendingDevices;
    int priorSerial;
    internal sealed class PanelActions {
        public Action<bool> Enable;
        public Action Mix,Devices,Calibration,Resync,Diagnostics;
    }
    sealed class Card : Panel {
        internal Card(){BackColor=Color.White;Padding=new Padding(16);Margin=new Padding(0,0,0,12);Dock=DockStyle.Fill;}
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            using(var pen=new Pen(Line))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
        }
    }
    internal StereoTray(Coordinator c,ActivationSignal activation=null) {
        coordinator=c;this.activation=activation;
        artwork=typeof(StereoTray).Assembly.GetManifestResourceStream("XiaomiStereo.ico");
        logo=new Icon(artwork,new Size(48,48));
        menu.Items.Add("打开控制面板",null,(s,e)=>OpenControlPanel());
        menu.Items.Add("退出",null,(s,e)=>Application.ExitThread());
        icon.Icon=logo;icon.Text="小米立体声";icon.ContextMenuStrip=menu;
        icon.DoubleClick+=(s,e)=>OpenControlPanel();icon.Visible=true;
        timer.Interval=1000;timer.Tick+=(s,e)=>RefreshView();timer.Start();
    }
    void RefreshView() {
        if(activation!=null && activation.Consume())OpenControlPanel();
        var view=coordinator.Current;
        string tip=BoundedDiagnostics.Limit("小米立体声 · "+view.State,63);
        if(icon.Text!=tip)icon.Text=tip;
        if(statusWindow!=null)statusText.Text=Summary(view,true);
        if(controlWindow!=null)RefreshControlPanel(controlWindow,view);
        if(pendingDevices && view.ChoiceSerial!=priorSerial) {
            pendingDevices=false;
            if(controlWindow==null)return;
            if(view.ChoiceError!="") {
                Control(controlWindow,"controlDetail").Text="设备列表读取失败。可重试“设备设置”，或打开诊断查看详情。\r\n"+view.ChoiceError;
            } else if(view.Settings!=null)ShowDeviceSettings(view);
        }
    }
    internal void OpenControlPanel() {
        if(controlWindow!=null){RestoreWindow(controlWindow);return;}
        var actions=new PanelActions {
            Enable=coordinator.Enable,Mix=ApplyMix,Devices=OpenDevices,
            Calibration=()=>{using(var details=BuildCalibration(logo))details.ShowDialog(controlWindow);},
            Resync=coordinator.Resync,Diagnostics=OpenStatus
        };
        var form=BuildControlPanel(coordinator.Current,logo,actions);
        controlWindow=form;
        form.FormClosed+=(s,e)=>{controlWindow=null;pendingDevices=false;};
        form.Show();
        RestoreWindow(form);
        // Names are refreshed after the window is already usable. Enumeration failure leaves controls available.
        coordinator.RequestChoices();
    }
    void ApplyMix() {
        var latest=coordinator.Current.Settings;
        if(latest==null || controlWindow==null)return;
        var copy=latest.Copy();
        copy.SwapChannels=((CheckBox)Control(controlWindow,"swapChannels")).Checked;
        copy.BalanceDb=((TrackBar)Control(controlWindow,"balance")).Value/2.0;
        coordinator.Configure(copy);
    }
    void OpenDevices() {
        if(deviceWindow!=null){RestoreWindow(deviceWindow);return;}
        if(pendingDevices)return;
        if(coordinator.Current.Settings==null) {
            Control(controlWindow,"controlDetail").Text="设置文件需要修复，请打开“状态与诊断”查看错误。";
            return;
        }
        priorSerial=coordinator.Current.ChoiceSerial;pendingDevices=true;coordinator.RequestChoices();
        Control(controlWindow,"controlDetail").Text="正在读取可用设备…";
    }
    void ShowDeviceSettings(Coordinator.View view) {
        var form=BuildDeviceSettings(view.Settings.Copy(),view.Choices,logo,edited=>{
            MergeDeviceMix(controlWindow,edited,coordinator.Current.Settings);
            coordinator.Configure(edited);
        });
        deviceWindow=form;
        form.FormClosed+=(s,e)=>deviceWindow=null;
        form.Show(controlWindow);
        RestoreWindow(form);
    }
    internal static void MergeDeviceMix(Form panel,AppSettings edited,AppSettings latest) {
        // A device save can replace a pending mix command before the owner publishes its snapshot.
        // The visible controls hold the user's newer mix values in that interval.
        if(panel!=null && !panel.IsDisposed) {
            edited.SwapChannels=((CheckBox)Control(panel,"swapChannels")).Checked;
            edited.BalanceDb=((TrackBar)Control(panel,"balance")).Value/2.0;
        } else if(latest!=null) {
            edited.SwapChannels=latest.SwapChannels;edited.BalanceDb=latest.BalanceDb;
        }
    }
    string Summary(Coordinator.View view,bool forDisplay) {
        var text=new StringBuilder();text.AppendLine(view.State);
        if(forDisplay && !String.IsNullOrEmpty(view.Detail))text.AppendLine(view.Detail);
        if(forDisplay && view.Detail!=view.LastError && !String.IsNullOrEmpty(view.LastError))text.AppendLine("最近错误　"+view.LastError);
        text.AppendLine("会话代次 "+view.Generation+"，成功建链 "+view.Restarts+" 次");
        if(view.RetrySeconds>0)text.AppendLine("距下次连接约 "+view.RetrySeconds+" 秒");
        if(view.Settings!=null) {
            text.AppendLine("缓冲档 "+view.Settings.Profile+"，左右交换 "+(view.Settings.SwapChannels?"开":"关")+"，平衡 "+view.Settings.BalanceDb+" dB");
            text.AppendLine(view.Settings.ManageDefaults?"自动管理 Console / Multimedia":"手动选择 Windows 默认输出");
        }
        if(!String.IsNullOrEmpty(view.Audio))text.AppendLine(view.Audio);
        text.AppendLine("驱动相对时间不可用，声学延时未校准");text.AppendLine(coordinator.Diag.Counts);
        using(var process=Process.GetCurrentProcess())
            text.Append("本进程私有内存 ").Append(process.PrivateMemorySize64/1048576).Append(" MiB，句柄 ").Append(process.HandleCount);
        return text.ToString();
    }
    void OpenStatus() {
        if(statusWindow!=null){RestoreWindow(statusWindow);return;}
        var form=BuildDiagnostics(logo,Summary(coordinator.Current,true),owner=>{
            using(var dialog=new SaveFileDialog{FileName="小米立体声诊断.txt",Filter="文本文件 (*.txt)|*.txt",OverwritePrompt=true}) {
                if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
                try {coordinator.Diag.Export(dialog.FileName,Summary(coordinator.Current,false));}
                catch(Exception ex){MessageBox.Show(owner,BoundedDiagnostics.Limit(ex.Message,240),"导出失败");}
            }
        });
        statusWindow=form;statusText=(TextBox)Control(form,"diagnosticsText");
        form.FormClosed+=(s,e)=>{statusWindow=null;statusText=null;};form.Show(controlWindow);RestoreWindow(form);
    }
    [DllImport("user32.dll")] static extern IntPtr GetLastActivePopup(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    internal static void RestoreWindow(Form form,bool activate=true) {
        if(form==null || form.IsDisposed)return;
        if(form.Owner!=null && !form.Owner.IsDisposed)MakeVisible(form.Owner);
        MakeVisible(form);
        if(!form.Enabled) {
            // ShowDialog disables its owner. Keep that state and bring the
            // existing interactive child forward instead of bypassing modality.
            foreach(Form child in form.OwnedForms) {
                if(!child.IsDisposed && child.Enabled && (child.Modal || child.Visible)) {
                    MakeVisible(child);
                    if(activate){child.BringToFront();child.Activate();}
                    return;
                }
            }
            // SaveFileDialog is a native owned window, absent from OwnedForms.
            IntPtr popup=form.Handle;
            for(int i=0;i<8;i++) {
                IntPtr next=GetLastActivePopup(popup);
                if(next==IntPtr.Zero || next==popup)break;
                popup=next;
                if(IsWindowEnabled(popup) && IsWindowVisible(popup)) {
                    ShowWindow(popup,9); // SW_RESTORE
                    if(activate)SetForegroundWindow(popup);
                    return;
                }
            }
            return;
        }
        if(activate){form.BringToFront();form.Activate();}
    }
    static void MakeVisible(Form form) {
        if(!form.Visible)form.Show();
        if(form.WindowState==FormWindowState.Minimized)form.WindowState=FormWindowState.Normal;
        // Shell desktop/minimize commands can hide the HWND while WinForms
        // still reports Visible=true. Restore actual native visibility too.
        if(IsIconic(form.Handle) || !IsWindowVisible(form.Handle))ShowWindow(form.Handle,4); // SW_SHOWNOACTIVATE
    }
    internal static Form BuildDiagnostics(Icon logo,string summary,Action<Form> exportAction) {
        var form=NewWindow("状态与诊断",logo,new Size(650,440));
        var content=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=2};
        content.RowStyles.Add(new RowStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        var text=new TextBox{Name="diagnosticsText",Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,
            BackColor=Color.White,BorderStyle=BorderStyle.FixedSingle,Text=summary};
        var export=Button("导出诊断…","exportDiagnostics",false);export.Anchor=AnchorStyles.Right;export.Width=108;
        export.Click+=(s,e)=>exportAction(form);
        content.Controls.Add(text,0,0);content.Controls.Add(export,0,1);form.Controls.Add(content);return form;
    }
    static Form NewWindow(string title,Icon logo,Size size) {
        return new Form{Text="小米立体声 · "+title,Icon=logo,Font=SystemFonts.MessageBoxFont,ForeColor=Ink,
            BackColor=Background,ClientSize=size,AutoScaleMode=AutoScaleMode.Dpi,AutoScaleDimensions=new SizeF(96,96),
            FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,StartPosition=FormStartPosition.CenterParent};
    }
    static void FontSize(Control control,float size,FontStyle style) {
        var font=new Font(SystemFonts.MessageBoxFont.FontFamily,size,style);
        control.Font=font;control.Disposed+=(s,e)=>font.Dispose();
    }
    static Label Label(string text,string name,Color color) {
        return new Label{Text=text,Name=name,ForeColor=color,AutoEllipsis=true,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,Margin=new Padding(0)};
    }
    static Button Button(string text,string name,bool primary) {
        var button=new Button{Text=text,Name=name,FlatStyle=FlatStyle.Flat,Height=34,AutoSize=false,
            BackColor=primary?Orange:Color.White,ForeColor=primary?Color.White:Ink,Cursor=Cursors.Hand,Margin=new Padding(0)};
        button.FlatAppearance.BorderSize=primary?0:1;button.FlatAppearance.BorderColor=Line;
        button.FlatAppearance.MouseOverBackColor=primary?Color.FromArgb(232,92,0):Color.FromArgb(247,248,250);
        return button;
    }
    static Control Control(Form form,string name){return form.Controls.Find(name,true)[0];}
    static string SpeakerName(Coordinator.View view,bool left) {
        if(view.Settings==null)return "等待设备配置";
        string id=left?view.Settings.LeftId:view.Settings.RightId;
        foreach(var item in view.Choices)if(DeviceSelector.Same(item.Id,id)) {
            string name=item.Name??"扬声器";
            int pro=name.IndexOf("Pro-",StringComparison.OrdinalIgnoreCase);
            return pro>=0 && name.Length>=pro+8?name.Substring(pro,8):BoundedDiagnostics.Limit(name,30);
        }
        if(view.ChoiceError!="")return "设备名称暂不可用";
        return view.ChoiceSerial==0?"正在读取设备名称…":"设备信息待刷新";
    }
    internal static void RefreshControlPanel(Form form,Coordinator.View view) {
        string status=view.State;
        bool fault=status=="需要处理" || status=="故障";
        bool running=status=="正在转发" || status=="等待输入" || status=="静音包" || status=="已连接";
        if(status=="正在转发")status="立体声正在播放";
        else if(status=="等待输入")status="立体声已开启，等待声音";
        else if(status=="静音包")status="立体声已开启，当前无声音";
        var state=Control(form,"controlState");state.Text=status;
        state.ForeColor=fault?Color.FromArgb(180,45,45):running?Color.FromArgb(30,116,76):Muted;
        string detail=view.Detail;
        if(String.IsNullOrEmpty(detail) && view.ChoiceError!="")detail="设备列表暂不可用。"+view.ChoiceError;
        if(String.IsNullOrEmpty(detail))detail=view.Enabled?"暂停时恢复普通扬声器；正常使用保持默认即可。":"声音使用普通输出。点击启用即可恢复双音箱立体声。";
        if(view.RetrySeconds>0)detail+=" 约 "+view.RetrySeconds+" 秒后重试。";
        Control(form,"controlDetail").Text=detail;
        var toggle=(Button)Control(form,"toggleStereo");toggle.Text=fault?"重新连接":view.Enabled?"暂停立体声":"启用立体声";
        toggle.Tag=fault || !view.Enabled;
        Control(form,"pauseStereo").Visible=fault && view.Enabled;
        Control(form,"leftSpeaker").Text=SpeakerName(view,true);Control(form,"rightSpeaker").Text=SpeakerName(view,false);
        Control(form,"resync").Enabled=running && view.Enabled;
        var swap=(CheckBox)Control(form,"swapChannels");var balance=(TrackBar)Control(form,"balance");
        swap.Enabled=balance.Enabled=view.Settings!=null;Control(form,"restoreBalance").Enabled=view.Settings!=null;
        form.Tag=true;
        try {
            if(view.Settings!=null) {
                swap.Checked=view.Settings.SwapChannels;
                if(!balance.Capture && !balance.Focused)balance.Value=(int)Math.Round(view.Settings.BalanceDb*2);
            }
            swap.Text=swap.Checked?"左右声道已交换":"交换左右声道";
        } finally {form.Tag=false;}
    }
    internal static Form BuildControlPanel(Coordinator.View view,Icon logo,PanelActions actions) {
        var form=NewWindow("控制面板",logo,new Size(680,650));form.StartPosition=FormStartPosition.CenterScreen;form.Tag=true;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,16,24,16),ColumnCount=1,RowCount=6};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{64,112,194,114,76})root.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=new Padding(0,0,0,8)};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,60));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute,32));header.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var picture=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.CenterImage,Margin=new Padding(0,0,12,0)};
        if(logo!=null){picture.Image=logo.ToBitmap();picture.Disposed+=(s,e)=>picture.Image.Dispose();}
        header.Controls.Add(picture,0,0);header.SetRowSpan(picture,2);
        var title=Label("小米立体声",null,Ink);FontSize(title,18,FontStyle.Bold);
        header.Controls.Add(title,1,0);header.Controls.Add(Label("双 USB 音箱控制面板",null,Muted),1,1);root.Controls.Add(header,0,0);

        var status=new Card();var statusLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=new Padding(0)};
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,136));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var state=Label("正在读取状态","controlState",Ink);FontSize(state,11,FontStyle.Bold);statusLayout.Controls.Add(state,0,0);
        var detail=Label("","controlDetail",Muted);detail.TextAlign=ContentAlignment.TopLeft;detail.Margin=new Padding(0,4,14,0);statusLayout.Controls.Add(detail,0,1);
        var actionsPanel=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=new Padding(0)};
        actionsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,36));actionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var toggle=Button("启用立体声","toggleStereo",true);toggle.Dock=DockStyle.Fill;
        toggle.Click+=(s,e)=>actions.Enable((bool)toggle.Tag);actionsPanel.Controls.Add(toggle,0,0);
        var pause=Button("暂停立体声","pauseStereo",false);pause.Dock=DockStyle.Fill;pause.Margin=new Padding(0,6,0,0);
        pause.Click+=(s,e)=>actions.Enable(false);actionsPanel.Controls.Add(pause,0,1);
        statusLayout.Controls.Add(actionsPanel,1,0);statusLayout.SetRowSpan(actionsPanel,2);status.Controls.Add(statusLayout);root.Controls.Add(status,0,1);

        var speakers=new Card();var sound=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,Margin=new Padding(0)};
        sound.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));sound.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        sound.RowStyles.Add(new RowStyle(SizeType.Absolute,54));sound.RowStyles.Add(new RowStyle(SizeType.Absolute,32));sound.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        for(int side=0;side<2;side++) {
            var block=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(side==0?0:16,0,0,0)};
            block.RowStyles.Add(new RowStyle(SizeType.Absolute,20));block.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            block.Controls.Add(Label(side==0?"左侧音箱":"右侧音箱",null,Muted),0,0);
            var name=Label("",side==0?"leftSpeaker":"rightSpeaker",Ink);FontSize(name,13,FontStyle.Bold);block.Controls.Add(name,0,1);sound.Controls.Add(block,side,0);
        }
        sound.Controls.Add(Label("音量平衡",null,Ink),0,1);
        var swap=new CheckBox{Name="swapChannels",Appearance=Appearance.Button,FlatStyle=FlatStyle.Flat,TextAlign=ContentAlignment.MiddleCenter,
            Anchor=AnchorStyles.Right,Width=136,Height=28,Margin=new Padding(0),Cursor=Cursors.Hand};
        swap.FlatAppearance.BorderColor=Line;swap.FlatAppearance.CheckedBackColor=Color.FromArgb(255,240,228);
        swap.CheckedChanged+=(s,e)=>{if(!(bool)form.Tag){swap.Text=swap.Checked?"左右声道已交换":"交换左右声道";actions.Mix();}};
        sound.Controls.Add(swap,1,1);
        var balanceRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0)};
        balanceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));balanceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
        var scale=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=2,Margin=new Padding(0,3,0,0)};
        for(int col=0;col<3;col++)scale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));
        scale.RowStyles.Add(new RowStyle(SizeType.Absolute,20));scale.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        scale.Controls.Add(Label("偏左",null,Muted),0,0);
        var centerLabel=Label("居中",null,Muted);centerLabel.TextAlign=ContentAlignment.MiddleCenter;scale.Controls.Add(centerLabel,1,0);
        var rightLabel=Label("偏右",null,Muted);rightLabel.TextAlign=ContentAlignment.MiddleRight;scale.Controls.Add(rightLabel,2,0);
        var balance=new TrackBar{Name="balance",Minimum=-48,Maximum=48,SmallChange=1,LargeChange=6,TickStyle=TickStyle.None,AutoSize=false,Dock=DockStyle.Fill,Margin=new Padding(0)};
        balance.ValueChanged+=(s,e)=>{if(!(bool)form.Tag)actions.Mix();};
        scale.Controls.Add(balance,0,1);scale.SetColumnSpan(balance,3);balanceRow.Controls.Add(scale,0,0);
        var center=Button("恢复居中","restoreBalance",false);center.Anchor=AnchorStyles.Right;center.Width=90;center.Margin=new Padding(10,10,0,0);
        center.Click+=(s,e)=>balance.Value=0;balanceRow.Controls.Add(center,1,0);sound.Controls.Add(balanceRow,0,2);sound.SetColumnSpan(balanceRow,2);
        speakers.Controls.Add(sound);root.Controls.Add(speakers,0,2);

        var syncCard=new Card();var sync=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=new Padding(0)};
        sync.RowStyles.Add(new RowStyle(SizeType.Absolute,22));sync.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var syncTitle=Label("音量自动双向同步",null,Ink);FontSize(syncTitle,10,FontStyle.Bold);sync.Controls.Add(syncTitle,0,0);
        sync.Controls.Add(Label("立体声运行时，调 Windows → 两台音箱。\r\n调任意音箱的小米 App 或机身按键 → Windows 和另一台音箱。","volumeSyncHelp",Muted),0,1);
        syncCard.Controls.Add(sync);root.Controls.Add(syncCard,0,3);

        var tools=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=new Padding(0,4,0,14)};
        for(int col=0;col<4;col++)tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
        var labels=new[]{"设备设置","音量校准","重新同步","状态与诊断"};
        var names=new[]{"deviceSettings","viewCalibration","resync","diagnostics"};
        var callbacks=new[]{actions.Devices,actions.Calibration,actions.Resync,actions.Diagnostics};
        for(int i=0;i<labels.Length;i++){
            var callback=callbacks[i];var button=Button(labels[i],names[i],false);
            button.Dock=DockStyle.Fill;button.Margin=new Padding(i==0?0:8,6,0,6);button.Click+=(s,e)=>callback();tools.Controls.Add(button,i,0);
        }
        root.Controls.Add(tools,0,4);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0)};
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,98));
        footer.Controls.Add(Label("自动采用稳定模式，无需调整。\r\n电脑播放时，请保持米家中的立体声／全屋组合关闭。",null,Muted),0,0);
        var version=Label("版本 "+typeof(StereoTray).Assembly.GetName().Version.ToString(3),"version",Muted);version.TextAlign=ContentAlignment.MiddleRight;footer.Controls.Add(version,1,0);
        root.Controls.Add(footer,0,5);form.Controls.Add(root);RefreshControlPanel(form,view);return form;
    }
    sealed class DeviceItem {
        internal readonly string Id,Container;
        readonly string label;
        internal DeviceItem(string id,string container,string label){Id=id;Container=container;this.label=label;}
        public override string ToString(){return label;}
    }
    static ComboBox Selector(EndpointChoice[] devices,string current,string container) {
        var combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,DropDownWidth=480};
        combo.Items.Add(new DeviceItem(null,null,"未指定"));combo.SelectedIndex=0;
        var names=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in devices){string name=item.Name??"扬声器";int count;names.TryGetValue(name,out count);names[name]=count+1;}
        var labels=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var item in devices) {
            string name=item.Name??"扬声器";
            string label=names[name]>1?name+" · "+ShortId(item.Id):name;
            if(!labels.Add(label))label+=" ("+combo.Items.Count+")";
            int index=combo.Items.Add(new DeviceItem(item.Id,item.Container,label));
            if(DeviceSelector.Same(item.Id,current))combo.SelectedIndex=index;
        }
        if(current!=null && combo.SelectedIndex==0)
            combo.SelectedIndex=combo.Items.Add(new DeviceItem(current,container,"已保存的设备（离线 · "+ShortId(current)+"）"));
        return combo;
    }
    static string ShortId(string id) {string value=(id??"").TrimEnd('}');return value.Length>8?value.Substring(value.Length-8):value;}
    internal static Form BuildDeviceSettings(AppSettings cfg,EndpointChoice[] choices,Icon logo,Action<AppSettings> save) {
        var form=NewWindow("设备设置",logo,new Size(620,390));
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=2,RowCount=8};
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        for(int row=0;row<4;row++)grid.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute,72));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,36));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var intro=Label("设备已选好时，保持当前设置即可。",null,Muted);grid.Controls.Add(intro,0,0);grid.SetColumnSpan(intro,2);
        var source=Selector(choices,cfg.SourceId,cfg.SourceContainer);source.Name="sourceDevice";
        var left=Selector(choices,cfg.LeftId,cfg.LeftContainer);left.Name="leftDevice";
        var right=Selector(choices,cfg.RightId,cfg.RightContainer);right.Name="rightDevice";
        var normal=Selector(choices,cfg.FallbackId,null);normal.Name="fallbackDevice";
        var boxes=new[]{source,left,right,normal};var labels=new[]{"电脑音频入口","左侧音箱","右侧音箱","备用普通扬声器"};
        for(int i=0;i<4;i++){grid.Controls.Add(Label(labels[i],null,Ink),0,i+1);boxes[i].Margin=new Padding(0,6,0,0);grid.Controls.Add(boxes[i],1,i+1);}
        grid.Controls.Add(Label("暂停或退出时，优先恢复启用前的扬声器；原设备不可用时使用此备用扬声器。普通模式只通过一台设备播放。",null,Muted),1,5);
        var manage=new CheckBox{Name="manageDefaults",Text="启用时自动切换 Windows 到立体声",Checked=cfg.ManageDefaults,AutoSize=true,Anchor=AnchorStyles.Left};
        grid.Controls.Add(manage,1,6);
        var saveButton=Button("保存设置","saveDevices",true);saveButton.Width=108;saveButton.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;grid.Controls.Add(saveButton,1,7);
        saveButton.Click+=(s,e)=>{
            try {
                var src=(DeviceItem)source.SelectedItem;var l=(DeviceItem)left.SelectedItem;var r=(DeviceItem)right.SelectedItem;var fallback=(DeviceItem)normal.SelectedItem;
                if(src.Id==null || l.Id==null || r.Id==null)throw new InvalidOperationException("请选择电脑音频入口、左箱和右箱。");
                cfg.SourceId=src.Id;cfg.SourceContainer=src.Container;cfg.LeftId=l.Id;cfg.LeftContainer=l.Container;
                cfg.RightId=r.Id;cfg.RightContainer=r.Container;cfg.FallbackId=fallback.Id;cfg.ManageDefaults=manage.Checked;
                cfg.Validate();save(cfg);form.Close();
            } catch(Exception error){MessageBox.Show(form,error.Message,"检查设备设置");}
        };
        form.Controls.Add(grid);form.AcceptButton=saveButton;return form;
    }
    internal static Form BuildCalibration(Icon logo) {
        var form=NewWindow("音量校准（只读）",logo,new Size(560,682));
        form.MinimumSize=new Size(480,500);form.FormBorderStyle=FormBorderStyle.Sizable;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=4};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,104));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        layout.Controls.Add(new Label{Text="Windows 档位对应程序写入音箱的 USB 校准值。\r\ndB 为内部换算单位，正常使用无需调整。",Dock=DockStyle.Fill},0,0);
        var table=new DataGridView{Name="calibrationTable",Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,
            AllowUserToDeleteRows=false,AllowUserToResizeRows=false,AllowUserToResizeColumns=false,
            RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=SystemColors.Window,
            BorderStyle=BorderStyle.FixedSingle,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight=26,ScrollBars=ScrollBars.Vertical};
        table.RowTemplate.Height=20;
        table.Columns.Add(new DataGridViewTextBoxColumn{Name="windowsLevel",HeaderText="Windows 档位",SortMode=DataGridViewColumnSortMode.NotSortable});
        table.Columns.Add(new DataGridViewTextBoxColumn{Name="usbDecibels",HeaderText="USB 校准值 (dB)",SortMode=DataGridViewColumnSortMode.NotSortable});
        for(int percent=0;percent<=100;percent+=5)
            table.Rows.Add(percent.ToString(CultureInfo.InvariantCulture)+"%",VolumeLink.SpeakerDecibels(percent/100f).ToString("0.##",CultureInfo.InvariantCulture));
        table.ClearSelection();layout.Controls.Add(table,0,1);
        layout.Controls.Add(new Label{Name="calibrationHelp",Text="44% 保留机身按键的特殊映射，USB 写入 −29 dB。\r\nWindows 75% 可能在音箱上显示 74%，受硬件档位限制。\r\n\r\n程序无法直接读取音箱 App 的实际百分比，请在 App 中查看。",
            Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0)},0,2);
        var close=Button("关闭",null,false);close.Width=86;close.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;close.DialogResult=DialogResult.OK;
        layout.Controls.Add(close,0,3);form.Controls.Add(layout);form.AcceptButton=close;form.CancelButton=close;return form;
    }
    public void Dispose() {
        timer.Stop();timer.Dispose();
        if(controlWindow!=null)controlWindow.Close();
        if(deviceWindow!=null)deviceWindow.Close();
        if(statusWindow!=null)statusWindow.Close();
        icon.Visible=false;icon.Dispose();menu.Dispose();logo.Dispose();artwork.Dispose();
    }
}
