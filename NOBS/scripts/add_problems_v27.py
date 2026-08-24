# -*- coding: utf-8 -*-
"""为 OBS_Helper 问题库新增 9 条问题（2026-08-24 网络调研，V2.7.0 优化指引）。
A 组：浏览器告警挂件失效、自定义 Dock 无法刷新、OBS 自身资源开销、双编码预算。
B 组：x264/NVENC 预设速查、录像 CQP 恒定质量、字幕转写插件方案、推流节点实测。
C 组：磁盘写入速度不足导致录制卡顿。
版本 1.9 -> 2.0。
注：拟定清单中的色彩范围 / 关键帧 / 动态码率 / 采样率 / 进程优先级 / 采集卡
六类痛点经核对已由既有条目覆盖（cf-colorrange、lag-keyint、lag-dynamic-bitrate、
au-sample-mismatch、cf-priority、bs-capturecard），本脚本不再重复收录。"""
import json

PATH = r"F:\OBS\NOBS\OBS_Helper.Wpf\Assets\problems.json"

KB = {"title": "OBS 官方 · 知识库（中文）", "url": "https://obsproject.com/zh-cn/kb"}
REL32 = {"title": "OBS 官方 · OBS Studio 32.x 发布说明", "url": "https://obsproject.com/blog/obs-studio-32-0-release-notes"}
WIN26 = {"title": "OBS Windows 排障指南（2026 版）", "url": "https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html"}
FORUM = {"title": "OBS 官方论坛 · Windows 支持", "url": "https://obsproject.com/forum/forums/windows-support.17/"}
GUIDE26 = {"title": "OBS 2026 直播设置指南（社区汇总）", "url": "https://techtippr.com/obs-settings-guide-for-streaming/"}

new_problems = [
    # ============================== A 组 ==============================
    {
        "id": "src-browser-alert",
        "category": "sources",
        "title": "浏览器源告警挂件空白 / 冻结：widget URL 过期与缓存清理",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": [
            "关注提醒 / 打赏告警（Streamlabs、StreamElements 等）在直播里不显示或一直空白",
            "告警以前正常，某次重置 token 或换服务后就再没出现过",
            "同一挂件在浏览器里打开正常，放进 OBS 就不刷新"
        ],
        "causes": [
            "告警服务的 widget URL 在重置 token / 重装服务后会重新生成，旧 URL 全部失效",
            "OBS 内置浏览器的缓存陈旧，页面停留在过期会话",
            "浏览器源勾选了「关闭不可见时刷新」，切场景回来后停在旧状态"
        ],
        "steps": [
            {"title": "先验证 URL 本身", "detail": "把告警服务的 widget URL 复制到普通浏览器打开：能正常显示说明 URL 有效，问题在 OBS 缓存；打不开就回告警后台重新生成 URL 并更新到 OBS。", "level": "基础"},
            {"title": "刷新浏览器源", "detail": "右键来源 → 刷新（或给「刷新缓存」设快捷键）；仍不行就把该来源删除重建，重新粘贴新 URL。", "level": "基础"},
            {"title": "清理内置浏览器缓存", "detail": "完全退出 OBS 后删除 %AppData%\\obs-studio\\obs-browser 目录（只删缓存不影响场景），重启 OBS。", "level": "进阶"},
            {"title": "检查硬件加速", "detail": "设置 → 高级 → 视频：「浏览器硬件加速」在部分老显卡驱动上会导致页面渲染异常，可尝试开关切换对比。", "level": "进阶"}
        ],
        "tips": [
            "重置告警服务 token 是 URL 失效的最常见原因，重置后所有相关浏览器源都要换新 URL",
            "重要直播前把每个告警源手动刷新一遍并试触发一次"
        ],
        "related": ["bs-browser-src", "cfg-browser-dock-refresh", "cf-webcam"],
        "links": [KB, FORUM]
    },
    {
        "id": "cfg-browser-dock-refresh",
        "category": "config",
        "title": "自定义浏览器 Dock 卡住无法刷新的变通方案",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "小众",
        "symptoms": [
            "视图 → 停靠窗口里添加的自定义网页面板（弹幕 / 后台 / 数据面板）内容卡住不更新",
            "浏览器来源有右键「刷新」，Dock 却没有任何刷新入口",
            "只能重启 OBS 才能恢复面板显示"
        ],
        "causes": [
            "截至 32.x，自定义浏览器 Dock 尚未提供与浏览器来源同级的刷新菜单 / 快捷键 / API（官方仓库已确认此能力缺口）",
            "Dock 与来源共用同一个浏览器内核，但刷新操作没有暴露到 Dock 层"
        ],
        "steps": [
            {"title": "变通一：改用浏览器来源", "detail": "把面板做成一个隐藏场景里的浏览器来源（不输出到画面），需要看时切到该场景；来源拥有完整的右键刷新能力。", "level": "基础"},
            {"title": "变通二：移除再添加 Dock", "detail": "视图 → 停靠窗口 → 自定义浏览器窗口，删除该条目后重新添加同 URL，等效强制重载。", "level": "基础"},
            {"title": "变通三：外部浏览器兜底", "detail": "把面板 URL 放到第二块屏幕的独立浏览器窗口；牺牲布局一体化换取稳定可控的刷新。", "level": "基础"},
            {"title": "跟进官方进展", "detail": "该缺口在官方仓库有明确 feature request，后续版本可能补齐；升级前留意 Release Notes。", "level": "进阶"}
        ],
        "tips": [
            "直播中不要依赖 AX/UI 自动化去点 Dock 右键刷新——会抢焦点且跨语言系统不可靠",
            "本助手的 Dock 版插件走原生协议，不受此问题影响"
        ],
        "related": ["bs-browser-src", "src-browser-alert", "cr-plugin-load"],
        "links": [KB, REL32]
    },
    {
        "id": "perf-obs-overhead",
        "category": "performance",
        "title": "OBS 本身的隐性开销：隐藏来源未关停、重复捕获与浏览器源数量",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常见",
        "symptoms": [
            "什么都没推也觉得游戏帧数比不开 OBS 时低一截",
            "CPU / 显存占用随直播时长缓慢上涨",
            "场景越多越卡，删掉几个场景又好转"
        ],
        "causes": [
            "来源未勾选「不可见时关闭」：藏在其他场景里的媒体源、浏览器源仍在解码 / 渲染",
            "每个浏览器源都是一个独立渲染进程，数量直接决定内存基线",
            "显示器捕获会把整个桌面合成进管线，代价高于游戏捕获",
            "重复捕获：同一画面用多个来源各抓一遍"
        ],
        "steps": [
            {"title": "审计来源列表", "detail": "逐个检查非常驻来源，属性里勾选「当不可见时关闭」（OBS 32.x 为「通过可见性控制激活」）；注意循环播放的视频源关闭后切回会从头播，这类保持常驻。", "level": "基础"},
            {"title": "合并重复捕获", "detail": "同一窗口 / 显示器只保留一个捕获源，多场景通过嵌套场景复用，而不是各场景各抓一份。", "level": "基础"},
            {"title": "控制浏览器源数量", "detail": "能合并的挂件合并成一个页面；长期直播每 2~3 小时右键刷新一次防内存膨胀；不用的告警 / 弹幕源直接删除而非隐藏。", "level": "基础"},
            {"title": "优先用游戏捕获", "detail": "全屏 / 无边框游戏用「游戏捕获」，比显示器捕获省一次桌面合成；反作弊冲突再降级到窗口 / 显示器捕获。", "level": "进阶"}
        ],
        "tips": [
            "怀疑开销高时做对照实验：安全模式启动（无插件无脚本）跑同样场景，差值就是第三方贡献的开销",
            "监控页盯着内存走势，持续单调上涨基本是浏览器源泄漏"
        ],
        "related": ["lag-gpu-cap", "cf-priority", "lag-browser", "bs-leak"],
        "links": [WIN26, GUIDE26]
    },
    {
        "id": "perf-dual-encode",
        "category": "performance",
        "title": "直播同时本地录像：双编码的 GPU 预算与降级顺序",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": [
            "单直播很稳，勾上「边播边录」就开始掉帧 / 编码过载",
            "录像画质和推流画质想分开调，不知道额外代价多大",
            "双编码开启后游戏帧数明显下降"
        ],
        "causes": [
            "高级输出模式下推流与录像是两路独立编码，即使同一块显卡也要排两次编码队列（约 +10~15% GPU 占用）",
            "x264 软编 + 硬编混搭时 CPU 与 GPU 各自吃满，互不让路"
        ],
        "steps": [
            {"title": "开播前先压测", "detail": "在吃配置的场景里同时开推流 + 录制跑 10 分钟，观察状态栏丢帧；现场翻车不如提前暴露。", "level": "基础"},
            {"title": "录像选低代价档位", "detail": "录像用硬件编码 + CQP 恒定质量（如 HEVC CQP 20），比推流档位更省心；NVENC 的 H.264/HEVC/AV1 会共享同一编码硬件队列，注意总吞吐上限。", "level": "基础"},
            {"title": "按顺序降级", "detail": "过载时依次尝试：录像分辨率降到 720p → 录像帧率减半 → 录像改 x264 veryfast（GPU 让给推流）→ 最后才砍推流参数。", "level": "进阶"},
            {"title": "考虑串流机 / 单路高质量", "detail": "预算允许上双机方案；或者干脆只保留一路高质量输出，回放直接用平台的直播录制。", "level": "进阶"}
        ],
        "tips": [
            "「推流用 H.264 平台兼容 + 录像用 HEVC/AV1 高质量」是最常见的双编码组合",
            "工具箱的编码顾问卡可以按你的显卡型号给出具体参数组合"
        ],
        "related": ["enc-overload", "enc-recording-cqp", "lag-gpu-cap", "setup-dual-pc"],
        "links": [GUIDE26, KB]
    },
    # ============================== B 组 ==============================
    {
        "id": "enc-preset-guide",
        "category": "encoder",
        "title": "预设怎么选：x264 档位与 NVENC P1~P7 分档速查",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常用技巧",
        "symptoms": [
            "不知道 x264 的 veryfast / medium 该选哪个，随手选了 slow 结果编码过载",
            "NVENC 更新后预设变成了 P1~P7，和老教程对不上号",
            "想提画质又怕掉帧，不敢动预设"
        ],
        "causes": [
            "x264 预设越慢画质越高，但 CPU 占用非线性上涨：从 veryfast 到 fast 约翻倍，medium 再翻倍",
            "OBS 30 起 NVIDIA 新预设体系 P1（最快）~ P7（最高质量）替代了旧的 quality/balanced 命名"
        ],
        "steps": [
            {"title": "x264 只推荐三档", "detail": "直播 veryfast 起步（过载再 ultrafast）；本地录制最多 fast；medium 及以上只适合离线渲染。", "level": "基础"},
            {"title": "NVENC 按 GPU 分代", "detail": "RTX 30/40/50 系选 P5（质量与性能平衡点）；GTX 16 / 20 系选 P4；过载降一档而不是降分辨率。", "level": "基础"},
            {"title": "AMF / QSV 对应档", "detail": "AMD 选 Quality 预设；Intel QSV 选 Balanced 起步，驱动较新的机器可试 Quality。", "level": "基础"},
            {"title": "改预设的验证方法", "detail": "每次只改一档，跑同样的场景 10 分钟，看日志分析里的编码滞后占比是否归零，再决定下一步。", "level": "进阶"}
        ],
        "tips": [
            "同码率下 P5 的 NVENC 观感普遍优于 x264 veryfast，有 N 卡优先硬编",
            "「预设降一档」永远比「分辨率降一级」对观感的伤害小"
        ],
        "related": ["enc-overload", "enc-nvenc", "enc-av1", "enc-cpu"],
        "links": [GUIDE26, KB]
    },
    {
        "id": "enc-recording-cqp",
        "category": "encoder",
        "title": "录像用恒定质量（CQP / CRF）：参考值与适用场景",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常用技巧",
        "symptoms": [
            "录像码率不知道填多少，低了糊高了占盘",
            "静态画面和激烈团战画质忽好忽坏",
            "听说 CQP 更聪明但不敢用"
        ],
        "causes": [
            "CBR 给每一秒分配相同比特，复杂画面不够用、静止画面浪费",
            "CQP / CRF 按「维持目标画质需要多少就给多少」分配，复杂场景自动多给比特"
        ],
        "steps": [
            {"title": "速率控制改成 CQP", "detail": "设置 → 输出 → 录像 → 速率控制选 CQP（NVENC）/ CRF（x264）；数值越小质量越高体积越大。", "level": "基础"},
            {"title": "按编码器取值", "detail": "NVENC H.264：CQP 18 基本无损级、20 均衡；HEVC 同观感可再 +2；AV1（40 系+）：CQP 22 约等于 HEVC 18 的观感且体积小三成；x264：CRF 18~23。", "level": "基础"},
            {"title": "磁盘紧张再上调", "detail": "1080p60 游戏 CQP 18 大约每小时 15~40GB（视内容波动）；空间不够优先上调 2 个点而不是换 CBR。", "level": "基础"},
            {"title": "直播推流别用 CQP", "detail": "平台要求稳定码率，推流保持 CBR；CQP 只用于本地录像这条支路。", "level": "进阶"}
        ],
        "tips": [
            "剪辑素材用 CQP 录制可避免二次压缩叠加劣化",
            "先试录 5 分钟估算体积，再决定要不要微调数值"
        ],
        "related": ["rc-mkv", "rc-fps-specs", "perf-dual-encode", "enc-av1"],
        "links": [GUIDE26, KB]
    },
    {
        "id": "vc-caption-plugins",
        "category": "virtualcam",
        "title": "直播字幕 / 语音转写：原生缺失下的插件方案",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常用技巧",
        "symptoms": [
            "想给直播加实时字幕（听障观众 / 静音观看场景），发现 OBS 没有原生功能",
            "装了某个字幕插件识别延迟大或不支持中文",
            "担心字幕插件吃性能"
        ],
        "causes": [
            "OBS 官方至今没有内置语音转写字幕功能，该需求完全由插件生态承担",
            "不同插件的引擎（云端 API / 本地模型）在延迟、语种、离线能力上差异巨大"
        ],
        "steps": [
            {"title": "本地推理优先", "detail": "LocalVocal、Auto Subtitle 这类本地模型插件无需联网不上传音频，中文支持可用；首次加载模型有几秒延迟属正常。", "level": "基础"},
            {"title": "云端引擎权衡", "detail": "云 API 识别准但按量计费且有网络依赖；直播场景务必准备断网时的降级预案（直接隐藏字幕源）。", "level": "基础"},
            {"title": "性能预算", "detail": "本地转写主要吃 CPU / 内存，与硬编码不抢 GPU；低配机建议降低模型档位或只在说话时启用。", "level": "进阶"},
            {"title": "样式与安全区", "detail": "字幕文本源放在画面下三分之一安全区内，避开平台进度条遮挡区；字号以手机端可读为准。", "level": "进阶"}
        ],
        "tips": [
            "插件广场的 AI 分类已收录 LocalVocal / Auto Subtitle / CleanStream 等精选，全部经过仓库可达性验证",
            "字幕有 2~5 秒延迟是技术常态，主播适当放慢语速体验更好"
        ],
        "related": ["st-chat", "au-random-desync", "au-mic-noise"],
        "links": [KB]
    },
    {
        "id": "sf-ingest-ping",
        "category": "streamfail",
        "title": "推流节点怎么选：按 ping 与质量实测，而非默认自动",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常见",
        "symptoms": [
            "自动选择的推流服务器经常掉帧，换个节点立刻好转",
            "离我地理最近的节点反而不如远一点的质量好",
            "不知道有哪些节点可选、怎么测好坏"
        ],
        "causes": [
            "OBS 的「自动（推荐）」通常按地理位置就近选择，但运营商路由绕行会让近节点绕远路",
            "节点质量随时段波动，昨晚最优不代表今晚最优"
        ],
        "steps": [
            {"title": "列出候选节点", "detail": "设置 → 推流 → 服务选对应平台，服务器下拉框即候选列表；Twitch 用户还可用社区测速工具逐个测质量。", "level": "基础"},
            {"title": "按 ping 初筛", "detail": "对候选域名做 TCP 连接测试（本助手工具箱提供探测入口），筛出 RTT 较低的 2~3 个；注意 ping 低只是必要条件不是充分条件。", "level": "基础"},
            {"title": "实推验证", "detail": "对入围节点各实推 10 分钟，比较状态栏丢帧百分比；0% 丢帧且码率稳定的即为当前最优。", "level": "基础"},
            {"title": "定期复测", "detail": "网络环境或平台调整都会改变最优解，每月或掉帧复发时复测一轮即可，不必天天折腾。", "level": "进阶"}
        ],
        "tips": [
            "WiFi 环境下 ping 波动大，测节点前先用有线排除局域网变量",
            "掉帧伴随码率骤降多半是节点或运营商 QoS，换节点往往立竿见影"
        ],
        "related": ["sf-server", "lag-network", "lag-wifi", "sf-timeout"],
        "links": [GUIDE26, KB]
    },
    # ============================== C 组 ==============================
    {
        "id": "rc-disk-speed",
        "category": "recording",
        "title": "录制卡顿但编码正常：磁盘写入速度不足（HDD / 满盘 SSD）",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "偶发",
        "symptoms": [
            "日志里没有编码过载，但录像每隔几秒卡一下",
            "录制写到后半段越来越卡（盘越写越满）",
            "机械硬盘录 1080p60 高码率频繁丢帧"
        ],
        "causes": [
            "机械硬盘顺序写入只有 100~200MB/s 且随机读写更差，多任务时不足以支撑高码率录像的持续写入",
            "SSD 写满接近容量或缓外速度暴跌，长录制中途掉速",
            "录制盘同时承载下载 / 素材整理等 IO 任务，带宽被抢"
        ],
        "steps": [
            {"title": "先测磁盘写入速度", "detail": "对录像所在盘做顺序写基准（本助手工具箱提供测试入口）：结果应 ≥ 计划码率 ÷ 8000 × 1.5 以上（例如 20000kbps 至少需要约 3.75MB/s 的持续写入余量）。", "level": "基础"},
            {"title": "换 SSD / NVMe", "detail": "高码率录像一律建议 SATA SSD 起步；机械盘留给成品归档，不做录制工作盘。", "level": "基础"},
            {"title": "留足空间与 TRIM", "detail": "SSD 保持 15% 以上空闲；近满盘是长录制后半段卡顿的典型原因。", "level": "基础"},
            {"title": "错峰 IO", "detail": "录制时暂停网盘同步、备份与下载任务；必要时开启自动分段，给磁盘喘息间隔。", "level": "进阶"}
        ],
        "tips": [
            "「编码滞后 0% 但录像卡」基本可以直接锁定磁盘链路",
            "录前体检会检查剩余空间，配合本条的测速可以完整评估磁盘风险"
        ],
        "related": ["rc-disk-space", "av-drift-long", "rc-nofile", "rc-schedule"],
        "links": [WIN26, GUIDE26]
    },
]

with open(PATH, "r", encoding="utf-8") as f:
    data = json.load(f)

existing = {p["id"] for p in data["problems"]}
dupes = [p["id"] for p in new_problems if p["id"] in existing]
if dupes:
    raise SystemExit(f"重复 id: {dupes}")

# 校验 related 引用的 id 都真实存在
all_known = existing | {p["id"] for p in new_problems}
bad = [(p["id"], r) for p in new_problems for r in p.get("related", []) if r not in all_known]
if bad:
    raise SystemExit(f"related 引用不存在: {bad}")

data["problems"].extend(new_problems)
data["version"] = "2.0"

with open(PATH, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
    f.write("\n")

print(f"OK: v{data['version']}, {len(data['problems'])} problems (+{len(new_problems)})")
