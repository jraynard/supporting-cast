"""Inventory icons for the worn gear, drawn to match the sheets in norse_people.py (same palettes,
same shapes): laced jerkin, ringed byrnie, fur-brimmed hood-cap, spangenhelm, knotwork satchel,
and the clothes (data/defs/outfits.json), cloth ones once per dye (``items/tunic_woad``; the plain
``items/tunic`` is undyed). 32x32, no tint (the game draws icons as they are)."""
import json
import os
import re

from PIL import Image

from norse_art import mask_of, shade_shape, selective_outline, bayer, ramp, ribbon
from norse_people import LEATHER, MAIL, IRON, BRONZE, FUR, WRAP, GOLD, SHOE, LINEN, TRIM_DARK, BEAD_AMBER, BEAD_BLUE

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
S = 32
SIZE = (S, S)


def shape(img, pts_fn, colors, **kw):
    kw.setdefault("radius", 1.6)
    shade_shape(img, mask_of(SIZE, pts_fn), colors, **kw)


def px(img, pts, color):
    for x, y in pts:
        if 0 <= x < S and 0 <= y < S:
            img.putpixel((x, y), color)


def jerkin():
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    body = [(9, 5), (13, 4), (16, 8), (19, 4), (23, 5), (26, 10), (23, 13), (22, 26), (10, 26), (9, 13), (6, 10)]
    shape(img, lambda d: d.polygon(body, fill=255), LEATHER, tex=lambda x, y: bayer(x, y) * 0.09)
    for x in range(10, 23, 3):                                      # a tabbed hem
        px(img, [(x, 27), (x + 1, 27)], LEATHER[1])
    px(img, [(15, y) for y in range(9, 26)] + [(16, y) for y in range(9, 26)], LEATHER[0])      # lacing
    px(img, [(14 if k % 2 else 17, 10 + k * 2) for k in range(8)], WRAP[1])
    px(img, [(x, 15) for x in range(10, 22)], LEATHER[3])
    px(img, [(11, 6), (12, 5), (20, 5), (21, 6)], FUR[2])
    return selective_outline(img)


def cap():
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    shape(img, lambda d: (d.pieslice([5, 6, 27, 30], 180, 360, fill=255), d.rectangle([5, 18, 27, 21], fill=255)), LEATHER, tex=lambda x, y: bayer(x, y) * 0.08)
    for x in range(5, 28):                                          # the fur brim
        px(img, [(x, 18), (x, 19)], FUR[3] if x % 2 == 0 else FUR[2])
        if (x * 7) % 3 == 0:
            px(img, [(x, 20)], FUR[1])
    px(img, [(x, 8) for x in range(13, 19)], LEATHER[4])
    return selective_outline(img)


def helm():
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    shape(img, lambda d: (d.pieslice([5, 4, 27, 30], 180, 360, fill=255), d.rectangle([5, 16, 27, 19], fill=255)), IRON, tex=lambda x, y: bayer(x, y) * 0.06)
    px(img, [(x, 17) for x in range(5, 28)], BRONZE[2])
    px(img, [(x, 18) for x in range(5, 28)], BRONZE[0])
    px(img, [(15, y) for y in range(5, 17)], BRONZE[1])
    px(img, [(16, y) for y in range(5, 17)], BRONZE[2])
    px(img, [(7, 17), (24, 17)], BRONZE[3])
    shape(img, lambda d: d.rectangle([15, 19, 16, 27], fill=255), IRON, radius=0.8)             # the nasal guard
    for y in range(19, 24):                                                                       # mail at the nape
        px(img, [(5, y), (6, y), (7, y), (25, y), (26, y), (27, y)], MAIL[2] if y % 2 else MAIL[1])
    return selective_outline(img)


def horned_helm():
    """An iron cap with a riveted band and a pair of ox horns curving up."""
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    horn = ramp((228, 216, 186), 4, dark=0.55, light=1.12)
    for s in (-1, 1):
        spine = [(16 + s * 9, 17), (16 + s * 13, 13), (16 + s * 14, 7), (16 + s * 12, 3)]
        shape(img, lambda d, spine=spine: d.polygon(ribbon(spine, [5, 4, 2.6, 0.8], steps=5), fill=255), horn, radius=1.0)
    shape(img, lambda d: (d.pieslice([6, 8, 26, 30], 180, 360, fill=255), d.rectangle([6, 18, 26, 21], fill=255)), IRON, tex=lambda x, y: bayer(x, y) * 0.06)
    px(img, [(x, 19) for x in range(6, 27)], IRON[1])
    px(img, [(x, 20) for x in range(6, 27)], IRON[0])
    px(img, [(16, y) for y in range(9, 18)], IRON[3])
    px(img, [(9, 20), (13, 20), (19, 20), (23, 20)], IRON[4])                                     # rivets
    px(img, [(16, 17), (15, 18), (17, 18), (16, 19), (16, 20), (15, 20), (17, 20), (16, 21)], IRON[1])  # the diamond boss
    return selective_outline(img)


def byrnie():
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    body = [(8, 5), (13, 4), (16, 7), (19, 4), (24, 5), (28, 11), (24, 14), (23, 27), (9, 27), (8, 14), (4, 11)]
    shape(img, lambda d: d.polygon(body, fill=255), MAIL, tex=lambda x, y: (0.1 if (x + y) % 2 == 0 else -0.1) + bayer(x, y) * 0.03)
    for x in range(9, 24, 2):                                                                    # dagged hem
        px(img, [(x, 28)], MAIL[1])
    px(img, [(x, 20) for x in range(9, 24)], LEATHER[1])                                         # a belt over the mail
    px(img, [(15, 20), (16, 20)], BRONZE[2])
    return selective_outline(img)


def satchel():
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    shape(img, lambda d: d.arc([8, 2, 24, 18], 180, 360, fill=255, width=3), LEATHER[:4], radius=0.8)   # the strap
    shape(img, lambda d: d.rounded_rectangle([5, 11, 27, 29], radius=3, fill=255), LEATHER, tex=lambda x, y: bayer(x, y) * 0.07)
    shape(img, lambda d: d.polygon([(5, 11), (27, 11), (27, 17), (16, 21), (5, 17)], fill=255), LEATHER[1:] + [LEATHER[4]], radius=1.2)   # the flap
    px(img, [(16, 19), (16, 20), (15, 19), (17, 19)], BRONZE[3])                                  # clasp
    px(img, [(7, 13), (25, 13), (7, 27), (25, 27)], GOLD[3])                                       # studs
    px(img, [(x, 14) for x in range(9, 24, 2)], GOLD[1])                                           # a knotwork stitch line
    return selective_outline(img)


def tankard(drink):
    """A staved wooden tankard, iron-hooped, with a head on it (``drink``: the liquid's ramp at the lip)."""
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    wood = ramp((150, 104, 62), 5)
    shape(img, lambda d: d.arc([18, 11, 29, 24], 270, 90, fill=255, width=3), wood[:4], radius=0.8)          # the handle
    shape(img, lambda d: d.rounded_rectangle([6, 8, 22, 29], radius=2, fill=255), wood,
          tex=lambda x, y: 0.12 if x % 4 == 0 else 0.0)                                                     # the staves
    for y in (11, 25):                                                                                        # iron hoops
        px(img, [(x, y) for x in range(6, 23)], IRON[1])
        px(img, [(x, y + 1) for x in range(6, 23)], IRON[3])
    shape(img, lambda d: d.ellipse([5, 3, 23, 11], fill=255), [(222, 210, 180, 255), (236, 226, 200, 255), (246, 240, 222, 255), (252, 250, 240, 255)], radius=1.0)  # foam
    px(img, [(x, 9) for x in range(8, 21, 3)], drink[2])                                                      # the drink showing under the head
    return selective_outline(img)


def strongbox():
    """An iron-bound oak box with a hasp and a heavy lock plate: what parties drag back from a quest."""
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    oak = ramp((128, 86, 50), 5)
    shape(img, lambda d: d.rounded_rectangle([3, 12, 28, 28], radius=1, fill=255), oak,
          tex=lambda x, y: 0.1 if y % 4 == 0 else 0.0)                                               # the body, in planks
    shape(img, lambda d: d.rounded_rectangle([2, 6, 29, 13], radius=3, fill=255), oak[1:] + [oak[4]], radius=1.2)   # the curved lid
    for x in (6, 25):                                                                                  # iron straps over lid and body
        px(img, [(x, y) for y in range(6, 29)], IRON[1])
        px(img, [(x + 1, y) for y in range(6, 29)], IRON[3])
    px(img, [(x, 13) for x in range(3, 29)], IRON[2])                                                  # the lid's rim
    shape(img, lambda d: d.rectangle([13, 14, 18, 21], fill=255), IRON, radius=0.8)                    # the lock plate
    px(img, [(15, 16), (16, 16), (15, 17), (16, 17), (16, 18), (16, 19)], (24, 20, 18, 255))           # keyhole
    px(img, [(14, 11), (15, 11), (16, 11), (17, 11), (14, 12), (17, 12)], IRON[3])                     # the hasp
    px(img, [(4, 27), (27, 27), (4, 14), (27, 14)], GOLD[2])                                           # corner studs
    return selective_outline(img)


def lockpicks():
    """A leather roll, opened out, with a probe and three thin picks."""
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    shape(img, lambda d: d.rounded_rectangle([3, 9, 29, 25], radius=2, fill=255), LEATHER, tex=lambda x, y: bayer(x, y) * 0.06)
    px(img, [(x, 22) for x in range(5, 28, 2)], LEATHER[0])                                            # the stitched pocket
    for i, (x, tip) in enumerate(((8, "hook"), (13, "rake"), (18, "probe"), (23, "half"))):
        px(img, [(x, y) for y in range(5, 22)], IRON[3] if i % 2 else IRON[2])
        px(img, [(x + 1, y) for y in range(14, 22)], IRON[1])                                          # the handles, wider
        if tip == "hook":
            px(img, [(x + 1, 5), (x + 1, 6)], IRON[3])
        elif tip == "rake":
            px(img, [(x + 1, 6), (x + 1, 8), (x + 1, 10)], IRON[3])
        elif tip == "half":
            px(img, [(x + 1, 6), (x + 1, 7), (x + 2, 7)], IRON[3])
    return selective_outline(img)


# ---------------------------------------------------------------- clothes

def dyes():
    """(name, rgb) for every Cloth material in the defs, so icons follow the dye list."""
    text = open(os.path.join(os.path.dirname(__file__), "..", "data", "defs", "outfits.json")).read()
    defs = json.loads(re.sub(r"^\s*//.*$", "", text, flags=re.M))
    return [(m["name"].lower(), tuple(round(c * 255) for c in m["color"])) for m in defs["materials"] if m.get("of") == "Cloth"]


def wool(x, y):
    return (-0.05 if (x + 2 * y) % 5 == 0 else 0) + bayer(x, y) * 0.06


def trim(img, x0, x1, y):
    for x in range(x0, x1 + 1):
        if img.getpixel((x, y))[3]:
            px(img, [(x, y)], GOLD[2] if (x // 2) % 2 == 0 else TRIM_DARK[1])


def tunic(cloth, long_=False):
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    hem = 29 if long_ else 26
    body = [(9, 5), (13, 4), (16, 7), (19, 4), (23, 5), (29, 14), (26, 16), (23, 12), (24, hem), (8, hem), (9, 12), (6, 16), (3, 14)]
    shape(img, lambda d: d.polygon(body, fill=255), cloth, tex=wool)
    trim(img, 8, 24, hem - 1)
    px(img, [(x, 17) for x in range(9, 24)], LEATHER[1])
    px(img, [(15, 17), (16, 17)], BRONZE[2])
    if long_:
        px(img, [(13 + min(3, (y - 6) // 3), y) for y in range(6, 17)] + [(16, y) for y in range(18, hem - 1)], GOLD[1])
    else:
        px(img, [(14, 5), (15, 6), (16, 6), (17, 5)], GOLD[2])
    return selective_outline(img)


def apron_dress(cloth):
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    shape(img, lambda d: d.polygon([(10, 8), (22, 8), (22, 15), (26, 29), (6, 29), (10, 15)], fill=255), cloth, tex=wool)
    shape(img, lambda d: (d.rectangle([10, 3, 12, 8], fill=255), d.rectangle([20, 3, 22, 8], fill=255)), cloth[1:], radius=0.8)
    trim(img, 6, 26, 27)
    px(img, [(11, 9), (12, 9), (20, 9), (21, 9)], BRONZE[3])
    px(img, [(11, 10), (12, 10), (20, 10), (21, 10)], BRONZE[1])
    for k, x in enumerate(range(13, 20)):
        px(img, [(x, 11 + (0 if x in (13, 19) else 1))], BEAD_AMBER if k % 2 == 0 else BEAD_BLUE)
    return selective_outline(img)


def vest():
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    shape(img, lambda d: d.polygon([(9, 4), (13, 4), (15, 13), (17, 13), (19, 4), (23, 4), (25, 10), (24, 27), (8, 27), (7, 10)], fill=255),
          LEATHER, tex=lambda x, y: bayer(x, y) * 0.09)
    px(img, [(16, y) for y in range(13, 27)], LEATHER[0])
    px(img, [(15 if k % 2 else 17, 14 + k * 2) for k in range(6)], WRAP[1])
    px(img, [(10, 4), (11, 5), (12, 5), (20, 5), (21, 5), (22, 4)], FUR[2])
    return selective_outline(img)


def trousers(cloth):
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    shape(img, lambda d: d.polygon([(8, 3), (24, 3), (25, 22), (18, 22), (16, 9), (14, 22), (7, 22)], fill=255), cloth, tex=wool)
    shape(img, lambda d: (d.rectangle([7, 22, 13, 29], fill=255), d.rectangle([19, 22, 25, 29], fill=255)), WRAP, radius=1.0)
    for y in range(23, 29):
        px(img, [(8 + (y - 23) % 4, y), (20 + (y - 23) % 4, y)], WRAP[0])
    px(img, [(x, 4) for x in range(9, 24)], cloth[1])
    return selective_outline(img)


def skirt(cloth):
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    shape(img, lambda d: d.polygon([(10, 4), (22, 4), (27, 28), (5, 28)], fill=255), cloth,
          tex=lambda x, y: wool(x, y) + (-0.12 if (x + y // 6) % 3 == 0 and y > 8 else 0))
    trim(img, 5, 27, 26)
    trim(img, 5, 27, 27)
    px(img, [(x, 5) for x in range(10, 23)], cloth[1])
    return selective_outline(img)


def shoes(boots):
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    for x0 in (3, 17):
        top = 8 if boots else 18
        shape(img, lambda d: d.polygon([(x0 + 2, top), (x0 + 8, top), (x0 + 8, 22), (x0 + 12, 25), (x0 + 12, 28), (x0 + 1, 28)], fill=255), SHOE, radius=1.2)
        if boots:
            for x in range(x0 + 2, x0 + 9):
                px(img, [(x, top), (x, top + 1)], FUR[3] if x % 2 else FUR[2])
                px(img, [(x, top + 2)], FUR[1] if x % 3 == 0 else SHOE[2])
        else:
            px(img, [(x, 21) for x in range(x0 + 2, x0 + 8)], SHOE[3])
    return selective_outline(img)


def cloak(cloth, hooded):
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    shape(img, lambda d: d.polygon([(11, 4), (21, 4), (27, 12), (29, 29), (3, 29), (5, 12)], fill=255), cloth,
          tex=lambda x, y: wool(x, y) + (-0.14 if (x + y // 5) % 4 == 0 and y > 12 else 0))
    if hooded:
        shape(img, lambda d: d.polygon([(10, 2), (22, 2), (23, 9), (16, 13), (9, 9)], fill=255), cloth[1:], radius=1.2, tex=wool)
        px(img, [(16, y) for y in range(3, 12)], cloth[0])
    px(img, [(10, 8), (11, 8), (10, 9), (11, 9)], BRONZE[2])          # the ring pin
    px(img, [(10, 8)], BRONZE[3])
    return selective_outline(img)


def mantle():
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    shape(img, lambda d: d.polygon([(10, 6), (22, 6), (29, 14), (28, 22), (22, 20), (16, 23), (10, 20), (4, 22), (3, 14)], fill=255), FUR,
          tex=lambda x, y: (0.12 if (x * 3 + y * 2) % 5 == 0 else -0.04) + bayer(x, y) * 0.05)
    px(img, [(15, 9), (16, 9)], BRONZE[3])
    px(img, [(15, 10), (16, 10)], BRONZE[1])
    return selective_outline(img)


def scarf(cloth):
    """A wound scarf: a ring of folds, its fringed tail hanging, a bronze ring pin."""
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    folds = lambda x, y: wool(x, y) + (-0.16 if (y + x // 4) % 3 == 0 else 0)
    shape(img, lambda d: d.ellipse([4, 4, 28, 18], fill=255), cloth, tex=folds)
    shape(img, lambda d: d.ellipse([10, 7, 22, 14], fill=255), cloth[:2], radius=1.0)
    shape(img, lambda d: d.polygon([(18, 14), (24, 13), (23, 26), (19, 27)], fill=255), cloth, radius=1.2, tex=folds)
    px(img, [(19 + k, 28 + k % 2) for k in range(5)], cloth[0])
    px(img, [(22, 8), (23, 8), (22, 9), (23, 9)], BRONZE[2])
    return selective_outline(img)


def wool_mantle(cloth):
    """A short mantle laid flat: a wide half-round of wool, a fringed hem."""
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    folds = lambda x, y: wool(x, y) + (-0.15 if (x + y // 5) % 4 == 0 and y > 12 else 0)
    shape(img, lambda d: d.pieslice([2, -6, 30, 28], 20, 160, fill=255), cloth, tex=folds)
    shape(img, lambda d: d.ellipse([11, 2, 21, 8], fill=255), cloth[:2], radius=1.0)
    px(img, [(x, 26 - abs(x - 16) // 4) for x in range(6, 27, 2)], cloth[0])
    return selective_outline(img)


def bolt(cloth):
    """A folded length of wool cloth."""
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    for k, y in enumerate((20, 14, 8)):
        shape(img, lambda d: d.rounded_rectangle([5 + k, y, 27 - k, y + 7], radius=2, fill=255), cloth, radius=1.2, tex=wool)
        px(img, [(x, y + 7) for x in range(6 + k, 27 - k)], cloth[0])
    return selective_outline(img)


DYED = (("tunic", lambda c: tunic(c)), ("kaftan", lambda c: tunic(c, long_=True)), ("apron_dress", apron_dress), ("trousers", trousers),
        ("skirt", skirt), ("cloak", lambda c: cloak(c, False)), ("hooded_cloak", lambda c: cloak(c, True)), ("scarf", scarf), ("mantle", wool_mantle), ("cloth", bolt))


def save(img, name):
    path = os.path.join(ROOT, "items", name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def clothes():
    for name, rgb in dyes():
        cloth = ramp(rgb, 5, main=3)
        for item, make in DYED:
            save(make(cloth), f"{item}_{name}")
            if name == "undyed":
                save(make(cloth), item)
    for name, make in (("leather_vest", vest), ("turnshoes", lambda: shoes(False)), ("fur_boots", lambda: shoes(True)), ("fur_mantle", mantle)):
        save(make(), name)


def generate():
    clothes()
    for name, make in (("leather_jerkin", jerkin), ("leather_cap", cap), ("iron_helm", helm), ("horned_helm", horned_helm), ("chain_shirt", byrnie), ("satchel", satchel),
                       ("ale", lambda: tankard(ramp((176, 118, 40), 5))), ("strongbox", strongbox), ("lockpicks", lockpicks)):
        path = os.path.join(ROOT, "items", name + ".png")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        make().save(path)


if __name__ == "__main__":
    generate()
