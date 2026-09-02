# Island Generation — the spec

How a Domain is generated, in the order it happens: the model, each stage's
rules and the class that owns them, the parameters, the rendering handoff. **The
reasoning, the things tried and removed, the audit's findings and the ideas not
yet taken are in [island-generation-appendix.md](island-generation-appendix.md)**;
the lab and audit manuals, the repository layout and the glossary are in
`CLAUDE.md`. History is in git.

Status (2026-09-02): every stage below is implemented in `scripts/generation/`,
five footprints — 48², 64², 72², 96², 128² — are supported and audited, and the
chunked mesher is the next piece of work.

---

## 1. The model

A Domain is a square grid of **columns**, `Size` cells on a side. Each column
holds a short list of solid `Span(bottom, top)` runs whose bounds are **slab
indices** — integers on Y, a quarter of a cell each. Almost every column has
exactly one span, keel to surface; the air gap between two spans is an overhang
or an arch, and only `Overhangs` makes one. Three readings of a column:

- `SurfaceLevel(x, z)` — top of the **lowest** span: the ground you stand on.
  Under a lip the ground is underneath, and every rule in the pipeline means the
  ground.
- `KeelLevel(x, z)` — bottom of the lowest span: the underside.
- `EffectiveLevel(x, z)` — the water surface where the column is flooded,
  otherwise the ground. Habitat and anchors are measured against it, because
  they describe what a place looks like.

| constant | value | |
|---|---|---|
| `CellSize` | 1.0 | one cell, in metres. In fiction, about an orchard. |
| `SlabHeight` | 0.25 | one slab. Terrain Y is an integer count of these. |
| free step | **1 slab** | walk it for nothing. Two or more needs building. |

**The free step is the invariant:** terrain built under a one-slab slope limit
is walkable by construction, and every cliff on the island is one some rule put
there on purpose.

**The bounding cube** is `Size` cells across and `Size` slabs tall.
`IslandGenerator.BoundAltitude` caps the mountain rise and the keel depth at the
share of the cube they take on a 128 Domain (40 and 34 slabs), so a smaller
island is proportionally lower, and nothing the generator builds — Gates
included — may leave the cube.

Everything the later stages work out — landform, water and fluid, the habitat
vector, the anchor lists, Gates, roads, names — lives on `IslandData` beside
the spans; `scripts/generation/IslandData.cs` is the field list.

Generation is a **pure function of `(seed, IslandParams)`**: same inputs, same
Domain, every time. Every roll is salted per stage (`SeedHash`; the terrain
stages and the feature stages use different mixers on purpose), and every flood
and scan walks the grid in one order (`Grid`, `Flood`), which is what makes
ties reproducible.

---

## 2. The pipeline

`IslandGenerator.Build` runs the stages in the order below; the stage classes
do the work, and `IslandGenerator` itself owns the fit loop, the settle loop,
the pack and the guarantees. Later stages read what earlier ones left, so
nothing may be reordered.

### Footprint (`Footprint`, `Landmasses`, `Roster`)

The land mask is a set of placed **lobes**, one or more per landmass, laid out
by `IslandArrangement`. Each lobe is an ellipse with a radius that wanders on a
noise field, so no coastline is a circle. Where two lobes meet, the seam is
either left alone (they fuse) or **carved into a strait** — that one flag is the
whole difference between `Ring` and `BrokenRing`. A strait may pinch to a single
cell but never close. Lobes in a **group** fuse with each other whatever the
layout, and only the seams between groups are cut.

| layouts | shape |
|---|---|
| `Single`, `Satellites` | one landmass; with two to four islets round it. The only two that take **bites** |
| `Twins`, `Triplets`, `Archipelago` | two, three, or five to eight comparable landmasses |
| `Shards` | one landmass cracked into four to six pieces by straits narrow enough to read as fractures |
| `Ring` / `BrokenRing`, `Arc` / `BrokenArc` | a ring or a crescent of lobes round a lagoon or a bay, seams fused / carved |
| `Atoll` | beads on a string: rounded islets that all but touch, a step of water between each pair |
| `Cross`, `TShape`, `LShape`, `Star`; `BrokenCross`, `BrokenT`, `BrokenL` | a wide hub with thick arms, **axis-aligned**, so an arm points at an edge and therefore at a Gate; the broken forms part the arms |
| `Fractal` / `BrokenFractal` | a chain of overlapping lobes that turns as it goes; parted, a run of stepping stones |
| `Rosette` | a ring of lobes over a full hub: a run of deep round bays with headlands between |
| `ThousandIsles` | a **quilt**: a jittered 4×4 to 6×6 grid of lobes by footprint, a few holes, every seam a strait — the only spread the linker leaves where it was |
| `Square`, `Rhomb` | blocky fused grids of lobes, axis-aligned and stood on a corner |
| `NShape` | the letter itself: two uprights and the diagonal joining them |
| `Quarters`, `Halves` | four and two roughly symmetric parts split by straight straits |
| `Harmony` | the yin-yang: two grouped chains whose own seams fuse while the S between them is carved |
| `Isthmus` | two broad heads joined by a narrow walkable neck |
| `Reef` | a main island sheltered behind a long thin barrier chain |

Then the mask is finished. On `Single` and `Satellites` **bites** are taken out
by deleting whole regions of a draft partition; diagonal joins are filled
*within* a landmass only (welding two islands that touch at a corner deletes
one); components under `Landmasses.MinIsletCells` are dropped; and
**`LinkLandmasses` nudges the pieces together** until every one faces another,
cardinally, across at most a bridge span (`Crossings`). Whatever the
arrangement, the pieces are linkable, and `FindBridgeSites` records the pairs.

**The fit pass** (`IslandGenerator.FitFootprint`) wraps the stage: the
landmass's bounding rectangle must cover **55–85% of the grid's extent**,
measured on what ships — after the bites, the islet filter and the linker,
whose stray-dragging shrinks every scattered layout. A layout outside the band
is rebuilt scaled about the centre, radii and all, up to three times.

`Roster` resolves `Auto` for the arrangement, the character and the relief
style. The arrangement pool is weighted toward a single landmass, and
`IslandParams.NewArrangements` takes the newer shapes out of that pool without
taking them out of the code — named by hand, a shape is always available and the
flag does nothing.

### Regions and landforms (`Regions`, `Landforms`)

A jittered-grid Voronoi with a domain-warped lookup at `RegionScale`, split
into connected components and merged until nothing is under `MinRegionArea`. A
**relief envelope** in [0, 1] says where this island's high ground lies — a
tilt, a broad flat, a spine or a pair of masses, per `ReliefStyle`. It biases
which rung a region lands on and where mountains cluster, and never shapes
elevation directly. Each region gets a `LandformType` and a **rung** on the
plateau ladder (`RegionPlan`).

**The plateau ladder** is the island's vertical vocabulary. A rung is a whole
multiple of `CliffHeight` slabs above the coastal level, and `PlateauLevels`
says how many rungs exist. Two neighbouring regions on the same rung are joined
ground — the slope limiter reaches across the border and holds it to the free
step. Two on different rungs are a cliff of exactly `CliffHeight`. So
`PlateauLevels` is the knob for how terraced the island is: at 1 the only
cliffs come from mesas, basins and mountains; at 2 (the default) escarpments
are occasional; at 5 the island is a flight of terraces.

| landform | built from | slope limit | reads as |
|---|---|---|---|
| `Plain` | rung + ~1.4 slabs of noise | 1 | flat, buildable, dull on purpose |
| `Hills` | rung + up to ~15 slabs | 1 | rolling, walkable everywhere |
| `Dunes` | rung + a wave along the Domain's wind | 1 | parallel ridges: level along, washboard across |
| `Mountain` | an S-curve off the ground it meets, no rung | none | foothills, steep flanks, a rugged summit |
| `Mesa` | above every neighbour, flat top | 1 | tableland ringed by cliff |
| `Basin` | below every neighbour, flat floor | 1 | the mesa rule inverted |
| `Badlands` | a plain, then **gullies cut into it** (5 slabs) | sculpted | flat fingers, a maze of ravines |
| `Karst` | a plain, then **towers raised out of it** (13 slabs) | sculpted | a floor you walk, columns you cannot |
| `Massif` | a plain, then **concentric terraces** (4-slab risers) | sculpted | a stepped massif; every riser wants a stair |
| `Sinkholes` | a plain, then **round pits punched out** (6 slabs) | sculpted | crossable, while watching your feet |

**The Domain has a wind:** one grain for the whole island, snapped to one of
the eight compass points, so every dune field lies the same way and the
direction can be named. `IslandData.DuneGrain` carries it, `WindFrom` and
`DuneRun` say it in letters (north is −Z, as the lab's compass has it), and
`Habitat` reads it for exposure. It is rolled for every Domain, whether or not
any dunes come out to show it.

**The four sculpted landforms carry cliffs *inside* a patch**, which relief
under a slope limit cannot express. They are cut into a surface the limiter has
already settled and then exempted from it — the mechanism a canyon uses. Two
rules keep them honest: nothing is sculpted within the outermost ring of its
own patch, so every border stays bound; and every cut is a **fixed depth**,
because a tapering gully has a two-slab step somewhere along it by
construction. A lone pit or pillar is undone — one cell is an orchard.

**The character is a quota.** `TerrainCharacter` (`Plains`, `Tablelands`,
`Downs`, `Highlands`, `Badlands`, `Karst`, `Massif`, `Dunes`) says which
landforms the island is built from, and the shares are counts, not dice: every
landform a character names gets at least one region, and the counts are handed
out by rank on the envelope — mountains to the high ground, basins to the low,
hills to the middle — with a per-region jitter so the island is not banded like
a contour map. `LandformMix` tilts the quota from low ground to high. Adjacency
rules follow: a mesa may touch only plains (beside a mountain the mesa gives
way; any other neighbour is flattened, which is the apron that makes a mesa
read as one); adjacent mountains merge into one massif; and a **bridgehead**
region is a plain, since a table or a mountain would ignore the rung agreement
between two banks. The quota is restored last, after everything that flattens a
region. A character is a recipe, not a list of what came out; the lab names
what an island actually got. `IslandParams.NewLandforms` gates the sculpted
characters out of `Auto`'s pool exactly as `NewArrangements` gates the layouts.

### Surface (`Relief`, `StepGrammar`, `Sculpting`)

`Relief.BuildSurface` builds each region's relief on its rung under its own
slope limit: rung plus noise for plains and hills, a wave along the wind for
dunes, an S-curve for a mountain whose foot is seeded from the real surface at
its border and blurred so it joins flush. Then `StepGrammar` settles it:

- **`LimitSlope`** — a Lipschitz projection from above: repeatedly lower any
  cell standing more than its region's limit above a neighbour. It only lowers,
  so it converges, and it reaches *across* a region border wherever the two
  share a rung, which is what closes the cliffs the rules forbid. Cells flagged
  exempt (a sculpt, a canyon floor, a lake bed) are neither lowered nor used as
  a bound — taken as a bound, a lake bed drags its whole rung down into it.
- **`ResolveAmbiguousSteps`** — removes two-slab steps. Two is the worst height
  a step can be: too tall to walk, too short to read as a cliff.

Between the first and second settle, `Sculpting` makes the deliberate
exceptions: `Sculpt` cuts the four sculpted landforms; `CarveCanyon` (one
Domain in five) cuts a cliff across a border where the rules would forbid one;
`CutPasses` does the opposite — a saddle where one plateau sags to meet the
next, so a cliff border has exactly one place you can walk across. Sculpts and
the canyon are exempted from the limiter; a pass is the reverse, a border the
limiter is told to reach across.

### Standing water (`Lakes`)

Lakes go in after the grammar passes they must not undo and before the keel
measures thickness. A patch a canyon or pass cuts through holds no water — it
would fill to the bottom of the cut and pour out. Flooded cells are exempted
from the limiter.

**A lake sinks into the interior of a flat patch (plain, mesa or basin), and
the patch's own untouched rim is the containment.** At least two cells of rim
stay dry all the way round, and the shore inset **wanders** on a noise field a
few cells further, so a lake is the patch's shape read through that field
rather than a scale copy of a Voronoi polygon. The step from rim to water is
one slab — a walkable shore — while the bed drops three or four, clear of the
ambiguous two. One lake per patch, and a patch beside one that holds water
stays dry: a row of pools at slightly different levels reads as flooding.
`Lakes` scales how many; a large flat interior lifts the chance by up to half;
a mesa rarely, and then only a tarn.

**A big pool rolls a shape.** Where the pool is large enough to have an inside
it is still a single body more often than not, else a *thousand-lakes* scatter
of separate pools, a *ring* round a dry islet of its own floor, a *crescent*, a
ragged *cross*, or a *tarn* cropped small; a patch that loses the main roll can
still take a tarn. Every shape is a subset of the pool the containment already
approved, so fragmented islands are untouched while broad flat country comes
out wetter and more varied.

**Fluids.** `IslandData.Fluid` is per column. Water is the default and the only
fluid that behaves: rivers, ferries and fords are water's alone. **Goo** —
violet puddles placed like small tarns in dry flat patches, one to three on
about 30% of islands — makes no rivers (the routing treats it as not-land) and
**never touches water, even diagonally**: no water may stand within a king's
move of goo. Placement guarantees it, the rivers' keep-mask preserves it, and
the audit counts it (`gooTouchesWater`, want 0). Nothing sails, fords or walks
it; it is inert until the biome layer says what it is for. **Geysers** are an
empty hook — `IslandData.Geysers` and the lab's jet rendering exist and nothing
fills them, because where a jet belongs is a fact about the biome.

### Settle (`Beaches`, `Bridgeheads`, `StepGrammar`)

**Beaches.** Where the ground reaches the rim gently — a plain, hills or dunes,
level with its neighbours — the outermost two cells step down a slab per cell,
a whole band at a time so the only new step is the free one. It is the
difference between land that stops and land that *meets* the aether, and it
gives the content layer a shoreline anchor (`IslandData.Beach`). Steep coasts,
mesa rims, basin walls and anything under water are left alone; berth placement
does not read it.

**Bridgeheads.** `LevelBridgeheads` brings the two ends of every crossing to
one level, because a bridge is a run of slabs at one level — it does not climb.
It only lowers, leaves a disagreement over a stair's worth alone rather than
gouge the coast, and will not touch ground beside a lake.

Then the **settle loop**: `LevelBridgeheads`, `LimitSlope` and
`ResolveAmbiguousSteps` are cycled until nothing moves — resolving a two-slab
step can expose a three, closing that a new two, and the bridgeheads need
re-levelling after either. All three only lower, so it terminates.

### Rivers (`Rivers`)

Rivers are cut across the finished patchwork and carry their own step grammar.
Off limits to the water: the bridgeheads (a channel through one un-levels the
crossing) and goo with its whole king's-move neighbourhood.

**Routing is a priority flood inward from the rim.** Terrain under a slope
limit is mostly flats, so steepest descent stalls; flooding inward gives every
cell a downstream neighbour by construction and passes straight through a lake.
**Ties break on a noise field, which is what makes rivers bend** — a
first-in-first-out tie-break is a breadth-first search whose tree is a fan of
straight cardinal rays. Sources are **named**: every summit, and **one outflow
per lake** — the shore cell whose downstream ground is lowest. `Rivers` sets
the upstream area a channel needs before it counts as a river.

- **A river has a bed**: the channel is cut two slabs down and filled to one
  below the ground, so the banks stand proud and the course reads as a channel.
  The banks are cut to match, so no two-slab step is left behind.
- **A stream is crossed at a ford** — one every ~11 cells, where both banks are
  dry and within a slab of the water — and is an obstacle everywhere else.
- **A navigable river** is two cells across, three slabs deep, not fordable,
  and a course earns it below its first real confluence, where a barge would in
  fact get in. It occasionally splits round an **eyot**. **It is a stair of
  pools**: the water is walked from the rim upstream and held level until the
  ground has risen a fall's worth (3 slabs), and its two cells always hold
  **one** level whatever the valley pass did to either (`Settle` runs the
  descend and pair-levelling corrections against each other until both hold).
- **Valleys**: the ground either side sinks toward a course in bands tapered so
  no step exceeds one slab. **The channel sinks with its valley**, one band
  deeper than its own bank — a bank already stands one slab above the water,
  so lowering only the ground beside a river makes a moat, not a valley.
  Ground whose height is the point of it — a mesa rim, a tower, a levelled
  bridgehead — does not come down, and no cell may sink past such a neighbour
  by more than the free step. **`Valleys` acts per watercourse**: each drainage
  (a 4-connected component of the channel network) draws a rank, and the knob
  slides a window across the ranks (`3 × strength − 2 × rank`), the rank
  tilted by the course's own descent so that at mid-slider valleys go to the
  courses that came down through uneven country while a river crossing a plain
  keeps its bare incision. 0 cuts nothing; 1 cuts steep courses in full and
  plain ones shallow. Because the valley and bank passes only lower,
  `Lakes.RaiseSunkenShores` runs afterwards.
- **Every river reaches the rim and pours off it**, because there is no sea.
  Rim falls are drawn spilling past the keel into the aether.
- **A lip pours every way it plausibly can.** A cell that is a fall at all —
  any aether beside it, or a fall's depth of drop along its own course — throws
  a sheet off *every* aether edge (a corner spills both ways) and toward every
  neighbouring *water* a fall's depth below it; a lake does the same where its
  outflow leaves well under its surface. Toward water and aether only, never
  onto dry ground, so nothing new gets wet.

### Keel and pack (`Keel`, `IslandGenerator.Pack`)

The island hangs as a spinning top: a thin lip at the coast (`EdgeThickness`)
thickening inland to `KeelDepth`. The underside is an **absolute** level, not a
thickness subtracted from the surface — that would mirror the relief downward
— and the distance field is sampled through a domain warp so the underside is
not a surface of revolution. Every column is kept at least `EdgeThickness`
slabs thick.

`Pack` writes one span per land column into `IslandData` with its landform,
water level, fluid, canyon and pass flags; records the crossings as built
(`Bridgeheads.RecordCrossings`: a deck level halfway between the banks, so each
end is a one-slab step, and the cells of nothing it covers); drops the rim
falls past the keel, which is only known now; and marks the fords the traversal
analysis reads.

### Traversal (`Traversal`)

Pure analysis. It changes nothing; it is how we find out whether the island is
playable.

- **`Walk`** — what connects on foot: neighbours within the free step. Water is
  not ground; a stream is crossed at a ford. `Areas` lists the walk areas
  largest first, and `Mainland` is the largest.
- **`Reach`** — what connects once you build, with three kinds of works.
  `Reaches` and `Heartland` are the same reading of it.

| works | rule |
|---|---|
| **stair / hoist** | a face of at most 8 slabs. Stands on two cells, neither of which may be a quay, a bridgehead or a Gate's ground |
| **bridge** | land facing land, cardinally, across at most `Crossings` cells of **aether**, 3 cells of **water**, or a **chasm** — ground 5 slabs or more below the deck, which is how one cliff top is bridged to another. A deck is level; its banks are levelled to within a slab of it |
| **ferry** | between two quays on one body of water, however far apart |

- **Water bodies** are 4-connected over standing fluid, and **a waterfall cuts
  a body in two** — nothing sails up one.
- **Ferry berths** are a domino: a walkable quay within two slabs of sailable
  water, with somewhere to unload behind it. Berths are then **pruned**: the
  reach flood is run once without ferries, and a body keeps its berths only if
  they land in two or more different pieces of that answer, so what survives is
  the crossings that exist because the water is genuinely in the way. In the
  audited sample every body can be bridged and no berth survives (`berths` in
  the baseline), so the ferry machinery is currently idle.
- **Shelves** — ground level enough to lay a settlement out on: each cell flat
  or at one lone step. `Width` is the largest square that fits, because a
  fifty-cell ledge one cell deep is nowhere anyone can settle; a shelf is
  **buildable** at `MinShelfArea` cells and `MinShelfWidth` across.

### Gates (`GatePlacement`, `Gate`)

A Gate is **one block**: 1 cell wide, 1 deep, 4 slabs tall. Every Domain gets
one `Entry` and one to three `Exit`s, **at most one per edge and on that
edge** — Domains sit on a plane at their world-tree position, so a Gate facing
east that is not the easternmost thing on the map points back over the Domain
it leaves.

**Four hanging Gates first, then take away.** Every Domain is given a hanging
Gate on each of its four edges — the maximum — and the parameters *reduce*
that: an Exit the Domain does not need is deleted, a Gate asked to be a `Land`
one is moved from the end of its flight path down onto its own landing strip,
and the Entry is whichever of the four the world-tree names. There is one site
search, not two. The four are chosen as a **set** — a small backtracking search
over the best candidates per edge — because each Gate has to out-reach every
other on both axes, and a greedy pass has the first Gate move the line the next
one has to beat until there is nowhere left.

| kind | |
|---|---|
| `Hanging` | floats five cells off the rim; you fly through it. Needs clear air for the last three cells of the approach and a **1 × 3 landing strip** running inland from the coast under it |
| `Land` | the same site with the portal moved down onto that strip. You walk through it, and the ground you walk out onto is the ground a vessel would have landed on |

**Nothing hangs outside the bounding cube** (§1) — a hanging portal juts off
the rim toward a wall, so the Gates are the first thing the lid and the walls
bite. The audit checks every Gate against the cube (`gateOutOfBox`, want 0).

**The strip is built, not found.** A Gate is a built structure and so is the
ground under it: once the site is chosen, the three cells of the strip are
**levelled** to the height of the innermost one — the end that joins the
island, so the walk off the strip is exactly what the terrain made it and only
the cells running out toward the rim move. It is never short and never sloped,
and the audit asserts both to the letter. Gate placement is therefore the one
pass that both reads the traversal analysis and changes the terrain, so
`Traversal.Analyse` runs again when it moved a slab. Each Gate also has an
**apron** — the level ground its strip opens onto (`ApronArea` is a ranking
target, not a requirement) — and the roads run apron to apron.

**The Entry's kind and edge are inputs** (`EntryGate`, `EntryEdge`): a Link
joins two Gates of the same kind, and a Domain reached by travelling east comes
out on its west edge. Since the four sites are chosen before any of them has a
role, the named edge simply *is* the Entry and the named kind is applied to it
— there is nothing to search for and nothing to trade away. `ExitGates` and
`ExitGate` work the same way by subtraction. All four requests are checked in
`Unmet`.

**A way in and a way out are guaranteed.** Where a coast will not take four
Gates under the full rules, the rules give a rung at a time: the edge band
widens to `RelaxedEdgeBand`, then the corner inset goes, then separation falls
to `CrowdedSeparation` and finally to `MinSeparation`, at which point the
dominance order gives too. The band widens and never disappears: a Gate far
from its edge leaves most of the Domain behind the player as they arrive, and
"the south Gate" then names nothing. An edge with no candidate at all comes
back empty — a heartland with no north-facing coast cannot have a north Gate,
and no rule can conjure one — and that is the only way a Domain ends up with
fewer than four sites.

**The mainland is where you land.** `Mainland` and `Heartland` are re-anchored
on the Entry's apron once the Gates are placed (`Traversal.AnchorOn`) — ranking
by area answers a different question and can name a mainland across a strait
from the only way in.

### Roads (`Passages`)

`Passages.Find`: the cheapest road from the Entry's apron to each Exit's, where
cheap means **needs the least building**. Walking is free; every work costs one
point. `Cost = 0` means you can walk between your two Links on the day you
land.

The move set is exactly the reach rule, priced, so the two cannot disagree.
Cost is packed **works first, then cells** — works alone leaves thousands of
equally cheap answers and returns whichever it reached first. A ford costs
eight cells of length, so a road crosses a stream rather than walking down it.

**Five elevators inside fifteen cells is a flight**, and a road with one is
telling you it is going the wrong way. `IslandData.Rough` says whether the
Domain has any. Not a fault: a Domain is allowed to be hard country.

### Habitat, surfaces and names (`Habitat`, `Surfaces`, `Names`)

**The habitat vector** (`Habitat.Measure`) is five bytes per column measuring
the growing conditions, kept as separate axes so the biome layer can compose
them instead of unpicking one score.

| axis | 0 … 255 | how it is measured |
|---|---|---|
| `Moisture` | parched … waterside | breadth-first distance from fresh water (goo waters nothing), wobbled by noise so the bands are not contour lines of the water network, decayed to 1/e at ~6.5 cells |
| `Warmth` | frozen … warm lowland | a **fixed lapse per slab** above the island's lowest ground, anchored to the tallest a mountain can stand at this footprint (`Size × 40/128` — the `BoundAltitude` cap). Absolute on purpose: the top of what a mountain *can be* is always frozen, and a flat island is warm to its highest hill |
| `Ruggedness` | flat … broken | local relief within two cells, 32 per slab, with **water read as its bank** (a slab over its surface): a stream through a plain is flat country and a gorge is still its walls. Measured against the water surface instead, every shore read a slab rougher than the country round it |
| `Exposure` | lee … windswept | tallest cover found walking up to ten cells upwind (`WindFrom`); eight slabs of upwind rise is full shelter. The wind is rolled for every Domain, dunes or not |
| `RimDistance` | — | cells of land to the aether, capped at 255. The setting's own axis: essencecoral grows on rims, and the deep interior is the sheltered country |

**Every geometric question is asked of the effective surface**
(`EffectiveLevel`), because habitat and anchors describe what a place *looks
like* — measured against the bare ground, the bank of a navigable river is a
"cliff" on the strength of its bed.

`Surfaces.Classify` collects the **feature anchors**: `CoastCells`;
`CliffCells` (**brinks**: dry cells three or more slabs over a neighbour's
effective surface — a gorge rim qualifies, a bank does not); `CliffFootCells`
(the ground under those faces); `BankCells` (the walkable wet margin, at most
one slab over the water); `RiverBedCells` and `LakeBedCells` (the flooded
columns, split by whether a watercourse runs over them; a goo puddle is neither,
`Fluid` says where it is); `Summits` (the highest dry cells of genuinely high
country — at least half the mountain cap above the lowest ground, so a flat
island honestly has none — spaced apart); and `Overhangs`; alongside `Beach`,
`Ford`, `Landings` and `Ferry`. The lists overlap freely — a bench on a
mountainside is a brink over one neighbour and a foot under another, and a
brink can be a bank or a summit — and only the lab's flattened view has to pick
one. A forest goes "on flat well-watered ground away
from the coast", not at a coordinate, so generation answers the geometric
questions once and content reads the lists.

`Material` is a **provisional** mapping of the habitat vector — stone, scree,
snow (a warmth floor only real peaks reach), sand, silt, grass, meadow, heath,
dust — kept so the island reads as a place in the lab before the biome layer
exists. The biome layer is expected to replace the mapping; the vector is the
part meant to last. The lab's `surface`, `anchors` and five habitat views paint
all of it, and the audit's `FieldMaps` writes the same as PNGs.

`Names.Give` names the Domain, its districts and its bodies of water, so the
output can be talked about.

### Overhangs and arches (`Overhangs`)

The only stage that gives a column two spans, and it runs after the analysis on
purpose.

- **Undercut.** A columnar model cannot cut sideways into a cliff, so it is
  built the other way round: the columns *in front of* a face of 8 slabs or
  more get a second span up at the cliff top — a lip two slabs thick with four
  slabs of air under it. From below that is an undercut; from above it is the
  cliff edge jutting out.
- **Arch.** Two cliff tops within 2 slabs of each other with a gorge or a
  channel between them, joined by a deck flush with the lower end, up to
  `ArchSpan` cells. Over a gorge or a channel, never over aether: every cell of
  an arch is a column that already has land, a region and a keel.

Both need **backing** — the high side must have two neighbours within a slab of
its own top, and must not be a landform whose whole shape is the wall (karst,
badlands, basin, sinkholes). Without that, a lip off a two-cell karst tower
reads as a hole punched through it.

**What this stage adds is not walkable.** The lip of an overhang is a roof, and
pathing over a two-level column wants spans as nodes rather than columns. That
is a real problem and a separate one — see §6.

### Guarantees and re-roll (`IslandGenerator.Generate`)

`Generate` checks five things and **rebuilds the island from a derived seed**
if any fails — up to four attempts, keeping the best failure (the fewest
unmet) if none passes. Still a pure function of `(seed, params)`.

| guarantee | why it is the bar |
|---|---|
| exactly one Entry, of the kind and on the edge asked for | a Link whose ends disagree is not a Link |
| at least one Exit — as many as `ExitGates` asked for, of the kind `ExitGate` asked for | a Domain with no way onward is a dead end in a tree |
| a road from the Entry to every Exit | an Exit you cannot reach is the same dead end wearing a portal |
| a buildable shelf on the heartland | somewhere the first company can be laid out |
| the heartland covers ≥ 75% of the dry land | below that there is a second island nobody asked for |

Nothing else is re-rolled for.

**Checking it.** `scenes/dev/generation_audit.tscn` (`GenerationAudit.cs`)
runs the real generator over 60 seeds headless, prints the measured guarantees,
and diffs its headline numbers against `docs/audit-baseline.json` — a
machine-written file of the last accepted run, rewritten by `AcceptBaseline`.
Its opt-in sweeps are the `[Export]` properties of `GenerationAudit.cs`, each
documented there, and can be given on the command line after `--`
(`godot --headless scenes/dev/generation_audit.tscn -- Knobs Portraits=<dir>`).
`scenes/dev/generation_checksum.tscn` (`GenerationChecksum.cs`) hashes every
field of `IslandData` for 440 islands — the 60 default seeds; every
arrangement × character at 64²; all five sizes; every Gate request; every
`BridgeEase`; both ends of every knob — and diffs against
`docs/checksum-baseline.txt` (`-- accept` rewrites it). Two runs that print the
same lines built the same islands bit for bit: it is the regression gate for
any change meant to leave generation untouched. Appearance still needs a human
at the lab.

---

## 3. Parameters

`IslandParams` is a `[GlobalClass]` resource; the preset both dev scenes load is
`resources/island_default.tres`. Heights are in **slabs**.

| param | range | drives |
|---|---|---|
| `Size` | 48 / 64 / 72 / 96 / 128 | footprint edge, in cells. Audited at these five (`SupportedSizes`); any 16–128 is accepted, unaudited |
| `Radius` | 0 = auto | land-mask radius |
| `Coverage` | 0 – 1 | share of each lobe's disc that becomes land |
| `Irregularity` | 0 – 1 | disc ↔ deeply lobed coastline |
| `Arrangement` | enum | the layout — see Footprint |
| `NewArrangements` | bool | whether `Auto` may roll the newer layouts |
| `Character` | enum | which landforms the island is built from |
| `NewLandforms` | bool | whether `Auto` may roll the sculpted characters |
| `LandformMix` | 0 – 1 | the quota, low ground ↔ high |
| `Relief` | 0 – 1 | vertical exaggeration |
| `Hilliness` | 0 – 1 | swells ↔ mounds |
| `RegionScale` | 6 – 40 | typical width of one region, in cells |
| `CliffHeight` | 3 – 16 | one rung of the plateau ladder |
| `PlateauLevels` | 1 – 8 | how many rungs — how terraced the island is |
| `MountainHeight` | 8 – 160 | foot to summit, capped by `BoundAltitude` |
| `MesaHeight` / `BasinDepth` | 3 – 24 | clearance above / below the ground around |
| `Rivers` | 0 – 1 | how wet: the bar for a channel to be a river |
| `Lakes` | 0 – 1 | how readily standing water collects |
| `Valleys` | 0 – 1 | how far the ground falls toward a course |
| `Crossings` | enum | Easy / Medium / Hard = 1 / 3 / 6 cells a bridge spans |
| `EntryGate` / `EntryEdge` | enum | **inputs**, set by the Domain that sent you |
| `ExitGates` / `ExitGate` | 0 – 3, enum | how many Links onward, and of what kind |
| `EdgeThickness` / `KeelDepth` / `KeelRoughness` | | the underside; `KeelDepth` capped by `BoundAltitude` |
| `OverhangDensity` / `OverhangDepth` / `ArchSpan` | | overhangs and arches |

**`Crossings` is not only an analysis setting.** It decides how wide the straits
between an arrangement's landmasses may open, how far apart the linker leaves
the pieces, and what counts as reachable. An `Easy` Domain has no navigable
rivers at all, because a two-cell river it could not bridge would cut the
country in half.

**Not parameters, on purpose:** the free step (1 slab, everywhere — the whole
landform scheme is built on it), `MinRegionArea` (derived from `RegionScale`),
the keel taper, and the settlement thresholds in `Traversal`. A biome that
varied any of them would change what a cliff *means*.

---

## 4. Rendering handoff

`IslandData` feeds the terrain renderer; it does **not** spawn per-slab nodes.

- **Faces**, per column per span: top at `Top + 1`, bottom at `Bottom` (the gap
  under a higher span is the overhang's underside), sides wherever the
  neighbouring column's spans do not cover that slab range.
- **Chunks** of 16 × 16 columns → one `ArrayMesh` + one trimesh collider each,
  so an edit re-meshes one chunk.
- **Water** is a separate translucent surface at `WaterLevel + 1`; only its top
  face and the faces against air need geometry.
- The dev lab does none of this — it draws one scaled `MultiMesh` box per span,
  which is why it costs what it costs. The mesher is the next piece of work.

---

## 5. File layout

The tree, with one line per file, is under **Repository layout** in
`CLAUDE.md`; the generation classes are the ones named in the headings of §2.

---

## 6. What is next

1. **The chunked span-aware mesher + colliders** — the biggest piece left, and
   the only thing that will answer the performance question for real.
2. **Settlement placement** — everything it needs exists: shelves, berths,
   roads, Gate aprons.
3. **The biome layer** above the habitat vector — the living things as opposed
   to the ground; the vector and the anchor lists are its inputs, the
   provisional `Material` mapping is its to replace.
4. **Span-aware pathing**, which is what would make an overhang walkable.

The appendix lists the ideas logged and not taken, and the gaps the last audit
found.
