#!/usr/bin/env python3
"""The inn's furniture faces (data/defs/furniture.json): warm oak boards for table tops and the
counter, legs and end grain, a frame-and-panel counter front, barrel staves and hoops, the cask's
head and its brass tap.

The older props/planks family is a pale neutral the game tints by material, and a preset
building's furniture has no material, so the inn's tables came out the grey of driftwood. These
are drawn in their wood, with a version per wood (<name>_oak, _pine, _birch: MaterialArt picks
them untinted for built furniture); the plain name is oak, for the preset inn.

32x32 faces, 32 px a cell (docs/ART.md), through the shared world grade. Writes only its own
files: python3 tools/inn_furniture_art.py. gen_placeholder_art.py runs it after the Norse pass.
"""
import math
import random

from PIL import Image, ImageDraw

from village_art import S, clamp, save, tint

# Pre-grade wood colours: the grade cools and mutes them, so they start warm.
WOODS = {
    "oak": (156, 102, 58),
    "pine": (196, 146, 86),
    "birch": (210, 184, 140),
}
IRON = (58, 54, 52)
BRASS = (214, 168, 74)


def _grain(img, base, seed, along_x=True, lines=10, dark=0.8, light=1.12):
    """Streaks of grain along the board, a few knots."""
    rnd = random.Random(seed)
    d = ImageDraw.Draw(img)
    w, h = img.size
    for _ in range(lines):
        if along_x:
            y = rnd.randrange(h)
            x = rnd.randrange(w)
            d.line([(x, y), (x + rnd.randrange(6, 18), y)], fill=tint(base, rnd.choice([dark, light, 0.9])))
        else:
            x = rnd.randrange(w)
            y = rnd.randrange(h)
            d.line([(x, y), (x, y + rnd.randrange(6, 18))], fill=tint(base, rnd.choice([dark, light, 0.9])))
    if rnd.random() < 0.5:
        x, y = rnd.randrange(3, w - 3), rnd.randrange(3, h - 3)
        d.ellipse([x - 1, y - 1, x + 1, y + 1], fill=tint(base, 0.62))


def _field(base, seed, size=(S, S), var=4):
    rnd = random.Random(seed)
    img = Image.new("RGBA", size)
    px = img.load()
    for y in range(size[1]):
        for x in range(size[0]):
            px[x, y] = tint(base, 1 + rnd.uniform(-var, var) / 100)
    return img


def planks(base, seed=11):
    """A table top or counter top: four boards along it, each its own shade, a dark joint between, pegs at the ends."""
    img = _field(base, seed)
    d = ImageDraw.Draw(img)
    rnd = random.Random(seed)
    for b in range(4):
        y0 = b * 8
        k = rnd.choice([0.92, 0.97, 1.0, 1.05])
        for y in range(y0, y0 + 8):
            for x in range(S):
                img.putpixel((x, y), tint(img.getpixel((x, y)), k))
        board = img.crop((0, y0, S, y0 + 8))
        _grain(board, tint(base, k), seed + b * 7, along_x=True, lines=4)
        img.paste(board, (0, y0))
        d.line([(0, y0 + 7), (S - 1, y0 + 7)], fill=tint(base, 0.55))       # the joint
        d.line([(0, y0), (S - 1, y0)], fill=tint(base, 1.14))               # its lit edge
        for x in (2, S - 3):                                                 # pegs
            img.putpixel((x, y0 + 3), tint(base, 0.5))
    return img


def leg(base, seed=21):
    """A leg or rail's side: close grain running up it, a lit edge and a shaded one."""
    img = _field(tint(base, 0.9), seed)
    _grain(img, tint(base, 0.9), seed, along_x=False, lines=14)
    d = ImageDraw.Draw(img)
    for x in range(0, S, 8):
        d.line([(x, 0), (x, S - 1)], fill=tint(base, 1.12))
        d.line([(x + 7, 0), (x + 7, S - 1)], fill=tint(base, 0.66))
    return img


def end_grain(base, seed=31):
    """Sawn end: growth rings round a heart, a crack."""
    img = Image.new("RGBA", (S, S))
    rnd = random.Random(seed)
    cx, cy = S / 2 + rnd.uniform(-3, 3), S / 2 + rnd.uniform(-3, 3)
    pale = tint(base, 1.18)
    for y in range(S):
        for x in range(S):
            r = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
            ring = (r + rnd.uniform(-0.4, 0.4)) % 3 < 1
            img.putpixel((x, y), tint(pale, 0.82 if ring else 1.0 + rnd.uniform(-0.03, 0.03)))
    d = ImageDraw.Draw(img)
    d.line([(int(cx), int(cy)), (int(cx) + 9, int(cy) - 6)], fill=tint(base, 0.6))
    return img


def counter_panel(base, seed=41):
    """A counter's front, a bay a cell: a frame of stiles and rails round a raised panel, lit along its top edge."""
    img = _field(tint(base, 0.92), seed)
    _grain(img, tint(base, 0.92), seed, along_x=False, lines=8)
    d = ImageDraw.Draw(img)
    frame = tint(base, 0.78)
    d.rectangle([0, 0, 3, S - 1], fill=frame)                 # the stile between bays
    d.rectangle([0, 0, S - 1, 3], fill=frame)                 # the top rail
    d.rectangle([0, S - 5, S - 1, S - 1], fill=frame)         # the bottom rail
    d.rectangle([7, 7, S - 5, S - 9], outline=tint(base, 0.6))  # the panel's sunk edge
    d.line([(8, 8), (S - 6, 8)], fill=tint(base, 1.2))          # and its lit bevel
    d.line([(8, 8), (8, S - 10)], fill=tint(base, 1.1))
    for x in range(S):
        img.putpixel((x, 4), tint(base, 0.55))                # shadow under the top rail
    return img


def staves(base, seed=51):
    """A barrel's side, staves running along it (the cask lies along the face's width): each a shade of its own, rounded light down the middle."""
    img = Image.new("RGBA", (S, S))
    rnd = random.Random(seed)
    for s in range(0, S, 5):
        k = rnd.choice([0.92, 0.98, 1.04])
        for y in range(s, min(S, s + 5)):
            t = (y - s) / 4.0
            light = 0.86 + 0.24 * math.sin(math.pi * t)       # each stave rounded
            for x in range(S):
                img.putpixel((x, y), tint(base, k * light * (1 + rnd.uniform(-0.03, 0.03))))
        for x in range(S):
            img.putpixel((x, s), tint(base, 0.52))            # the seam
    _grain(img, base, seed, along_x=True, lines=6, dark=0.82, light=1.1)
    return img


def cask_head(base, seed=61):
    """The cask's end: head boards inside the dark chime of its staves, a bung, the round on a clear ground."""
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([0, 0, S - 1, S - 1], fill=tint(base, 0.62))         # the chime: the staves' ends
    d.ellipse([3, 3, S - 4, S - 4], fill=tint(base, 1.02))         # the head
    head = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hp = head.load()
    rnd = random.Random(seed)
    for y in range(S):
        for x in range(S):
            if img.getpixel((x, y))[3] and math.hypot(x + 0.5 - S / 2, y + 0.5 - S / 2) < S / 2 - 3.5:
                board = x // 7
                hp[x, y] = tint(base, (1.0 + 0.05 * (board % 2)) * (1 + rnd.uniform(-0.03, 0.03)))
    img.alpha_composite(head)
    for x in range(7, S - 3, 7):                                   # the joints between head boards
        for y in range(5, S - 5):
            if math.hypot(x + 0.5 - S / 2, y + 0.5 - S / 2) < S / 2 - 4:
                img.putpixel((x, y), tint(base, 0.62))
    d.ellipse([S // 2 + 4, 7, S // 2 + 7, 10], fill=tint(base, 0.45))  # the bung
    return img


def hoop(seed=71):
    """An iron hoop's face: dark, a lit edge, rivets."""
    img = _field(IRON, seed, var=6)
    d = ImageDraw.Draw(img)
    d.line([(0, 0), (S - 1, 0)], fill=tint(IRON, 1.6))
    d.line([(0, S - 1), (S - 1, S - 1)], fill=tint(IRON, 0.7))
    for x in range(4, S, 10):
        d.rectangle([x, S // 2 - 1, x + 1, S // 2], fill=tint(IRON, 1.8))
    return img


def brass(seed=81):
    """The tap: polished brass, a highlight."""
    img = _field(BRASS, seed, var=5)
    d = ImageDraw.Draw(img)
    d.line([(0, 4), (S - 1, 4)], fill=tint(BRASS, 1.25))
    d.line([(0, S - 5), (S - 1, S - 5)], fill=tint(BRASS, 0.7))
    return img


def generate():
    for wood, base in WOODS.items():
        for name, art in (("oak_planks", planks), ("oak_leg", leg), ("oak_end", end_grain), ("counter_panel", counter_panel),
                          ("staves", staves), ("cask_head", cask_head)):
            img = art(base)
            save(img, f"furniture/{name}_{wood}")
            if wood == "oak":
                save(img, f"furniture/{name}")
    save(hoop(), "furniture/hoop")
    save(brass(), "furniture/brass")
    print("inn furniture: oak boards, legs, end grain, counter panels, staves, cask heads, hoops, brass")


if __name__ == "__main__":
    generate()
