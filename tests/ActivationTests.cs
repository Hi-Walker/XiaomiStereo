using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public sealed class ActivationArgumentsProbe : MarshalByRefObject {
    public bool ParseWithoutDefaultConfig() {
        var launch=StereoProgram.ParseArguments(new string[0]);
        return !launch.Startup && launch.Directory==null;
    }
}

// Isolated named events and test-only off-screen forms. Never creates an audio coordinator or real tray.
internal static class ActivationTests {
    static bool observedNativeVisibilityMismatch;
    [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process,int flag);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] struct StartupInfo {
        internal uint Size;internal IntPtr Reserved,Desktop,Title;
        internal uint X,Y,XSize,YSize,XChars,YChars,Fill,Flags;
        internal ushort Show,ReservedBytes;internal IntPtr ReservedData,Input,Output,Error;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern void GetStartupInfo(out StartupInfo info);
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {
        internal IntPtr Process,Thread;internal uint ProcessId,ThreadId;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(
        string application,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inheritHandles,
        uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo process);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    sealed class OffscreenForm : Form {
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {
            get {var p=base.CreateParams;p.ExStyle|=0x08000000;return p;} // WS_EX_NOACTIVATE
        }
        internal OffscreenForm() {
            StartPosition=FormStartPosition.Manual;Location=new Point(-30000,-30000);
            ShowInTaskbar=false;ClientSize=new Size(260,100);Text="Activation regression only";
        }
    }
    static void Check(bool test,string message){if(!test)throw new Exception(message);}
    static void RunChild(string name,bool startup) {
        string self=Assembly.GetExecutingAssembly().Location;
        var info=new ProcessStartInfo(self,"--signal "+name+(startup?" --startup":"")) {
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden
        };
        using(var child=Process.Start(info)) {
            if(!child.WaitForExit(5000)){child.Kill();throw new Exception("Activation test child timed out");}
            Check(child.ExitCode==0,"Activation test child failed");
        }
    }
    static void TestHiddenStartup() {
        string self=Assembly.GetExecutingAssembly().Location;
        var info=new StartupInfo{Size=(uint)Marshal.SizeOf(typeof(StartupInfo)),Flags=1,Show=0};
        ProcessInfo child;
        Check(CreateProcess(self,new StringBuilder("\""+self+"\" --first-window"),IntPtr.Zero,IntPtr.Zero,false,
            0x08000000,IntPtr.Zero,null,ref info,out child),"CreateProcess hidden-start fixture failed");
        try {
            if(WaitForSingleObject(child.Process,5000)!=0){TerminateProcess(child.Process,1);throw new Exception("Hidden-start fixture timed out");}
            uint code;Check(GetExitCodeProcess(child.Process,out code) && (code==0 || code==3),"Hidden-start fixture failed");
            Console.WriteLine("PASS STARTUPINFO SW_HIDE first form: native visibility after Show={0}; after restore=True, iconic=False",code==0);
        }finally {
            CloseHandle(child.Thread);CloseHandle(child.Process);
        }
    }
    static int FirstWindowFixture() {
        StartupInfo startup;GetStartupInfo(out startup);
        Check((startup.Flags&1)!=0 && startup.Show==0,"fixture did not inherit STARTF_USESHOWWINDOW/SW_HIDE");
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using(var form=new OffscreenForm()) {
            form.Show();
            bool initiallyVisible=IsWindowVisible(form.Handle);
            StereoTray.RestoreWindow(form,false);
            Check(IsWindowVisible(form.Handle) && !IsIconic(form.Handle),"first window stayed hidden after SW_HIDE startup");
            return initiallyVisible?0:3;
        }
    }
    static void TestArgumentsAndSignals() {
        var manual=StereoProgram.ParseArguments(new string[0]);
        var startup=StereoProgram.ParseArguments(new[]{"--startup"});
        Check(!manual.Startup && manual.Directory==null && startup.Startup && startup.Directory==null,"manual/startup policy");
        string explicitPath=Path.GetFullPath("activation-state");
        var mixed=StereoProgram.ParseArguments(new[]{"--state-dir",explicitPath,"--startup"});
        Check(mixed.Startup && mixed.Directory==explicitPath,"combined parameters");
        foreach(var args in new[]{new[]{"--unknown"},new[]{"--state-dir"}}) {
            bool rejected=false;try{StereoProgram.ParseArguments(args);}catch(ArgumentException){rejected=true;}
            Check(rejected,"invalid launch arguments accepted");
        }
        string name=@"Local\XiaomiStereo.Activation.Test."+Guid.NewGuid().ToString("N");
        using(var receiver=new ActivationSignal(name)) {
            Check(!receiver.Consume(),"new signal was set");
            RunChild(name,true);Check(!receiver.Consume(),"startup duplicate requested UI");
            // A sender can exit before the first UI timer poll; the fixed event retains the request.
            RunChild(name,false);Check(receiver.Consume() && !receiver.Consume(),"early manual launch was lost or repeated");
            using(var sender=new ActivationSignal(name)) {
                for(int i=0;i<100;i++)sender.Request();
                Check(receiver.Consume() && !receiver.Consume(),"repeated launches were not coalesced");
                StereoProgram.ActivateExisting(startup,sender);
                Check(!receiver.Consume(),"startup duplicate woke existing panel");
            }
            RunChild(name,false);Check(receiver.Consume() && !receiver.Consume(),"later manual launch lost");
        }
        Console.WriteLine("PASS activation: pure argument parsing, startup silence, cross-process manual wake before/after first poll, 100 requests coalesced");
    }
    static void TestBadDefaultDoesNotBlockActivation() {
        string directory=Path.Combine(Path.GetTempPath(),"XiaomiStereo-Activation-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file=Path.Combine(directory,"invalid-default.config");
        try {
            File.WriteAllText(file,"<configuration><appSettings><add key=\"StateDirectory\" value=\"bad&lt;path\"/></appSettings></configuration>");
            var setup=new AppDomainSetup{ApplicationBase=AppDomain.CurrentDomain.BaseDirectory,ConfigurationFile=file};
            var domain=AppDomain.CreateDomain("ActivationWithInvalidDefault",null,setup);
            try {
                var probe=(ActivationArgumentsProbe)domain.CreateInstanceAndUnwrap(typeof(ActivationArgumentsProbe).Assembly.FullName,typeof(ActivationArgumentsProbe).FullName);
                Check(probe.ParseWithoutDefaultConfig(),"activation read invalid default settings before instance check");
            }finally{AppDomain.Unload(domain);}
        }finally{File.Delete(file);Directory.Delete(directory);}
        Console.WriteLine("PASS manual activation argument parsing does not resolve invalid default state directory");
    }
    static void RestoreForms() {
        using(var main=new OffscreenForm())
        using(var devices=new OffscreenForm())
        using(var status=new OffscreenForm()) {
            main.Show();devices.Show(main);status.Show(main);Application.DoEvents();
            int forms=Application.OpenForms.Count;
            var windows=new[]{main.Handle,devices.Handle,status.Handle};
            foreach(var form in new Form[]{main,devices,status}) {
                form.Hide();StereoTray.RestoreWindow(form,false);
                Check(form.Visible && form.WindowState==FormWindowState.Normal,"hidden window did not reappear");
                form.WindowState=FormWindowState.Minimized;
                StereoTray.RestoreWindow(form,false);
                Check(form.Visible && form.WindowState==FormWindowState.Normal,"minimized window did not restore");
                ShowWindow(form.Handle,0); // Native hide, independent of WinForms' cached Visible state.
                Check(!IsWindowVisible(form.Handle) && !IsIconic(form.Handle),"native hide setup failed");
                observedNativeVisibilityMismatch|=form.Visible;
                StereoTray.RestoreWindow(form,false);
                Check(IsWindowVisible(form.Handle) && !IsIconic(form.Handle),"native hidden HWND did not restore");
                ShowWindow(form.Handle,7); // SW_SHOWMINNOACTIVE: native minimize without activating a test window.
                Check(IsIconic(form.Handle),"native minimize setup failed");
                StereoTray.RestoreWindow(form,false);
                Check(IsWindowVisible(form.Handle) && !IsIconic(form.Handle),"native minimized HWND did not restore");
                for(int i=0;i<20;i++)StereoTray.RestoreWindow(form,false);
                Check(Application.OpenForms.Count==forms,"reopening created extra windows");
            }
            main.WindowState=FormWindowState.Minimized;devices.WindowState=FormWindowState.Minimized;
            StereoTray.RestoreWindow(devices,false);
            Check(main.WindowState==FormWindowState.Normal && devices.WindowState==FormWindowState.Normal,"owned window did not restore owner");
            // Exercise the disabled-parent branch without entering a real modal loop or taking focus.
            status.Hide();main.Enabled=false;devices.WindowState=FormWindowState.Minimized;
            StereoTray.RestoreWindow(main,false);
            Check(!main.Enabled && devices.Visible && devices.WindowState==FormWindowState.Normal,"disabled parent bypassed or child not restored");
            main.Enabled=true;
            main.Dispose();
            Check(devices.IsDisposed && status.IsDisposed,"owned windows leaked after owner disposal");
            Check(Application.OpenForms.Count==forms-3,"disposed forms remained registered");
            foreach(var window in windows)Check(!IsWindow(window),"disposed native window remained alive");
        }
    }
    static void SettleResources() {
        // Pump for the same fixed duration before every sample. GC alone does
        // not process window/theme messages or allow startup work to settle.
        var settle=Stopwatch.StartNew();
        while(settle.ElapsedMilliseconds<1000){Application.DoEvents();Thread.Sleep(10);}
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Application.DoEvents();
    }
    static void TestWindowsAndResources() {
        RestoreForms();SettleResources();
        using(var process=Process.GetCurrentProcess()) {
            int gdi=GetGuiResources(process.Handle,0),user=GetGuiResources(process.Handle,1);
            process.Refresh();int handles=process.HandleCount;
            for(int batch=1;batch<=3;batch++) {
                for(int i=0;i<20;i++)RestoreForms();
                process.Refresh();
                Console.WriteLine("Batch {0} before draining messages: GDI={1}, USER={2}, handles={3}",batch,GetGuiResources(process.Handle,0),GetGuiResources(process.Handle,1),process.HandleCount);
                SettleResources();process.Refresh();
                int afterGdi=GetGuiResources(process.Handle,0),afterUser=GetGuiResources(process.Handle,1);
                Console.WriteLine("Restore resource sample after {0} cycles: GDI {1}->{2}, USER {3}->{4}, handles {5}->{6}",batch*20,gdi,afterGdi,user,afterUser,handles,process.HandleCount);
                Check(afterGdi<=gdi+2 && afterUser<=user+2 && process.HandleCount<=handles+3,"restore cycles grew resources");
            }
            Console.WriteLine("PASS off-screen main/device/status managed+native hidden/minimized HWND+owned restoration without activation; 60 cycles, disposed HWNDs/forms checked, fixed settling interval and unchanged resource thresholds");
            Console.WriteLine("Native hidden HWND with managed Visible=true observed: "+observedNativeVisibilityMismatch);
        }
    }
    [STAThread] static int Main(string[] args) {
        try {
            if(args.Length==1 && args[0]=="--first-window")return FirstWindowFixture();
            if(args.Length>=2 && args[0]=="--signal") {
                using(var sender=new ActivationSignal(args[1]))
                    StereoProgram.ActivateExisting(StereoProgram.ParseArguments(args.Length==3?new[]{args[2]}:new string[0]),sender);
                return 0;
            }
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            TestHiddenStartup();TestArgumentsAndSignals();TestBadDefaultDoesNotBlockActivation();TestWindowsAndResources();return 0;
        }catch(Exception e){Console.WriteLine(e.GetType().Name+": "+e.Message);return 1;}
    }
}
