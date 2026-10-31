#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 X 系列极简应用图标（OBS_Helper.Wpf/Assets/appicon_x.ico）。

设计：黑色圆角方块 + 白色「OBS」大字标 + 下方「helper」小字标，
左侧一条青绿色圆弧与 O 重叠；16/24/32 小尺寸只保留 O 与弧线，保证任务栏可辨认。
不依赖网络、不引用第三方素材；只依赖 Pillow。

用法：
    python NOBS/scripts/gen_x_appicon.py
    python NOBS/scripts/gen_x_appicon.py --preview preview.png --sheet sheet.png
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]          # NOBS/
DST = ROOT / "OBS_Helper.Wpf" / "Assets" / "appicon_x.ico"

SIZES = (16, 24, 32, 48, 64, 128, 256)
SS = 8
BG = (11, 15, 20, 255)
TEXT = (245, 248, 250, 255)
ACCENT = (80, 220, 190, 255)
MUTED = (170, 210, 205, 255)


def _font(size: int, bold: bool = True) -> ImageFont.FreeTypeFont:
    """优先用 Windows 自带字体；失败时退回 Pillow 默认字体。"""
    candidates = [
        "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf",
        "C:/Windows/Fonts/segoeuib.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf",
    ]
    for path in candidates:
        try:
            if Path(path).exists():
                return ImageFont.truetype(path, size)
        except OSError:
            pass
    return ImageFont.load_default()


def _rounded(size: int, radius: int) -> Image.Image:
    """带抗锯齿的圆角方块（4x 超采样后缩小）。"""
    big = Image.new("RGBA", (size * 4, size * 4), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    d.rounded_rectangle([0, 0, size * 4 - 1, size * 4 - 1], radius=radius * 4, fill=BG)
    return big.resize((size, size), Image.LANCZOS)


def _arc(draw: ImageDraw.ImageDraw, box, width: int) -> None:
    """青绿色重叠弧线（从左下绕到左上）。"""
    draw.arc(box, start=120, end=330, fill=ACCENT, width=width)


def render(size: int) -> Image.Image:
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    # 圆角底板：内缩 4%，圆角 22%
    inset = round(S * 0.04)
    radius = round(S * 0.22)
    plate = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(plate).rounded_rectangle(
        [inset, inset, S - inset - 1, S - inset - 1], radius=radius, fill=BG)
    img.alpha_composite(plate)

    d = ImageDraw.Draw(img)

    if size < 48:
        # 小尺寸：只保留 O + 弧线，避免文字糊成一团。
        o_size = round(S * 0.58)
        font = _font(o_size)
        text = "O"
        bbox = d.textbbox((0, 0), text, font=font)
        tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
        tx = (S - tw) / 2 - bbox[0]
        ty = (S - th) / 2 - bbox[1] - round(S * 0.02)
        d.text((tx, ty), text, font=font, fill=TEXT)
        _arc(d, [round(S * 0.16), round(S * 0.18), round(S * 0.86), round(S * 0.88)],
             max(2, round(S * 0.045)))
    else:
        # 大尺寸：OBS + helper，弧线从 O 的左侧穿过。
        _arc(d, [round(S * 0.08), round(S * 0.18), round(S * 0.74), round(S * 0.84)],
             max(3, round(S * 0.035)))

        font_main = _font(round(S * 0.36))
        bbox = d.textbbox((0, 0), "OBS", font=font_main)
        tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
        tx = (S - tw) / 2 - bbox[0]
        ty = round(S * 0.20) - bbox[1]
        d.text((tx, ty), "OBS", font=font_main, fill=TEXT)

        font_sub = _font(round(S * 0.135), bold=False)
        sub = "helper"
        bbox2 = d.textbbox((0, 0), sub, font=font_sub)
        sw, sh = bbox2[2] - bbox2[0], bbox2[3] - bbox2[1]
        sx = (S - sw) / 2 - bbox2[0]
        sy = round(S * 0.62) - bbox2[1]
        d.text((sx, sy), sub, font=font_sub, fill=MUTED)

    return img.resize((size, size), Image.LANCZOS)


def _contact_sheet(ico: Path, out: Path) -> None:
    gap, pad = 12, 8
    W = pad * 2 + sum(SIZES) + gap * (len(SIZES) - 1)
    H = max(SIZES) + pad * 2
    sheet = Image.new("RGB", (W, H), (32, 36, 44))
    x = pad
    for s in SIZES:
        frame = Image.open(ico)
        frame.size = (s, s)
        frame.load()
        frame = frame.convert("RGBA")
        sheet.paste(frame, (x, pad + (max(SIZES) - s) // 2), frame)
        x += s + gap
    sheet.save(out)


def main() -> int:
    ap = argparse.ArgumentParser(description="生成 X 系列应用图标 appicon_x.ico")
    ap.add_argument("--preview", help="额外导出 256px 预览 PNG")
    ap.add_argument("--sheet", help="额外导出各尺寸对照图")
    args = ap.parse_args()

    frames = {s: render(s) for s in SIZES}
    frames[256].save(DST, format="ICO", sizes=[(s, s) for s in SIZES],
                     append_images=[frames[s] for s in SIZES if s != 256])

    verify = Image.open(DST)
    print(f"OK: {DST}")
    print(f"    sizes: {sorted(verify.ico.sizes())}")
    if args.preview:
        frames[256].save(args.preview)
        print(f"    preview: {args.preview}")
    if args.sheet:
        _contact_sheet(DST, Path(args.sheet))
        print(f"    sheet: {args.sheet}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
