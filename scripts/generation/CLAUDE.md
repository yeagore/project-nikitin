# Island generation: working notes

Loaded when you work under `scripts/generation/`. The root `CLAUDE.md` has the
project, the spatial model and the vocabulary; this file is what you need before
touching the generator. The dev scenes that drive it are under `scripts/dev/`
(see `scripts/dev/CLAUDE.md` and `docs/dev-scenes.md`).

## The pipeline

Full spec: **`docs/island-generation.md`**. Reasoning, things tried and removed,
the audit and the ideas not taken: **`docs/island-generation-appendix.md`**.

`IslandGenerator.Generate(seed, IslandParams)` is a pure function producing the
columnar `IslandData`; it re-rolls (from a derived seed) a Domain that comes out
unplayable. `IslandGenerator` is the orchestrator; each stage is a static class
under `scripts/generation/`, in the order they run:

| Stage | Class | What it settles |
|---|---|---|
| Footprint | `Footprint`, `Fjords`, `Landmasses` | The land mask: lobes laid out per `IslandArrangement` (thirty shapes), bitten, cut with fjords (winding inlets of aether along one grain per Domain into the largest landmass, never through it; rifts were tried and removed), huddled within bridge reach, fitted to 55–85% of the grid; two or three of the specks dropped as too small kept as sea stacks (aether, an anchor list). |
| Regions | `Regions`, `Landforms` | A warped Voronoi of patches; each gets a `LandformType` (ten of them, by quota from the `TerrainCharacter`) and a rung on the plateau ladder. |
| Surface | `Relief`, `StepGrammar`, `Sculpting` | Relief under each landform's slope limit, settled to the free step; sculpted landforms, passes and canyons cut into it and exempted. |
| Standing water | `Lakes` | Lakes sunk into flat patches with their own rim as containment, shaped; each bed flat or a bathymetry (a bowl, a shelf with a drop-off, a plunge, to 20 slabs at 128²; the deepest cell a deep, the bed tiered by the water over it: shallow to two slabs, mid to eight, deep of ooze from nine); on about one Domain in ten a great lake, two to five patches on one rung flooded as one site; goo puddles that never touch water (off by default since 2026-09-15). |
| Settle | `Beaches`, `Bridgeheads` | Beaches, then the lowering passes cycled until nothing moves. |
| Rivers | `Rivers` | Priority flood from the rim with noise-broken ties; beds, banks, valleys, navigable reaches as a stair of pools, fords spaced by the ground's relief, falls, springs; a plunge pool dug under most inner falls and a deep middle on half the long reaches (the bed, never the water); occasionally a lake that swallows a river; at a navigable mouth over gentle ground an estuary (the lower reach opened into a funnel four to six cells across, crossed nowhere) or a delta. |
| Keel | `Keel` | The underside, a level hung under the surface; then a root pass brings every column to at least the edge thickness under the lowest ground beside it and tapers the push outward three slabs a cell, so a deep bed never hangs clear of the island (2026-09-19; the audit holds `hangingColumns` and `waterOpenBelow` at 0). The columns are packed into `IslandData`. |
| Traversal | `Traversal` | Read-back: walk areas (a district — walk-connected, no works — is somewhere to build), reach areas (once built, by ladders, stairs and bridges), water bodies. Shelves are gone; so are ferries (2026-09-07: one island in sixty ever kept a berth). |
| Gates | `GatePlacement` | Four hanging Gates chosen as a set, one per edge; then subtraction to what was asked for. Levels its landing strips, so traversal runs again. |
| Roads | `Passages` | The least-works road from the Entry to each Exit. |
| Habitat | `Habitat`, `Surfaces`, `Names` | The six-byte habitat vector: moisture (the wind's rain shadow, damp sheltered gorges, the water strip), warmth (a lapse per mountain from its own foot, a rolled sun on the slopes, frost hollows, the milder lee), ruggedness, exposure, rim distance and water distance; the wind knob scales what exposure moves. On a cold Domain some springs and pools run hot, with a bloom of warmth round each. Then the feature anchors and a provisional material per column, named and coloured by the soil glossary (a four-by-three climate grid of plain soil names, frostearth to redearth, with murkearth on the cold-to-cool half and muckearth on the warm-to-hot, floodearth along hot water, tors in soft country, a delta's fan the wet ground of its row), names. |
| Magicks | `Magicks` | The magickal density byte, grown rather than sampled: a Turing reaction (Gray–Scott) between the magick, which makes more of itself, and the inhibitor it feeds on, which is replenished everywhere and spreads faster — so the field breaks into spots, worms, mazes or lace instead of settling flat. Its coefficients are not knobs: the settings that pattern at all are islands in a sea of dead and flooded ones, so six of them are named as `MagickPattern` (motes, wells, veins, labyrinth, lace, hollows) and the stage shows two parameters — which pattern, and `MagickDensity`, how much magick the Domain holds. The reaction runs on its own lattice, three ground cells to the side, and is enlarged back onto the columns, so a feature is three cells across for every cell it would have been — magick is a place, not a texture. Read by nothing. |
| Overhangs | `Overhangs` | The only stage that gives a column a second span; runs last because a lip is a roof, not ground. |

Shared: `Grid` (neighbourhoods; their order is a tie-breaker everywhere),
`SeedHash` (one mixer; the salt at each call site keeps rolls apart), `Flood`, `Terrain`, `FieldOps`, `Noise`.

**Auto knobs.** The twelve 0–1 knobs in `IslandParams` (fjords, relief, hilliness, mix,
rivers, lakes, valleys, moisture, warmth, wind, overhang density, magick density)
accept `IslandParams.Auto` (any negative value); `Roster.ResolveKnobs`
then rolls them from the seed before anything runs, and the values used are
`IslandData.Settings`. `MagickPattern` is resolved there too, `Auto` picking one
of the six evenly — it is a named point on the reaction's plane, not a number to
roll over a range. The preset leaves all of them on Auto, so the audit's default
seeds sample the whole knob space; a sweep pins the knob it sweeps.

**Two regression gates.** `generation_checksum.tscn` hashes every field of
`IslandData` for 458 islands against `docs/checksum-baseline.txt`: a change
meant to leave generation alone must report zero moved; one meant to change it
re-baselines with `-- accept` and says so. `generation_audit.tscn` prints the
measured guarantees and diffs its headline numbers (forty-six) against
`docs/audit-baseline.json`. Determinism hangs on details a refactor can break
silently: hash salts, `Noise` seed offsets, float expression order, scan and
neighbour order, `List.Sort` (unstable) versus `OrderBy`, and dictionary
insertion order. When in doubt, run the checksum.

Newer content ships behind a toggle that takes it out of `Auto`'s dice without
taking it out of the code (`NewArrangements`, `NewLandforms`).

**The knob matrix.** `generation_audit.tscn -- Seeds=1 KnobMatrix` steps every
0–1 knob over the same seeds against twenty-six outcomes, paired per seed, and
prints what each knob moves, whether it reverses and what else it moves. Run it
after touching a knob; the 2026-09-07 reading is in the appendix.

## The two commands after touching the generator

```
godot --path . --headless scenes/dev/generation_checksum.tscn     # 0 of 458 islands moved?
godot --path . --headless --quit-after 2 scenes/dev/generation_audit.tscn   # the measured guarantees
```

Run both under a timeout (headless Godot does not always exit; macOS has no
`timeout`, use `perl -e 'alarm 900; exec @ARGV' <godot> ...`), and note the
Windows machine prints decimals with a comma. They are separate processes and can
run at once; the `runner` agent does exactly this. To *look* at a shape headless,
the audit's `Gallery=<dir> GalleryShapes=Isthmus,Quarters` writes a contact sheet
of sixteen seeds per arrangement, captioned with the landmass count.

`docs/island-generation-plain.md` is the spec, the appendix and the manual retold
in plain words for Maxim. Do not read it for orientation (the spec is the source);
do keep it true, in the same plain register, whenever a change alters what the
spec or the dev-scenes manual say.

## The files

```
IslandGenerator.cs         Generate(seed, params): the stages in order, the re-roll.
Footprint.cs, Fjords.cs, Landmasses.cs, Bridgeheads.cs, Regions.cs, Landforms.cs,
Relief.cs, StepGrammar.cs, Sculpting.cs, Beaches.cs, Lakes.cs, Keel.cs,
Roster.cs                  The terrain stages (see the table above).
Rivers*.cs                 Drainage routing, channels, valleys, profile, falls, fords,
                           deltas and springs, the lake that swallows a river.
Traversal*.cs, WalkArea.cs, Crossing.cs, BridgeEase.cs
                           The read-back analysis and its value types.
Passage.cs, Works.cs       The roads between the Gates.
Gate.cs, GatePlacement.cs, GateSites.cs
Habitat.cs, Magicks.cs, MagickPattern.cs, Surfaces.cs, SurfaceMaterial.cs,
Names.cs, Overhangs.cs
IslandData.cs, IslandParams.cs, Span.cs, Terrain.cs
LandformType.cs, TerrainCharacter.cs, ReliefStyle.cs, IslandArrangement.cs,
FluidKind.cs, Geyser.cs, Fall.cs, RegionPlan.cs
Grid.cs, SeedHash.cs, Flood.cs, Noise.cs, FieldOps.cs
```

`resources/island_default.tres` is the `IslandParams` preset every dev scene and
the game scene load. `docs/audit-baseline.json` and `docs/checksum-baseline.txt`
are the last accepted numbers and hashes.
