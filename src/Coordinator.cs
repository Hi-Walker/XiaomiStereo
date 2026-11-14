using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

internal interface ILiveBridge : IDisposable {
    string State { get; }
    string Summary { get; }
    bool Failed { get; }
    int ErrorCode { get; }
    string ErrorType { get; }
    void Tick();
    void SetMix(bool swap,double balanceDb);
    void Resync();
}

internal interface IControlHost : IDisposable {
    void Register(IMMNotificationClient observer);
    void Unregister(IMMNotificationClient observer);
    bool RecoverLegacy();
    void SnapshotIntent();
    bool ExternalChange();
    void Take(string source,string fallback);
    void Restore();
    void ForgetIntent();
    bool Bind(AppSettings config);
    ILiveBridge OpenBridge(AppSettings config);
    void Unbind();
    bool DevicesActive();
    EndpointChoice[] List();
}

internal sealed class WindowsControlHost : IControlHost {
    readonly MMDeviceEnumerator devices;
    readonly RouteManager routes;
    DeviceSet active;
    internal WindowsControlHost(string directory) {
        devices=new MMDeviceEnumerator();
        try {routes=new RouteManager(directory,devices);}
        catch {devices.Dispose();throw;}
    }
    public void Register(IMMNotificationClient observer) { Marshal.ThrowExceptionForHR(devices.RegisterEndpointNotificationCallback(observer)); }
    public void Unregister(IMMNotificationClient observer) { Marshal.ThrowExceptionForHR(devices.UnregisterEndpointNotificationCallback(observer)); }
    public bool RecoverLegacy() {return routes.RecoverLegacy();}
    public void SnapshotIntent() {routes.SnapshotIntent();}
    public bool ExternalChange() {return routes.ExternalChange();}
    public void Take(string source,string fallback) {routes.Take(source,fallback);}
    public void Restore() {routes.Restore();}
    public void ForgetIntent() {routes.ForgetIntent();}
    public bool Bind(AppSettings config) {
        if(active!=null)throw new ConfigurationException("旧设备仍被占用");
        active=DeviceSelector.Resolve(devices,config);
        return active.UpdateIdentity(config);
    }
    public ILiveBridge OpenBridge(AppSettings config) {return new Bridge(active.Source,active.Left,active.Right,config.SwapChannels,config.BalanceDb);}
    public bool DevicesActive() {return active!=null && active.Source.State==DeviceState.Active &&
        active.Left.State==DeviceState.Active && active.Right.State==DeviceState.Active;}
    public EndpointChoice[] List() {return DeviceSelector.List(devices);}
    public void Unbind() {if(active!=null){active.Dispose();active=null;}}
    public void Dispose() {try{Unbind();}finally{try{routes.Dispose();}finally{devices.Dispose();}}}
}

// One owner of all lifecycle and route operations. UI commands have a fixed number of
// replaceable slots. Core Audio callbacks only set bits; they never enter the UI or COM.
internal sealed class Coordinator : IDisposable {
    internal sealed class View {
        public string State="正在启动", Detail="", Audio="", ChoiceError="", LastError="";
        public bool Enabled;
        public long Generation, Restarts;
        public int RetrySeconds, ChoiceSerial;
        public EndpointChoice[] Choices=new EndpointChoice[0];
        public AppSettings Settings;
    }
    readonly object gate=new object();
    readonly AutoResetEvent wake=new AutoResetEvent(false);
    readonly Thread owner;
    readonly Stopwatch clock=Stopwatch.StartNew();
    readonly string directory;
    readonly Func<string,IControlHost> createHost;
    internal readonly BoundedDiagnostics Diag=new BoundedDiagnostics();
    volatile bool quitting;
    bool joined;
    int? enableCommand;
    AppSettings editCommand;
    bool resyncCommand, listCommand;
    int topologyFlag, defaultFlag, sleepFlag;
    long nextTry, nextHealth, nextTick, nextPublish, nextTopologyCheck, lastAttempt=-2000;
    bool wanted, intent, blocked, sleeping, registered, hasConfig, resumeRequired, pendingRestore;
    int failures, choiceSerial;
    long generation, successful;
    const int VolumeTick=100, HealthPeriod=2500;
    View snapshot=new View();
    string state="正在启动", detail="", choiceError="", lastError="";
    EndpointChoice[] choices=new EndpointChoice[0];
    IControlHost host;
    DeviceNotice notice;
    AppSettings config;
    ConfigStore store;
    ILiveBridge bridge;
    internal Coordinator(string directory) : this(directory,path=>new WindowsControlHost(path)) { }
    internal Coordinator(string directory,Func<string,IControlHost> createHost) {
        this.directory=directory;
        this.createHost=createHost;
        owner=new Thread(Run){Name="小米立体声 设备协调器",IsBackground=true};
        owner.SetApartmentState(ApartmentState.MTA);
        owner.Start();
    }
    internal View Current {get {lock(gate) return snapshot;}}
    internal void Enable(bool on) {lock(gate){if(quitting)return;enableCommand=on?1:0;wake.Set();}}
    internal void Resync() {lock(gate){if(quitting)return;resyncCommand=true;wake.Set();}}
    internal void Configure(AppSettings settings) {lock(gate){if(quitting)return;editCommand=settings.Copy();wake.Set();}}
    internal void RequestChoices() {lock(gate){if(quitting)return;listCommand=true;wake.Set();}}
    internal void SleepChanged(bool asleep) {Interlocked.Exchange(ref sleepFlag,asleep?1:2);Signal();}
    void Signal() {if(!quitting) {try {wake.Set();} catch(ObjectDisposedException) {} }}
    internal static int DelayMs(int failureCount) {
        if(failureCount<=1) return 2000;
        if(failureCount==2) return 5000;
        return failureCount==3?10000:30000;
    }
    sealed class DeviceNotice : IMMNotificationClient {
        readonly Coordinator o;
        internal DeviceNotice(Coordinator o){this.o=o;}
        void MarkTopology(){ if(Interlocked.Exchange(ref o.topologyFlag,1)==0)o.Signal(); }
        public void OnDeviceAdded(string id){MarkTopology();}
        public void OnDeviceRemoved(string id){MarkTopology();}
        public void OnDeviceStateChanged(string id,DeviceState state){MarkTopology();}
        public void OnDefaultDeviceChanged(DataFlow flow,Role role,string id){
            if(flow==DataFlow.Render && (role==Role.Console || role==Role.Multimedia))
                if(Interlocked.Exchange(ref o.defaultFlag,1)==0)o.Signal();
        }
        public void OnPropertyValueChanged(string id,PropertyKey key){}
    }
    void OpenSystem() {
        if(host==null) host=createHost(directory);
        if(notice==null) {
            notice=new DeviceNotice(this);
            try {
                host.Register(notice);
                registered=true;
            } catch(COMException e) { Diag.Note(0,"端点通知注册失败，使用恢复退避和周期状态检查",e.HResult); }
        }
    }
    bool InspectPrevious() {
        // Never overwrite an unresolved transaction. Rechecked on every explicit enable.
        bool external=host.RecoverLegacy();
        if(external) {Pause("普通播放设备已由其他程序或用户选择");return false;}
        return true;
    }
    void Run() {
        try {
            try {
                store=new ConfigStore(directory);
                try {config=store.Load(); hasConfig=true; wanted=config.Enabled;}
                catch {OpenSystem();host.RecoverLegacy();throw;}
                OpenSystem();
                if(InspectPrevious()) {
                    if(wanted) BeginIntent(); else state="已暂停";
                }
            } catch(Exception e){Fault(e);}
            Publish();
            while(!quitting) {
                try { Iterate(); }
                catch(Exception e) {Fault(e);}
                if(quitting) break;
                long now=clock.ElapsedMilliseconds;
                long due=now+1000;
                if(bridge!=null && wanted && !blocked && !sleeping)due=Math.Min(due,nextTick);
                else if(!sleeping && (pendingRestore || wanted && !blocked))due=Math.Min(due,nextTry);
                if(nextTopologyCheck!=0)due=Math.Min(due,nextTopologyCheck);
                wake.WaitOne((int)Math.Max(1,due-now));
            }
        } finally {
            try { CloseAudio(); } catch(Exception e){RecordError(e);}
            if(registered)try {host.Unregister(notice);}catch(Exception e){RecordError(e);}
            if(host!=null)try {host.Dispose();}catch(Exception e){RecordError(e);}
        }
    }
    void BeginIntent() {
        if(config.ManageDefaults) {host.SnapshotIntent();intent=true;}
        nextTry=0;failures=0;blocked=false;state="正在连接";detail="";
    }
    void SetDesired(bool value) {
        wanted=value;
        if(config.Enabled==value)return;
        var copy=config.Copy();copy.Enabled=value;
        store.Save(copy);config=copy;
    }
    void Pause(string reason) {
        wanted=false;blocked=true;
        try { SetDesired(false); }
        finally {try {CloseAudio();}finally {if(host!=null)host.ForgetIntent();intent=false;}}
        state="已暂停";detail=reason;Diag.Note(generation,reason,0);nextPublish=0;
    }
    void Iterate() {
        long now=clock.ElapsedMilliseconds;
        bool checkHealth=now>=nextHealth;
        if(checkHealth)nextHealth=now+HealthPeriod;
        int? command; AppSettings edit; bool sync,list;
        lock(gate) {
            command=enableCommand;enableCommand=null; edit=editCommand;editCommand=null;
            sync=resyncCommand;resyncCommand=false;list=listCommand;
        }
        int power=Interlocked.Exchange(ref sleepFlag,0);
        if(Interlocked.Exchange(ref topologyFlag,0)!=0 && nextTopologyCheck==0) nextTopologyCheck=now+200;
        bool topo=nextTopologyCheck!=0 && now>=nextTopologyCheck;
        if(topo)nextTopologyCheck=0;
        bool changed=Interlocked.Exchange(ref defaultFlag,0)!=0;
        if(command==0) {
            wanted=false;blocked=true;
            if(hasConfig)SetDesired(false);
            OpenSystem();
            if(!hasConfig) {CloseAudio();state="需要处理";detail="请修复设置文件后启用";blocked=true;wanted=false;}
            else Pause("已恢复普通输出");
        }
        if(edit!=null) Apply(edit);
        if(command==1) {
            if(!hasConfig) {config=store.Load();hasConfig=true;}
            OpenSystem();
            if(bridge==null || blocked) {
                if(bridge!=null || pendingRestore || intent)CloseAudio();
                if(intent) {host.ForgetIntent();intent=false;}
                if(InspectPrevious()) {SetDesired(true);BeginIntent();}
            }
        }
        if(list) {
            // Keep the request pending if an earlier command threw. Always publish a result once processed.
            lock(gate){listCommand=false;}
            try {OpenSystem();choices=host.List();choiceError="";}
            catch(Exception e) {choices=new EndpointChoice[0];choiceError=UserMessage(e);}
            choiceSerial=unchecked(choiceSerial+1);nextPublish=0;
        }
        if(power!=0) {
            sleeping=power==1;
            if(sleeping) { CloseAudio(); if(wanted && !blocked)state="等待系统唤醒"; }
            else {
                // Suspend/resume messages may collapse into a single resume notification.
                if(bridge!=null)CloseAudio();
                nextTry=now+200;resumeRequired=true;
            }
            nextPublish=0;
        }
        if(bridge==null && topo)nextTry=Math.Min(nextTry,Math.Max(lastAttempt+2000,now));
        if(pendingRestore && !sleeping && now>=nextTry) {
            lastAttempt=now;
            CloseAudio();
            if(!wanted) {host.ForgetIntent();intent=false;state="已暂停";detail="已恢复普通输出";nextPublish=0;}
        }
        if(wanted && !blocked && !sleeping && !pendingRestore) {
            if(intent && (changed || checkHealth) && host.ExternalChange()) {
                Pause("默认输出已更改，已停止自动接管");
            } else {
                if(bridge!=null && (topo || resumeRequired || checkHealth)) {
                    resumeRequired=false;
                    if(!host.DevicesActive())
                        throw new DeviceUnavailableException("配置的播放设备已离线");
                }
                if(bridge==null && now>=nextTry) StartAudio();
                if(bridge!=null && sync) bridge.Resync();
                if(bridge!=null && now>=nextTick) {
                    nextTick=now+VolumeTick;
                    if(bridge.Failed) throw new DeviceUnavailableException("音频流已停止。"+BoundedDiagnostics.Limit(bridge.Summary,240));
                    bridge.Tick();
                }
            }
        }
        if(now>=nextPublish || command.HasValue || edit!=null)Publish();
    }
    void Apply(AppSettings edit) {
        // Preserve the current user intent even if the editor was opened before a pause.
        if(!hasConfig) throw new ConfigurationException("请先修复 settings.json 再重新启用");
        edit.Enabled=wanted;edit.Validate();
        bool rebind=edit.SourceId!=config.SourceId || edit.LeftId!=config.LeftId ||
            edit.RightId!=config.RightId || edit.FallbackId!=config.FallbackId ||
            edit.SourceContainer!=config.SourceContainer || edit.LeftContainer!=config.LeftContainer ||
            edit.RightContainer!=config.RightContainer || edit.ManageDefaults!=config.ManageDefaults;
        if(!rebind && edit.SwapChannels==config.SwapChannels && edit.BalanceDb==config.BalanceDb)return;
        if(rebind) {
            CloseAudio();
            // Restore can observe an external choice while settings are being saved.
            // Preserve that intent before ForgetIntent takes the next route snapshot.
            if(intent && host.ExternalChange()) {
                Pause("默认输出已更改，已停止自动接管");
                edit.Enabled=wanted;
            } else {
                if(host!=null)host.ForgetIntent();
                intent=false;
            }
        }
        store.Save(edit);config=edit;
        if(rebind && wanted){OpenSystem();if(InspectPrevious())BeginIntent();}
        if(bridge!=null)bridge.SetMix(config.SwapChannels,config.BalanceDb);
        nextPublish=0;
    }
    void StartAudio() {
        lastAttempt=clock.ElapsedMilliseconds;
        OpenSystem();
        if(config.ManageDefaults && !intent) {if(!InspectPrevious())return;BeginIntent();}
        var bound=config.Copy();
        if(host.Bind(bound)){store.Save(bound);config=bound;}
        bridge=host.OpenBridge(config);
        bool cancel;
        lock(gate){cancel=quitting || enableCommand==0 || Volatile.Read(ref sleepFlag)==1;}
        if(cancel){CloseAudio();return;}
        if(config.ManageDefaults) {
            if(host.ExternalChange()){Pause("连接期间默认输出已更改");return;}
            host.Take(config.SourceId,config.FallbackId);
        }
        generation=BoundedDiagnostics.Increment(generation);
        successful=BoundedDiagnostics.Increment(successful);
        Diag.Note(generation,"播放链路已准备好",0);
        failures=0;blocked=false;detail="";state="已连接";nextTick=0;nextPublish=0;
    }
    void CloseAudio() {
        try {if(host!=null)host.Restore();pendingRestore=false;}
        catch(DeviceUnavailableException){pendingRestore=true;throw;}
        finally {
            // Keep ownership until Dispose succeeds. If teardown is uncertain, do not start another flow.
            if(bridge!=null) {
                try {bridge.Dispose();} catch(Exception e){throw new ConfigurationException("旧音频流未能完整停止，请退出后检查设备",e);}
                bridge=null;
            }
            if(host!=null)host.Unbind();
        }
    }
    void RecordError(Exception e) { Diag.Note(generation,e.GetType().Name,e.HResult); }
    static string UserMessage(Exception e) {
        return BoundedDiagnostics.Limit(e.Message,256);
    }
    static bool NeedsIntervention(int code) {
        return code==unchecked((int)0x80070005) || code==unchecked((int)0x88890008);
    }
    void Fault(Exception e) {
        bool failedStream=bridge!=null && bridge.Failed;
        if(failedStream)Diag.Note(generation,BoundedDiagnostics.Limit(bridge.ErrorType,64),bridge.ErrorCode);
        else RecordError(e);
        bool stop=e is ConfigurationException || e is IOException || e is UnauthorizedAccessException ||
            e is ArgumentException || e is NotSupportedException || !hasConfig || NeedsIntervention(e.HResult) ||
            (failedStream && NeedsIntervention(bridge.ErrorCode));
        try {
            if(e is RouteConflictException) {Pause("默认输出已更改，已停止自动接管");return;}
            if(!pendingRestore || bridge!=null)CloseAudio();
        } catch(Exception release) {e=release;RecordError(e);stop=stop || !(release is DeviceUnavailableException);}
        detail=UserMessage(e);lastError=detail;
        if((!wanted && !pendingRestore) || stop) {blocked=true;pendingRestore=false;state="需要处理";}
        else {
            if(failures<int.MaxValue)failures++;
            state=pendingRestore?"等待恢复普通输出":e is DeviceUnavailableException?"等待设备":"正在恢复";
            nextTry=clock.ElapsedMilliseconds+DelayMs(failures);
        }
        nextPublish=0;Publish();
    }
    void Publish() {
        var view=new View();
        view.State=state;view.Detail=detail;view.LastError=lastError;
        if(bridge!=null && !blocked && wanted) {
            try {
                view.State=bridge.State;
                string summary=bridge.Summary;
                if(bridge.Failed)view.Detail=BoundedDiagnostics.Limit(summary,240);
                else view.Audio=BoundedDiagnostics.Limit(summary,3072);
            }
            catch(Exception e){view.Audio="等待音频统计";RecordError(e);}
        }
        view.Generation=generation;view.Restarts=successful;view.Enabled=wanted;
        view.Settings=hasConfig?config.Copy():null;
        view.Choices=choices;view.ChoiceSerial=choiceSerial;view.ChoiceError=choiceError;
        if((pendingRestore || wanted && !blocked) && bridge==null && !sleeping)
            view.RetrySeconds=(int)Math.Max(0,(nextTry-clock.ElapsedMilliseconds+999)/1000);
        lock(gate){snapshot=view;}
        nextPublish=clock.ElapsedMilliseconds+1000;
    }
    public void Dispose() {
        lock(gate){if(joined)return;quitting=true;wake.Set();}
        if(!owner.Join(3500))return; // OS cleanup/journal on restart if a driver blocks.
        lock(gate){if(!joined){joined=true;wake.Dispose();}}
    }
}
