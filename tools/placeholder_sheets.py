"""Procedural placeholder sprite *sheets* (animated), in the same format tools/export_art.py writes.

Each sheet is a horizontal strip PNG plus a sibling .anim.json manifest:
    { "frameSize": [w, h], "pivot": [x, y], "points": {"mouth": [x, y]},
      "frames": [{"x","y","w","h","ox","oy","ms"}...],
      "tags": {"fly": {"from": 0, "to": 5, "loop": true, "event": -1}} }

Hand-made Aseprite art exported to the same names replaces these files.
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
                     ("fight", sword), ("doctor", cross), ("haul", haul),
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
    animal_sheets()
    effects()
    ui_icons()


if __name__ == "__main__":
    generate()
