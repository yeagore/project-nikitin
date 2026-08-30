namespace ProjectNikitin.Generation;

/// <summary>
/// How hard the Domain is to build your way across: the widest gap one bridge
/// may span, in cells. It is the only thing that decides how far apart the
/// arrangement's landmasses are allowed to sit, so it is a statement about the
/// Domain's difficulty rather than a rendering detail.
///
/// <b>The value is the span in cells</b>, so a deck is exactly that many cells
/// of nothing between two banks. A bridge is a run of slabs all at one level —
/// see <see cref="Crossing"/> — and both ends have to be walkable onto, so the
/// banks are levelled to within a slab of the deck whatever the span.
/// </summary>
public enum BridgeEase
{
    /// <summary>One cell. Every crossing is a single-span footbridge.</summary>
    Easy = 1,

    /// <summary>Up to three cells. The default: straits a small crew can still deck over.</summary>
    Medium = 3,

    /// <summary>Up to six cells. Real spans, and islets that read as separate places.</summary>
    Hard = 6,
}
