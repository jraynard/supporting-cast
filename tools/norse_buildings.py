"""Norse HD-2D buildings: log and stave walls, plank floors, doors with scrolled iron straps, stairs,
ladders, beds, stone walls of every rock, the furniture models' face textures (props/) and the toolbar
icons for furniture and workstations.

Wood is built per species from hue-shifted ramps (graded once from the placeholder base colours so it sits
with the graded world): long grain streaks, a lit leading edge and a shadowed seam on every board, moss
caulking between wall logs, carved interlace on stave rails. Every file keeps the contract of the drawing it
replaces (sizes, transparent areas, the slope/stair side wedge), so nothing in the defs changes. Saved as is,
never through ``nordic_finish``.
"""
import math
import os
import random

from PIL import Image

from norse_art import ramp, mask_of, shade_shape, shade_over, selective_outline, bayer, mix, ribbon, nordic_finish
from norse_stone import ROCKS, rock_ramp

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
S = 32
LEVEL = 64   # how tall a level looks in the game (src/HeroesNotIncluded/Rendering/WorldScale.cs): faces drawn this tall show once


def graded(c):
    return nordic_finish(Image.new("RGBA", (1, 1), tuple(c[:3]) + (255,))).getpixel((0, 0))[:3]


def save(img, name):
    path = os.path.join(ROOT, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def canvas(w=S, h=S, fill=(0, 0, 0, 0)):
    return Image.new("RGBA", (w, h), fill)


def put(img, x, y, c):
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((int(x), int(y)), c)


def clampi(v, n):
    return max(0, min(n - 1, int(round(v))))


# ---------------------------------------------------------------- materials

class Wood:
    """A species: ``w`` the planed wood (6 steps), ``bark`` (5), ``end`` the end grain (4), and flags."""

    def __init__(self, base, bark, end, knots=False, birch=False):
        self.w = ramp(graded(base), 6, main=3, dark=0.42, light=1.28, cool=22, warm=10)
        self.bark = ramp(graded(bark), 5, dark=0.45, light=1.35)
        self.end = ramp(graded(end), 4, main=1, dark=0.62, light=1.18)
        self.knots = knots
        self.birch = birch


WOODS = {   # pre-grade colours from placeholder_wood.WOODS
    "": Wood((140, 100, 60), (96, 72, 50), (186, 142, 92)),
    "_oak": Wood((146, 100, 60), (96, 86, 74), (196, 150, 98)),
    "_pine": Wood((206, 162, 102), (134, 80, 54), (232, 196, 140), knots=True),
    "_birch": Wood((226, 208, 172), (236, 232, 222), (220, 190, 140), birch=True),
}

IRON = ramp((66, 70, 86), 5, main=1, dark=0.6, light=2.0, cool=12, warm=4)
BRONZE = ramp((178, 126, 64), 4)
MOSS = ramp(graded((96, 116, 62)), 4, main=1, dark=0.6, light=1.35)
WOAD = ramp((62, 92, 152), 5, dark=0.5, light=1.35)
MADDER = ramp((156, 60, 52), 5, dark=0.5, light=1.3)
OAT = ramp((224, 208, 176), 4, main=2, dark=0.7, light=1.08)
LINEN = ramp((232, 224, 204), 4, main=2, dark=0.72, light=1.06)
FLEECE = ramp((226, 218, 196), 4, main=2, dark=0.62, light=1.08)
STRAW = ramp((206, 176, 100), 5, dark=0.55, light=1.25)
LEATHER = ramp((132, 88, 54), 5, dark=0.5, light=1.35)


# ---------------------------------------------------------------- boards

def board(img, x0, y0, x1, y1, wood, rnd, horizontal, tone=3.0, seam=True):
    """One board: a ramp step, long wavy grain streaks with the occasional pale one, a lit leading edge,
    a darker trailing edge and a seam line, pine knots."""
    w = wood.w
    px = img.load()
    length = (x1 - x0) if horizontal else (y1 - y0)
    across = (y1 - y0) if horizontal else (x1 - x0)
    streaks = [(rnd.randint(1, max(1, across - 1)), rnd.uniform(0, math.tau), rnd.choice((-1.1, -1.1, 0.8))) for _ in range(max(1, across // 2))]
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            a, o = (x - x0, y - y0) if horizontal else (y - y0, x - x0)
            v = tone + bayer(x, y) * 0.35
            for off, ph, dv in streaks:
                if o == round(off + 0.7 * math.sin(a * 0.33 + ph)):
                    v += dv
            if o == 0:
                v += 1.2
            elif o == across - 1:
                v -= 1.0
            if seam and o == across:
                px[x, y] = w[0]
                continue
            px[x, y] = w[clampi(v, 6)]
    if wood.knots and rnd.random() < 0.6 and length > 6 and across > 2:
        a = rnd.randint(2, max(2, length - 3))
        o = rnd.randint(1, max(1, across - 2))
        kx, ky = (x0 + a, y0 + o) if horizontal else (x0 + o, y0 + a)
        put(img, kx, ky, w[0]); put(img, kx + (1 if horizontal else 0), ky + (0 if horizontal else 1), w[1])
        put(img, kx - (1 if horizontal else 0), ky - (0 if horizontal else 1), w[4])


def nail(img, x, y):
    put(img, x, y, IRON[1]); put(img, x - 1, y - 1, IRON[3])


# ---------------------------------------------------------------- walls

def log_wall(wood, seed):
    """A wall of round logs laid horizontally (four courses), lit along their top curve and shadowed
    under it, with moss caulking pressed into the joints and adze marks along the faces."""
    rnd = random.Random(seed)
    img = canvas()
    px = img.load()
    w = wood.w
    curve = (4.6, 4.3, 3.8, 3.3, 2.8, 2.2, 1.4, 0.4)       # tone by row within a course: a round log against the light
    for course in range(4):
        y0 = course * 8
        ph = rnd.uniform(0, math.tau)
        for y in range(y0, y0 + 8):
            r = y - y0
            for x in range(S):
                v = curve[r] + bayer(x, y) * 0.4
                if r in (2, 5) and math.sin(x * 0.4 + ph + r) > 0.75:
                    v -= 1.0                              # grain streak
                px[x, y] = w[clampi(v, 6)]
        for _ in range(4):                                # adze marks: short scallops in the face
            x, y = rnd.randrange(S), y0 + rnd.randint(2, 4)
            for k in range(3):
                put(img, (x + k) % S, y, w[2]); put(img, (x + k) % S, y - 1, w[4])
        if wood.birch:                                    # birch keeps strips of its bark
            for _ in range(3):
                x = rnd.randrange(S)
                for k in range(rnd.randint(3, 7)):
                    put(img, (x + k) % S, y0 + 6, wood.bark[3])
                    if k % 3 == 0:
                        put(img, (x + k) % S, y0 + 6, (52, 48, 50, 255))
        for x in range(S):                                # moss caulking in the joint below
            if (x * 7 + course * 3) % 5 < 3:
                put(img, x, y0 + 7, MOSS[1] if (x + course) % 3 else MOSS[2])
    return img


def stave_wall(wood, seed):
    """Upright staves (half-round, lit on the left) with a carved rail across: two strands of interlace
    crossing over and under, iron nails pinning the rail."""
    rnd = random.Random(seed)
    img = canvas()
    w = wood.w
    for col in range(8):
        board(img, col * 4, 0, col * 4 + 3, S - 1, wood, rnd, False, tone=3 + rnd.uniform(-0.4, 0.4))
    px = img.load()
    for y in range(13, 20):                               # the rail
        for x in range(S):
            v = 3.6 + bayer(x, y) * 0.35 + (1.2 if y == 13 else -1.4 if y == 19 else -0.6 if y == 18 else 0)
            px[x, y] = w[clampi(v, 6)]
    for x in range(S):                                    # interlace: two strands, over and under
        wave = math.sin(x * math.tau / 16)
        a, b = round(16 + 1.6 * wave), round(16 - 1.6 * wave)
        over, under = (b, a) if (x // 8) % 2 else (a, b)
        put(img, x, under, w[1])
        put(img, x, under + 1, w[0])
        put(img, x, over - 1, w[5])
        put(img, x, over, w[4])
        put(img, x, over + 1, w[1])
    for x in (2, 14, 26):
        nail(img, x, 14)
        nail(img, x, 18)
    return img


def wood_floor(wood, seed):
    """Wide boards running north-south, staggered end seams, a nail pair at every seam."""
    rnd = random.Random(seed)
    img = canvas()
    for col in range(4):
        x0 = col * 8
        cut = (5, 21, 13, 27)[col]
        board(img, x0, 0, x0 + 7, cut - 1, wood, rnd, False, tone=3 + rnd.uniform(-0.5, 0.5))
        board(img, x0, cut, x0 + 7, S - 1, wood, rnd, False, tone=3 + rnd.uniform(-0.5, 0.5))
        for y in (cut + 1, cut - 2):
            if 0 <= y < S:
                nail(img, x0 + 2, y); nail(img, x0 + 5, y)
        px = img.load()
        for x in range(x0, x0 + 8):
            px[x, cut] = wood.w[0]                        # the end seam
    return img


def plank_floor(wood, seed):
    """Narrow boards laid across, tightly staggered."""
    rnd = random.Random(seed)
    img = canvas()
    for row in range(8):
        y0 = row * 4
        cut = (6, 22, 14, 28, 10, 18, 26, 4)[row]
        board(img, 0, y0, cut - 1, y0 + 3, wood, rnd, True, tone=3 + rnd.uniform(-0.5, 0.5))
        board(img, cut, y0, S - 1, y0 + 3, wood, rnd, True, tone=3 + rnd.uniform(-0.5, 0.5))
        img.putpixel((cut, y0 + 1), wood.w[0]); img.putpixel((cut, y0 + 2), wood.w[0])
    return img


def stone_wall(rock, kind):
    """Stone walls in a rock's own ramp. ``dry``: irregular rubble, chinked with small stones;
    ``cut``: squared ashlar with tight dark joints; ``mortared``: ashlar set in pale lime mortar."""
    r = rock_ramp(graded(ROCKS[rock]["base"]))
    rnd = random.Random(sum(map(ord, rock + kind)))
    img = canvas()
    px = img.load()
    if kind == "dry":
        rows, y = [], 0
        while y < S:
            h = rnd.choice((6, 7, 8, 9))
            h = min(h, S - y)
            rows.append((y, h))
            y += h
        for y0, h in rows:
            x = -rnd.randint(0, 6)
            while x < S:
                w_ = rnd.randint(6, 13)
                t = 3 + rnd.uniform(-1.0, 1.0)
                bulge = [round(rnd.uniform(-0.6, 0.6)) for _ in range(4)]
                for yy in range(y0, y0 + h):
                    for xx in range(max(0, x), min(S, x + w_)):
                        ry, rx = yy - y0, xx - x
                        corner = (rx in (0, w_ - 1)) and (ry in (0, h - 1))
                        if ry == h - 1 or rx == w_ - 1 or corner:
                            px[xx, yy] = r[0]
                            continue
                        v = t + bayer(xx, yy) * 0.6 - (rx + ry) / 22
                        if ry == 0 or rx == 0:
                            v += 1.4
                        elif ry == h - 2 or rx == w_ - 2:
                            v -= 1.3
                        px[xx, yy] = r[clampi(v, 7)]
                x += w_
        for _ in range(6):                                # chinking: little wedge stones in the joints
            x, y = rnd.randrange(S), rnd.choice([y0 + h - 1 for y0, h in rows])
            put(img, x, y, r[4]); put(img, x + 1, y, r[2])
        return img
    mortar = (206, 196, 178, 255) if kind == "mortared" else r[0]
    hi = 8 if kind == "cut" else 11
    for row in range(0, S // hi + 1):
        y0 = row * hi
        off = 0 if row % 2 == 0 else 8
        for y in range(y0, min(S, y0 + hi)):
            for x in range(S):
                bx = (x + off) % 16
                ry = y - y0
                if ry == hi - 1 or bx == 15:
                    px[x, y] = mortar
                    continue
                block = ((x + off) // 16 + row * 3) % 4
                v = 3 + (block - 1.5) * 0.35 + bayer(x, y) * 0.55
                if ry == 0 or bx == 0:
                    v += 1.3
                elif ry == hi - 2 or bx == 14:
                    v -= 1.2
                if kind == "cut" and (x * 3 + y * 5) % 11 == 0:
                    v -= 0.8                              # tooling marks
                px[x, y] = r[clampi(v, 7)]
    if kind == "mortared":
        for y in range(S):
            for x in range(S):
                if px[x, y] == mortar and (x + y) % 4 == 0:
                    px[x, y] = (178, 168, 152, 255)
    return img


def brick_wall():
    br = ramp(graded((158, 70, 50)), 5, dark=0.5, light=1.35, cool=20, warm=12)
    mortar = (208, 196, 178, 255)
    img = canvas()
    px = img.load()
    for row in range(0, 6):
        y0 = row * 6
        off = 0 if row % 2 == 0 else 6
        for y in range(y0, min(S, y0 + 6)):
            for x in range(S):
                bx = (x + off) % 12
                if y == y0 + 5 or bx == 11:
                    px[x, y] = mortar if (x + y) % 5 else (180, 168, 150, 255)
                    continue
                t = 2 + (((x + off) // 12 * 7 + row * 5) % 3 - 1) * 0.5 + bayer(x, y) * 0.5
                if y == y0:
                    t += 1.2
                elif y == y0 + 4 or bx == 10:
                    t -= 0.9
                px[x, y] = br[clampi(t, 5)]
    return img


# ---------------------------------------------------------------- doors, stairs, ladders, gate

def scroll_strap(img, y, x0=3, x1=30):
    """A forged strap hinge: a bar with a lit top, rivets, and a split scrolled end curling up and down."""
    for x in range(x0, x1 - 3):
        put(img, x, y, IRON[2]); put(img, x, y + 1, IRON[1]); put(img, x, y - 1, IRON[3] if x % 2 else IRON[2])
    ex = x1 - 3
    for dx, dy in ((0, -2), (1, -3), (2, -3), (3, -2), (2, -1), (0, 2), (1, 3), (2, 3), (3, 2), (2, 1)):
        put(img, ex + dx, y + dy, IRON[2])
    put(img, ex + 1, y - 3, IRON[3]); put(img, ex + 1, y + 2, IRON[1])
    for x in range(x0 + 4, x1 - 4, 6):
        put(img, x, y, IRON[4])
    put(img, x0, y - 2, IRON[2]); put(img, x0, y + 2, IRON[1])     # the knuckle at the hinge side


def door(wood, seed):
    """A plank door as tall as a level looks (32x64): four boards, a diagonal brace, two forged strap
    hinges with scrolled ends, a ring pull at hand height, the frame round it."""
    rnd = random.Random(seed)
    H = LEVEL
    img = canvas(S, H)
    for col in range(4):
        board(img, 3 + col * 7, 1, 3 + col * 7 + 6, H - 2, wood, rnd, False, tone=3 + rnd.uniform(-0.4, 0.4))
    w = wood.w
    for y in range(1, H - 1):                             # a diagonal brace behind, seen through as a darker band
        x = 5 + round((H - 2 - y) * 23 / (H - 3))
        for dx in (0, 1):
            put(img, x + dx, y, w[1] if dx else w[2])
    px = img.load()
    for y in range(H):                                    # the frame
        px[2, y] = w[1]; px[31, y] = w[0]
    for x in range(2, 32):
        px[x, 0] = w[4]; px[x, H - 1] = w[0]
    scroll_strap(img, 10)
    scroll_strap(img, H - 12)
    m = mask_of(img.size, lambda d: d.ellipse([22, 30, 28, 36], outline=255, width=1))   # ring pull on its plate
    img.paste(IRON[3], mask=m)
    put(img, 25, 31, IRON[4]); put(img, 25, 35, IRON[0]); put(img, 22, 33, IRON[1])
    put(img, 25, 29, IRON[2]); put(img, 24, 29, IRON[1]); put(img, 26, 29, IRON[1])
    return img


def stairs(wood, seed):
    """Treads along the slope, high end at the top row, lighter toward it; 32x64, since a flight now
    climbs a level that looks 64 px tall. Each tread has a lit nosing and the riser's shadow under it."""
    rnd = random.Random(seed)
    H = LEVEL
    img = canvas(S, H)
    n = 8
    for i, y in enumerate(range(H - 2, -2, -H // n)):
        y0 = max(0, y - (H // n - 1))
        board(img, 3, y0, 28, y, wood, rnd, True, tone=2.0 + i * 0.3)
        for x in range(3, 29):
            put(img, x, y0, wood.w[5])
            if y + 1 < H:
                put(img, x, y + 1, wood.w[0])
        nail(img, 5, y0 + 3); nail(img, 26, y0 + 3)
    for y in range(H):                                    # stringers either side
        for x, c in ((2, wood.w[1]), (29, wood.w[0])):
            put(img, x, y, c)
    return img


def stairs_side(wood, seed):
    """The stringer seen from the side, 32x64, cut to the stepped profile (high end left: four steps
    each 8 px across and 16 down; above each step top is transparent, as the mesher maps it), each
    tread's nosing lit, each riser shadowed."""
    rnd = random.Random(seed)
    H = LEVEL
    img = canvas(S, H)
    for k in range(H // 8):                               # grain along the stringer
        board(img, 0, k * 8, S - 1, k * 8 + 7, wood, rnd, True, tone=2.8 + (k % 2) * 0.4, seam=False)
    px = img.load()
    w = wood.w
    step = H // 4
    for y in range(H):
        for x in range(S):
            top = (x // 8) * step
            if y < top:
                px[x, y] = (0, 0, 0, 0)
            elif y == top:
                px[x, y] = w[5]
            elif y == top + 1:
                px[x, y] = w[2]
            elif x % 8 == 7 and y < top + step:
                px[x, y] = w[0]
    return img


def ladder(wood, seed):
    """Two split rails and rungs lashed on with rawhide, 32x96 (renderSize 3.0): it climbs a level that
    looks 64 px tall and stands proud of the floor above. Billboard sprite, transparent behind."""
    rnd = random.Random(seed)
    H = 96
    img = canvas(S, H)
    w = wood.w
    for x0 in (7, 22):
        m = mask_of(img.size, lambda d, x0=x0: d.rectangle([x0, 0, x0 + 3, H - 1], fill=255))
        shade_shape(img, m, w[1:6], radius=1.0, slope=1.6, tex=lambda x, y: -0.15 if (y * 3 + x) % 9 == 0 else 0)
    for y in range(4, H, 10):
        m = mask_of(img.size, lambda d, y=y: d.rectangle([9, y, 23, y + 2], fill=255))
        shade_over(img, m, w[1:6], w[0], radius=0.8, slope=1.6)
        for x in (9, 10, 21, 22):                         # lashings
            put(img, x, y + rnd.choice((0, 1)), LEATHER[3])
            put(img, x, y + 2, LEATHER[1])
    return selective_outline(img)


def cage_gate():
    """Iron-banded wooden bars, as tall as a level looks (32x64), with gaps you see through (the door
    slab is alpha-tested)."""
    wood = WOODS["_oak"]
    H = LEVEL
    img = canvas(S, H)
    for x in range(2, 32, 6):
        m = mask_of(img.size, lambda d, x=x: d.rectangle([x, 0, x + 2, H - 1], fill=255))
        shade_shape(img, m, wood.w[1:6], radius=0.8, slope=1.6, tex=lambda xx, yy: -0.2 if (yy * 5 + xx) % 11 == 0 else 0)
    for y in (3, 30, H - 6):
        for x in range(S):
            put(img, x, y, IRON[3]); put(img, x, y + 1, IRON[2]); put(img, x, y + 2, IRON[0])
        for x in range(3, 32, 6):
            put(img, x, y + 1, IRON[4])
    return img


def padlock():
    img = canvas(16, 16)
    m = mask_of(img.size, lambda d: d.arc([4, 1, 11, 10], 180, 360, fill=255, width=2))
    img.paste(IRON[3], mask=m)
    for y in range(5, 8):
        put(img, 4, y, IRON[3]); put(img, 5, y, IRON[2]); put(img, 10, y, IRON[2]); put(img, 11, y, IRON[1])
    shade_shape(img, mask_of(img.size, lambda d: d.rounded_rectangle([3, 7, 12, 14], radius=2, fill=255)), BRONZE, radius=0.8, slope=1.5)
    put(img, 7, 10, (40, 30, 20, 255)); put(img, 7, 11, (40, 30, 20, 255)); put(img, 8, 10, (40, 30, 20, 255))
    return selective_outline(img)


# ---------------------------------------------------------------- beds

def blanket_rect(img, x0, y0, x1, y1):
    """A woad wool blanket with a twill weave, a fold across the top and a tablet-woven braid at the foot."""
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            v = 2.4 + (0.45 if (x + y * 2) % 4 == 0 else 0) - (y - y0) / max(1, y1 - y0) * 0.6
            if x == x0 or y == y0:
                v += 0.9
            elif x == x1 or y == y1:
                v -= 1.1
            put(img, x, y, WOAD[clampi(v, 5)])
    for x in range(x0, x1 + 1):                           # the fold
        put(img, x, y0 + 2, WOAD[4]); put(img, x, y0 + 3, WOAD[1])
    for x in range(x0 + 1, x1):                           # braid at the foot: madder and oat chevrons
        c1, c2 = (MADDER[3], OAT[2]) if (x // 2) % 2 == 0 else (OAT[2], MADDER[2])
        put(img, x, y1 - 3, c1); put(img, x, y1 - 2, c2)


def bed(wood, seed):
    """A box bed from above: plank frame with carved bedposts, straw mattress showing at the edges, linen
    pillow, woad blanket with a braid, a fleece thrown over the foot."""
    rnd = random.Random(seed)
    img = canvas()
    board(img, 4, 1, 27, 30, wood, rnd, False)
    w = wood.w
    px = img.load()
    for x in range(4, 28):                                # headboard with a carved band
        for y in range(1, 5):
            px[x, y] = w[clampi(3.6 - (y - 1) * 0.7 + bayer(x, y) * 0.3, 6)]
        put(img, x, 3, w[1] if (x // 2) % 2 else w[5])
    for x, y in ((4, 1), (27, 1), (4, 30), (27, 30)):     # bedposts
        put(img, x, y, w[5] if x == 4 else w[1])
    for y in range(5, 29):
        for x in range(6, 26):
            px[x, y] = STRAW[2 if (x + y) % 3 else 1]
    blanket_rect(img, 6, 12, 25, 28)
    shade_shape(img, mask_of(img.size, lambda d: d.rounded_rectangle([7, 5, 24, 11], radius=2, fill=255)), LINEN, radius=1.2, slope=1.3)
    for x in range(8, 24, 4):
        put(img, x, 8, LINEN[1])
    fleece = mask_of(img.size, lambda d: d.polygon([(14, 22), (26, 19), (27, 28), (16, 29)], fill=255))
    shade_over(img, fleece, FLEECE, FLEECE[0], radius=1.0, slope=1.2, tex=lambda x, y: 0.2 if (x * 2 + y) % 3 == 0 else 0)
    return selective_outline(img)


def bed_blanket():
    """The blanket decal drawn over whoever sleeps in a bed: the same blanket, nothing else."""
    img = canvas()
    blanket_rect(img, 5, 12, 26, 29)
    fleece = mask_of(img.size, lambda d: d.polygon([(14, 23), (26, 20), (27, 29), (16, 29)], fill=255))
    shade_over(img, fleece, FLEECE, FLEECE[0], radius=1.0, slope=1.2, tex=lambda x, y: 0.2 if (x * 2 + y) % 3 == 0 else 0)
    return img


# ---------------------------------------------------------------- props: face textures for box models

PALE = Wood((200, 176, 140), (130, 104, 78), (226, 206, 168))     # neutral: the game tints it by material


def prop_planks():
    rnd = random.Random(3)
    img = canvas()
    for i in range(4):
        y0 = i * 8
        cut = (9, 23, 15, 27)[i]
        board(img, 0, y0, cut - 1, y0 + 7, PALE, rnd, True, tone=3 + (i % 2) * 0.3)
        board(img, cut, y0, S - 1, y0 + 7, PALE, rnd, True, tone=2.8)
        img.load()[cut, y0 + 3] = PALE.w[0]
        nail(img, cut - 2, y0 + 3); nail(img, cut + 2, y0 + 3)
    return img


def prop_log_side():
    """Bark running along a log: furrows, a lit top, a shadowed underside (tinted per wood in game)."""
    b = PALE.bark
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            v = 2.2 + bayer(x, y) * 0.5
            yy = y + round(math.sin(x * math.tau / 16 + (y // 4) * 1.7) * 0.8)     # furrows running along the log
            if yy % 4 == 0:
                v -= 1.2
            elif yy % 4 == 1:
                v += 0.8
            px[x, y] = b[clampi(v, 5)]
    for x in range(S):
        px[x, 0] = b[4]
    return img


def prop_log_end():
    """End grain: bark rim, pale sapwood, growth rings round a slightly off-centre pith, radial checks."""
    img = canvas()
    px = img.load()
    e, b = PALE.end, PALE.bark
    for y in range(S):
        for x in range(S):
            d = math.hypot(x - 15.2, y - 16.6)
            if d > 14.5:
                px[x, y] = b[1] if d > 15.3 else b[2]
                continue
            ring = int(d) % 3 == 0
            v = 2.0 - d / 30 + (-0.9 if ring else 0) + bayer(x, y) * 0.3 - ((x - 16) + (y - 16)) / 60
            px[x, y] = e[clampi(v, 4)]
    for k in range(9):                                    # a radial check
        put(img, 16 + k, 17 - k // 2, e[0])
    put(img, 15, 16, e[0])
    return img


# Log pile (the Wood item's model): each log is a box LOG across and LOG / 2 levels tall, so it
# looks square on screen (levels look twice as tall), and box faces show the top-left of their
# texture at the face's size: an end shows the top-left LOG x LOG pixels, a side LOG rows. The
# art repeats every LOG pixels so any crop of it shows whole logs.
LOG = 8


def prop_woodpile_end():
    """A log's cut end in each LOG x LOG tile: bark rim, pale wood, a ring and the pith, the corners
    in the shadow between logs."""
    img = canvas()
    px = img.load()
    e, b = PALE.end, PALE.bark
    c = (LOG - 1) / 2
    for y in range(S):
        for x in range(S):
            u, v = x % LOG, y % LOG
            d = math.hypot(u - c, v - c)
            if d > c + 0.6:
                px[x, y] = b[0]                                  # the gap between logs
            elif d > c - 0.6:
                px[x, y] = b[1] if (u + v) > LOG - 1 else b[3]   # the bark rim, lit upper left
            else:
                lit = ((c - u) + (c - v)) / (2 * LOG)
                ring = 1.6 < d < 2.4
                px[x, y] = e[clampi(2.2 + lit * 2 - (1.0 if ring else 0) - d / 6, 4)]
    for y in range(0, S, LOG):
        for x in range(0, S, LOG):
            px[x + LOG // 2 - 1, y + LOG // 2 - 1] = e[0]       # the pith
    return img


def prop_woodpile_bark(across):
    """Bark along a log, shaded round it: ``across`` puts the curve across the columns (the top face,
    a log running away from you), else down the rows (a side: lit top, dark underside)."""
    img = canvas()
    px = img.load()
    b = PALE.bark
    shade = [1.2, 2.4, 3.4, 3.6, 3.0, 2.2, 1.3, 0.4]            # across one log, lit from the upper left
    for y in range(S):
        for x in range(S):
            t, along = (x % LOG, y) if across else (y % LOG, x)
            v = shade[t] + bayer(x, y) * 0.4
            k = (along + t * 5) % 7                              # furrows: dark splits and lit ridges in the bark
            if k == 0 and 0 < t < LOG - 1:
                v -= 1.1
            elif k == 3:
                v += 0.4
            px[x, y] = b[clampi(v, 5)]
    return img


def cloth(base_ramp, seed, stripe=None):
    """Tabby-woven cloth, warp and weft picked out in a fixed pattern."""
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            v = 2 + (0.5 if (x % 2 == 0) ^ (y % 2 == 0) else -0.3) + (-0.6 if x % 4 == 3 else 0)
            px[x, y] = base_ramp[clampi(v, len(base_ramp))]
    return img


def prop_blanket():
    """Madder wool with a band of woad and oat braid, a darker hem."""
    img = cloth(MADDER, 7)
    for y in range(10, 15):
        for x in range(S):
            c = (WOAD[3] if y in (10, 14) else OAT[2] if (x // 2 + y) % 2 else WOAD[2])
            put(img, x, y, c)
    for y in range(28, 32):
        for x in range(S):
            put(img, x, y, MADDER[1] if (x + y) % 2 else MADDER[0])
    return img


def prop_stone_block():
    """Squared stones (campfire rings, hearths, benches): blocks with lit tops and a cool shadow side."""
    r = rock_ramp(graded(ROCKS["rough"]["base"]))
    img = canvas()
    px = img.load()
    for row in range(2):
        y0 = row * 16
        off = 0 if row % 2 == 0 else 16
        for y in range(y0, y0 + 16):
            for x in range(S):
                bx = (x + off) % 32
                bx16 = bx % 16 if row else bx % 32
                ry = y - y0
                if ry == 15 or bx in (15, 31):
                    px[x, y] = r[0]
                    continue
                v = 3 + bayer(x, y) * 0.6 - (bx % 16 + ry) / 40
                if ry == 0 or bx in (0, 16):
                    v += 1.3
                elif ry == 14 or bx in (14, 30):
                    v -= 1.2
                if (x * 5 + y * 3) % 17 == 0:
                    v -= 1
                px[x, y] = r[clampi(v, 7)]
    return img


def prop_straw():
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = STRAW[1 if (x + y * 3) % 5 == 0 else 2]
    rnd = random.Random(13)
    for _ in range(70):                                   # stalks: a lit side and a shadow side
        x, y = rnd.randrange(S), rnd.randrange(S)
        dx = rnd.choice((-1, 0, 1))
        for k in range(rnd.randint(3, 6)):
            put(img, (x + dx * k // 2) % S, (y + k) % S, STRAW[4] if k % 2 else STRAW[3])
            put(img, (x + dx * k // 2 + 1) % S, (y + k) % S, STRAW[1])
    return img


def prop_chest(front):
    """An iron-bound chest of pale boards (tinted per wood): bands, rivets, a lock plate with a hasp."""
    img = prop_planks()
    px = img.load()
    for y in range(S):
        put(img, 0, y, PALE.w[1]); put(img, S - 1, y, PALE.w[0])
    for x in range(S):
        put(img, x, 0, PALE.w[4]); put(img, x, S - 1, PALE.w[0])
    if front:
        for x in range(S):
            put(img, x, 4, IRON[3]); put(img, x, 5, IRON[2]); put(img, x, 6, IRON[0])
        for x in (4, 27):
            for y in range(7, S):
                put(img, x, y, IRON[2]); put(img, x + 1, y, IRON[1])
            for y in range(9, S, 6):
                put(img, x, y, IRON[4])
        shade_shape(img, mask_of(img.size, lambda d: d.rectangle([13, 5, 18, 13], fill=255)), BRONZE, radius=0.6, slope=1.4)
        put(img, 15, 9, (40, 30, 20, 255)); put(img, 15, 10, (40, 30, 20, 255)); put(img, 16, 9, (40, 30, 20, 255))
    else:
        for x0 in (4, 25):
            for y in range(S):
                put(img, x0, y, IRON[3]); put(img, x0 + 1, y, IRON[2]); put(img, x0 + 2, y, IRON[0])
            for y in range(3, S, 7):
                put(img, x0 + 1, y, IRON[4])
    return img


def prop_basket():
    """Woven withies: over-under rows with a lit upper edge and a bound rim."""
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            row, col = y // 4, (x + (y // 4) * 3) // 6
            over = (row + col) % 2 == 0
            ry = y % 4
            v = (3.2 if over else 1.6) + (0.8 if ry == 0 else -0.9 if ry == 3 else 0) + bayer(x, y) * 0.3
            px[x, y] = STRAW[clampi(v, 5)]
    for x in range(S):
        for y in range(3):
            put(img, x, y, LEATHER[2 + (1 if y == 0 else 0)] if (x + y) % 3 else LEATHER[1])
    return img


def heap(colors, seed, base, count=30, rmax=4):
    """A heap of round things seen from above (berries, fish): lit tops, shadowed undersides, packed."""
    img = canvas(fill=base)
    rnd = random.Random(seed)
    for _ in range(count):
        x, y, r = rnd.randrange(S), rnd.randrange(S), rnd.randint(2, rmax)
        c = colors[rnd.randrange(len(colors))]
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if dx * dx + dy * dy <= r * r:
                    u = (-dx * 0.55 - dy * 0.83) / r
                    put(img, (x + dx) % S, (y + dy) % S, c[2] if u > 0.45 else c[1] if u > -0.35 else c[0])
        put(img, (x - r // 2) % S, (y - r // 2) % S, (255, 248, 236, 255))
    return img


def prop_fish_top():
    """Fish laid head to tail: silver flanks, a darker back, an eye, a forked tail."""
    img = canvas(fill=(76, 92, 104, 255))
    rnd = random.Random(19)
    body = ramp((128, 150, 164), 4, main=1, dark=0.6, light=1.4)
    for row in range(5):
        y = 3 + row * 7
        x0 = (row * 11) % 32
        flip = row % 2
        for k in range(2):
            cx = (x0 + k * 16) % S
            for dx in range(-6, 7):
                half = round(2.6 * math.sqrt(max(0, 1 - (dx / 6.5) ** 2)))
                for dy in range(-half, half + 1):
                    c = body[3] if dy < -half // 2 else body[1] if dy > half // 2 else body[2]
                    if dy == -half:
                        c = body[0]
                    put(img, (cx + dx) % S, y + dy, c)
            tx = cx + (7 if not flip else -7)
            for dy in (-2, -1, 1, 2):
                put(img, tx % S, y + dy, body[1])
            put(img, (cx + (-4 if not flip else 4)) % S, y - 1, (20, 22, 30, 255))
    return img


def prop_meat_top():
    """Cuts of meat: red muscle with a lit sheen, a cap of fat, a bone end."""
    img = canvas(fill=(110, 40, 40, 255))
    meat = ramp((172, 62, 56), 4, main=1, dark=0.55, light=1.3)
    fat = ((214, 196, 178, 255), (240, 228, 210, 255))
    rnd = random.Random(21)
    for _ in range(9):
        x, y = rnd.randrange(4, 28), rnd.randrange(4, 28)
        m = mask_of(img.size, lambda d, x=x, y=y: d.ellipse([x - 6, y - 4, x + 6, y + 4], fill=255))
        shade_over(img, m, meat, (80, 26, 30, 255), radius=1.2, slope=1.3)
        for k in range(-4, 5):
            put(img, x + k, y - 3 + abs(k) // 3, fat[1] if k < 0 else fat[0])
        put(img, x + 5, y + 1, fat[1])
    return img


def prop_meal_top():
    """Stew in the bowl: brown broth, chunks of meat and root, a green herb, a lit ladle of steam."""
    img = canvas(fill=(150, 88, 50, 255))
    broth = ramp((150, 88, 50), 4, main=1, dark=0.65, light=1.25)
    px = img.load()
    for y in range(S):
        for x in range(S):
            px[x, y] = broth[1 + ((x + y * 2) % 7 == 0) + (math.sin(x * 0.4 + y * 0.3) > 0.7)]
    rnd = random.Random(33)
    for _ in range(14):
        x, y = rnd.randrange(S), rnd.randrange(S)
        c = rnd.choice((((150, 70, 52, 255), (190, 104, 78, 255)), ((214, 168, 92, 255), (240, 204, 130, 255)), ((96, 122, 60, 255), (130, 156, 80, 255))))
        put(img, x, y, c[0]); put(img, x + 1, y, c[0]); put(img, x, y - 1, c[1])
    return img


def prop_hide():
    """A stretched hide, hair side: soft patches with combed hair strokes."""
    img = canvas()
    fur = ramp(graded((150, 108, 72)), 5, dark=0.55, light=1.3)
    px = img.load()
    for y in range(S):
        for x in range(S):
            v = 2 + 0.6 * math.sin(x * 0.3 + y * 0.2) + bayer(x, y) * 0.4
            if (x + y * 2) % 6 == 0:
                v += 1
            elif (x + y * 2) % 6 == 3:
                v -= 1
            px[x, y] = fur[clampi(v, 5)]
    return img


# ---------------------------------------------------------------- toolbar icons

def icon_table():
    img = canvas()
    w = WOODS[""]
    rnd = random.Random(1)
    board(img, 3, 9, 28, 14, w, rnd, True, tone=3.4)
    for x in (5, 25):                                     # trestle legs, splayed
        m = mask_of(img.size, lambda d, x=x: d.polygon([(x, 15), (x + 2, 15), (x + 3 + (1 if x > 16 else -1) * 2, 28), (x + 1 + (1 if x > 16 else -1) * 2, 28)], fill=255))
        shade_over(img, m, w.w[1:6], w.w[0], radius=0.8, slope=1.5)
    for x in range(7, 26):
        put(img, x, 21, w.w[2]); put(img, x, 22, w.w[1])
    for x in range(5, 27, 4):
        put(img, x, 11, w.w[1] if x % 8 else w.w[5])
    return selective_outline(img)


def icon_stool():
    img = canvas()
    w = WOODS[""]
    for a, z in (((11, 15), (8, 28)), ((21, 15), (24, 28)), ((16, 16), (16, 27))):
        m = mask_of(img.size, lambda d, a=a, z=z: d.line([a, z], fill=255, width=2))
        shade_shape(img, m, w.w[1:6], radius=0.7, slope=1.5)
    m = mask_of(img.size, lambda d: d.ellipse([7, 10, 25, 17], fill=255))
    shade_over(img, m, w.w[1:6], w.w[0], radius=1.2, slope=1.4, tex=lambda x, y: -0.3 if (x + y) % 5 == 0 else 0)
    return selective_outline(img)


def icon_chest():
    img = canvas()
    w = WOODS["_oak"]
    rnd = random.Random(5)
    for y0 in (9, 15, 21):
        board(img, 5, y0, 27, y0 + 5, w, rnd, True, tone=3.2)
    m = mask_of(img.size, lambda d: d.chord([5, 4, 27, 16], 180, 360, fill=255))
    shade_over(img, m, w.w[1:6], w.w[0], radius=1.2, slope=1.4)
    for x in range(5, 28):
        put(img, x, 12, IRON[3]); put(img, x, 13, IRON[1])
    for y in range(6, 27):
        put(img, 8, y, IRON[2]); put(img, 24, y, IRON[2])
    shade_shape(img, mask_of(img.size, lambda d: d.rectangle([14, 12, 18, 17], fill=255)), BRONZE, radius=0.5, slope=1.4)
    put(img, 16, 15, (40, 30, 20, 255))
    return selective_outline(img)


def icon_sawhorse():
    img = canvas()
    w = WOODS[""]
    for a, z in (((6, 28), (12, 12)), ((12, 28), (6, 12)), ((20, 28), (26, 12)), ((26, 28), (20, 12))):
        m = mask_of(img.size, lambda d, a=a, z=z: d.line([a, z], fill=255, width=2))
        shade_shape(img, m, w.w[1:6], radius=0.7, slope=1.5)
    m = mask_of(img.size, lambda d: d.rounded_rectangle([2, 9, 29, 15], radius=3, fill=255))
    shade_over(img, m, w.bark, w.bark[0], radius=1.2, slope=1.5, tex=lambda x, y: -0.4 if (x * 2 + y) % 5 == 0 else 0)
    for y in range(10, 15):
        for x in range(27, 31):
            if (x - 28.5) ** 2 / 4 + (y - 12) ** 2 / 7 <= 1:
                put(img, x, y, w.end[2] if (x + y) % 3 else w.end[1])
    return selective_outline(img)


def icon_carpenter_bench():
    img = canvas()
    w = WOODS[""]
    rnd = random.Random(91)
    board(img, 2, 12, 29, 16, w, rnd, True, tone=3.5)
    for x in (4, 25):
        m = mask_of(img.size, lambda d, x=x: d.rectangle([x, 17, x + 2, 29], fill=255))
        shade_over(img, m, w.w[1:6], w.w[0], radius=0.6, slope=1.5)
    for x in range(4, 28):
        put(img, x, 23, w.w[2]); put(img, x, 24, w.w[1])
    m = mask_of(img.size, lambda d: d.polygon([(6, 11), (9, 5), (11, 5), (11, 11)], fill=255))     # an adze resting on it
    shade_shape(img, m, IRON[1:], radius=0.5, slope=1.5)
    m = mask_of(img.size, lambda d: d.line([(11, 7), (21, 9)], fill=255, width=2))
    shade_shape(img, m, w.w[1:6], radius=0.5, slope=1.5)
    for x in range(18, 28, 2):                                                                      # shavings
        put(img, x, 11, w.w[5]); put(img, x + 1, 10, w.w[4])
    return selective_outline(img)


def icon_mason_bench():
    img = canvas()
    r = rock_ramp(graded(ROCKS["rough"]["base"]))[1:6]
    w = WOODS[""]
    m = mask_of(img.size, lambda d: d.rectangle([2, 12, 29, 17], fill=255))
    shade_shape(img, m, r, radius=1.0, slope=1.6)
    for x in (4, 24):
        m = mask_of(img.size, lambda d, x=x: d.rectangle([x, 18, x + 4, 29], fill=255))
        shade_over(img, m, r, r[0], radius=0.8, slope=1.6)
    m = mask_of(img.size, lambda d: d.rectangle([11, 5, 20, 11], fill=255))                       # a block being dressed
    shade_over(img, m, r, r[0], radius=0.6, slope=1.8)
    m = mask_of(img.size, lambda d: d.line([(22, 3), (27, 10)], fill=255, width=2))               # mallet
    shade_shape(img, m, w.w[1:6], radius=0.5, slope=1.5)
    m = mask_of(img.size, lambda d: d.rectangle([19, 1, 25, 4], fill=255))
    shade_over(img, m, w.w[1:6], w.w[0], radius=0.5, slope=1.5)
    for x, y in ((9, 11), (8, 10), (21, 11)):                                                       # chips
        put(img, x, y, r[4])
    return selective_outline(img)


def icon_tanning_rack():
    img = canvas()
    w = WOODS[""]
    fur = ramp(graded((170, 124, 82)), 4, main=1, dark=0.6, light=1.3)
    for box in ([4, 3, 6, 29], [25, 3, 27, 29], [3, 3, 28, 5], [3, 26, 28, 28]):
        m = mask_of(img.size, lambda d, b=box: d.rectangle(b, fill=255))
        shade_over(img, m, w.w[1:6], w.w[0], radius=0.6, slope=1.5)
    hide = [(9, 8), (16, 6), (23, 8), (22, 17), (24, 24), (16, 25), (8, 24), (10, 16)]
    shade_shape(img, mask_of(img.size, lambda d: d.polygon(hide, fill=255)), fur, radius=1.4, slope=1.3, tex=lambda x, y: bayer(x, y) * 0.1)
    for a, b in (((6, 5), (9, 8)), ((25, 5), (23, 8)), ((6, 26), (8, 24)), ((25, 26), (24, 24)), ((6, 16), (10, 16)), ((25, 17), (22, 17))):
        m = mask_of(img.size, lambda d, a=a, b=b: d.line([a, b], fill=255))
        img.paste(OAT[2], mask=m)
    return selective_outline(img)


def icon_butcher_spot():
    """A chopping block on a plank board, a cleaver bitten into it."""
    img = canvas()
    w = WOODS[""]
    m = mask_of(img.size, lambda d: d.rectangle([3, 18, 28, 26], fill=255))                        # the board
    shade_over(img, m, w.w[1:6], w.w[0], radius=0.6, slope=1.5)
    m = mask_of(img.size, lambda d: d.rectangle([9, 12, 22, 22], fill=255))                        # the block's side
    shade_over(img, m, w.w[1:6], w.w[0], radius=0.8, slope=1.5)
    m = mask_of(img.size, lambda d: d.ellipse([9, 8, 22, 15], fill=255))                           # its cut top
    shade_shape(img, m, OAT, radius=1.0, slope=1.2)
    m = mask_of(img.size, lambda d: d.polygon([(14, 4), (21, 3), (21, 10), (15, 11)], fill=255))   # the cleaver
    shade_over(img, m, IRON[1:], IRON[0], radius=0.6, slope=1.5)
    m = mask_of(img.size, lambda d: d.line([(21, 6), (28, 5)], fill=255, width=2))
    shade_shape(img, m, w.w[1:6], radius=0.5, slope=1.5)
    return selective_outline(img)


def icon_bellows(wood):
    img = canvas()
    w = wood.w
    m = mask_of(img.size, lambda d: d.polygon([(3, 7), (21, 12), (21, 20), (3, 25)], fill=255))
    shade_shape(img, m, LEATHER, radius=1.0, slope=1.2)
    for x in range(6, 20, 3):                                                                       # pleats
        for y in range(10, 23):
            if (x - 3) * 0.28 + 7 < y < 25 - (x - 3) * 0.28:
                put(img, x, y, LEATHER[0]); put(img, x + 1, y, LEATHER[3])
    for pts in ([(2, 5), (21, 11), (21, 12), (2, 8)], [(2, 24), (21, 20), (21, 21), (2, 27)]):    # the boards
        shade_over(img, mask_of(img.size, lambda d, p=pts: d.polygon(p, fill=255)), w[1:6], w[0], radius=0.6, slope=1.5)
    m = mask_of(img.size, lambda d: d.polygon([(21, 13), (30, 15), (30, 17), (21, 19)], fill=255))
    shade_over(img, m, IRON[1:], IRON[0], radius=0.6, slope=1.5)
    for a, z in (((1, 4), (5, 8)), ((1, 28), (5, 24))):
        m = mask_of(img.size, lambda d, a=a, z=z: d.line([a, z], fill=255, width=2))
        shade_shape(img, m, w[1:6], radius=0.5, slope=1.5)
    return selective_outline(img)


def icon_mint():
    img = canvas()
    w = WOODS[""]
    r = rock_ramp(graded(ROCKS["rough"]["base"]))[1:6]
    gold = ramp((222, 180, 60), 4, main=1, dark=0.6, light=1.18)
    m = mask_of(img.size, lambda d: d.rectangle([3, 14, 28, 21], fill=255))
    shade_shape(img, m, w.w[1:6], radius=0.8, slope=1.5)
    for x in (5, 25):
        m = mask_of(img.size, lambda d, x=x: d.rectangle([x, 22, x + 2, 29], fill=255))
        shade_over(img, m, w.w[1:6], w.w[0], radius=0.5, slope=1.5)
    m = mask_of(img.size, lambda d: d.rectangle([11, 8, 19, 13], fill=255))                      # the anvil die
    shade_over(img, m, r, r[0], radius=0.6, slope=1.6)
    m = mask_of(img.size, lambda d: d.rectangle([13, 2, 17, 7], fill=255))                       # the punch
    shade_shape(img, m, IRON[1:], radius=0.5, slope=1.5)
    for cx, cy in ((24, 10), (7, 11)):
        m = mask_of(img.size, lambda d, cx=cx, cy=cy: d.ellipse([cx - 3, cy - 2, cx + 3, cy + 2], fill=255))
        shade_shape(img, m, gold, radius=0.6, slope=1.5)
        put(img, cx, cy, gold[0])
    return selective_outline(img)


def icon_nest():
    """A ring of woven boughs round a bed of straw (the pyre's icon, for now)."""
    img = canvas()
    w = WOODS[""]
    m = mask_of(img.size, lambda d: d.ellipse([2, 6, 30, 30], fill=255))
    shade_shape(img, m, w.bark, radius=1.6, slope=1.2, tex=lambda x, y: -0.5 if (x * 2 + y * 3) % 7 < 2 else 0.2)
    m = mask_of(img.size, lambda d: d.ellipse([8, 11, 24, 25], fill=255))
    img.paste(STRAW[1], mask=m)
    shade_shape(img, mask_of(img.size, lambda d: d.ellipse([9, 12, 23, 24], fill=255)), STRAW, radius=2.0, slope=-1.0, depth=-0.4,
                tex=lambda x, y: 0.25 if (x + y * 3) % 5 == 0 else 0)
    for x, y in ((13, 15), (18, 18), (15, 20), (20, 14)):                                           # tufts of down
        put(img, x, y, FLEECE[3]); put(img, x + 1, y, FLEECE[2])
    return selective_outline(img)


def wagon():
    """A covered wagon in side view, 72x60 (renderSize 1.875, so drawn 1:1 beside 56-px people): oak bed
    with carved planking, oat-wool hood over bent hoops, a woad stripe, spoked wheels with iron tyres,
    the tongue reaching forward."""
    W, H = 72, 60
    img = canvas(W, H)
    w = WOODS["_oak"]
    rnd = random.Random(7)
    hoops = (21, 36, 51)
    hood = mask_of(img.size, lambda d: d.chord([9, 3, 63, 54], 180, 360, fill=255))
    shade_shape(img, hood, OAT, radius=3.2, slope=1.3, tex=lambda x, y: -0.25 if x in hoops else 0)
    for x in range(10, 63):
        for y in range(3, 28):
            if img.getpixel((x, y))[3] and 18 <= y <= 20:
                put(img, x, y, WOAD[2] if y < 20 else WOAD[1])
    for x in hoops:                                       # hoops showing through
        for y in range(5, 28):
            if img.getpixel((x, y))[3]:
                put(img, x, y, OAT[0])
    for y0 in (27, 33):
        board(img, 6, y0, 66, y0 + 5, w, rnd, True, tone=3.1)
    for x in range(8, 66, 8):
        nail(img, x, 30)
    for x in range(8, 64, 2):                             # a carved band along the bed
        put(img, x, 36 + (x // 2) % 2, w.w[1])
    m = mask_of(img.size, lambda d: d.line([(66, 36), (71, 31)], fill=255, width=3))
    shade_shape(img, m, w.w[1:6], radius=0.6, slope=1.5)
    for cx in (18, 54):                                   # wheels
        cy = 48
        for y in range(37, H):
            for x in range(cx - 12, cx + 13):
                d = math.hypot(x - cx, y - cy)
                if 9.2 <= d <= 11.2:
                    put(img, x, y, IRON[3] if (x - cx) + (y - cy) < 0 else IRON[1])
                elif 7.6 <= d < 9.2:
                    put(img, x, y, w.w[3] if (x - cx) + (y - cy) < 0 else w.w[1])
        for a in range(0, 360, 45):                       # spokes
            for k in range(2, 8):
                put(img, cx + round(math.cos(math.radians(a)) * k), cy + round(math.sin(math.radians(a)) * k), w.w[2])
        for dx, dy in ((0, 0), (1, 0), (0, 1), (1, 1)):
            put(img, cx + dx, cy + dy, IRON[3])
        put(img, cx - 1, cy - 1, IRON[4])
    return selective_outline(img)


# ---------------------------------------------------------------- generating

def generate():
    for suffix, wood in WOODS.items():
        seed = sum(map(ord, suffix))
        save(log_wall(wood, seed), "buildings/wall_wood" + suffix)
        save(stave_wall(wood, seed + 9), "buildings/wall_planks" + suffix)
        save(wood_floor(wood, seed + 1), "buildings/floor_wood" + suffix)
        save(plank_floor(wood, seed + 10), "buildings/floor_planks" + suffix)
        save(door(wood, seed + 2), "buildings/door" + suffix)
        save(stairs(wood, seed + 3), "buildings/stairs_wood" + suffix)
        save(stairs_side(wood, seed + 8), "buildings/stairs_wood" + suffix + "_side")
        save(ladder(wood, seed + 4), "buildings/ladder" + suffix)
        save(bed(wood, seed + 5), "buildings/bed" + suffix)
        save(icon_bellows(wood), "buildings/bellows" + suffix)
    save(stone_wall("rough", "dry"), "buildings/wall_stone")
    save(stone_wall("rough", "cut"), "buildings/wall_cut_stone")
    save(stone_wall("rough", "mortared"), "buildings/wall_mortared")
    for rock in ("sandstone", "limestone", "granite", "slate", "basalt"):
        save(stone_wall(rock, "dry"), f"buildings/wall_stone_{rock}")
        save(stone_wall(rock, "cut"), f"buildings/wall_cut_stone_{rock}")
        save(stone_wall(rock, "mortared"), f"buildings/wall_mortared_{rock}")
    save(brick_wall(), "buildings/wall_brick")
    save(bed_blanket(), "buildings/bed_blanket")
    save(cage_gate(), "buildings/cage_gate")
    save(padlock(), "props/padlock")
    save(prop_planks(), "props/planks")
    save(prop_log_side(), "props/log_side")
    save(prop_log_end(), "props/log_end")
    save(prop_woodpile_end(), "props/woodpile_end")
    save(prop_woodpile_bark(True), "props/woodpile_top")
    save(prop_woodpile_bark(False), "props/woodpile_side")
    save(cloth(LINEN, 3), "props/linen")
    save(cloth(ramp((246, 242, 232), 4, main=2, dark=0.8, light=1.03), 4), "props/pillow")
    save(prop_blanket(), "props/blanket")
    save(prop_stone_block(), "props/stone_block")
    save(prop_straw(), "props/straw")
    save(prop_chest(True), "props/chest_front")
    save(prop_chest(False), "props/chest_top")
    save(prop_basket(), "props/basket")
    berry = (((110, 20, 40, 255), (170, 34, 62, 255), (214, 92, 112, 255)), ((70, 30, 90, 255), (110, 54, 130, 255), (160, 110, 180, 255)))
    save(heap(berry, 17, (70, 30, 46, 255)), "props/berries_top")
    save(prop_fish_top(), "props/fish_top")
    save(prop_meat_top(), "props/meat_top")
    save(prop_meal_top(), "props/meal_top")
    save(prop_hide(), "props/hide")
    for name, make in (("table", icon_table), ("stool", icon_stool), ("chest", icon_chest), ("sawhorse", icon_sawhorse),
                       ("carpenter_bench", icon_carpenter_bench), ("mason_bench", icon_mason_bench),
                       ("tanning_rack", icon_tanning_rack), ("mint", icon_mint), ("nest", icon_nest),
                       ("butcher_spot", icon_butcher_spot), ("wagon", wagon)):
        save(make(), "buildings/" + name)


if __name__ == "__main__":
    generate()
