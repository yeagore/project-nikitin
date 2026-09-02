using System;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>Gentle coasts stepped down a slab, so a shallow shore meets the aether instead of stopping at a cliff.</summary>
internal static class Beaches
{
    /// <summary>Cells of coast a beach takes. Berth placement does not read beaches.</summary>
    private const int BeachWidth = 2;

    /// <summary>
    /// Steps the outermost cells of a gentle coast down one slab and flags them in
    /// <paramref name="beach"/>. The drop is band-wise and tapered, so the only new
    /// step is the free one; steep coasts, table rims and flooded cells are left alone.
    /// </summary>
    internal static void MakeBeaches(bool[,] land, short[,] surface, short[,] water,
                                    int[,] region, RegionPlan[] plan, bool[,] beach)
    {
        int n = land.GetLength(0);
        int[,] toRim = Flood.Distance(n,
            (x, z) => land[x, z] && AtRim(land, n, x, z),
            (_, _, nx, nz) => land[nx, nz],
            cap: BeachWidth);

        // One slab, not a ramp: a two-slab beach spends a landing strip's whole tolerance.
        var drop = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z] || toRim[x, z] < 0 || toRim[x, z] >= BeachWidth) continue;
            if (water[x, z] != IslandData.NoLand) continue;

            LandformType type = plan[region[x, z]].Type;
            if (type is not (LandformType.Plain or LandformType.Hills or LandformType.Dunes))
                continue;

            // Gentle: dry, and no cell in the band more than a slab off its neighbours.
            bool even = true;
            for (int k = 0; k < 4 && even; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                even = Math.Abs(surface[nx, nz] - surface[x, z]) <= 1
                       && water[nx, nz] == IslandData.NoLand;
            }
            if (even) drop[x, z] = 1;
        }
        FieldOps.Taper(drop, land);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (drop[x, z] <= 0) continue;
            surface[x, z] = Terrain.SlabClamp(surface[x, z] - drop[x, z]);
            beach[x, z] = true;
        }
    }

    /// <summary>Whether a cell has aether, or the grid's edge, beside it.</summary>
    private static bool AtRim(bool[,] land, int n, int x, int z)
    {
        for (int k = 0; k < 4; k++)
        {
            int nx = x + Dx[k], nz = z + Dz[k];
            if (!(InBounds(n, nx, nz) && land[nx, nz])) return true;
        }
        return false;
    }
}
