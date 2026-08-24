# OBS 用户常见问题 × 现有功能 对照与开发指引（2026-08）

> 方法：网络检索 OBS Studio 高频故障（中文社区/CSDN/obsproject 论坛/KB/GitHub Issues，2023–2026 资料为主），
> 与本仓库已实现功能及 `problems.json`（149 条知识库）逐一对照去重。
> 结论分三档：**A 已覆盖（不开发）** / **B 已覆盖但可增强（低优先级）** / **C 缺口（本次开发清单）**。

---

## 1. 已覆盖问题域（去重后不再开发）

| 问题域 | 检索到的典型症状 | 现有覆盖 |
|---|---|---|
| 网络掉帧 | Dropped Frames、WiFi 不稳、节点拥堵 | 知识库 lag 类 15 条；`BandwidthAdvisorCore`；`IngestPingService` 节点测速；`ObsLogAnalyzer` 三分类主因判定 |
| 编码过载 | Encoding overloaded、NVENC 失败 | 知识库 encoding 类 9 条；`EncoderAdvisorCore`；日志分析器过载处理顺序 |
| 采集黑屏 | 显示器/窗口/游戏捕获黑屏、双显卡、反作弊、HDR 发灰 | 知识库 black-screen 类 13 条；`ColorCheckCore`；日志分析器双显卡错位检测 |
| 音频无声/回声/底噪 | 麦克风无声、桌面音频丢失、啸叫、降噪 | 知识库 audio 类 17 条；`SampleRateCheckCore`（48kHz 共享模式） |
| 音画不同步 | Sync Offset、采样率漂移 | 知识库 avsync 类 6 条 |
| 推流失败 | 连接超时、密钥错误、VPN 干扰 | 知识库 streamfail 类 12 条；冲突软件扫描 `ConflictScannerCore` |
| 录制文件损坏/转封装 | MKV 崩溃恢复、remux MP4 | 知识库 recording 类 16 条；`PreflightCheckCore` 强制 MKV；`RecordingToolsService` ffmpeg 无损转封装 |
| 崩溃/启动失败 | VC++ 运行库、Nahimic/RTSS/Overwolf、安全软件 | 知识库 crash 类 14 条；冲突扫描 |
| 配置损坏 | 场景集合丢失、升级翻车 | `ObsBackupService` zip 备份/事务恢复；`ObsResetService` |
| 磁盘空间不足 | 录制中途静默停止 | 预检磁盘空间 + `SystemMonitorService` 秒级磁盘预警托盘通知 |

---

## 2. 缺口开发清单（C 档）

按优先级排序。所有实现遵循项目既定约束：
零第三方 NuGet 包（纯 BCL）、对用户系统**只读探测优先**、写入类操作必须显式确认、
纯逻辑放 `*Core.cs` 便于 xUnit 单测（现有 241 项测试模式）。

### GAP-1 录制守护（Recording Watchdog）★ 最高价值
- **问题证据**：OBS 故障的本质是"静默失败"——全屏游戏中录制中断/崩溃/卡在 Stopping Recording，
  用户毫不知情直到录完发现空文件（obsproject 论坛高频帖；GitHub #8362；商业工具 Mynofi 专门做这件事且收费 $4.99）。
- **现状差距**：现有 `ControlTimerService` 只做"定时主动停止"；`RecordStateChanged` 事件已订阅但未用于异常检测。
- **方案指引**：
  - 新建 `Services\Shell\RecordWatchdogService.cs`（参照 `ControlTimerService` 的生命周期挂接方式）；
  - 监控三层信号：① WebSocket 断连（`ObsReconnectPolicy` 重连期间若处于录制态则告警）；
    ② `RecordStateChanged` 收到非预期 `stopped/paused`；③ 心跳超时（录制中 N 秒无任何事件且 GetRecordStatus 轮询失败/返回异常）；
  - 触发动作：Toast/托盘强提醒（复用 `TrayService`，需确保全屏游戏时可弹出——用 WinForms `NotifyIcon.ShowBalloonTip` 或 Topmost 边框窗）；
    可选自动恢复策略：重连成功且录制已断 → 提示一键 `StartRecord`；
  - 配置项进 `Models\Shell\ShellSettings.cs` 同款持久化。
- **涉及**：`ObsConnectionService`（暴露连接状态事件）、`AppServices.cs` 装配。
- **验收**：模拟杀掉 obs64.exe 进程 / 手动停录 / 断网三种场景均能在 ≤5s 内收到提醒；单测覆盖状态机判定逻辑（Core 化）。

### GAP-2 黑屏专项体检（系统图形环境探测）
- **问题证据**：黑屏是检索中提及率第一的问题；社区标准排查链 = 管理员权限 → GPU 偏好 → HAGS → HDR → Game DVR → 驱动版本。
  其中多数是**注册表/系统状态可程序化检测**的，目前只有知识库文章，没有自动化体检。
- **现状差距**：`PreflightCheckCore` 只读 OBS 自身配置；`ColorCheckCore` 只读 Profile ini；系统侧图形环境无检测。
- **方案指引**：新建 `Services\System\GraphicsEnvCheckCore.cs`，逐项只读探测：
  1. 当前进程/OBS 是否以管理员运行（WindowsIdentity + obs64.exe 进程令牌）；
  2. HAGS 开关：`HKLM\SYSTEM\...\GraphicsDrivers\HwSchMode`；
  3. Windows 图形首选项中 obs64.exe 的 GPU 绑定（`GraphicsDrivers\PerAdapterOptimization`/用户 GpuPreference 注册表）；
  4. HDR 是否开启（DisplayInformation/注册表 `AdvancedScaleCode` 或 WMI）→ 结合 `ColorCheckCore` 的发灰告警联动；
  5. Game Mode / Game DVR 后台录制（`HKCU\...\GameDVR_*`、`AllowAutoGameMode`）；
  6. 显卡驱动版本 + 日期（WMI `Win32_VideoController` DriverVersion/DriverDate）→ 与 `EncoderAdvisorCore` 的显卡识别打通给出"驱动过旧"提示；
  7. 笔记本双显卡活动 GPU（复用日志分析器的错位检测逻辑前移为事前检查）。
  - 输出统一 `CheckResult` 结构（沿用 `PreflightCheckCore` 的三档结论风格）；每项附"修复指引"文案，写入类操作仅提供一键打开对应系统设置页（`ms-settings:` URI），不直接改注册表。
- **UI 落点**：预检页新增分组，或独立"黑屏体检"入口。
- **验收**：每项检测在干净 Win10/Win11 上有确定性输出；Core 层全量单测（注入假注册表数据源）。

### GAP-3 音频设备深度体检
- **问题证据**："OBS 没声音/麦克风无声"类检索量长期居前；根因常在系统侧：隐私权限未授权、
  设备独占模式抢占、通信 Ducking 把音乐压小、默认设备漂移——这些知识库有文字但无自动检测。
- **现状差距**：`SampleRateCheckCore` 已建立 MMDevices 注册表只读枚举模式，可直接扩展。
- **方案指引**：扩展 `SampleRateCheckCore` 或新建 `AudioDeviceHealthCore.cs`：
  1. 麦克风隐私权限（`HKCU\...\Privacy\Value` 对 Microphone 全局开关）；
  2. 默认捕获/渲染设备与 OBS 所选输入是否一致（对比 websocket `GetInputList` 中 audio 输入的设备名）；
  3. 设备独占模式标志位（MMDevices 注册表 Level=3/4 独占属性）；
  4. 通信 Ducking 行为（`HKCU\...\Communications`，值应为 0/不执行任何操作才不影响直播 BGM）；
  5. 音频服务状态（Audiosrv/AudioEndpointBuilder 运行中）。
- **验收**：同上 Core 单测模式；与预检页整合。

### GAP-4 实时日志尾随预警（事中监控）
- **问题证据**：掉帧/过载/断流的社区排查全部依赖事后看日志；直播进行中主播看不到 OBS 窗口。
- **现状差距**：`ObsLogAnalyzer` 是会话后整文件解析；`SystemMonitorService` 只有 CPU/内存/磁盘，不含 OBS 内部指标。
- **方案指引**：
  - obs-websocket v5 **不提供** stats 端点，故采用日志尾随方案：定位当前会话日志文件（%AppData%\obs-studio\logs 最新一个），
    用 `FileStream` + `FileShare.ReadWrite` 尾随增量（参照 `LocalPluginScanner`/`ObsPathService` 的路径定位约定）；
  - 匹配规则复用 `ObsLogAnalyzer` 已有正则（掉帧三分类、编码过载、bitrate drop、capture card、audio resample），抽成共享 `LogRuleSet` 避免两处维护；
  - 命中即节流式托盘提醒（同类告警 ≥90s 抑制重复），直播结束可一键把本次实时命中汇总交给现有分析器出报告；
  - 注意文件滚动（新日志文件切换）与大行缓冲边界处理。
- **验收**：手工向日志追加测试行触发提醒；节流逻辑单测。

### GAP-5 虚拟摄像头体检与一键启动
- **问题证据**：虚拟摄像头用于腾讯会议/GitHub 认证等场景检索量大；典型故障 = 会议软件列表里找不到 "OBS Virtual Camera"、启动无效。
- **现状差距**：virtualcam 知识库仅 1 条；websocket 已订阅 `VirtualcamStateChanged` 但 UI 无入口。
- **方案指引**：
  - 探测驱动注册状态（注册表 `HKLM\SOFTWARE\Classes\CLSID` 下 obs-virtualcam 的 DirectShow/MediaFoundation 注册项 + obs-plugins 目录下 DLL 存在性）；
  - 通过 websocket `StartVirtualCam`/`TriggerStudioModeTransition` 提供 UI 按钮（ConsolePage 增加）；
  - 输出排查树：未安装驱动 → 引导重装 OBS；已装但会议软件不见 → 杀毒拦截/需重启会议软件指引。
- **验收**：驱动未注册/已注册两种环境下的判定正确；按钮调用有连接态校验（沿用现有护栏风格）。

### GAP-6 已知问题插件标注
- **问题证据**："Stopping Recording 卡死"相当比例由第三方插件引起（如 obs-source-record Issue #99 长期 Open）；StreamFX 停更迁移事故。
- **现状差距**：`PluginScannerCore` 能扫出本机插件、`plugins.json` 有广场目录，但没有"风险插件"维度。
- **方案指引**：在 `plugins.json` schema 增加可选字段 `riskNote`（风险等级+一句话说明+建议动作），外置热更新通道天然支持免发版更新名单；
  本机体检结果页对命中项打标。兼容旧 JSON（缺字段忽略）。
- **验收**：新旧 schema 解析回归测试；热更新覆盖生效路径验证。

### GAP-7 音画同步偏移校准助手（B 档增强，可延后）
- **问题证据**：avsync 社区解法高度模板化（拍手/闪光对齐 → 手调 sync offset），但手动试错繁琐。
- **现状差距**：知识库有方法论；无工具化。
- **方案指引**：内置测试信号发生器（屏幕黑白闪块 + 同时 beep，可用 `System.Media.SoundPlayer` 与 WPF 动画实现，无需 ffmpeg），
  用户手机拍摄回放后估测偏差 ms 数 → 经 websocket `SetInputSyncOffset` 写入指定输入并持久化提示。
  若确认 `ObsToolRegistry` 未暴露该请求则补白名单工具。
- **验收**：偏移写入前后 GetInputSettings 值正确。

### GAP-8 电源计划与供电检测（小件，可并入 GAP-2）
- **问题证据**：笔记本省电模式/未插电导致编码欠载、Stuck on Stopping，多份教程列为标准检查项。
- **方案指引**：`PowerProfileProbe`：`powercfg /getactivescheme` 解析 + `SystemInformation.PowerStatus.BatteryChargeStatus` 判断是否使用电池；
  高性能计划缺失或电池供电 → 预检黄色警告。约半天工作量。

---

## 3. 建议实施顺序

1. **GAP-1 录制守护**（独立性强、价值最高、复用面广）
2. **GAP-2 黑屏体检 + GAP-8 电源检测**（同一批注册表/WMI 探测基建）
3. **GAP-3 音频深度体检**（复用 MMDevices 模式）
4. **GAP-4 实时日志尾随**（依赖 LogRuleSet 抽取重构）
5. **GAP-5 虚拟摄像头**、**GAP-6 插件风险标注**（小件）
6. **GAP-7 同步校准助手**（体验型，最后）

## 4. 风险与约束备忘

- 全程零第三方包：注册表用 `Microsoft.Win32.Registry`，WMI 用 `System.Management`（BCL 内置于 Windows TFM），日志尾随用 FileStream。
- 所有系统级探测保持只读；任何"一键修复"先以 `ms-settings:` 跳转替代直接写注册表，确需写注册表的须用户显式确认并记录回滚值。
- PRNOBS（C++ Dock 插件）侧暂不动；以上均为主程序能力，避免 Qt ABI 跟随成本。
- 新增 Core 类一律配套 xUnit 单测；涉及路由的新页面记得补 Headless 自检（`OBS_SELFTEST=1`）。
