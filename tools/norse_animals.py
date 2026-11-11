"""Norse HD-2D wild animals: a red deer stag, a wild boar and a mountain hare, side on, facing right.

Each keeps the canvas, tags, frame counts and pivot of the placeholder sheet it replaces
(placeholder_sheets.ANIMALS: idle grazing, walk, sleep, downed), so nothing in the defs changes. Built
from shaded masks like the dragons: the far legs first and a step darker, then the body (coat ramp with
a paler belly and a fur pattern that stays put frame to frame), neck, head, the near legs over it, and a
selective outline. Saved as is (no grade).
"""
import math

from PIL import Image

from norse_art import ramp, mask_of, shade_shape, shade_over, selective_outline, bayer, ribbon
from placeholder_sheets import Sequencer, write_sheet

EYE = (24, 18, 20, 255)
GLINT = (236, 232, 222, 255)
BONE = ramp((214, 198, 166), 4, main=2, dark=0.6, light=1.12)
HOOF = (46, 36, 34, 255)

KINDS = {
    "deer": dict(canvas=(40, 36), body=(8, 13, 29, 23), leg=12, legw=1.7, hips=(11, 15, 22, 26),
                 neck=((26, 16), (30, 11)), head=(28, 6, 35, 12), snout=((34, 9), (38, 11), (34, 12)),
                 coat=ramp((150, 92, 58), 6, main=3, dark=0.45, light=1.32, cool=18, warm=12),
                 belly=ramp((214, 190, 150), 4, main=2, dark=0.7, light=1.08), muzzle=(62, 46, 42, 255),
                 ear=((29, 7), (27, 3), (31, 6)), antlers=True, tail=((7, 14), (5, 17))),
    "boar": dict(canvas=(36, 26), body=(4, 7, 28, 20), leg=6, legw=3.0, hips=(7, 11, 21, 25),
                 neck=None, head=(23, 8, 32, 18), snout=((30, 12), (35, 15), (34, 18), (29, 18)),
                 coat=ramp((82, 66, 58), 6, main=3, dark=0.5, light=1.5, cool=16, warm=14),
                 belly=ramp((108, 90, 78), 4, main=2, dark=0.7, light=1.2), muzzle=(150, 112, 104, 255),
                 ear=((25, 9), (24, 5), (27, 8)), tusks=True, bristles=True, tail=((4, 10), (2, 13))),
    "hare": dict(canvas=(20, 16), body=(2, 6, 14, 13), leg=2, legw=2.0, hips=(4, 6, 11, 12),
                 neck=None, head=(11, 3, 17, 9), snout=((16, 6), (19, 7), (16, 8)),
                 coat=ramp((150, 138, 120), 5, main=2, dark=0.55, light=1.3, cool=16, warm=10),
                 belly=ramp((232, 226, 214), 3, main=1, dark=0.85, light=1.04), muzzle=(110, 96, 86, 255),
                 ear=((12, 4), (10, -1), (13, -1), (14, 4)), long_ears=True, tail=((2, 8), (1, 10))),
}


def fur(x, y):
    """A fixed coat pattern: short combed strokes, the same on every frame."""
    return 0.18 if (x * 2 + y * 3) % 7 == 0 else -0.14 if (x * 2 + y * 3) % 7 == 3 else 0


def leg(img, hip, foot, width, coat, far, kind, bend=1.0):
    """A leg from hip to hoof with a knee/hock kink, thick at the thigh, thin at the cannon."""
    knee = (hip[0] + (foot[0] - hip[0]) * 0.45 + (1.4 if kind == "deer" else 0.6) * bend, hip[1] + (foot[1] - hip[1]) * 0.5)
    m = mask_of(img.size, lambda d: d.polygon(ribbon([hip, knee, foot], [width + 1.2, width * 0.75, width * 0.6], steps=4), fill=255))
    shade_over(img, m, coat[:-1] if far else coat[1:], coat[0], radius=0.6, slope=1.2, bias=-0.15 if far else 0.05)
    img.putpixel((int(round(foot[0])), int(round(foot[1]))), HOOF)
    if 0 <= int(round(foot[0])) + 1 < img.width:
        img.putpixel((int(round(foot[0])) + 1, int(round(foot[1]))), HOOF)


def frame(kind, stride=0, bob=0, head_down=0, lying=False, dead=False):
    k = KINDS[kind]
    w, h = k["canvas"]
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    x0, y0, x1, y1 = k["body"]
    drop = (h - 2 - y1) if (lying or dead) else 0
    dy = drop + bob
    y0, y1 = y0 + dy, y1 + dy
    hx0, hy0, hx1, hy1 = k["head"]
    hdy = dy + head_down + ((h - 3 - hy1 - dy) if dead else (3 if lying and kind != "hare" else 0))
    coat, belly = k["coat"], k["belly"]
    foot = h - 1
    hips = [(x, y1 - 2) for x in k["hips"]]

    # far legs (the second of each pair), then the body, then the near legs
    if dead:
        for i, (hx, hy) in enumerate(hips):
            if i % 2:
                leg(img, (hx, hy), (hx + 4 + i, h - 1), k["legw"], coat, True, kind, bend=0)
    elif not lying:
        for i, (hx, hy) in enumerate(hips):
            if i % 2:
                s = -stride if i < 2 else stride
                leg(img, (hx, hy), (hx + s, foot), k["legw"], coat, True, kind)

    body = mask_of(img.size, lambda d: d.ellipse([x0, y0, x1, y1], fill=255))
    if k.get("bristles"):                                      # the boar's high, bristled shoulders
        body = mask_of(img.size, lambda d: (d.ellipse([x0, y0, x1, y1], fill=255),
                                            d.ellipse([x0 + (x1 - x0) // 2, y0 - 3, x1 - 1, y0 + (y1 - y0) // 2 + 2], fill=255)))
    shade_shape(img, body, coat, radius=1.8, slope=1.2, depth=0.4, tex=fur)
    bm = mask_of(img.size, lambda d: d.chord([x0 + 2, y0 + (y1 - y0) // 2, x1 - 2, y1], 0, 180, fill=255))
    px, bmp = img.load(), bm.load()
    for y in range(h):
        for x in range(w):
            if bmp[x, y] and px[x, y][3]:
                px[x, y] = belly[0] if y >= y1 - 1 else belly[1 if (x + y) % 5 else 0] if y > y0 + (y1 - y0) * 0.75 else belly[min(len(belly) - 1, 2)]
    if kind == "deer":                                         # the pale rump patch
        for y in range(y0 + 2, y0 + 7):
            for x in range(x0, x0 + 4):
                if px[x, y][3]:
                    px[x, y] = belly[2]
    if k.get("bristles"):
        for bx in range(x0 + 4, x1 - 1, 2):
            top = next((y for y in range(h) if px[bx, y][3]), None)
            if top is not None and top > 0:
                px[bx, top - 1] = coat[0]
                if top > 1 and bx % 4 == 0:
                    px[bx, top - 2] = coat[1]
    (tx0, ty0), (tx1, ty1) = k["tail"]
    tm = mask_of(img.size, lambda d: d.line([(tx0, ty0 + dy), (tx1, ty1 + dy)], fill=255, width=2))
    shade_over(img, tm, belly if kind != "boar" else coat, coat[0], radius=0.5)

    if k["neck"] and not dead:
        (nx0, ny0), (nx1, ny1) = k["neck"]
        nm = mask_of(img.size, lambda d: d.polygon(ribbon([(nx0, ny0 + dy), (nx1, ny1 + hdy)], [7, 5], steps=3), fill=255))
        shade_over(img, nm, coat, coat[0], radius=1.2, slope=1.2, tex=fur)
        for t in range(4):                                     # the stag's darker throat mane
            put(img, nx1 - 1 + t // 2, ny1 + hdy + 2 + t, coat[1])
    hm = mask_of(img.size, lambda d: d.ellipse([hx0, hy0 + hdy, hx1, hy1 + hdy], fill=255))
    shade_over(img, hm, coat, coat[0], radius=1.0, slope=1.3, tex=fur)
    sn = [(x, y + hdy) for x, y in k["snout"]]
    shade_over(img, mask_of(img.size, lambda d: d.polygon(sn, fill=255)), coat[1:], coat[0], radius=0.6, slope=1.3)
    put(img, sn[1][0], sn[1][1], k["muzzle"])
    put(img, sn[1][0] - 1, sn[1][1], k["muzzle"])
    ear = [(x, y + hdy) for x, y in k["ear"]]
    shade_over(img, mask_of(img.size, lambda d: d.polygon(ear, fill=255)), coat, coat[0], radius=0.5, slope=1.3)
    if k.get("long_ears"):                                     # the hare's black ear tips
        put(img, ear[1][0], ear[1][1], EYE); put(img, ear[2][0], ear[2][1], EYE)
    ex, ey = hx0 + (hx1 - hx0) // 2 + 1, hy0 + hdy + 2
    if dead:
        put(img, ex, ey, EYE)
        put(img, ex - 1, ey, coat[1])
    elif lying and kind != "hare":
        put(img, ex, ey, coat[0]); put(img, ex + 1, ey, coat[0])   # asleep: a closed lid
    else:
        put(img, ex, ey, EYE)
        put(img, ex, ey - 1, GLINT) if kind == "hare" else put(img, ex - 1, ey - 1, GLINT)
    if k.get("antlers") and not dead:
        ax, ay = hx0 + 2, hy0 + hdy
        tines = [((ax, ay), (ax - 3, ay - 3)), ((ax - 3, ay - 3), (ax - 5, ay - 6)), ((ax - 3, ay - 3), (ax - 1, ay - 6)),
                 ((ax - 5, ay - 6), (ax - 8, ay - 7)), ((ax - 5, ay - 6), (ax - 5, ay - 9)), ((ax, ay), (ax + 2, ay - 2))]
        for a, b in tines:
            n = max(abs(b[0] - a[0]), abs(b[1] - a[1]))
            for s in range(n + 1):
                put(img, round(a[0] + (b[0] - a[0]) * s / max(1, n)), round(a[1] + (b[1] - a[1]) * s / max(1, n)), BONE[2 if s < n else 3])
    if k.get("tusks"):
        put(img, sn[2][0] - 2, sn[2][1] - 2, BONE[3]); put(img, sn[2][0] - 1, sn[2][1] - 3, BONE[3]); put(img, sn[2][0] - 1, sn[2][1] - 4, BONE[2])

    if dead:
        for i, (hx, hy) in enumerate(hips):
            if not i % 2:
                leg(img, (hx, hy + dy - drop), (hx + 5 + i, h - 2), k["legw"], coat, False, kind, bend=0)
    elif lying:
        for i, (hx, hy) in enumerate(hips):                     # legs folded under, a hoof showing
            if not i % 2:
                put(img, hx + 2, h - 2, coat[1]); put(img, hx + 3, h - 2, HOOF)
    else:
        for i, (hx, hy) in enumerate(hips):
            if not i % 2:
                s = stride if i < 2 else -stride
                leg(img, (hx, hy), (hx + s, foot), k["legw"], coat, False, kind)
    return selective_outline(img, keep=(GLINT,))


def put(img, x, y, c):
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((int(x), int(y)), c)


def sheet(kind):
    w, h = KINDS[kind]["canvas"]
    seq = Sequencer()
    seq.add("idle", [frame(kind), frame(kind, head_down=3), frame(kind, head_down=4), frame(kind, head_down=3)], 500)
    seq.add("walk", [frame(kind, stride=s, bob=b) for s, b in ((2, 0), (0, -1), (-2, 0), (0, -1))], 110)
    seq.add("sleep", [frame(kind, lying=True), frame(kind, lying=True, bob=1)], 1200)
    seq.add("downed", [frame(kind, dead=True)], 1000, loop=False)
    return seq, (w // 2, h - 1)


def generate():
    for kind in KINDS:
        seq, pivot = sheet(kind)
        write_sheet("creatures/" + kind, seq.frames, seq.tags, pivot)


if __name__ == "__main__":
    generate()
