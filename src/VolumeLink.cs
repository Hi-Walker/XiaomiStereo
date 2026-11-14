using System;
using System.Threading;
using NAudio.CoreAudioApi;

internal sealed class VolumeLink : IDisposable {
    readonly AudioEndpointVolume masterVolume, leftVolume, rightVolume;
    bool subscribedSource,subscribedLeft,subscribedRight;
    float lastVolume=-1,speakerDb,pendingDb=float.NaN;
    bool? lastMute;
    int dirty=1;
    long nextAudit;
    public VolumeLink(MMDevice source,MMDevice left,MMDevice right) {
        masterVolume=source.AudioEndpointVolume;
        leftVolume=left.AudioEndpointVolume;
        rightVolume=right.AudioEndpointVolume;
        try {
            masterVolume.OnVolumeNotification+=Changed;subscribedSource=true;
            leftVolume.OnVolumeNotification+=Changed;subscribedLeft=true;
            rightVolume.OnVolumeNotification+=Changed;subscribedRight=true;
            Tick();
        } catch { Dispose(); throw; }
    }
    void Changed(AudioVolumeNotificationData data) { Interlocked.Exchange(ref dirty,1); }
    public void Tick() {
        long now=System.Diagnostics.Stopwatch.GetTimestamp();
        if(Interlocked.Exchange(ref dirty,0)==0 && float.IsNaN(pendingDb) && now<nextAudit) return;
        nextAudit=now+System.Diagnostics.Stopwatch.Frequency;
        SyncVolume();
    }
    public void Dispose() {
        if(subscribedSource){masterVolume.OnVolumeNotification-=Changed;subscribedSource=false;}
        if(subscribedLeft){leftVolume.OnVolumeNotification-=Changed;subscribedLeft=false;}
        if(subscribedRight){rightVolume.OnVolumeNotification-=Changed;subscribedRight=false;}
    }
    public void SyncVolume() {
        float level=masterVolume.MasterVolumeLevelScalar;
        bool mute=masterVolume.Mute;
        var lv=leftVolume;
        var rv=rightVolume;
        if(lastMute==mute && (lv.Mute!=mute || rv.Mute!=mute)) {
            mute=!mute;
            masterVolume.Mute=mute;
        }
        if(lastMute!=mute && mute) SetMuted(true);
        if(level!=lastVolume) {
            speakerDb=SpeakerDecibels(level);
            lv.MasterVolumeLevel=speakerDb;
            rv.MasterVolumeLevel=speakerDb;
            lastVolume=level;
            pendingDb=float.NaN;
        } else {
            float leftDb=lv.MasterVolumeLevel, rightDb=rv.MasterVolumeLevel;
            float db=Math.Abs(leftDb-speakerDb)>0.05f ? leftDb : rightDb;
            if(Math.Abs(db-speakerDb)<=0.05f) pendingDb=float.NaN;
            else if(db!=pendingDb) pendingDb=db;
            else {
                // Two consecutive reads distinguish a panel change from a brief USB write echo.
                // One physical "+" press from panel 40 to 44 reports -30 dB, but
                // writing -30 dB over USB shows 42. Both panels show 44 at -29 dB.
                bool panelStepTo44=speakerDb==-32f && db==-30f;
                float levelFromPanel=panelStepTo44 ? 0.44f : WindowsLevelForSpeaker(db);
                if(panelStepTo44) db=SpeakerDecibels(levelFromPanel);
                masterVolume.MasterVolumeLevelScalar=levelFromPanel;
                lastVolume=masterVolume.MasterVolumeLevelScalar;
                if(Math.Abs(leftDb-db)>0.05f) lv.MasterVolumeLevel=db;
                if(Math.Abs(rightDb-db)>0.05f) rv.MasterVolumeLevel=db;
                speakerDb=db;
                pendingDb=float.NaN;
            }
        }
        if(lastMute!=mute && !mute) SetMuted(false);
    }
    // USB decibels at five-percent intervals on the speaker panel. At 75%, the
    // panel skips from 74 to 76, so use the quieter of the two settings.
    static readonly float[] SpeakerCurve={
        -100,-83,-65,-58,-50,   // 0–20%
         -45,-40,-36,-32,-29,   // 25–45%
         -25,-22,-19,-17,-14,   // 50–70%
         -12, -9, -7, -5, -3, 0 // 75–100%
    };
    internal static float SpeakerDecibels(float level) {
        if(Math.Abs(level-0.44f)<0.00001f) return -29f;
        float position=Math.Max(0f,Math.Min(1f,level))*(SpeakerCurve.Length-1);
        int lower=Math.Min((int)position,SpeakerCurve.Length-2);
        float fraction=position-lower;
        float db=SpeakerCurve[lower]+fraction*(SpeakerCurve[lower+1]-SpeakerCurve[lower]);
        return (float)Math.Round(db,MidpointRounding.AwayFromZero);
    }
    internal static float WindowsLevelForSpeaker(float db) {
        db=Math.Max(SpeakerCurve[0],Math.Min(0f,db));
        int upper=1;
        while(SpeakerCurve[upper]<db) upper++;
        float low=SpeakerCurve[upper-1];
        float fraction=(db-low)/(SpeakerCurve[upper]-low);
        return (float)Math.Round(5*(upper-1+fraction),MidpointRounding.AwayFromZero)/100f;
    }
    void SetMuted(bool value) {
        leftVolume.Mute=value;
        rightVolume.Mute=value;
        lastMute=value;
    }

    // Owned by the WASAPI capture thread. Hardware panel synchronization remains
    // on the coordinator; inverse gain is sampled after each released packet.
    internal sealed class CaptureGain : IDisposable {
        readonly AudioEndpointVolume endpoint;
        readonly GainGuard guard;
        long revision;
        bool subscribed;
        public CaptureGain(AudioEndpointVolume endpoint,int bufferFrames,int sampleRate,long period100ns) {
            this.endpoint=endpoint;
            endpoint.OnVolumeNotification+=Changed;subscribed=true;
            try {
                long before=Interlocked.Read(ref revision);
                float initial=ReadGain();
                long after=Interlocked.Read(ref revision);
                long buffer100ns=bufferFrames*10000000L/sampleRate;
                long settle=buffer100ns+2*period100ns;
                guard=new GainGuard(initial,after,before==after,Now100ns(),settle,bufferFrames);
            } catch { Dispose();throw; }
        }
        void Changed(AudioVolumeNotificationData data) { Interlocked.Increment(ref revision); }
        float ReadGain() {
            float db=endpoint.MasterVolumeLevel;
            bool mute=endpoint.Mute;
            float scalar=endpoint.MasterVolumeLevelScalar;
            if(float.IsNaN(db)||float.IsInfinity(db)||float.IsNaN(scalar)||float.IsInfinity(scalar))
                throw new InvalidOperationException("源设备返回无效音量");
            return mute||scalar==0?0:(float)Math.Pow(10,-Math.Max(-96,db)/20.0);
        }
        public float ForPacket(long packet100ns,int frames,bool timestampValid,bool discontinuity) {
            long before=Interlocked.Read(ref revision);
            float actual=ReadGain();
            long after=Interlocked.Read(ref revision);
            return guard.Select(actual,after,before==after,Now100ns(),packet100ns,frames,timestampValid,discontinuity);
        }
        static long Now100ns() {
            long ticks=System.Diagnostics.Stopwatch.GetTimestamp();
            long frequency=System.Diagnostics.Stopwatch.Frequency;
            return ticks/frequency*10000000L+(ticks%frequency)*10000000L/frequency;
        }
        public string Describe() { return guard.Describe(); }
        public void Dispose() {
            if(subscribed){endpoint.OnVolumeNotification-=Changed;subscribed=false;}
        }
    }

    // Fixed state. Each successful GetBuffer/ReleaseBuffer consumes real frames.
    // Wait for a stable endpoint snapshot, then drain a whole capture buffer at
    // unity before restoring inverse gain on the following complete packet.
    // Loopback QPC can lead wall time by a driver-dependent amount, so it is
    // diagnostic only and cannot keep otherwise valid audio permanently guarded.
    internal sealed class GainGuard {
        readonly long settle;
        readonly int bufferFrames;
        float requested;
        long revision,notBefore,transitions,restarts,guardedPackets,badTimestampPackets,maxAhead;
        int remainingFrames;
        bool guarding;
        volatile string state;
        public GainGuard(float initial,long revision,bool stable,long now,long settle100ns,int bufferFrames) {
            requested=initial;this.revision=revision;settle=settle100ns;
            this.bufferFrames=bufferFrames;
            guarding=!stable;notBefore=now+settle;
            remainingFrames=stable?0:bufferFrames;
            state=guarding?"settling":"stable";
        }
        public float Select(float actual,long version,bool stable,long now,long packetStart,int frames,bool validTime,bool discontinuity=false) {
            if(frames<0 || frames>bufferFrames)throw new ArgumentOutOfRangeException("frames");
            long ahead=packetStart-now;
            if(validTime && ahead>maxAhead)Interlocked.Exchange(ref maxAhead,ahead);
            if(frames>0 && (!validTime || packetStart<=0))Interlocked.Increment(ref badTimestampPackets);
            bool changed=actual!=requested || version!=revision || !stable;
            bool restart=discontinuity && guarding;
            if(changed || restart) {
                requested=actual;revision=version;notBefore=now+settle;guarding=true;
                remainingFrames=bufferFrames;
                if(changed)Interlocked.Increment(ref transitions);
                if(restart)Interlocked.Increment(ref restarts);
            }
            if(guarding) {
                if(!stable || now<notBefore) {
                    state="settling";
                } else if(remainingFrames>0) {
                    remainingFrames=Math.Max(0,remainingFrames-frames);
                    state="draining";
                } else if(frames>0) {
                    guarding=false;
                }
                if(guarding) {
                    if(frames>0)Interlocked.Increment(ref guardedPackets);
                    return Math.Min(1f,requested);
                }
            }
            state="stable";
            return requested;
        }
        public string Describe() {
            return String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "gainState={0} gainTransitions={1} guardedPackets={2} gainBadTimestamps={3} gainAheadMs={4:F2} gainDrainFrames={5} gainRestarts={6}",
                state,Interlocked.Read(ref transitions),Interlocked.Read(ref guardedPackets),Interlocked.Read(ref badTimestampPackets),
                Interlocked.Read(ref maxAhead)/10000.0,Volatile.Read(ref remainingFrames),Interlocked.Read(ref restarts));
        }
    }
}
