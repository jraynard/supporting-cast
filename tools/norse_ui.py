"""Norse HD-2D toolbar and order icons (ui/icon_*, 24x24) and the fire effects (fx/fire, fx/ember).

One set, drawn like the inventory icons: shaded metal, wood and bone with madder-red and woad-blue accents,
lit from the top left, and a selective outline darkened into an ink line so each glyph reads on the dark UI
and over the world alike. Same names and sizes as before. The other fx (breath, smoke, glint, zzz, speech)
and ui/selection, ui/designate_x, ui/bubble are white or neutral art the game tints, so they
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
QUEST_EMBER = ramp((252, 163, 66), 5, dark=0.5, light=1.25)  # the brand's ember: the quest marker
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


def call(img):
    """A drinking horn blown as a signal: banded with bronze, sound arcs going out."""
    part(img, lambda d: d.polygon(ribbon([(2, 17), (8, 15), (13, 10), (15, 4)], [2, 3.4, 5, 6.4]), fill=255), BONE, radius=1.0)
    for x, y in ((6, 15), (11, 12)):
        put(img, x, y, BRONZE[3]); put(img, x + 1, y + 1, BRONZE[1]); put(img, x - 1, y - 1, BRONZE[3])
    for r in (4, 7):
        part(img, lambda d, r=r: d.arc([16 - r, 4 - r, 16 + r, 4 + r], 300, 60, fill=255, width=1), BONE[2:], line=None)


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
         ("hunt", hunt), ("fish", fish), ("butcher", butcher), ("call", call))


# ---------------------------------------------------------------- chrome (nine-slice frames for the UI kit)
# Narsil.Ui draws these nine-sliced: corners at a whole-pixel scale, edges and middle tiled. The
# border is 6 px (UiFrames.Border), so everything decorative stays in the outer 6 px and the
# middle tiles cleanly. Pixel art, drawn pixel by pixel: oak planks, an iron rim, riveted corners.

OAK = ramp((92, 62, 40), 6, main=2, dark=0.5, light=1.45)
IRONR = METAL["iron"]


def _px(img, x, y, c):
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((x, y), c if len(c) == 4 else (*c, 255))


def _plank(x, y, wood, period=10, top=6):
    """Oak planks running across: a lit top edge, a dark seam, long grain streaks, staggered butt joints."""
    row = (y - top) % period
    plank = (y - top) // period
    if row == period - 1:
        return wood[0]
    if row == 0:
        return wood[3]
    if (x - top + plank * 9) % 20 == 0:
        return wood[1]                                  # a butt joint, staggered plank to plank
    streak = (x + plank * 7 + row * 5) % 23
    if row in (2, 6) and streak < 9:
        return wood[1]
    if row == 4 and streak > 16:
        return wood[3]
    return wood[2]


def frame_panel():
    W = 32
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    for y in range(W):
        for x in range(W):
            _px(img, x, y, _plank(x, y, OAK))
    # A shadow under the rim, then the rim itself: iron, lit from the top left.
    for i in range(W):
        for d, col in ((3, OAK[0]), (4, OAK[1])):
            _px(img, i, d, col); _px(img, d, i, col)
            _px(img, i, W - 1 - d, OAK[1]); _px(img, W - 1 - d, i, OAK[1])
        _px(img, i, 1, IRONR[4]); _px(img, 1, i, IRONR[4])
        _px(img, i, 2, IRONR[3]); _px(img, 2, i, IRONR[3])
        _px(img, i, W - 2, IRONR[1]); _px(img, W - 2, i, IRONR[2])
        _px(img, i, W - 3, IRONR[2]); _px(img, W - 3, i, IRONR[2])
        _px(img, i, 0, INK); _px(img, 0, i, INK); _px(img, i, W - 1, INK); _px(img, W - 1, i, INK)
    # Riveted corner brackets.
    for cx, cy in ((0, 0), (W - 6, 0), (0, W - 6), (W - 6, W - 6)):
        for y in range(6):
            for x in range(6):
                edge = x in (0, 5) or y in (0, 5)
                _px(img, cx + x, cy + y, INK if edge else (IRONR[4] if (x + y) < 5 else IRONR[2]))
        _px(img, cx + 2, cy + 2, IRONR[5]); _px(img, cx + 3, cy + 3, IRONR[1]); _px(img, cx + 3, cy + 2, IRONR[3]); _px(img, cx + 2, cy + 3, IRONR[3])
    return img


def frame_title():
    """A carved plate for a panel's title: lighter oak, bevelled, iron caps at the ends."""
    W, H = 32, 16
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    wood = ramp((132, 92, 56), 6, main=2, dark=0.5, light=1.4)
    for y in range(H):
        for x in range(W):
            c = wood[4] if y == 1 else wood[3] if y <= 3 else wood[1] if y >= H - 3 else wood[2]
            if 3 < y < H - 3 and (x + y * 5) % 19 < 6 and y in (6, 9):
                c = wood[1]
            _px(img, x, y, c)
    for x in range(W):
        _px(img, x, 0, INK); _px(img, x, H - 1, INK); _px(img, x, H - 2, wood[0])
    for y in range(H):
        _px(img, 0, y, INK); _px(img, W - 1, y, INK)
        for x in (1, 2, 3, 4):
            _px(img, x, y, IRONR[4] if x == 1 else IRONR[3] if x == 2 else IRONR[2])
            _px(img, W - 1 - x, y, IRONR[3] if x == 1 else IRONR[2] if x == 2 else IRONR[1])
    for x, y in ((2, 3), (W - 3, 3), (2, H - 4), (W - 3, H - 4)):
        _px(img, x, y, IRONR[5])
    return img


def frame_inset():
    """A well cut into the panel: dark, shadowed at the top left, lit at the bottom right edge."""
    W = 16
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    deep = ramp((40, 28, 20), 5, main=2, dark=0.6, light=1.5)
    for y in range(W):
        for x in range(W):
            c = deep[2]
            if x <= 1 or y <= 1:
                c = deep[0]
            elif x == 2 or y == 2:
                c = deep[1]
            if x == W - 1 or y == W - 1:
                c = OAK[3]
            _px(img, x, y, (*c[:3], 235))
    return img


def bar_trough():
    """The iron channel a bar fills."""
    W, H = 24, 14
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for y in range(H):
        for x in range(W):
            edge = x in (0, W - 1) or y in (0, H - 1)
            rim = x in (1, W - 2) or y in (1, H - 2)
            c = INK if edge else (IRONR[3] if y == 1 else IRONR[1]) if rim else (18, 14, 12)
            if not edge and not rim and y == 2:
                c = (8, 6, 6)
            _px(img, x, y, c)
    return img


def keycap():
    """A key, iron-faced, with a lip at the bottom."""
    W = 16
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    for y in range(W):
        for x in range(W):
            corner = (x in (0, W - 1)) and (y in (0, W - 1))
            if corner:
                continue
            edge = x in (0, W - 1) or y in (0, W - 1)
            c = INK if edge else IRONR[1] if y >= W - 4 else IRONR[3] if y == 1 else (52, 50, 54)
            _px(img, x, y, c)
    return img


def coin(img):
    part(img, ell([3, 3, 21, 21]), GOLD, radius=1.2)
    part(img, ell([8, 8, 16, 16]), GOLD[1:] + [GOLD[-1]], radius=0.6)


def quest(img):
    """A rolled map with a red mark on it."""
    part(img, lambda d: d.rounded_rectangle([4, 5, 20, 19], radius=2, fill=255), LINEN, radius=1.0)
    part(img, ell([2, 3, 7, 21]), LINEN[1:] + [LINEN[-1]])
    part(img, ell([17, 3, 22, 21]), LINEN[1:] + [LINEN[-1]])
    part(img, stick((9, 9), (15, 15), 2.2), RED)
    part(img, stick((15, 9), (9, 15), 2.2), RED)


def wound(img):
    """A drop of blood."""
    part(img, lambda d: (d.polygon([(12, 2), (6, 13), (18, 13)], fill=255), d.ellipse([6, 8, 18, 21], fill=255)), RED, radius=1.2)


def marker(img):
    """The quest marker, "!": over a customer's head it means they have work for you (the brand's emblem)."""
    part(img, poly([(9, 1), (15, 1), (14, 15), (10, 15)]), QUEST_EMBER, radius=1.0)
    part(img, ell([9, 17, 15, 23]), QUEST_EMBER)


def home(img):
    """A cottage, thatched, with its door: over your own home, so you know which is yours."""
    part(img, lambda d: d.rectangle([5, 11, 18, 21], fill=255), LINEN, radius=1.0)       # the walls
    part(img, poly([(1, 12), (11.5, 2), (22, 12)]), STRAW, radius=1.0)                  # the thatch
    part(img, lambda d: d.rectangle([10, 15, 13, 21], fill=255), WALNUT, radius=0.6)     # the door
    put(img, 12, 18, GOLD[4])


def good(img):
    """A tick, carved."""
    part(img, poly([(3, 12), (7, 9), (10, 13), (19, 3), (22, 6), (10, 20)]), GREEN, radius=1.0)


def work_anvil():
    """A horned anvil on a stump, side on, for the hammering scene."""
    img = Image.new("RGBA", (48, 32), (0, 0, 0, 0))
    part(img, poly([(3, 6), (12, 6), (15, 4), (44, 4), (44, 11), (36, 13), (34, 18), (14, 18), (12, 13), (8, 11)]), IRONR[1:], radius=1.2)  # horn, face, waist
    part(img, poly([(16, 18), (32, 18), (36, 22), (12, 22)]), IRONR[1:], radius=0.8)                                                 # the foot
    part(img, lambda d: d.rectangle([14, 22, 34, 31], fill=255), OAK[1:], radius=1.0)                                             # the stump
    for x in range(15, 44):
        put(img, x, 4, IRONR[5])                                                                                                   # the face, polished
    return ink_outline(img)


def work_hammer():
    """A smith's hammer, lying flat: the head at the right, the handle running left (the pivot end)."""
    img = Image.new("RGBA", (32, 14), (0, 0, 0, 0))
    part(img, stick((1, 8), (24, 7), 2.4, 2.8), WALNUT, radius=0.6)
    part(img, lambda d: d.rounded_rectangle([22, 1, 30, 13], radius=1, fill=255), IRONR[1:], radius=0.8)
    for y in range(2, 13):
        put(img, 29, y, IRONR[5])
    return ink_outline(img)


def work_billet():
    """The bar being worked: drawn pale, the game tints it by its heat."""
    img = Image.new("RGBA", (26, 6), (0, 0, 0, 0))
    for y in range(6):
        for x in range(26):
            v = 255 if y < 2 else 225 if y < 4 else 190
            if x in (0, 25) or y == 5:
                v = 150
            put(img, x, y, (v, v, v, 255))
    return img


WORK = (("anvil", work_anvil), ("hammer", work_hammer), ("billet", work_billet))


CHROME = (("frame_panel", frame_panel), ("frame_title", frame_title), ("frame_inset", frame_inset),
          ("bar_trough", bar_trough), ("keycap", keycap))
TOAST_ICONS = (("coin", coin), ("quest", quest), ("wound", wound), ("good", good), ("marker", marker), ("home", home))


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
    for name, fn in CHROME:
        fn().save(os.path.join(ROOT, "ui", f"{name}.png"))
    for name, fn in TOAST_ICONS:
        img = canvas()
        fn(img)
        ink_outline(img).save(os.path.join(ROOT, "ui", f"icon_{name}.png"))
    os.makedirs(os.path.join(ROOT, "ui", "work"), exist_ok=True)
    for name, fn in WORK:
        fn().save(os.path.join(ROOT, "ui", "work", f"{name}.png"))
    fx_fire().save(os.path.join(ROOT, "fx", "fire.png"))
    fx_ember().save(os.path.join(ROOT, "fx", "ember.png"))


if __name__ == "__main__":
    generate()
