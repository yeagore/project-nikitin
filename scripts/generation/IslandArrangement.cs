namespace ProjectNikitin.Generation;

/// <summary>
/// How a Domain's land is laid out in its footprint — one mass, or several. Every arrangement
/// is linkable: the pieces are nudged together until land faces land across at most
/// <see cref="IslandParams.Crossings"/> cells somewhere. Values are serialised; never renumber.
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

    /// <summary>A ring of islets round an open lagoon, parted by straits.</summary>
    BrokenRing = 6,

    /// <summary>The same ring unbroken: one landmass you can walk all the way round.</summary>
    Ring = 7,

    /// <summary>Part of a ring — a crescent of land round an open bay.</summary>
    Arc = 8,

    /// <summary>The crescent, parted into islets.</summary>
    BrokenArc = 9,

    /// <summary>Beads on a string: a ring of rounded islets that all but touch, a step of water between each pair.</summary>
    Atoll = 10,

    /// <summary>Many small islands, quilted over the whole footprint.</summary>
    ThousandIsles = 11,

    /// <summary>One landmass with four arms, on the cardinal axes.</summary>
    Cross = 12,

    /// <summary>A winding chain, doubling back on itself: a snake of land.</summary>
    Fractal = 13,

    /// <summary>One landmass cracked into four to six pieces by straits narrow enough to read as fractures.</summary>
    Shards = 14,

    /// <summary>A bar with one arm off the middle of it: three arms, not four.</summary>
    TShape = 15,

    /// <summary>Two arms meeting at a right angle — a corner of land round a bay.</summary>
    LShape = 16,

    // The broken forms: the same layouts with the seam between two blobs carved into a strait.

    /// <summary>The cross, its arms parted from the hub and from each other.</summary>
    BrokenCross = 17,

    /// <summary>The T, parted.</summary>
    BrokenT = 18,

    /// <summary>The L, parted.</summary>
    BrokenL = 19,

    /// <summary>The snake, parted into a chain of stepping stones.</summary>
    BrokenFractal = 20,

    /// <summary>A ring of lobes overlapping a full hub: deep round bays with headlands between them.</summary>
    Rosette = 21,

    /// <summary>A hub with five or six arms, so no two arms face each other.</summary>
    Star = 22,

    // 23 was Spiral (removed); keep the gap — values are serialised.

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

    /// <summary>The yin-yang: two grouped comma chains that each fuse, with only the S between them carved.</summary>
    Harmony = 29,

    /// <summary>Two broad heads joined by a narrow walkable neck of land.</summary>
    Isthmus = 30,

    /// <summary>A main island sheltered behind a long thin barrier chain of islets.</summary>
    Reef = 31,
}
