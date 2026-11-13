#!/usr/bin/env python3
"""The folk's builds: dwarves and halflings, made from the people's sheets (tools/chibi_people.py).

Every people layer is drawn on one frame layout, the head the same in every body, so a build is a
warp of the finished frames: the head drops by the build's ``drop``, the body below the neck is
squashed into what's left above the feet (which stay on the pivot), and a dwarf's body broadens
(``widen``, eased in below the neck so an arm or a tool crossing it doesn't tear). Applied the same
to every layer, they stay aligned:

  creatures/person_<body>[_skin|_eyes_*|_face_*]  ->  creatures/person_<body>_<build>[...]
  apparel/<worn>_<body>                           ->  apparel/<worn>_<body>_<build>
  creatures/person_hair_<style>[_back], person_beard_<style>  ->  ..._<build>  (moved down by the drop only)

An elf's skin and ears aren't built (no elf is a dwarf). The game picks a build's sheets by the
look's folk (Rendering/HumanLook) and maps hand positions with the same numbers (HumanLook.Builds).

    python3 tools/chibi_builds.py              # every build's sheets into data/textures
    python3 tools/chibi_builds.py --preview    # ...and .cache/people/builds.png
"""
import json
import os
import re
import sys
from multiprocessing import Pool

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from placeholder_sheets import write_sheet  # noqa: E402

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")

# Design units (the 40x60 frame). Keep in step with HumanLook.Builds.
NECK = 31.5          # above it, the head (moved down by the drop); below, the body (squashed)
FEET = 58.0          # the pivot: feet stay on it
EASE = 3.0           # how far below the neck the broadening eases in
BUILDS = {
    "dwarf": dict(drop=5.0, widen=1.24),
    "halfling": dict(drop=7.0, widen=1.0),
}
BODIES = ("male", "female")


def source_map(size, density, drop, widen):
    """For every pixel of a build's frame, the human frame's pixel it takes (nearest), or -1."""
    w, h = size
    k = density
    y = (np.arange(h) + 0.5) / k                                     # design units
    x = (np.arange(w) + 0.5) / k
    neck, feet = NECK + drop, FEET
    squash = (FEET - NECK) / (feet - neck)
    sy = np.where(y < drop, -1.0, np.where(y < neck, y - drop, np.where(y <= feet, NECK + (y - neck) * squash, y)))
    t = np.clip((y - neck) / EASE, 0.0, 1.0)
    grow = np.where(y < neck, 1.0, 1.0 + (widen - 1.0) * t * t * (3 - 2 * t))
    cx = w / k / 2
    sx = cx + (x[None, :] - cx) / grow[:, None]
    syi = np.floor(sy * k).astype(int)
    sxi = np.floor(sx * k).astype(int)
    valid = (syi[:, None] >= 0) & (syi[:, None] < h) & (sxi >= 0) & (sxi < w)
    return np.broadcast_to(syi[:, None], (h, w)), sxi, valid


def warp_sheet(job):
    """One sheet into one build: every frame rebuilt full size, warped, written trimmed."""
    src, dst, build, hair_only = job
    with open(os.path.join(ROOT, src + ".anim.json")) as f:
        man = json.load(f)
    sheet = Image.open(os.path.join(ROOT, src + ".png")).convert("RGBA")
    fw, fh = man["frameSize"]
    density = man.get("density", 1)
    b = BUILDS[build]
    syi, sxi, valid = source_map((fw, fh), density, b["drop"], 1.0 if hair_only else b["widen"])
    if hair_only:                                                    # hair and beards: only moved down
        dy = int(round(b["drop"] * density))
        syi = np.broadcast_to((np.arange(fh) - dy)[:, None], (fh, fw))
        sxi = np.broadcast_to(np.arange(fw)[None, :], (fh, fw))
        valid = (syi >= 0) & (syi < fh)
    frames = []
    for fr in man["frames"]:
        full = np.zeros((fh, fw, 4), np.uint8)
        part = np.asarray(sheet.crop((fr["x"], fr["y"], fr["x"] + fr["w"], fr["y"] + fr["h"])))
        full[fr["oy"]:fr["oy"] + fr["h"], fr["ox"]:fr["ox"] + fr["w"]] = part
        out = np.zeros_like(full)
        out[valid] = full[syi[valid], sxi[valid]]
        frames.append((Image.fromarray(out, "RGBA"), fr["ms"]))
    tags = {name: (t["from"], t["to"], t["loop"], t.get("event", -1)) for name, t in man["tags"].items()}
    pivot = tuple(man["pivot"])
    write_sheet(dst, frames, tags, pivot, man.get("points") or None, trim=True, force=True, density=density)
    return dst


def jobs():
    out = []
    names = []
    for folder in ("creatures", "apparel"):
        for fn in sorted(os.listdir(os.path.join(ROOT, folder))):
            if fn.endswith(".anim.json"):
                names.append(f"{folder}/{fn[:-len('.anim.json')]}")
    built = re.compile(r"_(" + "|".join(BUILDS) + r")(_|$)")
    for name in names:
        if built.search(name):
            continue                                                 # a build's own sheet
        for build in BUILDS:
            if m := re.fullmatch(r"creatures/person_(male|female)((?:_(?:skin|eyes_\w+|face_\w+))?)", name):
                out.append((name, f"creatures/person_{m.group(1)}_{build}{m.group(2)}", build, False))
            elif re.fullmatch(r"creatures/person_(hair|beard)_\w+", name):
                out.append((name, f"{name}_{build}", build, True))
            elif m := re.fullmatch(r"apparel/(\w+)_(male|female)", name):
                out.append((name, f"apparel/{m.group(1)}_{m.group(2)}_{build}", build, False))
    return out


def generate():
    todo = jobs()
    with Pool() as pool:
        done = pool.map(warp_sheet, todo, chunksize=4)
    print(f"builds: wrote {len(done)} sheets for {', '.join(BUILDS)}")


def preview(path):
    """Each body in each build in its underclothes, idle facing us, beside the human."""
    from norse_art import contact_sheet

    def frame(name, i=0):
        with open(os.path.join(ROOT, name + ".anim.json")) as f:
            man = json.load(f)
        sheet = Image.open(os.path.join(ROOT, name + ".png")).convert("RGBA")
        fr = man["frames"][i]
        img = Image.new("RGBA", tuple(man["frameSize"]), (0, 0, 0, 0))
        img.alpha_composite(sheet.crop((fr["x"], fr["y"], fr["x"] + fr["w"], fr["y"] + fr["h"])), (fr["ox"], fr["oy"]))
        return img

    imgs = []
    for body in BODIES:
        for build in ("",) + tuple("_" + b for b in BUILDS):
            bg = Image.new("RGBA", (160, 240), (96, 120, 80, 255))
            for layer in (f"creatures/person_{body}{build}_skin", f"creatures/person_{body}{build}",
                          f"apparel/tunic_{body}{build}", f"apparel/trousers_{body}{build}",
                          f"creatures/person_beard_full{build}" if body == "male" else f"creatures/person_hair_long{build}"):
                if os.path.exists(os.path.join(ROOT, layer + ".png")):
                    bg.alpha_composite(frame(layer))
            imgs.append(bg)
    contact_sheet(imgs, path, scale=1, columns=3, bg=(40, 40, 40, 255), pad=4)


if __name__ == "__main__":
    if "--preview" in sys.argv:
        out = os.path.join(os.path.dirname(__file__), "..", ".cache", "people", "builds.png")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        preview(out)
        print(out)
    else:
        generate()
