using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Where a bridge goes: two bank cells facing each other across <see cref="Span"/>
/// cells of aether, water or chasm, and the one level its deck runs at. A deck does
/// not climb, so both banks are levelled to within a free step of it before the
/// terrain is finished (<c>IslandGenerator.LevelBridgeheads</c>); this record is what
/// the settlement layer, the lab overlay and the audit all read.
/// </summary>
/// <param name="A">Bank cell on one side.</param>
/// <param name="B">Bank cell on the other, cardinally in line with <paramref name="A"/>.</param>
/// <param name="Deck">Slab level of the deck.</param>
/// <param name="Span">Cells of gap between the banks: the length of the deck.</param>
public readonly record struct Crossing(Vector2I A, Vector2I B, short Deck, int Span)
{
    /// <summary>Unit step from <see cref="A"/> toward <see cref="B"/>.</summary>
    public Vector2I Step => new(System.Math.Sign(B.X - A.X), System.Math.Sign(B.Y - A.Y));

    /// <summary>The deck cells themselves, in order from <see cref="A"/> to <see cref="B"/>.</summary>
    public Vector2I Cell(int i) => A + Step * (i + 1);
}
