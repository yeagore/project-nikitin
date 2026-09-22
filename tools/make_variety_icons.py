#!/usr/bin/env python3
"""Draw 52 new variety-safe icons and append them to the icon sheet.

Some goods will soon be recoloured at run time by variety hue (see the
project's variety-ledger work): that breaks when two different goods share
one silhouette, since a recoloured icon could land on another good's exact
shape. This script redraws every good that has varieties so its silhouette
is unique on the whole sheet, appends the new icons as new cells (the old
288 cells are untouched), and writes a matching masks sheet for the goods
that recolour in parts (golem, jewel, ship, and four of the new icons).

Run:  python3 tools/make_variety_icons.py     (no arguments, deterministic)

Reads:
  tools/icon_pixels.json                        -- exact pixels of the old 286 goods
  resources/economy/sprites/icons.png            -- the old 288-cell sheet

Writes:
  resources/economy/sprites/icons.png            -- old 288 cells + 52 new cells
  resources/economy/sprites/masks.png            -- 16-cell mask sheet
  tools/variety_icons.json                       -- the cell/mask/layer index
  <scratchpad>/variety_icons_x4.png              -- a 4x inspection sheet

Does not touch scripts/ or resources/economy/webs/.
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PIXELS_PATH = os.path.join(ROOT, "tools", "icon_pixels.json")
SHEET_PATH = os.path.join(ROOT, "resources", "economy", "sprites", "icons.png")
MASKS_PATH = os.path.join(ROOT, "resources", "economy", "sprites", "masks.png")
INDEX_PATH = os.path.join(ROOT, "tools", "variety_icons.json")

SCRATCH = ("/private/tmp/claude-501/-Users-maximbarganov-Documents-GitHub-project-nikitin/"
           "eb16bb98-e7af-458e-abfa-e46373be5589/scratchpad")
BEFORE_PATH = os.path.join(SCRATCH, "icons_before.png")
PREVIEW_PATH = os.path.join(SCRATCH, "variety_icons_x4.png")

CELL = 16
COLUMNS = 16
OLD_ROWS = 18
OLD_CELLS = COLUMNS * OLD_ROWS  # 288

# ---------------------------------------------------------------------------
# Source data: the exact pixels of every existing good.
# ---------------------------------------------------------------------------
with open(PIXELS_PATH) as f:
    PIX = json.load(f)
GOODS = PIX["goods"]


def old_palette(gid):
    return GOODS[gid]["icon"]["palette"]


def old_rows(gid):
    return GOODS[gid]["icon"]["rows"]


def old_alpha(gid):
    rows = old_rows(gid)
    return tuple(ch != "." for row in rows for ch in row)


# ---------------------------------------------------------------------------
# Colour helpers. We reuse a good's own existing hex values wherever it
# already has enough shading levels; `lighten`/`darken` only cover the few
# goods whose old palette had fewer than three tones for a given part. They
# keep the hue, only moving toward white or black, so the "current hues"
# stay the same.
# ---------------------------------------------------------------------------
def hex_to_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def rgb_to_hex(rgb):
    return "#%02x%02x%02x" % tuple(max(0, min(255, round(c))) for c in rgb)


def darken(h, f=0.7):
    r, g, b = hex_to_rgb(h)
    return rgb_to_hex((r * f, g * f, b * f))


def lighten(h, f=1.25):
    r, g, b = hex_to_rgb(h)
    return rgb_to_hex((r + (255 - r) * (f - 1), g + (255 - g) * (f - 1), b + (255 - b) * (f - 1)))


# ---------------------------------------------------------------------------
# Geometry helpers. Everything lives on a 16x16 grid; every shape is a
# Python set of (x, y) integer cells so shapes combine with plain set
# operators (| union, - subtraction, & intersection).
# ---------------------------------------------------------------------------
def rect(x0, y0, x1, y1):
    return {(x, y) for y in range(int(round(y0)), int(round(y1)) + 1)
            for x in range(int(round(x0)), int(round(x1)) + 1)
            if 0 <= x < 16 and 0 <= y < 16}


def hline(x0, x1, y):
    y = int(round(y))
    return {(x, y) for x in range(int(round(x0)), int(round(x1)) + 1) if 0 <= x < 16 and 0 <= y < 16}


def vline(x, y0, y1):
    x = int(round(x))
    return {(x, y) for y in range(int(round(y0)), int(round(y1)) + 1) if 0 <= y < 16 and 0 <= x < 16}


def ellipse(cx, cy, rx, ry):
    s = set()
    for y in range(16):
        for x in range(16):
            dx = (x + 0.5 - cx) / rx
            dy = (y + 0.5 - cy) / ry
            if dx * dx + dy * dy <= 1.0:
                s.add((x, y))
    return s


def ring(cx, cy, rox, roy, rix, riy):
    return ellipse(cx, cy, rox, roy) - ellipse(cx, cy, rix, riy)


def poly(points):
    """Scanline polygon fill. points is a list of (x, y) floats."""
    s = set()
    ys = [p[1] for p in points]
    miny, maxy = int(math.floor(min(ys))), int(math.ceil(max(ys)))
    n = len(points)
    for y in range(max(0, miny), min(16, maxy + 1)):
        yc = y + 0.5
        xs = []
        for i in range(n):
            x1, y1 = points[i]
            x2, y2 = points[(i + 1) % n]
            if y1 == y2:
                continue
            if min(y1, y2) <= yc < max(y1, y2):
                t = (yc - y1) / (y2 - y1)
                xs.append(x1 + t * (x2 - x1))
        xs.sort()
        for i in range(0, len(xs) - 1, 2):
            xa, xb = xs[i], xs[i + 1]
            x0 = max(0, int(math.floor(xa + 0.5)))
            x1_ = min(15, int(math.ceil(xb - 0.5)))
            for x in range(x0, x1_ + 1):
                s.add((x, y))
    return s


def bresline(x0, y0, x1, y1):
    x0, y0, x1, y1 = int(round(x0)), int(round(y0)), int(round(x1)), int(round(y1))
    s = set()
    dx = abs(x1 - x0)
    dy = -abs(y1 - y0)
    sx = 1 if x0 < x1 else -1
    sy = 1 if y0 < y1 else -1
    err = dx + dy
    x, y = x0, y0
    while True:
        if 0 <= x < 16 and 0 <= y < 16:
            s.add((x, y))
        if x == x1 and y == y1:
            break
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x += sx
        if e2 <= dx:
            err += dx
            y += sy
    return s


def clip14(cells):
    """Keep the required 1px transparent margin (content in rows/cols 1..14)."""
    return {(x, y) for (x, y) in cells if 1 <= x <= 14 and 1 <= y <= 14}


def pick_highlight(cells):
    """The filled cell closest to the top-left that is not itself on the edge."""
    interior = [(x, y) for (x, y) in cells
                if all((x + dx, y + dy) in cells for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))]
    pool = interior if interior else list(cells)
    return min(pool, key=lambda p: p[0] + 1.3 * p[1])


# ---------------------------------------------------------------------------
# Compositor. Each icon is one or two "layers" (a cell set shaded light/mid
# /dark across its own bounding box, in the good's own hues), a shared
# outline colour, one highlight char, and optional flat single-tone
# "overrides" for small details (ties, marks, bands, sprouts...).
# A pixel's character is chosen in this priority order: override > outline >
# highlight > the shaded layer that owns that cell.
# ---------------------------------------------------------------------------
def compose(layers, outline_hex, highlight=None, highlight_hex="#ffffff",
            overrides=None, weight=(0.35, 0.65), thresholds=(0.32, 0.72)):
    overrides = overrides or {}
    highlight = highlight or set()
    cells_union = set()
    for layer in layers:
        cells_union |= layer["cells"]
    all_opaque = cells_union | set(overrides.keys()) | set(highlight)

    def is_outline(x, y):
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if not (0 <= nx < 16 and 0 <= ny < 16) or (nx, ny) not in all_opaque:
                return True
        return False

    bboxes = []
    for layer in layers:
        cs = layer["cells"]
        if cs:
            xs = [c[0] for c in cs]
            ys = [c[1] for c in cs]
            bboxes.append((min(xs), max(xs), min(ys), max(ys)))
        else:
            bboxes.append((0, 15, 0, 15))

    palette = {}
    grid = [["." for _ in range(16)] for _ in range(16)]
    for y in range(16):
        for x in range(16):
            if (x, y) in overrides:
                ch, hexv = overrides[(x, y)]
                grid[y][x] = ch
                palette[ch] = hexv
                continue
            if (x, y) not in all_opaque:
                continue
            if is_outline(x, y):
                grid[y][x] = "o"
                palette["o"] = outline_hex
                continue
            if (x, y) in highlight:
                grid[y][x] = "h"
                palette["h"] = highlight_hex
                continue
            li = None
            for i in range(len(layers) - 1, -1, -1):
                if (x, y) in layers[i]["cells"]:
                    li = i
                    break
            layer = layers[li]
            minx, maxx, miny, maxy = bboxes[li]
            wspan = max(maxx - minx, 1)
            hspan = max(maxy - miny, 1)
            tx = (x - minx) / wspan
            ty = (y - miny) / hspan
            t = weight[0] * tx + weight[1] * ty
            idx = 0 if t < thresholds[0] else (1 if t < thresholds[1] else 2)
            ch = layer["chars"][idx]
            hexv = layer["hexes"][idx]
            grid[y][x] = ch
            palette[ch] = hexv
    rows = ["".join(r) for r in grid]
    return rows, palette


# MASK_SRC collects, in local 16x16 icon coordinates, the cell sets that
# masks.png needs. The golem/jewel/ship entries are filled in from the old
# JSON once those goods' rows are available; the other five are stashed by
# the relevant icon_* builder below as a side effect of drawing that icon.
MASK_SRC = {}


# ---------------------------------------------------------------------------
# Icon builders, in the fixed order the task lists them. Each returns
# (rows, palette). A few stash a cell set into MASK_SRC for masks.png.
# ---------------------------------------------------------------------------
def icon_lampblk():
    P = old_palette("lampblk")
    body = ellipse(7, 10.3, 4.2, 2.6) | rect(3, 10, 11, 13)
    spout = poly([(10.5, 9.0), (13.5, 10.6), (10.5, 12.2)])
    handle = ellipse(2.6, 10.2, 1.0, 1.3)
    cells = clip14(body | spout | handle)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    overrides = {(11, 7): ("s", P["h"]), (10, 5): ("s", P["h"]), (11, 3): ("s", P["h"])}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"], overrides=overrides)


def icon_verm():
    P = old_palette("verm")
    lump1 = ellipse(6.2, 8.5, 3.2, 3.6)
    lump2 = ellipse(10.2, 6.3, 2.3, 2.5)
    cells = clip14(lump1 | lump2)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    facet = bresline(6, 5, 9, 11)
    overrides = {(x, y): ("f", P["d"]) for (x, y) in facet if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"], overrides=overrides)


def icon_oil():
    P = old_palette("oil")
    neck = rect(7, 2, 8, 4) | rect(6, 2, 9, 2)
    body_base = {(x, y) for (x, y) in ellipse(7.5, 9, 4.3, 4.3) if y >= 5}
    body_base |= rect(4, 5, 11, 8)
    handles = ellipse(3.4, 6.2, 1.0, 1.6) | ellipse(11.6, 6.2, 1.0, 1.6)
    neck_cells = clip14(neck)
    body_cells = clip14(body_base | handles)
    neck_layer = dict(cells=neck_cells, chars=("L", "M", "D"), hexes=(P["G"], P["g"], darken(P["g"], 0.6)))
    body_layer = dict(cells=body_cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    return compose([neck_layer, body_layer], P["o"], highlight={pick_highlight(body_cells)}, highlight_hex=P["w"])


def icon_ife():
    P = old_palette("ife")
    base = rect(3, 8, 12, 12)
    bumps = ellipse(5, 6.6, 1.8, 1.8) | ellipse(8, 5.8, 2.0, 2.0) | ellipse(11, 6.8, 1.7, 1.7)
    cells = clip14(base | bumps)
    layer = dict(cells=cells, chars=("l", "t", "f"), hexes=(P["l"], P["t"], P["f"]))
    seam = clip14(hline(4, 11, 8))
    overrides = {(x, y): ("s", P["s"]) for (x, y) in seam if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"], overrides=overrides)


def icon_iag():
    P = old_palette("iag")
    cells = clip14(rect(3, 8, 12, 12))
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["t"], P["f"], P["s"]))
    mark = rect(6, 9, 9, 10)
    overrides = {(x, y): ("k", P["o"]) for (x, y) in mark if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"], overrides=overrides)


def icon_iau():
    P = old_palette("iau")
    top = rect(4, 3, 11, 7)
    bottom = rect(5, 9, 10, 13)
    cells = clip14(top | bottom)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["t"], P["f"], P["s"]))
    return compose([layer], P["o"], highlight={pick_highlight(clip14(top))}, highlight_hex=P["h"])


def icon_wax():
    P = old_palette("wax")
    top = ellipse(7.5, 5.5, 4.3, 1.7)
    side = rect(4, 6, 11, 11)
    bottom = ellipse(7.5, 11, 4.3, 1.7)
    cells = clip14(top | side | bottom)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"])


def icon_bronze():
    P = old_palette("bronze")
    dome = ellipse(7.5, 6, 3.4, 2.6)
    flare = poly([(3, 10), (12, 10), (13.5, 12.5), (1.5, 12.5)])
    loop = ellipse(7.5, 2.6, 1.1, 1.0)
    cells = clip14(dome | flare | loop)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["t"], P["f"], P["s"]))
    return compose([layer], P["o"], highlight={pick_highlight(clip14(dome))}, highlight_hex=P["h"])


def icon_steel():
    P = old_palette("steel")
    blade = poly([(3, 12), (6, 13), (13, 4.5), (11.5, 3), (5, 10)])
    cells = clip14(blade)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"])


def icon_soap():
    P = old_palette("soap")
    bar = rect(3, 8, 12, 12)
    cut_tl = poly([(3, 8), (5, 8), (3, 10)])
    cut_tr = poly([(12, 8), (10, 8), (12, 10)])
    cut_bl = poly([(3, 12), (5, 12), (3, 10.5)])
    cut_br = poly([(12, 12), (10, 12), (12, 10.5)])
    cells = clip14(bar - cut_tl - cut_tr - cut_bl - cut_br)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["t"], P["f"], P["s"]))
    stamp = rect(6, 9, 9, 11) - rect(7, 10, 8, 10)
    overrides = {(x, y): ("k", P["f"]) for (x, y) in stamp if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"], overrides=overrides)


def icon_palmoil():
    P = old_palette("palmoil")
    top = ellipse(7.5, 5, 2.2, 1.8)
    neck = rect(6, 5, 9, 6)
    bottom = ellipse(7.5, 10, 4.2, 3.6)
    cells = clip14(top | neck | bottom)
    dark = darken(P["m"], 0.6)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], dark))
    return compose([layer], P["o"], highlight={pick_highlight(clip14(top))}, highlight_hex=lighten(P["l"], 1.2))


def icon_dye():
    P = old_palette("indigod")
    tub = poly([(3, 7), (12, 7), (11, 13), (4, 13)])
    cells_tub = clip14(tub)
    tub_layer = dict(cells=cells_tub, chars=("L", "M", "D"), hexes=(P["p"], P["P"], P["q"]))
    pool = clip14(ellipse(7.5, 7.1, 3.8, 1.5))
    dye_dark = darken(P["m"], 0.6)
    pool_layer = dict(cells=pool, chars=("l", "m", "d"), hexes=(P["l"], P["m"], dye_dark))
    drip = clip14(poly([(6.8, 12), (8.6, 12), (8.0, 14.6), (7.4, 14.6)]))
    overrides = {(x, y): ("k", P["m"]) for (x, y) in drip}
    return compose([tub_layer, pool_layer], P["o"], highlight={pick_highlight(cells_tub)},
                   highlight_hex=lighten(P["p"], 1.3), overrides=overrides)


def icon_glaze():
    P = old_palette("glaze")
    bowl = poly([(3, 8), (12, 8), (10.5, 12.5), (4.5, 12.5)])
    cells = clip14(bowl)
    dark = darken(P["m"], 0.6)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], dark))
    rim = clip14(hline(3, 12, 8) | hline(3, 12, 9))
    overrides = {(x, y): ("k", lighten(P["l"], 1.3)) for (x, y) in rim if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=lighten(P["l"], 1.3),
                   overrides=overrides)


def icon_lacquer():
    P = old_palette("lacquer")
    bowl = poly([(3, 9), (12, 9), (10.5, 13), (4.5, 13)])
    cells = clip14(bowl)
    dark = darken(P["m"], 0.62)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], dark))
    brush = rect(11, 3, 12, 8)
    tip = poly([(10.5, 7.5), (13, 7.5), (11.75, 9.5)])
    cells_brush = clip14(brush | tip)
    brush_layer = dict(cells=cells_brush, chars=("L", "M", "D"),
                        hexes=(lighten(P["o"], 2.6), P["o"], darken(P["o"], 0.6)))
    return compose([layer, brush_layer], P["o"], highlight={pick_highlight(cells)},
                   highlight_hex=lighten(P["l"], 1.2))


def icon_yarn():
    P = old_palette("yarn")
    ball = ellipse(7.5, 8.3, 4.3, 4.3)
    tail = bresline(11, 11, 13, 13)
    cells = clip14(ball | tail)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    strands = bresline(4, 6, 11, 11) | bresline(4, 10, 11, 6)
    overrides = {(x, y): ("k", P["d"]) for (x, y) in strands if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(clip14(ball))}, highlight_hex=P["h"],
                   overrides=overrides)


def icon_blood():
    P = old_palette("blood")
    bulb = ellipse(7.5, 9.3, 3.3, 3.7)
    point = poly([(7.5, 2), (9.7, 7.2), (5.3, 7.2)])
    cells = clip14(bulb | point)
    dark = darken(P["g"], 0.55)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["G"], P["g"], dark))
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["w"])


def icon_perfume():
    P = old_palette("perfume")
    cap = poly([(5.5, 1.5), (9.5, 1.5), (10.5, 3.2), (4.5, 3.2)])
    neck = rect(6, 3, 9, 5)
    body = ellipse(7.5, 9.5, 3.6, 4.0)
    cap_cells = clip14(cap)
    body_cells = clip14(neck | body)
    dark = darken(P["d"], 0.65)
    cap_layer = dict(cells=cap_cells, chars=("L", "M", "D"),
                      hexes=(lighten(P["Q"], 1.3), P["Q"], darken(P["Q"], 0.6)))
    body_layer = dict(cells=body_cells, chars=("l", "m", "d"), hexes=(P["l"], P["d"], dark))
    return compose([cap_layer, body_layer], P["o"], highlight={pick_highlight(body_cells)}, highlight_hex=P["h"])


def icon_wine():
    P = old_palette("wine")
    neck = rect(7, 2, 8, 6)
    body = rect(5, 7, 10, 13)
    cells = clip14(neck | body)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    label = rect(5, 9, 9, 11)
    overrides = {(x, y): ("k", P["L"]) for (x, y) in label if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=lighten(P["l"], 1.15),
                   overrides=overrides)


def icon_beer():
    P = old_palette("beer")
    mug = rect(4, 6, 10, 13)
    handle = ring(11.4, 9.5, 2.1, 2.5, 1.0, 1.5)
    cells = clip14(mug | handle)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    foam = clip14(hline(4, 10, 5) | hline(4, 10, 6))
    overrides = {(x, y): ("k", lighten(P["l"], 1.5)) for (x, y) in foam}
    return compose([layer], P["o"], highlight={pick_highlight(clip14(mug))}, highlight_hex=lighten(P["l"], 1.15),
                   overrides=overrides)


def icon_grapes():
    P = old_palette("grapes")
    berries = set()
    for (cx, cy) in [(7.5, 4.5), (6, 6), (9, 6), (5, 8), (8, 8), (10.5, 8), (6.5, 10), (9, 10), (7.5, 12)]:
        berries |= ellipse(cx, cy, 1.5, 1.5)
    cells = clip14(berries)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    leaf = clip14(poly([(6, 1.5), (10, 1.5), (9, 4), (7, 4)]))
    leaf_layer = dict(cells=leaf, chars=("L", "M", "D"), hexes=(P["L"], P["M"], P["D"]))
    return compose([layer, leaf_layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"])


def icon_milk():
    P = old_palette("milk")
    lid = rect(6, 2, 9, 3) | ellipse(7.5, 2, 1.7, 1.0)
    body = rect(4, 4, 11, 13)
    cells_body = clip14(body)
    cells_lid = clip14(lid)
    band = clip14(hline(4, 11, 9) | hline(4, 11, 6))
    body_layer = dict(cells=cells_body, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    lid_layer = dict(cells=cells_lid, chars=("L", "M", "D"), hexes=(P["L"], P["M"], P["D"]))
    overrides = {(x, y): ("k", P["H"]) for (x, y) in band if (x, y) in cells_body}
    return compose([lid_layer, body_layer], P["o"], highlight={pick_highlight(cells_body)}, highlight_hex=P["h"],
                   overrides=overrides)


def icon_planks():
    P = old_palette("planks")
    b1 = rect(3, 3, 12, 5)
    b2 = rect(2, 7, 11, 9)
    b3 = rect(3, 11, 12, 13)
    cells = clip14(b1 | b2 | b3)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    grain = clip14(hline(4, 11, 4) | hline(3, 10, 8) | hline(4, 11, 12))
    overrides = {(x, y): ("k", P["L"]) for (x, y) in grain if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(clip14(b1))}, highlight_hex=lighten(P["l"], 1.15),
                   overrides=overrides)


def icon_cloth():
    P = old_palette("linen")
    roll = ellipse(7.5, 8, 5.0, 5.0)
    flag = rect(11, 11, 13, 13)
    cells = clip14(roll | flag)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    rings = ring(7.5, 8, 4.6, 4.6, 3.6, 3.6) | ring(7.5, 8, 2.6, 2.6, 1.6, 1.6)
    overrides = {(x, y): ("k", P["L"]) for (x, y) in rings if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=lighten(P["l"], 1.2),
                   overrides=overrides)


def icon_hull():
    P = old_palette("hull")
    deck = hline(2, 13, 5)
    body = poly([(2, 5), (13, 5), (11, 10), (8, 13), (6.5, 13), (4, 10)])
    cells = clip14(body | deck)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    seam = clip14(hline(4, 11, 8))
    overrides = {(x, y): ("k", P["L"]) for (x, y) in seam if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=lighten(P["l"], 1.15),
                   overrides=overrides)


def icon_dyed():
    P = old_palette("dyed")
    cloth = poly([(4, 3), (11, 3), (11, 11), (9.5, 12), (8, 11), (6.5, 12), (5, 11), (4, 12)])
    cells_cloth = clip14(cloth)
    cloth_layer = dict(cells=cells_cloth, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    rod = clip14(hline(2, 13, 2))
    overrides = {(x, y): ("k", P["y"]) for (x, y) in rod}
    return compose([cloth_layer], P["o"], highlight={pick_highlight(cells_cloth)}, highlight_hex=lighten(P["l"], 1.1),
                   overrides=overrides)


def icon_batik():
    P = old_palette("batik")
    diamond = poly([(7.5, 2), (13, 8), (7.5, 14), (2, 8)])
    cells = clip14(diamond)
    dark = darken(P["m"], 0.6)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], dark))
    dots = set()
    for (cx, cy) in [(7.5, 5), (5.5, 8), (9.5, 8), (7.5, 11)]:
        dots |= ellipse(cx, cy, 0.6, 0.6)
    overrides = {(x, y): ("k", P["L"]) for (x, y) in dots if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=lighten(P["l"], 1.2),
                   overrides=overrides)


def icon_obs():
    P = old_palette("obs")
    blade = poly([(7.5, 1.5), (9, 6), (12, 9), (8.5, 10), (9.5, 14), (6.5, 10.5), (3, 9), (6.5, 6)])
    cells = clip14(blade)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    sheen = bresline(7, 3, 8, 12)
    overrides = {(x, y): ("k", P["M"]) for (x, y) in sheen if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"], overrides=overrides)


def icon_amber():
    P = old_palette("amber")
    bead = poly([(7.5, 2), (11, 4.5), (11, 9.5), (7.5, 13), (4, 9.5), (4, 4.5)])
    cells = clip14(bead)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    inclusion = ellipse(7.8, 8, 0.8, 0.8)
    overrides = {(x, y): ("k", darken(P["d"], 0.5)) for (x, y) in inclusion if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"], overrides=overrides)


def icon_spice():
    P = old_palette("spice")
    box = poly([(3, 8), (12, 8), (11, 13), (4, 13)])
    cells_box = clip14(box)
    box_layer = dict(cells=cells_box, chars=("L", "M", "D"), hexes=(P["L"], P["M"], P["D"]))
    pods = set()
    for (cx, cy) in [(5.5, 7), (7.5, 6), (9.5, 7)]:
        pods |= ellipse(cx, cy, 1.1, 1.5)
    cells_pods = clip14(pods)
    dark = darken(P["m"], 0.6)
    pod_layer = dict(cells=cells_pods, chars=("l", "m", "d"), hexes=(P["l"], P["m"], dark))
    return compose([box_layer, pod_layer], P["o"], highlight={pick_highlight(cells_pods)},
                   highlight_hex=lighten(P["l"], 1.2))


def icon_flour():
    P = old_palette("flour")
    sack = poly([(5, 3), (10, 3), (11.5, 6), (11, 12), (4, 12), (3.5, 6)])
    tie = hline(6, 9, 3)
    cells_sack = clip14(sack | tie)
    sack_layer = dict(cells=cells_sack, chars=("L", "M", "D"), hexes=(P["L"], P["M"], P["D"]))
    scoop = poly([(11, 10), (14, 9), (14.5, 10.5), (12, 12.5), (11, 12)])
    cells_scoop = clip14(scoop)
    scoop_layer = dict(cells=cells_scoop, chars=("l", "m", "d"), hexes=(P["l"], P["m"], darken(P["m"], 0.7)))
    return compose([sack_layer, scoop_layer], P["o"], highlight={pick_highlight(cells_sack)},
                   highlight_hex=lighten(P["L"], 1.15))


def icon_malt():
    P = old_palette("malt")
    tray = poly([(2, 10), (13, 10), (12, 13), (3, 13)])
    cells_tray = clip14(tray)
    tray_layer = dict(cells=cells_tray, chars=("L", "M", "D"), hexes=(P["L"], P["M"], P["D"]))
    sprouts = set()
    for x in [3, 5, 7, 9, 11]:
        sprouts |= vline(x, 6, 9)
    overrides = {(x, y): ("k", P["l"]) for (x, y) in clip14(sprouts)}
    return compose([tray_layer], P["o"], highlight={pick_highlight(cells_tray)}, highlight_hex=lighten(P["L"], 1.15),
                   overrides=overrides)


def icon_lapis():
    P = old_palette("lapis")
    rock = poly([(7.5, 2), (12, 5), (13, 9), (10, 13), (5, 13), (3, 9), (4, 5)])
    cells = clip14(rock)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    flecks = {(6, 5), (9, 6), (7, 9), (10, 10), (5, 10)}
    overrides = {(x, y): ("k", P["L"]) for (x, y) in flecks if (x, y) in cells}
    facet = bresline(7, 2, 7, 13)
    for (x, y) in facet:
        if (x, y) in cells and (x, y) not in overrides:
            overrides[(x, y)] = ("f", darken(P["m"], 0.7))
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"], overrides=overrides)


def icon_hides():
    P = old_palette("hides")
    frame = rect(2, 2, 13, 13) - rect(4, 4, 11, 11)
    hide = rect(4, 4, 11, 11)
    hide -= ellipse(7.5, 4, 1.7, 0.9)
    hide -= ellipse(7.5, 11, 1.7, 0.9)
    hide -= ellipse(4, 7.5, 0.9, 1.7)
    hide -= ellipse(11, 7.5, 0.9, 1.7)
    cells_frame = clip14(frame)
    cells_hide = clip14(hide)
    frame_layer = dict(cells=cells_frame, chars=("L", "M", "D"), hexes=(lighten(P["m"], 1.15), P["m"], P["d"]))
    hide_layer = dict(cells=cells_hide, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    pegs = {(2, 2), (13, 2), (2, 13), (13, 13)}
    overrides = {(x, y): ("k", P["o"]) for (x, y) in pegs if (x, y) in cells_frame}
    return compose([frame_layer, hide_layer], P["o"], highlight={pick_highlight(cells_hide)}, highlight_hex=P["h"],
                   overrides=overrides)


def icon_furs():
    P = old_palette("furs")
    pelt = ellipse(7, 7.5, 4.6, 4.2)
    tail = poly([(10.5, 10), (14, 12.5), (13.5, 14), (10, 12), (9.5, 10.5)])
    cells = clip14(pelt | tail)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    return compose([layer], P["o"], highlight={pick_highlight(clip14(pelt))}, highlight_hex=P["h"])


def icon_leather():
    P = old_palette("leather")
    coil = ring(7.5, 8, 5.2, 5.2, 4.2, 4.2) | ring(7.5, 8, 3.0, 3.0, 2.0, 2.0) | ellipse(7.5, 8, 0.9, 0.9)
    strap = rect(11, 6, 13, 8)
    cells = clip14(coil | strap)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"])


def icon_tallow():
    P = old_palette("tallow")
    top = poly([(4, 6), (9, 6), (12, 3.5), (7, 3.5)])
    front = rect(4, 6, 9, 13)
    side = poly([(9, 6), (12, 3.5), (12, 11), (9, 13)])
    cells = clip14(top | front | side)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    return compose([layer], P["o"], highlight={pick_highlight(clip14(top))}, highlight_hex=P["h"])


def icon_bread():
    P = old_palette("bread")
    loaf = poly([(4, 13), (4, 8), (7.5, 3), (11, 8), (11, 13)])
    dome = ellipse(7.5, 8, 3.6, 5.2)
    slashes = bresline(5.5, 7, 6.5, 4.5) | bresline(7, 7.5, 8, 5) | bresline(8.5, 8, 9.5, 5.5)
    cells = clip14((loaf | dome) - slashes)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"])


def icon_sealwax():
    P = old_palette("sealwax")
    stick = rect(7, 2, 8, 9)
    blob = ellipse(7.5, 11.3, 3.4, 2.4)
    cells_stick = clip14(stick)
    cells_blob = clip14(blob)
    stick_layer = dict(cells=cells_stick, chars=("L", "M", "D"),
                        hexes=(lighten(P["D"], 1.15), P["D"], darken(P["D"], 0.6)))
    blob_layer = dict(cells=cells_blob, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    seal = ellipse(7.5, 11.3, 1.1, 1.1)
    overrides = {(x, y): ("j", P["h"]) for (x, y) in seal if (x, y) in cells_blob}
    return compose([stick_layer, blob_layer], P["o"], highlight={pick_highlight(cells_blob)}, highlight_hex=P["h"],
                   overrides=overrides)


def icon_glass():
    P = old_palette("glass")
    rod = rect(1, 7, 7, 8)
    blob = ellipse(10.5, 8, 3.6, 4.6)
    cells_rod = clip14(rod)
    cells_blob = clip14(blob)
    rod_layer = dict(cells=cells_rod, chars=("L", "M", "D"),
                      hexes=(lighten(P["d"], 1.2), P["d"], darken(P["d"], 0.6)))
    blob_layer = dict(cells=cells_blob, chars=("l", "m", "d"), hexes=(P["l"], P["G"], P["d"]))
    return compose([rod_layer, blob_layer], P["o"], highlight={pick_highlight(cells_blob)}, highlight_hex=P["w"])


def icon_winpane():
    P = old_palette("winpane")
    octagon = poly([(5, 2), (11, 2), (13, 4), (13, 10), (11, 12), (5, 12), (3, 10), (3, 4)])
    cells = clip14(octagon)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["h"], P["m"], darken(P["m"], 0.65)))
    cames = clip14(hline(3, 13, 7) | vline(8, 2, 12))
    overrides = {(x, y): ("k", P["d"]) for (x, y) in cames if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["w"], overrides=overrides)


def icon_pottery():
    P = old_palette("pottery")
    neck = rect(6, 2, 9, 4)
    lip = hline(5, 10, 2)
    body = poly([(4, 5), (11, 5), (12, 9), (10, 13), (5, 13), (3, 9)])
    handle = ring(12.4, 7.5, 2.0, 2.3, 1.0, 1.3)
    cells = clip14(neck | lip | body | handle)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    band = clip14(hline(4, 11, 8))
    band_cells = {(x, y) for (x, y) in band if (x, y) in cells}
    overrides = {(x, y): ("g", P["L"]) for (x, y) in band_cells}
    MASK_SRC["pottery_glaze"] = band_cells
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=lighten(P["l"], 1.15),
                   overrides=overrides)


def icon_porcel():
    P = old_palette("porcel")
    neck = rect(7, 2, 8, 4)
    body = poly([(5, 4), (10, 4), (11, 7), (9, 12), (9, 13), (6, 13), (6, 12), (4, 7)])
    cells = clip14(neck | body)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    band = clip14(hline(4, 11, 7))
    band_cells = {(x, y) for (x, y) in band if (x, y) in cells}
    overrides = {(x, y): ("g", P["L"]) for (x, y) in band_cells}
    MASK_SRC["porcel_decor"] = band_cells
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=lighten(P["l"], 1.2),
                   overrides=overrides)


def icon_rockc():
    P = old_palette("rockc")
    spikes = poly([(3, 13), (4, 6), (6, 9), (7.5, 2), (9, 9), (11, 5), (13, 13)])
    cells = clip14(spikes)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    facet = bresline(7.5, 2, 7.5, 13)
    overrides = {(x, y): ("k", P["D"]) for (x, y) in facet if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["h"], overrides=overrides)


def icon_jade():
    P = old_palette("jade")
    disc = ring(7.5, 7.5, 6.2, 6.2, 3.1, 3.1)
    cells = clip14(disc)
    light = lighten(P["m"], 1.3)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(light, P["m"], P["d"]))
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["H"])


def icon_grain():
    P = old_palette("grain")
    top = poly([(5, 3), (10, 3), (10.5, 6), (4.5, 6)])
    waist = rect(6.5, 6.5, 8.5, 7.5)
    bottom = poly([(4, 8), (11, 8), (10, 13), (5, 13)])
    cells = clip14(top | waist | bottom)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    tie = clip14(hline(5, 10, 7))
    overrides = {(x, y): ("k", P["D"]) for (x, y) in tie if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(clip14(top))}, highlight_hex=lighten(P["l"], 1.15),
                   overrides=overrides)


def icon_body():
    P = old_palette("body")
    torso = poly([(4, 3), (11, 3), (12, 6), (11, 13), (4, 13), (3, 6)])
    arm_l = rect(1, 6, 2, 9)
    arm_r = rect(13, 6, 14, 9)
    cells = clip14(torso | arm_l | arm_r)
    hi = lighten(P["l"], 1.3)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["f"], P["d"]))
    flecks = {(6, 7), (9, 7), (6, 10), (9, 10), (7, 5)}
    overrides = {(x, y): ("k", P["k"]) for (x, y) in flecks if (x, y) in cells}
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=hi, overrides=overrides)


def icon_timber():
    P = old_palette("timber")
    log = ellipse(7.5, 7.5, 6.2, 6.2)
    cells = clip14(log)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    rings = ring(7.5, 7.5, 4.6, 4.6, 3.6, 3.6) | ring(7.5, 7.5, 2.4, 2.4, 1.4, 1.4)
    overrides = {(x, y): ("k", P["D"]) for (x, y) in rings if (x, y) in cells}
    pith = ellipse(7.5, 7.5, 0.8, 0.8)
    for (x, y) in pith:
        if (x, y) in cells:
            overrides[(x, y)] = ("j", P["H"])
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["H"], overrides=overrides)


def icon_saltfish():
    P = old_palette("saltfish")
    body = poly([(2, 8), (5, 5.5), (11, 6), (13, 8), (11, 10), (5, 10.5)])
    tail = poly([(11, 6), (14, 4), (14, 7), (12.5, 8), (14, 9), (14, 12), (11, 10)])
    cells = clip14(body | tail)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    split = bresline(3, 8, 12, 8)
    overrides = {(x, y): ("k", P["d"]) for (x, y) in split if (x, y) in cells}
    eye = (4, 7)
    if eye in cells:
        overrides[eye] = ("j", P["k"])
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=lighten(P["l"], 1.15),
                   overrides=overrides)


def icon_lacqw():
    P = old_palette("lacqw")
    lid = poly([(3, 5), (12, 5), (13, 7), (2, 7)])
    box = rect(3, 7, 12, 13)
    cells_lid = clip14(lid)
    cells_box = clip14(box)
    lid_layer = dict(cells=cells_lid, chars=("r", "l", "m"), hexes=(P["h"], P["l"], P["m"]))
    box_layer = dict(cells=cells_box, chars=("H", "L", "M"), hexes=(P["H"], P["L"], P["M"]))
    knob = clip14(ellipse(7.5, 4.3, 0.9, 0.7))
    overrides = {(x, y): ("k", P["h"]) for (x, y) in knob}
    MASK_SRC["lacqw_body"] = cells_box
    return compose([lid_layer, box_layer], P["o"], highlight={pick_highlight(cells_box)}, highlight_hex=P["H"],
                   overrides=overrides)


def icon_autom():
    P = old_palette("autom")
    head = rect(6, 2, 9, 4)
    torso = rect(5, 5, 10, 11)
    legs = rect(6, 12, 9, 13)
    cells = clip14(head | torso | legs)
    layer = dict(cells=cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    eyes = {(7, 3), (8, 3)}
    heart_patch = rect(7, 7, 8, 8)
    key = poly([(11, 6.5), (13.6, 6), (14.2, 6.7), (13.6, 7.4), (11, 7)]) | rect(10, 6, 11, 7)
    overrides = {}
    for (x, y) in eyes:
        overrides[(x, y)] = ("e", P["e"])
    heart_cells = clip14(heart_patch)
    for (x, y) in heart_cells:
        overrides[(x, y)] = ("H", P["H"])
    for (x, y) in clip14(key):
        overrides[(x, y)] = ("k", P["B"])
    MASK_SRC["autom_heart"] = heart_cells
    return compose([layer], P["o"], highlight={pick_highlight(cells)}, highlight_hex=P["w"], overrides=overrides)


def icon_court():
    P = old_palette("court")
    bodice = poly([(6, 2), (9, 2), (9.5, 6), (5.5, 6)])
    skirt = poly([(5.5, 6), (9.5, 6), (13, 13), (2, 13)])
    cells_bodice = clip14(bodice)
    cells_skirt = clip14(skirt)
    all_cells = cells_bodice | cells_skirt
    body_layer = dict(cells=all_cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    collar = clip14(hline(6, 9, 2))
    hem = clip14(hline(2, 13, 12) | hline(2, 13, 13))
    trim_cells = {(x, y) for (x, y) in (collar | hem) if (x, y) in all_cells}
    overrides = {(x, y): ("T", P["L"]) for (x, y) in trim_cells}
    MASK_SRC["court_body"] = all_cells - trim_cells
    MASK_SRC["court_trim"] = trim_cells
    return compose([body_layer], P["o"], highlight={pick_highlight(cells_skirt)}, highlight_hex=P["h"],
                   overrides=overrides)


def icon_heart():
    P = old_palette("h2")
    lobe_l = ellipse(6.5, 6.6, 2.5, 2.2)
    lobe_r = ellipse(9.5, 6.6, 2.5, 2.2)
    point = poly([(4.2, 7.3), (11.8, 7.3), (8, 12.3)])
    heart_cells = clip14(lobe_l | lobe_r | point)
    heart_layer = dict(cells=heart_cells, chars=("l", "m", "d"), hexes=(P["l"], P["m"], P["d"]))
    cage = rect(2, 2, 13, 13) - rect(3, 3, 12, 12)
    cage_cells = clip14(cage)
    overrides = {}
    for (x, y) in cage_cells:
        overrides[(x, y)] = ("k", P["B"] if (x + y) % 2 == 0 else P["b"])
    return compose([heart_layer], P["o"], highlight={pick_highlight(heart_cells)}, highlight_hex=P["h"],
                   overrides=overrides)


# ---------------------------------------------------------------------------
# The fixed order the task lists the 51 redraws in, plus "heart" as the 52nd.
# ---------------------------------------------------------------------------
BUILDERS = [
    ("lampblk", icon_lampblk), ("verm", icon_verm), ("oil", icon_oil),
    ("ife", icon_ife), ("iag", icon_iag), ("iau", icon_iau), ("wax", icon_wax),
    ("bronze", icon_bronze), ("steel", icon_steel), ("soap", icon_soap),
    ("palmoil", icon_palmoil), ("dye", icon_dye), ("glaze", icon_glaze),
    ("lacquer", icon_lacquer), ("yarn", icon_yarn), ("blood", icon_blood),
    ("perfume", icon_perfume), ("wine", icon_wine), ("beer", icon_beer),
    ("grapes", icon_grapes), ("milk", icon_milk), ("planks", icon_planks),
    ("cloth", icon_cloth), ("hull", icon_hull), ("dyed", icon_dyed),
    ("batik", icon_batik), ("obs", icon_obs), ("amber", icon_amber),
    ("spice", icon_spice), ("flour", icon_flour), ("malt", icon_malt),
    ("lapis", icon_lapis), ("hides", icon_hides), ("furs", icon_furs),
    ("leather", icon_leather), ("tallow", icon_tallow), ("bread", icon_bread),
    ("sealwax", icon_sealwax), ("glass", icon_glass), ("winpane", icon_winpane),
    ("pottery", icon_pottery), ("porcel", icon_porcel), ("rockc", icon_rockc),
    ("jade", icon_jade), ("grain", icon_grain), ("body", icon_body),
    ("timber", icon_timber), ("saltfish", icon_saltfish), ("lacqw", icon_lacqw),
    ("autom", icon_autom), ("court", icon_court), ("heart", icon_heart),
]

# The three goods that are new and start from another good's pixels.
SOURCE_FOR = {"dye": "indigod", "cloth": "linen", "heart": "h2"}

# masks.png cell order (0..12); cell 13 is the spare/empty one.
MASK_KEYS = [
    "golem_heart", "golem_eyes", "golem_body", "jewel_gem", "jewel_metal",
    "ship_hull", "ship_sails", "court_body", "court_trim", "pottery_glaze",
    "porcel_decor", "lacqw_body", "autom_heart",
]


def rows_alpha(rows):
    return tuple(ch != "." for row in rows for ch in row)


def mask_to_int(mask):
    v = 0
    for i, b in enumerate(mask):
        if b:
            v |= 1 << i
    return v


def old_char_cells(gid, chars):
    rows = old_rows(gid)
    return {(x, y) for y, row in enumerate(rows) for x, ch in enumerate(row) if ch in chars}


def main():
    assert len(BUILDERS) == 52, f"expected 52 icons, got {len(BUILDERS)}"
    order = [gid for gid, _ in BUILDERS]
    assert len(set(order)) == 52, "duplicate good id in BUILDERS"
    for gid in ("dye", "cloth", "heart"):
        assert gid not in GOODS, f"{gid} unexpectedly already exists in icon_pixels.json"

    # The old 288 cells are always the top 288 px of whatever is on disk right
    # now, whether that is the pristine sheet (first run) or a sheet this same
    # script already appended to (a later run) -- we only ever add rows below
    # them, never touch them. That makes this self-correcting across any
    # number of runs, on any machine, with or without a scratchpad already
    # holding a copy. We still keep a copy of the pristine original for the
    # record, the first time we see it.
    os.makedirs(SCRATCH, exist_ok=True)
    current_on_disk = Image.open(SHEET_PATH).convert("RGBA")
    assert current_on_disk.size[0] == 256, f"unexpected sheet width {current_on_disk.size[0]}"
    assert current_on_disk.size[1] >= 288, f"sheet shorter than the old 288 cells: {current_on_disk.size}"
    old_img = current_on_disk.crop((0, 0, 256, 288))
    if not os.path.exists(BEFORE_PATH):
        old_img.save(BEFORE_PATH)

    # --- build every new icon -------------------------------------------------
    NEW = {}
    for gid, fn in BUILDERS:
        rows, palette = fn()
        assert len(rows) == 16, f"{gid}: expected 16 rows, got {len(rows)}"
        assert all(len(r) == 16 for r in rows), f"{gid}: a row is not 16 chars wide"
        assert all(c == "." for c in rows[0]), f"{gid}: row 0 is not empty (margin)"
        assert all(c == "." for c in rows[15]), f"{gid}: row 15 is not empty (margin)"
        assert all(r[0] == "." for r in rows), f"{gid}: column 0 is not empty (margin)"
        assert all(r[15] == "." for r in rows), f"{gid}: column 15 is not empty (margin)"
        for ch in "".join(rows):
            assert ch == "." or ch in palette, f"{gid}: char {ch!r} used but not in its palette"
        NEW[gid] = (rows, palette)

    # --- mask sources from the untouched old goods -----------------------------
    MASK_SRC["golem_heart"] = old_char_cells("golem", {"H"})
    MASK_SRC["golem_eyes"] = old_char_cells("golem", {"e"})
    MASK_SRC["golem_body"] = old_char_cells("golem", set("lmdw"))
    MASK_SRC["jewel_gem"] = old_char_cells("jewel", set("HLMD"))
    MASK_SRC["jewel_metal"] = old_char_cells("jewel", set("lmd"))
    MASK_SRC["ship_hull"] = old_char_cells("ship", set("hlmd"))
    MASK_SRC["ship_sails"] = old_char_cells("ship", set("HLM"))
    for key in MASK_KEYS:
        assert key in MASK_SRC, f"mask source {key!r} was never populated"
        assert len(MASK_SRC[key]) > 0, f"mask source {key!r} is empty"

    # --- no new icon identical in silhouette to its own old icon ---------------
    for gid in order:
        if gid in GOODS:
            old_m = old_alpha(gid)
            new_m = rows_alpha(NEW[gid][0])
            assert old_m != new_m, f"{gid}: new icon has the same silhouette as the old one"

    # --- every new icon must differ from every OTHER cell on the whole sheet ---
    def sheet_cell_alpha(img, cell_index):
        col = cell_index % COLUMNS
        row = cell_index // COLUMNS
        box = (col * 16, row * 16, col * 16 + 16, row * 16 + 16)
        crop = img.crop(box)
        return tuple(px[3] > 0 for px in crop.getdata())

    old_masks_int = [mask_to_int(sheet_cell_alpha(old_img, i)) for i in range(OLD_CELLS)]
    new_masks_int = [mask_to_int(rows_alpha(NEW[gid][0])) for gid in order]
    all_masks_int = old_masks_int + new_masks_int
    n_old = len(old_masks_int)

    worst = None
    for i, gid in enumerate(order):
        mine = new_masks_int[i]
        my_global = n_old + i
        for j, other in enumerate(all_masks_int):
            if j == my_global:
                continue
            d = bin(mine ^ other).count("1")
            assert d > 8, f"{gid} (new cell {288 + i}) only differs from sheet cell {j} by {d} px"
            if worst is None or d < worst[0]:
                worst = (d, gid, j)

    print(f"silhouette check: {len(order)} new icons each differ from all {len(all_masks_int) - 1} "
          f"other cells by > 8 px (closest pair: {worst[0]} px, {worst[1]} vs cell {worst[2]})")

    # --- assemble the final icons.png: old 288 cells untouched + new cells -----
    new_rows_count = math.ceil(len(order) / COLUMNS)
    new_h = new_rows_count * CELL
    final_img = Image.new("RGBA", (256, 288 + new_h), (0, 0, 0, 0))
    final_img.paste(old_img, (0, 0))

    unchanged = final_img.crop((0, 0, 256, 288))
    assert list(unchanged.getdata()) == list(old_img.getdata()), "the old 288 cells changed!"

    icons_map = {}
    for i, gid in enumerate(order):
        rows, palette = NEW[gid]
        col = i % COLUMNS
        row = 18 + i // COLUMNS
        cell_index = row * COLUMNS + col
        icons_map[gid] = cell_index
        ox, oy = col * 16, row * 16
        for y in range(16):
            for x in range(16):
                ch = rows[y][x]
                if ch == ".":
                    continue
                r, g, b = hex_to_rgb(palette[ch])
                final_img.putpixel((ox + x, oy + y), (r, g, b, 255))

    final_img.save(SHEET_PATH)
    print(f"wrote {SHEET_PATH}  ({final_img.size[0]}x{final_img.size[1]}, "
          f"{OLD_CELLS} old cells + {len(order)} new cells)")

    # --- masks.png: 16 columns x 1 row, cells 0..12 defined, 13 spare ----------
    mask_img = Image.new("RGBA", (256, 16), (0, 0, 0, 0))
    for mid, key in enumerate(MASK_KEYS):
        ox = mid * 16
        for (x, y) in MASK_SRC[key]:
            mask_img.putpixel((ox + x, y), (255, 255, 255, 255))
    mask_img.save(MASKS_PATH)
    print(f"wrote {MASKS_PATH}  (masks 0-{len(MASK_KEYS) - 1} defined, cell {len(MASK_KEYS)} is the spare)")

    # --- tools/variety_icons.json ----------------------------------------------
    index = {
        "iconAtlas": "icons",
        "maskAtlas": "masks",
        "atlases": [{"id": "masks", "file": "sprites/masks.png", "cell": 16, "columns": 16}],
        "icons": icons_map,
        "layers": {
            "golem": [{"match": "heart", "mask": 0}, {"match": "fit:smalt-eyes", "mask": 1},
                      {"match": "soil", "mask": 2}],
            "jewel": [{"match": "gem", "mask": 3}, {"match": "metal", "mask": 4}],
            "ship": [{"match": "wood", "mask": 5}],
            "court": [{"match": "colour", "mask": 7}, {"match": "fur", "mask": 8}],
            "pottery": [{"match": "glaze", "mask": 9}],
            "porcel": [{"match": "decor", "mask": 10}],
            "lacqw": [{"match": "hue", "mask": 11}, {"match": "wood", "mask": None}],
            "autom": [{"match": "heart", "mask": 12}],
        },
    }
    with open(INDEX_PATH, "w") as f:
        json.dump(index, f, indent=2)
        f.write("\n")
    print(f"wrote {INDEX_PATH}")

    build_preview(order, NEW, icons_map)

    print(f"\n{'good':10s} {'cell':>5s}")
    for gid in order:
        print(f"{gid:10s} {icons_map[gid]:>5d}")

    return NEW, order, icons_map


def _render4x(rows, palette, scale=4):
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    for y in range(16):
        for x in range(16):
            ch = rows[y][x]
            if ch == ".":
                continue
            r, g, b = hex_to_rgb(palette[ch])
            img.putpixel((x, y), (r, g, b, 255))
    return img.resize((16 * scale, 16 * scale), Image.NEAREST)


def build_preview(order, NEW, icons_map):
    """Old/source vs new, side by side, plus the 13 masks over their icon."""
    SCALE = 4
    cell = 16 * SCALE
    pad = 6
    label_h = 11
    block_w = cell * 2 + pad * 3
    block_h = cell + label_h + pad * 2
    cols = 4
    n = len(order)
    rows_n = math.ceil(n / cols)
    header_h = 18
    section_gap = 24

    mcols = 5
    mrows = math.ceil(len(MASK_KEYS) / mcols)
    mblock_w = cell + pad * 2
    mblock_h = cell + label_h + pad * 2

    W = max(cols * block_w, mcols * mblock_w) + pad
    H = header_h + rows_n * block_h + section_gap + header_h + mrows * mblock_h + pad

    BG = (28, 28, 32, 255)
    img = Image.new("RGBA", (W, H), BG)
    draw = ImageDraw.Draw(img)
    font = ImageFont.load_default()

    draw.text((pad, 4), "Deliverable 1: old/source (left) vs new (right), 4x", fill=(235, 235, 235, 255), font=font)
    y0 = header_h
    for i, gid in enumerate(order):
        col = i % cols
        row = i // cols
        bx = pad + col * block_w
        by = y0 + row * block_h
        src = SOURCE_FOR.get(gid, gid)
        oimg = _render4x(old_rows(src), old_palette(src), SCALE)
        nrows, npal = NEW[gid]
        nimg = _render4x(nrows, npal, SCALE)
        img.paste(oimg, (bx, by), oimg)
        img.paste(nimg, (bx + cell + pad, by), nimg)
        label = f"{gid}* {icons_map[gid]}" if gid in SOURCE_FOR else f"{gid} {icons_map[gid]}"
        draw.text((bx, by + cell + 1), label, fill=(220, 220, 220, 255), font=font)

    y1 = y0 + rows_n * block_h + section_gap
    draw.text((pad, y1 - header_h + 4), "Deliverable 2: masks (green) over their icon, 4x",
              fill=(235, 235, 235, 255), font=font)

    mask_icon_source = {
        "golem_heart": ("golem", None), "golem_eyes": ("golem", None), "golem_body": ("golem", None),
        "jewel_gem": ("jewel", None), "jewel_metal": ("jewel", None),
        "ship_hull": ("ship", None), "ship_sails": ("ship", None),
        "court_body": (None, "court"), "court_trim": (None, "court"),
        "pottery_glaze": (None, "pottery"), "porcel_decor": (None, "porcel"),
        "lacqw_body": (None, "lacqw"), "autom_heart": (None, "autom"),
    }
    GREEN = (60, 240, 120, 160)
    for mid, key in enumerate(MASK_KEYS):
        col = mid % mcols
        row = mid // mcols
        bx = pad + col * mblock_w
        by = y1 + row * mblock_h
        old_gid, new_gid = mask_icon_source[key]
        if old_gid:
            rows_, pal_ = old_rows(old_gid), old_palette(old_gid)
        else:
            rows_, pal_ = NEW[new_gid]
        base = _render4x(rows_, pal_, SCALE)
        overlay = Image.new("RGBA", base.size, (0, 0, 0, 0))
        odraw = ImageDraw.Draw(overlay)
        for (x, y) in MASK_SRC[key]:
            odraw.rectangle([x * SCALE, y * SCALE, x * SCALE + SCALE - 1, y * SCALE + SCALE - 1], fill=GREEN)
        combined = Image.alpha_composite(base, overlay)
        img.paste(combined, (bx, by), combined)
        draw.text((bx, by + cell + 1), f"{mid}:{key}", fill=(220, 220, 220, 255), font=font)

    img.save(PREVIEW_PATH)
    print(f"wrote {PREVIEW_PATH}  ({W}x{H})")


if __name__ == "__main__":
    main()
