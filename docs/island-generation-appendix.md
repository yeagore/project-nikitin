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
up until the landmass covers 55–85% of the grid. Five footprints are supported
and audited (`Sizes`); 48² is the strained one, carrying constants tuned for
more room, and the multi-piece ring and split family is what re-rolls there
(`Strain` names them). The intended size gate is `ArrangementPool` filtered by
`Size`, once the ladder is chosen.

### Grouped lobes, and the seam between pieces

A comma of `Harmony` is a chain of lobes that must fuse while the S between the
commas is carved, and "a cutting layout carves every seam" would have shredded
it into beads. Lobes sharing a `Lobe.Group` keep their seams; a lobe's default
group of −1 is a piece of its own, so every earlier arrangement behaves to the
cell as before. The strait is measured between pieces (nearest distance per
group), not between nearest lobes: deep in an overlap both nearest lobes belong
to one comma, and no width can fix a cut drawn in the wrong place.

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

### The effective surface, and the fixed lapse

Anchors and habitat describe what a place looks like, so every geometric
question is asked of `EffectiveLevel`, the water where a column is flooded:
against the bed, every bank of a navigable river was a "cliff". Warmth uses a
fixed lapse per slab anchored to the tallest a mountain can stand at the
footprint, because normalising per island put snow on the top fifth of every
island, a flat one's highest hill included. `FieldMaps` writes the axes as PNGs
so both can be looked at headless.

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
`IslandData` for 448 islands across the parameter matrix. It is the bit-for-bit
gate: a change meant to leave generation alone reports `0 of 448 islands moved`;
a change meant to alter it re-baselines with `-- accept` and says so in its
commit. `docs/dev-scenes.md` has both scenes in detail.

### What the baseline does not carry

Measured on the accepted run of 2026-09-02 (60 seeds, 128²); nothing here is
diffed automatically.

| | |
|---|---|
| surface, at the preset (moisture 0.45, warmth 0.95) | stone 9.6%, scree 4.7%, snow 0.8%, sand 6.4%, silt 7.8%, floodplain 7.3%, grass 5.5%, meadow 53.4%, moorland 3.8%, dust 0.6%, peatland 0.1%; nothing `NEVER`. The preset is a temperate, middling-wet country; the climate corners below are what the knobs do. Before rock was tied to rock landforms and tall faces it was stone 10.6%, scree 8.0% |
| habitat, per-island means | moisture 122–173, warmth 186–209 (the lapse only bites at the top now), rugged 27–154, exposure 171–247, rim distance 1–13 cells |
| climate corners (`Climate`, 8 seeds each) | dry cold: moorland 67%, grass 1%, peatland 1%. Dry warm: dust 42%, moorland 16%, meadow 9%, grass 1%, floodplain 1%. Wet cold: moorland 35%, grass 14%, meadow 12%, peatland 3%. Wet warm: meadow 41%, grass 17%, floodplain 10%. Sand, silt, stone, scree and snow make up the rest of each |
| rugged by cells from fresh water | bank 87, then 95 · 99 · 98 · 98 · 96, seven cells and further 81. With water read as its surface rather than its bank the bank was 118 and the second cell 124: the shore read a slab rougher than its country |
| anchors | 34.5k coast (30% beached, one cell deep; it was 84% and two deep), 20.4k cliff brink (2.2k honest gorge rims), 20.0k cliff foot, 9.5k bank, 6.4k river bed, 6.6k lake bed, 121 summits, 390 overhang, 381 ford, 543 Gate landing, 0 quay |
| Gates | one Entry and one to three Exits on every island, none on a shared edge, off the heartland, outside the box, or not outermost on its own axis; every landing exactly 3 cells and level |
| roads | one per Exit; median one work; roughly a third can simply be walked |
| requests | `GateRequests` and `GateMatrix` report every edge, kind and count delivered on every seed, and four hanging Gates on every arrangement × character |

### Feasibility

`Feasibility` runs every arrangement against every character. `ThousandIsles`
and `BrokenFractal` are the hard ones (the most attempts; a piece the linker
cannot always nudge into range); everything else runs at one attempt with most
of the island reachable.

### Open gaps

| | |
|---|---|
| two-slab steps at a riverbank or valley side | `twoSlabOffMountain` in the baseline: all where the ground the pass would have to cut is a landform, a bridgehead or standing water. The alternative is eating the landform. |
| a landmass adrift on the two most broken layouts | `BrokenFractal` and `ThousandIsles`. The guarantees still hold; it is one islet of thirty. |
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
