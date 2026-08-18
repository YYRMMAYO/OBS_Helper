# -*- coding: utf-8 -*-
"""为 OBS_Helper 问题库新增 15 条问题（2026-08-19 网络调研：随机音画不同步、麦克风滤镜链、
串流密钥安全、虚拟摄像头、捕获源冲突、关键帧间隔、动态码率、GPU 满载等缺口主题）。
版本 1.4 -> 1.5。"""
import json
import io

PATH = r"F:\OBS\NOBS\OBS_Helper.Wpf\Assets\problems.json"

KB = {"title": "OBS 官方 · 知识库（中文）", "url": "https://obsproject.com/zh-cn/kb"}
ANALYZER = {"title": "OBS 官方 · 日志分析器", "url": "https://obsproject.com/analyzer"}
WIN26 = {"title": "OBS Windows 排障指南（2026 版）", "url": "https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html"}
TSIGHT = {"title": "OBS Studio 深度优化与故障排查指南（2026 版）", "url": "https://tsight.io/articles/10161700"}
PIE = {"title": "How To Stream With OBS（掉帧/音画/断流全解）", "url": "https://www.positioniseverything.net/how-to-stream-on-twitch-with-obs-full-guide"}
VCAM = {"title": "OBS Virtual Camera Not Working 修复指南", "url": "https://appuals.com/obs-virtual-camera-not-working"}
KEY = {"title": "OBS Stream Key 获取与安全指南", "url": "https://obs-versions.com/blog/obs-stream-key-guide"}
DESYNC = {"title": "OBS 论坛 · 直播中随机音画不同步（官方解答）", "url": "https://obsproject.com/forum/threads/random-audio-desyncs-while-streaming.194456"}
ENCOVER = {"title": "How to Fix OBS Encoding Overloaded", "url": "https://obs-versions.com/blog/obs-encoding-overloaded-fix"}

new_problems = [
    {
        "id": "au-random-desync", "category": "audio",
        "title": "直播中随机音画不同步（几小时后突然错位）",
        "platforms": ["Windows"],
        "severity": "偶发",
        "symptoms": ["直播 1 小时以上后观众反馈音画差几秒", "音频时快时慢、反复漂移", "日志提示 Audio buffering hit the maximum value"],
        "causes": ["系统负载过高导致 OBS 音频缓冲被填满", "个别音频设备时间戳（timestamp）错误", "游戏 / 系统音量变化触发缓冲重置", "无线耳机 / 蓝牙设备延迟波动"],
        "steps": [
            {"title": "开启 Windows 游戏模式", "detail": "设置 → 游戏 → 游戏模式 → 打开。Windows 会在游戏模式下给 OBS 更稳定的调度，减少音频缓冲波动。", "level": "基础"},
            {"title": "重启 OBS 重置缓冲", "detail": "出现随机不同步后，先重启 OBS（仅重启流无效）。官方确认设备时间戳错误会导致缓冲持续膨胀，重启可复位。", "level": "基础"},
            {"title": "降低系统负载", "detail": "关闭不必要程序（浏览器标签页、云盘同步），留意 CPU 占用；负载高时音频缓冲易触发上限。", "level": "进阶"},
            {"title": "避免无线音频设备", "detail": "蓝牙耳机 / 无线麦克风延迟不稳定，直播监听与采集优先用有线设备。", "level": "进阶"}
        ],
        "tips": ["长播前先录 30 分钟测试素材检查音画", "日志（帮助 → 日志文件）里出现 audio buffering 提示，基本就是负载或时间戳问题"],
        "related": ["av-drift", "lag-gpu-cap", "lag-gamemode"],
        "links": [DESYNC, TSIGHT]
    },
    {
        "id": "au-mic-chain", "category": "audio",
        "title": "麦克风声音处理链：降噪 → 门限 → 压缩 → 限制",
        "platforms": ["Windows", "macOS"],
        "severity": "进阶",
        "symptoms": ["人声夹杂风扇 / 键盘 / 电流声", "说话声音忽大忽小", "激动时爆音、安静时听不清"],
        "causes": ["只加了一个降噪，没做动态处理", "滤镜顺序不对，降噪放最后把噪声又拉回来", "增益过高削波"],
        "steps": [
            {"title": "按正确顺序添加滤镜", "detail": "右键麦克风 → 滤镜 → 依序添加：噪声抑制（RNNoise）→ 噪声门限 → 压缩器 → 限制器。顺序反了效果会变差。", "level": "基础"},
            {"title": "噪声抑制选 RNNoise", "detail": "噪声抑制选择「RNNoise」算法（比旧版 Speex 效果明显好），强度 10~15 即可，过高会吞字。", "level": "基础"},
            {"title": "压缩器参数参考", "detail": "压缩器：阈值 -18~-24dB、比例 3:1~4:1、启动 5ms、释放 100ms。让小声更清楚、大声不破。", "level": "进阶"},
            {"title": "限制器兜底", "detail": "限制器：阈值 -6dB。突发大笑 / 拍桌子等瞬间音量由它兜住，保证不削波。", "level": "进阶"}
        ],
        "tips": ["滤镜链调好后用 OBS 录音测一段，看混音器波形是否平稳", "监听用「监听并输出」，听实时效果再微调"],
        "related": ["au-mic-noise", "au-mic-clip", "au-vst"],
        "links": [TSIGHT, KB]
    },
    {
        "id": "sf-key-leak", "category": "streamfail",
        "title": "串流密钥泄露 / 被冒充开播 / 密钥安全",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["别人用你的账号开播", "直播间出现不是你推的内容", "截图 / 录屏里露出了设置页密钥"],
        "causes": ["串流密钥等同于频道密码，被截图 / 分享泄露", "把密钥粘贴到公开群聊 / 论坛求助", "多人共用同一份密钥配置"],
        "steps": [
            {"title": "立即重置密钥", "detail": "Twitch：创作者中心 → 设置 → 串流 → 主串流密钥 → 重置；B站 / 抖音 / YouTube 都在直播后台的「推流设置」里重置。旧密钥即刻失效。", "level": "基础"},
            {"title": "优先用「连接账号」而非手动密钥", "detail": "OBS 28+ 支持 设置 → 推流 → 服务选 Twitch / YouTube → 连接账号，授权后无需复制粘贴密钥，天然防泄露。", "level": "基础"},
            {"title": "打码后再截图分享", "detail": "直播设置页截图前先点「隐藏密钥」或打码；求助他人时不要发包含密钥 / 推流地址的完整截图。", "level": "基础"},
            {"title": "密钥换新后同步所有设备", "detail": "重置后要更新所有在用这份密钥的电脑 / 手机 / 硬件编码器，否则旧设备会断流。", "level": "进阶"}
        ],
        "tips": ["密钥是发布凭据，不是账号密码，但泄露后果等同账号被接管", "换主播 / 员工离职后务必重置一次"],
        "related": ["sf-auth", "st-twitch", "st-bilibili"],
        "links": [KEY, KB]
    },
    {
        "id": "sf-connected-offline", "category": "streamfail",
        "title": "OBS 显示已连接，但平台端无画面 / 离线",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["OBS 状态栏显示推流中、码率有波动", "平台后台显示离线或直播未开", "观众点进直播间是黑屏 / 无内容"],
        "causes": ["密钥过期或填错（密钥换了没更新）", "推流服务器 / 区域选错", "推流 URL 少了路径或填成网页地址", "防火墙拦截了部分上行连接"],
        "steps": [
            {"title": "重新复制并粘贴密钥", "detail": "到平台后台复制最新密钥，粘贴进 OBS 设置 → 推流，注意别带前后空格。密钥重置 / 2FA 变更后必须重新复制。", "level": "基础"},
            {"title": "检查服务与服务器", "detail": "服务务必选平台对应的内置选项（Twitch / B站自定义 RTMP），服务器选「自动」或离你最近的节点，不要手填网页地址。", "level": "基础"},
            {"title": "用带宽测试模式验证", "detail": "Twitch 可在服务器 URL 后加 ?bandwidthtest=true，B站等平台可先发「私密直播 / 未发布」测试；确认 OBS 侧编码正常。", "level": "进阶"},
            {"title": "检查防火墙 / 杀毒", "detail": "确保防火墙放行 OBS（obs64.exe）的出站连接；企业网 / 校园网可能封 1935 等 RTMP 端口。", "level": "进阶"}
        ],
        "tips": ["「已连接但无画面」优先查密钥与服务器，别先怀疑电脑性能", "直播中密钥被重置会导致立刻断流且 OBS 不报错"],
        "related": ["sf-auth", "sf-server", "sf-firewall", "sf-bandwidth-test"],
        "links": [KEY, WIN26]
    },
    {
        "id": "sf-bandwidth-test", "category": "streamfail",
        "title": "不打扰观众的推流测试（bandwidthtest / 私密直播）",
        "platforms": ["Windows", "macOS"],
        "severity": "进阶",
        "symptoms": ["想测试推流但不让观众看到", "检查上行带宽是否够 1080p60", "换新设备 / 网络后先验证再正式开播"],
        "causes": ["直接开播会打扰观众", "不知道如何验证推流链路是否正常"],
        "steps": [
            {"title": "Twitch 带宽测试", "detail": "推流服务器选 Auto 后手动改成 Twitch 的测试服务器地址（如 auto-ingest 后加 ?bandwidthtest=true），推流 2~3 分钟看状态栏丢帧率。", "level": "进阶"},
            {"title": "B站 / 抖音私密开播", "detail": "B站开播时选择「密码直播 / 仅自己可见」，抖音用「观众不可见开播」（直播伴侣内），验证后再公开。", "level": "基础"},
            {"title": "YouTube 未公开直播", "detail": "YouTube 创建直播时可见性选「未公开」，用网页播放器确认音画正常后再公开。", "level": "基础"},
            {"title": "观察统计面板", "detail": "推流时看 视图 → 统计：丢帧率 0~1% 为健康；偏高则按 网络 类问题逐项排查。", "level": "基础"}
        ],
        "tips": ["测试建议跑满 5 分钟，覆盖网络波动", "新配的编码参数（码率 / 编码器）一定要先测试再上直播"],
        "related": ["sf-connected-offline", "lag-network", "st-twitch"],
        "links": [KEY, PIE]
    },
    {
        "id": "st-virtualcam", "category": "setup",
        "title": "虚拟摄像头无法使用 / 不出现 / 画面黑屏",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["会议软件里找不到 OBS Virtual Camera", "找到了但画面黑屏 / 冻结", "预览正常但对方看不到画面"],
        "causes": ["先打开了会议软件，虚拟摄像头启动在后，设备列表未刷新", "虚拟摄像头输出目标选错（如输出了空预览）", "Windows 桌面应用摄像头权限被关", "虚拟摄像头插件 / 驱动损坏或与其他虚拟摄像头冲突"],
        "steps": [
            {"title": "先启动虚拟摄像头再开会议软件", "detail": "OBS 中点击「开始虚拟摄像头」后，再打开 Teams / Zoom / 腾讯会议，在摄像头里选「OBS Virtual Camera」。顺序反了设备列表可能不刷新。", "level": "基础"},
            {"title": "输出目标设为「程序」", "detail": "虚拟摄像头设置里把输出目标从「预览」改为「程序」（跟随直播 / 录制的主输出），避免黑屏。", "level": "基础"},
            {"title": "打开 Windows 摄像头权限", "detail": "设置 → 隐私和安全性 → 相机 → 打开「相机访问」与「允许桌面应用访问相机」。", "level": "基础"},
            {"title": "排查冲突与重装", "detail": "关闭 Snap Camera / ManyCam 等其他虚拟摄像头软件；问题依旧就重装 OBS（虚拟摄像头组件随安装包注册）。", "level": "进阶"}
        ],
        "tips": ["Teams / 微信等有时要完全退出（含托盘）再重开才能刷新摄像头列表", "macOS 需在 系统设置 → 隐私与安全 → 相机 中授权 OBS"],
        "related": ["av-virtualcam", "cf-webcam", "bs-browser-src"],
        "links": [VCAM, KB]
    },
    {
        "id": "cf-capture-conflict", "category": "config",
        "title": "同一场景放多个捕获源互相干扰（黑屏 / 闪烁）",
        "platforms": ["Windows"],
        "severity": "常见",
        "symptoms": ["场景里同时有显示器捕获和游戏捕获时画面闪烁", "两个游戏捕获源互相抢占", "捕获画面忽明忽暗、资源占用高"],
        "causes": ["多个捕获源同时 hook 同一 GPU 合成层，互相干扰", "同一场景混用显示器 / 游戏 / 窗口捕获", "每个游戏单独建捕获源导致频繁切换"],
        "steps": [
            {"title": "同一场景只保留一种捕获源", "detail": "显示器捕获、游戏捕获、窗口捕获不要放在同一个场景里；需要多个画面时用不同场景 + 场景源组合。", "level": "基础"},
            {"title": "所有游戏共用一个游戏捕获", "detail": "一个「游戏捕获」源勾选「捕获任意全屏应用程序」，配合热键模式（按快捷键选择当前游戏），避免建多个互相抢占。", "level": "进阶"},
            {"title": "用窗口捕获替代游戏捕获", "detail": "常玩的游戏若捕获黑屏，改成「窗口捕获 + Windows 10/11 (WGC)」，同样只保留一个。", "level": "进阶"},
            {"title": "OBS 与游戏同卡运行", "detail": "笔记本在 Windows 图形设置里把 OBS 和游戏都设为「高性能（独显）」，减少跨 GPU 干扰。", "level": "进阶"}
        ],
        "tips": ["源越少越稳：能用一个捕获源解决的不要放两个", "直播时优先保证「当前场景」干净，预览场景可以复杂"],
        "related": ["bs-game", "bs-display", "bs-dualgpu"],
        "links": [PIE, WIN26]
    },
    {
        "id": "cf-webcam", "category": "config",
        "title": "摄像头画面模糊 / 卡顿 / 帧率低",
        "platforms": ["Windows"],
        "severity": "常见",
        "symptoms": ["摄像头画面发虚、不清晰", "视频捕获设备掉帧、卡顿", "在 OBS 里选不到摄像头或画面冻结"],
        "causes": ["摄像头分辨率 / 帧率设置超过设备能力", "USB 带宽不足（与其他设备抢带宽）", "Windows 相机隐私权限未开", "驱动未更新或与系统不兼容"],
        "steps": [
            {"title": "分辨率帧率匹配设备", "detail": "视频捕获设备属性里选摄像头原生分辨率（如 1080p）、帧率 30fps，不要盲目拉满 60fps 或 4K。", "level": "基础"},
            {"title": "检查 USB 带宽", "detail": "摄像头独占一个 USB 3.0 口（尤其别和采集卡 / 无线接收器共用），有条件换根短线。", "level": "进阶"},
            {"title": "打开相机权限", "detail": "设置 → 隐私和安全性 → 相机 → 打开「允许桌面应用访问相机」。", "level": "基础"},
            {"title": "更新 / 重装驱动", "detail": "去摄像头厂商官网装最新驱动；OBS 里可加「锐化」滤镜（0.3 左右）轻微提升清晰度。", "level": "进阶"}
        ],
        "tips": ["很多「模糊」是聚焦没对好：先用相机自带软件对焦", "拍人时用 1080p30 + 顺光，比 4K 高帧率更实用"],
        "related": ["st-virtualcam", "bs-capturecard", "cf-resolution"],
        "links": [WIN26, KB]
    },
    {
        "id": "cf-reset", "category": "config",
        "title": "配置损坏 / 改乱后恢复默认（先备份再重置）",
        "platforms": ["Windows", "macOS"],
        "severity": "进阶",
        "symptoms": ["OBS 设置怎么改都不生效", "某次异常后界面异常 / 频繁报错", "想干净重来又怕丢场景配置"],
        "causes": ["配置文件损坏或插件写入异常", "反复调试后参数互相矛盾", "想排查问题但不知道从哪下手"],
        "steps": [
            {"title": "先备份配置", "detail": "关闭 OBS 后复制 %AppData%\\obs-studio 整个目录到桌面备份（macOS：~/.config/obs-studio）。场景 / 来源 / 设置都在里面。", "level": "基础"},
            {"title": "用自动配置向导重建基线", "detail": "工具 → 自动配置向导，按网络与用途重跑一遍，生成一套稳妥的码率 / 分辨率基线。", "level": "基础"},
            {"title": "新建配置文件而非删旧", "detail": "设置 → 配置文件 → 新建，保留旧配置便于回退；场景集合同理。", "level": "进阶"},
            {"title": "彻底重置", "detail": "确需清空时，退出 OBS 后把 %AppData%\\obs-studio\\global.ini 重命名（或删除 profiles / scenes 子目录），重启 OBS 会重建默认配置。", "level": "进阶"}
        ],
        "tips": ["「先备份 → 逐步改 → 每次只改一个变量」是排查一切配置问题的通用方法", "重装 OBS 不会删除 %AppData%\\obs-studio，想清干净要手动删"],
        "related": ["cf-profiles", "cf-wizard", "cr-safe-mode"],
        "links": [KB, WIN26]
    },
    {
        "id": "lag-gamemode", "category": "lag",
        "title": "Windows 游戏模式 / 性能模式未开启导致掉帧",
        "platforms": ["Windows"],
        "severity": "偶发",
        "symptoms": ["游戏内流畅但 OBS 渲染跳帧", "推流码率正常却画面发卡", "任务管理器里 OBS 占用低但帧率不稳"],
        "causes": ["Windows 游戏模式关闭，系统调度未给 OBS 保留 GPU 时间片", "电源模式为「节能」导致 CPU/GPU 降频", "后台程序抢资源"],
        "steps": [
            {"title": "开启游戏模式", "detail": "设置 → 游戏 → 游戏模式 → 打开；游戏模式能让 Windows 优先保证前台游戏 + OBS 的资源。", "level": "基础"},
            {"title": "电源模式选「最佳性能」", "detail": "设置 → 系统 → 电源 → 电源模式改为「最佳性能」（笔记本插电直播更稳）。", "level": "基础"},
            {"title": "OBS 进程优先级", "detail": "任务管理器 → 详细信息 → OBS_Helper / obs64.exe → 设置优先级为「高于正常」（重启 OBS 后需重设）。", "level": "进阶"},
            {"title": "关闭后台高占用程序", "detail": "直播时退出云盘同步、浏览器多余标签、视频渲染等吃 CPU/GPU 的程序。", "level": "基础"}
        ],
        "tips": ["双屏直播时，把 OBS 放独立屏幕并用「全屏投影」能进一步降低掉帧", "渲染跳帧优先查 GPU 占用，网络丢帧才查带宽"],
        "related": ["lag-skip", "lag-gpu-cap", "cf-priority"],
        "links": [TSIGHT, PIE]
    },
    {
        "id": "lag-keyint", "category": "lag",
        "title": "关键帧间隔（Keyframe Interval）设置不当导致花屏 / 模糊",
        "platforms": ["Windows"],
        "severity": "进阶",
        "symptoms": ["观众端画面经常马赛克 / 花屏", "切流后很久才恢复清晰", "平台转码质量选项不可用"],
        "causes": ["关键帧间隔保持「自动」导致不满足平台 2 秒要求", "平台转码器需要固定关键帧才能提供多清晰度", "低码率 + 过长关键帧间隔画面模糊"],
        "steps": [
            {"title": "固定为 2 秒", "detail": "设置 → 输出 → 高级输出 → 推流 → 关键帧间隔填 2（秒）。Twitch / YouTube / B站普遍要求 2 秒，平台转码质量选项依赖它。", "level": "基础"},
            {"title": "检查编码器高级参数", "detail": "x264 用 keyint=2 会由设置控制；NVENC 同样在高级输出里填 2。不要同时用命令行参数覆盖。", "level": "进阶"},
            {"title": "别在码率不足时硬顶分辨率", "detail": "1080p60 至少 6000kbps；上传不够就降输出分辨率（1600x900 / 720p），比「高分辨率 + 花屏」观感好得多。", "level": "进阶"}
        ],
        "tips": ["花屏是丢关键帧的信号：先查 keyint，再查网络丢包", "直播平台建议的码率表：1080p60 ≈ 6000kbps，720p60 ≈ 4500kbps"],
        "related": ["lag-network", "lag-dynamic-bitrate", "enc-overload"],
        "links": [PIE, TSIGHT]
    },
    {
        "id": "lag-dynamic-bitrate", "category": "lag",
        "title": "网络波动时开启动态码率（Dynamic Bitrate）",
        "platforms": ["Windows"],
        "severity": "进阶",
        "symptoms": ["网络偶发抖动导致频繁丢帧", "WiFi / 上行不稳时直播画面反复卡", "不想降固定码率又想要稳定"],
        "causes": ["固定码率在网络波动时无法自动调整", "平台对过高码率的突发流量直接丢包", "无线网络本身抖动"],
        "steps": [
            {"title": "开启动态码率", "detail": "设置 → 高级 → 网络 → 勾选「启用动态码率」（Dynamic Bitrate）。网络变差时 OBS 自动降低码率保流畅，恢复后回升。", "level": "基础"},
            {"title": "配合带宽测试定基准", "detail": "先跑带宽测试确认可用上行，把「理想码率」设在可用带宽的 80% 左右，留出波动余量。", "level": "进阶"},
            {"title": "能上有线就上有线", "detail": "动态码率是补救，有线网络 + 稳定路由器才是根治；长期直播强烈建议网线。", "level": "基础"}
        ],
        "tips": ["开了动态码率后，直播中丢帧会明显减少，但码率波动可能让画质轻微浮动，属正常", "同时配合「自动重连」设置（高级 → 网络），断流后自动恢复"],
        "related": ["lag-network", "lag-wifi", "lag-buffer"],
        "links": [PIE, TSIGHT]
    },
    {
        "id": "lag-gpu-cap", "category": "lag",
        "title": "游戏不锁帧导致 GPU 满载，OBS 渲染延迟",
        "platforms": ["Windows"],
        "severity": "常见",
        "symptoms": ["游戏 200+ 帧很流畅，但 OBS 渲染跳帧", "OBS 统计里 Rendering Lag 持续增长", "GPU 占用 100%，OBS 分不到资源"],
        "causes": ["游戏无垂直同步 / 无帧率上限，GPU 被游戏占满", "单卡直播时 OBS 必须和游戏共享 GPU", "渲染延迟和网络丢帧被误认为是同一问题"],
        "steps": [
            {"title": "游戏内锁帧", "detail": "游戏设置里开启垂直同步（VSync）或把帧率上限设为 60 / 120，给 GPU 留出 OBS 合成与编码的时间片。", "level": "基础"},
            {"title": "降低游戏画质项", "detail": "体积雾、阴影、抗锯齿等开销大户适当下调；OBS 需要大约 5~10% 的 GPU 余量。", "level": "进阶"},
            {"title": "关掉 OBS 预览减轻负载", "detail": "预览分辨率调低或右键预览 → 禁用预览，释放一部分 GPU 渲染负担。", "level": "进阶"},
            {"title": "硬编 + 低分辨率组合", "detail": "用 NVENC / AMF 硬件编码并把输出缩到 720p，编码不再抢游戏 GPU 的通用单元。", "level": "进阶"}
        ],
        "tips": ["统计面板里「Rendering Lag 渲染延迟」涨 = GPU 不够，不是网络问题", "双机直播（游戏机 + 采集机）能从根上解决单卡资源争抢"],
        "related": ["lag-skip", "lag-gamemode", "enc-overload", "enc-vram"],
        "links": [PIE, ENCOVER]
    },
    {
        "id": "lag-stats", "category": "lag",
        "title": "看懂统计面板：丢帧 / 渲染延迟 / 编码器过载",
        "platforms": ["Windows", "macOS"],
        "severity": "入门",
        "symptoms": ["不知道直播卡顿是网络还是电脑问题", "统计面板一堆数字看不懂", "被各种「掉帧」说法搞混"],
        "causes": ["不同指标对应不同瓶颈：丢帧 = 网络，渲染/编码延迟 = 性能"],
        "steps": [
            {"title": "打开统计面板", "detail": "视图 → 统计（Stats）。直播 / 录制时实时观察三类指标。", "level": "基础"},
            {"title": "丢帧（Dropped Frames）涨 → 网络问题", "detail": "丢帧持续增长说明上行带宽不足或网络抖动：降低码率、换有线、开启动态码率。", "level": "基础"},
            {"title": "渲染延迟（Rendering Lag）涨 → GPU 问题", "detail": "渲染延迟增长说明 OBS 合成帧太慢：游戏锁帧、降低画质、关预览。", "level": "进阶"},
            {"title": "编码器过载（Encoder Overloaded）→ CPU/编码器问题", "detail": "出现编码过载提示：改用硬件编码（NVENC/AMF）、降低分辨率帧率、换更快的 x264 预设。", "level": "进阶"}
        ],
        "tips": ["健康的读数：丢帧 0~1%、码率贴近设定值、CPU 占用不高", "卡顿时先看统计，再动手改设置，避免瞎调"],
        "related": ["lag-network", "lag-skip", "enc-overload", "lag-gpu-cap"],
        "links": [PIE, TSIGHT]
    },
    {
        "id": "rc-disk-space", "category": "recording",
        "title": "录制文件过大 / 磁盘空间不足",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["录 1 小时视频占几十 GB", "录制中途提示磁盘空间不足", "录像目录在系统盘导致 C 盘爆满"],
        "causes": ["码率 / 质量设置过高（CBR 高码率或 CRF 过低）", "录像路径默认在系统盘", "同分辨率下帧率越高体积越大"],
        "steps": [
            {"title": "录像路径换到大盘", "detail": "设置 → 输出 → 录像 → 录像路径改到剩余空间充足的磁盘（避免 C 盘）。", "level": "基础"},
            {"title": "用 CRF / 质量模式而非 CBR", "detail": "录像建议用「高级输出 → 录像 → 速率控制 = CRF（x264 约 18~20；NVENC 用 CQ 20~22）」，同画质体积比 CBR 小很多。", "level": "进阶"},
            {"title": "按需降分辨率 / 帧率", "detail": "本地素材 1080p30 通常够用，不必每段都 4K60；需要回放剪辑再开高规格。", "level": "进阶"},
            {"title": "及时转码归档", "detail": "录完用 HandBrake 等转成 H.265 归档，体积可再省一半；或定期清理不需要的原始素材。", "level": "提示"}
        ],
        "tips": ["1 小时 1080p60 @ 20000kbps ≈ 9GB；按此估算磁盘需求", "同时直播 + 录像时，录像可单独用 CRF 保证画质，与推流码率互不影响"],
        "related": ["rc-4k", "rc-mkv", "rc-local"],
        "links": [KB, TSIGHT]
    },
]

with io.open(PATH, "r", encoding="utf-8") as f:
    data = json.load(f)

existing = {p["id"] for p in data["problems"]}
added = 0
for p in new_problems:
    if p["id"] in existing:
        print(f"SKIP (已存在): {p['id']}")
        continue
    data["problems"].append(p)
    added += 1

data["version"] = "1.5"
data["updated"] = "2026-08-19"
data["note"] = ("已扩充常见与罕见 OBS 问题、macOS 端问题及直播间搭建引导（含各平台接入与通用流程）。"
                "2026-08-05 增补：反作弊游戏捕获、浏览器源白屏、回放缓冲、绿幕抠像、媒体源、文本中文乱码、场景过渡、麦克风爆音、观众端延迟等。"
                "2026-08-19 增补（1.5）：随机音画不同步、麦克风滤镜链、串流密钥安全、虚拟摄像头排障、捕获源冲突、摄像头画质、配置重置、"
                "游戏模式/GPU 满载、关键帧间隔、动态码率、统计面板解读、录像体积管理等。")

with io.open(PATH, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
    f.write("\n")

print(f"完成：新增 {added} 条问题，共 {len(data['problems'])} 条；版本 -> {data['version']}（{data['updated']}）")
