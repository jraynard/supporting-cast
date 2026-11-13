#!/usr/bin/env python3
"""Heroes Not Included: emblem, logo lockups and palette (art/brand/).

The emblem is the tavern sign over the door of the people a hero walks past: an oak signboard in an
iron rim, a hammer and a chef's knife crossed where a crest would carry swords, an innkeeper's tankard, and
the quest marker ("!") that every adventurer reads as "this one has work for me". You are the NPC.

Everything is vector. The wordmark is converted to outlines, so the SVGs need no fonts installed.

    pip install fonttools
    python tools/brand_logo.py --fonts path/to/fonts          # Cinzel[wght].ttf (SIL OFL)

Writes art/brand/: the SVGs, emblem.png (via headless Edge/Chrome), palette.json and brand-guide.html. The palette is the single source of truth: it starts from
Ui/Theme.cs (oak, iron, bone, ember) so the logo sits in the game's own chrome.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "art" / "brand"

# ---- palette --------------------------------------------------------------------------------------
# name -> (hex, role). Oak, iron, bone and ember are Theme.cs's own colours, snapped to hex.
PALETTE = {
    "cellar":      ("#140E0A", "Outlines, the deepest shadow, text on bone. Theme.Well."),
    "oak_deep":    ("#261B13", "Panels and the dark ground. Theme.Panel."),
    "oak":         ("#4A3322", "The signboard: the brand's main surface."),
    "oak_light":   ("#7A5434", "Plank trim, the hammer's haft, wood highlights."),
    "barrel":      ("#C99A5B", "Pale oak: tankard and anything that must pop off dark wood."),
    "iron_dark":   ("#50545C", "Iron shade and rim bevels."),
    "iron":        ("#808590", "The rim, hoops, hammer head. Theme.Iron."),
    "iron_light":  ("#B4B9C1", "Steel: the knife blade and metal highlights."),
    "bone":        ("#F2E6CF", "Foam, lettering, UI text. Theme.Text."),
    "parchment":   ("#D9C49B", "Notices, ribbons' paper, foam shading, secondary text."),
    "ember":       ("#FCA342", "The accent: the quest marker, the hearth, calls to action. Theme.Accent."),
    "ale":         ("#D98A26", "Warm amber: ale and gold highlights."),
    "hearth_red":  ("#B5432B", "The 'Not Included' ribbon, wax seals, warnings."),
    "moss":        ("#6F8F47", "Hops and herbs; Good states, the village."),
    "rumour":      ("#A894FF", "Whispers, intel and the healer's gift. Theme.Arcane."),
}
P = {k: v[0] for k, v in PALETTE.items()}
RED_DEEP = "#8A2F1E"  # the folded back of the ribbon; not a palette colour, only a shade


def rel_lum(h: str) -> float:
    def ch(v: int) -> float:
        v /= 255
        return v / 12.92 if v <= 0.03928 else ((v + 0.055) / 1.055) ** 2.4
    r, g, b = (int(h[i:i + 2], 16) for i in (1, 3, 5))
    return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b)


def contrast(a: str, b: str) -> float:
    la, lb = sorted((rel_lum(a), rel_lum(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)


# ---- the emblem -----------------------------------------------------------------------------------
# Roles -> colours. The mono variants drive a mask: white paints, black knocks out.
COLOUR = dict(
    line=P["cellar"], rim=P["iron"], bevel=P["iron_dark"], field=P["oak"],
    plank=P["oak_deep"], grain="#5A4030", trim=P["oak_light"],
    wood=P["oak_light"], wood_line=P["oak_deep"],
    iron=P["iron"], iron_dark=P["iron_dark"], iron_light=P["iron_light"], blade_line=P["iron"], handle_rivet=P["iron_light"], rune_hi=P["iron_light"], rune_cut=P["cellar"], rim_sheen=P["iron_light"],
    barrel=P["barrel"], barrel_line="#A87B42", foam=P["bone"], foam_shade=P["parchment"],
    mark=P["ember"], flame=P["ember"], flame_core=P["bone"],
)
MONO = {k: "#000" for k in COLOUR}
MONO.update(rim="#fff", bevel="#000", iron="#fff", iron_light="#fff", iron_dark="#000",
            wood="#fff", barrel="#fff", foam="#fff", mark="#fff", flame_core="#fff", trim="#fff",
            foam_shade="#000", wood_line="#000", barrel_line="#000", blade_line="#000", handle_rivet="#000", rune_hi="#fff", rune_cut="#000", rim_sheen="#fff")

# The 24 runes of the Elder Futhark, as strokes in a box about 8 wide and 12 tall (up is -y). They run
# round the rim like an inscription, each one standing upright as seen from outside the sign.
RUNES = (
    "M-3 6V-6M-3 -2L3 -6M-3 2L3 -2",              # fehu
    "M-3 6V-6L3 -1V6",                            # uruz
    "M-3 -6V6M-3 -3L3 0L-3 3",                    # thurisaz
    "M-3 -6V6M-3 -6L3 -2M-3 -1L3 3",              # ansuz
    "M-3 -6V6M-3 -6L3 -3L-3 0L3 6",               # raido
    "M3 -5L-3 0L3 5",                             # kenaz
    "M-4 -5L4 5M4 -5L-4 5",                       # gebo
    "M-3 -6V6M-3 -6L3 -3L-3 0",                   # wunjo
    "M-3 -6V6M3 -6V6M-3 -1L3 1",                  # hagalaz
    "M0 -6V6M-4 -2L4 2",                          # nauthiz
    "M0 -6V6",                                    # isa
    "M-1 -6L-4 -3L-1 0M1 0L4 3L1 6",              # jera
    "M0 -6V6M0 -6L-3 -3M0 6L3 3",                 # eihwaz
    "M-3 -6V6M-3 -6L3 -3V3L-3 6",                 # perthro
    "M0 6V-6M-4 -6L0 -1L4 -6",                    # algiz
    "M3 -6L-3 -2L3 2L-3 6",                       # sowilo
    "M0 6V-6M-4 -2L0 -6L4 -2",                    # tiwaz
    "M-3 -6V6M-3 -6L3 -3L-3 0L3 3L-3 6",          # berkano
    "M-4 6V-6L0 -1L4 -6V6",                       # ehwaz
    "M-4 -6V6M4 -6V6M-4 -6L4 0M4 -6L-4 0",        # mannaz
    "M-3 6V-6L3 -2",                              # laguz
    "M0 -6L4 0L0 6L-4 0Z",                        # ingwaz
    "M-4 -6L4 6V-6L-4 6Z",                        # dagaz
    "M0 -6L4 -2L0 2L-4 -2ZM0 2L-4 6M0 2L4 6",     # othala
)

LW = 7  # outline weight: the sign's lettering and shapes read like the game's inked sprites


def emblem_group(c: dict, uid: str) -> str:
    """The 512x512 roundel. `uid` keeps clip ids unique when several are inlined in one page."""
    L = c["line"]
    out = [f'<defs><clipPath id="{uid}-field"><circle cx="256" cy="256" r="212"/></clipPath>'
           f'<clipPath id="{uid}-mug"><path d="{MUG}"/></clipPath></defs>']

    # signboard: iron rim, oak field, planks
    out += [f'<circle cx="256" cy="256" r="244" fill="{L}"/>',
            f'<circle cx="256" cy="256" r="238" fill="{c["rim"]}"/>',
            f'<circle cx="256" cy="256" r="219" fill="{c["bevel"]}"/>',
            f'<circle cx="256" cy="256" r="216" fill="{L}"/>',
            f'<circle cx="256" cy="256" r="212" fill="{c["field"]}"/>']
    out.append(f'<g clip-path="url(#{uid}-field)">')
    for x in (129, 214, 299, 383):
        out.append(f'<path d="M{x} 40V472" stroke="{c["plank"]}" stroke-width="4" fill="none"/>')
    for x, y, w in ((92, 120, 36), (176, 400, 30), (262, 70, 26), (340, 440, 34), (420, 180, 28), (150, 250, 22)):
        out.append(f'<path d="M{x} {y}h{w}" stroke="{c["grain"]}" stroke-width="3" stroke-linecap="round" fill="none"/>')
    out.append('</g>')
    out.append(f'<circle cx="256" cy="256" r="198" fill="none" stroke="{c["trim"]}" stroke-width="4"/>')
    # engraved runes: light catches the lower lip of each cut (light from the top left), the groove
    # is dark, and a thin bright core keeps a wide cut from reading as paint
    out.append(f'<path d="M35.6 175.8A234.5 234.5 0 0 1 175.8 35.6" fill="none" stroke="{c["rim_sheen"]}" stroke-width="3" '
               f'stroke-linecap="round" opacity=".6"/>')
    pose = "translate(256 256) rotate({}) translate(0 -228.5)"
    cut = 'fill="none" stroke-linecap="round" stroke-linejoin="round"'
    out.append(f'<g transform="translate(0.9 1.3)" stroke="{c["rune_hi"]}" stroke-width="3.4" {cut}>'
               + "".join(f'<path transform="{pose.format(15 * i)}" d="{d}"/>' for i, d in enumerate(RUNES)) + '</g>')
    out.append(f'<g stroke="{c["rune_cut"]}" stroke-width="3.2" {cut}>'
               + "".join(f'<path transform="{pose.format(15 * i)}" d="{d}"/>' for i, d in enumerate(RUNES)) + '</g>')
    out.append(f'<g transform="translate(-0.5 -0.6)" stroke="{c["bevel"]}" stroke-width="1" stroke-opacity=".9" {cut}>'
               + "".join(f'<path transform="{pose.format(15 * i)}" d="{d}"/>' for i, d in enumerate(RUNES)) + '</g>')

    # crossed tools behind the tankard
    out.append(f'<g transform="rotate(-40 256 290)" stroke="{L}" stroke-width="{LW}" stroke-linejoin="round">'
               f'<rect x="246" y="140" width="20" height="312" rx="7" fill="{c["wood"]}"/>'
               f'<path d="M246 404h20M246 422h20" stroke="{c["wood_line"]}" stroke-width="4" fill="none"/>'
               f'<g transform="translate(256 135) scale(.86) translate(-256 -135)" stroke-width="{LW / .86:.2f}">'
               f'<path d="M188 106H302Q316 106 316 120V150Q316 164 302 164H188L210 135Z" fill="{c["iron"]}"/>'
               f'<path d="M220 114H302" stroke="{c["iron_light"]}" stroke-width="6" stroke-linecap="round" fill="none"/>'
               f'<path d="M220 155H304" stroke="{c["iron_dark"]}" stroke-width="5" stroke-linecap="round" fill="none"/></g></g>')
    # chef's knife: pivots lower than the hammer so its broad heel clears the foam
    out.append(f'<g transform="rotate(40 256 310)" stroke="{L}" stroke-width="{LW}" stroke-linejoin="round">'
               f'<rect x="244" y="214" width="24" height="234" rx="10" fill="{c["wood"]}"/>'
               + "".join(f'<circle cx="256" cy="{y}" r="3.5" fill="{c["handle_rivet"]}" stroke-width="2.5"/>' for y in (422,))
               + f'<path d="M282 220V124Q283 104 277 92C244 100 226 156 226 220Z" fill="{c["iron_light"]}"/>'
               f'<path d="M267 134V200" stroke="{c["blade_line"]}" stroke-width="4" stroke-linecap="round" fill="none"/>'
               f'<rect x="224" y="212" width="60" height="14" rx="4" fill="{c["iron"]}"/></g>')

    # the tankard: handle, staves, hoops, a flame for the hearth
    out.append(f'<path d="M310 284C368 280 376 374 304 372" fill="none" stroke="{L}" stroke-width="33" stroke-linecap="round"/>'
               f'<path d="M310 284C368 280 376 374 304 372" fill="none" stroke="{c["iron"]}" stroke-width="19" stroke-linecap="round"/>'
               f'<path d="M326 294C356 296 362 340 332 352" fill="none" stroke="{c["iron_light"]}" stroke-width="4" stroke-linecap="round"/>')
    out.append(f'<path d="{MUG}" fill="{c["barrel"]}" stroke="{L}" stroke-width="{LW}" stroke-linejoin="round"/>')
    out.append(f'<g clip-path="url(#{uid}-mug)">')
    for k in (1, 2, 3):
        out.append(f'<path d="M{196 + 120 * k / 4:.1f} 258L{203 + 106 * k / 4:.1f} 398" stroke="{c["barrel_line"]}" stroke-width="3.5" fill="none"/>')
    for y in (290, 358):
        out.append(f'<rect x="180" y="{y}" width="150" height="17" fill="{c["iron"]}" stroke="{L}" stroke-width="4.5"/>'
                   f'<rect x="182" y="{y + 3}" width="146" height="4" fill="{c["iron_light"]}"/>')
    out.append('</g>')
    out.append(f'<g transform="translate(0 7)"><path d="M256 303C259 316 275 322 275 336C275 346 267 351 256 351C245 351 237 346 237 336C237 326 245 322 247 311C250 317 253 317 253 314C252 310 253 306 256 303Z" '
               f'fill="{c["flame"]}" stroke="{L}" stroke-width="4" stroke-linejoin="round"/>'
               f'<path d="M256 326C259 331 264 333 264 339C264 343 261 345 256 345C251 345 248 343 248 339C248 335 252 333 256 326Z" fill="{c["flame_core"]}"/></g>')

    # foam: outline pass then fill pass, so the lumps merge into one cloud
    foam = [(210, 257, 22), (230, 238, 28), (258, 228, 32), (286, 238, 28), (306, 257, 22), (258, 256, 26),
            (203, 283, 9), (312, 288, 10)]
    out.append(f'<g fill="{L}" stroke="{L}" stroke-width="14" stroke-linejoin="round">'
               + "".join(f'<circle cx="{x}" cy="{y}" r="{r}"/>' for x, y, r in foam)
               + f'<ellipse cx="312" cy="294" rx="10" ry="16"/></g>')
    out.append(f'<g fill="{c["foam"]}">' + "".join(f'<circle cx="{x}" cy="{y}" r="{r}"/>' for x, y, r in foam)
               + '<ellipse cx="312" cy="294" rx="10" ry="16"/></g>')
    out.append(f'<g fill="none" stroke="{c["foam_shade"]}" stroke-width="3.5" stroke-linecap="round">'
               f'<circle cx="244" cy="224" r="8"/><circle cx="276" cy="246" r="5.5"/><circle cx="294" cy="226" r="4.5"/></g>')

    # the quest marker
    out.append(f'<g transform="translate(0 -8)" fill="{c["mark"]}" stroke="{L}" stroke-width="{LW}" stroke-linejoin="round">'
               f'<path d="M239 74H273L268 138Q267 146 260 146H252Q245 146 244 138Z"/><circle cx="256" cy="168" r="15"/></g>')
    out.append(f'<path d="M250 78L248 112" stroke="{P["bone"] if c is COLOUR else "#000"}" stroke-opacity="{0.7 if c is COLOUR else 1}" '
               f'stroke-width="4" stroke-linecap="round" fill="none"/>')
    return "".join(out)


MUG = "M196 258H316L309 388Q308 398 298 398H214Q204 398 203 388Z"


def emblem_svg() -> str:
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512" role="img" '
            f'aria-label="Heroes Not Included emblem">{emblem_group(COLOUR, "e")}</svg>\n')


def emblem_mono_svg(ink: str, uid: str = "m") -> str:
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512" role="img" '
            f'aria-label="Heroes Not Included emblem, one colour"><defs><mask id="{uid}-mask" maskUnits="userSpaceOnUse" x="0" y="0" width="512" height="512">'
            f'<rect width="512" height="512" fill="#000"/>{emblem_group(MONO, uid)}</mask></defs>'
            f'<rect width="512" height="512" fill="{ink}" mask="url(#{uid}-mask)"/></svg>\n')


# ---- lettering ------------------------------------------------------------------------------------
def load_font(path: Path, weight: float):
    from fontTools.ttLib import TTFont
    from fontTools.varLib import instancer
    f = TTFont(str(path))
    if "fvar" in f:
        f = instancer.instantiateVariableFont(f, {"wght": weight})
    return f


def text_path(font, text: str, size: float, tracking: float = 0.0):
    """Outline `text` at `size` px. Returns (path d, width, cap height); baseline at y=0."""
    from fontTools.pens.svgPathPen import SVGPathPen
    from fontTools.pens.transformPen import TransformPen
    gs, cmap = font.getGlyphSet(), font.getBestCmap()
    s = size / font["head"].unitsPerEm
    ntos = lambda v: f"{v:.1f}".rstrip("0").rstrip(".")
    x, parts = 0.0, []
    for ch in text:
        g = gs[cmap[ord(ch)]]
        pen = SVGPathPen(gs, ntos=ntos)
        g.draw(TransformPen(pen, (s, 0, 0, -s, x, 0)))
        parts.append(pen.getCommands())
        x += g.width * s + tracking
    return " ".join(p for p in parts if p), x - tracking, font["OS/2"].sCapHeight * s


def title_plate(d: str, fill: str, line: str = P["cellar"], stroke: float = 12, depth: float = 8) -> str:
    """Carved, extruded lettering: a dark block underneath, the face on top."""
    return (f'<path d="{d}" transform="translate(0 {depth})" fill="{line}" stroke="{line}" stroke-width="{stroke}" stroke-linejoin="round"/>'
            f'<path d="{d}" fill="{line}" stroke="{line}" stroke-width="{stroke}" stroke-linejoin="round"/>'
            f'<path d="{d}" fill="{fill}"/>')


def ribbon(width: float, height: float, font, text: str, size: float, tracking: float) -> str:
    """The 'Not Included' ribbon, like a label stuck on the box."""
    d, tw, cap = text_path(font, text, size, tracking)
    tail, fold = 74, 16
    L, R = P["cellar"], P["hearth_red"]
    y0 = fold
    body = f'M{tail - 10} {y0}H{width - tail + 10}V{y0 + height}H{tail - 10}Z'
    lt = f'M0 {y0 - 4 + fold}H{tail}V{y0 + height + fold}H0L{tail * 0.3:.0f} {y0 + (height + 2 * fold - 4) / 2:.0f}Z'
    rt = f'M{width} {y0 - 4 + fold}H{width - tail}V{y0 + height + fold}H{width}L{width - tail * 0.3:.0f} {y0 + (height + 2 * fold - 4) / 2:.0f}Z'
    return (f'<g stroke="{L}" stroke-width="6" stroke-linejoin="round">'
            f'<path d="{lt}" fill="{RED_DEEP}"/><path d="{rt}" fill="{RED_DEEP}"/>'
            f'<path d="{body}" fill="{R}"/></g>'
            f'<path d="M{tail} {y0 + 7}H{width - tail}" stroke="{P["ember"]}" stroke-opacity=".55" stroke-width="3"/>'
            f'<path d="M{tail} {y0 + height - 7}H{width - tail}" stroke="{P["cellar"]}" stroke-opacity=".35" stroke-width="3"/>'
            f'<g transform="translate({(width - tw) / 2:.1f} {y0 + (height + cap) / 2:.1f})">'
            f'<path d="{d}" transform="translate(0 3)" fill="{L}"/><path d="{d}" fill="{P["bone"]}"/></g>')


def chain(x: float, y0: float, y1: float) -> str:
    """Two iron strands that hang the sign from its bracket."""
    out, y, i = [], y0, 0
    while y < y1 - 2:
        if i % 2 == 0:
            out.append(f'<ellipse cx="{x}" cy="{y + 13}" rx="8" ry="14" fill="none" stroke="{P["cellar"]}" stroke-width="11"/>'
                       f'<ellipse cx="{x}" cy="{y + 13}" rx="8" ry="14" fill="none" stroke="{P["iron"]}" stroke-width="5"/>')
        else:
            out.append(f'<rect x="{x - 3}" y="{y + 1}" width="6" height="24" rx="3" fill="{P["iron_light"]}" stroke="{P["cellar"]}" stroke-width="3"/>')
        y += 20
        i += 1
    return "".join(out)


def bracket(x0: float, x1: float, y: float) -> str:
    L = P["cellar"]
    out = [f'<rect x="{x0}" y="{y}" width="{x1 - x0}" height="20" rx="10" fill="{P["iron"]}" stroke="{L}" stroke-width="6"/>',
           f'<path d="M{x0 + 14} {y + 6}H{x1 - 14}" stroke="{P["iron_light"]}" stroke-width="4" stroke-linecap="round"/>']
    for x in (x0, x1):
        out.append(f'<circle cx="{x}" cy="{y + 10}" r="19" fill="{P["iron"]}" stroke="{L}" stroke-width="6"/>'
                   f'<circle cx="{x}" cy="{y + 10}" r="7" fill="{L}"/>')
    return "".join(out)


def logo_stacked(font) -> str:
    W, H = 880, 1030
    d, tw, cap = text_path(font, "HEROES", 190, 14)
    k = 660 / tw
    rib_w = 700
    sign_x, sign_y = (W - 512) / 2, 150
    cap_h = cap * k
    base = sign_y + 512 + 40 + cap_h
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-label="Heroes Not Included">',
             bracket(150, W - 150, 70),
             chain(W / 2 - 150, 92, sign_y + 70), chain(W / 2 + 150, 92, sign_y + 70),
             f'<g transform="translate({sign_x} {sign_y})">{emblem_group(COLOUR, "ls")}</g>',
             f'<g transform="translate({(W - tw * k) / 2:.1f} {base:.1f}) scale({k:.4f})">{title_plate(d, P["bone"], stroke=12 / k, depth=8 / k)}</g>',
             f'<g transform="translate({(W - rib_w) / 2} {base + 34})">{ribbon(rib_w, 74, font, "NOT INCLUDED", 46, 9)}</g>',
             '</svg>\n']
    return "".join(parts)


def logo_horizontal(font) -> str:
    W, H = 1280, 520
    es = 440 / 512
    d, tw, cap = text_path(font, "HEROES", 190, 14)
    k = 720 / tw
    tx, rib_w = 520, 720
    cap_h = cap * k
    top = 260 - (cap_h + 40 + 106) / 2
    base = top + cap_h
    return "".join([
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" role="img" aria-label="Heroes Not Included">',
        f'<g transform="translate(40 40) scale({es:.4f})">{emblem_group(COLOUR, "lh")}</g>',
        f'<g transform="translate({tx} {base:.1f}) scale({k:.4f})">{title_plate(d, P["bone"], stroke=12 / k, depth=8 / k)}</g>',
        f'<g transform="translate({tx} {base + 40:.1f})">{ribbon(rib_w, 74, font, "NOT INCLUDED", 46, 9)}</g>',
        '</svg>\n'])


GROUPS = (
    ("Wood and iron", "The sign itself. The game's chrome is built from these.",
     ("cellar", "oak_deep", "oak", "oak_light", "barrel", "iron_dark", "iron", "iron_light")),
    ("Paper and flame", "Lettering, notices and the one bright thing on any screen.",
     ("bone", "parchment", "ember", "ale", "hearth_red")),
    ("The village", "Small doses: status, herbs and the whisper of intel.", ("moss", "rumour")),
)


def label(k: str) -> str:
    return k.replace("_", " ").title()


GUIDE_CSS = """
:root{--cellar:@cellar;--deep:@oak_deep;--oak:@oak;--oakl:@oak_light;--bone:@bone;--parch:@parchment;--ember:@ember;--red:@hearth_red;--iron:@iron}
*{box-sizing:border-box}
html{scroll-behavior:smooth}
body{margin:0;background:var(--deep);color:var(--bone);font:17px/1.6 "Fira Sans",system-ui,sans-serif}
main{max-width:1080px;margin:0 auto;padding:40px 16px 112px}
h1,h2,h3,h4{font-family:Cinzel,Georgia,serif;margin:0;letter-spacing:.03em}
h2{font-size:1.75rem;margin:84px 0 6px;color:var(--ember);scroll-margin-top:16px}
h3{font-size:1.05rem;margin:30px 0 2px;color:var(--parch)}
p{margin:0 0 12px}
.lede{margin:0 0 18px;color:var(--parch);max-width:68ch}
nav{display:flex;flex-wrap:wrap;gap:6px 18px;margin:0 0 36px;font-size:.9rem}
nav a{color:var(--parch);text-decoration:none;border-bottom:1px solid var(--oakl)} nav a:hover{color:var(--ember);border-color:var(--ember)}
.hero{display:grid;gap:32px;align-items:center;grid-template-columns:minmax(0,1fr)}
@media(min-width:760px){.hero{grid-template-columns:1.1fr .9fr}}
.hero img{width:100%;height:auto;display:block}
.kicker{font:600 .8rem Cinzel,Georgia,serif;letter-spacing:.2em;color:var(--parch);margin:0 0 10px}
.big{font:800 clamp(1.7rem,4.5vw,2.5rem)/1.15 Cinzel,Georgia,serif;margin:0 0 16px;color:var(--bone)}
.big em{color:var(--ember);font-style:normal}
.card{margin:0;border:2px solid var(--cellar);border-radius:8px;overflow:hidden;background:var(--cellar)}
.card .pad{padding:24px;display:grid;place-items:center;min-width:0}
.card figcaption{padding:10px 14px;background:var(--cellar);font-size:.85rem;color:var(--parch)}
.card img{max-width:100%;height:auto;display:block}
.grid{display:grid;gap:16px;grid-template-columns:repeat(auto-fit,minmax(240px,1fr))}
.panel{background:var(--cellar);border-radius:8px;padding:18px 20px}
.panel h4{font:600 1.02rem "Fira Sans",sans-serif;color:var(--ember);margin:0 0 6px;letter-spacing:0}
.panel p{margin:0;font-size:.95rem}
.num{font:800 1.5rem Cinzel,Georgia,serif;color:var(--oakl);display:block;margin-bottom:2px}
.rule{margin:20px 0 0;padding:14px 18px;border-left:5px solid var(--ember);background:var(--cellar);border-radius:0 8px 8px 0}
.rule b{color:var(--ember)}
.pairs{list-style:none;margin:0;padding:0;display:grid;gap:12px}
.pairs li{display:grid;gap:10px;grid-template-columns:minmax(0,1fr)}
@media(min-width:700px){.pairs li{grid-template-columns:1fr 1fr}}
.pairs div{border-radius:8px;padding:12px 16px;border:2px solid var(--cellar)}
.pairs .no{background:var(--cellar);opacity:.85} .pairs .yes{background:var(--oak)}
.pairs small{display:block;font:600 .72rem "Fira Sans",sans-serif;letter-spacing:.14em;text-transform:uppercase;margin-bottom:2px}
.pairs .no small{color:var(--red);filter:brightness(1.5)} .pairs .yes small{color:var(--ember)}
.pairs q{font-size:1.02rem;quotes:none}
.lines{display:grid;gap:0;border:2px solid var(--cellar);border-radius:8px;overflow:hidden}
.lines .row{display:grid;gap:4px 16px;grid-template-columns:minmax(0,1fr);padding:11px 16px;background:var(--cellar)}
.lines .row:nth-child(even){background:var(--deep)}
@media(min-width:700px){.lines .row{grid-template-columns:200px 1fr}}
.lines .where{font-size:.82rem;color:var(--parch);letter-spacing:.04em}
.lines .say b{color:var(--bone)} .lines .say i{color:var(--parch)}
.tags{list-style:none;margin:0;padding:0;display:grid;gap:12px;grid-template-columns:repeat(auto-fit,minmax(260px,1fr))}
.tags li{background:var(--cellar);border-radius:8px;padding:16px 18px;font:600 1.1rem/1.3 Cinzel,Georgia,serif}
.tags li:first-child{outline:2px solid var(--ember)}
.tags small{display:block;font:.72rem "Fira Sans",sans-serif;letter-spacing:.14em;color:var(--ember);margin-bottom:4px}
.anat{display:grid;gap:28px;align-items:center;grid-template-columns:minmax(0,1fr)}
@media(min-width:800px){.anat{grid-template-columns:.8fr 1.2fr}}
.anat ol{margin:0;padding:0;list-style:none;display:grid;gap:10px}
.anat li{background:var(--cellar);border-radius:8px;padding:10px 14px;font-size:.95rem}
.anat li b{color:var(--ember)}
.sws{list-style:none;margin:0;padding:0;display:grid;gap:12px;grid-template-columns:repeat(auto-fill,minmax(220px,1fr))}
.sw{background:var(--c);color:var(--f);border:2px solid var(--cellar);border-radius:8px;padding:14px;display:flex;flex-direction:column;gap:2px;min-height:128px}
.sw b{font-family:Cinzel,Georgia,serif;font-size:1.02rem} .sw code{font:600 .95rem ui-monospace,Consolas,monospace}
.sw .use{font-size:.82rem;line-height:1.35;margin-top:4px} .sw .f4{font:.72rem ui-monospace,Consolas,monospace;opacity:.85;margin-top:auto;padding-top:6px}
.chips{list-style:none;margin:0;padding:0;display:grid;gap:12px;grid-template-columns:repeat(auto-fill,minmax(210px,1fr))}
.chips li{border:2px solid var(--cellar);border-radius:8px;padding:12px 14px;display:grid;grid-template-columns:auto 1fr;column-gap:12px;align-items:center}
.chips b{font:800 1.6rem Cinzel,Georgia,serif;grid-row:span 2} .chips span{font-size:.85rem} .chips em{font-style:normal;font-weight:600;font-size:.85rem}
.type{display:grid;gap:16px;grid-template-columns:repeat(auto-fit,minmax(280px,1fr))}
.type small{color:var(--parch);display:block;margin-bottom:6px}
footer{margin-top:96px;color:var(--parch);font-size:.9rem;border-top:2px solid var(--cellar);padding-top:20px}
"""

GUIDE_BODY = """
<nav aria-label="Sections"><a href="#story">Story</a><a href="#voice">Voice</a><a href="#words">Word bank</a><a href="#emblem">Emblem</a><a href="#logo">Logo</a><a href="#palette">Palette</a><a href="#using">Using it</a><a href="#type">Type</a></nav>

<header class="hero"><div>
<p class="kicker">BRAND GUIDE</p>
<h1 class="big">Every legend needs someone to <em>pour the drinks.</em></h1>
<p class="lede">Heroes Not Included is the village behind the adventure: the innkeeper, the smith, the healer, the tinker and the folks who know where everything is buried. The heroes get the glory. You get the tab, the gossip and, if you play it right, the whole story.</p>
</div><img src="emblem.svg" alt="Heroes Not Included emblem"></header>

<h2 id="story">The story in one breath</h2>
<p class="lede">Every adventure starts in a tavern. Ours starts <b>behind the bar</b>. You are the person the party walks past on the way to glory: the one who hears the rumours, posts the quests, patches the armour and keeps the lights on. The adventurers think they are the main characters. They are not wrong, exactly. They just aren't the ones running the place.</p>
<div class="grid">
<div class="panel"><h4>Who we are</h4><p>A warm, cheeky village of people with jobs, secrets and a long memory for who didn't tip.</p></div>
<div class="panel"><h4>Who we are not</h4><p>Not a hero's journey. No chosen ones, no destiny, no swords on the logo. The adventure is the weather; the village is the story.</p></div>
<div class="panel"><h4>How it feels</h4><p>A busy common room at dusk: warm, a little loud, someone laughing, and one person in the corner who is definitely up to something.</p></div></div>

<h2 id="voice">How we sound</h2>
<p class="lede">We sound like the innkeeper: warm, quick, a bit worn at the edges, and always listening. Fun first, fantasy as seasoning. If a line needs a glossary or a funny accent to land, it is too much.</p>
<div class="grid">
<div class="panel"><span class="num">1</span><h4>Warm first</h4><p>Welcome people in before explaining anything. We are the lit window on a cold night.</p></div>
<div class="panel"><span class="num">2</span><h4>Dry, never mean</h4><p>Heroes are the punchline. Players never are. Tease the entitled adventurer, not the person who just burned the stew.</p></div>
<div class="panel"><span class="num">3</span><h4>Plain words, fantasy furniture</h4><p>Quests, coin, ale and rumours are the seasoning. Sentences stay short and modern. No thee, thou, hath or "ye olde".</p></div>
<div class="panel"><span class="num">4</span><h4>Sly about secrets</h4><p>We always seem to know a bit more than we say. Hint, wink, raise an eyebrow. Never state what a character couldn't know.</p></div></div>
<p class="rule"><b>Clarity beats cute on anything you press.</b> A button says what it does. The joke goes in the line underneath.</p>

<h3>Too much, just right</h3>
<p class="lede">The fantasy is in the nouns, not the grammar.</p>
<ul class="pairs">
<li><div class="no"><small>Too much</small><q>Hark, weary traveller! Ye olde tavern doth bid thee welcome!</q></div><div class="yes"><small>Just right</small><q>Pull up a stool.</q></div></li>
<li><div class="no"><small>Too much</small><q>Alas, thy connection to the realm hath been severed.</q></div><div class="yes"><small>Just right</small><q>Lost the connection. The tavern's still standing. Probably.</q></div></li>
<li><div class="no"><small>Too much</small><q>Thy quest hath ended in naught but sorrow.</q></div><div class="yes"><small>Just right</small><q>The party didn't come back. Pour one out.</q></div></li>
<li><div class="no"><small>Too plain</small><q>Please wait while the game loads.</q></div><div class="yes"><small>Just right</small><q>Sweeping the common room...</q></div></li>
<li><div class="no"><small>Too mean</small><q>You mixed that drink wrong. Again.</q></div><div class="yes"><small>Just right</small><q>That one's going to be a story. Try another?</q></div></li></ul>

<h2 id="words">Word bank</h2>
<p class="lede">Real labels on the left so nobody gets lost. A little flavour on the right.</p>
<div class="lines">
<div class="row"><span class="where">Host a game</span><span class="say"><b>Host game</b> <i>Open the doors.</i></span></div>
<div class="row"><span class="where">Join a game</span><span class="say"><b>Join game</b> <i>Pull up a stool.</i></span></div>
<div class="row"><span class="where">Settings</span><span class="say"><b>Settings</b> <i>House rules.</i></span></div>
<div class="row"><span class="where">Quit</span><span class="say"><b>Quit</b> <i>Close up for the night.</i></span></div>
<div class="row"><span class="where">Empty lobby</span><span class="say"><i>Nobody's here yet. Suspicious.</i></span></div>
<div class="row"><span class="where">Round starts</span><span class="say"><i>Doors open. Smile.</i></span></div>
<div class="row"><span class="where">Town wins</span><span class="say"><i>The festival's on, the town is standing, and nobody had to say thank you.</i></span></div>
<div class="row"><span class="where">Town loses</span><span class="say"><i>Rough season. Next time, lock the cellar.</i></span></div>
<div class="row"><span class="where">Loading lines<br>(rotate)</span><span class="say"><i>Sweeping the common room... / Hiding the good ale... / Re-hanging the quest board... / Practising our polite smile... / Polishing tankards nobody asked about...</i></span></div></div>

<h3>Taglines</h3>
<ul class="tags">
<li><small>PRIMARY</small>Somebody has to keep the lights on.</li>
<li><small>ALSO GOOD</small>The heroes get the glory. You get the tab.</li>
<li><small>ALSO GOOD</small>Every legend needs someone to pour the drinks.</li>
<li><small>ALSO GOOD</small>Quests posted daily. Heroes tolerated.</li></ul>

<h3>Say it this way</h3>
<div class="lines">
<div class="row"><span class="where">The people in town</span><span class="say"><b>Villagers</b> in the game. <i>"NPC" is our in-joke for trailers and posters.</i></span></div>
<div class="row"><span class="where">The visitors</span><span class="say"><b>Heroes</b> when we're poking fun. <b>Adventurers</b> when we're being clear.</span></div>
<div class="row"><span class="where">What a player is</span><span class="say"><b>Trade</b>, not class. You're the innkeeper, the smith, the healer.</span></div>
<div class="row"><span class="where">Scores</span><span class="say"><b>Renown</b> and <b>Dread</b>. Always capitalised, never explained twice.</span></div>
<div class="row"><span class="where">Money</span><span class="say"><b>Silver</b> and <b>coin</b>. Never gold, never credits.</span></div></div>
<p class="rule"><b>Keep it ours.</b> We borrow the feel of the tabletop table, not its names. No trademarked monsters, classes or spell names, and no assets from the games that inspired us.</p>

<h2 id="emblem">The emblem</h2>
<p class="lede">A tavern sign, hung by the people who run the place. Where a hero's crest would carry crossed swords, ours carries the tools of the trades.</p>
<div class="anat"><figure class="card" style="background:var(--oak)"><div class="pad"><img src="emblem.svg" alt="Emblem" style="width:min(100%,380px)"></div></figure>
<ol>
<li><b>The signboard.</b> The village's own sign. This is the NPC world, and you're the one who hung it up.</li>
<li><b>The "!"</b> The quest marker every adventurer knows. Above your head, it means you have work for them.</li>
<li><b>The tankard.</b> The innkeeper's seat: where rumours land and jobs get handed out.</li>
<li><b>Hammer and chef's knife.</b> The smith and the cook, crossed where a crest would carry swords.</li>
<li><b>The flame.</b> The hearth. It's warm in here, so stay a while.</li>
<li><b>24 runes.</b> One for every hour of a game day, engraved into the iron.</li></ol></div>

<h2 id="logo">Logo</h2>
<p class="lede">Lettering is Cinzel Extra Bold, converted to outlines. The ribbon is the label on the box: heroes, not included.</p>
<div class="grid"><figure class="card" style="grid-column:1/-1"><div class="pad"><img src="logo-horizontal.svg" alt="Horizontal logo" style="width:min(100%,760px)"></div><figcaption>logo-horizontal.svg: headers, store pages, splash</figcaption></figure>
<figure class="card"><div class="pad"><img src="logo-stacked.svg" alt="Stacked logo" style="width:min(100%,380px)"></div><figcaption>logo-stacked.svg: title screen, posters, loading</figcaption></figure>
<figure class="card" style="background:var(--oak)"><div class="pad"><img src="emblem.png" alt="Emblem PNG" style="width:min(100%,300px)"></div><figcaption>emblem.png (1024 px, transparent): icons, avatars, socials</figcaption></figure>
<figure class="card" style="background:@bone"><div class="pad"><img src="emblem-mono-ink.svg" alt="One colour emblem, ink" style="width:min(100%,240px)"></div><figcaption>emblem-mono-ink.svg: print, stamps, light backgrounds</figcaption></figure>
<figure class="card"><div class="pad"><img src="emblem-mono-bone.svg" alt="One colour emblem, bone" style="width:min(100%,240px)"></div><figcaption>emblem-mono-bone.svg: engraved UI, dark backgrounds</figcaption></figure><figure class="card" style="background:var(--oak)"><div class="pad" style="grid-auto-flow:column;gap:20px;align-items:end"><img src="favicon.svg" alt="Favicon at 128 px" width="128"><img src="favicon.svg" alt="Favicon at 48 px" width="48"><img src="favicon.svg" alt="Favicon at 32 px" width="32"><img src="favicon.svg" alt="Favicon at 16 px" width="16"></div><figcaption>favicon.svg, favicon.ico (16 / 32 / 48), apple-touch-icon.png: the small-size sign, no runes or tools</figcaption></figure></div>

<h2 id="palette">Palette</h2>
<p class="lede">Oak, iron, bone and ember are the game's own colours from Ui/Theme.cs, so the logo and the chrome are one family. The last line on each swatch is ready to paste into C#.</p>
@GROUPS
<h3>Pairings that pass</h3><p class="lede">WCAG contrast ratios. Bone on the red ribbon is for large lettering only.</p><ul class="chips">@CHIPS</ul>

<h2 id="using">Using it</h2><div class="grid">
<div class="panel"><h4>Ember is rare</h4><p>One ember thing per screen: the quest marker, the call to action, a lit window. If everything glows, nothing is hiring.</p></div>
<div class="panel"><h4>Dark ground, bone text</h4><p>The brand lives in the dark of the common room. Oak Deep or Cellar for the page, Bone for text, Parchment for the quieter lines.</p></div>
<div class="panel"><h4>Clear space and size</h4><p>Keep the height of the quest marker (about one fifth of the emblem) clear on every side. Full-colour emblem down to 32 px; below that, use a simplified icon without the runes.</p></div>
<div class="panel"><h4>Never</h4><p>Put a hero on it. No swords, shields, helmets or dragons: the brand is the world around them. Don't recolour the ribbon, stretch the lettering, or set the emblem on a busy photo without the ink version.</p></div></div>

<h2 id="type">Type</h2><div class="type"><div class="panel"><small>Display: Cinzel 600 / 800 (SIL OFL)</small><span style="font:800 2rem Cinzel,Georgia,serif;letter-spacing:.06em">Heroes Not Included</span></div>
<div class="panel"><small>Text and UI: Fira Sans (already in data/fonts)</small><span style="font:600 1.4rem 'Fira Sans',sans-serif">Somebody has to keep the lights on.</span></div></div>

<footer>Generated by tools/brand_logo.py. Edit the script, not these files.</footer>
"""


def brand_sheet() -> str:
    def swatch(k: str) -> str:
        hx = P[k]
        fg = P["cellar"] if contrast(hx, P["cellar"]) >= contrast(hx, P["bone"]) else P["bone"]
        f4 = ", ".join(f"{int(hx[i:i + 2], 16) / 255:.2f}f" for i in (1, 3, 5))
        return (f'<li class="sw" style="--c:{hx};--f:{fg}"><b>{label(k)}</b><code>{hx}</code>'
                f'<span class="use">{PALETTE[k][1]}</span><span class="f4">new({f4}, 1f)</span></li>')

    groups = "".join(f'<section><h3>{n}</h3><p class="lede">{d}</p><ul class="sws">{"".join(swatch(k) for k in ks)}</ul></section>'
                     for n, d, ks in GROUPS)
    pairs = [("bone", "oak_deep"), ("ember", "oak_deep"), ("parchment", "oak_deep"), ("cellar", "bone"),
             ("cellar", "ember"), ("bone", "hearth_red")]
    chips = "".join(f'<li style="background:{P[b]};color:{P[f]}"><b>Aa</b><span>{label(f)} on {label(b)}</span>'
                    f'<em>{contrast(P[f], P[b]):.1f}:1</em></li>' for f, b in pairs)

    def fill(t: str) -> str:
        for k in sorted(P, key=len, reverse=True):
            t = t.replace("@" + k, P[k])
        return t

    body = GUIDE_BODY.replace("@GROUPS", groups).replace("@CHIPS", chips)
    return ('<!doctype html>\n<html lang="en"><head><meta charset="utf-8">'
            '<meta name="viewport" content="width=device-width,initial-scale=1">\n'
            '<title>Heroes Not Included: brand guide</title>\n'
            '<link rel="preconnect" href="https://fonts.googleapis.com">\n'
            '<link href="https://fonts.googleapis.com/css2?family=Cinzel:wght@600;800&family=Fira+Sans:wght@400;600&display=swap" rel="stylesheet">\n'
            f'<style>{fill(GUIDE_CSS)}</style></head><body><main>{fill(body)}</main></body></html>\n')


def favicon_group(c: dict, uid: str = "f") -> str:
    """The small-size sign: no runes and no crossed tools, just the rim, the "!" and the tankard, with
    heavy outlines so it survives 16 px."""
    L, W = c["line"], 12
    foam = [(200, 294, 32), (234, 268, 38), (278, 262, 40), (314, 284, 32), (256, 300, 38)]
    mug = "M176 298H336L328 398Q327 408 316 408H196Q185 408 184 398Z"
    return (f'<defs><clipPath id="{uid}-mug"><path d="{mug}"/></clipPath></defs>'
            f'<circle cx="256" cy="256" r="252" fill="{L}"/><circle cx="256" cy="256" r="244" fill="{c["rim"]}"/>'
            f'<circle cx="256" cy="256" r="220" fill="{L}"/><circle cx="256" cy="256" r="213" fill="{c["field"]}"/>'
            f'<g transform="translate(-12 6) translate(256 242) scale(1.12) translate(-256 -242)">'
            f'<path d="M328 318C384 312 390 388 322 384" fill="none" stroke="{L}" stroke-width="48" stroke-linecap="round"/>'
            f'<path d="M328 318C384 312 390 388 322 384" fill="none" stroke="{c["iron"]}" stroke-width="24" stroke-linecap="round"/>'
            f'<path d="{mug}" fill="{c["barrel"]}" stroke="{L}" stroke-width="{W}" stroke-linejoin="round"/>'
            f'<g clip-path="url(#{uid}-mug)"><rect x="160" y="344" width="190" height="24" fill="{c["iron"]}" stroke="{L}" stroke-width="7"/></g>'
            f'<g fill="{L}" stroke="{L}" stroke-width="{2 * W}" stroke-linejoin="round">'
            + "".join(f'<circle cx="{x}" cy="{y}" r="{r}"/>' for x, y, r in foam) + '</g>'
            f'<g fill="{c["foam"]}">' + "".join(f'<circle cx="{x}" cy="{y}" r="{r}"/>' for x, y, r in foam) + '</g>'
            f'<g fill="{c["mark"]}" stroke="{L}" stroke-width="{W}" stroke-linejoin="round">'
            f'<path d="M230 76H282L275 150Q274 160 264 160H248Q238 160 237 150Z"/><circle cx="256" cy="186" r="21"/></g></g>')


def favicon_svg() -> str:
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="Heroes Not Included">'
            f'{favicon_group(COLOUR)}</svg>\n')


def export_icons(svg: Path) -> list[Path]:
    """favicon.ico (16/32/48), apple-touch-icon.png (180, on oak since iOS fills transparency with black)."""
    try:
        from PIL import Image
    except ImportError:
        print("Pillow not installed: skipped favicon.ico and apple-touch-icon.png")
        return []
    big = OUT / "_favicon-512.png"
    if not export_png(svg, big, 512):
        return []
    src = Image.open(big).convert("RGBA")
    big.unlink()
    ico = OUT / "favicon.ico"
    src.save(ico, sizes=[(16, 16), (32, 32), (48, 48)])
    apple = Image.new("RGBA", (180, 180), P["oak_deep"])
    disc = src.resize((150, 150), Image.LANCZOS)
    apple.alpha_composite(disc, (15, 15))
    apple.convert("RGB").save(OUT / "apple-touch-icon.png")
    return [ico, OUT / "apple-touch-icon.png"]


def export_png(svg: Path, png: Path, size: int) -> bool:
    """Rasterise with headless Edge or Chrome, keeping the transparency. No extra Python packages."""
    import shutil
    import subprocess
    import tempfile
    cands = [shutil.which(n) for n in ("msedge", "chrome", "google-chrome", "chromium")] + [
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"]
    exe = next((c for c in cands if c and Path(c).exists()), None)
    if not exe:
        print("no Edge or Chrome found: skipped", png.name)
        return False
    with tempfile.TemporaryDirectory() as td:
        page = Path(td) / "p.html"
        page.write_text(f'<!doctype html><meta charset="utf-8"><style>html,body{{margin:0;background:transparent}}'
                        f'img{{display:block;width:{size}px;height:{size}px}}</style><img src="{svg.resolve().as_uri()}">', encoding="utf-8")
        png.unlink(missing_ok=True)
        subprocess.run([exe, "--headless", "--disable-gpu", "--hide-scrollbars", "--default-background-color=00000000",
                        f"--screenshot={png}", f"--window-size={size},{size}", page.as_uri()],
                       check=False, capture_output=True, timeout=60)
        # the browser can hand back before the file is written: wait for it to appear and settle
        import time
        last = -1
        for _ in range(60):
            now = png.stat().st_size if png.exists() else -1
            if now > 0 and now == last:
                return True
            last = now
            time.sleep(0.5)
    return False


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--fonts", type=Path, required=True, help="folder holding Cinzel[wght].ttf")
    ap.add_argument("--no-png", action="store_true", help="skip emblem.png (needs Edge or Chrome installed)")
    a = ap.parse_args()
    font = load_font(next(a.fonts.glob("Cinzel*.ttf")), 800)
    OUT.mkdir(parents=True, exist_ok=True)
    files = {
        "emblem.svg": emblem_svg(),
        "emblem-mono-bone.svg": emblem_mono_svg(P["bone"]),
        "emblem-mono-ink.svg": emblem_mono_svg(P["cellar"]),
        "logo-stacked.svg": logo_stacked(font),
        "logo-horizontal.svg": logo_horizontal(font),
        "favicon.svg": favicon_svg(),
        "brand-guide.html": brand_sheet(),
    }
    for name, svg in files.items():
        (OUT / name).write_text(svg, encoding="utf-8")
    (OUT / "palette.json").write_text(json.dumps({k: {"hex": v[0], "use": v[1]} for k, v in PALETTE.items()}, indent=2) + "\n", encoding="utf-8")
    for name in files:
        print("wrote", OUT / name)
    if not a.no_png and export_png(OUT / "emblem.svg", OUT / "emblem.png", 1024):
        print("wrote", OUT / "emblem.png")
    if not a.no_png:
        for f in export_icons(OUT / "favicon.svg"):
            print("wrote", f)
    for fg, bg in (("bone", "oak_deep"), ("bone", "cellar"), ("ember", "oak_deep"), ("parchment", "oak_deep"),
                   ("cellar", "bone"), ("cellar", "ember"), ("bone", "hearth_red"), ("iron_light", "oak_deep")):
        print(f"contrast {fg:>9} on {bg:<9} {contrast(P[fg], P[bg]):5.2f}")


if __name__ == "__main__":
    main()
