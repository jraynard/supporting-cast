#!/usr/bin/env python3
"""The tinker's art (data/defs/tinkering.json): the bench's icon, and the locks, keys, contraptions
and curios made, mended and appraised there. 32x32 icons in the style of norse_items.py. Writes
only its own files. Run alone (``python3 tools/tinker_art.py``) or from ``gen_placeholder_art.py``.
"""
import os

from PIL import Image

from norse_art import bayer, ramp, selective_outline
from norse_items import shape, px, S, SIZE
from norse_people import IRON, BRONZE, LEATHER, GOLD

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "textures")
OAK = ramp((128, 86, 50), 5)
DARK_OAK = ramp((96, 64, 40), 5)
BRASS = ramp((196, 158, 72), 5)


def blank():
    return Image.new("RGBA", SIZE, (0, 0, 0, 0))


def bench():
    """A dark bench with an iron vise at one end and files in a rack at the other."""
    img = blank()
    shape(img, lambda d: d.rectangle([2, 13, 29, 17], fill=255), DARK_OAK, tex=lambda x, y: 0.08 if x % 5 == 0 else 0.0)   # the top
    for x in (4, 26):                                                                                    # legs
        shape(img, lambda d, x=x: d.rectangle([x, 17, x + 2, 28], fill=255), DARK_OAK, radius=0.8)
    px(img, [(x, 24) for x in range(6, 26)], DARK_OAK[1])                                               # the stretcher
    shape(img, lambda d: d.rectangle([4, 8, 10, 13], fill=255), IRON, radius=0.8)                      # the vise's jaws
    px(img, [(x, 7) for x in range(3, 12)], IRON[3])                                                    # its screw bar
    for i, x in enumerate(range(19, 28, 3)):                                                             # files standing in a rack
        px(img, [(x, y) for y in range(5 + i, 13)], IRON[3 if i % 2 else 2])
        px(img, [(x, y) for y in range(10, 13)], LEATHER[2])
    px(img, [(13, 12), (14, 12), (16, 11), (17, 12)], BRASS[3])                                         # small parts on the top
    return selective_outline(img)


def door_lock():
    """A heavy iron box lock with a keyhole and a brass bolt."""
    img = blank()
    shape(img, lambda d: d.rounded_rectangle([6, 9, 25, 25], radius=2, fill=255), IRON, radius=1.0)
    px(img, [(x, 10) for x in range(8, 24)], IRON[3])
    shape(img, lambda d: d.rectangle([25, 14, 29, 17], fill=255), BRASS, radius=0.6)                      # the bolt
    px(img, [(15, 14), (16, 14), (15, 15), (16, 15), (16, 16), (16, 17), (16, 18)], (24, 20, 18, 255))  # keyhole
    for x, y in ((8, 12), (23, 12), (8, 22), (23, 22)):
        px(img, [(x, y)], BRASS[3])
    return selective_outline(img)


def key():
    """An iron key: a ring bow, a long shank, the bits."""
    img = blank()
    shape(img, lambda d: d.ellipse([4, 11, 13, 20], fill=255), IRON, radius=0.8)
    shape(img, lambda d: d.ellipse([7, 14, 10, 17], fill=0), IRON, radius=0.4)
    img2 = blank()
    shape(img2, lambda d: d.ellipse([7, 14, 10, 17], fill=255), IRON)
    for x in range(7, 11):
        for y in range(14, 18):
            if img2.getpixel((x, y))[3] > 0:
                img.putpixel((x, y), (0, 0, 0, 0))
    shape(img, lambda d: d.rectangle([12, 14, 27, 16], fill=255), IRON, radius=0.6)
    shape(img, lambda d: d.polygon([(21, 16), (27, 16), (27, 22), (25, 22), (25, 19), (23, 19), (23, 21), (21, 21)], fill=255), IRON, radius=0.6)
    return selective_outline(img)


def tapping_rod():
    """A long rod with a coiled spring near the tip and a bell on the grip."""
    img = blank()
    shape(img, lambda d: d.line([(6, 27), (26, 5)], fill=255, width=3), OAK, radius=0.6)
    for i in range(4):                                                                                  # the spring
        px(img, [(20 + i, 12 - i), (21 + i, 12 - i)], IRON[3 if i % 2 else 1])
    shape(img, lambda d: d.pieslice([2, 21, 10, 31], 180, 360, fill=255), BRASS, radius=0.6)            # the bell
    px(img, [(6, 27)], BRASS[0])
    return selective_outline(img)


def grapnel():
    """Three hooks on a shank, a coil of line and a small geared winch."""
    img = blank()
    shape(img, lambda d: d.ellipse([3, 15, 17, 29], fill=255), ramp((170, 140, 96), 5), radius=1.0, tex=lambda x, y: 0.1 if (x + y) % 3 == 0 else 0.0)
    shape(img, lambda d: d.ellipse([7, 19, 13, 25], fill=0), IRON)
    shape(img, lambda d: d.line([(18, 26), (22, 8)], fill=255, width=3), IRON, radius=0.6)
    for pts in ([(22, 8), (16, 5), (14, 9)], [(22, 8), (28, 5), (29, 10)], [(22, 8), (22, 3)]):
        shape(img, lambda d, pts=pts: d.line(pts, fill=255, width=2), IRON, radius=0.4)
    shape(img, lambda d: d.ellipse([21, 20, 29, 28], fill=255), BRASS, radius=0.8)                      # the winch's gear
    for x, y in ((25, 19), (30, 24), (25, 29), (20, 24)):
        px(img, [(x, y)], BRASS[1])
    return selective_outline(img)


def clockwork_lantern():
    """A shuttered iron lantern with a brass winding key on its side and a warm glow."""
    img = blank()
    shape(img, lambda d: d.rectangle([9, 9, 22, 26], fill=255), IRON, radius=0.8)
    shape(img, lambda d: d.rectangle([12, 12, 19, 23], fill=255), ramp((250, 196, 92), 5), radius=1.6)  # the flame behind horn
    px(img, [(x, y) for x in range(12, 20) for y in (15, 19)], IRON[2])
    shape(img, lambda d: d.polygon([(8, 9), (15, 3), (23, 9)], fill=255), IRON, radius=0.6)
    shape(img, lambda d: d.ellipse([13, 1, 18, 5], fill=255), IRON, radius=0.4)
    shape(img, lambda d: d.rectangle([23, 15, 27, 18], fill=255), BRASS, radius=0.5)                    # the key
    px(img, [(27, 14), (27, 19)], BRASS[3])
    return selective_outline(img)


def spring_snare():
    """Two toothless iron jaws open on a coiled spring, a chain and stake."""
    img = blank()
    shape(img, lambda d: d.ellipse([3, 9, 29, 25], fill=255), IRON, radius=0.8)
    shape(img, lambda d: d.ellipse([7, 12, 25, 22], fill=0), IRON)
    hole = blank()
    shape(hole, lambda d: d.ellipse([7, 12, 25, 22], fill=255), IRON)
    for x in range(S):
        for y in range(S):
            if hole.getpixel((x, y))[3] > 0:
                img.putpixel((x, y), (0, 0, 0, 0))
    shape(img, lambda d: d.rectangle([13, 15, 19, 19], fill=255), BRASS, radius=0.6)                    # the plate
    for i in range(5):                                                                                  # the spring
        px(img, [(4 + i, 26 + (i % 2)), (5 + i, 26 + (i % 2))], IRON[3])
    px(img, [(x, 28) for x in range(10, 26, 2)], IRON[1])                                              # the chain
    return selective_outline(img)


def signal_rocket():
    """A red paper case with a pointed cap on a stick, a fuse at the foot."""
    img = blank()
    red = ramp((176, 52, 44), 5)
    shape(img, lambda d: d.line([(10, 29), (17, 14)], fill=255, width=2), OAK, radius=0.4)
    shape(img, lambda d: d.polygon([(15, 18), (21, 21), (27, 9), (21, 6)], fill=255), red, radius=0.8)
    shape(img, lambda d: d.polygon([(21, 6), (27, 9), (28, 2)], fill=255), ramp((222, 200, 150), 5), radius=0.6)
    px(img, [(16, 21), (15, 22), (14, 23)], (240, 200, 90, 255))
    px(img, [(x, 14) for x in range(18, 25)], BRASS[3])
    return selective_outline(img)


def carved_idol():
    """A squat, worn stone figure."""
    img = blank()
    stone = ramp((96, 92, 86), 5)
    shape(img, lambda d: d.ellipse([10, 4, 22, 15], fill=255), stone, radius=1.2)
    shape(img, lambda d: d.rounded_rectangle([8, 13, 24, 28], radius=4, fill=255), stone, radius=1.4, tex=lambda x, y: bayer(x, y) * 0.08)
    px(img, [(13, 9), (18, 9)], (40, 34, 30, 255))
    px(img, [(x, 12) for x in range(14, 18)], stone[0])
    px(img, [(x, 20) for x in range(10, 23)], stone[1])
    px(img, [(13, 9), (18, 9)], GOLD[3])
    return selective_outline(img)


def clockwork_bird():
    """A brass songbird on a perch, a winding key in its back."""
    img = blank()
    shape(img, lambda d: d.ellipse([7, 11, 22, 23], fill=255), BRASS, radius=1.2)
    shape(img, lambda d: d.ellipse([17, 7, 25, 15], fill=255), BRASS, radius=1.0)
    shape(img, lambda d: d.polygon([(25, 10), (29, 11), (25, 12)], fill=255), IRON, radius=0.4)
    shape(img, lambda d: d.polygon([(8, 15), (2, 12), (4, 19)], fill=255), BRASS, radius=0.6)
    px(img, [(22, 10)], (24, 20, 18, 255))
    shape(img, lambda d: d.rectangle([5, 25, 27, 26], fill=255), OAK, radius=0.4)
    px(img, [(13, 23), (13, 24), (17, 23), (17, 24)], IRON[1])
    px(img, [(12, 9), (12, 10), (11, 8), (13, 8)], IRON[3])                                           # the key
    return selective_outline(img)


def old_seal():
    """A heavy signet ring with a carved face."""
    img = blank()
    shape(img, lambda d: d.ellipse([6, 12, 22, 28], fill=255), GOLD, radius=1.0)
    hole = blank()
    shape(hole, lambda d: d.ellipse([10, 16, 18, 24], fill=255), GOLD)
    for x in range(S):
        for y in range(S):
            if hole.getpixel((x, y))[3] > 0:
                img.putpixel((x, y), (0, 0, 0, 0))
    shape(img, lambda d: d.ellipse([14, 3, 28, 15], fill=255), GOLD, radius=1.2)
    px(img, [(19, 7), (21, 7), (20, 8), (19, 10), (21, 10), (23, 9), (17, 9)], GOLD[0])
    return selective_outline(img)


def gilded_chalice():
    """A cup on a stem, a gilt rim and a band of figures."""
    img = blank()
    silver = ramp((176, 176, 182), 5)
    shape(img, lambda d: d.polygon([(7, 4), (25, 4), (21, 16), (11, 16)], fill=255), silver, radius=1.0)
    px(img, [(x, 4) for x in range(7, 26)] + [(x, 5) for x in range(8, 25)], GOLD[3])
    px(img, [(x, 9) for x in range(9, 24, 2)], GOLD[1])
    shape(img, lambda d: d.rectangle([15, 16, 17, 24], fill=255), silver, radius=0.4)
    shape(img, lambda d: d.ellipse([9, 23, 23, 28], fill=255), silver, radius=0.8)
    return selective_outline(img)


def star_chart():
    """A curling sheet of brown paper with stars and lines in ink."""
    img = blank()
    paper = ramp((214, 192, 148), 5)
    shape(img, lambda d: d.rectangle([4, 6, 27, 26], fill=255), paper, radius=1.2, tex=lambda x, y: bayer(x, y) * 0.06)
    shape(img, lambda d: d.ellipse([2, 5, 7, 27], fill=255), paper, radius=0.8)
    stars = [(10, 10), (15, 13), (21, 9), (24, 17), (18, 21), (11, 19)]
    for (a, b), (c, e) in zip(stars, stars[1:]):
        for t in range(8):
            px(img, [(a + (c - a) * t // 8, b + (e - b) * t // 8)], (120, 84, 56, 255))
    px(img, stars, (60, 40, 30, 255))
    return selective_outline(img)


def collectors_crate():
    """A straw-packed crate with a mark burned in the side."""
    img = blank()
    shape(img, lambda d: d.rectangle([4, 12, 27, 28], fill=255), OAK, radius=1.0, tex=lambda x, y: 0.1 if y % 4 == 0 else 0.0)
    shape(img, lambda d: d.polygon([(4, 12), (8, 7), (24, 6), (27, 12)], fill=255), ramp((214, 186, 96), 5), radius=0.8)   # straw
    px(img, [(x, 12) for x in range(4, 28)], DARK_OAK[1])
    px(img, [(14, 18), (15, 19), (16, 20), (17, 19), (18, 18), (16, 22), (16, 21)], DARK_OAK[0])   # the collector's mark
    return selective_outline(img)


def save(img, rel):
    path = os.path.join(ROOT, rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def generate():
    save(bench(), "buildings/tinker_bench")
    save(collectors_crate(), "buildings/collectors_crate")
    for name, make in (("door_lock", door_lock), ("key", key), ("tapping_rod", tapping_rod), ("grapnel", grapnel),
                       ("clockwork_lantern", clockwork_lantern), ("spring_snare", spring_snare), ("signal_rocket", signal_rocket),
                       ("carved_idol", carved_idol), ("clockwork_bird", clockwork_bird), ("old_seal", old_seal),
                       ("gilded_chalice", gilded_chalice), ("star_chart", star_chart)):
        save(make(), "items/" + name)


if __name__ == "__main__":
    generate()
