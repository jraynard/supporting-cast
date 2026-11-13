#!/usr/bin/env python3
"""The village's scenery (data/defs/village.json) and the half-timbered building kit
(buildings.json "timber_plaster", "timber_shingle"): lanes and garden soil, plaster and dark oak,
straw thatch, the well, the stone bridge, fences and gates, crates, cabbages and the pig.

Programmer art in the style of the rest: 32x32 tiles, 128px chibi plants, the shared world grade
(placeholder_nature.world_finish). Writes only the files listed here, so it's safe to re-run on its
own: python3 tools/village_art.py. gen_placeholder_art.py runs it too, after the Norse pass.
"""
import colorsys
import json
import os
import random
import shutil

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
PLASTER = (228, 218, 190)


def timber_dark(seed=5):
    img = noise(OAK_DARK, 9, seed)
    rnd = random.Random(seed)
    d = ImageDraw.Draw(img)
    for _ in range(7):  # grain
        y = rnd.randrange(S)
        x = rnd.randrange(S - 10)
        d.line([(x, y), (x + rnd.randrange(6, 14), y)], fill=tint(OAK_DARK, 0.72))
    return img


def wall_timber_plaster(seed=21):
    img = noise(PLASTER, 4, seed)
    d = ImageDraw.Draw(img)
    beam = tint(OAK_DARK, 1.0)
    shadow = tint(PLASTER, 0.82)
    d.rectangle([0, 0, 3, S - 1], fill=beam)          # posts at the cell's ends
    d.rectangle([S - 4, 0, S - 1, S - 1], fill=beam)
    d.rectangle([0, 0, S - 1, 2], fill=beam)           # head and mid rails
    d.rectangle([0, 15, S - 1, 17], fill=beam)
    d.rectangle([0, S - 3, S - 1, S - 1], fill=beam)   # sole plate
    d.line([(5, 14), (13, 4)], fill=beam, width=3)     # a brace in the upper panel
    for x in range(4, S - 4):                          # the panels' shadow under each rail
        img.putpixel((x, 3), shadow)
        img.putpixel((x, 18), shadow)
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


def straw_thatch(seed):
    base = (210, 172, 98)
    img = noise(base, 6, seed)
    rnd = random.Random(seed)
    d = ImageDraw.Draw(img)
    for x in range(0, S, 2):  # straw strokes
        for y in range(rnd.randrange(3), S, 6):
            d.line([(x, y), (x, y + 3)], fill=tint(base, rnd.choice([0.86, 1.1, 1.16])))
    for y in range(7, S, 8):  # courses: a shadowed band under each
        d.line([(0, y), (S, y)], fill=tint(base, 0.68))
        d.line([(0, y + 1), (S, y + 1)], fill=tint(base, 0.82))
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


MANE = {(70, 56, 56), (73, 58, 58), (49, 39, 41), (52, 34, 38)}
OUTLINE = (34, 18, 26, 255)


def shave(src, frames):
    """The boar without its bristly mane: the mane painted over in the body's grey, and the spikes
    cut off along the line of the back (the top edge with its narrow peaks opened away), re-outlined."""
    img = src.copy()
    px = img.load()
    for f in frames:
        x0, y0, w, h = f["x"], f["y"], f["w"], f["h"]
        upper = y0 + int(h * 0.45)  # the legs below share the mane's dark grey
        maned = [any(px[x, y][:3] in MANE for y in range(y0, upper)) for x in range(x0, x0 + w)]
        top = []
        for x in range(x0, x0 + w):
            ys = [y for y in range(y0, y0 + h) if px[x, y][3] > 0]
            top.append(ys[0] if ys else y0 + h)
        # Opening on the top edge: narrow upward spikes go, the broad curve of the back stays.
        r = 7
        lowered = [max(top[max(0, i - r):i + r + 1]) for i in range(w)]
        back = [min(lowered[max(0, i - r):i + r + 1]) for i in range(w)]
        for i in range(w):
            near = any(maned[max(0, i - 3):i + 4])
            if not near:
                continue
            x = x0 + i
            for y in range(y0, min(back[i], y0 + h)):
                px[x, y] = (0, 0, 0, 0)
            for y in range(back[i], min(back[i] + 2, y0 + h)):
                if px[x, y][3] > 0:
                    px[x, y] = OUTLINE
            for y in range(back[i] + 2, upper):
                c = px[x, y]
                if c[:3] in MANE or (y < back[i] + 18 and c[:3] == (104, 88, 84)):
                    px[x, y] = (120, 105, 99, 255)  # the mane, and the scalloped shadow it cast on the back
    return img


def pig():
    # The boar's sheet, grown pink, smooth and shaven: same frames and pivot (creatures/boar.anim.json).
    with open(os.path.join(ROOT, "creatures", "boar.anim.json")) as fh:
        frames = json.load(fh)["frames"]
    src = shave(Image.open(os.path.join(ROOT, "creatures", "boar.png")).convert("RGBA"), frames)
    out = Image.new("RGBA", src.size)
    px, po = src.load(), out.load()
    for y in range(src.size[1]):
        for x in range(src.size[0]):
            r, g, b, a = px[x, y]
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if a == 0 or v < 0.2 or (s < 0.12 and v > 0.85):
                po[x, y] = (r, g, b, a)  # outline, eyes and highlights stay
                continue
            nr, ng, nb = colorsys.hsv_to_rgb(0.97, 0.32 + s * 0.3, min(1.0, v * 1.35 + 0.08))
            po[x, y] = (clamp(nr * 255), clamp(ng * 255), clamp(nb * 255), a)
    out.save(os.path.join(ROOT, "creatures", "pig.png"))
    shutil.copyfile(os.path.join(ROOT, "creatures", "boar.anim.json"), os.path.join(ROOT, "creatures", "pig.anim.json"))


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
    save(wall_timber_plaster(), "buildings/wall_timber_plaster")
    materials(wall_timber_plaster(), "buildings/wall_timber_plaster", WOODS, only_dark=True)
    save(gable_plaster(), "buildings/gable_plaster")
    save(straw_thatch(81), "buildings/roof_thatch_straw")
    save(straw_thatch(82), "buildings/roof_thatch_straw_v2")
    save(straw_thatch(83), "buildings/roof_thatch_straw_v3")
    save(fence_post(), "props/fence_post")
    save(fence_rail(), "props/fence_rail")
    save(crate_side(), "props/crate_side")
    save(cabbage_top(), "props/cabbage_top")
    save(bridge_stone(), "buildings/bridge_stone")
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
    pig()
    print("village art: lanes, gardens, half-timbering, thatch, well, bridge, fences, crates, cabbages, pig")


if __name__ == "__main__":
    generate()
