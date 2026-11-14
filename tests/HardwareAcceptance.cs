using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// Interactive Windows acceptance tool. Temporarily plays quiet stereo tones and
// exercises endpoint volume. build.ps1 never runs it automatically.
internal static class HardwareAcceptance {
    static void Check(bool condition, string description) {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
    sealed class Tone : IWaveProvider {
        readonly WaveFormat format;
        readonly float[] samples;
        int position;
        public WaveFormat WaveFormat { get { return format; } }
        internal Tone(int rate) {
            format=WaveFormat.CreateIeeeFloatWaveFormat(rate,2);
            // One exact second, so the repeated waveform is continuous.
            samples=new float[2*rate];
            for(int i=0;i<rate;i++) {
                samples[2*i]=(float)(0.012*Math.Sin(i*2*Math.PI*700/rate));
                samples[2*i+1]=(float)(0.012*Math.Sin(i*2*Math.PI*1700/rate));
            }
        }
        public int Read(byte[] data,int offset,int bytes) {
            int copied=0;
            while(copied<bytes) {
                int n=Math.Min(bytes-copied,samples.Length*4-position);
                Buffer.BlockCopy(samples,position,data,offset+copied,n);
                copied+=n;position=(position+n)%(samples.Length*4);
            }
            return bytes;
        }
    }
    static void Probe(DeviceSet devices, bool swap, double gainLeft, Bridge bridge) {
        var sums=new double[2,2];var count=new int[2];var caps=new WasapiLoopbackCapture[2];
        int measuring=0;
        try {
            for(int k=0;k<2;k++){
                int side=k;
                var c=caps[k]=new WasapiLoopbackCapture(k==0?devices.Left:devices.Right);
                int rate=c.WaveFormat.SampleRate,align=c.WaveFormat.BlockAlign;
                c.DataAvailable+=(s,a)=>{
                    if(Volatile.Read(ref measuring)==0)return;
                    int n=a.BytesRecorded/align;
                    if(n<rate/100)return;
                    // Project integer 10ms windows to reject the other test frequency.
                    n=n/(rate/100)*(rate/100);
                    lock(count){
                        for(int f=0;f<2;f++){
                            double re=0,im=0,step=2*Math.PI*(f==0?700:1700)/rate;
                            for(int i=0;i<n;i++){
                                float sample=BitConverter.ToSingle(a.Buffer,i*align);
                                re+=sample*Math.Cos(i*step);im+=sample*Math.Sin(i*step);
                            }
                            sums[side,f]+=2*Math.Sqrt(re*re+im*im)/n;
                        }
                        count[side]++;
                    }
                };
                c.StartRecording();
            }
            using(var client=devices.Source.AudioClient)
            using(var player=new WasapiOut(devices.Source,AudioClientShareMode.Shared,true,30)) {
                player.Init(new Tone(client.MixFormat.SampleRate));player.Play();
                Thread.Sleep(400);Volatile.Write(ref measuring,1);Thread.Sleep(1000);Volatile.Write(ref measuring,0);
                if(bridge!=null)Console.WriteLine(bridge.Summary);
                player.Stop();
            }
        } finally {Volatile.Write(ref measuring,0);foreach(var c in caps)if(c!=null)c.Dispose();}
        lock(count){
            for(int i=0;i<2;i++){
                for(int f=0;f<2;f++)sums[i,f]/=Math.Max(count[i],1);
                Console.WriteLine("{0} digital amplitudes at 700/1700 Hz: {1:F6}/{2:F6} packets={3}",i==0?"1111":"2222",sums[i,0],sums[i,1],count[i]);
                int own=swap?1-i:i;
                Check(count[i]>2 && sums[i,own]>0.003 && sums[i,1-own]<sums[i,own]*0.22,"USB channel "+i+" swap="+swap);
            }
            double ratio=sums[0,swap?1:0]/sums[1,swap?0:1];
            Check(Math.Abs(20*Math.Log10(ratio/gainLeft))<1.5,"channel balance gain "+gainLeft);
        }
    }
    static void Wait(Bridge bridge,int milliseconds) {
        var t=Stopwatch.StartNew();
        while(t.ElapsedMilliseconds<milliseconds){bridge.Tick();Thread.Sleep(90);}
    }
    static bool Close(float a,float b){return Math.Abs(a-b)<0.09f;}
    static void MuteOthers(DeviceSet d,List<AudioSessionControl> sessions) {
        uint ownPid;using(var process=Process.GetCurrentProcess())ownPid=(uint)process.Id;
        foreach(var device in new[]{d.Source,d.Left,d.Right}) {
            var manager=device.AudioSessionManager;manager.RefreshSessions();var available=manager.Sessions;
            if(available.Count>256)throw new InvalidOperationException("Too many sessions for bounded test");
            for(int i=0;i<available.Count;i++) {
                var session=available[i];bool keep=false;
                try {
                    bool bridge=false;
                    try{using(var process=Process.GetProcessById((int)session.GetProcessID))bridge=process.ProcessName=="小米立体声";}
                    catch(ArgumentException){}
                    if(!bridge && session.GetProcessID!=ownPid && !session.SimpleAudioVolume.Mute) {
                        sessions.Add(session);keep=true;session.SimpleAudioVolume.Mute=true;
                    }
                }finally{if(!keep)session.Dispose();}
            }
        }
    }
    static void RestoreSessions(List<AudioSessionControl> sessions) {
        Exception failure=null;
        foreach(var session in sessions) {
            try{session.SimpleAudioVolume.Mute=false;}catch(Exception e){if(failure==null)failure=e;}
            try{session.Dispose();}catch(Exception e){if(failure==null)failure=e;}
        }
        if(failure!=null)throw new InvalidOperationException("Test session restoration incomplete",failure);
    }
    // Opt-in real-device regression: restore all changed volumes/sessions in finally.
    static void VolumeTransition(DeviceSet d) {
        var src=d.Source.AudioEndpointVolume;
        float before=src.MasterVolumeLevelScalar;
        bool muted=src.Mute;
        float leftBefore=d.Left.AudioEndpointVolume.MasterVolumeLevel,rightBefore=d.Right.AudioEndpointVolume.MasterVolumeLevel;
        bool leftMuted=d.Left.AudioEndpointVolume.Mute,rightMuted=d.Right.AudioEndpointVolume.Mute;
        var sessions=new List<AudioSessionControl>();
        var caps=new WasapiLoopbackCapture[2];
        var peaks=new double[2];var samples=new long[2];
        var square=new double[2];var steadySamples=new long[2];
        object gate=new object();int measuring=0,steady=0;
        try {
            MuteOthers(d,sessions);
            for(int side=0;side<2;side++) {
                int index=side;var c=caps[side]=new WasapiLoopbackCapture(side==0?d.Left:d.Right);
                Check(AudioPipeline.Supports(c.WaveFormat),"transition capture format side="+side);
                c.DataAvailable+=(s,a)=>{
                    if(Volatile.Read(ref measuring)==0)return;
                    lock(gate)for(int i=0;i<a.BytesRecorded;i+=4){
                        double value=Math.Abs(BitConverter.ToSingle(a.Buffer,i));
                        if(double.IsNaN(value)||double.IsInfinity(value))value=double.PositiveInfinity;
                        peaks[index]=Math.Max(peaks[index],value);samples[index]++;
                        if(Volatile.Read(ref steady)!=0){square[index]+=value*value;steadySamples[index]++;}
                    }
                };
                c.StartRecording();
            }
            src.Mute=false;
            using(var client=d.Source.AudioClient)
            using(var player=new WasapiOut(d.Source,AudioClientShareMode.Shared,true,30)) {
                player.Init(new Tone(client.MixFormat.SampleRate));player.Play();Thread.Sleep(700);
                Volatile.Write(ref measuring,1);
                foreach(float level in new[]{.10f,.75f,.25f,.60f,.10f,.75f,.40f}) {
                    src.MasterVolumeLevelScalar=level;Thread.Sleep(350);
                    lock(gate){Array.Clear(square,0,2);Array.Clear(steadySamples,0,2);Volatile.Write(ref steady,1);}
                    Thread.Sleep(200);Volatile.Write(ref steady,0);
                    lock(gate)for(int i=0;i<2;i++){
                        double rms=Math.Sqrt(square[i]/Math.Max(1,steadySamples[i]));
                        Console.WriteLine("steady Windows={0:P0} side={1} rms={2:F6} samples={3}",level,i,rms,steadySamples[i]);
                        Check(steadySamples[i]>4800 && rms>.007 && rms<.0095,"compensation recovered at "+level+" side="+i);
                    }
                }
                for(int i=0;i<8;i++){src.MasterVolumeLevelScalar=(i%2==0?.15f:.70f);Thread.Sleep(70);}
                src.MasterVolumeLevelScalar=before;Thread.Sleep(700);
                Volatile.Write(ref measuring,0);player.Stop();
            }
            lock(gate)for(int i=0;i<2;i++) {
                Console.WriteLine("transition side={0} peak={1:F6} samples={2}",i,peaks[i],samples[i]);
                Check(samples[i]>48000 && peaks[i]>.004 && peaks[i]<.03,"no large transient amplification side="+i);
            }
        } finally {
            Volatile.Write(ref measuring,0);
            Exception failure=null;
            Action<Action> attempt=action=>{try{action();}catch(Exception e){if(failure==null)failure=e;}};
            foreach(var c in caps)if(c!=null)attempt(()=>c.Dispose());
            attempt(()=>src.MasterVolumeLevelScalar=before);
            attempt(()=>src.Mute=muted);
            Thread.Sleep(700);
            attempt(()=>d.Left.AudioEndpointVolume.MasterVolumeLevel=leftBefore);
            attempt(()=>d.Right.AudioEndpointVolume.MasterVolumeLevel=rightBefore);
            attempt(()=>d.Left.AudioEndpointVolume.Mute=leftMuted);
            attempt(()=>d.Right.AudioEndpointVolume.Mute=rightMuted);
            attempt(()=>RestoreSessions(sessions));
            Thread.Sleep(250);
            attempt(()=>Check(Math.Abs(src.MasterVolumeLevelScalar-before)<.005f && src.Mute==muted &&
                Close(d.Left.AudioEndpointVolume.MasterVolumeLevel,leftBefore) && Close(d.Right.AudioEndpointVolume.MasterVolumeLevel,rightBefore) &&
                d.Left.AudioEndpointVolume.Mute==leftMuted && d.Right.AudioEndpointVolume.Mute==rightMuted,"transition restored all three endpoint states"));
            if(failure!=null)throw new InvalidOperationException("Transition cleanup incomplete",failure);
            Console.WriteLine("transition restored Windows {0:P0}, muted={1}, USB L/R {2}/{3} dB",before,muted,leftBefore,rightBefore);
        }
    }
    static int Main(string[] args) {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        if(args.Length!=1 || Array.IndexOf(new[]{"--bridge","--through-tray","--select-left","--play-quiet","--role-probe","--volume-transition"},args[0])<0)return 2;
        try {
            using(var en=new MMDeviceEnumerator())
            using(var d=DeviceSelector.Resolve(en,new AppSettings())){
                if(args[0]=="--volume-transition"){VolumeTransition(d);return 0;}
                if(args[0]=="--role-probe"){
                    using(var roles=new WindowsRoles(en)){
                        string a=roles.Get(0),b=roles.Get(1);
                        try {
                            roles.Set(0,d.Source.ID);
                            Console.WriteLine("set console only: console=virtual {0}, multimedia=virtual {1}, multimedia previously virtual {2}",roles.Get(0)==d.Source.ID,roles.Get(1)==d.Source.ID,b==d.Source.ID);
                            roles.Set(0,a);roles.Set(1,b);
                            roles.Set(1,d.Source.ID);
                            Console.WriteLine("set multimedia only: console=virtual {0}, multimedia=virtual {1}, console previously virtual {2}",roles.Get(0)==d.Source.ID,roles.Get(1)==d.Source.ID,a==d.Source.ID);
                        } finally {roles.Set(0,a);roles.Set(1,b);}
                    }
                    return 0;
                }
                if(args[0]=="--play-quiet"){
                    using(var client=d.Source.AudioClient)
                    using(var player=new WasapiOut(d.Source,AudioClientShareMode.Shared,true,30)){
                        player.Init(new Tone(client.MixFormat.SampleRate));player.Play();Thread.Sleep(14000);player.Stop();
                    }
                    return 0;
                }
                if(args[0]=="--select-left"){
                    using(var roles=new WindowsRoles(en)) {roles.Set(0,d.Left.ID);roles.Set(1,d.Left.ID);}
                    return 0;
                }
                if(args[0]=="--through-tray"){
                    using(var def=en.GetDefaultAudioEndpoint(DataFlow.Render,Role.Multimedia))
                        Check(def.ID==d.Source.ID,"virtual endpoint selected");
                    var sessions=new List<AudioSessionControl>();
                    try{MuteOthers(d,sessions);Thread.Sleep(250);Probe(d,false,1,null);}
                    finally{RestoreSessions(sessions);}return 0;
                }
                bool exclusive;
                using(var mutex=new Mutex(true,@"Local\XiaomiUsbStereo",out exclusive)){
                    if(!exclusive)throw new InvalidOperationException("另一个小米立体声正在运行。");
                    var src=d.Source.AudioEndpointVolume;var l=d.Left.AudioEndpointVolume;var r=d.Right.AudioEndpointVolume;
                    float before=src.MasterVolumeLevelScalar, lb=l.MasterVolumeLevel, rb=r.MasterVolumeLevel;
                    bool sm=src.Mute,lm=l.Mute,rm=r.Mute;
                    var sessions=new List<AudioSessionControl>();
                    try {
                        Thread.Sleep(400);MuteOthers(d,sessions);
                        using(var bridge=new Bridge(d.Source,d.Left,d.Right,false,0)){
                            if(sm) {src.Mute=false;Wait(bridge,400);}
                            Probe(d,false,1,bridge);
                            bridge.SetMix(true,0);Wait(bridge,200);Probe(d,true,1,bridge);
                            bridge.SetMix(false,6);Wait(bridge,200);Probe(d,false,Math.Pow(10,-6.0/20),bridge);
                            src.MasterVolumeLevelScalar=0.32f;Wait(bridge,600);
                            Check(Close(l.MasterVolumeLevel,-38f) && Close(r.MasterVolumeLevel,-38f),"Windows 32% -> both speakers -38 dB");
                            r.MasterVolumeLevel=-32;Wait(bridge,700);
                            Check(Math.Abs(src.MasterVolumeLevelScalar-.40f)<.005 && Close(l.MasterVolumeLevel,-32f),"right USB -32 dB -> Windows 40% and left USB");
                            src.Mute=true;Wait(bridge,400);Check(l.Mute&&r.Mute,"Windows mute propagated");
                            l.Mute=false;Wait(bridge,400);Check(!r.Mute&&!src.Mute,"left USB unmute propagated");
                            bridge.SetMix(false,0);
                            foreach(int delay in new[]{1,5,30}) {
                                Wait(bridge,delay*1000);Console.WriteLine("Stopped source "+delay+"s: "+bridge.Summary);
                                Probe(d,false,1,bridge);
                            }
                        }
                        for(int i=0;i<30;i++){
                            using(var bridge=new Bridge(d.Source,d.Left,d.Right,false,0)){Wait(bridge,100);}
                            if(i==0 || i==9 || i==29){
                                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                                using(var proc=Process.GetCurrentProcess())
                                    Console.WriteLine("restart {0} post-release handles={1} threads={2} private={3}",i+1,proc.HandleCount,proc.Threads.Count,proc.PrivateMemorySize64);
                            }
                        }
                    } finally {
                        Exception cleanupFailure=null;
                        Action<Action> attempt=action=>{try{action();}catch(Exception e){if(cleanupFailure==null)cleanupFailure=e;}};
                        attempt(()=>src.MasterVolumeLevelScalar=before);attempt(()=>l.MasterVolumeLevel=lb);attempt(()=>r.MasterVolumeLevel=rb);
                        attempt(()=>src.Mute=sm);attempt(()=>l.Mute=lm);attempt(()=>r.Mute=rm);
                        attempt(()=>RestoreSessions(sessions));
                        if(cleanupFailure!=null)throw new InvalidOperationException("Bridge test cleanup incomplete",cleanupFailure);
                        Console.WriteLine("restored Windows {0:P0} USB L/R {1}/{2} dB",src.MasterVolumeLevelScalar,l.MasterVolumeLevel,r.MasterVolumeLevel);
                    }
                }
            }return 0;
        } catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
