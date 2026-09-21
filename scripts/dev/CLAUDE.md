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

```
EconomyLab.cs              The core: what is open, Change (the one door), undo, save, the window, shell modes.
EconomyLab.Graph.cs        The canvas: SyncGraph, gestures into changes, menus, pickers, trace, find.
EconomyLab.Bar.cs          Top bar, status line, help sheet, the new-good and new-web dialogs.
EconomyLab.Palette.cs      Left dock: the web's palette, the Tags tab with namespaces and roles.
EconomyLab.Import.cs       Import goods or a whole palette from another web.
EconomyLab.Inspector.cs    Right dock: the selected good, recipe, consumer, or the web.
EconomyLab.SelfTest.cs     -- selftest.
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
  (`ApplyWindow`, per machine in `user://economy_lab.cfg`); a `shot` or `selftest`
  run keeps a plain 1920 by 1080 window so pictures match across machines.
- A pop-up opened in the first frames of a shell run is closed again by the
  window's focus changes; `show=` waits (`ShowForShot`).

```
godot --path . --headless scenes/dev/economy_lab.tscn -- selftest    # the regression gate: links, tags, undo, palettes, import, varieties, files, canvas
godot --path . scenes/dev/economy_lab.tscn -- shot web=tagged-ledger select=r.golem zoom=1 out=/tmp/lab.png   # windowed; never writes data
godot --path . --headless scenes/dev/economy_lab.tscn -- bake        # arrange webs with no layout, rewrite every file in the lab's format
```

`shot` also takes `show=help|find|newweb|newgood|issues`, `tags`, `tags=ns:<namespace>` and `import`.
