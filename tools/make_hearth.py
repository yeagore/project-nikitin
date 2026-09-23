#!/usr/bin/env python3
"""Make "Hearth", the first rung of the ladder of skeletal economies: a web of seventeen goods
that feeds, clothes, tools and houses a village, with amounts on every slot, a time on every
recipe, a limit on every extraction and a want at every consumer, so the balance sheet has
something to say. Cut from the variety ledger's palette (same ids, same icons and signs), with
its own lean palette, unlocked: it is the web to fiddle with.

Since 2026-09-23 every good comes out of something: the raw goods come out of extractions that
stand on a site and have a limit (how many fields, pits, stands are at work), and a loop closes:
the byre is kept for its milk and eats grain; its dung, a by-product, and the sheep's go on the
fields as an optional input with a boost, and the fields give more grain.

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
GOODS = ["grain", "flour", "bread", "milk", "dung", "slt", "salt", "timber", "planks", "charcoal", "fe", "ife", "tools", "wool", "yarn", "cloth", "clothes"]

HEADS = 120

# Consumers: what one head wants a day, of whatever the consumer accepts, all told.
CONSUMERS = [
    ("c.food", "Food", ["#need:food"], 2.0, "A loaf and a jug of milk a head a day: the consumer asks bread and milk for equal shares."),
    ("c.clothing", "Clothing", ["#need:clothing"], 0.02, "A garment every fifty days."),
    ("c.works", "Works and arms", ["#need:works-and-arms"], 0.025, "A tool every forty days: they wear out."),
    ("c.building", "Building and upkeep", ["#need:building"], 0.025, "A plank every forty days a head: new roofs and mended floors."),
]

# Recipes: (id, name, element, site, limit, inputs, outputs, days, note)
#   an input is a dict: accepts, amount, and any of passes, grants, optional, boost;
#   an output is (good, amount) or (good, amount, "by-product")
RECIPES = [
    # The extractions: nothing fed, a site, a limit.
    ("r.fields", "Fields", "earth", ["soil:brownearth", "soil:blackearth"], 10,
     [dict(accepts=["dung"], amount=3, optional=True, boost=0.3)], [("grain", 10)], 1,
     "Ten fields of temperate ground, ten of grain a day each. Three of dung on a field makes it give three tenths more."),
    ("r.byre", "Byre", "water", ["soil:brownearth", "soil:blackearth", "soil:bleachearth"], 20,
     [dict(accepts=["grain"], amount=1)], [("milk", 6), ("dung", 2, "by-product")], 1,
     "Twenty cows on the pasture, each fed a measure of grain a day: six of milk, and two of dung that nobody keeps them for."),
    ("r.sheep", "Sheep walk", "earth", ["soil:bleachearth", "soil:dryearth"], 8,
     [], [("wool", 1), ("dung", 1, "by-product")], 1,
     "Rough grazing: a fleece's worth of wool a flock a day, and the flock's dung on the side."),
    ("r.woods", "Woodcutting", "wind", ["moisture:balanced", "moisture:wet"], 10,
     [], [("timber", 6)], 1, "Ten stands of trees, six of timber a day each."),
    ("r.fe", "Ore pit", "earth", ["soil:stone", "soil:scree"], 4,
     [], [("fe", 1)], 1, "Wants a deposit of ironstone: deposits are not in the generator yet, so the site is only the rock it would be under."),
    ("r.slt", "Salt mine", "earth", ["soil:stone", "soil:scree"], 5,
     [], [("slt", 1)], 1, "Wants a deposit of rock salt: deposits are not in the generator yet, so the site is only the rock it would be under."),
    # The workshops.
    ("r.flour", "Watermill", "wind", ["anchor:river"], None, [dict(accepts=["grain"], amount=10, passes=True)], [("flour", 9)], 1,
     "Ten of grain to nine of flour, on a river that turns the wheel: the bran stays with the miller."),
    ("r.bread", "Bakery", "fire", None, None, [dict(accepts=["flour"], amount=5, passes=True), dict(accepts=["salt"], amount=0.2)], [("bread", 6)], 0.5,
     "Two batches a day from one oven."),
    ("r.salt", "Salt boiling", "earth", None, None, [dict(accepts=["slt"], amount=1)], [("salt", 1)], 1, "Rock salt crushed and boiled clean."),
    ("r.planks", "Sawmill", "wind", None, None, [dict(accepts=["timber"], amount=4, passes=True)], [("planks", 3)], 1, "A quarter of the log is sawdust."),
    ("r.charcoal", "Charcoal clamp", "earth", None, None, [dict(accepts=["timber"], amount=5)], [("charcoal", 2)], 3,
     "Three days under turf. Five of wood to two of charcoal."),
    ("r.ife", "Bloomery", "fire", None, None, [dict(accepts=["fe"], amount=2), dict(accepts=["#kind:fuel"], amount=3)], [("ife", 1)], 2,
     "Two of ore and three of fuel for one of iron, over two days."),
    ("r.tools", "Toolsmith", "fire", None, None, [dict(accepts=["#kind:tool-metal"], amount=1, passes=True), dict(accepts=["planks"], amount=1)], [("tools", 2)], 1,
     "One of metal and one of planks: two tools."),
    ("r.yarn", "Spinning", "earth", None, None, [dict(accepts=["wool"], amount=2, passes=True)], [("yarn", 1)], 2, "Slow. Two of fleece to one of yarn, two days."),
    ("r.cloth", "Weaving", "water", None, None, [dict(accepts=["yarn"], amount=3, passes=True, grants=["cloth:wool"])], [("cloth", 1)], 3, "Three of yarn to a bolt, three days at the loom."),
    ("r.clothes", "Tailor", "water", None, None, [dict(accepts=["cloth"], amount=1, passes=True)], [("clothes", 2)], 1, "A bolt makes two garments."),
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
        if gid == "milk":
            good["tags"].append("need:food")  # in Hearth, milk is drunk as it comes
        palette["goods"].append(good)

    # Only the tags these goods carry (and their varieties, grants and implications), with their entries, and their namespaces.
    used = set()
    for good in palette["goods"]:
        used.update(good["tags"])
        for v in good.get("varieties") or []:
            used.update(v["tags"])
    for _, _, _, site, _, inputs, _, _, _ in RECIPES:
        used.update(site or [])
        for slot in inputs:
            used.update(a[1:] for a in slot["accepts"] if a.startswith("#"))
            used.update(slot.get("grants", []))
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
    for rid, name, element, site, limit, inputs, outputs, days, note in RECIPES:
        recipe = {"id": rid, "name": name, "note": note, "element": element}
        if site:
            recipe["site"] = list(site)
        if days != 1:
            recipe["time"] = days
        if limit is not None:
            recipe["limit"] = limit
        recipe["inputs"] = []
        for slot in inputs:
            entry = {"accepts": list(slot["accepts"])}
            if slot.get("amount", 1) != 1:
                entry["amount"] = slot["amount"]
            if slot.get("optional"):
                entry["optional"] = True
            if slot.get("boost"):
                entry["boost"] = slot["boost"]
            if slot.get("passes"):
                entry["passes"] = True
            if slot.get("grants"):
                entry["grants"] = list(slot["grants"])
            recipe["inputs"].append(entry)
        recipe["outputs"] = [{"good": o[0], **({"amount": o[1]} if o[1] != 1 else {}), **({"byProduct": True} if len(o) > 2 else {})} for o in outputs]
        recipes.append(recipe)

    web = {
        "format": 2,
        "id": "hearth",
        "name": "Hearth",
        "note": ("The first rung of the ladder of skeletal economies: a village that feeds, clothes, tools and houses itself. "
                 "Seventeen goods, sixteen recipes (five of them extractions on a site, with a limit), four consumers, and numbers "
                 "on all of them, so the balance sheet can be watched while they are changed. One loop: the byre eats grain, and "
                 "its dung goes back on the fields. Made by tools/make_hearth.py, which is the baseline; this file is the experiment."),
        "palette": palette,
        "goods": list(GOODS),
        "recipes": recipes,
        "consumers": [{"id": cid, "name": cname, "note": cnote, "accepts": list(accepts), "wants": wants}
                      for cid, cname, accepts, wants, cnote in CONSUMERS],
        "heads": HEADS,
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
    for gid in GOODS:  # every good comes out of something
        assert any(o["good"] == gid for r in recipes for o in r["outputs"]), f"nothing makes {gid}"
    defs = {t["id"] for t in palette["tags"]}
    for r in recipes:
        for t in r.get("site", []):
            assert t in defs, (r["id"], t)
        if all(s.get("optional") for s in r["inputs"]):
            assert r.get("site") and r.get("limit"), f"{r['id']} is an extraction without a site or a limit"

    out = ROOT / "hearth.json"
    json.dump(web, open(out, "w"), indent=2, ensure_ascii=False)
    open(out, "a").write("\n")
    print(f"wrote {out}: {len(GOODS)} goods, {len(recipes)} recipes ({sum(1 for r in recipes if all(s.get('optional') for s in r['inputs']))} extractions), "
          f"{len(CONSUMERS)} consumers, {HEADS} heads, "
          f"{len(palette['tags'])} tags in {len(palette['tagNamespaces'])} namespaces")


if __name__ == "__main__":
    main()
