# The economy model: working notes

Loaded when you work under `scripts/economy/`. Manual, data format and the
findings of the tagging pass: **`docs/economy-lab.md`**. The lab that edits this
data is `scripts/dev/EconomyLab*.cs`; its house rules are in `scripts/dev/CLAUDE.md`.

Namespace `ProjectNikitin.Economy`. **No Godot types in here**, so the game can
load a web as it is and a test can run without the engine.

## The model

- A **web** (`EconomyWeb`, one file `resources/economy/webs/<id>.json`) is one
  version of the economy, whole: its own **palette**, which of the palette's goods
  are on the canvas, the **recipes** between them, the **consumers**, the layout.
  **Nothing is shared between webs** (decided 2026-09-21, after a shared catalogue
  let an experiment in one web leak into the others). Goods cross from web to web
  only by `EconomyEdit.ImportGoods`, `ImportPalette` and `CopyChain`, which copy.
- A **palette** (`Palette`) is a web's goods (`Good`: id, name, note, tags, icon,
  sign, authored varieties), its tag notes (`TagDef`), its tag **namespaces**
  (`TagNamespace`: id, note, role) and its sprite sheets (`AtlasDef`).
- A **recipe** has input **slots** (`RecipeInput`) and outputs. A slot lists what
  it **accepts**, any one of which fills it: a good's id, or a tag behind a hash
  (`#kind:golem-heart`), which admits whatever carries it (`Acceptor`). A slot may
  be `Optional`, and may `Passes` variety on (below). A good made two ways has two
  recipes. A **consumer** is a sink that accepts the same way (`#need:food`); a
  good that reaches one is a consumable.
- **Tag roles.** A namespace is `core` (what a good is: what slots and consumers
  are meant to accept; it decides a good's place in the web), `variety` (what is
  particular about it), or plain (it only describes). Only `variety` has
  mechanics.
- **Varieties.** A good's variety tags are on every unit of it. A good may also
  have **authored varieties** (`Variety`: rye and wheat of grain, the seventeen
  soils): one node, the same slots, extra tags each. And through every slot with
  `Passes` set, the variety tags of whatever fills the slot are stamped on the
  recipe's output, transitively. So one golem recipe with a three-heart tag slot
  and four optional fittings yields 48 golems, and nobody writes them down:
  `WebAnalysis.VarietiesOf` derives them (`VarietySet`, listed up to 512, counted
  beyond). Prices and in-world uses will read variety tags; nothing does yet.
- Sources, final goods, depth, hubs, links (drawn, and implied by tags), varieties
  and issues are read off a web by `WebAnalysis` and **never stored**.

## Rules

- Every change is a plain function in `EconomyEdit` (add, remove, delete from the
  palette, new recipe, import, copy a chain, cut a web, rename or remove a tag,
  rename a namespace). Add new kinds of change there, not in the lab.
- The files must stay backwards compatible: every class keeps fields it does not
  know (`JsonExtensionData`), inputs and outputs are objects so that an amount can
  join them, omitted-when-default for new flags. `EconomyStore.Format` is 2.
  Layout is written one sorted line per node, so a moved node is a one-line diff.
- Determinism as elsewhere: ordinal compares, stable sorts, no dictionary order
  deciding anything (`LayeredLayout` is written to that rule).
- The regression gate is the lab's self-test (it exercises this model through the
  lab's handlers): `godot --path . --headless scenes/dev/economy_lab.tscn -- selftest`.
  Run it after any change here; it works on a scratch copy and exits non-zero on a failure.

## The files

```
EconomyWeb.cs, Palette.cs, Good.cs, Variety.cs, SpriteRef.cs, TagDef.cs, TagNamespace.cs, AtlasDef.cs
                           A web and its palette.
Recipe.cs, RecipeInput.cs, RecipeOutput.cs, Consumer.cs, Acceptor.cs, Spot.cs
                           Recipes with slots, consumers, what a slot accepts, a canvas position.
EconomyStore.cs            The JSON files: load, save, list, clone.
WebAnalysis.cs, VarietySet.cs, WebLink.cs, LinkKind.cs, GoodRole.cs, WebIssue.cs, IssueLevel.cs
                           A web read back.
EconomyEdit.cs             Every change as a plain function.
WebArrange.cs, LayeredLayout.cs
                           The left-to-right arrangement.
```

Data: `resources/economy/webs/` (`full-ledger`, `starter`, `tagged-ledger`, and
whatever Maxim has made), `resources/economy/sprites/`. `tools/import_economy_export.py`
made the first two from the chat export; `tools/make_tagged_ledger.py` derived the
third. Both are one-offs kept as the record; re-running either overwrites lab edits.
Icons and signs are 16 px placeholders generated in chat: pre-production material.
