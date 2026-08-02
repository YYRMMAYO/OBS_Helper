#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Augment content/problems.json with official doc links per category and append
web-sourced common problems. Then sync to the Client wwwroot copy."""
import json, shutil, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MASTER = os.path.join(ROOT, "content", "problems.json")
CLIENT_COPY = os.path.join(ROOT, "OBS_Helper.Client", "wwwroot", "data", "problems.json")

# Canonical official link library
L = {
    "mac_perm":   {"title": "OBS 官方 · macOS 屏幕录制/麦克风/辅助功能权限指南", "url": "https://obsproject.com/kb/macos-permissions-guide"},
    "win_tshoot": {"title": "OBS 官方 · Windows 排障指南（黑屏/编码/音频/崩溃）", "url": "https://obs-studio-app.github.io/obs-studio-troubleshooting-windows.html"},
    "black_fix":  {"title": "OBS Versions · 黑屏与编码问题修复教程", "url": "https://obs-versions.com/blog/fix-obs-black-screen"},
    "analyzer":   {"title": "OBS 官方 · 日志分析器（自动诊断日志）", "url": "https://obsproject.com/tools/analyzer"},
    "cn_guide":   {"title": "OBS 中文站 · 入门与基础配置指南", "url": "https://www.obsproject.com.cn/obs/62.html"},
    "official":   {"title": "OBS 官方首页 · 下载与文档", "url": "https://obsproject.com"},
}

# Category -> links
CAT_LINKS = {
    "black-screen": [L["mac_perm"], L["black_fix"], L["win_tshoot"]],
    "encoding":     [L["win_tshoot"], L["black_fix"], L["analyzer"]],
    "lag":          [L["win_tshoot"], L["analyzer"]],
    "avsync":       [L["win_tshoot"], L["analyzer"]],
    "audio":        [L["mac_perm"], L["win_tshoot"]],
    "streamfail":   [L["analyzer"], L["win_tshoot"]],
    "setup":        [L["official"], L["cn_guide"]],
    "recording":    [L["win_tshoot"], L["analyzer"]],
    "config":       [L["cn_guide"], L["win_tshoot"]],
    "crash":        [L["win_tshoot"], L["mac_perm"], L["analyzer"]],
}

with open(MASTER, encoding="utf-8") as f:
    data = json.load(f)

existing = {p["id"] for p in data["problems"]}

# 1) attach links to every existing problem
for p in data["problems"]:
    p["links"] = CAT_LINKS.get(p["category"], [L["official"]])

# 2) new web-sourced common problems
NEW = [
    {
        "id": "bs-mac-perm",
        "category": "black-screen",
        "title": "macOS 屏幕录制权限导致黑屏",
        "platforms": ["macOS"],
        "severity": "常见",
        "symptoms": [
            "显示器/窗口捕获源预览窗口全黑",
            "OBS 能打开但捕获不到任何画面",
            "系统权限弹窗未出现或曾点过「不允许」"
        ],
        "causes": [
            "macOS 未授予 OBS「屏幕录制」权限",
            "升级 macOS 大版本或重装 OBS 后权限被重置",
            "曾在系统设置里拒绝过该权限"
        ],
        "steps": [
            {"title": "通过 OBS 内置入口开启权限", "detail": "点击菜单栏 OBS Studio → Review App Permissions（查看应用权限），在弹窗中逐项开启 Screen Recording / Camera / Microphone / Accessibility。", "level": "基础"},
            {"title": "系统设置手动授权", "detail": "打开 系统设置 → 隐私与安全性 → 屏幕录制，找到 OBS Studio 并开启开关；麦克风与辅助功能同理。修改后必须完全退出 OBS（Cmd+Q）再重新打开才生效。", "level": "进阶"},
            {"title": "彻底重启 OBS 与系统", "detail": "仅关闭窗口不够，需 Cmd+Q 退出进程；若仍黑屏，重启 macOS 让权限变更生效。", "level": "基础"}
        ],
        "tips": [
            "授予权限后第一次捕获会弹出系统询问，务必点「好」",
            "升级 macOS 大版本后建议复查一次权限"
        ],
        "related": ["bs-display", "bs-window", "cr-mac-crash"],
        "links": [L["mac_perm"]]
    },
    {
        "id": "cr-safe-mode",
        "category": "crash",
        "title": "用安全模式(safe-mode)排查 OBS 启动崩溃",
        "platforms": ["Windows", "macOS"],
        "severity": "严重",
        "symptoms": [
            "双击 OBS 直接闪退或无响应",
            "进入主界面之前就崩溃",
            "安装新插件或某次更新后才出现"
        ],
        "causes": [
            "第三方插件冲突",
            "显卡驱动或运行库（Visual C++ Redistributable）损坏",
            "配置文件损坏"
        ],
        "steps": [
            {"title": "以安全模式启动", "detail": "Windows：按住 Shift 双击 OBS，或在快捷方式目标后加 --safe-mode；macOS：打开时按住 Alt，或在终端执行 open -n /Applications/OBS.app --args --safe-mode。安全模式会禁用所有插件。", "level": "基础"},
            {"title": "定位冲突插件", "detail": "若安全模式能正常打开，说明是插件问题。到 帮助 → 插件 中逐个禁用最近安装的插件，再正常启动验证。", "level": "进阶"},
            {"title": "修复运行库/驱动", "detail": "Windows 重装最新 Visual C++ Redistributable 并更新显卡驱动；必要时删除 %APPDATA%\\obs-studio\\crashes 后重试。", "level": "进阶"},
            {"title": "回退或重装 OBS", "detail": "若配置损坏，备份后重装 OBS；若问题始于某次更新，可下载旧稳定版（如 30.2.3）。", "level": "进阶"}
        ],
        "tips": [
            "安全模式是区分「插件问题」与「OBS 本体问题」的最快办法",
            "提交 issue 前先用日志分析器并附上崩溃日志"
        ],
        "related": ["cr-plugin", "cr-vcredist", "cr-mac-crash"],
        "links": [L["win_tshoot"], L["mac_perm"], L["analyzer"]]
    },
    {
        "id": "au-blackhole",
        "category": "audio",
        "title": "macOS 采集桌面/系统音频（BlackHole 虚拟设备）",
        "platforms": ["macOS"],
        "severity": "一般",
        "symptoms": [
            "macOS 下没有「桌面音频」设备可捕获",
            "只能录到麦克风，游戏/视频声音丢失",
            "想直播电脑播放的声音但系统不提供桌面音频源"
        ],
        "causes": [
            "macOS 不像 Windows 自带「桌面音频」设备",
            "未安装虚拟音频驱动",
            "输出设备路由错误"
        ],
        "steps": [
            {"title": "安装 BlackHole 虚拟音频驱动", "detail": "从 github.com/ExistentialAudio/BlackHole 安装 BlackHole 2ch（或 16ch），安装后重启 OBS。", "level": "基础"},
            {"title": "创建多输出设备", "detail": "打开「音频 MIDI 设置」→ 创建多输出设备，勾选「内建输出」与「BlackHole 2ch」，并设为系统输出，这样既能听到声音又能被 OBS 捕获。", "level": "进阶"},
            {"title": "在 OBS 添加音频输入捕获", "detail": "OBS 来源 → 音频输入捕获 → 设备选 BlackHole 2ch，即可把系统声音混入直播/录制。", "level": "进阶"}
        ],
        "tips": [
            "BlackHole 是免费开源方案，比收费虚拟声卡轻量",
            "多输出设备延迟略高，对延迟敏感可只用 BlackHole"
        ],
        "related": ["bs-mac-perm", "cr-mac-crash"],
        "links": [L["mac_perm"]]
    },
    {
        "id": "en-nvenc",
        "category": "encoding",
        "title": "NVENC 编码失败：Failed to open / 驱动初始化错误",
        "platforms": ["Windows"],
        "severity": "严重",
        "symptoms": [
            "开始推流/录制提示 NVENC 错误",
            "日志含 \"Failed to open NVENC codec\"",
            "编码器下拉为空或选中后无反应"
        ],
        "causes": [
            "NVIDIA 驱动过旧或损坏",
            "GPU 被其他程序占用或驱动被拦截（如杀软）",
            "OBS 与新驱动版本不兼容"
        ],
        "steps": [
            {"title": "更新 NVIDIA 显卡驱动", "detail": "到 NVIDIA 官网或用 GeForce Experience 安装最新 Game Ready 驱动，安装时选「清洁安装」。", "level": "基础"},
            {"title": "检查编码器是否被占用", "detail": "关闭其他占用 NVENC 的软件（其他推流/录屏、剪映、游戏内录制），任务管理器确认无残留编码进程。", "level": "进阶"},
            {"title": "添加杀软/Defender 排除项", "detail": "把 OBS 安装目录加入 Windows Defender 排除列表，避免驱动被拦截；必要时临时关闭第三方杀软验证。", "level": "进阶"},
            {"title": "回退或换用其他硬件编码器", "detail": "若新驱动仍不行，回退到上一个稳定驱动；或改用 AMD(AMF)/Intel(QSV) 编码器，x264 作为兜底。", "level": "进阶"}
        ],
        "tips": [
            "优先用 NVENC 而非 x264 以解放 CPU",
            "笔记本双显卡需让 OBS 跑在独显上"
        ],
        "related": ["enc-overload", "enc-cpu", "bs-dualgpu"],
        "links": [L["win_tshoot"], L["black_fix"], L["analyzer"]]
    },
]

added = 0
for np in NEW:
    if np["id"] in existing:
        print("SKIP (exists):", np["id"])
        continue
    data["problems"].append(np)
    added += 1
    print("ADDED:", np["id"])

data["updated"] = "2026-08-03"
data["version"] = "1.2"

with open(MASTER, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=2)

# sync to client copy
os.makedirs(os.path.dirname(CLIENT_COPY), exist_ok=True)
shutil.copyfile(MASTER, CLIENT_COPY)

print(f"OK master={len(data['problems'])} problems, added={added}, synced client copy.")
