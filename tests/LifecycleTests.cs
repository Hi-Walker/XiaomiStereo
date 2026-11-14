using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

// Drives the production coordinator and state store. The backend never opens Windows audio.
internal static class LifecycleTests {
    sealed class FakeAudio : ILiveBridge {
        readonly FakeHost host;
        bool closed;
        internal volatile bool fail;
        internal FakeAudio(FakeHost host){this.host=host;}
        public string State {get{return "测试音频流";}}
        public string Summary {get{return fail?"FakeError 0x88890004 测试故障":"ok";}}
        public bool Failed {get{return fail;}}
        public int ErrorCode {get{return unchecked((int)0x88890004);}}
        public string ErrorType {get{return "SimulatedAudioException";}}
        public void Tick(){
            host.CheckOwner();
            if(host.HoldTick){
                host.TickEntered.Set();
                if(!host.TickContinue.WaitOne(4000))throw new TimeoutException();
                host.HoldTick=false;
            }
        }
        public void SetMix(bool swap,double balance){host.CheckOwner();host.MixChanges++;}
        public void Resync(){host.CheckOwner();host.ResyncCount++;}
        public void Dispose(){host.CheckOwner();if(!closed){host.Closed++;closed=true;}}
    }
    sealed class FakeHost : IControlHost {
        internal volatile IMMNotificationClient Notice;
        internal volatile FakeAudio Audio;
        internal volatile bool External, Missing, HoldOpen, HoldTick, MissingFallback;
        internal volatile int Opened,Closed,Takes,Registrations,Unregistrations,Attempts,Lists,MixChanges,ResyncCount,Recovers;
        internal int BadOwner,Overlaps,Disposed;
        internal readonly ManualResetEvent Opening=new ManualResetEvent(false),Continue=new ManualResetEvent(false);
        internal readonly ManualResetEvent TickEntered=new ManualResetEvent(false),TickContinue=new ManualResetEvent(false);
        int thread;
        bool bound;
        public void CheckOwner(){
            int tid=Thread.CurrentThread.ManagedThreadId;
            int old=Interlocked.CompareExchange(ref thread,tid,0);
            if(old!=0 && old!=tid)Interlocked.Increment(ref BadOwner);
        }
        public void Register(IMMNotificationClient n){CheckOwner();Notice=n;Registrations++;}
        public void Unregister(IMMNotificationClient n){CheckOwner();if(n!=Notice)throw new Exception("unmatched observer");Notice=null;Unregistrations++;}
        public bool RecoverLegacy(){CheckOwner();Recovers++;return false;}
        public void SnapshotIntent(){CheckOwner();}
        public void Take(string source,string fallback){CheckOwner();Takes++;}
        public void Restore(){CheckOwner();if(MissingFallback)throw new DeviceUnavailableException("waiting for ordinary output");}
        public bool ExternalChange(){CheckOwner();return External;}
        public void ForgetIntent(){CheckOwner();External=false;}
        public bool Bind(AppSettings cfg){
            CheckOwner();Attempts++;
            if(Missing)throw new DeviceUnavailableException("missing");
            if(bound || Opened!=Closed)Overlaps++;
            bound=true;
            bool changed=cfg.SourceId==null;
            if(changed){cfg.SourceId="source";cfg.LeftId="1111";cfg.RightId="2222";cfg.FallbackId="1111";}
            return changed;
        }
        public ILiveBridge OpenBridge(AppSettings cfg){
            CheckOwner();
            if(HoldOpen){Opening.Set();if(!Continue.WaitOne(4000))throw new TimeoutException();HoldOpen=false;}
            Audio=new FakeAudio(this);Opened++;return Audio;
        }
        public void Unbind(){CheckOwner();bound=false;}
        public bool DevicesActive(){CheckOwner();return !Missing;}
        public EndpointChoice[] List(){CheckOwner();Lists++;return new[]{new EndpointChoice("source","fake",null,null)};}
        public void Dispose(){CheckOwner();Disposed++;Opening.Dispose();Continue.Dispose();TickEntered.Dispose();TickContinue.Dispose();}
    }
    static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
    static void Await(Func<bool> test,string reason){
        var timer=Stopwatch.StartNew();
        while(!test()){if(timer.ElapsedMilliseconds>6000)throw new Exception(reason);Thread.Sleep(8);}
    }
    static void TestCoordinator(string directory) {
        var host=new FakeHost();
        var store=new ConfigStore(directory);
        using(var c=new Coordinator(directory,unused=>host)){
            Await(()=>c.Current.Generation==1,"initial connection");
            var first=host.Audio;
            Check(host.Takes==1,"default takeover");
            var savedAt=File.GetLastWriteTimeUtc(Path.Combine(directory,"settings.json"));
            for(int i=0;i<20000;i++)host.Notice.OnDeviceAdded("irrelevant");
            c.Configure(c.Current.Settings);c.RequestChoices();
            Await(()=>c.Current.ChoiceSerial==1,"choices");
            Check(File.GetLastWriteTimeUtc(Path.Combine(directory,"settings.json"))==savedAt,"no-op writes settings");
            Check(host.Opened==1 && host.Lists==1 && host.Attempts==1,"healthy storm recreated endpoints");
            var mix=c.Current.Settings.Copy();mix.SwapChannels=true;mix.BalanceDb=3;c.Configure(mix);c.Resync();
            Await(()=>host.MixChanges==1 && host.ResyncCount==1,"mix/resync");
            Check(host.Opened==1,"mix reopened streams");
            host.External=true;host.Notice.OnDefaultDeviceChanged(DataFlow.Render,Role.Multimedia,"other");
            Await(()=>c.Current.State=="已暂停","external default takeover");
            Check(!store.Load().Enabled && host.Closed==1,"pause was not durable");
            for(int i=0;i<200;i++)host.Notice.OnDeviceAdded("other");
            Thread.Sleep(160);
            Check(host.Opened==1,"reopened while paused");
            c.Enable(true);Await(()=>c.Current.Generation==2,"explicit resume");
            first.fail=true;
            Thread.Sleep(220);
            Check(c.Current.Generation==2 && host.Closed==1,"stale bridge affected new generation");
            host.Audio.fail=true;host.Missing=true;
            Await(()=>c.Current.State=="等待设备","fault state");
            Check(c.Current.Detail.Contains("0x88890004"),"fault identity in interactive detail");
            Await(()=>host.Attempts>=3,"retry");
            host.Missing=false;host.Notice.OnDeviceAdded("2222");
            Await(()=>c.Current.Generation==3,"hotplug resume");
            c.SleepChanged(true);Await(()=>c.Current.State=="等待系统唤醒","suspend");
            c.SleepChanged(false);Await(()=>c.Current.Generation==4,"resume from sleep");
            for(int i=0;i<30;i++){
                c.Enable(false);Await(()=>c.Current.State=="已暂停","cycle pause");
                c.Enable(true);long expected=5+i;Await(()=>c.Current.Generation==expected,"cycle resume");
            }
            int takes=host.Takes;
            host.MissingFallback=true;
            c.Enable(false);Await(()=>c.Current.State=="等待恢复普通输出","pending restore on pause");
            Check(!store.Load().Enabled,"lost durable pause");
            host.MissingFallback=false;host.Notice.OnDeviceAdded("ordinary");
            Await(()=>c.Current.State=="已暂停","pending restore on arrival");
            Check(host.Takes==takes && host.Closed==34,"pending restore restarted audio");
            c.Enable(false);Await(()=>c.Current.State=="已暂停","last pause");
            int takesBefore=host.Takes;
            host.HoldOpen=true;c.Enable(true);
            Check(host.Opening.WaitOne(3000),"startup barrier");
            c.Enable(false);host.Continue.Set();
            Await(()=>host.Opened==host.Closed && !store.Load().Enabled,"pause during flow initialization");
            Check(takesBefore==host.Takes,"took defaults after cancellation");
            Check(host.Opened==35,"unexpected flow count");
        }
        Check(host.BadOwner==0 && host.Overlaps==0 && host.Registrations==1 &&
            host.Unregistrations==1 && host.Disposed==1,"resource ownership");
        Check(new DirectoryInfo(directory).GetFiles().Length==1,"unexpected automatic files");
        Check(Coordinator.DelayMs(1)==2000 && Coordinator.DelayMs(2)==5000 &&
            Coordinator.DelayMs(3)==10000 && Coordinator.DelayMs(int.MaxValue)==30000,"backoff");
        Console.WriteLine("PASS lifecycle: 35 opens/closes; notification storm, mix and resync, external default, stale callback, fault/hotplug retry, sleep/resume, 30 pause/resume cycles, deferred restore while paused, cancellation before takeover");
    }
    static void TestDiagnostics(string directory) {
        var d=new BoundedDiagnostics();
        string path=Path.Combine(directory,"manual.txt");
        for(int i=0;i<10000;i++)d.Note(1,i.ToString("D5")+new string('甲',300),0);
        for(int i=0;i<999;i++)d.Note(1,"09999"+new string('甲',300),0);
        d.Export(path,new string('乙',1000000));
        string content=File.ReadAllText(path,Encoding.UTF8);
        Check(d.Counts.Contains("512 / 512") && d.Counts.Contains("9488"),"ring bounds");
        Check(content.Contains("×1000") && content.Contains("09999") &&
            !content.Contains("09487") && content.Contains("09488"),"ring order/dedup");
        Check(!content.Contains(new string('甲',160)) && !content.Contains(new string('乙',8193)),"text bound");
        Check(new FileInfo(path).Length<=BoundedDiagnostics.ExportLimit,"export bound");
        bool failed=false;
        try{d.Export(Path.Combine(directory,"missing","export.txt"),"");}catch(DirectoryNotFoundException){failed=true;}
        Check(failed && BoundedDiagnostics.Increment(long.MaxValue)==long.MaxValue,"export errors and saturation");
        Console.WriteLine("PASS diagnostics: 10,999 updates, 512 records retained, 9,488 overwritten, 1000 equal errors coalesced; export "+new FileInfo(path).Length+" bytes; character and UTF-8 byte caps verified");
    }
    static void TestInvalidConfig(string directory){
        string file=Path.Combine(directory,"settings.json");
        File.WriteAllText(file,"{broken");
        var host=new FakeHost();
        using(var c=new Coordinator(directory,unused=>host)){
            Await(()=>c.Current.State=="需要处理","corrupt settings blocked");
            Check(host.Recovers==1 && host.Opened==0 && host.Takes==0,"did not inspect journal independently of settings");
            Check(File.ReadAllText(file)=="{broken","overwrote corrupt settings");
            new ConfigStore(directory).Save(new AppSettings{ManageDefaults=false});
            c.Enable(true);Await(()=>c.Current.Generation==1,"reload repaired settings");
            Check(host.Takes==0,"manual mode touched defaults");
        }
        Console.WriteLine("PASS corrupt settings preserved, prior recovery inspected, repair/explicit retry and manual mode");
    }
    static void Click(Control control) {
        typeof(Button).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
            .Invoke(control,new object[]{EventArgs.Empty});
    }
    static void TestSettingsForm() {
        const string leftId="{0.0.0.00000000}.{00000000-0000-0000-0000-000011110001}";
        const string rightId="{0.0.0.00000000}.{00000000-0000-0000-0000-000022220002}";
        var choices=new[]{new EndpointChoice("source","虚拟源",null,null),new EndpointChoice(leftId,"同名",null,null),
            new EndpointChoice(rightId,"同名",null,null),new EndpointChoice("normal","普通",null,null)};
        var cfg=new AppSettings{SourceId="source",LeftId=leftId,RightId=rightId,FallbackId="offline",BalanceDb=2.5,SwapChannels=true};
        AppSettings saved=null;
        using(var form=StereoTray.BuildDeviceSettings(cfg.Copy(),choices,null,edit=>saved=edit)) {
            var left=(ComboBox)form.Controls.Find("leftDevice",true)[0];
            var right=(ComboBox)form.Controls.Find("rightDevice",true)[0];
            var fallback=(ComboBox)form.Controls.Find("fallbackDevice",true)[0];
            Check(left.SelectedIndex==2 && right.SelectedIndex==3 && fallback.SelectedItem.ToString().Contains("offline"),"device selector binding");
            Check(!left.SelectedItem.ToString().Contains(leftId) && !right.SelectedItem.ToString().Contains(rightId) &&
                left.SelectedItem.ToString()!=right.SelectedItem.ToString(),"short unique display names");
            Check(left.Items[1].ToString()=="虚拟源","ordinary names contain device identifiers");
            left.SelectedIndex=3;right.SelectedIndex=2;fallback.SelectedIndex=4;
            ((CheckBox)form.Controls.Find("manageDefaults",true)[0]).Checked=false;
            Click((Control)form.AcceptButton);
        }
        Check(saved!=null && saved.LeftId==rightId && saved.RightId==leftId && saved.FallbackId=="normal" &&
            !saved.ManageDefaults && saved.BalanceDb==2.5 && saved.SwapChannels && cfg.LeftId==leftId,"device save changed other settings");
        bool? enabled=null;int mixes=0,devices=0,calibrations=0,resyncs=0,diagnostics=0;
        var actions=new StereoTray.PanelActions {
            Enable=on=>enabled=on,Mix=()=>mixes++,Devices=()=>devices++,Calibration=()=>calibrations++,Resync=()=>resyncs++,Diagnostics=()=>diagnostics++
        };
        var view=new Coordinator.View {Settings=cfg,Enabled=true,State="正在转发",ChoiceSerial=1,
            Choices=new[]{new EndpointChoice(leftId,"扬声器（智能音箱 Pro-LEFT）",null,null),new EndpointChoice(rightId,"扬声器（智能音箱 Pro-RIGHT）",null,null)}};
        using(var panel=StereoTray.BuildControlPanel(view,null,actions)) {
            Check(mixes==0,"snapshot triggered mix write");
            Check(panel.Controls.Find("leftSpeaker",true)[0].Text=="Pro-LEFT" && panel.Controls.Find("rightSpeaker",true)[0].Text=="Pro-RIGH","speaker summary");
            string sync=panel.Controls.Find("volumeSyncHelp",true)[0].Text;
            Check(sync.Contains("Windows → 两台音箱") && sync.Contains("任意音箱") && sync.Contains("Windows 和另一台"),"bidirectional explanation");
            var balance=(TrackBar)panel.Controls.Find("balance",true)[0];balance.Value=5;
            Click(panel.Controls.Find("restoreBalance",true)[0]);
            Check(balance.Value==0 && mixes==1,"center balance callback");
            balance.Value=-5;
            var swap=(CheckBox)panel.Controls.Find("swapChannels",true)[0];swap.Checked=false;
            Check(mixes==3,"mix callbacks");
            Click(panel.Controls.Find("toggleStereo",true)[0]);Check(enabled==false,"pause action");
            foreach(string name in new[]{"deviceSettings","viewCalibration","resync","diagnostics"})Click(panel.Controls.Find(name,true)[0]);
            Check(devices==1 && calibrations==1 && resyncs==1 && diagnostics==1,"panel actions unreachable");
            int before=mixes;
            StereoTray.RefreshControlPanel(panel,new Coordinator.View{Enabled=false,State="已暂停",Settings=cfg});
            Check(mixes==before && !panel.Controls.Find("resync",true)[0].Enabled,"pause snapshot");
            Click(panel.Controls.Find("toggleStereo",true)[0]);Check(enabled==true,"enable action");
            StereoTray.RefreshControlPanel(panel,new Coordinator.View{Enabled=true,State="需要处理",Detail="设置文件损坏",ChoiceError="枚举失败"});
            Check(panel.Controls.Find("controlDetail",true)[0].Text.Contains("设置文件损坏"),"fault not visible");
            Check(!swap.Enabled && !balance.Enabled && panel.Controls.Find("diagnostics",true)[0].Enabled,"fault controls");
            enabled=null;Click(panel.Controls.Find("toggleStereo",true)[0]);Check(enabled==true,"retry action");
            Click(panel.Controls.Find("pauseStereo",true)[0]);Check(enabled==false,"fault pause action");
            Click(panel.Controls.Find("diagnostics",true)[0]);Check(diagnostics==2,"fault diagnostics inaccessible");
            StereoTray.RefreshControlPanel(panel,new Coordinator.View{Enabled=true,State="正在恢复",RetrySeconds=5,Settings=cfg,ChoiceError="枚举失败"});
            Check(panel.Controls.Find("controlDetail",true)[0].Text.Contains("5 秒") && panel.Controls.Find("controlDetail",true)[0].Text.Contains("枚举失败"),"retry and enumeration error");
        }
        Console.WriteLine("PASS modern control panel and device dialog: all actions, pause/retry/errors without config or enumeration, mix controls, snapshot without writes, short device names, isolated device save");
    }
    static void TestPendingMixSave(string directory) {
        var store=new ConfigStore(directory);store.Save(new AppSettings());
        var host=new FakeHost();
        using(var c=new Coordinator(directory,unused=>host)) {
            Await(()=>c.Current.Generation==1,"pending mix initial connection");
            Form panel=null;
            var actions=new StereoTray.PanelActions {
                Enable=unused=>{},Devices=()=>{},Calibration=()=>{},Resync=()=>{},Diagnostics=()=>{},
                Mix=()=>{
                    var edit=c.Current.Settings.Copy();
                    edit.SwapChannels=((CheckBox)panel.Controls.Find("swapChannels",true)[0]).Checked;
                    edit.BalanceDb=((TrackBar)panel.Controls.Find("balance",true)[0]).Value/2.0;
                    c.Configure(edit);
                }
            };
            var choices=new[]{new EndpointChoice("source","source",null,null),new EndpointChoice("1111","left",null,null),
                new EndpointChoice("2222","right",null,null),new EndpointChoice("ordinary","ordinary",null,null)};
            using(panel=StereoTray.BuildControlPanel(c.Current,null,actions))
            using(var devices=StereoTray.BuildDeviceSettings(c.Current.Settings.Copy(),choices,null,edit=>{
                StereoTray.MergeDeviceMix(panel,edit,c.Current.Settings);c.Configure(edit);
            })) {
                host.HoldTick=true;
                Check(host.TickEntered.WaitOne(3000),"pending mix tick barrier");
                try {
                    ((TrackBar)panel.Controls.Find("balance",true)[0]).Value=5;
                    ((CheckBox)panel.Controls.Find("swapChannels",true)[0]).Checked=true;
                    Check(c.Current.Settings.BalanceDb==0 && !c.Current.Settings.SwapChannels,"snapshot was not held stale");
                    ((ComboBox)devices.Controls.Find("fallbackDevice",true)[0]).SelectedIndex=4;
                    Click((Control)devices.AcceptButton);
                } finally {host.TickContinue.Set();}
                Await(()=>c.Current.Settings.FallbackId=="ordinary","pending device edit not saved");
                var saved=store.Load();
                Check(saved.BalanceDb==2.5 && saved.SwapChannels && c.Current.Settings.BalanceDb==2.5 && c.Current.Settings.SwapChannels,
                    "device save overwrote pending balance and swap with stale snapshot");
            }
        }
        store.Save(new AppSettings());
        Console.WriteLine("PASS pending UI edits: tick barrier holds stale snapshot; device save preserves pending balance and swap");
    }
    static void TestCalibrationForm() {
        float[] anchors={-100,-83,-65,-58,-50,-45,-40,-36,-32,-29,-25,-22,-19,-17,-14,-12,-9,-7,-5,-3,0};
        using(var form=StereoTray.BuildCalibration(null)) {
            var table=(DataGridView)form.Controls.Find("calibrationTable",true)[0];
            Check(table.ReadOnly && !table.AllowUserToAddRows && !table.AllowUserToDeleteRows && table.Rows.Count==21,"calibration table editable or incomplete");
            Check(table.Columns.Count==2 && table.Columns[1].HeaderText=="USB 校准值 (dB)","calibration units");
            for(int i=0;i<anchors.Length;i++) {
                Check((string)table.Rows[i].Cells[0].Value==(i*5).ToString()+"%","wrong calibration percentage");
                Check((string)table.Rows[i].Cells[1].Value==anchors[i].ToString("0.##",System.Globalization.CultureInfo.InvariantCulture),"wrong calibration anchor");
            }
            var note=form.Controls.Find("calibrationHelp",true)[0].Text;
            Check(note.Contains("44%") && note.Contains("75%") && note.Contains("74%") && note.Contains("无法直接读取"),"calibration limitations missing");
            Check(form.AcceptButton==form.CancelButton,"calibration close controls");
        }
        Console.WriteLine("PASS read-only calibration form: 21 existing anchors, correct internal dB labels, panel-step notes, close controls");
    }
    static void TestUnavailableSystem(string directory) {
        new ConfigStore(directory).Save(new AppSettings());
        using(var entered=new ManualResetEvent(false))
        using(var release=new ManualResetEvent(false)) {
            int attempts=0,available=0;
            var host=new FakeHost();
            using(var c=new Coordinator(directory,unused=>{
                int attempt=Interlocked.Increment(ref attempts);
                if(attempt==1){entered.Set();if(!release.WaitOne(3000))throw new TimeoutException();}
                if(Volatile.Read(ref available)==0)throw new COMException("模拟系统音频尚未就绪",unchecked((int)0x80004005));
                return host;
            })) {
                Check(entered.WaitOne(3000),"host initialization barrier");
                c.Enable(false);c.RequestChoices();release.Set();
                Await(()=>c.Current.ChoiceSerial==1,"device choices lost after failed pause cleanup");
                Check(!c.Current.Enabled && !new ConfigStore(directory).Load().Enabled,"pause lost when system unavailable");
                Check(c.Current.ChoiceError.Contains("模拟系统音频尚未就绪"),"device choice failure missing response");
                int before=Volatile.Read(ref attempts);
                Thread.Sleep(2300);
                Check(Volatile.Read(ref attempts)==before,"automatic retries survived pause");
                Volatile.Write(ref available,1);c.Enable(true);
                Await(()=>c.Current.Generation==1,"explicit enable after service recovery");
                c.RequestChoices();Await(()=>c.Current.ChoiceSerial==2,"next device request");
                Check(c.Current.ChoiceError=="","device menu error never cleared");
            }
        }
        Console.WriteLine("PASS unavailable audio system: pause persists before COM failure, no retry after pause, device request returns error, explicit recovery and next request succeed");
    }
    static void TestRebindIntent(string directory) {
        var store=new ConfigStore(directory);
        for(int scenario=0;scenario<3;scenario++){
            store.Save(new AppSettings());
            var host=new FakeHost();
            bool external=scenario!=1, explicitEnable=scenario==2;
            using(var c=new Coordinator(directory,unused=>host)){
                Await(()=>c.Current.Generation==1,"rebind test initial connection");
                host.HoldTick=true;
                Check(host.TickEntered.WaitOne(3000),"rebind test tick barrier");
                var edited=c.Current.Settings.Copy();edited.LeftId="replacement-left";
                host.External=external;
                c.Configure(edited);
                host.Notice.OnDefaultDeviceChanged(DataFlow.Render,Role.Multimedia,external?"user-choice":"source");
                if(explicitEnable)c.Enable(true);
                host.TickContinue.Set();
                Await(()=>c.Current.Settings.LeftId=="replacement-left","rebind settings were not saved");
                if(external && !explicitEnable){
                    Check(!c.Current.Enabled && !store.Load().Enabled && host.Takes==1,
                        "rebind erased external output choice and reacquired defaults");
                    var pausedEdit=c.Current.Settings.Copy();pausedEdit.RightId="replacement-right";pausedEdit.Enabled=true;
                    c.Configure(pausedEdit);
                    Await(()=>c.Current.Settings.RightId=="replacement-right","paused rebind settings");
                    Check(!c.Current.Enabled && !store.Load().Enabled && host.Takes==1,"saving paused settings enabled playback");
                } else {
                    Await(()=>c.Current.Generation==2,"intended rebind did not restart");
                    Check(c.Current.Enabled && store.Load().Enabled && host.Takes==2,"explicit enable or own notification lost");
                }
            }
            Check(host.BadOwner==0 && host.Overlaps==0 && host.Opened==host.Closed,"rebind resource ownership");
        }
        store.Save(new AppSettings());
        Console.WriteLine("PASS simultaneous rebind/default notification: external selection stays paused, paused settings stay paused, own notifications permit rebind, explicit enable is honored");
    }
    [STAThread]
    static void Main(){
        string directory=Path.Combine(Path.GetTempPath(),"XiaomiStereo-Lifecycle-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {TestPendingMixSave(directory);TestRebindIntent(directory);TestCoordinator(directory);TestInvalidConfig(directory);TestUnavailableSystem(directory);TestDiagnostics(directory);TestSettingsForm();TestCalibrationForm();}
        finally {foreach(var file in new[]{"settings.json","settings.json.tmp","manual.txt"})File.Delete(Path.Combine(directory,file));Directory.Delete(directory);}
    }
}
