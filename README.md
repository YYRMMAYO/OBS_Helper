<div align="center">

<img src="assets/banner.svg" alt="OBS Helper — 面向直播新手的 OBS 排障工具" width="100%"/>

**OBS帮助助手 · OBS Helper** —— 本地优先的 OBS 直播 / 录制助手（Windows 原生 WPF）

**正式版：V3.1.1** · **本分支：`dev`（DEV 测试通道，编号 `01`、`02`、`03` …，不带字母 V）**

[![CI](https://github.com/YYRMMAYO/OBS_Helper/actions/workflows/ci.yml/badge.svg)](https://github.com/YYRMMAYO/OBS_Helper/actions/workflows/ci.yml)
[![平台](https://img.shields.io/badge/Platform-Windows_7_SP1%2B_%2F_10_%2F_11-0078D6.svg)]()
[![.NET](https://img.shields.io/badge/.NET-10_%2F_6-512BD4.svg)]()
[![版本](https://img.shields.io/badge/Release-3.1.1-38bdf8.svg)](https://github.com/YYRMMAYO/OBS_Helper/releases)
[![测试](https://img.shields.io/badge/Tests-1345%20passed-2ea44f.svg)]()
[![许可](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

[English](README.en.md) · **简体中文**

</div>

> **当前检出的分支是 `dev`（DEV 测试通道）**：这里的功能比正式版**更超前**，**可能包含不稳定功能**、
> 行为变更与已知缺陷。DEV 版编号是 `01`、`02`、`03`…（**不带字母 V**），只从 **dev Release 页**检查更新；
> 想要稳定请用正式版（`master` 分支 / 正式 Release）。

**OBS帮助助手**是面向直播新手的 OBS Studio 排障工具：连上本机 OBS 做体检、分析日志、部署录制环境、控制场景 / 录制 / 推流，**连不上也能完全离线**查 212 条问题库。原生 WPF、零第三方依赖、自包含单目录发布，无需 WebView2 与 .NET 运行时。

> **完整文档已随源码放进 [`NOBS/`](NOBS/README.md)** —— 功能清单、智能诊断、隐私与安全、安装与更新、构建、
> 工程结构与各版本发布说明都在那里；仓库根这份只是导航页。

## 从这里开始

| 想做什么 | 去哪里 |
| --- | --- |
| 下载安装包 / 便携版 / 看发布说明 | [Releases](https://github.com/YYRMMAYO/OBS_Helper/releases) |
| 读完整文档（功能 · 隐私 · 安装 · 构建 · 历史） | [`NOBS/README.md`](NOBS/README.md) |
| Full documentation in English | [`NOBS/README.en.md`](NOBS/README.en.md) |
| 这一版改了什么（正式版） | [正式版 V3.1.1 发布说明](https://github.com/YYRMMAYO/OBS_Helper/blob/master/NOBS/RELEASE_NOTES_v3.1.1.md) |
| DEV 测试通道的编号与更新规则 | [`NOBS/docs/DEV_CHANNEL.md`](NOBS/docs/DEV_CHANNEL.md) |
| DEV 测试通道的编号与更新规则 | [`NOBS/docs/DEV_CHANNEL.md`](NOBS/docs/DEV_CHANNEL.md) |
| 改文档前先看：哪类文档放 master / 哪类放 dev | [`docs/BRANCH_AND_DOC_RULES.md`](docs/BRANCH_AND_DOC_RULES.md) |
| 反馈问题 / 上报安全漏洞 | [Issues](https://github.com/YYRMMAYO/OBS_Helper/issues) · [SECURITY.md](SECURITY.md) |
| 许可证 | [LICENSE](LICENSE)（MIT） |

## V3.1.1 更新：提词器可以手动上下移动了

> 主线是修好提词器的「上下移动」：工具栏、鼠标拖动、滚轮、键盘四条路都能用，
> 窗口还能用 `Ctrl + 方向键` 微调位置；顺带修掉「自动滚到末尾只剩一片空白」。

- **工具栏新增 `⤒` / `▲` / `▼`**：回到开头、上移一行、下移一行，点一下就能动，不依赖键盘焦点；
- **拖动文字**：在正文按住鼠标上下拖，内容跟着手走；拖动时自动滚动会临时暂停；
- **键盘与滚轮**：`↑` / `↓` 一行、`PgUp` / `PgDn` 一屏、`Home` / `End` 首尾、滚轮细调；
  点一下正文即可激活窗口，之后按键立即生效（以前刚呼出时按键没反应）；
- **窗口位置**：`Ctrl + ↑ / ↓ / ← / →` 10 px 步长移动，自动夹在屏幕内；位置与尺寸拖动后自动保存；
- **滚动终点**：最后一行停在阅读参考线上就停，不再滚进底部留白；脚本末尾空行自动裁掉。

其余功能、数据格式与升级链路与 V3.1.0 一致，可直接增量升级。详见
[正式版 V3.1.1 发布说明](https://github.com/YYRMMAYO/OBS_Helper/blob/master/NOBS/RELEASE_NOTES_v3.1.1.md)。

## 版本通道：正式版与 DEV 测试版

| 通道 | 版本号样式 | 定位 | 更新来源 |
| --- | --- | --- | --- |
| **正式版**（`master`） | `V3.1.1` | **稳定优先**：功能收敛、经过完整回归与发布验证，适合日常直播 / 录制 | 正式 Release 页 · 蓝奏云 · 应用内增量更新 |
| **DEV 测试版**（`dev`，**本分支**） | `01`、`02`、`03` …（**不带字母 V**，当前 `DEV 01`） | **更超前**：开发中的功能先落到这里，**可能包含不稳定功能**、行为变更与已知缺陷 | **只**从 **dev Release 页** 检查更新 |

- DEV 的编号是自增的**构建序号**，与正式版的 `V主.次.修` 没有可比性：DEV 构建只认纯数字 tag（`01`），
  正式版只认 `V3.1.1` 这类 tag —— 两类 tag 挂在同一个仓库里也不会互相被当成新版本；
- DEV 版目前**不出安装包、不走蓝奏云**，更新提示只把你引导到 dev Release 页；
- 想要稳就用正式版；愿意帮忙试新功能、并能接受「这个版本可能坏」再装 DEV 版，
  问题单里请标明 `DEV 0X`（例如 `DEV 03`）。

## 宣传视频

[![宣传视频 · 中文版（含背景音乐）](assets/promo/poster-zh.png)](https://github.com/YYRMMAYO/OBS_Helper/releases/download/V2.9.6/nobs-promo-zh.mp4)

55 秒看懂它能帮你做什么（中文 / 英文两支，均含背景音乐）—— 点封面观看，或
[下载高清版](https://github.com/YYRMMAYO/OBS_Helper/releases/download/V2.9.6/nobs-promo-zh.mp4)。

## 捐赠支持

如果这个工具帮你省下了排查时间，可以请作者喝杯咖啡 —— **完全自愿，金额随意**，不捐也照样免费使用全部功能。

<div align="center">

| 微信支付（推荐） | 支付宝（推荐） |
| :---: | :---: |
| <img src="assets/donate/wechat-pay.png" alt="微信支付捐赠二维码" width="230"> | <img src="assets/donate/alipay.jpg" alt="支付宝捐赠二维码" width="230"> |
| 扫码 → 微信支付 | 扫码 → 支付宝 |

</div>

> **关于捐赠，有句话必须说清楚**：捐赠**不会换取任何优先级** —— 不插队、不加急、不影响 issue 的处理顺序，
> 也不附带功能解锁、定制开发或任何售后承诺。它**只是单纯的捐赠**，用来支持项目的持续开发
> （测试设备、签名证书、文档与时间投入）。软件本身始终免费、以 MIT 许可开源；
> 认真反馈问题、补充文档、提 PR 同样是很有价值的支持。

## 许可

MIT，见 [LICENSE](LICENSE)。
