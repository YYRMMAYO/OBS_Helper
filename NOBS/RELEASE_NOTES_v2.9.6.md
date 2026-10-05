# V2.9.6 发布说明 —— 更名「OBS帮助助手」· 图标统一 · 文档同步

> **一句话**：这一版不新增功能，做的是把「这个软件是谁」讲清楚 —— **名字、图标、文档**
> 三处对齐。对老用户最要紧的一句话是：**换成新名字，还是原来那个软件**，
> 升级路径、安装目录、数据目录、增量更新全都没动。

- 上一版：[`RELEASE_NOTES_v2.9.5.md`](RELEASE_NOTES_v2.9.5.md)
- 双构建：主构建（Windows 10 / 11，`net10.0-windows`）与 Win7 兼容构建（Windows 7 SP1+，`net6.0-windows`）

---

## 1. 更名：OBS 排障助手 → OBS帮助助手

### 改了什么

| 位置 | 说明 |
| --- | --- |
| 窗口标题 / 顶栏 | 文案表 `app.name`（中文侧 `OBS帮助助手`，英文侧仍是 `OBS Helper`） |
| 托盘 | ToolTip、通知标题、最小化提示 |
| 首启与引导 | 欢迎标题、新手引导文案 |
| 诊断报告 | 报告页脚署名（「由 OBS帮助助手生成」） |
| 反馈材料 | 一键复制的报错材料抬头 |
| 安装向导 | 快捷方式名、卸载项显示名、完成页「安装完成后启动 …」（中英各一套） |
| 插件广场 | 官方 Dock 插件条目 → 「OBS帮助助手（Dock 版）」 |
| AI 系统提示词 | 角色描述与产品名解耦（「诊断助手」），不再把「排障」这个动作词当产品名 |

### 刻意**没**改什么（改名最容易顺手改坏的地方）

| 保持不变 | 为什么 |
| --- | --- |
| `AppId`（`{4C9F2D18-…}`） | Inno 靠它把新安装包识别成**升级**而不是「并存装两份」 |
| `OBS_Helper.exe`（程序集名 / 可执行文件名） | 增量更新的文件替换、自举进程 `--apply-update`、用户已有的快捷方式都指着它 |
| `%LocalAppData%\OBS_Helper\` | `prefs.json` / `secrets.dat` 在这里 —— 改目录等于让老用户「丢了设置与密钥」 |
| Release 资产命名 `OBS_Helper_*` | 应用内更新与知识库热更新按这些名字找资产 |
| 数据目录名与语言无关的既有约定 | 英文用户重装不会装到第二个目录，增量更新也找得到目标文件 |

### 升级与兼容

- 安装目录默认值改为 `{autopf}\OBS帮助助手`，但 **Inno 升级时用的是「上次安装目录」而不是默认值**
  （`UsePreviousAppDir` 默认 yes），所以：
  - **2.9.5 及更早的存量用户**：升级后仍装在原目录，原样走增量更新 / 覆盖安装，设置与密钥不动；
  - **全新安装**：装到 `{autopf}\OBS帮助助手`。
- 主构建与 Win7 兼容构建继续共用同一个 `AppId`（两者之间切换仍被识别为升级）。
- 安装向导的卸载项显示名随安装语言变化（中文 `OBS帮助助手`、英文 `OBS Helper`）。

---

## 2. 图标：应用图标与仓库图标统一

- **问题**：应用窗口 / 任务栏 / 托盘 / 安装包 / 卸载项用的是旧版「OBS 圆标 + 齿轮角标」位图，
  而 README、仓库头像与社交预览用的是另一枚（`assets/icon.svg`：深色圆角底 + 显示器 +
  健康波形 + 录制指示点 + 体检对勾）。同一款软件出现两个「脸」。
- **本版做法**：统一为后者。48 / 64 / 128 / 256 px 用矢量源渲染的位图；
  **16 / 24 / 32 px 按同一套几何与配色另画**（描边加粗、波形减到一峰、对勾徽章放大并夹在圆角底板内）。
  这不是「换了设计」，而是图标设计的常规做法：把 256 的图直接缩到 16 会糊成一团，
  而托盘与任务栏恰恰只用这几个尺寸。
- **可复现**：生成脚本 [`scripts/gen_appicon.py`](scripts/gen_appicon.py) 重写入库
  （源 = `../assets/icon.png`，输出 `OBS_Helper.Wpf/Assets/appicon.ico`，含 7 档尺寸），
  只用到 Pillow。旧版脚本按 OBS 官方 logo 重绘、且依赖一张**本机路径**下的参考图做蒙版
  （换台机器就跑不起来），一并替换掉。
- **校验**：`ProductNameTests.AppIcon_ContainsAllShellSizes` 解析 ICO 目录，断言 7 档尺寸齐全，
  且 csproj 里 `appicon.ico` 的三处引用（`ApplicationIcon` / `EmbeddedResource` / `Resource`）都在
  —— 少任何一处，对应位置会静默退回系统默认图标。

---

## 3. 素材、文案与文档

- **品牌三图**：`icon.svg` / `banner.svg` / `social-preview.svg` 中的名称文字更新为新名，
  并按原有脚本 `scripts/render_assets.ps1` 重新渲染 PNG 入库（`banner.svg` 在 README 里是矢量引用，
  PNG 用于 GitHub 社交预览等只吃位图的位置）。
- **随包内容**：`troubleshooting.md` 页脚的署名更新；插件目录里的 Dock 条目更新。
- **文档**：根 `README.md`、`README.en.md`、`NOBS/README.md`、`CONTRIBUTING.md`、`SECURITY.md`、
  `CODE_OF_CONDUCT.md`、`docs/ARCHITECTURE.md`、`docs/CODEBASE.md`、Issue 表单与 PR 模板、
  构建脚本头部注释全部对齐到 2.9.6（名称、版本号、质量基线、资产清单）。
- **历史文档**：发布说明与审校记录里的**品牌名**一并归一为新名 ——
  只改品牌名，**不动其中的版本内容、结论与证据边界**；旧名保留在本说明与根 README 的「曾用名」说明里，
  否则新用户会以为是两个不同的软件。
- **刻意不改**：归档的旧版 Blazor 壳（`OBS_Helper.Client/`）中的名称保持原样 ——
  它对应的是历史版本，已不在发布链路上。

---

## 4. 版本与产物

- `OBS_Helper.Wpf.csproj`：`Version` / `FileVersion` / `AssemblyVersion` → **2.9.6**。
- `OBS_Helper_Setup.iss`：`MyAppVersion` 默认值 → **2.9.6**（`build.ps1` 仍会以 csproj 为准覆盖）。
- 增量更新基准 = **2.9.5**：安装了 2.9.5 的用户可直接走应用内增量更新。

### 发布资产（`NOBS\PAKE\windows\`）

| 资产 | 大小 | SHA-256 |
| --- | --- | --- |
| `OBS_Helper_Setup_2.9.6.exe` | 50.8 MB | `2a4900a14f519c5d3bdd474e9c18b615f4c4e8afacd1092df6ab3eca3900d466` |
| `OBS_Helper_Setup_2.9.6_win7.exe` | 47.8 MB | `4635293f57a0329861d18fdde880d5a99d9e71fe97a66f21b59e6186d98e6113` |
| `OBS_Helper_Portable_2.9.6.zip` | 66.8 MB | `c50fd15be5de9acaa17b6d4c38e38a949ae69dacf0e4f67c73c5224a08bb9aa5` |
| `OBS_Helper_Portable_2.9.6_win7.zip` | 62.9 MB | `f1b047fa4859094c2550b26b746ec1f8ee6b969018a588a1e37a5180c9e03f79` |
| `OBS_Helper_Update_2.9.6.zip` | 2.1 MB | `92b5a5828615169e517ab23d457770fb03bb2b1784d4d687efacf3a5b0a95cce` |
| `OBS_Helper_Knowledge_2.9.6.json` | 0.43 MB | `02213f248229a7120cc146484c427e3fd9c06a363ae04e9eeb12cf4ecbec4598` |
| `OBS_Helper_Knowledge_2.9.6.en-US.json` | 0.42 MB | `23a17bd5ca6d93eb110e2fae520ba9e97113b907a2a40591b03a2e7c709d16b5` |
| `OBS_Helper_Plugins_2.9.6.json` | 26 KB | `475e27ae74d15fde3dfbe335affefbdba498c4cf1f3a484e4868c88ebe021f84` |
| `OBS_Helper_Plugins_2.9.6.en-US.json` | 25 KB | `273207a786d33fa68021a6f952b00735ea2c1ab97d79d4ff03595ceee2cfda83` |

> 增量包只含 **5 个变更文件**（`OBS_Helper.dll` / `OBS_Helper.exe` / `OBS_Helper.deps.json` /
> `OBS_Helper.pdb` / `en-US\OBS_Helper.resources.dll`）：本版没有新增或删除任何运行时文件，
> 与「只改文案、图标与文档」的定位一致。完整文件清单见 `PAKE\windows\manifests\manifest_2.9.6.json`
> （不随 Release 发布）。

---

## 验证

| 验证项 | 命令 / 方式 | 结果 |
| --- | --- | --- |
| 单元测试 | `dotnet test OBS_Helper.Wpf.Tests\OBS_Helper.Wpf.Tests.csproj` | **630 项全部通过**（上一版 626 项；本版新增 4 项见下） |
| 新增回归项 | `ProductNameTests`（4 项） | 产品名在 csproj / 安装包脚本 / 中文文案表**三处一致**；文案表不再出现旧产品名；版本号在 csproj 与 `.iss` 之间一致（含 `FileVersion` / `AssemblyVersion` 前缀）；`appicon.ico` 含 7 档 Shell 尺寸且三处引用齐全 |
| Headless 自检 | `OBS_SELFTEST=1` 运行同源码的 WPF 构建，读回 `selftest_result.txt` | **22 项全部 PASS / 0 FAIL**（18 条路由 + 新手引导覆盖层 + 迷你小窗 + 文案资源解析 + 语言切换往返） |
| 增量的可升级性 | `python scripts\verify_delta.py --old PAKE\windows\OBS_Helper_Portable_2.9.5.zip --delta PAKE\windows\OBS_Helper_Update_2.9.6.zip --publish …` | **PASS**：清单 2.9.5 → 2.9.6，5 个变更文件；应用后与发布目录比对 **270 个共同文件，0 不一致、0 缺失** |
| 安装包元数据 | 读取安装包 exe 的 `FileVersionInfo` | `ProductName` / `FileDescription` = **OBS帮助助手**，`FileVersion` / `ProductVersion` = **2.9.6**（主构建与 Win7 构建一致） |
| 程序集元数据 | 读取发布目录里 `OBS_Helper.exe` 的 `FileVersionInfo` | `ProductName` = **OBS帮助助手**，`FileVersion` = **2.9.6.0**，`Company` = OBS Helper |
| 图标落地 | 用 `Icon.ExtractAssociatedIcon` 从构建出的 `OBS_Helper.exe` 取回图标，与 `Assets\appicon.ico` 的 32px 帧逐像素比对 | **完全一致（平均绝对差 0.0）** —— 新图标确实进了 exe |
| 图标可复现 | 重跑 `python scripts\gen_appicon.py` 后比对 `appicon.ico` 的 SHA-256 | **逐字节一致** —— 入库的生成脚本与打进安装包的图标同源 |

> **证据边界（如实说明）**：以上「单元测试 / 自检 / 增量校验 / 元数据」都是机器执行的结果；
> 「图标在托盘与任务栏上的实际观感」只有人工目视核对（已按 16 / 24 / 32 / 48 / 64 / 128 / 256
> 生成对照图逐档看过），**没有**在真实 Windows 7 机器上验证过渲染；
> 安装包**没有**做「装到干净系统再卸载」的端到端演练。

---

## 已知边界

- **GitHub 仓库社交预览图需在仓库设置里手动重新上传**：`assets/social-preview.png` 已按新名重新渲染入库，
  但 GitHub 的社交预览是在 Web 设置里指定的，API 无法更新 —— 未重传前，分享链接的缩略图仍是旧名。
- 蓝奏云（国内镜像）与部分第三方页面的文案需要手动更新，不在本仓库自动化范围内。
- 归档的旧版 Blazor 壳与本地记忆（`.workbuddy/`，未入库）中仍会出现旧名，属历史记录，不影响发布产物。
- 更名只覆盖中文显示名：英文界面、程序集名、数据目录、资产命名仍为 `OBS Helper` / `OBS_Helper`。
- **英文安装时开始菜单文件夹仍是中文**（`DefaultGroupName` 用的是 `{#MyAppName}`，没有走 `{cm:…}` 语言变量）：
  这是本次更名之前就有的行为（旧名同样是中文），修它要重编安装包并重传资产，安排在下一版处理；
  快捷方式名、卸载项显示名、完成页文案都已随安装语言切换。
- 归档的旧版 Blazor 壳（`OBS_Helper.Client/`）中仍是旧名：那是对应历史版本的代码，已不在发布链路上。
- 二进制是在**提交本次改动之前**构建的，所以发布目录里 exe 的 `ProductVersion` 附带的提交号指向
  上一版提交（`6bc95de`），而不是本次发布提交。版本号本身（`FileVersion` 2.9.6.0 / 安装包 2.9.6）
  是正确的 —— 这只是「先出包、后提交」的时序副产物，历次发布同样如此。

---

## 升级方式

- **2.9.5 → 2.9.6**：应用内「检查更新 → 增量更新」即可（本版只改变更文件）。
- **2.1.x ~ 2.9.4 → 2.9.6**：增量包基准对不上，请用安装包或便携包覆盖升级；设置与密钥不受影响。
- **全新安装**：从 [Releases](https://github.com/YYRMMAYO/OBS_Helper/releases) 取
  `OBS_Helper_Setup_2.9.6.exe`（Win10/11）或 `OBS_Helper_Setup_2.9.6_win7.exe`（Win7 SP1+）。
