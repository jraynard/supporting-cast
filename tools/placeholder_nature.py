"""Shared helpers for the placeholder world art (the Nordic grade, save, the canopy painter). The grass is
Norse art now (norse_ground.py), and so are the trees (norse_nature.py).

Variants follow the atlas convention `name`, `name_v2`, `name_v3`...: the game picks one per cell
(see docs/ART.md). Trees are drawn at the game's 32 px per cell, lit from the top-left, with a dark
selective outline, and stand on the bottom row of the image (the trunk base is the anchor).
"""
import math
import os
import random

from PIL import Image

from norse_art import nordic_finish
from placeholder_sheets import outline

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
LIGHT = (-0.55, -0.83)  # top-left


# The old 16-bit placeholder look (bright yellow-greens, warm earth, clear blues), graded into the Nordic
# palette by world_finish. GRASS is the one ground green every grass-edged tile shares.
GRASS = (90, 132, 66)


def world_finish(img):
    """The shared last step for world art: the Nordic grade (norse_art.nordic_finish)."""
    return nordic_finish(img)


def save(img, name):
    path = os.path.join(ROOT, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    world_finish(img).save(path)


def clamp(v):
    return max(0, min(255, int(v)))


def shade(c, k):
    return (clamp(c[0] * k), clamp(c[1] * k), clamp(c[2] * k), 255)


def variant_name(base, i):
    return base if i == 0 else f"{base}_v{i + 1}"


def blob_canopy(img, circles, palette, rnd, dither=0.12):
    """Fill the union of circles, shaded by each pixel's position in its circle against the light."""
    w, h = img.size
    px = img.load()
    lx, ly = LIGHT
    for y in range(h):
        for x in range(w):
            best = None
            for cx, cy, r in circles:
                d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
                if d < r:
                    depth = 1 - d / r
                    if best is None or depth > best[0]:
                        best = (depth, (x + 0.5 - cx) / r, (y + 0.5 - cy) / r)
            if best is None:
                continue
            depth, nx, ny = best
            light = -(nx * lx + ny * ly) * 0.75 + 0.25 * depth + rnd.uniform(-dither, dither)
            tone = 0 if light < -0.25 else 1 if light < 0.15 else 2 if light < 0.5 else 3
            px[x, y] = palette[tone]
