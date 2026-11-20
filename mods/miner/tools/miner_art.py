#!/usr/bin/env python3
"""The miner mod's own world sprites, made from nothing (docs/MODDING.md): run from the repo root.

    python3 mods/miner/tools/miner_art.py

Writes into the mod's textures/ folder, never the game's. The chalk a miner leaves on a rock face
(Miner.ChalkMarks), drawn flat on the face's top by the game: an X for a seam, a ring for nothing. White and
grainy, as chalk on rough stone is; the game tints them. And the wheelbarrow: its item picture, and its model's faces. The world is seen at 45 degrees, so the X is drawn here
as a cross, which the view turns into an X. (The pick in the hewing window is tools/work_ui_art.py's.)
"""
import os

import numpy as np
from PIL import Image, ImageDraw

MOD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
SIZE = 32


def chalk(strokes, seed, rings=()):
    """Chalk strokes (each a list of points) and rings (centre, radius) on a clear square: drawn wide, then rubbed thin
    where the stone's grain catches."""
    big = Image.new("L", (SIZE * 4, SIZE * 4), 0)
    d = ImageDraw.Draw(big)
    for pts in strokes:
        d.line([(x * 4, y * 4) for x, y in pts], fill=255, width=12, joint="curve")
    for (cx, cy), r in rings:
        d.ellipse([(cx - r) * 4, (cy - r) * 4, (cx + r) * 4, (cy + r) * 4], outline=255, width=11)
    mask = np.asarray(big.resize((SIZE, SIZE), Image.LANCZOS)).astype(float) / 255
    rng = np.random.default_rng(seed)
    grain = rng.uniform(0.45, 1.0, mask.shape)
    alpha = np.clip(mask * grain * 1.15, 0, 1)
    rgba = np.zeros((SIZE, SIZE, 4), np.uint8)
    rgba[..., :3] = 244
    rgba[..., 3] = (alpha * 255).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")


def wheelbarrow():
    """The wheelbarrow as an item lying in the world (items/wheelbarrow), three-quarter on like the game's items: a
    plank-sided tray, its iron-shod wheel at the front (left), the handles out behind (right), two legs under it."""
    import sys
    sys.path.insert(0, os.path.join(MOD, "..", "..", "tools"))
    from norse_ui import part, poly, stick, ell, put, ink_outline, OAK, IRONR, WALNUT
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    part(img, stick((20, 17), (31, 22), 1.6, 1.4), WALNUT, radius=0.5)               # the far handle
    part(img, stick((21, 22), (25, 28), 1.3, 1.3), WALNUT, radius=0.5)               # a leg
    part(img, poly([(4, 11), (24, 11), (22, 21), (8, 21)]), OAK, radius=0.8)          # the tray, its sides planked
    part(img, poly([(6, 12), (22, 12), (20.5, 15), (8, 15)]), [(c[0] // 2, c[1] // 2, c[2] // 2, 255) for c in OAK], radius=0.4)  # inside it
    part(img, ell([2, 19, 11, 28]), IRONR, radius=0.8)                                # the wheel
    part(img, ell([5, 22, 8, 25]), WALNUT, radius=0.4)                                # its hub
    part(img, stick((17, 20), (30, 25), 1.6, 1.4), WALNUT, radius=0.5)               # the near handle
    for x in range(5, 24, 4):
        put(img, x, 18, (70, 48, 30, 255))                                            # the planks' seams
    return ink_outline(img)


def barrow_props():
    """The pushed barrow's model (miner.json's Wheelbarrow "model"): its planked sides, the tray's inside, a handle's
    plain wood, the spoked wheel's face (an iron tyre round it, clear at the corners), and a heap of hewn ore for its
    load. 32 pixels square, as the game's props are."""
    rng = np.random.default_rng(7)
    out = {}
    oak = np.array([122, 84, 52]); dark = np.array([70, 46, 28]); light = np.array([150, 108, 70])

    def planks(base, seam, rows, rim=None):
        a = np.zeros((SIZE, SIZE, 4), np.uint8)
        for y in range(SIZE):
            for x in range(SIZE):
                c = base + rng.integers(-8, 9) + (6 if (x * 3 + y * 7) % 13 == 0 else 0)
                if y % (SIZE // rows) == 0:
                    c = seam
                a[y, x, :3] = np.clip(c, 0, 255)
                a[y, x, 3] = 255
        if rim is not None:
            a[:2, :, :3] = rim
            a[-2:, :, :3] = rim
        return Image.fromarray(a, "RGBA")

    out["barrow_side"] = planks(oak, dark, 3, rim=light)
    out["barrow_tray"] = planks(dark + 10, dark - 25, 4, rim=oak)
    out["barrow_handle"] = planks(light - 10, oak, 1)
    # The wheel: a dark wooden face with spokes, a grey iron tyre round it.
    wheel = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(wheel)
    d.ellipse([1, 1, 30, 30], fill=(92, 96, 108, 255))
    d.ellipse([4, 4, 27, 27], fill=(96, 64, 40, 255))
    for k in range(6):
        import math
        ang = k * math.pi / 3
        d.line([(16, 16), (16 + 11 * math.cos(ang), 16 + 11 * math.sin(ang))], fill=(132, 92, 58, 255), width=3)
    d.ellipse([12, 12, 20, 20], fill=(60, 62, 70, 255))
    out["barrow_wheel"] = wheel
    # A heap of hewn ore: grey rock and rust-red lumps, darker between.
    heap = np.zeros((SIZE, SIZE, 4), np.uint8)
    heap[..., :3] = (58, 54, 52)
    heap[..., 3] = 255
    for _ in range(26):
        cx, cy, r = rng.integers(0, SIZE), rng.integers(0, SIZE), rng.integers(3, 7)
        colour = (150, 80, 52) if rng.random() < 0.45 else (118, 116, 118) if rng.random() < 0.6 else (40, 38, 40)
        for y in range(max(0, cy - r), min(SIZE, cy + r)):
            for x in range(max(0, cx - r), min(SIZE, cx + r)):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                    lit = 1.15 if (x - cx) + (y - cy) < -r * 0.3 else 0.85 if (x - cx) + (y - cy) > r * 0.5 else 1.0
                    heap[y, x, :3] = np.clip(np.array(colour) * lit, 0, 255)
    out["barrow_ore"] = Image.fromarray(heap, "RGBA")
    return out


def main():
    out = os.path.join(MOD, "textures", "props")
    os.makedirs(out, exist_ok=True)
    chalk([[(16, 4), (16.5, 16), (16, 28)], [(4, 16.5), (16, 16), (28, 15.5)]], 1).save(os.path.join(out, "chalk_seam.png"))
    chalk([], 2, rings=[((16, 16), 9)]).save(os.path.join(out, "chalk_bare.png"))
    items = os.path.join(MOD, "textures", "items")
    os.makedirs(items, exist_ok=True)
    wheelbarrow().save(os.path.join(items, "wheelbarrow.png"))
    for name, img in barrow_props().items():
        img.save(os.path.join(out, name + ".png"))
    print("miner: wrote props/chalk_seam.png, props/chalk_bare.png, items/wheelbarrow.png, props/barrow_*.png")


if __name__ == "__main__":
    main()
