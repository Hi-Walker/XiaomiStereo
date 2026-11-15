using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Maintenance test runner only. Never calls SwitchDesktop or opens the production app.
// Contracts:
// https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/ns-processthreadsapi-startupinfow
// https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute
// https://learn.microsoft.com/en-us/windows/win32/winstation/thread-connection-to-a-desktop
// https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects
public static class PrivateDesktopRunner {
    [StructLayout(LayoutKind.Sequential)] struct SA {
        public int Size; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit;
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct SI {
        public int Size; public string Reserved, Desktop, Title;
        public uint X,Y,XSize,YSize,XChars,YChars,Fill,Flags;
        public short Show, ReservedBytes; public IntPtr ReservedData,Input,Output,Error;
    }
    [StructLayout(LayoutKind.Sequential)] struct SIX { public SI Startup; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] struct PI { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits {
        public long ProcessTime,JobTime; public uint Flags; public UIntPtr MinWorkingSet,MaxWorkingSet;
        public uint ActiveLimit; public UIntPtr Affinity; public uint Priority,Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits {
        public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;
    }
    [StructLayout(LayoutKind.Sequential)] struct Accounting {
        public long User,Kernel,PeriodUser,PeriodKernel; public uint Faults,Total,Active,Terminated;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,uint flags,uint access,ref SA attributes);
    [DllImport("user32.dll",SetLastError=true)] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool GetUserObjectInformation(IntPtr obj,int index,StringBuilder value,int bytes,out int needed);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateFile(string path,uint access,uint sharing,ref SA attributes,uint disposition,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,uint flags,ref IntPtr size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,IntPtr key,IntPtr value,IntPtr size,IntPtr previous,IntPtr returned);
    [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref SIX startup,out PI process);
    [DllImport("kernel32.dll",EntryPoint="CreateProcessW",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcessBasic(string app,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref SI startup,out PI process);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int kind,ref ExtendedLimits info,int size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool QueryInformationJobObject(IntPtr job,int kind,out Accounting info,int size,IntPtr returned);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job,uint code);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateProcess(IntPtr process,uint code);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
    static readonly IntPtr Invalid=new IntPtr(-1);
    static void Check(bool ok,string call) { if(!ok)throw new Win32Exception(Marshal.GetLastWin32Error(),call); }
    static string ObjectName(IntPtr handle) {
        var text=new StringBuilder(512);int needed;
        Check(GetUserObjectInformation(handle,2,text,text.Capacity*2,out needed),"GetUserObjectInformation");return text.ToString();
    }
    static string InputDesktopName() {
        IntPtr input=OpenInputDesktop(0,false,1);
        if(input==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"OpenInputDesktop");
        try{return ObjectName(input);}finally{Check(CloseDesktop(input),"Close input desktop handle");}
    }
    public static string Quote(string value) {
        if(value==null)throw new ArgumentNullException("value");
        var result=new StringBuilder("\"");int backslashes=0;
        foreach(char c in value) {
            if(c=='\\'){backslashes++;continue;}
            if(c=='\"'){result.Append('\\',backslashes*2+1).Append(c);backslashes=0;continue;}
            result.Append('\\',backslashes).Append(c);backslashes=0;
        }
        return result.Append('\\',backslashes*2).Append('"').ToString();
    }
    public sealed class Result {
        public string Executable,Desktop,InputBefore,InputAfter,Stdout,Stderr;
        public uint ProcessId; public int ExitCode; public bool TimedOut,DesktopClosed;
        public long ElapsedMilliseconds;
    }
    public static Result Run(string executable,string[] arguments,string cwd,string stdout,string stderr,int timeoutSeconds) {
        if(timeoutSeconds<1 || timeoutSeconds>1800)throw new ArgumentOutOfRangeException("timeoutSeconds");
        if(!File.Exists(executable))throw new FileNotFoundException("Test executable missing",executable);
        if(!Path.IsPathRooted(executable)||!Path.IsPathRooted(cwd)||!Path.IsPathRooted(stdout)||!Path.IsPathRooted(stderr))throw new ArgumentException("All paths must be absolute");
        Result result=null;Exception failure=null;
        var worker=new Thread(delegate(){try{result=RunCore(executable,arguments??new string[0],cwd,stdout,stderr,timeoutSeconds);}catch(Exception ex){failure=ex;}});
        worker.IsBackground=false;worker.Start();worker.Join();
        if(failure!=null)throw new InvalidOperationException("Private desktop runner failed",failure);
        return result;
    }
    static Result RunCore(string exe,string[] args,string cwd,string output,string error,int timeout) {
        var result=new Result{Executable=exe,Stdout=output,Stderr=error,Desktop="XiaomiStereoTest_"+Guid.NewGuid().ToString("N"),InputBefore=InputDesktopName()};
        var watch=Stopwatch.StartNew();
        IntPtr desktop=IntPtr.Zero,job=IntPtr.Zero,input=IntPtr.Zero,stdout=IntPtr.Zero,stderr=IntPtr.Zero,attributes=IntPtr.Zero,handles=IntPtr.Zero;
        IntPtr originalDesktop=GetThreadDesktop(GetCurrentThreadId());
        bool attributesReady=false,assigned=false;PI process=new PI();
        try {
            var inheritable=new SA{Size=Marshal.SizeOf(typeof(SA)),Inherit=true};
            desktop=CreateDesktop(result.Desktop,IntPtr.Zero,IntPtr.Zero,0,0x10000000,ref inheritable);
            Check(desktop!=IntPtr.Zero,"CreateDesktop");
            if(ObjectName(desktop)==result.InputBefore)throw new InvalidOperationException("Test desktop unexpectedly equals input desktop");
            input=CreateFile("NUL",0x80000000,3,ref inheritable,3,0,IntPtr.Zero);Check(input!=Invalid,"Open NUL");
            stdout=CreateFile(output,0x40000000,3,ref inheritable,2,0,IntPtr.Zero);Check(stdout!=Invalid,"Open stdout");
            stderr=CreateFile(error,0x40000000,3,ref inheritable,2,0,IntPtr.Zero);Check(stderr!=Invalid,"Open stderr");
            job=CreateJobObject(IntPtr.Zero,null);Check(job!=IntPtr.Zero,"CreateJobObject");
            var limits=new ExtendedLimits();limits.Basic.Flags=0x2000; // KILL_ON_JOB_CLOSE, no breakaway.
            Check(SetInformationJobObject(job,9,ref limits,Marshal.SizeOf(typeof(ExtendedLimits))),"SetInformationJobObject");
            IntPtr bytes=IntPtr.Zero;InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref bytes);
            if(bytes==IntPtr.Zero)throw new InvalidOperationException("Attribute-list size unavailable");
            attributes=Marshal.AllocHGlobal(bytes);Check(InitializeProcThreadAttributeList(attributes,1,0,ref bytes),"InitializeProcThreadAttributeList");attributesReady=true;
            handles=Marshal.AllocHGlobal(IntPtr.Size*4);
            Marshal.WriteIntPtr(handles,0,input);Marshal.WriteIntPtr(handles,IntPtr.Size,stdout);Marshal.WriteIntPtr(handles,IntPtr.Size*2,stderr);Marshal.WriteIntPtr(handles,IntPtr.Size*3,desktop);
            Check(UpdateProcThreadAttribute(attributes,0,new IntPtr(0x20002),handles,new IntPtr(IntPtr.Size*4),IntPtr.Zero,IntPtr.Zero),"Restrict inherited handles");
            var startup=new SIX{Startup=new SI{Size=Marshal.SizeOf(typeof(SIX)),Desktop=ObjectName(GetProcessWindowStation())+"\\"+result.Desktop,Flags=0x181,Show=0,Input=input,Output=stdout,Error=stderr},Attributes=attributes};
            var command=new StringBuilder(Quote(exe));foreach(var arg in args)command.Append(' ').Append(Quote(arg));
            Check(CreateProcess(exe,command,IntPtr.Zero,IntPtr.Zero,true,0x08080004,IntPtr.Zero,cwd,ref startup,out process),"CreateProcess suspended on private desktop");
            result.ProcessId=process.ProcessId;
            Check(AssignProcessToJobObject(job,process.Process),"AssignProcessToJobObject");assigned=true;
            Check(ResumeThread(process.Thread)!=0xffffffff,"ResumeThread");
            uint waited=WaitForSingleObject(process.Process,(uint)timeout*1000);
            if(waited==258) {result.TimedOut=true;result.ExitCode=124;Check(TerminateJobObject(job,124),"Terminate timed-out test job");Check(WaitForSingleObject(process.Process,5000)==0,"Wait after termination");}
            else {Check(waited==0,"Wait for test");uint code;Check(GetExitCodeProcess(process.Process,out code),"GetExitCodeProcess");result.ExitCode=unchecked((int)code);}
        } finally {
            // Assignment occurs before execution. On failure only this newly created child is targeted.
            if(process.Process!=IntPtr.Zero) {
                if(assigned)TerminateJobObject(job,125);else TerminateProcess(process.Process,125);
                WaitForSingleObject(process.Process,5000);
            }
            if(job!=IntPtr.Zero) {
                var deadline=Stopwatch.StartNew();Accounting info;
                while(QueryInformationJobObject(job,1,out info,Marshal.SizeOf(typeof(Accounting)),IntPtr.Zero) && info.Active!=0 && deadline.ElapsedMilliseconds<5000)Thread.Sleep(20);
            }
            if(process.Thread!=IntPtr.Zero)CloseHandle(process.Thread);
            if(process.Process!=IntPtr.Zero)CloseHandle(process.Process);
            if(job!=IntPtr.Zero)CloseHandle(job);
            if(attributesReady)DeleteProcThreadAttributeList(attributes);
            if(attributes!=IntPtr.Zero)Marshal.FreeHGlobal(attributes);
            if(handles!=IntPtr.Zero)Marshal.FreeHGlobal(handles);
            foreach(IntPtr file in new[]{input,stdout,stderr})if(file!=IntPtr.Zero && file!=Invalid)CloseHandle(file);
            if(desktop!=IntPtr.Zero) {
                Check(SetThreadDesktop(originalDesktop),"Restore runner thread desktop");
                result.DesktopClosed=CloseDesktop(desktop);Check(result.DesktopClosed,"Close private desktop");
            }
            result.ElapsedMilliseconds=watch.ElapsedMilliseconds;result.InputAfter=InputDesktopName();
        }
        return result;
    }
    // Harmless fixture. Verifies desktop before creating any windows. Its children use lpDesktop=NULL.
    [STAThread] public static int Main(string[] args) {
        if(args.Length<1)return 2;
        string actual=ObjectName(GetThreadDesktop(GetCurrentThreadId()));
        if(!actual.StartsWith("XiaomiStereoTest_",StringComparison.Ordinal)) {Console.Error.WriteLine("Fixture refused non-private desktop: "+actual);return 40;}
        Console.WriteLine("Fixture desktop="+actual+" pid="+Process.GetCurrentProcess().Id);
        if(args[0]=="--fixture-child")return 0;
        if(args[0]=="--fixture-timeout") {
            using(var child=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--fixture-hang"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}))Console.WriteLine("Timeout descendant="+child.Id);
            Thread.Sleep(60000);return 41;
        }
        if(args[0]=="--fixture-hang"){Thread.Sleep(60000);return 42;}
        if(args[0]!="--fixture")return 3;
        string self=System.Reflection.Assembly.GetExecutingAssembly().Location;
        using(var child=Process.Start(new ProcessStartInfo(self,"--fixture-child"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})) {
            if(!child.WaitForExit(5000)){child.Kill();return 43;}if(child.ExitCode!=0)return child.ExitCode;
        }
        var startup=new SI{Size=Marshal.SizeOf(typeof(SI)),Flags=1,Show=0};PI native;
        Check(CreateProcessBasic(self,new StringBuilder(Quote(self)+" --fixture-child"),IntPtr.Zero,IntPtr.Zero,false,0x08000000,IntPtr.Zero,null,ref startup,out native),"Fixture native child");
        try {if(WaitForSingleObject(native.Process,5000)!=0){TerminateProcess(native.Process,44);return 44;}uint code;Check(GetExitCodeProcess(native.Process,out code),"Fixture exit code");if(code!=0)return (int)code;}
        finally {CloseHandle(native.Thread);CloseHandle(native.Process);}
        using(var form=new System.Windows.Forms.Form()) {
            form.Text="Private desktop harmless test fixture";form.Show();System.Windows.Forms.Application.DoEvents();
            form.WindowState=System.Windows.Forms.FormWindowState.Minimized;System.Windows.Forms.Application.DoEvents();
            form.WindowState=System.Windows.Forms.FormWindowState.Normal;form.Activate();System.Windows.Forms.Application.DoEvents();
        }
        Console.WriteLine("PASS private desktop managed/native descendants and real window lifecycle");return 0;
    }
}
