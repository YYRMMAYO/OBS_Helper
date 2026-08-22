# OBS 排障助手（Windows 原生 WPF 版）

本目录是当前维护中的 **Windows 原生 WPF 版**源码（`OBS_Helper.Wpf/`），
这是原 Blazor WebAssembly + WebView2 版本的原生重构。

**当前版本：V2.2.0** —— 插件生态版：在 V2.1 的增量更新 / 知识库独立更新（112 条，v1.6）/ 安装包自动清理之上，
新增**插件广场全链路**：

- **插件目录外置热更新**（`plugins.json`，与问题库同通道，链接纠错 / 新插件上架无需发版）；
- **本地插件体检**（只读扫描已装 DLL 与版本，标注「广场收录 / 未收录」，隐私仅存本机）；
- **日志 × 插件嫌疑联动**（加载失败 / 崩溃肇事模块提取 → 一键跳转插件广场；StreamFX 迁移、
  obs-multi-rtmp 已知问题进入知识库 v1.6）；
- **直达 Releases 下载**（最新版本角标，24h 缓存节流，失败静默）；
- **AI 插件性能预算提示**（联动系统监控实时采样）+ 日志掉帧 × AI 开销关联分析；
- **竖屏双画布 / 多平台推流两条分步向导**（搭建页「进阶向导」入口）；
- **模板推荐插件标注**（落地前对照体检结果提示缺失并跳转）；
- **关注插件启动查新**（24h 节流，有新版仅 Toast 不弹窗）。

仓库的完整介绍（中英文、构建与使用说明）请看根目录文档：

- 简体中文 → [`../README.md`](../README.md)
- English → [`../README.en.md`](../README.en.md)

本目录的工程文档：

- [`docs/CODEBASE.md`](docs/CODEBASE.md) — 项目库代码清单（按模块列出全部源码 / 资源文件及职责）
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — 架构总览（分层 / 组合根 / 连接 / AI 诊断 / 安全设计）
- [`docs/ROADMAP_PLUGINS.md`](docs/ROADMAP_PLUGINS.md) — 插件生态路线图（2026 调研 + V2.2 落地清单）
- [`RELEASE_NOTES_v2.2.0.md`](RELEASE_NOTES_v2.2.0.md) — V2.2.0 发布说明
- [`docs/reviews/`](docs/reviews/) — 各版本发布审查报告
