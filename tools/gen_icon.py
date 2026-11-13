#!/usr/bin/env python3
"""The game's icons, from the brand (art/brand/, made by tools/brand_logo.py).

Large sizes are the full emblem (the signboard, tools, tankard and quest marker in a runed iron
rim); 48 px and under use the brand's small-size sign (favicon.svg: no runes or tools), which stays
legible in a taskbar. Rasterised with rsvg-convert (librsvg), packed with Pillow.

Writes data/icon.png (256, the window icon), packaging/icon.ico (Windows exe), packaging/icon.icns
(the Mac bundle), packaging/icon-<n>.png (Linux desktop entry sizes) and packaging/icon-512.png
(the Discord server and itch.io page), and data/textures/ui/logo.png (the stacked logo, for the
title screen; drawn at up to half its pixel height, so it stays crisp on a larger UI scale).

    python3 tools/gen_icon.py
"""
import io
import os
import shutil
import subprocess
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BRAND = os.path.join(ROOT, "art", "brand")
SMALL_MAX = 48  # this size and under use the small-size sign


def render(svg, size, height=None):
    """An SVG at size x size, or at a height with its own aspect when height is given."""
    dims = ["-h", str(height)] if height else ["-w", str(size), "-h", str(size)]
    png = subprocess.run(["rsvg-convert", *dims, os.path.join(BRAND, svg)], check=True, capture_output=True).stdout
    return Image.open(io.BytesIO(png)).convert("RGBA")


def icon(size):
    return render("favicon.svg" if size <= SMALL_MAX else "emblem.svg", size)


def main():
    if not shutil.which("rsvg-convert"):
        sys.exit("rsvg-convert not found: install librsvg (e.g. pacman -S librsvg, apt install librsvg2-bin)")
    pkg = os.path.join(ROOT, "packaging")
    os.makedirs(pkg, exist_ok=True)
    icon(256).save(os.path.join(ROOT, "data", "icon.png"))
    for size in (32, 48, 64, 128, 256, 512):
        icon(size).save(os.path.join(pkg, f"icon-{size}.png"))
    ico_sizes = (256, 128, 64, 48, 32, 16)
    first, *rest = (icon(s) for s in ico_sizes)
    first.save(os.path.join(pkg, "icon.ico"), sizes=[(s, s) for s in ico_sizes], append_images=rest)
    icon(1024).save(os.path.join(pkg, "icon.icns"))  # macOS bundle
    logo = render("logo-stacked.svg", 0, height=600)
    logo.crop(logo.getbbox()).save(os.path.join(ROOT, "data", "textures", "ui", "logo.png"))  # flush with the menu
    print("wrote data/icon.png, data/textures/ui/logo.png and packaging/icon*")


if __name__ == "__main__":
    main()
