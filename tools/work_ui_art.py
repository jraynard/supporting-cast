#!/usr/bin/env python3
"""The mini-games' props (``ui/work/*``), small pixel scenes the work windows are drawn from, in the
anvil's style (norse_ui.work_anvil): side-on, lit from the top left, an ink outline. The game draws
them at a whole-pixel scale and moves, turns and tints them from the session in hand.

  whetstone, blade        the whetstone in its holder, and a blade (pale: tinted by its heat)
  grindwheel              the smithy's grindstone wheel, side on (turned by the game as it spins)
  grind_posts, grind_trough  its trestle posts (behind the wheel) and the wet trough (in front)
  stew_pot, salt_crock    the pot over the fire and the salt crock the pinches come from
  bed, sheet, hand        a bed side on, a clean sheet (laid on it), a hand (sweeps, tucks, draws)
  forearm, wrap           a forearm to bind, and a turn of bandage
  strongbox, probe        a strongbox's front with its lid seam, and the probe run along it
  curio, loupe            a piece under study and the loupe moved along it
  patient                 someone lying on a pallet, head to feet (the healer's hands run along them)
  stone, spirit_level     a dressed stone of the bridge's span, and the level laid on them
  rasp                    the file a lock's pins are filed down with
  cask_end, spigot, mallet  the cask's head, its tap and the mallet that drives the tap home
  axe, log_side           the woodsman's axe lying flat (swung as the smith's hammer) and a log side on

The kitchen's:
  fire_bed, spoon, pot_lid   the logs and embers under the pot, the stirring spoon, the lid for a flare
  firedogs, joint            the spit's uprights and rod; a joint side on (pale: tinted by its crust)
  knead_board, dough         the floured board and the dough on it (pale: tinted as it comes together)
  oven_front, peel           the oven's mouth in its brick dome, and the peel the loaves go in on
  block_side, pork_side, cleaver  the butcher's block, a side of pork on it, the cleaver
  bird, feather              a fowl to pluck (pale skin) and a feather off it
  pie_top, chop_board, kitchen_knife  a pie's lid to crimp, the chopping board, the knife
  cabbage, fish, meat        food on the board
  cloth, cord                a ration's wrapping cloth and the cord it's tied with
The inn's:
  tankard, bottle, cup       a tankard side on, a bottle of what goes in a mix, the cup it's mixed in
  ledger_page, quill         the inn's book open at today's page, and the quill
  chamber_pot, jug, bucket   a room's pot, its jug, and the bucket the slops go in

Writes only its own files. Run alone (``python3 tools/work_ui_art.py``) or from gen_placeholder_art.py.
"""
import os

from PIL import Image

from norse_art import ramp
from norse_ui import part, poly, stick, ell, put, ink_outline, OAK, IRONR, WALNUT, ROOT
from norse_buildings import LINEN, MADDER, WOAD, STRAW

STONE = ramp((128, 134, 142), 5, dark=0.5, light=1.4)
SKIN = ramp((222, 172, 140), 5, dark=0.55, light=1.12)
CLAY = ramp((176, 110, 70), 5, dark=0.55, light=1.3)
STEW = ramp((150, 92, 50), 4, dark=0.55, light=1.35)
SALT = ramp((236, 234, 228), 3, main=1, dark=0.85, light=1.03)
BLACK_IRON = ramp((64, 66, 76), 5, main=1, dark=0.55, light=2.0)
POT_IRON = ramp((46, 45, 50), 5, main=1, dark=0.6, light=2.4)   # the stew pot's: near black, a warm sheen


def canvas(w, h):
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))


def whetstone():
    """The hone on its oak holder, the face it's worked on along the top."""
    img = canvas(48, 14)
    part(img, poly([(2, 8), (46, 8), (44, 13), (4, 13)]), OAK[1:], radius=0.8)
    part(img, lambda d: d.rounded_rectangle([5, 2, 43, 8], radius=1, fill=255), STONE, radius=1.0,
         tex=lambda x, y: -0.5 if (x * 3 + y) % 11 == 0 else 0)
    for x in range(6, 43):
        put(img, x, 2, STONE[4])
    return ink_outline(img)


def blade():
    """A knife's blade and tang, the edge along the bottom: greyscale, tinted by its heat."""
    img = canvas(44, 9)
    grey = [(v, v, v, 255) for v in (130, 170, 205, 235, 255)]
    part(img, poly([(0, 3), (12, 2), (36, 2), (43, 5), (36, 8), (12, 8), (0, 7)]), grey, radius=0.8)
    for x in range(13, 41):
        put(img, x, 7 if x < 37 else 7 - (x - 37) // 2, (255, 255, 255, 255))         # the bright edge
    return ink_outline(img)


GRIT = ramp((176, 156, 120), 5, dark=0.55, light=1.3)
WATER = ramp((54, 84, 98), 4, main=2, dark=0.6, light=1.6)


def grindwheel():
    """The grindstone side on: a sandstone disc, a worn darker rim, flecks and a pale chip so its turn
    shows, and the iron hub. Square, centred, so the game can spin it about its middle."""
    img = canvas(26, 26)
    part(img, ell([0, 0, 25, 25]), GRIT, radius=1.6)
    for x in range(26):
        for y in range(26):
            r = ((x - 12.5) ** 2 + (y - 12.5) ** 2) ** 0.5
            if 10.2 < r < 11.8:
                put(img, x, y, GRIT[1])                                          # the worn rim
    for x, y in ((7, 9), (16, 6), (18, 16), (9, 18), (13, 11), (5, 14), (20, 11)):
        put(img, x, y, GRIT[0])                                                  # flecks, to see it turn
    for x, y in ((14, 3), (15, 3), (15, 4)):
        put(img, x, y, GRIT[4])                                                  # a pale chip in the rim
    part(img, ell([10, 10, 15, 15]), IRONR[1:], radius=0.6)                      # the hub
    return ink_outline(img)


def grind_posts():
    """The trestle the wheel turns in: the post up to the axle (side on, the two overlap) behind the
    wheel, and its braces splayed out past the trough's ends."""
    img = canvas(52, 26)
    part(img, poly([(23, 0), (29, 0), (30, 25), (22, 25)]), OAK[1:], radius=0.8)
    part(img, poly([(1, 25), (23, 5), (25, 8), (6, 25)]), OAK[1:], radius=0.6)
    part(img, poly([(51, 25), (29, 5), (27, 8), (46, 25)]), OAK[1:], radius=0.6)
    return ink_outline(img)


def grind_trough():
    """The trough the wheel's foot runs in, in front of it: staves, an iron band, the water's edge."""
    img = canvas(36, 10)
    part(img, poly([(0, 2), (35, 2), (33, 9), (2, 9)]), OAK[1:], radius=0.8)
    for x in range(2, 34):
        put(img, x, 2, WATER[3]); put(img, x, 3, WATER[1])                        # the waterline
        put(img, x, 6, IRONR[2])
    return ink_outline(img)


def stew_pot():
    """The iron pot side on: a round hammered belly sooted low down, a lit lip, ring lugs, three
    stubby feet, and the stew showing at the rim with chunks in it."""
    img = canvas(44, 30)
    for x in (10, 21, 32):                                                  # the feet, behind the belly
        part(img, lambda d, x=x: d.rectangle([x, 24, x + 2, 29], fill=255), POT_IRON, radius=0.3)
    part(img, poly([(3, 8), (41, 8), (43, 13), (42, 19), (37, 25), (30, 27), (14, 27), (7, 25), (2, 19), (1, 13)]), POT_IRON,
         radius=1.8, tex=lambda x, y: (-0.9 if y > 22 else -0.4 if y > 19 else 0) + (0.4 if (x * 7 + y * 11) % 47 == 0 else 0))
    for y in range(11, 20):                                                 # a sheen down the lit side
        put(img, 6, y, POT_IRON[4] if 12 < y < 18 else POT_IRON[3])
        if 13 < y < 17:
            put(img, 7, y, POT_IRON[3])
    for x in (12, 22, 32):                                                  # a seam of rivets
        put(img, x, 10, POT_IRON[4]); put(img, x, 11, POT_IRON[0])
    part(img, ell([1, 3, 42, 11]), POT_IRON[1:], radius=0.5)               # the lip
    for x in range(6, 38):
        put(img, x, 3 if 9 < x < 34 else 4, POT_IRON[4])
    part(img, ell([5, 5, 38, 9]), STEW, radius=0.6, line=None,
         tex=lambda x, y: 0.6 if (x * 3 + y) % 7 == 0 else 0)
    for x, y, c in ((11, 6, (214, 168, 92, 255)), (17, 7, (150, 70, 52, 255)), (24, 6, (214, 168, 92, 255)),
                    (29, 7, (96, 122, 60, 255)), (33, 6, (150, 70, 52, 255)), (14, 8, (96, 122, 60, 255))):
        put(img, x, y, c); put(img, x + 1, y, c)                            # root, meat and herb
    for x in (0, 41):                                                       # ring lugs
        part(img, lambda d, x=x: d.ellipse([x - 1, 9, x + 3, 14], fill=255), POT_IRON, radius=0.3)
        put(img, x + 1, 11, (0, 0, 0, 0)); put(img, x + 1, 12, (0, 0, 0, 0))
    return ink_outline(img)


def salt_crock():
    img = canvas(18, 20)
    part(img, poly([(3, 4), (15, 4), (16, 10), (14, 19), (4, 19), (2, 10)]), CLAY, radius=1.0)
    part(img, lambda d: d.ellipse([3, 2, 15, 6], fill=255), CLAY[1:], radius=0.4)
    part(img, lambda d: d.ellipse([5, 3, 13, 5], fill=255), SALT, radius=0.3, line=None)
    for x in range(3, 16):
        put(img, x, 12, WOAD[2])                                            # a painted band
    return ink_outline(img)


def bed():
    """A bed side on: posts, the frame, a straw mattress and the pillow at the head (left)."""
    img = canvas(64, 26)
    for x in (2, 59):
        part(img, lambda d, x=x: d.rectangle([x, 6 if x == 2 else 12, x + 3, 25], fill=255), OAK[1:], radius=0.6)
    part(img, lambda d: d.rectangle([4, 16, 60, 21], fill=255), OAK[1:], radius=0.8)
    part(img, lambda d: d.rounded_rectangle([5, 11, 59, 17], radius=2, fill=255), STRAW, radius=1.0,
         tex=lambda x, y: -0.5 if (x + y * 2) % 5 == 0 else 0)
    part(img, lambda d: d.rounded_rectangle([6, 7, 18, 12], radius=3, fill=255), LINEN, radius=1.0)
    return ink_outline(img)


def sheet():
    """A clean linen sheet, laid along the mattress (the game folds and shifts it)."""
    img = canvas(46, 6)
    part(img, lambda d: d.rounded_rectangle([0, 0, 45, 5], radius=1, fill=255), LINEN, radius=0.8)
    for x in range(1, 45, 6):
        put(img, x, 4, MADDER[2])                                           # a woven red stripe at the hem
    return ink_outline(img)


def hand():
    """An open hand from the side, palm down, the fingers to the right."""
    img = canvas(22, 14)
    part(img, poly([(0, 5), (8, 3), (19, 4), (21, 6), (19, 8), (12, 9), (8, 12), (5, 11), (0, 9)]), SKIN, radius=1.0)
    for x in range(12, 19, 2):
        put(img, x, 6, SKIN[1])                                             # between the fingers
    return ink_outline(img)


def forearm():
    """A forearm across the scene, the wrist and hand at the right."""
    img = canvas(64, 16)
    part(img, poly([(0, 2), (40, 3), (50, 4), (56, 3), (63, 6), (62, 11), (54, 13), (48, 12), (40, 13), (0, 14)]), SKIN, radius=1.4)
    return ink_outline(img)


def wrap():
    """One turn of linen bandage, crossing the arm at a slant."""
    img = canvas(8, 18)
    part(img, poly([(2, 0), (7, 1), (6, 17), (1, 16)]), LINEN, radius=0.6)
    return ink_outline(img)


def strongbox():
    """A strongbox's front: oak boards in iron straps, the lid's seam a third of the way down, the
    hasp at the left and the hinge at the right (as the probe runs from hasp to hinge)."""
    img = canvas(64, 30)
    part(img, lambda d: d.rectangle([1, 1, 62, 28], fill=255), WALNUT, radius=1.2,
         tex=lambda x, y: -0.5 if y in (6, 16, 23) else 0)
    for x in (6, 31, 56):
        part(img, lambda d, x=x: d.rectangle([x, 1, x + 2, 28], fill=255), IRONR[1:], radius=0.3)
    part(img, lambda d: d.rectangle([0, 7, 5, 15], fill=255), IRONR[1:], radius=0.4)     # the hasp
    part(img, lambda d: d.rectangle([59, 8, 63, 12], fill=255), IRONR[1:], radius=0.4)   # the hinge
    img = ink_outline(img)
    for x in range(6, 59):                                                  # the lid's seam, over it all
        put(img, x, 10, (20, 13, 10, 255))
        put(img, x, 11, WALNUT[3])
    return img


def probe():
    """The probe: a fine steel pick on a walnut grip, the point at the bottom."""
    img = canvas(6, 28)
    part(img, stick((3, 1), (3, 12), 3.4, 3.0), WALNUT, radius=0.4)
    part(img, stick((3, 12), (3, 27), 1.4, 0.6), IRONR[2:], radius=0.3)
    return ink_outline(img)


def curio():
    """A curio under study: a brass astrolabe-like piece, rings and a pointer, on a cloth."""
    img = canvas(64, 24)
    part(img, lambda d: d.rounded_rectangle([0, 16, 63, 23], radius=2, fill=255), WOAD, radius=1.0)
    brass = ramp((196, 152, 70), 5, dark=0.5, light=1.3)
    part(img, lambda d: d.ellipse([6, 2, 30, 20], fill=255), brass, radius=1.4)
    part(img, lambda d: d.ellipse([11, 6, 25, 16], fill=255), brass[:3], radius=0.6)
    part(img, poly([(18, 11), (28, 4), (29, 5), (19, 12)]), brass[2:], radius=0.3)
    part(img, lambda d: d.rounded_rectangle([34, 8, 58, 18], radius=2, fill=255), brass, radius=1.0)
    for x in range(37, 56, 4):
        put(img, x, 12, brass[0]); put(img, x, 13, brass[4])               # engraved ticks
    return ink_outline(img)


def loupe():
    """A loupe: a brass-rimmed lens on a short handle, the handle down."""
    img = canvas(20, 30)
    brass = ramp((196, 152, 70), 5, dark=0.5, light=1.3)
    part(img, stick((10, 18), (10, 29), 3.2, 2.6), WALNUT, radius=0.4)
    part(img, lambda d: d.ellipse([1, 1, 19, 19], fill=255), brass, radius=0.6)
    img = ink_outline(img)
    for y in range(3, 18):                                                  # the glass: clear, a little blue
        for x in range(3, 18):
            if (x - 10) ** 2 + (y - 10) ** 2 <= 42:
                img.putpixel((x, y), (200, 226, 236, 70 if (x + y) > 18 else 110))
    put(img, 7, 6, (255, 255, 255, 230)); put(img, 6, 7, (255, 255, 255, 170)); put(img, 8, 5, (255, 255, 255, 140))
    return img


def patient():
    """Someone lying on a pallet, side on, the head at the left and the feet at the right."""
    img = canvas(128, 26)
    part(img, lambda d: d.rectangle([2, 18, 125, 25], fill=255), STRAW, radius=1.0,
         tex=lambda x, y: -0.5 if (x + y * 2) % 5 == 0 else 0)
    part(img, lambda d: d.ellipse([4, 4, 20, 18], fill=255), SKIN, radius=1.2)          # the head
    part(img, poly([(18, 9), (60, 8), (92, 10), (118, 11), (124, 13), (124, 18), (18, 19)]), LINEN, radius=1.4)
    part(img, poly([(60, 13), (124, 13), (124, 18), (60, 18)]), WOAD[1:], radius=0.8)   # a blanket over the legs
    part(img, lambda d: d.ellipse([119, 8, 127, 16], fill=255), SKIN, radius=0.6)       # the feet
    return ink_outline(img)


def stone():
    """A dressed stone for the span, pale where it's been worked."""
    img = canvas(18, 14)
    part(img, lambda d: d.rounded_rectangle([0, 0, 17, 13], radius=1, fill=255), STONE, radius=1.2,
         tex=lambda x, y: -0.5 if (x * 2 + y) % 7 == 0 else 0)
    return ink_outline(img)


def spirit_level():
    """A wooden level with its vial: the bubble is drawn by the game."""
    img = canvas(44, 9)
    part(img, lambda d: d.rectangle([0, 1, 43, 8], fill=255), OAK[1:], radius=0.8)
    part(img, lambda d: d.rounded_rectangle([13, 2, 30, 6], radius=2, fill=255), ramp((170, 200, 120), 4), radius=0.4, line=None)
    for x in (20, 23):
        for y in range(2, 7):
            put(img, x, y, (40, 30, 20, 255))                              # the marks either side of true
    return ink_outline(img)


def rasp():
    """A file, the tang into a wooden handle at the left."""
    img = canvas(34, 7)
    part(img, stick((1, 3), (11, 3), 4.6, 4.2), WALNUT, radius=0.5)
    part(img, lambda d: d.rectangle([11, 1, 33, 5], fill=255), IRONR[1:], radius=0.4,
         tex=lambda x, y: -0.7 if (x + y) % 2 == 0 else 0)
    return ink_outline(img)


def cask_end():
    """A cask's head end on: oak staves in an iron hoop, the bung, a chalked tally."""
    img = canvas(40, 40)
    oak = ramp((150, 104, 60), 6, main=3, dark=0.45, light=1.3)
    part(img, lambda d: d.ellipse([1, 1, 38, 38], fill=255), IRONR[1:], radius=0.8)
    part(img, lambda d: d.ellipse([4, 4, 35, 35], fill=255), oak, radius=2.0,
         tex=lambda x, y: -0.7 if x % 6 == 0 else 0)
    part(img, lambda d: d.ellipse([16, 22, 22, 28], fill=255), oak[:3], radius=0.4)      # the bung
    for y in (10, 12):                                                                  # chalk tally marks
        for x in range(13, 24, 3):
            put(img, x, y, (232, 228, 216, 255))
    return ink_outline(img)


def spigot():
    """The bronze tap, its key on top, the spout at the left (into the cask at the right)."""
    img = canvas(26, 12)
    bronze = ramp((186, 130, 66), 5, dark=0.5, light=1.35)
    part(img, lambda d: d.rectangle([4, 4, 25, 8], fill=255), bronze, radius=0.5)
    part(img, poly([(0, 6), (5, 4), (5, 11), (2, 11)]), bronze, radius=0.4)                # the spout
    part(img, lambda d: d.rectangle([12, 0, 16, 4], fill=255), bronze[1:], radius=0.3)     # the key
    return ink_outline(img)


def mallet():
    """A wooden mallet, the head at the left, the handle running right."""
    img = canvas(40, 16)
    part(img, stick((10, 8), (39, 9), 3.0, 2.4), WALNUT, radius=0.5)
    oak = ramp((150, 104, 60), 5, dark=0.45, light=1.3)
    part(img, lambda d: d.rounded_rectangle([0, 0, 12, 15], radius=2, fill=255), oak, radius=1.0,
         tex=lambda x, y: -0.6 if y in (3, 12) else 0)
    return ink_outline(img)


def axe():
    """A woodsman's axe lying flat, as the smith's hammer (norse_ui.work_hammer): the head at the
    right, its bit facing down, the haft running left to the hand (the pivot end)."""
    img = canvas(34, 16)
    part(img, stick((1, 6), (27, 5), 2.2, 2.6), WALNUT, radius=0.6)
    steel = [(v, v, v + 8, 255) for v in (96, 134, 170, 204, 236)]
    part(img, poly([(24, 1), (31, 1), (31, 6), (33, 15), (21, 15), (24, 7)]), steel, radius=0.8)
    for x in range(22, 33):
        put(img, x, 15, steel[4])                                                     # the bit, honed bright
    return ink_outline(img)


def log_side():
    """A log lying side on, bark on, its end showing at the right: what the axe is hewing."""
    img = canvas(64, 20)
    part(img, lambda d: d.rounded_rectangle([0, 2, 56, 19], radius=3, fill=255), LOG, radius=1.2,
         tex=lambda x, y: -0.5 if (x * 7 + y * 3) % 11 == 0 else 0)
    part(img, ell([50, 1, 63, 19]), ramp((206, 168, 112), 5, dark=0.55, light=1.2), radius=1.0)
    for r in (2, 4):
        for t in range(0, 360, 20):
            import math
            put(img, round(56.5 + r * 0.6 * math.cos(math.radians(t))), round(10 + r * math.sin(math.radians(t))), (150, 110, 70, 255))
    return ink_outline(img)


# ---------------------------------------------------------------- the kitchen

EMBER = ramp((214, 96, 40), 5, dark=0.45, light=1.4)
LOG = ramp((110, 74, 46), 5, dark=0.5, light=1.3)
PALEC = [(v, v, v, 255) for v in (120, 160, 196, 226, 250)]       # tinted by the game
BRICK = ramp((168, 84, 60), 5, dark=0.5, light=1.3)
FLOUR = ramp((236, 228, 210), 4, main=2, dark=0.75, light=1.05)
PORK = ramp((222, 150, 140), 5, dark=0.55, light=1.15)
FAT = ramp((240, 226, 206), 3, main=1, dark=0.85, light=1.04)
GREENS = ramp((108, 156, 72), 5, dark=0.5, light=1.35)
FISHR = ramp((128, 160, 176), 5, dark=0.5, light=1.35)
GLASS = ramp((96, 128, 96), 5, dark=0.5, light=1.6)
CREAM = ramp((228, 220, 200), 4, main=2, dark=0.7, light=1.06)


def fire_bed():
    """Two logs crossed on a bed of embers: the fire under the pot or the spit (the game scales it with the flame)."""
    img = canvas(44, 12)
    part(img, ell([0, 4, 43, 11]), EMBER, radius=1.2, tex=lambda x, y: 0.8 if (x * 5 + y * 3) % 7 == 0 else 0)
    part(img, stick((3, 8), (38, 4), 3.0), LOG, radius=0.6)
    part(img, stick((6, 3), (40, 9), 3.0), LOG, radius=0.6)
    return ink_outline(img)


def spoon():
    """A wooden stirring spoon, the bowl at the bottom (it's turned about its top)."""
    img = canvas(8, 34)
    part(img, stick((4, 0), (4, 26), 2.2, 2.6), OAK, radius=0.5)
    part(img, ell([1, 24, 7, 33]), OAK, radius=0.8)
    return ink_outline(img)


def pot_lid():
    """An iron lid side on, its knob on top."""
    img = canvas(46, 9)
    part(img, poly([(0, 8), (6, 3), (40, 3), (45, 8)]), BLACK_IRON, radius=0.8)
    part(img, lambda d: d.rectangle([20, 0, 25, 3], fill=255), BLACK_IRON[1:], radius=0.3)
    return ink_outline(img)


def firedogs():
    """The spit: two forked iron uprights and the rod across them, a crank at the right."""
    img = canvas(76, 34)
    for x in (6, 64):
        part(img, poly([(x, 33), (x + 2, 8), (x - 2, 2), (x + 1, 2), (x + 3, 6), (x + 5, 2), (x + 8, 2), (x + 4, 8), (x + 6, 33)]), BLACK_IRON, radius=0.5)
    part(img, lambda d: d.rectangle([2, 5, 72, 7], fill=255), IRONR[1:], radius=0.3)
    part(img, poly([(72, 4), (75, 4), (75, 14), (72, 14)]), IRONR[1:], radius=0.3)
    return ink_outline(img)


def joint():
    """A joint on the spit side on, pale so the game can tint it by its crust."""
    img = canvas(44, 20)
    part(img, ell([0, 1, 43, 19]), PALEC, radius=2.0, tex=lambda x, y: -0.4 if (x + y * 2) % 9 == 0 else 0)
    return ink_outline(img)


def knead_board():
    img = canvas(72, 10)
    part(img, lambda d: d.rectangle([0, 2, 71, 9], fill=255), OAK, radius=0.8, tex=lambda x, y: -0.5 if x % 12 == 0 else 0)
    for x in range(4, 68, 3):
        put(img, x, 2 + (x % 2), FLOUR[3])                                  # flour on it
    return ink_outline(img)


def dough():
    """The dough, pale: the game tints it as it comes together."""
    img = canvas(34, 16)
    part(img, poly([(2, 15), (4, 6), (11, 1), (23, 1), (30, 6), (33, 15)]), PALEC, radius=1.6)
    return ink_outline(img)


def oven_front():
    """The bread oven end on: a brick dome, the arched mouth (dark, for the glow) and its sill."""
    img = canvas(64, 48)
    part(img, poly([(2, 47), (2, 22), (10, 8), (24, 2), (40, 2), (54, 8), (62, 22), (62, 47)]), BRICK, radius=2.2,
         tex=lambda x, y: -0.7 if y % 6 == 0 or (x + (y // 6) * 4) % 9 == 0 else 0)
    part(img, poly([(18, 44), (18, 30), (24, 22), (40, 22), (46, 30), (46, 44)]), [(24, 16, 14, 255)] * 5, radius=0.2)
    part(img, lambda d: d.rectangle([12, 43, 52, 47], fill=255), STONE, radius=0.6)
    return ink_outline(img)


def peel():
    img = canvas(58, 10)
    part(img, stick((0, 5), (38, 5), 2.2), OAK, radius=0.4)
    part(img, poly([(36, 1), (57, 0), (57, 9), (36, 8)]), OAK, radius=0.8)
    return ink_outline(img)


def block_side():
    """The butcher's block, a hooped stump, its scarred top."""
    img = canvas(56, 28)
    part(img, poly([(2, 4), (53, 4), (51, 27), (4, 27)]), LOG, radius=1.4, tex=lambda x, y: -0.6 if x % 7 == 0 else 0)
    part(img, lambda d: d.rectangle([2, 2, 53, 6], fill=255), OAK, radius=0.6)
    for y in (12, 22):
        for x in range(3, 53):
            put(img, x, y, IRONR[2])
    return ink_outline(img)


def pork_side():
    """A side of pork, fat along the top: the game parts it at the joints."""
    img = canvas(52, 16)
    part(img, poly([(0, 6), (6, 1), (46, 1), (51, 6), (49, 15), (2, 15)]), PORK, radius=1.4)
    for x in range(4, 48):
        put(img, x, 3, FAT[2]); put(img, x, 4, FAT[1])
    for x in (13, 25, 37):
        for y in range(5, 14, 2):
            put(img, x, y, PORK[1])                                         # where the joints part
    return ink_outline(img)


def cleaver():
    img = canvas(32, 16)
    part(img, stick((18, 8), (31, 9), 2.6), WALNUT, radius=0.4)
    part(img, poly([(0, 0), (19, 0), (19, 15), (2, 13)]), [(v, v, v + 8, 255) for v in (110, 150, 186, 214, 240)], radius=0.8)
    return ink_outline(img)


def bird():
    """A fowl to pluck, side on, pale skin (the game draws its feathers over it, thinning)."""
    img = canvas(36, 24)
    part(img, ell([2, 4, 30, 23]), CREAM, radius=1.6)
    part(img, stick((28, 10), (35, 4), 2.6, 2.0), CREAM, radius=0.4)
    for x in (12, 20):
        part(img, stick((x, 21), (x, 23), 1.2), STRAW[1:], radius=0.2)
    return ink_outline(img)


def feather():
    img = canvas(7, 12)
    part(img, poly([(3, 0), (6, 5), (4, 11), (2, 11), (0, 5)]), LEATHER if False else ramp((150, 120, 90), 4), radius=0.5)
    return ink_outline(img)


def pie_top():
    img = canvas(48, 14)
    part(img, poly([(0, 13), (3, 6), (10, 2), (38, 2), (45, 6), (47, 13)]), ramp((214, 170, 110), 5, dark=0.55, light=1.2), radius=1.4)
    for x in (16, 24, 32):
        put(img, x, 6, (110, 70, 40, 255)); put(img, x + 1, 7, (110, 70, 40, 255))  # the slits
    return ink_outline(img)


def chop_board():
    img = canvas(64, 8)
    part(img, lambda d: d.rounded_rectangle([0, 1, 63, 7], radius=2, fill=255), OAK, radius=0.8)
    return ink_outline(img)


def kitchen_knife():
    img = canvas(36, 8)
    part(img, stick((22, 4), (35, 4), 2.4), WALNUT, radius=0.4)
    part(img, poly([(0, 5), (4, 2), (23, 2), (23, 7), (2, 7)]), [(v, v, v + 6, 255) for v in (120, 160, 196, 226, 250)], radius=0.6)
    return ink_outline(img)


def cabbage():
    img = canvas(18, 16)
    part(img, ell([0, 1, 17, 15]), GREENS, radius=1.4, tex=lambda x, y: -0.6 if (x - 8) ** 2 + (y - 8) ** 2 in range(20, 26) else 0)
    return ink_outline(img)


def fish_side():
    img = canvas(28, 12)
    part(img, poly([(0, 6), (6, 1), (18, 1), (22, 5), (27, 1), (27, 11), (22, 7), (18, 11), (6, 11)]), FISHR, radius=1.0)
    put(img, 5, 5, INKC)
    return ink_outline(img)


def meat():
    img = canvas(20, 14)
    part(img, poly([(0, 7), (4, 1), (16, 1), (19, 7), (16, 13), (4, 13)]), PORK, radius=1.2)
    for x in range(3, 17):
        put(img, x, 3, FAT[2])
    return ink_outline(img)


def cloth():
    """A square of cloth laid out flat (the game folds its corners in)."""
    img = canvas(32, 32)
    part(img, lambda d: d.rectangle([0, 0, 31, 31], fill=255), LINEN, radius=1.0, tex=lambda x, y: -0.4 if (x + y) % 6 == 0 else 0)
    return ink_outline(img)


def cord():
    img = canvas(34, 4)
    part(img, lambda d: d.rectangle([0, 1, 33, 3], fill=255), STRAW, radius=0.3, tex=lambda x, y: -0.7 if x % 3 == 0 else 0)
    return ink_outline(img)


# ---------------------------------------------------------------- the inn

def tankard():
    """A tankard side on, its handle at the right (empty: the game draws the ale rising in it)."""
    img = canvas(22, 22)
    oak = ramp((150, 104, 60), 5, dark=0.45, light=1.3)
    part(img, lambda d: d.rectangle([2, 2, 15, 21], fill=255), oak, radius=1.0, tex=lambda x, y: -0.6 if x % 4 == 0 else 0)
    for y in (5, 17):
        for x in range(2, 16):
            put(img, x, y, IRONR[2])
    part(img, poly([(15, 6), (21, 6), (21, 16), (15, 16), (15, 13), (18, 13), (18, 9), (15, 9)]), oak, radius=0.4)
    return ink_outline(img)


def bottle():
    img = canvas(12, 30)
    part(img, poly([(4, 0), (8, 0), (8, 8), (11, 12), (11, 29), (0, 29), (0, 12), (4, 8)]), GLASS, radius=1.0)
    part(img, lambda d: d.rectangle([4, 0, 8, 2], fill=255), OAK, radius=0.2)
    for y in range(13, 26):
        put(img, 2, y, GLASS[4])
    return ink_outline(img)


def cup():
    img = canvas(20, 16)
    part(img, poly([(0, 0), (19, 0), (16, 15), (3, 15)]), ramp((190, 150, 90), 5, dark=0.5, light=1.3), radius=1.0)
    return ink_outline(img)


def ledger_page():
    """The book open on the board: two pages, ruled, last nights' names in a faded hand."""
    img = canvas(92, 44)
    part(img, poly([(0, 4), (45, 0), (46, 43), (1, 40)]), CREAM, radius=1.2)
    part(img, poly([(46, 0), (91, 4), (90, 40), (46, 43)]), CREAM, radius=1.2)
    for y in range(9, 38, 6):
        for x in range(4, 88):
            if abs(x - 46) > 2:
                put(img, x, y, CREAM[1])
    for y in range(8, 30, 6):                                             # yesterday's lines, faded
        for x in range(6, 30, 2):
            put(img, x, y - 1, (120, 104, 90, 255))
    return ink_outline(img)


def quill():
    img = canvas(32, 10)
    part(img, poly([(0, 9), (10, 4), (31, 0), (24, 6), (8, 8)]), ramp((236, 232, 222), 4, main=2, dark=0.7, light=1.04), radius=0.6)
    return ink_outline(img)


def chamber_pot():
    img = canvas(24, 16)
    part(img, poly([(2, 3), (21, 3), (19, 15), (4, 15)]), CREAM, radius=1.2)
    part(img, ell([1, 0, 22, 5]), CREAM[1:], radius=0.4)
    part(img, poly([(21, 6), (24, 6), (24, 11), (20, 11)]), CREAM, radius=0.3)
    for x in range(4, 20):
        put(img, x, 9, WOAD[2])                                            # a blue band
    return ink_outline(img)


def jug():
    img = canvas(18, 24)
    part(img, poly([(4, 0), (12, 0), (14, 4), (16, 14), (13, 23), (3, 23), (1, 14), (3, 4)]), CLAY, radius=1.2)
    part(img, poly([(0, 1), (5, 1), (4, 4)]), CLAY, radius=0.3)            # the lip
    return ink_outline(img)


def bucket():
    img = canvas(24, 24)
    oak = ramp((150, 104, 60), 5, dark=0.45, light=1.3)
    part(img, poly([(1, 6), (22, 6), (19, 23), (4, 23)]), oak, radius=1.0, tex=lambda x, y: -0.6 if x % 5 == 0 else 0)
    for y in (9, 19):
        for x in range(2, 22):
            put(img, x, y, IRONR[2])
    for t in range(0, 23):                                                 # the bail
        put(img, t, 6 - round(5 * (1 - ((t - 11) / 11) ** 2)), IRONR[2])
    return ink_outline(img)


INKC = (30, 22, 20, 255)

SPRITES = (("whetstone", whetstone), ("blade", blade), ("stew_pot", stew_pot), ("salt_crock", salt_crock),
           ("bed", bed), ("sheet", sheet), ("hand", hand), ("forearm", forearm), ("wrap", wrap),
           ("grindwheel", grindwheel), ("grind_posts", grind_posts), ("grind_trough", grind_trough),
           ("strongbox", strongbox), ("probe", probe), ("curio", curio), ("loupe", loupe), ("patient", patient),
           ("stone", stone), ("spirit_level", spirit_level), ("rasp", rasp),
           ("cask_end", cask_end), ("spigot", spigot), ("mallet", mallet), ("axe", axe), ("log_side", log_side),
           ("fire_bed", fire_bed), ("spoon", spoon), ("pot_lid", pot_lid), ("firedogs", firedogs), ("joint", joint),
           ("knead_board", knead_board), ("dough", dough), ("oven_front", oven_front), ("peel", peel),
           ("block_side", block_side), ("pork_side", pork_side), ("cleaver", cleaver), ("bird", bird), ("feather", feather),
           ("pie_top", pie_top), ("chop_board", chop_board), ("kitchen_knife", kitchen_knife), ("cabbage", cabbage),
           ("fish", fish_side), ("meat", meat), ("cloth", cloth), ("cord", cord),
           ("tankard", tankard), ("bottle", bottle), ("cup", cup), ("ledger_page", ledger_page), ("quill", quill),
           ("chamber_pot", chamber_pot), ("jug", jug), ("bucket", bucket))


def generate():
    out = os.path.join(ROOT, "ui", "work")
    os.makedirs(out, exist_ok=True)
    for name, make in SPRITES:
        make().save(os.path.join(out, name + ".png"))
    print(f"work ui: wrote {len(SPRITES)} props")


if __name__ == "__main__":
    generate()
