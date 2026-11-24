"""Aperçu PNG de Resources/CityLayout.json (relief ombré, eau, forêts, routes, bâtiments, monuments).

Usage : python -I tools/citylayout/preview_layout.py <sortie.png> [pixels par mètre]
Sans dépendance (PNG écrit avec zlib).
"""
import json, math, os, struct, sys, zlib

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
L = json.load(open(os.path.join(ROOT, "IvoryState", "Assets", "_Project", "Resources", "CityLayout.json"), encoding="utf-8"))
out = sys.argv[1]
PPM = float(sys.argv[2]) if len(sys.argv) > 2 else 0.5
x0, z0, x1, z1 = L["bounds"]
W, H = int((x1 - x0) * PPM), int((z1 - z0) * PPM)
img = bytearray(W * H * 3)


def put(px, py, c):
    if 0 <= px < W and 0 <= py < H:
        i = (py * W + px) * 3
        img[i:i + 3] = bytes(c)


def to_px(x, z):
    return int((x - x0) * PPM), int(H - 1 - (z - z0) * PPM)


def mask(m):
    cells = set()
    r = m["runs"]
    for i in range(0, len(r), 3):
        for c in range(r[i + 1], r[i + 1] + r[i + 2]):
            cells.add((c, r[i]))
    return m, cells


wm, wc = mask(L["waterMask"])
fm, fc = mask(L["forestMask"])
hd = L["height"]


def height(x, z):
    fx, fz = (x - hd["x"]) / hd["cell"], (z - hd["z"]) / hd["cell"]
    i, j = min(max(int(fx), 0), hd["nx"] - 2), min(max(int(fz), 0), hd["nz"] - 2)
    tx, tz = fx - i, fz - j
    h = hd["h"]
    a, b, c, d = h[j * hd["nx"] + i], h[j * hd["nx"] + i + 1], h[(j + 1) * hd["nx"] + i], h[(j + 1) * hd["nx"] + i + 1]
    return ((a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz) * 0.1


for py in range(H):
    z = z0 + (H - 1 - py) / PPM
    for px in range(W):
        x = x0 + px / PPM
        if (int((x - wm["x"]) / wm["cell"]), int((z - wm["z"]) / wm["cell"])) in wc:
            c = (120, 175, 215)
        else:
            h = height(x, z)
            # Ombrage : pente vers le nord-ouest.
            shade = (height(x - 4, z + 4) - h) * 0.9
            base = (95, 130, 70) if (int((x - fm["x"]) / fm["cell"]), int((z - fm["z"]) / fm["cell"])) in fc else (196, 200, 160)
            k = 1.0 - h / 60.0 + shade
            c = tuple(max(0, min(255, int(v * k))) for v in base)
        put(px, py, c)


def line(xa, za, xb, zb, width, col):
    n = max(2, int(math.hypot(xb - xa, zb - za) * PPM * 1.5))
    r = max(0, int(width * PPM / 2))
    for k in range(n + 1):
        t = k / n
        px, py = to_px(xa + (xb - xa) * t, za + (zb - za) * t)
        for dx in range(-r, r + 1):
            for dy in range(-r, r + 1):
                put(px + dx, py + dy, col)


for strip in L["airport"]["runways"] + L["airport"]["taxiways"]:
    p = strip["p"]
    for i in range(0, len(p) - 2, 2):
        line(p[i], p[i + 1], p[i + 2], p[i + 3], strip["width"], (90, 90, 90))
for r in L["roads"]:
    p = r["p"]
    col = (60, 60, 60) if r["cls"] <= 1 else (95, 95, 95)
    for i in range(0, len(p) - 2, 2):
        line(p[i], p[i + 1], p[i + 2], p[i + 3], r["width"], col)
    b = r["b"]
    for i in range(0, len(b) - 3, 4):
        line(b[i], b[i + 1], b[i + 2], b[i + 3], r["width"] * 0.7, (230, 190, 90))
for b in L["buildings"]:
    px, py = to_px(b["x"], b["z"])
    s = max(1, int(min(b["w"], b["d"]) * PPM / 2))
    for dx in range(-s, s + 1):
        for dy in range(-s, s + 1):
            put(px + dx, py + dy, (170, 120, 90))
for item in L["blocks"] + L["landmarks"]:
    w = item.get("w", item.get("size", 64))
    d = item.get("d", item.get("size", 64))
    ya = math.radians(item["yaw"])
    ca, sa = math.cos(ya), math.sin(ya)
    corners = [(item["x"] + u * ca - v * sa, item["z"] + u * sa + v * ca) for u, v in ((-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2))]
    col = (200, 30, 30) if "key" in item else (240, 240, 240)
    for k in range(4):
        line(*corners[k], *corners[(k + 1) % 4], 2.5, col)

rows = b"".join(b"\x00" + bytes(img[y * W * 3:(y + 1) * W * 3]) for y in range(H))
png = b"\x89PNG\r\n\x1a\n"
for tag, data in ((b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0)), (b"IDAT", zlib.compress(rows, 9)), (b"IEND", b"")):
    png += struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
open(out, "wb").write(png)
print("aperçu", W, "x", H, "->", out)
