using System;
using System.Collections.Generic;

using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>
/// Collects the feature anchors (coast, cliff brinks and feet, banks, beds, summits)
/// and the provisional <see cref="SurfaceMaterial"/>. Everything is measured against
/// <see cref="IslandData.EffectiveLevel"/> — the water surface where a column is
/// flooded — otherwise every river bank reads as a cliff over its own bed.
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
    // Warmth in three bands and moisture in three, nine living grounds between
    // them, with sand and snow past the ends.

    /// <summary>Warmth below which ground is frozen: the extreme cold, and a mountain above its tundra.</summary>
    private const int SnowBelow = 35;

    /// <summary>Warmth below which the ground is the cold band: tundra, moorland, bog.</summary>
    private const int ColdBelow = 100;

    /// <summary>Warmth from which the ground is the hot band: dust, savanna, floodplain.</summary>
    private const int HotFrom = 175;

    /// <summary>Warmth from which hot ground is sand: the extreme heat. A floodplain still beats it.</summary>
    private const int SandFrom = 205;

    /// <summary>Moisture below which the ground is dry: dust, steppe, tundra.</summary>
    private const int DryBelow = 90;

    /// <summary>Moisture from which the ground is wet: floodplain, grass, bog.</summary>
    private const int WetFrom = 170;

    /// <summary>Cells from fresh water a hot floodplain reaches; wet hot ground further off is savanna.</summary>
    private const int FloodplainReach = 4;

    /// <summary>The noise bar cold wet ground must clear to be bog rather than moorland: occasional, not the rule.</summary>
    private const float BogBar = 0.6f;

    /// <summary>Rebuilds the anchor lists in scan order and picks every column's material.</summary>
    public static void Classify(int seed, IslandData d)
    {
        int n = d.Size;
        var bog = new Noise(seed + 71_019, 0.07f, octaves: 2);
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
            d.Material[x, z] = (byte)Pick(d, x, z, drop, face, gooSide, near, bog);
        }

        FindSummits(d);
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
    /// The material at one cell. Beds and beaches first; goo's bed and shore are
    /// stone; then snow; then rock — a tall face bares stone at its brink and drops
    /// scree at its foot whatever the landform, and a rock landform shows stone and
    /// scree wherever it is broken, so a mountain is stone up to its snow; then the
    /// dunes and the dry sculpted landforms; then the climate grid, warmth against
    /// moisture. A plateau rung in soft country changes nothing: the ground runs
    /// up to the edge.
    /// </summary>
    private static SurfaceMaterial Pick(IslandData d, int x, int z, int drop, int face,
                                        bool gooSide, int near, Noise bog)
    {
        if (d.Beach[x, z]) return SurfaceMaterial.Sand;
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
        if (form is LandformType.Badlands or LandformType.Karst or LandformType.Sinkholes)
            return SurfaceMaterial.Dust;

        // The climate grid. A mountain is stone and scree up to its snow; the cold
        // band is for the ground that is not rock.
        byte moist = d.Moisture[x, z];
        bool wet = moist >= WetFrom, dryGround = moist < DryBelow;

        if (warmth < ColdBelow)
        {
            if (wet && bog.At(x, z) > BogBar) return SurfaceMaterial.Bog;
            return dryGround ? SurfaceMaterial.Tundra : SurfaceMaterial.Moorland;
        }

        if (warmth >= HotFrom)
        {
            if (wet && near <= FloodplainReach) return SurfaceMaterial.Floodplain;
            if (warmth >= SandFrom) return SurfaceMaterial.Sand;
            return dryGround ? SurfaceMaterial.Dust : SurfaceMaterial.Savanna;
        }
        if (wet) return SurfaceMaterial.Grass;
        return dryGround ? SurfaceMaterial.Steppe : SurfaceMaterial.Meadow;
    }
}
