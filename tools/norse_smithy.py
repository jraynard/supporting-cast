"""The smithy's furniture: the anvil on its stump, the quench tub, the grindstone, the tool rack and the
fuel bin. Face textures for their box models (``props/``) and their toolbar icons (``buildings/``), in the
style and with the helpers of ``norse_buildings``. Run after it (``gen_placeholder_art.py``), or alone:
``python3 tools/norse_smithy.py``.
"""
import math
import random

from norse_art import ramp, mask_of, shade_shape, shade_over, selective_outline, bayer
from norse_buildings import (S, IRON, LEATHER, PALE, WOODS, canvas, put, clampi, save, board, nail,
                             prop_planks, graded)
from norse_stone import ROCKS, rock_ramp

WATER = ramp((54, 84, 98), 5, main=2, dark=0.5, light=1.6, cool=16, warm=2)
COAL = ramp((40, 40, 46), 5, main=1, dark=0.6, light=2.4, cool=10, warm=0)
GRIT = rock_ramp(graded(ROCKS["sandstone"]["base"]))
STEEL = ramp((120, 126, 140), 4, main=1, dark=0.55, light=1.5)


# ---------------------------------------------------------------- face textures

def prop_iron():
    """Wrought iron (the anvil's body): dark, hammer-polished on top, with pits and a faint scale."""
    img = canvas()
    px = img.load()
    rnd = random.Random(31)
    for y in range(S):
        for x in range(S):
            v = 1.6 + bayer(x, y) * 0.7 + math.sin((x + y * 0.3) * 0.5) * 0.25
            px[x, y] = IRON[clampi(v, 5)]
    for _ in range(14):                                   # pits and scale flecks
        x, y = rnd.randrange(S), rnd.randrange(S)
        put(img, x, y, IRON[0])
        if rnd.random() < 0.5:
            put(img, x + 1, y, IRON[3])
    for x in range(S):                                    # the polished face along the top edge
        put(img, x, 0, IRON[4]); put(img, x, 1, IRON[3])
    return img


def prop_water():
    """Still dark water in a tub, seen from above: slow ripples and a pale glint."""
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            r = math.hypot(x - 16, y - 16)
            v = 1.6 + bayer(x, y) * 0.5 + math.sin(r * 0.9) * 0.45
            px[x, y] = WATER[clampi(v, 5)]
    for x, y in ((9, 8), (10, 8), (11, 9), (22, 21), (23, 21)):
        put(img, x, y, WATER[4])
    return img


def prop_tub_side():
    """Staves of a quench tub, bound by two iron hoops."""
    img = canvas()
    rnd = random.Random(33)
    for i in range(4):
        board(img, i * 8, 0, i * 8 + 7, S - 1, PALE, rnd, False, tone=2.9 + (i % 2) * 0.3)
    for y0 in (3, 22):
        for x in range(S):
            put(img, x, y0, IRON[3]); put(img, x, y0 + 1, IRON[2]); put(img, x, y0 + 2, IRON[0])
        for x in range(4, S, 8):
            put(img, x, y0 + 1, IRON[4])
    return img


def prop_coal():
    """A heap of coal and charcoal lumps, seen from above: black with a cold blue sheen."""
    img = canvas(fill=COAL[0])
    rnd = random.Random(35)
    for _ in range(40):
        x, y, r = rnd.randrange(S), rnd.randrange(S), rnd.randint(2, 4)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if abs(dx) + abs(dy) * 1.2 <= r + 0.5:     # chunky, faceted lumps
                    u = (-dx * 0.55 - dy * 0.83) / r
                    put(img, (x + dx) % S, (y + dy) % S, COAL[3] if u > 0.5 else COAL[2] if u > -0.2 else COAL[1])
        put(img, (x - 1) % S, (y - 1) % S, COAL[4])
    return img


def prop_grindstone():
    """The wheel's face: coarse sandstone with a darker ring at the rim and an iron axle at the hub."""
    img = canvas()
    px = img.load()
    for y in range(S):
        for x in range(S):
            r = math.hypot(x - 15.5, y - 15.5)
            v = 3 + bayer(x, y) * 0.9 + (-1.4 if 13 <= r < 15 else 0) + (0.6 if r < 4 else 0)
            if (x * 7 + y * 13) % 11 == 0:
                v -= 0.8
            px[x, y] = GRIT[clampi(v, 7)]
    for dy in range(-2, 3):
        for dx in range(-2, 3):
            if dx * dx + dy * dy <= 5:
                put(img, 15 + dx, 15 + dy, IRON[2 if dx + dy < 0 else 1])
    return img


def prop_tool_rack():
    """A board hung with the smith's tools: tongs, two hammers, a file and a set hammer on pegs."""
    img = prop_planks()
    def peg(x, y):
        put(img, x, y, PALE.w[0]); put(img, x + 1, y, PALE.w[1])
    # Tongs: two long reins and short jaws.
    for x0, lean in ((3, 0), (6, 0)):
        for y in range(4, 26):
            put(img, x0 + lean, y, IRON[2] if x0 == 3 else IRON[1])
    for y in range(26, 30):
        put(img, 3 + (y - 26) // 2, y, IRON[3]); put(img, 6 - (y - 26) // 2, y, IRON[1])
    peg(4, 3)
    # Hammers: a handle hanging down and the head across the top.
    for hx, hw in ((11, 6), (19, 5)):
        for y in range(6, 24):
            put(img, hx + hw // 2, y, LEATHER[3] if y % 5 else LEATHER[1])
        m = mask_of(img.size, lambda d, hx=hx, hw=hw: d.rectangle([hx, 3, hx + hw, 6], fill=255))
        shade_shape(img, m, IRON[1:], radius=0.5, slope=1.5)
        peg(hx + hw // 2, 7)
    # A file, ridged.
    for y in range(5, 25):
        put(img, 27, y, STEEL[2] if y % 2 else STEEL[1]); put(img, 28, y, STEEL[1])
    for y in range(25, 29):
        put(img, 27, y, LEATHER[2]); put(img, 28, y, LEATHER[1])
    peg(27, 4)
    return img


# ---------------------------------------------------------------- toolbar icons

def icon_anvil():
    img = canvas()
    w = WOODS["_oak"]
    m = mask_of(img.size, lambda d: d.rectangle([10, 19, 22, 29], fill=255))          # the stump
    shade_shape(img, m, w.bark, radius=1.0, slope=1.3)
    for x in range(10, 23):
        put(img, x, 19, w.end[3]); put(img, x, 20, w.end[2])
    body = [(4, 10), (6, 8), (25, 8), (28, 10), (25, 13), (21, 13), (19, 17), (21, 19), (11, 19), (13, 17), (11, 13), (6, 13)]
    shade_over(img, mask_of(img.size, lambda d: d.polygon(body, fill=255)), IRON[1:], IRON[0], radius=0.8, slope=1.4)
    for x in range(7, 26):                                                              # the polished face
        put(img, x, 8, IRON[4])
    put(img, 22, 10, IRON[0]); put(img, 23, 10, IRON[0])                                # the hardy hole
    return selective_outline(img)


def icon_quench_tub():
    img = canvas()
    w = WOODS[""]
    rnd = random.Random(37)
    for i, x0 in enumerate(range(5, 27, 4)):
        board(img, x0, 13, x0 + 3, 29, w, rnd, False, tone=2.9 + (i % 2) * 0.3)
    for y0 in (15, 25):
        for x in range(5, 27):
            put(img, x, y0, IRON[3]); put(img, x, y0 + 1, IRON[1])
    m = mask_of(img.size, lambda d: d.ellipse([5, 9, 26, 17], fill=255))
    shade_over(img, m, WATER[1:], w.w[0], radius=1.0, slope=1.2)
    for x, y in ((11, 12), (12, 12), (19, 14)):
        put(img, x, y, WATER[4])
    for cx in (12, 17, 21):                                                             # a wisp of steam
        for k in range(5):
            put(img, cx + round(math.sin(k * 1.3 + cx) * 1.2), 7 - k, (226, 230, 236, 200 - k * 30))
    return selective_outline(img)


def icon_grindstone():
    img = canvas()
    w = WOODS[""]
    for a, z in (((6, 29), (11, 18)), ((26, 29), (21, 18)), ((8, 24), (24, 24))):          # the trestle
        m = mask_of(img.size, lambda d, a=a, z=z: d.line([a, z], fill=255, width=2))
        shade_shape(img, m, w.w[1:6], radius=0.6, slope=1.5)
    m = mask_of(img.size, lambda d: d.ellipse([6, 3, 26, 23], fill=255))
    shade_over(img, m, [GRIT[i] for i in (1, 2, 3, 4, 5)], GRIT[0], radius=1.6, slope=1.2)
    m = mask_of(img.size, lambda d: d.ellipse([8, 5, 24, 21], fill=255))
    for y in range(S):
        for x in range(S):
            if m.getpixel((x, y)) and not mask_of(img.size, lambda d: d.ellipse([9, 6, 23, 20], fill=255)).getpixel((x, y)):
                put(img, x, y, GRIT[1])
    m = mask_of(img.size, lambda d: d.line([(16, 13), (29, 13)], fill=255, width=2))       # axle and crank
    shade_shape(img, m, IRON[1:], radius=0.5, slope=1.5)
    m = mask_of(img.size, lambda d: d.line([(29, 13), (29, 18)], fill=255, width=2))
    shade_shape(img, m, LEATHER[1:], radius=0.5, slope=1.5)
    return selective_outline(img)


def icon_tool_rack():
    img = canvas()
    w = WOODS["_pine"]
    rnd = random.Random(39)
    board(img, 3, 4, 28, 9, w, rnd, True, tone=3.2)
    for x in (6, 25):
        nail(img, x, 6)
    for y in range(10, 27):                                                             # tongs
        put(img, 7, y, IRON[2]); put(img, 9, y, IRON[1])
    for x0, hw in ((13, 5), (20, 4)):                                                   # hammers
        for y in range(10, 25):
            put(img, x0 + hw // 2, y, LEATHER[3] if y % 4 else LEATHER[1])
        m = mask_of(img.size, lambda d, x0=x0, hw=hw: d.rectangle([x0, 10, x0 + hw, 13], fill=255))
        shade_shape(img, m, IRON[1:], radius=0.5, slope=1.5)
    for y in range(10, 26):                                                             # a file
        put(img, 26, y, STEEL[2] if y % 2 else STEEL[1])
    return selective_outline(img)


def icon_fuel_bin():
    img = canvas()
    w = WOODS["_oak"]
    rnd = random.Random(41)
    for y0 in (14, 20, 25):
        board(img, 4, y0, 27, y0 + 4, w, rnd, True, tone=3.0)
    m = mask_of(img.size, lambda d: d.chord([4, 6, 27, 20], 180, 360, fill=255))
    shade_over(img, m, COAL[1:], COAL[0], radius=1.0, slope=1.2)
    rnd = random.Random(43)
    for _ in range(12):
        x, y = rnd.randrange(7, 25), rnd.randrange(8, 13)
        put(img, x, y, COAL[4]); put(img, x + 1, y + 1, COAL[2])
    for x in (4, 27):
        for y in range(14, 30):
            put(img, x, y, IRON[2])
    return selective_outline(img)


def generate():
    save(prop_iron(), "props/iron")
    save(prop_water(), "props/water")
    save(prop_tub_side(), "props/tub_side")
    save(prop_coal(), "props/coal")
    save(prop_grindstone(), "props/grindstone")
    save(prop_tool_rack(), "props/tool_rack")
    for name, make in (("anvil", icon_anvil), ("quench_tub", icon_quench_tub), ("grindstone", icon_grindstone),
                       ("tool_rack", icon_tool_rack), ("fuel_bin", icon_fuel_bin)):
        save(make(), "buildings/" + name)


if __name__ == "__main__":
    generate()
