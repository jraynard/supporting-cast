#!/usr/bin/env python3
"""Drawing for the hi-res chibi people (tools/chibi_people.py): the palette, and helpers that take
shapes in design units (the game's 40x60 world pixels of a person's frame) and draw them into
sheets K times finer (K = 4: 160x240 frames, "density": 4 in their manifests, so the game shows them
at the same size). Shapes are shaded with norse_art's light and outlined inside their own edge, so
a layer drawn alone still has its line; fine work (eyes, strands, rivets) goes in sheet pixels.
"""
import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(__file__))
from norse_art import ramp, ribbon, catmull, shade_shape, bayer  # noqa: E402,F401
from norse_people import SKIN_GREYS, HAIR_GREYS, EYE_GREYS, DYE_GREYS  # noqa: E402

K = 4                                   # sheet pixels per design (world) pixel
DW, DH = 40, 60                         # the design frame
W, H = DW * K, DH * K
PIVOT = (20 * K, 58 * K)
VIEWS = ("down", "downside", "upside", "up")

INK = (40, 28, 44, 255)                 # the line
IRON = ramp((150, 158, 170), 5, dark=0.45, light=1.35)
IRON_DARK = ramp((96, 102, 116), 5)
HORN = ramp((228, 216, 186), 5, dark=0.55, light=1.12)
LEATHER = ramp((122, 78, 48), 5)
LEATHER_DARK = ramp((84, 54, 38), 5)
FUR = ramp((104, 84, 70), 5, dark=0.5, light=1.55)
LINEN = ramp((214, 200, 172), 5)
GOLD = ramp((214, 170, 64), 4)
SILVER = ramp((196, 204, 214), 4)
WHITE = (250, 248, 244, 255)
PUPIL = (36, 22, 52, 255)
LASH = (40, 26, 40, 255)
LID = (172, 118, 110, 255)
BROW = (118, 82, 48, 255)
BLUSH = (240, 158, 150, 255)
BLUSH_SOFT = (246, 190, 176, 255)
MOUTH = (110, 52, 58, 255)
NOSE = (214, 150, 130, 255)
WALNUT = ramp((128, 92, 60), 4)


def grey(v):
    return (v, v, v, 255)


SKIN = [grey(v) for v in SKIN_GREYS]
HAIR = [grey(v) for v in HAIR_GREYS]
DYE = [grey(v) for v in DYE_GREYS]
IRIS = [grey(v) for v in EYE_GREYS]
SKIN_LINE = grey(92)
HAIR_LINE = grey(58)
DYE_LINE = grey(40)



def P(x, y):
    """A design point to sheet pixels (the centre of its K x K block)."""
    return x * K + (K - 1) / 2, y * K + (K - 1) / 2


def mask(draw):
    m = Image.new("L", (W, H), 0)
    draw(ImageDraw.Draw(m))
    return m


def poly(points):
    return mask(lambda d: d.polygon([P(x, y) for x, y in points], fill=255))


def box(b):
    """A design box (inclusive pixels) in sheet pixels."""
    return [b[0] * K, b[1] * K, b[2] * K + K - 1, b[3] * K + K - 1]


def ellipse(b):
    return mask(lambda d: d.ellipse(box(b), fill=255))


def rect(b):
    return mask(lambda d: d.rectangle(box(b), fill=255))


def oval(cx, cy, rx, ry):
    """An ellipse in sheet pixels, about a centre."""
    return mask(lambda d: d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=255))


def strip(spine, widths, steps=6):
    """A ribbon (limb, horn, lock) along design points, widths in design pixels."""
    pts = ribbon([P(x, y) for x, y in spine], [w * K for w in widths], steps=steps)
    return mask(lambda d: d.polygon(pts, fill=255))


def union(*ms):
    out = Image.new("L", (W, H), 0)
    for m in ms:
        out.paste(255, mask=m)
    return out


def minus(a, b):
    out = a.copy()
    out.paste(0, mask=b)
    return out


def both(a, b):
    return ImageChops.multiply(a, b)


def edge(m, width):
    """The rim of a shape, ``width`` pixels in from its edge."""
    inner = m.filter(ImageFilter.MinFilter(2 * width + 1))
    return ImageChops.subtract(m, inner)


def ink(img, m, color, width=2):
    """Line the inside of a shape's edge, so the part carries its own outline."""
    img.paste(color, mask=edge(m.point(lambda v: 255 if v >= 128 else 0), width))


def shade(img, m, colors, radius=1.6, **kw):
    shade_shape(img, m, colors, radius=radius * K, **kw)


def part(img, m, colors, line, radius=1.6, width=2, **kw):
    shade(img, m, colors, radius=radius, **kw)
    ink(img, m, line, width)


def fill(img, m, color, only=None):
    img.paste(color, mask=both(m, only) if only is not None else m)


def fine(img, pts, color, only=None):
    """Sheet pixels; with ``only`` (a mask), just where it's set."""
    px = img.load()
    om = only.load() if only is not None else None
    for x, y in pts:
        x, y = math.floor(x + 0.5), math.floor(y + 0.5)           # not round(): halves to even skip pixels
        if 0 <= x < W and 0 <= y < H and (om is None or om[x, y] >= 128):
            px[x, y] = color


def curve(img, pts, color, width=1, only=None, design=True):
    """A smooth line through points (design units, or sheet pixels with design=False)."""
    pts = [P(x, y) for x, y in pts] if design else pts
    line_pts = catmull(pts, 6) if len(pts) > 2 else pts
    m = mask(lambda d: d.line(line_pts, fill=255, width=width, joint="curve"))
    fill(img, m, color, only)


def line(img, a, b, color, only=None):
    """A one-pixel line between sheet points."""
    curve(img, [a, b], color, 1, only, design=False)


def lerp(a, b, t):
    return a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t


BRONZE = ramp((182, 128, 62), 5)
STEEL = ramp((146, 156, 174), 5, dark=0.45, light=1.28)
MAIL = ramp((140, 146, 156), 4)
BEAD_BLUE = (70, 130, 190, 255)
BEAD_AMBER = (214, 150, 52, 255)


def blank():
    return Image.new("RGBA", (W, H), (0, 0, 0, 0))


def nothing():
    return Image.new("L", (W, H), 0)


def alpha(img):
    """An image's opaque pixels as a mask."""
    return img.getchannel("A").point(lambda v: 255 if v else 0)


def cut(img, m):
    """Clear an image where a mask is set."""
    img.paste((0, 0, 0, 0), mask=m)


def mirror_pt(pt):
    return 40 - pt[0], pt[1]
