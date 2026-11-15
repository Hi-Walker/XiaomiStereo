using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using NAudio.CoreAudioApi.Interfaces;

// Real Coordinator and ConfigStore, simulated endpoint ownership, no audio or UI.
internal static class ShutdownPowerTests {
    static readonly FieldInfo Quitting=typeof(Coordinator).GetField("quitting",BindingFlags.Instance|BindingFlags.NonPublic);
    static readonly FieldInfo Sleeping=typeof(Coordinator).GetField("sleeping",BindingFlags.Instance|BindingFlags.NonPublic);
    static int scenarios;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Await(Func<bool> condition,string message) {
        var watch=Stopwatch.StartNew();
        while(!condition()){if(watch.ElapsedMilliseconds>6500)throw new Exception(message);Thread.Sleep(4);}
    }
    sealed class Barrier : IDisposable {
        readonly ManualResetEvent entered=new ManualResetEvent(false),release=new ManualResetEvent(false);
        int armed;
        internal void Arm(){entered.Reset();release.Reset();Interlocked.Exchange(ref armed,1);}
        internal void Pass(){if(Interlocked.Exchange(ref armed,0)==1){entered.Set();if(!release.WaitOne(12000))throw new TimeoutException("owner barrier timed out");}}
        internal void Wait(){Check(entered.WaitOne(5000),"owner did not reach deterministic barrier");}
        internal void Release(){try{release.Set();}catch(ObjectDisposedException){}}
        public void Dispose(){entered.Dispose();release.Dispose();}
    }
    sealed class Audio : ILiveBridge {
        readonly Host host;
        bool closed;
        internal Audio(Host host){this.host=host;}
        public string State {get{return "正在转发";}}
        public string Summary {get{return "shutdown/power fixture";}}
        public bool Failed {get{return false;}}
        public int ErrorCode {get{return 0;}}
        public string ErrorType {get{return "";}}
        public void Tick(){host.Own();host.Tick.Pass();}
        public void SetMix(bool swap,double balance){host.Own();host.MixCalls++;}
        public void Resync(){host.Own();}
        public void Dispose(){host.Own();if(!closed){closed=true;host.Closed++;}}
    }
    sealed class Host : IControlHost {
        internal readonly Barrier Tick=new Barrier(),Opening=new Barrier(),Listing=new Barrier(),Restoring=new Barrier();
        internal volatile int Opened,Closed,Takes,BindCount,MixCalls,Restores,Registrations,Unregistrations,Disposals;
        internal int FailRestore,WrongOwner,Overlaps;
        int owner,disposed;
        bool bound;
        internal void Own(){int id=Thread.CurrentThread.ManagedThreadId;int old=Interlocked.CompareExchange(ref owner,id,0);if(old!=0 && old!=id)Interlocked.Increment(ref WrongOwner);}
        public void Register(IMMNotificationClient notice){Own();Registrations++;}
        public void Unregister(IMMNotificationClient notice){Own();Unregistrations++;}
        public bool RecoverLegacy(){Own();return false;}
        public void SnapshotIntent(){Own();}
        public bool ExternalChange(){Own();return false;}
        public void Take(string source,string fallback){Own();Takes++;}
        public void Restore(){Own();Restores++;Restoring.Pass();if(Interlocked.Exchange(ref FailRestore,0)==1)throw new DeviceUnavailableException("fixture USB restore failure");}
        public void ForgetIntent(){Own();}
        public bool Bind(AppSettings settings){Own();BindCount++;if(bound || Opened!=Closed)Overlaps++;bound=true;return false;}
        public ILiveBridge OpenBridge(AppSettings settings){Own();Opening.Pass();Opened++;return new Audio(this);}
        public void Unbind(){Own();bound=false;}
        public bool DevicesActive(){Own();return true;}
        public EndpointChoice[] List(){Own();Listing.Pass();return new EndpointChoice[0];}
        internal void ReleaseAll(){Tick.Release();Opening.Release();Listing.Release();Restoring.Release();}
        public void Dispose(){
            Own();if(Interlocked.Exchange(ref disposed,1)!=0)return;
            Disposals++;Tick.Dispose();Opening.Dispose();Listing.Dispose();Restoring.Dispose();
        }
    }
    sealed class Fixture : IDisposable {
        internal readonly string Directory;
        internal readonly ConfigStore Store;
        internal readonly Host Host;
        internal readonly Coordinator Worker;
        bool stopped;
        internal Fixture(bool enabled=true,bool corrupt=false) {
            Directory=Path.Combine(Path.GetTempPath(),"XiaomiStereo-ShutdownPower-"+Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);Store=new ConfigStore(Directory);
            if(corrupt)File.WriteAllText(Path.Combine(Directory,"settings.json"),"{broken");else Store.Save(Initial(enabled));
            Host=new Host();Worker=new Coordinator(Directory,unused=>Host);
            if(corrupt)Await(()=>Worker.Current.State=="需要处理","corrupt state not reported");
            else if(enabled)Await(()=>Worker.Current.Generation==1,"initial stream missing");
            else Await(()=>Worker.Current.State=="已暂停","initial pause missing");
        }
        internal static AppSettings Initial(bool enabled=true){return new AppSettings{Enabled=enabled,SourceId="source",LeftId="left",RightId="right",FallbackId="left"};}
        internal void HoldTick(){Host.Tick.Arm();Host.Tick.Wait();}
        internal void HoldList(){Host.Listing.Arm();Worker.RequestChoices();Host.Listing.Wait();}
        internal void Ack(long revision){Check(revision>0,"request rejected before test");Await(()=>Worker.Current.SettingsRevision>=revision,"settings completion not published");}
        internal void ExitHeld(Barrier held,Action afterSealed=null) {
            Exception error=null;
            var exit=new Thread(()=>{try{Worker.Dispose();}catch(Exception ex){error=ex;}}){IsBackground=true};
            exit.Start();
            try {
                Await(()=>(bool)Quitting.GetValue(Worker),"Dispose did not seal acceptance");
                if(afterSealed!=null)afterSealed();
            }finally{held.Release();}
            Check(exit.Join(6000),"normal Dispose did not finish");
            if(error!=null)throw error;
            stopped=true;
            Check(Host.Disposals==1 && Host.Opened==Host.Closed,"normal exit did not finish owned cleanup");
        }
        internal void Stop(){if(stopped)return;Host.ReleaseAll();Worker.Dispose();stopped=true;}
        internal string BlockSave(){string path=Path.Combine(Directory,"settings.json.tmp");System.IO.Directory.CreateDirectory(path);return path;}
        public void Dispose() {
            Stop();
            Check(Host.WrongOwner==0 && Host.Overlaps==0 && Host.Opened==Host.Closed &&
                Host.Registrations==Host.Unregistrations && Host.Disposals==1,"owner/resource invariant failed");
            string temporary=Path.Combine(Directory,"settings.json.tmp");
            if(System.IO.Directory.Exists(temporary))System.IO.Directory.Delete(temporary);
            foreach(string file in new[]{"settings.json","settings.json.tmp"})File.Delete(Path.Combine(Directory,file));
            System.IO.Directory.Delete(Directory);
        }
    }
    static AppSettings Devices(AppSettings settings,string suffix) {
        var edit=settings.Copy();edit.SourceId="source-"+suffix;edit.LeftId="left-"+suffix;edit.RightId="right-"+suffix;edit.FallbackId="fallback-"+suffix;
        edit.SourceContainer="11111111-1111-1111-1111-111111111111";
        edit.LeftContainer="22222222-2222-2222-2222-222222222222";
        edit.RightContainer="33333333-3333-3333-3333-333333333333";
        return edit;
    }
    static void DeviceEquals(AppSettings actual,AppSettings expected) {
        Check(actual.SourceId==expected.SourceId && actual.LeftId==expected.LeftId && actual.RightId==expected.RightId &&
            actual.FallbackId==expected.FallbackId && actual.SourceContainer==expected.SourceContainer &&
            actual.LeftContainer==expected.LeftContainer && actual.RightContainer==expected.RightContainer &&
            actual.ManageDefaults==expected.ManageDefaults,"shutdown lost or mixed device group");
    }
    static long Submit(Fixture f,string kind,AppSettings device) {
        if(kind=="mix"){f.Worker.SetBalance(-2);f.Worker.SetSwap(true);return f.Worker.SetBalance(6);}
        if(kind=="device")return f.Worker.ConfigureDevices(device);
        f.Worker.Enable(kind=="pause");f.Worker.Enable(kind=="enable");return 0;
    }
    static void FinalSaved(Fixture f,string kind,AppSettings device,long revision) {
        var saved=f.Store.Load();
        Check(String.IsNullOrEmpty(f.Worker.ShutdownError),"successful shutdown reported a failure");
        if(kind=="mix")Check(saved.BalanceDb==6 && saved.SwapChannels,"accepted last mix lost on exit");
        else if(kind=="device")DeviceEquals(saved,device);
        else Check(saved.Enabled==(kind=="enable"),"accepted last enable/pause intent lost on exit");
        if(revision>0)Check(f.Worker.Current.SettingsRevision>=revision,"normal shutdown did not acknowledge final setting");
    }
    static void PendingAndCompletedExit() {
        foreach(string kind in new[]{"mix","device","pause","enable"}) {
            for(int round=0;round<10;round++)using(var f=new Fixture(kind!="enable")) {
                var expected=Devices(f.Worker.Current.Settings,"exit");
                if(kind=="enable")f.HoldList();else f.HoldTick();
                int opens=f.Host.Opened,binds=f.Host.BindCount,takes=f.Host.Takes,mixes=f.Host.MixCalls;
                long revision=Submit(f,kind,expected);
                if(revision>0)Check(f.Worker.Current.SettingsRevision<revision,"fixture failed to hold pending settings");
                f.ExitHeld(kind=="enable"?f.Host.Listing:f.Host.Tick,()=>{
                    Check(f.Worker.SetBalance(20)==0 && f.Worker.SetSwap(false)==0 && f.Worker.ConfigureDevices(Fixture.Initial())==0,
                        "sealed coordinator accepted late settings");
                    f.Worker.Enable(kind!="enable");
                });
                FinalSaved(f,kind,expected,revision);
                Check(f.Host.Opened==opens && f.Host.BindCount==binds && f.Host.Takes==takes && f.Host.MixCalls==mixes,
                    "normal shutdown opened, rebound or altered audio just to save pending settings");scenarios++;
            }
            using(var f=new Fixture(kind!="enable")) {
                var expected=Devices(f.Worker.Current.Settings,"completed");long revision=Submit(f,kind,expected);
                if(revision>0)f.Ack(revision);
                else Await(()=>f.Worker.Current.Enabled==(kind=="enable") && f.Store.Load().Enabled==(kind=="enable"),"completed enable/pause control");
                f.Stop();FinalSaved(f,kind,expected,revision);scenarios++;
            }
        }
        Console.WriteLine("PASS normal exit: pending mix/device/pause/enable each 10 rounds, final value wins, late requests rejected, no new audio work; four completed controls");
    }
    static void ExitDuringRebind() {
        for(int round=0;round<10;round++)using(var f=new Fixture()) {
            var expected=Devices(f.Worker.Current.Settings,"inflight");f.Host.Opening.Arm();f.Worker.ConfigureDevices(expected);f.Host.Opening.Wait();
            Check(f.Store.Load().LeftId==expected.LeftId && f.Worker.Current.Settings.LeftId=="left","in-flight fixture lacks unpublished committed device");
            f.Worker.SetBalance(-5);long revision=f.Worker.SetSwap(true);f.Worker.Enable(false);
            f.ExitHeld(f.Host.Opening);var saved=f.Store.Load();DeviceEquals(saved,expected);
            Check(String.IsNullOrEmpty(f.Worker.ShutdownError),"in-flight shutdown reported a failure");
            Check(saved.BalanceDb==-5 && saved.SwapChannels && !saved.Enabled,"exit lost queued fields during rebind");
            Check(f.Worker.Current.SettingsRevision>=revision && f.Host.BindCount==2 && f.Host.Opened==2 && f.Host.Takes==1,
                "exit during existing OpenBridge started or took over another stream");scenarios++;
        }
        Console.WriteLine("PASS exit during unpublished rebind: 10 rounds preserve device + later mix + pause, no subsequent takeover");
    }
    static void ExitFailures() {
        using(var f=new Fixture()) {
            f.HoldTick();string blocked=f.BlockSave();
            long revision=f.Worker.SetBalance(9);f.Worker.Enable(false);f.ExitHeld(f.Host.Tick);
            Check(f.Store.Load().BalanceDb==0 && f.Store.Load().Enabled,"failed shutdown save partly replaced state");
            Check(f.Worker.Current.SettingsRevision>=revision && f.Worker.Current.LastError!="" && !String.IsNullOrEmpty(f.Worker.ShutdownError),
                "failed shutdown save had no completion/error");
            Check(f.Host.Opened==1 && f.Host.Closed==1 && f.Host.Takes==1,"shutdown save failure blocked ordinary cleanup");
            System.IO.Directory.Delete(blocked);scenarios++;
        }
        using(var f=new Fixture(false,true)) {
            f.HoldList();long revision=f.Worker.SetBalance(3);f.Worker.Enable(false);f.ExitHeld(f.Host.Listing);
            Check(File.ReadAllText(Path.Combine(f.Directory,"settings.json"))=="{broken","shutdown invented settings over invalid config");
            Check(f.Host.Opened==0 && f.Host.BindCount==0 && f.Worker.Current.LastError!="","invalid configuration shutdown opened audio or hid error");
            Check(!String.IsNullOrEmpty(f.Worker.ShutdownError),"invalid pending settings shutdown did not expose its error");
            Check(f.Worker.Current.SettingsRevision>=revision,"invalid-config shutdown left accepted settings permanently pending");scenarios++;
        }
        using(var f=new Fixture()) {
            f.HoldTick();string blocked=f.BlockSave();
            f.Host.FailRestore=1;long revision=f.Worker.SetBalance(9);f.Worker.Enable(false);f.ExitHeld(f.Host.Tick);
            Check(f.Store.Load().BalanceDb==0 && f.Store.Load().Enabled,"dual shutdown failure changed the saved settings");
            Check(f.Worker.Current.SettingsRevision>=revision,"dual shutdown failure left settings pending");
            foreach(string error in new[]{f.Worker.ShutdownError,f.Worker.Current.Detail,f.Worker.Current.LastError}) {
                Check(error.Length<=320 && error.Contains("退出时未能保存最后设置") && error.Contains("退出时未能完成普通输出恢复"),
                    "later restoration failure hid the unsaved-settings error or exceeded the message bound");
            }
            Check(f.Worker.Current.State=="需要处理" && f.Host.Restores==1 && f.Host.Opened==1 && f.Host.Closed==1 && f.Host.Takes==1,
                "dual failure skipped cleanup, hid its state, or restarted audio");
            System.IO.Directory.Delete(blocked);scenarios++;
        }
        Console.WriteLine("PASS shutdown write failure, corrupt configuration and simultaneous save/restore failures: old data preserved, bounded error retains both labels, failed settings complete, owned resources released");
    }
    static void Suspend(Fixture f) {
        f.Worker.SleepChanged(true);Await(()=>f.Worker.Current.State=="等待系统唤醒","suspend was not observed");
        Check(f.Host.Closed==f.Host.Opened,"suspend did not release existing stream");
    }
    static void SaveErrorAtResume(bool resume) {
        using(var f=new Fixture()) {
            if(resume)Suspend(f);
            f.HoldList();string blocked=f.BlockSave();long revision=f.Worker.SetBalance(7);
            if(resume)f.Worker.SleepChanged(false);
            f.Host.Listing.Release();
            try {
                f.Ack(revision);Check(f.Worker.Current.State=="需要处理" && f.Worker.Current.LastError!="" && f.Store.Load().BalanceDb==0,
                    "injected settings save failure not visible or changed state");
            }finally{System.IO.Directory.Delete(blocked);}
            f.Worker.Enable(true);
            Await(()=>f.Worker.Current.Generation==2,"repaired storage plus explicit reconnect still needs another Resume");
            Check(f.Host.Opened==2 && f.Worker.Current.Enabled,"reconnect after storage repair did not retain intent");scenarios++;
        }
    }
    static void RestoreErrorAtResume(bool resume) {
        using(var f=new Fixture()) {
            if(resume)Suspend(f);
            f.HoldList();var expected=Devices(f.Worker.Current.Settings,"restore-error");
            f.Host.FailRestore=1;long revision=f.Worker.ConfigureDevices(expected);
            if(resume)f.Worker.SleepChanged(false);
            f.Host.Listing.Release();f.Ack(revision);
            Check(f.Worker.Current.LastError.Contains("fixture USB restore failure"),"injected restore error not recorded");
            f.Worker.Enable(true);
            Await(()=>f.Worker.Current.Generation==2,"healthy endpoints plus explicit reconnect still need another Resume");
            Check(f.Host.Opened==2 && f.Worker.Current.Enabled,"reconnect after restore failure lost enable intent");scenarios++;
        }
    }
    static void ResumeAndErrors() {
        for(int round=0;round<3;round++) {
            using(var f=new Fixture()) {
                Suspend(f);f.Worker.SleepChanged(false);Await(()=>f.Worker.Current.Generation==2,"ordinary single Resume failed");
                Check(f.Host.Opened==2 && f.Worker.Current.Enabled,"ordinary resume control lost stream/intent");scenarios++;
            }
            SaveErrorAtResume(false);SaveErrorAtResume(true);RestoreErrorAtResume(false);RestoreErrorAtResume(true);
        }
        Console.WriteLine("PASS single Resume survives same-iteration settings I/O or endpoint restore error; 3 rounds each with ordinary-resume and awake-error controls, no second Resume");
    }
    static void PauseRestoreStatus() {
        for(int round=0;round<3;round++)using(var f=new Fixture()) {
            f.HoldTick();f.Host.FailRestore=1;f.Worker.Enable(false);
            var expected=Devices(f.Worker.Current.Settings,"paused");long revision=f.Worker.ConfigureDevices(expected);
            f.Host.Tick.Release();f.Ack(revision);DeviceEquals(f.Store.Load(),expected);
            Check(!f.Worker.Current.Enabled && !f.Store.Load().Enabled && f.Host.Opened==f.Host.Closed,"paused restore changed intent/stream");
            Check(f.Worker.Current.State=="已暂停" && f.Worker.Current.RetrySeconds==0,"successful restore still displays waiting");
            Check(f.Worker.Current.LastError.Contains("fixture USB restore failure"),"successful restore erased diagnostic error");scenarios++;
        }
        Console.WriteLine("PASS restored paused output publishes 已暂停 without clearing recent failure history: 3 rounds");
    }
    static void NewerPowerEventWins() {
        using(var f=new Fixture()) {
            Suspend(f);f.HoldList();f.Host.Restoring.Arm();f.Host.FailRestore=1;
            long revision=f.Worker.ConfigureDevices(Devices(f.Worker.Current.Settings,"power-order"));
            f.Worker.SleepChanged(false);f.Host.Listing.Release();f.Host.Restoring.Wait();
            f.Worker.SleepChanged(true);f.Host.Restoring.Release();f.Ack(revision);
            Check(f.Worker.Current.LastError.Contains("fixture USB restore failure"),"power-order fixture did not inject failure");
            int serial=f.Worker.Current.ChoiceSerial;f.Worker.RequestChoices();
            Await(()=>f.Worker.Current.ChoiceSerial>serial,"newer power event owner barrier");
            Check((bool)Sleeping.GetValue(f.Worker) && f.Worker.Current.Generation==1 && f.Host.Opened==1,
                "failed earlier Resume overwrote newer Suspend or restarted during sleep");
            f.Worker.SleepChanged(false);f.Worker.Enable(true);
            Await(()=>f.Worker.Current.Generation==2,"final real Resume could not recover after newer Suspend");scenarios++;
        }
        Console.WriteLine("PASS newer Suspend wins over a failing earlier Resume; only the subsequent real Resume permits recovery");
    }
    static void EnableIntentAcrossError() {
        foreach(bool enable in new[]{false,true})using(var f=new Fixture(!enable)) {
            f.HoldList();string blocked=f.BlockSave();long revision=f.Worker.SetBalance(4);f.Worker.Enable(enable);f.Host.Listing.Release();
            try {
                f.Ack(revision);Check(f.Worker.Current.LastError!="","expected same-turn settings error was absent");
                Check(f.Worker.Current.Enabled==enable,"settings failure discarded simultaneous latest enable/pause intent");
            }finally{System.IO.Directory.Delete(blocked);}
            f.Stop();Check(f.Store.Load().Enabled==enable,"normal exit failed to persist intent after earlier storage failure");scenarios++;
        }
        Console.WriteLine("PASS pause/enable intent survives same-turn settings error and becomes durable after storage repair plus normal exit, without a replacement user command");
    }
    static int Main() {
        try {
            PendingAndCompletedExit();ExitDuringRebind();ExitFailures();ResumeAndErrors();PauseRestoreStatus();NewerPowerEventWins();EnableIntentAcrossError();
            Console.WriteLine("PASS shutdown/power regression: "+scenarios+" deterministic scenarios; no real audio, power events, or UI");return 0;
        }catch(Exception error){Console.WriteLine(error.ToString());return 1;}
    }
}
