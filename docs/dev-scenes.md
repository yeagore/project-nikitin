# Dev scenes: the lab, the audit, the checksum, the mesh bench

Five scenes under `scenes/dev/` drive the generator without the game: three
measure the generator, two the renderer that draws its islands. All five load
the same preset, `resources/island_default.tres`, so the audit measures the
island the lab shows. Edit the `.tres` in the Inspector to change it durably; use
the lab's panel (or the Remote tab of the Scene dock) for a throwaway experiment.
A sixth scene, the economy lab, has nothing to do with terrain; it is at the end
of this file and has a manual of its own.

Godot is not on `PATH`; from a shell use the .NET build's own binary:

```
# macOS
/Applications/Godot_mono.app/Contents/MacOS/Godot --path . --headless ...
# Windows (the console build, quoted)
D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
```

Headless runs print `GD.Print` output and need the C# assembly built first
(`dotnet build "Project Nikitin.csproj"`). Gotchas: headless Godot sometimes
does not exit on `--quit-after`, so run it under a timeout — macOS has no
`timeout`, so `perl -e 'alarm 900; exec @ARGV' <godot> ...` does the job; the
Windows machine's locale prints decimals with a comma, which breaks patterns
looking for `\.`; and the headless runs are independent processes, so the
checksum, the audit and a collage can run at once.

The preset leaves the twelve 0–1 knobs (fjords, relief, hilliness, mix, rivers, lakes,
valleys, moisture, warmth, wind, overhangs, magick density) and the magick
pattern on **Auto**, so every seed rolls its own;
a sweep that sets a knob pins it for every seed it builds, and the others still
roll — the same way for every step of the sweep, since the roll is the seed's.

## The island lab — `island_lab.tscn`

Open the project in the .NET editor, build C#, open the scene and press **F6**
(it is not the main scene, so F5 will not run it). The control panel down the
left is the interface. The text is kept to the edges so the island has the
middle (2026-09-19; the legend, the island's readout and the cell line used to
stack in front of it): the island's readout along the top, folded to its title
and two lines until **F3** or its **more** button opens it; the view's legend
down the right edge, a colour to a line with the small print under the colours
(**F4** hides it); and under the legend the cell under the cursor. Each plate
scrolls rather than grows, and nothing sits along the bottom, which the editor's
chrome hides when the game is embedded. **Tab** (or **F1**) hides every plate at
once. Every control is also a key, and both write the same `Params`:

| Key | Does |
|---|---|
| **N** / **R** / **F** | new seed / rebuild / frame the island |
| **C** / **V** / **G** | cycle view / character / arrangement |
| **H** / **M** / **L** | hilliness / mix / plateau rungs |
| **U** | toggle the newer shapes in Auto's pool |
| **T** / **Y** | entry Gate kind / bridge ease |
| **B J O P X** | overlays: bridge sites, Gate landings, fords, roads, compass |
| **I** | liquid on or off: water, goo and falls; off shows the beds |
| **Z** | the ground as the game's mesh, or as the old box per span |
| **F2** | screenshot |
| **Tab** / **F1** | every plate on or off |
| **F3** / **F4** | open or fold the island's readout / hide or show the legend |

Camera: **WASD** pan, **Q/E** or middle-drag yaw, middle-drag or **Up/Down**
tilt, wheel zoom, **Shift** faster. (Fords are on **O** because **D** is the
strafe, and liquid on **I** because **W** is forward.)

Each 0–1 knob has an **auto** box beside it. Ticked, the seed rolls that knob:
the slider is greyed, and once the island is built it sits at what the seed
rolled and the caption says so (`Warmth   auto -> 0.71`), so the slider's
position is always true to the island shown. Untick the box to keep that value
and set it yourself; tick it again to hand the knob back to the seed, or press
**All knobs to auto** for all of them at once. The readout's `settings:` line
lists them all over two lines, a star on each rolled one. **H** and **M** step
their knob through auto, 0, 0.25 … 1.

The two under **magicks** are the whole of the layer the Turing reaction grows;
watch them on the `magick` view (**C**). **Magick pattern** is what shape it
takes — `motes` a fine dusting of points, `wells` round wells standing well
apart, `veins` worms winding across the country, `labyrinth` those veins joined
into one convoluted corridor, `lace` an open fine-strutted net, `hollows` the
inverse, a saturated Domain with inert hollows punched through it. It is a
dropdown and not a slider because each is a named point on the reaction's plane
and the country between two of them mostly grows nothing at all. **Magick
density** is how much magick the Domain holds on average, and 0 means none — the
whole Domain inert, the view flat at the ramp's dark end. A few hundredths leaves
only the crowns of the strongest wells standing on dead ground. It thickens what
the pattern draws and then cuts or lifts the level the byte is read at, and never
changes the pattern's kind or its scale. Its slider asks a bend, so the middle of
it is a quarter of the full mean and not half. The reaction is the one stage that costs real time
on a rebuild — it is steps × cells, and the patterns run three to three and a
half thousand steps.

The **Size** dropdown has an **Auto** entry that works the same way: the seed picks
one of the three footprints and the caption says which. **Goo may roll** is the
toggle for goo puddles; off, no seed makes any, and off is the default since
2026-09-15, so the audit's goo rows read 0 unless it is turned on. The preset's Gates are three
hanging Exits and a hanging Entry; the panel can still ask for anything else.

**N** rolls a seed; the **Seed** field under the buttons takes one you already
have — a seed named in an audit run, a commit or a screenshot — and **Enter** or
**Build** generates it against whatever the panel's parameters currently say.
Anything that is not a whole number is put back. The field is a `LineEdit`, so
while it has focus it swallows the single-key shortcuts; the camera still polls
**WASD** every frame, but a seed is digits.

Views: `height`, `landform`, `region`, `walk` (what connects on foot; a
district of twenty cells is somewhere to build), `reach` (what connects once
you build; red is out of reach whatever you build), `navigable` (walk's
counterpart on the water: a hue per body of sailable water — standing water and
navigable reaches, never goo and never a stream, which is forded and not sailed
— so a hull goes anywhere within one hue and nowhere between two, since a fall
cuts a body and nothing sails up one. The lip a body ends at is white, water no
hull uses slate, and the bed under a body carries its colour dimmed, so the
regions still read with the liquid off (**I**). In this view the readout gains a
line that names every body, counts its cells and says whether they are still
water or a reach; the water's own blue is dropped from the material there, or it
would multiply a warm hue down to nothing), `surface` (stone, scree,
snow, sand, silt, ooze (a bed under nine slabs of water or more) and the climate
grid — frostearth, bleachearth, shadowearth; dryearth, blackearth,
brownearth; dustearth, yellowearth, redearth, floodearth; murkearth and
muckearth for water in excess; tors of stone in soft country; an overhang's lip is drawn as stone; a beach is
the ground round it, not sand), `anchors` (what the content layer attaches to:
coast; cliff brink, cliff foot and a ledge where a cell is both, for a face of
4+ slabs; scarp brink, scarp foot and their ledge for a face of 2–3, a cliff
anchor winning where a cell is both kinds; bank, river bed,
lake bed, shallow lake bed (two slabs of water or fewer), mid lake bed (three to
eight), deep lake bed (nine or more), deep (the deepest cell of a lake with a bathymetry, or the pool under a
fall), goo bed, spring, hot spring or pool, fall, overhang lip, beach, ford,
Gate landing, summit; a sea stack is a dark column in the aether in
every view), the six habitat axes as ramps: `moisture`, `warmth`, `rugged`,
`exposure`, `rim`, `water` (the walk cost to fresh water), and the `magick`
layer (the Turing reaction's spots, worms, mazes and lace). Water is coloured by
kind (ford, stream, navigable reach, lake; hot water orange) in every view but
`navigable`, which colours it by body instead, and darker the deeper its bed lies
under what its kind is cut to, so a bathymetry, a plunge pool and a deep reach
read through the surface; goo is violet in all of them. The
legend shows each view's actual colours as swatches, from the one palette
(`DevPalette`) the audit's PNGs also use. The lighting is tuned so a top face
reads at about the legend's colour: a steep white sun over a neutral ambient,
linear tonemapping, no specular, and the ground reads its vertex colour as sRGB,
the way the palettes are written (until 2026-09-19 it read it as linear, which
drew every colour paler than its swatch: a deep red came out salmon). The water
still reads its tint as linear.

In the `anchors` view a column is coloured per span: only the lip of an
overhang is magenta, and the ground under it is whatever it is — a river bed,
a cliff foot. Turn the liquid off (**I**) to see the beds.

Overlays (bridge sites, landings, roads, fords and the compass are on when the lab
opens): **B** bridge sites; **J** each Gate's 1 × 3 landing strip; **O** fords;
**P** the roads between the Gates (pale yellow walk; violet ladder, red stair, gold bridge);
**X** the compass, each Gate's landward vector,
the Domain's wind — a run of orange arrows standing off the upwind edge with
its name, whether or not there are dunes, plus its grain along each dune field —
the sun, a gold disc off the edge it shines from with its name (the warmth
view's sunny and shaded slopes read against it), and two bounding boxes: the faint cube of the Domain (Size cells across and
Size slabs tall, standing on the keel's lowest point; nothing the generator
builds may hang outside it, and its shape never changes between seeds) and a
gold box tight round the landmass.

The ground is drawn by the game's own renderer (`IslandRenderer`, the chunked
mesh with colliders; `docs/island-generation.md` §4). The mesh is on when the
lab opens; **Z**, or the **Mesh, not boxes** box under GROUND, swaps in the old
one-box-per-span drawing, which is also the only mode that draws the sea stacks.
With the mesh on, the water is the mesh's own: a flat top per flooded column, a
wall wherever the water meets air, and a sheet of falling water down the rock
under every fall's lip and every two-slab cataract (until 2026-09-19 the mesh
drew only the lip's wall and the lab hid its own fall sheets, so a mesh had no
waterfalls). Every view tints the mesh as it tinted
the boxes, every face of a span in the span's colour. The readout's last line
says which mode is on, how many triangles and chunks the mesh came to and how
long it took to build.

**The cell under the cursor** (the plate under the legend, with the mesh on) is
read off the chunk's collider with a ray from the camera; nothing is cast while
the cursor is over a plate. It says what the column is in any view — cell and
slab, landform and patch, ground level and material, its water by kind with its
level and depth — and then what the *current view* says about it, so the cell
answers the question the map is asking:

| View | The cell says |
|---|---|
| height | top and keel, slabs of ground, any lip and the air under it, the step to each neighbour (free, scarp, cliff, water, aether) |
| landform, region | the landform, the patch and its size, a pass, a canyon, a fjord shore; on the patch's border or inside it |
| walk | the mainland, a named district or broken ground, with its size and levels, whether it is somewhere to build, the steps |
| reach | the heartland or out of reach, with its size |
| navigable | the body by name with its cells, a lip where the body ends, and any water beside it standing at another level (and whether that is the same body) |
| surface | the material and what decided it: the warmth and moisture bands, the walk to water, a beach, a delta's fan |
| anchors | **every anchor list the cell is on** (the lists overlap and the map shows one colour), then which one the colour is |
| moisture, warmth | the byte and its band, and the inputs that move it: exposure, ruggedness, rim distance, the background knob, the wind and the sun |
| rugged, exposure, rim, water, magick | the byte and what it measures; the magick pattern and density |

The band names are read off the surface stage's own chart lines, so they cannot
drift from the rule. It is the first thing to use the colliders, and the way to
ask "what is that cell".

The readout along the top says what the island
turned out to be: name, arrangement, the landforms it got, the ladder, walk and
reach shares, districts (and how many the heartland holds), bodies of water,
rivers, springs, any lake that swallows a river, great lakes, deeps, deltas, the
wind and the sun, Gates, and what each road out costs.
`ROUGH GOING` means a road climbs five elevators in fifteen cells; `COAST WOULD
NOT` means a Gate you asked for is not the Gate you got.

If the window is 1152 × 648 and will not stretch, the editor is embedding the
game: Editor Settings → Run → Window Placement → Game Embed Mode: Disabled.

### A screenshot from a shell — `-- shot`

```
godot --path . scenes/dev/island_lab.tscn -- shot nopanel view=surface zoom=4 at=64,96
```

Windowed, not headless (a screenshot needs a viewport): the lab builds the
island, frames it, saves the screenshot **F2** would have saved
(`user://island-<seed>-<view>.png`; the full path is printed) a few frames in,
and quits by itself. `seed=N` picks the seed, `view=NAME` the view, `boxes` the
box drawing, `nopanel` hides every plate, `noliquid` the beds with the water off
(**I** without a hand on the keys), `zoom=N` frames a 1/N of the island and
`at=X,Z` centres that on a cell; `tilt=DEG` sets the camera's height above the
horizon (`under` is −40, up at the keel) and `yaw=DEG` turns it about the island
(180 looks from the north); `pick=X,Z` pins the cell readout to a cell, since a
shell run has no cursor over its window. The run also prints the tallest inner
falls, the fjord mouths and the estuary mouths as cells, to aim a second shot at. It is the one way to look at the mesh without
a hand on the keys; the audit's pictures are drawn from the data and never see
the renderer.

## The audit — `generation_audit.tscn`

```
godot --path . --headless --quit-after 2 scenes/dev/generation_audit.tscn
```

Runs the real generator over 60 seeds at 128² and prints the measured
guarantees: the step grammar, patches, landforms, lakes with their beds and the
great lakes, goo, rivers and the bodies of water, surfaces and habitat, roads,
Gates, crossings, continuity. Run it after any
change to the generator. A `want 0` that is not 0 names its seed as it happens
(a crossing whose banks disagree prints the seed, the banks, the deck and what
is under each bank), so it can be built in the lab. It ends by diffing its headline numbers (forty-six) against
`docs/audit-baseline.json`; that is a diff, not a test — set `AcceptBaseline` to
accept the current numbers as the new reference.

The opt-in sweeps are `[Export]` flags on `GenerationAudit` (each documented on
its property): silhouettes and waterways as ASCII, close-ups of the sculpted
landforms, every arrangement × character, every Gate request, the four-hanging-
Gates matrix, the knob sweeps (how you check a slider does anything, and where
the magick table lives), one contact sheet of the magick layer — every pattern
down the page, `MagickSheetDensities` across it, `MagickSheet=<dir>` — the
material shares at the four climate corners (`Climate`), land share
per arrangement, the guarantee set at all three sizes (`Sizes`, with the share of
snow and how many mountainous islands carry any — the snow line has to exist at
64² as well as 128²), the newest shapes at every
footprint, where re-rolls cluster, and PNG portraits, field maps, the gallery and
the climate grid written to a directory. Every flag can be given on the command
line after `--`:

```
godot --path . --headless scenes/dev/generation_audit.tscn -- Knobs Sizes Portraits=C:/tmp/portraits
```

### The gallery — `Gallery`

`Gallery=<dir>` writes one contact sheet per arrangement: `GallerySeeds` (16)
consecutive seeds from `FirstSeed` at `GallerySize`² (96), four to a row, each
tile the portrait view captioned with its seed and how many landmasses it came
out as, and prints the landmass histogram per shape. `Portraits` draws two
islands per shape, which says what a shape *can* be; the gallery says what it
usually is, which is the question when a shape "often" merges or parts.
`GalleryShapes=Isthmus,Quarters` restricts it to the shapes you are working on;
run it at 64 and 128 as well, since a shape that only reads at 96 is a shape
that lies. A caption too wide for its tile goes on two lines rather than into
the next tile. `Fjords=1` (or `0`) pins the fjords knob, so a sheet shows every
tile cut, or none, the way `Arrangement=` and `Character=` pin the shape.

```
godot --path . --headless scenes/dev/generation_audit.tscn -- Seeds=1 Gallery=C:/tmp/gallery GalleryShapes=Isthmus,Caldera GallerySize=64
```

The summary line per shape also carries the mean re-roll count, how many seeds
missed a guarantee (`unmet`), the mean land and extent shares, and the seeds that
re-rolled or came out unmet, so a shape that reads oddly can be tied to a seed.
`GalleryMasks` writes a second sheet per shape, `<Shape>_<n>_mask.png`: the same
seeds' raw footprint masks from `BuildMask` alone, before the bites, the linker
and the fit pass, captioned with the mask's landmass count and extent share.
When a shape comes out wrong, the pair says whether the layout or what came after
is to blame; the extent number says whether the fit pass had to blow it up.

Appearance still needs a human at the editor, or **F2** in the lab.

### The climate grid — `ClimateGrid`

`ClimateGrid=<dir>` writes one sheet showing the whole climate model at once: a
single seed generated twenty-five times, at every pair of background moisture and
warmth in quarters, drawn as the surface view with warmth across, moisture down,
and a legend of every colour. Moisture and warmth are read by the Habitat stage
alone, so the terrain is the same island in every tile and the sheet is the
climate model on its own. The text on it is plain and sized to the tile: the
ramp labels under the field strip drop to two lines when a 48-cell tile is too
narrow for both, and the legend columns are as wide as their longest name. Under the grid is a strip of the seven fields the
surface is read from — height, warmth, moisture, exposure, rim distance, water
distance and magick, in two rows — each with its own ramp, which is the context
for why a tile looks as it does: the snow line is the lapse crossing the height
panel, the green threads are the fresh-water moisture strip. Height, exposure,
rim, water distance and magick hold across all twenty-five, so they are drawn
once and the run prints the cell counts that prove it; warmth and moisture are
the middle tile. The note over the strip names the wind and the sun the seed
rolled. `ClimateGridSize` picks the footprint (64 by default; 128
does not read at a glance) and `FirstSeed` picks the seed. It also prints the
material shares of each of the twenty-five, and drops the tiles beside the sheet.

`ClimateScout=<n>` is how the seed gets chosen rather than guessed: it scores `n`
consecutive seeds from `FirstSeed` at the same footprint for landform and material
variety, lakes, rivers and navigable water, and prints them best first. Avoid the
characters that force a material — Dunes, Karst, Badlands make sand and dustearth
whatever the climate — or the sheet shows the character rather than the knobs.

```
godot --path . --headless scenes/dev/generation_audit.tscn -- Seeds=1 ClimateScout=48 FirstSeed=7000
godot --path . --headless scenes/dev/generation_audit.tscn -- Seeds=1 FirstSeed=7046 ClimateGrid=C:/tmp/climate
```

### The knob matrix — `KnobMatrix`

```
godot --path . --headless scenes/dev/generation_audit.tscn -- Seeds=1 KnobMatrix SweepSeeds=16
```

Every 0–1 knob (mix, relief, hilliness, rivers, lakes, valleys, moisture,
warmth, wind, overhangs, magick density) at 0, ¼, ½, ¾ and 1 over the same
`SweepSeeds` seeds, the other knobs rolled by each seed as the preset rolls
them, and twenty-eight outcomes measured on every island: land share, high-ground
share, altitude spread, mean slope, cliff and two-slab shares, hill relief,
river, navigable, lake and ford cells, falls, springs, lake count, great lakes,
the deepest lake cell, the valley rise, mean moisture, warmth and magick, the wet and snow material shares, the
rain-shadow gap, overhang columns, the mainland and heartland shares, districts
and attempts. The comparison is paired, each seed against itself at 0, so the
spread the rolled knobs put between seeds cancels. For each knob it prints its
own promised outcomes at the five steps, then what it moves: every outcome whose
change from 0 to 1 is more than twice its standard error and at least a quarter
of the spread between seeds, in spreads, with the knob's own promise in
brackets and `!` where a step along the way ran against the trend (a
reversal, or a hump). Then the whole thing as one matrix, knobs down, outcomes
across, `·` where nothing moved. A knob that promises an outcome and does not
move it, one that reverses, and one that moves another knob's outcome are the
three things to read off it. About four minutes at sixteen seeds.

### The climate chart — `ClimateChart`

`ClimateChart=<dir>` writes `climate_chart.png`: the climate grid as an area
chart, warmth across and moisture down, every byte pair coloured with the
ground it gives. Two panels, open ground away from water and flat ground
beside it (where floodearth and muckearth can be); the band lines drawn on
the axes with their bytes; the range a warmth knob reaches on open lowland
(60 to 240) bracketed, with the knob's quarters ticked on both axes; and the
patches (murkearth, muckearth) as a checker of their colour over the ground they sit in,
since a noise field decides them cell by cell. It is drawn from
`Surfaces.Climate`, the rule the surface stage itself uses, so it cannot drift
from the code. No seed is involved; `Seeds=1` keeps the run short.

```
godot --path . --headless scenes/dev/generation_audit.tscn -- Seeds=1 ClimateChart=C:/tmp/chart
```

### The surface statistics — `ClimateStats`

`ClimateStats=<dir>` counts two things and draws each. `surface_shares.png`
is the knob grid again, but each tile is the mean share of dry land every
material takes at that pair of moisture and warmth, over `StatsSeeds` (30)
seeds with everything else rolled: a stacked bar and the figures beside it.
`surface_cooccurrence.png` is a matrix over `StatsIslands` (500) rolled
seeds: row A, column B, the share of islands that have A which also have B,
with a first column for how many islands have A at all; present means twenty
cells or more of dry land, a district's worth, so a tor does not make stone
"present". Both tables are printed as text as well. About four minutes at
128².

```
godot --path . --headless scenes/dev/generation_audit.tscn -- Seeds=1 ClimateStats=C:/tmp/stats
```

### The design page's sheets — `ArrangementSheet`, `KnobSheet`, `StageSheet`

Three more one-shot sheets, drawn for the Notion page. `ArrangementSheet=<dir>`
puts `FirstSeed` through every layout at `ArrangementSize`² (64), six to a row,
each captioned. `KnobSheet=<dir>` draws `FirstSeed` at `KnobSize`² (96) with a
row per 0–1 knob — mix, relief, hilliness, rivers, lakes, valleys in the
height-and-water view, wind in the moisture view — the knob at 0, ¼, ½, ¾, 1
across and everything else rolled by the seed; a row's height ramp is on one
scale, since per-island scaling hid the relief knob, and every height view
carries a hillshade (a rise toward the south-east is brighter), so a one-slab
step shows where a bare ramp hid it. `StageSheet=<dir>` draws
`FirstSeed` at `StageSize`² (96) after every stage of the pipeline — the mask,
regions and landforms, relief, lakes, the settled surface with its beaches,
rivers, walk areas and Gates, roads, warmth, surfaces — as one sheet and as a
captioned tile per stage (`stage_NN_<name>.png`), each with its own key. It
reads the generator's
`OnStage` hook, a dev-only callback that hands each stage's live state out to
be drawn; the hook is null in play and changes nothing, and the checksum says so.

```
godot --path . --headless scenes/dev/generation_audit.tscn -- Seeds=1 FirstSeed=9005 Character=Highlands StageSheet=C:/tmp/sheets KnobSheet=C:/tmp/sheets
godot --path . --headless scenes/dev/generation_audit.tscn -- Seeds=1 FirstSeed=7046 ArrangementSheet=C:/tmp/sheets
```

The labels are pixels: headless Godot has no rendering device, so `TinyFont` draws
a 5 × 7 bitmap alphabet straight into the `Image`.

## The checksum — `generation_checksum.tscn`

```
godot --path . --headless scenes/dev/generation_checksum.tscn
```

Hashes every field of `IslandData` for 458 islands — 60 default seeds, every
arrangement × character at 64², all three sizes, every Gate request, every bridge
ease, both ends of every knob, two seeds with goo on — and diffs the hashes against
`docs/checksum-baseline.txt`. A change meant to leave generation alone must
report `0 of 458 islands moved`; a change meant to alter it re-baselines with
`-- accept` on the command line and says so in its commit. This is the
bit-for-bit gate; the audit is the readable one.

## The mesh bench — `mesh_bench.tscn`

```
godot --path . --headless scenes/dev/mesh_bench.tscn
```

Measures the renderer rather than the generator. Three seeds at each footprint
are generated and meshed by `ChunkMesher` chunk by chunk, and the run reports
per island the columns, spans and flooded cells; the ground and liquid triangles
against the twelve per span and two per flooded cell the boxes draw; the time to
mesh (the pure part) and to build the `ArrayMesh`es, colliders and nodes; and
the GPU bytes; then a mean per footprint. Two checks run with it and fail the run
(exit code 1) when they fail. The **winding probe** reads a `BoxMesh`'s triangles
to confirm Godot winds front faces clockwise, the way `MeshBuffer` assumes, so a
mistaken constant cannot leave every face visible only from inside. The **voxel
oracle** counts, slab by slab, every face of solid touching air and every face of
water touching neither solid nor the same water, as area, and compares it with
the area of the triangles the mesher emitted: `oracle off by 0.000 m²` says the
mesh is exactly the exposed faces, nothing buried drawn and nothing exposed
missed. The falling water is sheets rather than volume, so it has an oracle of
its own on the same line: under every recorded fall the rock from what it lands
on up to the lip's bed, and the same between two waters whose step is too small
to be a fall but bares the higher bed. A third check runs last, once physics has stepped: the last island
stays in the tree and a ray down onto every third land column must hit the top
of its highest span, a ray up from below its keel, so the colliders are tested
as the game will use them (headless physics is real physics). About twenty
seconds; it quits by itself and needs no timeout. Run it after touching
anything under `scripts/terrain/`.

## The Domains bench — `domains_bench.tscn`

```
godot --path . scenes/dev/domains_bench.tscn -- domains=20 seed=1337 seconds=6
```

Windowed, since a frame rate needs a screen. Lays out that many Domains on
consecutive seeds in a grid a quarter footprint apart, frames them all (the far
plane pushed to 100 km, so the count is of everything and not of what the
frustum kept), and after the seconds prints the frame rate, the render thread's
CPU time, draw calls, primitives and memory, then quits. Vsync is off; the GPU
time reads 0 on Metal, so the frame time is the measure and the display's
refresh is a floor on it. Past 150 Domains no colliders are built, since Jolt's
default cap of 10,240 bodies is 160 Domains of 64 chunk bodies (the chunk nodes
are bodies with or without a shape, so the engine still logs the cap). The
numbers it found on the Mac are in `CLAUDE.md` under Rendering.

## The economy lab — `economy_lab.tscn`

Not a terrain scene: an editor of production webs (goods, recipes, tags,
consumers). Its manual and its data format are **`docs/economy-lab.md`**. From a
shell it takes seven runs, all on the scene itself:

```
godot --path . --headless scenes/dev/economy_lab.tscn -- selftest
godot --path . scenes/dev/economy_lab.tscn -- shot web=starter select=r.golem zoom=1 out=/tmp/lab.png
godot --path . --headless scenes/dev/economy_lab.tscn -- bake
godot --path . --headless scenes/dev/economy_lab.tscn -- census web=variety-ledger
godot --path . --headless scenes/dev/economy_lab.tscn -- balance web=hearth
godot --path . --headless scenes/dev/economy_lab.tscn -- sprites web=variety-ledger out=/tmp/sprite_review
godot --path . scenes/dev/economy_lab.tscn -- bench web=full-ledger-reference
```

`selftest` copies `resources/economy/` to a scratch folder under `user://`,
makes there the changes a hand would make (through the handlers the mouse
calls), checks that the web, the canvas and undo agree after each, and exits 1
if any check failed; it is the lab's regression gate and takes a few seconds.
`shot` is windowed, saves a PNG and quits, and never writes to the data. `bake`
arranges every web that has no layout and rewrites every file through the lab's
writer. `census` counts a web's varieties and the stacks they fall into, and
what the count would be with every slot passing variety on. `balance` prints a
web's balance sheet: what flows where in a day given the extractions' limits,
the recipes' amounts and times, and the people's wants. `sprites` writes
contact sheets of a web's icons and their varieties at four times the size, and
reports what is illegible. `bench` is windowed
and measures the lab on the machine it runs on (frame times over the big web,
the cost of a selection and of a change), writing a table to
`user://economy_bench.txt`; it is how a machine where the lab feels slow reports.
