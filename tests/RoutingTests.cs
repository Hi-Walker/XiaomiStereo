using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;

// Uses a fresh directory and fake roles. Child mode exits at a precise journal boundary.
static class RoutingTests {
    const string Virtual="virtual",Left="left",Right="right",Other="another-choice";
    static int passed;
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    static void Expect<T>(Action a) where T:Exception {
        try { a(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name);
    }
    static void Run(string title,Action test) { test();Console.WriteLine("PASS "+title);passed++; }
    sealed class FakeRoles : IRoleBackend {
        internal readonly string[] defaults={Left,Right};
        internal readonly HashSet<string> available=new HashSet<string>{Virtual,Left,Right,Other};
        internal int failRole=-1,writes;
        internal bool coupled;
        internal Action<int,string> afterSet;
        internal string directory;
        public string Get(int role) { return defaults[role]; }
        public void Set(int role,string id) {
            writes++;
            if(failRole==role) throw new IOException("injected set failure");
            defaults[role]=id;
            if(coupled)defaults[1-role]=id;
            if(afterSet!=null)afterSet(role,id);
            if(directory!=null) {
                File.WriteAllText(Path.Combine(directory,"child-role-"+role),id);
                if(coupled)File.WriteAllText(Path.Combine(directory,"child-role-"+(1-role)),id);
                if(role==0) Environment.Exit(73); // OS state updated, no acknowledgement persisted.
            }
        }
        public bool Available(string id) { return id!=null && available.Contains(id); }
    }
    sealed class FaultJournal : IRouteJournal {
        readonly FileJournal disk;
        internal bool full=false;
        internal bool failClear;
        internal int failAfter=-1,writes;
        internal FaultJournal(string dir) { disk=new FileJournal(dir); }
        public RouteRecord Read(){return disk.Read();}
        public void Clear(){if(failClear)throw new IOException("injected delete failure");disk.Clear();}
        public void Write(RouteRecord r) {
            if(full || ++writes==failAfter) throw new IOException("injected storage failure");
            disk.Write(r);
        }
    }
    static RouteRecord Pending() {
        var r=new RouteRecord { Source=Virtual,Fallback=Left,Pid=77,StartTicks=12345 };
        r.Roles[0].Prior=Left;r.Roles[0].Phase="owned";r.Roles[1].Prior=Right;
        return r;
    }
    static int Main(string[] args) {
        if(args.Length==2 && (args[0]=="child" || args[0]=="coupled-child")) {
            var roles=new FakeRoles {directory=args[1],coupled=args[0]=="coupled-child"};
            if(roles.coupled) roles.defaults[1]=Left;
            using(var m=new RouteManager(roles,new FileJournal(args[1]),delegate { return false; })) m.Take(Virtual,Left);
            return 1;
        }
        if(args.Length>1) throw new ArgumentException("Pass an optional, non-existing test directory.");
        string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        string name="xiaomi-stereo-test-"+Guid.NewGuid().ToString("N");
        string root=args.Length==0 ? Path.Combine(temp,name) : Path.GetFullPath(args[0]);
        if(Directory.Exists(root)) throw new ArgumentException("Use a fresh directory.");
        Directory.CreateDirectory(root);
        try { TestAll(root);Console.WriteLine("Routing/config total: "+passed+" passed");return 0; }
        catch(Exception e) { Console.Error.WriteLine(e);return 1; }
        finally {
            // Only remove the exact generated direct child of the temp directory.
            // Explicit, caller-chosen test evidence directories remain for inspection.
            if(args.Length==0 && Path.GetDirectoryName(root)==temp && Path.GetFileName(root)==name)
                Directory.Delete(root,true);
        }
    }
    static void TestAll(string root) {
        int index=0;
        Func<string> next=delegate { string d=Path.Combine(root,(++index).ToString());Directory.CreateDirectory(d);return d; };
        Run("distinct roles, normal restore and repeated stop",delegate{
            var f=new FakeRoles();var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})) {
                m.Take(Virtual,Left);Check(f.Get(0)==Virtual && f.Get(1)==Virtual,"acquisition");
                int calls=f.writes;Expect<ConfigurationException>(()=>m.Take(Virtual,Left));Check(f.writes==calls,"second take");
                m.Restore();m.Restore();Check(f.Get(0)==Left && f.Get(1)==Right && j.Read()==null,"restore");
            }
        });
        Run("first role switched, second role set fails",delegate{
            var f=new FakeRoles {failRole=1};var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                Expect<IOException>(()=>m.Take(Virtual,Left));
                Check(f.Get(0)==Left && f.Get(1)==Right && j.Read()==null,"partial rollback");
            }
        });
        Run("Win11 linked setters: one write acquires both and one restores both",delegate{
            var f=new FakeRoles {coupled=true};f.defaults[1]=Left;var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.Take(Virtual,Left);
                Check(f.Get(0)==Virtual && f.Get(1)==Virtual && f.writes==1 && j.Read().Coupled,"linked acquire");
                Check(!m.ExternalChange(),"own side effect classified as external");
                m.Restore();Check(f.Get(0)==Left && f.Get(1)==Left && f.writes==2 && !m.ExternalChange(),"linked restore");
            }
        });
        Run("Win11 linked setters: existing user override wins",delegate{
            var f=new FakeRoles {coupled=true};f.defaults[1]=Left;var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.Take(Virtual,Left);f.defaults[1]=Other;Check(m.ExternalChange(),"missing user override");
                m.Restore();Check(f.Get(0)==Other && f.Get(1)==Other && j.Read()==null,"user override replaced");
            }
        });
        Run("Win11 linked setters: original roles already virtual, fallback once",delegate{
            var f=new FakeRoles {coupled=true};f.defaults[0]=f.defaults[1]=Virtual;var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.Take(Virtual,Left);Check(f.writes==0,"unexpected virtual rewrite");
                m.Restore();Check(f.Get(0)==Left && f.Get(1)==Left && f.writes==1 && !m.ExternalChange(),"false restore conflict");
            }
        });
        Run("Win11 linked setters: split prior, persistent conflict, no retry writes",delegate{
            var f=new FakeRoles {coupled=true};var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.Take(Virtual,Left);Expect<ConfigurationException>(()=>m.Restore());
                int writes=f.writes;Check(j.Read().Roles[1].Prior==Right && writes==2,"split priors discarded");
                Expect<ConfigurationException>(()=>m.Restore());Check(f.writes==writes,"repeated writes");
                f.defaults[0]=f.defaults[1]=Other;m.Restore();Check(j.Read()==null && f.Get(1)==Other,"user acknowledgement");
            }
        });
        Run("Win11 linked setters: crash before first acknowledgement restores both",delegate{
            string dir=next();string exe=System.Reflection.Assembly.GetExecutingAssembly().Location;
            var p=new ProcessStartInfo(exe,"coupled-child \""+dir+"\"") {UseShellExecute=false,CreateNoWindow=true};
            using(var child=Process.Start(p)){Check(child.WaitForExit(12000) && child.ExitCode==73,"linked child exit");}
            var f=new FakeRoles {coupled=true};var j=new FileJournal(dir);
            for(int i=0;i<2;i++)f.defaults[i]=File.ReadAllText(Path.Combine(dir,"child-role-"+i));
            using(var m=new RouteManager(f,j,delegate{return false;})){
                Check(!m.RecoverLegacy() && j.Read()==null && f.Get(0)==Left && f.Get(1)==Left,"linked crash restoration");
            }
        });
        Run("no OS writes after journal admission failure",delegate{
            var f=new FakeRoles();var j=new FaultJournal(next()) {full=true};
            using(var m=new RouteManager(f,j,delegate{return false;})){
                Expect<IOException>(()=>m.Take(Virtual,Left));Check(f.writes==0,"disk full");
            }
        });
        Run("ack journal failure after successful OS call",delegate{
            var f=new FakeRoles();var j=new FaultJournal(next()) {failAfter=3};
            using(var m=new RouteManager(f,j,delegate{return false;})){
                Expect<IOException>(()=>m.Take(Virtual,Left));Check(f.Get(0)==Left && f.Get(1)==Right && j.Read()==null,"post-write failure");
            }
        });
        Run("user manual switch preserved and remaining virtual role released safely",delegate{
            var f=new FakeRoles();var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.Take(Virtual,Left);f.defaults[0]=Other;Check(m.ExternalChange(),"external detection");m.Restore();
                Check(f.Get(0)==Other && f.Get(1)==Other,"manual selections");Expect<RouteConflictException>(()=>m.Take(Virtual,Left));
                m.ForgetIntent();m.Take(Virtual,Left);m.Restore();Check(f.Get(0)==Other,"new intent");
            }
        });
        Run("user changes during waiting, no reacquire",delegate{
            var f=new FakeRoles();var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.SnapshotIntent();f.defaults[1]=Other;Expect<RouteConflictException>(()=>m.Take(Virtual,Left));Check(f.writes==0,"waiting");
            }
        });
        Run("historical independent setters cannot override a later user choice",delegate{
            var f=new FakeRoles();var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.Take(Virtual,Left);Check(j.Read().Independent,"independent baseline missing");
                f.coupled=true;f.defaults[0]=Other;
                m.Restore();
                Check(f.Get(0)==Other && f.Get(1)==Other && j.Read()==null,"external choice overwritten");
            }
        });
        Run("mixed defaults before first set: linked restore retains both originals",delegate{
            var f=new FakeRoles {coupled=true};f.defaults[0]=Virtual;
            var record=Pending();record.Roles[0].Prior=record.Roles[0].Preferred=Virtual;
            record.Roles[0].Phase=record.Roles[1].Phase="applying";
            var j=new FileJournal(next());j.Write(record);
            using(var m=new RouteManager(f,j,delegate{return false;})){
                Expect<ConfigurationException>(()=>m.RecoverLegacy());
                var saved=j.Read();
                Check(saved!=null && saved.Roles[1].Prior==Right &&
                    saved.Roles[0].Phase=="linked-conflict","lost ordinary peer recovery");
                int writes=f.writes;Expect<ConfigurationException>(()=>m.Restore());
                Check(f.writes==writes,"conflict keeps changing defaults");
                f.defaults[0]=f.defaults[1]=Other;m.Restore();
                Check(j.Read()==null,"user could not resolve linked conflict");
            }
        });
        Run("mixed defaults crash after linked restore set preserves original peer",delegate{
            var f=new FakeRoles {coupled=true};f.defaults[0]=Virtual;
            var record=Pending();record.Roles[0].Prior=record.Roles[0].Preferred=Virtual;
            record.Roles[0].Phase=record.Roles[1].Phase="applying";
            var j=new FileJournal(next());j.Write(record);
            RouteRecord crash=null;
            f.afterSet=delegate {crash=j.Read();};
            using(var m=new RouteManager(f,j,delegate{return false;})){
                Expect<ConfigurationException>(()=>m.RecoverLegacy());
            }
            Check(crash!=null && crash.PendingWrite!=null && crash.PendingWrite.PeerDestination==Right,"missing write preimage");
            var replay=new FileJournal(next());replay.Write(crash);
            f.afterSet=null;int calls=f.writes;
            using(var m=new RouteManager(f,replay,delegate{return false;})){
                Expect<ConfigurationException>(()=>m.RecoverLegacy());
                Check(f.writes==calls && replay.Read().Roles[1].Prior==Right,"crash retried setter or lost peer");
            }
        });
        Run("restoration acknowledgement write failure recovers without repeating setter",delegate{
            var f=new FakeRoles {coupled=true};f.defaults[1]=Left;
            string d=next();var j=new FaultJournal(d);
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.Take(Virtual,Left);
                f.afterSet=delegate {j.full=true;};
                Expect<IOException>(()=>m.Restore());
            }
            var disk=new FileJournal(d);
            Check(disk.Read().PendingWrite!=null,"lost crash boundary after failed acknowledgement");
            f.afterSet=null;int calls=f.writes;
            using(var m=new RouteManager(f,disk,delegate{return false;})){
                Check(!m.RecoverLegacy() && f.writes==calls && disk.Read()==null,"replayed confirmed restore");
            }
        });
        Run("journal deletion failure retains completed state and retries without setters",delegate{
            foreach(bool linked in new[]{false,true}) {
                var f=new FakeRoles {coupled=linked};if(linked)f.defaults[1]=Left;
                var j=new FaultJournal(next());
                using(var m=new RouteManager(f,j,delegate{return false;})){
                    m.Take(Virtual,Left);j.failClear=true;
                    Expect<IOException>(()=>m.Restore());
                    Check(j.Read()!=null,"delete failure lost debt");
                    int calls=f.writes;j.failClear=false;m.Restore();m.Restore();
                    Check(f.writes==calls && j.Read()==null,"delete retry changed defaults");
                }
            }
        });
        Run("fault fallback returns then reconnects under original intent",delegate{
            var f=new FakeRoles();var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.Take(Virtual,Left);m.Restore();Check(!m.ExternalChange(),"own removal treated as external");
                m.Take(Virtual,Left);m.Restore();Check(f.Get(0)==Left && f.Get(1)==Right,"lost prior");
            }
        });
        Run("missing original and fallback preserve recovery for retry",delegate{
            var f=new FakeRoles();var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.Take(Virtual,Left);f.available.Remove(Left);Expect<DeviceUnavailableException>(()=>m.Restore());
                Check(j.Read()!=null && f.Get(0)==Virtual && f.Get(1)==Right,"lost debt");
                f.available.Add(Left);m.Restore();Check(f.Get(0)==Left && j.Read()==null,"retry");
            }
        });
        Run("reacquisition retains original device after a temporary fallback",delegate{
            var f=new FakeRoles();f.defaults[0]=Other;var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.Take(Virtual,Left);f.available.Remove(Other);m.Restore();
                Check(f.Get(0)==Left,"fallback activation");
                m.Take(Virtual,Left);
                var persisted=j.Read().Roles[0];
                Check(persisted.Preferred==Other && persisted.Prior==Left,"lost original across reacquisition");
                f.available.Add(Other);m.Restore();Check(f.Get(0)==Other,"did not restore original selection");
            }
        });
        Run("ordinary fallback when prior was virtual",delegate{
            var f=new FakeRoles();f.defaults[0]=Virtual;var j=new FileJournal(next());
            using(var m=new RouteManager(f,j,delegate{return false;})){
                Expect<ConfigurationException>(()=>m.Take(Virtual,null));
                m.Take(Virtual,Left);m.Restore();Check(f.Get(0)==Left,"virtual fallback");
            }
        });
        Run("live owner denied and reused PID with different start admitted",delegate{
            var f=new FakeRoles();f.defaults[0]=Virtual;var j=new FileJournal(next());j.Write(Pending());
            using(var m=new RouteManager(f,j,(id,t)=>id==77 && t==12345)){
                Expect<ConfigurationException>(()=>m.RecoverLegacy());Check(j.Read()!=null && f.writes==0,"live owner");
            }
            using(var m=new RouteManager(f,j,(id,t)=>id==77 && t==99999)){
                Check(!m.RecoverLegacy() && f.Get(0)==Left && j.Read()==null,"reused pid");
            }
        });
        Run("real subprocess exit between set and journal confirmation (fake devices)",delegate{
            string d=next(), exe=System.Reflection.Assembly.GetExecutingAssembly().Location;
            var start=new ProcessStartInfo(exe,"child \""+d+"\"") { UseShellExecute=false,CreateNoWindow=true };
            using(var child=Process.Start(start)) {
                Check(child.WaitForExit(8000) && child.ExitCode==73,"child did not stop at boundary");
            }
            var f=new FakeRoles();f.defaults[0]=File.ReadAllText(Path.Combine(d,"child-role-0"));
            var j=new FileJournal(d);Check(j.Read().Roles[0].Phase=="applying","write ahead");
            using(var m=new RouteManager(f,j,delegate{return false;})){
                m.RecoverLegacy();Check(f.Get(0)==Left && f.Get(1)==Right && j.Read()==null,"crash recovery");
            }
        });
        Run("external choice after crash yields pause indication",delegate{
            var j=new FileJournal(next());j.Write(Pending());var f=new FakeRoles();f.defaults[0]=Other;
            using(var m=new RouteManager(f,j,delegate{return false;})){
                Check(m.RecoverLegacy() && f.Get(0)==Other && j.Read()==null,"external legacy");
            }
        });
        Run("legacy v1 journal without optional metadata remains recoverable",delegate{
            string d=next(),path=Path.Combine(d,"recovery.json");
            var j=new FileJournal(d);j.Write(Pending());
            var fields=StateFile.Parse(File.ReadAllBytes(path),FileJournal.MaxBytes);
            foreach(string key in new[]{"pendingWrite","coupled","independent","external"})fields.Remove(key);
            foreach(string role in new[]{"Console","Multimedia"})
                ((Dictionary<string,object>)fields[role]).Remove("preferred");
            StateFile.Write(path,FileJournal.MaxBytes,fields);
            var f=new FakeRoles();f.defaults[0]=Virtual;
            using(var m=new RouteManager(f,j,delegate{return false;})){
                Check(!m.RecoverLegacy() && f.Get(0)==Left && f.Get(1)==Right && j.Read()==null,"legacy recovery");
            }
        });
        Run("pending restore metadata rejects invalid roles, targets and lengths",delegate{
            foreach(int invalid in new[]{0,1,2,3,4}) {
                string d=next(),path=Path.Combine(d,"recovery.json");var j=new FileJournal(d);
                var record=Pending();
                record.PendingWrite=new RestoreWrite {Role=0,Target=Left,PeerBefore=Virtual,PeerDestination=Right};
                if(invalid==0)record.PendingWrite.Role=-1;
                if(invalid==1)record.PendingWrite.Role=2;
                if(invalid==2)record.PendingWrite.Target=Virtual;
                if(invalid==3)record.PendingWrite.PeerDestination=Virtual;
                if(invalid==4)record.PendingWrite.Target=new string('x',2049);
                j.Write(record);string before=File.ReadAllText(path);var f=new FakeRoles();
                using(var m=new RouteManager(f,j,delegate{return false;})){
                    Expect<ConfigurationException>(()=>m.RecoverLegacy());
                    Check(f.writes==0 && File.ReadAllText(path)==before,"invalid metadata was acted on");
                }
            }
        });
        Run("device IDs: unique bootstrap, ambiguity, duplicate names, container migration",delegate{
            var one=new EndpointChoice("A","Pro-LEFT","container-a","usb");
            var two=new EndpointChoice("B","Pro-LEFT","container-b","usb");
            var src=new EndpointChoice("S","custom label",null,@"ROOT\SteamStreamingSpeakers");
            Check(DeviceSelector.Pick(new[]{one},null,null,"Pro-LEFT",false)==one,"bootstrap");
            Expect<ConfigurationException>(()=>DeviceSelector.Pick(new[]{one,two},null,null,"Pro-LEFT",false));
            Check(DeviceSelector.Pick(new[]{one,two},"B",null,"Pro-LEFT",false)==two,"ID first");
            Check(DeviceSelector.Pick(new[]{one,two},"OLD","container-a","Pro-LEFT",false)==one,"container rebound");
            Expect<DeviceUnavailableException>(()=>DeviceSelector.Pick(new[]{one,two},"gone",null,"Pro-LEFT",false));
            Check(DeviceSelector.Pick(new[]{one,src},null,null,"source",true)==src,"matchingid");
        });
        Run("both speaker fallbacks follow verified endpoint identity changes",delegate{
            var devices=new DeviceSet {
                SourceInfo=new EndpointChoice("source-new","source",null,null),
                LeftInfo=new EndpointChoice("left-new","Pro-LEFT",null,null),
                RightInfo=new EndpointChoice("right-new","Pro-RIGHT",null,null)
            };
            foreach(string fallback in new[]{Left,Right,Other}) {
                var cfg=new AppSettings {SourceId=Virtual,LeftId=Left,RightId=Right,FallbackId=fallback};
                Check(devices.UpdateIdentity(cfg),"identity change not reported");
                string expected=fallback==Left?"left-new":fallback==Right?"right-new":Other;
                Check(cfg.FallbackId==expected && cfg.RightId=="right-new","fallback identity lost");
                Check(!devices.UpdateIdentity(cfg),"unchanged identity wrote settings");
            }
            var initial=new AppSettings();devices.UpdateIdentity(initial);
            Check(initial.FallbackId=="left-new","first-run left fallback changed");
        });
        Run("settings round-trip, no writes for equivalent values, stale temp replaced",delegate{
            string d=next(),path=Path.Combine(d,"settings.json");
            var c=new ConfigStore(d);var v=c.Load();v.SwapChannels=true;v.BalanceDb=-2.5;v.LeftId="left";
            File.WriteAllText(path+".tmp","interrupted new settings");c.Save(v);
            File.SetLastWriteTimeUtc(path,new DateTime(2010,1,1,0,0,0,DateTimeKind.Utc));
            c.Save(v);Check(File.GetLastWriteTimeUtc(path).Year==2010,"redundant overwrite");
            v=c.Load();Check(v.SwapChannels && v.BalanceDb==-2.5 && v.LeftId=="left","roundtrip");
            Check(Directory.GetFiles(d).Length==1,"file accumulation");
            v.BalanceDb=Double.NaN;Expect<ConfigurationException>(()=>c.Save(v));Check(c.Load().BalanceDb==-2.5,"lost valid settings");
        });
        Run("strict versions, unknown keys, bad JSON, IDs and payload limits",delegate{
            string d=next(), path=Path.Combine(d,"settings.json");
            var c=new ConfigStore(d);var v=c.Load();
            var valid=File.ReadAllText(path);
            File.WriteAllText(path,valid.Replace("\"schemaVersion\":1","\"schemaVersion\":42"));Expect<ConfigurationException>(()=>c.Load());
            File.WriteAllText(path,"{\"unknown\":true}");Expect<ConfigurationException>(()=>c.Load());
            File.WriteAllText(path,"[");Expect<ConfigurationException>(()=>c.Load());
            File.WriteAllBytes(path,new byte[ConfigStore.MaxBytes+1]);Expect<ConfigurationException>(()=>c.Load());
            v.LeftId=v.RightId=Left;Expect<ConfigurationException>(()=>c.Save(v));
            var j=new FileJournal(d);File.WriteAllBytes(Path.Combine(d,"recovery.json"),new byte[FileJournal.MaxBytes+1]);
            Expect<ConfigurationException>(()=>j.Read());
            File.WriteAllText(Path.Combine(d,"recovery.json"),"{}");
            using(var m=new RouteManager(new FakeRoles(),j,delegate{return false;})){
                Expect<ConfigurationException>(()=>m.Take(Virtual,Left));
                Check(File.ReadAllText(Path.Combine(d,"recovery.json"))=="{}","journal clobbered");
            }
        });
        Run("recovery read-only temp/disk write failure preserves primary",delegate{
            string d=next(), path=Path.Combine(d,"settings.json");var c=new ConfigStore(d);
            var v=c.Load();var old=File.ReadAllText(path);v.BalanceDb=2;
            Directory.CreateDirectory(path+".tmp");
            Expect<UnauthorizedAccessException>(()=>c.Save(v));Check(File.ReadAllText(path)==old,"old primary changed");
        });
    }
}
