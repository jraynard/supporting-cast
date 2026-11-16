#!/usr/bin/env python3
"""Author the Reedmere map (data/defs/maps/reedmere.json): a town on stilts over a lake.

A strip of shore down the west side takes the road in, with the working town: the barn, the smithy
(its forge kept off the timber), the woodshop, the healer's shrine by the road (raids are fought on
the shore, and the wounded are carried in there), and the cookhouse at the foot of the causeway. The
well and the festival green are on the shore too. The causeway runs out over the water to a square
on piles (the notice board, the muster rack, dipping posts for the buckets), and boardwalks branch
from it: north to the tinker and a house, east to the inn at the far landing, south to the houses.
Everything out there is timber over the water: one road in, a long way round by none, and a fire
that has the whole town to run along.

No reference image: the layout is authored here. Edit this script, not the JSON.

  python3 tools/lake_map.py                 # write data/defs/maps/reedmere.json
  python3 tools/lake_map.py --preview OUT   # also draw it
"""
import argparse
import math
import os
import random

import mapkit

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "data", "defs", "maps", "reedmere.json")

N = 96          # map size
TOWN = 2        # the shore's level, and the decks' (they stand level with it)
LAKE = 0        # the water's level: two below the decks, so the piles show
DECK = TOWN     # the level a building on the water stands at

GROUND_LEGEND = {"g": "Grass", "d": "Dirt", "r": "Road", "t": "TilledSoil", "k": "Stone", "s": "Sand", "~": "ShallowWater", "w": "Water"}
FEATURE_LEGEND = {
    "O": "Oak", "P": "Pine", "B": "Birch", "b": "BerryBush", "o": "Boulder", "x": "WildFlax",
    "f": "Fence", "G": "Gate!", "c": "CabbagePlant", "W": "Well", "u": "Barrel", "X": "Crate", "M": "Midden",
    # Over the water: decks and what stands on them, at the decks' level.
    "=": f"Boardwalk@{DECK}", "#": f"Causeway@{DECK}",
    "D": f"Boardwalk@{DECK}+DippingPost@{DECK}", "N": f"Boardwalk@{DECK}+NoticeBoard@{DECK}",
    "R": f"Boardwalk@{DECK}+WeaponRack@{DECK}", "U": f"Boardwalk@{DECK}+Barrel@{DECK}", "K": f"Boardwalk@{DECK}+Crate@{DECK}",
}
DECKS = "=#DNRUK"

M = mapkit.Canvas(N, TOWN)
ground, level, feature, reserved = M.ground, M.level, M.feature, M.reserved


def shore_x(y):
    """Where the land ends in a row: a wobbling shoreline down the west side."""
    return int(round(25 + 2 * math.sin(y / 7) + 1.5 * math.sin(y / 13 + 1)))


# ---------------- The land and the lake ----------------

for y in range(N):
    sx = shore_x(y)
    for x in range(N):
        if x < sx:
            continue
        if x == sx:
            ground[y][x], level[y][x] = "s", TOWN - 1          # a strip of beach, a step down
        elif x <= sx + 2:
            ground[y][x], level[y][x] = "~", LAKE              # the shallows, reedy
        else:
            ground[y][x], level[y][x] = "w", LAKE


def over_water(x, y):
    return level[y][x] < TOWN


def deck(cells, f="="):
    """Lay boardwalk over the water (on the shore it's just ground: a deck starts at the beach)."""
    for x, y in cells:
        if M.inside(x, y) and over_water(x, y) and feature[y][x] == ".":
            feature[y][x] = f


# The road in from the west, to the causeway's foot.
ROAD_Y = 48
M.paint(M.cells_path([(-2, ROAD_Y), (12, ROAD_Y - 1), (shore_x(ROAD_Y) + 1, ROAD_Y)], 4), g="r")
M.paint(M.cells_path([(12, ROAD_Y), (12, 60), (8, 68)], 3), g="r")            # down to the well green
M.paint(M.cells_disc(9, 70, 8, 6, wobble=0.15, seed=5), g="r")                # the green round the well
M.paint(M.cells_path([(14, ROAD_Y), (14, 32), (10, 26)], 3), g="r")           # up past the smithy to the woodshop

# The causeway: three planks wide, from the beach out to the square.
SQUARE = (42, 40, 56, 56)                                                      # x0, y0, x1, y1
for x, y in M.cells_rect(shore_x(ROAD_Y) - 1, ROAD_Y - 1, SQUARE[0] - 1, ROAD_Y + 1):
    if over_water(x, y):
        feature[y][x] = "#"
deck(M.cells_rect(*SQUARE))
# Boardwalks from the square: north, east to the inn, south.
deck(M.cells_rect(48, 10, 50, SQUARE[1] - 1))
deck(M.cells_rect(SQUARE[2] + 1, 47, 71, 49))
deck(M.cells_rect(48, SQUARE[3] + 1, 50, 86))


def on_water(name, ax, ay, facing, role="", rim=1):
    """A building on its own deck over the water, joined to the walks: no foundation (its Piled
    variant), the boardwalk under it its floor, and a rim of deck round it."""
    cells = M.plot(name, ax, ay, facing)
    xs, ys = [c[0] for c in cells], [c[1] for c in cells]
    deck(M.cells_rect(min(xs) - rim, min(ys) - rim, max(xs) + rim, max(ys) + rim))
    for x, y in cells:
        if not over_water(x, y) or feature[y][x] != "=":
            raise SystemExit(f"{name} at {ax},{ay}: plot cell {x},{y} isn't over open water")
    M.building(name, ax, ay, facing, role=role, variant="Piled", level=DECK, doorstep=None)


def gangway(x0, y0, x1, y1):
    """A short boardwalk from a building's deck to a walk."""
    deck(M.cells_rect(min(x0, x1), min(y0, y1), max(x0, x1), max(y0, y1)))


# ---------------- Buildings ----------------

# On the shore.
M.building("Barn", 6, 40, 0, variant="Timber")                           # the stockpile, by the road in
M.building("Smithy", 19, 41, 0, role="Forge", variant="Stone")           # its forge on solid ground, away from the timber
M.building("Woodshop", 7, 24, 0, role="Woodshop", variant="Timber")
M.building("Shrine", 18, 30, 1, role="Shrine")                         # by the road, where raids are fought: the wounded can be carried in
M.building("Cookhouse", 17, 57, 2, role="Kitchen", variant="Timber")     # at the causeway's foot, its door to the road

# Out on the water.
on_water("Cottage", 42, 20, 3, role="Home")                              # west of the north walk, its door onto it
gangway(46, 19, 47, 21)
on_water("TinkerShop", 57, 26, 1, role="TinkerShop")                     # east of the north walk
gangway(51, 25, 53, 27)
on_water("LandingInn", 79, 48, 1, role="Tavern")                         # at the far landing, its door to the east walk
on_water("Cottage", 42, 66, 3, role="Home")                              # down the south walk
on_water("Cottage", 57, 66, 1, role="Home")
on_water("Cottage", 42, 80, 3, role="Home")
on_water("Cottage", 57, 80, 1, role="Home")
gangway(46, 64, 47, 68)
gangway(51, 64, 53, 68)
gangway(46, 78, 47, 82)
gangway(51, 78, 53, 82)

# ---------------- On the square and the walks ----------------

for (x, y), f in {(50, 44): "N", (52, 44): "R",                             # the notice board and the muster rack
                  (42, 40): "D", (56, 40): "D", (42, 56): "D", (56, 56): "D",   # buckets at the square's corners
                  (48, 12): "D", (70, 47): "D", (48, 86): "D",                  # and at the walks' ends
                  (55, 52): "U", (54, 52): "K", (44, 52): "U"}.items():
    if feature[y][x] != "=" or reserved[y][x]:
        raise SystemExit(f"no deck free for '{f}' at {x},{y}")
    feature[y][x] = f
    reserved[y][x] = True

# On the shore: the well (the festival gathers there), the midden, barrels.
feature[70][9] = "W"
reserved[70][9] = True
M.place_first("M", [(22, 64), (21, 64), (22, 65)], "the midden behind the cookhouse")
for (x, y), f in {(10, 45): "u", (11, 45): "X", (24, 44): "u", (3, 52): "X"}.items():
    if M.free(x, y):
        feature[y][x] = f

# The festival, on the green round the well (Round/FestivalSystem).
festival = []
FESTIVAL = [
    ("Bonfire", 9, 76, 0, [(0, 0)]),
    ("FeastTable", 4, 66, 0, [(0, 0), (0, 1)]), ("FeastTable", 4, 69, 0, [(0, 0), (0, 1)]),
    *[("Stool", x, y, 0, [(0, 0)]) for x in (3, 5) for y in (66, 67, 69, 70)],
    ("ShowTable", 14, 70, 0, [(0, 0), (0, 1)]),
    ("HighStriker", 12, 65, 0, [(0, 0)]),
    ("ArcheryButt", 3, 76, 3, [(0, 0)]),
    *[("FestivalLantern", x, y, 0, [(0, 0)]) for x, y in [(6, 63), (12, 63), (2, 72), (16, 72), (6, 79), (13, 79)]],
]
for name, x, y, facing, cells in FESTIVAL:
    spots = [(x + dx, y + dy) for dx, dy in cells]
    for cx, cy in spots:
        if not M.free(cx, cy):
            raise SystemExit(f"no room for the festival's {name} at {cx},{cy}")
    for cx, cy in spots:
        reserved[cy][cx] = True
    festival.append({"def": name, "at": [x, y], "facing": facing})
BUTTS_LANE = 8   # festival.json's markCells, east of the butt
bx, by = next((x, y) for name, x, y, _, _ in FESTIVAL if name == "ArcheryButt")
if not M.free(bx + BUTTS_LANE, by):
    raise SystemExit(f"no room for the butts' mark at {bx + BUTTS_LANE},{by}")
reserved[by][bx + BUTTS_LANE] = True
for x in range(bx + 1, bx + BUTTS_LANE):
    reserved[by][x] = True   # the lane stays clear of trees

# Kitchen gardens on the shore.
M.garden(1, 54, 8, 61, gate=(8, 57))

# ---------------- Woods ----------------

r = random.Random(77)
for y in range(N):
    for x in range(shore_x(y) - 1):
        if ground[y][x] != "g" or level[y][x] != TOWN or reserved[y][x] or feature[y][x] != ".":
            continue
        # Thick at the map's west edge and the shore's two ends, thinning toward the houses.
        edge = min(x, y, N - 1 - y)
        k = max(0.0, (8 - edge) / 8)
        roll = r.random()
        if roll < 0.06 + 0.5 * k:
            pick = r.random()
            feature[y][x] = ("P" if pick < 0.5 else "B" if pick < 0.75 else "O") if y < N / 2 else ("O" if pick < 0.55 else "B" if pick < 0.8 else "P")
        elif roll > 0.985:
            feature[y][x] = "b"
        elif roll > 0.975:
            feature[y][x] = "x"
for y in range(N):
    for x in range(N):
        if ground[y][x] == "r" and feature[y][x] in "OPBbxo":
            feature[y][x] = "."
# The guaranteed resources a new round needs near the start.
for x, y in [(4, 47), (3, 49), (5, 51)]:
    if M.free(x, y):
        feature[y][x] = "b"
for x, y in [(20, 52), (21, 51), (19, 53)]:
    if M.free(x, y):
        feature[y][x] = "x"

CENTER = [20, 49]
STOCKPILE = [3, 39, 5, 3]   # inside the barn
LIVESTOCK = []


def check():
    """What MapBuilder would refuse, caught here."""
    problems = []
    for b in M.buildings:
        for x, y in M.plot(b["def"], b["at"][0], b["at"][1], b["facing"]):
            if "level" in b:
                if feature[y][x] != "=":
                    problems.append(f"{b['def']} at {b['at']}: plot cell {x},{y} isn't plain deck")
            elif not M.inside(x, y) or level[y][x] != TOWN or ground[y][x] in "wk~s":
                problems.append(f"{b['def']} at {b['at']}: plot cell {x},{y} isn't level shore")
            elif feature[y][x] in "fGWuXC":
                problems.append(f"{b['def']} at {b['at']}: {feature[y][x]} on its plot at {x},{y}")
    for y in range(N):
        for x in range(N):
            f = feature[y][x]
            if f in DECKS and not over_water(x, y):
                problems.append(f"deck '{f}' at {x},{y} isn't over the water")
            if f not in "." + DECKS and over_water(x, y):
                problems.append(f"'{f}' at {x},{y} is in the water")
    if problems:
        raise SystemExit("\n".join(problems))


def build_def():
    return M.map_def("Reedmere", "Reedmere",
                     "A town on stilts over a lake: a shore with the road in, the cookhouse and the forge, and out over the water on piles the square, the shrine, the tinker's, the houses and the inn at the far landing. One causeway in.",
                     GROUND_LEGEND, FEATURE_LEGEND, CENTER, STOCKPILE, LIVESTOCK, festival)


FEATURE_COLORS = dict(mapkit.FEATURE_COLORS, **{"=": (150, 110, 70), "#": (120, 85, 50), "D": (60, 60, 200), "N": (150, 110, 60),
                                                "R": (180, 60, 60), "U": (120, 80, 40), "K": (160, 120, 70), "M": (90, 70, 40)})

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", metavar="PNG")
    args = ap.parse_args()
    check()
    mapkit.write(OUT, build_def(),
                 "// Reedmere, a town on stilts over a lake. Generated by tools/lake_map.py:\n"
                 "// edit the script and re-run it (hand edits here are fine for small fixes, but a re-run overwrites them).\n")
    if args.preview:
        mapkit.preview(M, args.preview, feature_colors=FEATURE_COLORS)
