"""Nordic trees for the HD-2D art pass: the spruce, oak, birch and the burnt snag.

The spruce: a tall, layered spruce of drooping bough tiers with a
blue-green, cool-shadowed palette, flaky bark and moss at the roots. Replaces the old `pine`
(`things/pine`, `_v2`, `_v3`); the species keeps its name so every def and save stays valid.

Trees stand on the bottom row of the image with the trunk base centred (docs/ART.md). The sprite is
56x96 (3 cells tall, so the Pine def's renderSize is 3.0). All three variants share one palette and
tier layout statistics, so a grove has one average colour.
"""
import math
import os
import random

from PIL import Image, ImageDraw

from norse_art import ramp, ribbon, mask_of, shade_shape, shade_over, selective_outline, bayer

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
W, H = 56, 96
SIZE = (W, H)
CX = 27.5   # the trunk axis: the image mirrors about it (pixel x <-> 55 - x), so boughs and ribs match left and right

NEEDLE = ramp((54, 102, 88), 6, main=2, dark=0.38, light=1.58, cool=20, warm=28)   # blue-green spruce
BARK = ramp((98, 74, 58), 5, main=2, dark=0.42, light=1.35)
MOSS = ramp((86, 114, 66), 4, main=1, dark=0.5, light=1.35)


def variant_name(base, i):
    return base if i == 0 else f"{base}_v{i + 1}"


def trunk(img):
    """A tapering trunk with evenly spaced furrows, flared roots and moss on the shaded left."""
    top, bottom = 50, H - 1
    m = mask_of(SIZE, lambda d: d.polygon([
        (24, top), (31, top), (31, bottom - 10), (34, bottom - 3), (36, bottom),
        (19, bottom), (21, bottom - 3), (24, bottom - 10)], fill=255))

    def furrows(x, y):
        c = int(abs(x - CX))                                   # columns from the axis: furrows mirror
        groove = (c == 2 and y % 11 != 0) or (c == 5 and y > bottom - 10)
        return (-0.22 if groove else 0.0) + bayer(c, y) * 0.04
    shade_shape(img, m, BARK, radius=1.6, tex=furrows, depth=0.15)
    moss = mask_of(SIZE, lambda d: (d.ellipse([18, bottom - 6, 25, bottom], fill=255), d.ellipse([22, bottom - 12, 26, bottom - 5], fill=255)))
    moss = Image.composite(moss, Image.new("L", SIZE, 0), m)
    shade_shape(img, moss, MOSS, radius=1.0, tex=lambda x, y: bayer(x, y) * 0.2)


def tier_mask(top, bottom, hw, rnd):
    """One skirt of boughs, mirrored about the axis: a triangle whose lower edge breaks into drooping,
    pointed boughs. Returns the mask and the boughs as (distance from the axis, length) per side."""
    hw = math.floor(hw) + 0.5
    pairs = max(2, int(hw / 3.0) + 1)
    lobes = []
    for m in range(pairs):                                   # m = 0 is nearest the axis
        d = math.floor((m + 0.5) * hw / pairs) + 0.5
        end = m / (pairs - 1)                                # the outermost boughs droop furthest
        lobes.append((d, rnd.uniform(4, 7) + end * 4, (hw / pairs) * 1.05))

    def draw(dr):
        dr.polygon([(27, top), (28, top), (CX + hw, bottom - 5), (CX - hw, bottom - 5)], fill=255)
        for d, length, width in lobes:
            for x in (CX - d, CX + d):
                dr.polygon([(x - width / 2, bottom - 7), (x + width / 2, bottom - 7), (x, bottom - 5 + length)], fill=255)
                dr.ellipse([x - width * 0.32, bottom - 9, x + width * 0.32, bottom - 4 + length * 0.2], fill=255)
    return mask_of(SIZE, draw), lobes, hw


def cast_shadow(img, upper, lower_cols, dy=4):
    """Darken the tier below where the tier above shades it: pixels just under the upper silhouette."""
    px = img.load()
    mk = upper.load()
    index = {c: i for i, c in enumerate(lower_cols)}
    box = upper.getbbox()
    if not box:
        return
    for y in range(box[1], min(H, box[3] + dy + 1)):
        for x in range(max(0, box[0]), min(W, box[2])):
            sy = y - dy
            if 0 <= sy < H and mk[x, sy] > 127 and mk[x, y] < 128 and px[x, y][3] and px[x, y] in index:
                px[x, y] = lower_cols[max(0, index[px[x, y]] - 1)]


def ribs(img, lobes, top, span, bottom):
    """A short, clean rib down each bough, drawn on the left and mirrored exactly onto the right,
    with a light pixel above it. They start well out from the axis so the middle stays calm."""
    rib = set()
    for d, length, _ in lobes:
        x0, y0 = CX - d * 0.45, top + span * 0.45
        x1, y1 = CX - d, bottom - 4 + length * 0.3
        steps = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        for s in range(steps):
            t = s / steps
            rib.add((math.floor(x0 + (x1 - x0) * t + 0.5), math.floor(y0 + (y1 - y0) * t + 0.5)))
    pairs = rib | {(55 - x, y) for x, y in rib}
    light = {(x, y - 1) for x, y in pairs} - pairs
    for group, color in ((light, NEEDLE[4]), (pairs, NEEDLE[1])):
        for x, y in group:
            if 0 <= x < W and 0 <= y < H and img.getpixel((x, y))[3]:
                img.putpixel((x, y), color)


def spruce(seed):
    rnd = random.Random(seed)
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    trunk(img)
    tiers = 7
    for k in range(tiers):
        top = 55 - k * 8.4 + rnd.uniform(-1, 1)
        hw = 20.5 - k * 2.9 + rnd.uniform(-0.6, 0.6)
        bottom = top + 18 - k * 0.5
        if k == tiers - 1:
            top, hw, bottom = 3 + rnd.uniform(0, 1.5), 3.5, 19
        m, lobes, hw = tier_mask(top, bottom, hw, rnd)
        span = max(1.0, bottom - top)

        def tex(x, y, top=top, span=span, hw=hw, k=k):
            u = (x - CX) / max(1.0, hw)
            t = (y - top) / span
            c = int(abs(x - CX))                                # mirrored needle pattern: a herringbone, not noise
            needles = -0.05 if (c * 2 + y * 3 + k) % 11 == 0 else (0.04 if (c + y * 2 + k * 3) % 13 == 0 else 0.0)
            return -u * 0.2 + 0.2 - 0.34 * max(0.0, (t - 0.35) / 0.65) + needles + bayer(c, y) * 0.012
        painted = shade_shape(img, m, NEEDLE, radius=1.8, tex=tex, depth=0.1, slope=0.7)
        if k < tiers - 1:
            ribs(img, lobes, top, span, bottom)
        for (x, y) in painted:                                  # frost-lit tips on the upper left edge
            if rnd.random() < 0.05 and x < CX and (y - top) < span * 0.55:
                img.putpixel((x, y), NEEDLE[5])
        cast_shadow(img, m, NEEDLE)
    return selective_outline(img, strength=0.3, cool=0.3)


# ---------------------------------------------------------------- broadleaf trees

LEAF = ramp((70, 110, 62), 6, main=2, dark=0.36, light=1.75, cool=22, warm=34)      # moss-olive with warm tips
BIRCH_LEAF = ramp((112, 146, 70), 6, main=2, dark=0.4, light=1.6, cool=18, warm=30)
WHITE_BARK = ramp((228, 224, 214), 4, main=2, dark=0.55, light=1.05, cool=26, warm=8)
CHAR = ramp((60, 50, 50), 5, main=2, dark=0.4, light=1.9, cool=24, warm=8)
ASH = ramp((128, 124, 124), 3, main=1, dark=0.6, light=1.25)
GLOW, GLOW_HI, GLOW_DIM = (255, 140, 40, 255), (255, 224, 110, 255), (176, 78, 32, 255)


def clump(cx, cy, r, rnd, size):
    """A rounded, slightly lumpy clump of leaves as a mask."""
    ph = rnd.uniform(0, math.tau)
    pts = [(cx + math.cos(a) * r * (1 + 0.09 * math.sin(3 * a + ph) + 0.05 * math.sin(5 * a)),
            cy + math.sin(a) * r * 0.92 * (1 + 0.09 * math.sin(3 * a + ph + 1) + 0.05 * math.sin(5 * a)))
           for a in (k / 18 * math.tau for k in range(18))]
    return mask_of(size, lambda d: d.polygon(pts, fill=255))


def foliage(img, circles, colors, rnd, top, bottom, line_step=1):
    """Paint leaf clumps back to front. Higher clumps catch more light, lower ones sit in shade, each is
    rimmed where it overlaps the one behind it, and the whole gets sprinkled with sunlit leaf tips."""
    size = img.size
    painted_all = []
    for cx, cy, r in sorted(circles, key=lambda c: c[1]):
        m = clump(cx, cy, r, rnd, size)
        k = rnd.randrange(8)

        def tex(x, y, cy=cy, k=k):
            height = (y - top) / max(1.0, bottom - top)
            leaves = -0.08 if (x * 2 + y * 3 + k) % 8 == 0 else (0.07 if (x + y * 2 + k) % 9 == 0 else 0.0)
            return 0.2 - 0.42 * height + leaves + bayer(x, y) * 0.04
        painted_all += shade_over(img, m, colors, colors[line_step], radius=max(1.6, r * 0.34), tex=tex, depth=0.12, slope=0.9)
    for x, y in painted_all:                                  # sunlit tips on the upper-left of the crown
        if rnd.random() < 0.045 and (y - top) < (bottom - top) * 0.6:
            img.putpixel((x, y), colors[5])


def trunk_shape(img, cx, bottom, top, half_top, half_bottom, bark, rnd, flare=5, furrow=3, moss=True):
    """A tapering trunk with a flared foot, wobbling furrows and moss on the shaded left."""
    pts = [(cx - half_top, top), (cx + half_top, top), (cx + half_bottom, bottom - 10),
           (cx + half_bottom + flare, bottom - 2), (cx + half_bottom + flare + 2, bottom),
           (cx - half_bottom - flare - 2, bottom), (cx - half_bottom - flare, bottom - 2), (cx - half_bottom, bottom - 10)]
    m = mask_of(img.size, lambda d: d.polygon(pts, fill=255))
    ph = rnd.uniform(0, 6)

    def tex(x, y):
        c = int(abs(x - cx))
        wob = int(1.4 * math.sin(y / 6.0 + ph))
        return (-0.22 if (c + wob) % furrow == 0 and y % 13 != 0 else 0.0) + bayer(x, y) * 0.04
    shade_shape(img, m, bark, radius=1.8, tex=tex, depth=0.15)
    if moss:
        mm = Image.composite(mask_of(img.size, lambda d: (d.ellipse([cx - half_bottom - flare - 3, bottom - 8, cx - 2, bottom], fill=255),
                                                              d.ellipse([cx - half_bottom, bottom - 16, cx - half_bottom + 5, bottom - 6], fill=255))),
                             Image.new("L", img.size, 0), m)
        shade_shape(img, mm, MOSS, radius=1.0, tex=lambda x, y: bayer(x, y) * 0.2)
    return m


def oak(seed):
    """A broad old oak: a thick, gnarled trunk under a crown of overlapping leaf clumps, moss at the roots."""
    rnd = random.Random(seed)
    w, h = 72, 80
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    cx = w / 2
    trunk_shape(img, cx, h - 1, 46, 6, 8, BARK, rnd)
    for dx in (-1, 1):                                        # limbs reaching into the crown
        spine = [(cx + dx * 3, 52), (cx + dx * 10 + rnd.uniform(-2, 2), 44), (cx + dx * 17, 36 + rnd.uniform(-3, 2))]
        shade_shape(img, mask_of(img.size, lambda d: d.polygon(ribbon(spine, [8, 6, 3.5], 6), fill=255)), BARK, radius=1.4)
    circles = [(cx + rnd.uniform(-3, 3), 30 + rnd.uniform(-2, 2), 15 + rnd.uniform(0, 2))]
    for k in range(10):
        a = k / 10 * math.tau + rnd.uniform(-0.2, 0.2)
        d = rnd.uniform(13, 21)
        circles.append((cx + math.cos(a) * d * 1.3, 30 + math.sin(a) * d * 0.78, rnd.uniform(9, 13)))
    for dx in (-17, 0, 17):                                    # a lower fringe so the crown has weight
        circles.append((cx + dx + rnd.uniform(-3, 3), 49 + rnd.uniform(-2, 2), rnd.uniform(8, 10)))
    foliage(img, circles, LEAF, rnd, top=6, bottom=58)
    return selective_outline(img, strength=0.3, cool=0.3)


def birch(seed):
    """A slender white birch with black bark marks, leaning a little, under an airy drooping crown."""
    rnd = random.Random(seed)
    w, h = 56, 88
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    lean = rnd.uniform(-2.5, 2.5)
    cx = w / 2

    def trunk_x(y):
        return cx + lean * (1 - (y - 18) / (h - 18)) if y >= 18 else cx + lean
    top, bottom = 18, h - 1
    pts_l = [(trunk_x(y) - (1.8 + 2.2 * (y - top) / (bottom - top)), y) for y in range(top, bottom + 1, 4)]
    pts_r = [(trunk_x(y) + (1.8 + 2.2 * (y - top) / (bottom - top)), y) for y in range(top, bottom + 1, 4)]
    m = mask_of(img.size, lambda d: d.polygon(pts_l + [(trunk_x(bottom) - 5, bottom), (trunk_x(bottom) + 5, bottom)] + pts_r[::-1], fill=255))
    marks = set()
    for y in range(top + 6, bottom - 4, 5):                   # black lenticels: short horizontal dashes
        if rnd.random() < 0.8:
            x = int(trunk_x(y) + rnd.uniform(-2, 2))
            for k in range(rnd.randint(2, 4)):
                marks.add((x + k, y))

    def bark_tex(x, y):
        return -0.18 if (x, y) in marks else (bayer(x, y) * 0.04)
    shade_shape(img, m, WHITE_BARK, radius=1.4, tex=bark_tex, depth=0.1)
    for x, y in marks:
        if 0 <= x < w and img.getpixel((x, y))[3]:
            img.putpixel((x, y), (42, 40, 44, 255))
    foot = mask_of(img.size, lambda d: d.ellipse([trunk_x(bottom) - 9, bottom - 5, trunk_x(bottom) - 2, bottom], fill=255))
    shade_shape(img, Image.composite(foot, Image.new("L", img.size, 0), mask_of(img.size, lambda d: d.rectangle([0, bottom - 6, w, bottom], fill=255))), MOSS, radius=0.9)
    circles = []
    for k in range(20):                                        # an oval crown, widest a little below its middle
        y = 12 + 38 * (k / 19) + rnd.uniform(-3, 3)
        t = (y - 30) / 20.0
        spread = 16 * math.sqrt(max(0.05, 1 - t * t)) * (1.0 if y > 30 else 0.9)
        circles.append((cx + lean * 0.4 + rnd.uniform(-spread, spread) * 0.9, y, rnd.uniform(6.5, 9.5)))
    foliage(img, circles, BIRCH_LEAF, rnd, top=4, bottom=58)
    for y in range(46, 60):                                   # the trunk shows through the lower crown
        if rnd.random() < 0.35:
            x = int(trunk_x(y))
            img.putpixel((x, y), WHITE_BARK[2])
    return selective_outline(img, strength=0.3, cool=0.3)


def snag(seed):
    """What a burned tree leaves: a split, charred trunk with a few broken limbs, ash at its foot and
    embers still glowing in the cracks."""
    rnd = random.Random(seed)
    w, h = 44, 64
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    cx = w / 2
    lean = rnd.uniform(-2, 2)
    top = rnd.randint(8, 14)
    left = [(cx - 2.5 - 3.5 * ((y - top) / (h - top)) ** 1.4 + lean * (1 - (y - top) / (h - top)), y) for y in range(top + 2, h, 4)]
    right = [(cx + 2.5 + 3.5 * ((y - top) / (h - top)) ** 1.4 + lean * (1 - (y - top) / (h - top)), y) for y in range(top + 2, h, 4)]
    notch = rnd.choice((-1, 1))
    tip = [(cx - 3 + lean, top + 4), (cx - 1 + lean, top), (cx + 1 + lean + notch, top + 5), (cx + 3 + lean, top + 1)]   # a splintered top
    flare = [(left[-1][0] - 4, h - 1), (right[-1][0] + 4, h - 1)]
    m = mask_of(img.size, lambda d: d.polygon(tip + right + flare[::-1] + left[::-1], fill=255))

    def char_tex(x, y):
        return (-0.2 if (x + y // 6) % 4 == 0 else 0.0) + (-0.1 if (x * 3 + y) % 7 == 0 else 0.0) + bayer(x, y) * 0.05
    shade_shape(img, m, CHAR, radius=1.6, tex=char_tex, depth=0.15)
    for k in range(rnd.randint(3, 4)):                        # broken limbs
        y = rnd.randint(top + 6, h - 20)
        dx = rnd.choice((-1, 1))
        x0 = cx + lean * (1 - (y - top) / (h - top))
        spine = [(x0 + dx * 2, y), (x0 + dx * 8, y - 5 - k), (x0 + dx * (13 + rnd.randint(0, 4)), y - 8 - 2 * k)]
        shade_shape(img, mask_of(img.size, lambda d: d.polygon(ribbon(spine, [4, 3, 1.5], 5), fill=255)), CHAR, radius=1.0)
    for k in range(rnd.randint(3, 4)):                        # embers in the cracks: jagged, one hot pixel in each
        x = cx + lean * 0.4 + rnd.uniform(-3, 3)
        y = rnd.uniform(top + 10, h - 12)
        for step in range(rnd.randint(4, 7)):
            xi, yi = int(x), int(y)
            if 0 <= xi < w and 0 <= yi < h and img.getpixel((xi, yi))[3]:
                img.putpixel((xi, yi), GLOW_HI if step == 0 else (GLOW if step % 2 else GLOW_DIM))
            x += rnd.choice((-1, 0, 1))
            y += 1
    for _ in range(46):                                       # ash and cinders around the foot
        x = int(cx + rnd.gauss(0, 7))
        y = h - 1 - int(abs(rnd.gauss(0, 1.6)))
        if 0 <= x < w and 0 <= y < h and not img.getpixel((x, y))[3]:
            img.putpixel((x, y), ASH[rnd.randrange(3)])
    return selective_outline(img, strength=0.3, cool=0.3, keep=(GLOW, GLOW_HI, GLOW_DIM))


def generate():
    def write(img, name):
        path = os.path.join(ROOT, name + ".png")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        img.save(path)
    for i in range(3):
        write(spruce(20 + i), variant_name("things/pine", i))
        write(oak(10 + i), variant_name("things/oak", i))
        write(birch(30 + i), variant_name("things/birch", i))
    for i in range(2):
        write(snag(40 + i), variant_name("things/dead_tree", i))


if __name__ == "__main__":
    generate()
