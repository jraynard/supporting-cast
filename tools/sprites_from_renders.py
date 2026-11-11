#!/usr/bin/env python3
"""Turn a 3D character's renders (tools/blender/render_layers.py) into the game's layered sheets:

    python3 tools/sprites_from_renders.py art/3d/wanderer.json              # -> data/textures
    python3 tools/sprites_from_renders.py art/3d/wanderer.json --aseprite   # ...and art/creatures/<model>.aseprite to touch up

For every frame and layer: shrink the render to the frame size (each pixel the average of its
block, kept if at least half covered), then snap it to pixel-art colours by the layer's role:

  skin, hair, beard, eyes, dye   tinted by the game: brightness snaps to that layer's grey steps
                                 (norse_people's marker greys; white is the chosen colour). Model
                                 these white or light grey so the shading is all that's left; one
                                 modelled in colour has its brightness stretched onto the steps'
                                 range ("levels": false keeps it as is)
  plain                          drawn as is: snapped to a palette of "colors" (12) made from all of
                                 the layer's frames, so the colours hold still as it animates
  base                           drawn as is like plain (the underclothes, say), and the figure's
                                 outline goes here wherever it borders a tinted layer: a dark,
                                 violet-leaning version of what it borders (norse_art's selective
                                 outline). Its collection is optional.

Writes each layer's sheet with the renders' frames and tags (plus the plain idle/walk/work aliases
of the front view), its pivot and frame size, through the hand-drawn guard on purpose: a 3D
character is a source of its own sheets. docs/ART.md "3D to sprites" has the whole workflow.
"""
import argparse
import json
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
import norse_people as P  # noqa: E402
from norse_art import COOL, clamp8  # noqa: E402
from placeholder_sheets import write_sheet  # noqa: E402

REPO = os.path.normpath(os.path.join(os.path.dirname(__file__), ".."))

GREYS = {
    "skin": P.SKIN_GREYS, "hair": P.HAIR_GREYS, "beard": P.BEARD_GREYS, "eyes": P.EYE_GREYS, "dye": P.DYE_GREYS,
}


def shrink(img, size, cover=0.45):
    """A supersampled render down to the frame size: each pixel its block's average colour (alpha
    weighted, so the transparent background doesn't darken the edges), kept if mostly covered."""
    small = img.convert("RGBa").resize(size, Image.BOX).convert("RGBA")
    px = small.load()
    cut = int(255 * cover)
    for y in range(small.height):
        for x in range(small.width):
            r, g, b, a = px[x, y]
            px[x, y] = (r, g, b, 255) if a >= cut else (0, 0, 0, 0)
    return small


def lum(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def levels(frames, greys):
    """A tint layer modelled in colour (VRoid's blue hair, say): stretch its brightness, over all its
    frames, onto the grey steps' range, so its shading survives the tint. None if already in range."""
    values = sorted(lum(c) for img in frames for c in (img.get_flattened_data() if hasattr(img, "get_flattened_data") else img.getdata()) if c[3])
    if not values:
        return None
    lo, hi = values[len(values) // 50], values[-1 - len(values) // 50]
    if hi - lo < 8 or (lo >= min(greys) - 12 and hi <= max(greys) + 12):
        return None
    return lambda v: min(greys) + (v - lo) * (max(greys) - min(greys)) / (hi - lo)


def to_greys(img, greys, gain=1.0, stretch=None):
    """Brightness to the nearest of a tint layer's grey steps."""
    out = img.copy()
    px = out.load()
    for y in range(out.height):
        for x in range(out.width):
            r, g, b, a = px[x, y]
            if a:
                v = lum((r, g, b))
                v = (stretch(v) if stretch else v) * gain
                v = min(greys, key=lambda s: abs(s - v))
                px[x, y] = (v, v, v, 255)
    return out


def palette_snap(frames, colors):
    """Snap every frame of a plain layer to one palette built from all of them."""
    opaque = [img for img in frames if img.getbbox()]
    if not opaque:
        return frames
    w, h = frames[0].size
    strip = Image.new("RGB", (w * len(opaque), h))
    for i, img in enumerate(opaque):
        strip.paste(img.convert("RGB"), (i * w, 0), img)
    pal = strip.quantize(colors=colors, method=Image.Quantize.MEDIANCUT)
    out = []
    for img in frames:
        snapped = img.convert("RGB").quantize(palette=pal, dither=Image.Dither.NONE).convert("RGBA")
        snapped.putalpha(img.getchannel("A"))
        out.append(snapped)
    return out


def outline(layers, roles, base, partners):
    """Outline the whole figure: each transparent pixel touching it, in a dark, violet-leaning
    version of what it touches, goes on the layer it touches if that's drawn as is, else on the
    layer that layer names (a dye's garment) or the base."""
    w, h = layers[0].size
    loads = [img.load() for img in layers]
    owner = [[-1] * w for _ in range(h)]
    for i, px in enumerate(loads):
        for y in range(h):
            for x in range(w):
                if px[x, y][3]:
                    owner[y][x] = i
    marks = []
    for y in range(h):
        for x in range(w):
            if owner[y][x] >= 0:
                continue
            tot, n, first = [0, 0, 0], 0, None
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < w and 0 <= ny < h and owner[ny][nx] >= 0:
                    o = owner[ny][nx]
                    c = loads[o][nx, ny]
                    tot = [tot[k] + c[k] for k in range(3)]
                    n += 1
                    first = o if first is None else first
            if n:
                dark = [tot[k] / n * 0.34 for k in range(3)]
                color = tuple(clamp8(dark[k] * 0.62 + COOL[k] * 0.38) for k in range(3)) + (255,)
                target = first if roles[first] in ("plain", "base") else partners.get(first, base)
                marks.append((target, x, y, color))
    for target, x, y, color in marks:
        loads[target][x, y] = color


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("config", help="the character's render config (art/3d/<name>.json)")
    ap.add_argument("--renders", help="the renders' folder (default: the config's, or .cache/renders/<name>)")
    ap.add_argument("--out", help="where the sheets go (default: data/textures)")
    ap.add_argument("--aseprite", action="store_true", help="also gather the sheets into art/creatures/<name>.aseprite to touch up")
    args = ap.parse_args()

    with open(args.config) as f:
        cfg = json.load(f)
    name = os.path.splitext(os.path.basename(args.config))[0]
    renders = args.renders or os.path.join(REPO, cfg.get("renders", os.path.join(".cache", "renders", name)))
    with open(os.path.join(renders, "renders.json")) as f:
        info = json.load(f)
    w, h = info["frame"]
    layers = cfg["layers"]
    roles = [layer.get("role", "plain") for layer in layers]
    base = next((i for i, r in enumerate(roles) if r == "base"), 0)
    sheets = [layer["sheet"] for layer in layers]
    partners = {i: sheets.index(layer["outline_to"]) for i, layer in enumerate(layers) if layer.get("outline_to") in sheets}

    count = len(info["frames"])
    frames = [[] for _ in layers]                       # frames[layer][i]
    for i in range(count):
        for li, layer in enumerate(layers):
            path = os.path.join(renders, f"{i:04d}_{li:02d}.png")
            if os.path.exists(path):
                frames[li].append(shrink(Image.open(path).convert("RGBA"), (w, h), float(layer.get("cover", 0.45))))
            else:
                frames[li].append(Image.new("RGBA", (w, h)))
    for li, layer in enumerate(layers):
        role = roles[li]
        if role in GREYS:
            stretch = levels(frames[li], GREYS[role]) if layer.get("levels", True) else None
            frames[li] = [to_greys(img, GREYS[role], float(layer.get("gain", 1.0)), stretch) for img in frames[li]]
        elif role in ("plain", "base"):
            frames[li] = palette_snap(frames[li], int(layer.get("colors", 12)))
    for i in range(count):
        outline([frames[li][i] for li in range(len(layers))], roles, base, partners)

    tags = {t: (v["from"], v["to"], v["loop"], v["event"]) for t, v in info["tags"].items()}
    for alias in ("idle", "walk", "work"):              # the game's plain tags alias the front view
        if alias not in tags and alias + "_down" in tags:
            tags[alias] = tags[alias + "_down"]
    ms = [fr["ms"] for fr in info["frames"]]
    out = args.out or os.path.join(REPO, "data", "textures")
    written = []
    for li, sheet in enumerate(sheets):
        if li != base and not any(img.getbbox() for img in frames[li]):
            # Nothing modelled for it (a garment this character doesn't have yet): no sheet, so the
            # game leaves the garment off rather than drawing nothing; an older render's goes too.
            for ext in (".png", ".anim.json"):
                stale = os.path.join(out, sheet + ext)
                if os.path.exists(stale):
                    os.remove(stale)
            print(f"  {sheet}: empty, no sheet")
            continue
        write_sheet(sheet, [(img, ms[i]) for i, img in enumerate(frames[li])], tags, info["pivot"],
                    trim=li != base, force=True, root=out)
        written.append(sheet)
    print(f"wrote {len(written)} sheets ({count} frames, {len(tags)} tags) to {os.path.relpath(out, REPO)}")

    if args.aseprite:
        import make_look_aseprite as M
        tints = cfg.get("tints", {})
        look = [(sheet, M.hexrgb(int(tints[sheet], 16)) if sheet in tints else None) for sheet in written]
        dest = os.path.join(REPO, "art", "creatures", cfg.get("model", name) + ".aseprite")
        M.TEX = out
        M.build(look, dest)
        print(f"wrote {os.path.relpath(dest, REPO)}")


if __name__ == "__main__":
    main()
