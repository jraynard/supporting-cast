#!/usr/bin/env python3
"""The village's scenery (data/defs/village.json) and the half-timbered building kit
(buildings.json "timber_plaster", "timber_shingle"): lanes and garden soil, plaster and dark oak,
straw thatch, the well, the stone bridge, fences and gates, crates and cabbages.

Programmer art in the style of the rest: 32x32 tiles, 128px chibi plants, the shared world grade
(placeholder_nature.world_finish). Writes only the files listed here, so it's safe to re-run on its
own: python3 tools/village_art.py. gen_placeholder_art.py runs it too, after the Norse pass.
"""
import json
import os
import random

from PIL import Image, ImageDraw

import placeholder_nature

S = 32
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data", "textures")


def save(img, name, finish=True):
    path = os.path.join(ROOT, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    (placeholder_nature.world_finish(img) if finish else img).save(path)


def clamp(v):
    return max(0, min(255, int(v)))


def tint(c, k):
    return (clamp(c[0] * k), clamp(c[1] * k), clamp(c[2] * k), 255)


def noise(base, var, seed, size=(S, S)):
    rnd = random.Random(seed)
    img = Image.new("RGBA", size)
    for y in range(size[1]):
        for x in range(size[0]):
            k = 1 + rnd.uniform(-var, var) / 100
            img.putpixel((x, y), tint(base, k))
    return img


# ---------------- Ground ----------------

ROAD = (178, 152, 110)


def road(seed, ruts):
    img = noise(ROAD, 7, seed)
    rnd = random.Random(seed + 1)
    d = ImageDraw.Draw(img)
    for _ in range(9):  # pebbles
        x, y = rnd.randrange(S), rnd.randrange(S)
        d.point((x, y), fill=tint(ROAD, rnd.choice([0.72, 1.18])))
    for _ in range(5):  # worn darker patches
        x, y = rnd.randrange(S), rnd.randrange(S)
        d.rectangle([x, y, x + rnd.randrange(2, 5), y + 1], fill=tint(ROAD, 0.9))
    if ruts:
        for y0 in (9, 22):
            for x in range(S):
                y = y0 + (1 if (x // 6 + seed) % 3 == 0 else 0)
                img.putpixel((x, y), tint(ROAD, 0.74))
                img.putpixel((x, y + 1), tint(ROAD, 0.84))
    return img


def tilled(seed):
    base = (96, 66, 44)
    img = noise(base, 8, seed)
    d = ImageDraw.Draw(img)
    for y in range(0, S, 4):  # furrows: a dark trough and a lit ridge
        d.line([(0, y), (S, y)], fill=tint(base, 0.66))
        d.line([(0, y + 2), (S, y + 2)], fill=tint(base, 1.22))
    return img


# ---------------- Timber and plaster ----------------

OAK_DARK = (66, 46, 32)
# Limewash with a little ochre in it: the world grade cools and the evening light blues it, so it
# starts warmer than it ends up (a plain cream came out the grey of wet slate).
PLASTER = (242, 214, 160)


def timber_dark(seed=5):
    img = noise(OAK_DARK, 9, seed)
    rnd = random.Random(seed)
    d = ImageDraw.Draw(img)
    for _ in range(7):  # grain
        y = rnd.randrange(S)
        x = rnd.randrange(S - 10)
        d.line([(x, y), (x + rnd.randrange(6, 14), y)], fill=tint(OAK_DARK, 0.72))
    return img


# One cell of wall, one storey: 32 across and 64 up, so it shows once a level, pixels square
# (Rendering/WorldScale). The face is 0.84 of a level and hangs from its top, so rows below
# WALL_SHOWN are cut off: the sill plate sits just above that.
WALL_H = 64
WALL_SHOWN = 54


def _beam(img, box, seed, vertical):
    """A dark oak member: grain along it, its lit edge, and a shadow it casts on the plaster."""
    x0, y0, x1, y1 = box
    rnd = random.Random(seed)
    d = ImageDraw.Draw(img)
    d.rectangle(box, fill=tint(OAK_DARK, 1.0))
    for _ in range((x1 - x0 + y1 - y0) // 3):
        if vertical:
            x = rnd.randrange(x0, x1 + 1)
            y = rnd.randrange(y0, max(y0 + 1, y1 - 4))
            d.line([(x, y), (x, y + rnd.randrange(2, 6))], fill=tint(OAK_DARK, rnd.choice([0.74, 1.18])))
        else:
            y = rnd.randrange(y0, y1 + 1)
            x = rnd.randrange(x0, max(x0 + 1, x1 - 4))
            d.line([(x, y), (x + rnd.randrange(2, 7), y)], fill=tint(OAK_DARK, rnd.choice([0.74, 1.18])))
    # The edge facing the light (up and left) catches it.
    if vertical:
        d.line([(x0, y0), (x0, y1)], fill=tint(OAK_DARK, 1.32))
    else:
        d.line([(x0, y0), (x1, y0)], fill=tint(OAK_DARK, 1.32))


def _brace(img, a, b, seed):
    """A diagonal brace, three pixels thick, with its upper edge lit."""
    d = ImageDraw.Draw(img)
    d.line([a, b], fill=tint(OAK_DARK, 1.0), width=3)
    d.line([(a[0], a[1] - 1), (b[0], b[1] - 1)], fill=tint(OAK_DARK, 1.25), width=1)


def wall_timber_plaster(variant=0, seed=21):
    """
    A bay of half-timbering: a post at its left (the next bay's post closes it on the right), the
    head beam, a middle rail and the sill, limewashed infill mottled where the wash is thin, and
    darker under each rail. The variants differ in what's framed into the bay, so a long wall picks
    among them cell by cell and doesn't repeat: plain, a brace each way, or an extra stud.
    """
    rnd = random.Random(seed + variant * 101)
    img = noise(PLASTER, 3, seed + variant, size=(S, WALL_H))
    px = img.load()
    # The wash: soft blotches a shade darker or paler, and a few flecks where it's flaked.
    for _ in range(6):
        cx, cy, r = rnd.randrange(S), rnd.randrange(WALL_SHOWN), rnd.randrange(4, 9)
        k = rnd.choice([0.95, 0.97, 1.03])
        for y in range(max(0, cy - r), min(WALL_H, cy + r)):
            for x in range(max(0, cx - r), min(S, cx + r)):
                if (x - cx) ** 2 + (y - cy) ** 2 < r * r:
                    px[x, y] = tint(px[x, y], k)
    for _ in range(5):
        x, y = rnd.randrange(4, S - 1), rnd.randrange(4, WALL_SHOWN - 4)
        px[x, y] = tint(PLASTER, 0.78)
    # No head beam: the kit's cap timber lies along the wall's top already.
    head, mid, sill = (-1, -1), (25, 27), (WALL_SHOWN - 3, WALL_SHOWN - 1)
    # Shadow under the cap and the rail, and dirt splashed up from the footing.
    for x in range(S):
        for y in (head[1] + 1, mid[1] + 1):
            px[x, y] = tint(px[x, y], 0.8)
            px[x, y + 1] = tint(px[x, y + 1], 0.9)
        for y in range(sill[0] - 4, sill[0]):
            px[x, y] = tint(px[x, y], 0.97 - 0.03 * (y - sill[0] + 4))
    if variant == 1:
        _brace(img, (5, mid[0] - 1), (S - 3, head[1] + 2), seed)        # rising to the right, in the upper panel
    elif variant == 2:
        _brace(img, (5, mid[1] + 2), (S - 3, sill[0] - 1), seed)        # falling to the right, in the lower panel
    elif variant == 3:
        _beam(img, (15, mid[1], 17, sill[0]), seed + 7, vertical=True)  # a stud under the rail
    _beam(img, (0, mid[0], S - 1, mid[1]), seed + 2, vertical=False)
    _beam(img, (0, sill[0], S - 1, sill[1]), seed + 3, vertical=False)
    _beam(img, (0, 0, 3, WALL_H - 1), seed + 4, vertical=True)
    # Below what shows: footing-coloured, in case a short face shows more.
    for y in range(sill[1] + 1, WALL_H):
        for x in range(S):
            px[x, y] = tint(OAK_DARK, 0.9)
    return img


WALL_VARIANTS = 4


STONE = (166, 148, 122)
MORTAR = (198, 182, 150)


def wall_fieldstone(variant=0, seed=91):
    """
    The inn's ground storey: rubble fieldstone laid in rough courses, each stone rounded and shaded
    (lit at its top, dark under), set in pale lime mortar. A storey tall (32x64, once a level); the
    variants are the same wall laid differently, so a long run doesn't repeat.
    """
    rnd = random.Random(seed + variant * 37)
    img = noise(MORTAR, 5, seed + variant, size=(S, WALL_H))
    px = img.load()
    y = 0
    while y < WALL_H:
        h = rnd.randrange(6, 10)
        x = -rnd.randrange(0, 8)
        while x < S:
            w = rnd.randrange(7, 13)
            k = rnd.choice([0.86, 0.94, 1.0, 1.06, 1.12])
            hue = rnd.choice([(0, 0, 0), (6, 2, -4), (-4, -2, 4)])
            base = (STONE[0] * k + hue[0], STONE[1] * k + hue[1], STONE[2] * k + hue[2])
            cx, cy = x + w / 2, y + h / 2
            for yy in range(y, min(WALL_H, y + h)):
                for xx in range(max(0, x), min(S, x + w)):
                    # A rounded stone: inside an ellipse a pixel short of its cell, shaded from the top.
                    ex, ey = (xx + 0.5 - cx) / (w / 2 - 0.6), (yy + 0.5 - cy) / (h / 2 - 0.6)
                    if ex * ex + ey * ey > 1:
                        continue
                    shade = 1.12 - 0.3 * ((yy - y) / max(1, h - 1)) + rnd.uniform(-0.04, 0.04)
                    px[xx % S, yy] = tint(base, shade)
            x += w
        y += h
    return img


def quoin(seed=97):
    """Dressed corner stones: big squared blocks, alternately long and short, crisp edges."""
    rnd = random.Random(seed)
    img = noise(MORTAR, 4, seed)
    d = ImageDraw.Draw(img)
    for i, y in enumerate(range(0, S, 8)):
        x0, x1 = (0, S - 9) if i % 2 == 0 else (8, S - 1)
        base = tint((168, 158, 140), rnd.choice([0.95, 1.0, 1.05]))
        d.rectangle([x0, y, x1, y + 6], fill=base)
        d.line([(x0, y), (x1, y)], fill=tint(base, 1.14))
        d.line([(x0, y + 6), (x1, y + 6)], fill=tint(base, 0.78))
        for _ in range(6):
            img.putpixel((rnd.randrange(x0, x1 + 1), rnd.randrange(y + 1, y + 6)), tint(base, rnd.choice([0.9, 1.08])))
    return img


def gable_plaster():
    # The gable kit sprite: alpha-cut to a triangle, its high side on the right (as gable_boards).
    mask = Image.open(os.path.join(ROOT, "buildings", "gable_boards.png")).convert("RGBA").getchannel("A")
    img = noise(PLASTER, 4, 33)
    d = ImageDraw.Draw(img)
    beam = tint(OAK_DARK, 1.0)
    for x in (8, 20):                                  # studs
        d.rectangle([x, 0, x + 2, S - 1], fill=beam)
    d.rectangle([0, S - 4, S - 1, S - 1], fill=beam)   # tie beam
    d.line([(0, S - 1), (S - 1, 0)], fill=beam, width=3)  # the rafter along the slope
    img.putalpha(mask)
    return img


def gable_fill_plaster(seed=37):
    """Under a wide gable's triangle: limewash between upright studs, no rails (WorldMesher stacks it)."""
    rnd = random.Random(seed)
    img = noise(PLASTER, 3, seed)
    px = img.load()
    for _ in range(3):
        cx, cy, r = rnd.randrange(S), rnd.randrange(S), rnd.randrange(4, 8)
        for y in range(S):
            for x in range(S):
                if (x - cx) ** 2 + (y - cy) ** 2 < r * r:
                    px[x, y] = tint(px[x, y], 0.96)
    for x in (4, 5):
        for y in range(S):
            px[x, y] = tint(px[x, y], 0.86)  # the stud's shadow on the wash
    _beam(img, (0, 0, 3, S - 1), seed + 1, vertical=True)
    return img


def straw_thatch(seed):
    """
    Long straw laid in courses down the slope (tile v runs down it, u along the ridge). Each course's
    butt ends make a ragged edge, not a ruled line, and the courses are staggered and bunched, so
    cells side by side don't line up into stripes. The left and right edges wrap, for the same reason.
    """
    base = (214, 176, 100)
    rnd = random.Random(seed)
    img = noise(base, 5, seed)
    px = img.load()
    # Strands: short streaks down the slope, a few lighter or darker, wrapping across the tile.
    for x in range(S):
        y = rnd.randrange(4)
        while y < S:
            n = rnd.randrange(3, 7)
            k = rnd.choice([0.84, 0.92, 1.08, 1.14, 1.0])
            for i in range(n):
                px[x, (y + i) % S] = tint(base, k)
            y += n + rnd.randrange(1, 3)
    # One course a tile, at a height of its own: the butts' soft shadow under a ragged edge that
    # wanders up and down across the tile, not a ruled line.
    y0 = rnd.randrange(S)
    drift = 0
    for x in range(S):
        drift = max(-3, min(3, drift + rnd.choice([-1, 0, 0, 1])))
        edge = y0 + drift
        px[x, edge % S] = tint(px[x, edge % S], 0.8)
        px[x, (edge + 1) % S] = tint(px[x, (edge + 1) % S], 0.9)
        if rnd.random() < 0.35:
            px[x, (edge - 1) % S] = tint(px[x, (edge - 1) % S], 1.1)  # a butt end catching the light
    return img


# ---------------- Props ----------------

def fence_post():
    img = noise((118, 86, 56), 8, 41)
    d = ImageDraw.Draw(img)
    for x in (5, 14, 23):
        d.line([(x, 0), (x, S)], fill=(84, 60, 40, 255))
    return img


def fence_rail():
    base = (150, 116, 78)
    img = noise(base, 7, 43)
    d = ImageDraw.Draw(img)
    for y in (6, 17, 27):
        d.line([(0, y), (S, y + 1)], fill=tint(base, 0.74))
    return img


def crate_side():
    base = (172, 132, 86)
    img = noise(base, 6, 47)
    d = ImageDraw.Draw(img)
    edge = tint(base, 0.62)
    d.rectangle([0, 0, S - 1, S - 1], outline=edge, width=3)
    for y in (10, 21):
        d.line([(3, y), (S - 4, y)], fill=tint(base, 0.8))
    d.line([(3, S - 4), (S - 4, 3)], fill=edge, width=3)
    for x, y in ((1, 1), (S - 2, 1), (1, S - 2), (S - 2, S - 2)):  # nails
        d.point((x, y), fill=(150, 150, 156, 255))
    return img


def cabbage_top():
    img = noise((96, 150, 72), 6, 49)
    d = ImageDraw.Draw(img)
    for r, k in ((15, 0.7), (11, 0.86), (7, 1.06), (3, 1.2)):
        d.ellipse([16 - r, 16 - r, 16 + r, 16 + r], outline=tint((96, 150, 72), k), width=2)
    return img


def bridge_stone():
    base = (150, 146, 140)
    img = noise(base, 6, 53)
    d = ImageDraw.Draw(img)
    mortar = tint(base, 0.66)
    for row, y in enumerate(range(0, S, 8)):  # dressed slabs, courses staggered
        d.line([(0, y), (S, y)], fill=mortar)
        off = 0 if row % 2 == 0 else 8
        for x in range(off, S, 16):
            d.line([(x, y), (x, y + 8)], fill=mortar)
    return img


def bridge_parapet():
    """The parapet's faces: big squared blocks in courses of two, a shade darker than the deck."""
    base = (138, 134, 128)
    img = noise(base, 7, 54)
    d = ImageDraw.Draw(img)
    mortar = tint(base, 0.62)
    rnd = random.Random(55)
    for row, y in enumerate(range(0, S, 11)):
        d.line([(0, y), (S, y)], fill=mortar)
        off = 0 if row % 2 == 0 else 10
        for x in range(off, S + 20, 20):
            d.line([(x, y), (x, y + 11)], fill=mortar)
        for _ in range(3):  # a chipped arris here and there
            cx, cy = rnd.randrange(S), y + rnd.randrange(1, 10)
            img.putpixel((cx, min(S - 1, cy)), tint(base, 0.8))
    d.line([(0, 0), (S, 0)], fill=tint(base, 1.12))
    return img


def bridge_coping():
    """The coping along the parapet's top: long dressed slabs, pale and worn smooth."""
    base = (168, 164, 156)
    img = noise(base, 4, 56)
    d = ImageDraw.Draw(img)
    mortar = tint(base, 0.7)
    for x in range(0, S, 16):
        d.line([(x, 0), (x, S)], fill=mortar)
    d.line([(0, S - 1), (S, S - 1)], fill=tint(base, 0.8))
    return img


def bridge_pier():
    """Piers and arch rings: rough-faced blocks, darker and greened toward the water."""
    base = (120, 118, 112)
    img = noise(base, 9, 57)
    d = ImageDraw.Draw(img)
    mortar = tint(base, 0.55)
    for row, y in enumerate(range(0, S, 8)):
        d.line([(0, y), (S, y)], fill=mortar)
        off = 0 if row % 2 == 0 else 6
        for x in range(off, S + 12, 12):
            d.line([(x, y), (x, y + 8)], fill=mortar)
    rnd = random.Random(58)
    for y in range(S // 2, S):  # damp and moss low down
        for x in range(S):
            if rnd.random() < (y - S / 2) / S:
                r, g, b, a = img.getpixel((x, y))
                img.putpixel((x, y), (clamp(r * 0.8), clamp(g * 0.92), clamp(b * 0.75), a))
    return img


def hearth_soot():
    """A fireplace's back and its chimney's mouth: stone gone black with soot, warmest low down."""
    base = (74, 66, 60)
    img = noise(base, 6, 59)
    d = ImageDraw.Draw(img)
    mortar = tint(base, 0.6)
    for row, y in enumerate(range(0, S, 8)):
        d.line([(0, y), (S, y)], fill=mortar)
        off = 0 if row % 2 == 0 else 8
        for x in range(off, S, 16):
            d.line([(x, y), (x, y + 8)], fill=mortar)
    for y in range(S):  # soot thickest at the top; an ember glow at the foot
        for x in range(S):
            r, g, b, a = img.getpixel((x, y))
            k = 0.45 + 0.55 * (y / S)
            glow = max(0.0, (y - S * 0.7) / (S * 0.3))
            img.putpixel((x, y), (clamp(r * k + 60 * glow), clamp(g * k + 22 * glow), clamp(b * k), a))
    return img


def candle():
    """A tallow candle's side: creamy wax, drips down from the top."""
    base = (232, 218, 182)
    img = noise(base, 4, 60)
    d = ImageDraw.Draw(img)
    for x in range(2, S, 7):
        d.line([(x, 0), (x, 6 + (x * 5) % 9)], fill=tint(base, 1.08))
    return img


# ---------------- Icons and billboards ----------------

def well_icon():
    img = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(img)
    d.ellipse([4, 8, 28, 30], fill=(140, 138, 134, 255), outline=(70, 68, 66, 255))
    d.ellipse([9, 13, 23, 25], fill=(70, 120, 150, 255))
    d.rectangle([5, 2, 7, 20], fill=OAK_DARK + (255,))
    d.rectangle([25, 2, 27, 20], fill=OAK_DARK + (255,))
    d.polygon([(2, 4), (16, 0), (30, 4), (30, 6), (2, 6)], fill=(120, 80, 54, 255))
    return img


def fence_icon():
    img = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(img)
    for x in (4, 15, 26):
        d.rectangle([x, 8, x + 2, 30], fill=(118, 86, 56, 255), outline=(70, 50, 34, 255))
    for y in (13, 22):
        d.rectangle([1, y, 30, y + 2], fill=(150, 116, 78, 255), outline=(90, 66, 44, 255))
    return img


def gate():
    # A plank field gate, as a door leaf (32x64): rails and a brace, gaps between them see-through.
    img = Image.new("RGBA", (S, 64))
    d = ImageDraw.Draw(img)
    wood, dark = (150, 116, 78, 255), (84, 60, 40, 255)
    d.rectangle([1, 8, 4, 63], fill=wood, outline=dark)
    d.rectangle([27, 8, 30, 63], fill=wood, outline=dark)
    for y in (14, 30, 46):
        d.rectangle([1, y, 30, y + 4], fill=wood, outline=dark)
    d.line([(4, 50), (27, 18)], fill=dark, width=3)
    return img


def chibi_cabbages(bare):
    # Three heads in a row on the soil (bare: cut back to sprouts), in the 128px chibi style.
    img = Image.new("RGBA", (128, 128))
    d = ImageDraw.Draw(img)
    out = (36, 60, 30, 255)
    for cx, cy in ((34, 92), (64, 84), (94, 94)):
        if bare:
            for dx in (-6, 0, 6):
                d.ellipse([cx + dx - 5, cy - 4, cx + dx + 5, cy + 8], fill=(110, 170, 80, 255), outline=out, width=2)
            continue
        d.ellipse([cx - 26, cy - 18, cx + 26, cy + 20], fill=(84, 140, 64, 255), outline=out, width=3)  # outer leaves
        d.ellipse([cx - 18, cy - 16, cx + 18, cy + 14], fill=(128, 186, 96, 255), outline=out, width=2)  # the head
        d.arc([cx - 12, cy - 12, cx + 12, cy + 8], 200, 340, fill=(84, 140, 64, 255), width=3)
        d.ellipse([cx - 9, cy - 12, cx + 1, cy - 6], fill=(190, 226, 150, 255))  # the light
    return img


def cabbage_item():
    img = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(img)
    d.ellipse([4, 6, 28, 28], fill=(84, 140, 64, 255), outline=(36, 60, 30, 255))
    d.ellipse([9, 9, 23, 24], fill=(128, 186, 96, 255), outline=(36, 60, 30, 255))
    d.ellipse([11, 11, 16, 15], fill=(190, 226, 150, 255))
    return img




# Material colours (core.json and mining.json "materials"): the art for a piece built of each,
# so the game uses it instead of tinting the plain art.
WOODS = {"oak": (0.85, 0.68, 0.52), "pine": (1.0, 0.86, 0.62), "birch": (1.0, 0.97, 0.88)}
STONES = {"sandstone": (1.0, 0.86, 0.62), "limestone": (1.0, 0.98, 0.9), "granite": (1.0, 0.86, 0.84),
          "slate": (0.72, 0.78, 0.9), "basalt": (0.55, 0.53, 0.58)}


def by_material(img, color, only_dark=False):
    """Tinted as the material; only_dark leaves light pixels (the plaster between the timbers) alone."""
    out = img.copy()
    px = out.load()
    for y in range(out.size[1]):
        for x in range(out.size[0]):
            r, g, b, a = px[x, y]
            if a == 0 or (only_dark and (r + g + b) / 3 > 150):
                continue
            px[x, y] = (clamp(r * color[0] * 1.15), clamp(g * color[1] * 1.15), clamp(b * color[2] * 1.15), a)
    return out


def materials(img, name, kinds, only_dark=False):
    for k, c in kinds.items():
        save(by_material(img, c, only_dark), f"{name}_{k}")


def generate():
    save(road(61, ruts=False), "terrain/road")
    save(road(62, ruts=False), "terrain/road_v2")
    save(road(63, ruts=True), "terrain/road_ruts")
    save(tilled(71), "terrain/tilled")
    save(timber_dark(), "props/timber_dark")
    for v in range(WALL_VARIANTS):
        suffix = "" if v == 0 else f"_v{v + 1}"
        save(wall_timber_plaster(v), "buildings/wall_timber_plaster" + suffix)
        for k, c in WOODS.items():
            save(by_material(wall_timber_plaster(v), c, True), f"buildings/wall_timber_plaster_{k}{suffix}")
    for v in range(3):
        save(wall_fieldstone(v), "buildings/wall_fieldstone" + ("" if v == 0 else f"_v{v + 1}"))
    save(quoin(), "buildings/quoin")
    save(gable_plaster(), "buildings/gable_plaster")
    save(gable_fill_plaster(), "buildings/gable_fill_plaster")
    save(straw_thatch(81), "buildings/roof_thatch_straw")
    save(straw_thatch(82), "buildings/roof_thatch_straw_v2")
    save(straw_thatch(83), "buildings/roof_thatch_straw_v3")
    save(fence_post(), "props/fence_post")
    save(fence_rail(), "props/fence_rail")
    save(crate_side(), "props/crate_side")
    save(cabbage_top(), "props/cabbage_top")
    save(bridge_stone(), "buildings/bridge_stone")
    save(hearth_soot(), "props/hearth_soot")
    save(candle(), "props/candle")
    materials(bridge_stone(), "buildings/bridge_stone", STONES)
    for name, art in (("bridge_parapet", bridge_parapet), ("bridge_coping", bridge_coping), ("bridge_pier", bridge_pier)):
        save(art(), f"buildings/{name}")
        materials(art(), f"buildings/{name}", STONES)
    save(well_icon(), "buildings/well")
    save(fence_icon(), "buildings/fence")
    save(gate(), "buildings/gate")
    materials(gate(), "buildings/gate", WOODS)
    save(chibi_cabbages(bare=False), "things/cabbage")
    save(chibi_cabbages(bare=True), "things/cabbage_bare")
    save(cabbage_item(), "items/cabbage")
    print("village art: lanes, gardens, half-timbering, thatch, well, bridge, fences, crates, cabbages")


if __name__ == "__main__":
    generate()
