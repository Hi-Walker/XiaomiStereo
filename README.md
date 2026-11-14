# 小米立体声 / Xiaomi Stereo 2.0.2

通过 Windows 电脑，将两只分别以 USB 连接的小米音箱作为左右声道使用。提供音量联动、左右交换、声道平衡和播放设备恢复功能。当前软件界面为中文。

> **使用前必须关闭或解散小米 App 自带的“立体声组合”和“全屋播放”。使用本软件时，不要启用这两个功能。两只音箱必须分别通过 USB 连接电脑。**
>
> **Before using this software, disable or dissolve the Xiaomi app's native Stereo pairing and Whole-home playback groups. Do not enable either feature while using this software. Connect each speaker separately to the PC by USB.**

**AI 制作说明：本软件由 AI 完成设计、开发与文档编写。** 本项目使用的是 Xiaomi 智能音箱 Pro。

## 下载

- [下载 Windows x64 运行包](https://github.com/Hi-Walker/XiaomiStereo/releases/download/v2.0.2/XiaomiStereo-2.0.2-win-x64.zip)
- [查看发行版、源码包和校验值](https://github.com/Hi-Walker/XiaomiStereo/releases/tag/v2.0.2)

普通用户下载 `XiaomiStereo-2.0.2-win-x64.zip`，解压整个文件夹后运行 `小米立体声.exe`。`source.zip` 是源码，不是安装程序。

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

本版本已通过软件回归测试。尚未完成长时间真实连续播放、全部睡眠唤醒场景或声学同步测量。详情见 [验证说明](docs/VALIDATION.md)。

本软件不等同于小米官方无线组网功能，也不保证所有设备兼容或两箱声学延迟完全一致。

## 从源码构建

在 Windows x64 和 PowerShell 下，于源码目录执行：

```powershell
.\build.ps1 -Tests
```

首次构建下载固定版本的 NuGet 构建工具并核对 SHA-256；默认产物在 `release`。普通测试主要使用内存音频、模拟路由和隔离进程。`HardwareAcceptance.exe` 不会默认运行，只适合具备相应硬件且了解测试行为的开发者。

依赖 NAudio 的许可位于 [licenses/NAudio.txt](licenses/NAudio.txt)。此项目尚未添加覆盖整个项目的开源许可证；公开源码不代表授予无限制再分发许可。小米相关名称与标识属于相应权利人。

---

## English

Xiaomi Stereo sends the left and right channels from a Windows virtual playback device to two independently connected USB speakers. It includes volume linking, channel swapping, balance adjustment and default-output recovery. **The application UI is currently in Chinese.**

**AI development disclosure:** This software was designed, developed and documented using AI. This project uses the Xiaomi Smart Speaker Pro.

### Download and requirements

Download [XiaomiStereo-2.0.2-win-x64.zip](https://github.com/Hi-Walker/XiaomiStereo/releases/download/v2.0.2/XiaomiStereo-2.0.2-win-x64.zip), extract all files and run `小米立体声.exe`. The source archive is for development. Checksums and source are available on the [release page](https://github.com/Hi-Walker/XiaomiStereo/releases/tag/v2.0.2).

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

### Everyday operation

Windows volume and speaker volume changes reported over USB are linked. Hardware volume steps may differ from Windows percentages; the program does not directly read the percentage displayed in the Xiaomi app. Running the EXE again or double-clicking its tray icon opens the existing control panel.

Pause in the panel before switching to normal speakers. The program tries to restore the previous output, using the configured fallback if necessary. Manually changing the Windows output also pauses automatic takeover. Optional login startup uses `--startup`; the package does not create startup entries automatically. Exit normally through the tray menu before deleting the extracted folder.

### Settings and recovery

Settings are stored in the current Windows user's `%LOCALAPPDATA%\XiaomiStereo` directory.

**When upgrading, preserve the existing configuration file and state directory to retain device settings.**

`settings.json` stores preferences and `recovery.json` records pending output restoration. Do not delete them while the application is running or restoration is incomplete. If writing state fails, exit normally and set `StateDirectory` in the adjacent EXE configuration file to your own writable, non-EFS-encrypted absolute directory. Use the same state directory for all launch methods.

### Validation, development and notices

This release passed the software regression tests. Long physical playback runs, all suspend/resume conditions and acoustic synchronization measurements remain unverified. See [VALIDATION.md](docs/VALIDATION.md) for details.

Build on Windows x64 with PowerShell using `./build.ps1 -Tests`. The initial build downloads pinned NuGet build tools and verifies SHA-256; output goes to `release`. The default tests use in-memory audio, simulated routing and isolated processes. Hardware acceptance tests are not run automatically.

NAudio's license is in [licenses/NAudio.txt](licenses/NAudio.txt). No project-wide open-source license has been added; publishing the source is not an unrestricted redistribution grant. Xiaomi names and marks belong to their respective owners. This tool is not Xiaomi's native wireless grouping system and does not guarantee universal compatibility or identical acoustic latency.
