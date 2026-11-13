"""Placeholder art for mining (docs/plans/mining-progression.md): kinds of rock, ore seams, ores,
bars and coins, and the copper/bronze/steel gear made from them.

Programmer art like the rest: rock is the rough-stone generator recoloured, ores are their host
rock with coloured nuggets, and metal gear is the iron sprites palette-swapped per metal.
Run on its own (python3 tools/placeholder_mining.py) or from gen_placeholder_art.py.
"""
import math
import os
import random

from PIL import Image, ImageDraw

from placeholder_ground import rock_tile
from norse_ground import periodic_noise
from placeholder_nature import save, shade, variant_name

S = 32
ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")

# Rock: base colour, and how strongly its side shows strata (slate splits in thin sheets).
ROCKS = {
    "sandstone": ((198, 166, 118), 5, 9),
    "limestone": ((188, 184, 168), 4, 8),
    "granite": ((156, 134, 134), 6, 11),
    "slate": ((84, 92, 108), 2, 4),
    "basalt": ((62, 60, 68), 5, 10),
}


def rock_side(seed, base, band_min, band_max):
    """Horizontal strata in the rock's colour: lit top edges, dark seams, a few cracks."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    mottle = periodic_noise(rnd)
    y = 0
    bands = []
    while y < S:
        h = rnd.randint(band_min, band_max)
        bands.append((y, min(S, y + h), rnd.uniform(0.88, 1.08), rnd.uniform(-4, 4)))
        y += h
    for y0, y1, k, tint in bands:
        c = (base[0] + tint, base[1] + tint * 0.8, base[2])
        for yy in range(y0, y1):
            for x in range(S):
                edge = 1.12 if yy == y0 else 0.72 if yy == y1 - 1 else 1.0
                px[x, yy] = shade(c, k * edge * (1 + 0.05 * mottle(x, yy) + rnd.uniform(-0.04, 0.04)))
    for _ in range(rnd.randint(2, 4)):
        y0, y1, _, _ = rnd.choice(bands)
        x = rnd.randrange(S)
        for yy in range(y0 + 1, y1 - 1):
            px[x % S, yy] = shade(base, 0.6)
            x += rnd.choice((0, 0, 1, -1))
    return img


def speckle(img, seed, colors, count):
    """Granite's crystals: single pixels of each colour."""
    rnd = random.Random(seed)
    px = img.load()
    for _ in range(count):
        px[rnd.randrange(S), rnd.randrange(S)] = rnd.choice(colors) + (255,)
    return img


def rock_textures():
    for n, (name, (base, bmin, bmax)) in enumerate(ROCKS.items()):
        for i in range(2):
            top = rock_tile(2000 + n * 10 + i, base)
            side = rock_side(2100 + n * 10 + i, base, bmin, bmax)
            if name == "granite":
                top = speckle(top, 2200 + i, [(214, 204, 200), (52, 46, 50), (196, 150, 140)], 26)
                side = speckle(side, 2210 + i, [(214, 204, 200), (52, 46, 50)], 18)
            save(top, variant_name(f"terrain/{name}", i))
            save(side, variant_name(f"terrain/{name}_side", i))


def recolor_stone(img, base):
    """Repaint the grey stone in a sprite in a rock's colour, keeping its shading; coloured pixels
    (a kiln's fire, wood, warm lime mortar) and transparency stay as they are.

    "Grey" is low saturation and not warm: the Nordic grade and the Norse metal ramps tint greys
    cool, so a fixed channel-spread test misses the light ones and leaves whole tools iron-coloured."""
    img = img.convert("RGBA")
    px = img.load()

    def grey(p):
        r, g, b = p[:3]
        hi = max(r, g, b)
        return hi > 0 and (hi - min(r, g, b)) / hi < 0.3 and b >= r - 6

    greys = [sum(px[x, y][:3]) / 3 for y in range(img.height) for x in range(img.width)
             if px[x, y][3] > 0 and grey(px[x, y])]
    if not greys:
        return img
    mean = sum(greys) / len(greys)
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0 or not grey((r, g, b)):
                continue
            k = (r + g + b) / 3 / mean
            px[x, y] = tuple(max(0, min(255, int(c * k))) for c in base) + (a,)
    return img


# Sprites of things made of stone that get one per kind of rock (MaterialArt looks for name_<rock>).
STONE_THINGS = ["buildings/wall_stone", "buildings/wall_cut_stone", "buildings/wall_mortared", "buildings/kiln", "buildings/forge", "items/cut_stone"]


def mortared_wall():
    """Cut stone with pale lime mortar in the joints (the grey stone is what recolours per rock)."""
    wall = Image.open(os.path.join(ROOT, "buildings/wall_cut_stone.png")).convert("RGBA")
    px = wall.load()
    for y in range(wall.height):
        for x in range(wall.width):
            r, g, b, a = px[x, y]
            if a and r < 110:
                px[x, y] = (226, 214, 196, a)  # warm enough to stay put when the stone is recoloured
    wall.save(os.path.join(ROOT, "buildings/wall_mortared.png"))


def stone_variants():
    for thing in STONE_THINGS:
        src = Image.open(os.path.join(ROOT, thing + ".png"))
        for rock, (base, _, _) in ROCKS.items():
            out = recolor_stone(src.copy(), base)
            if rock == "granite":
                out = speckle_opaque(out, 3000 + STONE_THINGS.index(thing), [(214, 204, 200), (52, 46, 50)], 14)
            out.save(os.path.join(ROOT, f"{thing}_{rock}.png"))


def speckle_opaque(img, seed, colors, count):
    rnd = random.Random(seed)
    px = img.load()
    for _ in range(count):
        x, y = rnd.randrange(img.width), rnd.randrange(img.height)
        if px[x, y][3] > 0:
            px[x, y] = rnd.choice(colors) + (255,)
    return img


# Ore seams: a host rock with nuggets. (name, host rock colour, nugget fill, nugget rim)
ORES = {
    "copper_ore": ((176, 170, 156), (70, 160, 120), (196, 112, 64)),
    "tin_ore": ((150, 132, 132), (58, 52, 56), (200, 196, 190)),
    "coal": ((150, 138, 112), (30, 28, 30), (64, 60, 64)),
    "silver_ore": ((150, 134, 134), (214, 218, 226), (120, 124, 134)),
    "gold_ore": ((150, 134, 134), (236, 194, 70), (150, 108, 24)),
    "ember_crystal": ((62, 60, 68), (255, 140, 60), (180, 60, 30)),
}

# Metals: (fill, light edge, dark edge)
METALS = {
    "copper": ((196, 112, 64), (236, 160, 110), (120, 62, 34)),
    "bronze": ((184, 140, 70), (226, 188, 112), (110, 80, 36)),
    "iron": ((150, 154, 164), (196, 200, 210), (84, 86, 96)),
    "steel": ((132, 146, 168), (190, 204, 224), (70, 80, 100)),
    "silver": ((200, 204, 212), (240, 242, 248), (120, 124, 134)),
    "gold": ((222, 180, 60), (252, 226, 120), (140, 102, 24)),
}


def ore_seam(seed, host, fill, rim, coal=False):
    img = rock_tile(seed, host)
    d = ImageDraw.Draw(img)
    rnd = random.Random(seed)
    if coal:  # black bands rather than nuggets
        for y in (rnd.randint(4, 9), rnd.randint(16, 24)):
            for x in range(S):
                h = 2 + (1 if rnd.random() < 0.4 else 0)
                d.line([(x, y + rnd.choice((0, 0, 1))), (x, y + h)], fill=fill + (255,))
        return img
    for _ in range(9):
        x, y = rnd.randint(2, 28), rnd.randint(2, 28)
        d.ellipse([x - 2, y - 1, x + 2, y + 2], fill=fill + (255,), outline=rim + (255,))
    return img


def ore_item(seed, fill, rim, base=(118, 110, 104)):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(5, 22), (9, 11), (17, 7), (26, 12), (28, 22), (18, 28)], fill=base + (255,), outline=tuple(int(c * 0.6) for c in base) + (255,))
    rnd = random.Random(seed)
    for _ in range(5):
        x, y = rnd.randint(10, 22), rnd.randint(12, 24)
        d.ellipse([x - 2, y - 2, x + 2, y + 1], fill=fill + (255,), outline=rim + (255,))
    return img


def bar_item(metal):
    fill, light, dark = METALS[metal]
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for y in (18, 10):
        d.polygon([(4, y + 8), (8, y), (26, y), (29, y + 8)], fill=fill + (255,), outline=dark + (255,))
        d.line([(9, y + 2), (25, y + 2)], fill=light + (255,))
    return img


def coins(metal):
    fill, light, dark = METALS[metal]
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for i, (x, y) in enumerate(((10, 22), (20, 22), (15, 18), (15, 13), (15, 8))):
        d.ellipse([x - 6, y - 3, x + 6, y + 3], fill=fill + (255,), outline=dark + (255,))
        d.line([(x - 3, y - 1), (x + 2, y - 1)], fill=light + (255,))
    return img


def ring():
    fill, light, dark = METALS["silver"]
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([8, 10, 24, 26], outline=dark + (255,), width=4)
    d.ellipse([9, 11, 23, 25], outline=fill + (255,), width=2)
    d.polygon([(13, 8), (16, 4), (19, 8), (16, 12)], fill=(120, 200, 230, 255), outline=(40, 90, 120, 255))
    return img


def torc():
    fill, light, dark = METALS["gold"]
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.arc([4, 6, 28, 30], 200, 340, fill=dark + (255,), width=5)
    d.arc([4, 6, 28, 30], 200, 340, fill=fill + (255,), width=3)
    for x, y in ((5, 22), (27, 22)):
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=fill + (255,), outline=dark + (255,))
    return img


def mint_icon():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([3, 14, 28, 22], fill=(150, 108, 66, 255), outline=(84, 56, 32, 255))
    d.rectangle([11, 8, 20, 14], fill=(150, 150, 156, 255), outline=(92, 92, 98, 255))
    d.ellipse([22, 6, 29, 11], fill=METALS["gold"][0] + (255,), outline=METALS["gold"][2] + (255,))
    for x in (5, 25):
        d.rectangle([x, 22, x + 2, 29], fill=(120, 84, 50, 255))
    return img


def ember_crystal():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for pts, fill in (([(9, 26), (12, 10), (16, 6), (19, 12), (17, 26)], (240, 110, 40)),
                      ([(17, 26), (20, 14), (24, 12), (25, 26)], (200, 70, 30)),
                      ([(5, 26), (7, 18), (10, 17), (11, 26)], (220, 90, 36))):
        d.polygon(pts, fill=fill + (255,), outline=(110, 34, 18, 255))
    d.line([(14, 10), (15, 22)], fill=(255, 210, 120, 255))
    return img


def lime():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([5, 14, 27, 28], fill=(232, 230, 220, 255), outline=(170, 166, 154, 255))
    for x, y in ((11, 18), (19, 20), (15, 23)):
        d.point((x, y), fill=(200, 196, 186, 255))
    return img


def bellows():
    """Two boards hinged at a nozzle, with pleated leather between."""
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(3, 8), (21, 12), (21, 20), (3, 24)], fill=(150, 108, 66, 255), outline=(84, 56, 32, 255))
    for x in range(6, 20, 3):
        d.line([(x, 11), (x, 21)], fill=(110, 74, 42, 255))
    d.polygon([(21, 13), (29, 15), (29, 17), (21, 19)], fill=(150, 154, 164, 255), outline=(84, 86, 96, 255))
    d.line([(1, 6), (5, 10)], fill=(132, 92, 54, 255), width=2)
    d.line([(1, 26), (5, 22)], fill=(132, 92, 54, 255), width=2)
    return img


def coal_item():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for x, y in ((7, 19), (16, 21), (24, 18), (11, 11), (20, 10)):
        d.polygon([(x - 5, y + 2), (x - 3, y - 3), (x + 2, y - 4), (x + 5, y), (x + 3, y + 4), (x - 2, y + 4)],
                  fill=(36, 34, 38, 255), outline=(14, 12, 16, 255))
        d.point((x - 1, y - 2), fill=(110, 108, 120, 255))
    return img


def ores_and_metals():
    for n, (name, (host, fill, rim)) in enumerate(ORES.items()):
        coal = name == "coal"
        save(ore_seam(2300 + n * 2, host, fill, rim, coal), f"terrain/{name}")
        side = rock_side(2301 + n * 2, host, 4, 9)
        d = ImageDraw.Draw(side)
        rnd = random.Random(2350 + n)
        for _ in range(7 if not coal else 0):
            x, y = rnd.randint(2, 28), rnd.randint(2, 28)
            d.ellipse([x - 2, y - 1, x + 2, y + 2], fill=fill + (255,), outline=rim + (255,))
        if coal:
            for y in (8, 20):
                d.rectangle([0, y, S, y + 3], fill=fill + (255,))
        save(side, f"terrain/{name}_side")
    save(ore_item(2400, (70, 160, 120), (196, 112, 64), (150, 144, 132)), "items/copper_ore")
    save(ore_item(2401, (58, 52, 56), (200, 196, 190), (132, 118, 118)), "items/tin_ore")
    save(coal_item(), "items/coal")
    save(ore_item(2402, (214, 218, 226), (120, 124, 134), (132, 118, 118)), "items/silver_ore")
    save(ore_item(2403, (236, 194, 70), (150, 108, 24), (214, 210, 200)), "items/gold_ore")
    for metal in ("silver", "gold"):
        save(coins(metal), f"items/{metal}_coin")
    save(ring(), "items/silver_ring")
    save(torc(), "items/gold_torc")
    save(mint_icon(), "buildings/mint")
    save(ember_crystal(), "items/ember_crystal")
    save(lime(), "items/lime")

    for metal in ("copper", "bronze", "steel", "silver", "gold"):
        save(bar_item(metal), f"items/{metal}_bar")
    save(bellows(), "buildings/bellows")
    # Built of wood: a variant per wood (core.json colours), the metal nozzle left alone.
    for wood, tint in (("oak", (0.85, 0.68, 0.52)), ("pine", (1.0, 0.86, 0.62)), ("birch", (1.0, 0.97, 0.88))):
        img = Image.open(os.path.join(ROOT, "buildings/bellows.png")).convert("RGBA")
        px = img.load()
        for y in range(img.height):
            for x in range(img.width):
                r, g, b, a = px[x, y]
                if a and max(r, g, b) - min(r, g, b) >= 28:
                    px[x, y] = (int(r * tint[0]), int(g * tint[1]), int(b * tint[2]), a)
        img.save(os.path.join(ROOT, f"buildings/bellows_{wood}.png"))


def recolor_metal(img, metal):
    """Repaint an iron sprite's grey metal in another metal, keeping its shading; wood and leather stay."""
    return recolor_stone(img, METALS[metal][0])


def graded_metal(img, metal):
    """recolor_metal for a source sprite that is already finished (graded, or Norse art): the metal goes
    in already graded, so the result is saved as it is (save_raw) and the wood isn't graded twice."""
    from norse_art import nordic_finish
    c = nordic_finish(Image.new("RGBA", (1, 1), METALS[metal][0] + (255,))).getpixel((0, 0))[:3]
    return recolor_stone(img, c)


def save_raw(img, name):
    img.save(os.path.join(ROOT, name + ".png"))


def spear(head, rim, shaft=(132, 92, 54)):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([(5, 28), (23, 10)], fill=(84, 56, 32, 255), width=4)
    d.line([(5, 28), (23, 10)], fill=shaft + (255,), width=2)
    d.polygon([(21, 9), (28, 3), (25, 12)], fill=head + (255,), outline=rim + (255,))
    return img


def knife(blade, rim):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([(7, 25), (13, 19)], fill=(84, 56, 32, 255), width=4)
    d.polygon([(12, 18), (25, 6), (16, 20)], fill=blade + (255,), outline=rim + (255,))
    d.line([(10, 17), (15, 22)], fill=(70, 62, 56, 255), width=2)
    return img


def mace(head, rim, light):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([(6, 27), (18, 15)], fill=(84, 56, 32, 255), width=4)
    d.line([(6, 27), (18, 15)], fill=(132, 92, 54, 255), width=2)
    d.ellipse([15, 5, 27, 17], fill=head + (255,), outline=rim + (255,))
    for (x, y) in ((21, 3), (29, 11), (21, 19), (13, 11)):
        d.polygon([(x - 2, y), (x, y - 2), (x + 2, y), (x, y + 2)], fill=rim + (255,))
    d.point((18, 8), fill=light + (255,))
    return img


def gear():
    load = lambda name: Image.open(os.path.join(ROOT, name + ".png")).convert("RGBA")
    for metal in ("copper", "bronze", "steel"):
        for tool in ("axe", "pick", "hammer"):
            save_raw(graded_metal(load(f"items/{tool}"), metal), f"items/{metal}_{tool}")
    save_raw(graded_metal(load("items/sword"), "bronze"), "items/bronze_sword")
    save_raw(graded_metal(load("items/sword"), "steel"), "items/steel_sword")
    save_raw(graded_metal(load("items/iron_helm"), "bronze"), "items/bronze_helm")
    save_raw(graded_metal(load("items/chain_shirt"), "bronze"), "items/bronze_cuirass")
    save_raw(graded_metal(load("items/iron_helm"), "steel"), "items/steel_sallet")
    save_raw(graded_metal(load("items/chain_shirt"), "steel"), "items/steel_plate")
    studded = load("items/leather_jerkin")
    px = studded.load()
    for (x, y) in ((11, 12), (16, 12), (21, 12), (11, 18), (16, 18), (21, 18), (13, 24), (19, 24)):
        if px[x, y][3] > 0:
            px[x, y] = METALS["copper"][1] + (255,)
    save_raw(studded, "items/studded_jerkin")
    stone = ((150, 146, 140), (92, 88, 84))
    save(knife(*stone), "items/stone_knife")
    save(spear(*stone), "items/stone_spear")
    for metal in ("copper", "bronze", "iron", "steel"):
        fill, light, dark = METALS[metal]
        if metal != "steel":
            save(spear(fill, dark), f"items/{metal}_spear")
        if metal in ("bronze", "iron", "steel"):
            save(mace(fill, dark, light), f"items/{metal}_mace")
    save(knife(METALS["copper"][0], METALS["copper"][2]), "items/copper_knife")


def generate():
    rock_textures()
    mortared_wall()
    stone_variants()
    ores_and_metals()
    gear()   # (the worn layers are chibi_people's now)


if __name__ == "__main__":
    generate()
