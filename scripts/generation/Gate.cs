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

/// <summary>Grid directions of a <see cref="Cardinal"/>, defined once for Gates and their placement.</summary>
public static class Cardinals
{
    /// <summary>Outward normal: the way off the Domain.</summary>
    public static Vector2I Outward(this Cardinal edge) => edge switch
    {
        Cardinal.North => new Vector2I(0, -1),
        Cardinal.East => new Vector2I(1, 0),
        Cardinal.South => new Vector2I(0, 1),
        _ => new Vector2I(-1, 0),
    };

    /// <summary>Along the edge: outward turned a quarter.</summary>
    public static Vector2I Across(this Cardinal edge)
    {
        Vector2I o = edge.Outward();
        return new Vector2I(-o.Y, o.X);
    }
}

/// <summary>
/// How a Gate meets the Domain. A Link joins two Gates of the same kind, so the Entry's
/// kind is an input set by the far Domain — see <see cref="IslandParams.EntryGate"/>.
/// </summary>
public enum GateKind
{
    /// <summary>Choose from the seed.</summary>
    Auto = 0,

    /// <summary>Stands on the ground; walked through.</summary>
    Land = 1,

    /// <summary>Hangs in the aether off the rim; flown through, so the Domain owes it a landing strip running inland from the coast opposite.</summary>
    Hanging = 2,
}

/// <summary>
/// Which edge a Gate is asked to stand on: <see cref="Cardinal"/> plus <see cref="Auto"/>.
/// The Entry's edge is the far Domain's decision — arriving eastward comes out on the west
/// edge — so it is an input. Convert with <c>(Cardinal)((int)edge - 1)</c>.
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
/// The built structure at one end of a Link: one cell wide and four slabs tall — a single
/// block. A hanging Gate floats <c>GatePlacement.HangingOffset</c> cells off the rim, two
/// slabs above its 1 × <c>GatePlacement.StripLength</c> landing strip; a land Gate stands
/// on that strip's head instead.
/// </summary>
/// <param name="Kind">Standing on the ground, or hanging in the aether.</param>
/// <param name="Role">Arrival or departure.</param>
/// <param name="Facing">Which edge of the Domain it faces; at most one Gate per edge.</param>
/// <param name="Center">The portal's column and the slab its base sits on.</param>
/// <param name="Apron">The inner end of the landing strip: the ground the Gate is served by.</param>
/// <param name="ApronArea">Cells of the best buildable shelf near the strip's head — what the first settlement has to work with.</param>
/// <param name="Landing">Cells of landing strip running inland from the coast; always <c>GatePlacement.StripLength</c>, for both kinds.</param>
public readonly record struct Gate(GateKind Kind, GateRole Role, Cardinal Facing,
                                   Vector3I Center, Vector2I Apron, int ApronArea,
                                   int Landing = 0)
{
    /// <summary>Cells across the portal.</summary>
    public const int Width = 1;

    /// <summary>Slabs tall: one block, since a slab is a quarter of a cell.</summary>
    public const int Height = 4;

    /// <summary>Outward normal: the way out of the Domain.</summary>
    public Vector2I Outward => Facing.Outward();

    /// <summary>Along the portal's face.</summary>
    public Vector2I Across => Facing.Across();
}
