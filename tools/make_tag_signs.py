#!/usr/bin/env python3
"""Draw resources/economy/sprites/tags.png and tools/tag_signs.json: one 16x16
symbol per tag NAMESPACE (a single mark from tools/sign_vocab.json, doubled to
2x and tinted neutral grey-white, since the lab recolours it per tag), plus one
symbol for every TAG whose colour alone would not tell it apart from its
siblings (a namespace mark + a distinguishing mark, both at 1x, tinted in that
tag's own colour, exactly like the goods' compound signs).

Also rewrites tools/sign_vocab.json with the new 5x7 marks this needed, added
after every existing mark (byte-for-byte unchanged) so the shared vocabulary
stays one file.

Standard library + Pillow. Run from the repo root with no arguments:
    python3 tools/make_tag_signs.py

Does not read or write resources/economy/sprites/icons.png or masks.png.
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFont

VOCAB_PATH = "tools/sign_vocab.json"
TAG_SIGNS_JSON = "tools/tag_signs.json"
TAGS_PNG = "resources/economy/sprites/tags.png"

# Session scratchpad -- outside the repo on purpose, so `git status` never
# sees the preview. Not a repo artifact; only used for eyeballing this run.
SCRATCHPAD = (
    "/private/tmp/claude-501/-Users-maximbarganov-Documents-GitHub-project-nikitin/"
    "eb16bb98-e7af-458e-abfa-e46373be5589/scratchpad"
)
PREVIEW_PATH = os.path.join(SCRATCHPAD, "tag_signs_x4.png")

CELL = 16
COLUMNS = 16
MARK_W, MARK_H = 5, 7

# ---------------------------------------------------------------------------
# New marks this job adds to the shared vocabulary. Existing marks (read from
# sign_vocab.json) are carried over untouched; these are appended after them.
# Style: bold, symmetric where possible, readable at 1x -- see the report for
# which of these turned out weak.
# ---------------------------------------------------------------------------
NEW_MARKS = {
    "HD": {
        "meaning": "hide (stretched, corners tabbed)",
        "rows": [
            "#.#.#",
            ".###.",
            "#####",
            "#####",
            "#####",
            ".###.",
            "#.#.#",
        ],
    },
    "FU": {
        "meaning": "fur (a pelt with a tail curling clear of the body)",
        "rows": [
            "#...#",
            "#...#",
            ".###.",
            ".#.#.",
            ".###.",
            "..#.#",
            "...##",
        ],
    },
    "SC": {
        "meaning": "spice (a star of pods)",
        "rows": [
            "..#..",
            ".....",
            ".....",
            "#.#.#",
            ".....",
            ".....",
            "..#..",
        ],
    },
    "FA": {
        "meaning": "fat (a drop with a flat top)",
        "rows": [
            ".###.",
            "#####",
            "#####",
            "#...#",
            "#...#",
            ".###.",
            ".....",
        ],
    },
    "DY": {
        "meaning": "dye or colour (a drop inside a circle)",
        "rows": [
            ".###.",
            "#...#",
            "#.#.#",
            "#.#.#",
            "#...#",
            ".###.",
            ".....",
        ],
    },
    "MT": {
        "meaning": "metal (an ingot)",
        "rows": [
            ".....",
            ".###.",
            ".###.",
            "#####",
            "#####",
            "#####",
            ".....",
        ],
    },
    "BL": {
        "meaning": "blood (a drop with a spatter dot)",
        "rows": [
            ".#...",
            "..#..",
            ".#.#.",
            "#...#",
            "#...#",
            ".###.",
            ".....",
        ],
    },
    "DC": {
        "meaning": "decor (a flat painted rim, seen edge-on, with a bead on top)",
        "rows": [
            "..#..",
            "#####",
            "#...#",
            "#####",
            ".....",
            ".....",
            ".....",
        ],
    },
    "GZ": {
        "meaning": "glaze (a bowl with a rim line)",
        "rows": [
            "#####",
            ".....",
            "#...#",
            "#...#",
            "#...#",
            ".###.",
            ".....",
        ],
    },
    "GD": {
        "meaning": "grade (three steps rising to the right)",
        "rows": [
            ".....",
            "....#",
            "....#",
            "..###",
            "..###",
            "#####",
            "#####",
        ],
    },
    "PZ": {
        "meaning": "prized (a small crown)",
        "rows": [
            "#.#.#",
            "#.#.#",
            "#####",
            "#####",
            ".....",
            ".....",
            ".....",
        ],
    },
    "ST": {
        "meaning": "site (a flag on a post, on a ground line)",
        "rows": [
            ".##..",
            ".####",
            ".##..",
            ".#...",
            ".#...",
            ".#...",
            "#####",
        ],
    },
    "ND": {
        "meaning": "need (an open hand, mitten-shaped)",
        "rows": [
            "##.##",
            "#####",
            "#####",
            "#####",
            ".###.",
            "..#..",
            ".....",
        ],
    },
    "St": {
        "meaning": "steel (iron with a bar on top)",
        "rows": [
            "#####",
            "...##",
            "..#.#",
            ".##..",
            "#..#.",
            "#..#.",
            ".##..",
        ],
    },
    "SR": {
        "meaning": "superb (a five-point star burst)",
        "rows": [
            "..#..",
            "#.#.#",
            ".###.",
            "#####",
            ".###.",
            "#.#.#",
            "..#..",
        ],
    },
    "CO": {
        "meaning": "cold (a snowflake: a plus with corner dots)",
        "rows": [
            "#...#",
            "..#..",
            "..#..",
            "#####",
            "..#..",
            "..#..",
            "#...#",
        ],
    },
    "P1": {
        "meaning": "one pip (coarse grade)",
        "rows": [
            ".....",
            ".....",
            ".....",
            "..#..",
            ".....",
            ".....",
            ".....",
        ],
    },
    "P2": {
        "meaning": "two pips (common grade)",
        "rows": [
            ".....",
            ".....",
            ".....",
            ".#.#.",
            ".....",
            ".....",
            ".....",
        ],
    },
    "P3": {
        "meaning": "three pips (fine grade)",
        "rows": [
            ".....",
            ".....",
            ".....",
            "#.#.#",
            ".....",
            ".....",
            ".....",
        ],
    },
}

# Namespaces, in the order they lay out in the atlas (16 per row).
NAMESPACES = [
    ("soil", "EA"), ("grain", "GR"), ("grape", "FR"), ("wood", "TR"),
    ("fleece", "BS"), ("hide", "HD"), ("fur", "FU"), ("fish", "FS"),
    ("milk", "DP"), ("spice", "SC"), ("fat", "FA"), ("cloth", "CL"),
    ("colour", "DY"), ("heart", "HT"), ("fit", "GE"), ("gem", "GM"),
    ("metal", "MT"), ("hue", "PW"), ("blood", "BL"), ("decor", "DC"),
    ("glass", "GL"), ("glaze", "GZ"), ("spark", "FI"), ("scent", "AI"),
    ("grade", "GD"), ("work", "TL"), ("prized", "PZ"),
    ("site", "ST"),
    ("kind", "SQ"), ("need", "ND"),
]
NAMESPACE_TINT = ("#e8e8e8", "#bdbdbd", "#8a8a8a")  # light, mid, dark

# Tags that need a symbol of their own: (id, namespace mark, distinguishing
# mark, colour). hue:black overrides the derived shades (see below).
TAGS = [
    ("heart:arsenic", "HT", "As", "#8FBF6A"),
    ("heart:antimony", "HT", "Sb", "#9FB4D9"),
    ("heart:bismuth", "HT", "Bi", "#E7A6D8"),
    ("metal:bronze", "MT", "Cu", "#C58A3D"),
    ("metal:iron", "MT", "Fe", "#7D8790"),
    ("metal:steel", "MT", "St", "#C9D3DC"),
    ("metal:silver", "MT", "Ag", "#E3E8EE"),
    ("metal:gold", "MT", "Au", "#F2C230"),
    ("grade:coarse", "GD", "P1", "#8A7A6B"),
    ("grade:common", "GD", "P2", "#B9B9B9"),
    ("grade:fine", "GD", "P3", "#6BB5E8"),
    ("grade:superb", "GD", "SR", "#F2C230"),
    ("work:field", "TL", "GR", "#6B8F3B"),
    ("work:water", "TL", "WA", "#3FA39B"),
    ("work:quarry", "TL", "GM", "#8A8F94"),
    ("work:cold", "TL", "CO", "#BFD9E8"),
    ("prized:jadefolk", "PZ", "GM", "#4FB58A"),
    ("prized:lakefolk", "PZ", "WA", "#3F7FBF"),
    ("prized:steelfolk", "PZ", "Fe", "#9FB4D9"),
    ("site:coast", "ST", "WA", "#3F7FBF"),
    ("fit:bronze-joints", "GE", "Cu", "#C58A3D"),
    ("fit:smalt-eyes", "GE", "Ko", "#2B4FB5"),
    ("fit:caustic-nerves", "GE", "Ag", "#DDE6EE"),
    ("fit:clockwork-hands", "GE", "GE", "#D9B34A"),
    ("fit:spyglass", "GE", "LN", "#C9A24A"),
    ("fit:charts", "GE", "BK", "#E8DDB5"),
    ("fit:armed", "GE", "WP", "#5A5F66"),
    ("fit:provisioned", "GE", "CP", "#B5703B"),
    ("blood:gilded", "BL", "Au", "#F2C230"),
    ("glass:clear", "GL", "AI", "#DDF1F7"),
    ("glaze:blue", "GZ", "Ko", "#2B4FB5"),
    ("decor:blue-and-white", "DC", "Ko", "#2B4FB5"),
    ("decor:enamelled", "DC", "GL", "#3FA39B"),
    ("spark:blue", "FI", "Cu", "#3F7FE8"),
    ("spark:bright", "FI", "AI", "#FFF2B5"),
    ("scent:resin", "AI", "RS", "#B5703B"),
    ("scent:sweet", "AI", "FL", "#F2B5C9"),
    ("hue:red", "PW", "Hg", "#D9381E"),
    ("hue:black", "PW", "FI", "#26262B"),
]
# hue:black's mid is near-black, which would derive a dark band and an
# outline both too close to invisible on the dark canvas; the brief calls
# for a lighter override triple instead of the usual single-colour derivation.
# (First pass used the brief's literal #6a6a72/#44444c/#26262b, but next to
# the #2a2d34 canvas the mid and dark bands still all but vanished -- lifted
# further here so the shape actually reads on both backgrounds.)
HUE_BLACK_OVERRIDE = ("#9a9aa4", "#6e6e78", "#48484f")


def hexcolor(h):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


# ---------------------------------------------------------------------------
# Tint derivation. signs.png's own compound signs turn out (checked by
# sampling several cells) to come from a single "mid" colour by a fixed rule:
#   light   = mid + (255 - mid) * 0.55      (toward white)
#   dark    = mid * 0.72                    (toward black)
#   outline = mid * 0.18                    (toward black, further)
# each channel independently, truncated. Reused here so tags.png's own new
# colours sit in the same family as the goods' signs.
# ---------------------------------------------------------------------------

def shades_from_mid(mid):
    light = tuple(min(255, int(c + (255 - c) * 0.55)) for c in mid)
    dark = tuple(int(c * 0.72) for c in mid)
    outline = tuple(int(c * 0.18) for c in mid)
    return light, mid, dark, outline


def shades_from_explicit(light_hex, mid_hex, dark_hex):
    mid = hexcolor(mid_hex)
    outline = tuple(int(c * 0.18) for c in mid)
    return hexcolor(light_hex), mid, hexcolor(dark_hex), outline


NAMESPACE_SHADES = shades_from_explicit(*NAMESPACE_TINT)

# ---------------------------------------------------------------------------
# Drawing. A mark is 7 rows of 5 chars, '#' = ink. Vertical tint band is
# local to the mark's own drawing: for a 1x mark (7 rows), top 2 rows light,
# middle 3 mid, bottom 2 dark; for a 2x-doubled mark (14 rows), rows 0-4
# light, 5-9 mid, 10-13 dark -- exactly tools/sign_vocab.json's own layout
# note, and the brief's restatement of it.
# ---------------------------------------------------------------------------

def band_index(local_row, scale):
    if scale == 1:
        if local_row < 2:
            return 0
        if local_row < 5:
            return 1
        return 2
    else:
        if local_row <= 4:
            return 0
        if local_row <= 9:
            return 1
        return 2


def draw_mark(canvas, rows, x0, y0, scale, shades):
    light, mid, dark, _outline = shades
    bands = (light, mid, dark)
    for r, row in enumerate(rows):
        for c, ch in enumerate(row):
            if ch != "#":
                continue
            for dy in range(scale):
                local_row = r * scale + dy
                color = bands[band_index(local_row, scale)]
                py = y0 + r * scale + dy
                for dx in range(scale):
                    px = x0 + c * scale + dx
                    canvas[py][px] = color + (255,)


def apply_outline(canvas, outline_color):
    h = len(canvas)
    w = len(canvas[0])

    def filled(x, y):
        return 0 <= x < w and 0 <= y < h and canvas[y][x][3] == 255

    cells = []
    for y in range(h):
        for x in range(w):
            if canvas[y][x][3] == 0 and (
                filled(x - 1, y) or filled(x + 1, y) or
                filled(x, y - 1) or filled(x, y + 1)
            ):
                cells.append((x, y))
    for x, y in cells:
        canvas[y][x] = outline_color + (255,)


def blank_cell():
    return [[(0, 0, 0, 0)] * CELL for _ in range(CELL)]


def render_namespace(vocab, mark_key):
    canvas = blank_cell()
    rows = vocab["marks"][mark_key]["rows"]
    # A doubled single mark, 10x14, centred in the 16x16 tile:
    # (16-10)/2 = 3, (16-14)/2 = 1.
    draw_mark(canvas, rows, 3, 1, 2, NAMESPACE_SHADES)
    apply_outline(canvas, NAMESPACE_SHADES[3])
    return canvas


def render_tag(vocab, ns_mark, dist_mark, shades):
    canvas = blank_cell()
    draw_mark(canvas, vocab["marks"][ns_mark]["rows"], 2, 4, 1, shades)
    draw_mark(canvas, vocab["marks"][dist_mark]["rows"], 9, 4, 1, shades)
    apply_outline(canvas, shades[3])
    return canvas


def alpha_mask(canvas):
    return [[1 if px[3] else 0 for px in row] for row in canvas]


def mask_diff(a, b):
    return sum(
        1
        for y in range(len(a))
        for x in range(len(a[0]))
        if a[y][x] != b[y][x]
    )


def validate_marks(marks):
    for key, entry in marks.items():
        rows = entry["rows"]
        assert len(rows) == MARK_H, f"mark {key!r} has {len(rows)} rows, want {MARK_H}"
        for r in rows:
            assert len(r) == MARK_W, f"mark {key!r} row {r!r} has width {len(r)}, want {MARK_W}"
            assert set(r) <= {"#", "."}, f"mark {key!r} row {r!r} has stray characters"


def load_vocab():
    with open(VOCAB_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


# The marks tools/sign_vocab.json carried before this script ever touched it
# (the alphabet the compound good-signs already use). Frozen here, rather than
# read off the live file, so re-running this script to tweak a new mark's
# design can safely overwrite ITS OWN prior output without ever being able to
# clobber one of these.
ORIGINAL_MARK_KEYS = frozenset({
    "Cu", "Fe", "Sn", "Pb", "Zn", "Ag", "Au", "Hg", "S", "N", "Sb", "As", "W",
    "Fi", "GR", "FR", "LF", "FL", "RT", "TR", "RS", "TH", "CL", "BS", "BN",
    "DP", "HX", "FS", "IN", "EG", "FE", "SH", "GM", "EA", "GL", "SQ", "CP",
    "GT", "BK", "TL", "WP", "SP", "GE", "LN", "RX", "HT", "PW", "QE", "WA",
    "AI", "FM", "DS", "NA", "X", "Ca", "Ko", "Bi",
})


def write_vocab(vocab):
    for key, entry in NEW_MARKS.items():
        assert key not in ORIGINAL_MARK_KEYS, f"mark {key!r} would shadow an original mark"
        vocab["marks"][key] = entry  # add, or overwrite this script's own prior pass
    with open(VOCAB_PATH, "w", encoding="utf-8") as f:
        json.dump(vocab, f, indent=1)
        f.write("\n")


def build_cells(vocab):
    """Returns an ordered list of (id, canvas), namespaces then tags."""
    cells = []
    for ns_id, mark in NAMESPACES:
        cells.append((ns_id, render_namespace(vocab, mark)))
    for tag_id, ns_mark, dist_mark, colour in TAGS:
        if tag_id == "hue:black":
            shades = shades_from_explicit(*HUE_BLACK_OVERRIDE)
        else:
            shades = shades_from_mid(hexcolor(colour))
        cells.append((tag_id, render_tag(vocab, ns_mark, dist_mark, shades)))
    return cells


def write_atlas(cells):
    rows = math.ceil(len(cells) / COLUMNS)
    width, height = COLUMNS * CELL, rows * CELL
    sheet = [[(0, 0, 0, 0)] * width for _ in range(height)]
    for i, (_id, canvas) in enumerate(cells):
        col, row = i % COLUMNS, i // COLUMNS
        ox, oy = col * CELL, row * CELL
        for y in range(CELL):
            for x in range(CELL):
                sheet[oy + y][ox + x] = canvas[y][x]

    os.makedirs(os.path.dirname(TAGS_PNG), exist_ok=True)
    img = Image.new("RGBA", (width, height))
    flat = [px for row in sheet for px in row]
    img.putdata(flat)
    img.save(TAGS_PNG)
    return width, height


def write_index(cells):
    namespace_ids = {ns_id for ns_id, _ in NAMESPACES}
    namespaces = {}
    tags = {}
    for i, (cid, _canvas) in enumerate(cells):
        if cid in namespace_ids:
            namespaces[cid] = i
        else:
            tags[cid] = i
    data = {
        "atlas": "tags",
        "atlases": [{"id": "tags", "file": "sprites/tags.png", "cell": 16, "columns": 16}],
        "namespaces": namespaces,
        "tags": tags,
        "goods": {},
    }
    with open(TAG_SIGNS_JSON, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
        f.write("\n")
    return data


def run_checks(cells):
    n = len(NAMESPACES)
    ns_cells = cells[:n]
    tag_cells = cells[n:]

    ns_masks = [alpha_mask(c) for _id, c in ns_cells]
    for i in range(len(ns_masks)):
        for j in range(i + 1, len(ns_masks)):
            d = mask_diff(ns_masks[i], ns_masks[j])
            assert d > 6, (
                f"namespace symbols {ns_cells[i][0]!r} and {ns_cells[j][0]!r} "
                f"differ by only {d} pixels"
            )

    for i in range(len(tag_cells)):
        for j in range(i + 1, len(tag_cells)):
            assert tag_cells[i][1] != tag_cells[j][1], (
                f"tag symbols {tag_cells[i][0]!r} and {tag_cells[j][0]!r} are identical"
            )
    print(f"checks passed: {len(ns_cells)} namespace symbols pairwise >6px apart, "
          f"{len(tag_cells)} tag symbols pairwise distinct")


# ---------------------------------------------------------------------------
# Preview: every cell at 4x, labelled, once on the dark canvas colour and
# once on the light panel colour, so both real contexts can be eyeballed.
# ---------------------------------------------------------------------------

def cell_image(canvas, bg):
    img = Image.new("RGB", (CELL, CELL), bg)
    for y in range(CELL):
        for x in range(CELL):
            r, g, b, a = canvas[y][x]
            if a:
                img.putpixel((x, y), (r, g, b))
    return img.resize((CELL * 4, CELL * 4), Image.NEAREST)


def write_preview(cells):
    dark_bg = hexcolor("#2a2d34")
    light_bg = hexcolor("#f2efe6")
    font = ImageFont.load_default(size=13)

    cols = 8
    rows = math.ceil(len(cells) / cols)
    swatch = CELL * 4

    labels = [f"{i} {cid}" for i, (cid, _c) in enumerate(cells)]
    tmp = Image.new("RGB", (10, 10))
    draw = ImageDraw.Draw(tmp)
    label_w = max(draw.textbbox((0, 0), t, font=font)[2] for t in labels)
    label_h = draw.textbbox((0, 0), "Ag", font=font)[3] + 4

    pad = 6
    card_w = max(swatch, label_w) + pad * 2
    card_h = label_h + swatch * 2 + pad * 3

    sheet = Image.new("RGB", (cols * card_w, rows * card_h), (208, 206, 198))
    draw = ImageDraw.Draw(sheet)

    for i, (cid, canvas) in enumerate(cells):
        col, row = i % cols, i // cols
        cx, cy = col * card_w, row * card_h
        draw.text((cx + pad, cy + pad - 2), labels[i], font=font, fill=(20, 20, 20))
        dark_img = cell_image(canvas, dark_bg)
        light_img = cell_image(canvas, light_bg)
        sheet.paste(dark_img, (cx + pad, cy + pad + label_h))
        sheet.paste(light_img, (cx + pad, cy + pad + label_h + swatch))

    os.makedirs(os.path.dirname(PREVIEW_PATH), exist_ok=True)
    sheet.save(PREVIEW_PATH)
    print(f"wrote preview {PREVIEW_PATH} ({sheet.width}x{sheet.height})")


def main():
    validate_marks(NEW_MARKS)
    original_vocab = load_vocab()
    validate_marks(original_vocab["marks"])

    # Render against the vocabulary already extended with the new marks, so
    # the atlas and the vocab file it depends on are drawn from one state.
    # write_vocab() does its own (checked) merge into a fresh copy, so the
    # original stays untouched here.
    render_vocab = {**original_vocab, "marks": {**original_vocab["marks"], **NEW_MARKS}}

    cells = build_cells(render_vocab)
    assert len(cells) == len(NAMESPACES) + len(TAGS)

    run_checks(cells)

    w, h = write_atlas(cells)
    index = write_index(cells)
    write_vocab(original_vocab)
    write_preview(cells)

    print(f"wrote {TAGS_PNG} ({w}x{h}, {len(cells)} cells: "
          f"{len(NAMESPACES)} namespaces + {len(TAGS)} tags)")
    print(f"wrote {TAG_SIGNS_JSON}")
    print(f"wrote {VOCAB_PATH} (+{len(NEW_MARKS)} marks: {', '.join(NEW_MARKS)})")
    print(f"kind index = {index['namespaces']['kind']}, "
          f"heart index = {index['namespaces']['heart']}, "
          f"heart:bismuth index = {index['tags']['heart:bismuth']}")


if __name__ == "__main__":
    main()
