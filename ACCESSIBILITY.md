# 无障碍说明 / Accessibility

> 本文是 **OBS帮助助手（OBS Helper）** 的无障碍（accessibility / a11y）说明：
> 我们想做到什么、当前做到哪一步、哪里还不行、以及发现障碍时怎么告诉我们。
>
> **本文如实区分「目标」与「现状」。**「已知限制」一节里的每一条都在源码里核对过
> （文件与行为都写出来了），但那是对源码的**静态核对**，不是用辅助技术实测出来的结论 ——
> 详见 §5 的证据边界。请不要把 §1 的承诺读成「已经做到」。

## 中文版

### 1. 我们的无障碍承诺

我们想做到这四件事，并且接受按这四条被检验：

**① 主要任务可以只用键盘完成。**
主要任务指：查知识库（搜索 / 分类 / 问题详情）、跑体检与看诊断结论、连 OBS 与控制台操作、
简单录像、看日志分析、改设置（语言 / 主题 / 字号 / 高对比 / 减少动画）。
判据是「不碰鼠标能不能走完这条路」，而不是「有没有快捷键」。

**② 状态不靠颜色单独传达。**
成功 / 警告 / 失败 / 提示这类状态，除了颜色还必须有一个形状或一个词。
状态药丸因此是**图标 + 文字 + 颜色**三重编码（`Themes/Controls.xaml` 的 `StatusPill`：
圆形 / 对勾 / 三角 / 叉 / i 五种矢量图形）；连接状态除颜色点外还有文字
（已连接 / 连接中 / 认证中 / 重连中 / 连接失败 / 未连接）。
色觉障碍用户、黑白打印、高对比模式下都不该丢信息。

**③ 尊重系统与用户自己的视觉偏好。**
主题「跟随系统」读系统浅色 / 深色设置并**监听实时变更**（`AppearanceService.IsSystemDark` +
`SystemEvents.UserPreferenceChanged`）；应用内提供四档字号、应用内高对比、减少动画。
**减少动画**不是装饰性开关：它把统一的动效时长资源 `MotionDuration` 归零，
主窗口过渡、Toast、引导与插件页都引用同一份资源。

**④ 关键状态对屏幕阅读器可读。**
这是我们**最没做到**的一条，因此把它写成目标而不是成绩：目前只有 WPF 原生控件的默认
UI Automation 支持（按钮 / 复选框 / 下拉 / 列表项的文字能被读到），
加上轻提示（Toast）被声明成实时区域；自定义绘制的图形与动态出现的卡片
都还没有可访问名称与播报——具体缺口逐条列在 §3。

**当前整体判断（不回避）**：键盘可操作、不依赖颜色、跟随系统深浅色这几件事大体成立，
而且**系统级「文本大小」与「高对比主题」现在都跟随了**；
但「屏幕阅读器支持」仍然**只有原生控件的默认那一层加一个实时区域声明**，
不能宣称「对屏幕阅读器可用」。2026-10 的优化清单把无障碍单列为一类待办
（清单 C9 / E4：`AutomationProperties` 覆盖、分段控件焦点指示、高 DPI 最小宽度、系统级高对比）——
核对期间其中几项（分段控件焦点、下拉焦点、危险确认框焦点、系统高对比、系统文本大小）
**已经在代码里落地**，详见 §3 与 §5。

### 2. 支持的环境

| 维度 | 支持情况 | 依据 |
|---|---|---|
| **操作系统（主构建）** | Windows 10 / 11（x64）。安装包 `MinVersion=10.0`。 | `OBS_Helper_Setup.iss`、`Services/OsSupport.cs` |
| **操作系统（兼容构建）** | Windows 7 SP1 ~ 11（`net6.0-windows`，产物带 `_win7` 后缀，`MinVersion=6.1sp1`）。 | `OBS_Helper.Wpf.csproj`、`.iss` |
| **高 DPI** | **Per-Monitor V2**（`dpiAware=true/pm` + `dpiAwareness=PerMonitorV2, PerMonitor`）。多显示器不同缩放比例下界面按各自 DPI 重排，不做位图拉伸。 | `app.manifest` |
| **窗口缩放** | 窗口可自由缩放；最小 920 × 620 DIP。**限制见 §3.5。** | `MainWindow.xaml` |
| **主题** | 跟随系统 / 浅色 / 深色，即时生效、不用重启；另有 5 套品牌强调色。 | `AppearanceService`、设置 →「外观与无障碍」 |
| **字号档位** | 四档：小 / 标准 / 大 / 特大（档位系数 0.92 / 1.0 / 1.12 / **1.28**），即时生效。实际字号还要**再乘以** Windows「辅助功能 → 文本大小」的缩放系数（读注册表 `HKCU\Software\Microsoft\Accessibility\TextScaleFactor`，钳在 1.0~2.0），所以**会跟随系统把文字调大**。 | `AppearanceService.Apply`、`SystemTextScaleFactor` |
| **高对比** | 有（开关在设置 →「外观与无障碍」）**或** Windows 高对比主题 —— 实际生效值是「应用内开关 **OR** 系统高对比」（`SystemParameters.HighContrast`），并监听系统变更（关掉系统高对比后自动恢复原样，不改写用户自己的设置）。它会把正文 / 次要文字 / 线条推到纯黑或纯白，并强制内容区背景回默认底。**覆盖面仍有限，见 §3.4。** | `AppearanceService.HighContrastEffective`、`Apply` |
| **减少动画** | 有（开关同上）。统一动效时长 `MotionDuration` 归零。**只认应用内开关，不跟随系统偏好，见 §3.6。** | `AppearanceService`、`MotionDuration` 各引用点 |
| **界面语言** | 简体中文 / English，**即时切换、不用重启**；随包离线内容（知识库 / 模板 / 插件目录 / 排障指引）也是中英两份。首启语言次序：应用内选过 > 安装向导写的 `language.ini` > 简体中文。繁体中文 / 日语尚无。 | `Localization/*`、`LocalizationService` |
| **键盘（应用内）** | `Ctrl+F` 搜索、`Alt+←` 或鼠标后退键返回、`Esc` 关对话框（`IsCancel`）。导航项、按钮、复选框、下拉均为标准控件，参与 Tab 顺序。**剩余缺口见 §3.2。** | `MainWindow.xaml`、`Themes/Controls.xaml` |
| **全局热键（应用外也生效）** | 5 组，默认 `Ctrl+Alt+R` 录制 / `+S` 推流 / `+C` 虚拟摄像头 / `+M` 迷你小窗 / `+O` 显示隐藏窗口，均可在设置里改键或关闭。 | `Models/Shell/HotkeySettings.cs` |
| **屏幕阅读器** | 只有 WPF 原生控件的默认朗读，**加上轻提示（Toast）被声明为实时区域**。除这一处外没有任何 `AutomationProperties`。**见 §3.1。** | `Themes/Controls.xaml` 的 `ToastCard` 样式 |

> **关于 Windows 7 兼容构建的一句实话**：它能装、能跑、离线知识库那一半能用，
> 但本工具的连接层讲的是 obs-websocket **v5** 协议，而 v5 随 OBS 28 一起内置，
> OBS 28 起已不支持 Windows 7。也就是说在 **V2.9.6 及更早版本**里，Win7 上
> **「连上 OBS」这半边能力在最需要它的用户那里用不上**。
> （这条边界正在被处理：工作区里已出现 obs-websocket **4.x 旧协议**兼容核心
> `Services/Obs/ObsLegacyV4Core.cs`，**只在 Win7 兼容构建启用**。
> 它是否随某个版本发布、覆盖到什么程度，**以实际发布的版本为准** —— 本文不替它做承诺。）
> 另外 Win7 **真机未验证**（只做到「目标框架可编译 + 安装包 MinVersion 正确下发」），
> 且 Per-Monitor V2 是 Windows 10 1607+ 的能力，Win7 上只有系统级 DPI。

### 3. 已知限制

> 这一节是本文最该被认真读的部分。每条都给了源码位置，可自行核对。
> **核对时上游正在并行实现无障碍改进**，所以有些条目在核对过程中就从「有问题」变成了「已修好」——
> 这类条目已就地标为已修复（并说明现在的行为），其余条目也可能在你读到本文时已经过时：
> **以代码为准**，见 §5。

#### 3.1 屏幕阅读器：只有原生控件的默认一层（外加 Toast 一个实时区域）

全仓只有**一处**真正使用 `AutomationProperties`：轻提示卡片
（`Themes/Controls.xaml` 的 `ToastCard` 样式）声明了
`AutomationProperties.LiveSetting = "Assertive"`，让「已连接 OBS」「开录失败」这类提示
有机会被播报。`AutomationProperties.Name` / `.HelpText` 仍然**全仓零使用**。实际后果：

- **动态出现的卡片不会被朗读。** 问题卡、诊断项、日志发现、体检结论、简单录像卡
  都是运行时构造的，没有可访问名称、也没有实时区域，
  出现或更新时屏幕阅读器不会主动播报。用户得自己 Tab 过去逐个探索。
- **Toast 现在被声明成实时区域，但不保证真的会播报，而且仍然消失得很快。**
  WPF 不会仅凭 `LiveSetting` 就自动播报 —— 还需要在内容变化时主动抛出
  `AutomationEvents.LiveRegionChanged` 事件，而全仓**没有任何
  `RaiseAutomationEvent` 调用**。所以「读屏会不会真的读出这条 Toast」
  **取决于读屏实现与 WPF 版本，未经实测**，不要当成已经可用。
  另外统一轻提示 2.5 秒后自动消失，**没有关闭按钮、没有历史记录**，
  而「开录失败」这类**要照着做**的提示也走 Toast。
- **自定义绘制的图形没有可访问名称。** 状态药丸的矢量图标、监控页的迷你走势图
  （`Controls/Sparkline.cs`）、连接徽章的圆点都是画出来的形状；走势图尤其只有图像，
  **没有等价的数值表或文字摘要**。
- **连接徽章只能鼠标点。** 它是 `Border` + `MouseLeftButtonUp`，不是 Tab 停靠点
  （既没有 `IsTabStop` 也没有 `Focusable`）、没有键盘激活方式
  （键盘用户可走侧栏导航进控制台，功能不丢，但徽章本身不可键盘操作）。
- **没有跳过导航的入口（skip link）**，长页面里键盘用户要逐个 Tab 过侧栏。

**V3.0 已修（第三轮验证，2026-10-06）**：上面这些里能在代码里判定的都已处理 ——
① `ToastService` 现在会主动抛 `AutomationEvents.LiveRegionChanged`（不再是「只声明不抛」，
原先文档写的「全仓没有任何 RaiseAutomationEvent 调用」已过时）；
② 无文字内容的图标按钮补上了 `AutomationProperties.Name`（返回按钮）；
③ **连接徽章改成可 Tab 停靠 + 空格/回车激活**，并在每次刷新时写入**带状态**的可访问名称
（读屏能听出「已连接」还是「连接失败」）；
④ 设置页的 6 个背景色板原先只能鼠标点，现在可聚焦、有可见焦点框、回车/空格可选中、并带可访问名称
（键盘用户此前**换不了背景色**，属于「功能对某类用户直接不可用」）；
⑤ 新增静态检查脚本 `NOBS/scripts/check_accessibility.py` 并接入 CI，规则三条：
无文字内容的可交互控件必须有可访问名称；实时区域必须「声明 + 主动抛事件」两条齐全；
用鼠标点击事件当按钮的自绘元素必须可聚焦且有键盘激活方式。

**可以说的**：按钮 / 复选框 / 下拉 / 列表项这类标准控件有 WPF 自带的
UI Automation 支持，文字内容一般能被读到；Toast 是实时区域且会主动抛事件；
无文字内容的可交互控件都有可访问名称（**有 CI 守卫**，不会悄悄退化）。
**不能说的**：这个应用「支持屏幕阅读器」—— 仍然**没有用 NVDA / Narrator 实测过**。

#### 3.2 键盘：主要缺口已在核对期间修掉，剩下几处不可达

- ~~**分段控件 `SegmentButton` 没有键盘焦点指示**~~、~~**下拉框没有焦点反馈**~~、
  ~~**危险确认框默认焦点在「确认」**~~ —— **这三条在核对期间都修好了**：
  `SegmentButton` 现在有 `IsKeyboardFocused` 触发器（还有一条
  `IsKeyboardFocused` 的 `MultiTrigger`）；`AppComboBox` 的内层 `ComboToggle`
  用 `DataTrigger` 绑定了 `IsKeyboardFocusWithin`，`AppComboBoxItem` 也补上了
  `IsKeyboardFocused`；`ConfirmDialog.xaml.cs` 在 `danger: true` 时把默认按钮与
  初始焦点都换成「取消」（按回车或 `Esc` 都不会执行危险操作）。
  `BaseButton` / `CardButton` / `NavTabButton` / `AppCheckBox` / `AppSwitch` 本来就有。
- **迷你小窗键盘用不了。** 它设了 `ShowActivated="False"`（热键呼出时不抢焦点是刻意设计），
  代价是焦点留在别的窗口上，小窗里的按钮无法用键盘操作。
- **连接徽章只能鼠标点**（同 §3.1）：不是 Tab 停靠点。
- **没有访问键（Alt+字母）助记符。** 模板里配了 `RecognizesAccessKey`，但文案里没有用 `_` 前缀。
- **没有自定义 Tab 顺序**（全仓 `TabIndex` 零命中），顺序完全跟随可视树。
- **没有跳过导航的入口**（同 §3.1）。

> 逐控件核对（`Themes/Controls.xaml`）：`BaseButton` / `CardButton` / `NavTabButton` /
> `SegmentButton` / `AppCheckBox` / `AppSwitch` / `AppComboBoxItem` 都有键盘焦点触发器；
> 只有 `AppComboBox` 自身的模板没有触发器（焦点反馈由内层的 `ComboToggle` 提供）。

#### 3.3 字号：跟随系统「文本大小」，但应用内档位上限只有 1.28×

- 应用内四档系数为 0.92 / 1.0 / 1.12 / **1.28**，**应用内上限就是 1.28×**。
  好消息是**它现在会跟随 Windows 的「辅助功能 → 文本大小」**：
  最终系数 = 档位系数 × `SystemTextScaleFactor`（读注册表
  `HKCU\Software\Microsoft\Accessibility\TextScaleFactor`，钳在 1.0~2.0），
  所以系统调到 200% 时最大可以到 1.28 × 2.0 ≈ **2.56×**。
- 档位**只改字号资源**，不缩放图标与控件高度（这是刻意取舍：缩放整窗会让图标与边框糊掉），
  因此有 8 处字号是硬编码的装饰元素**不会跟着变大** —— 全部是固定尺寸的徽章 / 图标 / 序号：
  `Themes/Controls.xaml`（复选框的对勾字形）、`Controls/UpdateDialog.xaml`（庆祝字形）、
  `Controls/ProblemCard.xaml`（收藏星）、`Controls/ConfirmDialog.xaml`（告警字形）、
  `Views/HomePage.xaml`（分类图标），以及 `Views/ProblemPage.xaml.cs`（步骤序号徽章）、
  `Views/SetupPage.xaml.cs`（向导图标）、`Views/TemplatePage.xaml.cs`（模板图标）。
  正文不受影响，但它们是「不跟随档位」的已知面。
- 它仍然**替代不了系统级放大**（Windows 放大镜、浏览器式整页缩放）。

#### 3.4 高对比：现在跟随系统主题，但覆盖的语义面仍然有限

- **系统级「高对比主题」已经跟随**：实际生效值 `HighContrastEffective` =
  应用内开关 **OR** `SystemParameters.HighContrast`，并监听系统变更事件；
  关掉系统高对比后自动恢复原样，且**不改写用户自己的设置**。
- **但覆盖的语义面仍然有限**：高对比只影响 `TextBrush` / `MutedBrush` / `LineBrush`
  （推到纯黑或纯白）与内容区背景（强制回默认底，个性化背景让位给可读性）。
  它**不改**品牌色、语义色（成功 / 警告 / 失败 / 信息）与各类柔和底
  （`OkSoftBrush` / `DangerSoftBrush` / `WarnSoftBrush` / `InfoSoftBrush`）、
  以及分类语义色。也就是说「高对比」是**加深文字与线条 + 跟随系统开关**，
  仍不是一套完整的高对比配色方案。

#### 3.5 高 DPI 下的最小宽度可能超出可用逻辑宽度（按数值推算，未经真机复核）

`MainWindow.xaml` 设了 `MinWidth="920"`（DIP），主内容区只有**垂直**滚动
（`VerticalScrollBarVisibility="Auto"`），**没有横向滚动**。而在 1366 × 768 屏幕上：

| 系统缩放 | 可用逻辑宽度 | 与 MinWidth=920 的关系 |
|---|---|---|
| 100% | ≈1366 DIP | 够用 |
| 150% | ≈911 DIP | **小于 MinWidth** |
| 200% | ≈683 DIP | **明显小于 MinWidth** |

也就是说 150% 及以上缩放时窗口无法压到屏幕内，右侧内容可能落到屏幕外且**无法触达**
（没有横向滚动条）。**这条是按数值推算的，没有在真机上复核** —— 如实标注，欢迎按 §4 反馈实测结果。

#### 3.6 减少动画不跟随系统偏好

应用内「减少动画」把 `MotionDuration` 归零是有效的，但它**不读系统的「显示动画」偏好**
（`SystemParameters.ClientAreaAnimation` 零命中）。系统里关掉动画不会自动改变本应用，
需要用户在应用内自己开。

#### 3.7 内容与语言的限制

- 界面上所有文案与随包内容都有中英两份，但**英文内容未经母语者校订**
  （技术准确、术语统一，不等于出版级地道）；繁体中文与日语尚未支持。
- 知识库条目、排障指引是文本内容，其中的截图 / 示意图**没有替代文本**。
- ~~内容资产回退到中文时只写日志、界面上没有提示~~ —— **V3.0 已修**：英文内容资产缺失时，
  主窗口顶部会出现一条**可关闭**的提示条，并**点名是哪几块内容**
  （知识库条目 / 插件目录 / 场景模板 / 排障指引）；资产补齐后提示条会自行撤销。
  无界面自检第 24 项覆盖这条接线（上报 → 显示 → 点名资产 → 忽略 → 语言判定）。

#### 3.8 与无障碍相关的行为限制（会直接影响到人，所以一并列出）

- **连接失败与「从未连接」在部分界面上不可区分**：通用失败分支把状态置为「未连接」而不是
  「连接失败」，只有「连接失败」态才把原因放进徽章 ToolTip。也就是说连不上 OBS 时，
  首页 / 诊断页 / 托盘可能都没有任何提示。这对依赖托盘通知、或不看控制台页的用户尤其不利。
- **热键 / 小窗 / 后台刷新失败是静默的**：按键没反应时不会给出原因。
  对键盘 / 热键重度用户来说，「没消息 = 没问题」是错误的心智。

### 4. 如何反馈无障碍障碍

无障碍问题按 **BUG** 对待，走仓库既有的三条渠道（不新开渠道）：

| 渠道 | 入口 | 适合什么 |
|---|---|---|
| **GitHub Issue** | [新建 Issue](https://github.com/YYRMMAYO/OBS_Helper/issues/new/choose)（选「BUG 反馈」模板） | 可公开讨论的键盘 / 焦点 / 朗读 / 缩放问题 |
| **应用内「帮助与反馈」页** | 左侧导航「帮助与反馈」→ 反馈卡 | 不想注册 GitHub 时；卡上有官方表单、GitHub Issue、**离线二维码**，以及**一键复制报错材料** |
| **邮件** | **752139192@qq.com** | 不方便公开的、或涉及隐私 / 人身安全 / 行为准则的情形。中文或英文都可以 |

**请务必不要附未脱敏的日志或流密钥。**
日志里可能含推流地址与流密钥、账号密码、真实姓名，以及目录里的用户名。
提交前请先删除这些内容 —— 这条对**所有**反馈都成立，不只是安全类。

**反馈无障碍问题时，请尽量带上这几项**（有哪项写哪项）：

1. **应用版本**（设置 → 关于）与**安装方式**（安装包 / 便携版 / `_win7` 兼容构建）；
2. **Windows 版本**（如 Windows 11 24H2）与**显示缩放比例**、是否多显示器且各屏比例不同；
3. **用的什么辅助技术**：NVDA / Narrator / 放大镜 / 系统高对比主题 / 仅键盘 / 语音输入等，
   以及版本号；
4. **应用内设置**：字号档位、是否开了高对比 / 减少动画、界面语言；
5. **具体操作路径**：「按 Tab 到哪个控件 → 按了什么键 → 看到什么 / 没看到什么」；
6. **期望行为**：你期望它应该怎样（例如「焦点应该有一个可见的框」）；
7. 如果是朗读问题，说明**屏幕阅读器实际读出了什么**，哪怕只是「什么都没读」。

**安全漏洞不要走公开 Issue**：GitHub 没有私信功能，请走仓库
[SECURITY.md](SECURITY.md) 里的私密通道（GitHub Security 私密报告，或邮件并注明 `[SECURITY]`）。

### 5. 我们怎么验证（证据边界）

- §2「支持的环境」里的每一条都来自源码 / 清单的实测：
  `app.manifest`、`OBS_Helper.Wpf.csproj`、`OBS_Helper_Setup.iss`、
  `AppearanceService.Apply`、`Views/SettingsPage.xaml`、`Themes/Controls.xaml`、
  `Models/Shell/HotkeySettings.cs`。
- §3「已知限制」是**对源码的静态核对**（关键字命中数为 0 的地方就是没有实现的地方），
  **没有用 NVDA / Narrator / 高对比主题实测过**。
  §3.5 的最小宽度问题是**按数值推算**的，明确未经真机复核。
- **有一项无障碍自动化检查**（V3.0 新增）：`NOBS/scripts/check_accessibility.py`，已接入 CI，
  本地也可直接跑。它把**能静态判定**的三条钉住：
  ① 无文字内容的可交互控件（图标按钮等）必须显式写 `AutomationProperties.Name`；
  ② 实时区域必须「声明 `LiveSetting` + 主动抛 `LiveRegionChanged`」两条齐全；
  ③ 用鼠标点击事件当按钮用的自绘元素必须可聚焦且有键盘激活方式。
  它**不检查**（也不假装检查）：屏幕阅读器实际朗读效果、高对比配色的可读性、
  真机缩放与多显示器行为、焦点顺序是否合理 —— 这些仍需人工验证。
- 除上面这一项外，本文的结论**不会随代码变化自动失效或被纠正**：CI 跑的是单元测试、
  双 TFM 出包、XAML 资源体检、文案/文档一致性、无障碍静态检查与无界面自检，
  但**都不跑 UI Automation**，也没有 `AutomationProperties` 覆盖率断言。
  发现问题请按 §4 告诉我们。
- 版本基线：本文的核对基线是 **V2.9.6 的行为**，核对时点 **2026-10-05 晚（+08:00）**。
  无障碍相关的待办见
  [`NOBS/docs/OPTIMIZATION_PROPOSAL_2026-10.md`](NOBS/docs/OPTIMIZATION_PROPOSAL_2026-10.md) 的 C9 与 E4。
- **核对的时点很重要**：核对当时上游正在**并行实现无障碍改进**，所以「已知限制」里
  有好几条在你读到本文时**已经修好**。核对过程中实际撞到并已就地标注的有：
  ① 分段控件 `SegmentButton` 与下拉框的键盘焦点指示（新增焦点触发器）；
  ② 危险确认框的默认按钮与初始焦点从「确认」改成「取消」；
  ③ 轻提示（Toast）被声明为实时区域；
  ④ 系统级「高对比主题」开始被跟随（`HighContrastEffective`）；
  ⑤ 字号开始乘以系统「文本大小」系数（`SystemTextScaleFactor`）。
  其中 ④⑤ 是**本轮才出现的**，所以本文早期草稿曾把它们写成「不跟随」——
  现在以源码为准。任何条目若已过时，**以代码为准**，并欢迎按 §4 告知我们更新本文。

---

## English

> This document states **what we intend to do**, **what actually works today**,
> **what does not**, and **how to tell us about a barrier**.
>
> **We separate intent from reality.** Every item under "Known limitations" was checked against
> the source (the file and the observed behaviour are quoted). But that check is a
> **static reading of the code, not a test with assistive technology** — see §5.
> Please do not read §1 as "already done".

### 1. Our accessibility commitment

Four things we intend to deliver, and by which we accept being judged:

**① Core tasks can be completed with the keyboard alone.**
Core tasks: search the knowledge base (search / categories / problem detail), run the health checks
and read their conclusions, connect to OBS and use the console, one-click recording, read log
analysis, and change settings (language / theme / font size / high contrast / reduce motion).
The criterion is "can this path be walked without a mouse", not "are there shortcuts".

**② Status is never conveyed by colour alone.**
Success / warning / error / info must also carry a shape or a word. Status pills are therefore
**triple-encoded — icon + text + colour** (`StatusPill` in `Themes/Controls.xaml`: circle, check,
triangle, cross, and "i" vector glyphs). The connection badge shows words
(Connected / Connecting / Authenticating / Reconnecting / Failed / Disconnected) next to the
coloured dot. Colour-blind users, black-and-white printing, and high-contrast mode must not lose
information.

**③ We respect the system's and the user's visual preferences.**
Theme "Follow system" reads the Windows light/dark setting **and listens for live changes**
(`AppearanceService.IsSystemDark` + `SystemEvents.UserPreferenceChanged`). In-app we offer four
font-size steps, high contrast, and reduce motion. **Reduce motion is not decorative**: it zeroes the
shared `MotionDuration` resource that window transitions, toasts, onboarding and the plugins page
all reference.

**④ Key state is readable by screen readers.**
This is the one we are **worst** at, so it is written as an intent rather than a result:
today only WPF's default UI Automation for native controls (buttons, checkboxes, combo boxes,
list items) applies. Custom-drawn shapes and dynamically created cards still have no accessible
name and are not announced — the gaps are itemised in §3.

**Overall honest assessment**: keyboard operability, colour independence, and following the system
light/dark preference broadly hold, and the system **"text size" and high-contrast theme are now both
followed**. Screen-reader support, however, is still **only the native-control default layer plus one
live-region declaration** — we cannot claim the app "works with a screen reader". Accessibility was
added to our 2026-10 backlog as its own category (items C9 / E4: `AutomationProperties` coverage,
segment-control focus indication, high-DPI minimum width, system high-contrast theme) — and during
this review **several of those landed in the code** (segment/combo focus, destructive-confirm focus,
system high contrast, system text size). See §3 and §5.

### 2. Supported environments

| Dimension | Support | Source of truth |
|---|---|---|
| **OS (main build)** | Windows 10 / 11 (x64). Installer `MinVersion=10.0`. | `OBS_Helper_Setup.iss`, `Services/OsSupport.cs` |
| **OS (compat build)** | Windows 7 SP1 – 11 (`net6.0-windows`, assets suffixed `_win7`, `MinVersion=6.1sp1`). | `OBS_Helper.Wpf.csproj`, `.iss` |
| **High DPI** | **Per-Monitor V2** (`dpiAware=true/pm` + `dpiAwareness=PerMonitorV2, PerMonitor`). Mixed-DPI multi-monitor layouts are re-laid out, not bitmap-stretched. | `app.manifest` |
| **Window resizing** | Freely resizable; minimum 920 × 620 DIP. **See §3.5.** | `MainWindow.xaml` |
| **Theme** | Follow system / Light / Dark, applied live with no restart; plus 5 brand accent colours. | `AppearanceService`, Settings → "Appearance and accessibility" |
| **Font size** | Four steps: Small / Default / Large / Extra large (step factors 0.92 / 1.0 / 1.12 / **1.28**), applied live. The effective size is then **multiplied by** the Windows "Accessibility → Text size" factor (read from the registry key `HKCU\Software\Microsoft\Accessibility\TextScaleFactor`, clamped to 1.0–2.0), so it **does follow the system text-size setting**. | `AppearanceService.Apply`, `SystemTextScaleFactor` |
| **High contrast** | Yes (toggle in Settings → "Appearance and accessibility") **or** the Windows high-contrast theme — the effective value is the in-app toggle **OR** the system setting (`SystemParameters.HighContrast`), and system changes are observed (turning the system theme off restores the previous look without overwriting your own setting). It pushes body / muted text and lines to pure black or white and forces the content background back to the default. **Palette coverage is still partial — see §3.4.** | `AppearanceService.HighContrastEffective`, `Apply` |
| **Reduce motion** | Yes (same section). Zeroes the shared motion duration `MotionDuration`. **It honours only the in-app toggle, not the system preference — see §3.6.** | `AppearanceService`, `MotionDuration` call sites |
| **UI language** | Simplified Chinese / English, **switched live with no restart**; bundled offline content (knowledge base / templates / plugin catalog / troubleshooting guide) exists in both languages. First-run order: in-app choice > `language.ini` written by the installer > Simplified Chinese. Traditional Chinese and Japanese are not available. | `Localization/*`, `LocalizationService` |
| **Keyboard (in-app)** | `Ctrl+F` search, `Alt+←` or the mouse Back button to go back, `Esc` closes dialogs (`IsCancel`). Navigation items, buttons, checkboxes and combo boxes are standard controls and participate in tab order. **Remaining gaps in §3.2.** | `MainWindow.xaml`, `Themes/Controls.xaml` |
| **Global hotkeys (work outside the app)** | Five, defaulting to `Ctrl+Alt+R` record / `+S` stream / `+C` virtual camera / `+M` mini window / `+O` show-hide window. All are rebindable or can be disabled. | `Models/Shell/HotkeySettings.cs` |
| **Screen readers** | Only WPF's default announcement for native controls, **plus the toast being declared a live region**. That is the only `AutomationProperties` usage in the repo. **See §3.1.** | The `ToastCard` style in `Themes/Controls.xaml` |

> **One honest note about the Windows 7 compat build**: it installs and runs, and the offline
> knowledge-base half works. But the connection layer speaks obs-websocket **v5**, which ships with
> OBS 28 — and OBS 28 no longer supports Windows 7. So in **V2.9.6 and earlier**, on Windows 7
> **the "connect to OBS" half is unavailable to exactly the users who need it most**.
> (This boundary is being worked on: an obs-websocket **4.x legacy** compatibility core,
> `Services/Obs/ObsLegacyV4Core.cs`, is present in the working tree and is **enabled only in the
> Win7 compat build**. Whether it ships, and how much it covers, **depends on the released
> version** — this page makes no promise on its behalf.) Windows 7 is also **untested on real
> hardware** (we only verified "the target framework compiles and the installer ships the right
> `MinVersion`"), and Per-Monitor V2 requires Windows 10 1607+, so Windows 7 only gets system-level DPI.

### 3. Known limitations

> This is the section that matters most. Each item cites the source so you can verify it.
> **Accessibility work was landing in parallel while this was being checked**, so some items went from
> "broken" to "fixed" during the review — those are marked as fixed in place (with the new behaviour
> described). The rest may also be stale by the time you read this: **the code wins**, see §5.

#### 3.1 Screen readers: only the native-control default layer (plus one live region for toasts)

There is exactly **one** real use of `AutomationProperties` in the repo: the toast card
(the `ToastCard` style in `Themes/Controls.xaml`) declares
`AutomationProperties.LiveSetting = "Assertive"`, giving messages like "connected to OBS" or
"recording failed to start" a chance of being announced. `AutomationProperties.Name` and
`.HelpText` are still **unused everywhere**. Consequences:

**Fixed in V3.0 (third verification round, 2026-10-06)** — everything above that can be decided from
the code has been addressed:

1. `ToastService` now actively raises `AutomationEvents.LiveRegionChanged` (the earlier claim that
   "there is not a single `RaiseAutomationEvent` call in the repo" is outdated);
2. icon-only buttons got an explicit `AutomationProperties.Name` (the back button);
3. **the connection badge is now tab-focusable and activates on Space/Enter**, and it writes a
   **state-bearing** accessible name on every refresh (a screen reader can tell "connected" from
   "connection failed");
4. the six background-colour swatches in Settings were mouse-only; they are now focusable, show a
   visible focus ring, respond to Enter/Space and carry accessible names (keyboard users previously
   **could not change the background colour at all**);
5. a static checker, `NOBS/scripts/check_accessibility.py`, was added and wired into CI with three
   rules: text-less interactive controls must carry an accessible name; live regions must both
   declare `LiveSetting` **and** raise the event; and self-drawn elements used as buttons via mouse
   events must be focusable and keyboard-activatable.

**What we can claim**: standard controls (buttons, checkboxes, combo boxes, list items) get WPF's
built-in UI Automation support and their text is generally readable; toasts are live regions that
actively raise the event; text-less interactive controls all have accessible names (**guarded by
CI**, so they cannot silently regress).
**What we cannot claim**: that this app "supports screen readers" — it still has **not been tested
with NVDA or Narrator**.

- **Dynamically created cards are not announced.** Problem cards, diagnostic items, log findings,
  health-check results and the one-click recording card are all built at runtime with no accessible
  name and no live region, so a screen reader will not announce them when they appear or update.
  Users must tab through and explore.
- **Toasts are declared as a live region, but are not guaranteed to be announced — and still vanish
  quickly.** WPF does not announce anything just because `LiveSetting` is set: the app also has to
  raise `AutomationEvents.LiveRegionChanged`, and there is **no `RaiseAutomationEvent` call anywhere
  in the repo**. So whether a reader actually speaks the toast **depends on the reader and the WPF
  version and has not been tested** — do not treat it as working. The toast also auto-dismisses after
  2.5 seconds with **no close button and no history**, and messages you are supposed to *act on*
  (e.g. "recording failed to start") go through it as well.
- **Custom-drawn shapes have no accessible name.** The status-pill vector glyphs, the monitoring
  page's sparkline (`Controls/Sparkline.cs`) and the connection dot are drawn shapes. The sparkline
  in particular is an image with **no equivalent numeric table or text summary**.
- **The connection badge is mouse-only.** It is a `Border` with `MouseLeftButtonUp` — not a tab
  stop (it has neither `IsTabStop` nor `Focusable`), and it cannot be activated from the keyboard.
  (The functionality is still reachable by keyboard through the sidebar navigation to the console
  page, but the badge itself is not.)
- **There is no skip link**, so keyboard users tab through the sidebar on long pages.

**What we can say**: standard controls (buttons, checkboxes, combo boxes, list items) get WPF's
built-in UI Automation support and their text is generally announced, and toasts carry a live-region
declaration.
**What we cannot say**: that this app "works with a screen reader".

#### 3.2 Keyboard: the main gaps were fixed during this review, a few remain

- ~~**The segment control `SegmentButton` has no keyboard focus indicator**~~,
  ~~**combo boxes have no focus feedback**~~, ~~**destructive confirmations default to the
  affirmative button**~~ — **all three were fixed during this review**:
  `SegmentButton` now has an `IsKeyboardFocused` trigger (plus an `IsKeyboardFocused`
  `MultiTrigger`); `AppComboBox`'s inner `ComboToggle` binds a `DataTrigger` to
  `IsKeyboardFocusWithin`, and `AppComboBoxItem` gained an `IsKeyboardFocused` trigger;
  `ConfirmDialog.xaml.cs` now swaps both the default button and the initial focus to Cancel when
  `danger: true` (so Enter and `Esc` no longer perform the destructive action).
  `BaseButton` / `CardButton` / `NavTabButton` / `AppCheckBox` / `AppSwitch` already had one.
- **The mini control window is unusable from the keyboard.** It sets
  `ShowActivated="False"` (deliberate: the hotkey should not steal focus), which means focus stays in
  the other window and its buttons cannot be reached by keyboard.
- **The connection badge is mouse-only** (see §3.1): it is not a tab stop.
- **No access-key (Alt+letter) mnemonics.** The templates set `RecognizesAccessKey`, but no string
  uses the `_` prefix.
- **No custom tab order** (zero `TabIndex` hits); order follows the visual tree.
- **No skip link** (see §3.1).

> Per-control check of `Themes/Controls.xaml`: `BaseButton` / `CardButton` / `NavTabButton` /
> `SegmentButton` / `AppCheckBox` / `AppSwitch` / `AppComboBoxItem` all have keyboard-focus triggers;
> only `AppComboBox`'s own template has none (its focus feedback comes from the inner `ComboToggle`).

#### 3.3 Font size follows the Windows "text size" setting, but the in-app step caps at 1.28×

- The four in-app factors are 0.92 / 1.0 / 1.12 / **1.28** — **1.28× is the in-app ceiling**.
  The good news is that it now **follows Windows "Accessibility → Text size"**:
  the final factor is step × `SystemTextScaleFactor` (read from the registry key
  `HKCU\Software\Microsoft\Accessibility\TextScaleFactor`, clamped to 1.0–2.0), so at a 200% system
  setting the largest step reaches roughly 1.28 × 2.0 ≈ **2.56×**.
- A step **only changes font-size resources**; it does not scale icons or control heights
  (a deliberate trade-off: scaling the whole window makes icons and borders blurry). As a result,
  8 hard-coded decorative font sizes do **not** grow with the step — all of them fixed-size
  badges/glyphs/numbers: `Themes/Controls.xaml` (checkbox tick), `Controls/UpdateDialog.xaml`
  (celebration glyph), `Controls/ProblemCard.xaml` (favourite star), `Controls/ConfirmDialog.xaml`
  (warning glyph), `Views/HomePage.xaml` (category icon), plus `Views/ProblemPage.xaml.cs`
  (step-number badge), `Views/SetupPage.xaml.cs` (wizard icon) and `Views/TemplatePage.xaml.cs`
  (template icon). Body text is unaffected, but these are part of the "does not follow the step"
  surface.
- It is still **not a substitute for system magnification** (Windows Magnifier, full-page zoom).

#### 3.4 High contrast: the system theme is now followed, but the palette coverage is still partial

- **The system high-contrast theme is now followed**: the effective value `HighContrastEffective` is
  the in-app toggle **OR** `SystemParameters.HighContrast`, and system changes are observed;
  turning the system theme off restores the previous look, and **the user's own setting is never
  overwritten**.
- **But the semantic coverage is still partial**: high contrast only affects `TextBrush` /
  `MutedBrush` / `LineBrush` (pushed to pure black or white) and the content background (forced back
  to the default, so personalised backgrounds yield to readability). It does **not** change brand
  colours, the semantic colours (success / warning / error / info) or their soft backgrounds
  (`OkSoftBrush` / `DangerSoftBrush` / `WarnSoftBrush` / `InfoSoftBrush`), nor the per-category
  semantic colours. In other words, "high contrast" here **deepens text and lines and follows the
  system switch**; it is still not a complete high-contrast palette.

#### 3.5 At high DPI the minimum width can exceed the available logical width (computed, not verified on hardware)

`MainWindow.xaml` sets `MinWidth="920"` (DIP) and the main content area scrolls **vertically only**
(`VerticalScrollBarVisibility="Auto"`) with **no horizontal scrolling**. On a 1366 × 768 display:

| System scaling | Available logical width | vs. MinWidth = 920 |
|---|---|---|
| 100% | ≈1366 DIP | fits |
| 150% | ≈911 DIP | **below MinWidth** |
| 200% | ≈683 DIP | **well below MinWidth** |

So at 150% and above the window cannot be squeezed onto the screen and content on the right may fall
off-screen and be **unreachable** (there is no horizontal scrollbar). **This is derived from the
numbers, not verified on real hardware** — flagged honestly; measured results are welcome via §4.

#### 3.6 Reduce motion does not follow the system preference

The in-app "Reduce motion" toggle does zero `MotionDuration`, but it does **not** read the Windows
animation preference (`SystemParameters.ClientAreaAnimation` has zero hits). Turning animations off
in Windows will not change this app; it has to be switched on in-app.

#### 3.7 Content and language limits

- All UI strings and bundled content exist in Chinese and English, but the **English content has not
  been reviewed by a native speaker** (technically accurate and terminologically consistent, not
  publication-grade idiomatic). Traditional Chinese and Japanese are not supported.
- Knowledge-base entries and the troubleshooting guide are text content; screenshots and diagrams in
  them have **no alternative text**.
- ~~When content assets fall back to Chinese, the app only writes a log line and shows nothing in the
  UI~~ — **fixed in V3.0**: when an English content asset is missing, a **dismissible banner** appears
  at the top of the main window and **names the affected parts** (knowledge-base entries, plugin
  catalog, scene templates, troubleshooting guide); the banner clears itself once the asset is
  available. Headless self-test item 24 covers this wiring.

#### 3.8 Related behavioural limits (they affect people directly, so they are listed here)

- **"Connection failed" and "never connected" are indistinguishable in some places**: the generic
  failure branch sets the state to *Disconnected* rather than *Failed*, and only the *Failed* state
  puts the reason in the badge tooltip. So when OBS cannot be reached, the home page, the diagnostic
  page and the tray may show nothing at all. This is especially bad for users who rely on tray
  notifications or never open the console page.
- **Hotkey, mini-window and background-refresh failures are silent**: pressing a key that does
  nothing gives no reason. For heavy keyboard/hotkey users, "no news is good news" is the wrong
  mental model.

### 4. How to report an accessibility barrier

We treat accessibility issues as **bugs** and route them through the project's existing channels
(no new ones):

| Channel | Where | Best for |
|---|---|---|
| **GitHub issue** | [Open an issue](https://github.com/YYRMMAYO/OBS_Helper/issues/new/choose) (choose the bug report form) | Keyboard, focus, announcement and scaling problems that can be discussed publicly |
| **In-app "Help & Feedback" page** | Sidebar → Help & Feedback → feedback card | If you would rather not use GitHub; the card offers the official form, GitHub issues, an **offline QR code**, and **one-click copy of report material** |
| **Email** | **752139192@qq.com** | Anything you would rather not post publicly, or privacy / personal-safety / Code of Conduct matters. Chinese or English. |

**Please never attach unredacted logs or stream keys.**
Logs can contain stream URLs and stream keys, account passwords, real names, and the user name in
file paths. Remove them before submitting — this applies to **every** report, not only security ones.

**When reporting an accessibility barrier, please include as many of these as you can:**

1. **App version** (Settings → About) and **how it was installed** (installer / portable /
   `_win7` compat build);
2. **Windows version** (e.g. Windows 11 24H2), **display scaling**, and whether it is a mixed-DPI
   multi-monitor setup;
3. **Which assistive technology** you use — NVDA / Narrator / Magnifier / a system high-contrast
   theme / keyboard only / voice input — and its version;
4. **In-app settings**: font-size step, whether high contrast / reduce motion are on, UI language;
5. **The exact path**: "tabbed to control X → pressed Y → saw / did not see Z";
6. **Expected behaviour**: what you think should happen instead (e.g. "focus should have a visible
   outline");
7. If it is an announcement problem, say **what the screen reader actually said** — even if the
   answer is "nothing".

**Do not report security vulnerabilities in a public issue.** GitHub has no DMs — use the private
channels in [SECURITY.md](SECURITY.md) (the GitHub Security advisory form, or email marked
`[SECURITY]`).

### 5. How we verify (evidence boundaries)

- Every row in §2 comes from source or manifests we actually read:
  `app.manifest`, `OBS_Helper.Wpf.csproj`, `OBS_Helper_Setup.iss`, `AppearanceService.Apply`,
  `Views/SettingsPage.xaml`, `Themes/Controls.xaml`, `Models/Shell/HotkeySettings.cs`.
- §3 is a **static reading of the source** — wherever a keyword has zero hits, the feature is absent.
  It has **not** been tested with NVDA, Narrator or a high-contrast theme. The minimum-width issue in
  §3.5 is **derived arithmetically** and explicitly not verified on real hardware.
- **There is one accessibility automated check** (added in V3.0): `NOBS/scripts/check_accessibility.py`,
  wired into CI and runnable locally. It pins down the three things that *can* be decided statically:
  ① text-less interactive controls (icon buttons and the like) must carry an explicit
  `AutomationProperties.Name`; ② live regions must both declare `LiveSetting` **and** raise
  `LiveRegionChanged`; ③ self-drawn elements used as buttons via mouse events must be focusable and
  keyboard-activatable. It does **not** check (and does not pretend to check) how a screen reader
  actually reads the UI, high-contrast colour legibility, real-hardware scaling and multi-monitor
  behaviour, or whether the focus order makes sense — those still need human verification.
- Apart from that one check, the statements here will **not** be automatically invalidated or
  corrected when the code changes: CI runs unit tests, the dual-TFM build, the XAML resource check,
  string/doc consistency, the accessibility static check and the headless self-test, but **no UI
  Automation run** and no `AutomationProperties` coverage assertion. Please tell us via §4.
- Baseline for this review: the behaviour of **V2.9.6**, checked on the evening of
  **2026-10-05 (+08:00)**. The accessibility backlog lives in
  [`NOBS/docs/OPTIMIZATION_PROPOSAL_2026-10.md`](NOBS/docs/OPTIMIZATION_PROPOSAL_2026-10.md),
  items C9 and E4.
- **Timing matters**: accessibility work was **landing in parallel** while this was reviewed, so
  several items under "Known limitations" are already **fixed** by the time you read this. The ones
  that flipped during the review, and are marked as fixed in place, are:
  ① keyboard focus indication for the segment control `SegmentButton` and the combo boxes
  (focus triggers added); ② the destructive confirm dialog's default button and initial focus
  moving from Confirm to Cancel; ③ the toast being declared a live region;
  ④ the system high-contrast theme starting to be followed (`HighContrastEffective`);
  ⑤ font size starting to be multiplied by the system "text size" factor (`SystemTextScaleFactor`).
  Items ④ and ⑤ **appeared during this very review**, which is why an earlier draft of this page
  said they were not followed — the source is now the authority. For anything else that has gone
  stale, **the code wins** — and please tell us via §4 so we can update this page.
