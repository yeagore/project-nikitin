#!/usr/bin/env python3
"""Checks tools/extractions.json against resources/economy/webs/variety-ledger.json.

Usage (from the repo root):
    python3 tools/check_extractions.py

The designer's ruling of 2026-09-23: every good comes out of something. A raw good comes
out of an extraction, a recipe that takes nothing and stands on a site (tags of the ground
or place). This checks the table of new extractions in tools/extractions.json against that
ruling and against the web it will eventually be folded into: ids don't clash, every site
tag is one the terrain generator can actually test for, every output is a source good
(never one some recipe already makes), every source ends up a main output of something,
mined goods carry a deposit note and stand on rock, and no entry pretends to take an input
(an extraction takes nothing). It also reports the whole web's element shares with the new
extractions added, since the lab's self-test holds fire/wind/water/earth each to 15-35%.

This script only reads; it writes nothing and changes neither file.
"""
import json
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
WEB_PATH = ROOT / "resources" / "economy" / "webs" / "variety-ledger.json"
TABLE_PATH = ROOT / "tools" / "extractions.json"

# The site vocabulary (docs/economy-lab.md and the designer's ruling of 2026-09-23): tags of
# one namespace are alternatives, different namespaces must all hold. Hard-coded here, not
# read off the web's tags, because the web carries other namespaces (variety, property) that
# are not sites even though some of them (soil) double as one.
SOILS = {
    "soil:stone", "soil:scree", "soil:snow", "soil:sand", "soil:silt", "soil:ooze",
    "soil:frostearth", "soil:bleachearth", "soil:shadowearth", "soil:murkearth",
    "soil:dryearth", "soil:blackearth", "soil:brownearth", "soil:dustearth",
    "soil:yellowearth", "soil:redearth", "soil:floodearth", "soil:muckearth",
}
ANCHORS = {
    "anchor:river", "anchor:falls", "anchor:spring", "anchor:hot-spring", "anchor:lakeshore",
    "anchor:shallows", "anchor:deep", "anchor:salt-lake", "anchor:rim", "anchor:fjord",
    "anchor:sea-stack", "anchor:estuary", "anchor:delta", "anchor:cliff-foot",
    "anchor:cliff-brink", "anchor:scarp", "anchor:summit", "anchor:overhang",
}
WARMTHS = {"warmth:frigid", "warmth:cold", "warmth:temperate", "warmth:hot"}
MOISTURES = {"moisture:dry", "moisture:balanced", "moisture:wet"}
EXPOSURES = {"exposure:sheltered", "exposure:windswept"}
SITE_VOCAB = SOILS | ANCHORS | WARMTHS | MOISTURES | EXPOSURES
ROCK_TAGS = {"soil:stone", "soil:scree"}
ELEMENTS = {"earth", "water", "wind", "fire", "qe"}
BALANCED_ELEMENTS = ["earth", "fire", "wind", "water"]  # qe is exempt (magical, rare by design)
LO, HI = 0.15, 0.35


def fail(problems):
    print(f"FAILED: {len(problems)} problem(s)")
    for p in problems[:200]:
        print(" -", p)
    if len(problems) > 200:
        print(f"   ... and {len(problems) - 200} more")
    raise SystemExit(1)


def main():
    web = json.load(open(WEB_PATH))
    table = json.load(open(TABLE_PATH))

    palette = web["palette"]
    goods = {g["id"]: g for g in palette["goods"]}
    existing_recipes = web["recipes"]
    existing_ids = {r["id"] for r in existing_recipes}
    existing_outputs = {o["good"] for r in existing_recipes for o in r["outputs"]}
    sources = {gid for gid in goods if gid not in existing_outputs}

    problems = []

    # ---- shape and id checks --------------------------------------------------------------
    seen_ids = set()
    for i, e in enumerate(table):
        eid = e.get("id")
        where = eid or f"entry #{i}"
        if not eid:
            problems.append(f"{where}: missing id")
            continue
        if eid in existing_ids:
            problems.append(f"{eid}: id clashes with an existing recipe id in the web")
        if eid in seen_ids:
            problems.append(f"{eid}: id used twice in the table")
        seen_ids.add(eid)

        if e.get("inputs"):
            problems.append(f"{eid}: has inputs; an extraction takes nothing")

        element = e.get("element")
        if element not in ELEMENTS:
            problems.append(f"{eid}: element {element!r} is not one of {sorted(ELEMENTS)}")

        site = e.get("site")
        if not site:
            problems.append(f"{eid}: no site")
        else:
            for tag in site:
                if tag not in SITE_VOCAB:
                    problems.append(f"{eid}: site tag {tag!r} is not in the site vocabulary")

        outputs = e.get("outputs")
        if not outputs:
            problems.append(f"{eid}: no outputs")
            continue
        for o in outputs:
            gid = o.get("good")
            if gid not in goods:
                problems.append(f"{eid}: output good {gid!r} does not exist in the web's palette")
            elif gid not in sources:
                problems.append(f"{eid}: output good {gid!r} is not a source (some recipe already makes it)")

    # ---- every source is a main output somewhere -------------------------------------------
    main_outputs = set()
    all_outputs = set()
    for e in table:
        for o in e.get("outputs", []):
            gid = o.get("good")
            all_outputs.add(gid)
            if not o.get("byProduct"):
                main_outputs.add(gid)
    for gid in sorted(sources):
        if gid not in main_outputs:
            problems.append(f"{gid} ({goods[gid]['name']}): a source but never a main output of any entry")

    # ---- mined goods: deposit + rock site ---------------------------------------------------
    mined = {gid for gid, g in goods.items() if "source:mined" in g.get("tags", []) or "kind:ore" in g.get("tags", [])}
    output_entries = {}  # good id -> list of entries that output it as a main
    for e in table:
        for o in e.get("outputs", []):
            if not o.get("byProduct"):
                output_entries.setdefault(o.get("good"), []).append(e)
    for gid in sorted(mined & sources):
        entries = output_entries.get(gid, [])
        if not entries:
            continue  # already reported above as missing main coverage
        for e in entries:
            if not e.get("deposit"):
                problems.append(f"{e['id']} ({gid}): mined good has no \"deposit\"")
            site = set(e.get("site") or [])
            if not site & ROCK_TAGS:
                problems.append(f"{e['id']} ({gid}): mined good's site {sorted(site)} has no rock tag ({sorted(ROCK_TAGS)})")

    if problems:
        fail(problems)

    # ---- report -----------------------------------------------------------------------------
    byproducts = sum(1 for e in table for o in e["outputs"] if o.get("byProduct"))
    with_deposit = [e["id"] for e in table if e.get("deposit")]
    with_could_take = [e["id"] for e in table if e.get("could_take")]
    with_uncertain = [e["id"] for e in table if e.get("uncertain")]

    print(f"sources: {len(sources)}")
    print(f"extractions: {len(table)}")
    print(f"by-products: {byproducts}")
    print(f"entries with deposit: {len(with_deposit)}")
    for eid in with_deposit:
        print(f"  - {eid}")
    print(f"entries with could_take: {len(with_could_take)}")
    for eid in with_could_take:
        print(f"  - {eid}")
    print(f"entries with uncertain: {len(with_uncertain)}")
    for eid in with_uncertain:
        print(f"  - {eid}")

    site_ns_counts = Counter()
    anchor_counts = Counter()
    for e in table:
        namespaces = {tag.split(":")[0] for tag in e.get("site", [])}
        for ns in namespaces:
            site_ns_counts[ns] += 1
        for tag in e.get("site", []):
            if tag in ANCHORS:
                anchor_counts[tag] += 1
    print("site namespace use (entries touching each):")
    for ns in sorted(site_ns_counts):
        print(f"  {ns}: {site_ns_counts[ns]}")
    print(f"anchor tags used: {len(anchor_counts)} of {len(ANCHORS)}")
    for tag in sorted(ANCHORS):
        if tag in anchor_counts:
            print(f"  {tag}: {anchor_counts[tag]}")
    unused_anchors = ANCHORS - set(anchor_counts)
    print(f"anchor tags unused ({len(unused_anchors)}): {', '.join(sorted(unused_anchors)) or '(none)'}")

    # ---- element shares of the whole web with the new extractions added --------------------
    existing_element_counts = Counter(r["element"] for r in existing_recipes)
    new_element_counts = Counter(e["element"] for e in table)
    combined = Counter()
    combined.update(existing_element_counts)
    combined.update(new_element_counts)
    grand_total = sum(combined.values())
    print(f"\nelement shares of the whole web with these {len(table)} extractions added "
          f"(existing {len(existing_recipes)} + new {len(table)} = {grand_total} recipes):")
    out_of_band = []
    for el in BALANCED_ELEMENTS:
        n = combined.get(el, 0)
        share = n / grand_total
        flag = "" if LO <= share <= HI else "  <-- OUT OF 15-35% BAND"
        if flag:
            out_of_band.append(el)
        print(f"  {el}: {n} ({share*100:.2f}%){flag}")
    qe_n = combined.get("qe", 0)
    print(f"  qe: {qe_n} ({qe_n/grand_total*100:.2f}%)  (exempt from the 15-35% band)")

    if out_of_band:
        fail([f"{el} share is outside the required 15-35% band" for el in out_of_band])

    print("\nOK")


if __name__ == "__main__":
    main()
