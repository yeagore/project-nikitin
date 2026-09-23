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
  particular about it), `property` (what units stack by: what variety tags imply),
  `site` (ground or a place a recipe stands on, carried by no good: `anchor`,
  `warmth`, `moisture`, `exposure`), or plain (it only describes).
- **Varieties.** A good's variety tags are on every unit of it. A good may also
  have **authored varieties** (`Variety`: rye and wheat of grain, the seventeen
  soils): one node, the same slots, extra tags each. And through every slot with
  `Passes` set, the variety tags of whatever fills the slot are stamped on the
  recipe's output, transitively. So one golem recipe with a three-heart tag slot
  and four optional fittings yields 48 golems, and nobody writes them down:
  `WebAnalysis.VarietiesOf` derives them (`VarietySet`, listed up to 512, counted
  beyond). Prices and in-world uses will read variety tags; nothing does yet.
  A slot may also `Grants` variety tags of its own to the output whenever it is
  filled, for when the effect belongs to the combination and not the ingredient
  (the golem's hands slot grants `fit:clockwork-hands`; Clockwork stays plain).
  **A variety never gates:** no slot or consumer accepts a variety tag (the
  analysis notes it as an issue); a recipe that wants one soil wants a site.
- **Properties and stacks.** A variety tag's `TagDef.Implies` lists property tags
  (`soil:murkearth` implies `work:water`, `grain:oats` implies `grade:coarse`).
  `Palette.PropertiesOf(varietyTags)` is what a unit stacks as: the union, except
  in a namespace with `Combine = "lowest"` (a scale in the order of the tag list),
  which keeps the lowest. `WebAnalysis.StacksOf` counts a good's stacks by property
  the way `VarietiesOf` counts its varieties; `VarietiesIn(good, namespaces)` is
  the view split by some namespaces only. The variety ledger's golem: ~1,600
  varieties, 15 stacks, 3 when split by heart.
- **Every good comes out of something** (Maxim, 2026-09-23). A raw good comes out
  of an **extraction**: a recipe with no required input (`Recipe.IsExtraction`)
  that stands on a site. The analysis warns on a good nothing makes and on an
  extraction that stands nowhere. A good made only by extractions is still a
  `Source`, at depth 0.
- **Sites.** `Recipe.Site` lists the tags of the ground or place the work stands on;
  nothing is hauled. Tags of one namespace are alternatives, namespaces add up
  (`SiteGroups`): `soil:brownearth, soil:blackearth, anchor:river` is either soil,
  by a river. The soils are the `soil` variety tags; `anchor:*` are the generator's
  feature anchors (each tag's note names its `IslandData` field); `warmth`,
  `moisture`, `exposure` are habitat bands, named only where the soil does not fix
  them. **The coasts face the aether, not a sea**: there is no sea in the Ecumene, so
  no sea salt, sea fish, seaweed or beach palms; water is rivers, lakes, springs.
  **Ore deposits are not tags** (no one knows how much ore a Domain holds):
  a mine stands on rock and names its deposit in its note. Nothing yet tests a
  site against a Domain.
- **Colours, symbols, layers.** A `TagDef` has a `Colour` (`#RRGGBB`) and may have
  a `Sign`; a namespace has a `Sign` its tags share (`Palette.SignOf`,
  `ColourOf`). A good's `Layers` (`IconLayer`: a `Match`, a namespace or a whole
  tag, and a `Mask` sprite or none for the whole icon) say how the lab tints its
  icon per variety; the lab's `SpriteBank.Compose` does the tinting. Shape by
  good, hue by variety.
- **Elements.** A recipe has an `Element` (`Element.All`: fire violent synthesis,
  wind violent analysis, water gentle synthesis, earth gentle analysis, qe pure
  magic). A classification by feel, Maxim's scheme of 2026-09-22; nothing reads it
  yet. Webs should keep the four in rough balance; the self-test holds the shipped
  ones to 15 to 35% each. The icons are `sprites/elements.png`, the system's, not a palette's.
- **Locked webs.** `EconomyWeb.Locked` marks a reference copy: the lab refuses to
  change or bin it. `full-ledger-reference` is the big web as it was and
  `variety-ledger` the stretch test; both locked. Unlocking is an edit to the
  file, on purpose.
- **Amounts, time and the balance** (stage two). `RecipeInput.Amount` and
  `RecipeOutput.Amount` (per run, null = 1, read `Count`), `Recipe.Time` (days per
  run, null = 1, read `Days`), `Recipe.Limit` (how many at work at once; runs a
  day ≤ Limit ÷ Days; null none), `RecipeInput.Boost` (an optional slot's extra of
  every output when filled, a fraction, read `Bonus`), `RecipeOutput.ByProduct`,
  `Consumer.Wants` (units a head a day, read `Rate`), `EconomyWeb.Heads`. The old
  `EconomyWeb.Supply` is gone: `EconomyStore.MigrateSupply` turns a file's
  `supply` into extractions on load. `WebBalance.Of(web, analysis)` pulls the
  wants back (equal shares across fillers, and across a good's makers except that
  a capped maker is asked only for what it can make; only firm wants, from
  required slots and consumers, raise a maker; a by-product never does) and pushes
  forward (seed corn first: a firm asker whose main products lead back to the good
  through required slots; then firm askers, then optional ones, each tier in proportion;
  a recipe runs as far as its scarcest required slot and its limit allow; boosts
  scale the outputs). The push goes round loops from the top down until nothing
  moves: monotone, so it settles on the greatest flow the loop keeps up. Hearth
  is the worked example and the self-test's arithmetic; docs/economy-lab.md has
  the rules in plain words.
- Sources, final goods, depth, hubs, links (drawn, and implied by tags), varieties,
  issues and the balance are read off a web by `WebAnalysis` and `WebBalance` and **never stored**.

## Rules

- Every change is a plain function in `EconomyEdit` (add, remove, delete from the
  palette, new recipe, import, copy a chain, cut a web, rename or remove a tag,
  rename a namespace). Add new kinds of change there, not in the lab. A tag can be
  named in seven places (goods, varieties, the tag list, acceptors, grants, sites,
  implications, layer matches): `RenameTag` and `RemoveTag` reach all of them, and
  a new place joins `NamedLists` there.
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
EconomyWeb.cs, Palette.cs, Good.cs, Variety.cs, IconLayer.cs, SpriteRef.cs, TagDef.cs, TagNamespace.cs, AtlasDef.cs
                           A web and its palette.
Recipe.cs, RecipeInput.cs, RecipeOutput.cs, Consumer.cs, Acceptor.cs, Element.cs, Spot.cs
                           Recipes with slots, consumers, what a slot accepts, the five elements, a canvas position.
EconomyStore.cs            The JSON files: load, save, list, clone.
WebAnalysis.cs, VarietySet.cs, WebLink.cs, LinkKind.cs, GoodRole.cs, WebIssue.cs, IssueLevel.cs
                           A web read back.
WebBalance.cs              A web's day: what flows where, and where it starves or piles up.
EconomyEdit.cs             Every change as a plain function.
WebArrange.cs, LayeredLayout.cs
                           The left-to-right arrangement.
```

Data: `resources/economy/webs/` (the locked `full-ledger-reference`, `starter`,
`tagged-ledger`, the locked `variety-ledger`, `hearth` from `tools/make_hearth.py`, and whatever Maxim has made;
the older webs keep their sources, and the analysis names them),
`resources/economy/sprites/` (`icons`, `signs`, `masks`, `tags`, `elements`).
`tools/import_economy_export.py` made the full ledger from the chat export;
`tools/make_tagged_ledger.py` derived the tagged one and `tools/make_variety_ledger.py`
the variety one (it stamps in `tools/variety_icons.json` and `tools/tag_signs.json`,
which `make_variety_icons.py` and `make_tag_signs.py` draw); `tools/apply_recipe_elements.py`
stamped `tools/recipe_elements.json` in place; `tools/make_element_icons.py` drew the
element icons; `tools/make_icon_fixes.py` and `tools/make_shape_pass.py` redrew icons
after the sprite review (one shape per good), stamped in by `make_variety_ledger.py`.
One-offs kept as the record; re-running one overwrites lab edits to the web it writes.
Icons and signs are 16 px placeholders, drawn for a parchment ground: pre-production material.
