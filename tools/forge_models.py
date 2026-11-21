#!/usr/bin/env python3
"""The 3D forge prototype's models (`--forge-3d`): a smithy and its smith, as low-poly glTF.

Writes data/models/forge/*.glb: flat-shaded meshes with plain base-colour materials in the
brand's palette, drawn cel-shaded and outlined by Narsil's MeshRenderer (ToonStyle). Edit this
script, not the .glb files. No Blender: a few primitives and a tiny GLB writer.

Units are metres, Y up, the smithy's floor at y = 0, the camera's side towards +Z.
Anything that moves is its own file with its pivot at its origin:
  hammer, tongs  the grip at the origin, the tool along +X
  bar, blade     the tang at the origin, the piece along +X, its face up (+Y)
  smith_*        each joint at the origin: the torso from the hips up, limbs hanging along -Y

    python3 tools/forge_models.py               # write the models
    python3 tools/forge_models.py --preview OUT.png   # and a quick look at the smithy
"""

from __future__ import annotations

import argparse
import json
import math
import struct
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "data" / "models" / "forge"


def srgb(hexcode: str) -> tuple[float, float, float]:
    h = hexcode.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


# The brand's palette (art/brand/brand-guide.html), plus a few the smithy needs.
PAL = {
    "cellar": "#140E0A", "deep": "#261B13", "oak": "#4A3322", "oakl": "#7A5434", "bone": "#F2E6CF",
    "parch": "#D9C49B", "ember": "#FCA342", "red": "#B5432B", "iron": "#808590", "ironDark": "#50545C",
    "brass": "#C99A5B", "brick": "#8A4A34", "brickDark": "#5E3426", "earth": "#5A4634", "stone": "#7C7468",
    "leather": "#6B3F24", "skin": "#D9A07A", "hair": "#3A2618", "linen": "#BFAE8E", "water": "#3E6A78",
    "coal": "#2A2220", "steam": "#E8E4DC", "spark": "#FFD27A", "glow": "#FCA342",
}


class Mesh:
    """Triangles by material, flat-shaded."""

    def __init__(self):
        self.tris: dict[str, list] = {}

    def tri(self, mat: str, a, b, c):
        self.tris.setdefault(mat, []).append((np.array(a, float), np.array(b, float), np.array(c, float)))

    def quad(self, mat, a, b, c, d):
        self.tri(mat, a, b, c)
        self.tri(mat, a, c, d)

    def add(self, other: "Mesh", xf=None):
        for mat, ts in other.tris.items():
            for t in ts:
                self.tri(mat, *(apply(xf, p) if xf is not None else p for p in t))
        return self

    def moved(self, xf) -> "Mesh":
        return Mesh().add(self, xf)


def apply(xf, p):
    v = xf @ np.array([p[0], p[1], p[2], 1.0])
    return v[:3]


def T(x=0, y=0, z=0):
    m = np.eye(4)
    m[:3, 3] = (x, y, z)
    return m


def R(axis: str, deg: float):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    m = np.eye(4)
    i, j = {"x": (1, 2), "y": (2, 0), "z": (0, 1)}[axis]
    m[i, i], m[i, j], m[j, i], m[j, j] = c, -s, s, c
    return m


def S(x=1, y=1, z=1):
    return np.diag([x, y, z, 1.0])


# ---------------- primitives (counter-clockwise from outside) ----------------

def frustum(mat, lo, hi, y0, y1, cx=0.0, cz=0.0):
    """A box whose bottom rectangle (lo = half x, half z) at y0 becomes another (hi) at y1."""
    m = Mesh()
    (bx, bz), (tx, tz) = lo, hi
    b = [(cx - bx, y0, cz - bz), (cx + bx, y0, cz - bz), (cx + bx, y0, cz + bz), (cx - bx, y0, cz + bz)]
    t = [(cx - tx, y1, cz - tz), (cx + tx, y1, cz - tz), (cx + tx, y1, cz + tz), (cx - tx, y1, cz + tz)]
    m.quad(mat, b[0], b[1], b[2], b[3])  # bottom faces down
    m.quad(mat, t[3], t[2], t[1], t[0])  # top faces up
    for i in range(4):
        j = (i + 1) % 4
        m.quad(mat, b[j], b[i], t[i], t[j])
    return m


def box(mat, x0, y0, z0, x1, y1, z1):
    return frustum(mat, ((x1 - x0) / 2, (z1 - z0) / 2), ((x1 - x0) / 2, (z1 - z0) / 2), y0, y1, (x0 + x1) / 2, (z0 + z1) / 2)


def cylinder(mat, r0, r1, y0, y1, n=10, cap_mat=None, top=True, bottom=True):
    m = Mesh()
    ring = lambda r, y: [(r * math.cos(2 * math.pi * k / n), y, -r * math.sin(2 * math.pi * k / n)) for k in range(n)]
    b, t = ring(r0, y0), ring(r1, y1)
    for i in range(n):
        j = (i + 1) % n
        if r1 > 1e-6:
            m.quad(mat, b[i], b[j], t[j], t[i])
        else:
            m.tri(mat, b[i], b[j], (0, y1, 0))
    if top and r1 > 1e-6:
        for i in range(1, n - 1):
            m.tri(cap_mat or mat, t[0], t[i], t[i + 1])
    if bottom:
        for i in range(1, n - 1):
            m.tri(cap_mat or mat, b[0], b[i + 1], b[i])
    return m


def sphere(mat, r, n=8):
    m = Mesh()
    rows = n // 2
    pt = lambda a, b: (r * math.sin(b) * math.cos(a), r * math.cos(b), -r * math.sin(b) * math.sin(a))
    for i in range(rows):
        b0, b1 = math.pi * i / rows, math.pi * (i + 1) / rows
        for k in range(n):
            a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
            if i == 0:
                m.tri(mat, pt(a0, b0), pt(a0, b1), pt(a1, b1))
            elif i == rows - 1:
                m.tri(mat, pt(a0, b0), pt(a0, b1), pt(a1, b0))
            else:
                m.quad(mat, pt(a0, b0), pt(a0, b1), pt(a1, b1), pt(a1, b0))
    return m


# ---------------- the smithy ----------------

def room():
    m = Mesh()
    m.add(box("earth", -3.5, -0.1, -3.0, 3.5, 0.0, 3.0))  # the floor
    m.add(box("stone", -3.7, 0.0, -3.2, 3.7, 3.0, -3.0))  # back wall
    m.add(box("stone", -3.7, 0.0, -3.0, -3.5, 3.0, 3.0))  # left wall
    for x in (-3.3, 3.3):  # posts and the beam
        m.add(box("oak", x - 0.12, 0.0, -2.95, x + 0.12, 2.8, -2.7))
    m.add(box("oak", -3.5, 2.6, -2.95, 3.5, 2.85, -2.7))
    # a rack of tools on the back wall, and a bench by the tub
    m.add(box("oakl", 0.4, 1.3, -2.98, 2.2, 1.4, -2.88))
    for i, x in enumerate((0.6, 0.95, 1.3, 1.65, 2.0)):
        length = 0.55 - 0.08 * (i % 3)
        m.add(box("oak", x - 0.02, 1.35 - length, -2.93, x + 0.02, 1.38, -2.89))
        m.add(box("ironDark", x - 0.06, 1.35 - length - 0.08, -2.94, x + 0.06, 1.35 - length + 0.02, -2.88))
    m.add(box("oakl", 2.3, 0.75, -2.6, 3.4, 0.85, -1.8))
    for x, z in ((2.4, -2.5), (3.3, -2.5), (2.4, -1.9), (3.3, -1.9)):
        m.add(box("oak", x - 0.05, 0.0, z - 0.05, x + 0.05, 0.75, z + 0.05))
    m.add(box("iron", 2.5, 0.85, -2.4, 2.9, 0.9, -2.3))  # a finished blank on the bench
    # a pile of charcoal by the hearth
    m.add(frustum("coal", (0.35, 0.3), (0.12, 0.1), 0.0, 0.25, -3.0, -0.6))
    return m


HEARTH = (-2.0, -1.9)  # x, z of the fire's centre
FIRE_Y = 0.92


def hearth():
    x, z = HEARTH
    m = Mesh()
    m.add(box("brick", x - 0.75, 0.0, z - 0.6, x + 0.75, 0.8, z + 0.6))
    m.add(box("brickDark", x - 0.8, 0.8, z - 0.65, x + 0.8, 0.88, z + 0.65))  # the lip
    m.add(box("coal", x - 0.55, 0.88, z - 0.45, x + 0.55, 0.9, z + 0.45))  # the bed of spent coal (the live coals glow: their own model)
    for dx in (-0.65, 0.65):  # the pan's low walls round the coals
        m.add(box("brick", x + dx - 0.1, 0.88, z - 0.55, x + dx + 0.1, 1.0, z + 0.55))
    m.add(box("brick", x - 0.55, 0.88, z - 0.65, x + 0.55, 1.0, z - 0.45))
    # the hood and its chimney
    m.add(frustum("brickDark", (0.85, 0.7), (0.3, 0.3), 1.75, 2.35, x, z - 0.1))
    m.add(box("brickDark", x - 0.3, 2.35, z - 0.4, x + 0.3, 3.2, z + 0.2))
    for dx in (-0.78, 0.78):
        m.add(box("brick", x + dx - 0.07, 0.88, z - 0.6, x + dx + 0.07, 1.75, z - 0.46))
    return m


def coals():
    """The fire's bed, its glow set at draw time from the fire's heat."""
    x, z = HEARTH
    m = Mesh()
    rng = np.random.default_rng(7)
    for i in range(46):
        cx, cz = x + rng.uniform(-0.5, 0.5), z + rng.uniform(-0.4, 0.4)
        s = rng.uniform(0.035, 0.06)
        m.add(frustum("glow", (s, s * 0.8), (s * 0.6, s * 0.5), 0.0, s * 0.9).moved(T(cx, 0.885, cz) @ R("y", rng.uniform(0, 90))))
    return m


def bellows_body():
    """The bellows beside the hearth, their boards open; drawn squashed as they're worked (pivot at the nozzle)."""
    m = Mesh()
    m.add(frustum("leather", (0.05, 0.12), (0.32, 0.25), 0.0, 0.0001).moved(R("z", 0)))  # placeholder for orientation
    m = Mesh()
    # lying along -X from the nozzle at the origin: boards top and bottom, leather between
    m.add(frustum("leather", (0.06, 0.08), (0.06, 0.24), 0.0, 0.7).moved(R("z", 90)))
    m.add(box("oakl", -0.75, 0.06, -0.26, -0.05, 0.09, 0.26))
    m.add(box("oakl", -0.75, -0.09, -0.26, -0.05, -0.06, 0.26))
    m.add(box("oak", -1.05, 0.06, -0.04, -0.75, 0.09, 0.04))  # the handle
    m.add(cylinder("iron", 0.03, 0.02, 0.0, 0.25).moved(R("z", -90)))
    return m


ANVIL = (0.2, 0.2)  # x, z of the anvil's face
ANVIL_FACE = 0.86


def anvil():
    x, z = ANVIL
    m = Mesh()
    m.add(cylinder("oak", 0.34, 0.3, 0.0, 0.48, 9))  # the stump
    a = Mesh()
    a.add(frustum("ironDark", (0.22, 0.17), (0.17, 0.12), 0.48, 0.56))  # the foot
    a.add(frustum("ironDark", (0.12, 0.08), (0.09, 0.07), 0.56, 0.7))  # the waist
    a.add(frustum("iron", (0.15, 0.08), (0.28, 0.1), 0.7, ANVIL_FACE - 0.06))
    a.add(box("iron", -0.3, ANVIL_FACE - 0.06, -0.1, 0.3, ANVIL_FACE, 0.1))  # the face
    a.add(cylinder("iron", 0.09, 0.0, 0.0, 0.38, 8).moved(T(0.3, ANVIL_FACE - 0.08, 0) @ R("z", -90) @ S(1, 1, 0.9)))  # the horn
    a.add(box("iron", -0.42, ANVIL_FACE - 0.1, -0.07, -0.3, ANVIL_FACE - 0.02, 0.07))  # the heel
    m.add(a)
    return m.moved(T(x, 0, z))


TUB = (2.0, -0.9)
WATER_Y = 0.55


def tub():
    x, z = TUB
    m = Mesh()
    m.add(cylinder("oakl", 0.42, 0.46, 0.0, 0.62, 12, cap_mat="oak", top=False))
    for y in (0.1, 0.5):
        m.add(cylinder("ironDark", 0.475, 0.475, y, y + 0.05, 12, top=False, bottom=False))
    return m.moved(T(x, 0, z))


def water():
    x, z = TUB
    return cylinder("water", 0.43, 0.43, WATER_Y - 0.01, WATER_Y, 12, bottom=False).moved(T(x, 0, z))


# ---------------- tools and the piece ----------------

def hammer():
    m = Mesh()
    m.add(box("oakl", 0.0, -0.016, -0.016, 0.42, 0.016, 0.016))
    m.add(box("ironDark", 0.38, -0.05, -0.035, 0.48, 0.08, 0.035))  # the head: face down (-Y), peen up
    m.add(frustum("ironDark", (0.05, 0.035), (0.04, 0.008), 0.08, 0.13, 0.43, 0))
    return m


def tongs():
    m = Mesh()
    for dz in (-0.012, 0.012):
        m.add(box("ironDark", 0.0, -0.008, dz - 0.007, 0.42, 0.008, dz + 0.007))
    m.add(box("ironDark", 0.4, -0.02, -0.025, 0.5, 0.02, 0.025))
    return m


def bar():
    """The iron as it comes from the stock: a short square bar, the tang end at the origin."""
    return box("iron", 0.0, -0.02, -0.02, 0.32, 0.02, 0.02)


def blade():
    """The bar drawn out: a flat blade with its tang, fuller and point, 0.75 long."""
    m = Mesh()
    m.add(box("iron", 0.0, -0.007, -0.012, 0.14, 0.007, 0.012))  # tang
    w, t, L = 0.03, 0.007, 0.56
    x0 = 0.14
    # the blade as a lozenge section tapering to a point, its edges at ±w, spine at ±t
    sec = lambda x, s: [(x, 0, -w * s), (x, t * s, 0), (x, 0, w * s), (x, -t * s, 0)]
    a, b = sec(x0, 1.0), sec(x0 + L, 0.85)
    tip = (x0 + L + 0.08, 0, 0)
    for i in range(4):
        j = (i + 1) % 4
        m.quad("iron", a[j], a[i], b[i], b[j])
        m.tri("iron", b[j], b[i], tip)
    m.quad("iron", a[3], a[2], a[1], a[0])
    return m


# ---------------- the smith ----------------

def smith_torso():
    """From the hips up: the shirt, the leather apron in front, shoulders at y 0.55, ±0.2."""
    m = Mesh()
    m.add(frustum("linen", (0.17, 0.11), (0.22, 0.12), 0.0, 0.58))
    m.add(box("leather", -0.18, -0.25, 0.1, 0.18, 0.48, 0.135))  # the apron
    m.add(box("leather", -0.04, 0.48, 0.11, 0.04, 0.6, 0.125))
    m.add(cylinder("skin", 0.06, 0.06, 0.58, 0.66, 8))  # the neck
    return m


def smith_head():
    m = Mesh()
    m.add(box("skin", -0.1, 0.0, -0.1, 0.1, 0.24, 0.1))
    m.add(box("hair", -0.105, 0.17, -0.105, 0.105, 0.26, 0.09))  # hair and a cap
    m.add(box("hair", -0.105, 0.04, -0.105, 0.105, 0.2, -0.07))
    m.add(box("hair", -0.08, 0.0, 0.08, 0.08, 0.06, 0.11))  # the beard
    m.add(box("cellar", -0.06, 0.12, 0.1, -0.03, 0.15, 0.105))  # the eyes
    m.add(box("cellar", 0.03, 0.12, 0.1, 0.06, 0.15, 0.105))
    return m


def smith_upper_arm():
    return Mesh().add(frustum("linen", (0.055, 0.055), (0.045, 0.045), -0.3, 0.02))


def smith_forearm():
    """From the elbow down to the hand, which holds at y -0.3."""
    m = Mesh()
    m.add(frustum("skin", (0.035, 0.035), (0.042, 0.042), -0.26, 0.0))
    m.add(box("skin", -0.045, -0.34, -0.035, 0.045, -0.25, 0.035))
    return m


def smith_leg():
    m = Mesh()
    m.add(frustum("oak", (0.07, 0.07), (0.085, 0.085), -0.78, 0.0))
    m.add(box("deep", -0.075, -0.86, -0.08, 0.075, -0.76, 0.14))  # the boot
    return m


# ---------------- effects ----------------

def spark():
    return box("spark", -0.03, -0.004, -0.004, 0.03, 0.004, 0.004)


def puff():
    return sphere("steam", 0.1, 8)


def mark():
    """Where the next blow wants to land: a thin ring laid on the piece's face."""
    m = Mesh()
    n = 12
    for k in range(n):
        a0, a1 = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
        p = lambda a, r: (r * math.cos(a), 0, -r * math.sin(a))
        m.quad("spark", p(a0, 0.03), p(a0, 0.045), p(a1, 0.045), p(a1, 0.03))
    return m


MODELS = {
    "room": room, "hearth": hearth, "coals": coals, "bellows": bellows_body, "anvil": anvil, "tub": tub, "water": water,
    "hammer": hammer, "tongs": tongs, "bar": bar, "blade": blade,
    "smith_torso": smith_torso, "smith_head": smith_head, "smith_upper_arm": smith_upper_arm,
    "smith_forearm": smith_forearm, "smith_leg": smith_leg,
    "spark": spark, "puff": puff, "mark": mark,
}

EMISSIVE = {"glow": 0.0, "spark": 1.0}  # sparks glow by themselves; the coals' glow is set at draw time


# ---------------- GLB ----------------

def to_linear(c):
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]


def write_glb(mesh: Mesh, path: Path):
    blob = bytearray()
    views, accessors, prims, materials = [], [], [], []

    def add_view(data: bytes, target: int) -> int:
        while len(blob) % 4:
            blob.append(0)
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(data), "target": target})
        blob.extend(data)
        return len(views) - 1

    for mat, tris in mesh.tris.items():
        pos, nrm = [], []
        for a, b, c in tris:
            n = np.cross(b - a, c - a)
            ln = np.linalg.norm(n)
            if ln < 1e-12:
                continue
            n = n / ln
            for p in (a, b, c):
                pos.append(p)
                nrm.append(n)
        if not pos:
            continue
        pos, nrm = np.array(pos, np.float32), np.array(nrm, np.float32)
        idx = np.arange(len(pos), dtype=np.uint32)
        pv = add_view(pos.tobytes(), 34962)
        accessors.append({"bufferView": pv, "componentType": 5126, "count": len(pos), "type": "VEC3",
                          "min": pos.min(0).tolist(), "max": pos.max(0).tolist()})
        nv = add_view(nrm.tobytes(), 34962)
        accessors.append({"bufferView": nv, "componentType": 5126, "count": len(nrm), "type": "VEC3"})
        iv = add_view(idx.tobytes(), 34963)
        accessors.append({"bufferView": iv, "componentType": 5125, "count": len(idx), "type": "SCALAR"})
        material = {"name": mat, "pbrMetallicRoughness": {"baseColorFactor": to_linear(srgb(PAL[mat])) + [1.0],
                                                           "metallicFactor": 0.0, "roughnessFactor": 1.0}}
        if EMISSIVE.get(mat, 0) > 0:
            material["emissiveFactor"] = [x * EMISSIVE[mat] for x in to_linear(srgb(PAL[mat]))]
        materials.append(material)
        prims.append({"attributes": {"POSITION": len(accessors) - 3, "NORMAL": len(accessors) - 2},
                      "indices": len(accessors) - 1, "material": len(materials) - 1})

    gltf = {
        "asset": {"version": "2.0", "generator": "tools/forge_models.py"},
        "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0, "name": path.stem}],
        "meshes": [{"name": path.stem, "primitives": prims}], "materials": materials,
        "accessors": accessors, "bufferViews": views, "buffers": [{"byteLength": len(blob)}],
    }
    js = json.dumps(gltf, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    blob += b"\0" * (-len(blob) % 4)
    out = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(js) + 8 + len(blob))
    out += struct.pack("<II", len(js), 0x4E4F534A) + js
    out += struct.pack("<II", len(blob), 0x004E4942) + bytes(blob)
    path.write_bytes(out)


# ---------------- preview ----------------

def preview(path: Path):
    """The static smithy from the game's default angle, flat-shaded with a painter's sort: a quick check, not the look."""
    from PIL import Image, ImageDraw

    scene = Mesh()
    for name in ("room", "hearth", "coals", "anvil", "tub", "water"):
        scene.add(MODELS[name]())
    scene.add(MODELS["bellows"](), T(HEARTH[0] - 0.85, 1.05, HEARTH[1] + 0.05))
    scene.add(blade(), T(ANVIL[0] - 0.3, ANVIL_FACE + 0.007, ANVIL[1]))
    W, H = 960, 640
    eye, target = np.array([1.4, 2.6, 4.4]), np.array([-0.2, 0.8, -0.8])
    f = target - eye
    f /= np.linalg.norm(f)
    r = np.cross(f, [0, 1, 0])
    r /= np.linalg.norm(r)
    u = np.cross(r, f)
    light = np.array([0.5, 0.8, 0.6])
    light /= np.linalg.norm(light)
    polys = []
    for mat, tris in scene.tris.items():
        base = np.array(srgb(PAL[mat]))
        for a, b, c in tris:
            n = np.cross(b - a, c - a)
            if np.linalg.norm(n) < 1e-12:
                continue
            n /= np.linalg.norm(n)
            centre = (a + b + c) / 3
            if np.dot(n, eye - centre) < 0:
                continue
            pts = []
            for p in (a, b, c):
                d = p - eye
                z = np.dot(d, f)
                pts.append((W / 2 + np.dot(d, r) / z * W * 0.9, H / 2 - np.dot(d, u) / z * W * 0.9))
            shade = 0.45 + 0.55 * (1.0 if np.dot(n, light) > 0.35 else 0.5)
            col = tuple(int(255 * min(1, x * shade)) for x in base)
            polys.append((np.dot(centre - eye, f), pts, col))
    img = Image.new("RGB", (W, H), srgb_to_255(PAL["cellar"]))
    draw = ImageDraw.Draw(img)
    for _, pts, col in sorted(polys, key=lambda p: -p[0]):
        draw.polygon(pts, fill=col, outline=(20, 14, 10))
    img.save(path)
    print(f"preview: {path}")


def srgb_to_255(h):
    return tuple(int(255 * x) for x in srgb(h))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--preview", type=Path, help="also draw the smithy to this PNG")
    args = ap.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    for name, build in MODELS.items():
        write_glb(build(), OUT / f"{name}.glb")
    print(f"wrote {len(MODELS)} models to {OUT.relative_to(ROOT)}")
    if args.preview:
        preview(args.preview)


if __name__ == "__main__":
    main()
