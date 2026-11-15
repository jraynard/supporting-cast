#!/usr/bin/env python3
"""The healer's shrine, furnished (docs/ART.md): the rune altar, the herb rack, the linen basket, the
wash basin and the candles on its walls, the straw pallet the wounded lie on, and the shrine's own
icon; and the cottages' bread crock. Face textures for their box models (``props/``) and their
toolbar icons (``buildings/``).

Norse HD-2D like norse_smithy and station_art (whose palettes and helpers it uses): hue-shifted ramps
lit from the top left, ordered texture, a selective outline, glow colours kept bright. Writes only its
own files. Run alone (``python3 tools/shrine_art.py``) or from ``gen_placeholder_art.py``.
"""
import math
import os
import random

from PIL import Image

from norse_art import ramp, mask_of, shade_shape, shade_over, selective_outline, bayer
from norse_buildings import ROOT, save, canvas, put, clampi, board, graded, WOODS, IRON, LINEN, STRAW, MADDER, WOAD, OAT, LEATHER, S, prop_basket
from norse_glow import GLOW, HOT, CORE
from norse_stone import ROCKS, rock_ramp

STONE = rock_ramp(graded(ROCKS["rough"]["base"]))
GREY = ramp((126, 128, 136), 6, main=3, dark=0.5, light=1.4)
WATER = ramp((70, 118, 146), 5, main=2, dark=0.55, light=1.55)
SAGE = ramp((140, 160, 120), 4, dark=0.55, light=1.3)
LEAF = ramp((96, 140, 72), 5, dark=0.5, light=1.4)
DRIED = ramp((150, 132, 84), 4, dark=0.55, light=1.3)
BLOOM = ramp((214, 196, 96), 3, dark=0.7, light=1.15)
GLAZE = ramp((128, 82, 52), 6, main=3, dark=0.5, light=1.5, warm=14)   # a brown salt-glazed stoneware
CREAM = ramp((214, 196, 156), 4, main=2, dark=0.7, light=1.1)
WAX = ramp((236, 224, 190), 3, main=1, dark=0.8, light=1.06)
ARCANE = ramp((150, 132, 226), 4, main=2, dark=0.6, light=1.35, cool=10)
OUTLINE_KEEP = (GLOW, HOT, CORE)

# The six runes cut in the altar (as shapes, not words): strokes in a 6x8 cell.
RUNES = [
    [((1, 0), (1, 7)), ((1, 2), (4, 0))],                  # Thread
    [((1, 0), (1, 7)), ((1, 0), (4, 3)), ((4, 3), (1, 5))],  # Flesh
    [((0, 7), (2, 0)), ((2, 0), (4, 7))],                  # Ember
    [((2, 0), (2, 7)), ((0, 2), (4, 6)), ((4, 2), (0, 6))],  # Rime
    [((0, 0), (4, 7)), ((4, 0), (0, 7))],                  # Salt
    [((2, 0), (2, 7))],                                    # Still
]


def line(img, a, b, c):
    """A one-pixel line from a to b."""
    (x0, y0), (x1, y1) = a, b
    n = max(abs(x1 - x0), abs(y1 - y0), 1)
    for k in range(n + 1):
        put(img, round(x0 + (x1 - x0) * k / n), round(y0 + (y1 - y0) * k / n), c)


def carve(img, ox, oy, rune, deep, lit):
    """A rune cut into stone: the cut dark, its lower lip catching the light."""
    for a, b in rune:
        line(img, (ox + a[0], oy + a[1]), (ox + b[0], oy + b[1]), deep)
        line(img, (ox + a[0] + 1, oy + a[1] + 1), (ox + b[0] + 1, oy + b[1] + 1), lit)
        line(img, (ox + a[0], oy + a[1]), (ox + b[0], oy + b[1]), deep)


# ---------------------------------------------------------------- face textures

def prop_crock_side():
    """Brown salt-glazed stoneware: a glossy body, a cream band at the shoulder, drips of glaze."""
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            v = 2.6 + bayer(x, y) * 0.5 + math.sin(x / S * math.pi) * 1.2 - y / S * 0.8
            px[x, y] = GLAZE[clampi(v, 6)]
    for y in range(6, 10):                                         # the cream band
        for x in range(S):
            put(img, x, y, CREAM[2 if y in (7, 8) else 1])
    rnd = random.Random(61)
    for _ in range(6):                                             # drips of glaze over it
        x, y0 = rnd.randrange(S), 9
        for y in range(y0, y0 + rnd.randint(2, 5)):
            put(img, x, y, GLAZE[2])
    for x in range(3, 9):                                          # the gloss
        put(img, x, 14, GLAZE[5]); put(img, x + 1, 15, GLAZE[4])
    return img


def prop_crock_lid():
    """The crock's lid from above: stoneware rings round a knob."""
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            r = math.hypot(x - 15.5, y - 15.5)
            v = 2.8 + bayer(x, y) * 0.4 - (x + y - 31) / 40 + (0.7 if int(r) % 6 == 0 else 0)
            px[x, y] = CREAM[clampi(v - 1, 4)] if r < 5 else GLAZE[clampi(v, 6)]
    return img


def prop_herb_bundles():
    """Bunches of herbs hung head down to dry: green, grey-green and straw-dry, tied with madder thread."""
    img = canvas()
    rnd = random.Random(67)
    kinds = (LEAF, SAGE, DRIED, SAGE)
    for i, x0 in enumerate(range(1, S, 8)):
        herb = kinds[i % len(kinds)]
        for _ in range(26):                                        # stalks and leaves fanning down
            x = x0 + 3 + rnd.uniform(-1, 1)
            dx = rnd.uniform(-0.25, 0.25)
            for y in range(4, rnd.randint(20, 29)):
                put(img, round(x + dx * (y - 4)), y, herb[rnd.randint(1, len(herb) - 1)] if (y + i) % 3 else herb[0])
        if i % 2:
            for _ in range(5):                                     # a few dried flower heads
                put(img, x0 + rnd.randint(0, 6), rnd.randint(18, 27), BLOOM[rnd.randint(0, 2)])
        for x in range(x0 + 1, x0 + 6):                            # the tie, and the string up to the bar
            put(img, x, 4, MADDER[2]); put(img, x, 5, MADDER[1])
        for y in range(0, 4):
            put(img, x0 + 3, y, OAT[1])
    return img


def prop_altar_stone():
    """Grey stone cut with the six runes of mending, two rows of three."""
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            v = 3 + bayer(x, y) * 0.7 + math.sin(x * 0.7 + y * 0.3) * 0.25 - y / S * 0.6
            px[x, y] = GREY[clampi(v, 6)]
    for x in range(S):
        put(img, x, 0, GREY[5]); put(img, x, S - 1, GREY[0])
    for k, rune in enumerate(RUNES):
        carve(img, 4 + (k % 3) * 9, 5 + (k // 3) * 13, rune, GREY[0], GREY[5])
    return img


def prop_altar_cloth():
    """The altar's linen cloth from above, a woad band woven along its edge."""
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            v = 2 + (0.5 if (x % 2 == 0) ^ (y % 2 == 0) else -0.3)
            px[x, y] = LINEN[clampi(v, 4)]
    for y in (3, 4, 27, 28):
        for x in range(S):
            put(img, x, y, WOAD[3] if (x // 2) % 2 else WOAD[2])
    return img


def prop_linen_rolls():
    """Rolled bandages packed in a basket, seen from above: the spiral ends of each roll."""
    img = prop_basket()
    for cx, cy in ((8, 9), (19, 8), (27, 13), (12, 20), (23, 23), (5, 26)):
        m = mask_of(img.size, lambda d, cx=cx, cy=cy: d.ellipse([cx - 5, cy - 5, cx + 5, cy + 5], fill=255))
        shade_shape(img, m, LINEN, radius=1.2, slope=1.2)
        for a in range(0, 540, 30):                                # the roll's spiral
            r = 0.5 + a / 540 * 4
            put(img, round(cx + r * math.cos(math.radians(a))), round(cy + r * math.sin(math.radians(a))), LINEN[0])
    return selective_outline(img, strength=0.2)


def prop_basin_water():
    """Clean water in a stone basin: still, pale, with a glint."""
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            r = math.hypot(x - 16, y - 16)
            v = 2.4 + bayer(x, y) * 0.4 + math.sin(r * 0.8) * 0.35
            px[x, y] = WATER[clampi(v, 5)]
    for x, y in ((9, 9), (10, 9), (11, 10), (21, 20)):
        put(img, x, y, WATER[4])
    return img


# ---------------------------------------------------------------- the shrine's walls (its kit, shrine_stone)

LIME = ramp((206, 200, 184), 6, main=3, dark=0.62, light=1.12, cool=8, warm=6)   # pale limestone, lime-washed
VIOLET = ramp((118, 92, 176), 4, main=2, dark=0.55, light=1.35, cool=6)          # the runes' paint: the gift's colour


def wall_shrine():
    """
    Pale dressed limestone in courses, a level tall (64px, so it shows once up a wall), and a band
    of the runes cut and painted violet round it at eye height.
    """
    h = 64
    img = canvas(S, h)
    px = img.load()
    for y in range(h):
        course = y // 8
        off = 0 if course % 2 == 0 else 8
        for x in range(S):
            bx, by = (x + off) % 16, y % 8
            if by == 7 or bx == 15:
                px[x, y] = LIME[0]
                continue
            v = 3.2 + bayer(x, y) * 0.5 - (bx + by) / 60 + (0.8 if by == 0 or bx == 0 else -0.6 if by == 6 or bx == 14 else 0)
            if (x * 7 + y * 5) % 23 == 0:
                v -= 0.9
            px[x, y] = LIME[clampi(v, 6)]
    for x in range(S):                                             # the rune band: a dressed course
        for y in range(16, 28):
            put(img, x, y, LIME[4 if y == 16 else 1 if y == 27 else 3])
    for k in range(4):
        carve(img, 2 + k * 8, 18, RUNES[(k * 2 + 1) % len(RUNES)], VIOLET[1], VIOLET[3])
    return img


def wall_shrine_plain():
    """The same limestone without the rune band, 32px square: what fills the gables."""
    return wall_shrine().crop((0, 32, S, 64))


def gable_shrine():
    """The gable: dark oak boards, alpha-cut to the gable's triangle, with a carved sun disc of the gift's violet."""
    mask = Image.open(os.path.join(ROOT, "buildings", "gable_boards.png")).convert("RGBA").getchannel("A")
    img = canvas()
    w = WOODS["_oak"]
    rnd = random.Random(79)
    for x0 in range(0, S, 4):
        board(img, x0, 0, x0 + 3, S - 1, w, rnd, False, tone=2.2)
    disc = mask_of(img.size, lambda d: d.ellipse([18, 16, 28, 26], fill=255))
    shade_shape(img, disc, VIOLET, radius=1.0, slope=1.2)
    for a in range(0, 360, 45):                                    # its rays
        put(img, round(23 + 7 * math.cos(math.radians(a))), round(21 + 7 * math.sin(math.radians(a))), VIOLET[3])
    img.putalpha(Image.eval(mask, lambda a: a))
    return img


def lintel_rune():
    """The door's lintel: dark oak with the rune of Thread cut in it, painted."""
    img = canvas()
    w = WOODS["_oak"]
    rnd = random.Random(83)
    board(img, 0, 0, S - 1, S - 1, w, rnd, True, tone=2.0)
    for ox in (4, 13, 22):
        carve(img, ox, 12, RUNES[0], VIOLET[1], VIOLET[3])
    return img


# ---------------------------------------------------------------- toolbar icons

def icon_shrine():
    """The shrine: a turf-roofed hut, a rune glowing over its door."""
    img = canvas()
    w = WOODS["_pine"]
    rnd = random.Random(71)
    for x0 in range(5, 27, 4):                                     # log walls
        board(img, x0, 14, x0 + 3, 29, w, rnd, False, tone=2.8 + (x0 % 8 == 5) * 0.3)
    roof = mask_of(img.size, lambda d: d.polygon([(2, 15), (16, 4), (30, 15)], fill=255))
    shade_shape(img, roof, ramp(graded((96, 116, 62)), 5, dark=0.55, light=1.35)[1:], radius=1.2, slope=1.3)
    door = mask_of(img.size, lambda d: d.rectangle([13, 20, 19, 29], fill=255))
    shade_shape(img, door, LEATHER[:4], radius=0.6, slope=1.4)
    carve(img, 14, 9, RUNES[0], ARCANE[3], ARCANE[2])             # the rune of Thread, alight
    return selective_outline(img, keep=OUTLINE_KEEP)


def icon_straw_pallet():
    img = canvas()
    m = mask_of(img.size, lambda d: d.rounded_rectangle([3, 13, 29, 26], radius=4, fill=255))
    shade_shape(img, m, STRAW[1:], radius=1.6, slope=1.2)
    rnd = random.Random(73)
    for _ in range(30):                                            # stalks poking out
        x, y = rnd.randrange(4, 29), rnd.randrange(14, 26)
        put(img, x, y, STRAW[4] if rnd.random() < 0.5 else STRAW[1])
    roll = mask_of(img.size, lambda d: d.rounded_rectangle([4, 11, 12, 20], radius=3, fill=255))
    shade_shape(img, roll, LINEN, radius=1.0, slope=1.2)
    return selective_outline(img)


def icon_herb_rack():
    img = canvas()
    w = WOODS["_oak"]
    for x in (3, 27):                                              # the posts
        for y in range(4, 30):
            put(img, x, y, w.w[2]); put(img, x + 1, y, w.w[1])
    for x in range(3, 29):
        put(img, x, 5, w.w[3]); put(img, x, 6, w.w[1])
    bundles = prop_herb_bundles()
    for y in range(0, 22):
        for x in range(4, 27):
            c = bundles.getpixel((x, y))
            if c[3]:
                put(img, x, y + 6, c)
    return selective_outline(img)


def icon_rune_altar():
    img = canvas()
    face = prop_altar_stone()
    for y in range(12, 30):
        for x in range(4, 28):
            put(img, x, y, face.getpixel((x, (y - 12) * 2 % S)))
    cloth = mask_of(img.size, lambda d: d.polygon([(3, 12), (29, 12), (27, 8), (5, 8)], fill=255))
    shade_shape(img, cloth, LINEN, radius=0.8, slope=1.2)
    for x in range(5, 27):
        put(img, x, 11, WOAD[2])
    for cx in (7, 25):                                             # candle stubs, lit
        for y in range(4, 8):
            put(img, cx, y, WAX[1]); put(img, cx + 1, y, WAX[0])
        put(img, cx, 2, HOT); put(img, cx, 3, GLOW); put(img, cx + 1, 3, CORE)
    return selective_outline(img, keep=OUTLINE_KEEP)


def icon_linen_basket():
    img = canvas()
    weave = prop_basket()
    body = mask_of(img.size, lambda d: d.polygon([(5, 14), (27, 14), (25, 29), (7, 29)], fill=255))
    for y in range(S):
        for x in range(S):
            if body.getpixel((x, y)):
                put(img, x, y, weave.getpixel((x, y)))
    for cx, cy in ((9, 12), (16, 10), (23, 12), (13, 15), (20, 15)):
        m = mask_of(img.size, lambda d, cx=cx, cy=cy: d.ellipse([cx - 4, cy - 4, cx + 4, cy + 4], fill=255))
        shade_shape(img, m, LINEN, radius=1.0, slope=1.2)
        put(img, cx, cy, LINEN[0]); put(img, cx + 1, cy, LINEN[0])
    return selective_outline(img)


def icon_wash_basin():
    img = canvas()
    pillar = mask_of(img.size, lambda d: d.rectangle([12, 18, 20, 30], fill=255))
    shade_shape(img, pillar, STONE[1:], radius=1.0, slope=1.3)
    bowl = mask_of(img.size, lambda d: d.chord([3, 6, 29, 24], 0, 180, fill=255))
    shade_shape(img, bowl, STONE[1:], radius=1.6, slope=1.3)
    water = mask_of(img.size, lambda d: d.ellipse([5, 11, 27, 18], fill=255))
    shade_over(img, water, WATER[1:], STONE[0], radius=1.0, slope=1.2)
    for x, y in ((10, 13), (11, 13), (20, 15)):
        put(img, x, y, WATER[4])
    return selective_outline(img)


def icon_wall_candles():
    img = canvas()
    w = WOODS["_oak"]
    for x in range(3, 29):
        put(img, x, 20, w.w[3]); put(img, x, 21, w.w[2]); put(img, x, 22, w.w[0])
    for x in (7, 24):                                              # the brackets
        for k in range(5):
            put(img, x + (k if x < 16 else -k) // 2, 23 + k, IRON[2])
    for cx, top in ((8, 11), (15, 7), (22, 13)):
        for y in range(top, 20):
            put(img, cx, y, WAX[2]); put(img, cx + 1, y, WAX[1]); put(img, cx + 2, y, WAX[0])
        put(img, cx + 1, top - 1, (60, 50, 40, 255))
        put(img, cx + 1, top - 2, CORE); put(img, cx + 1, top - 3, HOT); put(img, cx, top - 3, GLOW); put(img, cx + 1, top - 4, GLOW)
    return selective_outline(img, keep=OUTLINE_KEEP)


def icon_bread_crock():
    img = canvas()
    body = mask_of(img.size, lambda d: d.polygon([(8, 9), (24, 9), (27, 16), (25, 29), (7, 29), (5, 16)], fill=255))
    shade_shape(img, body, GLAZE[1:], radius=2.0, slope=1.3, depth=0.5)
    for x in range(6, 27):                                         # the cream band at its shoulder
        put(img, x, 13, CREAM[2]); put(img, x, 14, CREAM[1])
    lid = mask_of(img.size, lambda d: d.ellipse([7, 5, 25, 11], fill=255))
    shade_shape(img, lid, CREAM, radius=1.0, slope=1.2)
    knob = mask_of(img.size, lambda d: d.ellipse([14, 3, 18, 7], fill=255))
    shade_shape(img, knob, GLAZE[2:], radius=0.6, slope=1.3)
    for x in range(9, 13):                                         # the gloss
        put(img, x, 18, GLAZE[5])
    return selective_outline(img)


def generate():
    for name, make in (("crock_side", prop_crock_side), ("crock_lid", prop_crock_lid), ("herb_bundles", prop_herb_bundles),
                       ("altar_stone", prop_altar_stone), ("altar_cloth", prop_altar_cloth), ("linen_rolls", prop_linen_rolls),
                       ("basin_water", prop_basin_water)):
        save(make(), "props/" + name)
    for name, make in (("wall_shrine", wall_shrine), ("wall_shrine_plain", wall_shrine_plain), ("gable_shrine", gable_shrine), ("lintel_rune", lintel_rune)):
        save(make(), "buildings/" + name)
    for name, make in (("shrine", icon_shrine), ("straw_pallet", icon_straw_pallet), ("herb_rack", icon_herb_rack),
                       ("rune_altar", icon_rune_altar), ("linen_basket", icon_linen_basket), ("wash_basin", icon_wash_basin),
                       ("wall_candles", icon_wall_candles), ("bread_crock", icon_bread_crock)):
        save(make(), "buildings/" + name)


if __name__ == "__main__":
    generate()
