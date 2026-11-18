# 测试与兼容性 / Testing and compatibility

## 中文

2.0.7 将主题选择收进控制面板的月亮菜单，并保留「恢复居中」按钮的布局修复。主题选择保存在 `ui.json`。Windows 高对比度优先。主题切换无需重新打开音频设备。

2.0.7 测试构建的最终 ThemeTests 主测 423 项与资源子测 164 项断言通过。17 次标题栏属性回读通过，跳过 0 次。启动窗口检查通过 60 轮，设置通过 11 个场景，生命周期回归通过。首轮资源检查曾超过句柄增长阈值。最终复测沿用原阈值并通过，有限次数的开关窗口只反映测试期间的资源变化。

本次发布构建的核对结果见下文。旧版音频验证仍可查阅 [2.0.4 测试记录](https://github.com/Hi-Walker/XiaomiStereo/blob/v2.0.4/docs/VALIDATION.md)。其中包括 7,200 秒虚拟时间软件仿真、约 30 分钟受控数字复测，以及此前中止和未通过的实机检查。后续使用中仍报告过单侧杂音或断续，恢复后原因未确定。2.0.7 的改动集中在界面主题与布局。公开 EXE 尚未进行新增硬件播放验收，声学同步、端到端延迟、真实 USB 拔插和完整睡眠唤醒也未在本轮验证。

---

## English

Version 2.0.7 moves theme selection into the control panel's moon menu and retains the layout fix for **Reset to center**. The selected theme is saved in `ui.json`. Windows high contrast takes priority. Switching themes does not reopen audio devices.

The final ThemeTests run on the 2.0.7 tested build passed 423 main assertions, 164 resource assertions, and 17 native title-bar property readbacks, with zero skips. Window activation passed 60 cycles, settings passed 11 scenarios, and the lifecycle regression passed. The first resource check exceeded its handle-growth threshold. The final rerun passed with the original threshold. The limited open-and-close cycles describe resource use during those tests.

The public build checks are listed below. The [2.0.4 validation record](https://github.com/Hi-Walker/XiaomiStereo/blob/v2.0.4/docs/VALIDATION.md) documents the 7,200-second virtual-time simulation, the controlled digital retest of about 30 minutes, and earlier hardware runs that were stopped or did not pass. Later use included a report of noise or dropouts from one speaker. Playback recovered, but the cause remains undetermined. The 2.0.7 changes address the theme and layout. This release has not undergone new hardware playback acceptance, acoustic synchronization, end-to-end latency, physical USB reconnect, or full sleep and resume testing.

## 发布构建软件测试 / Public build software tests

公开配置独立构建成功。Activation、Audio、Lifecycle、Routing、Settings、ShutdownPower、StateDirectory、Theme、Volume 九类软件测试均在独立 Windows 测试桌面通过。Theme 主测通过 417 项断言，资源子测通过 164 项，17 次标题栏属性回读全部通过。Activation 通过 60 轮，Audio 通过 28,868 项断言，Routing、Settings、ShutdownPower 分别通过 30、11、78 个场景。音频软件测试在内存中运行，实机播放验收需另行进行。

The public configuration built successfully. All nine software suites passed on a separate Windows test desktop: Activation, Audio, Lifecycle, Routing, Settings, ShutdownPower, StateDirectory, Theme, and Volume. Theme passed 417 main assertions, 164 resource assertions, and 17 title-bar property readbacks. Activation passed 60 cycles and Audio passed 28,868 assertions. Routing, Settings, and ShutdownPower passed 30, 11, and 78 scenarios, respectively. The audio software tests ran in memory. Hardware playback acceptance requires a separate test.
