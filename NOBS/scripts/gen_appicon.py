#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""从仓库图标资产生成 Windows 应用图标（OBS_Helper.Wpf/Assets/appicon.ico）。

历史（V2.9.6 重写）：本脚本此前按 OBS 官方 logo 重绘「紫底三旋涡 + 齿轮角标」，
且依赖一张**本机路径**下的参考图（`G:\\DCIM\\...`）做蒙版 —— 换台机器跑不起来，
产出的图标也和仓库对外展示的那一枚不是同一个。V2.9.6 起改为：

  · 48/64/128/256 px 帧 —— 直接用 `assets/icon.png`
    （由 `assets/icon.svg` 渲染：深色圆角底 + 显示器 + 健康波形 + 录制指示点 + 体检对勾），
    并补回透明圆角（渲染出的 PNG 是 RGB，圆角外是白底，直接用会出现白方块）；
  · 16/24/32 px 帧 —— 小尺寸下原图的三峰波形与细描边会糊成一团，这里按同一套几何与
    配色**另画一版小尺寸专用图**：加粗描边、波形减到一峰、对勾徽章放大并夹在圆角底板内。
    这与 Windows 官方图标集的做法一致（同一枚标识，多尺寸分别绘制），不是换设计。

这样 README / 仓库头像 / 社交预览 / 应用窗口 / 托盘 / 安装包用的是**同一枚**标识。

只依赖 Pillow（含 numpy）。用法：
    python NOBS/scripts/gen_appicon.py                 # 写入 Assets/appicon.ico
    python NOBS/scripts/gen_appicon.py --preview p.png # 额外导出一张 256px 预览
    python NOBS/scripts/gen_appicon.py --sheet s.png   # 额外导出各尺寸对照图（目视校验用）
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]          # NOBS/
REPO = ROOT.parent                                  # 仓库根
SRC = REPO / "assets" / "icon.png"
DST = ROOT / "OBS_Helper.Wpf" / "Assets" / "appicon.ico"

SIZES = (16, 24, 32, 48, 64, 128, 256)
SMALL_SIZES = (16, 24, 32)
SS = 8  # 小尺寸重绘的超采样倍数

# 与 assets/icon.svg 一致的配色
BG_STOPS = ((0.0, (0x0B, 0x12, 0x20)), (0.55, (0x14, 0x20, 0x3A)), (1.0, (0x1E, 0x2F, 0x52)))
SCREEN = (0x4A, 0xA8, 0xF0)
SCREEN_FILL = (0x38, 0xBD, 0xF8)
RED = (0xEF, 0x44, 0x44)
GREEN = (0x10, 0xB9, 0x81)
DARK = (0x0B, 0x12, 0x20)


def rounded_mask(size: int, inset: float = 16.0, radius: float = 116.0) -> Image.Image:
    """按 SVG 比例（512 源图：8/256 内缩、58/256 圆角）生成圆角矩形蒙版。"""
    k = size / 512.0
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [inset * k, inset * k, size - 1 - inset * k, size - 1 - inset * k],
        radius=round(radius * k),
        fill=255,
    )
    return mask


def _linear_gradient(S: int, stops) -> Image.Image:
    """对角（左上→右下）线性渐变。"""
    y, x = np.mgrid[0:S, 0:S]
    t = (x + y) / (2.0 * (S - 1))
    arr = np.zeros((S, S, 3), dtype=np.float64)
    for i in range(len(stops) - 1):
        p0, c0 = stops[i]
        p1, c1 = stops[i + 1]
        sel = (t >= p0) & (t <= p1)
        if not sel.any():
            continue
        local = np.clip((t - p0) / max(p1 - p0, 1e-9), 0.0, 1.0)
        for ch in range(3):
            arr[..., ch] = np.where(sel, c0[ch] + (c1[ch] - c0[ch]) * local, arr[..., ch])
    return Image.fromarray(arr.astype(np.uint8), "RGB")


def _radial_alpha(S: int, cx: float, cy: float, r: float, peak: float) -> Image.Image:
    y, x = np.mgrid[0:S, 0:S]
    d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2) / r
    a = np.clip(1.0 - d, 0.0, 1.0) * peak * 255.0
    return Image.fromarray(a.astype(np.uint8), "L")


def render_small(size: int) -> Image.Image:
    """小尺寸专用重绘（同一构图，加粗 + 简化）。"""
    S = size * SS
    k = S / 256.0  # 设计稿坐标（256 视图）→ 本图坐标
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    plate = rounded_mask(S, inset=8 * k, radius=58 * k)
    img.paste(_linear_gradient(S, BG_STOPS), (0, 0), plate)
    glow = Image.new("RGBA", (S, S), SCREEN_FILL + (0,))
    glow.putalpha(_radial_alpha(S, 128 * k, 114 * k, 102 * k, 0.28))
    img = Image.alpha_composite(img, Image.composite(glow, Image.new("RGBA", (S, S), (0, 0, 0, 0)), plate))

    d = ImageDraw.Draw(img)

    # 显示器外框 / 内屏
    d.rounded_rectangle(
        [40 * k, 58 * k, 216 * k, 166 * k], radius=16 * k,
        outline=SCREEN + (255,), width=round(max(7 * k, 1.5 * SS)),
    )
    screen = Image.new("RGBA", (S, S), SCREEN_FILL + (26,))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([56 * k, 74 * k, 200 * k, 150 * k], radius=9 * k, fill=255)
    img = Image.alpha_composite(img, Image.composite(screen, Image.new("RGBA", (S, S), (0, 0, 0, 0)), mask))
    d = ImageDraw.Draw(img)

    # 波形：32 是三峰（原设计），24 减到一峰，16 直接省掉
    wave_w = round(max(6 * k, 1.3 * SS))
    if size >= 32:
        d.line(
            [(68 * k, 112 * k), (90 * k, 112 * k), (100 * k, 94 * k), (112 * k, 130 * k),
             (124 * k, 106 * k), (134 * k, 112 * k), (188 * k, 112 * k)],
            fill=SCREEN + (255,), width=wave_w, joint="curve",
        )
    elif size >= 24:
        d.line(
            [(72 * k, 112 * k), (98 * k, 112 * k), (112 * k, 92 * k), (126 * k, 132 * k),
             (140 * k, 112 * k), (184 * k, 112 * k)],
            fill=SCREEN + (255,), width=wave_w, joint="curve",
        )

    # 支架
    stand_w = round(max(10 * k, 1.4 * SS))
    if size >= 24:
        d.line([(128 * k, 166 * k), (128 * k, 186 * k)], fill=SCREEN + (230,), width=stand_w)
    d.line([(96 * k, 191 * k), (160 * k, 191 * k)], fill=SCREEN + (230,), width=stand_w)

    # 录制指示点（小尺寸不画光晕，省得糊）
    r_dot = max(10.5 * k, 2.0 * SS)
    if size >= 24:
        d.ellipse([228 * k - 16 * k, 54 * k - 16 * k, 228 * k + 16 * k, 54 * k + 16 * k], fill=RED + (51,))
    d.ellipse([228 * k - r_dot, 54 * k - r_dot, 228 * k + r_dot, 54 * k + r_dot], fill=RED + (255,))

    # 体检对勾徽章：按**最终像素**定尺寸（设计稿的 26/256 缩到 16px 只剩 1.6px），
    # 并夹在圆角底板内 —— 小尺寸下这枚绿勾是「这是什么软件」最有效的识别点。
    ring = max(1.1, 0.08 * size)
    r_badge = max(26 / 256 * size, {16: 3.2, 24: 4.4}.get(size, 5.4))
    cx = cy = min(198 / 256 * size, size - 0.6 - r_badge - ring)
    cx, cy, r_badge, ring = cx * SS, cy * SS, r_badge * SS, ring * SS
    cw = max(1.3, 0.30 * (r_badge / SS)) * SS
    d.ellipse([cx - r_badge - ring, cy - r_badge - ring, cx + r_badge + ring, cy + r_badge + ring], fill=DARK + (255,))
    d.ellipse([cx - r_badge, cy - r_badge, cx + r_badge, cy + r_badge], fill=GREEN + (255,))
    d.line(
        [(cx - 0.40 * r_badge, cy + 0.05 * r_badge),
         (cx - 0.10 * r_badge, cy + 0.36 * r_badge),
         (cx + 0.44 * r_badge, cy - 0.34 * r_badge)],
        fill=DARK + (255,), width=round(cw), joint="curve",
    )
    return img.resize((size, size), Image.LANCZOS)


def _contact_sheet(ico: Path, out: Path, sizes=(16, 24, 32, 48, 64, 128, 256)) -> None:
    gap, pad = 12, 8
    W = pad * 2 + sum(sizes) + gap * (len(sizes) - 1)
    H = max(sizes) + pad * 2
    sheet = Image.new("RGB", (W, H), (32, 36, 44))
    x = pad
    for s in sizes:
        f = Image.open(ico)
        f.size = (s, s)
        f.load()
        f = f.convert("RGBA")
        sheet.paste(f, (x, pad + (max(sizes) - s) // 2), f)
        x += s + gap
    sheet.save(out)


def main() -> int:
    ap = argparse.ArgumentParser(description="生成应用图标 appicon.ico")
    ap.add_argument("--preview", help="额外导出 256px 预览 PNG")
    ap.add_argument("--sheet", help="额外导出各尺寸对照图（目视校验用）")
    args = ap.parse_args()

    if not SRC.exists():
        print(f"找不到源图标：{SRC}", file=sys.stderr)
        return 1

    src = Image.open(SRC).convert("RGBA")
    src.putalpha(rounded_mask(src.width))
    detailed = src.resize((256, 256), Image.LANCZOS)

    smalls = {s: render_small(s) for s in SMALL_SIZES}
    detailed.save(
        DST,
        format="ICO",
        sizes=[(s, s) for s in SIZES],
        # 逐尺寸指定帧：16/24/32 用重绘的小图，其余由 provided_ims 里最后一张（detailed）缩放
        append_images=[smalls[s] for s in SMALL_SIZES] + [detailed],
    )

    verify = Image.open(DST)
    print(f"OK: {DST}")
    print(f"    sizes: {sorted(verify.ico.sizes())}")
    if args.preview:
        detailed.save(args.preview)
        print(f"    preview: {args.preview}")
    if args.sheet:
        _contact_sheet(DST, Path(args.sheet))
        print(f"    sheet: {args.sheet}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
