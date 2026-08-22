# OBS 排障助手（Windows 原生 WPF 版）

本目录是当前维护中的 **Windows 原生 WPF 版**源码（`OBS_Helper.Wpf/`），
这是原 Blazor WebAssembly + WebView2 版本的原生重构。

**当前版本：V2.3.0** —— 插件生态打磨版：在 V2.2 的插件广场全链路之上，
本版聚焦三件事：

- **界面风格统一（全面去 emoji）**：插件板块与全部联动入口回归纯文字 +
  品牌色徽标的设计语言，分类图标不再渲染知识库数据，风格与全应用一致；
- **本机体检全盘定位**：OBS 安装目录多信号探测（注册表多视图 / DisplayIcon 反推 /
  全盘固定驱动器布局 / Steam 多库解析），装在 D、E 盘或 Steam 非默认库不再漏扫，
  手动指定目录对体检生效；
- **插件知识库 28 → 38 个条目（v1.1）**：新增 Source Dock、Replay Source、
  Waveform、Auto Subtitle、Spout2 等十个精选，仓库与 Releases 全部逐一验证；
  走分离热更新通道，V2.2+ 老版本无需升级应用即可收到。

V2.2 功能底座保持不变：插件目录外置热更新、本地插件体检（只读）、
日志 × 插件嫌疑联动一键跳转、直达 Releases 下载（24h 缓存节流）、
AI 插件性能预算提示、竖屏 / 多平台搭建向导、模板推荐插件标注、关注插件启动查新；
以及 V2.1 的增量更新 / 知识库独立更新（112 条，v1.6）/ 安装包自动清理。

仓库的完整介绍（中英文、构建与使用说明）请看根目录文档：

- 简体中文 → [`../README.md`](../README.md)
- English → [`../README.en.md`](../README.en.md)

本目录的工程文档：

- [`docs/CODEBASE.md`](docs/CODEBASE.md) — 项目库代码清单（按模块列出全部源码 / 资源文件及职责）
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — 架构总览（分层 / 组合根 / 连接 / AI 诊断 / 安全设计）
- [`docs/ROADMAP_PLUGINS.md`](docs/ROADMAP_PLUGINS.md) — 插件生态路线图（2026 调研 + V2.2 落地清单）
- [`RELEASE_NOTES_v2.3.0.md`](RELEASE_NOTES_v2.3.0.md) — V2.3.0 发布说明
- [`RELEASE_NOTES_v2.2.0.md`](RELEASE_NOTES_v2.2.0.md) — V2.2.0 发布说明
- [`docs/reviews/`](docs/reviews/) — 各版本发布审查报告
