#!/usr/bin/env python3
"""The carpenter's art. Item icons, drawn like norse_items.py's: a quiver of fletched arrows
(``items/quiver``) and a bundle of goose feathers (``items/feathers``). Faces for the woodshop's
dressing (data/defs/workshop.json), drawn like norse_smithy.py's tool rack: a rack of bows on the wall
(``props/bow_rack``), a barrel of arrows from above (``props/arrows_top``) and a scatter of shavings
on the floor (``props/shavings``). The woodshop's sign is with the others in inn_art.py.
Writes only its own files. Run alone: ``python3 tools/carpenter_art.py``.
"""
import os

from PIL import Image

from norse_art import mask_of, shade_shape, selective_outline, bayer, ramp
from norse_people import LEATHER, IRON
import norse_buildings as nb
import random

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
S = 32
SIZE = (S, S)
SHAFT = ramp((176, 132, 84), 5)
GOOSE = [(92, 94, 98, 255), (128, 130, 134, 255), (172, 174, 176, 255), (214, 214, 212, 255), (240, 240, 236, 255)]


def shape(img, pts_fn, colors, **kw):
    kw.setdefault("radius", 1.6)
    shade_shape(img, mask_of(SIZE, pts_fn), colors, **kw)


def px(img, pts, color):
    for x, y in pts:
        if 0 <= x < S and 0 <= y < S:
            img.putpixel((x, y), color)


def quiver():
    """A leather quiver slung on the slant, four arrows standing out of its mouth with grey fletching."""
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    for i, x in enumerate((13, 16, 19, 22)):                                       # the arrows, behind
        top = 3 + (i % 2) * 2
        px(img, [(x - (y - top) // 6, y) for y in range(top + 4, 14)], SHAFT[2])
        px(img, [(x - 1, top), (x, top), (x - 1, top + 1), (x, top + 1), (x - 1, top + 2), (x, top + 2), (x, top + 3)], GOOSE[3 - i % 2])
        px(img, [(x + 1, top + 1), (x + 1, top + 2)], GOOSE[1])
    body = [(9, 12), (25, 10), (26, 14), (14, 30), (8, 30), (6, 26)]
    shape(img, lambda d: d.polygon(body, fill=255), LEATHER, tex=lambda x, y: bayer(x, y) * 0.08)
    px(img, [(x, 12 - (x - 9) // 8) for x in range(9, 26)], LEATHER[0])           # the rim's stitching
    px(img, [(x, 13 - (x - 9) // 8) for x in range(10, 25)], IRON[2])              # an iron band round the mouth
    px(img, [(8 + k, 26 - k) for k in range(0, 14, 2)], LEATHER[4])                # the strap's edge
    return selective_outline(img)


def feathers():
    """Three long grey goose feathers, tied at the quills."""
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    for i, (dx, lean) in enumerate(((-5, -1), (0, 0), (5, 1))):
        cx = 16 + dx
        shape(img, lambda d, cx=cx, lean=lean: d.polygon([(cx + lean * 4, 3), (cx + 4 + lean * 3, 10), (cx + 2, 24), (cx - 2, 24), (cx - 4 + lean * 3, 10)], fill=255),
              GOOSE, tex=lambda x, y: 0.1 if (x + y) % 3 == 0 else 0.0, radius=1.0)
        px(img, [(cx + round(lean * 4 * (24 - y) / 21), y) for y in range(4, 28)], GOOSE[4] if i != 1 else GOOSE[3])   # the quill
    px(img, [(x, 24) for x in range(11, 22)], LEATHER[1])                          # the tie
    px(img, [(x, 25) for x in range(12, 21)], LEATHER[2])
    return selective_outline(img)


# ---------------------------------------------------------------- the woodshop's dressing

def prop_bow_rack():
    """The woodshop's back wall: two pegs-and-board racks with a pair of hunting bows hung in their
    curve, strings slack, and a bundle of unworked staves leaning beside them."""
    # Oak boards (the neutral prop_planks would come out grey: a preset building's furniture has no material to tint it).
    img = nb.canvas()
    rnd = random.Random(53)
    for i in range(4):
        y0 = i * 8
        nb.board(img, 0, y0, S - 1, y0 + 7, nb.WOODS["_oak"], rnd, True, tone=3 + (i % 2) * 0.3)
    wood = nb.WOODS["_oak"].w
    def peg(x, y):
        nb.put(img, x, y, nb.PALE.w[0]); nb.put(img, x + 1, y, nb.PALE.w[1])
    for cx in (8, 19):                                                   # two bows, belly toward the wall
        for y in range(3, 29):
            t = (y - 3) / 25
            bulge = int(round(4 * (1 - (2 * t - 1) ** 2)))
            nb.put(img, cx - bulge, y, wood[1]); nb.put(img, cx - bulge + 1, y, wood[3])
            nb.put(img, cx + 2, y, (226, 214, 180, 255))                 # the string, straight
        for y in (14, 15, 16):                                           # the grip, wrapped
            nb.put(img, cx - 4, y, nb.LEATHER[1]); nb.put(img, cx - 3, y, nb.LEATHER[2])
        peg(cx - 1, 2); peg(cx - 1, 29)
    for x in (26, 28, 30):                                               # staves, leaning
        for y in range(4, 31):
            nb.put(img, x + (30 - y) // 14, y, wood[2] if x != 28 else wood[1])
    return img


def prop_arrows_top():
    """A barrel's head seen from above, open and full of arrows: a staved rim, and the fletched ends
    standing up, grey and white feathers in a crowd."""
    img = nb.canvas()
    rnd = random.Random(51)
    wood = nb.WOODS["_oak"].w
    for y in range(S):
        for x in range(S):
            d = ((x - 15.5) ** 2 + (y - 15.5) ** 2) ** 0.5
            if 13 <= d < 16:
                nb.put(img, x, y, wood[2] if (x + y) % 5 else wood[1])   # the rim
            elif d < 13:
                nb.put(img, x, y, (34, 26, 20, 255))                     # the dark inside
    for _ in range(26):                                                  # fletchings
        x, y = rnd.randrange(6, 26), rnd.randrange(6, 26)
        if ((x - 15.5) ** 2 + (y - 15.5) ** 2) ** 0.5 > 11:
            continue
        c = GOOSE[rnd.randrange(2, 5)]
        for dx, dy in ((0, 0), (1, 0), (0, 1), (-1, 0), (0, -1)):
            nb.put(img, x + dx, y + dy, c if (dx, dy) == (0, 0) else GOOSE[rnd.randrange(1, 4)])
        nb.put(img, x, y, SHAFT[3])                                      # the nock
    return img


def prop_shavings():
    """Curls of pale shavings and a dusting of sawdust, for the floor round the sawhorse: transparent
    between, so the floor shows through."""
    img = nb.canvas()
    rnd = random.Random(52)
    pale = [(214, 180, 128, 255), (232, 204, 156, 255), (190, 150, 98, 255)]
    for _ in range(90):                                                  # dust
        nb.put(img, rnd.randrange(S), rnd.randrange(S), (206, 176, 132, 170))
    for _ in range(14):                                                  # curls
        x, y = rnd.randrange(2, 28), rnd.randrange(2, 28)
        for k in range(rnd.randrange(3, 6)):
            nb.put(img, x + k, y + (k * k) // 4 % 3, pale[k % 3])
    return img


def generate():
    for name, make in (("quiver", quiver), ("feathers", feathers)):
        from artroot import texture_path  # the game's, or the mod that owns it
        make().save(texture_path("items/" + name))
    nb.save(prop_bow_rack(), "props/bow_rack")
    nb.save(prop_arrows_top(), "props/arrows_top")
    nb.save(prop_shavings(), "props/shavings")


if __name__ == "__main__":
    generate()
    print("wrote the carpenter's art")
