#!/usr/bin/env python3
"""Derive "The full ledger, tagged" from the full ledger: a web of its own, with a palette
of its own, in which the recipes' either-or slots are tag slots wherever a tag can carry
the meaning, and in which varieties are switched on as a worked example.

Usage (from the repo root):
    python3 tools/make_tagged_ledger.py
    godot --path . --headless scenes/dev/economy_lab.tscn -- bake

It reads resources/economy/webs/full-ledger.json and writes .../tagged-ledger.json, and
prints what it did to each slot. A one-off, kept as the record of the decisions: running
it again overwrites whatever the lab has done to the tagged ledger since.

Every decision below is a judgement for Maxim to overrule; docs/economy-lab.md has the
findings in words.
"""

import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "resources" / "economy" / "webs"

# ---------------------------------------------------------------------------------------
# 1. Either-or slots into tags.
#    (recipe id, slot index) -> (tag, goods that get the tag, group, remark)
#    group: "named"   a category the brainstorm had already tagged
#           "category" a real family that had no tag yet and is likely to grow
#           "source"   several sources of one substance: the list under a name
# ---------------------------------------------------------------------------------------
SLOTS = {
    ("r.ife", 1): ("kind:fuel", [], "named",
                   "widens: iron may now be smelted with peat or timber as well as coal or charcoal"),
    ("r.brick", 1): ("kind:fuel", [], "named",
                     "widens: bricks may now be fired with charcoal or timber as well as coal or peat"),
    ("r.dyed", 2): ("kind:dye", [], "named",
                    "widens to scarlet dye, which overlaps the Scarlet cloth recipe: with varieties on, "
                    "scarlet cloth is just the scarlet variety of fast-dyed cloth and could be folded into it"),

    ("r.oov", 0): ("kind:vitriol", ["vit", "copperas"], "category", ""),
    ("r.leather", 1): ("kind:tannin", ["bark", "cutch", "galls"], "category",
                       "oak galls join: gall tanning is real, and they were a tannin all along"),
    ("r.paper", 0): ("kind:plant-fibre", ["linthr", "hempf", "bamboo", "coconut", "cotthr"], "category",
                     "kind:fibre was too wide (wool, silk); one tag serves paper and rope"),
    ("r.rope", 0): ("kind:plant-fibre", [], "category", "shared with paper"),
    ("r.varnish", 0): ("kind:varnish-resin", ["resin", "dammar", "shellac"], "category",
                       "kind:resin was too wide (frankincense, amber, lacquer sap)"),
    ("r.sealwax", 1): ("kind:varnish-resin", [], "category", "shared with varnish; widens sealing wax to dammar"),
    ("r.soap", 1): ("kind:fat", ["tallow", "oil", "palmoil"], "category", ""),
    ("r.candles", 0): ("kind:candle-stock", ["tallow", "wax", "palmoil"], "source",
                       "fats and a wax: not one family, so the tag is named for the use"),
    ("r.charts", 0): ("kind:writing-surface", ["paper", "parch"], "category", "kind:writing was too wide (ink, books)"),
    ("r.clothes", 0): ("kind:plain-cloth", ["linen", "woolc", "cottonc"], "category",
                       "kind:cloth was too wide (silk, canvas, the dyed cloths)"),
    ("r.jewel", 2): ("kind:gemstone", ["lapis", "jade", "amber", "obs", "rockc"], "category",
                     "kind:gem could not be used: Jewellery carries it too, so the recipe would have fed on its own "
                     "output. A tag a product shares with its ingredients cannot be the slot's tag"),
    ("r.incense", 0): ("kind:incense-resin", ["frank", "benzoin", "camphor"], "category",
                       "kind:incense could not be used for the same reason: Incense carries it"),
    ("r.madderl", 0): ("kind:red-dyestuff", ["madder", "dyewood"], "category", "kind:dyestuff was too wide (weld, indigo)"),
    ("r.scarlet", 0): ("kind:scarlet-insect", ["coch", "lac"], "category", ""),

    ("r.sama", 0): ("kind:ammonia-source", ["sam", "dung", "urine", "horn"], "source",
                    "urine and horn join: both were real sources, and the hartshorn recipe wants the same tag"),
    ("r.qlime", 0): ("kind:lime-source", ["lim", "shells"], "source", ""),
    ("r.soda", 0): ("kind:soda-source", ["nat", "kelp"], "source", ""),
    ("r.aqr", 1): ("kind:chloride", ["sama", "sos"], "source", ""),
    ("r.pitch", 0): ("kind:tar-stock", ["resin", "bitu"], "source", ""),
    ("r.lampblk", 0): ("kind:soot-fuel", ["resin", "oil", "palmoil", "tallow", "bitu"], "source",
                       "anything that burns with a sooty flame; widens lampblack to tallow, palm oil and bitumen"),
    ("r.lens", 0): ("kind:lens-stock", ["crystal", "rockc"], "source", ""),
}

TAG_NOTES = {
    "kind:vitriol": "The vitriols: metal sulphates that give oil of vitriol when roasted.",
    "kind:tannin": "Anything that tans a hide.",
    "kind:plant-fibre": "Fibre from a plant, for paper and rope; not wool, not silk.",
    "kind:varnish-resin": "A resin that dissolves into a varnish or melts into a wax.",
    "kind:fat": "Fats and oils, for the soap kettle.",
    "kind:candle-stock": "What a candle can be dipped from.",
    "kind:writing-surface": "What is written or drawn on.",
    "kind:plain-cloth": "Undyed everyday cloth.",
    "kind:gemstone": "A stone a jeweller sets. Not the finished jewellery, which would feed on itself.",
    "kind:incense-resin": "A resin burnt for its smoke.",
    "kind:red-dyestuff": "A plant that dyes red.",
    "kind:scarlet-insect": "An insect that dyes scarlet.",
    "kind:ammonia-source": "Anything the ammonia salts and spirits can be got from.",
    "kind:lime-source": "Anything that burns to quicklime.",
    "kind:soda-source": "Anything that burns or boils to soda ash.",
    "kind:chloride": "A source of the muriatic part of aqua regia.",
    "kind:tar-stock": "Anything that boils down to pitch.",
    "kind:soot-fuel": "Anything burnt under a hood for its soot.",
    "kind:lens-stock": "Clear enough to grind a lens from.",
}

# ---------------------------------------------------------------------------------------
# 2. Varieties: namespaces whose tags ride from inputs to outputs, the goods that carry
#    them, and the slots that pass them on.
# ---------------------------------------------------------------------------------------
VARIETY_NAMESPACES = {
    "heart": "Which heart a golem was given.",
    "fit": "An optional fitting built into a golem.",
    "soil": "Which of the seventeen soils: it travels from the clay to the golem's body to the golem.",
    "grain": "Which grain: it travels to the flour, the malt, the loaf and the beer.",
    "fat": "Which fat or wax a soap or a candle was made from.",
    "cloth": "Which cloth clothes were cut from.",
    "colour": "Which dye a cloth took.",
    "gem": "Which stone a jewel was set with.",
}

VARIETY_TAGS = {
    "h1": ["heart:arsenic"], "h2": ["heart:antimony"], "h3": ["heart:bismuth"],
    "bronze": ["fit:bronze-joints"], "smalt": ["fit:smalt-eyes"], "lunar": ["fit:caustic-nerves"], "clockw": ["fit:clockwork-hands"],
    "tallow": ["fat:tallow"], "oil": ["fat:olive"], "palmoil": ["fat:palm"], "wax": ["fat:beeswax"],
    "linen": ["cloth:linen"], "woolc": ["cloth:wool"], "cottonc": ["cloth:cotton"],
    "madderl": ["colour:red"], "weldd": ["colour:yellow"], "indigod": ["colour:blue"], "ironblk": ["colour:black"], "scarlet": ["colour:scarlet"],
    "lapis": ["gem:lapis"], "jade": ["gem:jade"], "amber": ["gem:amber"], "obs": ["gem:obsidian"], "rockc": ["gem:rock-crystal"],
}

SOILS = ["frostearth", "bleachearth", "shadowearth", "murkearth", "dryearth", "blackearth", "brownearth", "muckearth", "dustearth",
         "yellowearth", "redearth", "floodearth", "stone", "scree", "silt", "sand", "snow"]

AUTHORED = {
    "soil": [(s, s.capitalize(), [f"soil:{s}"], "") for s in SOILS],
    "grain": [("wheat", "Wheat", ["grain:wheat"], "The temperate staple."),
              ("rye", "Rye", ["grain:rye"], "Grows where wheat will not: cold, poor ground."),
              ("barley", "Barley", ["grain:barley"], "The brewer's grain.")],
}

# (recipe id, slot index): what fills it marks what comes out.
PASSES = [
    ("r.golem", 1), ("r.golem", 2), ("r.golem", 4), ("r.golem", 5), ("r.golem", 6), ("r.golem", 7),
    ("r.body", 0),
    ("r.flour", 0), ("r.bread", 0), ("r.malt", 0), ("r.beer", 0),
    ("r.soap", 1), ("r.candles", 0),
    ("r.clothes", 0), ("r.dyed", 2), ("r.jewel", 2),
]


def main():
    web = json.load(open(ROOT / "full-ledger.json"))
    web = copy.deepcopy(web)
    web["id"] = "tagged-ledger"
    web["name"] = "The full ledger, tagged"
    web["note"] = (
        "The full ledger with its either-or slots turned into tag slots wherever a tag can carry the meaning, "
        "and with varieties switched on as a worked example: hearts, fittings and soils on the golem, grain down "
        "to the loaf and the beer, fats in soap and candles, cloth, dye colours, gemstones. It has its own palette, "
        "so nothing here touches the full ledger. Made by tools/make_tagged_ledger.py; every choice is there to overrule."
    )
    palette = web["palette"]
    goods = {g["id"]: g for g in palette["goods"]}
    recipes = {r["id"]: r for r in web["recipes"]}
    name = lambda i: goods[i]["name"]

    def carriers(tag):
        return [g["id"] for g in palette["goods"] if tag in g["tags"]]

    either_or = sum(1 for r in web["recipes"] for s in r["inputs"] if len([a for a in s["accepts"] if not a.startswith("#")]) > 1)

    # ---- the hartshorn recipe is two recipes ------------------------------------------
    # The export wrote "from distilled horn, or from sal ammoniac and lime" as three alternatives
    # of one slot, which says sal ammoniac alone will do. It is two ways of making it.
    harts = recipes["r.harts"]
    assert harts["inputs"][0]["accepts"] == ["horn", "sama", "qlime"], harts
    harts["inputs"] = [{"accepts": ["horn"]}]
    second = {"id": "r.harts.2", "name": "", "note": "From sal ammoniac and lime.",
              "inputs": [{"accepts": ["sama"]}, {"accepts": ["qlime"]}], "outputs": [{"good": "harts"}]}
    web["recipes"].insert(web["recipes"].index(harts) + 1, second)
    recipes[second["id"]] = second
    x, y = web["layout"]["r.harts"]
    web["layout"]["r.harts.2"] = [x, y + 110]
    SLOTS[("r.harts", 0)] = ("kind:ammonia-source", [], "source",
                             "was Horn | Sal ammoniac | Quicklime in one slot, which was wrong: the second way needs both, "
                             "so it is now a recipe of its own (r.harts.2); the first takes any ammonia source")

    # ---- tags onto goods, tags into slots --------------------------------------------
    print("EITHER-OR SLOTS INTO TAGS\n")
    rows = []
    for (rid, index), (tag, tagged, group, remark) in SLOTS.items():
        slot = recipes[rid]["inputs"][index]
        was = [a for a in slot["accepts"] if not a.startswith("#")]
        for gid in tagged:
            if tag not in goods[gid]["tags"]:
                goods[gid]["tags"].append(tag)
        now = carriers(tag)
        missing = [a for a in was if a not in now]
        assert not missing, (rid, index, tag, missing)
        made = [o["good"] for o in recipes[rid]["outputs"]]
        assert not set(made) & set(now), f"{rid}: {tag} would admit its own output"
        slot["accepts"] = ["#" + tag]
        extra = [a for a in now if a not in was]
        rows.append((group, rid, index, was, tag, extra, remark))

    # The indigo vat: any alkali, or stale urine, which is not an alkali until it has stood.
    vat = recipes["r.indigod"]["inputs"][1]
    assert vat["accepts"] == ["urine", "lye"], vat
    vat["accepts"] = ["#kind:alkali", "urine"]
    rows.append(("mixed", "r.indigod", 1, ["urine", "lye"], "kind:alkali", [a for a in carriers("kind:alkali") if a != "lye"],
                 "a tag and a good in one slot: any alkali, or urine, which should not be tagged an alkali for one recipe's sake"))

    order = {"named": 0, "category": 1, "source": 2, "mixed": 3}
    labels = {"named": "A category the brainstorm had already named", "category": "A real family that had no tag",
              "source": "Several sources of one thing: the list under a name", "mixed": "A tag and a good together"}
    last = None
    for group, rid, index, was, tag, extra, remark in sorted(rows, key=lambda r: (order[r[0]], r[4], r[1])):
        if group != last:
            print(f"\n## {labels[group]}")
            last = group
        title = "→ " + ", ".join(name(o["good"]) for o in recipes[rid]["outputs"])
        print(f"- {title}: {' | '.join(name(a) for a in was)}  =>  #{tag}"
              + (f"  (also admits {', '.join(name(a) for a in extra)})" if extra else "")
              + (f"\n    {remark}" if remark else ""))

    left = [(r["id"], i, s["accepts"]) for r in web["recipes"] for i, s in enumerate(r["inputs"])
            if len([a for a in s["accepts"] if not a.startswith("#")]) > 1]
    print("\n## Left as a list")
    for rid, index, accepts in left:
        print(f"- → {', '.join(name(o['good']) for o in recipes[rid]['outputs'])}: {' | '.join(name(a) for a in accepts)}")
    tags_used = sorted({r[4] for r in rows})
    new_tags = [t for t in tags_used if t in TAG_NOTES]
    whole = sum(1 for r in rows if r[0] != "mixed" and (r[1], r[2]) != ("r.harts", 0))
    print(f"\n{either_or} either-or slots in the full ledger: {whole} became one tag, 1 became a tag and a good, "
          f"1 was two recipes in one slot and was split, {len(left)} left as a list. "
          f"{len(tags_used)} tags did it: {len(tags_used) - len(new_tags)} that existed, {len(new_tags)} new.")

    for tag, note in TAG_NOTES.items():
        entry = next((t for t in palette["tags"] if t["id"] == tag), None)
        if entry is None:
            palette["tags"].append({"id": tag, "note": note})
        else:
            entry["note"] = note

    # ---- varieties -----------------------------------------------------------------------
    for ns, note in VARIETY_NAMESPACES.items():
        palette["tagNamespaces"].append({"id": ns, "note": note, "role": "variety"})
    for gid, tags in VARIETY_TAGS.items():
        goods[gid]["tags"].extend(t for t in tags if t not in goods[gid]["tags"])
    for gid, varieties in AUTHORED.items():
        goods[gid]["varieties"] = [{"id": vid, "name": vname, "note": vnote, "tags": vtags} for vid, vname, vtags, vnote in varieties]
    for rid, index in PASSES:
        recipes[rid]["inputs"][index]["passes"] = True

    out = ROOT / "tagged-ledger.json"
    json.dump(web, open(out, "w"), indent=2, ensure_ascii=False)
    open(out, "a").write("\n")
    print(f"\nwrote {out}")


if __name__ == "__main__":
    main()
