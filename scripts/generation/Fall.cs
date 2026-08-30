using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Where a watercourse drops rather than runs.
///
/// Two kinds, and the second is the one the setting is built on: an
/// <b>inner</b> fall is a step of <see cref="Rivers.FallDepth"/> slabs or more
/// along a channel — the cliff a mountain stream comes over — and a
/// <see cref="OffRim"/> fall is the end of every river there is, because a
/// Domain floats in aether and has no sea. A Domain seen from below should have
/// water spilling off its rim into nothing.
/// </summary>
/// <param name="Cell">The column the water leaves from.</param>
/// <param name="Top">Slab level of the water surface at the head of the fall.</param>
/// <param name="Bottom">
/// Slab level the water reaches: the pool below for an inner fall, and for a rim
/// fall the bottom of the keel plus a tail, since what happens after that is
/// aether.
/// </param>
/// <param name="Flow">Cardinal direction the water is going, as a unit step.</param>
/// <param name="OffRim">True where the fall pours off the edge of the Domain.</param>
/// <param name="Width">Cells across — two where the river was navigable.</param>
public readonly record struct Fall(Vector2I Cell, short Top, short Bottom, Vector2I Flow,
                                   bool OffRim, int Width = 1)
{
    /// <summary>Slabs from the lip to where it lands.</summary>
    public int Drop => Top - Bottom;
}
