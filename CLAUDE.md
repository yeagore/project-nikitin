# Project Nikitin

A single-player economic/exploration strategy game built in **Godot 4.7**. The
player is a merchant-pioneer running a trading company across the **Ecumene**, a
tree of floating-island worlds (**Domains**) connected by **Gates**. Think Anno /
early Paradox economy sim, fantasy setting, procedurally generated worlds, an
in-fiction Age of Exploration driven by opening links between Domains.

**Documentation split:** the **Notion wiki is the design overview** (premise,
concepts, glossary, decisions); see **Design source of truth** below. **Technical
detail lives in this repo**: this file for orientation, `docs/*.md` for specs.
When a task needs a design fact that is not written down, ask rather than
invent, and offer to log the answer in the Notion Decision Log.

---

## Engine & tooling

| | |
|---|---|
| Engine | Godot **4.7**, Forward+ renderer, Direct3D 12, **Jolt** physics |
| Scripting | **C#** (Godot .NET). `Project Nikitin.csproj` uses `Godot.NET.Sdk/4.7.2`, `net8.0`, nullable enabled, root namespace `ProjectNikitin`. Needs the .NET ("Mono") build of the editor. |
| Main scene | `res://scenes/main/main.tscn` |
| Platform | Windows. Shell is PowerShell; a Bash tool is also available. |

`.godot/` is generated and git-ignored; never edit or commit it. `*.uid`
sidecars are tracked.

### Building & running

The C# side builds standalone with `dotnet build "Project Nikitin.csproj"`; do
this after editing any `.cs`. Godot lives on the D: drive, off `PATH`:

```
D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
```

It runs headless, so the dev scenes can be executed from a shell and their
output read without a window. **`docs/dev-scenes.md`** is the manual for the
three of them: the island lab (F6 in the editor), the audit, and the checksum.
The two commands that matter after touching the generator:

```
godot --path . --headless scenes/dev/generation_checksum.tscn     # 0 of 448 islands moved?
godot --path . --headless --quit-after 2 scenes/dev/generation_audit.tscn   # the measured guarantees
```

Run both under a timeout (headless Godot does not always exit), and note this
machine prints decimals with a comma. To *look* at a shape headless, the audit's
`Gallery=<dir> GalleryShapes=Isthmus,Quarters` writes a contact sheet of sixteen
seeds per arrangement, captioned with the landmass count.

---

## Spatial model (from Notion → "The Ecumene")

- A **Domain** is a 3D landmass or archipelago of terrain units suspended in
  aether: flying islands, coarse scale (one unit's top face is an orchard or a
  housing compound). Gravity points −Y. Each Domain sits in an invisible bounding
  cube that keeps vessels in but does not block Gate travel.
- The terrain unit is a **slab**: a square cell 1 wide and **1/4 as tall**
  (`SLAB_HEIGHT = CELL_SIZE / 4`). Terrain Y is an integer slab index. The
  ratio is decided; the Notion wiki still says a tentative "8?".
- **Traversal:** a one-slab step (0.25 u) is free; a face of two or more slabs
  is an obstacle needing infrastructure. Terrain generated under a one-slab
  slope limit is walkable by construction; every cliff is one some rule put there.
  Walking is by king's moves: a corner is cut unless both cardinal cells beside
  the diagonal are cliffs. Works, anchors and water stay cardinal.
- **Five supported footprints: 48², 64², 72², 96², 128²** (128² is the stress
  target). Altitude is bounded by the same number in slabs, so the bounding
  cube is a real shape, and the landmass takes 55–85% of the grid's extent.
  30–40 Domains per game; up to four side Links per Domain, one Gate per edge.
- **Terrain is stored per column**, not as a voxel array: each `(x, z)` holds a
  short list of `Span(bottom, top)` solid runs. The air gap between two spans is
  an overhang or arch; branching caves are not supported. Never one node per
  slab: a 128² island is tens of thousands of columns, which is why the columnar
  model and a batched mesher exist.
- **Biome features** (forests, herds, coral, vines) are a separate layer that
  sits on, beside or under slab stacks. It does not exist yet.

### Code conventions

| Constant | Value | Meaning |
|---|---|---|
| `Terrain.CellSize` | `1.0` | X/Z size of one cell, in metres. |
| `Terrain.SlabHeight` | `0.25` | Y size of one slab. |
| Grid → world | `Vector3(gx * CellSize, gy * SlabHeight, gz * CellSize)` | `gy` is a slab index. |

Godot axes: **Y up**, right-handed, cameras look down −Z, 1 unit = 1 metre.

**`.tscn` `Transform3D` gotcha:** the text form serialises the basis row-major,
the transpose of the constructor. Do not hand-author rotated bases; use
translation-only transforms and orient cameras and lights in code (`LookAt`).

---

## Island generation

Full spec: **`docs/island-generation.md`**. Reasoning, things tried and removed,
the audit and the ideas not taken: **`docs/island-generation-appendix.md`**.

`IslandGenerator.Generate(seed, IslandParams)` is a pure function producing the
columnar `IslandData`; it re-rolls (from a derived seed) a Domain that comes out
unplayable. `IslandGenerator` is the orchestrator; each stage is a static class
under `scripts/generation/`, in the order they run:

| Stage | Class | What it settles |
|---|---|---|
| Footprint | `Footprint`, `Landmasses` | The land mask: lobes laid out per `IslandArrangement` (thirty shapes), bitten, huddled within bridge reach, fitted to 55–85% of the grid. |
| Regions | `Regions`, `Landforms` | A warped Voronoi of patches; each gets a `LandformType` (ten of them, by quota from the `TerrainCharacter`) and a rung on the plateau ladder. |
| Surface | `Relief`, `StepGrammar`, `Sculpting` | Relief under each landform's slope limit, settled to the free step; sculpted landforms, passes and canyons cut into it and exempted. |
| Standing water | `Lakes` | Lakes sunk into flat patches with their own rim as containment, shaped; goo puddles that never touch water. |
| Settle | `Beaches`, `Bridgeheads` | Beaches, then the lowering passes cycled until nothing moves. |
| Rivers | `Rivers` | Priority flood from the rim with noise-broken ties; beds, banks, valleys, navigable reaches as a stair of pools, fords, falls. |
| Keel | `Keel` | The underside; the columns are packed into `IslandData`. |
| Traversal | `Traversal` | Read-back: walk areas, reach areas (once built), water bodies, ferry berths, shelves. |
| Gates | `GatePlacement` | Four hanging Gates chosen as a set, one per edge; then subtraction to what was asked for. Levels its landing strips, so traversal runs again. |
| Roads | `Passages` | The least-works road from the Entry to each Exit. |
| Habitat | `Habitat`, `Surfaces`, `Names` | The five-byte habitat vector, the feature anchors and a provisional material per column, names. |
| Overhangs | `Overhangs` | The only stage that gives a column a second span; runs last because a lip is a roof, not ground. |

Shared: `Grid` (neighbourhoods; their order is a tie-breaker everywhere),
`SeedHash` (one mixer; the salt at each call site keeps rolls apart), `Flood`, `Terrain`, `FieldOps`, `Noise`.

**Two regression gates.** `generation_checksum.tscn` hashes every field of
`IslandData` for 448 islands against `docs/checksum-baseline.txt`: a change
meant to leave generation alone must report zero moved; one meant to change it
re-baselines with `-- accept` and says so. `generation_audit.tscn` prints the
measured guarantees and diffs thirty headline numbers against
`docs/audit-baseline.json`. Determinism hangs on details a refactor can break
silently: hash salts, `Noise` seed offsets, float expression order, scan and
neighbour order, `List.Sort` (unstable) versus `OrderBy`, and dictionary
insertion order. When in doubt, run the checksum.

Newer content ships behind a toggle that takes it out of `Auto`'s dice without
taking it out of the code (`NewArrangements`, `NewLandforms`).

---

## Repository layout

```
project.godot                  Engine config. run/main_scene points at main.tscn.
Project Nikitin.csproj / .sln   .NET project (Godot.NET.Sdk 4.7.2, net8.0).
scenes/
  main/main.tscn               Single-slab viewer: environment, sun, camera rig, one slab.
  terrain/grass_block.tscn     Prototype terrain slab (1 × 0.25 × 1).
  dev/island_lab.tscn          Island generation harness (see docs/dev-scenes.md).
  dev/generation_audit.tscn    Headless guarantee audit.
  dev/generation_checksum.tscn Headless bit-for-bit checksum.
scripts/
  CameraRig.cs                 Strategy camera: pan / yaw / pitch / zoom, LookAt-aimed.
  generation/                  Namespace ProjectNikitin.Generation
    IslandGenerator.cs         Generate(seed, params): the stages in order, the re-roll.
    Footprint.cs, Landmasses.cs, Bridgeheads.cs, Regions.cs, Landforms.cs,
    Relief.cs, StepGrammar.cs, Sculpting.cs, Beaches.cs, Lakes.cs, Keel.cs,
    Roster.cs                  The terrain stages (see the table above).
    Rivers*.cs                 Drainage routing, channels, valleys, profile, falls, fords.
    Traversal*.cs, WalkArea.cs, Shelf.cs, Crossing.cs, Ferry.cs, BridgeEase.cs
                               The read-back analysis and its value types.
    Passage.cs, Works.cs       The roads between the Gates.
    Gate.cs, GatePlacement.cs, GateSites.cs
    Habitat.cs, Surfaces.cs, SurfaceMaterial.cs, Names.cs, Overhangs.cs
    IslandData.cs, IslandParams.cs, Span.cs, Terrain.cs
    LandformType.cs, TerrainCharacter.cs, ReliefStyle.cs, IslandArrangement.cs,
    FluidKind.cs, Geyser.cs, Fall.cs, RegionPlan.cs
    Grid.cs, SeedHash.cs, Flood.cs, Noise.cs, FieldOps.cs
  dev/
    IslandLab*.cs              The lab.
    GenerationAudit*.cs        The audit.
    GenerationChecksum.cs      The checksum.
    DevPalette.cs, TinyFont.cs The shared colours, and a 5x7 bitmap font so a
                               headless PNG can carry its own labels.
resources/island_default.tres  The IslandParams preset all three dev scenes load.
docs/
  island-generation.md         The generation spec.
  island-generation-appendix.md  Why, what was tried, the audit, the ideas.
  dev-scenes.md                The lab, audit and checksum manual.
  audit-baseline.json          The last accepted audit numbers.
  checksum-baseline.txt        The last accepted island hashes.
CLAUDE.md                      This file.
```

Planned, create as needed and keep the tree shallow: `scripts/terrain/` for the
chunked span-aware mesher, `resources/` for biome, archetype and goods data,
`addons/` for plugins.

### Naming

- Scenes, `.tscn`/`.tres` and their folders: `snake_case`.
- C# files `PascalCase`, one type per file, file name = type name; a class split
  across files uses `Name.Part.cs`. Namespace `ProjectNikitin` or a sub-namespace.
- Use the design vocabulary in code: `Domain`, `Slab`, `Gate`, `Link`, `Polity`,
  `Settlement`, `Essence`, not "block", "portal", "faction", "town".

---

## Glossary (condensed from Notion)

- **Ecumene**: the whole game world, the tree of Domains. **Domain**: one
  floating landmass or archipelago. The **Home Domain** is where the player starts.
- **Aether**: the space between Domains; hazardous to people.
- **Link**: a fast, safe route through aether joining two Domains. **Gate**: the
  built structure at each end. Links form a tree. A **hanging Gate** floats five
  cells off the rim and is flown through, so the Domain owes it a 1 × 3 landing
  strip running inland; this is the normal case. A **land Gate** is the same
  site with the portal on the strip, walked through. A Link joins two Gates of
  the same kind. One Gate per edge: one Entry, one to three Exits.
- **Slab**: the terrain unit, 1 × 1 × 0.25. **Biome**: a Domain's flora, fauna
  and climate.
- **Polity**: an NPC state ruling Domains. **Metropole**: the Polity the player
  answers to. **Cultural Archetype**: a people's template (Steelfolk, Lakefolk,
  Jadefolk), carrying Traits: School of Magicks, Societal Structure, Political
  Situation, Means of Extraction.
- **Class / Role / Prestige**: population stratification; Role gates employment,
  Prestige gates promotion and consumption.
- **Magicks**: the magic system. **Essence**: the refined magical resource,
  provisionally also the currency. **Means of Extraction**: how a Domain refines
  Essence early on.
- **Settlement**: the basic economic unit: market, warehouses, districts,
  facilities and land. **Needs**: Food, Intoxicants, Clothing, Wares, modulated
  by Habits, Sophistication, Pickiness and Fashion.
- **Player Avatar**: the on-map character, a mobile order relay. **Pioneers /
  Aethernaut / Aethership**: expedition crew, scout and vessel. **Aspiration**:
  the run's win condition.

---

## Design source of truth — Notion

Wiki database **"🪙 Project Nikitin"** (Notion MCP connector).

| Page | State | Notes |
|---|---|---|
| Premise and Vision | written | What the game is and why. |
| The Ecumene | written | Domains, slabs, Links, Gates, scale. Read before terrain work. |
| Mechanics and Concepts | index | Parent of the mechanics pages. |
| The Gameplay Loop → The First Hour | written | Best description of moment-to-moment play. |
| Economy, Population and Settlements | draft | Settlements, classes, Needs, money. |
| Generation → Island Generation | short | Requirements checklist for island generation. |
| Terrain, Polities, Magicks, Lore, Content | stubs | |
| Glossary | partial | |
| Decision Log | DB, near-empty | Log firm decisions here, with the why and the alternatives. |
| Open Questions | DB | Unresolved design questions. |
| Production Tasks → Tasks | DB | Roadmap: Prototype 0 → Prototype 1 → Vertical slice → Later. |
| Journal for Thoughts and Bits | DB | Loose ideas not yet promoted. |

Consult the relevant page before non-trivial design work. When a decision gets
made in a session, offer to add it to the Decision Log and to close the matching
Open Question. Two decisions are made but not yet logged there: the slab's 1:4
ratio, and the five supported footprints (the Ecumene page still says 16³–64³).

---

## Roadmap

- **Prototype 0**: dev environment (git, Godot, Claude, VS Code). Done; the repo
  is at `yeagore/project-nikitin`.
- **Render an island**, branch `island-generation`, PR
  [#2](https://github.com/yeagore/project-nikitin/pull/2). Every generation
  stage is done and audited at all five footprints. What is next, in rough
  order, is in `docs/island-generation.md` §6: the chunked span-aware mesher and
  colliders (the only thing that will answer the performance question), settlement
  placement, the biome layer above `Material`, and span-aware pathing.

---

## Open questions

Flagged so they are not silently hard-coded:

1. **Essence as currency.** Provisional; expect grades of Essence or per-Polity
   currencies later.
2. **Domains loaded at once.** Whether only the active Domain is simulated and
   rendered, or several. Drives the streaming and LOD approach.
3. **Camera.** `CameraRig` pans, yaws, pitches and wheel-zooms, aimed with
   `LookAt`; it polls physical keys. Undesigned: edge-scroll, orthographic, pan
   bounds, an InputMap.
4. **Domain size ladder.** Two candidate ladders (64/96/128 and 48/72/96) are
   overlaid in the five supported footprints until Maxim picks one.
