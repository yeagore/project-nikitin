namespace ProjectNikitin.Meshing;

/// <summary>Which side of a slab a face is: what a tint and a shader read off a vertex.</summary>
public enum FaceKind : byte
{
    Top = 0,
    Side = 1,
    Bottom = 2,

    /// <summary>A sheet of falling water: not a side of the water's volume but the face it pours down.</summary>
    Fall = 3,
}
