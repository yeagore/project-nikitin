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
| two-slab steps off mountains | 948 — riverbanks and valley sides the pass is not allowed to cut |
| cliffs between patches | plain-plain, plain-mesa, plain-basin, mesa-mesa — the pairs the rules allow |
| rivers | 4.5k cells on 60 of 60 islands, 796 navigable, ~540 falls |
| how a course runs | 60% straight / 40% turning |
| lakes | 93 on 45 of 60, **0 leaks, 0 water touching the void** |
| ferries | 48 berths of 3,362 sites (1% load-bearing), on the 1 island in 60 where water is genuinely in the way |
| surface | stone 11%, scree 15%, snow 13%, sand 21%, silt 5%, grass 3%, heath 23%, dust 2%, meadow 8% — none NEVER |
| anchors | 29k coast (81% of it beached), 31k cliff, 250 overhang, 372 ford, 1.8k gate landing |
| overhangs | ~250 columns with a second span |
| walk / reach | **40% mainland on foot, 95% heartland with building**, 50 of 60 islands one whole |
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
| two-slab steps at a riverbank or valley side | 948 in 378k pairs, all where the ground the pass would have to cut is a landform, a bridgehead or standing water. It rose from ~730 when valleys started working; the alternative is eating the landform. |
| ~~3 crossings with banks more than 2 slabs apart~~ | **closed.** It came in with the sculpted landforms and went out with the crater cull and the beach reordering: 0 of 149. |
| 3 gates in a corner of their own edge | The relaxed placement rungs firing where a coast will not take four Gates under the full rules. No pair is inside the separation floor any more. The alternative is a Domain with fewer Links, which is worse. |
| 2 river cells running uphill in 4,578 | Where a channel could not sink as far as the stretch above it — a bridgehead or a mesa rim in the way — and the second `Descend` did not fully flatten it. Was 1 before valleys worked. |
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
