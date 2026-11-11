#!/usr/bin/env python3
"""The game's icon: a front-on pixel-art dragon head in the adult dragon's colours (rust scales,
bone horns, gold eyes, ember veins) on a dark rounded tile.

Drawn at 64x64 with hard-edged shapes, mirrored down the middle, then scaled up by whole pixels.
Writes data/icon.png (256, the window icon), packaging/icon.ico (Windows exe), packaging/icon.icns
(the Mac bundle) and packaging/icon-<n>.png (Linux desktop entry sizes).

    python3 tools/gen_icon.py
"""
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
N = 64

OUTLINE = (34, 16, 18, 255)
SCALE = (154, 58, 38, 255)
SCALE_DARK = (110, 36, 24, 255)
SCALE_LIGHT = (196, 92, 56, 255)
HORN = (230, 220, 196, 255)
HORN_DARK = (168, 156, 132, 255)
EYE = (255, 210, 58, 255)
EYE_GLOW = (255, 240, 150, 255)
PUPIL = (40, 16, 8, 255)
EMBER = (255, 138, 30, 255)
EMBER_HOT = (255, 214, 96, 255)
FANG = (240, 232, 214, 255)


def mirror(points):
    return [(N - 1 - x, y) for x, y in points]


def both(draw, points, fill):
    draw.polygon(points, fill=fill)
    draw.polygon(mirror(points), fill=fill)


def both_line(draw, points, fill, width=1):
    draw.line(points, fill=fill, width=width)
    draw.line(mirror(points), fill=fill, width=width)


def both_point(draw, points, fill):
    draw.point(points, fill=fill)
    draw.point(mirror(points), fill=fill)


def head():
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Horns sweep up and out from the brow.
    both(d, [(21, 21), (25, 16), (18, 9), (10, 3), (7, 2), (11, 8), (15, 15), (17, 21)], HORN)
    both(d, [(17, 21), (15, 15), (11, 8), (7, 2), (9, 7), (13, 15), (15, 21)], HORN_DARK)
    # Small back horns.
    both(d, [(16, 25), (19, 22), (9, 15), (6, 14), (9, 18)], HORN_DARK)

    # Cheek frills.
    both(d, [(15, 30), (3, 31), (6, 34), (16, 37)], HORN_DARK)
    both(d, [(16, 36), (5, 41), (8, 43), (19, 42)], HORN_DARK)
    both(d, [(15, 30), (5, 31), (16, 35)], HORN)

    # Skull and snout.
    skull = [(24, 16), (32, 15), (32, 60), (27, 59), (24, 54), (22, 46), (16, 40), (14, 32), (16, 24), (20, 18)]
    both(d, skull, SCALE)
    # Shadowed jowls and the snout's sides.
    both(d, [(16, 34), (22, 46), (24, 54), (27, 59), (29, 59), (26, 52), (24, 44), (19, 36)], SCALE_DARK)
    # Light down the middle of the snout and across the brow.
    d.polygon([(29, 17), (35, 17), (34, 50), (32, 54), (30, 50)], fill=SCALE_LIGHT)
    both(d, [(21, 20), (28, 18), (29, 21), (22, 24)], SCALE_LIGHT)

    # Heavy brow ridges over slanted eyes.
    both(d, [(17, 26), (29, 30), (29, 32), (18, 29)], SCALE_DARK)
    both(d, [(19, 30), (28, 33), (27, 36), (22, 35), (19, 32)], EYE)
    both_point(d, [(20, 31), (21, 31)], EYE_GLOW)
    both_line(d, [(24, 32), (24, 35)], PUPIL)

    # Ember veins on the forehead.
    d.line([(32, 19), (32, 26)], fill=EMBER)
    d.line([(31, 26), (31, 28)], fill=EMBER)
    both_line(d, [(30, 22), (27, 25)], EMBER)
    d.point([(32, 21), (32, 22)], fill=EMBER_HOT)

    # Nostrils, glowing, and wisps of heat.
    both(d, [(27, 52), (30, 51), (30, 54), (28, 55)], OUTLINE)
    both_point(d, [(29, 53)], EMBER)
    both_point(d, [(27, 49), (26, 47)], EMBER)

    # Fangs below the snout.
    both(d, [(26, 57), (28, 58), (27, 62)], FANG)

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
    """A dark rounded tile with an ember glow rising behind the head."""
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, N - 1, N - 1], radius=12, fill=(30, 20, 34, 255))
    # Glow: stepped bands, kept chunky to stay pixel-art.
    for r, col in ((30, (44, 26, 52, 255)), (24, (58, 32, 66, 255)), (18, (74, 38, 78, 255))):
        d.ellipse([32 - r, 50 - r, 32 + r, 50 + r], fill=col)
    mask = Image.new("L", (N, N), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, N - 1, N - 1], radius=12, fill=255)
    clipped = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    clipped.paste(img, (0, 0), mask)
    ImageDraw.Draw(clipped).rounded_rectangle([0, 0, N - 1, N - 1], radius=12, outline=(84, 50, 60, 255))
    return clipped


def main():
    icon = tile()
    icon.alpha_composite(outlined(head()))
    big = icon.resize((256, 256), Image.NEAREST)
    big.save(os.path.join(ROOT, "data", "icon.png"))
    pkg = os.path.join(ROOT, "packaging")
    os.makedirs(pkg, exist_ok=True)
    for size in (32, 48, 64, 128, 256):
        resample = Image.NEAREST if size % N == 0 or N % size == 0 else Image.LANCZOS
        icon.resize((size, size), resample).save(os.path.join(pkg, f"icon-{size}.png"))
    big.save(os.path.join(pkg, "icon.ico"), sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    icon.resize((1024, 1024), Image.NEAREST).save(os.path.join(pkg, "icon.icns"))  # macOS bundle
    print("wrote data/icon.png and packaging/icon*")


if __name__ == "__main__":
    main()
