#!/usr/bin/env python3
"""The season's wild dragon (``creatures/dragon_adult``), drawn in the chibi creatures' style at four
times the detail ("density": 4), side-on and facing right:

    python3 tools/chibi_dragon.py              # the sheet into data/textures
    python3 tools/chibi_dragon.py --preview    # ...and .cache/dragons.png

Our own design (docs/ART.md, "The dragon"): upright on two legs and leaning forward, a long neck
curving like a carved prow into a wedge head with swept-back horns, wings wider than its body, a
ridge of bone plates down the back like a ship's ribs, rust scales with a bone belly and horns, and
glowing ember veins stepped at right angles like runes. It's a crisis, not a pet: a heavy brow, a
narrow burning eye, teeth.

Every frame is one pose of a rig (``Rig``: hips, chest, neck, head, legs, arms, wings and tail),
painted back to front. Tags: idle, walk, fly, glide, takeoff!, land!, breathe_windup, breathe!,
sleep and downed, with a ``mouth`` point where the breath leaves the jaws.
"""
import math
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from chibi_canvas import Canvas, K, INK, union, minus, both, lerp, add, rot  # noqa: E402
from norse_art import ramp, contact_sheet  # noqa: E402
from placeholder_sheets import write_sheet  # noqa: E402

W, H = 128, 96                          # the frame in world pixels (a person is 40x60)
PIVOT = (60, 92)
GROUND = 92

SCALES = ramp((0x9E, 0x40, 0x2E), 6, main=3, dark=0.42, light=1.32)
BELLY = ramp((0xED, 0xD9, 0xB1), 5, main=3, dark=0.55, light=1.06)
MEMBRANE = ramp((0x66, 0x26, 0x21), 5, main=2, dark=0.5, light=1.55)
BONE = ramp((0xE4, 0xD8, 0xBE), 5, main=3, dark=0.5, light=1.08)
MOUTH = (84, 30, 34, 255)
TOOTH = (246, 240, 226, 255)
GLOW = (255, 140, 40, 255)              # glow colours stay bright and outside any grade (ART.md)
HOT = (255, 196, 76, 255)
CORE = (255, 238, 160, 255)
EMBER = (196, 72, 30, 255)


# ---------------------------------------------------------------- poses

class Pose:
    """One frame's pose: ``kind`` stand, fly, windup, breathe, sleep or downed; ``legs`` the stride
    (-1..1); ``bob`` the body's rise and fall; ``wing`` the wings' angle (None: folded) and
    ``spread`` how far they fan; ``jaw`` how far it's open (0..1); ``glow`` the fire within."""

    def __init__(self, kind="stand", legs=0.0, bob=0.0, wing=None, spread=1.0, jaw=0.0, glow=0.0, tail=0.0):
        self.kind, self.legs, self.bob, self.wing, self.spread = kind, legs, bob, wing, spread
        self.jaw, self.glow, self.tail = jaw, glow, tail

    @property
    def lying(self):
        return self.kind in ("sleep", "downed")


class Rig:
    """Where the parts are in one pose (world pixels)."""

    def __init__(self, pose):
        self.pose = pose
        k, b = pose.kind, pose.bob
        if pose.lying:
            self.hip, self.chest = (48, 80), (70, 81)
            self.spine = [(36, 83), (48, 79), (60, 79), (72, 80)]
            self.widths = [12, 21, 22, 19]
            self.neck = [(74, 80), (82, 84), (90, 86), (96, 86)] if k == "downed" else [(74, 79), (82, 82), (90, 84), (96, 84)]
            self.neck_w = [13, 10, 8.5, 8]
            self.tilt = 6 if k == "downed" else 0
            self.tail = [(38, 84), (26, 88), (14, 89), (6, 86), (3, 82)]
            self.shoulder = (62, 72)
        else:
            lift = -5 if k == "fly" else 0
            self.hip = (50, 62 + b + lift)
            self.chest = (70, 47 + b + lift)
            hx, hy = self.hip
            cx, cy = self.chest
            self.spine = [(hx - 9, hy + 2), (hx, hy - 1), (hx + 10, hy - 8), (cx + 3, cy - 2)]
            self.widths = [15, 25, 27, 24]
            if k == "fly":                                            # stretched out, neck and tail level
                self.neck = [(cx + 3, cy - 4), (cx + 13, cy - 8), (cx + 22, cy - 11), (cx + 29, cy - 12)]
                self.tilt = 4
            elif k == "windup":                                       # reared back, drawing breath
                self.neck = [(cx + 2, cy - 5), (cx + 4, cy - 16), (cx + 6, cy - 26), (cx + 13, cy - 31)]
                self.tilt = -18
            elif k == "breathe":                                      # thrust forward and down
                self.neck = [(cx + 3, cy - 4), (cx + 12, cy - 11), (cx + 22, cy - 14), (cx + 30, cy - 13)]
                self.tilt = 10
            else:                                                     # the prow: up, then forward
                self.neck = [(cx + 3, cy - 4), (cx + 6, cy - 15), (cx + 12, cy - 23), (cx + 20, cy - 26)]
                self.tilt = 0
            self.neck_w = [17, 12.5, 10, 9]
            sway = pose.tail
            if k == "fly":
                self.tail = [(hx - 8, hy + 2), (hx - 20, hy + 4), (hx - 32, hy + 3 + sway), (hx - 42, hy - 1 + sway * 1.5), (hx - 48, hy - 6 + sway * 2)]
            else:
                self.tail = [(hx - 8, hy + 3), (hx - 19, hy + 11), (hx - 31, hy + 19 + sway * 0.5), (hx - 42, GROUND - 7 + sway), (hx - 49, GROUND - 9 + sway * 1.5)]
            self.shoulder = (cx - 6, cy - 9)
        self.head = self.neck[-1]


def along(pts, t):
    """The point ``t`` (0..1) of the way along a polyline, and the unit direction there."""
    seg = [math.dist(pts[i], pts[i + 1]) for i in range(len(pts) - 1)]
    d = t * sum(seg)
    for i, s in enumerate(seg):
        if d <= s or i == len(seg) - 1:
            u = d / s if s else 0
            a, b = pts[i], pts[i + 1]
            return lerp(a, b, min(1, u)), ((b[0] - a[0]) / (s or 1), (b[1] - a[1]) / (s or 1))
        d -= s


def width_at(ws, t):
    x = t * (len(ws) - 1)
    i = min(int(x), len(ws) - 2)
    return ws[i] + (ws[i + 1] - ws[i]) * (x - i)


# ---------------------------------------------------------------- the wings

def wing_shape(root, a, span, open_):
    """A bat wing laid out upright (forearm up, fingers fanning back toward the tail) and turned to
    angle ``a`` (degrees: 90 up, past 90 sweeping back, past 180 down). ``open_`` (0 folded .. 1
    spread) fans the fingers. Returns elbow, wrist, finger tips and the trailing point on the body."""
    th = math.radians(a - 90)

    def place(x, y):
        return root[0] + x * math.cos(th) + y * math.sin(th), root[1] - x * math.sin(th) + y * math.cos(th)

    def dir_(deg):
        r = math.radians(deg)
        return -math.sin(r), -math.cos(r)
    ux, uy = dir_(-12)
    e_local = (ux * span * 0.42, uy * span * 0.42)
    fx, fy = dir_(14)
    w_local = (e_local[0] + fx * span * 0.5, e_local[1] + fy * span * 0.5)
    spread = [8 + 30 * open_, 14 + 70 * open_, 20 + 108 * open_]
    lengths = [0.62 + 0.08 * open_, 0.58 + 0.1 * open_, 0.5 + 0.1 * open_]
    tips = []
    for deg, ln in zip(spread, lengths):
        dx, dy = dir_(14 + deg)
        tips.append(place(w_local[0] + dx * span * ln, w_local[1] + dy * span * ln))
    trail = place(-span * (0.22 + 0.42 * open_), span * (0.1 + 0.12 * open_))
    return place(*e_local), place(*w_local), tips, trail


def scallop(p, q, toward, k=0.24, n=6):
    """Points on the membrane's inward curve between two fingers."""
    ctrl = lerp(lerp(p, q, 0.5), toward, k)
    return [lerp(lerp(p, ctrl, i / n), lerp(ctrl, q, i / n), i / n) for i in range(1, n)]


def wing(cv, R, far):
    p = R.pose
    root = add(R.shoulder, (3, -2) if far else (0, 0))
    if p.lying:
        a, spread, span = (196, 0.55, 40) if p.kind == "downed" and not far else (150, 0.12, 36)
    elif p.wing is None:
        a, spread, span = 138, 0.1, 34                                # folded up against the back
    else:
        a, spread, span = p.wing + (7 if far else 0), p.spread, 44
    elbow, wrist, tips, trail = wing_shape(root, a, span, spread)
    edge = [wrist, tips[0]]
    for i in range(2):
        edge += scallop(tips[i], tips[i + 1], wrist)
        edge.append(tips[i + 1])
    edge += scallop(tips[2], trail, wrist, k=0.18)
    edge += [trail, root, elbow]
    bias = -0.4 if far else 0.0
    membrane = cv.poly(edge)
    cv.paint(membrane, MEMBRANE, radius=2.4, bias=bias - 0.05, depth=0.5)
    for tip in tips:                                                  # the fingers, and veins between
        cv.paint(cv.strip([wrist, lerp(wrist, tip, 0.5), tip], [1.6, 1.1, 0.4], steps=4), SCALES[:4], radius=0.4, bias=bias, line=False)
    arm = cv.strip([root, elbow, wrist], [4.0, 3.2, 2.4], steps=5)
    cv.paint(arm, SCALES, radius=0.9, bias=bias + 0.1)
    claw = cv.poly([add(wrist, (-1.2, 0)), add(wrist, (1.2, 0)), add(wrist, (0.2, -3.2))])
    cv.paint(claw, BONE, radius=0.3)
    return membrane


# ---------------------------------------------------------------- the body

def tail(cv, R):
    p = R.pose
    m = cv.strip(R.tail, [13, 10, 6.5, 3.4, 1.0], steps=6)
    tip, d = R.tail[-1], (R.tail[-1][0] - R.tail[-2][0], R.tail[-1][1] - R.tail[-2][1])
    n = math.hypot(*d) or 1
    d = (d[0] / n, d[1] / n)
    barb = cv.poly([add(tip, (-d[1] * 2.6, d[0] * 2.6)), add(tip, d, 5.0), add(tip, (d[1] * 2.6, -d[0] * 2.6)), add(tip, d, -1.5)])
    cv.paint(barb, BONE, radius=0.6, bias=-0.1)
    cv.paint(m, SCALES, radius=2.0, bias=-0.05 if not p.lying else -0.1)
    return m


def plates(cv, R, body_m, neck_m, tail_m):
    """The ridge of bone plates down the back, like a ship's ribs: biggest over the shoulders."""
    runs = [(R.neck[::-1], R.neck_w[::-1], 0.15, 0.85, 4, 3.0, 4.2), (R.spine[::-1], R.widths[::-1], 0.0, 0.9, 5, 4.6, 3.2),
            (R.tail, [13, 10, 6.5, 3.4, 1.0], 0.1, 0.75, 5, 3.0, 1.2)]
    for pts, ws, t0, t1, n, h0, h1 in runs:
        for i in range(n):
            t = t0 + (t1 - t0) * i / max(1, n - 1)
            (x, y), (dx, dy) = along(pts, t)
            nx, ny = dy, -dx                                          # the back's side (up, for a line running right)
            if ny > 0:
                nx, ny = -nx, -ny
            r = width_at(ws, t) / 2 - 0.8
            h = h0 + (h1 - h0) * i / max(1, n - 1)
            base = (x + nx * r, y + ny * r)
            tip = (base[0] + nx * h - dx * h * 0.55, base[1] + ny * h - dy * h * 0.55)
            plate = cv.poly([add(base, (dx, dy), 1.9), tip, add(base, (dx, dy), -1.9), add(base, (nx, ny), -1.5)])
            cv.paint(plate, BONE, radius=0.6, bias=-0.15)


def legs(cv, R, far):
    p = R.pose
    hx, hy = R.hip
    bias = -0.4 if far else 0.0
    if p.lying:                                                       # folded under, the haunch showing
        if far:
            return
        thigh = cv.ellipse(hx + 2, hy + 1, 9, 6.5)
        cv.paint(thigh, SCALES, radius=1.8, bias=0.05)
        foot = cv.strip([(hx + 6, GROUND - 2), (hx + 13, GROUND - 1.4)], [3.6, 3.0], steps=3)
        cv.paint(foot, SCALES, radius=0.8)
        toes(cv, (hx + 13, GROUND - 1.2), 0)
        return
    s = p.legs * (-1 if far else 1)
    off = -2.5 if far else 0
    top = (hx + 3 + off, hy + 3)
    if p.kind == "fly":                                               # tucked, feet trailing
        knee, ankle, toe = (hx + 9 + off, hy + 9), (hx + 2 + off, hy + 15), (hx - 3 + off, hy + 18)
    else:
        knee = (hx + 9 + off + s * 3.5, hy + 13)
        lift = max(0.0, s) * 3.0 if abs(p.legs) > 0.1 else 0
        ankle = (hx + 3 + off + s * 6, GROUND - 6 - lift)
        toe = (ankle[0] + 7, GROUND - 0.8 - lift * 0.6)
    thigh = cv.ellipse(top[0], top[1] + 2, 8.5, 11)
    shin = cv.strip([knee, lerp(knee, ankle, 0.5), ankle], [7.0, 5.0, 4.0], steps=4)
    foot = cv.strip([ankle, toe], [4.2, 3.0], steps=3)
    cv.paint(shin, SCALES, radius=1.0, bias=bias - 0.05)
    cv.paint(foot, SCALES, radius=0.8, bias=bias)
    cv.paint(thigh, SCALES, radius=2.2, bias=bias + 0.05)
    toes(cv, toe, bias)


def toes(cv, at, bias):
    for dx in (-1.6, 0.6, 2.6):
        claw = cv.poly([(at[0] + dx - 0.8, at[1] - 1.0), (at[0] + dx + 0.9, at[1] - 1.0), (at[0] + dx + 1.8, at[1] + 0.7)])
        cv.paint(claw, BONE, radius=0.2, bias=bias)


def arm(cv, R, far):
    p = R.pose
    if p.lying:
        return
    cx, cy = R.chest
    off = (-2, -1) if far else (0, 0)
    sh = add((cx + 4, cy + 4), off)
    if p.kind == "fly":
        el, hand = add((cx + 6, cy + 10), off), add((cx + 12, cy + 12), off)
    elif p.kind in ("windup", "breathe"):                             # braced, claws spread
        el, hand = add((cx + 9, cy + 9), off), add((cx + 15, cy + 7), off)
    else:
        el, hand = add((cx + 7, cy + 11), off), add((cx + 13, cy + 12 + p.bob * 0.5), off)
    limb = cv.strip([sh, el, hand], [5.0, 3.6, 2.8], steps=4)
    cv.paint(limb, SCALES, radius=0.9, bias=-0.4 if far else 0.05)
    for a in (-30, 0, 30):
        d = rot((2.6, 0), a)
        claw = cv.strip([hand, add(hand, d), add(hand, (d[0] * 1.2, d[1] * 1.2 + 1.2))], [1.1, 0.8, 0.2], steps=3)
        cv.paint(claw, BONE, radius=0.2, line=False)


def torso(cv, R):
    m = cv.strip(R.spine, R.widths, steps=6)
    cv.paint(m, SCALES, radius=2.6, bias=0.04, depth=0.4,
             tex=lambda x, y: -0.35 if (x // 7 + y // 6) % 2 == 0 and (x % 7 == 0 or y % 6 == 0) else 0)
    # The bone belly: the underside, banded like a ship's strakes.
    under = cv.strip([add(q, (3.4, 7.6)) for q in R.spine], [w * 0.5 for w in R.widths], steps=6)
    belly = both(under, m)
    cv.paint(belly, BELLY, radius=1.4, line=False, bias=0.05)
    for t in [i / 9 for i in range(1, 9)]:
        (x, y), (dx, dy) = along([add(q, (3.4, 7.6)) for q in R.spine], t)
        r = width_at(R.widths, t) * 0.28
        band = cv.strip([(x - dy * r, y + dx * r), (x + dy * r, y - dx * r)], [0.5, 0.5], steps=2)
        cv.fill(both(band, belly), BELLY[1])
    return m


def neck(cv, R, body_m):
    m = cv.strip(R.neck, R.neck_w, steps=6)
    cv.paint(m, SCALES, radius=2.0, bias=0.06, outside=body_m)
    throat = both(cv.strip([add(q, (3.0, 3.4)) for q in R.neck], [w * 0.42 for w in R.neck_w], steps=6), m)
    cv.paint(throat, BELLY, radius=1.2, line=False)
    return m


def veins(cv, R, masks):
    """Rune-stepped ember veins: right-angled runs over the flank, haunch, neck and tail, clipped to
    the hide. Brighter as the fire builds."""
    p = R.pose
    hx, hy = R.hip
    cx, cy = R.chest
    paths = [
        [(hx + 2, hy - 2), (hx + 7, hy - 2), (hx + 7, hy - 6), (hx + 13, hy - 6), (hx + 13, hy - 10), (cx + 1, hy - 10)],
        [(hx - 4, hy + 2), (hx - 4, hy + 6), (hx + 1, hy + 6), (hx + 1, hy + 10)],
        [(cx - 2, cy - 3), (cx + 3, cy - 3), (cx + 3, cy - 7)],
    ]
    (nx, ny), _ = along(R.neck, 0.45)
    paths.append([(nx - 2, ny + 2), (nx + 1, ny + 2), (nx + 1, ny - 1), (nx + 4, ny - 1)])
    (tx, ty), _ = along(R.tail, 0.3)
    paths.append([(tx + 4, ty - 1), (tx, ty - 1), (tx, ty + 2), (tx - 5, ty + 2)])
    hide = union(*masks)
    hot = p.glow > 0.5
    for pts in paths:
        line = cv.mask(lambda d, pts=pts: d.line([cv.P(*q) for q in pts], fill=255, width=round(1.3 * cv.k), joint="curve"))
        cv.fill(both(line, hide), GLOW)
        core = cv.mask(lambda d, pts=pts: d.line([cv.P(*q) for q in pts], fill=255, width=max(2, round(0.5 * cv.k))))
        cv.fill(both(core, hide), CORE if hot else HOT)
        for q in pts[1:-1]:                                           # the joints burn hottest
            cv.fill(both(cv.ellipse(q[0], q[1], 0.8, 0.8), hide), CORE if hot else HOT)
    if p.glow > 0:                                                    # the fire building in the chest and throat
        (cx2, cy2), (dx, dy) = along(R.neck, 0.4)
        for r, col in ((4.0, EMBER), (3.0, GLOW), (1.9, HOT), (1.0, CORE)):
            g = cv.ellipse(cx2 + dy * 2.2, cy2 - dx * 2.2 + 1.5, r * (0.5 + 0.5 * p.glow), r * (0.5 + 0.5 * p.glow))
            cv.fill(both(g, hide), col)


# ---------------------------------------------------------------- the head

def head_pts(R):
    """The head's outline points round its pivot (the neck's top), turned by the pose's tilt."""
    hx, hy = R.head
    t = R.tilt

    def P(x, y):
        dx, dy = rot((x, y), t)
        return hx + dx, hy + dy
    return P


def head(cv, R, neck_m):
    p = R.pose
    P = head_pts(R)
    closed = p.lying
    # Horns, swept back: the far one first, behind the skull.
    for far in (True, False):
        o = (1.6, -1.2) if far else (0, 0)
        horn = cv.strip([P(-1 + o[0], -4 + o[1]), P(-7 + o[0], -8 + o[1]), P(-13 + o[0], -9.5 + o[1]), P(-17 + o[0], -8 + o[1])],
                        [3.6, 2.8, 1.6, 0.4], steps=5)
        cv.paint(horn, BONE, radius=0.8, bias=-0.4 if far else 0.0, tex=lambda x, y: -0.4 if x % 9 == 0 else 0)
    # The lower jaw, hinged under the ear, opening for the breath.
    jaw_a = 26 * p.jaw
    hinge = P(-1, 2.5)

    def J(x, y):
        dx, dy = rot((x - hinge[0], y - hinge[1]), jaw_a)
        return hinge[0] + dx, hinge[1] + dy
    jaw = cv.poly([J(*P(-2, 1.5)), J(*P(13, 3.2)), J(*P(14.5, 4.4)), J(*P(12, 5.6)), J(*P(0, 6.2)), J(*P(-3, 4.5))])
    if p.jaw > 0:                                                     # the mouth's red, and the fire in it
        maw = cv.poly([P(-1, 2), P(15, 2.4), J(*P(14.5, 4.4)), J(*P(0, 5.5))])
        cv.fill(maw, MOUTH)
        if p.kind == "breathe":
            cv.fill(both(cv.poly([P(2, 2.6), P(15, 2.6), J(*P(13, 3.8)), J(*P(2, 4.4))]), maw), GLOW)
            cv.fill(both(cv.poly([P(5, 2.8), P(15, 2.8), J(*P(13, 3.4)), J(*P(5, 3.8))]), maw), CORE)
    cv.paint(jaw, SCALES, radius=0.8, bias=-0.05)
    cv.paint(both(cv.poly([J(*P(0, 4.6)), J(*P(12, 3.8)), J(*P(12, 5.2)), J(*P(0, 6.2))]), jaw), BELLY, radius=0.5, line=False)
    for x in (4, 7, 10, 12.5):                                        # lower teeth
        cv.fill(cv.poly([J(*P(x - 0.6, 3.0)), J(*P(x + 0.6, 3.0)), J(*P(x, 1.6))]), TOOTH)
    # The skull: a wedge, heavy-browed, the snout running out to a blunt point.
    skull = cv.poly([P(-5, -3), P(-2, -5.6), P(4, -5.8), P(9, -3.6), P(15, -1.6), P(17, 0.6), P(16, 2.6), P(4, 3.2), P(-3, 4.2), P(-6, 1)])
    cv.paint(skull, SCALES, radius=1.4, bias=0.12, outside=neck_m)
    for x in (3, 6, 9, 12, 14.5):                                     # upper teeth over the lip
        cv.fill(cv.poly([P(x - 0.7, 2.6), P(x + 0.7, 2.6), P(x, 4.2)]), TOOTH)
    brow = cv.poly([P(0, -6.2), P(7.5, -5.0), P(8.5, -3.6), P(1, -3.2)])
    cv.paint(brow, SCALES[:4], radius=0.5, bias=-0.2)
    cv.fill(cv.ellipse(*P(15, -0.6), 0.55, 0.45), INK)                 # the nostril
    if p.glow > 0.5:
        cv.fill(cv.ellipse(*P(15.2, -0.8), 0.3, 0.25), HOT)
    for k in range(3):                                                # a frill of spines along the jaw's back
        base = P(-4.5 - k * 1.2, 2 + k * 1.4)
        spine = cv.poly([add(base, (0.9, -0.8)), add(base, (-4 + k * 0.6, 1.0 + k * 0.4)), add(base, (0.6, 0.9))])
        cv.paint(spine, BONE, radius=0.3, bias=-0.2)
    ex, ey = P(4.8, -2.2)
    if closed:
        cv.curve([(ex - 1.8, ey + 0.3), (ex, ey + 0.8), (ex + 1.8, ey + 0.2)], INK, width=3)
    else:                                                             # a narrow burning eye, slit pupil
        eye = cv.poly([(ex - 2.2, ey + 0.4), (ex - 0.2, ey - 1.1), (ex + 2.2, ey - 0.4), (ex + 0.4, ey + 0.9)])
        cv.fill(eye, GLOW)
        cv.fill(both(cv.ellipse(ex + 0.2, ey - 0.2, 1.2, 0.6), eye), HOT)
        cv.fill(both(cv.mask(lambda d: d.line([cv.P(ex + 0.3, ey - 1.2), cv.P(ex + 0.1, ey + 1.0)], fill=255, width=3)), eye), INK)
        cv.ink(eye, 1)


# ---------------------------------------------------------------- one frame

def draw(pose):
    cv = Canvas(W, H)
    R = Rig(pose)
    wing(cv, R, far=True)
    legs(cv, R, far=True)
    arm(cv, R, far=True)
    tail_m = tail(cv, R)
    plates(cv, R, None, None, tail_m)
    legs(cv, R, far=False)
    body_m = torso(cv, R)
    neck_m = neck(cv, R, body_m)
    veins(cv, R, [body_m, neck_m, tail_m])
    head(cv, R, neck_m)
    arm(cv, R, far=False)
    wing(cv, R, far=False)
    return cv, R


def mouth_point():
    """Where the breath leaves the jaws (sheet pixels), from the breathe pose."""
    R = Rig(Pose("breathe", jaw=1.0, glow=1.0))
    x, y = head_pts(R)(17, 3.2)
    return x * K, y * K


def sequence():
    """The tags the game asks for (docs/ART.md, "Animation tags the game asks for")."""
    frames, tags = [], {}

    def add_tag(name, poses, ms, loop=True, event=-1):
        tags[name] = (len(frames), len(frames) + len(poses) - 1, loop, event)
        frames.extend((p, ms) for p in poses)

    add_tag("idle", [Pose(bob=b, glow=g, tail=t) for b, g, t in ((0, 0, 0), (0.4, 0, 0.6), (0.8, 0, 1.0), (0.4, 0, 0.6))], 240)
    add_tag("walk", [Pose(legs=l, bob=b, tail=t) for l, b, t in ((1, 0, 0), (0, -0.8, 0.8), (-1, 0, 1.4), (0, -0.8, 0.8))], 150)
    flap = [62, 98, 150, 225, 262, 160]
    add_tag("fly", [Pose("fly", wing=a, spread=0.95, bob=[0, 0, -0.6, -1.2, -1.2, -0.6][i], tail=[0, 0.6, 1.2, 0.6, 0, -0.6][i]) for i, a in enumerate(flap)], 80)
    add_tag("glide", [Pose("fly", wing=a, spread=1.0, tail=t) for a, t in ((168, 0.4), (164, -0.4))], 260)
    add_tag("takeoff", [Pose(wing=95, spread=0.7, bob=1.5), Pose(wing=235, spread=0.9, bob=-0.5), Pose("fly", wing=110)], 110, loop=False)
    add_tag("land", [Pose("fly", wing=100), Pose(wing=130, spread=0.8, bob=-0.5), Pose(bob=1.2)], 120, loop=False)
    add_tag("breathe_windup", [Pose("windup", glow=g, jaw=j) for g, j in ((0.4, 0.15), (0.9, 0.25))], 180)
    add_tag("breathe", [Pose("breathe", jaw=j, glow=1.0) for j in (0.7, 1.0, 1.0)], 130, loop=False)
    add_tag("sleep", [Pose("sleep", bob=b, glow=0) for b in (0, 0.5)], 900)
    add_tag("downed", [Pose("downed")], 1000, loop=False)
    return frames, tags


def generate(out_root=None):
    frames, tags = sequence()
    rendered = [(draw(p)[0].img, ms) for p, ms in frames]
    pivot = (PIVOT[0] * K, PIVOT[1] * K)
    write_sheet("creatures/dragon_adult", rendered, tags, pivot, {"mouth": mouth_point()}, trim=True, force=True, root=out_root, density=K)
    print("dragon: wrote the wild dragon")
    return rendered, tags


def preview(path):
    frames, tags = sequence()
    imgs = []
    for tag in ("idle", "walk", "fly", "glide", "breathe_windup", "breathe", "sleep", "downed"):
        pose, _ = frames[tags[tag][0] + (1 if tag in ("walk", "breathe") else 3 if tag == "fly" else 0)]
        cv, _ = draw(pose)
        bg = Image.new("RGBA", (cv.W, cv.H), (96, 120, 80, 255))
        bg.alpha_composite(cv.img)
        if tag == "breathe":                                          # mark the mouth point
            mx, my = mouth_point()
            for d in range(-6, 7):
                for x, y in ((mx + d, my), (mx, my + d)):
                    if 0 <= x < cv.W and 0 <= y < cv.H:
                        bg.putpixel((int(x), int(y)), (60, 200, 255, 255))
        imgs.append(bg)
    contact_sheet(imgs, path, scale=1, columns=4, bg=(40, 40, 40, 255), pad=4)


if __name__ == "__main__":
    if "--preview" in sys.argv:
        out = os.path.join(os.path.dirname(__file__), "..", ".cache", "dragons.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        preview(out)
        print(out)
    else:
        generate()
