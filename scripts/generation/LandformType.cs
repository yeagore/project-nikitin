namespace ProjectNikitin.Generation;

/// <summary>
/// What the terrain does inside one region: a relief amplitude and a slope limit. The limit
/// is a gameplay statement — a one-slab step is free — so cliffs come only from the gap
/// between regions or from sculpting, never from noise.
/// </summary>
public enum LandformType
{
    /// <summary>Flat give or take a slab. Buildable, crossable, dull on purpose.</summary>
    Plain = 0,

    /// <summary>Rolling ground in one-slab increments — walkable everywhere.</summary>
    Hills = 1,

    /// <summary>Rises on an S-curve: foothills, multi-slab risers, a rugged summit. Sits on no plateau of its own.</summary>
    Mountain = 2,

    /// <summary>A flat top standing clear above every neighbour, so its whole border is cliff.</summary>
    Mesa = 3,

    /// <summary>A mesa inverted: a flat floor sunk below every neighbour, ringed by an inward-facing cliff.</summary>
    Basin = 4,

    // ---- sculpted landforms -------------------------------------------------
    // Cut into a limited plain and exempted from the limiter, which is how they
    // carry cliffs inside a patch; their outer rings stay bound.

    /// <summary>Eroded tableland: flat fingers at the plateau level with a maze of gullies between them.</summary>
    Badlands = 5,

    /// <summary>A flat floor you walk with steep towers you cannot climb.</summary>
    Karst = 6,

    /// <summary>Concentric terraces, each a cliff above the last, climbing to a flat summit. Every riser wants a stair.</summary>
    Massif = 7,

    /// <summary>Long parallel ridges all running one way, in one-slab steps. Not sculpted.</summary>
    Dunes = 8,

    /// <summary>Open ground pocked with round pits; walkable everywhere except the holes.</summary>
    Sinkholes = 9,
}
