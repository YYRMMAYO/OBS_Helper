# -*- coding: utf-8 -*-
"""为 OBS_Helper 场景模板库新增 6 套模板（直播 + 录制），保持既有 JSON schema。"""
import json
import io

PATH = r"F:\OBS\NOBS\OBS_Helper.Wpf\Assets\scene_templates.json"

def src(name, kind, z, enabled=True, settings=None, fb=None, ph=None, transform=None, shared=False):
    d = {"name": name, "inputKind": kind, "zOrder": z, "enabled": enabled}
    if settings: d["settings"] = settings
    if fb: d["fallbackKinds"] = fb
    if ph: d["placeholder"] = ph
    if transform: d["transform"] = transform
    if shared: d["shared"] = True
    return d

def full(w, h):
    return {"posX": 0, "posY": 0, "boundsType": "OBS_BOUNDS_SCALE_OUTER",
            "boundsWidth": w, "boundsHeight": h, "alignment": 0}

def text(name, z, value, size=44, x=60, y=40, align=5):
    return src(name, "text_gdiplus_v3", z, settings={
        "text": value, "font": {"face": "微软雅黑", "size": size}, "color": 4294967295
    }, fb=["text_ft2_source_v3"],
        transform={"posX": x, "posY": y, "boundsType": "OBS_BOUNDS_NONE", "alignment": align})

def cam(name, z, x, y, bw=520, bh=None, align=5, btype="OBS_BOUNDS_TO_WIDTH"):
    t = {"posX": x, "posY": y, "boundsType": btype, "alignment": align}
    if bw: t["boundsWidth"] = bw
    if bh: t["boundsHeight"] = bh
    return src(name, "dshow_input", z, fb=["dshow_video_source"],
               ph={"kind": "device", "hint": "选择你的摄像头"},
               transform=t)

def mic(z=3):
    return src("我的麦克风", "wasapi_input_capture", z, shared=True,
               ph={"kind": "device", "hint": "选择你的麦克风"})

def desktop_audio(z=4):
    return src("桌面音频", "wasapi_output_capture", z,
               ph={"kind": "device", "hint": "选择桌面 / 系统声音"})

def color(name, z, hexcolor=4278190080):
    return src(name, "color_source_v3", z, settings={"color": hexcolor}, transform=full(1920, 1080))

H_CANVAS = {"baseWidth": 1920, "baseHeight": 1080, "outputWidth": 1920, "outputHeight": 1080,
            "fpsNumerator": 30, "fpsDenominator": 1}
V_CANVAS = {"baseWidth": 1080, "baseHeight": 1920, "outputWidth": 1080, "outputHeight": 1920,
            "fpsNumerator": 30, "fpsDenominator": 1}

new_templates = [
    # 1. 绿幕虚拟背景
    {
        "id": "green-screen", "title": "绿幕虚拟背景",
        "summary": "人物抠像叠加到虚拟背景图，适合游戏、讲课、直播换背景。",
        "icon": "🟢", "portrait": False,
        "notes": "摄像头需在绿幕前拍摄；没有绿幕时可在 OBS 里删掉「色度键」滤镜直接当普通画面用。背景图、摄像头与麦克风需你在 OBS 里选择。",
        "canvas": H_CANVAS, "transition": "淡入淡出", "transitionDurationMs": 300,
        "scenes": [
            {"name": "直播中", "transitionDurationMs": 300, "hotkey": "Ctrl+1", "sources": [
                src("虚拟背景", "image_source", 0, settings={"file": ""},
                    ph={"kind": "file", "hint": "选择虚拟背景图片"}, transform=full(1920, 1080)),
                cam("摄像头（绿幕抠像）", 1, 420, 60, bw=1080, align=0),
                mic(2), desktop_audio(3),
                text("直播标题", 4, "绿幕直播中 · 欢迎来到直播间", 44, 60, 40),
            ]},
            {"name": "马上回来 BRB", "transitionDurationMs": 500, "hotkey": "Ctrl+2", "sources": [
                color("背景色", 0, 4278650880),
                text("BRB 文案", 1, "马上回来", 96, 960, 430, 0),
            ]},
        ],
    },
    # 2. 带货双机位
    {
        "id": "product-duo", "title": "带货双机位",
        "summary": "主播全景 + 产品特写双机位，商品图与价格条随时上屏，适合直播带货。",
        "icon": "🛍️", "portrait": False,
        "notes": "需要两个摄像头（可用手机 + 采集卡当第二机位）。「产品特写」场景中的商品图默认隐藏，需要时点开来源可见即可。",
        "canvas": H_CANVAS, "transition": "淡入淡出", "transitionDurationMs": 300,
        "scenes": [
            {"name": "主播全景", "transitionDurationMs": 300, "hotkey": "Ctrl+1", "sources": [
                color("背景色", 0),
                cam("主机位（主播全景）", 1, 0, 0, bw=1920, bh=1080, align=0, btype="OBS_BOUNDS_SCALE_OUTER"),
                text("商品标题条", 2, "今日好物 · 点击购买", 40, 60, 940),
                mic(3), desktop_audio(4),
            ]},
            {"name": "产品特写", "transitionDurationMs": 300, "hotkey": "Ctrl+2", "sources": [
                color("背景色", 0),
                cam("特写机位（产品细节）", 1, 0, 0, bw=1920, bh=1080, align=0, btype="OBS_BOUNDS_SCALE_OUTER"),
                src("商品图", "image_source", 2, enabled=False, settings={"file": ""},
                    ph={"kind": "file", "hint": "选择要展示的商品图片（默认隐藏，需要时点开）"},
                    transform={"posX": 200, "posY": 120, "boundsType": "OBS_BOUNDS_SCALE_OUTER",
                               "boundsWidth": 1520, "boundsHeight": 720, "alignment": 0}),
                text("价格标签", 3, "限时特价 ¥99", 56, 60, 60),
                mic(4), desktop_audio(5),
            ]},
        ],
    },
    # 3. 电竞赛事解说
    {
        "id": "esports-cast", "title": "电竞赛事解说",
        "summary": "游戏画面全屏 + 右下解说头像 + 比分条，适合赛事转播与观战解说。",
        "icon": "🏆", "portrait": False,
        "notes": "游戏画面优先用「游戏捕获」，找不到窗口会回退到显示器捕获；比分条文字可双击修改。",
        "canvas": {"baseWidth": 1920, "baseHeight": 1080, "outputWidth": 1920, "outputHeight": 1080,
                   "fpsNumerator": 60, "fpsDenominator": 1},
        "transition": "淡入淡出", "transitionDurationMs": 300,
        "scenes": [
            {"name": "解说画面", "transitionDurationMs": 300, "hotkey": "Ctrl+1", "sources": [
                src("游戏画面", "game_capture", 0, fb=["window_capture", "monitor_capture"],
                    ph={"kind": "window", "hint": "选择要捕获的游戏窗口或显示器"}, transform=full(1920, 1080)),
                cam("解说头像", 1, 1490, 820, bw=380, align=5),
                text("比分条", 2, "队伍A 2 : 1 队伍B", 44, 60, 40),
                mic(3), desktop_audio(4),
            ]},
            {"name": "中场休息", "transitionDurationMs": 500, "hotkey": "Ctrl+2", "sources": [
                color("背景色", 0, 4278650880),
                text("中场文案", 1, "中场休息 · 马上回来", 72, 960, 460, 0),
                desktop_audio(2),
            ]},
        ],
    },
    # 4. 播客对谈
    {
        "id": "podcast-duo", "title": "播客对谈",
        "summary": "双嘉宾并排摄像头 + 背景图，适合访谈、播客与圆桌讨论。",
        "icon": "🎙️", "portrait": False,
        "notes": "需要两个摄像头；也可把「嘉宾机位」换成窗口捕获来连麦画面。背景图与设备需你在 OBS 里选择。",
        "canvas": H_CANVAS, "transition": "淡入淡出", "transitionDurationMs": 300,
        "scenes": [
            {"name": "对谈画面", "transitionDurationMs": 300, "hotkey": "Ctrl+1", "sources": [
                src("底图", "image_source", 0, settings={"file": ""},
                    ph={"kind": "file", "hint": "选择对谈背景图"}, transform=full(1920, 1080)),
                cam("主机位", 1, 120, 220, bw=820, align=0),
                cam("嘉宾机位", 2, 980, 220, bw=820, align=0),
                text("节目标题", 3, "今天聊点啥 · 第 1 期", 40, 60, 40),
                mic(4),
            ]},
            {"name": "等待开播", "transitionDurationMs": 500, "hotkey": "Ctrl+2", "sources": [
                color("背景色", 0, 4278650880),
                text("开场文案", 1, "即将开始 · 敬请期待", 72, 960, 460, 0),
            ]},
        ],
    },
    # 5. 竖屏短视频录制
    {
        "id": "short-video", "title": "竖屏短视频录制",
        "summary": "9:16 竖屏画布，摄像头居中 + 上下标题条，直接录制短视频或竖屏直播。",
        "icon": "📱", "portrait": True,
        "notes": "画布与输出均为 1080×1920；短视频平台竖屏直接可用。上下标题条的文字可双击修改。",
        "canvas": V_CANVAS, "transition": "淡入淡出", "transitionDurationMs": 300,
        "scenes": [
            {"name": "录制中", "transitionDurationMs": 300, "hotkey": "Ctrl+1", "sources": [
                color("背景色", 0),
                cam("摄像头（居中）", 1, 200, 240, bw=680, align=0),
                src("顶部标题", "text_gdiplus_v3", 2, settings={
                    "text": "短视频标题", "font": {"face": "微软雅黑", "size": 52}, "color": 4294967295
                }, fb=["text_ft2_source_v3"],
                    transform={"posX": 540, "posY": 100, "boundsType": "OBS_BOUNDS_NONE", "alignment": 0}),
                src("底部字幕", "text_gdiplus_v3", 3, settings={
                    "text": "喜欢记得点赞关注哦", "font": {"face": "微软雅黑", "size": 40}, "color": 4294967295
                }, fb=["text_ft2_source_v3"],
                    transform={"posX": 540, "posY": 1750, "boundsType": "OBS_BOUNDS_NONE", "alignment": 0}),
                mic(4),
            ]},
            {"name": "片尾", "transitionDurationMs": 500, "hotkey": "Ctrl+2", "sources": [
                color("背景色", 0, 4278650880),
                src("片尾文案", "text_gdiplus_v3", 1, settings={
                    "text": "感谢观看 · 我们下期再见", "font": {"face": "微软雅黑", "size": 64}, "color": 4294967295
                }, fb=["text_ft2_source_v3"],
                    transform={"posX": 540, "posY": 900, "boundsType": "OBS_BOUNDS_NONE", "alignment": 0}),
            ]},
        ],
    },
    # 6. 会议课程多机位
    {
        "id": "meeting-teach", "title": "会议课程多机位",
        "summary": "课件主屏 + 讲师画中画 + 白板特写机位，适合线上课程、演示与会议。",
        "icon": "🎓", "portrait": False,
        "notes": "「课件窗口」用窗口捕获选择 PPT / 浏览器；「白板特写」需要第二摄像头或用手机投屏。",
        "canvas": H_CANVAS, "transition": "淡入淡出", "transitionDurationMs": 300,
        "scenes": [
            {"name": "课件讲解", "transitionDurationMs": 300, "hotkey": "Ctrl+1", "sources": [
                src("课件窗口", "window_capture", 0, fb=["monitor_capture"],
                    settings={"method": 2}, ph={"kind": "window", "hint": "选择 PPT / 浏览器窗口（WGC 捕获）"},
                    transform=full(1920, 1080)),
                cam("讲师画中画", 1, 1490, 820, bw=380, align=5),
                text("课程标题", 2, "课程标题 · 第 1 章", 40, 60, 40),
                mic(3), desktop_audio(4),
            ]},
            {"name": "讲师特写", "transitionDurationMs": 300, "hotkey": "Ctrl+2", "sources": [
                color("背景色", 0),
                cam("讲师特写", 1, 0, 0, bw=1920, bh=1080, align=0, btype="OBS_BOUNDS_SCALE_OUTER"),
                text("讲师姓名条", 2, "讲师 · 姓名", 40, 60, 940),
                mic(3), desktop_audio(4),
            ]},
            {"name": "白板特写", "transitionDurationMs": 300, "hotkey": "Ctrl+3", "sources": [
                color("背景色", 0),
                cam("白板 / 实验机位", 1, 0, 0, bw=1920, bh=1080, align=0, btype="OBS_BOUNDS_SCALE_OUTER"),
                mic(2), desktop_audio(3),
            ]},
        ],
    },
]

with io.open(PATH, "r", encoding="utf-8") as f:
    data = json.load(f)

existing_ids = {t["id"] for t in data}
added = 0
for t in new_templates:
    if t["id"] in existing_ids:
        print(f"SKIP (已存在): {t['id']}")
        continue
    data.append(t)
    added += 1

with io.open(PATH, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
    f.write("\n")

print(f"完成：新增 {added} 套模板，共 {len(data)} 套")
for t in data:
    print(" -", t["id"], "|", t["title"], "|", [s["name"] for s in t["scenes"]])
