# Island Generation — technical spec

Status: draft, 2026-08-29. Living document. Owns the implementation detail behind
the Notion page *Mechanics and Concepts → Generation → Island Generation* (which
stays a short requirements list). Design vocabulary and the world model are in
`CLAUDE.md` and the Notion wiki.

---

## 1. Requirements

From Notion, with the intent spelled out:

1. **Varied size and density** — from one large "continent" filling the Domain to
   a scattered "archipelago" of small islets, and everything between.
2. **Varied surface relief** — from flat plains to mountains. Blocks are cubes
   (no half-blocks yet — deferred), so "mountains" read as stepped/mesa-like, not
   smooth. Accepted for now; one block is a *very small* part of an island, so
   the stepping is fine-grained relative to the whole.
3. **Variable edge thickness** — the island has a finite depth; how many blocks
   deep it is at the rim (the cliff you see from the side, or from a Domain
   below) is a tunable, and varies across one island.
4. **Multiple habitable shelves** — an island can have several roughly-flat
   levels, like a mountain with terraces. A shelf is only *habitable* if it is
   **at least 3–4 cells wide** in a mostly-flat patch; a 1-cell ledge is not
   something a settlement can prosper on (a few polities might tolerate such
   terrain, but that is a polity-specific exception, not the default target).
   Shelves may **gradually descend** — mostly flat with the occasional
   single-block step — rather than being perfectly level.

**Overhangs and arches are wanted** — undercut cliffs, rock shelves, natural
bridges. The data model (§2) represents them directly and Stage 4b generates
them. Non-requirements for v1: branching cave *networks* and horizontal tunnels
(would need voxels); half-blocks; multiple stone/soil strata; more than one
terrain material tier.

---

## 2. Data model — per-column span list

A Domain footprint is **128 × 128 cells** (see §7 on the size disagreement). A
dense 3D block array (128³ ≈ 2.1M cells) is a non-starter and per-block nodes are
impossible. Terrain is stored **per column** as a short list of vertical solid
runs:

- `Spans[x,z]` — an array of `Span`, where `Span { short Bottom; short Top; }` is
  one **contiguous** run of solid blocks, `Bottom..Top` inclusive.
- Spans in a column are sorted bottom-up, never overlapping, never touching
  (adjacent runs merge into one). An empty array means no land in that column.
- **The air gap between two spans is what makes an overhang** — undercut cliff,
  rock shelf, or (across several columns) a natural bridge / arch. Most columns
  have exactly one span; only cliff and arch areas have two or three.
- `Material[x,z]` — `byte`, surface material of the top span. Single tier for now
  (grass over dirt).

Derived, not stored: `SurfaceLevel(x,z)` = `Top` of the highest span (`NoLand` if
none, the walkable surface); `KeelLevel(x,z)` = `Bottom` of the lowest span;
local rim thickness = `Top - Bottom + 1` of the relevant span.

**Storage:** a jagged 2D array (`Span[Size,Size][]`). At a realistic ~1.3 spans
per column average that is ~128·128·1.3·4 B ≈ **90 KB per island** — still three
orders of magnitude under a dense voxel grid, whole island resident, no
streaming. If per-cell array churn becomes a problem, repack as CSR: one
`Span[]` blob plus `int[,] start` / `byte[,] count`.

**Still outside the model:** branching cave *networks* and horizontal tunnels
within a single column footprint — those need voxels or another structure. A
single vertical gap per column (overhang / shelf / arch underside) is fully
representable; that covers the wanted decorations.

Coordinates: `X, Z ∈ [0, 128)`. `Y` is signed, band `[-64, 64)`, `Y = 0` the
nominal float level the island sits around (it can build up into mountains and
down into a deep keel). The Ecumene's "invisible bounding cube" is this 128³
volume.

Output type (shape, names provisional):

```csharp
public readonly record struct Span(short Bottom, short Top);

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
identical output, no time/random-device dependence, so it is safe for a future
multiplayer or replay use even though that is not a current requirement.

| param | rough range | drives |
|---|---|---|
| `Seed` | int | all noise offsets |
| `Radius` | 20 – 120 cells | overall extent of the land mask |
| `Coverage` | 0 – 1 | fraction of the bounding disc that ends up as land |
| `Fragmentation` | 0 – 1 | single blob ↔ many separated islets |
| `Relief` | 0 – 1 | height amplitude: plains ↔ mountains |
| `Roughness` | 0 – 1 | octave weighting / jaggedness of the surface |
| `RimDepth` | 3 – 24 blocks | typical column depth at the coastline |
| `RimFalloff` | 0 – 1 | how far the thick rim reaches inland before thinning |
| `TerraceCount` | 0 – 6 | number of habitable shelf levels |
| `TerraceGrip` | 0 – 1 | how strongly surface snaps to shelf levels vs. free slope |
| `MinShelfWidth` | 3 – 6 cells | erosion radius that kills sub-width flats |
| `MinSettlementArea` | e.g. 8×8 | a guarantee the generator must satisfy or re-roll |
| `OverhangDensity` | 0 – 1 | how often tall cliff faces get undercut or arched |
| `OverhangDepth` | 1 – 8 cells | how far an undercut reaches back from the face |
| `ArchSpan` | 2 – 10 cells | widest gap a natural bridge will span |

`IslandParams` are themselves expected to be produced per Domain from its biome /
archetype (arid mesa vs. lush lowland …). That mapping is **Domain generation**,
a separate concern — this spec assumes `IslandParams` arrive ready.

---

## 4. Pipeline

### Stage 1 — Footprint mask → `bool Land[x,z]`

1. **Radial falloff** from the footprint centre: `d = dist(cell, centre) / Radius`;
   `fall = 1 - smoothstep(0.55, 1.0, d)`. Keeps islands off the bounding-box walls.
2. **Domain-warped fBm** `n(x,z) ∈ [0,1]`: sample fBm at `(x,z)` offset by a
   second low-frequency noise (`warp`) so coastlines meander instead of looking
   circular.
3. **Fragmentation:** blend the single-lobe falloff toward a higher-frequency
   ridged field as `Fragmentation → 1`, and subtract a "channel" noise that
   carves water gaps. Low `Fragmentation` = one contiguous mass; high = many
   disconnected high spots.
4. `field = fall * (0.35 + 0.65*n) * fragMix`. Choose `threshold` from `Coverage`
   (binary-search the threshold to hit the target land fraction; cheaper: a
   pre-calibrated `Coverage → threshold` curve).
5. `Land = field > threshold`.
6. **Cleanup:** drop connected components below a min area (unless
   `Fragmentation` is high), fill 1-cell holes, optionally keep only the largest
   component if connectivity is a hard guarantee (§7).

### Stage 2 — Raw height → `float H[x,z]` (land only)

1. fBm, amplitude `∝ Relief`, octave falloff `∝ Roughness`; add a ridged-noise
   term at high `Relief` for mountain spines.
2. Multiply by an **interior bias** `centerBias(d)` so relief rises inland and
   the coast stays low (`H *= 1 - d^2` or similar).
3. Scale into the usable Y band with headroom for the rim below.

### Stage 3 — Terracing → working grid `short SurfaceLevel[x,z]` with real shelves

Plain `round(H)` gives blocky noise but no *wide* flats, which fails requirement
4. Instead:

1. Choose `TerraceCount` shelf elevations across the height range. Irregular
   spacing (from a 1D noise) looks better than even spacing.
2. Per column: if `H` is within a band of a shelf elevation (band width `∝
   TerraceGrip`), snap to that shelf; otherwise place it on a **stepped slope**
   between shelves, quantised to 1-block risers.
3. **Enforce minimum width:** for each shelf, take its set of snapped cells and
   morphological-**open** it (erode then dilate, radius = `MinShelfWidth/2`).
   Flats narrower than `MinShelfWidth` vanish; survivors are re-flattened to
   exactly the shelf level. Cells that lost their shelf fall back to the slope.
4. **Gentle descent:** let a shelf's target level drift with a very-low-frequency
   noise (≤ ~1 block per 6–8 cells), still snapped, so a shelf can be "mostly
   flat, slowly stepping down" rather than dead level.
5. Result: plateaus ≥ `MinShelfWidth` wide, joined by short stepped faces — the
   buildable terrain.

### Stage 4 — Keel / underside, then build one span per column

1. `thick(x,z) = RimDepth * rimProfile(d) + interiorFloor + fbmNoise`
   where `rimProfile` is largest on coastline cells (a land cell 4-adjacent to
   non-land) and tapers inland over a distance set by `RimFalloff`.
2. `keel = SurfaceLevel - max(1, round(thick))`, clamped to the Y floor.
3. Give the underside its own coarser, craggier noise — it is the visible
   "floating rock" from below and from neighbouring Domains.
4. Emit `Spans[x,z] = [ Span(keel, SurfaceLevel) ]` for every land column — a
   single run. Stage 4b is the only place that produces more than one.

### Stage 4b — Overhangs & arches (splits / adds spans)

Runs only near `CliffCells` (columns with a tall exposed face) and short gaps
between landmasses. Two mechanisms, both bounded so the island stays legible:

1. **Undercuts.** For a cliff column, sample a low-frequency 3D noise
   `carve(x,y,z)` over its span; where it crosses a threshold in a horizontal
   band *and* the band is within `OverhangDepth` of the exposed face, delete that
   band, splitting the span in two — the upper part is now an overhang. Keep
   ≥ `MinLedgeThickness` blocks of solid above and below each cut; at most 1–2
   cuts per column; frequency scaled by `OverhangDensity`.
2. **Arches / natural bridges.** Find a gap ≤ `ArchSpan` cells wide between two
   land columns whose top spans are within a few blocks of each other. Add a
   bridging span, a few blocks thick, near the top across the gap columns,
   leaving air beneath. Skip any arch that would block a `GateAnchor` sightline.

Everything added here is real walkable, rendered, collidable terrain — not a
decoration overlay.

### Stage 5 — Feature anchors (metadata, terrain untouched)

- `CoastCells` — land cells adjacent to aether. Docks, airstrip.
- `CliffCells` — columns exposed on ≥1 side by ≥ K blocks. Essencercoral,
  hanging growth attach here. (A cheap version of this test also gates Stage 4b.)
- `Overhangs` — columns that ended up with more than one span. Attach points for
  hanging growth from below; also flags where pathing must treat two walkable
  levels in one cell.
- `Shelves` — `FlatRegion { level, cells, bbox, area }` for every surviving flat
  patch on a top span; the settlement/economy layer consumes this.
- `GateAnchor` — a reserved open point a few cells *off* the coast on one of the
  four Link edges, with clear sightline to the edge, for the offshore aether
  opening from "The First Hour".
- `Reachable` — flood fill from the largest shelf using the traversal rule (a
  single-block step up or down is free; a 2+ face is a wall). Flags stranded
  land.

### Stage 6 — Guarantees & re-roll

- At least one shelf must contain a contiguous flat region ≥ `MinSettlementArea`.
  If not: try lowering/merging a terrace or relaxing `MinShelfWidth` once, else
  bump the seed and regenerate (bounded retries).
- If connectivity is required (§7) and the largest reachable set is < X% of land,
  re-roll.

---

## 5. Rendering handoff (summary — the mesher is its own task)

`IslandData` feeds the terrain renderer; it does **not** spawn per-block nodes.

- **Face selection**, per column, per span `s` in that column:
  - top face at `s.Top + 1`; bottom face at `s.Bottom` (the lowest span always
    has air below; every higher span has the inter-span gap below it — that gap's
    ceiling is what makes an overhang read as one).
  - side faces toward each 4-neighbour: walk this column's spans and the
    neighbour's spans together and emit a face for every sub-range of
    `s.Bottom..s.Top` the neighbour does **not** cover (or the whole range if the
    neighbour has no land).
- **Chunking:** tile the footprint into 16×16 or 32×32 chunks; each chunk builds
  one `ArrayMesh` and one collider. Greedy-merge coplanar faces for triangle
  count (later pass).
- **Collision:** custom trimesh per chunk (a `HeightMapShape3D` only models a
  single surface — no rim, no underside, no overhangs — so it can't represent the
  island). Prototype can use box colliders per span.
- **First cut** may skip meshing entirely: one `MultiMeshInstance3D` of the
  existing `grass_block` mesh, one instance per column at `SurfaceLevel` (ignores
  overhangs). Ugly, no culling, but on screen fast; overhangs appear once the
  real mesher lands.

---

## 6. Namespace / file layout (proposed)

```
scripts/generation/
  IslandParams.cs          struct of the §3 knobs
  IslandData.cs            the §2 output + metadata types
  IslandGenerator.cs       Generate(seed, params) -> IslandData ; stages 1–6 (incl. 4b)
  Noise.cs                 fBm / ridged / domain-warp helpers over FastNoiseLite
scripts/terrain/
  IslandMesher.cs          IslandData -> chunked meshes + colliders (later)
scenes/dev/
  island_lab.tscn          debug scene: exported params, regenerate on change
```

All C#, namespace `ProjectNikitin.Generation` / `.Terrain`.

---

## 7. Open questions

- **Domain size.** This spec uses **128 × 128 × 128** (position: vasin; Maxim
  favours smaller; the Notion "Ecumene" page still says 16³–64³). The columnar
  model makes 128 cheap, so the spec commits to it pending a logged decision.
- **Connectivity.** Hard guarantee that all land is reachable under the traversal
  rule, or just "the largest region is playable and the rest is scenery"?
- **Terrace elevations.** Noise-driven vs. evenly spaced vs. authored per biome.
- **Traversal granularity.** With cube blocks and no half-blocks, a single step
  is a full block (~a small part of an island tall). Confirm one block up/down is
  still "free" movement, or whether even that needs a ramp.
- **Link edges.** Do all four edges need a reserved coastal `GateAnchor`, or only
  edges that actually get a Gate built?
- **Half-blocks.** Deferred. If added, they change Stage 3 (finer terracing) and
  `Span` bounds become fixed-point or grow a half-step flag.
- **Overhang pathing.** A column with two spans has two walkable levels; the
  reachability flood-fill and later nav need to treat spans, not columns, as
  nodes. Not yet designed.
- **Arch structural plausibility.** Stage 4b bridges by rule; nothing checks that
  the result looks load-bearing. May need a min-support or aesthetic pass.

---

## 8. First implementation slice (this branch)

1. `Span`, `IslandParams`, `IslandData` types; `IslandGenerator.Generate`
   skeleton.
2. Stages 1–4 only (mask → height → terrace → keel → one span per column). No
   overhangs, no metadata, no guarantees.
3. `MultiMeshInstance3D` of `grass_block`, one instance per column at
   `SurfaceLevel` — no mesher, no culling. Just make an island appear.
4. `scenes/dev/island_lab.tscn` with the params as `[Export]`s and a
   regenerate-on-change button.
5. Tune params until size/density, relief, and shelves are visibly achievable.

Then: Stage 4b overhangs, the chunked span-aware mesher + colliders, feature
anchors, the §6 guarantees, settlement placement hooks.
