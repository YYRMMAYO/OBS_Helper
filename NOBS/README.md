# OBS 排障助手（Windows 原生 WPF 版）

本目录是当前维护中的 **Windows 原生 WPF 版**源码（`OBS_Helper.Wpf/`），
这是原 Blazor WebAssembly + WebView2 版本的原生重构。

**当前版本：V2.8.0** —— 守护与体检版。依据 [`docs/DEV_GUIDE_GAPS_2026-08.md`](docs/DEV_GUIDE_GAPS_2026-08.md)
缺口清单，补齐 OBS 用户最高频故障场景的「事中守护 + 系统侧体检」：

<div align="center">

<video src="https://github.com/YYRMMAYO/OBS_Helper/releases/download/V2.8.0/nobs-promo.mp4" controls width="760"></video>

▲ **宣传视频**（含背景音乐）。若内嵌播放不可用，
[点击这里观看 / 下载](https://github.com/YYRMMAYO/OBS_Helper/releases/download/V2.8.0/nobs-promo.mp4)。

</div>

## V2.8.0 新增

- **录制守护**（GAP-1，默认开启）：断连且正在录制 / 录制中心跳超时（OBS 疑似假死）/
  重连后录制丢失，三层信号托盘强提醒，终结「录完才发现是空文件」；
- **实时日志尾随预警**（GAP-4，默认开启）：直播中命中掉帧 / 编码过载 / 断流等特征即时提醒；
  规则与离线分析器同源共享，90 秒同类抑制 + 每小时限流；
- **黑屏专项体检**（GAP-2 + GAP-8）：HAGS、obs64.exe GPU 偏好、Game DVR / 游戏模式、
  显卡驱动月龄、电源计划与电池供电逐项只读探测；
- **音频设备深度体检**（GAP-3）：麦克风隐私权限、通信 Ducking、音频服务、
  OBS 输入 × 系统设备漂移对照；
- **虚拟摄像头体检**（GAP-5）：DirectShow 驱动注册 + 插件文件探测 + 排查树指引；
- **已知问题插件标注**（GAP-6）：`riskNote` 字段热更新通道，本机体检命中风险插件打黄标。

## 功能底座（V2.2 ~ V2.7 保持不变）

- 体检工具箱：色彩 / 采样率 / 黑屏（新）/ 音频深度（新）/ 虚拟摄像头（新）/ 磁盘写入 /
  编码顾问 / 节点探测八卡齐全；
- 日志分析三大分诊：掉帧三分类主因判定、编码过载按当前设置分诊、双显卡错位检测；
- 插件广场 57 条目（v1.3 目录）外置热更新、本机插件只读体检、日志 × 插件嫌疑联动；
- 知识库 149 条（v2.0）、增量更新、知识库独立更新、安装包自动清理。

## 质量基线

- **280 项单元测试全部通过**；Headless 自检 18 条路由全部 PASS；
- 两遍独立交叉检验通过；全程零第三方 NuGet 包（纯 BCL）。

## 文档

- 仓库完整介绍 → [`../README.md`](../README.md)（含宣传视频）
- English → [`../README.en.md`](../README.en.md)
- 本版发布说明 → [`RELEASE_NOTES_v2.8.0.md`](RELEASE_NOTES_v2.8.0.md)
- 项目库代码清单 → [`docs/CODEBASE.md`](docs/CODEBASE.md)
- 架构总览 → [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- 缺口开发指引（V2.8 依据）→ [`docs/DEV_GUIDE_GAPS_2026-08.md`](docs/DEV_GUIDE_GAPS_2026-08.md)
