using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// The coordinator owns endpoint volume and policy. One MTA thread owns every
// WASAPI buffer from GetBuffer through ReleaseBuffer on all three streams.
internal sealed class Bridge : ILiveBridge {
    static int activePump;
    readonly string sourceId, leftId, rightId;
    readonly object startup = new object();
    readonly Thread worker;
    volatile bool stopping, ready;
    volatile string fault, formats = "", inputState = "准备中";
    int errorCode;
    string errorType;
    Exception startupFailure;
    volatile AudioPipeline pipeline;
    VolumeLink volume;
    volatile VolumeLink.CaptureGain captureGain;
    long packets, silentPackets, discontinuities, badTimes, positionGaps, maxGapTicks;
    readonly bool initialSwap;
    readonly double initialBalance;

    public Bridge(MMDevice source, MMDevice left, MMDevice right, bool swap, double balanceDb) {
        sourceId = source.ID; leftId = left.ID; rightId = right.ID;
        initialSwap = swap; initialBalance = balanceDb;
        worker = new Thread(Run) { IsBackground = true, Name = "Stereo WASAPI" };
        worker.SetApartmentState(ApartmentState.MTA);
        if (Interlocked.CompareExchange(ref activePump, 1, 0) != 0)
            throw new InvalidOperationException("上一组音频流仍在释放");
        try { worker.Start(); }
        catch { Interlocked.Exchange(ref activePump, 0); throw; }
        try {
            lock (startup) {
                var timeout = Stopwatch.StartNew();
                while (!ready && fault == null && timeout.ElapsedMilliseconds < 4000)
                    Monitor.Wait(startup, (int)Math.Max(1, 4000 - timeout.ElapsedMilliseconds));
                if (startupFailure != null) {
                    if (startupFailure is NotSupportedException) throw new ConfigurationException(fault, startupFailure);
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(startupFailure).Throw();
                }
                if (!ready) throw new TimeoutException("等待音频设备初始化超时");
            }
            volume = new VolumeLink(source, left, right);
        } catch { Dispose(); throw; }
    }
    public string State { get { return Failed ? "故障" : inputState; } }
    public bool Failed { get { return fault != null; } }
    public int ErrorCode { get { return errorCode; } }
    public string ErrorType { get { return errorType; } }
    public string Summary {
        get {
            var p = pipeline;
            return String.Format(CultureInfo.InvariantCulture,
                "{0} {1} packets={2} silentPackets={3} discontinuities={4} timestampErrors={5} sourcePositionGaps={6} maxPacketGapMs={7:F1} {8} {9}",
                fault ?? inputState, formats, Interlocked.Read(ref packets), Interlocked.Read(ref silentPackets),
                Interlocked.Read(ref discontinuities), Interlocked.Read(ref badTimes), Interlocked.Read(ref positionGaps),
                Interlocked.Read(ref maxGapTicks)*1000.0/Stopwatch.Frequency, p == null ? "" : p.Describe(),
                captureGain == null ? "" : captureGain.Describe());
        }
    }
    public void Tick() { if (volume != null) volume.Tick(); }
    public void Resync() { var p = pipeline; if (p != null) p.BeginEpoch(); }
    public void SetMix(bool swap, double balanceDb) { pipeline.SetMix(swap, balanceDb); }
    public void Dispose() {
        stopping = true;
        bool ended = worker.Join(2000);
        if (volume != null) { volume.Dispose(); volume = null; }
        if (!ended) throw new ConfigurationException("音频设备尚未释放；已停止重连，请退出后检查设备。");
    }

    sealed class Port : IDisposable {
        MMDevice device;
        internal AudioClient Client;
        internal AudioRenderClient Render;
        internal AudioCaptureClient Capture;
        internal VolumeLink.CaptureGain Gain;
        internal WaveFormat Format;
        internal byte[] Buffer;
        internal int Frames;
        internal long Period;
        bool started;
        long lastFill;

        internal Port(MMDeviceEnumerator devices, string id, bool input, AutoResetEvent workReady) {
            try {
                device = devices.GetDevice(id);
                Client = device.AudioClient;
                Format = Client.MixFormat;
                if (!AudioPipeline.Supports(Format))
                    throw new NotSupportedException("需要双声道 32 位浮点、44.1 或 48 kHz: " + Format);
                var flags = AudioClientStreamFlags.EventCallback;
                if (input) flags |= AudioClientStreamFlags.Loopback;
                Client.Initialize(AudioClientShareMode.Shared, flags, 300000, 0, Format, Guid.Empty);
                Frames = Client.BufferSize;
                if (Frames < 1 || Frames > Format.SampleRate/2) throw new InvalidOperationException("设备缓冲长度超出范围");
                Period = Client.DefaultDevicePeriod;
                Buffer = new byte[checked(Frames * Format.BlockAlign)];
                Client.SetEventHandle(workReady.SafeWaitHandle.DangerousGetHandle());
                if (input) {
                    Capture = Client.AudioCaptureClient;
                    Gain = new VolumeLink.CaptureGain(device.AudioEndpointVolume,Frames,Format.SampleRate,Period);
                }
                else Render = Client.AudioRenderClient;
            } catch { Dispose(); throw; }
        }
        internal void Start() { Client.Start(); started = true; }
        internal void Fill(IWaveProvider samples) {
            int frames = Frames - Client.CurrentPadding;
            if (frames == 0) {
                if (started && Stopwatch.GetTimestamp()-lastFill > 3*Stopwatch.Frequency)
                    throw new InvalidOperationException("输出设备已连续三秒未消费缓冲");
                return;
            }
            if (frames < 0 || frames > Frames) throw new InvalidOperationException("输出缓冲水位异常");
            int bytes = frames * Format.BlockAlign;
            samples.Read(Buffer, 0, bytes);
            IntPtr dest = Render.GetBuffer(frames);
            var flags = AudioClientBufferFlags.Silent;
            try { Marshal.Copy(Buffer, 0, dest, bytes); flags = AudioClientBufferFlags.None; }
            finally { Render.ReleaseBuffer(frames, flags); }
            lastFill = Stopwatch.GetTimestamp();
        }
        public void Dispose() {
            try { if (started) Client.Stop(); } catch (COMException) {}
            finally {
                try { if (Gain != null) Gain.Dispose(); }
                finally {
                    try { if (Client != null) Client.Dispose(); }
                    finally { if (device != null) device.Dispose(); }
                }
            }
        }
    }
    void Run() {
        try {
            using (var devices = new MMDeviceEnumerator())
            using (var workReady = new AutoResetEvent(false))
            using (var source = new Port(devices, sourceId, true, workReady))
            using (var left = new Port(devices, leftId, false, workReady))
            using (var right = new Port(devices, rightId, false, workReady)) {
                var p = new AudioPipeline(source.Format.SampleRate, left.Format.SampleRate, right.Format.SampleRate);
                p.SetMix(initialSwap, initialBalance);
                pipeline = p;
                captureGain = source.Gain;
                formats = String.Format(CultureInfo.InvariantCulture,
                    "source/L/R Hz={0}/{1}/{2} bufferFrames={3}/{4}/{5} defaultPeriod100ns={6}/{7}/{8}",
                    source.Format.SampleRate, left.Format.SampleRate, right.Format.SampleRate,
                    source.Frames, left.Frames, right.Frames, source.Period, left.Period, right.Period);
                left.Fill(p.Left); right.Fill(p.Right);
                left.Start(); right.Start(); source.Start();
                inputState = "等待输入";
                lock (startup) { ready = true; Monitor.PulseAll(startup); }
                long lastPacket = 0, expectedPosition = -1;
                while (!stopping) {
                    // A coalesced wake services all three clients. Close the shared
                    // event only after the last client stops referencing its handle.
                    workReady.WaitOne(50);
                    if (stopping) break;
                    // Cap work per pass so a backlog cannot starve either output.
                    for (int i=0; i<16 && source.Capture.GetNextPacketSize()>0; i++) {
                        long position, qpc;
                        int frames;
                        AudioClientBufferFlags flags;
                        IntPtr data = source.Capture.GetBuffer(out frames,out flags,out position,out qpc);
                        bool silent = (flags & AudioClientBufferFlags.Silent) != 0;
                        try {
                            if (frames < 0 || frames > source.Frames) throw new InvalidOperationException("采集包大小超出设备缓冲");
                            if (frames != 0 && !silent) Marshal.Copy(data,source.Buffer,0,frames * source.Format.BlockAlign);
                        } finally { source.Capture.ReleaseBuffer(frames); }
                        if (frames == 0) break;
                        long now = Stopwatch.GetTimestamp();
                        bool gap = false;
                        if (lastPacket != 0) {
                            long elapsed = now - lastPacket;
                            if (elapsed > maxGapTicks) Interlocked.Exchange(ref maxGapTicks,elapsed);
                            gap = elapsed > Stopwatch.Frequency/4;
                        }
                        if ((flags & AudioClientBufferFlags.DataDiscontinuity) != 0) {
                            Interlocked.Increment(ref discontinuities); gap |= lastPacket != 0;
                        }
                        if ((flags & AudioClientBufferFlags.TimestampError) != 0) {
                            Interlocked.Increment(ref badTimes); expectedPosition = -1;
                        } else {
                            if (expectedPosition >= 0 && position != expectedPosition) {
                                Interlocked.Increment(ref positionGaps); gap = true;
                            }
                            expectedPosition = position + frames;
                        }
                        if (gap) p.BeginEpoch();
                        float packetGain=source.Gain.ForPacket(qpc,frames,(flags & AudioClientBufferFlags.TimestampError)==0,gap);
                        lastPacket = now;
                        Interlocked.Increment(ref packets);
                        if (silent) {
                            Interlocked.Increment(ref silentPackets); p.PushSilence(frames); inputState = "静音包";
                        } else {
                            int bytes = frames * source.Format.BlockAlign;
                            p.Push(source.Buffer,bytes,packetGain); inputState = "正在转发";
                        }
                    }
                    left.Fill(p.Left); right.Fill(p.Right);
                    if (lastPacket != 0 && Stopwatch.GetTimestamp()-lastPacket > Stopwatch.Frequency/4)
                        inputState = "等待输入";
                }
            }
        } catch (Exception ex) {
            lock (startup) {
                if (!ready) startupFailure = ex;
                errorCode = ex.HResult; errorType = ex.GetType().Name;
                fault = BoundedDiagnostics.Limit(ex.GetType().Name + " 0x" + ex.HResult.ToString("X8") + " " + ex.Message,240);
                Monitor.PulseAll(startup);
            }
        } finally { Interlocked.Exchange(ref activePump, 0); }
    }
}
