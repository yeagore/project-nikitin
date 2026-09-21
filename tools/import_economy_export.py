#!/usr/bin/env python3
"""Convert the Project Nikitin economy design export into the data files of
the "economy lab" editor: a goods catalogue, and two "webs" (versions of the
economy) built from it.

Usage:
    python3 import_economy_export.py <export_dir> <out_dir>

Standard library only. See the export's README.md for the source shapes and
docs/economy-lab.md for the output shapes and what the import changed. A one-off:
the output goes into resources/economy/, and running it again over a folder the
lab has edited overwrites those edits. After it, run the lab's bake
(godot --path . --headless scenes/dev/economy_lab.tscn -- bake) to arrange the
webs and rewrite the files in the lab's own format.
"""

import json
import re
import shutil
import sys
from collections import OrderedDict, deque
from pathlib import Path

# ---------------------------------------------------------------------------
# small helpers
# ---------------------------------------------------------------------------

_FAILURES = []


def fail(msg):
    """Record a verification failure. Collected failures are reported (and
    cause a non-zero exit) at the end, so one run surfaces everything wrong
    rather than stopping at the first problem."""
    _FAILURES.append(msg)
    print(f"ASSERTION FAILED: {msg}", file=sys.stderr)


def die(msg):
    """A structural problem bad enough that continuing is meaningless."""
    print(f"FATAL: {msg}", file=sys.stderr)
    sys.exit(1)


def slug(text):
    """lowercase, every run of non-alphanumerics -> '-' """
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def load_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def write_json(obj, path):
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2, ensure_ascii=False)
        f.write("\n")


# ---------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------


def main():
    if len(sys.argv) != 3:
        print("usage: python3 import_economy_export.py <export_dir> <out_dir>", file=sys.stderr)
        sys.exit(2)

    export_dir = Path(sys.argv[1])
    out_dir = Path(sys.argv[2])

    goods_path = export_dir / "data" / "goods.json"
    tags_path = export_dir / "data" / "tags.json"
    if not goods_path.is_file():
        die(f"missing {goods_path}")
    if not tags_path.is_file():
        die(f"missing {tags_path}")

    goods_data = load_json(goods_path)
    tags_data = load_json(tags_path)

    source_goods = goods_data["goods"]
    expected_good_count = goods_data["meta"]["goods"]
    expected_link_count = goods_data["meta"]["recipe_links"]

    if len(source_goods) != expected_good_count:
        die(f"goods.json meta says {expected_good_count} goods but the array has {len(source_goods)}")

    ids_seen = set()
    for g in source_goods:
        if g["id"] in ids_seen:
            die(f"duplicate good id in source export: {g['id']}")
        ids_seen.add(g["id"])
    all_good_ids = ids_seen

    # -----------------------------------------------------------------
    # per-good transformed tags: copy in order, drop trait:hub, append
    # kind:golem-heart to h1/h2/h3. Also assert stage/group/need mirroring
    # before those fields get dropped.
    # -----------------------------------------------------------------

    GOLEM_HEART_GOODS = ("h1", "h2", "h3")

    transformed_tags = {}
    for g in source_goods:
        gid = g["id"]
        tags = [t for t in g["tags"] if t != "trait:hub"]
        if gid in GOLEM_HEART_GOODS:
            tags.append("kind:golem-heart")
        transformed_tags[gid] = tags

        stage_tag = f"stage:{slug(g['stage'])}"
        if stage_tag not in tags:
            fail(f"{gid}: stage {g['stage']!r} not mirrored by tag {stage_tag!r}")
        group_tag = f"group:{slug(g['group'])}"
        if group_tag not in tags:
            fail(f"{gid}: group {g['group']!r} not mirrored by tag {group_tag!r}")
        if g["need"] is not None:
            need_tag = f"need:{slug(g['need'])}"
            if need_tag not in tags:
                fail(f"{gid}: need {g['need']!r} not mirrored by tag {need_tag!r}")

    def goods_with_tag(tag):
        return [g["id"] for g in source_goods if tag in transformed_tags[g["id"]]]

    def acceptor_in_goodset(acceptor, good_ids):
        if acceptor.startswith("#"):
            tag = acceptor[1:]
            return any(tag in transformed_tags[gid] for gid in good_ids)
        return acceptor in good_ids

    # -----------------------------------------------------------------
    # catalogue: tag namespaces
    # -----------------------------------------------------------------

    tag_namespaces = []
    for ns_key, note in tags_data["namespaces"].items():
        if not ns_key.endswith(":"):
            die(f"tags.json namespace key {ns_key!r} does not end with ':'")
        tag_namespaces.append({"id": ns_key[:-1], "note": note})

    # -----------------------------------------------------------------
    # catalogue: tags in use, in order of first appearance over the
    # (transformed) goods array
    # -----------------------------------------------------------------

    tags_seen = OrderedDict()
    for g in source_goods:
        for t in transformed_tags[g["id"]]:
            tags_seen.setdefault(t, True)

    tags_out = []
    for t in tags_seen:
        note = "Fits the heart slot of a golem." if t == "kind:golem-heart" else ""
        tags_out.append({"id": t, "note": note})
    all_catalogue_tag_ids = set(tags_seen)

    # -----------------------------------------------------------------
    # catalogue: goods
    # -----------------------------------------------------------------

    ATLAS_ID_BY_SOURCE_PATH = {
        "sprites/icons.png": "icons",
        "sprites/signs.png": "signs",
    }

    catalogue_goods = []
    for g in source_goods:
        gid = g["id"]
        icon_src = g["icon"]
        sign_src = g["sign"]
        icon_atlas = ATLAS_ID_BY_SOURCE_PATH.get(icon_src["atlas"])
        sign_atlas = ATLAS_ID_BY_SOURCE_PATH.get(sign_src["atlas"])
        if icon_atlas is None:
            die(f"{gid}: unknown icon atlas {icon_src['atlas']!r}")
        if sign_atlas is None:
            die(f"{gid}: unknown sign atlas {sign_src['atlas']!r}")

        catalogue_goods.append(
            {
                "id": gid,
                "name": g["name"],
                "note": g["note"],
                "tags": transformed_tags[gid],
                "icon": {"atlas": icon_atlas, "index": int(icon_src["index"])},
                "sign": {
                    "atlas": sign_atlas,
                    "index": int(sign_src["index"]),
                    "source": sign_src["source"],
                    "reading": sign_src["reading"],
                    "parts": sign_src["parts"],
                },
            }
        )

    catalogue = {
        "format": 1,
        "title": "Project Nikitin goods catalogue",
        "note": (
            "Imported from the alchemical production tree export of 2026-09-21. "
            "Icons and signs are 16 px concept placeholders generated in chat: "
            "pre-production material."
        ),
        "atlases": [
            {"id": "icons", "file": "sprites/icons.png", "cell": 16, "columns": 16},
            {"id": "signs", "file": "sprites/signs.png", "cell": 16, "columns": 16},
        ],
        "tagNamespaces": tag_namespaces,
        "tags": tags_out,
        "goods": catalogue_goods,
    }

    # -----------------------------------------------------------------
    # full ledger: recipes
    # -----------------------------------------------------------------

    GOLEM_HEART_SLOT_ACCEPTS = ["h2", "h1", "h3"]

    def build_slot(entry, alternatives, optional):
        accepts = [entry] + [a["good"] for a in alternatives if a["replaces"] == entry]
        slot = {"accepts": accepts}
        if optional:
            slot["optional"] = True
        return slot

    full_recipes = []
    full_recipes_by_id = {}
    # gid -> list of (recipe_id, alt_good) for alternative-source recipes
    alt_source_recipes_by_good = {}
    plain_acceptor_count = 0

    for g in source_goods:
        gid = g["id"]
        recipe = g["recipe"]
        requires = recipe["requires"]
        optional_list = recipe["optional"]
        alternatives = recipe["alternatives"]

        for a in alternatives:
            if a["replaces"] is not None and a["replaces"] not in requires and a["replaces"] not in optional_list:
                fail(f"{gid}: alternative {a!r} replaces an entry not in its own requires/optional")

        if requires or optional_list:
            inputs = []
            for e in requires:
                slot = build_slot(e, alternatives, optional=False)
                if gid == "golem" and slot["accepts"] == GOLEM_HEART_SLOT_ACCEPTS:
                    slot = {"accepts": ["#kind:golem-heart"]}
                else:
                    plain_acceptor_count += len(slot["accepts"])
                inputs.append(slot)
            for e in optional_list:
                slot = build_slot(e, alternatives, optional=True)
                plain_acceptor_count += len(slot["accepts"])
                inputs.append(slot)

            rid = f"r.{gid}"
            rec = {"id": rid, "name": "", "note": "", "inputs": inputs, "outputs": [{"good": gid}]}
            full_recipes.append(rec)
            full_recipes_by_id[rid] = rec

        for a in alternatives:
            if a["replaces"] is None:
                alt_good = a["good"]
                rid = f"r.{gid}.{alt_good}"
                rec = {
                    "id": rid,
                    "name": "",
                    "note": "",
                    "inputs": [{"accepts": [alt_good]}],
                    "outputs": [{"good": gid}],
                }
                plain_acceptor_count += 1
                full_recipes.append(rec)
                full_recipes_by_id[rid] = rec
                alt_source_recipes_by_good.setdefault(gid, []).append((rid, alt_good))

    total_with_fold = plain_acceptor_count + 3  # the golem's 3 hearts, folded into one tag acceptor
    if total_with_fold != expected_link_count:
        fail(
            "link count mismatch on the full ledger: "
            f"{plain_acceptor_count} plain acceptors + 3 folded golem hearts = {total_with_fold}, "
            f"expected meta.recipe_links = {expected_link_count}"
        )

    CONSUMER_DEFS = [
        ("c.food", "Food", "#need:food"),
        ("c.intoxicants", "Intoxicants and physic", "#need:intoxicants-and-physic"),
        ("c.clothing", "Clothing", "#need:clothing"),
        ("c.wares", "Wares", "#need:wares"),
    ]

    def build_consumers(good_ids):
        good_ids = set(good_ids)
        out = []
        for cid, name, tag_accept in CONSUMER_DEFS:
            tag = tag_accept[1:]
            if any(tag in transformed_tags[gid] for gid in good_ids):
                out.append({"id": cid, "name": name, "note": "", "accepts": [tag_accept]})
        return out

    all_ids_in_source_order = [g["id"] for g in source_goods]

    full_ledger = {
        "format": 1,
        "id": "full-ledger",
        "name": "The full ledger",
        "note": "Every good and recipe of the 21 September 2026 brainstorm. A map to cut from, not a scope.",
        "goods": list(all_ids_in_source_order),
        "recipes": full_recipes,
        "consumers": build_consumers(all_ids_in_source_order),
        "layout": {},
    }

    # -----------------------------------------------------------------
    # starter web: upstream closure from {golem, bread, beer}
    # -----------------------------------------------------------------

    ALLOW_OPTIONAL = {"bronze"}
    START_SET = ["golem", "bread", "beer"]

    included = set(START_SET)
    queue = deque(START_SET)
    # gid -> list of (slot_dict, keep, mode)  mode is None for plain-keep, "filter" for
    # a kept optional slot whose accepts must be filtered against the final web at the end
    recipe_plan = {}

    while queue:
        gid = queue.popleft()
        if gid in recipe_plan:
            continue
        recipe = full_recipes_by_id.get(f"r.{gid}")
        if recipe is None:
            continue  # a source good: nothing produces it in this web

        plan = []
        for slot in recipe["inputs"]:
            is_optional = slot.get("optional", False)
            accepts = slot["accepts"]
            if not is_optional:
                for acc in accepts:
                    if acc.startswith("#"):
                        tag = acc[1:]
                        for gg in goods_with_tag(tag):
                            if gg not in included:
                                included.add(gg)
                                queue.append(gg)
                    else:
                        if acc not in included:
                            included.add(acc)
                            queue.append(acc)
                plan.append((slot, True, None))
            else:
                allow_hits = [a for a in accepts if a in ALLOW_OPTIONAL]
                if allow_hits:
                    for acc in allow_hits:
                        if acc not in included:
                            included.add(acc)
                            queue.append(acc)
                    plan.append((slot, True, "filter"))
                else:
                    plan.append((slot, False, None))
        recipe_plan[gid] = plan

    starter_recipes = []
    for gid, plan in recipe_plan.items():
        new_inputs = []
        for slot, keep, mode in plan:
            if not keep:
                continue
            if mode == "filter":
                filtered = [a for a in slot["accepts"] if acceptor_in_goodset(a, included)]
                new_slot = {"accepts": filtered, "optional": True}
                new_inputs.append(new_slot)
            else:
                new_inputs.append(slot)
        source_recipe = full_recipes_by_id[f"r.{gid}"]
        starter_recipes.append(
            {
                "id": source_recipe["id"],
                "name": source_recipe["name"],
                "note": source_recipe["note"],
                "inputs": new_inputs,
                "outputs": source_recipe["outputs"],
            }
        )

    # alternative-source recipes (r.<G>.<alt>) only if the alternative good is
    # independently in the web (never pulled in through this recipe itself)
    for gid, alts in alt_source_recipes_by_good.items():
        if gid not in included:
            continue
        for rid, alt_good in alts:
            if alt_good in included:
                starter_recipes.append(full_recipes_by_id[rid])

    starter_goods = [gid for gid in all_ids_in_source_order if gid in included]
    starter_consumers = build_consumers(included)

    starter = {
        "format": 1,
        "id": "starter",
        "name": "Starter: a golem, bread and beer",
        "note": (
            "A small web cut from the full ledger: the golem with its three hearts "
            "and bronze joints, and two things people eat and drink."
        ),
        "goods": starter_goods,
        "recipes": starter_recipes,
        "consumers": starter_consumers,
        "layout": {},
    }

    # -----------------------------------------------------------------
    # verification
    # -----------------------------------------------------------------

    if len(catalogue_goods) != 286:
        fail(f"catalogue has {len(catalogue_goods)} goods, expected 286")
    cat_ids = [g["id"] for g in catalogue_goods]
    if len(set(cat_ids)) != len(cat_ids):
        fail("catalogue good ids are not unique")

    def verify_web(web):
        name = web["id"]
        good_set = set(web["goods"])
        if len(good_set) != len(web["goods"]):
            fail(f"{name}: goods list has duplicates")

        recipe_ids = [r["id"] for r in web["recipes"]]
        if len(set(recipe_ids)) != len(recipe_ids):
            fail(f"{name}: recipe ids are not unique")

        for r in web["recipes"]:
            for o in r["outputs"]:
                if o["good"] not in good_set:
                    fail(f"{name}: recipe {r['id']} outputs {o['good']!r} which is not in the web's goods")
            for slot in r["inputs"]:
                for acc in slot["accepts"]:
                    if acc.startswith("#"):
                        tag = acc[1:]
                        if tag not in all_catalogue_tag_ids:
                            fail(f"{name}: recipe {r['id']} references unknown tag {acc!r}")
                        if not any(tag in transformed_tags[gid] for gid in good_set):
                            fail(f"{name}: recipe {r['id']} tag acceptor {acc!r} is carried by no good in the web")
                    else:
                        if acc not in good_set:
                            fail(f"{name}: recipe {r['id']} acceptor {acc!r} is not in the web's goods")

        for c in web["consumers"]:
            for acc in c["accepts"]:
                if acc.startswith("#"):
                    tag = acc[1:]
                    if not any(tag in transformed_tags[gid] for gid in good_set):
                        fail(f"{name}: consumer {c['id']} tag {acc!r} is carried by no good in the web")
                elif acc not in good_set:
                    fail(f"{name}: consumer {c['id']} acceptor {acc!r} is not in the web's goods")

    verify_web(full_ledger)
    verify_web(starter)

    # golem's special-cased slot: what would be ["h2","h1","h3"] is a tag now
    golem_recipe = full_recipes_by_id.get("r.golem")
    if golem_recipe is None:
        fail("no r.golem recipe was built")
    else:
        tag_slots = [s for s in golem_recipe["inputs"] if s["accepts"] == ["#kind:golem-heart"]]
        if len(tag_slots) != 1:
            fail("r.golem does not have exactly one #kind:golem-heart slot")

    if _FAILURES:
        print(f"\n{len(_FAILURES)} verification failure(s); see ASSERTION FAILED lines above.", file=sys.stderr)
        sys.exit(1)

    # -----------------------------------------------------------------
    # write output
    # -----------------------------------------------------------------

    write_json(catalogue, out_dir / "catalogue.json")
    write_json(full_ledger, out_dir / "webs" / "full-ledger.json")
    write_json(starter, out_dir / "webs" / "starter.json")

    sprites_src = export_dir / "sprites"
    sprites_out = out_dir / "sprites"
    sprites_out.mkdir(parents=True, exist_ok=True)
    for fname in ("icons.png", "signs.png"):
        src = sprites_src / fname
        if not src.is_file():
            die(f"missing sprite {src}")
        shutil.copyfile(src, sprites_out / fname)

    # -----------------------------------------------------------------
    # summary
    # -----------------------------------------------------------------

    def name_of(gid):
        for g in source_goods:
            if g["id"] == gid:
                return g["name"]
        return gid

    print("=" * 70)
    print("catalogue.json")
    print(f"  goods: {len(catalogue['goods'])}")
    print(f"  tag namespaces: {len(catalogue['tagNamespaces'])}")
    print(f"  tags: {len(catalogue['tags'])}")

    print("-" * 70)
    print("webs/full-ledger.json")
    print(f"  goods: {len(full_ledger['goods'])}")
    print(f"  recipes: {len(full_ledger['recipes'])}")
    print(f"  consumers: {len(full_ledger['consumers'])} ({', '.join(c['id'] for c in full_ledger['consumers'])})")

    print("-" * 70)
    print("webs/starter.json")
    print(f"  goods: {len(starter['goods'])}")
    print(f"  recipes: {len(starter['recipes'])}")
    print(f"  consumers: {len(starter['consumers'])} ({', '.join(c['id'] for c in starter['consumers'])})")
    producer_of = {r["outputs"][0]["good"] for r in starter["recipes"]}
    sources = [gid for gid in starter["goods"] if gid not in producer_of]
    print(f"  sources (nothing in this web produces them): {len(sources)}")
    print("  goods in the starter web, by name:")
    for gid in starter["goods"]:
        marker = " [source]" if gid in sources else ""
        print(f"    - {name_of(gid)} ({gid}){marker}")
    print("=" * 70)


if __name__ == "__main__":
    main()
