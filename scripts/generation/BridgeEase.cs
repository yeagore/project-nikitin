namespace ProjectNikitin.Generation;

/// <summary>
/// The widest gap one bridge may span, in cells — the value is the span. A difficulty
/// knob rather than a rendering detail: it also decides how far apart an arrangement's
/// landmasses may sit. Both banks are levelled to within a slab of the deck whatever the span.
/// </summary>
public enum BridgeEase
{
    /// <summary>One cell: every crossing is a single-span footbridge.</summary>
    Easy = 1,

    /// <summary>Up to three cells. The default.</summary>
    Medium = 3,

    /// <summary>Up to six cells: real spans, and islets that read as separate places.</summary>
    Hard = 6,
}
