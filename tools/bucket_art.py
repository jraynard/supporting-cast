#!/usr/bin/env python3
"""The well's buckets: ``items/bucket`` (empty) and ``items/bucket_full`` (water to the brim), inventory
icons for fighting fire (Environment/Buckets). A stave bucket in three-quarter view, iron hoops and a rope
bail. Writes only its own files. Run alone (``python3 tools/bucket_art.py``) or from
``gen_placeholder_art.py``.
"""
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
TEXTURES = os.path.join(HERE, "..", "data", "textures")
S = 32

WOOD = (150, 104, 62)
WOOD_DARK = (104, 70, 40)
WOOD_LIGHT = (186, 138, 88)
HOOP = (70, 72, 78)
ROPE = (196, 172, 120)
WATER = (78, 136, 196)
WATER_LIGHT = (150, 196, 232)
INSIDE = (66, 44, 26)


def save(img, rel):
    path = os.path.join(TEXTURES, rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def bucket(full):
    """Staves tapering to the foot, two hoops, an elliptical mouth, the bail arched over it."""
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    px = img.load()
    top, bottom = 12, 28
    for y in range(top, bottom + 1):
        t = (y - top) / (bottom - top)
        half = round(10 - 2.5 * t)               # wider at the mouth
        for x in range(16 - half, 16 + half):
            stave = (x - (16 - half)) % 4
            c = WOOD_LIGHT if stave == 0 else WOOD_DARK if stave == 3 else WOOD
            if x - (16 - half) < 2:
                c = WOOD_DARK                    # the shaded side
            px[x, y] = c + (255,)
    for hy in (15, 25):                          # the hoops
        t = (hy - top) / (bottom - top)
        half = round(10 - 2.5 * t)
        for x in range(16 - half, 16 + half):
            px[x, hy] = HOOP + (255,)
    for x in range(6, 26):                       # the mouth: rim, then inside or water
        for y in range(9, 15):
            dx, dy = (x - 15.5) / 10, (y - 12) / 3
            d = dx * dx + dy * dy
            if d <= 1:
                rim = d > 0.6
                if rim:
                    px[x, y] = WOOD_LIGHT + (255,)
                elif full:
                    px[x, y] = (WATER_LIGHT if (x + y) % 5 == 0 and y < 12 else WATER) + (255,)
                else:
                    px[x, y] = INSIDE + (255,)
    for x in range(7, 25):                       # the rope bail, arched over
        dx = (x - 16) / 9
        y = round(9 - 7 * (1 - dx * dx))
        px[x, y] = ROPE + (255,)
    return img


def generate():
    save(bucket(False), "items/bucket")
    save(bucket(True), "items/bucket_full")


if __name__ == "__main__":
    generate()
    print("wrote the buckets")
