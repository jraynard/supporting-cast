#!/usr/bin/env python3
"""The mini-games' props (``ui/work/*``), small pixel scenes the work windows are drawn from, in the
anvil's style (norse_ui.work_anvil): side-on, lit from the top left, an ink outline. The game draws
them at a whole-pixel scale and moves, turns and tints them from the session in hand.

  whetstone, blade        the whetstone in its holder, and a blade (pale: tinted by its heat)
  stew_pot, salt_crock    the pot over the fire and the salt crock the pinches come from
  bed, sheet, hand        a bed side on, a clean sheet (laid on it), a hand (sweeps, tucks, draws)
  forearm, wrap           a forearm to bind, and a turn of bandage
  strongbox, probe        a strongbox's front with its lid seam, and the probe run along it
  curio, loupe            a piece under study and the loupe moved along it
  patient                 someone lying on a pallet, head to feet (the healer's hands run along them)
  stone, spirit_level     a dressed stone of the bridge's span, and the level laid on them
  rasp                    the file a lock's pins are filed down with
  cask_end, spigot, mallet  the cask's head, its tap and the mallet that drives the tap home

Writes only its own files. Run alone (``python3 tools/work_ui_art.py``) or from gen_placeholder_art.py.
"""
import os

from PIL import Image

from norse_art import ramp
from norse_ui import part, poly, stick, put, ink_outline, OAK, IRONR, WALNUT, ROOT
from norse_buildings import LINEN, MADDER, WOAD, STRAW

STONE = ramp((128, 134, 142), 5, dark=0.5, light=1.4)
SKIN = ramp((222, 172, 140), 5, dark=0.55, light=1.12)
CLAY = ramp((176, 110, 70), 5, dark=0.55, light=1.3)
STEW = ramp((150, 92, 50), 4, dark=0.55, light=1.35)
SALT = ramp((236, 234, 228), 3, main=1, dark=0.85, light=1.03)
BLACK_IRON = ramp((64, 66, 76), 5, main=1, dark=0.55, light=2.0)


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


def stew_pot():
    """The iron pot side on, its stew showing at the rim."""
    img = canvas(44, 30)
    part(img, poly([(2, 7), (42, 7), (40, 22), (34, 28), (10, 28), (4, 22)]), BLACK_IRON, radius=1.4)
    part(img, lambda d: d.ellipse([3, 4, 41, 10], fill=255), BLACK_IRON[1:], radius=0.5)
    part(img, lambda d: d.ellipse([6, 5, 38, 9], fill=255), STEW, radius=0.6, line=None,
         tex=lambda x, y: 0.6 if (x * 3 + y) % 7 == 0 else 0)
    for x in (1, 42):                                                       # the lugs
        part(img, lambda d, x=x: d.rectangle([x - 1, 9, x + 1, 13], fill=255), BLACK_IRON, radius=0.3)
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


SPRITES = (("whetstone", whetstone), ("blade", blade), ("stew_pot", stew_pot), ("salt_crock", salt_crock),
           ("bed", bed), ("sheet", sheet), ("hand", hand), ("forearm", forearm), ("wrap", wrap),
           ("strongbox", strongbox), ("probe", probe), ("curio", curio), ("loupe", loupe), ("patient", patient),
           ("stone", stone), ("spirit_level", spirit_level), ("rasp", rasp),
           ("cask_end", cask_end), ("spigot", spigot), ("mallet", mallet))


def generate():
    out = os.path.join(ROOT, "ui", "work")
    os.makedirs(out, exist_ok=True)
    for name, make in SPRITES:
        make().save(os.path.join(out, name + ".png"))
    print(f"work ui: wrote {len(SPRITES)} props")


if __name__ == "__main__":
    generate()
