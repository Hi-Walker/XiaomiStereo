using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NAudio.Wave;

// Compiles together with AudioPipeline.cs, contains no Windows endpoint calls.
static class AudioTests {
    const int Hz=48000;
    static int checks;
    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static long Tick(double sec){return (long)(sec*Stopwatch.Frequency);}
    static byte[] Packet(int frames,double left,double right,int rate=Hz){
        var samples=new float[frames*2];var bytes=new byte[frames*8];
        for(int i=0;i<frames;i++){
            samples[2*i]=(float)(.2*Math.Sin(2*Math.PI*left*i/rate));
            samples[2*i+1]=(float)(.2*Math.Sin(2*Math.PI*right*i/rate));
        }
        Buffer.BlockCopy(samples,0,bytes,0,bytes.Length);return bytes;
    }
    static void Formats(){
        Check(AudioPipeline.Supports(WaveFormat.CreateIeeeFloatWaveFormat(44100,2)),"float");
        Check(!AudioPipeline.Supports(WaveFormat.CreateIeeeFloatWaveFormat(48000,1)),"mono");
        Check(!AudioPipeline.Supports(new WaveFormat(48000,32,2)),"pcm32");
        Check(!AudioPipeline.Supports(WaveFormat.CreateIeeeFloatWaveFormat(96000,2)),"rate");
        var ext=new WaveFormatExtensible(48000,32,2);
        Check(AudioPipeline.Supports(ext),"extensible float");
        var p=WaveFormat.MarshalToPtr(ext);
        try{
            Marshal.WriteInt32(p,20,12);
            Check(!AudioPipeline.Supports(WaveFormat.MarshalFromPtr(p)),"wrong speaker mask");
            Marshal.WriteInt32(p,20,3);Marshal.WriteInt16(p,18,24);
            Check(!AudioPipeline.Supports(WaveFormat.MarshalFromPtr(p)),"valid bits");
        }finally{Marshal.FreeHGlobal(p);}
    }
    static void Progress(AudioPipeline a,int n,long ticks,byte[] l,byte[] r){
        a.ReadAt(0,l,0,n*8,ticks);a.ReadAt(1,r,0,n*8,ticks);
    }
    static void Safety(){
        var a=new AudioPipeline(Hz,Hz,Hz);
        var l=new byte[960*8];var r=new byte[l.Length];var pk=Packet(320,660,880);
        try{a.Push(pk,7,1);throw new Exception("partial accepted");}catch(ArgumentException){}
        a.Push(pk,0,1); Check(a.SourceFrames==0,"empty");
        Progress(a,960,0,l,r);
        a.Push(pk,pk.Length,1);
        Progress(a,960,Tick(.02),l,r);Progress(a,960,Tick(.08),l,r);
        Check(Math.Abs(BitConverter.ToSingle(l,16*8))>0,"short clip eventually audible");
        for(int i=0;i<3;i++)Progress(a,960,Tick(.1+i*.02),l,r);
        Check(BitConverter.ToSingle(l,0)==0 && a.SourceFrames==320,"idle fallback");
        a.PushSilence(3000);Progress(a,960,Tick(.17),l,r);
        Check(a.SourceFrames==3320 && a.IsPrimed,"silent positive packets");
        var aa=new float[]{float.NaN,float.PositiveInfinity,5,-3};var ab=new byte[16];Buffer.BlockCopy(aa,0,ab,0,16);
        a.Push(ab,ab.Length,1);Check(a.Describe().Contains("nonfinite=2"),"sanitize");
        long before=a.ResetCount;
        a.PushSilence(Hz*10);Check(a.ResetCount>before && a.QueueFrames(0)<=2300,"coordinated overflow");
        Check(a.QueueFrames(0)==a.QueueFrames(1),"same recovery anchor");
        a.BeginEpoch();Check(a.QueueFrames(0)==0 && a.QueueFrames(1)==0 && !a.IsPrimed,"epoch flush");
        // Move the frame index past signed 32-bit without producing multi-GB fixtures.
        for(int i=0;i<3;i++)a.PushSilence(1000000000);
        a.BeginEpoch();long big=a.SourceFrames;a.Push(pk,pk.Length,1);
        Check(big>int.MaxValue && a.SourceFrames==big+320,"long source frame");
        a.PushSilence(2400);Progress(a,960,Tick(.175),l,r);
        Check(Math.Abs(BitConverter.ToSingle(l,16*8))>0,"large source render");
        Console.WriteLine("recovery test "+a.Describe());
    }
    static double Energy(byte[] data,int start,int count,double freq,int rate){
        double re=0,im=0;
        for(int i=0;i<count;i++){
            float v=BitConverter.ToSingle(data,(start+i)*8);
            double phase=i*freq*2*Math.PI/rate;
            re+=v*Math.Cos(phase);im+=v*Math.Sin(phase);
        }
        return Math.Sqrt(re*re+im*im)*2/count;
    }
    static double Db(double x){return 20*Math.Log10(Math.Max(x,1e-14));}
    static void Quality(int src,int dst,double f,bool alias){
        const int count=1440;
        var a=new AudioPipeline(src,dst,dst);
        var prime=new byte[8];
        Progress(a,1,0,prime,prime);
        var input=Packet(src/10,f,1000,src);a.Push(input,input.Length,1);
        var b=new byte[2400*8];var r=new byte[b.Length];
        Progress(a,2400,0,b,r);
        for(int i=0;i<2400;i++)Check(BitConverter.ToSingle(b,i*8)==BitConverter.ToSingle(b,i*8+4),"dual mono");
        double amplitude=Energy(b,900,count,alias?dst-f:f,dst);
        double desired=.2;
        double sum=0,err=0,linearSum=0,linearErr=0;
        for(int i=900;i<900+count;i++){
            double refval=desired*Math.Sin(2*Math.PI*f*i/dst);
            double v=BitConverter.ToSingle(b,i*8);
            sum+=v*v;err+=(refval-v)*(refval-v);
            // Same source phase and rates, using the earlier interpolator formula.
            double x=(double)i*src/dst;
            int baseFrame=(int)x;
            float low=BitConverter.ToSingle(input,baseFrame*8);
            float high=BitConverter.ToSingle(input,(baseFrame+1)*8);
            double linear=low+(high-low)*(x-baseFrame);
            linearSum+=linear*linear;
            linearErr+=(refval-linear)*(refval-linear);
        }
        double rmsDb=Db(Math.Sqrt(2*sum/count)/desired);
        Console.WriteLine("quality {0}->{1} {2}Hz projection={3:F3}dB outputRMS={4:F3}dB relative_error={5:F1}dB reference-linear-RMS/error={6:F3}/{7:F1}dB",src,dst,f,Db(amplitude/desired),rmsDb,Db(Math.Sqrt(2*err/count)/desired),Db(Math.Sqrt(2*linearSum/count)/desired),Db(Math.Sqrt(2*linearErr/count)/desired));
        Check(alias?rmsDb< -45:Math.Abs(rmsDb)<.14,"quality");
        if(!alias)Check(Db(Math.Sqrt(2*err/count)/desired)< -38,"passband reference error");
    }
    static void Swapping(){
        var a=new AudioPipeline(Hz,Hz,Hz);var buf=new byte[2400*8];var r=new byte[buf.Length];
        Progress(a,1,0,buf,r);a.SetMix(true,6.020599913);
        var packet=Packet(4800,700,1200);a.Push(packet,packet.Length,1);
        Progress(a,2400,0,buf,r);
        Check(Math.Abs(Energy(buf,960,1440,1200,Hz)-.1)<.001,"swapped balance");
        Check(Math.Abs(Energy(r,960,1440,700,Hz)-.2)<.001,"right content");
        Check(Energy(buf,960,1440,700,Hz)<1e-5,"isolation");
        var b=new AudioPipeline(Hz,Hz,Hz);Progress(b,1,0,buf,r);b.SetMix(true,6.020599913);b.Push(packet,packet.Length,1);
        var alt=new byte[buf.Length];var other=new byte[buf.Length];
        for(int off=0;off<2400;){
            int n=Math.Min(13+(off%71),2400-off);
            b.ReadAt(0,alt,off*8,n*8,0);b.ReadAt(1,other,off*8,n*8,0);off+=n;
        }
        for(int i=0;i<buf.Length;i++)Check(alt[i]==buf[i] && other[i]==r[i],"chunk invariance");
    }
    static void GainRamp(){
        var single=new AudioPipeline(Hz,Hz,Hz);
        var split=new AudioPipeline(Hz,Hz,Hz);
        var input=Packet(4800,1000,1200);
        var first=new byte[2400*8];var second=new byte[first.Length];var unused=new byte[first.Length];
        Progress(single,1,0,first,unused);Progress(split,1,0,second,unused);
        single.Push(input,input.Length,.5f);
        int block=32*8;
        var batch=new byte[block];
        for(int offset=0;offset<input.Length;offset+=block) {
            int bytes=Math.Min(block,input.Length-offset);
            Buffer.BlockCopy(input,offset,batch,0,bytes);split.Push(batch,bytes,.5f);
        }
        Progress(single,2400,0,first,unused);Progress(split,2400,0,second,unused);
        for(int i=0;i<first.Length;i++)if(first[i]!=second[i])throw new Exception("gain ramp depends on source packet size");
        Check(Math.Abs(Energy(second,960,1440,1000,Hz)-.1)<.001,"compensated amplitude");
    }
    static byte[] ConstantPacket(int frames,float level) {
        var floats=new float[frames*2];
        for(int i=0;i<floats.Length;i++)floats[i]=level;
        var bytes=new byte[floats.Length*4];Buffer.BlockCopy(floats,0,bytes,0,bytes.Length);
        return bytes;
    }
    static void ShortEnvelopes() {
        foreach(int rate in new[]{44100,48000})
        foreach(int duration in new[]{1,2,5,10,20}) {
            var a=new AudioPipeline(rate,48000,48000);var init=new byte[8];Progress(a,1,0,init,init);
            var packet=ConstantPacket(rate*duration/1000,.2f);a.Push(packet,packet.Length,1);
            var output=new byte[1440*8];var right=new byte[output.Length];
            Progress(a,1440,Tick(.001),output,right);Progress(a,1440,Tick(.061),output,right);
            double peak=0;
            for(int i=0;i<1440;i++)peak=Math.Max(peak,Math.Abs(BitConverter.ToSingle(output,i*8)));
            Check(peak>.18 && peak<.23,"short clip envelope "+rate+"/"+duration);
            Console.WriteLine("short envelope {0}Hz {1}ms peak={2:F6}",rate,duration,peak);
        }
    }
    static void SourceVolumeTransitions() {
        var a=new AudioPipeline(Hz,Hz,Hz);var initial=new byte[8];Progress(a,1,0,initial,initial);
        var l=new byte[480*8];var r=new byte[l.Length];
        var low=ConstantPacket(480,.002f);var high=ConstantPacket(480,.02f);
        var guard=new VolumeLink.GainGuard(100,0,true,0,500000,1440);
        double peak=0;
        for(int i=0;i<65;i++) {
            long now=(i+1)*100000L;
            bool loud=i>=15 && i<35;
            int version=i<15?0:i<35?1:2;
            float wanted=loud?10:100;
            float gain=guard.Select(wanted,version,true,now,now+379300,480,true);
            a.Push(loud?high:low,low.Length,gain);
            Progress(a,480,Tick((i+1)*.01),l,r);
            for(int j=0;j<480;j++)peak=Math.Max(peak,Math.Abs(BitConverter.ToSingle(l,j*8)));
        }
        Check(peak<.23 && peak>.19 && a.Describe().Contains("clipped=0"),"source volume transients preserve headroom");
        Console.WriteLine("source volume x10 up/down with captured backlog peak={0:F6} {1}",peak,guard.Describe());
    }
    static void StableGainShortResume() {
        var a=new AudioPipeline(Hz,Hz,Hz);var initial=new byte[8];Progress(a,1,0,initial,initial);
        var warm=ConstantPacket(4800,.002f);a.Push(warm,warm.Length,100);
        var output=new byte[2400*8];var right=new byte[output.Length];
        Progress(a,2400,0,output,right);Progress(a,2400,Tick(.05),output,right);
        Progress(a,2400,Tick(.10),output,right);
        var guard=new VolumeLink.GainGuard(100,0,true,0,500000,1440);
        float gain=guard.Select(100,0,true,10000000,20000000,48,true,true);
        var sound=ConstantPacket(48,.002f);a.Push(sound,sound.Length,gain);
        Progress(a,2400,Tick(1),output,right);Progress(a,2400,Tick(1.06),output,right);
        double peak=0;
        for(int i=0;i<2400;i++)peak=Math.Max(peak,Math.Abs(BitConverter.ToSingle(output,i*8)));
        Check(peak>.18 && peak<.23,"unchanged gain preserves one-ms sound after idle");
        Console.WriteLine("stable inverse gain, idle then 1ms notification peak={0:F6}",peak);
    }
    static void ImpulseAndWrap(){
        var a=new AudioPipeline(Hz,Hz,Hz);var b=new AudioPipeline(Hz,Hz,Hz);
        var single=new byte[2400*8];var rolled=new byte[single.Length];var other=new byte[single.Length];
        Progress(a,1,0,single,other);Progress(b,1,0,rolled,other);
        b.PushSilence(131072-2050);b.BeginEpoch();
        var packet=new float[4800*2];packet[2*2050]=.25f;
        // Simultaneous right-channel logarithmic sweep checks isolation and chunk boundaries.
        double phase=0;
        for(int i=0;i<4800;i++){
            phase+=2*Math.PI*80*Math.Pow(200,i/4799.0)/Hz;
            packet[2*i+1]=(float)(.18*Math.Sin(phase)+.03*Math.Sin(2*Math.PI*250*i/Hz));
        }
        var bytes=new byte[packet.Length*4];Buffer.BlockCopy(packet,0,bytes,0,bytes.Length);
        a.Push(bytes,bytes.Length,1);b.Push(bytes,bytes.Length,1);
        var rightSingle=new byte[single.Length];var rightRolled=new byte[single.Length];
        Progress(a,2400,0,single,rightSingle);Progress(b,2400,0,rolled,rightRolled);
        double integral=0,tail=0;int peak=0;
        for(int i=0;i<2400;i++){
            float s=BitConverter.ToSingle(single,i*8);
            if(s!=BitConverter.ToSingle(rolled,i*8)||BitConverter.ToSingle(rightSingle,i*8)!=BitConverter.ToSingle(rightRolled,i*8))
                throw new Exception("FIR guard/ring boundary mismatch");
            if(s>BitConverter.ToSingle(single,peak*8))peak=i;
            if(Math.Abs(i-2050)>32)tail=Math.Max(tail,Math.Abs(s));
            integral+=s;
        }
        Check(peak==2050 && Math.Abs(integral-.25)<1e-5 && tail==0,"impulse DC, support, center");
    }
    static void Drift(double srcPpm,double leftPpm,double rightPpm,int seconds,bool injectStall,bool expectFault,bool thermal=false){
        var a=new AudioPipeline(Hz,Hz,Hz);
        var buff=new byte[1024*8];
        var srcPacket=Packet(960,700,1200);
        double[] period={1/(Hz*(1+srcPpm*1e-6)),1/(Hz*(1+leftPpm*1e-6)),1/(Hz*(1+rightPpm*1e-6))};
        double[] next={0,0,thermal?.024:.001};int[] cnt={480,480,480};
        double[] sum={0,0},lo={9999,9999},hi={-9999,-9999};int[] obs={0,0};
        int[] saturated={0,0}; double[] good={0,0},settled={-1,-1};
        long min=long.MaxValue,max=0;
        var rng=new Random(123);
        bool stalled=false;
        while(true){
            int side=next[0]<=next[1] && next[0]<=next[2]?0:next[1]<=next[2]?1:2;
            double t=next[side];if(t>seconds)break;
            if(side==0){a.Push(srcPacket,cnt[0]*8,1);cnt[0]=rng.Next(192,900);}
            else{
                a.ReadAt(side-1,buff,0,cnt[side]*8,Tick(t));
                if(thermal){
                    double devicePpm=(side==1?1:-1)*500*Math.Sin(2*Math.PI*t/120.0);
                    period[side]=1/(Hz*(1+devicePpm*1e-6));
                }
                int channel=side-1;double measured=a.RatePpm(channel);
                if(Math.Abs(measured)>=2999.9)saturated[channel]++;
                double expected=srcPpm-(side==1?leftPpm:rightPpm);
                if(t>1 && Math.Abs(measured-expected)<180)good[channel]+=cnt[side]*period[side];
                else good[channel]=0;
                if(settled[channel]<0 && good[channel]>=5)settled[channel]=t;
                if(t>5){
                    int s=side-1;double ppm=a.RatePpm(s);sum[s]+=ppm;obs[s]++;
                    lo[s]=Math.Min(lo[s],ppm);hi[s]=Math.Max(hi[s],ppm);
                    long q=a.QueueFrames(s);min=Math.Min(q,min);max=Math.Max(q,max);
                }
                if(injectStall && side==1 && t>seconds/2 && !stalled){next[side]+=.19;stalled=true;}
            }
            next[side]+=cnt[side]*period[side];
        }
        Console.WriteLine("drift({0},{1},{2}) {3}s min/max queue={4}/{5} L avg[min,max]={6:F0}[{7:F0},{8:F0}] R={9:F0}[{10:F0},{11:F0}] {12}",srcPpm,leftPpm,rightPpm,seconds,min,max,sum[0]/obs[0],lo[0],hi[0],sum[1]/obs[1],lo[1],hi[1],a.Describe());
        Console.WriteLine("  thermal={0}; saturated read intervals approx seconds L/R={1:F2}/{2:F2}; first end of 5 s within 180 ppm L/R={3:F2}/{4:F2}",thermal,saturated[0]*480*period[1],saturated[1]*480*period[2],settled[0],settled[1]);
        Check(max<=6500 && min>=-4,"bounded queue");
        Check(expectFault?a.ResetCount>0:a.ResetCount==0,"unexpected reset count");
        if(!expectFault && seconds>=90 && !thermal){
            Check(Math.Abs(sum[0]/obs[0]-(srcPpm-leftPpm))<180,"left drift tracking");
            Check(Math.Abs(sum[1]/obs[1]-(srcPpm-rightPpm))<180,"right drift tracking");
        }
    }
    static void Speed(){
        var a=new AudioPipeline(44100,48000,48000);var data=Packet(882,18000,14000,44100);
        var l=new byte[960*8];var r=new byte[l.Length];
        int warm=120;long[] elapsed=new long[5000];
        var allocated=typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",Type.EmptyTypes);
        Func<long> meter=allocated==null?null:(Func<long>)Delegate.CreateDelegate(typeof(Func<long>),allocated);
        long mem=0;var gc0=GC.CollectionCount(0);
        for(int i=-warm;i<elapsed.Length;i++){
            if(i==0){if(meter!=null)mem=meter();gc0=GC.CollectionCount(0);}
            long t=Stopwatch.GetTimestamp();
            int n=(i&1)==0?960:480;
            a.Push(data,(n==960?882:441)*8,1);Progress(a,n,Tick((i+warm)*.015),l,r);
            if(i>=0)elapsed[i]=Stopwatch.GetTimestamp()-t;
        }
        long bytes=meter!=null?meter()-mem:-1;
        int gen0=GC.CollectionCount(0)-gc0;
        Array.Sort(elapsed);
        Console.Write("benchmark 44100->48000 push+both renders alternating 480/960 frames, 5000 blocks, us p50/p95/p99/max:");
        foreach(double q in new[]{.5,.95,.99,1})Console.Write(" {0:F1}",elapsed[(int)((elapsed.Length-1)*q)]*1e6/Stopwatch.Frequency);
        Console.WriteLine(" allocatedBytes={0} gen0Delta={1}",bytes,gen0);
        Check(bytes<=1024 && gen0==0,"steady allocations");
    }
    static void Scheduling(){
        const int count=480;
        var pipeline=new AudioPipeline(Hz,Hz,Hz);
        var packet=Packet(count,800,1200);
        var l=new byte[count*8];var r=new byte[l.Length];
        var rng=new Random(149);int produced=0,delivered=0;
        long recoveries=0;
        for(int tick=0;tick<60000;tick++){
            // The true capture/consumption clocks are fixed. Callback jitter and a
            // 70 ms delivery stall are injected independently of sample production.
            if(tick%10==0)produced++;
            if(!(tick>=20000 && tick<20070) && tick%10>=rng.Next(0,5)){
                while(delivered<produced){pipeline.Push(packet,packet.Length,1);delivered++;}
            }
            if(tick%10==4)pipeline.ReadAt(0,l,0,l.Length,Tick(tick*.001));
            if(tick%10==6)pipeline.ReadAt(1,r,0,r.Length,Tick(tick*.001));
            if(tick==19500)Check(pipeline.ResetCount==0,"jitter before capture delivery stall");
            if(tick==21000)recoveries=pipeline.ResetCount;
        }
        Check(delivered==produced && recoveries>0 && pipeline.ResetCount==recoveries,"capture delivery stall then stable");
        Console.WriteLine("capture delivery jitter 0..4ms, 70ms missed notifications with later catch-up: "+pipeline.Describe());
    }
    public static int Main(string[] args){
        try{
            CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
            Formats();Safety();Swapping();GainRamp();ImpulseAndWrap();ShortEnvelopes();SourceVolumeTransitions();StableGainShortResume();
            Quality(48000,48000,1000,false); Quality(44100,48000,18000,false);
            Quality(48000,44100,23500,true);Quality(48000,44100,16000,false);
            Drift(0,0,0,45,false,false);Drift(20,-20,20,100,false,false);Drift(0,-100,100,100,false,false);Drift(0,-500,500,180,false,false);
            Drift(0,-2800,2800,120,false,false);Drift(0,-5000,5000,130,false,true);Drift(-500,100,-100,60,true,true);
            Scheduling();
            Drift(0,0,0,180,false,false,true);
            if(Array.IndexOf(args,"--long")>=0)Drift(0,-500,500,7200,false,false);
            Speed();
            Console.WriteLine("PASS assertions="+checks);return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
