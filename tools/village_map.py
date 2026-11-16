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
import os
import random

import mapkit
from mapkit import PLANS, front

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "data", "defs", "maps", "millbrook.json")
REFERENCE = os.path.join(HERE, "..", "art", "reference", "village-map2.webp")

N = 96          # map size
CORE = 12       # where the reference's square (0, 0) lands
TOWN = 2        # the village's level; a river bank is one down, its bed two
rnd = random.Random(1234)

GROUND_LEGEND = {"g": "Grass", "d": "Dirt", "r": "Road", "t": "TilledSoil", "k": "Stone", "s": "Sand", "~": "ShallowWater", "w": "Water"}
FEATURE_LEGEND = {
    "O": "Oak", "P": "Pine", "B": "Birch", "b": "BerryBush", "o": "Boulder", "x": "WildFlax",
    "f": "Fence", "G": "Gate!", "c": "CabbagePlant", "W": "Well", "N": "NoticeBoard", "=": "StoneBridge@2",
    "u": "Barrel", "X": "Crate", "C": "Wagon", "/": "Slope", "R": "WeaponRack", "M": "Midden",
    "h": "OutdoorBench", "T": "OutdoorTable", "_": "DoorStep", "L": "LanternPost", "F": "FlowerTub",
}

# The canvas this script paints (tools/mapkit.py), its grids and helpers bound here by their old names.
M = mapkit.Canvas(N, TOWN)
ground, level, feature, reserved, buildings = M.ground, M.level, M.feature, M.reserved, M.buildings
inside, cells_rect, cells_disc, cells_path, paint, plot = M.inside, M.cells_rect, M.cells_disc, M.cells_path, M.paint, M.plot
building, fence_ring, garden, water, tree_clump = M.building, M.fence_ring, M.garden, M.water, M.tree_clump


def sq(sx, sy):
    """A reference square's cell."""
    return CORE + 2 * sx, CORE + 2 * sy


# ---------------- The land ----------------

# Water first: the river down the south-west under the bridge, and the lake to the north-east.
# Where the bridge crosses, the river runs a short straight reach east-west, so the bridge can span
# it square on (straight parapets, piers in a row) instead of stepping across on the diagonal.
river = [(-2, 61), sq(2, 23.5), (20, 63.5), (22.5, 68), (33.5, 68), (35, 73), sq(11, 34), (36, 96 + 2)]
water(cells_path(river, 5))
water(list(cells_disc(*sq(35.5, 4), 9, 18, wobble=0.18, seed=3)) + list(cells_path([sq(33, -6), sq(34, 0), sq(36.5, 6), (98, 34)], 8)))

# The square and the lanes (packed earth).
paint(cells_disc(*sq(16, 17), 15, 17, wobble=0.12, seed=5), g="r")           # the square round the well
paint(cells_disc(*sq(12, 9), 9, 6, wobble=0.15, seed=7), g="r")               # up to the barns
paint(cells_path([sq(23.7, 12), sq(23.7, 4), sq(23.2, 0), (58, -2)], 4), g="r")    # the north road
paint(cells_path([sq(7, 6.5), sq(12, 6), sq(16, 6.5)], 3), g="r")             # past the pig pen
paint(cells_path([sq(22, 21), sq(27, 21), sq(31, 20.5), (97, 52)], 4), g="r")     # the east road
SOUTH_WEST_ROAD = [sq(12, 24), (31, 61), (27.5, 62.5), (27.5, 74), (22, 76), sq(3.5, 31.5), sq(1.5, 35), (12, 97)]   # straight over the bridge
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
building("TinkerShop", 63, 64, 2, role="TinkerShop", variant="Timber")  # N: south of the square, its door to the north (cottage K stood by here; removed for room)
building("Smithy", 20, 30, 3, role="Forge", variant="Stone")     # E: the west barn with the stone chimney
building("Cottage", 28, 41, 0, role="Home")                     # F: west house with the yard
building("Inn", 61, 45, 1, role="Tavern", variant="Jettied")      # G: on the square, facing the well (its kitchen out the back)
building("Cottage", 74, 44, 1, role="Home", variant="Shingle")  # H: east, the big brown roof (moved over for the inn's kitchen)
building("Cottage", 30, 56, 0, role="Home")                     # I: south-west, with its garden
building("Cottage", 43, 61, 2, role="Home")                     # J: south of the square
building("Shrine", 57, 73, 2, role="Shrine")                     # L: south, in its garden
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
for x, y in [(44, 43), (44, 44), (46, 45), (45, 45), (42, 43), (46, 42)]:   # the muster rack, by them both
    if not reserved[y][x] and level[y][x] == TOWN and feature[y][x] == "." and ground[y][x] not in "wk~":
        feature[y][x] = "R"
        reserved[y][x] = True
        break
else:
    raise SystemExit("no room for the muster rack by the well")
for x, y in [(69, 50), (69, 51), (70, 50), (69, 49), (70, 51)]:    # the midden, behind the inn's storeroom
    if not reserved[y][x] and level[y][x] == TOWN and feature[y][x] == "." and ground[y][x] not in "wk~":
        feature[y][x] = "M"
        reserved[y][x] = True
        break
else:
    raise SystemExit("no room for the midden behind the inn")
# The festival's things (Round/FestivalSystem): put up in the square when the festival begins, so
# they're not features but a list of their own. Checked like the rest: town ground, nothing there.
festival = []
FESTIVAL = [
    ("Bonfire", 43, 51, 0, [(0, 0)]),                                   # south of the well; the dance goes round it
    ("FeastTable", 37, 42, 0, [(0, 0), (0, 1)]), ("FeastTable", 37, 45, 0, [(0, 0), (0, 1)]),   # the feast, west of the well
    *[("Stool", x, y, 0, [(0, 0)]) for x in (36, 38) for y in (42, 43, 45, 46)],
    ("ShowTable", 48, 48, 0, [(0, 0), (0, 1)]),                         # the town's best work, by the inn
    ("HighStriker", 49, 41, 0, [(0, 0)]),                               # the test of strength, by the notice board
    ("ArcheryButt", 31, 49, 3, [(0, 0)]),                               # the butts, facing east up a clear lane
    *[("FestivalLantern", x, y, 0, [(0, 0)]) for x, y in [(39, 40), (47, 39), (34, 45), (50, 45), (38, 55), (48, 55)]],
]
for name, x, y, facing, cells in FESTIVAL:
    spots = [(x + dx, y + dy) for dx, dy in cells]
    for cx, cy in spots:
        if not inside(cx, cy) or reserved[cy][cx] or level[cy][cx] != TOWN or feature[cy][cx] != "." or ground[cy][cx] in "wk~":
            raise SystemExit(f"no room for the festival's {name} at {cx},{cy}")
    for cx, cy in spots:
        reserved[cy][cx] = True
    festival.append({"def": name, "at": [x, y], "facing": facing})
# The butts' shooting mark: festival.json's markCells (8) east of the butt (facing 3), on open
# town ground and kept so. The lane between holds nothing taller than a plant (FestivalTests checks it).
BUTTS_LANE = 8
bx, by = next((x, y) for name, x, y, _, _ in FESTIVAL if name == "ArcheryButt")
mx, my = bx + BUTTS_LANE, by
if not inside(mx, my) or reserved[my][mx] or level[my][mx] != TOWN or feature[my][mx] != "." or ground[my][mx] in "wk~":
    raise SystemExit(f"no room for the butts' mark at {mx},{my}")
reserved[my][mx] = True
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

# The inn's front (its double door at 55,45-46 opens west onto the square): steps up to the doors
# between two lantern posts, a bench and a trestle table against the wall to the north, barrels and
# a flower tub to the south (art/reference/inn*.webp). The doors' own cells stay open.
for (x, y), f in {(54, 45): "_", (54, 46): "_", (54, 44): "L", (54, 47): "L", (54, 41): "h", (53, 41): "T",
                  (54, 48): "u", (54, 49): "u", (53, 49): "u", (54, 50): "F", (54, 39): "F"}.items():
    feature[y][x] = f

# Ramps down to the river and lake banks where the lanes meet them, so the water can be fished.
for x, y in [(15, 60), (24, 62), (34, 80), (78, 38), (75, 30)]:
    if level[y][x] == TOWN - 1:
        feature[y][x] = "/"


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
    return M.map_def("Millbrook", "Millbrook",
                     "A river village round a well: a square, an inn, a dozen thatched houses, gardens, a pig pen and a stone bridge.",
                     GROUND_LEGEND, FEATURE_LEGEND, CENTER, STOCKPILE, LIVESTOCK, festival)


def write():
    mapkit.write(OUT, build_def(),
                 "// Millbrook, traced from art/reference/village-map2.webp. Generated by tools/village_map.py:\n"
                 "// edit the script and re-run it (hand edits here are fine for small fixes, but a re-run overwrites them).\n")


def preview(path):
    mapkit.preview(M, path, REFERENCE, CORE)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", metavar="PNG")
    args = ap.parse_args()
    check()
    write()
    if args.preview:
        preview(args.preview)
