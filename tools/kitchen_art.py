#!/usr/bin/env python3
"""The kitchen's and the inn rooms' furniture: toolbar icons (``buildings/``), item icons (``items/``)
and the face textures their box models use (``props/``), in the style of ``norse_smithy``.

  kitchen   spit, oven, prep_table, butcher_block, log_basket, smoking_rack
  rooms     key_board, key_box, washstand, chamber_pot, midden, lost_property
  items     linen, soiled_linen, slop_bucket
  faces     brick_dome, oven_mouth, cutting_board, block_top, wicker, glaze, muck, key_fobs (hooks: the keys are drawn live)

Writes only its own files. Run alone (``python3 tools/kitchen_art.py``) or from gen_placeholder_art.py.
"""
import math
import random

from norse_art import ramp, mask_of, shade_shape, shade_over, selective_outline, bayer
from norse_buildings import (S, IRON, LEATHER, LINEN, STRAW, WOAD, MADDER, BRONZE, WOODS, canvas, put, clampi, save,
                             board, nail)

BRICK = ramp((168, 84, 60), 5, dark=0.5, light=1.3)
MORTAR = (196, 184, 160, 255)
EMBER = ramp((214, 96, 40), 5, dark=0.45, light=1.4)
GLAZE = ramp((226, 218, 198), 4, main=2, dark=0.7, light=1.06)
MUCK = ramp((92, 76, 54), 5, main=2, dark=0.55, light=1.4)
WICKER = ramp((182, 146, 86), 5, dark=0.5, light=1.3)
FAT = ramp((240, 226, 206), 3, main=1, dark=0.85, light=1.04)
PORK = ramp((222, 150, 140), 5, dark=0.55, light=1.15)
SOOT = ramp((54, 48, 46), 4, main=1, dark=0.6, light=1.8)
CLAY = ramp((176, 110, 70), 5, dark=0.55, light=1.3)


def shape(img, draw, colors, line=None, radius=0.8, slope=1.4):
    m = mask_of(img.size, draw)
    if line is None:
        shade_shape(img, m, colors, radius=radius, slope=slope)
    else:
        shade_over(img, m, colors, line, radius=radius, slope=slope)


# ---------------------------------------------------------------- faces

def prop_brick_dome():
    """Brick courses, a little soot-dark toward the top: the oven's dome."""
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            row = y // 5
            seam = y % 5 == 4 or (x + row * 4) % 9 == 8
            v = 2 + bayer(x, y) * 0.6 - (1 - y / S) * 0.6
            px[x, y] = MORTAR if seam else BRICK[clampi(v, 5)]
    return img


def prop_oven_mouth():
    """The oven's front: an arched black mouth in the brick, a stone sill. Fitted to the face."""
    img = prop_brick_dome()
    m = mask_of(img.size, lambda d: (d.rectangle([9, 14, 22, 29], fill=255), d.ellipse([9, 7, 22, 21], fill=255)))
    for y in range(S):
        for x in range(S):
            if m.getpixel((x, y)):
                put(img, x, y, SOOT[0] if y > 12 else SOOT[1])
    for x in range(6, 26):
        put(img, x, 29, (150, 150, 150, 255)); put(img, x, 30, (120, 120, 124, 255))
    return img


def prop_cutting_board():
    """An oak board, knife-scored, floured at one end."""
    img = canvas()
    rnd = random.Random(61)
    board(img, 0, 0, S - 1, S - 1, WOODS["_oak"], rnd, True, tone=3.4, seam=False)
    for _ in range(9):
        x, y = rnd.randrange(4, 28), rnd.randrange(4, 28)
        for k in range(5):
            put(img, x + k, y + k // 2, WOODS["_oak"].w[1])
    for _ in range(40):
        put(img, rnd.randrange(20, 32), rnd.randrange(0, 32), (238, 232, 216, 255))
    return img


def prop_block_top():
    """End grain of the butcher's block: rings, cleaver scars."""
    img = canvas()
    w = WOODS["_oak"]
    px = img.load()
    for y in range(S):
        for x in range(S):
            r = math.hypot(x - 15.5, y - 15.5)
            px[x, y] = w.end[clampi(2.2 + math.sin(r * 1.3) * 0.6 + bayer(x, y) * 0.4, len(w.end))]
    rnd = random.Random(63)
    for _ in range(8):
        x, y, a = rnd.randrange(4, 28), rnd.randrange(4, 28), rnd.random() * math.pi
        for k in range(-4, 5):
            put(img, round(x + math.cos(a) * k), round(y + math.sin(a) * k), w.end[0])
    return img


def prop_wicker():
    """Woven willow: over and under."""
    img = canvas()
    for y in range(S):
        for x in range(S):
            over = ((x // 4) + (y // 4)) % 2 == 0
            v = (3.2 if over else 1.6) - abs((y % 4) - 1.5) * 0.4 + bayer(x, y) * 0.3
            put(img, x, y, WICKER[clampi(v, 5)])
    return img


def prop_glaze():
    """A pale glazed pot's side, a blue band round it."""
    img = canvas()
    for y in range(S):
        for x in range(S):
            put(img, x, y, GLAZE[clampi(2.2 + bayer(x, y) * 0.4 - abs(x - 16) / 24, 4)])
    for x in range(S):
        put(img, x, 18, WOAD[2]); put(img, x, 19, WOAD[1])
    return img


def prop_muck():
    """The midden from above: muck, ash and straw."""
    img = canvas()
    rnd = random.Random(65)
    for y in range(S):
        for x in range(S):
            put(img, x, y, MUCK[clampi(1.8 + bayer(x, y) * 0.8 + math.sin(x * 0.7 + y * 0.4) * 0.4, 5)])
    for _ in range(26):
        x, y = rnd.randrange(S), rnd.randrange(S)
        for k in range(rnd.randint(2, 4)):
            put(img, x + k, y, STRAW[3])
    for _ in range(18):
        put(img, rnd.randrange(S), rnd.randrange(S), (150, 146, 140, 255))
    return img


def prop_key_fobs():
    """The key board's front: a planked board and six iron hooks, numbered in paint (the keys on them are drawn by the game)."""
    img = canvas()
    rnd = random.Random(67)
    board(img, 0, 0, S - 1, S - 1, WOODS["_pine"], rnd, True, tone=3.0)
    for i in range(6):
        cx, cy = 5 + (i % 3) * 11, 6 + (i // 3) * 14
        put(img, cx, cy, IRON[3]); put(img, cx, cy + 1, IRON[2]); put(img, cx + 1, cy + 2, IRON[1])
        for k in range(i + 1):                                            # the room's number, in tally strokes
            put(img, cx - 2 + k, cy + 9, (226, 216, 190, 255))
    return img


# ---------------------------------------------------------------- kitchen icons

def icon_spit():
    img = canvas()
    shape(img, lambda d: d.ellipse([3, 24, 28, 30], fill=255), EMBER[1:], radius=1.0)
    for x in (5, 26):
        shape(img, lambda d, x=x: d.line([(x, 29), (x, 10)], fill=255, width=2), IRON[1:], radius=0.4)
        put(img, x - 1, 9, IRON[3]); put(img, x + 1, 9, IRON[3])
    shape(img, lambda d: d.line([(2, 11), (29, 11)], fill=255, width=1), IRON[2:], radius=0.3)
    shape(img, lambda d: d.ellipse([9, 7, 22, 16], fill=255), ramp((176, 104, 56), 5, dark=0.5, light=1.3), radius=1.2)
    return selective_outline(img)


def icon_oven():
    img = canvas()
    shape(img, lambda d: (d.rectangle([3, 14, 28, 29], fill=255), d.ellipse([3, 3, 28, 25], fill=255)), BRICK[1:], BRICK[0], radius=1.6)
    for y in range(5, 29, 4):
        for x in range(4, 28):
            if img.getpixel((x, y))[3]:
                put(img, x, y, MORTAR)
    shape(img, lambda d: (d.rectangle([10, 19, 21, 29], fill=255), d.ellipse([10, 14, 21, 24], fill=255)), [SOOT[0]] * 4)
    for x in range(12, 20):
        put(img, x, 27, EMBER[3]); put(img, x, 28, EMBER[2])
    return selective_outline(img)


def icon_prep_table():
    img = canvas()
    w = WOODS["_oak"]
    rnd = random.Random(71)
    board(img, 2, 12, 29, 16, w, rnd, True, tone=3.2)
    for x in (4, 26):
        shape(img, lambda d, x=x: d.rectangle([x, 16, x + 2, 29], fill=255), w.w[1:6], radius=0.5)
    shape(img, lambda d: d.rectangle([6, 9, 18, 12], fill=255), w.w[2:6], radius=0.5)
    shape(img, lambda d: d.polygon([(19, 11), (27, 9), (28, 11), (20, 12)], fill=255), [(v, v, v, 255) for v in (150, 190, 220, 245)], radius=0.4)
    for x, y in ((8, 8), (11, 7), (14, 8)):
        put(img, x, y, (110, 160, 70, 255)); put(img, x + 1, y, (90, 140, 60, 255))
    return selective_outline(img)


def icon_butcher_block():
    img = canvas()
    w = WOODS["_oak"]
    shape(img, lambda d: d.rectangle([7, 12, 24, 29], fill=255), w.bark, radius=1.0)
    shape(img, lambda d: d.ellipse([7, 9, 24, 15], fill=255), w.end[1:], radius=0.6)
    for y in (18, 25):
        for x in range(7, 25):
            put(img, x, y, IRON[2])
    shape(img, lambda d: d.polygon([(13, 2), (21, 2), (21, 9), (14, 8)], fill=255), [(v, v, v, 255) for v in (130, 170, 205, 235)], radius=0.5)
    shape(img, lambda d: d.line([(17, 9), (17, 12)], fill=255, width=2), LEATHER[1:], radius=0.3)
    return selective_outline(img)


def icon_log_basket():
    img = canvas()
    shape(img, lambda d: d.polygon([(4, 14), (27, 14), (25, 29), (6, 29)], fill=255), WICKER[1:], WICKER[0], radius=1.0)
    for y in range(16, 29, 3):
        for x in range(5, 27, 2):
            put(img, x + (y // 3) % 2, y, WICKER[1])
    w = WOODS["_birch"] if "_birch" in WOODS else WOODS["_oak"]
    for x0, y0 in ((7, 8), (13, 6), (19, 9)):
        shape(img, lambda d, x0=x0, y0=y0: d.ellipse([x0, y0, x0 + 6, y0 + 7], fill=255), w.end[1:], radius=0.6)
    return selective_outline(img)


def icon_smoking_rack():
    img = canvas()
    w = WOODS["_pine"]
    for x in (4, 26):
        shape(img, lambda d, x=x: d.rectangle([x, 4, x + 2, 29], fill=255), w.w[1:6], radius=0.4)
    for y in (6, 15):
        shape(img, lambda d, y=y: d.rectangle([4, y, 28, y + 1], fill=255), w.w[2:6], radius=0.3)
        for x0 in (8, 15, 21):
            shape(img, lambda d, x0=x0, y=y: d.ellipse([x0, y + 2, x0 + 4, y + 9], fill=255), PORK[:4], radius=0.6)
    for k in range(6):
        put(img, 14 + round(math.sin(k) * 2), 3 - k // 2, (200, 200, 205, 180))
    return selective_outline(img)


# ---------------------------------------------------------------- the rooms' icons

def icon_key_board():
    img = canvas()
    w = WOODS["_pine"]
    rnd = random.Random(73)
    board(img, 3, 4, 28, 26, w, rnd, True, tone=3.0)
    for i in range(6):
        cx, cy = 8 + (i % 3) * 8, 7 + (i // 3) * 10
        put(img, cx, cy, IRON[3])
        for y in range(cy + 1, cy + 6):
            put(img, cx, y, BRONZE[2])
        put(img, cx - 1, cy + 6, BRONZE[3]); put(img, cx + 1, cy + 6, BRONZE[1])
        put(img, cx + 1, cy + 2, MADDER[2]); put(img, cx + 2, cy + 2, MADDER[2]); put(img, cx + 2, cy + 3, MADDER[1])
    return selective_outline(img)


def icon_key_box():
    img = canvas()
    w = WOODS["_oak"]
    rnd = random.Random(75)
    board(img, 6, 13, 25, 26, w, rnd, True, tone=3.1)
    shape(img, lambda d: d.rectangle([6, 9, 25, 13], fill=255), w.w[2:6], radius=0.5)
    for x in (6, 25):
        for y in range(9, 27):
            put(img, x, y, IRON[2])
    shape(img, lambda d: d.rectangle([14, 14, 17, 18], fill=255), BRONZE, radius=0.3)
    return selective_outline(img)


def icon_washstand():
    img = canvas()
    w = WOODS["_oak"]
    rnd = random.Random(77)
    board(img, 6, 17, 25, 29, w, rnd, False, tone=3.0)
    shape(img, lambda d: d.ellipse([4, 13, 27, 19], fill=255), GLAZE, radius=0.8)
    shape(img, lambda d: d.polygon([(15, 3), (21, 3), (23, 8), (22, 15), (14, 15), (13, 8)], fill=255), CLAY, radius=0.8)
    return selective_outline(img)


def icon_chamber_pot():
    img = canvas()
    shape(img, lambda d: d.polygon([(6, 14), (25, 14), (23, 28), (8, 28)], fill=255), GLAZE, radius=1.0)
    shape(img, lambda d: d.ellipse([5, 11, 26, 17], fill=255), GLAZE[1:], radius=0.5)
    shape(img, lambda d: d.rectangle([25, 17, 28, 23], fill=255), GLAZE, radius=0.3)
    for x in range(8, 24):
        put(img, x, 22, WOAD[2])
    return selective_outline(img)


def icon_midden():
    img = canvas()
    shape(img, lambda d: d.polygon([(2, 28), (8, 17), (16, 12), (24, 16), (30, 28)], fill=255), MUCK[1:], MUCK[0], radius=1.6)
    rnd = random.Random(79)
    for _ in range(14):
        x, y = rnd.randrange(8, 25), rnd.randrange(17, 27)
        put(img, x, y, STRAW[3]); put(img, x + 1, y, STRAW[2])
    for k in range(5):
        put(img, 12 + round(math.sin(k * 1.4) * 1.5), 10 - k, (150, 160, 120, 150))     # a whiff
    return selective_outline(img)


def icon_lost_property():
    img = canvas()
    w = WOODS["_pine"]
    rnd = random.Random(81)
    board(img, 5, 14, 26, 28, w, rnd, True, tone=3.2)
    shape(img, lambda d: d.polygon([(9, 14), (12, 5), (17, 4), (16, 14)], fill=255), LEATHER, radius=0.6)   # a glove
    shape(img, lambda d: d.ellipse([18, 8, 24, 14], fill=255), BRONZE, radius=0.5)                       # a buckle
    return selective_outline(img)


# ---------------------------------------------------------------- items

def item_linen():
    img = canvas()
    for i, y0 in enumerate((20, 15, 10)):
        shape(img, lambda d, y0=y0: d.rounded_rectangle([5, y0, 26, y0 + 6], radius=2, fill=255), LINEN, radius=0.8)
        for x in range(7, 25, 4):
            put(img, x, y0 + 5, MADDER[2] if i == 0 else LINEN[1])
    return selective_outline(img)


def item_soiled_linen():
    img = canvas()
    shape(img, lambda d: d.polygon([(4, 26), (6, 15), (12, 9), (21, 10), (27, 16), (28, 26)], fill=255),
          ramp((206, 196, 172), 4, main=2, dark=0.7, light=1.04), radius=1.4)
    rnd = random.Random(83)
    for _ in range(10):
        x, y = rnd.randrange(8, 25), rnd.randrange(13, 25)
        put(img, x, y, (160, 146, 116, 255))
    for x, y in ((11, 14), (16, 18), (21, 15)):
        put(img, x, y, (140, 128, 104, 255)); put(img, x + 1, y + 1, (140, 128, 104, 255))       # creases
    return selective_outline(img)


def item_slop_bucket():
    img = canvas()
    w = WOODS[""]
    rnd = random.Random(85)
    for i, x0 in enumerate(range(6, 26, 4)):
        board(img, x0, 11, x0 + 3, 28, w, rnd, False, tone=2.9 + (i % 2) * 0.3)
    for y0 in (14, 24):
        for x in range(6, 26):
            put(img, x, y0, IRON[3]); put(img, x, y0 + 1, IRON[1])
    shape(img, lambda d: d.ellipse([6, 8, 25, 14], fill=255), MUCK[1:], radius=0.8)
    for t in range(6, 26):
        put(img, t, 10 - round(7 * (1 - ((t - 15.5) / 10) ** 2)), IRON[2])
    return selective_outline(img)


def generate():
    for name, make in (("brick_dome", prop_brick_dome), ("oven_mouth", prop_oven_mouth), ("cutting_board", prop_cutting_board),
                       ("block_top", prop_block_top), ("wicker", prop_wicker), ("glaze", prop_glaze), ("muck", prop_muck),
                       ("key_fobs", prop_key_fobs)):
        save(make(), "props/" + name)
    for name, make in (("spit", icon_spit), ("oven", icon_oven), ("prep_table", icon_prep_table), ("butcher_block", icon_butcher_block),
                       ("log_basket", icon_log_basket), ("smoking_rack", icon_smoking_rack),
                       ("key_board", icon_key_board), ("key_box", icon_key_box), ("washstand", icon_washstand),
                       ("chamber_pot", icon_chamber_pot), ("midden", icon_midden), ("lost_property", icon_lost_property)):
        save(make(), "buildings/" + name)
    for name, make in (("linen", item_linen), ("soiled_linen", item_soiled_linen), ("slop_bucket", item_slop_bucket)):
        save(make(), "items/" + name)


if __name__ == "__main__":
    generate()
