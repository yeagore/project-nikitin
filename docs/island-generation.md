# Island Generation — the spec

How a Domain is generated, in the order it happens. **The reasoning, the things
that were tried and removed, the audit numbers and the ideas not yet taken are in
[island-generation-appendix.md](island-generation-appendix.md)** — this file is
meant to stay readable.

Status: 2026-09-01. Every stage below is implemented in `scripts/generation/`,
and `scenes/dev/island_lab.tscn` draws the result. Five footprints are supported
and audited — **48², 64², 72², 96², 128²**, two ladders overlaid until Maxim
picks one — with 128² the stress target, and a Domain's **altitude bounded by
the same number in slabs** (`IslandParams.SupportedSizes`,
`IslandGenerator.BoundAltitude`), so the bounding cube is a real shape. What is
left is the chunked mesher.

---

## 1. The model

A Domain is a square grid of **columns**. Each column holds a short list of solid
`Span(bottom, top)` runs, and the bounds are **slab indices** — integers on Y, a
quarter of a cell each.

```csharp
public Span[,][] Spans;          // [x, z] -> runs, bottom-up, disjoint, non-touching
public short SurfaceLevel(x, z); // top of the LOWEST span: the ground you stand on
public short KeelLevel(x, z);    // bottom of the lowest span: the underside
```

Almost every column has exactly one span, keel to surface. The air gap between
two spans is an **overhang** or an **arch**, and only Stage 6 makes one. That is
why `SurfaceLevel` reads the lowest span: under a lip, the ground is underneath,
and every rule in the pipeline means the ground.

| constant | value | |
|---|---|---|
| `CellSize` | 1.0 | one cell, in metres. In fiction, about an orchard. |
| `SlabHeight` | 0.25 | one slab. Terrain Y is an integer count of these. |
| free step | **1 slab** | walk it for nothing. Two or more needs building. |

**The free step is the whole grammar.** A one-slab step is free, so terrain built
under a one-slab slope limit is walkable everywhere by construction, and every
cliff on the island is one some rule put there on purpose. That is what makes
"where are the cliffs?" a decision the generator makes rather than an accident of
the noise — see the appendix, *Why it is built this way*.

Alongside the terrain, `IslandData` carries what the later stages worked out:

```csharp
byte[,]  Landform, Material;      // what this patch is, what its surface is made of
short[,] WaterLevel;              // top slab of standing fluid, or NoLand
byte[,]  Fluid;                   // what stands there: water unless a pass said otherwise
bool[,]  River, Navigable, Ford;  // a course; too deep to wade; where you can
bool[,]  Beach, Ferry, Landings;  // and the built-on / landed-on ground
int[,]   Region, Walk, Reach, ShelfId, WaterBody, Flow;
List<...> Areas, Reaches, Shelves, Bridges, Berths, Passages, Gates, Falls,
		  Geysers, CoastCells, CliffCells, Overhangs, Passes;
```

Generation is a **pure function of `(seed, IslandParams)`**. Same inputs, same
Domain, every time.

---

## 2. The pipeline

### Stage 1 — Footprint

The land mask is a set of **placed blobs**, one per landmass, chosen by
`IslandArrangement`. Each blob is an ellipse with a radius that wanders on a
noise field, so no coastline is a circle; where two blobs meet, the seam between
them is either left alone (they fuse into one shape) or **carved into a strait**.
That one flag is the whole difference between `Ring` and `BrokenRing`.

| | layouts |
|---|---|
| one landmass | `Single` `Ring` `Arc` `Cross` `TShape` `LShape` `Fractal` `Rosette` `Star` `Square` `Rhomb` `NShape` `Isthmus` |
| several | `Satellites` `Twins` `Triplets` `Archipelago` `BrokenRing` `BrokenArc` `Atoll` `ThousandIsles` `Shards` `BrokenCross` `BrokenT` `BrokenL` `BrokenFractal` `Quarters` `Halves` `Harmony` `Reef` |

The **geometric set** (2026-09-01): `Square` and `Rhomb` are blocky fused grids of
lobes, axis-aligned and corner-stood; `NShape` is the letter itself, three
strokes of elongated lobes; `Quarters` and `Halves` are four and two roughly
symmetric parts split by straight straits; `Harmony` is the yin-yang — the first
layout built from **grouped** lobes, each comma a chain whose own seams fuse
while the S between the two is carved (`Lobe.Group`); `Isthmus` is two heads and
a walkable neck; `Reef` is a main island sheltered behind a barrier chain.
`ThousandIsles` was rebuilt as a **quilt** — a jittered grid of lobes over the
whole footprint, every seam a strait — because any scatter wider than the bridge
span is huddled back together by the linker, which is why its isles always
crowded the middle.

`Cross`, `TShape`, `LShape` and `Star` are one shape with a different set of
spokes — a wide hub with thick arms, **axis-aligned**, so an arm points at an
edge and therefore at a Gate. `Fractal` walks a chain of overlapping blobs that
turns as it goes; `Rosette` is a coil made short and fat, which comes out as a
ring of deep bays.

There were twenty-three. `Spiral` — the same coil wound thin over two and a half
turns — is gone: keeping the arm continuous took a coil so thick and so densely
linked that what came out was a `Rosette` with more steps, and two names for one
shape is worse than one shape.

Then: bites are taken out of a lone island by deleting whole regions, diagonal
joins are filled, components under 30 cells are dropped, and **`LinkLandmasses`
nudges the pieces together** until every one is within a bridge span of the rest.
Whatever the arrangement, the pieces are linkable.

**The fit pass** wraps that whole stage: the landmass's bounding rectangle must
cover **55–85% of the grid** (Maxim's band, erring big), measured on what
actually ships — after the bites, the islet filter and above all the linker,
whose stray-dragging shrinks every scattered layout. A layout that lands short
is rebuilt scaled up about the centre, radii and all. Measured across all
thirty arrangements, every one now sits in the band; before the pass, Twins
took 38% of its box and Reef 39%.

*New layouts are gated by `IslandParams.NewArrangements`, which keeps them out of
`Auto`'s pool without taking them out of the code.*

### Stage 2 — Regions and landforms

A warped-Voronoi partition at `RegionScale`, split into connected components and
merged until nothing is under `MinRegionArea`. Each region gets a `LandformType`
and a **rung** on the plateau ladder.

**The plateau ladder** is the island's vertical vocabulary. A rung is a whole
multiple of `CliffHeight` slabs above the coastal level, and `PlateauLevels` says
how many rungs exist. Two neighbouring regions on the same rung are joined ground
— the slope limiter reaches across the border and holds it to the free step. Two
on different rungs are a cliff of exactly `CliffHeight`. So `PlateauLevels` is a
knob for **how terraced the island is**: at 1 the only cliffs come from mesas,
basins and mountains; at 2 (the default) escarpments are occasional; at 5 the
island is a flight of terraces.

| landform | built from | slope limit | reads as |
|---|---|---|---|
| `Plain` | rung + ~1.4 slabs of noise | 1 | flat, buildable, dull on purpose |
| `Hills` | rung + up to ~15 slabs | 1 | rolling, walkable everywhere |
| `Dunes` | rung + a wave along the Domain's wind | 1 | parallel ridges: level along, washboard across |
| `Mountain` | an S-curve off the ground it meets, no rung | none | foothills, steep flanks, a rugged summit |
| `Mesa` | above every neighbour, flat top | 1 | tableland ringed by cliff |
| `Basin` | below every neighbour, flat floor | 1 | the mesa rule inverted |
| `Badlands` | a plain, then **gullies cut into it** | sculpted | flat fingers, a maze of ravines |
| `Karst` | a plain, then **towers raised out of it** | sculpted | a floor you walk, columns you cannot |
| `Massif` | a plain, then **concentric terraces** | sculpted | a stepped massif; every riser wants a stair |
| `Sinkholes` | a plain, then **round pits punched out** | sculpted | crossable, while watching your feet |

**The Domain has a wind.** One grain for the whole island, so every dune field
lies the same way, and **snapped to one of the eight compass points** — `E`, `NE`,
`N`, … — rather than a free angle. A direction that cannot be named is a
direction nothing can show or use: `IslandData.DuneGrain` carries it, `WindFrom`
and `DuneRun` say it in letters, the lab's readout prints it and the compass
overlay (**X**) draws an arrow along it across each dune field.

**The four sculpted landforms carry cliffs *inside* a patch**, which relief under
a slope limit cannot express. They are cut into a surface the limiter has already
settled and then exempted from it — the mechanism a canyon already used. Two
rules keep them honest: nothing is sculpted within the outermost ring of its own
patch, so every border stays bound; and every cut is a **fixed depth**, because a
tapering gully has a two-slab step somewhere along it by construction.

`TerrainCharacter` says which landforms an island is built from — `Plains`,
`Tablelands`, `Downs`, `Highlands`, `Badlands`, `Karst`, `Massif`, `Dunes` — and
the shares are a **quota, not a dice roll**: every landform a character names is
guaranteed at least one region. `LandformMix` slides the quota from low ground to
high. *A character is a recipe, not a list of what came out: `Massif` gives you
plains, hills and a mountain too, and the lab's status line names what an island
actually got.* Gated by `IslandParams.NewLandforms`.

### Stage 3 — Surface

Relief per region under that region's slope limit, then:

- **`LimitSlope`** — a Lipschitz projection from above: lower any cell standing
  more than its limit above a neighbour. It reaches *across* a region border
  wherever the two share a rung, which is what closes the last cliffs the rules
  forbid.
- **`ResolveAmbiguousSteps`** — removes two-slab steps. Two is the worst height a
  step can be: too tall to walk, too short to read as a cliff.
- **canyons and passes** — a canyon is a deliberate cliff where the rules would
  forbid one; a pass is the opposite, a saddle cut so a cliff border has one
  place you can walk across.
- **`Sculpt`** — the four sculpted landforms, cut in and exempted.
- **beaches** — where the ground reaches the rim gently (soft landform, level
  neighbours), the outermost two cells step down. It is the difference between
  land that stops and land that *meets* the aether, and it gives a quay somewhere
  to sit.

These run in a **settle loop** with `LevelBridgeheads`, because each pass can
expose work for the others and all three only ever lower.

### Stage 4 — Water

Lakes first, then rivers, both cut across the finished patchwork.

**A lake sinks into the interior of a flat patch, and the patch's own untouched
rim is the containment.** No higher ground is needed, which is what lets lakes
happen anywhere. The shore inset **wanders** on a noise field from 2 to 5 cells,
so a lake is the patch's shape read through that field rather than a scale copy
of a Voronoi polygon. One lake per patch, and a patch beside one that holds water
stays dry — a row of pools at slightly different levels reads as flooding.
`Lakes` scales how many.

**A big pool need not be one big lake.** Where the pool is large enough to have
an inside, it rolls a **shape**: still a single body more often than not, else a
*thousand-lakes* scatter of separate pools, a *ring* round a dry island of its
own floor, a *crescent*, a ragged *cross*, or a *tarn* cropped small. Every
shape is a subset of the pool the containment already approved, so nothing about
the rules changes — and small pools keep the old behaviour to the cell, which is
why fragmented islands are unaffected. Broad country also rolls wetter (a large
interior lifts the lake chance by up to half) and patches that lose the main
roll can still take a small tarn, so a big connected flat island comes out with
more, and more varied, water.

**Rivers are routed by a priority flood inward from the rim.** Terrain under a
slope limit is mostly flats, so steepest descent stalls; flooding inward gives
every cell a downstream neighbour by construction and passes straight through a
lake. **Ties break on a noise field, which is what makes rivers bend** — a
first-in-first-out tie-break makes the flood a breadth-first search whose tree is
a fan of straight cardinal rays.

Sources are **named**: every summit, and **one outflow per lake** (the cell whose
downstream ground is lowest). Accumulation alone gives almost nothing on
slope-limited terrain, and one outflow per shore cell gave a fan of parallel
channels below every lake that read as a marsh.

- **A river has a bed**: the channel is cut two slabs down and filled to one
  below the ground, so the banks stand proud and the course reads as a channel.
  The river cuts its banks to match, so no two-slab step is left behind.
- **A stream is crossed at a ford** — one every ~11 cells, where both banks are
  dry and within a slab of the water — and is an obstacle everywhere else.
- **A navigable river** is two cells across, three slabs deep, and not fordable —
  and a course earns it below its first real confluence, which is where a barge
  would in fact get in. It occasionally splits round an **eyot**, an island of
  its own floodplain lying along the course. **It is a stair of pools**: the water is walked from the rim
  upstream and held level until the ground has risen a fall's worth, so a barge
  river is dead flat between falls instead of wearing thirty one-slab steps —
  and its two cells always hold **one** water level, whatever the valley pass
  did to either of them (`Settle` runs the descend and pair-levelling
  corrections against each other until both hold at once).
- **Valleys**: the ground either side sinks toward a course, in bands tapered so
  no step exceeds one slab. **The channel sinks with its valley** — the bank
  already stands exactly one slab above the water, so lowering only the ground
  beside a river cannot make a valley; it made an inverted one, a moat two cells
  out with the ground rising back toward the water. The band the river is in goes
  down one further than the bank, which is what puts the river at the bottom of
  its own valley. Ground whose height is the point of it — a mesa rim, a tower, a
  levelled bridgehead — does not come down, and no cell may sink past such a
  neighbour by more than the free step.
  **`Valleys` acts per watercourse, not per island.** Each drainage — a
  4-connected component of the channel network — draws a rank and keeps it, and
  the knob slides a window across those ranks (`3 × strength − 2 × rank`). So 0
  gives none, a quarter gives about a third of the courses a narrow valley and
  the rest none, a half gives three quarters of them valleys of differing depth,
  and 1 cuts every course to its full reach. One reach for the whole Domain made
  the knob all-or-nothing, which is not what a country looks like.
  **The rank is tilted by the course's own descent**: a river that falls a long
  way came down through uneven country and draws from the low end of the window,
  a river crossing a plain draws from the high end — so mid-slider, valleys go
  to the hills and a plain keeps its incisions. 0 still cuts nothing; and the
  slider's whole range now maps onto what used to be its **lower half**, because
  everything past the old midpoint was trenches nobody chose — 1 means "the most
  valley worth having", with steep courses cut in full and plain ones shallow.
- **Every river reaches the rim and pours off it**, because there is no sea. Rim
  falls are drawn spilling past the keel into the aether.
- **A lip pours every way it plausibly can.** A cell that is a fall at all — any
  aether beside it, or a fall's depth of drop along its own course — throws a
  sheet off *every* aether edge (a corner spills both ways) and toward every
  neighbouring *water* far enough below it; a lake does the same where its
  outflow leaves well under its surface. Only toward water and aether, never
  onto dry ground: a sheet onto dry land would be a course the drainage never
  routed, and either floods the ground or vanishes into it.

**Fluids.** `FluidKind` returned 2026-09-01, upside down: the removed one was a
Domain-wide dropdown (every watercourse water, or all of it lava) with nothing
visible behind it; the new one is **per column** (`IslandData.Fluid`), so one
Domain can hold different fluids at once — which is what the biome layer will
want. Water is the default and the only fluid that behaves: rivers, ferries and
fords are water's alone.

- **Goo** is the first other fluid: violet puddles sunk into dry flat patches
  like small tarns, on ~30% of islands, 1–3 apiece. It makes no rivers — the
  routing treats goo as not-land, so nothing drains through it, out of it, or
  into it — and **fluids never mix, even diagonally**: no water may stand within
  a king's move of goo (placement guarantees it, the rivers' `keep` mask
  preserves it, and the audit checks it). Nothing sails, fords or walks it. It
  is deliberately inert until the biome layer says what it is for.
- **Geysers** are a hook, not yet a feature: `IslandData.Geysers` and the lab's
  jet rendering exist, and nothing fills them. A terrain-stage placement was
  tried and binned the same day — where a jet belongs is a fact about the
  *biome*, so the placement waits for that layer.

### Stage 5 — Keel

The island hangs as a spinning top: a thin lip at the coast (`EdgeThickness`)
thickening inland to `KeelDepth`. The distance field is sampled through a domain
warp so the underside is not a surface of revolution, and every column is kept at
least `EdgeThickness` slabs thick.

### Stage 6 — Overhangs and arches

The only stage that gives a column two spans.

- **Undercut.** A columnar model cannot cut sideways into a cliff, so it is built
  the other way round: the columns *in front of* a face of 8 slabs or more get a
  second span up at the cliff top, with air between it and their own ground. From
  below that is an undercut; from above it is the cliff edge jutting out.
- **Arch.** Two cliff tops within 2 slabs of each other with a gorge or a channel
  between them, joined by a deck flush with the lower end.

Both need **backing** — the high side must have two neighbours within a slab of
its own top, and must not be a landform whose whole shape is the wall (karst,
badlands, basin, sinkholes). Without that, a lip off a two-cell karst tower reads
as a hole punched through it.

> **What Stage 6 adds is not walkable.** It runs after the analysis on purpose:
> the lip of an overhang is a roof, and pathing over a two-level column wants
> spans as nodes rather than columns. That is a real problem and a separate one.

### Stage 7 — Traversal

Pure analysis. It changes nothing; it is how we find out whether the island is
playable.

- **`Walk`** — what connects on foot: neighbours within the free step. Water is
  not ground; a stream is crossed at a ford.
- **`Reach`** — what connects once you build. Three kinds of works:

| works | rule |
|---|---|
| **stair / hoist** | a face of at most 8 slabs. Stands on two cells, neither of which may be a quay, a bridgehead or a Gate's ground |
| **bridge** | land facing land, cardinally, across at most `Crossings` cells of **aether**, 3 cells of **water**, or a **chasm** — ground 5 slabs or more below the deck, which is how one cliff top is bridged to another |
| **ferry** | between two quays on one body of water, however far apart |

- **Water bodies.** 4-connected over standing fluid, and **a waterfall cuts a
  body in two** — nothing sails up one.
- **Ferry berths** are a domino: a walkable quay within two slabs of sailable
  water, with somewhere to unload behind it. Berths are then **pruned**: the
  reach flood is run once without ferries, and a body keeps its berths only if
  they land in two or more different pieces of that answer. What survives is the
  crossings that exist because the water is genuinely in the way.
- **Shelves** — ground level enough to lay a settlement out on: each cell flat or
  at one lone step. `Width` is the largest square that fits, because a fifty-cell
  ledge one cell deep is nowhere anyone can settle.

### Stage 8 — Gates

A Gate is **one block**: 1 cell wide, 1 deep, 4 slabs tall. Every Domain gets one
`Entry` and one to three `Exit`s, **at most one per edge and on that edge** —
Domains sit on a plane at their world-tree position, so a Gate facing east that is
not the easternmost thing on the map points back over the Domain it leaves.

**Four hanging Gates first, then take away.** This is the shape of the whole
pass. Every Domain is given a hanging Gate on each of its four edges — the
maximum — and the parameters *reduce* that: an Exit the Domain does not need is
deleted, a Gate asked to be a `Land` one is moved from the end of its flight path
down onto its own landing strip, and the Entry is whichever of the four the
world-tree names. There is one site search, not two, and a land Gate is a hanging
site with the portal walked back down it.

The four are chosen as a **set** — a small backtracking search over the best
sites per edge — rather than one at a time. Each Gate has to out-reach every
other on both axes, so a greedy pass has the first Gate move the line the next
one has to beat, and by the third there is nowhere left. Measured, greedy gave
four hanging Gates on **25%** of Domains; set-wise gives **100%**, across all 176
arrangement × character combinations, with no seed needing a re-roll.

| kind | |
|---|---|
| `Hanging` | Floats five cells off the rim (was ten — see the bounding box below); you fly through it. Needs clear air for the last three cells of the approach and a **1 × 3 landing strip** running inland from the coast under it |
| `Land` | the same site with the portal moved down onto that strip. You walk through it, and the ground you walk out onto is the ground a vessel would have landed on |

**Nothing hangs outside the bounding box.** A Domain sits inside an invisible
bounding cube (see the Ecumene page): its walls are the grid, and its **lid is
the same number in slabs** — a Size-cell Domain is at most Size slabs from keel
to peak, which `BoundAltitude` enforces by capping the two big vertical
spenders (mountain rise and keel depth) at the share of the cube they take on a
128 Domain, so 128 is untouched and smaller Domains are proportionally lower.
Everything the
generator builds — the Gates above all, since a hanging portal juts off the rim
toward a wall — must stay inside it. That is why the hanging offset came down
from ten to five: ten cells of overhang eats a tenth of a 128 Domain's box and a
sixth of a 64 one's. The audit checks every Gate against the box (want 0
outside), and the lab's compass overlay (**X**) draws **two boxes**: the faint
cube of the Domain — the grid, the maximal possible extent, the law — and a
gold box tight round the landmass itself, keel to peak, waterfalls and Gates
left out. The gap between the two is the room an arrangement is not using.

**The strip is built, not found.** Three cells of usable ground running inland,
and once the site is chosen those three cells are **levelled** to the height of
the innermost one — the end that joins the island, so the walk off the strip is
exactly what the terrain made it and only the cells running out toward the rim
move. It is never short and never sloped, and the audit asserts both to the
letter rather than to within a slab.

> This is what made four hanging Gates possible. The old strip was 3 × 5 and had
> to be *found* already level to within three slabs, which left a Domain four or
> five viable sites in total — measured, an island offered 14 to 20 cells across
> all four edges, so four Gates were a coincidence. It was also the wrong
> requirement: a Gate is a built structure and so is the ground under it. Gate
> placement is now the one pass that both reads the traversal analysis and changes
> the terrain, so the analysis is run again afterwards.

**The Entry's kind and edge are inputs** (`EntryGate`, `EntryEdge`): a Link joins
two Gates, and a Domain reached by travelling east comes out on its west edge.
Since the four sites are chosen before any of them has a role, the named edge
simply *is* the Entry and the named kind is applied to it — there is nothing to
search for and nothing to trade away. `ExitGates` and `ExitGate` work the same
way by subtraction. All four are still checked in `Unmet`, but nothing now
reaches it: measured over sixteen seeds per request, **every edge, kind and count
is delivered on 100% of seeds, with a mean of 1.00 attempts.**

**A way in and a way out are guaranteed.** Where a coast will not take four Gates
under the full rules, the rules give a rung at a time: the edge band widens to
`RelaxedEdgeBand`, then the corner inset goes, then separation falls to
`CrowdedSeparation` and finally to `MinSeparation`, at which point the dominance
order gives too. The band widens and never disappears — dropping it outright once
put 73% of the Domain behind the player as they arrived, at which point "the south
Gate" names nothing; it is now at most 6%.

An edge with no candidate at all comes back empty, and that is the only way a
Domain ends up with fewer than four sites: a heartland with no north-facing coast
cannot have a north Gate, and no rule can conjure one. On a 128² footprint it
never happens. At 64² it happens on `ThousandIsles` — sixteen islets on a small
map — for 2 of 176 combinations.

### Stage 9 — The roads between the Gates

`Passages`: the cheapest road from the Entry's apron to each Exit's, where cheap
means **needs the least building**. Walking is free; every work costs one point.
`Cost = 0` means you can walk between your two Links on the day you land.

The move set is exactly the reach rule, priced, so the two cannot disagree. Cost
is packed **works first, then cells** — works alone leaves thousands of equally
cheap answers and returns whichever it reached first. A ford costs eight cells of
length, so a road crosses a stream rather than walking down it.

**Five elevators inside fifteen cells is a flight**, and a road with one is
telling you it is going the wrong way. `IslandData.Rough` says whether the Domain
has any. Not a fault: a Domain is allowed to be hard country.

### Stage 10 — Surfaces, anchors and names

`Surfaces.Classify` fills `Material` from height, slope, distance to water and
landform — stone, scree, snow, sand, silt, **grass** (within 3 cells of water),
**meadow** (within 9), **heath** (beyond), dust. It also collects the **feature
anchors**: `CoastCells`, `CliffCells`, `Overhangs`, alongside `Beach`, `Ford`,
`Landings` and `Ferry`. A forest goes "on flat well-watered ground away from the
coast", not at a coordinate, so generation answers the geometric questions once
and content reads the lists rather than each system carrying its own copy of the
terrain rules.

> Three of these were broken until they were measured, and all three failed
> silently. `slope >= 3` returned `Stone` from **both** arms of a ternary;
> `damp <= Damp` and `damp <= Dry` **both** returned `Grass`, so the middle band
> did not exist; and a cell the wetness flood never reached was given exactly
> `Dry`, which reads as still-green — so `Heath`, the driest ground on a Domain,
> was **0.0% of every island ever generated**. The lab's `surface` view and the
> audit's material histogram exist so that the next one of these is caught by
> looking rather than by reading the code.

Both are viewable: `surface` paints the material, `anchors` paints what the
content layer can attach to. The audit prints a share per material, with `NEVER`
beside any that no island produced.

`Names.Give` names the Domain, its districts and its bodies of water. Integers
are right for the generator and wrong for everyone who has to talk about the
output.

### Stage 11 — Guarantees and re-roll

`Generate` checks five things and **rebuilds the island from a derived seed** if
any fails — up to four attempts, keeping the best. Still a pure function of
`(seed, params)`.

| guarantee | why it is the bar |
|---|---|
| exactly one Entry, of the kind asked for | a Link whose ends disagree is not a Link |
| at least one Exit | a Domain with no way onward is a dead end in a tree |
| a road from the Entry to every Exit | an Exit you cannot reach is the same dead end wearing a portal |
| a buildable shelf on the heartland | somewhere the first company can be laid out |
| the heartland covers ≥ 75% of the dry land | below that there is a second island nobody asked for |

Nothing else. Re-rolling for *variety* is how a generator ends up producing one
island.

**The mainland is where you land.** `Mainland` and `Heartland` are re-anchored on
the Entry Gate's apron once the Gates are placed — ranking by area answers a
different question and can name a mainland across a strait from the only way in.

---

## 3. Parameters

`IslandParams` is a `[GlobalClass]` resource; the preset both dev scenes load is
`resources/island_default.tres`. Heights are in **slabs**.

| param | range | drives |
|---|---|---|
| `Size` | 16 – 128 | footprint edge, in cells |
| `Radius` | 0 = auto | land-mask radius |
| `Coverage` | 0 – 1 | share of each blob's disc that becomes land |
| `Irregularity` | 0 – 1 | disc ↔ deeply lobed coastline |
| `Arrangement` | enum | the layout — see Stage 1 |
| `NewArrangements` | bool | whether `Auto` may roll the newer layouts |
| `Character` | enum | which landforms the island is built from |
| `NewLandforms` | bool | whether `Auto` may roll the sculpted characters |
| `LandformMix` | 0 – 1 | the quota, low ground ↔ high |
| `Relief` | 0 – 1 | vertical exaggeration |
| `Hilliness` | 0 – 1 | swells ↔ mounds |
| `RegionScale` | 6 – 40 | typical width of one region, in cells |
| `CliffHeight` | 3 – 16 | one rung of the plateau ladder |
| `PlateauLevels` | 1 – 8 | how many rungs — how terraced the island is |
| `MountainHeight` | 8 – 160 | foot to summit |
| `MesaHeight` / `BasinDepth` | 3 – 24 | clearance above / below the ground around |
| `Rivers` | 0 – 1 | how wet: the bar for a channel to be a river |
| `Lakes` | 0 – 1 | how readily standing water collects |
| `Valleys` | 0 – 1 | how far the ground falls toward a course |

| `Crossings` | enum | Easy / Medium / Hard = 1 / 3 / 6 cells a bridge spans |
| `EntryGate` / `EntryEdge` | enum | **inputs**, set by the Domain that sent you |
| `ExitGates` / `ExitGate` | 0 – 3, enum | how many Links onward, and of what kind |
| `EdgeThickness` / `KeelDepth` / `KeelRoughness` | | the underside |
| `OverhangDensity` / `OverhangDepth` / `ArchSpan` | | Stage 6 |

**`Crossings` is not only an analysis setting.** It decides how wide the straits
between an arrangement's landmasses may open, how far apart the linker leaves the
pieces, and what counts as reachable. An `Easy` Domain has no navigable rivers at
all, because a two-cell river it could not bridge would cut the country in half.

**Not parameters, on purpose:** the free step (1 slab, everywhere — the whole
landform scheme is built on it), `MinRegionArea` (derived from `RegionScale`),
the keel taper, and the settlement thresholds in `Traversal`. A biome that varied
any of them would change what a cliff *means*.

---

## 4. Rendering handoff

`IslandData` feeds the terrain renderer; it does **not** spawn per-slab nodes.

- **Faces**, per column per span: top at `Top + 1`, bottom at `Bottom` (the gap
  under a higher span is the overhang's underside), sides wherever the
  neighbouring column's spans do not cover that slab range.
- **Chunks** of 16 × 16 columns → one `ArrayMesh` + one trimesh collider each, so
  an edit re-meshes one chunk.
- **Water** is a separate translucent surface at `WaterLevel + 1`; only its top
  face and the faces against air need geometry.
- The dev lab does none of this — it draws one scaled `MultiMesh` box per span,
  which is why it costs what it costs. The mesher is the next piece of work.

---

## 5. File layout

```
scripts/generation/            namespace ProjectNikitin.Generation
  Terrain.cs                   CellSize / SlabHeight
  Span.cs, IslandData.cs       the columnar model and everything derived from it
  IslandParams.cs              the §3 knobs
  Noise.cs, FieldOps.cs        FastNoiseLite wrapper; smoothstep, quantile, blur, taper
  IslandGenerator.cs           Generate(seed, params): stages 1-5 and the re-roll
  IslandArrangement.cs         the named layouts
  LandformType.cs              the ten landforms
  TerrainCharacter.cs          which of them an island is built from
  ReliefStyle.cs               where the high ground sits (internal)
  Rivers.cs                    routing, channels, fords, eyots, valleys, falls
  Fall.cs                      one waterfall
  FluidKind.cs                 what a body of standing fluid is (water, goo)
  Geyser.cs                    one jet of a geyser field
  Overhangs.cs                 stage 6
  Traversal.cs                 stage 7: walk, reach, water bodies, berths, shelves
  BridgeEase.cs, Crossing.cs   how far a bridge reaches, and one bridge site
  Ferry.cs                     a berth: a quay, its water, the body it reaches
  Gate.cs, GatePlacement.cs    stage 8
  Passage.cs                   stage 9: the least-works road, and the works on it
  Surfaces.cs, Names.cs        stage 10
scripts/terrain/               (later) the chunked span-aware mesher + colliders
scripts/dev/IslandLab.cs       the lab
scripts/dev/GenerationAudit.cs the headless audit — see the appendix
resources/island_default.tres  the preset both dev scenes load
docs/audit-baseline.json       the last accepted audit numbers
```

---

## 6. What is next

1. **The chunked span-aware mesher + colliders** — the biggest piece left, and
   the only thing that will answer the performance question for real.
2. **Settlement placement.** Everything it needs exists: shelves, berths, roads,
   Gate aprons.
3. **The biome layer** above `Material` — the living things, as opposed to the
   ground.
4. Span-aware pathing, which is what would make an overhang walkable.

The appendix lists the ideas that have been logged and not taken, and the gaps
the last audit found.
