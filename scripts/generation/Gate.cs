using Godot;

namespace ProjectNikitin.Generation;

/// <summary>Which edge of the Domain a Gate faces. Domains are laid out on a plane by world-tree position.</summary>
public enum Cardinal
{
    North = 0,   // -Z
    East = 1,    // +X
    South = 2,   // +Z
    West = 3,    // -X
}

/// <summary>
/// How a Gate meets the Domain. The two are not interchangeable: a Link joins
/// two Gates, so the far end of an <see cref="Entry"/> has to be the same kind as
/// the Gate it came from — see <see cref="IslandParams.EntryGate"/>.
/// </summary>
public enum GateKind
{
    /// <summary>Choose from the seed.</summary>
    Auto = 0,

    /// <summary>Stands on the ground. You walk through it.</summary>
    Land = 1,

    /// <summary>
    /// Hangs in the aether off the rim. You fly through it, which means the
    /// Domain has to offer somewhere to land: a strip of level ground running
    /// inward from the coast directly opposite.
    /// </summary>
    Hanging = 2,
}

/// <summary>
/// Which edge a Gate is asked to stand on. <c>Cardinal</c> with an <c>Auto</c>,
/// so it can be an input rather than a consequence.
///
/// The Entry's edge matters for the same reason its kind does: it is the *other*
/// Domain's decision, not this one's. A Domain reached by travelling east comes
/// out on its west edge, and nothing about the coast may move it — least of all
/// changing the Gate's kind, which used to send it round to the far side of the
/// island because that coast happened to score better.
/// </summary>
public enum GateEdge
{
    /// <summary>Let the seed pick, trying each edge in turn.</summary>
    Auto = 0,
    North = 1,
    East = 2,
    South = 3,
    West = 4,
}

/// <summary>Whether the player arrives through this Gate or leaves by it.</summary>
public enum GateRole
{
    /// <summary>Where the player emerges. Exactly one per Domain.</summary>
    Entry = 0,

    /// <summary>A Link onward. One to three per Domain.</summary>
    Exit = 1,
}

/// <summary>
/// A Gate: the built structure at one end of a Link. Three cells wide, one deep
/// and twelve slabs tall — a square portal, since a slab is a quarter of a cell.
/// </summary>
/// <param name="Kind">Standing on the ground, or hanging in the aether.</param>
/// <param name="Role">Arrival or departure.</param>
/// <param name="Facing">Which edge of the Domain it faces; at most one Gate per edge.</param>
/// <param name="Center">Centre column of the portal, and the slab its base sits on.</param>
/// <param name="Apron">
/// Centre of the ground the Gate is served by: the starter-base shelf for a Land
/// Gate, the inner end of the landing strip for a Hanging one.
/// </param>
/// <param name="ApronArea">Cells of level ground there. What the first settlement has to work with.</param>
/// <param name="Landing">
/// Cells of landing strip running inland from the coast, for a
/// <see cref="GateKind.Hanging"/> Gate; 0 for a Land one. Usually the full
/// <c>GatePlacement.StripLength</c>, but an Entry may settle for a shorter one:
/// its <i>kind</i> is fixed by the Link and cannot be traded away, so when a
/// coast will not offer a full strip, the strip is what gives.
/// </param>
public readonly record struct Gate(GateKind Kind, GateRole Role, Cardinal Facing,
                                   Vector3I Center, Vector2I Apron, int ApronArea,
                                   int Landing = 0)
{
    /// <summary>Cells across the portal.</summary>
    public const int Width = 3;

    /// <summary>Slabs tall — three world units, the same as its width.</summary>
    public const int Height = 12;

    /// <summary>Outward normal: the way out of the Domain.</summary>
    public Vector2I Outward => Facing switch
    {
        Cardinal.North => new Vector2I(0, -1),
        Cardinal.East => new Vector2I(1, 0),
        Cardinal.South => new Vector2I(0, 1),
        _ => new Vector2I(-1, 0),
    };

    /// <summary>Along the portal's face: the direction its three cells run.</summary>
    public Vector2I Across => new(-Outward.Y, Outward.X);
}
