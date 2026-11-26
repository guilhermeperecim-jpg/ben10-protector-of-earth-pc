# Draws the launcher's and the game window's icon (our own art: a green energy ring around a bright core on a dark
# rounded square, the home page's light) into launcher/Assets/icon.png (256 px) and icon.ico (16-256 px, PNG inside).
# No libraries: each pixel from distances, written with zlib. usage: python -I tools/make_icon.py
import math
import os
import struct
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'launcher', 'Assets')


def pixel(x, y, n):
    """RGBA of the pixel centred at (x, y) on an n x n icon, from 0-1 coordinates."""
    u, v = (x + 0.5) / n * 2 - 1, (y + 0.5) / n * 2 - 1  # -1..1
    # the rounded square (corner radius 0.32), anti-aliased over one pixel
    q = max(abs(u) - 0.68, 0.0), max(abs(v) - 0.68, 0.0)
    edge = math.hypot(*q) - 0.32
    inside = min(1.0, max(0.0, -edge * n / 2 + 0.5))
    if inside <= 0:
        return 0, 0, 0, 0
    r = math.hypot(u, v * 1.12)  # a little wider than tall, as the home page's ring
    # background: dark green, lighter towards the middle
    bg = 1 - min(1.0, r)
    cr, cg, cb = 14 + 18 * bg, 22 + 34 * bg, 17 + 16 * bg
    # the glow
    glow = math.exp(-(r / 0.42) ** 2)
    cr, cg, cb = cr + 60 * glow, cg + 150 * glow, cb + 40 * glow
    # the ring
    ring = math.exp(-((r - 0.62) / (0.045 + 1.5 / n)) ** 2)
    cr, cg, cb = cr + (127 - cr) * ring, cg + (210 - cg) * ring, cb + (63 - cb) * ring
    # the core
    core = math.exp(-(r / 0.16) ** 2)
    cr, cg, cb = cr + (255 - cr) * core, cg + (255 - cg) * core, cb + (235 - cb) * core
    clamp = lambda c: int(max(0, min(255, round(c))))
    return clamp(cr), clamp(cg), clamp(cb), clamp(255 * inside)


def png(n):
    rows = b''.join(b'\0' + b''.join(bytes(pixel(x, y, n)) for x in range(n)) for y in range(n))

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xFFFFFFFF)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', n, n, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b''))


def main():
    os.makedirs(OUT, exist_ok=True)
    sizes = [16, 24, 32, 48, 64, 128, 256]
    images = [png(n) for n in sizes]
    with open(os.path.join(OUT, 'icon.png'), 'wb') as f:
        f.write(images[-1])
    header = struct.pack('<HHH', 0, 1, len(sizes))
    offset = 6 + 16 * len(sizes)
    entries = b''
    for n, data in zip(sizes, images):
        entries += struct.pack('<BBBBHHII', n % 256, n % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    with open(os.path.join(OUT, 'icon.ico'), 'wb') as f:
        f.write(header + entries + b''.join(images))
    print('icon:', os.path.join(OUT, 'icon.ico'))


if __name__ == '__main__':
    main()
