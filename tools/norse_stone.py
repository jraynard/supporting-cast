"""Rock for the Norse art pass: stone tops, strata side faces, ore seams, slopes and boulders.

Every rock is built from a hue-shifted ramp (``norse_art.ramp``) instead of a multiplied base colour:
cool violet shadows, warm pale highlights. Tops are wrapped Voronoi slabs with a chiselled bevel (lit on
the upper-left edge, shadowed on the lower-right, as the light falls), and a per-kind finish: granite's
crystals, limestone's pits, sandstone's bedding, slate's thin plates, basalt's columns. Sides are
strata: wavy bands with a lit lip and a dark underside. Like norse_ground, tiles are authored in the
pre-grade colour space and ``placeholder_nature.save`` runs ``nordic_finish`` over them, so they sit
with the grass, dirt and sand beside them.

All tops tile seamlessly and every variant of one kind is scaled to the same average colour
(docs/ART.md: variants must not checker).
"""
import math
import random

from PIL import Image

from norse_art import ramp, clamp8, bayer, shade_shape, mask_of, selective_outline, mix
from norse_ground import S, periodic_noise, normalize_to, wrap, grass_tile, dirt_tile, sand_tile

# kind: shape of the slabs; base: mid-tone before the grade; bands: strata thickness range on sides.
ROCKS = {
    "rough":     dict(base=(126, 120, 132), kind="rough",     bands=(4, 9)),
    "sandstone": dict(base=(198, 166, 118), kind="sandstone", bands=(5, 9)),
    "limestone": dict(base=(188, 184, 168), kind="limestone", bands=(4, 8)),
    "granite":   dict(base=(156, 134, 134), kind="granite",   bands=(6, 11)),
    "slate":     dict(base=(84, 92, 108),   kind="slate",     bands=(2, 4)),
    "basalt":    dict(base=(66, 64, 74),    kind="basalt",    bands=(5, 10)),
}

LEVEL = 64   # how tall a level looks in the game (src/DragonWorld/Rendering/WorldScale.cs): ramp art is drawn this tall
DIRT = (130, 94, 66)
GRASS = (90, 132, 66)
SAND = (214, 194, 148)


def rock_ramp(base, n=7):
    """Seven steps, darkest (crack) to lightest (a chipped edge catching the light)."""
    return ramp(base, n, main=3, dark=0.42, light=1.42, cool=26, warm=14)


# ---------------------------------------------------------------- slabs

def sites_for(rnd, kind):
    """Slab centres on the wrapped tile. Basalt takes a staggered lattice (columns); the rest are
    scattered but kept apart so no sliver crams between two close points."""
    if kind == "basalt":
        pts = []
        for r in range(4):
            for c in range(3):
                x = (c + (0.5 if r % 2 else 0.0)) * S / 3 + rnd.uniform(-1.4, 1.4)
                y = (r + 0.5) * S / 4 + rnd.uniform(-1.2, 1.2)
                pts.append((x % S, y % S))
        return pts
    n, gap = {"limestone": (4, 12), "sandstone": (5, 10), "granite": (6, 9), "slate": (7, 7)}.get(kind, (5, 10))
    pts = []
    tries = 0
    while len(pts) < n and tries < 400:
        tries += 1
        p = (rnd.uniform(0, S), rnd.uniform(0, S))
        # slate slabs are wide and flat: measure distance with the vertical stretched
        sy = 1.9 if kind == "slate" else 1.0
        if all(math.hypot(min(abs(p[0] - q[0]), S - abs(p[0] - q[0])), sy * min(abs(p[1] - q[1]), S - abs(p[1] - q[1]))) > gap for q in pts):
            pts.append(p)
    return pts


def slab_ids(pts, kind):
    """For every pixel: the nearest site, and the gap between the nearest two (a crack where it is small)."""
    sy = 1.9 if kind == "slate" else 1.0
    ids = [[0] * S for _ in range(S)]
    gaps = [[0.0] * S for _ in range(S)]
    for y in range(S):
        for x in range(S):
            ds = []
            for i, (px0, py0) in enumerate(pts):
                dx = min(abs(x - px0), S - abs(x - px0))
                dy = min(abs(y - py0), S - abs(y - py0)) * sy
                ds.append((math.hypot(dx, dy), i))
            ds.sort()
            ids[y][x] = ds[0][1]
            gaps[y][x] = ds[1][0] - ds[0][0]
    return ids, gaps


def rock_top(seed, spec, detail=True):
    """A rock's top face: slabs with a chiselled bevel, a tonal ripple and the kind's own finish."""
    rnd = random.Random(seed)
    kind, base = spec["kind"], spec["base"]
    r = rock_ramp(base)
    pts = sites_for(rnd, kind)
    ids, gaps = slab_ids(pts, kind)
    # How much of the slab network cracks open: bedded rock barely cracks, columns crack everywhere.
    open_p = {"limestone": 0.35, "sandstone": 0.4, "granite": 0.55, "slate": 0.7, "basalt": 0.95}.get(kind, 0.55)
    seams = {(a, b) for a in range(len(pts)) for b in range(a + 1, len(pts)) if rnd.random() < open_p}
    tone = [3 + rnd.uniform(-0.9, 0.9) for _ in pts]                 # each slab sits a step lighter or darker
    mottle = periodic_noise(rnd, terms=7, max_freq=3)
    mottle2 = periodic_noise(rnd, terms=6, max_freq=4)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        for x in range(S):
            i = ids[y][x]
            sx, sy = pts[i]
            ox, oy = (x - sx + S / 2) % S - S / 2, (y - sy + S / 2) % S - S / 2
            v = tone[i] + 0.5 * mottle(x, y) + 0.35 * mottle2(x * 2, y * 2) - (ox + oy) / 42.0
            v += bayer(x, y) * 0.55
            # Bevel: a slab's upper-left lip catches the light, its lower-right edge falls into shadow.
            ul = ids[(y - 1) % S][(x - 1) % S] != i
            lr = ids[(y + 1) % S][(x + 1) % S] != i
            if ul:
                v += 1.35
            elif lr:
                v -= 1.35
            if gaps[y][x] < 0.8 and any((min(i, j), max(i, j)) in seams for j in {ids[(y + dy) % S][(x + dx) % S] for dx in (-1, 0, 1) for dy in (-1, 0, 1)} if j != i):
                px[x, y] = r[0]                                       # an open crack
                continue
            px[x, y] = r[max(1, min(5, int(round(v))))]
    if detail:
        finish_top(px, rnd, kind, r, base)
    return normalize_to(img, base)


def finish_top(px, rnd, kind, r, base):
    """What makes each rock itself: crystals, pits, bedding, plates or sheen."""
    if kind == "granite":
        for _ in range(34):                                           # quartz, feldspar and biotite grains
            x, y = rnd.randrange(S), rnd.randrange(S)
            roll = rnd.random()
            if roll < 0.4:
                wrap(px, x, y, (226, 220, 214, 255)); wrap(px, x + 1, y, (176, 168, 168, 255))
            elif roll < 0.72:
                wrap(px, x, y, (190, 140, 132, 255)); wrap(px, x, y + 1, (146, 98, 98, 255))
            else:
                wrap(px, x, y, (44, 38, 48, 255)); wrap(px, x + 1, y + 1, (70, 62, 72, 255))
    elif kind == "limestone":
        for _ in range(rnd.randint(5, 8)):                            # solution pits, lit on the far lip
            x, y = rnd.randrange(S), rnd.randrange(S)
            wrap(px, x, y, r[1]); wrap(px, x + 1, y, r[1]); wrap(px, x, y + 1, r[0])
            wrap(px, x + 1, y + 1, r[5]); wrap(px, x - 1, y - 1, r[4])
        if rnd.random() < 0.5:                                        # a fossil shell: a tiny ammonite whorl
            x, y = rnd.randrange(4, 28), rnd.randrange(4, 28)
            for dx, dy in ((0, -1), (1, -1), (2, 0), (2, 1), (1, 2), (0, 2), (-1, 1), (-1, 0)):
                wrap(px, x + dx, y + dy, r[5])
            wrap(px, x, y, r[2])
            wrap(px, x + 3, y + 2, r[1])
    elif kind == "sandstone":
        for k in range(3):                                            # bedding: faint wavy planes across the slabs
            y0, ph = rnd.randrange(S), rnd.uniform(0, math.tau)
            for x in range(S):
                y = y0 + round(math.sin(x * math.tau / S * 2 + ph) * 1.4)
                wrap(px, x, y, r[2]); wrap(px, x, y + 1, r[4])
        for _ in range(30):                                           # loose grains
            wrap(px, rnd.randrange(S), rnd.randrange(S), r[rnd.choice((2, 4, 5))])
    elif kind == "slate":
        for _ in range(9):                                            # split edges: short bright flakes
            x, y = rnd.randrange(S), rnd.randrange(S)
            for k in range(rnd.randint(2, 4)):
                wrap(px, x + k, y, r[5])
            wrap(px, x, y + 1, r[1])
    elif kind == "basalt":
        for _ in range(10):                                           # a cold blue sheen on fresh faces
            x, y = rnd.randrange(S), rnd.randrange(S)
            wrap(px, x, y, (98, 112, 150, 255)); wrap(px, x + 1, y, r[4])
        for _ in range(8):                                            # gas bubbles frozen in the glass
            x, y = rnd.randrange(S), rnd.randrange(S)
            wrap(px, x, y, r[0]); wrap(px, x - 1, y - 1, r[4])
    else:                                                             # rough stone: pits and lichen-free flecks
        for _ in range(12):
            x, y = rnd.randrange(S), rnd.randrange(S)
            wrap(px, x, y, r[rnd.choice((1, 1, 5, 5))])


# ---------------------------------------------------------------- strata sides

def rock_side(seed, spec):
    """The cliff face: wavy beds, each a tone of the ramp with a lit lip and a dark underside, vertical
    joints that step from bed to bed, and chips where the weather has bitten the lip."""
    rnd = random.Random(seed)
    kind, base = spec["kind"], spec["base"]
    r = rock_ramp(base)
    lo, hi = spec["bands"]
    mottle = periodic_noise(rnd, terms=7, max_freq=3)
    # bed boundaries: y positions that wobble with x (whole periods, so the face tiles sideways)
    cuts, y = [], 0
    while y < S - lo:
        cuts.append(y)
        y += rnd.randint(lo, hi)
    cuts.append(S)
    waves = [(rnd.randint(1, 2), rnd.uniform(0, math.tau), rnd.uniform(0.4, 1.0)) for _ in cuts]
    amp = 0.55 if kind == "slate" else 1.1

    def edge(i, x):
        if i == 0:
            return 0
        if i == len(cuts) - 1:
            return S
        f, ph, a = waves[i]
        return cuts[i] + round(math.sin(math.tau * f * x / S + ph) * amp * a)

    img = Image.new("RGBA", (S, S))
    px = img.load()
    bed_tone = [3 + rnd.uniform(-0.8, 0.8) for _ in cuts]
    joints = [[rnd.randrange(S)] if rnd.random() < 0.45 else [] for _ in cuts]
    for x in range(S):
        for i in range(len(cuts) - 1):
            y0, y1 = edge(i, x), edge(i + 1, x)
            for y in range(max(0, y0), min(S, y1)):
                v = bed_tone[i] + 0.55 * mottle(x, y) + bayer(x, y) * 0.5
                if y == y0 and i > 0:
                    v += 1.8                                          # the lit lip of this bed
                elif y == y0 + 1 and i > 0:
                    v += 0.6
                elif y >= y1 - 1:
                    v -= 1.9                                          # the shadow it throws on the bed below
                elif y == y1 - 2:
                    v -= 0.8
                if any(x == jx for jx in joints[i]):                  # a vertical joint
                    v = 1.0
                elif any(x == (jx + 1) % S for jx in joints[i]):
                    v += 0.8
                px[x, y] = r[max(0, min(6, int(round(v))))]
    rnd2 = random.Random(seed + 1)
    for _ in range(rnd2.randint(3, 6)):                               # chips and pits
        x, y = rnd2.randrange(S), rnd2.randrange(S)
        wrap(px, x, y, r[1]); wrap(px, x + 1, y + 1, r[5])
    if kind == "granite":
        finish_top(px, rnd2, "granite", r, base)
    return normalize_to(img, base)


# ---------------------------------------------------------------- ore

def nugget(px, cx, cy, rx, ry, cols, hi=None, shadow=(52, 44, 64, 255)):
    """A little lump of ore: three tones against the light, a bright pip, a shadow cut in the rock."""
    dark, mid, light = cols
    for dy in range(-ry - 1, ry + 2):
        for dx in range(-rx - 1, rx + 2):
            e = (dx / (rx + 0.35)) ** 2 + (dy / (ry + 0.35)) ** 2
            if e <= 1.0:
                u = (dx * -0.55 + dy * -0.83) / max(1.0, max(rx, ry))
                c = light if u > 0.35 else mid if u > -0.3 else dark
                wrap(px, cx + dx, cy + dy, c)
    wrap(px, cx - 1, cy - 1, hi or light)
    for dx in range(0, rx + 2):                                       # the rock's shadow, lower right
        wrap(px, cx + dx + 1, cy + ry + 1, shadow)
    wrap(px, cx + rx + 1, cy + 1, shadow)


ORES = {
    # name: host rock, nugget ramp (dark, mid, light), vein/streak colour
    "copper_ore":    dict(host="limestone", cols=((44, 112, 92), (70, 160, 120), (150, 214, 168)), vein=(204, 118, 66)),
    "tin_ore":       dict(host="granite",   cols=((38, 34, 44), (72, 66, 78), (206, 202, 198)),    vein=(214, 210, 204)),
    "coal":          dict(host="sandstone", cols=((16, 14, 20), (34, 32, 40), (96, 100, 124)),     vein=(60, 66, 88)),
    "silver_ore":    dict(host="granite",   cols=((120, 126, 142), (190, 196, 210), (246, 248, 252)), vein=(226, 232, 244)),
    "gold_ore":      dict(host="granite",   cols=((150, 104, 24), (232, 182, 58), (255, 232, 130)),  vein=(255, 220, 100)),
    "ember_crystal": dict(host="basalt",    cols=((156, 50, 28), (244, 112, 40), (255, 210, 96)),   vein=(255, 170, 60)),
}


def ore_top(seed, name, side=False):
    spec = ROCKS[ORES[name]["host"]]
    o = ORES[name]
    img = rock_side(seed, spec) if side else rock_top(seed, spec)
    px = img.load()
    rnd = random.Random(seed + 77)
    if name == "coal":                                                # glossy bands of coal threaded through the rock
        for y0 in (rnd.randint(3, 8), rnd.randint(15, 19), rnd.randint(25, 28)):
            for x in range(S):
                h = 2 + (1 if math.sin(x * 0.5 + y0) > 0.4 else 0)
                for k in range(h):
                    wrap(px, x, y0 + k + round(math.sin(x * 0.3 + y0) * 0.8), o["cols"][1] if k else o["cols"][2] if x % 5 == 0 else o["cols"][1])
                wrap(px, x, y0 + h + round(math.sin(x * 0.3 + y0) * 0.8), o["cols"][0])
        for _ in range(7):
            nugget(px, rnd.randrange(S), rnd.randrange(S), 2, 1, o["cols"])
        return img
    if name == "ember_crystal":                                       # glowing shards standing in cracks
        for _ in range(7):
            x, y = rnd.randrange(3, 29), rnd.randrange(3, 28)
            h = rnd.randint(3, 5)
            for k in range(h):
                wrap(px, x, y - k, o["cols"][1] if k < h - 1 else o["cols"][2])
                wrap(px, x + 1, y - k, o["cols"][0])
            wrap(px, x, y + 1, o["cols"][0])
            wrap(px, x - 1, y - h // 2, o["vein"])
        return img
    for _ in range(rnd.randint(3, 4)):                                # a vein or two running diagonally
        x, y = rnd.randrange(S), rnd.randrange(S)
        for k in range(rnd.randint(7, 12)):
            wrap(px, x + k, y + k // 2, o["vein"])
            if name == "copper_ore":
                wrap(px, x + k, y + k // 2 + 1, o["cols"][0])
    for _ in range(rnd.randint(7, 9)):
        nugget(px, rnd.randrange(2, 30), rnd.randrange(2, 30), rnd.choice((2, 2, 3)), rnd.choice((1, 2, 2)), o["cols"])
    return img


# ---------------------------------------------------------------- earth sides (dirt, turf, sand, water)

def dirt_side(seed, topsoil=True):
    """Peat topsoil over lighter earth: a wavy boundary, roots reaching down, buried stones with a lit lip.
    Without ``topsoil`` it's the subsoil alone, for the lower tile of a level (the game stacks two side
    tiles up each level and names the lower one <side>_low)."""
    rnd = random.Random(seed)
    d = ramp(DIRT, 6, main=2, dark=0.5, light=1.45, cool=18, warm=14)
    mottle = periodic_noise(rnd, terms=7, max_freq=3)
    top = [8 + round(2 * math.sin(math.tau * x / S * 2 + seed) + rnd.choice((-1, 0, 0, 1))) for x in range(S)]
    if not topsoil:
        top = [-1] * S
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for x in range(S):
        for y in range(S):
            if y < top[x]:
                v = 1.0 + 0.5 * mottle(x, y) + bayer(x, y) * 0.5                 # darker, peaty topsoil
            else:
                v = 2.2 + 0.5 * mottle(x, y) + bayer(x, y) * 0.5 + ((y - top[x]) // 7 % 2) * 0.35
                if y == top[x]:
                    v -= 1.0
            px[x, y] = d[max(0, min(5, int(round(v))))]
    for _ in range(rnd.randint(3, 4)):                                # roots
        x, y = rnd.randrange(S), rnd.randint(1, 5)
        for k in range(rnd.randint(5, 12)):
            x += rnd.choice((-1, 0, 0, 1))
            wrap(px, x, y + k, d[0]);
            if k % 3 == 0:
                wrap(px, x + 1, y + k, d[1])
    for _ in range(rnd.randint(3, 5)):                                # stones
        cx, cy = rnd.randrange(S), rnd.randint(12, 29)
        st = rock_ramp((150, 144, 150), 5)
        nugget(px, cx, cy, rnd.choice((1, 2)), 1, (st[1], st[2], st[3]), st[4])
    return normalize_to(img, DIRT)


def grass_side(seed):
    """The grass block's cliff: dirt face under a turf lip with blades hanging over the edge."""
    rnd = random.Random(seed)
    img = dirt_side(seed + 5)
    px = img.load()
    g = ramp(GRASS, 6, main=2, dark=0.5, light=1.5, cool=16, warm=24)
    for x in range(S):
        depth = rnd.randint(4, 6)
        for y in range(depth):
            v = 3 if y == 0 else 2 if y < depth - 1 else 0
            px[x, y] = g[v + (1 if (x + y) % 3 == 0 and y < depth - 1 else 0)]
        if rnd.random() < 0.4:                                        # a blade or two hanging down
            for k in range(rnd.randint(1, 3)):
                px[x, depth + k] = g[max(0, 2 - k)]
    return img


def sand_side(seed):
    """Layered pale sand: slightly wavy beds with a lit crest and a darker underside, a pebble or two."""
    rnd = random.Random(seed)
    s = ramp(SAND, 6, main=3, dark=0.62, light=1.18, cool=20, warm=10)
    mottle = periodic_noise(rnd, terms=6, max_freq=3)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for x in range(S):
        for y in range(S):
            wob = round(math.sin(math.tau * x / S * 2 + seed) * 1.0)
            ph = (y + wob) % 6
            v = 3 + 0.5 * mottle(x, y) + bayer(x, y) * 0.5 + (0.9 if ph == 0 else -0.9 if ph == 5 else 0)
            px[x, y] = s[max(0, min(5, int(round(v))))]
    for _ in range(rnd.randint(2, 3)):
        x, y = rnd.randrange(S), rnd.randint(4, 30)
        wrap(px, x, y, (132, 140, 152, 255)); wrap(px, x + 1, y, (96, 104, 116, 255)); wrap(px, x, y + 1, s[0])
    return normalize_to(img, SAND)


def water_side(base, seed=9, low=False):
    """A calm face: the top's colour deepening with depth, a bright meniscus line at the surface. The
    ``low`` tile carries on deeper below it, without the line."""
    rnd = random.Random(seed)
    w = ramp(base, 6, main=2, dark=0.7, light=1.45, cool=8, warm=26)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        for x in range(S):
            v = (1.6 - 1.6 * y / S if low else 3.2 - 1.6 * y / S) + bayer(x, y) * 0.35 + rnd.uniform(-0.15, 0.15)
            px[x, y] = w[max(0, min(5, int(round(v))))]
    if not low:
        for x in range(S):
            px[x, 0] = w[4] if x % 3 else w[5]
    return img


# ---------------------------------------------------------------- slopes

def gradient(img, top=1.08, bottom=0.86):
    """Lighter at the high (top) edge, darker where the slope meets the ground below."""
    px = img.load()
    w, h = img.size
    for y in range(h):
        k = top + (bottom - top) * y / (h - 1)
        for x in range(w):
            r_, g_, b_, a = px[x, y]
            px[x, y] = (clamp8(r_ * k), clamp8(g_ * k), clamp8(b_ * k), a)
    return img


def stack(*tiles):
    """Tiles one above another (the first on top): seamless ground tiles make a seamless tall one."""
    img = Image.new("RGBA", (S, S * len(tiles)))
    for i, t in enumerate(tiles):
        img.paste(t, (0, S * i))
    return img


def slope_grass(seed):
    """A grassy slope's surface, 32x64 (a ramp climbs a level that looks 64 px tall), high edge on top:
    turf with worn runnels down its lower half."""
    rnd = random.Random(seed)
    img = stack(grass_tile(seed, GRASS), grass_tile(seed + 50, GRASS))
    px = img.load()
    d = ramp(DIRT, 6, main=2, dark=0.5, light=1.45, cool=18, warm=14)
    for _ in range(rnd.randint(4, 6)):                                # worn runnels where feet and rain scuff the turf
        x = rnd.randrange(S)
        for y in range(rnd.randint(26, 40), LEVEL):
            if rnd.random() < 0.8:
                px[x % S, y] = d[2 + rnd.choice((0, 0, 1))]
            x += rnd.choice((0, 0, 1, -1))
    return gradient(img)


def slope_dirt(seed):
    """A bare earth slope, 32x64, wavy ruts across the incline."""
    rnd = random.Random(seed)
    img = stack(dirt_tile(seed), dirt_tile(seed + 50))
    px = img.load()
    d = ramp(DIRT, 6, main=2, dark=0.5, light=1.45, cool=18, warm=14)
    for y0 in (rnd.randint(8, 12), rnd.randint(22, 28), rnd.randint(38, 44), rnd.randint(52, 58)):
        for x in range(S):
            y = y0 + round(math.sin(x * 0.4 + seed + y0) * 1.2)
            px[x, y % LEVEL] = d[0]
            px[x, (y - 1) % LEVEL] = d[4]
    return gradient(img)


def slope_sand(seed):
    """A sandy slope, 32x64, wind ripples across it."""
    img = stack(sand_tile(seed, SAND), sand_tile(seed + 50, SAND))
    px = img.load()
    s = ramp(SAND, 5, main=2, dark=0.62, light=1.18, cool=20, warm=10)
    for y0 in range(4, LEVEL, 7):                                     # wind ripples across the slope
        for x in range(S):
            y = y0 + round(math.sin(x * 0.5 + y0) * 1.0)
            px[x, y % LEVEL] = s[1]
            px[x, (y - 1) % LEVEL] = s[3]
    return gradient(img)


def slope_stone(seed, spec=ROCKS["rough"]):
    """A rocky slope, 32x64, small ledges stepping down it."""
    rnd = random.Random(seed)
    img = stack(rock_top(seed, spec), rock_top(seed + 50, spec))
    px = img.load()
    r = rock_ramp(spec["base"])
    for y0 in (rnd.randint(9, 12), rnd.randint(23, 26), rnd.randint(38, 41), rnd.randint(52, 55)):
        for x in range(S):
            y = y0 + (1 if (x // 9) % 2 else 0)
            px[x, y] = r[5]
            px[x, (y + 1) % LEVEL] = r[1]
    return gradient(img, 1.1, 0.85)


def triangle(side, band_ramp=None, band=0, edge=True):
    """Cut a side texture to the slope's right triangle (high end on the left: opaque below the diagonal
    from the top-left corner to the bottom-right), laying a band of surface colour along the diagonal
    (the turf edge of a grassy bank) and a lit ridge line on the slope. Works at any height: a 32x64
    side climbs a level as the game draws it."""
    img = side.copy()
    px = img.load()
    w, h = img.size
    k = h / w
    rnd = random.Random(7)
    for y in range(h):
        for x in range(w):
            surface = x * k
            if y < surface:
                px[x, y] = (0, 0, 0, 0)
            elif band_ramp and y - surface < band * k + rnd.randint(-1, 1):
                px[x, y] = band_ramp[2 if (x + y) % 3 else 3]
            elif edge and y - surface < k:
                px[x, y] = mix(px[x, y][:3], (255, 238, 208), 0.35)
    return img


# ---------------------------------------------------------------- boulders

def boulder(seed, moss=False):
    """A boulder: a few overlapping rounded lumps shaded against the light, cracked and chipped, with a
    cool contact shadow on the ground and, now and then, moss on the upper faces."""
    rnd = random.Random(seed)
    r = rock_ramp((132, 130, 142), 7)
    r = r[1:6]
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    lumps = [(16 + rnd.uniform(-1, 1), 21, rnd.uniform(9.5, 11), 7.5)]
    for _ in range(rnd.randint(2, 3)):
        lumps.append((16 + rnd.uniform(-8, 8), 22 + rnd.uniform(-3, 4), rnd.uniform(5.5, 8), 5.5))
    for cx, cy, rr, ry in lumps:
        cy = min(cy, 31 - ry * 0.75)
        m = mask_of((S, S), lambda d, cx=cx, cy=cy, rr=rr, ry=ry: d.ellipse([cx - rr, cy - ry, cx + rr, cy + ry], fill=255))
        shade_shape(img, m, r, radius=1.2, slope=1.7, depth=0.7, tex=lambda x, y: bayer(x, y) * 0.09)
    px = img.load()
    for _ in range(rnd.randint(1, 3)):                                # cracks
        x, y = rnd.randint(9, 23), rnd.randint(12, 20)
        for k in range(rnd.randint(4, 8)):
            if 0 <= x < S and 0 <= y < S and px[x, y][3]:
                px[x, y] = r[0]
                if x + 1 < S and px[x + 1, y][3]:
                    px[x + 1, y] = r[3]
            x += rnd.choice((-1, 0, 1))
            y += 1
    if moss:
        m = ramp((90, 120, 62), 4, main=1, dark=0.55, light=1.5, cool=14, warm=24)
        for _ in range(rnd.randint(14, 22)):
            x, y = rnd.randint(7, 25), rnd.randint(8, 18)
            if px[x, y][3] and (px[x, y - 1][3] == 0 or rnd.random() < 0.45):
                px[x, y] = rnd.choice((m[1], m[2], m[3]))
    return selective_outline(img)


# ---------------------------------------------------------------- moss, clay, iron

def rock_moss(seed, density):
    """Rough stone with moss cushions (lit on top, shaded underneath) and crusty lichen: the stone
    block's accent patches."""
    rnd = random.Random(seed)
    img = rock_top(seed + 60, ROCKS["rough"])
    px = img.load()
    m = ramp((90, 118, 62), 5, main=2, dark=0.5, light=1.5, cool=16, warm=24)
    for gy in range(0, S, 8):
        for gx in range(0, S, 8):
            if rnd.random() > density:
                continue
            cx, cy = gx + rnd.randint(0, 7), gy + rnd.randint(0, 7)
            if rnd.random() < 0.65:
                rr = rnd.uniform(1.8, 3.2)
                for dy in range(-4, 5):
                    for dx in range(-4, 5):
                        if math.hypot(dx, dy * 1.15) <= rr * (1 + rnd.uniform(-0.15, 0.15)):
                            u = (dx * -0.55 + dy * -0.83) / max(1.0, rr)
                            wrap(px, cx + dx, cy + dy, m[3] if u > 0.4 else m[2] if u > -0.3 else m[1] if u > -0.7 else m[0])
                wrap(px, cx - 1, cy - 1, m[4])
            else:
                col = rnd.choice(((198, 178, 96, 255), (170, 182, 152, 255), (216, 140, 72, 255)))
                for _ in range(rnd.randint(4, 8)):
                    wrap(px, cx + rnd.randint(-2, 2), cy + rnd.randint(-2, 2), col)
    return img


CLAY = (168, 112, 84)


def clay_top(seed):
    """Smooth terracotta clay: soft patches and dried hairline cracks in a loose polygon net."""
    from norse_ground import patches
    rnd = random.Random(seed)
    c = ramp(CLAY, 6, main=2, dark=0.55, light=1.35, cool=16, warm=14)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    patches(px, rnd, (c[1], c[2], c[3]), thresholds=(-0.2, 0.15), jitter=0.1, scale=(5, 8))
    for _ in range(rnd.randint(3, 4)):
        x, y = rnd.randrange(S), rnd.randrange(S)
        dx, dy = rnd.choice(((1, 0), (0, 1), (1, 1), (1, -1)))
        for k in range(rnd.randint(7, 12)):
            wrap(px, x + dx * k + rnd.choice((0, 0, 1)), y + dy * k + rnd.choice((0, 0, 1)), c[0])
            wrap(px, x + dx * k + 1, y + dy * k + 1, c[4])
    for _ in range(18):
        wrap(px, rnd.randrange(S), rnd.randrange(S), c[4])
    return normalize_to(img, CLAY)


def generate():
    """Every rock, ore, slope, earth side and boulder in terrain/ and things/boulder (replacing the older
    drawings from placeholder_ground and placeholder_mining, which run before this)."""
    from placeholder_nature import save, variant_name
    ores = dict(ORES)
    ores["iron_ore"] = dict(host="rough", cols=((96, 38, 26), (156, 72, 44), (214, 130, 84)), vein=(190, 96, 58))
    for i in range(4):
        save(rock_top(600 + i, ROCKS["rough"]), variant_name("terrain/rock_floor", i))
    for i in range(3):
        save(rock_side(800 + i, ROCKS["rough"]), variant_name("terrain/rock_side", i))
    for i in range(2):
        save(rock_moss(940 + i, 0.7), variant_name("terrain/rock_floor_moss", i))
        save(rock_moss(960 + i, 0.28), variant_name("terrain/rock_floor_moss_light", i))
    for n, (name, spec) in enumerate(ROCKS.items()):
        if name == "rough":
            continue
        for i in range(2):
            save(rock_top(2000 + n * 10 + i, spec), variant_name(f"terrain/{name}", i))
            save(rock_side(2100 + n * 10 + i, spec), variant_name(f"terrain/{name}_side", i))
    for n, name in enumerate(ores):
        o = ores[name]
        if name not in ORES:
            ORES[name] = o
        save(ore_top(2300 + n * 2, name), f"terrain/{name}")
        save(ore_top(2301 + n * 2, name, side=True), f"terrain/{name}_side")
    save(clay_top(2500), "terrain/clay")
    save(rock_side(2501, dict(base=CLAY, kind="sandstone", bands=(4, 8))), "terrain/clay_side")

    for i in range(3):
        save(dirt_side(700 + i), variant_name("terrain/dirt_side", i))
        save(grass_side(1400 + i), variant_name("terrain/grass_side", i))
        # The lower tile of each level (the game stacks two up a level): no turf, no topsoil.
        save(dirt_side(720 + i, topsoil=False), variant_name("terrain/dirt_side_low", i))
        save(dirt_side(1420 + i, topsoil=False), variant_name("terrain/grass_side_low", i))
        save(boulder(1500 + i, moss=(i == 2)), variant_name("things/boulder", i).replace("terrain/", ""))
    for i in range(2):
        save(sand_side(1150 + i), variant_name("terrain/sand_side", i))
    from norse_ground import DEEP, SHALLOW
    save(water_side(DEEP), "terrain/water_side")
    save(water_side(SHALLOW), "terrain/shallow_water_side")
    save(water_side(DEEP, low=True), "terrain/water_side_low")
    save(water_side(SHALLOW, low=True), "terrain/shallow_water_side_low")

    g = ramp(GRASS, 6, main=2, dark=0.5, light=1.5, cool=16, warm=24)
    d = ramp(DIRT, 6, main=2, dark=0.5, light=1.45, cool=18, warm=14)
    s = ramp(SAND, 6, main=3, dark=0.62, light=1.18, cool=20, warm=10)
    save(slope_grass(1000), "terrain/slope_grass")
    save(slope_dirt(1001), "terrain/slope_dirt")
    save(slope_stone(1002), "terrain/slope_stone")
    save(slope_sand(1003), "terrain/slope_sand")
    save(slope_dirt(1004), "terrain/slope")
    # Ramp sides are wedges a level tall (32x64), never stacked, so they're drawn whole.
    save(triangle(stack(dirt_side(1010), dirt_side(1020, topsoil=False)), g, 5), "terrain/slope_grass_side")
    save(triangle(stack(dirt_side(1011), dirt_side(1021, topsoil=False)), d[1:], 3), "terrain/slope_dirt_side")
    save(triangle(stack(rock_side(1012, ROCKS["rough"]), rock_side(1022, ROCKS["rough"]))), "terrain/slope_stone_side")
    save(triangle(stack(sand_side(1013), sand_side(1023)), s, 2), "terrain/slope_sand_side")
    save(triangle(stack(dirt_side(1014), dirt_side(1024, topsoil=False))), "terrain/slope_side")


if __name__ == "__main__":
    generate()
