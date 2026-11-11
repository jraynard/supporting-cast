#!/usr/bin/env python3
"""Roof tiles (32x32): thatch (also drawn over enclosed rooms) and plank shingles.

Part of gen_placeholder_art.py; also runs alone, so new roof art doesn't mean regenerating
(and overwriting) everything else:  python3 tools/placeholder_roofs.py
"""
import os
import random
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
import placeholder_nature  # noqa: E402

S = 32
ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")


def _save(img, name):
    path = os.path.join(ROOT, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    placeholder_nature.world_finish(img).save(path)


def _clamp(c):
    return tuple(max(0, min(255, int(v))) for v in c)


def thatch(seed=71):
    """Rows of bundled straw, each row's ends overlapping the one below: reads as thatch from above."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    base = (176, 142, 78)
    for y in range(S):
        row = y // 8
        within = y % 8
        # Each course is lighter at its top edge and shadowed where the next course overlaps it.
        shade = 18 - within * 4 if within < 6 else -26
        for x in range(S):
            strand = ((x + row * 3) % 3 == 0) * -10
            d = rnd.randint(-14, 14)
            px[x, y] = _clamp((base[0] + shade + strand + d, base[1] + shade + strand + d * 0.8, base[2] + shade * 0.6 + d * 0.5)) + (255,)
    # Loose stalks.
    for _ in range(26):
        x, y = rnd.randrange(S), rnd.randrange(S)
        for k in range(rnd.randint(2, 5)):
            if y + k < S:
                px[x, y + k] = (214, 184, 112, 255)
    return img


# Base colours of each wood (as norse_buildings.WOODS): shingles come in the wood they're split from.
WOODS = {"": (140, 100, 60), "_oak": (146, 100, 60), "_pine": (206, 162, 102), "_birch": (226, 208, 172)}


def shingles(base, seed=83):
    """Overlapping wooden shingles in offset courses."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        course = y // 8
        within = y % 8
        offset = 5 if course % 2 else 0
        for x in range(S):
            seam = (x + offset) % 10 == 0
            shade = 14 - within * 3 if within < 7 else -40
            d = rnd.randint(-8, 8)
            if seam:
                shade -= 34
            px[x, y] = _clamp((base[0] + shade + d, base[1] + shade + d, base[2] + shade + d)) + (255,)
    return img


def generate():
    t = thatch()
    _save(t, "buildings/roof_thatch")
    # Thatch is straw, whatever wood its poles came from: the same art for every wood, so the
    # material tint doesn't colour the straw.
    for wood in ("oak", "pine", "birch"):
        _save(t, f"buildings/roof_thatch_{wood}")
    for suffix, base in WOODS.items():
        _save(shingles(base), "buildings/roof_shingles" + suffix)


if __name__ == "__main__":
    generate()
