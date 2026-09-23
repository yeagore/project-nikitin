#!/usr/bin/env python3
"""The shape pass of 2026-09-23: one shape per good.

Maxim's rule: the same shape means varieties of one good. Two goods drawn as the same shape in
two colours read as varieties even where the context says otherwise, so every good of the
variety ledger that shared its drawing with another (the sixteen powders on one heap, the
liquids in one bottle, the metals as one bar, the ores as one speckled lump, the leaves, the
nuts, the cups, the goblets, the cauldrons...) gets a shape of its own here: the thing as it
was traded or kept, a jar, a twist of paper, a cake, a keg, a coil, a crystal habit. The
colours stay near what they were, so a good is still recognised by its hue at a glance.
One good of each family keeps its old drawing.

These are concept icons, 16 px, drawn for a parchment ground: a one-pixel outline in a dark of
the icon's own hue, three tones lit from the top left, a highlight. Drawn by hand, as shapes.

Run:  python3 tools/make_shape_pass.py [preview.png]      (deterministic)

Writes new rows onto resources/economy/sprites/icons.png from cell 352 (nothing before is
touched, so the older webs keep their icons) and tools/icon_shapes.json, which
tools/make_variety_ledger.py stamps into the variety ledger after the earlier fixes.
"""
import json
import math
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEET = os.path.join(ROOT, "resources", "economy", "sprites", "icons.png")
INDEX = os.path.join(ROOT, "tools", "icon_shapes.json")
FIRST = 352
COLUMNS = 16


# ---- colour ---------------------------------------------------------------------------------
def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def hexc(c):
    return "#%02x%02x%02x" % tuple(max(0, min(255, round(v))) for v in c)


def dark(h, f=0.7):
    return hexc(tuple(v * f for v in rgb(h)))


def light(h, f=0.35):
    return hexc(tuple(v + (255 - v) * f for v in rgb(h)))


def tones(base):
    """Light, mid, dark of a hue, its outline and its highlight."""
    return dict(l=light(base, 0.3), m=base, d=dark(base, 0.72), o=dark(base, 0.3), h=light(base, 0.7))


# ---- shapes: sets of (x, y) cells on the 16 x 16 grid ------------------------------------------
def inside(cells):
    return {(x, y) for (x, y) in cells if 0 <= x < 16 and 0 <= y < 16}


def rect(x0, y0, x1, y1):
    return inside({(x, y) for y in range(y0, y1 + 1) for x in range(x0, x1 + 1)})


def ellipse(cx, cy, rx, ry):
    return {(x, y) for y in range(16) for x in range(16) if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1.0}


def poly(points):
    s = set()
    n = len(points)
    for y in range(16):
        yc = y + 0.5
        xs = []
        for i in range(n):
            (x1, y1), (x2, y2) = points[i], points[(i + 1) % n]
            if y1 != y2 and min(y1, y2) <= yc < max(y1, y2):
                xs.append(x1 + (yc - y1) / (y2 - y1) * (x2 - x1))
        xs.sort()
        for i in range(0, len(xs) - 1, 2):
            for x in range(max(0, math.floor(xs[i] + 0.5)), min(15, math.ceil(xs[i + 1] - 0.5)) + 1):
                s.add((x, y))
    return s


def line(x0, y0, x1, y1):
    s = set()
    steps = max(abs(x1 - x0), abs(y1 - y0), 1)
    for i in range(steps + 1):
        s.add((round(x0 + (x1 - x0) * i / steps), round(y0 + (y1 - y0) * i / steps)))
    return inside(s)


def pts(*cells):
    return set(cells)


def shift(cells, dx, dy):
    return inside({(x + dx, y + dy) for x, y in cells})


# ---- the compositor ------------------------------------------------------------------------------
def draw(parts, details=(), outline=None, lit=True):
    """parts: [(cells, base hex)], later over earlier, each shaded light top-left to dark
    bottom-right across its own box. details: [(cells, hex)], flat, over everything, never
    outlined over. outline: the outline hex (default: the first part's). Returns a 16 x 16 RGBA."""
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    owner = {}
    for i, (cells, _) in enumerate(parts):
        for c in cells:
            owner[c] = i
    flat = {}
    for cells, h in details:
        for c in inside(cells):
            flat[c] = h
    opaque = set(owner) | set(flat)
    out = outline or tones(parts[0][1])["o"]
    for (x, y) in opaque:
        edge = any((x + dx, y + dy) not in opaque for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
        if (x, y) in flat:
            img.putpixel((x, y), rgb(flat[(x, y)]) + (255,))
            continue
        if edge:
            img.putpixel((x, y), rgb(out) + (255,))
            continue
        i = owner[(x, y)]
        cells, base = parts[i]
        t = tones(base)
        xs = [c[0] for c in cells]
        ys = [c[1] for c in cells]
        tx = (x - min(xs)) / max(max(xs) - min(xs), 1)
        ty = (y - min(ys)) / max(max(ys) - min(ys), 1)
        k = 0.4 * tx + 0.6 * ty if lit else 0.5
        img.putpixel((x, y), rgb(t["l"] if k < 0.33 else t["m"] if k < 0.7 else t["d"]) + (255,))
    # one highlight on the first part, the lit interior cell nearest the top left
    cells = parts[0][0]
    interior = [c for c in cells if c not in flat and all((c[0] + dx, c[1] + dy) in opaque for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    if interior and lit:
        hx, hy = min(interior, key=lambda c: c[0] + 1.3 * c[1])
        img.putpixel((hx, hy), rgb(tones(parts[0][1])["h"]) + (255,))
    return img


ICONS = {}  # good id -> (why, image)


def icon(gid, why):
    def wrap(fn):
        ICONS[gid] = (why, fn())
        return fn
    return wrap


# =====================================================================================================
# The powders: sixteen goods drawn as one heap. Sand keeps the heap; the rest go in what they were
# kept, sold or made in.
# =====================================================================================================
@icon("boneash", "was the white heap; now an open box of bone ash")
def _():
    box = rect(2, 8, 13, 13)
    lid = poly([(2, 8), (5, 3), (15, 3), (13, 8)]) - rect(2, 8, 13, 13)
    ash = ellipse(7.5, 8.2, 5.2, 1.6) & rect(3, 6, 12, 8)
    return draw([(box, "#9a7a52"), (lid, "#b8956a"), (ash, "#eeeae2")], details=[(pts((4, 10), (11, 10)), "#6e5236")])


@icon("soda", "was a white heap; now a wooden scoop of soda ash")
def _():
    scoop = poly([(2, 7), (11, 7), (11, 12), (2, 12)])
    handle = rect(11, 9, 15, 10)
    powder = ellipse(6.5, 7.2, 4.6, 1.8) & rect(2, 5, 11, 7)
    return draw([(scoop, "#a8844e"), (handle, "#8a6a3a"), (powder, "#e4e6ea")])


@icon("ash", "was a grey heap; now a pail of wood ash")
def _():
    pail = poly([(3, 7), (12, 7), (11, 14), (4, 14)])
    ash = ellipse(7.5, 7.3, 4.6, 1.4) & rect(3, 5, 12, 7)
    bail = {(x, y) for x in range(2, 14) for y in range(1, 8) if abs(math.hypot(x + 0.5 - 7.5, (y + 0.5 - 7.5) * 1.1) - 5.8) < 0.55 and y < 7}
    return draw([(pail, "#7a6a58"), (ash, "#b8b4ae")], details=[(bail, "#3e3a36"), (pts((3, 10), (12, 10)), "#4e453a")])


@icon("ochre", "was an orange heap; now a broken chunk of ochre, its bright face showing")
def _():
    chunk = poly([(1, 9), (4, 4), (10, 3), (14, 7), (13, 12), (6, 14), (2, 13)])
    face = poly([(7, 4), (13, 7), (12, 12), (7, 12), (5, 7)])
    return draw([(chunk, "#7a5a38"), (face, "#e0962e")], outline="#3a2410", details=[(pts((9, 7), (10, 9), (8, 10)), "#f4c060")])


@icon("fearth", "was a lilac heap; now a wedge cut from a bank of fuller's earth")
def _():
    wedge = poly([(2, 13), (14, 13), (14, 5), (6, 5)])
    top = poly([(6, 5), (14, 5), (12, 3), (8, 3)])
    return draw([(wedge, "#b9a2b8"), (top, "#d6c4d2")], details=[(line(8, 8, 12, 8), "#9a8398"), (line(5, 11, 11, 11), "#9a8398")])


@icon("tartar", "was a pink heap; now the crust scraped from a cask, thick on a curved stave")
def _():
    stave = {(x, y) for x in range(1, 15) for y in range(8, 15) if 10.5 + (x - 7.5) ** 2 / 14 <= y + 0.5 <= 13 + (x - 7.5) ** 2 / 14}
    crust = {(x, y) for x in range(1, 15) for y in range(3, 12) if 6 + (x - 7.5) ** 2 / 14 - (1 if x % 3 == 1 else 0) <= y + 0.5 < 10.5 + (x - 7.5) ** 2 / 14}
    return draw([(stave, "#8a5a36"), (crust, "#e8a4b0")], outline="#4a2618", details=[(pts((4, 7), (8, 6), (11, 8)), "#fbe0e6")])


@icon("verdi", "was a green heap; now a copper plate grown over with green")
def _():
    plate = poly([(2, 5), (13, 3), (14, 11), (3, 13)])
    bloom = (ellipse(6, 7, 2.6, 2) | ellipse(10.5, 9, 2.4, 2) | ellipse(9, 5, 1.6, 1.2)) & plate
    return draw([(plate, "#c07040"), (bloom, "#4fc2a0")], outline="#4a2a18")


@icon("kingsy", "was a heap, then a jar like honey's; now a hinged tin, open on its yellow")
def _():
    base = rect(2, 9, 13, 13)
    lid = poly([(2, 9), (4, 3), (15, 3), (13, 9)]) - base
    paint = rect(3, 8, 12, 9)
    return draw([(base, "#9aa0a8"), (lid, "#b8bec6")], outline="#2a2e34", details=[(paint, "#f0c830"), (pts((4, 10), (11, 10)), "#6a7078")])


@icon("litharge", "was a yellow heap; now a cupel, the bone-ash dish it forms in, full of it")
def _():
    dish = poly([(1, 7), (15, 7), (13, 12), (3, 12)])
    melt = ellipse(8, 7.3, 5.5, 1.7)
    return draw([(dish, "#d8d2c4"), (melt, "#e6b43a")], outline="#4a3a20")


@icon("rouge", "was a red heap; now a cake of rouge in its tin")
def _():
    tin = ellipse(7.5, 9, 6.5, 4) | rect(1, 9, 14, 11)
    cake = ellipse(7.5, 8.3, 5, 2.6)
    return draw([(tin, "#9aa0a8"), (cake, "#b83838")], outline="#3a1a1a", details=[(ellipse(6, 7.6, 1.5, 0.8), "#d86060")])


@icon("prussian", "was a blue heap; now a stamped cake of Prussian blue")
def _():
    top = poly([(1, 5), (8, 2), (15, 5), (8, 8)])
    front = poly([(1, 5), (8, 8), (8, 14), (1, 11)])
    side = poly([(8, 8), (15, 5), (15, 11), (8, 14)])
    return draw([(top, "#4a6ac0"), (front, "#1e3070"), (side, "#2a4490")], outline="#0a1230", lit=False,
                details=[(pts((8, 4), (7, 5), (9, 5), (8, 6)), "#9ab4f0")])


@icon("ultram", "was a blue heap; now a folded paper packet with the blue showing")
def _():
    packet = poly([(2, 4), (13, 4), (13, 13), (2, 13)])
    flap = poly([(2, 4), (13, 4), (7.5, 9)])
    return draw([(packet, "#e8dcc0"), (flap, "#d4c6a4")], outline="#4a3a24",
                details=[(rect(4, 10, 11, 12), "#2a50c8"), (pts((5, 10), (7, 11)), "#6a8cf0")])


@icon("zaf", "was a grey-blue heap; now the small keg zaffre was shipped in")
def _():
    keg = ellipse(7.5, 8.5, 5, 6) & rect(2, 3, 13, 14)
    bands = (rect(2, 5, 13, 5) | rect(2, 11, 13, 11)) & keg
    return draw([(keg, "#8a6a44")], outline="#3a2a18", details=[(bands, "#4a4a58"), (rect(6, 7, 9, 9), "#5a6a9a")])


@icon("thun", "was a gold heap; now a spoonful of thundergold, sparking")
def _():
    bowl = ellipse(5, 10, 3.6, 2.4)
    handle = line(8, 9, 14, 4) | line(8, 10, 14, 5)
    powder = ellipse(5, 9.2, 2.6, 1.2)
    return draw([(bowl, "#a8aab0"), (handle, "#8a8c94"), (powder, "#f0c030")], outline="#2a2a30",
                details=[(pts((4, 5), (5, 6), (4, 7), (3, 3), (6, 4), (2, 6)), "#fff080")])


@icon("dung", "was a brown heap; now a dung pat")
def _():
    pat = ellipse(7.5, 10.5, 6.5, 3.5)
    swirl = ellipse(7.5, 8.5, 4.2, 2.2)
    top = ellipse(7.5, 6.8, 2.2, 1.3)
    return draw([(pat, "#6a4a28"), (swirl, "#7e5a32"), (top, "#8e6a3a")], outline="#2a1a0c")


@icon("gunp", "was a dark heap; now a powder horn")
def _():
    horn = poly([(1, 5), (5, 3), (11, 6), (14, 11), (12, 13), (8, 9), (3, 8)])
    cap = rect(1, 4, 2, 8)
    tip = rect(12, 11, 14, 13)
    return draw([(horn, "#d8c8a0"), (cap, "#8a6a3a"), (tip, "#5a4a3a")], outline="#3a2e1c",
                details=[(line(4, 5, 11, 9), "#b8a47c"), (line(3, 2, 9, 1), "#6a4a2a")])


# ---- the crusts: natron, nitre earth, nitre bed, sal ammoniac crust were one crusted lump ----
@icon("nat", "was a crusted lump; now the cracked crust of a dry salt lake")
def _():
    slab = poly([(1, 8), (8, 4), (15, 7), (14, 12), (6, 14), (1, 12)])
    cracks = line(4, 9, 8, 11) | line(8, 11, 12, 9) | line(8, 11, 7, 13) | line(8, 7, 8, 10) | line(11, 6, 12, 9)
    return draw([(slab, "#ece4cc")], outline="#4a4030", details=[(cracks & slab, "#7a6c50")])


@icon("nit", "was a crusted lump; now a trough of nitre earth, white where the nitre blooms")
def _():
    trough = poly([(1, 8), (15, 8), (13, 13), (3, 13)])
    earth = ellipse(8, 8, 6.4, 1.8) & rect(1, 5, 15, 8)
    return draw([(trough, "#8a6a44"), (earth, "#6a4a30")], outline="#2a1a0c",
                details=[(pts((4, 6), (6, 6), (8, 5), (10, 6), (12, 6), (7, 7), (11, 7)), "#f4f0e8"), (line(3, 10, 13, 10), "#6a4e30")])


# ---- the cauldrons: pearl ash keeps the pot ----
@icon("glue", "was a cauldron; now the cakes hide glue was sold in, amber and stacked")
def _():
    a = poly([(2, 9), (10, 7), (14, 9), (6, 12)])
    b = shift(a, 0, -4)
    edge_a = poly([(2, 9), (6, 12), (6, 13), (2, 10)]) | poly([(6, 12), (14, 9), (14, 10), (6, 13)])
    edge_b = shift(edge_a, 0, -4)
    return draw([(a | edge_a, "#c88a30"), (b | edge_b, "#d89a40")], outline="#4a2a08")


@icon("amalgam", "was a cauldron; now a chamois pouch squeezed round its amalgam")
def _():
    pouch = ellipse(7.5, 10, 5, 4)
    neck = poly([(6, 6), (9, 6), (11, 2), (4, 2)])
    drop = ellipse(7.5, 14, 1.4, 1.2)
    return draw([(pouch, "#d8b880"), (neck, "#c8a870")], outline="#4a3818",
                details=[(line(5, 6, 10, 6), "#8a6a3a"), (drop, "#c8ccd4")])


@icon("enamel", "was a cauldron; now chips of enamel frit in three colours")
def _():
    a = poly([(1, 9), (5, 6), (8, 9), (5, 13)])
    b = poly([(7, 5), (11, 2), (14, 5), (11, 8)])
    c = poly([(8, 11), (11, 8), (14, 11), (11, 14)])
    return draw([(a, "#3a6ad8"), (b, "#e8c030"), (c, "#d84040")], outline="#1a1a30")


@icon("printink", "was a cauldron; now a printer's ink ball, the leather dabber")
def _():
    pad = ellipse(7.5, 11, 6, 3.2)
    cap = ellipse(7.5, 9.6, 4.6, 2.2)
    grip = rect(6, 2, 9, 8)
    return draw([(pad, "#2a2a30"), (cap, "#8a5a3a"), (grip, "#a07048")], outline="#140e0a",
                details=[(line(3, 12, 12, 12), "#50505a")])


# ---- the buckets: ox blood keeps the bucket ----
@icon("urine", "was a bucket; now the pot it was gathered in")
def _():
    pot = ellipse(7.5, 10, 5.8, 4.2) & rect(1, 6, 14, 14)
    rim = ellipse(7.5, 6.5, 6, 1.4)
    handle = {(x, y) for x in range(12, 16) for y in range(6, 13) if abs(math.hypot(x + 0.5 - 12.5, y + 0.5 - 9.5) - 2.6) < 0.6 and x >= 13}
    return draw([(pot, "#b89a70"), (rim, "#d4b88a")], outline="#4a3620", details=[(handle, "#8a6e48"), (ellipse(7.5, 6.5, 4.4, 0.8), "#d8c040")])


@icon("mortar", "was a bucket; now a tub of mortar with the trowel in it")
def _():
    tub = poly([(1, 8), (14, 8), (12, 14), (3, 14)])
    mix = ellipse(7.5, 8, 6.2, 1.6)
    blade = poly([(8, 2), (13, 1), (11, 6)])
    haft = line(6, 7, 9, 4)
    return draw([(tub, "#8a6a4a"), (mix, "#c8c0b0"), (blade, "#a8acb4")], outline="#2e241a", details=[(haft, "#6a4a2a")])


@icon("lacsap", "was a bucket; now a tapped trunk bleeding sap into a cup")
def _():
    trunk = rect(2, 1, 7, 14)
    cup = poly([(8, 9), (14, 9), (13, 13), (9, 13)])
    return draw([(trunk, "#6a5038"), (cup, "#a87848")], outline="#2a1a0e",
                details=[(line(3, 6, 6, 4), "#3a2a1a"), (pts((7, 6), (8, 7), (9, 8)), "#c8a060"), (rect(9, 9, 13, 9), "#3a2a14")])


# =====================================================================================================
# The liquids: twelve in one square bottle, eight in one round flask, six in one jar. Aqua vitae keeps
# the bottle, quintessence the flask, honey the jar; the rest go in the vessel each was kept in.
# =====================================================================================================
GLASS = "#cfdde6"


@icon("aqf", "was the square bottle; now a tall bottle with a glass stopper, fuming")
def _():
    body = rect(5, 5, 10, 14)
    neck = rect(6, 3, 9, 4)
    ball = ellipse(7.5, 2, 1.6, 1.3)
    acid = rect(6, 8, 9, 13)
    return draw([(body, GLASS), (neck, GLASS), (ball, GLASS), (acid, "#d8d860")], outline="#2a3440",
                details=[(pts((11, 1), (12, 2), (13, 1)), "#e8a030")])


@icon("aqr", "was the square bottle; now a bell-mouthed carafe of aqua regia")
def _():
    base = poly([(2, 14), (13, 14), (10, 7), (5, 7)])
    neck = rect(6, 3, 9, 6)
    lip = rect(5, 2, 10, 2)
    acid = poly([(3, 13), (12, 13), (10.5, 10), (4.5, 10)])
    return draw([(base, GLASS), (neck, GLASS), (lip, GLASS), (acid, "#e87a2a")], outline="#2a3440")


@icon("oov", "was the square bottle; now a carboy in its wicker basket, as vitriol was shipped")
def _():
    basket = ellipse(7.5, 9.5, 6.5, 5) | rect(1, 9, 14, 13)
    neck = rect(6, 2, 9, 5)
    weave = {(x, y) for (x, y) in basket if (x + 2 * y) % 4 == 0}
    return draw([(basket, "#b08a50"), (neck, GLASS)], outline="#3a2a14", details=[(weave, "#8a6a38"), (rect(6, 1, 9, 1), "#7a5a3a")])


@icon("sos", "was the square bottle; now a retort, the bulb and its long neck")
def _():
    bulb = ellipse(5.5, 10, 4.5, 4)
    neck = line(8, 7, 14, 3) | line(8, 8, 14, 4) | line(9, 8, 15, 4)
    acid = ellipse(5.5, 11.3, 3.4, 2.2)
    return draw([(bulb, GLASS), (neck, GLASS), (acid, "#a8d890")], outline="#2a3440")


@icon("vinegar", "was the square bottle; now a clay jug with a handle")
def _():
    body = ellipse(7, 10, 5, 4.5)
    neck = rect(5, 3, 8, 6)
    lip = rect(4, 2, 9, 2)
    handle = {(x, y) for x in range(9, 15) for y in range(3, 11) if abs(math.hypot(x + 0.5 - 9.5, y + 0.5 - 6.5) - 3.3) < 0.7 and x >= 10}
    return draw([(body, "#a86a3a"), (neck, "#a86a3a"), (lip, "#c08050"), (handle, "#8a5430")], outline="#3a200c",
                details=[(line(3, 9, 11, 9), "#d8b070")])


@icon("ricew", "was the square bottle; now a glazed tokkuri with a blue band")
def _():
    body = ellipse(7.5, 10.5, 5, 4)
    neck = poly([(6, 7), (9, 7), (8.5, 3), (6.5, 3)])
    lip = rect(5, 2, 10, 2)
    return draw([(body, "#ece6d8"), (neck, "#ece6d8"), (lip, "#ece6d8")], outline="#3a3a44",
                details=[(rect(3, 9, 12, 10) & body, "#3a5aa8")])


@icon("lye", "was the square bottle; now a stoneware crock with two ears")
def _():
    crock = rect(3, 4, 12, 14)
    rim = rect(2, 3, 13, 4)
    ears = rect(1, 6, 2, 8) | rect(13, 6, 14, 8)
    return draw([(crock, "#a8aca8"), (rim, "#c0c4c0"), (ears, "#8a8e8a")], outline="#2a2e2c",
                details=[(line(4, 7, 11, 7), "#6a7aa0"), (line(4, 8, 11, 8), "#6a7aa0")])


@icon("linseed", "was the square bottle; now an oil can with a long spout")
def _():
    can = ellipse(6.5, 10.5, 5, 3.5) | rect(2, 8, 11, 11)
    spout = line(10, 8, 14, 2) | line(11, 8, 15, 2)
    cap = rect(5, 5, 8, 6)
    return draw([(can, "#c89a3a"), (spout, "#a07a2a"), (cap, "#8a6a2a")], outline="#3a2808", details=[(ellipse(6.5, 10.5, 2, 1.2), "#f0d070")])


@icon("turps", "was the square bottle; now a flat tin flask")
def _():
    flask = ellipse(7.5, 9, 5.5, 5) & rect(2, 4, 13, 14)
    neck = rect(6, 1, 9, 4)
    return draw([(flask, "#b0b4bc"), (neck, "#8a8e96")], outline="#2a2c34",
                details=[(ellipse(7.5, 9, 3, 2.6) - ellipse(7.5, 9, 2, 1.6), "#8a8e96"), (rect(6, 1, 9, 1), "#6a4a2a")])


@icon("laud", "was the square bottle; now a small phial, stoppered, dark")
def _():
    phial = rect(6, 4, 9, 14)
    cork = rect(6, 2, 9, 3)
    tincture = rect(7, 7, 8, 13)
    return draw([(phial, GLASS), (cork, "#a07048"), (tincture, "#6a1a10")], outline="#2a2030",
                details=[(rect(5, 9, 10, 10), "#e8dcc0")])


@icon("ink", "was the square bottle; now an inkwell")
def _():
    base = poly([(1, 14), (14, 14), (12, 8), (3, 8)])
    top = rect(5, 6, 10, 7)
    return draw([(base, "#5a5a68"), (top, "#3a3a44")], outline="#141418", details=[(rect(6, 6, 9, 6), "#0a0a10")])


@icon("spirit", "was the round flask; now a pear-shaped decanter of clear spirit")
def _():
    body = ellipse(7.5, 10.5, 5, 4.2)
    neck = poly([(6.5, 7), (8.5, 7), (8, 2), (7, 2)])
    stopper = rect(6, 1, 9, 1)
    return draw([(body, "#dcecf4"), (neck, "#dcecf4")], outline="#2a3a48", details=[(stopper, "#8a6a4a"), (pts((5, 9), (4, 10)), "#ffffff")])


@icon("harts", "was the round flask; now a gourd-shaped flask")
def _():
    low = ellipse(7.5, 11, 5, 3.6)
    high = ellipse(7.5, 5.5, 3, 2.6)
    cork = rect(6, 1, 9, 2)
    return draw([(low, "#e8d4a8"), (high, "#e8d4a8")], outline="#4a3818", details=[(cork, "#8a6040"), (rect(3, 11, 12, 13) & low, "#c8a868")])


@icon("potg", "was the round flask; now a teardrop bottle of potable gold under a crown stopper")
def _():
    drop = poly([(7.5, 4), (12, 10), (11, 13), (4, 13), (3, 10)])
    gold = poly([(4, 10), (11, 10), (10.5, 12.5), (4.5, 12.5)])
    crown = pts((5, 2), (7, 1), (8, 1), (10, 2)) | rect(5, 3, 10, 3)
    return draw([(drop, GLASS), (gold, "#f0c030")], outline="#3a2a10", details=[(crown, "#d8a020")])


@icon("phos", "was the round flask; now a jar of water with the phosphorus stick glowing in it")
def _():
    jar = rect(3, 4, 12, 14)
    rim = rect(2, 3, 13, 3)
    stick = rect(7, 7, 8, 13)
    glow = ellipse(7.5, 9.5, 3, 4) & jar
    return draw([(jar, "#b8d4e0"), (rim, "#9ab8c8"), (glow, "#c8f0a0")], outline="#1a3040", details=[(stick, "#f8fff0")])


@icon("philm", "was the round flask; now the philosophers' egg, sealed")
def _():
    egg = ellipse(7.5, 10, 4.5, 4.5)
    neck = rect(7, 2, 8, 6)
    seal = rect(6, 1, 9, 1)
    silver = ellipse(7.5, 11, 3, 2.5)
    return draw([(egg, GLASS), (neck, GLASS), (silver, "#b8a8d8")], outline="#2a2440", details=[(seal, "#b83030")])


@icon("tinct", "was the round flask; now a small amber bottle with a dropper")
def _():
    bottle = rect(4, 7, 11, 14)
    shoulder = rect(5, 6, 10, 6)
    dropper = rect(7, 1, 8, 5)
    bulb = ellipse(7.5, 1.5, 1.6, 1.3)
    return draw([(bottle, "#a86a28"), (shoulder, "#a86a28"), (dropper, GLASS)], outline="#2a1808", details=[(bulb, "#3a3a3a"), (rect(5, 10, 10, 11), "#e8dcc0")])


@icon("elix", "was a jar; now a faceted bottle")
def _():
    body = poly([(4, 6), (11, 6), (13, 9), (11, 14), (4, 14), (2, 9)])
    neck = rect(6, 3, 9, 5)
    stopper = ellipse(7.5, 2, 2, 1.3)
    return draw([(body, "#e8c860"), (neck, GLASS)], outline="#3a2c08",
                details=[(line(4, 7, 4, 13) | line(11, 7, 11, 13), "#b89830"), (stopper, "#c83838")])


@icon("smell", "was a jar; now a vinaigrette, the pierced box on its chain")
def _():
    box = ellipse(7.5, 10, 5, 4)
    lid = ellipse(7.5, 9, 3.4, 2.4)
    chain = pts((7, 1), (8, 2), (7, 3), (8, 4), (7, 5))
    return draw([(box, "#c8ccd4"), (lid, "#dfe2e8")], outline="#2a2c34",
                details=[(chain, "#8a8e96"), (pts((6, 8), (8, 8), (7, 9), (9, 9), (6, 10)), "#4a4e56")])


@icon("ointm", "was a jar; now an apothecary jar with its label and lid")
def _():
    jar = poly([(3, 4), (12, 4), (11.5, 9), (12, 14), (3, 14), (3.5, 9)])
    lid = rect(4, 2, 11, 3) | rect(7, 1, 8, 1)
    label = rect(5, 7, 10, 11)
    return draw([(jar, "#e8e4d8"), (lid, "#3a6ab0")], outline="#2a2a34", details=[(label, "#3a6ab0"), (rect(6, 9, 9, 9), "#e8e4d8")])


@icon("theriac", "was a jar; now a lidded urn with handles, as theriac was sold")
def _():
    urn = ellipse(7.5, 10, 4.5, 4) | rect(5, 13, 10, 14)
    lid = ellipse(7.5, 5.5, 3.5, 1.8) | rect(7, 2, 8, 4)
    handles = pts((2, 8), (2, 9), (13, 8), (13, 9), (3, 7), (12, 7))
    return draw([(urn, "#6a8a4a"), (lid, "#8a6a3a")], outline="#1e2a10", details=[(handles, "#c8a040"), (rect(4, 10, 11, 10), "#c8a040")])


@icon("cosm", "was a jar; now a kohl pot with its stick")
def _():
    pot = ellipse(6.5, 11, 4.5, 3.4)
    neck = rect(5, 6, 8, 8)
    stick = line(9, 7, 13, 1) | line(10, 7, 14, 1)
    return draw([(pot, "#c86a8a"), (neck, "#a85070")], outline="#3a1424", details=[(stick, "#d8b060"), (rect(5, 6, 8, 6), "#1a1a1a")])


@icon("coffeeb", "was the cup it shared with tea; now a long-spouted coffee pot")
def _():
    body = poly([(4, 6), (10, 6), (12, 14), (2, 14)])
    lid = poly([(5, 3), (9, 3), (10, 6), (4, 6)]) | rect(6, 1, 8, 2)
    spout = line(3, 8, 0, 4) | line(3, 9, 1, 4)
    handle = pts((11, 7), (12, 8), (13, 9), (12, 10))
    return draw([(body, "#c89a50"), (lid, "#d8aa60"), (spout, "#b08a40")], outline="#3a2408", details=[(handle, "#6a4a2a")])


@icon("choc", "was the cup it shared with tea; now a tablet of chocolate, scored")
def _():
    tablet = ellipse(7.5, 8.5, 6.5, 5.5)
    scores = (line(3, 8, 12, 8) | line(7, 4, 7, 13) | line(8, 4, 8, 13)) & tablet
    return draw([(tablet, "#6a3a1e")], outline="#200e04", details=[(scores, "#4a2410")])


@icon("perfume", "was a thin bottle like wine's; now a round flask under a tall cut stopper")
def _():
    body = ellipse(7.5, 11, 5, 3.6)
    neck = rect(6, 6, 9, 7)
    stopper = poly([(7.5, 1), (10, 4), (7.5, 6), (5, 4)])
    return draw([(body, "#e0a0c8"), (neck, GLASS), (stopper, "#c8e0f0")], outline="#3a1a30")


# =====================================================================================================
# The metals: one bar in seven colours. Tin keeps the bar; each other metal takes the form it was
# traded in.
# =====================================================================================================
@icon("icu", "was the metal bar; now a round cake of copper, as it came from the smelter")
def _():
    cake = ellipse(7.5, 9, 6.5, 4)
    top = ellipse(7.5, 8, 5.2, 2.8)
    return draw([(cake, "#b8683a"), (top, "#d8844a")], outline="#3a1a0a", details=[(pts((5, 7), (6, 7)), "#f8c090")])


@icon("ipb", "was the metal bar; now a pig of lead with its stamp")
def _():
    pig = poly([(1, 9), (3, 6), (13, 6), (15, 9), (14, 12), (2, 12)])
    return draw([(pig, "#6a7078")], outline="#1a1c20", details=[(rect(6, 8, 9, 9), "#4a5058"), (line(3, 7, 12, 7), "#8a9098")])


@icon("izn", "was the metal bar; now a slab of spelter, its face spangled")
def _():
    slab = poly([(1, 7), (5, 3), (15, 3), (11, 7)]) | rect(1, 7, 11, 12) | poly([(11, 7), (15, 3), (15, 8), (11, 12)])
    spangle = pts((4, 9), (5, 8), (6, 9), (5, 10), (5, 9), (8, 10), (9, 9), (9, 11), (10, 10), (8, 4), (10, 5), (12, 4))
    return draw([(slab, "#a8b4b8")], outline="#2a3034", details=[(spangle, "#dfe8ea")])


@icon("brass", "was the metal bar; now a manilla, the horseshoe of brass traded by the thousand")
def _():
    ring = {(x, y) for y in range(16) for x in range(16) if 3.2 <= math.hypot(x + 0.5 - 7.5, (y + 0.5 - 7) * 1.1) <= 5.6 and y + 0.5 < 11}
    ends = ellipse(3, 12, 2, 1.6) | ellipse(12, 12, 2, 1.6)
    return draw([(ring, "#d8b040"), (ends, "#c8a038")], outline="#4a3208", details=[(pts((5, 4), (6, 3)), "#fff0a0")])


@icon("pewter", "was the metal bar; now a pewter plate")
def _():
    plate = ellipse(7.5, 8, 7, 5.5)
    well = ellipse(7.5, 8, 4.4, 3.2)
    return draw([(plate, "#9aa0a8"), (well, "#b8bec6")], outline="#2a2e34", details=[(ellipse(7.5, 8, 4.4, 3.2) - ellipse(7.5, 8, 3.6, 2.4), "#7a8088")])


@icon("specm", "was the metal bar; now a speculum mirror standing in its cradle")
def _():
    disc = ellipse(7.5, 7, 4.5, 6)
    cradle = rect(3, 13, 12, 14) | rect(4, 11, 5, 12) | rect(10, 11, 11, 12)
    return draw([(disc, "#d8dce4"), (cradle, "#6a4a2a")], outline="#1e2028", details=[(line(6, 3, 5, 8), "#ffffff"), (line(8, 3, 7, 6), "#ffffff")])


@icon("ife", "was a small bar; now bar iron, three bars bound together")
def _():
    bars = rect(1, 5, 14, 7) | rect(1, 8, 14, 10) | rect(1, 11, 14, 13)
    return draw([(bars, "#6a6e74")], outline="#18191c", details=[(rect(4, 4, 5, 14), "#4a3a2a"), (rect(10, 4, 11, 14), "#4a3a2a"), (line(2, 6, 13, 6), "#8a8e94")])


@icon("iau", "was two small blocks; now a stack of gold bars")
def _():
    bottom = poly([(1, 13), (3, 10), (7, 10), (7, 13)]) | poly([(8, 13), (8, 10), (12, 10), (14, 13)])
    top = poly([(4, 9), (6, 6), (10, 6), (12, 9)])
    return draw([(bottom, "#e8b830"), (top, "#f0c840")], outline="#4a3208", details=[(pts((6, 7), (7, 7)), "#fff0a0")])


@icon("bronze", "was a small block; now a bronze bell")
def _():
    bell = poly([(5, 4), (10, 4), (11, 10), (14, 13), (1, 13), (4, 10)])
    crown = rect(6, 2, 9, 3)
    clapper = ellipse(7.5, 14, 1.3, 1)
    return draw([(bell, "#b07a3a"), (crown, "#8a5a2a")], outline="#3a2008", details=[(clapper, "#5a3a1a"), (line(3, 11, 12, 11), "#d89a50")])


@icon("salt", "was a sack like the new flour sack; now a salt cellar on its foot")
def _():
    bowl = poly([(2, 5), (13, 5), (11, 10), (4, 10)])
    foot = rect(6, 11, 9, 12) | rect(4, 13, 11, 14)
    salt = ellipse(7.5, 5, 5, 1.8) & rect(2, 3, 13, 5)
    return draw([(bowl, "#b8bec8"), (foot, "#9aa0aa"), (salt, "#ffffff")], outline="#2a2e38")


# =====================================================================================================
# The ores and stones: one speckled lump in many colours. Cinnabar keeps the lump; each other ore
# takes the habit it grows in.
# =====================================================================================================
@icon("cu", "was the speckled lump; now native copper, branching")
def _():
    branch = (line(7, 14, 7, 3) | line(7, 9, 3, 5) | line(7, 7, 12, 3) | line(7, 11, 12, 8) | line(3, 5, 2, 2)
              | line(12, 8, 14, 7) | line(4, 6, 2, 7) | line(12, 3, 13, 1) | line(8, 14, 8, 10))
    thick = branch | shift(branch, 1, 0)
    return draw([(thick, "#c8703a")], outline="#3a1a08", details=[(pts((3, 2), (12, 1), (14, 6)), "#6ab89a")])


@icon("ag", "was a crystal; now wire silver, curling out of its rock")
def _():
    rock = ellipse(7.5, 12, 6, 2.6)
    wires = {(x, y) for y in range(1, 11) for x in range(16) if abs(x + 0.5 - (7.5 + 3 * math.sin(y * 0.9))) < 0.8} | \
            {(x, y) for y in range(3, 11) for x in range(16) if abs(x + 0.5 - (4 + 2 * math.sin(y * 1.2 + 1))) < 0.7} | \
            {(x, y) for y in range(4, 11) for x in range(16) if abs(x + 0.5 - (11.5 + 2 * math.cos(y * 1.1))) < 0.7}
    return draw([(rock, "#6a6058"), (wires, "#dfe2e8")], outline="#1e1c20")


@icon("fe", "was the speckled lump; now ironstone in its kidney-shaped lumps")
def _():
    bumps = ellipse(5, 10, 3.6, 3.4) | ellipse(10, 10.5, 3.6, 3) | ellipse(7.5, 6, 3.4, 3)
    return draw([(bumps, "#8a3a2a")], outline="#2a0e08", details=[(pts((6, 5), (3, 9), (9, 9)), "#c8604a")])


@icon("sn", "was the speckled lump; now cassiterite, dark twinned crystals")
def _():
    a = poly([(4, 3), (8, 8), (6, 14), (1, 9)])
    b = poly([(11, 2), (15, 8), (11, 13), (7, 8)])
    return draw([(a, "#4a3a34"), (b, "#5a4840")], outline="#140e0a", details=[(line(4, 4, 3, 8) | line(11, 3, 10, 7), "#a89088")])


@icon("zn", "was the speckled lump; now calamine, porous as a sponge")
def _():
    lump = ellipse(7.5, 9, 6.5, 5)
    holes = pts((4, 7), (7, 6), (10, 8), (5, 10), (8, 10), (11, 11), (6, 12), (9, 13), (12, 7))
    return draw([(lump, "#9ac8c0")], outline="#1e3834", details=[(holes, "#4a7a74")])


@icon("bis", "was the speckled lump; now bismuth ore, a stepped crystal growing from grey rock")
def _():
    rock = poly([(1, 13), (3, 9), (8, 10), (13, 8), (15, 13)])
    step = rect(5, 3, 11, 9) - rect(7, 5, 9, 7)
    inner = rect(7, 5, 9, 7) - rect(8, 6, 8, 6)
    return draw([(rock, "#7a7880"), (step, "#c890c8"), (inner, "#90c8d8")], outline="#1e1c24")


@icon("cob", "was the speckled lump; now cobalt ore, its pink bloom on dark rock")
def _():
    rock = ellipse(7.5, 10, 6.5, 4.4)
    bloom = pts((5, 6), (4, 5), (6, 5), (5, 4), (10, 7), (9, 6), (11, 6), (10, 5), (12, 9), (7, 9), (8, 8), (6, 8))
    return draw([(rock, "#3a3a4a")], outline="#101018", details=[(bloom, "#e86aa8")])


@icon("coal", "was a black lump; now coal, two glossy broken chunks")
def _():
    a = poly([(1, 10), (4, 5), (9, 6), (8, 13), (3, 14)])
    b = poly([(9, 8), (12, 4), (15, 7), (14, 12), (10, 12)])
    return draw([(a, "#2a2a30"), (b, "#34343c")], outline="#08080a", details=[(line(4, 6, 7, 7) | line(12, 5, 14, 7), "#8a8aa0")])


@icon("lode", "was the speckled lump; now lodestone, bristling with the iron filings it holds")
def _():
    rock = ellipse(7.5, 9, 5, 4)
    spikes = pts((2, 5), (3, 6), (7, 3), (7, 4), (12, 4), (11, 5), (13, 9), (14, 9), (1, 10), (2, 10), (4, 14), (11, 14), (8, 14))
    return draw([(rock, "#3a3440")], outline="#0e0c12", details=[(spikes, "#8a8a94")])


@icon("mang", "was a black lump; now magnesia nigra, sooty round nodules")
def _():
    balls = ellipse(5, 10.5, 3.4, 3.2) | ellipse(10.5, 10.5, 3.2, 3) | ellipse(8, 5.5, 3, 2.8)
    return draw([(balls, "#3a3036")], outline="#0a080a", details=[(pts((4, 9), (9, 9), (7, 4)), "#6a5a64")])


@icon("pyr", "was a gold cube; now a marcasite sun, the flat radiating disc")
def _():
    disc = ellipse(7.5, 8.5, 6.5, 6)
    rays = {(x, y) for (x, y) in disc if (round(math.degrees(math.atan2(y + 0.5 - 8.5, x + 0.5 - 7.5))) // 30) % 2 == 0}
    return draw([(disc, "#b8a860")], outline="#2a2410", details=[(rays - ellipse(7.5, 8.5, 1.4, 1.4), "#8a7c40"), (ellipse(7.5, 8.5, 1.4, 1.4), "#d8c880")])


@icon("slt", "was the speckled lump; now a block of rock salt, split clean along its cleavage")
def _():
    top = poly([(2, 6), (7, 3), (14, 5), (9, 8)])
    front = poly([(2, 6), (9, 8), (9, 14), (2, 12)])
    side = poly([(9, 8), (14, 5), (13, 11), (9, 14)])
    return draw([(top, "#f4c8c8"), (front, "#d89a9a"), (side, "#e8b0b0")], outline="#4a2424", lit=False,
                details=[(line(3, 9, 8, 11), "#c08080"), (pts((12, 12), (13, 13), (14, 12)), "#e8b0b0")])


@icon("alu", "was the speckled lump; now alum stone in flat layered slabs")
def _():
    slabs = poly([(1, 12), (13, 10), (15, 12), (3, 14)]) | poly([(2, 9), (12, 7), (14, 9), (4, 11)]) | poly([(3, 6), (11, 4), (13, 6), (5, 8)])
    return draw([(slabs, "#e8d4d8")], outline="#4a3438", details=[(line(4, 13, 13, 11) | line(5, 10, 12, 8) | line(6, 7, 11, 5), "#b89aa0")])


@icon("lim", "was the speckled lump; now a quarried block of limestone, chisel-marked")
def _():
    top = poly([(1, 6), (5, 3), (15, 3), (11, 6)])
    front = rect(1, 6, 11, 13)
    side = poly([(11, 6), (15, 3), (15, 10), (11, 13)])
    return draw([(top, "#f0e8d0"), (front, "#d8ceb0"), (side, "#c0b494")], outline="#4a4230", lit=False,
                details=[(pts((3, 8), (4, 9), (6, 8), (7, 9), (9, 8), (3, 11), (5, 11), (8, 11)), "#b0a484")])


@icon("kaolin", "was the speckled lump; now a ball of white clay, thumb-marked")
def _():
    ball = ellipse(7.5, 8.5, 6, 5.8)
    return draw([(ball, "#f0ece4")], outline="#4a4640", details=[(ellipse(9.5, 7, 1.4, 1.2), "#c8c2b6"), (ellipse(5.5, 10.5, 1.2, 1), "#c8c2b6")])


@icon("clay", "was a lump; now a block of potter's clay with the cutting wire")
def _():
    block = rect(2, 6, 13, 13)
    top = poly([(2, 6), (5, 4), (15, 4), (13, 6)])
    wire = line(1, 9, 15, 9) | pts((0, 8), (15, 8))
    return draw([(block, "#a0785a"), (top, "#b88c6a")], outline="#3a2414", details=[(wire, "#d8d8e0")])


@icon("gyps", "was the speckled lump; now gypsum grown as a desert rose")
def _():
    petals = (poly([(7.5, 2), (10, 7), (5, 7)]) | poly([(2, 6), (7, 8), (4, 12)]) | poly([(13, 6), (11, 12), (8, 8)])
              | poly([(3, 13), (8, 9), (12, 13), (7.5, 14)]))
    return draw([(petals, "#e0c8a0")], outline="#4a3620", details=[(line(7, 3, 7, 6) | line(4, 7, 5, 10) | line(11, 7, 10, 10), "#b89a70")])


@icon("bitu", "was a black drop; now a pool of bitumen with a bubble rising")
def _():
    pool = ellipse(7.5, 11, 7, 3.2)
    bubble = ellipse(9, 7, 2.2, 2)
    return draw([(pool, "#1e1a1a"), (bubble, "#2a2626")], outline="#050404", details=[(pts((8, 6), (5, 10), (11, 11)), "#6a6470")])


@icon("resin", "was a drop; now a strip of pine bark with the resin beading on it")
def _():
    bark = poly([(2, 2), (7, 1), (9, 14), (4, 15)])
    drops = ellipse(10, 6, 2, 2.4) | ellipse(10.5, 11, 2.2, 2.4) | ellipse(7, 9, 1.4, 1.6)
    return draw([(bark, "#6a4a30"), (drops, "#e8a830")], outline="#2a1a0a", details=[(line(4, 3, 6, 13), "#4a3020"), (pts((9, 5), (10, 10)), "#fff0a0")])


@icon("frank", "was a lump; now a handful of frankincense tears")
def _():
    tears = ellipse(4.5, 10, 2.4, 3) | ellipse(10, 11, 2.6, 2.6) | ellipse(8, 5.5, 2.2, 2.8) | ellipse(12.5, 5, 1.6, 2)
    return draw([(tears, "#e8d8a8")], outline="#4a3e20", details=[(pts((4, 9), (9, 10), (7, 4), (12, 4)), "#fff8e0")])


@icon("camphor", "was a cube like borax's; now two pastilles of camphor, stacked")
def _():
    low = ellipse(7.5, 11, 6, 2.6) | rect(2, 10, 13, 11)
    high = ellipse(8.5, 6.5, 4.6, 2.2) | rect(4, 6, 13, 7)
    return draw([(low, "#e8eef0"), (high, "#f4f8fa")], outline="#3a444a", details=[(line(6, 6, 11, 6), "#9ab8a8"), (line(4, 11, 11, 11), "#c8d4d8")])


@icon("alum", "was a crystal like sulfur's; now a clear octahedron of alum")
def _():
    top = poly([(7.5, 1), (13, 8), (2, 8)])
    bottom = poly([(2, 8), (13, 8), (7.5, 15)])
    return draw([(top, "#f0f0f4"), (bottom, "#d4d8e0")], outline="#3a3e4a", details=[(line(7, 2, 7, 14), "#b8bcc8")])


@icon("coconut", "was a brown ball; now a coconut split to show its flesh")
def _():
    shell = ellipse(7.5, 9, 6.5, 5.2)
    flesh = ellipse(7.5, 8.2, 5, 3.6)
    return draw([(shell, "#6a4228"), (flesh, "#f4f0e4")], outline="#241208", details=[(ellipse(7.5, 8.4, 3.4, 2.2), "#dfeaf0")])


# =====================================================================================================
# Plants: leaves, nuts, stalks and flowers drawn alike. Mulberry keeps its leaves, areca its nut, maize
# its cob, bamboo its canes, cotton its boll.
# =====================================================================================================
@icon("tea", "was a leaf pair like mulberry's; now a pressed tea brick, stamped")
def _():
    brick = rect(2, 3, 13, 13) - pts((2, 3), (13, 3), (2, 13), (13, 13))
    stamp = ellipse(7.5, 8, 3.4, 3) - ellipse(7.5, 8, 2.2, 1.8)
    return draw([(brick, "#5a5a30")], outline="#1a1a08", details=[(stamp, "#8a8a50"), (pts((7, 7), (8, 8), (7, 9)), "#8a8a50")])


@icon("herbs", "was a leaf pair; now hop cones hanging from the bine")
def _():
    a = ellipse(5, 9, 2.6, 3.6)
    b = ellipse(10.5, 10, 2.6, 3.6)
    bine = line(2, 2, 13, 2) | line(5, 2, 5, 5) | line(10, 2, 10, 6)
    return draw([(a, "#9ac85a"), (b, "#8ab84a")], outline="#1e3010",
                details=[(bine, "#5a7a2a"), (line(4, 8, 6, 8) | line(4, 10, 6, 10) | line(10, 9, 12, 9) | line(10, 11, 12, 11), "#6a9a3a")])


@icon("agave", "was a leaf pair; now an agave, spiked leaves from one rosette")
def _():
    leaves = (poly([(7, 14), (8, 14), (7.5, 1)]) | poly([(6, 14), (8, 13), (1, 4)]) | poly([(7, 13), (9, 14), (14, 4)])
              | poly([(5, 14), (7, 12), (0, 10)]) | poly([(8, 12), (10, 14), (15, 10)]))
    return draw([(leaves, "#6a9a8a")], outline="#1a2e28")


@icon("kelp", "was a leaf pair; now lake wrack, long wavy ribbons")
def _():
    ribbons = set()
    for x0 in (4, 8, 11):
        for y in range(1, 15):
            x = round(x0 + 1.5 * math.sin(y * 0.8 + x0))
            ribbons |= {(x, y), (x + 1, y)}
    return draw([(inside(ribbons), "#6a8a3a")], outline="#1a2a0a")


@icon("indigo", "was a leaf pair; now indigo stems cut and tied in a bundle")
def _():
    stems = line(4, 14, 7, 3) | line(6, 14, 7, 3) | line(9, 14, 8, 3) | line(11, 14, 8, 3)
    leaves = ellipse(5, 3, 2.2, 1.4) | ellipse(10, 3, 2.2, 1.4) | ellipse(7.5, 2, 1.8, 1.3) | ellipse(4, 6, 1.6, 1.1) | ellipse(11, 6, 1.6, 1.1)
    return draw([(stems, "#4a6a3a"), (leaves, "#2a5a7a")], outline="#0e1e14", details=[(rect(5, 9, 10, 9), "#9a7a4a")])


@icon("betel", "was a leaf pair; now the betel quid, a leaf folded into a pinned parcel")
def _():
    parcel = poly([(2, 12), (13, 12), (7.5, 3)])
    fold = poly([(2, 12), (7.5, 8), (13, 12)])
    return draw([(parcel, "#4a8a3a"), (fold, "#3a7a2a")], outline="#122a0a", details=[(line(7, 4, 7, 11), "#d8c8a0"), (pts((7, 8),), "#c83a2a")])


@icon("olives", "was a nut like areca's; now an olive sprig with three olives")
def _():
    twig = line(1, 3, 13, 11)
    leaves = poly([(4, 5), (6, 1), (7, 5)]) | poly([(9, 8), (13, 5), (11, 9)])
    olives = ellipse(4, 9, 1.8, 2.2) | ellipse(8, 12, 1.8, 2.2) | ellipse(13.5, 13.5, 1.6, 1.6)
    return draw([(leaves, "#8a9a6a"), (olives, "#6a7a2a")], outline="#1e2410", details=[(twig, "#6a5a3a")])


@icon("kola", "was a nut like areca's; now a kola nut split into its two lobes")
def _():
    a = ellipse(5.5, 8.5, 4, 5)
    b = ellipse(10.5, 8.5, 3.6, 4.6)
    return draw([(a, "#c85a6a"), (b, "#b84a5a")], outline="#3a1018", details=[(line(8, 4, 8, 13), "#6a2030")])


@icon("coffee", "was a nut like olives'; now a bunch of red coffee cherries on the twig")
def _():
    twig = line(7, 1, 7, 6) | line(7, 4, 12, 2)
    cherries = ellipse(5, 8, 2.2, 2.2) | ellipse(9.5, 8, 2.2, 2.2) | ellipse(7.3, 11.5, 2.2, 2.2) | ellipse(3.5, 12, 2, 2) | ellipse(11.5, 12, 2, 2)
    return draw([(cherries, "#c83a2a")], outline="#3a0c08", details=[(twig, "#5a7a3a"), (pts((4, 7), (9, 7), (7, 11)), "#f09080")])


@icon("galls", "was a nut like olives'; now an oak gall on its twig, the wasp's hole in it")
def _():
    twig = line(1, 12, 14, 12) | line(10, 12, 13, 14)
    gall = ellipse(7.5, 7.5, 4.6, 4.6)
    return draw([(gall, "#a07a4a")], outline="#2a1a08", details=[(twig, "#5a4028"), (pts((9, 6),), "#2a1a08"), (pts((5, 5), (6, 5)), "#c8a070")])


@icon("cacao", "was a nut like maize's cob; now a ribbed cacao pod")
def _():
    pod = ellipse(7.5, 8.5, 4.2, 6.5)
    ribs = (line(6, 3, 5, 14) | line(9, 3, 10, 14)) & pod
    stalk = rect(7, 0, 8, 2)
    return draw([(pod, "#c8782a")], outline="#3a1c06", details=[(ribs, "#9a5418"), (stalk, "#5a4028")])


@icon("cane", "was the jointed stalks of bamboo; now a cane stalk with its long leaves flaring")
def _():
    stalk = rect(7, 5, 8, 15)
    joints = rect(7, 8, 8, 8) | rect(7, 12, 8, 12)
    leaves = line(7, 5, 1, 1) | line(8, 5, 14, 1) | line(7, 6, 2, 5) | line(8, 6, 13, 5) | line(7, 4, 6, 0)
    return draw([(stalk, "#b8a870")], outline="#2a2410", details=[(joints, "#7a6a40"), (leaves, "#6a9a3a")])


@icon("hemp", "was the jointed stalks of bamboo; now a hemp leaf, seven fingers")
def _():
    fingers = set()
    for ang in (-75, -45, -18, 0, 18, 45, 75):
        a = math.radians(ang - 90)
        length = 7 if ang == 0 else 6 if abs(ang) < 30 else 5 if abs(ang) < 60 else 3.5
        for t in range(1, int(length * 2) + 1):
            r = t / 2
            x, y = 7.5 + r * math.cos(a), 12 + r * math.sin(a)
            fingers |= {(int(x), int(y)), (int(x + 0.6), int(y))}
    stem = line(7, 12, 7, 15)
    return draw([(inside(fingers), "#4a8a3a")], outline="#102808", details=[(stem, "#3a5a2a")])


@icon("poppy", "was a flower like cotton's; now the poppy's seed head, crowned")
def _():
    head = ellipse(7.5, 8, 4.5, 4)
    crown = rect(4, 3, 11, 4) & poly([(4, 4), (11, 4), (7.5, 2)]) | pts((5, 3), (7, 2), (8, 2), (10, 3))
    stem = rect(7, 12, 8, 15)
    return draw([(head, "#9aa88a")], outline="#1e2418", details=[(crown, "#6a6a5a"), (stem, "#5a7a3a"), (line(5, 8, 10, 8), "#6a785a")])


@icon("weld", "was a flower like cotton's; now a spike of weld in yellow flower")
def _():
    spike = {(x, y) for y in range(1, 13) for x in range(16) if abs(x + 0.5 - 7.5) <= 0.8 + (y / 12) * 2.2}
    buds = {(x, y) for (x, y) in spike if (x + y) % 3 == 0}
    stem = rect(7, 13, 8, 15)
    return draw([(spike, "#d8c830")], outline="#3a3408", details=[(buds, "#a8982a"), (stem, "#5a7a3a")])


# ---- threads: one skein recoloured. Silk thread keeps the skein ----
@icon("linthr", "was the skein; now linen thread on a bobbin")
def _():
    thread = rect(4, 4, 11, 11)
    ends = rect(3, 2, 12, 3) | rect(3, 12, 12, 13)
    return draw([(thread, "#e8e0c8"), (ends, "#8a6a4a")], outline="#3a2e1c", details=[(line(4, 6, 11, 6) | line(4, 9, 11, 9), "#c8bca0")])


@icon("cotthr", "was the skein; now cotton thread spun onto a spindle")
def _():
    shaft = rect(7, 1, 8, 15)
    cop = ellipse(7.5, 6, 3.6, 3.6)
    whorl = ellipse(7.5, 12, 3.4, 1.2)
    return draw([(cop, "#f4f2ec"), (whorl, "#8a6a4a")], outline="#3a3a40", details=[(shaft - cop - whorl, "#a07848"), (line(5, 5, 10, 5) | line(5, 7, 10, 7), "#d8d4cc")])


@icon("hempf", "was the skein; now hemp fibre in a long twisted hank")
def _():
    hank = {(x, y) for x in range(1, 15) for y in range(16) if abs(y + 0.5 - (8 + 1.5 * math.sin(x * 0.6))) < 2.4}
    twist = {(x, y) for (x, y) in hank if (x * 2 + y) % 5 == 0}
    return draw([(hank, "#b8a870")], outline="#2e2810", details=[(twist, "#8a7a48")])


@icon("goldthr", "was the skein; now gold thread wound on a card")
def _():
    card = rect(3, 2, 12, 13)
    wound = rect(3, 5, 12, 10)
    return draw([(card, "#e8dcc0"), (wound, "#e0b030")], outline="#3a2a0a", details=[(line(3, 6, 12, 6) | line(3, 8, 12, 8), "#b08a18")])


@icon("yarn", "was a small skein; now a ball of woollen yarn")
def _():
    ball = ellipse(7.5, 8.5, 5.6, 5.4)
    wraps = {(x, y) for (x, y) in ball if abs((x - y) % 4) == 0} | {(x, y) for (x, y) in ball if (x + 2 * y) % 6 == 0}
    tail = line(12, 12, 15, 14)
    return draw([(ball, "#c8a88a")], outline="#3a2a1a", details=[(wraps, "#a8886a"), (tail, "#c8a88a")])


# =====================================================================================================
# Wares and the pairs that were one drawing: one of each keeps its icon.
# =====================================================================================================
@icon("silverw", "was a goblet like gilded ware's; now a silver ewer")
def _():
    body = ellipse(7, 10, 4.5, 4)
    neck = poly([(5, 6), (9, 6), (10, 2), (4, 2)])
    spout = line(10, 3, 13, 1)
    handle = pts((11, 5), (12, 6), (12, 8), (11, 9))
    foot = rect(5, 14, 9, 14)
    return draw([(body, "#c8ccd8"), (neck, "#d8dce6"), (foot, "#9aa0ac")], outline="#2a2c38", details=[(spout | handle, "#8a90a0")])


@icon("antcup", "was a goblet; now the antimony cup, squat and stemless")
def _():
    cup = poly([(2, 5), (13, 5), (11, 13), (4, 13)])
    rim = rect(2, 4, 13, 5)
    return draw([(cup, "#8a8e9a"), (rim, "#aab0bc")], outline="#1e2028", details=[(line(4, 8, 11, 8), "#6a6e7a")])


@icon("lucent", "was a goblet; now a lucentware bowl, glowing")
def _():
    bowl = poly([(1, 7), (14, 7), (11, 12), (4, 12)])
    foot = rect(6, 13, 9, 14)
    glow = pts((3, 4), (7, 3), (12, 4), (5, 2), (10, 2))
    return draw([(bowl, "#b8e8e0"), (foot, "#8ac8c0")], outline="#1a3a38", details=[(glow, "#e8fff8"), (line(3, 8, 12, 8), "#e8fff8")])


@icon("ruby", "was a gem like the philosophers' stone; now a beaker of ruby glass")
def _():
    beaker = poly([(3, 3), (12, 3), (11, 14), (4, 14)])
    return draw([(beaker, "#c82a4a")], outline="#3a0814", details=[(line(5, 5, 5, 12), "#f08aa0"), (rect(4, 3, 11, 3), "#e05a78")])


@icon("crystal", "was a gem; now a lead-crystal drop, as hung from a chandelier")
def _():
    drop = poly([(7.5, 4), (11, 9), (7.5, 15), (4, 9)])
    hook = pts((7, 1), (8, 1), (7, 2), (7, 3))
    return draw([(drop, "#d8ecf8")], outline="#2a3a48", details=[(hook, "#b8a060"), (line(7, 5, 7, 13), "#a8c8e0"), (pts((5, 9), (6, 8)), "#ffffff")])


@icon("obsw", "was a gem; now an obsidian blade")
def _():
    blade = poly([(2, 13), (12, 2), (14, 4), (4, 14)])
    grip = rect(1, 12, 3, 14)
    return draw([(blade, "#2a2a34")], outline="#050508", details=[(line(4, 12, 12, 4), "#6a6a80"), (grip, "#8a5a2a")])


@icon("charts", "was a sheet like parchment's; now a chart rolled and tied")
def _():
    roll = rect(1, 6, 14, 10)
    ends = ellipse(1.5, 8, 1.2, 2.2) | ellipse(14, 8, 1.2, 2.2)
    return draw([(roll, "#e8d8b0")], outline="#3a2e18", details=[(ends, "#c8b890"), (rect(7, 5, 8, 11), "#b83a2a"), (line(3, 7, 12, 7), "#6a8ab0")])


@icon("oilpaint", "was a sheet like paper's; now a palette with its dabs of colour")
def _():
    palette = ellipse(7.5, 8.5, 7, 5.5) - ellipse(3.5, 10.5, 1.3, 1.1)
    dabs = [(ellipse(5, 6, 1.3, 1.1), "#c83a2a"), (ellipse(8.5, 5, 1.3, 1.1), "#e8c030"), (ellipse(11.5, 7, 1.3, 1.1), "#2a5ac8"), (ellipse(10.5, 11, 1.3, 1.1), "#3a9a4a")]
    return draw([(palette, "#c89a60")], outline="#3a2410", details=dabs)


@icon("writing", "was a quill like quills'; now a writing box, its lid a slope")
def _():
    box = rect(1, 8, 14, 13)
    slope = poly([(1, 8), (14, 8), (14, 4), (1, 6)])
    return draw([(box, "#7a4a2a"), (slope, "#3a6a3a")], outline="#1e1008", details=[(rect(10, 9, 12, 10), "#c8a040"), (line(3, 5, 12, 3), "#e8e0c8")])


@icon("telescope", "was a tube like the spyglass; now a telescope on its tripod")
def _():
    tube = line(3, 6, 13, 2) | line(3, 7, 13, 3) | line(4, 8, 14, 4)
    legs = line(8, 6, 4, 14) | line(8, 6, 8, 14) | line(8, 6, 12, 14)
    return draw([(tube, "#c89a40")], outline="#3a2408", details=[(legs, "#6a4a2a")])


@icon("rigging", "was a coil like rope's; now a pulley block with its line")
def _():
    block = ellipse(7.5, 8, 3.6, 4.6)
    sheave = ellipse(7.5, 8, 1.6, 2.4)
    rope = line(3, 1, 3, 15) | line(12, 1, 12, 15)
    return draw([(block, "#9a6a3a")], outline="#2a1808", details=[(sheave, "#5a3a1a"), (rope, "#c8b080"), (rect(7, 1, 8, 3), "#6a6a70")])


@icon("lunar", "was a stick like brimstone's; now lunar caustic in its silver holder")
def _():
    holder = line(2, 13, 10, 5) | line(3, 13, 11, 5) | line(2, 12, 10, 4)
    stick = line(10, 5, 14, 1) | line(11, 5, 14, 2)
    return draw([(holder, "#c8ccd4")], outline="#2a2c34", details=[(stick, "#5a5a64")])


@icon("engine", "was a gear like clockwork's; now the aether engine, a vessel with its glow")
def _():
    vessel = ellipse(7.5, 9, 5.5, 4.5)
    pipes = rect(3, 2, 4, 6) | rect(11, 2, 12, 6)
    core = ellipse(7.5, 9, 2.4, 2)
    return draw([(vessel, "#6a5a8a"), (pipes, "#8a8a9a")], outline="#1a1428", details=[(core, "#c8a8ff"), (pts((7, 8),), "#ffffff")])


@icon("castbrass", "was a vase like enamelware's; now a cast brass head")
def _():
    head = ellipse(7.5, 7, 4, 5)
    neck = rect(5, 11, 10, 14)
    return draw([(head, "#c8903a"), (neck, "#a87428")], outline="#3a2408",
                details=[(pts((6, 6), (9, 6)), "#3a2408"), (line(6, 9, 9, 9), "#6a4818"), (rect(4, 2, 11, 3), "#8a6020")])


@icon("pitch", "was a barrel like the blasting charge's; now a pot of pitch with its stick, dripping")
def _():
    pot = poly([(2, 7), (13, 7), (12, 14), (3, 14)])
    stick = line(9, 8, 13, 1)
    drip = pts((2, 8), (2, 9), (2, 10))
    return draw([(pot, "#5a4a3a")], outline="#140e08", details=[(ellipse(7.5, 7, 5, 1.2), "#1a1410"), (stick, "#8a6a3a"), (drip, "#1a1410")])


@icon("charge", "was a barrel; now a round charge with its fuse")
def _():
    ball = ellipse(7, 9, 5.5, 5.3)
    fuse = line(10, 5, 13, 2)
    return draw([(ball, "#3a3a44")], outline="#0a0a10", details=[(fuse, "#c8b080"), (pts((13, 1), (14, 2), (14, 1)), "#f0a030"), (pts((5, 6), (4, 7)), "#7a7a8a")])


@icon("tinplate", "was a square like the looking glass; now a sheet of tinplate, its corner curling")
def _():
    sheet = poly([(1, 3), (14, 3), (14, 10), (10, 14), (1, 14)])
    curl = poly([(10, 14), (14, 10), (11, 11)])
    return draw([(sheet, "#c8ccd4"), (curl, "#9aa0aa")], outline="#2a2e36", details=[(line(3, 5, 8, 5), "#eef0f4")])


@icon("clock", "was a dial like the compass; now a bracket clock in its case")
def _():
    case = rect(3, 2, 12, 14)
    top = rect(5, 1, 10, 1)
    dial = ellipse(7.5, 7, 3.2, 3.2)
    return draw([(case, "#7a4a2a"), (top, "#9a6a3a"), (dial, "#f0e8d0")], outline="#1e1008",
                details=[(pts((7, 5), (7, 6), (8, 7), (9, 7)), "#1e1008"), (rect(6, 11, 9, 12), "#c8a040")])


@icon("plaster", "was a sack like salt's and flour's; now a plaster ceiling rose")
def _():
    rose = ellipse(7.5, 8, 6.5, 6.5)
    petals = {(x, y) for (x, y) in rose if (round(math.degrees(math.atan2(y + 0.5 - 8, x + 0.5 - 7.5))) // 45) % 2 == 0} - ellipse(7.5, 8, 2, 2)
    return draw([(rose, "#f0ece4")], outline="#4a4640", details=[(petals, "#d8d2c6"), (ellipse(7.5, 8, 1.4, 1.4), "#b8b0a0")])


# ---- the last look-alikes the likeness check found once the rest were drawn ----
@icon("was", "was the honey jar recoloured, pixel for pixel; now a flue brick crusted with white arsenic")
def _():
    brick = rect(1, 8, 14, 13)
    crust = {(x, y) for x in range(1, 15) for y in range(3, 9) if y >= 8 - (2 + (x * 7) % 4)}
    return draw([(brick, "#a8583a"), (crust, "#f4f4f8")], outline="#2a1410", details=[(line(1, 10, 14, 10), "#7a3a24"), (pts((4, 4), (9, 3), (12, 5)), "#dcdce8")])


@icon("lens", "was a disc like the compass; now a lens in a magnifier's rim and handle")
def _():
    rim = ellipse(6.5, 6.5, 5.5, 5.5)
    glass = ellipse(6.5, 6.5, 4, 4)
    handle = line(10, 10, 14, 14) | line(11, 10, 15, 14)
    return draw([(rim, "#b8903a"), (glass, "#d8ecf8")], outline="#2a2010", details=[(handle, "#6a4a2a"), (pts((4, 4), (5, 4), (4, 5)), "#ffffff")])


@icon("eggs", "was a pair of ovals like the silk cocoons; now eggs in a straw nest")
def _():
    nest = ellipse(7.5, 11, 7, 3.4)
    a = ellipse(5.5, 8, 2.4, 3)
    b = ellipse(10, 8.5, 2.4, 3)
    return draw([(nest, "#c8a860"), (a, "#f4ecdc"), (b, "#ece0c8")], outline="#3a2e14",
                details=[({(x, y) for (x, y) in nest if (x + y) % 3 == 0 and y > 10}, "#9a7a3a")])


@icon("sam", "was a crust like the nitre bed's; now sal ammoniac feathering the mouth of a vent")
def _():
    rock = poly([(1, 14), (3, 7), (6, 5), (9, 5), (12, 7), (14, 14)])
    vent = ellipse(7.5, 7, 2, 1.4)
    feathers = pts((3, 6), (4, 5), (5, 4), (10, 4), (11, 5), (12, 6), (6, 3), (9, 3), (2, 8), (13, 8))
    return draw([(rock, "#6a5a4a")], outline="#1e1810", details=[(vent, "#1e1810"), (feathers, "#f4f4f0"), (pts((7, 2), (8, 1)), "#c8c8c0")])


@icon("provis", "was a cask like mead's; now a slatted crate of ship's provisions")
def _():
    crate = rect(2, 5, 13, 13)
    lid = poly([(2, 5), (5, 2), (15, 2), (13, 5)])
    return draw([(crate, "#a07848"), (lid, "#b8905a")], outline="#2e1e0c",
                details=[(line(2, 8, 13, 8) | line(2, 11, 13, 11), "#6a4a2a"), (line(2, 5, 13, 13), "#6a4a2a")])



@icon("shot", "was a mound in a bowl like the silk cocoons; now lead shot, loose")
def _():
    balls = ellipse(4, 11, 2, 2) | ellipse(8.5, 12, 2, 2) | ellipse(12.5, 10.5, 2, 2) | ellipse(6, 6.5, 2, 2) | ellipse(10.5, 6, 2, 2) | ellipse(2.5, 4, 1.4, 1.4)
    return draw([(balls, "#6a7078")], outline="#16181c", details=[(pts((3, 10), (8, 11), (12, 9), (5, 5), (10, 5)), "#b8c0c8")])


@icon("qlime", "was white lumps like the wool's fleece; now quicklime, angular and steaming")
def _():
    lumps = poly([(1, 13), (3, 9), (7, 9), (7, 14)]) | poly([(7, 14), (8, 8), (13, 8), (15, 13)]) | poly([(4, 9), (6, 6), (10, 6), (9, 9)])
    steam = pts((5, 4), (6, 3), (5, 2), (10, 4), (11, 3), (10, 2), (11, 1))
    return draw([(lumps, "#eceae4")], outline="#4a4844", details=[(steam, "#a8b0b8"), (line(8, 9, 8, 13), "#c8c4bc")])



@icon("hg", "was the round flask, then a dish like litharge's cupel; now the iron flask quicksilver travelled in")
def _():
    flask = rect(4, 4, 11, 14)
    shoulder = ellipse(7.5, 4.5, 3.5, 1.6)
    neck = rect(6, 1, 9, 3)
    return draw([(flask, "#6a6e78"), (shoulder, "#6a6e78"), (neck, "#4a4e58")], outline="#16181e",
                details=[(line(5, 6, 5, 12), "#9aa0aa"), (rect(4, 9, 11, 9), "#4a4e58")])


@icon("charcoal", "was a black lump, then sticks like bar iron; now charred billets stacked end-on")
def _():
    logs = ellipse(4.5, 11, 3.2, 3) | ellipse(10.5, 11, 3.2, 3) | ellipse(7.5, 6, 3.2, 3)
    rings = (ellipse(4.5, 11, 1.4, 1.2) | ellipse(10.5, 11, 1.4, 1.2) | ellipse(7.5, 6, 1.4, 1.2))
    return draw([(logs, "#2e2a28")], outline="#0a0808", details=[(rings, "#5a4a3e"), (pts((4, 11), (10, 11), (7, 6)), "#8a5a3a")])


@icon("shellac", "was a cube, then flakes like the glue cakes; now button lac, round and amber")
def _():
    buttons = ellipse(4.5, 10.5, 3.2, 2.6) | ellipse(11, 11, 3.2, 2.6) | ellipse(8, 5.5, 3.2, 2.6)
    return draw([(buttons, "#c8801e")], outline="#3a1e04", details=[(pts((3, 9), (10, 10), (7, 4)), "#f8c870")])


@icon("pulque", "was a white jar like the linen bobbin; now an olla of pulque, frothing")
def _():
    pot = ellipse(7.5, 10, 6, 4.4)
    mouth = rect(4, 4, 11, 6)
    froth = ellipse(7.5, 4.2, 3.8, 1.6)
    return draw([(pot, "#b86a3a"), (mouth, "#a05a30")], outline="#3a1a08", details=[(froth, "#f4f0e0"), (line(3, 10, 12, 10), "#e0a060")])


def write(preview=None):
    sheet = Image.open(SHEET).convert("RGBA")
    need = FIRST + len(ICONS)
    rows = (need + COLUMNS - 1) // COLUMNS
    if sheet.height < rows * 16:
        grown = Image.new("RGBA", (sheet.width, rows * 16), (0, 0, 0, 0))
        grown.paste(sheet, (0, 0))
        sheet = grown
    index = {}
    for n, (gid, (why, img)) in enumerate(ICONS.items()):
        cell = FIRST + n
        x0, y0 = cell % COLUMNS * 16, cell // COLUMNS * 16
        sheet.paste((0, 0, 0, 0), (x0, y0, x0 + 16, y0 + 16))
        sheet.paste(img, (x0, y0), img)
        index[gid] = cell
    sheet.save(SHEET)
    with open(INDEX, "w") as f:
        json.dump({"atlas": "icons", "icons": index, "why": {g: w for g, (w, _) in ICONS.items()}}, f, indent=1)
        f.write("\n")
    print(f"wrote {len(index)} icons into cells {FIRST} to {FIRST + len(index) - 1} of {os.path.relpath(SHEET, ROOT)}")
    if preview:
        s, cols = 5, 12
        pr = (len(ICONS) + cols - 1) // cols
        out = Image.new("RGBA", (cols * (16 * s + 6), pr * (16 * s + 6)), (233, 217, 184, 255))
        for n, (gid, (_, img)) in enumerate(ICONS.items()):
            big = img.resize((16 * s, 16 * s), Image.NEAREST)
            out.paste(big, (n % cols * (16 * s + 6), n // cols * (16 * s + 6)), big)
        out.save(preview)


if __name__ == "__main__":
    write(sys.argv[1] if len(sys.argv) > 1 else None)
