# -*- coding: utf-8 -*-
"""为 OBS_Helper 问题库新增 8 条排障指引（2026-08-23 调研：插件加载失败、环境组件干扰、
多显示器刷新率、Steam 版插件目录、启动缓慢、Device removed、Windows 通信活动降音量、
码率不足马赛克等缺口主题）。版本 1.6 -> 1.7。"""
import json

PATH = r"F:\OBS\NOBS\OBS_Helper.Wpf\Assets\problems.json"

KB = {"title": "OBS 官方 · 知识库（中文）", "url": "https://obsproject.com/zh-cn/kb"}
ANALYZER = {"title": "OBS 官方 · 日志分析器", "url": "https://obsproject.com/analyzer"}
WIN26 = {"title": "OBS Windows 排障指南（2026 版）", "url": "https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html"}
TSIGHT = {"title": "OBS Studio 深度优化与故障排查指南（2026 版）", "url": "https://tsight.io/articles/10161700"}
OBS322 = {"title": "OBS 官方 · OBS Studio 32.2 发布说明（插件加载变更）", "url": "https://obsproject.com/blog/obs-studio-32-2-release-notes"}
NAHIMIC = {"title": "OBS 论坛 · Nahimic 导致 OBS 崩溃 / 黑屏汇总", "url": "https://obsproject.com/forum/threads/crash-on-startup-nahimic.157270/"}
RTSS = {"title": "OBS 官方论坛 · RivaTuner/RTSS 与游戏内覆盖层冲突", "url": "https://obsproject.com/forum/threads/obs-and-rivatuner-statistics-server-rtss-conflict.128953/"}
DEVICERM = {"title": "OBS 论坛 · 排查 Device Removed / GPU 挂起", "url": "https://obsproject.com/forum/threads/device-removed-gpu-hung.145856/"}
STEAM = {"title": "OBS 官方 · 知识库分类：Windows 排障", "url": "https://obsproject.com/zh-cn/kb/category/2"}

new_problems = [
    {
        "id": "cr-plugin-load", "category": "crash",
        "title": "更新 OBS 后第三方插件加载失败 / 反复弹安全模式",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": [
            "升级 OBS 后启动提示「未验证插件」或直接进安全模式",
            "日志出现 Failed to load module / incompatible 字样",
            "某个插件的面板 / 滤镜在菜单里消失了"
        ],
        "causes": [
            "OBS 32.2 重写了 Windows 插件 DLL 加载逻辑，个别带依赖的插件首次启动加载失败（32.2.1 / 32.2.2 已修复）",
            "插件版本过旧，与新 OBS 的 API 不兼容",
            "macOS 强制迁移 Apple Silicon 后，Intel 版插件不再加载",
            "旧版本插件残留文件与新版本同名冲突"
        ],
        "steps": [
            {"title": "先把 OBS 升到最新补丁版", "detail": "32.2 首发的插件加载问题已在 32.2.2 修复；设置 → 一般 → 检查更新，或官网重装最新版。", "level": "基础"},
            {"title": "重装该插件的当前 Release", "detail": "到插件项目 GitHub Releases 下载对应 OBS 版本的最新安装包重装；大多数情况这一步就能解决。", "level": "基础"},
            {"title": "仍不行：先删干净再装", "detail": "删除 <OBS目录>\\obs-plugins\\64bit\\ 下的旧 DLL 与 data\\obs-plugins\\ 同名目录后重装，避免新旧文件混装。", "level": "进阶"},
            {"title": "macOS 用户换通用二进制", "detail": "Apple Silicon 机器必须使用带 universal / arm64 标注的安装包；Intel 版插件不会再被加载。", "level": "进阶"}
        ],
        "tips": [
            "大版本更新前先记录已装插件清单，出问题好对照",
            "日志里 Failed to load module 后面的 DLL 名就是元凶"
        ],
        "related": ["cr-safe-mode", "cr-plugin", "cr-mac-crash", "cr-downgrade"],
        "links": [OBS322, ANALYZER]
    },
    {
        "id": "cr-env-interference", "category": "crash",
        "title": "Nahimic / RivaTuner(RTSS) / Overwolf 等组件干扰 OBS",
        "platforms": ["Windows"],
        "severity": "偶发",
        "symptoms": [
            "OBS 频繁崩溃但日志看不出自身错误",
            "游戏捕获黑屏 / 只有一层黑框",
            "帧率异常低或录制文件损坏"
        ],
        "causes": [
            "Nahimic 音频服务向进程注入 DLL，是 OBS 崩溃的常客（微星 / 联想等主板预装）",
            "RivaTuner / RTSS 游戏内覆盖层与捕获钩子冲突",
            "Overwolf、Voicemod 等同样会注入或挂钩图形与音频"
        ],
        "steps": [
            {"title": "卸载或停用 Nahimic", "detail": "应用列表卸载 Nahimic / A-Volute；品牌机可在服务中禁用 Nahimic service。这是官方论坛反复确认的头号干扰源。", "level": "基础"},
            {"title": "RTSS 关闭覆盖层或加白名单", "detail": "RTSS 设置里关闭 On-Screen Display 或把 obs64.exe 的检测关掉，只保留后台监控。", "level": "基础"},
            {"title": "排查其他注入型软件", "detail": "逐个退出 Overwolf / Voicemod / 各类游戏加亮工具（如 NVIDIA App 覆盖层）验证；用排除法定位。", "level": "进阶"},
            {"title": "以管理员身份运行 OBS", "detail": "右键快捷方式 → 以管理员身份运行，可减少 UAC 与注入类冲突；同时关闭「启用游戏内覆盖」类功能。", "level": "进阶"}
        ],
        "tips": [
            "崩溃转储（%AppData%\\obs-studio\\crashes）时间点能对上这些软件的服务日志即可实锤",
            "重装系统级音效软件前先试试禁用服务，很多情况就够了"
        ],
        "related": ["cr-antivirus", "cr-safe-mode", "bs-game", "cr-driver"],
        "links": [NAHIMIC, RTSS, WIN26]
    },
    {
        "id": "lag-multi-refresh", "category": "lag",
        "title": "多显示器刷新率不一致导致卡顿 / 微掉帧",
        "platforms": ["Windows"],
        "severity": "常见",
        "symptoms": [
            "双显示器下直播 / 录制画面周期性卡一下",
            "统计面板渲染时间（Render time）间歇飙高",
            "单屏时一切正常，接第二块屏就掉帧"
        ],
        "causes": [
            "两台显示器刷新率不同（如 144Hz + 60Hz），DWM 合成器跨屏同步拖累渲染",
            "浏览器 / 窗口捕获跨到了不同刷新率的屏幕上",
            "核显与独显输出分配不合理，合成负载压在同一颗 GPU 上"
        ],
        "steps": [
            {"title": "统一刷新率测试", "detail": "临时把两块屏都设成同一刷新率（设置 → 系统 → 屏幕 → 高级显示），若卡顿消失即可确认方向。", "level": "基础"},
            {"title": "把 OBS 放在与被捕获内容同刷新率的屏幕", "detail": "预览窗口和被捕获的游戏尽量放在主屏（通常刷新率更高那块）；避免窗口捕获源跨屏。", "level": "基础"},
            {"title": "Windows 11 开启「优化窗口化游戏」或改用 WGC 捕获", "detail": "窗口捕获属性里切换为「Windows 图形捕获(WGC)」，绕过旧式 BitBlt 跨刷新率的问题。", "level": "进阶"},
            {"title": "独显直连 / 调整 GPU 分配", "detail": "笔记本用户在显卡控制面板把 OBS 与游戏都指到独立 GPU；台式机确认显示器接在独显接口上。", "level": "进阶"}
        ],
        "tips": [
            "视图 → 统计 里渲染时间长期 >10ms 就是渲染侧问题，不是网络",
            "60Hz 副屏只挂静态资料页（弹幕、聊天）影响最小"
        ],
        "related": ["lag-skip", "lag-gpu-cap", "bs-dualgpu", "lag-stats"],
        "links": [WIN26, TSIGHT]
    },
    {
        "id": "cf-steam-plugins", "category": "config",
        "title": "Steam 版 OBS：插件装不上 / 找不到插件目录",
        "platforms": ["Windows"],
        "severity": "常见",
        "symptoms": [
            "按网上教程找 C:\\Program Files\\obs-studio 却没有这个目录",
            "插件 DLL 复制过去后 OBS 里毫无反应",
            "Steam 更新后插件全部消失"
        ],
        "causes": [
            "Steam 版安装在 Steam 库目录（可能不在 C 盘）而非默认 Program Files 路径",
            "插件复制到了错误的子层级（必须 bin\\64bit）",
            "Steam 校验文件完整性时清掉了非白名单的新增文件"
        ],
        "steps": [
            {"title": "找到真实安装目录", "detail": "Steam 库 → 右键 OBS Studio → 管理 → 浏览本地文件。典型路径形如 …\\steamapps\\common\\OBS Studio。", "level": "基础"},
            {"title": "按标准结构放插件", "detail": "DLL 放 <该目录>\\obs-plugins\\64bit\\，插件的 data 目录放 <该目录>\\data\\obs-plugins\\，层级不能错。", "level": "基础"},
            {"title": "优先用插件自带安装器", "detail": "多数热门插件提供 Setup 安装包，会自动探测包括 Steam 在内的 OBS 目录，比手动复制可靠。", "level": "基础"},
            {"title": "更省心：用户级插件目录", "detail": "OBS 30.2+ 支持免管理员安装：%APPDATA%\\obs-studio\\plugins\\<插件名>\\bin\\64bits\\x64.dll 结构，且不会被 Steam 校验清理。", "level": "进阶"}
        ],
        "tips": [
            "重启 OBS 后看 日志文件 里是否列出该模块加载成功",
            "Steam 版与独立安装版不要混装，数据目录是同一个容易打架"
        ],
        "related": ["cr-plugin-load", "cr-plugin", "cf-reset"],
        "links": [STEAM, KB]
    },
    {
        "id": "cr-slow-start", "category": "crash",
        "title": "OBS 启动慢 / 卡在启动画面很久",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": [
            "双击图标后要等几十秒才出主界面",
            "启动画面停在加载场景集阶段",
            "每次启动都感觉比上次更慢"
        ],
        "causes": [
            "插件数量多，个别插件初始化慢甚至联网超时（如检查更新的插件）",
            "场景集体积过大（大量来源、浏览器源自动加载）",
            "杀毒软件实时扫描 obs64.exe 与每个插件 DLL",
            "残留的崩溃转储 / 日志堆积拖慢启动统计"
        ],
        "steps": [
            {"title": "安全模式对比测试", "detail": "启动时长按 Shift（或开始菜单里的安全模式入口）进入安全模式；秒开就说明是插件问题。", "level": "基础"},
            {"title": "二分法定位慢插件", "detail": "把 obs-plugins\\64bit 下 DLL 一半移走再启动，逐步缩小范围；找到后去项目页反馈或换替代品。", "level": "进阶"},
            {"title": "精简场景集与浏览器源", "detail": "删掉常年不用的场景 / 来源；浏览器源设「关闭时休眠」，减少启动时批量拉取网页。", "level": "基础"},
            {"title": "给 OBS 加杀毒白名单", "detail": "把 obs64.exe 与插件目录加入 Defender / 第三方杀毒的排除项，避免每次启动全量扫描。", "level": "进阶"}
        ],
        "tips": [
            "帮助 → 日志文件 → 查看当前日志，每行模块加载都带耗时，一眼看出谁最慢",
            "定期清理 %AppData%\\obs-studio\\crashes 与 logs 目录"
        ],
        "related": ["cr-safe-mode", "cr-plugin-load", "cr-antivirus", "cf-profiles"],
        "links": [ANALYZER, WIN26]
    },
    {
        "id": "enc-device-removed", "category": "encoding",
        "title": "推流中报 Device removed / GPU 挂起重置",
        "platforms": ["Windows"],
        "severity": "偶发",
        "symptoms": [
            "日志出现 device removed / GPU hung / D3D 设备丢失",
            "直播画面瞬间冻结或花屏后恢复",
            "伴随显卡驱动超时的系统提示"
        ],
        "causes": [
            "GPU 长时间满载（游戏 + 编码叠加）触发驱动 TDR 超时重置",
            "显存耗尽（分辨率 / 多滤镜 / 高纹理游戏叠加）",
            "显卡驱动 bug 或核心不稳定（超频 / 显存超频过高）",
            "供电不足或散热差导致 GPU 瞬间异常"
        ],
        "steps": [
            {"title": "恢复默认频率", "detail": "MSI Afterburner 等工具的超频（尤其是显存超频）先全部归零——device removed 最常见的诱因就是显存不稳。", "level": "基础"},
            {"title": "给 GPU 减负", "detail": "游戏内限帧（如封顶 141/58）、降低画质档位，给 OBS 编码留出余量；NVENC 走独立硬件单元，优先硬件编码。", "level": "基础"},
            {"title": "干净重装显卡驱动", "detail": "DDU 卸载后安装最新 Studio 或 Game Ready 驱动；新驱动翻车可回退上一个稳定版。", "level": "进阶"},
            {"title": "延长 / 观察 TDR", "detail": "频繁误触发可适当调大注册表 TdrDelay（默认 2 秒）；但治本仍是降低 GPU 负载，调 TDR 只是缓解。", "level": "进阶"}
        ],
        "tips": [
            "夏季高温时段多发的话先清灰换硅脂，温度墙触顶也会挂",
            "发生一次不必恐慌，连续发生才需要按流程排查"
        ],
        "related": ["enc-overload", "enc-nvenc", "cr-driver", "lag-gpu-cap", "cr-graphics-init"],
        "links": [DEVICERM, TSIGHT]
    },
    {
        "id": "au-comm-lower", "category": "audio",
        "title": "开麦说话时游戏 / 音乐音量自动变小（Windows 通信活动）",
        "platforms": ["Windows"],
        "severity": "常见",
        "symptoms": [
            "一开麦克风或语音软件，其他声音突然变小",
            "说完话音量又慢慢回来",
            "观众端听到忽大忽小的背景音"
        ],
        "causes": [
            "Windows「通信活动」检测到通话时自动压低其他声音（默认压低 80%）",
            "语音软件（Discord / QQ / 微信）的自动灵敏度或回声抑制联动",
            "麦克风设备自带的「音频增强」功能误判"
        ],
        "steps": [
            {"title": "关闭通信活动衰减", "detail": "设置 → 系统 → 声音 → 更多声音设置 → 通信活动 选项卡 → 选「不执行任何操作」。这是根治步骤。", "level": "基础"},
            {"title": "关闭设备音频增强", "detail": "声音控制面板 → 麦克风属性 → 增强/信号增强 全部取消勾选；Realtek 机型的「回声消除」「波束成形」都会误触发。", "level": "基础"},
            {"title": "检查语音软件的自动降噪", "detail": "Discord 的「自动灵敏度 / 回声消除」、腾讯会议的「自动调节音量」逐一关闭测试。", "level": "进阶"},
            {"title": "统一采样率减少误判", "detail": "把麦克风与扬声器都固定 48000Hz（16bit），部分增强功能在不同采样率混跑时更容易抽风。", "level": "进阶"}
        ],
        "tips": [
            "这个设置藏在传统声音控制面板里，新版设置页搜不到",
            "改完记得在 OBS 混音器里重新核对各轨道音量基准"
        ],
        "related": ["au-mute", "au-echo", "au-mic-noise", "av-desync"],
        "links": [KB, WIN26]
    },
    {
        "id": "lag-bitrate-mosaic", "category": "lag",
        "title": "画面糊成马赛克 / 大量色块（码率不足或波动）",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": [
            "运动镜头 / 粒子特效时满屏色块",
            "观众反馈画质模糊，但本地录制很清晰",
            "码率数字在状态栏大幅跳动"
        ],
        "causes": [
            "推流码率低于平台推荐值（1080p 通常需要 4500~8000kbps）",
            "上行带宽不稳导致动态码率不断下调",
            "平台端二次压制叠加低码率雪上加霜",
            "关键帧间隔过长导致切入瞬间花屏"
        ],
        "steps": [
            {"title": "对齐平台推荐码率", "detail": "Twitch 约 6000kbps 封顶，B站按清晰度档位（1080p 建议 4000~6000）。设置 → 推流 → 输出码率先拉到平台建议区间。", "level": "基础"},
            {"title": "实测真实上行带宽", "detail": "speedtest 实测上行 ≥ 码率 × 1.5 再开播；不够就降到 936p / 720p，稳定低清胜过卡顿高清。", "level": "基础"},
            {"title": "区分网络抖动与参数问题", "detail": "状态栏丢帧 0% 但画质差 = 码率本身不够；丢帧同步飙升 = 网络问题，按网络类条目排查。", "level": "进阶"},
            {"title": "关键帧间隔固定 2 秒", "detail": "编码器高级设置里 keyframe interval 固定 2s，别开自适应；过长会让观众中途进入时长时间模糊。", "level": "进阶"}
        ],
        "tips": [
            "同码率下 NVENC H.264 比 x264 ultrafast 观感好得多，别用极速预设凑合",
            "平台录播发灰发糊多半是二次压制，介意可同步开本地高质量录制"
        ],
        "related": ["lag-network", "lag-keyint", "lag-upload", "enc-overload", "lag-stats"],
        "links": [TSIGHT, KB]
    }
]

with open(PATH, encoding="utf-8") as f:
    data = json.load(f)

existing_ids = {p["id"] for p in data["problems"]}
for p in new_problems:
    assert p["id"] not in existing_ids, f"重复 id: {p['id']}"
    for rid in p["related"]:
        assert rid in existing_ids or any(x["id"] == rid for x in new_problems), f"related 不存在: {rid}"

data["problems"].extend(new_problems)
data["version"] = "1.7"
data["updated"] = "2026-08-23"
note = data.get("note", "").rstrip()
data["note"] = note + "2026-08-23 增补（v1.7）：插件加载失败（32.2 变更）、Nahimic/RTSS 干扰、多显示器刷新率卡顿、Steam 版插件目录、启动缓慢二分定位、Device removed/GPU 挂起、Windows 通信活动降音量、码率不足马赛克。"

with open(PATH, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
    f.write("\n")

print(f"OK: v{data['version']}, 共 {len(data['problems'])} 条")
