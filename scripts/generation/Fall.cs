using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Where a watercourse drops rather than runs: an inner fall is a step of
/// <see cref="Rivers.FallDepth"/> slabs or more along a channel, and an
/// <see cref="OffRim"/> fall pours off the edge of the Domain into aether —
/// the end of every river there is, because there is no sea.
/// </summary>
/// <param name="Cell">The column the water leaves from.</param>
/// <param name="Top">Slab level of the water surface at the head of the fall.</param>
/// <param name="Bottom">
/// Slab level the water reaches: the pool below for an inner fall, and for a rim
/// fall the bottom of the keel plus a tail, since what happens after that is aether.
/// </param>
/// <param name="Flow">Cardinal direction the water is going, as a unit step.</param>
/// <param name="OffRim">True where the fall pours off the edge of the Domain.</param>
/// <param name="Width">
/// Cells across. Always 1: each cell of a navigable pair records its own sheet,
/// and the two together are the two-cell fall. Kept as a hook.
/// </param>
public readonly record struct Fall(Vector2I Cell, short Top, short Bottom, Vector2I Flow,
                                   bool OffRim, int Width = 1)
{
    /// <summary>Slabs from the lip to where it lands.</summary>
    public int Drop => Top - Bottom;
}
