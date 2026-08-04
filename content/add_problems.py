# -*- coding: utf-8 -*-
"""把 content/extra_problems.json 里的新问题合并进两份 problems.json（幂等去重）。"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXTRA = os.path.join(ROOT, "content", "extra_problems.json")
TARGETS = [
    os.path.join(ROOT, "content", "problems.json"),
    os.path.join(ROOT, "OBS_Helper.Client", "wwwroot", "data", "problems.json"),
]
NEW_DATE = "2026-08-04"


def load(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def save(path, data):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")


def main():
    extra = load(EXTRA)
    print("extra_problems.json 条目数: %d" % len(extra))

    valid_cats = None
    for path in TARGETS:
        data = load(path)
        before = len(data["problems"])
        existing = {p["id"] for p in data["problems"]}

        if valid_cats is None:
            valid_cats = {c["id"] for c in data["categories"]}

        added, skipped = [], []
        for p in extra:
            if p["id"] in existing:
                skipped.append(p["id"])
                continue
            if p["category"] not in valid_cats:
                raise SystemExit("非法分类 %s (问题 %s)" % (p["category"], p["id"]))
            data["problems"].append(p)
            existing.add(p["id"])
            added.append(p["id"])

        data["updated"] = NEW_DATE
        save(path, data)

        rel = os.path.relpath(path, ROOT)
        print("%s: %d -> %d (新增 %d, 跳过重复 %d)"
              % (rel, before, len(data["problems"]), len(added), len(skipped)))
        if skipped:
            print("   跳过: %s" % ", ".join(skipped))


if __name__ == "__main__":
    main()
