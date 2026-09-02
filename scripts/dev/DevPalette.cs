using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>
/// The one palette the lab and the audit's PNGs share, so what you look at in
/// one is what the other draws. The lab's colours won.
/// </summary>
internal static class DevPalette
{
    public static readonly Color Aether = new(0.07f, 0.08f, 0.11f);
    public static readonly Color Goo = new(0.52f, 0.14f, 0.72f, 0.9f);

    /// <summary>Water under the anchors view, dimmed so the anchors read.</summary>
    public static readonly Color AnchorWater = new(0.16f, 0.24f, 0.38f);
    public static readonly Color AnchorGoo = new(0.35f, 0.10f, 0.45f);

    public const int Coast = 1, Brink = 2, Overhang = 3, Beach = 4, Ford = 5,
                     Landing = 6, Quay = 7, CliffFoot = 8, Bank = 9, Summit = 10;

    /// <summary>Standing fluid by kind: goo, then ford, navigable reach, stream, lake.</summary>
    public static Color Water(IslandData d, int x, int z)
    {
        if (d.Fluid[x, z] == (byte)FluidKind.Goo) return Goo;
        if (d.Ford[x, z]) return new Color(0.55f, 0.80f, 0.72f, 0.55f);
        if (d.Navigable[x, z]) return new Color(0.10f, 0.45f, 0.60f, 0.85f);
        if (d.River[x, z]) return new Color(0.35f, 0.66f, 0.80f, 0.70f);
        return new Color(0.13f, 0.30f, 0.55f, 0.80f);
    }

    public static Color Material(SurfaceMaterial m) => m switch
    {
        SurfaceMaterial.Stone => new Color(0.46f, 0.46f, 0.48f),
        SurfaceMaterial.Scree => new Color(0.62f, 0.60f, 0.55f),
        SurfaceMaterial.Snow => new Color(0.92f, 0.94f, 0.96f),
        SurfaceMaterial.Sand => new Color(0.85f, 0.78f, 0.55f),
        SurfaceMaterial.Silt => new Color(0.52f, 0.44f, 0.32f),
        SurfaceMaterial.Grass => new Color(0.36f, 0.56f, 0.26f),
        SurfaceMaterial.Meadow => new Color(0.50f, 0.64f, 0.30f),
        SurfaceMaterial.Heath => new Color(0.52f, 0.52f, 0.32f),
        _ => new Color(0.68f, 0.58f, 0.42f),          // Dust
    };

    /// <summary>An anchor kind (the constants above); 0 is unremarkable ground.</summary>
    public static Color Anchor(int kind) => kind switch
    {
        Coast => new Color(0.30f, 0.82f, 0.88f),
        Brink => new Color(0.88f, 0.28f, 0.24f),
        Overhang => new Color(0.88f, 0.35f, 0.85f),
        Beach => new Color(0.90f, 0.82f, 0.55f),
        Ford => new Color(0.55f, 0.92f, 0.45f),
        Landing => new Color(0.98f, 0.86f, 0.25f),
        Quay => new Color(0.35f, 0.55f, 0.95f),
        CliffFoot => new Color(0.90f, 0.55f, 0.20f),
        Bank => new Color(0.30f, 0.75f, 0.55f),
        Summit => new Color(1f, 1f, 1f),
        _ => new Color(0.26f, 0.26f, 0.27f),
    };
}
