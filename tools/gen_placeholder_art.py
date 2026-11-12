#!/usr/bin/env python3
"""Generate simple placeholder sprites (32x32 tiles, animated creature sheets) into data/textures.

Programmer art only: deterministic noise and basic shapes so the prototype has something
readable on screen. Replace any PNG with real art of the same name and it will be picked up.
Re-run with: python3 tools/gen_placeholder_art.py
"""
import math
import os
import random
from PIL import Image, ImageDraw

import placeholder_ground
import placeholder_nature
import placeholder_wood
import placeholder_sheets
import placeholder_props
import norse_dragon
import chibi_dragon
import norse_ground
import norse_items
import norse_nature
import norse_people
import norse_roofs
import norse_fx
import norse_stone
import norse_glow
import norse_buildings
import norse_smithy
import inn_art
import healer_art
import tinker_art
import norse_gear
import chibi_people
import chibi_animals
import chibi_nature
import norse_plants
import norse_ui
import placeholder_mining
import placeholder_dragoncare
import placeholder_roofs
import village_art

S = 32
ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")


def save(img, name):
    path = os.path.join(ROOT, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # UI marks and effects are white or grey art the game tints: keep them neutral.
    (img if name.startswith(("ui/", "fx/")) else placeholder_nature.world_finish(img)).save(path)


def noise_tile(base, var, seed, speckle=None):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        for x in range(S):
            d = rnd.randint(-var, var)
            px[x, y] = tuple(max(0, min(255, c + d)) for c in base) + (255,)
    if speckle:
        col, count = speckle
        for _ in range(count):
            px[rnd.randrange(S), rnd.randrange(S)] = col + (255,)
    return img


def blank():
    return Image.new("RGBA", (S, S), (0, 0, 0, 0))


def terrain():
    # All ground (grass, dirt, stone, sand, water, slopes) is in placeholder_nature.py and
    # placeholder_ground.py; kept so the call order below stays readable.
    pass


def things():
    # Trees, bushes, boulders, dead trees, wood and stone items: see placeholder_nature.py,
    # placeholder_wood.py and placeholder_ground.py.
    # Weapons
    img = blank(); d = ImageDraw.Draw(img)
    d.arc([6, 3, 26, 29], 300, 60, fill=(120, 80, 40, 255), width=3)
    d.line([(20, 5), (20, 27)], fill=(230, 225, 210, 255), width=1)
    save(img, "items/bow")
    img = blank(); d = ImageDraw.Draw(img)
    d.line([(9, 28), (22, 6)], fill=(120, 82, 46, 255), width=3)
    d.polygon([(17, 5), (27, 8), (25, 16), (19, 12)], fill=(170, 175, 185, 255), outline=(90, 95, 105, 255))
    save(img, "items/axe")
    img = blank(); d = ImageDraw.Draw(img)
    d.line([(9, 28), (19, 12)], fill=(110, 76, 42, 255), width=3)
    d.ellipse([15, 3, 27, 15], fill=(100, 68, 38, 255), outline=(60, 40, 22, 255))
    for (x, y) in ((16, 5), (25, 6), (26, 13), (17, 14)):
        d.point((x, y), fill=(210, 210, 215, 255))
    save(img, "items/club")
    img = blank(); d = ImageDraw.Draw(img)
    d.line([(10, 26), (24, 6)], fill=(200, 205, 215, 255), width=3)
    d.line([(8, 21), (15, 26)], fill=(150, 120, 50, 255), width=2)
    d.line([(7, 29), (10, 26)], fill=(90, 60, 30, 255), width=2)
    save(img, "items/sword")
    img = blank(); d = ImageDraw.Draw(img)
    for (x, y) in ((10, 12), (17, 15), (13, 19), (19, 9)):
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(180, 30, 60, 255))
    save(img, "items/berries")
    img = blank(); d = ImageDraw.Draw(img)
    d.ellipse([6, 10, 26, 24], fill=(170, 60, 60, 255), outline=(110, 30, 30, 255))
    d.ellipse([10, 13, 16, 18], fill=(220, 170, 160, 255))
    save(img, "items/meat")
    img = blank(); d = ImageDraw.Draw(img)
    for dy, body in ((0, (120, 160, 190, 255)), (6, (100, 140, 170, 255))):  # two fish, one atop the other
        d.ellipse([5, 9 + dy, 21, 17 + dy], fill=body, outline=(50, 70, 90, 255))
        d.polygon([(20, 13 + dy), (27, 8 + dy), (27, 18 + dy)], fill=body, outline=(50, 70, 90, 255))
        d.point((9, 12 + dy), fill=(20, 20, 30, 255))
    save(img, "items/fish")


def crafting():
    """Crafted materials and their stations (data/defs/workshop.json)."""
    # Raw hide: an irregular pelt, fur side up.
    img = blank(); d = ImageDraw.Draw(img)
    d.polygon([(6, 8), (12, 4), (20, 5), (27, 9), (26, 18), (28, 25), (19, 28), (12, 27), (4, 24), (6, 16)],
              fill=(150, 108, 72, 255), outline=(92, 62, 38, 255))
    rnd = random.Random(31)
    for _ in range(40):
        x, y = rnd.randint(8, 24), rnd.randint(8, 24)
        d.point([(x, y)], fill=(122, 86, 56, 255))
    save(img, "items/hide")
    # Leather: a neat rolled sheet.
    img = blank(); d = ImageDraw.Draw(img)
    d.rounded_rectangle([4, 10, 28, 24], radius=6, fill=(122, 78, 44, 255), outline=(72, 44, 24, 255))
    d.ellipse([22, 10, 30, 24], fill=(148, 98, 58, 255), outline=(72, 44, 24, 255))
    d.arc([24, 13, 28, 21], 90, 270, fill=(96, 60, 32, 255))
    d.line([(6, 14), (20, 14)], fill=(150, 104, 64, 255))
    save(img, "items/leather")
    # A meal: a wooden bowl of stew.
    img = blank(); d = ImageDraw.Draw(img)
    d.chord([4, 6, 28, 30], 0, 180, fill=(132, 92, 54, 255), outline=(80, 52, 28, 255))
    d.ellipse([5, 13, 27, 21], fill=(170, 96, 52, 255), outline=(80, 52, 28, 255))
    for x, y in ((10, 16), (16, 15), (21, 17), (14, 18)):
        d.ellipse([x - 1, y - 1, x + 1, y + 1], fill=(222, 170, 90, 255))
    d.line([(17, 4), (15, 10)], fill=(230, 230, 230, 160)); d.line([(12, 5), (11, 10)], fill=(230, 230, 230, 140))
    save(img, "items/meal")
    img = noise_tile((170, 96, 52), 10, 33, ((222, 170, 90), 40))
    save(img, "props/meal_top")
    img = noise_tile((150, 108, 72), 8, 34, ((118, 82, 52), 60))
    save(img, "props/hide")
    # Leather pack: a bigger bag with two buckled straps.
    img = blank(); d = ImageDraw.Draw(img)
    d.arc([8, 1, 24, 15], 180, 360, fill=(84, 52, 28, 255), width=2)
    d.rounded_rectangle([4, 7, 28, 30], radius=4, fill=(122, 78, 44, 255), outline=(66, 40, 20, 255))
    d.rectangle([4, 7, 28, 15], fill=(104, 64, 34, 255), outline=(66, 40, 20, 255))
    for x in (10, 21):
        d.rectangle([x, 13, x + 2, 24], fill=(84, 52, 28, 255))
        d.rectangle([x - 1, 17, x + 3, 19], fill=(200, 170, 110, 255))
    save(img, "items/leather_pack")
    # Tanning rack icon: a hide stretched in a frame.
    img = blank(); d = ImageDraw.Draw(img)
    d.rectangle([4, 3, 27, 29], outline=(110, 76, 44, 255), width=2)
    d.polygon([(9, 8), (16, 6), (23, 8), (22, 18), (24, 25), (16, 26), (8, 25), (10, 17)], fill=(170, 124, 82, 255), outline=(110, 76, 44, 255))
    for (a, b) in (((6, 5), (9, 8)), ((25, 5), (23, 8)), ((6, 27), (8, 25)), ((25, 27), (24, 25))):
        d.line([a, b], fill=(220, 210, 180, 255))
    save(img, "buildings/tanning_rack")


def industry():
    """Kiln, forge and mason's materials (data/defs/workshop.json) and the clay and ore they come from."""
    # Terrain: a clay bank and an iron-ore seam, top and side.
    save(noise_tile((168, 112, 84), 10, 41, ((142, 92, 68), 50)), "terrain/clay")
    side = noise_tile((150, 98, 72), 10, 42, ((122, 78, 58), 60))
    d = ImageDraw.Draw(side)
    for y in (9, 20):
        d.line([(0, y), (S, y + 1)], fill=(128, 84, 62, 255))
    save(side, "terrain/clay_side")
    for name, seed in (("terrain/iron_ore", 43), ("terrain/iron_ore_side", 44)):
        img = noise_tile((118, 112, 112), 8, seed)
        d = ImageDraw.Draw(img)
        rnd = random.Random(seed)
        for _ in range(9):
            x, y = rnd.randint(2, 28), rnd.randint(2, 28)
            d.ellipse([x - 2, y - 1, x + 2, y + 2], fill=(156, 72, 44, 255), outline=(112, 50, 32, 255))
        save(img, name)
    # Items.
    img = blank(); d = ImageDraw.Draw(img)
    for x, y in ((6, 18), (14, 20), (22, 17), (10, 12), (18, 11)):
        d.rounded_rectangle([x - 4, y - 3, x + 4, y + 3], radius=2, fill=(40, 38, 40, 255), outline=(18, 16, 18, 255))
        d.line([(x - 2, y - 1), (x + 2, y - 1)], fill=(80, 76, 80, 255))
    save(img, "items/charcoal")
    img = blank(); d = ImageDraw.Draw(img)
    d.ellipse([5, 12, 27, 28], fill=(176, 116, 86, 255), outline=(118, 74, 54, 255))
    d.ellipse([10, 8, 22, 18], fill=(186, 126, 94, 255), outline=(118, 74, 54, 255))
    save(img, "items/clay")
    img = blank(); d = ImageDraw.Draw(img)
    for row, y in enumerate((20, 13, 6)):
        for x in ((3, 16) if row % 2 == 0 else (9,)):
            d.rectangle([x, y, x + 12, y + 6], fill=(164, 70, 48, 255), outline=(102, 40, 28, 255))
    save(img, "items/bricks")
    img = blank(); d = ImageDraw.Draw(img)
    for x, y in ((4, 16), (16, 16), (10, 6)):
        d.rectangle([x, y, x + 11, y + 10], fill=(150, 150, 156, 255), outline=(92, 92, 98, 255))
        d.line([(x + 1, y + 1), (x + 10, y + 1)], fill=(182, 182, 188, 255))
    save(img, "items/cut_stone")
    img = blank(); d = ImageDraw.Draw(img)
    d.polygon([(5, 22), (9, 11), (17, 7), (26, 12), (28, 22), (18, 28)], fill=(112, 104, 104, 255), outline=(70, 64, 64, 255))
    for x, y in ((12, 16), (19, 13), (21, 21), (14, 23)):
        d.ellipse([x - 2, y - 2, x + 2, y + 1], fill=(156, 72, 44, 255))
    save(img, "items/iron_ore")
    img = blank(); d = ImageDraw.Draw(img)
    for y in (18, 10):
        d.polygon([(4, y + 8), (8, y), (26, y), (29, y + 8)], fill=(124, 128, 138, 255), outline=(70, 72, 80, 255))
        d.line([(9, y + 2), (25, y + 2)], fill=(176, 180, 190, 255))
    save(img, "items/iron_bar")
    # Stations and walls (walls and the kiln/forge are blocks: one texture on every face).
    img = noise_tile((132, 128, 124), 6, 45)
    d = ImageDraw.Draw(img)
    d.chord([6, 12, 26, 36], 180, 360, fill=(40, 28, 24, 255))
    d.chord([9, 17, 23, 36], 180, 360, fill=(240, 140, 50, 255))
    d.rectangle([0, 0, S - 1, S - 1], outline=(84, 80, 78, 255))
    save(img, "buildings/kiln")
    img = noise_tile((150, 66, 46), 8, 46)
    d = ImageDraw.Draw(img)
    for y in range(0, S, 6):
        d.line([(0, y), (S, y)], fill=(96, 40, 28, 255))
    d.rectangle([8, 14, 24, 26], fill=(40, 22, 18, 255))
    d.rectangle([10, 17, 22, 26], fill=(255, 170, 60, 255))
    d.rectangle([0, 0, S - 1, S - 1], outline=(96, 40, 28, 255))
    save(img, "buildings/forge")
    img = blank(); d = ImageDraw.Draw(img)
    d.rectangle([2, 12, 29, 17], fill=(150, 150, 156, 255), outline=(92, 92, 98, 255))
    for x in (4, 24):
        d.rectangle([x, 18, x + 4, 29], fill=(128, 128, 134, 255), outline=(92, 92, 98, 255))
    d.rectangle([12, 6, 19, 12], fill=(170, 170, 176, 255), outline=(92, 92, 98, 255))
    d.line([(22, 4), (27, 10)], fill=(120, 84, 50, 255), width=2)
    save(img, "buildings/mason_bench")
    wall = noise_tile((158, 70, 50), 8, 47)
    d = ImageDraw.Draw(wall)
    for row, y in enumerate(range(0, S, 6)):
        d.line([(0, y), (S, y)], fill=(210, 190, 170, 255))
        for x in range(0 if row % 2 == 0 else 6, S, 12):
            d.line([(x, y), (x, y + 6)], fill=(210, 190, 170, 255))
    save(wall, "buildings/wall_brick")
    wall = noise_tile((146, 146, 152), 5, 48)
    d = ImageDraw.Draw(wall)
    for row, y in enumerate(range(0, S, 11)):
        d.line([(0, y), (S, y)], fill=(96, 96, 102, 255))
        for x in range(0 if row % 2 == 0 else 8, S, 16):
            d.line([(x, y), (x, y + 11)], fill=(96, 96, 102, 255))
    save(wall, "buildings/wall_cut_stone")


def tools_and_scales():
    """Tools (data/defs/workshop.json "tool") and dragon-scale gear."""
    wood, wood_dark, iron, iron_dark = (140, 98, 58, 255), (96, 64, 38, 255), (150, 154, 164, 255), (84, 86, 96, 255)
    img = blank(); d = ImageDraw.Draw(img)
    d.line([(9, 27), (19, 13)], fill=wood, width=3)
    d.rectangle([14, 5, 28, 12], fill=iron, outline=iron_dark)
    save(img, "items/hammer")
    img = blank(); d = ImageDraw.Draw(img)
    d.line([(6, 28), (19, 9)], fill=iron, width=2); d.line([(12, 28), (19, 9)], fill=iron, width=2)
    d.line([(19, 9), (25, 4)], fill=iron_dark, width=2); d.line([(19, 9), (27, 8)], fill=iron_dark, width=2)
    save(img, "items/tongs")
    img = blank(); d = ImageDraw.Draw(img)
    d.polygon([(4, 18), (24, 10), (26, 15), (6, 23)], fill=iron, outline=iron_dark)
    for x in range(7, 24, 3):
        d.point([(x, 22 - (x - 7) * 8 // 17)], fill=iron_dark)
    d.rounded_rectangle([22, 8, 30, 18], radius=2, fill=wood, outline=wood_dark)
    save(img, "items/saw")
    img = blank(); d = ImageDraw.Draw(img)
    d.line([(8, 26), (17, 14)], fill=wood, width=4)
    d.polygon([(16, 13), (24, 4), (26, 6), (19, 16)], fill=iron, outline=iron_dark)
    save(img, "items/chisel")
    img = blank(); d = ImageDraw.Draw(img)
    d.line([(8, 28), (18, 10)], fill=wood, width=3)
    d.arc([5, 3, 31, 21], 200, 340, fill=iron, width=3)
    save(img, "items/pick")
    scale, scale_dark, scale_light = (176, 52, 40, 255), (110, 28, 22, 255), (232, 120, 80, 255)
    img = blank(); d = ImageDraw.Draw(img)
    for x, y in ((10, 18), (20, 18), (15, 10)):
        d.polygon([(x - 6, y - 4), (x + 6, y - 4), (x + 6, y + 2), (x, y + 8), (x - 6, y + 2)], fill=scale, outline=scale_dark)
        d.line([(x - 4, y - 2), (x + 4, y - 2)], fill=scale_light)
    save(img, "items/dragon_scale")
    img = blank(); d = ImageDraw.Draw(img)
    d.chord([5, 6, 27, 30], 180, 360, fill=scale, outline=scale_dark)
    d.rectangle([5, 17, 27, 22], fill=(110, 72, 40, 255), outline=(70, 44, 24, 255))
    for x in range(8, 26, 5):
        d.arc([x - 3, 9, x + 3, 15], 0, 180, fill=scale_light)
    save(img, "items/scale_helm")
    img = blank(); d = ImageDraw.Draw(img)
    d.polygon([(8, 4), (24, 4), (29, 10), (25, 12), (25, 29), (7, 29), (7, 12), (3, 10)], fill=scale, outline=scale_dark)
    for y in range(8, 28, 4):
        for x in range(9 + (y // 4 % 2) * 2, 24, 4):
            d.arc([x - 2, y - 2, x + 2, y + 2], 0, 180, fill=scale_light)
    save(img, "items/scale_mail")


def forage_and_tools():
    """Emberroot, the dragon cake, and the tool tiers (stone, iron in tools_and_scales, dragonforged)."""
    leaf, leaf_dark, root, root_dark = (150, 40, 34, 255), (96, 24, 22, 255), (196, 84, 52, 255), (120, 46, 30, 255)
    for name, bare in (("things/emberroot", False), ("things/emberroot_bare", True)):
        img = blank(); d = ImageDraw.Draw(img)
        d.ellipse([9, 24, 23, 30], fill=(84, 60, 40, 255))  # turned earth
        if not bare:
            for (x0, y0, x1, y1) in ((16, 25, 8, 10), (16, 25, 24, 9), (16, 25, 16, 6), (16, 25, 11, 16), (16, 25, 22, 15)):
                d.line([(x0, y0), (x1, y1)], fill=leaf_dark, width=1)
                d.ellipse([x1 - 3, y1 - 2, x1 + 3, y1 + 2], fill=leaf, outline=leaf_dark)
        else:
            d.line([(16, 25), (16, 20)], fill=leaf_dark, width=1)
        save(img, name)
    img = blank(); d = ImageDraw.Draw(img)
    for (x, y, a) in ((10, 14, 0), (19, 18, 1)):
        d.polygon([(x - 6, y - 3), (x + 6, y - 1), (x + 4, y + 4), (x - 5, y + 3)], fill=root, outline=root_dark)
        d.line([(x + 5, y), (x + 9, y + 2 + a)], fill=root_dark)
    save(img, "items/emberroot")
    img = blank(); d = ImageDraw.Draw(img)
    d.ellipse([5, 12, 27, 26], fill=(150, 90, 50, 255), outline=(90, 50, 26, 255))
    d.ellipse([7, 10, 25, 22], fill=(176, 108, 60, 255), outline=(110, 62, 32, 255))
    for (x, y) in ((11, 14), (17, 13), (21, 17), (14, 18)):
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(160, 30, 60, 255))
    d.point([(19, 15), (12, 17)], fill=(210, 80, 40, 255))
    save(img, "items/dragon_cake")

    flax_and_fiber()

    wood, wood_dark = (140, 98, 58, 255), (96, 64, 38, 255)
    stone, stone_dark = (150, 146, 136, 255), (92, 88, 82, 255)
    drag, drag_dark, drag_glow = (60, 54, 62, 255), (30, 26, 34, 255), (236, 120, 60, 255)
    lash = (196, 170, 120, 255)

    def axe(name, head, dark, glow=None):
        img = blank(); d = ImageDraw.Draw(img)
        d.line([(9, 28), (22, 6)], fill=wood, width=3)
        d.polygon([(17, 5), (27, 8), (25, 16), (19, 12)], fill=head, outline=dark)
        if glow:
            d.line([(26, 9), (24, 15)], fill=glow)
        else:
            d.line([(18, 9), (21, 11)], fill=lash, width=2)
        save(img, name)

    def pick(name, head, dark, glow=None):
        img = blank(); d = ImageDraw.Draw(img)
        d.line([(8, 28), (18, 10)], fill=wood, width=3)
        d.arc([5, 3, 31, 21], 200, 340, fill=head, width=3)
        if glow:
            d.point([(7, 10), (29, 10)], fill=glow)
        save(img, name)

    def hammer(name, head, dark, glow=None):
        img = blank(); d = ImageDraw.Draw(img)
        d.line([(9, 27), (19, 13)], fill=wood, width=3)
        d.rectangle([14, 5, 28, 12], fill=head, outline=dark)
        if glow:
            d.line([(16, 8), (26, 8)], fill=glow)
        save(img, name)

    axe("items/stone_axe", stone, stone_dark); pick("items/stone_pick", stone, stone_dark); hammer("items/stone_hammer", stone, stone_dark)
    axe("items/dragon_axe", drag, drag_dark, drag_glow); pick("items/dragon_pick", drag, drag_dark, drag_glow); hammer("items/dragon_hammer", drag, drag_dark, drag_glow)
    img = blank(); d = ImageDraw.Draw(img)
    d.line([(6, 29), (26, 3)], fill=wood, width=2)
    d.line([(26, 3), (27, 20)], fill=(220, 220, 210, 255))
    d.arc([24, 18, 30, 24], 0, 180, fill=(150, 154, 164, 255))
    save(img, "items/fishing_rod")


def flax_and_fiber():
    """Wild flax (cut back to stubble once harvested) and the plant fiber it gives, for bandages."""
    stem, stem_dark, flower, flower_dark = (122, 150, 74, 255), (78, 100, 46, 255), (120, 150, 220, 255), (70, 90, 160, 255)
    for name, bare in (("things/wild_flax", False), ("things/wild_flax_bare", True)):
        img = blank(); d = ImageDraw.Draw(img)
        d.ellipse([9, 26, 23, 30], fill=(92, 78, 52, 255))  # the clump's earth
        for (x0, x1, top) in ((12, 9, 9), (14, 13, 6), (16, 17, 5), (18, 21, 7), (20, 24, 10), (15, 11, 12)):
            d.line([(x0, 28), (x1, 28 - (28 - top) // 3 if bare else top)], fill=stem_dark if bare else stem, width=1)
            if not bare:
                d.ellipse([x1 - 2, top - 2, x1 + 2, top + 2], fill=flower, outline=flower_dark)
        save(img, name)
    fiber, fiber_dark, fiber_hi = (206, 192, 150, 255), (150, 136, 98, 255), (232, 222, 188, 255)
    img = blank(); d = ImageDraw.Draw(img)
    for (dy, color) in ((0, fiber_dark), (2, fiber), (4, fiber), (6, fiber_hi)):
        d.line([(5, 18 + dy), (16, 14 + dy), (27, 17 + dy)], fill=color, width=2)
    d.rectangle([14, 13, 18, 25], fill=fiber_dark)  # the twist that binds the hank
    save(placeholder_sheets.outline(img, (84, 74, 50, 255)), "items/fiber")


def buildings():
    wall = noise_tile((128, 128, 134), 8, 11)
    d = ImageDraw.Draw(wall)
    for row, y in enumerate(range(0, S, 8)):
        d.line([(0, y), (S, y)], fill=(80, 80, 86, 255))
        off = 0 if row % 2 == 0 else 8
        for x in range(off, S, 16):
            d.line([(x, y), (x, y + 8)], fill=(80, 80, 86, 255))
    save(wall, "buildings/wall_stone")
    img = blank(); d = ImageDraw.Draw(img)
    d.ellipse([3, 3, 29, 29], fill=(130, 100, 70, 255), outline=(80, 60, 40, 255))
    d.ellipse([8, 8, 24, 24], fill=(200, 170, 120, 255))
    save(img, "buildings/nest")
    wagon()


def wagon():
    """Side view of a covered wagon, 48x40 (renderSize 1.5): wooden bed, canvas hood, two wheels."""
    img = Image.new("RGBA", (48, 40), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    wood, wood_dark, canvas, canvas_dark = (140, 98, 58, 255), (96, 64, 38, 255), (226, 214, 186, 255), (186, 172, 142, 255)
    d.chord([6, 2, 42, 34], 180, 360, fill=canvas)                     # hood
    for x in (14, 24, 34):
        d.line([(x, 4), (x, 18)], fill=canvas_dark)                      # hoops
    d.rectangle([4, 18, 44, 27], fill=wood)                              # bed
    d.line([(4, 22), (44, 22)], fill=wood_dark)
    d.line([(44, 24), (47, 21)], fill=wood_dark, width=2)                # tongue
    for cx in (12, 36):                                                  # wheels
        d.ellipse([cx - 7, 25, cx + 7, 39], fill=wood_dark)
        d.ellipse([cx - 5, 27, cx + 5, 37], fill=(0, 0, 0, 0))
        d.line([(cx - 5, 32), (cx + 5, 32)], fill=wood_dark)
        d.line([(cx, 27), (cx, 37)], fill=wood_dark)
    save(placeholder_sheets.outline(img, (34, 30, 40, 255)), "buildings/wagon")


def creatures():
    # Humans (chibi_people.py) and dragons (chibi_dragon.py) are animated sheets.
    # The dragon egg is a sheet from chibi_dragon.egg_sheet.
    pass


def story():
    """Adventure's story things (data/defs/story.json): moonpetal, standing stones, the ley stone."""
    petal, petal_light, glow, stem = (196, 214, 255, 255), (238, 244, 255, 255), (150, 190, 255, 255), (62, 110, 70, 255)
    img = blank(); d = ImageDraw.Draw(img)
    for (x, y, h) in ((11, 14, 12), (20, 11, 15), (16, 18, 9)):
        d.line([(x, y + 3), (x - 1 + (x % 3), 30)], fill=stem, width=1)
        d.ellipse([x - 4, y - 4, x + 4, y + 4], fill=glow)          # the faint halo
        for dx, dy in ((0, -3), (3, 0), (0, 3), (-3, 0)):
            d.ellipse([x + dx - 2, y + dy - 2, x + dx + 2, y + dy + 2], fill=petal)
        d.point((x, y), fill=petal_light)
    d.polygon([(8, 30), (12, 24), (14, 30)], fill=stem)
    d.polygon([(18, 30), (22, 25), (24, 30)], fill=stem)
    save(placeholder_sheets.outline(img, (34, 40, 70, 255)), "things/moonpetal")
    img = blank(); d = ImageDraw.Draw(img)
    for (x, y) in ((12, 14), (20, 18)):
        d.line([(x, y + 3), (x + 4, y + 11)], fill=stem, width=1)
        for dx, dy in ((0, -3), (3, 0), (0, 3), (-3, 0)):
            d.ellipse([x + dx - 2, y + dy - 2, x + dx + 2, y + dy + 2], fill=petal)
        d.point((x, y), fill=petal_light)
    save(placeholder_sheets.outline(img, (34, 40, 70, 255)), "items/moonpetal")
    stone, stone_dark, stone_light, moss = (138, 136, 128, 255), (92, 90, 86, 255), (176, 174, 164, 255), (96, 128, 70, 255)
    img = blank(); d = ImageDraw.Draw(img)
    d.polygon([(10, 31), (9, 8), (13, 2), (20, 3), (23, 9), (22, 31)], fill=stone)
    d.polygon([(18, 31), (19, 6), (23, 9), (22, 31)], fill=stone_dark)
    d.line([(11, 6), (13, 3)], fill=stone_light)
    d.polygon([(10, 31), (10, 24), (15, 27), (17, 31)], fill=moss)
    save(placeholder_sheets.outline(img, (40, 38, 36, 255)), "things/standing_stone")
    rune = (140, 210, 255, 255)
    img = blank(); d = ImageDraw.Draw(img)
    d.polygon([(8, 31), (9, 6), (16, 1), (23, 6), (24, 31)], fill=stone)
    d.polygon([(17, 31), (16, 1), (23, 6), (24, 31)], fill=stone_dark)
    d.line([(10, 8), (15, 3)], fill=stone_light)
    for y in (9, 15, 21, 27):  # glowing runes down the face
        d.line([(12, y), (14, y - 2), (16, y)], fill=rune)
        d.point((14, y + 1), fill=rune)
    save(placeholder_sheets.outline(img, (40, 38, 36, 255)), "things/ley_stone")
    iron, iron_dark, thong = (150, 154, 164, 255), (84, 86, 96, 255), (120, 82, 46, 255)
    img = blank(); d = ImageDraw.Draw(img)
    d.arc([6, 4, 16, 14], 0, 360, fill=thong, width=1)                   # the thong it hangs on
    d.ellipse([12, 9, 20, 17], fill=iron, outline=iron_dark)              # bow
    d.ellipse([15, 12, 17, 14], fill=(0, 0, 0, 0))
    d.line([(18, 16), (26, 26)], fill=iron, width=2)                      # shank
    d.line([(23, 23), (25, 21)], fill=iron, width=2); d.line([(25, 26), (27, 24)], fill=iron, width=2)  # bit
    save(placeholder_sheets.outline(img, (34, 30, 40, 255)), "items/chain_key")
    # A roll of bandages.
    linen, linen_shade, linen_hi = (232, 226, 210, 255), (190, 182, 162, 255), (250, 248, 240, 255)
    img = blank(); d = ImageDraw.Draw(img)
    d.ellipse([6, 10, 20, 26], fill=linen, outline=linen_shade)           # the roll's end
    d.ellipse([11, 15, 15, 21], fill=linen_shade)
    d.polygon([(13, 10), (24, 8), (27, 24), (13, 26)], fill=linen)         # the roll's side
    for y in (12, 16, 20):
        d.line([(15, y + 1), (25, y - 1)], fill=linen_shade)
    d.line([(14, 11), (23, 9)], fill=linen_hi)
    d.polygon([(26, 22), (30, 27), (27, 29), (24, 24)], fill=linen)       # a loose tail
    save(placeholder_sheets.outline(img, (70, 64, 56, 255)), "items/bandage")
    # The cage gate: iron-bound wooden bars with gaps you can see through (the door slab is alpha-tested).
    bar, bar_dark, band, band_hi = (122, 84, 50, 255), (84, 56, 32, 255), (96, 98, 108, 255), (150, 154, 164, 255)
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    for x in range(2, 32, 6):
        d.rectangle([x, 0, x + 2, 31], fill=bar)
        d.line([(x + 2, 0), (x + 2, 31)], fill=bar_dark)
    for y in (2, 14, 27):
        d.rectangle([0, y, 31, y + 2], fill=band)
        d.line([(0, y), (31, y)], fill=band_hi)
    save(img, "buildings/cage_gate")
    # A padlock, drawn on a locked gate.
    body, shackle = (176, 140, 60, 255), (150, 154, 164, 255)
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    d.arc([4, 1, 11, 9], 180, 360, fill=shackle, width=2)
    d.line([(4, 5), (4, 8)], fill=shackle, width=2); d.line([(11, 5), (11, 8)], fill=shackle, width=2)
    d.rectangle([3, 7, 12, 14], fill=body)
    d.point((7, 10), fill=(40, 30, 20, 255)); d.line([(7, 11), (7, 12)], fill=(40, 30, 20, 255))
    save(placeholder_sheets.outline(img, (34, 30, 40, 255)), "props/padlock")


def ui():
    img = blank(); d = ImageDraw.Draw(img)
    for (x0, y0, dx, dy) in ((0, 0, 1, 1), (31, 0, -1, 1), (0, 31, 1, -1), (31, 31, -1, -1)):
        d.line([(x0, y0), (x0 + dx * 8, y0)], fill=(255, 255, 255, 255), width=2)
        d.line([(x0, y0), (x0, y0 + dy * 8)], fill=(255, 255, 255, 255), width=2)
    save(img, "ui/selection")
    img = blank(); d = ImageDraw.Draw(img)
    d.line([(4, 4), (28, 28)], fill=(255, 255, 255, 255), width=3)
    d.line([(28, 4), (4, 28)], fill=(255, 255, 255, 255), width=3)
    save(img, "ui/designate_x")
    img = blank(); d = ImageDraw.Draw(img)
    d.polygon([(16, 2), (24, 16), (20, 28), (12, 28), (8, 16)], fill=(255, 140, 30, 230))
    d.polygon([(16, 10), (20, 20), (16, 27), (12, 20)], fill=(255, 230, 120, 240))
    save(img, "fx/fire")
    # Smoke puff: a soft white blob (tinted grey when drawn), alpha falling off from the middle.
    img = blank()
    for y in range(S):
        for x in range(S):
            r = math.hypot(x - 15.5, y - 15.5) / 15.5
            if r < 1:
                img.putpixel((x, y), (255, 255, 255, int(255 * (1 - r) ** 1.6)))
    save(img, "fx/smoke")


if __name__ == "__main__":
    terrain(); things(); crafting(); industry(); tools_and_scales(); forage_and_tools(); buildings(); creatures(); story(); ui()
    placeholder_sheets.generate()
    # The Norse art pass (docs/ART.md): people and their clothes, the adult dragon, the spruce.
    chibi_people.generate()  # people: hi-res chibi layers (bodies, hair, faces, every garment), 4x detail
    norse_dragon.fire_fx()   # the breath's element-coloured core
    chibi_dragon.generate()  # the Hearth dragon and the egg: chibi hatchling and juvenile, a majestic adult, 4x detail
    norse_dragon.generate()  # the Norse breed (creatures/dragon_norse_*), drawn a little larger
    norse_nature.generate()
    norse_items.generate()   # the worn gear's inventory icons
    placeholder_props.generate()
    norse_ground.generate()
    placeholder_ground.generate()
    placeholder_wood.generate()
    placeholder_mining.generate()
    norse_stone.generate()   # rock, ore, slopes, earth sides, boulders: replaces the drawings above
    placeholder_dragoncare.generate()
    norse_buildings.generate()   # walls, floors, doors, stairs, ladders, beds, model textures, furniture icons
    norse_smithy.generate()      # anvil, quench tub, grindstone, tool rack, fuel bin: model textures and icons
    inn_art.generate()           # the inn's upkeep: the mop, and the mess on the floor
    healer_art.generate()        # the empath's rune glyphs
    tinker_art.generate()        # the tinker's bench, locks, keys, contraptions and curios
    placeholder_roofs.generate()     # roof thatch and shingles
    norse_gear.generate()        # tools and weapons per metal, stock, ores, fuel, food, hides: replaces the recoloured gear
    chibi_animals.generate()     # deer, boar, hare: chibi, 4x detail
    norse_plants.generate()      # berry bushes, wild flax, emberroot
    norse_ui.generate()          # toolbar and care icons, fire and ember fx
    norse_glow.generate()    # campfire, kiln, forge, ember crystal, standing stones, moonpetal, dragonforged tools
    norse_roofs.generate()   # turf, thatch and shingle roofs: replaces placeholder_roofs' drawings
    norse_fx.generate()      # selection, designation mark, need and chatter bubbles (white art the game tints)
    chibi_nature.generate()  # trees, bushes, boulders, standing stones, wild plants: chibi, 4x detail (replaces the drawings above)
    village_art.generate()   # lanes, gardens, half-timbering and straw thatch, well, bridge, fences, crates, cabbages, pig (after the boar)
    print("wooden buildings, wood and berry bushes: see placeholder_wood.py")
    count = sum(len(f) for _, _, f in os.walk(ROOT))
    print(f"wrote {count} sprites to {os.path.normpath(ROOT)}")
