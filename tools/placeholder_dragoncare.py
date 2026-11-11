"""Placeholder art for dragon care (docs/plans/dragon-interactions.md): the care window's and the
dragon's want icons, the toolbar's Call, a scale brush, a feeding trough and the heart particle.

Programmer art like the rest: 24x24 light glyphs with a dark outline (as placeholder_sheets.ui_icons).
Run on its own (python3 tools/placeholder_dragoncare.py) or from gen_placeholder_art.py.
"""
import math
import os

from PIL import Image, ImageDraw

from placeholder_sheets import outline

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
INK = (40, 26, 20, 255)
S = 24

PINK = (236, 110, 130, 255)
PINK_D = (180, 70, 92, 255)
GOLD = (240, 200, 80, 255)
GOLD_D = (190, 140, 40, 255)
WOOD = (176, 124, 72, 255)
WOOD_D = (120, 82, 46, 255)
FIBRE = (222, 206, 150, 255)
BONE = (238, 230, 214, 255)
SKY = (130, 190, 235, 255)


def heart(d, cx, cy, r, fill):
    d.ellipse([cx - r * 2, cy - r * 1.6, cx, cy + r * 0.4], fill=fill)
    d.ellipse([cx, cy - r * 1.6, cx + r * 2, cy + r * 0.4], fill=fill)
    d.polygon([(cx - r * 1.9, cy - r * 0.3), (cx + r * 1.9, cy - r * 0.3), (cx, cy + r * 2.0)], fill=fill)


def icon(name, draw):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    draw(ImageDraw.Draw(img))
    outline(img, INK).save(os.path.join(ROOT, "ui", f"icon_{name}.png"))


def tend(d):  # a heart with a hand under it
    heart(d, 12, 9, 3.4, PINK)
    d.rounded_rectangle([4, 16, 20, 20], radius=2, fill=BONE)
    d.line([(6, 16), (6, 14)], fill=BONE, width=2)


def scratch(d):  # three short strokes and a heart
    for k in range(3):
        d.line([(4 + k * 4, 18), (8 + k * 4, 6)], fill=BONE, width=2)
    heart(d, 18, 16, 1.8, PINK)


def play(d):  # a ball with a seam
    d.ellipse([4, 4, 20, 20], fill=(230, 120, 60, 255))
    d.arc([4, 4, 20, 20], 200, 340, fill=(250, 230, 200, 255), width=2)
    d.arc([4, 9, 20, 25], 200, 340, fill=(250, 230, 200, 255), width=2)


def groom(d):  # a brush: wooden back, fibre bristles
    d.rounded_rectangle([3, 8, 20, 13], radius=2, fill=WOOD)
    for x in range(5, 20, 2):
        d.line([(x, 14), (x, 19)], fill=FIBRE)
    d.line([(20, 10), (22, 6)], fill=WOOD_D, width=2)


def comfort(d):  # a tear and a soft cloud
    d.ellipse([3, 10, 21, 20], fill=SKY)
    d.ellipse([7, 6, 17, 16], fill=SKY)
    d.polygon([(12, 2), (9, 8), (15, 8)], fill=(90, 150, 220, 255))
    d.ellipse([9, 6, 15, 11], fill=(90, 150, 220, 255))


def treasure(d):  # a heap of coins
    for (x, y) in ((5, 14), (11, 14), (8, 9), (14, 10), (11, 5)):
        d.ellipse([x, y, x + 7, y + 5], fill=GOLD, outline=GOLD_D)


def food(d):  # a drumstick
    d.ellipse([3, 3, 15, 15], fill=(200, 92, 70, 255))
    d.line([(13, 13), (19, 19)], fill=BONE, width=3)
    d.ellipse([17, 16, 22, 21], fill=BONE)


def train(d):  # a hoop to jump through, with a spark
    d.ellipse([4, 4, 20, 20], outline=GOLD, width=3)
    d.line([(12, 9), (12, 15)], fill=BONE, width=2)
    d.line([(9, 12), (15, 12)], fill=BONE, width=2)


def call(d):  # a whistle with sound lines
    d.rounded_rectangle([2, 11, 14, 17], radius=3, fill=(200, 205, 215, 255))
    d.rectangle([12, 9, 15, 13], fill=(200, 205, 215, 255))
    for k, r in enumerate((4, 7)):
        d.arc([16 - r, 12 - r, 16 + r, 12 + r], 300, 60, fill=BONE, width=2)


def stay(d):  # an open palm, held up
    d.rounded_rectangle([6, 11, 18, 21], radius=3, fill=BONE)
    for k, x in enumerate((6, 10, 14)):
        d.rectangle([x, 3 + k % 2, x + 2, 11], fill=BONE)
    d.rectangle([17, 9, 20, 15], fill=BONE)  # thumb


def items():
    # Scale brush: a wooden back with a fibre fringe.
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([6, 10, 26, 16], radius=3, fill=WOOD, outline=WOOD_D)
    for x in range(8, 25, 2):
        d.line([(x, 17), (x + 1, 23)], fill=FIBRE)
    d.line([(8, 12), (24, 12)], fill=(200, 150, 96, 255))
    outline(img, INK).save(os.path.join(ROOT, "items", "scale_brush.png"))

    # Feeding trough: a plank box, seen from above, with meat in it.
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([4, 9, 27, 23], fill=WOOD, outline=WOOD_D)
    d.rectangle([7, 12, 24, 20], fill=(96, 64, 38, 255))
    d.ellipse([9, 13, 16, 19], fill=(200, 92, 70, 255))
    d.ellipse([15, 14, 22, 19], fill=(180, 80, 62, 255))
    img.save(os.path.join(ROOT, "buildings", "feeding_trough.png"))

    # Heart particle (tinted pink by the renderer, so drawn white).
    img = Image.new("RGBA", (9, 8), (0, 0, 0, 0))
    heart(ImageDraw.Draw(img), 4.5, 3, 1.9, (255, 255, 255, 255))
    img.save(os.path.join(ROOT, "fx", "heart.png"))


def generate():
    for name, draw in (("tend", tend), ("scratch", scratch), ("play", play), ("groom", groom), ("comfort", comfort),
                       ("treasure", treasure), ("feed", food), ("train", train), ("call", call), ("stay", stay)):
        icon(name, draw)
    items()


if __name__ == "__main__":
    generate()
