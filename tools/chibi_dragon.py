#!/usr/bin/env python3
"""The dragons, redrawn in the people's chibi style (tools/chibi_people.py): a big-eyed, round
hatchling and juvenile growing into a more majestic, still kind-faced adult. Side-on, at four
times the detail ("density": 4), in the layered-look contract the game already reads
(Rendering/DragonLooks, docs/ART.md "Dragons: layered looks"):

    python3 tools/chibi_dragon.py              # every stage into data/textures
    python3 tools/chibi_dragon.py --preview    # ...and .cache/dragons.png

  creatures/dragon_<stage>            the plain composite, in the default colours (the def's sprite)
  creatures/dragon_<stage>_base       outlines, eye whites and lights, mouth, claws, the orange of flames
  creatures/dragon_<stage>_<layer>    greyscale scales (main 200), belly (230), wings (230), horns (255),
                                      eyes (255), tinted with the look; flame (the hot core) and veins
                                      (markings) tinted with the element

Every frame is one pose of a rig (``Rig``: body, neck, head, legs, wings, tail for a stage's
proportions). The figure is painted back to front into one picture while a map records which
layer owns each pixel, then split by that map, so the layers never overlap and the game's
drawing order doesn't matter. Same frames, tags and points (mouth, saddle) as before.
"""
import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

sys.path.insert(0, os.path.dirname(__file__))
from norse_art import ribbon, catmull, shade_shape, bayer, contact_sheet  # noqa: E402
from placeholder_sheets import write_sheet  # noqa: E402

K = 4
LAYERS = ("base", "scales", "belly", "wings", "horns", "eyes", "flame", "veins")
ID = {name: i for i, name in enumerate(LAYERS)}

GREYS = {
    "scales": (64, 100, 146, 200, 232, 255),
    "belly": (150, 190, 230, 255),
    "wings": (96, 140, 186, 230, 255),
    "horns": (120, 168, 214, 255),
    "eyes": (150, 205, 255),
}
DEFAULT = {"scales": (0x9E, 0x40, 0x2E), "belly": (0xED, 0xD9, 0xB1), "wings": (0x66, 0x26, 0x21), "horns": (0xE4, 0xD8, 0xBE),
           "eyes": (0xFF, 0xD6, 0x50), "flame": (0xFF, 0x4A, 0x24), "veins": (0xFF, 0x4A, 0x24)}

INK = (34, 18, 26, 255)
WHITE = (252, 250, 244, 255)
PUPIL = (30, 16, 28, 255)
MOUTH = (96, 40, 44, 255)
CHEEK = (240, 150, 140, 255)
CLAW = (238, 232, 216, 255)
GLOW = (255, 140, 40, 255)
GLOW_HI = (255, 224, 110, 255)
GLOW_DIM = (190, 82, 34, 255)


def g(v):
    return (v, v, v, 255)


def ramp(layer):
    return [g(v) for v in GREYS[layer]]


# ---------------------------------------------------------------- a canvas that records layer ownership

class Canvas:
    """A frame in world pixels (w, h) drawn K times finer; ``img`` the picture, ``ids`` who owns each pixel."""

    def __init__(self, w, h, k=None):
        """``k``: sheet pixels per world pixel (K, or more to draw a figure bigger than its design)."""
        self.k = k or K
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
    def paint(self, m, colors, layer, radius=1.6, line=True, outside=None, line_color=INK, **kw):
        """Shade a part in its layer's greys (or plain colours), owned by the layer, outlined in ink
        (only where it's ``outside`` a mask, for a part fused onto another: no seam lines)."""
        if m.getbbox() is None:
            return
        tmp = Image.new("RGBA", (self.W, self.H), (0, 0, 0, 0))
        shade_shape(tmp, m, colors, radius=radius * self.k, **kw)
        self.put(tmp, m, layer)
        if line:
            self.ink(m, outside=outside, color=line_color)

    def fill(self, m, color, layer, only=None):
        if only is not None:
            m = ImageChops.multiply(m, only)
        self.img.paste(color, mask=m)
        self.ids.paste(ID[layer], mask=m)

    def put(self, tmp, m, layer):
        self.img.paste(tmp, mask=m)
        self.ids.paste(ID[layer], mask=m)

    def ink(self, m, width=2, outside=None, color=INK):
        m = m.point(lambda v: 255 if v >= 128 else 0)
        rim = ImageChops.subtract(m, m.filter(ImageFilter.MinFilter(2 * width + 1)))
        if outside is not None:
            rim = minus(rim, outside.filter(ImageFilter.MinFilter(3)))
        self.fill(rim, color, "base")

    def curve(self, pts, color, layer, width=2, only=None, design=True):
        pts = [self.P(*p) for p in pts] if design else pts
        line_pts = catmull(pts, 6) if len(pts) > 2 else pts
        m = self.mask(lambda d: d.line(line_pts, fill=255, width=width, joint="curve"))
        self.fill(m, color, layer, only)

    def split(self):
        """Per layer: its own pixels; the base keeps everything that's not tinted."""
        out = {}
        for name in LAYERS:
            sel = self.ids.point(lambda v, i=ID[name]: 255 if v == i else 0)
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


# ---------------------------------------------------------------- stages

# World-pixel proportions per stage (side-on, facing right). canvas, pivot: as the old sheets.
# build: "sit" (the baby, upright on its haunches) or "quad" (standing on four legs). Ellipses are
# (cx, cy, rx, ry); the neck runs from the chest's top through a bend; legs are (hind, front)
# thicknesses; wing is the span; ears the ear fins' length; horn the horns' length.
STAGES = {
    "hatchling": dict(
        build="sit", canvas=(52, 39), pivot=(24, 33), ground=33,
        body=(22.4, 26, 6.0, 6.6), head=(24.4, 14.4), head_r=8.8, snout=(2.6, 2.2), eye=(2.6, 3.0),
        legs=(4.2, 2.2), wing=10, wing_fold=0.9, wing_rest=122, ears=4.4, horn=0, tuft=True,
        tail=9, spikes=0, spots=False, crest=False, baby=True),
    "juvenile": dict(
        build="quad", canvas=(68, 57), pivot=(32, 51), ground=51,
        chest=(37.2, 39.4, 7.4, 6.8), hip=(24.2, 40.6, 6.8, 6.4), neck=((1.2, -3.6), (2.4, -7.2)), neck_w=7.6,
        head_r=9.2, head_off=(3.6, -5.4), snout=(3.6, 3.0), eye=(2.6, 2.9),
        legs=(4.6, 3.6), wing=18, wing_fold=0.8, wing_rest=118, ears=5, horn=4.6, tuft=False,
        tail=14, spikes=4, spots=True, crest=False, baby=False),
    "adult": dict(
        build="quad", canvas=(128, 96), pivot=(60, 92), ground=92,
        chest=(78, 69, 14.6, 13.6), hip=(50, 71, 13.6, 12.6), neck=((3.4, -7), (10, -15)), neck_w=13.4,
        head_r=13.2, head_off=(8, -3.6), snout=(10, 6.2), eye=(2.9, 3.1),
        legs=(11.4, 9.6), wing=38, wing_fold=0.34, wing_rest=166, ears=7.5, horn=15, tuft=False,
        tail=34, spikes=6, spots=True, crest=True, baby=False, adult=True),
}


class Pose:
    def __init__(self, kind="stand", legs=0.0, bob=0.0, wing=None, wave=0.0, jaw=0.0, closed=False, happy=False, glow=0.0, pulse=0.0):
        self.kind, self.legs, self.bob, self.wing, self.wave = kind, legs, bob, wing, wave
        self.jaw, self.closed, self.happy, self.glow, self.pulse = jaw, closed, happy, glow, pulse


class Rig:
    """Where the parts of one stage are in one pose (world pixels)."""

    def __init__(self, stage, pose):
        c = STAGES[stage]
        self.c, self.pose, self.stage = c, pose, stage
        fly, sleep = pose.kind == "fly", pose.kind == "sleep"
        self.ground = c["ground"]
        self.hr = c["head_r"]
        dy = pose.bob
        if c["build"] == "sit":
            bx, by, rx, ry = c["body"]
            if fly:
                dy -= 3.5
            if sleep:                                                  # curled up: lower, rounder, head down on its paws
                by += ry * 0.4
                ry *= 0.78
                rx *= 1.12
            self.body = (bx, by + dy, rx, ry)
            self.chest = (bx + rx * 0.3, by + dy - ry * 0.2, rx * 0.8, ry * 0.85)
            self.hip = (bx - rx * 0.2, by + dy + ry * 0.2, rx * 0.85, ry * 0.8)
            hx, hy = c["head"]
            if pose.kind == "windup":
                hx, hy = hx - 1.2, hy - 0.6
            if pose.kind == "breathe":
                hx, hy = hx + 1.6, hy + 0.6
            if sleep:
                hx, hy = bx + rx * 0.9, self.ground - self.hr * 0.82
            self.head = (hx, hy + (dy if not sleep else 0))
            self.neck_root = self.neck_mid = self.neck_top = (bx + rx * 0.2, by + dy - ry * 0.6)
            return
        if fly:
            dy -= c["legs"][0] * 0.55 + 2
        cx, cy, crx, cry = c["chest"]
        hx, hy, hrx, hry = c["hip"]
        if sleep:                                                      # lying down: belly on the ground
            drop = self.ground - (max(cy + cry, hy + hry)) - 0.4
            cy, hy = cy + drop, hy + drop
            dy = 0
        if fly:
            cy, hy = cy + 0.8, hy - 1.0
        self.chest = (cx, cy + dy, crx, cry)
        self.hip = (hx, hy + dy, hrx, hry)
        self.body = ((cx + hx) / 2, (cy + hy) / 2 + dy, (cx + crx - (hx - hrx)) / 2, max(cry, hry))
        self.neck_root = (cx + crx * 0.42, cy + dy - cry * 0.5)
        b1, b2 = c["neck"]
        if pose.kind == "windup":
            b2 = (b2[0] * 0.5, b2[1] * 1.08)
        if pose.kind == "breathe":                                    # head thrust forward (the adult's big head less far)
            reach = 1.15 if c.get("adult") else 1.7
            b1, b2 = (b1[0] + 1.5, b1[1] * 0.7), (b2[0] * reach, b2[1] * 0.6)
        if fly:
            b1, b2 = (b1[0] + 1, b1[1] * 0.7), (b2[0] * 1.5, b2[1] * 0.6)
        self.neck_mid = add(self.neck_root, b1)
        self.neck_top = add(self.neck_root, b2)
        off = c["head_off"]
        if pose.kind == "windup":
            off = (off[0] - self.hr * 0.3, off[1] - self.hr * 0.15)
        self.head = add(self.neck_top, off)
        if sleep:                                                     # chin down on the ground in front
            self.head = (cx + crx + self.hr * 0.6, self.ground - self.hr * 0.86)
            self.neck_mid = (cx + crx * 0.75, cy - cry * 0.25)
            self.neck_top = (self.head[0] - self.hr * 0.7, self.head[1] + self.hr * 0.1)


def wing_shape(root, a, span, open_):
    """A bat wing laid out upright (forearm up, fingers fanning back toward the tail) and turned to
    angle ``a`` (degrees: 90 up, past 90 sweeping back, past 180 down). ``open_`` (0 folded .. 1
    spread) fans the fingers. Returns elbow, wrist, finger tips, the trailing point on the body."""
    th = math.radians(a - 90)

    def place(x, y):
        return root[0] + x * math.cos(th) + y * math.sin(th), root[1] - x * math.sin(th) + y * math.cos(th)

    def dir_(deg):                                                    # the up vector turned back by deg
        r = math.radians(deg)
        return -math.sin(r), -math.cos(r)
    elbow = place(*(v * span * 0.42 for v in dir_(-12)))
    ex, ey = elbow[0] - root[0], elbow[1] - root[1]
    # work in the upright frame: un-turn the elbow to add the forearm
    ux, uy = dir_(-12)
    e_local = (ux * span * 0.42, uy * span * 0.42)
    fx, fy = dir_(14)
    w_local = (e_local[0] + fx * span * 0.5, e_local[1] + fy * span * 0.5)
    spread = [8 + (38 - 8) * open_, 14 + (84 - 14) * open_, 20 + (128 - 20) * open_]
    lengths = [0.62 + 0.08 * open_, 0.58 + 0.1 * open_, 0.5 + 0.1 * open_]
    tips = []
    for deg, ln in zip(spread, lengths):
        dx, dy = dir_(14 + deg)
        tips.append(place(w_local[0] + dx * span * ln, w_local[1] + dy * span * ln))
    trail = place(-span * (0.22 + 0.42 * open_), span * (0.1 + 0.12 * open_))
    return place(*e_local), place(*w_local), tips, trail


def concave(p, q, toward, k=0.22, n=6):
    """Points on a gentle inward curve from p to q, pulled ``k`` of the way toward ``toward``."""
    mid = lerp(p, q, 0.5)
    ctrl = lerp(mid, toward, k)
    out = []
    for i in range(1, n):
        t = i / n
        a_ = lerp(p, ctrl, t)
        b_ = lerp(ctrl, q, t)
        out.append(lerp(a_, b_, t))
    return out


# ---------------------------------------------------------------- drawing one frame

def draw(stage, pose, size=None):
    c = STAGES[stage]
    w, h = c["canvas"]
    cv = Canvas(w, h)
    R = Rig(stage, pose)
    baby, adult, sit = c["baby"], c.get("adult", False), c["build"] == "sit"
    fly, sleep = pose.kind == "fly", pose.kind == "sleep"
    S = ramp("scales")
    cx, cy, crx, cry = R.chest
    hx, hy, hrx, hry = R.hip
    bx, by, brx, bry = R.body

    folded = pose.wing is None and not fly
    wa = pose.wing if pose.wing is not None else (c["wing_rest"] if not sleep else (150 if not adult else 172))
    if sit:
        shoulder = (bx - brx * 0.15, by - bry * 0.62)
    else:
        shoulder = (cx - crx * 0.45, cy - cry * 0.8)

    def folded_wing(near):
        """Folded flat to the back: the elbow raised in a peak over the shoulders, the wrist reaching
        back along the spine, the membrane hanging in pleats over the flank."""
        span = c["wing"] * (0.92 if not near else 1.0)
        root = add(shoulder, (-1.4 if not near else 0, -1.0 if not near else 0))
        lift = -span * (0.24 if not near else 0.2)
        elbow = add(root, (-span * 0.1, lift))
        wrist = add(root, (-span * 0.8, lift * 0.3))
        tips = [add(wrist, (span * 0.02, span * 0.3)), add(wrist, (span * 0.26, span * 0.36)), add(wrist, (span * 0.52, span * 0.36))]
        trail = add(root, (span * 0.04, span * 0.28))
        edge_pts = tips + [trail]
        outline = [root, elbow, wrist]
        for p, q in zip(edge_pts, edge_pts[1:]):
            outline += [p] + concave(p, q, wrist, 0.18)
        outline += [trail]
        membrane = cv.poly(outline)
        cv.paint(membrane, ramp("wings"), "wings", radius=1.2, bias=0.0 if near else -0.3)
        for tip_ in tips:                                            # the pleats: a fold line and its shadow
            cv.curve([lerp(wrist, elbow, 0.15), lerp(wrist, tip_, 0.5), tip_], g(120), "wings", 3, only=membrane)
            cv.curve([add(lerp(wrist, tip_, 0.3), (1.0, 0)), add(lerp(wrist, tip_, 0.85), (1.0, 0))], g(255), "wings", 2, only=membrane)
        bone = max(0.9, c["wing"] * 0.045)
        arm = cv.strip([root, elbow, wrist, add(wrist, (-span * 0.08, span * 0.04))], [bone * 2.6, bone * 2.2, bone * 1.6, 0.4], steps=6)
        cv.paint(arm, S, "scales", radius=0.6, bias=0.05 if near else -0.3)
        thumb = cv.poly([add(elbow, (-bone * 0.7, 0.2)), add(elbow, (bone * 0.7, 0.2)), add(elbow, (bone * 0.1, -bone * 2.0))])
        cv.paint(thumb, ramp("horns"), "horns", radius=0.3)

    def wing(near):
        if folded and adult:
            folded_wing(near)
            return
        span = c["wing"] * (0.92 if not near else 1.0)
        open_ = 1.0 if not folded else (0.55 if baby else (0.3 if not adult else 0.08))
        a = wa + ((10 if not adult else -6) if not near else 0) + pose.wave * 2
        root = add(shoulder, (-1.2 if not near else 0, -0.6 if not near else 0))
        elbow, wrist, tips, trail = wing_shape(root, a, span, open_)
        # the membrane: along the arm, then from tip to tip in gentle inward curves, back to the body
        edge_pts = [wrist] + tips + [trail]
        outline = [root, elbow, wrist]
        for p, q in zip(edge_pts[1:], edge_pts[2:]):
            outline += [p] + concave(p, q, wrist, 0.2 if open_ > 0.5 else 0.08)
        outline += [trail]
        membrane = cv.poly(outline)
        cv.paint(membrane, ramp("wings"), "wings", radius=1.4, bias=0.05 if near else -0.25)
        lead = both(cv.poly([root, elbow, wrist, tips[0], lerp(tips[0], root, 0.55)]), membrane)   # the lighter panel by the arm
        cv.paint(lead, ramp("wings"), "wings", radius=1.0, bias=0.22 if near else -0.05, line=False)
        bone = max(0.9, c["wing"] * 0.045)
        for tip_ in tips:                                              # finger bones, tapering
            m = cv.strip([wrist, lerp(wrist, tip_, 0.5), tip_], [bone * 1.1, bone * 0.8, 0.3], steps=4)
            cv.paint(m, S, "scales", radius=0.4, bias=0.05 if near else -0.25, line=False)
        arm = cv.strip([root, elbow, wrist], [bone * 2.6, bone * 2.0, bone * 1.4], steps=5)
        cv.paint(arm, S, "scales", radius=0.6, bias=0.05 if near else -0.25)
        thumb = cv.poly([add(wrist, (-bone * 0.6, 0)), add(wrist, (bone * 0.6, 0)), add(wrist, (bone * 0.2, -bone * 1.8))])
        cv.paint(thumb, ramp("horns"), "horns", radius=0.3)

    def claws(at, thick, tucked=False):
        if tucked:
            return
        for k in range(3):
            x = at[0] + thick * 0.15 + (k - 1) * thick * 0.3 + thick * 0.2
            y = at[1] + 0.05
            sz = max(0.4, thick * 0.11)
            cv.fill(cv.poly([(x - sz, y), (x + sz, y), (x + sz * 1.4, y + sz * 1.2)]), CLAW, "base")

    def quad_leg(front, near):
        thick = c["legs"][1 if front else 0]
        stride = pose.legs * (1 if front == near else -1)
        dark = 0.0 if near else -0.3
        if front:
            top = (cx + crx * (0.3 if near else 0.05), cy + cry * 0.2)
        else:
            top = (hx + (0.2 if near else -1.6), hy + hry * 0.1)
        if sleep:
            paw = (top[0] + thick * (1.2 if front else 0.4), R.ground - thick * 0.32)
            m = union(cv.ellipse(top[0], top[1], thick * 0.9, thick * 0.85), cv.ellipse(paw[0], paw[1], thick * 0.75, thick * 0.4),
                      cv.strip([top, paw], [thick * 1.1, thick * 0.8], steps=3))
            cv.paint(m, S, "scales", radius=0.9, bias=dark)
            return
        if fly:
            paw = add(top, (-thick * (0.9 if not front else 0.2), thick * 1.6))
            joint = add(lerp(top, paw, 0.5), (thick * 0.4, 0))
        else:
            paw = (top[0] + stride * 0.5 + (thick * 0.15 if front else thick * 0.35), R.ground - 0.5 - max(0, -stride) * 0.25)
            joint = lerp(top, paw, 0.5)
            joint = (joint[0] + (-thick * 0.25 if front else thick * 0.35), joint[1])
        upper = cv.ellipse(top[0], top[1], thick * (0.95 if not front else 0.8), thick * (1.05 if not front else 0.9))
        limb = cv.strip([top, joint, paw], [thick * 1.0, thick * 0.8, thick * 0.74], steps=4)
        foot = cv.ellipse(paw[0] + thick * 0.22, paw[1] - thick * 0.24, thick * 0.66, thick * 0.38)
        legm = union(upper, limb, foot)
        cv.paint(legm, S, "scales", radius=1.0, bias=dark, outside=BODY["m"] if near else None)
        if near and BODY["m"] is not None:                             # a soft crease, not a line, where it meets the body
            r_ = thick * (0.95 if not front else 0.8)
            crease = cv.mask(lambda d: d.arc([(top[0] - r_) * K, (top[1] - r_) * K, (top[0] + r_) * K, (top[1] + r_) * K],
                                             200 if not front else 230, 330 if not front else 310, fill=255, width=2))
            cv.fill(crease, g(146), "scales", only=legm)
        claws(paw, thick, fly)

    def sit_legs(near):
        thick = c["legs"][0]
        dark = 0.0 if near else -0.3
        hop = pose.legs * 0.25 * (1 if near else -1)
        thigh_c = (bx - brx * 0.25 + (0 if near else -1.2), by + bry * 0.45)
        if fly:
            foot = (thigh_c[0] + 1.2, thigh_c[1] + thick * 0.9)
        elif sleep:
            foot = (thigh_c[0] + thick * 0.9, R.ground - 0.9)
        else:
            foot = (thigh_c[0] + thick * 0.7 + hop, R.ground - 0.9 - max(0, hop))
        thigh = cv.ellipse(thigh_c[0], thigh_c[1], thick * 0.95, thick * 0.85)
        f = cv.ellipse(foot[0], foot[1], thick * 0.62, thick * 0.32)
        cv.paint(union(thigh, f, cv.strip([thigh_c, foot], [thick * 0.9, thick * 0.6], steps=3)), S, "scales", radius=0.9, bias=dark)
        claws((foot[0] - thick * 0.1, foot[1] + thick * 0.18), thick * 0.9, fly)

    def sit_arms(near):
        thick = c["legs"][1]
        dark = 0.0 if near else -0.3
        sh = (bx + brx * (0.45 if near else 0.3), by - bry * 0.15)
        if sleep:
            paw = (R.head[0] - R.hr * 0.4 + (0 if near else -1), R.ground - thick * 0.5)
        elif fly:
            paw = (sh[0] + thick * 0.8, sh[1] + thick * 1.6)
        else:
            paw = (sh[0] + thick * 0.7, by + bry * 0.42)
        m = union(cv.strip([sh, lerp(sh, paw, 0.5), paw], [thick * 1.0, thick * 0.9, thick * 0.85], steps=3), cv.ellipse(paw[0] + 0.3, paw[1], thick * 0.7, thick * 0.55))
        cv.paint(m, S, "scales", radius=0.6, bias=dark)
        for k in range(2):
            x = paw[0] + 0.3 + (k - 0.5) * thick * 0.5
            cv.fill(cv.ellipse(x, paw[1] + thick * 0.45, 0.32, 0.26), CLAW, "base")

    def tail():
        L = c["tail"]
        wave = pose.wave * 0.6
        if sit:
            root = (bx - brx * 0.7, by + bry * 0.55)
            if sleep:
                pts = [root, (bx - brx * 1.1, R.ground - 1.2), (bx - brx * 0.2, R.ground - 0.6), (bx + brx * 0.6, R.ground - 0.8)]
            else:
                pts = [root, add(root, (-L * 0.45, L * 0.22)), add(root, (-L * 0.85, L * 0.05 + wave)), add(root, (-L * 0.95, -L * 0.35 + wave))]
            widths = [bry * 0.55, bry * 0.42, bry * 0.28, 0.5]
        else:
            root = (hx - hrx * 0.78, hy - hry * 0.05)
            if sleep:
                pts = [root, (hx - hrx * 1.3, R.ground - 2), (hx - hrx * 0.4, R.ground - 0.8), (cx + crx * 0.4, R.ground - 0.9)]
            else:
                pts = [root, add(root, (-L * 0.32, L * 0.16 + wave)), add(root, (-L * 0.7, L * 0.06 - wave)), add(root, (-L * 0.9, -L * 0.3 + wave * 1.5))]
            widths = [hry * 0.85, hry * 0.56, hry * 0.32, 0.6]
        m = cv.strip(pts, widths, steps=7)
        cv.paint(m, S, "scales", radius=1.2, bias=-0.05)
        under = minus(m, cv.strip([add(p, (0, -widths[i] * 0.25)) for i, p in enumerate(pts)], [w_ * 0.8 for w_ in widths], steps=7))
        cv.paint(both(under, m), ramp("belly"), "belly", radius=0.6, line=False)
        line = catmull([cv.P(*p) for p in pts], 8)
        if c["spikes"] and not sleep:
            for i in range(1, c["spikes"] + 1):
                t = i / (c["spikes"] + 2)
                j = int(t * (len(line) - 1))
                (x0, y0), (x1, y1) = line[j], line[min(j + 1, len(line) - 1)]
                dx, dy = x1 - x0, y1 - y0
                dl = math.hypot(dx, dy) or 1
                nx, ny = -dy / dl, dx / dl                             # the tail's upper side (it runs toward the left)
                if ny > 0:
                    nx, ny = -nx, -ny
                wv = widths[0] * (1 - t) + widths[-1] * t
                base_ = (x0 / K + nx * wv * 0.42, y0 / K + ny * wv * 0.42)
                sz = max(0.9, wv * 0.36)
                spike = cv.poly([add(base_, (dx / dl, dy / dl), sz * 0.6), add(base_, (-dx / dl, -dy / dl), sz * 0.6), add(base_, (nx, ny), sz * 1.3)])
                cv.paint(spike, ramp("horns"), "horns", radius=0.3, outside=m)
        # the spade fin at the tip
        (x0, y0), (x1, y1) = line[-4], line[-1]
        d = ((x1 - x0) / K, (y1 - y0) / K)
        dl = math.hypot(*d) or 1
        d = (d[0] / dl, d[1] / dl)
        n = (-d[1], d[0])
        tip = pts[-1]
        sz = 1.4 if baby else (2.4 if not adult else 4.0)
        # a spade: two rounded lobes swept back from the tail's end and a point beyond it
        lobes = union(cv.ellipse(*add(add(tip, n, sz * 0.42), d, -sz * 0.05), sz * 0.46, sz * 0.46),
                      cv.ellipse(*add(add(tip, n, -sz * 0.42), d, -sz * 0.05), sz * 0.46, sz * 0.46))
        spade = union(lobes, cv.poly([add(add(tip, n, sz * 0.82), d, 0.0), add(tip, d, sz * 1.8), add(add(tip, n, -sz * 0.82), d, 0.0)]))
        cv.paint(spade, ramp("wings"), "wings", radius=0.5, bias=0.1, outside=m)
        cv.curve([add(tip, d, -sz * 0.2), add(tip, d, sz * 1.0)], g(150), "wings", 2, only=spade)
        # the element's mark: glowing rings round the tail near its flame
        for t_ in (0.62, 0.74):
            j = int(t_ * (len(line) - 1))
            (ax, ay), (bx_, by_) = line[j], line[min(j + 1, len(line) - 1)]
            dd = ((bx_ - ax) / K, (by_ - ay) / K)
            dln = math.hypot(*dd) or 1
            dd = (dd[0] / dln, dd[1] / dln)
            centre = (ax / K, ay / K)
            wv = widths[1] * (1 - t_) + widths[-1] * t_
            band = cv.poly([add(add(centre, (-dd[1], dd[0]), wv), dd, -0.35), add(add(centre, (-dd[1], dd[0]), wv), dd, 0.35),
                            add(add(centre, (dd[1], -dd[0]), wv), dd, 0.35), add(add(centre, (dd[1], -dd[0]), wv), dd, -0.35)])
            cv.fill(band, g(255), "veins", only=m)
        return add(tip, d, sz * 1.3)

    def flame(at):
        x, y = at
        r = 1.3 if baby else (2.0 if not adult else 3.4)
        hot = max(pose.glow, pose.pulse)
        lean = pose.wave * 0.4
        for rr, tt, color, layer in ((r, r * 2.6, GLOW_DIM, "base"), (r * 0.74, r * 2.0, GLOW, "base"), (r * 0.48, r * 1.4, g(255), "flame")):
            m = union(cv.ellipse(x, y - rr * 0.2, rr, rr), cv.poly([(x - rr * 0.92, y - rr * 0.2), (x + rr * 0.92, y - rr * 0.2), (x + lean, y - tt * (1 + hot * 0.2))]))
            cv.fill(m, color, layer)

    BODY = {"m": None}                                                # the torso, once drawn: fused parts don't outline over it

    # --- back to front
    if not sleep:
        wing(near=False)
    if sit:
        sit_legs(near=False)
        sit_arms(near=False)
    else:
        quad_leg(front=False, near=False)
        quad_leg(front=True, near=False)
    tip = tail()
    if sit:
        torso = cv.ellipse(bx, by, brx, bry)
    else:
        torso = union(cv.ellipse(cx, cy, crx, cry), cv.ellipse(hx, hy, hrx, hry),
                      cv.poly([(hx, hy - hry * 0.98), (cx, cy - cry * 0.98), (cx, cy + cry * 0.9), (hx, hy + hry * 0.9)]))
    cv.paint(torso, S, "scales", radius=2.2, bias=0.05, tex=lambda x, y: bayer(x, y) * 0.03)
    BODY["m"] = torso
    # the pale belly: a patch down the front (sitting), along the underside (standing)
    if sit:
        belly = both(cv.ellipse(bx + brx * 0.38, by + bry * 0.12, brx * 0.58, bry * 0.82), torso)
    else:
        belly = both(union(cv.ellipse(cx + crx * 0.35, cy + cry * 0.2, crx * 0.62, cry * 0.78),
                           cv.ellipse((cx + hx) / 2, (cy + hy) / 2 + cry * 0.62, (cx - hx) * 0.55, cry * 0.42)), torso)
    cv.paint(belly, ramp("belly"), "belly", radius=1.4, line=False, bias=0.05)
    if c["spots"]:                                                   # pale spots scattered down the back and haunch
        for k, (u, v) in enumerate(((0.15, -0.55), (0.32, -0.7), (-0.2, -0.45), (-0.45, -0.6), (-0.62, -0.2), (-0.35, -0.15), (0.05, -0.3))):
            x = bx + u * brx
            y = by + v * bry
            r = (0.62 if not adult else 1.4) * (1.1 - 0.12 * (k % 3))
            cv.fill(both(cv.ellipse(x, y, r, r * 0.85), torso), g(255), "belly")
    # spikes down the spine (young: a few small ones)
    if c["spikes"] and not sit:
        n = c["spikes"]
        for k in range(n):
            t = (k + 0.5) / n
            p0 = lerp((cx - crx * 0.2, cy - cry * 0.97), (hx - hrx * 0.5, hy - hry * 0.97), t)
            nrm = (-0.25, -1)
            nl = math.hypot(*nrm)
            nrm = (nrm[0] / nl, nrm[1] / nl)
            sz = (1.2 if not adult else 2.4) * (0.8 + 0.4 * math.sin(t * math.pi))
            spike = cv.poly([add(p0, rot(nrm, 90), sz * 0.55), add(p0, rot(nrm, -90), sz * 0.55), add(p0, nrm, sz * 1.4)])
            cv.paint(spike, ramp("horns"), "horns", radius=0.4, outside=torso)
    if sit:
        sit_legs(near=True)
    else:
        quad_leg(front=False, near=True)
    # the neck (standing builds): thick, with a pale throat
    if not sit:
        nw = c["neck_w"]
        nk = cv.strip([R.neck_root, R.neck_mid, R.neck_top], [nw * 1.25, nw * 1.05, nw * 0.92], steps=6)
        cv.paint(nk, S, "scales", radius=1.6, bias=0.05, outside=torso)
        front = lambda p, k_: add(p, (nw * k_, 0))
        nb = both(cv.strip([front(R.neck_root, 0.4), front(R.neck_mid, 0.36), front(R.neck_top, 0.3)], [nw * 0.42, nw * 0.36, nw * 0.3], steps=6), nk)
        cv.paint(nb, ramp("belly"), "belly", radius=0.8, line=False)
        if c["crest"]:                                               # a mane of soft tufts down the neck to the shoulders
            body_m = union(nk, torso)
            spine = [add(R.head, (-R.hr * 0.55, -R.hr * 0.55)), add(R.neck_top, (-nw * 0.42, -nw * 0.12)), add(R.neck_mid, (-nw * 0.5, -nw * 0.2)),
                     add(R.neck_root, (-nw * 0.62, -nw * 0.1)), (cx - crx * 0.35, cy - cry * 0.92)]
            path = catmull(spine, 6)
            n_tufts = 9
            for row in (0, 1):                                       # the far row first, offset and darker
                for k in range(n_tufts):
                    t = (k + (0.5 if row == 0 else 0)) / n_tufts
                    j = min(len(path) - 1, int(t * (len(path) - 1)))
                    root = (path[j][0] + (-1.0 if row == 0 else 0), path[j][1] + (-0.6 if row == 0 else 0))
                    sz = nw * (0.62 - 0.36 * t) * (0.85 if row == 0 else 1.0)
                    tuft_tip = add(root, (-sz * 1.0, -sz * 0.55))
                    bend = add(root, (-sz * 0.35, -sz * 0.65))
                    tuft = cv.strip([add(root, (sz * 0.2, sz * 0.25)), bend, tuft_tip], [sz * 0.75, sz * 0.55, 0.3], steps=6)
                    cv.paint(tuft, ramp("wings"), "wings", radius=0.6, bias=-0.25 if row == 0 else 0.1, outside=body_m)
                    if row == 1:
                        cv.curve([lerp(root, bend, 0.3), lerp(bend, tuft_tip, 0.6)], g(255), "wings", 2, only=tuft)
        torso_m = union(torso, nk)
    else:
        torso_m = torso
    if sit and not sleep:                                            # the baby's little wings stay behind its big head
        wing(near=True)
    head(cv, R, c, pose)
    if sit:
        sit_arms(near=True)
    else:
        quad_leg(front=True, near=True)
    if not sleep and not sit:
        wing(near=True)
    flame(tip)
    if pose.glow > 0:                                                # the chest glow while winding up a breath
        gx, gy = (add(R.neck_root, (c.get("neck_w", 5) * 0.3, c.get("neck_w", 5) * 0.2)) if not sit else (bx + brx * 0.4, by - bry * 0.3))
        r = (c.get("neck_w", 5)) * (0.38 + pose.glow * 0.25)
        cv.fill(cv.ellipse(gx, gy, r, r * 0.8), GLOW, "base", only=torso_m)
        cv.fill(cv.ellipse(gx, gy, r * 0.55, r * 0.45), g(255), "flame", only=torso_m)
    markings(cv, R, c, torso)
    return cv, R


def head(cv, R, c, pose):
    hx, hy = R.head
    hr = R.hr
    baby, adult = c["baby"], c.get("adult", False)
    S = ramp("scales")
    sx, sy = c["snout"]
    jaw = pose.jaw
    # behind the head: horns (swept back) and ear fins
    if c["horn"]:
        L = c["horn"]
        base_ = (hx - hr * 0.1, hy - hr * 0.78)
        spine = [base_, (base_[0] - L * 0.4, base_[1] - L * 0.42), (base_[0] - L * 0.95, base_[1] - L * 0.5), (base_[0] - L * 1.3, base_[1] - L * 0.3)]
        if not adult:                                                 # the young: short horns, curling up and back
            spine = [base_, (base_[0] - L * 0.2, base_[1] - L * 0.6), (base_[0] - L * 0.6, base_[1] - L * 0.95), (base_[0] - L * 1.0, base_[1] - L * 0.9)]
        for far in (True, False):
            sp = [add(p, (-1.0 if not adult else -2.0, 0.5)) for p in spine] if far else spine
            m = cv.strip(sp, [L * 0.3, L * 0.22, L * 0.13, 0.4], steps=6)
            cv.paint(m, ramp("horns"), "horns", radius=0.8, bias=-0.25 if far else 0.05)
            if not far:
                for t in (0.22, 0.42, 0.62):                          # rings
                    p = lerp(sp[0], sp[2], t)
                    cv.curve([add(p, (-0.7, -0.8)), add(p, (0.7, 0.8))], g(150), "horns", 2, only=m)
    E = c["ears"]

    def ear(far):
        """An ear fin standing up from the top of the head, leaning back, a membrane inside."""
        base_ = (hx - hr * (0.18 if not far else 0.42), hy - hr * (0.72 if not far else 0.62))
        tipp = (base_[0] - E * 0.55, base_[1] - E * 1.05)
        half = E * 0.36
        fin = cv.poly([add(base_, (-half, 0.4)), tipp, add(base_, (half, 0.4))])
        cv.paint(fin, S, "scales", radius=0.5, bias=-0.3 if far else 0.05)
        inner = cv.poly([add(base_, (-half * 0.45, 0)), lerp(tipp, base_, 0.22), add(base_, (half * 0.45, 0))])
        cv.paint(both(inner, fin), ramp("wings"), "wings", radius=0.3, line=False, bias=0.1)
    ear(far=True)
    # the skull and snout (the jaw drops for breathing)
    round_ = baby or not adult
    skull = cv.ellipse(hx, hy, hr, hr * (0.94 if round_ else 0.8))
    snout_c = (hx + hr * 0.78 + sx * 0.25, hy + hr * (0.3 if round_ else 0.2))
    snout = cv.ellipse(snout_c[0], snout_c[1], sx, sy)
    if adult:
        snout = union(snout, cv.poly([(hx + hr * 0.2, hy - hr * 0.6), (snout_c[0] + sx * 0.5, snout_c[1] - sy * 0.85), (snout_c[0], snout_c[1]), (hx, hy + hr * 0.3)]))
    headm = union(skull, snout)
    lower = None
    if jaw > 0:
        # the jaw swings down on a hinge under the cheek: the upper jaw keeps its shape above the
        # line from the hinge to the lip, the lower jaw turns down, the mouth dark between them
        hinge = (hx + hr * 0.28, snout_c[1] + sy * 0.42)
        lip = (snout_c[0] + sx * 1.0, snout_c[1] + sy * 0.2)
        ang = math.radians(jaw * (5 if baby else 6.5))
        v = (lip[0] - hinge[0], lip[1] - hinge[1])
        low_tip = add(hinge, (v[0] * math.cos(ang) - v[1] * math.sin(ang), v[0] * math.sin(ang) + v[1] * math.cos(ang)), 0.94)
        below = cv.poly([hinge, add(lip, (3, 0)), add(lip, (3, 12)), add(hinge, (0, 12))])
        headm = minus(headm, below)
        mouth = cv.poly([hinge, lip, low_tip])
        cv.fill(mouth, MOUTH, "base")
        tongue = both(cv.ellipse(*lerp(hinge, low_tip, 0.55), sx * 0.55, sy * 0.28), mouth)
        cv.fill(tongue, CHEEK, "base")
        lower = cv.strip([hinge, lerp(hinge, low_tip, 0.55), low_tip], [sy * 1.0, sy * 0.82, sy * 0.5], steps=4)
        if not baby:                                                   # fangs from the upper jaw
            for t_ in (0.55, 0.82):
                fx_, fy_ = lerp(hinge, lip, t_)
                cv.fill(cv.poly([(fx_ - 0.45, fy_), (fx_ + 0.45, fy_), (fx_ + 0.1, fy_ + (1.0 if adult else 0.7))]), CLAW, "base")
    cv.paint(headm, S, "scales", radius=2.0, bias=0.12, depth=0.25)
    ear(far=False)
    if lower is not None:
        cv.paint(lower, S, "scales", radius=0.8, bias=-0.05)
        cv.paint(both(lower, cv.ellipse(*lerp(hinge, low_tip, 0.5), sx * 1.2, sy * 0.45)), ramp("belly"), "belly", radius=0.4, line=False)
    chin = both(cv.ellipse(hx + hr * 0.5, hy + hr * 0.66, hr * 0.6, hr * 0.3), headm)
    cv.paint(chin, ramp("belly"), "belly", radius=0.8, line=False, bias=0.05)
    if c["tuft"]:                                                     # a little tuft on top (the babies)
        tx, ty = hx + hr * 0.12, hy - hr * 0.86
        tuft = union(cv.strip([(tx + 1.4, ty + 1.6), (tx + 1.6, ty - 1.0), (tx + 0.2, ty - 2.2)], [2.2, 1.6, 0.3], steps=5),
                     cv.strip([(tx + 2.8, ty + 1.6), (tx + 3.6, ty - 0.2), (tx + 3.0, ty - 1.6)], [1.6, 1.2, 0.3], steps=4))
        cv.paint(tuft, ramp("wings"), "wings", radius=0.4, bias=0.1)
    nx_, ny_ = snout_c[0] + sx * 0.62, snout_c[1] - sy * 0.35
    cv.fill(cv.ellipse(nx_, ny_, 0.42 if baby else 0.6, 0.3 if baby else 0.4), INK, "base")
    if jaw == 0:
        mx, my = snout_c[0] + sx * 0.1, snout_c[1] + sy * 0.55
        if pose.happy or baby or not adult:
            cv.curve([(mx - sx * 0.95, my - 0.4), (mx - sx * 0.2, my + 0.5), (mx + sx * 0.45, my - 0.1)], MOUTH, "base", 2)
        else:
            cv.curve([(hx + hr * 0.25, my + 0.2), (mx + sx * 0.55, my - 0.2)], MOUTH, "base", 2)
        if adult:                                                     # a fang over the lip
            fx_ = mx - sx * 0.05
            cv.fill(cv.poly([(fx_ - 0.45, my), (fx_ + 0.45, my), (fx_, my + 1.1)]), CLAW, "base")
    cv.fill(cv.ellipse(hx + hr * 0.22, hy + hr * 0.52, hr * 0.22, hr * 0.12), CHEEK, "base", only=headm)
    # the eye: big, glossy, kind
    ex, ey = hx + hr * 0.3, hy - hr * 0.06
    erx, ery = c["eye"]
    if pose.closed or pose.kind == "sleep":
        happy = pose.happy
        arc = cv.mask(lambda d: d.arc([(ex - erx) * K, (ey - ery * (0.2 if happy else 0.6)) * K, (ex + erx) * K, (ey + ery * (1.2 if happy else 0.6)) * K],
                                      200 if happy else 20, 340 if happy else 160, fill=255, width=3))
        cv.fill(arc, INK, "base")
        return
    white = cv.ellipse(ex, ey, erx, ery)
    cv.fill(white, WHITE, "base")
    ix, iy = ex + erx * 0.12, ey + ery * 0.08
    iris = both(cv.ellipse(ix, iy, erx * 0.82, ery * 0.86), white)
    cv.fill(iris, g(205), "eyes")
    cv.fill(both(iris, cv.mask(lambda d: d.rectangle([0, 0, cv.W, (iy - ery * 0.25) * K], fill=255))), g(150), "eyes")
    cv.fill(both(iris, minus(cv.ellipse(ix, iy + ery * 0.42, erx * 0.6, ery * 0.34), cv.ellipse(ix, iy + ery * 0.2, erx * 0.6, ery * 0.34))), g(255), "eyes")
    pup = cv.ellipse(ix + erx * 0.05, iy, erx * (0.4 if not adult else 0.26), ery * (0.5 if not adult else 0.62))
    cv.fill(both(pup, white), PUPIL, "base")
    cv.fill(both(cv.ellipse(ix - erx * 0.32, iy - ery * 0.42, erx * 0.3, ery * 0.28), white), WHITE, "base")
    cv.fill(both(cv.ellipse(ix + erx * 0.32, iy + ery * 0.38, erx * 0.13, ery * 0.12), white), WHITE, "base")
    cv.ink(white, 2)
    lid = cv.mask(lambda d: d.arc([(ex - erx * 1.08) * K, (ey - ery * 1.08) * K, (ex + erx * 1.08) * K, (ey + ery * 1.08) * K], 195, 345, fill=255, width=4))
    cv.fill(lid, INK, "base")
    if adult:
        brow = cv.strip([(ex - erx * 1.1, ey - ery * 1.25), (ex, ey - ery * 1.45), (ex + erx * 1.2, ey - ery * 1.1)], [1.2, 1.4, 0.6], steps=4)
        cv.paint(brow, S, "scales", radius=0.4, bias=0.1)


def markings(cv, R, c, body):
    """The element's markings live on the tail now (rings by the flame, see tail())."""
    return


# ---------------------------------------------------------------- sequences and sheets

def sequence(stage):
    """The tags the game asks for (as norse_dragon.build_sequence)."""
    st = 0.5 if STAGES[stage]["baby"] else 1.0
    f = lambda kind="stand", **kw: (stage, Pose(kind, **kw))
    frames, tags = [], {}

    def add_tag(name, poses, ms, loop=True, event=-1):
        tags[name] = (len(frames), len(frames) + len(poses) - 1, loop, event)
        frames.extend((p, ms) for p in poses)

    add_tag("idle", [f(bob=b, pulse=p) for b, p in ((0, 0), (0, 0.3), (0.5 * st, 0.6), (0.5 * st, 0.3))], 220)
    add_tag("walk", [f(legs=l, bob=b) for l, b in ((2, 0), (0, -0.5), (-2, 0), (0, -0.5))], 130)
    flap = [62, 98, 150, 225, 262, 160]
    add_tag("fly", [f("fly", wing=a, wave=math.sin(i / 6 * math.tau) * 2, bob=[0, 0, -0.5, -1, -1, -0.5][i]) for i, a in enumerate(flap)], 80)
    add_tag("glide", [f("fly", wing=a, wave=w) for a, w in ((168, 0.5), (164, -0.5))], 260)
    add_tag("takeoff", [f(wing=95, bob=1.5), f(wing=235, bob=-0.5), f("fly", wing=110)], 110, loop=False)
    add_tag("land", [f("fly", wing=100), f(wing=130, bob=-0.5), f(bob=1)], 120, loop=False)
    add_tag("breathe_windup", [f("windup", glow=gl) for gl in (0.4, 0.9)], 180)
    add_tag("breathe", [f("breathe", jaw=j) for j in (3, 5, 5)], 130, loop=False)
    add_tag("sleep", [f("sleep", bob=b, closed=True) for b in (0, 0.5)], 900)
    add_tag("downed", [f("sleep", closed=True)], 1000, loop=False)
    return frames, tags


def tint(img, rgb):
    a = img.getchannel("A")
    out = ImageChops.multiply(img, Image.new("RGBA", img.size, tuple(rgb) + (255,)))
    out.putalpha(a)
    return out


def composite(parts, look=None):
    look = dict(DEFAULT, **(look or {}))
    out = parts["base"].copy()
    for name in LAYERS[1:]:
        out.alpha_composite(tint(parts[name], look[name]))
    return out


def mouth_point(stage):
    _, R = draw(stage, Pose("breathe", jaw=5))
    c = STAGES[stage]
    hx, hy = R.head
    sx, sy = c["snout"]
    round_ = c["baby"] or not c.get("adult")
    snout_c = (hx + R.hr * 0.78 + sx * 0.25, hy + R.hr * (0.3 if round_ else 0.2))
    return (snout_c[0] + sx * 1.1) * K, (snout_c[1] + sy * 0.2) * K



def stage_sheets(stage, out_root=None):
    """The plain sheet the game draws, at world size (``creatures/dragon_<stage>``)."""
    c = STAGES[stage]
    frames, tags = sequence(stage)
    rendered = [(draw(st, pose)[0].split(), ms) for (st, pose), ms in frames]
    points = {"mouth": mouth_point(stage)}
    small = lambda img: img.resize((img.width // K, img.height // K), Image.BOX)
    write_sheet(f"creatures/dragon_{stage}", [(small(composite(p)), ms) for p, ms in rendered], tags, c["pivot"],
                {k: (x / K, y / K) for k, (x, y) in points.items()}, trim=True, force=True, root=out_root)
    return rendered, tags


def preview(path, stages=("adult",)):
    imgs = []
    for stage in stages:
        frames, tags = sequence(stage)
        for tag in ("idle", "walk", "fly", "breathe", "sleep", "downed"):
            (st, pose), _ = frames[tags[tag][0] + (1 if tag == "walk" else 2 if tag in ("fly", "breathe") else 0)]
            cv, _ = draw(st, pose)
            bg = Image.new("RGBA", (cv.W, cv.H), (96, 120, 80, 255))
            bg.alpha_composite(composite(cv.split()))
            imgs.append(bg)
    contact_sheet(imgs, path, scale=1, columns=6, bg=(40, 40, 40, 255), pad=2)


def generate():
    stage_sheets("adult")
    print("dragon: wrote the adult")


if __name__ == "__main__":
    if "--preview" in sys.argv:
        out = os.path.join(os.path.dirname(__file__), "..", ".cache", "dragons.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        preview(out)
        print(out)
    else:
        generate()
