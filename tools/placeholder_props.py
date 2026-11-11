"""Face textures for box-model props (beds, tables, chests, log piles...) and toolbar icons for new furniture.

Each texture is a 32x32 tile; a box face shows the part of it matching the face's size (32 px per
cell), so draw them as if they covered a full cell. Wood textures are pale and neutral: the game
tints them with the material's colour (oak, pine, birch) when there's no per-wood variant.
Writes only the files listed here: python3 tools/gen_placeholder_art.py, or call generate().
"""
import os
import random
from PIL import Image, ImageDraw

from placeholder_nature import world_finish

S = 32
ROOT = os.path.join(os.path.dirname(__file__), "..", "data", "textures")


def save(img, name):
    path = os.path.join(ROOT, name + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    world_finish(img).save(path)


def tile(color):
    return Image.new("RGBA", (S, S), color)


def speckle(img, seed, amount, delta):
    rnd = random.Random(seed)
    px = img.load()
    for _ in range(amount):
        x, y = rnd.randrange(S), rnd.randrange(S)
        r, g, b, a = px[x, y]
        d = rnd.randint(-delta, delta)
        px[x, y] = (max(0, min(255, r + d)), max(0, min(255, g + d)), max(0, min(255, b + d)), a)
    return img


def planks():
    """Wooden boards running across, with seams and nail dots (the wood type's tint shifts the shade)."""
    img = tile((178, 128, 82, 255))
    d = ImageDraw.Draw(img)
    for i, y in enumerate(range(0, S, 8)):
        d.rectangle([0, y, S, y + 7], fill=(178, 128, 82, 255) if i % 2 == 0 else (164, 116, 74, 255))
        d.line([(0, y), (S, y)], fill=(204, 156, 106, 255))
        d.line([(0, y + 7), (S, y + 7)], fill=(98, 64, 40, 255))
        off = 6 + (i * 11) % 20
        d.line([(off, y + 1), (off, y + 6)], fill=(120, 82, 52, 255))
        d.point([(off + 2, y + 3), (off - 3, y + 3)], fill=(84, 60, 44, 255))
    return speckle(img, 3, 90, 8)


def log_side():
    img = tile((128, 90, 58, 255))
    d = ImageDraw.Draw(img)
    for y in range(0, S, 3):
        d.line([(0, y), (S, y + (y % 7) // 3)], fill=(102, 70, 44, 255))
    d.line([(0, 0), (S, 0)], fill=(158, 116, 76, 255))
    return speckle(img, 5, 80, 10)


def log_end():
    """Rings on a cut end."""
    img = tile((0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, S, S], fill=(150, 126, 96, 255))
    for r, c in ((15, (226, 204, 160, 255)), (11, (206, 182, 140, 255)), (7, (226, 204, 160, 255)), (3, (190, 164, 120, 255))):
        d.ellipse([16 - r, 16 - r, 16 + r, 16 + r], fill=c)
    return img


def linen(color, seed):
    img = tile(color)
    d = ImageDraw.Draw(img)
    for x in range(0, S, 4):
        d.line([(x, 0), (x, S)], fill=tuple(max(0, v - 10) for v in color[:3]) + (255,))
    return speckle(img, seed, 60, 6)


def blanket():
    """A woven blanket: red with a cream band and a darker hem."""
    img = linen((170, 64, 56, 255), 7)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 10, S, 14], fill=(226, 210, 176, 255))
    d.line([(0, 12), (S, 12)], fill=(150, 56, 50, 255))
    d.rectangle([0, 29, S, S], fill=(124, 44, 40, 255))
    return img


def stone_block():
    img = tile((150, 146, 140, 255))
    d = ImageDraw.Draw(img)
    for i, y in enumerate(range(0, S, 16)):
        d.line([(0, y + 15), (S, y + 15)], fill=(96, 92, 90, 255))
        off = 0 if i % 2 == 0 else 16
        d.line([(off + 15, y), (off + 15, y + 15)], fill=(96, 92, 90, 255))
        d.line([(off, y), (off + 14, y)], fill=(178, 174, 168, 255))
    return speckle(img, 9, 160, 12)


def straw():
    img = tile((196, 170, 96, 255))
    d = ImageDraw.Draw(img)
    rnd = random.Random(13)
    for _ in range(60):
        x, y = rnd.randrange(S), rnd.randrange(S)
        d.line([(x, y), (x + rnd.randint(-4, 4), y + rnd.randint(2, 5))], fill=rnd.choice([(226, 200, 120, 255), (160, 132, 70, 255)]))
    return img


def chest_front():
    img = planks()
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, S - 1, S - 1], outline=(90, 70, 50, 255))
    d.rectangle([0, 4, S, 6], fill=(110, 110, 118, 255))             # iron band under the lid
    d.rectangle([13, 6, 18, 13], fill=(200, 170, 70, 255))           # lock plate
    d.point([(15, 9), (16, 9), (15, 10)], fill=(60, 50, 30, 255))
    return img


def chest_top():
    img = planks()
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, S - 1, S - 1], outline=(90, 70, 50, 255))
    d.rectangle([4, 0, 6, S], fill=(110, 110, 118, 255))
    d.rectangle([25, 0, 27, S], fill=(110, 110, 118, 255))
    return img


def basket():
    img = tile((176, 140, 80, 255))
    d = ImageDraw.Draw(img)
    for y in range(0, S, 4):
        for x in range(0, S, 6):
            d.line([(x + (y // 4 % 2) * 3, y), (x + (y // 4 % 2) * 3 + 3, y + 3)], fill=(130, 100, 56, 255))
    d.rectangle([0, 0, S, 2], fill=(120, 92, 50, 255))
    return img


def heap(base, dots, seed):
    """A heap of round things (berries, fish) seen from above."""
    img = tile(base)
    d = ImageDraw.Draw(img)
    rnd = random.Random(seed)
    for _ in range(26):
        x, y, r = rnd.randrange(S), rnd.randrange(S), rnd.randint(2, 4)
        c = rnd.choice(dots)
        d.ellipse([x - r, y - r, x + r, y + r], fill=c)
        d.point([(x - 1, y - 1)], fill=(255, 255, 255, 200))
    return img


def meat_top():
    img = tile((150, 52, 48, 255))
    d = ImageDraw.Draw(img)
    rnd = random.Random(21)
    for _ in range(9):
        x, y = rnd.randrange(4, 28), rnd.randrange(4, 28)
        d.ellipse([x - 6, y - 4, x + 6, y + 4], fill=(186, 70, 64, 255), outline=(110, 36, 34, 255))
        d.arc([x - 4, y - 2, x + 4, y + 2], 0, 180, fill=(236, 210, 200, 255))
    return img


def icon(name, draw):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    draw(ImageDraw.Draw(img))
    save(img, name)


def icons():
    wood, dark = (170, 128, 84, 255), (96, 66, 40, 255)

    def table(d):
        d.rectangle([4, 10, 28, 15], fill=wood, outline=dark)
        d.rectangle([6, 15, 8, 27], fill=dark)
        d.rectangle([24, 15, 26, 27], fill=dark)

    def stool(d):
        d.ellipse([9, 10, 23, 16], fill=wood, outline=dark)
        d.rectangle([11, 15, 13, 27], fill=dark)
        d.rectangle([19, 15, 21, 27], fill=dark)

    def chest(d):
        d.rectangle([5, 10, 27, 26], fill=wood, outline=dark)
        d.rectangle([5, 14, 27, 15], fill=(110, 110, 118, 255))
        d.rectangle([14, 15, 18, 19], fill=(200, 170, 70, 255))

    icon("buildings/table", table)
    icon("buildings/stool", stool)
    icon("buildings/chest", chest)


def generate():
    save(planks(), "props/planks")
    save(log_side(), "props/log_side")
    save(log_end(), "props/log_end")
    save(linen((232, 226, 210, 255), 3), "props/linen")
    save(linen((246, 242, 232, 255), 4), "props/pillow")
    save(blanket(), "props/blanket")
    save(stone_block(), "props/stone_block")
    save(straw(), "props/straw")
    save(chest_front(), "props/chest_front")
    save(chest_top(), "props/chest_top")
    save(basket(), "props/basket")
    save(heap((90, 40, 60, 255), [(170, 40, 70, 255), (120, 30, 90, 255), (200, 60, 80, 255)], 17), "props/berries_top")
    save(heap((120, 140, 150, 255), [(150, 170, 180, 255), (110, 130, 146, 255), (176, 190, 196, 255)], 19), "props/fish_top")
    save(meat_top(), "props/meat_top")
    icons()


if __name__ == "__main__":
    generate()
