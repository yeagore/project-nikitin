# The economy lab

`scenes/dev/economy_lab.tscn` (F6 in the editor) is an editor of production webs:
goods as draggable nodes, recipes between them, links drawn by hand or implied by
a tag, and consumer blobs that mark the consumables. It is the first stage of an
economy constructor. Amounts, proportions and time are not modelled yet; the
point of this stage is the lab's shape and the principle of the data.

This file is the manual and the data format. Written plainly on purpose: it is
meant to be read by Maxim as much as by the model.

## The idea

- The **catalogue** is every good there is: its id, name, description, tags and
  two sprites (an icon and an alchemical sign). One file, shared by all webs. It
  is the palette. Rename a good or redraw it and it changes everywhere.
- A **web** is one version of the economy, the painting: which goods of the
  catalogue are in it, the **recipes** that join them, the **consumers** they
  lead to, and where each node sits on the canvas. One file per web. Two webs
  can make the same good in different ways, because recipes belong to the web.
- A **recipe** has input **slots** and outputs. Every slot must be filled for
  the recipe to run, unless the slot is optional. A slot lists what it
  **accepts**, and any one of those fills it:
  - a good, by id (`icu`);
  - a **tag**, written with a hash (`#kind:golem-heart`), which admits every
    good that carries it. Give a new heart that tag and it fits the golem with
    no recipe edited. Links that a tag brings are drawn by the lab on its own, in
    amber.
  So "iron from ironstone with coal *or* charcoal" is one slot accepting two
  goods, and "a golem takes any heart" is one slot accepting a tag. A good made
  in two wholly different ways has two recipes.
- A **consumer** is a sink. A good that leads to one is a consumable (eaten,
  drunk, worn out, used up); a good that leads nowhere is a durable or a work. A
  consumer accepts goods and tags the way a slot does: the Food consumer accepts
  `#need:food`.
- Nothing about a good's place in the web is stored. A **source** is a good
  nothing in this web makes (whatever it is elsewhere), a **final** good is one
  no recipe here uses, and the rest are intermediates. The lab reads these off
  the links every time something changes, along with each good's depth (steps
  from the ground), the hubs (six recipes or more use it), and the issues.

## What ships

| File | What |
|---|---|
| `resources/economy/catalogue.json` | 286 goods, 90 tags in nine namespaces, two sprite sheets. |
| `resources/economy/webs/full-ledger.json` | Everything from the 21 September 2026 brainstorm: 286 goods, 197 recipes, four consumers (Food, Intoxicants and physic, Clothing, Wares). Big: a map to cut from. |
| `resources/economy/webs/starter.json` | A small one to learn on: the golem with its three hearts and bronze joints, bread and beer. 48 goods, 29 recipes, two consumers. Opens first. |
| `resources/economy/sprites/icons.png`, `signs.png` | 16 px cells, 16 columns. `sprites/custom/` takes PNGs imported through the lab. |
| `tools/import_economy_export.py` | The one-off converter from the chat export (`nikitin-economy-export.zip`) to the files above. Re-running it overwrites lab edits; it is kept as the record of how the data was mapped. |

The import changed three things in the data. The computed `trait:hub` tag was
dropped (the lab computes hubs). The golem's heart slot, which listed the
antimony heart with the arsenic and bismuth hearts as alternatives, became one
slot accepting `#kind:golem-heart`, a new tag on the three hearts. The `stage`,
`group` and `need` fields were dropped because the tags `stage:`, `group:` and
`need:` already said the same (checked for all 286). Goods tagged
`need:works-and-arms` have no consumer: they are durables and works. Not
imported: `pixels.json` (the sprites as text), `sign_vocab.json`, `soils.json`.

## Using it

Open `scenes/dev/economy_lab.tscn` in the editor and press F6. F1 in the lab
shows the same gestures as below.

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
  catalogue, a new good, a tag.
- Right-click the canvas: add a good from the catalogue, new good, new recipe,
  new consumer. Right-click a node: a recipe that makes it or uses it, remove.
- Delete removes the selected nodes from the web. Goods stay in the catalogue.
- Node colours are the good's stage (raw brown, processed blue, compound teal,
  magistery purple, assembly orange, finished olive). Link colours: white a
  required input, grey optional, amber by tag, blue what a recipe makes, green
  consumed.

**The top bar**
- *Web*: which web is open. *New…* makes one: empty, a copy of the open one, or
  **the selected goods and everything upstream of them**, which is how a
  vertical slice is cut from the full ledger: select the final goods you want,
  press New…. *Bin…* moves the web's file to the system trash.
- *Save*, and *Autosave* (on by default: a moment after every change). The files
  are in the repository, so git is the safety net and the history.
- *Undo* / *Redo*: a hundred steps, for the open web and the catalogue. Typing
  in one field is one step.
- *Arrange* lays the whole web out afresh, sources left, consumers right. *Trace*
  keeps lit what the selected node is made of and what is made with it. *Find…*
  goes to a node by name.
- *Issues* lists what the analysis found: a recipe that makes nothing, a slot
  that accepts nothing, a tag nothing in the web carries, a loose good, a loop.

**The left dock** is the catalogue. Filter by name or `#tag`, drag goods onto the
canvas or double-click. *With its chain from* brings each good with its recipes
and everything upstream, copied from another web. The Tags tab lists the tags,
edits their notes, renames and deletes them. A rename or a delete reaches every
web's file at once, and that part cannot be undone.

**The right dock** edits what is selected. A good: name, description, tags,
icon and sign (from the sheet, or a PNG of your own), what makes it and uses it.
A recipe: its label, its slots (what each accepts, optional or not), its
outputs. A consumer: its name and what it accepts. Nothing selected: the web's
name and note, its numbers, hubs, issues, and the legend.

## From a shell

```
godot --path . scenes/dev/economy_lab.tscn -- shot web=full-ledger select=brass out=/tmp/lab.png
godot --path . --headless scenes/dev/economy_lab.tscn -- bake
```

`shot` opens a window, draws, saves a PNG and quits; it never writes to the data
(`web=` which web, `select=` a node key to select and travel to, `zoom=` a zoom,
`out=` the file; `tags` opens the Tags tab). `bake` arranges every web that has
no layout and rewrites every file through the lab's own writer; run it after
the importer.

## The files

Plain JSON, two-space indent, written whole to a sibling file and moved into
place. Every object keeps fields it does not know, so a file from a newer lab
survives an older one. Ids are lowercase letters, digits and hyphens. A good's id
never changes; a recipe's id starts with `r.` and a consumer's with `c.`, so the
three kinds never collide and a layout key needs no prefix.

`catalogue.json`
```json
{
  "format": 1,
  "title": "…", "note": "…",
  "atlases": [ { "id": "icons", "file": "sprites/icons.png", "cell": 16, "columns": 16 } ],
  "tagNamespaces": [ { "id": "kind", "note": "What sort of stuff it is…" } ],
  "tags": [ { "id": "kind:golem-heart", "note": "Fits the heart slot of a golem." } ],
  "goods": [
    { "id": "h2", "name": "Antimony heart", "note": "…",
      "tags": [ "stage:assembly", "kind:golem-part", "kind:golem-heart" ],
      "icon": { "atlas": "icons", "index": 229 },
      "sign": { "atlas": "signs", "index": 229, "source": "compound", "reading": "…", "parts": [ "HT", "Sb" ] } }
  ]
}
```
A sprite is a cell of an atlas (`atlas`, `index`) or a PNG of its own
(`"file": "sprites/custom/h4.png"`). A tag may be in use without an entry under
`tags`; the entry is where its note lives.

`webs/<id>.json`
```json
{
  "format": 1,
  "id": "starter", "name": "…", "note": "…",
  "goods": [ "cu", "icu", "golem" ],
  "recipes": [
    { "id": "r.golem", "name": "", "note": "",
      "inputs": [
        { "accepts": [ "blood" ] },
        { "accepts": [ "#kind:golem-heart" ] },
        { "accepts": [ "bronze" ], "optional": true }
      ],
      "outputs": [ { "good": "golem" } ] }
  ],
  "consumers": [ { "id": "c.food", "name": "Food", "note": "", "accepts": [ "#need:food" ] } ],
  "layout": { "cu": [0, 0], "r.golem": [1480, 310] }
}
```
Inputs and outputs are objects so that amounts can join them (`"amount": 2`)
without breaking a file; a recipe will likewise take `time`, a building and
labour. The view (scroll and zoom per web), the last web opened and the two
toggles are per machine, in `user://economy_lab.cfg`, not in the repository.

## The code

| Where | What |
|---|---|
| `scripts/economy/` (`ProjectNikitin.Economy`) | The model, with no Godot types in it, so the game can load a web as it is: `Catalogue`, `Good`, `SpriteRef`, `TagDef`, `AtlasDef`; `EconomyWeb`, `Recipe`, `RecipeInput`, `RecipeOutput`, `Consumer`, `Acceptor`, `Spot`; `EconomyStore` (the files); `WebAnalysis` (links, roles, depth, hubs, issues, upstream and downstream), with `WebLink`, `LinkKind`, `GoodRole`, `WebIssue`, `IssueLevel`; `EconomyEdit` (every change as a plain function: add and remove, copy a chain, cut a web, rename a tag); `WebArrange` and `LayeredLayout` (the arrangement). |
| `scripts/dev/EconomyLab*.cs` | The lab. `EconomyLab.cs` is the core: what is open, and `Change`, the one door every edit goes through, which is what makes undo, autosave and the refresh work. `.Graph.cs` the canvas, `.Bar.cs` the bars and dialogs, `.Palette.cs` and `.Inspector.cs` the docks. |
| `scripts/dev/GoodNode.cs`, `RecipeNode.cs`, `ConsumerNode.cs`, `WebGraph.cs` | The three node kinds and the canvas (a `GraphEdit`). |
| `scripts/dev/LabLook.cs`, `SpriteBank.cs`, `PickPopup.cs` | Colours and boxes; sprites read straight off the disk; the search-and-pick pop-up. |

The web is the truth and the canvas follows it. A gesture becomes a change to
the web through `Change`; then the web is analysed again and the canvas is
brought into line (`SyncGraph`): stale wires and nodes dropped, new ones added,
each node redrawn only if what it shows has changed.

## What is next

Not done here, in rough order of how soon they will be wanted: amounts on slots
and outputs, and a recipe's time, building and labour; rates at the sources and
the consumers, and a balance sheet per web; by-products used in earnest (the
second output port is there); sets of webs compared side by side; a pixel editor
for the sprites; frames to group a chain on the canvas; the soil and climate a
raw good needs.
