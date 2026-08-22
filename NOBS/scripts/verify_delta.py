# -*- coding: utf-8 -*-
"""增量更新端到端校验（每次发布增量包后运行一次）。

模拟「旧版本目录 + 应用增量包」的升级过程，再与最新 publish 目录逐一比对 SHA-256，
确认增量包能完整覆盖新旧差异（files 应用 + remove 删除）。

用法：
  python scripts/verify_delta.py \
      --old  PAKE\windows\OBS_Helper_Portable_<旧版>.zip \
      --delta PAKE\windows\OBS_Helper_Update_<新版>.zip \
      --publish OBS_Helper.Wpf\bin\Release\net10.0-windows\win-x64\publish
"""
import argparse
import hashlib
import json
import os
import shutil
import sys
import tempfile
import zipfile

# Windows 控制台默认 GBK/cp936，打印 PASS ✅ 等字符会 UnicodeEncodeError，统一强制 UTF-8 输出
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")


def sha(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--old", required=True, help="旧版本便携包 zip 或旧版本目录")
    ap.add_argument("--delta", required=True, help="增量更新包 zip")
    ap.add_argument("--publish", required=True, help="最新发布目录（比对基准）")
    args = ap.parse_args()

    root = tempfile.mkdtemp(prefix="obs_verify_delta_")
    old_dir = os.path.join(root, "old")
    delta_dir = os.path.join(root, "delta")

    if args.old.lower().endswith(".zip"):
        with zipfile.ZipFile(args.old) as z:
            z.extractall(old_dir)
    else:
        shutil.copytree(args.old, old_dir)

    with zipfile.ZipFile(args.delta) as z:
        z.extractall(delta_dir)

    with open(os.path.join(delta_dir, "update_manifest.json"), encoding="utf-8") as f:
        m = json.load(f)

    print(f"清单: {m['baseVersion']} -> {m['targetVersion']}, "
          f"files={len(m['files'])}, remove={len(m.get('remove', []))}")

    for entry in m["files"]:
        src = os.path.join(delta_dir, "files", entry["path"].replace("/", os.sep))
        dst = os.path.join(old_dir, entry["path"].replace("/", os.sep))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)
        print(f"  应用 {entry['path']} ({entry['size']}B)")

    for rel in m.get("remove", []):
        dst = os.path.join(old_dir, rel.replace("/", os.sep))
        if os.path.exists(dst):
            os.remove(dst)
            print(f"  删除 {rel}")

    mismatch, missing, checked = [], [], 0
    for r, _, files in os.walk(old_dir):
        for f in files:
            rel = os.path.relpath(os.path.join(r, f), old_dir).replace(os.sep, "/")
            pub = os.path.join(args.publish, rel)
            if not os.path.exists(pub):
                continue
            if sha(os.path.join(old_dir, rel)) != sha(pub):
                mismatch.append(rel)
            checked += 1

    for r, _, files in os.walk(args.publish):
        for f in files:
            rel = os.path.relpath(os.path.join(r, f), args.publish).replace(os.sep, "/")
            if not os.path.exists(os.path.join(old_dir, rel)):
                missing.append(rel)

    print(f"\n比对：共同文件 {checked} 个，不一致 {len(mismatch)}，最新发布缺失 {len(missing)}")
    if mismatch:
        print("  不一致:", mismatch[:10])
    if missing:
        print("  缺失:", missing[:10])

    ok = not mismatch and not missing
    print("\n结论:", "PASS ✅ 增量包可完整升级" if ok else "FAIL ❌")
    shutil.rmtree(root, ignore_errors=True)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
