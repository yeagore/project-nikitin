# Project Nikitin

A single-player economic/exploration strategy game built in **Godot 4.7**. The
player is a merchant-pioneer running a trading company across the **Ecumene** — a
tree of floating-island worlds (**Domains**) connected by **Gates**. Think Anno /
early Paradox economy sim, fantasy setting, procedurally generated worlds, an
in-fiction Age of Exploration driven by opening links between Domains.

**Documentation split:** the **Notion wiki is the general design overview**
(premise, concepts, glossary, decisions) — see **Design source of truth** below.
**Technical detail and specs live in this repo** — this file for orientation,
`docs/*.md` for longer specs. Write technical write-ups locally, not in Notion.
Much of the wiki is still stubs; when a task needs a design fact that isn't
written down, ask rather than invent — and offer to log the answer in the Notion
Decision Log.

---

## Engine & tooling

| | |
|---|---|
| Engine | Godot **4.7**, Forward+ renderer |
| Graphics API | Direct3D 12 (`rendering_device/driver.windows="d3d12"`) |
| Physics | **Jolt** (`3d/physics_engine="Jolt Physics"`) |
| Scripting | **C#** (Godot .NET). `Project Nikitin.csproj` uses `Godot.NET.Sdk/4.7.0`, `net8.0`, nullable enabled, root namespace `ProjectNikitin`. `Project Nikitin.sln` is committed for IDE tooling. Requires the .NET-enabled ("Mono") build of the Godot editor. |
| Editor tooling | VS Code (per the Notion task list) |
| Main scene | `res://scenes/main/main.tscn` |
| Platform | Windows 11. Shell is PowerShell; a Bash tool is also available. |

`.godot/` is generated (import cache, shader cache) and git-ignored — never edit
or commit it.

### Building & running

The C# side builds standalone: `dotnet build "Project Nikitin.csproj"`. The SDK
is **10.0.400**, which builds the project's `net8.0` target fine; the
`Godot.NET.Sdk` NuGet restores from nuget.org. Do this after editing any `.cs` to
catch compile errors without the editor.

**Godot lives on the D: drive, not on `PATH`:**

```
D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe
D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
```

Use the `_console` variant from a shell — it writes to stdout. Quote the path.
**It runs headless**, so scenes can be executed and their `GD.Print` output read
without a window:

```
godot --path . --editor                              # open the project (also builds C#)
godot --path . --headless --build-solutions --quit   # build C# + import, no window
godot --path . --headless --quit-after 2 scenes/dev/generation_audit.tscn
```

Two gotchas: headless Godot often does **not** exit on `--quit-after`, so run it
with a timeout and kill it rather than blocking; and this machine's locale prints
decimals with a comma (`1,02`), which breaks naive `grep`/`Select-String` patterns
looking for `\.`.

Headless gives no rendering — numbers can be verified this way, **appearance
cannot**. Anything about how terrain *looks* still needs a human at the editor.

---

## Spatial model (from Notion → "The Ecumene")

This is the part that governs terrain, generation, and rendering code.

- A **Domain** is a 3D landmass (or archipelago) of terrain units, magically
  suspended in aether. Visually: flying islands, like Skyblock in Minecraft, but
  the scale is coarser — **one unit's top face ≈ an orchard or a housing
  compound**, not a person.
- The terrain unit is a **slab**: a square cell **1 wide/long and 1/4 as tall**
  (`SLAB_HEIGHT = CELL_SIZE / 4`). The 1:4 ratio is decided (the Notion wiki says
  a tentative "8?"); it lets terrain express hills and gentle grades in 0.25-unit
  steps instead of only sheer cliffs. Not yet reflected in Notion.
- **Traversal:** a **one-slab** step (0.25 u) is free; a face of two or more
  slabs is an obstacle needing infrastructure. So a noise surface that rises ≤1
  slab per cell is walkable everywhere; cliffs form at coastlines and at terrace
  faces.
- **Gravity** always points down (−Y) by default.
- Each Domain sits inside an **invisible bounding cube** that keeps vessels from
  drifting off; it does not block Gate travel.
- **Biome features** (forests, herds, coral/essencercoral growths, vines, fungal
  mats) are structures that sit *on top of, on the sides of, or underneath* slab
  stacks. They are a separate layer from the slabs themselves.
- Domain size: working target **128×128** cells footprint (position: vasin; the
  Notion "Ecumene" page still says 16³–64³, and Maxim favours smaller — decision
  not yet logged). 30–40 Domains per game, laid out on a plane by their position
  in the world-tree (a Domain linked "north" is found by scrolling north). Up to
  4 side Links per Domain now (maybe 6 — incl. top/bottom — later).
- **Terrain is stored per column, not as a 3D voxel array.** Each `(x,z)` holds a
  short list of `Span(bottom, top)` solid runs, bounds as **slab indices**; the
  air gap between two spans is an overhang / arch. Whole island resident, no
  per-slab storage. Overhangs and arches are supported; branching caves/tunnels
  are not. See `docs/island-generation.md`.
- Performance: per-node-per-slab is impossible (a 128² island is tens of
  thousands of columns, each many slabs deep), which is why the columnar model +
  a batched mesher exist. Treat a full 128² footprint as the stress target.

### Code conventions derived from the above

These are set here so every session stays consistent. Change them in one place.

| Constant | Value | Meaning |
|---|---|---|
| `CELL_SIZE` | `1.0` | X/Z size of one cell, in Godot units (metres). In fiction one cell is ~an orchard. |
| `SLAB_HEIGHT` | `0.25` | Y size of one slab = `CELL_SIZE / 4`. Terrain Y is an integer slab index. |
| Grid → world | `Vector3(gx * CELL_SIZE, gy * SLAB_HEIGHT, gz * CELL_SIZE)` | `gx, gy, gz` integers; `gy` is a slab index. |

Defined in code as `ProjectNikitin.Generation.Terrain.CellSize` / `.SlabHeight`.

Godot axis conventions (unchanged): **Y up**, right-handed, cameras look down
**−Z**, `1 unit = 1 metre`.

**`.tscn` `Transform3D` gotcha:** the text form serializes the basis
**row-major** — the transpose of the `Transform3D(xAxis, yAxis, zAxis, origin)`
constructor. Do not hand-author rotated bases into scene files; use identity
(translation-only) transforms and orient cameras/lights in code (`LookAt`) or in
the editor. `scripts/CameraRig.cs` aims itself with `LookAt` for this reason.

### Rendering an island (the current epic)

Full spec: **`docs/island-generation.md`** — data model, generation pipeline,
parameters, rendering handoff, first implementation slice. Being built on the
`island-generation` branch.

In short: generation is a pure function `Generate(seed, IslandParams)` producing
the columnar `IslandData` (Y in slab indices); a separate chunked mesher turns
that into per-chunk `ArrayMesh` + trimesh colliders (only exposed faces).
**Never one node per slab.**

Current state — the island is a **blanket of landform patches**, not a quantised
height field. Regions come from a warped Voronoi; each gets a `LandformType`
(Plain, Hills, Mountain, Mesa, Basin) and a rung on a plateau ladder, and relief
is generated under that landform's slope limit. This is what keeps steps
meaningful: a 1-slab step is free, so terrain is walkable by default and cliffs
only appear where they were decided — audited, and **every cliff in 60 islands is
now one the rules allow**. Mountains take no rung — they rise on an S-curve from
the ground they actually meet. Lakes sink into a patch's interior, the untouched
rim being what holds the water.

Landforms are handed out **by quota, not by per-region dice**: every landform a
`TerrainCharacter` names is guaranteed to appear, and `LandformMix` slides the
proportions from low ground to high. `ReliefStyle` is internal and follows from
the character.

The footprint is a set of **placed blobs**, one per landmass, chosen by
`IslandArrangement` — Single, Satellites, Twins, Triplets, Archipelago, Atoll.
Whatever the layout, the pieces are nudged together until each faces the next
across at most two cells, so **every arrangement is linkable by bridge**.

`Traversal.Analyse` then reads the finished terrain back: which ground connects
to which on foot (`WalkArea`), which connects **once stairs and bridges are
built** (`Reach` / heartland), and which is flat and wide enough to build on
(`Shelf`). It changes nothing — it is how we find out whether the island is
playable. The answer: 55% of land is walkable from the mainland, **93% is
reachable with infrastructure**, and 90% of what stays out is mountain. A cliff
is a cost, not a wall.

`Rivers` routes drainage by a priority flood inward from the rim, so water
crosses lakes rather than stopping at them. Sources are named — every summit and
lake outflow — because accumulation alone gives nothing on slope-limited terrain.
A stream is one slab deep and **fordable**; a navigable river is two cells wide
and is not. **Every river reaches the rim and pours off it**, because there is no
sea.

`GatePlacement` puts one Entry and one to three Exit Gates on the Domain, at most
one per edge. A Gate is 3 × 1 cells and 12 slabs tall. Land Gates stand on the
ground; hanging Gates float off the rim and need a landing strip opposite. The
**Entry's kind is an input** (`IslandParams.EntryGate`) because a Link joins two
Gates and they must match.

The dev lab (`scenes/dev/island_lab.tscn`) draws one scaled `MultiMesh` box per
span, flat quads for water, and a box per Gate. Still to come: the chunked
mesher, overhangs (stage 4b), the remaining feature anchors, settlement
guarantees.

**Launching the island lab:**

1. Open the project in the .NET Godot editor; build C# (hammer icon, or
   `dotnet build "Project Nikitin.csproj"`).
2. In the FileSystem dock open `scenes/dev/island_lab.tscn`, then press **F6**
   ("Run Current Scene"). It is not the project's main scene, so F5 won't run it.
3. Camera: **WASD** move, **Q/E** or middle-drag rotate, middle-drag or **up/down
   arrows** tilt (far enough to look up at the keel), **wheel** zoom, **Shift**
   faster. **N** new seed, **V** cycle `TerrainCharacter`, **G** cycle
   `IslandArrangement`, **H** cycle `Hilliness`, **M** cycle `LandformMix`,
   **C** cycle view, **F** re-frame, **R** rebuild the same island.
4. Views on **C**: `height` / `landform` (passes tinted pale yellow) / `region` /
   **`walk`** (what connects on foot — mainland green, other districts their own
   hue, all broken ground one grey) / **`reach`** (what connects once you build —
   green heartland, red for what building cannot reach, which should be mountain
   and little else) / **`shelves`** (flat ground; dim brown is flat but too small
   or too narrow to build on). Gates draw as portals in every view: **gold** for
   the entry, **cyan** for exits, pale for hanging ones. The status line names the
   character, arrangement, high-ground style, seed, lakes, rivers, falls, passes,
   the walk/shelf tally, and every Gate.

**Where the numbers live.** `resources/island_default.tres` is the `IslandParams`
preset, and **both** the lab and the audit scene load it — so the audit measures
the island you are tuning. Two ways to change it:

- **Durably:** select the `.tres` in the FileSystem dock and edit it in the
  Inspector. Saved to disk, picked up by the next run and by the audit.
- **Throwaway:** with the lab *running*, use the **Remote** tab of the Scene dock,
  select `IslandLab`, and edit `Seed` or the fields inside `Params`. The island
  rebuilds on every change and nothing is written to disk. This is the one to use
  while you are hunting for a look.

CLI: `godot --path . scenes/dev/island_lab.tscn`

**Checking generation without looking at it:**
`scenes/dev/generation_audit.tscn` runs the real generator over 60 seeds headless
and prints the measured guarantees — step grammar, cliff borders, patch sizes,
mesa/basin clearance, mountain profile, lake containment, continuity. Run it after
any change to `IslandGenerator`. See `docs/island-generation.md` §4d, which also
lists the gaps the last audit found.

`scenes/terrain/grass_block.tscn` predates the slab decision — it is still a
1×1×1 cube and should be reshaped to a 1×0.25×1 slab (and `main.tscn`'s camera
pivot adjusted from 0.5 to 0.125). Low priority; not part of the generation work.

---

## Repository layout

```
project.godot                  Engine config. run/main_scene points at main.tscn.
Project Nikitin.csproj / .sln   .NET project (Godot.NET.Sdk 4.7.0, net8.0).
icon.svg                       Default project icon (placeholder).
scenes/
  main/main.tscn               Entry scene: WorldEnvironment + sun + camera rig + one block.
  terrain/grass_block.tscn     Prototype grass-topped terrain block.
scripts/
  CameraRig.cs                  Strategy camera: pan / yaw / wheel-zoom, LookAt-aimed.
  generation/                   Namespace ProjectNikitin.Generation
    Terrain.cs                  CellSize / SlabHeight constants.
    Span.cs, IslandData.cs      Per-column span-list terrain model (+ water, regions).
    IslandParams.cs             [GlobalClass] generator inputs.
    LandformType.cs             Plain / Hills / Mountain / Mesa / Basin.
    TerrainCharacter.cs         Which landforms an island is built from.
    ReliefStyle.cs              Where the high ground sits (internal, per character).
    Noise.cs, FieldOps.cs       FastNoiseLite wrapper + field helpers.
    IslandGenerator.cs          Generate(seed, params) — mask, patches, relief, lakes, keel.
    IslandArrangement.cs        Single / Satellites / Twins / Triplets / Archipelago / Atoll.
    Traversal.cs                Stage 5: walk areas, reach areas, buildable shelves.
    Rivers.cs                   Drainage routing, channels, waterfalls.
    Gate.cs / GatePlacement.cs  Where the Links come out.
  dev/IslandLab.cs              Runtime harness for scenes/dev/island_lab.tscn.
  dev/GenerationAudit.cs        Headless audit of the measured guarantees.
resources/
  island_default.tres          The IslandParams preset both dev scenes load.
scenes/
  main/main.tscn               Single-slab viewer (grass_block.tscn — still a cube).
  terrain/grass_block.tscn     Prototype terrain block (pre-slab-decision).
  dev/island_lab.tscn          Island generation harness.
  dev/generation_audit.tscn    Headless guarantee audit (see docs §4d).
docs/
  island-generation.md         Terrain generation + rendering spec.
CLAUDE.md                      This file.
```

Planned (create as needed, keep the tree shallow):

```
scripts/terrain/  the chunked span-aware mesher (IslandData -> ArrayMesh + colliders)
resources/        .tres data resources (biomes, cultural archetypes, goods)
addons/           third-party plugins
```

### Naming

- Scenes, `.tscn`/`.tres`, and their folders: `snake_case` (Godot convention).
- C# files: `PascalCase`, one `public partial class` per file, file name = class
  name (e.g. `CameraRig.cs`). Namespace `ProjectNikitin` (or a sub-namespace).
- Nodes and C# types: `PascalCase`. `[Export]` properties `PascalCase`.
- Use the design vocabulary in code: `Domain`, `Slab`, `Gate`, `Link`,
  `Polity`, `Settlement`, `Essence` — not synonyms like "block", "portal",
  "faction", "town". The terrain unit is a `Slab` (1:4 height ratio; the wiki's
  "8?" is superseded).

---

## Glossary (condensed from Notion)

- **Ecumene** — the whole game world: the tree of Domains.
- **Domain** — one floating landmass / archipelago, surrounded by aether. The
  **Home Domain** is where the player starts.
- **Aether** — the space between Domains; hazardous to people.
- **Link** — a fast, safe route through aether joining two Domains. **Gate** —
  the built structure at each end of a Link. Links form a tree (no loops).
- **Slab** — the atomic terrain unit: a cell 1×1 in footprint, 0.25 tall (1:4).
  Terrain Y is measured in slab indices. **Biome** — a Domain's flora / fauna /
  climate.
- **Gate kinds** — a **land Gate** stands on the ground and is walked through; a
  **hanging Gate** floats off the rim and is flown through, so the Domain owes it
  a landing strip. A Link joins two Gates of the *same* kind. One Gate per
  cardinal edge: one Entry, and one to three Exits.
- **Polity** — an NPC state ruling one or more Domains. **Metropole** — the
  Polity the player answers to.
- **Cultural Archetype** — a people's defining template (e.g. Steelfolk,
  Lakefolk, Jadefolk). Carries **Traits**: School of Magicks, Societal
  Structure, Political Situation, Means of Extraction.
- **Class / Role / Prestige** — population is stratified into classes (per
  Archetype); Role gates employment, Prestige gates promotion and consumption
  expectations.
- **Magicks** — the world's magic system. **Essence** — the refined magical
  resource; currently also doubles as currency (provisional). **Means of
  Extraction** — how a Domain refines Essence early game (e.g. Essencercoral
  Milling).
- **Settlement** — the basic economic unit: a market + warehouses + districts +
  surrounding facilities and land. **Needs** — Food, Intoxicants, Clothing,
  Wares — modulated by **Habits**, **Sophistication**, **Pickiness**, **Fashion**.
- **Player Avatar / "you the unit"** — the player's on-map character; acts as a
  mobile order relay. **Pioneers / Aethernaut / Aethership** — expedition crew,
  scout, and vessel for crossing Gates. **Aspiration** — the run's win
  condition, tied to starting culture.

---

## Design source of truth — Notion

Wiki database **"🪙 Project Nikitin"** (accessed via the Notion MCP connector).
Key pages:

| Page | State | Notes |
|---|---|---|
| Premise and Vision | written | What the game is and why. |
| The Ecumene | written | Domains, slabs, Links/Gates, scale. **Read before terrain/generation work.** |
| Mechanics and Concepts | index | Parent of the mechanics pages below. |
| The Gameplay Loop → The First Hour | written (narrative) | Best single description of moment-to-moment play; sample opening of a run. |
| Economy, Population and Settlements | written (draft) | Settlements, classes, Needs, monetary economy. Explicitly pre-structure. |
| Generation → Island Generation | short | Requirements checklist for island gen (sizes, terrain types, cliffs, layers). |
| Terrain, Polities, Magicks, Lore, Content | stubs / empty | |
| Glossary | partial | Terms defined above; many entries still blank. |
| Decision Log | DB, ~empty | Log firm design decisions here (with the "why" and alternatives). |
| Open Questions | DB | Unresolved design questions; see below. |
| Production Tasks → Tasks | DB | Roadmap. Stages: Prototype 0 → Prototype 1 → Vertical slice → Later. |
| Journal for Thoughts and Bits | DB | Loose ideas not yet promoted (multiplayer, difficulty levels, Stellaris-like starts). |

Workflow: consult the relevant page before non-trivial design or systems work.
When a decision gets made in a session, offer to add it to the **Decision Log**
and to answer/close the matching **Open Question**.

---

## Roadmap (from the Tasks DB)

- **Prototype 0** — Set up dev environment (git, Godot, Claude, VS Code). *In
  progress:* repo scaffolded and pushed to GitHub (`yeagore/project-nikitin`).
- **Epic: Render an island** — on branch `island-generation`, PR
  [#2](https://github.com/yeagore/project-nikitin/pull/2). Spec:
  `docs/island-generation.md`. Done: footprint → landform patches → relief under
  per-landform slope limits → lakes → keel, plus the lab and the audit scene.

Stages 1–5 are done, and the §4d gaps with them: the cliff rule holds, mesas no
longer compound, basins exist, lake shores are one slab, passes cross the ladder
occasionally, every arrangement is bridge-linked, rivers pour off the rim, and
every Domain has its Gates. The working footprint is **128²** at about **70 ms**
an island.

Next, in rough order: the **chunked span-aware mesher + colliders** — the biggest
piece left, and the only thing that will answer the performance question for
real; Stage 4b overhangs; the remaining feature anchors (`CoastCells`,
`CliffCells`, `Overhangs`); the §6 re-roll guarantees; settlement placement
hooks.

---

## Open questions / unconfirmed assumptions

Flagged so they aren't silently hard-coded:

1. *(resolved 2026-08-29)* Scripting is **C#**, not GDScript.
2. **Terrain unit = slab, 1:4.** Settled 2026-08-29 (after a detour through full
   cubes): a slab is 1×1 footprint × 0.25 tall. The wiki's tentative "8?" is
   superseded and Notion is not yet updated — needs a Decision Log entry.
3. **Essence as currency.** The design leans toward Essence = money for now but
   expects to revisit (grades of Essence, per-Polity currencies).
4. **Domains loaded at once.** Whether only the active Domain is fully simulated
   / rendered, or several. Drives the whole streaming/LOD approach.
5. **Camera.** `scripts/CameraRig.cs`: fixed pitch (set by the camera's offset
   direction), `LookAt`-aimed at the rig pivot. Pans (WASD, Shift faster, speed
   scales with zoom), yaws (Q/E, middle-mouse drag), wheel-zooms between
   `MinZoomDistance`/`MaxZoomDistance`. Undesigned: edge-scroll, pitch adjust,
   orthographic, pan bounds, auto-framing, InputMap actions.
6. **Terrain representation.** Per-column list of `Span(bottom, top)` runs
   (slab-indexed), not a 3D lattice — see `docs/island-generation.md`. Supports
   overhangs/arches (gap between spans); rules out branching caves/tunnels.
7. **Domain size.** 128² footprint working target vs 16³–64³ in Notion (Maxim
   favours smaller). Unlogged.
8. **`grass_block.tscn`** is still a 1×1×1 cube from the block detour — reshape
   to a 1×0.25×1 slab when convenient.
