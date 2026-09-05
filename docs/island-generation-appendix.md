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
and somewhere to build (a district); and water means lakes, rivers, and, because
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

### The fit pass clamps a scaled lobe by its own reach

The split family (Halves, Quarters, Twins, Harmony) lays out at a third to a
half of the grid, so the fit pass always scales it up, and `ScaleLobes` used to
clamp every scaled centre by the radius plus three cells on both axes. For a
lobe stretched across the axis it is offset along, that pad is its long axis,
so at 64² the two halves of a Halves were pinned nearly on top of each other;
two lobes on top of each other have no seam for the strait to follow, and the
cut shredded both into slivers (ten of sixteen seeds at 64² came out in three
to five pieces, and 48² was worse). The fit pass now pads by the ellipse's
reach on each axis, or the radius if that is less. Two things were tried and
dropped on the way. Using the same pad at placement re-laid every arm shape,
because the arm layouts lean on the circular pad for where their arms sit
(BrokenT collapsed at 64², BrokenL came out in eight pieces). And a pad that
could be *stricter* than the old one on a lobe's long axis pinned a scaled arm
onto its hub, with the same shredding: hence "or the radius, whichever is
less". Trimming a lobe's radius instead of moving it was tried too, and
shredded BrokenL and Twins outright. After the fix Halves, Quarters and Twins
are their named count of pieces in sixteen of sixteen seeds at every
footprint; 75 of 442 checksum islands moved, nearly all Twins, Satellites and
the default seeds that go through the scaling.

### Ground inside two lobes is never cut

The blocks came out of the gallery with a scatter of one-to-three-cell pits in
the middle, at 128² a dozen of them where 64² had one or two blobs, and Arc grew
crumbs along its inner coast. Two causes, both fixed. The shape noise had a
fixed frequency per cell while the lobes scale with the footprint, so a 128²
lobe held twice the periods of a 64² one and its cut broke into twice the
pieces; the noise is now island-relative, normalised to 64² (which it leaves
bit-identical), as its warp already was. That evened the sizes out and fixed
nothing else: the pits were still there at 64². The cut ranks each lobe's cells
by the noise and drops the lowest `1 − Coverage` of them, which on a coastal
lobe shapes the coast; a block's hub is all interior, so its share had nowhere
to land but the middle. Now a cell inside two lobes' discs at once is land
whatever the noise says — interior by construction — and the cut only ever
shapes an outer coast. 378 of 442 checksum islands moved, since nearly every
layout overlaps somewhere, and the side effects were all in the right
direction: Star stopped dropping an arm (sixteen of sixteen whole at every
footprint, from ten or eleven), Rosette fused at its heart into one landmass
(the spec had been rewritten to promise pieces; it was rewritten back), Ring
and Caldera filled in, and Harmony's tails began to fuse. The `Solid` floors
that guard thin shapes against perforation are now belt and braces.

### Small tunes after the gallery

The L's hub sat on the centre, and its round edge poked into the bay between
the arms; on BrokenL the spur stood clear of both straits and read as a third
petal. The hub sits an eighth of a radius back toward the outer corner. Harmony
delivered three or four pieces against a spec of two because its comma tails,
0.15 of the radius, parted from their heads; at 0.21 it is two pieces in
fifteen of sixteen seeds at 128² and sixteen of sixteen at 96², with the S still
carved between them, and two to four at 64², where the tails are small in cells
whatever the ratio.

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

### Shelves gave way to districts

A shelf was a contiguous patch of level ground — every cell flat or at one
lone step — with the width of its largest inscribed square, and it was what
"somewhere to build" meant, what the Gate apron scored, and a lab view. It
never read as a place: a hillside of one-slab steps, walkable end to end, was
a scatter of shelves and non-shelves with nothing between them a player would
recognise, and the mechanic was disliked for it from the start. On 2026-09-05
it went. What replaced it is the thing the walk analysis already had: a
**district**, a walk area of twenty cells or more — walk-connected ground, no
works. The guarantee is now "a district on the heartland", the apron is the
largest district within four cells of the strip's head (capped at 400, so the
mainland does not outrank the edge; the old shelf areas ran to a few hundred),
and `WalkArea.Seat` was added so a walk area can say which reach area holds
it. Every island in the audit has one, which is the point: the guarantee is
now near-trivial, and settlement placement will want a stricter reading —
level ground *within* a district — when it exists. That reading belongs to the
settlement layer, not to the generator.

### The lee is a rain shadow, and the gorge keeps its damp

The moisture stage gave the lee up to 20 more ("the lee holds its damp"),
which is the wrong way round: the windward side takes the rain and the
sheltered side is the shadow. Reversed on 2026-09-05, and made a knob at the
same time — `Wind`, the tenth 0–1 knob, rolled per seed like the rest — since
"how much exposure moves things" is a property of a Domain and not of the
model. `2 × Wind` multiplies every modifier exposure drives: the rain shadow
(30 in the lee at the nominal 0.5), the milder lee (10) and the **gorge damp**
(70 × shelter × ruggedness), added in the same change so that a gorge floor
under its walls goes mossy while the plateau above it stays steppe. The
exposure byte is geometry and does not move, so the collage's field strip
holds across a wind sweep. In the `Knobs` sweep, from wind 0 to 1, flat lee
ground dries by 32, gorge floors wet by 41 and the lee warms by 15 while open
ground does not move. One thing the mean will not show: at any wind the flat
lee reads wetter than the flat open, because sheltered flat ground is valley
floor beside water and the water strip outweighs the shadow. The shadow is
real; the audit reads it in the sweep, not in the mean.

### The sun and the frost hollows

Warmth had a lapse and three small modifiers, none of which knew which way a
slope faced. Now a Domain rolls a sun as it rolls a wind: the effective
surface's downhill direction dotted with the way to the sun, over two slabs
per cell, gives −1 … 1, and a slope turned full to the sun is 8 warmer, one
turned away 8 colder, flat ground untouched — so the label still reads at the
knob (the audit puts sunny slopes at 141.6 and shaded at 133.5). Basins and
sinkhole pits are frost hollows, 8 colder than their rung. Both are touches:
the bands are 70 wide, and neither moves a whole landform across one.

### A lake that swallows a river

Every course reached the aether by construction, because the routing flood
passes straight through standing water and every lake got a spill. That was
also the one thing a terrain with no sea could never do: end a river. Now, on
about three islands in ten that have a river-fed lake, one lake — a basin's
for preference — is made a sink after the first accumulation: its cells lose
their downstream neighbour, the drainage is summed again, nothing is traced
past it and it finds no spill. The channel downstream of the old outflow
usually vanishes with it, which is the visible sign. The cost is one more
accumulate-and-trace on those islands. The audit found eight on sixty seeds,
each fed by about two channel cells, and every island still has a river that
reaches the rim.

### A delta is cut, not found

Nothing in the routing makes a river fork toward the rim — the flood's tree
converges. So a delta is built: from the axis cell four upstream of a
navigable mouth, an arm is walked off each side of the pair (sideways, forward,
forward, and round — cardinal all the way, since diagonals are not a channel)
until it meets the rim, over ground that never climbs and never drops more
than the free step, and is refused if it meets water, a bridgehead, an eyot or
the river itself. The arm is a stream with the pair cell as its head, and
`Descend` holds that head to the pair, since no `down` joins them. The dry
ground between the mouths is the fan, floodplain whatever the climate. Two
things kept the first version at zero deltas: the mouth cell has no partner
(`Widen` needs a downstream cell to find the side), so "an axis cell with a
mate" found no mouths at all — a mouth is an axis cell with nothing
downstream, and nothing else. And an arm that met the rim on its first
sideways step — the river running along the coast — made a one-cell notch
beside the pair; an arm now needs two cells forward before the rim. Seventeen
on sixty seeds, on twelve islands, with fans of about four cells: a navigable
river has to meet the rim over a plain for one to exist at all.

### Sea stacks are aether

The specks the islet filter dropped were simply gone. Now two or three of them
(under thirty cells, still wholly aether on the mask that ships, no land beside
them cardinally) are kept as `IslandData.SeaStacks` — an anchor list, not
land: nothing walks, builds, routes or flies through them, and one within a
cell of a hanging Gate's flight path is dropped once the Gates are placed. The
lab draws them as dark pillars so they can be seen at all. They are rarer
than the rule suggests: on most seeds the crop leaves no speck to keep, so the
audit found them on nine islands of sixty. If every Domain should have them,
they will have to be placed, not salvaged.

### Bog on the cold side, marsh on the warm, and the hot row's grass

The first cut put marsh in the temperate row and bog in the cold one, as
"past wet" cells. The second, later the same day, split them by warmth
instead: **bog** is water in excess on the cold-to-cool half (warmth under
140, moisture 190 or more, a noise field over 0.66) and **marsh** on the
warm-to-hot half (140 and over, moisture 230 or more — extreme, which takes a
high background and the water's strip both — within two cells of water, flat,
a noise field over 0.62). So there are more bogs than marshes, a marsh shares
the floodplain's ground on a hot Domain and takes a few percent of it, and a
warm temperate Domain gets no bog. The line at 140 rather than 150 is the
water's tempering: at a knob of 0.5 a wet bank is pulled to about 145, and at
150 the whole riverside of a temperate Domain went bog-side. At 0.6 the bog
was a fifth of the wet cool corners; 0.66 makes it a tenth. The hot row also
got its own wet cell, **verdure**, at moisture 200 or more, a higher bar than
grass because heat is the less forgiving side: at 0.75 moisture a hot Domain
is half savanna and a quarter verdure, at 1.0 it is verdure where it was all
savanna. And the cold row got **heath** between tundra and moorland, with a
**frigid** band under 85 that is tundra whatever the moisture: a knob of 0 is
mostly tundra, and the heath and the moor are a knob of 0.25.

### Hot water on a cold Domain

A frigid Domain had nothing livable on it but the bank the water tempered.
Now, on a Domain whose warmth knob is under 0.35, each spring has a chance
(40% at a knob of 0, nothing at 0.35) and each pool of standing water with no
watercourse through it and at most sixty cells a chance (35%) of running hot,
and a hot source adds 90 warmth at the source, decaying to 1/e over four cells
of the same walk cost the moisture uses. That is a meadow round a spring in
the tundra, which is what the extremes wanted. Eleven of the twenty cold
islands in the audit have some; the collages show them as orange water in
the cold columns.

### Marsh past grass, and rarer bogs

The temperate row had no cell for water in excess where the cold row had bog
and the hot row floodplain. Marsh is that cell: moisture 205 or more, fresh
water within two cells, flat ground (so it is low as well as near) and a noise
field over 0.62 — 0.4% of land over sixty seeds, which is "occasionally". Bog
was a third of cold wet ground; it now needs moisture past 190 and a noise
field over 0.7, and is a sixteenth of that corner and 0.7% of all land, from
2.1%. Tors — stone on plains and hills where a fine noise clears 0.87, about
one soft cell in a hundred — put building stone on a Domain with no rock
landform, material only.

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
| **Shelves** | Level-ground patches with an inscribed-square width, as the meaning of "somewhere to build" and the Gate apron. Never read as a place. Replaced by districts: walk-connected ground, no works (§B). |
| **The lee holds its damp** | Sheltered ground gained moisture. The lee is a rain shadow; it loses it now, by a knob. |
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

Measured on the accepted run of 2026-09-05 (60 seeds, 128², the ten knobs
rolled per seed); nothing here is diffed automatically. Older rows that
measured a fixed preset are kept where they still say something.

| | |
|---|---|
| surface, the sixty rolled seeds, after marsh, tors and the rarer bog | grass 22.1%, moorland 10.6%, meadow 9.7%, dust 8.4%, tundra 7.2%, savanna 6.6%, steppe 4.4%, floodplain 2.3%, bog 0.7% (from 2.1%), marsh 0.4%, stone 11.2% (from 9.5%: the tors, 6718 cells on all sixty islands), scree 5.7%, sand 5.3%, silt 4.7%, snow 0.8% |
| the climate corners after the change (`Climate`, 12 seeds each) | cold wet: moorland 69%, bog 6% (was 22%). Temperate balanced: meadow 61%, grass 12%, marsh 0.8%. Temperate wet: grass 73%, marsh 0.9%. The other corners within a point of before, stone a point higher everywhere (the tors) |
| the wind (`Knobs`, 6 seeds, wind 0 → 1) | flat lee moisture 188 → 156 against flat open 151 held; gorge floors 160 → 201; flat lee warmth 156 → 172 against open 160 held; marsh 0.62% → 0.55%, bog 0.74% → 0.72% |
| the sun and the hollows | warmth on slopes turned to the sun 141.6, turned away 133.5 (n≈18.8k each); basin floors 147.0 against an island median of 154 |
| rivers after the terminal lakes and deltas | 7876 river cells (from 7812), 3565 navigable, 778 falls; 8 lakes swallow a river on 8 islands, fed by 16 channel cells; 17 deltas on 12 islands, 70 cells of fan; 218 springs, none on a navigable cell; fords per 100 stream cells 11.2 on flat ground and 6.1 on broken; every island's rivers still reach the rim |
| districts | 455 on the heartland over 60 islands, every island with at least one; median 7 districts per island; the largest district median 2535 cells |
| the new bytes | water distance (walk cost) per-island mean 7–204, median 25; magick mean 106–141, median 130, and a range of 195–222 within one island (no plateaus) |
| sea stacks | 52 cells on 9 of 60 islands: the crop rarely leaves a speck to keep |
| the second climate grid, sixty rolled seeds | grass 21.5%, meadow 9.8%, tundra 8.2%, dust 8.4%, heath 4.5%, moorland 4.4%, steppe 4.4%, savanna 4.3%, verdure 2.4%, bog 2.1%, floodplain 2.0%, marsh 0.4%; hot water 109 cells on 11 islands, 11 of the 20 with a warmth knob under 0.35 |
| the twenty-five knob positions (`ClimateStats`, 30 seeds each, 128²) | the largest ground per tile, warmth across: at moisture 0 tundra 74%, tundra 65%, steppe 68%, dust 66%, sand 77%; at 0.5 tundra 68%, heath 55%, meadow 58%, savanna 32% with meadow 27%, sand 52%; at 1.0 tundra 50% with bog 13%, moorland 48% with grass 14% and bog 13%, grass 74%, grass 63% with floodplain 10%, verdure 64% with floodplain 10%. Stone 12–14%, scree 7%, sand 3% in every tile: the rock and the dunes, which the knobs do not move |
| what occurs together (`ClimateStats`, 500 rolled seeds, present = 20+ cells) | present at all: grass 67%, meadow 52%, bog 33%, floodplain 30%, moorland 28%, savanna 28%, marsh 27%, steppe 26%, heath 25%, tundra 23%, snow 21%, sand 20%, dust 14%, verdure 11%; stone 100%, scree 81%. Given tundra: heath 79%, moorland 84%, bog 58%, no hot ground. Given verdure: floodplain 100%, savanna 92%, marsh 75%. Given dust: savanna 91%, floodplain 87%. Given bog: grass 77%, moorland 62%, marsh 4%. Given marsh: grass 78%, floodplain 59%, savanna 52%, bog 5%. The cold row and the hot row never share an island |
| its corners (`Climate`, 12 seeds each) | cold dry: tundra 62%, heath 8%. Cold balanced: heath 60%, moorland 10%. Cold wet: moorland 63%, bog 9%. Cool wet (0.35): grass 64%, bog 9%. Temperate wet: grass 73%, marsh 0.8%. Warm wet (0.65): grass 72%, marsh 0.9%. Hot wet: savanna 43%, verdure 21%, floodplain 9%, marsh 1%. Frigid wet (0.05): tundra 38%, moorland 26% (the tempered banks), bog 9%. Snow end: tundra 69%, bog 2% |
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
| deltas are small, and rare | 17 on 60 seeds, fans of about four cells: a navigable river has to meet the rim over a plain. A longer arm walk or a wider fan would make more of each; more deltas need more navigable mouths on gentle coasts, which is the terrain's doing. |
| sea stacks on one island in seven | The islet filter usually has nothing under thirty cells to drop. Placing pillars deliberately off the rim would put them on every Domain; salvaging keeps them honest and rare. |
| 5 sealed gorge reaches | Misaligned rims, 4–19 cells, on which a deck fits but the banks disagree by three or more. Nothing is cut off, but a 19-cell reach with no deck is a real detour. A pass that re-levels the two rims at the least-misaligned cell would close it; it is the same class of surgery as `LevelBridgeheads` and worth doing deliberately. |

---

## E. Ideas not taken yet

1. **Settlement placement.** Everything it needs exists: districts, berths,
   roads, Gate aprons, the water-distance byte. It will want a stricter reading
   of level ground within a district than "walk-connected" — that is where the
   old shelf idea belongs, if anywhere. It is the first thing that would show whether the terrain rules
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
