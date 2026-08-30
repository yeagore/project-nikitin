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
}
