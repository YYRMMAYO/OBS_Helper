import zlib, struct, os

def write_png(path, size, bg, fg):
    raw = bytearray()
    cx = cy = size / 2.0
    r_outer = size * 0.42
    r_inner = size * 0.20
    for y in range(size):
        raw.append(0)  # filter type 0 per scanline
        for x in range(size):
            dx = x - cx + 0.5
            dy = y - cy + 0.5
            dist = (dx * dx + dy * dy) ** 0.5
            col = fg if r_inner <= dist <= r_outer else bg
            raw += bytes((col[0], col[1], col[2], 255))

    def chunk(typ, data):
        return (struct.pack(">I", len(data)) + typ + data
                + struct.pack(">I", zlib.crc32(typ + data) & 0xffffffff))

    sig = b'\x89PNG\r\n\x1a\n'
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    idat = zlib.compress(bytes(raw), 9)
    out = sig + chunk(b'IHDR', ihdr) + chunk(b'IDAT', idat) + chunk(b'IEND', b'')
    with open(path, 'wb') as f:
        f.write(out)

bg = (123, 47, 247)
fg = (255, 255, 255)
target = r'F:\OBS\OBS_Helper.Client\wwwroot'
os.makedirs(target, exist_ok=True)
write_png(os.path.join(target, 'obs-icon-192.png'), 192, bg, fg)
write_png(os.path.join(target, 'obs-icon-512.png'), 512, bg, fg)
print('icons generated:', os.path.join(target, 'obs-icon-192.png'), os.path.join(target, 'obs-icon-512.png'))
