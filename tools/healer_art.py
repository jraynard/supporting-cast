#!/usr/bin/env python3
"""The empath's art: the six rune glyphs woven into mendings (``ui/rune_*``, see
data/defs/healing.json), each a carved mark on a worn stone disc in its own colour. Writes only its
own files. Run alone (``python3 tools/healer_art.py``) or from ``gen_placeholder_art.py``.
"""
import math
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
TEXTURES = os.path.join(HERE, "..", "data", "textures")
S = 64  # drawn large in the weave
UP = 4
K = S / 32  # line weights were set at 32 px  # drawn large and shrunk, for soft edges
STONE = (206, 196, 176)
STONE_DARK = (150, 140, 122)


def save(img, rel):
    path = os.path.join(TEXTURES, rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def disc():
    img = Image.new("RGBA", (S * UP, S * UP), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = S * UP // 2 - int(UP * K)
    c = S * UP // 2
    d.ellipse((c - r, c - r + UP * K, c + r, c + r + UP * K), fill=(*STONE_DARK, 255))
    d.ellipse((c - r, c - r, c + r, c + r - UP * K), fill=(*STONE, 255))
    return img, d


def p(x, y):
    """Glyph coordinates (-1..1 across the disc) to pixels."""
    c = S * UP / 2
    k = S * UP * 0.3
    return (c + x * k, c + y * k)


def line(d, pts, colour, w=2.6):
    d.line([p(*q) for q in pts], fill=(*colour, 255), width=int(w * UP * K), joint="curve")
    for q in (pts[0], pts[-1]):
        x, y = p(*q)
        r = w * UP * K / 2
        d.ellipse((x - r, y - r, x + r, y + r), fill=(*colour, 255))


def finish(img):
    return img.resize((S, S), Image.LANCZOS)


def thread():
    """A seam pulled shut: a line with stitches across it."""
    img, d = disc()
    col = (150, 60, 70)
    line(d, [(0, -1), (0, 1)], col)
    for y in (-0.6, 0, 0.6):
        line(d, [(-0.55, y - 0.2), (0.55, y + 0.2)], col, 2.2)
    return finish(img)


def flesh():
    """The body: a ring around a heart's dot."""
    img, d = disc()
    col = (176, 74, 74)
    r = S * UP * 0.26
    c = S * UP / 2
    d.ellipse((c - r, c - r, c + r, c + r), outline=(*col, 255), width=int(2.6 * UP * K))
    k = S * UP * 0.07
    d.ellipse((c - k, c - k, c + k, c + k), fill=(*col, 255))
    return finish(img)


def ember():
    """Warmth rising: a flame's tongue over a coal."""
    img, d = disc()
    col = (206, 106, 30)
    line(d, [(-0.5, 0.7), (-0.15, -0.1), (0, -0.95), (0.15, -0.1), (0.5, 0.7)], col)
    line(d, [(-0.55, 0.8), (0.55, 0.8)], col)
    return finish(img)


def rime():
    """Cold: a six-armed frost star."""
    img, d = disc()
    col = (70, 130, 190)
    for i in range(3):
        a = i * math.pi / 3 + math.pi / 2
        x, y = math.cos(a), math.sin(a)
        line(d, [(-x * 0.95, -y * 0.95), (x * 0.95, y * 0.95)], col, 2.2)
    return finish(img)


def salt():
    """Purging: a circle cut through, as salt's old mark."""
    img, d = disc()
    col = (110, 110, 120)
    r = S * UP * 0.26
    c = S * UP / 2
    d.ellipse((c - r, c - r, c + r, c + r), outline=(*col, 255), width=int(2.6 * UP * K))
    line(d, [(-0.95, 0), (0.95, 0)], col)
    return finish(img)


def still():
    """Calm: two long, low waves, like water settling."""
    img, d = disc()
    col = (80, 140, 110)
    for y in (-0.3, 0.35):
        pts = [(x / 10, y + 0.12 * math.sin(x / 10 * math.pi)) for x in range(-9, 10)]
        line(d, pts, col, 2.4)
    return finish(img)


def charm():
    """The charm item: a strip of linen tied in a loop, knotted, a little stone token on it."""
    n = 32 * UP
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    linen, shade = (232, 224, 204, 255), (180, 168, 142, 255)
    # The loop of cloth, and its two loose tails.
    d.ellipse((5 * UP, 3 * UP, 27 * UP, 21 * UP), outline=shade, width=4 * UP)
    d.ellipse((5 * UP, 2 * UP, 27 * UP, 20 * UP), outline=linen, width=3 * UP)
    d.line((14 * UP, 20 * UP, 10 * UP, 29 * UP), fill=linen, width=3 * UP)
    d.line((18 * UP, 20 * UP, 22 * UP, 29 * UP), fill=linen, width=3 * UP)
    # The knot, and the token tied at it.
    d.ellipse((12 * UP, 16 * UP, 20 * UP, 23 * UP), fill=shade)
    d.ellipse((12 * UP, 9 * UP, 20 * UP, 17 * UP), fill=(150, 140, 122, 255))
    d.ellipse((12 * UP, 8 * UP, 20 * UP, 16 * UP), fill=(206, 196, 176, 255))
    d.line((16 * UP, 9.5 * UP, 16 * UP, 14.5 * UP), fill=(120, 96, 200, 255), width=int(1.5 * UP))
    d.line((14 * UP, 12 * UP, 18 * UP, 12 * UP), fill=(120, 96, 200, 255), width=int(1.5 * UP))
    return img.resize((32, 32), Image.LANCZOS)


def generate():
    for name, fn in (("thread", thread), ("flesh", flesh), ("ember", ember), ("rime", rime), ("salt", salt), ("still", still)):
        save(fn(), f"ui/rune_{name}")
    save(charm(), "items/charm")


if __name__ == "__main__":
    generate()
