# Dev scenes: working notes

Loaded when you work under `scripts/dev/`. Six dev scenes live here, in two
families that share nothing but `DevPalette`-style helpers.

## Terrain tools (manual: `docs/dev-scenes.md`)

```
IslandLab*.cs              The island lab (scenes/dev/island_lab.tscn, F6).
GenerationAudit*.cs        The audit (generation_audit.tscn): the measured guarantees.
GenerationChecksum.cs      The checksum (generation_checksum.tscn): bit for bit.
MeshBench*.cs, DomainsBench.cs
                           The mesh bench and the Domains bench.
StepProbe*.cs              A temporary probe of where two-slab steps come from.
DevPalette.cs, TinyFont.cs The shared colours, and a 5x7 bitmap font so a headless
                           PNG can carry its own labels.
```

Before changing what they measure, read `scripts/generation/CLAUDE.md` (the two
regression gates, determinism) or `scripts/terrain/CLAUDE.md` (the benches).

## The economy lab (manual: `docs/economy-lab.md`; model: `scripts/economy/CLAUDE.md`)

The lab's design page is the Notion page [CLAUDE] Alchemical Economy, Claude's to
keep current: a change to the lab that alters what the page says (a run, a
panel, a number, a picture) is finished only when the page says it too.

```
EconomyLab.cs              The core: what is open, Change (the one door), undo, save, the window, shell modes.
EconomyLab.Graph.cs        The canvas: SyncGraph, gestures into changes, menus, pickers, trace, find.
EconomyLab.Bar.cs          Top bar, status line, help sheet, the new-good and new-web dialogs.
EconomyLab.Palette.cs      Left dock: the web's palette, the Tags tab with namespaces and roles.
EconomyLab.Import.cs       Import goods or a whole palette from another web.
EconomyLab.Inspector.cs    Right dock: the selected good, recipe, consumer, or the web.
EconomyLab.SelfTest.cs     -- selftest.
EconomyLab.Census.cs       -- census: varieties and stacks per good, and the same with every slot passing; -- balance: the sheet.
EconomyLab.Bench.cs        -- bench: frame times and costs on this machine, for the laggy-on-Windows question.
EconomyLab.Sprites.cs      -- sprites: every icon and variety on contact sheets, and what is wrong with them.
GoodNode.cs, RecipeNode.cs, ConsumerNode.cs, WebGraph.cs
                           The three node kinds and the canvas (a GraphEdit).
PickPopup.cs, IconPickPopup.cs, PaletteTree.cs, InspectorLook.cs, LabLook.cs, SpriteBank.cs
                           Pop-ups, the draggable list, the look, sprites read straight off the disk.
```

House rules:

- **The web is the truth and the canvas follows it.** Every edit goes through
  `EconomyLab.Change(what, edit, merge, keepInspector)`: it snapshots the web for
  undo, runs the edit, re-reads the web (`WebAnalysis`), syncs the canvas
  (`SyncGraph`), refreshes the docks and autosaves. Never edit the canvas directly.
- Inside an edit, look goods and recipes up again by id; an object held from before
  an undo is dead. Typing in a field passes a `merge` key (one undo step) and
  `keepInspector: true` (the field keeps its caret).
- The lab turns the project's stretch off and scales its own interface
  (`ApplyWindow`, per machine in `user://economy_lab.cfg`). Auto is the screen's
  scale brought down until `ComfortableWidth` fits the window it actually got,
  because pressing F6 usually runs it **embedded in the editor's Game tab**
  (`Engine.IsEmbeddedInEditor()`), where it cannot be maximised or go full screen;
  the top bar is a flow so it wraps rather than hides the scale menu, and
  Cmd/Ctrl with + - 0 change the scale from the keys. A `shot` or `selftest` run
  keeps a plain 1920 by 1080 window so pictures match across machines
  (`window=WxH` asks for another size, to see the fitting).
- A locked web (`Web.Locked`) is refused at the door: `Change` says why and puts
  the canvas and the inspector back; `Save` and Bin skip it.
- A pop-up opened in the first frames of a shell run is closed again by the
  window's focus changes; `show=` waits (`ShowForShot`).
- `Balance` (a `WebBalance`) is read with `Analysis` on every change; the Balance
  toggle in the bar hands it to the nodes' `Show`, which then write the good's day
  under its name and the recipe's runs in its title.
- Icons follow the data: `SpriteBank.Compose(good, tags)` is the icon of a unit
  carrying those tags (the good's layers tint their masks with the tags' colours,
  the rest go to pips). A plain stack shows `Get(good.Icon)`, the same object.
  `ComposeImage` gives the same pixels as an image, for a headless run. A good's
  icon is shown on a parchment tile (`Sprites.Icon(good.Icon)`, and `Compose`
  bakes it in): icons are drawn for parchment. Signs and tag symbols stay bare.
- One shape per good: two goods never share a drawing (varieties share their good's).
- Sprites are drawn only by the most advanced models (Fable 5.1, Opus 5.5 today):
  Maxim's preference. A delegate may write the tooling around them.

```
godot --path . --headless scenes/dev/economy_lab.tscn -- selftest    # the regression gate: links, tags, undo, palettes, import, varieties, files, canvas
godot --path . scenes/dev/economy_lab.tscn -- shot web=tagged-ledger select=r.golem zoom=1 out=/tmp/lab.png   # windowed; never writes data
godot --path . --headless scenes/dev/economy_lab.tscn -- bake        # arrange webs with no layout, rewrite every file in the lab's format
godot --path . --headless scenes/dev/economy_lab.tscn -- census web=variety-ledger   # how many varieties and stacks; never writes
godot --path . --headless scenes/dev/economy_lab.tscn -- balance web=hearth           # the balance sheet; never writes
godot --path . --headless scenes/dev/economy_lab.tscn -- sprites web=variety-ledger out=/tmp/sprite_review   # contact sheets and a legibility report; writes only to out
godot --path . scenes/dev/economy_lab.tscn -- bench web=full-ledger-reference        # windowed; writes user://economy_bench.txt only
```

`shot` also takes `show=help|find|newweb|newgood|issues`, `tags`, `tags=ns:<namespace>`, `import` and `window=WxH`.
