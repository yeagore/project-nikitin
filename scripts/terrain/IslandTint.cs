using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Meshing;

/// <summary>
/// How the mesher colours what it builds: a colour per ground face from the column,
/// its span and the face's kind, and one per flooded column for the water. The
/// geometry is the same whatever the tint; the lab swaps in a tint per view, the
/// game uses <see cref="Default"/>.
/// </summary>
public sealed class IslandTint
{
    public delegate Color GroundTint(IslandData d, int x, int z, int span, FaceKind face);

    public delegate Color LiquidTint(IslandData d, int x, int z);

    public GroundTint Ground { get; }

    public LiquidTint Liquid { get; }

    public IslandTint(GroundTint ground, LiquidTint liquid)
    {
        Ground = ground;
        Liquid = liquid;
    }

    /// <summary>
    /// The game's provisional look: the column's <see cref="SurfaceMaterial"/> on the
    /// ground's top and sides, stone for an overhang's lip and for every underside,
    /// and white on the water so its material's own blue shows.
    /// </summary>
    public static readonly IslandTint Default = new(
        (d, x, z, span, face) => SurfacePalette.Of(
            span > 0 || face == FaceKind.Bottom
                ? SurfaceMaterial.Stone
                : (SurfaceMaterial)d.Material[x, z]),
        (d, x, z) => Colors.White);
}
