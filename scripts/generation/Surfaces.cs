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

    /// <summary>Slabs of face that bare the rock whatever the landform: a plateau rung is not one, a mesa wall, a mountain flank or a canyon is.</summary>
    private const int TallFace = 5;

    /// <summary>Ruggedness (32 per slab) at which a rock landform shows stone off its cliffs.</summary>
    private const int RockyStoneAt = 96;

    /// <summary>Ruggedness at which a rock landform shows scree.</summary>
    private const int RockyScreeAt = 64;

    /// <summary>Warmth below which ground is frozen.</summary>
    private const int SnowAt = 64;

    /// <summary>Warmth below which nothing ordinary grows — the alpine band.</summary>
    private const int ColdAt = 110;

    /// <summary>Ruggedness at which alpine ground is scree rather than stone.</summary>
    private const int AlpineScreeAt = 64;

    /// <summary>Moisture above which dry ground is a wet margin even off a bank.</summary>
    private const int SiltAt = 220;

    /// <summary>Moisture above which warm ground is floodplain.</summary>
    private const int FloodplainAt = 170;

    /// <summary>Moisture above which cool ground is peatland.</summary>
    private const int PeatAt = 130;

    /// <summary>Warmth above which wet ground is lush and dry ground bakes.</summary>
    private const int WarmAt = 208;

    /// <summary>Warmth below which wet ground is bog rather than grass.</summary>
    private const int CoolAt = 206;

    /// <summary>Moisture a degree of warmth over <see cref="WarmAt"/> adds to what grass and meadow need: warm dry ground bakes.</summary>
    private const int BakeRate = 1;

    /// <summary>Moisture above which ground is grass.</summary>
    private const int GrassAt = 125;

    /// <summary>Moisture above which ground is meadow.</summary>
    private const int MeadowAt = 55;

    /// <summary>Moisture above which ground is moorland; below it, parched dust.</summary>
    private const int MoorAt = 12;

    /// <summary>Ruggedness at which soft ground turns to scree: six slabs in five cells, which only stacked rungs manage.</summary>
    private const int BrokenAt = 200;

    /// <summary>Rebuilds the anchor lists in scan order and picks every column's material.</summary>
    public static void Classify(IslandData d)
    {
        int n = d.Size;

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

            bool coast = false, bank = false;
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

                if (dry && d.WaterLevel[nx, nz] != IslandData.NoLand
                    && d.Fluid[nx, nz] != (byte)FluidKind.Goo
                    && eff - d.WaterLevel[nx, nz] is >= 0 and <= 1)
                    bank = true;
            }

            if (coast) d.CoastCells.Add(new Vector2I(x, z));
            if (dry && drop >= CliffFace) d.CliffCells.Add(new Vector2I(x, z));
            if (dry && face >= CliffFace) d.CliffFootCells.Add(new Vector2I(x, z));
            if (bank && !d.Beach[x, z] && !d.Landings[x, z])
                d.BankCells.Add(new Vector2I(x, z));

            d.Material[x, z] = (byte)Pick(d, x, z, drop, face, bank);
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

    /// <summary>Whether the landform is made of rock: the only ground that bares stone off a tall face.</summary>
    private static bool Rocky(LandformType form)
        => form is LandformType.Mountain or LandformType.Massif or LandformType.Karst
                or LandformType.Badlands or LandformType.Sinkholes;

    /// <summary>
    /// The material at one cell: built and wet first, then snow, then rock — a tall
    /// face bares stone at its brink and drops scree at its foot anywhere, and a rock
    /// landform shows stone and scree wherever it is broken — then the cold band,
    /// the landform, the bank, and last the moisture ladder read against the warmth. A plateau rung
    /// in soft country changes nothing: the ground runs up to the edge.
    /// </summary>
    private static SurfaceMaterial Pick(IslandData d, int x, int z, int drop, int face, bool bank)
    {
        if (d.Beach[x, z]) return SurfaceMaterial.Sand;
        if (d.WaterLevel[x, z] != IslandData.NoLand) return SurfaceMaterial.Silt;

        byte warmth = d.Warmth[x, z];
        if (warmth < SnowAt) return SurfaceMaterial.Snow;

        byte rugged = d.Ruggedness[x, z];
        var form = (LandformType)d.Landform[x, z];
        bool rocky = Rocky(form) || d.Canyon[x, z];

        if (drop >= TallFace) return SurfaceMaterial.Stone;
        if (face >= TallFace) return SurfaceMaterial.Scree;        // talus under the face

        if (rocky)
        {
            if (drop >= CliffFace || face >= CliffFace || rugged >= RockyStoneAt)
                return SurfaceMaterial.Stone;
            if (rugged >= RockyScreeAt) return SurfaceMaterial.Scree;
        }

        if (warmth < ColdAt)
            return rugged >= AlpineScreeAt ? SurfaceMaterial.Scree : SurfaceMaterial.Stone;

        if (form == LandformType.Dunes) return SurfaceMaterial.Sand;
        if (form is LandformType.Badlands or LandformType.Karst or LandformType.Sinkholes)
            return SurfaceMaterial.Dust;

        if (bank) return SurfaceMaterial.Silt;
        if (rugged >= BrokenAt) return SurfaceMaterial.Scree;

        // The moisture ladder, read against the warmth: warm and wet is lush, cool
        // and wet is bog, warm and dry bakes.
        byte moist = d.Moisture[x, z];
        if (moist >= FloodplainAt && warmth >= WarmAt) return SurfaceMaterial.Floodplain;
        if (moist >= PeatAt && warmth < CoolAt) return SurfaceMaterial.Peatland;
        if (moist >= SiltAt) return SurfaceMaterial.Silt;

        int bake = Math.Max(0, warmth - WarmAt) * BakeRate;
        if (moist >= GrassAt + bake) return SurfaceMaterial.Grass;
        if (moist >= MeadowAt + bake) return SurfaceMaterial.Meadow;
        if (moist >= MoorAt) return SurfaceMaterial.Moorland;
        return SurfaceMaterial.Dust;
    }
}
