# Island Generation — appendix

The long form behind [island-generation.md](island-generation.md): why it is
built this way, what was tried and removed, what the audit measures and what it
currently says, and the ideas that are logged rather than done.

---

## A. Requirements

The original checklist, from Notion → *Generation → Island Generation*:

1. A Domain is a landmass or archipelago floating in aether, seen from a
   strategy camera. Working footprint **128 × 128** cells.
2. Terrain is **mostly flat with an occasional single-slab step** — ground you
   can lay a settlement out on — punctuated by cliffs that mean something.
3. Cliffs are **costs, not walls**: a player who builds should be able to reach
   almost all of the island.
4. There must be somewhere to arrive (a Gate and its apron) and somewhere to
   build (a shelf) on every Domain.
5. Water: lakes, rivers, and — because there is no sea — **every watercourse
   ends by pouring off the rim**.

---

## B. Why it is built this way

### Elevation is not a smooth field that gets quantised

The obvious approach is fBm → round to slabs. It was tried and it fails in two
specific ways, both observed:

- **Step sizes become an accident of the gradient.** Terrain comes out uniformly
  two-to-three slabs rugged, which is the worst case: a one-slab step is free and
  anything more needs infrastructure, so *nothing* is freely walkable and nothing
  reads as a deliberate cliff.
- **Under a radial envelope the contours are rings**, so snapping them to levels
  produces visible concentric banding with flat nothing between.

So the island is a blanket of **regions**, each with a landform and a rung, each
generated under its own slope limit; the envelope only says where the high ground
tends to be. That is what turns "where are the cliffs?" into a decision.

### The plateau ladder is what makes a cliff mean something

A rung difference *is* a cliff. `AssignPlateaus` therefore enforces the cliff
rule by construction: any pair of neighbours a cliff is **not** allowed between
is unioned into one rung group, and the slope limiter then reaches across that
border. Everything else — two rung groups, a mesa border, a mountain flank — is a
cliff somebody asked for.

The alternative, blurring the amplitude field until neighbouring patches happened
to meet, narrows the gap without closing it, and that is where the last handful
of forbidden cliffs were coming from.

### Ties in the river flood break on noise

The routing is a priority flood inward from the rim. Terrain under a slope limit
is mostly flats, so **most of the flood is a tie** — and a first-in-first-out
tie-break makes the flood a plain breadth-first search, whose tree is a fan of
straight cardinal rays. Every course traced down it came out as long straight runs
meeting at right angles: a ruler, not a river.

Ordering equal ground by a smooth noise field instead makes the front advance
along that field's low ground, so the tree bends at the field's wavelength (about
fourteen cells). The jitter is strictly below one slab, so it can only reorder
cells the terrain itself does not separate.

Measured: **60% of a course runs straight and 40% turns**; longest straight run,
median 4 cells, max 18. (A perfectly meandering river would not be 50% — a river
does hold a line for a while; what it does not do is hold one for twenty cells.)

### Sculpted landforms, and why they are a separate pass

Relief under a slope limit can only put a cliff at a patch *border*. A gully
wall, a tower side, a terrace riser and a sinkhole are cliffs **inside** a patch,
so they cannot come from relief at all. They are cut into a surface the limiter
has already settled and then exempted from it — which is exactly the mechanism
canyons already used for "a cliff somebody asked for".

Two rules keep them honest, and both are load-bearing:

- **Nothing is sculpted on the outermost ring of its own patch.** That ring is
  the patch's word to its neighbours, so every border stays bound by the limiter
  and the cliff rule holds at every edge a sculpted patch has.
- **Every cut is a fixed depth, never a taper.** A tapering gully has a two-slab
  step somewhere along its length by construction, and two slabs is the one
  height the grammar forbids everywhere.

### Lowering finished terrain: the taper rule

Two passes lower ground after the grammar has settled — beaches and river
valleys — and both hit the same trap. Lowering some cells and not others puts a
step at the edge of the set you lowered, and that step is the depth of the drop.
Lowering in bands does not help: a cell excluded for its own reasons (a mesa rim,
a bridgehead, a channel) sits at drop 0 beside a neighbour at drop 3.

`FieldOps.Taper` clamps each cell to one more than its lowest neighbour, making
the drop field 1-Lipschitz. That is necessary and **not sufficient** — it bounds
the *change* between neighbours, not the *result*, and two one-slab steps add. So
beaches run *before* the settle loop and let the limiter clean up behind them,
and the valley pass runs its own ambiguous-step correction afterwards.

Measured when this was got wrong: two-slab steps went from 0.5% of adjacent pairs
to **6.2%**, and the fix took it back to 0.5%.

### The valley that was a moat

The same trap, caught much later and from the other side. `CutValleys` lowered
the ground beside a watercourse and **held the channel where it was** — and that
cannot make a valley, because the bank already stands exactly one slab above the
water and so has nowhere to go. The "never sink into standing water" guard
therefore pinned the innermost band at drop 0; `Taper` read that zero as a
constraint and clamped each band to one more than the band inside it; and the
profile came out **inverted** — a ditch two or three cells out from the river,
with the ground rising back toward the water on both sides.

It is a good example of a bug a summary cannot show. Every guarantee held, every
step was legal, nothing was unreachable, and the number the audit prints for
rivers looked fine. Swept, it was unmistakable: *slabs the ground gains walking
from one cell off a river to five*, against `Valleys`, was 0.12 / 0.12 / 0.14 /
0.10 / **−0.06** — the whole range of the slider worth nothing, and its top worth
less than nothing.

The fix is one sentence: **the channel sinks with its valley**, one band deeper
than the bank beside it. Then the caps mean what they say — a cell may not sink
past a *lake*, and may not sink more than the free step past ground that cannot
come with it (a mesa rim, a karst tower, a levelled bridgehead), which is what
stops the pass opening cliffs it did not intend. Because the taper only ever
reduces a cell, and reduces it to one more than its smallest neighbour, the
outward profile survives it. `Descend` runs a second time afterwards, since a
channel that sank unevenly is a channel that might climb.

Same sweep after: 0.10 / 1.30 / 1.33 / 2.27 / **2.45**. The cost is real and
worth stating — a valley you must cross is an obstacle, so the share of land on
one walkable piece falls from 36% to 31%, while the share reachable *once you
build* is unchanged at 95%. That is a valley doing its job.

**Then it was all-or-nothing, which is its own fault.** One reach for the whole
Domain meant every river on an island had the same valley or none did — and a
country where every watercourse is identically incised looks as generated as one
where none is. `Valleys` now acts **per watercourse**: each 4-connected component
of the channel network draws a rank and keeps it, and the knob slides a window
across those ranks (`3 × strength − 2 × rank`, clamped). Courses whose window
lands at zero keep their bare incision.

Swept, counting per river rather than per island — how many courses gain a slab
or more walking five cells out from the water, and how deep the deepest gets:

| `Valleys` | mean rise | courses with a valley | deepest |
|---|---|---|---|
| 0.00 | 0.17 | 17 / 71 | 4.3 |
| 0.25 | 0.52 | 26 / 71 | 4.3 |
| 0.50 | 1.15 | 39 / 71 | 5.9 |
| 0.75 | 1.72 | 48 / 71 | 5.9 |
| 1.00 | 1.85 | 48 / 71 | 5.9 |

The row at 0.00 is the control and not a zero: a river runs in low ground anyway,
so some courses clear the bar on natural relief alone. The 23 courses that never
gain one are where the ground beside them is a landform, a bridgehead or a lake —
the things a valley may not cut. Two-slab steps off mountains went *down* with
this change, 1,069 → 931, because a valley that only some rivers get is a valley
that meets less of the terrain it is forbidden to touch.

**And then the ranks were tilted by relief** (2026-09-01): a course's descent —
head water to rim water — now shifts its draw by up to ±0.35 of the window, so a
river that came down through hills takes a valley early on the slider and a river
crossing a plain takes one late or never. The clamp keeps the ends of the slider
exact: 0 still cuts nothing, 1 still cuts every course in full. Swept at 0.25 /
0.50, courses with a valley went 26 → 21 and 39 → 30 of 71, and the mean rise
0.52 → 0.29 and 1.15 → 0.80 — the middle of the knob got choosier, and what it
now chooses is the uneven country.

### The river with one side higher than the other

A navigable river is two cells widened into one surface, and three passes could
move one cell of the pair without the other. `CutValleys`' caps are per-cell — a
lake or a pinned landform beside the left cell holds it back while the right cell
sinks free. `Descend` walks a cell's *chain*, and the partner's chain is not the
axis's. And on gentle ground the pair straddled the course's one-slab steps, so
the wide surface broke into shingles. Measured, the audit's uphill check — a
higher-flow river cell standing two or more above a neighbour — found the
leftovers: 2 pairs across 60 islands, each a stretch of water with one side
standing over the other.

Three corrections, together in `Rivers.Settle`:

- **`LevelPairs`**: a pair holds one water level, the higher cell coming down to
  the lower, bed and all. Run against `Descend` until both hold at once — each
  only ever lowers, so the loop terminates, in practice inside two passes.
- **The valley cuts the pair once**: after the taper, both cells take the
  smaller of their two wants. Reductions only, so nothing the taper settled is
  disturbed.
- **`FlattenReaches`**: a barge river is now a *stair of pools* — walked from
  the rim upstream, each cell held to the pool below it until the ground has
  risen `FallDepth`, and that step kept, as a fall. Dead level between falls,
  which is also what a reach *is* to a ferry. Streams keep their one-slab
  rapids; the flattening is the navigable river's alone, and it costs the bed at
  most two extra slabs at the held end of a reach, where the banks now read as a
  low gorge.

`riverUphill` went 2 → 0, and two-slab steps off mountains 948 → 830 — the
unequal valley sinks were most of both. The cost: mainland share 39.8% → 39.0%
and two of 121 roads stopped being free walks, which is gorge banks doing what
gorge banks do. Heartland share, berths, quay heights and every Gate guarantee
did not move.

### Water pours every way it plausibly can

`FindFalls` used to give each river cell at most one fall, in the first direction
found — so a river reaching a corner of the island poured off one aether edge and
ignored the other, and the levelled partner of a navigable pair (whose own chain
runs flat) drew nothing while its axis fell three slabs beside it. Now every
river cell throws a sheet off *every* aether edge beside it, and toward every
neighbouring **water** a `FallDepth` or more below it; a lake does the same where
its outflow channel leaves well under its surface. Falls went 544 → 584, of which
363 off the rim (was 282).

The restriction is the point: sheets land only on water or in aether, never on
dry ground. A sheet onto dry land would be a course the drainage never routed —
either it floods ground no channel was cut through, or the water visibly
vanishes into soil. Nothing new gets wet, so the extra falls cannot flood
anything, and the one audit case of "uphill" water that remained turned out to be
a drawn waterfall: a mainstem pouring three slabs sideways into the stream beside
it, which the uphill heuristic now excuses because a fall is proof of which way
the water goes.

Two rendering faults fixed with this, both in the lab: the sheet used to stand
*exactly* in the plane of the cliff face under the lip and z-fought it (now
pushed a whisker past — 0.53 of a cell instead of 0.50), and the falls popped in
and out as the camera swung — the automatic bounds of a flat quad are paper-thin,
so the multimesh now sets its own box, and the falls draw at a fixed priority
after the water sheet instead of tying with it on distance-to-origin.

**And the cataracts** (2026-09-01): a one- or two-slab step along a course is
a rapid the generator rightly does not call a fall — and the picture had a hole
there, two sheets of surface and a gap, which read as falls "sometimes just not
appearing". The lab now draws a small connecting sheet across every sub-fall
step between adjacent flooded cells, renderer-side only: the falls list stays
the falls, because `Traversal` cuts ferry bodies at falls and a rapid is not a
thing a barge cannot pass… downstream, at least; the routing already one-ways
what matters.

### The wide rivers that were not there

"A surprising lack of wide rivers" turned out to be tuning, not the pair fixes:
navigable cells were 796 before and after those, to the cell. The culprit was
`NavigableShare` — tuned in an era of outflow inflation so that it took *three*
courses meeting to make a reach navigable, which at the preset's `Rivers = 0.5`
left a median island 17 navigable cells: one short reach, read from the lab as
none. Now a course turns navigable below its first real confluence
(`NavigableShare` 0.16 → 0.11, the confluence floor `riverAt × 3` → `× 2`):
navigable cells 796 → 1,980 over 60 islands, 39.6 per island at the preset in
the sweep.

A side effect worth naming: the one island in sixty whose water a bridge could
not span resampled away, so the berth count is 0 across the audit — the domino
rule still finds 4,600 sites, and the pruning correctly keeps none, because
every island in this sample can be crossed without a ferry. Ferries earn their
keep on low-`Crossings` Domains, where a two-cell river is already past the
span; the machinery is intact and idle.

### The Valleys slider, rescaled

Everything anyone actually chose lived below a half, and the top half cut
trenches. The whole range now maps onto the old lower half (`strength × 0.5`,
with 0 still exactly nothing): 1.0 means "the most valley worth having", steep
courses in full and plain ones shallow, per the relief tilt. The consequence to
know about: the *bottom* half of the new range is correspondingly subtle —
swept, the rise over control at the new 0.5 is +0.08 slabs — because the old
0–0.25 always was.

### Lakes that are not Just One Big Lake

A lake was the patch's interior, filled — every island the same lake at a
different size. Where the pool is big enough to have an inside (40+ cells), it
now rolls a shape: single (still the plurality), a **thousand-lakes** scatter on
a chunky noise field, a **ring** round a dry island of its own floor (the
pool's inset ≤ 2), a **crescent** (the ring's core stamped out again
off-centre — the overlap is the bite), a ragged **cross** (two bars through the
centroid), or a **tarn** cropped small. Every shape is a subset of the pool the
containment approved, so the dry ring holding the water in is untouched, and
`RaiseSunkenShores` lifts what a shape leaves dry exactly as it lifts a
wandering shoreline. Two dials turned with it: a large interior lifts the lake
chance by up to half (broad country holds more water), and a patch that loses
the main roll can still take a small tarn. Fragmented islands are unaffected by
construction — their pools fail the same size floor the shapes need.

Measured: lake regions 93 → 173 over 60 islands, distinct bodies median 24
cells; at the preset's `Lakes = 0.5` the sweep's lake cells went 124 → 150. At
the slider's top the total *area* is a fifth lower than it was — the shapes
spend cells on being shapes — while the body count is up by half, which is the
trade the feature is.

### The other fluid, and the geysers

`FluidKind` came back upside down — see the spec. What is worth keeping here is
the shape of the "never mixes" guarantee, because it is belt and braces by
design: goo is *placed* only where no water stands within a king's move (the
patch's own dry interior guarantees it; a cell guard enforces it anyway), the
rivers' `keep` mask covers goo's whole king's-move neighbourhood so no channel,
widening or braid can approach, and `Rivers.Route` treats goo as not-land so no
course ever drains through a puddle and a goo body has no spill — goo makes no
rivers because the drainage has never heard of it. The audit counts water
within a king's move of goo and wants 0. `Traversal.Sailable` refuses it, so it
takes no berth and joins no ferry network; `Walkable` refuses it because it is
standing fluid without a ford. It is an obstacle the colour of a warning.

Geysers were the opposite trade — pure scenery, no rules — and were **binned
the same day** (Maxim looked; they did not turn out): a field of jets placed
where the rock was, and where a jet belongs is a fact about the *biome*, which
does not exist yet. What stays is the hook — `Geyser`, `IslandData.Geysers`,
and the lab's crossed-sheet rendering, all dormant — so the biome layer fills a
list rather than re-growing the plumbing. When water gets its content pass they
are the natural partner of plunge pools (§E.14) and the first candidate for an
eruption schedule.

### The gorge that cannot be bridged, measured

Maxim's worry, and a fair one: rivers often run between two cliffs — the
grammar makes gorges on purpose — and if the two rims were misaligned too
often, a gorge would be a wall you must walk the whole length of. The audit now
measures this with the exact rule the reach flood builds bridges with
(`Traversal.Walkable` endpoints, `DeckFits` over the gap, banks within
`MaxBridgeRise`), so what it reports is what the game would let you build. A
gorge cell is a river cell with dry ground 3+ slabs over its water on both
sides of an axis — looked for *through* the channel, since a navigable river's
rim stands beyond its partner — and a reach is a 4-connected run of them,
counted from three cells.

The answer is: **it does not happen.** Over 60 preset islands: 796 gorge cells
in 47 reaches (median 9 cells, longest 51) on 18 islands — **47 of 47
crossable**, 0 sealed, 0 refused for misalignment alone. The worst walk from
any gorge cell to its nearest deck site is 9 cells; the median is 0, meaning
most gorge cells can be bridged where you stand. Swept across `Crossings`
(12 seeds each): Easy 7 reaches / 0 sealed / worst walk 3, Medium 14 / 0 / 1,
Hard 13 / 0 / 1 — even on Easy Domains, where the span is one cell and
misalignment is the *only* thing that could seal a stream gorge, nothing does.

Why it is benign is structural, not luck: every pass that cuts a gorge —
valleys, banks, the flattened reaches — lowers a slab at a time under the
taper, so the two rims come off the same ground and rarely part by more than a
slab or two, which `MaxBridgeRise = 2` absorbs; and `DeckFits` reads a deep
gorge as a *chasm*, so the deck over it gets the full bridge span rather than
the three-cell water limit. The check stays in the audit as a tripwire: if a
future pass starts shearing rims apart, `gorgeSealed` is in the baseline and
will shout.

### Three dead branches nobody could see

`Surfaces.Pick` decides what the top of a column is made of. Three of its arms
were wrong in ways that compile, run, produce plausible islands, and are
invisible without a histogram:

```csharp
if (slope >= 3) return height > 0.72f ? Stone : Stone;   // both arms
if (damp <= Damp) return Grass;
if (damp <= Dry)  return Grass;                          // and again
int damp = wet[x, z] < 0 ? Dry : wet[x, z];              // never exceeds Dry
```

The first is a ternary with one answer. The second collapses three moisture bands
into two, so `Damp` was a constant that did nothing. The third is subtler and
worse: the wetness flood stops expanding at `Dry`, so no cell ever comes back
*above* it, and a cell the flood never reached was given exactly `Dry` — which
the last test reads as still-green. Between them, **`Heath` was 0.0% of every
island the generator had ever produced.** The driest ground on a Domain came out
the same colour as a water meadow.

None of this was findable by reading, and none of it broke a guarantee. What
found it was printing a share per material with `NEVER` beside the empty ones —
which the audit now does, and the lab's `surface` view now paints honestly.
Distribution after the fix: stone 11%, scree 15%, snow 13%, sand 21%, silt 5%,
grass 3%, heath 23%, dust 2%, meadow 8%.

### Beaches, measured

They work, and they are commoner than the name suggests: **81% of coast cells
step down onto a beach**, and no island in 60 lacks one. `MakeBeaches` drops the
outer two cells of any gentle coast — Plain, Hills or Dunes, dry, even within a
slab — by a single slab, which is free-step ground, and marks them. That single
slab is deliberate: a graduated two-slab beach spends the entire tolerance a
landing strip has, and when it was tried, hanging Gates fell to a quarter.

Two things follow that are worth knowing rather than fixing. A beach is the
*normal* coast rather than a special one, which makes it a weak anchor — 21% of
all land is classified `Sand`, most of it shoreline. And the doc comment claims a
beach "gives a quay somewhere natural to sit", which is not wired: `BuildBerths`
does not read `Beach` at all.

### A slider that only changes a count saturates

`Lakes` used to set one thing: the chance that a flat patch holds water. But a
patch beside one that already holds water stays dry (see *Lake chains*, below),
so raising the chance past a point only makes more patches lose that draw — the
count approaches a maximal independent set and stops. Swept, the top quarter of
the slider bought 10% more water and looked identical.

It now sets three things: the chance, the smallest patch worth flooding (40 cells
down to 12), and how far the shore wanders in from the patch rim, so a wet Domain
fills more of each patch as well as more patches. Lake cells per island across
the range: 0 / 47 / 124 / 288 / **390**, and the largest single lake 0 / 98 / 156
/ 409 / 449.

The general lesson is the one the `Knobs` sweep exists for: **a parameter that
drives only a count will saturate wherever the thing it counts has a spacing
rule.** Check what the slider does at both ends, not at the default.

### Four hanging Gates, and the rewrite that made them the default

Asked for the hardest thing there is — an Entry and three Exits, one per edge,
every one of them flown to — the greedy placer delivered on **25% of runs** across
all 176 arrangement × character combinations. It is now **100%**, at 1.00 attempts,
and every reduction from it (fewer Exits, land Gates at either end, a named entry
edge) is met on every seed. Getting there took finding out what was actually
wrong, and three plausible answers were not it:

| | |
|---|---|
| **Not the strip tolerance.** | Raising `StripTolerance` from 3 slabs to 5 changed the outcome by **nothing at all** — 25% and 41%, identical to the digit over 528 runs. |
| **Not the separation.** | Dropping `GateSeparation` back from 0.42 to 0.30 bought 25% → 27%. Real but small, and it is the rule that keeps two Links out of one bay. |
| **Not the coast.** | Counted with the other Gates removed, **every edge of every island** offered a hanging Gate — 4.0 of 4 edges, 130–160 candidate cells each, on all eight characters. |
| **Not the scoring.** | Weighting each Gate toward the middle of its own edge, on the theory that a Gate in a corner moves the line the next one has to beat, made it **worse**: three hanging Exits fell 41% → 33%. |

Two things were wrong, and both were structural.

**The Gates were placed greedily.** Each has to out-reach every other on *both*
axes, so the first one placed constrains the second, the second the third, and by
the fourth there is nowhere left — with the other Gates in place the same funnel
that showed 150 candidates per edge showed about 30, on one edge. Choosing the
four as a **set** is the fix: each edge offers its best sixteen sites in score
order and a depth-first search takes the first combination where every pair
agrees. It is a search over at most 16⁴ with heavy pruning and a node budget, so
it costs nothing measurable, and an edge with no workable candidate is left empty
rather than failing the whole assignment.

**The strip had to be found rather than built.** A 3 × 5 berth that already
agreed with itself to within three slabs left an island 14 to 20 viable cells
across all four edges; four Gates out of that was a coincidence. It was also the
wrong requirement in the first place — a Gate is a built structure and so is the
ground under it. The strip is now 1 × 3 and is **levelled** once chosen, to the
height of its innermost cell so the join to the island does not move. Gate
placement therefore became the one pass that both reads the traversal analysis and
changes the terrain, and `Place` returns whether it moved a slab so the analysis
can be run again.

Shrinking the portal from 3 × 12 to a single block is the third piece. A
three-cell portal needs three cells of footing, three of clear flight path and a
strip three across, and every one of those is a coast that has to agree with
itself over a wider span.

What it bought beyond the guarantee: the ground behind a Gate fell from a
worst case of 20% of the Domain to **6%**, the mainland share rose from 33.9% to
39.8%, and roads that can simply be walked went from 23 to 32 of about 120 —
levelled strips join up ground that used to need a step built onto it.

**What still cannot be done.** A Domain whose heartland has no coast facing one of
the four ways cannot have a Gate on that edge. On a 128² footprint this never
happens. At 64² it happens for `ThousandIsles` — sixteen islets on a small map —
on 2 of 176 combinations.

### Gate parameters are requests, and requests get re-rolled

`EntryGate`, `EntryEdge`, `ExitGates` and `ExitGate` are the only inputs set from
**outside** the Domain — the world-tree decides which edge you arrive on and what
kind of Gate you arrive through — so "usually" is not an answer for them.

Three separate things were quietly not honouring them, and all three were
symptoms of searching for each Gate in turn against a set of rules that could
refuse:

- **The Exit ladder stopped too early.** The tier ladder was the outer loop and
  broke at the first rung that produced *any* Exit, so a Domain asked for three
  got one whenever the strict rung only allowed one. Median Exits per island: 1.
- **A named kind was traded at the first refusal.** `ExitGate = Land` tried Land
  then immediately Hanging *at the same rung*, rather than holding Land down the
  whole ladder.
- **`EntryEdge` was not in `Unmet`.** The kind was checked, the edge was not, so
  the last-resort fallbacks fired and nothing objected.

The first two were fixed in the ladder and took the Entry edge to 100% and the
kind to 93–100%. The **rewrite** removed the question instead: the four sites are
chosen before any of them has a role, so a named edge simply *is* the Entry, a
named kind is applied to it, and `ExitGates` is a subtraction. All four checks
stayed in `Unmet`, and nothing now reaches them — measured over sixteen seeds per
request, every edge, kind and count is delivered on **100%** of seeds at a mean of
**1.00 attempts**. The lab's readout still says `COAST WOULD NOT` where a Domain
genuinely cannot oblige, which is now only a small island with a missing coast.

### The mainland is where you land

`Mainland` and `Heartland` used to be whichever area was largest. That answers a
different question, and it could name a mainland on the far side of a strait from
the only way in — making every number derived from it a number about somewhere
else. They are re-anchored on the Entry Gate's apron once the Gates are placed.

---

## C. Tried and removed

| | |
|---|---|
| **Ramps** | A generated ramp cut into a cliff. It read as a construction rather than as terrain, and one ramp per cliff made every escarpment the same. Replaced by **passes**: a saddle where one plateau sags to meet the next, which is a landform rather than a fixture. |
| **Lake chains** | Neighbouring patches each holding water at slightly different levels, joined by notched channels. It spread one sheet of water over more of the island and read as flooding. Now a patch beside one that holds water stays dry. Provisional — "not for now", not "wrong". |
| **`Fragmentation`, a float** | One number asked to mean both "how broken up" and "into how many pieces" and delivered neither. Replaced by named `IslandArrangement`s. |
| **Damping the coastline noise on multi-blob layouts** | It stopped `Twins` fusing and made every multi-island arrangement a field of discs. Replaced by **carving the strait** along the seam, so the layout decides where the land is and the noise decides that no coast is a circle. |
| **Craters, and the `Volcanic` character** | A ring wall round a sunken floor, breached on one side. It came out either messy or indistinguishable from a mesa-and-basin pair, and it was 16% of everything the player could not reach. Binned 2026-08-31; the sculpt mechanism it used is the same one the others use, so it can come back cheaply if the biome layer wants a caldera. |
| **A two-cell-wide fall sheet** | A navigable river's fall drawn as one sheet of width 2, centred on one cell — so half of it straddled the bank and looked like water pouring out of solid rock. Both cells of the pair emit their own one-cell sheet instead. |
| **Overhangs anywhere with an 8-slab face** | Karst towers, basin rims and sinkhole walls all qualify on height, and a lip off a two-cell tower reads as a hole punched through it. Undercuts now need **backing**. |
| **Streams fordable everywhere** | Which is the same as not being there: a watercourse that costs nothing to cross at any point on its length is a line drawn on the map. It also made roads walk *down* streams, since the bed was exactly as cheap as the bank. Now the crossing is a place. |
| **A berth wherever the domino fits** | Three thousand per audit, nearly all on water you could walk round. Berths are now pruned against a ferry-less reach flood: 97 survive, on 2 of 60 islands. |
| **A pad bigger than the Domain** | `PlaceLobes` kept every blob inside the footprint by clamping its centre to `[r + 3, n - 1 - r - 3]`, which is an empty range once a lobe is wider than half the map — so any `Size` small enough for the auto radius to fill it **crashed outright** (`Math.Clamp` throws when its minimum passes its maximum). At 64 cells the radius is 28.8, the pad 31.8 and the room 31.2. The pad is now capped at half the footprint, which puts an over-large lobe in the middle, where it belongs. Found by testing the Gate guarantee at a smaller `Size`. |
| **Arches over open aether** | An arch out into the void puts rock in a column the land mask says is empty — no region, no landform, no keel — and everything that reads "has land ⇒ has a region" is then wrong about it. (It crashed the audit.) Arches span gorges and channels, which is the commoner form anyway. |
| **`Spiral`** | A thin arm wound inward over two and a half turns. Keeping it one landmass took a coil thick enough and links dense enough that it came out as a `Rosette` with more steps — and cost twice the generation time doing it. Binned 2026-08-31. |
| **`FluidKind`** | A Domain-wide `Water` / `Lava` / `Essence`. What shipped was two `if` statements (no fords, no ferries) and a dropdown with nothing visible behind it; the whole idea is the *look*, and there was no renderer for it. Removed 2026-08-31, to come back with the thing that makes it mean something. |
| **Dropping the Gate edge band outright** | The relaxed placement rung used to remove the "stay near your own edge" test entirely rather than widening it. A Gate then only had to out-reach the other Gates, which put up to **73%** of the Domain behind the player as they arrived — at which point "the south Gate" names nothing. The band now widens to 45% and never disappears. |
| **A four-cell floor under Gate separation** | The last placement rung dropped the distance two Gates must keep to `Gate.Width + 1`, which is not a relaxation of "keep your distance" but a repeal of it. The floor is now a third of the footprint. |

---

## D. The audit

```bash
godot --path . --headless --quit-after 3 scenes/dev/generation_audit.tscn
```

`scenes/dev/generation_audit.tscn` runs the **real generator** over 60 seeds and
measures `IslandData` directly. It re-implements nothing, so there is nothing to
drift — the numbers in this file were originally produced by a stand-alone
harness against substitute noise, and when the real generator was finally
measured, several claims turned out optimistic.

Flags on the scene:

| flag | |
|---|---|
| `Silhouettes` | an ASCII map of one island per arrangement — is a `Ring` a ring? |
| `Waterways` | one island's water at full resolution — does a river bend? |
| `Sculpts` | a close-up height map of each sculpted landform — is a badlands a maze or one trench? |
| `Feasibility` | every arrangement × every character — see below |
| `GateRequests` | ask for each Entry edge and kind, and each Exit count and kind, and report what came out — the only parameters set from outside the Domain |
| `GateMatrix` | ask every arrangement × character for four hanging Gates — the maximum request — then check that asking for less works too. See above |
| `Knobs` | sweep `Lakes`, `Rivers` and `Valleys` from 0 to 1 and print what each one moves. A slider that does not change the island is worse than one that is not there, and a summary at one setting cannot tell you which it is |
| `AcceptBaseline` | write this run's headline numbers as the new accepted answer |

`GateRequests` and `Knobs` share `SweepSeeds` (12 by default). Both hold
everything else at the preset and vary one thing, which is what makes their
columns comparable — the ordinary audit rolls from `Auto`, where every parameter
is trivially satisfied because nothing was asked for.

### The baseline

`docs/audit-baseline.json` holds the last accepted headline numbers, and every
run prints what moved. It is a **diff, not a test**: every number is expected to
move when the generator changes, and the point is to see it move and decide
whether you meant it.

It exists because of a specific near-miss: when lake outflows were fixed, navigable
river cells fell from 1,642 to 146 and it was very nearly read past.

### What it currently says

| | |
|---|---|
| step grammar | **94.1% free**, 0.5% two-slab, 5.4% cliff, over 378k adjacent pairs |
| two-slab steps off mountains | 692 — riverbanks and valley sides the pass is not allowed to cut |
| cliffs between patches | plain-plain, plain-mesa, plain-basin, mesa-mesa — the pairs the rules allow |
| rivers | 5.1k cells on 60 of 60 islands, 1,980 navigable, ~630 falls, **0 running uphill** |
| how a course runs | 60% straight / 40% turning |
| lakes | ~125 on 56 of 60 (~156 distinct bodies, median 21 cells — the shapes), **0 leaks, 0 water touching the void** |
| goo | ~520 cells on 19 of 60, **0 within a king's move of water** (geysers are an empty hook — see *The other fluid*) |
| gorges | 47 walled reaches on 18 of 60 — **47 of 47 bridgeable, 0 sealed**, worst walk to a deck 9 cells |
| ferries | 0 berths of 4,621 sites — no island in this sample has water a bridge cannot span; the machinery is intact and idle (see *The wide rivers that were not there*) |
| surface | stone 11%, scree 15%, snow 13%, sand 21%, silt 5%, grass 3%, heath 23%, dust 2%, meadow 8% — none NEVER |
| anchors | 29k coast (81% of it beached), 35k cliff, 270 overhang, 342 ford, 543 gate landing |
| overhangs | ~270 columns with a second span |
| walk / reach | **39% mainland on foot, 94% heartland with building**, 51 of 60 islands one whole |
| what stays out of reach | mountain and karst tower — landforms whose point is the height |
| Gates | 1 entry and 1-3 exits on every island (median 2 exits); 0 on a shared edge, 0 off the heartland, 0 not outermost on their own axis |
| Gate landings | every one exactly 3 cells and dead level — **0 short or sloped** |
| Gates asked for | every edge, kind and count delivered on **100%** of seeds at 1.00 attempts; **four hanging Gates on 100%** of 176 arrangement x character combinations |
| roads | one per Exit on every island, **0 exits without one**; median 1 work |
| re-rolls | median 1 attempt, **0 seeds that never met the guarantees** |

### Feasibility: every arrangement against every character

`Feasibility` runs all 22 arrangements × 8 characters. The ordinary audit rolls
from `Auto`, so it measures the combinations the weights happen to produce — but
a Domain's biome and world-tree position will name both, and a combination that
takes four attempts is a bug nobody would ever see from the summary.

What it found, and what was done:

| combination | | |
|---|---|---|
| `Spiral` / anything | **removed.** First it came out as 13–21 separate islets — the per-blob coverage threshold perforates a thin arm — and the fix was a floor under `Coverage` (`Layout.Solid`) plus a coil stopping short of the centre. That got it to 1.0 masses, at the price of an arm thick enough and links dense enough that the result read as a `Rosette` with extra steps. Two names for one shape is worse than one shape, so it is gone; `Layout.Solid` stays, because `Fractal` needs it for the same reason. |
| `ThousandIsles` / `Tablelands`, `Badlands` | **known.** 1.5 attempts and ~78% reachable — fifteen islets, some of which the linker cannot nudge into range. It is the hardest layout by design. |
| `BrokenFractal` | **known.** 1.5 attempts; a chain of seven pieces has the same problem in miniature. |
| everything else | 1.0 attempts, 90–99% reachable, 65–250 ms |

It was also the slowest layout by a factor of two, because it placed 44–54 lobes
and the mask evaluates every lobe against every cell — which is what a layout
built out of a great many overlapping blobs costs.

### Open gaps

| | |
|---|---|
| two-slab steps at a riverbank or valley side | 830 in 378k pairs, all where the ground the pass would have to cut is a landform, a bridgehead or standing water. It rose from ~730 when valleys started working and fell back from 948 when pairs stopped sinking unevenly; the alternative is eating the landform. |
| ~~3 crossings with banks more than 2 slabs apart~~ | **closed.** It came in with the sculpted landforms and went out with the crater cull and the beach reordering: 0 of 149. |
| 3 gates in a corner of their own edge | The relaxed placement rungs firing where a coast will not take four Gates under the full rules. No pair is inside the separation floor any more. The alternative is a Domain with fewer Links, which is worse. |
| ~~2 river cells running uphill in 4,578~~ | **closed** by `Settle` (descend + pair-levelling run to a fixed point) — and the last case was a mainstem pouring a drawn fall into the stream beside it, which is water falling, not climbing. 0 of 4,506. |
| ~~3 hanging Exits delivered ~2/3 of the time~~ | **closed** by the set-wise placer and the built strip: 100% at every count, and 100% for four hanging Gates. |
| a landmass adrift on the two most broken layouts | `BrokenFractal` and `ThousandIsles`, per the feasibility table. The Stage 11 guarantees still hold, so it is one islet of fifteen rather than a broken island. |
| basins on a `Highlands` island | ~80% of islands, not 100% — adjacency cannot always place one beside a massif. *Accepted.* |
| undersized patches on `ThousandIsles` / `Atoll` | The coast, not the merge rule, sets the patch size on a small islet. *Accepted.* |
| overhangs are not walkable | Stage 6 runs after the analysis by design. Span-as-node traversal is its own problem. |
| feature anchors | `CoastCells`, `CliffCells` and `Overhangs` exist. What is missing is the layer that uses them. |

---

## E. Ideas not taken yet

Logged rather than done, so the reasoning survives the conversation it came out
of. The numbered ones that have since been **done** are marked.

1. ~~A biome / material layer~~ — **done** as `Surfaces.Classify`, at the ground
   level (stone / grass / sand / …). The *living* layer above it — what grows
   where — is still open, and is a Domain-level concern.
2. **Settlement placement.** Everything it needs now exists. It is the first
   thing that would expose whether the terrain rules produce good *play* rather
   than good pictures.
3. ~~Feature anchors~~ — **done**: `CoastCells`, `CliffCells`, `Overhangs`.
4. ~~Beaches~~ — **done**.
5. **A real cost model for works.** Every work costs 1 point today, which makes
   `Passage.Cost` mean "how many projects" — right as a first answer, but a
   six-cell bridge and a one-cell step are not the same project. Pricing by span,
   climb and ferry distance turns `Cost` into a budget, which the settlement
   layer will want the moment it has money.
6. ~~Valleys~~ — **done**, with a `Valleys` knob, because at any strength it
   starts eating the landform patchwork.
7. **The world-tree.** `EntryEdge` and `EntryGate` exist precisely so a Domain
   can be generated to match the one that sent you. What is missing is the layer
   above: which Domains exist, their characters and arrangements, and how
   difficulty moves with distance from home. The next *system*, not the next
   feature.
8. ~~Naming~~ — **done**, as scaffolding for the culture layer to replace.
9. ~~An audit baseline~~ — **done**.
10. ~~Screenshots from the lab~~ — **done**: **F2** writes a PNG.

Still open, added since:

11. **Span-aware pathing**, which is what would make an overhang or an arch
    walkable, and what a natural-bridge shortcut would need to matter.
12. **Moving a crossing that cannot be levelled**, per the open gaps above.
13. **Fjords.** Long narrow inlets cut into one landmass along a grain — the one
    obvious real-world coastline the arrangements do not produce. It is a mask
    operation (radial or parallel cuts inward from the rim) rather than a
    landform, so it belongs with Stage 1.
14. **Plunge pools.** When the falls learned to pour every plausible way, the
    sheets were restricted to landing on existing water so nothing floods — but
    the other road was to *dig* the landing: a small pool under a fall onto dry
    ground, fed by it, maybe spilling on. Maxim called a lake fed by waterfalls
    an aesthetically pleasant idea, and it is; what it costs is that a pool is
    standing water on ground the traversal already counted, so it wants the same
    guards as a lake (bridgeheads, landing strips, roads) and a re-run of the
    analysis. Worth doing when water gets its content pass.

---

## F. Otherworldly terrain — thoughts

Asked for, and worth writing down before any of it is built.

**The honest answer is that most of it is the biome layer's job.** A Domain feels
alien because of what is *on* it — the colour of the light, what grows, what the
rock is, what the water is made of — far more than because of its geometry. Two
Domains with identical terrain and different `Material` palettes read as two
different worlds; two Domains with the same palette and different terrain read as
the same world twice. So the cheap wins are all in materials, features and light,
and they should not be spent on terrain rules.

That said, terrain can do things Earth does not, and the model already supports
more of it than is being used:

**Already possible, not yet used.**

- **Non-water fluids.** `FluidKind` *was* in and parametrised — `Lava` and
  `Essence` turned every watercourse from a road into a wall, which is a large
  change to how a Domain plays. It has been **removed**, because what it actually
  shipped was two `if` statements (no fords, no ferries) and a dropdown with no
  visible effect: the whole idea is the *look* — glow, and a fluid not drawn like
  water — and none of that existed. Better to bring it back with the renderer
  that makes it mean something than to leave a control that does nothing. The two
  lines it gated are the only thing to restore; see the commit that removed it.
- **Arches and overhangs** already produce geometry no height field can, and at
  the moment they are decoration. Made walkable, a natural bridge is a free
  crossing you did not build, which is a very "somewhere else" thing to find.
- **The keel.** Nobody stands on the underside, and it is the most alien surface
  on the island. Inverted features — hanging spires, roots, stalactite ridges —
  cost nothing in gameplay terms because nothing walks there.

**Worth building, in rough order of value per unit of work.**

- **Aether-carved terrain.** The one thing this setting has that Earth does not is
  that the island *flies*. Wind and aether-scour would come from a fixed
  direction per Domain, so the windward rim is bare, undercut and streaked, and
  the lee is where soil and forest survive. It reuses the dune grain (already
  implemented, already directional) and the overhang machinery, and it makes
  "which way is the Domain moving" a visible fact.
- **Columnar forests.** Not karst — karst towers are scenery. A *forest* of
  narrow columns is a field of one-cell pillars close enough together that the
  gaps between them are the country: you walk the floor and the sky is broken
  into shafts. Mechanically it is karst with a much lower threshold and a much
  smaller tower footprint; visually it is completely different, and it makes a
  Domain that is genuinely hard to cross without being hard to *reach*.
- **Floating fragments.** Small masses hanging *above* the main island, tethered
  by nothing. The span model represents them for free (a column with a high span
  and no keel — which is exactly the case the arch work found is dangerous, so it
  would need the region and landform planes filled in for those columns). Reached
  by air only, which gives the aethership something to do that a bridge cannot.
- **Inverted watercourses.** Water that falls *upward* off the rim, or a lake
  whose surface is the underside of a slab. Cheap to render, deeply strange, and
  it would want its own traversal answer.
- **A Domain with no down.** Gravity is a per-Domain constant in the design
  already. A Domain where it points along an axis, or toward the island's core,
  is a large piece of engine work and probably a late-game set-piece rather than
  a generation option.

**What I would not do:** more Earth landforms for their own sake. There are ten
now, and the honest constraint is that a landform has to say something in the
grammar — walkable, climbable, a wall, a floor — or it is a texture, and textures
belong to the material layer.
