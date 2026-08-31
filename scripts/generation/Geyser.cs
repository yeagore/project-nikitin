using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// A column of water thrown up out of the ground — the one watercourse that
/// runs the wrong way. Geysers come in <b>fields</b>: an island that has any
/// has a handful, clustered, because a lone jet in the middle of a plain reads
/// as a glitch and a steaming field of them reads as a place.
///
/// <para><b>Nothing fills this today.</b> A terrain-stage placement briefly
/// did (2026-09-01, binned the same day): it put jets where the rock was, and
/// where a jet belongs is a fact about the <i>biome</i> — so the placement
/// waits for that layer, while the type, <see cref="IslandData.Geysers"/> and
/// the lab's crossed-sheet rendering stay as the hook it will fill.</para>
/// </summary>
/// <param name="Cell">The column the jet stands on.</param>
/// <param name="Base">Slab level of the ground it erupts from.</param>
/// <param name="Top">Slab level the jet reaches.</param>
public readonly record struct Geyser(Vector2I Cell, short Base, short Top)
{
    /// <summary>Slabs of jet above the ground.</summary>
    public int Height => Top - Base;
}
