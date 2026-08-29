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

The C# side builds standalone: `dotnet build "Project Nikitin.csproj"`
(`dotnet` 8.0 is available; the `Godot.NET.Sdk` NuGet restores from nuget.org).
Do this after editing any `.cs` to catch compile errors without the editor.

There is no `godot` binary on `PATH` in this environment, so scene loading and
the actual game can only be checked by running the installed .NET Godot editor.
Once its path is known, add it here. Useful invocations:

```
godot --path . --editor              # open the project (also triggers a C# build)
godot --path . scenes/main/main.tscn # run a scene directly
godot --path . --headless --build-solutions --quit   # build C# + import, no window
```

---

## Spatial model (from Notion → "The Ecumene")

This is the part that governs terrain, generation, and rendering code.

- A **Domain** is a 3D landmass (or archipelago) of terrain units, magically
  suspended in aether. Visually: flying islands, like Skyblock in Minecraft, but
  the scale is coarser — **one unit's top face ≈ an orchard or a housing
  compound**, not a person.
- The terrain unit is a **block**: a full **1×1×1** cube cell.
  > **Divergence from the wiki (2026-08-29):** "The Ecumene" and the Glossary
  > still describe a thin **slab** (~1/8 height) with a "one step is free, a
  > stack is an obstacle" traversal rule, introduced so terrain could express
  > hills rather than only sheer cliffs. That idea is being dropped in favour of
  > full cubes. This should be written into the Notion **Decision Log** (with the
  > cliff/hill trade-off as the noted downside) and the Glossary + "The Ecumene"
  > updated. Until then, code follows the block model here, not the wiki.
- **Gravity** always points down (−Y) by default.
- Each Domain sits inside an **invisible bounding cube** that keeps vessels from
  drifting off; it does not block Gate travel.
- **Biome features** (forests, herds, coral/essencercoral growths, vines, fungal
  mats) are structures that sit *on top of, on the sides of, or underneath* block
  stacks. They are a separate layer from the blocks themselves.
- Domain size: working target **128×128×128** block-cells (position: vasin; the
  Notion "Ecumene" page still says 16³–64³, and Maxim favours smaller — decision
  not yet logged). 30–40 Domains per game, laid out on a plane by their position
  in the world-tree (a Domain linked "north" is found by scrolling north). Up to
  4 side Links per Domain now (maybe 6 — incl. top/bottom — later).
- **Terrain is stored per column, not as a 3D voxel array.** Each `(x,z)` of the
  128×128 footprint holds a short list of `Span(bottom, top)` solid runs; the air
  gap between two spans is an overhang / arch. ~90 KB per island, no per-block
  storage, whole island resident. Overhangs and arches are supported; branching
  caves/tunnels are not. See `docs/island-generation.md`.
- Performance: a naive dense 128³ is ~2M cells and per-block nodes are impossible,
  which is why the columnar model exists. Treat a full 128² footprint as the
  stress target for the mesher.

### Code conventions derived from the above

These are set here so every session stays consistent. Change them in one place.

| Constant | Value | Meaning |
|---|---|---|
| `CELL_SIZE` | `1.0` | Edge length of one cube cell, in Godot units (metres). Applies to all three axes. In fiction one cell is ~an orchard. |
| Grid → world | `Vector3(gx, gy, gz) * CELL_SIZE` | `gx, gy, gz` are integers. |
| Block local origin | **base centre** | So placement is a bare grid→world call with no half-height offset. |

Godot axis conventions (unchanged): **Y up**, right-handed, cameras look down
**−Z**, `1 unit = 1 metre`.

### Rendering an island (the current epic)

Full spec: **`docs/island-generation.md`** — data model, generation pipeline,
parameters, rendering handoff, first implementation slice. Being built on the
`island-generation` branch.

In short: generation is a pure function `Generate(seed, IslandParams)` producing
the columnar `IslandData`; a separate chunked mesher turns that into per-chunk
`ArrayMesh` + trimesh colliders (only exposed faces). **Never one node per
block.** First cut may use a `MultiMeshInstance3D` of the existing block mesh,
one instance per surface cell, to get an island on screen before the mesher.

The current `scenes/terrain/grass_block.tscn` is a **single-block prototype** (its
own `StaticBody3D` + `CollisionShape3D`); it is a visual reference and a source
of the material look, not the pattern for the full island.

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
  CameraRig.cs                  Strategy-camera controller (attached to CameraRig in main.tscn).
docs/
  island-generation.md         Terrain generation + rendering spec.
CLAUDE.md                      This file.
```

Planned (create as needed, keep the tree shallow):

```
scenes/     .tscn scenes, foldered by area (terrain/, ui/, domain/, ...)
scripts/    C# node scripts and shared classes (namespace ProjectNikitin)
resources/  .tres data resources (block types, biomes, cultural archetypes, goods)
addons/     third-party plugins
```

### Naming

- Scenes, `.tscn`/`.tres`, and their folders: `snake_case` (Godot convention).
- C# files: `PascalCase`, one `public partial class` per file, file name = class
  name (e.g. `CameraRig.cs`). Namespace `ProjectNikitin` (or a sub-namespace).
- Nodes and C# types: `PascalCase`. `[Export]` properties `PascalCase`.
- Use the design vocabulary in code: `Domain`, `Block`, `Gate`, `Link`,
  `Polity`, `Settlement`, `Essence` — not synonyms like "portal", "faction",
  "town". (The wiki's term for the terrain unit is still "slab"; see the
  divergence note under Spatial model.)

---

## Glossary (condensed from Notion)

- **Ecumene** — the whole game world: the tree of Domains.
- **Domain** — one floating landmass / archipelago, surrounded by aether. The
  **Home Domain** is where the player starts.
- **Aether** — the space between Domains; hazardous to people.
- **Link** — a fast, safe route through aether joining two Domains. **Gate** —
  the built structure at each end of a Link. Links form a tree (no loops).
- **Block** — the atomic terrain unit (a 1×1×1 cube). The wiki still calls this a
  **Slab** and describes it as ~1/8 height; that is being dropped — see the
  divergence note under Spatial model. **Biome** — a Domain's flora/fauna/climate.
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
- **Epic: Render an island** — in progress on branch `island-generation`. Spec
  written (`docs/island-generation.md`). Done so far: single grass block in a lit
  3D scene (`main.tscn`) with the strategy camera.

Immediate direction (per the spec's §8 first slice): `IslandParams`/`IslandData`
types → generation stages 1–4 → `MultiMeshInstance3D` of the block mesh in a
`scenes/dev/island_lab.tscn` with exported params → then the chunked mesher,
feature anchors, and settlement hooks.

---

## Open questions / unconfirmed assumptions

Flagged so they aren't silently hard-coded:

1. *(resolved 2026-08-29)* Scripting is **C#**, not GDScript.
2. **Slab → block.** The thin-slab terrain unit from the wiki is dropped for full
   1×1×1 cubes (user call, 2026-08-29); half-blocks may return later. Not yet
   reflected in Notion. Consequence carried into the island spec: no smooth
   hills, "mountains" are stepped/mesa, and habitable shelves must be ≥3–4 cells
   wide to matter. Needs a Decision Log entry and wiki edits.
3. **Essence as currency.** The design leans toward Essence = money for now but
   expects to revisit (grades of Essence, per-Polity currencies).
4. **Domains loaded at once.** Whether only the active Domain is fully simulated
   / rendered, or several. Drives the whole streaming/LOD approach.
5. **Camera.** `main.tscn` uses a `CameraRig` (Node3D, `scripts/CameraRig.cs`)
   with a child `Camera3D` at a fixed ~25° pitch (perspective, fov 45). The rig
   pans on the ground plane (WASD, Shift to accelerate, pan speed scales with
   zoom), yaws (Q/E, middle-mouse drag), and zooms by sliding the camera along
   its fixed offset direction (mouse wheel, clamped to
   `MinZoomDistance`..`MaxZoomDistance` = 12..360 units). Pitch is deliberately
   locked. Still undesigned: edge-scroll, pitch adjust, orthographic option, pan
   bounds, and moving to InputMap actions instead of polled physical keys.
6. **Terrain representation.** Per-column list of `Span(bottom, top)` solid runs,
   not a 3D lattice — see `docs/island-generation.md`. Supports overhangs/arches
   (gap between spans); still rules out branching caves/tunnels.
7. **Domain size.** 128³ working target vs 16³–64³ in Notion (Maxim favours
   smaller). Unlogged.
