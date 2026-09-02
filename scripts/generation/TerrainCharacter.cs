namespace ProjectNikitin.Generation;

/// <summary>
/// Which landforms an island is built from; plains run through all of them. The only landform
/// knob — the <see cref="ReliefStyle"/> is chosen per character. Numeric values and order are
/// load-bearing: Auto's dice index them and the .tres stores them as ints.
/// </summary>
public enum TerrainCharacter
{
    /// <summary>Choose one of the concrete characters from the seed.</summary>
    Auto = 0,

    /// <summary>Plains, and nothing else. Open, buildable, crossable throughout.</summary>
    Plains = 1,

    /// <summary>Plains, with mesas standing out of them and basins sunk into them.</summary>
    Tablelands = 2,

    /// <summary>Plains and hills — rolling country, walkable everywhere.</summary>
    Downs = 3,

    /// <summary>Plains, hills, and mountains over one part of the island.</summary>
    Highlands = 4,

    // ---- characters built on the sculpted landforms (IslandParams.NewLandforms gates Auto's dice) ----

    /// <summary>Plains, mesas, and eroded badlands cut between them.</summary>
    Badlands = 5,

    /// <summary>Plains and hills with fields of karst towers standing out of them.</summary>
    Karst = 6,

    /// <summary>Plains and hills under stepped massifs, and a mountain behind them.</summary>
    Massif = 7,

    /// <summary>Plains, and long dune fields running one way across them.</summary>
    Dunes = 8,
}
