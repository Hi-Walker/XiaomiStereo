# 测试与兼容性 / Testing and compatibility

## 中文

版本 2.0.3 于 2026-11-15 完成构建，并通过 Activation、Audio、Lifecycle、Routing、Settings、StateDirectory、Volume 七类软件测试。七项均正常退出、无超时，独立测试桌面均成功关闭，测试前后输入桌面保持 Default。

- Settings：11 组设置回归，包含操作顺序交错、40,004 个请求合并、保存与恢复失败、无操作保存，以及界面中间确认和关闭重开。
- Activation：60 轮窗口恢复与释放检查，包含托管窗口列表与原生窗口句柄释放。资源采样采用相同的预热、消息处理与回收步骤；GDI 52→50、USER 57→56、句柄 523→523。有限循环未观察到累积增长，不代表长期零泄漏。
- Audio：28,868 次断言。Routing：30 个路由与配置场景。Volume：101 个音量档、21 个校准锚点及特殊档位回归。
- Lifecycle 与 StateDirectory：生命周期、外部默认输出及状态目录相关回归通过。

本轮没有新增实机听音或声学验收。以往的硬件短测不能视为 2.0.3 的新增硬件测试。

软件测试主要使用内存音频、模拟路由和隔离进程，不能替代实际硬件兼容性验证。尚未完成长时间实机连续播放、全部睡眠唤醒场景、多屏 DPI 或端到端声学同步测量。不能保证所有音箱型号兼容或两箱实际发声时差完全一致。

EXE SHA-256：`BF23458F37FB5425AC24E0D96AAA630EAD6606A4768E286DD84A2427C80B0B02`。压缩包校验值见发行版中的 `SHA256SUMS.txt`。

## English

Version 2.0.3 was built and tested on 2026-11-15. All seven software suites passed: Activation, Audio, Lifecycle, Routing, Settings, StateDirectory and Volume. Every suite exited normally without timing out; each separate test desktop was closed successfully, and the input desktop remained Default before and after testing.

- Settings: 11 regression scenarios covering interleaved operations, 40,004 coalesced requests, save and restoration failures, no-op saves, intermediate UI acknowledgements and closing/reopening the panel.
- Activation: 60 window recovery/disposal cycles, checking both managed window lists and native window handles. Resource samples used identical warm-up, message-processing and collection steps: GDI 52→50, USER 57→56, handles 523→523. No cumulative growth was observed in these finite cycles; they do not establish leak-free long-term operation.
- Audio: 28,868 assertions. Routing: 30 routing/configuration scenarios. Volume: 101 volume levels, 21 calibration anchors and special-step regression checks.
- Lifecycle and StateDirectory: lifecycle, external-default-output and state-directory regressions passed.

No new hardware listening or acoustic acceptance tests were performed. Earlier hardware spot checks are not new hardware tests of version 2.0.3.

The tests primarily use in-memory audio, simulated routing and isolated processes. They do not replace hardware compatibility validation. Long physical playback runs, all suspend/resume conditions, multi-monitor DPI and end-to-end acoustic synchronization measurements remain unverified. Compatibility with every speaker model or identical acoustic timing is not guaranteed.

EXE SHA-256: `BF23458F37FB5425AC24E0D96AAA630EAD6606A4768E286DD84A2427C80B0B02`. Archive hashes are provided in the release's `SHA256SUMS.txt`.

## 复现软件测试 / Reproducing the software tests

在 Windows x64 的 **Windows PowerShell 5.1** 中，于源码根目录执行以下命令。测试使用独立桌面，不切换当前显示桌面。运行器只允许七类软件测试；不会运行正式程序或硬件验收测试。

Run these commands from the source root in **Windows PowerShell 5.1** on Windows x64. Tests run on separate desktops without switching the displayed desktop. The runner permits only the seven software suites; it does not run the production app or hardware acceptance tests.

```powershell
.\build.ps1 -CompileTestsOnly
$testNames = 'ActivationTests','AudioTests','LifecycleTests','RoutingTests','SettingsTests','StateDirectoryTests','VolumeTests'
$testFiles = $testNames | ForEach-Object { (Resolve-Path ".\test-bin\$_.exe").Path }
.\tools\Run-PrivateDesktopTests.ps1 -TestExe $testFiles -LogDirectory (Join-Path $env:TEMP 'XiaomiStereo-Tests') -TimeoutSeconds 180
```

也可用 `./build.ps1 -Tests` 编译并直接运行软件测试；该方式会在当前桌面打开测试窗口。`HardwareAcceptance` 始终需要另行明确运行。

Alternatively, `./build.ps1 -Tests` builds and runs the software suites directly, opening test windows on the current desktop. `HardwareAcceptance` always requires a separate explicit run.
