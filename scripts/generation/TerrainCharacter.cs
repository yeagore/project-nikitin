namespace ProjectNikitin.Generation;

/// <summary>
/// What an island is made of. Real terrain does not mix every landform at once —
/// you get plains, or plains and hills, or plains and hills and mountains — so
/// each character names one plausible combination and plains are the constant
/// that runs through all of them.
///
/// This is the only landform knob. Where the high ground sits is picked
/// internally per character (see <see cref="ReliefStyle"/>), because it is a
/// consequence of the character rather than a separate choice.
/// </summary>
public enum TerrainCharacter
{
    /// <summary>Choose one of the concrete characters from the seed.</summary>
    Auto = 0,

    /// <summary>Plains, and nothing else. Open, buildable, crossable throughout.</summary>
    Plains = 1,

    /// <summary>Plains, with mesas standing out of them and basins sunk into them.</summary>
    Tableland = 2,

    /// <summary>Plains and hills — rolling country, walkable everywhere.</summary>
    Downs = 3,

    /// <summary>Plains, hills, and mountains over one part of the island.</summary>
    Highland = 4,
}
