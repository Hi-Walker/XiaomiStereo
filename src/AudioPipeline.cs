using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using NAudio.Wave;

// The capture thread publishes complete stereo frames; both renderers share one
// absolute write head. No worker or timer belongs to this object.
internal sealed class AudioPipeline {
    const int Taps=64, Half=Taps/2, Phases=256, Slice=128;
    readonly object gate=new object();
    readonly int sourceRate, capacity, mask, target, highWater;
    readonly float[][] ring;
    readonly Reader[] sides;
    float maxInputPeak, maxOutputPeak;
    long end, floor, epoch=1, resets, starves, dropped, bad, clips, observedGap, drainEnd=-1;
    long waitStart=-1;
    bool running, swapped;
    float captureGain=1, captureTarget=1, gainStep;
    int gainRemaining;
    public IWaveProvider Left { get { return sides[0]; } }
    public IWaveProvider Right { get { return sides[1]; } }
    public bool IsPrimed { get { lock(gate) return running; } }
    public static bool Supports(WaveFormat f) {
        if(f==null || f.Channels!=2 || f.BitsPerSample!=32 || f.BlockAlign!=8 || !RateSupported(f.SampleRate)) return false;
        if(f.Encoding==WaveFormatEncoding.IeeeFloat) return true;
        var ext=f as WaveFormatExtensible;
        if(ext==null || ext.SubFormat!=new Guid("00000003-0000-0010-8000-00aa00389b71") || ext.ExtraSize<22) return false;
        IntPtr p=WaveFormat.MarshalToPtr(f);
        try {
            int bits=Marshal.ReadInt16(p,18), channels=Marshal.ReadInt32(p,20);
            return bits==32 && (channels==0 || channels==3);
        } finally { Marshal.FreeHGlobal(p); }
    }
    static bool RateSupported(int rate) { return rate==44100 || rate==48000; }
    public AudioPipeline(int sourceRate,int leftRate,int rightRate,int targetMs=45) {
        if(!RateSupported(sourceRate)||!RateSupported(leftRate)||!RateSupported(rightRate)||targetMs<30||targetMs>100)
            throw new ArgumentOutOfRangeException("format/queue");
        this.sourceRate=sourceRate;
        capacity=1;
        while(capacity<2*sourceRate) capacity*=2;
        mask=capacity-1;
        ring=new[]{new float[capacity+Taps],new float[capacity+Taps]};
        target=sourceRate*targetMs/1000;
        highWater=target+sourceRate*90/1000;
        var leftKernel=BuildKernel(sourceRate,leftRate);
        sides=new[]{new Reader(this,0,leftRate,leftKernel),new Reader(this,1,rightRate,
            rightRate==leftRate?leftKernel:BuildKernel(sourceRate,rightRate))};
    }
    static float[] BuildKernel(int input,int output) {
        var table=new float[(Phases+1)*Taps];
        // Fixed cutoff accommodates nominal conversion and the full drift range.
        double cutoff=0.94*Math.Min(1.0,(double)output/(input*1.003));
        for(int p=0;p<=Phases;p++){
            double sum=0;
            for(int i=0;i<Taps;i++){
                double x=i-(Half-1)-(double)p/Phases, a=Math.PI*x;
                double sinc=Math.Abs(a)<1e-12?cutoff:Math.Sin(a*cutoff)/a;
                double w=0.42+0.5*Math.Cos(a/Half)+0.08*Math.Cos(2*a/Half);
                double h=sinc*w;
                table[p*Taps+i]=(float)h; sum+=h;
            }
            for(int i=0;i<Taps;i++) table[p*Taps+i]/=(float)sum;
        }
        return table;
    }
    public void SetMix(bool swap,double balanceDb) {
        if(Double.IsNaN(balanceDb)||Math.Abs(balanceDb)>24) throw new ArgumentOutOfRangeException("balanceDb");
        lock(gate) {
            if(swap!=swapped){swapped=swap; Reset(end);}
            sides[0].DesiredGain=(float)Math.Pow(10,Math.Min(0,-balanceDb)/20);
            sides[1].DesiredGain=(float)Math.Pow(10,Math.Min(0,balanceDb)/20);
        }
    }
    public void BeginEpoch() { lock(gate) Reset(end); }
    void Reset(long next) {
        // Count distinct source frames which at least one reader skipped.
        long old=running?Math.Min(sides[0].Frame,sides[1].Frame):floor;
        dropped+=Math.Max(0,next-old);
        running=false;floor=next;drainEnd=-1;waitStart=-1;epoch++;resets++;
        foreach(var r in sides) r.Reset(next);
    }
    public unsafe void Push(byte[] bytes,int count,float compensationGain) {
        if(bytes==null || count<0 || count>bytes.Length || count%8!=0) throw new ArgumentException("Incomplete stereo frame");
        if(float.IsNaN(compensationGain)||float.IsInfinity(compensationGain)||compensationGain<0||compensationGain>1000000)
            throw new ArgumentOutOfRangeException("compensationGain");
        if(count==0)return;
        lock(gate) {
            int frames=count/8, keep=Math.Min(frames,capacity);
            if(frames>capacity){Reset(end);dropped+=frames-keep;end+=frames-keep;floor=end;}
            if(compensationGain!=captureTarget) {
                captureTarget=compensationGain;
                gainRemaining=Math.Max(1,sourceRate/200);
                gainStep=(captureTarget-captureGain)/gainRemaining;
                // A smaller inverse gain must apply before the next input sample.
                // Keeping the previous gain during a source-volume rise can clip
                // the whole transition before any output fade has a chance to act.
                if(captureTarget<=captureGain) {
                    captureGain=captureTarget;
                    gainRemaining=0;
                }
            }
            fixed(byte* first=bytes){
                float* samples=(float*)first + 2*(frames-keep);
                for(int i=0;i<keep;i++){
                    if(gainRemaining>0) {
                        gainRemaining--;
                        captureGain=gainRemaining==0?captureTarget:captureGain+gainStep;
                    }
                    int index=(int)(end++ & mask);
                    for(int c=0;c<2;c++){
                        float s=*samples++ * captureGain;
                        if(float.IsNaN(s)||float.IsInfinity(s)){bad++;s=0;}
                        maxInputPeak=Math.Max(maxInputPeak,Math.Abs(s));
                        if(s>1 || s< -1){clips++;s=Math.Max(-1,Math.Min(1,s));}
                        Store(c,index,s);
                    }
                }
            }
            GuardQueue();
        }
    }
    public void PushSilence(int frames) {
        if(frames<0)throw new ArgumentOutOfRangeException("frames");
        if(frames==0)return;
        lock(gate){
            int keep=Math.Min(frames,capacity);
            if(keep<frames){Reset(end);dropped+=frames-keep;end+=frames-keep;floor=end;}
            for(int i=0;i<keep;i++){int slot=(int)(end++ & mask); Store(0,slot,0);Store(1,slot,0);}
            GuardQueue();
        }
    }
    void Store(int c,int slot,float value) {
        ring[c][slot]=value;
        if(slot<Taps) ring[c][capacity+slot]=value;
    }
    void GuardQueue() {
        long oldest=running?Math.Min(sides[0].Frame,sides[1].Frame):floor;
        if(end-oldest>highWater) Reset(end-target);
    }
    void TryStart(long ticks) {
        if(running||end<=floor||!sides[0].Seen||!sides[1].Seen)return;
        if(waitStart<0)waitStart=ticks;
        // Also flush short notification sounds which never fill the target queue.
        if(end-floor<target && ticks-waitStart<(long)target*Stopwatch.Frequency/sourceRate)return;
        foreach(var r in sides)r.Start(floor,ticks);
        running=true;
    }
    public string Describe() {
        lock(gate) return String.Format(CultureInfo.InvariantCulture,
            "state={0} epoch={1} frames={2} queueL/R={3}/{4} ppmL/R={5:F1}/{6:F1} resets={7} starves={8} discarded={9} nonfinite={10} clipped={11} callbackGaps={12} capacityFrames={13} targetFrames={14} highWaterFrames={15} peaksAfterGain/FIR={16:F3}/{17:F3}",
            running?(drainEnd>=0?"Draining":"Running"):(end>floor?"Priming":"Idle"),epoch,end,end-sides[0].Frame,end-sides[1].Frame,
            sides[0].Correction*1e6,sides[1].Correction*1e6,resets,starves,dropped,bad,clips,observedGap,capacity,target,highWater,maxInputPeak,maxOutputPeak);
    }
    // Tests feed independent scheduled capture/read events and a virtual monotonic clock.
    internal int ReadAt(int side,byte[] bytes,int offset,int count,long ticks) {
        return sides[side].At(bytes,offset,count,ticks);
    }
    internal long ResetCount {get{lock(gate)return resets;}}
    internal long SourceFrames {get{lock(gate)return end;}}
    internal long QueueFrames(int side){lock(gate)return end-sides[side].Frame;}
    internal double RatePpm(int side){lock(gate)return sides[side].Correction*1e6;}
    sealed class Reader : IWaveProvider {
        readonly AudioPipeline owner;
        readonly int side,rate,fade;
        readonly float[] kernel;
        readonly WaveFormat format;
        public long Frame;
        public bool Seen;
        public double Correction;
        public float DesiredGain=1;
        double frac, filtered, step, queueErrorArea, observedSeconds;
        long lastUpdate, lastRead=-1;
        int fadeIn, envelopeFrames, tail;
        float lastSample, tailSample, gain=1;
        public Reader(AudioPipeline a,int channel,int hz,float[] table){
            owner=a;side=channel;rate=hz;kernel=table;fade=hz*8/1000;
            format=WaveFormat.CreateIeeeFloatWaveFormat(hz,2);
        }
        public WaveFormat WaveFormat {get{return format;}}
        public int Read(byte[] b,int offset,int count){return At(b,offset,count,Stopwatch.GetTimestamp());}
        public void Reset(long next) {
            Frame=next;frac=0;Correction=0;filtered=0;
            tailSample=lastSample;tail=tailSample==0?0:fade;fadeIn=fade;
        }
        public void Start(long first,long ticks){
            Frame=first;frac=0;Correction=0;filtered=0;lastUpdate=ticks;
            step=(double)owner.sourceRate/rate;
            // Choose once for this epoch. The live packet boundary does not
            // shorten the envelope while a continuous stream is progressing.
            envelopeFrames=(int)Math.Min(fade,Math.Max(0,Math.Ceiling((owner.end-first)/step/2)-1));
            fadeIn=envelopeFrames;queueErrorArea=0;observedSeconds=0;
        }
        void Control(long ticks,int frames){
            if(owner.drainEnd>=0 || lastRead<0)return;
            double observed=Math.Max(0,Math.Min(.10,(double)(ticks-lastRead)/Stopwatch.Frequency));
            double error=(owner.end-Frame-0.5*frames*step-owner.target)/owner.sourceRate;
            queueErrorArea+=error*observed;
            observedSeconds+=observed;
            double dt=(double)(ticks-lastUpdate)/Stopwatch.Frequency;
            if(dt<0.095 || observedSeconds==0)return;
            dt=Math.Min(dt,.25);lastUpdate=ticks;
            double meanError=queueErrorArea/observedSeconds;
            queueErrorArea=0;observedSeconds=0;
            filtered+=(1-Math.Exp(-dt/1.0))*(meanError-filtered);
            double wanted=Math.Max(-0.003,Math.Min(0.003,filtered*0.32));
            double slew=0.0008*dt;
            Correction+=Math.Max(-slew,Math.Min(slew,wanted-Correction));
            step=(double)owner.sourceRate/rate*(1+Correction);
        }
        public unsafe int At(byte[] b,int offset,int count,long ticks) {
            if(b==null || offset<0 || count<0 || count%8!=0 || offset>b.Length-count)throw new ArgumentException("Invalid output buffer");
            if(count==0)return 0;
            lock(owner.gate) {
                if(lastRead>=0 && ticks-lastRead>Stopwatch.Frequency/6 && owner.running){
                    owner.observedGap++;
                    owner.Reset(owner.end); // stale frame age cannot be inferred after a long device stall
                }
                Seen=true;owner.TryStart(ticks);
                if(owner.running) Control(ticks,count/8);
                lastRead=ticks;
                if(!owner.running && tail==0) {
                    Array.Clear(b,offset,count);
                    return count;
                }
            }
            fixed(byte* ptr=b){
                float* dst=(float*)(ptr+offset);
                for(int done=0;done<count/8;){
                    int n=Math.Min(Slice,count/8-done);
                    lock(owner.gate){
                        Render(dst+done*2,n);
                    }
                    done+=n;
                }
            }
            return count;
        }
        unsafe void Render(float* dst,int n){
            long limit=owner.drainEnd>=0?owner.drainEnd:owner.end;
            float[] ring=owner.ring[owner.swapped?1-side:side];
            fixed(float* src=ring, co=kernel){
                for(int i=0;i<n;i++){
                    float sample=0;
                    if(owner.running && Frame<limit){
                        int phase=(int)(frac*Phases);
                        double blend=frac*Phases-phase;
                        int k=phase*Taps;
                        long first=Frame-(Half-1);
                        int slot=(int)(first & owner.mask);
                        double sum=0, offsetSum=0;
                        int low=(int)Math.Max(0L,Math.Min((long)Taps,owner.floor-first));
                        int high=(int)Math.Min((long)Taps,limit-first);
                        for(int tap=low;tap<high;tap++){
                            float v=src[slot+tap];float a=co[k+tap];
                            sum+=v*a;offsetSum+=v*(co[k+Taps+tap]-a);
                        }
                        sample=(float)(sum+offsetSum*blend);
                        // Exhaustion is known while the final samples are still writable.
                        float env=envelopeFrames==0?1:(float)Math.Min(1.0,(limit-Frame)/(step*envelopeFrames));
                        if(fadeIn>0){env*=1f-(float)fadeIn/envelopeFrames;fadeIn--;}
                        gain+=Math.Max(-1f/fade,Math.Min(1f/fade,DesiredGain-gain));
                        sample*=env*gain;
                        frac+=step;int whole=(int)frac;Frame+=whole;frac-=whole;
                    }else if(owner.running){
                        if(owner.drainEnd<0){owner.drainEnd=limit;owner.starves++;}
                        if(owner.sides[0].Frame>=limit && owner.sides[1].Frame>=limit) owner.Reset(limit);
                    }
                    if(tail>0){sample+=tailSample*((float)tail/fade);tail--;}
                    owner.maxOutputPeak=Math.Max(owner.maxOutputPeak,Math.Abs(sample));
                    if(sample>1 || sample< -1){owner.clips++;sample=Math.Max(-1,Math.Min(1,sample));}
                    dst[2*i]=dst[2*i+1]=sample;lastSample=sample;
                }
            }
        }
    }
}
