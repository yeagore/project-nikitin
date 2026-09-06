using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Meshing;

/// <summary>
/// The provisional look of each <see cref="SurfaceMaterial"/>: one flat colour the
/// mesher tints a column's faces with until the biome layer brings textures. The
/// lab's legend and the audit's sheets read this same table, so a swatch there is
/// the colour on the ground.
/// </summary>
public static class SurfacePalette
{
    public static Color Of(SurfaceMaterial m) => m switch
    {
        SurfaceMaterial.Stone => new Color(0.40f, 0.40f, 0.46f),      // dark cool grey
        SurfaceMaterial.Scree => new Color(0.80f, 0.68f, 0.54f),      // warm beige
        SurfaceMaterial.Snow => new Color(0.95f, 0.96f, 0.98f),
        SurfaceMaterial.Sand => new Color(0.95f, 0.90f, 0.66f),
        SurfaceMaterial.Silt => new Color(0.44f, 0.32f, 0.20f),
        SurfaceMaterial.Tundra => new Color(0.58f, 0.80f, 0.74f),     // pale mint, nothing like rock
        SurfaceMaterial.Moorland => new Color(0.62f, 0.44f, 0.64f),
        SurfaceMaterial.Bog => new Color(0.26f, 0.36f, 0.32f),
        SurfaceMaterial.Steppe => new Color(0.76f, 0.68f, 0.40f),
        SurfaceMaterial.Meadow => new Color(0.66f, 0.82f, 0.34f),
        SurfaceMaterial.Grass => new Color(0.28f, 0.62f, 0.22f),
        SurfaceMaterial.Dust => new Color(0.78f, 0.48f, 0.30f),
        SurfaceMaterial.Savanna => new Color(0.90f, 0.72f, 0.22f),
        SurfaceMaterial.Floodplain => new Color(0.16f, 0.74f, 0.46f),
        SurfaceMaterial.Marsh => new Color(0.30f, 0.52f, 0.50f),      // blue-green, duller than grass, lighter than bog
        SurfaceMaterial.Heath => new Color(0.58f, 0.42f, 0.40f),      // heather-brown, between the mint and the mauve
        SurfaceMaterial.Verdure => new Color(0.08f, 0.42f, 0.20f),    // the deepest green: darker than grass, purer than bog
        _ => new Color(1f, 0f, 1f),                                   // an unmapped member: loud on purpose
    };
}
