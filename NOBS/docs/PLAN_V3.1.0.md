# OBS帮助助手 V3.1.0 更新项目计划

> **版本**：V3.1.0（minor，功能增强版）
> **基线**：V3.0.0（`net10.0-windows` 主构建 + `net6.0-windows` Win7 兼容构建）
> **文档日期**：2026-10-18
> **文档状态**：规划稿。本文只做规划与可行性评估，**不修改任何源码、配置、安装脚本或发布物**。
> **本版范围**：① 个性化自定义壁纸；② 一键启动 OBS + 默认 WebSocket 连接；③ 提词器；④ AI 自动直播（用户自带 API，实验性）；⑤ 彩蛋；⑥ UI 视觉刷新；⑦ 补充有趣功能（1~5 进 3.1.0，6~12 进 3.1.x）。
> **第二轮追加**：2026-10-18 追加 ③~⑥；可行性、边界与合并排期见 **§12**。

---

## 0. 结论速览

### 0.1 两个需求都可行，但都不是“从零开始”

| 需求 | 可行性 | 现有基础 | 本版真正要补的缺口 |
|---|---|---|---|
| 个性化自定义壁纸 | **可行** | V1.10 已有「默认 / 纯色 / 单张本地图片」三种背景模式；主窗口内容区已有背景图层；设置页已有选图入口 | 图片没有纳入应用管理（只存绝对路径）、显示方式固定 `UniformToFill`、压暗强度固定、不能轮播、不能调模糊、文件丢失后静默回退、仅覆盖内容区 |
| 一键启动 OBS + 默认 WebSocket | **可行，但受 OBS 侧配置约束** | V2.9.4 已有「自检 → 文件通道落地 → 拉起 OBS → 等 WebSocket → 开录」的完整链路；`SimpleRecordAutoLaunch` 默认已为 `true`；`ObsLaunchCore` 已能解析 `obs64.exe` 路径 | 每次拉起前仍有确认弹窗；OBS 已在运行但未连接时直接阻断，不会自动重连；`AutoConnect` 默认关闭且只在「OBS 控制台」页尝试；没有检测/修复 obs-websocket 服务配置；托盘与全局热键没有走同一条链路 |

### 0.2 对“一键”的准确定义

“一键录制”能做到的是：**用户只点一次“开始简单录像”**，应用自动完成
“写配置（必要时）→ 启动 OBS（必要时）→ 建立 WebSocket 连接 → 调用 `StartRecord` → 确认进入录制状态”。

它不能做到、也不应承诺：

- OBS 首次启动向导、崩溃恢复提示、显卡驱动初始化等**OBS 自己的 UI 阻塞**无法被应用绕过；
- obs-websocket 服务被用户关闭、端口被改、密码未知时，应用不能“隔空”完成连接；
- OBS 未安装、安装损坏、被杀软拦截时，应用只能给出可照做的错误，不能替用户修好 OBS。

因此验收口径应是“**一次点击 + 有限等待 + 失败可照做**”，而不是“零等待、无条件成功”。

### 0.3 建议的 3.1.0 范围

**必须交付（P0）**

1. 壁纸库：多图导入 / 本地托管 / 缩略图选择 / 删除 / 持久化；
2. 壁纸显示：填充 / 适应 / 居中、压暗强度可调、恢复默认；
3. 壁纸轮播：固定间隔 + 随机顺序 + 最小化/非激活暂停；
4. 壁纸迁移与容错：旧 `bgImage` 自动导入、文件丢失/损坏可见提示、不崩溃；
5. 一键录制：OBS 未启动时自动拉起并连接；OBS 已启动但未连接时自动连接；连接成功后自动开录；
6. 一键录制：去掉每次点击的确认弹窗，改为首次一次同意 + 设置页可改；
7. 一键录制：分阶段进度、可取消、错误分类与可执行出口；
8. 双构建兼容：主构建 v5 与 Win7 构建 v4 都保留可用路径；
9. 单测、双构建回归、中英文案、无障碍静态检查、发布说明。

**时间允许则做（P1）**

- 壁纸模糊、浅色/深色各一张、平铺、整窗壁纸；
- obs-websocket 服务配置检测与“经用户同意后一键启用”；
- 启动时自动连接（仅当 OBS 已在运行，避免无意义重连）；
- 端口自动探测（4455 / 4444 各试一次）；
- 托盘/全局热键复用一键录制链路。

**明确不做（P2 / 非目标）**

- 视频壁纸、动态壁纸、在线壁纸市场；
- WebP / AVIF / HEIC 等 WPF 原生不支持的格式（除非引入第三方解码器，违背零依赖原则）；
- 自动关闭 obs-websocket 鉴权、自动重置 OBS 密码；
- 用 UI Automation 去点 OBS 的首次启动向导 / 弹窗；
- 自动安装或静默升级 OBS；
- 修改推流参数、输出模式或用户未同意的编码设置。

> 注：0.3 的 P0/P1 只列第一轮两项功能；第二轮追加后的合并优先级见 **§0.4** 与 **§12.6**。

### 0.4 第二轮追加功能结论（2026-10-18）

| 功能 | 可行性 | 关键条件 | 建议优先级 |
|---|---|---|---|
| **提词器** | **可实现** | 独立顶层窗口；窗口采集/游戏采集天然不包含它；显示器采集只有在 Windows 10 2004+ / Windows 11 且 OBS 采集后端配合 `WDA_EXCLUDEFROMCAPTURE` 时才可能排除，**不能承诺 100%**；Win7 不保证 | **3.1.0 P0** |
| **AI 自动直播（用户自带 API）** | **分层可行** | “用户自带 OpenAI 兼容 API + 固定规则 + OBS 白名单动作 + 外部脚本桥”可行；读平台弹幕、TTS、全自动无人直播不属于 3.1.0 | **3.1.0 P1 实验性（默认关闭）/ 完整版 3.2** |
| **彩蛋** | **可实现** | 纯本地、无网络、默认无声音、不干扰录制；可关闭；尊重“减少动画” | **3.1.0 P2** |
| **UI 视觉刷新** | **可实现** | 设计令牌 + 基础组件 + 关键页；19 个页面的全量重设计放 3.2 | **3.1.0 P1** |
| **补充有趣功能** | **可实现** | 倒计时 / 命名模板 / 开播检查清单 / 提词器场景联动 / 静音提醒进 3.1.0；其余 7 项进 3.1.x | **3.1.0 P1 / 3.1.x P2** |

**一句话建议**：3.1.0 的主线仍是“壁纸 + 一键录制”，**提词器**作为第三个 P0；
**UI 视觉刷新 + 补充功能 1~5** 作为 P1；**彩蛋**作为低风险 P2；
**AI 自动直播**先做实验性 MVP，完整自动直播放 3.2；补充功能 6~12 放 3.1.x。
若坚持把 AI MVP 也塞进 3.1.0，合并约 15~16 周（1 名开发者）；完整自动直播另立 3.2，
风险明显高于收益。

---

## 1. 现状审计（有代码依据）

### 1.1 壁纸 / 背景相关

| 现状 | 落点 | 事实 | 对 3.1.0 的含义 |
|---|---|---|---|
| 三种背景模式 | `Services/AppearanceService.cs` 的 `AppearanceSettings.BackgroundMode`：`default` / `color` / `image` | 已有默认、纯色、图片三种模式 | 不需要重做模式切换，重点是增强 `image` 模式 |
| 单张本地图片 | `AppearanceSettings.BackgroundImage` | 只保存**绝对路径**，不复制、不校验、不托管 | 文件移动/删除后静默回退；无法做壁纸库 |
| 图片加载 | `AppearanceService.LoadBackgroundImage()` | `BitmapImage` + `CacheOption.OnLoad` + `Freeze`，按路径缓存 | 大图会按原始分辨率解码，存在内存与首帧卡顿风险 |
| 渲染方式 | `MainWindow.xaml` 内容区图层 | `<Image Stretch="UniformToFill">` + 固定压暗 `Border` | 固定填充、固定压暗；不能适应/居中/平铺 |
| 压暗 | `ContentBackgroundDim` | 浅色 `#22000000`、深色 `#66000000` | 用户不可调；不同壁纸可读性差异大 |
| 高对比回退 | `ApplyBackgroundResources()` | 高对比时强制回默认底、清空图片 | 正确，必须保留 |
| 设置页 | `Views/SettingsPage.xaml(.cs)` | 三档模式 + 6 个纯色色板 + 选图/清除 | 没有壁纸库、缩略图、拖拽、轮播、模糊、预览 |
| 支持格式 | 设置页文件过滤器 | `jpg / jpeg / png / bmp` | 需要明确支持矩阵与不支持格式的提示 |
| 作用范围 | `MainWindow.xaml` | 背景层只在内容区（`Grid.Column=1`），侧栏与顶栏仍是 `SurfaceBrush` | 3.1.0 先做内容区；整窗作为 P1 |

**结论**：壁纸功能不是“没有”，而是“能用但不可管理、不可调、不可扩展”。3.1.0 应把它从
“选一张图”升级为“本地壁纸库 + 可调显示 + 可选轮播”，同时保留现有设置键的向后兼容。

### 1.2 一键录制 / OBS 连接相关

| 现状 | 落点 | 事实 | 对 3.1.0 的含义 |
|---|---|---|---|
| 一键录像状态机 | `Services/Shell/SimpleRecordingService.cs` | `Idle → Checking → SettingUp → LaunchingObs → WaitingObs → Recording → Stopping → Stopped` | 已有完整骨架；缺“主动连接”阶段与更细的进度 |
| OBS 未启动时的行为 | `SimpleRecordingService.StartAsync()` | 先用 `basic.ini` 文件通道落地，再按 `SimpleRecordAutoLaunch` 决定是否拉起 OBS | 方向正确；但每次拉起前有确认弹窗 |
| 拉起前确认 | `Controls/SimpleRecordCard.xaml.cs` 构造时注入 `ConfirmLaunch` | 服务层 `ConfirmLaunch is null` 时按“不允许”处理；离开页面时置空 | 托盘/热键/后台调用没有页面注入时会被拒绝；与“一键”目标冲突 |
| 自动拉起默认值 | `Models/Shell/ShellSettings.cs` | `SimpleRecordAutoLaunch = true` | 默认值已符合目标，不需要改默认 |
| OBS 已启动但未连接 | `RecordingEnvService.BuildPlanAsync()` | 返回 `env.blocked.obsRunning`；`SimpleRecordingCore.Check()` 判为阻断 | 用户必须先去控制台连接；3.1.0 要改成“自动尝试连接” |
| 连接能力 | `Services/Obs/ObsConnectionService.cs` | `ConnectAsync()`、`ScheduleReconnect()`、状态机 `Disconnected/Connecting/Authenticating/Connected/Reconnecting/Failed` | 能力已有；缺一个可等待、可超时、可取消、单飞的 `EnsureConnectedAsync()` |
| 自动连接设置 | `Services/Obs/ObsSettingsService.cs` | `AutoConnect` 默认 `false`；`AutoReconnect` 默认 `true` | “默认 WebSocket 连接”需要改默认值/迁移策略 |
| 自动连接入口 | `Views/ConsolePage.xaml.cs` | 只在首次进入“OBS 控制台”页时尝试一次 | 首页一键录制不会触发；需要前移到连接协调器 |
| OBS 路径 | `Services/ObsConfig/ObsPathService.cs` + `ObsLaunchCore.cs` | 支持注册表 `DisplayIcon`、常见安装目录、便携模式；找不到不猜路径 | 可直接复用，无需重写 |
| 等待就绪 | `SimpleRecordingService.LaunchAndWaitAsync()` | 每秒轮询，最长 `ObsReadyTimeoutSeconds = 75` 秒；OBS 退出会提前失败 | 保留 75 秒上限；补“已等待 N 秒”和取消 |
| 旧协议 | `SimpleRecordingService.StartAsync()` | v4 连着时跳过 `SetProfileParameter`，直接开录 | 3.1.0 必须保留，不能为了“统一 WebSocket”破坏 Win7 |
| 全局热键 | `Services/Shell/GlobalHotkeyService.cs` | `HotkeyAction.Record` 直接 `ToggleRecordAsync()` | 未连接时只提示；与“一键启动 OBS”不是同一条链路（P1 统一） |
| 托盘 | `Services/Shell/TrayService.cs` | 录制菜单项在未连接时禁用 | 不会自动拉起；P1 再评估是否放开 |

**结论**：用户要的能力在 V2.9.4 已有约 70% 的骨架，3.1.0 的主要工作是
**去掉摩擦（确认弹窗）＋ 把“连接”做成可等待的自动步骤 ＋ 统一入口与错误处理**，
而不是重写一套 OBS 启动器。

### 1.3 基线口径提醒

README 的「质量基线」标注 V3.0.0 为 **1209 项单测**，而 `RELEASE_NOTES_v3.0.0.md` 的
「验证」章节标注 **1009 项全部通过**。两处口径不一致，可能是 README 后续补充了测试但发布说明未回填。
**开工第一步**应先跑一次 `dotnet test OBS_Helper.Wpf.Tests`，以实际结果作为 3.1.0 的回归基线，
并在本计划执行时把真实数字写进发布说明与 README，避免继续漂移。

---

## 2. 版本目标、指标与原则

### 2.1 版本目标

1. **壁纸从“能用”变成“好用”**：用户可以在应用内管理多张壁纸、调整显示方式与可读性、按间隔轮换，
   不需要记住文件路径，也不会因为图片被移动而突然变回默认底。
2. **一键录制真正只剩一次点击**：OBS 没开就自动开，没连就自动连，连上就自动录；
   失败时给出能照做的下一步，而不是一句“未连接”。
3. **不破坏 V3.0.0 的底线**：数据安全（备份/回滚/路径护栏）、无障碍（高对比/减少动画/键盘）、
   Win7 兼容构建、零第三方包、中英双语。

### 2.2 可量化指标

| 指标 | 目标 | 测量方式 |
|---|---|---|
| 一键录制额外点击次数 | **0 次**（首次同意除外） | 手工走查 + 自检用例 |
| OBS 冷启动到开录 | 常见机器 ≤ 30 秒；上限 75 秒 | 真机计时（OBS 28+，含 31.x / 32.x） |
| 一键录制失败可解释率 | 100% 失败都有分类错误 + 至少一个出口 | 错误矩阵用例 |
| 壁纸导入到可见 | ≤ 2 秒（50 MB 以内、4K 图） | 真机计时 |
| 壁纸常驻内存 | 额外 ≤ 150 MB（两张 4K 图缓存） | 任务管理器 / 诊断日志 |
| 壁纸切换 UI 卡顿 | 主线程阻塞 ≤ 200 ms | 手工 + 日志 |
| 单测回归 | 全绿；新增用例覆盖两个功能的纯逻辑 | `dotnet test` |
| 双构建 | 主构建 + Win7 构建都能编译、自检通过 | `build.ps1` + Headless 自检 |
| 无障碍 | 新增控件可键盘操作、有可访问名称；高对比/减少动画有回退 | 静态检查 + 手工 |

### 2.3 设计原则

- **不静默改配置**：任何写 OBS 配置的动作都要有备份、读回校验、可回滚；
- **不猜路径**：沿用 `ObsLaunchCore` 的口径，找不到 `obs64.exe` 就让用户手动指定；
- **不关闭安全能力**：绝不为了连上而自动关闭 obs-websocket 鉴权或重置密码；
- **一次点击是同意，但不是无限授权**：首次拉起 OBS 给一次明确说明，之后记住选择；
- **失败要分类**：端口未开、密码错误、握手失败、OBS 退出、超时是不同问题，不能都叫“连接失败”；
- **双构建同源**：新代码不使用 .NET 7+ 独有 API；需要时走 `Services/Compat`；
- **零第三方包**：模糊、轮播、图片解码都用 BCL + WPF 实现。

---

## 3. 功能一：个性化自定义壁纸

### 3.1 用户故事

- 作为一个经常录屏的用户，我想把喜欢的图片设成应用背景，并且换电脑/移动文件后它还在；
- 作为一个长时间使用应用的用户，我想让几张壁纸自动轮换，而不是每天看同一张；
- 作为一个低视力/高对比用户，我希望壁纸不会影响我读文字，必要时自动回到纯色底；
- 作为一个普通用户，我不想理解“路径、解码、DPI”，我只想选图、看效果、点确定。

### 3.2 功能分层

| 优先级 | 功能 | 说明 |
|---|---|---|
| **P0** | 壁纸库 | 多图导入、复制到应用数据目录、按哈希去重、缩略图列表、删除、当前选中 |
| **P0** | 导入方式 | 文件选择器 + 拖拽到设置页；支持 `jpg/jpeg/png/bmp/tif/tiff`；单文件 ≤ 50 MB |
| **P0** | 显示方式 | 填充（`UniformToFill`）/ 适应（`Uniform`）/ 居中（原尺寸居中，超出裁剪） |
| **P0** | 可读性 | 压暗滑杆 0–80%；默认浅色 15%、深色 40%；高对比强制关闭壁纸 |
| **P0** | 轮播 | 开关、间隔 30 秒 / 1 分钟 / 5 分钟 / 15 分钟 / 30 分钟 / 1 小时、随机顺序 |
| **P0** | 轮播暂停 | 窗口最小化 / 非激活时暂停；恢复时从当前张继续，不跳张 |
| **P0** | 过渡 | 300 ms 淡入淡出；“减少动画”开启时直接切换 |
| **P0** | 预览与恢复 | 设置页即时预览；“恢复默认”一键回到主题默认底 |
| **P0** | 旧设置迁移 | 现有 `bgImage` 绝对路径在首次启动时导入壁纸库；文件不存在则提示并回退 |
| **P0** | 容错 | 文件丢失 / 损坏 / 超大 / 不支持格式：可见提示 + 回退，不崩溃、不静默 |
| **P1** | 模糊 | 0–24 px；低分辨率预模糊 + 缓存；默认关闭 |
| **P1** | 浅色/深色各一张 | 主题切换时交叉淡入；未设置则共用一张 |
| **P1** | 平铺 | `ImageBrush` + `TileMode.Tile`；适合小图/纹理 |
| **P1** | 整窗壁纸 | 覆盖侧栏与顶栏；需要同步调整半透明表面与文字对比 |
| **P2** | 视频/动态壁纸 | 性能、耗电、解码复杂度高，暂不做 |
| **P2** | 在线壁纸/市场 | 与“本地优先、数据不外传”的定位冲突，暂不做 |
| **P2** | 每页不同壁纸 | 维护成本高，先观察需求 |

### 3.3 P0 详细需求

#### W1 壁纸库

- 数据目录：`%LocalAppData%\OBS_Helper\wallpapers\`（Win7 构建为 `%LocalAppData%\OBS_Helper_Win7\wallpapers\`）；
- 文件命名：`<sha256>.<ext>`；同内容重复导入直接复用，不产生副本；
- `manifest.json` 记录 `id / file / name / addedAt / width / height / bytes`；
- manifest 损坏时：不阻塞启动，扫描目录重建，并写日志；
- 删除时：先从当前选择/轮播列表移除，再删除文件；被其他条目引用时不删；
- 启动时可选清理 7 天以上未被引用的孤儿文件（P1，默认关闭）。

#### W2 导入与校验

- 扩展名白名单：`.jpg/.jpeg/.png/.bmp/.tif/.tiff`；
- 单文件上限 50 MB；解码后单边上限 16384 px；
- 导入前用 `BitmapDecoder` 做一次可解码性校验，失败给“文件损坏或格式不支持”；
- 不支持 `.webp/.avif/.heic`，提示“当前版本不支持该格式，请转换为 JPG/PNG”；
- 导入是异步的：先显示进度/占位，不阻塞 UI；失败不改变当前壁纸。

#### W3 显示方式

| 模式 | WPF 实现 | 说明 |
|---|---|---|
| 填充 | `Stretch=UniformToFill` | 默认，铺满内容区，可能裁剪 |
| 适应 | `Stretch=Uniform` | 完整显示，可能留边（留边用主题底色） |
| 居中 | `Stretch=None` + 居中 + `ClipToBounds` | 原尺寸显示，超出部分裁剪 |
| 平铺（P1） | `ImageBrush` + `TileMode=Tile` | 小图/纹理 |

#### W4 可读性

- 压暗层：黑色 `Border`，`Opacity = 用户值`；
- 默认值：浅色主题 0.15、深色主题 0.40；
- 范围：0–0.80；低于 0.10 时提示“可能影响文字可读性”，但不强制阻止；
- 高对比模式：强制回默认底、隐藏壁纸（沿用现有逻辑）；
- 深色/浅色主题切换：若用户没有单独设置对应主题壁纸，保持当前图，只调整压暗默认值。

#### W5 轮播

- 仅在“壁纸库 ≥ 2 张”时可用；
- 间隔档位：30s / 1m / 5m / 15m / 30m / 1h；
- 随机：用 Fisher–Yates 洗牌；一轮内不重复；下一轮重新洗牌；
- 暂停条件：窗口最小化、应用失去激活、系统进入锁屏/睡眠（能检测到的范围内）；
- 切图失败：跳过该张、记录日志、继续下一张；连续失败 3 次则暂停并提示；
- 与“减少动画”联动：关闭淡入淡出，直接换图；
- 与高对比联动：高对比时停止轮播计时器，恢复后重新开始。

#### W6 预览与恢复

- 设置页选择后立即应用（与现有行为一致），不增加“应用”按钮；
- 缩略图网格显示当前选中态；键盘可 Tab / 方向键 / 回车；
- “恢复默认”清除当前壁纸选择，但**不删除**壁纸库文件；另设“管理壁纸”可删除；
- 提供“在资源管理器中显示”入口（可选，P1）。

#### W7 旧设置迁移

- 启动时若 `BackgroundMode == "image"` 且 `BackgroundImage` 非空：
  - 文件存在：后台导入壁纸库，成功后写 `bgWallpaperId`，保留 `bgImage` 作为兼容字段；
  - 文件不存在：回退默认底，显示一次可关闭提示“原壁纸文件已找不到，已恢复默认背景”；
- 迁移失败不阻塞启动，不删除原设置，下一次启动可重试；
- 迁移只做一次：用 `bgMigrationVersion` 标记，避免每次启动重复导入。

#### W8 错误处理

| 情况 | 行为 | 用户看到 |
|---|---|---|
| 文件不存在 | 回退默认底 | 一次提示 + “重新选择” |
| 文件损坏/无法解码 | 跳过并回退 | “这张图片无法读取，已跳过” |
| 格式不支持 | 不导入 | “请转换为 JPG/PNG/BMP/TIFF” |
| 文件过大 | 不导入 | “单张图片不能超过 50 MB” |
| 磁盘写入失败 | 不改变当前壁纸 | “无法保存壁纸，请检查磁盘权限/空间” |
| manifest 损坏 | 扫描重建 | 仅日志，不打扰用户 |

#### W9 性能

- 解码目标：按当前窗口客户区 × DPI 计算，最长边上限 3840 px；
- `BitmapCacheOption.OnLoad` + `Freeze()`；缓存最多 2 张（当前 + 下一张）；
- 轮播预加载下一张；切换时不再同步解码；
- 模糊图单独缓存（P1），不重复计算；
- 大图导入/解码在后台线程完成，UI 线程只做资源替换；
- 窗口尺寸变化不重新解码，只改变渲染尺寸；
- 退出/清空壁纸时释放 `BitmapImage` 引用，避免内存滞留。

#### W10 无障碍与国际化

- 壁纸库项：可聚焦、可键盘选择、`AutomationProperties.Name` 包含文件名与选中状态；
- 拖拽区域：提供等价的“选择图片”按钮，键盘用户不依赖拖拽；
- 压暗/模糊滑杆：`AutomationProperties.Name` + 数值朗读；
- 高对比：壁纸关闭，文字/边框使用高对比配色；
- 减少动画：轮播淡入淡出关闭；
- 新增文案全部进 `StringTableZhHans` / `StringTableEnUs`，键集由单测钉死；
- 英文文案避免直译“wallpaper”造成歧义，统一用 “background image / wallpaper” 并在设置页说明。

### 3.4 数据模型与存储

在 `AppearanceSettings` 中新增字段（全部带默认值，保证旧 `prefs.json` 可读）：

```json
{
  "bgMode": "image",
  "bgColor": "#f4f4fb",
  "bgImage": "C:\\old\\path.jpg",
  "bgWallpaperId": "a1b2c3...",
  "bgFit": "fill",
  "bgDim": 0.25,
  "bgBlur": 0,
  "bgSlideshow": true,
  "bgSlideshowSeconds": 300,
  "bgShuffle": false,
  "bgScope": "content",
  "bgLightId": "",
  "bgDarkId": "",
  "bgMigrationVersion": 1
}
```

兼容策略：

- 保留 `bgMode` 的 `default/color/image` 三个取值，**不新增** `wallpaper`，避免旧代码/旧配置分支判断失效；
- `bgWallpaperId` 为空时：先尝试旧 `bgImage`，再尝试壁纸库第一张；
- `bgDim` 用 `double`（0–0.8），不用百分比字符串，便于序列化与测试；
- 所有新字段读取时做 clamp / 枚举归一化，脏值回退默认，不让配置错误阻断启动。

壁纸库：

```
%LocalAppData%\OBS_Helper\wallpapers\
├── manifest.json
├── <sha256>.jpg
├── <sha256>.png
└── ...
```

`manifest.json`：

```json
{
  "version": 1,
  "items": [
    {
      "id": "3f2a1c...c9",
      "file": "3f2a1c...c9.jpg",
      "name": "夜景.jpg",
      "addedAt": "2026-10-18T12:00:00Z",
      "width": 3840,
      "height": 2160,
      "bytes": 5242880
    }
  ]
}
```

### 3.5 技术设计

#### 新增/调整的服务

| 文件 | 职责 | 备注 |
|---|---|---|
| `Services/Appearance/WallpaperCore.cs` | 纯逻辑：格式/大小校验、哈希文件名、fit/dim/blur 归一化、轮播顺序、迁移决策 | 零 WPF 依赖，单测直接链接 |
| `Services/Appearance/WallpaperStore.cs` | 壁纸库文件操作：导入、复制、删除、manifest 读写、孤儿清理 | 走路径护栏 + 原子写 |
| `Services/Appearance/WallpaperService.cs` | WPF 侧：解码、缓存、轮播计时器、当前图/下一张、主题联动、事件 | 不阻塞 UI |
| `Services/AppearanceService.cs` | 保留外观总入口；`ApplyBackgroundResources()` 委托给 `WallpaperService` | 保持现有资源键兼容 |
| `Controls/WallpaperLayer.xaml(.cs)` | 内容区背景控件：双层图片、fit、dim、blur、淡入淡出 | 替换 `MainWindow.xaml` 里现有 `<Image>` |
| `Views/SettingsPage.xaml(.cs)` | 壁纸库、导入、拖拽、显示方式、压暗、轮播、预览、恢复默认 | 复用现有外观卡片 |

#### 资源键

保留：

- `ContentBackgroundBrush`（回退底色）
- `ContentBackgroundImage`（兼容旧绑定）
- `ContentBackgroundDim`（压暗层）

新增（由 `WallpaperService` 写入，供 `WallpaperLayer` 使用）：

- `ContentBackgroundImageA` / `ContentBackgroundImageB`（双层交叉淡入）
- `ContentBackgroundFit`（`Fill` / `Fit` / `Center` / `Tile`）
- `ContentBackgroundOpacity`
- `ContentBackgroundBlurRadius`
- `ContentBackgroundFadeDuration`

#### 渲染实现

- `WallpaperLayer` 是 `Grid`：
  - 底层 `Border` 用 `ContentBackgroundBrush`；
  - 两个 `Image`（A/B）叠放，`Stretch` 由 fit 决定；
  - 顶层 `Border` 用黑色 + 用户压暗透明度；
  - 居中模式：`Image` 不拉伸、`HorizontalAlignment/VerticalAlignment=Center`、父级 `ClipToBounds=True`。
- 淡入淡出：只改两个 `Image.Opacity`，不重建控件；`ReduceMotion` 时直接切换；
- 模糊（P1）：先把图缩到 1/8 尺寸，再套 `BlurEffect`，最后放大绘制；缓存结果；
- 主题切换：若使用同一张图，只更新压暗默认值；若使用浅/深各一张，走一次交叉淡入。

#### 线程与生命周期

- `WallpaperService` 在 UI 线程创建，但解码通过 `Task.Run` 完成；
- 解码结果 `Freeze()` 后回到 UI 线程赋给 `ImageSource`；
- 轮播计时器用 `DispatcherTimer`，间隔来自设置；
- 窗口 `Deactivated` / `StateChanged` 时暂停，`Activated` 时恢复；
- 应用退出时停止计时器、清空缓存，避免静态事件拖住对象。

### 3.6 边界与风险

| 风险 | 说明 | 对策 |
|---|---|---|
| 大图内存 | 8K 图按原尺寸解码可能占用数百 MB | 解码上限 3840 px；缓存 2 张；导入时记录尺寸 |
| 模糊性能 | 全尺寸 `BlurEffect` 很贵 | 低分辨率预模糊 + 缓存；默认关闭 |
| 可读性 | 壁纸可能让文字不可读 | 默认压暗 + 高对比强制关闭 + 最低建议提示 |
| 文件被移动 | 旧实现静默回退 | 导入到本地库，不再依赖原路径 |
| manifest 损坏 | 壁纸库“消失” | 扫描重建 + 日志 + 不阻塞启动 |
| 格式不支持 | 用户选了 WebP/AVIF | 明确提示可转换的格式 |
| 轮播打断工作 | 切图动画/解码卡顿 | 预加载 + 减少动画 + 非激活暂停 |
| 整窗壁纸可读性 | 侧栏/顶栏文字对比不足 | P1 再做；需要半透明表面与对比度复核 |

### 3.7 测试计划

**纯逻辑单测（新增 `WallpaperCoreTests`）**

- 扩展名白名单 / 大小上限 / 尺寸上限；
- 哈希文件名稳定、同内容去重；
- `bgFit` / `bgDim` / `bgBlur` 的脏值归一化；
- 轮播顺序：顺序、随机不重复、单张不轮播、空列表；
- 迁移决策：文件存在/不存在/已在库中/旧字段为空；
- manifest 序列化/反序列化容错。

**WPF / 手工测试**

- 导入 10 张不同格式/尺寸，切换、重启后仍保持；
- 删除当前壁纸 → 回退默认 + 提示；
- 文件被外部删除 → 下次启动回退 + 提示；
- 高对比开启 → 壁纸关闭、文字可读；
- 减少动画开启 → 轮播无淡入；
- 125% / 150% / 200% DPI；
- 4K 图 + 轮播 1 小时，观察内存/CPU；
- Win7 构建（`.NET 6`）导入/切换/轮播。

### 3.8 验收标准

- [ ] 用户可以在设置页导入、选择、删除壁纸，重启后保持；
- [ ] 壁纸文件被移动/删除后，应用回退默认底并给出一次可见提示，不崩溃；
- [ ] 填充/适应/居中三种模式肉眼可辨，切换即时生效；
- [ ] 压暗滑杆 0–80% 生效，高对比模式强制关闭壁纸；
- [ ] 轮播按设定间隔切换，最小化/非激活暂停，减少动画时无淡入；
- [ ] 旧 `bgImage` 设置自动迁移，迁移失败不丢原设置；
- [ ] 导入 4K 图时 UI 不冻结超过 200 ms，常驻内存增量 ≤ 150 MB；
- [ ] 中英文案齐全，键盘可完成全部操作；
- [ ] 主构建与 Win7 构建均通过。

---

## 4. 功能二：一键启动 OBS + 默认 WebSocket 连接

### 4.1 用户故事

> 我点“开始简单录像”，希望它自己把 OBS 打开、自己连上、自己开始录。
> 我不想知道什么是 WebSocket，也不想先开 OBS 再回来点连接。

### 4.2 现状 vs 目标

| 场景 | 现状 | 3.1.0 目标 |
|---|---|---|
| OBS 未启动 | 文件通道落地 → 弹“启动 OBS 并开始录像？” → 同意后拉起 → 等 WebSocket → 开录 | **一次点击** → 必要时落地 → 自动拉起 → 自动连接 → 开录；首次给一次说明，之后不再问 |
| OBS 已启动但未连接 | `CheckAsync` 判为阻断：“OBS 在跑但没连上” | 自动尝试连接（用已保存密码/端口）→ 成功则开录；失败给分类错误与出口 |
| OBS 已连接 v5 | 应用设置 → 开录 | 保持；作为最快路径 |
| OBS 已连接 v4（Win7） | 跳过设置 → 直接开录 | 保持；显式提示旧协议限制 |
| OBS 正在录制 | 直接返回已录制 | 保持 no-op |
| OBS 正在推流 | 阻断 | 保持阻断（不抢输出） |
| 通过托盘/热键 | 未连接时禁用/提示 | P1：统一到一键录制链路；P0 先保证首页卡片 |
| 启动时自动连接 | 仅在首次进入“OBS 控制台”页尝试一次 | P1：仅当 OBS 已在运行时，启动后尝试一次；不在 OBS 关闭时制造重连噪音 |

### 4.3 目标决策矩阵

| OBS 进程 | WebSocket 状态 | 已保存密码 | 动作 |
|---|---|---|---|
| 未运行 | — | — | 若可定位 `basic.ini`：文件通道落地；否则跳过。→ 定位 `obs64.exe` → 拉起 → 等待端口 → 连接 → 开录 |
| 未运行 | — | — | 找不到 `obs64.exe`：阻断，提示手动选择/启动 OBS |
| 运行中 | 已连接 v5 | — | 通过 WebSocket 应用设置 → 开录 |
| 运行中 | 已连接 v4 | — | 跳过设置 → 直接开录；提示旧协议限制 |
| 运行中 | 未连接 | 有 | 自动连接 → 成功则应用设置/开录；失败按错误分类处理 |
| 运行中 | 未连接 | 无 | 若服务要求密码：提示输入；否则先尝试无密码连接 |
| 任意 | 正在连接/认证/重连 | — | 复用/等待现有连接；必要时取消重连后重新连接（单飞） |
| 任意 | 正在录制 | — | no-op，返回成功 |
| 任意 | 正在推流 | — | 阻断，不启动 OBS、不改配置 |
| 任意 | 取消 | — | 停止等待；已写配置保留可回滚记录；不产生录制 |

### 4.4 目标状态机

在现有 `SimpleRecordingStep` 上补充更细的阶段（保留旧成员，避免破坏现有绑定）：

```
Idle
  → Checking                只读检查
  → Blocked                 硬阻断（推流/找不到 OBS/用户关闭自动启动）
  → Preparing               备份 + 文件通道落地（仅 OBS 未运行且可写）
  → LaunchingObs            启动 obs64.exe
  → WaitingObs              等进程/端口就绪
  → ConnectingObs           主动 WebSocket 连接（认证/重试/超时）
  → ApplyingSettings        连接后走 WebSocket 落地（如前面没落地）
  → StartingRecord          发 StartRecord
  → Recording
  → Stopping → Stopped
```

状态文案要能回答三个问题：**现在在做什么、已经等了多久、失败后我能做什么**。

### 4.5 关键设计决策

#### D1：WebSocket 是默认控制通道，文件通道只用于“OBS 没运行时的预落地”

- OBS 没运行：先用文件通道写 `basic.ini`，这样 OBS 启动时直接带着正确设置，避免“启动后再改、要重启”；
- OBS 已运行：不再走文件通道（OBS 退出时会回写，改了也白改），一律走 WebSocket；
- 连接成功后不再重复应用同一批配置，避免二次写入与回滚表混乱；
- v4 没有 `SetProfileParameter`：跳过在线落地，直接开录（保持 V3.0.0 行为）。

#### D2：把“拉起 OBS 的同意”从每次弹窗改成“首次一次 + 设置页可改”

- 现状：`SimpleRecordCard` 构造时注入 `ConfirmLaunch`，每次点开始都弹确认；离开页面后回调被置空，
  服务层按“不允许”处理；
- 3.1.0：新增 `SimpleRecordLaunchConsent`（`ask` / `allow` / `deny`），默认 `ask`；
  - 首次遇到“需要拉起 OBS”时，显示一次说明（不是危险确认），用户可选“继续并记住”/“每次都问”/“我自己开”；
  - 选“继续并记住”后写入 `allow`，之后一键直达；
  - 选“我自己开”写入 `deny`，后续一键录制提示“自动启动已关闭”，给“去设置”出口；
- `SimpleRecordAutoLaunch`（现有，默认 `true`）继续作为总开关；
- 这一改动会改变 V2.9.4 “拉起 OBS 前必确认”的承诺，必须在发布说明中明确写出。

#### D3：新增 `ObsConnectionService.EnsureConnectedAsync()`

建议签名：

```csharp
public enum EnsureConnectionOutcome
{
    AlreadyConnected,
    Connected,
    ObsNotRunning,
    ObsExited,
    PortClosed,
    HandshakeFailed,
    AuthRequired,
    AuthFailed,
    Timeout,
    Cancelled,
    Error
}

public sealed record EnsureConnectionResult(
    EnsureConnectionOutcome Outcome,
    string? Message,
    int Attempts,
    TimeSpan Elapsed);

public Task<EnsureConnectionResult> EnsureConnectedAsync(
    TimeSpan timeout,
    string? password = null,
    CancellationToken ct = default);
```

实现要点：

- **单飞**：`SemaphoreSlim(1,1)` + 在途 `Task`；控制台页、一键录制、启动自动连接同时调用时不并发握手；
- **状态复用**：若已 `Connected`，直接返回；若正在 `Connecting/Authenticating`，等待在途任务；
- **重连处理**：若处于 `Reconnecting`，取消倒计时后重新连接（用户显式动作优先于后台重连）；
- **超时**：总时长由调用方给（一键录制传 75 秒），循环内每次尝试 1 次连接；间隔 1 秒；
- **认证失败不重试**：`UnauthorizedAccessException` → `AuthFailed`，避免反复弹错/锁账号；
- **不把 UI 塞进服务层**：需要密码时只返回 `AuthRequired`，由调用方提示并把密码作为下一次调用的
  `password` 参数传入；服务层不弹窗、不读写控件；
- **端口探测**：连接前可做一次 TCP 探测（1.5 秒）区分“端口没开”和“端口开了但不是 WebSocket”；
- **OBS 退出检测**：循环中若 `ObsPathService.IsObsRunning()` 变 false → `ObsExited`，不空等 75 秒；
- **取消**：`CancellationToken` 触发 → `Cancelled`，不写错误日志为 Error；
- **结果映射**：把底层异常翻译成用户能照做的文案，而不是直接抛 `WebSocketException`。

#### D4：`SimpleRecordingService.StartAsync()` 重排

目标顺序：

1. `_busy` 防重入；若正在录制 → no-op 成功；
2. 只读检查：推流 → 阻断；录制 → no-op；
3. 若 OBS 未运行：
   - 检查 `SimpleRecordAutoLaunch` / consent；
   - 定位 `obs64.exe`（找不到 → 阻断）；
   - 若可写 `basic.ini`：备份 → 文件通道落地 → 读回校验；
   - 拉起 OBS（单次，不重复启动）；
4. `EnsureConnectedAsync(75s)`：
   - 成功 → 继续；
   - `AuthRequired` → 请求密码（UI 线程）；
   - `AuthFailed` → 阻断，给“重新输入密码/检查 OBS 设置”；
   - `PortClosed` / `HandshakeFailed` → 阻断，给“开启 WebSocket / 检查端口 / 检查版本”；
   - `ObsExited` → 阻断，给“查看日志/重试”；
   - `Timeout` / `Cancelled` → 按对应文案；
5. 连接成功后：
   - 若前面没有文件通道落地 → `BuildPlanAsync()` 重新读计划（此时 `ViaWebSocket=true`）→ 应用；
   - 若前面已落地 → 跳过，避免重复写；
6. `StartRecordAsync()` → 等待 `RecordStatus.Active` 翻转（最多 10 秒）；
7. 进入 `Recording`，启动计时器，清 `PendingObsRestart`。

#### D5：就绪判定的语义要改

现状 `SimpleRecordingCore.Check()` 把“OBS 在跑但没连上”判为阻断。3.1.0 增加可选参数：

```csharp
public static SimpleReadyCheck Check(
    bool viaWebSocket,
    bool canWriteFiles,
    bool obsProcessRunning,
    bool streaming,
    bool recording,
    int failCount,
    int warnCount,
    string? blockedReason = null,
    string? noChannelReason = null,
    // 新增，默认 false，保持旧调用/旧测试行为不变
    bool canAutoConnect = false,
    bool canAutoLaunch = false,
    bool launchAllowed = true);
```

新逻辑：

- 推流 / 录制 → 阻断（最高优先级，保持）；
- 已连接 v5 → 就绪；
- OBS 在跑、未连接、`canAutoConnect` → **就绪（提示“将自动连接”）**；
- OBS 没跑、`canAutoLaunch && launchAllowed` → **就绪（提示“将自动启动并连接”）**；
- 否则按原逻辑阻断；
- 预检 Fail 仍然阻断；Warn 只提示。

`SimpleReadyCheck` 增加 `WillLaunchObs` / `WillConnectObs` 两个只读标志，供 UI 显示“将自动启动/连接”。

#### D6：默认连接设置与迁移

- `ObsConnectionSettings.AutoConnect` 默认值改为 `true`（**只影响新安装/未写过该键的配置**）；
- 已有 `prefs.json` 明确写了 `false` 的用户保持 `false`，不静默改用户选择；
- 一键录制**不受** `AutoConnect` 限制：它是用户显式动作，永远尝试连接；
- `AutoReconnect` 保持 `true`；
- `AutoConnect` 的启动尝试改为“仅当 OBS 进程在运行”：
  - 避免 OBS 没开时启动就进入重连/失败状态；
  - 失败只记日志，不弹窗，不阻塞首屏。

#### D7：密码与安全

- 继续使用 `ObsSettingsService` 的 DPAPI 加密存储；
- 连接时优先取已保存密码；没有则尝试空密码；服务要求密码时：
  - 在首页卡片/设置页给内联密码输入（不是系统弹窗），支持“记住密码”；
  - 密码只进 `secrets.dat`，不进日志、不进诊断报告；
- **不自动关闭鉴权**；不自动重置密码；
- 若用户忘记密码：给“在 OBS → 工具 → WebSocket 服务器设置中查看/修改”的指引，并提供“打开 OBS 设置”说明。

#### D8：obs-websocket 服务配置检测（P1）

这是“一键”能否真正成功的关键外部条件。建议新增：

- `Services/Obs/ObsWebSocketConfigCore.cs`（纯逻辑）：
  - 识别 config JSON 的关键字段：`server_enabled` / `server_port` / `auth_required`；
  - 只做“已知 schema”的读写，不创建未知结构；
  - 保留所有未知字段，避免 OBS 升级后配置被破坏；
- `Services/Obs/ObsWebSocketConfigService.cs`：
  - 定位：`ObsPathService.LocateAsync().ConfigDir` + `plugin_config\obs-websocket\config.json`；
  - OBS 运行时：只读，返回“需要先关闭 OBS”；
  - OBS 未运行且 schema 已知：备份 → 写 `server_enabled=true`（可选 `server_port`）→ 读回校验；
  - **绝不改** `server_password` / `auth_password` / `auth_required`；
  - 失败可回滚；任何不确定的 schema 一律不动，转为指引。

UX：

- 一键录制失败为 `PortClosed` 且 OBS 未运行：
  - 显示“OBS 的 WebSocket 服务没有开启。要由本工具开启并继续吗？”；
  - 说明会修改哪个文件、会先备份、不会关闭密码；
  - 同意 → 备份/写入/读回 → 继续启动/连接/开录；
  - 拒绝 → 零修改，给手动步骤。
- OBS 正在运行且服务关闭：
  - 不能在线改（OBS 会回写），显示步骤：“关闭 OBS → 由本工具开启 → 重新一键录制”；
  - 提供“复制步骤”或“打开配置目录”（P1）。

### 4.6 按文件的改动清单

| 文件 | 改动 | 风险 |
|---|---|---|
| `Services/Obs/ObsConnectionService.cs` | 新增 `EnsureConnectedAsync()`、单飞、结果类型、取消重连、TCP 探测 | 中：连接状态机是核心，必须补并发测试 |
| `Services/Obs/ObsSettingsService.cs` | `AutoConnect` 默认值调整；保持旧配置不覆盖 | 低 |
| `Services/Shell/SimpleRecordingService.cs` | 重排 `StartAsync`；新增 `ConnectingObs`/`ApplyingSettings`/`StartingRecord`；移除对 `ConfirmLaunch` 的硬依赖；补取消 | 中高：主流程改动 |
| `Services/ObsConfig/SimpleRecordingCore.cs` | `Check()` 增加自动连接/自动启动参数；新增结果标志；补单测 | 中：保持旧签名兼容 |
| `Services/ObsConfig/RecordingEnvService.cs` | 连接后重新建计划；必要时区分“连接前文件计划/连接后 WS 计划” | 中 |
| `Controls/SimpleRecordCard.xaml(.cs)` | 去掉每次确认；首次同意面板；分阶段进度；取消/重试/打开 OBS 按钮 | 中 |
| `Views/SettingsPage.xaml(.cs)` | 自动启动开关、首次同意状态、连接设置、WebSocket 修复入口 | 低 |
| `Views/ConsolePage.xaml.cs` | 复用 `EnsureConnectedAsync()`，避免与一键录制并发连接 | 低 |
| `AppServices.cs` | 注册新服务（如 `ObsWebSocketConfigService`） | 低 |
| `Services/Shell/GlobalHotkeyService.cs` / `TrayService.cs` | P1：统一到一键录制链路；P0 至少保持现状不回归 | 低 |
| `Localization/StringTable*.cs` | 新增状态/错误/设置/壁纸文案 | 低但量大 |
| `OBS_Helper.Wpf.Tests/*` | 新增连接决策、状态机、壁纸纯逻辑测试 | 低 |
| `docs/ARCHITECTURE.md` / `CODEBASE.md` / `README.md` / `RELEASE_NOTES_v3.1.0.md` | 同步文档与版本号 | 低 |

### 4.7 错误分类与用户出口

| 分类 | 触发 | 用户看到 | 出口 |
|---|---|---|---|
| `ObsNotFound` | 找不到 `obs64.exe` | “没找到 OBS 主程序，本工具不会猜路径” | 手动启动 OBS / 在设置中指定目录 / 官方下载 |
| `LaunchFailed` | `Process.Start` 异常 | “启动 OBS 失败：{原因}” | 手动启动 / 查看日志 / 重试 |
| `ObsExited` | 启动后进程消失 | “OBS 启动后又退出了（可能被杀软拦截或首次向导异常）” | 手动启动 / 查看 OBS 日志 / 重试 |
| `PortClosed` | TCP 连接被拒 | “OBS 的 WebSocket 端口没有响应（未开启服务或端口不一致）” | 开启 WebSocket（P1 一键）/ 改端口 / 关闭 OBS 后重试 |
| `HandshakeFailed` | 端口通但握手失败 | “端口通了，但不是可用的 obs-websocket（可能是旧版/其他程序占用）” | 检查 OBS 版本与插件 / 改端口 |
| `AuthRequired` | 服务要求密码且未提供 | “OBS 要求 WebSocket 密码” | 输入密码 / 记住密码 |
| `AuthFailed` | 密码错误 | “密码不正确” | 重新输入 / 清除已保存密码 / 在 OBS 中核对 |
| `Timeout` | 75 秒内未连上 | “已等待 75 秒仍未连接” | 查看 OBS 是否弹窗 / 重试 / 手动连接 |
| `Cancelled` | 用户取消 | “已取消” | 可重新开始 |
| `StartRecordFailed` | `StartRecord` 失败/状态未翻转 | “OBS 没有进入录制状态” | 检查编码器/输出/磁盘 / 重试 |
| `BlockedStreaming` | 正在推流 | “正在推流，不能抢占录制输出” | 停止推流后再试 |
| `NoProfile` | 找不到 `basic.ini` 且 OBS 未运行 | “找不到 OBS 配置集” | 先启动一次 OBS / 手动指定配置目录 |

### 4.8 并发与竞态

- 用户连点“开始”：`_busy` + `EnsureConnectedAsync` 单飞，只允许一个流程；
- 控制台页同时点“连接”：复用同一个在途连接任务，不重复握手；
- 用户在等待中手动启动 OBS：`IsObsRunning` 变 true，流程直接进入连接，不再第二次启动；
- 用户在等待中手动开始录制：`RecordStatus.Active` 变 true，流程 no-op 成功并进入 `Recording`；
- 用户在等待中取消：`CancellationToken` 停止轮询；不调用 `StartRecord`；已写配置保留回滚记录；
- OBS 在连接成功后立刻退出：`StartRecord` 失败 → 分类错误，不静默；
- 应用退出时：取消在途连接/启动任务，不留下半连接状态。

### 4.9 测试矩阵

**单元测试（纯逻辑）**

- `SimpleRecordingCore.Check()` 新旧参数组合：已连接/未连接/未运行/可自动连接/可自动启动/推流/录制/预检失败；
- `EnsureConnectionResult` 的异常映射（超时/拒绝/认证/握手/取消）；
- 重试次数与时间上限（用假时钟/假连接器）；
- 状态机阶段顺序与取消；
- 首次同意状态机：`ask → allow / deny`，重启后保持；
- v4 分支：不调用 `SetProfileParameter`；
- 配置检测纯逻辑：已知/未知 schema、字段保留、`server_enabled` 写入。

**集成 / 手工**

| 场景 | 预期 |
|---|---|
| OBS 28+（31.x / 32.x），WebSocket 已启用，密码已保存，OBS 未启动 | 一次点击 → 启动 → 连接 → 开录 ≤ 30 秒 |
| OBS 已启动但未连接 | 一次点击 → 自动连接 → 开录，不重启 OBS |
| OBS 已连接 v5 | 一次点击 → 应用设置 → 开录 |
| OBS 27 + obs-websocket 4.9（Win7 构建） | 一次点击 → 连接 4444 → 直接开录 |
| WebSocket 服务关闭，OBS 未启动 | 首次提示；同意后备份/启用/启动/连接/开录；拒绝则零修改 |
| WebSocket 服务关闭，OBS 正在运行 | 明确指引，不修改配置 |
| 密码错误 | 明确错误 + 输入入口；不无限重试 |
| 端口被改成非默认 | 明确 `PortClosed`；改端口后可成功 |
| OBS 首次启动向导 | 等待中提示“请完成 OBS 窗口中的设置”；完成后继续 |
| OBS 启动后崩溃 | `ObsExited`，不空等 75 秒 |
| 正在推流 | 阻断，不启动 OBS、不改配置 |
| 已经录制 | no-op，不重复开录 |
| 用户取消 | 停止等待，不产生录制文件 |
| 便携版 OBS | 能定位 exe/配置目录；行为与安装版一致 |
| 杀软拦截启动 | `LaunchFailed`，给手动启动/日志出口 |

**回归**

- 现有 `SimpleRecordingCoreTests` / `ObsLaunchCoreTests` / `ObsReconnectPolicyTests` 全绿；
- 控制台页连接/断开/重连不回归；
- 托盘录制菜单、全局热键不回归；
- 双构建（`net10.0-windows` / `net6.0-windows`）编译与 Headless 自检；
- `scripts/check_accessibility.py` 通过；
- `scripts/check_docs.py` 通过（版本号、服务数、文案键等）。

### 4.10 验收标准

- [ ] OBS 未启动、WebSocket 已启用、密码已保存：单击“开始简单录像”一次，≤ 30 秒进入录制，无额外弹窗；
- [ ] OBS 已启动但未连接：单击一次，自动连接并开录，不重启 OBS；
- [ ] OBS 已连接 v5：单击一次，应用设置并开录；
- [ ] OBS 27/v4：单击一次，直接开录，不调用 `SetProfileParameter`；
- [ ] WebSocket 服务关闭且 OBS 未运行：首次提示，同意后备份/启用/启动/连接/开录；拒绝时零修改；
- [ ] 密码错误：明确分类 + 可输入/清除密码，不无限重试；
- [ ] 正在推流：阻断，不启动 OBS、不改配置；
- [ ] 取消：停止等待，不产生录制文件，已写配置可回滚；
- [ ] 启动失败/OBS 退出/超时：都有分类错误 + 至少一个可执行出口；
- [ ] 首次同意状态重启后保持；设置页可改回“每次都问”；
- [ ] 托盘/热键/控制台页无回归；
- [ ] 双构建、单测、自检、无障碍静态检查全部通过。

---

## 5. 实施计划

> 本节只覆盖第一轮两项功能（壁纸 + 一键录制）。第二轮追加功能（提词器 / AI 自动直播 / 彩蛋 / UI 视觉刷新 / 补充有趣功能）
> 的评估与合并排期见 **§12**；两者冲突时，以 **§12.6 / §12.7** 为准。

### 5.1 工作分解

> 估算按 1 名熟悉该代码库的开发者计；实际排期可按人力并行调整。

| ID | 工作项 | 产出 | 估算（人日） | 依赖 |
|---|---|---|---|---|
| P0-01 | 设计冻结：数据模型、状态机、错误矩阵、文案键 | 本计划 + 评审记录 | 1 | — |
| P0-02 | `WallpaperCore` / `WallpaperStore` + 单测 | 纯逻辑与文件库 | 3 | P0-01 |
| P0-03 | `WallpaperService`：解码/缓存/轮播/主题联动 | 服务层 | 3 | P0-02 |
| P0-04 | `WallpaperLayer` + `MainWindow` 接入 | 渲染层 | 2 | P0-03 |
| P0-05 | 设置页壁纸库/导入/拖拽/滑杆/轮播 UI | 设置页 | 3 | P0-03 |
| P0-06 | 旧设置迁移 + 容错提示 | 迁移逻辑 | 1 | P0-02 |
| P0-07 | 壁纸单测 + 中英文案 + 无障碍 | 测试/文案 | 2 | P0-04/05 |
| P0-08 | `EnsureConnectedAsync` + 结果类型 + 单飞/超时 | 连接层 | 3 | P0-01 |
| P0-09 | `SimpleRecordingService.StartAsync` 重排 + 新状态 | 一键流程 | 3 | P0-08 |
| P0-10 | `SimpleRecordingCore.Check` 扩展 + 单测 | 就绪判定 | 1 | P0-09 |
| P0-11 | 首页卡片：首次同意、进度、取消、错误出口 | UI | 2 | P0-09 |
| P0-12 | 设置页连接/自动启动/首次同意状态 | UI | 1 | P0-09 |
| P0-13 | 迁移/并发/错误矩阵测试 | 测试 | 2 | P0-08~12 |
| P0-14 | 双构建回归、Headless 自检、无障碍/文档检查 | 回归 | 2 | 全部 |
| P0-15 | 发布说明、README、版本号、安装包 | 发布 | 2 | P0-14 |
| **合计（P0）** | | | **约 31 人日** | |
| P1-01 | 模糊 + 缓存 | 壁纸增强 | 2 | P0-04 |
| P1-02 | 浅色/深色各一张 | 壁纸增强 | 1 | P0-03 |
| P1-03 | 平铺 + 整窗壁纸 | 壁纸增强 | 3 | P0-04 |
| P1-04 | WebSocket 配置检测/一键启用 | 连接增强 | 3 | P0-08 |
| P1-05 | 启动时自动连接 + 端口探测 | 连接增强 | 2 | P0-08 |
| P1-06 | 托盘/热键统一 | 入口统一 | 2 | P0-09 |
| **合计（P1）** | | | **约 13 人日** | |

### 5.2 建议排期（7 周，含缓冲）

> P0 合计约 31 人日。按 1 名开发者、每周 5 个工作日计，约 6.2 周；下表按 **7 周（含约 1 周缓冲）** 排。
> 若有 2 名开发者并行，可压缩到约 4 周；P1 的 13 人日建议整体推迟到 3.1.x。

| 周 | 日期 | 里程碑 | 内容 |
|---|---|---|---|
| W1 | 2026-10-19 ~ 10-23 | **M1 设计冻结** | P0-01/02/08；数据模型、状态机、错误矩阵、单测骨架 |
| W2 | 2026-10-26 ~ 10-30 | **M2 壁纸核心** | P0-02/03/06；壁纸库、服务层、迁移 |
| W3 | 2026-11-02 ~ 11-06 | **M3 壁纸可交付** | P0-04/05/07；渲染层、设置页、文案/无障碍 |
| W4 | 2026-11-09 ~ 11-13 | **M4 一键录制可交付** | P0-09~12；流程重排、首页/设置页、v4 分支 |
| W5 | 2026-11-16 ~ 11-20 | **M5 集成** | P0-13；连接/并发/错误矩阵测试，双构建联调 |
| W6 | 2026-11-23 ~ 11-27 | **回归** | P0-14；真机矩阵、无障碍、文档一致性 |
| W7 | 2026-11-30 ~ 12-04 | **发布缓冲** | P0-15；修回归、发布说明、打包、最终验收 |

> 如果排期必须压缩，优先把「轮播」或「壁纸库缩略图」降到 3.1.1，但**一键录制的 P0 不应降级**；
> 不要把 P1 硬塞进 3.1.0，否则会同时牺牲真机验证与发布质量。

### 5.3 依赖顺序

```
P0-01 设计冻结
  ├─ P0-02 WallpaperCore/Store ─ P0-03 WallpaperService ─ P0-04 WallpaperLayer ─ P0-05 设置页
  │                                                        └─ P0-06 迁移 ─ P0-07 测试/文案
  └─ P0-08 EnsureConnected ─ P0-09 StartAsync 重排 ─ P0-10 Check 扩展
                                                   ├─ P0-11 首页卡片
                                                   └─ P0-12 设置页
                                                          └─ P0-13 测试 ─ P0-14 回归 ─ P0-15 发布
```

### 5.4 发布门禁

- [ ] `dotnet test OBS_Helper.Wpf.Tests` 全绿（记录真实数字）；
- [ ] 主构建与 Win7 构建各跑一次 Headless 自检；
- [ ] `scripts/check_accessibility.py` 通过；
- [ ] `scripts/check_docs.py` 通过（版本号 / 服务数 / 文案键 / 测试数）；
- [ ] 手工矩阵：OBS 28+（31.x / 32.x）、OBS 27/v4、便携版、服务关闭、密码错误、首次向导、推流中、取消；
- [ ] 壁纸矩阵：导入/切换/删除/轮播/高对比/减少动画/DPI/4K/文件丢失；
- [ ] 无 P0/P1 未修复缺陷；
- [ ] 发布说明如实列出未验证项与已知边界。

---

## 6. 风险与对策

| # | 风险 | 概率 | 影响 | 对策 |
|---|---|---|---|---|
| R1 | obs-websocket 服务被用户关闭 / 端口被改 / 密码未知 | 高 | 高 | 分类错误 + 指引；P1 提供“经同意后一键启用”；不假设默认一定可用 |
| R2 | OBS 首次启动向导/崩溃恢复弹窗阻塞连接 | 中 | 高 | 等待中提示“请完成 OBS 窗口中的设置”；提供“切换到 OBS”；不自动点弹窗 |
| R3 | OBS 冷启动慢于 75 秒 | 低 | 中 | 保留 75 秒；显示已等待时长；允许取消/重试；日志记录各阶段耗时 |
| R4 | 自动拉起 OBS 被用户视为“擅自启动” | 中 | 中 | 首次一次明确同意；设置页可关闭；发布说明写清楚 |
| R5 | 旧协议 v4 被“统一 WebSocket”改造破坏 | 中 | 高 | 保留 `legacyLive` 分支；v4 单测；双构建回归 |
| R6 | 连接状态机并发（控制台页 + 一键录制 + 自动连接） | 中 | 高 | `EnsureConnectedAsync` 单飞；取消重连；并发单测 |
| R7 | 自动连接在 OBS 未启动时制造重连噪音 | 中 | 中 | 仅当 `IsObsRunning` 时启动尝试；失败只记日志 |
| R8 | 壁纸大图导致内存/卡顿 | 中 | 中 | 解码上限 3840 px；缓存 2 张；后台解码；性能验收 |
| R9 | 壁纸影响文字可读性 | 高 | 中 | 默认压暗；高对比强制关闭；低于 0.10 提示 |
| R10 | manifest/文件损坏导致壁纸库丢失 | 低 | 中 | 扫描重建；不阻塞启动；日志；原文件不删 |
| R11 | 轮播动画干扰“减少动画”用户 | 中 | 中 | 与 `ReduceMotion` 联动；非激活暂停 |
| R12 | 整窗壁纸破坏侧栏/顶栏对比度 | 中 | 中 | 放到 P1；需要半透明表面 + 对比度复核 |
| R13 | 范围膨胀导致 3.1.0 延期 | 高 | 高 | P0/P1 硬分层；P1 可整体推迟到 3.1.x；一键录制 P0 不降级 |
| R14 | 文档测试数口径继续漂移 | 中 | 低 | 开工先跑真实基线；发布时统一回填 README/发布说明/check_docs |

---

## 7. 完成定义（Definition of Done）

### 7.1 代码

- [ ] 新功能有纯逻辑核心 + 单测；WPF 侧只做编排/渲染；
- [ ] 不新增第三方 NuGet 包；
- [ ] 不使用 .NET 7+ 独有 API（Win7 构建可编译）；
- [ ] 所有写 OBS 配置的路径有备份/读回/回滚；
- [ ] 所有用户可见错误有分类文案与出口；
- [ ] 日志脱敏覆盖新字段（密码、路径中的用户名等）。

### 7.2 测试

- [ ] 新增单测覆盖：壁纸校验/迁移/轮播顺序、连接决策/错误映射/状态机/首次同意；
- [ ] 现有单测全绿；
- [ ] 双构建 Headless 自检通过；
- [ ] 手工矩阵完成并记录结果；
- [ ] 无障碍静态检查通过；
- [ ] 文档一致性检查通过。

### 7.3 文档与发布

- [ ] `docs/ARCHITECTURE.md` / `docs/CODEBASE.md` 更新新服务与控件；
- [ ] `README.md` 增加 V3.1.0 摘要与质量基线；
- [ ] `RELEASE_NOTES_v3.1.0.md` 写清：新功能、行为变更（首次同意替代每次确认）、已知边界；
- [ ] `OBS_Helper.Wpf.csproj` 的 `Version/FileVersion/AssemblyVersion` 与 `OBS_Helper_Setup.iss` 的 `MyAppVersion` 对齐到 `3.1.0`；
- [ ] 安装包/便携包/增量包命名与标签对齐；
- [ ] 发布说明明确“未在真实 OBS 上验证”的项（如有）。

---

## 8. 待确认的产品决策

以下决策会改变用户体验或安全边界，建议在开工前拍板。括号内为推荐选项。

1. **一键录制是否彻底去掉每次确认？**
   - 推荐：首次一次说明 + 记住选择；设置页可改回“每次都问”。
   - 理由：保留“不静默拉起”的底线，同时满足“一键”目标。

2. **是否允许应用在 OBS 未运行时自动启用 obs-websocket 服务？**
   - 推荐：允许，但必须显式同意、先备份、只写 `server_enabled`/可选端口，绝不关闭鉴权/改密码。
   - 理由：这是提高一键成功率的关键；但属于修改 OBS 配置，必须让用户知情。

3. **壁纸作用范围：只做内容区，还是整窗？**
   - 推荐：3.1.0 只做内容区；整窗放 P1。
   - 理由：整窗要改侧栏/顶栏表面与文字对比，回归面大。

4. **壁纸轮播是否进 3.1.0？**
   - 推荐：进 P0（它是“个性化”最直观的价值），但允许在排期紧张时降到 3.1.1。
   - 理由：单张图片已存在，轮播才是本版的主要增量。

5. **启动时自动连接是否默认开启？**
   - 推荐：新安装默认开启，但只在 OBS 已运行时尝试；老用户保留原设置。
   - 理由：避免 OBS 没开时启动就进入重连/失败状态。

6. **托盘/全局热键是否也允许自动拉起 OBS？**
   - 推荐：P1，默认关闭；先保证首页“一键录制”链路稳定。
   - 理由：热键是“盲操作”，自动拉起 OBS 的副作用更大。

---

## 9. 代码落点索引（便于开工）

| 主题 | 文件 | 关键符号 |
|---|---|---|
| 外观设置 | `OBS_Helper.Wpf/Services/AppearanceService.cs` | `AppearanceSettings`、`ApplyBackgroundResources()`、`LoadBackgroundImage()` |
| 主窗口背景层 | `OBS_Helper.Wpf/MainWindow.xaml` | `ContentBackgroundBrush` / `ContentBackgroundImage` / `ContentBackgroundDim` |
| 设置页背景 | `OBS_Helper.Wpf/Views/SettingsPage.xaml(.cs)` | `BgDefault/BgColor/BgImage`、`OnPickBgImage`、`SyncBgImageState` |
| 简单录像服务 | `OBS_Helper.Wpf/Services/Shell/SimpleRecordingService.cs` | `StartAsync()`、`LaunchAndWaitAsync()`、`StartRecordAsync()`、`ObsReadyTimeoutSeconds` |
| 简单录像纯逻辑 | `OBS_Helper.Wpf/Services/ObsConfig/SimpleRecordingCore.cs` | `SimpleRecordingStep`、`SimpleReadyCheck`、`Check()` |
| 简单录像卡片 | `OBS_Helper.Wpf/Controls/SimpleRecordCard.xaml(.cs)` | `ConfirmLaunch`、`OnStartClick`、`RenderButtons` |
| OBS 连接 | `OBS_Helper.Wpf/Services/Obs/ObsConnectionService.cs` | `ConnectAsync()`、`ScheduleReconnect()`、`ObsConnectionState` |
| 连接设置 | `OBS_Helper.Wpf/Services/Obs/ObsSettingsService.cs` | `AutoConnect`、`AutoReconnect`、`GetPasswordAsync()` |
| 控制台自动连接 | `OBS_Helper.Wpf/Views/ConsolePage.xaml.cs` | `_autoConnectTried`、`OnNavigatedToAsync()` |
| OBS 路径 | `OBS_Helper.Wpf/Services/ObsConfig/ObsPathService.cs` | `LocateAsync()`、`FindObsExecutable()`、`IsObsRunning()` |
| 拉起纯逻辑 | `OBS_Helper.Wpf/Services/ObsConfig/ObsLaunchCore.cs` | `ExeFromDisplayIcon()`、`ExeFromInstallDir()`、`ResolveCandidates()` |
| 自动拉起设置 | `OBS_Helper.Wpf/Models/Shell/ShellSettings.cs` | `SimpleRecordAutoLaunch = true` |
| 热键 | `OBS_Helper.Wpf/Services/Shell/GlobalHotkeyService.cs` | `HotkeyAction.Record` → `ToggleRecordAsync()` |
| 托盘 | `OBS_Helper.Wpf/Services/Shell/TrayService.cs` | `_recordItem.Enabled = _obs.IsConnected` |
| 相关单测 | `OBS_Helper.Wpf.Tests/SimpleRecordingCoreTests.cs`、`ObsLaunchCoreTests.cs`、`ObsReconnectPolicyTests.cs` | 现有回归基线 |

---

## 10. 附：本版新增文案键（建议）

> 以下为建议键名，最终以 `StringTableZhHans` / `StringTableEnUs` 的键集单测为准。

**壁纸**

- `settings.bg.library.title`
- `settings.bg.library.empty`
- `settings.bg.import`
- `settings.bg.import.dragHint`
- `settings.bg.import.tooLarge`
- `settings.bg.import.unsupported`
- `settings.bg.import.corrupt`
- `settings.bg.import.failed`
- `settings.bg.delete`
- `settings.bg.delete.confirm`
- `settings.bg.fit`
- `settings.bg.fit.fill`
- `settings.bg.fit.contain`
- `settings.bg.fit.center`
- `settings.bg.dim`
- `settings.bg.dim.lowWarning`
- `settings.bg.slideshow`
- `settings.bg.slideshow.interval`
- `settings.bg.slideshow.shuffle`
- `settings.bg.slideshow.paused`
- `settings.bg.restoreDefault`
- `settings.bg.migrated`
- `settings.bg.missing`

**一键录制 / 连接**

- `simple.launch.consent.title`
- `simple.launch.consent.message`
- `simple.launch.consent.allow`
- `simple.launch.consent.ask`
- `simple.launch.consent.deny`
- `simple.state.connecting`
- `simple.state.connectingElapsed`
- `simple.state.applying`
- `simple.state.startingRecord`
- `simple.state.cancelled`
- `simple.ready.willLaunch`
- `simple.ready.willConnect`
- `simple.error.obsNotFound`
- `simple.error.portClosed`
- `simple.error.handshakeFailed`
- `simple.error.authRequired`
- `simple.error.authFailed`
- `simple.error.timeout`
- `simple.error.cancelled`
- `simple.error.obsExited`
- `simple.error.startRecordFailed`
- `simple.action.retry`
- `simple.action.openObs`
- `simple.action.openSettings`
- `simple.action.enableWebSocket`
- `simple.websocket.enable.title`
- `simple.websocket.enable.message`
- `simple.websocket.enable.done`
- `simple.websocket.enable.requiresClosed`
- `simple.websocket.enable.unknownSchema`

---

## 11. 附：建议的验收记录模板

| 日期 | 构建 | OBS 版本 | 协议 | 场景 | 结果 | 耗时 | 日志/截图 |
|---|---|---|---|---|---|---|---|
| 2026-11-xx | net10 | 31.x | v5 | OBS 未启动，WebSocket 已开，密码已存 | 通过/失败 |  |  |
| 2026-11-xx | net10 | 31.x | v5 | OBS 已启动未连接 |  |  |  |
| 2026-11-xx | net10 | 31.x | v5 | WebSocket 服务关闭 |  |  |  |
| 2026-11-xx | net10 | 31.x | v5 | 密码错误 |  |  |  |
| 2026-11-xx | net6 | 27.2 | v4 | OBS 未启动 |  |  |  |
| 2026-11-xx | net6 | 27.2 | v4 | OBS 已启动未连接 |  |  |  |
| 2026-11-xx | net10 | 31.x | v5 | 推流中 |  |  |  |
| 2026-11-xx | net10 | 31.x | v5 | 取消等待 |  |  |  |
| 2026-11-xx | net10 | 31.x | v5 | 壁纸 4K 轮播 1 小时 |  |  |  |
| 2026-11-xx | net10 | 31.x | v5 | 高对比 + 壁纸 |  |  |  |

---

---

## 12. 第二轮追加功能评估与规划（2026-10-18）

> 本节回应新增需求：提词器 / AI 自动直播 / 彩蛋 / UI 视觉刷新 / 补充有趣功能。
> 结论先行：**提词器、彩蛋、UI 视觉刷新可实现；AI 自动直播只能做“用户自带 API + 规则/脚本桥”的实验性 MVP；
> 完整自动直播（读弹幕、TTS、平台互动）不属于 3.1.0。**

### 12.1 提词器

#### 12.1.1 可行性结论

**可实现，建议作为 3.1.0 的 P0。** 但“不被录进去”必须分采集方式说清楚：

| 录制方式 | 是否包含提词器 | 条件 | 建议 |
|---|---|---|---|
| OBS **窗口采集**（指定某个窗口） | **不包含** | 提词器是独立顶层窗口，不属于被采集窗口 | **最稳，默认推荐** |
| OBS **游戏采集**（指定游戏） | **不包含** | 采集的是游戏交换链 | 推荐 |
| OBS **显示器采集** | 默认包含；可尝试排除 | 需要 Windows 10 2004+ / Windows 11；`WDA_EXCLUDEFROMCAPTURE`；OBS 采集后端必须配合；可能留下黑块 | 真机验证后使用；失败改用窗口采集/第二屏 |
| OBS **显示器采集 + Win7** | **不保证** | `WDA_EXCLUDEFROMCAPTURE` 不可用；`WDA_MONITOR` 会让窗口在录制里变黑 | 不建议；用窗口采集/第二屏 |
| 第二屏幕 | **不包含**（若 OBS 只采集主屏） | 需要两块屏 | 最稳的物理隔离 |

关键结论：

- **窗口采集/游戏采集是“天然排除”**：提词器不是被采集窗口的一部分，所以不会被录进去；
- **显示器采集不是“天然排除”**：需要 `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`；
- `WDA_EXCLUDEFROMCAPTURE` 的行为是“从捕获中排除”，不是“在捕获里挖一个透明洞”——
  有些采集后端会留下黑块；
- 不同 OBS 版本/采集后端（DXGI、Windows Graphics Capture、Game Capture）表现可能不同，**必须真机验证**；
- 产品文案只能写“在支持的采集方式下可排除”，不能写“绝对不会被录到”。

#### 12.1.2 功能设计

**P0（3.1.0）**

- 独立提词器窗口：置顶、无边框、可拖动、可缩放、可隐藏；
- 滚动：自动滚动 / 手动滚轮 / 空格暂停 / 方向键微调 / Home 回到开头；
- 速度：10–200 px/s；暂停/继续；3-2-1 倒计时；
- 排版：字体、字号、行高、字色、背景不透明度、左右边距、阅读参考线；
- 镜像：水平/垂直（供提词器玻璃/半反半透镜使用）；
- 点击穿透：开启后鼠标事件穿透到下层应用，提词器仍可用全局热键控制；
- 多屏：选择显示器，记住位置/尺寸/字号/速度/透明度/镜像/穿透状态；
- 内容：纯文本编辑、粘贴、导入 `.txt` / `.md`、自动保存草稿；不联网、不上传；
- 热键：呼出/隐藏、播放/暂停、点击穿透、速度 +/-；
- 托盘菜单与首页入口；
- 录屏兼容性向导：窗口采集 / 显示器采集 / 第二屏三种建议 + 自检步骤；
- 捕获排除开关：默认在支持的 Windows 上开启；不支持时显示原因与替代方案。

**P1**

- 多份脚本、脚本列表、最近使用；
- 场景/段落标记、进度条、预计剩余时间；
- 与场景模板联动：切到某个场景自动跳到对应脚本段落；
- 提词器字号/速度的快捷预设。

**P2**

- 语音跟随滚动（需要语音识别，延迟/隐私/性能都要评估）；
- AI 根据主题生成提词稿（复用 §12.2 的 AI 配置）；
- 手机遥控（需要局域网服务，安全面大）。

#### 12.1.3 技术设计

新增：

| 文件 | 职责 |
|---|---|
| `Views/TeleprompterWindow.xaml(.cs)` | 提词器显示窗口：滚动、排版、镜像、穿透、倒计时 |
| `Services/Shell/TeleprompterService.cs` | 窗口生命周期、单实例、位置/设置持久化、热键入口 |
| `Services/Shell/TeleprompterCore.cs` | 纯逻辑：速度/字号/透明度 clamp、滚动步进、镜像、设置归一化 |
| `Models/Shell/TeleprompterSettings.cs` | 持久化模型（`prefs.json`，不含敏感内容） |
| `Services/Compat/WindowCaptureExclusion.cs` | `SetWindowDisplayAffinity` P/Invoke、OS 版本判断、结果枚举 |
| `Views/TeleprompterPage.xaml(.cs)`（可选） | 脚本编辑与设置页；也可先做在窗口内的编辑模式 |

窗口实现要点：

- `WindowStyle=None`、`AllowsTransparency=True`、`Topmost=True`、`ShowInTaskbar=False`、
  `ShowActivated=False`；
- `WS_EX_TOOLWINDOW`：不出现在 Alt+Tab；
- 点击穿透：`WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE`，通过全局热键切换；
- 捕获排除：`SetWindowDisplayAffinity(hwnd, 0x11)`；在 `SourceInitialized` / `Loaded` 后调用；
  句柄重建时重新应用；失败时回退并显示说明；
- OS 判断：`OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)`；Win7 构建走“不可用”分支；
- 滚动：`ScrollViewer` + `DispatcherTimer`（16ms）或 `CompositionTarget.Rendering`；
  窗口隐藏/最小化时暂停；长文本用分段 `ItemsControl`/虚拟化，避免一次性布局巨量文本；
- 内容保存：`%LocalAppData%\OBS_Helper\teleprompter\current.txt`（Win7 构建用 `_Win7` 目录），
  原子写；不记录到日志；
- 热键：在 `HotkeyAction` 增加 `TeleprompterToggle` / `TeleprompterPlayPause` /
  `TeleprompterClickThrough`，`HotkeySettings` 增加对应键位与开关；
  默认键位要避开 OBS 自己的热键与系统保留键。

#### 12.1.4 测试矩阵

| 场景 | 预期 |
|---|---|
| OBS 窗口采集（采集浏览器/播放器） | 提词器不出现在录制里 |
| OBS 游戏采集 | 提词器不出现在录制里 |
| OBS 显示器采集，Win11 + OBS 31/32 | 开启排除后不出现；若出现黑块，记录为已知限制并提示改用窗口采集 |
| OBS 显示器采集，Win10 2004+ | 同上；低于 2004 不支持 |
| Win7 兼容构建 | 不启用 `WDA_EXCLUDEFROMCAPTURE`；提示窗口采集/第二屏 |
| 双屏（提词器在副屏，OBS 采集主屏） | 不出现 |
| 125% / 150% / 200% DPI | 字体/滚动/位置正常 |
| 长脚本（10 万字） | 打开/滚动不卡死；内存可接受 |
| 点击穿透 | 鼠标操作下层应用；热键仍可暂停/退出 |
| 录制中打开/关闭提词器 | 不影响录制、不丢帧、不弹出抢焦点 |

#### 12.1.5 验收标准

- [ ] 提词器可独立打开、置顶、拖动、缩放、记忆位置；
- [ ] 自动滚动/暂停/速度/字号/行高/透明度/镜像/阅读线可用；
- [ ] 点击穿透可用，且全局热键仍能控制；
- [ ] 窗口采集/游戏采集下不被录进去；
- [ ] 显示器采集在支持的 Windows 上可排除；不支持时有明确说明与替代方案；
- [ ] Win7 构建不崩溃，给出降级建议；
- [ ] 录制中打开/关闭提词器不影响录制；
- [ ] 中英文案、键盘操作、无障碍名称齐全。

### 12.2 AI 自动直播（用户自带 API）

#### 12.2.1 可行性结论

**分层看，不要一句话说“能做/不能做”。**

| 能力层 | 3.1.0 可行性 | 说明 |
|---|---|---|
| 用户自带 OpenAI 兼容 API（endpoint / key / model） | ✅ 可行 | 不内置任何 API；密钥 DPAPI；用户自付 |
| AI 生成文本 / JSON | ✅ 可行 | 提示词模板、超时、token 上限、取消 |
| 按固定规则控制 OBS | ✅ 可行 | 复用现有 obs-websocket；动作白名单 |
| 外部脚本桥（用户写脚本） | ✅ 可行 | JSON stdin/stdout；超时/限额；不内嵌 JS 引擎 |
| 自动开始/停止推流 | ⚠️ 有条件 | 必须单独开关 + 倒计时 + 预检；默认关闭 |
| 读取平台弹幕 / 私信 | ❌ 3.1.0 | 各平台鉴权/协议/风控差异大；放 3.2+ |
| TTS 语音解说 | ❌ 3.1.0 | Windows SAPI 可做，但把音频送进 OBS 需要虚拟声卡/路由；放 3.2+ |
| 全自动无人直播 | ❌ 3.1.0 | 合规、内容质量、平台条款、故障兜底都不是一个 minor 版本能承担的 |

**建议**：3.1.0 只做“AI 自动化（实验性）”——
**用户自带 API + 固定规则 + OBS 白名单动作 + 外部脚本桥 + 干跑/日志**，
默认关闭；平台弹幕、TTS、全自动直播放 3.2。

#### 12.2.2 MVP 范围

新增页面 `AiAutomationPage`：

- Provider 配置：Base URL、API Key、Model、System Prompt、Temperature、Max Tokens、Timeout；
- 兼容 OpenAI Chat Completions 风格；支持本地 Ollama / LM Studio（需显式允许本机/内网端点）；
- 连接测试、模型列表（可选）、Token/费用估算（按用户填写的价格，不内置价格表）；
- 规则列表：新增/复制/启用/禁用/删除；
- 规则编辑器：触发器、提示词模板、变量、动作、限额；
- 干跑：只生成、不执行 OBS 动作；显示将执行的动作；
- 运行日志：时间、规则、耗时、token、结果、错误；正文脱敏；
- 手动运行、热键运行、定时运行；
- 规则导入/导出 JSON（便于用户备份/分享）。

规则示例（建议 schema）：

```json
{
  "name": "每 60 秒生成一句解说",
  "enabled": false,
  "trigger": { "type": "interval", "seconds": 60 },
  "prompt": "你是直播解说助手。当前场景：{scene}。请用一句不超过 40 字的中文，语气自然，不要编造事实。",
  "output": { "kind": "text", "maxChars": 80 },
  "actions": [
    { "type": "obs.setTextSource", "source": "AI 字幕", "text": "{ai.text}" }
  ],
  "limits": { "maxRunsPerHour": 20, "timeoutSeconds": 20 }
}
```

触发器（P0）：手动、热键、固定间隔。
触发器（P1）：OBS 事件（开始/停止录制、场景切换）、外部 webhook（需单独评估端口暴露风险）。

动作白名单（P0）：

- `obs.setTextSource`：写入文本源；
- `obs.switchScene`：切换场景（场景名必须来自用户预先选择的列表）；
- `obs.setSourceVisibility`：显示/隐藏来源；
- `obs.startRecording` / `obs.stopRecording`：录制开关；
- `obs.startStreaming` / `obs.stopStreaming`：推流开关（**单独开关，默认关闭**）；
- `script.run`：调用用户指定脚本；
- `toast.show`：应用内提示。

**重要安全设计**：AI 只负责生成**文本/值**，不负责生成“动作名”。
动作名由用户在规则里固定并经过白名单校验；模型输出不能直接变成任意 OBS 请求或 shell 命令。

#### 12.2.3 外部脚本桥

用户说“或者写脚本来实现”，建议做成**受控的外部进程桥**，而不是在应用内嵌 JS/Python 运行时：

- 用户选择一个脚本/可执行文件（`.exe` / `.cmd` / `.ps1` / `.py`）；
- 应用把上下文 JSON 写到 stdin，脚本把结果 JSON 写到 stdout；
- 应用校验返回的 JSON schema 与动作白名单；
- 超时 10 秒；stdout 上限 64 KB；stderr 只进日志且脱敏；
- `UseShellExecute=false`、`CreateNoWindow=true`、不拼接 shell 字符串、不允许 AI 输出决定脚本路径；
- 默认不把 API Key 传给脚本；如用户需要，单独勾选并通过环境变量传递；
- 首次运行显示完整命令行与风险提示，用户确认后才执行；
- 脚本路径必须来自用户手动选择，不能来自网络/知识库/AI 输出。

这能满足“用户自己写脚本”的诉求，同时避免把任意代码执行能力做成默认开放面。

#### 12.2.4 本地/内网端点策略

现有 `HostBridge.ValidateAiUrl` 强制 HTTPS 且拒绝本机/私网地址（SSRF 防护）。
支持 Ollama / LM Studio 需要放开本机端点，这是**安全敏感改动**：

- 远程端点：继续 HTTPS + 公网地址；
- 本机/内网端点：只有用户显式勾选“允许本机/内网 AI 端点”后才允许 `http://127.0.0.1`、
  `http://localhost` 或用户明确填写的私网地址；
- 禁止 `file://`、`ftp://` 等协议；禁止重定向到私网；保留 DNS rebinding 防护；
- 该开关旁必须写明“这会允许应用访问你本机/局域网的服务”；
- 所有请求仍走超时、响应大小上限、日志脱敏。

#### 12.2.5 安全、隐私与合规

- **不内置 API**：不提供免费 key、不代理用户请求、不把 key 写进日志/规则/诊断报告；
- **密钥**：DPAPI 加密，存 `secrets.dat`；规则文件只存 secret key 名，不存明文；
- **提示词注入**：AI 输出只作为文本/值；动作由用户固定；场景名/来源名走白名单；
- **成本**：用户自付；显示请求数/token 估算；限频；超时；取消；
- **自动推流**：默认关闭；单独开关；5 秒倒计时；开播前预检；失败自动停止并提示；
- **合规**：AI 生成内容披露、平台条款、版权、声音克隆/仿冒、垃圾信息；
  首次启用显示“责任使用”提示，用户确认；
- **失败兜底**：AI 不可用/超时/返回非 JSON 时，不做任何 OBS 动作，只提示/记录；
- **可关闭**：整个 AI 自动化默认关闭；单条规则可禁用；一键“停止所有自动化”。

#### 12.2.6 技术设计与测试

新增：

| 文件 | 职责 |
|---|---|
| `Services/Ai/AiAutomationCore.cs` | 纯逻辑：规则校验、变量替换、动作白名单、限频、JSON 解析 |
| `Services/Ai/AiAutomationService.cs` | 编排：触发器、调用 AI、执行动作、日志、取消 |
| `Services/Ai/ScriptRunnerService.cs` | 外部脚本进程桥（stdin/stdout、超时、限额） |
| `Services/Ai/AiAutomationEndpointPolicy.cs` | 本地/内网端点策略（与现有 HostBridge 分离） |
| `Models/Ai/AutomationRule.cs` | 规则/触发器/动作/限额模型 |
| `Views/AiAutomationPage.xaml(.cs)` | 配置、规则编辑、干跑、日志 |
| `AppServices.cs` | 注册新服务 |
| `Localization/StringTable*.cs` | 新增文案 |

测试：

- 规则 schema 校验：缺字段/未知动作/超长提示词/非法 cron/间隔；
- 变量替换：`{scene}` / `{time}` / `{recording}` / `{lastError}`，缺失变量不崩；
- 动作白名单：模型返回 `switchScene` 但规则没配 → 拒绝；
- 脚本桥：超时、非 JSON、超大输出、非零退出码、路径不存在；
- 端点策略：HTTPS 公网通过；HTTP 公网拒绝；本机/私网仅在开关开启时通过；重定向拒绝；
- 自动推流：默认关闭；开启后倒计时；预检失败不推流；失败自动停止；
- 密钥：不写日志、不进规则 JSON、不出现在诊断报告；
- 限频：超过 `maxRunsPerHour` 不再调用；
- 干跑：不执行任何 OBS 动作。

#### 12.2.7 优先级与排期建议

- **推荐**：3.1.0 只交付 P1 实验性 MVP（配置 + 手动/热键/定时 + OBS 白名单动作 + 脚本桥 + 干跑），
  默认关闭；平台弹幕/TTS/全自动放 3.2。
- **如果 3.1.0 排期只能保一个**：保提词器 + 一键录制，AI 放 3.2。
- **如果坚持把 AI MVP 进 3.1.0**：约 +15 人日，合并约 15~16 周（1 名开发者）；
  完整自动直播（弹幕/TTS/平台互动）另立 3.2 项目，不建议作为 minor 版本。

### 12.3 彩蛋

**可实现，低风险，建议 P2（1~2 人日）。**

建议做 3 个小彩蛋，不要做成干扰：

| 彩蛋 | 触发 | 效果 | 注意 |
|---|---|---|---|
| 小助手路过 | 主窗口输入 Konami 码 `↑↑↓↓←→←→BA` | 一个像素小助手从标题栏走过并挥手 | 尊重“减少动画”；默认无声音 |
| 隐藏强调色 | 设置页连续点击应用图标 7 次 | 解锁一套隐藏强调色（如“午夜青”） | 不影响现有 5 套配色；可恢复默认 |
| 彩蛋词条 | 搜索“彩蛋”/“easter egg” | 知识库出现一条有趣但真实的 OBS 小知识 | 不污染正常搜索结果排名 |

可选（P2，工作量更大）：

- 托盘小助手表情随 OBS 状态变化（已连接/录制中/错误）；
- 首次录制成功当天，托盘气泡说一句“今天也要录得漂亮”（默认关闭通知）。

约束：

- 纯本地、无网络、无遥测；
- 默认无声音（录制中尤其不能出声）；
- 不弹置顶窗口、不抢焦点、不影响录制；
- 设置页提供“允许彩蛋”开关，默认开启但可关闭；
- 尊重“减少动画”和“高对比”；
- 彩蛋不能遮挡错误提示/危险确认，不能改变业务行为。

技术：`EasterEggService`（状态机 + 本地开关） + 主窗口/设置页/搜索页少量挂钩；
动画用 WPF Storyboard；不引入第三方库。

### 12.4 UI 视觉刷新

**可实现，但建议分两层：3.1.0 做“视觉刷新 1.0”，3.2 再做全量重设计。**

#### 12.4.1 3.1.0 范围

| 层级 | 内容 | 3.1.0 |
|---|---|---|
| 设计令牌 | 间距 4/8/12/16/24、圆角 4/6/8/12、阴影层级、字号/行高、动效时长 | ✅ P1 |
| 基础组件 | 按钮、卡片、输入框、开关、分段控件、标签、空状态、焦点环 | ✅ P1 |
| 关键页面 | 首页、控制台、设置、简单录像卡、迷你小窗、提词器窗口、AI 页面 | ✅ P1 |
| 全量 19 页 | 所有页面重排/重绘 | ❌ 3.2 |

#### 12.4.2 视觉刷新原则

- 不做“换皮式大改”：保留现有导航结构、页面信息架构、深浅色/强调色体系；
- 先改令牌，再改组件，最后改关键页；避免 19 个页面同时重写；
- 统一圆角/间距/阴影/焦点环；减少“AI 生成感”的过多渐变与装饰；
- 所有颜色走 `Palette.xaml` / `AppearanceService`，不新增硬编码 hex；
- 高对比、减少动画、系统文本缩放必须继续通过；
- Win7 构建不使用 Win10/11 独有的视觉效果（模糊、亚克力、Mica 等）。

#### 12.4.3 风险

| 风险 | 说明 | 对策 |
|---|---|---|
| 范围膨胀 | 19 个页面 + 51KB `Controls.xaml`，全量重设计很容易拖垮 3.1.0 | 只做令牌 + 关键页；全量放 3.2 |
| 对比度回归 | 新配色可能让次要文字/按钮不达标 | 用现有对比度基线 + 静态检查 + 手工核对 |
| Win7 兼容 | 新视觉效果在 net6/Win7 上不可用 | 只用 WPF 基础能力；效果开关降级 |
| 测试/文档不同步 | 版本/文案/资源引用漏改 | 现有 `check_docs.py` + 文案键单测守住 |

### 12.5 补充的有趣功能（纳入 3.1 规划）

> 以下 12 项全部纳入 **3.1 版本规划**；3.1.0 先交付 1~5，其余进 3.1.x。
> 成本是估算，实际以开工前评估为准。

#### 12.5.1 3.1.0 先做（P1）

| # | 功能 | 价值 | 成本 | 实现要点 |
|---|---|---|---|---|
| 1 | **录制前 3 秒倒计时 + 可选提示音/动画** | 给用户准备时间，减少“开头手忙脚乱” | S（约 2 人日） | `SimpleRecordingStep.CountingDown`；0/3/5/10 秒可配；可取消；默认无声音；尊重“减少动画” |
| 2 | **录像文件命名模板**（日期/场景/游戏/分辨率） | 录完不用手动改名；归档更整齐 | S（约 2 人日） | 模板变量 `{date}` `{time}` `{scene}` `{profile}` `{resolution}` `{fps}`；非法字符净化；优先写 OBS `FilenameFormatting`，不可用时停止后重命名并保留原文件 |
| 3 | **开播检查清单 / 倒计时** | 直播前 10 秒确认麦克风、场景、码率 | S（约 2 人日） | 复用 `PreflightCheckService`；麦克风/场景/码率/磁盘/GPU 五项；可选 10 秒倒计时；可关闭，不强制阻断 |
| 4 | **提词器脚本卡片 × 场景联动** | 切场景自动跳到对应段落，和 §12.1 天然互补 | M（约 3 人日） | 场景名 → 脚本段落标记；订阅 `CurrentProgramSceneChanged`；只滚动提词器，不自动切场景；场景改名时给一次提示 |
| 5 | **录制中静音/音量过低提醒** | 直播/录课最常见的“白录”事故 | S（约 2 人日） | 复用现有音频电平/设备健康数据；阈值 + 持续时间 + 防抖；只提醒，不自动取消静音；仅在录制/推流中生效 |

#### 12.5.2 3.1.x 排期（P2）

| # | 功能 | 价值 | 成本 | 说明 |
|---|---|---|---|---|
| 6 | **录像文件自动归档到日期文件夹** | 长期录制用户的刚需 | S（约 1 人日） | 可配置 `年/月/日` 层级；失败不丢文件 |
| 7 | **录制日历 / 热力图（本地统计）** | 看到自己的录制习惯；纯本地、无隐私风险 | M（约 4 人日） | 只读本地录像目录与归档记录；不上传 |
| 8 | **健康成就系统**（连续无丢帧、首次 4K、零崩溃周） | 把“体检”变成正反馈；与彩蛋/小助手天然结合 | M（约 4 人日） | 本地成就；可关闭；不弹窗打断录制 |
| 9 | **配置体检报告导出 PNG/PDF** | 求助/售后时一张图说清楚 | M（约 3 人日） | 复用诊断报告；脱敏后导出 |
| 10 | **短视频封面/标题生成（本地模板）** | 录完直接发；不依赖 AI | M（约 3 人日） | 本地模板 + 变量；不调用云端 |
| 11 | **一键彩排**（本地录制 + 虚拟摄像头，不推流） | 开播前完整走一遍流程 | M（约 4 人日） | 复用一键录制；不推流；可自动删除彩排文件 |
| 12 | **录后 AI 复盘**（日志 + AI 生成复盘报告） | 复用 §12.2 的 AI 配置，价值高 | M（约 4 人日） | 依赖 AI 配置；AI 未进 3.1.0 时随 AI 一起排期 |

**估算**：3.1.0 的 1~5 约 **11 人日**；3.1.x 的 6~12 约 **23 人日**。

### 12.6 合并后的 3.1.0 范围与优先级

| 功能 | 3.1.0 | 3.1.x / 3.2 | 说明 |
|---|---|---|---|
| 个性化自定义壁纸 | **P0** | — | 第一轮 |
| 一键启动 OBS + 默认 WebSocket | **P0** | — | 第一轮 |
| 提词器 | **P0** | — | 第二轮；含捕获排除与兼容向导 |
| UI 视觉刷新 1.0 | **P1** | 全量重设计 3.2 | 令牌 + 基础组件 + 关键页 |
| 彩蛋 | **P2** | — | 低风险，1~2 人日 |
| 补充有趣功能 1~5 | **P1** | — | 倒计时 / 命名模板 / 开播清单 / 提词器场景联动 / 静音提醒 |
| 补充有趣功能 6~12 | — | **P2** | 自动归档 / 日历 / 成就 / 报告导出 / 封面标题 / 彩排 / AI 复盘 |
| AI 自动直播 MVP | **P1 实验性（默认关闭）** | 完整版 3.2 | 用户自带 API；平台弹幕/TTS/全自动 3.2 |

**推荐方案 A（3.1.0 聚焦）**：

- 3.1.0 = 壁纸 + 一键录制 + 提词器 + UI 视觉刷新 1.0 + 补充功能 1~5 + 彩蛋；
- AI 自动直播作为 **3.1.0 实验性预览**（默认关闭）或直接放 3.2；
- 补充功能 6~12 放 3.1.x。

**方案 B（全部进 3.1.0）**：

- 壁纸 + 一键录制 + 提词器 + UI 视觉刷新 + 彩蛋 + AI MVP + 全部 12 项补充功能；
- 约 100 人日 ≈ 20 周（1 名开发者）；2 人并行约 10~11 周；
- 不建议把“完整自动直播”也塞进来；方案 B 只适合人力充足、且能接受更长验证周期的情况。

### 12.7 合并后的实施计划与排期

> 第一轮 P0 约 31 人日；提词器约 12 人日；UI 视觉刷新 1.0 约 6 人日；
> 彩集约 2 人日；补充功能 1~5 约 11 人日。
> 合计约 **62 人日 ≈ 12.4 周**（1 名开发者），按 **13 周（含缓冲）** 排。
> AI 自动化 MVP 约 15 人日，若加入则约 77 人日 ≈ 15.4 周；建议 2 人并行或放 3.2。
> 补充功能 6~12 约 23 人日，放 3.1.x。

| 周 | 日期 | 里程碑 | 内容 |
|---|---|---|---|
| W1 | 2026-10-19 ~ 10-23 | M1 设计冻结 | 壁纸/一键录制/提词器/AI 的 schema、状态机、错误矩阵；提词器捕获排除真机预研 |
| W2 | 2026-10-26 ~ 10-30 | M2 壁纸核心 | 壁纸库、服务层、迁移 |
| W3 | 2026-11-02 ~ 11-06 | M3 壁纸可交付 | 渲染层、设置页、文案/无障碍 |
| W4 | 2026-11-09 ~ 11-13 | M4 一键录制 | `EnsureConnected`、流程重排、首页/设置页、v4 分支 |
| W5 | 2026-11-16 ~ 11-20 | M5 提词器 | 窗口、滚动、热键、穿透、捕获排除 |
| W6 | 2026-11-23 ~ 11-27 | M6 提词器收尾 | 编辑/持久化/兼容向导/测试 |
| W7 | 2026-11-30 ~ 12-04 | M7 UI 视觉刷新 | 令牌 + 基础组件 + 关键页 |
| W8 | 2026-12-07 ~ 12-11 | M8 补充功能 1~3 | 倒计时、命名模板、开播检查清单 |
| W9 | 2026-12-14 ~ 12-18 | M9 补充功能 4~5 + 彩蛋 | 提词器场景联动、静音提醒、彩蛋、i18n |
| W10 | 2026-12-21 ~ 12-25 | M10 集成 | 双构建、连接/提词器/壁纸/补充功能矩阵 |
| W11 | 2026-12-28 ~ 2027-01-01 | M11 回归 | 真机矩阵、性能、文档一致性 |
| W12 | 2027-01-04 ~ 01-08 | M12 修回归 | 修回归、发布说明、打包 |
| W13 | 2027-01-11 ~ 01-15 | M13 发布缓冲 | 最终验收、发布 |

> 如果 AI 自动化 MVP 必须进 3.1.0：建议从 W5 起由第二名开发者并行，
> 主线（壁纸/一键/提词器）不变；否则把 AI 放到 3.2，避免 3.1.0 变成“什么都有一点、什么都不稳”。

### 12.8 风险与验收

#### 12.8.1 新增风险

| # | 风险 | 概率 | 影响 | 对策 |
|---|---|---|---|---|
| E1 | 显示器采集仍录到提词器 | 中 | 高 | 真机验证；失败改用窗口采集/第二屏；文案不承诺绝对排除 |
| E2 | 捕获排除留下黑块 | 中 | 中 | 兼容向导；建议窗口采集；提供关闭开关 |
| E3 | Win7 不支持排除 | 高 | 中 | 明确降级说明；窗口采集/第二屏 |
| E4 | 提词器抢焦点/干扰录制 | 中 | 高 | `ShowActivated=False`、`WS_EX_NOACTIVATE`、录制中不弹窗 |
| E5 | AI Key 泄漏 | 中 | 高 | DPAPI；日志脱敏；不进规则 JSON/诊断报告 |
| E6 | 提示词注入导致越权动作 | 中 | 高 | 动作白名单；AI 只生成值与文本；不做任意 OBS 请求 |
| E7 | 本地端点放开带来 SSRF | 中 | 高 | 独立策略；显式开关；禁止重定向；保留私网校验 |
| E8 | 脚本桥执行恶意代码 | 中 | 高 | 用户手动选路径 + 首次确认；不做 AI 决定路径；超时/限额 |
| E9 | 自动推流误触发 | 低 | 高 | 默认关闭；单独开关；倒计时；预检；审计日志 |
| E10 | AI 成本失控 | 中 | 中 | 限频；token 上限；超时；费用估算；一键停用 |
| E11 | UI 刷新范围膨胀 | 高 | 高 | 只做令牌 + 关键页；全量 3.2 |
| E12 | 倒计时/命名模板与 OBS 文件名格式冲突 | 中 | 中 | 优先写 OBS `FilenameFormatting`；不可用时停止后重命名；失败保留原文件 |
| E13 | 静音提醒误报/漏报 | 中 | 中 | 阈值 + 持续时间 + 防抖；只提醒不自动取消静音；仅在录制/推流中生效 |
| E14 | 提词器场景联动在场景改名后失效 | 中 | 低 | 场景名快照 + 改名提示 + 允许手动重绑 |
| E15 | 彩蛋干扰录制/无障碍 | 低 | 中 | 默认无声音；不置顶；可关闭；尊重减少动画 |

#### 12.8.2 新增验收

- [ ] 提词器：窗口采集/游戏采集不出现；显示器采集在支持的系统上可排除；不支持时有明确替代方案；
- [ ] 提词器：滚动/暂停/速度/字号/镜像/穿透/热键/多屏/持久化可用；
- [ ] AI：用户自带 OpenAI 兼容 API 可配置、可测试、可干跑；密钥加密且日志不出现；
- [ ] AI：规则只能执行白名单动作；模型输出不能直接触发任意 OBS/shell；
- [ ] AI：本地/内网端点必须显式开启；远程仍 HTTPS + 公网；
- [ ] AI：自动推流默认关闭；开启后倒计时 + 预检 + 失败停止；
- [ ] 彩蛋：可触发、可关闭、无声音、不干扰录制；
- [ ] 补充功能：倒计时可配置/可取消/默认无声音；命名模板可预览、非法字符净化、失败保留原文件；
- [ ] 补充功能：开播检查清单可关闭、不强制阻断；静音提醒只在录制/推流中生效且不自动取消静音；
- [ ] 补充功能：提词器场景联动只滚动提词器、不自动切场景；场景改名有提示；
- [ ] UI：关键页在高对比/减少动画/125%~200% DPI/Win7 下不破版；
- [ ] 双构建、单测、自检、无障碍/文档检查全部通过。

### 12.9 新增代码落点

| 主题 | 文件 | 关键符号 |
|---|---|---|
| 提词器窗口 | `Views/TeleprompterWindow.xaml(.cs)` | 滚动、镜像、穿透、倒计时 |
| 提词器服务 | `Services/Shell/TeleprompterService.cs` | 单实例、位置/设置持久化、热键入口 |
| 提词器纯逻辑 | `Services/Shell/TeleprompterCore.cs` | 速度/字号/透明度 clamp、滚动步进、设置归一化 |
| 提词器设置 | `Models/Shell/TeleprompterSettings.cs` | 位置/尺寸/字号/速度/镜像/穿透 |
| 捕获排除 | `Services/Compat/WindowCaptureExclusion.cs` | `SetWindowDisplayAffinity`、OS 版本判断 |
| 热键 | `Services/Shell/GlobalHotkeyService.cs`、`Models/Shell/HotkeySettings.cs` | 新增 3 个 `HotkeyAction` |
| 托盘/入口 | `Services/Shell/TrayService.cs`、`MainWindow.xaml.cs` | 菜单项、显示/隐藏 |
| AI 规则纯逻辑 | `Services/Ai/AiAutomationCore.cs` | 规则校验、变量、白名单、限频 |
| AI 编排 | `Services/Ai/AiAutomationService.cs` | 触发器、调用、动作、日志 |
| 脚本桥 | `Services/Ai/ScriptRunnerService.cs` | stdin/stdout、超时、限额 |
| 端点策略 | `Services/Ai/AiAutomationEndpointPolicy.cs` | 本地/内网显式开关 |
| AI 模型 | `Models/Ai/AutomationRule.cs` | 规则/触发器/动作/限额 |
| AI 页面 | `Views/AiAutomationPage.xaml(.cs)` | 配置、规则、干跑、日志 |
| 彩蛋 | `Services/Shell/EasterEggService.cs`、`MainWindow.xaml.cs`、`Views/SettingsPage.xaml(.cs)` | 状态机、开关 |
| UI 令牌 | `Themes/Palette.xaml`、`Themes/Controls.xaml`、`Themes/Icons.xaml` | 间距/圆角/字号/组件 |
| 倒计时 | `Services/Shell/SimpleRecordingService.cs`、`Services/ObsConfig/SimpleRecordingCore.cs` | `CountingDown` 阶段、秒数配置、取消 |
| 命名模板 | `Services/ObsConfig/RecordingNamingCore.cs`、`RecordingNamingService.cs` | 变量替换、非法字符净化、失败回退 |
| 开播检查清单 | `Services/Shell/StreamChecklistService.cs`、`Views/ConsolePage.xaml(.cs)` | 复用 `PreflightCheckService`；可关闭 |
| 静音提醒 | `Services/Shell/AudioAlertService.cs` | 电平阈值、持续时间、防抖、仅录制/推流中 |
| 提词器场景联动 | `Services/Shell/TeleprompterService.cs`、`Services/Shell/SceneAutoSwitcher.cs` | 场景名 → 脚本段落；只滚动不切场景 |

### 12.10 新增文案键（建议）

**提词器**

- `teleprompter.title`
- `teleprompter.open`
- `teleprompter.close`
- `teleprompter.play`
- `teleprompter.pause`
- `teleprompter.speed`
- `teleprompter.fontSize`
- `teleprompter.lineHeight`
- `teleprompter.opacity`
- `teleprompter.mirrorH`
- `teleprompter.mirrorV`
- `teleprompter.clickThrough`
- `teleprompter.countdown`
- `teleprompter.script`
- `teleprompter.scriptPlaceholder`
- `teleprompter.import`
- `teleprompter.clear`
- `teleprompter.monitor`
- `teleprompter.captureExclusion`
- `teleprompter.captureUnsupported`
- `teleprompter.captureGuide`
- `teleprompter.guide.windowCapture`
- `teleprompter.guide.displayCapture`
- `teleprompter.guide.secondMonitor`
- `teleprompter.hotkey.toggle`
- `teleprompter.hotkey.playPause`
- `teleprompter.hotkey.clickThrough`

**AI 自动化**

- `aiAuto.title`
- `aiAuto.desc`
- `aiAuto.enabled`
- `aiAuto.provider.baseUrl`
- `aiAuto.provider.apiKey`
- `aiAuto.provider.model`
- `aiAuto.provider.systemPrompt`
- `aiAuto.provider.temperature`
- `aiAuto.provider.maxTokens`
- `aiAuto.provider.timeout`
- `aiAuto.provider.test`
- `aiAuto.provider.localEndpoint`
- `aiAuto.provider.localEndpointWarning`
- `aiAuto.rule.new`
- `aiAuto.rule.name`
- `aiAuto.rule.trigger`
- `aiAuto.rule.prompt`
- `aiAuto.rule.actions`
- `aiAuto.rule.limits`
- `aiAuto.rule.dryRun`
- `aiAuto.rule.run`
- `aiAuto.rule.enabled`
- `aiAuto.trigger.manual`
- `aiAuto.trigger.hotkey`
- `aiAuto.trigger.interval`
- `aiAuto.action.setTextSource`
- `aiAuto.action.switchScene`
- `aiAuto.action.setSourceVisibility`
- `aiAuto.action.startRecording`
- `aiAuto.action.stopRecording`
- `aiAuto.action.startStreaming`
- `aiAuto.action.stopStreaming`
- `aiAuto.action.runScript`
- `aiAuto.action.toast`
- `aiAuto.script.path`
- `aiAuto.script.warning`
- `aiAuto.script.timeout`
- `aiAuto.error.invalidJson`
- `aiAuto.error.actionNotAllowed`
- `aiAuto.error.rateLimited`
- `aiAuto.error.timeout`
- `aiAuto.compliance.notice`
- `aiAuto.stopAll`

**彩蛋 / UI / 补充功能**

- `easterEgg.enabled`
- `easterEgg.found`
- `easterEgg.hiddenTheme`
- `easterEgg.knowledgeTitle`
- `theme.midnight`
- `settings.appearance.visualRefresh`
- `recording.countdown`
- `recording.countdown.seconds`
- `recording.naming`
- `recording.naming.preview`
- `recording.naming.invalid`
- `stream.checklist`
- `stream.checklist.mic`
- `stream.checklist.scene`
- `stream.checklist.bitrate`
- `stream.checklist.disk`
- `stream.checklist.gpu`
- `audio.muteAlert`
- `audio.muteAlert.low`
- `teleprompter.sceneLink`
- `teleprompter.sceneLink.missing`

### 12.11 待确认的产品决策（第二轮）

1. **提词器默认排除方式**：默认“窗口采集/游戏采集”引导，还是默认尝试显示器采集排除？
   - 推荐：默认引导“窗口采集/游戏采集”，显示器采集作为“高级/实验”选项。
2. **AI 自动直播的边界**：3.1.0 只做实验性 MVP，还是等 3.2 一起做完整版？
   - 推荐：3.1.0 实验性 MVP（默认关闭）；完整版 3.2。
3. **UI 刷新范围**：只做令牌 + 关键页，还是 19 页全量重设计？
   - 推荐：令牌 + 关键页；全量 3.2。
4. **补充功能批次**：1~5 进 3.1.0，6~12 进 3.1.x；还是全部进 3.1.0？
   - 推荐：1~5 进 3.1.0，6~12 进 3.1.x；全部进 3.1.0 会显著拉长排期。
5. **彩蛋风格**：像素小助手/隐藏主题/知识库词条，选哪几个？
   - 推荐：三个都做，但全部可关闭、默认无声音、不干扰录制。

---

**本文到此结束。** 本次会话只更新了本计划文档，未修改任何源码、配置、安装脚本或发布物。
