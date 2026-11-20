#!/usr/bin/env python3
"""The Steam capsules (art/steam/): a key-art shot and the brand's logo, cropped and laid out at each size
Steam asks for, plus a contact sheet that judges candidates side by side at real size.

Made in-house for Next Fest (docs/NEXT-FEST.md, "Capsule art"); an artist's capsule replaces it before Early
Access. The brief (MARKETING.md, STEAM-PAGE.md): the brand's colours, a busy common room or square at dusk,
the logo, no heroes in front, one ember thing. Above all it has to read at the small capsule's size, which a
store list shows at half (231x87): the contact sheet shows that, and a blow-up of it.

Key art comes from the game, clean and large. A first pass from the screenshot flags (or, better, Shift+F12
in camera mode during a real round, which catches a lived-in town):

    dotnet run --project src/HeroesNotIncluded -- --new-game --mute --size 2560x1440 --skip-hours 10 \\
        --look-at 54,44 --zoom 100 --screenshot keyart-inn.png --screenshot-at 15 --screenshot-no-ui --quit
    (the festival square, empty: --skip-hours 12 --festival-at 5 --no-people --lit-windows
    --look-at 50,48 --zoom 70 --screenshot-at 20;
    add --yaw/--pitch DEG and --camera-mode to frame)

    python3 tools/capsules.py keyart-inn.png keyart-square.png@0.4,0.6 --demo

Each key art is PATH or PATH@X,Y, the point (fractions of its width and height) the crops keep in frame.
Its name is the file's stem without "keyart-" (or --name, for one). Writes art/steam/<name>/: header.png,
small.png, main.png and library-logo.png (with --demo, *-demo.png beside them, marked in ember), and
art/steam/contact.png (not kept in git). The rest of Steam's set comes with it: vertical.png, library.png,
library-hero.png and page-background.png. A key art's own layout (LAYOUTS, by its name) moves the logo and
the crop per size, to keep the logo off what the shot is of.

Needs Pillow, and rsvg-convert (or Edge/Chrome) for the logo, as tools/brand_logo.py does.
"""
from __future__ import annotations

import argparse
import sys
import tempfile
from dataclasses import dataclass, replace
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

sys.path.insert(0, str(Path(__file__).resolve().parent))
from brand_logo import P, export_png  # noqa: E402  (the palette, and the SVG rasteriser)

ROOT = Path(__file__).resolve().parent.parent
BRAND = ROOT / "art" / "brand"
FONT = ROOT / "data" / "fonts" / "Cinzel-SemiBold.ttf"


def rgb(name: str) -> tuple[int, int, int]:
    h = P[name]
    return int(h[1:3], 16), int(h[3:5], 16), int(h[5:7], 16)


@dataclass(frozen=True)
class Size:
    """One of Steam's images. `logo` is "horizontal", "stacked" or None (Steam wants no text on the library
    hero or the page background); the logo is centred at (ax, ay), fractions of the image, `share` of its height.
    The crop keeps (fx, fy) of the key art in frame (None: the key art's own focus), `zoom` times tighter than
    the least that covers. `dim` softens and darkens it, for the page background Steam lays its page over."""
    name: str
    w: int
    h: int
    logo: str | None
    ax: float = 0.5
    ay: float = 0.5
    share: float = 0.6
    fx: float | None = None
    fy: float | None = None
    zoom: float = 1.0
    dim: bool = False


# Steam's sizes (STEAM-PAGE.md, "Capsules and media"; check Steamworks' current list when uploading).
SIZES = (
    Size("header", 920, 430, "horizontal", 0.5, 0.54, 0.62),
    Size("small", 462, 174, "horizontal", 0.5, 0.5, 0.9),      # the logo nearly fills it: nothing else reads here
    Size("main", 1232, 706, "horizontal", 0.5, 0.6, 0.44),
    Size("vertical", 748, 896, "stacked", 0.5, 0.4, 0.56),
    Size("library", 600, 900, "stacked", 0.5, 0.36, 0.5),
    Size("library-hero", 3840, 1240, None),       # Steam lays the library logo over it: no text of our own
    Size("page-background", 1438, 810, None, dim=True),
)

# A key art's own layout, by set name: what each size changes so the logo keeps off what the shot is of.
LAYOUTS: dict[str, dict[str, dict]] = {
    # The festival square at 20:00 (3840x1440): the bonfire at (0.45, 0.66), the inn's lit front across the top
    # from 0.43 to 0.77. The crops shift right to put the bonfire on the left; the logo sits on the cottages'
    # roofs bottom right.
    "square": {
        "header": dict(fx=0.6, ax=0.7, ay=0.72, share=0.46),
        "main": dict(fx=0.58, ax=0.7, ay=0.74, share=0.36),
        "vertical": dict(fx=0.55, ax=0.6, ay=0.78, share=0.42),
        "library": dict(fx=0.53, ax=0.6, ay=0.8, share=0.4),
    },
}
LIBRARY_LOGO_W = 1280  # transparent, 1280 wide (or 720 tall)


def smooth(t: float) -> float:
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def cover(img: Image.Image, w: int, h: int, fx: float, fy: float, zoom: float = 1.0) -> Image.Image:
    """Scale to cover w x h (`zoom` times more), then crop around the focus point, as near it as the edges allow."""
    s = max(w / img.width, h / img.height) * zoom
    sw, sh = round(img.width * s), round(img.height * s)
    big = img.resize((sw, sh), Image.LANCZOS)
    x = min(max(round(fx * sw - w / 2), 0), sw - w)
    y = min(max(round(fy * sh - h / 2), 0), sh - h)
    return big.crop((x, y, x + w, y + h))


def logos(height: int) -> dict[str, Image.Image]:
    """Both lockups rasterised once, large, for downscaling to each size."""
    out = {}
    with tempfile.TemporaryDirectory() as td:
        for kind, (vw, vh) in (("horizontal", (1280, 520)), ("stacked", (880, 1030))):
            png = Path(td) / f"{kind}.png"
            w = round(height * vw / vh)
            if not export_png(BRAND / f"logo-{kind}.svg", png, w, height):
                sys.exit(f"couldn't rasterise logo-{kind}.svg: install rsvg-convert (librsvg) or Chrome")
            out[kind] = Image.open(png).convert("RGBA")
            out[kind].load()
    return out


def fit(logo: Image.Image, h: int) -> Image.Image:
    return logo.resize((round(logo.width * h / logo.height), h), Image.LANCZOS)


def compose(art: Image.Image, focus: tuple[float, float], size: Size, lockups: dict[str, Image.Image]) -> Image.Image:
    img = cover(art, size.w, size.h, focus[0] if size.fx is None else size.fx, focus[1] if size.fy is None else size.fy, size.zoom)
    if size.dim:
        # Softened and sunk into the oak, so the page reads over it; the edges fade to the cellar.
        img = img.filter(ImageFilter.GaussianBlur(2.5))
        img.paste(rgb("oak_deep"), (0, 0), Image.new("L", img.size, 110))
        vig = Image.new("L", (64, 64))
        vig.putdata([int(220 * smooth((max(abs(x / 63 - 0.5), abs(y / 63 - 0.5)) - 0.3) / 0.2)) for y in range(64) for x in range(64)])
        img.paste(rgb("cellar"), (0, 0), vig.resize(img.size, Image.BICUBIC))
    if size.logo is None:
        return img
    logo = fit(lockups[size.logo], round(size.h * size.share))
    if logo.width > size.w * 0.94:  # a wide logo on a tall-ish image: fit the width instead
        logo = logo.resize((round(size.w * 0.94), round(logo.height * size.w * 0.94 / logo.width)), Image.LANCZOS)
    lx = round(size.ax * size.w - logo.width / 2)
    ly = round(size.ay * size.h - logo.height / 2)

    # A soft pool of cellar under the logo, so bone lettering reads over any shot; strongest at its centre.
    pool = Image.new("L", img.size, 0)
    pad_x, pad_y = logo.width * 0.12, logo.height * 0.18
    ImageDraw.Draw(pool).ellipse((lx - pad_x, ly - pad_y, lx + logo.width + pad_x, ly + logo.height + pad_y), fill=190)
    pool = pool.filter(ImageFilter.GaussianBlur(max(logo.height, 8) * 0.28))
    img.paste(rgb("cellar"), (0, 0), pool)
    # A light vignette at the edges, so the capsule sits in the store's dark list.
    vig = Image.new("L", (64, 64))
    vig.putdata([int(150 * smooth((max(abs(x / 63 - 0.5), abs(y / 63 - 0.5)) - 0.36) / 0.14)) for y in range(64) for x in range(64)])
    img.paste(rgb("cellar"), (0, 0), vig.resize(img.size, Image.BICUBIC))

    # The logo's drop shadow, then the logo.
    blur = max(2, logo.height // 40)
    shadow = logo.getchannel("A").point(lambda a: int(a * 0.65)).filter(ImageFilter.GaussianBlur(blur))
    img.paste((0, 0, 0), (lx + blur, ly + round(blur * 1.4)), shadow)
    img.paste(logo, (lx, ly), logo)
    return img


def demo_mark(img: Image.Image) -> Image.Image:
    """A "DEMO" tab in ember, cellar lettering, top right: the demo's set is the game's with this on it."""
    img = img.copy()
    # Sized as on the header (920x430): a tall capsule's tab is no bigger than a wide one's.
    fs = max(14, round(min(img.height, img.width * 430 / 920) * 0.085))
    font = ImageFont.truetype(str(FONT), fs)
    d = ImageDraw.Draw(img)
    l, t, r, b = d.textbbox((0, 0), "DEMO", font=font)
    px, py = round(fs * 0.55), round(fs * 0.3)
    w, h = r - l + 2 * px, b - t + 2 * py
    m = round(fs * 0.45)
    x0, y0 = img.width - w - m, m
    d.rounded_rectangle((x0 + 2, y0 + 3, x0 + w + 2, y0 + h + 3), radius=round(fs * 0.25), fill=rgb("cellar"))
    d.rounded_rectangle((x0, y0, x0 + w, y0 + h), radius=round(fs * 0.25), fill=rgb("ember"), outline=rgb("cellar"), width=max(1, fs // 12))
    d.text((x0 + px - l, y0 + py - t), "DEMO", font=font, fill=rgb("cellar"))
    return img


def sheet_rows(name: str, imgs: dict[str, Image.Image], library_logo: Image.Image) -> list[tuple[str, list[tuple[str, Image.Image]]]]:
    """A set's two rows on the contact sheet. The store's: header, small and main at 100%, then the small at a store
    list's half size, and that blown up 3x (pixels kept) to judge whether the title survives it. The library's and
    the page's: vertical, library, the page background, and the hero at half size with the library logo over it,
    as the Steam library shows them together."""
    rows = []
    if "small" in imgs:
        half = imgs["small"].resize((imgs["small"].width // 2, imgs["small"].height // 2), Image.LANCZOS)
        rows.append((name, [("header", imgs["header"]), ("small", imgs["small"]), ("main", imgs["main"]),
                            ("small at half", half), ("half, 3x", half.resize((half.width * 3, half.height * 3), Image.NEAREST))]))
    rest = [(k, imgs[k]) for k in ("vertical", "library", "page-background") if k in imgs]
    if "library-hero" in imgs:
        hero = imgs["library-hero"].resize((1920, 620), Image.LANCZOS)
        logo = fit(library_logo, round(hero.height * 0.42))
        hero.paste(logo, (round(hero.width * 0.04), hero.height - logo.height - round(hero.height * 0.08)), logo)
        rest.append(("library-hero at half, with the library logo", hero))
    if rest:
        rows.append((f"{name}: library and page", rest))
    return rows


def contact_sheet(rows: list[tuple[str, list[tuple[str, Image.Image]]]], path: Path) -> None:
    gap, label_h = 24, 44
    font = ImageFont.truetype(str(FONT), 26)
    small_font = ImageFont.truetype(str(FONT), 22)
    width = gap + max(sum(i.width + gap for _, i in c) for _, c in rows)
    row_h = [label_h + max(i.height for _, i in c) + 30 + gap for _, c in rows]
    sheet = Image.new("RGB", (width, gap + sum(row_h)), rgb("oak_deep"))
    d = ImageDraw.Draw(sheet)
    y = gap
    for (name, c), rh in zip(rows, row_h):
        d.text((gap, y), name, font=font, fill=rgb("bone"))
        x = gap
        for label, im in c:
            sheet.paste(im, (x, y + label_h))
            d.text((x, y + label_h + im.height + 4), f"{label} {im.width}x{im.height}", font=small_font, fill=rgb("parchment"))
            x += im.width + gap
        y += rh
    sheet.save(path, optimize=True)


def parse_art(spec: str) -> tuple[Path, tuple[float, float]]:
    path, _, focus = spec.partition("@")
    if not focus:
        return Path(path), (0.5, 0.5)
    fx, fy = (float(v) for v in focus.split(","))
    return Path(path), (fx, fy)


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("art", nargs="+", help="key art: PATH or PATH@X,Y (the focus, as fractions)")
    ap.add_argument("--name", help="the set's name, for a single key art (default: the file's stem)")
    ap.add_argument("--demo", action="store_true", help="also the demo's set, marked in ember")
    ap.add_argument("--out", type=Path, default=ROOT / "art" / "steam")
    a = ap.parse_args()
    if a.name and len(a.art) > 1:
        ap.error("--name is for a single key art")

    lockups = logos(1600)
    lib = fit(lockups["horizontal"], round(LIBRARY_LOGO_W * 520 / 1280))
    rows = []
    for spec in a.art:
        path, focus = parse_art(spec)
        name = a.name or path.stem.removeprefix("keyart-")
        art = Image.open(path).convert("RGB")
        if art.width < 1920:
            print(f"warning: {path.name} is {art.width}x{art.height}; Steam's largest wants more (a 16:9 --size as large as the monitor allows)")
        out = a.out / name
        out.mkdir(parents=True, exist_ok=True)
        made = {}
        for size in SIZES:
            size = replace(size, **LAYOUTS.get(name, {}).get(size.name, {}))
            img = compose(art, focus, size, lockups)
            assert img.size == (size.w, size.h), (size.name, img.size)
            img.save(out / f"{size.name}.png", optimize=True)
            made[size.name] = img
            if a.demo and size.logo:
                demo_mark(img).save(out / f"{size.name}-demo.png", optimize=True)
        rows += sheet_rows(name, made, lib)
        if a.demo:
            marked = {s.name: demo_mark(made[s.name]) for s in SIZES if s.logo}
            rows += sheet_rows(f"{name} (demo)", marked, lib)
        print("wrote", out)

    a.out.mkdir(parents=True, exist_ok=True)
    lib.save(a.out / "library-logo.png", optimize=True)
    contact_sheet(rows, a.out / "contact.png")
    print("wrote", a.out / "library-logo.png")
    print("wrote", a.out / "contact.png")


if __name__ == "__main__":
    main()
