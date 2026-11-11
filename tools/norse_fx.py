"""The white art the game tints, redrawn for the Norse pass: the selection brackets (knotwork corners),
the designation mark (a rune-cut X, like the gebo rune), the need bubble (a round shield with rivets)
and the chatter bubble (rune scratches instead of dots). They stay neutral (white and greys, the
bubble cream) because the game multiplies them by a colour. Small effects (heart, Zzz, glints) and
the soft smoke puff (also the contact shadow) keep their older drawings.

Runs after the older placeholder modules in gen_placeholder_art.py; also runs alone:
``python3 tools/norse_fx.py``.
"""
import os

from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")
WHITE = (255, 255, 255, 255)
LIGHT = (226, 226, 230, 255)
GREY = (176, 176, 184, 255)


def save(img, name):
    path = os.path.join(ROOT, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)


def put(img, x, y, c):
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((x, y), c)


def selection():
    """Corner brackets two pixels thick, each ending in a little interlaced loop (a knotwork corner)."""
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    corner = Image.new("RGBA", (12, 12), (0, 0, 0, 0))
    d = ImageDraw.Draw(corner)
    d.rectangle([1, 1, 9, 2], fill=WHITE)            # the arms
    d.rectangle([1, 1, 2, 9], fill=WHITE)
    for x, y in ((4, 4), (5, 4), (4, 5), (5, 5)):     # the loop's knot in the crook
        corner.putpixel((x, y), WHITE)
    for x, y in ((3, 4), (4, 3), (6, 5), (5, 6), (6, 4), (4, 6)):
        corner.putpixel((x, y), LIGHT)
    corner.putpixel((10, 1), GREY)                     # tapered ends
    corner.putpixel((1, 10), GREY)
    for x in range(2, 10):                             # a shaded under-edge, so it reads carved
        corner.putpixel((x, 3), GREY) if corner.getpixel((x, 3))[3] == 0 and x > 6 else None
    for y in range(2, 10):
        corner.putpixel((3, y), GREY) if corner.getpixel((3, y))[3] == 0 and y > 6 else None
    mirrored = corner.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    img.alpha_composite(corner, (0, 0))
    img.alpha_composite(mirrored, (20, 0))
    img.alpha_composite(corner.transpose(Image.Transpose.FLIP_TOP_BOTTOM), (0, 20))
    img.alpha_composite(mirrored.transpose(Image.Transpose.FLIP_TOP_BOTTOM), (20, 20))
    return img


def designate_x():
    """The gebo rune: two strokes cut with a chisel, thick in the middle, tapering, with notched ends."""
    img = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
    for t in range(4, 28):
        mid = 1 - abs(t - 15.5) / 12.5                 # 1 at the crossing, 0 at the ends
        w = 1 if mid < 0.25 else 2 if mid < 0.7 else 3
        for s in range(-(w // 2), w - w // 2):
            put(img, t + s, t, WHITE)
            put(img, 31 - t + s, t, WHITE)
        put(img, t + w - w // 2, t + 1, GREY)          # the cut's shadow side
        put(img, 31 - t - (w // 2) - 1, t + 1, GREY)
    for x, y in ((3, 3), (28, 3), (3, 28), (28, 28)):  # notched serifs
        put(img, x, y, LIGHT)
        put(img, x + (1 if x < 16 else -1), y, LIGHT)
    return img


def bubble():
    """A round shield on a short spike (the needs bubble, 24x30): cream boss, a ring, four rivets."""
    img = Image.new("RGBA", (24, 30), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ink = (52, 40, 44, 255)
    cream = (238, 232, 218, 255)
    shade = (214, 204, 186, 255)
    ring = (196, 182, 160, 255)
    d.ellipse([0, 0, 23, 23], fill=ink)
    d.ellipse([1, 1, 22, 22], fill=ring)
    d.ellipse([3, 3, 20, 20], fill=cream)
    d.pieslice([3, 3, 20, 20], 20, 160, fill=shade)  # the lower half a shade darker
    d.ellipse([4, 4, 19, 19], outline=None)
    d.polygon([(9, 22), (14, 22), (12, 29)], fill=ink)
    d.polygon([(10, 22), (13, 22), (12, 27)], fill=ring)
    for x, y in ((11, 1), (11, 21), (1, 11), (21, 11)):
        img.putpixel((x, y), (250, 246, 236, 255))
        img.putpixel((x + 1, y), (150, 136, 120, 255))
    return img


def speech():
    """A tiny chatter bubble (14x13) with rune scratches."""
    img = Image.new("RGBA", (14, 13), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ink = (40, 34, 38, 255)
    d.rounded_rectangle([0, 0, 13, 9], radius=3, fill=ink)
    d.rounded_rectangle([1, 1, 12, 8], radius=2, fill=WHITE)
    d.polygon([(2, 9), (5, 9), (2, 12)], fill=ink)
    img.putpixel((3, 9), WHITE)
    for x, y in ((3, 3), (3, 4), (3, 5), (4, 3)):      # ᚠ-ish
        img.putpixel((x, y), ink)
    img.putpixel((5, 4), ink)
    for x, y in ((7, 3), (7, 4), (7, 5), (8, 4), (9, 3), (9, 5)):   # a crossed stave
        img.putpixel((x, y), ink)
    for x, y in ((11, 3), (11, 4), (11, 5), (10, 3)):
        img.putpixel((x, y), ink)
    return img


def generate():
    save(selection(), "ui/selection")
    save(designate_x(), "ui/designate_x")
    save(bubble(), "ui/bubble")
    save(speech(), "fx/speech")


if __name__ == "__main__":
    generate()
