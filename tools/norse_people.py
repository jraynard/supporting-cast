"""Norse villagers for the HD-2D art pass: Octopath-style people about three heads tall (32x56
frames, feet on row 54) in linen, wool and leather, built as layers the game recolours.

Each frame is drawn as a composite using *marker* colours for the parts the game tints, then split
(``placeholder_sheets.split_layers``) into greyscale tint layers, as docs/ART.md describes:

* ``creatures/human_<body>_<hair>``: the bare person (linen underclothes, outline) plus ``_skin``
  and ``_hair`` (brows included) tint layers. No eyes: those come from the eye-style layers.
* ``creatures/human_<body>_eyes_<style>`` (iris, tinted) and ``_face_<style>`` (lashes and
  highlights, as is).
* ``creatures/human_<body>_beard_<style>``: tinted with the hair colour, plus ``_line`` for the
  pixels it adds to the outline.
* ``apparel/<item>_<body>_<short|long>``: each garment or piece of armour alone (dressed minus
  bare), with a greyscale ``<item>_dye_...`` layer the game tints with the cloth's dye, and for
  cloaks a ``<item>_back_...`` panel drawn under the whole person.

The marker ramps are the game's default look (HumanLook.Default) scaled by each step's grey, so
the plain ``creatures/human`` sheet (dressed in a woad tunic) matches an untouched layered person.

Four-way: ``_down`` (facing the viewer), ``_up`` and ``_side`` (facing right, the game mirrors it).
Idle/walk/work per view, plus the flat top-down ``sleep``/``downed`` poses for beds.
"""
import math

from PIL import Image, ImageDraw

from norse_art import (mask_of, union, shade_shape, shade_over, ramp, ribbon, spine_points, selective_outline, bayer, scaled, mix)
from placeholder_sheets import Sequencer, write_sheet, split_layers, diff_layer

W, H = 40, 60
FEET = 54            # the design row the soles rest on (the image row is FEET + OY)
OY = 4               # design y → image y: headroom, so a big crown lifted by an idle breath isn't cut off
PIVOT = (20, FEET + OY)
SIZE = (W, H)
# Figures are designed on a 32-wide grid centred on x = 15.5 (the old frame) and drawn through
# Fig.T, which centres them in the wider frame and broadens the body: people aren't pencils.
OX = 4               # design x → image x
CX = 15.5            # the design's middle
STRETCH = {"head": 1.0, "body": 1.0, "legs": 1.1}


def Y(y):
    """Design row → image row (before the headroom): the head and body are drawn a touch shorter and
    the legs a touch longer than designed, for a leaner figure; the soles stay on FEET."""
    hip, hip_to = 34, 32
    return hip_to - (hip - y) * 0.94 if y < hip else hip_to + (y - hip) * (FEET - hip_to) / (FEET - hip)

# ---------------------------------------------------------------- tint-layer markers

SKIN_TINT, HAIR_TINT, EYE_TINT = (232, 190, 150), (90, 58, 34), (60, 42, 30)
SKIN_GREYS = (150, 186, 214, 238, 255)
HAIR_GREYS = (96, 132, 172, 214, 255)
BEARD_GREYS = (100, 136, 176, 218, 250)        # the hair colour too, but its own layer
DYE_GREYS = (72, 112, 152, 200, 232)           # dyed cloth; 200 is the dye itself (HumanLook.Dye)


def _tinted(tint, greys):
    return [tuple(round(c * g / 255) for c in tint) + (255,) for g in greys]


SKIN = _tinted(SKIN_TINT, SKIN_GREYS)
HAIR = _tinted(HAIR_TINT, HAIR_GREYS)
BEARD = _tinted(HAIR_TINT, BEARD_GREYS)
EYE = EYE_TINT + (255,)
EYE_GREYS = (150, 205, 255)                    # the iris darkens toward its top
EYE_DARK, EYE_MID = (tuple(round(c * g / 255) for c in EYE_TINT) + (255,) for g in EYE_GREYS[:2])
DYE = [(g, g, g, 255) for g in DYE_GREYS]

LAYER_SHADES = {
    "skin": dict(zip(SKIN, SKIN_GREYS)),
    "hair": dict(zip(HAIR, HAIR_GREYS)),
}
EYE_SHADES = {"eyes": {EYE_DARK: EYE_GREYS[0], EYE_MID: EYE_GREYS[1], EYE: 255}}
BEARD_SHADES = {"beard": dict(zip(BEARD, BEARD_GREYS))}
DYE_SHADES = {"dye": dict(zip(DYE, DYE_GREYS))}

# ---------------------------------------------------------------- clothing palette (untinted)

WOAD = ramp((62, 92, 152), 5)          # the plain sheet's tunic (and the default dye)
MADDER = ramp((156, 60, 52), 5)
LINEN = ramp((212, 198, 168), 5)       # underclothes
BRAIES = ramp((190, 175, 146), 5)
WALNUT = ramp((108, 80, 56), 5)
WRAP = ramp((122, 118, 116), 4)        # leg wraps: undyed grey wool        # leg wraps (winingas)
SHOE = ramp((84, 58, 40), 4)
LEATHER = ramp((130, 86, 50), 5)
GLOVE = ramp((182, 106, 54), 5)        # tanned bracers and gloves: a warm accent
DARK_LEATHER = ramp((92, 76, 74), 5)   # oiled, blackened: the vest
FUR = ramp((178, 158, 128), 4)
GOLD = ramp((220, 176, 70), 4)         # tablet-woven trim, buckles
BRONZE = ramp((178, 126, 64), 4)
HORN = ramp((228, 216, 186), 4, dark=0.55, light=1.12)
IRON = ramp((150, 158, 174), 5)
MAIL = ramp((132, 142, 160), 5)
WOODC = ramp((136, 96, 58), 4)
TRIM_DARK = ramp((70, 52, 60), 3)      # the woven band's ground
WHITE = (244, 240, 238, 255)
LASH = (52, 30, 38, 255)
LID = (178, 120, 104, 255)
BLUSH = (232, 146, 124, 255)
MOUTH = (170, 88, 82, 255)
BEAD_BLUE = (92, 140, 200, 255)
BEAD_AMBER = (232, 168, 60, 255)

HAIR_STYLES = ("short", "long", "ponytail", "pigtails", "curlyshort", "curlylong", "none",
               "sidebraid", "topknot", "bun", "crownbraid", "shaggy", "swept")
LONG_HAIR = ("long", "curlylong")        # what the worn layers' _long variants are drawn over
EYE_STYLES = ("round", "narrow", "sleepy", "bright")
STANCES = ("crossed", "hip", "coin")    # idle attitudes besides the plain one (Appearance.Stance)
BEARDS = ("stubble", "short", "full", "braided")

# Garments: the things people wear that aren't armour. Dyed ones are drawn in the DYE markers.
TUNICS = ("tunic", "kaftan", "apron_dress", "vest")
LEGWEAR = ("trousers", "skirt")
FOOTWEAR = ("turnshoes", "boots")
CLOAKS = ("cloak", "hooded_cloak", "fur_mantle", "mantle", "scarf")
GARMENTS = (*LEGWEAR, *FOOTWEAR, *TUNICS, *CLOAKS)
BACK_PANELS = ("cloak", "hooded_cloak")   # have a _back layer drawn under the person
BODY_ITEMS = ("leather_jerkin", "chain_shirt")
HEAD_ITEMS = ("leather_cap", "iron_helm", "horned_helm")
BACK_ITEMS = ("satchel",)

# The plain sheet's outfit, and what the tests of the default look expect.
DEFAULT_WEAR = ("trousers", "turnshoes", "tunic")

LEG_X = (10.5, 17.5)   # each leg's left edge (front design): a little apart, so they stand easy


def circles_mask(items, size=None):
    """A mask from (x, y, radius) discs: the bumps of curly hair and the bulbs of its tails."""
    def fn(d):
        for x, y, r in items:
            d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    return mask_of(size or SIZE, fn)


def arc_discs(cx, cy, rx, ry, a0, a1, n, r):
    """n discs of radius r along an elliptical arc (degrees, 0 = right, 90 = down)."""
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * k / max(1, n - 1))),
             cy + ry * math.sin(math.radians(a0 + (a1 - a0) * k / max(1, n - 1))), r) for k in range(n)]


def curl_tex(x, y):
    """Little rings, offset every other row, so curly hair reads as ringlets and not as a lumpy cap."""
    u, v = (x + (2 if (y // 3) % 2 else 0)) % 4, y % 3
    if (u, v) in ((0, 1), (3, 1), (1, 0), (2, 0), (1, 2), (2, 2)):
        return -0.16 + bayer(x, y) * 0.03
    return 0.06 + bayer(x, y) * 0.03


def lock_tex(x, y):
    """Straight hair: vertical clumps (every fourth column a shade darker), barely dithered."""
    return (-0.12 if x % 4 == 0 else 0.03 if x % 4 == 2 else 0) + bayer(x, y) * 0.03


def braid_tex(x, y):
    """A three-strand plait seen flat: chevrons stepping down."""
    return -0.18 if (y + abs((x % 4) - 1.5)) % 3 < 1 else 0.05


def darker(colors, k=0.72):
    """The far side of a limb: marker ramps (tinted later) keep to their darker steps so the split
    still finds them; ordinary colours are scaled."""
    if colors[0] in (SKIN[0], HAIR[0], BEARD[0], DYE[0]):
        return list(colors[:max(2, len(colors) - 2)])
    return [scaled(c, k) for c in colors]


def cloth_tex(x, y):
    return bayer(x, y) * 0.07


def wool_tex(x, y):
    return (-0.05 if (x + 2 * y) % 5 == 0 else 0) + bayer(x, y) * 0.06


class TDraw:
    """An ImageDraw that maps coordinates first: the three-quarter views draw the front and back
    designs through a turn (``Fig.T``), so every garment, hair style and piece of armour gets them."""

    def __init__(self, d, f):
        self.d, self.f = d, f

    def _box(self, box):
        x0, y0 = self.f(box[0], box[1])
        x1, y1 = self.f(box[2], box[3])
        return [min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)]

    def polygon(self, pts, fill=255):
        self.d.polygon([self.f(x, y) for x, y in pts], fill=fill)

    def ellipse(self, box, fill=255):
        self.d.ellipse(self._box(box), fill=fill)

    def rectangle(self, box, fill=255):
        self.d.rectangle(self._box(box), fill=fill)

    def pieslice(self, box, a0, a1, fill=255):
        self.d.pieslice(self._box(box), a0, a1, fill=fill)


class Fig:
    """One standing person in one pose. Draw order is back to front; ``by`` lifts the upper body
    (idle breathing, walk bounce) while the feet stay planted.

    ``eyes`` None leaves the face blank (the base sheets: eyes are their own layers). ``beard``
    is a style from BEARDS. ``wear`` lists garments and armour; ``dye`` is the ramp dyed cloth is
    drawn in (the DYE markers unless a composite wants a real colour). ``part`` draws only a
    cloak's back panel (``"back"``) or everything but it (``"front"``).

    Views: ``down`` (the front), ``up`` (the back) and the three-quarter ``downside`` and ``upside``
    (turned to face screen right, the game mirrors them): those draw the front or back design
    through a turn (``T``: narrower, the head turned further than the body), with the far arm
    behind the body and the face drawn for the turn. ``stance`` is an idle attitude (STANCES)."""

    def __init__(self, view="down", legs=0, bob=0, blink=False, arm=None, body="male", hair="short",
                 eyes=None, beard=None, wear=(), dye=None, part=None, stance=None, beat=0, walk=None):
        self.turn = view if view in ("downside", "upside") else None
        view = {"downside": "down", "upside": "up"}.get(view, view)
        self.stance, self.beat = stance, beat
        self.walk = walk
        self.view, self.legs, self.by = view, legs, -bob
        self.blink, self.arm = blink, arm
        self.female = body == "female"
        self.hair = hair
        self.long = hair in LONG_HAIR
        self.curly = hair.startswith("curly")
        self.eyes, self.beard = eyes, beard
        self.wear = tuple(wear)
        self.dye = dye or DYE
        self.part = part
        self.img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)

    def has(self, *items):
        return any(i in self.wear for i in items)

    # -- the three-quarter turn
    # The three-quarter turn: how wide the near and far halves stay, and how far the head, body
    # and legs shift toward where they face. Deeper turns foreshorten the far side more.
    TURNS = {   # yaw: the head's turn in degrees (it rotates on a cylinder; the body is squashed)
        "shallow": dict(near=0.84, far=0.84, head=1.4, body=0.7, legs=0.25, yaw=25),
        "angled": dict(near=1.0, far=0.8, head=2.6, body=1.0, legs=0.4, yaw=40),
        "deep": dict(near=0.98, far=0.48, head=3.4, body=1.6, legs=0.6, yaw=55),
    }
    HEAD_R = 7.5      # the head's radius across, for its turn
    TURN = "angled"

    def near_side(self, dx):
        """Whether a point (offset from the middle) is on the near side: turned toward the viewer
        that's screen left; turned away, screen right."""
        return (dx < 0) == (self.turn == "downside")

    def region(self, y):
        return "head" if y < 19.5 + self.by else "body" if y < 34 else "legs"

    def T(self, x, y):
        """Design coordinates to the image. Front and back: centred in the frame, the body and legs
        broadened (STRETCH). Turned: the near half keeps most of its width, the far half is
        foreshortened, and the head shifts toward where they face further than the body (away
        from it, seen from behind)."""
        dx = (x - CX) * STRETCH[self.region(y)]
        if not self.turn:
            return (CX + OX + dx, Y(y) + OY)
        t = self.TURNS[self.TURN]
        if y < 19.5 + self.by and self.view == "down":
            # The face turns on a cylinder: features slide toward where they look and crowd the
            # far cheek, the near side opens onto the back of the head (drawn by head_back).
            a = math.radians(t["yaw"])
            r = self.HEAD_R
            depth = math.sqrt(max(0.0, r * r - dx * dx))
            return (CX + OX + dx * math.cos(a) + depth * math.sin(a) - r * math.sin(a) * 0.35, Y(y) + OY)
        k = t["near"] if self.near_side(dx) else t["far"]
        shift = t["head"] if y < 19.5 else t["body"] if y < 34 else t["legs"]
        if self.turn == "upside":
            shift = -shift
        return (CX + OX + dx * k + shift, Y(y) + OY)

    def mask(self, fn):
        return mask_of(SIZE, lambda d: fn(TDraw(d, self.T)))

    def circles(self, items):
        """Discs (x, y, r) through the turn: ellipses, narrower across."""
        def fn(d):
            for x, y, r in items:
                cx, cy = self.T(x, y)
                k = STRETCH[self.region(y)] * (1.0 if not self.turn else self.TURNS[self.TURN]["near" if self.near_side(x - CX) else "far"])
                d.ellipse([cx - r * k, cy - r, cx + r * k, cy + r], fill=255)
        return mask_of(SIZE, fn)

    # -- tiny helpers
    def shape(self, fn, colors, over=None, **kw):
        kw.setdefault("radius", 1.3)
        m = self.mask(fn)
        if over is not None:
            shade_over(self.img, m, colors, over, **kw)
        else:
            shade_shape(self.img, m, colors, **kw)
        return m

    def poly(self, pts, colors, **kw):
        return self.shape(lambda d: d.polygon(pts, fill=255), colors, **kw)

    def rib(self, spine, widths, colors, **kw):
        return self.shape(lambda d: d.polygon(ribbon(spine, widths, 4), fill=255), colors, **kw)

    def px(self, pts, color):
        """Pixels at design coordinates (through the turn)."""
        for x, y in pts:
            self.raw(self.T(x, y)[0], y, color)

    def raw(self, x, y, color):
        """A pixel at an image column and a design row (the frame's headroom, OY, added)."""
        x, y = int(round(x)), int(round(Y(y) + OY))
        if 0 <= x < W and 0 <= y < H:
            self.img.putpixel((x, y), color)

    def hline(self, x0, x1, y, color):
        a, b = round(self.T(x0, y)[0]), round(self.T(x1, y)[0])
        for x in range(a, b + 1):
            self.raw(x, y, color)

    def opaque(self, x, y):
        x, y = (round(c) for c in self.T(x, y))
        return 0 <= x < W and 0 <= y < H and self.img.getpixel((x, y))[3] > 0

    def trim(self, x0, x1, y, rows=2):
        """Tablet-woven braid: a gold zig-zag on a dark ground, only over what's drawn."""
        a, b = round(self.T(x0, y)[0]), round(self.T(x1, y)[0])
        for x in range(a, b + 1):
            for r in range(rows):
                yy = round(Y(y + r) + OY)
                if not (0 <= x < W and 0 <= yy < H) or self.img.getpixel((x, yy))[3] == 0:
                    continue
                on = (x // 2 + r) % 2 == 0
                self.raw(x, y + r, GOLD[2] if on else TRIM_DARK[1])

    # -- the whole figure
    def render(self):
        v = self.view
        if self.part == "back":
            self.cloak_back()
            return selective_outline(self.img)
        if self.part != "front":
            self.cloak_back()
        if v == "side":
            self.leg_side(-self.legs, far=True)
            self.arm_side(far=True)
        self.hair_back()
        if v != "side":
            self.legs_front_back()
        else:
            self.leg_side(self.legs, far=False)
        self.legwear()
        self.footwear()
        far = self.far_arm()
        if far is not None:
            self.arms(only=far)                  # turned: the far arm is behind the body
        self.torso()
        self.tunic()
        self.armor()
        overhead = v == "side" and isinstance(self.arm, (int, float))   # a raised arm passes in front of the face
        if not overhead:
            self.arms(only=None if far is None else not far)
        self.back_gear()
        self.cloak_front()
        self.head()
        self.scarf_front()
        self.beard_front()
        self.hair_front()
        self.head_armor()
        if overhead:
            self.arms()
        return selective_outline(self.img)

    def far_arm(self):
        """Which arm is behind the body when turned (True: the screen-left one), else None. Facing
        screen right and the viewer, the screen-right arm swings back; seen from behind, the left."""
        if not self.turn:
            return None
        return self.turn == "upside"

    # ================================================================ body

    def hip_y(self):
        return 36

    def toes(self, left):
        """How far a foot's toe reaches past it (left, right), so feet point somewhere: outward a
        little from the front, both the way they face when turned, none from behind (heels)."""
        if self.view == "up":
            return 0, 0
        if self.turn == "downside":
            return 0, 2.5
        return (1, 0) if left else (0, 1)

    def foot(self, x0, x1, top, bottom, colors, left, **kw):
        """A foot or shoe from ``top`` down to the sole, its toe pushed out (``toes``)."""
        tl, tr = self.toes(left)
        self.poly([(x0 + 0.5, top), (x1 - 0.5, top), (x1, bottom - 2), (x1 + tr, bottom - 1), (x1 + tr, bottom), (x0 - tl, bottom),
                   (x0 - tl, bottom - 1), (x0, bottom - 2)], colors, **kw)

    def leg_x(self):
        """Each leg's left edge at the hip and at the ankle: a leg runs from one to the other, so
        a leg set wide angles out from the hip. Turned, the far leg steps out and the near one
        plants a little under the body: standing at ease."""
        if self.turn:
            return ((LEG_X[0], LEG_X[0] - 1), (LEG_X[1] - 0.5, LEG_X[1] + 3.5))
        return ((LEG_X[0], LEG_X[0] - 0.4), (LEG_X[1], LEG_X[1] + 0.4))

    def leg_pose(self, i):
        """Where leg i (0: the screen-left one) puts its foot this frame: (dx, dy, lift) in design
        pixels: across, down the screen, and up off the ground.

        Walking (``walk`` 0..3) is a real step, not feet pumping in place: contact (one leg reaches
        forward, the other trails, both feet down), passing (the planted foot under the hip, the
        other swinging through lifted), then the same with the legs swapped. Turned, the stride runs
        along the way they face (screen right); from the front the forward foot comes a pixel
        nearer the viewer and the trailing heel lifts; from behind, the other way round."""
        if self.walk is None:
            return 0, 0, (max(0, self.legs) if i == 0 else max(0, -self.legs))
        reach = (1, 0, -1, 0)[self.walk % 4] * (1 if i == 0 else -1)      # +1 forward, -1 trailing
        swinging = (self.walk % 4 == 1 and i == 1) or (self.walk % 4 == 3 and i == 0)
        if self.turn:   # along the way they face, the forward foot landing a pixel nearer the viewer
            return reach * 2.2, (1 if reach > 0 and self.turn == "downside" else 0), 2 if swinging else 0
        if swinging:
            return (0.5 if i == 0 else -0.5), 0, 2                              # in under the body
        toward_viewer = reach if self.view == "down" else -reach
        return 0, max(0, toward_viewer), (1 if toward_viewer < 0 else 0)

    def posed_legs(self):
        """(i, (hip x, ankle x), dy, lift) per leg with this frame's step applied, in drawing order:
        turned toward the viewer, the near (screen-left) leg is drawn last, in front."""
        out = []
        for i, (top, bottom) in enumerate(self.leg_x()):
            dx, dy, lift = self.leg_pose(i)
            out.append((i, (top, bottom + dx), dy, lift))
        return out[::-1] if self.turn == "downside" else out

    @staticmethod
    def at(leg, y):
        """A leg's left edge at row y (between hip and ankle)."""
        top, bottom = leg
        return top + (bottom - top) * max(0.0, min(1.0, (y - 34) / (FEET - 2 - 34)))

    def legs_front_back(self):
        """Bare legs and feet (chunky, a little apart); men's linen braies to the knee (women's
        shift covers theirs)."""
        for i, leg, dy, lift in self.posed_legs():
            foot = FEET - lift + dy
            xt, xb = leg
            self.poly([(xt + 0.2, 34), (xt + 4, 34), (xb + 3.3, foot - 2), (xb + 0.8, foot - 2)], SKIN[:4], radius=1.0)
            left = i == 0
            fx0, fx1 = (xb - 0.8, xb + 3.4) if left else (xb + 0.6, xb + 4.8)
            self.foot(fx0, fx1, foot - 2, foot, SKIN[1:], left, radius=0.8)
            self.px([(fx0 + (1 if left else 2), foot)], SKIN[0])            # a toe line
            if not self.female:
                k = self.at(leg, 44)
                knee = 44 - (lift - dy) / 2
                self.poly([(xt - 0.5, 34), (xt + 4.8, 34), (k + 4.6, knee), (k, knee)], BRAIES, radius=1.2, tex=cloth_tex)
                self.hline(k + 0.5, k + 3.8, knee, BRAIES[0])

    def leg_side(self, stride, far):
        dx = stride * 2
        hip = 15.5
        fx = hip + dx
        dark = 0.72 if far else 1.0
        skin = darker(SKIN) if far else SKIN
        # thigh, shin, foot
        self.rib([(hip, 34), (hip + dx * 0.45, 43), (fx, FEET - 2)], [4.4, 3.8, 3.0], skin[:4], radius=1.0)
        self.poly([(fx - 1.5, FEET - 2), (fx + 2, FEET - 2), (fx + 3.5, FEET), (fx - 1.5, FEET)], skin[1:], radius=0.8)
        if not self.female:
            braies = darker(BRAIES) if far else BRAIES
            self.rib([(hip, 34), (hip + dx * 0.35, 40), (hip + dx * 0.5, 44)], [5.0, 4.6, 4.2], braies, radius=1.1, tex=cloth_tex)

    def torso(self):
        """The linen shirt (men, to the hip) or shift (women, to mid-calf)."""
        v, by = self.view, self.by
        side = v == "side"
        if self.female:
            if side:
                pts = [(12, 21 + by), (14, 20 + by), (18, 20 + by), (20, 23 + by), (19, 30), (21, 47), (10, 47), (11, 30), (11, 25 + by)]
            else:
                pts = [(11, 21 + by), (13, 20 + by), (18, 20 + by), (20, 21 + by), (20, 26 + by), (19, 30), (22, 47), (9, 47), (12, 30), (11, 26 + by)]
            self.poly(pts, LINEN, radius=2.0, tex=lambda x, y: cloth_tex(x, y) + (-0.1 if y > 32 and (x + y // 5) % 4 == 0 else 0))
            self.hline(9 if not side else 10, 22 if not side else 21, 47, LINEN[1])
        else:
            if side:
                pts = [(12, 21 + by), (14, 20 + by), (18, 20 + by), (20, 23 + by), (20, 28), (19, 31), (20, 36), (11, 36), (12, 31), (11, 26 + by)]
            else:
                pts = [(10, 21 + by), (13, 20 + by), (18, 20 + by), (21, 21 + by), (21, 27 + by), (20, 31), (21, 36), (10, 36), (11, 31), (10, 27 + by)]
            self.poly(pts, LINEN, radius=2.0, tex=cloth_tex)
        if v == "down":                                              # the open neck
            self.px([(14, 20 + by), (15, 20 + by), (16, 20 + by), (17, 20 + by), (15, 21 + by), (16, 21 + by), (15, 22 + by)], SKIN[1])
            self.px([(16, 22 + by)], SKIN[0])
            self.px([(14, 21 + by), (17, 21 + by)], LINEN[1])
        if not self.female and not side:
            self.hline(11, 20, 31, LINEN[1])                          # a cord belt
        elif not self.female:
            self.hline(12, 19, 31, LINEN[1])

    # ================================================================ arms

    def sleeve(self):
        """What the arm wears to the wrist: a sleeved garment's dyed sleeve, else the bare arm (the
        shirt's sleeves are short caps, ``cap_sleeve``)."""
        if self.has("tunic", "kaftan"):
            return list(self.dye), "cuff"
        return list(SKIN), None

    def cap_sleeve(self):
        """The linen shirt's short sleeve over the top of a bare arm; none under a vest or jerkin,
        whose shoulder covers it."""
        if self.has("tunic", "kaftan", "vest", "leather_jerkin"):
            return None
        return LINEN

    def upper_sleeve(self):
        """Armour over the upper arm: mail's short sleeves."""
        if self.has("chain_shirt"):
            return MAIL
        return None

    def shoulder_x(self, left):
        if self.female:
            return 9.5 if left else 21.5
        return 9 if left else 22

    def arms(self, only=None):
        """Both arms, or ``only`` one (True: the screen-left arm). Turned, the far arm is darker.
        Standing still (no swing, no work), the ``stance`` sets them: arms crossed, a hand on the
        hip, or flipping a coin (``beat`` is the frame of the toss)."""
        v, by = self.view, self.by
        if v == "side":
            self.arm_side(far=False)
            return
        sleeve, cuff = self.sleeve()
        arm = self.arm
        still = arm is None and self.legs == 0
        stance = self.stance if still else None
        far_side = self.far_arm()
        sides = (True, False) if only is None else (only,)
        if stance == "crossed" and only is None:
            sides = (True, False)
        for left in sides:
            far = far_side is not None and left == far_side
            cols = darker(sleeve) if far else sleeve
            # a seam, so the arm reads apart from the body (skin's own darkest step: it's tinted)
            line = DYE[0] if cols[0] in DYE else SKIN[0] if cols[0] in SKIN else scaled(cols[0], 0.7)
            sx = self.shoulder_x(left)
            out = -0.6 if left else 0.6
            raised = arm == "up" and not left
            struck = arm == "down" and not left
            swing = (self.legs / 2) * (1 if left else -1)
            coin_hand = stance == "coin" and left == (self.turn == "upside")   # the open, far side
            if raised:
                spine = [(sx, 22 + by), (sx + 1.5, 17 + by), (sx + 1.5, 12 + by)]
                hand = (sx + 1, 10 + by)
            elif struck:
                spine = [(sx, 22 + by), (sx + 0.8, 28 + by), (sx + 2, 33 + by)]
                hand = (sx + 2, 35 + by)
            elif stance == "crossed":
                elbow = (sx + out * 0.8, 28.5 + by)
                spine = [(sx, 22 + by), elbow, (15.5 + (4 if left else -4), 28 + by + (0 if left else 1))]
                hand = None if v == "up" else (15.5 + (5 if left else -5), 27.5 + by + (0 if left else 1))
            elif (stance == "hip" and not left) or (stance == "coin" and not coin_hand):
                o = 1 if not left else -1                               # elbow out, knuckles on the hip
                spine = [(sx, 22 + by), (sx + 3 * o, 27 + by), (sx - 0.5 * o, 31 + by)]
                hand = (sx - 1 * o, 31.5 + by)
            elif coin_hand:                                             # hand out beside the body, palm up
                o = -1 if left else 1
                spine = [(sx, 22 + by), (sx + 1.5 * o, 28.5 + by), (sx + 4 * o, 27 + by)]
                hand = (sx + 4.5 * o, 26.5 + by)
            else:
                spine = [(sx, 22 + by), (sx + out, 27 + by + swing * 0.5), (sx + out, 32 + by + swing)]
                hand = (sx + out, 34 + by + swing)
            if stance == "crossed" and v == "up":
                spine = spine[:2] + [((spine[1][0] + spine[2][0]) / 2, spine[1][1])]
            self.shape(lambda d: d.polygon(ribbon(spine, [3.9, 3.4, 3.0], 4), fill=255), cols, over=line, radius=1.1, tex=cloth_tex)
            if (up := self.upper_sleeve()) is not None:
                top = [spine[0], ((spine[0][0] + spine[1][0]) / 2, (spine[0][1] + spine[1][1]) / 2)]
                self.shape(lambda d: d.polygon(ribbon(top, [3.9, 3.4], 3), fill=255), darker(up) if far else up, radius=1.0,
                           tex=lambda x, y: 0.1 if (x + y) % 2 == 0 else -0.1)
            elif (cap := self.cap_sleeve()) is not None:
                top = [spine[0], (spine[0][0] + (spine[1][0] - spine[0][0]) * 0.45, spine[0][1] + (spine[1][1] - spine[0][1]) * 0.45)]
                self.shape(lambda d: d.polygon(ribbon(top, [4.2, 3.9], 3), fill=255), darker(cap) if far else cap, radius=1.0, tex=cloth_tex)
            if self.has("vest", "leather_jerkin") and not raised:       # leather bracers on the forearm
                a, b = spine[1], spine[2]
                brace = [(a[0] + (b[0] - a[0]) * 0.35, a[1] + (b[1] - a[1]) * 0.35), (a[0] + (b[0] - a[0]) * 0.95, a[1] + (b[1] - a[1]) * 0.95)]
                self.shape(lambda d: d.polygon(ribbon(brace, [3.6, 3.4], 3), fill=255), darker(GLOVE) if far else GLOVE, radius=0.8)
                mid = ((brace[0][0] + brace[1][0]) / 2, (brace[0][1] + brace[1][1]) / 2)
                self.px([mid], BRONZE[2])
            elif cuff and not raised and hand is not None and stance != "crossed":
                self.trim(round(hand[0]) - 1, round(hand[0]) + 1, round(hand[1]) - 2, rows=1)
            if hand is not None:
                self.hand(*hand, glove=self.has("vest", "leather_jerkin"))
            if raised:
                self.hammer_front(round(sx + 2), round(10 + by), up=True)
            elif struck:
                self.hammer_front(round(sx + 3), round(36 + by), up=False)
            if coin_hand and hand is not None and v != "up":
                # Out to the side and below the hair: the base sheet is under every other layer.
                lift = (0, 4, 7, 4)[self.beat % 4]
                cx, cy = round(self.T(hand[0], 0)[0]) - (1 if left else 0), round(hand[1] - 3 - lift)
                if self.beat % 2 == 1:                                  # spinning: edge-on on the way
                    for yy in (cy, cy + 1):
                        self.raw(cx, yy, GOLD[2])
                else:
                    for dx, dy, c in ((0, 0, GOLD[3]), (1, 0, GOLD[2]), (0, 1, GOLD[2]), (1, 1, GOLD[0])):
                        self.raw(cx + dx, cy + dy, c)
                    if self.beat == 2:
                        self.raw(cx - 1, cy - 1, WHITE)                 # a glint at the top of the toss

    def hand(self, x, y, glove=False):
        x, y = round(x), round(y)
        self.poly([(x - 1.2, y - 1), (x + 1.2, y - 1), (x + 1.2, y + 1.2), (x, y + 1.8), (x - 1.2, y + 1.2)], GLOVE[1:] if glove else SKIN[1:], radius=0.8)

    def hammer_front(self, x, y, up):
        if up:
            self.px([(x, yy) for yy in range(y - 7, y)], WOODC[1])
            self.px([(x + 1, yy) for yy in range(y - 7, y)], WOODC[2])
            self.shape(lambda d: d.rectangle([x - 3, y - 11, x + 4, y - 7], fill=255), IRON, radius=1.0)
        else:
            self.px([(x + k, y + k) for k in range(0, 6)], WOODC[1])
            self.px([(x + k + 1, y + k) for k in range(0, 6)], WOODC[2])
            self.shape(lambda d: d.rectangle([x + 3, y + 6, x + 9, y + 10], fill=255), IRON, radius=1.0)

    def arm_side(self, far=False):
        by = self.by
        sleeve, cuff = self.sleeve()
        up = self.upper_sleeve()
        if far:
            sleeve = darker(sleeve)
            up = darker(up) if up else None
        line = sleeve[1] if sleeve[0] == DYE[0] else sleeve[0]
        sx, sy = (14.5 if far else 15.5), 22 + by
        arm = None if far else self.arm
        if arm is None or isinstance(arm, str):
            swing = (self.legs if far else -self.legs) * 1.6
            spine = [(sx, sy), (sx + swing * 0.4, 27 + by), (sx + swing, 32 + by)]
            hand = (sx + swing, 34 + by)
            self.shape(lambda d: d.polygon(ribbon(spine, [3.8, 3.3, 2.8], 4), fill=255), sleeve, over=None if far else line, radius=1.1, tex=cloth_tex)
            if up:
                top = [spine[0], ((spine[0][0] + spine[1][0]) / 2, (spine[0][1] + spine[1][1]) / 2)]
                self.shape(lambda d: d.polygon(ribbon(top, [4.1, 3.6], 3), fill=255), up, radius=1.0,
                           tex=lambda x, y: 0.1 if (x + y) % 2 == 0 else -0.1)
            if cuff and not far:
                self.trim(round(hand[0]) - 1, round(hand[0]) + 1, round(hand[1]) - 2, rows=1)
            if far:
                x, y = round(hand[0]), round(hand[1])
                self.poly([(x - 1, y - 1), (x + 1, y - 1), (x + 1, y + 1), (x - 1, y + 1)], darker(SKIN), radius=0.8)
            else:
                self.hand(*hand)
            return
        a = math.radians(arm)
        hx, hy = sx + 8 * math.cos(a), sy + 8 * math.sin(a)
        self.rib([(sx, sy), ((sx + hx) / 2 + 1, (sy + hy) / 2), (hx, hy)], [4.0, 3.5, 3.0], sleeve, radius=1.2)
        self.hand(hx, hy)
        ta = a - 0.4
        tx, ty = hx + 9 * math.cos(ta), hy + 9 * math.sin(ta)
        self.rib([(hx, hy), (tx, ty)], [1.8, 1.8], WOODC, radius=0.6)
        px_, py_ = -math.sin(ta) * 3.0, math.cos(ta) * 3.0
        ex, ey = math.cos(ta) * 2.0, math.sin(ta) * 2.0
        self.shape(lambda d: d.polygon([(tx - px_ - ex, ty - py_ - ey), (tx + px_ - ex, ty + py_ - ey),
                                        (tx + px_ + ex, ty + py_ + ey), (tx - px_ + ex, ty - py_ + ey)], fill=255), IRON, radius=0.9)

    # ================================================================ head and face

    def face_mask(self, by):
        if self.view == "side":
            return union(self.mask(lambda d: d.ellipse([9, 3 + by, 21, 17 + by], fill=255)),
                         self.mask(lambda d: d.polygon([(10, 10 + by), (21, 9 + by), (22, 13 + by), (21.5, 16 + by),
                                                            (19, 19 + by), (15, 19 + by), (11, 16 + by)], fill=255)))
        return union(self.mask(lambda d: d.ellipse([9, 3 + by, 22, 17 + by], fill=255)),
                     self.mask(lambda d: d.polygon([(9, 10 + by), (22, 10 + by), (21.5, 15 + by), (18.5, 18.6 + by),
                                                        (12.5, 18.6 + by), (9.5, 15 + by)], fill=255)))

    def head(self):
        v, by = self.view, self.by
        # neck, in the chin's shadow
        x0 = 13.5
        self.poly([(x0, 17 + by), (x0 + 4, 17 + by), (x0 + 4, 21 + by), (x0, 21 + by)], SKIN[:3], radius=0.8, bias=-0.15)
        if self.turn == "downside":
            # the side and back of the head, behind the turned face: hair (skin if shaved or bald)
            if self.hair in ("none", "topknot"):
                shade_shape(self.img, mask_of(SIZE, lambda d: d.ellipse([8.5 + OX, Y(3 + by) + OY, 21.5 + OX, Y(18 + by) + OY], fill=255)), SKIN[:4], radius=2.0)
            else:
                self.head_back_hair(by)
        shade_shape(self.img, self.face_mask(by), SKIN, radius=2.2, depth=0.3)
        if v == "side":
            self.shape(lambda d: d.polygon([(21, 11 + by), (23, 13 + by), (23, 14 + by), (21, 14 + by)], fill=255), SKIN[1:], radius=0.7)
            self.px([(13, 11 + by), (13, 12 + by), (14, 13 + by)], SKIN[1])                 # the ear
            self.px([(14, 11 + by), (14, 12 + by)], SKIN[3])
            self.px([(19, 15 + by)], BLUSH)
            self.px([(20, 18 + by), (19, 19 + by), (18, 19 + by)], SKIN[1])                # under the jaw
        elif v == "down":
            # No mouth: the eyes carry the face (the Octopath way). Ears, blush, the nose's shadow.
            ears = [(8, 11 + by), (8, 12 + by)] + ([] if self.turn else [(23, 11 + by), (23, 12 + by)])
            self.px(ears, SKIN[2])
            self.px([(8, 13 + by)] + ([] if self.turn else [(23, 13 + by)]), SKIN[1])
            fx = self.face_x()
            for x in fx["blush"]:
                self.raw(x, 15 + by, BLUSH)
            self.raw(fx["nose"], 14 + by, SKIN[2])
            self.px([(14, 18 + by), (15, 18 + by), (16, 18 + by), (17, 18 + by)], SKIN[2])  # the chin's underside
        else:
            ears = [(8, 11 + by), (8, 12 + by), (23, 11 + by), (23, 12 + by)]
            if self.turn:
                ears = ears[2:]                                    # turned away right: their right ear shows
            self.px(ears, SKIN[1])
        self.brows(by)
        if self.eyes:
            self.draw_eyes(by)

    def face_x(self):
        """Image columns of the face's features: each eye's first column and width, the brows,
        the blush and the nose. Turned toward screen right, the near eye stays full and the far
        one narrows by the cheek."""
        far = self.TURNS[self.TURN]["far"] if self.turn == "downside" else 1.0
        near_x = round(self.T(11, 12)[0])
        far_x = round(self.T(18 if self.turn else 18, 12)[0])
        return {"eyes": ((near_x, 3), (far_x, max(1, round(3 * far + 0.2)))), "blush": (near_x - 1, far_x + 3 * far + 0.5),
                "nose": round(self.T(16.5 if self.turn else 16, 14)[0])}

    def brows(self, by):
        """Short strokes in the hair colour (the hair layer): bald heads keep them."""
        v = self.view
        if v == "down":
            for x0, w in self.face_x()["eyes"]:
                for x in range(x0, x0 + w):
                    self.raw(x, 8 + by, HAIR[1])
        elif v == "side":
            self.px([(18, 9 + by), (19, 9 + by), (20, 9 + by)], HAIR[1])

    def draw_eyes(self, by):
        """Big JRPG eyes, 3 px wide (2 for a turned face's far eye): a lash line, a two-tone iris
        darker at the top, and a catch-light toward the light. No eyes from behind."""
        style = self.eyes
        if self.view != "down":
            return
        for i, (x0, w) in enumerate(self.face_x()["eyes"]):
            outer = x0 - 1 if i == 0 else x0 + w                    # lashes flick outward
            cols = range(x0, x0 + w)
            if self.blink:
                for x in cols:
                    self.raw(x, 12 + by, LASH)
                self.raw(outer, 11 + by, LASH)
                continue
            top, rows = {"round": (10, 3), "narrow": (11, 2), "sleepy": (11, 2), "bright": (9, 4)}[style]
            if style == "sleepy":
                for x in cols:
                    self.raw(x, 10 + by, LID)
            for x in cols:
                self.raw(x, top + by, LASH)
            self.raw(outer, top + by + (1 if style == "narrow" else 0), LASH)
            iris = [EYE_DARK, EYE_MID, EYE] if rows >= 3 else [EYE_DARK, EYE]
            if rows == 4:
                iris = [EYE_DARK, EYE_DARK, EYE_MID, EYE]
            for r in range(rows):
                for x in cols:
                    self.raw(x, top + 1 + r + by, iris[r])
            self.raw(x0, top + 1 + by, WHITE)                       # catch-light, toward the light
            if style == "bright" and w > 2:
                self.raw(x0 + w - 1, top + rows + by, WHITE)

    def beard_front(self):
        style = self.beard
        if not style or self.view == "up":
            if style and self.view == "up" and style in ("full", "braided"):
                by = self.by
                self.px([(9, 16 + by), (22, 16 + by)], BEARD[1])
            return
        by = self.by
        side = self.view == "side"
        if style == "stubble":
            pts = ([(x, y + by) for y in range(14, 19) for x in range(10, 22) if (x + y) % 2 == 0 and not (13 <= x <= 18 and y <= 16)]
                   if not side else [(x, y + by) for y in range(14, 19) for x in range(15, 22) if (x + y) % 2 == 0 and not (x >= 19 and y <= 16)])
            ok = [p for p in pts if self.opaque(*p)]
            self.px(ok, BEARD[1])
            return
        if side:
            if style == "short":
                pts = [(14, 14 + by), (16, 15.5 + by), (19, 16 + by), (22, 16 + by), (22, 18 + by), (20, 20 + by), (16, 20 + by), (14, 17 + by)]
            else:
                pts = [(14, 14 + by), (16, 15.5 + by), (19, 16 + by), (22, 16 + by), (23, 19 + by), (22, 23 + by), (18, 25 + by), (15, 21 + by), (14, 17 + by)]
            self.poly(pts, BEARD, radius=1.2, tex=lock_tex)
            self.hline(20, 22, 16 + by, BEARD[2])                                         # moustache
            if style == "braided":
                self.rib([(20, 23 + by), (20.5, 27 + by), (20, 30 + by)], [2.6, 2.4, 2.0], BEARD, radius=0.8, tex=braid_tex)
                self.px([(19, 30 + by), (20, 30 + by), (21, 30 + by)], BRONZE[2])
            return
        if style == "short":
            pts = [(9.5, 14 + by), (11.5, 16 + by), (14, 16.5 + by), (17, 16.5 + by), (19.5, 16 + by), (21.5, 14 + by), (21.5, 16.5 + by),
                   (19, 20 + by), (12, 20 + by), (9.5, 16.5 + by)]
        else:
            pts = [(9.5, 13.5 + by), (11.5, 16 + by), (14, 16.5 + by), (17, 16.5 + by), (19.5, 16 + by), (21.5, 13.5 + by), (22.5, 18 + by),
                   (20, 23 + by), (17, 25 + by), (14, 25 + by), (11, 23 + by), (8.5, 18 + by)]
        self.poly(pts, BEARD, radius=1.4, tex=lock_tex)
        self.hline(13, 18, 15 + by, BEARD[2])                                             # moustache
        self.px([(12, 16 + by), (19, 16 + by)], BEARD[1])
        if style == "braided":
            self.rib([(15.5, 23 + by), (15.5, 27 + by), (15.5, 30 + by)], [3.0, 2.8, 2.2], BEARD, radius=0.8, tex=braid_tex)
            self.px([(14, 30 + by), (15, 30 + by), (16, 30 + by), (17, 30 + by)], BRONZE[2])
            self.px([(15, 30 + by)], BRONZE[3])

    # ================================================================ hair

    def hair_shape(self, mask, tex=None, radius=1.5, halo=None):
        """Shade a hair mask, then lay the Octopath sheen: a broken band of the lightest step across
        the crown, on the lit (left) side."""
        painted = shade_shape(self.img, mask, HAIR, radius=radius, tex=tex or (curl_tex if self.curly else lock_tex))
        if halo is not None:
            y0, x0, x1 = halo
            mk = mask.load()
            x0, x1 = round(self.T(x0, y0)[0]), round(self.T(x1, y0)[0])
            for x in range(x0, x1 + 1):
                y = y0 + (abs(x - (x0 + x1) / 2) > (x1 - x0) * 0.35)
                yy = round(Y(y) + OY)
                if 0 <= x < W and 0 <= yy < H and mk[x, yy] > 127 and x % 4 != 3:
                    self.raw(x, y, HAIR[4] if x % 4 != 2 else HAIR[3])
        return painted

    def tail(self, spine, widths, tie_y, braided=False):
        """A hanging tail (pigtail, ponytail or braid) with a bronze tie near its root."""
        self.rib(spine, widths, HAIR, radius=1.0, tex=braid_tex if braided else lock_tex)
        x = round(spine[0][0] + (spine[1][0] - spine[0][0]) * 0.18)
        self.px([(x - 1, tie_y), (x, tie_y), (x + 1, tie_y), (x - 1, tie_y + 1), (x, tie_y + 1), (x + 1, tie_y + 1)], BRONZE[2])
        self.px([(x - 1, tie_y), (x + 1, tie_y + 1)], BRONZE[3])

    def hair_back(self):
        """Hair behind the head and body (drawn before the torso): long hair, tails, a low bun."""
        by = self.by
        v, style = self.view, self.hair
        side = v == "side"
        if style == "ponytail":
            if side:
                self.rib([(9, 7 + by), (4.5, 12 + by), (5.5, 26 + by)], [3.6, 4.0, 1.8], HAIR, radius=1.0, tex=lock_tex)
            elif v == "down":
                self.rib([(21, 6 + by), (25.5, 13 + by), (25, 25 + by)], [3.2, 3.8, 1.8], HAIR, radius=1.0, tex=lock_tex)
            return
        if style == "swept" and v == "down":
            return
            return
        if style == "bun" and side:
            self.shape(lambda d: d.ellipse([4, 6 + by, 10, 12 + by], fill=255), HAIR, radius=1.0, tex=braid_tex)
            return
        if not self.long:
            return
        if side:
            pts = [(7, 5 + by), (14, 3 + by), (14, 14 + by), (13, 22 + by), (14, 31), (12, 32), (10, 30), (8, 32), (6, 30), (6, 20)]
            bumps = [(7, y, 2.3) for y in range(10 + by, 31, 3)] + [(x, 31, 2.3) for x in range(8, 14, 3)]
        else:
            pts = [(8, 6 + by), (23, 6 + by), (24.5, 20 + by), (24, 31), (22, 32), (20.5, 30), (18.5, 32), (16, 30), (13.5, 32),
                   (11, 30), (9, 32), (7, 31), (6.5, 20 + by)]
            bumps = ([(7.5, y, 2.3) for y in range(10 + by, 31, 3)] + [(24, y, 2.3) for y in range(10 + by, 31, 3)]
                     + [(x, 31, 2.3) for x in range(9, 24, 3)])
        if self.curly:
            m = union(self.mask(lambda d: d.polygon(pts, fill=255)), self.circles(bumps))
        else:
            m = self.mask(lambda d: d.polygon(pts, fill=255))
        shade_shape(self.img, m, HAIR, radius=1.8, tex=curl_tex if self.curly else lock_tex, bias=-0.12)

    def hair_front(self):
        style = self.hair
        if style == "none":
            return
        v, by = self.view, self.by
        if self.curly:
            return self.curly_front(v, by)
        if style == "swept":
            return self.hair_swept(v, by)
        getattr(self, f"hair_{v}")(style, by)

    def head_back_hair(self, by):
        """Turned toward the viewer, the near side of the head is its side and back: a full-width
        mass of hair (in image space, not through the turn) for the turned face and fringe to sit in."""
        bottom = 26 if self.long else 16 if self.hair == "swept" else 17.5
        m = mask_of(SIZE, lambda d: (d.ellipse([5.5 + OX, Y((-2.5 if self.hair == "swept" else -0.5) + by) + OY, 22 + OX, Y(17.5 + by) + OY], fill=255),
                                     d.polygon([(x + OX, Y(y) + OY) for x, y in [(7.5, 8 + by), (12.5, 8 + by), (12.5, 13 + by), (11, bottom + by), (8, bottom - 1 + by), (7, 13 + by)]], fill=255)))
        shade_shape(self.img, m, HAIR, radius=1.8, tex=curl_tex if self.curly else lock_tex, bias=-0.1)

    def locks(self, specs, by, shift=None, seam=True):
        """Hair as separate tapered locks, back to front, each inked where it overlaps the last, with
        a strand of sheen down its lit side toward the root. ``shift`` places them straight in the
        frame (design x + OX + shift), not through the turn: a turned head's own design."""
        if shift is None:
            mask = lambda fn: self.mask(fn)
            to = self.T
        else:
            to = lambda x, y: (x + OX + shift, Y(y) + OY)
            mask = lambda fn: mask_of(SIZE, lambda d: fn(TDraw(d, to)))
        for spine, widths in specs:
            pts = [(x, y + by) for x, y in spine]
            tip = (pts[-1][0], pts[-1][1], widths[-1] / 2)
            m = union(mask(lambda d: d.polygon(ribbon(pts, widths, 5), fill=255)),
                      mask(lambda d: d.ellipse([tip[0] - tip[2], tip[1] - tip[2], tip[0] + tip[2], tip[1] + tip[2]], fill=255)))   # a soft tip
            # Mostly mid and light tones (thin locks would otherwise be all dark edge), a soft
            # mid-tone where one lies over the last, so they blend into one head of hair.
            if seam:
                shade_over(self.img, m, HAIR, HAIR[2], radius=1.6, bias=0.16, tex=lambda x, y: bayer(x, y) * 0.05)
            else:
                shade_shape(self.img, m, HAIR, radius=1.6, bias=0.16, tex=lambda x, y: bayer(x, y) * 0.05)
            line = spine_points(pts, 4)
            for x, y in line[1:max(2, len(line) * 3 // 5)]:
                self.raw(to(x - 0.7, y)[0], y, HAIR[4])

    def hair_swept(self, v, by):
        """Messy, feathered hair: a full crown, many thin locks of uneven length with fine tips
        falling round the face to the jaw, and one long lock across the right eye (the far one when
        turned). Short and feathered at the nape from behind. Each view is placed by hand; the
        turned one isn't the front squeezed through the turn."""
        if v == "up":
            mass = self.mask(lambda d: (d.ellipse([7, -2 + by, 24, 15 + by], fill=255), d.rectangle([8, 6 + by, 23, 13 + by], fill=255)))
            self.hair_shape(mass, halo=(0 + by, 10, 20), radius=1.8, tex=lambda x, y: bayer(x, y) * 0.05)
            self.locks([
                ([(11, 1), (8.5, -0.5), (6.5, 0.5)], [3.6, 2.8, 1.2]), ([(20, 1), (22.5, -0.5), (24.5, 0.5)], [3.6, 2.8, 1.2]),
                ([(9, 4), (6.5, 7), (5, 10)], [3.4, 2.8, 1.0]), ([(22, 4), (24.5, 7), (26, 9.5)], [3.4, 2.8, 1.0]),
            ] + [([(x, 4), (x + d * 0.4, 9), (x + d, 13), (x + d * 1.5, 15.5 + (k % 2))], [4.6, 4.2, 3.0, 1.0])
                 for k, (x, d) in enumerate(((9.5, -1.2), (13.5, -0.4), (17.5, 0.4), (21.5, 1.2)))], by)
            return
        if self.turn == "downside":
            # Placed in the head's own frame: its middle where the turn puts it, the back of the head
            # on the near (left) side, the face on the right.
            shift = self.T(15.5, 10 + by)[0] - (CX + OX)
            self.locks([
                ([(8, 1), (5.5, -0.5), (3.5, 0.5)], [3.6, 2.8, 1.2]),                         # soft tufts off the crown
                ([(15, -0.5), (17.5, -1.5), (19.5, -0.5)], [3.2, 2.6, 1.2]),
                ([(8, 3), (5.5, 8), (5, 13), (3.5, 16.5)], [3.4, 3.0, 2.0, 0.6]),             # down the back of the head, feathered
                ([(10, 4), (8.5, 9), (8, 14), (7, 18)], [3.0, 2.8, 1.8, 0.6]),
                ([(6.5, 6), (4, 10), (3, 13.5)], [2.8, 2.2, 0.6]),
                ([(9.5, 8), (9, 12.5), (9.5, 16)], [2.4, 2.0, 0.6]),                          # before the ear
                ([(6, 2), (10, -1.5), (15.5, -1), (20.5, 2.5)], [3.4, 3.8, 3.2, 1.2]),        # over the crown, forward
                ([(8, 4), (12, 1), (17, 2), (20.5, 5.5)], [3.0, 3.4, 2.8, 1.0]),
                ([(11, 3), (11.5, 6), (10.5, 8.5)], [2.4, 2.0, 0.6]),                         # a wisp above the near eye
                ([(15, 3), (18, 6), (20.5, 9), (21, 12.5)], [2.6, 2.4, 1.8, 0.6]),
                ([(13, 2), (16, 4.5), (18.5, 8.5), (19, 12), (18, 15)], [3.4, 3.8, 3.4, 2.2, 0.8]),   # the long lock, over the far eye
            ], by, shift=shift)
            return
        crown = self.mask(lambda d: (d.pieslice([7, -2 + by, 24, 16 + by], 180, 360, fill=255),
                                     d.rectangle([7.5, 5 + by, 9.5, 11 + by], fill=255), d.rectangle([21.5, 5 + by, 23.5, 11 + by], fill=255)))
        self.hair_shape(crown, halo=(0 + by, 10, 19), radius=1.6, tex=lambda x, y: bayer(x, y) * 0.05)
        self.locks([
            ([(8.5, 5), (7, 10), (6.5, 15), (5, 18.5)], [3.6, 3.2, 2.2, 0.8]),               # framing the face to the jaw
            ([(22.5, 5), (24, 10), (24.5, 15), (26, 18)], [3.4, 3.0, 2.0, 0.6]),
            ([(10, 7), (8.5, 12), (8.5, 16.5)], [2.6, 2.2, 0.6]),
            ([(21, 7), (22.5, 12), (22.5, 16)], [2.6, 2.2, 0.6]),
            ([(11, 1), (8.5, -0.5), (6.5, 0.5)], [3.6, 2.8, 1.2]),                           # soft tufts off the crown, leaning out
            ([(19.5, 0.5), (22, -0.5), (24, 0.5)], [3.6, 2.8, 1.2]),
            ([(22, 3), (24.5, 3), (26.5, 5)], [3.0, 2.4, 1.0]),
            ([(9, 3), (6.5, 3), (4.5, 5)], [3.0, 2.4, 1.0]),
            ([(11, 2), (11.5, 5.5), (10.5, 9)], [2.8, 2.2, 0.6]),                             # the fringe: thin, uneven
            ([(13, 2), (13.5, 6), (13, 9.5)], [2.6, 2.0, 0.6]),
            ([(15, 2), (15.5, 6), (16, 10.5)], [2.6, 2.0, 0.6]),
            ([(17, 2), (20, 4.5), (22.5, 8.5), (23, 12.5)], [2.8, 2.8, 2.0, 0.6]),
            ([(19, 3), (21.5, 7), (21.5, 10)], [2.0, 1.8, 0.6]),
            ([(14, 1), (17, 3.5), (19.5, 7.5), (20.5, 11.5), (20, 15)], [3.6, 4.0, 3.6, 2.4, 0.8]),   # the long lock, over the right eye
        ], by)

    # -- front view
    def hair_down(self, style, by):
        cap = self.mask(lambda d: d.pieslice([8, 1 + by, 23, 18 + by], 180, 360, fill=255))
        temples = self.mask(lambda d: (d.polygon([(8, 8 + by), (10.5, 8 + by), (10.5, 11 + by), (9, 13.5 + by), (8, 12 + by)], fill=255),
                                           d.polygon([(20.5, 8 + by), (23, 8 + by), (23, 12 + by), (22, 13.5 + by), (20.5, 11 + by)], fill=255)))
        halo = (4 + by, 10, 19)
        if style == "short":
            fringe = self.mask(lambda d: d.polygon([(9, 5 + by), (22, 5 + by), (22.5, 8 + by), (21, 7.5 + by), (19.5, 9.5 + by), (18, 7.5 + by),
                                                        (16, 9 + by), (14.5, 7 + by), (12.5, 9.5 + by), (11, 7.5 + by), (9, 9.5 + by), (8.5, 8 + by)], fill=255))
            self.hair_shape(union(cap, fringe, temples), halo=halo)
        elif style == "shaggy":
            mass = self.mask(lambda d: (d.polygon([(7, 7 + by), (11, 6 + by), (10.5, 12 + by), (10, 17 + by), (9, 19 + by), (8, 16.5 + by),
                                                       (7, 18 + by), (6.5, 12 + by)], fill=255),
                                            d.polygon([(20, 6 + by), (24.5, 7 + by), (25, 12 + by), (24, 18 + by), (23, 16.5 + by), (22, 19 + by),
                                                       (21, 17 + by), (20.5, 12 + by)], fill=255)))
            fringe = self.mask(lambda d: d.polygon([(8, 5 + by), (23, 5 + by), (23, 8 + by), (21.5, 9.5 + by), (20, 8 + by), (18.5, 10 + by),
                                                        (17, 7.5 + by), (15.5, 9.5 + by), (14, 7.5 + by), (12.5, 10 + by), (11, 8 + by), (9.5, 9.5 + by),
                                                        (8, 8.5 + by)], fill=255))
            cap = self.mask(lambda d: d.pieslice([7, 0 + by, 24, 18 + by], 180, 360, fill=255))
            self.hair_shape(union(cap, fringe, mass), halo=halo)
            self.px([(12, 0 + by), (13, 0 + by), (20, 1 + by)], HAIR[3])
        elif style == "long":
            fringe = self.mask(lambda d: (d.polygon([(8.5, 5 + by), (15.5, 3 + by), (15, 6 + by), (13, 8.5 + by), (11, 10.5 + by), (9.5, 13 + by), (8.5, 12 + by)], fill=255),
                                              d.polygon([(16, 3 + by), (22.5, 5 + by), (22.5, 12 + by), (21.5, 13 + by), (20, 10.5 + by), (18, 8.5 + by), (16.5, 6 + by)], fill=255)))
            self.hair_shape(union(cap, fringe), halo=halo)
            self.px([(15, y + by) for y in range(2, 6)], HAIR[1])                             # the parting
            for left in (True, False):
                x0 = 8.5 if left else 22.5
                o = -0.6 if left else 0.6
                self.rib([(x0, 8 + by), (x0 + o, 18 + by), (x0 + o * 0.5, 29 + by)], [3.0, 3.2, 1.6], HAIR, radius=1.0, tex=lock_tex)
        elif style in ("ponytail", "bun", "crownbraid", "sidebraid", "pigtails"):
            # pulled back: a smooth cap and a light fringe, the forehead shows
            if style == "sidebraid":
                fringe = self.mask(lambda d: d.polygon([(8.5, 5 + by), (19, 3 + by), (22.5, 6 + by), (22.5, 10 + by), (20.5, 8 + by), (17, 6.5 + by),
                                                            (13, 7 + by), (10, 9 + by), (8.5, 11 + by)], fill=255))
            else:
                fringe = self.mask(lambda d: d.polygon([(9, 5 + by), (22, 5 + by), (22.5, 9 + by), (20.5, 7 + by), (17.5, 6.5 + by), (16, 7.5 + by),
                                                            (14.5, 6.5 + by), (11, 7 + by), (8.5, 9 + by)], fill=255))
            self.hair_shape(union(cap, fringe, temples), halo=halo, radius=1.6)
            if style == "bun":
                self.shape(lambda d: d.ellipse([12.5, -1 + by, 18.5, 4 + by], fill=255), HAIR, radius=1.0, tex=braid_tex)
                self.px([(14, 3 + by), (15, 3 + by), (16, 3 + by), (17, 3 + by)], BRONZE[2])
            elif style == "crownbraid":
                self.rib([(8.5, 9 + by), (11, 4.5 + by), (15.5, 3 + by), (20, 4.5 + by), (22.5, 9 + by)], [2.6, 3.0, 3.0, 3.0, 2.6], HAIR, radius=0.8, tex=braid_tex)
            elif style == "sidebraid":
                self.tail([(21.5, 10 + by), (23, 18 + by), (21, 27 + by), (20, 33 + by)], [3.6, 3.6, 3.0, 2.2], 30 + by, braided=True)
            elif style == "pigtails":
                self.tail([(8, 9 + by), (5, 16 + by), (6, 26 + by)], [3.4, 3.8, 2.2], 10 + by, braided=True)
                self.tail([(23, 9 + by), (26, 16 + by), (25, 26 + by)], [3.4, 3.8, 2.2], 10 + by, braided=True)
        elif style == "topknot":
            top = self.mask(lambda d: (d.pieslice([10.5, 1 + by, 20.5, 14 + by], 180, 360, fill=255),
                                           d.polygon([(11, 6 + by), (20, 6 + by), (19, 8 + by), (16.5, 7 + by), (14, 8 + by), (11.5, 7.5 + by)], fill=255)))
            self.hair_shape(top, halo=(3 + by, 12, 18), radius=1.2)
            for y in range(3, 11):                                                           # shaved sides: stubble
                for x in list(range(8, 11)) + list(range(21, 24)):
                    if (x * 2 + y * 3) % 5 == 0 and self.opaque(x, y + by):
                        self.px([(x, y + by)], HAIR[1])
            self.shape(lambda d: d.ellipse([13, -1 + by, 18, 3 + by], fill=255), HAIR, radius=0.9, tex=braid_tex)
            self.px([(14, 2 + by), (15, 2 + by), (16, 2 + by), (17, 2 + by)], BRONZE[2])

    # -- back view
    def hair_up(self, style, by):
        head = self.mask(lambda d: (d.ellipse([8, 1 + by, 23, 18 + by], fill=255), d.rectangle([9, 9 + by, 22, 15 + by], fill=255)))
        nape = self.mask(lambda d: d.polygon([(9, 14 + by), (22, 14 + by), (22, 17 + by), (20.5, 19 + by), (19, 17 + by), (17, 19.5 + by),
                                                  (15, 17.5 + by), (13, 19.5 + by), (11, 17.5 + by), (9.5, 18.5 + by)], fill=255))
        halo = (4 + by, 10, 20)
        if style in ("short", "shaggy"):
            self.hair_shape(union(head, nape), halo=halo)
            if style == "shaggy":
                self.shape(lambda d: d.polygon([(8, 12 + by), (23, 12 + by), (24, 18 + by), (22.5, 21 + by), (20.5, 19 + by), (18.5, 21.5 + by),
                                                (16, 19.5 + by), (13.5, 21.5 + by), (11, 19.5 + by), (9, 21 + by), (7, 18 + by)], fill=255), HAIR, radius=1.4, tex=lock_tex)
            self.px([(15, 5 + by), (16, 4 + by), (16, 6 + by)], HAIR[1])                      # the crown's whorl
        elif style == "long":
            sheet = self.mask(lambda d: d.polygon([(8.5, 10 + by), (22.5, 10 + by), (23.5, 22 + by), (23, 31 + by), (21, 32.5 + by), (19.5, 30.5 + by),
                                                       (17.5, 32.5 + by), (15.5, 30.5 + by), (13.5, 32.5 + by), (11.5, 30.5 + by), (9.5, 32.5 + by),
                                                       (8, 31 + by), (7.5, 22 + by)], fill=255))
            self.hair_shape(union(head, sheet), halo=halo, radius=1.8)
            self.px([(15, y) for y in range(2 + by, 30 + by, 1)], HAIR[1])
        else:
            smooth = union(head, self.mask(lambda d: d.polygon([(9, 14 + by), (22, 14 + by), (20, 18 + by), (11, 18 + by)], fill=255)))
            if style == "topknot":
                self.hair_shape(self.mask(lambda d: d.ellipse([11, 1 + by, 20, 16 + by], fill=255)), halo=(3 + by, 12, 19), radius=1.2)
                for y in range(5, 17):
                    for x in range(8, 24):
                        if (x * 2 + y * 3) % 5 == 0 and self.opaque(x, y + by) and not (11 <= x <= 20 and y <= 15):
                            self.px([(x, y + by)], HAIR[1])
                self.shape(lambda d: d.ellipse([13, 0 + by, 18, 5 + by], fill=255), HAIR, radius=0.9, tex=braid_tex)
                self.px([(14, 5 + by), (15, 5 + by), (16, 5 + by), (17, 5 + by)], BRONZE[2])
                return
            self.hair_shape(smooth, halo=halo, radius=1.8)
            for x in (12, 15, 18):                                                            # combed lines toward the tie
                self.px([(x + (1 if x < 15 else -1 if x > 15 else 0) * (k // 3), 4 + by + k) for k in range(0, 9, 2)], HAIR[1])
            if style == "ponytail":
                self.tail([(15.5, 8 + by), (15.5, 18 + by), (16.5, 31 + by)], [3.6, 4.4, 2.0], 9 + by)
            elif style == "bun":
                self.shape(lambda d: d.ellipse([12, 9 + by, 19, 16 + by], fill=255), HAIR, radius=1.0, tex=braid_tex)
                self.hline(13, 18, 9 + by, BRONZE[2])
            elif style == "crownbraid":
                self.rib([(8.5, 9 + by), (11, 12.5 + by), (15.5, 13.5 + by), (20, 12.5 + by), (22.5, 9 + by)], [2.6, 3.0, 3.0, 3.0, 2.6], HAIR, radius=0.8, tex=braid_tex)
            elif style == "sidebraid":
                self.tail([(21, 12 + by), (23.5, 18 + by), (23.5, 24 + by)], [3.4, 3.2, 2.6], 13 + by, braided=True)
            elif style == "pigtails":
                self.tail([(10.5, 9 + by), (8.5, 18 + by), (9.5, 28 + by)], [3.4, 4.0, 2.2], 10 + by, braided=True)
                self.tail([(21.5, 9 + by), (23.5, 18 + by), (22.5, 28 + by)], [3.4, 4.0, 2.2], 10 + by, braided=True)

    # -- side view (facing right)
    def hair_side(self, style, by):
        cap = self.mask(lambda d: d.pieslice([7, 1 + by, 22, 18 + by], 180, 360, fill=255))
        back = self.mask(lambda d: d.polygon([(7, 8 + by), (13, 8 + by), (12.5, 11 + by), (12, 15 + by), (10, 17.5 + by), (8, 17 + by), (7, 14 + by)], fill=255))
        halo = (4 + by, 9, 17)
        if style in ("short", "shaggy"):
            fringe = self.mask(lambda d: d.polygon([(15, 2 + by), (21.5, 5 + by), (22, 9 + by), (20.5, 8 + by), (19.5, 10 + by), (18, 7.5 + by),
                                                        (16, 9 + by), (14, 7.5 + by)], fill=255))
            parts = [cap, back, fringe]
            if style == "shaggy":
                parts.append(self.mask(lambda d: d.polygon([(6.5, 7 + by), (13.5, 8 + by), (14, 13 + by), (13, 19 + by), (11.5, 17 + by), (10, 20 + by),
                                                                (8.5, 18 + by), (6.5, 19 + by), (6, 13 + by)], fill=255)))
            self.hair_shape(union(*parts), halo=halo)
        elif style == "long":
            fringe = self.mask(lambda d: d.polygon([(15, 2 + by), (21.5, 5 + by), (21.5, 10 + by), (19.5, 7.5 + by), (17, 7 + by), (14, 8 + by)], fill=255))
            self.hair_shape(union(cap, back, fringe), halo=halo)
            self.rib([(12, 9 + by), (11, 18 + by), (12, 27 + by)], [3.0, 3.4, 1.8], HAIR, radius=1.0, tex=lock_tex)
        elif style == "topknot":
            top = self.mask(lambda d: (d.pieslice([9, 1 + by, 22, 14 + by], 180, 360, fill=255),
                                           d.polygon([(18, 4 + by), (21.5, 6 + by), (21, 8 + by), (19, 7 + by)], fill=255)))
            self.hair_shape(top, halo=(3 + by, 11, 17), radius=1.2)
            for y in range(6, 17):
                for x in range(7, 14):
                    if (x * 2 + y * 3) % 5 == 0 and self.opaque(x, y + by) and y > 7:
                        self.px([(x, y + by)], HAIR[1])
            self.shape(lambda d: d.ellipse([10, -1 + by, 15, 3 + by], fill=255), HAIR, radius=0.9, tex=braid_tex)
            self.px([(11, 2 + by), (12, 2 + by), (13, 2 + by)], BRONZE[2])
        else:
            fringe = self.mask(lambda d: d.polygon([(15, 2 + by), (21.5, 5 + by), (22, 8 + by), (19.5, 6.5 + by), (16, 6.5 + by), (14, 7 + by)], fill=255))
            self.hair_shape(union(cap, back, fringe), halo=halo, radius=1.6)
            self.px([(x, 6 + by + (x - 9) // 3) for x in range(9, 15)], HAIR[1])            # combed back
            if style == "ponytail":
                self.px([(8, 8 + by), (8, 9 + by), (9, 8 + by), (9, 9 + by)], BRONZE[2])
                self.px([(8, 8 + by)], BRONZE[3])
            elif style == "crownbraid":
                self.rib([(10, 12 + by), (11, 6 + by), (15, 2.5 + by), (19, 3.5 + by)], [2.6, 3.0, 3.0, 2.6], HAIR, radius=0.8, tex=braid_tex)
            elif style == "sidebraid":
                self.tail([(12.5, 13 + by), (14, 20 + by), (16, 27 + by), (16.5, 33 + by)], [3.6, 3.4, 3.0, 2.2], 30 + by, braided=True)
            elif style == "pigtails":
                self.tail([(11, 10 + by), (9, 17 + by), (10, 27 + by)], [3.4, 3.8, 2.2], 11 + by, braided=True)
            elif style == "bun":
                self.hline(9, 10, 8 + by, BRONZE[2])

    def curly_front(self, v, by):
        long_ = self.long
        if v == "down":
            m = self.mask(lambda d: d.pieslice([8, 0 + by, 23, 15 + by], 180, 360, fill=255))
            fringe = self.circles([(x, 7 + by + (1 if k % 2 else 0), 2.0) for k, x in enumerate(range(10, 22, 3))])
            m = union(m, fringe, self.circles(arc_discs(15.5, 7.5 + by, 7.4, 7.4, 180, 360, 11, 2.3)),
                      self.circles([(8.5, 9 + by, 2.0), (22.5, 9 + by, 2.0), (8.5, 11.5 + by, 1.7), (22.5, 11.5 + by, 1.7)]))
            self.hair_shape(m, tex=curl_tex, halo=(3 + by, 10, 19))
            if long_:
                for left in (True, False):
                    x0 = 7 if left else 23
                    discs = [(x0 + 1 + (0.8 if (k % 2 == 0) == left else -0.4), y, 2.3) for k, y in enumerate(range(10 + by, 28 + by, 3))]
                    shade_shape(self.img, self.circles(discs), HAIR, radius=1.2, tex=curl_tex)
        elif v == "up":
            m = union(self.mask(lambda d: (d.ellipse([8, 1 + by, 23, 18 + by], fill=255), d.rectangle([9, 9 + by, 22, 15 + by], fill=255))),
                      self.circles(arc_discs(15.5, 10 + by, 7.8, 8.0, 0, 360, 14, 2.2)))
            self.hair_shape(m, tex=curl_tex, halo=(3 + by, 10, 20), radius=2.0)
            if long_:
                discs = ([(9.5 + k * 2.4, 30.5 + by, 2.4) for k in range(6)] + [(9.5, y, 2.3) for y in range(16 + by, 30 + by, 3)]
                         + [(22, y, 2.3) for y in range(16 + by, 30 + by, 3)])
                shade_shape(self.img, union(self.mask(lambda d: d.rectangle([9, 15 + by, 22, 31 + by], fill=255)), self.circles(discs)),
                            HAIR, radius=1.8, tex=curl_tex)
        else:
            m = self.mask(lambda d: (d.pieslice([7, 0 + by, 22, 16 + by], 180, 360, fill=255), d.rectangle([7, 8 + by, 12, 12 + by], fill=255)))
            fringe = self.circles([(x, 6.5 + by + (1 if k % 2 else 0), 2.0) for k, x in enumerate(range(16, 22, 2))])
            m = union(m, fringe, self.circles(arc_discs(14.5, 8 + by, 7.7, 7.7, 180, 360, 10, 2.2) + [(7.5, 9.5 + by, 2.2), (7.5, 12 + by, 2.0), (9, 14 + by, 2.0)]))
            self.hair_shape(m, tex=curl_tex, halo=(3 + by, 9, 17))
            if long_:
                discs = [(10.5 + (0.8 if k % 2 else -0.4), y, 2.4) for k, y in enumerate(range(12 + by, 29 + by, 3))]
                shade_shape(self.img, self.circles(discs), HAIR, radius=1.2, tex=curl_tex)

    # ================================================================ garments

    def legwear(self):
        v = self.view
        if self.has("trousers"):
            dye = list(self.dye)
            if v == "side":
                for stride, far in ((-self.legs, True), (self.legs, False)):
                    dx = stride * 2
                    k = 0.72 if far else 1.0
                    cols = darker(dye) if far else dye
                    fx = 15.5 + dx
                    self.rib([(15.5, 33), (15.5 + dx * 0.45, 42), (fx + dx * 0.05, 47)], [5.4, 4.8, 4.2], cols, radius=1.1, tex=wool_tex)
                    self.rib([(fx + dx * 0.05, 46), (fx, 51)], [4.0, 3.6], [scaled(c, k) for c in WRAP], radius=0.9)
                    for yy in range(47, 51):
                        self.px([(round(fx) - 1 + (yy - 47) % 3, yy)], scaled(WRAP[0], k))
                return
            for _, leg, dy, foot_lift in self.posed_legs():
                lift = (foot_lift - dy) / 2      # the knee rises half as far as the foot
                xt = leg[0]
                knee = self.at(leg, 43)
                ankle = self.at(leg, 51)
                self.poly([(xt - 0.4, 33), (xt + 4.8, 33), (knee + 5, 41.5 - lift), (knee + 4.4, 43.5 - lift), (knee, 43.5 - lift), (knee - 0.6, 41.5 - lift)],
                          dye, radius=1.2, tex=wool_tex)                                   # bagged a little over the knee
                # Leg wraps (winingas) from the knee down: wound bands, each turn lit at its top.
                self.poly([(knee + 0.4, 43.5 - lift), (knee + 4, 43.5 - lift), (ankle + 3.6, 51.5 - foot_lift + dy), (ankle + 0.6, 51.5 - foot_lift + dy)], WRAP,
                          radius=0.9, tex=lambda x, y: 0.28 if (y + x // 3) % 3 == 0 else -0.22 if (y + x // 3) % 3 == 2 else 0)
            self.hline(10, 21, 33, dye[1])
        elif self.has("skirt"):
            dye = list(self.dye)
            if v == "side":
                pts = [(11, 30), (19, 30), (20, 40), (22, 51), (9, 51), (10, 40)]
            else:
                pts = [(10, 30), (21, 30), (22, 40), (24, 51), (7, 51), (9, 40)]
            self.poly(pts, dye, radius=2.0, tex=lambda x, y: wool_tex(x, y) + (-0.12 if (x + y // 6) % 3 == 0 and y > 34 else 0))
            self.trim(7 if v != "side" else 9, 24 if v != "side" else 22, 49, rows=2)

    def footwear(self):
        v = self.view
        boots = self.has("boots")
        if not boots and not self.has("turnshoes"):
            return
        top = 45 if boots else 50
        if v == "side":
            for stride, far in ((-self.legs, True), (self.legs, False)):
                fx = 15.5 + stride * 2
                k = 0.72 if far else 1.0
                shoe = [scaled(c, k) for c in SHOE]
                self.poly([(fx - 2, top), (fx + 1.5, top), (fx + 1.5, FEET - 2.5), (fx + 3.5, FEET - 1), (fx + 3.5, FEET), (fx - 2, FEET)], shoe, radius=1.0)
                if boots:
                    self.hline(fx - 2, fx + 1, top, scaled(FUR[3], k))
                    self.hline(fx - 2, fx + 1, top + 1, scaled(FUR[1], k))
            return
        for i, leg, dy, lift in self.posed_legs():
            left = i == 0
            lift -= dy                                                   # a foot nearer the viewer sits lower
            x0 = self.at(leg, top)
            sx0, sx1 = (x0 - 1.6, x0 + 4) if left else (x0 + 0.4, x0 + 6)     # big feet
            self.foot(sx0, sx1, top - lift, FEET - lift, SHOE, left, radius=1.0)
            if boots:                                                   # a strap round the shaft
                self.hline(sx0 + 0.5, sx1 - 0.5, top - lift + 4, LEATHER[1])
            self.hline(sx0 + 1, sx1 - 1, FEET - lift - 2, SHOE[3]) if not boots else None
            if boots:
                for x in range(int(sx0), int(sx1) + 1):
                    self.px([(x, top - lift)], FUR[3] if x % 2 else FUR[2])
                    self.px([(x, top + 1 - lift)], FUR[1] if x % 2 else FUR[2])

    def tunic(self):
        v, by = self.view, self.by
        side = v == "side"
        dye = list(self.dye)
        if self.has("tunic", "kaftan"):
            long_ = self.has("kaftan")
            hem = 44 if long_ else 39
            if side:
                pts = [(12, 21 + by), (14, 20 + by), (18, 20 + by), (20, 23 + by), (20.5, 28), (19.5, 31), (21, hem), (10, hem), (11.5, 31), (11, 26 + by)]
                x0, x1 = 10, 21
            else:
                w = 0 if not self.female else 0.5
                pts = [(10 + w, 21 + by), (13, 20 + by), (18, 20 + by), (21 - w, 21 + by), (21.5 - w, 27 + by), (20.5, 31), (22.5, hem), (8.5, hem),
                       (10.5, 31), (9.5 + w, 27 + by)]
                x0, x1 = 8, 23
            self.poly(pts, dye, radius=2.0, tex=lambda x, y: wool_tex(x, y) + (-0.1 if y > 32 and (x + y // 4) % 5 == 0 else 0))
            self.trim(x0, x1, hem - 1)
            # belt
            bx0, bx1 = (11, 20) if side else (10, 21)
            self.hline(bx0, bx1, 30, LEATHER[1])
            self.hline(bx0, bx1, 31, LEATHER[2])
            if v == "down":
                self.px([(15, 30), (16, 30), (15, 31), (16, 31)], BRONZE[2])
                self.px([(15, 30)], BRONZE[3])
                self.px([(16, 32), (16, 33), (16, 34)], LEATHER[1])                          # the strap end
                if long_:   # wrapped front, its edge trimmed
                    for y in range(21 + by, hem):
                        x = 13 + min(4, (y - 21 - by) // 2) if y < 30 else 17
                        self.px([(x, y)], GOLD[1] if y % 2 else TRIM_DARK[1])
                    self.trim(13, 18, 20 + by, rows=1)
                else:
                    self.px([(14, 20 + by), (15, 20 + by), (16, 20 + by), (17, 20 + by), (15, 21 + by), (16, 21 + by)], SKIN[1])
                    self.px([(13, 20 + by), (18, 20 + by), (14, 21 + by), (17, 21 + by)], GOLD[2])
            elif side:
                self.px([(20, 30), (20, 31)], BRONZE[2])
                if long_:
                    self.px([(19, y) for y in range(22 + by, hem, 2)], GOLD[1])
        elif self.has("apron_dress"):
            if side:
                pts = [(12, 23 + by), (19.5, 23 + by), (19.5, 30), (21.5, 48), (9.5, 48), (11, 30)]
            else:
                pts = [(10.5, 23 + by), (20.5, 23 + by), (20.5, 30), (23, 48), (8, 48), (10.5, 30)]
            self.poly(pts, dye, radius=2.2, tex=lambda x, y: wool_tex(x, y) + (-0.12 if (x + y // 6) % 3 == 0 and y > 31 else 0))
            self.trim(8 if not side else 9, 23 if not side else 22, 46, rows=2)
            if not side:
                self.px([(11, 20 + by), (12, 20 + by), (11, 21 + by), (12, 21 + by), (11, 22 + by), (12, 22 + by),
                         (19, 20 + by), (20, 20 + by), (19, 21 + by), (20, 21 + by), (19, 22 + by), (20, 22 + by)], dye[2])   # straps
            if v == "down":
                self.px([(11, 23 + by), (12, 23 + by), (19, 23 + by), (20, 23 + by)], BRONZE[3])   # tortoise brooches
                self.px([(11, 24 + by), (12, 24 + by), (19, 24 + by), (20, 24 + by)], BRONZE[1])
                for x, c in ((13, BEAD_AMBER), (14, BEAD_BLUE), (15, BEAD_AMBER), (16, BEAD_BLUE), (17, BEAD_AMBER), (18, BEAD_BLUE)):
                    self.px([(x, 24 + by + (0 if x in (13, 18) else 1))], c)
            elif side:
                self.px([(18, 24 + by), (18, 25 + by)], BRONZE[2])
                self.px([(x, 20 + by + (x - 13)) for x in range(13, 16)], dye[2])
        if self.has("vest"):                                            # worn over a tunic (the Body slot)
            if side:
                pts = [(12, 21 + by), (17, 21 + by), (20, 23 + by), (20.5, 28), (19.5, 31), (20.5, 37), (11, 37), (11.5, 31), (11, 26 + by)]
            else:
                pts = [(10, 21 + by), (13, 20.5 + by), (14.5, 25 + by), (17, 25 + by), (18, 20.5 + by), (21, 21 + by), (21.5, 27 + by), (20.5, 31),
                       (21.5, 37), (9.5, 37), (10.5, 31), (9.5, 27 + by)]
            self.poly(pts, DARK_LEATHER, radius=1.8, tex=lambda x, y: bayer(x, y) * 0.09)
            if v == "down":
                self.px([(15, y) for y in range(25 + by, 37)], DARK_LEATHER[0])
                for y in range(26 + by, 34, 2):
                    self.px([(14, y), (16, y)], WRAP[1])
                self.px([(11, 21 + by), (12, 21 + by), (19, 21 + by), (20, 21 + by), (12, 22 + by), (19, 22 + by)], FUR[2])
            self.hline(10 if not side else 11, 21 if not side else 20, 36, DARK_LEATHER[0])
            self.px([(x, 37) for x in range(10 if not side else 11, 22 if not side else 21, 3)], DARK_LEATHER[1])

    def cloak_back(self):
        """The panel hanging behind the person (front and side views; the back view draws it in front)."""
        if not self.has(*BACK_PANELS) or self.view == "up":
            return
        dye = list(self.dye[:4]) if self.dye[0] == DYE[0] else [scaled(c, 0.85) for c in self.dye]
        by = self.by
        if self.view == "side":
            pts = [(9, 21 + by), (13, 20 + by), (13, 30), (11, 47), (6, 46), (6.5, 32)]
        else:
            pts = [(8, 21 + by), (23, 21 + by), (24.5, 34), (25, 46), (6, 46), (6.5, 34)]
        self.poly(pts, dye, radius=2.0, tex=lambda x, y: wool_tex(x, y) + (-0.14 if (x + y // 5) % 4 == 0 and y > 28 else 0))

    def scarf_front(self):
        """A big wool scarf wound thick round the neck and up over the chin, one long fringed tail
        hanging down the front on the near side; from behind, the other down the middle of the back."""
        if not self.has("scarf"):
            return
        v, by = self.view, self.by
        dye = list(self.dye)
        folds = lambda x, y: wool_tex(x, y) + (-0.16 if (y + (x // 4)) % 3 == 0 else 0)
        if v == "up":
            self.rib([(15.5, 22 + by), (15, 30), (15.8, 38), (15, 42)], [5.2, 4.8, 4.4, 4.0], dye, radius=1.2, tex=folds)
            self.px([(13.5 + k, 42.5 + k % 2) for k in range(5)], dye[0])                # the fringe
        self.poly([(8.5, 18 + by), (11, 15 + by), (20, 15 + by), (23, 18 + by), (25, 22 + by), (23.5, 26 + by), (19, 27.5 + by), (12, 27.5 + by),
                   (7.5, 26 + by), (6, 22 + by)], dye, radius=1.8, tex=folds)            # wound up over the chin and out onto the shoulders
        for y in (17.5, 21, 24.5):                                                       # the turns of the wrap
            self.hline(9, 22.5, y + by, dye[0])
            self.hline(9.5, 22, y + 1 + by, dye[1])

    def mantle_front(self, v, by, dye):
        """A short, ragged wool poncho thrown over one side: over that shoulder and out past the arm
        down to the hip, its hem cut in uneven tatters with a loose fringe, rising across the front
        to cap the other shoulder, so that arm and the dark clothes under it show. The covered side
        is screen left from the front (the near side when turned), screen right from behind."""
        folds = lambda x, y: (-0.2 if (x * 2 + y // 6) % 7 == 0 and y > 22 else 0.06 if (x + y // 5) % 5 == 2 else 0) + bayer(x, y) * 0.06
        pts = [(15.5, 18.5 + by), (10.5, 19.2 + by), (7, 20.8 + by), (4.8, 23.5 + by), (4, 27), (4.4, 30.5),
               (5.6, 29.6), (6.6, 31.6), (8, 30.2), (9, 33), (10.5, 31.6), (11.6, 34.8), (13, 33.2), (14.2, 36), (15.5, 34.4),
               (16.8, 36.4), (18, 34), (19, 30), (20.5, 26.5), (22, 25), (23.5, 26), (24.5, 23.8), (23.5, 20.8 + by), (20.5, 19.2 + by)]
        if v == "up":   # from behind, the covered side is screen right
            pts = [(31 - x, y) for x, y in pts]
        self.poly(pts, dye, radius=1.4, slope=0.7, tex=folds)
        threads = [(6, 31.6), (9, 33.6), (11.6, 35.4), (14.2, 36.6), (16.8, 37), (22, 25.8)]
        self.px([(x if v != "up" else 31 - x, y) for x, y in threads] + [((x if v != "up" else 31 - x) + 0.6, y + 1) for x, y in threads], dye[0])

    def cloak_front(self):
        v, by = self.view, self.by
        dye = list(self.dye)
        if self.has("mantle"):
            return self.mantle_front(v, by, dye)
        if self.has("fur_mantle"):
            if v == "side":
                pts = [(10, 21 + by), (14, 19 + by), (19, 20 + by), (21, 23 + by), (20, 27 + by), (17, 26 + by), (13, 28 + by), (10, 27 + by)]
            elif v == "down":
                pts = [(8, 22 + by), (12, 19.5 + by), (19, 19.5 + by), (23, 22 + by), (23.5, 26 + by), (21, 27.5 + by), (18, 25 + by),
                       (15.5, 27 + by), (13, 25 + by), (10, 27.5 + by), (7.5, 26 + by)]
            else:
                pts = [(8, 22 + by), (12, 19.5 + by), (19, 19.5 + by), (23, 22 + by), (23.5, 28 + by), (20, 29.5 + by), (15.5, 28.5 + by),
                       (11, 29.5 + by), (7.5, 28 + by)]
            self.poly(pts, FUR, radius=1.4, tex=lambda x, y: (0.12 if (x * 3 + y * 2) % 5 == 0 else -0.04) + bayer(x, y) * 0.05)
            if v == "down":
                self.px([(15, 22 + by), (16, 22 + by)], BRONZE[3])
                self.px([(15, 23 + by), (16, 23 + by)], BRONZE[1])
            return
        if not self.has("cloak", "hooded_cloak"):
            return
        hooded = self.has("hooded_cloak")
        if v == "up":
            if self.part == "back":
                return
            pts = [(8, 21 + by), (11, 19.5 + by), (20, 19.5 + by), (23, 21 + by), (24.5, 34), (25, 46), (6, 46), (6.5, 34)]
            self.poly(pts, dye, radius=2.0, tex=lambda x, y: wool_tex(x, y) + (-0.14 if (x + y // 5) % 4 == 0 and y > 28 else 0))
            if hooded:
                self.poly([(10, 18 + by), (21, 18 + by), (20.5, 23 + by), (15.5, 27 + by), (10.5, 23 + by)], dye, radius=1.4, tex=wool_tex)
                self.px([(15, y) for y in range(19 + by, 26 + by)], dye[1])
            return
        if v == "down":
            # over both shoulders, open down the front, pinned at the right shoulder
            self.poly([(8.5, 21.5 + by), (11, 20 + by), (12.5, 21 + by), (11.5, 24 + by), (9, 25 + by)], dye, radius=1.2, tex=wool_tex)
            self.poly([(17, 20.5 + by), (20, 20 + by), (23.5, 22 + by), (25, 32), (24.5, 44), (21.5, 42), (19.5, 34), (18.5, 26 + by)], dye, radius=1.6,
                      tex=lambda x, y: wool_tex(x, y) + (-0.14 if (x + y // 5) % 4 == 0 and y > 28 else 0))
            self.hline(19, 21, 41, dye[1])
            if hooded:
                self.poly([(11, 19 + by), (20, 19 + by), (21, 22 + by), (15.5, 24.5 + by), (10, 22 + by)], dye, radius=1.2, tex=wool_tex)
                self.hline(12, 19, 22 + by, dye[1])
            self.px([(10, 22 + by), (11, 22 + by), (10, 23 + by), (11, 23 + by)], BRONZE[2])     # ring pin
            self.px([(10, 22 + by)], BRONZE[3])
            self.px([(9, 21 + by), (12, 24 + by)], BRONZE[1])
        else:
            self.poly([(10, 21 + by), (14, 19.5 + by), (17, 20 + by), (16, 25 + by), (13, 29 + by), (10, 28 + by)], dye, radius=1.4, tex=wool_tex)
            if hooded:
                self.poly([(9, 18 + by), (15, 18 + by), (15, 22 + by), (10, 24 + by), (7, 23 + by)], dye, radius=1.2, tex=wool_tex)
            self.px([(16, 21 + by), (16, 22 + by)], BRONZE[2])

    # ================================================================ armour and gear

    def armor(self):
        if "leather_jerkin" in self.wear:
            self.jerkin()
        if "chain_shirt" in self.wear:
            self.byrnie()

    def torso_shape(self, bottom):
        v, by = self.view, self.by
        if v == "side":
            return [(12, 21 + by), (14, 20 + by), (18, 20 + by), (20.5, 23 + by), (20.5, 28), (19.5, 31), (20.5, bottom), (21, bottom + 1),
                    (10.5, bottom + 1), (11, bottom), (11.5, 31), (11, 26 + by)]
        return [(9.5, 21 + by), (13, 20 + by), (18, 20 + by), (21.5, 21 + by), (21.5, 27 + by), (20.5, 31), (21.5, bottom), (22, bottom + 1),
                (9, bottom + 1), (9.5, bottom), (10.5, 31), (9.5, 27 + by)]

    def jerkin(self):
        v, by = self.view, self.by
        bottom = 35
        self.poly(self.torso_shape(bottom), LEATHER, radius=2.0, tex=lambda x, y: bayer(x, y) * 0.09)
        for x in range(9 if v != "side" else 11, (23 if v != "side" else 21), 3):              # tabbed hem
            self.px([(x, bottom + 2), (x + 1, bottom + 2)], LEATHER[1])
        self.hline(9 if v != "side" else 11, 22 if v != "side" else 21, bottom + 1, LEATHER[0])
        if v == "down":
            for y in range(22 + by, bottom):                                                  # laced front
                self.px([(15, y), (16, y)], LEATHER[0])
                if y % 2 == 0:
                    self.px([(14, y), (17, y)], WRAP[1])
            self.px([(13, 21 + by), (18, 21 + by), (12, 21 + by), (19, 21 + by)], FUR[2])
        elif v == "up":
            self.px([(15, y) for y in range(22 + by, bottom)], LEATHER[1])
        self.hline(10 if v != "side" else 12, 21 if v != "side" else 20, 30, LEATHER[3])

    def byrnie(self):
        v, by = self.view, self.by
        bottom = 38 + (4 if self.female else 0)

        def rings(x, y):
            return (0.10 if (x + y) % 2 == 0 else -0.10) + bayer(x, y) * 0.03
        self.poly(self.torso_shape(bottom), MAIL, radius=2.0, tex=rings)
        for x in range(9 if v != "side" else 11, (24 if v != "side" else 22), 2):             # dagged hem
            self.px([(x, bottom + 2)], MAIL[1])
        self.hline(9 if v != "side" else 11, 22 if v != "side" else 21, bottom + 1, MAIL[0])
        self.hline(10 if v != "side" else 12, 21 if v != "side" else 20, 30, LEATHER[1])       # belt over the mail
        if v == "down":
            self.px([(15, 30), (16, 30)], BRONZE[2])
            self.px([(14, 20 + by), (15, 20 + by), (16, 20 + by), (17, 20 + by)], MAIL[3])

    def back_gear(self):
        if "satchel" not in self.wear:
            return
        v, by = self.view, self.by
        if v == "down":
            self.px([(x, y) for x, y in zip(range(11, 22), range(21 + by, 32))], LEATHER[0])        # strap
            self.px([(x, y) for x, y in zip(range(12, 22), range(21 + by, 31))], LEATHER[2])
            self.bag(21, 32, 27, 41)
        elif v == "up":
            self.bag(11, 27, 21, 38)
            self.px([(x, 21 + by + (x - 11)) for x in range(11, 15)], LEATHER[1])
        else:
            self.bag(5, 29, 11, 39)
            self.px([(x, y) for x, y in zip(range(11, 18), range(21 + by, 29 + by))], LEATHER[0])

    def bag(self, x0, y0, x1, y1):
        self.poly([(x0, y0 + 2), (x1, y0 + 2), (x1, y1), (x0 + 1, y1), (x0, y1 - 1)], LEATHER, radius=1.2, tex=lambda x, y: bayer(x, y) * 0.07)
        self.poly([(x0, y0), (x1, y0), (x1, y0 + 4), ((x0 + x1) // 2, y0 + 6), (x0, y0 + 4)], LEATHER[1:] + [LEATHER[4]], radius=1.0)
        cx = (x0 + x1) // 2
        self.px([(cx, y0 + 5), (cx, y0 + 4)], BRONZE[3])                           # clasp
        self.px([(x0 + 1, y0 + 1), (x1 - 1, y0 + 1)], GOLD[3])                      # knotwork studs
        self.px([(x0 + 1, y1 - 1), (x1 - 1, y1 - 1)], LEATHER[0])

    def head_armor(self):
        v, by = self.view, self.by
        if "leather_cap" in self.wear:
            self.hood(v, by)
        if "iron_helm" in self.wear:
            self.helm(v, by)
        if "horned_helm" in self.wear:
            self.horns(v, by)
            self.helm(v, by, nasal=False)

    def hood(self, v, by):
        """A wool hood-cap with a fur-lined brim."""
        if v == "side":
            m = self.mask(lambda d: (d.pieslice([6, 0 + by, 23, 18 + by], 180, 360, fill=255), d.rectangle([6, 8 + by, 21, 10 + by], fill=255),
                                         d.polygon([(6, 8 + by), (12, 8 + by), (12, 17 + by), (8, 18 + by)], fill=255)))
        elif v == "up":
            m = self.mask(lambda d: (d.pieslice([7, 0 + by, 24, 18 + by], 180, 360, fill=255), d.polygon([(7.5, 8 + by), (23.5, 8 + by), (22.5, 18 + by), (8.5, 18 + by)], fill=255)))
        else:
            m = self.mask(lambda d: (d.pieslice([7, 0 + by, 24, 18 + by], 180, 360, fill=255), d.rectangle([7, 8 + by, 24, 10 + by], fill=255),
                                         d.rectangle([7, 8 + by, 9, 15 + by], fill=255), d.rectangle([22, 8 + by, 24, 15 + by], fill=255)))
        shade_shape(self.img, m, LEATHER, radius=2.0, tex=lambda x, y: bayer(x, y) * 0.08)
        y0, x0, x1 = 8 + by, (6 if v == "side" else 7), (21 if v == "side" else 24)
        for x in range(x0, x1 + 1):                                                    # fur brim
            self.px([(x, y0), (x, y0 + 1)], FUR[3] if (x + y0) % 2 == 0 else FUR[2])
            if (x * 7) % 3 == 0:
                self.px([(x, y0 + 2)], FUR[1])
        self.px([(x, 2 + by) for x in range(12, 18)], LEATHER[4])

    def horns(self, v, by):
        """A pair of ox horns out of the helm's sides, curving up (drawn first: the dome covers their roots)."""
        for s in (-1, 1):
            cx = 15.5
            spine = [(cx + s * 7.5, 6 + by), (cx + s * 11, 3.5 + by), (cx + s * 12.5, -0.5 + by), (cx + s * 11.5, -3.5 + by)]
            m = self.mask(lambda d: d.polygon(ribbon(spine, [3.6, 3.0, 2.0, 0.8], steps=5), fill=255))
            shade_shape(self.img, m, HORN, radius=1.0)

    def helm(self, v, by, nasal=True):
        """A spangenhelm: iron dome, bronze brow band and centre strip, nasal guard, mail at the nape."""
        if v == "side":
            m = self.mask(lambda d: (d.pieslice([7, -1 + by, 23, 17 + by], 180, 360, fill=255), d.rectangle([7, 7 + by, 22, 9 + by], fill=255)))
        else:
            m = self.mask(lambda d: (d.pieslice([7, -1 + by, 24, 17 + by], 180, 360, fill=255), d.rectangle([7, 7 + by, 24, 9 + by], fill=255)))
        shade_shape(self.img, m, IRON, radius=2.0, tex=lambda x, y: bayer(x, y) * 0.06)
        x0, x1 = (7, 22) if v == "side" else (7, 24)
        self.hline(x0, x1, 8 + by, BRONZE[2])
        self.hline(x0, x1, 9 + by, BRONZE[0])
        self.px([(x0 + 2, 8 + by), (x1 - 2, 8 + by)], BRONZE[3])
        if v == "down":
            self.px([(15, y) for y in range(0 + by, 8 + by)], BRONZE[1])
            self.px([(16, y) for y in range(0 + by, 8 + by)], BRONZE[2])
            if nasal:
                self.px([(15, y) for y in range(10 + by, 15 + by)], IRON[2])                  # nasal
                self.px([(16, y) for y in range(10 + by, 15 + by)], IRON[3])
        elif v == "up":
            self.px([(15, y) for y in range(0 + by, 9 + by)], BRONZE[1])
            self.px([(16, y) for y in range(0 + by, 9 + by)], BRONZE[2])
            if nasal:
                for y in range(10 + by, 17 + by):                                             # mail aventail
                    self.hline(8, 23, y, MAIL[2] if (y % 2) else MAIL[1])
        else:
            self.px([(x, 3 + by) for x in range(9, 19)], BRONZE[1])
            if nasal:
                self.px([(22, y) for y in range(10 + by, 15 + by)], IRON[3])                  # nasal
                for y in range(10 + by, 17 + by):
                    self.hline(7, 11, y, MAIL[2] if (y % 2) else MAIL[1])


# ---------------------------------------------------------------- lying (bed / downed): flat, top-down, head at the top

def lying(breath=0, **look):
    """The flat poses, drawn on the old 32-wide canvas and centred in the frame."""
    small = _lying(breath, **look)
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    img.alpha_composite(small, (OX, OY))
    return img


def _lying(breath=0, downed=False, body="male", hair="short", eyes=None, beard=None, wear=(), dye=None, part=None):
    SIZE = (32, FEET + 2)
    W, H = 32, SIZE[1]
    img = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    female = body == "female"
    dye = dye or DYE
    o = 12   # the figure spans rows 8..46, centred in the frame like the old one

    def shape(fn, cols, **kw):
        kw.setdefault("radius", 1.3)
        shade_shape(img, mask_of(SIZE, fn), cols, **kw)

    def px(pts, color):
        for x, y in pts:
            if 0 <= x < W and 0 <= y < H:
                img.putpixel((int(x), int(y)), color)

    def has(*items):
        return any(i in wear for i in items)

    if (part == "back" or part is None) and has("cloak", "hooded_cloak"):
        shape(lambda d: d.polygon([(7, o + 10), (24, o + 10), (26, o + 32), (5, o + 32)], fill=255), list(dye[:4]), radius=2.0, tex=wool_tex)
    if part == "back":
        return selective_outline(img)
    curly = hair.startswith("curly")
    htex = curl_tex if curly else lock_tex
    if hair in LONG_HAIR:
        spread = circles_mask(size=SIZE, items=[(7 + k * 2.4, o + 21.5, 2.2) for k in range(8)] + [(6.5, o + y, 2.2) for y in range(2, 22, 3)] + [(24.5, o + y, 2.2) for y in range(2, 22, 3)]) if curly else None
        base = mask_of(SIZE, lambda d: d.ellipse([6, o - 1, 25, o + 22], fill=255))
        shade_shape(img, union(base, spread) if spread is not None else base, HAIR, radius=1.8, tex=htex)
    elif hair in ("ponytail", "bun", "topknot"):
        shape(lambda d: d.ellipse([13, o - 8, 18, o + 1], fill=255), HAIR, radius=1.0, tex=braid_tex if hair != "ponytail" else htex)
    elif hair in ("pigtails",):
        shape(lambda d: (d.ellipse([4, o + 1, 9, o + 13], fill=255), d.ellipse([22, o + 1, 27, o + 13], fill=255)), HAIR, radius=1.0, tex=braid_tex)
    elif hair == "sidebraid":
        shape(lambda d: d.polygon(ribbon([(21, o + 6), (23, o + 14), (21, o + 22)], [3.4, 3.2, 2.4], 4), fill=255), HAIR, radius=1.0, tex=braid_tex)
    # legs, bare or dressed
    if female:
        shape(lambda d: d.polygon([(10, o + 18), (21, o + 18), (23, o + 33), (8, o + 33)], fill=255), LINEN, radius=2.0)
        shape(lambda d: (d.rectangle([12, o + 33, 14, o + 35], fill=255), d.rectangle([17, o + 33, 19, o + 35], fill=255)), SKIN[1:], radius=0.9)
    else:
        shape(lambda d: (d.rectangle([11, o + 20, 14, o + 31], fill=255), d.rectangle([17, o + 20, 20, o + 31], fill=255)), SKIN[:4], radius=1.0)
        shape(lambda d: (d.rectangle([11, o + 20, 14, o + 26], fill=255), d.rectangle([17, o + 20, 20, o + 26], fill=255)), BRAIES, radius=1.0)
        shape(lambda d: (d.rectangle([11, o + 31, 14, o + 33], fill=255), d.rectangle([17, o + 31, 20, o + 33], fill=255)), SKIN[1:], radius=0.9)
    if has("trousers"):
        shape(lambda d: (d.rectangle([10, o + 20, 14, o + 30], fill=255), d.rectangle([17, o + 20, 21, o + 30], fill=255)), list(dye), radius=1.0, tex=wool_tex)
    elif has("skirt"):
        shape(lambda d: d.polygon([(10, o + 18), (21, o + 18), (24, o + 33), (7, o + 33)], fill=255), list(dye), radius=2.0, tex=wool_tex)
    if has("turnshoes", "boots"):
        top = o + 27 if has("boots") else o + 31
        shape(lambda d: (d.rectangle([11, top, 14, o + 34], fill=255), d.rectangle([17, top, 20, o + 34], fill=255)), SHOE, radius=0.9)
    # torso and arms
    sleeve = list(dye) if has("tunic", "kaftan") else LINEN
    shape(lambda d: d.polygon([(10 - breath, o + 12), (21 + breath, o + 12), (22 + breath, o + 21), (9 - breath, o + 21)], fill=255), LINEN, radius=1.8)
    if has("tunic", "kaftan"):
        bottom = o + 27 if has("kaftan") else o + 23
        shape(lambda d: d.polygon([(10 - breath, o + 12), (21 + breath, o + 12), (22 + breath, bottom), (9 - breath, bottom)], fill=255), list(dye), radius=1.8, tex=wool_tex)
        px([(x, o + 19) for x in range(10, 22)], LEATHER[2])
        px([(15, o + 19), (16, o + 19)], BRONZE[3])
    elif has("apron_dress"):
        shape(lambda d: d.polygon([(11 - breath, o + 14), (20 + breath, o + 14), (23 + breath, o + 32), (8 - breath, o + 32)], fill=255), list(dye), radius=1.6, tex=wool_tex)
        px([(11, o + 14), (12, o + 14), (19, o + 14), (20, o + 14)], BRONZE[3])
    elif has("vest"):
        shape(lambda d: d.polygon([(10 - breath, o + 12), (21 + breath, o + 12), (22 + breath, o + 22), (9 - breath, o + 22)], fill=255), LEATHER, radius=1.6)
        px([(15, y) for y in range(o + 13, o + 22)], LEATHER[0])
    spread = 4 if downed else 0
    for left in (True, False):
        x0 = 6 - spread if left else 23 + spread
        shape(lambda d: d.rectangle([x0, o + 12, x0 + 3, o + 21], fill=255), sleeve, radius=1.0)
        shape(lambda d: d.rectangle([x0, o + 22, x0 + 3, o + 24], fill=255), SKIN[1:], radius=0.8)
    for item in wear:
        if item == "leather_jerkin":
            shape(lambda d: d.polygon([(10 - breath, o + 12), (21 + breath, o + 12), (21 + breath, o + 20), (10 - breath, o + 20)], fill=255), LEATHER, radius=1.6)
            px([(15, y) for y in range(o + 12, o + 20)], LEATHER[0])
        elif item == "chain_shirt":
            shape(lambda d: d.polygon([(10 - breath, o + 12), (21 + breath, o + 12), (22 + breath, o + 22), (9 - breath, o + 22)], fill=255),
                  MAIL, radius=1.6, tex=lambda x, y: (0.1 if (x + y) % 2 == 0 else -0.1))
    if has("scarf"):
        shape(lambda d: d.polygon([(9, o + 9), (22, o + 9), (22, o + 13), (9, o + 13)], fill=255), list(dye), radius=1.2, tex=wool_tex)
    if has("fur_mantle"):
        shape(lambda d: d.polygon([(8, o + 11), (23, o + 11), (24, o + 16), (7, o + 16)], fill=255), FUR, radius=1.2)
    elif has("cloak", "hooded_cloak"):
        shape(lambda d: (d.polygon([(8, o + 11), (12, o + 11), (11, o + 24), (8, o + 22)], fill=255),
                         d.polygon([(19, o + 11), (23, o + 11), (23, o + 22), (20, o + 24)], fill=255)), list(dye), radius=1.2, tex=wool_tex)
    # head
    shape(lambda d: d.ellipse([9, o - 3, 22, o + 12], fill=255), SKIN, radius=2.2, depth=0.25)
    if beard and beard != "stubble":
        shape(lambda d: d.polygon([(10, o + 6), (21, o + 6), (20, o + (14 if beard != "short" else 12)), (11, o + (14 if beard != "short" else 12))], fill=255), BEARD, radius=1.2, tex=lock_tex)
    elif beard == "stubble":
        px([(x, y) for y in range(o + 7, o + 11) for x in range(10, 22) if (x + y) % 2 == 0 and not 13 <= x <= 18], BEARD[1])
    if hair != "none":
        cap = mask_of(SIZE, lambda d: d.pieslice([8, o - 4, 23, o + 9], 180, 360, fill=255))
        if curly:
            cap = union(cap, circles_mask(arc_discs(15.5, o + 2, 7.2, 7.2, 180, 360, 11, 2.2), size=SIZE))
        shade_shape(img, cap, HAIR, radius=1.4, tex=htex)
    px([(11, o + 2), (12, o + 2), (19, o + 2), (20, o + 2)], HAIR[1])                       # brows
    if eyes:
        if downed:
            for ex in (11, 19):
                px([(ex, o + 4), (ex + 1, o + 5), (ex + 1, o + 4), (ex, o + 5)], LASH)
        else:
            for ex in (11, 19):
                px([(ex - 1 if ex == 11 else ex, o + 5), (ex, o + 5) if ex == 11 else (ex + 1, o + 5), (ex + 1, o + 5) if ex == 11 else (ex + 2, o + 5)], LASH)
    for item in wear:
        if item == "leather_cap":
            shape(lambda d: d.pieslice([8, o - 4, 23, o + 8], 180, 360, fill=255), LEATHER, radius=1.6)
            px([(x, o + 2) for x in range(8, 24)], FUR[3])
        elif item in ("iron_helm", "horned_helm"):
            if item == "horned_helm":
                for hx in (5, 23):
                    shape(lambda d, hx=hx: d.ellipse([hx, o - 2, hx + 3, o + 2], fill=255), HORN, radius=0.8)
            shape(lambda d: d.pieslice([8, o - 4, 23, o + 8], 180, 360, fill=255), IRON, radius=1.6)
            px([(x, o + 2) for x in range(8, 24)], BRONZE[2])
            px([(15, y) for y in range(o - 3, o + 2)], BRONZE[1])
    return selective_outline(img)


# ---------------------------------------------------------------- sheets

VIEWS = ("down", "downside", "upside", "up")


def human_sequence(**look):
    """Directional tags (idle_down, walk_downside, ...), an idle per stance (idle_down_crossed), and
    plain idle/walk/work aliasing the front view. Turned views are drawn facing screen right."""
    seq = Sequencer()
    for view in VIEWS:
        def f(**kw):
            return Fig(view, **kw, **look).render()
        blink = view in ("down", "downside")
        seq.add(f"idle_{view}", [f(), f(bob=1), f(bob=1, blink=blink), f()], 350)
        # contact, passing, contact, passing: legs (the arm swing) and the bob follow the step
        seq.add(f"walk_{view}", [f(walk=k, legs=(2, 0, -2, 0)[k], bob=(0, 1, 0, 1)[k]) for k in range(4)], 140)
        seq.add(f"work_{view}", [f(arm=a) for a in ("up", "up", "down", None)], 140, event=2)
        for stance in STANCES:
            if stance == "coin":   # hold it, flip it, catch it, hold it again: about two seconds a loop
                tag = f"idle_{view}_{stance}"
                start = len(seq.frames)
                for kw, ms in ((dict(beat=0), 420), (dict(beat=0, bob=1), 420), (dict(beat=1), 110), (dict(beat=2), 150),
                               (dict(beat=3), 110), (dict(beat=0), 300), (dict(beat=0, bob=1, blink=blink), 380), (dict(beat=0), 300)):
                    seq.frames.append((f(stance=stance, **kw), ms))
                seq.tags[tag] = (start, len(seq.frames) - 1, True, -1)
            else:
                seq.add(f"idle_{view}_{stance}", [f(stance=stance), f(stance=stance, bob=1), f(stance=stance, bob=1, blink=blink),
                                                   f(stance=stance)], 380)
    seq.add("sleep", [lying(0, **look), lying(1, **look)], 1100)
    seq.add("downed", [lying(downed=True, **look)], 1000, loop=False)
    for base in ("idle", "walk", "work"):
        seq.tags[base] = seq.tags[f"{base}_down"]
    return seq


def _write_split(name, seq, shades, keep_rest=True, trim=True):
    """Split every frame by marker colours; write the tint layers as <name>_<layer> and the rest as <name>."""
    split = [split_layers(img, shades) for img, _ in seq.frames]
    for layer in ["base", *shades]:
        if layer == "base" and not keep_rest:
            continue
        frames = [(parts[layer], ms) for parts, (_, ms) in zip(split, seq.frames)]
        if layer != "base" and not any(f.getbbox() for f, _ in frames):
            continue
        write_sheet(name if layer == "base" else f"{name}_{layer}", frames, seq.tags, PIVOT, trim=trim)


def _diff(dressed, bare):
    seq = Sequencer()
    seq.frames = [(diff_layer(d, b), ms) for (d, ms), (b, _) in zip(dressed.frames, bare.frames)]
    seq.tags = bare.tags
    return seq


def human_sheets():
    """creatures/human (plain, dressed) and creatures/human_<body>_<hair> (+ _skin, _hair tint layers)."""
    plain = human_sequence(eyes="round", wear=DEFAULT_WEAR, dye=WOAD)
    write_sheet("creatures/human", plain.frames, plain.tags, PIVOT)
    for body in ("male", "female"):
        for hair in HAIR_STYLES:
            seq = human_sequence(body=body, hair=hair)
            split = [split_layers(img, LAYER_SHADES) for img, _ in seq.frames]
            for layer in ["base", *LAYER_SHADES]:
                frames = [(parts[layer], ms) for parts, (_, ms) in zip(split, seq.frames)]
                suffix = "" if layer == "base" else "_" + layer
                write_sheet(f"creatures/human_{body}_{hair}{suffix}", frames, seq.tags, PIVOT, trim=layer != "base")


def face_sheets():
    """Per body: eyes (tinted iris + as-is lashes) per style, and beards (tinted, plus outline pixels)."""
    for body in ("male", "female"):
        bare = human_sequence(body=body, hair="none")
        for style in EYE_STYLES:
            seq = _diff(human_sequence(body=body, hair="none", eyes=style), bare)
            split = [split_layers(img, EYE_SHADES) for img, _ in seq.frames]
            write_sheet(f"creatures/human_{body}_eyes_{style}", [(p["eyes"], ms) for p, (_, ms) in zip(split, seq.frames)], seq.tags, PIVOT, trim=True)
            write_sheet(f"creatures/human_{body}_face_{style}", [(p["base"], ms) for p, (_, ms) in zip(split, seq.frames)], seq.tags, PIVOT, trim=True)
        for style in BEARDS:
            seq = _diff(human_sequence(body=body, hair="none", beard=style), bare)
            split = [split_layers(img, BEARD_SHADES) for img, _ in seq.frames]
            write_sheet(f"creatures/human_{body}_beard_{style}", [(p["beard"], ms) for p, (_, ms) in zip(split, seq.frames)], seq.tags, PIVOT, trim=True)
            write_sheet(f"creatures/human_{body}_beard_{style}_line", [(p["base"], ms) for p, (_, ms) in zip(split, seq.frames)], seq.tags, PIVOT, trim=True)


def apparel_sheets():
    """apparel/<item>_<body>_<hair>: each item alone (dressed minus bare), in the people's frame layout,
    with dyed cloth split out to <item>_dye_<body>_<hair> and cloak panels to <item>_back_<body>_<hair>.

    Only short and long: the other styles borrow whichever matches (HumanLook.WornSuffix)."""
    for body in ("male", "female"):
        for hair in ("short", "long"):
            look = dict(body=body, hair=hair)
            bare = human_sequence(**look)
            for item in [*GARMENTS, *BODY_ITEMS, *HEAD_ITEMS, *BACK_ITEMS]:
                part = "front" if item in BACK_PANELS else None
                seq = _diff(human_sequence(**look, wear=(item,), part=part), bare)
                _write_dyed(f"apparel/{item}", f"_{body}_{hair}", seq)
                if item in BACK_PANELS:
                    # Drawn first of the layers, so cut away wherever the bare person covers it:
                    # it reads as hanging behind them, and wider garments still draw over it.
                    back = human_sequence(**look, wear=(item,), part="back")
                    back.frames = [(_minus(f, b), ms) for (f, ms), (b, _) in zip(back.frames, bare.frames)]
                    _write_dyed(f"apparel/{item}_back", f"_{body}_{hair}", back)


def _minus(img, cover):
    """img without the pixels cover has."""
    out = img.copy()
    o, c = out.load(), cover.load()
    for y in range(out.height):
        for x in range(out.width):
            if c[x, y][3] > 0:
                o[x, y] = (0, 0, 0, 0)
    return out


def _write_dyed(name, suffix, seq):
    split = [split_layers(img, DYE_SHADES) for img, _ in seq.frames]
    write_sheet(name + suffix, [(p["base"], ms) for p, (_, ms) in zip(split, seq.frames)], seq.tags, PIVOT, trim=True)
    dyed = [(p["dye"], ms) for p, (_, ms) in zip(split, seq.frames)]
    if any(f.getbbox() for f, _ in dyed):
        write_sheet(name + "_dye" + suffix, dyed, seq.tags, PIVOT, trim=True)


def check_markers():
    """Marker colours must be unique, or the layer split would steal pixels."""
    groups = [SKIN, HAIR, BEARD, [EYE_DARK, EYE_MID, EYE], DYE]
    seen = {}
    for gi, g in enumerate(groups):
        for c in g:
            assert c not in seen or seen[c] == gi, f"marker colour {c} is used twice"
            seen[c] = gi
    palette = [*GLOVE, *DARK_LEATHER, *WOAD, *MADDER, *LINEN, *BRAIES, *WALNUT, *WRAP, *SHOE, *LEATHER, *FUR, *GOLD, *BRONZE, *IRON, *MAIL, *WOODC, *TRIM_DARK,
               WHITE, LASH, LID, BLUSH, MOUTH, BEAD_BLUE, BEAD_AMBER]
    for c in palette:
        assert c not in seen, f"palette colour {c} collides with a marker"


def generate():
    check_markers()
    human_sheets()
    face_sheets()
    apparel_sheets()


if __name__ == "__main__":
    generate()
