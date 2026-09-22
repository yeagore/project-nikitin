#!/usr/bin/env python3
"""Derive "The variety ledger" from the tagged ledger: a locked web of its own in which the
recipes' mistakes are mended, goods that were one thing in several flavours are folded into
one good with varieties, raw goods and what is made of them get many more varieties, variety
tags imply property tags that units can stack by, and every variety tag has a colour.

Usage (from the repo root):
    python3 tools/make_variety_ledger.py
    godot --path . --headless scenes/dev/economy_lab.tscn -- bake        # arranges it
    godot --path . --headless scenes/dev/economy_lab.tscn -- census web=variety-ledger

It reads resources/economy/webs/tagged-ledger.json and writes .../variety-ledger.json, and
prints what it did. If tools/variety_icons.json and tools/tag_signs.json exist (the art passes
write them) their sprites are stamped in. A one-off kept as the record of the decisions;
docs/economy-lab.md has them in words. The rules it works to:

  * A variety never gates. If some recipe or consumer would take one and refuse another, they
    are different goods (joined by a core tag where they substitute), or the recipe is asking
    for a place and not a good (a site), or the gate should be a grade.
  * Fold goods into one good with varieties when every recipe treats them alike AND a player
    would call them one thing in several flavours (dye in six colours; not porcelain and pottery).
  * A slot passes variety on only where the choice shows in the product or something will read
    it. Rations do not remember their bread.
"""

import copy
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent / "resources" / "economy" / "webs"

# ---------------------------------------------------------------------------------------
# 1. Mistakes mended.
# ---------------------------------------------------------------------------------------

# The five earths were "Soil, any of 17 -> X" though each good's note names its soils. They are
# not made of hauled soil at all: they are dug where the ground is right. A site, not a slot.
SITES = {
    "r.clay": ("Clay pit", ["soil:silt", "soil:floodearth"], "clay",
               "Dug where the ground is silt or floodearth (Limosol, Fluvisol)."),
    "r.sand": ("Sand pit", ["soil:sand"], "sand", "Dug where the ground is sand (Arenosol)."),
    "r.ochre": ("Ochre pit", ["soil:redearth", "soil:yellowearth"], "ochre",
                "Dug where the ground is redearth or yellowearth (Rubrisol, Vertisol)."),
    "r.fearth": ("Fuller's pit", ["soil:bleachearth"], "fearth",
                 "Dug where the ground is bleachearth (Cinerisol). Scours grease out of wool."),
    "r.peat": ("Peat cutting", ["soil:murkearth"], "peat",
               "Cut where the ground is murkearth (Turbisol). A poor Domain's coal."),
}

# Slots that said coal where any fuel burns as well.
FUEL_SLOTS = ["r.qlime", "r.plaster", "r.porcel"]

# ---------------------------------------------------------------------------------------
# 2. Folds: goods that were one thing in several flavours.
#    new id -> (name, note, tags, {member: (variety tag, recipe id, recipe name)}, icon/sign donor)
# ---------------------------------------------------------------------------------------
FOLDS = {
    "dye": ("Dye",
            "Colour made fast: madder red, indigo blue, weld yellow, cochineal scarlet, iron black, and green from weld "
            "over indigo. One good in six colours; the vat it came from decides which.",
            ["stage:compound", "group:powders-and-pigments", "nature:alchemical", "kind:dye"],
            {"madderl": ("colour:red", "r.madderl", "Red vat"),
             "indigod": ("colour:blue", "r.indigod", "Indigo vat"),
             "weldd": ("colour:yellow", "r.weldd", "Weld vat"),
             "scarlet": ("colour:scarlet", "r.scarlet", "Scarlet vat"),
             "ironblk": ("colour:black", "r.ironblk", "Iron-gall vat")},
            "indigod"),
    "cloth": ("Cloth",
              "Woven and finished: linen, wool, cotton or silk. What it was woven from rides with it into whatever is cut from it.",
              ["stage:compound", "group:materials", "need:clothing", "nature:mundane", "kind:cloth", "trait:staple"],
              {"linen": ("cloth:linen", "r.linen", "Linen weaving"),
               "woolc": ("cloth:wool", "r.woolc", "Wool weaving and fulling"),
               "cottonc": ("cloth:cotton", "r.cottonc", "Cotton weaving"),
               "silk": ("cloth:silk", "r.silk", "Silk weaving")},
              "linen"),
    "heart": ("Golem heart",
              "A metal heart in a brass cage. Arsenic is cheap, toxic and short-lived; antimony the dependable standard; "
              "bismuth clean, stable, and holds the most instruction.",
              ["stage:assembly", "group:golem-works", "nature:mundane", "kind:golem-part"],
              {"h1": ("heart:arsenic", "r.h1", "Arsenic heart"),
               "h2": ("heart:antimony", "r.h2", "Antimony heart"),
               "h3": ("heart:bismuth", "r.h3", "Bismuth heart")},
              "h2"),
}

# Goods that were a variety of another good all along: (gone, into, the recipe that goes with it)
ABSORBED = [
    ("scarletc", "dyed", "r.scarletc"),    # scarlet cloth is dyed cloth whose dye was scarlet
    ("bluewhite", "porcel", "r.bluewhite"),  # blue-and-white is porcelain whose optional zaffre slot was filled
]

# ---------------------------------------------------------------------------------------
# 3. Namespaces, tags, colours, and what varieties imply.
# ---------------------------------------------------------------------------------------
VARIETY_NAMESPACES = {
    "fleece": "Which animal a wool came from: it travels to the yarn and the cloth.",
    "wood": "Which timber: it travels to the planks, the hull, the ship and the lacquerware.",
    "fur": "Which fur: it travels to the hat and the trim of a court dress.",
    "fish": "Which fish was salted.",
    "milk": "Whose milk a cheese was made from.",
    "hide": "Whose hide: it travels to the leather and the shoe.",
    "grape": "Which grape a wine was pressed from.",
    "spice": "Which spice. Nothing passes it on: it is traded, not built with.",
    "metal": "Which metal a thing was made of, where several would do: bronze, iron or steel tools; gold or silver jewellery. "
             "The metals are goods of their own; this is the mark they leave.",
    "hue": "Which pigment coloured a lacquer or a sealing wax.",
    "blood": "What a golem's quickblood was enriched with.",
    "decor": "A decoration an optional slot adds.",
    "glass": "What an optional ingredient did to a glass.",
    "glaze": "What an optional ingredient did to a glaze.",
    "spark": "What an optional ingredient does to a firework.",
    "scent": "What an optional resin does to a perfume.",
}
NAMESPACE_NOTES = {  # notes reworded for namespaces the tagged ledger already had
    "fit": "An optional fitting built into a golem or a ship. Granted by the recipe's own slots, not carried by the bronze or the cannon.",
    "soil": "Which of the seventeen soils: the ground itself, as the terrain generator lays it. It is a recipe's site (peat is cut "
            "on murkearth) and the variety of dug soil, which travels to the golem's body and the golem.",
    "cloth": "Which cloth: granted by the weaving recipe, it travels to the dyed cloth and the clothes.",
    "colour": "Which colour a dye is: granted by the vat that made it, it travels to the cloth, the batik and the court dress.",
    "heart": "Which heart: granted by the recipe that cast it, it travels to the golem.",
}
PROPERTY_NAMESPACES = {
    "grade": ("How good a thing is, as prices and the better classes will read it. A scale: a unit is as good as the "
              "least of what went into it, so fine wool in a common dye is common cloth.", "lowest"),
    "work": ("What a golem is good at, from the soil of its body. Seventeen soils, five kinds of work.", None),
    "prized": ("Which people prize it. A jewel set with jade sells to the Jadefolk.", None),
}
SITE_NAMESPACE = ("site", "A place a recipe has to stand that is not a soil: the coast, a river. Read from the Domain, not hauled.")

# tag -> (colour, note, [implied property tags])
SOIL_COLOURS = {  # scripts/terrain/SurfacePalette.cs: the colour on the ground is the colour on the golem
    "stone": "#62676B", "scree": "#ABA498", "snow": "#F4F6F7", "sand": "#F1DD8B", "silt": "#8F866B", "frostearth": "#9FB0B8",
    "bleachearth": "#C3A2B8", "shadowearth": "#5A5675", "murkearth": "#7E9270", "dryearth": "#D8A86E", "blackearth": "#443A31",
    "brownearth": "#74483F", "muckearth": "#3FA39B", "dustearth": "#EADFC8", "yellowearth": "#C39A22", "redearth": "#A8463F",
    "floodearth": "#7A3E78",
}
SOIL_WORK = {
    "cold": ["frostearth", "snow", "bleachearth", "shadowearth"],
    "water": ["murkearth", "muckearth", "silt", "floodearth"],
    "field": ["blackearth", "brownearth", "yellowearth", "redearth"],
    "heat": ["dryearth", "dustearth", "sand"],
    "quarry": ["stone", "scree"],
}
TAGS = {
    "kind:alkali": ("#B9B9B9", "The fixed alkalis: potash, soda, lime, lye and wood ash. Not the volatile alkali, hartshorn.", []),
    # property tags first: the order of a scale is the order here
    "grade:coarse": ("#8A7A6B", "The poorest sort.", []),
    "grade:common": ("#B9B9B9", "The everyday sort.", []),
    "grade:fine": ("#6BB5E8", "Better than most can afford.", []),
    "grade:superb": ("#F2C230", "The best there is.", []),
    "work:field": ("#6B8F3B", "Tilling, sowing, reaping.", []),
    "work:water": ("#3FA39B", "Wet work: marsh, river, harbour.", []),
    "work:quarry": ("#8A8F94", "Rock: mines and quarries.", []),
    "work:cold": ("#BFD9E8", "Work where men freeze.", []),
    "work:heat": ("#E0662B", "Work where men burn: forges, kilns, deserts.", []),
    "prized:jadefolk": ("#4FB58A", "", []),
    "prized:lakefolk": ("#3F7FBF", "", []),
    "prized:steelfolk": ("#9FB4D9", "", []),

    "heart:arsenic": ("#8FBF6A", "Cheap, toxic, short-lived.", ["grade:coarse"]),
    "heart:antimony": ("#9FB4D9", "The dependable standard.", ["grade:common"]),
    "heart:bismuth": ("#E7A6D8", "Clean, stable, holds the most instruction.", ["grade:fine"]),
    "fit:bronze-joints": ("#C58A3D", "", []),
    "fit:smalt-eyes": ("#2B4FB5", "", []),
    "fit:caustic-nerves": ("#DDE6EE", "", []),
    "fit:clockwork-hands": ("#D9B34A", "", []),
    "fit:spyglass": ("#C9A24A", "A spyglass aboard.", []),
    "fit:charts": ("#E8DDB5", "Charts aboard.", []),
    "fit:armed": ("#5A5F66", "Cannon aboard.", []),
    "fit:provisioned": ("#B5703B", "Provisioned for a long passage.", []),
    "blood:gilded": ("#F2C230", "Quickblood enriched with potable gold.", []),

    "grain:wheat": ("#E8C45A", "", ["grade:fine"]),
    "grain:spelt": ("#D9A84E", "", ["grade:fine"]),
    "grain:rye": ("#A9773F", "", ["grade:common"]),
    "grain:barley": ("#D8D08A", "", ["grade:common"]),
    "grain:oats": ("#EFE3B5", "", ["grade:coarse"]),
    "grain:millet": ("#F0A93B", "", ["grade:coarse"]),
    "grape:red": ("#7B2C56", "", ["grade:common"]),
    "grape:white": ("#C9D66B", "", ["grade:common"]),
    "grape:muscat": ("#E3B04B", "", ["grade:fine"]),
    "wood:pine": ("#E0B26B", "", ["grade:coarse"]),
    "wood:oak": ("#A97442", "", ["grade:common"]),
    "wood:cedar": ("#B5533C", "", ["grade:fine"]),
    "wood:teak": ("#8A5A2B", "", ["grade:fine"]),
    "wood:ebony": ("#2E2622", "", ["grade:superb"]),
    "fleece:sheep": ("#E6DFCF", "", ["grade:common"]),
    "fleece:cashmere": ("#C8B59B", "", ["grade:fine"]),
    "fleece:alpaca": ("#8B6B4F", "", ["grade:fine"]),
    "cloth:linen": ("#E8E0CC", "", ["grade:common"]),
    "cloth:cotton": ("#F4F4F0", "", ["grade:common"]),
    "cloth:wool": ("#C9B79A", "Its grade is its fleece's.", []),
    "cloth:silk": ("#F2D7E8", "", ["grade:superb"]),
    "colour:black": ("#2B2B33", "", ["grade:common"]),
    "colour:yellow": ("#E3C21E", "", ["grade:common"]),
    "colour:red": ("#B5302B", "", ["grade:common"]),
    "colour:blue": ("#2F4FA8", "", ["grade:common"]),
    "colour:green": ("#3E8F4A", "", ["grade:fine"]),
    "colour:scarlet": ("#E8202A", "", ["grade:superb"]),
    "hide:cattle": ("#8A5630", "", ["grade:common"]),
    "hide:goat": ("#C9A36B", "", ["grade:fine"]),
    "hide:deer": ("#B9814F", "", ["grade:fine"]),
    "fur:fox": ("#D9722B", "", ["grade:common"]),
    "fur:beaver": ("#6B4A2F", "", ["grade:fine"]),
    "fur:sable": ("#2F2622", "", ["grade:superb"]),
    "fur:ermine": ("#F3F1EA", "", ["grade:superb"]),
    "fish:herring": ("#A9C3D6", "", ["grade:coarse"]),
    "fish:cod": ("#C9C2A8", "", ["grade:common"]),
    "fish:salmon": ("#F08A6B", "", ["grade:fine"]),
    "milk:cow": ("#FFFFFF", "", ["grade:common"]),
    "milk:goat": ("#F1EBD8", "", ["grade:coarse"]),
    "milk:sheep": ("#F7E9B8", "", ["grade:fine"]),
    "spice:pepper": ("#3B3B3B", "", ["grade:fine"]),
    "spice:clove": ("#6B3B2B", "", ["grade:fine"]),
    "spice:nutmeg": ("#A9773F", "", ["grade:fine"]),
    "spice:cinnamon": ("#B5652B", "", ["grade:fine"]),
    "spice:saffron": ("#E8A01E", "", ["grade:superb"]),
    "fat:tallow": ("#EFE6C9", "", ["grade:coarse"]),
    "fat:palm": ("#E0662B", "", ["grade:common"]),
    "fat:olive": ("#A8B84A", "", ["grade:fine"]),
    "fat:beeswax": ("#F2C230", "", ["grade:fine"]),
    "metal:bronze": ("#C58A3D", "", ["grade:coarse"]),
    "metal:iron": ("#7D8790", "", ["grade:common"]),
    "metal:steel": ("#C9D3DC", "", ["grade:fine"]),
    "metal:silver": ("#E3E8EE", "", ["grade:fine"]),
    "metal:gold": ("#F2C230", "", ["grade:superb"]),
    "gem:obsidian": ("#2B2B33", "", ["grade:common", "prized:lakefolk"]),
    "gem:amber": ("#F2A230", "", ["grade:fine", "prized:steelfolk"]),
    "gem:rock-crystal": ("#DDF1F7", "", ["grade:fine"]),
    "gem:jade": ("#4FB58A", "", ["grade:superb", "prized:jadefolk", "prized:lakefolk"]),
    "gem:lapis": ("#2B4FB5", "", ["grade:superb", "prized:steelfolk"]),
    "hue:red": ("#D9381E", "Vermilion: the classic red.", []),
    "hue:black": ("#26262B", "Lampblack: for mourning.", []),
    "decor:blue-and-white": ("#2B4FB5", "Cobalt painted under a clear glaze. What used to be a good of its own.", ["grade:superb"]),
    "decor:enamelled": ("#3FA39B", "", ["grade:superb"]),
    "glass:clear": ("#DDF1F7", "The green taken out with magnesia.", ["grade:fine"]),
    "glaze:blue": ("#2B4FB5", "Cobalt in the glaze.", ["grade:fine"]),
    "spark:blue": ("#3F7FE8", "Verdigris burns blue.", []),
    "spark:bright": ("#FFF2B5", "Camphor burns white and bright.", []),
    "scent:resin": ("#B5703B", "Frankincense and myrrh in the base.", []),
    "scent:sweet": ("#F2B5C9", "Benzoin in the base.", []),
}
for soil, colour in SOIL_COLOURS.items():
    work = next(w for w, soils in SOIL_WORK.items() if soil in soils)
    TAGS["soil:" + soil] = (colour, "", ["work:" + work])

# ---------------------------------------------------------------------------------------
# 4. Varieties authored on raw goods.   good -> namespace, [(id, name, note)]
# ---------------------------------------------------------------------------------------
AUTHORED = {
    "grain": ("grain", [("wheat", "Wheat", "The white loaf."), ("spelt", "Spelt", "Old wheat, hardier."),
                        ("rye", "Rye", "The black loaf of cold country."), ("barley", "Barley", "The brewer's grain."),
                        ("oats", "Oats", "Horse feed, and porridge for the poor."), ("millet", "Millet", "Dry country's grain.")]),
    "grapes": ("grape", [("red", "Red grapes", ""), ("white", "White grapes", ""), ("muscat", "Muscat", "Sweet and perfumed.")]),
    "timber": ("wood", [("pine", "Pine", "Soft, quick, resinous."), ("oak", "Oak", "The shipwright's and the cooper's wood."),
                        ("cedar", "Cedar", "Fragrant and rot-proof."), ("teak", "Teak", "Oily, heavy, and proof against the worm."),
                        ("ebony", "Ebony", "Black, dense and scarce.")]),
    "wool": ("fleece", [("sheep", "Sheep's wool", ""), ("cashmere", "Cashmere", "Combed from mountain goats."),
                        ("alpaca", "Alpaca", "")]),
    "hides": ("hide", [("cattle", "Cattle hides", ""), ("goat", "Goatskins", ""), ("deer", "Deerskins", "")]),
    "furs": ("fur", [("fox", "Fox", ""), ("beaver", "Beaver", "Its barbed underfur felts better than any other."),
                     ("sable", "Sable", ""), ("ermine", "Ermine", "")]),
    "fish": ("fish", [("herring", "Herring", ""), ("cod", "Cod", ""), ("salmon", "Salmon", "")]),
    "milk": ("milk", [("cow", "Cow's milk", ""), ("goat", "Goat's milk", ""), ("sheep", "Ewe's milk", "")]),
    "spice": ("spice", [("pepper", "Pepper", ""), ("clove", "Cloves", ""), ("nutmeg", "Nutmeg", ""), ("cinnamon", "Cinnamon", ""),
                        ("saffron", "Saffron", "")]),
}

# Goods that are no variety of anything, and leave their mark on what is made of them where a slot passes it on.
SIGNATURES = {
    "bronze": "metal:bronze", "ife": "metal:iron", "steel": "metal:steel", "iau": "metal:gold", "iag": "metal:silver",
    "verm": "hue:red", "lampblk": "hue:black",
}

# New core tags for slots that several goods can fill, none a variety of another.
CORE_TAGS = {
    "kind:tool-metal": (["bronze", "ife", "steel"], "A metal that takes an edge. Which one marks the tool."),
    "kind:gun-metal": (["bronze", "ife"], "A metal a cannon can be cast from. Bronze bulges before it bursts; iron is cheaper."),
    "kind:precious-metal": (["iau", "iag"], "What a jeweller works in."),
}
DEAD_TAGS = ["kind:plain-cloth", "kind:golem-heart"]  # slot tags whose goods folded into one good

# ---------------------------------------------------------------------------------------
# 5. Slots rewritten, passing and granting.  (recipe, an acceptor that names the slot) -> ...
# ---------------------------------------------------------------------------------------
REWRITE = {  # the slot's new accepts
    ("r.tools", "steel"): ["#kind:tool-metal"],
    ("r.cannon", "bronze"): ["#kind:gun-metal"],
    ("r.jewel", "iau"): ["#kind:precious-metal"],
    ("r.sealwax", "verm"): ["verm", "lampblk"],
    ("r.lacquer", "verm"): ["verm", "lampblk"],
    ("r.cosm", "dye"): ["coch"],          # the rouge in the pot was cochineal carmine, not a cloth dye
    ("r.izn", "coal"): ["coal", "charcoal"],
}
PASSES = [
    ("r.wine", "grapes"), ("r.planks", "timber"), ("r.hull", "planks"), ("r.ship", "hull"), ("r.lacqw", "planks"), ("r.lacqw", "lacquer"),
    ("r.yarn", "wool"), ("r.woolc", "yarn"), ("r.leather", "hides"), ("r.shoes", "leather"), ("r.hats", "furs"),
    ("r.saltfish", "fish"), ("r.cheese", "milk"), ("r.tools", "#kind:tool-metal"), ("r.cannon", "#kind:gun-metal"),
    ("r.jewel", "#kind:precious-metal"), ("r.sealwax", "verm"), ("r.lacquer", "verm"), ("r.winpane", "glass"),
    ("r.pottery", "glaze"), ("r.golem", "blood"), ("r.golem", "heart"), ("r.clothes", "cloth"), ("r.dyed", "cloth"), ("r.dyed", "dye"),
    ("r.batik", "dye"), ("r.autom", "heart"),
]
GRANTS = {
    ("r.porcel", "zaf"): ["decor:blue-and-white"], ("r.jewel", "enamel"): ["decor:enamelled"],
    ("r.glass", "mang"): ["glass:clear"], ("r.glaze", "zaf"): ["glaze:blue"], ("r.blood", "potg"): ["blood:gilded"],
    ("r.firew", "verdi"): ["spark:blue"], ("r.firew", "camphor"): ["spark:bright"],
    ("r.perfume", "frank"): ["scent:resin"], ("r.perfume", "benzoin"): ["scent:sweet"],
    ("r.ship", "spyglass"): ["fit:spyglass"], ("r.ship", "charts"): ["fit:charts"], ("r.ship", "cannon"): ["fit:armed"],
    ("r.ship", "provis"): ["fit:provisioned"],
}
# Slots that deliberately do NOT pass, though their filler has varieties: the record of restraint.
HELD_BACK = [
    ("r.provis", "bread", "rations do not remember their bread, their fish or their beer"),
    ("r.provis", "saltfish", ""), ("r.provis", "beer", ""),
    ("r.brandy", "wine", "a spirit forgets its grape"), ("r.vinegar", "wine", ""), ("r.tartar", "wine", ""),
    ("r.ash", "timber", "ash is ash"), ("r.charcoal", "timber", ""),
    ("r.batik", "cloth", "a batik is bought for its colour"),
    ("r.book", "leather", "nobody asks whose hide backs a book"), ("r.parch", "hides", ""), ("r.glue", "hides", ""),
    ("r.stone", "heart", "the stone takes a heart and is the stone"),
    ("r.musket", "steel", ""), ("r.clockw", "steel", ""), ("r.giltw", "bronze", ""), ("r.goldthr", "iau", ""),
    ("r.choc", "spice", "spices end at the spice rack"), ("r.theriac", "spice", ""),
    ("r.sails", "canvas", ""), ("r.ship", "cannon", "an armed ship is armed: the cannon's metal stays in the gunroom"),
]

# How varieties show on an icon where one namespace is not the obvious one (the rest are pips). Masks come from the art pass.
LAYERS = {
    "dyed": ["colour"], "court": ["colour"], "batik": ["colour"], "cloth": ["cloth"], "clothes": ["cloth"], "lacqw": ["hue"],
    "ship": ["wood"], "golem": ["soil"], "jewel": ["metal"], "porcel": ["decor"], "pottery": ["glaze"],
}


def main():
    web = copy.deepcopy(json.load(open(ROOT / "tagged-ledger.json")))
    web["id"] = "variety-ledger"
    web["name"] = "The variety ledger"
    web["locked"] = True
    web["note"] = (
        "The tagged ledger taken further, as a stretch test of varieties: the recipes' mistakes mended (the five earths are dug "
        "on a site, not made of hauled soil), goods that were one thing in several flavours folded into one (dye, cloth, golem heart), "
        "many more varieties on raw goods and on what is made of them, property tags the varieties imply and units can stack by, "
        "and a colour for every variety. Locked: New… → a copy of the open web to work on it. Made by tools/make_variety_ledger.py."
    )
    web["layout"] = {}  # so much moves that the lab arranges it afresh (bake)
    palette = web["palette"]
    goods = {g["id"]: g for g in palette["goods"]}
    recipes = {r["id"]: r for r in web["recipes"]}
    log = []

    def slot_of(rid, acceptor):
        found = [s for s in recipes[rid]["inputs"] if acceptor in s["accepts"]]
        assert len(found) == 1, (rid, acceptor, [s["accepts"] for s in recipes[rid]["inputs"]])
        return found[0]

    def drop_good(gid):
        palette["goods"][:] = [g for g in palette["goods"] if g["id"] != gid]
        web["goods"][:] = [g for g in web["goods"] if g != gid]
        goods.pop(gid)

    def drop_recipe(rid):
        web["recipes"][:] = [r for r in web["recipes"] if r["id"] != rid]
        recipes.pop(rid)

    def replace_everywhere(old, new):
        for r in web["recipes"]:
            for s in r["inputs"]:
                if old in s["accepts"]:
                    s["accepts"] = list(dict.fromkeys(new if a == old else a for a in s["accepts"]))
            for o in r["outputs"]:
                if o["good"] == old:
                    o["good"] = new
        for c in web["consumers"]:
            if old in c["accepts"]:
                c["accepts"] = list(dict.fromkeys(new if a == old else a for a in c["accepts"]))

    # ---- 1. mistakes ---------------------------------------------------------------------
    for rid, (name, site, made, note) in SITES.items():
        r = recipes[rid]
        assert [s["accepts"] for s in r["inputs"]] == [["soil"]] and [o["good"] for o in r["outputs"]] == [made], r
        r["name"], r["inputs"], r["site"] = name, [], site
        goods[made]["note"] = note
        log.append(f"site: {name} -> {goods[made]['name']} stands on {', '.join(site)}; it took \"Soil, any of 17\"")
    goods["soil"]["name"] = "Soil"
    goods["soil"]["note"] = ("Earth dug for a golem's body, of whichever of the seventeen soils the ground there is. "
                             "Which soil decides what the golem is good at. The earths that want one soil only "
                             "(clay, sand, ochre, fuller's earth, peat) are dug on their site instead.")
    web["recipes"].insert(web["recipes"].index(recipes["r.salt"]) + 1, {
        "id": "r.salt.sea", "name": "Salt pans", "note": "Sea water let into shallow pans and left to the sun.",
        "element": "earth", "site": ["site:coast"], "inputs": [], "outputs": [{"good": "salt"}]})
    recipes["r.salt.sea"] = web["recipes"][web["recipes"].index(recipes["r.salt"]) + 1]
    log.append("site: a second way to salt, Salt pans, stands on site:coast (a site that is not a soil)")

    cup = recipes["r.litharge"]
    assert [o["good"] for o in cup["outputs"]] == ["litharge"]
    cup["name"] = "Cupellation"
    cup["outputs"] = [{"good": "iag"}, {"good": "litharge"}]
    cup["note"] = "Silver-bearing lead on a bed of bone ash: the lead goes to litharge and the silver is left."
    log.append("fix: Cupellation (lead + bone ash) now yields silver as well as litharge, as Silver's note says")

    for rid in FUEL_SLOTS:
        slot_of(rid, "coal")["accepts"] = ["#kind:fuel"]
        assert not any(o["good"] in [g["id"] for g in palette["goods"] if "kind:fuel" in g["tags"]] for o in recipes[rid]["outputs"])
    log.append(f"fix: {', '.join(FUEL_SLOTS)} said coal where any fuel burns; they take #kind:fuel")

    # From the second-opinion audit of 2026-09-23 (the rest of its findings are in docs/economy-lab.md).
    sugar = recipes["r.sugar"]
    assert [s["accepts"] for s in sugar["inputs"]] == [["cane"]], sugar
    sugar["inputs"] += [{"accepts": ["qlime"]}, {"accepts": ["oxblood"], "optional": True}]
    log.append("fix: Sugar is clarified with lime and blood, as its note says; the recipe now takes quicklime and optional ox blood")
    inc = recipes["r.incense"]
    assert [s["accepts"] for s in inc["inputs"]] == [["#kind:incense-resin"], ["charcoal"], ["camphor"]], inc
    inc["inputs"] = inc["inputs"][:2]
    log.append("fix: Incense listed camphor twice (the resin slot admits it); the extra optional slot goes")
    web["recipes"].insert(web["recipes"].index(recipes["r.varnish"]) + 1, {
        "id": "r.varnish.2", "name": "Spirit varnish", "note": "Shellac dissolved in spirit of wine: the polish on furniture and the finish on a fiddle.",
        "element": "water", "inputs": [{"accepts": ["shellac"]}, {"accepts": ["spirit"]}], "outputs": [{"good": "varnish"}]})
    recipes["r.varnish.2"] = next(r for r in web["recipes"] if r["id"] == "r.varnish.2")
    log.append("fix: Spirit of wine's note called it the solvent behind varnish and no recipe did; Spirit varnish (shellac + spirit) added")

    # The rest of the audit, applied on Maxim's word of 2026-09-23. Kola stays sold raw (so are coconuts and eggs) and oak galls stay a
    # tannin (gall tanning is real; scarcity is a price's business), so two findings are left as they were.
    web["consumers"].append({"id": "c.works", "name": "Works and arms", "note": "Tools wear out, powder is spent, and buildings eat planks and bricks.",
                             "accepts": ["#need:works-and-arms"]})
    log.append("fix: a fifth consumer, Works and arms, buys #need:works-and-arms; twelve goods carried a Need nothing bought")
    leaf = {"id": "betelleaf", "name": "Betel leaf", "note": "The leaf the quid is wrapped in, from a vine grown up the areca palm. (Icon and sign borrowed from the mulberry leaf until it has its own.)",
            "tags": ["stage:raw", "source:farmed", "group:farmed", "nature:mundane", "origin:southeast-asia", "origin:south-asia"],
            "icon": dict(goods["mulb"]["icon"]), "sign": dict(goods["mulb"]["sign"])}
    palette["goods"].insert(palette["goods"].index(goods["areca"]) + 1, leaf)
    web["goods"].insert(web["goods"].index("areca") + 1, "betelleaf")
    goods["betelleaf"] = leaf
    recipes["r.betel"]["inputs"].insert(1, {"accepts": ["betelleaf"]})
    log.append("fix: the betel quid's note named a leaf the recipe lacked; Betel leaf is a good and a slot")
    recipes["r.wine"]["inputs"].append({"accepts": ["eggs"], "optional": True})
    goods["wine"]["note"] = "Grape juice left alone, fined with egg white if it is to be clear. Parent of vinegar, brandy and tartar."
    log.append("fix: eggs were used by nothing though their note promised three uses; wine takes them, optional, for fining")
    goods["autom"]["note"] = "A golem's clockwork cousin: no blood, no soil, and only as much wit as the cheap heart you give it."
    goods["cosm"]["note"] = "Kohl from stibnite, ceruse from white lead, pearl white from bismuth dissolved in aqua fortis and thrown down with water."
    log.append("fix: the automaton's note allowed for its heart slot and the cosmetics' note explains its aqua fortis")
    goods["coconut"]["tags"].remove("kind:plant-fibre")
    slot_of("r.rope", "#kind:plant-fibre")["accepts"] = ["#kind:plant-fibre", "coconut"]
    goods["paper"]["note"] = "Rag or bamboo paper, sized with alum and glue."
    log.append("fix: coir went into the paper vat as a plant fibre; coconut is off the tag and rope names it beside the tag")
    for slot in recipes["r.pigm"]["inputs"]:
        slot["optional"] = next(iter(slot["accepts"])) != "ochre"
    goods["pigm"]["note"] = "The colourman's stock: ochre from the pit, and whatever rarer hue the trade brings, each with a different mine, plant or poison behind it."
    log.append("fix: the pigments recipe made the rare hues mandatory and ochre optional; now ochre is the base and the rest are choices")
    goods["harts"]["tags"].remove("kind:alkali")
    log.append("fix: hartshorn, the volatile alkali, no longer counts as the vat's alkali; kind:alkali is the fixed alkalis")

    # ---- 2. folds ------------------------------------------------------------------------
    at = {g["id"]: i for i, g in enumerate(palette["goods"])}
    for new, (name, note, tags, members, donor) in FOLDS.items():
        good = {"id": new, "name": name, "note": note, "tags": list(tags), "icon": goods[donor].get("icon"), "sign": goods[donor].get("sign")}
        first = min(at[m] for m in members)
        palette["goods"].insert(first, good)
        web["goods"].insert(web["goods"].index(next(iter(members))), new)
        goods[new] = good
        for member, (tag, rid, rname) in members.items():
            r = recipes[rid]
            assert [o["good"] for o in r["outputs"]] == [member], r
            r["name"] = rname
            granted = r["inputs"][0].setdefault("grants", [])
            if tag not in granted:
                granted.append(tag)
            replace_everywhere(member, new)
            drop_good(member)
        log.append(f"fold: {', '.join(members)} -> {name} ({len(members)} recipes, each granting its variety)")
        at = {g["id"]: i for i, g in enumerate(palette["goods"])}

    web["recipes"].insert(web["recipes"].index(recipes["r.weldd"]) + 1, {
        "id": "r.dye.green", "name": "Green vat", "note": "Weld over indigo: the only good green.", "element": "water",
        "inputs": [{"accepts": ["indigo"], "grants": ["colour:green"]}, {"accepts": ["weld"]}, {"accepts": ["alum"]}],
        "outputs": [{"good": "dye"}]})
    recipes["r.dye.green"] = next(r for r in web["recipes"] if r["id"] == "r.dye.green")

    for gone, into, rid in ABSORBED:
        drop_recipe(rid)
        replace_everywhere(gone, into)
        drop_good(gone)
        log.append(f"fold: {gone} was a variety of {goods[into]['name']} all along; its recipe {rid} goes")
    goods["dyed"]["name"] = "Dyed cloth"
    goods["dyed"]["note"] = "Cloth in a fast colour; alum is the mordant that makes it stay. Scarlet is the brightest money can buy."
    goods["porcel"]["note"] += " Painted with cobalt under the glaze it is blue-and-white, the most copied ceramics in history."

    # ---- recipes reshaped by the folds ----------------------------------------------------
    for slot in recipes["r.dyed"]["inputs"] + recipes["r.clothes"]["inputs"] + recipes["r.golem"]["inputs"]:
        slot["accepts"] = [{"#kind:dye": "dye", "#kind:plain-cloth": "cloth", "#kind:golem-heart": "heart"}.get(a, a) for a in slot["accepts"]]
    court = recipes["r.court"]
    assert sorted(a for s in court["inputs"] for a in s["accepts"]) == ["cloth", "dyed", "goldthr"], court
    court["inputs"] = [{"accepts": ["dyed"], "passes": True}, {"accepts": ["goldthr"]},
                       {"accepts": ["furs"], "optional": True, "passes": True}]
    goods["court"]["note"] = "Dyed cloth and gold thread, trimmed with fur if the wearer can. Silk in scarlet with ermine is the top of it. Worn twice, talked about for a season."
    log.append("reshape: Court dress takes any dyed cloth (passing cloth and colour) + gold thread + optional fur trim: "
               "it demanded silk and scarlet, a gate; now the gate is a grade")
    autom = slot_of("r.autom", "heart")
    assert autom.get("optional")

    # ---- 3. namespaces and tags ------------------------------------------------------------
    spaces = {n["id"]: n for n in palette["tagNamespaces"]}
    for ns, note in NAMESPACE_NOTES.items():
        spaces[ns]["note"] = note
    for ns, note in VARIETY_NAMESPACES.items():
        palette["tagNamespaces"].append({"id": ns, "note": note, "role": "variety"})
    for ns, (note, combine) in PROPERTY_NAMESPACES.items():
        entry = {"id": ns, "note": note, "role": "property"}
        if combine:
            entry["combine"] = combine
        palette["tagNamespaces"].append(entry)
    palette["tagNamespaces"].append({"id": SITE_NAMESPACE[0], "note": SITE_NAMESPACE[1]})
    palette["tags"].append({"id": "site:coast", "note": "By the sea.", "colour": "#3F7FBF"})

    defs = {t["id"]: t for t in palette["tags"]}
    for tag, (colour, note, implies) in TAGS.items():
        entry = defs.get(tag)
        if entry is None:
            entry = {"id": tag, "note": note}
            palette["tags"].append(entry)
            defs[tag] = entry
        elif note:
            entry["note"] = note
        entry["colour"] = colour
        if implies:
            entry["implies"] = list(implies)
    for tag, (carriers, note) in CORE_TAGS.items():
        palette["tags"].append({"id": tag, "note": note})
        for gid in carriers:
            goods[gid]["tags"].append(tag)
    for tag in DEAD_TAGS:
        palette["tags"][:] = [t for t in palette["tags"] if t["id"] != tag]
        for g in palette["goods"]:
            if tag in g["tags"]:
                g["tags"].remove(tag)

    # ---- 4. varieties ------------------------------------------------------------------------
    for gid, (ns, varieties) in AUTHORED.items():
        goods[gid]["varieties"] = [{"id": vid, "name": vname, "note": vnote, "tags": [f"{ns}:{vid}"]} for vid, vname, vnote in varieties]
    goods["grain"]["note"] = "Wheat, spelt, rye, barley, oats or millet, as the climate allows. Bread and beer both start here."
    for gid, tag in SIGNATURES.items():
        goods[gid]["tags"].append(tag)

    # ---- 5. slots ----------------------------------------------------------------------------
    for (rid, acceptor), accepts in REWRITE.items():
        slot_of(rid, acceptor)["accepts"] = list(accepts)
    for rid, acceptor in PASSES:
        slot_of(rid, acceptor)["passes"] = True
    for (rid, acceptor), tags in GRANTS.items():
        slot = slot_of(rid, acceptor)
        assert slot.get("optional"), (rid, acceptor)
        slot["grants"] = list(tags)
    for rid, acceptor, _ in HELD_BACK:
        assert not slot_of(rid, acceptor).get("passes"), (rid, acceptor)
    goods["tools"]["note"] = "An edge of bronze, iron or steel on a wooden handle. Every other workshop waits on these."
    goods["jewel"]["note"] = "Gold or silver soldered with borax, set with whatever stone the wearer's culture prizes."
    goods["sealwax"]["note"] = "Snap, melt, press. Vermilion for letters, lampblack for mourning."

    for gid, spaces_shown in LAYERS.items():
        goods[gid]["layers"] = [{"match": ns} for ns in spaces_shown]

    stamp_art(web, goods, log)

    # ---- checks --------------------------------------------------------------------------------
    ids = set(web["goods"])
    assert ids == set(goods), ids ^ set(goods)
    carried = {t for g in palette["goods"] for t in g["tags"]}
    roles = {n["id"]: n.get("role") for n in palette["tagNamespaces"]}
    defs = {t["id"]: t for t in palette["tags"]}
    used_variety = set()
    for r in web["recipes"]:
        made = {o["good"] for o in r["outputs"]}
        assert made <= ids, (r["id"], made - ids)
        for s in r["inputs"]:
            for a in s["accepts"]:
                if a.startswith("#"):
                    assert a[1:] in carried, (r["id"], a)
                    assert roles.get(a[1:].split(":")[0]) != "variety", (r["id"], a, "a variety tag gates a slot")
                    assert not made & {g["id"] for g in palette["goods"] if a[1:] in g["tags"]}, (r["id"], a, "admits its own output")
                else:
                    assert a in ids, (r["id"], a)
            used_variety.update(s.get("grants", []))
        for t in r.get("site", []):
            assert t in defs, (r["id"], t)
    for g in palette["goods"]:
        used_variety.update(t for t in g["tags"] if roles.get(t.split(":")[0]) == "variety")
        used_variety.update(t for v in g.get("varieties") or [] for t in v["tags"])
    for t in sorted(used_variety):
        assert roles.get(t.split(":")[0]) == "variety", t
        assert t in defs and defs[t].get("colour"), f"{t} has no colour"
        for p in defs[t].get("implies", []):
            assert roles.get(p.split(":")[0]) == "property" and p in defs, (t, p)
    for c in web["consumers"]:
        for a in c["accepts"]:
            assert (a[1:] in carried) if a.startswith("#") else (a in ids), (c["id"], a)

    out = ROOT / "variety-ledger.json"
    json.dump(web, open(out, "w"), indent=2, ensure_ascii=False)
    open(out, "a").write("\n")
    print("\n".join(log))
    print(f"\n{len(palette['goods'])} goods (the tagged ledger had 286), {len(web['recipes'])} recipes (198), "
          f"{sum(1 for r in web['recipes'] if r.get('site'))} with a site, "
          f"{sum(1 for r in web['recipes'] for s in r['inputs'] if s.get('passes'))} passing slots, "
          f"{sum(1 for r in web['recipes'] for s in r['inputs'] if s.get('grants'))} granting slots, "
          f"{len(HELD_BACK)} held back on purpose, {len(used_variety)} variety tags in "
          f"{len({t.split(':')[0] for t in used_variety})} namespaces, "
          f"{sum(1 for t in palette['tags'] if roles.get(t['id'].split(':')[0]) == 'property')} property tags.")
    print(f"wrote {out}")


def stamp_art(web, goods, log):
    """The art passes leave an index of what they drew; whatever is there is stamped in."""
    palette = web["palette"]
    atlases = {a["id"] for a in palette["atlases"]}

    def atlas(entry):
        if entry["id"] not in atlases:
            palette["atlases"].append(entry)
            atlases.add(entry["id"])

    icons = HERE / "variety_icons.json"
    if icons.exists():
        art = json.load(open(icons))
        for entry in art.get("atlases", []):
            atlas(entry)
        for gid, cell in art.get("icons", {}).items():
            if gid in goods:
                goods[gid]["icon"] = {"atlas": art["iconAtlas"], "index": cell}
        for gid, layers in art.get("layers", {}).items():
            if gid in goods:
                goods[gid]["layers"] = [{"match": l["match"], **({"mask": {"atlas": art["maskAtlas"], "index": l["mask"]}} if l.get("mask") is not None else {})}
                                        for l in layers]
        log.append(f"art: {len(art.get('icons', {}))} icons and {sum(len(l) for l in art.get('layers', {}).values())} layers from {icons.name}")

    signs = HERE / "tag_signs.json"
    if signs.exists():
        art = json.load(open(signs))
        for entry in art.get("atlases", []):
            atlas(entry)
        ref = lambda cell: {"atlas": art["atlas"], "index": cell}
        defs = {t["id"]: t for t in palette["tags"]}
        for tag, cell in art.get("tags", {}).items():
            if tag not in defs:
                defs[tag] = {"id": tag, "note": ""}
                palette["tags"].append(defs[tag])
            defs[tag]["sign"] = ref(cell)
        for ns in palette["tagNamespaces"]:
            if ns["id"] in art.get("namespaces", {}):
                ns["sign"] = ref(art["namespaces"][ns["id"]])
        for gid, cell in art.get("goods", {}).items():
            if gid in goods:
                goods[gid]["sign"] = ref(cell)
        log.append(f"art: {len(art.get('tags', {}))} tag symbols, {len(art.get('namespaces', {}))} namespace symbols and "
                   f"{len(art.get('goods', {}))} good signs from {signs.name}")


if __name__ == "__main__":
    main()
