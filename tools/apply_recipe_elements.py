#!/usr/bin/env python3
"""Stamp the recipes' elemental associations into the webs, and keep a locked reference
copy of the full ledger.

Usage (from the repo root):
    python3 tools/apply_recipe_elements.py [--force]

It reads tools/recipe_elements.json (recipe id -> fire | wind | water | earth | qe, a
classification by feel made on 2026-09-22; see docs/economy-lab.md) and writes an
"element" onto every recipe of every web under resources/economy/webs/ whose id is in
the map and which has none yet (--force overwrites). It edits the files in place and
touches nothing else in them, so layouts and lab edits survive. Locked webs are skipped.

Then, if resources/economy/webs/full-ledger-reference.json does not exist, it makes it:
a copy of the full ledger marked "locked", which the lab opens to look at, cut from and
import from, and refuses to change or bin.
"""

import json
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
WEBS = ROOT / "resources" / "economy" / "webs"
ELEMENTS = ("fire", "wind", "water", "earth", "qe")


def write(web, path):
    json.dump(web, open(path, "w"), indent=2, ensure_ascii=False)
    open(path, "a").write("\n")


def stamp(recipe, element):
    """Put "element" after "note", where the lab writes it."""
    out = {}
    for key, value in recipe.items():
        if key == "element":
            continue
        out[key] = value
        if key == "note":
            out["element"] = element
    if "element" not in out:
        out["element"] = element
    recipe.clear()
    recipe.update(out)


def main():
    force = "--force" in sys.argv
    mapping = json.load(open(ROOT / "tools" / "recipe_elements.json"))
    bad = {k: v for k, v in mapping.items() if v not in ELEMENTS}
    assert not bad, f"not elements: {bad}"

    for path in sorted(WEBS.glob("*.json")):
        web = json.load(open(path))
        if web.get("locked"):
            print(f"{path.name}: locked, skipped")
            continue
        changed = 0
        for recipe in web.get("recipes", []):
            element = mapping.get(recipe["id"])
            if element and (force or not recipe.get("element")) and recipe.get("element") != element:
                stamp(recipe, element)
                changed += 1
        if changed:
            write(web, path)
        tally = Counter(r.get("element") or "none" for r in web.get("recipes", []))
        print(f"{path.name}: {changed} stamped; " + ", ".join(f"{e} {tally[e]}" for e in (*ELEMENTS, "none") if tally[e]))

    reference = WEBS / "full-ledger-reference.json"
    if not reference.exists():
        web = json.load(open(WEBS / "full-ledger.json"))
        copy = {}
        for key, value in web.items():
            if key == "id":
                value = "full-ledger-reference"
            elif key == "name":
                value = "The full ledger: reference copy"
            elif key == "note":
                value = ("A locked copy of the full ledger as it was imported and classified, kept so that the big web can always be "
                         "got back. The lab will not change it or bin it. To work from it, New… → a copy of the open web.")
            copy[key] = value
            if key == "note":
                copy["locked"] = True
        write(copy, reference)
        print(f"{reference.name}: made, locked")


if __name__ == "__main__":
    main()
