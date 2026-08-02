# -*- coding: utf-8 -*-
"""扩展 problems.json：在现有数据基础上追加常见与罕见 OBS 问题、macOS 端问题、
直播间搭建引导（各平台接入与通用流程）。结果写回：
  - OBS_Helper.Client/wwwroot/data/problems.json  (随包发布的站点数据)
  - content/problems.json                          (主数据源，保持一致)
"""
import json, os

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(BASE, "OBS_Helper.Client", "wwwroot", "data", "problems.json")
MIRROR = os.path.join(BASE, "content", "problems.json")

with open(SRC, encoding="utf-8") as f:
    data = json.load(f)

existing_ids = {p["id"] for p in data["problems"]}

NEW = [
    # ---------------- 黑屏 / 画面捕获失败 ----------------
    {
        "id": "bs-protected", "category": "black-screen",
        "title": "受保护内容（DRM/网飞等）捕获黑屏",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["捕获网飞、Disney+、浏览器付费视频时黑屏", "画面出现“受保护内容无法显示”提示"],
        "causes": ["系统级版权保护（HDCP/DRM）禁止捕获受保护视频", "浏览器开启了对受保护内容的限制"],
        "steps": [
            {"title": "确认内容是否受保护", "detail": "网飞、Prime Video、Disney+ 等流媒体默认禁止被捕获，这是系统与版权限制，并非 OBS 故障。", "level": "基础"},
            {"title": "改用本地/自有素材演示", "detail": "如需录教程，请用本地视频文件或浏览器中不受保护的页面，避免捕获受 DRM 保护的内容。", "level": "基础"},
            {"title": "关闭浏览器的“为了保护你……”限制(仅限自有内容)", "detail": "在 Edge/Chrome 设置中关闭“在屏幕上阻止受保护内容”仅对自有/授权内容有效，流媒体依然无法捕获。", "level": "进阶"}
        ],
        "tips": ["受保护内容无法被任何录屏软件合法捕获，这是设计行为"],
        "related": ["bs-display", "bs-window"]
    },
    {
        "id": "bs-hdr", "category": "black-screen",
        "title": "HDR 游戏/视频捕获后发灰、过暗或偏色",
        "platforms": ["Windows"], "severity": "罕见",
        "symptoms": ["捕获 HDR 游戏画面后整体发灰、过暗", "录制文件在普通播放器里颜色异常", "预览与成片不一致"],
        "causes": ["OBS 未开启 HDR 模式，源是 HDR 但 OBS 按 SDR 处理", "色彩格式/空间/范围设置不匹配", "预览被色调映射但录制未应用正确参数"],
        "steps": [
            {"title": "在 OBS 中启用 HDR", "detail": "文件 → 设置 → 高级 → 视频：色彩格式选 P010（10-bit），色彩空间选 Rec.2100(PQ)，色彩范围选 Limited。", "level": "进阶"},
            {"title": "采集卡/源设置为相同格式", "detail": "视频捕获设备属性里同样选 P010 / Rec.2100(PQ)，与 OBS 保持一致。", "level": "进阶"},
            {"title": "使用支持 HDR 的编码器", "detail": "录制 HDR 需用 HEVC（Main10）或 AV1；H.264 不兼容 HDR。推流 HDR 目前主要 YouTube 支持。", "level": "进阶"},
            {"title": "调整 SDR 预览亮度", "detail": "高级设置里的 SDR 白电平（默认 300 nits）控制预览明暗，不影响录制文件本身。", "level": "进阶"}
        ],
        "tips": ["普通显示器看不到 HDR 效果时，预览会偏暗属正常", "观看 HDR 成片需在支持 HDR 的显示器与播放器上"],
        "related": ["cf-colorspace", "cf-colorrange", "bs-display"]
    },
    {
        "id": "bs-mac-perm", "category": "black-screen",
        "title": "macOS 显示器/窗口捕获黑屏（未授权屏幕录制）",
        "platforms": ["macOS"], "severity": "常见",
        "symptoms": ["macOS 下添加显示器捕获/窗口捕获后画面全黑", "预览窗口空白但源已添加"],
        "causes": ["macOS 隐私设置未授予 OBS“屏幕录制”权限", "授予权限后未完全退出并重开 OBS", "macOS 更新后权限被重置"],
        "steps": [
            {"title": "授予屏幕录制权限", "detail": "系统设置 → 隐私与安全性 → 屏幕录制，将 OBS 开关打开。", "level": "基础"},
            {"title": "完全退出并重开 OBS", "detail": "按 Cmd+Q 彻底退出 OBS，再重新打开（macOS 要求重启生效，这是系统限制而非 OBS 问题）。", "level": "基础"},
            {"title": "摄像头/麦克风权限同样处理", "detail": "若摄像头黑屏或麦克风无声，在相同路径下分别开启“摄像头”“麦克风”，然后重启 OBS。", "level": "基础"},
            {"title": "更新后权限被重置需重新授予", "detail": "每次 macOS 大版本更新后建议重新检查并授予上述权限。", "level": "进阶"}
        ],
        "tips": ["macOS 没有独立“游戏捕获”源，全屏游戏用显示器捕获、窗口游戏用窗口捕获", "使用 ScreenCaptureKit（macOS 13+）体验最佳"],
        "related": ["bs-display", "bs-window", "bs-mac-rosetta"]
    },
    {
        "id": "bs-mac-rosetta", "category": "black-screen",
        "title": "macOS 通过 Rosetta 运行导致性能差 / 黑屏",
        "platforms": ["macOS"], "severity": "罕见",
        "symptoms": ["Apple 芯片 Mac 上 OBS 卡顿、CPU 占用异常高", "无法使用 Apple 硬件编码器", "捕获偶发黑屏"],
        "causes": ["下载/安装了仅 Intel(x86_64) 的 OBS 或插件", "OBS 经 Rosetta 2 转译运行"],
        "steps": [
            {"title": "确认是否为原生运行", "detail": "活动监视器中查看 OBS 的“种类”列：应为“Apple”，若为“Intel”说明在 Rosetta 下运行。", "level": "基础"},
            {"title": "重装通用版/Apple 芯片版 OBS", "detail": "前往官网下载 Universal 或 arm64 版本，拖入“应用程序”，确保原生运行。", "level": "基础"},
            {"title": "移除/更新仅 Intel 的插件", "detail": "启动崩溃或异常时，用 帮助 → 以安全模式启动 OBS 跳过插件，再更新或删除不兼容插件。", "level": "进阶"}
        ],
        "tips": ["Apple 芯片原生运行可让 1080p60 推流 CPU 仅约 8–15%"],
        "related": ["bs-mac-perm", "cr-mac-crash"]
    },
    {
        "id": "bs-capturecard", "category": "black-screen",
        "title": "采集卡无信号 / 黑屏",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["视频捕获设备源无画面、黑屏或“无信号”", "预览偶尔闪一下后变黑"],
        "causes": ["HDMI 线/接口接触不良或线材不支持所需规格", "采集卡分辨率/色彩范围与信号源不匹配", "采集卡被其他软件独占", "驱动未安装或需要固件更新"],
        "steps": [
            {"title": "检查物理连接与线材", "detail": "更换 HDMI 线、确认两端插紧；4K/高刷需 HDMI 2.0/2.1 线材。", "level": "基础"},
            {"title": "核对分辨率与色彩范围", "detail": "在源属性里把分辨率/FPS、YUV 色彩空间与范围设为与信号源一致（常选 709 + Limited）。", "level": "进阶"},
            {"title": "关闭其他占用采集卡的程序", "detail": "如厂商自带工具、相机软件、另一个 OBS 实例占用了设备，先关闭再重试。", "level": "进阶"},
            {"title": "更新采集卡驱动/固件", "detail": "到厂商官网更新驱动与固件；部分卡在打开 4K Capture Utility 时会重置色彩范围设置。", "level": "进阶"}
        ],
        "tips": ["色彩范围不匹配会造成画面过暗或发白，优先统一为 Limited/Partial"],
        "related": ["cf-colorrange", "cf-colorspace", "bs-display"]
    },
    {
        "id": "bs-dsr", "category": "black-screen",
        "title": "高 DPI / 系统缩放下捕获窗口异常",
        "platforms": ["Windows"], "severity": "罕见",
        "symptoms": ["窗口捕获位置偏移、只捕获到部分画面", "高分屏下源尺寸错乱", "缩放后预览模糊"],
        "causes": ["Windows 显示缩放（125%/150%）与 OBS DPI 感知冲突", "OBS 未以正确 DPI 模式运行"],
        "steps": [
            {"title": "统一 OBS 与系统的 DPI 设置", "detail": "右键 OBS 快捷方式 → 属性 → 兼容性 → 更改高 DPI 设置，勾选“替代高 DPI 缩放行为”并选“系统”或“应用程序”。", "level": "进阶"},
            {"title": "重启 OBS 生效", "detail": "修改 DPI 设置后完全退出 OBS 再重新打开。", "level": "基础"},
            {"title": "优先使用游戏/显示器捕获", "detail": "对全屏内容尽量用显示器或游戏捕获，减少窗口捕获在高 DPI 下的误差。", "level": "进阶"}
        ],
        "tips": ["应用清单里已启用 PerMonitorV2，通常无需手动改，异常时再覆盖"],
        "related": ["bs-window", "bs-display"]
    },
    # ---------------- 编码过载 / 性能 ----------------
    {
        "id": "enc-nvenc", "category": "encoding",
        "title": "NVENC / 硬件编码器不可用或报错",
        "platforms": ["Windows"], "severity": "一般",
        "symptoms": ["推流/录制时报“无法创建编码器”或“NVENC 错误”", "编码器下拉为空"],
        "causes": ["显卡驱动过旧或损坏", "OBS 与当前 NVIDIA 驱动不兼容", "编码器被其他程序占用", "笔记本双显卡下 OBS 跑在核显"],
        "steps": [
            {"title": "更新/干净安装显卡驱动", "detail": "到 NVIDIA 官网下载最新驱动，安装时选“自定义 → 执行干净安装”。", "level": "基础"},
            {"title": "确认 OBS 使用独显", "detail": "笔记本在 Windows 图形设置里把 obs64.exe 指定为“高性能”GPU。", "level": "进阶"},
            {"title": "关闭占用编码器的程序", "detail": "如其他录屏、直播、剪辑软件，释放 NVENC 后重试。", "level": "进阶"},
            {"title": "临时改用 x264 软件编码", "detail": "在 设置 → 输出 把编码器改为 x264 作为兜底，虽更吃 CPU 但验证是否编码器问题。", "level": "进阶"}
        ],
        "tips": ["游戏与 OBS 必须同走一块 GPU，否则硬件编码无法访问"],
        "related": ["enc-overload", "bs-dualgpu"]
    },
    {
        "id": "enc-av1", "category": "encoding",
        "title": "AV1 编码不支持 / 掉帧",
        "platforms": ["Windows"], "severity": "罕见",
        "symptoms": ["选 AV1 编码器后报错或无法开始推流", "AV1 推流观众端黑屏/不支持", "编码负载异常"],
        "causes": ["显卡不支持 AV1 硬编码（较老 GPU）", "多数直播平台暂不支持 AV1 拉流", "AV1 单帧延迟高导致实时性下降"],
        "steps": [
            {"title": "确认 GPU 是否支持 AV1", "detail": "RTX 40 系及以上、较新 AMD/Intel 才支持 AV1 硬编码；否则改用 HEVC/H.264。", "level": "基础"},
            {"title": "直播优先用平台支持的编码", "detail": "Twitch/B站/抖音等主流平台接受 H.264/HEVC，直接用 NVENC H.264/HEVC 更稳妥。", "level": "基础"},
            {"title": "仅本地录制可尝试 AV1", "detail": "AV1 更适合本地高质量录制，注意播放端需支持 AV1 解码。", "level": "进阶"}
        ],
        "tips": ["盲目上 AV1 可能导致观众端无法播放"],
        "related": ["enc-nvenc", "enc-overload"]
    },
    {
        "id": "enc-vram", "category": "encoding",
        "title": "编码器占用显存导致游戏掉帧",
        "platforms": ["Windows"], "severity": "一般",
        "symptoms": ["开播后游戏帧率明显下降", "编码与游戏争夺 GPU 资源"],
        "causes": ["硬件编码与游戏共用同一 GPU 显存/算力", "高分辨率高帧率推流占用过多带宽", "同时运行多个 GPU 负载程序"],
        "steps": [
            {"title": "降低推流分辨率/帧率", "detail": "将输出降到 936p 或 720p、30fps，可显著减轻 GPU 压力。", "level": "基础"},
            {"title": "适当降低游戏画质", "detail": "略微下调游戏内画质/帧率上限，为编码留出余量。", "level": "基础"},
            {"title": "用双编码器/独立编码卡", "detail": "有条件可用独立采集/编码设备（如 Elgato、专用编码卡）分担 GPU。", "level": "进阶"}
        ],
        "tips": ["查看 OBS 统计面板（视图 → 统计）判断是渲染还是编码滞后"],
        "related": ["enc-overload", "lag-skip"]
    },
    {
        "id": "enc-10bit", "category": "encoding",
        "title": "10-bit / 4:2:0 编码失败或画质异常",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["启用 10-bit 色彩后编码报错", "录制文件颜色断层或无法播放", "HDR 录制失败"],
        "causes": ["所选编码器/配置文件不支持 10-bit", "色彩格式与编码器不匹配", "播放器不支持 10-bit/HEVC Main10"],
        "steps": [
            {"title": "确认编码器支持 10-bit", "detail": "NVIDIA HEVC 需将 Profile 设为 Main10；AMD/Intel 选对应 10-bit 配置。", "level": "进阶"},
            {"title": "HDR 用 HEVC/AV1 而非 H.264", "detail": "H.264 不支持 HDR；录制 HDR 必须 HEVC(Main10) 或 AV1。", "level": "进阶"},
            {"title": "用支持的设备播放", "detail": "10-bit/HEVC 文件需用支持 HEVC 的播放器，并确认显示设备支持。", "level": "基础"}
        ],
        "tips": ["普通直播用 8-bit NV12 即可，10-bit 主要面向高质量录制"],
        "related": ["bs-hdr", "enc-nvenc"]
    },
    # ---------------- 直播卡顿 / 网络掉帧 ----------------
    {
        "id": "lag-wifi", "category": "lag",
        "title": "WiFi 推流不稳定 / 频繁掉帧",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["使用 WiFi 推流时周期性掉帧、卡顿", "信号波动导致间歇性断流"],
        "causes": ["WiFi 2.4G 干扰多、带宽不稳", "与路由器距离远/隔墙", "同频段设备抢占带宽"],
        "steps": [
            {"title": "改用有线网络", "detail": "尽可能用网线直连路由器，稳定性远高于 WiFi。", "level": "基础"},
            {"title": "靠近路由器或换 5G/6G WiFi", "detail": "若只能用无线，连接 5GHz 并尽量靠近路由器，避开 2.4G 拥堵频段。", "level": "基础"},
            {"title": "降低码率留出余量", "detail": "无线环境下把码率降到实际上行速度的 50–60% 以抗波动。", "level": "进阶"}
        ],
        "tips": ["推流对上行稳定性要求极高，WiFi 只作为临时方案"],
        "related": ["lag-network", "lag-buffer"]
    },
    {
        "id": "lag-upload", "category": "lag",
        "title": "上行带宽不足导致整体卡顿",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["码率稍高就大量掉帧", "测速上行远低于推流码率", "观众端持续缓冲"],
        "causes": ["家庭宽带上行本身较小", "其他设备占用上行（云同步、下载回传）", "运营商限速"],
        "steps": [
            {"title": "实测上行速度", "detail": "用测速工具测上行（非下行），推流码率不应超过上行速度的 70%。", "level": "基础"},
            {"title": "下调推流码率/分辨率", "detail": "上行不足时降到 720p30 或更低码率，优先保证流畅。", "level": "基础"},
            {"title": "暂停占用上行的后台任务", "detail": "关闭云盘同步、大型下载等会回传数据的程序。", "level": "进阶"}
        ],
        "tips": ["很多套餐“百兆”指的是下行，上行可能仅 20–50Mbps"],
        "related": ["lag-network", "enc-drop"]
    },
    {
        "id": "lag-browser", "category": "lag",
        "title": "浏览器源占用过高导致卡顿",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["添加浏览器源（提醒、聊天、置顶动画）后整体变卡", "渲染跳帧上升"],
        "causes": ["浏览器源运行复杂网页/动画占用 CPU", "多个浏览器源叠加", "浏览器硬件加速未开启或异常"],
        "steps": [
            {"title": "减少同时运行的浏览器源", "detail": "合并或精简浏览器源，非必要不常驻。", "level": "基础"},
            {"title": "开启浏览器源硬件加速", "detail": "设置 → 高级 勾选“启用浏览器源硬件加速”，重启 OBS。", "level": "进阶"},
            {"title": "限制刷新/分辨率", "detail": "对纯文本提醒类源降低分辨率或刷新频率。", "level": "进阶"}
        ],
        "tips": ["浏览器源本质是内嵌 Chromium，开销不容小觑"],
        "related": ["lag-skip", "enc-cpu"]
    },
    # ---------------- 音画不同步 ----------------
    {
        "id": "av-offset", "category": "avsync",
        "title": "麦克风/设备同步偏移（Sync Offset）",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["说话与口型对不上，声音提前或滞后", "使用虚拟摄像头/直播软件中转后更明显"],
        "causes": ["采集链路各阶段引入不同延迟", "虚拟摄像头/中转软件（如直播伴侣）额外加延迟", "设备缓冲设置差异"],
        "steps": [
            {"title": "用“拍手法”测量延迟", "detail": "录一段自己拍手视频，在剪辑软件里对比“拍手声”与“手接触画面”的时间差，即为需要补偿的毫秒数。", "level": "进阶"},
            {"title": "在高级音频属性设置同步偏移", "detail": "混音器麦克风源右侧齿轮 → 高级音频属性 → 同步偏移(ms)：声音偏早填正数（如 150），偏晚填负数。", "level": "进阶"},
            {"title": "关闭麦克风硬件缓冲", "detail": "右键麦克风源 → 属性 → 取消“使用硬件缓冲”，让 OBS 接管时序。", "level": "进阶"}
        ],
        "tips": ["直接用串流密钥推流比经虚拟摄像头中转延迟更小"],
        "related": ["av-desync", "av-virtualcam"]
    },
    {
        "id": "av-virtualcam", "category": "avsync",
        "title": "虚拟摄像头/直播伴侣中转导致音画延迟",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["经 OBS 虚拟摄像头接入直播伴侣后声音与画面对不上", "视频比音频慢"],
        "causes": ["OBS 处理 → 虚拟摄像头驱动 → 直播软件 三段各自加延迟", "麦克风被直播软件直接采集，绕过了 OBS 的同步补偿"],
        "steps": [
            {"title": "优先用平台串流密钥直推", "detail": "能拿到推流地址/密钥的平台尽量用 OBS 直接推，避免虚拟摄像头中转。", "level": "基础"},
            {"title": "用虚拟音频线统一音频路径", "detail": "安装 VB-Audio 虚拟音频线：OBS 监控设备设为 CABLE Input，麦克风设为“监听并输出”，直播软件选 CABLE Output，使补偿后音频一并传入。", "level": "进阶"},
            {"title": "在直播软件侧做整体延迟补偿", "detail": "若必须用虚拟摄像头，按“拍手法”在直播软件里统一设置音视频延迟。", "level": "进阶"}
        ],
        "tips": ["OBS 虚拟摄像头只传视频，不传音频，这是设计使然"],
        "related": ["av-offset", "av-desync", "au-monitor"]
    },
    {
        "id": "av-drift", "category": "avsync",
        "title": "音画不同步随时间漂移（采样率不匹配）",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["开播初期同步，几分钟后逐渐错位且越来越严重", "周期性“越来越偏”"],
        "causes": ["Windows 声音、OBS、直播软件采样率不一致（44.1k vs 48k）", "设备时钟漂移累积"],
        "steps": [
            {"title": "统一所有采样率为 48kHz", "detail": "Windows 声音设置、OBS（设置 → 音频 → 采样率）、直播软件全部设为 48kHz，切勿混用 44.1k。", "level": "进阶"},
            {"title": "重新设置同步偏移基准", "detail": "统一采样率后重新用拍手法测量并填入同步偏移。", "level": "进阶"}
        ],
        "tips": ["采样率不一致是导致“逐渐漂移”的典型根因"],
        "related": ["av-sample", "av-offset"]
    },
    # ---------------- 音频问题 ----------------
    {
        "id": "au-mac-desktop", "category": "audio",
        "title": "macOS 无法采集系统桌面音频",
        "platforms": ["macOS"], "severity": "常见",
        "symptoms": ["macOS 下直播没有游戏/系统声音", "桌面音频轨电平不动", "想采集系统声音却只能录到麦克风"],
        "causes": ["macOS 不提供系统级音频回环（与 Windows 不同）", "未安装虚拟音频驱动", "OBS 30+ 的应用音频捕获未配置"],
        "steps": [
            {"title": "方案A：使用 BlackHole（免费）", "detail": "安装 BlackHole → 音频 MIDI 设置里建“多输出设备”（勾选扬声器 + BlackHole）→ 系统输出设为该设备 → OBS 添加“音频输入捕获”选 BlackHole。", "level": "进阶"},
            {"title": "方案B：OBS 30+ 原生应用音频捕获", "detail": "添加“macOS 音频捕获”源，选择要采集的具体 App（如 Chrome、游戏），无需虚拟驱动（仅支持使用 CoreAudio 的 App）。", "level": "基础"},
            {"title": "可同时听与采集", "detail": "多输出设备让扬声器与 BlackHole 并行，你既能听到也能被 OBS 采集。", "level": "进阶"}
        ],
        "tips": ["Loopback（付费）、Soundflower（已弃用）也是可选方案"],
        "related": ["au-mute", "au-mic"]
    },
    {
        "id": "au-mic-quiet", "category": "audio",
        "title": "麦克风声音太小 / 增益不足",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["观众反映听不清说话", "混音器麦克风电平很低", "需很大声才有一点信号"],
        "causes": ["麦克风增益过低", "系统/接口输入音量偏小", "麦克风离嘴过远"],
        "steps": [
            {"title": "提高系统麦克风音量", "detail": "Windows 声音设置 → 录制 → 麦克风 → 属性 → 级别，调高；macOS 系统设置 → 声音 → 输入，调高输入音量。", "level": "基础"},
            {"title": "在 OBS 中加增益滤镜", "detail": "混音器麦克风齿轮 → 滤镜 → + → 增益，适度提升（建议先调系统，再少量用增益）。", "level": "基础"},
            {"title": "调整麦克风位置与距离", "detail": "麦克风距嘴约一拳，避免喷麦；考虑防喷罩。", "level": "基础"}
        ],
        "tips": ["增益过大会引入底噪，宁可提高系统音量也少堆增益"],
        "related": ["au-mic", "au-mic-noise"]
    },
    {
        "id": "au-mic-noise", "category": "audio",
        "title": "麦克风底噪 / 环境噪音（降噪处理）",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["直播有风扇声、空调嗡嗡声", "安静时背景噪音明显", "录音带嘶嘶声"],
        "causes": ["环境噪音本底高", "麦克风增益过高放大噪声", "未做噪音抑制"],
        "steps": [
            {"title": "加噪音抑制滤镜", "detail": "混音器麦克风齿轮 → 滤镜 → + → 噪音抑制，选 RNNoise（效果好、开销低）。", "level": "基础"},
            {"title": "用噪音门限滤除静音段", "detail": "再加“噪音门限”滤镜，设置开/关阈值，仅在说话时通过声音。", "level": "进阶"},
            {"title": "改善物理环境", "detail": "用动圈麦、拉近麦克风、加装吸音材料、关闭空调/风扇。", "level": "基础"}
        ],
        "tips": ["若使用 NVIDIA Broadcast，可在那里降噪，OBS 内就无需重复加 RNNoise"],
        "related": ["au-mic-quiet", "au-mic"]
    },
    {
        "id": "au-monitor", "category": "audio",
        "title": "如何监听自己的声音 / 监听导致回声",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["想实时听到自己处理后的声音却听不到", "开启监听后出现回声/啸叫"],
        "causes": ["未设置监听设备或监听模式", "用音箱监听造成麦克风回授", "同一音源被重复监听"],
        "steps": [
            {"title": "设置监听设备", "detail": "设置 → 音频 → 高级 → 监听设备 选你的耳机（不要用音箱，避免回授）。", "level": "基础"},
            {"title": "在高级音频属性设监听模式", "detail": "混音器麦克风齿轮 → 高级音频属性 → 音频监听：仅监听 / 监听并输出。想自己听选“监听并输出”。", "level": "基础"},
            {"title": "避免回声", "detail": "用耳机而非音箱；同一音源只设一次监听；检查会议软件是否也在监听你的麦克风。", "level": "进阶"}
        ],
        "tips": ["虚拟摄像头只传视频不传音频，监听与推流是两条独立路径"],
        "related": ["av-virtualcam", "au-echo"]
    },
    {
        "id": "au-sample-mismatch", "category": "audio",
        "title": "OBS 与系统采样率不一致导致爆音/杂音",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["音频出现爆音、噼啪声", "偶发杂音或断续", "切换场景后音频异常"],
        "causes": ["OBS 采样率与 Windows 声音默认格式不一致", "多个音频设备采样率不同"],
        "steps": [
            {"title": "统一 OBS 采样率为 48kHz", "detail": "设置 → 音频 → 采样率 选 48kHz。", "level": "基础"},
            {"title": "统一系统默认格式为 48kHz", "detail": "Windows 声音 → 播放/录制 设备属性 → 高级 → 默认格式 选 48000 Hz；所有设备保持一致。", "level": "进阶"}
        ],
        "tips": ["采样率不一致还会引发音画漂移"],
        "related": ["av-sample", "av-drift"]
    },
    {
        "id": "au-vst", "category": "audio",
        "title": "音频滤镜 / VST 插件处理人声",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["想加 EQ、压缩、混响提升音质", "VST 插件加载失败或导致卡顿", "人声单薄或忽大忽小"],
        "causes": ["未使用压缩/限制器导致音量起伏", "VST 插件与系统架构不兼容", "滤镜顺序不当"],
        "steps": [
            {"title": "添加常用人声滤镜", "detail": "麦克风滤镜链建议：噪音抑制 → 增益 → 压缩器 → EQ → 限制器。", "level": "进阶"},
            {"title": "用压缩器稳定音量", "detail": "添加“压缩器”缩小动态范围，避免忽大忽小。", "level": "进阶"},
            {"title": "VST 插件架构匹配", "detail": "macOS 上确认 VST 为当前芯片架构（Apple/Intel）；加载异常时移除该插件。", "level": "进阶"}
        ],
        "tips": ["滤镜顺序会影响最终效果，一般降噪在前、润色在后"],
        "related": ["au-mic-noise", "au-mic-quiet"]
    },
    # ---------------- 推流失败 / 连接超时 ----------------
    {
        "id": "sf-rtmps", "category": "streamfail",
        "title": "RTMPS / 协议不兼容导致连接失败",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["填入推流地址后报协议错误", "部分平台要求 RTMPS 而本地填了 RTMP", "连接立即断开"],
        "causes": ["平台要求加密的 RTMPS 但填了 RTMP", "OBS 版本过旧不支持 RTMPS", "地址中 rtmp:// 与 rtmps:// 混淆"],
        "steps": [
            {"title": "按平台要求选择协议", "detail": "多数现代平台用 RTMPS；在 OBS 设置 → 推流 选对应服务，让其自动生成 RTMPS 地址。", "level": "基础"},
            {"title": "手动修正地址前缀", "detail": "若手动填服务器，确认是 rtmps://（加密）还是 rtmp://，与平台要求一致。", "level": "进阶"},
            {"title": "更新 OBS 到最新版", "detail": "旧版可能不支持 RTMPS，升级后再试。", "level": "基础"}
        ],
        "tips": ["直接选 OBS 内置的“服务”列表可避免手动填错协议"],
        "related": ["sf-auth", "sf-server"]
    },
    {
        "id": "sf-auth", "category": "streamfail",
        "title": "串流密钥错误 / 鉴权失败",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["连接被服务器拒绝、鉴权失败", "提示“流密钥无效”", "能连上但立即被踢"],
        "causes": ["串流密钥复制不完整或含空格", "用错账号/频道的密钥", "密钥已过期（部分平台每次变化）", "服务器 URL 与密钥不匹配"],
        "steps": [
            {"title": "重新复制完整密钥", "detail": "从平台直播设置页完整复制服务器地址与串流密钥，避免首尾空格。", "level": "基础"},
            {"title": "确认密钥未过期", "detail": "抖音/小红书等部分平台密钥每次开播变化，需重新获取后填入。", "level": "基础"},
            {"title": "用“服务”下拉自动填充", "detail": "OBS 设置 → 推流 → 服务 选对应平台并登录/粘贴，减少手动错误。", "level": "基础"}
        ],
        "tips": ["不要把串流密钥泄露给他人，它等同于你频道的推流权限"],
        "related": ["sf-server", "st-bilibili", "st-douyin"]
    },
    {
        "id": "sf-server", "category": "streamfail",
        "title": "服务器地址填错 / 区域选择不当",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["连接超时或极慢", "能连上但延迟高、易断流", "提示服务器不可达"],
        "causes": ["手工填写的服务器地址拼写错误", "选了离自己很远的接入点", "自定义 RTMP 地址格式不对"],
        "steps": [
            {"title": "用平台内置服务自动选节点", "detail": "OBS 推流“服务”里选对应平台，让其自动选择最优接入点。", "level": "基础"},
            {"title": "手动填时核对格式", "detail": "自定义 RTMP 一般形如 rtmp://<区域>.contribute.live-video.net/app/<key>，确认无多余字符。", "level": "进阶"},
            {"title": "就近选择区域", "detail": "跨大区推流会显著增加延迟与丢包，优先选离所在地最近的区域。", "level": "进阶"}
        ],
        "tips": ["能直推就别绕第三方，减少一跳就少一分不稳定"],
        "related": ["sf-auth", "sf-firewall"]
    },
    {
        "id": "sf-firewall", "category": "streamfail",
        "title": "防火墙 / 公司网络阻断推流",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["公司/校园网无法连接推流服务器", "能上网但 OBS 连不上", "连接被重置"],
        "causes": ["防火墙/安全策略封锁 1935(RTMP) 等端口", "企业代理拦截非标准流量", "VPN 规则冲突"],
        "steps": [
            {"title": "更换网络环境测试", "detail": "用手机热点或其他网络测试，确认是否为当前网络策略问题。", "level": "基础"},
            {"title": "允许 OBS 通过防火墙", "detail": "Windows 防火墙将 obs64.exe 设为允许出站；公司网需联系 IT 放行推流端口。", "level": "进阶"},
            {"title": "改用支持的端口/协议", "detail": "部分平台提供 443 端口的 RTMPS 入口，企业网更易放行。", "level": "进阶"}
        ],
        "tips": ["家庭网络一般无需改防火墙，公司网常是元凶"],
        "related": ["sf-vpn", "sf-server"]
    },
    # ---------------- 直播间搭建 / 多平台推流 ----------------
    {
        "id": "st-youtube", "category": "setup",
        "title": "YouTube 直播接入",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["不知道如何在 YouTube 开播", "OBS 与 YouTube 对接失败", "想做 4K/HDR 直播"],
        "causes": ["未开启 YouTube 直播权限", "推流地址/密钥获取方式不对", "编码/分辨率不符合频道等级"],
        "steps": [
            {"title": "获取推流信息", "detail": "YouTube 工作室 → 直播 → 右侧“编码器”页，复制“串流网址”与“串流密钥”粘贴到 OBS 设置 → 推流。", "level": "基础"},
            {"title": "设置编码与分辨率", "detail": "输出选 CBR，关键帧 2s；1080p60 建议 6000–12000 Kbps；HDR 直播用 HEVC 且平台支持。", "level": "进阶"},
            {"title": "用 OBS 内置 YouTube 服务", "detail": "推流“服务”直接选 YouTube，登录账号可自动获取，减少手动错误。", "level": "基础"}
        ],
        "tips": ["新频道需先通过直播权限审核；YouTube 是少数支持 HDR 直播的平台"],
        "related": ["st-bilibili", "st-twitch", "sf-auth"]
    },
    {
        "id": "st-videoaccount", "category": "setup",
        "title": "视频号直播接入",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["想在微信视频号开播并用 OBS", "找不到推流地址/密钥", "视频号助手对接失败"],
        "causes": ["需通过视频号直播助手获取推流信息", "未满足开播资质/认证", "OBS 设置与助手不一致"],
        "steps": [
            {"title": "在视频号直播助手获取推流信息", "detail": "打开“视频号直播助手”→ 添加直播 → 选择 OBS 推流 → 复制推流地址与密钥到 OBS。", "level": "基础"},
            {"title": "配置 OBS 输出", "detail": "推流服务选“自定义”，粘贴地址与密钥；分辨率建议 1080p、码率 4000–6000。", "level": "基础"},
            {"title": "开播并核对画面", "detail": "在助手端点击开播，OBS 开始推流，确认预览与助手端一致。", "level": "基础"}
        ],
        "tips": ["视频号对内容与资质有要求，需先完成相应开通"],
        "related": ["st-bilibili", "st-douyin", "sf-auth"]
    },
    {
        "id": "st-xhs", "category": "setup",
        "title": "小红书直播接入",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["想在小红书用 OBS 专业开播", "找不到推流入口", "第三方工具对接失败"],
        "causes": ["需借助小红书直播伴侣/创作者中心获取推流", "账号需满足开播条件", "密钥每次变化"],
        "steps": [
            {"title": "通过创作者中心/直播伴侣获取推流", "detail": "在小红书直播伴侣或创作者后台选择“OBS 推流”，复制服务器与串流密钥（通常每次开播变化）。", "level": "基础"},
            {"title": "粘贴到 OBS 并开播", "detail": "OBS 推流服务选“自定义”，粘贴后开始推流，再在伴侣端确认开播。", "level": "基础"},
            {"title": "开播前重新取密钥", "detail": "小红书等密钥常每次变化，务必开播当次重新复制。", "level": "基础"}
        ],
        "tips": ["各平台开播资质不同，先确认账号已开通直播权限"],
        "related": ["st-douyin", "st-videoaccount", "sf-auth"]
    },
    {
        "id": "st-general", "category": "setup",
        "title": "从零搭建直播间（通用流程）",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["第一次开播不知从何下手", "场景/来源/音频一团乱", "推流总出问题"],
        "causes": ["缺少标准化的搭建流程", "未按“准备→场景→来源→音频→推流→开播”顺序", "参数凭感觉设置"],
        "steps": [
            {"title": "① 准备与权限", "detail": "更新 OBS；Windows 建议以管理员运行、统一 GPU；macOS 授予屏幕录制/麦克风/摄像头权限（见“关于 macOS 端”）。", "level": "基础"},
            {"title": "② 建立场景与来源", "detail": "新建场景，依次添加：显示器/窗口/游戏捕获（画面）、视频捕获设备（摄像头）、音频输入（麦克风）、音频输出（桌面声）。", "level": "基础"},
            {"title": "③ 音频校对", "detail": "混音器确认麦克风与桌面声音电平正常、未静音；统一采样率 48kHz；按需加降噪/压缩。", "level": "基础"},
            {"title": "④ 推流设置", "detail": "设置 → 推流 选平台并粘贴密钥；输出用硬件编码、CBR、关键帧 2s；码率不超过上行 70%。", "level": "基础"},
            {"title": "⑤ 开播前自检", "detail": "视图 → 统计 观察渲染/编码/网络掉帧；先“开始录制”自测画面声音，再“开始推流”。", "level": "进阶"}
        ],
        "tips": ["善用“搭建”页的分阶段引导与各平台接入指南"],
        "related": ["st-bilibili", "st-twitch", "st-scene"]
    },
    {
        "id": "st-scene", "category": "setup",
        "title": "场景与来源布局 / 叠加层",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["画面元素重叠混乱", "想做摄像头圆角/边框/置顶动画", "来源顺序不对导致遮挡"],
        "causes": ["来源层级顺序（上下）决定前后遮挡", "未用变换/裁剪定位", "缺少边框、遮罩等叠加层"],
        "steps": [
            {"title": "理解来源顺序", "detail": "来源列表靠上的显示在前面；用拖拽调整层级，背景放最底、摄像头/文字放上层。", "level": "基础"},
            {"title": "用变换与裁剪排版", "detail": "右键来源 → 变换/裁剪（按住 Alt 拖动手柄可局部裁剪），配合对齐参考线。", "level": "基础"},
            {"title": "添加叠加层与滤镜", "detail": "用图片/浏览器源做边框、角标；摄像头加“圆角/遮罩”等滤镜美化。", "level": "进阶"},
            {"title": "锁定完成的元素", "detail": "摆放满意后点来源前的锁形图标，避免误拖。", "level": "基础"}
        ],
        "tips": ["工作室模式可分别编辑“预览”与“直播”两版场景，确认后再过渡"],
        "related": ["st-general", "cf-hotkeys"]
    },
    {
        "id": "st-vertical", "category": "setup",
        "title": "竖屏 9:16 直播（抖音 / TikTok 等）",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["手机端观众看到黑边或画面被压", "横屏内容直接推到竖屏平台不对劲", "想同时做竖屏+横屏"],
        "causes": ["画布仍是 16:9 却推到竖屏平台", "未做 1080×1920 竖版画布", "横竖两套输出未分开"],
        "steps": [
            {"title": "建立竖屏画布", "detail": "设置 → 视频：基础(画布)分辨率 1920×1920？应为 1080×1920；输出(缩放)与画布一致，避免二次缩放。", "level": "基础"},
            {"title": "用插件做双画布（可选）", "detail": "Aitum Vertical / MultiStream 等插件可在同一 OBS 内维护竖屏 9:16 画布并独立输出到 TikTok/短视频平台。", "level": "进阶"},
            {"title": "竖屏构图", "detail": "上 1/3 摄像头、下 2/3 内容；用“变换 → 水平居中”快速对齐。", "level": "基础"},
            {"title": "码率与稳定优先", "detail": "竖屏先 720×1280@30fps、码率 2500–4000；确认上行余量后再上 1080×1920@60。", "level": "进阶"}
        ],
        "tips": ["手机端才是真相：用手机看自己直播确认清晰度与比例"],
        "related": ["st-douyin", "st-general", "av-virtualcam"]
    },
    {
        "id": "st-mac", "category": "setup",
        "title": "macOS 推流搭建注意事项",
        "platforms": ["macOS"], "severity": "一般",
        "symptoms": ["macOS 上不知道如何接入直播", "没有“游戏捕获”源", "桌面音频采不到"],
        "causes": ["macOS 与 Windows 在捕获源、编码器、音频上有差异", "未授予必要权限", "不了解 Apple 硬件编码器"],
        "steps": [
            {"title": "授予权限", "detail": "系统设置 → 隐私与安全性：开启屏幕录制、麦克风、摄像头、辅助功能（见“关于 macOS 端”）。", "level": "基础"},
            {"title": "选用 macOS 捕获源", "detail": "屏幕用“macOS 屏幕捕获”（ScreenCaptureKit，选显示器/窗口/App）；游戏用窗口或显示器捕获（无独立游戏源）。", "level": "基础"},
            {"title": "使用 Apple 硬件编码器", "detail": "设置 → 输出 编码器选“Apple VT H.264/HEVC 硬件编码器”，Apple 芯片上几乎零 CPU 占用。", "level": "基础"},
            {"title": "采集系统音频", "detail": "用 BlackHole 或 OBS 30+ 的“macOS 音频捕获”获取系统声音。", "level": "进阶"}
        ],
        "tips": ["Apple 芯片无 AV1 硬编码（截至 2026），用 H.264/HEVC 即可"],
        "related": ["bs-mac-perm", "au-mac-desktop", "st-general"]
    },
    {
        "id": "st-chat", "category": "setup",
        "title": "聊天窗口 / 浏览器停靠（Browser Docks）",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["想在主界面看平台聊天", "不知道如何嵌入网页/提醒", "多平台聊天难以兼顾"],
        "causes": ["未使用 OBS 自定义浏览器停靠", "聊天链接未从平台获取"],
        "steps": [
            {"title": "添加自定义浏览器停靠", "detail": "视图 → 停靠 → 自定义浏览器停靠，粘贴平台聊天网页地址（如 TikFinity/Casterlabs 提供的 dock 链接），停靠到侧边。", "level": "进阶"},
            {"title": "嵌入提醒/控件网页", "detail": "把 Streamlabs/直播姬等提醒页、点歌页的 URL 作为浏览器源或停靠嵌入。", "level": "进阶"}
        ],
        "tips": ["多平台可用 Casterlabs 等聚合聊天，再以一个停靠嵌入 OBS"],
        "related": ["st-multi", "st-general"]
    },
    {
        "id": "st-multirtmp", "category": "setup",
        "title": "多平台同时推流（自定义 RTMP 多输出）",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["想一次开播到多个平台", "不知道如何加第二个推流目标", "多平台延迟/音画问题"],
        "causes": ["OBS 原生只推一个目标，需插件或中转", "未正确配置各平台密钥", "上行不足以支撑多路"],
        "steps": [
            {"title": "用多推流插件", "detail": "安装 Aitum Multi / MultiStream 等插件，在面板里逐个添加平台（粘贴各自服务器+密钥），可分别设横/竖画布。", "level": "进阶"},
            {"title": "或用中转服务 Restream", "detail": "把各平台聚合到一个 Restream 推流地址，OBS 只推一路（见既有“多平台同时推流”条目）。", "level": "基础"},
            {"title": "评估上行带宽", "detail": "每多一路推流都增加上行占用，确保总码率不超过上行 70%。", "level": "进阶"}
        ],
        "tips": ["竖屏多平台可让竖画画布单独输出到 TikTok/短视频平台"],
        "related": ["st-multi", "st-vertical", "lag-upload"]
    },
    # ---------------- 录制问题 ----------------
    {
        "id": "rc-mkv", "category": "recording",
        "title": "录制用 MKV 防崩溃丢文件 / 重新封装",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["录制中崩溃/断电后 MP4 打不开", "担心录制文件损坏", "想要既安全又通用的格式"],
        "causes": ["MP4 在录制未完成时索引缺失无法播放", "意外退出导致文件损坏"],
        "steps": [
            {"title": "录制格式选 MKV", "detail": "设置 → 输出 → 录制 格式选 MKV，录制中断也能保留已写入数据。", "level": "基础"},
            {"title": "录完“重新封装”为 MP4", "detail": "文件 → 重新封装录制（Remux），把 MKV 转成 MP4，兼容剪辑软件。", "level": "基础"},
            {"title": "仍想直接出 MP4", "detail": "稳定性要求不高时可选 MP4，但务必正常停止录制以写入索引。", "level": "进阶"}
        ],
        "tips": ["“开始录制”与“开始推流”是两套独立输出，互不影响"],
        "related": ["rc-nofile", "rc-remux"]
    },
    {
        "id": "rc-remux", "category": "recording",
        "title": "MKV 转 MP4（重新封装）操作",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["剪辑软件不认 MKV", "想把录好的 MKV 转成 MP4", "封装失败"],
        "causes": ["部分剪辑工具只接受 MP4/MOV", "手动转码耗时且损画质"],
        "steps": [
            {"title": "用 OBS 自带重新封装", "detail": "文件 → 重新封装录制（Remux Recordings），选 MKV 源文件，输出 MP4，几乎秒转且不重编码。", "level": "基础"},
            {"title": "批量处理", "detail": "在重新封装窗口可一次添加多个文件，逐个生成 MP4。", "level": "基础"}
        ],
        "tips": ["重新封装只改容器不重编码，画质无损、速度极快"],
        "related": ["rc-mkv", "rc-nofile"]
    },
    {
        "id": "rc-audio-missing", "category": "recording",
        "title": "录制文件没有声音",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["录出来的视频画面正常但没声音", "回放只有画面", "某条音轨缺失"],
        "causes": ["录制轨道未包含对应音频", "桌面/麦克风设备未正确指定", "音频被静音或未进入混音"],
        "steps": [
            {"title": "检查音频设备指定", "detail": "设置 → 音频：全局音频设备里手动指定桌面音频与麦克风设备（别只留“默认”）。", "level": "基础"},
            {"title": "确认混音器有电平", "detail": "录制时看混音器对应轨电平是否跳动，不动说明没采到声。", "level": "基础"},
            {"title": "检查录制音轨设置", "detail": "设置 → 输出 → 录制 → 音轨，勾选要录制的轨（如 1=麦克风 2=桌面）。", "level": "进阶"}
        ],
        "tips": ["macOS 需用 BlackHole/应用音频捕获才能录到系统声音"],
        "related": ["au-mute", "au-mac-desktop", "rc-nofile"]
    },
    {
        "id": "rc-4k", "category": "recording",
        "title": "4K / 高码率录制卡顿或文件过大",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["4K 录制掉帧、磁盘写不过来", "文件体积巨大难以存储", "回放卡顿"],
        "causes": ["磁盘写入速度不足（机械盘/USB 慢盘）", "码率/分辨率过高", "编码格式选择不当"],
        "steps": [
            {"title": "录制到高速磁盘", "detail": "用 NVMe/SSD 录制 4K；避免录制到机械硬盘或慢速 U 盘。", "level": "基础"},
            {"title": "选高效编码与合理码率", "detail": "用 HEVC/AV1 降低体积；按需设置码率，4K60 可能需要 40–80 Mbps 以上。", "level": "进阶"},
            {"title": "降低录制分辨率/帧率", "detail": "非必要不用 4K，1080p60 已能满足大多数场景，文件更小更稳。", "level": "基础"}
        ],
        "tips": ["录制与推流可不同分辨率：推流 1080p、本地录制更高清"],
        "related": ["rc-local", "enc-10bit"]
    },
    # ---------------- 基础配置 / 画面变形 ----------------
    {
        "id": "cf-colorspace", "category": "config",
        "title": "色彩空间不匹配导致偏色",
        "platforms": ["Windows", "macOS"], "severity": "一般",
        "symptoms": ["画面整体偏色、颜色怪异", "录制/直播与源画面颜色不一致", "暗部或亮部异常"],
        "causes": ["OBS 内部色彩空间与源不一致（709 vs 2020）", "采集卡/相机色彩空间设置不同", "HDR/SDR 混用"],
        "steps": [
            {"title": "统一色彩空间为 Rec.709（SDR）", "detail": "设置 → 高级 → 视频：色彩空间选 Rec.709，色彩范围选 Limited（Partial），适合绝大多数直播。", "level": "基础"},
            {"title": "源设备保持一致", "detail": "视频捕获设备属性里的色彩空间也与 OBS 一致，避免二次转换。", "level": "进阶"},
            {"title": "HDR 内容单独处理", "detail": "若做 HDR，整体切到 Rec.2100(PQ) + P010，且播放端支持（见“HDR 捕获”条目）。", "level": "进阶"}
        ],
        "tips": ["流媒体几乎都按 Rec.709/Limited 处理，除非确定在做 HDR"],
        "related": ["cf-colorrange", "bs-hdr"]
    },
    {
        "id": "cf-colorrange", "category": "config",
        "title": "色彩范围（受限 vs 完全）导致过暗/过曝",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["画面发灰、发白（过曝感）", "画面过暗、对比过强", "颜色“洗掉”了"],
        "causes": ["OBS 与信号源色彩范围不一致", "源是 Full 却设成 Limited（或反之）", "采集卡默认值不匹配"],
        "steps": [
            {"title": "OBS 保持 Limited（Partial）", "detail": "设置 → 高级 → 视频：色彩范围 一般保持 Limited；除非明确录制 Full 且后续软件支持。", "level": "基础"},
            {"title": "让源与 OBS 匹配", "detail": "视频捕获设备属性里色彩范围选与源实际一致的档（大多数游戏机/PC 默认可设为 Limited；若源是 Full 则两边都为 Full）。", "level": "进阶"},
            {"title": "用测试图验证", "detail": "放一张灰阶/彩条测试图，对比是否发灰或过暗来判断范围是否匹配。", "level": "进阶"}
        ],
        "tips": ["范围不一致是最常见的“画面发灰/过暗”元凶"],
        "related": ["cf-colorspace", "bs-capturecard"]
    },
    {
        "id": "cf-canvas", "category": "config",
        "title": "画布与输出分辨率设置",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["画面模糊或带黑边", "输出分辨率不是想要的尺寸", "性能与画质难以平衡"],
        "causes": ["基础(画布)与输出(缩放)不一致", "输出分辨率高于必要导致浪费性能", "未勾选“按显示缩放”或拉伸"],
        "steps": [
            {"title": "设定画布分辨率", "detail": "设置 → 视频：基础(画布)分辨率设为内容原生分辨率（如 1920×1080）。", "level": "基础"},
            {"title": "设定输出分辨率", "detail": "输出(缩放)分辨率设为实际推流分辨率（如 1920×1080 或 1280×720），与画布一致避免二次缩放。", "level": "基础"},
            {"title": "Downscale 滤镜选 Lanczos", "detail": "缩放下采样滤镜选“Lanczos（36 抽头）”画质更好（开销略高）。", "level": "进阶"}
        ],
        "tips": ["竖屏直播把画布设为 1080×1920"],
        "related": ["cf-resolution", "st-vertical"]
    },
    {
        "id": "cf-hotkeys", "category": "config",
        "title": "快捷键 / 工作室模式",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["想一键切换场景/开始推流", "直播中切换生硬", "不知工作室模式用途"],
        "causes": ["未配置全局/场景快捷键", "直接切换场景无过渡", "未启用工作室模式预编辑"],
        "steps": [
            {"title": "设置常用快捷键", "detail": "设置 → 快捷键：为“开始推流/录制”“切换场景”“静音麦克风”等分配快捷键。", "level": "基础"},
            {"title": "用工作室模式平滑切换", "detail": "视图 → 工作室模式，左侧编辑预览、右侧是直播画面，确认后用“转场”平滑切换。", "level": "进阶"},
            {"title": "为场景单独设热键", "detail": "每个场景可在快捷键里指定“切换到该场景”的专用键。", "level": "基础"}
        ],
        "tips": ["直播中误触可用热键保护，避免鼠标点错"],
        "related": ["cf-profiles", "st-scene"]
    },
    {
        "id": "cf-profiles", "category": "config",
        "title": "配置文件 / 场景集合管理",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["多套直播配置互相干扰", "想备份/迁移设置", "不同内容用不同参数"],
        "causes": ["所有设置混在一个配置里", "未用配置文件/场景集合隔离", "重装前未备份"],
        "steps": [
            {"title": "用场景集合隔离内容", "detail": "配置 → 场景集合 → 新建/切换，为不同直播内容（游戏/聊天/竖屏）分别建集合。", "level": "基础"},
            {"title": "用配置文件隔离参数", "detail": "配置 → 配置文件 可保存不同的输出/编码/推流参数组合。", "level": "基础"},
            {"title": "备份与迁移", "detail": "设置/场景集合存于用户目录（Windows：%AppData%\obs-studio；macOS：~/Library/Application Support/obs-studio），可复制备份或迁移。", "level": "进阶"}
        ],
        "tips": ["重装系统或换机前务必备份场景集合"],
        "related": ["cf-hotkeys", "rc-local"]
    },
    {
        "id": "cf-multiview", "category": "config",
        "title": "多视图 / 全屏投影监看",
        "platforms": ["Windows", "macOS"], "severity": "罕见",
        "symptoms": ["想一屏看多个来源", "需要把某一来源投到第二屏", "现场监看不方便"],
        "causes": ["未使用多视图或全屏投影", "多显示器布局未配置"],
        "steps": [
            {"title": "启用多视图", "detail": "视图 → 多视图，将多个场景/来源以网格形式显示在一个窗口，便于监看。", "level": "进阶"},
            {"title": "全屏投影单个来源", "detail": "右键来源/场景 → 全屏投影（或 Projector），投到第二块屏幕单独监看。", "level": "进阶"}
        ],
        "tips": ["多视图适合多机位/多平台同时监看"],
        "related": ["st-scene", "cf-hotkeys"]
    },
    # ---------------- 崩溃 / 兼容性 ----------------
    {
        "id": "cr-plugin", "category": "crash",
        "title": "插件导致崩溃 / 安全模式排查",
        "platforms": ["Windows", "macOS"], "severity": "常见",
        "symptoms": ["启动即崩溃或卡死", "添加某插件后异常", "特定功能一用就崩"],
        "causes": ["插件与当前 OBS 版本不兼容", "插件架构不匹配（尤其 macOS Intel/Apple）", "插件文件损坏或冲突"],
        "steps": [
            {"title": "安全模式启动", "detail": "帮助 → 以安全模式启动 OBS，仅加载核心，确认是否为插件问题。", "level": "基础"},
            {"title": "更新或移除问题插件", "detail": "到插件官网下载匹配当前 OBS 的版本；macOS 注意选 Apple/Intel 对应架构。", "level": "进阶"},
            {"title": "逐个排查", "detail": "在安全模式正常后，逐个重新启用插件定位元凶。", "level": "进阶"}
        ],
        "tips": ["升级 OBS 大版本后，旧插件常需同步更新"],
        "related": ["cr-mac-crash", "cr-downgrade"]
    },
    {
        "id": "cr-vcredist", "category": "crash",
        "title": "缺少运行库（Visual C++ 可再发行）导致无法启动",
        "platforms": ["Windows"], "severity": "罕见",
        "symptoms": ["OBS 或某插件启动报缺少 msvcp*.dll / vcruntime", "一点开就闪退无提示"],
        "causes": ["系统缺少对应的 Visual C++ 可再发行运行库", "运行库被精简/误删"],
        "steps": [
            {"title": "安装 VC++ 可再发行", "detail": "到微软官网下载并安装最新 Visual C++ Redistributable（x64），重启后再试。", "level": "基础"},
            {"title": "修复/重装运行库", "detail": "在“程序和功能”中修复或重装对应版本的 VC++ 运行库。", "level": "进阶"}
        ],
        "tips": ["多数 Windows 软件崩溃与缺运行库相关，优先排查"],
        "related": ["cr-downgrade", "cr-antivirus"]
    },
    {
        "id": "cr-antivirus", "category": "crash",
        "title": "杀毒/安全软件拦截 OBS",
        "platforms": ["Windows"], "severity": "罕见",
        "symptoms": ["OBS 被莫名关闭或无法写入配置", "捕获行为被拦截", "启动缓慢或报错"],
        "causes": ["杀软将 OBS 或捕获行为误判为风险", "实时防护锁定文件/进程"],
        "steps": [
            {"title": "将 OBS 加入白名单", "detail": "在杀软中添加 obs64.exe 及 OBS 配置目录为信任/排除项。", "level": "基础"},
            {"title": "临时关闭实时防护测试", "detail": "临时禁用实时防护验证是否为其所致，确认后改加白名单而非长期关闭。", "level": "进阶"}
        ],
        "tips": ["切勿长期关闭安全防护，加白名单即可"],
        "related": ["cr-plugin", "cr-vcredist"]
    },
    {
        "id": "cr-mac-crash", "category": "crash",
        "title": "macOS 启动崩溃（插件架构不匹配）",
        "platforms": ["macOS"], "severity": "罕见",
        "symptoms": ["macOS 上 OBS 一开就崩", "更新后突然无法启动", "提示插件加载失败"],
        "causes": ["装了仅 Intel 架构的插件", "Apple 芯片上混用 Rosetta", "插件版本过旧"],
        "steps": [
            {"title": "安全模式启动", "detail": "帮助 → 以安全模式启动 OBS 跳过插件，确认问题来自插件。", "level": "基础"},
            {"title": "更新/移除不匹配插件", "detail": "到插件官网下载 Apple 芯片(arm64)版本；旧插件直接删除。", "level": "进阶"},
            {"title": "确认原生运行", "detail": "活动监视器确认 OBS 为“Apple”架构，避免 Rosetta 转译。", "level": "进阶"}
        ],
        "tips": ["Apple 芯片优先用 Universal/arm64 的 OBS 与插件"],
        "related": ["bs-mac-rosetta", "cr-plugin"]
    },
]

added = 0
for p in NEW:
    if p["id"] in existing_ids:
        print("SKIP (id 已存在):", p["id"])
        continue
    # basic schema check
    for k in ("id", "category", "title", "platforms", "severity", "symptoms", "causes", "steps", "tips", "related"):
        if k not in p:
            raise SystemExit(f"缺少字段 {k} in {p.get('id')}")
    data["problems"].append(p)
    existing_ids.add(p["id"])
    added += 1

data["version"] = "1.1"
data["updated"] = "2026-08-02"
data["note"] = "已扩充常见与罕见 OBS 问题、macOS 端问题及直播间搭建引导（含各平台接入与通用流程）。"

for path in (SRC, MIRROR):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

print(f"已追加 {added} 条，总计 {len(data['problems'])} 条问题。写入：\n  {SRC}\n  {MIRROR}")
