# 测试与兼容性 / Testing and compatibility

## 中文

版本 2.0.2 已通过 Activation、Audio、Lifecycle、Routing、StateDirectory、Volume 默认软件测试，其中音频测试包含 28,868 次断言，路由与配置测试覆盖 30 个场景。测试命令为 `./build.ps1 -Tests`。

软件测试主要使用内存音频、模拟路由和隔离进程，不能替代实际硬件兼容性验证。尚未完成长时间实机连续播放、全部睡眠唤醒场景、多屏 DPI 或端到端声学同步测量。不能保证所有音箱型号兼容或两箱实际发声时差完全一致。

EXE SHA-256：`EDBC1D7D2939D5E8395479F4D9FBB612BE0B0257F2BE855653351C4E6A60E346`。压缩包校验值见发行版中的 `SHA256SUMS.txt`。

## English

Version 2.0.2 passed the default Activation, Audio, Lifecycle, Routing, StateDirectory and Volume software tests, including 28,868 audio assertions and 30 routing/configuration scenarios. Run them with `./build.ps1 -Tests`.

The tests primarily use in-memory audio, simulated routing and isolated processes. They do not replace hardware compatibility validation. Long physical playback runs, all suspend/resume conditions, multi-monitor DPI and end-to-end acoustic synchronization measurements remain unverified. Compatibility with every speaker model or identical acoustic timing is not guaranteed.

EXE SHA-256: `EDBC1D7D2939D5E8395479F4D9FBB612BE0B0257F2BE855653351C4E6A60E346`. Archive hashes are provided in the release's `SHA256SUMS.txt`.