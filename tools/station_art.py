#!/usr/bin/env python3
"""Art of their own for things that used to borrow someone else's: the inn's stations (cask, keg,
shelf, larder, stew pot, wash tub, counter, serving hatch, fireplace, lantern), the village's barrel,
crate and barn, the whetstone, the empath's meditation stone and herbs, and the funeral pyre. Toolbar
and inventory icons (32x32), plus the model faces the cask, pot and tub wear (``props/iron_band``,
``props/iron_pot``, ``props/washtub_side``, ``props/suds``).

Norse HD-2D like norse_buildings (whose palettes and helpers it uses): hue-shifted ramps lit from the
top left, ordered texture, a selective outline, glow colours kept bright. Writes only its own files.
Run alone (``python3 tools/station_art.py``) or from ``gen_placeholder_art.py``.
"""
import math
import random

from norse_art import ramp, mask_of, shade_shape, shade_over, selective_outline, bayer
from norse_buildings import save, canvas, put, board, graded, WOODS, IRON, BRONZE, LINEN, STRAW, MADDER, S
from norse_glow import GLOW, HOT, CORE, EMBER
from norse_stone import ROCKS, rock_ramp

STONE = rock_ramp(graded(ROCKS["rough"]["base"]))
HONE = ramp((128, 134, 140), 5, dark=0.5, light=1.4)
SLATE = ramp((110, 116, 128), 5, dark=0.5, light=1.4)
WATER = ramp((70, 110, 140), 4, dark=0.55, light=1.5)
SUDS = ramp((226, 232, 236), 3, main=1, dark=0.8, light=1.05)
LEAF = ramp((96, 140, 72), 5, dark=0.5, light=1.4)
SAGE = ramp((140, 160, 120), 4, dark=0.55, light=1.3)
CLAY = ramp((176, 110, 70), 4, dark=0.55, light=1.3)
STEW = ramp((150, 92, 50), 4, dark=0.55, light=1.35)
ALE = ramp((196, 132, 44), 4, dark=0.55, light=1.3)
OUTLINE_KEEP = (GLOW, HOT, CORE, EMBER)


def hoops(img, xs, y0, y1):
    """Iron bands across a cask or tub, a lit top edge on each."""
    for x in xs:
        for y in range(y0, y1):
            put(img, x, y, IRON[2]); put(img, x + 1, y, IRON[1])
        put(img, x, y0, IRON[4])


def flame(img, cx, base, h, w=3):
    """A small hearth flame: ember, glow, hot heart, core."""
    for ramp_, f in ((EMBER, 1.0), (GLOW, 0.8), (HOT, 0.55), (CORE, 0.28)):
        m = mask_of(img.size, lambda d, f=f: d.polygon([(cx - w * f, base), (cx, base - h * f - (1 - f) * 2), (cx + w * f, base)], fill=255))
        img.paste(ramp_, mask=m)


def cask_body(img, box, wood, rnd, staves=True):
    """A barrel seen side-on (lying): an ellipse of staves, the end grain at the left."""
    x0, y0, x1, y1 = box
    m = mask_of(img.size, lambda d: d.rounded_rectangle(box, radius=(y1 - y0) // 2 - 1, fill=255))
    shade_shape(img, m, wood.w[1:6], radius=1.6, slope=1.2,
                tex=(lambda x, y: -0.6 if staves and (y - y0) % 4 == 0 else 0))
    end = mask_of(img.size, lambda d: d.ellipse([x0, y0, x0 + (y1 - y0) // 2 + 1, y1], fill=255))
    shade_shape(img, end, wood.end, radius=1.0, slope=-0.6, tex=lambda x, y: -0.5 if (x - x0 + y) % 3 == 0 else 0)


# ---------------------------------------------------------------- the inn

def icon_ale_cask():
    img = canvas()
    w = WOODS["_oak"]
    rnd = random.Random(11)
    for x in (8, 22):                                                 # the cradle
        m = mask_of(img.size, lambda d, x=x: d.polygon([(x - 4, 29), (x + 4, 29), (x + 3, 22), (x - 3, 22)], fill=255))
        shade_shape(img, m, w.w[0:5], radius=0.8, slope=1.4)
    cask_body(img, (3, 7, 29, 25), w, rnd)
    hoops(img, (11, 20), 8, 25)
    tap = mask_of(img.size, lambda d: d.rectangle([28, 17, 31, 19], fill=255))
    shade_shape(img, tap, BRONZE, radius=0.4, slope=1.4)
    put(img, 30, 20, BRONZE[1]); put(img, 30, 21, ALE[2])
    return selective_outline(img)


def icon_keg():
    img = canvas()
    w = WOODS["_oak"]
    m = mask_of(img.size, lambda d: d.polygon([(9, 6), (23, 6), (25, 16), (23, 28), (9, 28), (7, 16)], fill=255))
    shade_shape(img, m, w.w[1:6], radius=1.6, slope=1.2, tex=lambda x, y: -0.6 if x % 4 == 0 else 0)
    top = mask_of(img.size, lambda d: d.ellipse([9, 4, 23, 9], fill=255))
    shade_shape(img, top, w.end, radius=1.0, slope=-0.8)
    for y in (10, 23):
        for x in range(8, 25):
            if m.getpixel((x, y)):
                put(img, x, y, IRON[2]); put(img, x, y + 1, IRON[1])
    shade_shape(img, mask_of(img.size, lambda d: d.ellipse([14, 5, 18, 8], fill=255)), BRONZE, radius=0.4)
    return selective_outline(img)


def icon_shelf():
    img = canvas()
    w = WOODS[""]
    rnd = random.Random(12)
    for x in (3, 26):
        board(img, x, 3, x + 3, 30, w, rnd, False, tone=3.0)
    for y in (12, 22):
        board(img, 3, y, 29, y + 3, w, rnd, True, tone=3.4)
    for x in (7, 15):                                                 # tankards on the top shelf
        m = mask_of(img.size, lambda d, x=x: d.rectangle([x, 5, x + 5, 11], fill=255))
        shade_shape(img, m, w.w[2:6], radius=0.6, slope=1.4, tex=lambda x, y: -0.5 if x % 2 == 0 else 0)
        put(img, x + 6, 7, w.w[1]); put(img, x + 6, 8, w.w[1]); put(img, x + 6, 9, w.w[1])
    for x in (6, 15, 21):                                             # bowls below
        m = mask_of(img.size, lambda d, x=x: d.chord([x, 15, x + 6, 23], 0, 180, fill=255))
        shade_shape(img, m, CLAY, radius=0.6, slope=1.3)
    return selective_outline(img)


def icon_larder():
    img = canvas()
    w = WOODS["_pine"]
    rnd = random.Random(13)
    for y0 in (14, 20):
        board(img, 3, y0, 29, y0 + 5, w, rnd, True, tone=3.0)
    for x in (3, 26):
        board(img, x, 10, x + 3, 29, w, rnd, False, tone=2.6)
    for (x, c) in ((6, CLAY), (16, LINEN)):                           # a crock and a sack on top
        m = mask_of(img.size, lambda d, x=x: d.rounded_rectangle([x, 3, x + 9, 14], radius=3, fill=255))
        shade_shape(img, m, c, radius=1.2, slope=1.3)
    put(img, 20, 3, STRAW[1]); put(img, 21, 2, STRAW[2])
    for x in range(5, 27, 4):                                         # cabbages and roots peeking from the slats
        put(img, x, 19, LEAF[3]); put(img, x + 1, 19, LEAF[2])
    return selective_outline(img)


def icon_stew_pot():
    img = canvas()
    for a, z in (((7, 30), (11, 20)), ((25, 30), (21, 20)), ((16, 31), (16, 22))):   # the trivet
        m = mask_of(img.size, lambda d, a=a, z=z: d.line([a, z], fill=255, width=2))
        shade_shape(img, m, IRON, radius=0.4, slope=1.4)
    pot = mask_of(img.size, lambda d: d.chord([4, 6, 28, 28], 0, 180, fill=255))
    shade_shape(img, pot, IRON, radius=1.8, slope=1.3, depth=0.5)
    rim = mask_of(img.size, lambda d: d.ellipse([4, 13, 28, 19], fill=255))
    shade_shape(img, rim, IRON[1:], radius=0.6, slope=1.2)
    stew = mask_of(img.size, lambda d: d.ellipse([6, 14, 26, 18], fill=255))
    shade_shape(img, stew, STEW, radius=0.8, slope=-0.8, tex=lambda x, y: 0.6 if (x * 3 + y) % 7 == 0 else 0)
    for x, y in ((11, 9), (16, 6), (21, 9)):                          # steam
        put(img, x, y, (232, 232, 228, 200)); put(img, x + 1, y - 1, (232, 232, 228, 150)); put(img, x, y - 3, (232, 232, 228, 110))
    return selective_outline(img)


def icon_wash_tub():
    img = canvas()
    w = WOODS["_pine"]
    m = mask_of(img.size, lambda d: d.polygon([(3, 12), (29, 12), (26, 29), (6, 29)], fill=255))
    shade_shape(img, m, w.w[1:6], radius=1.4, slope=1.3, tex=lambda x, y: -0.6 if x % 4 == 1 else 0)
    for y in (16, 25):
        for x in range(3, 30):
            if m.getpixel((x, y)):
                put(img, x, y, IRON[2])
    water = mask_of(img.size, lambda d: d.ellipse([4, 9, 28, 15], fill=255))
    shade_shape(img, water, WATER, radius=1.0, slope=-1.0)
    for x, y in ((9, 11), (13, 12), (20, 11), (23, 12), (17, 10)):    # suds
        shade_shape(img, mask_of(img.size, lambda d, x=x, y=y: d.ellipse([x - 2, y - 1, x + 2, y + 2], fill=255)), SUDS, radius=0.6)
    brush = mask_of(img.size, lambda d: d.line([(22, 3), (27, 11)], fill=255, width=2))
    shade_shape(img, brush, WOODS[""].w[2:6], radius=0.4)
    shade_shape(img, mask_of(img.size, lambda d: d.rectangle([18, 1, 24, 4], fill=255)), STRAW, radius=0.4)
    return selective_outline(img)


def icon_bar_counter():
    img = canvas()
    w = WOODS["_oak"]
    rnd = random.Random(14)
    board(img, 1, 13, 31, 17, w, rnd, True, tone=3.6)                 # the top, then the front panels
    for x0 in (2, 12, 22):
        board(img, x0, 17, x0 + 9, 30, w, rnd, False, tone=2.8)
    for x in range(2, 31):
        put(img, x, 29, w.w[0])
    t = mask_of(img.size, lambda d: d.rectangle([20, 5, 26, 13], fill=255))   # a tankard of ale on it
    shade_shape(img, t, WOODS["_pine"].w[2:6], radius=0.5, slope=1.4, tex=lambda x, y: -0.5 if x % 2 == 0 else 0)
    for x in range(20, 27):
        put(img, x, 4, SUDS[2]); put(img, x, 5, SUDS[1])
    put(img, 27, 7, WOODS["_pine"].w[1]); put(img, 27, 8, WOODS["_pine"].w[1]); put(img, 27, 9, WOODS["_pine"].w[1])
    return selective_outline(img)


def icon_serving_hatch():
    img = canvas()
    w = WOODS["_oak"]
    rnd = random.Random(15)
    plaster = ramp((226, 214, 186), 4, main=2, dark=0.7, light=1.06)
    wall = mask_of(img.size, lambda d: d.rectangle([1, 2, 30, 30], fill=255))
    shade_shape(img, wall, plaster, radius=2.0, slope=0.6, tex=lambda x, y: -0.3 if bayer(x, y) > 0.8 else 0)
    hole = mask_of(img.size, lambda d: d.rectangle([7, 7, 24, 19], fill=255))
    img.paste((44, 32, 30, 255), mask=hole)
    board(img, 4, 19, 28, 23, w, rnd, True, tone=3.6)                 # the sill
    for x in (5, 24):
        board(img, x, 5, x + 3, 19, w, rnd, False, tone=2.6)
    board(img, 4, 3, 28, 7, w, rnd, True, tone=3.0)
    bowl = mask_of(img.size, lambda d: d.chord([11, 14, 21, 22], 0, 180, fill=255))
    shade_shape(img, bowl, CLAY, radius=0.6, slope=1.3)
    for x in range(12, 21):
        put(img, x, 17, STEW[2])
    return selective_outline(img)


def icon_fireplace():
    img = canvas()
    m = mask_of(img.size, lambda d: d.rectangle([2, 4, 29, 30], fill=255))
    shade_shape(img, m, STONE[1:], radius=1.2, slope=1.0, tex=lambda x, y: -1.0 if (y % 6 == 0 or (x + (y // 6) * 4) % 8 == 0) else 0)
    mouth = mask_of(img.size, lambda d: d.chord([8, 11, 24, 37], 180, 360, fill=255))
    img.paste((30, 22, 22, 255), mask=mouth)
    board(img, 1, 2, 31, 6, WOODS["_oak"], random.Random(16), True, tone=3.2)   # the mantel
    for x in (11, 17):
        m = mask_of(img.size, lambda d, x=x: d.rectangle([x - 1, 26, x + 6, 28], fill=255))
        shade_shape(img, m, WOODS["_oak"].bark, radius=0.4)
    flame(img, 16, 27, 11, 5)
    return selective_outline(img, keep=OUTLINE_KEEP)


def icon_lantern():
    img = canvas()
    for y in range(0, 7):                                             # the chain
        put(img, 16, y, IRON[2] if y % 2 else IRON[3])
    cap = mask_of(img.size, lambda d: d.polygon([(10, 11), (16, 6), (22, 11)], fill=255))
    shade_shape(img, cap, IRON, radius=0.6, slope=1.4)
    glass = mask_of(img.size, lambda d: d.rectangle([11, 11, 21, 25], fill=255))
    img.paste((255, 196, 110, 255), mask=glass)
    flame(img, 16, 22, 7, 2)
    for x in (11, 16, 21):
        for y in range(11, 26):
            put(img, x, y, IRON[1])
    base = mask_of(img.size, lambda d: d.rectangle([10, 25, 22, 28], fill=255))
    shade_shape(img, base, IRON, radius=0.5, slope=1.4)
    return selective_outline(img, keep=OUTLINE_KEEP + ((255, 196, 110, 255),))


def icon_wall_torch():
    img = canvas()
    plate = mask_of(img.size, lambda d: d.rectangle([5, 12, 10, 22], fill=255))     # the bracket on the wall
    shade_shape(img, plate, IRON, radius=0.5, slope=1.4)
    arm = mask_of(img.size, lambda d: d.line([(10, 17), (17, 17)], fill=255, width=2))
    shade_shape(img, arm, IRON, radius=0.3)
    stick = mask_of(img.size, lambda d: d.line([(14, 28), (20, 12)], fill=255, width=3))
    shade_shape(img, stick, WOODS[""].w[1:5], radius=0.5, slope=1.4)
    ring = mask_of(img.size, lambda d: d.ellipse([14, 15, 20, 19], fill=255))
    shade_shape(img, ring, IRON, radius=0.4)
    head = mask_of(img.size, lambda d: d.rounded_rectangle([17, 9, 23, 14], radius=2, fill=255))
    shade_shape(img, head, ramp((70, 50, 40), 4), radius=0.6, tex=lambda x, y: -0.5 if (x + y) % 3 == 0 else 0)
    flame(img, 20, 10, 9, 4)
    return selective_outline(img, keep=OUTLINE_KEEP)


# ---------------------------------------------------------------- the village

def icon_barrel():
    img = canvas()
    w = WOODS["_pine"]
    m = mask_of(img.size, lambda d: d.polygon([(8, 4), (24, 4), (27, 16), (24, 29), (8, 29), (5, 16)], fill=255))
    shade_shape(img, m, w.w[1:6], radius=1.8, slope=1.2, tex=lambda x, y: -0.6 if x % 4 == 2 else 0)
    top = mask_of(img.size, lambda d: d.ellipse([8, 2, 24, 7], fill=255))
    shade_shape(img, top, w.end, radius=1.0, slope=-0.8, tex=lambda x, y: -0.4 if x % 4 == 0 else 0)
    for y in (9, 23):
        for x in range(5, 28):
            if m.getpixel((x, y)):
                put(img, x, y, IRON[2]); put(img, x, y + 1, IRON[1])
    return selective_outline(img)


def icon_crate():
    img = canvas()
    w = WOODS[""]
    rnd = random.Random(17)
    for y0 in (6, 12, 18, 24):
        board(img, 4, y0, 28, y0 + 6, w, rnd, True, tone=3.0)
    for x0 in (4, 25):
        board(img, x0, 6, x0 + 3, 30, w, rnd, False, tone=3.6)
    brace = mask_of(img.size, lambda d: d.line([(7, 28), (25, 8)], fill=255, width=3))
    shade_over(img, brace, w.w[2:6], w.w[0], radius=0.6, slope=1.4)
    return selective_outline(img)


def icon_barn():
    img = canvas()
    w = WOODS["_pine"]
    rnd = random.Random(18)
    for x0 in range(4, 28, 4):
        board(img, x0, 14, x0 + 4, 30, w, rnd, False, tone=2.8)
    roof = mask_of(img.size, lambda d: d.polygon([(1, 15), (16, 3), (31, 15), (28, 16), (16, 7), (4, 16)], fill=255))
    shade_shape(img, roof, STRAW, radius=1.0, slope=1.2, tex=lambda x, y: -0.5 if (x + y) % 3 == 0 else 0)
    gable = mask_of(img.size, lambda d: d.polygon([(5, 15), (16, 7), (27, 15)], fill=255))
    shade_shape(img, gable, w.w[1:5], radius=1.0, slope=1.0, tex=lambda x, y: -0.6 if x % 4 == 0 else 0)
    door = mask_of(img.size, lambda d: d.rectangle([11, 18, 21, 30], fill=255))
    shade_shape(img, door, MADDER, radius=0.8, slope=1.2)
    for a, z in (((11, 18), (21, 30)), ((21, 18), (11, 30))):         # the cross brace
        m = mask_of(img.size, lambda d, a=a, z=z: d.line([a, z], fill=255, width=1))
        img.paste(OAT_LINE, mask=m)
    return selective_outline(img)


OAT_LINE = (232, 216, 184, 255)


def icon_pyre():
    img = canvas()
    w = WOODS[""]
    for row, y in enumerate((24, 19, 14, 9)):                         # logs laid crosswise, a layer at a time
        if row % 2 == 0:
            for x0 in (3, 18):
                m = mask_of(img.size, lambda d, x0=x0, y=y: d.rounded_rectangle([x0, y, x0 + 11, y + 4], radius=2, fill=255))
                shade_shape(img, m, w.bark, radius=0.8, slope=1.3, tex=lambda x, y: -0.5 if (x + y) % 4 == 0 else 0)
        else:
            for x0 in (5, 13, 21):
                m = mask_of(img.size, lambda d, x0=x0, y=y: d.ellipse([x0, y, x0 + 5, y + 5], fill=255))
                shade_shape(img, m, w.end, radius=0.6, slope=-0.6)
    shroud = mask_of(img.size, lambda d: d.rounded_rectangle([7, 3, 25, 9], radius=3, fill=255))
    shade_over(img, shroud, LINEN, LINEN[0], radius=1.0, slope=1.2)
    return selective_outline(img)


# ---------------------------------------------------------------- the smith and the empath

def item_whetstone():
    img = canvas()
    m = mask_of(img.size, lambda d: d.polygon([(4, 20), (22, 9), (28, 12), (10, 24)], fill=255))
    shade_shape(img, m, HONE, radius=1.2, slope=1.4, tex=lambda x, y: -0.5 if (x * 2 + y) % 9 == 0 else 0.2 if bayer(x, y) > 0.85 else 0)
    face = mask_of(img.size, lambda d: d.polygon([(5, 19), (22, 9), (24, 10), (7, 21)], fill=255))
    shade_shape(img, face, SLATE[2:], radius=0.5, slope=1.6)
    for x, y in ((12, 17), (16, 15), (20, 12)):                       # an oily sheen where it's been worked
        put(img, x, y, (206, 214, 222, 255))
    return selective_outline(img)


def icon_meditation_stone():
    img = canvas()
    for a in range(0, 360, 40):                                       # a ring of pebbles
        x, y = 16 + 13 * math.cos(math.radians(a)), 22 + 6 * math.sin(math.radians(a))
        m = mask_of(img.size, lambda d, x=x, y=y: d.ellipse([x - 2, y - 1.5, x + 2, y + 1.5], fill=255))
        shade_shape(img, m, STONE[1:], radius=0.5, slope=1.4)
    stone = mask_of(img.size, lambda d: d.ellipse([6, 8, 26, 26], fill=255))
    shade_shape(img, stone, HONE, radius=2.4, slope=1.4, depth=0.6)
    cloth = mask_of(img.size, lambda d: d.ellipse([10, 9, 22, 15], fill=255))
    shade_shape(img, cloth, LINEN, radius=0.8, slope=1.2)
    for y in range(17, 23):                                           # a rune stroke worn into its face
        put(img, 18, y, HONE[0])
    put(img, 19, 18, HONE[0]); put(img, 20, 19, HONE[0])
    return selective_outline(img)


def item_herbs():
    img = canvas()
    for i, (dx, tip) in enumerate(((-6, (6, 4)), (-2, (12, 2)), (2, (18, 3)), (6, (25, 6)), (0, (15, 7)))):
        stem = mask_of(img.size, lambda d, tip=tip: d.line([(16, 27), tip], fill=255, width=1))
        img.paste(SAGE[1], mask=stem)
        for k in range(3):
            t = 0.3 + 0.25 * k
            x, y = 16 + (tip[0] - 16) * t, 27 + (tip[1] - 27) * t
            leaf = mask_of(img.size, lambda d, x=x, y=y, k=k: d.ellipse([x - 3 + (k % 2) * 2, y - 2, x + 1 + (k % 2) * 2, y + 1], fill=255))
            shade_shape(img, leaf, LEAF if i % 2 else SAGE, radius=0.5, slope=1.3)
    tie = mask_of(img.size, lambda d: d.rectangle([13, 22, 19, 24], fill=255))
    shade_shape(img, tie, MADDER, radius=0.4)
    return selective_outline(img)


# ---------------------------------------------------------------- model faces

def prop_iron_band():
    """A cask hoop's face: dark iron with a lit top edge and rivets, tiling both ways."""
    img = canvas()
    for y in range(S):
        for x in range(S):
            v = 1 + (1 if y % 16 == 0 else 0) - (1 if y % 16 == 15 else 0) + (0.4 if bayer(x, y) > 0.75 else 0)
            img.putpixel((x, y), IRON[max(0, min(4, round(v)))])
    for x in (4, 20):
        for y in (6, 22):
            put(img, x, y, IRON[4]); put(img, x + 1, y + 1, IRON[0])
    return img


def prop_iron_pot():
    """A black iron pot's sides: hammered, with a soot bloom low down."""
    img = canvas()
    for y in range(S):
        for x in range(S):
            dent = 0.5 if ((x // 5 + y // 4) % 3 == 0 and (x % 5 in (1, 2))) else 0
            soot = -0.6 if (y % 32) > 22 else 0
            v = 1.4 + dent + soot + (0.3 if bayer(x, y) > 0.8 else 0)
            img.putpixel((x, y), IRON[max(0, min(4, round(v)))])
    return img


def prop_washtub_side():
    """Pale pine staves with a hoop: a wash tub's sides (not the smithy's dark quench tub)."""
    img = canvas()
    w = WOODS["_pine"]
    for y in range(S):
        for x in range(S):
            if x % 8 == 7:
                c = w.w[1]
            elif y % 32 in (10, 11):
                c = IRON[2] if y % 32 == 10 else IRON[1]
            else:
                c = w.w[3 + (1 if x % 8 == 0 else 0) - (1 if bayer(x, y) > 0.85 else 0)]
            img.putpixel((x, y), c)
    return img


def prop_suds():
    """Grey-blue washing water with drifts of suds."""
    img = canvas()
    rnd = random.Random(19)
    for y in range(S):
        for x in range(S):
            img.putpixel((x, y), WATER[1 + (1 if bayer(x, y) > 0.7 else 0)])
    for _ in range(14):
        cx, cy, r = rnd.randrange(S), rnd.randrange(S), rnd.uniform(1.5, 3.2)
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                    lit = (x - cx) + (y - cy) < -r * 0.3
                    img.putpixel((x % S, y % S), SUDS[2] if lit else SUDS[1])
    return img


def generate():
    for name, make in (("ale_cask", icon_ale_cask), ("shelf", icon_shelf), ("larder", icon_larder), ("stew_pot", icon_stew_pot),
                       ("wash_tub", icon_wash_tub), ("bar_counter", icon_bar_counter), ("serving_hatch", icon_serving_hatch),
                       ("fireplace", icon_fireplace), ("lantern", icon_lantern), ("wall_torch", icon_wall_torch), ("barrel", icon_barrel), ("crate", icon_crate),
                       ("barn", icon_barn), ("pyre", icon_pyre), ("meditation_stone", icon_meditation_stone)):
        save(make(), "buildings/" + name)
    save(icon_keg(), "items/keg")
    save(item_whetstone(), "items/whetstone")
    save(item_herbs(), "items/herbs")
    save(prop_iron_band(), "props/iron_band")
    save(prop_iron_pot(), "props/iron_pot")
    save(prop_washtub_side(), "props/washtub_side")
    save(prop_suds(), "props/suds")
    print("stations: wrote 18 icons and 4 model faces")


if __name__ == "__main__":
    generate()
