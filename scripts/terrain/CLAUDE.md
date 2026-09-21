# Rendering: working notes

Loaded when you work under `scripts/terrain/`. The root `CLAUDE.md` has the
project, the spatial model and the grid-to-world rule; this file is what you need
before touching the renderer.

## The renderer

Spec: **`docs/island-generation.md` §4**. Code under `scripts/terrain/`,
namespace **`ProjectNikitin.Meshing`**, not `.Terrain`: a namespace of that name
would shadow the `Terrain` constants class for every file under
`ProjectNikitin`.

`IslandRenderer` is the terrain renderer: a `Node3D` that draws an `IslandData`
as `TerrainChunk`s of 16 × 16 columns, each a `StaticBody3D` holding a ground
`ArrayMesh`, a liquid `ArrayMesh` (water, goo and falling water as three surfaces) and a trimesh
collider over the ground. `ChunkMesher` is the pure part: per column per span it
emits the top at `Top + 1`, the underside at `Bottom` and a side wherever the
neighbouring column's spans do not fill that slab range, merged over the range
so a cliff is one quad; water gets its top at `WaterLevel + 1` and a wall
wherever it meets anything that is neither solid nor the same water, which is
what the lip of a fall and a cataract are; the fall itself is a fourth surface,
a sheet down the rock from the lip's bed to what it lands on, one per `Fall` and
one per two-slab cataract (`FaceKind.Fall`, `TerrainMaterials.Falls`). Nothing
buried is emitted, and the bench's voxel oracle checks that to 0.000 m², with a
second oracle for the falling water. Vertices are flat-shaded quads with a normal, a
UV in metres, UV2 = (material or fluid byte, `FaceKind`) for a shader to read,
and a colour from an `IslandTint`: two callbacks the lab swaps per view and the
game leaves at `IslandTint.Default` (the column's `SurfaceMaterial` through
`SurfacePalette`, stone for a lip and every underside). `TerrainMaterials` holds
the four materials; the lab's boxes use the same factories. The ground reads its
vertex colour as sRGB (`VertexColorIsSrgb`), as the palettes are written, so a
face draws its legend swatch's hex; the water still reads its tint as linear. In the renderer's
local space cell (x, z) is centred on `(x · CellSize, ·, z · CellSize)`, the grid
→ world rule above. `Show(data)` builds everything; `RebuildAround(x, z)`
remeshes the chunk holding a column and the neighbours its border faces depend
on, the hook a build or a terraform will call.

Measured by `mesh_bench.tscn` on the Mac: a 128² island is about 50,000 ground
triangles (62% of what the boxes drew) meshed in 9 ms, with meshes, colliders
and nodes in another 40 ms, about 6 MB. Triangles were never the cost; the
performance question the mesher was to answer is answered, yes with room to
spare. Greedy merging of coplanar faces is not done and not needed. `main.tscn`
(F5) shows one generated Domain through the renderer (`Main.cs`: N for a new
seed, F to frame); the lab draws through it too, Z for the old boxes, and reads
the column under the cursor off the colliders with a ray (`IslandLab.Pick.cs`),
the pattern a settlement placer's cell pick will follow. The bench casts rays
at every third column from above and below and expects the top and the keel.

**Many Domains.** `godot --path . scenes/dev/domains_bench.tscn -- domains=20`
(windowed) lays out N Domains on consecutive seeds in a grid a quarter footprint
apart, frames them all, and after six seconds with vsync off prints the frame
rate, draw calls, primitives, the render thread's CPU time and memory, then
quits (the GPU time
reads 0 on Metal; past 150 Domains it builds no colliders, since Jolt's default
cap of 10,240 bodies is 160 Domains × 64 chunk bodies, a project setting).
Measured on the Mac (M2, 16 GB, a 4K display) on 2026-09-06 with every Domain
in view: 1, 20, 40 and 80 Domains all hold the display's 120 Hz; 80 is 4,173
draw calls and 3.0 million triangles. The knee is between 80 and 160: 160
Domains (8,300 draw calls, 6.1 million triangles) run at 65 fps, 320 at 33,
640 at 17, the frame time growing about 0.1 ms per Domain in view with draw
submission about 0.65 µs a call. Per Domain: about 52 draw calls, 50,000
triangles, 3.5 MB of video memory, 1 MB of data and 2 MB of collider, over a
170 MB engine baseline. Rendering the terrain of twenty Domains is not the
constraint; generating them is 3.3 s for twenty on one thread at load, and
`Generate` is pure, so that parallelises. The budget the biome layer inherits
with one Domain in view is some 4 million triangles a frame at 120 Hz on this
machine, on two conditions: features are drawn by instancing (`MultiMesh`),
never a node or a draw call per tree, and the directional shadow's cascades,
which multiply geometry cost, are the first knob if it is ever needed.

## The two commands after touching the renderer

```
godot --path . --headless scenes/dev/mesh_bench.tscn              # triangles, times, the winding probe, the voxel oracle, the colliders
godot --path . scenes/dev/domains_bench.tscn -- domains=20        # windowed: the frame rate with N Domains in view
```

To look at the *rendered* island without a hand on the keys, the lab takes a
screenshot from a shell and quits: `godot --path . scenes/dev/island_lab.tscn --
shot nopanel zoom=4` (windowed, since a screenshot needs a viewport; a window
opens for a few seconds on the machine it runs on).

## The files

```
IslandRenderer.cs          The terrain renderer: the chunk grid; Show and RebuildAround.
TerrainChunk.cs            One 16 × 16 tile: ground mesh, liquid mesh, trimesh collider.
ChunkMesher.cs             The pure mesher: the exposed faces of one chunk.
MeshBuffer.cs              Quads into ArrayMesh arrays and collider faces; the winding rule.
IslandTint.cs, FaceKind.cs, SurfacePalette.cs, TerrainMaterials.cs
                           Colour per face, which side a face is, the provisional
                           material palette (the soil glossary's colours), the materials.
```

`scripts/Main.cs` is the game scene's script (generate, show, frame) and
`scripts/CameraRig.cs` the strategy camera.
