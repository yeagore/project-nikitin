namespace ProjectNikitin.Generation;

/// <summary>
/// How a Domain's land is laid out in its footprint — one mass, or several.
///
/// This replaces the old <c>Fragmentation</c> float, which asked one number to
/// mean both "how broken up" and "how many pieces" and produced neither
/// reliably. Named arrangements are what a biome or a world-tree position would
/// actually want to specify, and each one can then be built deliberately rather
/// than hoped for out of a noise threshold.
///
/// <b>Every arrangement is linkable.</b> Whatever the layout, the pieces are
/// nudged together until land faces land across at most
/// <see cref="IslandParams.Crossings"/> cells somewhere — so an archipelago is
/// an island you have to *build* your way across, not a set of separate worlds.
/// </summary>
public enum IslandArrangement
{
    /// <summary>Choose one of the concrete arrangements from the seed.</summary>
    Auto = 0,

    /// <summary>One landmass filling the footprint.</summary>
    Single = 1,

    /// <summary>One dominant landmass with two to four islets around it.</summary>
    Satellites = 2,

    /// <summary>Two comparable landmasses.</summary>
    Twins = 3,

    /// <summary>Three comparable landmasses.</summary>
    Triplets = 4,

    /// <summary>Five to eight small islands, none dominant.</summary>
    Archipelago = 5,

    /// <summary>
    /// A ring of islets round an open lagoon, parted by straits. Was called
    /// <c>Atoll</c>; the name moved to the layout that actually looks like one.
    /// </summary>
    BrokenRing = 6,

    /// <summary>
    /// The same ring unbroken: one landmass you can walk all the way round,
    /// enclosing its lagoon.
    /// </summary>
    Ring = 7,

    /// <summary>Part of a ring — a crescent of land round an open bay.</summary>
    Arc = 8,

    /// <summary>The crescent, parted into islets.</summary>
    BrokenArc = 9,

    /// <summary>
    /// Beads on a string: a ring of rounded islets that all but touch, cape to
    /// cape, with a step of water between each pair.
    /// </summary>
    Atoll = 10,

    /// <summary>Many small islands — not literally a thousand, but too many to name.</summary>
    ThousandIsles = 11,

    /// <summary>One landmass with four arms, on the cardinal axes.</summary>
    Cross = 12,

    /// <summary>A winding chain, doubling back on itself: a snake of land.</summary>
    Fractal = 13,

    /// <summary>
    /// One landmass cracked into four to six pieces, the straits between them
    /// narrow enough to read as fractures rather than as sea.
    /// </summary>
    Shards = 14,

    /// <summary>A bar with one arm off the middle of it: three arms, not four.</summary>
    TShape = 15,

    /// <summary>Two arms meeting at a right angle — a corner of land round a bay.</summary>
    LShape = 16,

    // The broken forms. Same layouts, but the seam where two blobs meet is carved
    // into a strait, so the arms become a line of islands off a central one. A
    // broken cross is the shape you cross by building; a whole one you walk.

    /// <summary>The cross, its arms parted from the hub and from each other.</summary>
    BrokenCross = 17,

    /// <summary>The T, parted.</summary>
    BrokenT = 18,

    /// <summary>The L, parted.</summary>
    BrokenL = 19,

    /// <summary>The snake, parted into a chain of stepping stones.</summary>
    BrokenFractal = 20,

    /// <summary>
    /// A rosette: a ring of lobes overlapping a full hub, so the coast comes out
    /// as a run of deep round bays with headlands between them. It was meant to be
    /// a spiral and came out a flower; the flower was better, so it kept the
    /// shape and lost the name.
    /// </summary>
    Rosette = 21,

    /// <summary>
    /// A hub with five or six arms — the cross taken further, so no two arms face
    /// each other and the bays between them are wedges.
    /// </summary>
    Star = 22,

    // 23 was Spiral — a thin arm wound inward over two and a half turns. On paper
    // it was the one layout whose middle is a long walk round or a short crossing
    // over; on the island it needed a coil so thick and so tightly linked to stay
    // one landmass that what came out was Rosette with more steps. Removed rather
    // than tuned: two names for one shape is worse than one shape.

    // --- the geometric set, 2026-09-01 -------------------------------------

    /// <summary>One blocky landmass filling a square, axis-aligned.</summary>
    Square = 24,

    /// <summary>The square stood on a corner: a diamond with its points on the axes.</summary>
    Rhomb = 25,

    /// <summary>The letter itself: two uprights and the diagonal joining them.</summary>
    NShape = 26,

    /// <summary>Four roughly symmetric parts, one per quadrant, parted by a cross of straits.</summary>
    Quarters = 27,

    /// <summary>Two roughly symmetric halves parted by one axis-aligned strait.</summary>
    Halves = 28,

    /// <summary>
    /// Two interlocked commas chasing each other round one disc — the yin-yang.
    /// The first arrangement built from <b>grouped</b> lobes: each comma is a
    /// chain that fuses, and only the S between the two is carved.
    /// </summary>
    Harmony = 29,

    /// <summary>Two broad heads joined by a narrow walkable neck of land.</summary>
    Isthmus = 30,

    /// <summary>A main island sheltered behind a long thin barrier chain of islets.</summary>
    Reef = 31,
}
