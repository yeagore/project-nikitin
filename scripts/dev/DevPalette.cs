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

    public const int Coast = 1, Brink = 2, Overhang = 3, Beach = 4, Ford = 5,
                     Landing = 6, Quay = 7, CliffFoot = 8, Bank = 9, Summit = 10,
                     RiverBed = 11, LakeBed = 12, GooBed = 13, Ledge = 14;

    /// <summary>The anchor kinds in the order a legend reads them: shore, water, rock, built, high.</summary>
    public static readonly int[] LegendOrder =
    {
        Coast, Beach, Bank, RiverBed, LakeBed, GooBed, Ford, Quay,
        Brink, CliffFoot, Ledge, Overhang, Landing, Summit,
    };

    /// <summary>The two ends of each habitat axis's ramp, 0 then 255.</summary>
    public static readonly (Color Lo, Color Hi) MoistureRamp =
        (new Color(0.55f, 0.45f, 0.30f), new Color(0.10f, 0.52f, 0.62f));
    public static readonly (Color Lo, Color Hi) WarmthRamp =
        (new Color(0.88f, 0.92f, 1.00f), new Color(0.85f, 0.48f, 0.18f));
    public static readonly (Color Lo, Color Hi) RuggedRamp =
        (new Color(0.10f, 0.11f, 0.13f), new Color(0.95f, 0.88f, 0.70f));
    public static readonly (Color Lo, Color Hi) ExposureRamp =
        (new Color(0.14f, 0.30f, 0.20f), new Color(0.92f, 0.93f, 0.85f));
    public static readonly (Color Lo, Color Hi) RimRamp =
        (new Color(0.85f, 0.55f, 0.90f), new Color(0.10f, 0.12f, 0.22f));

    /// <summary>Standing fluid by kind: goo, then ford, navigable reach, stream, lake.</summary>
    public static Color Water(IslandData d, int x, int z)
    {
        if (d.Fluid[x, z] == (byte)FluidKind.Goo) return Goo;
        if (d.Ford[x, z]) return new Color(0.55f, 0.80f, 0.72f, 0.55f);
        if (d.Navigable[x, z]) return new Color(0.10f, 0.45f, 0.60f, 0.85f);
        if (d.River[x, z]) return new Color(0.35f, 0.66f, 0.80f, 0.70f);
        return new Color(0.13f, 0.30f, 0.55f, 0.80f);
    }

    /// <summary>Nine materials, no two of them neighbours on the colour wheel: heath is heather, dust is orange.</summary>
    public static Color Material(SurfaceMaterial m) => m switch
    {
        SurfaceMaterial.Stone => new Color(0.50f, 0.50f, 0.54f),
        SurfaceMaterial.Scree => new Color(0.76f, 0.70f, 0.64f),
        SurfaceMaterial.Snow => new Color(0.95f, 0.96f, 0.98f),
        SurfaceMaterial.Sand => new Color(0.93f, 0.86f, 0.55f),
        SurfaceMaterial.Silt => new Color(0.44f, 0.32f, 0.20f),
        SurfaceMaterial.Grass => new Color(0.28f, 0.62f, 0.22f),
        SurfaceMaterial.Meadow => new Color(0.66f, 0.82f, 0.34f),
        SurfaceMaterial.Heath => new Color(0.62f, 0.44f, 0.64f),
        _ => new Color(0.82f, 0.58f, 0.34f),          // Dust
    };

    /// <summary>An anchor kind (the constants above); 0 is unremarkable ground. The beds are dim: the water sits over them.</summary>
    public static Color Anchor(int kind) => kind switch
    {
        Coast => new Color(0.30f, 0.85f, 0.92f),
        Brink => new Color(0.92f, 0.24f, 0.20f),
        Overhang => new Color(0.90f, 0.35f, 0.88f),
        Beach => new Color(0.94f, 0.87f, 0.62f),
        Ford => new Color(0.88f, 1.00f, 0.45f),
        Landing => new Color(0.98f, 0.78f, 0.15f),
        Quay => new Color(0.30f, 0.50f, 1.00f),
        CliffFoot => new Color(0.96f, 0.58f, 0.16f),
        Bank => new Color(0.38f, 0.80f, 0.36f),
        Summit => new Color(1f, 1f, 1f),
        RiverBed => new Color(0.22f, 0.40f, 0.66f),
        LakeBed => new Color(0.14f, 0.44f, 0.50f),
        GooBed => new Color(0.42f, 0.12f, 0.52f),
        Ledge => new Color(0.98f, 0.72f, 0.58f),      // between the brink's red and the foot's orange
        _ => new Color(0.26f, 0.26f, 0.27f),
    };

    /// <summary>What an anchor kind is called in a legend.</summary>
    public static string AnchorName(int kind) => kind switch
    {
        Coast => "coast",
        Brink => "cliff brink",
        Overhang => "overhang lip",
        Beach => "beach",
        Ford => "ford",
        Landing => "gate landing",
        Quay => "ferry quay",
        CliffFoot => "cliff foot",
        Bank => "bank",
        Summit => "summit",
        RiverBed => "river bed",
        LakeBed => "lake bed",
        GooBed => "goo bed",
        Ledge => "brink and foot (a ledge)",
        _ => "unremarkable",
    };
}
