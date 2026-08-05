# -*- coding: utf-8 -*-
"""为 OBS_Helper 问题库新增 10 条问题（来自 2026-08-05 网络调研的缺口主题）。"""
import json
import io

PATH = r"F:\OBS\NOBS\OBS_Helper.Wpf\Assets\problems.json"

KB = {"title": "OBS 官方 · Windows 排障指南（黑屏/编码/音频/崩溃）", "url": "https://obsproject.com/zh-cn/kb/category/2"}
WIN26 = {"title": "OBS Windows 排障指南（2026 版）", "url": "https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html"}
EVENT = {"title": "直播常见 OBS 问题修复（掉帧/音画/无画面）", "url": "https://eventlive.pro/blog/obs-problems-live-streaming-events"}
BLACK = {"title": "OBS 黑屏修复（笔记本双显卡 GPU 选择）", "url": "https://www.fixlabguide.com/2025/09/how-to-fix-obs-black-screen-on-windows.html"}
GUIDE = {"title": "OBS Studio 常见问题解决方案（黑屏到直播通关指南）", "url": "https://blog.csdn.net/gitblog_01176/article/details/151498675"}

new_problems = [
    {
        "id": "bs-game-anticheat", "category": "black-screen",
        "title": "反作弊保护的游戏（Valorant / CS2 / Fortnite 等）捕获黑屏",
        "platforms": ["Windows"],
        "severity": "常见",
        "symptoms": ["游戏捕获为黑屏，但桌面与窗口捕获正常", "堡垒之夜、无畏契约、CS2 等带反作弊的游戏无法捕获", "游戏窗口化后可以捕获但全屏时黑屏"],
        "causes": ["游戏反作弊（Vanguard / VAC / Easy Anti-Cheat）禁止外部程序注入捕获钩子", "游戏运行在独占全屏模式，桌面合成层不可见", "笔记本双显卡下 OBS 与游戏使用不同 GPU"],
        "steps": [
            {"title": "改用窗口捕获（WGC）", "detail": "把游戏设为「无边框窗口化」，添加窗口捕获源并选择「Windows 10/11 (WGC)」捕获方式；此方法对 Valorant、Fortnite、Genshin 等游戏最稳定。", "level": "基础"},
            {"title": "强制游戏运行在 DX11 模式", "detail": "Steam 游戏可在启动选项加 -dx11；部分游戏设置里有「渲染接口 / 渲染模式」，从 DX12 切到 DX11 往往就能被捕获。", "level": "进阶"},
            {"title": "OBS 与游戏同卡运行", "detail": "笔记本用户在 Windows 设置 → 系统 → 显示 → 图形 中把 OBS 与游戏都设为「高性能（独显）」，避免跨 GPU 捕获。", "level": "进阶"},
            {"title": "显示器捕获兜底", "detail": "仍捕获不到时，用显示器捕获整屏画面（需管理员权限）；注意不要和游戏窗口重叠出隐私内容。", "level": "兜底"}
        ],
        "tips": ["同一场景里不要放多个游戏捕获源，只放一个并指定目标窗口", "关闭 RTSS / MSI Afterburner / Discord 覆盖层等冲突软件再试"],
        "related": ["bs-game", "bs-dualgpu", "bs-win11"],
        "links": [BLACK, KB]
    },
    {
        "id": "bs-browser-src", "category": "config",
        "title": "浏览器源（Browser Source）白屏 / 不加载内容",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["浏览器源区域空白或白屏", "网页内容 / 聊天框 / 动态壁纸不显示", "声音正常但画面不刷新"],
        "causes": ["网页本身禁止 iframe 嵌入（X-Frame-Options / CSP）", "浏览器源需要联网或需要 WebSocket 权限", "GPU 硬件加速崩溃导致 CEF 渲染失败", "自定义 CSS / JS 写错导致页面空白"],
        "steps": [
            {"title": "确认网页允许嵌入", "detail": "部分网站（如登录态的网页面板）禁止 iframe 加载；换用支持嵌入的地址，或使用「本地 HTML 文件 + file:// 协议」。", "level": "基础"},
            {"title": "刷新 / 重启浏览器源", "detail": "右键来源 → 刷新缓存；仍空白则删掉该来源重新添加，多数 CEF 偶发崩溃能这样恢复。", "level": "基础"},
            {"title": "关闭硬件加速重试", "detail": "在浏览器源属性里取消「关闭硬件加速时仍启用」以外的加速选项，或临时把 Chrome 的硬件加速关掉对比。", "level": "进阶"},
            {"title": "检查本地文件与 CSS", "detail": "本地 HTML 用 file:// 协议时，脚本 / CSS 不能引用 http 资源（跨域会被拦）；确认没有语法错误。", "level": "进阶"}
        ],
        "tips": ["浏览器源占资源较高，多开会卡，尽量合并成一个面板", "OBS 升级大版本后浏览器源偶发白屏，重建来源通常能解决"],
        "related": ["lag-browser", "st-chat", "cr-plugin"],
        "links": [GUIDE, KB]
    },
    {
        "id": "rc-replay-buffer", "category": "recording",
        "title": "回放缓冲（Replay Buffer）无法启用 / 保存失败 / 片段丢失",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["设置里「启用回放缓冲」是灰的", "按保存热键没反应或提示失败", "保存出来的片段是空文件 / 开头被截断"],
        "causes": ["录制 / 推流编码器未配置，回放缓冲必须依托其中一个编码器", "输出模式里「输出模式」选了高级但没选任何编码", "磁盘空间不足或写入权限问题", "保存热键未设置"],
        "steps": [
            {"title": "先启用录制或推流编码器", "detail": "回放缓冲基于录制 / 推流编码器运行。在 设置 → 输出 里确认「录像」或「推流」页签下已选编码器（如 x264 / NVENC）。", "level": "基础"},
            {"title": "勾选并设置缓冲时长", "detail": "设置 → 输出 → 录像 → 勾选「启用回放缓冲」，把「回放缓冲时长」设为 30~120 秒（越长越占内存与磁盘）。", "level": "基础"},
            {"title": "设置保存热键", "detail": "设置 → 热键 → 找到「保存回放缓冲」，绑定一个组合键（如 Ctrl+F8），直播中按下即可存下刚才一段。", "level": "基础"},
            {"title": "检查磁盘与权限", "detail": "确认录像路径所在磁盘剩余空间充足（至少缓冲时长的数倍），且目录可写；必要时更换到其他盘。", "level": "进阶"}
        ],
        "tips": ["缓冲时长不建议超过 5 分钟，否则内存占用大", "长时间直播后按保存，建议先观察右下角是否提示保存成功再继续"],
        "related": ["rc-local", "cf-hotkeys"],
        "links": [EVENT, KB]
    },
    {
        "id": "cr-graphics-init", "category": "crash",
        "title": "启动即崩溃 / 黑屏：Failed to initialize graphics context",
        "platforms": ["Windows"],
        "severity": "常见",
        "symptoms": ["OBS 启动后立刻退出或卡在黑屏", "日志里出现 Failed to initialize graphics context", "更新显卡驱动后出现"],
        "causes": ["显卡驱动与 OBS 渲染接口（Direct3D 11）不兼容", "驱动损坏或版本过旧", "多显卡笔记本 GPU 切换异常"],
        "steps": [
            {"title": "切换到 OpenGL 渲染器", "detail": "按住 Shift 打开 OBS 的「配置向导」，或编辑 %AppData%\\obs-studio\\global.ini 把 渲染器 从 d3d11 改为 opengl（OBS 启动选项也可加 --renderer opengl）。", "level": "基础"},
            {"title": "更新 / 回滚显卡驱动", "detail": "用 DDU 干净卸载后安装最新正式版驱动；若最新版仍崩溃，回退到上一版。", "level": "进阶"},
            {"title": "笔记本指定独显运行 OBS", "detail": "Windows 设置 → 系统 → 显示 → 图形 → 把 OBS 设为「高性能（独显）」；有时核显初始化失败导致此报错。", "level": "进阶"},
            {"title": "安全模式排查插件", "detail": "用 --safe-mode 启动，排除第三方插件调用图形接口导致的初始化失败。", "level": "进阶"}
        ],
        "tips": ["保留最新的 crash 日志（%AppData%\\obs-studio\\crashes），反馈问题时一并上传"],
        "related": ["cr-driver", "cr-safe-mode", "cr-plugin"],
        "links": [GUIDE, WIN26]
    },
    {
        "id": "cf-chroma", "category": "setup",
        "title": "绿幕抠像不干净 / 色度键（Chroma Key）边缘毛边",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["人物边缘有绿色光晕或毛边", "衣服 / 皮肤上出现透明空洞", "抠像后背景透出噪点"],
        "causes": ["绿幕布有褶皱、阴影或反光不均", "灯光不足或人物身上有绿色反光", "色度键参数（相似度 / 平滑 / 去溢出色）没调好", "摄像头画质差、压缩噪声大"],
        "steps": [
            {"title": "把灯光打匀", "detail": "绿幕用两盏灯均匀照亮，避免褶皱与阴影；人物与绿幕保持 1~2 米距离，防止绿色反光到身上。", "level": "基础"},
            {"title": "添加色度键滤镜并调整参数", "detail": "右键摄像头 → 滤镜 → 添加「色度键」：先提高「相似度」抠干净，再小幅调「平滑」消除毛边，最后用「去溢出色」去掉边缘绿色。", "level": "进阶"},
            {"title": "提高摄像头清晰度", "detail": "在摄像头属性里把分辨率调到 720p/1080p、关闭自动曝光与自动白平衡，降低压缩噪声后抠像更干净。", "level": "进阶"},
            {"title": "避免绿色系服装", "detail": "人物不要穿绿色 / 荧光色衣服、戴绿色饰品，否则会被一并抠掉。", "level": "提示"}
        ],
        "tips": ["抠像不追求 100% 干净，边缘留一点绿色比满屏噪点更好看", "条件允许时优先用「虚拟背景」插件或 AI 抠像，不依赖绿幕"],
        "related": ["st-scene", "au-mic-noise"],
        "links": [GUIDE, KB]
    },
    {
        "id": "rc-media-source", "category": "recording",
        "title": "媒体源（本地视频 / 音乐）无法播放、绿屏或没有声音",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["添加本地视频后画面绿屏或黑屏", "视频播放但只有画面没有声音", "音乐文件添加后不发声", "循环播放或结束时不按要求停止"],
        "causes": ["编码格式 OBS 不支持（如 HEVC 10bit、Dolby 音轨、个别容器）", "音轨被 FFmpeg 默认丢弃（多音轨 mkv）", "文件在移动硬盘 / 网络盘上读取慢", "「循环」与「在结束时停止」选项冲突"],
        "steps": [
            {"title": "转码后再导入", "detail": "用 HandBrake / 格式工厂把视频转成 H.264 + AAC 的 MP4，兼容性最好；HEVC 10bit 等格式 OBS 播放支持差。", "level": "基础"},
            {"title": "勾选「在媒体结束前播放音轨」", "detail": "媒体源属性 → 音频：多音轨文件需要手动选择音轨（Track 1/2/3）；确保勾选「在媒体结束前播放音轨」否则不出声。", "level": "进阶"},
            {"title": "避免从网络盘 / 移动盘直接播放", "detail": "把素材复制到本地固态盘再添加，USB 2.0 或网络盘的读取速度会导致卡顿与绿屏。", "level": "进阶"},
            {"title": "检查循环与停止设置", "detail": "需要循环播放就只勾「循环」，不要同时勾「在结束时停止」；音乐类素材建议用「媒体源」+ 循环。", "level": "基础"}
        ],
        "tips": ["直播背景音乐更推荐用浏览器源播放歌单，可随时切歌", "录制时确保媒体源在预览里正常播放再开播，避免事故"],
        "related": ["rc-nofile", "au-mute"],
        "links": [KB, EVENT]
    },
    {
        "id": "cf-text-cjk", "category": "config",
        "title": "文本来源中文乱码 / 字体不生效 / 无法显示",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["文本来源里中文显示为方块或乱码", "选的中文字体不生效，总是默认字体", "添加文本来源后直接闪退或空白"],
        "causes": ["所选字体不支持中文（如 Arial）", "系统字体列表加载不全 / 字体缓存损坏", "文本来源旧版（text_ft2）与新版（text_gdiplus）渲染差异"],
        "steps": [
            {"title": "选择支持中文的字体", "detail": "在文本来源属性里选 微软雅黑 / 思源黑体 / 楷体 等中文字体，字号建议 ≥24，避免渲染过小发虚。", "level": "基础"},
            {"title": "更新到新式文本来源", "detail": "OBS 30+ 提供「文本（GDI+）」新来源，中文渲染更好；旧「文本（FreeType 2）」对中文支持较差，优先用新版。", "level": "进阶"},
            {"title": "刷新字体缓存", "detail": "Windows 删除 %SystemRoot%\\Fonts 下的字体缓存文件后重启（或重建字体缓存服务）；macOS 用「字体册」修复字体。", "level": "进阶"},
            {"title": "用图片 / 浏览器源代替", "detail": "需要复杂排版（阴影、描边、渐变色）时，用设计工具导出透明 PNG，或用浏览器源加载 HTML，效果更可控。", "level": "提示"}
        ],
        "tips": ["动态文字（跑马灯 / 弹幕）用浏览器源 + HTML/CSS 实现最方便", "直播标题建议单独放一层文字，方便随时改"],
        "related": ["st-chat", "bs-browser-src"],
        "links": [KB, GUIDE]
    },
    {
        "id": "cf-transition", "category": "config",
        "title": "场景过渡效果不生效 / 切换生硬",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["场景切换是硬切，没有淡入淡出", "过渡选好后只生效一次", "工作室模式下「转换」按钮不可用"],
        "causes": ["过渡面板没选中任何过渡，或选中了「立即切换」", "过度时长设为 0ms", "工作室模式没设置过渡（只设了预览）"],
        "steps": [
            {"title": "在过渡面板选择一个过渡", "detail": "OBS 底部「过渡」下拉框选择「淡入淡出 / 切出 / 滑入」等，旁边数字是时长（建议 200~500ms）。", "level": "基础"},
            {"title": "检查时长不为 0", "detail": "过渡时长设为 0 就等于硬切；把数值调回 300ms 左右。", "level": "基础"},
            {"title": "工作室模式里两个过渡都要设", "detail": "工作室模式下「预览过渡」与「节目过渡」分别设置；点「转换」按钮才会带过渡切换。", "level": "进阶"},
            {"title": "自定义过渡检查插件", "detail": "用了 Stinger / 自定义过渡插件时，确认插件文件路径与时长设置正确。", "level": "进阶"}
        ],
        "tips": ["直播中保持过渡风格统一，别每个场景用不同花哨过渡", "开播 / 下播用「滑入」或 Stinger 更有仪式感"],
        "related": ["st-scene", "cf-hotkeys"],
        "links": [EVENT, KB]
    },
    {
        "id": "au-mic-clip", "category": "audio",
        "title": "麦克风爆音 / 削波（增益过高导致声音发破）",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["说话大声时声音发破、有爆音", "音频混音器里麦克风条经常顶满变红", "录制的文件声音刺耳、波形削顶"],
        "causes": ["麦克风增益（Gain）或系统麦克风增强拉得过高", "距离麦克风太近或说话音量波动大", "设备采样率与 OBS 不一致产生杂音"],
        "steps": [
            {"title": "降低增益到不削波", "detail": "音频混音器里麦克风音量调到 60~80%，说话最大声时条不要顶到红色区域；必要时把系统「麦克风增强」归零。", "level": "基础"},
            {"title": "添加限制器滤镜", "detail": "右键麦克风 → 滤镜 → 添加「限制器」，阈值设 -6dB 左右，可以兜住突然的大声。", "level": "进阶"},
            {"title": "降噪 + 压缩让声音更稳", "detail": "依次加「噪声抑制（RNNoise）」→「压缩器（阈值 -20dB、比例 3:1）」→「限制器」，人声更清晰且不会爆。", "level": "进阶"},
            {"title": "统一采样率", "detail": "Windows 声音设置与 OBS 设置 → 音频 → 采样率都设为 48kHz，避免杂音。", "level": "进阶"}
        ],
        "tips": ["声音宁可偏小也不要削波——后期可以放大，削掉的细节找不回来", "先用 OBS 的「录制一段测试」确认波形再正式开播"],
        "related": ["au-mic-quiet", "au-mic-noise", "av-sample"],
        "links": [EVENT, KB]
    },
    {
        "id": "lag-ms-latency", "category": "lag",
        "title": "观众端延迟过大（直播延迟数十秒）",
        "platforms": ["Windows", "macOS"],
        "severity": "常见",
        "symptoms": ["观众看到的画面比实况晚 20 秒以上", "弹幕互动对不上画面", "直播平台后台提示延迟过高"],
        "causes": ["直播平台默认缓冲大（部分平台默认 15~30 秒）", "主播设置了较高码率 + 平台转码排队", "网络抖动触发平台加大缓冲"],
        "steps": [
            {"title": "开启低延迟模式", "detail": "多数平台（B站 / 抖音 / Twitch）有「低延迟 / Low Latency」选项，开启后可压到 5~8 秒；OBS 侧无需改动。", "level": "基础"},
            {"title": "使用正确的推流协议", "detail": "平台支持时优先用 RTMPS 或 SRT（新平台），比传统 RTMP 延迟低且抗丢包更好。", "level": "进阶"},
            {"title": "适度降低码率", "detail": "1080p60 用 6000kbps 以上时若上行不稳，会引发平台加大缓冲；降到 4500~5000kbps 更稳。", "level": "进阶"},
            {"title": "检查平台转码", "detail": "直播后台看是否有「转码中」状态；部分平台对非热门直播间强制转码会额外加延迟，可联系客服调整。", "level": "提示"}
        ],
        "tips": ["互动型直播（聊天、答题）务必开低延迟；录播型内容（课程回放）延迟大点无所谓", "网络差时延迟变大是平台自我保护，别在低延迟模式 + 高码率之间两头要"],
        "related": ["lag-network", "sf-rtmps", "lag-upload"],
        "links": [EVENT, KB]
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

data["version"] = "1.3"
data["updated"] = "2026-08-05"
data["note"] = "已扩充常见与罕见 OBS 问题、macOS 端问题及直播间搭建引导（含各平台接入与通用流程）。2026-08-05 增补：反作弊游戏捕获、浏览器源白屏、回放缓冲、绿幕抠像、媒体源、文本中文乱码、场景过渡、麦克风爆音、观众端延迟等。"

with io.open(PATH, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
    f.write("\n")

print(f"完成：新增 {added} 条问题，共 {len(data['problems'])} 条；版本 -> {data['version']}（{data['updated']}）")
