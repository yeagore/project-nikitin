# Island Generation — appendix

The reasoning behind [island-generation.md](island-generation.md): why each
mechanism is the way it is, what was tried and removed, how the audit and the
checksum are used, the open gaps, and the ideas not yet taken. The spec says
what the rules are; this says why. The history of how they got there is in git.

---

## A. Requirements

From Notion → *Generation → Island Generation*: a Domain is a landmass or
archipelago floating in aether, seen from a strategy camera; terrain is mostly
flat with the occasional single-slab step, punctuated by cliffs that mean
something; cliffs are costs, not walls, so a player who builds reaches almost
all of the island; every Domain has somewhere to arrive (a Gate and its apron)
and somewhere to build (a shelf); and water means lakes, rivers, and, because
there is no sea, every watercourse pouring off the rim.

---

## B. Why it is built this way

### Elevation is not a smooth field that gets quantised

fBm rounded to slabs fails two ways: step sizes become an accident of the
gradient, so the ground is uniformly two-to-three slabs rugged (nothing freely
walkable, nothing a deliberate cliff), and under a radial envelope the contours
are rings, so snapping them makes concentric bands. So the island is a blanket
of regions, each with a landform and a rung, each generated under its own slope
limit, and the envelope only says where the high ground tends to be. That turns
"where are the cliffs?" into a decision.

### The plateau ladder is what makes a cliff mean something

A rung difference is a cliff. `Landforms.AssignPlateaus` therefore unions every
pair of neighbours a cliff is not allowed between into one rung group, and the
slope limiter reaches across that border. Blurring the amplitude field until
neighbours happened to meet narrows the gap without closing it; that is where
the last forbidden cliffs came from.

### Ties in the river flood break on noise

Terrain under a slope limit is mostly flats, so most of the priority flood is a
tie, and a first-in-first-out tie-break is a breadth-first search whose tree is
a fan of straight cardinal rays: a ruler, not a river. Ordering equal ground by
a smooth noise field bends the tree at the field's wavelength. The jitter is
strictly below one slab, so it can only reorder cells the terrain itself does
not separate. `riverStraight%` in the baseline is the measure.

### Sculpted landforms are a separate pass

Relief under a slope limit can only put a cliff at a patch border; a gully
wall, a tower, a terrace riser and a sinkhole are cliffs inside a patch. They
are cut into a surface the limiter has already settled and then exempted from
it, the mechanism a canyon uses. Two rules keep them honest: nothing is sculpted
on the outermost ring of its own patch, so every border stays bound; and every
cut is a fixed depth, because a tapering cut has a two-slab step somewhere along
it by construction.

### Lowering finished terrain: the taper rule

Beaches and valleys lower ground after the grammar has settled, and lowering
some cells and not others puts a step the size of the drop at the edge of the
set. `FieldOps.Taper` clamps each cell to one more than its lowest neighbour,
making the drop field 1-Lipschitz. That is necessary and not sufficient: it
bounds the change between neighbours, not the result, and two one-slab steps
add. So beaches run before the settle loop and let the limiter clean up, and the
valley pass runs its own ambiguous-step correction afterwards.

### The channel sinks with its valley

A bank already stands exactly one slab above the water. Lowering only the ground
beside a river therefore has nowhere to go, and the taper turns the profile
inside out into a moat two cells out. So the channel sinks one band deeper than
its own bank, and the caps then mean what they say: a cell may not sink past a
lake, nor more than the free step past ground that cannot come with it (a mesa
rim, a tower, a levelled bridgehead). `Descend` runs again afterwards, since a
channel that sank unevenly might climb. The `Valleys` knob acts per watercourse
(each drainage draws a rank; the knob slides a window across the ranks, tilted
by the course's own descent) because one reach for the whole Domain made every
river identically incised, which looks generated. The slider's range maps onto
what anyone actually chose: 1 is the most valley worth having, not a trench.
The `Knobs` sweep is how this was found, and how it is checked.

### A navigable river holds one level

Its two cells are one surface, and three passes could move one without the
other. `LevelPairs` brings the higher cell down to the lower; the valley cuts
the pair once, both cells taking the smaller want; and `FlattenReaches` makes a
barge river a stair of pools, dead level between falls, which is also what a
reach is to a ferry. `Settle` cycles these against `Descend` until all hold;
each only lowers, so it terminates.

### Water pours every way it plausibly can

A cell that is a fall at all throws a sheet off every aether edge beside it and
toward every neighbouring water a fall's depth below it, so a corner spills both
ways and the level partner of a navigable pair pours beside its axis. Sheets
land only on water or in aether, never on dry ground: a sheet onto dry land
would be a course the drainage never routed, so nothing new gets wet. The
lab's sub-fall cataract sheets are renderer-side only; the falls list stays the
falls, because `Traversal` cuts ferry bodies at falls.

### Wide rivers, and the idle ferries

A course turns navigable below its first real confluence (`NavigableShare`),
where a barge would in fact get in; tuned any stricter, a median island had one
short reach and read as having none. In the audited sample every body of water
can be bridged, so berth pruning keeps none and `berths` is 0 in the baseline:
the ferry machinery is intact and idle, and earns its keep on low-`Crossings`
Domains where a two-cell river is already past the span.

### Lakes that are not one big lake

Filling a patch's interior gave every island the same lake at a different size.
Where the pool is big enough to have an inside it rolls a shape, and every shape
is a subset of the pool the containment approved, so the dry rim is untouched
and fragmented islands are unaffected by construction. Total area at the top of
the slider is a fifth lower than a plain fill and the body count half again
higher, which is the trade the feature is.

### A slider that only changes a count saturates

`Lakes` once set one thing, the chance a flat patch floods; a patch beside one
that holds water stays dry, so past a point more patches just lost the draw. It
now sets three: the chance, the smallest patch worth flooding, and how far the
shore wanders in. The general rule: a parameter that drives only a count
saturates wherever the thing it counts has a spacing rule. Check both ends.

### Goo never mixes, three ways

Goo is placed only where no water stands within a king's move; the rivers' keep
mask covers goo's whole king's-move neighbourhood so no channel, widening or
braid can approach; and `Rivers.Route` treats goo as not-land, so no course
drains through a puddle and a goo body has no spill. `gooTouchesWater` counts
the failures and wants 0. `Sailable` and `Walkable` both refuse it. Geysers were
pure scenery with no rules and were binned; the hook (`Geyser`,
`IslandData.Geysers`, the lab's jets) stays for the biome layer to fill.

### The cube has a lid, and Gates hang inside it

A Size-cell Domain is at most Size slabs keel to peak: `BoundAltitude` caps the
mountain rise and the keel depth at their 128 share, so a small Domain is
proportionally lower, not a scale model in a shoebox (`altOverCap`, want 0). A
hanging portal juts off the rim toward a wall, so the Gates are the first thing
the walls bite: the hanging offset is five cells (four reads as a doorway just
off the step; ten put a fifth of the portals outside small grids), and
`Flyable` refuses any site whose portal would stand outside the grid, which
makes the box a law of placement rather than a hope (`gateOutOfBox`, want 0).

### The fit band wraps the linker

Half the layouts crouched in the middle of their Domain, and measuring the raw
mask missed it, because `LinkLandmasses` drags every unbridgeable stray inward
after the mask is drawn and shrinks every scattered layout. So the fit pass
wraps the whole mask stage, bites, islet filter and linker, and rebuilds scaled
up until the landmass covers 55–85% of the grid. Three footprints are supported
and audited (`Sizes`): 64², 96², 128². There were five until 2026-09-05; 48² was
dropped after a gallery of every shape at every size showed the split family
(Halves, Quarters, Twins, Harmony) wrecked there and Reef losing its barrier.
The layouts were clean; the damage was the fit pass, whose lobe clamp
(`ClampIntoFootprint`, a pad of the radius plus three cells) squeezes the
scaled-up lobes together until the strait cut eats the land, plus the widths
measured in cells (straits, the islet floor, the shape noise) that take a far
bigger bite of a small island. 72² went with the ladder it belonged to. The
intended size gate is `ArrangementPool` filtered by `Size`.

### Grouped lobes, and the seam between pieces

A comma of `Harmony` is a chain of lobes that must fuse while the S between the
commas is carved, and "a cutting layout carves every seam" would have shredded
it into beads. Lobes sharing a `Lobe.Group` keep their seams; a lobe's default
group of −1 is a piece of its own, so every earlier arrangement behaves to the
cell as before. The strait is measured between pieces (nearest distance per
group), not between nearest lobes: deep in an overlap both nearest lobes belong
to one comma, and no width can fix a cut drawn in the wrong place.

### A neck is carved, not placed

`Isthmus` was two heads with two thin lobes between them, and half its seeds
were a `Single` with a dent. The fit pass was the culprit: a long thin layout
covers a third of the grid, so it was scaled up by the cap, and `ScaleLobes`
grows every radius while `ClampIntoFootprint` holds the centres inside the
wall — so the heads grew into each other and swallowed the neck. Two fixes,
both kept. The heads lie broadside to the axis and are staggered across it, so
the layout fills its box and the fit pass has no cause to blow it up. And the
neck is a **waist**, a clearing like the lagoon: two bays either side of the
head-to-head line, each a wedge that is the neck's half-width at the middle
and flares toward the heads, cut whatever the lobes say. The neck is a neck on
every seed at every footprint now (`Gallery` shows sixteen at a time). A
block's hole is the same override the other way round: `Square` and `Rhomb`
sometimes clear a lagoon of a rolled size, a little off the centre, so the hole
is aether through the Domain and not a lake.

### Bites eat coastline, never a satellite

Only `Single` and `Satellites` take bites, and the guards protect the total, so
a bite could delete an islet whole and ship the layout short. Any region with a
cell off the largest landmass is exempt.

### The gorge tripwire

Rivers often run between two cliffs on purpose, and a gorge whose two rims are
misaligned is a wall you must walk the length of. The audit measures gorge
reaches with the exact rule the reach flood builds bridges with. It is
structurally benign: every pass that cuts a gorge lowers a slab at a time under
the taper, so the rims come off the same ground and rarely part by more than
`MaxBridgeRise`, and `DeckFits` reads a deep gorge as a chasm and gives the deck
the full span. `gorgeSealed` is in the baseline (5 as accepted) so a future pass
that shears rims apart will shout; see the open gaps.

### Print the empty bins

`Surfaces.Pick` once had a ternary with one answer, two moisture bands
returning the same material, and an unreached cell given exactly the threshold
value, and between them Heath (now moorland) was 0.0% of every island ever generated. Nothing
broke a guarantee. What found it was printing a share per category with `NEVER`
beside the empty ones, which the audit does for every enum the generator
assigns. A branch that never fires looks exactly like a branch that works.

### The effective surface, and the lapse per mountain

Anchors and habitat describe what a place looks like, so every geometric
question is asked of `EffectiveLevel`, the water where a column is flooded:
against the bed, every bank of a navigable river was a "cliff". Warmth's lapse
is measured on mountain cells alone, from each mountain's own foot, in shares
of the mountain cap at the footprint: nothing for the first 40%, the full loss
over the next 60%. Three models came before it. Normalising per island put snow
on the top fifth of every island, a flat one's highest hill included. A fixed
lapse from the cube's top fifth cleared the plateaus but reached no mountaintop
at temperate settings, because the keel pushes a centred island down. A ceiling
read off `PlateauLevels × CliffHeight + 2 × MesaHeight + 4` over the lowest
ground was right at 128² and nowhere else: the ceiling is 22 slabs at the
preset whatever the footprint, while the mountain cap is 15 slabs at 48² and 30
at 96², so below 96² no mountain could reach its own snow line, and a knob
about mesa height moved the snow on a Domain with no mesas. Reading the foot
off the terrain (`Relief.MountainFoot` again, on the finished surface) makes the
snow line a property of the mountain: the `Sizes` sweep now counts it, and every
mountainous island carries snow at every footprint. `FieldMaps` writes the
axes as PNGs so all of it can be looked at headless.

### The chills were the label

Warmth was 60 + 180 × the knob, then lost up to 15 to the wind and 12 to the
rim "fading over sixteen cells". Measured over sixty islands at 128², rim
distance is a median five cells and at most thirteen, and exposure a median
220 of 255, so neither modifier ever faded: they were a permanent 20-point
cooling, the per-island mean read 118–133 against a label of 150, and at
warmth 0 on dry open coast 60 − 15 − 12 = 33 was under the snow line of 35 —
the coldest setting froze the coast and not the peaks, the opposite of the
knob's own doc. Now the label is the open lowland: the lee gains up to 10, the
rim loses 6 over four cells, and the mean reads 153 at the knob's middle. The
grid's bands moved up to meet it (cold below 115, hot from 185, sand from 220,
floodplain from 170), which is why a knob of 0.85 is still hot country and not
a desert.

### Beaches are ground, and the sculpted rock is scree

A beach was sand, which on a cold Domain drew a yellow strand round tundra;
nothing washes a beach, so it is now whatever the climate grid says a slab
lower, and the anchor is unchanged. Badlands, karst and sinkholes were dust
before the grid was consulted, so a cold karst Domain was 10% dust beside 53%
tundra; they are scree, a rock, until the biome layer decides otherwise.

### The knobs roll

The nine 0–1 knobs sat at the preset for every seed, so sixty audited islands
were sixty shapes of one climate. `IslandParams.Auto` (any negative) makes
`Roster.ResolveKnobs` roll the knob from the seed over its whole range, the
preset leaves all nine on it, and the audit's default seeds now sample the
knob space — which is how the Dunes bridgehead turned up (below). A sweep
pins the knob it sweeps, and the checksum's knob cases pin theirs.

### A beach is one slab

The outer two cells of a gentle coast step down one slab, which is free-step
ground. A graduated two-slab beach spends the whole tolerance a landing strip
has, and hanging Gates fell to a quarter when it was tried. A beach is the
normal coast rather than a special one, so it is a weak anchor; berth placement
does not read it.

### Four hanging Gates, chosen as a set

Each Gate has to out-reach every other on both axes, so placing them one at a
time has the first move the line the next must beat until there is nowhere
left; the strip tolerance, the separation and the scoring were each suspected
and each measured to be innocent. The fix is structural: every edge offers its
best sixteen sites in score order and a depth-first search takes the first
combination where every pair agrees, leaving an edge empty rather than failing
the set. Two more things made the maximum request routine: the strip is built,
not found (levelled to its innermost cell once chosen, so the join to the island
does not move), and the portal is a single block rather than three cells and
twelve slabs, so a coast has to agree with itself over one cell rather than
three. What still cannot be done is a Gate on an edge the heartland has no coast
facing; only a very small quilt manages that.

### Gate parameters are hard requirements

`EntryGate`, `EntryEdge`, `ExitGates` and `ExitGate` are the only inputs set
from outside the Domain: the world-tree decides which edge you arrive on and
through what. Searching for each Gate in turn against rules that could refuse
kept trading them away; choosing the four sites before any has a role makes a
named edge simply the Entry and a named kind something applied to it, and all
four stay checked in `Unmet` so a Domain that genuinely cannot oblige is
re-rolled rather than shrugged at. `Mainland` and `Heartland` are re-anchored on
the Entry's apron, since the largest area can sit across a strait from the only
way in.

---

## C. Tried and removed

- **A road check that could not pass.** The audit flagged any road hop that was
  diagonal, but roads walk by king's moves, so every legal corner cut was
  counted and "a step on a road longer than one bridge" stood at 4110. The
  check is now: a diagonal hop must be one cell, a straight one within a
  bridge; it reads 0.
- **A bridgehead on dunes.** `Bridgeheads.FlattenPad` lowered only Plain and
  Hills ground, and a Dunes region is neither, so a crossing with one end on
  dunes was never levelled; the first rolled-knob audit found one at 7 slabs
  against 4. Dunes are one-slab ground like hills and are flattened the same.

| | |
|---|---|
| **Ramps** | A ramp cut into a cliff read as a fixture, and one per cliff made every escarpment the same. Replaced by passes: a saddle where one plateau sags to meet the next. |
| **Lake chains** | Neighbouring patches holding water at slightly different levels read as flooding. A patch beside one that holds water stays dry. Provisional: "not for now", not "wrong". |
| **`Fragmentation`, a float** | One number asked to mean both "how broken up" and "into how many pieces". Replaced by named `IslandArrangement`s. |
| **Damping the coastline noise on multi-blob layouts** | Made every multi-island arrangement a field of discs. Replaced by carving the strait along the seam, so the layout decides where the land is and the noise decides no coast is a circle. |
| **Craters and the `Volcanic` character** | Either messy or indistinguishable from a mesa-and-basin pair, and a large share of unreachable ground. The sculpt mechanism is the same one the others use, so a caldera can come back if the biome layer wants one. |
| **A two-cell-wide fall sheet** | Centred on one cell, half of it poured out of solid rock. Each cell of a navigable pair emits its own sheet. |
| **Overhangs anywhere with an 8-slab face** | A lip off a two-cell karst tower reads as a hole punched through it. Undercuts need backing. |
| **Streams fordable everywhere** | A watercourse that costs nothing to cross anywhere is a line on the map, and roads walked down the bed. The crossing is now a place. |
| **A berth wherever the domino fits** | Thousands per audit, nearly all on water you could walk round. Berths are pruned against a ferry-less reach flood. |
| **A pad bigger than the Domain** | Clamping a lobe's centre to `[r + 3, n − 1 − r − 3]` is an empty range once a lobe is wider than half the map, and `Math.Clamp` throws. The pad is capped at half the footprint. |
| **Arches over open aether** | Rock in a column the mask says is empty breaks every "has land ⇒ has a region" assumption. Arches span gorges and channels. |
| **`Spiral`** | Kept as one landmass it was a `Rosette` with more steps, at twice the generation time. |
| **`BrokenFractal`** | The snake parted into stepping stones played exactly like `Shards`: a cluster of like-sized pieces across narrow straits. Replaced by `Caldera`, which plays like nothing else — an inner island every approach to which crosses the moat. The enum keeps the gap at 20. |
| **Four small islands for `Quarters`** | Four lobes placed to nearly touch, then cropped to coverage each, were four islets in an empty field. `Halves` had it right: lobes overlapping deeply, so only the strait says the mass is parted. `Quarters` is now that, sliced twice. |
| **Thin strokes for the arcs and the N** | Lobes stretched 1.8–2.1 along the stroke were threads next to the cross's arms. The stretch is 1.45–1.55 now and the lobes fatter, with more of them along the whole arc, since fat lobes spaced by their length still part where the jitter and the coverage crop both go against a seam. |
| **A Domain-wide `FluidKind`** | `Water` / `Lava` / `Essence` as one dropdown was two `if` statements with nothing visible behind them. The fluid came back per column, as `IslandData.Fluid`, with `Goo` the first thing that behaves differently. |
| **Dropping the Gate edge band outright** | Removing "stay near your own edge" at the relaxed rung put most of the Domain behind the player as they arrived. The band widens and never disappears. |
| **A four-cell floor under Gate separation** | Not a relaxation of "keep your distance" but a repeal of it. The floor is a third of the footprint. |
| **Geysers as terrain** | Jets placed where the rock was; where a jet belongs is a fact about the biome. The hook stays empty. |

---

## D. The audit and the checksum

```
godot --path . --headless --quit-after 2 scenes/dev/generation_audit.tscn
godot --path . --headless scenes/dev/generation_checksum.tscn
```

The audit runs the real generator over 60 seeds and measures `IslandData`
directly, so it re-implements nothing and cannot drift; the numbers once quoted
from a stand-alone harness against substitute noise turned out optimistic. Its
opt-in sweeps are the `[Export]` flags of `GenerationAudit`, documented on the
properties, and every flag can be given on the command line after `--`
(`-- Knobs Portraits=<dir>`). The sweeps hold everything else at the preset and
vary one thing, which is what makes their columns comparable; the ordinary audit
rolls from `Auto`, where every request is trivially satisfied because nothing
was asked for.

**The baseline** (`docs/audit-baseline.json`) holds thirty headline numbers
from the last accepted run and every run prints what moved. It is a diff, not a
test: numbers are expected to move when the generator changes, and the point is
to see them move and decide whether you meant it.

**The checksum** (`docs/checksum-baseline.txt`) hashes every field of
`IslandData` for 442 islands across the parameter matrix. It is the bit-for-bit
gate: a change meant to leave generation alone reports `0 of 442 islands moved`;
a change meant to alter it re-baselines with `-- accept` and says so in its
commit. `docs/dev-scenes.md` has both scenes in detail.

### What the baseline does not carry

Measured on the accepted run of 2026-09-05 (60 seeds, 128², the nine knobs
rolled per seed); nothing here is diffed automatically. Older rows that
measured a fixed preset are kept where they still say something.

| | |
|---|---|
| surface, at the preset (moisture 0.45, warmth 0.5: temperate and balanced) | meadow 61.8%, grass 11.8%, stone 8.9%, scree 4.5%, sand 6.4%, silt 4.6%, snow 0.9%, steppe 0.6%, dust 0.5%. The whole cold row and the whole hot row are `NEVER` here because the preset is temperate and the lapse only bites above the plateau ceiling, where a mountain is stone and then snow: they are the other rows of the grid, below. Before rock was tied to rock landforms and tall faces stone was 10.6%, scree 8.0% |
| the plateau ceiling, seed 1220260150 as Single Tablelands at 72² | before, with the lapse starting at 30% of a 22-slab cap: warmth 0.5 and moisture 0.5 gave moorland 58%; the mesas were cold at every setting. After: meadow 58%, grass 18%, and no tundra or moorland at any warmth above 0.25. The same seed as Highlands keeps snow on its summits — 2% at 72², 6% at 128², at temperate |
| walking by king's moves | against four-way walking on the same islands: land on the mainland 40.8% → 42.8%, heartland 94.9% → 95.0%, roads that can simply be walked 45 → 50 of 121. Cutting corners joins a few scraps to their districts and lets a few roads round a cliff; nothing large moves |
| habitat, per-island means | moisture 32–254 (median 183), warmth 75–225 (median 153: the knob's middle reads at its label; it was 118–133 with the chills), rugged 25–213, exposure 134–245, rim distance 1–13 cells |
| surface, the sixty rolled seeds | grass 23.8%, meadow 10.5%, savanna 6.4%, moorland 9.4%, tundra 7.0%, steppe 4.1%, dust 8.3%, floodplain 2.3%, bog 2.1%, stone 9.5%, scree 5.8%, sand 5.1%, silt 5.0%, snow 0.8%: every row of the grid present, because every seed is its own climate |
| snow at every footprint (`Sizes`, 12 seeds each) | share of land under snow 0.8 / 0.7 / 0.5 / 0.4 / 1.0% at 48 / 64 / 72 / 96 / 128², and every island with a mountain carries some at every size (3 of 3, 3 of 3, 4 of 4, 3 of 3, 4 of 4). Under the parameter ceiling it was 128² only |
| the climate grid, re-tuned (`Climate`, 12 seeds each) | cold dry: tundra 62%, moorland 14%. Cold balanced: moorland 72%, bog 4%. Cold wet: moorland 56%, bog 22%. Temperate dry: steppe 62%, meadow 11%. Temperate balanced: meadow 62%, grass 15%. Temperate wet: grass 76%. Hot dry: dust 62%, savanna 11%, floodplain 5%. Hot balanced: savanna 66%, floodplain 11%. Hot wet: savanna 66%, floodplain 11%. Sand end: sand 63%, floodplain 11%, savanna 7%. Snow end: moorland 72%, bog 4%, snow 1.6%. Stone, scree and silt hold at 9 / 4 / 5.5% in all eleven |
| road hops no work explains | 0 of 121 roads; the old diagonal-counting check read 4110 |
| the climate grid (`Climate`, 6 seeds each) | cold dry: tundra 64%, moorland 13%. Cold balanced: moorland 72%, bog 4%. Cold wet: moorland 55%, bog 21%. Temperate dry: steppe 59%, meadow 10%, grass 4% (along the water). Temperate balanced: meadow 59%, grass 13%. Temperate wet: grass 72%. Hot dry: dust 58%, savanna 10%, floodplain 4% (along the water). Hot balanced: savanna 62%, floodplain 10%. Hot wet: savanna 55%, floodplain 11%, grass 8% (the tempered riverside). The sand end (warmth 1): sand 43%, savanna 27%, floodplain 10%. Every floodplain touches its water: the tempered bank used to turn to grass with the floodplain starting a cell behind it, until the floodplain got its own warmth line and stranded patches were wiped. The snow end (warmth 0): moorland 72%, snow 5% — the lowland stays liveable. Rock, silt, beaches and snow make up the rest of each |
| rugged by cells from fresh water | bank 87, then 95 · 99 · 98 · 98 · 96, seven cells and further 81. With water read as its surface rather than its bank the bank was 118 and the second cell 124: the shore read a slab rougher than its country |
| anchors | 34.5k coast (30% beached, one cell deep; it was 84% and two deep), 20.4k cliff brink (2.2k honest gorge rims), 20.0k cliff foot, 9.5k bank, 6.4k river bed, 6.6k lake bed, 121 summits, 390 overhang, 381 ford, 543 Gate landing, 0 quay |
| Gates | one Entry and one to three Exits on every island, none on a shared edge, off the heartland, outside the box, or not outermost on its own axis; every landing exactly 3 cells and level |
| roads | one per Exit; median one work; roughly a third can simply be walked |
| requests | `GateRequests` and `GateMatrix` report every edge, kind and count delivered on every seed, and four hanging Gates on every arrangement × character |

### Feasibility

`Feasibility` runs every arrangement against every character. `ThousandIsles`
is the hard one (the most attempts; a piece the linker cannot always nudge into
range; `BrokenFractal` was the other until it was removed); everything else
runs at one attempt with most of the island reachable.

### Open gaps

| | |
|---|---|
| two-slab steps at a riverbank or valley side | `twoSlabOffMountain` in the baseline: all where the ground the pass would have to cut is a landform, a bridgehead or standing water. The alternative is eating the landform. |
| a landmass adrift on the most broken layout | `ThousandIsles`. The guarantees still hold; it is one islet of thirty. |
| basins on a `Highlands` island | About nine islands in ten, not all: adjacency cannot always place one beside a massif. Accepted. |
| undersized patches on `ThousandIsles` and `Atoll` | The coast, not the merge rule, sets the patch size on a small islet. Accepted. |
| overhangs are not walkable | By design; span-as-node traversal is its own problem (§E). |
| `Halves` and `Triplets` fuse on one 128² seed in twelve | Ungrouped layouts, so not the seam bug; the re-roll absorbs it. Logged by `Strain`. |
| 5 sealed gorge reaches | Misaligned rims, 4–19 cells, on which a deck fits but the banks disagree by three or more. Nothing is cut off, but a 19-cell reach with no deck is a real detour. A pass that re-levels the two rims at the least-misaligned cell would close it; it is the same class of surgery as `LevelBridgeheads` and worth doing deliberately. |

---

## E. Ideas not taken yet

1. **Settlement placement.** Everything it needs exists: shelves, berths, roads,
   Gate aprons. It is the first thing that would show whether the terrain rules
   make good play rather than good pictures.
2. **A real cost model for works.** Every work costs one point today, so
   `Passage.Cost` means "how many projects". Pricing by span, climb and ferry
   distance turns it into a budget the settlement layer will want.
3. **The world-tree.** `EntryEdge` and `EntryGate` exist so a Domain can be
   generated to match the one that sent you. Missing is the layer above: which
   Domains exist, their characters and arrangements, and how difficulty moves
   with distance from home. The next system, not the next feature.
4. **Span-aware pathing**, which is what would make an overhang or an arch
   walkable, and what a natural-bridge shortcut needs to matter.
5. **Re-levelling a sealed gorge**, per the open gaps.
6. **Fjords.** Long narrow inlets cut into one landmass along a grain, the one
   obvious coastline the arrangements do not produce. A mask operation, so it
   belongs with `Footprint`.
7. **Size-gating the arrangement pool.** One filter on `ArrangementPool` by
   `Size`, to be wired when the ladder is chosen; `Strain` names the layouts.
8. **Plunge pools.** A small pool dug under a fall onto dry ground, fed by it.
   Standing water on ground the traversal already counted, so it wants the same
   guards as a lake and a re-run of the analysis. For water's content pass.

---

## F. Otherworldly terrain

Most of it is the biome layer's job: a Domain feels alien because of what is on
it, the light, what grows, what the rock and the water are made of, far more
than because of its geometry. The cheap wins are materials, features and light.
What terrain itself can do:

- **Already possible, not yet used:** a fluid that is not water (`Fluid` is per
  column; goo is the first; a glowing one wants a renderer first); arches and
  overhangs made walkable, so a natural bridge is a crossing you did not build;
  the keel, the most alien surface on the island and one nothing walks on, so
  hanging spires and roots cost nothing in play.
- **Worth building, in order of value per unit of work:** aether-carved terrain
  (scour from the wind direction the dune grain already gives, so the windward
  rim is bare and undercut and the lee is where soil survives); columnar forests
  (karst with a far lower threshold and one-cell towers, so the gaps are the
  country); floating fragments above the main island, reached by air only (the
  span model represents them, but their columns need region and landform
  planes, which is exactly what the arch work found dangerous); inverted
  watercourses; and, as a late set-piece rather than an option, a Domain whose
  gravity points elsewhere.
- **Not worth doing:** more Earth landforms for their own sake. A landform has
  to say something in the grammar, walkable, climbable, a wall, a floor, or it
  is a texture, and textures belong to the material layer.
