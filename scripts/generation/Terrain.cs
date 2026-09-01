using System;

namespace ProjectNikitin.Generation;

/// <summary>
/// World-scale constants. The terrain unit is a <b>slab</b>: a square cell
/// <see cref="CellSize"/> on the X/Z side and <see cref="SlabHeight"/> (a quarter
/// of that) tall. Levels in <see cref="IslandData"/> are integer slab indices on Y.
/// </summary>
public static class Terrain
{
    /// <summary>X/Z size of one grid cell, in Godot units (metres).</summary>
    public const float CellSize = 1.0f;

    /// <summary>Y height of one slab = <see cref="CellSize"/> / 4.</summary>
    public const float SlabHeight = 0.25f;

    /// <summary>
    /// A level as a slab index, rounded (half to even) and kept one above
    /// <see cref="IslandData.NoLand"/> so no height computation can produce the sentinel.
    /// </summary>
    public static short SlabClamp(float level)
        => (short)Math.Clamp((int)MathF.Round(level), short.MinValue + 1, short.MaxValue);

    public static short SlabClamp(int level)
        => (short)Math.Clamp(level, short.MinValue + 1, short.MaxValue);
}
