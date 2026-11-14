using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

sealed class EndpointChoice {
    public readonly string Id,Name,Container,MatchingId;
    public EndpointChoice(string id,string name,string container,string matchingId) {
        Id=id;Name=name;Container=container;MatchingId=matchingId;
    }
    public override string ToString() { return Name; }
}
sealed class DeviceSet : IDisposable {
    public MMDevice Source, Left, Right;
    internal EndpointChoice SourceInfo,LeftInfo,RightInfo;
    public bool UpdateIdentity(AppSettings cfg) {
        string sourceId=SourceInfo.Id, leftId=LeftInfo.Id, rightId=RightInfo.Id;
        bool first=cfg.SourceId==null && cfg.LeftId==null && cfg.RightId==null;
        string fallback=cfg.FallbackId;
        // The verified endpoint binding supplies the replacement for either speaker.
        if(cfg.LeftId!=null && DeviceSelector.Same(fallback,cfg.LeftId)) fallback=leftId;
        else if(cfg.RightId!=null && DeviceSelector.Same(fallback,cfg.RightId)) fallback=rightId;
        else if(first && fallback==null) fallback=leftId;
        bool changed=cfg.SourceId!=sourceId || cfg.LeftId!=leftId || cfg.RightId!=rightId ||
            cfg.SourceContainer!=SourceInfo.Container || cfg.LeftContainer!=LeftInfo.Container ||
            cfg.RightContainer!=RightInfo.Container || cfg.FallbackId!=fallback;
        cfg.SourceId=sourceId;cfg.LeftId=leftId;cfg.RightId=rightId;cfg.FallbackId=fallback;
        cfg.SourceContainer=SourceInfo.Container;cfg.LeftContainer=LeftInfo.Container;cfg.RightContainer=RightInfo.Container;
        return changed;
    }
    public void Dispose() {
        try { if(Right!=null) Right.Dispose(); }
        finally { try { if(Left!=null) Left.Dispose(); } finally { if(Source!=null) Source.Dispose(); Source=Left=Right=null; } }
    }
}
static class DeviceSelector {
    static readonly PropertyKey ContainerKey=new PropertyKey(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"),2);
    static readonly PropertyKey MatchKey=new PropertyKey(new Guid("a8b865dd-2e3d-4094-ad97-e593a70c75d6"),8);
    const string VirtualDriver=@"ROOT\SteamStreamingSpeakers";
    internal static bool Same(string a,string b) { return String.Equals(a,b,StringComparison.OrdinalIgnoreCase); }
    static string Property(MMDevice device,PropertyKey key) {
        var p=device.Properties[key];return p==null || p.Value==null ? null : Convert.ToString(p.Value);
    }
    public static EndpointChoice[] List(MMDeviceEnumerator e) {
        var result=new List<EndpointChoice>();
        var devices=e.EnumerateAudioEndPoints(DataFlow.Render,DeviceState.Active);
        if(devices.Count>256) throw new ConfigurationException("活动播放端点超过支持的 256 个。");
        foreach(var d in devices) using(d) {
            try {
                var container=Property(d,ContainerKey);
                if(Same(container,"00000000-0000-0000-ffff-ffffffffffff") || Same(container,Guid.Empty.ToString())) container=null;
                result.Add(new EndpointChoice(d.ID,d.FriendlyName,container,Property(d,MatchKey)));
            } catch(COMException) { /* Device vanished while resolving its metadata. */ }
        }
        return result.ToArray();
    }
    internal static EndpointChoice Pick(EndpointChoice[] candidates,string id,string container,string hint,bool source) {
        if(id!=null) foreach(var x in candidates) if(Same(id,x.Id)) return x;
        EndpointChoice found=null;
        foreach(var x in candidates) {
            bool match=container!=null ? Same(container,x.Container) :
                source ? id==null && Same(x.MatchingId,VirtualDriver) :
                id==null && x.Name!=null && x.Name.IndexOf(hint,StringComparison.OrdinalIgnoreCase)>=0;
            if(!match) continue;
            if(found!=null) throw new ConfigurationException("多个设备符合 "+hint+"；请按端点 ID 手动选择。");
            found=x;
        }
        if(found==null) throw new DeviceUnavailableException("等待播放设备 "+hint+"。可在设备菜单重新绑定。");
        return found;
    }
    public static DeviceSet Resolve(MMDeviceEnumerator e,AppSettings cfg) {
        cfg.Validate();
        var list=List(e);
        var source=Pick(list,cfg.SourceId,cfg.SourceContainer,"虚拟源",true);
        var left=Pick(list,cfg.LeftId,cfg.LeftContainer,"Pro-LEFT",false);
        var right=Pick(list,cfg.RightId,cfg.RightContainer,"Pro-RIGHT",false);
        if(Same(source.Id,left.Id) || Same(source.Id,right.Id) || Same(left.Id,right.Id))
            throw new ConfigurationException("源、左、右不能是同一设备。");
        var set=new DeviceSet { SourceInfo=source,LeftInfo=left,RightInfo=right };
        try {
            set.Source=e.GetDevice(source.Id);set.Left=e.GetDevice(left.Id);set.Right=e.GetDevice(right.Id);
            return set;
        } catch { set.Dispose(); throw; }
    }
}

// This Windows compatibility interface is isolated here. Slots refer to the original
// Windows 7 IPolicyConfig layout, not the differently ordered Vista interface.
[ComImport,Guid("F8679F50-850A-41CF-9C72-430F290290C8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IRoutePolicy {
    void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
    void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
    void GetPropertyValue(); void SetPropertyValue();
    void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id,Role role);
}
[ComImport,Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class RoutePolicy {}

interface IRoleBackend {
    string Get(int role); void Set(int role,string id); bool Available(string id);
}
sealed class WindowsRoles : IRoleBackend,IDisposable {
    readonly MMDeviceEnumerator devices;
    IRoutePolicy policy;
    public WindowsRoles(MMDeviceEnumerator devices) { this.devices=devices; }
    public string Get(int role) {
        try { using(var d=devices.GetDefaultAudioEndpoint(DataFlow.Render,(Role)role)) return d.ID; }
        catch(COMException ex) { if((uint)ex.ErrorCode==0x80070490) return null; throw; }
    }
    public void Set(int role,string id) {
        if(policy==null) policy=(IRoutePolicy)new RoutePolicy();
        policy.SetDefaultEndpoint(id,(Role)role);
    }
    public bool Available(string id) {
        if(String.IsNullOrEmpty(id)) return false;
        try { using(var d=devices.GetDevice(id)) return d.State==DeviceState.Active && d.DataFlow==DataFlow.Render; }
        catch(COMException ex) { if((uint)ex.ErrorCode==0x80070490 || (uint)ex.ErrorCode==0x88890004) return false; throw; }
    }
    public void Dispose() { if(policy!=null) { Marshal.ReleaseComObject(policy);policy=null; } }
}

sealed class RouteEntry {
    public string Prior,Preferred,Phase="prepared",Destination;
}
sealed class RestoreWrite {
    public int Role;
    public string Target,PeerBefore,PeerDestination;
}
sealed class RouteRecord {
    public string Source,Fallback,Session=Guid.NewGuid().ToString("D");
    public bool Coupled,Independent,External;
    public int Pid; public long StartTicks;
    public RouteEntry[] Roles={new RouteEntry(),new RouteEntry()};
    public RestoreWrite PendingWrite;
}
interface IRouteJournal {
    RouteRecord Read(); void Write(RouteRecord record); void Clear();
}
sealed class FileJournal : IRouteJournal {
    public const int MaxBytes=32768;
    readonly string path;
    public FileJournal(string directory) { path=Path.Combine(directory,"recovery.json"); }
    static Dictionary<string,object> RoleFields(RouteEntry role) {
        return new Dictionary<string,object> { {"prior",role.Prior},{"preferred",role.Preferred},{"phase",role.Phase},{"destination",role.Destination} };
    }
    public void Write(RouteRecord r) {
        object pending=null;
        if(r.PendingWrite!=null) pending=new Dictionary<string,object> {
            {"role",r.PendingWrite.Role},{"target",r.PendingWrite.Target},
            {"peerBefore",r.PendingWrite.PeerBefore},{"peerDestination",r.PendingWrite.PeerDestination}
        };
        StateFile.Write(path,MaxBytes,new Dictionary<string,object> {
            {"schemaVersion",1},{"session",r.Session},{"pid",r.Pid},{"startUtcTicks",r.StartTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)},
            {"target",r.Source},{"fallback",r.Fallback},{"coupled",r.Coupled},{"independent",r.Independent},{"external",r.External},
            {"pendingWrite",pending},{"Console",RoleFields(r.Roles[0])},{"Multimedia",RoleFields(r.Roles[1])}
        });
    }
    public RouteRecord Read() {
        byte[] b;
        try { b=StateFile.Read(path,MaxBytes); }
        catch(FileNotFoundException) { return null; } catch(DirectoryNotFoundException) { return null; }
        var j=StateFile.Parse(b,MaxBytes);
        StateFile.Keys(j,"schemaVersion","session","pid","startUtcTicks","target","fallback","coupled","independent","external","pendingWrite","Console","Multimedia");
        var r=new RouteRecord();
        Guid session;
        if(StateFile.Get<int>(j,"schemaVersion")!=1 ||
           !Guid.TryParseExact(StateFile.Get<string>(j,"session"),"D",out session) ||
           !long.TryParse(StateFile.Get<string>(j,"startUtcTicks"),out r.StartTicks) || r.StartTicks<=0 ||
           (r.Pid=StateFile.Get<int>(j,"pid"))<=0) throw new ConfigurationException("恢复记录版本或拥有者无效。");
        r.Session=session.ToString("D");r.Source=StateFile.Text(j,"target",false);r.Fallback=StateFile.Text(j,"fallback",true);
        r.Coupled=j.ContainsKey("coupled") && StateFile.Get<bool>(j,"coupled");
        r.Independent=j.ContainsKey("independent") && StateFile.Get<bool>(j,"independent");
        r.External=j.ContainsKey("external") && StateFile.Get<bool>(j,"external");
        if(j.ContainsKey("pendingWrite") && j["pendingWrite"]!=null) {
            var pending=StateFile.Get<Dictionary<string,object>>(j,"pendingWrite");
            StateFile.Keys(pending,"role","target","peerBefore","peerDestination");
            r.PendingWrite=new RestoreWrite {
                Role=StateFile.Get<int>(pending,"role"),Target=StateFile.Text(pending,"target",false),
                PeerBefore=StateFile.Text(pending,"peerBefore",true),PeerDestination=StateFile.Text(pending,"peerDestination",true)
            };
            if(r.PendingWrite.Role<0 || r.PendingWrite.Role>1 || DeviceSelector.Same(r.PendingWrite.Target,r.Source) ||
                DeviceSelector.Same(r.PendingWrite.PeerDestination,r.Source))
                throw new ConfigurationException("恢复记录的待确认操作无效。");
        }
        for(int i=0;i<2;i++) {
            var map=StateFile.Get<Dictionary<string,object>>(j,i==0?"Console":"Multimedia");
            StateFile.Keys(map,"prior","preferred","phase","destination");
            var x=r.Roles[i];x.Prior=StateFile.Text(map,"prior",true);x.Phase=StateFile.Text(map,"phase",false);
            x.Preferred=map.ContainsKey("preferred")?StateFile.Text(map,"preferred",true):x.Prior;
            x.Destination=StateFile.Text(map,"destination",true);
            if(Array.IndexOf(new[]{"prepared","applying","owned","restoring","done","external","linked-conflict"},x.Phase)<0 ||
               ((x.Phase=="restoring" || x.Phase=="linked-conflict") && (x.Destination==null || DeviceSelector.Same(r.Source,x.Destination))))
                throw new ConfigurationException("恢复记录动作无效。");
        }
        return r;
    }
    public void Clear() { File.Delete(path); }
}

sealed class RouteManager : IDisposable {
    readonly IRoleBackend backend;
    readonly IRouteJournal journal;
    readonly Func<int,long,bool> alive;
    RouteRecord active;
    string[] expected,original;
    bool conflict,checkedLegacy;
    public RouteManager(string directory,MMDeviceEnumerator devices) :
        this(new WindowsRoles(devices),new FileJournal(directory),OwnerAlive) {}
    internal RouteManager(IRoleBackend backend,IRouteJournal journal,Func<int,long,bool> checkOwner) {
        this.backend=backend;this.journal=journal;alive=checkOwner;
    }
    static bool OwnerAlive(int pid,long ticks) {
        try {
            using(var p=Process.GetProcessById(pid)) return !p.HasExited && p.StartTime.ToUniversalTime().Ticks==ticks;
        } catch(ArgumentException) { return false; }
        catch(InvalidOperationException) { return false; }
        catch(Win32Exception) { return true; } // Inaccessible owner: fail closed.
    }
    public bool RecoverLegacy() {
        if(checkedLegacy) return conflict;
        var prior=journal.Read();
        if(prior!=null) {
            if(alive(prior.Pid,prior.StartTicks)) throw new ConfigurationException("恢复记录属于仍在运行或无法检查的进程。");
            active=prior;
            Restore();
        }
        checkedLegacy=true;
        return conflict;
    }
    public void SnapshotIntent() {
        if(!checkedLegacy) RecoverLegacy();
        if(active!=null) throw new ConfigurationException("旧路由事务尚待恢复。");
        if(expected==null) {
            expected=new[]{backend.Get(0),backend.Get(1)};
            original=(string[])expected.Clone();
        }
    }
    public void ForgetIntent() { expected=null;original=null;conflict=false; }
    public bool ExternalChange() {
        if(expected!=null && !conflict) {
            for(int i=0;i<2;i++) if(!DeviceSelector.Same(backend.Get(i),expected[i])) conflict=true;
        }
        return conflict;
    }
    public void Take(string sourceId,string fallbackId) {
        if(!checkedLegacy) RecoverLegacy();
        if(active!=null) throw new ConfigurationException("尚有未完成的路由事务。");
        SnapshotIntent();
        if(ExternalChange()) throw new RouteConflictException("默认输出已在程序外更改，请手动启用以重新接管。");
        StateFile.CheckText(sourceId,false);StateFile.CheckText(fallbackId,true);
        if(!backend.Available(sourceId)) throw new DeviceUnavailableException("虚拟源尚未就绪。");
        var r=new RouteRecord { Source=sourceId,Fallback=fallbackId };
        using(var p=Process.GetCurrentProcess()) { r.Pid=p.Id;r.StartTicks=p.StartTime.ToUniversalTime().Ticks; }
        for(int i=0;i<2;i++) {
            r.Roles[i].Prior=expected[i];
            r.Roles[i].Preferred=original[i];
            if((expected[i]==null || DeviceSelector.Same(expected[i],sourceId)) &&
               (DeviceSelector.Same(sourceId,fallbackId) || !backend.Available(fallbackId)))
                throw new ConfigurationException("接管前需要选择可用的普通回退输出。");
        }
        journal.Write(r); active=r;
        try {
            // A real Win11 setter may change both roles atomically at the policy layer.
            // Both preimages must already be recoverable before the very first COM call.
            r.Roles[0].Phase=r.Roles[1].Phase="applying";journal.Write(r);
            for(int i=0;i<2;i++) {
                var x=r.Roles[i];
                string current=backend.Get(i);
                bool alreadySet=i>0 && DeviceSelector.Same(current,sourceId);
                if(!DeviceSelector.Same(current,x.Prior) && !alreadySet) {
                    conflict=true;
                    throw new RouteConflictException("修改默认设备前检测到外部选择。");
                }
                int peer=1-i;
                string beforePeer=backend.Get(peer);
                string expectedPeer=i==0 ? r.Roles[peer].Prior : sourceId;
                if(!DeviceSelector.Same(backend.Get(i),current) || !DeviceSelector.Same(beforePeer,expectedPeer)) {
                    conflict=true;
                    throw new RouteConflictException("路由已被外部修改。");
                }
                if(!DeviceSelector.Same(current,sourceId)) {
                    backend.Set(i,sourceId);
                    string afterPeer=backend.Get(peer);
                    if(!DeviceSelector.Same(beforePeer,afterPeer)) {
                        if(!DeviceSelector.Same(afterPeer,sourceId)) {
                            conflict=true;throw new RouteConflictException("切换期间另一个播放角色已被外部修改。");
                        }
                        r.Coupled=true;
                    } else if(!DeviceSelector.Same(beforePeer,sourceId))r.Independent=true;
                }
                if(!DeviceSelector.Same(backend.Get(i),sourceId)) throw new IOException("系统未确认默认输出切换。");
                x.Phase="owned";expected[i]=sourceId;journal.Write(r);
            }
        } catch(Exception cause) {
            try { Restore(); } catch(Exception rollback) {
                if(rollback is DeviceUnavailableException) throw;
                throw new ConfigurationException("路由接管失败，恢复记录仍待处理。",new AggregateException(cause,rollback));
            }
            throw;
        }
    }
    public void Restore() {
        if(active==null) return;
        ReconcileRestoreWrite();
        if(RestoreLinked()) return;
        Exception first=null;
        for(int i=1;i>=0;i--) {
            try { RestoreRole(i); }
            catch(Exception ex) { if(first==null || first is DeviceUnavailableException) first=ex; }
            if(active.Roles[0].Phase=="linked-conflict") break;
        }
        if(first is DeviceUnavailableException) throw first;
        if(first!=null) throw new ConfigurationException("普通输出尚未完全恢复："+first.Message,first);
        journal.Clear();active=null;
    }
    string PeerDestination(RouteEntry peer,string current) {
        if(current!=null && !DeviceSelector.Same(current,active.Source)) return current;
        try {return Destination(peer);}
        catch(DeviceUnavailableException) {return null;}
    }
    void SetRestoredRole(int role,string target,string peerBefore,string peerDestination) {
        var r=active;
        if(r.PendingWrite!=null) throw new ConfigurationException("上一次默认输出恢复尚未确认。");
        r.PendingWrite=new RestoreWrite {
            Role=role,Target=target,PeerBefore=peerBefore,PeerDestination=peerDestination
        };
        journal.Write(r);
        if(!DeviceSelector.Same(backend.Get(role),r.Source) ||
            !DeviceSelector.Same(backend.Get(1-role),peerBefore))
            throw new DeviceUnavailableException("普通输出恢复期间系统设备已更改，将重新核对。");
        backend.Set(role,target);
        ReconcileRestoreWrite();
    }
    void ReconcileRestoreWrite() {
        var r=active;
        if(r.PendingWrite==null) return;
        var pending=r.PendingWrite;
        int role=pending.Role, peer=1-role;
        string ownNow=backend.Get(role), peerNow=backend.Get(peer);
        bool peerChanged=!DeviceSelector.Same(peerNow,pending.PeerBefore);
        bool ownApplied=DeviceSelector.Same(ownNow,pending.Target);
        bool peerFollowed=peerChanged && ownApplied && DeviceSelector.Same(peerNow,pending.Target);
        if(peerFollowed) {
            r.Coupled=true;
            string required=pending.PeerDestination;
            if(required==null) required=Destination(r.Roles[peer]);
            if(!DeviceSelector.Same(required,pending.Target)) {
                // A completed or previously untouched role was also changed by this
                // setter. Retain both preimages, including across a crash after Set.
                r.PendingWrite=null;
                LinkedConflict(peerNow);
            }
            r.Roles[peer].Phase="done";r.Roles[peer].Destination=peerNow;
            if(expected!=null)expected[peer]=peerNow;
        } else if(peerChanged) {
            conflict=true;r.External=true;
            r.Roles[peer].Phase="external";r.Roles[peer].Destination=peerNow;
        }
        if(ownApplied) {
            r.Roles[role].Phase="done";r.Roles[role].Destination=ownNow;
            if(expected!=null)expected[role]=ownNow;
        } else if(!DeviceSelector.Same(ownNow,r.Source)) {
            conflict=true;r.External=true;
            r.Roles[role].Phase="external";r.Roles[role].Destination=ownNow;
        }
        r.PendingWrite=null;
        journal.Write(r);
    }
    string Destination(RouteEntry role) {
        var r=active;
        string dest=(!DeviceSelector.Same(r.Source,role.Preferred) && backend.Available(role.Preferred)) ? role.Preferred : role.Prior;
        if(DeviceSelector.Same(r.Source,dest) || !backend.Available(dest)) dest=r.Fallback;
        if(DeviceSelector.Same(r.Source,dest) || !backend.Available(dest))
            throw new DeviceUnavailableException("原播放设备及普通回退设备均不可用。");
        return dest;
    }
    void LinkedConflict(string observed) {
        var r=active;
        r.PendingWrite=null;
        for(int i=0;i<2;i++){r.Roles[i].Phase="linked-conflict";r.Roles[i].Destination=observed;}
        conflict=true;journal.Write(r);
        throw new ConfigurationException("Windows 联动了两个播放角色，无法分别恢复原设备。"+
            "已保留普通输出及恢复记录。请在 Windows 选择另一个普通设备，再点击暂停；然后可按需手动选择。");
    }
    bool RestoreLinked() {
        var r=active;
        string[] now={backend.Get(0),backend.Get(1)};
        if(r.Roles[0].Phase=="linked-conflict") {
            bool changed=false;
            for(int i=0;i<2;i++) {
                if(now[i]==null || DeviceSelector.Same(now[i],r.Source))
                    throw new ConfigurationException("请在 Windows 为两个播放角色选择普通输出，再点击暂停。");
                if(!DeviceSelector.Same(now[i],r.Roles[i].Destination))changed=true;
            }
            if(!changed) throw new ConfigurationException("Windows 联动恢复存在不同原目标。请选择另一个普通输出，再点击暂停。");
            conflict=true;journal.Clear();active=null;return true;
        }
        // If the process stopped before the first setter's acknowledgement was saved,
        // both roles newly pointing to the virtual endpoint identifies the same risk.
        if(!r.Coupled && r.Roles[1].Phase=="applying" && !DeviceSelector.Same(r.Source,r.Roles[1].Prior)
            && DeviceSelector.Same(now[1],r.Source) && DeviceSelector.Same(now[0],r.Source)
            && r.Roles[0].Phase=="applying")r.Coupled=true;
        if(!r.Coupled && r.Roles[0].Phase=="restoring" && r.Roles[1].Phase=="restoring" &&
            DeviceSelector.Same(r.Roles[0].Destination,r.Roles[1].Destination) &&
            DeviceSelector.Same(now[0],r.Roles[0].Destination) && DeviceSelector.Same(now[1],now[0]))r.Coupled=true;
        if(!r.Coupled || r.Roles[0].Phase=="prepared" || r.Roles[1].Phase=="prepared")return false;
        // Resolve previous in-flight writes before choosing any new destinations.
        for(int i=0;i<2;i++) {
            var x=r.Roles[i];
            if(x.Phase=="restoring" && DeviceSelector.Same(now[i],x.Destination))x.Phase="done";
        }
        bool owns0=DeviceSelector.Same(now[0],r.Source),owns1=DeviceSelector.Same(now[1],r.Source);
        conflict=conflict || r.External;
        if(!owns0 && !owns1) {
            for(int i=0;i<2;i++) if(r.Roles[i].Phase!="done" || !DeviceSelector.Same(now[i],r.Roles[i].Destination)) conflict=true;
            if(!conflict && DeviceSelector.Same(now[0],now[1]) && !DeviceSelector.Same(Destination(r.Roles[0]),Destination(r.Roles[1])))
                LinkedConflict(now[0]);
            if(expected!=null)Array.Copy(now,expected,2);
            journal.Clear();active=null;return true;
        }
        int role=owns0?0:1, peer=1-role;
        string target;
        bool external=!DeviceSelector.Same(now[peer],r.Source);
        if(external) {
            target=now[peer];conflict=true;r.External=true;
            if(target==null || !backend.Available(target)) throw new DeviceUnavailableException("等待已选择的普通播放设备。");
        } else target=Destination(r.Roles[role]);
        string peerTarget=PeerDestination(r.Roles[peer],now[peer]);
        for(int i=0;i<2;i++) {r.Roles[i].Destination=target;r.Roles[i].Phase="restoring";}
        SetRestoredRole(role,target,now[peer],peerTarget);
        var a=backend.Get(role);var b=backend.Get(peer);
        if(!DeviceSelector.Same(a,target))throw new IOException("系统未确认联动角色恢复。");
        if(DeviceSelector.Same(b,r.Source)) {
            // Coupling may vary over time. Re-check the peer independently.
            r.Coupled=false;r.Roles[role].Phase="done";r.Roles[peer].Phase="owned";journal.Write(r);return false;
        }
        if(expected!=null){expected[role]=a;expected[peer]=b;}
        if(!DeviceSelector.Same(b,target))conflict=true;
        else if(!external && !DeviceSelector.Same(Destination(r.Roles[peer]),target))LinkedConflict(target);
        journal.Clear();active=null;return true;
    }
    void RestoreRole(int i) {
        ReconcileRestoreWrite();
        var r=active;var x=r.Roles[i];
        string current=backend.Get(i);
        if(x.Phase=="external") { conflict=true;return; }
        if(x.Phase=="done") {
            if(!DeviceSelector.Same(current,x.Destination)) conflict=true;
            return;
        }
        if(x.Phase=="prepared") {
            // No call for this role was issued. Never roll back an unrelated selection.
            if(!DeviceSelector.Same(current,x.Prior)) conflict=true;
        } else if(!DeviceSelector.Same(current,r.Source)) {
            bool unchanged=x.Phase=="applying" && DeviceSelector.Same(current,x.Prior);
            bool restored=x.Phase=="restoring" && DeviceSelector.Same(current,x.Destination);
            if(!unchanged && !restored) conflict=true;
        } else {
            int peer=1-i;
            var other=r.Roles[peer];
            string peerBefore=backend.Get(peer);
            bool peerOwned=DeviceSelector.Same(peerBefore,r.Source) &&
                (other.Phase=="owned" || other.Phase=="applying" || other.Phase=="restoring");
            bool peerExternal=!DeviceSelector.Same(peerBefore,r.Source) && peerBefore!=null &&
                (other.Phase=="external" || (other.Phase=="owned") ||
                (other.Phase=="restoring" && !DeviceSelector.Same(other.Destination,peerBefore)) ||
                (other.Phase=="done" && !DeviceSelector.Same(other.Destination,peerBefore)) ||
                ((other.Phase=="prepared" || other.Phase=="applying") && !DeviceSelector.Same(other.Prior,peerBefore)));
            // The user's per-role destination takes priority if this Windows setter
            // is later discovered to affect both roles.
            string dest=peerExternal?peerBefore:Destination(x);
            if(peerExternal){conflict=true;r.External=true;}
            string peerTarget=PeerDestination(other,peerBefore);
            if(peerOwned){other.Destination=dest;other.Phase="restoring";}
            x.Destination=dest;x.Phase="restoring";
            SetRestoredRole(i,dest,peerBefore,peerTarget);
            current=backend.Get(i);
            if(DeviceSelector.Same(current,r.Source)) throw new IOException("系统未确认普通输出恢复。");
            if(!DeviceSelector.Same(current,dest)) conflict=true;
        }
        x.Destination=current;
        x.Phase=conflict?"external":"done";
        if(expected!=null) expected[i]=current;
        journal.Write(r);
    }
    public void Dispose() {
        // The coordinator owns restoring before disposal. A failed restoration preserves its journal.
        var disposable=backend as IDisposable;
        if(disposable!=null) disposable.Dispose();
    }
}
