# 小米立体声 / Xiaomi Stereo 2.0.7

将两只分别以 USB 连接的小米音箱用作 Windows 电脑的左右声道。软件支持音量联动、左右交换、声道平衡和播放设备恢复。控制面板右上角的月亮菜单可选择「跟随系统」「浅色」或「深色」，并保存选择。Windows 高对比度优先。软件界面目前为中文。

> **使用前请关闭或解散小米 App 自带的“立体声组合”和“全屋播放”，使用期间保持关闭。两只音箱分别用 USB 连接电脑。**

**AI 制作说明：本软件由 AI 完成设计、开发与文档编写。** 本项目使用的是 Xiaomi 智能音箱 Pro。

## 下载与使用条件

- [下载 2.0.7 Windows x64 运行包](https://github.com/Hi-Walker/XiaomiStereo/releases/download/v2.0.7/XiaomiStereo-2.0.7-win-x64.zip)
- [查看版本说明、源码包和校验值](https://github.com/Hi-Walker/XiaomiStereo/releases/tag/v2.0.7)
- Windows 10 1703 或更新版本的 x64 系统，需 .NET Framework 4.8 或更高版本。
- 两只在 Windows 中显示为独立 USB 播放设备的音箱，以及一个用来接收电脑声音的虚拟播放设备。以往测试使用 `SteamStreamingSpeakers`。虚拟音频驱动需另行准备。

## 首次使用

1. 在小米 App 中关闭或解散「立体声组合」和「全屋播放」。分别用 USB 连接两只音箱，并确认 Windows 能识别两条独立播放输出。
2. 在 Windows 中选择平时使用的普通扬声器。解压整个运行包，保留 EXE、DLL 和配置文件在同一文件夹，再运行 `小米立体声.exe`。
3. 在「设备设置」中手动选择虚拟播放设备、左音箱、右音箱和备用普通扬声器。虚拟播放设备和左右音箱必须是三个不同的设备。
4. 保存设置并启用立体声。在自动模式下，音频链路就绪后程序会切换普通和多媒体默认输出。在手动模式下，请在 Windows 或播放应用里选择配置好的虚拟播放设备。
5. 先以较低音量测试左右方向。可在面板中交换左右、调整平衡和重新同步。

## 日常使用与升级

- Windows 音量与音箱通过 USB 回报的音量变化会联动。音箱音量档位可能与 Windows 百分比有差异。
- 再次运行 EXE 或双击托盘图标可打开已有控制面板。需要登录后静默启动时，可自行创建带 `--startup` 参数的快捷方式。
- 切回普通扬声器前可先在面板暂停。程序尝试恢复之前的普通输出，必要时使用备用设备。主动更换 Windows 输出也会暂停自动接管。
- 升级前请先从托盘正常退出旧版。替换运行文件时保留现有配置文件和状态目录。卸载运行包时，先暂停并正常退出，再删除解压目录。若曾手动添加启动快捷方式，请一并移除。

默认状态目录是 `%LOCALAPPDATA%\XiaomiStereo`。`settings.json` 保存音频设置，`ui.json` 保存主题，`recovery.json` 用于默认播放设备的异常恢复。程序运行中或恢复尚未完成时请保留这些文件。若状态目录写入失败，先正常退出，再通过相邻 EXE 配置文件里的 `StateDirectory` 指定可写的、未用 EFS 加密的绝对目录。不同启动方式应使用同一状态目录。

本版更新和验证范围见 [版本说明](RELEASE_NOTES.md)及[测试与兼容性](docs/VALIDATION.md)。从源码构建可在 Windows x64 的 PowerShell 中运行 `./build.ps1`。脚本校验固定版本的构建工具，结果写入 `release` 目录。依赖许可见 [NAudio 许可](licenses/NAudio.txt)。目前项目未指定覆盖整个项目的开源许可证。

---

## English

Xiaomi Stereo sends the left and right channels from a Windows virtual playback device to two independent USB speakers. It provides volume linking, channel swapping, balance adjustment, and default-output recovery. The moon menu in the control panel remembers your choice of **Follow system**, **Light**, or **Dark**. Windows high contrast takes priority. The app interface is currently in Chinese.

> **Before use, disable or dissolve the Xiaomi app's native Stereo pairing and Whole-home playback groups. Keep both disabled while using this software. Connect each speaker separately to the PC by USB.**

**AI development disclosure:** This software was designed, developed, and documented using AI. This project uses the Xiaomi Smart Speaker Pro.

### Downloads and requirements

- [Download the 2.0.7 Windows x64 runtime](https://github.com/Hi-Walker/XiaomiStereo/releases/download/v2.0.7/XiaomiStereo-2.0.7-win-x64.zip)
- [View the release, source archive, and checksums](https://github.com/Hi-Walker/XiaomiStereo/releases/tag/v2.0.7)
- Windows 10 version 1703 or later on x64, with .NET Framework 4.8 or later.
- Two speakers visible in Windows as separate USB playback devices and an existing virtual playback device for PC audio. Earlier tests used `SteamStreamingSpeakers`. Obtain the virtual audio driver separately.

### First use

1. Disable both grouping features in the Xiaomi app. Connect each speaker separately by USB and check that Windows lists two playback devices.
2. Select your normal Windows output. Extract the entire runtime package, keep the EXE, DLLs, and configuration file together, and run `小米立体声.exe`.
3. In **设备设置 (Device settings)**, select the virtual playback device, left speaker, right speaker, and fallback output. The virtual device and the two speaker outputs must be distinct.
4. Save your settings and enable stereo. Automatic mode switches the normal and multimedia default outputs once the audio path is ready. In manual mode, select the configured virtual device in Windows or your playback app.
5. Test the channels at low volume. Use the panel to swap channels, adjust balance, or resynchronize.

### Everyday use and upgrades

- Windows volume and USB-reported speaker volume changes are linked. Hardware volume steps may differ from the Windows percentage.
- Run the EXE again or double-click its tray icon to open the existing panel. To start silently at login, create a shortcut with `--startup`.
- Pause in the panel before switching to ordinary speakers. The app tries to restore the previous normal output and uses the chosen fallback if needed. Changing the Windows output manually also pauses automatic takeover.
- Exit the old version from the tray before upgrading. Keep your configuration file and state directory when replacing the runtime files. To remove the app, pause stereo, exit normally, and delete the extracted folder and any startup shortcut you added.

The default state directory is `%LOCALAPPDATA%\XiaomiStereo`. `settings.json` stores audio settings, `ui.json` stores the theme, and `recovery.json` tracks default-output recovery. Keep these files while the app is running or recovery is pending. If the state directory cannot be written, exit normally and set `StateDirectory` in the adjacent EXE configuration file to a writable absolute path without EFS encryption. Use the same state directory for every launch method.

See the [release notes](RELEASE_NOTES.md) and [testing and compatibility](docs/VALIDATION.md) for details. Run `./build.ps1` on Windows x64 using PowerShell to build from source. The script verifies the pinned build tools and writes the runtime to `release`. See the [NAudio license](licenses/NAudio.txt). No project-wide open-source license has been specified.
