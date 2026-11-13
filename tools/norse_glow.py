"""Norse HD-2D art for everything that glows: the campfire, kiln and forge (and their per-rock
variants), the ember crystal, the ley stone and its plain sibling, and moonpetal.

The rule from docs/ART.md: glow colours stay separate and bright, outside any tint and outside the
Nordic grade, so bloom catches them and they read at night. Everything else here is built from
hue-shifted ramps in the final palette, so these sprites are saved as they are (no ``nordic_finish``).
Base colours borrowed from the older placeholder modules are graded once on the way in (``graded``) so
the stone and wood match the graded world around them.
"""
import math
import os
import random

from PIL import Image

from norse_art import ramp, mask_of, shade_shape, shade_over, selective_outline, bayer, mix, nordic_finish, ribbon
from norse_stone import ROCKS, rock_ramp

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
S = 32
LEVEL = 64   # how tall a level looks in the game (WorldScale.LevelHeight x 32): block faces drawn this tall show once

# Fire: the outer flame, the hot inner flame, the white core. Deep embers are the dull red underneath.
GLOW = (255, 138, 38, 255)
HOT = (255, 214, 96, 255)
CORE = (255, 248, 214, 255)
EMBER = (214, 74, 30, 255)
SOOT = (34, 26, 28, 255)
# Rune light (ley lines, moonpetal): cold blue-white.
RUNE = (140, 214, 255, 255)
RUNE_CORE = (232, 248, 255, 255)
GLOWS = (GLOW, HOT, CORE, RUNE, RUNE_CORE)


def graded(c):
    """A pre-grade base colour as the Nordic grade leaves it (for ramps that sit beside graded art)."""
    return nordic_finish(Image.new("RGBA", (1, 1), tuple(c[:3]) + (255,))).getpixel((0, 0))[:3]


def canvas(w=S, h=S):
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))


def put(img, x, y, c):
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((int(x), int(y)), c)


def save(img, name):
    path = os.path.join(ROOT, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


WALNUT = ramp((108, 80, 56), 5, dark=0.45, light=1.4)
STEEL = ramp((150, 158, 176), 5, dark=0.45, light=1.35)
BRONZE = ramp((178, 126, 64), 4)


# ---------------------------------------------------------------- campfire (toolbar icon, one per wood)

WOODS = {   # bark, bark mark, end grain (pre-grade, from placeholder_wood.WOODS)
    "": ((96, 72, 50), (70, 52, 36), (186, 142, 92)),
    "_oak": ((96, 86, 74), (66, 58, 50), (196, 150, 98)),
    "_pine": ((134, 80, 54), (96, 54, 36), (232, 196, 140)),
    "_birch": ((236, 232, 222), (46, 42, 40), (220, 190, 140)),
}


def flame(img, cx, base, height, half):
    """A layered flame: tongues of GLOW, a HOT heart, a white core at the root, sparks above."""
    def tongue(scale, lean=0.0):
        h, w = height * scale, half * scale
        return [(cx - w, base), (cx - w * 0.9, base - h * 0.35), (cx - w * 0.35 + lean, base - h * 0.7),
                (cx - w * 0.5 + lean, base - h * 0.55), (cx + lean, base - h),
                (cx + w * 0.45 + lean, base - h * 0.6), (cx + w * 0.3, base - h * 0.78),
                (cx + w * 0.95, base - h * 0.3), (cx + w, base)]
    for pts, col in ((tongue(1.0), GLOW), (tongue(0.68, -0.5), HOT), (tongue(0.36), CORE)):
        img.paste(col, mask=mask_of(img.size, lambda d, pts=pts: d.polygon(pts, fill=255)))
    for x, y in ((cx - half * 0.6, base - height - 2), (cx + half * 0.7, base - height * 0.9 - 3), (cx + 1, base - height - 5)):
        put(img, round(x), round(y), HOT)


def campfire(suffix):
    bark, mark, end = (graded(c) for c in WOODS[suffix])
    b = ramp(bark, 5, dark=0.45, light=1.35)
    e = ramp(end, 4, main=1, dark=0.6, light=1.2)
    stone = rock_ramp(graded(ROCKS["rough"]["base"]))[1:6]
    img = canvas()
    stones = [(16 + 12 * math.cos(a), 22 + 6 * math.sin(a)) for a in (math.radians(d) for d in range(0, 360, 40))]
    back = [p for p in stones if p[1] < 22]
    front = [p for p in stones if p[1] >= 22]

    def ring(pts):
        return mask_of(img.size, lambda d: [d.ellipse([x - 3.2, y - 2.4, x + 3.2, y + 2.4], fill=255) for x, y in pts])
    shade_shape(img, ring(back), stone, radius=1.0, slope=1.6, depth=0.5)
    put(img, 16, 23, EMBER)                                                         # glowing bed between the logs
    for x in range(11, 22):
        put(img, x, 22 + (x % 2), EMBER if x % 3 else GLOW)
    for a, z in (((6, 26), (25, 16)), ((7, 16), (26, 26))):                          # two crossed logs
        m = mask_of(img.size, lambda d, a=a, z=z: d.polygon(ribbon([a, ((a[0] + z[0]) / 2, (a[1] + z[1]) / 2), z], [4.4, 4.6, 4.4]), fill=255))
        shade_over(img, m, b, SOOT, radius=1.0, slope=1.4, tex=lambda x, y: 0.12 if (x * 3 + y) % 7 == 0 else 0)
        for ex, ey in (a, z):                                                       # end grain
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    if abs(dx) + abs(dy) < 2:
                        put(img, ex + dx, ey + dy, e[2] if (dx, dy) != (0, 0) else e[0])
            put(img, ex - 1, ey - 1, e[3])
    if suffix == "_birch":                                                          # birch's black lenticels
        for x, y in ((10, 23), (14, 21), (19, 19), (12, 18), (17, 21), (22, 23)):
            put(img, x, y, (46, 42, 44, 255))
    flame(img, 16, 21, 15, 5.0)
    shade_shape(img, ring(front), stone, radius=1.0, slope=1.6, depth=0.5)
    for x, y in front:                                                              # the fire's light on the near stones' tops
        put(img, round(x), round(y) - 2, mix(stone[4][:3], GLOW[:3], 0.35))
    return selective_outline(img, keep=GLOWS)


# ---------------------------------------------------------------- kiln and forge (block faces, one per rock)

def courses(px, r, rows, rnd, w_range=(6, 10), gap=None, x0=0, x1=S, y0=0, ymax=S):
    """Rough stones laid in courses: each stone lit on its upper-left, shadowed lower-right, with a dark
    clay joint between them."""
    gap = gap or r[0]
    y = y0
    for k, h in enumerate(rows):
        x = x0 - rnd.randint(0, w_range[0])
        while x < x1:
            w = rnd.randint(*w_range)
            t = 3 + rnd.uniform(-0.8, 0.8)
            for yy in range(y, min(ymax, y + h)):
                for xx in range(max(x0, x), min(x1, x + w)):
                    if yy == y + h - 1 or xx == x + w - 1:
                        px[xx, yy] = gap
                        continue
                    v = t + bayer(xx, yy) * 0.6
                    if yy == y or xx == x:
                        v += 1.4
                    elif yy == y + h - 2 or xx == x + w - 2:
                        v -= 1.2
                    # round the corners off
                    corner = (xx in (x, x + w - 2)) and (yy in (y, y + h - 2))
                    px[xx, yy] = gap if corner else r[max(1, min(6, int(round(v))))]
            x += w
        y += h


def fire_mouth(px, cx, floor, half, top):
    """The glowing inside of an oven: white-hot at the fire bed, through yellow and orange to dull red,
    soot black under the crown, with dark coals sitting in the bed."""
    for y in range(top, floor + 1):
        for x in range(cx - half, cx + half + 1):
            dist = math.hypot((x - cx) / (half + 0.5), (y - floor) / max(1, floor - top + 0.5))
            c = CORE if dist < 0.28 else HOT if dist < 0.5 else GLOW if dist < 0.74 else EMBER if dist < 0.9 else SOOT
            px[x, y] = c
    for x in range(cx - half + 1, cx + half, 3):                                    # coals
        px[x, floor] = SOOT
        px[x + 1, floor] = (70, 40, 34, 255)
        px[x, floor - 1] = (60, 34, 30, 255)


def kiln(rock):
    """A kiln's face, as tall as a level looks (32x64): coursed stones, a ring of wedge stones round an
    arched mouth with the fire glowing inside it, soot licking up the face above, a smoke vent near the
    crown."""
    r = rock_ramp(graded(ROCKS[rock]["base"]))
    rnd = random.Random(sum(map(ord, rock)) + 11)
    H = LEVEL
    img = Image.new("RGBA", (S, H))
    px = img.load()
    courses(px, r, [5, 6, 6, 5, 5, 5] * 2, rnd, ymax=H)
    cx, cy, rad = 16, 44, 10                                                         # the arch: a semicircle over upright jambs
    for y in range(cy - rad - 3, H):
        for x in range(cx - rad - 3, cx + rad + 4):
            if not (0 <= x < S and 0 <= y < H):
                continue
            d = math.hypot(x - cx, max(0, cy - y))
            if d <= rad - 0.5:
                continue
            if d <= rad + 2.6:                                                       # voussoirs: wedges round the arch
                ang = math.atan2(max(0, cy - y), x - cx)
                seam = (abs((ang * 180 / math.pi) % 30 - 15) < 2.2 and y < cy) or (y >= cy and (y - cy) % 5 == 4)
                lit = (x - cx) < 0 and y < cy + 2
                px[x, y] = r[0] if seam else r[5 if lit and d > rad + 1.5 else 4 if lit else 3]
    fire_mouth(px, cx, H - 2, rad - 1, cy - rad + 1)
    for x in range(cx - rad + 1, cx + rad):                                          # the hearth sill
        px[x, H - 1] = r[2] if x % 4 else r[1]
    for x in range(13, 20):                                                          # the smoke vent, warm inside
        for y in range(6, 10):
            px[x, y] = SOOT if y < 8 or x in (13, 19) else EMBER
        px[x, 5] = r[5]
    for y in range(0, cy - rad - 3):                                                 # soot above the mouth
        for x in range(cx - 4, cx + 5):
            k = 0.55 * (1 - abs(x - cx) / 5) * (1 - y / (cy - rad))
            if k > 0.1 and bayer(x, y) + 0.5 < k * 1.6:
                px[x, y] = mix(px[x, y][:3], SOOT[:3], 0.5)
    return img


BRICK = (150, 66, 46)


def forge(rock):
    """A brick forge: fired brick courses in pale lime, a stone lintel and plinth in the rock's kind, an
    iron-framed hearth with a glowing coal bed and fire tongues licking up from it."""
    r = rock_ramp(graded(ROCKS[rock]["base"]))
    br = ramp(graded(BRICK), 5, dark=0.5, light=1.35, cool=20, warm=12)
    mortar = (196, 186, 170, 255)
    iron = ramp((70, 74, 88), 4, main=1, dark=0.6, light=1.7)
    H = LEVEL                                                                        # a face as tall as a level looks
    img = Image.new("RGBA", (S, H))
    px = img.load()
    for row in range(0, H // 4):                                                     # bricks, 8x4, staggered
        y0 = row * 4
        off = 0 if row % 2 == 0 else 4
        for x in range(S):
            for y in range(y0, y0 + 4):
                bx = (x + off) % 8
                if y == y0 + 3 or bx == 7:
                    px[x, y] = mortar
                    continue
                t = 2 + ((x + off) // 8 * 5 + row * 3) % 3 * 0.45 - 0.45 + bayer(x, y) * 0.5
                if y == y0:
                    t += 1.2
                elif y == y0 + 2 or bx == 6:
                    t -= 0.8
                px[x, y] = br[max(0, min(4, int(round(t))))]
    for y in range(0, 4):                                                            # stone cap
        for x in range(S):
            v = 3 + bayer(x, y) * 0.6 + (1.5 if y == 0 else -1.4 if y == 3 else 0)
            px[x, y] = r[0] if x % 11 == 10 else r[max(1, min(6, int(round(v))))]
    for y in range(H - 4, H):                                                        # stone plinth
        for x in range(S):
            v = 3 + bayer(x, y) * 0.6 + (1.5 if y == H - 4 else -1.4 if y == H - 1 else 0)
            px[x, y] = r[0] if (x + 5) % 13 == 12 else r[max(1, min(6, int(round(v))))]
    x0, x1, y0, y1 = 6, 25, 32, 57                                                   # the hearth, low in the face
    for x in range(x0 - 1, x1 + 2):                                                  # lintel stone
        for y in range(y0 - 3, y0):
            v = 3 + (1.5 if y == y0 - 3 else -1.4 if y == y0 - 1 else 0) + bayer(x, y) * 0.5
            px[x, y] = r[max(1, min(6, int(round(v))))]
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            px[x, y] = SOOT if y < y0 + 4 else (52, 30, 28, 255)
    for x in range(x0 + 1, x1):                                                      # fire tongues over the bed
        h = 3 + round(2.5 * (1 + math.sin(x * 1.3))) if x0 + 2 < x < x1 - 2 else 1
        for k in range(h):
            y = y1 - 4 - k
            px[x, y] = HOT if k < h // 2 and x0 + 4 < x < x1 - 4 else GLOW if k < h - 1 else EMBER
    for x in range(x0, x1 + 1):                                                      # the coal bed
        for y in range(y1 - 3, y1 + 1):
            h = (x * 73856093 ^ y * 19349663) & 0xFF                                  # irregular lumps, fixed per pixel
            lump = h < 120 or (y == y1 and h < 200)
            px[x, y] = (40, 32, 36, 255) if lump else (HOT if h > 225 else GLOW)
        if (x * 7) % 5 == 0:
            px[x, y1 - 3] = CORE
    for y in range(y0, y1 + 1):                                                      # iron jambs
        px[x0 - 1, y] = iron[2]
        px[x1 + 1, y] = iron[0]
    for x in range(x0 - 1, x1 + 2):
        px[x, y1 + 1] = iron[1]
        px[x, y0] = iron[3] if x % 4 else iron[1]
    for x, y in ((x0 - 1, y0 + 2), (x1 + 1, y0 + 2), (x0 - 1, y1 - 2), (x1 + 1, y1 - 2)):
        px[x, y] = iron[3]
    for x in range(x0 - 2, x1 + 3):                                                  # firelight on the brick round the hearth
        for y in (y0 - 4, y1 + 2):
            if 0 <= y < H and px[x, y] != mortar:
                px[x, y] = mix(px[x, y][:3], GLOW[:3], 0.18)
    return img


def oven_top(rock, brick=False):
    """The top of a kiln or forge block (<sprite>_top): its crown of stone or brick round a flue, the
    fire's glow showing down it."""
    r = rock_ramp(graded(ROCKS[rock]["base"]))
    img = Image.new("RGBA", (S, S))
    px = img.load()
    if brick:
        br = ramp(graded(BRICK), 5, dark=0.5, light=1.35, cool=20, warm=12)
        for y in range(S):
            for x in range(S):
                row = y // 4
                bx = (x + (4 if row % 2 else 0)) % 8
                if y % 4 == 3 or bx == 7:
                    px[x, y] = (196, 186, 170, 255)
                else:
                    t = 2 + bayer(x, y) * 0.5 + (1.2 if y % 4 == 0 else -0.7 if y % 4 == 2 else 0)
                    px[x, y] = br[max(0, min(4, int(round(t))))]
    else:
        courses(px, r, [5, 6, 5, 6, 5, 5], random.Random(sum(map(ord, rock)) + 12))
    for y in range(S):                                                               # a raised rim round the flue
        for x in range(S):
            d = math.hypot(x - 15.5, y - 15.5)
            if d < 5.5:
                px[x, y] = SOOT if d > 3.4 else EMBER if d > 2.2 else GLOW
            elif d < 7.5:
                px[x, y] = r[5] if (x - 15.5) + (y - 15.5) < 0 else r[2]
    return img


# ---------------------------------------------------------------- ember crystal (item)

def ember_crystal():
    """A cluster of ember crystal on a basalt chip: prisms with a lit face, a deeper face and a white-hot
    core line, tips burning bright."""
    img = canvas()
    bas = rock_ramp(graded(ROCKS["basalt"]["base"]))[1:6]
    shade_shape(img, mask_of(img.size, lambda d: d.polygon([(4, 28), (7, 23), (14, 21), (24, 22), (29, 26), (26, 30), (8, 30)], fill=255)),
                bas, radius=1.2, slope=1.6)
    lit, deep = (255, 176, 74, 255), (206, 76, 34, 255)
    for (bx, by, tx, ty, w) in ((9, 26, 11, 11, 3), (15, 25, 17, 4, 4), (21, 26, 24, 12, 3), (12, 27, 7, 17, 2), (24, 27, 27, 19, 2)):
        left = [(bx - w, by), (tx - 1, ty + 2), (tx, ty), (bx, by)]
        right = [(bx, by), (tx, ty), (tx + 1, ty + 2), (bx + w, by)]
        img.paste(lit, mask=mask_of(img.size, lambda d, p=left: d.polygon(p, fill=255)))
        img.paste(deep, mask=mask_of(img.size, lambda d, p=right: d.polygon(p, fill=255)))
        for k in range(1, 7):                                                        # the hot core line
            t = k / 8
            put(img, round(bx + (tx - bx) * t - 0.5), round(by + (ty - by) * t), CORE if k > 4 else HOT)
        put(img, tx, ty, CORE)
    return selective_outline(img, keep=(lit, CORE, HOT))


# ---------------------------------------------------------------- standing stones

RUNES = {   # Elder Futhark, 3x5: fehu, raido, algiz, othala, sowilo, tiwaz
    "f": ["x..", "x.x", "xx.", "x.x", "x.."],
    "r": ["xx.", "x.x", "xx.", "x.x", "x.x"],
    "z": ["x.x", "x.x", ".x.", ".x.", ".x."],
    "o": [".x.", "x.x", ".x.", "x.x", "x.x"],
    "s": ["..x", ".x.", "x.x", ".x.", "x.."],
    "t": [".x.", "xxx", "x.x", ".x.", ".x."],
}


LEY = [(9, 31), (10, 18), (9, 9), (13, 2), (16, 0), (20, 3), (23, 10), (22, 20), (24, 31)]
STANDING = [(8, 31), (9, 20), (11, 9), (15, 3), (19, 4), (21, 9), (22, 18), (24, 31)]


def menhir(seed, pts, runes=None, moss=0.5):
    rnd = random.Random(seed)
    img = canvas()
    st = rock_ramp(graded((118, 120, 132)))[1:7]
    m = mask_of(img.size, lambda d: d.polygon(pts, fill=255))

    def tex(x, y):
        return bayer(x, y) * 0.08 + (0.1 if ((x * 7 + y * 3) % 23) == 0 else 0)
    shade_shape(img, m, st, radius=1.3, slope=1.9, depth=0.6, tex=tex)
    px = img.load()
    mk = m.load()
    lich = ((200, 186, 112, 255), (176, 186, 160, 255))
    for _ in range(9):                                                               # lichen rosettes
        x, y = rnd.randrange(S), rnd.randrange(S)
        if mk[x, y] and mk[min(31, x + 1), y]:
            px[x, y] = lich[rnd.randrange(2)]
            px[x + 1, y] = lich[0]
    g = ramp(graded((90, 132, 66)), 5, main=2, dark=0.5, light=1.5, cool=16, warm=24)
    for x in range(S):                                                               # moss creeping up the foot, and turf
        if not mk[x, 31]:
            continue
        h = round(rnd.uniform(1, 5) * moss) + 1
        for k in range(h):
            if mk[x, 31 - k]:
                px[x, 31 - k] = g[1 + (k + x) % 3] if k < h - 1 else g[3]
    if runes:
        y = 4
        for ch in runes:
            glyph = RUNES[ch]
            gx = 15
            for j, row in enumerate(glyph):
                for i, c in enumerate(row):
                    if c != "x":
                        continue
                    x, yy = gx + i, y + j
                    if x + 1 < S and yy + 1 < S and mk[x + 1, yy + 1] and px[x + 1, yy + 1] not in (RUNE, RUNE_CORE):  # the carved groove's shadow
                        px[x + 1, yy + 1] = st[0]
                    px[x, yy] = RUNE_CORE if (i + j) % 3 == 0 else RUNE
            for j in range(-1, 6):                                                    # a faint cold sheen on the stone round it
                for i in range(-1, 4):
                    x, yy = gx + i, y + j
                    if mk[x, yy] and px[x, yy] not in (RUNE, RUNE_CORE, st[0]):
                        px[x, yy] = mix(px[x, yy][:3], RUNE[:3], 0.22)
            y += 7
    return selective_outline(img, keep=(RUNE, RUNE_CORE))


# ---------------------------------------------------------------- moonpetal

PETAL = ((150, 176, 232, 255), (196, 216, 252, 255), (236, 244, 255, 255))


def bloom(img, x, y, r=3):
    """A five-petalled moonpetal bloom: pale blue petals lit from the top left, a pale gold eye, a
    dithered halo of rune light round it."""
    for k in range(5):
        a = -math.pi / 2 + k * math.tau / 5
        px_, py_ = x + math.cos(a) * r * 0.7, y + math.sin(a) * r * 0.7
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                if abs(dx) + abs(dy) <= 1:
                    lit = (dx + dy) < 0 or math.cos(a) * -0.55 + math.sin(a) * -0.83 > 0.3
                    put(img, round(px_ + dx), round(py_ + dy), PETAL[2] if lit and dx + dy < 0 else PETAL[1] if lit else PETAL[0])
    put(img, x, y, (250, 232, 160, 255))
    put(img, x - 1, y - 1, RUNE_CORE)


def halo(img, x, y, r):
    for dy in range(-r - 1, r + 2):
        for dx in range(-r - 1, r + 2):
            d = math.hypot(dx, dy)
            if r - 0.5 < d < r + 1.2 and bayer(x + dx, y + dy) > 0.05 and img.getpixel((min(31, max(0, x + dx)), min(31, max(0, y + dy))))[3] == 0:
                put(img, x + dx, y + dy, RUNE)


def moonpetal(item=False):
    img = canvas()
    stem = ramp(graded((62, 110, 70)), 4, main=1, dark=0.6, light=1.4)
    heads = ((11, 18), (21, 14)) if item else ((10, 13), (21, 10), (16, 18))
    for i, (x, y) in enumerate(heads):                                               # stems, curving to the root
        root = (x + 4, 27) if item else (15 + i, 31)
        m = mask_of(img.size, lambda d, x=x, y=y, root=root: d.line([(x, y + 2), ((x + root[0]) / 2 + 1, (y + root[1]) / 2), root], fill=255, width=1))
        img.paste(stem[1], mask=m)
    if not item:                                                                     # lance leaves at the foot
        for pts in (((15, 31), (9, 25), (12, 25), (16, 30)), ((17, 31), (23, 24), (21, 27), (18, 31))):
            shade_shape(img, mask_of(img.size, lambda d, p=pts: d.polygon(p, fill=255)), stem, radius=0.8, slope=1.3)
    for x, y in heads:
        bloom(img, x, y)
    img = selective_outline(img, keep=PETAL + (RUNE, RUNE_CORE, (250, 232, 160, 255)))
    return img


# ---------------------------------------------------------------- generating

def generate():
    for suffix in WOODS:
        save(campfire(suffix), "buildings/campfire" + suffix)
    for rock, suffix in (("rough", ""), ("sandstone", "_sandstone"), ("limestone", "_limestone"), ("granite", "_granite"), ("slate", "_slate"), ("basalt", "_basalt")):
        save(kiln(rock), "buildings/kiln" + suffix)
        save(forge(rock), "buildings/forge" + suffix)
        save(oven_top(rock), "buildings/kiln" + suffix + "_top")
        save(oven_top(rock, brick=True), "buildings/forge" + suffix + "_top")
    save(ember_crystal(), "items/ember_crystal")
    save(menhir(5, LEY, runes="fzot", moss=0.6), "things/ley_stone")
    save(menhir(6, STANDING, moss=1.0), "things/standing_stone")
    save(moonpetal(), "things/moonpetal")
    save(moonpetal(item=True), "items/moonpetal")


if __name__ == "__main__":
    generate()
