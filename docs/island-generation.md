# Island Generation — technical spec

Status: draft, 2026-08-29. Terrain unit is a **slab** (1×1 footprint, 0.25 tall —
a 1:4 ratio). Build order §8 steps 1–4 implemented in slab units
(`scripts/generation/`, `scenes/dev/island_lab.tscn`); the Stage 3 morphological
open, Stage 4b overhangs, feature anchors and guarantees are still pending.
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

    // metadata (see §5)
    public List<FlatRegion> Shelves;
    public List<Vector2I>   CoastCells;
    public List<Vector2I>   CliffCells;
    public List<Vector2I>   Overhangs;   // columns with more than one span
    public GateAnchor       GateAnchor;
    public bool[,]          Reachable;

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
| `Fragmentation` | 0 – 1 | single blob ↔ many separated islets |
| `Character` | `TerrainCharacter` | which landforms the island is built from; `Auto` per seed |
| `Relief` | 0 – 1 | vertical exaggeration of every landform's relief |
| `Roughness` | 0 – 1 | noise gain / jaggedness of the surface |
| `RegionScale` | 6 – 40 cells | typical width of one landform region |
| `MinRegionArea` | 12 – 400 cells | smallest patch allowed; smaller ones are merged away |
| `CliffHeight` | 3 – 16 slabs | spacing of the plateau ladder; every region border cliff |
| `PlateauLevels` | 1 – 8 | rungs on that ladder above the coastal level |
| `MountainHeight` | 8 – 160 slabs | rise from a mountain's foot to its summit |
| `MesaHeight` | 3 – 24 slabs | how far a mesa top clears the highest ground around it |
| `BasinDepth` | 3 – 24 slabs | how far a basin floor sits below the ground around it |
| `MinSettlementArea` | e.g. 8×8 | guarantee the generator must meet or re-roll *(not wired yet)* |
| `EdgeThickness` | 1 – 32 slabs | column depth at the coastline (the thin lip) |
| `KeelDepth` | 4 – 256 slabs | extra depth under the innermost interior |
| `KeelTaper` | 0.3 – 3 | keel profile exponent; 1 = straight cone |
| `KeelRoughness` | 0 – 1 | craggedness of the underside, and how far its contours wander |
| `OverhangDensity` | 0 – 1 | how often tall cliff faces get undercut or arched *(Stage 4b)* |
| `OverhangDepth` | 1 – 8 cells | how far an undercut reaches back from the face *(Stage 4b)* |
| `ArchSpan` | 2 – 10 cells | widest gap a natural bridge will span *(Stage 4b)* |

`IslandParams` are themselves expected to be produced per Domain from its biome /
archetype (arid mesa vs. lush lowland …). That mapping is **Domain generation**,
a separate concern — this spec assumes `IslandParams` arrive ready.

---

## 4. Pipeline

### Stage 1 — Footprint mask → `bool Land[x,z]`  *(implemented)*

1. **Irregular silhouette** (scaled by `Irregularity`), which is what stops the
   result reading as a disc:
   - an **ellipse** at a seed-chosen aspect and rotation, so the island has a
     long axis;
   - a **lobed radius** — `rEff = radius * (1 ± 0.55 * lobeNoise(θ))` — giving
     bays, capes and a wandering coast at the largest scale. The lobe noise is
     sampled at `(cos θ, sin θ)` rather than at `θ`, so it is seamless where the
     angle wraps;
   - one or more **bites**, applied *after* the partition — see below.
2. **Radial falloff** against that effective radius: `d = dist / rEff`;
   `fall = 1 - smoothstep(0.40, 1, d)`. Keeps land off the bounding-box walls.
3. **Domain-warped fBm** `n(x,z) ∈ [0,1]` — warp amplitude rises with
   `Irregularity`, so coastlines meander at the small scale too.
4. **Fragmentation:** `frag = lerp(1, ridgedBlobNoise, Fragmentation)`; multiplied
   in, so higher `Fragmentation` breaks the mass into separated high spots.
5. `field = fall * (0.35 + 0.65*n) * frag`. `threshold` = the `(1 - Coverage)`
   quantile of `field` (one sort — no iterative search), taken **over the disc
   `d < 1` only**. Sampling the whole grid pads the population with guaranteed
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
7. **Continuity.** Below `Fragmentation` 0.25 the island is reduced to its single
   largest **4-connected** component. Keeping every piece above a size threshold
   is not enough: two comparable survivors can meet only at a corner, and a corner
   is not a join you can walk. Above that threshold an archipelago is intended, so
   pieces down to 20% of the largest are kept. Audited over 60 islands: exactly
   **60 landmasses, one each**, 0 diagonal-only joins in water — and **3** in
   land, a small residue the component filter does not catch because both sides
   belong to the same component. Not yet fixed.
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

**Cordillera.** Weights alone rarely put two mountains side by side, so every
peak comes out solitary. On a share of islands — 85% of `Highland`, 15% of
`Plains`, plus 35% whenever the style is `Ridge` — the highest 12–25% of regions
by envelope are *forced* to `Mountain`, and the massif merge welds them into one.
Under `Ridge` that band is a spine, so the result is a chain crossing the isle;
under a dome it is a central massif. Measured: the largest massif spans a mean
0.58 of the island's own extent under `Ridge`, against 0.38 before the pass.

**Adjacency.** A mesa or basin may only touch plains (or each other). Where one
abuts a mountain it gives way — a massif is the larger feature — and any other
neighbour is flattened to plain, which is what puts the apron of open ground
around a mesa that makes it read as one. Hills, plains and mountains may touch
freely.

**Basins** are the mesa rule with the sign flipped: assigned highest-envelope
first (so a run of them steps *down* one after another), sunk to
`min(neighbour rung) − BasinDepth`, flat floor, inward-facing cliff all round.
They favour low ground where mesas favour middling-high. They are also the
obvious home for standing water once §4c exists.

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

`MountainHeight` is literal: measured rise above the foot is a median 41 slabs
for a setting of 40, and no border cell now sits below the ground it meets
(0 of 2818, against 14.6% when mountains hung off a rung).

**Mesas** are placed after every other rung is fixed, at
`max(neighbour surface) + MesaHeight` — measured against neighbours' plateau
*plus* their relief amplitude, so a mesa clears the tops of anything beside it,
not just its base. `MesaHeight` is likewise literal: a mesa is a step up, not a
peak. Audited over 60 islands: 37 mesas, **none level with or below a neighbour,
none touching a mountain or any other landform kind.** Clearance is a median of 7
slabs — but the maximum is **22**, because a mesa placed against an already-raised
mesa clears *that* one, and a chain of them compounds five slabs at a time. A
stepped tableland is the intent; a tower is not. **Not yet fixed.**

> **Basins are all but extinct.** The audit found **one** across 60 islands: the
> character weights (0.20 / 0.10 / 0.07) are multiplied by both an envelope term
> and a coastal smoothstep, and the product almost never wins the draw. The
> landform works — the one basin measured a clean 5-slab drop — but it effectively
> never appears, which also caps how many lakes are anything other than a flooded
> plain.

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

   Audited over 60 islands, that holds for **99.6%** of cliffs — but not all:

   | border | cliffs | |
   |---|---|---|
   | plain-plain | 1773 | by the rule |
   | plain-mesa | 1516 | the mesa's own escarpment |
   | mesa-mesa | 440 | by the rule |
   | plain-basin | 90 | the basin's own escarpment |
   | plain-hills | **11** | *leak* |
   | hills-hills | **3** | *leak* |
   | mountain borders | **3** | massifs take no rung, so a steep flank is allowed |

   The 14 hills leaks are not by design. Sharing a rung equalises a border's
   *base*, but hills carry more relief than a plain; the blurred amplitude field
   narrows that gap without closing it, so a few hills borders still reach three
   slabs. **Not yet fixed.**
2. **Slope limit**, applied *within* each region only: a Lipschitz projection
   from above (repeatedly lower any cell standing more than the region's limit
   above a neighbour). It only lowers, so it converges. Excluding region borders
   is what leaves the plateau gaps standing as cliffs.
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

Audited over 60 islands (224k adjacent pairs, real generator — see
`scenes/dev/generation_audit.tscn`): **91.8% of steps are free (0–1 slabs), 7.8%
are cliffs (3+), and two-slab steps outside mountains number 0 in 205,788.**

*Not yet:* minimum-shelf-width enforcement, gentle descent within a plateau.

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

### Stage 5 — Feature anchors (metadata, terrain untouched)  *(not yet)*

- `CoastCells` — land cells adjacent to aether. Docks, airstrip.
- `CliffCells` — columns exposed on ≥ 1 side by ≥ K slabs. Essencercoral /
  hanging growth attach here. (A cheap version also gates Stage 4b.)
- `Overhangs` — columns that ended up with more than one span. Attach points for
  hanging growth; also flag where pathing must treat two walkable levels in one
  cell.
- `Shelves` — `FlatRegion { level, cells, bbox, area }` for every surviving flat
  patch on a top span; the settlement layer consumes this.
- `GateAnchor` — a reserved open point a few cells off the coast on a Link edge,
  clear line to the edge, for the offshore aether opening from "The First Hour".
- `Reachable` — flood fill from the largest shelf under the traversal rule (a
  one-slab step up/down is free; a 2+-slab face is a wall). Flags stranded land.
  Because a one-slab slope is free, most of a noise island is one reachable set;
  cliffs and terrace faces are the only barriers.

### Stage 6 — Guarantees & re-roll  *(not yet)*

- At least one shelf must hold a contiguous flat region ≥ `MinSettlementArea`;
  else lower/merge a terrace or relax `MinShelfWidth` once, else bump the seed
  (bounded retries).
- If connectivity is required (§7) and the largest reachable set is < X% of land,
  re-roll.

---

## 4c. Water — lakes *(implemented)*, rivers *(design)*

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
current state.

**Rendering.** Water is one flat quad per cell at the surface, *not* a box. Boxes
share interior faces with their neighbours and alpha blending draws every one of
them, so the doubled alpha paints a dark grid line along each cell edge. Coplanar
quads do not overlap. Culling is disabled so the surface is visible from beneath.

### Rivers *(design)*

Rivers run **across** patches, not along their borders, and are therefore cut
*after* the patchwork rather than constraining it. (The alternative — cutting
them first so regions are drawn around them — was considered and rejected: a
river that only ever follows a boundary reads as a seam, and it would make the
partition answer to the hydrology instead of the other way round.)

- **Source** on high ground — a mountain's summit band, or a lake's outflow.
- **Route** by steepest descent on the surface, with flats resolved by a BFS
  toward the nearest lower cell. Terrain built under a slope limit is full of
  flats, so the flat-resolver is the real work here, not the descent.
- **Carve** a channel one cell wide, one slab deep, with `WaterLevel` at the old
  surface. Because a one-slab step is free, a river is crossable everywhere —
  which is the right default. A *wide* river (3+ cells, deeper) becomes a genuine
  barrier and should be the exception, chosen deliberately, since it implies
  bridges.
- **Termination.** A river reaching the rim becomes a fall. It never reaches a
  sea, because there isn't one.

### Waterfalls, and the thing worth building for

Where a route crosses a cliff — and cliffs are now restricted to plain-plain and
mesa-mesa borders, so their locations are known — the river becomes a fall. At
the coastline every river becomes one, pouring off the edge into aether. A
Domain seen from below should have water spilling from its rim; that single
image probably does more for "these are floating islands" than anything else in
the renderer.

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

It needs no rendering, takes about 1.4 s, and prints the step grammar, cliff
borders by landform pair, patch sizes, mesa/basin clearances, mountain rise and
step profile, lake containment, and landmass continuity.

> **Read the numbers, don't assume them.** These figures were originally produced
> by a stand-alone harness that re-implemented the pipeline against substitute
> noise, because `FastNoiseLite` needs the engine. That validated the
> *architecture* but not the shipped output, and when the real generator was
> finally measured several claims turned out optimistic — lakes were half as
> common, mesa clearance ran to 22 slabs rather than 6, basins had effectively
> vanished, and the cliff rule leaked in 17 places. The audit scene measures
> `IslandData` and so re-implements nothing; there is nothing left to drift.

### Known gaps, as of the last audit

| | |
|---|---|
| cliff rule leaks | 14 hills borders reach 3+ slabs |
| mesa clearance | compounds to 22 slabs in a chain of stepped mesas |
| basins | 1 in 60 islands — the weights are multiplied down to nothing |
| lake shore | median 1 slab, but reaches 4 at islet banks and channel rims |
| diagonal joins | 3 in land across 60 islands |

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
  FieldOps.cs                   smoothstep, quantile-threshold, (later) morphology
  IslandGenerator.cs           Generate(seed, params) — stages 1–4 now
scripts/terrain/               (later) the chunked span-aware mesher + colliders
scripts/dev/IslandLab.cs       runtime harness for the scene below
scenes/dev/island_lab.tscn     params resource + MultiMesh terrain + camera rig
```

---

## 7. Open questions

- **Domain size.** This spec uses a **128 × 128** footprint (position: vasin;
  Maxim favours smaller; Notion's "Ecumene" still says 16³–64³). Vertical extent:
  the bounding cube is 512 slabs, but generation only uses a band around Y = 0 —
  how tall a band is a tuning question, not a hard limit.
- **Connectivity.** Hard guarantee that all land is reachable, or just "the
  largest region is playable and the rest is scenery"?
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
4. ⬜ Tune params until size/density, relief and the beginnings of shelves read
   right.

Then: finish Stage 3 (morphological open, gentle descent), Stage 1 cleanup,
Stage 4b overhangs, the chunked span-aware mesher + colliders, feature anchors,
the §6 guarantees, settlement placement hooks.
