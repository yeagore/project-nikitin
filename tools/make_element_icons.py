#!/usr/bin/env python3
"""Generate five 16x16 pixel-art alchemical element icons: fire, wind, water,
earth, quintessence (qe). Standard library only -- PNG bytes are assembled
by hand with struct + zlib (no PIL).

Usage: python3 make_element_icons.py <out_dir>

Writes <out_dir>/elements.png (80x16 RGBA, five cells in a row, transparent
background) and <out_dir>/elements@8x.png (640x128, nearest-neighbour
enlargement composited onto a dark #1c1e24 background, for eyeballing).
"""
import sys
import os
import struct
import zlib
import math

CELL = 16
ORDER = ['fire', 'wind', 'water', 'earth', 'qe']


def hexcolor(h):
    h = h.lstrip('#')
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))


# ---------------------------------------------------------------------------
# PNG writing (no PIL): raw RGBA scanlines -> zlib -> IDAT chunk.
# ---------------------------------------------------------------------------

def write_png(path, width, height, pixels):
    """pixels: list of `height` rows, each a list of `width` (r,g,b,a) tuples."""

    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data +
                struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff))

    raw = bytearray()
    for row in pixels:
        raw.append(0)  # filter type: None
        for (r, g, b, a) in row:
            raw.extend((r, g, b, a))
    ihdr = struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)  # 8-bit RGBA
    idat = zlib.compress(bytes(raw), 9)
    blob = (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', ihdr) +
            chunk(b'IDAT', idat) + chunk(b'IEND', b''))
    with open(path, 'wb') as f:
        f.write(blob)


# ---------------------------------------------------------------------------
# Glyph construction: a 16x16 grid of "part" labels (str) or None (empty).
# Every glyph is built inside rows/cols 2..13 so that the 1px outline added
# later (which grows the silhouette by one ring) lands on 1..14, leaving row
# 0/15 and col 0/15 fully clear.
# ---------------------------------------------------------------------------

def blank_grid():
    return [[None] * CELL for _ in range(CELL)]


def fill_row(grid, y, x0, x1, part):
    for x in range(x0, x1 + 1):
        grid[y][x] = part


def clear_row(grid, y, x0, x1):
    for x in range(x0, x1 + 1):
        grid[y][x] = None


# A hollow (2px-stroke) upward triangle, apex at row 2, base at row 13,
# widening two rows at a time so the diagonal reads as a clean stair-step.
# The hole is omitted near the apex (too narrow) and near the base (closes
# the bottom edge), which is what makes it read as a closed triangle ring
# instead of an open chevron.
_TRI_OUTER = {
    2: (7, 8), 3: (7, 8),
    4: (6, 9), 5: (6, 9),
    6: (5, 10), 7: (5, 10),
    8: (4, 11), 9: (4, 11),
    10: (3, 12), 11: (3, 12),
    12: (2, 13), 13: (2, 13),
}
_TRI_HOLE = {
    6: (7, 8), 7: (7, 8),
    8: (6, 9), 9: (6, 9),
    10: (5, 10), 11: (5, 10),
}


def hollow_triangle(part, point_up):
    grid = blank_grid()
    for y, (x0, x1) in _TRI_OUTER.items():
        fill_row(grid, y, x0, x1, part)
    for y, (x0, x1) in _TRI_HOLE.items():
        clear_row(grid, y, x0, x1)
    if not point_up:
        grid = grid[::-1]  # vertical flip -> apex at the bottom
    return grid


def build_fire():
    return hollow_triangle('fire', point_up=True)


def build_water():
    return hollow_triangle('water', point_up=False)


def build_wind():
    # Fire's triangle plus a bar a third of the way down from the apex,
    # reaching two pixels past the triangle's sides on each side.
    grid = hollow_triangle('wind', point_up=True)
    for y in (6, 7):
        fill_row(grid, y, 3, 12, 'wind')
    return grid


def build_earth():
    # Water's triangle plus a bar a third of the way up from the apex.
    grid = hollow_triangle('earth', point_up=False)
    for y in (8, 9):
        fill_row(grid, y, 3, 12, 'earth')
    return grid


def build_qe():
    # A four-pointed star (a bold "+") with a bright centre pixel, floating
    # inside a thin ring that touches the same inner margin the other four
    # glyphs use. (A first version let the star's arms reach all the way to
    # the ring -- same colour, no gap -- and it fused into a solid disc with
    # two dark holes, reading as a face rather than a star-in-a-ring; the
    # full eight-point plus-and-x star would only have made that worse. The
    # fix is a visibly shorter star with a clear ring of background between
    # its tips and the ring, so both shapes stay legible -- see the report.)
    grid = blank_grid()
    cx = cy = 8.0
    outer_r, inner_r = 6.2, 5.0
    for y in range(CELL):
        for x in range(CELL):
            dx = (x + 0.5) - cx
            dy = (y + 0.5) - cy
            dist = math.hypot(dx, dy)
            if inner_r <= dist <= outer_r:
                grid[y][x] = 'violet'
    for y in range(5, 11):
        grid[y][7] = 'violet'
        grid[y][8] = 'violet'
    for x in range(5, 11):
        grid[7][x] = 'violet'
        grid[8][x] = 'violet'
    for y in (7, 8):
        for x in (7, 8):
            grid[y][x] = 'gold'
    return grid


BUILDERS = {
    'fire': build_fire, 'wind': build_wind,
    'water': build_water, 'earth': build_earth, 'qe': build_qe,
}

# ---------------------------------------------------------------------------
# Colouring: a 3-band vertical tint (light top, mid, dark bottom) over the
# glyph's own row span, then a 1px outline: every transparent pixel that is
# 4-adjacent (not diagonal) to a filled pixel becomes the outline colour.
# ---------------------------------------------------------------------------

PALETTES = {
    'fire': dict(light='#ffd27a', mid='#ff8a3d', dark='#c9402a', outline='#3a1208'),
    'wind': dict(light='#f2fbff', mid='#a9dcf5', dark='#5d9ccc', outline='#10283a'),
    'water': dict(light='#9fd0ff', mid='#4a8fe0', dark='#2a4fa8', outline='#0b1638'),
    'earth': dict(light='#cfe08a', mid='#7fa646', dark='#4f6b2a', outline='#172108'),
}
QE_OUTLINE = hexcolor('#1d0f33')
QE_GOLD = hexcolor('#ffd27a')
QE_VIOLET = dict(light=hexcolor('#f3d9ff'), mid=hexcolor('#b98cf0'), dark=hexcolor('#6c4b9c'))


def render_glyph(name, grid):
    filled_rows = [y for y in range(CELL) if any(grid[y])]
    lo, hi = min(filled_rows), max(filled_rows)
    band_size = math.ceil((hi - lo + 1) / 3)

    def band_color(y, light, mid, dark):
        band = min((y - lo) // band_size, 2)
        return (light, mid, dark)[band]

    out = [[(0, 0, 0, 0)] * CELL for _ in range(CELL)]

    if name == 'qe':
        light, mid, dark = QE_VIOLET['light'], QE_VIOLET['mid'], QE_VIOLET['dark']
        outline = QE_OUTLINE
        for y in range(CELL):
            for x in range(CELL):
                part = grid[y][x]
                if part == 'gold':
                    out[y][x] = QE_GOLD + (255,)
                elif part == 'violet':
                    out[y][x] = band_color(y, light, mid, dark) + (255,)
    else:
        pal = PALETTES[name]
        light, mid, dark = hexcolor(pal['light']), hexcolor(pal['mid']), hexcolor(pal['dark'])
        outline = hexcolor(pal['outline'])
        for y in range(CELL):
            for x in range(CELL):
                if grid[y][x]:
                    out[y][x] = band_color(y, light, mid, dark) + (255,)

    def filled(x, y):
        return 0 <= x < CELL and 0 <= y < CELL and out[y][x][3] == 255

    outline_cells = []
    for y in range(CELL):
        for x in range(CELL):
            if out[y][x][3] == 0 and (filled(x - 1, y) or filled(x + 1, y) or
                                       filled(x, y - 1) or filled(x, y + 1)):
                outline_cells.append((x, y))
    for (x, y) in outline_cells:
        out[y][x] = outline + (255,)

    return out


def main():
    if len(sys.argv) != 2:
        print('usage: python3 make_element_icons.py <out_dir>', file=sys.stderr)
        sys.exit(1)
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)

    glyphs = {name: render_glyph(name, BUILDERS[name]()) for name in ORDER}

    sheet_w = CELL * len(ORDER)
    sheet = [[(0, 0, 0, 0)] * sheet_w for _ in range(CELL)]
    for i, name in enumerate(ORDER):
        g = glyphs[name]
        for y in range(CELL):
            for x in range(CELL):
                sheet[y][i * CELL + x] = g[y][x]

    write_png(os.path.join(out_dir, 'elements.png'), sheet_w, CELL, sheet)

    bg = hexcolor('#1c1e24')
    scale = 8
    big_w, big_h = sheet_w * scale, CELL * scale
    big = [[bg + (255,)] * big_w for _ in range(big_h)]
    for y in range(CELL):
        for x in range(sheet_w):
            r, g_, b, a = sheet[y][x]
            color = (r, g_, b, 255) if a == 255 else bg + (255,)
            for dy in range(scale):
                row = big[y * scale + dy]
                for dx in range(scale):
                    row[x * scale + dx] = color

    write_png(os.path.join(out_dir, 'elements@8x.png'), big_w, big_h, big)
    print(f'wrote {out_dir}/elements.png ({sheet_w}x{CELL}) '
          f'and {out_dir}/elements@8x.png ({big_w}x{big_h})')


if __name__ == '__main__':
    main()
