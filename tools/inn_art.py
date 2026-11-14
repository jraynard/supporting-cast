#!/usr/bin/env python3
"""The inn's upkeep art: the mop's inventory icon (``items/mop``) and the mess on the floor (``ui/mess_*``:
a puddle of spilled ale, muddy footprints, crumbs, a dark stain), drawn as flat decals the renderer lays on
the floor. Writes only its own files. Run alone (``python3 tools/inn_art.py``) or from
``gen_placeholder_art.py``.
"""
import math
import os
import random

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
TEXTURES = os.path.join(HERE, "..", "data", "textures")
S = 32


def canvas():
    return Image.new("RGBA", (S, S), (0, 0, 0, 0))


def save(img, rel):
    path = os.path.join(TEXTURES, rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def blob(img, cx, cy, r, colour, rnd, wobble=0.25, alpha=255):
    """A soft-edged irregular splash."""
    px = img.load()
    phase = [rnd.uniform(0, math.tau) for _ in range(3)]
    for y in range(S):
        for x in range(S):
            a = math.atan2(y - cy, x - cx)
            k = 1 + wobble * sum(math.sin(a * (i + 2) + p) for i, p in enumerate(phase)) / 3
            d = math.hypot(x - cx, y - cy) / (r * k)
            if d < 1:
                edge = min(1.0, (1 - d) * 3)
                old = px[x, y]
                aa = int(alpha * edge)
                if aa > old[3]:
                    px[x, y] = (*colour, aa)


def mess_puddle():
    """Spilled ale: an amber puddle with a pale froth at its rim."""
    img = canvas()
    rnd = random.Random(41)
    blob(img, 16, 16, 11, (232, 220, 190), rnd, alpha=170)
    blob(img, 16, 16, 9, (176, 116, 40), rnd, alpha=200)
    blob(img, 23, 9, 3, (176, 116, 40), rnd, alpha=190)
    for x, y in ((11, 12), (12, 12), (19, 18)):
        img.putpixel((x, y), (250, 236, 200, 230))
    return img


def mess_mud():
    """Muddy bootprints, walking across."""
    img = canvas()
    rnd = random.Random(42)
    for i, (x, y) in enumerate(((7, 24), (13, 18), (19, 13), (25, 7))):
        blob(img, x, y, 3.2, (92, 68, 44), rnd, wobble=0.15, alpha=200)
        blob(img, x + (1 if i % 2 else -1), y + 3, 1.8, (92, 68, 44), rnd, wobble=0.1, alpha=200)
    return img


def mess_crumbs():
    """Crumbs and scraps scattered about."""
    img = canvas()
    rnd = random.Random(43)
    for _ in range(26):
        x, y = rnd.randrange(5, 27), rnd.randrange(5, 27)
        c = rnd.choice([(196, 160, 104), (150, 110, 60), (120, 150, 80)])
        img.putpixel((x, y), (*c, 255))
        if rnd.random() < 0.4:
            img.putpixel((x + 1, y), (*c, 220))
    return img


def mess_stain():
    """A dark stain: could be anything spilled, if you don't look closer."""
    img = canvas()
    rnd = random.Random(44)
    blob(img, 15, 17, 10, (70, 28, 26), rnd, wobble=0.35, alpha=200)
    blob(img, 24, 8, 2.5, (70, 28, 26), rnd, alpha=190)
    blob(img, 7, 9, 1.8, (70, 28, 26), rnd, alpha=190)
    return img


def item_mop():
    """A mop: a long pale handle and a grey rag head."""
    img = canvas()
    px = img.load()
    for i in range(22):                       # the handle, corner to corner
        x, y = 26 - i, 5 + i
        px[x, y] = (196, 160, 110, 255)
        px[x + 1, y] = (150, 112, 70, 255)
    rnd = random.Random(45)
    for _ in range(60):                       # the rag head, strands hanging
        a = rnd.uniform(0, math.tau)
        r = rnd.uniform(0, 5.5)
        x, y = int(5 + math.cos(a) * r), int(26 + math.sin(a) * r * 0.8)
        if 0 <= x < S and 0 <= y < S:
            g = rnd.randrange(150, 205)
            px[x, y] = (g, g, g - 10, 255)
    return img


def sign_blank(seed):
    """A sign's board: a dark frame round a painted face, with gilt rules top and bottom."""
    img = canvas()
    px = img.load()
    rnd = random.Random(seed)
    for y in range(S):
        for x in range(S):
            edge = min(x, y, S - 1 - x, S - 1 - y)
            if edge < 3:
                v = 40 + rnd.randrange(12)
                px[x, y] = (v + 18, v + 6, v - 6, 255)                      # the dark frame
            else:
                v = 92 + rnd.randrange(14) + (6 if (y // 4) % 2 else 0)      # painted planks
                px[x, y] = (v, int(v * 0.62), int(v * 0.36), 255)
    for x in range(5, S - 5):                                               # gilt rules
        px[x, 5] = (236, 178, 74, 255)
        px[x, S - 6] = (236, 178, 74, 255)
    return img


def prop_sign_board():
    """A hanging inn sign: the board with a bear's head in the middle."""
    img = sign_blank(46)
    px = img.load()
    # A bear's head: round face, two ears, a pale muzzle.
    dark = (44, 28, 20, 255)
    for y in range(S):
        for x in range(S):
            if math.hypot(x - 16, y - 17) < 6.5 or math.hypot(x - 11, y - 11) < 2.6 or math.hypot(x - 21, y - 11) < 2.6:
                px[x, y] = dark
    for y in range(S):
        for x in range(S):
            if math.hypot(x - 16, y - 19.5) < 2.6:
                px[x, y] = (196, 150, 104, 255)
    px[16, 18] = dark
    px[14, 15] = (236, 178, 74, 255)
    px[18, 15] = (236, 178, 74, 255)
    return img


def prop_sign_anvil():
    """The smithy's sign: the board with a black iron anvil on it, a glint along its face."""
    img = sign_blank(47)
    px = img.load()
    iron, lit = (34, 32, 34, 255), (150, 146, 150, 255)
    # The face and its horn, tapering to a point on the left.
    for y in range(11, 15):
        for x in range(5, 25):
            horn = 13 - x                                                     # how far into the horn
            if horn <= 0 or (y - 11) < 4 - horn * 0.45:
                px[x, y] = iron
    for x in range(6, 25):                                                  # the glint along the top
        px[x, 11] = lit
    for y in range(15, 19):                                                 # the waist
        for x in range(13, 21):
            px[x, y] = iron
    for y in range(19, 23):                                                 # the foot, splaying out
        for x in range(11 - (y - 19), 23 + (y - 19)):
            px[x, y] = iron
    return img


def prop_sign_gear():
    """The tinker's sign: a black iron cogwheel on the board, eight teeth round a hub with its axle hole,
    a glint along its upper left."""
    img = sign_blank(48)
    px = img.load()
    iron, lit = (34, 32, 34, 255), (150, 146, 150, 255)
    cx, cy = 16, 16.5
    for y in range(S):
        for x in range(S):
            dx, dy = x + 0.5 - cx, y + 0.5 - cy
            r = math.hypot(dx, dy)
            a = math.atan2(dy, dx)
            tooth = math.cos(a * 8) > 0.35                                     # eight square-ish teeth
            if r < 2.2:
                continue                                                      # the axle hole: the board shows through
            if r < 6.6 or (tooth and r < 8.6):
                if 3.6 < r < 4.6 and math.cos(a * 4) > 0.2:
                    continue                                                  # spokes: four windows in the web
                px[x, y] = lit if (dx + dy < -7.0 and r > 6.0) else iron
    return img


def prop_sign_rune():
    """The healer's sign: a mending bindrune painted in pale gold on the board, a stave with its arms
    raised to ward, and a diamond bound on below it."""
    img = sign_blank(49)
    px = img.load()
    gold, deep = (232, 204, 120, 255), (150, 110, 52, 255)

    def dot(x, y, c=gold):
        if 0 <= x < S and 0 <= y < S:
            px[x, y] = c

    def stroke(x0, y0, x1, y1):
        n = int(max(abs(x1 - x0), abs(y1 - y0)) * 2) + 1
        for k in range(n + 1):
            t = k / n
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            dot(round(x), round(y) + 1, deep)                                 # the painted stroke's shadow
            dot(round(x), round(y))
    stroke(16, 8, 16, 24)                                                   # the stave
    stroke(16, 13, 11, 8); stroke(16, 13, 21, 8)                           # arms raised, warding
    stroke(16, 16, 12, 20); stroke(12, 20, 16, 24)                         # a diamond bound on below: kept whole
    stroke(16, 16, 20, 20); stroke(20, 20, 16, 24)
    return img


def generate():
    save(mess_puddle(), "ui/mess_puddle")
    save(mess_mud(), "ui/mess_mud")
    save(mess_crumbs(), "ui/mess_crumbs")
    save(mess_stain(), "ui/mess_stain")
    save(item_mop(), "items/mop")
    save(prop_sign_board(), "props/sign_board")
    save(prop_sign_anvil(), "props/sign_anvil")
    save(prop_sign_gear(), "props/sign_gear")
    save(prop_sign_rune(), "props/sign_rune")


if __name__ == "__main__":
    generate()
    print("wrote the inn's art")
