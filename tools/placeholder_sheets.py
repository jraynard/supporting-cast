"""Procedural placeholder sprite *sheets* (animated), in the same format tools/export_art.py writes.

Each sheet is a horizontal strip PNG plus a sibling .anim.json manifest:
    { "frameSize": [w, h], "pivot": [x, y], "points": {"mouth": [x, y]},
      "frames": [{"x","y","w","h","ox","oy","ms"}...],
      "tags": {"fly": {"from": 0, "to": 5, "loop": true, "event": -1}} }

The dragon follows the design brief in docs/ART.md: an original upright, heavy-set dragon in
charcoal and rust with bone horns/belly and glowing ember veins; never a copy of an existing
creature. Hand-made Aseprite art exported to the same names replaces these files.
"""
import json
import math
import random
import os
from PIL import Image, ImageDraw

from norse_art import nordic_finish, relight, selective_outline

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")

# ---------------------------------------------------------------- palette (see docs/ART.md)
OUTLINE = (30, 14, 12, 255)
SCALE_DARK = (58, 40, 42, 255)      # charcoal: spine plates, far limbs
SCALE = (124, 50, 36, 255)          # rust
SCALE_LIGHT = (160, 74, 48, 255)
BELLY = (214, 196, 160, 255)        # bone
BELLY_DARK = (168, 146, 116, 255)
HORN = (228, 216, 190, 255)
MEMBRANE = (92, 34, 30, 255)
MEMBRANE_DARK = (64, 24, 24, 255)
SPAR = (150, 118, 98, 255)
GLOW = (255, 140, 40, 255)
GLOW_HI = (255, 224, 110, 255)
EYE = (255, 214, 80, 255)



# ---------------------------------------------------------------- sheet writing

_HAND_DRAWN = None


def hand_drawn():
    """Sheets a file in art/ provides (a layered look's sheet-named layers, or a whole file's
    sheet): exported by tools/export_art.py, so the generator must not overwrite them.
    DW_REGEN_HAND_DRAWN=1 regenerates them anyway, to refresh a file nobody has painted yet
    (then rerun tools/make_look_aseprite.py)."""
    global _HAND_DRAWN
    if _HAND_DRAWN is None and os.environ.get("DW_REGEN_HAND_DRAWN") == "1":
        _HAND_DRAWN = set()
    if _HAND_DRAWN is None:
        import aseprite_file
        art = os.path.join(os.path.dirname(__file__), "..", "art")
        names = set()
        for root, _, files in os.walk(art):
            for f in files:
                if f.endswith((".aseprite", ".ase")):
                    src = os.path.join(root, f)
                    try:
                        layers = [n for n in aseprite_file.layer_names(src) if "/" in n]
                    except (ValueError, OSError, KeyError, IndexError):
                        layers = []
                    names.update(layers or [os.path.splitext(os.path.relpath(src, art))[0].replace(os.sep, "/")])
        _HAND_DRAWN = names
    return _HAND_DRAWN


def write_sheet(name, frames, tags, pivot, points=None, ms=100, trim=False, force=False, root=None, density=1):
    """frames: list of (Image, ms or None). tags: {name: (from, to, loop, event)}.

    trim: crop each frame to its opaque pixels (offset kept in ox/oy, as Aseprite's --trim
    export does), for sparse layers like a dragon's eyes that would waste atlas space.
    Sheets that hand-drawn art in art/ provides are left alone (``hand_drawn``) unless ``force``
    (the 3D pipeline, tools/sprites_from_renders.py, writes them on purpose). ``root`` overrides
    data/textures. ``density`` is sheet pixels per world pixel: 2 for art drawn at twice the detail
    (the game draws it at the same size)."""
    if name in hand_drawn() and not force:
        return
    w, h = frames[0][0].size
    crops = []
    for img, dur in frames:
        box = img.getbbox() if trim else (0, 0, w, h)
        if box is None:
            box = (0, 0, 1, 1)  # an empty frame still needs a pixel
        crops.append((img.crop(box), box, dur))
    sheet = Image.new("RGBA", (sum(c.size[0] for c, _, _ in crops), max(c.size[1] for c, _, _ in crops)), (0, 0, 0, 0))
    entries = []
    x = 0
    for img, box, dur in crops:
        sheet.paste(img, (x, 0))
        entries.append({"x": x, "y": 0, "w": img.size[0], "h": img.size[1], "ox": box[0], "oy": box[1], "ms": dur or ms})
        x += img.size[0]
    path = os.path.join(root or ROOT, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sheet.save(path)
    manifest = {
        "frameSize": [w, h],
        "pivot": list(pivot),
        "points": {k: list(v) for k, v in (points or {}).items()},
        "frames": entries,
        "tags": {t: {"from": a, "to": b, "loop": loop, "event": ev} for t, (a, b, loop, ev) in tags.items()},
    }
    if density != 1:
        manifest["density"] = density
    with open(os.path.join(root or ROOT, name + ".anim.json"), "w") as f:
        json.dump(manifest, f, indent=1)
        f.write("\n")


class Sequencer:
    """Collects frames and records tag ranges as they're added."""

    def __init__(self):
        self.frames = []
        self.tags = {}

    def add(self, tag, images, ms, loop=True, event=-1):
        start = len(self.frames)
        for img in images:
            self.frames.append((img, ms))
        self.tags[tag] = (start, len(self.frames) - 1, loop, event)


def outline(img, color):
    """Selective-free simple outline: every transparent pixel touching an opaque one."""
    w, h = img.size
    src = img.load()
    out = img.copy()
    dst = out.load()
    for y in range(h):
        for x in range(w):
            if src[x, y][3] != 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and src[nx, ny][3] > 0 and src[nx, ny] != color:
                    dst[x, y] = color
                    break
    return out


# ---------------------------------------------------------------- dragon

class DragonPen:
    """Draws in the adult's 96x80 design space, scaled to the stage's canvas.

    Points inside the head group are scaled about the head anchor by head_scale, so the
    hatchling gets its oversized head from the same drawing code.
    """

    def __init__(self, img, stage, dy=0):
        self.d = ImageDraw.Draw(img)
        self.s = stage["scale"]
        self.hs = stage["head"]
        self.ws = stage["wing"]
        self.horn = stage.get("horn", 1.0)
        self.dy = dy + stage.get("drop", 0)
        self.head = None  # anchor while drawing the head group
        self.wing = None  # anchor while drawing a wing

    def p(self, x, y):
        y += self.dy
        if self.head is not None:
            hx, hy = self.head
            x, y = hx + (x - hx) * self.hs, hy + (y - hy) * self.hs
        if self.wing is not None:
            wx, wy = self.wing
            x, y = wx + (x - wx) * self.ws, wy + (y - wy) * self.ws
        return (round(x * self.s), round(y * self.s))

    def poly(self, pts, fill):
        self.d.polygon([self.p(*q) for q in pts], fill=fill)

    def line(self, pts, fill, width=1):
        self.d.line([self.p(*q) for q in pts], fill=fill, width=max(1, round(width * self.s)))

    def dot(self, x, y, r, fill):
        cx, cy = self.p(x, y)
        r = max(0.5, r * self.s)
        self.d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)


def wing_points(shoulder, angle, reach):
    """Membrane polygon for a wing raised `angle` degrees from straight back (180 = level back)."""
    sx, sy = shoulder
    a = math.radians(angle)
    ex, ey = sx + 18 * reach * math.cos(a), sy - 18 * reach * math.sin(a)
    tips = []
    for da, length in ((-28, 26), (0, 24), (28, 19)):
        b = math.radians(angle + da)
        tips.append((ex + length * reach * math.cos(b), ey - length * reach * math.sin(b)))
    return (ex, ey), tips


def draw_wing(pen, shoulder, root, angle, far):
    pen.wing = shoulder
    elbow, tips = wing_points(shoulder, angle, 1.0)
    membrane = MEMBRANE_DARK if far else MEMBRANE
    pen.poly([shoulder, elbow] + tips + [root], membrane)
    # Scalloped trailing edge: notch between fingers.
    for a, b in zip(tips, tips[1:]):
        mid = ((a[0] + b[0]) / 2 * 0.8 + root[0] * 0.2, (a[1] + b[1]) / 2 * 0.8 + root[1] * 0.2)
        pen.poly([a, mid, b], (0, 0, 0, 0))
    spar = SCALE_DARK if far else SPAR
    pen.line([shoulder, elbow], spar, 2)
    for t in tips:
        pen.line([elbow, t], spar, 1)
    pen.wing = None


def draw_head(pen, hx, hy, jaw_open=0, eyes_closed=False, tilt=0):
    """Head group anchored at the back of the skull (hx, hy). Faces right."""
    pen.head = (hx, hy)
    t = tilt
    k = pen.horn
    pen.poly([(hx - 3 * k, hy + 1), (hx + 6 * k, hy - 6 * k), (hx - 9 * k, hy - 10 * k), (hx - 6 * k, hy - 4 * k)], HORN)  # far horn
    pen.poly([(hx, hy), (hx + 8, hy - 2), (hx + 18, hy + 1 + t), (hx + 25, hy + 5 + t), (hx + 25, hy + 8 + t),
              (hx + 16, hy + 10 + t), (hx + 6, hy + 12), (hx, hy + 9)], SCALE)                      # skull + snout
    pen.poly([(hx + 6, hy + 10 + jaw_open * 0.4), (hx + 22, hy + 9 + t + jaw_open),
              (hx + 20, hy + 12 + t + jaw_open), (hx + 7, hy + 13 + jaw_open * 0.4)], BELLY_DARK)    # lower jaw
    if jaw_open > 0:
        pen.poly([(hx + 10, hy + 10), (hx + 24, hy + 8 + t), (hx + 21, hy + 9 + t + jaw_open)], GLOW)
    pen.poly([(hx + 2, hy + 1), (hx - 9 * k, hy - 7 * k), (hx - 6 * k, hy - 8 * k), (hx + 6, hy - 1)], HORN)  # near horn
    pen.line([(hx + 4, hy - 1), (hx + 16, hy + 1 + t)], SCALE_LIGHT, 1)                              # brow ridge
    if eyes_closed:
        pen.line([(hx + 9, hy + 3), (hx + 12, hy + 3)], OUTLINE, 1)
    else:
        pen.dot(hx + 10.5, hy + 3, 1.3, EYE)
    pen.dot(hx + 23, hy + 5 + t, 0.6, OUTLINE)                                                       # nostril
    pen.head = None


def draw_dragon(img, stage, pose, phase=0.0, bob=0, wing_angle=None, jaw=0, legs=0, eyes_closed=False, glow=0.0, pulse=0.0):
    """glow: chest fire building before a breath (0..1). pulse: ember veins brightening (idle)."""
    pen = DragonPen(img, stage, dy=bob)
    if pose == "fly":
        shoulder, root = (52, 44), (36, 50)
        far_wing = (shoulder[0] + 3, shoulder[1] - 2)
        draw_wing(pen, far_wing, (root[0] + 3, root[1] - 2), (wing_angle or 140) + 8, far=True)
        # Tail streams out behind, gently waving with the flap.
        wave = math.sin(phase * math.tau) * 2
        pen.poly([(34, 48), (22, 50 + wave), (8, 49 + wave * 1.5), (2, 51 + wave * 2), (8, 54 + wave * 1.5),
                  (22, 56 + wave), (34, 56)], SCALE)
        pen.dot(3, 51.5 + wave * 2, 1.6, GLOW)
        pen.poly([(40, 56), (46, 56), (42, 64), (36, 62)], SCALE_DARK)                    # tucked legs
        pen.poly([(30, 50), (40, 42), (56, 41), (66, 46), (63, 55), (46, 58), (32, 56)], SCALE)
        pen.poly([(44, 55), (62, 50), (60, 55), (46, 58)], BELLY)
        pen.poly([(60, 44), (70, 38), (76, 35), (79, 41), (70, 47), (63, 52)], SCALE)   # neck
        for x, y in ((36, 44), (44, 41), (52, 40), (60, 41), (68, 37)):
            pen.poly([(x - 2, y + 1), (x, y - 3), (x + 2, y + 1)], SCALE_DARK)
        draw_head(pen, 74, 31, jaw_open=jaw, eyes_closed=eyes_closed)
        pen.line([(42, 48), (46, 51), (44, 54)], GLOW, 1)
        draw_wing(pen, shoulder, root, wing_angle or 140, far=False)
        return

    lying = pose == "sleep"
    low = 10 if lying else 0
    # Far legs and far wing behind the body.
    if not lying:
        pen.poly([(34 + legs, 56), (42 + legs, 55), (44 + legs, 70), (40 + legs, 77), (34 + legs, 77), (37 + legs, 70)], SCALE_DARK)
    if wing_angle is None:
        pen.poly([(45, 40 + low), (38, 22 + low), (47, 27 + low), (56, 38 + low)], MEMBRANE_DARK)
    else:
        draw_wing(pen, (49, 38), (37, 46), wing_angle + 8, far=True)
    # Tail: thick at the hips, sweeping down and back to a glowing tip.
    curl = 4 if lying else 0
    pen.poly([(40, 60 + low * 0.5), (30, 66 + curl), (18, 72), (8, 74), (3, 72), (13, 67), (25, 60 + curl), (36, 53 + low)], SCALE)
    hot = GLOW_HI if max(glow, pulse) >= 0.5 else GLOW
    pen.dot(4.5, 72.5, 1.8, hot)
    for x, y in ((32, 61), (22, 67), (13, 71)):
        pen.poly([(x - 2, y), (x, y - 3), (x + 2, y)], SCALE_DARK)
    # Body: heavy, leaning forward.
    pen.poly([(32, 52 + low), (37, 41 + low), (50, 36 + low), (60, 40 + low), (63, 52 + low), (57, 62 + low * 0.6),
              (43, 65 + low * 0.3), (34, 61 + low * 0.5)], SCALE)
    pen.poly([(51, 40 + low), (60, 43 + low), (61, 53 + low), (55, 62 + low * 0.6), (49, 59 + low * 0.6)], BELLY)
    for k in range(3):
        y = 46 + low + k * 5
        pen.line([(52, y), (60, y - 1)], BELLY_DARK, 1)
    for x, y in ((37, 44), (43, 39), (49, 36)):
        pen.poly([(x - 2, y + 2 + low), (x, y - 2 + low), (x + 2, y + 2 + low)], SCALE_DARK)
    pen.line([(38, 48 + low), (43, 52 + low), (41, 57 + low)], hot, 1)  # ember vein
    pen.line([(46, 43 + low), (50, 47 + low)], GLOW, 1)
    if glow > 0:  # chest fire building up before a breath
        pen.dot(57, 47 + low, 3 + 2 * glow, GLOW if glow < 0.8 else GLOW_HI)
    # Near hind leg and foot.
    if lying:
        pen.poly([(40, 66), (54, 66), (56, 72), (40, 72)], SCALE)
        pen.poly([(52, 70), (60, 70), (60, 73), (50, 73)], SCALE_DARK)
    else:
        pen.poly([(38 - legs, 54), (49 - legs, 52), (51 - legs, 64), (46 - legs, 70)], SCALE)
        pen.poly([(42 - legs, 66), (48 - legs, 64), (50 - legs, 76), (44 - legs, 76)], SCALE)
        pen.poly([(42 - legs, 74), (54 - legs, 74), (56 - legs, 77), (40 - legs, 77)], SCALE_DARK)
        pen.poly([(57, 50), (61, 50), (65, 58), (63, 60), (59, 56)], SCALE)              # arm
        pen.line([(63, 60), (66, 61)], HORN, 1)
    # Neck and head.
    if lying:
        pen.poly([(54, 50), (62, 50), (70, 58), (68, 64), (58, 60)], SCALE)
        for x, y in ((58, 50), (64, 53)):
            pen.poly([(x - 2, y + 1), (x, y - 2), (x + 2, y + 1)], SCALE_DARK)
        draw_head(pen, 66, 58, eyes_closed=True, tilt=2)
    else:
        lean = {"breathe": 5, "windup": -3}.get(pose, 0)
        lift = {"breathe": 3, "windup": -2}.get(pose, 0)
        hx, hy = 66 + lean, 16 + lift
        pen.poly([(52, 42), (58, 34), (hx - 2, hy + 8), (hx + 4, hy + 6), (hx + 6, hy + 12), (64, 34), (62, 46)], SCALE)
        for k in range(3):
            x, y = 58 + (hx - 58) * k / 3, 34 + (hy + 6 - 34) * k / 3
            pen.poly([(x - 2, y + 1), (x - 1, y - 3), (x + 1, y + 1)], SCALE_DARK)
        pen.line([(62, 38), (hx + 2, hy + 12)], BELLY, 2)
        draw_head(pen, hx, hy, jaw_open=jaw, eyes_closed=eyes_closed, tilt={"breathe": 2}.get(pose, 0))
    # Near wing.
    if wing_angle is None:
        pen.poly([(47, 40 + low), (40, 21 + low), (50, 27 + low), (59, 39 + low)], MEMBRANE)  # folded
        pen.line([(47, 40 + low), (40, 21 + low)], SPAR, 2)
        pen.line([(40, 21 + low), (52, 31 + low)], SPAR, 1)
    else:
        draw_wing(pen, (47, 40), (35, 48), wing_angle, far=False)


def dragon_sheet(name, canvas, stage, pivot, mouth, hatch=False, saddle=None):
    """canvas: (w, h) in pixels; stage: scale, head/wing/horn proportions and a drop (design units) from the 96x80 adult."""

    def frame(pose, **kw):
        img = Image.new("RGBA", canvas, (0, 0, 0, 0))
        draw_dragon(img, stage, pose, **kw)
        return outline(img, OUTLINE)

    seq = Sequencer()
    seq.add("idle", [frame("stand", bob=b, pulse=g) for b, g in ((0, 0), (0, 0.3), (1, 0.6), (1, 0.3))], 220)
    seq.add("walk", [frame("stand", legs=l, bob=b) for l, b in ((3, 0), (0, -1), (-3, 0), (0, -1))], 130)
    flap = [80, 105, 145, 195, 170, 120]
    seq.add("fly", [frame("fly", wing_angle=a, phase=i / 6, bob=[0, 0, -1, -2, -1, 0][i]) for i, a in enumerate(flap)], 80)
    seq.add("glide", [frame("fly", wing_angle=a, phase=i / 2) for i, a in enumerate((168, 164))], 260)
    seq.add("takeoff", [frame("stand", wing_angle=95, bob=3), frame("stand", wing_angle=200, bob=-3), frame("fly", wing_angle=110)], 110, loop=False)
    seq.add("land", [frame("fly", wing_angle=100), frame("stand", wing_angle=130, bob=-2), frame("stand", bob=2)], 120, loop=False)
    seq.add("breathe_windup", [frame("windup", glow=g) for g in (0.4, 0.9)], 180)
    seq.add("breathe", [frame("breathe", jaw=j) for j in (3, 5, 5)], 130, loop=False)
    seq.add("sleep", [frame("sleep", bob=b) for b in (0, 1)], 900)
    seq.add("downed", [frame("sleep")], 1000, loop=False)
    if hatch:
        start = len(seq.frames)
        seq.frames.extend(hatch_frames(canvas, stage, pivot))
        seq.tags["hatch"] = (start, len(seq.frames) - 1, False, 2)
    points = {"mouth": mouth}
    if saddle:  # where a rider sits (rideable stages)
        points["saddle"] = saddle
    write_sheet(name, seq.frames, seq.tags, pivot, points)
    # Dragon looks: a base sheet (outline, ember glow) plus greyscale scale, belly, wing, horn and
    # eye layers the game tints (Rendering/DragonLooks.cs). The plain sheet above stays the
    # default-coloured composite for anything drawn without a look.
    split = [split_layers(img, DRAGON_LAYER_SHADES) for img, _ in seq.frames]
    for layer in ["base", *DRAGON_LAYER_SHADES]:
        frames = [(parts[layer], ms) for parts, (_, ms) in zip(split, seq.frames)]
        write_sheet(f"{name}_{layer}", frames, seq.tags, pivot, points, trim=layer != "base")


# Dragon colour groups and the grey each palette colour becomes. Main tones sit below white so
# highlights can stay lighter than the chosen colour; the game's default look (DragonLooks.Default)
# is each group's main colour scaled back up, so an untouched dragon matches the composite sheet.
DRAGON_LAYER_SHADES = {
    "scales": {SCALE: 200, SCALE_LIGHT: 255, SCALE_DARK: 110},
    "belly": {BELLY: 230, BELLY_DARK: 180},
    "wings": {MEMBRANE: 230, MEMBRANE_DARK: 160},
    "horns": {HORN: 255, SPAR: 170},
    "eyes": {EYE: 255},
}


def shade(c, k):
    """Scale an RGB(A) colour's brightness, returning opaque RGBA."""
    return tuple(max(0, min(255, int(v * k))) for v in c[:3]) + (255,)


# ---------------------------------------------------------------- egg & hatching

def egg_layer(canvas, cx, bottom, w, h, seed=3, glow=0.5, cracks=0, cold=False, tilt=0.0):
    """A dragon egg in the dragon's colours: charcoal shell with rust scales and glowing ember
    veins (dull when cold), lit from the top-left. cracks: 0 none, 1 a few, 2 many (glowing).
    Tilt rocks it about its base (degrees)."""
    rnd = random.Random(seed)
    img = Image.new("RGBA", canvas, (0, 0, 0, 0))
    px = img.load()
    cy = bottom - h / 2
    shell = (66, 46, 48) if not cold else (64, 62, 74)
    scale = (124, 50, 36) if not cold else (92, 70, 74)
    for y in range(canvas[1]):
        for x in range(canvas[0]):
            nx = (x + 0.5 - cx) / (w / 2)
            ny = (y + 0.5 - cy) / (h / 2)
            ny *= 0.92 if ny < 0 else 1.06  # pointier top, rounder bottom
            r = nx * nx + ny * ny
            if r > 1:
                continue
            light = -(nx * -0.55 + ny * -0.8) * 0.6 + 0.25 * (1 - r)
            row = int((y - cy) // 3)
            on_scale = (x + (row % 2) * 2) % 4 == 0 or (y % 3 == 0 and (x + row) % 4 < 2)
            base = scale if on_scale else shell
            k = 0.7 if light < -0.2 else 0.9 if light < 0.15 else 1.1 if light < 0.4 else 1.3
            px[x, y] = shade(base, k)
    # Ember veins: a few branching lines down the shell.
    vein = GLOW_HI if glow > 0.75 else GLOW if glow > 0.3 else (170, 70, 30, 255)
    if cold:
        vein = (96, 52, 44, 255)
    dim = (110, 50, 36, 255) if cold else (150, 64, 30, 255)
    band = int(cy + 2)                                        # an engraved rune band round the middle
    for x in range(canvas[0]):
        if px[x, band][3]:
            px[x, band] = shade(shell, 0.6)
            if x % 3 == 0 and px[x, band + 1][3]:
                px[x, band + 1] = dim
    for v in range(3):                                        # veins step at right angles, like runes
        x, y = int(cx + rnd.uniform(-w / 3, w / 3)), int(cy - h / 2 + rnd.uniform(3, 6))
        dx = rnd.choice((-1, 1))
        done = 0
        while done < int(h * 0.7):
            for _ in range(rnd.randint(2, 4)):
                if px[x, y][3]:
                    px[x, y] = vein
                y += 1
                done += 1
            for _ in range(rnd.randint(1, 3)):
                if px[x, min(y, canvas[1] - 1)][3]:
                    px[x, min(y, canvas[1] - 1)] = vein
                x += dx
            dx = -dx if rnd.random() < 0.5 else dx
            x = max(0, min(canvas[0] - 1, x))
            y = min(y, canvas[1] - 1)
    if cracks:
        for c in range(1 + cracks * 2):
            x, y = cx + rnd.uniform(-w / 3, w / 3), cy - h / 2 + 2 + c
            for _ in range(rnd.randint(4, 6 + cracks * 3)):
                if 0 <= int(x) < canvas[0] and 0 <= int(y) < canvas[1] and px[int(x), int(y)][3]:
                    px[int(x), int(y)] = GLOW_HI
                x += rnd.choice((-1, 1))
                y += rnd.choice((0, 1))
    img = selective_outline(img, keep=(GLOW, GLOW_HI, (110, 50, 36, 255), (150, 64, 30, 255), (170, 70, 30, 255), (96, 52, 44, 255)))
    if tilt:
        img = img.rotate(tilt, resample=Image.NEAREST, center=(cx, bottom))
    return img


def split_egg(egg, cut_y, seed=5):
    """Split an egg image along a jagged line: (lid above, cup below)."""
    rnd = random.Random(seed)
    lid = egg.copy()
    cup = egg.copy()
    lp, cp = lid.load(), cup.load()
    w, h = egg.size
    for x in range(w):
        edge = cut_y + (2 if (x // 2) % 2 else 0) + rnd.choice((0, 0, 1))
        for y in range(h):
            if y < edge:
                cp[x, y] = (0, 0, 0, 0)
            else:
                lp[x, y] = (0, 0, 0, 0)
        if 0 <= edge < h and cp[x, edge][3]:
            cp[x, edge] = GLOW_HI  # the glowing broken rim
    return lid, cup


def egg_sheet():
    canvas, cx, bottom, w, h = (32, 32), 16, 30, 14, 20
    seq = Sequencer()
    seq.add("idle", [egg_layer(canvas, cx, bottom, w, h, glow=g) for g in (0.15, 0.35, 0.55, 0.35)], 300)
    seq.add("cold", [egg_layer(canvas, cx, bottom, w, h, cold=True)], 1000)
    seq.add("wobble", [egg_layer(canvas, cx, bottom, w, h, glow=0.6, tilt=t) for t in (0, -7, 0, 7, 0, -4)], 110)
    seq.add("crack", [egg_layer(canvas, cx, bottom, w, h, glow=1, cracks=1, tilt=t) for t in (0, -6, 0, 6)], 100)
    write_sheet("creatures/dragon_egg", seq.frames, seq.tags, (16, 31))


def hatch_frames(canvas, stage, pivot, draw=None):
    """Egg cracks, lid pops (event frame 2: shell chips fly), the hatchling peeks out, climbs free.
    ``draw(eyes_closed)`` returns the hatchling's frame; without it the old placeholder is drawn."""
    cx, bottom = pivot[0], pivot[1]
    w, h = 14, 20

    def dragon(dy=0, dx=0):
        """The hatchling, sunk dy pixels into the shell and shifted so its head rises out of it."""
        if draw is not None:
            img = draw(dy > 6)
        else:
            img = Image.new("RGBA", canvas, (0, 0, 0, 0))
            draw_dragon(img, stage, "stand", eyes_closed=dy > 6)
            img = outline(img, OUTLINE)
        if dy or dx:
            shifted = Image.new("RGBA", canvas, (0, 0, 0, 0))
            shifted.paste(img, (dx, dy))
            img = shifted
        return img

    def over(*layers):
        out = Image.new("RGBA", canvas, (0, 0, 0, 0))
        for layer in layers:
            out.alpha_composite(layer)
        return out

    cracked = egg_layer(canvas, cx, bottom, w, h, glow=1, cracks=2)
    lid, cup = split_egg(cracked, bottom - 11)
    popped = Image.new("RGBA", canvas, (0, 0, 0, 0))
    popped.paste(lid.rotate(-25, resample=Image.NEAREST, center=(cx, bottom - 11)), (4, -7))
    flash = Image.new("RGBA", canvas, (0, 0, 0, 0))
    ImageDraw.Draw(flash).ellipse([cx - 6, bottom - 16, cx + 6, bottom - 8], fill=GLOW_HI)
    shard_l = cup.crop((0, 0, cx, canvas[1]))
    shard_r = cup.crop((cx, 0, canvas[0], canvas[1]))
    halves = Image.new("RGBA", canvas, (0, 0, 0, 0))
    halves.paste(shard_l.rotate(30, resample=Image.NEAREST, center=(cx, bottom)), (-8, 3), shard_l.rotate(30, resample=Image.NEAREST, center=(cx, bottom)))
    halves.paste(shard_r.rotate(-30, resample=Image.NEAREST, center=(0, bottom)), (cx + 8, 3), shard_r.rotate(-30, resample=Image.NEAREST, center=(0, bottom)))
    return [
        (egg_layer(canvas, cx, bottom, w, h, glow=1, cracks=1, tilt=-6), 180),
        (egg_layer(canvas, cx, bottom, w, h, glow=1, cracks=2, tilt=6), 180),
        (over(flash, cup, popped), 220),
        (over(dragon(10, -8), cup), 320),
        (over(dragon(5, -5), cup), 320),
        (over(halves, dragon()), 480),
    ]


# ---------------------------------------------------------------- tint layers

def split_layers(img, layer_shades=None):
    """Split a composite frame into disjoint layers: base (clothes, outline; drawn as is) and greyscale tint layers."""
    layer_shades = layer_shades or LAYER_SHADES
    w, h = img.size
    src = img.load()
    layers = {name: Image.new("RGBA", (w, h), (0, 0, 0, 0)) for name in ["base", *layer_shades]}
    px = {name: layer.load() for name, layer in layers.items()}
    for yy in range(h):
        for xx in range(w):
            c = src[xx, yy]
            if c[3] == 0:
                continue
            for name, shades in layer_shades.items():
                if c in shades:
                    g = shades[c]
                    px[name][xx, yy] = (g, g, g, 255)
                    break
            else:
                px["base"][xx, yy] = c
    return layers


def diff_layer(dressed, bare):
    """The pixels the dressed frame changes: the worn item on its own."""
    w, h = dressed.size
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    a, b, o = dressed.load(), bare.load(), out.load()
    for yy in range(h):
        for xx in range(w):
            if a[xx, yy] != b[xx, yy] and a[xx, yy][3] > 0:
                o[xx, yy] = a[xx, yy]
    return out


# ---------------------------------------------------------------- effects & overlays

def flame(seed, s=32):
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    def blob(cx, cy, r, col):
        pts = []
        for k in range(12):
            a = k / 12 * math.tau
            wob = 1 + 0.18 * math.sin(a * 3 + seed * 1.9) + 0.1 * math.sin(a * 5 + seed)
            stretch = 1.35 if math.sin(a) < 0 else 1.0  # tongues lick upward
            pts.append((cx + math.cos(a) * r * wob, cy + math.sin(a) * r * wob * stretch))
        d.polygon(pts, fill=col)
    blob(16, 18, 11, (220, 70, 20, 220))
    blob(16, 19, 8, (255, 150, 40, 235))
    blob(16, 20, 4.5, (255, 232, 140, 250))
    return img


def speech_bubble():
    """A white speech bubble with a tail (tinted per interaction by the renderer) and three dots."""
    img = Image.new("RGBA", (14, 13), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(3, 8), (3, 12), (7, 8)], fill=(255, 255, 255, 255), outline=(40, 32, 30, 255))
    d.rounded_rectangle([0, 0, 13, 9], radius=3, fill=(255, 255, 255, 255), outline=(40, 32, 30, 255))
    d.line([(4, 8), (6, 8)], fill=(255, 255, 255, 255))
    for x in (4, 7, 10):
        d.point([(x - 1, 5), (x, 5)], fill=(40, 32, 30, 255))
    return img


def effects():
    # Eggshell chip, thrown when a hatchling breaks out.
    img = Image.new("RGBA", (6, 6), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(0, 2), (3, 0), (5, 2), (4, 5), (1, 4)], fill=(70, 48, 50, 255))
    d.line([(1, 4), (4, 5)], fill=(214, 196, 160, 255))
    img.save(os.path.join(ROOT, "fx", "shell.png"))
    # Water glint: a sparkle that swells and fades (one loop, picked by phase per cell).
    frames = []
    for r in (0, 1, 2, 3, 2, 1):
        g = Image.new("RGBA", (9, 9), (0, 0, 0, 0))
        gd = ImageDraw.Draw(g)
        if r:
            gd.line([(4 - r, 4), (4 + r, 4)], fill=(255, 255, 255, 230))
            gd.line([(4, 4 - r), (4, 4 + r)], fill=(255, 255, 255, 230))
            gd.point([(4, 4)], fill=(255, 255, 255, 255))
        else:
            gd.point([(4, 4)], fill=(255, 255, 255, 160))
        frames.append((g, 90))
    write_sheet("fx/glint", frames, {"sparkle": (0, len(frames) - 1, True, -1)}, (4, 4))
    seq = Sequencer()
    seq.add("burn", [flame(k) for k in range(4)], 60)
    write_sheet("fx/breath", seq.frames, seq.tags, (16, 31))

    img = Image.new("RGBA", (12, 12), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([(2, 2), (9, 2), (2, 9), (9, 9)], fill=(255, 255, 255, 255), width=2)
    img.save(os.path.join(ROOT, "fx", "zzz.png"))

    speech_bubble().save(os.path.join(ROOT, "fx", "speech.png"))

    img = Image.new("RGBA", (6, 6), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([0, 0, 5, 5], fill=(255, 170, 60, 255))
    d.ellipse([2, 2, 3, 3], fill=(255, 245, 190, 255))
    img.save(os.path.join(ROOT, "fx", "ember.png"))

    # Blanket drawn over a sleeper in bed; matches buildings/bed (pillow at the top).
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([5, 12, 26, 29], fill=(66, 98, 170, 255), outline=(40, 60, 110, 255))
    d.line([(6, 13), (25, 13)], fill=(225, 225, 215, 255))
    d.line([(6, 20), (25, 20)], fill=(56, 84, 150, 255))
    img.save(os.path.join(ROOT, "buildings", "bed_blanket.png"))


# ---------------------------------------------------------------- UI icons (24x24)

def ui_icons():
    """Toolbar, designation and pawn-bubble icons. Light glyphs with a dark outline so they read on
    the warm UI and over the world alike."""
    S = 24
    INK = (40, 26, 20, 255)
    def icon(name, draw):
        img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        draw(ImageDraw.Draw(img))
        outline(img, INK).save(os.path.join(ROOT, "ui", f"icon_{name}.png"))

    STEEL, STEEL_D = (205, 210, 220, 255), (140, 146, 160, 255)
    HANDLE = (176, 124, 72, 255)

    def select(d):  # arrow cursor
        d.polygon([(6, 3), (6, 19), (10, 15), (13, 21), (15, 20), (12, 14), (18, 14)], fill=(245, 240, 230, 255))
    def cancel(d):
        d.line([(5, 5), (18, 18)], fill=(235, 80, 60, 255), width=4)
        d.line([(18, 5), (5, 18)], fill=(235, 80, 60, 255), width=4)
    def axe(d):
        d.line([(6, 20), (15, 6)], fill=HANDLE, width=3)
        d.polygon([(12, 3), (20, 6), (19, 13), (14, 9)], fill=STEEL)
    def pick(d):
        d.line([(6, 20), (14, 8)], fill=HANDLE, width=3)
        d.arc([4, 2, 22, 16], 200, 340, fill=STEEL, width=3)
    def shovel(d):
        d.line([(15, 4), (9, 14)], fill=HANDLE, width=3)
        d.polygon([(6, 13), (12, 16), (9, 22), (4, 19)], fill=STEEL)
        d.line([(13, 3), (18, 6)], fill=HANDLE, width=2)
    def berries(d):
        d.polygon([(11, 3), (17, 4), (13, 8)], fill=(90, 160, 70, 255))
        for x, y in ((8, 12), (15, 11), (11, 17), (17, 17)):
            d.ellipse([x - 3.5, y - 3.5, x + 3.5, y + 3.5], fill=(205, 40, 60, 255))
    def hammer(d):
        d.line([(6, 20), (14, 9)], fill=HANDLE, width=3)
        d.polygon([(9, 5), (17, 2), (21, 9), (13, 12)], fill=STEEL)
    def deconstruct(d):
        hammer(d)
        d.line([(3, 3), (9, 9)], fill=(235, 80, 60, 255), width=2)
        d.line([(9, 3), (3, 9)], fill=(235, 80, 60, 255), width=2)
    def flame(d):
        d.polygon([(12, 2), (18, 10), (19, 16), (15, 21), (9, 21), (5, 16), (7, 9), (10, 12)], fill=(240, 110, 30, 255))
        d.polygon([(12, 10), (15, 15), (13, 20), (10, 20), (9, 16)], fill=(255, 225, 110, 255))
    def crate(d):
        d.rectangle([4, 7, 19, 20], fill=(170, 120, 70, 255))
        d.rectangle([4, 7, 19, 10], fill=(200, 150, 90, 255))
        d.line([(4, 7), (19, 20)], fill=(120, 80, 45, 255), width=2)
        d.line([(19, 7), (4, 20)], fill=(120, 80, 45, 255), width=2)
    def eraser(d):
        d.polygon([(4, 15), (13, 6), (20, 13), (11, 22)], fill=(230, 140, 150, 255))
        d.polygon([(4, 15), (8, 11), (15, 18), (11, 22)], fill=(110, 140, 210, 255))
    def moon(d):
        d.ellipse([4, 4, 19, 19], fill=(240, 230, 170, 255))
        d.ellipse([9, 2, 23, 16], fill=(0, 0, 0, 0))
    def plate(d):
        d.ellipse([3, 8, 20, 19], fill=(235, 235, 225, 255))
        d.ellipse([7, 10, 16, 16], fill=(200, 80, 60, 255))
    def alert(d):
        d.rectangle([10, 3, 14, 15], fill=(255, 80, 60, 255))
        d.rectangle([10, 17, 14, 21], fill=(255, 80, 60, 255))
    def sword(d):
        d.line([(6, 18), (18, 6)], fill=STEEL, width=3)
        d.line([(5, 13), (11, 19)], fill=(210, 170, 80, 255), width=2)
        d.line([(4, 20), (6, 18)], fill=HANDLE, width=3)
    def cross(d):
        d.rectangle([9, 4, 14, 19], fill=(235, 60, 60, 255))
        d.rectangle([4, 9, 19, 14], fill=(235, 60, 60, 255))
    def haul(d):
        d.rectangle([5, 10, 18, 19], fill=(170, 120, 70, 255))
        d.polygon([(11, 3), (16, 8), (6, 8)], fill=(245, 240, 230, 255))
    def heart(d):
        d.ellipse([4, 5, 12, 13], fill=(230, 80, 110, 255))
        d.ellipse([11, 5, 19, 13], fill=(230, 80, 110, 255))
        d.polygon([(4, 10), (19, 10), (12, 20)], fill=(230, 80, 110, 255))

    def bow(d):  # hunt: a bow with an arrow nocked
        d.arc([3, 3, 17, 21], 280, 80, fill=HANDLE, width=3)
        d.line([(10, 4), (10, 20)], fill=(235, 230, 215, 255), width=1)
        d.line([(6, 12), (21, 12)], fill=STEEL, width=2)
        d.polygon([(21, 9), (21, 15), (24, 12)], fill=STEEL)
    def fish(d):
        d.ellipse([3, 7, 17, 17], fill=(120, 170, 210, 255))
        d.polygon([(15, 12), (21, 6), (21, 18)], fill=(90, 140, 190, 255))
        d.point([(7, 11)], fill=INK)
    def cleaver(d):  # butcher
        d.polygon([(4, 5), (15, 5), (15, 14), (6, 14)], fill=STEEL)
        d.line([(4, 13), (15, 13)], fill=STEEL_D, width=1)
        d.line([(15, 9), (21, 18)], fill=HANDLE, width=3)

    for name, fn in (("select", select), ("cancel", cancel), ("chop", axe), ("mine", pick), ("dig", shovel),
                     ("harvest", berries), ("build", hammer), ("deconstruct", deconstruct), ("burn", flame),
                     ("stockpile", crate), ("clearzone", eraser), ("sleep", moon), ("eat", plate), ("alert", alert),
                     ("fight", sword), ("doctor", cross), ("haul", haul), ("bond", heart),
                     ("hunt", bow), ("fish", fish), ("butcher", cleaver)):
        icon(name, fn)

    # Speech-bubble backing for status icons over pawns.
    img = Image.new("RGBA", (S, S + 6), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([1, 1, S - 2, S - 2], fill=(250, 244, 232, 235))
    d.polygon([(9, S - 4), (15, S - 4), (12, S + 4)], fill=(250, 244, 232, 235))
    outline(img, INK).save(os.path.join(ROOT, "ui", "bubble.png"))


# ---------------------------------------------------------------- wild animals

ANIMAL_OUTLINE = (30, 24, 20, 255)

# Proportions per kind, in pixels of its own canvas. Side view facing right, feet on the bottom row.
ANIMALS = {
    "deer": dict(canvas=(40, 36), body=(9, 14, 29, 23), leg=11, neck=True, head=(28, 4, 37, 11), coat=(150, 104, 64, 255),
                 coat_dark=(112, 76, 46, 255), belly=(214, 190, 150, 255), antlers=True),
    "boar": dict(canvas=(36, 26), body=(5, 7, 28, 19), leg=6, neck=False, head=(25, 9, 34, 17), coat=(84, 66, 56, 255),
                 coat_dark=(58, 44, 38, 255), belly=(110, 90, 76, 255), tusks=True, bristles=True),
    "hare": dict(canvas=(20, 16), body=(3, 6, 14, 13), leg=2, neck=False, head=(12, 3, 18, 9), coat=(160, 140, 112, 255),
                 coat_dark=(120, 102, 80, 255), belly=(226, 216, 196, 255), ears=True),
}


def animal_frame(kind, legs=0, bob=0, head_down=0, lying=False, dead=False):
    """One quadruped frame. legs: stride offset; head_down: pixels the head drops (grazing)."""
    k = ANIMALS[kind]
    w, h = k["canvas"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = k["body"]
    hx0, hy0, hx1, hy1 = k["head"]
    drop = (h - 1 - y1 - k["leg"] + (y1 - y0) // 2) if lying else 0
    y0, y1, hy0, hy1 = y0 + drop + bob, y1 + drop + bob, hy0 + drop + bob + head_down, hy1 + drop + bob + head_down
    if dead:  # on its side: legs out stiff toward the viewer
        for lx in (x0 + 2, x0 + 5, x1 - 6, x1 - 3):
            d.line([(lx, y1 - 1), (lx + 3, h - 1)], fill=k["coat_dark"], width=2)
    elif not lying:
        foot = h - 1
        for i, lx in enumerate((x0 + 2, x0 + 5, x1 - 6, x1 - 3)):
            stride = legs if i % 2 == 0 else -legs
            colour = k["coat_dark"] if i in (1, 3) else k["coat"]
            d.line([(lx, y1 - 1), (lx + stride, foot)], fill=colour, width=2)
    d.ellipse([x0, y0, x1, y1], fill=k["coat"])
    d.chord([x0 + 2, y0 + (y1 - y0) // 2, x1 - 2, y1], 0, 180, fill=k["belly"])
    if k.get("bristles"):
        for bx in range(x0 + 3, x1 - 2, 3):
            d.line([(bx, y0), (bx + 1, y0 - 2)], fill=k["coat_dark"])
    if k.get("neck"):
        d.polygon([(x1 - 6, y0 + 3), (x1 - 1, y0 + 1), (hx0 + 3, hy1 - 1), (hx0, hy1 - 3)], fill=k["coat"])
    d.ellipse([hx0, hy0, hx1, hy1], fill=k["coat"])
    d.ellipse([hx1 - 3, hy0 + (hy1 - hy0) // 2 - 1, hx1 + 1, hy1], fill=k["coat_dark"])  # snout
    eye = (20, 16, 14, 255)
    if dead:
        ex, ey = hx0 + (hx1 - hx0) // 2, hy0 + 2
        d.line([(ex - 1, ey - 1), (ex + 1, ey + 1)], fill=eye)
        d.line([(ex + 1, ey - 1), (ex - 1, ey + 1)], fill=eye)
    else:
        d.point([(hx0 + (hx1 - hx0) // 2 + 1, hy0 + 2)], fill=eye)
    if k.get("antlers"):
        ax = hx0 + 3
        d.line([(ax, hy0), (ax - 2, hy0 - 4), (ax - 4, hy0 - 4)], fill=(206, 190, 160, 255))
        d.line([(ax - 2, hy0 - 4), (ax - 1, hy0 - 7)], fill=(206, 190, 160, 255))
    if k.get("tusks"):
        d.line([(hx1 - 1, hy1 - 1), (hx1 + 1, hy1 - 4)], fill=(236, 228, 210, 255), width=1)
    if k.get("ears"):
        d.polygon([(hx0 + 1, hy0 + 1), (hx0 - 1, hy0 - 4), (hx0 + 2, hy0 - 4), (hx0 + 3, hy0 + 1)], fill=k["coat"])
    d.ellipse([x0 - 1, y0 + 1, x0 + 2, y0 + 4], fill=k["belly"])  # tail
    return selective_outline(nordic_finish(relight(img)))   # volume, the Nordic grade, a tinted outline


def animal_sheet(kind):
    w, h = ANIMALS[kind]["canvas"]
    seq = Sequencer()
    seq.add("idle", [animal_frame(kind), animal_frame(kind, head_down=3), animal_frame(kind, head_down=4), animal_frame(kind, head_down=3)], 500)
    seq.add("walk", [animal_frame(kind, legs=l, bob=b) for l, b in ((2, 0), (0, -1), (-2, 0), (0, -1))], 110)
    seq.add("sleep", [animal_frame(kind, lying=True), animal_frame(kind, lying=True, bob=1)], 1200)
    seq.add("downed", [animal_frame(kind, lying=True, dead=True)], 1000, loop=False)
    write_sheet("creatures/" + kind, seq.frames, seq.tags, (w // 2, h - 1))


def animal_sheets():
    for kind in ANIMALS:
        animal_sheet(kind)


def generate():
    # Proportions relative to the 96x80 adult design: the young have big heads, stubby wings, budding horns.
    # the egg comes from chibi_dragon.egg_sheet now
    animal_sheets()
    effects()
    ui_icons()


if __name__ == "__main__":
    generate()
