using System;
using System.Collections.Generic;

using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>
/// Collects the feature anchors (coast, cliff brinks and feet, banks, summits) and
/// the provisional <see cref="SurfaceMaterial"/>. Everything is measured against
/// <see cref="IslandData.EffectiveLevel"/> — the water surface where a column is
/// flooded — otherwise every river bank reads as a cliff over its own bed.
/// </summary>
internal static class Surfaces
{
    /// <summary>Slabs of visible face that make a cliff — the traversal's own "needs a hoist".</summary>
    private const int CliffFace = 3;

    /// <summary>Warmth below which ground is frozen.</summary>
    private const int SnowAt = 64;

    /// <summary>Warmth below which nothing ordinary grows — the alpine band.</summary>
    private const int ColdAt = 110;

    /// <summary>Ruggedness at which alpine ground is scree rather than stone.</summary>
    private const int AlpineScreeAt = 64;

    /// <summary>Moisture above which dry ground is a wet margin even off a bank.</summary>
    private const int SiltAt = 205;

    /// <summary>Moisture above which ground is grass.</summary>
    private const int GrassAt = 140;

    /// <summary>Moisture above which ground is meadow.</summary>
    private const int MeadowAt = 60;

    /// <summary>Moisture above which ground is heath; below it, parched dust.</summary>
    private const int HeathAt = 10;

    /// <summary>Ruggedness at which temperate ground turns to scree.</summary>
    private const int BrokenAt = 160;

    /// <summary>Rebuilds the anchor lists in scan order and picks every column's material.</summary>
    public static void Classify(IslandData d)
    {
        int n = d.Size;

        d.CoastCells.Clear();
        d.CliffCells.Clear();
        d.CliffFootCells.Clear();
        d.BankCells.Clear();
        d.Summits.Clear();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            short eff = d.EffectiveLevel(x, z);
            bool dry = d.WaterLevel[x, z] == IslandData.NoLand;

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

    /// <summary>The material at one cell: built and wet first, then rock, cold, landform, moisture.</summary>
    private static SurfaceMaterial Pick(IslandData d, int x, int z, int drop, int face, bool bank)
    {
        if (d.Beach[x, z]) return SurfaceMaterial.Sand;
        if (d.WaterLevel[x, z] != IslandData.NoLand) return SurfaceMaterial.Silt;

        byte warmth = d.Warmth[x, z];

        if (drop >= CliffFace || face >= CliffFace)
            return warmth < SnowAt ? SurfaceMaterial.Snow : SurfaceMaterial.Stone;

        if (warmth < SnowAt) return SurfaceMaterial.Snow;
        if (warmth < ColdAt)
            return d.Ruggedness[x, z] >= AlpineScreeAt ? SurfaceMaterial.Scree : SurfaceMaterial.Stone;

        var form = (LandformType)d.Landform[x, z];
        if (form == LandformType.Dunes) return SurfaceMaterial.Sand;
        if (form is LandformType.Badlands or LandformType.Karst or LandformType.Sinkholes)
            return SurfaceMaterial.Dust;

        if (bank) return SurfaceMaterial.Silt;
        if (d.Ruggedness[x, z] >= BrokenAt) return SurfaceMaterial.Scree;

        byte moist = d.Moisture[x, z];
        if (moist >= SiltAt) return SurfaceMaterial.Silt;
        if (moist >= GrassAt) return SurfaceMaterial.Grass;
        if (moist >= MeadowAt) return SurfaceMaterial.Meadow;
        if (moist >= HeathAt) return SurfaceMaterial.Heath;
        return SurfaceMaterial.Dust;
    }
}
