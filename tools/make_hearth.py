#!/usr/bin/env python3
"""Make "Hearth", the first rung of the ladder of skeletal economies: a web of fifteen goods
that feeds, clothes and tools a village, with amounts on every slot, a time on every recipe,
a supply at every source and a want at every consumer, so the balance sheet has something to
say. Cut from the variety ledger's palette (same ids, same icons and signs), with its own lean
palette, unlocked: it is the web to fiddle with.

Usage (from the repo root):
    python3 tools/make_hearth.py
    godot --path . --headless scenes/dev/economy_lab.tscn -- bake        # arranges it

Running it again puts the numbers back as they are here; the lab's own edits to hearth.json
are the experiment, this file is the baseline.
"""

import copy
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent / "resources" / "economy" / "webs"

# The goods, in the order they will sit in the palette and the canvas.
GOODS = ["grain", "flour", "bread", "slt", "salt", "timber", "planks", "charcoal", "fe", "ife", "tools", "wool", "yarn", "cloth", "clothes"]

# What the land gives a day, at the sources.
SUPPLY = {"grain": 100, "timber": 60, "fe": 10, "wool": 8, "slt": 5}

HEADS = 120

# Consumers: what one head wants a day, of whatever the consumer accepts, all told.
CONSUMERS = [
    ("c.food", "Food", ["#need:food"], 1.0, "A loaf a head a day."),
    ("c.clothing", "Clothing", ["#need:clothing"], 0.02, "A garment every fifty days."),
    ("c.works", "Works and arms", ["#need:works-and-arms"], 0.05, "Tools wear out and buildings eat planks."),
]

# Recipes: (id, name, element, inputs, outputs, days, note)
#   an input is (accepts, amount, passes) or (accepts, amount, passes, grants); an output is (good, amount)
RECIPES = [
    ("r.flour", "Mill", "wind", [(["grain"], 10, True)], [("flour", 9)], 1,
     "Ten of grain to nine of flour: the bran stays with the miller."),
    ("r.bread", "Bakery", "fire", [(["flour"], 5, True), (["salt"], 0.2, False)], [("bread", 6)], 0.5,
     "Two batches a day from one oven."),
    ("r.salt", "Salt boiling", "earth", [(["slt"], 1, False)], [("salt", 1)], 1, "Rock salt crushed and boiled clean."),
    ("r.planks", "Sawmill", "wind", [(["timber"], 4, True)], [("planks", 3)], 1, "A quarter of the log is sawdust."),
    ("r.charcoal", "Charcoal clamp", "earth", [(["timber"], 5, False)], [("charcoal", 2)], 3,
     "Three days under turf. Five of wood to two of charcoal."),
    ("r.ife", "Bloomery", "fire", [(["fe"], 2, False), (["#kind:fuel"], 3, False)], [("ife", 1)], 2,
     "Two of ore and three of fuel for one of iron, over two days."),
    ("r.tools", "Toolsmith", "fire", [(["#kind:tool-metal"], 1, True), (["planks"], 1, False)], [("tools", 2)], 1,
     "One of metal and one of planks: two tools."),
    ("r.yarn", "Spinning", "earth", [(["wool"], 2, True)], [("yarn", 1)], 2, "Slow. Two of fleece to one of yarn, two days."),
    ("r.cloth", "Weaving", "water", [(["yarn"], 3, True, ["cloth:wool"])], [("cloth", 1)], 3, "Three of yarn to a bolt, three days at the loom."),
    ("r.clothes", "Tailor", "water", [(["cloth"], 1, True)], [("clothes", 2)], 1, "A bolt makes two garments."),
]


def main():
    ledger = json.load(open(ROOT / "variety-ledger.json"))
    pal = ledger["palette"]
    goods = {g["id"]: g for g in pal["goods"]}
    missing = [g for g in GOODS if g not in goods]
    assert not missing, missing

    palette = {"atlases": copy.deepcopy(pal["atlases"]), "tagNamespaces": [], "tags": [], "goods": []}
    for gid in GOODS:
        good = copy.deepcopy(goods[gid])
        if gid == "cloth":
            good["tags"] = [t for t in good["tags"] if t != "need:clothing"]  # in Hearth, cloth is cut into clothes and not worn as it is
        palette["goods"].append(good)

    # Only the tags these goods carry (and their varieties, grants and implications), with their entries, and their namespaces.
    used = set()
    for good in palette["goods"]:
        used.update(good["tags"])
        for v in good.get("varieties") or []:
            used.update(v["tags"])
    for _, _, _, inputs, _, _, _ in RECIPES:
        for slot in inputs:
            used.update(a[1:] for a in slot[0] if a.startswith("#"))
            if len(slot) > 3:
                used.update(slot[3])
    for _, _, accepts, _, _ in CONSUMERS:
        used.update(a[1:] for a in accepts if a.startswith("#"))
    defs = {t["id"]: t for t in pal["tags"]}
    for tag in sorted(used):
        for implied in defs.get(tag, {}).get("implies", []):
            used.add(implied)
    palette["tags"] = [copy.deepcopy(defs[t]) for t in (x["id"] for x in pal["tags"]) if t in used]
    spaces = {t.split(":")[0] for t in used}
    palette["tagNamespaces"] = [copy.deepcopy(n) for n in pal["tagNamespaces"] if n["id"] in spaces]

    recipes = []
    for rid, name, element, inputs, outputs, days, note in RECIPES:
        recipe = {"id": rid, "name": name, "note": note, "element": element}
        if days != 1:
            recipe["time"] = days
        recipe["inputs"] = []
        for slot in inputs:
            accepts, amount, passes = slot[0], slot[1], slot[2]
            entry = {"accepts": list(accepts)}
            if amount != 1:
                entry["amount"] = amount
            if passes:
                entry["passes"] = True
            if len(slot) > 3:
                entry["grants"] = list(slot[3])
            recipe["inputs"].append(entry)
        recipe["outputs"] = [{"good": g, **({"amount": n} if n != 1 else {})} for g, n in outputs]
        recipes.append(recipe)

    web = {
        "format": 2,
        "id": "hearth",
        "name": "Hearth",
        "note": ("The first rung of the ladder of skeletal economies: a village that feeds, clothes and tools itself. "
                 "Fifteen goods, ten recipes, three consumers, and numbers on all of them, so the balance sheet can be "
                 "watched while they are changed. Made by tools/make_hearth.py, which is the baseline; this file is the experiment."),
        "palette": palette,
        "goods": list(GOODS),
        "recipes": recipes,
        "consumers": [{"id": cid, "name": cname, "note": cnote, "accepts": list(accepts), "wants": wants}
                      for cid, cname, accepts, wants, cnote in CONSUMERS],
        "heads": HEADS,
        "supply": dict(sorted(SUPPLY.items())),
        "layout": {},
    }

    # Checks: every acceptor names a good in the web or a tag some good here carries; no slot admits its own output.
    ids = set(GOODS)
    carried = {t for g in palette["goods"] for t in g["tags"]}
    for r in recipes:
        made = {o["good"] for o in r["outputs"]}
        assert made <= ids, (r["id"], made - ids)
        for s in r["inputs"]:
            for a in s["accepts"]:
                if a.startswith("#"):
                    assert a[1:] in carried, (r["id"], a)
                    assert not made & {g["id"] for g in palette["goods"] if a[1:] in g["tags"]}, (r["id"], a)
                else:
                    assert a in ids, (r["id"], a)
    for c in web["consumers"]:
        for a in c["accepts"]:
            assert a[1:] in carried, (c["id"], a)
    for gid in SUPPLY:
        assert not any(o["good"] == gid for r in recipes for o in r["outputs"]), f"{gid} has a supply and a maker"

    out = ROOT / "hearth.json"
    json.dump(web, open(out, "w"), indent=2, ensure_ascii=False)
    open(out, "a").write("\n")
    print(f"wrote {out}: {len(GOODS)} goods, {len(recipes)} recipes, {len(CONSUMERS)} consumers, {HEADS} heads, "
          f"{len(palette['tags'])} tags in {len(palette['tagNamespaces'])} namespaces")


if __name__ == "__main__":
    main()
