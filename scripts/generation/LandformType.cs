namespace ProjectNikitin.Generation;

/// <summary>
/// What the terrain does inside one region. Each type is defined by its relief
/// amplitude and, crucially, its <b>slope limit</b> — the largest step allowed
/// between neighbouring cells inside the region.
///
/// A one-slab step is free to traverse; two or more needs infrastructure. So the
/// limit is a gameplay statement, not a visual one: everything at limit 1 is
/// walkable everywhere, and only <see cref="Mountain"/> is deliberately not.
/// Cliffs come from the gap between regions, never from noise.
/// </summary>
public enum LandformType
{
    /// <summary>Flat give or take a slab. Buildable, crossable, dull on purpose.</summary>
    Plain = 0,

    /// <summary>Rolling ground in one-slab increments — walkable everywhere.</summary>
    Hills = 1,

    /// <summary>
    /// Rises out of its surroundings on an S-curve — foothills at the foot,
    /// consecutive multi-slab risers through the middle, a flatter but rugged
    /// summit. It sits on no plateau of its own: a mountain that starts with a
    /// cliff is a mesa with hills on it.
    /// </summary>
    Mountain = 2,

    /// <summary>
    /// A flat top standing clear above <b>every</b> neighbour, so its whole
    /// border is cliff. Same silhouette as a mountain's lower two thirds, minus
    /// the summit — cut off flat instead.
    /// </summary>
    Mesa = 3,

    /// <summary>
    /// A mesa inverted: a flat floor sunk clear below every neighbour, ringed by
    /// an inward-facing cliff. Sheltered, enclosed, and the natural place for
    /// standing water to collect once lakes exist.
    /// </summary>
    Basin = 4,

    // ---- sculpted landforms -------------------------------------------------
    // The three below are built the way a canyon is: a plain is laid down and
    // limited like any other, and the shape is then cut or raised into it and
    // exempted from the limiter. That is what lets them carry cliffs *inside* a
    // patch, which the ladder alone cannot express — and it keeps every border
    // they have bound, so the cliff rule still holds at the edges.

    /// <summary>
    /// Eroded tableland: flat fingers at the plateau level with a maze of narrow
    /// gullies cut between them. Walkable along a finger, a climb between two —
    /// the same country read as a network of corridors rather than as open
    /// ground.
    /// </summary>
    Badlands = 5,

    /// <summary>
    /// A field of towers: a flat valley floor with steep-sided columns standing
    /// out of it, the way the karst of Guilin does. The floor is the country and
    /// the towers are scenery you cannot climb — the inverse of a mesa, which is
    /// a top you stand on.
    /// </summary>
    Karst = 6,

    /// <summary>
    /// A stepped massif: concentric terraces, each a cliff above the last,
    /// climbing to a flat summit. Not circular — the rings follow a warped
    /// contour, so it reads as weathered rather than as a wedding cake. Every
    /// terrace is walkable and every riser wants a stair, which makes it the one
    /// high landform that is <i>meant</i> to be built up.
    /// </summary>
    Massif = 7,

    /// <summary>
    /// Long parallel ridges, all running the same way, in one-slab steps. Rolling
    /// like hills and directional like a grain: crossing them is a wash-board and
    /// running along them is level, which is a thing no other landform does.
    /// </summary>
    Dunes = 8,

    /// <summary>
    /// Open ground pocked with round pits — dolines, the same limestone as
    /// <see cref="Karst"/> read from the other side. Walkable everywhere except
    /// the holes, which makes it country you cross while watching your feet rather
    /// than country you cannot cross.
    /// </summary>
    Sinkholes = 9,
}
