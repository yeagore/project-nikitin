# The economy lab

`scenes/dev/economy_lab.tscn` (F6 in the editor) is an editor of production webs:
goods as draggable nodes, recipes between them, links drawn by hand or implied by
a tag, and consumer blobs that mark the consumables. It is the first stage of an
economy constructor. Amounts, proportions and time are not modelled yet; the
point of this stage is the lab's shape and the principle of the data.

This file is the manual, the data format, and the findings so far. Written
plainly on purpose: it is meant to be read by Maxim as much as by the model.

## The idea

- A **web** is one version of the economy, whole in one file. It has its own
  **palette** (its goods, its tags, its tag namespaces), a canvas with some of
  those goods on it, the **recipes** that join them, and the **consumers** they
  lead to. **Nothing is shared between webs.** Rename a good, retag it, invent
  Unobtanium: it happens in the web you are in and nowhere else. Goods travel
  from one web to another only by **Import**, which copies them.
- A **recipe** has input **slots** and outputs. Every slot must be filled for the
  recipe to run, unless the slot is optional. A slot lists what it **accepts**,
  and any one of those fills it:
  - a good, by id (`icu`);
  - a **tag**, written with a hash (`#kind:golem-heart`), which admits every good
    that carries it. Give a new heart that tag and it fits the golem with no
    recipe edited. Links that a tag brings are drawn by the lab on its own.
  So "iron from ironstone with coal *or* charcoal" is one slot accepting two
  goods, or better one slot accepting `#kind:fuel`; and a good made in two wholly
  different ways has two recipes.
- A **consumer** is a sink. A good that leads to one is a consumable (eaten,
  drunk, worn out, used up); a good that leads nowhere is a durable or a work. A
  consumer accepts goods and tags the way a slot does: the Food consumer accepts
  `#need:food`.
- **Tags have namespaces, and namespaces have roles.** The part before the colon
  (`kind` in `kind:metal`) is the namespace. You can make namespaces, describe
  them, rename them, and give each a role:
  - **core**: what a good *is*. Core tags are what slots and consumers are meant
    to accept, so they decide a good's place in the web (`kind:fuel`, `need:food`).
  - **variety**: what is *particular* about it (`heart:arsenic`, `grain:rye`).
    Variety tags travel: see below.
  - no role: the tag only describes (`stage:raw`, `origin:europe`).
- **Varieties.** One node can stand for many kinds of the same good.
  - A good can carry variety tags of its own (the Arsenic heart carries
    `heart:arsenic`).
  - A good can have **authored varieties**: rye, wheat and barley are varieties of
    Grain; the seventeen soils are varieties of Soil. One node, the same slots;
    each variety adds its own tags.
  - A slot can be marked **passes variety** (a `»` on the recipe node). Whatever
    fills such a slot stamps its variety tags on what the recipe makes, and those
    travel on through the next passing slot. Rye grain makes rye flour makes rye
    bread, through one Flour node and one Bread node. A golem recipe with a
    three-heart slot that passes variety on and four optional fittings makes 48
    different golems, and nobody writes any of them down: the lab derives them and shows the count on
    the node and the list in the inspector. Same inputs, same variety, always.
  - A slot can also **grant** tags of its own (a `+` on the recipe node): whatever
    fills it, the output gets them. This is for when the effect belongs to the
    combination and not to the ingredient. Clockwork in the golem's hands slot
    makes a golem with `fit:clockwork-hands`; Clockwork itself stays plain
    clockwork for the clockmaker, where a tag like that would be nonsense.
  - Nothing reads variety tags yet. They are where prices ("rye sells better where
    boreal goods are in fashion") and uses ("a sand-bodied golem bears heat")
    will attach in the next stage.
- **Every recipe has an element**, in the spirit of alchemy. Two questions sort a
  craft: does it put things together or take a thing apart, and does it do so
  with violence or with patience. **Fire** is violent synthesis (smelting,
  burning, firing). **Wind** is violent analysis (milling, crushing, distilling).
  **Water** is gentle synthesis (mixing, assembling, weaving). **Earth** is gentle
  analysis (fermenting, curing, spinning wool into thread). **Quintessence** is
  pure magic, like animating a golem. It is a classification by feel, not a law,
  and nothing reads it yet; a web does well to keep the four in rough balance
  with a little quintessence on top, and the web view says how it stands. The
  element's icon sits before the recipe's title, and leads its **formula**: the
  recipe written in the goods' alchemical signs, which is where all this is
  meant to end up.
- A web can be **locked**: a reference copy that the lab will open, cut from and
  import from, and will not change or bin. To work on one, make a copy of it.
- Nothing about a good's place in the web is stored. A **source** is a good
  nothing in this web makes, a **final** good is one no recipe here uses, and the
  rest are intermediates. The lab reads these off the links every time something
  changes, along with each good's depth (steps from the ground), the hubs (six
  recipes or more use it), the varieties, and the issues.

## What ships

| File | What |
|---|---|
| `resources/economy/webs/starter.json` | A small one to learn on: the golem with its three hearts and bronze joints, bread and beer. 48 goods on the canvas, 29 recipes, two consumers; the full 286-good palette, so there is plenty to drag in. Opens first. |
| `resources/economy/webs/full-ledger.json` | Everything from the 21 September 2026 brainstorm: 286 goods, 197 recipes, four consumers (Food, Intoxicants and physic, Clothing, Wares). Big: a map to cut from. |
| `resources/economy/webs/full-ledger-reference.json` | A **locked** copy of the full ledger, so the big web can always be got back however the working copy is treated. |
| `resources/economy/webs/tagged-ledger.json` | The full ledger reworked as a worked example: its either-or slots turned into tag slots, and varieties switched on (see the findings below). Its own palette, so the full ledger is untouched. |
| `resources/economy/sprites/icons.png`, `signs.png` | 16 px cells, 16 columns. `sprites/custom/` takes PNGs imported through the lab, for a good's icon (`<id>.png`) and for its sign (`<id>.sign.png`). The sheets are files shared by every web; each palette names the sheets it uses. |
| `resources/economy/sprites/elements.png` | The five element icons, 16 px, in a row: fire, wind, water, earth, quintessence. They belong to the system, not to a palette. `tools/make_element_icons.py` drew them. |
| `tools/import_economy_export.py`, `tools/make_tagged_ledger.py` | The one-off converter from the chat export, and the script that derived the tagged ledger. Kept as the record of how the data was made; running either again overwrites lab edits to the webs it writes. |
| `tools/recipe_elements.json`, `tools/apply_recipe_elements.py` | The classification of every recipe by element (made by feel on 2026-09-22, to be overruled in the lab), and the script that stamped it into the webs in place and made the locked reference copy. |

The import changed three things in the data. The computed `trait:hub` tag was
dropped (the lab computes hubs). The golem's heart slot, which listed the
antimony heart with the arsenic and bismuth hearts as alternatives, became one
slot accepting `#kind:golem-heart`, a new tag on the three hearts. The `stage`,
`group` and `need` fields were dropped because the tags `stage:`, `group:` and
`need:` already said the same (checked for all 286). Goods tagged
`need:works-and-arms` have no consumer: they are durables and works. Not
imported: `pixels.json` (the sprites as text), `sign_vocab.json`, `soils.json`
(though the seventeen soil names are in the tagged ledger as varieties of Soil).

## Where your webs live, and how they reach the other machine

A web is a file in the repository: `resources/economy/webs/<id>.json`, on the
machine you made it on. Autosave writes it a moment after every change. It reaches
the other machine, and is backed up, the way the code is: **commit and push, then
pull**. That is the hygienic cloud: one history, diffs you can read (a moved node
is one changed line), and any web can be brought back to any earlier day. Nothing
else is needed, and nothing else would be as safe. What is *not* in the repository
is per machine on purpose: where each web was scrolled and zoomed, the last web
opened, the interface scale and the toggles (`user://economy_lab.cfg`).

## Using it

Open `scenes/dev/economy_lab.tscn` in the editor and press F6. F1 in the lab
shows the same gestures as below.

**The window.** The lab fits whatever window it gets: **UI auto** draws the
interface at the screen's own scale, brought down until everything fits, and the
top bar wraps onto a second line rather than run off the edge. Cmd/Ctrl with `+`
and `-` make the interface larger and smaller from the keys, and with `0` it goes
back to Auto; each machine remembers its own. With a window of its own the lab
opens maximised, and **Full screen** (F11 or Alt+Enter) is in the top bar.
**If the lab appears inside the editor, in the Game tab,** it cannot be maximised
or go full screen from within, because the editor owns that window: in the Game
tab's ⋮ menu untick *Embed Game on Next Play* and run again, and it gets a window
of its own. The status line says so when that is the case.

**The canvas** (middle)
- Drag a node to move it. Drag on empty canvas for a rubber band. Wheel zooms,
  middle-drag pans, F frames the whole web. The minimap is bottom right.
- Drag from a good's right port to a recipe's left port: the good goes into that
  slot. Drop it on a slot that already has something and the slot accepts either.
  Drop it on the last row, `+ input`, to make a new slot.
- Drag from a recipe's right port to a good: the recipe makes that good. The
  last row, `output +`, adds a second output (a by-product).
- Drag from a good to a consumer: it is a consumable.
- Cut a link by dragging it off its left end, or right-click it. A link that a
  tag brings cannot be cut: take the tag off the good, or change the slot.
- Let go of a link over empty canvas for a menu: a new recipe, a good from the
  palette, a new good, a tag.
- Right-click the canvas: add a good from the palette, new good, new recipe, new
  consumer. Right-click a node: a recipe that makes it or uses it, remove.
- Delete (Backspace on a Mac) removes the selected nodes from the canvas. Goods
  stay in the palette.
- Node colours are the good's stage (raw brown, processed blue, compound teal,
  magistery purple, assembly orange, finished olive). Link colours: white a
  required input, grey optional, amber by tag, blue what a recipe makes, green
  consumed. A `»` on a slot: it passes variety on. A good's line says how many
  varieties the web can make of it.

**The top bar**
- *Web*: which web is open. *New…* makes one with its own palette: **a clean
  palette** on an empty canvas; this web's palette on an empty canvas; a copy of
  this web; or **the selected goods and everything upstream of them**, with a lean
  palette (only what the cut uses) or the whole one. That last is how a vertical
  slice is cut from the full ledger: select the final goods you want, press New….
  *Bin…* moves the web's file to the system trash.
- *Save*, and *Autosave* (on by default). *Undo* / *Redo*: a hundred steps; typing
  in one field is one step.
- *Arrange* lays the whole web out afresh, sources left, consumers right. *Trace*
  keeps lit what the selected node is made of and what is made with it, and fades
  the rest, wires and all. *Find…* goes to a node by name.
- *Issues* lists what the analysis found: a recipe that makes nothing, a slot
  that accepts nothing, a tag nothing in the web carries, a loose good, a loop.

**The left dock** is the web's palette. Filter by name or `#tag`, drag goods onto
the canvas or double-click. *With its chain from* brings a good's recipes and
everything upstream from another web as that web has them. **Import…** brings
goods from another web's palette into this one's: some, or the whole palette,
with or without their chains onto the canvas. The **Tags** tab lists the tags by
namespace; select a tag to edit its note, rename or delete it; select a namespace
to describe it, set its role, rename it, or add a tag in it; *New namespace…*
makes one. All of it stays in this web.

**The right dock** edits what is selected. A good: name, description, tags, its
varieties (what the web can make of it, and the ones authored by hand), its icon
and its sign (each from the sheet, or from a PNG of your own), what makes it and
uses it, delete from the palette. A recipe: its label, its element, its formula
in signs, its slots (what each accepts, optional or not, passes variety or not
and what each filler would pass, what the slot itself grants), its outputs. A
consumer: its name and what it accepts. Nothing selected: the web's name and
note, its numbers, hubs, issues, and the legend.

## Findings: either-or slots into tags (the tagged ledger)

The full ledger had 27 slots of the form "x or y". In `tagged-ledger`:

- **24 became one tag, 1 became a tag and a good, 1 was really two recipes, 1 stayed a list.**
  22 tags did it, and only **3 of them already existed** (`kind:fuel`, `kind:dye`,
  `kind:alkali`). The brainstorm's tags describe goods; they were not written to
  be slots, so most were too wide to use: `kind:resin` holds frankincense and
  amber as well as what makes varnish, `kind:fibre` holds wool and silk as well as
  what makes paper, `kind:cloth` holds silk and canvas as well as what everyday
  clothes are cut from.
- **Eleven new tags are real families that were waiting for a name**, and will
  grow: vitriol, tannin (oak galls joined at once), plant-fibre (one tag now
  serves paper *and* rope), varnish-resin (varnish and sealing wax), fat,
  plain-cloth, gemstone, incense-resin, red-dyestuff, scarlet-insect,
  writing-surface.
- **Eight are several sources of one substance, the list under a name:**
  ammonia-source, lime-source, soda-source, chloride, tar-stock, soot-fuel,
  lens-stock, candle-stock. Honest only if you expect more sources; otherwise the
  list said the same thing. They do buy something: a new source needs the tag and
  no recipe edited.
- **A tag a product shares with its ingredients cannot be its slot's tag.**
  Jewellery carries `kind:gem` and Incense carries `kind:incense`, so those
  recipes would have fed on their own output. A slot tag has to name the
  *ingredient*, not the theme. The script checks for this and so does the self-test.
- **Tags widen.** `#kind:fuel` lets iron be smelted with peat or timber, and
  bricks fired with charcoal. `#kind:dye` lets fast-dyed cloth take scarlet dye,
  which overlaps the Scarlet cloth recipe. Each is one good's tag away from being
  undone, but each is a design decision the list did not force.
- **One slot mixes a tag and a good:** the indigo vat takes any alkali, or stale
  urine, which should not be called an alkali for one recipe's sake. The model
  allows that, and it reads well.
- **One slot stayed a list:** spectacle frames of horn or brass. Two materials
  with nothing in common but this use.
- **One slot was wrong in the export.** Spirit of hartshorn is made "from distilled
  horn, or from sal ammoniac *and* lime"; the export flattened that into three
  alternatives of one slot, which says sal ammoniac alone will do. It is now two
  recipes (`r.harts`, `r.harts.2`).
- **Varieties can fold goods.** With dye colour passing through, Scarlet cloth is
  just the scarlet variety of Fast-dyed cloth; its own good and recipe could go.
  The three hearts could likewise be one Heart whose recipe takes `#heart-metal`.
  Neither is done; both are the kind of cut the variety system is for.

The tagged ledger's varieties, as a worked example: the golem takes its heart
(three, passed on), four optional fittings (granted by the recipe's own slots) and
its body's soil (seventeen, passed on from the clay up) and comes out in about 816
varieties from one recipe; bread, beer, soap and
candles in three each (grain, fat); clothes in three cloths, dyed cloth in five
colours, jewellery in five stones.

## An open question: how varieties stack in play

One recipe can make hundreds of varieties. A warehouse that kept a stack for each
would bury the player, and one that hid them would make them pointless. Thoughts,
none of them built:

- **Stack by what matters, not by what happened.** A variety tag is history ("made
  with rye", "bodied in sand"). What the player and the market care about is what
  that history *does*: sells better under a boreal fashion, bears heat. If every
  reader of variety tags (a fashion, a use, a recipe that asks for a particular
  variety) is a short list of **effects**, then units stack by good and effect
  signature: seventeen soils collapse into the four or five kinds of golem anyone
  can tell apart. Tags that nothing reads are dropped at the workshop door, or
  kept as flavour on the item. The lab can already count varieties; the number
  worth watching will be the *distinguishable* ones, once readers exist.
- **Bulk goods blend, made things stay themselves.** Grain, flour, bread, cloth
  are fungible: a stack of bread can be "two thirds wheat, one third rye", shown
  as one row with a composition bar, priced by its mix. Golems, jewels, ships are
  few and dear: each unit keeps its variety, like equipment, and five golems with
  five names overwhelm nobody.
- **Order by intent.** The player asks for "a golem for the desert mine" or "bread,
  any", not for a stock-keeping unit. Orders and trade routes default to the good,
  and narrow to an effect or a variety only where the player chooses to care.
- **One row per good, opened on demand.** Every list shows the good; the varieties
  are inside it. Opaque is when a difference matters and is hidden; overwhelming
  is when it does not matter and is shown. The effect table is what decides which.
- **Keep most slots from passing variety on**, in the webs themselves. Every
  passing slot multiplies the count by whatever can fill it, down the whole chain:
  if bread remembers its grain and its salt, and ship's provisions remember their
  bread, fish and beer, provisions come in dozens of kinds that mean nothing. A
  slot should pass only where the choice shows in the product or something
  downstream reads it, and chains should forget at the point where identity
  dissolves (provisions are provisions). A good that shows hundreds of varieties
  on the canvas is a prompt to look at which slots pass.

## From a shell

```
godot --path . --headless scenes/dev/economy_lab.tscn -- selftest
godot --path . scenes/dev/economy_lab.tscn -- shot web=tagged-ledger select=r.golem zoom=1 out=/tmp/lab.png
godot --path . --headless scenes/dev/economy_lab.tscn -- bake
```

`selftest` copies `resources/economy/` to a scratch folder, makes there the
changes a hand would make through the handlers the mouse calls, checks that the
web, the canvas and undo agree after each, and exits 1 if any check failed. It is
the regression gate for the lab and the model and takes a few seconds. `shot`
opens a window, draws, saves a PNG and quits; it never writes to the data (`web=`
which web, `select=` a node key, `zoom=`, `out=` the file, `show=help|find|newweb|newgood|issues`
a pop-up, `tags` or `tags=ns:kind` the Tags tab, `import` the Import dialog,
`window=1150x700` a window of that size with the interface fitted to it). `bake`
arranges every web that has no layout and rewrites every file through the lab's
own writer; run it after either script in `tools/`.

## The files

Plain JSON, two-space indent, written whole to a sibling file and moved into
place. Every object keeps fields it does not know, so a file from a newer lab
survives an older one. Ids are lowercase letters, digits and hyphens. A good's id
never changes; a recipe's id starts with `r.` and a consumer's with `c.`, so the
three kinds never collide and a layout key needs no prefix.

`webs/<id>.json` (format 2)
```json
{
  "format": 2,
  "id": "tagged-ledger", "name": "…", "note": "…",
  "locked": true,
  "palette": {
    "atlases": [ { "id": "icons", "file": "sprites/icons.png", "cell": 16, "columns": 16 } ],
    "tagNamespaces": [
      { "id": "kind", "note": "What sort of stuff it is…", "role": "core" },
      { "id": "heart", "note": "Which heart a golem was given.", "role": "variety" }
    ],
    "tags": [ { "id": "kind:golem-heart", "note": "Fits the heart slot of a golem." } ],
    "goods": [
      { "id": "h2", "name": "Antimony heart", "note": "…",
        "tags": [ "stage:assembly", "kind:golem-heart", "heart:antimony" ],
        "icon": { "atlas": "icons", "index": 229 },
        "sign": { "atlas": "signs", "index": 229, "source": "compound", "reading": "…", "parts": [ "HT", "Sb" ] } },
      { "id": "grain", "name": "Grain", "note": "…", "tags": [ "stage:raw", "kind:food" ],
        "varieties": [ { "id": "rye", "name": "Rye", "note": "…", "tags": [ "grain:rye" ] } ] }
    ]
  },
  "goods": [ "h2", "grain", "golem" ],
  "recipes": [
    { "id": "r.golem", "name": "", "note": "", "element": "qe",
      "inputs": [
        { "accepts": [ "blood" ] },
        { "accepts": [ "#kind:golem-heart" ], "passes": true },
        { "accepts": [ "bronze" ], "optional": true, "grants": [ "fit:bronze-joints" ] }
      ],
      "outputs": [ { "good": "golem" } ] }
  ],
  "consumers": [ { "id": "c.food", "name": "Food", "note": "", "accepts": [ "#need:food" ] } ],
  "layout": { "h2": [0, 0], "r.golem": [1480, 310] }
}
```
`locked` is written only when true. `element` is `fire`, `wind`, `water`, `earth`
or `qe`. `goods` lists which of the palette's goods are on the canvas. A sprite is a cell
of an atlas (`atlas`, `index`) or a PNG of its own (`"file": "sprites/custom/h4.png"`).
A tag may be in use without an entry under `tags`; the entry is where its note
lives. Inputs and outputs are objects so that amounts can join them
(`"amount": 2`) without breaking a file; a recipe will likewise take `time`, a
building and labour.

## The code

`scripts/economy/` is the model, with no Godot types in it, so the game can load a
web as it is; `scripts/dev/EconomyLab*.cs` is the lab. Each folder has a
`CLAUDE.md` with its map and its rules. In one sentence: the web is the truth and
the canvas follows it; a gesture becomes a change to the web through
`EconomyLab.Change`, the web is analysed again, and the canvas is brought into line.

## What is next

Not done here, in rough order of how soon they will be wanted: amounts on slots
and outputs, and a recipe's time, building and labour; rates at the sources and
the consumers, and a balance sheet per web; variety tags read by consumers (what
a fashion pays for) and by uses (what a variety is good at), which is also what
will decide how varieties stack (above); folding near-duplicate goods into
varieties; whole chains written out as formulae in signs, with signs for tags; by-products
used in earnest (the second output port is there); webs compared side by side; a
pixel editor for the sprites; frames to group a chain on the canvas; the soil and
climate a raw variety needs.
