using System;

internal static class VolumeTests {
    static void Check(bool condition,string name) { if(!condition)throw new Exception(name); }
    static void CaptureTransitions() {
        const long ms=10000,settle=50*ms;
        var guard=new VolumeLink.GainGuard(100,0,true,100*ms,settle,1440);
        Check(guard.Select(100,0,true,120*ms,110*ms,480,true)==100,"stable startup gain");
        Check(guard.Select(10,1,true,130*ms,120*ms,480,true)==1,"upward source volume immediately guarded");
        Check(guard.Select(10,1,true,179*ms,175*ms,1440,true)==1,"pre-deadline packets cannot drain guard");
        Check(guard.Select(10,1,true,180*ms,218*ms,480,true)==1,"first drain packet");
        Check(guard.Select(10,1,true,190*ms,228*ms,960,true)==1,"last drain packet remains unity");
        Check(guard.Select(10,1,true,200*ms,238*ms,480,true)==10,"following whole packet restores gain");
        Check(guard.Select(100,2,true,210*ms,200*ms,480,true)==1,"downward source volume with old captured samples");
        Check(guard.Select(10,3,true,220*ms,210*ms,480,true)==1,"rapid return renews guard");
        Check(guard.Select(10,4,true,225*ms,215*ms,480,true)==1,"same-value notification renews guard");
        Check(guard.Select(10,4,false,230*ms,220*ms,480,true)==1,"snapshot race");
        Check(guard.Select(10,4,true,300*ms,0,1440,false)==1,"timestamp-error packet still represents consumed frames");
        Check(guard.Select(10,4,true,310*ms,0,480,false)==10,"valid frame drain recovers with timestamp errors");
        Check(guard.Describe().Contains("gainBadTimestamps=2"),"timestamp errors remain diagnosed");
        Check(guard.Select(0,5,true,320*ms,300*ms,480,true)==0,"mute immediate");
        Check(guard.Select(100,6,true,330*ms,310*ms,480,true)==1,"unmute protects queued data");
        Check(guard.Select(100,6,true,400*ms,380*ms,1440,true)==1,"unmute full-buffer drain");
        Check(guard.Select(100,6,true,410*ms,390*ms,480,true)==100,"unmute next packet");
        Check(guard.Select(100,6,true,4410*ms,9000*ms,48,true,true)==100,"stable gain preserves resumed short sound");
        Check(guard.Select(10,7,true,5410*ms,9000*ms,480,true,true)==1,"volume changed while idle starts protection");
        Check(guard.Select(10,7,true,5500*ms,9000*ms,1440,true)==1,"resume drain still guarded");
        Check(guard.Select(10,7,true,5510*ms,9000*ms,480,true,true)==1,"gap before first restored packet restarts pending guard");
        Check(guard.Select(10,7,true,5600*ms,9000*ms,1440,true)==1,"restarted guard drains");
        Check(guard.Select(10,7,true,5610*ms,9000*ms,480,true)==10,"resume recovers with arbitrary timestamp lead");
        Console.WriteLine("PASS source gain startup, rise/fall, rapid return, snapshot race, completed-drain boundary, mute/unmute and resume");
    }
    static void BufferedPackets() {
        const long ms=10000,period=10*ms,start=100*ms;
        foreach(int rate in new[]{44100,48000})
        foreach(int bufferFrames in new[]{rate/100,rate*3/100,rate/10})
        foreach(int frames in new[]{1,rate/1000,rate/100,bufferFrames})
        foreach(long timestamp in new[]{0L,start-1,start+20*ms,start+379300,start+30000*ms}) {
            long settle=bufferFrames*10000000L/rate+2*period;
            var guard=new VolumeLink.GainGuard(100,0,true,0,settle,bufferFrames);
            bool valid=timestamp!=0;
            Check(guard.Select(10,1,true,start,timestamp,frames,valid)==1,"transition begins protected");
            Check(guard.Select(10,1,true,start+settle-1,timestamp,bufferFrames,valid)==1,"full early buffer does not count");
            long now=start+settle+30000*ms;
            Check(guard.Select(10,1,true,now,timestamp,0,valid)==1,"long no-input wait does not consume frames");
            int remaining=bufferFrames;
            while(remaining>0) {
                int block=Math.Min(frames,remaining);
                Check(guard.Select(10,1,true,now,timestamp,block,valid)==1,"old queued frames consumed at unity");
                remaining-=block;
            }
            Check(guard.Select(10,1,true,now,timestamp,0,valid)==1,"empty packet cannot restore gain");
            Check(guard.Select(10,1,true,now,timestamp,frames,valid)==10,"first complete packet after drain restored");
            bool rejected=false;
            try{guard.Select(10,1,true,now,timestamp,bufferFrames+1,valid);}catch(ArgumentOutOfRangeException){rejected=true;}
            Check(rejected,"oversized packet rejected");
        }
        Console.WriteLine("PASS 44.1/48k, actual-buffer/packet sizes, idle and old backlog; zero/stale/20ms/37.93ms/arbitrary QPC are diagnostic");
    }
    static int Main() {
        CaptureTransitions();BufferedPackets();
        var calibrated=new[]{-100,-83,-65,-58,-50,-45,-40,-36,-32,-29,-25,-22,-19,-17,-14,-12,-9,-7,-5,-3,0};
        float previous=-101;
        for(int i=0;i<=100;i++){
            float db=VolumeLink.SpeakerDecibels(i/100f);
            if(db<previous || (i%5==0 && db!=calibrated[i/5])) return 1;
            float back=VolumeLink.WindowsLevelForSpeaker(db);
            if(Math.Abs(VolumeLink.SpeakerDecibels(back)-db)>1) return 2;
            previous=db;
        }
        if(VolumeLink.SpeakerDecibels(.44f)!=-29 || VolumeLink.SpeakerDecibels(.75f)!=-12) return 3;
        Console.WriteLine("PASS 101 master levels monotonic, 21 calibration anchors, 44% key and 75% panel exceptions");
        return 0;
    }
}
