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
  - **A variety never gates.** No slot and no consumer accepts a variety tag; the
    lab notes it as an issue if one does. If a recipe would take one soil and
    refuse another, it is asking for a *place* (a site, below), for a core tag, or
    for a good of its own.
- **Property tags** are what variety tags *imply*, and what units **stack** by.
  A third role for a namespace. `soil:murkearth` implies `work:water`;
  `grain:oats` implies `grade:coarse`; `gem:jade` implies `grade:superb` and
  `prized:jadefolk`. A unit's properties are the union of what its variety tags
  imply, except in a namespace marked as a **scale** (`combine: lowest`), where
  it keeps the lowest: silk in a common dye is common cloth. Prices, fashions and
  uses will read property tags, not variety tags; so a warehouse can show one
  row per good and property, and the seventeen soils of a golem come to five
  kinds of work. The inspector shows both counts, and lets you split a good's
  stack by one namespace at a time, which is how the player will see it.
- **A site** is where a recipe has to stand: tags of the ground (`soil:murkearth`)
  or of the place (`site:coast`), any one of which will do. Nothing is hauled or
  used up. Peat is cut where the ground is murkearth, so its recipe has a site
  and no slot; salt pans stand on the coast. A recipe with a site and no inputs
  is an extraction, and the lab does not complain that it takes nothing.
- **Shape by good, hue by variety.** Every good has an icon of its own shape. A
  variety tag has a **colour**, and the lab recolours the good's icon with it: a
  plain stack shows the plain icon; a stack split by heart shows three icons in
  three heart colours. A good can name **layers**: which part of its icon each
  variety namespace tints (the golem's heart, its eyes, its body), through a
  mask on the `masks` sheet; with no layers the first variety tag tints the
  whole icon. Further coloured tags show as small pips down the icon's right
  edge, four at most.
- **Tags have symbols.** Each namespace has a symbol on the `tags` sheet, in the
  signs' own vocabulary of marks, tinted with the tag's colour; a tag whose
  colour alone would not tell it apart has a symbol of its own (the three hearts
  carry the metal's mark). A recipe's formula row writes tag slots and sites
  with them, so a chain can be read in signs end to end.
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
- **Amounts, time and rates** (stage two, begun 2026-09-23). Every slot and
  every output has an **amount** per run (one when unsaid); every recipe a
  **time** in days per run (one when unsaid). The land gives a **supply** at the
  sources, in units a day per good. The web has a population (**heads**), and
  each consumer says what one head **wants** a day of whatever it accepts. From
  these the lab reads a **balance**: what flows where in a day. A workshop runs
  one batch at a time, so runs a day times days is workshops busy.
- **The balance** is two passes over the web with its loops cut. First the wants
  are *pulled* back from the consumers to the ground: each consumer asks for its
  people's due, each recipe asks its slots for enough runs to make what is asked
  of it, and a slot or a consumer that accepts several goods asks each for an
  equal share. Then what is there is *pushed* forward: a good that cannot cover
  what is asked of it is rationed among its askers in proportion, and a recipe
  runs as far as its scarcest required slot allows and no further than it is
  asked. An optional slot uses what it gets and never holds a recipe back; an
  extraction (a site, no inputs) runs as often as it is asked. The result is a
  rate per good (supplied, made, wanted, taken), per recipe (runs asked, runs
  managed, workshops busy, the slot that held it back) and per consumer (wanted,
  got), and notes that say where the web starves and where it piles up. Two
  simplifications, on purpose for now: a good short in one place is not made up
  from another that has it to spare (the equal share is not revised), and
  nothing caps a recipe but its inputs (buildings and labour will).
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
| `resources/economy/webs/hearth.json` | **The first rung of the ladder of skeletal economies:** a village that feeds, clothes and tools itself. Fifteen goods, ten recipes, three consumers, and numbers on all of them: amounts, times, supplies, 120 heads and their wants. Unlocked, on purpose: it is the web to fiddle with while watching the balance. `tools/make_hearth.py` is its baseline and puts the numbers back. |
| `resources/economy/webs/starter.json` | A small one to learn on: the golem with its three hearts and bronze joints, bread and beer. 48 goods on the canvas, 29 recipes, two consumers; the full 286-good palette, so there is plenty to drag in. Opens first. |
| `resources/economy/webs/full-ledger-reference.json` | **Locked.** Everything from the 21 September 2026 brainstorm as it was: 286 goods, 197 recipes, four consumers (Food, Intoxicants and physic, Clothing, Wares). The map every other ledger was cut from; it cannot be changed or binned. (The unlocked copy, `full-ledger`, and the `test` web were removed on 2026-09-23: New… → a copy of this one does the same.) |
| `resources/economy/webs/tagged-ledger.json` | The full ledger reworked as a worked example: its either-or slots turned into tag slots, and varieties switched on (see the findings below). Its own palette. |
| `resources/economy/webs/variety-ledger.json` | **Locked.** The tagged ledger taken further as a stretch test of varieties (the second findings section below): the recipes' mistakes mended, dye, cloth and the golem heart folded into one good each, many more varieties on raw goods and on what is made of them, property tags the varieties imply, a colour and a symbol for every tag, icons of their own shape for every good that varies, and a fifth consumer for works and arms. `tools/make_variety_ledger.py` derives it. |
| `resources/economy/sprites/icons.png`, `signs.png` | 16 px cells, 16 columns. Cells 0 to 285 are the brainstorm's; cells from 288 on are the icons redrawn for the variety ledger, so the old webs keep their look. `sprites/custom/` takes PNGs imported through the lab, for a good's icon (`<id>.png`) and for its sign (`<id>.sign.png`). The sheets are files shared by every web; each palette names the sheets it uses. |
| `resources/economy/sprites/masks.png`, `tags.png` | The masks that icon layers tint (the golem's heart, eyes and body, the jewel's stone and band, the ship's hull), and the symbols of the tag namespaces and of the tags that need their own. `tools/make_variety_icons.py` and `tools/make_tag_signs.py` draw them, from `tools/icon_pixels.json` and `tools/sign_vocab.json` (the marks). |
| `resources/economy/sprites/elements.png` | The five element icons, 16 px, in a row: fire, wind, water, earth, quintessence. They belong to the system, not to a palette. `tools/make_element_icons.py` drew them. |
| `tools/import_economy_export.py`, `tools/make_tagged_ledger.py`, `tools/make_variety_ledger.py` | The one-off converter from the chat export, and the scripts that derived the tagged ledger from the full one and the variety ledger from the tagged one. Kept as the record of how the data was made and of every decision in it; running one again overwrites lab edits to the web it writes. |
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

## Findings: the stretch test (the variety ledger)

The variety ledger asks how far varieties go before they become a mess. Its
numbers come from the census (`-- census web=variety-ledger`, below).

**Mistakes mended first.** The five earths (clay, sand, ochre, fuller's earth,
peat) took "Soil, any of 17" while each good's note named its soil. Neither
seventeen soils of peat nor seventeen soil goods was right: the recipes were
asking for a *place*, so they now have a **site** and no slot (peat is cut on
murkearth; clay is dug on silt or floodearth). Soil the good stays, with its
seventeen varieties, for the golem's body, where the soil is the point. Salt got
a second recipe, salt pans on the coast, to show a site that is not a soil.
Cupellation now yields silver as well as litharge, as Silver's note said all
along. Three kilns that said coal take any fuel. Court dress demanded silk and
scarlet, which was a variety gating a slot: it now takes any dyed cloth and gold
thread, with an optional fur trim, and silk-in-scarlet-with-ermine is simply the
superb end of it. A second-opinion audit (Sonnet, 2026-09-23) found sixteen
more, and Maxim had all of them applied that could be: sugar is clarified with
lime and blood as its note says; incense listed camphor twice; spirit varnish
(shellac in spirit of wine) was promised by a note and missing; the madder vat
is a red vat, since dyewood fills it too; a fifth consumer, **Works and arms**,
buys `need:works-and-arms` (tools wear out, powder is spent, buildings eat
planks), so those twelve goods are consumables after all; the betel quid has its
leaf (a new good, with a borrowed icon until it has its own); wine takes eggs,
optional, for fining; the automaton's and the cosmetics' notes now explain their
heart slot and their aqua fortis; coir is no longer paper stock (coconut is off
`kind:plant-fibre` and rope names it beside the tag); the pigments recipe has
ochre as its base and the rare hues as choices; hartshorn, the volatile alkali,
is off `kind:alkali`. Two were left as they were, with reasons: kola is sold
raw (so are coconuts and eggs), and oak galls stay a tannin (gall tanning is
real; their scarcity is a price's business).

**Folds.** Five dyes became one Dye in six colours (a sixth recipe, green from
weld over indigo, cost one line); four cloths became one Cloth; three hearts one
Golem heart. The rule that decided: fold when every recipe treats the members
alike *and* a player would call them one thing in several flavours. Pottery and
porcelain stay apart; linen and wool do not. Scarlet cloth and blue-and-white
ware were varieties of Dyed cloth and Porcelain all along, so they and their
recipes went. Ten goods fewer (276), one recipe more (199), one consumer more (5).

**Many more varieties.** Grain went from three to six; timber, wool, hides, furs,
fish, milk, grapes and spices got varieties; the metals leave their mark on what
is made of them (bronze, iron or steel tools; gold or silver jewellery) through
a passing slot that takes a core tag, which is the case of substitutes that are
no varieties of each other but whose products are; optional slots grant what they
add (a blue glaze, a clear glass, an armed ship). 98 variety tags in 24 namespaces.

**Restraint, on purpose.** 38 slots pass variety on; 21 that could were held back
and are listed in the script with their reasons: rations do not remember their
bread, a spirit forgets its grape, ash is ash, nobody asks whose hide backs a book.
The census shows what that buys: **as it is, 50 of 275 goods vary, and a warehouse
holding one of everything would have 276 rows by good, 377 by property, about
2,400 by variety. With every slot passing, 133 goods vary and the count by variety
is about five hundred thousand million** (the aethership alone, tens of thousands
of millions, and the fifth consumer's provisions now carry their wine's egg). The system grows exponentially exactly where it is told to, and stays
flat where it is not; the leverage is the `passes` flag, and the count on the
node is the warning light.

**Where it lands.** The golem is the honest worst case: about 1,600 varieties
(eleven soils that reach it, three hearts, four fittings) from one recipe. Split
by heart it is three stacks; by heart and soil, fifty-one; **by property, fifteen**
(five kinds of work by three grades of heart), which is what the market will see.
Court dress is 180 varieties and 3 stacks; bread six and three; the rest under
ten. Nothing else is close, so the answer to "does this blow up" is: not while
slots are chosen, and the two goods that are big are the two that should be.

**What the property layer does not solve yet.** It decides how units stack; it
does not yet decide how a unit *looks* when two properties disagree, nor what a
recipe with amounts does with a mixed stack (blend, or take the lowest grade,
which the scale rule already says). Both wait for volume and time.

## How varieties stack in play: what is built and what is not

The stacking model of 2026-09-22 (stack by effect, not by history; bulk goods
blend, made things stay themselves; order by intent; one row per good, opened on
demand) is now half built: **property tags are the effects**, the `implies` list
is where history becomes effect, and the stack counts in the inspector and the
census are the numbers to watch. Not built: the warehouse itself, blending, and
readers of properties (prices, fashions, uses). Those come with the next stage.

## Hearth, and the ladder

Stage two is built against skeletal economies rather than the ledger, so that
the numbers can be judged by hand. **Hearth** is the first: grain to flour to
bread (with salt), timber to planks and to charcoal, ironstone and fuel to iron
to tools, wool to yarn to cloth to clothes; Food, Clothing and Works and arms;
120 people who want a loaf a day, a garment every fifty days and a tool or a
plank every twenty. As it ships the village is short of grain: the farms give
100 a day and the mill asks 111, so the mill manages 10 runs of 11.1, the
bakery 18 of 20 with nine ovens busy, and the people get 108 of 120 loaves;
timber and ore pile up. Every one of those numbers is in `tools/make_hearth.py`
and meant to be moved. Next on the ladder, not yet made: a market town (about
40 goods, all five Needs, one graded chain) and an alchemist's town (about 80,
the acids, Essence and the golem), and the ledger last.

## From a shell

```
godot --path . --headless scenes/dev/economy_lab.tscn -- selftest
godot --path . scenes/dev/economy_lab.tscn -- shot web=tagged-ledger select=r.golem zoom=1 out=/tmp/lab.png
godot --path . --headless scenes/dev/economy_lab.tscn -- bake
godot --path . --headless scenes/dev/economy_lab.tscn -- census web=variety-ledger
godot --path . --headless scenes/dev/economy_lab.tscn -- balance web=hearth
godot --path . scenes/dev/economy_lab.tscn -- bench web=full-ledger-reference
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
own writer; run it after any script in `tools/`. `census` counts, for every good
of a web, how many varieties the web can make of it and how many stacks those
fall into by property, then does the same with every slot passing variety on, so
"does this blow up" has a number. `balance` prints a web's balance sheet: the
notes, the consumers, the recipes with their runs and workshops, the goods with
what is supplied, made, wanted and taken. `bench` (windowed) measures the lab on the
machine it runs on: frame times idle, panning, zooming and hovering over the big
web, the cost of a selection and of a change, and writes a table to
`user://economy_bench.txt`; run it on a machine where the lab feels slow and
send the table.

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
      { "id": "kind", "note": "What sort of stuff it is…", "role": "core", "sign": { "atlas": "tags", "index": 28 } },
      { "id": "heart", "note": "Which heart a golem was given.", "role": "variety", "sign": { "atlas": "tags", "index": 13 } },
      { "id": "grade", "note": "How good a thing is…", "role": "property", "combine": "lowest" }
    ],
    "tags": [
      { "id": "kind:golem-heart", "note": "Fits the heart slot of a golem." },
      { "id": "heart:bismuth", "note": "…", "colour": "#E7A6D8", "implies": [ "grade:fine" ], "sign": { "atlas": "tags", "index": 32 } }
    ],
    "goods": [
      { "id": "heart", "name": "Golem heart", "note": "…",
        "tags": [ "stage:assembly", "kind:golem-part" ],
        "icon": { "atlas": "icons", "index": 339 },
        "sign": { "atlas": "signs", "index": 229, "source": "compound", "reading": "…", "parts": [ "HT", "Sb" ] } },
      { "id": "grain", "name": "Grain", "note": "…", "tags": [ "stage:raw", "kind:food" ],
        "varieties": [ { "id": "rye", "name": "Rye", "note": "…", "tags": [ "grain:rye" ] } ] },
      { "id": "golem", "name": "Golem", "note": "…", "tags": [ "stage:assembly" ],
        "layers": [ { "match": "heart", "mask": { "atlas": "masks", "index": 0 } }, { "match": "fit:smalt-eyes", "mask": { "atlas": "masks", "index": 1 } }, { "match": "soil" } ] }
    ]
  },
  "goods": [ "heart", "grain", "golem", "peat" ],
  "recipes": [
    { "id": "r.golem", "name": "", "note": "", "element": "qe",
      "inputs": [
        { "accepts": [ "blood" ] },
        { "accepts": [ "heart" ], "passes": true },
        { "accepts": [ "bronze" ], "optional": true, "grants": [ "fit:bronze-joints" ] }
      ],
      "outputs": [ { "good": "golem" } ] },
    { "id": "r.peat", "name": "Peat cutting", "element": "earth", "site": [ "soil:murkearth" ], "inputs": [], "outputs": [ { "good": "peat" } ] },
    { "id": "r.bread", "name": "Bakery", "element": "fire", "time": 0.5,
      "inputs": [ { "accepts": [ "flour" ], "amount": 5, "passes": true }, { "accepts": [ "salt" ], "amount": 0.2 } ],
      "outputs": [ { "good": "bread", "amount": 6 } ] }
  ],
  "consumers": [ { "id": "c.food", "name": "Food", "note": "", "accepts": [ "#need:food" ], "wants": 1 } ],
  "heads": 120,
  "supply": { "grain": 100, "slt": 5 },
  "layout": { "h2": [0, 0], "r.golem": [1480, 310] }
}
```
`locked` is written only when true. `element` is `fire`, `wind`, `water`, `earth`
or `qe`. `goods` lists which of the palette's goods are on the canvas. A sprite is a cell
of an atlas (`atlas`, `index`) or a PNG of its own (`"file": "sprites/custom/h4.png"`).
A tag may be in use without an entry under `tags`; the entry is where its note,
its `colour` (`#RRGGBB`), its own `sign` and, for a variety tag, what it
`implies` live. A namespace's `role` is `core`, `variety`, `property` or absent;
a property namespace with `"combine": "lowest"` is a scale in the order its
tags are listed. A recipe's `site` lists the tags of the ground or the place it
must stand on, any one of which will do. A good's `layers` say which part of its
icon each variety namespace (or one whole tag) tints, through a `mask` sprite,
or the whole icon when there is none. Every one of these is omitted when empty,
so a file from the tagged ledger's day reads unchanged. An input's or an
output's `amount` is per run and one when absent; a recipe's `time` is days per
run and one when absent; a consumer's `wants` is units a head a day; the web's
`heads` is its population and `supply` what the land gives a day by good,
written sorted. A recipe will likewise take a building and labour.

## The code

`scripts/economy/` is the model, with no Godot types in it, so the game can load a
web as it is; `scripts/dev/EconomyLab*.cs` is the lab. Each folder has a
`CLAUDE.md` with its map and its rules. In one sentence: the web is the truth and
the canvas follows it; a gesture becomes a change to the web through
`EconomyLab.Change`, the web is analysed again, and the canvas is brought into line.

## What is next

Not done here, in rough order of how soon they will be wanted: a recipe's
building and labour, which is what will cap a recipe besides its inputs; the
balance revised so that a good short in one place is made up from another
that has it to spare; the next rungs of the ladder (a market town, an
alchemist's town); property tags read by consumers (what
a fashion pays for) and by uses (what a grade or a kind of work is worth), and
the warehouse that stacks by them; icons of their own shape for the goods that
do not vary yet (the redraw covered the ones that do); whole chains written out
as formulae in signs end to end; by-products used in earnest (the second output
port is there); webs compared side by side; a pixel editor for the sprites and
the masks; frames to group a chain on the canvas; the soil and climate a raw
variety needs, which the site is the first half of.
