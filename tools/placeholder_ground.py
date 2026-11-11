"""Procedural placeholder ground art: dirt, rough stone, sand and water (tops, sides, patch accents),
natural slopes matched to the ground they climb, boulders, mined stone and burnt dead trees.

Tops tile seamlessly (wrapped noise and features) and come in variants with the same average
colour, so the per-cell picking in the mesher never shows a checkerboard. Sides tile
horizontally. Slope tops have their high edge at the top row; slope sides are right triangles
(alpha-cut: the high end on the left, the slope surface along the diagonal).
"""
import math
import random

from PIL import Image

from norse_ground import DEEP, SHALLOW, dirt_tile, grass_tile, periodic_noise, sand_tile, water_tile
from placeholder_nature import GRASS, save, shade, variant_name

S = 32

DIRT = (130, 94, 66)
STONE = (126, 120, 132)
SAND = (214, 194, 148)


def normalize(img, base):
    """Scale a tile so its average colour is exactly the base: variants then differ in pattern
    only, and per-cell picking never shows lighter and darker squares."""
    px = img.load()
    n = S * S
    avg = [sum(px[x, y][c] for y in range(S) for x in range(S)) / n for c in range(3)]
    k = [base[c] / max(1, avg[c]) for c in range(3)]
    for y in range(S):
        for x in range(S):
            r, g, b, a = px[x, y]
            px[x, y] = (min(255, int(r * k[0])), min(255, int(g * k[1])), min(255, int(b * k[2])), a)
    return img


def wrap(px, x, y, c):
    px[x % S, y % S] = c


def blob(px, cx, cy, r, color, rnd, rough=0.35):
    for dy in range(-int(r) - 1, int(r) + 2):
        for dx in range(-int(r) - 1, int(r) + 2):
            if math.hypot(dx, dy) <= r * (1 + rnd.uniform(-rough, rough) * 0.5):
                wrap(px, cx + dx, cy + dy, color)


# ---------------------------------------------------------------- dirt

def dirt_side(seed):
    """Topsoil over lighter subsoil, roots reaching down, buried stones, faint strata."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    mottle = periodic_noise(rnd)
    top = [rnd.randint(7, 10) for _ in range(S)]
    for x in range(S):
        for y in range(S):
            base = shade(DIRT, 0.8) if y < top[x] else shade(DIRT, 1.06 if (y // 7) % 2 else 1.0)
            px[x, y] = shade(base[:3], 1 + 0.06 * mottle(x, y) + rnd.uniform(-0.04, 0.04))
    for _ in range(rnd.randint(3, 5)):  # roots
        x, y = rnd.randrange(S), rnd.randint(2, 6)
        for k in range(rnd.randint(5, 12)):
            x += rnd.choice((-1, 0, 0, 1))
            wrap(px, x, y + k, (78, 58, 40, 255))
    for _ in range(rnd.randint(3, 5)):  # stones
        cx, cy = rnd.randrange(S), rnd.randint(12, 29)
        stone = (rnd.randint(130, 150), rnd.randint(122, 138), rnd.randint(108, 120), 255)
        blob(px, cx, cy, rnd.uniform(1.2, 2.2), stone, rnd)
        wrap(px, cx - 1, cy - 1, shade(stone, 1.15))
    return img


def dirt_tufts(seed, density):
    """Bare dirt with clumps of grass pushing through (the dirt block's accent patches)."""
    rnd = random.Random(seed)
    img = dirt_tile(seed + 50)
    px = img.load()
    for gy in range(0, S, 7):
        for gx in range(0, S, 7):
            if rnd.random() > density:
                continue
            cx, cy = gx + rnd.randint(0, 5), gy + rnd.randint(0, 5)
            wrap(px, cx, cy + 1, shade(DIRT, 0.7))
            for _ in range(rnd.randint(3, 6)):
                bx = cx + rnd.randint(-2, 2)
                for k in range(rnd.randint(1, 3)):
                    wrap(px, bx, cy - k, shade(GRASS, 1.0 + 0.1 * k))
    return img


# ---------------------------------------------------------------- stone

def rock_tile(seed, base=STONE):
    """Rough stone: irregular faceted slabs (wrapped Voronoi), each a slightly different grey,
    lit along their upper-left edge, with dark cracks between them and mineral flecks."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    pts = []
    while len(pts) < 5:
        p = (rnd.uniform(0, S), rnd.uniform(0, S), rnd.uniform(0.96, 1.04), rnd.uniform(-2, 2))
        # Keep facets apart (wrapped distance), or slivers between close points crack solid black.
        if all(math.hypot(min(abs(p[0] - q[0]), S - abs(p[0] - q[0])), min(abs(p[1] - q[1]), S - abs(p[1] - q[1]))) > 10 for q in pts):
            pts.append(p)
    # Only some facet borders crack open; the rest are just a change of shade, so it reads as
    # weathered bedrock rather than paving.
    open_seams = {(a, b) for a in range(len(pts)) for b in range(len(pts)) if a < b and rnd.random() < 0.55}
    mottle = periodic_noise(rnd, terms=7)
    mottle2 = periodic_noise(rnd, terms=7)
    for y in range(S):
        for x in range(S):
            dists = []
            for i, (px0, py0, _, _) in enumerate(pts):
                dx = min(abs(x - px0), S - abs(x - px0))
                dy = min(abs(y - py0), S - abs(y - py0))
                dists.append((math.hypot(dx, dy), i))
            dists.sort()
            (d0, i0), (d1, i1) = dists[0], dists[1]
            k, tint = pts[i0][2], pts[i0][3]
            c = (base[0] + tint, base[1] + tint, base[2] + tint * 0.6)
            if d1 - d0 < 0.75 and (min(i0, i1), max(i0, i1)) in open_seams:
                px[x, y] = shade(c, 0.74)  # a hairline crack
                continue
            # Lit near the top-left of each facet, a touch darker towards its lower right.
            ox, oy = (x - pts[i0][0] + S / 2) % S - S / 2, (y - pts[i0][1] + S / 2) % S - S / 2
            light = -(ox + oy) / 30
            px[x, y] = shade(c, k * (1 + 0.04 * light + 0.06 * mottle(x, y) + 0.04 * mottle2(x * 2, y * 2) + rnd.uniform(-0.04, 0.04)))
    for _ in range(rnd.randint(8, 14)):  # pits and mineral flecks
        x, y = rnd.randrange(S), rnd.randrange(S)
        px[x, y] = shade(base, rnd.choice((0.78, 0.82, 1.2, 1.28)))
    return img


def rock_side(seed):
    """Horizontal strata: bands of varied grey with lit top edges and dark seams, a few cracks."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    mottle = periodic_noise(rnd)
    y = 0
    bands = []
    while y < S:
        h = rnd.randint(4, 9)
        bands.append((y, min(S, y + h), rnd.uniform(0.86, 1.08), rnd.uniform(-4, 4)))
        y += h
    for y0, y1, k, tint in bands:
        c = (STONE[0] + tint, STONE[1] + tint * 0.8, STONE[2])
        for yy in range(y0, y1):
            for x in range(S):
                edge = 1.12 if yy == y0 else 0.72 if yy == y1 - 1 else 1.0
                px[x, yy] = shade(c, k * edge * (1 + 0.05 * mottle(x, yy) + rnd.uniform(-0.04, 0.04)))
    for _ in range(rnd.randint(2, 4)):  # vertical cracks within a band
        y0, y1, _, _ = rnd.choice(bands)
        x = rnd.randrange(S)
        for yy in range(y0 + 1, y1 - 1):
            px[x % S, yy] = shade(STONE, 0.6)
            x += rnd.choice((0, 0, 1, -1))
    return img


def rock_moss(seed, density):
    """Stone with moss cushions and lichen crusts (the stone block's accent patches)."""
    rnd = random.Random(seed)
    img = rock_tile(seed + 60)
    px = img.load()
    for gy in range(0, S, 8):
        for gx in range(0, S, 8):
            if rnd.random() > density:
                continue
            cx, cy = gx + rnd.randint(0, 7), gy + rnd.randint(0, 7)
            if rnd.random() < 0.65:  # moss: soft green cushion, lit on top
                r = rnd.uniform(1.6, 3.0)
                blob(px, cx, cy, r, (86, 112, 58, 255), rnd)
                blob(px, cx - 1, cy - 1, r * 0.5, (112, 140, 70, 255), rnd)
            else:  # lichen: flat crusty speckle
                col = rnd.choice(((196, 176, 92, 255), (168, 180, 150, 255), (214, 138, 70, 255)))
                for _ in range(rnd.randint(4, 8)):
                    wrap(px, cx + rnd.randint(-2, 2), cy + rnd.randint(-2, 2), col)
    return img


# ---------------------------------------------------------------- slopes

def gradient(img, top=1.06, bottom=0.88):
    """Lighter at the high (top) edge, darker where the slope meets the ground below."""
    px = img.load()
    for y in range(S):
        k = top + (bottom - top) * y / (S - 1)
        for x in range(S):
            px[x, y] = shade(px[x, y][:3], k)
    return img


def slope_grass(seed):
    rnd = random.Random(seed)
    img = grass_tile(seed, GRASS)
    px = img.load()
    # Worn, earthy runnels down the lower half where feet and rain have scuffed the turf.
    for _ in range(rnd.randint(3, 5)):
        x = rnd.randrange(S)
        for y in range(rnd.randint(14, 22), S):
            if rnd.random() < 0.8:
                px[x % S, y] = shade(DIRT, rnd.uniform(0.9, 1.05))
            x += rnd.choice((0, 0, 1, -1))
    return gradient(img)


def slope_dirt(seed):
    rnd = random.Random(seed)
    img = dirt_tile(seed)
    px = img.load()
    for y0 in (rnd.randint(8, 12), rnd.randint(20, 25)):  # wavy ruts across the incline
        for x in range(S):
            y = y0 + int(round(math.sin(x * 0.4 + seed) * 1.2))
            px[x, y % S] = shade(DIRT, 0.72)
            px[x, (y - 1) % S] = shade(DIRT, 1.1)
    return gradient(img)


def slope_stone(seed):
    rnd = random.Random(seed)
    img = rock_tile(seed)
    px = img.load()
    for y0 in (rnd.randint(9, 12), rnd.randint(21, 24)):  # small ledges stepping down the slope
        for x in range(S):
            y = y0 + (1 if (x // 9) % 2 else 0)
            px[x, y] = shade(STONE, 1.25)
            px[x, (y + 1) % S] = shade(STONE, 0.6)
    return gradient(img, 1.08, 0.86)


def slope_sand(seed):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = shade(SAND, 1 + rnd.uniform(-0.035, 0.035))
    for y0 in range(4, S, 7):  # wind ripples across the slope
        for x in range(S):
            y = y0 + int(round(math.sin(x * 0.5 + y0) * 1.0))
            px[x, y % S] = shade(SAND, 0.88)
    return gradient(img)


def triangle(side, band_color=None, band=0):
    """Cut a side texture to the slope's right triangle (opaque where y >= x), optionally laying
    a band of surface colour along the diagonal (the turf edge of a grassy bank)."""
    img = side.copy()
    px = img.load()
    rnd = random.Random(7)
    for y in range(S):
        for x in range(S):
            if y < x:
                px[x, y] = (0, 0, 0, 0)
            elif band_color and y - x < band + rnd.randint(-1, 1):
                px[x, y] = shade(band_color, rnd.uniform(0.92, 1.12))
    return img


def sand_side():
    rnd = random.Random(3)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = shade(SAND, (0.95 if (y // 6) % 2 else 1.0) * (1 + rnd.uniform(-0.04, 0.04)))
    return img


def generate():
    for i in range(4):
        save(normalize(dirt_tile(500 + i), DIRT), variant_name("terrain/dirt", i))
        save(normalize(rock_tile(600 + i), STONE), variant_name("terrain/rock_floor", i))
    for i in range(3):
        save(dirt_side(700 + i), variant_name("terrain/dirt_side", i))
        save(rock_side(800 + i), variant_name("terrain/rock_side", i))
    for i in range(2):
        save(dirt_tufts(900 + i, 0.75), variant_name("terrain/dirt_tufts", i))
        save(dirt_tufts(920 + i, 0.3), variant_name("terrain/dirt_tufts_light", i))
        save(rock_moss(940 + i, 0.7), variant_name("terrain/rock_floor_moss", i))
        save(rock_moss(960 + i, 0.28), variant_name("terrain/rock_floor_moss_light", i))

    # Natural slopes, one look per ground they climb (the mesher picks slope_<ground>), with
    # matching triangular side faces. The plain "terrain/slope" is the fallback (dirt).
    save(slope_grass(1000), "terrain/slope_grass")
    save(slope_dirt(1001), "terrain/slope_dirt")
    save(slope_stone(1002), "terrain/slope_stone")
    save(slope_sand(1003), "terrain/slope_sand")
    save(slope_dirt(1004), "terrain/slope")
    save(triangle(dirt_side(1010), GRASS, 5), "terrain/slope_grass_side")
    save(triangle(dirt_side(1011), shade(DIRT, 0.8)[:3], 3), "terrain/slope_dirt_side")
    save(triangle(rock_side(1012)), "terrain/slope_stone_side")
    save(triangle(sand_side(), shade(SAND, 0.9)[:3], 2), "terrain/slope_sand_side")
    save(triangle(dirt_side(1013)), "terrain/slope_side")
    generate_more()


# ---------------------------------------------------------------- sand

def sand_side_tile(seed):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        band = 0.94 if (y // 5) % 2 else 1.0
        for x in range(S):
            px[x, y] = shade(SAND, band * (1 + rnd.uniform(-0.04, 0.04)))
    for _ in range(3):
        x, y = rnd.randrange(S), rnd.randint(4, 30)
        wrap(px, x, y, (150, 140, 120, 255))
    return img


def sand_grass(seed, density):
    """Sand with tufts of tough dune grass (the sand block's accent patches)."""
    rnd = random.Random(seed)
    img = sand_tile(seed + 70)
    px = img.load()
    for gy in range(0, S, 8):
        for gx in range(0, S, 8):
            if rnd.random() > density:
                continue
            cx, cy = gx + rnd.randint(1, 6), gy + rnd.randint(2, 6)
            wrap(px, cx, cy + 1, shade(SAND, 0.78))
            for _ in range(rnd.randint(4, 7)):
                lean = rnd.choice((-1, 0, 1))
                for k in range(rnd.randint(2, 4)):
                    wrap(px, cx + (lean * k) // 2, cy - k, shade((128, 150, 82), 0.9 + 0.08 * k))
    return img


# ---------------------------------------------------------------- water

def water_side_tile(base):
    rnd = random.Random(9)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = shade(base, (1.05 - 0.3 * y / S) * (1 + rnd.uniform(-0.03, 0.03)))
    return img


# ---------------------------------------------------------------- rocks

def boulder(seed, moss=False):
    """A rounded, faceted lump of stone (shaded like the tree canopies), with cracks and maybe moss."""
    from placeholder_nature import blob_canopy
    from placeholder_sheets import outline
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    palette = [(78, 78, 86, 255), (104, 104, 112, 255), (130, 130, 138, 255), (162, 162, 168, 255)]
    circles = [(16 + rnd.uniform(-1, 1), 21, rnd.uniform(9.5, 11))]
    for _ in range(rnd.randint(2, 3)):
        circles.append((16 + rnd.uniform(-8, 8), 22 + rnd.uniform(-3, 4), rnd.uniform(5.5, 8)))
    circles = [(x, min(y, 31 - r * 0.75), r) for x, y, r in circles]
    blob_canopy(img, circles, palette, rnd, dither=0.08)
    px = img.load()
    for _ in range(rnd.randint(1, 3)):  # cracks
        x, y = rnd.randint(9, 23), rnd.randint(12, 20)
        for k in range(rnd.randint(4, 8)):
            if 0 <= x < S and 0 <= y < S and px[x, y][3]:
                px[x, y] = (62, 62, 70, 255)
            x += rnd.choice((-1, 0, 1))
            y += 1
    if moss:
        for _ in range(rnd.randint(10, 18)):
            x, y = rnd.randint(7, 25), rnd.randint(8, 18)
            if px[x, y][3] and (y == 0 or px[x, y - 1][3] == 0 or rnd.random() < 0.5):
                px[x, y] = rnd.choice(((86, 112, 58, 255), (112, 140, 70, 255)))
    return outline(img, (44, 44, 50, 255))


def stone_blocks(seed):
    """The mined stone item: a neat stack of rough-cut blocks, lit tops and shaded sides."""
    from placeholder_sheets import outline
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    px = img.load()
    for (x0, y0, w, h) in ((3, 20, 12, 9), (16, 20, 12, 9), (9, 11, 13, 9)):
        tint = rnd.uniform(-6, 6)
        face = (128 + tint, 128 + tint, 134 + tint)
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                k = 1.18 if y < y0 + 3 else 0.82 if x > x0 + w - 3 else 1.0  # top lit, right side shaded
                px[x, y] = shade(face, k * (1 + rnd.uniform(-0.05, 0.05)))
        for x in range(x0, x0 + w):  # chipped top edge
            if rnd.random() < 0.2:
                px[x, y0] = (0, 0, 0, 0)
    return outline(img, (60, 60, 66, 255))


def grass_side_tile(seed):
    """The grass block's cliff face: dirt side under a turf band, blades hanging over the edge."""
    rnd = random.Random(seed)
    img = dirt_side(seed + 5)
    px = img.load()
    grass = GRASS
    for x in range(S):
        depth = rnd.randint(4, 6)
        for y in range(depth):
            px[x, y] = shade(grass, (1.1 if y == 0 else 1.0 if y < depth - 1 else 0.8) * (1 + rnd.uniform(-0.05, 0.05)))
        if rnd.random() < 0.35:  # a blade or two hanging down
            for k in range(rnd.randint(1, 3)):
                px[x, depth + k] = shade(grass, 0.85 - 0.08 * k)
    return img


def generate_more():
    for i in range(4):
        save(normalize(sand_tile(1100 + i), SAND), variant_name("terrain/sand", i))
        save(normalize(water_tile(1200 + i), DEEP), variant_name("terrain/water", i))
        save(normalize(water_tile(1300 + i, SHALLOW, sandy=True), SHALLOW), variant_name("terrain/shallow_water", i))
    for i in range(2):
        save(sand_side_tile(1150 + i), variant_name("terrain/sand_side", i))
        save(sand_grass(1160 + i, 0.75), variant_name("terrain/sand_grass", i))
        save(sand_grass(1170 + i, 0.3), variant_name("terrain/sand_grass_light", i))
    save(water_side_tile(DEEP), "terrain/water_side")
    save(water_side_tile(SHALLOW), "terrain/shallow_water_side")
    for i in range(3):
        save(grass_side_tile(1400 + i), variant_name("terrain/grass_side", i))
        save(boulder(1500 + i, moss=(i == 2)), variant_name("things/boulder", i))
    save(stone_blocks(1600), "items/stone")


if __name__ == "__main__":
    generate()
