using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Where a bridge goes: two bank cells facing each other across
/// <see cref="Span"/> cells of aether or water, and the level its deck runs at.
///
/// <b>A bridge is several slabs at one level running from land to land in one
/// direction.</b> It is not a ramp and it does not climb, so the deck has a
/// single level and both banks have to be walkable onto it — one slab of
/// difference at each end, which is the free step. The generator guarantees that
/// by levelling the two bridgeheads before the terrain is finished
/// (<c>IslandGenerator.LevelBridgeheads</c>); what remains here is a record of
/// the crossing so the settlement layer, the lab overlay and the audit all read
/// the same answer.
/// </summary>
/// <param name="A">Bank cell on one side.</param>
/// <param name="B">Bank cell on the other; always cardinally in line with <paramref name="A"/>.</param>
/// <param name="Deck">Slab level of the deck — the level you walk across at.</param>
/// <param name="Span">Cells of gap between the two banks: the length of the deck.</param>
public readonly record struct Crossing(Vector2I A, Vector2I B, short Deck, int Span)
{
    /// <summary>Unit step from <see cref="A"/> toward <see cref="B"/>.</summary>
    public Vector2I Step => new(System.Math.Sign(B.X - A.X), System.Math.Sign(B.Y - A.Y));

    /// <summary>The deck cells themselves, in order from <see cref="A"/> to <see cref="B"/>.</summary>
    public Vector2I Cell(int i) => A + Step * (i + 1);
}
