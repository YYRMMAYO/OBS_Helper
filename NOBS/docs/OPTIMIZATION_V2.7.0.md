# V2.7.0 优化指引（2026-08-24 网络调研）

> 依据：本次网络调研结论——Reddit r/obs 与 r/Twitch、OBS 官方论坛（含 32.x RC 讨论）、
> GitHub `obsproject/obs-studio` Issue #13360、第三方 2026 年设置指南
> （tech-insider / dacast / streamguardian / missoutpc 等）、中文社区
> （obs.cn 教程站、果核剥壳评论区、下载之家 FAQ）。
> 对照本应用既有能力（V2.5 排障闭环、V2.6 工具箱、问题库 v1.9 共 140 条）去重后形成。
> 本文档是 V2.7.0 的**实施依据与验收标准**。

---

## 一、调研发现的剩余痛点（v2.6 未覆盖）

| # | 痛点 | 来源佐证 | 本版对策 |
|---|------|---------|---------|
| P1 | 画面发灰 / 发白：色彩范围 Full↔Limited 不匹配 | streamguardian「Full range causes washed-out colours」；中文社区高频 | 新增色彩体检 + 问题库条目 |
| P2 | 直播画质糊但找不到原因：关键帧间隔设 0、未开动态码率 | streamguardian「Setting 0 … almost always bad」「Dynamic Bitrate … most underused setting」；Reddit 像素化帖 | 日志规则 + 设置体检项 |
| P3 | 选错推流节点：自动选择不等于最优，需按 ping/质量实测 | missoutpc「Judge by ping and stability, not geography」 | 新增推流节点延迟探测 |
| P4 | 录像卡顿丢帧：写普通 HDD 而非 SSD，磁盘吞吐不足 | missoutpc「Record to an SSD or NVMe … causes stutters」 | 新增磁盘写入基准测试 |
| P5 | 编码器 preset/CQP 不知道怎么选：x264 veryfast→fast CPU 翻倍；NVENC P5/P4 分档；录屏 CQP 18、AV1 CQP 22 | tech-insider / obs-versions 2026 指南 | 编码顾问按显卡型号给具体值 |
| P6 | 直播+本地录像双编码额外吃 10–15% GPU，开播前不知道撑不撑得住 | tech-insider「dual encoding adds roughly 10–15% GPU load」 | 性能预算估算器 |
| P7 | 音频发闷/有伪影：系统混音 44.1kHz 与 OBS 48kHz 不一致产生重采样 | streamguardian「using 44.1 kHz causes resampling artefacts」 | 采样率体检 + 问题库条目 |
| P8 | 浏览器源告警不加载：widget URL 过期 / CEF 缓存陈旧 | tech-insider Pitfall 7 | 浏览器源健康检查 + 条目 |
| P9 | 自定义浏览器 Dock 无法刷新，只能重启 OBS | GitHub Issue #13360（官方已确认缺此能力） | 问题库条目 + 变通方案指引 |
| P10 | OBS 打开就掉游戏帧：进程优先级、Display Capture vs Game Capture、隐藏来源未关停 | tech-insider Pitfall 6/8 | 问题库条目整合进性能预算卡 |
| P11 | DeckLink 等采集卡输出失效跨版本反复出现 | 官方论坛 32.0 RC 讨论楼中楼（BMD 8K Pro 用户实测 31.1.2 未修复） | 问题库条目 |
| P12 | OBS 无原生字幕/转写，AI 能力依赖插件 | rfp.wiki 对比报告「No native transcription, captioning…」 | 插件广场补充字幕类精选 |

---

## 二、问题库扩充（v1.9 → v2.0，新增 9 条）

> 实施调整说明：拟定清单中的色彩范围、关键帧间隔、动态码率、采样率不一致、
> 进程优先级、采集卡六类痛点经与既有条目逐一比对，已由
> `cf-colorrange` / `lag-keyint` / `lag-dynamic-bitrate` / `au-sample-mismatch` /
> `cf-priority` / `bs-capturecard` 覆盖，为避免重复未再收录；
> 对应的新功能卡与日志规则改为关联这些既有条目。

### A 组 · 高频缺口

| # | id | 标题 | 分类 | 调研依据 |
|---|----|------|------|---------|
| 1 | `src-browser-alert` | 浏览器源告警挂件空白 / 冻结：widget URL 过期与缓存清理 | sources | tech-insider Pitfall 7；社区高频 |
| 2 | `cfg-browser-dock-refresh` | 自定义浏览器 Dock 卡住无法刷新的变通方案 | config | GitHub Issue #13360（官方确认能力缺口） |
| 3 | `perf-obs-overhead` | OBS 隐性开销：隐藏来源未关停、重复捕获与浏览器源数量 | performance | tech-insider Pitfall 6/8 |
| 4 | `perf-dual-encode` | 直播同时本地录像：双编码的 GPU 预算（约 +10~15%）与降级顺序 | performance | tech-insider「dual encoding adds roughly 10–15% GPU load」 |

### B 组 · 编码器与生态

| # | id | 标题 | 分类 |
|---|----|------|------|
| 5 | `enc-preset-guide` | 预设怎么选：x264 档位与 NVENC P1~P7 分档速查 | encoder |
| 6 | `enc-recording-cqp` | 录像用恒定质量（CQP/CRF）：参考值与适用场景 | encoder |
| 7 | `vc-caption-plugins` | 直播字幕 / 语音转写：原生缺失下的插件方案 | virtualcam |
| 8 | `sf-ingest-ping` | 推流节点怎么选：按 ping 与质量实测，而非默认自动 | streamfail |

### C 组 · 低优补充

| # | id | 标题 | 分类 |
|---|----|------|------|
| 9 | `rc-disk-speed` | 录制卡顿但编码正常：磁盘写入速度不足（HDD / 满盘 SSD） | recording |

实现方式：沿用 `scripts/add_problems_v26.py` 模式，脚本 `scripts/add_problems_v27.py`
一次性追加，版本号 1.9 → 2.0（140 → 149 条）。所有 `related` 引用校验为真实条目 id。

---

## 三、新功能（工具箱页扩展）

### F1 色彩体检卡（对应 P1）
- 从当前 Profile 的 `basic.ini` 只读解析 `colorrange` / `colorspace` /
  `colorformat`，与主流平台安全值（NV12 · Rec.709 · Limited）比对；
- 命中 Full Range 或 Rec.2100（未开 HDR 直播）时给出黄色警告与一键说明；
- 服务：`Services/ObsConfig/ColorCheckService.cs`（只读，失败降级为提示）。

### F2 磁盘写入基准卡（对应 P4）
- 在用户选择的目录写入临时文件做顺序写测速（默认 256MB，可取消），输出 MB/s；
- 对照表：≤ 码率换算需求 ×1.5 → 红色警告（建议换 SSD/降低分辨率），
  否则绿色通过；测完立即删除临时文件，不留垃圾；
- 核心：`Services/Tools/DiskBenchmarkCore.cs`（纯逻辑：样本点 → 结论，可单测）。

### F3 推流节点延迟探测卡（对应 P3）
- 内置 Twitch/B站/抖音等常见 ingest 域名清单（随包热更新通道可更新），
  逐个 TCP 握手测 RTT，排序展示并标注推荐节点；
- 说明文字强调「RTT 低 ≠ 一定稳，建议结合实际推流观察」，避免过度承诺；
- 服务：`Services/Tools/IngestPingService.cs`（超时 800ms，全部失败降级为离线提示）。

### F4 编码顾问增强卡（对应 P5/P6）
- 升级 V2.5 的编码器识别：读到显卡型号后直接给出**具体参数组合**——
  - NVIDIA ≥30系：NVENC H.264 P5（推流）/ HEVC CQP 18 或 AV1 CQP 22（录像，40系以上）；
  - NVIDIA <30系：P4；AMD：AMF Quality；Intel：QuickSync；
  - x264：veryfast 起步，并注明每升一档 CPU 约翻倍；
- 双编码预算提示：勾选「边播边录」时叠加 +10~15% GPU 占用预估与降级建议（对应 P6）；
- 核心：`Services/Tools/EncoderAdvisorCore.cs`（纯函数：显卡字符串 + 用途 → 参数集，可单测）。

### F5 音频采样率体检卡（对应 P7）
- 枚举系统默认播放/录音设备的共享模式采样率（WASAPI 只读查询），
  与 OBS 设置的 48kHz 比对，不一致时给出改哪一端的明确指引；
- 服务：`Services/Audio/SampleRateCheckService.cs`。

### F6 浏览器源健康检查卡（对应 P8/P9）
- 清单式自检：widget URL 是否过期（引导重新生成）、CEF 缓存清理步骤、
  自定义 Dock 无刷新功能的变通方案（引用条目 `cfg-browser-dock-refresh`）；
- 纯静态 checklist + 系统缓存路径直达按钮（只读定位，不自动删除）。

---

## 四、通用功能（已实施）

### G1 日志分析规则扩展（+4 条）
- `LOG-COLOR-RANGE`：日志出现 `color range: full` → Info 提示画面发灰可能
  （关联既有条目 `cf-colorrange`）；
- `LOG-BITRATE-DROP`：动态码率下调记录 → 建议降基础码率 / 换节点
  （关联 `lag-dynamic-bitrate`）；
- `LOG-CAPTURE-CARD`：DeckLink 等采集卡初始化失败关键字
  （关联 `bs-capturecard`）；
- `LOG-AUDIO-RESAMPLE`：音频实时重采样警告关键字
  （关联 `au-sample-mismatch`，与既有 LOG-AUDIO-SAMPLERATE 互补）。

### G2 录前自检项追加
- V2.5 的录前自检新增「关键帧间隔」核对：keyint = 0 或 > 4 秒 → 警告并关联
  `lag-keyint`；键缺失按默认 2 秒处理不告警。（采样率 48kHz 检查项 V2.5 已有，
  本版由工具箱「音频采样率体检卡」补齐系统设备侧的比对。）

### G3 插件广场核验（对应 P12）
- 经核验，字幕 / 语音转写类精选已在 AI 分类中收录
  （LocalVocal、Auto Subtitle、CleanStream、OBS Detect 共 4 条，仓库可达），
  新增条目 `vc-caption-plugins` 直接引导用户前往插件广场；本版不重复新增插件数据。

---

## 五、导航与版本（已实施）

- 不新增一级导航，全部功能挂载在现有「工具箱」（`toolbox`）页：
  上半区追加磁盘写入基准 / 音频采样率体检，下半区按序新增
  色彩体检、编码顾问、节点探测、浏览器源健康检查；
- 组合根注册：`ColorCheckService`、`SampleRateCheckService`
  （DiskBenchmark / EncoderAdvisor / IngestPing 为纯静态核心，页面直接调用）；
- HeadlessTest 路由自检不变（仍覆盖 `toolbox`，实测 18 路由全 PASS）；
  新增单元测试覆盖 EncoderAdvisor / DiskBenchmark / IngestPing / ColorCheck /
  SampleRateCheck 核心 + 关键帧自检项 + 新日志规则，全量 241 项测试通过；
- 版本号：csproj `2.6.0` → `2.7.0`；新增 `RELEASE_NOTES_v2.7.0.md`。

## 六、验收标准

1. `dotnet build` 零警告零错误；`dotnet test` 全绿（241 项，含新增单测）。✅
2. `OBS_SELFTEST=1` 自检：全部路由 PASS 且无 ReportError。✅
3. 问题库 JSON 可被 `ProblemService` 正常解析（version=2.0，149 条），
   所有 `related` 引用真实存在。✅
4. 所有新服务遵循项目铁律：任何探测失败降级为提示而非抛异常；
   绝不修改 OBS 配置文件；磁盘测速临时文件必须清理；
   ingest 清单走热更新通道，硬编码仅作兜底快照。

## 七、明确不做（本版边界）

- 不做自动修改 OBS 色彩/采样率设置（保持只读原则，仅给指引）;
- 不做真实推流质量打分（RTT 只是参考维度，避免误导）；
- 不做内置转写/字幕引擎（维持插件生态推荐路线，见 ROADMAP_PLUGINS.md）。
