#!/usr/bin/env python3
"""Byproduct candidates for the variety ledger (resources/economy/webs/variety-ledger.json).

Reads every recipe in the ledger and, by hand-authored judgement (the CANDIDATES
table below), proposes a second, by-product output for the recipes whose real
process is well known to throw one off: bran from milling, slag from smelting,
whey from cheese-making, and so on. Deliberately not exhaustive -- most recipes
(alloying, mixing, assembly, most magisteries) get nothing, because a plausible
"waste" output that nobody would use is not worth modelling.

For every candidate the script computes, BY CODE, which recipes and consumers
of the *current* web would accept the by-product (matched by good id or by any
tag it carries), and which recipes already make it as a main output. It writes
tools/byproduct_candidates.json and prints a summary.

Run: python3 tools/byproduct_candidates.py
"""
import json
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
LEDGER_PATH = REPO_ROOT / "resources" / "economy" / "webs" / "variety-ledger.json"
OUT_PATH = Path(__file__).resolve().parent / "byproduct_candidates.json"

# ---------------------------------------------------------------------------
# The judgement table: (recipe_id, byproduct, amount_per_run_guess, why)
#
# `byproduct` is a palette good id when one fits, otherwise a short proposed
# name prefixed "new:". A recipe may appear more than once when its process
# plausibly throws off more than one distinct by-product worth naming (only
# charcoal-burning does, below -- tar and pyroligneous acid are the two
# fractions of the same condensed smoke, and both are namable goods).
# ---------------------------------------------------------------------------

CANDIDATES = [
    # --- ore smelting: the universal by-product is slag -----------------
    ("r.ife", "new:slag", 1.5,
     "Bloomery or blast-furnace smelting of ironstone throws heavy silicate "
     "slag; historically tipped, road-metalled, or spun into slag wool."),
    ("r.isn", "new:slag", 1.0,
     "Smelting cassiterite is a reduction like any other ore; it leaves a "
     "tin-rich slag valuable enough to be re-smelted rather than dumped."),
    ("r.ipb", "new:slag", 1.0,
     "Smelting galena to lead leaves a lead-silicate slag at the furnace "
     "foot, periodically re-run for the metal still locked in it."),
    ("r.reg", "new:slag", 0.5,
     "Reducing stibnite with iron to strike the star throws an iron-sulfide "
     "matte (liver of antimony) off the melt, skimmed away as dross."),

    # --- acid/salt works: a residue is literally another named good -----
    ("r.oov", "rouge", 0.3,
     "Distilling vitriol for oil of vitriol leaves a calcined red iron-oxide "
     "residue (colcothar) in the retort -- the same stuff rouge is made "
     "from directly by roasting copperas."),
    ("r.was", "kingsy", 0.3,
     "Roasting realgar to sublime white arsenic leaves an orpiment-like "
     "yellow sulfide residue behind, the same substance king's yellow is "
     "made from directly."),
    ("r.zaf", "was", 0.3,
     "Cobalt ores are typically arsenical; roasting them for zaffre "
     "volatilises arsenic trioxide, historically the main by-product of the "
     "Saxon cobalt works, condensed as a poor man's white arsenic."),
    ("r.copperas", "brim", 0.2,
     "Roasting marcasite for green vitriol drives off sulfur, which "
     "condenses as a little brimstone alongside the vitriol."),
    ("r.salt.sea", "new:bittern", 0.3,
     "The mother liquor left in the pans once salt has crystallised out is "
     "rich in magnesium and other salts -- bittern, an alchemist's curiosity "
     "and a source for Epsom-like salts."),

    # --- milling, pressing, brewing: the classic farmyard by-products ---
    ("r.honey", "wax", 0.3,
     "Rendering honeycomb for honey leaves the wax comb behind, melted down "
     "and strained separately -- the good's own note says one harvest gives "
     "two goods: honey to eat, wax to burn and seal."),
    ("r.flour", "new:bran", 0.3,
     "Milling grain into flour sieves out the husk and germ as bran, kept "
     "for animal feed or coarse baking."),
    ("r.wine", "new:pomace", 1.0,
     "Pressing grapes for wine leaves the skins, pips and stems (marc), "
     "fermented on their own into a rough spirit or spread as fertiliser."),
    ("r.oil", "new:olive-pomace", 1.0,
     "Pressing olives leaves a pulp of skins and stones, burned as a poor "
     "fuel or re-pressed for coarse lamp oil."),
    ("r.linthr", "new:tow", 0.4,
     "Scutching flax for fine linen thread leaves short broken fibre (tow), "
     "spun coarse or picked into oakum for caulking."),
    ("r.linseed", "new:linseed-cake", 0.5,
     "Pressing flax seed for oil leaves a protein-rich cake, a valued "
     "cattle feed."),
    ("r.hempf", "new:hemp-tow", 0.4,
     "Breaking and scutching hemp for fibre leaves short tow and the woody "
     "hurds, good for coarse cordage, paper stock or tinder."),
    ("r.cotthr", "new:cottonseed", 0.5,
     "Ginning cotton separates out the seed, pressed in a small way for oil "
     "or fed to stock."),
    ("r.sugar", "new:molasses", 0.8,
     "Boiling cane juice down to crystallised sugar leaves a thick mother "
     "liquor, molasses -- fermented into rum or sold as a cheap sweetener."),
    ("r.opium", "new:poppyseed", 0.3,
     "After the pod is scored for its latex the ripe seed is harvested too, "
     "pressed for a mild cooking oil."),
    ("r.planks", "new:sawdust", 0.5,
     "Sawing timber square for planks throws off sawdust and edge offcuts, "
     "swept up for kindling or stable bedding."),
    ("r.charcoal", "new:wood-tar", 0.2,
     "Charring wood in a closed kiln condenses a tarry pyroligneous liquor; "
     "skimmed off, the tar waterproofs rope and timber like pine pitch."),
    ("r.charcoal", "new:pyroligneous-acid", 0.2,
     "The same condensate, once the tar is skimmed off, yields a sour crude "
     "wood-vinegar used to preserve and pickle."),
    ("r.turps", "new:rosin", 1.0,
     "Distilling raw resin for spirit of turpentine leaves solid rosin "
     "(colophony) behind in the still, used to varnish, seal casks and "
     "rosin a bow."),
    ("r.silkthr", "new:pupae", 0.4,
     "Reeling silk in hot water kills and loosens the pupa inside each "
     "cocoon; scooped out, it is a cheap protein for fish, fowl or table."),
    ("r.yarn", "new:wool-grease", 0.2,
     "Scouring raw fleece before spinning renders a greasy skim off the "
     "wash water (wool grease/suint), used crudely in ointments and "
     "axle-grease."),
    ("r.glue", "new:bone-grease", 0.2,
     "Boiling bones and hide clippings down for glue skims a layer of fat "
     "off the pot, rendered like tallow for cheap soap or candles."),
    ("r.parch", "glue", 0.2,
     "Scraping and trimming a limed skin to make parchment leaves clippings "
     "and shavings, boiled down into a fine glue used for gilding and "
     "bookbinding."),
    ("r.leather", "new:hair", 0.3,
     "Hides are limed to loosen the hair before tanning; the scraped-off "
     "hair is swept up and worked into lime plaster as a binder."),
    ("r.cheese", "new:whey", 0.8,
     "Curdling milk for cheese leaves the watery whey behind, drunk, fed to "
     "pigs, or boiled down further for a soft whey cheese."),
    ("r.saltfish", "new:fish-oil", 0.2,
     "Gutting and splitting fish before salting yields a rank oil rendered "
     "from the trimmings, burned in poor lamps or dressed into leather."),
    ("r.coffeeb", "new:coffee-husk", 0.3,
     "Hulling and roasting coffee cherries leaves the dried husk and "
     "parchment skin, burned as fuel around the mill or steeped as a rough "
     "cherry tea at origin."),
    ("r.shellac", "dye", 0.3,
     "Washing sticklac to melt out the shellac resin first rinses a red dye "
     "into the wash-water -- sticklac's own note says one harvest gives a "
     "varnish, a wax and a red dye."),
    ("r.ultram", "new:ultramarine-ash", 0.3,
     "Kneading blue out of a lapis dough is done in several washes; the "
     "last, palest extraction is ultramarine ash, a cheaper grey-blue "
     "pigment sold on its own."),
    ("r.glass", "new:glass-gall", 0.1,
     "The scum skimmed off the top of a glass melt is sandiver (glass "
     "gall), alkaline salts some glassmakers saved as a coarse flux."),
    ("r.beer", "new:spent-grain", 1.0,
     "Mashing malt for beer leaves the spent grain (draff) once the wort is "
     "drained off, prime cattle feed and a baker's cheap flour stretcher."),
    ("r.masa", "new:nejayote", 0.3,
     "Steeping maize in lime water to make nixtamal leaves an alkaline "
     "steep liquor, fed to animals or worked back into the fields."),
    ("r.ricew", "new:rice-lees", 0.4,
     "Pressing the fermented mash for rice wine leaves a sweet, boozy lees, "
     "used to pickle vegetables or flavour a dish, much as sake kasu is "
     "today."),
    ("r.choc", "new:cacao-husk", 0.2,
     "Winnowing the papery husk off roasted cacao beans before grinding "
     "leaves chaff, brewed thin as a cheap cacao tea or burned as kindling."),
    ("r.clothes", "new:rag-scraps", 0.2,
     "Cutting cloth to a clothing pattern leaves scraps and offcuts, the "
     "ragman's stock and, pulped, the papermaker's best raw material."),
    ("r.shoes", "new:leather-scraps", 0.2,
     "Cutting leather to a shoe pattern leaves offcuts too small for boots "
     "but big enough for a patch, a hinge, or the glue-pot."),
    ("r.hats", "new:hat-pelts", 0.5,
     "Felting only the barbed underfur off a beaver pelt leaves the skin "
     "itself, tanned into a coarse leather or boiled for glue."),
    ("r.harts", "new:bone-black", 0.2,
     "Roasting horn or bone for spirit of hartshorn chars the residue into "
     "bone black, a fine black pigment and the sugar-refiner's decolorising "
     "char."),
    ("r.giltw", "hg", 0.2,
     "Heating a gilded piece to drive off the amalgam's mercury lets a "
     "careful gilder catch and condense some of the fume back to quicksilver "
     "under a hood."),
    ("r.brick", "new:brick-dust", 0.2,
     "A brick kiln always over- and under-fires part of the load; the "
     "broken bats are ground to dust and worked into mortar for a harder, "
     "pozzolanic set."),
]


def load_web():
    with open(LEDGER_PATH, encoding="utf-8") as f:
        return json.load(f)


def slot_accepts(accepts, good_id, good_tags):
    """Does one input slot's `accepts` list admit this good?"""
    for entry in accepts:
        if entry.startswith("#"):
            if entry[1:] in good_tags:
                return True
        elif entry == good_id:
            return True
    return False


def main():
    web = load_web()
    goods_by_id = {g["id"]: g for g in web["palette"]["goods"]}
    recipes_by_id = {r["id"]: r for r in web["recipes"]}
    consumers = web["consumers"]

    # --- assertions -------------------------------------------------------
    for recipe_id, byproduct, amount, why in CANDIDATES:
        assert recipe_id in recipes_by_id, f"unknown recipe id: {recipe_id}"
        if not byproduct.startswith("new:"):
            assert byproduct in goods_by_id, (
                f"byproduct '{byproduct}' for {recipe_id} is not a palette good "
                f"id (and is not prefixed new:)"
            )

    # --- compute usage, by code, for every candidate -----------------------
    results = []
    for recipe_id, byproduct, amount, why in CANDIDATES:
        recipe = recipes_by_id[recipe_id]
        in_palette = not byproduct.startswith("new:")

        used_by = []
        consumed_by = []
        also_made_by = []

        if in_palette:
            good = goods_by_id[byproduct]
            good_tags = set(good.get("tags", []))

            for other_id, other in recipes_by_id.items():
                if any(
                    slot_accepts(slot["accepts"], byproduct, good_tags)
                    for slot in other["inputs"]
                ):
                    used_by.append(other_id)

            for consumer in consumers:
                if slot_accepts(consumer["accepts"], byproduct, good_tags):
                    consumed_by.append(consumer["id"])

            for other_id, other in recipes_by_id.items():
                if other_id == recipe_id:
                    continue
                if any(o["good"] == byproduct for o in other["outputs"]):
                    also_made_by.append(other_id)

        results.append({
            "recipe": recipe_id,
            "recipe_name": recipe.get("name", ""),
            "byproduct": byproduct,
            "in_palette": in_palette,
            "amount": amount,
            "why": why,
            "used_by": used_by,
            "consumed_by": consumed_by,
            "also_made_by": also_made_by,
        })

    results.sort(key=lambda r: r["recipe"])

    OUT_PATH.write_text(json.dumps(results, indent=2) + "\n", encoding="utf-8")

    # --- summary ------------------------------------------------------------
    total_recipes = len(recipes_by_id)
    recipes_with_candidate = len({r["recipe"] for r in results})
    in_palette_rows = [r for r in results if r["in_palette"]]
    new_rows = [r for r in results if not r["in_palette"]]

    def has_use(r):
        return bool(r["used_by"]) or bool(r["consumed_by"])

    useful_rows = [r for r in in_palette_rows if has_use(r)]

    print(f"Total recipes in the web: {total_recipes}")
    print(f"Recipes with a by-product candidate: {recipes_with_candidate}")
    print(f"Candidates total: {len(results)}")
    print(f"Candidates in-palette: {len(in_palette_rows)}")
    print(f"In-palette candidates that close a link (used or consumed somewhere): "
          f"{len(useful_rows)}")
    print(f"Candidates proposing a new good: {len(new_rows)}")
    print()
    print("Top useful in-palette by-products (recipe -> byproduct: used_by / consumed_by):")

    def score(r):
        return len(r["used_by"]) + len(r["consumed_by"])

    top = sorted(useful_rows, key=score, reverse=True)[:15]
    for r in top:
        also = f", already made by {r['also_made_by']}" if r["also_made_by"] else ""
        print(
            f"  {r['recipe']} -> {r['byproduct']}: "
            f"used_by={r['used_by']} consumed_by={r['consumed_by']}{also}"
        )


if __name__ == "__main__":
    main()
