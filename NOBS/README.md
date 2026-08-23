# OBS 排障助手（Windows 原生 WPF 版）

本目录是当前维护中的 **Windows 原生 WPF 版**源码（`OBS_Helper.Wpf/`），
这是原 Blazor WebAssembly + WebView2 版本的原生重构。

**当前版本：V2.5.0** —— 排障闭环增强版：在 V2.4 的插件生态扩充之上，本版聚焦「从发现问题到解决问题」的闭环：

- **日志分析三大分诊升级**：掉帧三分类主因判定（渲染 / 编码 / 网络占比最高者即先治方向）、
  编码过载按当前设置生成处理顺序（识别编码器与显卡厂商给出具体步骤）、
  双显卡错位检测（核显渲染告警 + 独显确认提示）；
- **插件加载失败 × OBS 版本联动**：日志出现插件加载失败且 OBS < 32.2.2 时，
  单独提示「先升级补丁版」这条最高性价比解法；
- **录前自检（只读）**：一键核对录制格式（防崩溃 MKV）、录制路径与磁盘剩余空间、
  编码器、音频采样率与麦克风配置，多数"录完才发现"的事故可事前拦截；
- **插件广场 StreamFX 迁移矩阵**：模糊 / 遮罩 / 3D 变换 / 描边辉光 / 复古特效
  五大常用功能直达维护活跃的替代插件；
- **知识库 120 → 122 条（v1.8）**：新增长录制音画漂移（时钟漂移 / 磁盘饱和）、
  浏览器源内存膨胀两篇排障指引；插件崩溃条目补充「先升级 OBS ≥ 32.2.2」首推解法；
- **插件广场 46 → 57 个条目（v1.3）**：新增 obs-detect、Soundboard、Countdown Timer、
  Scale to Sound、3D Effect（StreamFX 替代）、Recursion Effect、Browser Transition、
  HTML Source、Device Switcher、Time Shift、Color Monitor 十一个精选，
  仓库全部逐一验证可达。

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
