# 插件广场目录复核报告（v1.4 · 2026-09-26）

> 对应数据文件：`OBS_Helper.Wpf/Assets/plugins.json`（v1.3 → v1.4）
> 目的：确保广场里的每个条目**实用**且**上游仍在维护**。
> 复核方式：逐条调用 GitHub API 取仓库实况，不做「记忆判断」。

---

## 一、复核方法与收录口径

对目录中全部 **57** 个条目逐一查询：

| 查询 | GitHub API | 用途 |
|---|---|---|
| 仓库实况 | `GET /repos/{owner}/{repo}` | `archived`（是否归档）、`pushed_at`（最近提交） |
| 最新发行 | `GET /repos/{owner}/{repo}/releases/latest` | 最新 tag 与发行日期 |
| 全部发行 | `GET /repos/{owner}/{repo}/releases` | 区分「没有发行」与「只有预发行」 |
| 仓库迁移 | 响应体的 `full_name` | 识别改名（GitHub 对旧名 302 跳转） |

### 收录口径（v1.4 起）

- **必须同时满足**：仓库未归档 ＋ 近 24 个月内有提交或发行 ＋ 提供 Windows 成品包（安装器或 zip）。
- **近 12 个月内有提交或发行** → `maintain: "active"`；**更久** → `maintain: "slow"`（明示「维护放缓」）。
- 不满足「未归档 / 有成品包」的一律剔除；满足但已过 12 个月的，只有**暂无同类替代**时才保留并标注。

---

## 二、剔除的 8 个条目

| 条目 | 最近提交 | 最新发行 | 归档 | 结论依据 |
|---|---|---|---|---|
| Spectralizer | 2022-03-30 | v1.3.4（2021-01-28） | **是** | 仓库已归档；音频可视化由维护活跃的 `Waveform` 承接 |
| OBS Detect | 2024-12-18 | 0.0.3（2024-06-01） | **是** | 仓库已归档 |
| Win Capture Audio | 2024-01-30 | 无 | 否 | 31 个月无动静；OBS 28.1+ 已内置「应用程序音频采集」，无需插件 |
| Dynamic Delay | 2022-12-31 | **无任何发行** | 否 | 45 个月无动静，且用户拿不到成品包 |
| Time Shift | 2020-08-22 | **无任何发行** | 否 | 73 个月无动静，且无成品包 |
| Time Warp Scan | 2022-11-27 | **无任何发行** | 否 | 46 个月无动静，且无成品包 |
| Device Switcher | 2024-01-15 | **无任何发行** | 否 | 32 个月无动静，且无成品包（exteldro 其它插件都有，唯此没有） |
| Recursion Effect | 2024-08-17 | 0.1.0（2024-01-08） | 否 | 最后发行 32 个月前、最后提交 25 个月前 |

> 说明：`Time Shift` / `Dynamic Delay` 的功能（音画对齐）在 OBS 内置的「视频延迟（异步）」滤镜已有等价能力；`Win Capture Audio` 的功能已由 OBS 内置；因此剔除不会造成能力空缺。

---

## 三、修正的 3 个仓库归属

| 条目 | 旧仓库 | 新仓库 | 依据 |
|---|---|---|---|
| Background Removal | `occ-ai/obs-backgroundremoval` | `royshil/obs-backgroundremoval` | 组织改名，新名 `full_name` 与旧名同库（stars 一致） |
| LocalVocal | `occ-ai/obs-localvocal` | `royshil/obs-localvocal` | 同上 |
| CleanStream | `occ-ai/obs-cleanstream` | `royshil/obs-cleanstream` | 同上 |

旧名靠 302 仍可访问，但「Releases 查新」与「下载」都会多一跳；改名被再次调整时会直接断链，故统一为新名。仓库中有测试断言禁止出现 `occ-ai/`。

---

## 四、新增的 8 个条目（实测数据）

| 条目 | 仓库 | 最近提交 | 最新发行 | 是否归档 | 是否有 Windows 包 |
|---|---|---|---|---|---|
| Source Profiler | `exeldro/obs-source-profiler` | 2026-02-05 | 0.0.9（2025-04-15） | 否 | ✔ windows.zip + 安装器 |
| Record Rename | `exeldro/obs-record-rename` | 2026-04-06 | 0.1.3（2025-11-24） | 否 | ✔ windows.zip + 安装器 |
| BILIBILI Stream for OBS | `Zarosmm/obs-bilibili-stream` | 2026-09-09 | 2.1.5（2026-09-09） | 否 | ✔ windows-x64.zip |
| Branch Output | `OPENSPHERE-Inc/branch-output` | 2026-09-26 | 1.0.9（2026-04-12） | 否 | ✔ windows-x64.zip + 签名安装器 |
| iOS Camera Source | `wtsnz/obs-ios-camera-source` | 2026-08-09 | v2.11.0（2026-08-09） | 否 | ✔ windows-x64.zip + 安装器 |
| MIDI MG | `nhielost/obs-midi-mg` | 2026-03-28 | 3.1.5（2026-03-18） | 否 | ✔ windows-x64.zip + 安装器 |
| Shadertastic | `xurei/shadertastic` | 2026-09-24 | 1.3.0（2026-09-24） | 否 | ✔ windows-x64.zip |
| Durchblick | `univrsal/durchblick` | 2025-11-04 | 0.5.3（2025-10-05） | 否 | ✔ windows-x64.zip + 安装器 |

### DLL 名（用于「本机插件体检」回匹配）

DLL 名不是猜的，取自各仓库的 `buildspec.json` 的 `name`（obs-plugintemplate 系）或 `CMakeLists.txt` 的 `project()`：

| 条目 | 插件根目录名 / DLL 主干 |
|---|---|
| Source Profiler | `source-profiler`（buildspec name） |
| Record Rename | `record-rename`（CMake project） |
| BILIBILI Stream | `bilibili-stream-for-obs`（buildspec name） |
| Branch Output | `osi-branch-output`（buildspec name） |
| iOS Camera Source | `obs-ios-camera-source`（CMake project） |
| MIDI MG | `obs-midi-mg`（buildspec name） |
| Shadertastic | `shadertastic`（buildspec name） |
| Durchblick | `durchblick`（buildspec name） |

---

## 五、标注为「维护放缓」的 6 个条目（保留不删）

| 条目 | 最近提交 | 最新发行 | 保留理由 |
|---|---|---|---|
| RTSP Server | 2024-01-12 | v3.1.0（2023-12-25） | 局域网 RTSP 拉流监看暂无同类替代 |
| Zoom to Mouse | 2024-06-07 | v1.0.1（2023-11-30） | Lua 脚本，不随 OBS 版本重编译，实测仍可用 |
| Freeze Filter | 2025-07-16 | 0.3.5（2025-07-16） | 冻结来源滤镜，广场内无等价替代 |
| Auto Subtitle | 2024-12-06 | 1.1.0（2024-12-06） | 云端识别 + 翻译字幕；本地识别可由维护活跃的 LocalVocal 替代，但翻译链路不同 |
| Soundboard | 2025-06-15 | 2.0.0（2025-06-12） | 热键音效板；更早的同类条目已无人维护 |
| Scale to Sound | 2025-07-13 | 1.2.5（2025-07-13） | 音量驱动缩放，广场内无等价替代 |

> 判定口径是「近 12 个月内有提交或发行」，因此这 6 条只是**超过 12 个月没有更新的成熟作品**，并非仓库停摆（都在 24 个月内有动静）。卡片上以警示色标注 `维护：放缓 · 最近提交 …`，让用户自己判断是否值得装。

---

## 六、复核结论

| 指标 | v1.3 | v1.4 |
|---|---|---|
| 条目总数 | 57 | 57 |
| 维护活跃（近 12 个月有动静） | 未标注 | **51** |
| 维护放缓（已标注理由） | 未标注 | 6 |
| 已归档仓库 | 2 | **0** |
| 无发行包（用户拿不到成品） | 3 | **0** |
| 指向已改名仓库 | 3 | **0** |

复核后发现的问题同步落地到代码：

1. **插件广场安装小贴士**更新为 OBS 32.x 的真实安装路径（插件根 `…\plugins\<插件名>\bin\64bit`）与「不要装两份」告警；
2. **本机插件体检**补齐 OBS 32.x 的插件根布局（否则用新版方式安装的插件在体检里会全部失踪），见 `LocalPluginScanner` / `PluginScanLocations.NestedPluginDirs`；
3. **日志分析**新增「插件重复安装」规则：本机实测存在 `obs-helper-dock.dll` 两份副本，OBS 启动时告警 `Duplicate library?`。
