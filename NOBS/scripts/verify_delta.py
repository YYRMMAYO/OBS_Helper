# -*- coding: utf-8 -*-
r"""增量更新端到端校验（每次发布增量包后运行一次）。

模拟「旧版本目录 + 应用增量包」的升级过程，再与最新 publish 目录逐一比对 SHA-256，
确认增量包能完整覆盖新旧差异（files 应用 + remove 删除）。

用法：
  python scripts/verify_delta.py \
      --old  PAKE\windows\OBS_Helper_Portable_<旧版>.zip \
      --delta PAKE\windows\OBS_Helper_Update_<新版>.zip \
      --publish OBS_Helper.Wpf\bin\Release\net10.0-windows\win-x64\publish
      [--new PAKE\windows\OBS_Helper_Portable_<新版>.zip]

关于参照系（V3.0 第四轮验证修正）：
  publish 目录**不是**发货产物 —— 便携包按设计排除了 `*.pdb` 与 `selftest_result.txt`。
  早先直接拿 publish 当基准，于是「旧包里的 pdb 被增量包正确删除」反而被判成
  「最新发布缺失 OBS_Helper.pdb」，校验永远 FAIL（真问题会被这条噪声淹没）。
  现在：优先用 `--new`（新版便携包 = 真正的发货产物）做基准；未提供时退回 publish，
  但按打包排除规则忽略那几个文件。
"""
import argparse
import hashlib
import json
import os
import shutil
import sys
import tempfile
import zipfile

# 打包时按设计排除的文件（build.ps1 的 robocopy /XF 与 Compress-Archive 过滤保持一致）。
# 它们不该出现在任何「发货产物」里，因此也不该参与「发货产物是否完整」的比对。
PACKAGING_EXCLUDED_SUFFIXES = (".pdb",)
PACKAGING_EXCLUDED_NAMES = ("selftest_result.txt",)


def is_packaging_excluded(rel: str) -> bool:
    name = os.path.basename(rel).lower()
    return name.endswith(PACKAGING_EXCLUDED_SUFFIXES) or name in PACKAGING_EXCLUDED_NAMES

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
    ap.add_argument("--publish", required=True, help="最新发布目录（未提供 --new 时的比对基准）")
    ap.add_argument("--new", default="", help="新版便携包 zip（真正的发货产物）；提供时用它做比对基准")
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

    # 比对基准：优先用新版便携包（= 发货产物），否则退回 publish 目录并忽略打包排除项。
    if args.new:
        reference_dir = os.path.join(root, "new")
        with zipfile.ZipFile(args.new) as z:
            z.extractall(reference_dir)
        print(f"比对基准：新版便携包 {os.path.basename(args.new)}（发货产物）")
    else:
        reference_dir = args.publish
        print("比对基准：publish 目录（未提供 --new；按打包排除规则忽略 *.pdb / selftest_result.txt）")

    mismatch, missing, checked, skipped = [], [], 0, 0
    for r, _, files in os.walk(old_dir):
        for f in files:
            rel = os.path.relpath(os.path.join(r, f), old_dir).replace(os.sep, "/")
            ref = os.path.join(reference_dir, rel)
            if not os.path.exists(ref):
                # 老包里多出来的文件（例如 2.9.6 里的 pdb）在新版发货产物里本就不该存在
                if not args.new and is_packaging_excluded(rel):
                    skipped += 1
                    continue
                continue
            if sha(os.path.join(old_dir, rel)) != sha(ref):
                mismatch.append(rel)
            checked += 1

    for r, _, files in os.walk(reference_dir):
        for f in files:
            rel = os.path.relpath(os.path.join(r, f), reference_dir).replace(os.sep, "/")
            if not args.new and is_packaging_excluded(rel):
                skipped += 1
                continue
            if not os.path.exists(os.path.join(old_dir, rel)):
                missing.append(rel)

    print(f"\n比对：共同文件 {checked} 个，不一致 {len(mismatch)}，最新发布缺失 {len(missing)}"
          + (f"，按打包排除规则忽略 {skipped} 个" if skipped else ""))
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
