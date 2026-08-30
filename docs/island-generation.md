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
| `Fragmentation` | 0 – 1 | single blob ↔ many separated islets |
| `Relief` | 0 – 1 | height amplitude: plains ↔ mountains |
| `Roughness` | 0 – 1 | noise gain / jaggedness of the surface |
| `HeightScale` | 4 – 512 slabs | peak surface height at `Relief` = 1 |
| `TerraceCount` | 0 – 6 | number of habitable shelf levels |
| `TerraceGrip` | 0 – 1 | how strongly the surface snaps to shelves vs. free slope |
| `MinShelfWidth` | 3 – 6 cells | erosion radius that kills sub-width flats *(not wired yet)* |
| `MinSettlementArea` | e.g. 8×8 | guarantee the generator must meet or re-roll *(not wired yet)* |
| `RimDepth` | 2 – 128 slabs | column depth at the coastline |
| `RimFalloff` | 0 – 1 | how far the thick rim reaches inland |
| `OverhangDensity` | 0 – 1 | how often tall cliff faces get undercut or arched *(Stage 4b)* |
| `OverhangDepth` | 1 – 8 cells | how far an undercut reaches back from the face *(Stage 4b)* |
| `ArchSpan` | 2 – 10 cells | widest gap a natural bridge will span *(Stage 4b)* |

`IslandParams` are themselves expected to be produced per Domain from its biome /
archetype (arid mesa vs. lush lowland …). That mapping is **Domain generation**,
a separate concern — this spec assumes `IslandParams` arrive ready.

---

## 4. Pipeline

### Stage 1 — Footprint mask → `bool Land[x,z]`  *(implemented)*

1. **Radial falloff** from the footprint centre: `d = dist / radius`;
   `fall = 1 - smoothstep(0.45, 1, d)`. Keeps land off the bounding-box walls.
2. **Domain-warped fBm** `n(x,z) ∈ [0,1]` — fBm sampled through a domain-warp so
   coastlines meander instead of looking circular.
3. **Fragmentation:** `frag = lerp(1, ridgedBlobNoise, Fragmentation)`; multiplied
   in, so higher `Fragmentation` breaks the mass into separated high spots.
4. `field = fall * (0.35 + 0.65*n) * frag`. `threshold` = the `(1 - Coverage)`
   quantile of `field` (one sort — no iterative search).
5. `Land = insideBox && field > threshold`.
6. *Not yet:* connected-component cleanup, hole fill, largest-component-only.

### Stage 2 — Raw height → `float H[x,z]` in slabs, land only  *(implemented)*

1. fBm (`gain ∝ Roughness`) plus a ridged term whose weight rises with `Relief`
   (mountain spines).
2. Multiply by an **interior bias** `1 - d²` so relief rises inland, coast stays
   low.
3. Scale to `peak = 1 + HeightScale * Relief` slabs.

### Stage 3 — Terracing → `short SurfaceLevel[x,z]` (slab indices)  *(snap only)*

Plain `round(H)` gives fine steps but no *wide* flats — requirement 4. Plan:

1. Choose `TerraceCount` shelf elevations across the height range, irregularly
   spaced (from a 1D noise).
2. Per column: if `|H - nearestShelf| ≤ band` (`band ∝ TerraceGrip`), snap to the
   shelf; else `round(H)` — a free slope in 1-slab (0.25 u) risers, which is
   already walkable, so terracing is about deliberate *wide flats*, not about
   making terrain traversable.
3. *Not yet:* morphological **open** of each shelf's cell set at
   `MinShelfWidth/2` (erode then dilate) to delete sub-width flats and
   re-flatten survivors.
4. *Not yet:* gentle descent — let a shelf's target drift with a very-low-freq
   noise (≤ ~1 slab per 6–8 cells), still snapped.

### Stage 4 — Keel / underside → one span per column  *(implemented)*

1. `toCoast[x,z]` = BFS distance in cells from each land cell to the nearest
   non-land cell.
2. `coast = clamp(1 - toCoast / reach, 0, 1)` with `reach = 2 + RimFalloff*20`
   cells — 1 at the shoreline, tapering inland.
3. `thick = 2 + RimDepth * coast + noise*4` slabs; underside noise keeps the
   "floating rock" look.
4. `keel = SurfaceLevel - round(thick)`; emit `Spans[x,z] = [Span(keel, surface)]`.
   Stage 4b is the only stage that produces more than one span.

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
