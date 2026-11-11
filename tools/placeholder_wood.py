"""Procedural placeholder art for wood materials and berry bushes.

Every wooden building and the wood item come in one sprite per material, named with the
material's suffix (buildings/wall_wood_birch); the unsuffixed sprite is the generic fallback. The
game picks the material sprite when it exists and tints the generic one otherwise (docs/ART.md).
"""
import math
import random

from PIL import Image, ImageDraw

from placeholder_nature import ROOT, blob_canopy, save, shade, variant_name
from placeholder_sheets import outline

S = 32

# base, dark, light, grain/seam, bark, bark_mark, end-grain, ring
WOODS = {
    "": dict(base=(140, 100, 60), dark=(96, 64, 36), light=(170, 126, 80), grain=(116, 80, 46),
             bark=(96, 72, 50), mark=(70, 52, 36), end=(186, 142, 92), ring=(140, 100, 60)),
    "_oak": dict(base=(146, 100, 60), dark=(98, 64, 38), light=(176, 126, 78), grain=(118, 80, 46),
                 bark=(96, 86, 74), mark=(66, 58, 50), end=(196, 150, 98), ring=(148, 104, 62)),
    "_pine": dict(base=(206, 162, 102), dark=(150, 108, 62), light=(228, 192, 134), grain=(178, 132, 78),
                  bark=(134, 80, 54), mark=(96, 54, 36), end=(232, 196, 140), ring=(194, 148, 94), knots=True),
    "_birch": dict(base=(226, 208, 172), dark=(178, 158, 120), light=(242, 232, 206), grain=(204, 184, 146),
                   bark=(236, 232, 222), mark=(46, 42, 40), end=(220, 190, 140), ring=(178, 148, 100), marks=5),
}


def rgba(c):
    return tuple(c) + (255,) if len(c) == 3 else c


def grain_board(img, x0, y0, x1, y1, w, rnd, horizontal):
    """Fill a board with base colour, a few long grain streaks, optional knots."""
    px = img.load()
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            k = 1 + rnd.uniform(-0.04, 0.04)
            px[x, y] = shade(w["base"], k)
    length = (x1 - x0) if horizontal else (y1 - y0)
    across = (y1 - y0) if horizontal else (x1 - x0)
    for _ in range(max(1, across // 2)):
        o = rnd.randint(1, max(1, across - 1))
        start = rnd.randint(0, max(0, length // 3))
        end = rnd.randint(length * 2 // 3, length)
        wobble = rnd.uniform(0, math.tau)
        for t in range(start, end + 1):
            d = int(round(o + 0.6 * math.sin(t * 0.35 + wobble)))
            x, y = (x0 + t, y0 + d) if horizontal else (x0 + d, y0 + t)
            if x0 <= x <= x1 and y0 <= y <= y1:
                px[x, y] = rgba(w["grain"])
    if w.get("knots") and rnd.random() < 0.6:
        cx = rnd.randint(x0 + 2, max(x0 + 2, x1 - 2))
        cy = rnd.randint(y0 + 1, max(y0 + 1, y1 - 1))
        px[cx, cy] = rgba(w["dark"])
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            if x0 <= cx + dx <= x1 and y0 <= cy + dy <= y1:
                px[cx + dx, cy + dy] = rgba(w["grain"])
    # Light top/left edge, dark bottom/right edge: each board reads as its own piece.
    for x in range(x0, x1 + 1):
        px[x, y0] = rgba(w["light"])
        px[x, y1] = rgba(w["dark"])
    for y in range(y0, y1 + 1):
        px[x0, y] = rgba(w["light"]) if not horizontal else px[x0, y]
        px[x1, y] = rgba(w["dark"]) if not horizontal else px[x1, y]


def wall(w, seed):
    """Horizontal planks, staggered butt joints, nail heads. Used on every face of a wall block."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    for row in range(4):
        y0, y1 = row * 8, row * 8 + 7
        joint = 12 if row % 2 == 0 else 22
        grain_board(img, 0, y0, joint - 1, y1, w, rnd, True)
        grain_board(img, joint, y0, S - 1, y1, w, rnd, True)
        d = ImageDraw.Draw(img)
        for x in (joint - 2, joint + 1):
            d.point([(x, y0 + 2), (x, y1 - 2)], fill=rgba(w["mark"] if w is WOODS["_birch"] else w["dark"]))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, S - 1, S - 1], outline=rgba(w["dark"]))
    return img


def floor(w, seed):
    """Vertical boards with staggered end seams."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    for col in range(4):
        x0, x1 = col * 8, col * 8 + 7
        seam = (5, 21, 13, 27)[col]
        grain_board(img, x0, 0, x1, seam - 1, w, rnd, False)
        grain_board(img, x0, seam, x1, S - 1, w, rnd, False)
    return img


def door(w, seed):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    for col in range(4):
        grain_board(img, 3 + col * 7, 1, 3 + col * 7 + 6, 30, w, rnd, False)
    d = ImageDraw.Draw(img)
    # Z brace and frame.
    for y in (5, 24):
        d.rectangle([3, y, 30, y + 2], fill=rgba(w["dark"]))
    d.line([(5, 23), (28, 8)], fill=rgba(w["dark"]), width=2)
    d.rectangle([2, 0, 31, 31], outline=shade(w["dark"], 0.75))
    iron, iron_lit, rivet = (66, 70, 84, 255), (118, 124, 140, 255), (150, 156, 170, 255)
    for y in (6, 23):                                          # forged strap hinges: a bar, a curled end, rivets
        d.rectangle([3, y, 30, y + 1], fill=iron)
        d.line([(3, y - 1), (29, y - 1)], fill=iron_lit)
        d.line([(3, y - 2), (3, y + 3)], fill=iron)
        d.point([(4, y - 2), (4, y + 3)], fill=iron)
        for x in (8, 14, 20, 26):
            d.point([(x, y)], fill=rivet)
    d.ellipse([22, 13, 28, 19], fill=iron, outline=(36, 38, 46, 255))      # a ring pull on its plate
    d.ellipse([23, 14, 27, 18], outline=iron_lit)
    return img


def stairs(w, seed):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    for i, y in enumerate(range(26, -2, -6)):
        k = 0.82 + i * 0.06  # treads lighter towards the top (north) edge
        lit = {key: (shade(v, k)[:3] if isinstance(v, tuple) else v) for key, v in w.items()}
        grain_board(img, 3, max(0, y - 5), 28, y, lit, rnd, True)
    return img


def ladder(w, seed):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for x in (7, 22):
        d.rectangle([x, 0, x + 3, 31], fill=rgba(w["base"]), outline=rgba(w["dark"]))
        d.line([(x + 1, 0), (x + 1, 31)], fill=rgba(w["light"]))
    for y in range(3, S, 6):
        d.rectangle([10, y, 22, y + 2], fill=rgba(w["light"]), outline=rgba(w["dark"]))
    return img


def stairs_side(w, seed):
    """The stringer seen from the side: a board cut to the stairs' stepped profile (high end on
    the left, as the mesher maps it), with each tread's nosing along the step tops."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grain_board(img, 0, 0, S - 1, S - 1, w, rnd, True)
    px = img.load()
    for y in range(S):
        for x in range(S):
            step_top = (x // 8) * 8
            if y < step_top:
                px[x, y] = (0, 0, 0, 0)
            elif y < step_top + 2:
                px[x, y] = rgba(w["light"] if y == step_top else w["dark"])  # tread edge
            elif x % 8 == 7 and y < step_top + 8:
                px[x, y] = rgba(w["dark"])  # riser shadow
    return img


def bed(w, seed):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grain_board(img, 4, 1, 27, 30, w, rnd, False)                    # frame
    d = ImageDraw.Draw(img)
    d.rectangle([4, 1, 27, 4], fill=rgba(w["dark"]))                  # headboard
    d.rectangle([6, 5, 25, 28], fill=(62, 92, 152, 255))              # mattress / blanket: woad wool
    for y in range(13, 28):                                           # woven texture
        for x in range(6, 26):
            if (x + y) % 4 == 0:
                d.point([(x, y)], fill=(52, 80, 136, 255))
    for x in range(6, 26):                                            # a braid across the foot
        d.point([(x, 25)], fill=(220, 176, 70, 255) if (x // 2) % 2 == 0 else (150, 112, 44, 255))
        d.point([(x, 26)], fill=(150, 112, 44, 255) if (x // 2) % 2 == 0 else (220, 176, 70, 255))
    d.rectangle([7, 6, 24, 11], fill=(232, 222, 198, 255))            # linen pillow
    d.line([(7, 11), (24, 11)], fill=(196, 184, 156, 255))
    d.rectangle([4, 1, 27, 30], outline=shade(w["dark"], 0.8))
    return img


def campfire(w, seed):
    """A ring of stones round crossed logs of this wood, with a flame on top."""
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for a in range(0, 360, 40):
        x, y = 16 + 12 * math.cos(math.radians(a)), 20 + 8 * math.sin(math.radians(a))
        d.ellipse([x - 3, y - 2.5, x + 3, y + 2.5], fill=(118, 116, 120, 255), outline=(70, 68, 72, 255))
        d.point([(x - 1, y - 1)], fill=(160, 158, 162, 255))
    for (x0, y0, x1, y1) in ((7, 25, 24, 15), (8, 15, 25, 25)):
        d.line([(x0, y0), (x1, y1)], fill=rgba(w["bark"]), width=4)
        d.line([(x0, y0 - 1), (x1, y1 - 1)], fill=rgba(w["mark"]), width=1)
        for (ex, ey) in ((x0, y0), (x1, y1)):
            d.ellipse([ex - 2, ey - 2, ex + 2, ey + 2], fill=rgba(w["end"]), outline=rgba(w["ring"]))
    d.polygon([(10, 21), (16, 3), (22, 21)], fill=(240, 120, 30, 255))
    d.polygon([(13, 21), (16, 10), (19, 21)], fill=(255, 220, 90, 255))
    return img


def log_pile(w, seed):
    """Six logs stacked 3-2-1, end grain facing the viewer, bark visible along the top of each."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = 4.6
    rows = [(9, [16]), (17, [11.5, 20.5]), (25, [7, 16, 25])]
    for cy, xs in rows:
        for cx in xs:
            cx += rnd.uniform(-0.4, 0.4)
            # Bark side running back and up from the end (a little perspective).
            back = [(cx - r, cy), (cx - r + 3, cy - 4), (cx + r + 3, cy - 4), (cx + r, cy)]
            d.polygon(back, fill=rgba(w["bark"]))
            for k in range(w.get("marks", 3)):
                mx = cx - r + 2 + rnd.uniform(0, 2 * r)
                d.point([(mx, cy - 3 + rnd.randint(0, 2))], fill=rgba(w["mark"]))
            # End grain: bark rim, sapwood, rings, a lit upper-left.
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=rgba(w["bark"]))
            d.ellipse([cx - r + 1, cy - r + 1, cx + r - 1, cy + r - 1], fill=rgba(w["end"]))
            d.ellipse([cx - r + 2.2, cy - r + 2.2, cx + r - 2.2, cy + r - 2.2], outline=rgba(w["ring"]))
            d.point([(cx, cy)], fill=rgba(w["ring"]))
            d.point([(cx - 2, cy - 2), (cx - 1, cy - 3)], fill=shade(w["end"], 1.12))
    return outline(img, shade(w["mark"], 0.7))


def berry_bush(seed, berries=True):
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hue = rnd.uniform(-6, 6)
    palette = [(30, 72 + hue, 38, 255), (44, 96 + hue, 46, 255), (62, 124 + hue, 54, 255), (98, 156 + hue, 70, 255)]
    palette = [(int(r), int(g), int(b), a) for r, g, b, a in palette]
    if not berries:  # picked bushes look a little tired
        palette = [shade(c, 0.9) for c in palette]
    circles = [(16, 20, 10.5)]
    for _ in range(rnd.randint(5, 7)):
        a = rnd.uniform(math.pi, math.tau) if rnd.random() < 0.6 else rnd.uniform(0, math.tau)
        circles.append((16 + math.cos(a) * rnd.uniform(5, 9), 21 + math.sin(a) * rnd.uniform(3, 7), rnd.uniform(5, 7.5)))
    circles = [(x, min(y, 31 - r * 0.6), r) for x, y, r in circles]
    blob_canopy(img, circles, palette, rnd, dither=0.16)
    img = outline(img, (18, 42, 22, 255))
    if berries:
        px = img.load()
        for _ in range(rnd.randint(6, 9)):
            cx, cy = rnd.randint(7, 25), rnd.randint(12, 27)
            if px[cx, cy][3] == 0:
                continue
            for dx, dy in rnd.sample([(0, 0), (2, 1), (-1, 2), (1, -2), (-2, -1)], rnd.randint(2, 4)):
                x, y = cx + dx, cy + dy
                if 1 <= x < S - 1 and 1 <= y < S - 1 and px[x, y][3] > 0:
                    px[x, y] = (196, 28, 58, 255)
                    px[x + 1, y] = (150, 18, 44, 255)
                    px[x, y + 1] = (150, 18, 44, 255)
                    px[x, y - 1] = (230, 90, 110, 255)  # highlight
    return img


def plank_wall(w, seed):
    """Narrow vertical tongue-and-groove boards with a rail across: finer than the log-built wall."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    for col in range(8):
        grain_board(img, col * 4, 0, col * 4 + 3, S - 1, w, rnd, False)
    d = ImageDraw.Draw(img)
    for x in range(0, S, 4):
        d.line([(x, 0), (x, S - 1)], fill=rgba(w["grain"]))
    grain_board(img, 0, 13, S - 1, 18, w, rnd, True)  # the rail
    for x in range(1, S - 1):                           # carved interlace: two strands crossing, over and under
        wave = math.sin(x * 0.62)
        a, b = round(15.5 + 1.7 * wave), round(15.5 - 1.7 * wave)
        over, under = (b, a) if wave > 0 else (a, b)
        d.point([(x, under)], fill=rgba(w["dark"]))
        d.point([(x, over)], fill=rgba(w["light"]))
    d.rectangle([0, 0, S - 1, S - 1], outline=rgba(w["dark"]))
    return img


def plank_floor(w, seed):
    """Narrow boards laid across, staggered: tighter than the wide log-floor boards."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S))
    for row in range(8):
        y0, y1 = row * 4, row * 4 + 3
        seam = (6, 22, 14, 28, 10, 18, 26, 4)[row]
        grain_board(img, 0, y0, seam - 1, y1, w, rnd, True)
        grain_board(img, seam, y0, S - 1, y1, w, rnd, True)
    return img


def planks_item(w, seed):
    """A small stack of sawn boards, seen from the front."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    for i, (y0, x0, x1) in enumerate(((22, 3, 28), (17, 5, 30), (12, 2, 26), (7, 6, 29))):
        grain_board(img, x0, y0, x1, y0 + 4, w, rnd, True)
    return outline(img, (40, 28, 20, 255))


def sawhorse_icon(w, seed):
    """Toolbar icon: a log across two crossed legs."""
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for x0, x1 in ((6, 12), (12, 6), (20, 26), (26, 20)):
        d.line([(x0, 28), (x1, 12)], fill=rgba(w["dark"]), width=2)
    d.rounded_rectangle([2, 10, 30, 16], radius=3, fill=rgba(w["bark"]), outline=rgba(w["mark"]))
    d.ellipse([26, 10, 31, 16], fill=rgba(w["end"]), outline=rgba(w["ring"]))
    return outline(img, (40, 28, 20, 255))


def bench_icon(w, seed):
    """Toolbar icon: a planked bench top on legs, a stone vise at one end."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    grain_board(img, 2, 12, 29, 16, w, rnd, True)
    d = ImageDraw.Draw(img)
    for x in (4, 26):
        d.rectangle([x, 17, x + 2, 29], fill=rgba(w["dark"]))
    d.rectangle([4, 23, 28, 24], fill=rgba(w["grain"]))
    d.rectangle([22, 6, 28, 12], fill=(128, 128, 134, 255), outline=(80, 80, 86, 255))
    return outline(img, (40, 28, 20, 255))


def generate():
    for suffix, w in WOODS.items():
        seed = sum(map(ord, suffix))
        save(wall(w, seed), "buildings/wall_wood" + suffix)
        save(floor(w, seed + 1), "buildings/floor_wood" + suffix)
        save(door(w, seed + 2), "buildings/door" + suffix)
        save(stairs(w, seed + 3), "buildings/stairs_wood" + suffix)
        save(stairs_side(w, seed + 8), "buildings/stairs_wood" + suffix + "_side")
        save(ladder(w, seed + 4), "buildings/ladder" + suffix)
        save(bed(w, seed + 5), "buildings/bed" + suffix)
        save(log_pile(w, seed + 6), "items/wood" + suffix)
        save(campfire(w, seed + 7), "buildings/campfire" + suffix)
        save(plank_wall(w, seed + 9), "buildings/wall_planks" + suffix)
        save(plank_floor(w, seed + 10), "buildings/floor_planks" + suffix)
        save(planks_item(w, seed + 11), "items/planks" + suffix)
    save(sawhorse_icon(WOODS[""], 90), "buildings/sawhorse")
    save(bench_icon(WOODS[""], 91), "buildings/carpenter_bench")
    for i in range(3):
        save(berry_bush(40 + i), variant_name("things/berry_bush", i))
        save(berry_bush(40 + i, berries=False), variant_name("things/berry_bush_bare", i))


if __name__ == "__main__":
    generate()
