#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""生成 Tauri 图标源文件 app-icon.png（512x512，品牌紫底 + 白色播放三角）。
仅用标准库，无需第三方依赖。CI 中会用 `tauri icon app-icon.png` 生成其余尺寸。"""
import struct, zlib, os

SIZE = 512
BG = (0x7B, 0x2F, 0xF7)      # 品牌紫 #7b2ff7
FG = (0xFF, 0xFF, 0xFF)      # 白色

def px(x, y):
    # 白色播放三角（指向右的三角形），居中
    # 三角形顶点
    cx, cy = SIZE * 0.46, SIZE * 0.5
    w, h = SIZE * 0.30, SIZE * 0.34
    # 三角形：左中、右上、右下
    ax, ay = cx - w/2, cy - h/2
    bx, by = cx - w/2, cy + h/2
    cx2, cy2 = cx + w/2, cy
    def sign(px, py, qx, qy, rx, ry):
        return (px - rx) * (qy - ry) - (qx - rx) * (py - ry)
    d1 = sign(x, y, ax, ay, bx, by)
    d2 = sign(x, y, bx, by, cx2, cy2)
    d3 = sign(x, y, cx2, cy2, ax, ay)
    has_neg = (d1 < 0) or (d2 < 0) or (d3 < 0)
    has_pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
    if not (has_neg and has_pos):
        return FG
    return BG

# 构建原始 RGBA 像素行
raw = bytearray()
for y in range(SIZE):
    raw.append(0)  # filter type 0
    for x in range(SIZE):
        r, g, b = px(x, y)
        raw.extend((r, g, b, 255))

def chunk(tag, data):
    c = tag + data
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(c) & 0xffffffff)

png = b"\x89PNG\r\n\x1a\n"
png += chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
png += chunk(b"IEND", b"")

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "app-icon.png")
with open(out, "wb") as f:
    f.write(png)
print("wrote", out, os.path.getsize(out), "bytes")
