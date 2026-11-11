"""Norse HD-2D forage plants: berry bushes (lingonberry-red berries on a dark, small-leaved shrub; picked
bushes keep their leaves), wild flax in flower (cut back to stubble once harvested) and emberroot (its red
leaves above turned earth). 32x32, standing on the bottom row, lit from the top left, selective outline,
saved as is. Same names and variants as the placeholder drawings they replace.
"""
import math
import os
import random

from PIL import Image

from norse_art import ramp, mask_of, shade_shape, shade_over, selective_outline, bayer
from norse_buildings import graded

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
S = 32

LEAF = ramp(graded((50, 104, 52)), 6, main=3, dark=0.42, light=1.45, cool=18, warm=26)
BERRY = ((112, 16, 38, 255), (178, 30, 52, 255), (232, 108, 120, 255))
EARTH = ramp(graded((110, 82, 58)), 4, main=1, dark=0.6, light=1.25)


def put(img, x, y, c):
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((int(x), int(y)), c)


def leaves(x, y):
    """Small oval leaves in a fixed pattern: a lit tip and a shadowed base on each."""
    k = (x * 3 + y * 5) % 9
    return 0.35 if k == 0 else -0.3 if k in (4, 5) else 0


def berry_bush(seed, berries=True):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    lf = LEAF if berries else [tuple(int(c * 0.92) for c in col[:3]) + (255,) for col in LEAF]
    clumps = [(16, 21, 10.5)]
    for _ in range(rnd.randint(5, 7)):
        a = rnd.uniform(math.pi, math.tau) if rnd.random() < 0.6 else rnd.uniform(0, math.tau)
        clumps.append((16 + math.cos(a) * rnd.uniform(5, 9), 21 + math.sin(a) * rnd.uniform(3, 7), rnd.uniform(5, 7.5)))
    clumps = [(x, min(y, 31 - r * 0.6), r) for x, y, r in clumps]
    for x, y, r in sorted(clumps, key=lambda c: c[1]):          # back clumps first, each overlapping the last
        m = mask_of(img.size, lambda d, x=x, y=y, r=r: d.ellipse([x - r, y - r * 0.85, x + r, y + r * 0.85], fill=255))
        shade_over(img, m, lf, lf[0], radius=1.6, slope=1.3, depth=0.5, tex=leaves)
    img = selective_outline(img)
    if berries:
        px = img.load()
        for _ in range(rnd.randint(7, 10)):
            cx, cy = rnd.randint(7, 25), rnd.randint(11, 27)
            if px[cx, cy][3] == 0:
                continue
            for dx, dy in rnd.sample([(0, 0), (2, 1), (-1, 2), (1, -2), (-2, -1)], rnd.randint(2, 4)):
                x, y = cx + dx, cy + dy
                if 1 <= x < S - 1 and 1 <= y < S - 1 and px[x, y][3] > 0:
                    px[x, y] = BERRY[1]; px[x + 1, y] = BERRY[0]; px[x, y + 1] = BERRY[0]; px[x, y - 1] = BERRY[2]
    return img


def mound(img, cx=16, w=7):
    m = mask_of(img.size, lambda d: d.ellipse([cx - w, 26, cx + w, 31], fill=255))
    shade_shape(img, m, EARTH, radius=1.0, slope=1.3)


def wild_flax(bare=False):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    mound(img)
    stem = ramp(graded((122, 150, 74)), 4, main=1, dark=0.6, light=1.3)
    flower = ((70, 92, 168, 255), (120, 150, 222, 255), (190, 206, 246, 255))
    for (x0, x1, top) in ((12, 9, 9), (14, 13, 6), (16, 17, 5), (18, 21, 7), (20, 24, 10), (15, 11, 12), (17, 19, 13)):
        t = top if not bare else 28 - (28 - top) // 3
        n = 28 - t
        for k in range(n + 1):
            put(img, round(x0 + (x1 - x0) * k / max(1, n)), 28 - k, stem[2 if k % 3 else 1] if not bare else stem[0])
        if not bare:                                            # five-petalled flax flower, pale centre
            for dx, dy, c in ((0, -1, 2), (-1, 0, 1), (1, 0, 1), (0, 1, 0), (-1, -1, 2), (1, 1, 0)):
                put(img, x1 + dx, t + dy, flower[c])
            put(img, x1, t, (240, 232, 180, 255))
        else:
            put(img, x1, t, stem[3])                            # a cut end
    return selective_outline(img, keep=())


def emberroot(bare=False):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    mound(img)
    red = ramp(graded((150, 40, 34)), 5, dark=0.5, light=1.45, cool=16, warm=16)
    if bare:
        put(img, 16, 25, red[0]); put(img, 16, 24, red[1]); put(img, 16, 23, red[2])
        return selective_outline(img)
    for tip in ((7, 10), (24, 8), (16, 5), (10, 16), (23, 15)):
        mid = ((16 + tip[0]) / 2, (26 + tip[1]) / 2 + 1)
        leafpts = [(16, 26), (mid[0] - 2.5, mid[1]), tip, (mid[0] + 2.5, mid[1] + 1)]
        m = mask_of(img.size, lambda d, p=leafpts: d.polygon(p, fill=255))
        shade_over(img, m, red, red[0], radius=1.0, slope=1.4)
        n = 8
        for k in range(1, n):                                   # the pale midrib
            put(img, round(16 + (tip[0] - 16) * k / n), round(26 + (tip[1] - 26) * k / n), red[4])
    put(img, 16, 26, (232, 120, 60, 255)); put(img, 15, 26, (196, 84, 52, 255))   # the root's shoulder in the earth
    return selective_outline(img)


def generate():
    def save(img, name):
        path = os.path.join(ROOT, name + ".png")
        img.save(path)
    for i in range(3):
        suffix = "" if i == 0 else f"_v{i + 1}"
        save(berry_bush(40 + i), "things/berry_bush" + suffix)
        save(berry_bush(40 + i, berries=False), "things/berry_bush_bare" + suffix)
    save(wild_flax(), "things/wild_flax")
    save(wild_flax(bare=True), "things/wild_flax_bare")
    save(emberroot(), "things/emberroot")
    save(emberroot(bare=True), "things/emberroot_bare")


if __name__ == "__main__":
    generate()
