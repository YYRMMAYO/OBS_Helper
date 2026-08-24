# V2.6.0 优化指引（2026-08-24 网络调研）

> 依据：Reddit r/obs、OBS 官方论坛 / 知识库 / 32.x 发布说明、CSDN / 知乎中文社区的
> 调研结论（见会话调研记录）。对照问题库 v1.8（122 条）去重后形成本清单。
> 本文档是 V2.6.0 的**实施依据与验收标准**：全部条目在本版本内实现完毕。

---

## 一、问题库扩充（v1.8 → v1.9，新增 18 条）

### A 组 · 高频缺口

| # | id | 标题 | 分类 | 调研依据 |
|---|----|------|------|---------|
| 1 | `os-win-update` | Windows 系统更新后 OBS 异常（黑屏 / 设备消失） | black-screen | Win11 KB5074109 事故报道；OBS 32.0.2/32.0.3 修复「睡眠唤醒后音频设备消失」「Windows 更新后虚拟摄像头失效」 |
| 2 | `rc-hybrid-mp4` | Hybrid MP4/MOV 成为默认格式后的兼容性问题 | recording | OBS 32.0 Release Notes：Hybrid MP4/MOV 转正并成为新 Profile 默认输出 |
| 3 | `au-bluetooth` | 蓝牙耳机做麦克风：音质差、延迟、音画漂移 | audio | 社区高频；蓝牙 HFP 免提协议采样率限制 |
| 4 | `au-ducking` | 用 OBS 压缩器侧链实现「说话时音乐自动变小」 | audio | 官方文档 Compressor sidechain；与既有条目 au-commactivity（Windows 通信活动）互补 |
| 5 | `cfg-log-analyzer` | 如何导出日志并用官方日志分析器排查 | config | 官方 KB「Log files」；本应用日志分析页的官方对照入口 |
| 6 | `rc-meeting` | 录制网课 / 视频会议窗口捕获失败（Zoom / 腾讯会议） | recording | Wondershare/社区 FAQ 高频 |
| 7 | `rc-schedule` | 定时录制 / 自动分段录制 | recording | 社区高频诉求（OBS 无原生定时开关，需 obs-websocket 或按大小分割） |
| 8 | `rc-privacy` | 录制时屏蔽系统通知 / 隐私信息入镜 | recording | 教程类高频 |
| 9 | `setup-more-platforms` | 快手 / 淘宝 / 京东直播接入 + 直播伴侣 vs 纯 OBS 选型 | setup | 库中已有 B站/抖音/小红书/视频号，缺这几个 |

### B 组 · OBS 32.x 迁移相关

| # | id | 标题 | 分类 |
|---|----|------|------|
| 10 | `cr-plugin-manager` | 用好 32.x 插件管理器：启用 / 禁用缺失插件 | crash |
| 11 | `cfg-sdr2hdr` | SDR 转 HDR 合成滤镜的使用场景与误区（32.2 新增） | config |
| 12 | `sf-webrtc-simulcast` | WebRTC 联播（Simulcast）是什么 / 怎么开（32.1 新增） | streamfail |
| 13 | `au-rtx-audio` | NVIDIA RTX 音频滤镜（VAD 降噪 / 背景移除）配置与性能代价 | audio |
| 14 | `cfg-undo-redo` | 误操作救场：撤销 / 重做（32.1 新增） | config |
| 15 | `cfg-backup-before-upgrade` | 升级 / 试 Beta 前备份场景集合与配置的正确姿势 | config |

### C 组 · 低优补充

| # | id | 标题 | 分类 |
|---|----|------|------|
| 16 | `setup-dual-pc` | 双机位 / 双 PC 推流搭建（采集卡 + 串流机） | setup |
| 17 | `rc-device-disconnect` | 录制中途摄像头 / 采集卡掉线重连（USB 电源管理） | config |
| 18 | `rc-fps-specs` | 录制帧率 / 分辨率与发布平台规格匹配 | recording |

实现方式：沿用 `scripts/add_problems_*.py` 模式，脚本 `scripts/add_problems_v26.py`
一次性追加，版本号 1.8 → 1.9。所有 `related` 引用既有条目真实 id。

---

## 二、录屏侧新功能（工具箱页 · 上半部分）

### F1 录像工具卡
- **打开录像目录**：从 OBS 配置（global.ini → 当前 Profile → basic.ini 的
  `advout.recfilepath` / `simpleoutput.filepath`）解析实际保存目录；
  未配置时回退系统「视频」文件夹；一键在资源管理器打开。
  解决「录完找不到文件」这一纯录屏用户最高频痛点。
- **MKV → MP4 重封装**：探测 ffmpeg（PATH → 常见安装目录），找到则调用
  `-c copy` 无损转封装（不重编码、秒级完成）；找不到给下载指引。
  解决崩溃后文件打不开 / 平台上传只认 MP4。
- 服务：`Services/ObsConfig/RecordingToolsService.cs`（只读定位 + 进程调用）。

### F2 场景化参数处方卡
- 内置四套推荐参数组合（录网课 / 录游戏 / 竖屏短视频 / 直播带货）：
  分辨率、帧率、编码器、格式、码率建议，一键复制为文本。
- 纯静态数据，直接写在页面代码中（无需热更新通道）。

### F3 隐私模式清单卡
- 录屏前 checklist：系统通知（勿扰）、桌面图标、任务栏预览、浏览器标签。
- 提供「打开 Windows 专注助手设置」「打开个性化设置」直达按钮
  （`ms-settings:` 协议），其余为引导式说明。

---

## 三、直播侧新功能（工具箱页 · 下半部分）

### F4 冲突软件扫描卡
- 枚举运行中进程，比对已知干扰源表：
  Nahimic / RivaTuner(RTSS) / Overwolf / Voicemod / MSI AfterBurner 等，
  每项给出风险等级与处置建议，命中知识库条目 `cr-env-interference`。
- 核心：`Services/Tools/ConflictScannerCore.cs`（纯逻辑，进程名列表注入，可单测）。

### F5 推流带宽计算器卡
- **上行 → 推荐**：输入实测上行带宽（Mbps），按 60~70% 安全系数输出
  推荐码率 / 输出分辨率 / 帧率组合。
- **多路推流 → 所需上行**：路数 × 单路码率 × 1.2 冗余 = 所需上行，
  并判断当前带宽是否够用。
- 核心：`Services/Tools/BandwidthAdvisorCore.cs`（纯函数，可单测）。

### F6 OBS 新版本情报卡
- 拉取 GitHub `obsproject/obs-studio` 最新 Release（tag、日期、说明摘要），
  给出「是否值得升级」建议；结果本地缓存 6 小时，失败静默显示缓存或提示离线。
- 服务：`Services/Update/ObsReleaseInfoService.cs`。

---

## 四、通用功能

### G1 日志分析 × 问题库联动扩展
- `ObsLogAnalyzer` 规则表追加两条规则：
  - `LOG-HYBRID-MP4`：日志出现 hybrid mp4 写入记录 → Info 提示格式兼容性与
    重封装入口（关联 `rc-hybrid-mp4`）；
  - `LOG-VIRTUALCAM`：虚拟摄像头启动失败关键字（关联 `vc-virtualcam` 既有条目）。
- （日志关键字 → ProblemId 映射主体已在 V2.5 完成，本次仅补缺口。）

### G2 快捷键速查卡
- 工具箱页内置常用默认快捷键 / 建议自定快捷键速查表（静态内容）。

---

## 五、导航与版本

- 新增一级导航「工具箱」（路由 `toolbox`，位于「插件」之后）：
  `Routes.Toolbox`、MainWindow `_meta` + 注册 + 侧栏按钮 + HeadlessTest 自检用例。
- 组合根注册：`RecordingToolsService`、`ConflictScanner`、`ObsReleaseInfoService`。
- 版本号：csproj `2.5.0` → `2.6.0`；新增 `RELEASE_NOTES_v2.6.0.md`。

## 六、验收标准

1. `dotnet build` 零警告零错误；`dotnet test` 全绿（含新增 BandwidthAdvisor /
   ConflictScanner 单测）。
2. `OBS_SELFTEST=1` 自检：`toolbox` 路由 PASS 且无 ReportError。
3. 问题库 JSON 可被 `ProblemService` 正常解析（version=1.9，140 条）。
4. 所有新服务遵循项目铁律：任何探测失败降级为提示而非抛异常；绝不修改 OBS 配置文件。
