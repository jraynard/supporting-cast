"""Ground tops for the Norse art pass: grass and its wildflower patches, peat-brown dirt, pale sand and
cold water, each built from a hue-shifted ramp instead of a multiplied base colour.

Tiles are 32x32 and seamless (every mark wraps), and every variant of one kind is scaled to the same
average colour, so the mesher's per-cell variant picking never shows a checkerboard (docs/ART.md).
They are authored in the pre-grade colour space: ``placeholder_nature.save`` runs ``nordic_finish`` over
them like the rest of the world, so slopes and side faces built from these tops match.
"""
import math
import random

from PIL import Image

from norse_art import ramp, clamp8

S = 32


def periodic_noise(rnd, size=32, terms=5, max_freq=3):
    """Smooth noise that tiles: a few sine waves with whole periods across the tile."""
    waves = [(rnd.randint(1, max_freq), rnd.randint(1, max_freq), rnd.uniform(0, math.tau), rnd.uniform(0.4, 1.0)) for _ in range(terms)]

    def f(x, y):
        return sum(a * math.sin(math.tau * (fx * x + fy * y) / size + ph) for fx, fy, ph, a in waves) / terms
    return f


def wrap(px, x, y, c):
    px[int(x) % S, int(y) % S] = c


def normalize_to(img, target):
    """Scale a tile's colour so its average is exactly ``target``: variants then differ in pattern only."""
    px = img.load()
    n = S * S
    avg = [sum(px[x, y][c] for y in range(S) for x in range(S)) / n for c in range(3)]
    k = [target[c] / max(1.0, avg[c]) for c in range(3)]
    for y in range(S):
        for x in range(S):
            r, g, b, a = px[x, y]
            px[x, y] = (clamp8(r * k[0]), clamp8(g * k[1]), clamp8(b * k[2]), a)
    return img


def patches(px, rnd, colors, thresholds=(-0.28, 0.12), jitter=0.16, scale=(5, 8)):
    """Fill the tile with tonal patches: two noise octaves, a little dither, three steps of a ramp
    (``colors`` is the 3 steps to use, dark to light)."""
    # Fine-grained waves (up to 4 periods per tile): long diagonal swells would repeat as visible streaks.
    m1, m2 = periodic_noise(rnd, terms=scale[0] + 2, max_freq=4), periodic_noise(rnd, terms=scale[1], max_freq=4)
    for y in range(S):
        for x in range(S):
            v = 0.6 * m1(x, y) + 0.4 * m2(x * 2, y * 2) + rnd.uniform(-jitter, jitter)
            px[x, y] = colors[0] if v < thresholds[0] else colors[1] if v < thresholds[1] else colors[2]


# ---------------------------------------------------------------- grass and wildflowers

def grass_tile(seed, base, blades=46):
    """Seamless meadow grass: soft tonal patches, little clumps of blades (dark roots, warm sunlit tips),
    trefoil clover, a pebble or two and now and then a fallen leaf."""
    rnd = random.Random(seed)
    g = ramp(base, 6, main=2, dark=0.5, light=1.5, cool=16, warm=24)
    soft = ramp(base, 5, main=2, dark=0.78, light=1.2, cool=10, warm=14)    # a gentler ramp for the broad tone
    img = Image.new("RGBA", (S, S))
    px = img.load()
    patches(px, rnd, (soft[1], soft[2], soft[3]), thresholds=(-0.3, 0.2))
    for _ in range(max(10, blades // 3)):                      # clumps: 2-4 blades leaning a little
        rx, ry = rnd.randrange(S), rnd.randrange(S)
        wrap(px, rx, ry + 1, g[0])
        wrap(px, rx + 1, ry + 1, g[1])
        for b in range(rnd.randint(2, 4)):
            ox = rx + b - 1 + rnd.randint(-1, 1)
            length = rnd.randint(3, 5)
            lean = rnd.choice((-1, 0, 0, 1))
            for k in range(length):
                col = g[2] if k == 0 else (g[3] if k < length - 1 else g[4 if rnd.random() < 0.6 else 5])
                wrap(px, ox + (lean if k >= length - 2 else 0), ry - k, col)
    for _ in range(rnd.randint(1, 2)):                         # clover: three dots and a darker heart
        cx, cy = rnd.randrange(S), rnd.randrange(S)
        for dx, dy in ((-1, 0), (1, 0), (0, -1)):
            wrap(px, cx + dx, cy + dy, g[4])
        wrap(px, cx, cy, g[1])
        wrap(px, cx, cy + 1, g[0])
    for _ in range(rnd.randint(1, 2)):                         # pebbles, cool grey
        x, y = rnd.randrange(S), rnd.randrange(S)
        wrap(px, x, y, (136, 138, 146, 255))
        wrap(px, x + 1, y, (98, 100, 110, 255))
        wrap(px, x, y + 1, g[0])
    if rnd.random() < 0.3:                                     # a fallen birch leaf
        x, y = rnd.randrange(S), rnd.randrange(S)
        wrap(px, x, y, (200, 158, 62, 255))
        wrap(px, x + 1, y, (156, 112, 44, 255))
    return normalize_to(img, base)


FLOWERS = {
    "yarrow": ((244, 242, 232), (232, 214, 150)),
    "buttercup": ((246, 206, 64), (222, 150, 36)),
    "harebell": ((124, 134, 214), (84, 90, 176)),
    "heather": ((176, 108, 170), (126, 70, 128)),
}


def flower_tile(seed, base, mix, density=0.7):
    """Grass with clusters of wildflowers; mix: list of (flower name, weight)."""
    rnd = random.Random(seed)
    g = ramp(base, 6, main=2, dark=0.5, light=1.5, cool=16, warm=24)
    img = grass_tile(seed + 500, base, blades=36)
    px = img.load()
    names = [n for n, _ in mix]
    weights = [w for _, w in mix]
    for gy in range(0, S, 6):                                  # a jittered grid, so a tile reads as a meadow
        for gx in range(0, S, 6):
            if rnd.random() > density:
                continue
            x, y = (gx + rnd.randint(0, 4)) % S, (gy + rnd.randint(0, 4)) % S
            kind = rnd.choices(names, weights)[0]
            petal, centre = FLOWERS[kind]
            petal, centre = petal + (255,), centre + (255,)
            lit = tuple(min(255, int(c * 1.12)) for c in petal[:3]) + (255,)
            wrap(px, x + 1, y + 2, g[0])                       # shadow under the head
            if kind == "yarrow":                               # a flat-topped cluster of tiny florets
                for dx, dy in ((-1, 0), (0, 0), (1, 0), (0, -1), (-1, 1), (1, 1)):
                    wrap(px, x + dx, y + dy, petal)
                wrap(px, x, y, centre)
                wrap(px, x - 1, y, lit)
            elif kind == "harebell":                           # a nodding bell on a thin stem
                wrap(px, x, y + 1, g[3])
                wrap(px, x, y, g[3])
                wrap(px, x + 1, y - 1, petal)
                wrap(px, x + 1, y, petal)
                wrap(px, x + 2, y, centre)
                wrap(px, x + 1, y - 2, lit)
            elif kind == "heather":                            # a small sprig of tight purple bells
                for k in range(3):
                    wrap(px, x, y - k, petal if k != 1 else centre)
                    if k == 1:
                        wrap(px, x + 1, y - k, petal)
                wrap(px, x - 1, y - 2, lit)
            else:                                              # buttercup: a bright cup
                for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1)):
                    wrap(px, x + dx, y + dy, petal)
                wrap(px, x, y, lit)
                wrap(px, x + 1, y + 1, centre)
    return img


# ---------------------------------------------------------------- dirt

def dirt_tile(seed, base=(130, 94, 66)):
    """Peaty earth: soft patches, clods with a lit edge and a shadow, cool pebbles, root fibres, grains."""
    rnd = random.Random(seed)
    d = ramp(base, 6, main=2, dark=0.5, light=1.45, cool=18, warm=14)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    patches(px, rnd, (d[1], d[2], d[3]), jitter=0.2, scale=(5, 9))
    for _ in range(rnd.randint(6, 9)):                         # clods
        cx, cy = rnd.randrange(S), rnd.randrange(S)
        r = rnd.choice((1, 1, 2))
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if dx * dx + dy * dy <= r * r + 1:
                    wrap(px, cx + dx, cy + dy, d[2] if (dx + dy) <= 0 else d[1])
        wrap(px, cx - r, cy - r, d[4])
        wrap(px, cx + r, cy + r, d[0])
    for _ in range(rnd.randint(3, 5)):                         # pebbles
        cx, cy = rnd.randrange(S), rnd.randrange(S)
        stone = (rnd.randint(138, 158), rnd.randint(140, 156), rnd.randint(144, 160), 255)
        for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1))[: rnd.choice((3, 4))]:
            wrap(px, cx + dx, cy + dy, stone)
        wrap(px, cx, cy, tuple(min(255, int(c * 1.14)) for c in stone[:3]) + (255,))
        wrap(px, cx, cy + 2, d[0])
        wrap(px, cx + 1, cy + 2, d[1])
    for _ in range(rnd.randint(1, 2)):                         # a root fibre, wandering
        x, y = rnd.randrange(S), rnd.randrange(S)
        dx = rnd.choice((-1, 1))
        for k in range(rnd.randint(4, 8)):
            wrap(px, x + k * dx, y + (k // 3) * rnd.choice((0, 1)), d[0])
    for _ in range(14):                                        # light grains
        wrap(px, rnd.randrange(S), rnd.randrange(S), d[4])
    return normalize_to(img, base)


# ---------------------------------------------------------------- sand

def sand_tile(seed, base=(214, 194, 148)):
    """Pale beach sand: soft patches, wind ripples with a lit crest, blue-grey shingle and shells."""
    rnd = random.Random(seed)
    s = ramp(base, 5, main=2, dark=0.62, light=1.18, cool=20, warm=10)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    patches(px, rnd, (s[1], s[2], s[2]), thresholds=(-0.2, 0.0), jitter=0.22, scale=(5, 9))
    for _ in range(rnd.randint(1, 2)):                         # ripples
        y0 = rnd.randrange(S)
        ph = rnd.uniform(0, math.tau)
        for x in range(S):
            y = y0 + round(math.sin(x * math.tau / S * 2 + ph) * 1.5)
            wrap(px, x, y, s[1])
            wrap(px, x, y - 1, s[3])
    for _ in range(rnd.randint(2, 4)):
        x, y = rnd.randrange(S), rnd.randrange(S)
        if rnd.random() < 0.55:                                # shingle
            wrap(px, x, y, (132, 140, 152, 255))
            wrap(px, x + 1, y, (96, 104, 116, 255))
            wrap(px, x, y + 1, s[0])
        else:                                                  # a shell
            wrap(px, x, y, (240, 230, 220, 255))
            wrap(px, x + 1, y, (214, 190, 182, 255))
            wrap(px, x, y + 1, s[1])
    return normalize_to(img, base)


# ---------------------------------------------------------------- water

DEEP = (30, 84, 142)
SHALLOW = (66, 132, 160)
BED = (206, 190, 150)


def water_tile(seed, base=DEEP, sandy=False):
    """Seamless water, kept calm: broad low-frequency tonal swells in three close shades, a few short
    pale ripple dashes and one or two glints. Shallow water lets the pale bed show through in patches."""
    rnd = random.Random(seed)
    w = ramp(base, 6, main=2, dark=0.75, light=1.45, cool=8, warm=26)
    soft = ramp(base, 4, main=1, dark=0.95, light=1.07, cool=2, warm=4)      # the swells barely show
    img = Image.new("RGBA", (S, S))
    px = img.load()
    # Waves with random signs on both axes, or the swells all lean one diagonal and read as streaks.
    waves = [(rnd.choice((-1, 1)), rnd.choice((-1, 0, 1)), rnd.uniform(0, math.tau), rnd.uniform(0.5, 1.0)) for _ in range(4)]
    norm = sum(a for *_, a in waves)

    def m1(x, y):
        return sum(a * math.sin(math.tau * (fx * x + fy * y) / S + ph) for fx, fy, ph, a in waves) / norm
    bed = periodic_noise(rnd, terms=6, max_freq=2)
    for y in range(S):
        for x in range(S):
            v = m1(x, y)                                           # no per-pixel jitter: that was the busyness
            c = soft[0] if v < -0.3 else soft[2] if v > 0.3 else soft[1]
            if sandy:
                k = 0.12 + 0.1 * max(-1.0, min(1.0, bed(x, y) * 1.5))
                c = tuple(int(c[i] * (1 - k) + BED[i] * k) for i in range(3)) + (255,)
            px[x, y] = c
    for _ in range(rnd.randint(2, 3)):                         # a few long, soft ripple dashes
        x, y = rnd.randrange(S), rnd.randrange(S)
        n = rnd.randint(5, 9)
        for k in range(n):
            wrap(px, x + k, y, w[3] if 0 < k < n - 1 else w[2])
    for _ in range(rnd.randint(1, 2)):                         # glints
        x, y = rnd.randrange(S), rnd.randrange(S)
        wrap(px, x, y, w[4])
        wrap(px, x + 1, y, w[3])
    return normalize_to(img, base)


# ---------------------------------------------------------------- generating the grass tiles

def generate():
    """terrain/grass and its wildflower patches (the dirt, sand and water tops are generated by
    placeholder_ground, which uses the tiles above)."""
    from placeholder_nature import GRASS, save, variant_name
    for i in range(4):                                          # one base for every variant, or tiles checker
        save(grass_tile(100 + i, GRASS), variant_name("terrain/grass", i))
    mixes = [
        [("yarrow", 5), ("buttercup", 1)],
        [("buttercup", 4), ("yarrow", 2)],
        [("harebell", 4), ("yarrow", 1)],
        [("heather", 3), ("yarrow", 2), ("buttercup", 1)],
    ]
    # Dense tiles fill a patch; light ones fray its edges. Variant i of each uses the same mix, and the
    # game keeps one variant per patch, so a patch has one colour scheme.
    for i, mix in enumerate(mixes):
        save(flower_tile(200 + i, GRASS, mix), variant_name("terrain/grass_flowers", i))
        save(flower_tile(300 + i, GRASS, mix, density=0.22), variant_name("terrain/grass_flowers_light", i))


if __name__ == "__main__":
    generate()
