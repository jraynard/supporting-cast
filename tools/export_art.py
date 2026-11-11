#!/usr/bin/env python3
"""Export Aseprite sources under art/ into data/textures (sheet PNG + .anim.json manifest).

    python3 tools/export_art.py                  # export everything that's stale
    python3 tools/export_art.py --all            # export everything
    python3 tools/export_art.py --check          # list stale exports, exit 1 if any (CI)
    python3 tools/export_art.py --watch --also-copy-to src/DragonWorld/bin/Debug/net10.0/data/textures
                                                 # re-export on save; the running game hot-reloads

art/creatures/dragon_adult.aseprite -> data/textures/creatures/dragon_adult.png + .anim.json.
A file whose layers are named after sheets ("apparel/scarf_male_short": a "/" in the name) exports
each of those layers to its own sheet instead, every frame at full size so all of them keep the same
frame layout (a person's look layers are matched by frame index); its other layers (references)
aren't exported. tools/make_look_aseprite.py makes such files from a character's generated look.
Conventions (see docs/ART.md): frame tags are animation names; a tag ending in "!" plays once;
slices named "pivot" and "mouth" (or any name) become the pivot and named points; a tag's user
data "event=N" marks frame N of the tag for gameplay effects. Needs the `aseprite` CLI on PATH
(or ASEPRITE=/path/to/aseprite). Only the standard library is used.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, os.path.dirname(__file__))
import aseprite_file  # noqa: E402

REPO = os.path.normpath(os.path.join(os.path.dirname(__file__), ".."))
ART = os.path.join(REPO, "art")
OUT = os.path.join(REPO, "data", "textures")


def sheet_layers(src):
    """The layers named after sheets, if the file is a layered look (see the module doc)."""
    try:
        return [n for n in aseprite_file.layer_names(src) if "/" in n]
    except (ValueError, OSError, KeyError, IndexError):
        return []   # a file our reader doesn't understand (groups, indexed colour): export it whole


def sources():
    """(source, sheet name, layer or None): a whole file, or one sheet-named layer of it."""
    for root, _, files in os.walk(ART):
        for f in sorted(files):
            if f.endswith((".aseprite", ".ase")):
                src = os.path.join(root, f)
                layers = sheet_layers(src)
                if layers:
                    for layer in layers:
                        yield src, layer, layer
                else:
                    yield src, os.path.splitext(os.path.relpath(src, ART))[0].replace(os.sep, "/"), None


def stale(src, name):
    png = os.path.join(OUT, name + ".png")
    manifest = os.path.join(OUT, name + ".anim.json")
    if not (os.path.exists(png) and os.path.exists(manifest)):
        return True
    return os.path.getmtime(src) > min(os.path.getmtime(png), os.path.getmtime(manifest))


def aseprite():
    exe = os.environ.get("ASEPRITE") or shutil.which("aseprite")
    if not exe:
        sys.exit("aseprite not found: install it or set ASEPRITE=/path/to/aseprite")
    return exe


def convert(data):
    """Aseprite --format json-array output -> our manifest."""
    frames = data["frames"]
    if not frames:
        raise ValueError("no frames")
    size = frames[0]["sourceSize"]
    out_frames = []
    for f in frames:
        r, s = f["frame"], f["spriteSourceSize"]
        out_frames.append({"x": r["x"], "y": r["y"], "w": r["w"], "h": r["h"], "ox": s["x"], "oy": s["y"], "ms": f["duration"]})

    meta = data.get("meta", {})
    tags = {}
    for t in meta.get("frameTags", []):
        name = t["name"]
        loop = not name.endswith("!")
        name = name.rstrip("!")
        event = -1
        for part in (t.get("data") or "").replace(",", " ").split():
            if part.startswith("event="):
                event = int(part[len("event="):])
        tags[name] = {"from": t["from"], "to": t["to"], "loop": loop, "event": event}
    for base in ("idle", "walk", "work"):   # four-way people: the plain tags alias the front view's
        if base not in tags and base + "_down" in tags:
            tags[base] = dict(tags[base + "_down"])

    pivot = [size["w"] / 2, size["h"] - 1]
    points = {}
    for sl in meta.get("slices", []):
        keys = sl.get("keys") or []
        if not keys:
            continue
        b = keys[0]["bounds"]
        p = keys[0].get("pivot")
        x = b["x"] + (p["x"] if p else b["w"] / 2)
        y = b["y"] + (p["y"] if p else b["h"] / 2)
        if sl["name"].lower() == "pivot":
            pivot = [x, y]
        else:
            points[sl["name"].lower()] = [x, y]

    return {"frameSize": [size["w"], size["h"]], "pivot": pivot, "points": points, "frames": out_frames, "tags": tags}


def export(src, name, copy_to, layer=None):
    png = os.path.join(OUT, name + ".png")
    os.makedirs(os.path.dirname(png), exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        data_path = os.path.join(tmp, "sheet.json")
        # One layer of a layered look: untrimmed, so an empty frame keeps its place in the layout.
        pick = ["--layer", layer] if layer else []
        trim = [] if layer else ["--trim"]
        subprocess.run(
            [aseprite(), "-b", *pick, src, "--sheet", png, "--data", data_path, "--format", "json-array",
             "--list-tags", "--list-slices", "--sheet-type", "packed", *trim, "--shape-padding", "1"],
            check=True,
        )
        with open(data_path) as f:
            manifest = convert(json.load(f))
    manifest_path = os.path.join(OUT, name + ".anim.json")
    # A fine sheet (the people: "density": 4) stays fine: keep the density the generator gave it.
    if os.path.exists(manifest_path):
        with open(manifest_path) as f:
            density = json.load(f).get("density")
        if density:
            manifest["density"] = density
    with open(manifest_path, "w") as f:
        json.dump(manifest, f, indent=1)
        f.write("\n")
    for dest in copy_to:
        target = os.path.join(dest, os.path.dirname(name))
        os.makedirs(target, exist_ok=True)
        shutil.copy2(png, target)
        shutil.copy2(manifest_path, target)
    tags = ", ".join(manifest["tags"]) or "no tags"
    print(f"exported {name}: {len(manifest['frames'])} frames ({tags})")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--all", action="store_true", help="export everything, not just stale files")
    ap.add_argument("--check", action="store_true", help="list stale exports and exit 1 if any")
    ap.add_argument("--watch", action="store_true", help="keep running and re-export on save")
    ap.add_argument("--also-copy-to", action="append", default=[], metavar="DIR",
                    help="also copy exports here (a build output's data/textures, for hot reload)")
    args = ap.parse_args()

    if args.check:
        bad = [name for src, name, _ in sources() if stale(src, name)]
        for name in bad:
            print(f"stale: {name}")
        sys.exit(1 if bad else 0)

    def run(force):
        n = 0
        for src, name, layer in sources():
            if force or stale(src, name):
                try:
                    export(src, name, args.also_copy_to, layer)
                    n += 1
                except (subprocess.CalledProcessError, ValueError, KeyError) as ex:
                    print(f"FAILED {name}: {ex}", file=sys.stderr)
        return n

    n = run(args.all)
    if not args.watch:
        print(f"{n} file(s) exported" if n else "everything up to date")
        return
    print(f"watching {os.path.relpath(ART, REPO)}/ (Ctrl+C to stop)")
    try:
        while True:
            time.sleep(0.5)
            run(False)
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
