#!/usr/bin/env python3
"""Textures for the preset buildings' art kits (data/defs/buildings.json "kits", docs/ART.md
"Building kits"):

- buildings/gable_boards[_<wood>]: the stave wall's boards cut to the gable's triangle (opaque
  below the diagonal, the high side on the right), one per wood material like the wall art,
  so a birch house gets birch gables. The renderer mirrors it for the other slope.
- buildings/trim_carved: a barge board, 32x8, dark wood with chip-carved triangles along it.
- buildings/post_top: a corner post's end grain filling the face (the log end's rings, ringed with
  bark), so a post's top has no square background; turned per post.
- buildings/door_carved[_<wood>]: the plank door with iron straps and a carved panel.
- buildings/turf_edge: the lip of a turf roof's eaves, sod with grass hanging over it (32x8).
- buildings/roof_turf[_<wood>]_v2/_v3, roof_thatch[...]_v2/_v3: the roof art rolled and mirrored,
  so a long slope doesn't repeat cell by cell (same colours, so no checkerboard).

Run from the repo root: python3 tools/norse_kit.py
"""
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
TEX = ROOT / "data" / "textures" / "buildings"


def gable(src: Path, dst: Path) -> None:
    im = Image.open(src).convert("RGBA")
    w, h = im.size
    px = im.load()
    for y in range(h):
        for x in range(w):
            # Keep what's below the line from bottom-left to top-right (with a pixel's margin).
            if (h - 1 - y) > (x * (h - 1)) / (w - 1):
                px[x, y] = (0, 0, 0, 0)
    # A darker board along the slope, where the barge board sits.
    for x in range(w):
        y = round(h - 1 - x * (h - 1) / (w - 1))
        for d in (0, 1):
            if 0 <= y + d < h and px[x, y + d][3]:
                r, g, b, a = px[x, y + d]
                px[x, y + d] = (int(r * 0.7), int(g * 0.7), int(b * 0.7), a)
    im.save(dst)


def trim(dst: Path) -> None:
    w, h = 32, 8
    dark, mid, light = (74, 50, 32, 255), (104, 72, 46, 255), (140, 102, 66, 255)
    im = Image.new("RGBA", (w, h), mid)
    px = im.load()
    for x in range(w):
        px[x, 0] = light
        px[x, h - 1] = dark
    # Chip-carved triangles every 8 px: a dark notch pointing down, a lit pixel above it.
    for start in range(0, w, 8):
        for y in range(2, 6):
            half = 5 - y  # widest at the top row
            for x in range(start + 4 - half, start + 4 + half):
                if 0 <= x < w:
                    px[x, y] = dark
        px[start + 4, 1] = light
    im.save(dst)


def post_top(src: Path, dst: Path) -> None:
    im = Image.open(src).convert("RGBA")
    w, h = im.size
    px = im.load()
    out = Image.new("RGBA", (w, h))
    o = out.load()
    cx, cy = (w - 1) / 2, (h - 1) / 2
    # The rings fill a disc of 0.9 of the face; bark rings it out to the corners.
    bark_dark, bark = (58, 42, 30, 255), (82, 60, 42, 255)
    for y in range(h):
        for x in range(w):
            dx, dy = (x - cx) / cx, (y - cy) / cy
            r = (dx * dx + dy * dy) ** 0.5
            if r <= 0.9:
                # Sample the source disc (radius about 0.8 of its half-size) scaled up to ours.
                sx = round(cx + dx / 0.9 * 0.8 * cx)
                sy = round(cy + dy / 0.9 * 0.8 * cy)
                r, g, b, a = px[min(max(sx, 0), w - 1), min(max(sy, 0), h - 1)]
                # Weathered: a post's end grain has greyed and darkened, not fresh-cut pale.
                o[x, y] = (int(r * 0.62 + 18), int(g * 0.58 + 14), int(b * 0.52 + 10), a)
            else:
                o[x, y] = bark_dark if (x + y) % 3 == 0 or r > 1.2 else bark
    out.save(dst)


def door(src: Path, dst: Path) -> None:
    im = Image.open(src).convert("RGBA")
    w, h = im.size
    px = im.load()

    def tone(x: int, y: int, f: float) -> None:
        r, g, b, a = px[x, y]
        if a:
            px[x, y] = (min(255, int(r * f)), min(255, int(g * f)), min(255, int(b * f)), a)

    # A chip-carved diamond with a knot at its heart in the middle panel (the hinges are already there):
    # cut faces in shadow, the edges caught by the light.
    cx, cy = w // 2, h // 2
    for y in range(cy - 10, cy + 11):
        for x in range(cx - 10, cx + 11):
            d = abs(x - cx) + abs(y - cy)
            if d in (8, 9) or d in (3, 4):
                tone(x, y, 0.55)
            elif d in (7, 2):
                tone(x, y, 1.25)
    im.save(dst)


def turf_edge(dst: Path) -> None:
    w, h = 32, 8
    soil, dark, grass, light = (78, 58, 40, 255), (58, 42, 30, 255), (70, 120, 52, 255), (104, 156, 70, 255)
    im = Image.new("RGBA", (w, h), soil)
    px = im.load()
    for x in range(w):
        px[x, h - 1] = dark
        # Grass over the top, a blade hanging down every few pixels.
        hang = 3 + (x * 7 + 3) % 5 if x % 3 != 1 else 2
        for y in range(0, min(hang, h - 1)):
            px[x, y] = light if y == 0 or (x + y) % 5 == 0 else grass
    im.save(dst)


def roll_variants(src: Path) -> None:
    im = Image.open(src).convert("RGBA")
    w, h = im.size
    for k, (shift, mirror) in enumerate([(w // 2, True), (w // 3, False)], start=2):
        v = Image.new("RGBA", (w, h))
        v.paste(im.crop((shift, 0, w, h)), (0, 0))
        v.paste(im.crop((0, 0, shift, h)), (w - shift, 0))
        if mirror:
            v = v.transpose(Image.FLIP_LEFT_RIGHT)
        v.save(src.with_name(f"{src.stem}_v{k}.png"))


def main() -> None:
    for suffix in ("", "_oak", "_pine", "_birch"):
        src = TEX / f"wall_planks{suffix}.png"
        if src.exists():
            gable(src, TEX / f"gable_boards{suffix}.png")
    trim(TEX / "trim_carved.png")
    post_top(ROOT / "data" / "textures" / "props" / "log_end.png", TEX / "post_top.png")
    for suffix in ("", "_oak", "_pine", "_birch"):
        if (TEX / f"door{suffix}.png").exists():
            door(TEX / f"door{suffix}.png", TEX / f"door_carved{suffix}.png")
    turf_edge(TEX / "turf_edge.png")
    for roof in ("roof_turf", "roof_thatch"):
        for suffix in ("", "_oak", "_pine", "_birch"):
            if (TEX / f"{roof}{suffix}.png").exists():
                roll_variants(TEX / f"{roof}{suffix}.png")
    print("wrote gable_boards*, trim_carved, post_top, door_carved*, turf_edge, roof variants")


if __name__ == "__main__":
    main()
