"""Shared drawing toolkit for the Norse HD-2D art pass (docs/ART.md, "Style guide").

Everything here is about making small sprites read as *painted* rather than flat-filled:

* ``ramp`` builds a hue-shifted colour ramp (shadows lean cool, highlights lean warm), so a material
  has 4-6 deliberate steps instead of "colour, darker colour".
* ``shade_shape`` fills a mask with a ramp, lit from the top-left: a blurred copy of the mask is
  treated as a height field, its slope against the light picks the step, and an optional texture
  callback nudges it (scales, bark, wool).
* ``selective_outline`` outlines a silhouette in a dark, tinted version of the colour it borders,
  instead of one flat black, so shapes keep their warmth.

Pure Pillow, no numpy. Nothing in here knows about the game's sheet formats.
"""
import colorsys
import math
import os

from PIL import Image, ImageDraw, ImageFilter

LIGHT = (-0.55, -0.83)  # direction *toward* the light: up and to the left


# ---------------------------------------------------------------- colour

def clamp8(v):
    return max(0, min(255, int(round(v))))


def rgba(c, a=255):
    return (clamp8(c[0]), clamp8(c[1]), clamp8(c[2]), a)


def mix(a, b, t):
    return tuple(clamp8(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)


def scaled(c, k):
    return tuple(clamp8(v * k) for v in c[:3]) + (255,)


def ramp(base, n=5, main=None, dark=0.40, light=1.30, cool=22, warm=10):
    """n colours from darkest to lightest around ``base`` (which lands on index ``main``).

    Shadows lose value and drift toward blue-violet by up to ``cool`` degrees; highlights gain value
    and drift toward yellow by up to ``warm`` degrees and lose a little saturation.
    """
    main = n // 2 if main is None else main
    h, s, v = colorsys.rgb_to_hsv(*(c / 255 for c in base[:3]))
    out = []
    for i in range(n):
        if i < main:
            t = (main - i) / max(1, main)           # 1 = deepest shadow
            vv = v * (1 - (1 - dark) * t)
            hh = _toward(h, 262 / 360, cool / 360 * t)
            ss = min(1, s * (1 + 0.12 * t))
        elif i > main:
            t = (i - main) / max(1, n - 1 - main)   # 1 = brightest highlight
            vv = min(1, v * (1 + (light - 1) * t))
            hh = _toward(h, 50 / 360, warm / 360 * t)
            ss = s * (1 - 0.22 * t)
        else:
            vv, hh, ss = v, h, s
        r, g, b = colorsys.hsv_to_rgb(hh % 1.0, max(0, min(1, ss)), max(0, min(1, vv)))
        out.append((clamp8(r * 255), clamp8(g * 255), clamp8(b * 255), 255))
    return out


def _toward(h, target, amount):
    """Move hue h toward target along the short way round, by at most amount (all in 0..1 turns)."""
    d = (target - h + 0.5) % 1.0 - 0.5
    return (h + max(-amount, min(amount, d))) % 1.0


# ---------------------------------------------------------------- shapes and shading

def mask_of(size, draw_fn):
    """An 'L' mask the size of the canvas with draw_fn(ImageDraw) painting white shapes on it."""
    m = Image.new("L", size, 0)
    draw_fn(ImageDraw.Draw(m))
    return m


def union(*masks):
    out = masks[0].copy()
    for m in masks[1:]:
        out.paste(255, mask=m)
    return out


def catmull(points, steps=6, closed=False):
    """A smooth curve through the points (Catmull-Rom), as a denser point list."""
    pts = list(points)
    if len(pts) < 3:
        return pts
    if closed:
        pts = [pts[-1]] + pts + [pts[0], pts[1]]
    else:
        pts = [pts[0]] + pts + [pts[-1]]
    out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        for k in range(steps):
            t = k / steps
            t2, t3 = t * t, t * t * t
            out.append(tuple(
                0.5 * ((2 * p1[j]) + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2
                       + (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3) for j in range(2)))
    if not closed:
        out.append(pts[-2])
    return out


def ribbon(spine, widths, steps=6):
    """A polygon following a smooth centre line with a width at each control point (limbs, necks,
    tails, horns). widths is one value per spine point."""
    line = catmull(spine, steps)
    ws = catmull([(w, 0) for w in widths], steps)
    n = min(len(line), len(ws))
    left, right = [], []
    for i in range(n):
        a = line[max(0, i - 1)]
        b = line[min(n - 1, i + 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]
        d = math.hypot(dx, dy) or 1
        nx, ny = -dy / d, dx / d
        w = ws[i][0] / 2
        left.append((line[i][0] + nx * w, line[i][1] + ny * w))
        right.append((line[i][0] - nx * w, line[i][1] - ny * w))
    return left + right[::-1]


def spine_points(spine, steps=6):
    return catmull(spine, steps)


def shade_shape(img, mask, colors, radius=1.6, bias=0.0, slope=1.0, depth=0.35, tex=None, steps=None):
    """Paint ``colors`` (darkest..lightest) into ``img`` wherever ``mask`` is set.

    The step comes from the surface slope against the light (rounded forms catch it on the
    top-left and fall to shadow bottom-right), plus ``depth`` (the middle of a shape is a little
    lighter than its rim), ``bias`` (push the whole shape lighter or darker) and ``tex(x, y)``.
    Returns the list of painted pixel coordinates.
    """
    w, h = mask.size
    box = mask.getbbox()
    if box is None:
        return []
    blur = mask.filter(ImageFilter.GaussianBlur(radius))
    hb, mk, px = blur.load(), mask.load(), img.load()
    n = len(colors)
    lx, ly = LIGHT
    painted = []
    for y in range(box[1], box[3]):
        for x in range(box[0], box[2]):
            if mk[x, y] < 128:
                continue
            gx = hb[min(x + 1, w - 1), y] - hb[max(x - 1, 0), y]
            gy = hb[x, min(y + 1, h - 1)] - hb[x, max(y - 1, 0)]
            lam = (-gx * lx - gy * ly) / 255.0 * slope
            v = 0.5 + bias + lam * 0.55 + (hb[x, y] / 255.0 - 0.5) * depth
            if tex is not None:
                v += tex(x, y)
            i = int(v * n)
            px[x, y] = colors[0 if i < 0 else n - 1 if i >= n else i]
            painted.append((x, y))
    return painted


def shade_over(img, mask, colors, line, **kw):
    """``shade_shape`` for a part that overlaps what is already drawn: afterwards, ink ``line`` along
    the part's edge wherever it meets existing pixels (clumps of foliage, limbs over a body)."""
    before = img.split()[3].load()
    painted = shade_shape(img, mask, colors, **kw)
    w, h = mask.size
    mk, px = mask.load(), img.load()
    edge = []
    for x, y in painted:
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if 0 <= nx < w and 0 <= ny < h and mk[nx, ny] < 128 and before[nx, ny] > 0:
                edge.append((x, y))
                break
    for x, y in edge:
        px[x, y] = line
    return painted


def fill_mask(img, mask, color):
    img.paste(color, mask=mask.point(lambda v: 255 if v >= 128 else 0))


BAYER = ((0, 8, 2, 10), (12, 4, 14, 6), (3, 11, 1, 9), (15, 7, 13, 5))


def bayer(x, y):
    """-0.5..0.5 ordered-dither value for a pixel: texture that stays put when a shape moves a pixel."""
    return BAYER[y & 3][x & 3] / 16.0 - 0.47


# ---------------------------------------------------------------- outlines

COOL = (22, 14, 40)


def selective_outline(img, strength=0.34, cool=0.38, keep=()):
    """Outline every transparent pixel touching an opaque one (4-neighbours) in the average colour
    of what it touches, darkened and pulled toward violet. Colours in ``keep`` (glows) don't cast one."""
    w, h = img.size
    src = img.load()
    out = img.copy()
    dst = out.load()
    for y in range(h):
        for x in range(w):
            if src[x, y][3] != 0:
                continue
            tot = [0, 0, 0]
            n = 0
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and src[nx, ny][3] > 0 and src[nx, ny] not in keep:
                    c = src[nx, ny]
                    tot[0] += c[0]; tot[1] += c[1]; tot[2] += c[2]
                    n += 1
            if n:
                avg = (tot[0] / n, tot[1] / n, tot[2] / n)
                dark = tuple(c * strength for c in avg)
                dst[x, y] = tuple(clamp8(dark[i] * (1 - cool) + COOL[i] * cool) for i in range(3)) + (255,)
    return out


def relight(img, lit=1.2, shade=0.78, radius=1.4, cool=(0.9, 0.93, 1.04)):
    """Give a flat-filled sprite volume from its own silhouette: edges facing the light (top-left) step
    lighter, edges facing away step darker and cooler, and the whole form falls off a little from top
    to bottom. For old placeholder sprites that were drawn as flat colour."""
    img = img.convert("RGBA")
    a = img.split()[3]
    box = a.getbbox()
    if box is None:
        return img
    blur = a.filter(ImageFilter.GaussianBlur(radius))
    hb, px = blur.load(), img.load()
    w, h = img.size
    lx, ly = LIGHT
    for y in range(box[1], box[3]):
        t = (y - box[1]) / max(1, box[3] - box[1] - 1)
        for x in range(box[0], box[2]):
            r, g, b, al = px[x, y]
            if al == 0:
                continue
            gx = hb[min(x + 1, w - 1), y] - hb[max(x - 1, 0), y]
            gy = hb[x, min(y + 1, h - 1)] - hb[x, max(y - 1, 0)]
            lam = (-gx * lx - gy * ly) / 255.0
            k = 1.06 - 0.14 * t
            if lam > 0.12:
                k *= lit
            elif lam < -0.12:
                r, g, b = r * cool[0], g * cool[1], b * cool[2]
                k *= shade
            px[x, y] = (clamp8(r * k), clamp8(g * k), clamp8(b * k), al)
    return img


# ---------------------------------------------------------------- the world's colour grade

def is_glow(r, g, b):
    """Light-emitting colours: a near-full red channel with a saturated fire hue (orange to amber, not
    the yellow of buttercups or gold), or a near-full blue rune/moonlight glow."""
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    if r >= 232 and s >= 0.55 and 8 / 360 <= h <= 44 / 360:
        return True
    return b >= 240 and s >= 0.35 and 195 / 360 <= h <= 235 / 360


def nordic_finish(img, sat=0.8, cool=(0.94, 0.99, 1.08), shadow=(26, 32, 74), shadow_mix=0.2, light=(255, 238, 208), light_mix=0.12):
    """The last step for old placeholder world art (ground, buildings, items) so it sits in the Nordic
    palette: a little less saturation, a cool bias in the mid-tones, shadows split toward blue-violet
    and highlights toward warm cream. Deterministic per pixel, so a tile's variants keep their average
    colour. Not for greyscale tint layers, and ui/fx art stays neutral. Glow colours (``is_glow``: fire,
    embers, runes) pass through untouched so bloom still catches them (docs/ART.md, "Glow colours")."""
    img = img.convert("RGBA")
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0 or is_glow(r, g, b):
                continue
            lum = 0.299 * r + 0.587 * g + 0.114 * b
            r, g, b = (lum + (c - lum) * sat for c in (r, g, b))
            r, g, b = r * cool[0], g * cool[1], b * cool[2]
            lum = lum / 255.0
            ts = max(0.0, 0.5 - lum) / 0.5
            th = max(0.0, lum - 0.62) / 0.38
            r, g, b = ((c * (1 - shadow_mix * ts - light_mix * th) + shadow[i] * shadow_mix * ts + light[i] * light_mix * th)
                       for i, c in enumerate((r, g, b)))
            px[x, y] = (clamp8(r), clamp8(g), clamp8(b), a)
    return img


# ---------------------------------------------------------------- preview

def contact_sheet(images, path, scale=4, columns=8, bg=(52, 60, 70, 255), pad=4):
    """Lay images out on a dark slab, scaled up with nearest-neighbour, for eyeballing."""
    if not images:
        return
    cw = max(i.size[0] for i in images)
    ch = max(i.size[1] for i in images)
    rows = (len(images) + columns - 1) // columns
    sheet = Image.new("RGBA", (columns * (cw * scale + pad) + pad, rows * (ch * scale + pad) + pad), bg)
    for k, im in enumerate(images):
        big = im.resize((im.size[0] * scale, im.size[1] * scale), Image.NEAREST)
        x = pad + (k % columns) * (cw * scale + pad)
        y = pad + (k // columns) * (ch * scale + pad)
        sheet.alpha_composite(big, (x, y))
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    sheet.save(path)
