"""Norse HD-2D inventory icons: tools and weapons in every metal tier, bars, coins and jewellery, ores and
fuel, building materials, logs and planks per wood, food, hides and leather, and the odds and ends (bow,
club, rod, bandage, key).

Each shape is drawn once and painted with a metal's own hue-shifted ramp (copper, bronze, iron, steel,
silver, gold, knapped stone), so the tiers differ in colour and in the ramp's shine rather than by a
recolour of one sprite. 32x32, lit from the top left, selective outline, saved as is (no grade). This runs
after placeholder_mining, whose recoloured gear it replaces. The worn-gear icons (helms, mail, jerkins,
satchel) come from norse_items; the ember crystal from norse_glow.
"""
import math
import os
import random

from PIL import Image

from norse_art import ramp, mask_of, shade_shape, shade_over, selective_outline, bayer, mix, ribbon
from norse_buildings import WOODS, IRON as IRON_DARK, LEATHER, LINEN, OAT, WOAD, STRAW, graded, board, nail
from norse_stone import ROCKS, rock_ramp, nugget, ORES

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
S = 32

METAL = {   # 6 steps, shadow to glint
    "stone":  ramp((132, 130, 140), 6, main=3, dark=0.45, light=1.4, cool=20, warm=8),
    "copper": ramp((190, 108, 66), 6, main=3, dark=0.42, light=1.38, cool=14, warm=14),
    "bronze": ramp((178, 132, 70), 6, main=3, dark=0.42, light=1.4, cool=16, warm=10),
    "iron":   ramp((138, 146, 162), 6, main=3, dark=0.4, light=1.42, cool=16, warm=6),
    "steel":  ramp((150, 166, 192), 6, main=3, dark=0.4, light=1.5, cool=14, warm=4),
    "silver": ramp((190, 196, 210), 6, main=3, dark=0.45, light=1.3, cool=12, warm=4),
    "gold":   ramp((214, 168, 56), 6, main=3, dark=0.45, light=1.25, cool=10, warm=8),
}
WALNUT = ramp((108, 80, 56), 5, dark=0.45, light=1.4)
ASH = ramp(graded((176, 140, 96)), 5, dark=0.5, light=1.3)
INK = (40, 30, 26, 255)


def canvas(w=S, h=S):
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))


def put(img, x, y, c):
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((int(x), int(y)), c)


def save(img, name):
    path = os.path.join(ROOT, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def part(img, draw, colors, line=None, **kw):
    """Shade a shape drawn by ``draw(ImageDraw)``; with ``line``, ink its edge where it overlaps."""
    kw.setdefault("radius", 0.9)
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


def haft(img, a, z, wood=WALNUT, width=3.0, grip=True):
    part(img, stick(a, z, width, width - 0.4), wood, radius=0.7, tex=lambda x, y: 0.2 if (x + 2 * y) % 5 == 0 else 0)
    if grip:
        for k in range(7):
            t = 0.06 + k * 0.04
            x, y = a[0] + (z[0] - a[0]) * t, a[1] + (z[1] - a[1]) * t
            put(img, round(x), round(y), LEATHER[3] if k % 2 else LEATHER[1])
            put(img, round(x) + 1, round(y), LEATHER[2] if k % 2 else LEATHER[0])


def lashing(img, x, y, n=3):
    """Rawhide binding a stone head to its haft."""
    for k in range(n):
        put(img, x + k, y + k, OAT[2]); put(img, x + k + 1, y + k, OAT[0])


def edge(img, pts, c):
    for x, y in pts:
        if img.getpixel((x, y))[3]:
            put(img, x, y, c)


def done(img):
    return selective_outline(img)


# ---------------------------------------------------------------- tools and weapons

def axe(metal):
    m = METAL[metal]
    img = canvas()
    haft(img, (7, 29), (21, 6))
    if metal == "stone":
        part(img, poly([(16, 6), (21, 3), (27, 6), (27, 13), (21, 14), (17, 11)]), m, tex=lambda x, y: 0.25 if (x * 3 + y) % 7 == 0 else 0)
        lashing(img, 17, 8)
        return done(img)
    part(img, poly([(17, 7), (20, 4), (24, 4), (28, 2), (30, 8), (29, 15), (26, 20), (23, 15), (22, 11), (18, 11)]), m, radius=1.2, slope=1.7)
    edge(img, [(29, 4), (30, 7), (30, 10), (29, 13), (28, 16), (27, 18)], m[5])          # the honed bit
    return done(img)


def pick(metal):
    m = METAL[metal]
    img = canvas()
    haft(img, (9, 30), (17, 8))
    if metal == "stone":                                                                    # an antler pick, tine lashed on
        part(img, stick((5, 12), (27, 7), 3.4, 2), ramp((210, 196, 168), 6, main=3, dark=0.5, light=1.15))
        lashing(img, 15, 7)
        return done(img)
    part(img, lambda d: d.polygon(ribbon([(4, 13), (10, 8), (17, 6), (24, 7), (30, 12)], [1, 3.5, 4.5, 3.5, 1]), fill=255), m)
    put(img, 4, 13, m[5]); put(img, 30, 12, m[5])
    return done(img)


def hammer(metal):
    m = METAL[metal]
    img = canvas()
    haft(img, (8, 29), (19, 11))
    if metal == "stone":
        part(img, lambda d: d.ellipse([12, 3, 28, 14], fill=255), m, radius=1.4, tex=lambda x, y: 0.25 if (x + y * 3) % 8 == 0 else 0)
        lashing(img, 17, 10)
        return done(img)
    part(img, poly([(13, 6), (14, 4), (27, 4), (29, 6), (29, 12), (27, 14), (14, 14), (13, 12)]), m, radius=1.2)
    for y in range(5, 14):
        put(img, 28, y, m[1])
    return done(img)


def sword(metal, long=False):
    """A Norse sword: straight double-edged blade with a fuller, a short guard, a leather grip and a lobed
    pommel (bronze fittings on iron and bronze blades, silver on steel)."""
    m = METAL[metal]
    fit = METAL["silver"] if metal == "steel" else METAL["bronze"] if metal != "bronze" else METAL["gold"]
    img = canvas()
    tip = (29, 3) if long else (26, 6)
    part(img, lambda d: d.polygon(ribbon([(11, 21), (19, 13), tip], [4.2, 3.6, 1.0]), fill=255), m, radius=0.8, slope=1.7)
    n = 14 if long else 11
    for k in range(n):                                                                      # the fuller
        put(img, 13 + k, 19 - k, m[1] if k % 3 else m[2])
    put(img, tip[0] - 1, tip[1] + 1, m[5])
    part(img, poly([(6, 18), (8, 16), (15, 23), (13, 25)]), fit)
    for k in range(5):
        put(img, 9 - k, 22 + k, LEATHER[3] if k % 2 else LEATHER[1])
        put(img, 10 - k, 22 + k, LEATHER[2] if k % 2 else LEATHER[0])
    part(img, lambda d: d.ellipse([1, 25, 6, 30], fill=255), fit)
    put(img, 3, 27, fit[0]); put(img, 4, 26, fit[4])
    return done(img)


def spear(metal):
    m = METAL[metal]
    img = canvas()
    haft(img, (3, 30), (21, 12), ASH, width=2.4, grip=False)
    part(img, poly([(19, 11), (22, 8), (29, 2), (25, 10), (22, 13)]), m, radius=0.7, slope=1.7)   # leaf blade
    for k in range(5):
        put(img, 22 + k, 10 - k, m[1])
    part(img, stick((18, 15), (22, 11), 3.0), m[:5] if metal != "stone" else OAT)            # socket (or lashing)
    return done(img)


def mace(metal):
    m = METAL[metal]
    img = canvas()
    haft(img, (6, 28), (18, 15))
    part(img, lambda d: d.ellipse([15, 5, 27, 17], fill=255), m, radius=1.4)
    for (x, y) in ((21, 3), (29, 11), (21, 19), (13, 11), (26, 5), (16, 6)):              # flanges
        part(img, poly([(x - 2, y), (x, y - 2), (x + 2, y), (x, y + 2)]), m, line=m[0], radius=0.5)
    return done(img)


def seax(metal):
    """A seax: the Norse knife, a broken-back single-edged blade on a long handle."""
    m = METAL[metal]
    img = canvas()
    part(img, stick((5, 27), (12, 20), 3.2, 3.0), WALNUT, radius=0.6)
    if metal == "stone":
        part(img, poly([(12, 18), (24, 7), (26, 8), (16, 21)]), m, tex=lambda x, y: 0.3 if (x + y) % 3 == 0 else 0)
        return done(img)
    part(img, poly([(11, 19), (22, 8), (27, 4), (24, 11), (15, 22)]), m, radius=0.7, slope=1.7)
    for k in range(7):
        put(img, 14 + k, 20 - k, m[5])
    put(img, 12, 20, METAL["bronze"][3]); put(img, 13, 21, METAL["bronze"][1])
    return done(img)


def bow():
    img = canvas()
    stave = ramp(graded((150, 110, 66)), 5, dark=0.5, light=1.35)
    part(img, lambda d: d.arc([4, 2, 30, 30], 285, 75, fill=255, width=3), stave, radius=0.6)
    for y in range(4, 29):                                                                  # the string
        put(img, 20, y, OAT[3] if y % 2 else OAT[2])
    for y in (14, 15, 16, 17):
        put(img, 28, y, LEATHER[2])                                                         # grip wrap
    return done(img)


def club():
    img = canvas()
    oak = WOODS["_oak"]
    part(img, stick((8, 29), (22, 8), 3.0, 8.0), oak.bark, radius=1.4, tex=lambda x, y: -0.4 if (x * 2 + y) % 6 == 0 else 0)
    for x, y in ((20, 7), (24, 11), (18, 12), (22, 15), (25, 6)):
        put(img, x, y, IRON_DARK[3]); put(img, x + 1, y + 1, IRON_DARK[0])
    for k in range(4):
        put(img, 9 + k, 26 - k * 2, LEATHER[2])
    return done(img)


def dagger():
    """The tinker's dagger: a slim double-edged blade, a brass cross and a cord-wrapped grip."""
    m = METAL["steel"]
    img = canvas()
    part(img, stick((6, 26), (11, 21), 3.0, 2.8), WALNUT, radius=0.6)
    for k in range(4):
        put(img, 7 + k, 25 - k, OAT[2] if k % 2 else OAT[0])                               # the cord
    part(img, poly([(10, 19), (24, 6), (26, 5), (25, 8), (12, 21)]), m, radius=0.7, slope=1.7)
    for k in range(10):
        put(img, 13 + k, 18 - k, m[5] if k % 2 else m[4])
    part(img, poly([(7, 18), (9, 16), (15, 22), (13, 24)]), METAL["bronze"])
    put(img, 5, 27, METAL["bronze"][3])
    return done(img)


def kitchen_knife():
    """A cook's knife: a broad, belly-curved blade on a riveted wooden handle."""
    m = METAL["iron"]
    img = canvas()
    part(img, stick((4, 28), (11, 21), 3.4), WALNUT, radius=0.6)
    for x, y in ((6, 26), (9, 23)):
        put(img, x, y, METAL["silver"][4])
    part(img, poly([(10, 19), (24, 5), (27, 4), (26, 9), (20, 17), (13, 23)]), m, radius=0.9, slope=1.6)
    for k in range(9):
        put(img, 15 + k, 20 - k * 1.3, m[5])                                                # the honed edge
    return done(img)


def lucky_lou():
    """Lucky Lou: a turned ash cudgel, smooth and swelling to a rounded end, a leather wrap and a nick or two."""
    img = canvas()
    part(img, lambda d: (d.polygon(ribbon([(7, 29), (14, 20), (21, 11)], [2.4, 3.8, 6.0]), fill=255),
                         d.ellipse([18, 4, 27, 13], fill=255)), ASH, radius=1.6)
    for k in range(5):
        put(img, 7 + k, 28 - k * 1.3, LEATHER[3] if k % 2 else LEATHER[1])
        put(img, 8 + k, 28 - k * 1.3, LEATHER[2] if k % 2 else LEATHER[0])
    for x, y in ((20, 11), (24, 7)):                                                        # the nicks
        put(img, x, y, ASH[0]); put(img, x + 1, y, ASH[1])
    return done(img)


def staff():
    """A walking staff: a long knotted pole, a knob at the top and a leather grip near it."""
    img = canvas()
    part(img, stick((3, 31), (26, 5), 2.4, 2.6), ASH, radius=0.7, tex=lambda x, y: 0.2 if (x + 2 * y) % 7 == 0 else 0)
    part(img, lambda d: d.ellipse([23, 1, 30, 8], fill=255), WALNUT, radius=1.2)
    for k in range(4):
        put(img, 18 + k, 14 - k, LEATHER[3] if k % 2 else LEATHER[1])
    for x, y in ((10, 23), (15, 17)):                                                       # knots
        put(img, x, y, WALNUT[1])
    return done(img)


def crossbow():
    """The tinker's crossbow: a walnut stock, a steel-sprung bow square across its head, the string drawn
    back to the nut, a trigger beneath and the crank at the butt."""
    m = METAL["steel"]
    img = canvas()
    nut = (15, 18)
    for tip in ((13, 6), (28, 21)):                                                         # the string, tip to nut
        n = max(abs(tip[0] - nut[0]), abs(tip[1] - nut[1]))
        for k in range(n + 1):
            put(img, round(tip[0] + (nut[0] - tip[0]) * k / n), round(tip[1] + (nut[1] - tip[1]) * k / n), OAT[3] if k % 2 else OAT[1])
    part(img, stick((4, 29), (25, 8), 3.4, 2.4), WALNUT, radius=0.7)                       # the stock
    part(img, lambda d: d.polygon(ribbon([(13, 6), (19, 9), (22, 12), (25, 15), (28, 21)], [1.0, 2.0, 2.6, 2.0, 1.0]), fill=255), m, radius=0.6)
    part(img, poly([(11, 23), (13, 22), (13, 27)]), METAL["iron"])                          # the trigger
    part(img, lambda d: d.ellipse([2, 26, 7, 31], fill=255), METAL["iron"], radius=0.6)      # the crank
    return done(img)


def tongs():
    m = METAL["iron"]
    img = canvas()
    part(img, stick((6, 28), (19, 9), 2.0), m)
    part(img, stick((12, 29), (19, 9), 2.0), m, line=m[0])
    part(img, stick((19, 9), (26, 3), 2.0), m)
    part(img, stick((19, 9), (28, 8), 2.0), m, line=m[0])
    put(img, 19, 9, m[5])
    return done(img)


def saw():
    m = METAL["iron"]
    img = canvas()
    part(img, poly([(2, 18), (22, 8), (24, 17), (3, 25)]), m, radius=1.4, slope=1.6)
    for x in range(3, 23, 2):                                                               # teeth along the lower edge
        y = round(25 - (x - 3) * 0.4)
        put(img, x, y + 1, m[1]); put(img, x + 1, y, m[0])
    for x in range(4, 22):
        put(img, x, round(18 - (x - 2) * 0.5), m[5])
    part(img, lambda d: d.rounded_rectangle([21, 7, 29, 18], radius=3, fill=255), WALNUT, line=WALNUT[0])
    for x in range(23, 27):
        for y in range(10, 14):
            if (x - 24.5) ** 2 + (y - 11.5) ** 2 <= 2.3:
                img.putpixel((x, y), (0, 0, 0, 0))
    return done(img)


def chisel():
    img = canvas()
    part(img, stick((7, 27), (16, 15), 4.2, 3.8), WALNUT, radius=0.8)
    part(img, poly([(15, 13), (24, 4), (27, 7), (18, 16)]), METAL["iron"], line=METAL["iron"][0], slope=1.8)
    put(img, 25, 5, METAL["iron"][5]); put(img, 26, 6, METAL["iron"][5])
    return done(img)


def fishing_rod():
    img = canvas()
    part(img, stick((5, 30), (26, 3), 2.4, 1.2), ASH, radius=0.5)
    for k in range(5):
        put(img, 7 + k, 26 - k * 1.3, LEATHER[2])
    for y in range(4, 21):                                                                  # the line
        put(img, 27, y, OAT[3] if y % 3 else OAT[1])
    for x, y in ((27, 21), (28, 22), (28, 23), (27, 24), (26, 23)):                          # a bone hook
        put(img, x, y, (232, 222, 196, 255))
    put(img, 27, 13, (186, 58, 50, 255)); put(img, 27, 12, (236, 230, 220, 255))             # a float
    return done(img)


def chain_key():
    m = METAL["iron"]
    img = canvas()
    part(img, lambda d: d.arc([5, 3, 17, 15], 0, 360, fill=255, width=1), LEATHER[1:])
    part(img, lambda d: d.ellipse([12, 9, 20, 17], fill=255), m, radius=0.8)
    for x, y in ((15, 12), (16, 12), (15, 13), (16, 13)):
        img.putpixel((x, y), (0, 0, 0, 0))
    part(img, stick((18, 16), (26, 25), 2.2), m, line=m[0])
    part(img, poly([(22, 22), (25, 19), (27, 21), (24, 24)]), m, line=m[0])
    part(img, poly([(24, 25), (27, 22), (29, 24), (26, 27)]), m, line=m[0])
    return done(img)


# ---------------------------------------------------------------- metal stock and treasure

def bars(metal):
    m = METAL[metal]
    img = canvas()
    for y in (19, 11):
        part(img, poly([(4, y + 8), (8, y), (26, y), (29, y + 8)]), m, line=m[0], radius=0.6, slope=1.8)
        for x in range(9, 26):
            put(img, x, y + 1, m[5] if x % 5 else m[4])
        for x in (12, 20):                                                                  # a smith's stamp
            put(img, x, y + 4, m[1]); put(img, x + 1, y + 4, m[1])
    return done(img)


def coins(metal):
    m = METAL[metal]
    img = canvas()
    for x, y in ((9, 24), (21, 24), (15, 21), (15, 17), (15, 13), (15, 9)):
        part(img, lambda d, x=x, y=y: d.ellipse([x - 6, y - 3, x + 6, y + 3], fill=255), m, line=m[0], radius=0.8)
        for k in range(-3, 3):
            put(img, x + k, y - 2, m[5])
        put(img, x, y, m[2]); put(img, x + 1, y, m[2])
    return done(img)


def ring():
    m = METAL["silver"]
    img = canvas()
    part(img, lambda d: d.ellipse([7, 10, 25, 28], outline=255, width=3), m, radius=0.6)
    for a in range(0, 360, 30):                                                             # twisted wire
        put(img, 16 + round(7.5 * math.cos(math.radians(a))), 19 + round(7.5 * math.sin(math.radians(a))), m[1])
    part(img, poly([(13, 8), (16, 4), (19, 8), (16, 12)]), ramp((110, 190, 220), 4, main=1, light=1.3), radius=0.5)
    return done(img)


def torc():
    m = METAL["gold"]
    img = canvas()
    part(img, lambda d: d.arc([4, 6, 28, 30], 195, 345, fill=255, width=4), m, radius=0.6)
    for a in range(196, 345, 9):                                                            # the twist
        put(img, 16 + round(11 * math.cos(math.radians(a))), 18 + round(11 * math.sin(math.radians(a))), m[1])
    for x, y in ((5, 21), (27, 21)):                                                        # beast-head terminals
        part(img, lambda d, x=x, y=y: d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=255), m, line=m[0], radius=0.6)
        put(img, x, y - 1, (30, 26, 30, 255))
    return done(img)


# ---------------------------------------------------------------- ores, fuel, building stock

def ore_lump(seed, name):
    """A lump of host rock with the ore showing in it."""
    o = ORES[name]
    host = rock_ramp(graded(ROCKS[o["host"]]["base"]))
    img = canvas()
    part(img, poly([(5, 23), (8, 12), (16, 7), (25, 10), (28, 21), (19, 28), (9, 28)]), host[1:7], radius=1.2, slope=1.7,
         tex=lambda x, y: bayer(x, y) * 0.08)
    rnd = random.Random(seed)
    px = img.load()
    if name == "coal":
        return img
    for _ in range(5):
        x, y = rnd.randint(11, 22), rnd.randint(12, 23)
        nugget(px, x, y, 2, 1, o["cols"], shadow=host[1])
    return done(img)


def lumps(dark, mid, light, seed=0):
    img = canvas()
    for x, y in ((8, 21), (17, 23), (25, 19), (12, 13), (21, 11)):
        part(img, poly([(x - 5, y + 2), (x - 3, y - 3), (x + 2, y - 4), (x + 5, y), (x + 3, y + 4), (x - 2, y + 4)]),
             ramp(mid, 5, dark=dark, light=light), line=INK, radius=0.8, slope=1.8)
    return done(img)


def charcoal():
    img = lumps(0.5, (52, 48, 54), 1.9)
    for x, y in ((6, 20), (16, 22), (11, 12), (20, 10)):                                    # split along the grain
        put(img, x, y, (24, 20, 26, 255)); put(img, x + 1, y, (24, 20, 26, 255)); put(img, x + 2, y, (24, 20, 26, 255))
    return img


def coal():
    img = lumps(0.45, (40, 40, 50), 2.6)
    for x, y in ((7, 19), (16, 21), (24, 17), (11, 11), (20, 9)):                            # glassy glints
        put(img, x, y, (150, 160, 190, 255))
    return img


def lime():
    img = canvas()
    sack = ramp((196, 178, 140), 5, dark=0.55, light=1.2)
    part(img, poly([(6, 29), (5, 16), (9, 9), (23, 9), (27, 16), (26, 29)]), sack, radius=1.6, slope=1.2)
    part(img, lambda d: d.ellipse([8, 6, 24, 14], fill=255), ramp((236, 234, 226), 4, main=2, dark=0.75, light=1.05), radius=1.2)
    for x in range(9, 24):
        put(img, x, 15, sack[1])
    return done(img)


def clay():
    img = canvas()
    c = ramp(graded((176, 116, 86)), 5, dark=0.55, light=1.3)
    part(img, lambda d: d.ellipse([5, 12, 27, 28], fill=255), c, radius=1.8)
    part(img, lambda d: d.ellipse([10, 7, 22, 18], fill=255), c, line=c[0], radius=1.4)
    for x, y in ((12, 10), (13, 11), (19, 21), (20, 22)):                                   # finger marks
        put(img, x, y, c[1])
    return done(img)


def bricks():
    img = canvas()
    br = ramp(graded((164, 72, 50)), 5, dark=0.5, light=1.35)
    for row, y in enumerate((21, 14, 7)):
        for x in ((3, 16) if row % 2 == 0 else (9,)):
            part(img, lambda d, x=x, y=y: d.rectangle([x, y, x + 12, y + 6], fill=255), br, line=br[0], radius=0.5, slope=2.0)
    return done(img)


def block_stack(r):
    img = canvas()
    for (x, y, w, h) in ((3, 19, 12, 10), (16, 19, 12, 10), (9, 9, 13, 10)):
        part(img, lambda d, x=x, y=y, w=w, h=h: d.rectangle([x, y, x + w, y + h], fill=255), r[1:7], line=r[0], radius=0.5, slope=2.2,
             tex=lambda xx, yy: -0.6 if (xx * 3 + yy * 5) % 13 == 0 else 0)
    return done(img)


def rubble():
    r = rock_ramp(graded(ROCKS["rough"]["base"]))
    img = canvas()
    for pts in ([(3, 28), (4, 20), (10, 17), (15, 21), (14, 28)], [(14, 28), (15, 19), (22, 16), (28, 20), (28, 28)], [(8, 18), (11, 9), (18, 7), (22, 12), (20, 18), (13, 20)]):
        part(img, poly(pts), r[1:7], line=r[0], radius=0.9, slope=1.9, tex=lambda x, y: bayer(x, y) * 0.1)
    return done(img)


def log_pile(wood, seed):
    """Six logs stacked 3-2-1, end grain to the viewer: bark rim, sapwood, rings, a lit upper left."""
    rnd = random.Random(seed)
    img = canvas()
    r = 4.6
    for cy, xs in ((9, [16]), (17, [11.5, 20.5]), (25, [7, 16, 25])):
        for cx in xs:
            cx += rnd.uniform(-0.4, 0.4)
            part(img, poly([(cx - r, cy), (cx - r + 3, cy - 4), (cx + r + 3, cy - 4), (cx + r, cy)]), wood.bark, line=wood.bark[0], radius=0.6)
            for y in range(int(cy - r - 1), int(cy + r + 2)):
                for x in range(int(cx - r - 1), int(cx + r + 2)):
                    d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
                    if d > r:
                        continue
                    if d > r - 1.1:
                        c = wood.bark[1] if not wood.birch else (wood.bark[2] if (x + y) % 5 else (52, 48, 50, 255))
                    else:
                        lit = (x - cx) + (y - cy) < -1.5
                        c = wood.end[1] if abs(d - 2.4) < 0.45 else wood.end[3 if lit else 2]
                    put(img, x, y, c)
            put(img, round(cx), round(cy), wood.end[0])
    return done(img)


def planks_item(wood, seed):
    rnd = random.Random(seed)
    img = canvas()
    for y0, x0, x1 in ((22, 3, 28), (17, 5, 30), (12, 2, 26), (7, 6, 29)):
        board(img, x0, y0, x1, y0 + 4, wood, rnd, True, tone=3.2)
        for x in range(x0, x1 + 1):
            put(img, x, y0 + 4, wood.w[0])
    return done(img)


# ---------------------------------------------------------------- food, hides, cloth

def berries():
    """A sprig of lingonberries: glossy red berries with a lit pip, dark leaves."""
    img = canvas()
    leaf = ramp(graded((60, 100, 58)), 4, main=1, dark=0.6, light=1.4)
    part(img, lambda d: d.line([(5, 27), (14, 17), (25, 7)], fill=255, width=1), WALNUT)
    for pts in ([(10, 22), (6, 18), (11, 18)], [(18, 13), (16, 8), (21, 11)], [(21, 16), (26, 15), (23, 19)]):
        part(img, poly(pts), leaf, radius=0.5)
    red = ramp((176, 32, 48), 4, main=1, dark=0.55, light=1.4)
    for x, y in ((12, 13), (16, 18), (9, 17), (20, 21), (14, 23), (23, 12), (18, 25)):
        part(img, lambda d, x=x, y=y: d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=255), red, line=red[0], radius=0.6)
        put(img, x - 1, y - 1, (255, 236, 236, 255))
    return done(img)


def meat():
    """A haunch: red meat with a lit sheen, a cap of fat, the bone end."""
    img = canvas()
    mt = ramp((168, 58, 52), 5, dark=0.5, light=1.35)
    part(img, lambda d: d.ellipse([4, 9, 24, 27], fill=255), mt, radius=1.6)
    for x in range(6, 21):
        put(img, x, 10 + abs(x - 13) // 4, (232, 218, 196, 255))
    bone = ramp((228, 216, 190), 4, main=2, dark=0.7, light=1.06)
    part(img, stick((20, 18), (28, 10), 3.0), bone, line=(110, 90, 80, 255))
    part(img, lambda d: d.ellipse([25, 6, 30, 11], fill=255), bone)
    return done(img)


def fish():
    """Two herring, one above the other: blue-green backs, silver flanks, a forked tail, an eye."""
    img = canvas()
    body = ramp((128, 156, 168), 5, dark=0.5, light=1.35)
    for dy in (0, 8):
        part(img, lambda d, dy=dy: d.ellipse([4, 9 + dy, 22, 17 + dy], fill=255), body, line=body[0], radius=1.0)
        part(img, poly([(21, 13 + dy), (28, 8 + dy), (26, 13 + dy), (28, 18 + dy)]), body, line=body[0], radius=0.5)
        for x in range(6, 20):
            put(img, x, 10 + dy, mix(body[1][:3], (60, 110, 110), 0.6))
        put(img, 8, 12 + dy, (20, 20, 30, 255)); put(img, 7, 11 + dy, (240, 244, 250, 255))
    return done(img)


def meal():
    """A turned wooden bowl of stew, steam rising."""
    img = canvas()
    w = WOODS["_birch"].w
    part(img, lambda d: d.chord([3, 6, 29, 30], 0, 180, fill=255), w[1:], radius=1.4)
    broth = ramp((150, 88, 50), 4, main=1, dark=0.65, light=1.25)
    part(img, lambda d: d.ellipse([4, 14, 28, 22], fill=255), broth, radius=1.2, slope=-1.0)
    for x, y, c in ((9, 17, (214, 168, 92)), (15, 16, (190, 104, 78)), (21, 18, (214, 168, 92)), (13, 19, (110, 140, 70)), (18, 19, (190, 104, 78))):
        put(img, x, y, c + (255,)); put(img, x + 1, y, c + (255,))
    for x in range(5, 28):
        put(img, x, 14 + (1 if x in (5, 27) else 0), w[4])
    img = done(img)
    for k, (x, y) in enumerate(((12, 11), (11, 9), (12, 7), (19, 10), (20, 8), (19, 6))):    # steam
        put(img, x, y, (236, 236, 240, 200 - k * 20))
    return img


def hide():
    img = canvas()
    fur = ramp(graded((150, 108, 72)), 5, dark=0.55, light=1.3)
    part(img, poly([(6, 8), (12, 4), (20, 5), (27, 9), (26, 18), (28, 25), (19, 28), (12, 27), (4, 24), (6, 16)]), fur, radius=1.8,
         tex=lambda x, y: 0.3 if (x + y * 2) % 5 == 0 else -0.2 if (x + y * 2) % 5 == 2 else 0)
    return done(img)


def leather():
    img = canvas()
    part(img, lambda d: d.rounded_rectangle([3, 10, 26, 24], radius=6, fill=255), LEATHER, radius=1.6)
    part(img, lambda d: d.ellipse([21, 10, 30, 24], fill=255), LEATHER, line=LEATHER[0], radius=1.0)
    m = mask_of((S, S), lambda d: d.arc([23, 12, 28, 22], 90, 270, fill=255))
    canvas_px = img.load()
    for y in range(S):
        for x in range(S):
            if m.getpixel((x, y)):
                canvas_px[x, y] = LEATHER[1]
    for x in range(5, 21, 3):
        put(img, x, 16, LEATHER[4])
    for x in (9, 16):                                                                      # a thong tying the roll
        for y in range(10, 25):
            put(img, x, y, OAT[1] if y % 2 else OAT[2])
    return done(img)


def leather_pack():
    img = canvas()
    part(img, lambda d: d.arc([8, 1, 24, 15], 180, 360, fill=255, width=2), LEATHER[:4])
    part(img, lambda d: d.rounded_rectangle([4, 7, 28, 30], radius=4, fill=255), LEATHER, radius=1.4)
    part(img, poly([(4, 7), (28, 7), (28, 16), (16, 19), (4, 16)]), LEATHER[1:] + [LEATHER[4]], line=LEATHER[0], radius=1.0)
    for x in (10, 21):
        for y in range(15, 26):
            put(img, x, y, LEATHER[1]); put(img, x + 1, y, LEATHER[0])
        for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1), (-1, 0), (2, 0)):
            put(img, x + dx, 19 + dy, METAL["bronze"][3 if dy == 0 else 2])
    for x in range(7, 26, 2):                                                               # a knotwork stitch line
        put(img, x, 9, (214, 168, 56, 255))
    return done(img)


def emberroot():
    img = canvas()
    root = ramp(graded((196, 84, 52)), 5, dark=0.5, light=1.3)
    for (x, y, a) in ((10, 14, 0), (19, 19, 1)):
        part(img, poly([(x - 6, y - 3), (x + 6, y - 1), (x + 4, y + 4), (x - 5, y + 3)]), root, line=root[0], radius=1.0)
        for k in range(4):
            put(img, x + 5 + k, y + 1 + a + k // 2, root[1])
        put(img, x - 4, y - 2, (98, 140, 70, 255)); put(img, x - 5, y - 3, (98, 140, 70, 255))
        for k in (-2, 1, 3):
            put(img, x + k, y, root[0])
    return done(img)


def fiber():
    """A hank of flax fibre, twisted and bound at the middle."""
    img = canvas()
    f = ramp((206, 192, 150), 5, dark=0.6, light=1.15)
    for dy, c in ((0, 1), (2, 2), (4, 3), (6, 4), (8, 2)):
        part(img, lambda d, dy=dy: d.line([(4, 18 + dy // 2), (16, 13 + dy), (28, 16 + dy // 2)], fill=255, width=2), [f[c]] * 2)
    part(img, lambda d: d.rectangle([14, 12, 18, 24], fill=255), f[:3] + [f[1]], line=f[0])
    for y in range(13, 24, 2):
        put(img, 15, y, f[3])
    return done(img)


def bandage():
    img = canvas()
    part(img, poly([(13, 10), (24, 8), (27, 24), (13, 26)]), LINEN, radius=1.2)
    for y in (12, 16, 20):
        for x in range(15, 26):
            put(img, x, y + 1 - (x - 15) // 6, LINEN[1])
    part(img, lambda d: d.ellipse([6, 10, 20, 26], fill=255), LINEN, line=LINEN[0], radius=1.2)
    for a in range(0, 360, 20):
        put(img, 13 + round(3.5 * math.cos(math.radians(a))), 18 + round(4.5 * math.sin(math.radians(a))), LINEN[1])
    part(img, lambda d: d.ellipse([11, 16, 15, 20], fill=255), LINEN[:2])
    part(img, poly([(26, 22), (30, 27), (27, 29), (24, 24)]), LINEN, line=LINEN[0])
    return done(img)


SCALE = ramp((168, 64, 46), 5, dark=0.45, light=1.4, cool=18, warm=12)


def scale_shape(img, x, y, r=6):
    part(img, poly([(x - r, y - 4), (x + r, y - 4), (x + r, y + 2), (x, y + 8), (x - r, y + 2)]), SCALE, line=SCALE[0], radius=1.0)
    for k in range(-r + 2, r - 1):
        put(img, x + k, y - 3, SCALE[4])
    for k in range(5):
        put(img, x, y - 2 + k, SCALE[1])


# ---------------------------------------------------------------- generating

def generate():
    out = {}
    for metal, prefix in (("iron", ""), ("copper", "copper_"), ("bronze", "bronze_"), ("steel", "steel_"), ("stone", "stone_")):
        out[f"items/{prefix}axe"] = axe(metal)
        out[f"items/{prefix}pick"] = pick(metal)
        out[f"items/{prefix}hammer"] = hammer(metal)
    out["items/sword"] = sword("iron")
    out["items/bronze_sword"] = sword("bronze")
    out["items/steel_sword"] = sword("steel", long=True)
    for metal in ("stone", "copper", "bronze", "iron"):
        out[f"items/{metal}_spear"] = spear(metal)
    for metal in ("bronze", "iron", "steel"):
        out[f"items/{metal}_mace"] = mace(metal)
    out["items/stone_knife"] = seax("stone")
    out["items/copper_knife"] = seax("copper")
    out["items/bow"] = bow()
    out["items/club"] = club()
    out["items/dagger"] = dagger()
    out["items/kitchen_knife"] = kitchen_knife()
    out["items/lucky_lou"] = lucky_lou()
    out["items/staff"] = staff()
    out["items/crossbow"] = crossbow()
    out["items/tongs"] = tongs()
    out["items/saw"] = saw()
    out["items/chisel"] = chisel()
    out["items/fishing_rod"] = fishing_rod()
    out["items/chain_key"] = chain_key()
    for metal in ("copper", "bronze", "steel", "silver", "gold", "iron"):
        out[f"items/{metal}_bar"] = bars(metal)
    out["items/silver_coin"] = coins("silver")
    out["items/gold_coin"] = coins("gold")
    out["items/silver_ring"] = ring()
    out["items/gold_torc"] = torc()
    ORES.setdefault("iron_ore", dict(host="rough", cols=((96, 38, 26), (156, 72, 44), (214, 130, 84)), vein=(190, 96, 58)))
    for n, name in enumerate(("copper_ore", "tin_ore", "silver_ore", "gold_ore", "iron_ore")):
        out[f"items/{name}"] = ore_lump(2400 + n, name)
    out["items/coal"] = coal()
    out["items/charcoal"] = charcoal()
    out["items/lime"] = lime()
    out["items/clay"] = clay()
    out["items/bricks"] = bricks()
    out["items/cut_stone"] = block_stack(rock_ramp(graded(ROCKS["rough"]["base"])))
    for rock in ("sandstone", "limestone", "granite", "slate", "basalt"):
        out[f"items/cut_stone_{rock}"] = block_stack(rock_ramp(graded(ROCKS[rock]["base"])))
    out["items/stone"] = rubble()
    for suffix, wood in WOODS.items():
        seed = sum(map(ord, suffix))
        out["items/wood" + suffix] = log_pile(wood, seed + 6)
        out["items/planks" + suffix] = planks_item(wood, seed + 11)
    for name, make in (("berries", berries), ("meat", meat), ("fish", fish), ("meal", meal), ("hide", hide), ("leather", leather),
                       ("leather_pack", leather_pack), ("emberroot", emberroot), ("fiber", fiber),
                       ("bandage", bandage)):
        out["items/" + name] = make()
    for name, img in out.items():
        save(img, name)
    return out


if __name__ == "__main__":
    generate()
