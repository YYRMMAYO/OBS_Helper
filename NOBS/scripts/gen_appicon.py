"""按 OBS 官方 logo 重绘应用图标 appicon.ico（多尺寸）。

设计思路：
- 以 OBS 三旋涡圆形 logo 为主体，放在品牌紫底色圆角矩形中。
- 右下角叠加一个白色齿轮小徽章，体现“Helper / 助手”。
- 主体使用参考图 G:\\DCIM\\Tencent Files\\...\\e16f2b40384671a4dc9599db33cfda1f.png 的旋涡部分做蒙版。

用法：
  python scripts/gen_appicon.py [输出 .ico 路径]
  （默认输出到 OBS_Helper.Wpf/Assets/appicon.ico，并在 Outputs/ 目录写一张预览 PNG）

依赖：Pillow（pip install Pillow）
"""
import os
import sys
from math import cos, sin, pi
from pathlib import Path
from collections import deque

from PIL import Image, ImageDraw, ImageChops

# 品牌色
BRAND = (123, 47, 247, 255)          # #7b2ff7 紫
BADGE_BG = (0, 200, 83, 255)         # #00c853 绿， helper 徽章
WHITE = (255, 255, 255, 255)
TRANSPARENT = (0, 0, 0, 0)

GRID = 64
RADIUS = 14                          # 圆角半径（64 网格）
SIZES = [16, 24, 32, 48, 64, 128, 256]
BASE_SIZE = 1024

# 参考图路径（用户提供）
REFERENCE = Path(r"G:\DCIM\Tencent Files\752139192\nt_qq\nt_data\Pic\2026-08\Ori\e16f2b40384671a4dc9599db33cfda1f.png")


def _flood_fill_component(mask: Image.Image, start_x: int, start_y: int):
    """返回包含 (start_x,start_y) 的四连通区域的所有点坐标。"""
    w, h = mask.size
    px = mask.load()
    visited = [[False] * h for _ in range(w)]
    q = deque()
    q.append((start_x, start_y))
    visited[start_x][start_y] = True
    comp = [(start_x, start_y)]
    while q:
        x, y = q.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h and not visited[nx][ny] and px[nx, ny]:
                visited[nx][ny] = True
                q.append((nx, ny))
                comp.append((nx, ny))
    return comp


def load_obs_symbol(size: int, ref_path: Path) -> Image.Image:
    """从参考图中截取左侧三旋涡符号，缩放到 size×size，白色前景透明背景。"""
    if not ref_path.exists():
        raise FileNotFoundError(f"找不到参考图：{ref_path}")

    img = Image.open(ref_path).convert("L")
    # 左半区：符号整体在左侧，右侧是 OBS 文字
    left = img.crop((0, 0, img.width // 2, img.height))

    # 二值化
    mask = left.point(lambda p: 1 if p < 240 else 0)
    px = mask.load()

    # 找到一个属于符号的黑色像素作为种子（从左侧区域开始扫描）
    seed = None
    for y in range(mask.height):
        for x in range(mask.width):
            if px[x, y]:
                seed = (x, y)
                break
        if seed:
            break
    if not seed:
        raise RuntimeError("参考图中未识别到 OBS 符号")

    # 洪水填充获取符号连通区域，排除右侧文字
    comp = _flood_fill_component(mask, seed[0], seed[1])
    xs = [p[0] for p in comp]
    ys = [p[1] for p in comp]
    cx = sum(xs) // len(comp)
    cy = sum(ys) // len(comp)

    # 以连通区域中心裁出最大正方形
    half = int(min(cx, cy, left.width - cx, left.height - cy))
    # 保证正方形能完整包住符号区域
    max_dist = max(max(abs(x - cx), abs(y - cy)) for x, y in comp)
    half = max(half, max_dist + 4)
    half = min(half, cx, cy, left.width - cx, left.height - cy)
    crop_box = (cx - half, cy - half, cx + half, cy + half)
    symbol_gray = left.crop(crop_box)

    # 用灰度反转为 alpha：黑 -> 不透明，白 -> 透明
    symbol_rgba = Image.new("RGBA", symbol_gray.size, TRANSPARENT)
    alpha = symbol_gray.point(lambda p: 255 - p)
    white = Image.new("RGBA", symbol_gray.size, WHITE)
    symbol_rgba.paste(white, (0, 0), alpha)

    return symbol_rgba.resize((size, size), Image.LANCZOS)


def rounded_square(draw: ImageDraw.Draw, size: int, radius: int, fill):
    """绘制圆角正方形背景。"""
    draw.rounded_rectangle((0, 0, size - 1, size - 1), radius=radius, fill=fill)


def draw_gear(draw: ImageDraw.Draw, cx: int, cy: int, outer_r: int, inner_r: int,
              teeth: int, hole_r: int, fill):
    """绘制一个简单的齿轮。"""
    pts = []
    for i in range(teeth * 2):
        angle = 2 * pi * i / (teeth * 2)
        r = outer_r if i % 2 == 0 else inner_r
        pts.append((cx + r * cos(angle), cy + r * sin(angle)))
    draw.polygon(pts, fill=fill)
    draw.ellipse((cx - hole_r, cy - hole_r, cx + hole_r, cy + hole_r), fill=BADGE_BG)


def render_base(size: int, symbol: Image.Image) -> Image.Image:
    """渲染单张 size×size 图标。"""
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    d = ImageDraw.Draw(img)

    r = max(1, round(size * RADIUS / GRID))
    rounded_square(d, size, r, BRAND)

    # 主体旋涡居中，占画布约 72%
    sym_size = int(size * 0.72)
    symbol_scaled = symbol.resize((sym_size, sym_size), Image.LANCZOS)
    offset = (size - sym_size) // 2
    img.paste(symbol_scaled, (offset, offset), symbol_scaled)

    # 右下角 helper 徽章
    badge_r = int(size * 0.18)
    badge_cx = size - badge_r - int(size * 0.05)
    badge_cy = size - badge_r - int(size * 0.05)
    d.ellipse((badge_cx - badge_r, badge_cy - badge_r,
               badge_cx + badge_r, badge_cy + badge_r), fill=BADGE_BG)

    # 齿轮尺寸
    gear_outer = int(badge_r * 0.55)
    gear_inner = int(badge_r * 0.40)
    gear_hole = int(badge_r * 0.15)
    draw_gear(d, badge_cx, badge_cy, gear_outer, gear_inner, 8, gear_hole, WHITE)

    return img


def main() -> None:
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(r"F:\OBS\NOBS\OBS_Helper.Wpf\Assets\appicon.ico")
    out.parent.mkdir(parents=True, exist_ok=True)

    # 预先渲染一次高分辨率符号，供各尺寸复用
    symbol = load_obs_symbol(512, REFERENCE)

    base = render_base(BASE_SIZE, symbol)

    # 生成 ICO
    big = base.resize((256, 256), Image.LANCZOS)
    smaller = [base.resize((s, s), Image.LANCZOS) for s in SIZES if s != 256]
    big.save(
        out,
        format="ICO",
        sizes=[(s, s) for s in SIZES],
        append_images=smaller,
    )
    print(f"OK: {out} ({len(SIZES)} sizes: {', '.join(str(s) for s in SIZES)})")

    # 输出预览 PNG
    preview_dir = Path(r"F:\OBS\NOBS\Outputs")
    preview_dir.mkdir(parents=True, exist_ok=True)
    preview = preview_dir / "appicon_preview.png"
    base.resize((256, 256), Image.LANCZOS).save(preview)
    print(f"preview: {preview}")


if __name__ == "__main__":
    main()
