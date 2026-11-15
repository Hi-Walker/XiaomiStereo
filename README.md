# 小米立体声 / Xiaomi Stereo 2.0.4

> **本次说明补充未改动 2.0.4 程序；测试构建新增的 30 分钟受控复测通过既定数字音频检查，未复现此前异常。前两轮失败与未归因范围仍保留。公开 EXE 本身尚未上硬件；下载文件以 GitHub Release 实际附件为准。**
> **This validation addendum does not change the 2.0.4 application. A new controlled 30-minute run of the tested build passed its existing digital audio checks without reproducing the earlier anomalies. Both earlier failed runs and attribution limits remain documented. The public EXE itself has not been installed on hardware; available downloads are determined by the actual GitHub Release attachments.**

通过 Windows 电脑，将两只分别以 USB 连接的小米音箱作为左右声道使用。提供音量联动、左右交换、声道平衡和播放设备恢复功能。当前软件界面为中文。

> **使用前必须关闭或解散小米 App 自带的“立体声组合”和“全屋播放”。使用本软件时，不要启用这两个功能。两只音箱必须分别通过 USB 连接电脑。**
>
> **Before using this software, disable or dissolve the Xiaomi app's native Stereo pairing and Whole-home playback groups. Do not enable either feature while using this software. Connect each speaker separately to the PC by USB.**

**AI 制作说明：本软件由 AI 完成设计、开发与文档编写。** 本项目使用的是 Xiaomi 智能音箱 Pro。

## 下载

以下为版本下载地址，具体可用文件以 GitHub Release 实际附件为准。

- [下载 Windows x64 运行包](https://github.com/Hi-Walker/XiaomiStereo/releases/download/v2.0.4/XiaomiStereo-2.0.4-win-x64.zip)
- [查看发行版、源码包和校验值](https://github.com/Hi-Walker/XiaomiStereo/releases/tag/v2.0.4)

普通用户下载 `XiaomiStereo-2.0.4-win-x64.zip`，解压整个文件夹后运行 `小米立体声.exe`。`source.zip` 是源码，不是安装程序。

## 使用条件

- Windows 10 1703 或更新版本的 **x64** 系统，以及 **.NET Framework 4.8 或更高版本**；实机短测在 Windows 11 上完成。
- 两只可在 Windows 中作为独立 USB 播放设备使用的音箱。本项目在两只小米智能音箱 Pro 上验证；未验证所有小米型号、固件或其他品牌。
- 已有的可用虚拟音频播放设备，用来接收电脑声音。本项目实测使用 `SteamStreamingSpeakers` 虚拟播放端点。**运行包不包含、不安装虚拟音频驱动**；只有两只 USB 音箱、没有虚拟播放设备时，不能直接完成本方案。
- 小米 App 的“立体声组合”和“全屋播放”必须关闭或解散。

## 第一次使用

1. 在小米 App 中关闭或解散“立体声组合”和“全屋播放”，让两只音箱各自独立工作。
2. 分别用 USB 将两只音箱接入电脑，确认 Windows 能识别两条独立播放输出；准备好可用虚拟播放端点。
3. 在 Windows 中先选择平时使用的普通扬声器。解压运行包，保留 EXE、两个 DLL 和配置文件在同一文件夹，然后双击 `小米立体声.exe`。
4. 打开面板中的“设备设置”，手动选择电脑音频入口（虚拟播放设备）、左音箱、右音箱和备用普通扬声器。前三项必须是三个不同的设备。首次出现等待设备提示时，在此选择自己的设备，不必修改音箱名称。
5. 保存设置并启用立体声。开启自动切换时，程序在链路就绪后管理 Windows 的普通/多媒体默认输出；手动模式下，请在 Windows 或播放应用中选择所配置的虚拟播放设备。
6. 先以较低音量播放测试内容，确认左右方向。可在面板中交换左右、调整平衡和重新同步。

## 2.0.4 更新

- 正常退出前保存已经接受的设置及最后的启用/暂停意图；退出过程中不重新启动音频链路。
- 保存、恢复普通输出失败或退出等待超时时给出提示，避免把未完成的清理显示为成功。
- 先记录系统挂起/恢复状态，再执行可能失败的保存和设备操作，避免恢复通知因同轮异常被丢失。
- 音频处理、缓冲控制、音量曲线和双向音量联动保持不变。

## 日常使用

- Windows 音量与两只音箱通过 USB 回报的音量变化会联动。硬件可能存在音量跳档；软件不能直接读取小米 App 显示的实际百分比。
- 再次双击 EXE 会唤醒已有控制面板；双击托盘图标也可打开面板。
- 临时使用其他扬声器时，先在面板暂停。程序尝试恢复原普通输出，必要时使用备用设备；主动更换 Windows 输出也会使自动接管暂停。
- 如需登录后静默启动，可自行创建携带 `--startup` 参数的快捷方式；运行包不会自动添加启动项。
- 退出请使用托盘菜单。卸载前先暂停并正常退出，再删除解压目录。

## 设置、恢复与隐私

设置保存在当前 Windows 用户的 `%LOCALAPPDATA%\XiaomiStereo` 目录中。

**升级时请保留现有配置文件和状态目录，避免丢失设备设置。**

`settings.json` 保存设备和偏好；`recovery.json` 用于默认播放设备的异常恢复。**程序运行中或恢复尚未完成时，不要删除这些文件。** 如状态目录写入失败，可先正常退出，再把 EXE 旁配置文件中的 `StateDirectory` 改成自己可写、非 EFS 加密的绝对路径。不同启动方式须使用同一状态目录。

## 已测试范围与限制

最新补充：2026-11-15 的 30 分钟受控复测在无交互操作、Edge 与 Wallpaper Engine 已关闭并隔离其他音频会话的条件下通过（退出码 0）。测试器耗时 1,802.477 秒，实际检查播放 1,797.107 秒，含 1 次计划暂停/恢复；源端左右各 179,713 个、输出左右各 179,714 个 10 ms 窗口，不合格数均为 0，并复核了源端 RMS 与峰值。32 次正式进程采样中，私有内存 38.629～38.891 MiB，句柄 454～475、线程 11～14；整机口径平均 CPU 0.08089%，最大区间平均 0.20813%（非瞬时峰值）。19 项环境核对和 4 项会话恢复全部通过。程序、版本和音频实现未改；这是测试构建的数字回环结果，不能替代公开 EXE 或声学验收，也不能把下面两轮旧失败改写为通过。

公开候选独立通过七个软件测试、Activation 的 --headless 子集及虚拟时间 7,200 秒的音频仿真（进程墙钟 50.732 秒，非实机两小时）。测试构建另通过约 63 秒双输出数字短测、真实音量切换与恢复及 60 轮原生窗口恢复。首轮实机长测约 19.8 分钟中止，保留未归因异常，不计为通过；第二轮 90 分钟主测量阶段结束，含 5 次计划暂停、实际播放 89 分 44.505 秒，严格纯音检查未通过（左右 6,685/7,420 个不合格窗口）。额外输入只在一段较晚观察中明确关联到 Edge，不能解释全部异常。92 次采样的私有内存为 44.676～44.879 MiB，句柄 459～481、线程 11～14；13 项结束核对及 5 个会话恢复检查全部通过。公开 EXE 本身尚未上硬件；真实睡眠唤醒和声学同步仍未验收。详情见 [验证说明](docs/VALIDATION.md)。

本软件不等同于小米官方无线组网功能，也不保证所有设备兼容或两箱声学延迟完全一致。

## 从源码构建

在 Windows x64 和 PowerShell 下，于源码目录执行：

```powershell
.\build.ps1
```

如需复现本轮不显示窗口的软件测试，先用 `-CompileTestsOnly` 编译，再按 [验证说明](docs/VALIDATION.md) 运行。完整原生窗口测试需要可创建独立桌面的交互式 Windows 会话；不要在独立桌面创建失败时自动退回当前桌面。

首次构建下载固定版本的 NuGet 构建工具并核对 SHA-256；默认产物在 `release`。普通测试主要使用内存音频、模拟路由和隔离进程。`HardwareAcceptance.exe` 不会默认运行，只适合具备相应硬件且了解测试行为的开发者。

依赖 NAudio 的许可位于 [licenses/NAudio.txt](licenses/NAudio.txt)。此项目尚未添加覆盖整个项目的开源许可证；公开源码不代表授予无限制再分发许可。小米相关名称与标识属于相应权利人。

---

## English

Xiaomi Stereo sends the left and right channels from a Windows virtual playback device to two independently connected USB speakers. It includes volume linking, channel swapping, balance adjustment and default-output recovery. **The application UI is currently in Chinese.**

**AI development disclosure:** This software was designed, developed and documented using AI. This project uses the Xiaomi Smart Speaker Pro.

### Download and requirements

The links below identify this version; use the actual GitHub Release attachments to determine available downloads.

Download [XiaomiStereo-2.0.4-win-x64.zip](https://github.com/Hi-Walker/XiaomiStereo/releases/download/v2.0.4/XiaomiStereo-2.0.4-win-x64.zip), extract all files and run `小米立体声.exe`. The source archive is for development. Checksums and source are available on the [release page](https://github.com/Hi-Walker/XiaomiStereo/releases/tag/v2.0.4).

- Windows 10 version 1703 or newer, x64, with .NET Framework 4.8 or later. Hardware checks were performed on Windows 11.
- Two speakers that Windows can access as separate USB playback devices. Testing used two Xiaomi Smart Speaker Pro units; compatibility with every model or firmware is not established.
- An existing working virtual playback endpoint to receive PC audio. The tested setup used `SteamStreamingSpeakers`. **No virtual audio driver is included or installed by this package.** Two USB speakers alone are not sufficient for this setup.
- **Disable/dissolve both native Stereo pairing and Whole-home playback in the Xiaomi app. Keep both features disabled while using this program.**

### First use

1. Disable the two Xiaomi app grouping features above. Connect each speaker separately to the PC by USB.
2. Confirm both speakers and a suitable virtual playback device are available in Windows. Select your normal physical speaker as the Windows output before starting.
3. Extract the entire runtime package. Keep the EXE, DLLs and configuration file together, then run `小米立体声.exe`.
4. Open **设备设置 (Device settings)** and manually select the virtual audio source, left speaker, right speaker and fallback normal output. The source and two speaker outputs must be distinct. If it initially waits for devices, select your own devices here; do not rename your speakers.
5. Save and enable stereo. Automatic mode takes over the normal/multimedia default outputs after the audio path is ready. In manual mode, select the configured virtual device in Windows or in the playback application.
6. Start at a low volume and verify the channels. Use the panel to swap channels, adjust balance or resynchronize.

### Changes in 2.0.4

- Saves accepted settings and the latest enable/pause intent before normal exit, without restarting audio during shutdown.
- Reports save/restoration failures and shutdown timeouts instead of treating incomplete cleanup as success.
- Records suspend/resume state before storage and device operations that may fail, preventing a consumed resume notification from leaving the coordinator asleep.
- Audio processing, buffer control, the volume curve and bidirectional volume linking are unchanged.

### Everyday operation

Windows volume and speaker volume changes reported over USB are linked. Hardware volume steps may differ from Windows percentages; the program does not directly read the percentage displayed in the Xiaomi app. Running the EXE again or double-clicking its tray icon opens the existing control panel.

Pause in the panel before switching to normal speakers. The program tries to restore the previous output, using the configured fallback if necessary. Manually changing the Windows output also pauses automatic takeover. Optional login startup uses `--startup`; the package does not create startup entries automatically. Exit normally through the tray menu before deleting the extracted folder.

### Settings and recovery

Settings are stored in the current Windows user's `%LOCALAPPDATA%\XiaomiStereo` directory.

**When upgrading, preserve the existing configuration file and state directory to retain device settings.**

`settings.json` stores preferences and `recovery.json` records pending output restoration. Do not delete them while the application is running or restoration is incomplete. If writing state fails, exit normally and set `StateDirectory` in the adjacent EXE configuration file to your own writable, non-EFS-encrypted absolute directory. Use the same state directory for all launch methods.

### Validation, development and notices

Latest supplemental result: on 2026-11-15, a controlled 30-minute run passed with exit code 0 while there was no interactive input, Edge and Wallpaper Engine were already closed, and other audio sessions were isolated. Harness elapsed time was 1,802.477 seconds, with 1,797.107 seconds of checked playback and one planned pause/resume. Each source channel had 179,713 ten-millisecond windows and each output had 179,714, all with zero bad windows; source RMS and peak were also checked. Across 32 production-process samples, private memory was 38.629–38.891 MiB, handles 454–475 and threads 11–14; whole-machine mean CPU was 0.08089%, and the largest interval average was 0.20813% (not an instantaneous peak). All 19 environment checks and 4 session restorations passed. The application, version and audio implementation are unchanged. This digital loopback result from the tested build is not public-binary or acoustic acceptance and does not convert either earlier failed run below into a pass.

The public candidate independently passed seven software suites, Activation's --headless subset and a 7,200-second virtual-time audio simulation (50.732 seconds of process wall time, not two hours on hardware). The tested build separately passed an approximately 63-second two-output digital check, real volume transitions/restoration and 60 native-window recovery cycles. The first hardware run was stopped after approximately 19.8 minutes with unclassified anomalies retained and is not counted as passed. The second 90-minute measurement phase completed with 5 planned pauses and 89 minutes 44.505 seconds of measured playback; the strict pure-tone check did not pass (6,685/7,420 bad left/right windows). Extra input was directly associated with Edge only during a later observation segment, not all anomalies. Across 92 samples, private memory was 44.676–44.879 MiB, handles 459–481 and threads 11–14; all 13 post-run checks and 5 session restorations passed. The public EXE itself has not been installed on hardware. Real suspend/resume and acoustic synchronization remain unverified. See [VALIDATION.md](docs/VALIDATION.md).

Build on Windows x64 with PowerShell using `./build.ps1`. To reproduce this run without showing windows, compile with `-CompileTestsOnly` and follow [VALIDATION.md](docs/VALIDATION.md). Full native-window tests require an interactive Windows session that permits a separate desktop; do not silently fall back to the visible desktop if desktop creation fails. The initial build downloads pinned NuGet build tools and verifies SHA-256; output goes to `release`. The default tests use in-memory audio, simulated routing and isolated processes. Hardware acceptance tests are not run automatically.

NAudio's license is in [licenses/NAudio.txt](licenses/NAudio.txt). No project-wide open-source license has been added; publishing the source is not an unrestricted redistribution grant. Xiaomi names and marks belong to their respective owners. This tool is not Xiaomi's native wireless grouping system and does not guarantee universal compatibility or identical acoustic latency.
