#!/usr/bin/env python3
"""The candlemaker mod's art, made from nothing (docs/MODDING.md): run from the repo root.

    python3 mods/candlemaker/tools/candlemaker_assets.py

Writes into the mod's own textures/, never the game's. Placeholder sprites, 32x32: the dipping rod
(wicks hung from a stick), a dipped candle, and a lumpy one.
"""
import os
from PIL import Image, ImageDraw

MOD = os.path.join(os.path.dirname(__file__), "..")
OUT = os.path.join(MOD, "textures", "items")

WAX = (236, 222, 180, 255)
WAX_SHADE = (196, 176, 128, 255)
WICK = (60, 44, 30, 255)
OAK = (122, 84, 52, 255)
OAK_DARK = (74, 51, 34, 255)


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, name + ".png"))


def rod():
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([3, 6, 28, 8], fill=OAK, outline=OAK_DARK)
    for x in (9, 16, 23):
        d.line([x, 9, x, 15], fill=WICK)
        d.rectangle([x - 1, 16, x + 1, 25], fill=WAX, outline=WAX_SHADE)
    return img


def candle(lumpy):
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([16, 3, 16, 7], fill=WICK)
    d.rectangle([12, 7, 20, 28], fill=WAX, outline=WAX_SHADE)
    d.line([19, 8, 19, 27], fill=WAX_SHADE)
    if lumpy:
        for (x, y, r) in ((11, 12, 2), (20, 17, 3), (12, 22, 2), (19, 25, 2)):
            d.ellipse([x - r, y - r, x + r, y + r], fill=WAX, outline=WAX_SHADE)
    return img


if __name__ == "__main__":
    save(rod(), "dipping_rod")
    save(candle(False), "candle")
    save(candle(True), "candle_lumpy")
    print("wrote", OUT)
