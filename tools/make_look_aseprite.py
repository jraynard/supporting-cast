#!/usr/bin/env python3
"""Gather a character's layered look into one Aseprite file to paint over by hand:

    python3 tools/make_look_aseprite.py            # art/creatures/wanderer.aseprite (the Wanderer preset)

One layer per sheet the game draws them from, bottom to top in the game's order
(Rendering/HumanLook.Layers), each named after its sheet ("apparel/scarf_dye_male_short"), with
every frame, every tag and the pivot slice. tools/export_art.py exports each sheet-named layer back
to its own sheet, and the placeholder generator leaves sheets a file in art/ provides alone.

Layers the game tints (skin, hair, eyes, dyed cloth) stay greyscale: paint them in the greys from
the palette (white = the colour itself, darker = shaded). A hidden "ref composite" layer at the
top shows the character in colour, as the game draws them; it isn't exported.

To refresh a file nobody has painted yet from newer generated art, regenerate its sheets first
(the generator otherwise leaves them alone): DW_REGEN_HAND_DRAWN=1 python3 tools/gen_placeholder_art.py
"""
import json
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
import aseprite_file  # noqa: E402
import norse_people as P  # noqa: E402

ROOT = os.path.join(os.path.dirname(__file__), "..")
TEX = os.path.join(ROOT, "data", "textures")


def hexrgb(h):
    return ((h >> 16) & 255, (h >> 8) & 255, h & 255)


def lift(rgb, k):
    return tuple(min(255, int(c * k + 0.5)) for c in rgb)


def dye(c):
    return lift(tuple(round(v * 255) for v in c), 1.275)    # HumanLook.Dye


# The Wanderer preset (data/defs/outfits.json): male, Swept hair, round eyes; in draw order, each
# sheet with the tint the game multiplies it by (None: drawn as is).
SKIN, HAIR, EYES = hexrgb(0xE8BE96), lift(hexrgb(0x3A56B0), 1.3), hexrgb(0x4AA86A)
IRONBLACK, HEATHER, ROSE = dye((0.227, 0.212, 0.251)), dye((0.604, 0.525, 0.722)), dye((0.722, 0.384, 0.565))
WANDERER = [
    ("creatures/person_male", None),
    ("creatures/person_hair_swept_back", HAIR),
    ("creatures/person_male_skin", SKIN),
    ("creatures/person_male_eyes_round", EYES),
    ("creatures/person_male_face_round", None),
    ("apparel/trousers_male", None),
    ("apparel/trousers_dye_male", IRONBLACK),
    ("apparel/turnshoes_male", None),
    ("apparel/vest_male", None),
    ("apparel/mantle_male", None),
    ("apparel/mantle_dye_male", HEATHER),
    ("apparel/scarf_male", None),
    ("apparel/scarf_dye_male", ROSE),
    ("creatures/person_hair_swept", HAIR),
]

ALIASES = ("idle", "walk", "work")   # the exporter adds these back as aliases of the _down tags


def load(name):
    """A sheet as full-size frames, and its manifest."""
    with open(os.path.join(TEX, name + ".anim.json")) as f:
        m = json.load(f)
    img = Image.open(os.path.join(TEX, name + ".png")).convert("RGBA")
    w, h = m["frameSize"]
    frames = []
    for fr in m["frames"]:
        full = Image.new("RGBA", (w, h))
        full.paste(img.crop((fr["x"], fr["y"], fr["x"] + fr["w"], fr["y"] + fr["h"])), (fr["ox"], fr["oy"]))
        frames.append(full)
    return frames, m


def tinted(img, rgb):
    if rgb is None:
        return img
    r, g, b, a = img.split()
    k = [c / 255 for c in rgb]
    return Image.merge("RGBA", (r.point(lambda v: int(v * k[0])), g.point(lambda v: int(v * k[1])), b.point(lambda v: int(v * k[2])), a))


def palette():
    """The tint greys (skin, hair, eyes, dye), then the untinted clothing ramps, for painting."""
    greys = sorted({*P.SKIN_GREYS, *P.HAIR_GREYS, *P.EYE_GREYS, *P.DYE_GREYS})
    colours = [(g, g, g, 255) for g in greys]
    for ramp_ in (P.LINEN, P.BRAIES, P.LEATHER, P.DARK_LEATHER, P.GLOVE, P.WRAP, P.SHOE, P.FUR, P.GOLD, P.BRONZE, P.IRON):
        colours += [tuple(c[:3]) + (255,) for c in ramp_]
    colours += [P.WHITE, P.LASH, P.LID, P.BLUSH]
    seen, out = set(), []
    for c in colours:
        if c not in seen:
            seen.add(c)
            out.append(c)
    return out[:256]


def build(layers, out_path):
    base_frames, manifest = load(layers[0][0])
    w, h = manifest["frameSize"]
    sprite = aseprite_file.Sprite(w, h)
    sprite.durations = [fr["ms"] for fr in manifest["frames"]]
    composite = [Image.new("RGBA", (w, h)) for _ in base_frames]
    for name, tint in layers:
        frames, m = load(name) if name != layers[0][0] else (base_frames, manifest)
        if len(frames) != len(base_frames) or m["frameSize"] != manifest["frameSize"]:
            sys.exit(f"{name}: its frames don't match {layers[0][0]}'s")
        layer = sprite.layer(name)
        for i, img in enumerate(frames):
            cel = aseprite_file.crop_rgba(w, h, img.tobytes())
            if cel:
                layer.cels[i] = cel
            composite[i].alpha_composite(tinted(img, tint))
    ref = sprite.layer("ref composite (not exported)", visible=False)
    for i, img in enumerate(composite):
        cel = aseprite_file.crop_rgba(w, h, img.tobytes())
        if cel:
            ref.cels[i] = cel
    for tag, t in manifest["tags"].items():
        if tag in ALIASES and manifest["tags"].get(tag + "_down") == t:
            continue
        name = tag if t["loop"] else tag + "!"
        sprite.tags.append((name, t["from"], t["to"], f"event={t['event']}" if t["event"] >= 0 else ""))
    px, py = manifest["pivot"]
    sprite.slices.append(("pivot", int(px), int(py), 1, 1, (0, 0)))
    sprite.palette = palette()
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    aseprite_file.write(sprite, out_path)
    return sprite, composite


def main():
    out = os.path.join(ROOT, "art", "creatures", "wanderer.aseprite")
    sprite, _ = build(WANDERER, out)
    print(f"wrote {os.path.relpath(out, ROOT)}: {len(sprite.layers)} layers, {len(sprite.durations)} frames, "
          f"{len(sprite.tags)} tags, {sum(len(l.cels) for l in sprite.layers)} cels")


if __name__ == "__main__":
    main()
