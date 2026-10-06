#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""无障碍静态检查（V3.0 / E4）。

为什么要有这个脚本：`ACCESSIBILITY.md` 的 §5 原先明确写着「本项目没有无障碍自动化测试」——
也就是说文档里的结论**不会随代码变化自动失效或被纠正**。这个脚本把其中能静态判定的两条钉住，
让「已经做到的」不会在后续改动里悄悄退化：

  R1 无文字内容的可交互控件必须有可访问名称
     （图标的 Button / ToggleButton / CheckBox / RadioButton，内容里既没有 TextBlock
       也没有字面文本时，必须显式写 AutomationProperties.Name；
       WPF 对纯图形内容不会自动生成名称，而 ToolTip **不能**当名称用）

  R2 实时区域必须是「声明 + 主动抛事件」两条都齐
     （只在样式里声明 AutomationProperties.LiveSetting 是不够的：WPF 不会自动播报，
       还需要代码里 RaiseAutomationEvent(LiveRegionChanged)）

  R3 可键盘操作的约定：带 Click 的 Button 不需要额外处理（WPF 原生可 Tab）。
     这里只检查“用鼠标事件当按钮使”的自绘元素必须 Focusable + 有键盘激活方式。

用法：python NOBS/scripts/check_accessibility.py [--verbose]
退出码：0 = 通过；1 = 有问题。
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "NOBS" / "OBS_Helper.Wpf"

# 这些控件类型只要「内容里没有文字」就必须给可访问名称
INTERACTIVE_TAGS = ["Button", "ToggleButton", "CheckBox", "RadioButton", "RepeatButton"]

TAG_RE = re.compile(
    r"<(?P<tag>" + "|".join(INTERACTIVE_TAGS) + r")\b(?P<attrs>[^>]*?)(?P<selfclose>/?)>",
    re.S,
)


def element_body(text: str, start: int, tag: str) -> str:
    """取元素的内容（到同名结束标签为止）；自闭合或找不到结束标签时返回空串。"""
    end = text.find(f"</{tag}>", start)
    if end < 0:
        return ""
    return text[start:end]


def has_text_content(body: str) -> bool:
    """内容里有没有文字（TextBlock / 字面文本）。"""
    if "<TextBlock" in body:
        return True
    # 去掉所有标签后还剩可见文字 → 有字面文本
    stripped = re.sub(r"<[^>]+>", "", body)
    stripped = re.sub(r"<!--.*?-->", "", stripped, flags=re.S)
    return bool(stripped.strip())


def check_accessible_names(verbose: bool):
    problems = []
    for path in sorted(APP.rglob("*.xaml")):
        if any(p in str(path) for p in ("\\obj\\", "\\bin\\")):
            continue
        text = path.read_text(encoding="utf-8")
        rel = path.relative_to(ROOT).as_posix()

        for m in TAG_RE.finditer(text):
            tag = m.group("tag")
            attrs = m.group("attrs")
            if m.group("selfclose"):
                continue

            body = element_body(text, m.end(), tag)
            name = ""
            name_m = re.search(r'AutomationProperties\.Name="([^"]*)"', attrs)
            if name_m:
                name = name_m.group(1)

            # 内容里有字面文本 / TextBlock 时，WPF 能从内容推出名称，不需要显式写
            if has_text_content(body):
                continue
            # 内容本身是「绑定的 Content 属性」时交由数据决定，静态判不了，跳过
            if re.search(r'Content="\{', attrs):
                continue

            if not name:
                line = text[: m.start()].count("\n") + 1
                problems.append(
                    f"{rel}:{line}  <{tag}> 内容里没有文字，也没有 AutomationProperties.Name"
                    f"（读屏用户只会听到「按钮」）"
                )
            elif verbose:
                print(f"  [R1 ok] {rel}:{line} {tag} → {name}")

    return problems


def check_live_region(verbose: bool):
    """R2：Toast 的实时区域必须「声明 + 抛事件」两条都齐。"""
    problems = []
    controls = APP / "Themes" / "Controls.xaml"
    toast = APP / "Services" / "ToastService.cs"

    declared = controls.exists() and "AutomationProperties.LiveSetting" in controls.read_text(encoding="utf-8")
    raised = toast.exists() and "RaiseAutomationEvent" in toast.read_text(encoding="utf-8")

    if declared and not raised:
        problems.append(
            "Themes/Controls.xaml 声明了 AutomationProperties.LiveSetting，"
            "但 Services/ToastService.cs 没有 RaiseAutomationEvent(LiveRegionChanged) —— "
            "WPF 不会仅凭声明就播报，读屏用户收不到 Toast"
        )
    elif raised and not declared:
        problems.append(
            "Services/ToastService.cs 抛了 LiveRegionChanged，但样式里没有声明 "
            "AutomationProperties.LiveSetting —— 实时区域语义不完整"
        )
    elif not declared and not raised:
        problems.append(
            "轻提示（Toast）既没有 LiveSetting 声明也没有抛 LiveRegionChanged 事件："
            "「已连接 OBS」「开录失败」这类唯一的结果反馈对读屏用户完全不可达"
        )
    elif verbose:
        print("  [R2 ok] Toast 实时区域：声明 + 主动抛事件 两条齐全")

    return problems


def check_custom_mouse_buttons(verbose: bool):
    """R3：把鼠标事件当按钮用的自绘元素必须可聚焦且有键盘激活方式。"""
    problems = []
    for path in sorted(APP.rglob("*.xaml")):
        if any(p in str(path) for p in ("\\obj\\", "\\bin\\")):
            continue
        text = path.read_text(encoding="utf-8")
        rel = path.relative_to(ROOT).as_posix()

        for m in re.finditer(r"<(?P<tag>Border|Grid|StackPanel|Image|Path)\b(?P<attrs>[^>]*?)>", text, re.S):
            attrs = m.group("attrs")
            # 只看「点击」语义：MouseLeftButtonUp / MouseDoubleClick。
            # MouseLeftButtonDown 常被用来拖窗口（例如迷你小窗的 RootBorder），
            # 那不是「按钮」，不该要求可聚焦 —— 静态检查把误报压到零才有人愿意跑它。
            if not re.search(r"Mouse(LeftButtonUp|DoubleClick)=", attrs):
                continue
            # Border/Grid 上没有 IsTabStop（那是 Control 的成员）—— 只能用附加属性 KeyboardNavigation.IsTabStop
            focusable = 'Focusable="True"' in attrs and (
                'IsTabStop="True"' in attrs or 'KeyboardNavigation.IsTabStop="True"' in attrs)
            has_key = "KeyDown=" in attrs or "KeyUp=" in attrs or "Command=" in attrs
            if not (focusable and has_key):
                line = text[: m.start()].count("\n") + 1
                problems.append(
                    f"{rel}:{line}  <{m.group('tag')}> 用鼠标事件当按钮，但"
                    f"{'不可聚焦' if not focusable else '没有键盘激活方式'}"
                    f"（键盘用户无法操作；要么改成 Button，要么补 Focusable + KeyDown）"
                )
            elif verbose:
                print(f"  [R3 ok] {rel}:{line} 可聚焦且支持键盘激活")

    return problems


def main():
    verbose = "--verbose" in sys.argv
    print("无障碍静态检查（V3.0 / E4）")
    print("-" * 60)

    problems = []
    problems += check_accessible_names(verbose)
    problems += check_live_region(verbose)
    problems += check_custom_mouse_buttons(verbose)

    if problems:
        print(f"发现 {len(problems)} 处问题：")
        for p in problems:
            print("  [FAIL] " + p)
        print("-" * 60)
        print("说明：本脚本只覆盖**能静态判定**的几条；屏幕阅读器实读、高对比配色、")
        print("      真机缩放等仍需人工验证，边界见 ACCESSIBILITY.md §5。")
        return 1

    print("全部通过：无文字内容的可交互控件都有可访问名称、实时区域声明与事件齐全、")
    print("            自绘鼠标按钮均具备键盘可操作性。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
