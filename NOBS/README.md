# OBS 排障助手（Windows 原生 WPF 版）

本目录是当前维护中的 **Windows 原生 WPF 版**源码（`OBS_Helper.Wpf/`），
这是原 Blazor WebAssembly + WebView2 版本的原生重构。

**当前版本：V2.4.0** —— 数据生态扩充版：在 V2.3 的插件生态打磨之上，本版聚焦两件事：

- **插件知识库 38 → 46 个条目（v1.2）**：新增「工具 / 排障」分类，收录本软件的
  官方 OBS 停靠面板插件 **OBS 排障助手（Dock 版）**；另新增 Win Capture Audio、
  Dynamic Delay、Scene Notes Dock、Face Tracker、PTZ Controls、RTSP Server、
  Zoom to Mouse 七个精选，仓库全部逐一验证可达；
- **问题知识库 112 → 120 条（v1.7）**：新增插件加载失败（OBS 32.2 变更）、
  Nahimic/RTSS 环境组件干扰、多显示器刷新率卡顿、Steam 版插件目录、
  启动缓慢二分定位、Device removed/GPU 挂起、Windows 通信活动降音量、
  码率不足马赛克等八篇排障指引。

V2.2 / V2.3 功能底座保持不变：插件目录外置热更新、本地插件体检（只读）、
日志 × 插件嫌疑联动一键跳转、直达 Releases 下载（24h 缓存节流）、
AI 插件性能预算提示、竖屏 / 多平台搭建向导、模板推荐插件标注、关注插件启动查新、
界面纯文字设计语言、本机体检全盘定位，
以及 V2.1 的增量更新 / 知识库独立更新（120 条，v1.7）/ 安装包自动清理。

仓库的完整介绍（中英文、构建与使用说明）请看根目录文档：

- 简体中文 → [`../README.md`](../README.md)
- English → [`../README.en.md`](../README.en.md)

本目录的工程文档：

- [`docs/CODEBASE.md`](docs/CODEBASE.md) — 项目库代码清单（按模块列出全部源码 / 资源文件及职责）
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — 架构总览（分层 / 组合根 / 连接 / AI 诊断 / 安全设计）
- [`docs/ROADMAP_PLUGINS.md`](docs/ROADMAP_PLUGINS.md) — 插件生态路线图（2026 调研 + V2.2 落地清单）
- [`RELEASE_NOTES_v2.4.0.md`](RELEASE_NOTES_v2.4.0.md) — V2.4.0 发布说明
- [`RELEASE_NOTES_v2.3.0.md`](RELEASE_NOTES_v2.3.0.md) — V2.3.0 发布说明
- [`RELEASE_NOTES_v2.2.0.md`](RELEASE_NOTES_v2.2.0.md) — V2.2.0 发布说明
- [`docs/reviews/`](docs/reviews/) — 各版本发布审查报告
