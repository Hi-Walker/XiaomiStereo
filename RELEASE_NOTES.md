# 小米立体声 / Xiaomi Stereo v2.0.3

> **使用前必须关闭或解散小米 App 自带的“立体声组合”和“全屋播放”，使用期间不要启用。两只音箱分别通过 USB 连接电脑。**

**AI 制作说明：本软件由 AI 完成设计、开发与文档编写。** 本项目使用的是 Xiaomi 智能音箱 Pro。

## 本版更新

- 修复设备设置、声道平衡与左右交换操作互相覆盖的问题。
- 修复界面的旧状态刷新撤销刚提交的操作，以及关闭重开面板时丢失待确认操作的问题。
- 修复设置保存失败、暂停恢复失败及重复保存等边界情况。
- 音频处理、音量曲线和双向音量联动保持不变。

## 功能

- 将电脑音频的左、右声道分别输出到两只独立 USB 音箱。
- 联动 Windows 音量与音箱通过 USB 回报的音量变化。
- 支持左右交换、声道平衡与手动重新同步。
- 手动启动或再次运行 EXE 时打开或恢复控制面板；`--startup` 可静默进入托盘。
- 暂停立体声时尝试恢复普通播放输出，必要时使用所选备用扬声器。

## 下载与使用

下载 **`XiaomiStereo-2.0.3-win-x64.zip`**，解压全部文件，运行 `小米立体声.exe`。首次使用请在“设备设置”中选择虚拟音频入口、左音箱、右音箱及备用普通扬声器。源码为 `XiaomiStereo-2.0.3-source.zip`，文件校验值见 `SHA256SUMS.txt`。

升级前请暂停立体声并从托盘正常退出。替换程序文件时，**保留现有配置文件和状态目录**，避免丢失设备设置；不要在程序运行中或恢复尚未完成时删除状态文件。

需要 **Windows 10 1703 或更新版本的 x64 系统、.NET Framework 4.8+、两只独立 USB 音箱，以及已有的可用虚拟播放设备**。**运行包不包含虚拟音频驱动**，当前软件界面为中文。详细步骤见仓库 README。

## 验证范围

本版本通过 Activation、Audio、Lifecycle、Routing、Settings、StateDirectory、Volume 七类软件测试，包括新增的 11 组设置回归和 60 轮窗口恢复与释放检查。音频测试包含 28,868 次断言，路由与配置测试覆盖 30 个场景。

本轮未新增实机听音或声学验收。长时间实机连续播放、完整睡眠唤醒及声学同步/延迟测量尚未完成；不能保证所有音箱型号兼容或两箱实际发声时差完全一致。有限窗口循环检查不代表长期零泄漏。

---

## English

> **Before use, disable or dissolve the Xiaomi app's native Stereo pairing and Whole-home playback. Keep both disabled while using this software. Connect each speaker separately to the PC by USB.**

**AI development disclosure:** This software was designed, developed and documented using AI. This project uses the Xiaomi Smart Speaker Pro.

### Changes in this release

- Fixes device settings, balance changes and channel swaps overwriting one another.
- Prevents stale UI refreshes from undoing pending edits and preserves pending edits when the panel is closed and reopened.
- Fixes edge cases involving failed saves, failed restoration while pausing and repeated saves.
- Audio processing, the volume curve and bidirectional volume linking are unchanged.

### Features

- Sends the left and right channels of PC audio to two independent USB speakers.
- Links Windows volume with speaker volume changes reported over USB.
- Provides channel swapping, balance adjustment and manual resynchronization.
- Opens or restores the control panel on manual/repeated launches; `--startup` starts silently in the tray.
- Tries to restore normal playback when stereo is paused, using the selected fallback speaker when necessary.

### Download and setup

Download **`XiaomiStereo-2.0.3-win-x64.zip`**, extract all files and run `小米立体声.exe`. On first use, open Device settings and select the virtual audio source, left speaker, right speaker and fallback normal output. Source is available as `XiaomiStereo-2.0.3-source.zip`; file hashes are in `SHA256SUMS.txt`.

Before upgrading, pause stereo and exit normally through the tray menu. When replacing program files, **preserve the existing configuration file and state directory** to retain device settings. Do not delete state files while the app is running or restoration is incomplete.

Requires **Windows 10 version 1703 or newer, x64, .NET Framework 4.8+, two independent USB speakers and an existing compatible virtual playback endpoint**. **No virtual audio driver is included.** The current application UI is Chinese. See the repository README for detailed setup steps.

### Validation scope

This release passed seven software test suites: Activation, Audio, Lifecycle, Routing, Settings, StateDirectory and Volume. Coverage includes 11 new settings regression scenarios, 60 window recovery/disposal cycles, 28,868 audio assertions and 30 routing/configuration scenarios.

No new hardware listening or acoustic acceptance tests were performed for this release. Long physical playback runs, full suspend/resume coverage and acoustic synchronization/latency measurements remain unverified. Compatibility with every speaker model or identical acoustic timing is not guaranteed. The finite window-cycle checks do not establish leak-free long-term operation.
