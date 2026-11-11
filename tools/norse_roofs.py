"""Roof tops for the Norse art pass (32x32, seamless): turf (sod laid over birch bark: the roof the game
draws over every enclosed room), thatch and split shingles. Ordered patterns, no random noise, so the
roof doesn't shimmer as the camera moves. Authored in the pre-grade space and saved through the Nordic
grade (``placeholder_nature.save``) like the ground they sit above.

Runs after placeholder_roofs in gen_placeholder_art.py and replaces its drawings; also runs alone:
``python3 tools/norse_roofs.py``.
"""
from PIL import Image

from norse_art import ramp, bayer
from norse_ground import grass_tile

S = 32

# Per-wood art, so the material tint never colours straw or sod (docs/ART.md "Materials").
WOOD_SUFFIXES = ("", "_oak", "_pine", "_birch")


def turf(seed=410):
    """Meadow grass grown over laid sods: the grass tile, with the sods' seams showing as darker rows
    and a few sun-bleached tufts on the ridges between them."""
    from placeholder_nature import GRASS
    base = tuple(int(c * k) for c, k in zip(GRASS, (0.94, 0.98, 0.9)))
    img = grass_tile(seed, base, blades=60)
    px = img.load()
    g = ramp(base, 6, main=2, dark=0.5, light=1.5, cool=16, warm=24)
    for y in range(S):
        for x in range(S):
            course = y // 8
            seam = (y % 8 == 7) and ((x + course * 5) % 16 != 0)
            butt = (x + (8 if course % 2 else 0)) % 16 == 0 and y % 8 > 1
            if (seam and (x + course) % 2 == 0) or (butt and y % 2 == 0):
                px[x, y] = g[1]                                  # seams half hidden by the grass
            elif y % 8 == 0 and (x * 5 + course * 3) % 7 == 0:
                px[x, y] = g[5]
    return img


def thatch():
    """Courses of bundled straw, each lit at its top and shadowed where the next one overlaps it,
    the stalks in a fixed comb pattern."""
    straw = ramp((180, 146, 82), 6, main=3, dark=0.5, light=1.3, cool=18, warm=10)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        course, within = divmod(y, 8)
        for x in range(S):
            v = 3.6 - within * 0.42                              # lit top, falling to the overlap
            if within == 7:
                v = 0.4
            if (x + course * 3) % 3 == 0:                         # stalk grooves
                v -= 0.9
            if (x * 7 + y * 3 + course) % 11 == 0 and within < 6:  # a stray light stalk
                v += 1.2
            v += bayer(x, y) * 0.8
            px[x, y] = straw[max(0, min(len(straw) - 1, int(v)))]
    return img


WOODS = {"": (140, 100, 60), "_oak": (146, 100, 60), "_pine": (206, 162, 102), "_birch": (226, 208, 172)}


def shingles(base):
    """Split shingles in offset courses: rounded butt ends, a split-grain line down each, the next
    course's shadow along the top."""
    w = ramp(base, 6, main=3, dark=0.45, light=1.25, cool=22, warm=10)
    img = Image.new("RGBA", (S, S))
    px = img.load()
    for y in range(S):
        course, within = divmod(y, 8)
        offset = 4 if course % 2 else 0
        for x in range(S):
            u = (x + offset) % 8
            v = 3.4 - within * 0.25
            if within == 0:
                v = 1.0                                           # under the course above
            if u == 0:
                v = 0.6                                           # the gap between shingles
            elif within == 7 and u in (1, 7):
                v = 1.2                                           # rounded butt corners
            if (u == 4 and 1 < within < 7) or (u == 2 and (x + course) % 5 == 0 and within > 2):
                v -= 0.8                                          # split grain
            v += bayer(x, y) * 0.6
            px[x, y] = w[max(0, min(len(w) - 1, int(v)))]
    return img


def generate():
    from placeholder_nature import save
    t, sod = thatch(), turf()
    for suffix in WOOD_SUFFIXES:
        save(t, "buildings/roof_thatch" + suffix)
        save(sod, "buildings/roof_turf" + suffix)
    for suffix, base in WOODS.items():
        save(shingles(base), "buildings/roof_shingles" + suffix)


if __name__ == "__main__":
    generate()
