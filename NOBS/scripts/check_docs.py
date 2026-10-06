#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""check_docs.py —— 文档与代码事实一致性巡检（仅标准库，零依赖）。

为什么要有这个脚本
==================
本仓反复出现的「文档与代码事实漂移」几乎全是**数字**：服务数、路由数、自检项数、
文案键数、知识库条数、版本号。这类漂移不会让程序崩，却会让判断失真 ——
同一个事实在三份「已同步」的文档里可以有三个不同的答案。

数字类的漂移靠人眼对读是守不住的（改一处代码要同时想起四处文档），所以这里改成
**从源码实测、与文档里写的数字比对、不一致就非零退出**。可以直接接进 CI。

它检查什么
==========
| 实测来源 | 文档锚点 |
|---|---|
| `OBS_Helper.Wpf/AppServices.cs` 的 `private static readonly Lazy<` 个数 | `docs/ARCHITECTURE.md`、`docs/CODEBASE.md` |
| `OBS_Helper.Wpf/Navigation/Routes.cs` 的路由常量个数 | `docs/CODEBASE.md` |
| `OBS_Helper.Wpf/MainWindow.xaml.cs` 自检 `cases` 数组长度 | `README.md`、`docs/ARCHITECTURE.md` |
| 自检总项数（`cases` + 报告行里声明的非路由检查项） | `README.md`、`docs/ARCHITECTURE.md` |
| `OBS_Helper.Wpf/Assets/problems.json` 的 `problems` 数组条数 | `README.md` |
| `Localization/StringTableZhHans.cs` 与 `StringTableEnUs.cs` 的键数 / 键集 | `docs/CODEBASE.md`、`README.md` |
| `OBS_Helper.Wpf.csproj` 与 `OBS_Helper_Setup.iss` 的版本号 | 两个文件互校 |

另外两条**不依赖文档**的完整性检查：
* `Routes.cs` 的每条路由都在自检 `cases` 里出现过（否则新增路由不会被自检覆盖）；
* `problems.json` 的 JSON 解析结果与「只在 `problems` 数组内数 `"id"`」的原始文本计数一致
  （分类里也有 `id`，所以不能直接全文数）。

用法
====
    python scripts/check_docs.py            # 在 NOBS/ 或仓库根目录下都能跑
    python scripts/check_docs.py --strict-all
    python scripts/check_docs.py -v

退出码：0 = 全部一致；1 = 存在不一致（差异会逐条打印「文档位置 + 文档值 + 实测值」）。

关于 `--strict-all`
------------------
默认只对**文档写手可维护的那几份文件**（`NOBS/docs/*`、`NOBS/README.md`）判失败。
仓库根的 `README.md` / `README.en.md` 属于另一条维护线，脚本只给 `[WARN]` 提示、不影响退出码；
接进 CI 时建议加 `--strict-all` 把它们也变成硬失败。

**刻意不检查「测试项数」**
------------------------
那个数字是**每个发布版本的基线**（README 说的是「V2.9.6 发布时 630 项」），
不是「当前源码里有多少个测试用例」。开发期每加一个测试它就变，
拿它当硬失败会在正常开发里天天误报。真要核对，就按发布说明里那一版的口径核，
或者把它写成量级（「600+ 项」）。
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path

# --------------------------------------------------------------------------- #
# 路径定位：脚本在 NOBS/scripts/ 下，从「脚本所在目录」与「当前目录」两个方向
# 向上找含 OBS_Helper.Wpf/AppServices.cs 且含 docs/ 的目录，因此
# 在 F:\OBS（仓库根）与 F:\OBS\NOBS（产品源码根）下都能直接跑。
# --------------------------------------------------------------------------- #

def find_nobs_root() -> Path:
    seeds: list[Path] = []
    here = Path(__file__).resolve().parent
    for base in (here, Path.cwd().resolve()):
        seeds.extend([base, *base.parents])
    seen: set[Path] = set()
    ordered: list[Path] = []
    for p in seeds:
        if p not in seen:
            seen.add(p)
            ordered.append(p)

    for base in ordered:
        if (base / "OBS_Helper.Wpf" / "AppServices.cs").is_file() and (base / "docs").is_dir():
            return base
    for base in ordered:
        cand = base / "NOBS"
        if (cand / "OBS_Helper.Wpf" / "AppServices.cs").is_file() and (cand / "docs").is_dir():
            return cand
    raise SystemExit(
        "找不到 NOBS 源码根（需要同时存在 OBS_Helper.Wpf/AppServices.cs 与 docs/）。\n"
        "请从仓库根或 NOBS/ 目录下运行：python scripts/check_docs.py"
    )


NOBS = find_nobs_root()
REPO = NOBS.parent
SRC = NOBS / "OBS_Helper.Wpf"

# --------------------------------------------------------------------------- #
# 结果收集
# --------------------------------------------------------------------------- #

class Report:
    def __init__(self, strict_all: bool, verbose: bool) -> None:
        self.strict_all = strict_all
        self.verbose = verbose
        self.failures: list[str] = []
        self.warnings: list[str] = []
        self.ok_count = 0
        self.checked_docs: set[str] = set()

    def ok(self, label: str, detail: str) -> None:
        self.ok_count += 1
        if self.verbose:
            print(f"  [ OK ] {label}  {detail}")

    def fail(self, label: str, detail: str) -> None:
        self.failures.append(f"{label}\n{detail}")

    def warn(self, label: str, detail: str) -> None:
        self.warnings.append(f"{label}\n{detail}")

    def problem(self, label: str, detail: str, advisory: bool) -> None:
        """advisory 且未开 --strict-all 时只提示，否则判失败。"""
        if advisory and not self.strict_all:
            self.warn(label, detail)
        else:
            self.fail(label, detail)


REPORT: Report


# --------------------------------------------------------------------------- #
# 读文件 / 定位数字
# --------------------------------------------------------------------------- #

def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def rel(path: Path) -> str:
    """相对仓库根的展示路径（越短越好认）。"""
    try:
        return path.relative_to(REPO).as_posix()
    except ValueError:
        return path.as_posix()


def strip_markdown(line: str) -> str:
    """去掉 ** 与反引号，让 `**45 个**服务` 这类写法也能被普通正则命中。"""
    return line.replace("*", "").replace("`", "")


def scan_doc_strings(doc: Path, pattern: str) -> list[tuple[int, str, str]]:
    """同 scan_doc_numbers，但捕获组按字符串返回（用于 2.9.6 这类带点的版本号）。"""
    if not doc.is_file():
        return []
    rx = re.compile(pattern)
    found: list[tuple[int, str, str]] = []
    for lineno, raw in enumerate(read_text(doc).splitlines(), 1):
        m = rx.search(strip_markdown(raw))
        if m:
            found.append((lineno, m.group(1), raw.strip()))
    return found


def scan_doc_numbers(doc: Path, pattern: str) -> list[tuple[int, int, str]]:
    """在文档里按 pattern 找数字，返回 [(行号, 文档里的数字, 原始行)]。

    pattern 必须含且仅含一个捕获组，捕获的就是那个数字。
    """
    return [(n, int(v), raw) for n, v, raw in scan_doc_strings(doc, pattern)]


def expect_doc_numbers(
    *,
    label: str,
    doc: Path,
    pattern: str,
    actual: int,
    source: str,
    required: bool = True,
    advisory: bool = False,
    note: str = "",
) -> None:
    """把文档里所有命中 pattern 的数字都要求等于 actual。"""
    hits = scan_doc_numbers(doc, pattern)
    doc_rel = rel(doc)

    if not hits:
        if not required:
            return
        detail = (
            f"        文档: {doc_rel}   文档值: （未找到可核对的数字）   实测值: {actual}\n"
            f"        来源: {source}\n"
            f"        说明: 文档里没有匹配 {pattern!r} 的表述，无法核对；"
            f"若是有意删除，请同步更新 check_docs.py"
        )
        REPORT.problem(f"缺失锚点 · {label}", detail, advisory)
        return

    REPORT.checked_docs.add(doc_rel)
    for lineno, value, raw in hits:
        if value == actual:
            REPORT.ok(label, f"{doc_rel}:{lineno} = {value} ✓")
            continue
        detail = (
            f"        文档: {doc_rel}:{lineno}   文档值: {value}   实测值: {actual}\n"
            f"        来源: {source}\n"
            f"        原文: {raw[:160]}"
            + (f"\n        备注: {note}" if note else "")
        )
        REPORT.problem(f"{label}（{doc_rel}:{lineno}）", detail, advisory)


# --------------------------------------------------------------------------- #
# 实测：AppServices 的 Lazy 单例数
# --------------------------------------------------------------------------- #

def count_lazy_singletons() -> int:
    text = read_text(SRC / "AppServices.cs")
    return len(re.findall(r"private\s+static\s+readonly\s+Lazy<", text))


# --------------------------------------------------------------------------- #
# 实测：路由常量 / 自检 cases / 自检总项数
# --------------------------------------------------------------------------- #

def read_routes() -> list[str]:
    text = read_text(SRC / "Navigation" / "Routes.cs")
    return re.findall(r"public\s+const\s+string\s+(\w+)\s*=", text)


def read_selftest_cases() -> list[str]:
    """取 MainWindow.xaml.cs 里 `var cases = new (string Route, object? Param)[] { ... };`
    这一段，返回其中出现的路由常量名（含重复，因为 plugins 会带参跑两次）。"""
    text = read_text(SRC / "MainWindow.xaml.cs")
    start = text.find("var cases = new (string Route, object? Param)[]")
    if start < 0:
        raise SystemExit("MainWindow.xaml.cs 里找不到自检 `cases` 数组 —— "
                         "自检结构改了，请同步更新 check_docs.py")
    brace = text.find("{", start)
    depth = 0
    end = -1
    for i in range(brace, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                end = i
                break
    if end < 0:
        raise SystemExit("自检 `cases` 数组的大括号不闭合 —— 请检查 MainWindow.xaml.cs")
    return re.findall(r"\(\s*Routes\.(\w+)\s*,", text[brace:end])


def read_selftest_non_route_count() -> int:
    """自检报告行自己声明了「路由 N 项 + 新手引导 + 迷你小窗 + 文案资源 + 语言切换」，
    这里数出加号个数 = 非路由检查项数。"""
    text = read_text(SRC / "MainWindow.xaml.cs")
    m = re.search(r"路由\s*\{cases\.Length\}\s*项([^）)]*)[）)]", text)
    if not m:
        raise SystemExit("MainWindow.xaml.cs 里找不到自检报告行的「路由 {cases.Length} 项 ...」片段 —— "
                         "报告格式改了，请同步更新 check_docs.py")
    return m.group(1).count("+")


# --------------------------------------------------------------------------- #
# 实测：knowledge base 条数
# --------------------------------------------------------------------------- #

def _slice_json_array(text: str, key: str) -> str | None:
    """取出 `"key": [ ... ]` 的数组原文（按括号配对，跳过字符串内的括号与转义）。

    这样数的 `"id"` 只可能落在该数组内 —— 分类数组里也有 `id`，全文计数会多算。
    """
    m = re.search(r'"%s"\s*:\s*\[' % re.escape(key), text)
    if not m:
        return None
    i = text.index("[", m.start())
    depth = 0
    in_string = False
    escaped = False
    for j in range(i, len(text)):
        ch = text[j]
        if in_string:
            if escaped:
                escaped = False
            elif ch == "\\":
                escaped = True
            elif ch == '"':
                in_string = False
            continue
        if ch == '"':
            in_string = True
        elif ch == "[":
            depth += 1
        elif ch == "]":
            depth -= 1
            if depth == 0:
                return text[i:j + 1]
    return None


def count_problems() -> tuple[int, int]:
    """返回 (JSON 解析出的条数, 只在 problems 数组内数 "id" 的条数)。"""
    path = SRC / "Assets" / "problems.json"
    text = read_text(path)
    data = json.loads(text)
    parsed = len(data.get("problems") or [])
    raw_slice = _slice_json_array(text, "problems")
    if raw_slice is None:
        raise SystemExit("problems.json 里找不到 `problems` 数组 —— 数据形状改了？")
    raw = len(re.findall(r'"id"\s*:', raw_slice))
    return parsed, raw


# --------------------------------------------------------------------------- #
# 实测：文案表键
# --------------------------------------------------------------------------- #

KEY_RX = re.compile(r'\[\s*"((?:[^"\\]|\\.)*)"\s*\]\s*=')


def read_string_table(name: str) -> tuple[list[str], str]:
    path = SRC / "Localization" / name
    keys = KEY_RX.findall(read_text(path))
    return keys, rel(path)


# --------------------------------------------------------------------------- #
# 实测：CODEBASE 是否覆盖了全部源码文件
# --------------------------------------------------------------------------- #

def find_unlisted_sources(codebase: Path) -> list[str]:
    """返回 CODEBASE.md 里没有出现的源码 / 资源文件（相对 OBS_Helper.Wpf）。

    收录判定放宽到「读者能找到」的程度，避免把规范写法误报成缺失：
    * `Views/HomePage.xaml(.cs)` 这种合并写法覆盖同名的 `.xaml` 与 `.xaml.cs`；
    * `Assets/problems.en-US.json` 视为被 `Assets/problems.json` 那条覆盖。
    """
    if not codebase.is_file():
        return []
    doc = read_text(codebase).replace(" ", "")
    found: list[str] = []
    for dirpath, dirnames, filenames in os.walk(SRC):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", ".vs")]
        for name in filenames:
            full = Path(dirpath) / name
            rel_path = full.relative_to(SRC).as_posix()
            if rel_path in doc:
                continue
            if rel_path.endswith(".xaml.cs") and (rel_path[:-3] + "(.cs)") in doc:
                continue
            if rel_path.endswith(".xaml") and (rel_path + "(.cs)") in doc:
                continue
            if ".en-US." in rel_path and rel_path.replace(".en-US", "") in doc:
                continue
            found.append(rel_path)
    return sorted(found)


# --------------------------------------------------------------------------- #
# 实测：版本号
# --------------------------------------------------------------------------- #

def read_versions() -> dict[str, str]:
    csproj = read_text(SRC / "OBS_Helper.Wpf.csproj")
    iss = read_text(SRC / "OBS_Helper_Setup.iss")

    def one(pattern: str, text: str, what: str) -> str:
        m = re.search(pattern, text)
        if not m:
            raise SystemExit(f"读不到 {what} —— 项目文件结构改了，请同步更新 check_docs.py")
        return m.group(1)

    return {
        "csproj.Version": one(r"<Version>\s*([^<\s]+)\s*</Version>", csproj, "csproj 的 <Version>"),
        "csproj.FileVersion": one(r"<FileVersion>\s*([^<\s]+)\s*</FileVersion>", csproj, "csproj 的 <FileVersion>"),
        "csproj.AssemblyVersion": one(r"<AssemblyVersion>\s*([^<\s]+)\s*</AssemblyVersion>", csproj, "csproj 的 <AssemblyVersion>"),
        "iss.MyAppVersion": one(r'#define\s+MyAppVersion\s+"([^"]+)"', iss, "iss 的 MyAppVersion"),
        "iss.path": rel(SRC / "OBS_Helper_Setup.iss"),
        "csproj.path": rel(SRC / "OBS_Helper.Wpf.csproj"),
    }


# --------------------------------------------------------------------------- #
# 主流程
# --------------------------------------------------------------------------- #

def main() -> int:
    global REPORT

    ap = argparse.ArgumentParser(
        description="文档与代码事实一致性巡检（数字类漂移）",
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--strict-all", action="store_true",
                    help="把仓库根 README.md / README.en.md 也变成硬失败（默认只提示）")
    ap.add_argument("-v", "--verbose", action="store_true", help="逐条打印通过的检查")
    args = ap.parse_args()

    REPORT = Report(strict_all=args.strict_all, verbose=args.verbose)

    arch = NOBS / "docs" / "ARCHITECTURE.md"
    codebase = NOBS / "docs" / "CODEBASE.md"
    nobs_readme = NOBS / "README.md"
    root_readme = REPO / "README.md"
    root_readme_en = REPO / "README.en.md"

    print(f"仓库根: {REPO}")
    print(f"源码根: {NOBS}")
    print("-" * 78)

    # ---------------------------------------------------------------- 1. 服务数
    lazy = count_lazy_singletons()
    lazy_src = f"{rel(SRC / 'AppServices.cs')} 的 `private static readonly Lazy<` 计数"
    print(f"[1/7] AppServices Lazy 单例：实测 {lazy}")
    expect_doc_numbers(label="AppServices 的 Lazy 单例个数", doc=arch,
                       pattern=r"(\d+)\s*个\s*Lazy\s*单例",
                       actual=lazy, source=lazy_src)
    expect_doc_numbers(label="AppServices 的服务个数", doc=codebase,
                       pattern=r"(\d+)\s*个\s*服务的手工\s*Lazy\s*单例装配",
                       actual=lazy, source=lazy_src)

    # ------------------------------------------------- 2. 路由常量 / 自检 cases
    routes = read_routes()
    cases = read_selftest_cases()
    non_route = read_selftest_non_route_count()
    total = len(cases) + non_route
    cases_src = f"{rel(SRC / 'MainWindow.xaml.cs')} 自检 `cases` 数组里 (Routes.X, ...) 的条数"
    routes_src = f"{rel(SRC / 'Navigation' / 'Routes.cs')} 的 `public const string` 计数"
    total_src = (f"{rel(SRC / 'MainWindow.xaml.cs')} 自检报告行 = 路由 {len(cases)} 项 "
                 f"+ {non_route} 项非路由检查（{total} 项）")

    print(f"[2/7] 路由常量 {len(routes)} · 自检 cases {len(cases)} · 非路由检查 {non_route} · 自检总项数 {total}")

    expect_doc_numbers(label="路由常量个数", doc=codebase,
                       pattern=r"(\d+)\s*条\s*路由[；;]",
                       actual=len(routes), source=routes_src)
    expect_doc_numbers(label="自检路由用例数", doc=nobs_readme,
                       pattern=r"[（(](\d+)\s*条路由\s*\+",
                       actual=len(cases), source=cases_src)
    expect_doc_numbers(label="自检路由用例数", doc=arch,
                       pattern=r"(\d+)\s*条路由用例",
                       actual=len(cases), source=cases_src)
    expect_doc_numbers(label="自检总项数", doc=arch,
                       pattern=r"(\d+)\s*项\s*自检",
                       actual=total, source=total_src)
    expect_doc_numbers(label="自检总项数", doc=nobs_readme,
                       pattern=r"Headless\s*自检\s*(\d+)\s*项",
                       actual=total, source=total_src)
    # 仓库根 README 也写着自检项数，但它属于另一条维护线：默认只提示。
    expect_doc_numbers(label="自检总项数", doc=root_readme,
                       pattern=r"Headless\s*自检\s*(\d+)\s*项",
                       actual=total, source=total_src, required=False, advisory=True)

    # 完整性（无文档锚点）：每条路由都得被自检覆盖
    uncovered = sorted(set(routes) - set(cases))
    if uncovered:
        REPORT.fail("自检覆盖：有路由没被 cases 覆盖",
                    f"        路线: {', '.join(uncovered)}\n"
                    f"        来源: {routes_src} vs {cases_src}\n"
                    f"        说明: 新增路由必须在 MainWindow.xaml.cs 的 cases 里补一条，"
                    f"否则 `OBS_SELFTEST=1` 不会走到它")
    else:
        REPORT.ok("自检覆盖 ✓", f"{len(routes)} 条路由全部出现在 cases 里")
    duplicated = sorted({r for r in cases if cases.count(r) > 1})
    if not duplicated:
        REPORT.warn("自检 cases 无重复路由",
                    "        说明: 目前每条路由只跑一次；plugins 带参跑两次的写法若被删掉，"
                    "总项数会变，请确认是有意的")

    # ------------------------------------------------------ 3. 知识库条数
    parsed, raw = count_problems()
    problems_src = (f"{rel(SRC / 'Assets' / 'problems.json')} 的 `problems` 数组"
                    f"（JSON 解析 {parsed} 条 / 数组内 `\"id\"` 原始计数 {raw} 条）")
    print(f"[3/7] 知识库条数：problems 数组 {parsed} 条（原始文本计数 {raw}）")

    if parsed != raw:
        REPORT.fail("problems.json 计数不自洽",
                    f"        文档: （无，两项实测互校）   文档值: JSON 解析 {parsed}   "
                    f"实测值: 数组内 `\"id\"` 计数 {raw}\n"
                    f"        来源: {rel(SRC / 'Assets' / 'problems.json')}\n"
                    f"        说明: 数组里可能有条目缺 `id` 或多了重复的 `id` 字段")
    else:
        REPORT.ok("problems.json 计数自洽 ✓", f"{parsed} == {raw}")

    expect_doc_numbers(label="知识库条数", doc=nobs_readme,
                       pattern=r"知识库\s*(\d+)\s*条",
                       actual=parsed, source=problems_src)
    # 仓库根 README 的三种写法
    for pat in (r"知识库\s*(\d+)\s*条", r"(\d+)\s*条问题的知识库", r"内置\s*(\d+)\s*条问题"):
        expect_doc_numbers(label="知识库条数", doc=root_readme, pattern=pat,
                           actual=parsed, source=problems_src, required=False, advisory=True)

    # ------------------------------------------------------ 4. 文案表键
    zh_keys, zh_rel = read_string_table("StringTableZhHans.cs")
    en_keys, en_rel = read_string_table("StringTableEnUs.cs")
    zh_set, en_set = set(zh_keys), set(en_keys)
    union = len(zh_set | en_set)
    keys_src = f"{zh_rel} {len(zh_keys)} 键 / {en_rel} {len(en_keys)} 键（键集并集 {union}）"
    print(f"[4/7] 文案键：中表 {len(zh_keys)} · 英表 {len(en_keys)} · 键集并集 {union}")

    if zh_set != en_set:
        only_zh = sorted(zh_set - en_set)[:10]
        only_en = sorted(en_set - zh_set)[:10]
        REPORT.fail("中英文案表键集不一致",
                    f"        文档: {zh_rel} / {en_rel}   文档值: 键集应完全相同   "
                    f"实测值: 只在中文 {len(zh_set - en_set)} 键 / 只在英文 {len(en_set - zh_set)} 键\n"
                    f"        来源: {keys_src}\n"
                    f"        只在中文表: {only_zh}\n"
                    f"        只在英文表: {only_en}")
    else:
        REPORT.ok("中英文案表键集一致 ✓", f"{union} 键")

    if len(zh_keys) != len(zh_set):
        REPORT.fail("中文文案表有重复键",
                    f"        文档: {zh_rel}   文档值: 键数 = 唯一键数   "
                    f"实测值: 赋值 {len(zh_keys)} 次 / 唯一键 {len(zh_set)} 个\n"
                    f"        来源: {keys_src}")
    else:
        REPORT.ok("中文文案表无重复键 ✓", f"{len(zh_keys)} 键")

    if len(en_keys) != len(en_set):
        REPORT.fail("英文文案表有重复键",
                    f"        文档: {en_rel}   文档值: 键数 = 唯一键数   "
                    f"实测值: 赋值 {len(en_keys)} 次 / 唯一键 {len(en_set)} 个\n"
                    f"        来源: {keys_src}")
    else:
        REPORT.ok("英文文案表无重复键 ✓", f"{len(en_keys)} 键")

    expect_doc_numbers(label="中文文案表键数", doc=codebase,
                       pattern=r"(\d+)\s*条键",
                       actual=len(zh_keys),
                       source=f"{zh_rel} / {en_rel}（两张表的 `[\"key\"] =` 计数，键集已校验一致）")
    expect_doc_numbers(label="文案表键数（当前）", doc=nobs_readme,
                       pattern=r"截至\s*V?\d+\.\d+\.\d+\s*为\s*(\d+)\s*条",
                       actual=len(zh_keys),
                       source=f"{zh_rel} / {en_rel}")
    expect_doc_numbers(label="文案表键数", doc=root_readme,
                       pattern=r"共\s*(\d+)\s*条文案键",
                       actual=len(zh_keys), source=f"{zh_rel} / {en_rel}",
                       required=False, advisory=True)

    # ------------------------------------------------------ 5. 版本号
    v = read_versions()
    print(f"[5/7] 版本号：csproj {v['csproj.Version']} · iss {v['iss.MyAppVersion']}")

    if v["csproj.Version"] != v["iss.MyAppVersion"]:
        REPORT.fail("csproj 与 .iss 的版本号不一致",
                    f"        文档: {v['csproj.path']} 与 {v['iss.path']}\n"
                    f"        文档值: csproj <Version> = {v['csproj.Version']}   "
                    f"实测值: .iss MyAppVersion = {v['iss.MyAppVersion']}\n"
                    f"        说明: 两者必须一致（README 里也把这条当单测钉着）")
    else:
        REPORT.ok("csproj / .iss 版本一致 ✓", v["csproj.Version"])

    for fv in ("csproj.FileVersion", "csproj.AssemblyVersion"):
        if not v[fv].startswith(v["csproj.Version"]):
            REPORT.fail(f"{fv} 前缀与 <Version> 不一致",
                        f"        文档: {v['csproj.path']}\n"
                        f"        文档值: <Version> = {v['csproj.Version']}   "
                        f"实测值: {fv.split('.')[-1]} = {v[fv]}\n"
                        f"        说明: FileVersion / AssemblyVersion 应以 <Version> 开头")
        else:
            REPORT.ok(f"{fv} 前缀一致 ✓", v[fv])

    # README 的「当前版本」：数字带点，不能走 expect_doc_numbers（它按整数比），单独比一次
    if nobs_readme.is_file():
        for lineno, shown, raw in scan_doc_strings(nobs_readme, r"当前版本[：:]\s*V(\d+\.\d+\.\d+)"):
            if shown == v["csproj.Version"]:
                REPORT.ok("README 当前版本 ✓", f"{rel(nobs_readme)}:{lineno} = {shown}")
            else:
                REPORT.problem(
                    f"README 当前版本（{rel(nobs_readme)}:{lineno}）",
                    f"        文档: {rel(nobs_readme)}:{lineno}   文档值: V{shown}   "
                    f"实测值: csproj <Version> = {v['csproj.Version']}\n"
                    f"        来源: {v['csproj.path']}\n"
                    f"        原文: {raw[:160]}",
                    advisory=True)

    # ------------------------------------------- 6/7. 顺带的文档存在性检查
    print("[6/7] 关键文档存在性")
    for doc in (arch, codebase, nobs_readme):
        if doc.is_file() and doc.stat().st_size > 0:
            REPORT.ok("文档存在且非空 ✓", rel(doc))
        else:
            REPORT.fail("关键文档缺失或为空",
                        f"        文档: {rel(doc)}   文档值: 应存在且非空   实测值: "
                        f"{'不存在' if not doc.is_file() else '0 字节'}")

    # docs/QA-REPORT.md 曾经是一个 0 字节空文件（V2.9.6 复核发现并补成索引）
    qa = NOBS / "docs" / "QA-REPORT.md"
    if qa.is_file() and qa.stat().st_size == 0:
        REPORT.fail("docs/QA-REPORT.md 是空文件",
                    f"        文档: {rel(qa)}   文档值: 应含内容或删除   实测值: 0 字节")
    elif qa.is_file():
        REPORT.ok("docs/QA-REPORT.md ✓", f"{qa.stat().st_size} 字节")

    # ------------------------------------------- CODEBASE 是否覆盖了全部源码文件
    # 这是 C8 第 8 条（「号称完整代码清单，其实漏了 20+ 个已存在的文件」）的根因守卫。
    # 默认只提示：开发期新增文件很频繁，硬失败会在正常开发里天天见红；
    # 用 --strict-all（CI 收尾时）再把它变成硬失败。
    not_listed = find_unlisted_sources(codebase)
    if not_listed:
        REPORT.problem(
            "CODEBASE.md 未收录的源码文件",
            f"        文档: {rel(codebase)}   文档值: 0 个未收录   实测值: {len(not_listed)} 个\n"
            f"        来源: {rel(SRC)} 下全部 .cs / .xaml / .json 资产，"
            f"对照 CODEBASE 的文件路径（`X.xaml(.cs)` 与 `.en-US.*` 变体视为已收录）\n"
            f"        未收录: {', '.join(not_listed[:12])}"
            + ("…" if len(not_listed) > 12 else ""),
            advisory=True)
    else:
        REPORT.ok("CODEBASE 覆盖全部源码文件 ✓", "0 个未收录")

    print("[7/7] 汇总")
    print("-" * 78)
    for w in REPORT.warnings:
        print(f"[WARN] {w}")
    if REPORT.warnings:
        hint = "" if args.strict_all else "（加 --strict-all 可让这些也判失败）"
        print(f"提示: {len(REPORT.warnings)} 处非阻塞差异{hint}")
        print("-" * 78)

    if REPORT.failures:
        print(f"不一致 {len(REPORT.failures)} 处（通过 {REPORT.ok_count} 项）：")
        for f in REPORT.failures:
            print(f"[FAIL] {f}")
        print("-" * 78)
        print("结论: 文档与代码事实不一致 —— 请修正文档（数字以本脚本的实测值为准）。")
        return 1

    print(f"结论: 全部一致（通过 {REPORT.ok_count} 项，核对文档 {len(REPORT.checked_docs)} 份"
          f"{'，另有 ' + str(len(REPORT.warnings)) + ' 处提示' if REPORT.warnings else ''}）。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
