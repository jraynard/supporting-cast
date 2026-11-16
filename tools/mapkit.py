"""Shared helpers for authoring hand-made maps (data/defs/maps/*.json): a Canvas of ground, level and
feature grids that a map's script paints into, building plots, fences and gardens, trees, and the
writer and preview every map uses. See tools/village_map.py (Millbrook) and tools/lake_map.py (Reedmere).

The output is the plain text-grid MapDef (src/HeroesNotIncluded.Sim/Defs/MapDefs.cs).
"""
import json
import math
import os
import random

# Plan sizes (width across, depth front to back) of the preset buildings, as buildings.json.
PLANS = {"Inn": (13, 13), "Dwelling": (6, 5), "Cottage": (9, 6), "Smithy": (7, 5), "Shrine": (11, 7), "Woodshop": (7, 5),
         "TinkerShop": (7, 6), "Barn": (10, 6), "Cookhouse": (11, 10), "LandingInn": (13, 13), "MootHall": (13, 9)}

COLORS = {"g": (96, 150, 70), "d": (130, 100, 70), "r": (186, 160, 116), "t": (100, 70, 46), "k": (120, 116, 110),
          "s": (210, 196, 140), "~": (90, 160, 190), "w": (50, 120, 170)}
FEATURE_COLORS = {"O": (40, 90, 40), "P": (30, 70, 50), "B": (90, 130, 60), "b": (150, 40, 60), "x": (110, 140, 200),
                  "o": (140, 140, 140), "f": (90, 60, 30), "G": (200, 140, 60), "c": (120, 200, 90), "W": (60, 60, 200),
                  "=": (170, 170, 170), "N": (150, 110, 60), "u": (120, 80, 40), "X": (160, 120, 70), "C": (110, 70, 40), "/": (200, 200, 120)}


def front(facing):
    return {0: (0, 1), 1: (-1, 0), 2: (0, -1), 3: (1, 0)}[facing]


def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    t = 0 if dx == dy == 0 else max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


class Canvas:
    """An n x n map being painted: ground, level and feature grids, reserved plots, and the buildings."""

    def __init__(self, n, town, fill="g"):
        self.n = n
        self.town = town     # the village's level
        self.ground = [[fill] * n for _ in range(n)]
        self.level = [[town] * n for _ in range(n)]
        self.feature = [["."] * n for _ in range(n)]
        self.reserved = [[False] * n for _ in range(n)]   # building plots and their doorsteps: nothing grows there
        self.buildings = []

    def inside(self, x, y):
        return 0 <= x < self.n and 0 <= y < self.n

    def cells_rect(self, x0, y0, x1, y1):
        for y in range(max(0, y0), min(self.n - 1, y1) + 1):
            for x in range(max(0, x0), min(self.n - 1, x1) + 1):
                yield x, y

    def cells_disc(self, cx, cy, rx, ry, wobble=0.0, seed=0):
        r = random.Random(seed)
        phase = [r.uniform(0, math.tau) for _ in range(3)]
        for y in range(int(cy - ry - 2), int(cy + ry + 3)):
            for x in range(int(cx - rx - 2), int(cx + rx + 3)):
                if not self.inside(x, y):
                    continue
                a = math.atan2(y - cy, x - cx)
                k = 1 + wobble * sum(math.sin(a * (i + 2) + p) for i, p in enumerate(phase)) / 3
                if ((x - cx) / (rx * k)) ** 2 + ((y - cy) / (ry * k)) ** 2 <= 1:
                    yield x, y

    def cells_path(self, points, width):
        xs = [p[0] for p in points]
        ys = [p[1] for p in points]
        for y in range(max(0, int(min(ys) - width - 1)), min(self.n, int(max(ys) + width + 2))):
            for x in range(max(0, int(min(xs) - width - 1)), min(self.n, int(max(xs) + width + 2))):
                d = min(seg_dist(x, y, *points[i], *points[i + 1]) for i in range(len(points) - 1))
                if d <= width / 2:
                    yield x, y

    def paint(self, cells, g=None, lv=None):
        for x, y in cells:
            if g == "r" and self.level[y][x] != self.town:
                continue  # lanes stop at the banks: a bridge carries them over
            if g is not None:
                self.ground[y][x] = g
            if lv is not None:
                self.level[y][x] = lv

    def plot(self, name, ax, ay, facing):
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

    def building(self, name, ax, ay, facing, role="", variant="", level=None, doorstep="r"):
        """A preset building's plot, reserved with a clear cell round it; a doorstep of packed earth
        along its front (none on a deck: doorstep=None). level: it stands on a deck at that level."""
        cells = self.plot(name, ax, ay, facing)
        fx, fy = front(facing)
        for x, y in cells:
            self.reserved[y][x] = True
            if self.ground[y][x] in ("t",):
                self.ground[y][x] = "g"
        for x, y in cells:
            for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                if self.inside(nx, ny) and (nx, ny) not in cells:
                    self.reserved[ny][nx] = True
            sx, sy = x + fx, y + fy
            if doorstep and self.inside(sx, sy) and (sx, sy) not in cells and self.ground[sy][sx] == "g":
                self.ground[sy][sx] = doorstep
        entry = {"def": name, "at": [ax, ay], "facing": facing}
        if variant:
            entry["variant"] = variant
        if role:
            entry["role"] = role
        if level is not None:
            entry["level"] = level
        self.buildings.append(entry)

    def fence_ring(self, x0, y0, x1, y1, gate=None, locked=False):
        for x, y in self.cells_rect(x0, y0, x1, y1):
            if (x in (x0, x1) or y in (y0, y1)) and self.level[y][x] == self.town:
                self.feature[y][x] = "f"
        if gate:
            self.feature[gate[1]][gate[0]] = "G" if locked else "."

    def garden(self, x0, y0, x1, y1, gate):
        self.fence_ring(x0, y0, x1, y1, gate)
        for x, y in self.cells_rect(x0 + 1, y0 + 1, x1 - 1, y1 - 1):
            if self.level[y][x] != self.town:
                continue
            self.ground[y][x] = "t"
            if (y - y0) % 2 == 1 and not self.reserved[y][x]:
                self.feature[y][x] = "c"

    def water(self, cells, seed=0):
        """A river or lake: water two levels down, a one-cell stone bank one down round it."""
        cells = set(cells)
        town = self.town
        for x, y in cells:
            self.ground[y][x] = "w"
            self.level[y][x] = town - 2
            self.feature[y][x] = "."
        for x, y in cells:
            for dx in (-1, 0, 1):
                for dy in (-1, 0, 1):
                    nx, ny = x + dx, y + dy
                    if self.inside(nx, ny) and (nx, ny) not in cells and self.level[ny][nx] == town:
                        self.ground[ny][nx] = "k"
                        self.level[ny][nx] = town - 1
                        self.feature[ny][nx] = "."

    def free(self, x, y):
        """Open town ground: nothing reserved, built or growing there, not in the water."""
        return (self.inside(x, y) and not self.reserved[y][x] and self.level[y][x] == self.town
                and self.feature[y][x] == "." and self.ground[y][x] not in "wk~")

    def place_first(self, f, spots, what):
        """Put feature f on the first free spot of several, reserving it; stop the script if none is."""
        for x, y in spots:
            if self.free(x, y):
                self.feature[y][x] = f
                self.reserved[y][x] = True
                return x, y
        raise SystemExit(f"no room for {what}")

    def tree_clump(self, cx, cy, rx, ry, density, seed, species="O"):
        r = random.Random(seed)
        for x, y in self.cells_disc(cx, cy, rx, ry, wobble=0.25, seed=seed):
            if (r.random() < density and self.ground[y][x] == "g" and self.level[y][x] == self.town
                    and not self.reserved[y][x] and self.feature[y][x] == "."):
                pick = r.random()
                self.feature[y][x] = species if pick < 0.75 else ("B" if pick < 0.9 else ("P" if species == "O" else "O"))

    def map_def(self, name, label, description, ground_legend, feature_legend, center, stockpile, livestock, festival, depth=12, base_z=2):
        return {
            "maps": [{
                "name": name, "label": label,
                "description": description,
                "width": self.n, "height": self.n, "depth": depth, "baseZ": base_z,
                "groundLegend": ground_legend,
                "featureLegend": feature_legend,
                "center": center,
                "stockpile": stockpile,
                "livestock": livestock,
                "festival": festival,
                "buildings": self.buildings,
                "ground": ["".join(row) for row in self.ground],
                "levels": ["".join(str(v) for v in row) for row in self.level],
                "features": ["".join(row) for row in self.feature],
            }]
        }


def write(out, data, header):
    """Write a map def, one grid row per line so the file reads (and diffs) as a map. header: the
    comment lines at the top (where it came from, how it's made)."""
    os.makedirs(os.path.dirname(out), exist_ok=True)
    m = data["maps"][0]
    head = json.dumps({k: v for k, v in m.items() if k not in ("ground", "levels", "features", "buildings", "festival")}, indent=2)[:-2]
    rows = lambda key: ",\n".join(f'      "{r}"' for r in m[key])
    blds = ",\n".join("      " + json.dumps(b) for b in m["buildings"])
    fest = ",\n".join("      " + json.dumps(b) for b in m["festival"])
    text = (header +
            '{\n  "maps": [\n    ' + head.replace("\n", "\n    ") + ',\n'
            f'      "buildings": [\n{blds}\n      ],\n'
            f'      "festival": [\n{fest}\n      ],\n'
            f'      "ground": [\n{rows("ground")}\n      ],\n'
            f'      "levels": [\n{rows("levels")}\n      ],\n'
            f'      "features": [\n{rows("features")}\n      ]\n    }}\n  ]\n}}\n')
    with open(out, "w") as f:
        f.write(text)
    print(f"wrote {os.path.normpath(out)}")


def preview(c, path, reference=None, core=0, feature_colors=None, roof=None):
    """Draw the map (and the reference it was traced from, if any, beside it) to a PNG."""
    from PIL import Image, ImageDraw
    n, k = c.n, 8
    colors = feature_colors or FEATURE_COLORS
    img = Image.new("RGB", (n * k, n * k))
    d = ImageDraw.Draw(img)
    for y in range(n):
        for x in range(n):
            d.rectangle([x * k, y * k, x * k + k - 1, y * k + k - 1], fill=COLORS[c.ground[y][x]])
            f = c.feature[y][x]
            if f != ".":
                d.ellipse([x * k + 1, y * k + 1, x * k + k - 2, y * k + k - 2], fill=colors.get(f, (255, 0, 255)))
    for b in c.buildings:
        cells = c.plot(b["def"], b["at"][0], b["at"][1], b["facing"])
        xs, ys = [p[0] for p in cells], [p[1] for p in cells]
        fill = roof(b) if roof else (200, 168, 96) if b.get("variant") != "Shingle" and b["def"] != "Smithy" else (130, 80, 50)
        d.rectangle([min(xs) * k, min(ys) * k, max(xs) * k + k - 1, max(ys) * k + k - 1], fill=fill, outline=(40, 30, 20))
        fx, fy = front(b["facing"])
        cx, cy = b["at"][0] * k + k // 2, b["at"][1] * k + k // 2
        d.line([cx, cy, cx + fx * 3 * k, cy + fy * 3 * k], fill=(40, 30, 20), width=2)
        d.text((min(xs) * k + 2, min(ys) * k + 2), b.get("role") or b["def"], fill=(0, 0, 0))
    if reference and os.path.exists(reference):
        out = Image.new("RGB", (n * k * 2 + 8, n * k), (30, 30, 30))
        out.paste(img, (0, 0))
        side = (n - 2 * core) * k
        ref = Image.open(reference).convert("RGB").resize((side, side))
        out.paste(ref, (n * k + 8 + core * k, core * k))
        img = out
    img.save(path)
    print(f"preview: {path}")
