#!/usr/bin/env python3
"""The wild animals in the people's and dragons' chibi style (round bodies, big heads, big glossy
eyes, clean outlines), side-on at four times the detail ("density": 4):

    python3 tools/chibi_animals.py              # creatures/deer, boar, hare, wolf, bear into data/textures
    python3 tools/chibi_animals.py --preview    # ...and .cache/animals.png

Same frame sizes, pivots and tags as before (idle, walk, sleep, downed: a carcass is the animal
lying on its side). Drawn as is, untinted. Shapes in world pixels on chibi_dragon's Canvas.
"""
import math
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from chibi_dragon import Canvas, K, INK, WHITE, PUPIL, union, minus, both, lerp, add  # noqa: E402
from norse_art import ramp, contact_sheet  # noqa: E402
from placeholder_sheets import write_sheet  # noqa: E402

CHEEK = (240, 156, 146, 255)
HOOF = ramp((70, 50, 44), 4)
NOSE = (52, 34, 38, 255)


def shade(cv, m, colors, line=True, radius=1.2, **kw):
    cv.paint(m, colors, "base", radius=radius, line=line, **kw)


def eye(cv, x, y, rx, ry, closed=False, iris=(92, 58, 40)):
    """A big glossy eye: dark iris with a light crescent, two catch-lights, a lid line."""
    if closed:
        arc = cv.mask(lambda d: d.arc([(x - rx) * cv.k, (y - ry * 0.6) * cv.k, (x + rx) * cv.k, (y + ry * 0.6) * cv.k], 20, 160, fill=255, width=3))
        cv.fill(arc, INK, "base")
        return
    white = cv.ellipse(x, y, rx, ry)
    cv.fill(white, WHITE, "base")
    irs = both(cv.ellipse(x + rx * 0.1, y + ry * 0.08, rx * 0.84, ry * 0.88), white)
    cv.fill(irs, iris + (255,), "base")
    lo = tuple(min(255, int(c * 1.45 + 30)) for c in iris) + (255,)
    cv.fill(both(irs, minus(cv.ellipse(x + rx * 0.1, y + ry * 0.5, rx * 0.6, ry * 0.32), cv.ellipse(x + rx * 0.1, y + ry * 0.26, rx * 0.6, ry * 0.32))), lo, "base")
    cv.fill(both(cv.ellipse(x + rx * 0.14, y + ry * 0.05, rx * 0.42, ry * 0.5), white), PUPIL, "base")
    cv.fill(both(cv.ellipse(x - rx * 0.25, y - ry * 0.38, rx * 0.32, ry * 0.3), white), WHITE, "base")
    cv.fill(both(cv.ellipse(x + rx * 0.38, y + ry * 0.36, rx * 0.15, ry * 0.14), white), WHITE, "base")
    cv.ink(white, 2)


def legs4(cv, body, ground, thick, pose, colors, hoof=None, length_front=1.0):
    """Four legs under a body (cx, cy, rx, ry): far pair darker and drawn first, near pair after."""
    bx, by, rx, ry = body
    out = []
    for near in (False, True):
        for front in (False, True):
            stride = pose["legs"] * (1 if front == near else -1)
            x = bx + rx * (0.55 if front else -0.5) + (0 if near else -0.8)
            top = (x, by + ry * 0.35)
            if pose.get("dead"):                                      # stiff, sticking out along the ground
                foot = (x + (thick * 3.2 if front else -thick * 3.0) + (0 if near else 0.6), ground - 0.9 - (0 if near else 0.8))
            elif pose["sleep"]:                                       # folded under the body
                foot = (x + (thick * 1.6 if front else thick * 0.9), ground - 0.5)
            else:
                foot = (x + stride * 0.45, ground - 0.2 - max(0, -stride) * 0.18)
            m = cv.strip([top, lerp(top, foot, 0.55), foot], [thick * 1.25, thick, thick * 0.9], steps=4)
            out.append((near, m, foot))
    return out


# ---------------------------------------------------------------- the animals

SCALE = {"deer": 1.55, "boar": 1.75, "hare": 1.3, "wolf": 1.6, "bear": 1.9}   # drawn bigger than their old frames: the deer about 3/4 of a person


def deer(pose):
    cv = Canvas(40, 36, K * SCALE["deer"])
    ground, bob = 35, pose["bob"]
    coat = ramp((178, 112, 62), 5)
    cream = ramp((240, 222, 186), 4)
    body = (16.5, 25.4 + bob, 8.0, 5.4)
    if pose["sleep"]:
        body = (16.5, ground - 5.0, 8.4, 4.8)
    bx, by, rx, ry = body
    legs = legs4(cv, body, ground, 1.9, pose, coat)
    for near, m, foot in legs:
        if not near:
            shade(cv, m, coat, bias=-0.3)
            cv.fill(cv.ellipse(foot[0], foot[1] - 0.5, 0.9, 0.7), HOOF[1], "base")
    tail = union(cv.ellipse(bx - rx * 0.92, by - ry * 0.45, 1.6, 1.3))
    shade(cv, tail, cream)
    torso = cv.ellipse(bx, by, rx, ry)
    shade(cv, torso, coat, radius=1.6, bias=0.05)
    shade(cv, both(cv.ellipse(bx + 1, by + ry * 0.55, rx * 0.8, ry * 0.45), torso), cream, line=False)
    for k, (u, v) in enumerate(((-0.4, -0.45), (-0.1, -0.6), (0.2, -0.5), (-0.55, -0.15), (0.0, -0.25), (0.42, -0.3))):   # fawn spots
        cv.fill(both(cv.ellipse(bx + u * rx, by + v * ry, 0.5, 0.42), torso), cream[3], "base")
    for near, m, foot in legs:
        if near:
            shade(cv, m, coat, outside=torso)
            cv.fill(cv.ellipse(foot[0], foot[1] - 0.5, 0.9, 0.7), HOOF[1], "base")
    # neck and the big head
    hx, hy = bx + rx * 1.15, by - ry * 1.75 + (pose["head"])
    if pose["sleep"]:
        hx, hy = bx + rx * 1.0, by - ry * 0.8
    neck = cv.strip([(bx + rx * 0.55, by - ry * 0.3), lerp((bx + rx * 0.55, by - ry * 0.3), (hx - 1.0, hy + 1.6), 0.5), (hx - 1.0, hy + 1.6)], [4.2, 3.6, 3.2], steps=4)
    shade(cv, neck, coat, outside=torso)
    for far in (True, False):                                         # leaf ears, pink inside
        ex = hx - (2.6 if not far else 1.6)
        ear = cv.poly([(ex - 0.2, hy - 1.2), (ex - 3.6 + pose["ear"], hy - 3.4), (ex + 0.8, hy - 2.6)])
        shade(cv, ear, coat, bias=-0.3 if far else 0)
        cv.fill(both(cv.poly([(ex - 0.2, hy - 1.5), (ex - 2.8 + pose["ear"], hy - 3.0), (ex + 0.3, hy - 2.4)]), ear), CHEEK, "base")
    for far in (True, False):                                         # little antlers
        ax = hx - (0.6 if not far else -0.2)
        cv.curve([(ax, hy - 3.6), (ax - 0.4, hy - 6.0), (ax - 1.6, hy - 7.2)], cream[2], "base", 4)
        cv.curve([(ax - 0.3, hy - 5.2), (ax + 0.8, hy - 6.4)], cream[2], "base", 3)
    head = union(cv.ellipse(hx, hy, 4.9, 4.5), cv.ellipse(hx + 3.9, hy + 1.3, 2.5, 2.0))
    shade(cv, head, coat, radius=1.4, bias=0.12)
    shade(cv, both(cv.ellipse(hx + 2.6, hy + 2.4, 2.8, 1.4), head), cream, line=False)
    cv.fill(cv.ellipse(hx + 5.6, hy + 0.7, 0.55, 0.45), NOSE, "base")
    eye(cv, hx + 0.9, hy - 0.4, 1.5, 1.8, closed=pose["closed"])
    cv.fill(cv.ellipse(hx + 0.6, hy + 1.8, 0.9, 0.5), CHEEK, "base")
    return cv


def boar(pose):
    cv = Canvas(36, 26, K * SCALE["boar"])
    ground, bob = 25, pose["bob"]
    hide = ramp((104, 88, 84), 5)
    dark = ramp((70, 56, 56), 4)
    snout_c = ramp((206, 150, 140), 4)
    tusk = ramp((240, 232, 210), 3)
    body = (15, 16.6 + bob, 9.6, 6.2)
    if pose["sleep"]:
        body = (15, ground - 5.6, 9.8, 5.6)
    bx, by, rx, ry = body
    legs = legs4(cv, body, ground, 1.7, pose, hide)
    for near, m, foot in legs:
        if not near:
            shade(cv, m, hide, bias=-0.3)
            cv.fill(cv.ellipse(foot[0], foot[1] - 0.4, 1.0, 0.6), HOOF[0], "base")
    cv.curve([(bx - rx * 0.95, by - ry * 0.2), (bx - rx * 1.2, by - ry * 0.55), (bx - rx * 1.05, by - ry * 0.85), (bx - rx * 1.25, by - ry * 0.9)], hide[1], "base", 4)
    torso = cv.ellipse(bx, by, rx, ry)
    shade(cv, torso, hide, radius=1.8, bias=0.05)
    shade(cv, both(cv.ellipse(bx + 1.5, by + ry * 0.6, rx * 0.75, ry * 0.4), torso), ramp((150, 128, 120), 4), line=False)
    for k in range(9):                                                # the bristly mane down the back
        t = k / 8
        x = bx + rx * (0.6 - 1.2 * t)
        y = by - ry * (0.95 - 0.25 * abs(t - 0.4))
        spike = cv.poly([(x - 1.0, y + 0.8), (x + 1.0, y + 0.8), (x - 0.6, y - 1.6 - 0.6 * (1 - abs(t - 0.4)))])
        shade(cv, spike, dark, radius=0.4, outside=torso)
    for near, m, foot in legs:
        if near:
            shade(cv, m, hide, outside=torso)
            cv.fill(cv.ellipse(foot[0], foot[1] - 0.4, 1.0, 0.6), HOOF[0], "base")
    hx, hy = bx + rx * 0.95, by - ry * 0.25 + pose["head"]
    if pose["sleep"]:
        hx, hy = bx + rx * 0.95, ground - 4.4
    for far in (True, False):
        ex = hx - (2.2 if not far else 1.2)
        ear = cv.poly([(ex - 1, hy - 3.2), (ex - 1.6 + pose["ear"], hy - 6.0), (ex + 1.0, hy - 3.6)])
        shade(cv, ear, hide, bias=-0.3 if far else 0)
    head = cv.ellipse(hx, hy, 5.2, 4.6)
    shade(cv, head, hide, radius=1.4, bias=0.12, outside=torso)
    snout = cv.ellipse(hx + 4.8, hy + 1.2, 1.7, 2.0)
    shade(cv, snout, snout_c, radius=0.5)
    for dy in (-0.6, 0.8):
        cv.fill(cv.ellipse(hx + 5.3, hy + 1.2 + dy, 0.32, 0.42), NOSE, "base")
    t_ = cv.strip([(hx + 3.2, hy + 2.6), (hx + 3.8, hy + 1.4), (hx + 3.4, hy + 0.2)], [1.0, 0.8, 0.3], steps=4)
    shade(cv, t_, tusk, radius=0.3)
    eye(cv, hx + 0.6, hy - 0.6, 1.3, 1.5, closed=pose["closed"], iris=(70, 44, 34))
    brow = cv.strip([(hx - 0.8, hy - 2.4), (hx + 0.6, hy - 2.7), (hx + 2.0, hy - 2.0)], [0.8, 0.9, 0.4], steps=3)
    shade(cv, brow, dark, radius=0.3, line=False)
    cv.fill(cv.ellipse(hx + 0.6, hy + 1.6, 0.9, 0.5), CHEEK, "base")
    return cv


def hare(pose):
    cv = Canvas(20, 16, K * SCALE["hare"])
    ground = 15
    hop = pose["hop"]
    fur = ramp((190, 172, 150), 5)
    cream = ramp((244, 236, 224), 3)
    by = 10.6 - hop + pose["bob"]
    body = (8.0, by, 4.4, 3.4)
    bx, _, rx, ry = body
    if pose["sleep"]:
        by = ground - 3.0
        body = (8.0, by, 4.8, 2.8)
    bx, by, rx, ry = body
    tail = cv.ellipse(bx - rx * 0.95, by - ry * 0.2, 1.3, 1.2)
    shade(cv, tail, cream)
    # the big haunch and hind foot, a small front paw
    haunch = cv.ellipse(bx - rx * 0.35, by + ry * 0.2, 2.6, 2.6)
    hind = cv.ellipse(bx - rx * 0.1 + hop * 0.6, min(ground - 0.7, by + ry * 0.95), 2.4, 0.8)
    front = cv.strip([(bx + rx * 0.55, by + ry * 0.5), (bx + rx * 0.7 + hop * 0.3, ground - 0.5 - hop * 0.4)], [1.2, 1.0], steps=2)
    torso = cv.ellipse(bx, by, rx, ry)
    shade(cv, union(hind), fur, bias=-0.1)
    shade(cv, torso, fur, radius=1.2, bias=0.05)
    shade(cv, both(cv.ellipse(bx + 1, by + ry * 0.6, rx * 0.7, ry * 0.45), torso), cream, line=False)
    shade(cv, haunch, fur, outside=torso)
    cv.fill(cv.mask(lambda d: d.arc([(bx - rx * 0.35 - 2.6) * cv.k, (by + ry * 0.2 - 2.6) * cv.k, (bx - rx * 0.35 + 2.6) * cv.k, (by + ry * 0.2 + 2.6) * cv.k], 200, 320, fill=255, width=2)), fur[1], "base")
    if not pose["sleep"]:
        shade(cv, front, fur, outside=torso)
    hx, hy = bx + rx * 0.9, by - ry * 0.95
    if pose["sleep"]:
        hx, hy = bx + rx * 0.95, ground - 2.6
    for far in (True, False):                                         # long ears, laid back asleep
        ex = hx - (0.9 if not far else 0.3)
        tip = (ex - 2.4 + pose["ear"], hy - 6.4) if not pose["sleep"] else (ex - 5.4, hy - 1.4)
        ear = cv.strip([(ex, hy - 1.6), lerp((ex, hy - 1.6), tip, 0.5), tip], [1.4, 1.5, 0.5], steps=4)
        shade(cv, ear, fur, bias=-0.3 if far else 0)
        if not far:
            cv.fill(both(cv.strip([(ex, hy - 1.8), lerp((ex, hy - 1.8), tip, 0.5), lerp((ex, hy - 1.8), tip, 0.92)], [0.6, 0.7, 0.2], steps=4), ear), CHEEK, "base")
    head = cv.ellipse(hx, hy, 2.8, 2.5)
    shade(cv, head, fur, radius=0.9, bias=0.12)
    cv.fill(cv.ellipse(hx + 2.6, hy + 0.5, 0.35, 0.3), NOSE, "base")
    eye(cv, hx + 0.6, hy - 0.2, 0.95, 1.1, closed=pose["closed"], iris=(60, 40, 34))
    cv.fill(cv.ellipse(hx + 0.6, hy + 1.2, 0.6, 0.35), CHEEK, "base")
    return cv


def wolf(pose):
    """A grey wolf: lean, long-legged, a long muzzle, pointed ears and a bushy tail."""
    cv = Canvas(36, 28, K * SCALE["wolf"])
    ground, bob = 27, pose["bob"]
    fur = ramp((150, 152, 160), 5)
    dark = ramp((92, 94, 106), 4)
    pale = ramp((222, 220, 214), 3)
    body = (15, 17.8 + bob, 8.8, 5.4)
    if pose["sleep"]:
        body = (15, ground - 4.6, 9.2, 4.4)
    bx, by, rx, ry = body
    legs = legs4(cv, body, ground, 1.9, pose, fur)
    for near, m, foot in legs:
        if not near:
            shade(cv, m, fur, bias=-0.35)
            cv.fill(cv.ellipse(foot[0] + 0.3, foot[1] - 0.3, 0.9, 0.5), dark[0], "base")
    tail_tip = (bx - rx * 1.55, by - ry * (0.1 if not pose["sleep"] else -0.6) + 1.4)
    tail = cv.strip([(bx - rx * 0.85, by - ry * 0.35), (bx - rx * 1.25, by - ry * 0.2), tail_tip], [1.6, 2.2, 1.0], steps=5)
    shade(cv, tail, fur, bias=-0.1)
    cv.fill(both(cv.ellipse(tail_tip[0], tail_tip[1], 1.2, 1.2), tail), pale[1], "base")
    torso = cv.ellipse(bx, by, rx, ry)
    shade(cv, torso, fur, radius=1.6, bias=0.05)
    shade(cv, both(cv.ellipse(bx + 1.5, by + ry * 0.65, rx * 0.7, ry * 0.38), torso), pale, line=False)
    shade(cv, both(cv.ellipse(bx - 0.5, by - ry * 0.7, rx * 0.85, ry * 0.35), torso), dark, line=False)     # the dark saddle
    for near, m, foot in legs:
        if near:
            shade(cv, m, fur, outside=torso)
            cv.fill(cv.ellipse(foot[0] + 0.3, foot[1] - 0.3, 0.9, 0.5), dark[0], "base")
    hx, hy = bx + rx * 1.0, by - ry * 0.75 + pose["head"]
    if pose["sleep"]:
        hx, hy = bx + rx * 1.0, ground - 3.8
    for far in (True, False):
        ex = hx - (1.8 if not far else 0.8)
        ear = cv.poly([(ex - 2.0, hy - 2.4), (ex - 0.5 + pose["ear"] * 0.5, hy - 8.0), (ex + 1.6, hy - 2.8)])
        shade(cv, ear, fur if not far else dark, bias=-0.2)
        if not far:
            cv.fill(both(cv.poly([(ex - 1.0, hy - 3.0), (ex - 0.4 + pose["ear"] * 0.5, hy - 6.4), (ex + 0.8, hy - 3.2)]), ear), CHEEK, "base")
    head = cv.ellipse(hx, hy, 4.8, 4.3)
    shade(cv, head, fur, radius=1.2, bias=0.12, outside=torso)
    muzzle = cv.ellipse(hx + 4.0, hy + 1.2, 2.6, 1.6)
    shade(cv, muzzle, pale, radius=0.6)
    cv.fill(cv.ellipse(hx + 6.2, hy + 0.7, 0.6, 0.5), NOSE, "base")
    cv.fill(cv.mask(lambda d: d.line([((hx + 3.2) * cv.k, (hy + 2.2) * cv.k), ((hx + 5.8) * cv.k, (hy + 2.0) * cv.k)], fill=255, width=2)), INK, "base")
    eye(cv, hx + 0.8, hy - 0.4, 1.2, 1.35, closed=pose["closed"], iris=(196, 150, 46))
    cv.fill(cv.ellipse(hx + 0.6, hy + 1.6, 0.8, 0.45), CHEEK, "base")
    return cv


def bear(pose):
    """A brown bear: big and round, heavy legs, small round ears, a pale muzzle."""
    cv = Canvas(44, 34, K * SCALE["bear"])
    ground, bob = 33, pose["bob"]
    fur = ramp((122, 84, 56), 5)
    dark = ramp((80, 54, 38), 4)
    pale = ramp((196, 160, 120), 3)
    body = (19, 19.6 + bob, 12.0, 8.4)
    if pose["sleep"]:
        body = (19, ground - 7.2, 12.6, 7.2)
    bx, by, rx, ry = body
    legs = legs4(cv, body, ground, 2.6, pose, fur)
    for near, m, foot in legs:
        if not near:
            shade(cv, m, fur, bias=-0.35)
            cv.fill(cv.ellipse(foot[0] + 0.4, foot[1] - 0.4, 1.6, 0.8), dark[0], "base")
    torso = cv.ellipse(bx, by, rx, ry)
    shade(cv, torso, fur, radius=2.2, bias=0.05)
    shade(cv, both(cv.ellipse(bx + 2.0, by + ry * 0.6, rx * 0.7, ry * 0.38), torso), pale, line=False)
    hump = cv.ellipse(bx + rx * 0.35, by - ry * 0.8, rx * 0.45, ry * 0.35)                               # the shoulder hump
    shade(cv, both(hump, torso), dark, line=False)
    for near, m, foot in legs:
        if near:
            shade(cv, m, fur, outside=torso)
            cv.fill(cv.ellipse(foot[0] + 0.4, foot[1] - 0.4, 1.6, 0.8), dark[0], "base")
    hx, hy = bx + rx * 0.95, by - ry * 0.35 + pose["head"]
    if pose["sleep"]:
        hx, hy = bx + rx * 0.95, ground - 5.2
    for far in (True, False):
        ex = hx - (2.6 if not far else 0.6)
        ear = cv.ellipse(ex, hy - 4.6, 1.6, 1.5)
        shade(cv, ear, fur if not far else dark, bias=-0.2)
        if not far:
            cv.fill(both(cv.ellipse(ex, hy - 4.5, 0.8, 0.7), ear), CHEEK, "base")
    head = cv.ellipse(hx, hy, 5.8, 5.2)
    shade(cv, head, fur, radius=1.6, bias=0.12, outside=torso)
    muzzle = cv.ellipse(hx + 4.6, hy + 1.6, 2.6, 2.1)
    shade(cv, muzzle, pale, radius=0.7)
    cv.fill(cv.ellipse(hx + 6.4, hy + 0.9, 0.9, 0.7), NOSE, "base")
    eye(cv, hx + 1.0, hy - 0.8, 1.3, 1.45, closed=pose["closed"], iris=(70, 44, 30))
    cv.fill(cv.ellipse(hx + 0.6, hy + 2.0, 1.0, 0.55), CHEEK, "base")
    return cv


KINDS = {"deer": deer, "boar": boar, "hare": hare, "wolf": wolf, "bear": bear}


def poses(kind):
    """idle 4, walk 4 (the hare hops), sleep 2, downed 1: as before."""
    P = lambda **kw: dict(dict(legs=0, bob=0, head=0, ear=0, closed=False, sleep=False, hop=0), **kw)
    idle = [P(), P(bob=0.25), P(bob=0.25, ear=0.6), P(closed=True)]
    if kind == "hare":
        walk = [P(hop=0), P(hop=1.6, legs=1), P(hop=2.2, legs=1.5), P(hop=0.8)]
    else:
        walk = [P(legs=1.6), P(legs=0, bob=-0.3), P(legs=-1.6), P(legs=0, bob=-0.3)]
    sleep = [P(sleep=True, closed=True, bob=0), P(sleep=True, closed=True, bob=0.25)]
    return [("idle", idle, 300), ("walk", walk, 130 if kind != "hare" else 110), ("sleep", sleep, 900)]


def lying(cv):
    """The carcass: the standing figure rolled onto its side, feet out, on the ground."""
    img = cv.img
    turned = img.rotate(90, resample=Image.NEAREST, expand=False)
    bb = turned.getbbox()
    out = Image.new("RGBA", img.size, (0, 0, 0, 0))
    if bb:
        crop = turned.crop(bb)
        out.alpha_composite(crop, ((img.width - crop.width) // 2, img.height - crop.height - K))
    return out


def sheet(kind):
    frames, tags = [], {}
    for tag, ps, ms in poses(kind):
        tags[tag] = (len(frames), len(frames) + len(ps) - 1, True, -1)
        frames += [(KINDS[kind](p).img, ms) for p in ps]
    dead = KINDS[kind](dict(legs=0, bob=0, head=0, ear=0, closed=True, sleep=True, hop=0, dead=True))
    tags["downed"] = (len(frames), len(frames), False, -1)
    frames.append((dead.img, 1000))
    return frames, tags


PIVOTS = {"deer": (20, 35), "boar": (18, 25), "hare": (10, 15), "wolf": (17, 27), "bear": (22, 33)}


def generate():
    for kind in KINDS:
        frames, tags = sheet(kind)
        px, py = PIVOTS[kind]
        k = K * SCALE[kind]                                              # the design's pivot, in the finer pixels
        write_sheet("creatures/" + kind, frames, tags, (px * k, py * k), trim=True, force=True, density=K)
    print("animals: wrote", ", ".join(KINDS))


if __name__ == "__main__":
    if "--preview" in sys.argv:
        imgs = []
        for kind in KINDS:
            frames, tags = sheet(kind)
            for tag in ("idle", "walk", "sleep", "downed"):
                img, _ = frames[tags[tag][0] + (2 if tag == "walk" else 0)]
                bg = Image.new("RGBA", (round(44 * K * 1.9), round(36 * K * 1.9)), (96, 120, 80, 255))
                bg.alpha_composite(img, ((bg.width - img.width) // 2, bg.height - img.height))
                imgs.append(bg)
        out = os.path.join(os.path.dirname(__file__), "..", ".cache", "animals.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        contact_sheet(imgs, out, scale=1, columns=4, bg=(40, 40, 40, 255), pad=2)
        print(out)
    else:
        generate()
