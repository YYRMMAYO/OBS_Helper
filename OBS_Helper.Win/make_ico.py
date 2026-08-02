#!/usr/bin/env python3
"""把 PNG 包装成 Windows .ico（PNG 负载，支持 256+ 尺寸）。无需第三方库。"""
import struct, os

src = r"F:\OBS\OBS_Helper.Win\wwwroot\obs-icon-512.png"
out = r"F:\OBS\OBS_Helper.Win\appicon.ico"

with open(src, "rb") as f:
    png = f.read()

# ICONDIR
header = struct.pack("<HHH", 0, 1, 1)
# ICONDIRENTRY: w,h,colorCount,reserved,planes,bitCount,bytesInRes,imageOffset
entry = struct.pack("<BBBBHHII", 0, 0, 0, 0, 1, 32, len(png), 6 + 16)
with open(out, "wb") as f:
    f.write(header + entry + png)

print("wrote", out, os.path.getsize(out), "bytes")
