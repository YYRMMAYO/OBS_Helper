# -*- coding: utf-8 -*-
"""从 docs/problem_library_update_2026-08-25.md 解析 63 条新排障指引，
合并写入 Assets/problems.json（149 -> 212）。版本 1.7 -> 1.8。
解析前先做完整性校验：id 无冲突、related 引用均存在、字段齐全。"""
import json
import re
import sys

MD_PATH = r"F:\OBS\NOBS\docs\problem_library_update_2026-08-25.md"
JSON_PATH = r"F:\OBS\NOBS\OBS_Helper.Wpf\Assets\problems.json"

SECTION_RE = re.compile(r"^### (\d+)\.\s*(?:【(?:必|新)】)?\s*`([\w-]+)`｜(.+)$")


def parse_md(path):
    with open(path, encoding="utf-8") as f:
        lines = f.read().splitlines()

    problems = []
    cur = None
    field = None  # 当前收集的字段名
    for raw in lines:
        m = SECTION_RE.match(raw)
        if m:
            if cur:
                problems.append(cur)
            cur = {
                "id": m.group(2),
                "title": m.group(3).strip(),
                "symptoms": [], "causes": [], "steps": [],
                "tips": [], "related": [], "links": [],
                "platforms": [], "category": "", "severity": "常见",
            }
            field = None
            continue
        if cur is None:
            continue
        line = raw.strip()
        if not line or line.startswith(">") or line.startswith("---"):
            continue
        if line.startswith("- **分类**"):
            meta = re.split(r"[:：]", line, maxsplit=1)[1]
            parts = [p.strip() for p in meta.split("｜")]
            cur["category"] = parts[0]
            for p in parts[1:]:
                if p.startswith("**严重度**"):
                    cur["severity"] = p.split(":", 1)[1].strip() if ":" in p else p.split("：", 1)[1].strip()
                elif p.startswith("**平台**"):
                    plat = p.split(":", 1)[1] if ":" in p else p.split("：", 1)[1]
                    plat = plat.strip()
                    cur["platforms"] = ["Windows", "macOS", "Linux"] if "全平台" in plat else \
                        [x.strip() for x in plat.split("/") if x.strip()]
            field = None
        elif line.startswith("- **症状**"):
            field = "symptoms"
        elif line.startswith("- **原因**"):
            field = "causes"
        elif line.startswith("- **步骤**"):
            field = "steps"
        elif line.startswith("- **提示**"):
            field = "tips"
        elif line.startswith("- **关联**"):
            rel = line.split("：", 1)[1]
            cur["related"] = [x.strip() for x in re.split(r"[,，]", rel) if x.strip()]
            field = None
        elif line.startswith("- **链接**"):
            body = line.split("：", 1)[1]
            for seg in body.split("；"):
                lm = re.match(r"\[(.+?)\]\((.+?)\)", seg.strip())
                if lm:
                    cur["links"].append({"title": lm.group(1), "url": lm.group(2)})
            field = None
        elif raw.startswith("  - ") and field in ("symptoms", "causes", "tips"):
            cur[field].append(line[2:].strip())
        elif re.match(r"^\s+\d+\.\s", raw) and field == "steps":
            body = raw.strip()
            body = re.sub(r"^\d+\.\s+", "", body)
            sm = re.match(r"\*\*(.+?)\*\*\s*—\s*(.+)$", body)
            if sm:
                title, detail = sm.group(1), sm.group(2)
            elif " — " in body:
                title, detail = body.split(" — ", 1)
            else:
                seg = re.split(r"[；;：:，（(]", body, maxsplit=1)[0]
                title, detail = seg.strip(), body
            cur["steps"].append({
                "title": title.strip(),
                "detail": detail.strip(),
                "level": guess_level(title, detail),
            })
    if cur:
        problems.append(cur)
    return problems


def guess_level(title, detail):
    advanced_kw = ("ffmpeg", "ffprobe", "JSON", "注册表", "VPS", "nginx", "SRS",
                   "计划任务", "DDU", "BIOS", "msconfig", "caffeinate", "Spout",
                   "virtualcam-install", "wrapOBS", "flatpak")
    if any(k in detail for k in advanced_kw) or "进阶" in title:
        return "进阶"
    return "基础"


def main():
    new_items = parse_md(MD_PATH)
    print(f"parsed from MD: {len(new_items)}")
    if len(new_items) < 60:
        sys.exit(f"FATAL: expected >=60 entries, got {len(new_items)}")

    ids = [p["id"] for p in new_items]
    dup = {x for x in ids if ids.count(x) > 1}
    if dup:
        sys.exit(f"FATAL: duplicate ids in MD: {dup}")

    with open(JSON_PATH, encoding="utf-8") as f:
        data = json.load(f)
    existing = {p["id"] for p in data["problems"]}
    clash = existing & set(ids)
    if clash:
        sys.exit(f"FATAL: id conflict with existing library: {clash}")

    # 字段完整性检查
    for p in new_items:
        for k in ("id", "title", "category", "severity"):
            assert p[k], f"missing {k}: {p['id']}"
        assert p["platforms"], f"missing platforms: {p['id']}"
        assert len(p["symptoms"]) >= 2, f"too few symptoms: {p['id']}"
        assert len(p["causes"]) >= 1, f"too few causes: {p['id']}"
        assert len(p["steps"]) >= 3, f"too few steps: {p['id']}"
        for s in p["steps"]:
            assert s["title"] and s["detail"], f"incomplete step: {p['id']}"

    # related 引用校验（现有 + 新增）
    all_ids = existing | set(ids)
    missing_refs = {}
    for p in new_items:
        bad = [r for r in p["related"] if r not in all_ids]
        if bad:
            missing_refs[p["id"]] = bad
    if missing_refs:
        print("WARN: unknown related refs (will be dropped):")
        for pid, bad in missing_refs.items():
            print(f"  {pid}: {bad}")
        for p in new_items:
            p["related"] = [r for r in p["related"] if r in all_ids]

    added = 0
    for p in new_items:
        if p["id"] in existing:
            continue
        data["problems"].append({
            "id": p["id"],
            "category": p["category"],
            "title": p["title"],
            "platforms": p["platforms"],
            "severity": p["severity"],
            "symptoms": p["symptoms"],
            "causes": p["causes"],
            "steps": [{"title": s["title"], "detail": s["detail"], "level": s["level"]} for s in p["steps"]],
            "tips": p["tips"],
            "related": p["related"],
            "links": p["links"],
        })
        added += 1

    data["updated"] = "2026-08-25"
    data["version"] = "2.1"
    note_add = ("；2026-08-25 增补（v1.8 网络调研批量扩充）：录制重音（监听回环/双采）、音频独占模式、"
                "多轨录制、虚拟摄像头会议软件接入、全局热键冲突、UWP/浏览器/PPT 捕获、G-Sync 闪烁、"
                "全屏优化、AMF/QSV 编码、码率速查、Stinger 转场、嵌套场景性能、Twitch 增强广播、SRT 输出、"
                "多实例/便携模式、NDI 与手机作摄像头、章节标记、回放缓存热键、macOS 休眠与虚拟相机权限、"
                "Linux Flatpak 插件路径、WebSocket 远程控制等 63 条")
    data["note"] = (data.get("note") or "") + note_add

    with open(JSON_PATH, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

    print(f"added: {added}, total now: {len(data['problems'])}")
    print("OK")


if __name__ == "__main__":
    main()
