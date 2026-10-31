# X1031 / V3.5.0 技术文档索引

> 目的：把「要求 → 技术判断 → 落点 → 验收」写清楚，避免实现完成后只剩一堆 commit。
> 适用范围：Windows WPF 版（`NOBS/`）。Mac 版本轮不处理。

## 版本边界（按 2026-10-31 最新说明修正）

| 版本 | 范围 | 更新通道 |
| --- | --- | --- |
| `X1031` | 需求 1–9；**不含**全局 UI 重构、手机连接页重排、功能中心体验优化 | 只检测 GitHub 上 `X*` 标签，绝不混入 V 系列 |
| `V3.5.0` | 需求 1–8 + **全局 UI 重构** + 手机连接页 / 功能中心体验优化；**不含**需求 9（新应用图标，图标暂不同步 V 系列） | 只检测 GitHub 上 `V*` 正式版标签 |

> 因此 X1031 的范围更小、先交付；V3.5.0 在共享的 1–8 项之上再叠加全局 UI 与页面体验重构，
> 但不含新应用图标。两者共用同一套业务代码，差异通过发布通道与图标资源隔离，
> 详见 [`09_X_SERIES_CHANNEL.md`](09_X_SERIES_CHANNEL.md)。

## 文档清单

| # | 文档 | 类型 | 对应需求 |
| --- | --- | --- | --- |
| 1 | [`01_PLAN_OVERALL.md`](01_PLAN_OVERALL.md) | 总纲 / 体验评估 / 对策 | 全部 |
| 2 | [`02_UI_BASELINE_AND_REFRESH.md`](02_UI_BASELINE_AND_REFRESH.md) | UI 基线与全局背景规范 | 10、V3.5.0 UI 重构 |
| 3 | [`03_ICON_SYSTEM.md`](03_ICON_SYSTEM.md) | 图标体系与应用图标 | 1、9 |
| 4 | [`04_PLUGIN_ACCELERATOR_AND_MANAGED_LIST.md`](04_PLUGIN_ACCELERATOR_AND_MANAGED_LIST.md) | 插件加速器 + 已安装列表 | 2、3 |
| 5 | [`05_PHONE_CAMERA.md`](05_PHONE_CAMERA.md) | 手机当摄像头排障与一键媒体服务 | 4、V3.5.0 手机摄像 |
| 6 | [`06_VIRTUAL_LIVE_TEMPLATES.md`](06_VIRTUAL_LIVE_TEMPLATES.md) | 虚拟主播模板与落地排版 | 5 |
| 7 | [`07_MINI_CONTROL_AND_TELEPROMPTER.md`](07_MINI_CONTROL_AND_TELEPROMPTER.md) | 小窗控制重构 + 手机改提词稿 | 6、7 |
| 8 | [`08_GUIDE_AND_UX.md`](08_GUIDE_AND_UX.md) | 内置指引、功能中心、手机连接页 | 8、V3.5.0 体验优化 |
| 9 | [`09_X_SERIES_CHANNEL.md`](09_X_SERIES_CHANNEL.md) | X/V 双通道技术规范 | X 系列独立大版本 |
| 10 | [`10_IMPLEMENTATION_STATUS.md`](10_IMPLEMENTATION_STATUS.md) | 实现状态与三轮自检记录 | 交付验收 |
| 11 | [`11_X1031Y2_PLUGIN_MANAGEMENT.md`](11_X1031Y2_PLUGIN_MANAGEMENT.md) | X1031Y2：插件 / 依赖应用内拉取、本机插件管理、安全加固与问题库精简 | X1031Y2 |

## 关键原则

1. **先可用，再好看**：摄像头、插件列表这类「用户认为它坏了」的功能，先补失败路径与可操作引导。
2. **失败要说人话**：错误码必须映射成「现在能做什么」，不能只显示英文错误。
3. **不改数据目录 / 程序集名**：`OBS_Helper` 与 `%LocalAppData%\OBS_Helper` 不能动，否则升级链路断裂。
4. **X 与 V 互不污染**：通道过滤在解析层完成，不靠界面隐藏。
5. **可回滚**：重构前 UI 已打 tag `ui-baseline-v3.4.2`。
