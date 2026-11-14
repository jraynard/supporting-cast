#!/usr/bin/env python3
"""The people: big-headed chibi Norse villagers at four times the detail (tools/chibi_art.py), in
layers the game mixes and matches (Rendering/HumanLook, docs/ART.md "People"):

    python3 tools/chibi_people.py                 # every sheet into data/textures
    python3 tools/chibi_people.py --preview       # ...and contact sheets in .cache/people/
    python3 tools/chibi_people.py --only hair     # just the sheets whose names contain "hair"

  creatures/person_<body>                  linen underclothes: the base every layer matches by frame
  creatures/person_<body>_skin             head, arms, legs (tinted with the skin colour)
  creatures/person_<body>_eyes_<style>     irises (tinted); _face_<style>: whites, lids, lashes, mouth, blush
  creatures/person_hair_<style>[_back]     hair and brows (tinted), the back half drawn behind the body
  creatures/person_beard_<style>           beards (tinted with the hair colour)
  apparel/<worn>_<body>[_dye][_back][_back_dye]   every garment and piece of armor

Every frame of every sheet is one pose of one shared skeleton (``Fig``: the shoulders, elbows,
hands, legs, head drop of a body in a frame), so sleeves, bracers and armor follow the arms through
the crossed, hip and coin stances. 147 frames: idle, walk, work and the three stances in four
views (facing the viewer, turned toward screen right, turned away, from behind; the game mirrors
the turned ones), then sleep and downed, then riding astride and sitting in the four views. Garments draw what lies over their cloth into the plain
sheet and cut it from the dyed one (the game draws the plain sheet first); whatever a crossed
arm or a coin covers is cut from the torso; back panels and the back half of the hair are cut
under the base, since the game draws them after it.
"""
import os
import sys
from multiprocessing import Pool

sys.path.insert(0, os.path.dirname(__file__))
from chibi_art import *  # noqa: E402,F401,F403
import chibi_art as A  # noqa: E402
from placeholder_sheets import write_sheet  # noqa: E402
from norse_art import contact_sheet  # noqa: E402

BODIES = ("male", "female")
FOLK = ("human", "elf")                                    # Folk's order; each but the first has its own skin sheet
HAIR_STYLES = ("short", "long", "ponytail", "pigtails", "curlyshort", "curlylong", "none", "sidebraid", "topknot", "bun",
               "crownbraid", "shaggy", "swept", "braidedponytail")           # HairStyle's order
EYE_STYLES = ("round", "narrow", "sleepy", "bright")       # EyeStyle's order
BEARDS = ("stubble", "short", "full", "braided")
VIEWS = ("down", "downside", "upside", "up")


# ---------------------------------------------------------------- poses and the skeleton

class Pose:
    def __init__(self, view, anim, k=0, head_dy=0.0, body_dy=0.0, legs=((0, 0), (0, 0)), swing=0, blink=False,
                 lying=False, helm=True, coin=None):
        self.view, self.anim, self.k = view, anim, k
        self.head_dy, self.body_dy, self.legs, self.swing = head_dy, body_dy, legs, swing
        self.blink, self.lying, self.helm, self.coin = blink, lying, helm, coin


# How far the body drops onto a seat (design units of the 40x60 frame): the game lifts the figure
# by as much less the stool's height (WorldRenderer.SitSeat).
SIT_DROP = 6.0


def sequence():
    """The 147 frames and their tags, as the game asks for them (CreatureAnimator, HumanLook.IdleTag)."""
    frames, tags = [], {}

    def add(name, poses, ms, loop=True, event=-1):
        if not isinstance(ms, (list, tuple)):
            ms = [ms] * len(poses)
        tags[name] = (len(frames), len(frames) + len(poses) - 1, loop, event)
        frames.extend(zip(poses, ms))

    for v in VIEWS:
        face = v in ("down", "downside")
        add(f"idle_{v}", [Pose(v, "idle"), Pose(v, "idle", head_dy=0.25), Pose(v, "idle", head_dy=0.5),
                          Pose(v, "idle", head_dy=0.25, blink=face)], 300)
        turned = v in ("downside", "upside")
        stride = ([((-1.2, 0.8), (1.4, 0)), ((-0.2, 0), (0.2, 1.4)), ((-1.4, 0), (1.2, 0.8)), ((-0.2, 1.4), (0.2, 0))] if turned else
                  [((0, 0), (0, 1.5)), ((0, 0.5), (0, 0)), ((0, 1.5), (0, 0)), ((0, 0), (0, 0.5))])
        add(f"walk_{v}", [Pose(v, "walk", k, body_dy=(0.5, 0, 0.5, 0)[k], legs=stride[k], swing=(1, 0, -1, 0)[k]) for k in range(4)], 140)
        add(f"work_{v}", [Pose(v, "work", k, body_dy=(0, 0, 0.5, 0)[k]) for k in range(4)], 140, event=2)
        add(f"idle_{v}_crossed", [Pose(v, "crossed", k, head_dy=(0, 0.25, 0.5, 0.25)[k], blink=face and k == 3) for k in range(4)], 380)
        add(f"idle_{v}_hip", [Pose(v, "hip", k, head_dy=(0, 0.25, 0.5, 0.25)[k], blink=face and k == 2) for k in range(4)], 380)
        flick = (0, 2, 5, 7.5, 5, 2, 0, 0)
        add(f"idle_{v}_coin", [Pose(v, "coin", k, coin=flick[k], blink=face and k == 7) for k in range(8)],
            [600, 90, 90, 120, 90, 90, 500, 260])
    add("sleep", [Pose("down", "sleep", blink=True, lying=True, helm=False), Pose("down", "sleep", 1, head_dy=0.25, blink=True, lying=True, helm=False)], 1100)
    add("downed", [Pose("down", "downed", blink=True, lying=True)], 1000, loop=False)
    # Astride a dragon (after the rest so the frames above keep their indexes): knees out over its
    # flanks, hands forward on the reins, a gentle bob with the wingbeat.
    for v in VIEWS:
        add(f"ride_{v}", [Pose(v, "ride", k, body_dy=(0, 0.5, 0.75, 0.25)[k], head_dy=(0, 0.1, 0.25, 0.1)[k],
                               legs=((-2.4, 3.0), (2.4, 3.0))) for k in range(4)], 180)
    # Sat on a stool with a drink or a bowl (after the rest, so the frames above keep their
    # indexes): the body drops onto the seat and the knees come forward, so the legs foreshorten;
    # one hand rests at the lap, the other holds the cup there, and now and then lifts it for a sip.
    for v in VIEWS:
        face = v in ("down", "downside")
        knees = ((-1.4, 0), (1.4, 0))
        add(f"sit_{v}", [Pose(v, "sit", 0, body_dy=SIT_DROP, legs=knees), Pose(v, "sit", 1, body_dy=SIT_DROP, head_dy=0.25, legs=knees),
                         Pose(v, "sit", 2, body_dy=SIT_DROP, head_dy=-0.25, legs=knees),
                         Pose(v, "sit", 3, body_dy=SIT_DROP, head_dy=0.25, blink=face, legs=knees)],
            [1400, 260, 900, 300])
    for alias in ("idle", "walk", "work", "ride", "sit"):
        tags[alias] = tags[f"{alias}_down"]
    return frames, tags


BODY = {
    "female": dict(torso=[(15, 31), (25, 31), (26, 39), (14, 39)], shoulders=(13.5, 26.5), legs=(15.5, 21.5), leg_w=3.0),
    "male": dict(torso=[(14.3, 31), (25.7, 31), (25.6, 39.4), (14.4, 39.4)], shoulders=(13.0, 27.0), legs=(15.2, 21.4), leg_w=3.4),
}


class Fig:
    """One body in one frame: where everything is. Design units throughout (the 40x60 frame)."""

    def __init__(self, body, pose):
        self.body, self.pose, self.g = body, pose, BODY[body]
        self.male = body == "male"
        self.view = pose.view
        self.front = pose.view in ("down", "downside")
        self.back = not self.front
        self.t = 1 if pose.view in ("downside", "upside") else 0
        self.fs = 2 * self.t if self.front else 0
        self.by = pose.body_dy
        self.hy = pose.head_dy + pose.body_dy
        self.arms = [self.arm(side) for side in (0, 1)]
        self.coin = None
        if pose.coin is not None:
            hand = self.arms[self.role_side(1)][2]
            self.coin = (hand[0], hand[1] - 1.8 - pose.coin)

    def role_side(self, side):
        """From behind, the arm on the screen's left is the other one."""
        return 1 - side if self.back else side

    def arm(self, side):
        """(shoulder, elbow, hand, in front of the body) for one side (0: screen left)."""
        p, by = self.pose, self.by
        sx = self.g["shoulders"][side]
        out = -1 if side == 0 else 1
        role = self.role_side(side)
        shoulder = (sx, 32.5 + by)
        elbow, hand, front = (sx + out * 1.0, 37 + by), (sx + out * 1.5, 42 + by + p.swing * (1 if side == 0 else -1)), False
        if p.anim == "work" and role == 0:
            hand = [(sx - 2.5 * -out, 28 + by), (sx - 2 * -out, 25 + by), (sx + 2.5 * -out, 41 + by), (sx - 1 * -out, 42 + by)][p.k]
            hand = (hand[0] if out < 0 else 40 - hand[0] + 0, hand[1]) if False else hand
            front = p.k == 2
        elif p.anim == "crossed":
            # folded high on the chest: each forearm runs across to tuck its fist under the other arm
            elbow = (sx + out * 1.4, 36.6 + by)
            hand = (25.0, 34.0 + by) if side == 0 else (15.0, 35.0 + by)
            front = True
        elif p.anim == "hip" and role == 1:
            # akimbo: the elbow juts well out, the fist sits on the hip
            elbow, hand, front = (sx + out * 3.8, 36.2 + by), (sx + out * 0.6, 40.4 + by), True
        elif p.anim == "coin" and role == 0:
            elbow, hand, front = (sx + out * 2.6, 36.4 + by), (sx - out * 0.6, 39.4 + by), True
        elif p.anim == "ride":
            # both hands forward at the waist on the reins: in front from the front, hidden from behind
            elbow, hand, front = (sx + out * 2.2, 36.6 + by), (sx - out * 1.2, 40.2 + by), self.front
        elif p.anim == "sit":
            # hands together at the lap, the cup in the right; on k == 2 it's lifted to the mouth
            if role == 1 and p.k == 2:
                elbow, hand, front = (sx + out * 2.4, 37.4 + by), (sx - out * 3.6, 30.5 + by), self.front
            else:
                elbow, hand, front = (sx + out * 2.0, 37.2 + by), (sx - out * 1.6, 41.2 + by), self.front
        elif p.anim == "coin" and role == 1:
            lift = 0.6 if p.coin and p.coin > 0 else 0
            elbow, hand, front = (sx + out * 1.6, 38.2 + by), (sx - out * 1.4, 35 + by - lift), True
        return shoulder, elbow, hand, front

    def arm_mask(self, side, upto=1.0, width=(3.0, 2.8, 2.6)):
        shoulder, elbow, hand, _ = self.arms[side]
        end = lerp(elbow, hand, upto) if upto <= 1 else hand
        if upto <= 0.5:
            end = lerp(shoulder, elbow, upto * 2)
            return strip([shoulder, end], [width[0], width[1]], steps=3)
        return strip([shoulder, elbow, end], list(width), steps=4)

    def front_arms(self):
        """What lies in front of the torso: forearms and fists of arms held before it, and a coin."""
        m = nothing()
        if self.back:
            return m
        for side in (0, 1):
            shoulder, elbow, hand, front = self.arms[side]
            if front:   # the drawn forearm and fist (skin_layer), so they show whole over the clothes
                m = union(m, strip([lerp(elbow, hand, 0.12), hand], [2.8, 2.6], steps=3), ellipse([hand[0] - 1.6, hand[1] - 1.4, hand[0] + 1.6, hand[1] + 1.6]))
        if self.coin:
            m = union(m, ellipse([self.coin[0] - 1.2, self.coin[1] - 1.2, self.coin[0] + 1.2, self.coin[1] + 1.2]))
        return m

    def torso(self):
        return poly([(x, y + self.by) for x, y in self.g["torso"]])

    def leg(self, i):
        sx, lift = self.pose.legs[i]
        x0 = self.g["legs"][i] + sx
        return x0, 42 + self.by, 57 - lift, self.g["leg_w"]

    def head(self):
        return ellipse([9.5, 5 + self.hy, 30.5, 31.2 + self.hy])

    def face_rect(self):
        return ellipse([10, 9 + self.hy, 30, 31 + self.hy])


# ---------------------------------------------------------------- body: linen, skin, eyes, face

def base_layer(F):
    """Linen underclothes: a chemise for her, a shirt and braies for him; and the coin."""
    img = blank()
    by = F.by
    if F.male:
        shirt = union(F.torso(), rect([14.6, 38.6 + by, 25.4, 41 + by]))
        braies = union(rect([14.6, 40.4 + by, 25.4, 43 + by]), *[rect([F.leg(i)[0] - 0.2, 42 + by, F.leg(i)[0] + F.g["leg_w"] + 0.2, 45 + by]) for i in (0, 1)])
        part(img, braies, LINEN, INK, radius=1.2, bias=-0.1)
        part(img, shirt, LINEN, INK, radius=1.4)
    else:
        shift = union(F.torso(), poly([(14.2, 38.6 + by), (25.8, 38.6 + by), (26.6, 46 + by), (13.4, 46 + by)]))
        part(img, shift, LINEN, INK, radius=1.4)
        curve(img, [(17, 40 + by), (16.6, 45.5 + by)], LINEN[1], 1, shift)
        curve(img, [(23, 40 + by), (23.4, 45.5 + by)], LINEN[1], 1, shift)
    return img


def coin_layer(F, img):
    if F.coin is None:
        return
    x, y = F.coin
    edge_on = F.pose.k % 2 == 1
    c = ellipse([x - (0.4 if edge_on else 1.1), y - 1.1, x + (0.4 if edge_on else 1.1), y + 1.1])
    part(img, c, GOLD, INK, radius=0.4, bias=0.2, width=1)


def elf_ear(ex, hy, s):
    """Our elves' ear: long, narrow and swept up and back past the head, tip above the brow."""
    return poly([(ex - s * 0.4, 19.6 + hy), (ex + s * 1.6, 17.4 + hy), (ex + s * 6.0, 12.4 + hy), (ex + s * 3.4, 18.2 + hy),
                 (ex + s * 1.8, 21.6 + hy), (ex + s * 0.5, 24.2 + hy), (ex - s * 0.6, 23.4 + hy)])


def ear_spots(F):
    """Each ear's root (x) and side, as the head turns: the far one's hidden in a front three-quarter view."""
    for s in (-1, 1):
        if F.front and F.t and s == 1:
            continue
        yield 20 + s * 10.4 + (F.t if F.front else 0) * (0.6 if s < 0 else 0), s


def ears_layer(F, folk):
    """The ears again, over the hair (a folk whose ears stand out through it): what shows past the head."""
    img = blank()
    if folk != "elf":
        return img
    for ex, s in ear_spots(F):
        part(img, minus(elf_ear(ex, F.hy, s), F.head()), SKIN, SKIN_LINE, radius=0.6)
    return img


def skin_layer(F, folk="human"):
    """The head (all of it: hair and helms go over), ears, arms with fists, legs."""
    img = blank()
    by, hy, t = F.by, F.hy, F.t
    for i in (0, 1):
        x0, top, foot, w = F.leg(i)
        part(img, rect([x0, top, x0 + w, foot - 0.6]), SKIN, SKIN_LINE, radius=1.0)
        part(img, ellipse([x0 - 0.6, foot - 2.6, x0 + w + 0.8, foot + 0.6]), SKIN, SKIN_LINE, radius=0.8)
    # ears, then the head over their roots
    for ex, s in ear_spots(F):
        ear = elf_ear(ex, hy, s) if folk == "elf" else ellipse([ex - 1.1, 19.6 + hy, ex + 1.1, 24.4 + hy])
        part(img, ear, SKIN, SKIN_LINE, radius=0.6)
    part(img, F.head(), SKIN, SKIN_LINE, radius=2.4, depth=0.2, bias=0.18)
    arms_drawn = []
    for side in (0, 1):
        shoulder, elbow, hand, front = F.arms[side]
        arm = F.arm_mask(side)
        fist = ellipse([hand[0] - 1.6, hand[1] - 1.4, hand[0] + 1.6, hand[1] + 1.6])
        if F.back and front:
            # held before the chest, seen from behind: only the upper arm shows past the body
            arm = minus(arm, F.torso())
            fist = minus(fist, F.torso())
        part(img, arm, SKIN, SKIN_LINE, radius=1.0)
        part(img, fist, SKIN, SKIN_LINE, radius=0.8)
        arms_drawn.append(arm)
    coin_layer(F, img)
    return img


EYE_AT = ((14.6, 1.0), (25.4, 1.0))


def eye_spots(F):
    """Each eye's centre (design units) and horizontal squash: turned, they slide toward the facing
    side and the far one narrows."""
    if F.t:
        return [(14.6 + 1.6, 1.0), (25.4 + 1.0, 0.78)]
    return list(EYE_AT)


def eyes_layers(F, style):
    """(irises, face lines) for an eye style; nothing from behind."""
    eyes_img, face_img = blank(), blank()
    if F.back:
        return eyes_img, face_img
    face = F.face_rect()
    for i, (x, q) in enumerate(eye_spots(F)):
        eye(eyes_img, face_img, P(x, 22.4 + F.hy), -1 if i == 0 else 1, F.pose.blink, q, style, F.male)
    cx, cy = P(20 + (1.5 if F.t else 0), 22.4 + F.hy)
    for i, (x, q) in enumerate(eye_spots(F)):                                 # blush under each eye
        s = -1 if i == 0 else 1
        bx, by_ = P(x + s * 0.9 * q, 0)[0], cy + 15
        fill(face_img, oval(bx, by_, 7 * q, 3.5), BLUSH_SOFT, face)
        if not F.male:
            for k in range(3 if q == 1.0 else 2):
                line(face_img, (bx - 4 * q + k * 3, by_ + 2), (bx - 2 * q + k * 3, by_ - 2), BLUSH, face)
    fine(face_img, [(cx, cy + 15), (cx + 1, cy + 15)], NOSE)
    if F.male:
        curve(face_img, [(cx - 4, cy + 23), (cx + 4, cy + 22.6)], MOUTH, 2, design=False)
    else:
        curve(face_img, [(cx - 4, cy + 22), (cx, cy + 23.6), (cx + 4, cy + 22)], MOUTH, 2, design=False)
    return eyes_img, face_img


def eye(eyes_img, face_img, c, s, blink, squash, style, male):
    """One eye at sheet point ``c`` (``s`` = -1 for the viewer's left). Round: the Valkyrie's eye (a
    thick lid sweeping to a wing, a ringed iris dark on top with a light crescent, a big catch-light);
    bright: a bigger iris and more sparkle; narrow: a flat, keen eye; sleepy: a heavy lowered lid."""
    cx, cy = c
    q = squash
    sq = s * q
    ov = lambda x, y, rx, ry: oval(cx + (x - cx) * q, y, rx * q, ry)
    lash_w = 0 if male else 1
    if blink:
        lid = mask(lambda d: d.arc([cx - 12 * q, cy - 6, cx + 12 * q, cy + 8], 200, 340, fill=255, width=3))
        fill(face_img, lid, LASH)
        if not male:
            curve(face_img, [(cx - sq * 2, cy + 3), (cx + sq * 12, cy - 1), (cx + sq * 15, cy - 4)], LASH, 2, design=False)
        return
    top_cut = {"round": cy + 2, "bright": cy + 2, "narrow": cy + 2, "sleepy": cy + 2}[style]
    if style == "narrow":
        white = both(ov(cx, cy + 3, 11.5, 8), ov(cx, cy + 5, 13, 9))
        irx, iry = 7.5, 8.5
    elif style == "bright":
        white = both(ov(cx, cy + 1, 11.5, 13), ov(cx, cy + 3, 13.5, 12.5))
        irx, iry = 9.5, 11.5
    else:
        white = both(ov(cx, cy + 1, 11, 12.5), ov(cx, cy + 3, 13, 12))
        irx, iry = 8.5, 10.5
    if male:
        irx, iry = irx * 0.92, iry * 0.92
    if style == "sleepy":
        white = minus(white, rect([0, 0, 40, (cy - 1) / K]))
    ix, iy = cx - sq * 1.8, cy + 2 + (1 if style == "narrow" else 0)
    iris = both(ov(ix, iy, irx, iry), white)
    fill(face_img, minus(white, iris), WHITE)
    fill(eyes_img, iris, IRIS[1])
    fill(eyes_img, both(iris, rect([0, 0, 40, (iy - 3) / K])), IRIS[0])
    fill(eyes_img, both(iris, minus(ov(ix, iy + 4, irx - 2, 5.5), ov(ix, iy + 1, irx - 2, 5.5))), IRIS[2])
    fill(eyes_img, edge(iris, 1), grey(96))
    fill(face_img, both(ov(ix, iy - 0.5, 3.2, 4.4 if style != "narrow" else 3.6), white), PUPIL)
    hl = both(ov(ix - 3.4, iy - 4.4 + (2 if style == "sleepy" else 0), 2.6, 3.2), white)
    fill(face_img, hl, WHITE)
    fill(face_img, both(ov(ix + 3.4, iy + 4.4, 1.2, 1.2), white), WHITE)
    if style == "bright":
        fill(face_img, both(ov(ix + 3.6, iy - 4.6, 1.1, 1.1), white), WHITE)
        fine(face_img, [(ix - 4 * q, iy + 4), (ix - 4 * q, iy + 5), (ix - 5 * q, iy + 4.5), (ix - 3 * q, iy + 4.5)], WHITE, white)
    # the upper lid
    if style == "narrow":
        lid = mask(lambda d: d.polygon([(cx - 12 * q, cy - 2), (cx + 12 * q, cy - 4 - (2 * s if s else 0) * 0), (cx + 12 * q, cy), (cx - 12 * q, cy + 1.5)], fill=255))
        lid = union(lid, both(minus(ov(cx, cy + 3, 13, 9.5), ov(cx, cy + 5.5, 13, 9)), rect([0, 0, 40, (cy + 3) / K])))
    elif style == "sleepy":
        lid = both(minus(ov(cx, cy + 6, 13, 9), ov(cx, cy + 8, 13, 8.4)), rect([0, 0, 40, (cy + 5) / K]))
        fill(face_img, both(ov(cx, cy - 1.5, 12, 4.5), rect([0, (cy - 6) / K, 40, (cy - 0.6) / K])), LID)   # the heavy lid's skin line
    else:
        thick = 4 if style == "round" else 3
        lid = minus(ov(cx, cy + 1, 13, 13.5), ov(cx, cy + 1 + thick, 13, 13.5 - thick + 1.5))
        lid = both(lid, rect([0, 0, 40, top_cut / K]))
    if male:
        lid = minus(lid, rect([0, 0, 40, (cy - 11) / K]))
    fill(face_img, lid, LASH)
    if not male and style != "sleepy":
        wing = mask(lambda d: d.polygon([(cx + sq * 9, cy - 9 + (5 if style == "narrow" else 0)), (cx + sq * 17, cy - 9 + (6 if style == "narrow" else 0)),
                                         (cx + sq * 12, cy - 2 + (3 if style == "narrow" else 0))], fill=255))
        fill(face_img, wing, LASH)
        if style != "narrow":
            for k in range(2):
                line(face_img, (cx + sq * (4 + k * 4), cy - 11), (cx + sq * (6 + k * 5), cy - 14), LASH)
    low = cy + (13.4 if style != "narrow" else 10.6)
    curve(face_img, [(cx - 6 * q, low), (cx, low + 0.8), (cx + 6 * q, low)], LID, 1, design=False)
    _ = lash_w


# ---------------------------------------------------------------- hair

def dilate(m, n):
    return m.filter(ImageFilter.MaxFilter(2 * n + 1))


def under_helm(m, F):
    """Hair above the brow band's lower edge stays inside the helm's outline: the head's own
    shape, so it neither pokes out from under a helm nor looks cut with it off."""
    cx = 20 + F.t
    hy = F.hy
    head = union(ellipse([cx - 12, 2 + hy, cx + 12, 28 + hy]), rect([cx - 12.5, 13 + hy, cx + 12.5, 16.5 + hy]))
    return minus(m, minus(rect([0, 0, 40, 16.5 + hy]), head))


def crown(F):
    return minus(ellipse([8 + F.t, 4 + F.hy, 32 + F.t, 30 + F.hy]), rect([0, 17 + F.hy, 40, 60]))


def lock(F, pts, widths, steps=6):
    return strip([(x, y + F.hy) for x, y in pts], widths, steps)


def side_x(F, s, x):
    """x mirrored to side s (-1 left, 1 right); turned, the far lock hugs the cheek."""
    return 20 + s * (20 - x) + (0.6 if F.t and s == 1 else 0)


def fringe(F, kind):
    """(mask, partings) for a fringe kind."""
    fs, hy = F.fs * 0.5, F.hy
    sh = lambda p: (p[0] + fs, p[1] + hy)
    band = rect([10 + fs, 13.4 + hy, 30 + fs, 16 + hy])
    ms, partings = [band], []
    if kind in ("parted", "parted_short"):
        short = 1.2 if kind == "parted_short" else 0
        roots = [(19, 13.6), (16.5, 13.6), (14, 13.6), (11.6, 14), (21, 13.6), (23.5, 13.6), (26, 13.6), (28.4, 14)]
        tips = [(17.6, 20.2), (14.8, 18.9), (12.2, 19.6), (10.4, 22.8), (22.4, 20.2), (25.2, 18.9), (27.8, 19.6), (29.6, 22.8)]
        for (rx, ry), (tx, ty) in zip(roots, tips):
            ty -= short
            s = -1 if tx < 20 else 1
            mid = (rx + (tx - rx) * 0.45 + s * 0.6, ry + (ty - ry) * 0.5)
            ms.append(strip([sh((rx, ry)), sh(mid), sh((tx, ty))], [3.4, 2.8, 0.3], steps=6))
            partings.append([sh((rx - s * 1.2, ry + 0.8)), sh((mid[0] - s * 1.2, mid[1])), sh((tx - s * 1.0, ty - 1.6))])
        if not short:
            ms.append(strip([sh((20, 13.6)), sh((20.3, 18.5)), sh((20, 22.6))], [2.4, 1.6, 0.3], steps=5))
    elif kind == "swept":
        for k in range(7):
            rx = 11 + k * 3
            tx, ty = rx + 4.2, 18.6 + (k % 3) * 0.9 + (2.4 if 3 <= k <= 4 else 0)
            mid = (rx + 2.2, 16.6)
            ms.append(strip([sh((rx, 13.6)), sh(mid), sh((tx, ty))], [3.6, 3.0, 0.3], steps=6))
            partings.append([sh((rx + 0.8, 14.4)), sh((mid[0] + 0.4, mid[1] + 0.4)), sh((tx - 1.2, ty - 1.6))])
    elif kind in ("spiky", "messy"):
        n = 7
        for k in range(n):
            rx = 11.2 + k * 2.9
            jag = (0.8, -0.4, 1.2, 0, 1, -0.2, 0.6)[k] if kind == "messy" else 0
            s = -1 if rx < 20 else 1
            tx, ty = rx + s * 1.0, 18.2 + jag + (0.4 if k % 2 else 0)
            ms.append(strip([sh((rx, 13.6)), sh((rx + s * 0.4, 16)), sh((tx, ty))], [3.4, 2.8, 0.3], steps=5))
            partings.append([sh((rx + 1.3, 14.2)), sh((rx + 1.5, ty - 2.2))])
    elif kind == "blunt":
        ms.append(rect([10.4 + fs, 13.4 + hy, 29.6 + fs, 17.8 + hy]))
        for k in range(9):
            x = 11 + k * 2.3
            ms.append(poly([sh((x - 1.1, 17.6)), sh((x + 1.1, 17.6)), sh((x, 19.4))]))
            partings.append([sh((x + 1.15, 14.4)), sh((x + 1.15, 18))])
    elif kind == "curly":
        for k in range(8):
            x = 11 + k * 2.6
            y = 16.8 + (k % 2) * 0.8
            ms.append(ellipse([x - 1.8 + fs, y - 1.8 + hy, x + 1.8 + fs, y + 1.8 + hy]))
    elif kind == "bare":
        ms = [poly([sh((10.2, 16)), sh((14, 13.8)), sh((20, 13.2)), sh((26, 13.8)), sh((29.8, 16)), sh((29.8, 13)), sh((10.2, 13))])]
        partings.append([sh((20, 13.2)), sh((20, 9))])
    return union(*ms), partings


def side_locks(F, length, width=4.2, tuck=True, curly=False, curls=None):
    """The locks framing the face, down to ``length`` (design y)."""
    out = []
    for s in (-1, 1):
        X = lambda x: side_x(F, s, x)
        if curly:
            n = int((length - 15) / 2.6) + 1
            for k in range(n):
                y = 15.5 + k * 2.6
                x = X(9.6 - (k % 2) * 0.6)
                out.append(ellipse([x - 1.9, y - 1.9 + F.hy, x + 1.9, y + 1.9 + F.hy]))
                if curls is not None:
                    curls.append((x, y + F.hy, 1.9))
            continue
        if length <= 26:
            out.append(lock(F, [(X(10.6), 14), (X(9.4), 19), (X(9.8), length)], [width, width * 0.8, 0.3]))
        else:
            mid = 14 + (length - 14) * 0.45
            out.append(lock(F, [(X(10.6), 14), (X(8.8), mid - 3), (X(8.6), mid + 2), (X(9.6), length - 4), (X(8.8), length)],
                            [width, width, width * 0.9, width * 0.6, 0.3]))
            out.append(lock(F, [(X(12), 15), (X(11.2), 21), (X(11.8), 26.5)], [2.4, 1.8, 0.3], steps=5))
    return union(*out)


def back_mass(F, length, curly=False, width=1.0, curls=None):
    """The hair behind a facing figure, down to ``length``."""
    hy, t = F.hy, F.t
    if curly:
        parts = [ellipse([8.5 + t, 10 + hy, 31.5 + t, min(length, 34) + hy])]
        for s in (-1, 1):
            for k in range(int((length - 18) / 3) + 1):
                y = 18 + k * 3
                x = 20 + s * (11.6 - (k % 2) * 0.8) * width + t
                parts.append(ellipse([x - 2.4, y - 2.4 + hy, x + 2.4, y + 2.4 + hy]))
                if curls is not None:
                    curls.append((x, y + hy, 2.4))
        return union(*parts)
    parts = [ellipse([8.5 + t, 10 + hy, 31.5 + t, min(length, 34) + hy])]
    if length > 30:
        for s in (-1, 1):
            X = lambda x: 20 + s * (20 - x) * width + t
            parts.append(strip([(X(9.5), 15 + hy), (X(7.2), 23 + hy), (X(7.4), 31 + hy), (X(6.6), length - 6 + hy), (X(8), length + hy)], [5, 5, 4.4, 3.2, 0.3], steps=6))
            parts.append(strip([(X(11.5), 18 + hy), (X(10.2), 27 + hy), (X(10.8), length - 9 + hy), (X(11.4), length + 1.2 + hy)], [4, 4.2, 3.6, 0.3], steps=6))
    return union(*parts)


def mane(F, length, curly=False, tips=7, curls=None):
    """From behind: the whole head of hair, ending in points (or curls) at ``length``."""
    hy, t = F.hy, F.t
    parts = [crown(F), ellipse([8 + t, 9 + hy, 32 + t, min(length, 34) + hy])]
    if curly:
        for k in range(int((length - 12) / 3) + 1):
            for x in (10.5, 15, 20, 25, 29.5):
                y = 12 + k * 3 + (1.5 if x in (15, 25) else 0)
                if y < length:
                    parts.append(ellipse([x - 2.6 + t, y - 2.6 + hy, x + 2.6 + t, y + 2.6 + hy]))
                    if curls is not None:
                        curls.append((x + t, y + hy, 2.6))
        return union(*parts)
    span = 22.4
    for k in range(tips):
        root = 8.5 + k * span / (tips - 1)
        x = root + (-1.2 if root < 20 else 1.2 if root > 20 else 0) * (1 if length > 32 else 0.4)
        y = length + (2.4 if k % 2 else 0) - (1.2 if k in (0, tips - 1) else 0)
        parts.append(strip([(root + t, 14 + hy), (root + t, (14 + y) / 2 + hy), (x + t, y + hy)], [5.6 * 7 / tips, 5 * 7 / tips, 0.3], steps=6))
    return union(*parts)


def curl_marks(img, curls, m):
    """Each curl a coil: a shadowed rim on its lower right, a catch of light on its upper left."""
    for x, y, r in curls:
        cx, cy = P(x, y)
        rr = r * K
        rim = mask(lambda d: d.arc([cx - rr + 1, cy - rr + 1, cx + rr - 1, cy + rr - 1], 10, 150, fill=255, width=2))
        fill(img, rim, HAIR[1], m)
        shine = mask(lambda d: d.arc([cx - rr * 0.6, cy - rr * 0.6, cx + rr * 0.6, cy + rr * 0.6], 200, 260, fill=255, width=2))
        fill(img, shine, HAIR[4], m)


def braid(spine, width, n):
    """A plait: lobes alternating along a spine (design points)."""
    pts = catmull([P(*q) for q in spine], 8)
    lobes = []
    seg = len(pts) / n
    for k in range(n):
        a = pts[int(k * seg)]
        b = pts[min(len(pts) - 1, int((k + 1) * seg))]
        cx, cy = (a[0] + b[0]) / 2 + (1 if k % 2 else -1) * width * K * 0.12, (a[1] + b[1]) / 2
        r = width * K * (0.55 - 0.15 * k / n)
        lobes.append(oval(cx, cy, r, max(r * 0.8, abs(b[1] - a[1]) * 0.75)))
    return union(*lobes), pts


def wave_spine(x0, y0, length, side, amp=1.4, period=9.0, drift=0.0):
    """Points down a wavy lock: from (x0, y0) for ``length``, swinging ``amp`` either way."""
    n = max(3, int(length / (period / 2)) + 1)
    return [(x0 + side * (math.sin(k * math.pi + 0.6) * amp) + drift * k / (n - 1), y0 + length * k / (n - 1)) for k in range(n)]


def wavy(F, x0, y0, length, side, width, amp=1.1, drift=0.0, dx=0.0):
    pts = [(x + dx, y + F.hy) for x, y in wave_spine(x0, y0, length, side, amp, drift=drift)]
    widths = [width * (0.9 + 0.15 * math.sin(k)) for k in range(len(pts) - 1)] + [0.3]
    return strip(pts, widths, steps=6), pts


def wave_strands(img, pts, m, width):
    """Strand lines that follow a wavy lock: a parting each side of its middle, a sheen on the lit side."""
    for off, col, w in ((width * 0.12, HAIR[1], 2), (-width * 0.18, HAIR[4], 2)):
        curve(img, [(x + off, y) for x, y in pts[:-1]], col, w, m)


def plait(img, spine, width, n, clip=None, F=None):
    """A Dutch braid: a smooth band (gently scalloped at the crossings) along a spine, with the
    strands crossing in chevrons inside it, each with a shadow line and a sheen above it."""
    widths = [width * 0.55] + [width] * (len(spine) - 2) + [width * 0.7]     # tucked in at the start, narrowing into the tie
    band = union(strip(spine, widths, steps=8), ellipse([spine[0][0] - width * 0.3, spine[0][1] - width * 0.6, spine[0][0] + width * 0.3, spine[0][1] + width * 0.2]))
    pts = catmull([P(*q) for q in spine], 10)
    total = sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pts, pts[1:]))

    def at(dist):
        for a, b in zip(pts, pts[1:]):
            seg = math.hypot(b[0] - a[0], b[1] - a[1])
            if dist <= seg and seg > 0:
                t = dist / seg
                return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t), ((b[0] - a[0]) / seg, (b[1] - a[1]) / seg)
            dist -= seg
        a, b = pts[-2], pts[-1]
        seg = math.hypot(b[0] - a[0], b[1] - a[1]) or 1
        return b, ((b[0] - a[0]) / seg, (b[1] - a[1]) / seg)
    half = width * K / 2
    for k in range(n):                                                  # scallops where strands bulge out
        (cx, cy), (dx, dy) = at(total * (k + 0.5) / n)
        side = 1 if k % 2 else -1
        band = union(band, oval(cx - dy * half * 0.55 * side, cy + dx * half * 0.55 * side, half * 0.62, half * 0.62))
    if clip is not None:
        band = clip(band)
    part(img, band, HAIR, HAIR_LINE, radius=0.7, bias=0.1)
    for k in range(1, n):                                               # the crossings: a V pointing down the braid
        (cx, cy), (dx, dy) = at(total * k / n)
        nx, ny = -dy, dx
        tip = (cx + dx * half * 0.7, cy + dy * half * 0.7)
        left, right = (cx + nx * half * 0.85, cy + ny * half * 0.85), (cx - nx * half * 0.85, cy - ny * half * 0.85)
        curve(img, [left, tip, right], HAIR[1], 2, band, design=False)
        sheen = [(x - dx * 3, y - dy * 3) for x, y in (lerp(left, tip, 0.3), lerp(tip, right, 0.7))]
        curve(img, sheen, HAIR[4], 2, band, design=False)
    return band


def wave_lock(F, x0, y0, length, side, width, amp=1.2, drift=0.0, dx=0.0, curl=1, period=9.0):
    """A wavy lock that stays full, then tapers over its lower half into a tip that curls toward
    ``curl`` (-1 left, 1 right): loose waves that end softly instead of in a blunt tube."""
    pts = [(x + dx, y + F.hy) for x, y in wave_spine(x0, y0, length, side, amp, period=period, drift=drift)]
    lx, ly = pts[-1]
    pts.append((lx + curl * 1.6, ly - 0.2))
    n = len(pts)
    widths = []
    for k in range(n):
        f = k / (n - 1)
        taper = 1.0 if f < 0.5 else ((1 - f) / 0.5) ** 0.8
        widths.append(max(0.3, width * taper * (0.94 + 0.08 * math.sin(k * 1.7))))
    return strip(pts, widths, steps=8), pts, width


def wave_lines(img, locks, m):
    """Strands flowing down wavy locks: a shadow line each side of the middle, a sheen between."""
    for _, pts, w in locks:
        body = pts[:-1]
        curve(img, [(x + w * 0.2, y) for x, y in body], HAIR[1], 2 if w >= 5 else 1, m)
        curve(img, [(x - w * 0.24, y) for x, y in body[:max(2, len(body) * 2 // 3)]], HAIR[4], 1, m)
        if w >= 5:
            curve(img, [(x - w * 0.02, y + 0.4) for x, y in body[1:]], HAIR[2], 1, m)


def dutch_plait(img, spine, width, n, clip=None, tie=True, line=HAIR_LINE, soft=False):
    """A Dutch braid along a spine (design points): ``n`` rows of woven chevrons, each row a strand
    from either edge meeting the other in a V that points along the spine, the row nearer the
    start lying over the next one's top. Each strand is lined and shaded on its own over a
    shadowed core, and tucks into shadow where it meets its partner. Returns the braid's mask."""
    pts = catmull([P(*q) for q in spine], 10)
    segs = [math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(pts, pts[1:])]
    total = sum(segs)
    unp = lambda q: (q[0] / K - (K - 1) / 2 / K, q[1] / K - (K - 1) / 2 / K)

    def at(dist):
        for (a, b), seg in zip(zip(pts, pts[1:]), segs):
            if dist <= seg and seg > 0:
                t = dist / seg
                return unp((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)), ((b[0] - a[0]) / seg, (b[1] - a[1]) / seg)
            dist -= seg
        a, b = pts[-2], pts[-1]
        seg = segs[-1] or 1
        return unp(b), ((b[0] - a[0]) / seg, (b[1] - a[1]) / seg)
    core = strip(spine, [width * 0.55] + [width * 0.85] * (len(spine) - 2) + [width * 0.55], steps=8)
    if clip is not None:
        core = clip(core)
    if soft:                                                            # lying on the hair: shaded into it, no outline
        shade(img, core, [HAIR[0], HAIR[1], HAIR[1], HAIR[2], HAIR[2]], radius=0.6)
    else:
        part(img, core, [HAIR[0], HAIR[1], HAIR[1], HAIR[2], HAIR[2]], line, radius=0.6)
    seg = total / K / n
    whole = core
    for r in reversed(range(n)):                                         # the far rows first: each nearer row lies over them
        (cx, cy), (dx, dy) = at(total * (r + 0.5) / n)
        nx, ny = -dy, dx
        h = width / 2 * (1 - 0.22 * r / n)
        for side in (-1, 1):
            a = (cx - dx * seg * 0.5 + nx * side * h * 0.62, cy - dy * seg * 0.5 + ny * side * h * 0.62)     # from the edge...
            mid = (cx - dx * seg * 0.05 + nx * side * h * 0.5, cy - dy * seg * 0.05 + ny * side * h * 0.5)
            b = (cx + dx * seg * 0.42 + nx * side * h * 0.1, cy + dy * seg * 0.42 + ny * side * h * 0.1)      # ...in to the V's point
            r_ = h * 0.46
            lobe = union(strip([a, mid, b], [h * 0.92, h * 0.98, h * 0.88], steps=6),                         # an even, rounded bar
                         ellipse([a[0] - r_, a[1] - r_, a[0] + r_, a[1] + r_]), ellipse([b[0] - r_, b[1] - r_, b[0] + r_, b[1] + r_]))
            if clip is not None:
                lobe = clip(lobe)
            if soft:                                                    # only where it tucks under the next: a mid-tone, not ink
                shade(img, lobe, HAIR, radius=0.6, bias=0.15)
                curve(img, [lerp(a, b, 0.55), b], HAIR[1], 2, lobe, design=False)
                curve(img, [b, (b[0] - nx * side * h * 0.3, b[1] - ny * side * h * 0.3)], HAIR[0], 1, lobe, design=False)
            else:
                part(img, lobe, HAIR, line, radius=0.6, bias=0.15, width=1)
            curve(img, [lerp(a, mid, 0.45), lerp(mid, b, 0.25)], HAIR[4], 1, minus(lobe, edge(lobe, 2)))   # the sheen across the strand
            whole = union(whole, lobe)
    if tie:
        (cx, cy), _ = at(total)
        part(img, ellipse([cx - 1.2, cy - 0.6, cx + 1.2, cy + 0.8]), [grey(70), grey(96), grey(120)], HAIR_LINE, radius=0.4, width=1)
    return whole


def braided_ponytail(F):
    """The Shieldmaiden's hair (art/reference/viking-girl-hairstyle.jpg): a high ponytail spilling
    over in soft waves, a Dutch braid up one side of the head into its tie with a thin braid
    hanging by that ear, and from a deep side parting, full waves swept over the brow and down the
    other shoulder. The loose hair is one mass, lined once, its waves tapering into curled tips;
    only the braids are drawn strand by strand."""
    front, back = blank(), blank()
    hy, t, fs = F.hy, F.t, F.fs * 0.5
    clip = lambda m: under_helm(m, F)
    tie_col = [grey(70), grey(96), grey(120)]

    def swept_locks(locks):
        """Locks (design points, a width) as wisps: masks and their spines for the strand lines."""
        out = []
        for pts, w in locks:
            n = len(pts)
            widths = [max(0.3, w * (1 - (k / (n - 1)) ** 2.2)) for k in range(n)]
            sh = [(x + fs, y + hy) for x, y in pts]
            out.append((strip(sh, widths, steps=8), sh, w))
        return out

    def fountain(x0, x1, top):
        """The ponytail's gathered crown above the tie, locks spilling over toward screen right."""
        puff = ellipse([x0, top + hy, x1, top + 8.4 + hy])
        locks = [wave_lock(F, x0 + 1.4, top + 0.4, 12, 1, 4.4, amp=0.8, drift=3.6, curl=1),
                 wave_lock(F, (x0 + x1) / 2, top - 0.6, 10, -1, 4.0, amp=0.7, drift=4.2, curl=1)]
        return union(puff, *[l[0] for l in locks]), locks

    if F.front:
        # behind the body: the ponytail falling down the viewer's right, waves behind the left shoulder
        puff, plocks = fountain(24.0 + t, 32.4 + t, 0.8)
        tail = wave_lock(F, 31.2 + t, 8, 32, 1, 6.4, amp=1.4, drift=1.2, curl=-1)
        tail2 = wave_lock(F, 28.6 + t, 12, 26, -1, 4.4, amp=1.2, drift=1.6, curl=1)
        behind = wave_lock(F, 10 + t * 0.3, 18, 22, 1, 4.6, amp=1.2, curl=-1)
        bm = union(puff, tail[0], tail2[0], behind[0], back_mass(F, 30))
        part(back, bm, HAIR, HAIR_LINE, radius=2.2, bias=-0.12)
        wave_lines(back, plocks + [tail, tail2, behind], bm)
        part(back, strip([(23.6 + t, 3.6 + hy), (25.6 + t, 6.0 + hy)], [1.2, 1.2], steps=2), tie_col, HAIR_LINE, radius=0.4, width=1)
        # the front: from the parting beside the braid (just off the middle), big locks rise to a
        # crest, fold over and fall past the far temple in waves that curl at their ends
        sweep_def = [  # root on the parting, crest, fold, fall, curled tip; width
            ([(23.2, 3.6), (18.6, 2.2), (12.6, 4.8), (9.0, 10.6), (7.6, 16.4), (8.8, 19.6)], 6.0),
            ([(22.8, 5.4), (17.6, 3.8), (11.6, 7.6), (9.2, 13.4), (8.6, 19.0), (6.8, 21.4)], 5.6),
            ([(22.4, 7.2), (17.8, 6.4), (12.6, 9.6), (10.4, 14.6), (10.6, 18.4), (12.2, 19.8)], 5.0),
            ([(22.0, 9.0), (18.4, 8.8), (14.4, 11.8), (12.8, 16.0), (13.6, 18.8)], 4.4),
            ([(21.6, 10.8), (18.8, 11.4), (16.2, 14.2), (15.4, 17.6), (16.6, 19.2)], 3.6),
        ]
        sweep = swept_locks(sweep_def)
        loose = swept_locks([([(20.6, 12.2), (18.6, 15.4), (18.0, 18.6), (19.2, 20.2)], 2.4),             # strands breaking loose
                             ([(19.6, 11.6), (15.4, 13.0), (12.6, 16.8), (11.6, 21.6), (12.8, 23.2)], 2.0),
                             ([(15.6, 4.0), (12.4, 2.8), (10.2, 4.2), (10.6, 5.6)], 1.0),                 # flyaways off the crest
                             ([(19.4, 2.8), (17.0, 1.6), (15.0, 2.4)], 0.9)])
        # waves over the viewer's left shoulder; on the braid's side, hair combed back from the temple
        waves = [wave_lock(F, 9.6, 16, 27, -1, 6.6, dx=t * 0.3, curl=-1), wave_lock(F, 12.6, 19, 18, 1, 3.6, amp=1.1, dx=t * 0.3, curl=1),
                 wave_lock(F, 7.8, 21, 19, 1, 3.8, amp=1.0, dx=t * 0.3, curl=-1), wave_lock(F, 6.6, 30, 7, -1, 1.2, amp=0.8, dx=t * 0.3, curl=-1),
                 wave_lock(F, 30.2 + t * 0.4, 18, 12, 1, 3.4, amp=0.9, curl=1)]
        brow_ = poly([(21.0 + fs, 11.6 + hy), (23.0 + fs, 8.4 + hy), (26.4 + fs, 10.0 + hy), (28.6 + fs, 14.4 + hy), (29.4 + fs, 20 + hy), (20.4 + fs, 20 + hy)])
        fm = clip(union(minus(crown(F), brow_), *[l[0] for l in sweep + loose + waves]))
        part(front, fm, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        for i, (_, pts, w) in enumerate(sweep):
            # a lit band along the crest where the lock lifts, a shadow where it folds over and down,
            # and broken strand lines (of varied length) running out into the curl
            curve(front, [(x - 0.2, y + 0.5) for x, y in (lerp(pts[0], pts[1], 0.3), pts[1], lerp(pts[1], pts[2], 0.5))], HAIR[4], 3 if i < 2 else 2, fm)
            curve(front, [(x + 1.0, y + 1.0) for x, y in (lerp(pts[1], pts[2], 0.5), pts[2], lerp(pts[2], pts[3], 0.55))], HAIR[0] if i < 2 else HAIR[1], 2, fm)
            a0, a1 = (0.15, 0.55, 0.3, 0.6, 0.2)[i], (0.8, 0.95, 0.7, 0.9, 0.85)[i]
            along = [lerp(pts[int(f * (len(pts) - 1))], pts[min(len(pts) - 1, int(f * (len(pts) - 1)) + 1)], f * (len(pts) - 1) % 1)
                     for f in [a0 + (a1 - a0) * k / 5 for k in range(6)]]
            curve(front, [(x + w * 0.18, y + 0.2) for x, y in along], HAIR[1], 1, fm)
            curve(front, [(x - w * 0.22, y) for x, y in along[:3]], HAIR[3], 1, fm)
        curl_marks(front, [(9.2, 19.0 + hy, 1.4), (7.4, 20.8 + hy, 1.2), (12.0, 19.2 + hy, 1.2), (13.2, 18.2 + hy, 1.0)], fm)
        for _, pts, w in loose:
            curve(front, [(x + 0.3, y + 0.4) for x, y in pts[1:-1]], HAIR[1], 1, fm)
        for k in range(3):                                              # combed back from the temple to the tie
            x = 26.6 + k * 1.6 + t
            curve(front, [(x + 1.6, 19.6 - k + hy), (x + 1.0, 12 + hy), (x - 0.8, 6.4 + hy), (25.4 + t, 3.6 + hy)], HAIR[1], 1, fm)
        wave_lines(front, waves, fm)
        halo(front, fm, 6 + hy, F.fs)
        # the braid from the brow back into the tie (it runs from the tie, so its chevrons point at
        # the brow), and the thin one hanging behind the ear
        spine = [(22.6, 5.0), (26.0, 7.0), (28.6, 11.6), (30.0, 17.4), (30.8, 23.0), (31.2, 28.6), (31.0, 34.6)]
        dutch_plait(front, [(x + fs, y + hy) for x, y in spine], 3.8, 9, clip=clip, soft=True)
        # its start fed by locks off the crown, so it rises out of the hair rather than starting in a blunt end
        feed = [hwisp(F, [(18.8, 3.2), (21.6, 4.2), (24.2, 6.4), (25.8, 8.6)], 2.8, fs, power=2.0),
                hwisp(F, [(20.0, 2.0), (23.0, 3.2), (25.6, 5.4), (27.0, 7.6)], 2.4, fs, power=2.0),
                hwisp(F, [(21.6, 6.6), (23.8, 7.2), (25.4, 9.4)], 1.8, fs, power=2.0)]
        fm2 = clip(union(*[l[0] for l in feed]))
        shade(front, fm2, HAIR, radius=0.8, bias=0.1)
        hstrands(front, feed, fm2, start=0.1)
        brows(front, F)
    else:
        # from behind: the braids are out of sight round the front; the ponytail, tied off the
        # middle (the front view's tie, seen from the other side), spills over and falls in waves
        # down the screen's left, over hair combed up into it and loose waves on the right
        tx, ty = 15.6 + t, 5.6                                           # the tie
        loose = wave_lock(F, 29.2 + t, 15, 24, 1, 4.8, amp=1.3, curl=1)
        loose2 = wave_lock(F, 10.4 + t, 17, 12, -1, 3.6, amp=1.0, curl=-1)
        nape = [wave_lock(F, x + t, 17, n_, s_, 6.4, amp=1.1, curl=s_) for x, s_, n_ in ((16.0, -1, 17), (20.8, 1, 19), (25.4, 1, 16))]
        head = clip(union(crown(F), ellipse([8.4 + t, 9 + hy, 31.6 + t, 26 + hy]), loose[0], loose2[0], *[l[0] for l in nape]))
        part(front, head, HAIR, HAIR_LINE, radius=2.2, bias=-0.05)
        for k in range(6):                                              # combed up to the tie
            x = 11.0 + k * 3.8 + t
            curve(front, [(x, 27 + hy), (x + (tx - x) * 0.35, 17 + hy), (tx + (x - tx) * 0.12, ty + 1.2 + hy)], HAIR[1], 2 if k % 2 else 1, head)
            curve(front, [(x - 1.2, 25 + hy), (x - 1.2 + (tx - x) * 0.32, 16 + hy), (x - 1.2 + (tx - x) * 0.6, 11 + hy)], HAIR[4], 1, head)
            curve(front, [(x + 1.4, 22 + hy), (x + 1.4 + (tx - x) * 0.3, 14.6 + hy)], HAIR[2], 1, head)
        wave_lines(front, [loose, loose2] + nape, head)
        halo(front, head, 8 + hy, 0)
        # the ponytail over it: its gathered crown above the tie, the tail down the screen's left
        puff = ellipse([tx - 6.2, ty - 4.8 + hy, tx + 2.0, ty + 3.4 + hy])
        plocks = [wave_lock(F, tx + 0.4, ty - 4.4, 12, -1, 4.4, amp=0.8, drift=-3.6, curl=-1),
                  wave_lock(F, tx - 2.6, ty - 5.2, 10, 1, 4.0, amp=0.7, drift=-4.2, curl=-1)]
        tail = wave_lock(F, tx - 5.6, ty + 2.4, 33, -1, 6.6, amp=1.4, drift=-0.8, curl=1)
        tail2 = wave_lock(F, tx - 2.2, ty + 4.0, 27, 1, 4.6, amp=1.2, drift=-1.4, curl=-1)
        pm = union(puff, tail[0], tail2[0], *[l[0] for l in plocks])
        part(front, pm, HAIR, HAIR_LINE, radius=2.0, bias=0.0)
        wave_lines(front, [tail, tail2] + plocks, pm)
        part(front, strip([(tx - 1.4, ty + hy), (tx + 1.0, ty + 0.8 + hy)], [1.3, 1.3], steps=2), tie_col, HAIR_LINE, radius=0.4, width=1)
    return front, back


def tousled(F):
    """The Wanderer's mop (art/reference/chibi-therion.webp): one soft, full mass of hair, a gently
    lumpy crown with a cowlick, that only breaks into wisps at its ends: flicks curling up over
    the ears, short wisps over one brow, a long sweep down over the other eye. The locks are
    merged and lined as one shape; strand lines inside suggest them without cutting them apart."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)

    def wisp(pts, w, dx=0.0):
        """A lock tapering smoothly from ``w`` to a fine point (design points, shifted)."""
        n = len(pts)
        widths = [max(0.25, w * (1 - (k / (n - 1)) ** 1.6)) for k in range(n)]
        shifted = [(x + dx, y + hy) for x, y in pts]
        return strip(shifted, widths, steps=8), shifted

    def strands(img, locks, m, start=0.35):
        """Flowing lines down each lock's lower half (darker), a sheen up its root (lighter)."""
        for lm, pts, w in locks:
            if len(pts) < 3:
                continue
            k0 = max(1, int(len(pts) * start))
            off = 0.22 * w
            curve(img, [(x + off, y + 0.3) for x, y in pts[k0:-1]], HAIR[1], 2 if w >= 4.4 else 1, m)
            if w >= 4:
                curve(img, [(x - off * 0.9, y - 0.2) for x, y in pts[:k0 + 1]], HAIR[4], 1, m)

    def side_wisps(dx, far=0.7):
        """Flicks over the ears, curling up at their tips, and a stray strand off the lower one."""
        out = []
        for s in (-1, 1):
            k = far if (t and s == 1) else 1.0
            sc = lambda x: 20 + s * (10 + ((20 - x) - 10) * k if (20 - x) > 10 else (20 - x))
            out.append(([(sc(11.0), 17.0), (sc(7.8), 18.6), (sc(5.2), 18.2), (sc(3.8), 16.8)], 4.2))
            out.append(([(sc(10.8), 19.4), (sc(7.6), 21.8), (sc(4.8), 22.6), (sc(3.2), 21.8)], 4.2))
            out.append(([(sc(10.8), 22.0), (sc(8.8), 25.4), (sc(7.6), 28.4), (sc(7.6), 30.6)], 3.6))
            out.append(([(sc(8.6), 23.4), (sc(5.8), 25.2), (sc(4.0), 25.0)], 1.0))
        return [wisp(p_, w, dx) + (w,) for p_, w in out]

    def crown_mass(dx, low=17):
        """The top of the head: the skull's dome with three soft lumps, inside the helm's outline."""
        top = union(crown(F), ellipse([10.6 + dx, 3.6 + hy, 20.4 + dx, 13 + hy]), ellipse([16.4 + dx, 2.2 + hy, 26.6 + dx, 11.6 + hy]),
                    ellipse([22.6 + dx, 3.8 + hy, 31.0 + dx, 14 + hy]))
        cow = wisp([(18.6, 7.0), (19.4, 3.6), (21.4, 2.2), (23.4, 2.6), (24.2, 4.2)], 1.8, dx)
        return top, cow

    def lumps(img, dx, m):
        """Where the crown's lumps meet: soft partings running down from the top."""
        for pts in (((15.4, 4.6), (14.4, 7.6), (13.6, 10.8)), ((21.6, 3.0), (21.0, 6.4), (20.0, 9.0)), ((27.2, 5.4), (27.8, 8.6), (28.2, 11.4))):
            curve(img, [(x + dx, y + hy) for x, y in pts], HAIR[1], 1, m)

    if F.front:
        fs = F.fs * 0.5
        # behind the body: the hair at the nape, its ends in a few wisps
        nape = [wisp([(x, 22), (x + s * 1.2, 27), (x + s * 2.2, 31.4)], 4.6, t)[0] for x, s in ((10.8, -1), (13.8, -1), (26.2, 1), (29.2, 1))]
        bm = clip(union(back_mass(F, 30), *nape))
        part(back, bm, HAIR, HAIR_LINE, radius=2.2, bias=-0.12)
        top, cow = crown_mass(fs * 0.6)
        locks = [wisp(p_, w, fs) + (w,) for p_, w in (
            ([(12.4, 9.6), (10.2, 16.4), (9.8, 23.4), (10.8, 28.0), (12.0, 29.6)], 4.8),               # framing the face
            ([(27.6, 9.6), (30.0, 15.6), (30.6, 22.4), (29.6, 28.4), (28.4, 30.0)], 4.6),
            ([(16.0, 9.0), (14.2, 14.0), (12.6, 18.4), (12.0, 20.4)], 4.6),                            # wisps over the left brow
            ([(17.6, 9.4), (16.8, 14.4), (15.6, 19.6), (15.4, 21.2)], 4.0),
            ([(19.2, 10.0), (19.4, 15.0), (18.6, 19.4), (18.8, 20.6)], 3.2),
            ([(17.0, 8.0), (21.2, 12.2), (23.4, 18.2), (24.0, 24.4), (23.4, 27.4)], 6.6),              # the sweep over the right eye
            ([(16.4, 7.0), (22.4, 10.0), (26.6, 15.2), (28.4, 21.4), (27.8, 26.8), (26.4, 29.2)], 7.8),
            ([(18.6, 8.0), (23.4, 12.4), (25.6, 18.4), (26.0, 24.2), (25.2, 28.2)], 6.4),
            ([(25.8, 11.6), (28.6, 16.8), (29.4, 22.8), (28.8, 27.4), (27.6, 29.6)], 4.8),             # under it, over the temple
            ([(24.6, 25.0), (24.8, 28.2), (23.6, 30.2)], 1.0),                                         # strays off its tip
        )]
        sides = side_wisps(fs * 0.4)
        m = clip(union(top, cow[0], *[l[0] for l in locks + sides]))
        part(front, m, HAIR, HAIR_LINE, radius=2.0, bias=0.05)
        lumps(front, fs * 0.6, m)
        strands(front, locks + sides, m)
        curve(front, cow[1][1:-1], HAIR[1], 1, m)
        halo(front, m, 4 + hy, F.fs)
        brows(front, F, only=(0,))                                      # the other is under the sweep
    else:
        dx = t
        top, cow = crown_mass(dx)
        nape = [wisp([(x, 18), (x + s * 0.8, 24), (x + s * 1.8, y), (x + s * 2.6, y + 1.2)], 5.6, dx) + (5.6,)
                for x, s, y in ((10.6, -1, 27.4), (14.8, -1, 29.4), (19.0, -1, 28.4), (22.6, 1, 29.8), (26.4, 1, 28.2), (29.6, 1, 26.8))]
        sides = side_wisps(dx * 0.4)
        mass = union(top, ellipse([8.6 + dx, 8 + hy, 31.4 + dx, 25 + hy]), cow[0], *[l[0] for l in nape + sides])
        m = clip(mass)
        part(front, m, HAIR, HAIR_LINE, radius=2.2, bias=-0.05)
        for x in (12.0, 16.4, 20.8, 25.0, 28.8):                        # combed down from the whorl into the ends
            s = -1 if x < 20 else 1
            curve(front, [(20.4 + dx + s * 0.8, 8.4 + hy), (x + dx - s * 1.6, 15 + hy), (x + dx - s * 0.4, 21 + hy), (x + dx + s * 0.6, 26 + hy)], HAIR[1], 1, m)
        strands(front, nape + sides, m, start=0.5)
        curve(front, cow[1][1:-1], HAIR[1], 1, m)
        halo(front, m, 4 + hy, 0)
    return front, back


# ---------------------------------------------------------------- hair, lock by lock
# Every style below is built as the swept one is: a soft crown, then locks (tapered strips along a
# flowing spine, each its own width and length) merged into one mass and lined once, with strand
# lines down each lock (a shadow along its fold, a sheen at its root) and a few strays breaking
# loose. Design points are on the 40x60 frame; ``dx`` shifts with a turned head.

def hwisp(F, pts, w, dx=0.0, power=1.6):
    """A lock tapering from ``w`` to a fine point: (mask, spine, width)."""
    n = len(pts)
    widths = [max(0.25, w * (1 - (k / (n - 1)) ** power)) for k in range(n)]
    sh = [(x + dx, y + F.hy) for x, y in pts]
    return strip(sh, widths, steps=8), sh, w


def hstrands(img, locks, m, start=0.3):
    """Strand lines down each lock: a shadow along its fold, a sheen near its root, a mid line in a wide one."""
    for _, pts, w in locks:
        if len(pts) < 3:
            continue
        k0 = max(1, int(len(pts) * start))
        off = 0.22 * w
        curve(img, [(x + off, y + 0.3) for x, y in pts[k0:-1]], HAIR[1], 2 if w >= 4.4 else 1, m)
        if w >= 3.0:
            curve(img, [(x - off * 0.9, y - 0.2) for x, y in pts[:k0 + 1]], HAIR[4], 1, m)
        if w >= 5.2:
            curve(img, [(x - off * 0.1, y + 0.4) for x, y in pts[1:-1]], HAIR[2], 1, m)


def mirror(pts, s, t=0.0):
    """Design points mirrored to side ``s`` (-1: as given, the screen's left; 1: the right)."""
    return [((x if s < 0 else 40 - x) + t, y) for x, y in pts]


def soft_crown(F, dx, lumps=((11.0, 4.6, 20.0, 13.4), (16.8, 3.0, 25.0, 11.6), (22.2, 4.6, 30.0, 13.4))):
    """The top of the head, its dome eased by a few soft lumps."""
    return union(crown(F), *[ellipse([a + dx, b + F.hy, c + dx, d + F.hy]) for a, b, c, d in lumps])


def combed(img, F, m, roots, to, dx=0.0, bend=0.35, every=2, reach=0.78):
    """Fine strand lines combed from ``roots`` toward ``to`` (a parting, a tie), shadow and sheen in
    turn, each fading out short of it (they'd meet in a star otherwise), no two quite alike."""
    tx, ty = to
    for k, (x, y) in enumerate(roots):
        r = reach - 0.08 * (k % 3)
        end = (x + (tx - x) * r, y + (ty - y) * r)
        mid = (x + (end[0] - x) * bend + (0.4 if k % 2 else -0.3), y + (end[1] - y) * 0.5)
        pts = [(px + dx, py + F.hy) for px, py in ((x, y), mid, end)]
        curve(img, pts, HAIR[1] if k % every == 0 else HAIR[4], 1, m)


def ties(img, F, spots):
    for tx, ty in spots:
        part(img, ellipse([tx - 1.3, ty - 0.7 + F.hy, tx + 1.3, ty + 0.7 + F.hy]), [grey(70), grey(96), grey(120)], HAIR_LINE, radius=0.4, width=1)


def style_short(F):
    """A neat crop: a soft crown, a fringe of short locks falling unevenly to the brows, the sides
    tucked over the ears, the nape in a few short points."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)
    if F.front:
        fs = F.fs * 0.5
        bm = clip(back_mass(F, 26))
        part(back, bm, HAIR, HAIR_LINE, radius=2.2, bias=-0.15)
        top = soft_crown(F, fs * 0.6, lumps=((11.4, 5.6, 20.2, 13.4), (17.4, 4.6, 24.8, 11.8), (22.0, 5.6, 29.6, 13.4)))
        fringe_ = [hwisp(F, [(rx, 8.4 + (k % 2) * 0.6), (rx + 0.6, 12.4), (rx + 1.4 + (k % 3) * 0.2, 16.4 + d)], 3.8 - (k % 2) * 0.5, fs)
                   for k, (rx, d) in enumerate(((11.6, 0.6), (14.2, -0.4), (16.8, 1.0), (19.4, 0.0), (22.0, 0.8), (24.6, -0.6), (27.2, 0.2)))]
        sides = []
        for s in (-1, 1):
            sides.append(hwisp(F, mirror([(11.4, 11.6), (9.8, 16.6), (9.4, 21.2), (9.8, 22.6)], s, t * (0.6 if s > 0 else 0)), 4.0))
            sides.append(hwisp(F, mirror([(12.6, 14.0), (11.2, 18.0), (11.0, 20.6)], s, t * (0.6 if s > 0 else 0)), 2.6))
        strays = [hwisp(F, [(18.2, 9.4), (17.2, 13.6), (16.4, 17.4)], 1.0, fs), hwisp(F, [(25.6, 8.6), (27.4, 6.8), (28.6, 7.2)], 0.9, fs)]
        m = clip(union(top, *[l[0] for l in fringe_ + sides + strays]))
        part(front, m, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        hstrands(front, fringe_ + sides, m, start=0.45)
        for x in (15.0, 20.6, 26.0):                                    # soft partings between the crown's lumps
            curve(front, [(x + fs * 0.6, 4.4 + hy), (x - 0.4 + fs * 0.6, 7.6 + hy), (x - 0.8 + fs * 0.6, 9.6 + hy)], HAIR[1], 1, m)
        halo(front, m, 4.6 + hy, F.fs)
        brows(front, F)
    else:
        nape = [hwisp(F, [(x, 18), (x + s * 0.6, 23), (x + s * 1.2, y)], 4.6, t)
                for x, s, y in ((10.8, -1, 25.4), (14.6, -1, 27.2), (18.4, -1, 26.6), (21.8, 1, 27.6), (25.6, 1, 26.8), (29.2, 1, 25.2))]
        m = clip(union(soft_crown(F, t, lumps=((11.4, 5.6, 20.2, 13.4), (17.4, 4.6, 24.8, 11.8), (22.0, 5.6, 29.6, 13.4))),
                       ellipse([8.6 + t, 8 + hy, 31.4 + t, 23.6 + hy]), *[l[0] for l in nape]))
        part(front, m, HAIR, HAIR_LINE, radius=2.2, bias=-0.05)
        for x in (12.0, 16.2, 20.4, 24.6, 28.6):                        # combed down from the whorl
            s = -1 if x < 20 else 1
            curve(front, [(20.4 + t + s * 0.6, 8.4 + hy), (x + t - s * 1.4, 14.6 + hy), (x + t - s * 0.2, 20.6 + hy), (x + t + s * 0.6, 24.6 + hy)], HAIR[1], 1, m)
        hstrands(front, nape, m, start=0.5)
        curve(front, [(19.6 + t, 7.6 + hy), (21.0 + t, 6.6 + hy), (22.2 + t, 7.8 + hy), (21.0 + t, 9.0 + hy)], HAIR[1], 1, m)   # the whorl
        halo(front, m, 4.6 + hy, 0)
    return front, back


def long_fall(F, length, dx=0.0, curly=False):
    """The long hair down a back, seen from behind: waves from the crown, ending at different lengths."""
    locks = []
    for k, (x, s, extra, w) in enumerate(((9.6, -1, -3.0, 4.8), (12.6, 1, 0.4, 5.6), (15.8, -1, 1.6, 5.8), (19.2, 1, 2.4, 6.0),
                                          (22.6, -1, 1.8, 5.8), (25.8, 1, 0.6, 5.6), (28.8, -1, -1.6, 5.0), (31.0, 1, -4.0, 4.0))):
        locks.append(wave_lock(F, x + dx, 12 + (abs(x - 20) * 0.25), length - 12 + extra, s, w, amp=0.9, period=11.0, curl=-s if k % 2 else s))
    return locks


def style_long(F):
    """Long and loose, parted in the middle: curtains falling from the parting to frame the face,
    waves past the shoulders on either side, a few fine strands loose over the brow; from behind,
    a fall of waves from the whorl, its ends at different lengths."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)
    if F.front:
        fs = F.fs * 0.5
        # behind the body: the length of it, in waves behind each shoulder
        behind = []
        for s in (-1, 1):
            X = lambda x: (x if s < 0 else 40 - x) + t
            behind += [wave_lock(F, X(9.6), 16, 30, s, 6.2, amp=0.9, period=11.0, curl=-s), wave_lock(F, X(12.4), 20, 24, -s, 5.0, amp=0.8, period=10.0, curl=s)]
        bm = clip(union(back_mass(F, 34), *[l[0] for l in behind]))
        part(back, bm, HAIR, HAIR_LINE, radius=2.2, bias=-0.14)
        wave_lines(back, behind, bm)
        top = soft_crown(F, fs * 0.6, lumps=((10.6, 4.8, 19.6, 13.0), (20.4, 4.8, 29.4, 13.0)))
        curtains = []
        for s in (-1, 1):
            curtains.append(hwisp(F, mirror([(19.6, 5.4), (15.6, 8.4), (12.4, 12.8), (10.6, 18.6), (10.0, 24.0)], s), 5.4, fs, power=2.2))
            curtains.append(hwisp(F, mirror([(19.2, 7.2), (16.2, 10.6), (14.0, 14.4), (13.0, 17.6)], s), 3.2, fs))
        falls = []
        for s in (-1, 1):
            X = lambda x: (x if s < 0 else 40 - x) + (t * 0.6 if s > 0 else 0)
            falls += [wave_lock(F, X(10.0), 13, 29, s, 5.6, amp=0.9, period=11.0, curl=-s),
                      wave_lock(F, X(11.8), 17, 24, -s, 3.8, amp=1.0, period=9.0, curl=s),
                      wave_lock(F, X(8.6), 20, 21, s, 3.0, amp=0.8, period=10.0, curl=-s)]
        strays = [hwisp(F, [(19.0, 8.0), (17.8, 12.6), (17.6, 16.8)], 1.0, fs), hwisp(F, [(21.2, 8.2), (22.6, 12.8), (22.4, 15.6)], 0.9, fs)]
        m = clip(union(top, *[l[0] for l in curtains + strays + falls]))
        part(front, m, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        curve(front, [(20 + fs, 4.0 + hy), (20.2 + fs, 7.4 + hy), (20.0 + fs, 9.0 + hy)], HAIR_LINE, 1, m)      # the parting
        hstrands(front, curtains, m, start=0.25)
        wave_lines(front, falls, m)
        halo(front, m, 4.4 + hy, F.fs)
        brows(front, F)
    else:
        falls = long_fall(F, 44, t)
        m = clip(union(soft_crown(F, t, lumps=((10.6, 4.8, 19.6, 13.0), (20.4, 4.8, 29.4, 13.0))), ellipse([8.4 + t, 8 + hy, 31.6 + t, 30 + hy]), *[l[0] for l in falls]))
        part(front, m, HAIR, HAIR_LINE, radius=2.4, bias=-0.05)
        curve(front, [(20 + t, 4.2 + hy), (20 + t, 9.0 + hy)], HAIR_LINE, 1, m)
        combed(front, F, m, [(x, 12) for x in (11.0, 14.6, 25.4, 29.0)], (20, 5.4), dx=t, bend=0.2)
        wave_lines(front, falls, m)
        halo(front, m, 4.4 + hy, 0)
    return front, back


def style_ponytail(F):
    """Drawn back into a ponytail at the back of the head: the crown combed smooth toward the tie,
    short locks loose at the temples, the tail swinging behind one shoulder; from behind, the tie
    and the tail falling in waves from it."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)
    if F.front:
        fs = F.fs * 0.5
        tail = wave_lock(F, 29.0 + t, 11, 26, 1, 5.2, amp=1.1, drift=1.6, curl=-1)
        tail2 = wave_lock(F, 27.4 + t, 14, 20, -1, 3.4, amp=1.0, drift=1.2, curl=1)
        bm = clip(union(back_mass(F, 26), tail[0], tail2[0]))
        part(back, bm, HAIR, HAIR_LINE, radius=2.0, bias=-0.1)
        wave_lines(back, [tail, tail2], bm)
        top = soft_crown(F, fs * 0.6, lumps=((11.2, 4.6, 28.8, 12.6),))
        fringe_ = [hwisp(F, [(13.0, 8.4), (12.2, 11.6), (12.0, 15.4), (12.8, 17.2)], 3.4, fs),       # swept to the side, behind the ears
                   hwisp(F, [(15.6, 7.6), (15.8, 11.0), (14.6, 14.8), (13.8, 16.6)], 3.0, fs),
                   hwisp(F, [(26.6, 8.0), (27.6, 11.4), (27.8, 15.0), (27.0, 16.8)], 3.2, fs)]
        temples = [hwisp(F, mirror([(11.6, 12.6), (10.8, 18.0), (11.0, 23.2), (11.8, 25.4)], s, t * (0.6 if s > 0 else 0)), 1.6) for s in (-1, 1)]
        sides = [hwisp(F, mirror([(10.6, 11.4), (9.8, 15.4), (10.6, 18.4)], s, t * (0.6 if s > 0 else 0)), 3.6, power=1.2) for s in (-1, 1)]
        m = clip(union(top, *[l[0] for l in fringe_ + temples + sides]))
        part(front, m, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        combed(front, F, m, [(x, 11.4) for x in (16.4, 19.0, 22.2, 24.8)], (20.6, 3.0), dx=fs * 0.6, bend=0.1, reach=0.6)
        hstrands(front, fringe_ + temples, m, start=0.4)
        halo(front, m, 4.4 + hy, F.fs)
        brows(front, F)
    else:
        tx, ty = 20 + t, 14.6
        m = clip(union(soft_crown(F, t, lumps=((11.2, 4.6, 28.8, 12.6),)), ellipse([8.6 + t, 8 + hy, 31.4 + t, 24.6 + hy]),
                       *[hwisp(F, [(x, 19), (x + s * 0.4, 23), (x + s * 0.8, 25.6)], 3.0, t)[0] for x, s in ((10.4, -1), (29.6, 1))]))
        part(front, m, HAIR, HAIR_LINE, radius=2.2, bias=-0.05)
        combed(front, F, m, [(x, y) for x, y in ((10.6, 22), (13.4, 24), (16.6, 25), (19.6, 25.4), (23.0, 25), (26.4, 24), (29.4, 22),
                                                 (11.2, 8), (15.0, 4.8), (20.0, 4.0), (25.0, 4.8), (28.8, 8))], (tx - t, ty), dx=t, bend=0.25)
        locks = [wave_lock(F, tx - 1.2, ty + 0.6, 26, -1, 5.6, amp=1.2, drift=-0.6, curl=1),
                 wave_lock(F, tx + 1.0, ty + 1.2, 23, 1, 4.6, amp=1.1, drift=0.8, curl=-1),
                 wave_lock(F, tx, ty + 2.0, 17, -1, 2.6, amp=0.9, drift=1.6, curl=1)]
        pm = clip(union(ellipse([tx - 3.2, ty - 1.6 + hy, tx + 3.2, ty + 3.4 + hy]), *[l[0] for l in locks]))
        part(front, pm, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        wave_lines(front, locks, pm)
        ties(front, F, [(tx, ty)])
        halo(front, m, 4.4 + hy, 0)
    return front, back


def style_pigtails(F):
    """Two braids: parted in the middle, combed smooth to either side behind the ears, and a braid
    from each, hanging in front of the shoulders to a tie and a tuft."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)
    spines = {s: mirror([(10.6, 20.0), (10.0, 25.0), (10.2, 30.0), (10.8, 35.0), (11.4, 38.6)], s, t * (0.6 if s > 0 else 0)) for s in (-1, 1)}
    if F.front:
        fs = F.fs * 0.5
        part(back, clip(back_mass(F, 24)), HAIR, HAIR_LINE, radius=2.2, bias=-0.15)
        top = soft_crown(F, fs * 0.6, lumps=((10.8, 4.8, 19.8, 13.0), (20.2, 4.8, 29.2, 13.0)))
        sides = [hwisp(F, mirror([(19.4, 5.6), (14.6, 8.4), (11.4, 13.4), (10.2, 18.2), (10.6, 21.0)], s, t * (0.6 if s > 0 else 0)), 5.0, power=1.4) for s in (-1, 1)]
        bangs = [hwisp(F, mirror([(19.0, 6.6), (17.4, 10.4), (16.6, 14.2), (17.0, 15.8)], s), 2.6, fs) for s in (-1, 1)]
        edge_ = [hwisp(F, mirror([(rx, 11.6), (rx - 0.4, 13.8), (rx - 0.8, 15.4)], s), 2.6, fs) for s in (-1, 1) for rx in (12.6, 15.0)]
        for s in (-1, 1):                                               # the braids first: the hair lies over their roots
            dutch_plait(front, [(x, y + hy) for x, y in spines[s]], 3.6, 8, clip=clip)
            tip = spines[s][-1]
            tuft = hwisp(F, [(tip[0], tip[1] + 0.6), (tip[0] - s * 0.4, tip[1] + 2.6), (tip[0] + s * 0.2, tip[1] + 4.2)], 2.6)
            part(front, clip(tuft[0]), HAIR, HAIR_LINE, radius=0.6, bias=0.05)
        gather = [hwisp(F, mirror([(11.6, 15.6), (10.8, 20.0), (10.6, 23.6)], s, t * (0.6 if s > 0 else 0)), 3.4, power=2.2) for s in (-1, 1)]
        m = clip(union(top, *[l[0] for l in sides + bangs + edge_ + gather]))
        part(front, m, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        curve(front, [(20 + fs, 4.0 + hy), (20 + fs, 7.6 + hy)], HAIR_LINE, 1, m)
        for s in (-1, 1):                                               # combed from the parting round behind the ears
            for k in range(3):
                pts = mirror([(19.0 - k * 0.4, 5.0 + k * 2.0), (14.6 - k * 0.6, 8.6 + k * 1.8), (11.4 - k * 0.2, 14.0 + k * 1.4), (10.6, 19.0)], s, fs)
                curve(front, [(x, y + hy) for x, y in pts], HAIR[1] if k != 1 else HAIR[4], 1, m)
        hstrands(front, bangs + edge_ + gather, m, start=0.4)
        halo(front, m, 4.4 + hy, F.fs)
        brows(front, F)
    else:
        for s in (-1, 1):                                               # the braids under the hair they grow from
            spine = [(x + (0.6 if s < 0 else -0.6), y) for x, y in spines[s]]
            dutch_plait(front, [(x, y + hy) for x, y in spine], 3.4, 8, clip=clip)
        gather = [hwisp(F, mirror([(12.4, 17.0), (11.2, 21.0), (11.2, 24.4)], s, t), 3.6, power=2.2) for s in (-1, 1)]
        m = clip(union(soft_crown(F, t, lumps=((10.8, 4.8, 19.8, 13.0), (20.2, 4.8, 29.2, 13.0))), ellipse([8.6 + t, 8 + hy, 31.4 + t, 24.0 + hy]),
                       *[l[0] for l in gather]))
        part(front, m, HAIR, HAIR_LINE, radius=2.2, bias=-0.05)
        curve(front, [(20 + t, 4.2 + hy), (20 + t, 12.0 + hy), (20.2 + t, 23.6 + hy)], HAIR_LINE, 1, m)           # parted down the back
        for s in (-1, 1):
            for k in range(4):
                pts = mirror([(19.2, 6.0 + k * 4.4), (15.0, 9.0 + k * 3.8), (11.6, 15.0 + k * 2.4), (10.4, 21.0)], s, t)
                curve(front, [(x, y + hy) for x, y in pts], HAIR[1] if k % 2 == 0 else HAIR[4], 1, m)
        halo(front, m, 4.4 + hy, 0)
    return front, back


def jitter(k, salt=0.0):
    """A fixed wobble in -1..1 for the k-th of something: no randomness, so a rerun draws the same."""
    return math.sin(k * 12.9898 + salt * 78.233) * 43758.5453 % 2 - 1


def curl_hooks(img, spots, m):
    """Each curl a C turning on itself: a dark hook round its lower side, a light one round its top,
    and the shadowed hollow inside it, so the coils read as springs, not dents."""
    for k, (x, y, r) in enumerate(spots):
        cx, cy = P(x, y)
        rr = r * K
        turn = 40 * jitter(k, 9.0)                                         # each curl turns its own way
        hollow = mask(lambda d: d.ellipse([cx - rr * 0.42, cy - rr * 0.3, cx + rr * 0.42, cy + rr * 0.42], fill=255))
        fill(img, hollow, HAIR[1], m)
        rim = mask(lambda d: d.arc([cx - rr, cy - rr, cx + rr, cy + rr], 300 + turn, 160 + turn + 360, fill=255, width=3))
        fill(img, rim, HAIR[0], m)
        lit = mask(lambda d: d.arc([cx - rr * 0.8, cy - rr * 0.85, cx + rr * 0.8, cy + rr * 0.75], 190 + turn, 280 + turn, fill=255, width=2))
        fill(img, lit, HAIR[4], m)


def coils(img, F, m, area, n, rmin=1.0, rmax=2.0, salt=0.0):
    """Curls scattered over a region (x0, y0, x1, y1), each its own size and turn."""
    x0, y0, x1, y1 = area
    cols = max(1, int(round(math.sqrt(n * (x1 - x0) / max(1, y1 - y0)))))
    rows = max(1, (n + cols - 1) // cols)
    spots = []
    for k in range(n):
        cx = x0 + (k % cols + 0.5 + 0.35 * jitter(k, salt)) * (x1 - x0) / cols + (0.5 if (k // cols) % 2 else 0) * (x1 - x0) / cols * 0.5
        cy = y0 + (k // cols + 0.5 + 0.3 * jitter(k, salt + 1)) * (y1 - y0) / rows
        spots.append((cx, cy + F.hy, rmin + (rmax - rmin) * (0.5 + 0.5 * jitter(k, salt + 2))))
    curl_hooks(img, spots, m)


def bumpy(F, dx, pts, rmin=1.5, rmax=2.4, salt=0.0):
    """A curly outline: a tight bump every little way along it (twice as many as points given), each its own size."""
    dense = []
    for a, b in zip(pts, pts[1:]):
        dense += [a, lerp(a, b, 0.5)]
    dense.append(pts[-1])
    return union(*[ellipse([x + dx - r, y + F.hy - r, x + dx + r, y + F.hy + r])
                   for k, (x, y) in enumerate(dense) for r in [rmin + (rmax - rmin) * (0.5 + 0.5 * jitter(k, salt))]])


def ringlet(F, x0, y0, length, side, w, dx=0.0):
    """A corkscrew curl: tight, springy waves, tapering into a turned-up tip."""
    return wave_lock(F, x0 + dx, y0, length, side, w, amp=1.6, period=3.0, curl=-side)


def ringlet_turns(img, locks, m):
    """Down each ringlet, its turns: a small curl at every swing of the spring, smaller toward the tip."""
    for _, pts, w in locks:
        body = pts[:-1]
        for k, (x, y) in enumerate(body[::2]):
            f = k / max(1, len(body[::2]) - 1)
            curl_hooks(img, [(x, y, max(0.7, w * 0.42 * (1 - 0.45 * f)))], m)


CROWN_EDGE = [(9.4, 15.0), (9.8, 11.2), (11.4, 7.6), (14.0, 5.0), (17.2, 3.6), (20.4, 3.2), (23.6, 3.8), (26.4, 5.2), (28.8, 7.8), (30.4, 11.4), (30.8, 15.0)]


def style_curly(F, long):
    """Curls, short or long: a crown of soft bumps covered in coils of every size, ringlets falling
    over the brow and down the sides (to the jaw, or past the shoulders), the ends turned up."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)
    side_len = 24 if long else 10
    if F.front:
        fs = F.fs * 0.5
        behind = []
        if long:
            for s in (-1, 1):
                for k, (x, y) in enumerate(((9.0, 18), (11.6, 22), (13.8, 24))):
                    behind.append(ringlet(F, (x if s < 0 else 40 - x) + t, y, 22 - k * 2, s * (1 if k % 2 else -1), 4.2))
        bm = clip(union(bumpy(F, t, [(x, y) for x, y in CROWN_EDGE] + [(9.2, 20), (30.8, 20), (10.0, 25), (30.0, 25)], salt=3), *[l[0] for l in behind]))
        part(back, bm, HAIR, HAIR_LINE, radius=2.0, bias=-0.15)
        coils(back, F, bm, (8.6 + t, 17, 31.4 + t, 38 if long else 27), 26 if long else 14, salt=1)
        ringlet_turns(back, behind, bm)
        top = union(crown(F), bumpy(F, fs * 0.6, CROWN_EDGE, salt=0))
        fringe_ = [ringlet(F, x, 10.2 + (k % 2) * 0.8, 5.6 + (k % 3) * 0.9, (-1, 1)[k % 2], 2.8, fs)
                   for k, x in enumerate((12.0, 14.4, 16.8, 19.2, 21.6, 24.0, 26.4, 28.4))]
        sides = []
        for s in (-1, 1):
            X = lambda x: (x if s < 0 else 40 - x) + (t * 0.6 if s > 0 else 0)
            sides += [ringlet(F, X(10.0), 12, side_len, s, 3.6), ringlet(F, X(12.0), 15, side_len - 3, -s, 2.8),
                      ringlet(F, X(8.4), 17, side_len - 6, -s, 2.6)]
        m = clip(union(top, *[l[0] for l in fringe_ + sides]))
        part(front, m, HAIR, HAIR_LINE, radius=1.6, bias=0.05)
        coils(front, F, both(m, rect([0, 0, 40, 13.6 + hy])), (10.0 + fs, 3.8, 30.0 + fs, 12.8), 22, salt=4)
        ringlet_turns(front, fringe_ + sides, m)
        halo(front, m, 4.2 + hy, F.fs)
        brows(front, F)
    else:
        length = 44 if long else 28
        hang = [ringlet(F, x + t, length - 12 + (abs(x - 20) * 0.2), 10 + (k % 3) * 2, (-1, 1)[k % 2], 3.8) for k, x in enumerate((9.6, 12.4, 15.2, 18.0, 20.8, 23.6, 26.4, 29.0, 31.0))]
        edge_ = CROWN_EDGE + [(9.0, 19), (31.0, 19), (9.4, 24), (30.6, 24)] + ([(9.8, 30), (30.2, 30), (10.4, 36), (29.6, 36)] if long else [])
        m = clip(union(bumpy(F, t, edge_, salt=5), ellipse([9.0 + t, 6 + hy, 31.0 + t, length - 8 + hy]), *[l[0] for l in hang]))
        part(front, m, HAIR, HAIR_LINE, radius=2.2, bias=-0.05)
        coils(front, F, m, (9.4 + t, 4.4, 30.6 + t, length - 10), 48 if long else 28, salt=6)
        ringlet_turns(front, hang, m)
        halo(front, m, 4.2 + hy, 0)
    return front, back


# ---------------------------------------------------------------- curly (art/reference/curly-hair.webp, curly-hair-long.webp)

def spiral(img, x, y, r, turn, m, color=None, width=2, turns=1.3, F=None):
    """One curl as line-work: a loop winding in on itself from radius ``r``, its lit side caught."""
    pts = []
    n = int(turns * 14)
    for i in range(n + 1):
        f = i / n
        a = turn + f * turns * 2 * math.pi
        rr = r * (1 - 0.72 * f)
        pts.append((x + rr * math.cos(a), y + rr * 0.9 * math.sin(a)))
    curve(img, pts, color or HAIR[0], width, m)
    curve(img, [(px - 0.3, py - 0.3) for px, py in pts[n // 6:n // 2]], HAIR[4], 1, m)


def spirals(img, m, area, n, rmin=1.3, rmax=2.3, salt=0.0, hy=0.0):
    """Curls drawn as loops all over a region: each its own size and turn, a few of them looser and lighter."""
    x0, y0, x1, y1 = area
    cols = max(1, int(round(math.sqrt(n * (x1 - x0) / max(1, y1 - y0)))))
    rows = max(1, (n + cols - 1) // cols)
    for k in range(n):
        cx = x0 + (k % cols + 0.5 + 0.4 * jitter(k, salt)) * (x1 - x0) / cols + ((k // cols) % 2) * (x1 - x0) / cols * 0.5
        cy = y0 + (k // cols + 0.5 + 0.35 * jitter(k, salt + 1)) * (y1 - y0) / rows + hy
        r = rmin + (rmax - rmin) * (0.5 + 0.5 * jitter(k, salt + 2))
        loose = jitter(k, salt + 3) > 0.55
        spiral(img, cx, cy, r, math.pi * jitter(k, salt + 4), m, color=HAIR[1] if loose else HAIR[0], width=1 if loose else 2, turns=1.0 if loose else 1.4)


def scalloped(F, dx, outline, rmin=1.1, rmax=1.9, salt=0.0, step=1.6):
    """A curly silhouette: the outline's polygon, edged all round with small loops."""
    pts = []
    for a, b in zip(outline, outline[1:] + outline[:1]):
        n = max(1, int(math.dist(a, b) / step))
        pts += [lerp(a, b, i / n) for i in range(n)]
    loops = [ellipse([x + dx - r, y + F.hy - r, x + dx + r, y + F.hy + r]) for k, (x, y) in enumerate(pts)
             for r in [rmin + (rmax - rmin) * (0.5 + 0.5 * jitter(k, salt))]]
    return union(poly([(x + dx, y + F.hy) for x, y in outline]), *loops)


def under_volume(m, F, top=1.0):
    """For big hair: kept within a full dome over the head (not the skull's own outline), so it can
    billow out from the crown."""
    dome = ellipse([1.6 + F.t, top + F.hy, 38.4 + F.t, 48 + F.hy])
    return minus(m, minus(rect([0, 0, 40, 18 + F.hy]), dome))


# The bob's silhouette from the front: a dome, widening past the cheeks to the jaw, the ends at the shoulders.
BOB = [(20.0, 1.4), (24.8, 1.8), (28.8, 3.6), (31.8, 6.6), (33.8, 10.6), (35.0, 15.2), (35.8, 20.4), (35.8, 25.6), (34.8, 30.2),
       (32.6, 33.6), (29.4, 34.8), (26.2, 33.4), (13.8, 33.4), (10.6, 34.8), (7.4, 33.6), (5.2, 30.2), (4.2, 25.6), (4.2, 20.4),
       (5.0, 15.2), (6.2, 10.6), (8.2, 6.6), (11.2, 3.6), (15.2, 1.8)]


def clump(F, root, tip, w, dx=0.0, phase=0.0):
    """One curl clump: a short springy lock from ``root`` to ``tip`` (design points), wiggling tightly, its tip a spiral."""
    mid = lerp(root, tip, 0.5)
    bend = ((tip[1] - root[1]) * 0.12, -(tip[0] - root[0]) * 0.12)
    return s_path(F, [root, (mid[0] + bend[0], mid[1] + bend[1]), tip], w, amp=0.9, period=3.4, phase=phase, curl=1 if tip[0] >= root[0] else -1, dx=dx)


def paint_clumps(img, clumps, clip, bias=0.0):
    """Curl clumps painted back to front, each shaded and lined on its own, a sheen down it and a spiral at its tip."""
    for c_ in clumps:
        cm = clip(c_[0])
        part(img, cm, HAIR, HAIR_LINE, radius=1.0, bias=bias)
        pts, w = c_[1], c_[2]
        body = pts[:-2]
        curve(img, [(x - w * 0.22, y) for x, y in body[: max(2, len(body) * 2 // 3)]], HAIR[4], 1, cm)
        curve(img, [(x + w * 0.22, y) for x, y in body[len(body) // 3:]], HAIR[1], 1, cm)
        tx, ty = pts[-3]
        spiral(img, tx, ty, min(1.3, w * 0.34), 0.0, cm, width=1, turns=1.1)


def bob_clumps(F, dx, front):
    """The bob as curl clumps: radiating from the crown, hanging in tiers down the sides, the
    longest at the jaw; from the front, small ones over the brow. Back to front."""
    out = []
    k = 0
    def add(root, tip, w):
        nonlocal k
        out.append(clump(F, root, tip, w, dx, phase=k * 1.3))
        k += 1
    # the outermost tier first: down the sides to the jaw and the shoulders
    for s in (-1, 1):
        X = lambda x: x if s < 0 else 40 - x
        for (rx, ry), (tx, ty), w in (((9.0, 18.0), (4.6, 30.0), 4.6), ((10.6, 22.0), (7.0, 34.0), 4.4), ((12.6, 25.0), (10.6, 34.6), 4.0),
                                      ((8.6, 12.0), (4.2, 22.0), 4.4), ((7.6, 24.0), (3.8, 29.0), 3.6)):
            add((X(rx), ry), (X(tx), ty), w)
    # round the crown, falling outward and down
    for i, a in enumerate((-172, -154, -136, -118, -100, -82, -64, -46, -28, -10)):      # up and over the crown from its whorl
        ang = math.radians(a + 6 * jitter(i, 3.0))
        r0, r1 = 2.4, 11.8 + 1.2 * jitter(i, 4.0)
        root = (20 + r0 * math.cos(ang), 10.0 + r0 * math.sin(ang) * 0.8)
        tip = (20 + r1 * math.cos(ang) * 1.22, 10.0 + r1 * math.sin(ang) * 0.72 + (3.0 if abs(a + 90) > 60 else 0.0))
        add(root, tip, 4.6 + 0.4 * jitter(i, 5.0))
    for s in (-1, 1):                                                 # framing the face, over the ears
        X = lambda x: x if s < 0 else 40 - x
        add((X(11.4), 9.0), (X(8.0), 20.0), 4.2)
        add((X(12.6), 12.0), (X(11.0), 24.0), 3.8)
    if front:                                                         # over the brow
        for rx, tx in ((14.0, 12.8), (17.4, 16.6), (21.0, 22.0), (24.6, 25.6), (27.0, 28.4)):
            add((rx, 6.4), (tx, 14.4 + (0.8 if rx > 20 else 0)), 3.4)
    else:                                                             # over the back of the head
        for i in range(6):                                            # tiers down the back, no two alike
            rx = 11.4 + i * 3.4 + 1.0 * jitter(i, 6.0)
            add((rx, 9.0 + 1.6 * jitter(i, 7.0)), (rx + (rx - 20) * 0.18 + 1.2 * jitter(i, 8.0), 20.0 + 2.4 * jitter(i, 9.0)), 4.4 + 0.5 * jitter(i, 10.0))
        for i in range(5):
            rx = 12.8 + i * 3.6 + 1.2 * jitter(i, 11.0)
            add((rx, 17.0 + 2.0 * jitter(i, 12.0)), (rx + (rx - 20) * 0.2 + 1.4 * jitter(i, 13.0), 31.0 + 2.6 * jitter(i, 14.0)), 4.2 + 0.5 * jitter(i, 15.0))
    return out


def style_curly_bob(F):
    """Big shoulder-length curls (the reference's): a wide, airy mass widening to the jaw, built of
    curl clumps, each its own springy lock with its sheen and spiral tip; the shadowed hair shows
    only in the gaps between them, so the silhouette is the curls' own; some fall over the brow."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_volume(m, F)
    depth = [grey(v) for v in (40, 52, 66, 80, 96)]

    def gaps(clumps):
        """The shadowed hair between curls: within the curls' own outline, never past it."""
        whole = union(*[c_[0] for c_ in clumps])
        return clip(whole.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.MinFilter(7)))
    if F.front:
        fs = F.fs * 0.5
        sides = bob_clumps(F, t * 0.6, True)[:10]
        shade(back, gaps(sides), depth, radius=2.0)
        paint_clumps(back, sides, clip, bias=-0.12)                     # the sides, behind the shoulders
        rest = bob_clumps(F, fs * 0.6, True)[10:]
        shade(front, gaps(rest), depth, radius=2.0)
        paint_clumps(front, rest, clip, bias=0.04)
        halo(front, clip(union(*[c_[0] for c_ in rest])), 3.6 + hy, F.fs)
        brows(front, F)
    else:
        allc = bob_clumps(F, t, False)
        shade(front, gaps(allc), depth, radius=2.0)
        paint_clumps(front, allc, clip)
        halo(front, clip(union(*[c_[0] for c_ in allc])), 3.6 + hy, 0)
    return front, back


def s_wave(F, x0, y0, length, drift, w, amp=2.8, period=10.0, dx=0.0, phase=0.0, curl=1):
    """A big loose S-wave lock (the long reference's), tapering into a spiral tip."""
    n = max(6, int(length / (period / 10)) + 1)                         # sampled finely: a smooth S, not a zigzag
    pts = [(x0 + dx + drift * k / (n - 1) + amp * math.sin(phase + 2 * math.pi * (length * k / (n - 1)) / period), y0 + length * k / (n - 1) + F.hy)
           for k in range(n)]
    lx, ly = pts[-1]
    pts += [(lx + curl * 1.4, ly + 0.8), (lx + curl * 2.0, ly - 0.4)]
    widths = [max(0.3, w * (1.0 if k / (len(pts) - 1) < 0.6 else ((1 - k / (len(pts) - 1)) / 0.4) ** 0.9)) for k in range(len(pts))]
    return strip(pts, widths, steps=8), pts, w


def s_path(F, path, w, amp=2.4, period=10.0, phase=0.0, curl=1, dx=0.0):
    """A wave lock along a path (from the parting, round the head and down): the S swings across
    the path, small at the root and full further down, tapering into a spiral tip."""
    line = catmull([(x + dx, y + F.hy) for x, y in path], 12)
    dist = [0.0]
    for a, b in zip(line, line[1:]):
        dist.append(dist[-1] + math.dist(a, b))
    total = dist[-1] or 1
    pts = []
    for i, ((x, y), d) in enumerate(zip(line, dist)):
        a, b = line[max(0, i - 1)], line[min(len(line) - 1, i + 1)]
        tx, ty = b[0] - a[0], b[1] - a[1]
        n = math.hypot(tx, ty) or 1
        nx, ny = -ty / n, tx / n
        f = d / total
        off = amp * min(1.0, f * 2.2) ** 0.8 * math.sin(phase + 2 * math.pi * d / period)
        pts.append((x + nx * off, y + ny * off))
    pts = pts[::2]
    lx, ly = pts[-1]
    pts += [(lx + curl * 1.4, ly + 0.8), (lx + curl * 2.0, ly - 0.4)]
    widths = [max(0.3, w * (0.75 + 0.25 * min(1, k / 6)) * (1.0 if k / (len(pts) - 1) < 0.62 else ((1 - k / (len(pts) - 1)) / 0.38) ** 0.9))
              for k in range(len(pts))]
    return strip(pts, widths, steps=4), pts, w


def s_wave_lines(img, locks, m):
    """Each wave: a bright sheen along its lit side, a shadow along the other, its tip a spiral."""
    for _, pts, w in locks:
        body = pts[:-2]
        curve(img, [(x + w * 0.24, y) for x, y in body], HAIR[1], 2, m)
        curve(img, [(x - w * 0.22, y) for x, y in body], HAIR[4], 2, m)
        curve(img, [(x - w * 0.05, y + 0.3) for x, y in body[1:-1]], HAIR[3], 1, m)
        tx, ty = pts[-3]
        spiral(img, tx, ty + 0.4, min(1.6, w * 0.3), 0.0, m, width=1, turns=1.1)


def paint_waves(img, locks, clip, bias=0.0):
    """Waves painted one by one, back to front: each shaded and lined on its own, so a nearer lock
    hides the lines of the ones behind it (separate locks, not a lattice)."""
    for lock in locks:
        lm = clip(lock[0])
        part(img, lm, HAIR, HAIR_LINE, radius=1.6, bias=bias)
        s_wave_lines(img, [lock], lm)


def style_curly_long(F):
    """Long, full curls to the waist (the reference's): big loose S-waves from a deep side parting,
    wrapping round the head and fanning out wide as they fall, each its own lock with its sheen and
    its spiral tip, painted back to front over the crown."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_volume(m, F, top=2.0)
    if F.front:
        fs = F.fs * 0.5
        behind = [s_wave(F, x, y, l, d, w, phase=ph, dx=t * 0.5, curl=-1 if x < 20 else 1)
                  for x, y, l, d, w, ph in ((9.0, 14, 34, -5.0, 6.4, 0.0), (11.6, 18, 30, -2.6, 5.6, 1.6), (6.8, 20, 26, -4.4, 5.0, 3.0),
                                            (31.0, 14, 34, 5.0, 6.4, 1.0), (28.4, 18, 30, 2.6, 5.6, 2.4), (33.2, 20, 26, 4.4, 5.0, 0.6),
                                            (16.0, 22, 26, -1.0, 5.4, 2.0), (24.0, 22, 26, 1.0, 5.4, 0.4))]
        part(back, clip(back_mass(F, 34)), HAIR, HAIR_LINE, radius=2.4, bias=-0.15)
        paint_waves(back, behind, clip, bias=-0.12)
        # From a deep side parting, big locks rise, fold over and sweep down across the brow to the far
        # temple (as the braided ponytail's do), leaving the forehead bare on the parting's side,
        # where the hair is combed back over the ear; the waves fall from under the sweep and the ear.
        waves = [s_path(F, [(11.4, 14.0), (9.4, 20.0), (8.6, 30.0), (6.4, 44.0)], 6.4, phase=0.4, curl=-1, dx=fs * 0.3),
                 s_path(F, [(13.6, 17.0), (12.0, 24.0), (12.2, 33.0), (10.6, 42.0)], 4.8, phase=2.4, curl=1, dx=fs * 0.3),
                 s_path(F, [(29.6, 12.0), (31.2, 18.0), (31.8, 27.0), (33.6, 41.0)], 6.0, phase=1.6, curl=1, dx=t * 0.6),
                 s_path(F, [(28.0, 16.0), (28.8, 23.0), (28.6, 31.0), (29.6, 38.0)], 4.0, phase=0.8, curl=-1, dx=t * 0.6)]
        paint_waves(front, waves, clip, bias=0.04)
        sweep_def = [([(24.4, 3.2), (19.2, 2.0), (13.2, 4.6), (9.6, 10.4), (8.4, 16.0), (9.6, 19.2)], 6.4),
                     ([(24.0, 5.0), (18.4, 3.6), (12.4, 7.4), (9.8, 13.2), (9.4, 18.6), (7.6, 21.0)], 5.8),
                     ([(23.6, 6.8), (18.6, 6.2), (13.4, 9.4), (11.2, 14.4), (11.4, 18.2), (13.0, 19.6)], 5.0),
                     ([(23.2, 8.6), (19.2, 8.6), (15.2, 11.6), (13.6, 15.8), (14.4, 18.6)], 4.2)]
        sweep = [hwisp(F, pts, w, fs, power=2.2) for pts, w in sweep_def]
        back_comb = [hwisp(F, [(25.4, 3.6), (28.4, 6.2), (30.2, 10.6), (30.6, 15.0)], 4.6, fs, power=1.6),
                     hwisp(F, [(25.0, 5.6), (27.6, 8.4), (28.8, 12.6), (29.0, 16.4)], 3.2, fs)]
        brow_ = poly([(22.0 + fs, 10.6 + hy), (24.0 + fs, 8.0 + hy), (27.0 + fs, 9.6 + hy), (28.6 + fs, 13.6 + hy), (29.0 + fs, 20 + hy), (21.0 + fs, 20 + hy)])
        fm = clip(union(minus(crown(F), brow_), *[l[0] for l in sweep + back_comb]))
        part(front, fm, HAIR, HAIR_LINE, radius=1.8, bias=0.06)
        for k, (_, pts, w) in enumerate(sweep):                         # a lit crest where each lock lifts, a shadow where it folds
            curve(front, [(x - 0.2, y + 0.5) for x, y in (lerp(pts[0], pts[1], 0.3), pts[1], lerp(pts[1], pts[2], 0.5))], HAIR[4], 3 if k < 2 else 2, fm)
            curve(front, [(x + 1.0, y + 1.0) for x, y in (lerp(pts[1], pts[2], 0.5), pts[2], lerp(pts[2], pts[3], 0.55))], HAIR[0] if k < 2 else HAIR[1], 2, fm)
        hstrands(front, sweep + back_comb, fm, start=0.45)
        spiral(front, 8.8 + fs, 19.2 + hy, 1.4, 0.0, fm, width=1)
        curve(front, [(24.8 + fs, 2.8 + hy), (24.6 + fs, 6.0 + hy)], HAIR_LINE, 1, fm)
        halo(front, fm, 3.6 + hy, F.fs)
        brows(front, F)
    else:
        m = clip(union(soft_crown(F, t, lumps=((8.6, 3.6, 22.6, 13.4), (19.4, 2.8, 31.4, 13.4))), ellipse([7.6 + t, 7 + hy, 32.4 + t, 26 + hy])))
        part(front, m, HAIR, HAIR_LINE, radius=2.4, bias=-0.05)
        falls = [s_path(F, [(20.0, 4.0), (x0, 10.0), (x1, 22.0), (x2, 34.0), (x3, 48.0)], w, phase=ph, curl=-1 if x3 < 20 else 1, dx=t)
                 for x0, x1, x2, x3, w, ph in ((12.0, 8.2, 6.6, 3.8, 6.0, 0.0), (28.0, 31.8, 33.4, 36.2, 6.0, 1.2), (14.6, 11.4, 10.6, 9.0, 6.4, 2.4),
                                               (25.4, 28.6, 29.4, 31.0, 6.4, 0.6), (17.4, 15.4, 15.2, 14.6, 6.6, 1.8), (22.6, 24.6, 24.8, 25.4, 6.6, 3.0),
                                               (20.0, 20.0, 19.8, 20.0, 6.0, 0.9))]
        paint_waves(front, falls, clip, bias=-0.04)
        curve(front, [(20.0 + t, 3.6 + hy), (20.6 + t, 6.6 + hy), (19.8 + t, 8.0 + hy)], HAIR[1], 1, m)       # the whorl
        halo(front, clip(union(m, *[l[0] for l in falls])), 4.0 + hy, 0)
    return front, back


def coil(img, F, m, cx, cy, rx, ry):
    """A bun: hair wound round itself, drawn as strands curving round the knot at a few depths,
    each starting where another ends, the lit ones on the upper left; the tucked end in the middle."""
    for k, (r, a0, span) in enumerate(((0.92, 0.6, 3.6), (0.74, 2.6, 3.4), (0.56, 4.6, 3.2), (0.86, 3.8, 2.0), (0.38, 0.4, 3.0))):
        pts = [(cx + rx * r * math.cos(a), cy + ry * r * math.sin(a) + F.hy) for a in [a0 + span * i / 10 for i in range(11)]]
        curve(img, pts, HAIR[1] if k % 2 == 0 else HAIR[2], 1, m)
        lit = [(x, y) for x, y in pts if x < cx and y < cy + F.hy]
        if len(lit) > 1:
            curve(img, [(x + 0.4, y + 0.4) for x, y in lit], HAIR[4], 1, m)
    curve(img, [(cx - rx * 0.1, cy + F.hy), (cx + rx * 0.16, cy - ry * 0.12 + F.hy), (cx + rx * 0.2, cy + ry * 0.14 + F.hy)], HAIR_LINE, 1, m)


def style_sidebraid(F):
    """Parted deep on one side: the hair swept across the brow from it, a braid from the far temple
    down over the shoulder, and loose waves to the shoulder on the parting's side."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)
    spine = [(12.6, 13.0), (10.6, 19.0), (10.2, 26.0), (11.4, 33.0), (12.6, 38.6)]
    if F.front:
        fs = F.fs * 0.5
        loose = wave_lock(F, 29.6 + t, 14, 20, 1, 5.0, amp=1.0, curl=1)
        bm = clip(union(back_mass(F, 28), loose[0]))
        part(back, bm, HAIR, HAIR_LINE, radius=2.0, bias=-0.12)
        wave_lines(back, [loose], bm)
        top = soft_crown(F, fs * 0.6, lumps=((10.8, 5.0, 25.0, 13.0), (22.8, 4.6, 30.0, 13.2)))
        sweep = [hwisp(F, [(25.0, 5.2), (20.4, 6.6), (15.6, 9.8), (12.6, 14.0), (12.0, 17.0)], 5.6, fs, power=2.0),
                 hwisp(F, [(24.6, 7.2), (20.2, 9.4), (16.6, 12.6), (15.0, 16.0), (15.4, 17.6)], 4.2, fs),
                 hwisp(F, [(24.4, 9.0), (21.0, 11.6), (18.6, 15.0), (18.6, 16.8)], 2.8, fs)]
        far = [hwisp(F, [(25.8, 6.4), (28.0, 10.0), (29.0, 15.0), (28.6, 18.4)], 3.4, fs),
               wave_lock(F, 29.8 + t * 0.6, 12, 18, 1, 4.4, amp=1.0, curl=-1)]
        dutch_plait(front, [(x + t * 0.3, y + hy) for x, y in spine], 3.8, 10, clip=clip)          # under the sweep it grows from
        ties(front, F, [(12.6 + t * 0.3, 39.0)])
        tuft = hwisp(F, [(12.6, 39.6), (12.2, 41.6), (12.8, 43.2)], 2.6, t * 0.3)
        part(front, clip(tuft[0]), HAIR, HAIR_LINE, radius=0.6, bias=0.05)
        gather = [hwisp(F, [(14.6, 11.0), (12.4, 15.6), (11.4, 19.6), (11.0, 22.0)], 3.8, fs, power=2.2)]
        m = clip(union(top, *[l[0] for l in sweep + far + gather]))
        part(front, m, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        curve(front, [(25.2 + fs, 4.2 + hy), (25.0 + fs, 8.0 + hy)], HAIR_LINE, 1, m)
        hstrands(front, sweep + far[:1] + gather, m, start=0.3)
        wave_lines(front, far[1:], m)
        halo(front, m, 4.4 + hy, F.fs)
        brows(front, F)
    else:
        m = clip(union(soft_crown(F, t), ellipse([8.6 + t, 8 + hy, 31.4 + t, 25.0 + hy]),
                       wave_lock(F, 10.4 + t, 15, 14, -1, 4.6, amp=1.0, curl=-1)[0]))
        part(front, m, HAIR, HAIR_LINE, radius=2.2, bias=-0.05)
        combed(front, F, m, [(x, y) for x, y in ((11.0, 12), (13.6, 8), (17.4, 5.4), (21.6, 5.0), (25.4, 7.0), (27.6, 12.0), (24.0, 20.0), (18.0, 22.0))],
               (30.0, 21.0), dx=t, bend=0.3)
        bspine = [(28.4, 15.0), (30.0, 21.0), (30.2, 28.0), (29.4, 34.0), (28.6, 38.0)]          # round to the other shoulder, seen from behind
        dutch_plait(front, [(x + t, y + hy) for x, y in bspine], 3.6, 9, clip=clip, line=HAIR[0])
        halo(front, m, 4.4 + hy, 0)
    return front, back


def style_bun(F):
    """Gathered up into a bun high at the back of the head: the crown combed smooth and up, wisps
    escaping at the temples and the nape, the bun a coil wound round itself."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)
    if F.front:
        fs = F.fs * 0.5
        bun = ellipse([14.6 + t, -0.4 + hy, 25.4 + t, 9.0 + hy])
        part(back, bun, HAIR, HAIR_LINE, radius=1.8, bias=0.0)
        coil(back, F, bun, 20 + t, 4.3, 4.2, 3.6)
        part(back, clip(back_mass(F, 24)), HAIR, HAIR_LINE, radius=2.2, bias=-0.15)
        top = soft_crown(F, fs * 0.6, lumps=((10.8, 5.0, 29.2, 13.0),))
        edge_ = [hwisp(F, [(rx, 9.4), (rx + 0.4, 12.0), (rx + 0.8, 14.0)], 3.0, fs) for rx in (11.8, 14.6, 25.4, 28.0)]
        wisps = [hwisp(F, mirror([(11.4, 12.4), (10.6, 17.6), (11.2, 22.4), (12.2, 24.0)], s, t * (0.6 if s > 0 else 0)), 1.4) for s in (-1, 1)]
        sides = [hwisp(F, mirror([(10.6, 11.4), (9.8, 15.6), (10.4, 18.0)], s, t * (0.6 if s > 0 else 0)), 3.4, power=1.2) for s in (-1, 1)]
        m = clip(union(top, *[l[0] for l in edge_ + wisps + sides]))
        part(front, m, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        combed(front, F, m, [(x, 12.6) for x in (13.0, 16.0, 19.0, 22.0, 25.0, 27.6)], (20.0, 2.0), dx=fs * 0.6, bend=0.15, reach=0.62)
        hstrands(front, wisps, m, start=0.3)
        halo(front, m, 4.4 + hy, F.fs)
        brows(front, F)
    else:
        m = clip(union(soft_crown(F, t), ellipse([8.6 + t, 8 + hy, 31.4 + t, 24.0 + hy]),
                       *[hwisp(F, [(x, 20), (x + s * 0.3, 23.4), (x + s * 0.6, 25.6)], 2.0, t)[0] for x, s in ((12.4, -1), (16.4, -1), (23.6, 1), (27.6, 1))]))
        part(front, m, HAIR, HAIR_LINE, radius=2.2, bias=-0.05)
        combed(front, F, m, [(x, y) for x, y in ((10.6, 21), (13.4, 23.4), (17.0, 24.6), (20.4, 25.0), (23.8, 24.6), (27.2, 23.2), (29.6, 20.6))],
               (20.0, 11.0), dx=t, bend=0.25, reach=0.7)
        bun = ellipse([14.4 + t, 5.0 + hy, 25.6 + t, 15.0 + hy])
        part(front, bun, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        coil(front, F, bun, 20 + t, 10.0, 4.4, 4.0)
        ties(front, F, [(20 + t, 14.6)])
        halo(front, m, 4.4 + hy, 0)
    return front, back


def style_crownbraid(F):
    """A braid laid over the crown from ear to ear like a circlet, the rest combed smoothly back
    under it, soft wisps loose at the temples and the nape."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)
    if F.front:
        fs = F.fs * 0.5
        part(back, clip(back_mass(F, 27)), HAIR, HAIR_LINE, radius=2.2, bias=-0.15)
        top = soft_crown(F, fs * 0.6, lumps=((10.6, 4.8, 29.4, 13.6),))
        edge_ = [hwisp(F, [(rx, 13.4), (rx + 0.3, 15.0), (rx + 0.6, 16.0)], 2.4, fs) for rx in (12.6, 16.4, 23.6, 27.4)]
        wisps = [hwisp(F, mirror([(10.8, 14.4), (10.4, 19.4), (11.2, 23.8), (12.0, 25.0)], s, t * (0.6 if s > 0 else 0)), 1.3) for s in (-1, 1)]
        sides = [hwisp(F, mirror([(10.4, 13.6), (9.8, 17.0), (10.6, 19.6)], s, t * (0.6 if s > 0 else 0)), 3.2, power=1.2) for s in (-1, 1)]
        m = clip(union(top, *[l[0] for l in edge_ + wisps + sides]))
        part(front, m, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        combed(front, F, m, [(x, 15.4) for x in (14.2, 18.0, 22.0, 25.8)], (20.0, 9.0), dx=fs * 0.6, bend=0.1, reach=0.5)
        hstrands(front, wisps, m, start=0.3)
        halo(front, m, 4.2 + hy, F.fs)
        dutch_plait(front, [(10.2 + fs * 0.3, 17.4 + hy), (12.0 + fs * 0.4, 11.4 + hy), (20.0 + fs, 7.6 + hy), (28.0 + fs * 0.4, 11.4 + hy), (29.8 + fs * 0.3, 17.4 + hy)],
                    3.8, 11, clip=clip, tie=False, soft=True)
        brows(front, F)
    else:
        nape = [hwisp(F, [(x, 20), (x + s * 0.4, 24.4), (x + s * 0.8, 27.0)], 3.6, t) for x, s in ((11.4, -1), (15.4, -1), (19.6, 1), (24.0, 1), (28.4, 1))]
        m = clip(union(soft_crown(F, t), ellipse([8.6 + t, 8 + hy, 31.4 + t, 24.4 + hy]), *[l[0] for l in nape]))
        part(front, m, HAIR, HAIR_LINE, radius=2.2, bias=-0.05)
        combed(front, F, m, [(x, 4.6 + abs(x - 20) * 0.18) for x in (12.0, 15.6, 19.4, 23.2, 27.6)], (20.0, 13.0), dx=t, bend=0.2, reach=0.6)
        hstrands(front, nape, m, start=0.4)
        halo(front, m, 4.4 + hy, 0)
        dutch_plait(front, [(9.8 + t, 12.4 + hy), (14.0 + t, 15.4 + hy), (20.0 + t, 16.4 + hy), (26.0 + t, 15.4 + hy), (30.2 + t, 12.4 + hy)], 3.8, 11,
                    clip=lambda m_: under_helm(both(m_, ellipse([9.5 + t * 0.5, 5 + hy, 30.5 + t * 0.5, 31.2 + hy])), F), tie=False, soft=True)
    return front, back


def style_shaggy(F):
    """Shaggy and layered, cut at every length: choppy locks over the brow, layers flicking out at
    the ears and the jaw, the nape in uneven points."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)
    if F.front:
        fs = F.fs * 0.5
        nape = [hwisp(F, [(x, 22), (x + s * 1.4, 27), (x + s * 2.6, 31.0 + (k % 2))], 4.4, t) for k, (x, s) in enumerate(((10.6, -1), (13.4, -1), (26.6, 1), (29.4, 1)))]
        bm = clip(union(back_mass(F, 28), *[l[0] for l in nape]))
        part(back, bm, HAIR, HAIR_LINE, radius=2.2, bias=-0.12)
        top = soft_crown(F, fs * 0.6, lumps=((10.4, 4.4, 18.6, 12.6), (15.6, 2.8, 23.6, 10.8), (21.0, 3.4, 29.6, 12.4)))
        fringe_ = [hwisp(F, [(rx, 8.4), (rx + dx * 0.5, 12.6), (rx + dx, 16.0 + d)], 3.6, fs)
                   for rx, dx, d in ((11.4, -1.2, 0.8), (13.8, 0.4, 2.2), (16.2, -0.6, 0.4), (18.4, 0.8, 2.6), (20.8, -0.2, 1.0), (23.0, 1.2, 2.0), (25.4, 0.2, 0.2), (27.8, 1.4, 1.4))]
        layers = []
        for s in (-1, 1):
            X = lambda pts: mirror(pts, s, t * (0.6 if s > 0 else 0))
            layers += [hwisp(F, X([(11.4, 11.4), (9.6, 16.0), (8.6, 20.0), (7.0, 21.6)]), 4.0),           # flicks out over the ear
                       hwisp(F, X([(11.0, 15.0), (9.8, 20.6), (9.2, 25.0), (7.6, 26.8)]), 3.6),
                       hwisp(F, X([(12.0, 17.4), (11.0, 22.6), (10.8, 27.4), (9.8, 29.4)]), 2.6)]
        strays = [hwisp(F, [(17.0, 3.6), (14.6, 1.8), (12.8, 2.6)], 1.0, fs), hwisp(F, [(22.4, 3.0), (24.6, 1.2), (26.4, 2.0)], 0.9, fs)]
        m = clip(union(top, *[l[0] for l in fringe_ + layers + strays]))
        part(front, m, HAIR, HAIR_LINE, radius=1.8, bias=0.05)
        hstrands(front, fringe_ + layers, m, start=0.4)
        for x in (14.4, 19.4, 24.6):
            curve(front, [(x + fs * 0.6, 3.8 + hy), (x - 0.6 + fs * 0.6, 7.0 + hy), (x - 1.0 + fs * 0.6, 9.4 + hy)], HAIR[1], 1, m)
        halo(front, m, 4.0 + hy, F.fs)
        brows(front, F)
    else:
        nape = [hwisp(F, [(x, 17), (x + s * 0.8, 23), (x + s * 1.8, y), (x + s * 2.8, y + 1.0)], 5.0, t)
                for x, s, y in ((10.2, -1, 28.0), (13.6, -1, 30.6), (17.0, -1, 29.0), (20.4, 1, 31.2), (23.8, 1, 29.4), (27.2, 1, 30.4), (30.2, 1, 27.6))]
        sides = [hwisp(F, mirror([(10.6, 14.0), (8.6, 18.4), (6.8, 20.4)], s, t), 3.6) for s in (-1, 1)]
        m = clip(union(soft_crown(F, t, lumps=((10.4, 4.4, 18.6, 12.6), (15.6, 2.8, 23.6, 10.8), (21.0, 3.4, 29.6, 12.4))),
                       ellipse([8.6 + t, 8 + hy, 31.4 + t, 24.0 + hy]), *[l[0] for l in nape + sides]))
        part(front, m, HAIR, HAIR_LINE, radius=2.2, bias=-0.05)
        for x in (11.6, 15.6, 19.8, 24.0, 28.2):
            s = -1 if x < 20 else 1
            curve(front, [(20.2 + t + s * 0.8, 7.8 + hy), (x + t - s * 1.6, 14.6 + hy), (x + t - s * 0.4, 21.0 + hy)], HAIR[1], 1, m)
        hstrands(front, nape + sides, m, start=0.5)
        halo(front, m, 4.0 + hy, 0)
    return front, back


def style_topknot(F):
    """Shaved at the sides and back (stubble showing), the hair on top combed back and up into a
    knot on the crown, bound, its end falling back over it."""
    front, back = blank(), blank()
    hy, t = F.hy, F.t
    clip = lambda m: under_helm(m, F)
    shaved(front, F)
    band = minus(ellipse([14.8 + t, 3.4 + hy, 25.2 + t, 26 + hy]), rect([0, (13.4 if F.front else 15.0) + hy, 40, 60]))
    knot = ellipse([16.4 + t, -1.4 + hy, 23.6 + t, 4.6 + hy])
    if F.front:
        tail = hwisp(F, [(21.0, 0.4), (24.0, 1.6), (25.6, 4.6), (25.4, 7.6)], 2.6, t)
        part(back, clip(tail[0]), HAIR, HAIR_LINE, radius=0.8, bias=-0.1)
        edge_ = [hwisp(F, [(rx, 10.0), (rx + 0.2, 12.4), (rx + 0.3, 14.2)], 2.4, t) for rx in (16.0, 18.6, 21.4, 24.0)]
        m = clip(union(band, *[l[0] for l in edge_]))
        part(front, m, HAIR, HAIR_LINE, radius=1.4, bias=0.05)
        combed(front, F, m, [(x, 13.0) for x in (16.2, 18.4, 20.4, 22.4, 24.2)], (20.0, 3.4), dx=t, bend=0.1, reach=0.75)
        km = clip(knot)
        part(front, km, HAIR, HAIR_LINE, radius=1.0, bias=0.1)
        coil(front, F, km, 20 + t, 1.6, 3.0, 2.4)
        ties(front, F, [(20 + t, 4.4)])
        halo(front, m, 5.4 + hy, F.fs)
        brows(front, F)
    else:
        m = clip(band)
        part(front, m, HAIR, HAIR_LINE, radius=1.4, bias=-0.05)
        combed(front, F, m, [(x, 14.4) for x in (16.0, 18.2, 20.2, 22.2, 24.2)], (20.0, 3.6), dx=t, bend=0.1, reach=0.75)
        km = clip(knot)
        part(front, km, HAIR, HAIR_LINE, radius=1.0, bias=0.05)
        coil(front, F, km, 20 + t, 1.6, 3.0, 2.4)
        tail = hwisp(F, [(20.6, 2.6), (21.6, 6.0), (21.0, 9.6), (21.6, 12.0)], 2.8, t)
        tm = clip(tail[0])
        part(front, tm, HAIR, HAIR_LINE, radius=0.8, bias=0.05)
        hstrands(front, [tail], tm, start=0.2)
        ties(front, F, [(20 + t, 4.0)])
        halo(front, m, 5.4 + hy, 0)
    return front, back


STYLES = {"braidedponytail": braided_ponytail, "swept": tousled, "short": style_short, "long": style_long,
          "ponytail": style_ponytail, "pigtails": style_pigtails, "curlyshort": style_curly_bob,
          "curlylong": style_curly_long, "sidebraid": style_sidebraid, "bun": style_bun,
          "crownbraid": style_crownbraid, "shaggy": style_shaggy, "topknot": style_topknot}


def hair_layers(F, style):
    """(front, back) hair images for a style, brows included in the front."""
    if style in STYLES:
        return STYLES[style](F)
    front, back = blank(), blank()
    hy = F.hy
    strokes, sheen, extras = [], [], []
    front_curls, back_curls = [], []

    def done(img, m, bias=0.05, radius=1.6):
        part(img, m, HAIR, HAIR_LINE, radius=radius, bias=bias)

    if F.front:
        fr_kind, sides, back_len, curly = {
            "short": ("spiky", 24, 30, False), "long": ("parted", 36.5, 44, False), "ponytail": ("parted_short", 27, 30, False),
            "pigtails": ("blunt", 24, 30, False), "curlyshort": ("curly", 26, 30, True), "curlylong": ("curly", 34, 40, True),
            "none": (None, 0, 0, False), "sidebraid": ("swept", 26, 33, False), "topknot": (None, 0, 0, False),
            "bun": ("parted_short", 25, 28, False), "crownbraid": ("bare", 23, 30, False), "shaggy": ("messy", 30, 32, False),
            "swept": ("swept", 31, 34, False),
        }[style]
        fm = nothing()
        if style == "topknot":
            fm = minus(ellipse([15.6 + F.t, 3.6 + hy, 24.4 + F.t, 26 + hy]), rect([0, 13.2 + hy, 40, 60]))
            fm = union(fm, poly([(17 + F.t, 12.6 + hy), (23 + F.t, 12.6 + hy), (21.8 + F.t, 14.2 + hy), (20 + F.t, 13.6 + hy), (18.2 + F.t, 14.2 + hy)]))
            fm = union(fm, ellipse([17.2 + F.t, -0.8 + hy, 22.8 + F.t, 4.2 + hy]))
            for x in (17.8, 20, 22.2):                                   # combed back toward the knot
                strokes.append([(x + F.t, 13 + hy), (x + (20 - x) * 0.3 + F.t, 8 + hy), (20 + F.t, 3.6 + hy)])
            shaved(front, F)
        elif style != "none":
            fr, partings = fringe(F, fr_kind)
            fm = union(crown(F), fr, side_locks(F, sides, curly=curly, curls=front_curls) if sides else nothing())
            strokes += partings
            if not curly and sides > 26:
                for s in (-1, 1):
                    X = lambda x: side_x(F, s, x)
                    strokes.append([(X(10), 17 + hy), (X(9), 23 + hy), (X(9.2), min(29, sides - 4) + hy)])
                    sheen.append([(X(9.4), 18 + hy), (X(8.4), 24 + hy)])
        if style == "pigtails":
            for s in (-1, 1):
                X = lambda x: side_x(F, s, x)
                m, _ = braid([(X(10.6), 20 + hy), (X(10.2), 28 + hy), (X(10.8), 37 + hy)], 3.6, 7)
                extras.append((m, [(X(10.6), 37.4 + hy)]))
        if style == "sidebraid":
            m, _ = braid([(11.6, 18 + hy), (10.6, 26 + hy), (12, 34 + hy), (13.2, 39 + hy)], 3.8, 8)
            extras.append((m, [(13.2, 39.4 + hy)]))
        fm = under_helm(fm, F)
        if fm.getbbox():
            done(front, fm)
            if style == "topknot":
                part(front, ellipse([18.2 + F.t, 3.4 + hy, 21.8 + F.t, 4.6 + hy]), [grey(70), grey(96), grey(120)], HAIR_LINE, radius=0.4, width=1)
        for m, ties in extras:
            m = under_helm(m, F)
            done(front, m, radius=0.8, bias=0.1)
            for tx, ty in ties:
                part(front, ellipse([tx - 1.3, ty - 0.7, tx + 1.3, ty + 0.7]), [grey(70), grey(96), grey(120)], HAIR_LINE, radius=0.4, width=1)
        if style == "crownbraid":   # a plaited crown from ear to ear, like the braided ponytail's
            plait(front, [(10.2, 16 + hy), (12.4, 10 + hy), (20 + F.t, 7.2 + hy), (27.6, 10 + hy), (29.8, 16 + hy)], 3.8, 10, clip=lambda m: under_helm(m, F))
        if style == "curlylong" or style == "curlyshort":
            pass
        # back half
        bm = nothing()
        if style not in ("none", "topknot"):
            bm = back_mass(F, back_len, curly=curly, curls=back_curls)
        if style == "ponytail":
            bm = union(bm, lock(F, [(29 + F.t, 16), (31.6 + F.t, 23), (32.4 + F.t, 30), (31.2 + F.t, 36)], [4, 4, 3, 0.3]))
        if style == "bun":
            bm = union(bm, ellipse([15 + F.t, 1 + hy, 25 + F.t, 9 + hy]))
        if style == "topknot":
            bm = union(bm, lock(F, [(21.4 + F.t, 2.6), (25 + F.t, 5), (26.8 + F.t, 10.4), (26.4 + F.t, 14)], [2.6, 2.4, 1.8, 0.3]))
        bm = under_helm(bm, F) if style not in ("bun",) else bm
        if bm.getbbox():
            done(back, bm, bias=-0.12, radius=2.0)
            curl_marks(back, back_curls, bm)
            if not curly:
                for s in (-1, 1):
                    X = lambda x: 20 + s * (20 - x) + F.t
                    if back_len > 32:
                        curve(back, [(X(8.8), 20 + hy), (X(7.4), 28 + hy), (X(7.6), back_len - 9 + hy), (X(7.2), back_len - 3 + hy)], HAIR[1], 2, bm)
                        curve(back, [(X(7.8), 22 + hy), (X(6.6), 30 + hy)], HAIR[3], 2, bm)
        curl_marks(front, front_curls, fm)
        if fr_kind == "curly":
            curl_marks(front, [(11 + k * 2.6 + F.fs * 0.5, 16.8 + (k % 2) * 0.8 + hy, 1.8) for k in range(8)], fm)
        for pts in strokes:
            curve(front, pts, HAIR[1], 2, fm)
        for pts in sheen:
            curve(front, pts, HAIR[4], 2, fm)
        if style not in ("none",):
            halo(front, fm, 8 + hy, F.fs)
        brows(front, F)
    else:
        length, curly, tips = {
            "short": (27, False, 7), "long": (44, False, 7), "ponytail": (28, False, 7), "pigtails": (28, False, 7),
            "curlyshort": (29, True, 7), "curlylong": (40, True, 7), "none": (0, False, 7), "sidebraid": (33, False, 7),
            "topknot": (0, False, 7), "bun": (27, False, 7), "crownbraid": (28, False, 7), "shaggy": (32, False, 9), "swept": (34, False, 7),
        }[style]
        m = nothing()
        if style == "topknot":
            m = union(minus(ellipse([15.6 + F.t, 3.6 + hy, 24.4 + F.t, 26 + hy]), rect([0, 15 + hy, 40, 60])), ellipse([17.2 + F.t, -0.8 + hy, 22.8 + F.t, 4.2 + hy]),
                      lock(F, [(20 + F.t, 3.4), (21.2 + F.t, 9), (19.8 + F.t, 15), (20.8 + F.t, 20.4)], [4.2, 4.6, 3.8, 1.2]))
            shaved(front, F)
        elif style != "none":
            m = mane(F, length, curly, tips, curls=back_curls)
        if style == "ponytail":
            m = union(m, lock(F, [(20 + F.t, 17), (20.8 + F.t, 26), (20.2 + F.t, 33), (20.6 + F.t, 41)], [4.4, 4.2, 3.4, 0.3]))
        if style == "bun":
            m = union(m, ellipse([15 + F.t, 8 + hy, 25 + F.t, 17 + hy]))
        m = under_helm(m, F) if style != "bun" else m
        if m.getbbox():
            done(front, m, bias=-0.05, radius=2.2)
            curl_marks(front, back_curls, m)
            if not curly:
                for k in range(6):
                    x = 11 + k * 3.6 + F.t
                    curve(front, [(x, 17 + hy), (x + (-0.5 if x < 20 else 0.5), max(20, length - 3) + hy)], HAIR[1], 2, m)
                    curve(front, [(x - 1.2, 18 + hy), (x - 1.6, max(20, length - 10) + hy)], HAIR[4], 2, m)
            if style == "topknot":
                part(front, ellipse([18.2 + F.t, 3.6 + hy, 21.8 + F.t, 4.8 + hy]), [grey(70), grey(96), grey(120)], HAIR_LINE, radius=0.4, width=1)
            if style in ("ponytail", "bun"):
                tie = ellipse([18.4 + F.t, (17 if style == "ponytail" else 12) + hy, 21.6 + F.t, (18.4 if style == "ponytail" else 13.4) + hy])
                part(front, tie, [grey(70), grey(96), grey(120)], HAIR_LINE, radius=0.4, width=1)
            if style == "crownbraid":
                plait(front, [(9.8, 11.4 + hy), (14, 14.4 + hy), (20 + F.t, 15.4 + hy), (26, 14.4 + hy), (30.2, 11.4 + hy)], 3.8, 10,
                      clip=lambda m: under_helm(both(m, ellipse([9.5 + F.t * 0.5, 5 + hy, 30.5 + F.t * 0.5, 31.2 + hy])), F))   # the back of the ring dips, wrapping round the head
            halo(front, m, 8 + hy, 0)
        if F.t and style != "none":
            pass
    return front, back


def shaved(img, F):
    """Stubble over a shaved scalp: a light dusting of hair-coloured dots where hair would be."""
    hy = F.hy
    scalp = minus(both(ellipse([9.6 + F.t, 5 + hy, 30.4 + F.t, 31 + hy]), rect([0, 0, 40, (16.4 if F.front else 26) + hy])),
                  ellipse([15.2 + F.t, 4 + hy, 24.8 + F.t, 24 + hy]))
    bb = scalp.getbbox()
    if bb:
        pts = [(x, y) for y in range(bb[1], bb[3]) for x in range(bb[0], bb[2]) if ((x * 73856093) ^ (y * 19349663)) % 11 == 0]
        fine(img, pts, HAIR[2], scalp)


def halo(img, m, top, shift):
    """The sheen across the crown (only seen with the helm off): short broken strokes along an arc,
    brightest on the lit (left) side, fading out toward the right, so it reads as shine on locks
    rather than a headband."""
    cx = (20 + shift * 0.5) * K
    arc = lambda dx: (cx + dx, (top + 2.6) * K + (dx * dx) / (11 * K))
    for k, (a, b) in enumerate(((-30, -22), (-19, -12), (-9, -3), (1, 6), (10, 14), (18, 21))):
        pts = [arc(dx) for dx in range(a, b + 1, 2)]
        curve(img, pts, HAIR[4] if k < 3 else HAIR[3], 2, m, design=False)
        if k < 2:                                                   # a second, shorter stroke under the brightest
            curve(img, [(x + 2, y + 4) for x, y in pts[1:-1]], HAIR[3], 1, m, design=False)


def brows(img, F, only=(0, 1)):
    """Gentle brows over the fringe (as anime draws them), in the hair's colour."""
    for i, (x, q) in enumerate(eye_spots(F)):
        if i not in only:
            continue
        s = -1 if i == 0 else 1
        outer, inner = (x + s * 2.6 * q, 17.6 + F.hy), (x - s * 2.3 * q, 17.8 + F.hy)
        curve(img, [outer, ((outer[0] + inner[0]) / 2, 17.0 + F.hy), inner], HAIR_LINE, 3 if F.male else 2)


def beard_layer(F, style):
    img = blank()
    if F.back:
        return img
    hy, d = F.hy, (1.5 if F.t else 0)
    cx = 20 + d
    face = F.face_rect()
    mouth = ellipse([cx - 1.6, 27.6 + hy, cx + 1.6, 29.2 + hy])
    if style == "stubble":
        jaw = both(minus(ellipse([10.2, 12 + hy, 29.8, 31.2 + hy]), ellipse([12.6, 9 + hy, 27.4, 28.6 + hy])), face)
        jaw = union(jaw, both(ellipse([cx - 4, 26.4 + hy, cx + 4, 31 + hy]), face))
        jaw = minus(jaw, mouth)
        bb = jaw.getbbox()
        if bb:
            pts = [(x, y) for y in range(bb[1], bb[3]) for x in range(bb[0], bb[2]) if ((x * 73856093) ^ (y * 19349663)) % 5 == 0]
            fine(img, pts, grey(120), jaw)
        return img
    # Short, full or braided, after art/reference/shortbeard.webp and fullbeard.webp. The beard is
    # the jaw, in smooth curves: its outer edge the face's, from the sideburns round the chin and a
    # little past it; beside the eyes it keeps to the side of the face (never near them), then its
    # inner edge curves in along the jaw to meet the moustache under the nose, a notch of skin under
    # the lower lip. The full beard hangs well past the chin, its ends in soft tapering wisps, under
    # a moustache of its own whose tips curl up.
    big = style in ("full", "braided")
    sx = d * 0.3
    bottom = 40.6 if big else 33.4
    if big:
        outer = [(9.6, 16.2), (9.4, 19.6), (9.3, 23.6), (9.7, 28.0), (11.0, 32.4), (13.2, 36.4), (16.4, 39.4), (20.0, bottom)]
    else:
        outer = [(9.6, 16.2), (9.4, 19.6), (9.3, 23.4), (9.9, 27.0), (11.4, 30.0), (13.8, 32.2), (16.8, 33.2), (20.0, bottom)]
    # the sideburns start well up under the hair (drawn over them), so no sliver of skin opens between
    # the two as the head bobs and the layers land a pixel apart
    inner = [(10.8, 16.2), (10.8, 19.6), (10.9, 23.2), (11.5, 26.0), (12.8, 27.6), (14.6, 28.0), (cx - 3.6 - sx, 27.6)]

    def side(pts, s_):
        return [((x if s_ < 0 else 40 - x) + sx, y + hy) for x, y in pts]
    left = catmull([P(*q) for q in side(outer, -1)], 8)
    right = catmull([P(*q) for q in side(outer, 1)], 8)[::-1]
    inner_r = catmull([P(*q) for q in side(inner, 1)], 8)
    inner_l = catmull([P(*q) for q in side(inner, -1)], 8)[::-1]
    shape = mask(lambda d_: d_.polygon(left + right + inner_r[::-1][::-1] + [P(cx, 27.4 + hy)] + inner_l, fill=255))
    # the ends: soft wisps along the bottom, tapering down
    ends = []
    n = 8 if big else 6
    for k in range(n + 1):
        f = k / n
        x = 20 + (f - 0.5) * (14.0 if big else 8.8)
        y = bottom - (abs(x - 20) / 7.0) ** 2 * (3.4 if big else 1.4)
        ends.append(hwisp(F, [(x + sx, y - 2.0), (x + (20 - x) * 0.05 + sx, y + 0.4), (x + (20 - x) * 0.1 + sx, y + (2.0 if big else 1.0) + 0.6 * (k % 2))],
                          2.6 if big else 1.8, 0.0, power=1.3))
    shape = union(shape, *[e[0] for e in ends])
    lip = ellipse([cx - 1.8, 28.4 + hy, cx + 1.8, 29.8 + hy])
    notch = ellipse([cx - 1.0, 29.4 + hy, cx + 1.0, 31.0 + hy])
    stache = []
    for s_ in (-1, 1):
        if big:                                                        # sweeping out past the mouth, the tips curling down (well away from the eyes)
            pts = [(cx + s_ * 0.2, 26.9), (cx + s_ * 2.4, 27.2), (cx + s_ * 4.4, 28.0), (cx + s_ * 5.8, 29.4), (cx + s_ * 6.2, 31.0), (cx + s_ * 5.4, 32.0)]
            stache.append(hwisp(F, pts, 2.2, 0.0, power=1.1))
        else:
            pts = [(cx + s_ * 0.2, 26.9), (cx + s_ * 2.2, 27.1), (cx + s_ * 3.8, 27.8), (cx + s_ * 4.4, 28.8)]
            stache.append(hwisp(F, pts, 1.7, 0.0, power=1.2))
    sm = union(*[l[0] for l in stache])
    shape = minus(shape, union(lip, notch) if not big else lip)
    if style == "braided":                                              # the plait first: the beard lies over its root
        spine = [(cx, bottom - 2.6 + hy), (cx + 0.4, bottom + 0.6 + hy), (cx - 0.2, bottom + 3.8 + hy), (cx + 0.2, bottom + 6.2 + hy)]
        dutch_plait(img, spine, 3.0, 6)
        part(img, ellipse([cx - 1.4, bottom + 6.0 + hy, cx + 1.4, bottom + 7.4 + hy]), [grey(110), grey(150), grey(196)], HAIR_LINE, radius=0.4, width=1)
        tuft = hwisp(F, [(cx, bottom + 7.2), (cx - 0.4, bottom + 8.8), (cx + 0.2, bottom + 10.0)], 2.2, 0.0)
        part(img, tuft[0], HAIR, HAIR_LINE, radius=0.5, bias=0.05)
    part(img, shape, HAIR, HAIR_LINE, radius=1.6, bias=-0.08)
    # texture: strokes down the beard with its growth, a sheen along the jaw, strands in the ends
    for k in range(9 if big else 6):
        x = 12.4 + k * (15.2 / ((9 if big else 6) - 1)) + sx
        top = 26.0 if abs(x - 20) > 5 else 30.4
        length = (bottom - top - 1.6) * (0.55 + 0.35 * (k % 2))
        curve(img, [(x, top + hy), (x + (20 - x) * 0.06, top + length * 0.5 + hy), (x + (20 - x) * 0.12, top + length + hy)], HAIR[1], 1, shape)
    for s_ in (-1, 1):
        X = lambda x: (x if s_ < 0 else 40 - x) + sx
        curve(img, [(X(10.0), 21.0 + hy), (X(10.2), 24.6 + hy), (X(11.2), 28.0 + hy)], HAIR[4], 1, shape)
    hstrands(img, ends, shape, start=0.3)
    part(img, sm, HAIR, HAIR_LINE, radius=0.9, bias=0.16)              # the moustache its own shape over it
    for _, pts, w in stache:
        curve(img, [(x, y + 0.4) for x, y in pts[1:-1]], HAIR[1], 1, sm)
        curve(img, [(x - 0.2, y - 0.4) for x, y in pts[:3]], HAIR[4], 1, sm)
    if big:
        fill(img, ellipse([cx - 1.6, 28.6 + hy, cx + 1.6, 29.3 + hy]), grey(64))     # the mouth under it
    return img


# ---------------------------------------------------------------- garments

class Garment:
    """One garment's canvases for a frame: ``plain`` (drawn first by the game), ``over`` (plain
    parts lying over the cloth: merged into plain and cut from the dye), ``dye``, and the back
    panel's ``back``/``back_dye`` (drawn before the body, cut under the base)."""

    def __init__(self, F):
        self.F = F
        self.plain, self.over, self.dye, self.back, self.back_dye = blank(), blank(), blank(), blank(), blank()
        self.onarm = blank()                     # worn on the forearm itself (bracers): never cut by it
        self.arms_cut = F.front_arms()

    def body(self, m):
        """A torso part. (What a held arm or the coin covers is cut from the finished layers, so
        the outlines stay round the garment and don't ring the arm.)"""
        return m

    def finish(self, base_mask):
        cut(self.dye, alpha(self.over))
        self.plain.alpha_composite(self.over)
        for img in (self.plain, self.dye):
            cut(img, self.arms_cut)
        cut(self.dye, alpha(self.onarm))
        self.plain.alpha_composite(self.onarm)
        cut(self.back, base_mask)
        cut(self.back_dye, base_mask)
        return {"": self.plain, "_dye": self.dye, "_back": self.back, "_back_dye": self.back_dye}


def sleeve(F, side, upto, width=3.6):
    """A sleeve along an arm (a forearm held across in front of it is cut out at the finish)."""
    return F.arm_mask(side, upto, (width, width * 0.95, width * 0.9))


def trim_band(m, top, bottom):
    return both(m, rect([0, top, 40, bottom]))


def g_tunic(G, F, long=False, sleeves_to=0.42):
    by = F.by
    hem_y = (44.6 if F.male else 45) + (5 if long else 0)
    hem = [(13, hem_y), (14.5, hem_y + 1.6), (16, hem_y + 0.2), (18, hem_y + 1.8), (20, hem_y + 0.4), (22, hem_y + 1.8), (24, hem_y + 0.2), (25.5, hem_y + 1.6), (27, hem_y)]
    flare = 1.5 if not F.male else 0.8
    skirt = poly([(14.5, 38.8 + by), (25.5, 38.8 + by), (26 + flare, hem_y + by)] + [(x, y + by) for x, y in reversed(hem)] + [(14 - flare, hem_y + by)])
    body = G.body(union(F.torso(), skirt))
    sl = union(*(sleeve(F, s, sleeves_to) for s in (0, 1)))
    cloth = union(body, sl)
    part(G.dye, cloth, DYE, DYE_LINE, radius=1.6, tex=lambda x, y: bayer(x, y) * 0.04)
    for x, bend in ((16.5, -0.5), (19.6, 0.2), (22.8, 0.5)):
        curve(G.dye, [(x, 40.2 + by), (x + bend, 42.5 + by), (x + bend * 0.5, hem_y - 0.5 + by)], DYE[1], 2, body)
        curve(G.dye, [(x - 0.6, 40.6 + by), (x - 0.6 + bend, 42.6 + by)], DYE[3], 1, body)
    trim = trim_band(body, (hem_y - 1.1 + by) * 1, 60)
    trim = minus(trim, minus(trim, body))
    hem_band = both(body, minus(rect([0, hem_y - 1.2 + by, 40, 60]), nothing()))
    fill(G.over, both(hem_band, minus(body, poly([(10, 30 + by), (30, 30 + by), (30, hem_y - 1.2 + by)] + [(x, y + by - 1.3) for x, y in reversed(hem)] + [(10, hem_y - 1.2 + by)]))), WALNUT[3])
    for s in (0, 1):
        cuff = minus(sleeve(F, s, sleeves_to), sleeve(F, s, sleeves_to - 0.08))
        fill(G.over, cuff, WALNUT[3])
    if F.front:
        nx = 20 + F.t * 0.8
        neck = G.body(poly([(nx - 2.2, 31 + by), (nx + 2.2, 31 + by), (nx, 34.8 + by)]))
        fill(G.dye, neck, DYE[0], cloth)
        for k in range(3):
            y = 31.6 + by + k * 1.0
            curve(G.over, [(nx - 1.6 + k * 0.45, y), (nx + 1.6 - k * 0.45, y + 0.7)], LEATHER_DARK[1], 1, G.body(F.torso()))
            curve(G.over, [(nx + 1.6 - k * 0.45, y), (nx - 1.6 + k * 0.45, y + 0.7)], LEATHER_DARK[1], 1, G.body(F.torso()))
    belt(G, F)


def belt(G, F, y=37.4):
    by = F.by
    b = G.body(rect([13.6, y + by, 26.4, y + 2.2 + by]))
    part(G.over, b, LEATHER, INK, radius=0.6)
    for x in range(int(14.2 * K), int(26 * K), 4):
        fine(G.over, [(x, (y + by) * K + 3), (x + 2, (y + 2.2 + by) * K + 1)], LEATHER[4], b)
    if F.front:
        bx = 20 + F.t
        frame = G.body(minus(rect([bx - 1.6, y - 0.4 + by, bx + 1.6, y + 2.6 + by]), rect([bx - 0.8, y + 0.4 + by, bx + 0.8, y + 1.8 + by])))
        part(G.over, frame, GOLD, INK, radius=0.5, bias=0.25, width=1)


def g_kaftan(G, F):
    by = F.by
    hem_y = 50.5 if not F.male else 49.5
    coat = poly([(14.6, 31 + by), (25.4, 31 + by), (26.2, 39 + by), (28.4, hem_y + by), (11.6, hem_y + by), (13.8, 39 + by)])
    body = G.body(coat)
    sl = union(*(sleeve(F, s, 0.86, 3.4) for s in (0, 1)))
    cloth = union(body, sl)
    part(G.dye, cloth, DYE, DYE_LINE, radius=1.6, tex=lambda x, y: bayer(x, y) * 0.04)
    for x in (15.6, 24.4):
        curve(G.dye, [(x, 40 + by), (x + (-0.8 if x < 20 else 0.8), hem_y - 0.6 + by)], DYE[1], 2, body)
    if F.front:
        cx = 20 + F.t
        edge_ = G.body(strip([(cx - 2.6, 31 + by), (cx + 0.6, 36 + by), (cx + 1, 39 + by), (cx + 1.2, hem_y + by)], [1.4, 1.4, 1.4, 1.4], steps=4))
        fill(G.over, both(edge_, coat), WALNUT[3])
        for k in range(8):
            fine(G.over, [P(cx + 0.9, 40 + k * 1.2 + by)], GOLD[1], edge_)
    hem_band = both(body, rect([0, hem_y - 1.1 + by, 40, 60]))
    fill(G.over, hem_band, WALNUT[3])
    for s in (0, 1):
        fill(G.over, minus(sleeve(F, s, 0.86, 3.4), sleeve(F, s, 0.76, 3.4)), WALNUT[3])
    belt(G, F)


def g_apron_dress(G, F):
    by = F.by
    if F.front:
        apron = G.body(poly([(15, 32.6 + by), (25, 32.6 + by), (26, 39 + by), (27.6, 52.6 + by), (12.4, 52.6 + by), (14, 39 + by)]))
    else:
        apron = poly([(15, 32 + by), (25, 32 + by), (26, 39 + by), (27.6, 52.6 + by), (12.4, 52.6 + by), (14, 39 + by)])
    straps = union(strip([(15.6, 30.8 + by), (16.4, 33.4 + by)], [1.2, 1.2], steps=2), strip([(24.4, 30.8 + by), (23.6, 33.4 + by)], [1.2, 1.2], steps=2))
    cloth = union(apron, G.body(straps))
    part(G.dye, cloth, DYE, DYE_LINE, radius=1.6, tex=lambda x, y: bayer(x, y) * 0.04)
    for x in (16.6, 20, 23.4):
        curve(G.dye, [(x, 40 + by), (x + (x - 20) * 0.12, 52 + by)], DYE[1], 2, apron)
    fill(G.over, both(apron, rect([0, 51.4 + by, 40, 60])), WALNUT[3])
    if F.front:
        for bx in (16.2, 23.8):                                        # tortoise brooches
            br = G.body(ellipse([bx - 1.5, 32.4 + by, bx + 1.5, 34.8 + by]))
            part(G.over, br, BRONZE, INK, radius=0.5, bias=0.2, width=1)
            fine(G.over, [P(bx, 33.4 + by)], BRONZE[4], br)
        beads = mask(lambda d: d.arc([P(16.2, 32)[0], P(0, 31.4 + by)[1], P(23.8, 0)[0], P(0, 37.6 + by)[1]], 20, 160, fill=255, width=3))
        beads = G.body(beads)
        bb = beads.getbbox()
        if bb:
            px_ = beads.load()
            pts = [(x, y) for y in range(bb[1], bb[3]) for x in range(bb[0], bb[2]) if px_[x, y]]
            fine(G.over, [(x, y) for x, y in pts if (x // 3) % 2 == 0], BEAD_AMBER)
            fine(G.over, [(x, y) for x, y in pts if (x // 3) % 2 == 1], BEAD_BLUE)


def g_vest(G, F):
    by = F.by
    vest = G.body(poly([(14.8, 31 + by), (25.2, 31 + by), (26.4, 41 + by), (13.6, 41 + by)]))
    part(G.over, vest, DARK_LEATHER, INK, radius=1.2)
    if F.front:
        cx = 20 + F.t
        gap = G.body(poly([(cx - 1.6, 31 + by), (cx + 1.6, 31 + by), (cx + 0.6, 41 + by), (cx - 0.6, 41 + by)]))
        cut(G.over, gap)
        for k in range(5):
            y = 32.6 + k * 1.7 + by
            curve(G.over, [(cx - 1.4, y), (cx + 1.4, y + 0.6)], LEATHER[3], 1, G.body(rect([cx - 1.8, 31 + by, cx + 1.8, 41 + by])))
    for s in (0, 1):                                                    # bracers
        sh, el, ha, _ = F.arms[s]
        at = lambda k: lerp(el, ha, k)
        br = strip([at(0.2), at(0.55), at(0.85)], [3.4, 3.4, 3.2], steps=3)
        if F.back and F.arms[s][3]:
            br = minus(br, F.torso())
        part(G.onarm, br, LEATHER, INK, radius=0.8)
        for k in (0.35, 0.6):
            x, y = P(*at(k))
            line(G.onarm, (x - 4, y - 2), (x + 4, y + 2), LEATHER_DARK[0], br)
            line(G.onarm, (x - 4, y + 2), (x + 4, y - 2), LEATHER_DARK[0], br)


def g_trousers(G, F):
    by = F.by
    legs = [rect([F.leg(i)[0] - 0.3, 42 + by, F.leg(i)[0] + F.g["leg_w"] + 0.3, F.leg(i)[2] - 2.6]) for i in (0, 1)]
    seat = rect([14.4, 39 + by, 25.6, 43.6 + by])
    cloth = G.body(union(seat, *legs))
    part(G.dye, cloth, DYE, DYE_LINE, radius=1.0)
    for i in (0, 1):
        x0, top, foot, w = F.leg(i)
        curve(G.dye, [(x0 + 0.8, top + 1.5), (x0 + 0.7, foot - 5)], DYE[3], 2, legs[i])
        for k in range(3):                                              # leg wraps
            y = foot - 7.6 + k * 1.4
            line(G.over, P(x0 - 0.2, y), P(x0 + w + 0.2, y + 0.8), WRAP_C[1], legs[i])


def g_skirt(G, F):
    by = F.by
    skirt = poly([(14.4, 38.6 + by), (25.6, 38.6 + by), (27.8, 53.4 + by), (24, 54 + by), (20, 53.6 + by), (16, 54 + by), (12.2, 53.4 + by)])
    body = G.body(skirt)
    part(G.dye, body, DYE, DYE_LINE, radius=1.6, tex=lambda x, y: bayer(x, y) * 0.04)
    for x in (16, 19, 22, 25):
        curve(G.dye, [(x - 0.2, 41 + by), (x + (x - 20) * 0.1, 53 + by)], DYE[1], 2, body)
        curve(G.dye, [(x - 1, 42 + by), (x - 1 + (x - 20) * 0.08, 50 + by)], DYE[3], 1, body)
    fill(G.over, both(body, rect([0, 38.6 + by, 40, 39.6 + by])), WALNUT[2])


def g_shoes(G, F, boots):
    for i in (0, 1):
        x0, top, foot, w = F.leg(i)
        toe = 1 if F.t else (-1 if i == 0 else 1)
        sole = ellipse([x0 - 1 + min(0, toe), foot - 3, x0 + w + 1 + max(0, toe), foot + 0.6])
        if not boots:
            shoe = minus(sole, rect([0, 0, 40, foot - 2.4]))
            part(G.plain, shoe, SHOE_C, INK, radius=0.8)
            fill(G.plain, rect([x0 - 1.5, foot + 0.1, x0 + w + 2, foot + 0.6]), LEATHER_DARK[0], shoe)
            continue
        shaft = rect([x0 - 0.5, foot - 5, x0 + w + 0.5, foot - 1])
        boot = union(shaft, sole)
        part(G.plain, boot, LEATHER, INK, radius=1.2)
        fill(G.plain, rect([x0 - 1.5, foot + 0.1, x0 + w + 2.5, foot + 0.6]), LEATHER_DARK[0], boot)
        for y in (foot - 3.8, foot - 2.4):
            fill(G.plain, rect([x0 - 0.5, y, x0 + w + 0.5, y + 0.4]), LEATHER_DARK[1], boot)
            bx, byy = P(x0 + (w - 0.4 if i == 1 else 0.6), y + 0.2)
            fill(G.plain, minus(oval(bx, byy, 2.5, 2.5), oval(bx, byy, 1, 1)), GOLD[1], boot)
        cuff = ellipse([x0 - 1.2, foot - 6.8, x0 + w + 1.2, foot - 4.4])
        for k in range(4):
            fx = x0 - 0.8 + k * 1.5
            cuff = union(cuff, poly([(fx - 0.6, foot - 5), (fx + 0.8, foot - 5), (fx + 0.2, foot - 3.8)]))
        part(G.plain, cuff, FUR, INK, radius=0.8, bias=0.15)
        for k in range(4):
            fx = x0 - 0.5 + k * 1.4
            curve(G.plain, [(fx, foot - 6.2), (fx + 0.4, foot - 4.8)], FUR[1], 1, cuff)
            curve(G.plain, [(fx - 0.4, foot - 6.4), (fx - 0.1, foot - 5.6)], FUR[4], 1, cuff)


def g_cloak(G, F, hood=False):
    """A cape from the shoulders: over them in front (falling outside the arms), a ring pin; the
    rest hangs behind (the back panel), and from behind it covers the back."""
    by = F.by
    panel = poly([(13.4, 31 + by), (26.6, 31 + by), (30, 51.6 + by), (25, 52.4 + by), (20, 51.8 + by), (15, 52.4 + by), (10, 51.6 + by)])
    if F.front:
        caps = union(ellipse([10.6, 29.6 + by, 18.6, 35.4 + by]), ellipse([21.4, 29.6 + by, 29.4, 35.4 + by]))
        drapes = union(poly([(10.8, 32 + by), (13.2, 32 + by), (12.6, 50.8 + by), (9.8, 51.2 + by)]),
                       poly([(26.8, 32 + by), (29.2, 32 + by), (30.2, 51.2 + by), (27.4, 50.8 + by)]))
        front = G.body(union(caps, drapes))
        front = minus(front, F.face_rect())
        part(G.dye, front, DYE, DYE_LINE, radius=1.4)
        for x in (11.6, 28.4):
            curve(G.dye, [(x, 35 + by), (x + (-0.4 if x < 20 else 0.4), 50 + by)], DYE[1], 2, front)
        # the lining showing along each drape's inner edge, and a woven band at the hem
        for x0, x1 in ((12.6, 13.2), (26.8, 27.4)):
            lining = both(drapes, poly([(x0 - 0.2, 33 + by), (x1 + 0.2, 33 + by), (x1 + (0.2 if x0 > 20 else -0.4), 50.8 + by), (x0 + (0.4 if x0 > 20 else -0.6), 51 + by)]))
            fill(G.over, G.body(lining), LINEN[1])
        fill(G.over, both(front, rect([0, 49.8 + by, 40, 60])), WALNUT[3])
        pin = (24.8 + F.t * 0.5, 31.8 + by)
        ring = minus(ellipse([pin[0] - 1.4, pin[1] - 1.4, pin[0] + 1.4, pin[1] + 1.4]), ellipse([pin[0] - 0.6, pin[1] - 0.6, pin[0] + 0.6, pin[1] + 0.6]))
        part(G.over, ring, GOLD, INK, radius=0.4, bias=0.25, width=1)
        line(G.over, P(pin[0] - 1.8, pin[1] + 0.8), P(pin[0] + 1.8, pin[1] - 0.8), GOLD[1])
        part(G.back_dye, panel, DYE, DYE_LINE, radius=1.6, bias=-0.15)
        fill(G.back, both(panel, rect([0, 50.6 + by, 40, 60])), WALNUT[2])
        if hood:
            hd = minus(ellipse([12.6, 26.4 + by, 27.4, 33.4 + by]), F.face_rect())
            part(G.dye, hd, DYE, DYE_LINE, radius=1.0, bias=-0.1)
    else:
        part(G.dye, panel, DYE, DYE_LINE, radius=1.6)
        for x in (15, 20, 25):
            curve(G.dye, [(x, 34 + by), (x + (x - 20) * 0.15, 51 + by)], DYE[1], 2, panel)
            curve(G.dye, [(x - 1.2, 35 + by), (x - 1.2 + (x - 20) * 0.12, 48 + by)], DYE[3], 1, panel)
        fill(G.over, both(panel, rect([0, 50.6 + by, 40, 60])), WALNUT[3])
        for x in range(int(10.6 * K), int(29.6 * K), 6):
            fine(G.over, [(x, (51.2 + by) * K), (x + 1, (51.2 + by) * K - 1), (x + 2, (51.2 + by) * K)], GOLD[1], panel)
        if hood:
            hd = ellipse([13.2, 27 + by, 26.8, 37.4 + by])
            part(G.dye, hd, DYE, DYE_LINE, radius=1.2, bias=0.05)
            curve(G.dye, [(15, 30 + by), (20, 33.6 + by), (25, 30 + by)], DYE[0], 2, hd)


def g_fur_mantle(G, F):
    by, hy = F.by, F.hy
    d = 1 if F.t else 0
    collar = minus(ellipse([12, 27.4 + by, 28, 37.6 + by]), rect([0, 35 + by, 40, 60]))
    if F.front:
        vx = 20 + d * 1.2
        collar = minus(collar, poly([(vx - 2.6, 27 + by), (vx + 2.6, 27 + by), (vx + 0.4, 34.2 + by), (vx - 0.4, 34.2 + by)]))
    for k in range(10):
        x = 12.8 + k * 1.6
        if F.front and abs(x - 20 - d * 1.2) < 1.6:
            continue
        collar = union(collar, poly([(x - 1, 34.4 + by), (x + 1, 34.4 + by), (x + 0.2, 36 + (k % 3) * 0.5 + by)]))
    for s_ in (-1, 1):
        for x, y in ((13, 29.6), (14.4, 28.2), (15.9, 27.4)):
            X = 20 + s_ * (20 - x)
            collar = union(collar, poly([(X - 0.9, y + 1.2 + by), (X + 0.9, y + 1.2 + by), (X + s_ * 0.6, y - 1 + by)]))
    if F.front:
        collar = minus(collar, ellipse([10, 9 + hy, 30, 31 + hy]))
    collar = G.body(collar)
    shade(G.over, collar, FUR, radius=1.2, bias=0.1, tex=lambda x, y: bayer(x * 3, y) * 0.35)
    for k in range(10):
        x = 12.8 + k * 1.6
        curve(G.over, [(x - 0.3, 34 + by), (x + 0.1, 35.4 + (k % 3) * 0.4 + by)], FUR[4], 1, collar)
    for k in range(30):
        x = 12.6 + (k * 7.3) % 14.8
        y = 28.8 + (k * 3.1) % 5.4
        lean = -0.4 if x < 20 else 0.4
        curve(G.over, [(x, y + 1.1 + by), (x + lean, y + by)], FUR[0] if k % 2 else FUR[3], 1, collar)
    ink(G.over, collar, INK)
    if F.front:
        px = 20 + d * 1.2
        curve(G.over, [(px - 1.8, 31.2 + by), (px - 0.7, 32.8 + by)], LEATHER_DARK[0], 2)
        curve(G.over, [(px + 1.8, 31.2 + by), (px + 0.7, 32.8 + by)], LEATHER_DARK[0], 2)
        disc = G.body(ellipse([px - 1.8, 32.4 + by, px + 1.8, 36 + by]))
        part(G.over, disc, SILVER, INK, radius=0.6, bias=0.15, width=1)
        cx, cy = P(px, 34.2 + by)
        line(G.over, (cx, cy - 4), (cx, cy + 4), SILVER[0], disc)
        line(G.over, (cx, cy - 4), (cx - 3, cy - 1), SILVER[0], disc)
        line(G.over, (cx, cy - 4), (cx + 3, cy - 1), SILVER[0], disc)


def g_mantle(G, F):
    """A poncho (art/reference/chibi-therion.webp): off the shoulders in one sweep, short over the
    hands so they peek out under it, dipping to a point in front, folds running down to it."""
    by = F.by
    d = F.t * 0.6
    X = lambda x: x + d
    hem = [(9.8, 41.2), (11.6, 42.2), (13.4, 42.0), (15.2, 43.8), (17.2, 44.6), (19.0, 46.2), (20.4, 46.8),
           (21.8, 45.6), (23.6, 45.2), (25.4, 43.6), (27.0, 43.0), (28.6, 42.4), (30.2, 41.0)]
    outline = [(15.6, 29.6), (24.4, 29.6), (28.2, 31.6), (30.0, 35.0), (30.6, 39.6)] + list(reversed(hem)) + [(9.4, 39.6), (10.0, 35.0), (11.8, 31.6)]
    p = poly([(X(x), y + by) for x, y in outline])
    if F.front:
        cx = 20 + F.t
        p = minus(p, poly([(cx - 2.4, 29 + by), (cx + 2.4, 29 + by), (cx, 33.4 + by)]))
        p = minus(p, F.face_rect())
    p = G.body(p)
    part(G.dye, p, DYE, DYE_LINE, radius=1.8, bias=0.08, tex=lambda x, y: bayer(x, y) * 0.04)
    # folds: from the shoulders and the throat down toward the point, a lit edge beside each
    for top, bottom in (((12.6, 33.4), (15.6, 43.0)), ((16.4, 31.6), (18.6, 45.4)), ((23.2, 31.8), (21.6, 45.0)), ((27.2, 33.6), (25.0, 43.0))):
        mid = lerp(top, bottom, 0.5)
        pts = [(X(top[0]), top[1] + by), (X(mid[0] + 0.4), mid[1] + by), (X(bottom[0]), bottom[1] + by)]
        curve(G.dye, pts, DYE[1], 2, p)
        curve(G.dye, [(x - 0.7, y + 0.6) for x, y in pts[:-1]], DYE[3], 1, p)
    # the shadowed underside along the hem
    curve(G.dye, [(X(x), y - 0.8 + by) for x, y in hem[1:-1]], DYE[1], 2, p)
    if F.front:                                                         # a wooden toggle on a cord at the throat
        cx = 20 + F.t
        curve(G.over, [(cx - 1.6, 32.2 + by), (cx, 33.6 + by), (cx + 1.6, 32.2 + by)], LEATHER_DARK[0], 1)
        part(G.over, strip([(cx - 1.3, 33.8 + by), (cx + 1.3, 33.4 + by)], [1.1, 1.1], steps=2), WALNUT, INK, radius=0.4, width=1)


def g_scarf(G, F):
    """A thick cowl wound twice round the neck, high under the chin, one end left hanging."""
    by = F.by
    d = F.t * 0.8
    wrap = union(ellipse([13.0 + d, 27.4 + by, 27.0 + d, 34.6 + by]), rect([14.2 + d, 30 + by, 25.8 + d, 33.4 + by]))
    if F.front:
        wrap = minus(wrap, both(F.face_rect(), rect([0, 0, 40, 29.2 + by])))
        tail = strip([(24.2 + d, 32.6 + by), (25.4 + d, 35.6 + by), (25.0 + d, 39.0 + by)], [3.4, 3.2, 3.0], steps=4)
    else:
        tail = strip([(18.4, 31.4 + by), (17.8, 36 + by), (18.2, 40.6 + by)], [3.4, 3.2, 3.0], steps=4)
    sc = G.body(union(wrap, tail))
    part(G.dye, sc, DYE, DYE_LINE, radius=1.4, bias=0.1)
    for y0, sag in ((30.4, 2.0), (32.6, 1.8)):                         # the turns of the cowl
        pts = [(14.0 + d, y0 + by), (20 + d, y0 + sag + by), (26.0 + d, y0 + by)]
        curve(G.dye, pts, DYE[1], 2, wrap)
        curve(G.dye, [(x, y - 1.0) for x, y in pts], DYE[3], 1, wrap)
    ink(G.dye, minus(tail, dilate(wrap, 0)), DYE_LINE)
    fringe_ = both(tail, rect([0, 38.2 + by, 40, 60]))
    fill(G.over, fringe_, WALNUT[3])
    for k in range(3):
        x = (23.8 if F.front else 16.8) + d * (1 if F.front else 0) + k * 1.1
        curve(G.over, [(x, 38.4 + by), (x, 40.2 + by)], WALNUT[1], 1, fringe_)


def g_satchel(G, F):
    by = F.by
    if F.front:
        strap = G.body(strip([(25, 31 + by), (20, 35.6 + by), (15.6, 39.6 + by)], [1.3, 1.3, 1.3], steps=3))
        bag = rect([12.2, 39.4 + by, 16.4, 44 + by])
    else:
        strap = strip([(15, 31 + by), (20, 35.6 + by), (24.4, 39.6 + by)], [1.3, 1.3, 1.3], steps=3)
        bag = rect([23.6, 39.4 + by, 27.8, 44 + by])
    part(G.over, strap, LEATHER, INK, radius=0.5, width=1)
    part(G.over, bag, LEATHER, INK, radius=0.8)
    bx0 = 12.2 if F.front else 23.6
    flap = poly([(bx0, 39.4 + by), (bx0 + 4.2, 39.4 + by), (bx0 + 4.2, 41 + by), (bx0 + 2.1, 41.8 + by), (bx0, 41 + by)])
    part(G.over, flap, LEATHER_DARK, INK, radius=0.5, width=1)
    for m_, c in ((bag, LEATHER[4]), (flap, LEATHER[3])):
        rim = edge(m_, 4)
        rim = minus(rim, edge(m_, 3))
        bb = rim.getbbox()
        if bb:
            px_ = rim.load()
            fine(G.over, [(x, y) for y in range(bb[1], bb[3]) for x in range(bb[0], bb[2]) if px_[x, y] and (x + y) % 4 == 0], c)
    fill(G.over, oval(*P(bx0 + 2.1, 41.2 + by), 1.5, 1.5), BRONZE[3])
    for k in range(4):                                                  # the strap's stitching
        x, y = P(*lerp((25 if F.front else 15, 31.4 + by), (15.8 if F.front else 24.2, 39.2 + by), 0.15 + k * 0.22))
        fine(G.over, [(x, y)], LEATHER[4], strap)


def dome(G, F, colors, dark, top=2, brim=None):
    hy = F.hy
    cx = 20 + (F.t if F.front else F.t)
    d = minus(ellipse([cx - 12, top + hy, cx + 12, 28 + hy]), rect([0, 15 + hy, 40, 60]))
    part(G.plain, d, colors, INK, radius=2.4, bias=0.05)
    band = rect([cx - 12.5, 13 + hy, cx + 12.5, 16 + hy])
    part(G.plain, band, dark, INK, radius=1.0)
    curve(G.plain, [(cx - 12, 13.7 + hy), (cx + 12, 13.7 + hy)], dark[3], 2, band)
    gx, gy = P(cx - 7, 6 + hy)
    curve(G.plain, [(gx, gy + 6), (gx + 6, gy)], colors[4], 2, d, design=False)
    return cx, d, band


def g_helm(G, F, kind):
    if not F.pose.helm:
        return
    hy = F.hy
    if kind == "leather_cap":
        cx = 20 + F.t
        cap = minus(ellipse([cx - 11.6, 3 + hy, cx + 11.6, 27 + hy]), rect([0, 15 + hy, 40, 60]))
        part(G.plain, cap, LEATHER, INK, radius=2.2)
        for x in (cx - 4, cx + 4):
            curve(G.plain, [(x, 4.4 + hy), (x + (x - cx) * 0.4, 13 + hy)], LEATHER[1], 2, cap)
        brim = rect([cx - 12.4, 13.4 + hy, cx + 12.4, 16.4 + hy])
        for k in range(13):
            x = cx - 12 + k * 2
            brim = union(brim, poly([(x - 1, 16 + hy), (x + 1, 16 + hy), (x + 0.3, 17.4 + hy)]))
        part(G.plain, brim, FUR, INK, radius=0.8, bias=0.15)
        for k in range(12):
            x = cx - 11.4 + k * 2
            curve(G.plain, [(x, 14 + hy), (x + 0.4, 16.2 + hy)], FUR[1], 1, brim)
        return
    if kind == "horned_helm":
        cx = 20 + F.t
        for side in (0, 1):
            s = -1 if side == 0 else 1
            reach = 1.0 if not F.t else (1.08 if side == 0 else 0.82)
            spine = [(cx + s * 9, 12 + hy), (cx + s * 13.5 * reach, 9.4 + hy), (cx + s * 15.6 * reach, 5 + hy), (cx + s * 14.6 * reach, 1.6 + hy), (cx + s * 12.8 * reach, -0.4 + hy)]
            horn = strip(spine, [5.6, 4.8, 3.6, 2.0, 0.3], steps=6)
            part(G.plain, horn, HORN, INK, radius=1.0, bias=0.1)
            a = catmull([P(*q) for q in spine], 6)
            for k in (0.18, 0.3, 0.42):
                i = int(k * (len(a) - 1))
                (x0, y0), (x1, y1) = a[i], a[i + 1]
                n = math.hypot(x1 - x0, y1 - y0) or 1
                nx, ny = -(y1 - y0) / n, (x1 - x0) / n
                line(G.plain, (x0 - nx * 12, y0 - ny * 12), (x0 + nx * 12, y0 + ny * 12), HORN[1], horn)
            curve(G.plain, [spine[1], spine[2], spine[3]], HORN[4], 2, horn)
    colors, dark = {"iron_helm": (IRON, IRON_DARK), "horned_helm": (IRON, IRON_DARK), "bronze_helm": (BRONZE, ramp((120, 84, 44), 5)),
                    "steel_sallet": (STEEL, IRON_DARK)}[kind]
    cx, d, band = dome(G, F, colors, dark)
    for s in (-1, 1):
        curve(G.plain, [(cx + s * 6, 4.6 + hy), (cx + s * 8.4, 9 + hy), (cx + s * 9, 13.4 + hy)], colors[1], 2, d)
    for x in (cx - 10.4, cx - 7, cx + 7, cx + 10.4):
        rx, ry = P(x, 15 + hy)
        fill(G.plain, mask(lambda dd: dd.polygon([(rx, ry - 3), (rx + 3, ry), (rx, ry + 3), (rx - 3, ry)], fill=255)), colors[4])
    if F.front or F.view == "up":
        ridge = mask(lambda dd: dd.rectangle([cx * K - 2, (3 + hy) * K, cx * K + 4, (13 + hy) * K + 2], fill=255))
        part(G.plain, ridge, colors, INK, radius=0.4, bias=-0.05)
    if kind == "horned_helm" and F.front:
        point = poly([(cx - 4, 15.5 + hy), (cx + 4, 15.5 + hy), (cx, 19 + hy)])
        part(G.plain, point, dark, INK, radius=0.6)
        boss = poly([(cx, 9.6 + hy), (cx + 3, 14 + hy), (cx, 18.4 + hy), (cx - 3, 14 + hy)])
        part(G.plain, boss, dark, INK, radius=0.6)
        gem = poly([(cx, 11.4 + hy), (cx + 1.4, 14 + hy), (cx, 16.4 + hy), (cx - 1.4, 14 + hy)])
        shade(G.plain, gem, colors, radius=0.4, bias=0.25)
    if kind == "iron_helm":
        if F.front:
            nasal = rect([cx - 0.8, 15.6 + hy, cx + 0.8, 21.6 + hy])
            part(G.plain, nasal, IRON, INK, radius=0.4, width=1)
        mail_x = ((9.2, 11.8), (28.2, 30.8)) if F.front else ((9, 31),)
        for x0, x1 in mail_x:
            m = rect([x0 + F.t, 16 + hy, x1 + F.t, 25 + hy])
            shade(G.plain, m, MAIL, radius=0.6, tex=lambda x, y: 0.12 if (x // 2 + y // 2) % 2 else -0.12)
            ink(G.plain, m, INK, 1)
    if kind == "bronze_helm":
        comb = minus(ellipse([cx - 1.4, 1 + hy, cx + 1.4, 27 + hy]), rect([0, 13 + hy, 40, 60]))
        part(G.plain, comb, BRONZE, INK, radius=0.4, bias=0.25, width=1)
        curve(G.plain, [(cx - 0.4, 2.4 + hy), (cx - 0.4, 12 + hy)], BRONZE[4], 1, comb)
    if kind == "bronze_helm" and F.front:
        for s in (-1, 1):
            cheek = poly([(cx + s * 12, 15.6 + hy), (cx + s * 9.4, 15.6 + hy), (cx + s * 9.8, 23 + hy), (cx + s * 11.6, 24 + hy)])
            part(G.plain, cheek, BRONZE, INK, radius=0.6)
    if kind == "steel_sallet":
        # a broad brim all round: the near half in front of the dome, flaring past the cheeks
        brim = minus(ellipse([cx - 15, 11.8 + hy, cx + 15, 18.4 + hy]), ellipse([cx - 12, 8.6 + hy, cx + 12, 15.4 + hy]))
        part(G.plain, brim, STEEL, INK, radius=0.8, bias=0.12)
        top_edge = minus(edge(ellipse([cx - 12, 8.6 + hy, cx + 12, 15.6 + hy]), 2), rect([0, 0, 40, 13 + hy]))
        fill(G.plain, both(dilate(top_edge, 1), brim), STEEL[4])
        under = both(edge(ellipse([cx - 15, 11.8 + hy, cx + 15, 18.4 + hy]), 3), rect([0, 16.4 + hy, 40, 60]))
        fill(G.plain, both(under, brim), STEEL[0])
        for x in (cx - 12, cx - 6, cx + 6, cx + 12):                    # rivets round the brim
            rx, ry = P(x, 16.4 + hy + (0.6 if abs(x - cx) > 8 else 1.2))
            fill(G.plain, oval(rx, ry, 1.6, 1.6), STEEL[4], brim)


def g_armor(G, F, kind):
    by = F.by
    torso = poly([(14.4, 31 + by), (25.6, 31 + by), (26.4, 43.4 + by), (13.6, 43.4 + by)])
    t = G.body(torso)
    if kind in ("leather_jerkin", "studded_jerkin"):
        part(G.plain, t, DARK_LEATHER if kind == "studded_jerkin" else LEATHER, INK, radius=1.4)
        for s in (0, 1):
            pad = sleeve(F, s, 0.3, 4.2)
            part(G.plain, pad, LEATHER_DARK, INK, radius=0.8)
        for y in (34.4, 38):
            curve(G.plain, [(14.6, y + by), (25.4, y + by)], LEATHER_DARK[1], 1, t)
        if kind == "studded_jerkin":
            for row in range(3):
                for col in range(5):
                    x, y = 15.6 + col * 2.2, 32.6 + row * 3.4
                    fill(G.plain, oval(*P(x, y + by), 1.6, 1.6), IRON[3], t)
        belt(G, F, 40.6)
        G.plain.alpha_composite(G.over)
        G.over = blank()
    elif kind == "chain_shirt":
        m = union(t, *(sleeve(F, s, 0.5, 3.8) for s in (0, 1)), G.body(poly([(13.6, 43 + by), (26.4, 43 + by), (27, 46 + by), (13, 46 + by)])))
        shade(G.plain, m, MAIL, radius=1.2, tex=lambda x, y: 0.14 if (x // 2 + y // 2) % 2 else -0.14)
        ink(G.plain, m, INK)
        belt(G, F, 40.4)
        G.plain.alpha_composite(G.over)
        G.over = blank()
    elif kind in ("bronze_cuirass", "steel_plate"):
        colors = BRONZE if kind == "bronze_cuirass" else STEEL
        plate = G.body(poly([(14.6, 31 + by), (25.4, 31 + by), (25.8, 40.6 + by), (14.2, 40.6 + by)]))
        part(G.plain, plate, colors, INK, radius=1.6, bias=0.1)
        curve(G.plain, [(20 + F.t, 32 + by), (20 + F.t, 40 + by)], colors[1], 2, plate)
        curve(G.plain, [(16, 34.6 + by), (19.4, 35.8 + by)], colors[1], 1, plate)
        curve(G.plain, [(24, 34.6 + by), (20.6, 35.8 + by)], colors[1], 1, plate)
        skirt = nothing()
        for k in range(6):
            x = 14.4 + k * 1.95
            skirt = union(skirt, rect([x, 40.8 + by, x + 1.7, 45 + by]))
        skirt = G.body(skirt)
        if kind == "bronze_cuirass":
            part(G.plain, skirt, LEATHER, INK, radius=0.5, width=1)
        else:
            for y in (40.8, 42.4, 44):
                band = G.body(rect([14, y + by, 26, y + 1.4 + by]))
                part(G.plain, band, STEEL, INK, radius=0.4, width=1)
            for s in (0, 1):
                pauldron = sleeve(F, s, 0.32, 5)
                part(G.plain, pauldron, STEEL, INK, radius=0.8, bias=0.1)


GARMENTS = {
    "tunic": lambda G, F: g_tunic(G, F),
    "kaftan": g_kaftan,
    "apron_dress": g_apron_dress,
    "vest": g_vest,
    "trousers": g_trousers,
    "skirt": g_skirt,
    "turnshoes": lambda G, F: g_shoes(G, F, False),
    "boots": lambda G, F: g_shoes(G, F, True),
    "cloak": lambda G, F: g_cloak(G, F),
    "hooded_cloak": lambda G, F: g_cloak(G, F, hood=True),
    "fur_mantle": g_fur_mantle,
    "mantle": g_mantle,
    "scarf": g_scarf,
    "satchel": g_satchel,
    "leather_cap": lambda G, F: g_helm(G, F, "leather_cap"),
    "iron_helm": lambda G, F: g_helm(G, F, "iron_helm"),
    "bronze_helm": lambda G, F: g_helm(G, F, "bronze_helm"),
    "steel_sallet": lambda G, F: g_helm(G, F, "steel_sallet"),
    "horned_helm": lambda G, F: g_helm(G, F, "horned_helm"),
    "leather_jerkin": lambda G, F: g_armor(G, F, "leather_jerkin"),
    "studded_jerkin": lambda G, F: g_armor(G, F, "studded_jerkin"),
    "chain_shirt": lambda G, F: g_armor(G, F, "chain_shirt"),
    "bronze_cuirass": lambda G, F: g_armor(G, F, "bronze_cuirass"),
    "steel_plate": lambda G, F: g_armor(G, F, "steel_plate"),
}
HEAD_ITEMS = ("leather_cap", "iron_helm", "bronze_helm", "steel_sallet", "horned_helm")

DARK_LEATHER = ramp((92, 76, 74), 5)
WRAP_C = ramp((122, 118, 116), 4)
SHOE_C = ramp((112, 74, 48), 5)



# ---------------------------------------------------------------- held: weapons and tools in the working hand

WOOD = ramp((150, 104, 62), 5)
GRIP = ramp((96, 62, 44), 4)
STRING = (236, 226, 200, 255)
METAL = [grey(v) for v in DYE_GREYS]           # tinted by the item's tier (HumanLook.MetalTint)
HELD_SHAPES = ("sword", "longsword", "knife", "axe", "pick", "hammer", "mace", "club", "spear", "bow", "rod", "tongs", "saw", "chisel", "brush",
               "staff", "crossbow")


def held_layers(F, shape):
    """(plain, metal) images of a weapon or tool in the working hand: its axis runs along the forearm
    (elbow to hand), so it follows every pose and the work swing; the fist is cut out of its grip."""
    plain, metal = blank(), blank()
    if F.pose.anim in ("sleep", "downed") or F.pose.lying:
        return plain, metal
    sh, el, hand, _ = F.arms[F.role_side(0)]
    ux, uy = hand[0] - el[0], hand[1] - el[1]
    ul = math.hypot(ux, uy) or 1
    u = (ux / ul, uy / ul)
    n = (-u[1], u[0])
    if n[0] < 0:                                                      # the blade's side faces forward
        n = (-n[0], -n[1])
    Z, T = 1.3, 1.5                                                  # chibi weapons: bigger and bolder than life
    at = lambda d, side=0.0: (hand[0] + u[0] * d * Z + n[0] * side * Z, hand[1] + u[1] * d * Z + n[1] * side * Z)
    wstrip = lambda pts, ws, steps=4: strip(pts, [w * T for w in ws], steps=steps)

    def p(img, m, colors, radius=0.5, **kw):
        part(img, m, colors, INK, radius=radius, width=1, **kw)

    if shape in ("sword", "longsword", "knife"):
        blade = {"sword": 10.5, "longsword": 13.5, "knife": 4.2}[shape]
        guard_w = 3.0 if shape != "knife" else 1.6
        p(plain, wstrip([at(-1.4), at(0.8)], [1.0, 1.0], steps=2), GRIP)
        p(metal, ellipse([at(-1.8)[0] - 0.7 * T, at(-1.8)[1] - 0.7 * T, at(-1.8)[0] + 0.7 * T, at(-1.8)[1] + 0.7 * T]), METAL)
        b = wstrip([at(0.8), at(blade * 0.55), at(blade)], [1.5 if shape != "knife" else 1.2, 1.3, 0.2], steps=4)
        p(metal, b, METAL, bias=0.15)
        curve(metal, [at(1.2), at(blade * 0.85)], METAL[4], 1, b)               # the fuller's shine
        p(metal, wstrip([at(0.8, -guard_w / 2), at(0.8, guard_w / 2)], [0.8, 0.8], steps=2), METAL, bias=-0.1)
    elif shape in ("axe", "pick", "hammer", "mace", "club"):
        length = {"axe": 9, "pick": 9.5, "hammer": 8, "mace": 7.5, "club": 8}[shape]
        if shape == "club":
            p(plain, wstrip([at(-1), at(length * 0.5), at(length)], [1.2, 1.9, 2.8], steps=4), WOOD, radius=0.8)
            for d in (3.5, 6):
                fine(plain, [P(*at(d, 0.4))], WOOD[0])
        else:
            p(plain, wstrip([at(-1.4), at(length)], [1.0, 0.9], steps=2), WOOD)
        if shape == "axe":
            head = poly([at(length - 2.8, -0.4), at(length - 3.2, 2.8), at(length - 0.8, 4.4), at(length + 0.9, 3.2), at(length + 0.3, -0.4)])
            p(metal, head, METAL, bias=0.1)
            curve(metal, [at(length - 2.0, 3.0), at(length + 0.3, 3.1)], METAL[4], 1, head)
        elif shape == "pick":
            head = wstrip([at(length - 1.6, -3.6), at(length + 0.4, -0.6), at(length + 0.6, 0.6), at(length - 1.4, 3.8)], [0.9, 1.6, 1.6, 0.3], steps=6)
            p(metal, head, METAL, bias=0.1)
        elif shape == "hammer":
            head = poly([at(length - 1.0, -1.8), at(length + 1.2, -1.8), at(length + 1.2, 1.8), at(length - 1.0, 1.8)])
            p(metal, head, METAL, bias=0.12)
        elif shape == "mace":
            c = at(length + 0.6)
            head = union(ellipse([c[0] - 1.7 * Z, c[1] - 1.7 * Z, c[0] + 1.7 * Z, c[1] + 1.7 * Z]),
                         *[poly([at(length + 0.6, sgn * 1.4), at(length + 0.6 + dd, sgn * 2.4), at(length + 1.2 + dd, sgn * 1.4)]) for sgn in (-1, 1) for dd in (-0.6, 0.4)])
            p(metal, head, METAL, bias=0.1)
    elif shape == "spear":
        p(plain, wstrip([at(-7), at(12)], [0.9, 0.8], steps=2), WOOD)
        head = wstrip([at(11.4), at(13.2), at(15.6)], [1.2, 1.8, 0.2], steps=4)
        p(metal, head, METAL, bias=0.12)
    elif shape == "bow":
        tips = (at(-6.5, 0.6), at(6.5, 0.6))
        limb = wstrip([tips[0], at(-3.2, 2.0), at(0, 2.4), at(3.2, 2.0), tips[1]], [0.5, 1.0, 1.2, 1.0, 0.5], steps=6)
        p(plain, limb, WOOD)
        line(plain, P(*tips[0]), P(*tips[1]), STRING)
        p(plain, wstrip([at(-0.9, 2.3), at(0.9, 2.3)], [1.4, 1.4], steps=2), GRIP, radius=0.3)
    elif shape == "staff":                                                    # a long walking staff, a knob at its head
        p(plain, wstrip([at(-6), at(13)], [1.0, 1.0], steps=2), WOOD)
        c = at(13.6)
        p(plain, ellipse([c[0] - 1.3 * Z, c[1] - 1.3 * Z, c[0] + 1.3 * Z, c[1] + 1.3 * Z]), GRIP, radius=0.6)
    elif shape == "crossbow":                                                 # the stock along the forearm, the bow square across its head
        p(plain, wstrip([at(-3), at(7)], [1.4, 1.1], steps=2), WOOD)
        tips = (at(6.2, -4.2), at(6.2, 4.2))
        line(plain, P(*tips[0]), P(*at(2.6)), STRING)
        line(plain, P(*tips[1]), P(*at(2.6)), STRING)
        p(metal, wstrip([tips[0], at(7.2), tips[1]], [0.5, 0.9, 0.5], steps=5), METAL)
        p(metal, wstrip([at(0.6, -0.9), at(0.6, -2.0)], [0.6, 0.6], steps=2), METAL)       # the latch
    elif shape == "rod":
        rod = wstrip([at(-1.5), at(8), at(16, -1.0)], [0.9, 0.6, 0.3], steps=5)
        p(plain, rod, WOOD)
        tip = at(16, -1.0)
        curve(plain, [tip, (tip[0] + 0.6, tip[1] + 3), (tip[0] + 0.2, tip[1] + 6)], STRING, 1)
    elif shape == "tongs":
        for sgn in (-1, 1):
            p(metal, wstrip([at(-1, sgn * 0.6), at(3, sgn * 0.5), at(6, sgn * 0.15)], [0.6, 0.6, 0.4], steps=3), METAL)
        p(plain, wstrip([at(-1.6), at(1.2)], [1.4, 1.3], steps=2), GRIP)                  # the leather-wrapped handles
    elif shape == "saw":
        p(plain, wstrip([at(-1.2), at(1.4)], [1.4, 1.4], steps=2), WOOD)
        blade = poly([at(1.4, -1.3), at(7, -0.8), at(7, 0.8), at(1.4, 1.3)])
        p(metal, blade, METAL, bias=0.1)
        for k in range(6):
            d = 1.8 + k * 0.9
            fine(metal, [P(*at(d, 1.2))], INK, blade)
    elif shape == "chisel":
        p(plain, wstrip([at(-1.4), at(1.8)], [1.2, 1.1], steps=2), WOOD)
        p(metal, wstrip([at(1.8), at(4.4)], [0.8, 0.5], steps=2), METAL)
    elif shape == "brush":
        p(plain, wstrip([at(-1.2), at(2.4)], [1.6, 1.6], steps=2), WOOD)
        p(plain, wstrip([at(2.4), at(3.4)], [1.8, 1.8], steps=2), ramp((214, 198, 160), 3))
    # the fist goes over the grip: the item passes behind the fingers
    fist = ellipse([hand[0] - 1.6, hand[1] - 1.4, hand[0] + 1.6, hand[1] + 1.6])
    cut(plain, fist)
    cut(metal, fist)
    return plain, metal

# ---------------------------------------------------------------- frames to sheets

FRAMES, TAGS = sequence()


def lie(img, pose):
    """In bed (seen from above): the figure flat, raised toward the middle of the frame."""
    if not pose.lying:
        return img
    out = blank()
    out.alpha_composite(img, (0, -5 * K))
    return out


def base_mask(body, pose):
    return alpha(base_layer(Fig(body, pose)))


def both_bases(pose):
    """Where both bodies' bases are: what body-independent layers (the hair's back half) cut under."""
    return both(base_mask("male", pose), base_mask("female", pose))


def job_frames(job):
    kind, args = job
    out = []
    for pose, _ in FRAMES:
        if kind == "base":
            img = base_layer(Fig(args, pose))
        elif kind == "skin":
            body, folk = args
            img = skin_layer(Fig(body, pose), folk)
        elif kind == "ears":
            body, folk = args
            img = ears_layer(Fig(body, pose), folk)
        elif kind == "eyes":
            body, style, which = args
            img = eyes_layers(Fig(body, pose), style)[0 if which == "eyes" else 1]
        elif kind == "hair":
            style, which = args
            front, back = hair_layers(Fig("female", pose), style)
            if which == "back":
                cut(back, both_bases(pose))
                img = back
            else:
                img = front
        elif kind == "beard":
            img = beard_layer(Fig("female", pose), args)
        elif kind == "worn":
            item, body, part_ = args
            F = Fig(body, pose)
            G = Garment(F)
            if not (pose.anim == "sleep" and item in HEAD_ITEMS):
                GARMENTS[item](G, F)
            img = G.finish(base_mask(body, pose))[part_]
        elif kind == "held":
            shape, body, part_ = args
            F = Fig(body, pose)
            plain, metal = held_layers(F, shape)
            img = metal if part_.endswith("metal") else plain
            back_view = not F.front
            if part_.startswith("_back") != back_view:                # front views: over the body; back views: behind it
                img = blank()
            elif back_view:
                cut(img, base_mask(body, pose))
        out.append(lie(img, pose))
    return out


def jobs():
    """(sheet name, job) for every sheet."""
    out = []
    for body in BODIES:
        out.append((f"creatures/person_{body}", ("base", body)))
        out.append((f"creatures/person_{body}_skin", ("skin", (body, "human"))))
        for folk in FOLK[1:]:
            out.append((f"creatures/person_{body}_skin_{folk}", ("skin", (body, folk))))
            out.append((f"creatures/person_{body}_ears_{folk}", ("ears", (body, folk))))
        for style in EYE_STYLES:
            out.append((f"creatures/person_{body}_eyes_{style}", ("eyes", (body, style, "eyes"))))
            out.append((f"creatures/person_{body}_face_{style}", ("eyes", (body, style, "face"))))
        for item in GARMENTS:
            for part_ in ("", "_dye", "_back", "_back_dye"):
                out.append((f"apparel/{item}{part_}_{body}", ("worn", (item, body, part_))))
        for shape in HELD_SHAPES:
            for part_ in ("", "_metal", "_back", "_back_metal"):
                out.append((f"apparel/held_{shape}{part_}_{body}", ("held", (shape, body, part_))))
    for style in HAIR_STYLES:
        out.append((f"creatures/person_hair_{style}", ("hair", (style, "front"))))
        out.append((f"creatures/person_hair_{style}_back", ("hair", (style, "back"))))
    for style in BEARDS:
        out.append((f"creatures/person_beard_{style}", ("beard", style)))
    return out


def run(item):
    name, job = item
    frames = job_frames(job)
    path = os.path.join(A_ROOT, name)
    is_base = name.startswith("creatures/person_") and name.count("_") == 1
    if not is_base and not any(f.getbbox() for f in frames):
        for ext in (".png", ".anim.json"):
            if os.path.exists(path + ext):
                os.remove(path + ext)
        return name, False
    ms = [m for _, m in FRAMES]
    write_sheet(name, [(img, m) for img, m in zip(frames, ms)], TAGS, PIVOT, trim=True, density=K)
    if name == "creatures/person_male":                                     # the def's own sprite: a plain villager
        write_sheet("creatures/human", [(img, m) for img, m in zip(frames, ms)], TAGS, PIVOT, trim=True, force=True, density=K)
    return name, True


A_ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "data", "textures"))


def generate(only=None, processes=None):
    todo = [j for j in jobs() if only is None or only in j[0]]
    with Pool(processes) as pool:
        results = pool.map(run, todo, chunksize=1)
    written = sum(1 for _, ok in results if ok)
    print(f"people: wrote {written} sheets ({len(todo) - written} empty), {len(FRAMES)} frames each")


# ---------------------------------------------------------------- previews

TINTS = {"skin": (238, 196, 160, 255), "hair": (226, 168, 84, 255), "eyes": (110, 140, 220, 255), "dye": (160, 72, 64, 255)}


def tint(img, rgb):
    a = img.getchannel("A")
    out = ImageChops.multiply(img, Image.new("RGBA", img.size, rgb))
    out.putalpha(a)
    return out


def figure(body, pose, hair="long", eyes="round", beard=None, wear=(), dyes=None, colors=None, folk="human"):
    """A whole person for one frame, layered as the game does (HumanLook.Layers)."""
    colors = dict(TINTS, **(colors or {}))
    dyes = dyes or {}
    F = Fig(body, pose)
    hairc = tuple(min(255, int(v * 1.3)) for v in colors["hair"][:3]) + (255,)
    order = []
    worn = {}
    for item in wear:
        G = Garment(F)
        if not (pose.anim == "sleep" and item in HEAD_ITEMS):
            GARMENTS[item](G, F)
        worn[item] = G.finish(base_mask(body, pose))
    img = base_layer(F)
    front, back = hair_layers(Fig("female", pose), hair)
    cut(back, both_bases(pose))
    dye_of = lambda item: tuple(min(255, int(v * 1.275)) for v in dyes.get(item, colors["dye"])[:3]) + (255,)
    for item in wear:
        if item in ("cloak", "hooded_cloak", "scarf"):
            order += [worn[item]["_back"], tint(worn[item]["_back_dye"], dye_of(item))]
    order.append(tint(back, hairc))
    order.append(tint(skin_layer(F, folk), colors["skin"]))
    e, f = eyes_layers(F, eyes)
    order += [tint(e, colors["eyes"]), f]
    slots = ["trousers", "skirt", "turnshoes", "boots", "tunic", "kaftan", "apron_dress", "vest", "leather_jerkin", "studded_jerkin",
             "chain_shirt", "bronze_cuirass", "steel_plate", "satchel", "cloak", "hooded_cloak", "fur_mantle", "mantle", "scarf"]
    for item in slots:
        if item in wear:
            order += [worn[item][""], tint(worn[item]["_dye"], dye_of(item))]
    if beard:
        order.append(tint(beard_layer(Fig("female", pose), beard), hairc))
    order.append(tint(front, hairc))
    order.append(tint(ears_layer(F, folk), colors["skin"]))
    for item in HEAD_ITEMS:
        if item in wear:
            order.append(worn[item][""])
    for layer in order:
        img.alpha_composite(layer)
    return lie(img, pose)


def preview(path, looks, tags=("idle_down", "idle_downside", "idle_up"), scale=1, bg=(96, 120, 80, 255)):
    imgs = []
    for look in looks:
        for tag in tags:
            name, _, k = tag.partition("+")
            pose = FRAMES[TAGS[name][0] + int(k or 0)][0]
            canvas = Image.new("RGBA", (W, H), bg)
            canvas.alpha_composite(figure(pose=pose, **look))
            imgs.append(canvas)
    contact_sheet(imgs, path, scale=scale, columns=len(tags) * 2, bg=(40, 40, 40, 255), pad=2)


# ---------------------------------------------------------------- a true three-quarter turn (test)
# The turned views today slide the face a little toward the far cheek. A real three-quarter view
# turns the whole head: drawn facing us and from behind, the head and its hair are wrapped round a
# cylinder and turned by ``psi`` degrees about it (0 faces us, 180 faces away), so the face slides
# and foreshortens, the far eye narrows, and the side and back of the hair come round on the near
# side. Hair behind the body turns too but stays behind it; the body keeps its turned pose.

TURN_R = 13.0     # the cylinder the head and hair wrap round (design units: wide enough for the hair)


def turn_warp(front, back, psi, cx=20.0, chin=None):
    """Turn a head drawn facing us (``front``) and from behind (``back``, or None) by ``psi``
    degrees. Each row wraps round its own cylinder, as wide as the head and hair are there, so the
    silhouette keeps its width and nothing is cut off; columns past the turn come from behind."""
    import numpy as np
    fa = np.asarray(front)
    ba = np.asarray(back) if back is not None else np.zeros_like(fa)
    h, w = fa.shape[:2]
    c = cx * K
    xs = (np.arange(w) + 0.5) - c
    solid = (fa[:, :, 3] > 0) | (ba[:, :, 3] > 0)
    reach = np.where(solid, np.abs(xs)[None, :], 0).max(axis=1) + 1.0         # each row's half width...
    win = 4 * K                                                                # ...eased over its neighbours, so the
    padded = np.pad(reach, win, mode="edge")                                   # cylinders vary gently down the head
    reach = np.array([padded[i:i + 2 * win + 1].max() for i in range(h)])
    reach = np.convolve(np.pad(reach, win, mode="edge"), np.ones(2 * win + 1) / (2 * win + 1), mode="valid")
    R = reach[:, None]
    inside = np.abs(xs)[None, :] < R
    phi = np.arcsin(np.clip(xs[None, :] / R, -1, 1))
    turn = np.full((h, 1), math.radians(psi))
    if chin is not None:                                                       # the jaw pivots on the neck: the turn
        y = (np.arange(h)[:, None] + 0.5) / K                                  # eases off down the face to a third at the chin
        f = np.clip((y - (chin - 9.0)) / 9.0, 0.0, 1.0)
        turn = turn * (1.0 - 0.67 * f * f * (3 - 2 * f))
    a = phi - turn
    a = (a + math.pi) % (2 * math.pi) - math.pi
    front_side = np.abs(a) <= math.pi / 2
    fx = np.clip(np.floor(c + R * np.sin(a)).astype(int), 0, w - 1)
    bx = np.clip(np.floor(c - R * np.sin(a)).astype(int), 0, w - 1)
    rows = np.broadcast_to(np.arange(h)[:, None], (h, w))
    out = np.where((inside & front_side)[:, :, None], fa[rows, fx], 0)
    if back is not None:
        out = np.where((inside & ~front_side)[:, :, None], ba[rows, bx], out)
    img = Image.fromarray(out.astype(np.uint8), "RGBA")
    # Specks the wrap's steep edge picks up: keep only what lies by the main mass (its outline and
    # flyaways stay), dropping loose pixels off on their own.
    solid_ = img.getchannel("A").point(lambda v: 255 if v else 0)
    core = solid_.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(5))
    near = core.filter(ImageFilter.MaxFilter(9))
    img.putalpha(ImageChops.multiply(img.getchannel("A"), near))
    return img


def head_only(img, pose):
    """The part of a skin layer above the neck: the head and its ears."""
    keep = Image.new("L", img.size, 0)
    keep.paste(255, (0, 0, img.width, round((31.5 + pose.head_dy + pose.body_dy) * K)))
    out = img.copy()
    out.putalpha(ImageChops.multiply(img.getchannel("A"), keep))
    return out


def turned_figure(body, psi, hair="long", eyes="round", beard=None, wear=("tunic", "trousers", "turnshoes"), colors=None):
    """A person with the head truly turned (``psi``: 30 toward us and to the right, 150 away), the
    body in the matching turned pose."""
    colors = dict(TINTS, **(colors or {}))
    hairc = tuple(min(255, int(v * 1.3)) for v in colors["hair"][:3]) + (255,)
    toward = psi < 90
    pose_t = FRAMES[TAGS["idle_downside" if toward else "idle_upside"][0]][0]
    pose_f = FRAMES[TAGS["idle_down"][0]][0]
    pose_b = FRAMES[TAGS["idle_up"][0]][0]
    Ft, Ff, Fb = Fig(body, pose_t), Fig(body, pose_f), Fig(body, pose_b)
    hf_front, hf_behind = hair_layers(Fig("female", pose_f), hair)
    hb_front, _ = hair_layers(Fig("female", pose_b), hair)
    head_f = tint(head_only(skin_layer(Ff), pose_f), colors["skin"])
    e, f = eyes_layers(Ff, eyes)
    head_f.alpha_composite(tint(e, colors["eyes"]))
    head_f.alpha_composite(f)
    if beard:
        head_f.alpha_composite(tint(beard_layer(Fig("female", pose_f), beard), hairc))
    head_f.alpha_composite(tint(hf_front, hairc))
    head_b = tint(head_only(skin_layer(Fb), pose_b), colors["skin"])
    head_b.alpha_composite(tint(hb_front, hairc))
    behind = tint(hf_behind, hairc)
    cut(behind, both_bases(pose_f))
    # the body in its turned pose, without its own head
    body_img = base_layer(Ft)
    skin = tint(skin_layer(Ft), colors["skin"])
    neck = round((31.5 + pose_t.head_dy + pose_t.body_dy) * K)
    skin_body = skin.copy()
    skin_body.paste((0, 0, 0, 0), (0, 0, skin.width, neck))
    layers = [turn_warp(behind, None, psi), skin_body]
    for item in ("trousers", "turnshoes", "tunic"):
        if item in wear:
            G = Garment(Ft)
            GARMENTS[item](G, Ft)
            done = G.finish(base_mask(body, pose_t))
            layers += [done[""], tint(done["_dye"], colors["dye"])]
    chin = 31.2 + pose_f.head_dy + pose_f.body_dy
    head = turn_warp(head_f, head_b, psi, chin=chin) if toward else turn_warp(head_b, head_f, psi - 180, chin=chin)
    layers.append(head)
    for layer in layers:
        body_img.alpha_composite(layer)
    return body_img


def turn_preview(path, styles=("short", "long", "ponytail", "braidedponytail")):
    """Each style: today's turned views beside the true turn, toward us and away."""
    imgs = []
    colors = [(118, 82, 52, 255), (60, 44, 36, 255), (196, 150, 90, 255), (150, 70, 46, 255)]
    for i, h in enumerate(styles):
        body = "female" if i % 2 else "male"
        look = dict(body=body, hair=h, colors={"hair": colors[i % len(colors)]})
        for tag, psi in (("idle_downside", 30), ("idle_upside", 150)):
            for new in (False, True):
                canvas = Image.new("RGBA", (W, H), (96, 120, 80, 255) if not new else (106, 132, 88, 255))
                if new:
                    canvas.alpha_composite(turned_figure(psi=psi, **look))
                else:
                    canvas.alpha_composite(figure(pose=FRAMES[TAGS[tag][0]][0], wear=("tunic", "trousers", "turnshoes"), **look))
                imgs.append(canvas.crop((0, 0, W, H * 2 // 3)))
    contact_sheet(imgs, path, scale=1, columns=4, bg=(40, 40, 40, 255), pad=2)


def hair_preview(path, styles=None, beards=True):
    """Every hair style (a row each, in a natural colour on a plain tunic) in four views, then every
    beard: to judge the hair side by side. Two sizes: the detail at 1x, and the game's 1/4 beside it."""
    styles = styles or HAIR_STYLES
    colors = [(118, 82, 52, 255), (60, 44, 36, 255), (196, 150, 90, 255), (150, 70, 46, 255)]
    views = ("idle_down", "idle_downside", "idle_upside", "idle_up")
    rows = [dict(body="female" if i % 2 else "male", hair=h, colors={"hair": colors[i % len(colors)]}) for i, h in enumerate(styles)]
    if beards:
        rows += [dict(body="male", hair="short", beard=b, colors={"hair": colors[i % len(colors)]}) for i, b in enumerate(BEARDS)]
    imgs = []
    for look in rows:
        for tag in views:
            pose = FRAMES[TAGS[tag][0]][0]
            canvas = Image.new("RGBA", (W, H), (96, 120, 80, 255))
            canvas.alpha_composite(figure(pose=pose, wear=("tunic", "trousers", "turnshoes"), **look))
            imgs.append(canvas.crop((0, 0, W, H * 2 // 3)))
        small = Image.new("RGBA", (W, H * 2 // 3), (96, 120, 80, 255))
        game = imgs[-4].resize((W // 4, H // 6), Image.BOX).resize((W // 2, H // 3), Image.NEAREST)
        small.alpha_composite(game, ((W - game.width) // 2, (H * 2 // 3 - game.height) // 2))
        imgs.append(small)
    contact_sheet(imgs, path, scale=1, columns=5, bg=(40, 40, 40, 255), pad=2)


if __name__ == "__main__":
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    if "--turn-test" in sys.argv:
        out = os.path.join(os.path.dirname(__file__), "..", ".cache", "people", "turn.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        pick = [a for a in sys.argv[sys.argv.index("--turn-test") + 1:] if not a.startswith("--")]
        turn_preview(out, tuple(pick) or ("short", "long", "ponytail", "braidedponytail"))
        print(out)
        sys.exit(0)
    if "--hair-preview" in sys.argv:
        out = os.path.join(os.path.dirname(__file__), "..", ".cache", "people")
        os.makedirs(out, exist_ok=True)
        pick = [a for a in sys.argv[sys.argv.index("--hair-preview") + 1:] if not a.startswith("--")]
        hair_preview(os.path.join(out, "hair.png"), pick or None, beards=not pick)
        print(os.path.join(out, "hair.png"))
        sys.exit(0)
    if "--preview" in sys.argv:
        out = os.path.join(os.path.dirname(__file__), "..", ".cache", "people")
        os.makedirs(out, exist_ok=True)
        preview(os.path.join(out, "looks.png"), [
            dict(body="female", hair="long", eyes="round", wear=("tunic", "trousers", "boots", "fur_mantle", "horned_helm"), colors={"dye": (150, 156, 170, 255)}),
            dict(body="male", hair="short", eyes="narrow", beard="short", wear=("tunic", "trousers", "turnshoes")),
            dict(body="female", hair="long", eyes="bright", folk="elf", wear=("kaftan", "trousers", "boots"),
                 colors={"skin": (226, 214, 196, 255), "hair": (214, 214, 220, 255), "eyes": (96, 176, 140, 255)}),
            dict(body="male", hair="swept", eyes="narrow", folk="elf", wear=("tunic", "trousers", "boots", "cloak"),
                 colors={"skin": (150, 116, 96, 255), "hair": (40, 36, 44, 255), "eyes": (196, 160, 72, 255), "dye": (60, 96, 70, 255)}),
        ])
    else:
        generate(only)
