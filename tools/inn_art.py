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


def generate():
    save(mess_puddle(), "ui/mess_puddle")
    save(mess_mud(), "ui/mess_mud")
    save(mess_crumbs(), "ui/mess_crumbs")
    save(mess_stain(), "ui/mess_stain")
    save(item_mop(), "items/mop")


if __name__ == "__main__":
    generate()
    print("wrote the inn's art")
