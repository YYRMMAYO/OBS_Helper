# -*- coding: utf-8 -*-
"""为 OBS_Helper 问题库新增 18 条问题（2026-08-24 网络调研，V2.6.0 优化指引 A/B/C 组）。
A 组：Windows 更新异常、Hybrid MP4 兼容、蓝牙麦克风、侧链闪避、日志分析器、
      会议录制、定时录制、隐私屏蔽、更多平台接入。
B 组：插件管理器、SDR 转 HDR、WebRTC 联播、RTX 音频滤镜、撤销重做、升级前备份。
C 组：双 PC 推流、设备中途掉线、帧率平台规格匹配。
版本 1.8 -> 1.9。"""
import json

PATH = r"F:\OBS\NOBS\OBS_Helper.Wpf\Assets\problems.json"

KB = {"title": "OBS 官方 · 知识库（中文）", "url": "https://obsproject.com/zh-cn/kb"}
ANALYZER = {"title": "OBS 官方 · 日志分析器", "url": "https://obsproject.com/analyzer"}
REL32 = {"title": "OBS 官方 · OBS Studio 32.x 发布说明", "url": "https://obsproject.com/blog/obs-studio-32-0-release-notes"}
WIN26 = {"title": "OBS Windows 排障指南（2026 版）", "url": "https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html"}
FORUM = {"title": "OBS 官方论坛 · Windows 支持", "url": "https://obsproject.com/forum/forums/windows-support.17/"}

new_problems = [
    # ============================== A 组 ==============================
    {
        "id": "os-win-update", "category": "black-screen",
        "title": "Windows 系统更新后 OBS 异常（黑屏 / 设备消失 / 虚拟摄像头失效）",
        "platforms": ["Windows"],
        "severity": "常见",
        "symptoms": [
            "Windows 累积更新重启后，显示器 / 窗口捕获变黑屏",
            "睡眠唤醒后音频设备从混音器里消失，或不再有声音",
            "更新前可用的虚拟摄像头，在 Zoom / 腾讯会议里突然失效",
            "OBS 本身没动过任何设置，行为却变了"
        ],
        "causes": [
            "系统累积更新可能更换图形驱动栈或重置隐私权限，捕获钩子失效（如 2026 年初 KB5074109 引发批量黑屏反馈）",
            "更新会重置应用的摄像头 / 麦克风权限与默认音频设备",
            "部分更新的电源管理回归会影响 USB 设备重新枚举",
            "OBS 官方也随更新发布适配补丁（32.0.2 修复睡眠唤醒后音频设备消失，32.0.3 修复 Windows 更新后虚拟摄像头失效）"
        ],
        "steps": [
            {"title": "先更新 OBS 到最新版", "detail": "设置 → 一般 → 检查更新。多个「更新后异常」正是 OBS 随后的补丁版修掉的，先排除版本因素。", "level": "基础"},
            {"title": "重选一遍音频 / 摄像头设备", "detail": "更新常重置默认设备：设置 → 音频里重新选择桌面音频与麦克风；来源里的视频捕获设备重新指定一次摄像头。", "level": "基础"},
            {"title": "检查系统权限", "detail": "Windows 设置 → 隐私和安全性 → 相机 / 麦克风，确认「允许桌面应用访问」已开启；OBS 若被关掉权限，捕获与虚拟摄像头都会失效。", "level": "基础"},
            {"title": "黑屏按捕获黑屏流程排查", "detail": "管理员运行 OBS、笔记本把 OBS 指定到独显、切换捕获方式——详见「显示器捕获黑屏」条目；若仍不行可卸载该更新观察。", "level": "进阶"}
        ],
        "tips": [
            "大版本 Windows 更新落地当天，先别急着开播，跑一次录前自检再开工",
            "本助手「工具箱」页可一键做录前体检，覆盖路径 / 编码器 / 音频配置"
        ],
        "related": ["bs-display", "bs-win11", "st-virtualcam", "au-mute"],
        "links": [KB, FORUM, REL32]
    },
    {
        "id": "rc-hybrid-mp4", "category": "recording",
        "title": "Hybrid MP4/MOV 成为默认格式后的兼容性问题",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常见",
        "symptoms": [
            "升级 OBS 后新 Profile 的录像变成 hybrid_mp4 格式，老播放器 / 剪辑软件打不开",
            "输出到不可写位置时文件异常（旧版还有内存泄漏问题）",
            "想按老教程改回 MKV 却找不到原来的选项"
        ],
        "causes": [
            "OBS 32.0 起 Hybrid MP4/MOV 转正并成为新 Profile 的默认输出格式（防崩溃 + 支持章节标记）",
            "部分旧版播放器、剪辑软件与转码工具不识别其封装变体",
            "老教程截图还是 MKV/MP4 二选一的界面，用户找不到对应设置"
        ],
        "steps": [
            {"title": "优先用官方「录像转封装」", "detail": "OBS 内 文件 → 录像转封装（Remux），hybrid MP4 转 MP4/MKV 只改封装不重编码，秒级完成。", "level": "基础"},
            {"title": "换用兼容的播放 / 剪辑软件", "detail": "VLC、较新版本的 Premiere / 剪映等均可直接读 Hybrid MP4；剪辑软件报错时先升级它再考虑转格式。", "level": "基础"},
            {"title": "不喜欢就改回 MKV", "detail": "设置 → 输出 → 录像格式，改回「Matroska (MKV)」即可，防崩溃能力等同；本助手工具箱也提供 ffmpeg 重封装入口。", "level": "进阶"},
            {"title": "保持 OBS 为最新补丁版", "detail": "32.0 早期对写保护位置的 Hybrid MP4 输出存在内存泄漏，已在后续补丁修复。", "level": "进阶"}
        ],
        "tips": [
            "Hybrid MP4 本身是防崩溃设计，不必恐慌回退；解决不了再换格式",
            "上传平台只认 MP4 时，转封装即可，不要重新导出浪费时间"
        ],
        "related": ["rc-mkv", "rc-remux", "rc-nofile"],
        "links": [REL32, KB]
    },
    {
        "id": "au-bluetooth", "category": "audio",
        "title": "蓝牙耳机做麦克风：人声发闷、延迟大、音画漂移",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": [
            "连上蓝牙耳机后，录音质量明显下降、听起来像电话音质",
            "嘴动声音半秒后才响，音画逐渐错位",
            "同时开蓝牙耳机听声音 + 用它收音时更严重"
        ],
        "causes": [
            "蓝牙耳机的麦克风走 HFP（免提）协议，采样率与带宽远低于 A2DP 音频通道，音质必然差",
            "HFP 通道延迟高且不稳定，是音画漂移的重灾区",
            "系统在「立体声耳机（A2DP）」与「免提（HFP）」模式间切换时，OBS 采集会中断或错位"
        ],
        "steps": [
            {"title": "收音别用蓝牙麦", "detail": "人声采集改用有线耳机麦 / 独立麦克风 / 手机耳机线控；这是唯一根治办法。", "level": "基础"},
            {"title": "只听不录可以留蓝牙", "label": "", "detail": "监听走蓝牙 A2DP 没有问题：OBS 设置 → 音频 → 高级 → 监听设备选有线输出或另配，避免监听又回到蓝牙麦。", "level": "基础"},
            {"title": "必须用时统一 48kHz 并加同步偏移", "detail": "系统与 OBS 采样率都设 48kHz；在混音器 → 高级音频属性里给蓝牙轨手动加同步偏移补偿固定延迟。", "level": "进阶"},
            {"title": "漂移严重就后期替换音轨", "detail": "录制时用 MKV 双音轨（麦克风单独一轨），后期对齐替换，比现场硬扛更稳。", "level": "进阶"}
        ],
        "tips": [
            "判断当前协议：Windows 声音设置里出现「Hands-Free / 免提」字样即 HFP 模式",
            "直播场景绝对不建议蓝牙收音，观众端延迟会被放大"
        ],
        "related": ["av-drift", "au-sample-mismatch", "av-sample", "au-mic-noise"],
        "links": [KB, WIN26]
    },
    {
        "id": "au-ducking", "category": "audio",
        "title": "说话时音乐自动变小：OBS 压缩器侧链（Ducking）做法",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常用技巧",
        "symptoms": [
            "开麦说话时背景音乐盖住人声，观众听不清",
            "手动拉推子顾不过来，忘记调回来又被观众吐槽",
            "已试过 Windows「通信活动」自动降音量，但只在特定条件下触发、不好控制"
        ],
        "causes": [
            "BGM 与人声没有主次关系，靠人工调音量不可持续",
            "Windows 通信活动只认「通话类应用」，OBS 触发不可靠，需要应用内方案"
        ],
        "steps": [
            {"title": "给 BGM 加压缩器滤镜", "detail": "混音器里 BGM 轨 → 滤镜 → 添加「压缩器（Compressor）」。", "level": "基础"},
            {"title": "把侧链源设为麦克风", "detail": "压缩器的「侧链来源」选择你的麦克风轨道：麦克风一有声，BGM 就被压低。", "level": "基础"},
            {"title": "调三组参数", "detail": "比率 4:1~8:1、阈值 -30~-24dB、释放 300~800ms：说话时 BGM 明显让位、停顿后平滑恢复。", "level": "进阶"},
            {"title": "配合人声链路效果更好", "detail": "麦克风先做降噪 / 门限处理再作侧链源，可避免环境噪音误触发光 BGM。", "level": "进阶"}
        ],
        "tips": [
            "这条与「开麦说话游戏音量自动变小（Windows 通信活动）」是两套机制，OBS 内侧链可控性最好",
            "参数宁轻勿重，压得太狠音乐会显得忽快忽慢"
        ],
        "related": ["au-comm-lower", "au-mic-chain", "au-vst", "au-monitor"],
        "links": [KB]
    },
    {
        "id": "cfg-log-analyzer", "category": "config",
        "title": "如何导出日志并使用官方日志分析器排查",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常用技巧",
        "symptoms": [
            "论坛求助时被要求提供 log file，不知道去哪找",
            "把日志直接粘贴到公开场合，担心泄露串流密钥",
            "拿到 obsproject.com/analyzer 的结果看不懂下一步该做什么"
        ],
        "causes": [
            "OBS 日志记录了每次会话的全部警告与错误，是最客观的排查依据",
            "原始日志含推流地址 / 密钥片段，公开前需要脱敏"
        ],
        "steps": [
            {"title": "复现问题后再取日志", "detail": "先让问题发生（开播 / 录制至少 30 秒），再点 帮助 → 日志文件 → 查看当前日志；排障要的是「出事那一次」的日志。", "level": "基础"},
            {"title": "上传到官方分析器", "detail": "帮助 → 日志文件 → 上传日志文件，得到 obsproject.com/logs/xxx 链接，粘贴到 https://obsproject.com/analyzer 自动解读。", "level": "基础"},
            {"title": "或者用本助手离线分析", "detail": "诊断页 → 日志分析：本地脱敏 + 关键字命中知识库条目，不需要联网也不泄露密钥。", "level": "基础"},
            {"title": "按分析结果的严重度顺序处理", "detail": "先治 Critical / Error，再处理 Warning；一次只改一类设置，改完复测再看新日志。", "level": "进阶"}
        ],
        "tips": [
            "求助帖附「分析器链接 + 一句现象描述」命中率最高",
            "崩溃问题还要带 %AppData%\\obs-studio\\crashes 下对应时间的转储说明"
        ],
        "related": ["lag-stats", "cr-safe-mode", "sf-key-leak"],
        "links": [ANALYZER, KB]
    },
    {
        "id": "rc-meeting", "category": "recording",
        "title": "录制网课 / 视频会议窗口捕获失败（Zoom / 腾讯会议 / Teams）",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": [
            "窗口捕获列表里根本找不到会议软件窗口",
            "能选但画面黑屏或只有一片纯色",
            "共享 PPT 时录到的却是演讲者视图而不是共享内容"
        ],
        "causes": [
            "会议软件的全屏 / 演讲者窗口用了特殊渲染层，普通窗口捕获抓不到",
            "浏览器打开的网页版会议需要捕获浏览器进程而非会议窗口",
            "UWP / 受保护窗口在普通权限下不可见"
        ],
        "steps": [
            {"title": "优先捕获「共享内容」专用窗口", "detail": "开始共享后，窗口捕获列表会出现「Zoom 共享屏幕 / Meeting 共享内容」之类的独立窗口，直接捕获它的画面最干净。", "level": "基础"},
            {"title": "抓不到就用显示器捕获兜底", "detail": "把会议全屏化后用显示器捕获整屏，后期裁剪；稳定性最高，代价是录进所有弹窗（配合隐私清单使用）。", "level": "基础"},
            {"title": "网页版会议捕获浏览器窗口", "detail": "窗口捕获选浏览器进程；黑屏时在浏览器里关闭硬件加速再重试。", "level": "进阶"},
            {"title": "勾选窗口捕获的兼容性选项", "detail": "来源属性里开启「Windows 10 (1903+) 的 WGC 方法（Windows 图形捕获）」，对这类特殊窗口成功率更高。", "level": "进阶"}
        ],
        "tips": [
            "录课前先试录 10 秒验证画面与声音，比课后发现翻车强一百倍",
            "网课场景建议直接用「录前体检」（工具箱页）：路径 / 格式 / 音频一次查完"
        ],
        "related": ["bs-window", "bs-display", "rc-black", "os-win-update"],
        "links": [WIN26, FORUM]
    },
    {
        "id": "rc-schedule", "category": "recording",
        "title": "定时录制 / 自动分段录制",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常用技巧",
        "symptoms": [
            "想在指定时间自动开始 / 停止录制（人不在电脑前）",
            "长时间录制希望每 30 分钟 / 每 2GB 自动存成一段，防止单文件损坏全丢",
            "用过第三方「计划任务 + 快捷键」的土办法，时灵时不灵"
        ],
        "causes": [
            "OBS 至今没有原生的定时开关录制功能",
            "长单文件一旦损坏（断电 / 崩溃 / 磁盘满）损失全部内容，分段是把风险切开"
        ],
        "steps": [
            {"title": "自动分段：输出设置里直接配", "detail": "设置 → 输出 → 录像：「自动分割文件」按时间 / 大小切分；配合 MKV 格式，每段独立防崩溃。", "level": "基础"},
            {"title": "定时开始 / 停止：obs-websocket 方案", "detail": "设置 → WebSocket 服务器开启后，任何支持 obs-websocket 协议的工具都能按时间表调用 StartRecord / StopRecord；本助手的 OBS 控制台即基于该协议。", "level": "基础"},
            {"title": "脚本方案（进阶）", "detail": "OBS 内置 Lua / Python 脚本（工具 → 脚本）也有社区现成的定时录制脚本可装；Windows 任务计划程序 + obs-cli 同理。", "level": "进阶"},
            {"title": "兜底：快捷键 + 提醒", "detail": "实在不想折腾，给「开始 / 停止录制」设全局热键，再用手机定闹钟提醒自己。", "level": "基础"}
        ],
        "tips": [
            "自动分割建议 15~60 分钟一段：太碎难管理，太长风险大",
            "磁盘剩余空间不足会让最后一段损坏，录前体检会帮你预估可用时长"
        ],
        "related": ["rc-mkv", "rc-disk-space", "rc-4k"],
        "links": [KB, WIN26]
    },
    {
        "id": "rc-privacy", "category": "recording",
        "title": "录制时屏蔽系统通知 / 隐私信息入镜",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": [
            "微信 / QQ 消息弹窗被完整录进视频，内容社死",
            "桌面上的文件名、浏览器标签页、聊天预览出现在成品里",
            "录完才发现只能打码或重录"
        ],
        "causes": [
            "屏幕捕获是「所见即所得」，任何前台弹窗都会入镜",
            "通知横幅、任务栏缩略图、输入法候选词都属于容易被忽略的隐私面"
        ],
        "steps": [
            {"title": "开启勿扰 / 专注助手", "detail": "Win+N 打开通知中心勾选「勿扰」；或 设置 → 系统 → 通知 里临时关闭通知（本助手工具箱提供直达按钮）。macOS 开启「专注模式」。", "level": "基础"},
            {"title": "清理桌面与任务栏", "detail": "桌面图标收进文件夹、任务栏关闭「显示聊天 / 小组件」、右下角时间含日期的考虑裁剪画面。", "level": "基础"},
            {"title": "用窗口捕获缩小暴露面", "detail": "只捕获目标应用窗口而不是整个显示器，天然隔离弹窗；会议 / 网课场景尤其有效。", "level": "基础"},
            {"title": "浏览器专门清理", "detail": "无痕窗口录制、隐藏书签栏、退出多余账号；地址栏里的 token 参数同样算敏感信息。", "level": "进阶"}
        ],
        "tips": [
            "正式录制前先录 5 秒回放检查一遍画面边缘，成本最低的保险",
            "竖屏短视频裁剪幅度大，画面边缘最容易带出隐私内容"
        ],
        "related": ["bs-window", "rc-meeting", "cf-canvas"],
        "links": [WIN26]
    },
    {
        "id": "setup-more-platforms", "category": "setup",
        "title": "快手 / 淘宝 / 京东直播接入，以及「直播伴侣 vs 纯 OBS」怎么选",
        "platforms": ["Windows"],
        "severity": "常用技巧",
        "symptoms": [
            "平台后台找不到 RTMP 推流地址与串流密钥入口",
            "不确定该用平台官方伴侣工具还是 OBS 直接推流",
            "用 OBS 推流后手机端预览画质 / 声音异常"
        ],
        "causes": [
            "各电商 / 短视频平台的开放程度不同：有的提供网页开播入口，有的仅支持官方客户端",
            "部分平台对非官方推流有限流、清晰度降档甚至封禁的风险策略",
            "抖音停止第三方 RTMP 入口之后，「哪些平台还能 OBS 直推」成为高频疑问"
        ],
        "steps": [
            {"title": "先查平台是否还开放 RTMP", "detail": "快手部分主播可在创作者后台申请推流地址；淘宝 / 京东主要面向商家开放，需在中控台确认资格。拿不到地址就只能用官方工具。", "level": "基础"},
            {"title": "选型原则", "detail": "要平台特效 / 商品挂载 / 连麦玩法 → 官方伴侣；要自定义场景合成、多平台分发 → OBS（或 OBS 推给伴侣中转）。", "level": "基础"},
            {"title": "OBS 推流配置", "detail": "服务选「自定义」，填平台给的 服务器地址 + 串流密钥；码率按平台建议值（一般 4000~6000kbps），关键帧 2 秒。", "level": "基础"},
            {"title": "合规提示", "detail": "绕过官方入口的「无人直播 / 抓流」方案违反平台规则，有封号风险；本库只收录合规路径。", "level": "进阶"}
        ],
        "tips": [
            "平台政策变化频繁，开播前以平台中控台当日的说明为准",
            "多平台分发的合规做法参考「多平台同时推流」条目"
        ],
        "related": ["st-general", "st-douyin", "st-multi", "st-bilibili"],
        "links": [KB]
    },
    # ============================== B 组 ==============================
    {
        "id": "cr-plugin-manager", "category": "crash",
        "title": "用好 32.x 插件管理器：启用 / 禁用缺失插件",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常用技巧",
        "symptoms": [
            "启动时提示「缺失插件 / 未加载插件」，不知道装了哪些、哪个坏了",
            "怀疑某个插件导致崩溃，想去掉它试试却找不到 DLL 在哪",
            "安全模式正常、正常模式出问题，想二分定位"
        ],
        "causes": [
            "OBS 32.0 新增了内置插件管理器（早期版本 UI 较简单，32.1 起逐步增强）",
            "旧插件残留、架构不匹配（ARM/x64）、依赖缺失都会被列进「未加载」清单"
        ],
        "steps": [
            {"title": "打开插件管理器总览", "detail": "菜单 工具 → 插件（Plugin Manager）：能看到全部已加载 / 未加载插件的清单与状态。", "level": "基础"},
            {"title": "按清单逐个处理未加载项", "detail": "未加载项通常是版本过旧或文件残留：去插件项目 Releases 重装最新版，或直接删掉旧 DLL。", "level": "基础"},
            {"title": "禁用可疑插件做二分排查", "detail": "崩溃 / 异常时先禁用一半插件重启验证，再对可疑的一半折半，几轮就能锁定肇事者。", "level": "进阶"},
            {"title": "结合本助手插件体检", "detail": "插件广场 → 本机体检：扫描安装目录里的 DLL 版本并与目录比对，和管理器互为印证。", "level": "进阶"}
        ],
        "tips": [
            "升级 OBS 大版本前，先把插件管理器里的清单截个图，出问题好对照",
            "「未验证插件」弹窗出现时选择继续进入，再去管理器里处理，不必急着清空"
        ],
        "related": ["cr-plugin-load", "cr-plugin", "cr-safe-mode", "cr-streamfx"],
        "links": [REL32, KB]
    },
    {
        "id": "cfg-sdr2hdr", "category": "config",
        "title": "SDR 转 HDR 合成滤镜的使用场景与误区（32.2 新增）",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "小众",
        "symptoms": [
            "HDR 输出场景里，插入的 SDR 来源（普通图片 / 老素材）显得发灰或刺眼",
            "找不到把普通素材融入 HDR 画布的办法",
            "加了滤镜后颜色反而不对"
        ],
        "causes": [
            "HDR 项目里 SDR 内容默认按简单映射转换，观感偏差大",
            "32.2 新增的「SDR 合成到 HDR」滤镜提供了受控的色彩空间转换，但参数需要与画布色彩设置匹配"
        ],
        "steps": [
            {"title": "确认整体色彩链路", "detail": "设置 → 高级 → 视频里 HDR 相关选项（色彩空间 / HDR 启用）先配置正确，滤镜只是链条最后一环。", "level": "基础"},
            {"title": "给 SDR 来源加滤镜", "detail": "右键 SDR 来源 → 滤镜 → 添加「SDR 转 HDR」效果滤镜，替代默认映射。", "level": "基础"},
            {"title": "按观感微调亮度 / 对比", "detail": "滤镜内提供的映射参数按实际观感调整；先小幅度，避免高光溢出。", "level": "进阶"},
            {"title": "非 HDR 场景不要乱加", "detail": "普通 SDR 直播 / 录制用不到这个滤镜；加了反而引入多余的转换损耗。", "level": "进阶"}
        ],
        "tips": [
            "HDR 全链路（采集 → 合成 → 编码 → 平台）都要支持才有意义，单点改造意义不大",
            "HDR 相关偏色问题先看「HDR 游戏/视频捕获发灰」条目"
        ],
        "related": ["bs-hdr", "cf-colorspace", "cf-colorrange"],
        "links": [REL32, KB]
    },
    {
        "id": "sf-webrtc-simulcast", "category": "streamfail",
        "title": "WebRTC 联播（Simulcast）：一路推流多种画质（32.1 新增）",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "小众",
        "symptoms": [
            "想让网络差的观众自动看到低画质、网络好的看高清（自适应）",
            "听说 32.1 支持 WebRTC Simulcast 但不知道怎么用、平台端要不要开通",
            "开了联播后上行吃紧"
        ],
        "causes": [
            "Simulcast 是同一内容同时编码多档分辨率 / 码率，由平台按观众网络分发",
            "它依赖平台侧 WebRTC ingest 支持，不是传统 RTMP 推流的属性"
        ],
        "steps": [
            {"title": "确认平台支持", "detail": "只有接入了 WebRTC ingest 且声明支持联播的服务才能用（如部分 YouTube / 新兴平台场景）；RTMP 推流与此无关。", "level": "基础"},
            {"title": "更新 OBS 到 32.1+", "detail": "设置 → 一般 → 检查更新；WebRTC 联播为 32.1 新增能力。", "level": "基础"},
            {"title": "按平台文档开启联播输出", "detail": "在服务的输出设置里启用 simulcast / 多档位选项，档位数与各档码率按平台建议配置。", "level": "进阶"},
            {"title": "评估上行预算", "detail": "每一档都是一份独立编码流量：N 档 × 各档码率之和才是实际上行需求，用工具箱的带宽计算器核算。", "level": "进阶"}
        ],
        "tips": [
            "多数国内平台直播仍是 RTMP 单档推流，此功能暂时与主流场景无关",
            "RTMP 多平台分发需求请看「多平台同时推流」条目，两者不是一回事"
        ],
        "related": ["st-multi", "st-multirtmp", "lag-upload", "lag-dynamic-bitrate"],
        "links": [REL32, KB]
    },
    {
        "id": "au-rtx-audio", "category": "audio",
        "title": "NVIDIA RTX 音频滤镜：AI 降噪 / 语音分离的配置与性能代价",
        "platforms": ["Windows"],
        "severity": "常用技巧",
        "symptoms": [
            "麦克风滤镜列表里出现「NVIDIA Audio Effects」，不知道和自带 RNNoise 有何区别",
            "开了 RTX 降噪后 GPU 占用上涨、游戏掉帧",
            "人声被过度抑制，尾音吞字"
        ],
        "causes": [
            "RTX Audio Effects（含 Voice Activity Detection 语音活动检测，32.0 增强）用显卡跑 AI 模型，效果强于 RNNoise 但有算力开销",
            "强度参数过高会把语音一并当成噪声压制"
        ],
        "steps": [
            {"title": "前置条件", "detail": "NVIDIA RTX 显卡 + 安装了 Broadcast / 驱动附带的 Audio Effects 运行组件；否则滤镜不可用或报错。", "level": "基础"},
            {"title": "与 RNNoise 二选一即可", "detail": "两者叠加不会更好，反而双重处理损伤音质；普通环境 RNNoise 已够用。", "level": "基础"},
            {"title": "游戏直播注意 GPU 预算", "detail": "AI 降噪与编码器抢 GPU：编码过载 / 游戏掉帧时优先关 RTX 效果或降低强度，再考虑降编码预设。", "level": "进阶"},
            {"title": "用 VAD 减少误处理", "detail": "开启语音活动检测后，静默期不做处理，既省算力又减少对底噪的过度修饰。", "level": "进阶"}
        ],
        "tips": [
            "完整的麦克风处理顺序建议：降噪 → 门限 → 压缩 → 限制，RTX 降噪只是第一环的 AI 替代品",
            "AMD / Intel 用户没有对应滤镜属正常，用 RNNoise 即可"
        ],
        "related": ["au-mic-noise", "au-mic-chain", "enc-vram", "enc-overload"],
        "links": [REL32, KB]
    },
    {
        "id": "cfg-undo-redo", "category": "config",
        "title": "误操作救场：撤销 / 重做（32.1 新增）",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常用技巧",
        "symptoms": [
            "手滑删了来源 / 缩放滤镜调乱 / 混合模式改错，想一键还原",
            "以前只能记住改动项手工改回去，或干脆恢复整份备份",
            "不知道新版已经支持 Ctrl+Z"
        ],
        "causes": [
            "OBS 32.1 为创作过程中的误操作加入了撤销 / 重做，覆盖缩放过滤、混合模式、去隔行配置、场顺序等设置"
        ],
        "steps": [
            {"title": "第一时间 Ctrl+Z", "detail": "发现改错立即撤销；连续多次可一直退到操作前的状态。Ctrl+Y / Ctrl+Shift+Z 重做。", "level": "基础"},
            {"title": "注意覆盖范围", "detail": "撤销主要覆盖来源属性类操作（滤镜 / 变换 / 混合模式等）；删除整个场景集合这类结构性操作不在保证范围。", "level": "基础"},
            {"title": "重要改动前手动备份", "detail": "场景集合 → 右键导出，或用本助手「OBS 配置管理 → 备份」；撤销栈不是备份的替代品。", "level": "进阶"}
        ],
        "tips": [
            "升级到 32.1+ 才有此功能；老版本用户依赖备份习惯",
            "直播中改场景前先在工作室模式预览，配合撤销双保险"
        ],
        "related": ["cf-reset", "cf-profiles", "cfg-backup-before-upgrade"],
        "links": [REL32]
    },
    {
        "id": "cfg-backup-before-upgrade", "category": "config",
        "title": "升级 / 试 Beta 前备份场景集合与配置的正确姿势",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常用技巧",
        "symptoms": [
            "升完大版本场景全丢 / 设置被重置",
            "试 Beta / RC 后回退稳定版，场景集合打不开或来源丢失",
            "只知道手动拷文件夹，不清楚最小备份集是什么"
        ],
        "causes": [
            "Beta / RC 官方明确要求备份 scene collection 与 profile，测试版写入的配置可能不被旧版识别",
            "配置目录里真正关键的是 basic/scenes/*.json 与 basic/profiles/*/"
        ],
        "steps": [
            {"title": "应用内导出（最小集）", "detail": "场景集合菜单 → 导出：单个 JSON 就能救回全部场景结构；Profile 同理在配置文件处导出。", "level": "基础"},
            {"title": "整目录打包（完整集）", "detail": "关闭 OBS 后打包 %AppData%\\obs-studio 目录；或直接用本助手「OBS 配置管理 → 备份」一键 zip。", "level": "基础"},
            {"title": "升级后先验证再开播", "detail": "导入 / 打开后逐个场景点一遍，检查来源、滤镜、热键都在，再进行正式直播 / 录制。", "level": "基础"},
            {"title": "回退失败的处理", "detail": "新版配置损坏导致旧版打不开：删除损坏的场景集合 JSON，导入升级前的备份；这也是备份要放在升级前的原因。", "level": "进阶"}
        ],
        "tips": [
            "备份命名带上 OBS 版本号（如 scenes_32.2.json），回退时一眼找到对应版本",
            "本助手诊断页 / 配置管理页均可直达备份入口"
        ],
        "related": ["cf-reset", "cf-profiles", "cr-downgrade", "cfg-undo-redo"],
        "links": [KB, REL32]
    },
    # ============================== C 组 ==============================
    {
        "id": "setup-dual-pc", "category": "setup",
        "title": "双机位 / 双 PC 推流搭建（游戏机 + 串流机）",
        "platforms": ["Windows"],
        "severity": "小众",
        "symptoms": [
            "一台电脑又要跑游戏又要编码推流，两头都卡",
            "有两台机器想分工：一台打游戏、一台专职编码推流",
            "采集卡接好了却没有画面 / 只有黑屏"
        ],
        "causes": [
            "单机方案里 GPU 同时承担 渲染游戏 + OBS 合成 + 编码，负载互相挤兑",
            "双 PC 方案依赖采集卡把游戏机画面送进串流机，链路上任一环节（接口 / 带宽 / HDCP）都可能出问题"
        ],
        "steps": [
            {"title": "拓扑与硬件", "detail": "游戏 PC 显卡 HDMI 输出 → 采集卡输入 → 采集卡 USB3.0/HDMI 输出到串流机；采集卡建议支持 1080p60 及以上与环出（HDMI passthrough）。", "level": "基础"},
            {"title": "游戏 PC 只管渲染", "detail": "游戏机上不再运行 OBS；必要时用本地录制作为备份而非推流。", "level": "基础"},
            {"title": "串流机加「视频捕获设备」来源", "detail": "来源选采集卡对应的设备，分辨率 / 帧率设为设备原生值；黑屏先查 HDCP 与线材，再参考「采集卡无信号」条目。", "level": "基础"},
            {"title": "音频回路", "detail": "游戏声音随采集卡 HDMI 进来（选「输出桌面音频」）；或用 3.5mm 线回传独立音轨，混音更灵活。", "level": "进阶"}
        ],
        "tips": [
            "预算有限时，单机 + 硬件编码（NVENC 独立编码单元）已能满足大多数场景，先优化单机再考虑双机",
            "采集卡黑屏 / 无信号是双机方案最高频故障，接线顺序：先开机箱机再开串流机"
        ],
        "related": ["bs-capturecard", "bs-display", "lag-gpu-cap", "enc-vram"],
        "links": [WIN26, KB]
    },
    {
        "id": "rc-device-disconnect", "category": "config",
        "title": "录制中途摄像头 / 采集卡掉线重连（USB 电源管理）",
        "platforms": ["Windows"],
        "severity": "偶发",
        "symptoms": [
            "录着录着摄像头画面冻结，来源上出现感叹号",
            "采集卡偶尔消失又出现，日志里反复枚举设备",
            "USB 设备拔插后 OBS 里要手动重新选择一次"
        ],
        "causes": [
            "Windows 默认允许集线器「选择性暂停」省电，长时间低负载的 USB 摄像头会被休眠",
            "USB 口供电不足（尤其外置硬盘 + 摄像头同 hub）或线材过长",
            "设备固件在长时间传输下的稳定性问题"
        ],
        "steps": [
            {"title": "关闭 USB 选择性暂停", "detail": "控制面板 → 电源选项 → 更改高级电源设置 → USB 设置 → USB 选择性暂停设置 → 已禁用。", "level": "基础"},
            {"title": "设备管理器里逐个关节能", "detail": "设备管理器找到相机 / 通用串行总线控制器下的根集线器与设备 → 属性 → 电源管理 → 取消「允许计算机关闭此设备以节约电源」。", "level": "基础"},
            {"title": "直插主板后置接口", "detail": "避免前面板接口与 Hub；大功率采集卡建议接独立供电或有源 Hub。", "level": "基础"},
            {"title": "换线材 / 更新固件", "detail": "劣质长线是信号完整性杀手；有条件的换品牌短线，摄像头 / 采集卡固件有更新的一并升级。", "level": "进阶"}
        ],
        "tips": [
            "长录制前做 30 分钟压力试录，中途掉线问题基本都能提前暴露",
            "掉线后不必重启 OBS：右键来源 → 属性重新选一次设备即可恢复"
        ],
        "related": ["bs-capturecard", "cf-webcam", "rc-schedule", "os-win-update"],
        "links": [FORUM, WIN26]
    },
    {
        "id": "rc-fps-specs", "category": "recording",
        "title": "录制帧率 / 分辨率与发布平台规格匹配",
        "platforms": ["Windows", "macOS", "Linux"],
        "severity": "常用技巧",
        "symptoms": [
            "本地看着很清晰的录像传到平台后糊成一片",
            "60fps 素材上传后被平台压得拖影 / 卡顿感",
            "不知道该导出多大码率才不会被二次压缩毁掉"
        ],
        "causes": [
            "各平台上传后会按自家规格转码，源文件码率低于平台转码档位时画质不可逆地下降",
            "帧率不一致（如 59.94 vs 60、30 vs 25）会在平台转码时产生抽帧抖动",
            "竖屏 / 横屏比例不符会被强制裁剪或加黑边"
        ],
        "steps": [
            {"title": "按平台推荐档位录制", "detail": "B站 / 抖音等 1080p 内容建议源码率不低于 10000~16000kbps（60fps 取上限）；平台「投稿规范」页面有当日标准。", "level": "基础"},
            {"title": "帧率用整数标准值", "detail": "录 30 或 60fps，避免 59.94 这类广播帧率；剪辑工程帧率与录制一致。", "level": "基础"},
            {"title": "分辨率与画幅匹配", "detail": "横屏 1920x1080、竖屏 1080x1920；混合画幅的项目按主发布平台决定画布。", "level": "基础"},
            {"title": "导出环节别降码", "detail": "MKV → MP4 转封装保留原画质；确需剪辑再导出时，导出码率应 ≥ 源码率。", "level": "进阶"}
        ],
        "tips": [
            "「源文件略超平台上限」永远好于「刚好压线」：二次压缩只会向下",
            "工具箱的参数处方卡内置了常见场景的推荐组合，可直接抄"
        ],
        "related": ["rc-remux", "rc-hybrid-mp4", "rc-4k", "st-vertical"],
        "links": [KB]
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
data["version"] = "1.9"

# 移除误写的空键
for p in data["problems"]:
    p.pop("label", None)

with open(PATH, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
    f.write("\n")

print(f"OK: v{data['version']}, {len(data['problems'])} problems (+{len(new_problems)})")
