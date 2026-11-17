#!/usr/bin/env python3
"""Author the Reedmere map (data/defs/maps/reedmere.json): a town on stilts over a lake.

After the lake towns in art/reference/ (laketown_map_by_tipexleloup*.jpg for the plan; esgaroth*,
laketown*.webp for the look): a crowded rectangle of houses on piles standing out in the lake, with
a pool of open harbour water in its middle, and one long bridge back to the shore.

  - The shore is only a headland in the south-west corner: the road in off the west edge, the
    woods, a garden, a barn and the woodshop, and a rocky spit reaching out to the bridge's foot.
  - The long bridge (the Causeway: a raid can bring it down) runs north from the spit to the gate
    quay. The shrine stands at the gate, where the fighting comes in; the forge beside it on a
    stone pier, kept off the timber; the town's storehouse (and its stockpile) behind the forge.
  - The main street runs from the gate up to the harbour, across it on the festival's boats,
    and on to the moot hall on the far quay: the town's hall, the notice board and the muster rack
    on its stone forecourt, the great bow beside them.
  - The inn and the cookhouse face each other across the water-gate, the canal from the harbour
    out to the lake; the tinker's is on the west quay; the houses fill the rest.
  - The festival is out on the boats lashed together in the harbour, round the mast (the town has
    no square to spare); rowboats are moored about, nets and fish dry on the quays, and lanterns
    burn along the harbour's edge all night.

Everything out there is timber over the water: one road in, and a fire that has the whole town to
run along. Edit this script, not the JSON.

  python3 tools/lake_map.py                 # write data/defs/maps/reedmere.json
  python3 tools/lake_map.py --preview OUT   # also draw it
"""
import argparse
import json
import math
import os
import random
import re

import mapkit

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "data", "defs", "maps", "reedmere.json")
DEFS = os.path.join(HERE, "..", "data", "defs")

N = 96          # map size
TOWN = 2        # the shore's level, and the decks' (they stand level with it)
LAKE = 0        # the water's level: two below the decks, so the piles show
DECK = TOWN     # the level a building on the water stands at

GROUND_LEGEND = {"g": "Grass", "d": "Dirt", "r": "Road", "t": "TilledSoil", "k": "Stone", "s": "Sand", "~": "ShallowWater", "w": "Water"}
FEATURE_LEGEND = {
    "O": "Oak", "P": "Pine", "B": "Birch", "b": "BerryBush", "o": "Boulder", "x": "WildFlax",
    "f": "Fence", "G": "Gate!", "g": "Gate", "c": "CabbagePlant", "W": "Well", "u": "Barrel", "X": "Crate", "M": "Midden",
    # Over the water: decks and what stands on them, at the decks' level.
    "=": f"Boardwalk@{DECK}", "#": f"Causeway@{DECK}", "S": f"StonePier@{DECK}", "=b": None,
    "D": f"Boardwalk@{DECK}+DippingPost@{DECK}", "N": f"StonePier@{DECK}+NoticeBoard@{DECK}",
    "R": f"StonePier@{DECK}+WeaponRack@{DECK}", "U": f"Boardwalk@{DECK}+Barrel@{DECK}", "K": f"Boardwalk@{DECK}+Crate@{DECK}",
    "L": f"Boardwalk@{DECK}+QuayLantern@{DECK}", "Y": f"Boardwalk@{DECK}+DryingRack@{DECK}", "T": f"Boardwalk@{DECK}+NetFrame@{DECK}",
    "Q": f"StonePier@{DECK}+GreatBow@{DECK}", "l": f"StonePier@{DECK}+QuayLantern@{DECK}",
    # The festival's boats in the harbour, lashed into a deck round the mast, the bonfire's barge among them.
    "a": f"BoatDeck@{DECK}", "A": f"BoatDeck@{DECK}+FestivalMast@{DECK}", "F": f"FireBarge@{DECK}",
    # In the water itself.
    "v": "Rowboat", "j": "Reeds",
}
del FEATURE_LEGEND["=b"]
DECKS = "=#SDNRUKLYTQlaAF"     # a floor at the deck's level, maybe with something on it
PLAIN = "=S"                   # deck with nothing on it: a building's plot or a prop's place
FLOATING = "vj"                # stands in the water

M = mapkit.Canvas(N, TOWN)
ground, level, feature, reserved = M.ground, M.level, M.feature, M.reserved


# ---------------- Building plans, read from the defs (for doors and lines) ----------------

def _load(name):
    text = open(os.path.join(DEFS, name)).read()
    text = re.sub(r'(?m)^\s*//.*$', "", text)                      # whole-line comments
    text = re.sub(r'(?<=[,\[{\]}"\d])\s*//[^\n"]*$', "", text, flags=re.M)  # trailing ones
    text = re.sub(r",(\s*[\]}])", r"\1", text)                     # trailing commas
    return json.loads(text)


LAYOUTS = {b["name"]: b for f in ("buildings.json", "village.json") for b in _load(f).get("buildings", [])}


def turn(dx, dy, facing):
    return {0: (dx, dy), 1: (-dy, dx), 2: (-dx, -dy), 3: (dy, -dx)}[facing]


def plan_cell(name, col, row, ax, ay, facing):
    w, d = mapkit.PLANS[name]
    rx, ry = turn(col - (w - 1) // 2, row - (d - 1) // 2, facing)
    return ax + rx, ay + ry


def doors(name, ax, ay, facing):
    """Each door's cell and the cell outside it (the plan's edge it's in, turned as the building is)."""
    rows = LAYOUTS[name]["layout"]
    out = []
    for row, line in enumerate(rows):
        for col, ch in enumerate(line):
            if ch != "D":
                continue
            ox, oy = (-1 if col == 0 else 1 if col == len(line) - 1 else 0), (-1 if row == 0 else 1 if row == len(rows) - 1 else 0)
            if ox == oy == 0:
                continue  # inside the building
            dx, dy = turn(ox, oy, facing)
            x, y = plan_cell(name, col, row, ax, ay, facing)
            out.append(((x, y), (x + dx, y + dy)))
    return out


# ---------------- The land and the lake ----------------

def shore_x(y):
    """Where the headland ends in a row (land west of it): only the south-west corner is land."""
    if y < 62:
        return -1
    return int(round(36 * math.sqrt((y - 62) / 34) + 1.5 * math.sin(y / 5)))


SPIT = [(24, 89), (44, 85), (57, 81)]      # the rocky spit out to the bridge's foot


def on_spit(x, y):
    return min(mapkit.seg_dist(x, y, *SPIT[i], *SPIT[i + 1]) for i in range(len(SPIT) - 1)) <= 3.2


for y in range(N):
    for x in range(N):
        land = x < shore_x(y) or on_spit(x, y)
        near = any(0 <= x + dx < N and 0 <= y + dy < N and (x + dx < shore_x(y + dy) or on_spit(x + dx, y + dy))
                   for dx in range(-3, 4) for dy in range(-3, 4) if abs(dx) + abs(dy) <= 3)
        if land:
            continue
        beach = any(0 <= x + dx < N and 0 <= y + dy < N and (x + dx < shore_x(y + dy) or on_spit(x + dx, y + dy))
                    for dx in (-1, 0, 1) for dy in (-1, 0, 1))
        if beach:
            ground[y][x], level[y][x] = "s", TOWN - 1          # a strip of beach, a step down
        elif near:
            ground[y][x], level[y][x] = "~", LAKE              # the shallows, reedy
        else:
            ground[y][x], level[y][x] = "w", LAKE
for y in range(N):
    for x in range(N):
        if on_spit(x, y) and ground[y][x] == "g" and level[y][x] == TOWN and (x * 7 + y * 3) % 4 == 0:
            ground[y][x] = "d"    # thin turf over the spit's rock


def over_water(x, y):
    return level[y][x] < TOWN


def deck(cells, f="="):
    """Lay deck over the water (on the shore it's just ground)."""
    for x, y in cells:
        if M.inside(x, y) and over_water(x, y) and feature[y][x] in ".=S":
            feature[y][x] = f


def open_water(cells):
    """Leave the water open: no deck (the harbour, the water-gate)."""
    for x, y in cells:
        if M.inside(x, y) and over_water(x, y):
            feature[y][x] = "."


# ---------------- The town on the water ----------------

TOWN_RECT = (31, 6, 89, 58)     # x0, y0, x1, y1: all of it on piles
POOL = (50, 24, 70, 42)         # the harbour, open water in the middle
GATE_CANAL = (74, 30, 86, 34)   # the water-gate: the harbour's way out to the lake, under the walks
RAFT = (55, 28, 65, 37)         # the festival's boats, lashed in the harbour's middle: five, east-west, two rows each
MAIN = (59, 61)                 # the main street's columns, from the gate to the moot hall
BRIDGE_Y = (59, 80)             # the long bridge, from the gate quay south to the spit

deck(M.cells_rect(*TOWN_RECT))
open_water(M.cells_rect(*POOL))
open_water(M.cells_rect(*GATE_CANAL))
for x, y in M.cells_rect(*RAFT):
    feature[y][x] = "a"
for x, y in M.cells_rect(MAIN[0], POOL[1], MAIN[1], RAFT[1] - 1):       # gangplanks to the quays
    feature[y][x] = "="
for x, y in M.cells_rect(MAIN[0], RAFT[3] + 1, MAIN[1], POOL[3]):
    feature[y][x] = "="
# The long bridge, three planks wide, from the gate to the spit; over the water only.
for x, y in M.cells_rect(MAIN[0], BRIDGE_Y[0], MAIN[1], BRIDGE_Y[1]):
    if over_water(x, y):
        feature[y][x] = "#"

# The stone: the forge's pier and the moot hall's forecourt.
SMITHY_PIER = (62, 46, 69, 56)
FORECOURT = (52, 19, 68, 23)
for x, y in M.cells_rect(*SMITHY_PIER):
    feature[y][x] = "S"
for x, y in M.cells_rect(*FORECOURT):
    feature[y][x] = "S"

# ---------------- Buildings ----------------

plots = {}   # cell -> the building on it


def place(name, ax, ay, facing, role="", variant="", water=True):
    cells = M.plot(name, ax, ay, facing)
    for x, y in cells:
        if (x, y) in plots:
            raise SystemExit(f"{name} at {ax},{ay}: cell {x},{y} is {plots[(x, y)]}'s")
        if water and feature[y][x] not in PLAIN:
            raise SystemExit(f"{name} at {ax},{ay}: plot cell {x},{y} isn't plain deck ('{feature[y][x]}')")
    for c in cells:
        plots[c] = name
    if water:
        M.building(name, ax, ay, facing, role=role, variant=variant, level=DECK, doorstep=None)
    else:
        M.building(name, ax, ay, facing, role=role, variant=variant)
    return cells


# Round the harbour.
place("MootHall", 60, 14, 0, variant="Piled")                                  # the far quay, its door on the forecourt
place("LandingInn", 80, 22, 1, role="Tavern", variant="PiledShingle")        # north of the water-gate, its door on the quay
place("Cookhouse", 80, 41, 1, role="Kitchen", variant="PiledShingle")        # south of it: the cook a footbridge from the bar
place("TinkerShop", 42, 31, 3, role="TinkerShop", variant="Piled")           # the west quay
# At the gate.
place("Shrine", 52, 51, 2, role="Shrine", variant="Piled")                     # where the fighting comes in
place("Smithy", 66, 51, 1, role="Forge", variant="Piled")                      # on its stone pier, door to the main street
place("Barn", 73, 51, 1, variant="Piled")                                      # the storehouse, where the stockpile is
# The houses, crowded in rows along the walks: the north quay, then two columns down the west side
# (doors out to the west walk, and in to the harbour's quay), and the corners by the gate.
place("Cottage", 37, 13, 0, role="Home", variant="PiledShingle")
place("Barn", 47, 13, 0, variant="Piled")                                      # a warehouse on the north quay
place("Cottage", 72, 11, 2, role="Home", variant="PiledShingle")
place("Cottage", 82, 11, 2, role="Home", variant="Piled")
place("Cottage", 36, 23, 1, role="Home", variant="Piled")
place("Barn", 36, 33, 1, variant="Piled")                                      # the net loft
place("Cottage", 36, 44, 1, role="Home", variant="PiledShingle")
place("Cottage", 42, 22, 3, role="Home", variant="PiledShingle")
place("Cottage", 42, 40, 3, role="Home", variant="Piled")
place("Cottage", 37, 53, 0, role="Home", variant="PiledShingle")
place("Cottage", 82, 52, 0, role="Home", variant="Piled")
# On the shore: the old barn and the woodshop by the road.
place("Barn", 10, 82, 0, variant="Timber", water=False)
place("Woodshop", 20, 80, 0, role="Woodshop", variant="Timber", water=False)

STOCKPILE = [71, 49, 4, 6]    # inside the storehouse at the gate (Barn, facing 1, at 73,51)
CENTER = [61, 50]             # up the main street from the gate, where the bridge comes in (the villagers land in a row two south of it)

# ---------------- On the quays ----------------


DOORSTEPS = {out for b in M.buildings for _, out in doors(b["def"], b["at"][0], b["at"][1], b["facing"])}


def put(f, x, y, why):
    """A prop on the deck: never on a plot, before a door or on the main street."""
    if feature[y][x] not in PLAIN or (x, y) in plots or (x, y) in DOORSTEPS or MAIN[0] <= x <= MAIN[1] and y > FORECOURT[3] or (x, y) in taken:
        raise SystemExit(f"no deck free for '{f}' ({why}) at {x},{y}: '{feature[y][x]}'")
    feature[y][x] = f
    taken.add((x, y))


taken = set()


put("N", 57, 21, "the notice board")
put("R", 63, 21, "the muster rack")
put("Q", 54, 21, "the great bow")
put("l", 52, 23, "a lantern")
put("l", 68, 23, "a lantern")
# Buckets: at the harbour's corners, the gate, and the town's outer edges.
for x, y in [(49, 24), (71, 24), (49, 42), (71, 42), (58, 58), (31, 30), (89, 46), (45, 6)]:
    put("D", x, y, "a dipping post")
# Lanterns along the harbour's edge, and at the gate.
for x, y in [(49, 28), (49, 33), (49, 38), (71, 28), (71, 38), (54, 43), (66, 43), (62, 58)]:
    put("L", x, y, "a lantern")
# Fish drying and nets mending along the outer walks, facing the lake.
for x, y in [(31, 20), (31, 24), (31, 38), (31, 44), (89, 12), (89, 52)]:
    put("Y" if (x + y) % 2 else "T", x, y, "the catch")
for x, y in [(44, 7), (52, 7), (66, 7), (84, 57), (76, 57)]:
    put("T" if x % 2 else "Y", x, y, "the nets")
for (x, y), f in {(56, 46): "U", (57, 46): "K", (44, 46): "U", (75, 29): "K", (76, 29): "U", (86, 29): "U", (39, 30): "K"}.items():
    put(f, x, y, "stores")

# Rowboats moored in the harbour, the water-gate and off the outer walks (each two cells, along y).
for x, y in [(52, 26), (68, 26), (52, 34), (68, 33), (53, 39), (67, 39), (29, 16), (29, 34), (91, 22), (91, 42), (40, 60), (80, 60), (84, 31)]:
    for cy in (y, y + 1):
        if feature[cy][x] != "." or ground[cy][x] != "w":
            raise SystemExit(f"no open water for a rowboat at {x},{cy}")
    feature[y][x] = "v"
    reserved[y][x] = reserved[y + 1][x] = True
    feature[y + 1][x] = ","     # the boat's stern: kept clear (written back as '.')

# Reeds in the shallows along the headland and the spit.
r = random.Random(41)
for y in range(N):
    for x in range(N):
        if ground[y][x] == "~" and feature[y][x] == "." and r.random() < 0.45:
            feature[y][x] = "j"
for y in range(N):
    for x in range(N):
        if feature[y][x] == ",":
            feature[y][x] = "."

# ---------------- The festival, on the boats ----------------

feature[32][60] = "A"      # the mast: the gathering place
feature[35][60] = "F"      # the bonfire's barge
reserved[32][60] = True
festival = []
FESTIVAL = [
    ("Bonfire", 60, 35, 0, [(0, 0)]),
    ("FeastTable", 57, 29, 0, [(0, 0), (0, 1)]), ("FeastTable", 57, 32, 0, [(0, 0), (0, 1)]),
    *[("Stool", x, y, 0, [(0, 0)]) for x in (56, 58) for y in (29, 30, 32, 33)],
    ("ShowTable", 63, 29, 0, [(0, 0), (0, 1)]),
    ("HighStriker", 64, 33, 0, [(0, 0)]),
    ("ArcheryButt", 55, 37, 3, [(0, 0)]),
    *[("FestivalLantern", x, y, 0, [(0, 0)]) for x, y in [(55, 28), (65, 28), (55, 35), (65, 36), (62, 31), (58, 35)]],
]
for name, x, y, facing, cells in FESTIVAL:
    spots = [(x + dx, y + dy) for dx, dy in cells]
    for cx, cy in spots:
        if feature[cy][cx] not in "aF" or reserved[cy][cx]:
            raise SystemExit(f"no room on the boats for the festival's {name} at {cx},{cy}")
    for cx, cy in spots:
        reserved[cy][cx] = True
    festival.append({"def": name, "at": [x, y], "facing": facing, "level": DECK})
BUTTS_LANE = 8   # festival.json's markCells, east of the butt
bx, by = next((x, y) for name, x, y, _, _ in FESTIVAL if name == "ArcheryButt")
for x in range(bx + 1, bx + BUTTS_LANE + 1):
    if feature[by][x] not in "aF" or reserved[by][x]:
        raise SystemExit(f"the butts' lane isn't clear at {x},{by}")
    reserved[by][x] = True

# ---------------- The headland ----------------

ROAD_Y = 88
M.paint(M.cells_path([(-2, ROAD_Y), (20, ROAD_Y - 1), (34, ROAD_Y - 2), (46, 84), (57, 81), (60, 81)], 3), g="r")
M.paint(M.cells_path([(10, ROAD_Y - 1), (10, 86)], 2), g="r")                       # to the barn's doors
M.place_first("W", [(36, 83), (38, 83), (33, 84), (30, 81)], "the well by the bridge's foot")
M.garden(2, 90, 12, 94, gate=(7, 90))
M.place_first("M", [(30, 92), (31, 92), (30, 93)], "the midden")
for (x, y), f in {(18, 86): "u", (19, 86): "X", (30, 85): "u"}.items():
    if M.free(x, y):
        feature[y][x] = f
for x, y in M.cells_rect(0, 0, N - 1, N - 1):                 # nothing grows on a building's plot
    if (x, y) in plots:
        reserved[y][x] = True

r = random.Random(77)
for y in range(N):
    for x in range(N):
        if ground[y][x] != "g" or level[y][x] != TOWN or reserved[y][x] or feature[y][x] != ".":
            continue
        edge = min(x, N - 1 - y)
        k = max(0.0, (10 - edge) / 10)
        roll = r.random()
        if roll < 0.1 + 0.55 * k:
            pick = r.random()
            feature[y][x] = "P" if pick < 0.45 else "B" if pick < 0.7 else "O"
        elif roll > 0.985:
            feature[y][x] = "b"
        elif roll > 0.975:
            feature[y][x] = "x"
for y in range(N):
    for x in range(N):
        if ground[y][x] == "r" and feature[y][x] in "OPBbxo":
            feature[y][x] = "."
# The guaranteed resources a new round needs near the start.
for x, y in [(30, 82), (32, 83), (28, 83)]:
    if M.free(x, y):
        feature[y][x] = "b"
for x, y in [(36, 81), (37, 82), (35, 82)]:
    if M.free(x, y):
        feature[y][x] = "x"
for x, y in [(44, 87), (49, 84), (52, 80), (40, 88), (47, 87), (54, 83), (36, 89)]:
    if M.free(x, y) and ground[y][x] != "r":
        feature[y][x] = "o"

LIVESTOCK = []


def check():
    """What MapBuilder would refuse, caught here."""
    problems = []
    for b in M.buildings:
        name, (ax, ay), facing = b["def"], b["at"], b["facing"]
        for x, y in M.plot(name, ax, ay, facing):
            if "level" in b:
                if feature[y][x] not in PLAIN:
                    problems.append(f"{name} at {b['at']}: plot cell {x},{y} isn't plain deck")
            elif not M.inside(x, y) or level[y][x] != TOWN or ground[y][x] in "wk~s":
                problems.append(f"{name} at {b['at']}: plot cell {x},{y} isn't level shore")
            elif feature[y][x] not in ".":
                problems.append(f"{name} at {b['at']}: {feature[y][x]} on its plot at {x},{y}")
        for (dx, dy), (ox, oy) in doors(name, ax, ay, facing):
            if not M.inside(ox, oy) or (ox, oy) in plots:
                problems.append(f"{name} at {b['at']}: its door at {dx},{dy} opens onto {ox},{oy}, another building")
            elif "level" in b and feature[oy][ox] not in DECKS:
                problems.append(f"{name} at {b['at']}: its door at {dx},{dy} opens onto the water at {ox},{oy}")
            elif feature[oy][ox] not in "=S#." or (ox, oy) in [tuple(p) for p in []]:
                problems.append(f"{name} at {b['at']}: its door at {dx},{dy} is blocked by '{feature[oy][ox]}' at {ox},{oy}")
    for y in range(N):
        for x in range(N):
            f = feature[y][x]
            if f in DECKS and not over_water(x, y):
                problems.append(f"deck '{f}' at {x},{y} isn't over the water")
            if f not in "." + DECKS + FLOATING and over_water(x, y):
                problems.append(f"'{f}' at {x},{y} is in the water")
            if f in FLOATING and not over_water(x, y):
                problems.append(f"'{f}' at {x},{y} isn't in the water")
    if problems:
        raise SystemExit("\n".join(problems))


def build_def():
    d = M.map_def("Reedmere", "Reedmere",
                  "A town on stilts out in a lake: houses crowded on piles round a harbour, one long bridge back to the shore. The festival's held on the boats. Still being built: untuned, and harder than Millbrook.",
                  GROUND_LEGEND, FEATURE_LEGEND, CENTER, STOCKPILE, LIVESTOCK, festival)
    return d


FEATURE_COLORS = dict(mapkit.FEATURE_COLORS, **{"=": (150, 110, 70), "#": (120, 85, 50), "S": (150, 150, 150), "D": (60, 60, 200),
                                                "N": (150, 110, 60), "R": (180, 60, 60), "U": (120, 80, 40), "K": (160, 120, 70),
                                                "M": (90, 70, 40), "L": (250, 210, 90), "l": (250, 210, 90), "Y": (200, 190, 170),
                                                "T": (190, 170, 120), "Q": (90, 40, 30), "a": (100, 70, 45), "A": (255, 255, 255),
                                                "F": (110, 110, 110), "v": (120, 80, 50), "j": (110, 140, 70)})


def roof(b):
    return (110, 120, 140) if "Shingle" in b.get("variant", "") or b["def"] == "MootHall" else \
        (130, 130, 130) if b["def"] == "Smithy" else (200, 168, 96)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", metavar="PNG")
    args = ap.parse_args()
    check()
    mapkit.write(OUT, build_def(),
                 "// Reedmere, a town on stilts over a lake. Generated by tools/lake_map.py:\n"
                 "// edit the script and re-run it (hand edits here are fine for small fixes, but a re-run overwrites them).\n")
    if args.preview:
        mapkit.preview(M, args.preview, feature_colors=FEATURE_COLORS, roof=roof)
