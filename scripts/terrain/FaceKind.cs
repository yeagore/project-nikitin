namespace ProjectNikitin.Meshing;

/// <summary>Which side of a slab a face is: what a tint and a shader read off a vertex.</summary>
public enum FaceKind : byte
{
    Top = 0,
    Side = 1,
    Bottom = 2,
}
