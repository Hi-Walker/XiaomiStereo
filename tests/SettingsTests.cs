using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using NAudio.CoreAudioApi.Interfaces;

// Deterministic command barriers and real state files; no Windows audio, tray, or shown forms.
internal static class SettingsTests {
    const string C1="11111111-1111-1111-1111-111111111111";
    const string C2="22222222-2222-2222-2222-222222222222";
    const string C3="33333333-3333-3333-3333-333333333333";
    static int scenarios;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Await(Func<bool> condition,string message) {
        var watch=Stopwatch.StartNew();
        while(!condition()){if(watch.ElapsedMilliseconds>7000)throw new Exception(message);Thread.Sleep(5);}
    }
    sealed class Barrier : IDisposable {
        readonly ManualResetEvent entered=new ManualResetEvent(false),release=new ManualResetEvent(false);
        int armed;
        internal void Arm(){entered.Reset();release.Reset();Interlocked.Exchange(ref armed,1);}
        internal void Pass(){if(Interlocked.Exchange(ref armed,0)==1){entered.Set();if(!release.WaitOne(10000))throw new TimeoutException("test barrier");}}
        internal void Wait(){Check(entered.WaitOne(5000),"owner did not reach test barrier");}
        internal void Release(){release.Set();}
        public void Dispose(){entered.Dispose();release.Dispose();}
    }
    sealed class FakeAudio : ILiveBridge {
        readonly Host host;
        bool disposed;
        internal bool Swap;
        internal double Balance;
        internal FakeAudio(Host host,AppSettings settings){this.host=host;Swap=settings.SwapChannels;Balance=settings.BalanceDb;}
        public string State {get{return "settings-test";}}
        public string Summary {get{return "settings-test";}}
        public bool Failed {get{return false;}}
        public int ErrorCode {get{return 0;}}
        public string ErrorType {get{return "";}}
        public void Tick(){host.Own();host.Tick.Pass();}
        public void SetMix(bool swap,double balance){host.Own();Swap=swap;Balance=balance;host.MixCalls++;}
        public void Resync(){host.Own();}
        public void Dispose(){host.Own();if(!disposed){disposed=true;host.Closed++;}}
    }
    sealed class Host : IControlHost {
        internal readonly Barrier Tick=new Barrier(),Opening=new Barrier();
        internal volatile FakeAudio Audio;
        internal volatile AppSettings Bound;
        internal volatile int Opened,Closed,MixCalls,Takes,BindCount;
        internal int FailRestore,WrongOwner,Overlaps,RegisterCount,UnregisterCount;
        internal bool AutoIdentity;
        int owner;
        bool bound;
        internal void Own(){int id=Thread.CurrentThread.ManagedThreadId;int old=Interlocked.CompareExchange(ref owner,id,0);if(old!=0 && old!=id)Interlocked.Increment(ref WrongOwner);}
        public void Register(IMMNotificationClient notice){Own();RegisterCount++;}
        public void Unregister(IMMNotificationClient notice){Own();UnregisterCount++;}
        public bool RecoverLegacy(){Own();return false;}
        public void SnapshotIntent(){Own();}
        public bool ExternalChange(){Own();return false;}
        public void Take(string source,string fallback){Own();Takes++;}
        public void Restore(){Own();if(Interlocked.Exchange(ref FailRestore,0)==1)throw new DeviceUnavailableException("test restore not ready");}
        public void ForgetIntent(){Own();}
        public bool Bind(AppSettings settings){
            Own();BindCount++;
            if(bound || Opened!=Closed)Overlaps++;
            bound=true;
            bool changed=AutoIdentity;
            if(changed){settings.SourceId="discovered-source";settings.LeftId="discovered-left";settings.RightId="discovered-right";
                settings.FallbackId="discovered-left";settings.SourceContainer=C1;settings.LeftContainer=C2;settings.RightContainer=C3;AutoIdentity=false;}
            Bound=settings.Copy();return changed;
        }
        public ILiveBridge OpenBridge(AppSettings settings){Own();Opening.Pass();Audio=new FakeAudio(this,settings);Opened++;return Audio;}
        public void Unbind(){Own();bound=false;}
        public bool DevicesActive(){Own();return true;}
        public EndpointChoice[] List(){Own();return new EndpointChoice[0];}
        public void Dispose(){Own();Tick.Dispose();Opening.Dispose();}
    }
    sealed class Fixture : IDisposable {
        internal readonly string Directory;
        internal readonly ConfigStore Store;
        internal readonly Host Host;
        internal readonly Coordinator Worker;
        bool stopped;
        internal Fixture(bool initialOpenBarrier=false){
            Directory=Path.Combine(Path.GetTempPath(),"XiaomiStereo-Settings-"+Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);Store=new ConfigStore(Directory);
            Store.Save(new AppSettings{SourceId="source",LeftId="left",RightId="right",FallbackId="left"});
            Host=new Host();
            if(initialOpenBarrier){Host.AutoIdentity=true;Host.Opening.Arm();}
            Worker=new Coordinator(Directory,unused=>Host);
            if(initialOpenBarrier)Host.Opening.Wait();else Await(()=>Worker.Current.Generation==1,"initial connection");
        }
        internal void Hold(){Host.Tick.Arm();Host.Tick.Wait();}
        internal void Ack(long revision){Check(revision>0,"request not accepted");Await(()=>Worker.Current.SettingsRevision>=revision,"settings acknowledgement");}
        internal void Stop(){if(!stopped){Host.Tick.Release();Host.Opening.Release();Worker.Dispose();stopped=true;}}
        public void Dispose(){
            Stop();
            Check(Host.WrongOwner==0 && Host.Overlaps==0 && Host.Opened==Host.Closed && Host.RegisterCount==Host.UnregisterCount,"owner or stream resource imbalance");
            foreach(string file in new[]{"settings.json","settings.json.tmp"})File.Delete(Path.Combine(Directory,file));
            System.IO.Directory.Delete(Directory);
        }
    }
    static AppSettings DeviceEdit(AppSettings current,string suffix) {
        var edit=current.Copy();edit.SourceId="source-"+suffix;edit.LeftId="left-"+suffix;edit.RightId="right-"+suffix;
        edit.FallbackId="fallback-"+suffix;edit.SourceContainer=C1;edit.LeftContainer=C2;edit.RightContainer=C3;
        return edit;
    }
    static void AssertSettings(AppSettings actual,AppSettings devices,double balance,bool swap,string at) {
        Check(actual.SourceId==devices.SourceId && actual.LeftId==devices.LeftId && actual.RightId==devices.RightId &&
            actual.FallbackId==devices.FallbackId && actual.SourceContainer==devices.SourceContainer &&
            actual.LeftContainer==devices.LeftContainer && actual.RightContainer==devices.RightContainer &&
            actual.ManageDefaults==devices.ManageDefaults,at+": device fields were lost or mixed");
        Check(actual.BalanceDb==balance && actual.SwapChannels==swap,at+": mix fields were lost");
        Check(actual.Profile=="Stable",at+": unrelated profile was changed");
    }
    static void AssertEverywhere(Fixture f,AppSettings devices,double balance,bool swap,int opens) {
        AssertSettings(f.Worker.Current.Settings,devices,balance,swap,"snapshot");
        AssertSettings(f.Store.Load(),devices,balance,swap,"disk");
        Check(f.Host.Bound.SourceId==devices.SourceId && f.Host.Bound.LeftId==devices.LeftId && f.Host.Bound.RightId==devices.RightId &&
            f.Host.Bound.FallbackId==devices.FallbackId && f.Host.Bound.SourceContainer==devices.SourceContainer &&
            f.Host.Bound.LeftContainer==devices.LeftContainer && f.Host.Bound.RightContainer==devices.RightContainer &&
            f.Host.Bound.ManageDefaults==devices.ManageDefaults,
            "actual binding differs from published settings");
        Check(f.Host.Audio.Balance==balance && f.Host.Audio.Swap==swap,"live bridge did not receive final mix");
        Check(f.Host.Opened==opens && f.Host.BindCount==opens,"settings caused unnecessary rebinds");
    }
    static void QueuedOrders() {
        for(int order=0;order<2;order++)using(var f=new Fixture()) {
            var old=f.Worker.Current.Settings.Copy();var expected=DeviceEdit(old,"queued");expected.ManageDefaults=false;
            var request=expected.Copy();request.Enabled=false;request.Profile="ignored-profile";request.BalanceDb=Double.NaN;request.SwapChannels=false;
            f.Hold();long last=0;
            try {
                if(order==0)last=f.Worker.ConfigureDevices(request);
                last=f.Worker.SetBalance(2.5);last=f.Worker.SetSwap(true);
                if(order==1)last=f.Worker.ConfigureDevices(request);
                request.LeftId="caller-mutated-after-submit";request.LeftContainer=null;
                AssertSettings(f.Worker.Current.Settings,old,0,false,"held old snapshot");
            }finally{f.Host.Tick.Release();}
            f.Ack(last);AssertEverywhere(f,expected,2.5,true,2);
            Check(f.Worker.Current.Enabled && f.Store.Load().Enabled,"device edit replaced enable intent");scenarios++;
        }
        Console.WriteLine("PASS queued device/mix in both orders: detached device group, unrelated fields ignored, disk/snapshot/live bridge agree, one rebind");
    }
    static void InFlightAndIdentity() {
        using(var f=new Fixture()) {
            var old=f.Worker.Current.Settings.Copy();var expected=DeviceEdit(old,"in-flight");
            f.Host.Opening.Arm();f.Worker.ConfigureDevices(expected);f.Host.Opening.Wait();long last=0;
            try {
                Check(f.Worker.Current.Settings.LeftId==old.LeftId && f.Store.Load().LeftId==expected.LeftId,"fixture did not hold stale published settings after save");
                last=f.Worker.SetSwap(true);last=f.Worker.SetBalance(-3.5);
            }finally{f.Host.Opening.Release();}
            f.Ack(last);AssertEverywhere(f,expected,-3.5,true,2);scenarios++;
        }
        using(var f=new Fixture(true)) {
            long last=f.Worker.SetBalance(4);last=f.Worker.SetSwap(true);f.Host.Opening.Release();f.Ack(last);
            var expected=new AppSettings{SourceId="discovered-source",LeftId="discovered-left",RightId="discovered-right",FallbackId="discovered-left",
                SourceContainer=C1,LeftContainer=C2,RightContainer=C3};
            AssertEverywhere(f,expected,4,true,1);scenarios++;
        }
        Console.WriteLine("PASS unpublished device rebind and first-bind identity discovery: subsequent field edits preserve latest device identity");
    }
    static void CoalescingAndNoOp() {
        using(var f=new Fixture()) {
            var expected=DeviceEdit(f.Worker.Current.Settings,"last");f.Hold();long last=0;
            try {
                f.Worker.ConfigureDevices(DeviceEdit(f.Worker.Current.Settings,"superseded"));
                for(int i=0;i<20000;i++){last=f.Worker.SetBalance((i%97-48)/2.0);last=f.Worker.SetSwap((i&1)!=0);}
                last=f.Worker.ConfigureDevices(expected);last=f.Worker.SetBalance(-2);last=f.Worker.SetSwap(false);
            }finally{f.Host.Tick.Release();}
            f.Ack(last);AssertEverywhere(f,expected,-2,false,2);
            Check(f.Host.MixCalls<=1,"coalesced commands replayed a history");scenarios++;
            DateTime timestamp=File.GetLastWriteTimeUtc(Path.Combine(f.Directory,"settings.json"));
            f.Hold();int mixes=f.Host.MixCalls;
            try {f.Worker.ConfigureDevices(f.Worker.Current.Settings);f.Worker.SetBalance(-2);last=f.Worker.SetSwap(false);}
            finally{f.Host.Tick.Release();}
            f.Ack(last);Check(File.GetLastWriteTimeUtc(Path.Combine(f.Directory,"settings.json"))==timestamp,"no-op rewrote state file");
            Check(f.Host.MixCalls==mixes && f.Host.Opened==2,"no-op changed live stream");scenarios++;
        }
        Console.WriteLine("PASS 40,004 queued edits coalesce to final fields and one device rebind; no-op requests acknowledge without disk/audio changes");
    }
    static void PauseAndFailure() {
        using(var f=new Fixture()) {
            var expected=DeviceEdit(f.Worker.Current.Settings,"paused");f.Hold();long last=0;
            try {
                f.Host.FailRestore=1;f.Worker.Enable(false);
                f.Worker.ConfigureDevices(expected);f.Worker.SetBalance(6);last=f.Worker.SetSwap(true);
            }finally{f.Host.Tick.Release();}
            f.Ack(last);AssertSettings(f.Worker.Current.Settings,expected,6,true,"pause snapshot");AssertSettings(f.Store.Load(),expected,6,true,"pause disk");
            Check(!f.Worker.Current.Enabled && !f.Store.Load().Enabled && f.Host.Opened==1 && f.Host.Takes==1,"settings restarted paused output");
            Check(f.Worker.Current.LastError!="","restore failure was not reported");scenarios++;
        }
        using(var f=new Fixture()) {
            string blocked=Path.Combine(f.Directory,"settings.json.tmp");System.IO.Directory.CreateDirectory(blocked);
            long revision;
            try {
                revision=f.Worker.SetBalance(7);f.Ack(revision);
                Check(f.Worker.Current.Settings.BalanceDb==0 && f.Store.Load().BalanceDb==0,"failed save committed partial settings");
                Check(f.Worker.Current.State=="需要处理" && f.Worker.Current.LastError!="","save failure not exposed");
            }finally{System.IO.Directory.Delete(blocked);}
            revision=f.Worker.SetBalance(5);f.Ack(revision);
            Check(f.Store.Load().BalanceDb==5 && f.Worker.Current.Settings.BalanceDb==5,"corrected settings could not be saved");
            f.Worker.Enable(true);Await(()=>f.Worker.Current.Generation==2,"explicit recovery after failed save");
            Check(f.Host.Audio.Balance==5,"new bridge lost recovered setting");scenarios++;
        }
        using(var f=new Fixture()) {
            string blocked=Path.Combine(f.Directory,"settings.json.tmp");System.IO.Directory.CreateDirectory(blocked);
            try {
                f.Worker.Enable(false);
                Await(()=>f.Worker.Current.State=="需要处理","pause save failure not reported");
                Check(!f.Worker.Current.Enabled && f.Store.Load().Enabled,"pause save failure fixture did not retain old persisted intent");
            }finally{System.IO.Directory.Delete(blocked);}
            long revision=f.Worker.SetBalance(0);f.Ack(revision);
            Check(!f.Worker.Current.Settings.Enabled && !f.Store.Load().Enabled,"same mix request failed to persist the latest paused intent");
            Check(f.Host.Opened==1 && f.Host.Closed==1 && f.Host.Takes==1,"repairing paused intent restarted output");scenarios++;
        }
        Console.WriteLine("PASS pause before settings survives restore failure; failed save acknowledges with old config/error and permits corrected save plus explicit recovery; no-op mix persists a previously failed pause");
    }
    static void RejectedAndStopped() {
        using(var f=new Fixture()) {
            long before=f.Worker.Current.SettingsRevision;
            foreach(double value in new[]{Double.NaN,Double.PositiveInfinity,-24.5,24.5}){
                bool rejected=false;try{f.Worker.SetBalance(value);}catch(ConfigurationException){rejected=true;}Check(rejected,"invalid balance accepted");
            }
            var invalid=f.Worker.Current.Settings.Copy();invalid.RightId=invalid.LeftId;
            bool bad=false;try{f.Worker.ConfigureDevices(invalid);}catch(ConfigurationException){bad=true;}Check(bad,"invalid devices accepted");
            Check(f.Worker.Current.SettingsRevision==before,"rejected settings acknowledged as accepted");
            long revision=f.Worker.SetBalance(1);f.Ack(revision);Check(revision==before+1,"rejected requests consumed sequence numbers");
            f.Stop();
            Check(f.Worker.SetBalance(2)==0 && f.Worker.SetSwap(true)==0 && f.Worker.ConfigureDevices(f.Store.Load())==0,"stopped coordinator accepted pending work");scenarios++;
        }
        Console.WriteLine("PASS invalid fields reject before acceptance; corrected request remains usable; stopped coordinator accepts no settings");
    }
    static StereoTray.PanelActions Actions(Func<double,long> balance,Func<bool,long> swap) {
        return new StereoTray.PanelActions{Balance=balance,Swap=swap,Enable=unused=>{},Devices=()=>{},Calibration=()=>{},Resync=()=>{},Diagnostics=()=>{}};
    }
    static TrackBar Balance(Form form){return (TrackBar)form.Controls.Find("balance",true)[0];}
    static CheckBox Swap(Form form){return (CheckBox)form.Controls.Find("swapChannels",true)[0];}
    static Coordinator.View View(long revision,double balance,bool swap) {
        return new Coordinator.View{SettingsRevision=revision,Settings=new AppSettings{BalanceDb=balance,SwapChannels=swap},State="已连接",Enabled=true};
    }
    static void PanelRefresh() {
        long serial=0;int balanceCalls=0,swapCalls=0;double sentBalance=0;bool sentSwap=false;
        var actions=Actions(value=>{balanceCalls++;sentBalance=value;return ++serial;},value=>{swapCalls++;sentSwap=value;return ++serial;});
        var pending=new StereoTray.PanelEdits();
        using(var panel=StereoTray.BuildControlPanel(View(0,0,false),null,actions,pending)) {
            Balance(panel).Value=7;Swap(panel).Checked=true;
            for(int i=0;i<10;i++)StereoTray.RefreshControlPanel(panel,View(0,0,false));
            Check(Balance(panel).Value==7 && Swap(panel).Checked && balanceCalls==1 && swapCalls==1,"stale refresh reverted pending fields or emitted writes");
            Balance(panel).Value=-8;
            StereoTray.RefreshControlPanel(panel,View(1,3.5,false));
            Check(Balance(panel).Value==-8 && Swap(panel).Checked,"first balance acknowledgement erased a newer pending field");
            StereoTray.RefreshControlPanel(panel,View(2,3.5,true));
            Check(Balance(panel).Value==-8 && Swap(panel).Checked,"intermediate ack erased newer balance request");
            Check(sentBalance==-4 && sentSwap && balanceCalls==2 && swapCalls==1,"field action included or rewrote another control");
        }
        using(var reopened=StereoTray.BuildControlPanel(View(2,3.5,true),null,actions,pending)) {
            Check(Balance(reopened).Value==-8 && Swap(reopened).Checked,"close/reopen lost pending value");
            StereoTray.RefreshControlPanel(reopened,View(3,-4,true));
            StereoTray.RefreshControlPanel(reopened,View(4,1,false));
            Check(Balance(reopened).Value==2 && !Swap(reopened).Checked,"acknowledged controls no longer follow worker");
            serial=4;
            Balance(reopened).Value=12;
            var failure=View(5,1,false);failure.State="需要处理";failure.Detail="simulated save failure";
            StereoTray.RefreshControlPanel(reopened,failure);
            Check(Balance(reopened).Value==2 && reopened.Controls.Find("controlDetail",true)[0].Text.Contains("simulated save failure"),"failed ack left permanent optimistic control");
        }
        using(var panel=StereoTray.BuildControlPanel(View(0,0,false),null,Actions(value=>0,value=>0))) {
            Balance(panel).Value=10;Swap(panel).Checked=true;StereoTray.RefreshControlPanel(panel,View(0,0,false));
            Check(Balance(panel).Value==0 && !Swap(panel).Checked,"unaccepted request stayed pending forever");
        }
        scenarios++;
        Console.WriteLine("PASS unshown panel: repeated stale refresh, independent/intermediate acks, rapid edits, close/reopen, final updates, failed ack and unaccepted request");
    }
    [STAThread] static int Main() {
        try {
            QueuedOrders();InFlightAndIdentity();CoalescingAndNoOp();PauseAndFailure();RejectedAndStopped();PanelRefresh();
            Console.WriteLine("PASS settings regression: "+scenarios+" scenarios; no real audio or shown windows");return 0;
        }catch(Exception e){Console.WriteLine(e.ToString());return 1;}
    }
}
