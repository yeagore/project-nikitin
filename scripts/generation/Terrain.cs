namespace ProjectNikitin.Generation;

/// <summary>
/// World-scale constants for terrain. The terrain unit is a <b>slab</b>: a
/// square cell <see cref="CellSize"/> on the X/Z side and <see cref="SlabHeight"/>
/// (a quarter of that) tall. Levels in <see cref="IslandData"/> are integer slab
/// indices on Y; multiply by <see cref="SlabHeight"/> for world units.
/// See CLAUDE.md and docs/island-generation.md.
/// </summary>
public static class Terrain
{
    /// <summary>X/Z size of one grid cell, in Godot units (metres).</summary>
    public const float CellSize = 1.0f;

    /// <summary>Y height of one slab = <see cref="CellSize"/> / 4.</summary>
    public const float SlabHeight = 0.25f;

    /// <summary>Slabs stacked to the height of one full cell.</summary>
    public const int SlabsPerCell = 4;
}
