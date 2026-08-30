# Island Generation — technical spec

Status: draft, 2026-08-30. Terrain unit is a **slab** (1×1 footprint, 0.25 tall —
a 1:4 ratio). Stages 1–5 are implemented in slab units
(`scripts/generation/`, `scenes/dev/island_lab.tscn`), including arrangements,
passes, rivers and Gates. Stage 4b overhangs, the remaining feature anchors and
the Stage 6 re-roll guarantees are still pending. The working footprint is
**128 × 128**, which costs about **70 ms** an island.
Living document. Owns the implementation detail behind the Notion page
*Mechanics and Concepts → Generation → Island Generation* (which stays a short
requirements list). Design vocabulary and the world model are in `CLAUDE.md` and
the Notion wiki.

---

## 1. Requirements

From Notion, with the intent spelled out:

1. **Varied size and density** — from one large "continent" filling the Domain to
   a scattered "archipelago" of small islets, and everything between.
2. **Varied surface relief** — from flat plains to mountains. Terrain steps in
   **slab units of 0.25**, so a slope that gains one slab per cell (~14° grade)
   reads as a smooth-ish hillside, not a cliff. Mountains are still stepped, but
   finely; genuine sheer faces only appear where the surface jumps 2+ slabs
   between neighbours (coastlines, terrace edges).
3. **Variable edge thickness** — the island has a finite depth; how many slabs
   deep it is at the rim (the cliff seen from the side, or from a Domain below)
   is tunable and varies across one island.
4. **Multiple habitable shelves** — several roughly-flat levels, like a mountain
   with terraces. A shelf is *habitable* only if it is **≥ 3–4 cells wide** in a
   mostly-flat patch; a 1-cell ledge is not something a settlement can prosper on
   (a few polities may tolerate such terrain — a polity-specific exception, not
   the default). Shelves may **gradually descend** — mostly flat with an
   occasional single-slab step — rather than being dead level.

**Overhangs and arches are wanted** — undercut cliffs, rock shelves, natural
bridges. The data model (§2) represents them directly; Stage 4b generates them.
Non-requirements for v1: branching cave *networks* / horizontal tunnels (would
need voxels); half-slabs; multiple stone/soil strata; more than one terrain
material tier.

---

## 2. Data model — per-column span list

A Domain footprint is **128 × 128 cells** (see §7 on the size disagreement). A
dense 3D slab array is a non-starter and per-slab nodes are impossible. Terrain
is stored **per column** as a short list of vertical solid runs:

- `Spans[x,z]` — an array of `Span`, where `Span { short Bottom; short Top; }` is
  one **contiguous** run of solid slabs, `Bottom..Top` inclusive, bounds given as
  **slab indices** on Y (world Y = `index * Terrain.SlabHeight`).
- Spans in a column are sorted bottom-up, never overlapping, never touching
  (adjacent runs merge into one). An empty array means no land in that column.
- **The air gap between two spans is what makes an overhang** — undercut cliff,
  rock shelf, or (across several columns) a natural bridge / arch. Most columns
  have exactly one span; only cliff and arch areas have two or three.
- `Material[x,z]` — `byte`, surface material of the top span. Single tier for now
  (grass over dirt).

Derived, not stored: `SurfaceLevel(x,z)` = `Top` of the highest span (`NoLand` if
none — the walkable surface); `KeelLevel(x,z)` = `Bottom` of the lowest span;
local rim thickness in slabs = `Top - Bottom + 1` of the relevant span.

**Storage:** a jagged 2D array (`Span[Size,Size][]`). Most columns are one span;
call it ~1.3 spans/column average → ~128·128·1.3·4 B ≈ **90 KB per island** —
orders of magnitude under a dense voxel grid, whole island resident, no
streaming. If per-cell array churn becomes a problem, repack as CSR: one `Span[]`
blob plus `int[,] start` / `byte[,] count`.

**Still outside the model:** branching cave *networks* and horizontal tunnels
within a single column footprint — those need voxels or another structure. A
single vertical gap per column (overhang / shelf / arch underside) is fully
representable; that covers the wanted decorations.

Coordinates: `X, Z ∈ [0, 128)`. `Y` is a signed slab index (`short`), `Y = 0` the
nominal float level the island sits around — it builds up into mountains
(hundreds of slabs) and down into a keel. The Ecumene's "invisible bounding cube"
is 128 units on a side = **512 slabs** tall; generation stays well inside that.

Output type (shape, names provisional):

```csharp
public readonly record struct Span(short Bottom, short Top);   // slab indices

public sealed class IslandData
{
    public int Size;                 // 128
    public Span[,][] Spans;          // [Size, Size] -> runs, bottom-up, disjoint, may be empty
    public byte[,]   Material;       // [Size, Size], top-span surface material

    public short[,]  WaterLevel;   // top slab of standing water, NoLand = dry (§4c)
    public bool[,]   Canyon;       // columns a trench was cut through

    // walkability & shelves (§5, filled by Traversal.Analyse)
    public int[,]          Walk;      // walk-area id, Traversal.Water, or -1
    public List<WalkArea>  Areas;     // largest first; Areas[0] is the mainland
    public int             Mainland;
    public int[,]          ShelfId;
    public List<Shelf>     Shelves;

    // still to come (§5)
    public List<Vector2I>   CoastCells;
    public List<Vector2I>   CliffCells;
    public List<Vector2I>   Overhangs;   // columns with more than one span
    public GateAnchor       GateAnchor;

    public const short NoLand = short.MinValue;

    public short SurfaceLevel(int x, int z);   // Top of highest span, or NoLand
    public short KeelLevel(int x, int z);      // Bottom of lowest span
}
```

---

## 3. Parameters

Generation is a **pure deterministic function**
`IslandGenerator.Generate(int seed, IslandParams p) -> IslandData`. Same inputs →
identical output, no time / random-device dependence, so it is safe for future
multiplayer or replay use even though that is not a current requirement.

`IslandParams` is a `[GlobalClass]` resource. Heights are in **slabs**.

| param | rough range | drives |
|---|---|---|
| `Seed` | int | all noise offsets |
| `Size` | 16 – 128 cells | footprint edge length |
| `Radius` | 0 or 20 – 120 cells | land-mask radius (0 = auto, `Size * 0.45`) |
| `Coverage` | 0 – 1 | fraction of the bounding disc that ends up as land |
| `Irregularity` | 0 – 1 | disc ↔ elongated, deeply lobed coastline |
| `Arrangement` | `IslandArrangement` | Single / Satellites / Twins / Triplets / Archipelago / Atoll |
| `Character` | `TerrainCharacter` | which landforms the island is built from; `Auto` per seed |
| `LandformMix` | 0 – 1 | how the character's landforms are shared out: low ↔ high ground |
| `Relief` | 0 – 1 | vertical exaggeration of every landform's relief |
| `Hilliness` | 0 – 1 | swells ↔ mounds: hills amplitude, and the surface's fine detail |
| `RegionScale` | 6 – 40 cells | typical width of one landform region |
| `CliffHeight` | 3 – 16 slabs | spacing of the plateau ladder; every region border cliff |
| `PlateauLevels` | 1 – 8 | rungs on that ladder above the coastal level |
| `MountainHeight` | 8 – 160 slabs | rise from a mountain's foot to its summit |
| `MesaHeight` | 3 – 24 slabs | how far a mesa top clears the ground around it (capped at 2×) |
| `BasinDepth` | 3 – 24 slabs | how far a basin floor sits below that ground (capped at 2×) |
| `Rivers` | 0 – 1 | how wet: how much catchment a channel needs to become a river |
| `EntryGate` | `GateKind` | Land / Hanging — **an input**, set by the Domain that sent you |
| `ExitGates` | 0 – 3 | Links onward; 0 picks 1–3 from the seed |
| `EdgeThickness` | 1 – 32 slabs | column depth at the coastline (the thin lip) |
| `KeelDepth` | 4 – 256 slabs | extra depth under the innermost interior |
| `KeelRoughness` | 0 – 1 | craggedness of the underside, and how far its contours wander |
| `OverhangDensity` | 0 – 1 | how often tall cliff faces get undercut or arched *(Stage 4b)* |
| `OverhangDepth` | 1 – 8 cells | how far an undercut reaches back from the face *(Stage 4b)* |
| `ArchSpan` | 2 – 10 cells | widest gap a natural bridge will span *(Stage 4b)* |

**Not parameters, on purpose.** Some numbers define the terrain *grammar* rather
than an island's looks, and a biome that varied them would change what a cliff
*means*:

| | |
|---|---|
| free-step size | 1 slab, everywhere. The whole landform scheme is built on it. |
| `MinRegionArea` | derived: `0.215 · RegionScale²`. What counts as a sliver depends entirely on how big a patch is meant to be, so the two were never independent. |
| `KeelTaper` | constant 0.85. It shapes a surface nobody stands on, and every value in its old range read the same from above. |
| `MinShelfArea` / `MinShelfWidth` / `MinDistrictArea` | `Traversal` constants — 24 cells, 3 cells, 20 cells. Settlement thresholds; they belong to Stage 6's guarantees, not to a biome. |

Four knobs were removed. `Roughness` set only the fBm gain, and the ≤1-slab
slope limiter destroyed most of what it produced — it is folded into `Hilliness`,
which is a knob you can see. `MinRegionArea` and `KeelTaper` became the derived
values above. **`Fragmentation`** is replaced by `Arrangement`: one float asked
to mean both "how broken up" and "into how many pieces" and delivered neither,
and it was untested above the 0.25 continuity threshold anyway.

**The preset lives at `resources/island_default.tres`**, and both dev scenes
point at it, so the audit measures the same island you are tuning.

`IslandParams` are themselves expected to be produced per Domain from its biome /
archetype (arid mesa vs. lush lowland …). That mapping is **Domain generation**,
a separate concern — this spec assumes `IslandParams` arrive ready.

---

## 4. Pipeline

### Stage 1 — Footprint mask → `bool Land[x,z]`  *(implemented)*

**The footprint is a set of placed blobs, one per landmass**, chosen by
`IslandArrangement`: `Single`, `Satellites`, `Twins`, `Triplets`, `Archipelago`,
`Atoll`. Laid out deliberately rather than thresholded out of noise, because
"one big island with three satellites" is a thing a Domain wants to *be*, and no
single fragmentation number produces it reliably.

1. **Irregular silhouette** per blob (scaled by `Irregularity`), which is what
   stops the result reading as a disc:
   - an **ellipse** at a seed-chosen aspect and rotation, so the island has a
     long axis;
   - a **lobed radius** — `rEff = radius * (1 ± 0.55 * lobeNoise(θ))` — giving
     bays, capes and a wandering coast at the largest scale. The lobe noise is
     sampled at `(cos θ, sin θ)` rather than at `θ`, so it is seamless where the
     angle wraps;
   - one or more **bites**, applied *after* the partition — see below. Bites are
     for a single landmass only: a bite takes a third of what is left, measured
     over the whole footprint, and on a `Twins` layout that is most of one island.

   > **Both of those are damped when there is more than one blob** — the ellipse
   > to 1.22, the wander to 0.22 of `Irregularity`. At full amplitude a lobe
   > reaches half again as far along its long axis as its radius says, so it is
   > the *noise*, not the layout, that decides whether two islands are two
   > islands: a third of `Twins` came out as one peanut. Placement has to be the
   > decision. `BuildMask` also re-rolls with a wider spread, up to three times,
   > if the layout did not deliver the landmasses it promised.
2. **Radial falloff** against that effective radius: `d = dist / rEff`;
   `fall = 1 - smoothstep(0.40, 1, d)`. Keeps land off the bounding-box walls.
3. **Domain-warped fBm** `n(x,z) ∈ [0,1]` — warp amplitude rises with
   `Irregularity`, so coastlines meander at the small scale too.
4. **Fragmentation:** `frag = lerp(1, ridgedBlobNoise, Fragmentation)`; multiplied
   in, so higher `Fragmentation` breaks the mass into separated high spots.
5. `field = fall * (0.35 + 0.65*n)`, where `fall` comes from the **nearest** blob
   — a minimum, not a sum, so two islets do not fuse into a peanut merely by
   being close. `threshold` = the `(1 - Coverage)` quantile of `field`
   (one sort — no iterative search), taken **over the disc `d < 1` only**,
   **and per blob**: one global cut makes `Coverage` a fraction of the whole
   layout, so a lobe sitting under a low patch of the shape noise is simply
   deleted. Sampling the whole grid pads the population with guaranteed
   zeroes — the empty aether around the island — which pins the threshold at 0
   and makes `Coverage` inert for most of its range.
6. `Land = d < 1 && field > threshold`, with a one-cell border kept empty so
   every land cell has a reachable coast for the Stage 4 BFS.
6b. **Bites, taken patch by patch.** A draft partition (stage 2b) is run first,
   and each bite deletes every region at least half inside it — rather than
   subtracting the bite's shape from the mask. Subtracting leaves the bite's own
   outline on the coast: an arc, however softly its edge is faded, and it slices
   in half whatever patches it crosses. Deleting whole regions leaves a coastline
   that runs along region borders, which are warped Voronoi edges and so already
   organic. Measured over 60 seeds — spread of the new coast's distance from the
   bite centre, where an arc scores near zero:

   | | radial spread |
   |---|---|
   | subtracting a soft disc | 0.012 |
   | deleting whole patches | **0.171** |

   It also makes two bites on one island differ in size (spread 0.52 of the mean),
   because what each removes depends on the patches it lands on rather than on
   its own radius. About a third of first bites are placed well inside and kept
   small, which takes out interior patches and punches a **hole** through the
   island: 7 of 60 seeds.

   Two guards, because one is not enough: no single bite may take a third of what
   is left, *and* the bites together may not drop the island below 60% of the land
   it started with. Three bites each under a per-bite cap still compound.
   Measured: land kept has a minimum of 0.61 and a median of 0.90. Regions are
   then rebuilt over what survives — the partition is deterministic, so this just
   re-derives it.
7. **Continuity.** A `Single` Domain is reduced to its largest 4-connected
   component. Every other arrangement keeps its pieces — **and then has to earn
   them.**

   `LinkLandmasses` nudges the pieces bodily together until every one faces the
   next across at most `Traversal.MaxBridgeSpan` (2) cells, cardinally, so an
   archipelago is an island you build your way across rather than a set of
   separate worlds. Translating preserves each islet's shape, where widening it
   or filling the gap with a spit would leave a visible causeway. Two islands set
   diagonally apart can miss each other on *both* axes, so the linker first
   slides one sideways into a line of sight. It never deletes a stray mid-loop —
   doing that was how an arrangement quietly delivered fewer landmasses than it
   promised.

   The crossings it settles on are kept as `IslandData.Bridges`, and Stage 2c
   unions the two banks of each into one **rung group** so they agree on a level.
   They are not neighbours — there is aether between them — so nothing else would
   make them agree, and a crossing whose far bank stands eight slabs higher is
   not a crossing.

   `CloseDiagonalJoins` fills a corner where two cells of the **same** landmass
   touch only diagonally. Only the same landmass: welding two separate islands
   that touch at a corner does not heal anything, it deletes an island.

   Audited, 60 seeds per arrangement: **every arrangement is 100% linked.**

   | | masses per island | |
   |---|---|---|
   | `Single` | 1.0 | |
   | `Twins` | 1.9 | some seeds still fuse |
   | `Triplets` | 2.9 | |
   | `Satellites` | 3.5 | |
   | `Archipelago` | 5.8 | |
   | `Atoll` | 5.7 | a broken ring, so some islets merge by design |

   > **Performance.** `FacingPairs` finds every crossing in one sweep of the grid.
   > Asking pair by pair is the obvious shape and it is cubic — `Triplets` spent
   > **407 ms** an island re-scanning the field on every nudge, against 31 ms for
   > everything else.
8. *Not yet:* hole fill.

### Stage 2 — Raw height → `float H[x,z]` in slabs, land only  *(implemented)*

> **Elevation is not a smooth field that gets quantised.** Quantising makes step
> sizes an accident of the gradient: terrain comes out uniformly two-to-three
> slabs rugged — the worst case, since a 1-slab step is free and anything more
> needs infrastructure, so *nothing* is freely walkable and nothing reads as a
> deliberate cliff. Worse, under a radial envelope the contours of that field are
> rings, so snapping them to levels produces visible concentric banding with flat
> nothing in between. Both were observed. Stage 2 is therefore built out of
> **regions with assigned characters**, and the envelope only says where the high
> ground tends to be.

**2a — Relief envelope** ∈ `[0,1]`, the macro trend only. `ReliefStyle` selects:

| style | form |
|---|---|
| `CentralPeak` | one dome on the footprint centre |
| `OffsetPeak` | dome pushed off-centre; one flank becomes a long slope |
| `TwinPeaks` | two unequal domes, usually with a saddle between |
| `Ridge` | a spine across the island: steep flanks, tapered ends |
| `Plateau` | broad tableland ringed by a steep drop |
| `Tilted` | one edge high, sloping steadily to the far side |

Mixed 70/30 with a low-frequency noise so even `CentralPeak` is not perfectly
concentric, then multiplied by a **coastal taper** (0.45 at the shoreline
recovering over ~3.5 cells, measured on `toCoast`). `Auto` hashes the seed.

**2b — Region partition.** Jittered-grid Voronoi at `RegionScale`, with the
lookup position domain-warped so borders meander rather than being straight
Voronoi edges. Then, so the island reads as a blanket of legible patches:

1. split each Voronoi cell into **connected components** — the coastline routinely
   cuts one in two, and a region must be a single patch;
2. **merge** any component under `MinRegionArea` into the neighbour it shares the
   most border with, repeatedly, until none is left. Isolated islets that have no
   neighbour to merge into are left alone.

Measured over 30 islands: 424 patches, smallest exactly `MinRegionArea`, median
208 cells. No undersized patch survives.

**2c — Region assignment.** Each region gets a `LandformType` and a rung on a
plateau ladder spaced at `CliffHeight`:

| landform | built from | slope limit | reads as |
|---|---|---|---|
| `Plain` | rung + ~1.4 slabs of noise | 1 | flat, buildable, crossable |
| `Hills` | rung + ~9 slabs of noise | 1 | rolling but walkable everywhere |
| `Mountain` | **S-curve, no rung of its own** | none | see below |
| `Mesa` | **above every neighbour** + flat top | 1 | tableland ringed by cliff |
| `Basin` | **below every neighbour** + flat floor | 1 | the mesa rule inverted |

`TerrainCharacter` sets the base weights. Real terrain does not mix every
landform at once, so each character is one plausible combination, with plains as
the constant running through all of them:

| character | contains |
|---|---|
| `Plains` | plains only |
| `Tableland` | plains + mesas + basins |
| `Downs` | plains + hills |
| `Highland` | plains + hills + mountains |

**It is the only landform knob.** `ReliefStyle` used to be a second one and was
nearly inert — it only nudged rungs, which reads as elevation shifting between
patches for no reason. It is now internal: each character picks from the subset
of high-ground shapes that suits it (a `Highland` draws `Ridge`, `TwinPeaks` or
`OffsetPeak`; `Plains` draws `Tilted` or `Plateau`), because where the high
ground sits is a consequence of the character, not a separate decision.

Every weight is then keyed to the envelope — plains favour low ground, hills the
middle, mountains high ground only, mesas middling-high. That is what gives the
style visible work: it decides where the high ground is, and the high ground
decides what grows there.

The rung follows the envelope plus a *small* nudge — a rung that is a pure
function of the macro shape reads as contour banding, but a large nudge makes
neighbours disagree constantly and every disagreement is a cliff.

**By quota, not by dice.** The weights are turned into whole region *counts*
(largest remainder), and every landform the character names is guaranteed at
least one region. Independent per-region draws over ten-odd regions have enormous
variance, and it showed: a `Highland` came out with no mountains on one seed and
with mountains but no hills on the next, which makes the character an unreliable
promise. `LandformMix` tilts the counts along a low-to-high axis — 0 pushes an
island toward plains and basins, 1 toward hills, mesas and mountains — leaving
0.5 as the character's own balance.

The counts are then handed out **by rank on the envelope**: mountains to the
highest ground, mesas next, basins to the low and sheltered, hills to what is
left in the middle, plains to the remainder. Rank alone would band the island by
elevation like a contour map, so the sort key carries a per-region jitter.

The **cordillera** is the exception that wants no jitter. Weights alone rarely
put two mountains side by side, so every peak came out solitary; taking the
mountain quota as a strict top band of the envelope makes the chosen regions
adjacent, and the massif merge welds them into one. That happens on 90% of
`Highland` islands under a `Ridge` envelope and 55% otherwise — under `Ridge` the
band is a spine, so the chain crosses the isle; under a dome it is a central
massif.

Audited over 60 islands: **`Downs` delivers hills on 100% of its islands,
`Highland` delivers both hills and mountains on 100%, `Tableland` delivers mesas
and basins on 100%.** The one landform that still misses is a basin on a
`Highland` (61%), which is adjacency doing its job — there is not always a patch
that can hold one without touching a massif.

**Adjacency.** A mesa or basin may only touch plains (or each other). Where one
abuts a mountain it gives way — a massif is the larger feature — and any other
neighbour is flattened to plain, which is what puts the apron of open ground
around a mesa that makes it read as one. Hills, plains and mountains may touch
freely.

That repair can take out the *last* region of a landform the character promised —
a `Downs` island whose single hills patch happened to touch a basin came out as
plains, which is what an earlier audit's 93% was. So the quota is checked again
afterwards and one region is restored: the largest plain that satisfies the
adjacency rules on its own, since nothing repairs them a second time.

**Basins** are the mesa rule with the sign flipped: assigned highest-envelope
first (so a run of them steps *down* one after another), sunk to
`min(neighbour rung) − BasinDepth`, flat floor, inward-facing cliff all round.
They favour low ground where mesas favour middling-high.

> **They used to be extinct** — 1 across 60 islands. The weight was multiplied by
> a coastal `smoothstep` on the region's **minimum** distance to the void, and
> almost every patch touches the coast somewhere, so the product was zero for all
> but a handful. Under the quota scheme shelter is a *ranking* term on the
> region's **mean** distance instead, not a gate: 46 basins over 60 islands, and
> every `Downs` and `Tableland` island has one.

**Massifs.** Adjacent regions of the same type are unioned for `Mountain` and
`Mesa`. A mountain penned inside one region has only a handful of cells of run
for its entire rise, which leaves no room for a foot — it can only be a wall.

**Mountains** take no rung of their own. A rung is a region's *base* level, but
the terrain beside it sits on top of its own relief, so starting a mountain from
a rung drops it below the plains it is supposed to rise out of — the foothills
begin with a descent. Instead the massif is hung off a **foot field**: seeded
from the real surface height each border cell touches, propagated inward, blurred
so fronts meeting inside leave no seam, then restored to at least the seeded
height (the blur is an average and would otherwise pull a border cell under its
own neighbour). Then

`h = foot + MountainHeight · u²(3−2u)`

with noise near the summit only. Rounding that curve to slabs *is* the step
profile, because step size is just the gradient — measured over 30 islands:

| distance into massif | mean step | reads as |
|---|---|---|
| 0.0 – 0.1 | 0.88 slabs | foothills, free to walk |
| 0.2 – 0.3 | 3.00 slabs | steepening |
| 0.4 – 0.5 | 4.15 slabs | consecutive multi-slab risers |
| 0.9 – 1.0 | 1.55 slabs | flatter but rugged summit |

> A mountain that *begins* with a cliff is a mesa with hills on it.

`MountainHeight` is literal: measured rise above the foot is a median 43 slabs
for a setting of 40, and no border cell now sits below the ground it meets
(0 of 1732, against 14.6% when mountains hung off a rung).

**Mesas** are placed after every other rung is fixed, at
`max(neighbour surface) + MesaHeight` — measured against neighbours' plateau
*plus* their relief amplitude, so a mesa clears the tops of anything beside it,
not just its base. `MesaHeight` is likewise literal: a mesa is a step up, not a
peak.

**The ground a mesa stands on and the mesas beside it are measured separately.**
Lumping them together is what let a chain compound to **22 slabs**: each mesa
cleared the last by a full `MesaHeight`, and at five slabs a time a stepped
tableland becomes a tower. Now a neighbouring mesa is cleared by *half* a step,
and nothing may stand more than `2 × MesaHeight` above the plain the group rests
on. Basins are capped the same way, inverted.

Audited over 60 islands: 22 mesas clearing their neighbours by min 5 / median 6 /
**max 7** slabs, and 46 basins dropping min 4 / median 4 / max 7 — **none level
with or below what it borders, none touching a mountain or any other landform
kind.**

### Stage 3 — Surface synthesis → `short SurfaceLevel[x,z]`  *(implemented)*

1. `surface = regionPlateau + sharedNoise * amplitudeField`. The noise field is
   **shared across every region**, so relief is continuous over a border. The
   amplitude is a *blurred field*, not a per-region constant: a hills patch
   swinging over nine slabs beside a plain swinging over one still steps several
   slabs at their shared border, which would be a cliff where the rules forbid
   one. Blurring makes hills subside into plains instead.

   **Where cliffs may fall.** A rung difference between neighbours *is* a cliff,
   so the rule — cliffs only between two plains, two mesas or two basins — is
   enforced structurally: every other adjacent pair is unioned into a rung group,
   and each group gets one rung.

   Audited over 60 islands, **every cliff is now one somebody asked for:**

   | border | cliffs | |
   |---|---|---|
   | plain-basin | 1991 | the basin's own escarpment |
   | plain-plain | 1655 | by the rule — two rungs of the ladder |
   | plain-mesa | 1085 | the mesa's own escarpment |
   | canyon | 12 | a cut, not a step; allowed between any pair (§4 step 3) |
   | mesa-mesa | 3 | by the rule |
   | basin-basin | 2 | by the rule |
   | hills borders | **0** | *was 14* |
   | mountain borders | **0** | *was 3* |

2. **Slope limit** — a Lipschitz projection from above: repeatedly lower any cell
   standing more than the limit above a neighbour. It only lowers, so it
   converges.

   It runs *within* a region, and **across a border wherever the two sides were
   unioned into one rung group** — which is precisely the statement that no cliff
   belongs there. That second half is what closed the hills leaks. Sharing a rung
   equalises a border's *base*, but hills carry more relief than the plain beside
   them, and blurring the amplitude field narrows that gap without closing it; a
   few hills borders still reached three slabs. Enforcing the limit on the border
   itself closes it by construction rather than by tuning.

   Cells flagged **exempt** — a lake bed, a canyon floor — are neither lowered nor
   used as a bound. A bed sits three or four slabs under its own shore and a
   canyon floor seven under its lip; take either as a bound and the limiter drags
   the whole rung group down into it a slab per cell. That was observed: plains
   ended up *below* the basins they bordered, and the escarpment read inside out.
3. **Canyon** *(20% of seeds)* — a trench `1.8 × CliffHeight` deep and 2–4 cells
   wide, carved **along a region border**, preferring one that is otherwise
   invisible: same landform, same rung. The seed set already spans both sides of
   the border, so it is two cells across before the BFS widens it at all — a
   canyon is a crack, not a valley. A canyon is a boundary made legible, so cutting one straight across
   a region would undo the distinction the patchwork exists to draw. Unlike a
   cliff, a canyon may fall between **any** two patches — it is a cut, not a step,
   so it does not imply the two sides sit at different levels.
4. **Resolve two-slab steps** outside mountains, by lowering the higher cell.
   Two slabs is the worst height a step can be — too tall to walk, too short to
   read as a cliff — so it is neither free movement nor a deliberate obstacle.
   Three or more is left alone.

   Steps 2 and 4 are **run alternately to a fixpoint**, not once each. Resolving a
   two lowers a cell, which can leave a *three* behind it on a border the rules
   forbid a cliff on; closing that can in turn expose a new two. Both passes only
   ever lower, so alternating them terminates.

Audited over 60 islands (224k adjacent pairs, real generator — see
`scenes/dev/generation_audit.tscn`): **90.8% of steps are free (0–1 slabs), 8.7%
are cliffs (3+), and two-slab steps outside mountains number 0 in 204,130.** The
free share dipped a point from the previous audit only because basins now exist,
and every basin brings an escarpment.

*Not yet:* gentle descent within a plateau.

### Stage 4 — Keel / underside → one span per column  *(implemented)*

A Domain hangs in aether as a **spinning top**: a thin lip at the coastline
descending inland to a deep keel under the interior.

1. `toCoast[x,z]` = distance in cells to the nearest non-land cell, as a **smooth
   float field**: a chamfer (3,4) transform (4-neighbour BFS is Manhattan, whose
   contours are diamonds — that is where the *pyramid* came from) followed by a
   blur to remove the integer steps. Computed once, shared with Stage 2's coastal
   taper.
2. The field is sampled at a **domain-warped position**, amplitude
   `radius * (0.25 + 0.45 * KeelRoughness)`. This is the step that stops the
   underside being a surface of revolution: displacing where the field is read
   bends its contours, whereas adding noise to the depth afterwards only ripples
   a shape that is still concentric. Measured on a test island, warping raises the
   spread of keel depth within a radial band from 1.2 to ~5 slabs while leaving
   the rim-to-centre trend untouched.
3. `t = sampled / maxToCoast`, swayed by a low-frequency noise — 0 at the
   shoreline, 1 at the innermost point. Normalising against the island's *own*
   maximum makes the keel come to a tip rather than bottoming out on a plateau.
4. `depth = EdgeThickness + KeelDepth * scale * t^KeelTaper`, where
   `scale = clamp(maxToCoast / (radius * 0.75), 0.25, 1)` shrinks the keel for
   small landmasses — an islet gets an islet's keel, not a full-length spike.
   Noise scaled by `KeelRoughness * depth` crags the underside while leaving the
   lip clean.
5. `keel = min(-round(depth), SurfaceLevel - EdgeThickness)`; emit
   `Spans[x,z] = [Span(keel, surface)]`. Stage 4b is the only stage that produces
   more than one span.

> **The underside is an absolute level, not a thickness below the surface.**
> Subtracting a thickness mirrors the surface's relief downwards, so any central
> peak lifts the underside with it and the island ends up **concave** — a bowl,
> thickest at the rim, with the interior hanging above the coastline's own keel.
> That is what the first implementation did. Driving the floor to an absolute
> depth and clamping for minimum thickness gives the intended silhouette.

### Stage 4b — Overhangs & arches (splits / adds spans)  *(not yet)*

Runs only near `CliffCells` and short gaps between landmasses. Bounded so the
island stays legible:

1. **Undercuts.** For a cliff column, sample a low-frequency 3D noise over its
   span; where it crosses a threshold in a horizontal band *within* `OverhangDepth`
   of the exposed face, delete that band, splitting the span — the upper part is
   an overhang. Keep ≥ `MinLedgeThickness` slabs solid above and below each cut;
   ≤ 1–2 cuts per column; frequency ∝ `OverhangDensity`.
2. **Arches / natural bridges.** Find a gap ≤ `ArchSpan` cells between two land
   columns whose top spans are within a few slabs of each other; add a bridging
   span a few slabs thick near the top, air beneath. Skip arches that would block
   a `GateAnchor` sightline.

Everything added here is real walkable, rendered, collidable terrain.

### Stage 5 — Walkability & shelves  *(implemented)* + feature anchors *(not yet)*

`Traversal.Analyse(IslandData)` is **pure analysis**: it reads finished terrain
and changes nothing, so it can run on any `IslandData` and sits outside the
pipeline proper. It fills two things.

**Walk areas** — `int[,] Walk` plus `List<WalkArea> Areas`, largest first, so
`Areas[0]` is the mainland. Two columns share an id exactly when you can walk
between them: 4-connected, land, not flooded, and `|Δsurface| ≤ 1`. That single
edge test *is* the traversal rule.

> **Most walk areas are not places.** A mountain flank climbs in 4–5 slab risers,
> so each contour bench between two risers is its own connected set — 2,745 of
> them across 60 islands, against 194 areas of any size. `WalkArea.IsDistrict`
> (≥ `MinDistrictArea`, 20 cells) is the line between a district and **broken
> ground**, and the lab paints all broken ground one grey so a massif reads as
> the single impassable mass it is rather than as fifty stripes.

**Shelves** — `int[,] ShelfId` plus `List<Shelf> Shelves`: contiguous walkable
ground all at *one* slab level. `Width` is the largest square that fits inside,
by repeated 8-way erosion, and `Buildable` means ≥ `MinShelfArea` (24 cells) and
≥ `MinShelfWidth` (3) across. Erosion rather than area is what requirement §1.4
actually asks for — a ledge fifty cells long and one deep has ample area and is
still nowhere anyone can settle.

Audited over 60 islands: **531 buildable shelves, at least one on every island**,
with a widest square of min 7 / median 11 / max 19 cells. Flat ground is not the
scarce resource.

**Reach areas** — the same connectivity question asked of a player who can
*build*. Two ground cells join when the face between them is at most
`InfrastructureStep` (8 slabs — a stair or a hoist), or when land faces land
across at most `MaxBridgeSpan` (2) cells of water or aether, cardinally. A
diagonal crossing is not something you build on a square grid without it reading
as a mistake. Bridge ends may differ by the full stair height: a bridge is a
built thing and so are its approaches, and holding decks level made most
archipelagos unlinkable — two islets have no reason to agree on a rung.

> **This is the connectivity the design should be held to.** Walking is the
> *free* case; a cliff is meant to cost something, not to be a wall.
>
> | | on foot | with building |
> |---|---|---|
> | land on the largest area | 55% | **93%** |
> | median island | 51% | **94%** |
> | islands that are one whole | — | **57 of 60** |
> | mesa tops | 8% | **99.4%** |
>
> And what no amount of building reaches is **90% mountain**, which is the
> intent. A summit is meant to be a summit.

### Stage 5b — Gates  *(implemented)*

A **Gate** is the built structure at one end of a Link: **3 cells wide, 1 deep,
12 slabs tall** — a square portal, since a slab is a quarter of a cell. Every
Domain gets one `Entry` and one to three `Exit`s, and **at most one per edge**:
Domains sit on a plane at their world-tree position, so two Gates facing the same
way would be two Links to the same place.

| kind | |
|---|---|
| `Land` | Stands on the ground; you walk through it. Needs three level cells to stand on, a clear outlook — nothing over its sill for ten cells — and level ground behind it to start a company. |
| `Hanging` | Floats four cells off the rim; you fly through it. Needs aether under all three of its cells, a flyable approach, and a **landing strip** of level ground running inland from the coast opposite. |

**The Entry's kind is an input, not a choice.** A Link joins two Gates, so the one
you arrive at has to match the one you left — hence `IslandParams.EntryGate`,
which the Domain that sends you sets and this Domain is generated around. `Auto`
is for a Home Domain, which has nothing to match.

Placement runs **after** `Traversal`, because every rule is about ground the
player can actually use: a Gate on a stranded ledge, or opening onto a cliff
face, is not a place to start a run. All of them require the heartland, dry
ground, and an apron of ≥ 60 level cells.

> **Three things had to give to keep the Entry guarantee.** A dead-level 8×3
> strip is near-impossible on ground tapering toward a coast — 24 of 60 islands
> could not host a hanging Entry at all — so a strip tolerates a slab, which is
> the free step anyway. The approach is tested against the **sill height** rather
> than against land as such, since a low spit under the flight path is scenery.
> And where a coast still will not take it, the Entry gives up its comfort in
> tiers — apron, then strip length — **never its kind**.

Audited over 60 islands, with `EntryGate` forced each way: exactly one Entry
every time, exits 1–3, **0** shared edges, **0** Entries of the wrong kind, **0**
Gates off the heartland or in water, **0** hanging Gates standing on land, **0**
without a strip. Apron a median of 211 cells.

*Still to come:* `CoastCells` (docks), `CliffCells` (essencercoral and hanging
growth attach here; a cheap version also gates Stage 4b), and `Overhangs`
(columns with more than one span — and where pathing must treat two walkable
levels in one cell).

### Stage 6 — Guarantees & re-roll  *(not yet)*

- At least one shelf must hold a contiguous flat region ≥ `MinSettlementArea`;
  else lower/merge a terrace or relax `MinShelfWidth` once, else bump the seed
  (bounded retries).
- If connectivity is required (§7) and the largest reachable set is < X% of land,
  re-roll.

---

## 4c. Water — lakes and rivers  *(implemented)*

A Domain floats in aether, which settles nothing about water *on* it: rain has to
land somewhere and run somewhere. The constraint that makes this interesting is
that there is no sea to run to — **every watercourse ends by pouring off the
rim into the aether.** That is a strong silhouette and worth building toward.

### Data

One extra plane on `IslandData`:

```csharp
public short[,] WaterLevel { get; }   // top slab of water, NoLand = dry
```

Water occupies `SurfaceLevel+1 … WaterLevel` in a column. It is a *level*, not a
volume, so it costs one short per column and needs no simulation. The mesher
emits it as a separate translucent surface, and only its top face plus the faces
against air need geometry.

### Lakes *(implemented)*

**A lake sinks into the interior of a flat patch — plain, mesa or basin — leaving
a two-cell ring of that patch's own ground dry around it. That ring is the
containment.** Which is what makes it work anywhere: no rim of higher ground is
needed, no distance from the coast, no particular landform. Water can never reach
anything outside the patch, because a flooded cell is at least two cells from the
patch border and so is surrounded by the patch's own shore.

> The first attempt filled *basins only*, using the surrounding escarpment as the
> rim. It produced 6 lakes across 60 islands: basins are a minority landform, and
> requiring one that also sits clear of the void left almost nothing. Hosting the
> lake inside any flat patch gives **138 lakes on 52 of 60 islands.**

- `level = shoreMin − 1`; the bed is cut two or three slabs below that, so the
  terrain drops three or four from the ring — never the ambiguous two.
- **The innermost dry ring is levelled to exactly `level + 1`.** Left at its
  natural height it stands one *or two* above the water, and a two-slab shore is
  the one step height the grammar exists to avoid — a beach you cannot walk onto.
  Median shore step is 1 slab, maximum 2 (at islet banks).
- Adjacent lake patches whose shores are **equal** share one level and get a
  channel notched between them, so they read as one body of water. A channel cell
  is carved only when every one of its neighbours belongs to the same pair, so
  linking two lakes can never open one to the outside. "Close enough" grouping was
  tried and rejected: a slab of disagreement becomes a two-slab shore.
- The flooded set is the **largest 4-connected component** of the patch's
  interior. A pinched patch otherwise leaves two pools meeting at a corner, and a
  corner is not a join you can swim through. Channel cutting can leave one too, so
  any water cell still joined only diagonally is raised to shore height afterwards
  — draining it to bed height instead would leave dry ground below the water
  beside it.
- **Mesas get a tarn, not a lake**, and only a tenth as often as a plain. Flooding
  a whole mesa interior drops the bed to near the surrounding plain and the
  landform stops reading as a tableland at all: it becomes a wall around a pit.
  Capped to a disc of radius 1.6–2.8, so ~10 cells.
- Roughly a third of lakes keep an **islet** — cells left uncarved deep inside,
  raised so they break the surface. Circular with a noise wobble: a Chebyshev
  radius makes literal squares.
- Lakes cut the surface *after* the step-grammar passes, so the ambiguous-step
  pass runs once more over what they left, taught to skip lake beds and never to
  lower a shore into its own water.

Audited over 60 islands: **67 lakes across 7,267 cells on 39 of the 60**, with
**0 cells of dry land below a water surface, 0 water touching the void, and 0
diagonal-only joins in water**. Shore steps are a median of 1 slab, but reach 4
at the worst — the levelling only covers cells directly touching the pool, so an
islet bank or a channel rim can still stand higher. Not yet fixed.

### Ramps — tried and removed

A causeway from a mesa's edge down to the plain, two cells wide, dropping one
slab per cell. It worked, and cost almost nothing: 14 ramps over 60 islands added
only 4 two-slab steps in 359,633 pairs. **It was removed because it reads as a
staircase, not a ramp.**

That is not a tuning failure, it is the grid. A mesa stands 5–6 slabs, so a
1-slab-per-cell grade covers the drop in 5–6 cells — five or six discrete risers
in a row, which is a staircase by any reading. For it to read as a slope the
grade would have to be roughly a slab every three cells, i.e. 15–18 cells of run,
which is longer than the open ground beside most mesas; and anything shallower
than one slab per cell cannot be expressed at all, since the slab *is* the
vertical quantum.

So a walkable approach to a mesa needs one of:

- **much longer, shallower ramps**, which means reserving space for them at patch
  assignment rather than carving them in afterwards;
- **sub-slab geometry** — a sloped mesh over the slab grid, a rendering change
  rather than a generation one;
- **built infrastructure** — stairs or a lift as a player-placed structure, which
  sidesteps the terrain question entirely and may be the better answer.

Leaving mesas unreachable on foot is a legitimate design position and is the
current state — and the numbers back it: a mesa top is reachable on foot 8% of
the time and **reachable with a stair 99.4%** of the time. A mesa is a
destination you pay to get onto, which is content rather than a defect.

### Passes — the ramp done as a landform  *(implemented)*

The ramp failed on its **shape**, not its grade. A narrow causeway sticking out
into a plain shows every riser in profile, and five risers in a row is a
staircase however you tune it. A *pass* takes the same grade and makes it a
landform: a broad radial sag, some fifteen to twenty cells across, centred on a
point of a cliff border, whose outline is a noise-wobbled radius so it is not a
disc. The ground either side of the path descends with it, so the eye reads a
col instead of a stair.

- The radius is `drop + 4`, so the grade stays under a slab per cell — the sag
  has to be longer than it is deep or it is a staircase again.
- Only **rung-ladder** cliffs qualify: both sides plain or hills, different rung
  groups, drop of 3 or more. A two-slab step is resolved to walkable by the
  grammar anyway, so a pass cut there only scars the ground. Mesas and basins are
  excluded on purpose: a mesa with a pass cut into it stops being a mesa.
- The slope limiter is told to reach **across** the border inside a pass. It is
  the one place a cliff border is deliberately bound.
- Like a canyon, a pass cuts a patch's rim, so a patch with one through it holds
  no lake.

**Occasional by design.** Passes are flavour, not the connectivity answer — that
is infrastructure. Cutting one on every border would flatten the island into a
single walkable district and throw away the plateau ladder. Audited: **24 passes
on 21 of 60 islands, all 24 joining their two patches into one walk area**, and
the steepest step inside a pass is 1 slab.

**Rendering.** Water is one flat quad per cell at the surface, *not* a box. Boxes
share interior faces with their neighbours and alpha blending draws every one of
them, so the doubled alpha paints a dark grid line along each cell edge. Coplanar
quads do not overlap. Culling is disabled so the surface is visible from beneath.

### Rivers  *(implemented)*

Rivers run **across** patches, not along their borders, and are therefore cut
*after* the patchwork rather than constraining it. (The alternative — cutting
them first so regions are drawn around them — was considered and rejected: a
river that only ever follows a boundary reads as a seam, and it would make the
partition answer to the hydrology instead of the other way round.)

**Routing is a priority flood inward from the rim, not a steepest-descent walk.**
Terrain built under a one-slab slope limit is mostly flats and shallow pits, so
descent stalls constantly and the flat-resolver becomes the whole algorithm.
Flooding inward gives every cell a downstream neighbour by construction. Each
cell is queued at `max(its own level, the level water had to clear to reach it)`;
carrying that maximum forward is what makes a depression fill and spill at its
lowest rim, and it is why a lake needs no special case — **water enters, crosses
and leaves**, so lake-to-lake links fall out for free. Ties break first-in
first-out, so a flat drains evenly outward instead of picking a corner.

> **Accumulation alone gives almost nothing, and structurally so.** Every rim
> cell is an outlet, so water leaves by the shortest way out, and slope-limited
> terrain has no valleys to gather it — the drainage fans out from each coast
> cell and no catchment ever grows large. Measured: a median of **13** river
> cells an island, and **11** navigable cells across 60 seeds.
>
> So the sources are **named** rather than emergent. Every summit and every lake
> outflow starts a watercourse and is traced to the rim whatever its catchment,
> which is what makes a river run the length of an island. Accumulation still
> decides how wide it gets on the way down, and traced courses meeting add up.

- **Carve** one cell wide, one slab deep, `WaterLevel` at the old surface.
- **A stream is fordable.** One slab is the same step that makes a hillside free,
  so `Traversal` treats a non-navigable river column as walkable ground: a
  watercourse running the length of an island should not cut it in two. Fording
  took islands that are one reachable whole from 48 to 54 of 60.
- **A navigable river is not.** Where flow passes the navigable threshold the
  channel widens to two cells — meant for barges and by the same token too wide
  to wade. Two cells is still inside the bridge span, so it divides the country
  without cutting it off. Three would not be, which is why it stops at two.
- **Termination.** A river reaching the rim becomes a fall. It never reaches a
  sea, because there isn't one.

Audited over 60 islands at 128²:

| | |
|---|---|
| rivers | 8,618 cells, on **60 of 60** islands, median 99 an island |
| navigable | 1,048 cells |
| falls | 1,004, of which **852 pour off the rim** |
| rivers reaching the rim | **60 of 60 islands** — there is no sea; they must |
| channel not cut below its own water | 0 |
| water running uphill | 14 in 8,618 |

> The audit measures the step grammar at the height you actually **cross** a
> column at — a ford's water surface, not its bed. Measuring the bed reported a
> two-slab step at every bank standing a slab proud: 1,373 false positives.

### Waterfalls, and the thing worth building for

Where a route crosses a cliff — and cliffs fall only where the rules put them, so
their locations are known — the river becomes a fall. At the coastline every
river becomes one, pouring off the edge into aether. A Domain seen from below
should have water spilling from its rim; that single image probably does more for
"these are floating islands" than anything else in the renderer.

Open questions: where the water *comes from* in fiction (condensation out of
aether? an Essence cycle?); whether lakes should be fresh-water sites that
settlements need, which would make them a placement constraint rather than
decoration; and whether rivers should be cut before landform assignment so
patches can be drawn around them instead of over them.

---

## 4d. Auditing the guarantees

Every "measured" figure above comes from **`scenes/dev/generation_audit.tscn`**,
which runs the real generator over 60 seeds and measures `IslandData` directly:

```
godot --path . --headless --quit-after 2 scenes/dev/generation_audit.tscn
```

It needs no rendering, takes about 1.5 s, and prints the step grammar, cliff
borders by landform pair, patch sizes, mesa/basin clearances, mountain rise and
step profile, lake containment, which landforms each character actually
delivered, walkability, shelves, and landmass continuity.

> **Read the numbers, don't assume them.** These figures were originally produced
> by a stand-alone harness that re-implemented the pipeline against substitute
> noise, because `FastNoiseLite` needs the engine. That validated the
> *architecture* but not the shipped output, and when the real generator was
> finally measured several claims turned out optimistic — lakes were half as
> common, mesa clearance ran to 22 slabs rather than 6, basins had effectively
> vanished, and the cliff rule leaked in 17 places. The audit scene measures
> `IslandData` and so re-implements nothing; there is nothing left to drift.

### Closed, as of this audit

| was | now |
|---|---|
| 14 hills cliffs the rules forbid | **0** — the slope limit runs across bound borders |
| mesa clearance compounding to 22 slabs | **max 7** — ground and neighbouring mesas measured separately, capped at 2× |
| 1 basin in 60 islands | **46**, on every `Downs` and `Tableland` island |
| lake shores reaching 4 slabs | **max 1** — levelling runs last, over all patches |
| 3 diagonal-only land joins | **0** — the corner is filled before the component filter |

Two bugs surfaced while fixing those, and are fixed too: a canyon cutting a
patch's rim set that patch's lake level to the bottom of the trench (a canyon is
a drain — a cut patch now holds no water), and a canyon cut alongside a basin rim
dropped the plain *below* the basin floor, inverting the escarpment.

### Open gaps

| | |
|---|---|
| `Twins` fusing | 1.9 landmasses an island, not 2.0 — a fifth of seeds still merge the pair despite the re-roll |
| water running uphill | 14 cells in 8,618, at confluences and lake margins |
| basins on a `Highland` | 94% of islands, not 100% — adjacency cannot always place one beside a massif |
| shelf descent | a shelf is one *exact* level; "mostly flat with an occasional single-slab step" (§1.4) is not modelled |
| Stage 4b | overhangs and arches |
| Stage 6 | the re-roll guarantees; nothing yet re-rolls a bad island |

**Reachability is answered.** 93% of land is on the heartland, a median island
reaches 94% of itself, 57 of 60 are a single whole, and 90% of what stays out is
mountain. Cliffs are costs, not walls.

---

## 5. Rendering handoff (the mesher is its own task)

`IslandData` feeds the terrain renderer; it does **not** spawn per-slab nodes.

- **Face selection**, per column, per span `s`:
  - top face at `s.Top + 1`; bottom face at `s.Bottom` (the lowest span has air
    below; a higher span has the inter-span gap below it — that gap's ceiling is
    the overhang underside).
  - side faces toward each 4-neighbour: walk this column's spans and the
    neighbour's spans together, emit a face for every slab sub-range of
    `s.Bottom..s.Top` the neighbour does **not** cover (whole range if the
    neighbour has no land).
- **Greedy merge** matters more than in a cube model: columns are ~4× taller in
  slab count, so merge vertical runs of identical exposed faces before
  triangulating, then merge coplanar quads across cells.
- **Chunking:** 16×16 or 32×32 cell chunks; one `ArrayMesh` + one collider each.
- **Collision:** custom trimesh per chunk (a `HeightMapShape3D` models a single
  surface only — no rim, underside or overhangs). Prototype: box colliders per
  merged run.
- **Current first cut** (`IslandLab.cs`): one `MultiMeshInstance3D`, one
  **scaled** unit-box instance per span (`Basis.Identity.Scaled(1, spanHeight, 1)`
  at the span's world-Y centre), height-tinted. No mesher, no culling; solid at a
  glance and cheap enough for the 96² lab. Overhangs render for free once Stage 4b
  produces multi-span columns.

---

## 6. File layout

```
scripts/generation/            namespace ProjectNikitin.Generation
  Terrain.cs                    CellSize (1.0) / SlabHeight (0.25) constants
  Span.cs                       readonly record struct Span(short Bottom, short Top)
  IslandParams.cs               [GlobalClass] resource — the §3 knobs
  IslandData.cs                 §2 output + metadata types
  Noise.cs                      FastNoiseLite wrapper (fBm / ridged / domain-warp), [0,1]
  FieldOps.cs                   smoothstep, quantile-threshold, blur
  IslandGenerator.cs           Generate(seed, params) — stages 1-4
  IslandArrangement.cs          Single / Satellites / Twins / Triplets / Archipelago / Atoll
  Traversal.cs                  Stage 5 analysis: walk areas, reach areas, shelves
  Rivers.cs                     drainage routing, channels, falls
  Gate.cs                       Gate, GateKind, GateRole, Cardinal
  GatePlacement.cs              Stage 5b: where the Links come out
scripts/terrain/               (later) the chunked span-aware mesher + colliders
scripts/dev/IslandLab.cs       runtime harness for the scene below
scripts/dev/GenerationAudit.cs headless guarantee audit (§4d)
resources/island_default.tres  the IslandParams preset both dev scenes load
scenes/dev/island_lab.tscn     MultiMesh terrain + camera rig
scenes/dev/generation_audit.tscn
```

---

## 7. Open questions

- **Domain size.** This spec uses a **128 × 128** footprint (position: vasin;
  Maxim favours smaller; Notion's "Ecumene" still says 16³–64³). Vertical extent:
  the bounding cube is 512 slabs, but generation only uses a band around Y = 0 —
  how tall a band is a tuning question, not a hard limit.
- *(answered)* **Connectivity.** Measured both ways: 55% of land is walkable
  from the mainland, **93% is reachable once stairs and bridges are built**, and
  90% of the remainder is mountain. The position taken is that a cliff is a cost,
  not a wall — infrastructure is the answer, passes are occasional flavour, and a
  mesa top is a destination you pay to reach. What is *not* yet done is Stage 6:
  nothing re-rolls an island that falls short.
- **Where water comes from**, in fiction. Condensation out of aether? An Essence
  cycle? Rivers exist mechanically and pour off the rim; nothing says why.
- **Are lakes and rivers a settlement constraint?** Fresh water as a placement
  requirement would make them load-bearing rather than decorative.
- **What a Gate costs to use, and what an Exit needs.** Placement guarantees an
  apron and a landing strip; nothing yet says what a company must build there.
- **Terrace elevations.** Noise-driven vs. evenly spaced vs. authored per biome.
- **Free-step size.** Assumed **one slab** (0.25 u) is free movement, two+ is an
  obstacle. Confirm; it sets where cliffs vs. walkable slopes fall.
- **Link edges.** Do all four edges need a reserved coastal `GateAnchor`, or only
  edges that actually get a Gate?
- **Half-slabs.** Out for now. If added, `Span` bounds become fixed-point or grow
  a half-step flag, and Stage 3 gets finer.
- **Overhang pathing.** A two-span column has two walkable levels; reachability
  and later nav must treat spans, not columns, as nodes. Not yet designed.
- **Arch plausibility.** Stage 4b bridges by rule; nothing checks the result
  looks load-bearing. May need a min-support / aesthetic pass.

---

## 8. Build order (this branch)

1. ✅ `Terrain`, `Span`, `IslandParams` (`[GlobalClass]`), `IslandData`;
   `IslandGenerator.Generate`.
2. ✅ Stages 1–4 in slab units: mask → height → terrace snap → keel (with
   distance-to-coast rim) → one `Span` per land column.
3. ✅ `scenes/dev/island_lab.tscn` + `IslandLab.cs`: one scaled `MultiMesh` box
   per span (keel → surface), height-tinted; auto-rebuilds when `Seed`/`Params`
   change, or on the **R** key; `LookAt` camera rig for fly-around.
4. ✅ Landform patches, lakes, the step grammar closed (§4d), and Stage 5
   walkability + shelves, with the lab's `walk` and `shelves` views to see them.
5. ✅ Reachability measured and answered (§7); passes; arrangements with a bridge
   guarantee; rivers, falls and fording; Gates. Footprint moved to 128².

Then: the **chunked span-aware mesher + colliders** — the biggest single piece
left, and the only thing that will answer the performance question for real;
Stage 4b overhangs; the remaining feature anchors (`CoastCells`, `CliffCells`,
`Overhangs`); the §6 re-roll guarantees; settlement placement hooks.
