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
                     RiverBed = 11, LakeBed = 12, GooBed = 13, Ledge = 14,
                     Spring = 15, FallLip = 16, SeaStack = 17, HotSpring = 18;

    /// <summary>The anchor kinds in the order a legend reads them: shore, water, rock, built, high, and the stacks off the coast.</summary>
    public static readonly int[] LegendOrder =
    {
        Coast, Beach, Bank, RiverBed, LakeBed, GooBed, Spring, HotSpring, FallLip, Ford, Quay,
        Brink, CliffFoot, Ledge, Overhang, Landing, Summit, SeaStack,
    };

    /// <summary>The landform view's colours: plains green, hills darker, mountain grey, mesa rust, basin blue, the sculpted ones their own.</summary>
    public static Color Landform(LandformType type) => type switch
    {
        LandformType.Plain => new Color(0.45f, 0.60f, 0.28f),
        LandformType.Hills => new Color(0.30f, 0.44f, 0.20f),
        LandformType.Mountain => new Color(0.52f, 0.50f, 0.55f),
        LandformType.Mesa => new Color(0.68f, 0.45f, 0.26f),
        LandformType.Basin => new Color(0.28f, 0.40f, 0.52f),
        LandformType.Badlands => new Color(0.72f, 0.56f, 0.34f),
        LandformType.Karst => new Color(0.58f, 0.66f, 0.62f),
        LandformType.Massif => new Color(0.62f, 0.42f, 0.48f),
        LandformType.Dunes => new Color(0.80f, 0.74f, 0.46f),
        LandformType.Sinkholes => new Color(0.50f, 0.58f, 0.44f),
        _ => new Color(0.5f, 0.5f, 0.5f),
    };

    /// <summary>The walk view's greens and greys: the mainland, a hue per other district, one grey for broken ground.</summary>
    public static readonly Color Mainland = new(0.42f, 0.62f, 0.28f);
    public static readonly Color Broken = new(0.34f, 0.34f, 0.36f);
    public static readonly Color WalkWater = new(0.16f, 0.34f, 0.52f);
    public static Color District(int id) => Color.FromHsv((0.08f + id * 0.61803399f) % 1f, 0.62f, 0.88f);

    /// <summary>The height view's ramp: deep dirt, then grass, then highlands.</summary>
    public static readonly Color HeightLow = new(0.24f, 0.20f, 0.13f);
    public static readonly Color HeightMid = new(0.30f, 0.42f, 0.18f);
    public static readonly Color HeightHigh = new(0.66f, 0.72f, 0.52f);

    /// <summary>The height ramp at <paramref name="t"/>, 0 the lowest ground and 1 the highest.</summary>
    public static Color Height(float t)
        => t < 0.5f ? HeightLow.Lerp(HeightMid, t * 2f)
                    : HeightMid.Lerp(HeightHigh, (t - 0.5f) * 2f);

    /// <summary>The two ends of each habitat axis's ramp, 0 then 255.</summary>
    public static readonly (Color Lo, Color Hi) MoistureRamp =
        (new Color(0.55f, 0.45f, 0.30f), new Color(0.10f, 0.52f, 0.62f));
    public static readonly (Color Lo, Color Hi) RuggedRamp =
        (new Color(0.10f, 0.11f, 0.13f), new Color(0.95f, 0.88f, 0.70f));
    public static readonly (Color Lo, Color Hi) ExposureRamp =
        (new Color(0.14f, 0.30f, 0.20f), new Color(0.92f, 0.93f, 0.85f));
    public static readonly (Color Lo, Color Hi) RimRamp =
        (new Color(0.85f, 0.55f, 0.90f), new Color(0.10f, 0.12f, 0.22f));

    /// <summary>Water distance: teal at the bank, dry earth where no fresh water is within reach.</summary>
    public static readonly (Color Lo, Color Hi) WaterRamp =
        (new Color(0.16f, 0.58f, 0.70f), new Color(0.38f, 0.28f, 0.20f));

    /// <summary>Magickal density: inert indigo to a saturated, luminous violet.</summary>
    public static readonly (Color Lo, Color Hi) MagickRamp =
        (new Color(0.10f, 0.08f, 0.22f), new Color(0.98f, 0.62f, 1.00f));

    /// <summary>A sea stack's column in the lab: darker than any stone the island shows.</summary>
    public static readonly Color StackTint = new(0.24f, 0.23f, 0.27f);

    /// <summary>The four kinds of standing water, named so a legend can show them without an island to sample.</summary>
    public static readonly Color FordTint = new(0.55f, 0.80f, 0.72f, 0.55f);
    public static readonly Color ReachTint = new(0.10f, 0.45f, 0.60f, 0.85f);
    public static readonly Color StreamTint = new(0.35f, 0.66f, 0.80f, 0.70f);
    public static readonly Color LakeTint = new(0.13f, 0.30f, 0.55f, 0.80f);

    /// <summary>Hot water: a spring or a pool that runs warm on a cold Domain.</summary>
    public static readonly Color HotTint = new(0.96f, 0.56f, 0.38f, 0.85f);

    /// <summary>Standing fluid by kind: goo, then hot water, ford, navigable reach, stream, lake.</summary>
    public static Color Water(IslandData d, int x, int z)
    {
        if (d.Fluid[x, z] == (byte)FluidKind.Goo) return Goo;
        if (d.Hot[x, z]) return HotTint;
        if (d.Ford[x, z]) return FordTint;
        if (d.Navigable[x, z]) return ReachTint;
        if (d.River[x, z]) return StreamTint;
        return LakeTint;
    }

    /// <summary>
    /// Seventeen materials. The climate grid reads as a grid: the cold row is
    /// mint, heather-brown, mauve and a dark bog; the temperate row straw,
    /// yellow-green, green and a blue-green marsh; the hot row red-brown, gold, a
    /// deep verdure and the emerald floodplain. Sand pale, snow white, silt brown.
    /// </summary>
    public static Color Material(SurfaceMaterial m) => m switch
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
        SurfaceMaterial.Marsh => new Color(0.30f, 0.52f, 0.50f),        // blue-green, duller than grass, lighter than bog
        SurfaceMaterial.Heath => new Color(0.58f, 0.42f, 0.40f),        // heather-brown, between the mint and the mauve
        SurfaceMaterial.Verdure => new Color(0.08f, 0.42f, 0.20f),      // the deepest green: darker than grass, purer than bog
        _ => new Color(1f, 0f, 1f),                    // an unmapped member: make it shout
    };

    /// <summary>
    /// Warmth as a colour with a stop at each band line: ice white below the snow
    /// line, steel blue through the cold band, pale yellow at the temperate middle,
    /// orange at the hot line, deep red at sand. A plain ramp put a whole lowland
    /// in one shade.
    /// </summary>
    public static Color WarmthTint(byte warmth)
    {
        var stops = new (float At, Color C)[]
        {
            (0f, new Color(0.92f, 0.95f, 1.00f)),
            (35f, new Color(0.75f, 0.85f, 0.98f)),
            (100f, new Color(0.42f, 0.55f, 0.82f)),
            (135f, new Color(0.96f, 0.90f, 0.62f)),
            (175f, new Color(0.92f, 0.60f, 0.20f)),
            (255f, new Color(0.70f, 0.18f, 0.08f)),
        };
        for (int i = 1; i < stops.Length; i++)
        {
            if (warmth > stops[i].At) continue;
            float t = (warmth - stops[i - 1].At) / (stops[i].At - stops[i - 1].At);
            return stops[i - 1].C.Lerp(stops[i].C, t);
        }
        return stops[^1].C;
    }

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
        Spring => new Color(0.62f, 0.95f, 1.00f),     // a pale spark at the head of a stream
        FallLip => new Color(0.80f, 0.90f, 1.00f),    // white water
        SeaStack => StackTint,
        HotSpring => new Color(1.00f, 0.50f, 0.20f),  // steam-orange
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
        Spring => "spring",
        FallLip => "fall",
        SeaStack => "sea stack (in the aether)",
        HotSpring => "hot spring or pool",
        _ => "unremarkable",
    };
}
