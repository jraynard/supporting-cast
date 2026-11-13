"""The itch.io page art (art/store/itch/): the banner (960x300, and @2x) and the page background
(1920x1080), composed from two in-game night shots and the brand's horizontal logo.

    dotnet run --project src/HeroesNotIncluded -- --new-game --mute --size 3200x1796 --skip-hours 12 \
        --look-at 54,44 --zoom 100 --screenshot inn.png --screenshot-at 4 --screenshot-no-ui --quit
    (again with --look-at 46,48 --zoom 70 --screenshot village.png)
    rsvg-convert -w 1400 art/brand/logo-horizontal.svg -o logo.png
    python3 tools/itch_art.py inn.png logo.png data/fonts/Cinzel-SemiBold.ttf art/store/itch village.png
"""
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

shot_path, logo_path, font_path, out_dir, wide_path = sys.argv[1:6]
CELLAR = (0x14, 0x0E, 0x0A)
OAK_DEEP = (0x26, 0x1B, 0x13)
BONE = (0xF2, 0xE6, 0xCF)
PARCHMENT = (0xD9, 0xC4, 0x9B)

shot = Image.open(shot_path).convert("RGB")


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def gradient_mask(size, fn):
    w, h = size
    m = Image.new("L", size)
    px = m.load()
    for y in range(h):
        for x in range(w):
            px[x, y] = int(255 * fn(x / w, y / h))
    return m


def overlay(img, color, mask):
    img.paste(Image.new("RGB", img.size, color), mask=mask)


# ---------------------------------------------------------------- the banner (960x300, made at 2x)
W, H = 1920, 600
banner = shot.crop((160, 240, 3200, 1190)).resize((W, H), Image.LANCZOS)
# Dark under the lettering on the left, clearing toward the lit inn; a little weight top and bottom.
overlay(banner, CELLAR, gradient_mask((W // 4, H // 4), lambda u, v: 0.9 * (1 - smooth((u - 0.2) / 0.42))).resize((W, H), Image.BICUBIC))
overlay(banner, CELLAR, gradient_mask((W // 4, H // 4), lambda u, v: 0.55 * max(smooth((v - 0.72) / 0.28), smooth((0.16 - v) / 0.16))).resize((W, H), Image.BICUBIC))

logo = Image.open(logo_path).convert("RGBA")
lh = 360
logo = logo.resize((round(logo.width * lh / logo.height), lh), Image.LANCZOS)
lx, ly = 50, 36
shadow = Image.new("RGBA", logo.size, (0, 0, 0, 0))
shadow.putalpha(logo.getchannel("A").point(lambda a: int(a * 0.6)))
banner.paste((0, 0, 0), (lx + 6, ly + 8), shadow.getchannel("A").filter(ImageFilter.GaussianBlur(10)))
banner.paste(logo, (lx, ly), logo)

font = ImageFont.truetype(font_path, 36)
d = ImageDraw.Draw(banner)
line = "Somebody has to keep the lights on."
tx, ty = lx + round(lh * 1.04), ly + round(lh * 0.86)  # under the ribbon, past the emblem
for dx, dy in ((3, 4), (0, 3)):
    d.text((tx + dx, ty + dy), line, font=font, fill=CELLAR)
d.text((tx, ty), line, font=font, fill=PARCHMENT)

banner.save(f"{out_dir}/banner@2x.png", optimize=True)
banner.resize((W // 2, H // 2), Image.LANCZOS).save(f"{out_dir}/banner.png", optimize=True)

# ---------------------------------------------------------------- the background (1920x1080)
BW, BH = 1920, 1080
wide = Image.open(wide_path).convert("RGB")
bg = wide.crop((0, 0, round(wide.height * 16 / 9), wide.height)).resize((BW, BH), Image.LANCZOS)
bg = bg.filter(ImageFilter.GaussianBlur(3))
overlay(bg, OAK_DEEP, Image.new("L", bg.size, 120))     # dimmed into the oak, so the page reads over it
# The middle, where itch lays the page, sinks further; the edges and the foot fade to the cellar.
overlay(bg, CELLAR, gradient_mask((BW // 8, BH // 8), lambda u, v: max(
    0.55 * (1 - smooth(abs(u - 0.5) / 0.28)),
    smooth((v - 0.6) / 0.4),
    0.7 * smooth((abs(u - 0.5) - 0.38) / 0.12),
)).resize((BW, BH), Image.BICUBIC))
bg.save(f"{out_dir}/background.png", optimize=True)
print("wrote banner.png, banner@2x.png, background.png")
