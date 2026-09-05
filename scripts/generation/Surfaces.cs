using System;
using System.Collections.Generic;

using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>
/// Collects the feature anchors (coast, cliff brinks and feet, banks, beds, summits)
/// and the provisional <see cref="SurfaceMaterial"/>. Everything is measured against
/// <see cref="IslandData.EffectiveLevel"/> — the water surface where a column is
/// flooded — otherwise every river bank reads as a cliff over its own bed. The
/// springs, the falls, the sea stacks and the deltas are anchors the water and
/// footprint stages wrote already.
/// </summary>
internal static class Surfaces
{
    /// <summary>Slabs of visible face that make a cliff — the traversal's own "needs a hoist".</summary>
    private const int CliffFace = 3;

    /// <summary>Slabs of face that bare the rock whatever the landform: a plateau rung or a mesa wall is not one, a mountain flank or a canyon is.</summary>
    private const int TallFace = 6;

    /// <summary>Ruggedness (32 per slab) at which a rock landform shows stone off its cliffs.</summary>
    private const int RockyStoneAt = 144;

    /// <summary>Ruggedness at which a rock landform shows scree.</summary>
    private const int RockyScreeAt = 96;

    /// <summary>Ruggedness at which soft ground turns to scree: seven slabs in five cells, which only stacked rungs manage.</summary>
    private const int BrokenAt = 224;

    // ---- the climate grid --------------------------------------------------
    // Warmth in four bands (frigid, cold, temperate, hot) and moisture in three,
    // with two cells for water in excess — bog on the cold-to-cool half of the
    // warmth range, marsh on the warm-to-hot half — and sand and snow past the ends.

    /// <summary>Warmth below which ground is frozen: the extreme cold, and a mountain above its tundra.</summary>
    private const int SnowBelow = 35;

    // The bands are placed on the knob: warmth is 60 + 180 × the knob on open
    // lowland, so frigid is a knob under about 0.14, cold under about 0.3, hot one
    // over about 0.7, and sand the last twentieth.

    /// <summary>Warmth below which the ground is frigid: tundra whatever the moisture, but for the bog.</summary>
    private const int FrigidBelow = 85;

    /// <summary>Warmth below which the ground is the cold band: tundra, heath, moorland.</summary>
    private const int ColdBelow = 115;

    /// <summary>
    /// Warmth from which the excess cell is marsh rather than bog: the warm part of
    /// the temperate band and everything hotter. Just under the knob's middle (150)
    /// less what the water's tempering takes off a wet bank, so a temperate Domain's
    /// riversides are warm-side and a cool one's (a knob of 0.4 and under) bog-side.
    /// </summary>
    private const int WarmFrom = 140;

    /// <summary>Warmth from which the ground is the hot band: dust, savanna, floodplain.</summary>
    private const int HotFrom = 185;

    /// <summary>Warmth from which hot ground is sand: the extreme heat. A floodplain still beats it.</summary>
    private const int SandFrom = 220;

    /// <summary>Moisture below which the ground is dry: dust, steppe, tundra.</summary>
    private const int DryBelow = 90;

    /// <summary>Moisture from which the ground is wet: floodplain, grass, bog.</summary>
    private const int WetFrom = 170;

    /// <summary>Cells from fresh water a hot floodplain reaches; wet hot ground further off is savanna.</summary>
    private const int FloodplainReach = 3;

    /// <summary>
    /// Warmth from which a wet riverside is floodplain: the hot line less what the
    /// water's tempering takes off a bank (135 + 0.7 × (185 − 135)), so the bank and
    /// the strip behind it read the same and a floodplain never starts a cell away
    /// from its river.
    /// </summary>
    private const int FloodplainFrom = 170;

    /// <summary>Moisture from which hot ground is verdure rather than savanna: a higher bar than grass, since heat is the less forgiving side.</summary>
    private const int HotWetFrom = 200;

    /// <summary>Moisture from which cold-to-cool ground may be bog: past wet, water in excess.</summary>
    private const int BogFrom = 190;

    /// <summary>The noise bar such ground must clear to be bog: in patches, and more of them than there are marshes, but a tenth of a wet cool Domain and not a fifth.</summary>
    private const float BogBar = 0.66f;

    /// <summary>Moisture from which warm-to-hot ground beside the water may be marsh: extreme, so a high background and the water's own strip both.</summary>
    private const int MarshFrom = 230;

    /// <summary>Cells from fresh water a marsh reaches.</summary>
    private const int MarshReach = 2;

    /// <summary>Ruggedness (32 per slab) a marsh tolerates: flat, give or take a slab, so the ground is low as well as near.</summary>
    private const int MarshFlat = 40;

    /// <summary>The noise bar such ground must clear to be marsh: occasional.</summary>
    private const float MarshBar = 0.62f;

    /// <summary>The noise bar a plain or a hillside must clear to show a tor: a small outcrop, rare.</summary>
    private const float TorBar = 0.87f;

    /// <summary>Rebuilds the anchor lists in scan order and picks every column's material.</summary>
    public static void Classify(int seed, IslandData d)
    {
        int n = d.Size;
        var bog = new Noise(seed + 71_019, 0.07f, octaves: 2);
        var marsh = new Noise(seed + 71_029, 0.09f, octaves: 2);
        var tor = new Noise(seed + 71_027, 0.16f, octaves: 1);
        // Cells from fresh water (goo waters nothing), as far as a floodplain reaches; -1 beyond.
        int[,] toWater = Flood.Distance(n,
            (x, z) => d.HasLand(x, z) && d.WaterLevel[x, z] != IslandData.NoLand
                      && d.Fluid[x, z] != (byte)FluidKind.Goo,
            (_, _, nx, nz) => d.HasLand(nx, nz),
            cap: FloodplainReach);

        d.CoastCells.Clear();
        d.CliffCells.Clear();
        d.CliffFootCells.Clear();
        d.BankCells.Clear();
        d.RiverBedCells.Clear();
        d.LakeBedCells.Clear();
        d.Summits.Clear();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            short eff = d.EffectiveLevel(x, z);
            bool dry = d.WaterLevel[x, z] == IslandData.NoLand;

            if (!dry)
            {
                if (d.River[x, z]) d.RiverBedCells.Add(new Vector2I(x, z));
                else if (d.Fluid[x, z] == (byte)FluidKind.Water) d.LakeBedCells.Add(new Vector2I(x, z));
            }

            bool coast = false, bank = false, gooSide = false;
            int drop = 0, face = 0;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz))
                {
                    coast = true;
                    continue;
                }
                short ne = d.EffectiveLevel(nx, nz);
                drop = Math.Max(drop, eff - ne);
                face = Math.Max(face, ne - eff);

                if (!dry || d.WaterLevel[nx, nz] == IslandData.NoLand) continue;
                if (d.Fluid[nx, nz] == (byte)FluidKind.Goo) gooSide = true;
                else if (eff - d.WaterLevel[nx, nz] is >= 0 and <= 1) bank = true;
            }

            if (coast) d.CoastCells.Add(new Vector2I(x, z));
            if (dry && drop >= CliffFace) d.CliffCells.Add(new Vector2I(x, z));
            if (dry && face >= CliffFace) d.CliffFootCells.Add(new Vector2I(x, z));
            if (bank && !d.Beach[x, z] && !d.Landings[x, z])
                d.BankCells.Add(new Vector2I(x, z));

            int near = toWater[x, z] < 0 ? int.MaxValue : toWater[x, z];
            d.Material[x, z] = (byte)Pick(d, x, z, drop, face, gooSide, near, bog, marsh, tor);
        }

        WipeStrandedFloodplain(d);
        FindSummits(d);
    }

    /// <summary>
    /// A floodplain is the flat beside the water: any patch of it that does not
    /// touch fresh water through other floodplain is savanna instead. One flood
    /// over the footprint, so it costs nothing worth measuring.
    /// </summary>
    private static void WipeStrandedFloodplain(IslandData d)
    {
        int n = d.Size;
        int[,] linked = Flood.Distance(n,
            (x, z) => d.HasLand(x, z) && d.WaterLevel[x, z] != IslandData.NoLand
                      && d.Fluid[x, z] != (byte)FluidKind.Goo,
            (_, _, nx, nz) => d.HasLand(nx, nz) && d.Material[nx, nz] == (byte)SurfaceMaterial.Floodplain);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.Material[x, z] == (byte)SurfaceMaterial.Floodplain && linked[x, z] < 0)
                d.Material[x, z] = (byte)SurfaceMaterial.Savanna;
    }

    /// <summary>
    /// The highest dry cells, greedily spaced. The minimum rise is absolute (half
    /// the mountain cap above the lowest ground), so a flat island has no summits.
    /// </summary>
    private static void FindSummits(IslandData d)
    {
        int n = d.Size;
        short low = short.MaxValue;
        var peaks = new List<Vector2I>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            short eff = d.EffectiveLevel(x, z);
            if (eff == IslandData.NoLand) continue;
            if (eff < low) low = eff;
            if (d.WaterLevel[x, z] == IslandData.NoLand) peaks.Add(new Vector2I(x, z));
        }
        if (peaks.Count == 0) return;

        float cap = Habitat.MountainCap(d.Size);
        int minRise = Math.Max(8, Mathf.RoundToInt(cap / 2f));
        int spacing = Math.Max(8, n / 8);

        peaks.Sort((a, b) => d.SurfaceLevel(b.X, b.Y).CompareTo(d.SurfaceLevel(a.X, a.Y)));

        foreach (Vector2I c in peaks)
        {
            if (d.SurfaceLevel(c.X, c.Y) - low < minRise) break;
            bool crowded = false;
            foreach (Vector2I had in d.Summits)
                if (Math.Abs(had.X - c.X) + Math.Abs(had.Y - c.Y) < spacing)
                {
                    crowded = true;
                    break;
                }
            if (!crowded) d.Summits.Add(c);
        }
    }

    /// <summary>Whether the landform is made of rock: the only ground that bares stone off a tall face, and the ground drought patches fall on.</summary>
    internal static bool Rocky(LandformType form)
        => form is LandformType.Mountain or LandformType.Massif or LandformType.Karst
                or LandformType.Badlands or LandformType.Sinkholes;

    /// <summary>
    /// The material at one cell. Beds first; goo's bed and shore are stone; then
    /// snow; then rock — a tall face bares stone at its brink and drops scree at its
    /// foot whatever the landform, and a rock landform shows stone and scree
    /// wherever it is broken, so a mountain is stone up to its snow; then the dunes
    /// and the sculpted rock; then a delta's fan, floodplain in any climate; then
    /// the tors, small outcrops of stone in soft country; then the water in excess
    /// — marsh on warm-to-hot ground, bog on cold-to-cool — and then the climate
    /// grid, warmth against moisture. A beach is ground like any other — nothing
    /// washes it — and a plateau rung in soft country changes nothing: the ground
    /// runs up to the edge.
    /// </summary>
    private static SurfaceMaterial Pick(IslandData d, int x, int z, int drop, int face,
                                        bool gooSide, int near, Noise bog, Noise marsh, Noise tor)
    {
        if (d.WaterLevel[x, z] != IslandData.NoLand)
            return d.Fluid[x, z] == (byte)FluidKind.Goo ? SurfaceMaterial.Stone : SurfaceMaterial.Silt;
        if (gooSide) return SurfaceMaterial.Stone;

        byte warmth = d.Warmth[x, z];
        if (warmth < SnowBelow) return SurfaceMaterial.Snow;

        byte rugged = d.Ruggedness[x, z];
        var form = (LandformType)d.Landform[x, z];
        bool rocky = Rocky(form) || d.Canyon[x, z];

        if (drop >= TallFace) return SurfaceMaterial.Stone;
        if (face >= TallFace) return SurfaceMaterial.Scree;        // talus under the face

        if (rocky && (drop >= CliffFace || face >= CliffFace || rugged >= RockyStoneAt))
            return SurfaceMaterial.Stone;
        if (rocky && rugged >= RockyScreeAt) return SurfaceMaterial.Scree;
        if (rugged >= BrokenAt) return SurfaceMaterial.Scree;

        if (form == LandformType.Dunes) return SurfaceMaterial.Sand;
        // Broken rock, not a desert: dust here put a hot-band ground in cold country.
        if (form is LandformType.Badlands or LandformType.Karst or LandformType.Sinkholes)
            return SurfaceMaterial.Scree;

        // A delta's fan is the river's own floodplain, whatever the climate says.
        if (d.Delta[x, z]) return SurfaceMaterial.Floodplain;

        // A tor: building stone in soft country, where no rock landform is.
        if (form is LandformType.Plain or LandformType.Hills && tor.At(x, z) > TorBar)
            return SurfaceMaterial.Stone;

        // Water in excess, in patches: marsh on the warm-to-hot half, bog on the
        // cold-to-cool half, and neither is the rule. A marsh wants extreme moisture
        // — a high background and the water's strip both — on flat ground beside the
        // water; a bog only asks for the excess, so there are more bogs than marshes.
        byte moist = d.Moisture[x, z];
        bool wet = moist >= WetFrom, dryGround = moist < DryBelow;
        if (warmth >= WarmFrom)
        {
            if (moist >= MarshFrom && near <= MarshReach && rugged <= MarshFlat
                && marsh.At(x, z) > MarshBar)
                return SurfaceMaterial.Marsh;
        }
        else if (moist >= BogFrom && bog.At(x, z) > BogBar) return SurfaceMaterial.Bog;

        // The climate grid. A mountain is stone and scree up to its snow; the cold
        // bands are for the ground that is not rock. Frigid ground is tundra
        // whatever the moisture; the cold band splits tundra, heath, moorland.
        if (warmth < FrigidBelow) return SurfaceMaterial.Tundra;
        if (warmth < ColdBelow)
        {
            if (dryGround) return SurfaceMaterial.Tundra;
            return wet ? SurfaceMaterial.Moorland : SurfaceMaterial.Heath;
        }

        if (wet && near <= FloodplainReach && warmth >= FloodplainFrom) return SurfaceMaterial.Floodplain;
        if (warmth >= HotFrom)
        {
            // Hot and wet enough: verdure, the hot row's grass, and it beats the sand as the floodplain does.
            if (moist >= HotWetFrom) return SurfaceMaterial.Verdure;
            if (warmth >= SandFrom) return SurfaceMaterial.Sand;
            return dryGround ? SurfaceMaterial.Dust : SurfaceMaterial.Savanna;
        }
        if (wet) return SurfaceMaterial.Grass;
        return dryGround ? SurfaceMaterial.Steppe : SurfaceMaterial.Meadow;
    }
}
