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
/// <see cref="Traversal.MaxBridgeSpan"/> cells somewhere — so an archipelago is
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

    /// <summary>A broken ring of islets around an open lagoon.</summary>
    Atoll = 6,
}
