# X1031Y3 功能中心「前置条件一键化」技术开发指引

> 版本：`X1031Y3`（内部版本 `1031.3.0`，2026-10-31）
> 范围：功能中心全部「需要前置条件」的能力；目标是让用户尽量不自己找 exe、解压、填路径。
> 关联：`11_X1031Y2_PLUGIN_MANAGEMENT.md`、`04_PLUGIN_ACCELERATOR_AND_MANAGED_LIST.md`

## 1. 问题重述

功能中心里有一批能力不是「打开即用」，而是依赖外部组件或用户自有素材：

| 功能 | 原前置条件 | 用户当前体感 |
| --- | --- | --- |
| 手机只读监看 / Tally | 电脑连 OBS、手机同局域网、停留页面 | 能用，但预览失败时只看到空白 |
| 手机当 Wi-Fi 摄像头 | 安装 MediaMTX、防火墙放行、私网 IP | 页面能显示，但手机推不上来 / OBS 没画面 |
| 实时字幕 | 安装 whisper.cpp、准备模型 | 要自己找 exe、找模型、填两个路径 |
| 录制转 MP4 / 归档 / 转推 | FFmpeg / rclone / MediaMTX；转推还要自备服务 | 要自己找 exe、填路径；不知道缺什么 |
| 虚拟直播（Live2D） | 自备 Live2D 模型、VTube Studio（可选） | 没模型时只有场景骨架，不知道为什么 |
| AI 诊断 | 本地规则默认可用；云端需 API Key | 不清楚当前用的是本地还是云端、额度剩多少 |
| 手机配对 | 同一局域网、最多 5 台设备 | 第 6 台只显示「配对失败」，不知道原因 |

核心矛盾：**软件依赖（可自动下载的）和用户素材 / 外部服务（不可自动安装的）混在一起，用户无法判断「我到底缺什么、下一步点哪里」。**

## 2. 结论：哪些能一键，哪些不能

### 2.1 能做成「一键安装 + 自动接入」

| 组件 | 来源 | 安装方式 | 自动接入 |
| --- | --- | --- | --- |
| FFmpeg | BtbN/FFmpeg-Builds 最新 Release | `DependencyInstallService` 下载 zip、校验 digest、安全解压 | 写入 `dep.ffmpeg.path`，字幕/切片/转推页自动填入 |
| rclone | rclone 最新 Release | 同上 | 写入 `dep.rclone.path`，归档页自动填入 |
| whisper.cpp | ggml-org/whisper.cpp 最新 Release | 同上 | 写入 `dep.whisper.path`，字幕页自动填入 |
| whisper 模型 | Hugging Face `ggerganov/whisper.cpp` | 固定 URL + 固定 SHA-256 + 体积上限 | 落到 `%LocalAppData%\OBS_Helper\sidecars\models\ggml-base.bin`，字幕页自动填入 |
| MediaMTX | bluenviron/mediamtx 最新 Release | 现有 sidecar 一键安装 | 摄像头链路直接使用；补「检测推流」和防火墙指引 |

### 2.2 不能替用户安装，但可以做到「明确指引 + 一键跳转 / 检测」

| 前置条件 | 原因 | Y3 做法 |
| --- | --- | --- |
| Live2D 模型 | 用户自有版权素材，不能静默下载 | 「获取模型」官方入口 + 「先用场景骨架」入口；没有模型时不再只显示灰色按钮 |
| VTube Studio | 第三方闭源软件，可选 | 明确「可选」；未安装时走窗口/摄像头采集，不阻断 |
| 云端 AI API Key | 用户账号与计费 | 默认本地引擎；设置页显示免费额度与「本地/云端」当前状态；云端失败自动回退本地 |
| 多平台转推目标 | 需要用户自己的 RTMP/SRT 服务或中转 | 保留「自备转发服务」说明；页面给出目标地址格式校验与测试按钮 |
| 手机推流 App | 浏览器 HTTP 页面无法调用 `getUserMedia`（非安全上下文） | 保留 Larix Broadcaster / OBS Camera 指引；增加推流二维码与「检测手机是否推上来」 |
| 防火墙 | 需要管理员权限 | 提供「一键放行 MediaMTX」按钮（UAC 提权，仅对 sidecar 目录内的 mediamtx.exe 加私网入站规则）；失败时给出手动命令 |

## 3. 架构：依赖准备器

### 3.1 新增 `DependencyProvisioner`

位置：`Services/Dependencies/DependencyProvisioner.cs`

职责：把「下载 → 校验 → 解压 → 配置路径」收成一个可复用动作，供功能页调用。

```csharp
public sealed record DependencyProvisionResult(bool Ok, string Code, string Path, bool DigestVerified);

public sealed class DependencyProvisioner
{
    public Task<DependencyProvisionResult> EnsureAsync(
        DependencySpec spec,
        IProgress<(long Done, long Total)>? progress,
        CancellationToken ct);
}
```

行为：

1. `DependencyService.ResolvePath(spec)` 已有可用路径 → 直接返回，不重复下载；
2. 否则调用 `DependencyInstallService.InstallAsync`；
3. 成功后调用 `DependencyService.Configure` 写入设置并立即生效；
4. 任何一步失败返回稳定错误码，由页面显示「下一步」。

`PluginsPage` 的依赖中心、`InteractionPage`、`StreamPage` 共用这一个入口，避免三处各写一套。

### 3.2 新增 `WhisperModelService`

位置：`Services/Subtitles/WhisperModelService.cs`

固定参数：

| 项 | 值 |
| --- | --- |
| 文件名 | `ggml-base.bin` |
| URL | `https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin` |
| 大小 | `147951465` 字节 |
| SHA-256 | `60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe` |
| 落点 | `%LocalAppData%\OBS_Helper\sidecars\models\ggml-base.bin` |

安全边界：

- URL 是代码常量，不接受用户输入；
- 只允许 HTTPS + `huggingface.co` / `cdn-lfs.huggingface.co`（重定向域名）；
- 下载中边写边算 SHA-256，超过 200 MB 立即中止；
- 校验不通过删除临时文件，绝不覆盖已有可用模型；
- 不使用通用依赖白名单，避免把 Hugging Face 变成任意下载通道。

### 3.3 功能页接入

| 页面 | 新增动作 | 自动结果 |
| --- | --- | --- |
| `InteractionPage` 字幕区 | 「一键安装 whisper」「下载推荐模型」 | 填入 `WhisperBox` / `ModelBox`，清空错误提示 |
| `InteractionPage` 切片区 | 「一键安装 FFmpeg」 | 填入 `FfmpegBox`，切片按钮可用 |
| `StreamPage` 转推区 | 「一键安装 FFmpeg」 | 填入 `RelayFfmpegBox`，设置 `Relay.FfmpegPath` |
| `StreamPage` 归档区 | 「一键安装 rclone」 | 填入 `RcloneBox`，设置 `ArchiveUpload.RclonePath` |
| `MobileLinkPage` 摄像头区 | 保留「下载并安装 MediaMTX」；新增「检测手机推流」「复制推流地址」「推流二维码」「一键放行防火墙」 | 显示手机是否真的推上来；二维码可直接扫进 Larix |
| `FeatureHubPage` | 新增「运行环境准备」卡片：列出 5 个组件的状态与一键按钮 | 用户不用先找依赖中心 |

### 3.4 手机摄像头「显示但不可用」的修复

现状：页面只显示 SRT/RTMP 地址，手机端要自己装 Larix / OBS Camera 并手动抄地址；防火墙未放行时，页面仍然显示「等待手机推流」，用户无法判断卡在哪。

Y3 改动：

1. **推流二维码**：用现有 `QrCodeCore` 把 SRT 地址生成二维码，手机扫码即可导入 Larix；同时保留「复制」按钮。
2. **检测手机推流**：查询 MediaMTX 本地 API `http://127.0.0.1:<apiPort>/v3/paths/list`，判断 `live/<key>` 是否 `ready`；页面上显示「手机已推流 / 尚未收到手机推流」。
3. **一键放行防火墙**：对 sidecar 目录内的 `mediamtx.exe` 添加私网入站规则；UAC 提权，失败时回退显示手动 `netsh` 命令。规则只针对该 exe，不改全局端口。
4. **状态机**：未安装 → 已安装未启动 → 已启动未收到推流 → 已收到推流 → OBS 已绑定，五种状态分别给下一步，不再笼统显示「等待」。

### 3.5 监看：FPS 截图 vs 视频流

现状：手机监看走 `GetSourceScreenshot`，1~5 FPS，不是视频流；优点是实现简单、CPU/带宽可控，缺点是运动画面卡顿。

真视频流可行方案：

| 方案 | 延迟 | 依赖 | 结论 |
| --- | --- | --- | --- |
| OBS WHIP → MediaMTX → 手机 WebRTC/WHEP | 0.3~1 s | OBS 30.2+、MediaMTX WebRTC、HTTPS/安全上下文 | 可行，但涉及 OBS 输出配置、证书与播放器，工作量大，放 Y4 |
| MediaMTX HLS / LL-HLS | 2~8 s | MediaMTX + OBS 推流到本地 | 可行，延迟不适合互动，适合监看 |
| 截图轮询（现状） | 0.2~1 s/帧 | 无 | 保留为默认，低配机器友好 |

Y3 结论：**不把「真视频流」作为 Y3 的硬承诺**；先把截图监看的失败提示、冷却和「为什么卡」讲清楚。文档保留 WHIP 方案作为 Y4 预研。

## 4. 用户路径（验收视角）

### 4.1 第一次用实时字幕

1. 打开「直播互动」→ 字幕区看到「whisper.cpp 未安装」；
2. 点「一键安装 whisper」→ 进度 → 自动填入 exe 路径；
3. 点「下载推荐模型」→ 进度 → 自动填入模型路径；
4. 点「开始字幕」→ 直接出字；不需要打开资源管理器。

### 4.2 第一次用手机摄像头

1. 功能中心 → 手机当摄像头 → 看到「MediaMTX 未安装」；
2. 点「下载并安装」→ 完成后点「启动摄像头链路」；
3. 页面出现推流二维码 + SRT/RTMP 地址；
4. 手机扫码导入 Larix → 开始推流；
5. 页面「检测手机推流」变成「已收到手机推流」；OBS 自动出现「手机摄像头 cam1」来源；
6. 如果卡在「未收到推流」→ 点「一键放行防火墙」或按提示手动放行。

### 4.3 第一次用归档 / 转推

1. 画质与分发页 → 归档区显示「rclone 未配置」；
2. 点「一键安装 rclone」→ 自动填路径；
3. 填写远端名 → 验证 → 归档；不需要自己解压。

## 5. 开发拆分与优先级

| 优先级 | 工作 | 文件 | 验收 |
| --- | --- | --- | --- |
| P0 | `DependencyProvisioner` + 依赖中心复用 | `Services/Dependencies/*`、`AppServices.cs`、`PluginsPage.xaml.cs` | 现有依赖中心行为不变；单测/构建通过 |
| P0 | whisper / FFmpeg / rclone 一键按钮与自动填入 | `InteractionPage.xaml(.cs)`、`StreamPage.xaml(.cs)` | 点一次后路径自动出现，重启后仍保留 |
| P0 | whisper 模型下载 + SHA-256 | `WhisperModelService.cs` + 文案 | 下载后模型路径自动填入；篡改文件被拒 |
| P0 | 手机摄像头推流二维码 + 检测 + 防火墙 | `MobileLinkPage.xaml(.cs)`、`MediaServerManager`、`FirewallHelper` | 能区分「未推流 / 已推流 / OBS 未绑定」 |
| P1 | 功能中心「运行环境准备」卡 | `FeatureHubPage.xaml(.cs)` | 5 个组件状态一目了然，可一键安装 |
| P1 | 手机配对第 6 台上限提示 | 手机页 JS + 文案 | 手机端显示「最多 5 台设备」而不是笼统失败 |
| P2 | 真视频流（WHIP/WHEP）预研 | 文档 + 技术验证 | 不作为 Y3 交付门槛 |

## 6. 安全与合规

- 只从 GitHub 官方 Release 资产域 / Hugging Face 固定模型 URL 下载；
- 资产重定向后的最终域名必须在白名单内（Y2 已补 `release-assets.githubusercontent.com`）；
- whisper 模型固定 SHA-256，校验失败不落盘；
- 防火墙规则只允许 sidecar 目录内的 `mediamtx.exe`，只对私网生效，不开放任意端口；
- 不静默安装用户素材（Live2D 模型、VTS、API Key）；
- 所有下载失败写日志（host / HTTP 状态 / 体积上限），不再静默返回。

## 7. 测试与自检

- 单测：host 白名单、`WhisperModelService` 的路径 / 大小 / 哈希校验、`DependencyProvisioner` 的「已就绪不重复下载」分支；
- 集成：真实下载 `exeldro/obs-move-transition` 插件 + MediaMTX 依赖（验证 302 → `release-assets.githubusercontent.com`）；
- 三轮：全量单测 → 双 TFM publish + `OBS_SELFTEST=1` 36 项 → 真实下载 + 资源 / 无障碍 / 文档检查；
- 手动：字幕「安装 → 下载模型 → 开始」、摄像头「安装 → 启动 → 扫码 → 检测」、归档「安装 rclone → 验证」。

## 8. 已知边界

- 手机浏览器的 `getUserMedia` 在 `http://192.168.x.x` 不可用，Y3 不承诺「手机网页直接当摄像头」；仍需 Larix / OBS Camera 推流。
- Live2D 模型、云端 API Key、转推服务无法替用户准备，只能给入口与状态。
- 真视频流监看是 Y4 预研，不在 Y3 交付范围。
- Hugging Face 模型下载受网络影响；失败时保留「选择已下载模型」手动路径。

## 9. 宣传 vs 实现对照审查（2026-10-31）

> 审查方式：以 `FeatureCatalog.Modules` 为清单，逐项对照页面、服务与资产文件；
> 下表是**代码级初审**，标注了证据路径。未做真机 / 真网络复现的项在「备注」里写「未验证」，
> 不能把本表当成验收报告。

### 9.1 真实的（可以继续宣传）

手机监看/Tally、MediaMTX 摄像头链路、Live2D 一键配置、AI 诊断三通道回退、多平台转推、
FFmpeg 切片/竖屏、rclone 归档、OBS 配置备份、插件广场/依赖中心、日志分析、场景模板等，
都有实际服务与页面承载，不是占位页。证据见 `MobileServices.cs`、`Live2DSetupService.cs`、
`DiagnosticOrchestrator.cs`、`RelayService.cs`、`ArchiveService.cs`、`ObsBackupService.cs`、
`PluginInstallService.cs`、`ObsLogAnalyzer.cs`。

### 9.2 宣传强于实现 / 需要补实现或改文案

| 功能 | 初审结论 | 证据 | Y3 处理 |
| --- | --- | --- | --- |
| M04 手机麦克风 | **缺失**：没有 `/api/mic` 端点、没有页面 Tab、没有 `mobile.mic` 文案 | `MobileLinkServer.cs` 路由表；`MobilePageAssets.cs` Tab 列表 | 从功能中心移除或标「未实现」；Y4 再补 |
| C07 AI 回复建议 | 名为 AI，实际是无引擎时的 3 条固定模板 | `ChatCore.cs:817-853`；`InteractionPage.xaml.cs:391-403` | 文案改为「回复建议（模板）」；接入免费 AI 后再叫 AI |
| S04 AI 搭档 | SAPI 念稿 + VTS 口型，没有 LLM 对话 | `AutomationCore.cs` 的 `AiHostCore` | 文案改为「语音搭档」；LLM 列入 Y4 |
| S09 AI 导播 | 规则查表，上下文由用户手敲，不是画面/焦点识别 | `ClipsCore.cs:159-187`；`AutomationPage.xaml.cs:397-409` | 文案改为「规则导播」；自动识别列入 Y4 |
| E03 装修市场 | 没有市场目录 / 下载，只有用户粘贴 JSON | `AutomationPage.xaml.cs:121-163` | 文案改为「主题包导入」；市场列入 Y4 |
| E04 团队协作 | 内存角色/邀请/审计，没有网络与真实权限 | `AutomationCore.cs:265-320`；`AutomationPage.xaml.cs:331-381` | 标「本地演练」；联机协作列入 Y4 |
| Q05 自动归档 | 只有「执行归档」按钮，录制结束不会自动触发 | `StreamPage.xaml.cs:361-379`；全仓 `ArchiveUpload` 调用点 | Y3 增加录制结束自动触发（需用户显式开启） |
| Q04 构图检查 | 5 个滑块 + 公式，没有画面分析 | `StreamPage.xaml:33-44`；`StreamPage.xaml.cs:135-143` | 文案改为「构图参考」；画面分析列入 Y4 |
| S07 封面文案 | 模板串 + 禁用词，不是智能生成 | `InsightsCore.cs:78-107` | 文案改为「封面文案模板」 |
| D02 AI 助手 | 关键词检索，不是对话式 AI | `AssistantService.cs:14-61` | 文案改为「助手（本地检索）」 |
| C08 / S02 翻译 / 双语字幕 | 没有注入翻译引擎时走 22 词本地表 | `ChatCore.cs:771-775,944-963`；`InteractionPage.xaml.cs:418-419` | 文案标「本地短语表」；引擎接入列入 Y4 |
| S05 高光切片 | 只按弹幕密度，音频能量/场景变化硬编码为 0/false | `InteractionPage.xaml.cs:510-534` | 标「弹幕密度切片」；多信号列入 Y4 |
| S10 录制检索 | 只索引本次会话字幕，不索引历史录像 | `InteractionPage.xaml.cs:467-477` | 文案改为「本次会话检索」 |
| C06 观众投稿 | 只有主播在 PC 手打，没有观众侧入口 | `InteractionPage.xaml.cs:300-308` | 标「主播代录」；观众入口列入 Y4 |
| M10 远程嘉宾 | 只做房间/权限，`MediaPathAvailable=false`，不能真推流 | `RemoteGuestService.cs:41-42` | 标「房间/权限（媒体未启用）」 |
| C10 直播日历 | 内存 List，重启丢，没有平台同步 | `InteractionPage.xaml.cs:21,360-389` | 落盘 + 导出；平台同步列入 Y4 |
| E06 观众看板 | 内存统计，重启丢；观众数只来自 B 站人气，Twitch/YouTube 恒 0 | `InsightsCore.cs:110-184`；`AppServices.cs:413-416` | 落盘；多平台人气列入 Y4；文案已注明 B 站 |
| 功能中心状态 | 只渲染模块卡片，没有逐功能「已实现/实验性/未实现」状态 | `FeatureHubPage.xaml.cs:95-101` | Y3 增加状态位与「实验性」标签 |

### 9.3 处理原则

1. **能马上补的**：Q05 自动归档、C10/E06 落盘、功能中心状态位、手机配对第 6 台提示 —— 放 Y3。
2. **不能马上补的**：LLM 对话、真 AI 导播、装修市场、联机协作、多平台人气、历史录像索引 —— 放 Y4，
   本轮先把文案改成与实现一致，避免「宣传了但做不到」。
3. **不能替用户准备的**：Live2D 模型、API Key、转推服务、手机推流 App —— 保留明确入口与兜底，
   不写成「开箱即用」。

## 10. 技术路线与备用方案

| 场景 | 主路线 | 失败兜底 |
| --- | --- | --- |
| 依赖下载 | GitHub API 最新 Release → 资产 302 → `release-assets.githubusercontent.com` → 校验 → 解压 | 允许 302 最终域；失败写 host/HTTP 日志；保留「下载页」「选择已下载文件」 |
| whisper 模型 | Hugging Face 固定 URL + 固定 SHA-256 + 200 MB 上限 | 失败保留手动选择模型；不覆盖已有模型 |
| 手机摄像头 | MediaMTX 本地 SRT/RTMP 收流，OBS 回环 RTSP 拉流 | 未安装→一键安装；未推流→检测 API；防火墙→一键规则/手动命令；浏览器 HTTP 不能 `getUserMedia`→保留 Larix 指引 |
| 手机监看 | `GetSourceScreenshot` 1~5 FPS + 3 次失败冷却 10 秒 | 缓存帧 + `MOB_PREVIEW_FAILED`；真视频流 WHIP 列 Y4 |
| 转推 | MediaMTX 收流 + 每目标一条 FFmpeg `-c copy` | 目标不可达→错误码；自备 RTMP/SRT 服务仍是用户前置 |
| 归档 | rclone copy + 校验 + 可撤销 | 未配置→`ARCHIVE_REMOTE_MISSING`；Y3 增加录制结束自动触发（可选） |
| Live2D | VTS WebSocket + OBS 场景骨架 | 无模型→获取模型入口/场景骨架；VTS 可选 |
| AI 诊断 | 本地规则 → 免费通道 → 云端 Key，任一失败回退本地 | 本地永远可用；额度/Key 状态在设置页显示 |

## 11. 用户路径审查（假如我是用户）

| 目标 | 当前能否完成 | 卡点 | Y3 改动 |
| --- | --- | --- | --- |
| 用手机看节目画面 + Tally | 能 | 需要同网段、OBS 已连接；失败只看到空白 | 保留冷却；页面显示「预览失败，检查 OBS 输出」 |
| 用手机当摄像头 | 显示但常卡住 | 要自己装 Larix、抄地址、放行防火墙 | 一键装 MediaMTX + 二维码 + 检测推流 + 防火墙按钮 |
| 开实时字幕 | 不能一步完成 | 要自己找 whisper.exe 和模型 | 一键装 whisper + 一键下模型 + 自动填路径 |
| 录完转 MP4/归档 | 能，但路径要手填 | FFmpeg/rclone 要自己找 | 一键装 FFmpeg/rclone + 自动填路径 |
| 多平台转推 | 能，但要有转发服务 | 自备 RTMP/SRT 目标 | 保留目标格式校验；文档说明「自备服务」 |
| 虚拟直播 | 能，但要有模型 | Live2D 模型是用户素材 | 获取模型入口 + 场景骨架兜底 |
| AI 诊断 | 能 | 云端要 Key，额度有限 | 默认本地；设置页显示额度/Key 状态 |
| 第 6 台手机配对 | 不能 | 只显示「配对失败」 | 手机端显示「最多 5 台设备」 |

## 12. Y3 交付边界与 Y4 待办

**Y3 必做（本版）**：
- Y2 下载白名单修复（`release-assets.githubusercontent.com`）与日志；
- `DependencyProvisioner` + whisper/FFmpeg/rclone 一键安装与自动填入；
- whisper 模型固定 SHA-256 下载；
- 手机摄像头二维码、推流检测、防火墙指引/一键规则；
- 手机配对第 6 台提示；
- 功能中心「运行环境准备」卡与「实验性/未实现」状态位；
- 文案修正：M04、C07、S04、S09、E03、E04、Q04、Q05、S07、D02、C08/S02、S05、S10、C06、M10、C10、E06。

**Y4 并入 Y3 的落地结果（2026-10-31 复核）**：

| 能力 | 状态 | 说明 / 边界 |
| --- | --- | --- |
| 真视频流监看（OBS WHIP → MediaMTX → WHEP） | ✅ 接收侧已落地 | 摄像头链路启用 MediaMTX WebRTC，页面给出视频监看地址与 WHIP 推流地址；OBS 需 30.2+ 并手动配置 WHIP 输出，浏览器/网络需支持 WebRTC |
| 历史录像索引 | ✅ 文件级已落地 | 「扫描历史录像」按修改时间列出录像目录中的 mp4/mkv/mov/flv/ts；只做文件索引，不读取视频内容，不做历史字幕检索 |
| 主题市场 | ◐ 本地导入 | 只有 JSON 主题包导入/校验，没有在线目录、下载与作者体系 |
| 团队协作 | ◐ 本地演练 | 角色/邀请/审计在本机内存中；没有联机、账号、服务端权限 |
| 多平台观众统计 | ◐ B 站 + 手动 | 自动人气仅 B 站公开接口；Twitch/YouTube 需要外部 OAuth/API，应用不内置 |
| LLM 回复/搭档、真 AI 导播 | ✗ 未接入 LLM | 当前分别是本地模板 / 规则查表；接入云端需要 API Key、计费与提示词工程 |
| 手机浏览器直接当摄像头 | ✗ 不支持 | `getUserMedia` 需要可信 HTTPS 安全上下文；局域网 HTTP 下不可用，仍建议 Larix / OBS Camera |

## 13. 边界声明

- LLM 回复 / 搭档、真 AI 导播、在线主题市场、联机团队协作等需要应用层开发或自建服务端的能力，不提供接入步骤；可自行完成的配置项（Twitch / YouTube 观众数手动回填、本地 JSON 主题包、OBS WHIP → MediaMTX 视频监看）见根 README 的「自行接入指引」。
- 未落地项与已知边界见 README / 发布说明；不把「可以接」写成「已经接」，不把「需要外部服务」写成「开箱即用」。
- 历史录像索引仅做文件级扫描，不读取视频内容，不做历史字幕/语音检索。
