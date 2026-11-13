#!/usr/bin/env python3
"""Author the Millbrook map (data/defs/maps/millbrook.json) from a layout traced off
art/reference/village-map2.webp.

The reference is a 36x36-square battlemap; one square is two cells, and the village sits in the
middle of a 96x96 map with a 12-cell band of woods, fields and water round it (so a reference
square (sx, sy) is the cell (12 + 2*sx, 12 + 2*sy)). Everything below is in cells.

The output is the plain text-grid MapDef (see src/HeroesNotIncluded.Sim/Defs/MapDefs.cs): edit this
script and re-run it, or hand-edit the JSON for a small fix.

  python3 tools/village_map.py                 # write data/defs/maps/millbrook.json
  python3 tools/village_map.py --preview OUT   # also draw the map beside the reference, to compare
"""
import argparse
import json
import math
import os
import random

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "data", "defs", "maps", "millbrook.json")
REFERENCE = os.path.join(HERE, "..", "art", "reference", "village-map2.webp")

N = 96          # map size
CORE = 12       # where the reference's square (0, 0) lands
TOWN = 2        # the village's level; a river bank is one down, its bed two
rnd = random.Random(1234)

# Plan sizes (width across, depth front to back) of the preset buildings, as buildings.json.
PLANS = {"Inn": (13, 13), "Dwelling": (6, 5), "Cottage": (9, 6), "Smithy": (7, 5), "Shrine": (7, 5), "Woodshop": (7, 5), "TinkerShop": (7, 6), "Barn": (10, 6)}

GROUND_LEGEND = {"g": "Grass", "d": "Dirt", "r": "Road", "t": "TilledSoil", "k": "Stone", "s": "Sand", "~": "ShallowWater", "w": "Water"}
FEATURE_LEGEND = {
    "O": "Oak", "P": "Pine", "B": "Birch", "b": "BerryBush", "o": "Boulder", "x": "WildFlax",
    "f": "Fence", "G": "Gate!", "c": "CabbagePlant", "W": "Well", "N": "NoticeBoard", "=": "StoneBridge@2",
    "u": "Barrel", "X": "Crate", "C": "Wagon", "/": "Slope",
}

ground = [["g"] * N for _ in range(N)]
level = [[TOWN] * N for _ in range(N)]
feature = [["."] * N for _ in range(N)]
reserved = [[False] * N for _ in range(N)]   # building plots and their doorsteps: nothing grows there
buildings = []


def inside(x, y):
    return 0 <= x < N and 0 <= y < N


def cells_rect(x0, y0, x1, y1):
    for y in range(max(0, y0), min(N - 1, y1) + 1):
        for x in range(max(0, x0), min(N - 1, x1) + 1):
            yield x, y


def cells_disc(cx, cy, rx, ry, wobble=0.0, seed=0):
    r = random.Random(seed)
    phase = [r.uniform(0, math.tau) for _ in range(3)]
    for y in range(int(cy - ry - 2), int(cy + ry + 3)):
        for x in range(int(cx - rx - 2), int(cx + rx + 3)):
            if not inside(x, y):
                continue
            a = math.atan2(y - cy, x - cx)
            k = 1 + wobble * sum(math.sin(a * (i + 2) + p) for i, p in enumerate(phase)) / 3
            if ((x - cx) / (rx * k)) ** 2 + ((y - cy) / (ry * k)) ** 2 <= 1:
                yield x, y


def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = 0 if dx == dy == 0 else max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def cells_path(points, width):
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    for y in range(max(0, int(min(ys) - width - 1)), min(N, int(max(ys) + width + 2))):
        for x in range(max(0, int(min(xs) - width - 1)), min(N, int(max(xs) + width + 2))):
            d = min(seg_dist(x, y, *points[i], *points[i + 1]) for i in range(len(points) - 1))
            if d <= width / 2:
                yield x, y


def paint(cells, g=None, lv=None):
    for x, y in cells:
        if g == "r" and level[y][x] != TOWN:
            continue  # lanes stop at the banks: a bridge carries them over
        if g is not None:
            ground[y][x] = g
        if lv is not None:
            level[y][x] = lv


def plot(name, ax, ay, facing):
    """The cells a building's plot covers (the plan turned round its middle, as Footprint.Rotate)."""
    w, d = PLANS[name]
    lo_w, lo_d = -((w - 1) // 2), -((d - 1) // 2)
    out = []
    for row in range(d):
        for col in range(w):
            dx, dy = col + lo_w, row + lo_d
            rx, ry = {0: (dx, dy), 1: (-dy, dx), 2: (-dx, -dy), 3: (dy, -dx)}[facing]
            out.append((ax + rx, ay + ry))
    return out


def front(facing):
    return {0: (0, 1), 1: (-1, 0), 2: (0, -1), 3: (1, 0)}[facing]


def building(name, ax, ay, facing, role="", variant=""):
    cells = plot(name, ax, ay, facing)
    fx, fy = front(facing)
    for x, y in cells:
        reserved[y][x] = True
        if ground[y][x] in ("t",):
            ground[y][x] = "g"
    # A doorstep of packed earth all along the front, and a clear cell round the rest.
    for x, y in cells:
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if inside(nx, ny) and (nx, ny) not in cells:
                reserved[ny][nx] = True
        sx, sy = x + fx, y + fy
        if inside(sx, sy) and (sx, sy) not in cells and ground[sy][sx] == "g":
            ground[sy][sx] = "r"
    entry = {"def": name, "at": [ax, ay], "facing": facing}
    if variant:
        entry["variant"] = variant
    if role:
        entry["role"] = role
    buildings.append(entry)


def fence_ring(x0, y0, x1, y1, gate=None, locked=False):
    for x, y in cells_rect(x0, y0, x1, y1):
        if (x in (x0, x1) or y in (y0, y1)) and level[y][x] == TOWN:
            feature[y][x] = "f"
    if gate:
        feature[gate[1]][gate[0]] = "G" if locked else "."


def garden(x0, y0, x1, y1, gate):
    fence_ring(x0, y0, x1, y1, gate)
    for x, y in cells_rect(x0 + 1, y0 + 1, x1 - 1, y1 - 1):
        if level[y][x] != TOWN:
            continue
        ground[y][x] = "t"
        if (y - y0) % 2 == 1 and not reserved[y][x]:
            feature[y][x] = "c"


def water(cells, seed=0):
    """A river or lake: water two levels down, a one-cell stone bank one down round it."""
    cells = set(cells)
    for x, y in cells:
        ground[y][x] = "w"
        level[y][x] = TOWN - 2
        feature[y][x] = "."
    for x, y in cells:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                nx, ny = x + dx, y + dy
                if inside(nx, ny) and (nx, ny) not in cells and level[ny][nx] == TOWN:
                    ground[ny][nx] = "k"
                    level[ny][nx] = TOWN - 1
                    feature[ny][nx] = "."


def sq(sx, sy):
    """A reference square's cell."""
    return CORE + 2 * sx, CORE + 2 * sy


# ---------------- The land ----------------

# Water first: the river down the south-west under the bridge, and the lake to the north-east.
river = [(-2, 61), sq(2, 23.5), sq(5, 25.5), sq(7.5, 28.5), sq(9.5, 31), sq(11, 34), (36, 96 + 2)]
water(cells_path(river, 5))
water(list(cells_disc(*sq(35.5, 4), 9, 18, wobble=0.18, seed=3)) + list(cells_path([sq(33, -6), sq(34, 0), sq(36.5, 6), (98, 34)], 8)))

# The square and the lanes (packed earth).
paint(cells_disc(*sq(16, 17), 15, 17, wobble=0.12, seed=5), g="r")           # the square round the well
paint(cells_disc(*sq(12, 9), 9, 6, wobble=0.15, seed=7), g="r")               # up to the barns
paint(cells_path([sq(23.7, 12), sq(23.7, 4), sq(23.2, 0), (58, -2)], 4), g="r")    # the north road
paint(cells_path([sq(7, 6.5), sq(12, 6), sq(16, 6.5)], 3), g="r")             # past the pig pen
paint(cells_path([sq(22, 21), sq(27, 21), sq(31, 20.5), (97, 52)], 4), g="r")     # the east road
SOUTH_WEST_ROAD = [sq(12, 24), sq(9, 26.5), sq(6.5, 28.5), sq(3.5, 31.5), sq(1.5, 35), (12, 97)]
paint(cells_path(SOUTH_WEST_ROAD, 4), g="r")                                  # south-west, over the bridge
paint(cells_path([sq(5, 9), sq(1, 9.5), (-2, 30)], 3), g="r")                # out west past the barn
paint(cells_path([sq(22, 25), sq(25, 26), sq(27, 26)], 3), g="r")             # to the south-east houses

# The bridge carries the south-west road over the river: wherever the road meets the water or its banks.
for x, y in cells_path(SOUTH_WEST_ROAD, 4):
    if level[y][x] < TOWN:
        feature[y][x] = "="

# ---------------- Buildings (reference letters in comments) ----------------

building("Barn", 51, 18, 0, variant="Timber")                    # A: the big barn at the top
building("Cottage", 40, 31, 3, role="Home")                     # B: the house by the pig pen
building("Cottage", 50, 28, 0, role="Home")                     # C: top middle
building("Woodshop", 70, 28, 1, role="Woodshop")                 # D: by the lake, the cart outside
building("TinkerShop", 63, 67, 2, role="TinkerShop", variant="Timber")  # N: south of the square, between the east houses, its door to the north
building("Smithy", 20, 30, 3, role="Forge", variant="Stone")     # E: the west barn with the stone chimney
building("Cottage", 28, 41, 0, role="Home")                     # F: west house with the yard
building("Inn", 61, 45, 1, role="Tavern", variant="Timber")      # G: on the square, facing the well (its kitchen out the back)
building("Cottage", 74, 44, 1, role="Home", variant="Shingle")  # H: east, the big brown roof (moved over for the inn's kitchen)
building("Cottage", 30, 56, 0, role="Home")                     # I: south-west, with its garden
building("Cottage", 43, 61, 2, role="Home")                     # J: south of the square
building("Cottage", 69, 56, 1, role="Home")                     # K: east, by the cabbage field (moved off the tinker's shop)
building("Shrine", 55, 73, 2, role="Shrine")                     # L: south, in its garden
building("Cottage", 75, 70, 2, role="Home", variant="Shingle")  # M: the south-east corner

# ---------------- Yards, gardens, the pen ----------------

fence_ring(27, 21, 35, 30, gate=(31, 30), locked=True)            # the pig pen (B), gate onto the lane
paint(cells_rect(28, 22, 34, 29), g="d")
garden(14, 36, 21, 45, gate=(21, 40))                            # F's kitchen garden
garden(18, 46, 24, 53, gate=(24, 50))                            # I's
garden(73, 53, 80, 61, gate=(73, 57))                            # K's cabbage field
garden(45, 76, 51, 82, gate=(48, 76))                            # L's
garden(77, 46, 82, 51, gate=(77, 48))                            # H's

# ---------------- Things on the ground ----------------

feature[45][43] = "W"                                            # the well
for x, y in [(46, 43), (46, 44), (47, 43), (40, 43)]:             # the notice board, by the well
    if not reserved[y][x] and level[y][x] == TOWN and feature[y][x] == "." and ground[y][x] not in "wk~":
        feature[y][x] = "N"
        reserved[y][x] = True
        break
else:
    raise SystemExit("no room for the notice board by the well")
# Carts are 3 cells long and 2 wide (the Wagon def's size, facing 0: x-1..x+1, y..y+1): placed only
# where the whole of one fits on open town ground, and kept clear of trees and props.
CART = [(dx, dy) for dy in (0, 1) for dx in (-1, 0, 1)]
for x, y in [(63, 33), (35, 62)]:
    cells = [(x + dx, y + dy) for dx, dy in CART]
    if all(inside(cx, cy) and not reserved[cy][cx] and level[cy][cx] == TOWN and feature[cy][cx] == "." and ground[cy][cx] not in "wk~"
           for cx, cy in cells):
        feature[y][x] = "C"
        for cx, cy in cells:
            reserved[cy][cx] = True
for (x, y), f in {(65, 37): "u", (66, 37): "u", (53, 52): "X", (24, 36): "u", (25, 36): "X",
                  (47, 34): "X", (48, 34): "u", (64, 52): "u", (37, 51): "X", (79, 64): "u",
                  (40, 40): "o", (58, 36): "o", (52, 32): "o", (65, 20): "o", (38, 66): "o", (19, 66): "o"}.items():
    if not reserved[y][x] and level[y][x] == TOWN:
        feature[y][x] = f

# Ramps down to the river and lake banks where the lanes meet them, so the water can be fished.
for x, y in [(15, 60), (24, 62), (34, 80), (78, 38), (75, 30)]:
    if level[y][x] == TOWN - 1:
        feature[y][x] = "/"


def tree_clump(cx, cy, rx, ry, density, seed, species="O"):
    r = random.Random(seed)
    for x, y in cells_disc(cx, cy, rx, ry, wobble=0.25, seed=seed):
        if r.random() < density and ground[y][x] == "g" and level[y][x] == TOWN and not reserved[y][x] and feature[y][x] == ".":
            pick = r.random()
            feature[y][x] = species if pick < 0.75 else ("B" if pick < 0.9 else ("P" if species == "O" else "O"))


# The reference's oak clumps, then the woods round the village.
for i, (cx, cy, rx, ry) in enumerate([(18, 15, 8, 5), (30, 15, 9, 4), (73, 15, 6, 4), (15, 50, 4, 5), (41, 73, 4, 4),
                                      (68, 81, 6, 3), (81, 63, 3, 4), (79, 84, 5, 3), (60, 36, 2, 2), (12, 28, 3, 6)]):
    tree_clump(cx, cy, rx, ry, 0.5, 100 + i)

r = random.Random(77)
for y in range(N):
    for x in range(N):
        edge = min(x, y, N - 1 - x, N - 1 - y)
        if edge >= CORE + 2 or ground[y][x] != "g" or level[y][x] != TOWN or reserved[y][x] or feature[y][x] != ".":
            continue
        # The band round the village: thick woods at the map's edge thinning towards the houses,
        # pines to the north, oaks and birches south; a few bushes and flax in the clearings.
        k = (CORE + 2 - edge) / (CORE + 2)
        roll = r.random()
        if roll < 0.08 + 0.42 * k:
            north = y < N / 2
            pick = r.random()
            feature[y][x] = ("P" if pick < 0.6 else "B" if pick < 0.8 else "O") if north else ("O" if pick < 0.55 else "B" if pick < 0.8 else "P")
        elif roll > 0.985:
            feature[y][x] = "b"
        elif roll > 0.975:
            feature[y][x] = "x"

# Keep the lanes clear right to the edges, and the bank cells bare.
for y in range(N):
    for x in range(N):
        if ground[y][x] == "r" and feature[y][x] in "OPBbxo":
            feature[y][x] = "."

# The guaranteed resources a new round needs near the start (as the generated valley gives).
for x, y in [(23, 24), (24, 25), (22, 26)]:
    if feature[y][x] == "." and not reserved[y][x]:
        feature[y][x] = "b"
for x, y in [(34, 47), (35, 49), (33, 50)]:
    if feature[y][x] == "." and not reserved[y][x]:
        feature[y][x] = "x"

CENTER = [40, 48]
STOCKPILE = [50, 18, 5, 3]   # inside the barn
LIVESTOCK = [{"creature": "Pig", "count": 3, "at": [31, 25]}]


def check():
    """What MapBuilder would refuse, caught here: plots off the town's level or on water, things in the river."""
    problems = []
    for b in buildings:
        for x, y in plot(b["def"], b["at"][0], b["at"][1], b["facing"]):
            if not inside(x, y) or level[y][x] != TOWN or ground[y][x] in "wk~":
                problems.append(f"{b['def']} at {b['at']}: plot cell {x},{y} isn't level town ground")
            elif feature[y][x] in "fGWuXC":
                problems.append(f"{b['def']} at {b['at']}: {feature[y][x]} on its plot at {x},{y}")
    for y in range(N):
        for x in range(N):
            f = feature[y][x]
            if f not in ".=/" and (level[y][x] != TOWN or ground[y][x] in "wk~"):
                problems.append(f"'{f}' at {x},{y} is in the water or on a bank")
            if f == "=" and level[y][x] == TOWN:
                problems.append(f"bridge at {x},{y} isn't over the river")
    if problems:
        raise SystemExit("\n".join(problems))


def build_def():
    return {
        "maps": [{
            "name": "Millbrook", "label": "Millbrook",
            "description": "A river village round a well: a square, an inn, a dozen thatched houses, gardens, a pig pen and a stone bridge.",
            "width": N, "height": N, "depth": 12, "baseZ": 2,
            "groundLegend": GROUND_LEGEND,
            "featureLegend": FEATURE_LEGEND,
            "center": CENTER,
            "stockpile": STOCKPILE,
            "livestock": LIVESTOCK,
            "buildings": buildings,
            "ground": ["".join(row) for row in ground],
            "levels": ["".join(str(v) for v in row) for row in level],
            "features": ["".join(row) for row in feature],
        }]
    }


def write():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    data = build_def()
    m = data["maps"][0]
    # One grid row per line, so the file reads (and diffs) as a map.
    head = json.dumps({k: v for k, v in m.items() if k not in ("ground", "levels", "features", "buildings")}, indent=2)[:-2]
    rows = lambda key: ",\n".join(f'      "{r}"' for r in m[key])
    blds = ",\n".join("      " + json.dumps(b) for b in m["buildings"])
    text = ("// Millbrook, traced from art/reference/village-map2.webp. Generated by tools/village_map.py:\n"
            "// edit the script and re-run it (hand edits here are fine for small fixes, but a re-run overwrites them).\n"
            '{\n  "maps": [\n    ' + head.replace("\n", "\n    ") + ',\n'
            f'      "buildings": [\n{blds}\n      ],\n'
            f'      "ground": [\n{rows("ground")}\n      ],\n'
            f'      "levels": [\n{rows("levels")}\n      ],\n'
            f'      "features": [\n{rows("features")}\n      ]\n    }}\n  ]\n}}\n')
    with open(OUT, "w") as f:
        f.write(text)
    print(f"wrote {os.path.normpath(OUT)}")


COLORS = {"g": (96, 150, 70), "d": (130, 100, 70), "r": (186, 160, 116), "t": (100, 70, 46), "k": (120, 116, 110),
          "s": (210, 196, 140), "~": (90, 160, 190), "w": (50, 120, 170)}
FEATURE_COLORS = {"O": (40, 90, 40), "P": (30, 70, 50), "B": (90, 130, 60), "b": (150, 40, 60), "x": (110, 140, 200),
                  "o": (140, 140, 140), "f": (90, 60, 30), "G": (200, 140, 60), "c": (120, 200, 90), "W": (60, 60, 200),
                  "=": (170, 170, 170), "u": (120, 80, 40), "X": (160, 120, 70), "C": (110, 70, 40), "/": (200, 200, 120)}


def preview(path):
    from PIL import Image, ImageDraw
    k = 8
    img = Image.new("RGB", (N * k, N * k))
    d = ImageDraw.Draw(img)
    for y in range(N):
        for x in range(N):
            d.rectangle([x * k, y * k, x * k + k - 1, y * k + k - 1], fill=COLORS[ground[y][x]])
            f = feature[y][x]
            if f != ".":
                d.ellipse([x * k + 1, y * k + 1, x * k + k - 2, y * k + k - 2], fill=FEATURE_COLORS[f])
    for b in buildings:
        cells = plot(b["def"], b["at"][0], b["at"][1], b["facing"])
        xs, ys = [c[0] for c in cells], [c[1] for c in cells]
        roof = (200, 168, 96) if b.get("variant") != "Shingle" and b["def"] != "Smithy" else (130, 80, 50)
        d.rectangle([min(xs) * k, min(ys) * k, max(xs) * k + k - 1, max(ys) * k + k - 1], fill=roof, outline=(40, 30, 20))
        fx, fy = front(b["facing"])
        cx, cy = b["at"][0] * k + k // 2, b["at"][1] * k + k // 2
        d.line([cx, cy, cx + fx * 3 * k, cy + fy * 3 * k], fill=(40, 30, 20), width=2)
        d.text((min(xs) * k + 2, min(ys) * k + 2), b.get("role") or b["def"], fill=(0, 0, 0))
    out = Image.new("RGB", (N * k * 2 + 8, N * k), (30, 30, 30))
    out.paste(img, (0, 0))
    if os.path.exists(REFERENCE):
        ref = Image.open(REFERENCE).convert("RGB").resize((72 * k, 72 * k))
        out.paste(ref, (N * k + 8 + CORE * k, CORE * k))
    out.save(path)
    print(f"preview: {path}")


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", metavar="PNG")
    args = ap.parse_args()
    check()
    write()
    if args.preview:
        preview(args.preview)
