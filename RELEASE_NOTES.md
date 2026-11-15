# 小米立体声 / Xiaomi Stereo v2.0.4

> **本次说明补充未改动 2.0.4 程序；测试构建新增的 30 分钟受控复测通过既定数字音频检查，未复现此前异常。前两轮失败与未归因范围仍保留。公开 EXE 本身尚未上硬件；下载文件以 GitHub Release 实际附件为准。**

> 使用前必须关闭或解散小米 App 自带的“立体声组合”和“全屋播放”，使用期间不要启用。两只音箱分别通过 USB 连接电脑。

**AI 制作说明：本软件由 AI 完成设计、开发与文档编写。** 本项目使用的是 Xiaomi 智能音箱 Pro。

## 本版更新

- 修复正常退出时尚未处理的设置请求可能丢失：退出前保存已经接受的设备、平衡、左右交换和最后的启用/暂停意图，退出期间不重新启动音频链路。
- 修复系统恢复通知与设置保存或普通输出恢复失败同时发生时，协调器可能仍停留在睡眠状态的问题。挂起/恢复状态先记录，失败后仍可按当前状态继续恢复；更新的挂起通知仍优先。
- 退出保存或恢复失败、等待超时时给出明确提示；保存和恢复同时失败时保留两类错误，提示长度有上限。确认系统会话结束时不弹出退出对话框。
- 音频处理、缓冲控制、音量曲线和双向音量联动保持不变。

## 验证范围

公开源码候选独立构建成功。ShutdownPower、Settings、Lifecycle、Audio、Routing、StateDirectory、Volume 七个软件测试正常退出；Activation 仅运行 `--headless` 子集。新增退出/电源回归包含 78 个确定性场景；另保留 11 组设置场景、28,868 次音频断言、30 个路由/配置场景，以及音量曲线回归。额外的 `AudioTests --long` 通过 28,872 次断言，覆盖虚拟时间 7,200 秒；含常规回归的进程实际运行 50.732 秒。这是仿真，不是两小时实机播放。

公开候选独立通过七个软件测试、Activation 的 --headless 子集及虚拟时间 7,200 秒的音频仿真（进程墙钟 50.732 秒，非实机两小时）。测试构建另通过约 63 秒双输出数字短测、真实音量切换与恢复及 60 轮原生窗口恢复。首轮实机长测约 19.8 分钟中止，保留未归因异常，不计为通过；第二轮 90 分钟主测量阶段结束，含 5 次计划暂停、实际播放 89 分 44.505 秒，严格纯音检查未通过（左右 6,685/7,420 个不合格窗口）。额外输入只在一段较晚观察中明确关联到 Edge，不能解释全部异常。92 次采样的私有内存为 44.676～44.879 MiB，句柄 459～481、线程 11～14；13 项结束核对及 5 个会话恢复检查全部通过。公开 EXE 本身尚未上硬件；真实睡眠唤醒和声学同步仍未验收。详情见 [验证说明](docs/VALIDATION.md)。

第二轮隔离复测于 13:49:34（UTC+8）结束，状态 completed-with-digital-failures、退出码 3，会话恢复成功。报告耗时 5,402.551 秒，主测量阶段墙钟 5,400.507 秒；包含 5 次计划暂停，实际播放 5,384.505 秒（89 分 44.505 秒），不是 90 分钟不间断播放或两小时连续播放通过。最终左右严格纯音不合格的 10 ms 窗口为 6,685/7,420，源端超阈值回调 1,287 次，三端点音量通知 0 次。左右峰值 0.821662/0.985046，源端峰值 0.278510；非有限值计数均为 0。严格纯音条件未满足，未归因窗口仍然存在，不能把所有失败窗口归因于 Edge，也不能据此直接认定程序故障。

92 次进程采样中，私有内存 44.676～44.879 MiB（末值 44.738），句柄 459～481（末值 460），线程 11～14（末值 11）。整机口径加权平均 CPU 为 0.06924%，最大采样区间平均为 0.17884%，后者不是瞬时峰值。临时句柄/线程峰值回落，观测范围有限；本轮没有预设资源验收阈值，不能据此证明长期零泄漏。结束后的 13 项核对及 5 个会话恢复检查全部通过；设置、启动、默认输出和端点状态保持。Wallpaper Engine 已恢复 active、未静音并检测到峰值约 0.114，配置哈希保持；这不是新增人耳听音结论。

### 30 分钟受控复测：通过既定数字检查

2026-11-15 14:32:06～15:02:07（UTC+8，含收尾），在无交互操作条件下，Edge 与 Wallpaper Engine 已关闭，测试临时隔离 4 个其他音频会话，运行中新发现会话 0 个。会话按约 250 ms 轮询发现，调度可能延长间隔，不能宣称瞬时、绝对隔离。测试没有重新启动原已关闭的应用。

同一测试构建完成测试，状态 completed-digital-pass、退出码 0；测试器耗时 1,802.477 秒，主测量阶段墙钟 1,800.353 秒，实际计入检查的播放为 1,797.107 秒（29 分 57.107 秒），含 1 次计划暂停与恢复，暂停及稳定等待不计入纯音判据。源端左右各 179,713 个、输出左右各 179,714 个 10 ms 窗口，不合格数均为 0；四路非有限值与完整 PCM 全零窗口也均为 0。源预期峰值 0.001666519368、实测最高 0.001666519907，超阈值回调 0；源左右 RMS 约 0.001178407170/0.001178407551。输入有效性同时核对 RMS、峰值、高峰计数和原输出判据，不只依赖 InputValid 标志；未放宽阈值、重置异常计数或保存录音。

32 次正式进程采样中，私有内存 38.629～38.891 MiB（末值 38.852），句柄 454～475（末值 454），线程 11～14（末值 12）；整机口径加权平均 CPU 0.08089%，最大采样区间平均 0.20813%，后者不是瞬时峰值，有限采样不证明永久无泄漏。19 项环境核对及 4 项会话静音恢复全部通过。程序未重启，默认输出、音量/静音、设置、自启及壁纸配置保持；测试未设置端点音量，音量通知为 0，临时静音已恢复，测试器已退出，原已关闭的应用仍保持关闭。

本轮没有复现前两轮异常，当前证据支持保留原版本；程序、音量曲线和双向联动均未改。旧失败仍保留，不能全部归因于 Edge；不合格的 10 ms 窗口数量不是爆音或断音次数。此次通过不排除未复现的偶发问题，也不替代公开 EXE、真实发声、声学同步或端到端延迟验收。700/1700 Hz 的频率投影可能漏掉整周期重复、跳过或固定延迟，低于五秒超时的无数据间断也可能逃过检查。本轮未执行真实拔插或完整睡眠唤醒，完整四路 RMS 数据见验证说明。

## 下载与升级

运行包为 `XiaomiStereo-2.0.4-win-x64.zip`，源码包为 `XiaomiStereo-2.0.4-source.zip`，校验值为 `SHA256SUMS.txt`。具体可用文件和校验值以 GitHub Release 实际附件为准。

升级前请暂停立体声并从托盘正常退出。替换运行文件时，**保留现有配置文件和状态目录**。需要 Windows 10 1703 或更新版本的 x64 系统、.NET Framework 4.8+、两只独立 USB 音箱，以及已有的可用虚拟播放设备。运行包不包含虚拟音频驱动；软件界面为中文。首次使用请在设备设置中选择自己的设备。

---

## English

> **This validation addendum does not change the 2.0.4 application. A new controlled 30-minute run of the tested build passed its existing digital audio checks without reproducing the earlier anomalies. Both earlier failed runs and attribution limits remain documented. The public EXE itself has not been installed on hardware; available downloads are determined by the actual GitHub Release attachments.**

> Disable or dissolve the Xiaomi app's native Stereo pairing and Whole-home playback before use, and keep both disabled. Connect each speaker separately by USB.

**AI development disclosure:** This software was designed, developed and documented using AI. This project uses the Xiaomi Smart Speaker Pro.

### Changes

- Saves accepted device, balance and channel-swap settings, plus the latest enable/pause intent, before normal exit. Audio is not restarted during shutdown.
- Fixes a consumed resume notification leaving the coordinator asleep when settings persistence or output restoration fails in the same iteration. Power state is recorded first and remains available for recovery; newer suspend notifications still take precedence.
- Reports shutdown save/restoration failures and timeouts. When both saving and restoration fail, a bounded message preserves both failure categories. The confirmed system-session-end path does not show an exit dialog.
- Audio processing, buffer control, the volume curve and bidirectional volume linking are unchanged.

### Validation scope

The public-source candidate built independently. Seven software suites passed: ShutdownPower, Settings, Lifecycle, Audio, Routing, StateDirectory and Volume. Activation ran only its `--headless` subset. Coverage includes 78 deterministic shutdown/power scenarios, 11 settings scenarios, 28,868 audio assertions, 30 routing/configuration scenarios and the existing volume-curve regressions. The additional `AudioTests --long` run passed 28,872 assertions, covering 7,200 seconds of virtual time; the process, including ordinary regressions, took 50.732 seconds of wall time. This is a simulation, not two hours of hardware playback.

The public candidate independently passed seven software suites, Activation's --headless subset and a 7,200-second virtual-time audio simulation (50.732 seconds of process wall time, not two hours on hardware). The tested build separately passed an approximately 63-second two-output digital check, real volume transitions/restoration and 60 native-window recovery cycles. The first hardware run was stopped after approximately 19.8 minutes with unclassified anomalies retained and is not counted as passed. The second 90-minute measurement phase completed with 5 planned pauses and 89 minutes 44.505 seconds of measured playback; the strict pure-tone check did not pass (6,685/7,420 bad left/right windows). Extra input was directly associated with Edge only during a later observation segment, not all anomalies. Across 92 samples, private memory was 44.676–44.879 MiB, handles 459–481 and threads 11–14; all 13 post-run checks and 5 session restorations passed. The public EXE itself has not been installed on hardware. Real suspend/resume and acoustic synchronization remain unverified. See [VALIDATION.md](docs/VALIDATION.md).

The isolated repeat ended at 13:49:34 (UTC+8), with phase completed-with-digital-failures, exit code 3 and successful session restoration. Reported elapsed time was 5,402.551 seconds; the main measurement phase took 5,400.507 wall-clock seconds. It included 5 planned pauses and 5,384.505 seconds of measured playback (89 minutes 44.505 seconds), not 90 uninterrupted minutes or a passed two-hour continuous run. Final left/right bad 10 ms pure-tone windows were 6,685/7,420; high-source callbacks totaled 1,287 and volume notifications across the three endpoints totaled 0. Left/right peaks were 0.821662/0.985046 and source peak was 0.278510; all nonfinite counts were 0. The strict pure-tone condition was not met. Unclassified windows remain: not all failed windows can be attributed to Edge, nor do these observations alone establish an application defect.

Across 92 process samples, private memory ranged from 44.676 to 44.879 MiB (final 44.738), handles from 459 to 481 (final 460), and threads from 11 to 14 (final 11). Weighted mean whole-machine CPU was 0.06924%; the largest sampled-interval average was 0.17884%, not an instantaneous peak. Temporary handle/thread peaks receded within this finite observation window. No resource acceptance threshold was predefined, and these results do not establish long-term freedom from leaks. All 13 post-run checks and 5 session-restoration checks passed; settings, startup, default-output roles and endpoint states were preserved. Wallpaper Engine was restored active and unmuted, with a detected peak of approximately 0.114 and unchanged configuration hash; this is not a new human listening result.

### Controlled 30-minute repeat: existing digital checks passed

On 2026-11-15 from 14:32:06 to 15:02:07 (UTC+8, including cleanup), there was no interactive input and Edge and Wallpaper Engine were already closed. Four other audio sessions were temporarily isolated; no new sessions were discovered. Discovery polls at approximately 250 ms, potentially longer with scheduling, so isolation is not instantaneous or absolute. The test did not reopen the already closed applications.

The same tested build completed with phase completed-digital-pass and exit code 0. Harness elapsed time was 1,802.477 seconds and the main measurement phase took 1,800.353 wall-clock seconds; checked playback was 1,797.107 seconds (29 minutes 57.107 seconds), with one planned pause/resume whose pause and settling interval were excluded from pure-tone criteria. Each source channel had 179,713 ten-millisecond windows and each output had 179,714, all with zero bad windows; all four lanes also had zero nonfinite values and complete all-zero PCM windows. Expected source peak was 0.001666519368 and observed maximum was 0.001666519907, with zero high-peak callbacks. Source left/right RMS was approximately 0.001178407170/0.001178407551. Input validity was cross-checked against RMS, peak, high-peak counts and the original output criteria, not just an InputValid flag. Thresholds were not relaxed, anomaly counters were not reset and no recording was saved.

Across 32 production-process samples, private memory was 38.629–38.891 MiB (final 38.852), handles 454–475 (final 454), and threads 11–14 (final 12). Whole-machine weighted mean CPU was 0.08089%; the maximum sampled-interval mean was 0.20813%, not an instantaneous peak. Finite sampling does not prove permanent absence of leaks. All 19 environment checks and 4 session-mute restorations passed. The application was not restarted; default outputs, endpoint volume/mute, settings, startup and wallpaper configuration were preserved. The test did not set endpoint volumes and observed zero volume notifications. Temporary mutes were restored, the harness exited, and the already closed applications remained closed.

The earlier anomalies did not recur, and current evidence supports retaining the existing version; the application, volume curve and bidirectional linking are unchanged. Earlier failures remain documented and cannot all be attributed to Edge; bad 10 ms window counts are not counts of audible clicks or dropouts. This pass does not exclude intermittent issues or replace public-binary, physical sound output, acoustic synchronization or end-to-end latency acceptance. Frequency projection at 700/1700 Hz may miss complete-period repetition, skipping or fixed delay; no-data gaps below the five-second timeout may also escape detection. This run did not exercise real unplug/replug or full system suspend/resume. Complete four-lane RMS ranges are in the validation document.

### Download and upgrade

The asset names are `XiaomiStereo-2.0.4-win-x64.zip`, `XiaomiStereo-2.0.4-source.zip` and `SHA256SUMS.txt`. Use the actual GitHub Release attachments for available files and checksums.

Pause stereo and exit normally before upgrading. **Preserve the existing configuration file and state directory** when replacing runtime files. Requires Windows 10 version 1703 or later, x64, .NET Framework 4.8+, two independent USB speakers and an existing compatible virtual playback endpoint. No virtual audio driver is included; the UI is Chinese. Select your own devices on first use.
