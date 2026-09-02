namespace ProjectNikitin.Generation;

/// <summary>
/// Where a Domain's high ground lies: biases which landform each region gets.
/// Internal; chosen per <see cref="TerrainCharacter"/> in <c>IslandGenerator.StyleFor</c>.
/// </summary>
public enum ReliefStyle
{
    /// <summary>Choose one of the concrete styles from the seed.</summary>
    Auto = 0,

    /// <summary>A single dome centred on the footprint.</summary>
    CentralPeak = 1,

    /// <summary>A single dome pushed off-centre, so one flank is a long slope.</summary>
    OffsetPeak = 2,

    /// <summary>Two domes of unequal size, usually with a saddle between them.</summary>
    TwinPeaks = 3,

    /// <summary>A spine running across the island; steep flanks, flat ends.</summary>
    Ridge = 4,

    /// <summary>A broad flat tableland ringed by a steep drop.</summary>
    Plateau = 5,

    /// <summary>One edge high, sloping steadily down to the far side.</summary>
    Tilted = 6,
}
