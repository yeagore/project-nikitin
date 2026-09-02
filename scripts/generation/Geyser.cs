using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// One jet of a geyser field. Dormant hook: nothing fills <see cref="IslandData.Geysers"/>
/// today; the biome layer will place these.
/// </summary>
/// <param name="Cell">The column the jet stands on.</param>
/// <param name="Base">Slab level of the ground it erupts from.</param>
/// <param name="Top">Slab level the jet reaches.</param>
public readonly record struct Geyser(Vector2I Cell, short Base, short Top)
{
    /// <summary>Slabs of jet above the ground.</summary>
    public int Height => Top - Base;
}
