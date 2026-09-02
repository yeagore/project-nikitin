using Godot;

namespace ProjectNikitin.Generation;

/// <summary>A piece of built infrastructure a route depends on; the routing counts these, not cells.</summary>
public enum WorksKind
{
    /// <summary>A stair or hoist up a face of at most <see cref="Traversal.InfrastructureStep"/> slabs.</summary>
    Stair = 0,

    /// <summary>A level deck across aether, water or a chasm — see <see cref="Crossing"/>.</summary>
    Bridge = 1,

    /// <summary>A ferry between two quays on one body of water — see <see cref="FerryBerth"/>.</summary>
    Ferry = 2,
}

/// <summary>One work on a route: its kind, the cell you leave and the cell you arrive at.</summary>
public readonly record struct Works(WorksKind Kind, Vector2I From, Vector2I To);
