# Project Nikitin

A single-player economic/exploration strategy game built in **Godot 4.7**. The
player is a merchant-pioneer running a trading company across the **Ecumene**, a
tree of floating-island worlds (**Domains**) connected by **Gates**. Think Anno /
early Paradox economy sim, fantasy setting, procedurally generated worlds, an
in-fiction Age of Exploration driven by opening links between Domains.

**Documentation split:** the **Notion wiki is the design overview** (premise,
concepts, glossary, decisions); see **Design source of truth** below. **Technical
detail lives in this repo**: this file for orientation, `docs/*.md` for specs.
`docs/island-generation-plain.md` is a plain-language retelling for Maxim: not
a source for you, but a document you owe an update to whenever a change alters
what the spec or the dev-scenes manual say.
When a task needs a design fact that is not written down, ask rather than
invent, and offer to log the answer in the Notion Decision Log.

---

## Engine & tooling

| | |
|---|---|
| Engine | Godot **4.7**, Forward+ renderer, Direct3D 12, **Jolt** physics |
| Scripting | **C#** (Godot .NET). `Project Nikitin.csproj` uses `Godot.NET.Sdk/4.7.2`, `net8.0`, nullable enabled, root namespace `ProjectNikitin`. Needs the .NET ("Mono") build of the editor. |
| Main scene | `res://scenes/main/main.tscn` |
| Platform | Two machines: a Mac (zsh; Godot at `/Applications/Godot_mono.app`) and a Windows box (PowerShell; Godot on `D:`). The checksum reproduces bit-for-bit across both. |

`.godot/` is generated and git-ignored; never edit or commit it. `*.uid`
sidecars are tracked.

### Building & running

The C# side builds standalone with `dotnet build "Project Nikitin.csproj"`; do
this after editing any `.cs`. Godot is off `PATH` on both machines:

```
/Applications/Godot_mono.app/Contents/MacOS/Godot                          # macOS
D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe   # Windows
```

It runs headless, so the dev scenes can be executed from a shell and their
output read without a window. **`docs/dev-scenes.md`** is the manual for the
five of them: the island lab (F6 in the editor), the audit, the checksum, the
mesh bench, and the Domains bench. The two commands that matter after touching
the generator, and the two after touching the renderer:

```
godot --path . --headless scenes/dev/generation_checksum.tscn     # 0 of 446 islands moved?
godot --path . --headless --quit-after 2 scenes/dev/generation_audit.tscn   # the measured guarantees
godot --path . --headless scenes/dev/mesh_bench.tscn              # triangles, times, the winding probe, the voxel oracle, the colliders
godot --path . scenes/dev/domains_bench.tscn -- domains=20        # windowed: the frame rate with N Domains in view
```

Run the first two under a timeout (headless Godot does not always exit; macOS
has no `timeout`, use `perl -e 'alarm 900; exec @ARGV' <godot> ...`), and note
the Windows machine prints decimals with a comma. The headless runs are separate
processes and can run at once. To *look* at a shape headless, the audit's
`Gallery=<dir> GalleryShapes=Isthmus,Quarters` writes a contact sheet of sixteen
seeds per arrangement, captioned with the landmass count. To look at the
*rendered* island without a hand on the keys, the lab takes a screenshot from a
shell and quits: `godot --path . scenes/dev/island_lab.tscn -- shot nopanel
zoom=4` (windowed, since a screenshot needs a viewport; a window opens for a
few seconds on the machine it runs on).

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
- **Three supported footprints: 64², 96², 128²** (128² is the stress target;
  48² and 72² were dropped on 2026-09-05, 48² because the footprint constants
  measured in cells wreck the split shapes there, 72² with the ladder it sat
  on). Altitude is bounded by the same number in slabs, so the bounding
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
| Footprint | `Footprint`, `Landmasses` | The land mask: lobes laid out per `IslandArrangement` (thirty shapes), bitten, huddled within bridge reach, fitted to 55–85% of the grid; two or three of the specks dropped as too small kept as sea stacks (aether, an anchor list). |
| Regions | `Regions`, `Landforms` | A warped Voronoi of patches; each gets a `LandformType` (ten of them, by quota from the `TerrainCharacter`) and a rung on the plateau ladder. |
| Surface | `Relief`, `StepGrammar`, `Sculpting` | Relief under each landform's slope limit, settled to the free step; sculpted landforms, passes and canyons cut into it and exempted. |
| Standing water | `Lakes` | Lakes sunk into flat patches with their own rim as containment, shaped; goo puddles that never touch water. |
| Settle | `Beaches`, `Bridgeheads` | Beaches, then the lowering passes cycled until nothing moves. |
| Rivers | `Rivers` | Priority flood from the rim with noise-broken ties; beds, banks, valleys, navigable reaches as a stair of pools, fords spaced by the ground's relief, falls, springs; occasionally a lake that swallows a river, and a delta where a navigable river meets a gentle coast. |
| Keel | `Keel` | The underside; the columns are packed into `IslandData`. |
| Traversal | `Traversal` | Read-back: walk areas (a district — walk-connected, no works — is somewhere to build), reach areas (once built), water bodies, ferry berths. Shelves are gone. |
| Gates | `GatePlacement` | Four hanging Gates chosen as a set, one per edge; then subtraction to what was asked for. Levels its landing strips, so traversal runs again. |
| Roads | `Passages` | The least-works road from the Entry to each Exit. |
| Habitat | `Habitat`, `Surfaces`, `Names` | The six-byte habitat vector: moisture (the wind's rain shadow, damp sheltered gorges, the water strip), warmth (a lapse per mountain from its own foot, a rolled sun on the slopes, frost hollows, the milder lee), ruggedness, exposure, rim distance and water distance; the wind knob scales what exposure moves. On a cold Domain some springs and pools run hot, with a bloom of warmth round each. Then the feature anchors and a provisional material per column (a four-by-three climate grid with heath and verdure, bog on the cold-to-cool half and marsh on the warm-to-hot, tors in soft country, floodplain on a delta), names. |
| Magicks | `Magicks` | The magickal density byte, grown rather than sampled: a Turing reaction (Gray–Scott) between the magick, which makes more of itself, and the inhibitor it feeds on, which is replenished everywhere and spreads faster — so the field breaks into spots, worms, mazes or lace instead of settling flat. Its coefficients are not knobs: the settings that pattern at all are islands in a sea of dead and flooded ones, so six of them are named as `MagickPattern` (motes, wells, veins, labyrinth, lace, hollows) and the stage shows two parameters — which pattern, and `MagickDensity`, how much magick the Domain holds. The reaction runs on its own lattice, three ground cells to the side, and is enlarged back onto the columns, so a feature is three cells across for every cell it would have been — magick is a place, not a texture. Read by nothing. |
| Overhangs | `Overhangs` | The only stage that gives a column a second span; runs last because a lip is a roof, not ground. |

Shared: `Grid` (neighbourhoods; their order is a tie-breaker everywhere),
`SeedHash` (one mixer; the salt at each call site keeps rolls apart), `Flood`, `Terrain`, `FieldOps`, `Noise`.

**Auto knobs.** The eleven 0–1 knobs in `IslandParams` (relief, hilliness, mix,
rivers, lakes, valleys, moisture, warmth, wind, overhang density, magick density)
accept `IslandParams.Auto` (any negative value); `Roster.ResolveKnobs`
then rolls them from the seed before anything runs, and the values used are
`IslandData.Settings`. `MagickPattern` is resolved there too, `Auto` picking one
of the six evenly — it is a named point on the reaction's plane, not a number to
roll over a range. The preset leaves all of them on Auto, so the audit's default
seeds sample the whole knob space; a sweep pins the knob it sweeps.

**Two regression gates.** `generation_checksum.tscn` hashes every field of
`IslandData` for 446 islands against `docs/checksum-baseline.txt`: a change
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

## Rendering

Spec: **`docs/island-generation.md` §4**. Code under `scripts/terrain/`,
namespace **`ProjectNikitin.Meshing`**, not `.Terrain`: a namespace of that name
would shadow the `Terrain` constants class for every file under
`ProjectNikitin`.

`IslandRenderer` is the terrain renderer: a `Node3D` that draws an `IslandData`
as `TerrainChunk`s of 16 × 16 columns, each a `StaticBody3D` holding a ground
`ArrayMesh`, a liquid `ArrayMesh` (water and goo as two surfaces) and a trimesh
collider over the ground. `ChunkMesher` is the pure part: per column per span it
emits the top at `Top + 1`, the underside at `Bottom` and a side wherever the
neighbouring column's spans do not fill that slab range, merged over the range
so a cliff is one quad; water gets its top at `WaterLevel + 1` and a wall
wherever it meets anything that is neither solid nor the same water, which is
what a fall and a cataract are. Nothing buried is emitted, and the bench's voxel
oracle checks that to 0.000 m². Vertices are flat-shaded quads with a normal, a
UV in metres, UV2 = (material or fluid byte, `FaceKind`) for a shader to read,
and a colour from an `IslandTint`: two callbacks the lab swaps per view and the
game leaves at `IslandTint.Default` (the column's `SurfaceMaterial` through
`SurfacePalette`, stone for a lip and every underside). `TerrainMaterials` holds
the three materials; the lab's boxes use the same factories. In the renderer's
local space cell (x, z) is centred on `(x · CellSize, ·, z · CellSize)`, the grid
→ world rule above. `Show(data)` builds everything; `RebuildAround(x, z)`
remeshes the chunk holding a column and the neighbours its border faces depend
on, the hook a build or a terraform will call.

Measured by `mesh_bench.tscn` on the Mac: a 128² island is about 50,000 ground
triangles (62% of what the boxes drew) meshed in 9 ms, with meshes, colliders
and nodes in another 40 ms, about 6 MB. Triangles were never the cost; the
performance question the mesher was to answer is answered, yes with room to
spare. Greedy merging of coplanar faces is not done and not needed. `main.tscn`
(F5) shows one generated Domain through the renderer (`Main.cs`: N for a new
seed, F to frame); the lab draws through it too, Z for the old boxes, and reads
the column under the cursor off the colliders with a ray (`IslandLab.Pick.cs`),
the pattern a settlement placer's cell pick will follow. The bench casts rays
at every third column from above and below and expects the top and the keel.

**Many Domains.** `godot --path . scenes/dev/domains_bench.tscn -- domains=20`
(windowed) lays out N Domains on consecutive seeds in a grid a quarter footprint
apart, frames them all, and after six seconds with vsync off prints the frame
rate, draw calls, primitives, the render thread's CPU time and memory, then
quits (the GPU time
reads 0 on Metal; past 150 Domains it builds no colliders, since Jolt's default
cap of 10,240 bodies is 160 Domains × 64 chunk bodies, a project setting).
Measured on the Mac (M2, 16 GB, a 4K display) on 2026-09-06 with every Domain
in view: 1, 20, 40 and 80 Domains all hold the display's 120 Hz; 80 is 4,173
draw calls and 3.0 million triangles. The knee is between 80 and 160: 160
Domains (8,300 draw calls, 6.1 million triangles) run at 65 fps, 320 at 33,
640 at 17, the frame time growing about 0.1 ms per Domain in view with draw
submission about 0.65 µs a call. Per Domain: about 52 draw calls, 50,000
triangles, 3.5 MB of video memory, 1 MB of data and 2 MB of collider, over a
170 MB engine baseline. Rendering the terrain of twenty Domains is not the
constraint; generating them is 3.3 s for twenty on one thread at load, and
`Generate` is pure, so that parallelises. The budget the biome layer inherits
with one Domain in view is some 4 million triangles a frame at 120 Hz on this
machine, on two conditions: features are drawn by instancing (`MultiMesh`),
never a node or a draw call per tree, and the directional shadow's cascades,
which multiply geometry cost, are the first knob if it is ever needed.

---

## Repository layout

```
project.godot                  Engine config. run/main_scene points at main.tscn.
Project Nikitin.csproj / .sln   .NET project (Godot.NET.Sdk 4.7.2, net8.0).
scenes/
  main/main.tscn               The game scene: one generated Domain through IslandRenderer.
  dev/island_lab.tscn          Island generation harness (see docs/dev-scenes.md).
  dev/generation_audit.tscn    Headless guarantee audit.
  dev/generation_checksum.tscn Headless bit-for-bit checksum.
  dev/mesh_bench.tscn          Headless mesher measure: triangles, times, winding probe, voxel oracle, colliders.
  dev/domains_bench.tscn       Windowed: N Domains in view, the frame rate.
scripts/
  Main.cs                      The game scene's script: generate, show, frame.
  CameraRig.cs                 Strategy camera: pan / yaw / pitch / zoom, LookAt-aimed.
  terrain/                     Namespace ProjectNikitin.Meshing (see Rendering)
    IslandRenderer.cs          The terrain renderer: the chunk grid; Show and RebuildAround.
    TerrainChunk.cs            One 16 × 16 tile: ground mesh, liquid mesh, trimesh collider.
    ChunkMesher.cs             The pure mesher: the exposed faces of one chunk.
    MeshBuffer.cs              Quads into ArrayMesh arrays and collider faces; the winding rule.
    IslandTint.cs, FaceKind.cs, SurfacePalette.cs, TerrainMaterials.cs
                               Colour per face, which side a face is, the provisional
                               material palette, the materials.
  generation/                  Namespace ProjectNikitin.Generation
    IslandGenerator.cs         Generate(seed, params): the stages in order, the re-roll.
    Footprint.cs, Landmasses.cs, Bridgeheads.cs, Regions.cs, Landforms.cs,
    Relief.cs, StepGrammar.cs, Sculpting.cs, Beaches.cs, Lakes.cs, Keel.cs,
    Roster.cs                  The terrain stages (see the table above).
    Rivers*.cs                 Drainage routing, channels, valleys, profile, falls, fords,
                               deltas and springs, the lake that swallows a river.
    Traversal*.cs, WalkArea.cs, Crossing.cs, Ferry.cs, BridgeEase.cs
                               The read-back analysis and its value types.
    Passage.cs, Works.cs       The roads between the Gates.
    Gate.cs, GatePlacement.cs, GateSites.cs
    Habitat.cs, Magicks.cs, MagickPattern.cs, Surfaces.cs, SurfaceMaterial.cs,
    Names.cs, Overhangs.cs
    IslandData.cs, IslandParams.cs, Span.cs, Terrain.cs
    LandformType.cs, TerrainCharacter.cs, ReliefStyle.cs, IslandArrangement.cs,
    FluidKind.cs, Geyser.cs, Fall.cs, RegionPlan.cs
    Grid.cs, SeedHash.cs, Flood.cs, Noise.cs, FieldOps.cs
  dev/
    IslandLab*.cs              The lab.
    GenerationAudit*.cs        The audit.
    GenerationChecksum.cs      The checksum.
    MeshBench*.cs, DomainsBench.cs
                               The mesh bench and the Domains bench.
    DevPalette.cs, TinyFont.cs The shared colours, and a 5x7 bitmap font so a
                               headless PNG can carry its own labels.
resources/island_default.tres  The IslandParams preset every dev scene and the game scene load.
docs/
  island-generation.md         The generation spec, and the renderer in §4.
  island-generation-appendix.md  Why, what was tried, the audit, the ideas.
  island-generation-plain.md   The spec, appendix and manual retold in plain words,
                               for Maxim. Do not read it for orientation (the spec
                               is the source); do keep it true when the generator
                               or the audit changes, in the same plain register.
  dev-scenes.md                The lab, audit, checksum and mesh bench manual.
  audit-baseline.json          The last accepted audit numbers.
  checksum-baseline.txt        The last accepted island hashes.
CLAUDE.md                      This file.
```

Planned, create as needed and keep the tree shallow: `resources/` for biome,
archetype and goods data, `addons/` for plugins.

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
ratio, and the three supported footprints (the Ecumene page still says 16³–64³).

---

## Roadmap

- **Prototype 0**: dev environment (git, Godot, Claude, VS Code). Done; the repo
  is at `yeagore/project-nikitin`.
- **Render an island**, branch `island-generation`, merged in PRs
  [#1](https://github.com/yeagore/project-nikitin/pull/1),
  [#3](https://github.com/yeagore/project-nikitin/pull/3),
  [#4](https://github.com/yeagore/project-nikitin/pull/4),
  [#5](https://github.com/yeagore/project-nikitin/pull/5) (the magick layer) and
  [#7](https://github.com/yeagore/project-nikitin/pull/7). Every generation
  stage is done and audited at all three footprints.
- **The mesher**, branch `mesher`, PR
  [#6](https://github.com/yeagore/project-nikitin/pull/6): the chunked
  span-aware renderer with colliders, drawing the main scene and the lab, with
  its two benches and the lab's cursor pick as the colliders' first reader.
  Built, measured and checked; the performance question is answered.
- What comes after, in rough order, is in `docs/island-generation.md` §6:
  settlement placement, the biome layer above `Material` (which is also where
  the ground gets a look beyond flat colours), and span-aware pathing.

---

## Open questions

Flagged so they are not silently hard-coded:

1. **Essence as currency.** Provisional; expect grades of Essence or per-Polity
   currencies later.
2. **Domains loaded at once.** Whether only the active Domain is simulated and
   rendered, or several. Drives the streaming and LOD approach. The renderer
   does not constrain it: forty 128² Domains in view hold 120 Hz (see
   Rendering); generation time and the simulation are what would.
3. **Camera.** `CameraRig` pans, yaws, pitches and wheel-zooms, aimed with
   `LookAt`; it polls physical keys. Undesigned: edge-scroll, orthographic, pan
   bounds, an InputMap.
4. ~~**Domain size ladder.**~~ Decided 2026-09-05: 64 / 96 / 128. Not yet in
   the Notion Decision Log.
