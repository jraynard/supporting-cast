#!/usr/bin/env python3
"""The game's icon: a smith's hammer and a foaming tankard crossed over a village shield, the
trades a party walks past, on a dark rounded tile with a hearth glow.

Drawn at 64x64 with hard-edged shapes (the hammer and tankard upright on their own layers, then
turned into place), each piece outlined, then scaled up by whole pixels. Writes data/icon.png
(256, the window icon), packaging/icon.ico (Windows exe), packaging/icon.icns (the Mac bundle),
packaging/icon-<n>.png (Linux desktop entry sizes) and packaging/icon-512.png (the Discord
server and itch.io page).

    python3 tools/gen_icon.py
"""
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
N = 64

OUTLINE = (30, 18, 14, 255)
CLEAR = (0, 0, 0, 0)
# The shield: a heater of dark green boards in an iron rim.
FIELD = (46, 84, 62, 255)
FIELD_DARK = (34, 62, 48, 255)
FIELD_LIGHT = (62, 106, 78, 255)
RIM = (126, 132, 140, 255)
RIM_DARK = (84, 88, 98, 255)
# The hammer: iron head, ash handle, leather grip.
IRON = (150, 156, 166, 255)
IRON_LIGHT = (208, 214, 220, 255)
IRON_DARK = (92, 96, 108, 255)
HANDLE = (176, 124, 72, 255)
HANDLE_DARK = (128, 84, 46, 255)
GRIP = (96, 56, 34, 255)
# The tankard: oak staves in brass hoops, a head of foam.
STAVE = (150, 92, 48, 255)
STAVE_DARK = (110, 64, 32, 255)
STAVE_LIGHT = (190, 124, 70, 255)
BRASS = (222, 176, 64, 255)
BRASS_LIGHT = (255, 224, 128, 255)
BRASS_DARK = (164, 120, 36, 255)
FOAM = (248, 244, 230, 255)
FOAM_DARK = (214, 204, 182, 255)


def layer():
    img = Image.new("RGBA", (N, N), CLEAR)
    return img, ImageDraw.Draw(img)


def shield():
    img, d = layer()
    outer = [(9, 8), (54, 8), (54, 30), (51, 42), (44, 51), (32, 58), (20, 51), (13, 42), (9, 30)]
    inner = [(12, 11), (51, 11), (51, 30), (48, 41), (42, 49), (32, 55), (22, 49), (16, 41), (12, 30)]
    d.polygon(inner, fill=FIELD)
    # Boards: darker seams, a lit left edge.
    for x in (22, 32, 42):
        d.line([(x, 11), (x, 56)], fill=FIELD_DARK)
    d.polygon([(13, 12), (16, 12), (16, 40), (13, 31)], fill=FIELD_LIGHT)
    # The rim drawn over the seams' ends, shaded down its right side.
    mask = Image.new("L", (N, N), 0)
    md = ImageDraw.Draw(mask)
    md.polygon(outer, fill=255)
    md.polygon(inner, fill=0)
    img.paste(Image.new("RGBA", (N, N), RIM), (0, 0), mask)
    d.line([(54, 9), (54, 30), (51, 42), (44, 51), (33, 57)], fill=RIM_DARK, width=2)
    d.point([(11, 10), (52, 10), (31, 9), (32, 9)], fill=IRON_LIGHT)  # rivets
    return img


def hammer():
    img, d = layer()
    d.rectangle([30, 18, 33, 63], fill=HANDLE)
    d.rectangle([32, 18, 33, 63], fill=HANDLE_DARK)
    d.rectangle([29, 50, 34, 63], fill=GRIP)
    for y in (53, 56, 59):
        d.line([(29, y), (34, y)], fill=HANDLE_DARK)
    # Head: a block with a broad face on one end and a peen on the other.
    d.rectangle([19, 9, 44, 19], fill=IRON)
    d.rectangle([19, 9, 44, 11], fill=IRON_LIGHT)
    d.rectangle([19, 17, 44, 19], fill=IRON_DARK)
    d.rectangle([15, 6, 20, 22], fill=IRON)
    d.rectangle([15, 6, 20, 8], fill=IRON_LIGHT)
    d.rectangle([15, 20, 20, 22], fill=IRON_DARK)
    d.polygon([(44, 10), (50, 12), (50, 16), (44, 18)], fill=IRON_DARK)
    return img


def tankard():
    img, d = layer()
    # Handle on the right: a brass loop.
    d.rectangle([37, 31, 44, 46], fill=BRASS)
    d.rectangle([37, 34, 40, 43], fill=CLEAR)
    d.line([(44, 32), (44, 45)], fill=BRASS_DARK)
    # Body, a touch wider at the foot.
    d.polygon([(21, 27), (38, 27), (39, 53), (20, 53)], fill=STAVE)
    for x in (26, 31, 35):
        d.line([(x, 27), (x, 53)], fill=STAVE_DARK)
    d.line([(22, 27), (21, 53)], fill=STAVE_LIGHT, width=2)
    d.line([(38, 27), (39, 53)], fill=STAVE_DARK)
    for y in (30, 46):
        d.rectangle([20, y, 39, y + 2], fill=BRASS)
        d.line([(20, y), (39, y)], fill=BRASS_LIGHT)
        d.line([(20, y + 2), (39, y + 2)], fill=BRASS_DARK)
    # Foam heaped over the rim, with a drip down the side.
    d.ellipse([18, 19, 27, 28], fill=FOAM)
    d.ellipse([24, 16, 34, 26], fill=FOAM)
    d.ellipse([31, 19, 40, 28], fill=FOAM)
    d.rectangle([20, 23, 39, 28], fill=FOAM)
    d.polygon([(22, 28), (25, 28), (24, 34), (22, 33)], fill=FOAM)
    d.line([(25, 28), (38, 28)], fill=FOAM_DARK)
    d.point([(27, 18), (28, 18), (26, 19)], fill=(255, 255, 255, 255))
    return img


def outlined(img):
    """A one-pixel dark outline around everything drawn (8-neighbour)."""
    src = img.load()
    out = img.copy()
    dst = out.load()
    for y in range(N):
        for x in range(N):
            if src[x, y][3]:
                continue
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < N and 0 <= ny < N and src[nx, ny][3]:
                        dst[x, y] = OUTLINE
                        break
                else:
                    continue
                break
    return out


def tile():
    """A dark rounded tile with a warm hearth glow behind the shield."""
    img, d = layer()
    d.rounded_rectangle([0, 0, N - 1, N - 1], radius=12, fill=(34, 24, 22, 255))
    # Glow: stepped bands, kept chunky to stay pixel-art.
    for r, col in ((34, (52, 34, 26, 255)), (27, (70, 44, 30, 255)), (20, (90, 56, 34, 255))):
        d.ellipse([32 - r, 36 - r, 32 + r, 36 + r], fill=col)
    mask = Image.new("L", (N, N), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, N - 1, N - 1], radius=12, fill=255)
    clipped = Image.new("RGBA", (N, N), CLEAR)
    clipped.paste(img, (0, 0), mask)
    ImageDraw.Draw(clipped).rounded_rectangle([0, 0, N - 1, N - 1], radius=12, outline=(96, 66, 46, 255))
    return clipped


def icon():
    img = tile()
    img.alpha_composite(outlined(shield()))
    # Crossed: the hammer leans right (head up on the right), the tankard leans left over it.
    img.alpha_composite(outlined(hammer().rotate(-40, resample=Image.NEAREST, center=(32, 38), translate=(0, 0))))
    img.alpha_composite(outlined(tankard().rotate(16, resample=Image.NEAREST, center=(30, 40), translate=(1, 2))))
    return img


def main():
    img = icon()
    big = img.resize((256, 256), Image.NEAREST)
    big.save(os.path.join(ROOT, "data", "icon.png"))
    pkg = os.path.join(ROOT, "packaging")
    os.makedirs(pkg, exist_ok=True)
    for size in (32, 48, 64, 128, 256):
        resample = Image.NEAREST if size % N == 0 or N % size == 0 else Image.LANCZOS
        img.resize((size, size), resample).save(os.path.join(pkg, f"icon-{size}.png"))
    big.save(os.path.join(pkg, "icon.ico"), sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    img.resize((1024, 1024), Image.NEAREST).save(os.path.join(pkg, "icon.icns"))  # macOS bundle
    img.resize((512, 512), Image.NEAREST).save(os.path.join(pkg, "icon-512.png"))  # Discord, itch.io
    print("wrote data/icon.png and packaging/icon*")


if __name__ == "__main__":
    main()
