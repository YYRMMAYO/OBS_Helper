# 开源组件与许可证（V3.3.0）

> 本文档说明 OBS帮助助手（OBS Helper）在开发与运行中使用的开源项目、协议与许可证。
> 本项目主程序以 **MIT** 许可发布（见仓库根 `LICENSE`）。以下项目**不改变**主程序的 MIT 许可，
> 也不代表本项目对其代码进行再分发 —— 每一行都写明「用在哪、边界是什么」。

## 一、直接依赖的运行时 / 框架

| 项目 | 许可证 | 用途 | 边界 |
|---|---|---|---|
| [.NET / WPF](https://github.com/dotnet/wpf) | MIT | 桌面运行时与 UI 框架 | 自包含发布，运行时随包携带 |
| [xUnit](https://github.com/xunit/xunit) | Apache-2.0 | 单元测试框架 | 仅测试工程，不进发布物 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | MIT | 测试宿主 | 仅测试工程，不进发布物 |

## 二、按公开协议实现的集成（不复制代码）

| 项目 / 协议 | 许可证 | 用途 | 边界 |
|---|---|---|---|
| [obs-websocket](https://github.com/obsproject/obs-websocket) | GPL-2.0 | OBS 远程控制（场景 / 录制 / 推流 / 虚拟摄像头） | **按公开协议自行实现 C# 客户端**；不复制其源码，不链接其二进制 |
| [OBS Studio](https://github.com/obsproject/obs-studio) | GPL-2.0 | 被控制的目标软件、官方下载与日志格式 | 只做外部集成；不捆绑、不修改 OBS |
| [VTube Studio API](https://github.com/DenchiSoft/VTubeStudio) | MIT | Live2D 模型加载 / 鉴权 / 移动 | 只使用公开 WebSocket API；不注入进程、不读内存、不改配置 |
| [GitHub REST API](https://docs.github.com/rest) | GitHub 服务条款 | Release 资产查询、仓库搜索 | 只读公开仓库；限频 + 缓存；可选用户 token 存本机 |

## 三、只做「外部可选软件 / 插件」引导（不捆绑、不复制）

以下项目的代码**不在**本项目仓库与发布物中。应用只做检测、官方入口跳转与图文引导，
由用户自行安装；它们的许可证与义务由各自项目与用户之间的使用关系承担。

| 项目 | 许可证 | 用途 |
|---|---|---|
| [DistroAV](https://github.com/DistroAV/DistroAV) | GPL-2.0 | Live2D 的 NDI 采集（可选插件） |
| [obs-spout2-plugin](https://github.com/Off-World-Live/obs-spout2-plugin) | GPL-2.0 | Live2D 的 Spout2 采集（可选插件） |
| [obs-localvocal](https://github.com/occ-ai/obs-localvocal) | GPL-2.0 | 字幕 / 语音识别（可选插件，界面引导） |
| [Watt Toolkit](https://github.com/BeyondDimension/SteamTools) | GPL-3.0 | GitHub 加速（只检测 / 启动 / 引导，不修改其配置） |
| [VTube Studio](https://store.steampowered.com/app/1325860/VTube_Studio/) | 专有（Steam 分发） | Live2D 面捕软件（用户自行安装） |

## 四、明确未使用 / 未分发

| 项目 | 许可证 / 状态 | 说明 |
|---|---|---|
| [Live2D Cubism SDK](https://www.live2d.com/) | 专有许可 | 本项目**不包含** Cubism Core / Native SDK，也不分发任何 Live2D 示例模型 |
| [pixi-live2d-display](https://github.com/guansss/pixi-live2d-display) | MIT | 评估后未采用（2024-08 后无提交，且会引入浏览器运行时） |
| [OpenSeeFace](https://github.com/emilianavt/OpenSeeFace) | BSD-2-Clause | 仅作为未来面捕后端评估，未采用 |

## 五、素材与内容

- 内置 overlay 页面（聊天框 / 边框 / BRB / Ending / 倒计时）为本项目原创 HTML/CSS，无外部图片与字体。
- 插件目录（`plugins.json`）只记录第三方插件的名称、仓库与说明，不包含其代码或二进制。
- 平台目录（`platforms.json`）只记录官方入口与文字说明，不使用平台 Logo。
- 用户导入的 Live2D 模型、房间素材与插件归用户所有；本项目不复制、不再分发。

## V3.4.0 新增外部组件与协议
| 项目 | 许可证 | 用途 | 边界 |
|---|---|---|---|
| mediamtx | MIT | 本地 SRT/RTMP/RTSP 媒体服务器、转推收流 | 外部进程；清单 manual，用户自行下载；不捆绑 |
| whisper.cpp | MIT | 本地实时字幕 | 外部进程；模型权重另行授权；不捆绑 |
| rclone | MIT | 录制归档 | 外部进程；用户自行配置远端 |
| FFmpeg | LGPL-2.1+ | 切片/竖屏/音频链 | 外部进程；只用 LGPL 构建 |
| scrcpy | Apache-2.0 | Android USB 摄像头引导 | 仅检测与步骤，不自动安装 |
| WLED | EUPL-1.2 | 灯光 HTTP API | 仅调用 API，不复制代码 |
| VTube Studio | 专有 | 数字人口型 | 用户自装；通过公开 API 调用 |
| obs-websocket | GPL-2.0 | OBS 控制协议 | 协议复刻，不复制代码 |
| xfgryujk/blivedm | MIT | B 站弹幕协议参考 | 协议复刻，不复制代码 |
| TwitchLib | MIT | Twitch IRC 参考 | 协议复刻，不复制代码 |
| LiveKit / mediasoup | Apache-2.0 / ISC | 远程嘉宾 SFU（可选） | 外部进程；TLS 部署由用户负责 |

## 六、署名与反馈

如果你认为本项目遗漏了某个应当署名的项目、或许可证标注有误，请通过
[GitHub Issues](https://github.com/YYRMMAYO/OBS_Helper/issues) 反馈，我们会在下个版本修正。

> 本文件随版本更新；平台入口、第三方项目许可证与维护状态会变化，实施集成前请以各项目官方仓库为准。
