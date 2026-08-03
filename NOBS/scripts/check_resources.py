"""
XAML 资源引用体检。

背景：{StaticResource X} / {DynamicResource X} 里的 X 拼错，编译期一声不吭，
要等用户导航到那个页面才抛 ResourceReferenceKeyNotFoundException。
本项目有 11 个页面、上百处资源引用，靠手点一遍不现实，所以做成脚本。

规则：
  * 全局键来自 Themes/Palette.xaml + Themes/Controls.xaml 的 x:Key
  * 页面内 <UserControl.Resources> / <Window.Resources> 等局部 x:Key 也算已定义
  * WPF 内置键（SystemColors.* / 各类 ComponentResourceKey）跳过

用法：python scripts/check_resources.py
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "OBS_Helper.Wpf"

KEY_RE = re.compile(r'x:Key="([^"]+)"')
REF_RE = re.compile(r"\{(?:Static|Dynamic)Resource\s+([^\}\s,]+)\s*\}")

# WPF 自带的资源键，不在我们的字典里也能解析出来
BUILTIN_PREFIXES = ("System", "{x:Static", "ToolBar.", "Menu", "GridView")


def collect_keys(path: Path) -> set[str]:
    return set(KEY_RE.findall(path.read_text(encoding="utf-8-sig")))


def main() -> int:
    palette = ROOT / "Themes" / "Palette.xaml"
    controls = ROOT / "Themes" / "Controls.xaml"
    for f in (palette, controls):
        if not f.exists():
            print(f"找不到主题文件：{f}")
            return 2

    global_keys = collect_keys(palette) | collect_keys(controls)
    print(f"全局资源键 {len(global_keys)} 个（Palette + Controls）")

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

    print("全部资源引用均可解析。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
