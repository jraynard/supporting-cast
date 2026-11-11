#!/usr/bin/env python3
"""Trees, bushes, rocks and wild plants in the people's and dragons' chibi style: puffy rounded
clumps of foliage, flat cel shading lit from the upper left, clean outlines (dark green on leaves,
dark brown on wood, slate on stone), at four times the detail.

    python3 tools/chibi_nature.py              # things/* into data/textures (replacing the older drawings)
    python3 tools/chibi_nature.py --preview    # ...and .cache/nature.png

Each sprite keeps its old canvas (the game sizes billboards by ``renderSize`` and the sprite's
aspect, anchored at its bottom middle), only K times finer; WorldRenderer box-filters them when
shrunk (Quad.Smooth). Variants (``_v2``, ``_v3``) differ by seed; the game picks one per cell.
"""
import math
import os
import random
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from chibi_dragon import Canvas, K, union, minus, both, lerp, add  # noqa: E402
from norse_art import ramp, bayer, contact_sheet  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "data", "textures"))

LEAF_LINE = (30, 52, 38, 255)
WOOD_LINE = (52, 34, 30, 255)
STONE_LINE = (40, 42, 56, 255)
BARK = ramp((116, 80, 54), 5)
OAK_LEAF = ramp((96, 154, 76), 5, light=1.35)
BIRCH_LEAF = ramp((156, 194, 86), 5, light=1.3)
PINE_LEAF = ramp((52, 122, 98), 5, light=1.35)
BUSH_LEAF = ramp((70, 132, 74), 5, light=1.35)
BIRCH_BARK = ramp((228, 224, 212), 4, dark=0.6)
DEAD_BARK = ramp((98, 86, 82), 5)
STONE = ramp((150, 156, 172), 5, light=1.25)
MOSS = ramp((112, 152, 72), 4)
BERRY = ramp((210, 44, 52), 4)
SOIL = ramp((112, 82, 60), 4)
GLOW = (255, 150, 50, 255)
GLOW_HI = (255, 228, 120, 255)
RUNE_BLUE = (150, 220, 255, 255)
RUNE_BLUE_HI = (230, 248, 255, 255)
WHITE = (255, 255, 250, 255)


def clump(cv, lobes, colors, rnd, line=LEAF_LINE, sparkle=True, back=0):
    """Foliage as rounded lobes painted back to front, each outlined, lit from the upper left, with a
    few leaf highlights: the cartoon tree's puffs. The first ``back`` lobes sit behind the rest and
    take a tone darker, so the crown has depth."""
    whole = cv.empty()
    deep = [colors[0], colors[0], colors[1], colors[2], colors[3]]
    for i, (x, y, rx, ry) in enumerate(lobes):
        tones = deep if i < back else colors
        m = cv.ellipse(x, y, rx, ry)
        # a scalloped edge: small bumps round the lower rim
        for k in range(6):
            a = math.pi * (0.15 + 0.7 * k / 5)
            m = union(m, cv.ellipse(x + math.cos(a) * rx * 0.86, y + math.sin(a) * ry * 0.86, rx * 0.26, ry * 0.26))
        cv.paint(m, tones, "base", radius=1.6, line_color=line, bias=0.08, depth=0.35)
        leaves(cv, m, x, y, rx, ry, tones, rnd, line)
        if sparkle and i >= back:
            for _ in range(3):
                a = rnd.uniform(math.pi * 1.05, math.pi * 1.55)
                hx, hy = x + math.cos(a) * rx * 0.55, y + math.sin(a) * ry * 0.55
                cv.curve([(hx - rx * 0.12, hy + ry * 0.04), (hx + rx * 0.12, hy - ry * 0.06)], tones[4], "base", 3, only=m)
        whole = union(whole, m)
    return whole


def leaves(cv, m, x, y, rx, ry, tones, rnd, line=LEAF_LINE):
    """Leaf clusters inside a lobe: little scalloped cups in the shade (lower right), their rims in a
    darker tone, and small lit dabs on the upper left; the texture the older dithered trees had."""
    n = int(rx * ry / 2.6)
    for _ in range(n):
        a = rnd.uniform(0, math.tau)
        r = math.sqrt(rnd.uniform(0.04, 0.8))
        px, py = x + math.cos(a) * rx * r, y + math.sin(a) * ry * r
        lit = (math.cos(a) + math.sin(a)) * r                         # -1.1 upper left .. 1.1 lower right
        w = rnd.uniform(0.9, 1.4)
        if lit > 0.15:                                                # a cup: the underside of a cluster
            col = tones[1] if lit > 0.6 else tones[2]
            cv.curve([(px - w, py - w * 0.4), (px, py + w * 0.3), (px + w, py - w * 0.4)], col, "base", 2, only=m)
        elif lit < -0.25:                                             # a lit leaf
            col = tones[4] if lit < -0.6 else tones[3]
            cv.curve([(px - w * 0.6, py + w * 0.1), (px + w * 0.5, py - w * 0.25)], col, "base", 2, only=m)
        else:
            cv.curve([(px - w * 0.8, py - w * 0.2), (px, py + w * 0.25), (px + w * 0.8, py - w * 0.2)], tones[3] if lit < 0 else tones[2], "base", 2, only=m)


def branches(cv, root, spans, colors=BARK, line=WOOD_LINE):
    """Boughs from the top of the trunk up into the crown: they show in the gaps between lobes."""
    for (dx, dy, w) in spans:
        tip = (root[0] + dx, root[1] + dy)
        b = cv.strip([root, lerp(root, tip, 0.5), tip], [w, w * 0.7, w * 0.35], steps=4)
        cv.paint(b, colors, "base", radius=0.6, line_color=line)


def crown_shadow(cv, crown, trunk_mask, top_y, color):
    """The crown's shade on the trunk just under it."""
    band = both(trunk_mask, cv.poly([(0, 0), (cv.w, 0), (cv.w, top_y + 5), (0, top_y + 5)]))
    cv.fill(minus(band, crown), color, "base")


def trunk(cv, base, top, w0, w1, colors=BARK, line=WOOD_LINE, roots=True, marks=0, rnd=None, moss=False):
    bx, by = base
    m = cv.strip([base, lerp(base, top, 0.5), top], [w0, (w0 + w1) / 2, w1], steps=4)
    if roots:
        for s in (-1, 1):
            m = union(m, cv.poly([(bx + s * w0 * 0.3, by - 3), (bx + s * w0 * 0.95, by + 0.4), (bx + s * w0 * 0.2, by + 0.4)]))
    cv.paint(m, colors, "base", radius=1.0, line_color=line, bias=0.0)
    for k in range(4):                                                # bark furrows, wavering up the trunk
        f = (k + 0.5) / 4 - 0.5
        x0, x1 = bx + f * w0 * 0.7, top[0] + f * w1 * 0.8
        wob = 0.4 if k % 2 else -0.4
        cv.curve([(x0, by - 1.5), (lerp((x0, by), (x1, top[1]), 0.5)[0] + wob, (by + top[1]) / 2), (x1, top[1] + 2)], colors[0 if k % 2 else 1], "base", 2, only=m)
    if moss and rnd is not None:
        knot_y = top[1] + (by - top[1]) * rnd.uniform(0.35, 0.55)
        kx = lerp(base, top, (by - knot_y) / max(1, by - top[1]))[0] + w1 * 0.15
        knot = cv.ellipse(kx, knot_y, w1 * 0.13, w1 * 0.2)
        cv.paint(both(knot, m), [colors[0], colors[0], colors[1]], "base", radius=0.3, line_color=line)
        patch = both(m, union(cv.ellipse(bx - w0 * 0.2, by - 2.2, w0 * 0.2, 2.4), cv.ellipse(bx - w0 * 0.32, by - 0.6, w0 * 0.2, 1.4)))
        cv.paint(patch, MOSS, "base", radius=0.5, line_color=LEAF_LINE, bias=0.1, outside=None)
    if marks and rnd is not None:                                     # birch's dark marks
        for _ in range(marks):
            y = rnd.uniform(top[1] + 2, by - 2)
            x = bx + rnd.uniform(-w1 * 0.3, w1 * 0.3)
            cv.curve([(x - w1 * 0.25, y), (x + w1 * 0.25, y + 0.3)], (52, 48, 46, 255), "base", 3, only=m)
    return m


def oak(seed):
    rnd = random.Random(seed)
    cv = Canvas(72, 80)
    tm = trunk(cv, (36, 79), (36, 48), 17, 10, rnd=rnd, moss=True)
    branches(cv, (36, 50), [(-16, -20, 4.0), (15, -22, 4.0), (-2, -30, 3.6)])
    j = lambda v, s=2.0: v + rnd.uniform(-s, s)
    lobes = [(j(20), j(20), 14, 12), (j(52), j(19), 15, 12), (j(36), j(12), 16, 11),      # back row
             (j(12.5), j(38), 10, 10.5), (j(59.5), j(37), 10, 10.5),
             (j(24), j(30), 15, 13), (j(48), j(29), 15, 13), (j(36), j(24), 14, 11),
             (j(22), j(47), 13, 10), (j(50), j(47), 13, 10), (j(36), j(45), 13, 11)]
    crown = clump(cv, lobes, OAK_LEAF, rnd, back=3)
    crown_shadow(cv, crown, tm, 52, BARK[0])
    return cv.img


def birch(seed):
    rnd = random.Random(seed)
    cv = Canvas(56, 88)
    tm = trunk(cv, (28, 87), (28, 40), 8, 5.5, colors=BIRCH_BARK, line=(70, 66, 64, 255), marks=8, rnd=rnd)
    branches(cv, (28, 44), [(-10, -16, 2.4), (10, -18, 2.4)], colors=BIRCH_BARK, line=(70, 66, 64, 255))
    j = lambda v, s=1.6: v + rnd.uniform(-s, s)
    lobes = [(j(28), j(14), 12, 10), (j(18), j(30), 10, 11), (j(39), j(28), 10, 11),       # back row
             (j(28), j(26), 12, 11), (j(17), j(46), 10, 10), (j(40), j(45), 10, 10), (j(28), j(52), 12, 10)]
    crown = clump(cv, lobes, BIRCH_LEAF, rnd, back=3)
    crown_shadow(cv, crown, tm, 60, BIRCH_BARK[0])
    return cv.img


def pine(seed):
    rnd = random.Random(seed)
    cv = Canvas(56, 96)
    trunk(cv, (28, 95), (28, 76), 9, 7, roots=True)
    tiers = 5
    for k in range(tiers):                                            # bottom tier first: each skirt drapes over the one below
        t = k / (tiers - 1)
        y_bottom = 84 - k * 16 + rnd.uniform(-1, 1)
        half = 23 - t * 15 + rnd.uniform(-1, 1)
        top = y_bottom - 28 + t * 4 if k < tiers - 1 else 2
        pts = [(28, top)]
        n = 5
        for i in range(n + 1):                                        # a scalloped skirt: drooping points
            u = i / n
            x = 28 + half * (u * 2 - 1)
            pts.append((x, y_bottom + (1.6 if i % 2 else -0.6) - abs(u - 0.5) * 2.0))
        m = cv.poly([pts[0]] + pts[1:][::-1][::-1])
        cv.paint(m, PINE_LEAF, "base", radius=1.6, line_color=LEAF_LINE, bias=0.06)
        for side in (-1, 1):                                          # needle strokes and a lit edge
            cv.curve([(28 + side * half * 0.15, top + 6), (28 + side * half * 0.6, y_bottom - 2)], PINE_LEAF[1], "base", 2, only=m)
        cv.curve([(28 - 1, top + 4), (28 - half * 0.55, y_bottom - 4)], PINE_LEAF[4], "base", 3, only=m)
        for i in range(int(half * 1.3)):                              # needle tufts: down-pointing flicks, lit left, shaded right
            u = rnd.uniform(-0.9, 0.9)
            v = rnd.uniform(0.25, 0.95)
            nx, ny = 28 + half * u * v, top + (y_bottom - top) * v - 1.2
            col = PINE_LEAF[3] if u < -0.2 else PINE_LEAF[1] if u > 0.3 else PINE_LEAF[2]
            cv.curve([(nx - 0.9, ny - 0.9), (nx, ny + 0.6), (nx + 0.9, ny - 0.9)], col, "base", 2, only=m)
        cv.curve([(28 - half * 0.9, y_bottom - 0.6), (28 + half * 0.9, y_bottom - 0.6)], PINE_LEAF[0], "base", 2, only=m)   # the skirt's shaded hem
    return cv.img


def dead_tree(seed):
    rnd = random.Random(seed)
    cv = Canvas(44, 64)
    m = trunk(cv, (24, 63), (24, 18), 13, 7, colors=DEAD_BARK, line=(36, 30, 32, 255))
    for k, (y, side, ln) in enumerate(((36, -1, 17), (28, 1, 18), (22, -1, 11), (44, 1, 13))):   # bare branches
        start = (24 + side * 2.5, y)
        mid = (24 + side * ln * 0.55, y - ln * 0.4 + rnd.uniform(-1, 1))
        tip = (24 + side * ln, y - ln * 0.85 + rnd.uniform(-1.5, 1.5))
        br = cv.strip([start, mid, tip], [4.5, 2.8, 0.8], steps=4)
        cv.paint(br, DEAD_BARK, "base", radius=0.6, line_color=(36, 30, 32, 255), outside=m)
        twig = cv.strip([mid, add(mid, (side * 2.5, -3.5))], [1.2, 0.4], steps=2)
        cv.paint(twig, DEAD_BARK, "base", radius=0.4, line_color=(36, 30, 32, 255))
    top = cv.poly([(19.5, 20), (22, 11), (24.5, 16), (26.5, 13), (28.5, 20)])                 # a broken top
    cv.paint(top, DEAD_BARK, "base", radius=0.5, line_color=(36, 30, 32, 255), outside=m)
    for _ in range(4):                                                # embers still glowing in the cracks
        y = rnd.uniform(26, 56)
        x = 24 + rnd.uniform(-3, 3)
        cv.curve([(x, y), (x + rnd.uniform(-1, 1), y + 2.4)], GLOW, "base", 3, only=m)
        cv.curve([(x, y + 0.4), (x, y + 1.6)], GLOW_HI, "base", 1, only=m)
    return cv.img


def berry_bush(seed, berries=True):
    rnd = random.Random(seed)
    cv = Canvas(32, 32)
    j = lambda v, s=1.0: v + rnd.uniform(-s, s)
    lobes = [(j(11), j(16), 8, 7), (j(21), j(15), 8, 7), (j(7), j(24), 6.5, 6.5), (j(25), j(24), 6.5, 6.5), (j(16), j(23), 10, 8)]
    whole = clump(cv, lobes, BUSH_LEAF, rnd, back=2)
    if berries:
        for _ in range(12):
            x, y = j(16, 11), j(21, 8)
            r = rnd.uniform(1.4, 1.9)
            b = both(cv.ellipse(x, y, r, r), whole)
            cv.paint(b, BERRY, "base", radius=0.3, line_color=(90, 20, 30, 255))
            cv.fill(cv.ellipse(x - r * 0.35, y - r * 0.35, r * 0.28, r * 0.28), WHITE, "base", only=b)
    return cv.img


def boulder(seed, moss=False):
    rnd = random.Random(seed)
    cv = Canvas(32, 32)
    def lump(cx, rx, ry):
        pts = []
        for k in range(11):                                           # rounded, slightly faceted
            a = math.pi + k / 10 * math.pi
            f = 1.0 if k % 3 else 0.93
            pts.append((cx + math.cos(a) * (rx + rnd.uniform(-0.6, 0.6)), 31 + math.sin(a) * (ry + rnd.uniform(-0.6, 0.6)) * f))
        return cv.poly(pts + [(cx + rx, 32), (cx - rx, 32)])
    back = lump(18 + rnd.uniform(-1, 1), 12, 17)
    cv.paint(back, STONE, "base", radius=2.0, line_color=STONE_LINE, bias=0.06, depth=0.3)
    cv.curve([(11, 20), (16, 15.5), (22, 15)], STONE[4], "base", 3, only=back)           # the lit ridge
    front = lump(10, 9, 10)
    cv.paint(front, STONE, "base", radius=1.6, line_color=STONE_LINE, bias=0.1, depth=0.3)
    cv.curve([(4, 27), (7, 23.5), (11, 22.5)], STONE[4], "base", 3, only=front)
    cv.curve([(22, 21), (24.5, 25), (23.5, 30)], STONE[1], "base", 2, only=back)       # a crack
    if moss:
        cap = union(both(cv.ellipse(19, 15.5, 8, 3), back), both(cv.ellipse(9, 22, 5, 2), front))
        cv.paint(cap, MOSS, "base", radius=0.6, line=False, bias=0.1)
    return cv.img


def menhir(seed, glowing):
    rnd = random.Random(seed)
    cv = Canvas(32, 32)
    stone = cv.poly([(8.5, 31.5), (8, 14), (10.5, 6), (16, 2.5), (21.5, 5), (24.5, 14), (24, 31.5)])
    cv.paint(stone, STONE, "base", radius=1.6, line_color=STONE_LINE, bias=0.04, depth=0.3)
    cv.curve([(11, 28), (10.8, 12), (13.5, 6)], STONE[4], "base", 3, only=stone)
    rune = RUNE_BLUE if glowing else STONE[0]
    for k, (y, kind) in enumerate(((11, 0), (17, 1), (23, 2))):        # carved runes: Tiwaz, a cross, a zigzag
        x = 16.2
        if kind == 0:
            strokes = [[(x, y - 2.4), (x, y + 2.6)], [(x, y - 2.4), (x - 2, y)], [(x, y - 2.4), (x + 2, y)]]
        elif kind == 1:
            strokes = [[(x, y - 2.4), (x, y + 2.4)], [(x - 2, y - 1.2), (x + 2, y + 1.2)]]
        else:
            strokes = [[(x - 2, y - 2.4), (x + 2, y - 0.6), (x - 2, y + 1.2), (x + 2, y + 2.8)]]
        for st in strokes:
            cv.curve(st, rune, "base", 3 if glowing else 2, only=stone)
            if glowing:
                cv.curve(st, RUNE_BLUE_HI, "base", 1, only=stone)
    base = both(union(cv.ellipse(10.5, 31, 4.5, 2.6), cv.ellipse(21, 31.2, 5, 2.4)), cv.poly([(4, 26), (28, 26), (28, 32), (4, 32)]))
    cv.paint(base, MOSS, "base", radius=0.5, line_color=LEAF_LINE)
    return cv.img


def moonpetal():
    cv = Canvas(32, 32)
    stem_c = ramp((70, 120, 80), 3)
    for (x, y, h) in ((11, 14, 12), (20, 11, 15), (16, 18, 9)):
        stem = cv.strip([(x, y + 2), (x - 0.6 + (x % 3) * 0.4, 30.5)], [1.0, 1.0], steps=2)
        cv.paint(stem, stem_c, "base", radius=0.3, line_color=LEAF_LINE)
        glow = cv.ellipse(x, y, 4.6, 4.6)
        cv.fill(glow, (160, 200, 255, 120), "base")
        petals = cv.empty()
        for k in range(5):
            a = -math.pi / 2 + k / 5 * math.tau
            petals = union(petals, cv.ellipse(x + math.cos(a) * 2.0, y + math.sin(a) * 2.0, 1.6, 1.6))
        cv.paint(petals, ramp((196, 214, 255), 4, light=1.15), "base", radius=0.4, line_color=(54, 66, 120, 255))
        cv.fill(cv.ellipse(x, y, 0.8, 0.8), (255, 246, 200, 255), "base")
    for (x0, x1) in ((8, 13), (19, 24)):                              # leaves at the foot
        leaf = cv.poly([(x0, 31), ((x0 + x1) / 2 - 0.5, 25.5), (x1, 31)])
        cv.paint(leaf, stem_c, "base", radius=0.4, line_color=LEAF_LINE)
    return cv.img


def emberroot(bare=False):
    rnd = random.Random(21)
    cv = Canvas(32, 32)
    mound = cv.ellipse(16, 30.5, 8, 2.6)
    cv.paint(mound, SOIL, "base", radius=0.6, line_color=WOOD_LINE)
    if bare:
        stub = cv.strip([(16, 30), (16.3, 27)], [2.2, 1.6], steps=2)
        cv.paint(stub, ramp((120, 60, 44), 3), "base", radius=0.3, line_color=WOOD_LINE)
        return cv.img
    leaves = ramp((186, 58, 44), 5, light=1.35)
    stalk = ramp((120, 60, 44), 3)
    for k, ang in enumerate((-58, 52, -30, 26, -6)):                  # broad leaves, outer ones first
        a = math.radians(ang - 90)
        ln = 22 - abs(ang) * 0.1 + rnd.uniform(-1, 1)
        base_ = (16, 29)
        tip = (16 + math.cos(a) * ln * 0.75, 29 + math.sin(a) * ln)
        leaf = cv.strip([base_, lerp(base_, tip, 0.25), lerp(base_, tip, 0.6), tip], [1.4, 4.2, 4.6, 0.2], steps=6)
        cv.paint(leaf, leaves, "base", radius=0.6, line_color=(70, 20, 22, 255), bias=0.06)
        cv.curve([lerp(base_, tip, 0.15), lerp(base_, tip, 0.85)], leaves[1], "base", 2, only=leaf)     # the vein
        cv.curve([lerp(base_, tip, 0.7), lerp(base_, tip, 0.93)], GLOW, "base", 2, only=leaf)           # ember tips
    root = both(cv.ellipse(16, 30, 3.2, 2.2), cv.poly([(10, 27), (22, 27), (22, 32), (10, 32)]))
    cv.paint(root, stalk, "base", radius=0.4, line_color=WOOD_LINE)
    return cv.img


def wild_flax(bare=False):
    rnd = random.Random(31)
    cv = Canvas(32, 32)
    stem_c = ramp((96, 140, 76), 3)
    if bare:
        for k in range(7):
            x = 11 + k * 1.6 + rnd.uniform(-0.4, 0.4)
            s = cv.strip([(x, 31), (x + 0.2, 27.5 + rnd.uniform(-0.8, 0.8))], [0.8, 0.7], steps=2)
            cv.paint(s, ramp((170, 156, 96), 3), "base", radius=0.3, line_color=WOOD_LINE)
        return cv.img
    for k in range(7):
        x = 10 + k * 2.0 + rnd.uniform(-0.6, 0.6)
        top = (x + rnd.uniform(-2, 2), 12 + rnd.uniform(-2, 3))
        s = cv.strip([(x, 31), lerp((x, 31), top, 0.5), top], [0.8, 0.7, 0.6], steps=3)
        cv.paint(s, stem_c, "base", radius=0.3, line_color=LEAF_LINE)
        flower = cv.empty()
        for i in range(5):
            a = -math.pi / 2 + i / 5 * math.tau
            flower = union(flower, cv.ellipse(top[0] + math.cos(a) * 1.1, top[1] + math.sin(a) * 1.1, 1.0, 1.0))
        cv.paint(flower, ramp((96, 138, 230), 4, light=1.25), "base", radius=0.3, line_color=(36, 44, 96, 255))
        cv.fill(cv.ellipse(top[0], top[1], 0.45, 0.45), (250, 236, 150, 255), "base")
    return cv.img


def sprites():
    out = {}
    for i, sfx in enumerate(("", "_v2", "_v3")):
        out["things/oak" + sfx] = oak(10 + i)
        out["things/birch" + sfx] = birch(30 + i)
        out["things/pine" + sfx] = pine(20 + i)
        out["things/berry_bush" + sfx] = berry_bush(40 + i)
        out["things/berry_bush_bare" + sfx] = berry_bush(40 + i, berries=False)
        out["things/boulder" + sfx] = boulder(1500 + i, moss=(i == 2))
    for i, sfx in enumerate(("", "_v2")):
        out["things/dead_tree" + sfx] = dead_tree(40 + i)
    out["things/standing_stone"] = menhir(6, glowing=False)
    out["things/ley_stone"] = menhir(5, glowing=True)
    out["things/moonpetal"] = moonpetal()
    out["things/emberroot"] = emberroot()
    out["things/emberroot_bare"] = emberroot(bare=True)
    out["things/wild_flax"] = wild_flax()
    out["things/wild_flax_bare"] = wild_flax(bare=True)
    return out


def generate():
    for name, img in sprites().items():
        path = os.path.join(ROOT, name + ".png")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        img.save(path)
    print(f"nature: wrote {len(sprites())} sprites")


if __name__ == "__main__":
    if "--preview" in sys.argv:
        imgs = []
        for name, img in sprites().items():
            bg = Image.new("RGBA", (72 * K, 96 * K), (96, 120, 80, 255))
            bg.alpha_composite(img, ((bg.width - img.width) // 2, bg.height - img.height))
            imgs.append(bg)
        out = os.path.join(os.path.dirname(__file__), "..", ".cache", "nature.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        contact_sheet(imgs, out, scale=1, columns=8, bg=(40, 40, 40, 255), pad=2)
        print(out)
    else:
        generate()
