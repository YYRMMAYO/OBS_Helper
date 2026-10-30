# OBS帮助助手 V3.3.0 发布说明

> **版本**：V3.3.0（minor，功能增强版）
> **基线**：V3.1.1（`master` 提交 `6b0b57f`）
> **构建**：`net10.0-windows`（Windows 10 / 11）+ `net6.0-windows`（Windows 7 SP1 兼容构建）
> **发布日期**：待人工测试通过后发布

---

## 一句话

V3.3.0 把「虚拟主播从零开播」串成一条可走通的路径：**绑定 Live2D 形象 → 套用直播间模板 → 按平台指引开播**；
同时把 **插件广场** 从「打开网页自己找」升级为「可信目录 + 可回滚安装 + 可管理」，把 **无障碍** 从几个开关升级为
**可整体切换、可组合、可验证**的「无障碍模式 / 老年模式」。

---

## 一、无障碍模式与老年模式

- 新增纯逻辑 `UiProfileCore`：把系统文本缩放 / 系统减少动画 / 应用开关 / 两个模式组合成一组**有效令牌**。
- 全局令牌落地：字号、导航宽度、导航项高度、控件最小高度、按钮最小尺寸、卡片内边距、焦点框粗细、行高、跳转链接。
- **系统设置是下限**：系统减少动画不能被应用关掉；系统文本缩放参与字号计算（1.0~2.0 钳制）。
- **预设只改默认值**：用户显式改过的密度 / 减少动画 / 布局优先于模式预设。
- 主窗口新增「跳到主内容」链接（无障碍 / 老年模式自动出现），键盘 / 读屏用户不必每次 Tab 穿过 13 个导航项。
- 设置页新增两个模式开关、当前生效摘要与「恢复默认」。
- 无障碍模式：字号至少 +15%、密度 ≥ 舒适、按钮 ≥ 44×44、焦点框 3 px。
- 老年模式：字号 ≥ 1.3×、宽大密度、按钮 ≥ 48×48、侧栏 ≥ 240、简化布局（功能仍可展开）。

> **边界**：NVDA / Narrator 仍需人工实测；自动化只覆盖模式组合、令牌数值与静态检查。

## 二、虚拟直播间（6 套模板 + 本地 overlay）

新增 6 套虚拟主播模板（中英双语资产）：

| id | 名称 | 画布 | 场景 |
|---|---|---|---|
| `vtuber-chat` | 虚拟主播 · 杂谈 | 1920×1080 30fps | 杂谈 / 全屏互动 / 暂离 |
| `vtuber-game` | 虚拟主播 · 游戏 | 1920×1080 60fps | 游戏 / 全屏游戏 / 暂离 |
| `vtuber-radio` | 虚拟主播 · 电台 | 1920×1080 30fps | 电台 / 点歌 / 暂离 |
| `vtuber-debut` | 虚拟主播 · 纪念回 | 1920×1080 30fps | 开播等待 / 主视觉 / 感谢 |
| `vtuber-vertical` | 虚拟主播 · 竖屏 | 1080×1920 30fps | 竖屏主画面 / 全屏 / 暂离 |
| `vtuber-triple` | 开播 / 暂离 / 下播三件套 | 1920×1080 30fps | Starting Soon / BRB / Ending |

- 模板 schema 向后兼容扩展：`tags` / `assetPack` / `minAppVersion`、来源级 `role` 与 `browser`。
- 新增本地 overlay 服务：**只绑回环、随机端口、随机路径 token、只读内置页面、CSP `default-src 'none'`**。
- overlay 页面：聊天框、装饰边框、BRB、Ending、倒计时（透明背景，无远程请求，不依赖任何 CDN）。
- 应用后检查清单：形象 / 聊天 / 音频 / 文字 / 边框 / 热键 / 开播前体检。
- 房间模板复用既有 `SceneTemplateService` 的落地链路，默认新建场景集合，不覆盖用户配置。
- 虚拟直播页使用全站统一的分段按钮 + Card / SoftPanel / 状态面板，替代默认 TabControl，视觉与其他页面一致。

> **边界**：不抓取平台弹幕、不内嵌账号 cookie；聊天框是占位页，URL 由用户自己替换。

## 三、Live2D 一键配置（VTube Studio）

- 模型校验（纯逻辑）：解析 `.model3.json`，拒绝目录穿越 / 绝对路径 / 符号链接 / 可执行文件 / 超限目录。
- 目录清单 SHA-256：`<slug>-<hash8>` 可复现；模型文件只读，不修改用户原文件。
- VTube Studio 公开 WebSocket API：连接、鉴权（首次授权 / token 复用）、模型列表、加载模型、模型目录查询。
- 采集链路：**NDI → Spout2 → VTS 虚拟摄像头（DirectShow）→ 窗口采集**，按 OBS 实际可用输入类型选择。
- OBS 落地：新建 / 复用「虚拟形象」场景、创建采集源、按布局预设计算变换、麦克风单独一路（避免双声）。
- 布局预设：全身居中 / 半身底部 / 右下角 / 左侧三分之一，横竖屏都按画布比例计算。
- 失败全部映射为稳定错误码（`L2D_*`），界面给下一步。

> **边界**：需要用户已安装 VTube Studio 并在其窗口内授权；不注入进程、不读内存、不自带任何模型；模型必须先放进 VTS 模型目录（界面会显示 `VTSFolderInfoRequest` 返回的真实目录）。

## 四、开播引导（国内 + 国外平台）

- 平台目录 `platforms.json` / `platforms.en-US.json`：16 个平台，含区域、模式（直推 / 虚拟摄像头 / 仅图文）、官方入口、核对日期。
- 直推：`SetStreamServiceSettings`（`rtmp_custom` + server/key）→ `GetStreamServiceSettings` 复核 → `StartStream` → 轮询 `GetStreamStatus` 确认。
- 虚拟摄像头：启动 → 轮询 `outputActive`；平台侧给图文 / 官方帮助入口。
- 推流码：只进内存；界面只显示长度遮罩（不透露任何原字符）；可选「记住」才进 `secrets.dat`（DPAPI + AES-GCM）。
- 第三方客户端：进程 / 注册表 / 安装目录三路只读探测，不自动安装、不点「开始直播」、不登录账号。
- Win7 兼容构建不做 `SetStreamServiceSettings` 假支持，明确走手动填写。

> **边界**：抖音等平台不提供通用推流码，走「直播伴侣 + OBS 虚拟摄像头」；平台入口会变，目录条目带核对日期，未核验的条目不承诺一键成功。

## 五、插件广场：一键下载 / 安装 / 管理 + GitHub 导入

- 插件事务安装：下载 → SHA-256 校验 → 安全解压到 staging → 校验 x64 / 托管 DLL → 备份旧版本 → 替换 → 写清单。
- 安全边界：只允许 GitHub 官方 HTTPS 域、200 MB 上限、拒绝 zip slip / 符号链接 / 可执行脚本、OBS 运行中拒绝替换。
- 可回滚：更新 / 卸载前自动备份，保留最近 2 份；卸载只移入备份，不硬删。
- 已安装管理：版本、路径、回滚、卸载；**只管理本应用装过的插件**。
- GitHub 导入：`owner/repo` 或仓库 URL → 读取最新 Release → 自动挑选 Windows x64 `.zip` → 强警告 + 用户确认 → 同一套事务安装。
- GitHub 原生搜索：应用内搜索 `topic:obs-plugin`，结果可直接查看最新 Release 并安装；API 查询带 5/10 分钟缓存与 60 秒限流冷却。
- 插件目录一键更新：走与知识库相同的热更新通道，检查到新版本后立即重建列表。
- 插件管理冒烟测试：`NOBS/tools/PluginSmoke` 在无 OBS、无网络环境下验证安装 / 覆盖 / 回滚 / 卸载 / OBS 运行中拒绝 / zip slip / x86 / 哈希不匹配（17 项全过）。

> **边界**：精选目录之外的插件一律视为未验证第三方代码；不静默安装、不静默提权、不自动禁用杀软。

---

## 六、安全与合规

- 本地 HTTP overlay：回环 + 随机端口 + 随机 token + CSP + 无远程请求。
- zip / 路径：统一 `IsSafeEntryPath`，拒绝绝对路径、`..`、盘符、控制字符、Windows 尾部空格 / 点。
- PE 架构：x64 或托管 AnyCPU 才接受；原生 x86 拒绝。
- 密钥：推流码 / VTS token 不落日志；界面只显示遮罩；诊断与 AI 请求不包含。
- 第三方软件：只从官方域名下载；安装器由用户自己运行；不静默、不 `/S`、不替用户接受协议。

## 七、开源署名

V3.3.0 起在 `docs/OPEN_SOURCE_ATTRIBUTION.md`、README 与软件「关于」界面列出所用开源项目、许可证与用途边界：

- obs-websocket（GPL-2.0）—— 只按公开协议自行实现客户端，不复制代码；
- VTube Studio API（MIT）—— 只使用公开协议；
- 以及其他仅作外部可选软件 / 插件引导的项目（DistroAV、Spout2、LocalVocal、Watt Toolkit 等）。

> 主程序仍是 MIT；GPL 代码不进主程序，不捆绑、不复制。

## 八、验证与未验证项

已验证：
- 双 TFM 编译通过；单元测试全绿；`check_resources.py` / `check_accessibility.py` / `check_docs.py` 通过；
- 模板 / 平台 / 文案资产的中英一致性与结构校验（单测）；
- zip 安全、zip slip、符号链接、PE 架构、路径穿越（单测）。

需要人工测试：
- 真实 OBS 下的 Live2D 采集（NDI / Spout / 虚拟摄像头 / 窗口）；
- 真实 VTS 授权与模型加载；
- B 站 / YouTube / Twitch 等直推（需要账号与推流码）；
- 抖音 / B 站直播姬的虚拟摄像头路径；
- 真实插件的安装 / 更新 / 卸载 / 回滚；
- NVDA / Narrator、150% / 200% DPI、老年模式实测。

## 九、构建

```powershell
dotnet build OBS_Helper.Wpf/OBS_Helper.Wpf.csproj -f net10.0-windows
dotnet test  OBS_Helper.Wpf.Tests/OBS_Helper.Wpf.Tests.csproj
python scripts/check_resources.py
python scripts/check_accessibility.py
python scripts/check_docs.py
.\build.ps1 -SkipLegacy   # 主构建安装包 + 便携包 + 增量包
```
