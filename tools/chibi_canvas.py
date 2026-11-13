#!/usr/bin/env python3
"""The canvas the side-on chibi creatures are drawn on (tools/chibi_dragon.py, chibi_animals.py,
chibi_nature.py): shapes in world pixels, drawn K times finer, shaded with norse_art's light and
outlined in ink. A canvas can record which layer owns each pixel, so a figure painted back to front
can be split into layers afterwards; most callers draw everything into "base".
"""
import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(__file__))
from norse_art import ribbon, catmull, shade_shape  # noqa: E402

K = 4                                   # sheet pixels per world pixel ("density": 4)

INK = (34, 18, 26, 255)
WHITE = (252, 250, 244, 255)
PUPIL = (30, 16, 28, 255)


class Canvas:
    """A frame in world pixels (w, h) drawn K times finer; ``img`` the picture, ``ids`` who owns each pixel."""

    def __init__(self, w, h, k=None, layers=("base",)):
        """``k``: sheet pixels per world pixel (K, or more to draw a figure bigger than its design).
        ``layers``: the names a pixel can belong to, the first being the base that takes the ink."""
        self.k = k or K
        self.layers = layers
        self.id = {name: i for i, name in enumerate(layers)}
        self.w, self.h = w, h
        self.W, self.H = round(w * self.k), round(h * self.k)
        self.img = Image.new("RGBA", (self.W, self.H), (0, 0, 0, 0))
        self.ids = Image.new("L", (self.W, self.H), 0)

    def P(self, x, y):
        return x * self.k + (self.k - 1) / 2, y * self.k + (self.k - 1) / 2

    def mask(self, fn):
        m = Image.new("L", (self.W, self.H), 0)
        fn(ImageDraw.Draw(m))
        return m

    def poly(self, pts):
        return self.mask(lambda d: d.polygon([self.P(*p) for p in pts], fill=255))

    def ellipse(self, cx, cy, rx, ry):
        return self.mask(lambda d: d.ellipse([(cx - rx) * self.k, (cy - ry) * self.k, (cx + rx) * self.k, (cy + ry) * self.k], fill=255))

    def strip(self, spine, widths, steps=6):
        pts = ribbon([self.P(*p) for p in spine], [w * self.k for w in widths], steps=steps)
        return self.mask(lambda d: d.polygon(pts, fill=255))

    def empty(self):
        return Image.new("L", (self.W, self.H), 0)

    # painting
    def paint(self, m, colors, layer="base", radius=1.6, line=True, outside=None, line_color=INK, **kw):
        """Shade a part in its colours, owned by the layer, outlined in ink (only where it's
        ``outside`` a mask, for a part fused onto another: no seam lines)."""
        if m.getbbox() is None:
            return
        tmp = Image.new("RGBA", (self.W, self.H), (0, 0, 0, 0))
        shade_shape(tmp, m, colors, radius=radius * self.k, **kw)
        self.put(tmp, m, layer)
        if line:
            self.ink(m, outside=outside, color=line_color)

    def fill(self, m, color, layer="base", only=None):
        if only is not None:
            m = ImageChops.multiply(m, only)
        self.img.paste(color, mask=m)
        self.ids.paste(self.id[layer], mask=m)

    def put(self, tmp, m, layer="base"):
        self.img.paste(tmp, mask=m)
        self.ids.paste(self.id[layer], mask=m)

    def ink(self, m, width=2, outside=None, color=INK):
        m = m.point(lambda v: 255 if v >= 128 else 0)
        rim = ImageChops.subtract(m, m.filter(ImageFilter.MinFilter(2 * width + 1)))
        if outside is not None:
            rim = minus(rim, outside.filter(ImageFilter.MinFilter(3)))
        self.fill(rim, color, self.layers[0])

    def curve(self, pts, color, layer="base", width=2, only=None, design=True):
        pts = [self.P(*p) for p in pts] if design else pts
        line_pts = catmull(pts, 6) if len(pts) > 2 else pts
        m = self.mask(lambda d: d.line(line_pts, fill=255, width=width, joint="curve"))
        self.fill(m, color, layer, only)

    def split(self):
        """Per layer: its own pixels."""
        out = {}
        for name in self.layers:
            sel = self.ids.point(lambda v, i=self.id[name]: 255 if v == i else 0)
            layer = Image.new("RGBA", (self.W, self.H), (0, 0, 0, 0))
            layer.paste(self.img, mask=ImageChops.multiply(sel, self.img.getchannel("A")))
            out[name] = layer
        return out


def union(*ms):
    out = Image.new("L", ms[0].size, 0)
    for m in ms:
        out.paste(255, mask=m)
    return out


def minus(a, b):
    out = a.copy()
    out.paste(0, mask=b)
    return out


def both(a, b):
    return ImageChops.multiply(a, b)


def lerp(a, b, t):
    return a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t


def rot(v, deg):
    a = math.radians(deg)
    return v[0] * math.cos(a) - v[1] * math.sin(a), v[0] * math.sin(a) + v[1] * math.cos(a)


def add(a, b, k=1.0):
    return a[0] + b[0] * k, a[1] + b[1] * k
