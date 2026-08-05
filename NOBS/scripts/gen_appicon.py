"""按品牌图标 ob-logo.svg 的设计重绘应用图标 appicon.ico（多尺寸）。

设计规格（来自 品牌图标/ob-logo.svg）：
  64×64 网格，圆角矩形 rx=14（≈21.9%），底色 #7b2ff7，白字 "OB"（Segoe UI Bold 26px）居中。

用法：
  python scripts/gen_appicon.py [输出 .ico 路径]
  （默认输出到 OBS_Helper.Wpf/Assets/appicon.ico，并在系统临时目录写一张预览 PNG）

依赖：Pillow（pip install Pillow）
"""
import os
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

BRAND = (123, 47, 247, 255)  # #7b2ff7
WHITE = (255, 255, 255, 255)
GRID = 64
RADIUS = 14                  # 圆角半径（64 网格）
FONT_RATIO = 26 / GRID       # SVG 字号 26px / 网格
BASELINE_Y = 42 / GRID       # SVG 基线 y=42 / 网格

# 应用图标标准多尺寸（Windows .ico 常用集合）
SIZES = [16, 24, 32, 48, 64, 128, 256]

# 渲染超采样倍数：基础图按 256×4 绘制再逐级缩小，小尺寸更清晰
SUPERSAMPLE = 4
BASE_SIZE = 256 * SUPERSAMPLE


def find_font(px: int) -> ImageFont.FreeTypeFont:
    candidates = [
        r"C:\Windows\Fonts\segoeuib.ttf",   # Segoe UI Bold（与 SVG font-weight 700 对应）
        r"C:\Windows\Fonts\seguisb.ttf",    # Segoe UI Semibold
        r"C:\Windows\Fonts\segoeui.ttf",    # Segoe UI Regular
        r"C:\Windows\Fonts\arialbd.ttf",    # Arial Bold（fallback）
        r"C:\Windows\Fonts\arial.ttf",      # Arial（fallback）
    ]
    for path in candidates:
        if Path(path).exists():
            try:
                return ImageFont.truetype(path, px)
            except Exception:
                continue
    return ImageFont.load_default()


def render_base(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = max(1, round(size * RADIUS / GRID))
    d.rounded_rectangle((0, 0, size - 1, size - 1), radius=r, fill=BRAND)

    font = find_font(round(size * FONT_RATIO))
    # 视觉居中：中心 y ≈ 基线 - 0.32 × 字号；x 用文本包围盒精确居中
    cy = size * BASELINE_Y - round(size * FONT_RATIO * 0.32)
    bbox = d.textbbox((0, 0), "OB", font=font)
    cx = (size - (bbox[2] - bbox[0])) / 2 - bbox[0]
    d.text((cx, cy), "OB", font=font, fill=WHITE)
    return img


def main() -> None:
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(r"F:\OBS\NOBS\OBS_Helper.Wpf\Assets\appicon.ico")
    out.parent.mkdir(parents=True, exist_ok=True)

    base = render_base(BASE_SIZE)
    big = base.resize((256, 256), Image.LANCZOS)
    smaller = [base.resize((s, s), Image.LANCZOS) for s in SIZES if s != 256]
    big.save(
        out,
        format="ICO",
        sizes=[(s, s) for s in SIZES],
        append_images=smaller,
    )
    print(f"OK: {out} ({len(SIZES)} sizes: {', '.join(str(s) for s in SIZES)})")

    preview = Path(os.environ.get("TEMP", ".")) / "appicon_ob.preview.png"
    base.resize((256, 256), Image.LANCZOS).save(preview)
    print(f"preview: {preview}")


if __name__ == "__main__":
    main()
