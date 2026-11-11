"""Norse HD-2D toolbar, order and care icons (ui/icon_*, 24x24) and the fire effects (fx/fire, fx/ember).

One set, drawn like the inventory icons: shaded metal, wood and bone with madder-red and woad-blue accents,
lit from the top left, and a selective outline darkened into an ink line so each glyph reads on the dark UI
and over the world alike. Same names and sizes as before. The other fx (breath, smoke, glint, zzz, speech,
heart, shell) and ui/selection, ui/designate_x, ui/bubble are white or neutral art the game tints, so they
stay as they are.
"""
import math
import os

from PIL import Image

from norse_art import ramp, mask_of, shade_shape, shade_over, selective_outline, ribbon
from norse_gear import METAL, WALNUT, ASH
from norse_buildings import LEATHER, OAT, WOAD, MADDER, STRAW, LINEN
from norse_glow import GLOW, HOT, CORE, EMBER

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
S = 24
IRON, STEEL, GOLD, BRONZE, SILVER = METAL["iron"], METAL["steel"], METAL["gold"], METAL["bronze"], METAL["silver"]
BONE = ramp((228, 216, 190), 5, main=2, dark=0.6, light=1.1)
RED = ramp((196, 58, 48), 5, dark=0.5, light=1.3)
HEART = ramp((214, 72, 96), 5, dark=0.5, light=1.3)
GREEN = ramp((84, 140, 70), 5, dark=0.5, light=1.4)
MOON = ramp((236, 222, 160), 4, main=2, dark=0.7, light=1.1)
WATER = ramp((96, 156, 206), 5, dark=0.55, light=1.35)
FISHC = ramp((120, 160, 190), 5, dark=0.5, light=1.35)
MEAT = ramp((176, 70, 58), 5, dark=0.5, light=1.35)
INK = (30, 22, 20, 255)


def canvas():
    return Image.new("RGBA", (S, S), (0, 0, 0, 0))


def put(img, x, y, c):
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((int(x), int(y)), c)


def part(img, draw, colors, line=INK, **kw):
    kw.setdefault("radius", 0.8)
    kw.setdefault("slope", 1.5)
    m = mask_of(img.size, draw)
    if line is None:
        shade_shape(img, m, colors, **kw)
    else:
        shade_over(img, m, colors, line, **kw)


def poly(pts):
    return lambda d: d.polygon(pts, fill=255)


def stick(a, z, w0, w1=None):
    w1 = w0 if w1 is None else w1
    return lambda d: d.polygon(ribbon([a, ((a[0] + z[0]) / 2, (a[1] + z[1]) / 2), z], [w0, (w0 + w1) / 2, w1]), fill=255)


def ell(box):
    return lambda d: d.ellipse(box, fill=255)


def ink_outline(img):
    """The selective outline, pulled further toward ink so small glyphs hold their shape."""
    return selective_outline(img, strength=0.22, cool=0.5)


# ---------------------------------------------------------------- glyphs

def select(img):
    part(img, poly([(5, 2), (5, 18), (9, 14), (12, 21), (15, 20), (12, 13), (18, 13)]), BONE)


def cancel(img):
    part(img, stick((4, 4), (19, 19), 4), RED)
    part(img, stick((19, 4), (4, 19), 4), RED)


def axe(img):
    part(img, stick((4, 21), (15, 4), 2.6, 2.2), WALNUT)
    part(img, poly([(12, 5), (15, 2), (18, 3), (21, 1), (23, 6), (22, 12), (19, 15), (17, 11), (16, 8), (13, 8)]), IRON, radius=1.0)


def pick(img):
    part(img, stick((6, 22), (13, 6), 2.6, 2.2), WALNUT)
    part(img, lambda d: d.polygon(ribbon([(2, 9), (7, 5), (13, 4), (18, 5), (22, 9)], [1, 3, 3.6, 3, 1]), fill=255), IRON)


def spade(img):
    part(img, stick((16, 3), (10, 14), 2.4), WALNUT)
    part(img, stick((13, 2), (19, 5), 2.0), WALNUT)
    part(img, poly([(6, 13), (12, 15), (10, 22), (5, 22), (3, 18)]), IRON, radius=1.0)


def berries(img):
    part(img, poly([(10, 2), (17, 3), (12, 7)]), GREEN)
    for x, y in ((8, 11), (15, 10), (11, 16), (17, 16)):
        part(img, ell([x - 4, y - 4, x + 4, y + 4]), RED, radius=1.0)
        put(img, x - 2, y - 2, (255, 236, 230, 255))


def hammer(img):
    part(img, stick((5, 21), (14, 9), 2.6, 2.2), WALNUT)
    part(img, poly([(8, 5), (17, 1), (21, 9), (12, 12)]), IRON, radius=1.0)


def deconstruct(img):
    hammer(img)
    part(img, stick((2, 2), (9, 9), 2.2), RED)
    part(img, stick((9, 2), (2, 9), 2.2), RED)


def flame(img):
    for pts, col in (([(12, 1), (18, 9), (19, 16), (15, 22), (9, 22), (5, 16), (7, 8), (10, 11)], GLOW),
                     ([(12, 9), (16, 15), (14, 21), (10, 21), (8, 16)], HOT),
                     ([(12, 15), (14, 19), (12, 21), (10, 19)], CORE)):
        img.paste(col, mask=mask_of(img.size, poly(pts)))


def crate(img):
    part(img, lambda d: d.rectangle([3, 6, 20, 20], fill=255), WALNUT, radius=1.0)
    for y in (6, 13, 20):
        for x in range(3, 21):
            put(img, x, y, WALNUT[0])
    for x in (3, 20):
        for y in range(6, 21):
            put(img, x, y, IRON[2])
    for x, y in ((3, 6), (20, 6), (3, 20), (20, 20)):
        put(img, x, y, IRON[4])


def clearzone(img):
    part(img, lambda d: d.rectangle([3, 8, 18, 21], fill=255), WALNUT, radius=1.0)
    for y in (8, 14, 21):
        for x in range(3, 19):
            put(img, x, y, WALNUT[0])
    part(img, stick((12, 2), (22, 12), 3), RED)
    part(img, stick((22, 2), (12, 12), 3), RED)


def moon(img):
    m = mask_of(img.size, ell([3, 3, 20, 20]))
    cut = mask_of(img.size, ell([9, 1, 24, 16]))
    m.paste(0, mask=cut)
    shade_over(img, m, MOON, INK, radius=1.2, slope=1.3)


def bowl(img):
    part(img, lambda d: d.chord([2, 4, 21, 22], 0, 180, fill=255), WALNUT, radius=1.0)
    part(img, ell([3, 10, 20, 16]), MEAT[1:], radius=1.0, slope=-1.0)
    for x, y in ((8, 12), (13, 13), (16, 12)):
        put(img, x, y, (226, 180, 100, 255))


def alert(img):
    part(img, poly([(9, 2), (15, 2), (14, 15), (10, 15)]), RED)
    part(img, ell([9, 17, 15, 22]), RED)


def sword(img):
    part(img, lambda d: d.polygon(ribbon([(7, 17), (14, 10), (21, 3)], [3.2, 2.8, 0.8]), fill=255), STEEL, radius=0.6, slope=1.8)
    part(img, poly([(3, 13), (5, 11), (12, 18), (10, 20)]), BRONZE)
    part(img, stick((3, 21), (7, 17), 2.2), LEATHER)
    part(img, ell([1, 19, 5, 23]), BRONZE)


def doctor(img):
    """A healer's bundle: a bandage cross on a linen pad."""
    part(img, lambda d: d.rounded_rectangle([2, 2, 21, 21], radius=4, fill=255), LINEN, radius=1.2)
    part(img, lambda d: (d.rectangle([9, 4, 14, 19], fill=255), d.rectangle([4, 9, 19, 14], fill=255)), RED)


def haul(img):
    """A sack over the shoulder: a tied bag with an arrow onward."""
    part(img, poly([(5, 21), (4, 13), (8, 7), (15, 7), (19, 13), (18, 21)]), OAT, radius=1.4)
    part(img, poly([(9, 7), (14, 7), (13, 4), (10, 4)]), LEATHER)
    part(img, poly([(14, 15), (20, 15), (20, 12), (24, 17), (20, 22), (20, 19), (14, 19)]), BONE)


def heart(img, colors=HEART, box=(2, 3, 21, 21)):
    x0, y0, x1, y1 = box
    mx = (x0 + x1) / 2
    part(img, lambda d: (d.ellipse([x0, y0, mx + 1, y0 + (y1 - y0) * 0.55], fill=255),
                         d.ellipse([mx - 1, y0, x1, y0 + (y1 - y0) * 0.55], fill=255),
                         d.polygon([(x0 + 0.5, y0 + (y1 - y0) * 0.35), (x1 - 0.5, y0 + (y1 - y0) * 0.35), (mx, y1)], fill=255)),
         colors, radius=1.4, slope=1.3)
    put(img, x0 + 3, y0 + 2, (255, 236, 236, 255))


def hunt(img):
    part(img, lambda d: d.arc([2, 2, 16, 22], 280, 80, fill=255, width=3), ASH, radius=0.5)
    for y in range(3, 21):
        put(img, 9, y, OAT[3])
    part(img, stick((4, 12), (19, 12), 1.6), WALNUT, line=None)
    part(img, poly([(19, 9), (23, 12), (19, 15)]), IRON)
    part(img, poly([(2, 10), (5, 12), (2, 14)]), RED)


def fish(img):
    part(img, ell([2, 7, 17, 17]), FISHC, radius=1.0)
    part(img, poly([(15, 12), (22, 6), (20, 12), (22, 18)]), FISHC)
    for x in range(4, 16):
        put(img, x, 8, FISHC[1])
    put(img, 6, 11, INK); put(img, 5, 10, (240, 244, 250, 255))


def butcher(img):
    """A seax and a cut of meat."""
    part(img, ell([2, 11, 14, 21]), MEAT, radius=1.0)
    put(img, 5, 13, (236, 224, 204, 255)); put(img, 6, 13, (236, 224, 204, 255))
    part(img, poly([(8, 12), (17, 3), (21, 1), (19, 7), (11, 15)]), STEEL, radius=0.6, slope=1.8)
    part(img, stick((18, 8), (22, 12), 2.6), WALNUT)


def tend(img):
    heart(img, box=(5, 1, 19, 14))
    part(img, poly([(2, 16), (8, 14), (16, 15), (21, 13), (22, 16), (16, 21), (6, 22), (2, 20)]), OAT, radius=1.0)


def scratch(img):
    for k in range(3):
        part(img, stick((3 + k * 4, 6), (8 + k * 4, 18), 2.0), BONE)
    heart(img, box=(14, 2, 23, 10))


def play(img):
    part(img, ell([3, 3, 20, 20]), RED, radius=1.6)
    part(img, lambda d: d.arc([9, 2, 25, 21], 110, 250, fill=255, width=2), OAT, line=None)     # a stitched leather seam
    for y in (6, 10, 14, 18):
        put(img, 9 + abs(y - 12) // 3, y, LEATHER[1])


def groom(img):
    part(img, lambda d: d.rounded_rectangle([3, 5, 20, 11], radius=3, fill=255), WALNUT, radius=1.0)
    for x in range(4, 20):
        for y in range(12, 18 + (x % 3 == 0)):
            put(img, x, y, STRAW[3] if (x + y) % 3 else STRAW[1])
    for x in (6, 11, 16):
        put(img, x, 8, BRONZE[4])


def comfort(img):
    part(img, poly([(7, 2), (11, 10), (10, 13), (5, 13), (3, 10)]), WATER, radius=1.0)
    part(img, lambda d: (d.ellipse([7, 12, 16, 21], fill=255), d.ellipse([12, 10, 22, 20], fill=255), d.ellipse([4, 15, 12, 22], fill=255)),
         ramp((236, 236, 240), 4, main=2, dark=0.75, light=1.05), radius=1.6)


def treasure(img):
    for x, y in ((6, 18), (16, 18), (11, 15), (11, 11), (11, 7)):
        part(img, ell([x - 5, y - 3, x + 5, y + 3]), GOLD)
        for k in range(-2, 3):
            put(img, x + k, y - 2, GOLD[5])
    put(img, 18, 8, CORE); put(img, 19, 7, HOT)


def feed(img):
    """A haunch on the bone."""
    part(img, ell([2, 4, 16, 18]), MEAT, radius=1.4)
    part(img, stick((13, 14), (20, 20), 2.6), BONE)
    part(img, ell([18, 18, 23, 23]), BONE)


def train(img):
    part(img, lambda d: d.ellipse([4, 2, 20, 22], outline=255, width=3), BRONZE, radius=0.5)
    for x, y in ((3, 3), (21, 4), (2, 20)):
        put(img, x, y, HOT); put(img, x + 1, y, GLOW)


def call(img):
    """A drinking horn blown as a signal: banded with bronze, sound arcs going out."""
    part(img, lambda d: d.polygon(ribbon([(2, 17), (8, 15), (13, 10), (15, 4)], [2, 3.4, 5, 6.4]), fill=255), BONE, radius=1.0)
    for x, y in ((6, 15), (11, 12)):
        put(img, x, y, BRONZE[3]); put(img, x + 1, y + 1, BRONZE[1]); put(img, x - 1, y - 1, BRONZE[3])
    for r in (4, 7):
        part(img, lambda d, r=r: d.arc([16 - r, 4 - r, 16 + r, 4 + r], 300, 60, fill=255, width=1), BONE[2:], line=None)


def stay(img):
    part(img, poly([(6, 22), (5, 13), (5, 6), (7, 5), (8, 11), (9, 3), (11, 2), (12, 10), (13, 3), (15, 3), (15, 11), (17, 5), (19, 6), (18, 14),
                    (21, 11), (22, 13), (18, 20), (15, 22)]), ramp((222, 180, 146), 5, dark=0.6, light=1.15), radius=1.0)


def breath(img):
    """A dragon's breath: a cone of fire roaring out of a horned head."""
    for pts, col in (([(9, 10), (23, 2), (23, 22)], GLOW), ([(10, 11), (22, 6), (22, 17)], HOT), ([(11, 11), (19, 9), (19, 14)], CORE)):
        img.paste(col, mask=mask_of(img.size, poly(pts)))
    part(img, poly([(1, 7), (6, 5), (11, 9), (11, 14), (6, 16), (1, 13)]), MADDER, radius=1.0)
    part(img, poly([(2, 7), (0, 2), (5, 6)]), BONE)
    put(img, 6, 9, HOT)


def fly(img):
    """A membranous wing, spread."""
    part(img, poly([(2, 20), (6, 9), (11, 3), (16, 2), (22, 6), (19, 9), (21, 13), (17, 14), (18, 19), (12, 17), (10, 21)]), MADDER, radius=1.2)
    for a, z in (((2, 20), (16, 2)), ((2, 20), (22, 6)), ((2, 20), (21, 13)), ((2, 20), (18, 19))):
        part(img, stick(a, z, 1.2, 0.8), BONE[1:], line=None)


def ride(img):
    """A saddle: high cantle and pommel, girth strap and a stirrup."""
    part(img, lambda d: d.polygon([(2, 6), (6, 10), (17, 10), (21, 4), (22, 12), (18, 15), (5, 15), (2, 12)], fill=255), LEATHER, radius=1.2)
    for x in range(5, 18, 3):
        put(img, x, 12, BRONZE[3])
    part(img, stick((11, 15), (11, 20), 1.4), LEATHER[:3])
    part(img, lambda d: d.arc([8, 18, 14, 23], 0, 360, fill=255, width=1), IRON, line=None)


# ---------------------------------------------------------------- fx

def fx_fire():
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    for pts, col in (([(16, 1), (21, 9), (24, 6), (25, 17), (22, 27), (10, 27), (7, 18), (9, 8), (12, 12)], GLOW),
                     ([(16, 9), (20, 16), (19, 26), (13, 26), (11, 18), (14, 14)], HOT),
                     ([(16, 17), (18, 22), (17, 26), (14, 26), (14, 22)], CORE)):
        img.paste(col, mask=mask_of(img.size, poly(pts)))
    for x, y in ((9, 6), (23, 3), (19, 1)):
        put(img, x, y, HOT)
    for x in range(10, 23):                                     # the red heart where it meets the fuel
        put(img, x, 27, EMBER)
    return img


def fx_ember():
    img = Image.new("RGBA", (6, 6), (0, 0, 0, 0))
    for x, y, c in ((2, 0, GLOW), (1, 1, GLOW), (2, 1, HOT), (3, 1, GLOW), (0, 2, EMBER), (1, 2, HOT), (2, 2, CORE), (3, 2, HOT), (4, 2, GLOW),
                    (1, 3, GLOW), (2, 3, HOT), (3, 3, GLOW), (2, 4, EMBER)):
        put(img, x, y, c)
    return img


ICONS = (("select", select), ("cancel", cancel), ("chop", axe), ("mine", pick), ("dig", spade), ("harvest", berries),
         ("build", hammer), ("deconstruct", deconstruct), ("burn", flame), ("stockpile", crate), ("clearzone", clearzone),
         ("sleep", moon), ("eat", bowl), ("alert", alert), ("fight", sword), ("doctor", doctor), ("haul", haul),
         ("bond", heart), ("hunt", hunt), ("fish", fish), ("butcher", butcher), ("tend", tend), ("scratch", scratch),
         ("play", play), ("groom", groom), ("comfort", comfort), ("treasure", treasure), ("feed", feed), ("train", train),
         ("call", call), ("stay", stay), ("breath", breath), ("fly", fly), ("ride", ride))


def icons():
    out = {}
    for name, fn in ICONS:
        img = canvas()
        fn(img)
        out[name] = ink_outline(img)
    return out


def generate():
    for name, img in icons().items():
        img.save(os.path.join(ROOT, "ui", f"icon_{name}.png"))
    fx_fire().save(os.path.join(ROOT, "fx", "fire.png"))
    fx_ember().save(os.path.join(ROOT, "fx", "ember.png"))


if __name__ == "__main__":
    generate()
