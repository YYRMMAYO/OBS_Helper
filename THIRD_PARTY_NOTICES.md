# 第三方署名与许可证（THIRD PARTY NOTICES）

> 本文件汇总 OBS 帮助助手（OBS Helper）的第三方署名与许可证边界。
> 项目主程序以 **MIT** 发布（见根 `LICENSE`）；下列项目**不改变**主程序的 MIT 许可。
> 更完整的「用在哪、边界是什么」见 [`NOBS/docs/OPEN_SOURCE_ATTRIBUTION.md`](NOBS/docs/OPEN_SOURCE_ATTRIBUTION.md)。

## 1. 运行时与测试

| 项目 | 许可证 | 用途 | 边界 |
|---|---|---|---|
| [.NET / WPF](https://github.com/dotnet/wpf) | MIT | 桌面运行时与 UI 框架 | 自包含发布，运行时随包携带 |
| [xUnit](https://github.com/xunit/xunit) | Apache-2.0 | 单元测试框架 | 仅测试工程，不进发布物 |
| [Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) | MIT | 测试宿主 | 仅测试工程，不进发布物 |

## 2. 功能设计参考（独立实现）

| 项目 | 许可证 | 用途 | 边界 |
|---|---|---|---|
| [咩计时 meTimer](https://github.com/zanjie1999/meTimer) | [咩License 1.1](https://github.com/zanjie1999/LICENSE) | 导播计时器的功能设计参考 | **清洁室独立实现，不复制源代码**；署名、许可证原文与边界见 [`NOBS/third_party/meTimer/NOTICE.md`](NOBS/third_party/meTimer/NOTICE.md) 与 [`LICENSE.md`](NOBS/third_party/meTimer/LICENSE.md) |

> 咩License 1.1 禁止商业用途，并要求衍生作品以同许可开源。本项目的计时器模块未复制原项目代码，因此不复用其许可证；若未来直接移植代码，必须重新评估许可兼容性。

## 3. 按公开协议 / 外部软件集成（不复制代码）

| 项目 | 许可证 | 用途 | 边界 |
|---|---|---|---|
| [OBS Studio](https://github.com/obsproject/obs-studio) | GPL-2.0 | 被控制的目标软件、日志格式 | 只做外部集成；不捆绑、不修改 OBS |
| [obs-websocket](https://github.com/obsproject/obs-websocket) | GPL-2.0 | OBS 远程控制协议 | 按公开协议自行实现 C# 客户端；不复制源码、不链接二进制 |
| [VTube Studio API](https://github.com/DenchiSoft/VTubeStudio) | MIT | Live2D 模型加载 / 鉴权 / 移动 | 只使用公开 WebSocket API |
| [GitHub REST API](https://docs.github.com/rest) | GitHub 服务条款 | Release 资产查询、仓库搜索 | 只读公开仓库；限频 + 缓存 |

## 4. 只做引导的外部可选软件（不捆绑、不复制）

DistroAV、obs-spout2-plugin、obs-localvocal、Watt Toolkit、VTube Studio、FFmpeg、rclone、whisper.cpp、MediaMTX、scrcpy、WLED 等项目的代码不在本仓库与发布物中；应用只做检测、官方入口跳转与图文引导，由用户自行安装。

## 5. 素材与内容

- 内置 overlay 页面为本项目原创 HTML/CSS，无外部图片与字体。
- 插件目录、平台目录只记录名称、仓库与说明，不包含第三方代码或二进制。
- 用户导入的 Live2D 模型、房间素材与插件归用户所有；本项目不复制、不再分发。

## 6. 反馈

如果认为本项目遗漏了应当署名的项目，或许可证标注有误，请通过 [GitHub Issues](https://github.com/YYRMMAYO/OBS_Helper/issues) 反馈；我们会在下个版本修正。
