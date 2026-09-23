#!/usr/bin/env python3
"""Draw the icons the sprite review of 2026-09-23 found wanting, into the last twelve free cells.

The review (`-- sprites web=variety-ledger`) found eight pairs of goods sharing one icon, pixel
for pixel, which breaks "shape by good, hue by variety", and a handful that do not read at all
at 16 px. One of each pair is redrawn here (the other keeps its icon), and four of the worst
illegible ones: Soil was a letter tile ("Br"), Leather and Silver nearly vanished on the dark
canvas, Flour was a scrap. Drawn by hand, pixel by pixel, in the sheet's style: a one-pixel
outline in a dark of the icon's own hue, light from the top left.

Run:  python3 tools/make_icon_fixes.py     (no arguments, deterministic)

Writes cells 340 to 351 of resources/economy/sprites/icons.png (the cells before them are
untouched, so every web keeps its old icons until a script points it at the new ones), and
tools/icon_fixes.json, which tools/make_variety_ledger.py stamps into the variety ledger.
"""
import json
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEET = os.path.join(ROOT, "resources", "economy", "sprites", "icons.png")
INDEX = os.path.join(ROOT, "tools", "icon_fixes.json")
FIRST = 340
COLUMNS = 16

# good id -> (why, palette, 16 rows of 16). "." is transparent.
ICONS = {
    "iag": ("Silver was a flat dark sliver; now an ingot as bright as the metal.",
            {"o": "#2a2f3a", "w": "#f6f9fc", "l": "#d6dde6", "m": "#a9b4c2", "d": "#7c8898"}, [
        "................",
        ".............w..",
        "............www.",
        ".............w..",
        "....oooooooo....",
        "...owwwwwwwlo...",
        "..owlllllllllo..",
        ".owllllllllllmo.",
        ".oooooooooooooo.",
        ".omllllllllllmo.",
        ".ommmmmmmmmmmdo.",
        ".oddddddddddddo.",
        "..oooooooooooo..",
        "................",
        "................",
        "................"]),
    "flour": ("Flour was a beige scrap; now a tied sack with a little spilled.",
              {"o": "#4a3a28", "w": "#fbf6ea", "l": "#ece2cc", "m": "#d3c4a4", "d": "#b09c78", "t": "#8a6a3a", "f": "#ffffff"}, [
        "................",
        "......oooo......",
        ".....owwwlo.....",
        "......otto......",
        ".....owwllo.....",
        "....owwlllmo....",
        "...owwllllllmo..",
        "..owwlllllllmmo.",
        "..owllllllllmmo.",
        "..owllllllllmmo.",
        "..ollllllllmmdo.",
        "..olllllllmmmdo.",
        "...omlllmmmmdo..",
        "....ooooooooo...",
        "..........fwwf..",
        "................"]),
    "leather": ("Leather was a dark ring lost on the canvas; now a stretched hide, tanned light, stitched.",
                {"o": "#3a2212", "h": "#e8bc88", "l": "#cf955a", "m": "#a86c38", "d": "#7e4c24", "s": "#f6e2bc"}, [
        "................",
        ".oo..........oo.",
        ".oho........omo.",
        "..ohoooooooomo..",
        "...ohhhhllllo...",
        "...ohhllllllo...",
        "..ohllsllsllmo..",
        "..ohllllllllmo..",
        "..olllllllllmo..",
        "..ollsllllslmo..",
        "...ollllllmmo...",
        "..oolmmmmmmdoo..",
        ".odo.oooooo.odo.",
        ".oo..........oo.",
        "................",
        "................"]),
    "soil": ("Soil was the letters Br on a tile; now a clod of earth, which its soil tints whole.",
             {"o": "#2b1d14", "e": "#d6b08a", "a": "#b58a62", "b": "#8e6644", "c": "#6e4c30", "d": "#4e3421"}, [
        "................",
        "................",
        "................",
        "...oo...ooo.....",
        "..oeao.oeaaoo...",
        ".oeaaooeaabaao..",
        ".oaabaaaaabbbo..",
        "oeaabboaabbccco.",
        "oaabbcbbbbccccdo",
        "obbcbbccbcccdddo",
        "obccecccccdcdddo",
        "occcdccddcddcddo",
        ".oooooooooooooo.",
        "................",
        "................",
        "................"]),
    "sugar": ("Sugar shared Camphor's cubes; now a sugarloaf in its blue paper.",
              {"o": "#2a3040", "w": "#ffffff", "l": "#eef1f5", "m": "#cfd6e0", "d": "#a8b3c2", "p": "#3a5fa8", "q": "#2a4580"}, [
        "................",
        ".......oo.......",
        "......owlo......",
        "......owlo......",
        ".....owllmo.....",
        ".....owllmo.....",
        "....owlllmmo....",
        "....owlllmmo....",
        "...owllllmmdo...",
        "...owllllmmdo...",
        "..oppppppppqqo..",
        "..oppppppppqqo..",
        "..opppppppqqqo..",
        "...oooooooooo...",
        "................",
        "................"]),
    "dammar": ("Dammar shared Gum arabic's tear; now a heap of pale lumps.",
               {"o": "#4a3810", "w": "#fff6c8", "l": "#f2dc8a", "m": "#d9b85a", "d": "#b08a36"}, [
        "................",
        "................",
        "................",
        "................",
        ".......oooo.....",
        "......owwlmo....",
        "......owllmo....",
        "..oooooollmdo...",
        ".owwlmoolmdoooo.",
        ".owllmmoodoowlmo",
        ".ollmmdo.oowllmo",
        "..ommddo..ollmdo",
        "...oooo...ommddo",
        "...........oooo.",
        "................",
        "................"]),
    "varnish": ("Varnish shared Linseed oil's bottle; now a pot with the brush standing in it.",
                {"o": "#2a1a0e", "T": "#c9ced6", "t": "#9aa0a8", "D": "#5e636b", "v": "#c8781a", "V": "#f0a83a",
                 "h": "#8a5a2a", "H": "#c08a4a", "b": "#3a2a1a"}, [
        "................",
        "..........oo....",
        ".........ohHo...",
        "........ohHo....",
        ".......ohHo.....",
        "......obbo......",
        "...oooobboooo...",
        "..oVVVVbbVVvvo..",
        "..oTTTTTTTttDo..",
        "..oTttttttttDo..",
        "..oTttvvvtttDo..",
        "..oTttttttttDo..",
        "..oTttttttttDo..",
        "..oDDDDDDDDDDo..",
        "...oooooooooo...",
        "................"]),
    "betelleaf": ("Betel leaf borrowed the mulberry's; now its own heart-shaped leaf, glossy, on a stalk.",
                  {"o": "#16301a", "G": "#7ccc66", "g": "#3f8f3a", "d": "#2a6a2a", "v": "#a8dc88", "s": "#7a5a32"}, [
        "................",
        "........so......",
        "...ooo..s.ooo...",
        "..oGGgo..oggdo..",
        ".oGGggGooGgggdo.",
        ".oGgggggvggggdo.",
        ".oGggggvvvgggdo.",
        "..oGgggvgggddo..",
        "..oGggvgvggddo..",
        "...oGggvggddo...",
        "....oGgvgddo....",
        ".....oggddo.....",
        "......oddo......",
        ".......oo.......",
        "................",
        "................"]),
    "shells": ("Shells shared Silk thread's pink skein; now a scallop.",
               {"o": "#4a2a2a", "w": "#fff0e6", "l": "#f5cdb8", "m": "#e0a08a", "d": "#b86f5e", "r": "#d08878"}, [
        "................",
        "................",
        ".....oooooo.....",
        "...oowlwlwloo...",
        "..owlwlwlwlmmo..",
        ".owlwlwlwlwlmdo.",
        ".olrlrlrlrlrmdo.",
        ".olrlrlrlrlrmdo.",
        "..olrlrlrlrmdo..",
        "...olrlrlrmdo...",
        "....olrlrmdo....",
        "...ooolmmdooo...",
        "...omdooooodmo..",
        "...ooo.....ooo..",
        "................",
        "................"]),
    "whitelead": ("White lead shared Bone ash's heap; now the cakes it was sold in, stacked.",
                  {"o": "#3a3f4a", "w": "#ffffff", "l": "#e8ecf2", "m": "#c4ccd8", "d": "#98a2b2"}, [
        "................",
        "................",
        "................",
        ".....oooooo.....",
        "...oowwwwlloo...",
        "..owwwwllllmmo..",
        "..ommlllllmmdo..",
        "..oooooooooooo..",
        "..owwwllllmmmo..",
        "..ommmmmmmmddo..",
        "..oooooooooooo..",
        "..owwwllllmmmo..",
        "..ommmmmmmmddo..",
        "...oooooooooo...",
        "................",
        "................"]),
    "redlead": ("Red lead shared Ochre's heap; now heaped in the crucible it was roasted in.",
                {"o": "#3a120a", "r": "#e8502a", "R": "#ff8a5a", "d": "#b0321a", "C": "#c4c4cc", "c": "#8e8e98", "k": "#5a5a62"}, [
        "................",
        "................",
        "................",
        "................",
        "......oooo......",
        "....ooRRRroo....",
        "...oRRrrrrrdo...",
        "..oooooooooooo..",
        "..oCCCccccccko..",
        "...oCCcccccko...",
        "...oCccccccko...",
        "....oCcccckko...",
        ".....okkkkko....",
        "......ooooo.....",
        "................",
        "................"]),
    "cutch": ("Cutch shared Peat's bricks; now the dark red cubes it is sold as.",
              {"o": "#1e0c08", "h": "#c87050", "a": "#a8543a", "b": "#7e3a26", "c": "#5a2818"}, [
        "................",
        "................",
        "................",
        ".....oooooo.....",
        ".....ohhaao.....",
        ".....oabbco.....",
        ".....obbcco.....",
        ".ooooooooooooo..",
        ".ohhaao.ohhaao..",
        ".oabbco.oabbco..",
        ".obbcco.obbcco..",
        ".obbcco.obbcco..",
        ".oooooo.oooooo..",
        "................",
        "................",
        "................"]),
}


def rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def main():
    sheet = Image.open(SHEET).convert("RGBA")
    rows_on_sheet = sheet.height // 16
    assert FIRST + len(ICONS) <= rows_on_sheet * COLUMNS, "no room on the sheet"
    index = {}
    for n, (gid, (why, palette, rows)) in enumerate(ICONS.items()):
        rows = [r.replace(" ", "o") for r in rows]  # a stray space is an outline pixel
        assert len(rows) == 16 and all(len(r) == 16 for r in rows), (gid, [len(r) for r in rows])
        unknown = {ch for r in rows for ch in r} - set(palette) - {"."}
        assert not unknown, (gid, unknown)
        cell = FIRST + n
        x0, y0 = cell % COLUMNS * 16, cell // COLUMNS * 16
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                sheet.putpixel((x0 + x, y0 + y), (0, 0, 0, 0) if ch == "." else rgb(palette[ch]))
        index[gid] = cell
        print(f"{gid:10} -> cell {cell}: {why}")
    sheet.save(SHEET)
    with open(INDEX, "w") as f:
        json.dump({"atlas": "icons", "icons": index}, f, indent=1)
        f.write("\n")
    print(f"wrote {len(index)} icons into {os.path.relpath(SHEET, ROOT)} and {os.path.relpath(INDEX, ROOT)}")
    if len(sys.argv) > 1:  # a preview at six times the size, for looking at
        s = 6
        out = Image.new("RGBA", (len(index) * (16 * s + 8), 16 * s), (30, 32, 40, 255))
        for k, cell in enumerate(index.values()):
            c = sheet.crop((cell % COLUMNS * 16, cell // COLUMNS * 16, cell % COLUMNS * 16 + 16, cell // COLUMNS * 16 + 16)).resize((16 * s, 16 * s), Image.NEAREST)
            out.paste(c, (k * (16 * s + 8), 0), c)
        out.save(sys.argv[1])


if __name__ == "__main__":
    main()
