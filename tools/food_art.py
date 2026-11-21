#!/usr/bin/env python3
"""The inn's crockery and the kitchen's food: an icon each, also how each stands in the world (the
defs have no box model, so the game draws the sprite upright, sitting on its bottom edge).

    python3 tools/food_art.py              # into data/textures/items
    python3 tools/food_art.py --preview    # ...and a contact sheet in .cache/food.png

Clean and dirty tankards and bowls, the dishes (stew, porridge, a trencher meal, a loaf, pies baked
and raw, dough), the meats (roast, a side of pork, salt and smoked meat, chopped meat, a fowl plucked
and not), the staples (flour, oats, salt, rations) and the drinks (spirits, the house mixes). 32x32,
lit from the top left, selective outline, in norse_gear's idiom (whose drumstick, herring and stew
bowl these sit beside).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(__file__))
from norse_art import ramp, mix, bayer  # noqa: E402
from norse_gear import canvas, put, part, poly, stick, done, save, WOODS  # noqa: E402

IRON = ramp((96, 100, 112), 4, dark=0.55, light=1.6)
OAK = ramp((150, 104, 62), 5)
BIRCH = WOODS["_birch"].w
CLAY_POT = ramp((168, 96, 64), 5, dark=0.5, light=1.3)
LINEN = ramp((222, 208, 176), 5, dark=0.6, light=1.12)
SACK = ramp((176, 150, 108), 5, dark=0.5, light=1.25)
TWINE = (196, 168, 112, 255)
CRUST = ramp((184, 120, 58), 5, dark=0.45, light=1.35)
PALE_DOUGH = ramp((226, 208, 170), 5, dark=0.62, light=1.1)
RED_MEAT = ramp((168, 58, 52), 5, dark=0.5, light=1.35)
FAT = (236, 222, 200, 255)
FOAM = [(214, 204, 176, 255), (232, 224, 200, 255), (244, 238, 222, 255), (252, 250, 240, 255)]


def dots(img, pts, color):
    for x, y in pts:
        put(img, x, y, color)


# ---------------------------------------------------------------- tankards

def tankard_body(img, wood=OAK, inside=(58, 40, 28, 255)):
    """A staved, iron-hooped tankard, empty, seen a little from above: the handle, the staves, the
    open top with its dark inside."""
    part(img, lambda d: d.arc([18, 13, 29, 26], 270, 90, fill=255, width=3), wood[:4], radius=0.8)       # the handle
    part(img, lambda d: d.rounded_rectangle([6, 10, 22, 30], radius=2, fill=255), wood,
         tex=lambda x, y: 0.12 if x % 4 == 0 else 0.0)                                                  # the staves
    for y in (13, 26):                                                                                   # iron hoops
        dots(img, [(x, y) for x in range(6, 23)], IRON[1])
        dots(img, [(x, y + 1) for x in range(6, 23)], IRON[3])
    part(img, lambda d: d.ellipse([6, 6, 22, 13], fill=255), wood[1:], radius=1.0)                     # the rim
    part(img, lambda d: d.ellipse([8, 7, 20, 12], fill=255), [inside, mix(inside[:3], wood[1][:3], 0.4)], radius=0.8, slope=-1.0)


def tankard():
    """Clean and empty, on the shelf: pale-scrubbed staves, a bright rim."""
    img = canvas()
    tankard_body(img, ramp((166, 120, 74), 5))
    put(img, 9, 8, OAK[4])
    return done(img)


def dirty_tankard():
    """Drunk from and left: a ring of old foam inside the rim, the dregs, a dribble of foam dried
    down the side, a sticky dark band where it was held."""
    img = canvas()
    tankard_body(img, ramp((138, 96, 58), 5), inside=(70, 46, 24, 255))
    dots(img, [(x, 8) for x in (9, 10, 12, 13, 16, 18)] + [(8, 9), (19, 9), (9, 11), (18, 11)], FOAM[2])   # the foam ring
    dots(img, [(x, 10) for x in range(11, 17)], (150, 100, 40, 255))                                      # the dregs
    stale = (206, 186, 136, 255)
    for x, length in ((9, 4), (19, 6)):                                                                   # two dried runs of foam, wobbling
        dots(img, [(x + (k // 3) % 2, 14 + k) for k in range(length)], stale)
        dots(img, [(x + ((length - 1) // 3) % 2, 14 + length), (x + 1 + ((length - 1) // 3) % 2, 14 + length)], stale)
    dots(img, [(7, 12), (8, 13), (20, 12), (21, 13)], stale)                                              # spilt over the rim
    dots(img, [(x, y) for x in range(7, 22) for y in (19, 20) if (x + y) % 3], (92, 62, 40, 255))            # the hand's grime
    return done(img)


# ---------------------------------------------------------------- bowls

def bowl_body(img, wood):
    """A turned wooden bowl: the curve of its side and the open rim."""
    part(img, lambda d: d.chord([3, 6, 29, 30], 0, 180, fill=255), wood[1:], radius=1.4)
    part(img, lambda d: d.ellipse([4, 14, 28, 23], fill=255), wood[:4], radius=1.2, slope=-1.0)          # the hollow, lit at the far side
    for x in range(5, 28):
        put(img, x, 14 + (1 if x in (5, 27) else 0), wood[4])                                             # the rim's bright edge


def bowl():
    """Clean and empty, scrubbed pale."""
    img = canvas()
    bowl_body(img, BIRCH)
    dots(img, [(11, 18), (12, 18), (13, 17)], BIRCH[4])
    return done(img)


def dirty_bowl():
    """Eaten from and left: the spoon still in it, smears of stew up the inside, a crust at the
    bottom, a drip down the outside."""
    img = canvas()
    bowl_body(img, ramp(mix(BIRCH[2][:3], (120, 96, 70), 0.35)[:3], 5))
    smear = (112, 64, 34, 255)
    part(img, lambda d: d.ellipse([7, 17, 25, 22], fill=255), ramp((120, 72, 40), 3, dark=0.7, light=1.2), radius=0.8)   # the dregs
    dots(img, [(6, 16), (7, 15), (9, 15), (22, 15), (24, 16), (25, 17), (12, 16), (18, 16)], smear)       # smears up the sides
    dots(img, [(14, 20), (15, 20), (16, 21), (18, 20)], (176, 132, 74, 255))                             # crust and a crumb
    part(img, stick((17, 19), (26, 4), 2.2, 1.6), BIRCH[1:], radius=0.6)                                 # the spoon's handle
    part(img, lambda d: d.ellipse([13, 17, 19, 21], fill=255), BIRCH[1:], radius=0.6)                    # its bowl, in the dregs
    dots(img, [(7, 23), (7, 24), (7, 25), (8, 26)], smear)                                               # a drip down the side
    return done(img)


def stew():
    """A bowl of stew: dark broth, chunks of meat and roots, steam."""
    img = canvas()
    bowl_body(img, BIRCH)
    broth = ramp((140, 80, 44), 4, main=1, dark=0.65, light=1.25)
    part(img, lambda d: d.ellipse([5, 15, 27, 22], fill=255), broth, radius=1.2, slope=-1.0)
    for x, y, c in ((9, 18, (214, 168, 92)), (14, 17, (176, 84, 64)), (20, 18, (214, 168, 92)), (12, 19, (110, 140, 70)),
                    (18, 20, (176, 84, 64)), (23, 17, (230, 150, 70))):
        dots(img, [(x, y), (x + 1, y)], c + (255,))
    img = done(img)
    for k, (x, y) in enumerate(((12, 11), (11, 9), (12, 7), (19, 10), (20, 8), (19, 6))):
        put(img, x, y, (236, 236, 240, 200 - k * 20))
    return img


def porridge():
    """A bowl of oat porridge: pale and thick, a swirl of honey on it, the spoon standing up."""
    img = canvas()
    bowl_body(img, BIRCH)
    oats = ramp((226, 206, 160), 4, main=2, dark=0.7, light=1.08)
    part(img, lambda d: d.ellipse([5, 14, 27, 22], fill=255), oats, radius=1.4, tex=lambda x, y: bayer(x, y) * 0.12)
    dots(img, [(11, 17), (12, 16), (14, 16), (16, 17), (17, 18), (15, 19), (13, 19), (12, 18)], (214, 150, 40, 255))   # honey
    part(img, stick((19, 17), (25, 5), 2.0, 1.6), BIRCH[1:], radius=0.6)
    return done(img)


def trencher():
    """A simple meal: a trencher (a thick slice of old bread for a plate) with a slab of meat, a
    turnip and greens on it."""
    img = canvas()
    part(img, lambda d: d.ellipse([2, 16, 30, 29], fill=255), CRUST, radius=1.4)
    part(img, lambda d: d.ellipse([4, 17, 28, 26], fill=255), PALE_DOUGH[1:], radius=1.4, tex=lambda x, y: bayer(x, y) * 0.15)
    part(img, poly([(8, 20), (17, 15), (21, 18), (12, 24)]), RED_MEAT, radius=1.0)                     # the meat
    dots(img, [(9, 20), (11, 19), (13, 18), (15, 17)], FAT)
    part(img, lambda d: d.ellipse([18, 17, 25, 23], fill=255), ramp((222, 210, 200), 4, dark=0.7), radius=0.9)   # the turnip
    dots(img, [(21, 16), (22, 15), (23, 16)], (150, 110, 160, 255))
    for x, y in ((6, 22), (7, 21), (24, 22), (25, 21), (23, 23)):                                       # greens
        put(img, x, y, (96, 140, 70, 255))
    return done(img)


# ---------------------------------------------------------------- baking

def loaf():
    """A round rye loaf: a dark, scored crust dusted with flour."""
    img = canvas()
    part(img, lambda d: d.ellipse([3, 10, 29, 30], fill=255), CRUST, radius=2.0, tex=lambda x, y: bayer(x, y) * 0.08)
    for k in range(3):                                                                                   # the scoring
        dots(img, [(9 + k * 5 + i, 14 + i) for i in range(6)], CRUST[4])
        dots(img, [(9 + k * 5 + i, 15 + i) for i in range(5)], CRUST[1])
    dots(img, [(8, 13), (12, 12), (17, 12), (22, 13), (14, 11), (20, 15)], (240, 232, 214, 255))         # flour
    return done(img)


def pie(crust_ramp, filling=(150, 40, 60, 255), baked=True):
    """A pie in its earthenware dish: a domed lid with a crimped rim and steam vents."""
    img = canvas()
    part(img, lambda d: d.chord([3, 12, 29, 30], 0, 180, fill=255), CLAY_POT[1:], radius=1.2)           # the dish
    part(img, lambda d: d.ellipse([3, 11, 29, 25], fill=255), crust_ramp, radius=2.0, tex=lambda x, y: bayer(x, y) * 0.06)
    for x in range(4, 29, 2):                                                                            # the crimp
        put(img, x, 18 + (1 if x in (4, 28) else 0) + (0 if 8 < x < 24 else 0), crust_ramp[4 if x % 4 == 0 else 1])
    for x in range(5, 28, 3):
        put(img, x, 12 if 9 < x < 23 else 13, crust_ramp[4])
    for vx in (12, 16, 20):                                                                              # the vents
        dots(img, [(vx, 15), (vx, 16)], filling if baked else crust_ramp[1])
    if baked:
        dots(img, [(9, 14), (23, 15), (15, 13)], crust_ramp[0])                                          # browned spots
    return done(img)


def dough():
    """A lump of dough on a floured board, folded over, dusted."""
    img = canvas()
    part(img, lambda d: d.rounded_rectangle([2, 22, 30, 29], radius=2, fill=255), OAK, radius=0.8,
         tex=lambda x, y: 0.1 if y % 3 == 0 else 0.0)                                                    # the board
    part(img, lambda d: d.ellipse([6, 10, 26, 26], fill=255), PALE_DOUGH, radius=2.2)
    dots(img, [(10 + i, 16 + i // 3) for i in range(10)], PALE_DOUGH[1])                                 # the fold
    dots(img, [(4, 23), (8, 22), (25, 23), (27, 22), (12, 12), (19, 13), (15, 11)], (248, 244, 236, 255))   # flour
    return done(img)


# ---------------------------------------------------------------- meat

def roast():
    """A roast joint, browned and glistening, a slice cut away to show it pink inside, the bone end."""
    img = canvas()
    brown = ramp((140, 78, 40), 5, dark=0.5, light=1.45)
    part(img, lambda d: d.ellipse([3, 11, 25, 29], fill=255), brown, radius=1.8, tex=lambda x, y: 0.1 if (x + y) % 4 == 0 else 0.0)
    part(img, lambda d: d.ellipse([16, 14, 25, 27], fill=255), ramp((214, 132, 120), 4, dark=0.7), radius=0.9)   # the cut face
    bone = ramp((228, 216, 190), 4, main=2, dark=0.7, light=1.06)
    part(img, stick((6, 16), (2, 8), 3.0), bone)
    dots(img, [(7, 13), (10, 12), (13, 12)], brown[4])                                                    # glisten
    return done(img)


def pork_side():
    """A side of pork: a long slab, the rind on top, a band of white fat, the ribs showing."""
    img = canvas()
    rind = ramp((196, 140, 100), 4, dark=0.6)
    pink = ramp((214, 128, 120), 5, dark=0.55, light=1.2)
    part(img, poly([(2, 14), (28, 10), (30, 25), (4, 29)]), pink, radius=1.2)
    part(img, poly([(2, 14), (28, 10), (28, 14), (2, 18)]), [FAT, FAT, (246, 238, 226, 255)], radius=0.8)
    part(img, poly([(2, 13), (28, 9), (28, 11), (2, 15)]), rind, radius=0.5)
    for k in range(5):                                                                                   # ribs
        x = 7 + k * 5
        dots(img, [(x + i // 3, 18 + i) for i in range(8)], (240, 222, 206, 255))
    return done(img)


def salt_meat():
    """Salt meat: a pale, cured slab crusted with coarse salt."""
    img = canvas()
    part(img, lambda d: d.rounded_rectangle([4, 12, 28, 28], radius=4, fill=255), ramp((176, 112, 108), 5, dark=0.55, light=1.25), radius=1.6)
    for x, y in ((7, 14), (11, 13), (15, 15), (20, 13), (24, 15), (9, 18), (18, 18), (13, 21), (23, 20), (7, 23), (16, 25), (25, 25)):
        put(img, x, y, (250, 250, 252, 255))
        put(img, x + 1, y + 1, (214, 220, 228, 255))
    return done(img)


def smoked_meat():
    """Smoked meat: a dark mahogany ham hung on a loop of twine."""
    img = canvas()
    part(img, lambda d: d.ellipse([6, 10, 26, 30], fill=255), ramp((112, 56, 32), 5, dark=0.5, light=1.6), radius=1.8,
         tex=lambda x, y: 0.08 if (x * 3 + y) % 5 == 0 else 0.0)
    part(img, stick((16, 11), (14, 3), 1.6), [TWINE, TWINE])
    dots(img, [(13, 3), (14, 2), (15, 2), (16, 3)], TWINE)
    dots(img, [(x, 14) for x in range(10, 22)], TWINE)                                                   # the binding
    dots(img, [(10, 17), (11, 16), (13, 15)], (214, 140, 80, 255))                                       # the shine
    return done(img)


def chopped_meat():
    """Meat cut for the pot: red cubes on a board."""
    img = canvas()
    part(img, lambda d: d.rounded_rectangle([2, 20, 30, 29], radius=2, fill=255), OAK, radius=0.8)
    for x, y in ((6, 15), (12, 17), (18, 14), (23, 17), (9, 21), (16, 21), (21, 21)):
        part(img, lambda d, x=x, y=y: d.rectangle([x, y, x + 4, y + 4], fill=255), RED_MEAT, radius=0.5)
        put(img, x + 1, y, FAT)
    return done(img)


def dressed_fowl():
    """A plucked fowl, trussed for the spit: pale skin, the drumsticks tucked up."""
    img = canvas()
    skin = ramp((236, 206, 172), 5, dark=0.6, light=1.08)
    part(img, lambda d: d.ellipse([4, 12, 24, 29], fill=255), skin, radius=2.0, tex=lambda x, y: bayer(x, y) * 0.06)
    for a, z in (((20, 16), (28, 11)), ((21, 22), (29, 19))):                                            # the drumsticks
        part(img, stick(a, z, 4.0, 2.6), skin, radius=0.8)
        put(img, z[0], z[1], (244, 236, 220, 255))
    dots(img, [(x, 20) for x in range(9, 22, 3)], TWINE)                                                 # the trussing
    return done(img)


def fowl():
    """A fowl as the carter brings it, unplucked: brown speckled feathers, the neck hanging, the beak."""
    img = canvas()
    feathers = ramp((132, 92, 58), 5, dark=0.5, light=1.4)
    part(img, lambda d: d.ellipse([8, 10, 29, 27], fill=255), feathers, radius=1.8,
         tex=lambda x, y: 0.14 if (x + 2 * y) % 5 == 0 else 0.0)
    part(img, stick((10, 17), (4, 26), 3.0, 2.0), feathers[1:])                                          # the neck, hanging
    part(img, lambda d: d.ellipse([1, 24, 7, 30], fill=255), feathers[1:])
    dots(img, [(0, 28), (1, 29)], (214, 170, 60, 255))                                                   # the beak
    dots(img, [(3, 26)], (188, 50, 40, 255))
    for x, y in ((14, 14), (19, 13), (23, 16), (16, 19), (21, 21), (25, 20)):                            # speckles
        put(img, x, y, feathers[4])
    part(img, poly([(26, 12), (31, 8), (30, 15)]), feathers, radius=0.6)                                 # the tail
    return done(img)


# ---------------------------------------------------------------- fish and greens

def herring(img, dy, body, belly=None):
    """One herring along the canvas at ``dy``: body, forked tail, an eye."""
    part(img, lambda d: d.ellipse([4, 9 + dy, 22, 17 + dy], fill=255), body, line=body[0], radius=1.0)
    part(img, poly([(21, 13 + dy), (28, 8 + dy), (26, 13 + dy), (28, 18 + dy)]), body, line=body[0], radius=0.5)
    if belly:
        for x in range(6, 20):
            put(img, x, 15 + dy, belly)
    put(img, 8, 12 + dy, (20, 20, 30, 255))


def smoked_fish():
    """Two smoked herring hung by the tail on a loop of twine: golden-brown, glossy."""
    img = canvas()
    gold = ramp((176, 116, 48), 5, dark=0.5, light=1.45)
    herring(img, 2, gold, belly=gold[4])
    herring(img, 10, gold, belly=gold[4])
    dots(img, [(28, 8), (29, 7), (30, 9), (30, 17), (29, 18)], TWINE)
    return done(img)


def salt_fish():
    """A salt fish, split and dried stiff: pale grey flesh crusted white."""
    img = canvas()
    grey = ramp((196, 192, 180), 5, dark=0.6, light=1.15)
    part(img, poly([(3, 18), (12, 10), (24, 11), (29, 7), (28, 16), (29, 25), (24, 21), (12, 24)]), grey, line=grey[0], radius=1.0)
    for x, y in ((9, 16), (13, 13), (17, 18), (21, 14), (14, 20), (24, 17)):
        put(img, x, y, (252, 252, 252, 255))
    dots(img, [(x, 17) for x in range(8, 24)], grey[1])                                                 # the split
    return done(img)


def chopped_fish():
    """Fish cut for the pot: silver-skinned chunks on a board."""
    img = canvas()
    part(img, lambda d: d.rounded_rectangle([2, 20, 30, 29], radius=2, fill=255), OAK, radius=0.8)
    flesh = ramp((226, 196, 180), 4, dark=0.7)
    for x, y in ((5, 15), (12, 17), (19, 14), (24, 18), (9, 22)):
        part(img, lambda d, x=x, y=y: d.rectangle([x, y, x + 5, y + 4], fill=255), flesh, radius=0.5)
        dots(img, [(x + i, y) for i in range(6)], (150, 176, 186, 255))                                  # the skin
    return done(img)


def chopped_cabbage():
    """Cabbage shredded for the pot, heaped on a board."""
    img = canvas()
    part(img, lambda d: d.rounded_rectangle([2, 20, 30, 29], radius=2, fill=255), OAK, radius=0.8)
    leaf = ramp((120, 168, 90), 5, dark=0.5, light=1.3)
    part(img, lambda d: d.ellipse([5, 11, 27, 25], fill=255), leaf, radius=1.6,
         tex=lambda x, y: 0.25 if (x + 2 * y) % 4 == 0 else -0.05)
    for x, y in ((9, 15), (14, 13), (20, 16), (12, 19), (18, 20)):                                      # pale ribs
        dots(img, [(x, y), (x + 1, y + 1), (x + 2, y + 1)], (214, 230, 190, 255))
    return done(img)


# ---------------------------------------------------------------- staples

def sack(fill_ramp, grains=None):
    """An open sack, its neck rolled down, heaped with what's in it."""
    img = canvas()
    part(img, poly([(6, 14), (26, 14), (28, 29), (4, 29)]), SACK, radius=1.4, tex=lambda x, y: 0.1 if (x + y) % 2 == 0 else 0.0)
    part(img, lambda d: d.ellipse([5, 11, 27, 17], fill=255), SACK[1:], radius=0.8)                      # the rolled neck
    part(img, lambda d: d.ellipse([7, 6, 25, 15], fill=255), fill_ramp, radius=1.6, tex=grains)
    return done(img)


def flour():
    img = sack(ramp((240, 236, 228), 4, main=2, dark=0.8, light=1.03))
    dots(img, [(9, 4), (12, 3), (20, 4), (23, 5)], (250, 250, 250, 160))                                 # a puff of it
    return img


def oats():
    return sack(ramp((214, 180, 112), 4, dark=0.62, light=1.15), grains=lambda x, y: 0.22 if (x * 2 + y) % 3 == 0 else -0.05)


def coarse_salt():
    """Coarse salt heaped in a shallow earthenware dish, glinting."""
    img = canvas()
    part(img, lambda d: d.chord([3, 14, 29, 30], 0, 180, fill=255), CLAY_POT[1:], radius=1.2)
    part(img, lambda d: d.ellipse([5, 10, 27, 24], fill=255), ramp((226, 230, 236), 4, dark=0.75, light=1.06), radius=1.6,
         tex=lambda x, y: 0.2 if (x + y * 3) % 4 == 0 else 0.0)
    dots(img, [(10, 13), (16, 12), (21, 15), (13, 17)], (255, 255, 255, 255))
    return done(img)


def rations():
    """Rations for the road: a parcel wrapped in linen and tied with twine, a crust of bread
    peeking out of one end."""
    img = canvas()
    part(img, lambda d: d.rounded_rectangle([4, 12, 26, 29], radius=3, fill=255), LINEN, radius=1.4,
         tex=lambda x, y: 0.06 if (x + y) % 3 == 0 else 0.0)
    part(img, lambda d: d.ellipse([22, 14, 30, 22], fill=255), CRUST, radius=0.8)                        # the bread's end
    dots(img, [(x, 20) for x in range(4, 27)] + [(15, y) for y in range(12, 30)], TWINE)
    dots(img, [(14, 9), (13, 10), (16, 9), (17, 10), (15, 11)], TWINE)                                   # the knot's bow
    dots(img, [(6, 14), (8, 13)], LINEN[4])
    return done(img)


# ---------------------------------------------------------------- drinks

def spirits():
    """A squat stoneware bottle of spirits, stoppered with a cork."""
    img = canvas()
    stone = ramp((120, 132, 118), 5, dark=0.5, light=1.45)
    part(img, lambda d: d.ellipse([7, 12, 25, 30], fill=255), stone, radius=2.0)
    part(img, lambda d: d.rounded_rectangle([13, 6, 19, 15], radius=1, fill=255), stone, radius=0.8)
    part(img, lambda d: d.rectangle([13, 2, 19, 7], fill=255), ramp((176, 132, 84), 4), radius=0.6)      # the cork
    dots(img, [(10, 17), (11, 16), (12, 15)], stone[4])
    return done(img)


def mixed_drink():
    """A wooden cup of the house's mixed drink: dark and ruddy, a sprig of herb in it."""
    img = canvas()
    cup = ramp((150, 104, 62), 5)
    part(img, poly([(7, 11), (25, 11), (22, 29), (10, 29)]), cup, radius=1.2, tex=lambda x, y: 0.12 if x % 4 == 0 else 0.0)
    part(img, lambda d: d.ellipse([7, 8, 25, 14], fill=255), cup[1:], radius=0.8)
    part(img, lambda d: d.ellipse([9, 9, 23, 13], fill=255), ramp((126, 34, 40), 4, dark=0.6, light=1.4), radius=0.8, slope=-1.0)
    part(img, stick((19, 11), (24, 2), 1.4), ramp((96, 140, 70), 3))
    dots(img, [(23, 3), (25, 4), (22, 5)], (128, 176, 88, 255))
    return done(img)


SPRITES = {
    "tankard": tankard, "dirty_tankard": dirty_tankard, "bowl": bowl, "dirty_bowl": dirty_bowl,
    "stew": stew, "porridge": porridge, "trencher": trencher, "loaf": loaf,
    "pie": lambda: pie(CRUST), "raw_pie": lambda: pie(PALE_DOUGH, baked=False), "dough": dough,
    "roast": roast, "pork_side": pork_side, "salt_meat": salt_meat, "smoked_meat": smoked_meat,
    "chopped_meat": chopped_meat, "dressed_fowl": dressed_fowl, "fowl": fowl,
    "smoked_fish": smoked_fish, "salt_fish": salt_fish, "chopped_fish": chopped_fish, "chopped_cabbage": chopped_cabbage,
    "flour": flour, "oats": oats, "coarse_salt": coarse_salt, "rations": rations,
    "spirits": spirits, "mixed_drink": mixed_drink,
}


def generate():
    for name, make in SPRITES.items():
        save(make(), f"items/{name}")


if __name__ == "__main__":
    generate()
    if "--preview" in sys.argv:
        from norse_art import contact_sheet
        out = os.path.join(os.path.dirname(__file__), "..", ".cache", "food.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        contact_sheet([make() for make in SPRITES.values()], out, scale=4, columns=8)
        print(out)
