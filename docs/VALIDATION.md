# 测试与兼容性 / Testing and compatibility

## 2.0.4 候选状态

2026-11-15，公开源码候选完成独立构建和下列软件回归。七个不显示窗口的软件测试，加上 Activation 的 `--headless` 子集，均正常退出、没有超时。此后测试构建补做硬件短测、真实音量切换和完整原生 Activation；首轮硬件长测发现未归因信号异常后正常停止，不计为通过；第二轮 90 分钟主测量阶段已结束，严格纯音检测未通过；资源与恢复另行记录，不能汇总为长测全部通过。随后在更受控条件下完成 30 分钟复测，通过既定数字检查；版本与音频实现没有改变，旧失败及归因边界仍保留。公开 EXE 本身尚未上硬件，下载文件以 GitHub Release 实际附件为准。

| 测试 | 本轮执行及结果 |
| --- | --- |
| ShutdownPower | 78 个确定性场景通过；覆盖退出时待处理设置和启用意图、重绑中退出、保存/恢复失败、挂起/恢复与异常交错、新电源事件优先。使用模拟设备，没有触发真实电源事件。 |
| Settings | 11 组场景通过；覆盖设置合并、确认、失败和未显示面板的关闭重开。 |
| Lifecycle | 生命周期、默认输出意图、有限诊断及未显示控制面板/校准界面测试通过。 |
| Audio | 28,868 次断言通过；使用内存音频与模拟速率，不代表实机声学同步或延迟。 |
| Audio --long | 额外一次执行通过 28,872 次断言。模拟左右输出时钟 −500/+500 ppm，虚拟时间 7,200 秒；含常规回归的整个进程墙钟为 50.732 秒。不是两小时实机播放。 |
| Routing | 30 个路由与配置场景通过。 |
| StateDirectory | 默认目录、环境变量展开、启动参数和无效路径行为通过。 |
| Volume | 101 个音量档、21 个校准锚点及特殊档位、增益切换回归通过。 |
| Activation | 仅 `--headless`：参数解析、跨进程唤醒信号及默认目录异常隔离通过；原生窗口恢复和资源循环跳过。 |

公开候选最初在隔离环境中验证，`CreateDesktop` 返回 `ERROR_ACCESS_DENIED`（错误 5），所以其独立测试只执行上表中的无窗口范围，没有自动退回用户可见桌面。之后恢复真实桌面访问，测试构建另行完成以下补充验证。两组证据不得合并成“公开 EXE 的八个完整套件及硬件验收全部通过”。

### 测试构建的补充证据

测试构建版本为 2.0.4.0，EXE SHA-256 为 `AC8EEA3DE974A23CC6340393CC77B2AC765D5D9A7CCB4CD3219D8C8AEB23C546`。该测试构建与公开构建共享本次控制流程修复和音频实现。**公开 EXE 本身尚未进行硬件测试；以下不能视为公开二进制的硬件验收。**

- 安装核对：原设备设置、启动项、相邻配置和 Wallpaper Engine 例外规则保持；默认输出角色、三端点音量保持，静默启动成功。这里核对了壁纸配置保留，没有新增人工壁纸听音结论。
- 真实双输出短测：报告耗时 62.997 秒，其中播放 60.962 秒。两路物理输出端点的数字回环均通过；期望频率幅度约 0.006，badWindows 与非有限值计数均为 0。测试没有修改三端点音量，会话恢复完成。该数字检测不等于麦克风测量或人耳听音。
- 真实音量切换：Windows 按 10→75→25→60→10→75→40% 切换；两路稳态 RMS 均为 0.008485、观察到的峰值均为 0.012，退出码 0。测试后恢复 Windows 48%、两路 USB −27 dB，三端点原状态恢复成功。
- 本轮完整原生 Activation：在独立桌面正常退出，无超时，桌面成功关闭，输入桌面前后保持 Default。完成 60 轮窗口恢复与释放；固定采样中 GDI 52→50、USER 56→55，句柄从 514 到 20/40/60 轮后的 515/516/516。结果通过既定检查，但有限循环不证明长期零泄漏。这是 2.0.4 本轮新执行结果，未沿用旧版结果。
- 首轮硬件长测：2026-11-15 11:54（UTC+8）启动，原计划 120 分钟。约第 14 分钟观察到累计左右 22/27 个不合格的 10 ms 窗口，此后直到结束计数没有增加。左/右最高峰值为 0.345947/0.403596，而测试音期望幅度为 0.006。来源尚未确定，**不能据此归因于 Wallpaper Engine 或程序本身，也不能将此轮记为通过**。
- 首轮于 12:13:58 正常请求停止：报告耗时 1,185.990 秒（约 19.8 分钟），进程墙钟 1,186.062 秒，退出码 2、状态 cancelled，原会话恢复成功。两侧非有限值计数为 0；测试未修改端点音量。原始记录完整保留，后续复测不会替代或抹去这一轮。
- 第二轮隔离复测：2026-11-15 12:19:31（UTC+8）开始，计划 90 分钟。只增强测试器的源端峰值和音量通知观测，生产音频/音量代码未改；Wallpaper Engine 已暂停。
- 第二轮约 28 分 44 秒（约 12:48）出现源端峰值 0.112225，随后左右物理输出数字峰值约 0.340836/0.404318。当时源端高峰值回调累计 6 次，左右严格纯音不合格窗口累计 20/27，三端点音量事件计数为 0。这些数据说明当时源音频不满足纯测试音条件，但没有识别该段输入的具体应用；Wallpaper 已暂停，不能将该异常归因于壁纸。
- 13:07:35～13:08:21 的 46 次只读采样中，新增的另一段事件里，只读源会话观察器观测到 Edge 会话处于 active、未静音，并有与源同步的约 0.1～0.9 峰值；其他非观察器会话峰值均为 0。**这只能确认该段新事件存在 Edge 的额外输入，不能倒推首轮或第二轮早先事件也来自 Edge。** 观察器没有修改音频会话状态。
- 第二轮隔离复测于 13:49:34（UTC+8）结束，状态 completed-with-digital-failures、退出码 3，会话恢复成功。报告耗时 5,402.551 秒，主测量阶段墙钟 5,400.507 秒；包含 5 次计划暂停，实际播放 5,384.505 秒（89 分 44.505 秒），不是 90 分钟不间断播放或两小时连续播放通过。最终左右严格纯音不合格的 10 ms 窗口为 6,685/7,420，源端超阈值回调 1,287 次，三端点音量通知 0 次。左右峰值 0.821662/0.985046，源端峰值 0.278510；非有限值计数均为 0。严格纯音条件未满足，未归因窗口仍然存在，不能把所有失败窗口归因于 Edge，也不能据此直接认定程序故障。
- 92 次进程采样中，私有内存 44.676～44.879 MiB（末值 44.738），句柄 459～481（末值 460），线程 11～14（末值 11）。整机口径加权平均 CPU 为 0.06924%，最大采样区间平均为 0.17884%，后者不是瞬时峰值。临时句柄/线程峰值回落，观测范围有限；本轮没有预设资源验收阈值，不能据此证明长期零泄漏。结束后的 13 项核对及 5 个会话恢复检查全部通过；设置、启动、默认输出和端点状态保持。Wallpaper Engine 已恢复 active、未静音并检测到峰值约 0.114，配置哈希保持；这不是新增人耳听音结论。

### 30 分钟受控复测：通过既定数字检查

2026-11-15 14:32:06～15:02:07（UTC+8，含收尾），在无交互操作条件下，Edge 与 Wallpaper Engine 已关闭。测试临时隔离 4 个其他音频会话，运行中新发现会话 0 个。会话发现按约 250 ms 轮询，调度可能延长间隔，不能宣称瞬时、绝对隔离；测试没有重新启动原已关闭的应用。

同一测试构建完成测试，状态 completed-digital-pass、退出码 0。测试器耗时 1,802.477 秒，主测量阶段墙钟 1,800.353 秒，实际计入检查的播放为 1,797.107 秒（29 分 57.107 秒），包含 1 次计划暂停与恢复；暂停及其稳定等待不计入纯音判据。正式进程未重启，程序、版本、音量曲线和双向联动均未修改。

| 观察通道 | 10 ms 窗口 | 不合格窗口 | RMS 范围 |
| --- | ---: | ---: | --- |
| 源左 | 179,713 | 0 | 0.001178407170～0.001178407170 |
| 源右 | 179,713 | 0 | 0.001178407551～0.001178407551 |
| 左输出 | 179,714 | 0 | 0.004242580438～0.004242648569 |
| 右输出 | 179,714 | 0 | 0.004241118154～0.004244222824 |

四路非有限值与完整 PCM 全零窗口均为 0，源端超阈值回调为 0。源预期峰值 0.001666519368、实测最高 0.001666519907；两路输出峰值约 0.006。输入有效性同时通过 RMS、峰值、高峰计数和原输出判据核对，不只依赖 InputValid 标志；检查阈值未放宽，异常计数未重置，没有保存音频录音。测试未设置端点音量，收到音量通知 0 次。

正式进程共 32 个资源样本：私有内存 38.629～38.891 MiB（末值 38.852），句柄 454～475（末值 454），线程 11～14（末值 12）；整机口径加权平均 CPU 0.08089%，最大采样区间平均 0.20813%，不是瞬时峰值。有限采样不能证明永久无泄漏。19 项环境核对及 4 项会话静音恢复检查全部通过；默认输出、音量/静音、设置、自启和壁纸配置保持，测试器已退出，临时静音已恢复，原已关闭的 Edge/Wallpaper Engine 仍保持关闭。

**本轮没有复现前两轮异常，当前证据支持保留原版本；它不能把旧失败改写为通过，也不能将所有旧异常归因于 Edge。** 不合格的 10 ms 窗口数量不是爆音或断音次数。此次通过不排除未复现的偶发问题。这仍是测试构建的 WASAPI/USB 数字回环观察，公开 EXE 本身没有运行在本轮硬件测试中；不能证明音箱实际发声、声学同步或端到端延迟，也未执行真实拔插或完整睡眠唤醒。

真实系统睡眠唤醒、拔插兼容性复验、多屏 DPI、端到端声学同步与延迟测量仍未完成。数字频率投影不是逐采样连续性证明：700/1700 Hz 在每个 10 ms 窗口恰为 7/17 个周期，因此某些整周期重复、跳过或固定延迟仍可能不可见，低于五秒超时的无数据间断也可能逃过检查。前两轮长测未采集 RMS；新增 30 分钟测试已采集上表的四路 RMS。三轮均未采集内部队列/时钟计数，不能从旧峰值推算旧 RMS，也不能把回调先后时间当成声学延迟。长测工具目前未包含在公开候选源码包中。

以下六个核心文件与公开版 v2.0.3 的 Git blob 完全相同：`AudioPipeline.cs`、`Bridge.cs`、`Config.cs`、`Diagnostics.cs`、`Routing.cs`、`VolumeLink.cs`。

公开候选 EXE SHA-256：`CB18B3EF57802B8828BCAEE6A1C1A05C721F730851923B287C0F242D44692BE6`。候选归档校验值见与 ZIP 一同提供的 `SHA256SUMS.txt`；下载文件以 GitHub Release 实际附件为准。

## English

On 2026-11-15, the public-source candidate built independently and completed seven software suites without showing windows, plus Activation's `--headless` subset. All invoked processes exited normally without timeouts. The tested build subsequently completed hardware spot checks, real volume transitions and full native Activation. Its initial hardware run was stopped after unclassified signal anomalies and is not counted as passed. The second 90-minute measurement phase is complete; its strict pure-tone check did not pass. Resource observations and restoration are reported separately, not as an all-passed hardware result. A subsequent controlled 30-minute repeat passed the existing digital checks without changing the version or audio implementation; the earlier failures and attribution limits remain documented. The public EXE itself has not been installed for hardware validation. Available downloads are determined by the actual GitHub Release attachments.

- ShutdownPower: 78 deterministic scenarios covering pending settings and enable intent at exit, exit during rebind, save/restoration failures, interleaved power events and newer power-event precedence. Devices and power notifications are simulated.
- Settings: 11 settings merge/acknowledgement/failure scenarios, including closing and reopening an unshown panel.
- Lifecycle: lifecycle, default-output intent, bounded diagnostics and unshown control/calibration UI checks.
- Audio: 28,868 assertions using in-memory audio and simulated rates; these do not measure acoustic synchronization or end-to-end latency.
- Audio --long: an additional run passed 28,872 assertions, including a 7,200-second virtual-time simulation with left/right clocks at −500/+500 ppm. The entire process, including ordinary regressions, took 50.732 seconds of wall time. This is not two hours of hardware playback.
- Routing: 30 routing/configuration scenarios. StateDirectory: default paths, environment expansion, launch arguments and invalid paths.
- Volume: 101 levels, 21 anchors, special steps and source-gain transition regressions.
- Activation: only `--headless` argument parsing, cross-process activation signalling and invalid-default-path isolation. Native window restoration and resource cycles were skipped.

The public candidate was initially tested in an isolated environment where `CreateDesktop` returned `ERROR_ACCESS_DENIED` (error 5). Its independent run therefore used only the headless scope above, without falling back to the visible desktop. After real desktop access returned, the tested build completed the supplemental checks below. These evidence sets must not be combined into a claim that the public EXE passed all eight complete suites and hardware acceptance.

### Supplemental evidence for the tested build

Tested build version: 2.0.4.0. EXE SHA-256: `AC8EEA3DE974A23CC6340393CC77B2AC765D5D9A7CCB4CD3219D8C8AEB23C546`. This tested build shares the control-flow fixes and audio implementation with the public build. **The public EXE itself has not undergone hardware testing; these results are not hardware acceptance of the public binary.**

- Installation checks preserved device settings, startup registration, adjacent configuration, Wallpaper Engine exception rules, default-output roles and all three endpoint volumes; silent startup succeeded. Preserved wallpaper configuration is not a new human listening check.
- Real two-output spot check: 62.997 seconds elapsed, including 60.962 seconds playing. Both physical-endpoint digital loopbacks passed; expected-frequency amplitude was approximately 0.006, with zero bad windows and nonfinite samples. The test did not modify endpoint volumes and completed session restoration. Digital capture is not a microphone or human listening measurement.
- Real volume transitions: Windows 10→75→25→60→10→75→40%; both outputs had steady RMS 0.008485 and observed peak 0.012. Exit code was 0, and all three endpoint states were restored (Windows 48%, both USB endpoints −27 dB).
- Full native Activation in this run: normal exit, no timeout, separate desktop closed, and input desktop remained Default. All 60 window recovery/disposal cycles passed. Fixed resource samples were GDI 52→50, USER 56→55 and handles 514→515/516/516 after 20/40/60 cycles. The finite checks do not establish leak-free long-term operation; these are newly executed 2.0.4 results, not reused older measurements.
- Initial hardware run: started on 2026-11-15 at 11:54 (UTC+8), originally planned for 120 minutes. Around minute 14, 22/27 cumulative bad 10 ms windows were observed on the left/right; the counts did not increase before the run ended. Peak levels were 0.345947/0.403596 while the expected test-tone amplitude was 0.006. The source is undetermined: **these observations cannot be attributed to Wallpaper Engine or the application, and this run is not counted as passed**.
- The first run was stopped normally at 12:13:58: reported elapsed time 1,185.990 seconds (about 19.8 minutes), process wall time 1,186.062 seconds, exit code 2 and phase cancelled. Original sessions were restored. Both nonfinite counters were zero, and endpoint volumes were not modified by the test. The original records are retained; a later result will not replace or erase this run.
- Second isolated run: started on 2026-11-15 at 12:19:31 (UTC+8), planned for 90 minutes. Only the test harness gained source-peak and volume-notification observation; production audio/volume code was unchanged. Wallpaper Engine was paused.
- At approximately 28 minutes 44 seconds (around 12:48), the source reached a peak of 0.112225, followed by physical-output digital peaks of approximately 0.340836/0.404318. At that observation point, there were 6 high-source-peak callbacks, 20/27 bad strict pure-tone windows and 0 volume events across the three endpoints. This shows the source did not meet the pure-test-tone condition, but it did not identify the specific application for that segment. Wallpaper was paused, so these observations must not be attributed to it.
- During 46 read-only snapshots from 13:07:35 to 13:08:21, in a separate later event, a read-only source-session observer saw an active, unmuted Edge session with peaks approximately 0.1–0.9 synchronized with the source; the other non-observer sessions had zero peaks. **This confirms additional Edge input during that later event only; it does not establish that Edge caused the first-run anomaly or the earlier event in the second run.** The observer did not change audio-session state.
- The isolated repeat ended at 13:49:34 (UTC+8), with phase completed-with-digital-failures, exit code 3 and successful session restoration. Reported elapsed time was 5,402.551 seconds; the main measurement phase took 5,400.507 wall-clock seconds. It included 5 planned pauses and 5,384.505 seconds of measured playback (89 minutes 44.505 seconds), not 90 uninterrupted minutes or a passed two-hour continuous run. Final left/right bad 10 ms pure-tone windows were 6,685/7,420; high-source callbacks totaled 1,287 and volume notifications across the three endpoints totaled 0. Left/right peaks were 0.821662/0.985046 and source peak was 0.278510; all nonfinite counts were 0. The strict pure-tone condition was not met. Unclassified windows remain: not all failed windows can be attributed to Edge, nor do these observations alone establish an application defect.
- Across 92 process samples, private memory ranged from 44.676 to 44.879 MiB (final 44.738), handles from 459 to 481 (final 460), and threads from 11 to 14 (final 11). Weighted mean whole-machine CPU was 0.06924%; the largest sampled-interval average was 0.17884%, not an instantaneous peak. Temporary handle/thread peaks receded within this finite observation window. No resource acceptance threshold was predefined, and these results do not establish long-term freedom from leaks. All 13 post-run checks and 5 session-restoration checks passed; settings, startup, default-output roles and endpoint states were preserved. Wallpaper Engine was restored active and unmuted, with a detected peak of approximately 0.114 and unchanged configuration hash; this is not a new human listening result.

### Controlled 30-minute repeat: existing digital checks passed

On 2026-11-15 from 14:32:06 to 15:02:07 (UTC+8, including cleanup), there was no interactive input and Edge and Wallpaper Engine were already closed. Four other audio sessions were temporarily isolated; no new sessions were discovered during the run. Discovery polls at approximately 250 ms, potentially longer with scheduling, so isolation is not instantaneous or absolute. The test did not reopen the already closed applications.

The same tested build completed with phase completed-digital-pass and exit code 0. Harness elapsed time was 1,802.477 seconds and the main measurement phase took 1,800.353 wall-clock seconds; checked playback was 1,797.107 seconds (29 minutes 57.107 seconds), including one planned pause/resume whose pause and settling interval were excluded from pure-tone criteria. The production process was not restarted. The application, version, volume curve and bidirectional volume linking were unchanged.

| Observed channel | 10 ms windows | Bad windows | RMS range |
| --- | ---: | ---: | --- |
| Source left | 179,713 | 0 | 0.001178407170–0.001178407170 |
| Source right | 179,713 | 0 | 0.001178407551–0.001178407551 |
| Left output | 179,714 | 0 | 0.004242580438–0.004242648569 |
| Right output | 179,714 | 0 | 0.004241118154–0.004244222824 |

All four lanes had zero nonfinite values and complete all-zero PCM windows; high-source callbacks totaled zero. Expected source peak was 0.001666519368 and observed maximum was 0.001666519907; output peaks were approximately 0.006. Input validity was cross-checked against RMS, peak, high-peak counts and the original output criteria, not just an InputValid flag. Thresholds were not relaxed, anomaly counters were not reset, and no audio recording was saved. The test did not set endpoint volumes and observed zero volume notifications.

There were 32 production-process resource samples: private memory 38.629–38.891 MiB (final 38.852), handles 454–475 (final 454), and threads 11–14 (final 12). Whole-machine weighted mean CPU was 0.08089%; the maximum sampled-interval mean was 0.20813%, not an instantaneous peak. Finite samples do not prove permanent absence of leaks. All 19 environment checks and 4 session-mute restorations passed. Default outputs, endpoint volume/mute, settings, startup and wallpaper configuration were preserved; the harness exited, temporary mutes were restored, and the already closed Edge/Wallpaper Engine applications remained closed.

**The earlier anomalies did not recur, and the current evidence supports retaining the existing version; it does not convert either earlier failure into a pass or attribute all earlier anomalies to Edge.** A count of bad 10 ms windows is not a count of audible clicks or dropouts. This passing run does not exclude intermittent issues that did not recur. It remains a WASAPI/USB digital loopback observation of the tested build; the public EXE itself did not run in this hardware test. It does not establish physical sound output, acoustic synchronization or end-to-end latency, and did not exercise real unplug/replug or full system suspend/resume.

Real system suspend/resume, renewed unplug/replug checks, multi-monitor DPI and end-to-end acoustic timing remain unverified. Frequency projection is not sample-exact continuity proof: at 700/1700 Hz, each 10 ms window contains exactly 7/17 periods, so some complete-period repetition, skipping or fixed delay may remain invisible; no-data gaps below the five-second timeout may also escape detection. The first two soak runs did not collect RMS; the new 30-minute test collected all four RMS ranges shown above. None of the three runs collected internal queue/clock counters. Old RMS cannot be inferred from old peaks, and callback timing is not acoustic latency. The hardware soak tool is not currently included in the public candidate source archive.

Six core files are byte-identical to the public v2.0.3 Git blobs: `AudioPipeline.cs`, `Bridge.cs`, `Config.cs`, `Diagnostics.cs`, `Routing.cs` and `VolumeLink.cs`.

Public candidate EXE SHA-256: `CB18B3EF57802B8828BCAEE6A1C1A05C721F730851923B287C0F242D44692BE6`. Candidate archive checksums are provided alongside the ZIP archives in `SHA256SUMS.txt`; use the actual GitHub Release attachments for available downloads.

## 复现本轮范围 / Reproducing this run

在 Windows x64 / PowerShell 中于源码根目录执行。下面不会主动显示测试窗口；Activation 仅运行无窗口子集。运行器或构建错误须先排查，不应直接跳过报错后声称全部通过。

Run from the source root on Windows x64 with PowerShell. These commands do not show test windows; Activation runs only its headless subset. Investigate any errors instead of skipping them and claiming success.

```powershell
.\build.ps1 -CompileTestsOnly
$testNames = 'ShutdownPowerTests','SettingsTests','LifecycleTests','AudioTests','RoutingTests','StateDirectoryTests','VolumeTests'
foreach ($test in $testNames) {
    & ".\test-bin\$test.exe"
    if ($LASTEXITCODE -ne 0) { throw "Failed: $test" }
}
& .\test-bin\ActivationTests.exe --headless
if ($LASTEXITCODE -ne 0) { throw 'Failed: ActivationTests --headless' }
& .\test-bin\AudioTests.exe --long
if ($LASTEXITCODE -ne 0) { throw 'Failed: AudioTests --long' }
```

## 完整窗口测试 / Full native-window tests

交互式 Windows 会话允许创建独立桌面时，可使用 Windows PowerShell 5.1 执行以下命令。公开候选的独立回归没有执行此完整范围；测试构建后来单独补做了完整 Activation，结果见上文。独立桌面创建失败时不要自动改在当前桌面运行；`build.ps1 -Tests` 会直接在当前桌面运行测试并可能显示窗口。

When an interactive Windows session permits separate-desktop creation, run the following in Windows PowerShell 5.1. The public candidate's independent regression did not run this full scope; the tested build subsequently completed full Activation separately, as documented above. Do not fall back to the visible desktop on failure; `build.ps1 -Tests` directly runs tests and can show windows.

```powershell
.\build.ps1 -CompileTestsOnly
$testNames = 'ShutdownPowerTests','ActivationTests','AudioTests','LifecycleTests','RoutingTests','SettingsTests','StateDirectoryTests','VolumeTests'
$testFiles = $testNames | ForEach-Object { (Resolve-Path ".\test-bin\$_.exe").Path }
.\tools\Run-PrivateDesktopTests.ps1 -TestExe $testFiles -LogDirectory (Join-Path $env:TEMP 'XiaomiStereo-Tests') -TimeoutSeconds 180
```

硬件测试需要另行明确运行，不能把软件测试结果视为硬件验收。

Hardware tests require a separate explicit run; software regression results are not hardware acceptance.
