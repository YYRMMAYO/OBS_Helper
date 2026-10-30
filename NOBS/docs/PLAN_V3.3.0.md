# OBS帮助助手 V3.3.0 技术规划

> **版本**：V3.3.0（minor，功能增强版）
> **基线**：`master` 当前提交 `6b0b57f`（V3.1.1），`net10.0-windows` 主构建 + `net6.0-windows` Win7 兼容构建
> **文档日期**：2026-10-25
> **文档状态**：规划稿。本文只做可行性评估、技术路线与落点设计，**不修改任何源码、配置、安装脚本或发布物**。
> **已确认决策（2026-10-25）**：V3.3.0 五项工作流全部纳入；插件路线采用“精选为主 + GitHub 导入”；Live2D 采用 VTube Studio 公开 API 集成。
> **本版范围**：① Live2D 一键配置；② 虚拟直播间一键配置；③ OBS 开播引导（国内 + 国外平台）；④ 插件广场的一键下载、安装、管理与 GitHub 联动；⑤ 无障碍模式与老年模式。
>
> **配套文档（建议一起阅读）**
>
> - [`V3.3.0_LIVE2D_INTEGRATION.md`](V3.3.0_LIVE2D_INTEGRATION.md) —— Live2D/VTube Studio 接入细节、模型校验、OBS 采集链路
> - [`V3.3.0_VIRTUAL_ROOM_GUIDE.md`](V3.3.0_VIRTUAL_ROOM_GUIDE.md) —— 虚拟直播间模板、可爱画风素材索引、SVG/PNG 资产规范
> - [`V3.3.0_PLATFORM_MATRIX.md`](V3.3.0_PLATFORM_MATRIX.md) —— 国内外平台开播矩阵、官方入口、推流码获取方式
> - [`V3.3.0_PLUGIN_MARKETPLACE.md`](V3.3.0_PLUGIN_MARKETPLACE.md) —— 插件广场多路线方案、一键安装/管理、Watt Toolkit 引导
> - [`V3.3.0_ACCESSIBILITY.md`](V3.3.0_ACCESSIBILITY.md) —— 无障碍现状审计、无障碍模式/老年模式、稳定性与测试
> - [`V3.3.0_OPEN_SOURCE_AUDIT.md`](V3.3.0_OPEN_SOURCE_AUDIT.md) —— 开源复用审计、许可证与“只借鉴协议、不复制代码”的边界

---

## 0. 结论速览

### 0.1 五个需求都能做，但“能做什么”和“不能承诺什么”必须分开写

| 需求 | 可行性 | 推荐技术路线 | 关键前提 | 建议优先级 |
|---|---|---|---|---|
| **Live2D 一键配置** | **可行（已选路线）** | 检测/启动 **VTube Studio**，通过其公开 WebSocket API 导入并加载模型，再在 OBS 中自动建立 NDI / Spout2 / 虚拟摄像头 / 窗口采集源 | 用户已安装 VTube Studio（或在引导下安装）；模型归用户所有；首次需在 VTube Studio 内允许本应用访问 API | **P0** |
| **虚拟直播间一键配置** | **可行，且基础已具备** | 复用现有 `SceneTemplateService`，新增“房间模板 + 资产包 + 本地 overlay 服务 + 应用后检查清单” | OBS 已连接；素材/字体/聊天组件授权合规；平台弹幕组件需用户提供自己的 widget URL | **P0** |
| **OBS 开播引导（国内 + 国外）** | **分层可行** | 直推平台：官方页面取推流码 → `SetStreamServiceSettings` → `StartStream`；抖音等无通用推流码的平台：OBS 虚拟摄像头 + 平台直播伴侣摄像头源 | 用户已登录平台；平台允许 OBS/虚拟摄像头；第三方软件首次配置需用户操作 | **P0（直推 + 抖音引导）/ P1（UI Automation 辅助）** |
| **插件广场一键下载/管理 + GitHub 联动** | **可行，但必须分级** | 精选目录（可验证资产 + 事务安装 + 回滚）为主；GitHub 搜索 + 粘贴仓库导入为次级，两者均纳入 3.3.0；任意仓库静默安装不做 | 插件是 DLL，等于在本机执行第三方代码；必须哈希/签名/用户确认/可回滚 | **P0（精选主线 + GitHub 导入次级）** |
| **无障碍模式 + 老年模式** | **可行，且应作为前置能力** | 设计令牌 + 纯逻辑 `UiProfileCore` + 全量可访问名称/实时区域 + 键盘焦点 + 高对比/大字/宽大排版 | 需要真实 NVDA/Narrator 测试；不能只靠静态检查 | **P0（横切，先于其它新页面）** |

### 0.2 三条“必须说清楚”的边界

1. **“一键”不等于“零前提、零等待、无条件成功”。**
   本项目的“一键”定义为：**用户只做一次明确的产品动作，应用完成可自动化的部分，并在失败时给出可照做的下一步**。
   首次安装第三方软件、登录平台账号、在平台侧点击“开始直播”、在 VTube Studio 内首次授权 API，这些都不能被“隔空”完成。

2. **不能承诺“在后台无感控制第三方直播软件”。**
   抖音直播伴侣、B 站直播姬等是闭源第三方应用，没有公开的“设置摄像头”API。可以用 Windows UI Automation **辅助**选择 `OBS Virtual Camera`，但它受版本、UI 改版、权限级别、语言、DPI、反自动化策略影响，**只能作为“辅助模式”，失败必须回退到图文引导**。
   应用不会在用户不知情时点击第三方软件的按钮，也不会自动替用户点击“开始直播”。

3. **不能把“任意 GitHub 仓库”当成可信安装源。**
   OBS 插件是原生 DLL，安装即执行第三方代码。精选目录可以做哈希、签名、兼容性、回滚；任意仓库只能做“导入 + 展示 + 强警告 + 用户确认”，不能静默安装。详见 [`V3.3.0_PLUGIN_MARKETPLACE.md`](V3.3.0_PLUGIN_MARKETPLACE.md)。

### 0.3 建议的 3.3.0 范围

**P0（必须交付）**

1. 无障碍模式 + 老年模式：设计令牌、模式组合、键盘/读屏/高对比/大字/宽大排版、自动化测试与人工验收。
2. 虚拟直播间：6 套房间模板 + 6 套可爱画风资产 + 应用后检查清单 + 中英文使用文档。
3. Live2D：VTube Studio 接入（检测、启动、鉴权、模型导入/加载、OBS 采集源、场景布局、失败回退）。
4. 开播引导：直推平台（B 站、YouTube、Twitch、Kick、Facebook、Trovo、NicoNico、AfreecaTV/Soop、自定义 RTMP 等）+ 抖音虚拟摄像头路径 + 官方软件检测/下载引导。
5. 插件广场：精选插件一键安装/更新/卸载、安装清单与回滚、已安装管理；GitHub 原生搜索、粘贴仓库导入（高级确认）、外部 GitHub 搜索入口；Watt Toolkit 引导。
6. 安全与合规：官方源白名单、哈希/签名校验、密钥不落日志、zip 安全解压、OBS 关闭校验、回滚。

**P1（时间允许）**

- 更多插件的“安装后配方”、UI Automation 辅助摄像头选择、全局/ProgramData 安装的提升权限助手、插件更新提醒、更多房间模板/画风、内置字幕引导（LocalVocal 等外部插件）。
- Live2D 的 NDI/Spout2 自动优选、更多追踪软件窗口采集、模型缩略图与预览截图。

**P2（明确不做或后置）**

- 自研 Live2D 渲染器 + 自研面捕（Cubism Native/Web + 摄像头推理）：工程量和许可证风险都过高，3.3.0 不做；详见 §4.2。
- 静默安装第三方软件、静默点击平台“开始直播”、自动登录/绕过验证码、自动修改 Watt Toolkit/hosts/系统代理。
- 任意 GitHub 仓库的“无确认静默安装”。
- 平台弹幕/礼物/聊天 API 的账号级集成（需要平台开放平台权限、OAuth、审核）；3.3.0 只提供 widget URL 占位与文档。

### 0.4 已确认：五个工作流全部进 3.3.0

你已选择“**全上**”，因此本文按单版本交付来排期，不再拆 3.3.0/3.3.1。按当前代码基础估算：

| 方案 | 范围 | 粗略工期 | 风险 |
|---|---|---|---|
| **B（已选）** | 无障碍/老年 + 虚拟直播间 + Live2D + 开播引导 + 插件广场全部进 3.3.0 | **18~22 周（1 人）**；**12~14 周（2 人并行）** | 高；无障碍与插件安装安全最容易被压缩，必须按 §10 的阶段门禁推进 |
| A（未选） | 3.3.0：无障碍/老年 + 插件 + 房间；3.3.1：Live2D + 开播 | 10~12 周 + 8~10 周 | 较低，但不符合“全上”的决策 |
| C（未选） | 3.3.0：Live2D + 开播 + 房间；插件延后 | 12~14 周 | 中；插件管理是明确诉求，不建议延后 |

**全上版本的三条硬约束**：

1. **先做横切能力**：无障碍/老年模式、设计令牌、错误码、目录 schema 必须先落地，否则后面四个功能会重复返工。
2. **插件安装安全不能压缩**：精选插件必须有哈希/签名/回滚；GitHub 导入必须是“高级模式 + 用户确认 + 可回滚”，不能为了赶工期改成静默安装。
3. **Live2D/开播的平台边界不能压缩**：第三方软件/平台 UI 的“无感自动化”不承诺；失败必须有图文回退。

> 如果后期发现 18~22 周不可接受，建议优先把“GitHub 原生搜索”降为后续增量，而不是削减安装安全或无障碍测试。

---

## 1. 现状审计（有代码依据）

> 本节只列与 V3.3.0 直接相关的现有能力与缺口。行号会随代码变化，实施时以文件内容为准。

### 1.1 可复用的现有基础

| 能力 | 现有落点 | 事实 | 对 V3.3.0 的含义 |
|---|---|---|---|
| **场景模板落地** | `OBS_Helper.Wpf/Services/ObsConfig/SceneTemplateService.cs` | `ApplyAsync` 会新建场景集合、设置画布/过渡、逐场景 `CreateScene`、逐来源 `CreateInput`/`CreateSceneItem`、应用变换/层级/滤镜；跨场景 `shared` 来源复用；失败记占位提示 | 虚拟直播间不需要重写落地器；扩展“资产/浏览器源/角色”即可 |
| **模板数据** | `OBS_Helper.Wpf/Assets/scene_templates.json` / `.en-US.json`、`Models/ObsConfig/SceneTemplate.cs` | 已有 12 套模板；来源支持 `inputKind`、`settings`、`transform`、`fallbackKinds`、`placeholder`、`filters`、`shared` | 新房间模板可沿用同一 schema；新增字段须保持旧模板可读 |
| **一键推流** | `Services/Shell/SimpleStreamingService.cs` | 检查推流服务是否填了服务器/密钥（只判存在性，不保留密钥）、开播、轮询确认、停播；`simple.stream.blocked.noKey` 已覆盖“没填密钥” | 开播引导应复用其“检查 → 开播 → 确认”骨架，不另起一套状态机 |
| **一键录制 / 拉起 OBS** | `Services/Shell/SimpleRecordingService.cs`、`Services/ObsConfig/ObsLaunchCore.cs`、`ObsEnsureCore.cs` | 已有“自检 → 落地 → 拉起 OBS → 等待 WebSocket → 开录”的完整链路；`EnsureConnectedAsync` 支持单飞/超时/取消 | Live2D/开播引导需要“确保 OBS 在运行且已连接”时直接复用，不再写第二套拉起逻辑 |
| **OBS 请求透传** | `Services/Obs/ObsConnectionService.cs` | `RawRequestAsync(requestType, data, ct)` 可发任意 v5 请求；已有 `StartStream`/`StopStream`/`StartVirtualCam`/`StopVirtualCam`/`GetStreamServiceInfoAsync` | 新增 `SetStreamServiceSettings` 包装即可；不需要改 WebSocket 协议层 |
| **虚拟摄像头体检** | `Services/Tools/VirtualCamCheckCore.cs` / `VirtualCamCheckService.cs` | 只读检查 DirectShow 滤镜注册项、`win-dshow.dll`、obs64 进程，输出排查树 | 开播引导的“虚拟摄像头是否可用”直接复用；不要重复探测 |
| **官方 OBS 下载** | `Services/Update/ObsDownloadLinks.cs`、`ObsReleaseInfoService.cs`、`Controls/ObsDownloadCard.xaml.cs` | 官网/GitHub 白名单、GitHub API 解析 Windows 安装包、应用内下载、MZ 头校验；**不做静默安装** | 第三方软件下载可复用同一套“白名单 + 进度 + 用户运行安装器”模式 |
| **插件目录** | `Assets/plugins.json` / `.en-US.json`、`Services/Plugins/PluginCatalog.cs` | 已有 57 条精选插件；含 `Repo`、`Dlls`、分类、维护状态、AI 开销、`ObsCompat` | 插件一键安装的“精选目录”可直接扩展，不需要从零建数据 |
| **插件本地扫描** | `Services/Plugins/LocalPluginScanner.cs`、`PluginScannerCore.cs` | 已覆盖 `obs-plugins\64bit`、OBS 32.x 的 `<插件根>\<插件名>\bin\64bit`、用户级 `%AppData%\obs-studio\plugins`、全局 `%ProgramData%\obs-studio\plugins` | 插件管理页的“已安装”数据源已存在；补“安装清单/版本/回滚”即可 |
| **插件版本查询** | `Services/Plugins/PluginReleaseService.cs` | GitHub `releases/latest`，内存/磁盘缓存、失败冷却、在途合并；匿名限额 60 次/小时 | GitHub 搜索/资产解析可复用节流思路；搜索 API 限额更低（10 次/分钟），必须另做缓存与用户触发 |
| **密钥存储** | `Services/Host/HostBridge.cs` | `secrets.dat`：值级 AES-256-GCM + 文件级 DPAPI；`SetSecretAsync`/`GetSecretAsync`/`DeleteSecretAsync` | VTube Studio API token、可选“记住推流码”、可选 GitHub token 都走这里；绝不进 `prefs.json`/日志 |
| **外观令牌** | `Services/AppearanceService.cs`、`Themes/Palette.xaml` | 已有主题/强调色/四档字号/高对比/减少动画/系统文本缩放；资源通过 `DynamicResource` 全局生效 | 无障碍/老年模式应在同一条 `Apply()` 链上扩展，不另建一套主题系统 |
| **轻提示** | `Services/ToastService.cs`、`Themes/Controls.xaml` | 已有按严重度停留、悬停暂停、最近 30 条历史、`LiveRegionChanged` | 还需要“键盘可关闭、可聚焦、可回看”的无障碍出口 |
| **新手引导状态机** | `Services/Shell/OnboardingGuide.cs` | 纯逻辑步骤数组 + 游标 + 路由校验；每步自动导航 | 开播/插件/Live2D 向导可复用同一模式：纯逻辑步骤 + 页面渲染 |
| **中英文案与内容回退** | `Localization/Strings.cs`、`StringTableZhHans.cs`、`StringTableEnUs.cs`、`Localization/ContentAssets.cs` | 中英双语、缺失回退中文并显示提示条 | 所有新功能必须同时补中英文案；平台/模板数据用 `.en-US` 并列资产 |

### 1.2 关键缺口

| 缺口 | 现状 | 影响 |
|---|---|---|
| **无 `SetStreamServiceSettings` 封装** | 只有 `GetStreamServiceInfoAsync`（读存在性）；没有写入推流服务配置的封装 | 开播引导必须新增薄封装，并处理 v5 与 v4 的差异 |
| **无第三方应用检测/下载/安装框架** | `ObsDownloadCard` 只针对 OBS；没有通用 `CompanionApp` 目录、进程/注册表探测、签名校验 | 抖音/B 站/VTube Studio 的“检测 → 下载 → 启动”需要新服务 |
| **无 UI Automation 辅助层** | 全仓没有 `UIAutomationClient` 使用 | “辅助选择虚拟摄像头”只能作为 P1，且必须可回退 |
| **无插件安装事务** | 插件页只打开 Releases 页面；`PluginScannerCore` 只读扫描 | 一键安装/更新/卸载/回滚是全新能力 |
| **无本地 overlay 服务** | 模板只支持 `browser_source` 类型，但没有本地 HTTP 服务/资产解析 | 虚拟直播间的动态聊天占位、时钟、素材 URL 需要新服务 |
| **无“角色/资产”语义** | `TemplateSource` 只有 `Name`/`InputKind`/`Settings` 等 | 应用后无法自动把“头像位”交给 Live2D、把“聊天位”交给 widget |
| **无无障碍模式/老年模式** | 只有单项“高对比/减少动画/字号”；没有密度、模式组合、简化布局 | 用户需要自己逐项调，且无法整体切换 |
| **动态卡片可访问名称覆盖不足** | 多数页面 XAML 的 `AutomationProperties.Name` 为 0；`TabIndex` 全仓零命中；无 skip link | 读屏用户只能靠标准控件默认层，动态卡片/状态无法可靠朗读 |
| **Toast 键盘出口不足** | 可鼠标点击关闭，但卡片本身不可聚焦、无可见关闭按钮 | 键盘用户无法主动关闭/回看提示 |
| **高 DPI 最小宽度风险** | `MainWindow.xaml` `MinWidth="920"`，内容区只有垂直滚动 | 150%/200% 缩放下可能超出可用宽度；老年模式必须重排而不是硬撑 |
| **减少动画不跟随系统** | 只认应用内开关；`SystemParameters.ClientAreaAnimation` 零命中 | 系统已关动画时应用仍会动，前庭功能障碍用户体验不一致 |
| **高对比语义色覆盖有限** | 只推文字/线条/背景，语义色与柔和底不变 | 高对比模式下状态色仍可能不达标 |
| **音频提醒只有托盘气泡** | `AudioAlertService` 调 `TrayService.Notify` | 听障用户看不到持续状态；需要应用内视觉横幅 + 状态区 |
| **图表无文字替代** | `Controls/Sparkline.cs` 等自绘图形没有可访问名称/数值摘要 | 读屏用户拿不到趋势信息 |
| **迷你小窗键盘不可用** | `ShowActivated="False"`，热键呼出时焦点不进来 | 键盘用户无法操作小窗按钮；需提供可选激活方式或等价入口 |

---

## 2. 版本目标、原则与非目标

### 2.1 版本目标

1. **把“虚拟直播”从一堆散落教程变成一条可走通的产品路径**：选形象 → 选房间 → 选平台 → 开播。
2. **把插件从“打开网页自己找”升级为“可信目录 + 可回滚安装 + 可管理”**，同时保留 GitHub 的开放性。
3. **把无障碍从“几个开关”升级为“可整体切换、可组合、可验证的模式”**，并新增老年模式。
4. **所有新能力都必须可失败、可回退、可解释**：没有 OBS、没有 VTS、没有插件、没有网络、没有管理员权限时，用户知道下一步做什么。

### 2.2 设计原则

- **本地优先**：Live2D 模型、房间素材、插件安装包默认只在本机处理；不上传模型/素材/推流码。
- **官方源优先**：OBS、抖音直播伴侣、B 站直播姬、VTube Studio、GitHub Releases 只从官方域名/官方仓库下载；拒绝第三方镜像与“下载站”。
- **默认不破坏**：模板默认新建场景集合；修改现有集合前必须备份；插件安装前备份旧文件；所有写操作可回滚。
- **密钥最小化**：推流码默认不落盘；VTS token/GitHub token 只进 `secrets.dat`；任何日志/诊断/AI 请求都不得包含密钥。
- **第三方代码分级**：精选插件（有哈希/签名/兼容性）可一键安装；任意仓库只做“导入 + 强警告 + 用户确认”。
- **无障碍是默认能力，不是附加页**：新页面从第一行 XAML 就带可访问名称、键盘路径、焦点管理和实时区域。
- **零第三方 NuGet 包的底线不破**：优先 BCL（`System.Net.WebSockets`、`System.IO.Compression`、`System.Text.Json`、`System.Windows.Automation` 框架程序集）。需要 JS/原生运行时的方案必须先过许可证与打包评审。
- **中英双语 + 数据驱动**：平台、模板、插件安装元数据都走 JSON 资产；逻辑值与展示文案分离，语言切换不冻文案。

### 2.3 非目标（V3.3.0 明确不做）

- 自研 Live2D 渲染器/面捕推理。
- 自动登录平台账号、自动过验证码、自动点击平台“开始直播”。
- 静默安装第三方软件或静默提权。
- 自动修改 Watt Toolkit 配置、hosts、系统代理、防火墙。
- 任意 GitHub 仓库的无人值守安装。
- 平台弹幕/礼物/收益 API 的账号级集成。
- 插件“启用/禁用”开关（OBS 没有稳定的外部 API；移动 DLL 风险高）。

---

## 3. 总体架构

### 3.1 产品结构：一个“虚拟直播”工作台 + 一个“插件广场”

导航不新增五个入口，避免侧栏膨胀（尤其老年模式）：

```
虚拟直播（一级导航，Routes.VirtualLive = "virtuallive"）
├── 形象（Live2D）      parameter = "avatar"
├── 直播间（模板）      parameter = "room"
└── 开播（平台引导）    parameter = "golive"

插件广场（沿用 Routes.Plugins = "plugins"）
├── 精选
├── GitHub 搜索 / 导入
├── 已安装 / 更新
└── 加速与网络诊断
```

- 首页 / 搭建页各放一张“虚拟直播”入口卡，深链到 `virtuallive/avatar`、`virtuallive/room`、`virtuallive/golive`。
- 现有 `Routes.Templates`（通用场景模板）保留；新房间模板也可以从模板页按 `vtuber` 标签筛出，避免用户找不到。
- 插件广场沿用现有页面，新增 Tab；不新建第二个插件页。

### 3.2 服务分层

```
Views
  VirtualLivePage (Tabs)
    ├── Live2DPanel / Live2DSetupPage
    ├── RoomTemplatePanel
    └── GoLivePanel
  PluginsPage（扩展 Tab）
  Controls/RoomTemplateCard / PlatformCard / InstallProgress / StepStatusList

Services（新增）
  Services/Live/
    VirtualLiveService          编排“形象/房间/开播”三条流程的公共会话
    Live2DSetupService          模型导入 → VTS 鉴权/加载 → OBS 采集源 → 场景落地
    Live2DModelCore             纯逻辑：模型校验、清单、哈希、路径安全
    VTubeStudioClient           本地 WebSocket 客户端（8001 / UDP 发现 47779）
    VtsProtocolCore             纯逻辑：请求/响应 JSON、错误码映射
    Live2DCaptureService        OBS 源探测与创建（NDI/Spout2/虚拟摄像头/窗口采集）
    RoomTemplateService         房间模板加载/校验（可复用 SceneTemplateService 落地）
    RoomAssetService            资产包解压、哈希校验、路径解析
    RoomOverlayServer           本地 HTTP（TcpListener，仅回环）
    GoLiveService               开播编排（直推/虚拟摄像头/官方软件）
    GoLiveCore                  纯逻辑：步骤状态机、平台模式判定、错误分类
    PlatformCatalogService      平台目录加载/校验/语言选择
    PlatformAppService          第三方直播软件检测/启动/版本读取
    CompanionInstallerService   官方安装包下载、签名/哈希校验、运行安装器
    StreamKeyService            推流码的临时内存、遮罩、剪贴板导入、清除
    UiAutomationAssistService   可选：UI Automation 辅助选择摄像头（强回退）
    AuthenticodeVerifier        WinVerifyTrust P/Invoke（无第三方包）
    InstalledAppLocator         进程/注册表/安装目录/Steam 库的统一探测

  Services/Plugins/（扩展）
    PluginInstallService        事务安装/更新/卸载/回滚
    PluginInstallCore           纯逻辑：资产选择、zip 路径映射、清单、版本比较
    PluginReleaseAssetService   GitHub Release 资产解析（缓存/限频）
    GitHubSearchService         GitHub 搜索/仓库导入（缓存/限频/可选 token）
    PluginRecipeService         安装后配方（加滤镜/加源/打开对话框/文档）
    NetworkAccelerationService  Watt 检测/启动/引导；GitHub 连通性诊断
    PluginStateStore            installed.json / backups / downloads

  Services/Appearance/（扩展）
    UiProfileCore               纯逻辑：无障碍模式/老年模式/系统偏好 → 有效令牌
    UiProfileService            持久化、组合、通知（或并入 AppearanceService）

Models
  Models/Live/LivePlatform.cs
  Models/Live/Live2DModel.cs
  Models/Live/RoomTemplate.cs（或扩展 SceneTemplate）
  Models/Live/GoLivePlan.cs
  Models/Live/CompanionApp.cs
  Models/Plugins/PluginInstallManifest.cs

Assets
  Assets/platforms.json / platforms.en-US.json
  Assets/virtual_rooms.json / virtual_rooms.en-US.json
  Assets/rooms/<theme>/...（内嵌 zip / 资源清单）
  Assets/plugin_install.json（或并入 plugins.json 的 install 字段）
```

### 3.3 组合根与生命周期

- 在 `AppServices.cs` 增加 Lazy 单例：`VirtualLive`、`Live2D`、`Rooms`、`GoLive`、`PlatformApps`、`PluginInstall`、`PluginSearch`、`NetworkAcceleration`、`UiProfile`。
- 依赖方向保持 **Views → Services → Models**；纯逻辑 Core 不引用 WPF，直接链接进 `OBS_Helper.Wpf.Tests`。
- 长任务（下载、解压、OBS 写操作、VTS 请求）全部 `async` + `CancellationToken`；UI 线程只做状态渲染。
- 所有第三方进程/网络/文件操作都要有超时、取消、单飞（同一流程不并发两份）。
- 页面离开时取消订阅/停止计时器；`RoomOverlayServer` 跟随应用生命周期，关闭时释放端口。

### 3.4 数据与目录

| 数据 | 位置 | 说明 |
|---|---|---|
| 用户设置 | `%LocalAppData%\OBS_Helper\prefs.json` | 模式、最近平台、最近模板、下载偏好等非机密 |
| 机密 | `%LocalAppData%\OBS_Helper\secrets.dat` | VTS token、可选推流码、可选 GitHub token；DPAPI + AES-GCM |
| Live2D 模型 | `%LocalAppData%\OBS_Helper\live2d\models\<slug>-<hash8>\` | 应用托管副本；不覆盖用户原文件 |
| 房间资产 | `%LocalAppData%\OBS_Helper\rooms\<templateId>\<version>\` | 版本化、不可变；应用后清理旧版本 |
| 插件状态 | `%LocalAppData%\OBS_Helper\plugins\installed.json` | 应用管理的安装清单；不包含未管理插件 |
| 插件备份 | `%LocalAppData%\OBS_Helper\plugins\backups\<id>\<version>\` | 更新/卸载前备份；保留最近 2 份 |
| 插件下载 | `%LocalAppData%\OBS_Helper\plugins\downloads\` | 临时文件；校验后删除或移入备份 |
| 房间/平台缓存 | `%LocalAppData%\OBS_Helper\data\...` | GitHub/平台元数据缓存，带 TTL |
| OBS 配置 | OBS 自己的配置目录 | 所有写操作先过 `ObsSafePath`；默认新建集合 |

> **Win7 兼容构建**：数据目录已有 `_Win7` 分支逻辑；新增目录必须复用 `HostBridge.AppDataDirectory`，不要自己拼 `%LocalAppData%`。

### 3.5 安全边界

| 面 | 措施 |
|---|---|
| 第三方下载 | 只允许 https + 官方域名/官方 GitHub 仓库；校验重定向后的最终域名；MZ/PE 头；SHA-256（目录有则必须匹配）；Authenticode `WinVerifyTrust`；不静默安装 |
| 第三方进程 | 只按明确进程名/安装目录/注册表项探测；启动前展示路径与签名；不注入、不读取账号数据 |
| UI Automation | 默认关闭；用户显式开启“辅助模式”；只操作白名单窗口/控件；任一步失败立即停止并回退图文；不点击“开始直播” |
| 推流码 | 默认不落盘；只进内存；UI 遮罩；日志/诊断/AI 永不包含；可选“记住”才进 `secrets.dat` |
| VTS token | 只进 `secrets.dat`；可撤销/清除；不写日志 |
| 本地 HTTP | 只绑 `127.0.0.1`；随机端口；随机路径 token；只读静态资源；防目录穿越；CSP；无远程请求 |
| zip 解压 | 拒绝绝对路径/`..`/符号链接；限制文件数/总大小/压缩比；只允许预期扩展名；目标路径二次校验 |
| 插件 DLL | 只从目录/官方 Release 资产安装；校验 PE 架构 x64；备份/回滚；要求 OBS 关闭；可选 Defender 扫描 |
| 日志 | 复用 `LogSanitizer`；新增平台密钥/token 规则；路径中的用户名可保留但不得带密钥 |

---

## 4. 功能一：Live2D 一键配置

> 详细协议、模型校验、OBS 采集链路见 [`V3.3.0_LIVE2D_INTEGRATION.md`](V3.3.0_LIVE2D_INTEGRATION.md)。

### 4.1 用户故事

- 我是虚拟主播，已有 Live2D 模型（`.model3.json` 目录）和 VTube Studio。
  我希望点“一键配置”，选模型 → 选布局，应用自动把模型加载进 VTS，并在 OBS 里建好带透明背景的“形象”源。
- 我没有 VTube Studio。
  我希望应用告诉我它是什么、为什么需要、从哪里官方下载，并在装好后继续。
- 我已有 VTS 但 API 没开/授权被拒。
  我希望看到明确原因和下一步，而不是“配置失败”。

### 4.2 路线比较（已确认采用 VTube Studio 接入）

| 路线 | 做法 | 可行性 | 优点 | 风险/成本 | 结论 |
|---|---|---|---|---|---|
| **A. VTube Studio 集成** | 检测/启动 VTS；WebSocket API 鉴权、列模型、加载模型、移动/缩放、开 NDI；OBS 建 NDI/Spout/虚拟摄像头/窗口源 | **高** | 现成面捕、物理、表情、热键；用户生态成熟；不碰 Live2D 运行时许可证 | 依赖 VTS；首次授权；API 版本变化；NDI 插件可能未装 | **已选（P0）** |
| **B. 浏览器源自绘** | 本地 HTTP + HTML/JS + Cubism Core + pixi-live2d-display，OBS `browser_source` 渲染 | **中** | 不依赖 VTS；可做纯展示/待机 | Cubism Core 专有许可；`pixi-live2d-display` 2024-08 后无提交；无面捕；音频口型/参数驱动要另做；CEF/打包/安全成本 | **P2/实验，不建议 3.3.0** |
| **C. 原生渲染器** | C++/C# + Cubism Native SDK + 摄像头推理 | **低** | 完全自控 | 工程量巨大；模型/纹理/物理/动作/面捕/性能/打包；许可证；与零依赖冲突 | **不做** |
| **D. 通用追踪软件采集** | 不集成 API，只把 VTS/VSeeFace/PrprLive 窗口/NDI/Spout 采进 OBS | **高（只读）** | 覆盖已有用户；不依赖 API | 无法自动加载模型/布局；透明背景与窗口捕获不稳定；VSeeFace 主要是 VRM，不是 Live2D | **P1 兜底** |

**已确认组合**：A 为主，D 为兜底；B 3.3.0 不做，C 不做。

### 4.3 目标流程（状态机）

```
Idle
  → 检查环境（OBS 安装/运行/连接；VTS 安装/运行；模型路径）
  → 用户确认（复制模型到 VTS？启动 VTS？允许 API？）
  → 导入模型（校验 → 复制/复用 → 写清单）
  → 启动/连接 VTS（UDP 发现 47779 → ws://127.0.0.1:8001）
  → 鉴权（已有 token → AuthenticationRequest；否则 AuthenticationTokenRequest）
  → 选择/加载模型（ModelsRequest → ModelLoadRequest）
  → 选择布局（全身/半身/右下角/左侧）
  → 配置采集（NDI > Spout2 > VTS 虚拟摄像头 > 窗口采集+色键）
  → 落地 OBS 场景（新集合；创建/复用“形象”源；变换/层级）
  → 预览校验（GetSourceScreenshot / 虚拟摄像头状态）
  → 完成 / 可重试的失败
```

每一步都要：
- 可取消；可重试；幂等；
- 失败给 `ErrorCodes` + 一句人话 + 一个动作（重试/打开官方页/换采集方式/跳过）；
- 不把“部分成功”显示成“成功”；
- 不覆盖用户已有场景集合（默认新建；修改现有集合先备份）。

### 4.4 模型导入与校验

- 支持用户选择 `.model3.json` 或其所在目录；也支持从 VTS 已加载/已有模型列表直接选。
- 解析 `.model3.json` 的 `FileReferences`（`Moc`/`Textures`/`Physics`/`Pose`/`Motions`/`Expressions`/`UserData`），确认引用文件存在且在模型根内。
- 拒绝：路径穿越、符号链接/reparse point、缺失 `.moc3`、纹理尺寸/文件数/总大小超限、可执行文件混入。
- 计算全目录 SHA-256 清单；`<slug>-<hash8>` 作为托管目录名；同哈希复用，不重复复制。
- 复制到 VTS 的 `Live2DModels` 目录前，先通过 `VTSFolderInfoRequest` 读取真实目录名（不要硬编码）；复制后需要 VTS 重新扫描/重启才能出现在模型列表——流程要明确提示。
- 只管理应用复制进去的目录；用户原有模型不删除、不改名；卸载只删应用清单里的副本。
- 要求用户勾选“我拥有该模型的使用权/授权”；不内置任何 Live2D 示例模型。

### 4.5 OBS 采集链路（按优先级）

| 优先级 | 方式 | 适用 | 关键点 | 失败回退 |
|---|---|---|---|---|
| 1 | **NDI** | VTS 开启 NDI；已装 DistroAV/NDI 插件 | VTS API `NDIConfigRequest` 需要 `ControlNDISettings` 权限；OBS 侧 `ndi_source` 的 inputKind/字段按插件版本探测，不硬编码 | Spout2 |
| 2 | **Spout2** | VTS/追踪软件支持 Spout2；已装 Spout2 插件 | 同样先 `GetInputKindList` + 属性探测；注意 GPL 插件不随包分发 | 虚拟摄像头 |
| 3 | **VTS 虚拟摄像头** | VTS 提供 DirectShow 虚拟摄像头 | 用 `GetInputPropertiesListPropertyItems` 枚举 `dshow_input` 的 `video_device_id`，匹配含 `VTube Studio`/`OBS` 的设备；音频仍走麦克风，避免双声 | 窗口采集 |
| 4 | **窗口采集 + 透明/色键** | 以上都没有 | Windows 10 1903+ 可尝试 WGC 透明捕获；否则让 VTS 用纯色背景 + `chroma_key_filter_v2`；不承诺 100% 透明 | 图文引导 |

- **音频**：OBS 直接采麦克风（`wasapi_input_capture`），不要从 VTS/NDI 再采一路，否则会出现双声/回声。
- **布局预设**：全身居中（约 80% 高）、半身底部、右下角半身、左侧 1/3；按画布宽高计算 transform，处理竖屏。
- **场景落地**：如果用户从房间模板进入，把源放进模板的 `role=avatar` 占位；否则新建“虚拟形象”场景/集合。
- **预览**：`SaveSourceScreenshot` 或 `GetSourceScreenshot` 抓一帧；黑屏/全透明时给“检查 NDI/窗口/色键”的具体建议。
- **不承诺**：VTS 窗口最小化/遮挡、GPU 驱动、NDI 网络、第三方插件版本变化都可能导致采集失败；失败必须可回退。

### 4.6 VTS API 细节（核对结论）

- 默认 WebSocket：`ws://127.0.0.1:8001`；端口可被用户修改。
- UDP 发现：`47779`，每 2 秒广播 `VTubeStudioAPIStateBroadcast`，含 `active`/`port`/`instanceID`/`windowTitle`；用于找端口和多实例。
- 鉴权：`AuthenticationTokenRequest`（首次，VTS 内弹窗允许）→ `AuthenticationRequest`（后续会话）；token 最长 64 字符，存 `secrets.dat`。
- 模型：`VTSFolderInfoRequest` 取模型目录名；`ModelsRequest` 列可用模型；`ModelLoadRequest` 按 `modelID` 加载；`MoveModelRequest` 调整位置/缩放/旋转。
- NDI：`NDIConfigRequest` 需要 `ControlNDISettings` 权限；不要默认申请，只有用户选 NDI 且插件可用时才申请。
- 错误：用户拒绝授权、API 未开启、配置窗口打开、模型加载冷却（2 秒）等都要映射成可读错误；不要重试风暴。
- **只使用公开 API**：不注入 DLL、不读 VTS 进程内存、不改 VTS 配置文件。

### 4.7 失败分类与出口

| 场景 | 错误码（建议） | 用户看到什么 | 下一步 |
|---|---|---|---|
| 没装 OBS | `L2D_NO_OBS` | “没有找到 OBS” | 打开官方下载卡 |
| OBS 未运行 | `L2D_OBS_NOT_RUNNING` | “需要先启动 OBS” | 一键启动并等待连接 |
| OBS 未连接 | `L2D_OBS_NOT_CONNECTED` | “OBS 已启动但未连接 WebSocket” | 自动尝试连接 / 打开控制台 |
| 没装 VTS | `L2D_NO_VTS` | “需要 VTube Studio 才能做面捕” | 打开 Steam 商店/官方说明；或“仅采集已有窗口” |
| VTS API 未开 | `L2D_VTS_API_OFF` | “VTS 的 API 访问没有开启” | 图文步骤 + 重试 |
| 用户拒绝授权 | `L2D_VTS_AUTH_DENIED` | “你在 VTS 里拒绝了本应用” | 重新申请 / 打开 VTS 设置 |
| 模型无效 | `L2D_MODEL_INVALID` | 缺哪个文件/哪个路径越界 | 重新选择模型 |
| 模型复制失败 | `L2D_MODEL_COPY_FAILED` | 权限/磁盘/占用 | 换目录/关闭 VTS 重试 |
| 采集不可用 | `L2D_CAPTURE_UNAVAILABLE` | NDI/Spout/虚拟摄像头都没有 | 安装插件/改用窗口采集 |
| 预览黑屏 | `L2D_PREVIEW_BLACK` | 可能透明/未渲染/源隐藏 | 检查源显隐、NDI、色键、VTS 窗口 |

---

## 5. 功能二：虚拟直播间一键配置

> 模板清单、6 套画风、素材规范、SVG/下载索引与用户指南见 [`V3.3.0_VIRTUAL_ROOM_GUIDE.md`](V3.3.0_VIRTUAL_ROOM_GUIDE.md)。

### 5.1 目标

- 提供 6 套“像 VTuber 直播间”的模板：聊天/杂谈、游戏、歌回/纪念回、电台/陪伴学习、竖屏短视频、开播/暂离/下播三件套。
- 每套模板包含：场景、来源、布局、占位、资产、使用说明、素材许可。
- 一键应用后，用户只需要替换“形象源”“麦克风/桌面音频”“聊天 widget URL”“BGM 目录”四类东西。
- 不依赖平台账号 API；聊天/礼物用“用户自己的 widget URL”占位。

### 5.2 模板清单（详见配套文档）

| id | 名称 | 画布 | 场景 | 形象位 | 适合 |
|---|---|---|---|---|---|
| `vtuber-chat` | 虚拟主播 · 杂谈 | 1920×1080 30fps | 待机 / 杂谈 / 全屏互动 / 暂离 / 下播 | 半身底部/右侧 | 聊天、杂谈、歌回 |
| `vtuber-game` | 虚拟主播 · 游戏 | 1920×1080 60fps | 游戏 / 全屏游戏 / 杂谈 / 暂离 | 右下角半身 | 游戏直播 |
| `vtuber-radio` | 虚拟主播 · 电台 | 1920×1080 30fps | 电台 / 点歌 / 暂离 | 左下/居中半身 | 电台、陪伴学习 |
| `vtuber-debut` | 虚拟主播 · 纪念回 | 1920×1080 30fps | 开播等待 / 主视觉 / 全屏立绘 / 感谢 / 下播 | 全身/居中 | 新衣装、生日、纪念 |
| `vtuber-vertical` | 虚拟主播 · 竖屏 | 1080×1920 30fps | 竖屏主画面 / 全屏 / 暂离 | 上半身/居中 | 抖音/Shorts/Reels |
| `vtuber-triple` | 开播 / 暂离 / 下播三件套 | 1920×1080 30fps | Starting Soon / BRB / Ending | 可选小窗 | 任何虚拟主播 |

### 5.3 Schema 扩展（保持旧模板可读）

在 `SceneTemplate` / `TemplateSource` 上增加**可选字段**，旧 JSON 缺字段时按默认值处理：

```jsonc
{
  "id": "vtuber-chat",
  "title": "虚拟主播 · 杂谈",
  "assetPack": "cream-cloud",          // 资产包 id
  "tags": ["vtuber", "chat", "cute"],  // 模板页筛选
  "minAppVersion": "3.3.0",
  "scenes": [
    {
      "name": "杂谈",
      "sources": [
        {
          "name": "虚拟形象",
          "role": "avatar",             // avatar / chat / alert / caption / bgm / brb / ending
          "inputKind": "ndi_source",     // 落地时由 Live2D 流程替换/复用
          "placeholder": { "kind": "tracker", "hint": "由「Live2D 一键配置」绑定" }
        },
        {
          "name": "聊天框",
          "role": "chat",
          "inputKind": "browser_source",
          "browser": {
            "page": "chat.html",
            "width": 420,
            "height": 600,
            "css": "body{background:rgba(0,0,0,0);margin:0;overflow:hidden}",
            "rerouteAudio": false
          }
        },
        {
          "name": "装饰边框",
          "inputKind": "image_source",
          "asset": "overlays/frame.png", // 相对资产包根
          "transform": { "posX": 0, "posY": 0, "boundsType": "OBS_BOUNDS_STRETCH", "boundsWidth": 1920, "boundsHeight": 1080 }
        }
      ]
    }
  ]
}
```

实施时：
- `SceneTemplateService` 解析 `asset` → `RoomAssetService` 的绝对路径；
- 解析 `browser.page` → `RoomOverlayServer` 的 `http://127.0.0.1:<port>/<token>/<page>`；
- `role` 供应用后检查清单和 Live2D 绑定使用；
- 旧模板没有 `assetPack`/`role`/`browser` 时行为不变；
- `shared` 语义保持不变（麦克风/桌面音频跨场景复用）。

### 5.4 资产包

- 每个画风一个资产包：`Assets/rooms/<packId>/manifest.json` + `overlays/*.png` + `web/*.html/css/js` + `LICENSES.md`。
- 打包方式：内嵌 zip（单文件发布可读）或逐文件 `EmbeddedResource`；推荐 zip + manifest，应用时解压到 `%LocalAppData%\OBS_Helper\rooms\<templateId>\<version>\`。
- 解压前校验 manifest：文件列表、SHA-256、尺寸、数量、总大小；拒绝 zip slip。
- 资产版本不可变；更新发布新版本目录；应用后保留当前版本，旧版本延迟清理。
- 聊天/礼物/时钟等动态页面由本地 `RoomOverlayServer` 提供；**不代理平台账号请求**，不内嵌 token。
- 用户提供 widget URL 时，只允许 https，展示域名与“这是第三方页面”的提醒；不把 URL 写入日志。

### 5.5 应用流程

1. 选择模板 → 预览场景/来源/资产/所需插件/许可。
2. 选择“新建场景集合”（默认）或“当前集合”（需备份 + 二次确认）。
3. 应用画布/过渡/场景/来源/资产；逐项进度；可取消。
4. 应用后检查清单：
   - 绑定形象（Live2D 一键配置 / 选择已有源）；
   - 选择麦克风/桌面音频；
   - 设置聊天 widget URL（可选）；
   - 选择 BGM 目录（可选）；
   - 绑定场景热键（OBS 无 API，给提示）；
   - 录制 10 秒或截图预览；
   - 开播前体检（复用 `PreflightCheckService`）。
5. 失败/跳过项以“占位清单”呈现，不假装成功。
6. 结果页提供“继续配置 Live2D”“去开播”“打开使用说明”。

### 5.6 平台聊天/弹幕边界

- 不抓取平台弹幕、不内嵌账号 cookie、不调用未公开接口。
- 只提供：
  - 本地占位聊天框（显示“请粘贴你的聊天 widget URL”）；
  - 常见 widget 提供方的官方入口与图文说明；
  - 用户粘贴 URL 后写入 `browser_source`；
  - “清除/恢复占位”按钮。
- 这样既能让模板开箱可用，又不把账号安全和平台 ToS 风险带进应用。

---

## 6. 功能三：OBS 开播引导（国内 + 国外平台）

> 平台矩阵、官方入口、推流码路径、软件检测/下载方式见 [`V3.3.0_PLATFORM_MATRIX.md`](V3.3.0_PLATFORM_MATRIX.md)。

### 6.1 两种开播模式

| 模式 | 适用 | OBS 侧 | 平台侧 | 一键程度 |
|---|---|---|---|---|
| **A. OBS 直推（RTMP/RTMPS）** | B 站、YouTube、Twitch、Kick、Facebook、Trovo、NicoNico、AfreecaTV/Soop、自定义 RTMP 等 | `SetStreamServiceSettings` 写服务器/密钥 → `StartStream` → 轮询 `GetStreamStatus` | 用户在官方后台复制推流码 | **高**：选平台 → 粘贴码 → 开播 → 验证 |
| **B. 平台软件 + OBS 虚拟摄像头** | 抖音（官方路径）；其他不提供通用推流码/只允许自家客户端的平台 | `StartVirtualCam` → 确认虚拟摄像头设备 | 平台软件添加“摄像头”源，选择 `OBS Virtual Camera` | **中**：OBS 侧自动；平台侧首次需用户选择（可 P1 UI Automation 辅助） |

> **重要核对结论（2026-10-25）**
> - **抖音**：官方帮助明确给出“OBS 启动虚拟摄像机 → 直播伴侣添加摄像头 → 基础设置选择 OBS”的路径；没有面向普通主播的通用推流码入口。因此抖音以 **B 模式** 为主。
> - **B 站**：官方帮助中心当前明确给出“OBS 开播”流程：在直播间管理 → 开播设置获取 **rtmp 地址 + 直播码**，填入 OBS。因此 B 站**不应被默认归类为“只能虚拟摄像头”**；直推是首选，直播姬摄像头/第三方推流是回退。
> - 平台政策/入口会变；目录数据必须带 `verifiedAt`，实施时按官方页面复核，不把按钮路径写死在代码里。

### 6.2 直推流程（技术细节）

1. **选择平台**：从 `platforms.json` 读取；展示“直推/虚拟摄像头/需要登录/仅手动”。
2. **打开官方入口**：`HostBridge.OpenExternalAsync`；只允许官方域名；不内嵌登录页。
3. **获取推流码**：
   - 用户从平台后台复制；
   - 应用提供“从剪贴板粘贴”（用户点击后才读剪贴板，不持续监听）；
   - 显示遮罩/长度；不显示完整密钥；不写日志；
   - 可选“记住”（默认关，进 `secrets.dat`）。
4. **写入 OBS**：
   - v5：`SetStreamServiceSettings`，`streamServiceType="rtmp_custom"`，`streamServiceSettings={server,key}`；协议明确说明“简单 RTMP 用 `rtmp_custom` + `server`/`key`”。该请求没有 `save` 参数，写完必须 `GetStreamServiceSettings` 复核，持久化以 OBS 行为为准。
   - 写完 `GetStreamServiceSettings` 复核“有服务器/有密钥”（只判存在性，不保留密钥）。
   - **不要**用不存在的 `SetStreamSettings` 名称；当前代码也没有这个封装。
5. **开播前检查**：复用 `PreflightCheckService` + `SimpleStreamingService.CheckAsync`；检查编码器、磁盘、网络、是否已在录制/推流。
6. **开始推流**：`StartStream`；轮询 `GetStreamStatus`/等待 `StreamStateChanged`；确认 `outputActive=true` 才显示“已开播”。
7. **失败处理**：解析 OBS 错误/日志（`ObsLogAnalyzer`），给“密钥错误/服务器不可达/编码器失败/网络拥塞”等可执行出口；不要自动重试风暴。
8. **停止**：`StopStream`；只停“本次由我们启动”的输出；如果用户同时开了录制，按现有规则处理，不擅自停用户的录制。

**v4/Win7**：`SetStreamServiceSettings` 是 v5 能力；旧协议不假装支持。Win7 构建走“手动填写/文件通道/图文引导”，并在 UI 明确说明。

### 6.3 虚拟摄像头流程（抖音为主，其他平台复用）

1. **确保 OBS 虚拟摄像头可用**：
   - `VirtualCamCheckService` 检查驱动/插件；
   - `StartVirtualCam`；轮询 `GetVirtualCamStatus`；
   - 如果 OBS 未运行，先复用 `SimpleRecordingService` 的拉起/连接链路。
2. **检测/启动平台软件**：
   - 抖音直播伴侣（官方下载页）；
   - B 站直播姬（官方下载页，作为直推回退/摄像头方案）；
   - 其他平台客户端按目录配置。
3. **平台侧选择摄像头**：
   - 默认：图文引导，明确写出“添加素材 → 外接设备 → 摄像头 → 基础设置 → 选择 OBS Virtual Camera”；
   - P1：用户显式打开“辅助选择摄像头”，用 UI Automation 尝试展开菜单并选择含 `OBS Virtual Camera` 的项；每一步都校验，失败立即停；
   - **绝不**自动点击“开始直播”、不自动登录、不自动同意平台协议。
4. **验证**：
   - OBS 侧虚拟摄像头 `outputActive=true`；
   - 平台侧预览出现画面（用户确认 / 可选 UI Automation 读取预览控件状态，但不截图上传）；
   - 如果没有画面，检查：OBS 虚拟摄像头是否被其他软件占用、平台软件是否以管理员运行、摄像头列表是否刷新、OBS 是否在推流/录制中。
5. **停止**：
   - 先让用户在平台侧停止直播；
   - 再 `StopVirtualCam`；
   - 如果虚拟摄像头是用户自己开的，不要擅自关闭；记录“本次由我们启动”的标记。

### 6.4 第三方软件检测、下载与安装

| 软件 | 官方入口（核对日期 2026-10-25） | 检测 | 下载模式 |
|---|---|---|---|
| 抖音直播伴侣 | 官方文档 `https://streamingtool.douyin.com/docs/guide_yr6qkl09`；下载页 `https://www.douyin.com/falcon/webcast_openpc/pages/streamingtool_download/index.html` | 注册表卸载项/安装目录/进程/窗口标题，多信号交叉 | 官方页优先；若能解析官方直链则应用内下载，签名校验 |
| B 站直播姬 | `https://live.bilibili.com/liveHime` | 注册表/安装目录/进程 | 官方页优先；直链可用则应用内下载 |
| VTube Studio | Steam `https://store.steampowered.com/app/1325860/VTube_Studio/` | Steam appmanifest/安装目录/进程 | 打开 Steam 安装页（`steam://install/1325860` 可用时）；不静默装 Steam |
| OBS | 现有 `ObsDownloadLinks` | 现有 `ObsPathService`/进程 | 现有应用内下载 + 安装器 |

**安装原则**：
- 下载到临时目录；HTTPS + 官方域名白名单；限制大小；进度/取消；MZ/PE 头；SHA-256（有则校验）；`WinVerifyTrust` + 签名主体展示；**用户点击运行安装器**；不 `/S`、不静默、不替用户接受协议。
- 安装后重新探测；探测不到就给“打开安装目录/官方页/重试”。
- 第三方软件更新由其自身更新器负责；本应用不自动升级。

### 6.5 UI Automation 辅助模式（P1，默认关闭）

**能做的**：
- 找到平台窗口；展开“添加素材”菜单；找到“摄像头”；在摄像头下拉框中选择名称匹配 `OBS Virtual Camera` 的项；读取当前选中值确认。
- 每一步都有 `AutomationId`/Name/ControlType 白名单；找不到就停止。

**不能承诺的**：
- 平台改 UI、Electron/CEF 未暴露 UIA 树、应用以管理员运行而本应用不是、语言/DPI 变化、反自动化策略，都会让辅助失败。
- 因此必须有图文引导回退；辅助模式不能是唯一路径。

**合规**：
- 用户显式开启；显示“将操作哪个窗口、做哪几步”；可随时取消；不读取/上传账号信息；不绕过平台限制；不自动开播。
- 若平台条款禁止自动化，默认关闭并提示用户手动操作。

### 6.6 平台矩阵摘要

| 区域 | 平台 | 推荐模式 | 备注 |
|---|---|---|---|
| 中国 | B 站 | 直推（官方 rtmp 地址 + 直播码）；直播姬摄像头回退 | 官方帮助已给出 OBS 流程 |
| 中国 | 抖音 | 虚拟摄像头 + 直播伴侣摄像头源 | 官方帮助已给出步骤；无通用推流码 |
| 中国 | 快手 / 视频号 / 小红书 | 直推或平台客户端；**待逐项核验** | 不写死按钮路径；目录带 `verifiedAt` |
| 全球 | YouTube | 直推 RTMPS | YouTube Studio → 直播 → 流密钥；可能需要频道验证/24 小时开通 |
| 全球 | Twitch | 直推 RTMPS | Creator Dashboard → 设置 → 流；部分账号需 2FA |
| 全球 | TikTok | 直推（有资格）或 LIVE Studio 摄像头；**待核验** | 地区/年龄/粉丝门槛；不承诺所有账号可用 |
| 全球 | Kick / Facebook / Trovo / NicoNico / AfreecaTV(Soop) / BIGO / 自定义 RTMP | 直推为主 | 统一“复制服务器/密钥 → 写入 OBS → 开播”骨架 |
| 全球 | Instagram / X 等 | 手动/不支持 | 移动端或资格限制；不纳入一键 |

> 详细表格、官方 URL、待核验标记见 [`V3.3.0_PLATFORM_MATRIX.md`](V3.3.0_PLATFORM_MATRIX.md)。

---

## 7. 功能四：无障碍模式与老年模式

> 完整审计、令牌表、页面改造清单、测试矩阵见 [`V3.3.0_ACCESSIBILITY.md`](V3.3.0_ACCESSIBILITY.md)。

### 7.1 现状一句话

当前项目已经做到：键盘可操作（大部分标准控件）、状态不完全依赖颜色、跟随系统深浅色/高对比/文本大小、减少动画（应用内）、Toast 实时区域；但**屏幕阅读器只覆盖原生默认层 + 少数控件**，动态卡片、图表、Toast 键盘出口、迷你小窗、高 DPI 最小宽度仍有缺口。`ACCESSIBILITY.md` 已如实标注“未经 NVDA/Narrator 实测”。

### 7.2 两种模式，而不是一堆互斥开关

#### 无障碍模式（Accessibility Mode）

面向低视力、读屏、键盘/开关控制、色觉障碍、前庭功能障碍、听障、认知障碍用户。打开后：
- 字号至少 +15%；行高/段距增大；
- 焦点框始终可见（3 px 品牌色 + 2 px 偏移）；
- 减少动画（并跟随系统 `ClientAreaAnimation`）；
- 所有状态“图标 + 文字 + 颜色”；
- 动态结果/进度/错误进实时区域；
- 显示“跳到主内容”链接；
- 按钮最小 44×44 DIP，表单标签在输入框上方；
- Toast 可聚焦、可关闭、可回看；
- 音频提醒有应用内视觉横幅；
- 新页面（Live2D/房间/开播/插件）从设计上满足读屏/键盘要求。

#### 老年模式（Elder Mode）

面向不熟悉复杂界面的用户（不等于“老年人都会视力差”，所以它是**可选预设**，不自动推断年龄）：
- 字号 1.3~1.4×（与系统文本缩放相乘）；
- 密度“宽大”：卡片内边距 20~24 DIP，控件高度 ≥ 48 DIP；
- 侧栏更宽（240~260 DIP），导航项更大、可换行；
- 工具箱：常用工具置顶，其余收进“更多工具”；每张卡一个主操作，横向按钮组改为纵向；
- 表单/向导：一步一屏、一个主按钮、文字更短、错误就近显示；
- 隐藏装饰性壁纸/动效，保留高对比选项；
- 关键操作二次确认，但不做“每次弹窗”的骚扰；
- 仍然保留全部功能，不把高级功能藏死（“更多/高级”可展开）。

#### 组合与优先级

- 系统设置是**下限**：系统高对比、系统文本缩放、系统减少动画不能被应用关掉。
- 两个模式可叠加：字号/密度取较大值；无障碍特性取并集；高对比由用户/系统决定，不强制。
- 用户手动改单项后，该项不再被预设覆盖（“预设只改默认值”）。
- `UiProfileCore` 纯逻辑计算有效值，单测覆盖组合，避免“模式打架”。

### 7.3 稳定性要求

- 所有尺寸/间距/字号走资源令牌，不在页面里硬编码；`AppearanceService.Apply()` 一次写入，`DynamicResource` 即时生效。
- 模式切换不重启、不丢数据、不改变用户已保存的单选项。
- 200% 文本 + 150%/200% DPI 下不裁切关键按钮；内容区允许横向滚动兜底。
- 所有模式都有“恢复默认”与“预览”说明。
- 新增页面必须通过 `scripts/check_accessibility.py` 扩展规则 + 键盘/读屏手工清单。

---

## 8. 功能五：插件广场一键下载、安装与管理

> 完整多路线比较、安装事务、GitHub 搜索、Watt Toolkit 引导、安全模型见 [`V3.3.0_PLUGIN_MARKETPLACE.md`](V3.3.0_PLUGIN_MARKETPLACE.md)。

### 8.1 三条路线，已确认“精选为主 + GitHub 导入”

| 路线 | 形态 | 可行性 | 风险 | 决策 |
|---|---|---|---|---|
| **A. 精选卡片 + 一键安装/管理** | 现有 `plugins.json` 扩展安装元数据；卡片内“一键配置”；事务安装/更新/卸载/回滚 | **高** | 目录维护成本；资产/哈希需随版本更新 | **3.3.0 主路线（P0）** |
| **B. GitHub 原生搜索/导入** | 软件内搜索 GitHub `topic:obs-plugin` 等；结果卡片；粘贴 `owner/repo` 导入；选择 Release 资产 | **中高** | 任意仓库不可信；资产命名不统一；API 限额 10 次/分钟 | **3.3.0 次级路线（P0，强警告 + 高级确认 + 可回滚）** |
| **C. 内嵌 GitHub 网页** | WPF `WebBrowser`/WebView2 打开 GitHub 搜索页 | **低/不推荐** | `WebBrowser` 是 IE，GitHub 基本不可用；WebView2 需第三方包 + Evergreen 运行时，破坏零依赖/Win7 | **不做**；改为“外部浏览器打开搜索页” |

**已确认混合路线**：A 为主；B 做“原生搜索 + 粘贴导入”补充；C 只保留“在浏览器中打开 GitHub 搜索”按钮。

### 8.2 一键安装的核心不是“下载”，是“可信 + 可回滚”

- 精选插件目录增加：`assetPattern`、`sha256`、`size`、`layout`（OBS 32 新布局/旧布局）、`requiresAdmin`、`minObsVersion`、`license`、`recipe`。
- 下载：官方 GitHub Release 资产/官方站点；HTTPS；大小限制；进度/取消；拒绝 source code 资产；`.zip` 优先，`.7z` 不做（BCL 不支持）。
- 校验：SHA-256（目录有则必须匹配）；GitHub API `digest`（若有）；Authenticode（有签名则验，展示签名主体）；PE 架构 x64；可选 Windows Defender 扫描。
- 安装：要求 OBS 关闭；解压到 staging；校验目录结构；备份旧文件；事务替换；写 `installed.json`；失败回滚。
- 管理：已安装列表、版本、路径、更新、卸载（只删应用管理的文件）、回滚、打开目录、查看许可；不碰未管理的插件。
- GitHub 原生搜索/导入：结果卡片展示仓库/许可证/最近提交/Release 资产；粘贴 `owner/repo` 解析最新 Release；用户必须选择资产、勾选“未经过精选验证”并确认；安装仍走同一套事务/回滚，不静默。
- 安装后“配置”：按 `recipe` 加滤镜/加源/打开 OBS 对话框/打开文档；不能脚本化的插件只给图文，不假装“全自动”。

### 8.3 Watt Toolkit（Steam++）的正确接入方式

- **不建议**自动改 Watt 的配置、hosts、系统代理或防火墙：需要管理员权限、会与其他网络工具冲突、且属于第三方软件内部实现。
- **推荐**：
  1. 检测 Watt 是否安装/运行；
  2. 提供“打开 Watt Toolkit”与“打开 GitHub 加速说明”；
  3. 用户手动开启 GitHub 加速；
  4. 应用用**系统代理**下载（`HttpClient` 默认走系统代理），并做 GitHub 连通性/延迟诊断；
  5. 仍失败则回退官方页面/手动下载。
- 可选“自定义代理”设置（用户自填），但不内置任何镜像/破解代理。
- Watt Toolkit 是独立开源项目（GPL 系），**只做外部工具集成，不复制代码、不捆绑安装包**。

### 8.4 插件安装的硬约束

- 插件是 DLL，安装即执行第三方代码；**精选目录**与**任意仓库**必须分级。
- 未签名插件必须显示“未签名/无法验证发布者”，并要求显式确认。
- 不静默提权；ProgramData 安装需要 UAC 时明确说明；优先用户级/官方安装器。
- 不自动禁用杀软、不改系统策略、不静默运行 `.exe` 安装器。
- 更新/卸载前备份；失败可回滚；不删除用户 OBS 场景/配置。
- OBS 运行中不替换 DLL；提示关闭 OBS，绝不强杀。

---

## 9. 开源复用与素材合规

> 详细审计表、许可证、维护状态、复用结论见 [`V3.3.0_OPEN_SOURCE_AUDIT.md`](V3.3.0_OPEN_SOURCE_AUDIT.md)。

**总原则**：
- **能用协议就不抄代码**：OBS WebSocket、VTube Studio API 都按公开协议自己实现 C# 客户端。
- **GPL 代码不进 MIT 主程序**：DistroAV、Spout2、LocalVocal、Watt Toolkit 等只作为**外部可选软件/插件**，由用户自行安装；不捆绑、不复制。
- **不碰来路不明的 Live2D 运行时/示例模型**：Cubism Core 是专有许可；示例模型有使用限制；3.3.0 不自带模型。
- **素材必须可追溯**：原创 SVG 最稳；下载素材只用 CC0/CC-BY 等明确许可，记录作者/来源/许可证/哈希；不用同人图、平台 Logo、付费素材。
- **复用前先验证**：最近提交/Release、许可证、Issue/PR、构建可重复性、Windows 支持、依赖、测试、安全公告；不因为“星多”就复制。

---

## 10. 实施计划

### 10.1 阶段拆分（推荐按依赖顺序）

| 阶段 | 内容 | 产出 | 预估 |
|---|---|---|---|
| **P0-0 基础** | 设计令牌（密度/控件高度/焦点/最大宽度）、`UiProfileCore`、平台/模板/插件 schema、错误码、文案键规范、测试夹具 | 可编译的空壳 + 单测 | 1.5 周 |
| **P0-1 无障碍/老年** | 模式组合、全局焦点、skip link、动态卡片可访问名称/实时区域、Toast 键盘、高对比补全、系统减少动画、布局重排、工具箱简化 | 两个模式可用 + 自动化/人工验收 | 3 周 |
| **P0-2 虚拟直播间** | 6 套模板、6 套画风资产、资产包/本地 overlay、应用后清单、中英文文档 | 可一键应用 + 文档 | 3 周 |
| **P0-3 Live2D** | 模型校验/导入、VTS 客户端/鉴权/加载、采集源探测、场景布局、失败回退 | 端到端可用 | 3.5 周 |
| **P0-4 开播引导** | 平台目录、直推写入/校验、虚拟摄像头流程、软件检测/官方下载/签名、抖音/B 站路径 | 直推 + 抖音可用 | 3 周 |
| **P0-5 插件广场** | 精选安装/更新/卸载/回滚、已安装管理、GitHub 原生搜索、粘贴仓库导入、外部 GitHub 搜索入口、Watt 引导 | 精选一键安装 + GitHub 导入可用 | 5.5 周 |
| **P1** | 更多插件配方、UI Automation 辅助、ProgramData 提升助手、字幕引导、更多模板/画风 | 增量 | 3~5 周 |
| **P2** | 内置渲染器/面捕、内嵌浏览器、自动 Watt 配置、插件启用/禁用 | 评估后 | — |

> 已确认全上：单人顺序做约 **18~22 周**；两人并行（A：无障碍+房间+文档；B：Live2D+开播+插件）约 **12~14 周**。不再按 3.3.0/3.3.1 拆分；若必须压缩，优先把 GitHub 原生搜索降为增量，不削减安装安全与无障碍测试。

### 10.2 建议的提交/评审切分

1. `feat(a11y): UiProfileCore + tokens + focus/skip link`
2. `feat(a11y): dynamic cards names + live regions + toast keyboard`
3. `feat(rooms): schema + asset pipeline + overlay server`
4. `feat(rooms): 6 templates + assets + docs`
5. `feat(live2d): model core + VTS protocol client`
6. `feat(live2d): capture + scene integration + wizard`
7. `feat(golive): platform catalog + direct RTMP + virtual cam`
8. `feat(golive): companion detection/download/signature`
9. `feat(plugins): install core + transaction + state`
10. `feat(plugins): UI + GitHub import + Watt guidance`
11. `test/docs/release`

每一步都能单独构建、单独回滚；不要把所有功能塞进一个巨型 PR。

### 10.3 发布门禁

- 双 TFM 构建通过：`net10.0-windows` + `net6.0-windows`。
- `dotnet test` 全绿；新增纯逻辑 Core 覆盖关键分支。
- `OBS_SELFTEST=1` 无界面自检通过（新增路由/页面/模式重放）。
- `scripts/check_accessibility.py`、`check_resources.py`、`check_docs.py`、新增 `check_platform_catalog.py` / `check_plugin_manifest.py` / `check_room_assets.py` 通过。
- 手工矩阵：OBS 30/31/32、Win10/11、100/150/200% DPI、浅/深/高对比、Narrator/NVDA、键盘 only、200% 文本、老年模式。
- 安全复核：路径穿越、zip slip、未签名安装包、密钥泄漏、UI Automation 越权、日志脱敏。
- 发布说明中英文；明确“已验证/未验证”的边界。

---

## 11. 测试矩阵（摘要）

| 面 | 自动化 | 手工/集成 | 重点 |
|---|---|---|---|
| 无障碍 | `UiProfileCore` 单测；静态规则；对比度计算；路由/页面构造 | Narrator/NVDA、键盘 only、200% 文本、高对比、减少动画 | 动态卡片朗读、焦点返回、Toast、对话框、新向导 |
| 老年模式 | 令牌组合单测；布局 Measure/Arrange | 1366×768 @150%、1920×1080 @200% | 工具箱、设置、向导、按钮高度、无裁切 |
| 房间模板 | schema/资产/哈希/路径单测 | 应用/取消/失败回滚、素材替换、聊天 URL | 6 套模板全部可落地、无缺源 |
| Live2D | 模型校验/VTS 协议 JSON/错误映射单测 | VTS 当前版、API 开关/拒绝、NDI/Spout/vcam/窗口 | 透明、布局、音频不双路、失败回退 |
| 开播 | 平台目录/状态机/密钥遮罩单测 | B 站直推、抖音虚拟摄像头、YouTube/Twitch（网络允许时） | 写入 OBS、确认开播、错误分类、停止 |
| 插件 | 资产选择/zip 映射/版本/清单/回滚单测 | 真实插件安装/更新/卸载、未签名、坏 zip、OBS 运行中、无管理员 | 不破坏 OBS、可回滚、不误删 |
| 安全 | 路径/zip/URL/密钥规则单测 | 恶意 zip、伪造签名、重定向、代理、日志抽查 | 不执行不可信代码、不泄漏 |
| 网络 | 限频/缓存/离线单测 | GitHub 限流、Watt 开/关、系统代理、断网 | 不刷爆 API、可回退官方页 |

---

## 12. 风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| 平台 UI/入口频繁变化 | 引导失效、UIA 失败 | 目录数据带 `verifiedAt`；不写死按钮路径；图文回退；每版复核 |
| VTS API/版本变化 | Live2D 配置失败 | 协议客户端隔离；错误映射；版本探测；只使用公开 API；回退窗口采集 |
| NDI/Spout 插件未装 | 透明采集失败 | 能力探测；官方插件引导；虚拟摄像头/窗口+色键回退 |
| Cubism/模型许可证 | 不能分发/被投诉 | 不自带模型/运行时；用户自备；许可提示；法律评审 |
| GPL 插件/工具 | 许可证冲突 | 只做外部安装/引导；不复制、不捆绑；保留许可证与来源 |
| 任意插件是第三方 DLL | 安全风险 | 精选目录 + 哈希/签名 + 用户确认 + 备份回滚；任意仓库强警告 |
| 静默安装/提权 | 安全/信任风险 | 不静默；安装器由用户运行；UAC 明确说明 |
| 本地 HTTP 服务 | 端口/越权/信息泄露 | 只绑回环、随机端口、随机路径、只读、CSP、无远程请求 |
| 高 DPI/大字布局 | 裁切/不可用 | 令牌化、重排、横向滚动兜底、200% 实测 |
| 读屏未实测 | 虚假承诺 | 明确“静态检查 + 人工验收”；NVDA/Narrator 清单；文档标注边界 |
| Win7 兼容 | 新功能不可用 | 功能门禁；v4 手动/文件通道；不假装支持 `SetStreamServiceSettings` |
| GitHub API 限流 | 搜索/版本查询失败 | 缓存、失败冷却、用户触发、可选 token、外部浏览器回退 |
| Watt 冲突/权限 | 网络更差 | 只引导、不自动改系统；连通性诊断；可关闭/回退直连 |
| 范围过大 | 质量下降 | 分阶段/拆版本；P0 先横切能力；发布门禁 |

---

## 13. 决策记录与剩余待确认项

### 13.1 已确认（2026-10-25）

| # | 决策 | 结论 |
|---|---|---|
| 1 | Live2D 范围 | **采用 VTube Studio 公开 API 集成（路线 A）**。3.3.0 不做自研渲染器/面捕；浏览器自绘不作为 3.3.0 依赖。 |
| 2 | 3.3.0 是否全上 | **全上**：无障碍/老年 + 虚拟直播间 + Live2D + 开播引导 + 插件广场全部纳入 3.3.0。 |
| 3 | 插件安装范围 | **精选为主 + GitHub 导入**：精选插件可一键安装/管理；GitHub 原生搜索 + 粘贴仓库导入作为次级路径，必须高级确认、可回滚。任意仓库静默安装不做。 |
| 4 | 内嵌 GitHub 网页 | **不做**：外部浏览器 + 原生 API 结果卡片；不引入 WebView2。 |
| 5 | Watt Toolkit | **只做检测/启动/引导 + 系统代理**；不自动改 Watt 配置/hosts/系统代理，不捆绑。 |

### 13.2 仍按建议默认执行、实施前可再确认

| # | 决策 | 默认 |
|---|---|---|
| 6 | 第三方软件“无感配置” | 只做默认关闭的 UIA 辅助 + 图文回退；不做静默自动化 |
| 7 | 插件安装位置 | 用户级优先；ProgramData + UAC 作为高级选项 |
| 8 | 房间素材 | 原创 SVG/PNG 优先，CC0/CC-BY 补充；不用同人图/平台 Logo/Live2D 示例模型 |
| 9 | 老年模式默认值 | 1.3~1.4× 字号 + 宽大密度 + 简化布局；用户可调 |
| 10 | 推流码记忆 | 默认不记；显式勾选才进 `secrets.dat` |

---

## 14. 参考与核对记录

### 14.1 已核对（2026-10-25）

- **抖音官方帮助**：`https://streamingtool.douyin.com/docs/guide_yr6qkl09` —— “游戏主播添加 obs 作为摄像头素材”，明确 OBS 启动虚拟摄像机 → 直播伴侣添加摄像头 → 基础设置选择 OBS；另有采集卡方案。
- **抖音官方下载页**：`https://www.douyin.com/falcon/webcast_openpc/pages/streamingtool_download/index.html`；官方帮助站 `https://streamingtool.douyin.com/`。
- **B 站官方帮助**：`https://link.bilibili.com/p/help/index#/tools-tutorial` —— “OBS 开播”章节明确在直播间管理 → 开播设置获取 rtmp 地址 + 直播码，填入 OBS；直播姬下载 `https://live.bilibili.com/liveHime`。
- **VTube Studio API**：`https://github.com/DenchiSoft/VTubeStudio`（MIT，最近提交 2026-09-28）；默认 `ws://localhost:8001`；UDP 发现 `47779`；`AuthenticationTokenRequest` / `AuthenticationRequest` / `VTSFolderInfoRequest` / `ModelsRequest` / `ModelLoadRequest` / `MoveModelRequest` / `NDIConfigRequest`（需 `ControlNDISettings`）。
- **obs-websocket 协议**：`https://github.com/obsproject/obs-websocket`（GPL-2.0）；v5 请求名是 **`SetStreamServiceSettings`**（不是 `SetStreamSettings`）；字段 `streamServiceType` / `streamServiceSettings`；简单 RTMP 用 `rtmp_custom` + `server`/`key`。
- **OBS 官方插件指南**：`https://obsproject.com/kb/plugins-guide` —— 推荐 `C:\ProgramData\obs-studio\plugins\<插件名>\bin\64bit` + `data\locale`；旧布局 `obs-plugins\64bit` 将停止工作。
- **OBS 插件项目状态**：DistroAV（GPL-2.0，最近提交 2026-10-23）、obs-spout2-plugin（GPL-2.0，2026-10-11）、obs-localvocal（GPL-2.0，2026-10-15）均活跃；只作为外部可选插件。
- **pixi-live2d-display**：MIT，但最近提交 2024-08-20；不作为 3.3.0 依赖（详见开源审计）。
- **Live2D Cubism Samples**：许可为 “Other/NOASSERTION”（专有），不能按 MIT 处理。
- **OpenSeeFace**：BSD-2-Clause，2026-09 仍活跃；仅作为未来面捕后端评估。

### 14.2 未能核对/需实施时复核

- YouTube/Twitch/TikTok/Kick/Facebook/Trovo/NicoNico/AfreecaTV 的**具体后台按钮路径**：受网络与地区限制，本文只给官方域名/入口，不在代码里硬编码；实施时逐项截图核对，写入 `verifiedAt`。
- 快手/视频号/小红书的具体推流码入口：标记“待核验”，默认按“直推或平台客户端”处理，未核验前不承诺一键。
- Watt Toolkit 仓库许可证/当前配置方式：实施前重新核对 LICENSE 与官方文档；本方案只做外部引导，不复制代码。
- B 站直播姬当前版本的摄像头列表是否稳定出现 `OBS Virtual Camera`：以官方帮助的直推为主，摄像头方案作为回退，实测后写入目录。

### 14.3 核对原则

- 文档里的“已验证”只代表**当天从官方页面/协议文档读到的事实**；平台和第三方软件会变，实施时必须复核。
- 不把博客/视频教程当作平台政策依据；平台入口以官方帮助/官方客户端为准。
- 不在代码里硬编码平台按钮路径、推流服务器地址（可给默认值但允许覆盖）、插件资产文件名。
- 所有第三方下载/安装/自动化都必须有“用户可见、可取消、可回退”的路径。

---

## 附录 A：建议的新增路由与页面

| 路由/页面 | 说明 |
|---|---|
| `Routes.VirtualLive = "virtuallive"` | 一级导航“虚拟直播”；参数 `avatar` / `room` / `golive` |
| `Views/VirtualLivePage.xaml` | Tab 容器；根据参数切 Tab；`INavigationAware` |
| `Views/Live2DPanel.xaml` | 模型选择、环境检查、进度、预览、失败出口 |
| `Views/RoomTemplatePanel.xaml` | 模板卡片、预览、应用、检查清单 |
| `Views/GoLivePanel.xaml` | 平台选择、推流码、开播/停止、虚拟摄像头流程 |
| `PluginsPage` 扩展 | Tab：精选 / 搜索 / 已安装 / 更新 / 网络 |
| `Controls/PlatformCard.xaml` | 平台卡片（模式徽章、官方入口、状态） |
| `Controls/RoomTemplateCard.xaml` | 模板卡片（风格、场景数、形象位、许可） |
| `Controls/InstallProgress.xaml` | 下载/安装/回滚进度（可访问） |
| `Controls/StepStatusList.xaml` | 可访问的步骤状态列表 |

## 附录 B：建议的错误码前缀

| 前缀 | 领域 |
|---|---|
| `L2D_*` | Live2D / VTS / 采集 |
| `ROOM_*` | 房间模板 / 资产 / overlay |
| `GOLIVE_*` | 开播引导 / 平台 / 虚拟摄像头 |
| `PLUGIN_*` | 插件下载 / 安装 / 更新 / 卸载 |
| `NET_*` | GitHub / 代理 / 加速器 |
| `A11Y_*` | 模式/布局/无障碍能力 |

## 附录 C：完成定义（DoD）

- 所有 P0 功能在 Win10/11 主构建可用；Win7 构建不崩溃，明确降级。
- 所有新功能有中英文案、错误码、可执行出口。
- 所有写操作默认可回滚；所有第三方下载可校验；所有密钥不落日志。
- 所有新页面键盘可走通、读屏有名称/状态、老年模式不裁切。
- 文档、发布说明、验收记录齐全；未验证项明确标注。
