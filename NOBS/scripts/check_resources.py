"""
XAML 资源引用体检。

背景：{StaticResource X} / {DynamicResource X} 里的 X 拼错，编译期一声不吭，
要等用户导航到那个页面才抛 ResourceReferenceKeyNotFoundException。
本项目有 11 个页面、上百处资源引用，靠手点一遍不现实，所以做成脚本。

规则：
  * 全局键来自 Themes/ 下所有 .xaml（Palette + Controls + Icons 等）
  * 页面内 <UserControl.Resources> / <Window.Resources> 等局部 x:Key 也算已定义
  * WPF 内置键（SystemColors.* / 各类 ComponentResourceKey）跳过

用法：python scripts/check_resources.py
"""

import re
import sys
from pathlib import Path

# CI（GitHub Actions windows runner）默认 stdout 编码为 cp1252，
# 打印中文会抛 UnicodeEncodeError；统一强制 UTF-8 输出。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")

ROOT = Path(__file__).resolve().parent.parent / "OBS_Helper.Wpf"
THEMES = ROOT / "Themes"

KEY_RE = re.compile(r'x:Key="([^"]+)"')
REF_RE = re.compile(r"\{(?:Static|Dynamic)Resource\s+([^\}\s,]+)\s*\}")

# WPF 自带的资源键，不在我们的字典里也能解析出来
BUILTIN_PREFIXES = ("System", "{x:Static", "ToolBar.", "Menu", "GridView")

# problems.json 分类语义色键白名单（P2-1）：与 Palette.xaml 的 Semantic{key}Brush 一一对应
VALID_SEMANTIC = {
    "red", "orange", "yellow", "purple", "blue", "teal", "green", "azure", "violet", "crimson",
}


def collect_keys(path: Path) -> set[str]:
    return set(KEY_RE.findall(path.read_text(encoding="utf-8-sig")))


def main() -> int:
    if not THEMES.is_dir():
        print(f"找不到主题目录：{THEMES}")
        return 2

    theme_files = sorted(THEMES.glob("*.xaml"))
    if not theme_files:
        print(f"主题目录为空：{THEMES}")
        return 2

    global_keys: set[str] = set()
    for f in theme_files:
        global_keys |= collect_keys(f)
    print(f"全局资源键 {len(global_keys)} 个（Themes/ 下 {len(theme_files)} 个字典：{', '.join(p.name for p in theme_files)}）")

    xamls = sorted(p for p in ROOT.rglob("*.xaml") if "obj" not in p.parts and "bin" not in p.parts)

    problems: list[tuple[str, str]] = []
    total_refs = 0

    for xaml in xamls:
        text = xaml.read_text(encoding="utf-8-sig")
        local_keys = set(KEY_RE.findall(text))
        available = global_keys | local_keys

        for ref in REF_RE.findall(text):
            total_refs += 1
            if ref.startswith(BUILTIN_PREFIXES):
                continue
            if ref not in available:
                problems.append((str(xaml.relative_to(ROOT)), ref))

    print(f"扫描 {len(xamls)} 个 XAML，共 {total_refs} 处资源引用")

    if problems:
        print(f"\n发现 {len(problems)} 处未定义的资源键：")
        for f, key in problems:
            print(f"  {f}: {key}")
        return 1

    # ---- problems.json 分类语义色校验（P2-1）：semantic 必须在白名单内，且主题资源存在对应画刷
    problems_json = ROOT / "Assets" / "problems.json"
    if problems_json.exists():
        import json
        data = json.loads(problems_json.read_text(encoding="utf-8-sig"))
        bad = []
        for c in data.get("categories", []):
            sem = (c.get("semantic") or "").strip()
            if sem not in VALID_SEMANTIC:
                bad.append(f"{c.get('id')}: semantic={sem or '(缺失)'}")
            elif f"Semantic{sem.capitalize()}Brush" not in global_keys:
                bad.append(f"{c.get('id')}: 主题缺少 Semantic{sem.capitalize()}Brush")
        if bad:
            print(f"\nproblems.json 分类语义色校验失败（{len(bad)} 处）：")
            for line in bad:
                print("  " + line)
            return 1
        print(f"problems.json 分类语义色 {len(data.get('categories', []))} 个全部合法。")

    print("全部资源引用均可解析。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
