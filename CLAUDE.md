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

Full spec: **`docs/island-generation.md`** — the model, the pipeline in the order
it runs, the parameters, the rendering handoff. The reasoning, the things tried
and removed, the audit numbers and the ideas not yet taken are in
**`docs/island-generation-appendix.md`**. Being built on the `island-generation`
branch.

Generation is a pure function `Generate(seed, IslandParams)` producing the
columnar `IslandData` (Y in slab indices); a separate chunked mesher will turn
that into per-chunk `ArrayMesh` + trimesh colliders. **Never one node per slab.**

**The free step is the whole grammar.** A one-slab step is free and two or more
needs building, so terrain generated under a one-slab slope limit is walkable by
construction and every cliff is one some rule put there on purpose. The island is
a blanket of **landform patches** — a warped Voronoi, each patch with a
`LandformType` and a rung on a plateau ladder — not a quantised height field,
which makes step sizes an accident of the gradient and contours into rings.

**Ten landforms.** Six are relief under a slope limit: `Plain`, `Hills`,
`Mountain`, `Mesa`, `Basin`, `Dunes`. Four are **sculpted** — cut into a surface
the limiter has already settled, then exempted from it, which is how they carry
cliffs *inside* a patch: `Badlands` (flat fingers, a maze of gullies), `Karst` (a
floor you walk with towers you cannot), `Massif` (concentric terraces climbing to
a summit), `Sinkholes` (round pits in open ground). Nothing is sculpted on a
patch's outer ring, so every border stays bound. `TerrainCharacter` says which an
island is built from, **by quota**: every landform a character names is
guaranteed to appear. A character is a *recipe*, not a list of what came out — the
lab names the landforms an island actually got.

**Twenty-two arrangements**, one shape per `IslandArrangement`: `Single`,
`Satellites`, `Twins`, `Triplets`, `Archipelago`, `Ring`, `BrokenRing`, `Arc`,
`BrokenArc`, `Atoll`, `ThousandIsles`, `Cross`, `TShape`, `LShape`,
`BrokenCross`, `BrokenT`, `BrokenL`, `Fractal`, `BrokenFractal`,
`Rosette`, `Star`, `Shards`. Blobs are placed deliberately and the seam where two
meet is either left alone (they fuse) or **carved into a strait** — that one flag
is the whole difference between `Ring` and `BrokenRing`. Crosses, Ts, Ls and
stars are axis-aligned, so an arm points at an edge and therefore at a Gate.
`NewArrangements` / `NewLandforms` keep the newer shapes out of **`Auto`'s pool**
without taking them out of the code — they gate the dice and nothing else, so
with an arrangement and a character both named by hand they do nothing at all,
which is what the lab's panel now says under the checkbox. (`Spiral` was binned
2026-08-31: keeping the coil continuous made it a `Rosette` with extra steps.)

**Water.** Lakes sink into a flat patch's interior with the patch's own rim as
containment, and the shore inset wanders so a lake is not a scale copy of a
Voronoi polygon. A big pool rolls a **shape** — single, thousand-lakes scatter,
ring, crescent, cross, or a small tarn — every shape a subset of the approved
pool, so fragmented islands are untouched while broad flat country comes out
wetter and more varied. Rivers are routed by a priority flood inward from the rim, with
**ties broken on a noise field — which is what makes them bend**. Sources are
named: every summit, and **one outflow per lake**. A river has a bed; a stream is
crossed **at a ford** (one every ~11 cells) and is an obstacle everywhere else; a
navigable river is two cells wide, not fordable, earned below a course's first
confluence, and sometimes splits round an **eyot**.

**Fluids.** `IslandData.Fluid` is per column (the removed Domain-wide `FluidKind`
dropdown came back upside down, 2026-09-01): water is the default and the only
fluid that behaves. **Goo** — violet puddles on ~30% of islands — makes no
rivers (the routing treats it as not-land) and **never mixes with water, even
diagonally**; the audit checks that at zero. **Geysers** (~35% of islands) are a
clustered field of water jets on high dry ground: scenery and a feature anchor,
no terrain moved, in `IslandData.Geysers`. The ground sinks toward a course in tapered bands (`Valleys`) — **and
the channel sinks with them**, one band deeper than its own bank, because a bank
already stands one slab above the water and a valley that only lowers the ground
beside a river comes out as a moat around it. Valleys favour the courses that
descend through uneven country; a river crossing a plain keeps its bare incision.
A navigable river is a **stair of pools** — dead level between falls, its two
cells always at one level (`Settle` in `Rivers.cs`) — and water **pours every way
it plausibly can**: off every aether edge beside a cell and toward any
neighbouring water a fall's depth below it, never onto dry ground, so nothing new
gets wet. Every river reaches the rim and
pours off it, because there is no sea. Everything is water: `FluidKind` (lava,
essence) was removed 2026-08-31 — it was two `if`s and a dropdown with nothing
visible behind it, and the whole idea is the look.

**Three kinds of works cross what you cannot walk.** A **bridge** is a level run
of slabs spanning aether (up to `Crossings` cells), water (3), or a **chasm** —
ground 5 slabs or more below the deck, which is how one cliff top is bridged to
another. A **stair** climbs 8 slabs and stands on two cells that nothing else is
built on. A **ferry** runs between two quays on one body of water; a waterfall
cuts a body in two, and berths are pruned against a ferry-less reach flood so
only the load-bearing ones survive.

`Traversal.Analyse` reads the finished terrain back: `Walk` (on foot), `Reach`
(once built), water bodies, ferry berths and `Shelves` (level enough to settle
on). `GatePlacement` then puts **four hanging Gates on the Domain, one per edge**
— the maximum — and everything else is a *subtraction* from that: an Exit the
Domain does not need is deleted, and a Gate asked to be a land one is moved from
the end of its flight path down onto its own landing strip. A Gate is **one
block** (1 cell, 4 slabs), its strip is **1 × 3** running inland, and the strip is
**levelled** rather than found level — a Gate is a built structure and so is the
ground under it. The four are chosen as a **set** by a small backtracking search,
because each has to out-reach every other on both axes and placing them one at a
time paints the island into a corner.

The Entry's **kind and edge are inputs**, because a Link joins two Gates and a
Domain reached by travelling east comes out on its west side — and so are
`ExitGates` and `ExitGate`. Since the sites are chosen before any of them has a
role, the named edge simply *is* the Entry and the named kind is applied to it.
Measured over 176 arrangement × character combinations: **four hanging Gates on
100% of runs, every edge/kind/count request met on 100% of seeds at 1.00
attempts, and every landing exactly 3 cells and dead level.** Gate placement is
the one pass that both reads the traversal analysis and changes the terrain, so
`Traversal.Analyse` runs again when it moved a slab.

`Passages` is the payoff: the **least-infrastructure road from the Entry to each
Exit**, walking free and every work one point. Five elevators inside fifteen
cells is a *flight*, which marks the Domain `Rough`. `Surfaces` then classifies
what the ground is made of and collects the feature anchors (`CoastCells`,
`CliffCells`, `Overhangs`); `Names` names the Domain and its parts. Stage 6
overhangs and arches give some columns a second span — rendered and collidable,
**not yet walkable**, because pathing over a two-level column wants spans as
nodes and that is its own problem.

Finally `Generate` **re-rolls an unplayable Domain**, or one built to the wrong
specification: one Entry of the right kind *on the right edge*, at least one Exit
and as many as `ExitGates` asked for, of the kind `ExitGate` asked for, a road to
every Exit, a buildable shelf on the heartland, and three quarters of the land
reachable from it.

**Launching the island lab:**

1. Open the project in the .NET Godot editor; build C# (hammer icon, or
   `dotnet build "Project Nikitin.csproj"`).
2. In the FileSystem dock open `scenes/dev/island_lab.tscn`, then press **F6**
   ("Run Current Scene"). It is not the project's main scene, so F5 won't run it.
3. **The control panel down the left is the interface** — dropdowns for the view,
   arrangement, character, entry kind and edge, exit kind and crossing
   ease; sliders for hilliness, mix, relief, rivers, lakes and valleys; spin
   boxes for the plateau rungs, cliff height, region scale and exit count; a
   checkbox each for the newer shapes and every overlay. **Tab** hides it. Every
   control is also a key, and both write the same `Params`: **N** new seed, **R**
   rebuild, **F** frame, **C** view, **V** character, **G** arrangement, **H**
   hilliness, **M** mix, **L** rungs, **U** new shapes, **T** entry kind, **Y**
   crossings, **B J K O P X** the overlays, **F2** a screenshot.
   Camera: **WASD** move, **Q/E** or middle-drag rotate, middle-drag or **up/down
   arrows** tilt, **wheel** zoom, **Shift** faster. (Fords moved from **D** to
   **O** — D is the camera's strafe, so the two fought.)
4. Views: `height` / `landform` / `region` / `walk` (what connects on foot) /
   `reach` (what connects once you build — red is out of reach whatever you
   build) / `shelves` / `surface` (what the ground is made of: stone, scree, snow,
   sand, silt, grass, meadow, heath, dust) / `anchors` (what the content layer
   attaches to: coast, cliff, overhang, beach, ford, gate landing, ferry quay —
   everything else dimmed). Water is coloured by kind: pale a ford, mid a stream,
   deep a navigable reach, dark a lake — and goo is violet, in every view.
5. Overlays: **B** bridge sites, **J** the ground each Gate is served by (its
   1 × 3 landing strip, whichever kind of Gate it is),
   **K** ferry berths (quay and hull), **O** fords, **P** the roads between
   the Gates (pale yellow walk; red stair, gold bridge, cyan ferry), **X** the
   compass, each Gate's landward vector, and the **prevailing wind** drawn along
   each dune field (the ridges lie across it).
6. The readout is at the **top right**: what the view means, then what this island
   turned out to be — its name, arrangement, the landforms it actually got, the
   ladder, walk and reach shares, shelves, berths, rivers, Gates, and what each
   road out costs. `ROUGH GOING` means a road climbs five elevators in fifteen
   cells; `COAST WOULD NOT` means a Gate you asked for is not the Gate you got.
7. **The window is 1152 × 648 and will not stretch?** That is the editor
   *embedding* the game, not the project — the base viewport is 1920 × 1080 and
   the UI scales. Editor Settings → Run → Window Placement → **Game Embed Mode:
   Disabled** runs it as its own OS window.

**Where the numbers live.** `resources/island_default.tres` is the `IslandParams`
preset, and **both** the lab and the audit load it — so the audit measures the
island you are tuning. Edit the `.tres` in the Inspector to change it durably, or
use the lab's own panel (or the **Remote** tab of the Scene dock) for a throwaway
experiment that is never written to disk.

CLI: `godot --path . scenes/dev/island_lab.tscn`

**Checking generation without looking at it:**
`scenes/dev/generation_audit.tscn` runs the real generator over 60 seeds headless
and prints the measured guarantees. Run it after any change to the generator. It
also prints **what moved since the last accepted run** against
`docs/audit-baseline.json` — a diff, not a test; set `AcceptBaseline` to accept
the current numbers. Seven opt-in flags print what a summary cannot show:
`Silhouettes` (one island per arrangement), `Waterways` (one island's water, full
resolution), `Sculpts` (a close-up of each sculpted landform), `Feasibility`
(every arrangement × every character, flagging the combinations the pipeline
finds hard), `GateRequests` (ask for each Entry edge and kind and each Exit count
and kind, and report what came out), `GateMatrix` (ask every arrangement ×
character for **four hanging Gates** — the maximum request — and then check the
reductions; it also prints the funnel saying *why* a coast refuses one) and
`Knobs` (sweep `Lakes` / `Rivers` / `Valleys` from 0 to 1 and print what each one
moves — **this is how you check a slider does anything**, and it is how the
inverted valley pass was found).
Appearance still needs a human at the editor — or **F2** in the lab.
See `docs/island-generation-appendix.md` §D for what the audit currently says and
which gaps are open.


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
    LandformType.cs             Plain / Hills / Mountain / Mesa / Basin / Badlands / Karst /
                                Massif / Dunes / Sinkholes.
    TerrainCharacter.cs         Which landforms an island is built from.
    ReliefStyle.cs              Where the high ground sits (internal, per character).
    Noise.cs, FieldOps.cs       FastNoiseLite wrapper + field helpers.
    IslandGenerator.cs          Generate(seed, params) — mask, patches, relief, lakes, keel, re-roll.
    IslandArrangement.cs        The twenty-two named layouts.
    Traversal.cs                Stage 5: walk areas, reach areas, water bodies, ferry
                                berths, buildable shelves.
    BridgeEase.cs               Easy / Medium / Hard — cells one bridge spans.
    Crossing.cs                 A bridge site: two banks, a deck level, a span.
    Ferry.cs                    A ferry berth: a quay, its water, the body it reaches.
    Surfaces.cs                 What the ground is made of, and the feature anchors.
    Names.cs                    Names for the Domain, its districts and its water.
    Rivers.cs                   Drainage routing, channels, banks, eyots, waterfalls.
    Fall.cs                     One waterfall; the off-rim ones are the silhouette.
    FluidKind.cs                What a body of standing fluid is (water, goo).
    Geyser.cs                   One jet of a geyser field.
    Gate.cs / GatePlacement.cs  Where the Links come out.
    Passage.cs                  The least-works road from the Entry to each Exit, and
                                the works — stair, bridge, ferry — along it.
    Overhangs.cs                Undercut lips and arches — the only stage that gives
                                a column two spans.
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
  island-generation.md         The generation spec: model, pipeline, parameters.
  island-generation-appendix.md  Why, what was tried, the audit, the ideas.
  audit-baseline.json          The last accepted audit numbers.
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
- **Gate kinds** — a **hanging Gate** floats ten cells off the rim and is flown
  through, so the Domain owes it a landing strip (1 × 3 cells, running inland from
  the coast under it); this is the **normal** case. A **land Gate** is the same
  site with the portal moved down onto that strip, and is walked through. A Link
  joins two Gates of the *same* kind. One Gate per cardinal edge, near that edge:
  one Entry, and one to three Exits.
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

All eleven stages are done: footprint, regions and landforms, surface, water,
keel, overhangs, traversal, Gates, the roads between them, surfaces and names,
and the re-roll guarantees. So are beaches, valleys, fords, ferries, the feature
anchors, the audit baseline and lab screenshots. The working footprint is
**128²**.

Next, in rough order: the **chunked span-aware mesher + colliders** — the biggest
piece left, and the only thing that will answer the performance question for
real; **settlement placement**, which is the first thing that would show whether
the terrain rules make good play rather than good pictures; the **biome layer**
above `Material` — what grows where, as opposed to what the ground is; and
**span-aware pathing**, which is what would make an overhang walkable.

**Ideas logged rather than done** — a real cost model for works, the world-tree
above the Domain, fjords, and a page of thinking about otherworldly terrain — are
in `docs/island-generation-appendix.md` §E and §F, with the reasoning.

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
