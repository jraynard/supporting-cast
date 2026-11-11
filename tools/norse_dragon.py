"""The Norse dragon breed (``NorseDragon`` in data/defs/core.json): designed at 128x96 and drawn
a little larger than the Hearth dragon (tools/chibi_dragon.py), since it's the heavier breed.
Its sheets are ``creatures/dragon_norse_<stage>[_layer]``.

    python3 tools/norse_dragon.py              # every stage into data/textures
    python3 tools/norse_dragon.py --preview    # ...and a contact sheet in .cache/norse_dragons.png

Same creature as docs/ART.md describes (upright, leaning forward, ember veins, bone horns and
belly), pushed toward Norse carving: a long prow-curved neck, swept horns, a ridge of bone plates
down the spine like ship ribs, and rune-stepped ember veins instead of random cracks.

The layered-look contract is unchanged (docs/ART.md "Dragons: layered looks"): the composite is
drawn with *marker* colours per group, then split into ``_base`` (outline, glow, face details,
drawn as is) and greyscale ``_scales``, ``_belly``, ``_wings``, ``_horns``, ``_eyes`` layers the
game tints, plus two layers the game tints with the dragon's element colour: ``_flame``, only the
hottest glow marker (the yellow core of the tail flame and chest glow; the orange rest of each flame
stays in ``_base``, so every flame is a normal one with an element-coloured heart), and ``_veins``,
the ember veins, which are entirely the element colour (their own markers, near-orange so the plain
sheet still looks like embers). Each group's marker ramp is the game's default tint (DragonLooks.Default) scaled by
the layer's grey, so an untouched dragon matches the plain sheet. Main tones keep the old greys
(scales 200, belly 230, wings 230, horns 255), so DragonLooks needs no change.
"""
import math
import os

from PIL import Image, ImageDraw

from norse_art import mask_of, union, shade_shape, ribbon, catmull, selective_outline, bayer
from placeholder_sheets import Sequencer, write_sheet, split_layers, hatch_frames, ROOT

W, H = 128, 96
SIZE = (W, H)
GROUND = 92
PIVOT = (60, 92)

# ---------------------------------------------------------------- marker ramps (default tint x grey)

TINTS = {
    "scales": (0x9E, 0x40, 0x2E),
    "belly": (0xED, 0xD9, 0xB1),
    "wings": (0x66, 0x26, 0x21),
    "horns": (0xE4, 0xD8, 0xBE),
    "eyes": (0xFF, 0xD6, 0x50),
}
GREYS = {
    "scales": (64, 100, 146, 200, 232, 255),     # main = 200
    "belly": (150, 190, 230, 255),               # main = 230
    "wings": (96, 140, 186, 230, 255),           # main = 230
    "horns": (120, 168, 214, 255),               # main = 255
    "eyes": (255,),
}


def _ramp(group):
    return [tuple(round(c * g / 255) for c in TINTS[group]) + (255,) for g in GREYS[group]]


SCALES, BELLY, WINGS, HORNS, EYES = (_ramp(g) for g in ("scales", "belly", "wings", "horns", "eyes"))

LAYER_SHADES = {g: dict(zip(_ramp(g), GREYS[g])) for g in ("scales", "belly", "wings", "horns", "eyes")}

GLOW = (255, 140, 40, 255)
GLOW_HI = (255, 224, 110, 255)
GLOW_DIM = (176, 78, 32, 255)
INK = (30, 14, 22, 255)
EYE_LIGHT = (255, 248, 220, 255)
VEIN = (255, 141, 40, 255)
VEIN_HI = (255, 225, 110, 255)
GLOWS = (GLOW, GLOW_HI, GLOW_DIM, VEIN, VEIN_HI)
# The flame layer: just the core, which becomes the element colour itself.
LAYER_SHADES["flame"] = {GLOW_HI: 255}
LAYER_SHADES["veins"] = {VEIN: 205, VEIN_HI: 255}      # the whole vein is the element colour, hot ones brighter
RIM = (150, 162, 188, 255)
_tinted_scales = set(SCALES[2:])

_seen = {}
for _g, _m in LAYER_SHADES.items():                 # marker colours must be unique across groups
    for _c in _m:
        assert _c not in _seen, f"{_g} marker {_c} collides with {_seen.get(_c)}"
        _seen[_c] = _g
for _c in (INK, EYE_LIGHT):
    assert _c not in _seen


# ---------------------------------------------------------------- geometry helpers

def shifted(points, dx=0, dy=0):
    return [(x + dx, y + dy) for x, y in points]


def blend_shift(points, dx, dy, weights):
    """Move each control point by (dx, dy) scaled by its weight: bends a neck or tail without moving its root."""
    return [(x + dx * w, y + dy * w) for (x, y), w in zip(points, weights)]


def normals(points):
    out = []
    for i, p in enumerate(points):
        a, b = points[max(0, i - 1)], points[min(len(points) - 1, i + 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]
        d = math.hypot(dx, dy) or 1
        out.append((-dy / d, dx / d, dx / d, dy / d))      # normal (toward the belly side), tangent
    return out


def strip(spine, widths, offset, ratio):
    """A band running along a tube, ``offset`` of its half-width toward the belly side, ``ratio`` as wide."""
    shifted_ctrl = []
    for (x, y), (nx, ny, _, _), w in zip(spine, normals(spine), widths):
        shifted_ctrl.append((x + nx * w * offset, y + ny * w * offset))
    return ribbon(shifted_ctrl, [w * ratio for w in widths], 8)


def edge_samples(spine, widths, side, every):
    """Points along one edge of a tube spaced ``every`` px: (x, y, outward nx, ny, local width).
    side=-1 is the back (away from the belly)."""
    line = catmull(spine, 10)
    ws = [w for w, _ in catmull([(w, 0) for w in widths], 10)]
    n = min(len(line), len(ws))
    line = line[:n]
    out, acc = [], every * 0.5
    for i in range(1, n):
        acc += math.hypot(line[i][0] - line[i - 1][0], line[i][1] - line[i - 1][1])
        if acc >= every:
            acc = 0
            nx, ny, _, _ = normals(line)[i]
            half = ws[i] / 2
            out.append((line[i][0] + side * nx * half, line[i][1] + side * ny * half, side * nx, side * ny, ws[i]))
    return out


def dirv(angle):
    a = math.radians(angle)
    return math.cos(a), -math.sin(a)


def scale_tex(x, y):
    """A diamond lattice of faint seams with the odd lighter scale: reads as scales, not tiles."""
    return -0.07 if ((x + y) % 6 == 0 or (x - y) % 6 == 0) else (0.03 if (x * 7 + y * 3) % 11 == 0 else 0.0)


def plate_tex(x, y):
    return -0.12 if (x * 0.83 - y * 0.55) % 4.5 < 1.1 else 0.03


def rim_light(img):
    """A one-pixel cool highlight on the silhouette's top-left edge. It sits in the untinted base
    layer, so it stays pale-blue whatever colour the dragon is: sky light, not paint."""
    src = img.copy().load()
    px = img.load()
    for y in range(1, img.height):
        for x in range(1, img.width):
            if src[x, y][3] == 0 or src[x, y] in GLOWS:
                continue
            if (src[x, y - 1][3] == 0 or src[x - 1, y][3] == 0) and src[x, y] in _tinted_scales:
                px[x, y] = RIM


class Dragon:
    """One frame: a pose (stand / windup / breathe / fly / sleep) plus its animation parameters."""

    def __init__(self, pose="stand", legs=0, bob=0, wing=None, jaw=0, eyes_closed=False, glow=0.0, pulse=0.0, wave=0.0, stage=None):
        st = stage or {}
        self.hs, self.ws, self.plate, self.hornk = st.get("hs", 1.0), st.get("ws", 1.0), st.get("plate", 1.0), st.get("horn", 1.0)
        self.baby, self.staged = st.get("baby", False), stage is not None   # young stages: drawn here, then shrunk (see stage_frame)
        self.eye_at = self.mouth_at = self.tail_at = None
        self.pose, self.legs, self.dy = pose, legs, bob
        self.wing, self.jaw, self.closed = wing, jaw, eyes_closed
        self.glow, self.pulse, self.wave = glow, pulse, wave
        self.img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
        self.body_mask = None

    # -- primitives
    def shade(self, mask, colors, **kw):
        kw.setdefault("radius", 2.4)
        return shade_shape(self.img, mask, colors, **kw)

    def over(self, mask, colors, line=None, **kw):
        """Shade a part that overlaps what's already drawn, then ink a dark contour wherever its edge meets that."""
        before = self.img.split()[3].load()
        painted = self.shade(mask, colors, **kw)
        w, h = SIZE
        mk, px = mask.load(), self.img.load()
        line = line or SCALES[1]
        edge = []
        for x, y in painted:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and mk[nx, ny] < 128 and before[nx, ny] > 0:
                    edge.append((x, y))
                    break
        for x, y in edge:
            px[x, y] = line

    def tube(self, spine, widths):
        return mask_of(SIZE, lambda d: d.polygon(ribbon(spine, widths, 8), fill=255))

    def poly(self, pts, colors, **kw):
        return self.shade(mask_of(SIZE, lambda d: d.polygon(pts, fill=255)), colors, **kw)

    def px(self, pts, color):
        for x, y in pts:
            x, y = int(round(x)), int(round(y))
            if 0 <= x < W and 0 <= y < H:
                self.img.putpixel((x, y), color)

    def line(self, pts, color, width=1):
        ImageDraw.Draw(self.img).line([(round(x), round(y)) for x, y in pts], fill=color, width=width)

    def on_body(self, x, y):
        return self.body_mask is not None and self.body_mask.getpixel((int(x), int(y))) > 127 if 0 <= x < W and 0 <= y < H else False

    # -- the rig for each pose: (torso spine, widths, neck spine, widths, tail spine, widths, head anchor, head tilt)
    def rig(self):
        dy, wave = self.dy, self.wave
        if self.pose == "fly":
            torso = [(34, 58), (46, 57), (58, 53), (68, 49), (75, 45)]
            neck = [(70, 48), (77, 42), (84, 37), (90, 33), (95, 32)]
            tail = [(36, 58), (26, 60 + wave), (17, 61 + wave * 1.6), (10, 59 + wave * 2.2), (6, 56 + wave * 2.6)]
            tail_w = [18, 13, 9, 6, 3]
            anchor, tilt = (95, 32), 1
        elif self.pose == "sleep":
            torso = [(30, 84), (42, 81), (54, 76), (66, 73), (74, 74)]
            neck = [(68, 73), (76, 75), (84, 76), (90, 76), (93, 76)]
            tail = [(32, 84), (22, 88), (12, 87), (6, 82), (6, 76)]
            tail_w = [16, 11, 8, 5, 3]
            anchor, tilt = (92, 75), 3
        else:
            lean = {"breathe": 3, "windup": -3}.get(self.pose, 0)
            lift = {"breathe": 3, "windup": -1}.get(self.pose, 0)
            torso = [(34, 68), (44, 63), (52, 54), (58, 43), (62, 33)]
            neck = [(60, 40), (66, 29), (74, 22), (83, 19), (90, 19)]
            neck = blend_shift(neck, lean, lift, (0, 0.1, 0.35, 0.7, 1.0))
            tail = [(38, 68), (28, 73), (17, 77), (9, 73), (5, 66), (8, 58 + wave)]
            tail_w = [18, 13, 9, 6, 4, 3]
            if self.baby:      # a round little body, a short neck and tail, a head too big for all of it
                torso = [(40, 70), (48, 66), (54, 58), (58, 49), (60, 42)]
                neck = [(58, 46), (62, 38), (68, 32), (75, 29), (80, 29)]
                neck = blend_shift(neck, lean, lift, (0, 0.1, 0.35, 0.7, 1.0))
                tail = [(42, 70), (34, 74), (26, 76), (20, 73), (18, 66 + wave)]
                tail_w = [20, 14, 10, 7, 4]
            anchor, tilt = neck[-1], 2 if self.pose == "breathe" else 0
        widths = [14, 27, 31, 27, 17]
        neck_w = [18, 14, 12, 11, 10]
        if self.baby and self.pose not in ("fly", "sleep"):
            widths, neck_w = [20, 34, 38, 32, 22], [22, 18, 16, 15, 14]
        torso, neck, tail = (shifted(s, 0, dy if self.pose != "sleep" else 0) for s in (torso, neck, tail))
        anchor = (anchor[0], anchor[1] + (dy if self.pose != "sleep" else 0))
        return torso, widths, neck, neck_w, tail, tail_w, anchor, tilt

    # -- the whole dragon, back to front
    def render(self):
        self.compose()
        rim_light(self.img)
        return selective_outline(self.img, keep=GLOWS)

    def compose(self):
        """The dragon in design space (128x96 pixels), without the rim light or outline."""
        torso, tw, neck, nw, tail, tailw, anchor, tilt = self.rig()
        self.tail_at = tail[-1]
        flying, sleeping = self.pose == "fly", self.pose == "sleep"
        if not sleeping:
            self.wing_pair(torso, far=True)
        if not flying and not sleeping:
            self.far_leg()
        # one body mask for torso + neck + tail so the shading flows across the joins
        m_torso, m_neck, m_tail = self.tube(torso, tw), self.tube(neck, nw), self.tube(tail, tailw)
        self.body_mask = union(m_torso, m_neck, m_tail)
        self.shade(self.body_mask, SCALES, radius=3.4, tex=scale_tex, depth=0.3)
        self.belly(torso, tw, neck, nw)
        self.ridge(torso, tw, neck, nw, tail, tailw)
        self.veins(torso, neck, tail)
        self.tail_tip(tail)
        if flying:
            self.tucked_legs()
        elif sleeping:
            self.lying_leg()
        else:
            self.near_leg()
            self.fore_arm()
        self.head(anchor, tilt)
        if self.glow > 0 and not flying and not sleeping:
            cx, cy, r = torso[3][0] + 4, torso[3][1] + 4, 2.5 + 2.5 * self.glow
            self.px_disc(cx, cy, r + 2, GLOW_DIM)
            self.px_disc(cx, cy, r, GLOW)
            if self.glow >= 0.8:
                self.px_disc(cx, cy, r - 2, GLOW_HI)
        self.wing_pair(torso, far=False)
        return self.img

    def px_disc(self, cx, cy, r, color):
        ImageDraw.Draw(self.img).ellipse([cx - r, cy - r, cx + r, cy + r], fill=color)

    # ---- body detail
    def belly(self, torso, tw, neck, nw):
        for spine, widths in ((torso, tw), (neck, nw)):
            m = mask_of(SIZE, lambda d: d.polygon(strip(spine, widths, 0.62, 0.5), fill=255))
            clipped = mask_of(SIZE, lambda d: None)
            clipped.paste(m, mask=self.body_mask)
            self.shade(clipped, BELLY, radius=1.6, tex=plate_tex, depth=0.1, bias=0.05)

    def ridge(self, torso, tw, neck, nw, tail, tailw):
        """Bone plates down the back like ship ribs: biggest at the shoulders, shrinking along tail and neck."""
        for spine, widths, every, size, side in ((tail, tailw, 8, 5.0, 1), (torso, tw, 8, 6.5, -1), (neck, nw, 7, 5.0, -1)):
            for x, y, nx, ny, w in edge_samples(spine, widths, side, every):
                s = size * self.plate * min(1.0, 0.4 + w / 26)
                tx, ty = -ny, nx
                a = (x - tx * s * 0.45, y - ty * s * 0.45)
                b = (x + tx * s * 0.45, y + ty * s * 0.45)
                tip = (x + nx * s * 1.25 - tx * s * 0.25, y + ny * s * 1.25 - ty * s * 0.25)
                mask = mask_of(SIZE, lambda d: d.polygon([a, b, tip], fill=255))
                self.shade(mask, HORNS, radius=0.9, depth=0.2)

    def veins(self, torso, neck, tail):
        if self.baby:
            return
        dy = self.dy if self.pose != "sleep" else 0
        hot = VEIN_HI if self.pulse >= 0.5 or self.glow >= 0.5 else VEIN
        if self.pose == "fly":
            paths = [[(44, 56), (44, 52), (50, 52), (50, 48), (56, 48)], [(54, 58), (54, 55), (60, 55)], [(82, 40), (84, 40), (84, 37), (87, 37)]]
        elif self.pose == "sleep":
            paths = [[(44, 80), (44, 76), (50, 76)], [(58, 76), (58, 72), (63, 72)]]
        else:
            paths = [[(42, 60), (42, 55), (47, 55), (47, 50), (52, 50), (52, 45)], [(50, 66), (50, 61), (55, 61)],
                     [(72, 26), (74, 26), (74, 22), (77, 22)], [(26, 70), (26, 66), (21, 66)]]
        for k, path in enumerate(paths):
            color = hot if k % 2 == 0 else VEIN
            for a, b in zip(path, path[1:]):
                self.vein_line((a[0], a[1] + (dy if self.pose != "sleep" else 0)), (b[0], b[1] + (dy if self.pose != "sleep" else 0)), color)

    def vein_line(self, a, b, color):
        n = int(max(abs(b[0] - a[0]), abs(b[1] - a[1]))) + 1
        for i in range(n + 1):
            t = i / max(1, n)
            x, y = round(a[0] + (b[0] - a[0]) * t), round(a[1] + (b[1] - a[1]) * t)
            if self.on_body(x, y):
                self.img.putpixel((x, y), color)

    def tail_tip(self, tail):
        if self.staged:                  # drawn after the shrink, at a size that reads
            return
        x, y = tail[-1]
        hot = GLOW_HI if max(self.glow, self.pulse) >= 0.5 else GLOW
        lean = 1.5 * (self.wave / 2) if self.wave else 0          # the tongue leans with the tail's wave
        d = ImageDraw.Draw(self.img)
        for r, tip, color in ((4.2, 11, GLOW_DIM), (3.2, 8.5, GLOW), (2.2, 6, hot), (1.5, 4.5, GLOW_HI)):
            d.ellipse([x - r, y - r + 1, x + r, y + r + 1], fill=color)                      # a teardrop: round base,
            d.polygon([(x - r * 0.95, y), (x + r * 0.95, y), (x + lean, y - tip)], fill=color)   # tongue drawn upward

    # ---- legs and arms
    def leg_spine(self, dx, dy, stride):
        base = self.dy
        return [(45 + dx, 60 + base + dy), (55 + dx, 68 + base + dy), (50 + dx - stride, 80 + dy * 0.2), (53 + dx - stride * 1.6, 91)]

    def far_leg(self):
        spine = self.leg_spine(-9, -2, -self.legs)
        self.over(self.tube(spine, [20, 13, 9, 7]), SCALES[:4], line=SCALES[0], radius=2.2, tex=scale_tex)
        self.foot(spine[-1][0], dark=True)

    def near_leg(self):
        spine = self.leg_spine(0, 0, self.legs)
        mask = self.tube(spine, [24, 16, 10, 8])
        self.over(mask, SCALES, radius=2.6, tex=scale_tex)
        self.foot(spine[-1][0], dark=False)
        for x, y, nx, ny, w in edge_samples(spine[:3], [24, 16, 10], -1, 7)[:2]:       # knee spurs
            self.shade(mask_of(SIZE, lambda d: d.polygon([(x - 2, y + 1), (x + nx * 4 - 3, y + ny * 4), (x + 2, y - 1)], fill=255)), HORNS, radius=0.8)

    def foot(self, fx, dark):
        y = GROUND
        cols = SCALES[:3] if dark else SCALES
        self.shade(mask_of(SIZE, lambda d: d.polygon([(fx - 8, y - 8), (fx + 5, y - 10), (fx + 15, y - 5), (fx + 16, y), (fx - 9, y)], fill=255)), cols, radius=1.6, tex=scale_tex)
        for k, cx in enumerate((fx + 8, fx + 12)):                                   # toe seams
            self.px([(cx, y - 4 + k), (cx, y - 3 + k), (cx, y - 2 + k), (cx, y - 1 + k)], SCALES[0])
        for k in range(3):                                                           # claws
            cy = y - 6 + k * 2
            self.shade(mask_of(SIZE, lambda d: d.polygon([(fx + 14, cy - 1), (fx + 20 - k, cy + 1), (fx + 14, cy + 2)], fill=255)), HORNS, radius=0.5)

    def fore_arm(self):
        spine = [(56, 50 + self.dy), (62, 58 + self.dy), (69, 61 + self.dy)] if self.baby else [(60, 42 + self.dy), (67, 51 + self.dy), (75, 54 + self.dy)]
        self.over(self.tube(spine, [10, 7, 5]), SCALES, radius=1.8, tex=scale_tex)
        x, y = spine[-1]
        for k in range(3):
            self.shade(mask_of(SIZE, lambda d: d.polygon([(x + k * 2, y - 1 + k), (x + 7 + k, y + 1 + k * 2), (x + k * 2, y + 2 + k)], fill=255)), HORNS, radius=0.6)

    def tucked_legs(self):
        for dx, cols in ((-6, SCALES[:3]), (0, SCALES)):
            spine = [(46 + dx, 58), (54 + dx, 65), (50 + dx, 71), (57 + dx, 72)]
            self.over(self.tube(spine, [15, 11, 8, 6]), cols, line=SCALES[0], radius=1.8, tex=scale_tex)
            for k in range(3):
                self.shade(mask_of(SIZE, lambda d: d.polygon([(56 + dx + k * 2, 70 + k), (63 + dx + k, 72 + k), (56 + dx + k * 2, 73 + k)], fill=255)), HORNS[1:], radius=0.5)

    def lying_leg(self):
        self.shade(mask_of(SIZE, lambda d: d.ellipse([42, 80, 62, 91], fill=255)), SCALES, radius=2.2, tex=scale_tex)
        self.shade(mask_of(SIZE, lambda d: d.polygon([(56, 86), (68, 87), (72, 91), (56, 91)], fill=255)), SCALES[:4], radius=1.2)
        for k in range(3):
            self.shade(mask_of(SIZE, lambda d: d.polygon([(67 + k * 2, 89), (73 + k * 2, 90), (67 + k * 2, 91)], fill=255)), HORNS, radius=0.5)

    # ---- the head: a long, wedge-shaped prow with swept horns
    def head(self, anchor, tilt):
        ax, ay = anchor
        sleeping = self.pose == "sleep"
        jaw = self.jaw

        hs, hk = self.hs, self.hornk

        def P(x, y):
            return (ax + x * hs, ay + y * hs)

        def PH(x, y):                      # horns are budding on the young: shorter, but rooted in the same place
            return (ax + (x * hk if x < 0 else x) * hs, ay + (y if y >= -4 else -4 + (y + 4) * hk) * hs)
        self.eye_at, self.mouth_at = P(12, -2), P(28, 8 + tilt)
        # far horn (behind the skull), near horn
        self.shade(mask_of(SIZE, lambda d: d.polygon(ribbon([PH(2, -6), PH(-3, -11), PH(-10, -14), PH(-19, -14)], [5 * hs, 4 * hs, 2.4, 1], 8), fill=255)), HORNS[:3] + [HORNS[2]], radius=0.9)
        skull = [P(-7, -4), P(2, -8), P(12, -6), P(21, -2 + tilt), P(28, 3 + tilt), P(29, 7 + tilt), P(24, 10 + tilt),
                 P(14, 12), P(4, 13), P(-3, 11), P(-8, 5)]
        self.over(mask_of(SIZE, lambda d: d.polygon(catmull(skull, 5, closed=True), fill=255)), SCALES, radius=2.4, tex=scale_tex, depth=0.3)
        # lower jaw, hinged at the back; mouth glow when open
        if jaw > 0:
            self.poly([P(8, 10), P(26, 8 + tilt), P(27, 9 + tilt + jaw), P(10, 13 + jaw)], [GLOW_DIM, GLOW, GLOW_HI][:3], radius=0.8)
        jawline = [P(4, 11), P(14, 12 + jaw * 0.4), P(24, 10 + tilt + jaw), P(27, 11 + tilt + jaw), P(22, 15 + tilt + jaw * 0.9), P(10, 16 + jaw * 0.35), P(2, 14)]
        self.over(mask_of(SIZE, lambda d: d.polygon(catmull(jawline, 4, closed=True), fill=255)), SCALES[:4], line=SCALES[0], radius=1.6, tex=scale_tex, bias=0.05)
        # fangs along the upper lip
        for k, fx in enumerate((14, 19, 24) if not self.staged else ()):
            x, y = P(fx, 11 + (tilt if fx > 18 else 0) * 0.5)
            self.px([(x, y), (x, y + 1)], HORNS[3])
            self.px([(x + 1, y)], HORNS[2])
        # near horn: swept back like the curl of a prow
        self.shade(mask_of(SIZE, lambda d: d.polygon(ribbon([PH(0, -4), PH(-8, -6), PH(-17, -5), PH(-26, -1)], [6 * hs, 5 * hs, 3, 1.2], 8), fill=255)), HORNS, radius=1.0)
        # cheek spikes and a brow ridge
        for k, (cx, cy) in enumerate(((-7, 5), (-7, 10))):
            self.shade(mask_of(SIZE, lambda d: d.polygon([P(cx + 3, cy - 2), P(cx - 7, cy + 1), P(cx + 3, cy + 2)], fill=255)), HORNS, radius=0.6)
        if not self.staged:
            self.px([P(x, -4 + (x - 5) // 6 + tilt // 2) for x in range(5, 19)], SCALES[5])
        # eye, nostril
        ex, ey = P(12, -2)
        if self.staged:
            return
        if self.closed or sleeping:
            self.px([(ex + k, ey + 2) for k in range(5)], INK)
        else:
            self.px([(ex + k, ey + r) for r in range(3) for k in range(5)], EYES[0])
            self.px([(ex + 2, ey + r) for r in range(3)], INK)                         # slit pupil
            self.px([(ex - 1 + k, ey - 1) for k in range(7)], INK)                     # heavy brow lid
            self.px([(ex + 4, ey)], EYE_LIGHT)                                         # catchlight
        nx_, ny_ = P(26, 3 + tilt)
        self.px([(nx_, ny_), (nx_ + 1, ny_)], INK)
        if jaw > 4:
            self.px_disc(*P(26, 9 + tilt), 1.2, GLOW_HI)

    # ---- wings
    def wing_pair(self, torso, far):
        shoulder = (torso[3][0] - 8, torso[3][1] - 2) if self.pose != "sleep" else (torso[3][0] - 8, torso[3][1] - 2)
        if self.pose == "sleep":
            if not far:
                self.folded_wing(shoulder, low=True, far=False)
            return
        if far:
            s = (shoulder[0] + 5, shoulder[1] - 3)
            if self.wing is not None:
                self.open_wing(s, self.wing + 8, far=True)
        else:
            if self.wing is None:
                self.folded_wing(shoulder, low=False, far=False)
            else:
                self.open_wing(shoulder, self.wing, far=False)

    def membrane(self, pts, root, wrist, far):
        cols = [scale for scale in WINGS[:4]] if far else WINGS
        ref = wrist
        far_d = max(math.hypot(x - ref[0], y - ref[1]) for x, y in pts) or 1

        def tex(x, y):
            return 0.28 * (1 - math.hypot(x - ref[0], y - ref[1]) / far_d) - 0.14 + bayer(x, y) * 0.03
        self.shade(mask_of(SIZE, lambda d: d.polygon(pts, fill=255)), cols, radius=1.0, slope=0.25, depth=0.0, tex=tex)

    def bones(self, joints, fingers, far):
        bone = HORNS[:2] + [HORNS[1]] if far else HORNS[1:]
        for a, b in zip(joints, joints[1:]):
            self.shade(mask_of(SIZE, lambda d: d.polygon(ribbon([a, ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2 - 1), b], [4, 3.6, 3], 5), fill=255)), bone, radius=1.0)
        for t in fingers:
            self.line([joints[-1], t], bone[1], 1)

    def wing_geometry(self, shoulder, angle, reach):
        sx, sy = shoulder
        dx, dy = dirv(angle)
        elbow = (sx + dx * 18 * reach, sy + dy * 18 * reach - 2)
        wrist = (sx + dx * 34 * reach, sy + dy * 34 * reach - 3)
        tips = []
        trail = 1 if angle < 180 else -1             # fingers trail behind the arm on the up and the down stroke
        for da, length in ((24, 30), (54, 32), (84, 26)):
            fx, fy = dirv(angle + trail * da)
            tips.append((wrist[0] + fx * length * reach, wrist[1] + fy * length * reach))
        root = (sx - 8, sy + 8)
        pts = [shoulder, elbow, wrist, tips[0]]
        for a, b in zip(tips, tips[1:]):
            mid = ((a[0] + b[0]) / 2 * 0.72 + root[0] * 0.28, (a[1] + b[1]) / 2 * 0.72 + root[1] * 0.28)
            pts += [mid, b]
        pts += [root]
        return elbow, wrist, tips, pts

    def open_wing(self, shoulder, angle, far):
        reach = self.ws
        for _ in range(14):                        # a wing swung up or back is foreshortened to stay in frame
            elbow, wrist, tips, pts = self.wing_geometry(shoulder, angle, reach)
            if all(5 <= x <= W - 6 and 6 <= y <= H - 4 for x, y in pts):
                break
            reach *= 0.93
        root = (shoulder[0] - 8, shoulder[1] + 8)
        self.membrane(pts, root, wrist, far)
        self.veins_membrane(wrist, tips, far)
        self.bones([shoulder, elbow, wrist], tips, far)
        self.thumb(wrist, angle, far)

    def veins_membrane(self, wrist, tips, far):
        col = WINGS[1] if far else WINGS[1]
        for t in tips:
            mx, my = (wrist[0] + t[0]) / 2, (wrist[1] + t[1]) / 2
            self.line([(wrist[0] * 0.7 + t[0] * 0.3, wrist[1] * 0.7 + t[1] * 0.3), (mx, my + 3)], col, 1)

    def thumb(self, wrist, angle, far, length=9):
        fx, fy = dirv(angle - 40)
        self.shade(mask_of(SIZE, lambda d: d.polygon([(wrist[0] - 2, wrist[1] - 1), (wrist[0] + fx * length, wrist[1] + fy * length), (wrist[0] + 2, wrist[1] + 2)], fill=255)), HORNS[1:] if not far else HORNS[:3], radius=0.6)

    def folded_wing(self, shoulder, low, far):
        sx, sy = shoulder
        if low:   # asleep: folded flat along the back
            pts = [(sx, sy + 4), (sx - 10, sy - 6), (sx - 24, sy - 8), (sx - 28, sy + 2), (sx - 20, sy + 8), (sx - 8, sy + 10)]
            self.membrane(pts, (sx, sy + 8), (sx - 24, sy - 8), far)
            self.line([(sx, sy + 3), (sx - 12, sy - 6), (sx - 24, sy - 8)], HORNS[2], 2)
            return
        sy += self.dy
        ws = self.ws

        def o(dx, dy):
            return (sx + dx * ws, sy + dy * ws)
        elbow, wrist = o(-8, -13), o(-18, -30)
        tips = [o(-34, -12), o(-27, 1), o(-14, 6)]
        root = o(-2, 8)
        pts = [(sx, sy), elbow, wrist, tips[0], ((tips[0][0] + tips[1][0]) / 2 + 3 * ws, (tips[0][1] + tips[1][1]) / 2 - 2 * ws), tips[1],
               ((tips[1][0] + tips[2][0]) / 2 + 2 * ws, (tips[1][1] + tips[2][1]) / 2 - 2 * ws), tips[2], root]
        self.membrane(pts, root, wrist, far)
        self.veins_membrane(wrist, tips, far)
        self.bones([(sx, sy), elbow, wrist], tips, far)
        self.thumb(wrist, 110, far, length=6)


# ---------------------------------------------------------------- sheets

MOUTH = (121, 30)    # the snout tip in the breathing pose
SADDLE = (50, 44)    # on the back between wings and hips; riders' feet go here


def build_sequence(frame_fn, step=1):
    """The tags the game asks for. ``step`` converts the adult's design-unit bobs and strides to canvas pixels."""
    f = frame_fn

    def b(v):
        return v * step
    seq = Sequencer()
    seq.add("idle", [f("stand", bob=b(bo), pulse=g) for bo, g in ((0, 0), (0, 0.3), (1, 0.6), (1, 0.3))], 220)
    seq.add("walk", [f("stand", legs=b(l), bob=b(bo)) for l, bo in ((3, 0), (0, -1), (-3, 0), (0, -1))], 130)
    flap = [62, 98, 150, 225, 262, 160]          # up, over the top, down and forward, then recovering
    seq.add("fly", [f("fly", wing=a, wave=math.sin(i / 6 * math.tau) * 2, bob=b([0, 0, -1, -2, -2, -1][i])) for i, a in enumerate(flap)], 80)
    seq.add("glide", [f("fly", wing=a, wave=w) for a, w in ((168, 0.5), (164, -0.5))], 260)
    seq.add("takeoff", [f("stand", wing=95, bob=b(3)), f("stand", wing=235, bob=b(-1)), f("fly", wing=110)], 110, loop=False)
    seq.add("land", [f("fly", wing=100), f("stand", wing=130, bob=b(-1)), f("stand", bob=b(2))], 120, loop=False)
    seq.add("breathe_windup", [f("windup", glow=g) for g in (0.4, 0.9)], 180)
    seq.add("breathe", [f("breathe", jaw=j) for j in (3, 5, 5)], 130, loop=False)
    seq.add("sleep", [f("sleep", bob=b(bo)) for bo in (0, 1)], 900)
    seq.add("downed", [f("sleep")], 1000, loop=False)
    # Being cared for (CreatureAnimator.CareTag): eyes shut and leaning into a scratch; bouncing
    # about at play; wings eased open while its scales are brushed.
    seq.add("pet", [f("stand", eyes_closed=True, bob=b(bo), pulse=g) for bo, g in ((1, 0.3), (2, 0.6), (1, 0.3), (0, 0))], 260)
    seq.add("play", [f("stand", legs=b(l), bob=b(bo), wing=wg) for l, bo, wg in ((3, -2, 120), (0, 1, None), (-3, -2, 140), (0, 1, None))], 120)
    seq.add("groom", [f("stand", wing=wg, eyes_closed=True, bob=b(bo)) for wg, bo in ((105, 0), (125, 1), (140, 1), (125, 0))], 360)
    return seq


def write_dragon(name, seq, pivot, points):
    """The plain composite plus the _base and tinted layers, all sharing the frame layout."""
    write_sheet(name, seq.frames, seq.tags, pivot, points)
    split = [split_layers(img, LAYER_SHADES) for img, _ in seq.frames]
    for layer in ["base", *LAYER_SHADES]:
        frames = [(parts[layer], ms) for parts, (_, ms) in zip(split, seq.frames)]
        write_sheet(f"{name}_{layer}", frames, seq.tags, pivot, points, trim=layer != "base")


PREFIX = "creatures/dragon_norse"

# The adult is drawn 1.2x the design (154x116 instead of 128x96): Norse adults are size 2.6 to the
# Hearth dragon's 2.2, whose sheet covers 128x96 world pixels. The resample keeps the palette
# exact (downscale works both ways), and the rim light and outline go on at the final size.
ADULT_K = 1.2
ADULT_CANVAS = (154, 116)
ADULT_PIVOT = (round(PIVOT[0] * ADULT_K), round(PIVOT[1] * ADULT_K))


def _adult_point(p):
    ox, oy = ADULT_PIVOT[0] - PIVOT[0] * ADULT_K, ADULT_PIVOT[1] - PIVOT[1] * ADULT_K
    return int(p[0] * ADULT_K + ox), int(p[1] * ADULT_K + oy)


def frame(pose, **kw):
    """One adult frame at the sheet's size."""
    ox, oy = ADULT_PIVOT[0] - PIVOT[0] * ADULT_K, ADULT_PIVOT[1] - PIVOT[1] * ADULT_K
    img = downscale(Dragon(pose, **kw).compose(), ADULT_K, ADULT_CANVAS, ox, oy)
    rim_light(img)
    return selective_outline(img, keep=GLOWS)


def adult_sheet():
    seq = build_sequence(frame)
    write_dragon(f"{PREFIX}_adult", seq, ADULT_PIVOT, {"mouth": _adult_point(MOUTH), "saddle": _adult_point(SADDLE)})


# ---------------------------------------------------------------- the young

_WEIGHT = {GLOW: 2.5, GLOW_HI: 2.5, GLOW_DIM: 2.0, VEIN: 2.5, VEIN_HI: 2.5, INK: 1.6, EYE_LIGHT: 1.6}


def downscale(big, k, size, ox, oy):
    """Shrink a design-space frame by k onto a canvas: each pixel takes the most common colour among
    the source pixels it covers, so the palette stays exact for the layer split. Glow and ink count
    extra, so thin veins and highlights survive the shrink."""
    out = Image.new("RGBA", size, (0, 0, 0, 0))
    src, dst = big.load(), out.load()
    for ty in range(size[1]):
        y0, y1 = (ty - oy) / k, (ty + 1 - oy) / k
        for tx in range(size[0]):
            x0, x1 = (tx - ox) / k, (tx + 1 - ox) / k
            counts, total, opaque = {}, 0, 0
            for sy in range(max(0, math.floor(y0)), min(big.height, math.ceil(y1))):
                for sx in range(max(0, math.floor(x0)), min(big.width, math.ceil(x1))):
                    total += 1
                    c = src[sx, sy]
                    if c[3]:
                        opaque += 1
                        counts[c] = counts.get(c, 0.0) + _WEIGHT.get(c, 1.0)
            if total and opaque / total >= 0.45:
                dst[tx, ty] = max(counts, key=counts.get)
    return out


# canvas, scale from the design, pivot on the canvas, proportions, tail flame (radius, height), eye (w, h).
# Each is the Hearth stage's size times the breeds' size ratio (juvenile 1.5/1.3, hatchling 0.9/0.8).
STAGES = {
    "juvenile": dict(canvas=(78, 66), k=0.575, pivot=(37, 59), stage=dict(hs=1.2, ws=0.85, plate=0.85, horn=0.75), flame=(2.0, 6), eye=(3, 3)),
    "hatchling": dict(canvas=(58, 44), k=0.34, pivot=(27, 37), stage=dict(hs=1.5, ws=0.6, plate=0.6, horn=0.4, baby=True), flame=(1.4, 4), eye=(4, 4)),
}


def stage_frame(name, pose, **kw):
    """A young dragon: the adult rig with the stage's proportions, shrunk, then given the details that
    only read at small size: a big bright eye, the tail flame, the outline."""
    cfg = STAGES[name]
    k, canvas, pivot = cfg["k"], cfg["canvas"], cfg["pivot"]
    ox, oy = pivot[0] - PIVOT[0] * k, pivot[1] - PIVOT[1] * k
    d = Dragon(pose, stage=cfg["stage"], **kw)
    img = downscale(d.compose(), k, canvas, ox, oy)

    def at(p):
        return int(math.floor(p[0] * k + ox)), int(math.floor(p[1] * k + oy))
    dr = ImageDraw.Draw(img)
    tx, ty = at(d.tail_at)
    r, tip = cfg["flame"]
    hot = GLOW_HI if max(d.glow, d.pulse) >= 0.5 else GLOW
    lean = 0.5 * (d.wave / 2) if d.wave else 0
    for rr, tt, color in ((r, tip, GLOW_DIM), (r * 0.72, tip * 0.78, GLOW), (r * 0.5, tip * 0.55, GLOW_HI)):    # the heart is always the hot colour
        dr.ellipse([tx - rr, ty - rr + 0.5, tx + rr, ty + rr + 0.5], fill=color)
        dr.polygon([(tx - rr * 0.9, ty), (tx + rr * 0.9, ty), (tx + lean, ty - tt)], fill=color)
    ex, ey = at(d.eye_at)
    w, h = cfg["eye"]
    ex, ey = ex - w // 2, ey - h // 2
    if d.closed or pose == "sleep":
        dr.line([(ex, ey + h - 1), (ex + w - 1, ey + h - 1)], fill=INK)
    else:
        dr.rectangle([ex, ey, ex + w - 1, ey + h - 1], fill=EYES[0])
        dr.line([(ex + w // 2, ey), (ex + w // 2, ey + h - 1)], fill=INK)                  # slit pupil
        dr.line([(ex - 1, ey - 1), (ex + w, ey - 1)], fill=INK)                            # brow lid
        img.putpixel((ex + w - 1, ey), EYE_LIGHT)
    rim_light(img)
    return selective_outline(img, keep=GLOWS)


def stage_sheet(name, hatch=False):
    cfg = STAGES[name]
    k, pivot = cfg["k"], cfg["pivot"]
    seq = build_sequence(lambda pose, **kw: stage_frame(name, pose, **kw), step=round(1 / k))
    ox, oy = pivot[0] - PIVOT[0] * k, pivot[1] - PIVOT[1] * k
    d = Dragon("breathe", jaw=5, stage=cfg["stage"])
    d.compose()
    mouth = (int(d.mouth_at[0] * k + ox), int(d.mouth_at[1] * k + oy))
    if hatch:
        start = len(seq.frames)
        seq.frames.extend(hatch_frames(cfg["canvas"], None, pivot, draw=lambda closed: stage_frame(name, "stand", eyes_closed=closed)))
        seq.tags["hatch"] = (start, len(seq.frames) - 1, False, 2)
    write_dragon(f"{PREFIX}_{name}", seq, pivot, {"mouth": mouth})


# ---------------------------------------------------------------- element-coloured fire effects

def core_fx(seed, s=32):
    """One frame of the breath's core in white, a touch larger than the orange flipbook's yellow core
    (fx/breath, same seed and wobble) so it covers it: the game draws it over each flame tinted with
    the breather's element colour."""
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def blob(cx, cy, r, col):
        pts = []
        for k in range(12):
            a = k / 12 * math.tau
            wob = 1 + 0.18 * math.sin(a * 3 + seed * 1.9) + 0.1 * math.sin(a * 5 + seed)
            stretch = 1.35 if math.sin(a) < 0 else 1.0           # tongues lick upward
            pts.append((cx + math.cos(a) * r * wob, cy + math.sin(a) * r * wob * stretch))
        d.polygon(pts, fill=col)
    blob(16, 20, 5.3, (255, 255, 255, 255))
    return img


def fire_fx():
    seq = Sequencer()
    seq.add("burn", [core_fx(k) for k in range(4)], 60)
    write_sheet("fx/breath_core", seq.frames, seq.tags, (16, 31))


def generate():
    """The Norse breed's sheets. The breath core (fire_fx) is shared by every breed and made by
    tools/gen_placeholder_art.py on its own."""
    adult_sheet()
    stage_sheet("juvenile")
    stage_sheet("hatchling", hatch=True)


def preview(out):
    """Every stage's idle, walk, fly and breathe frames side by side, plus the hatch."""
    from norse_art import contact_sheet
    rows = []
    for _name, fn in (("hatchling", lambda pose, **kw: stage_frame("hatchling", pose, **kw)),
                     ("juvenile", lambda pose, **kw: stage_frame("juvenile", pose, **kw)),
                     ("adult", frame)):
        rows.extend([fn(p, **kw) for p, kw in (("stand", {}), ("stand", {"legs": 3}), ("fly", {"wing": 150}),
                                               ("windup", {"glow": 0.9}), ("breathe", {"jaw": 5}), ("sleep", {}))])
    contact_sheet(rows, out, scale=3, columns=6)


if __name__ == "__main__":
    import sys
    if "--preview" in sys.argv:
        out = os.path.join(os.path.dirname(__file__), "..", ".cache", "norse_dragons.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        generate()
        preview(out)
        print(out)
    else:
        generate()
