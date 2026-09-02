using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>The free step: the slope limiter and the ambiguous-step resolver that settle a surface under the one-slab rule.</summary>
internal static class StepGrammar
{
    /// <summary>
    /// The lowest a cell beside a basin may be cut to and keep the escarpment
    /// facing the right way: a cliff's height above the floor it looks down on.
    /// </summary>
    internal static int BasinFloorNear(bool[,] land, short[,] surface, int[,] region,
                                      RegionPlan[] plan, int n, int x, int z)
    {
        int floor = int.MinValue;
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int nx = x + dx, nz = z + dz;
            if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
            if (plan[region[nx, nz]].Type != LandformType.Basin) continue;
            floor = Math.Max(floor, surface[nx, nz] + 3);
        }
        return floor;
    }

    /// <summary>
    /// Whether the slope limit reaches across a region border, i.e. no cliff belongs
    /// there: a shared rung group, and neither side a mountain, mesa or basin.
    /// </summary>
    private static bool BorderIsBound(RegionPlan a, RegionPlan b)
    {
        if (a.Type == LandformType.Mountain || b.Type == LandformType.Mountain) return false;
        if (a.Type is LandformType.Mesa or LandformType.Basin) return false;
        if (b.Type is LandformType.Mesa or LandformType.Basin) return false;
        return a.RungGroup == b.RungGroup;
    }

    /// <summary>
    /// Lowers any cell standing more than its region's slope limit above a
    /// neighbour until none does; it only lowers, so it converges. It reaches
    /// across a border where <see cref="BorderIsBound"/> or both cells are pass
    /// ground. Cells in <paramref name="exempt"/> are neither lowered nor used as a
    /// bound, or a lake bed or canyon floor would drag its whole rung group down.
    /// </summary>
    /// <returns>Whether anything was lowered.</returns>
    internal static bool LimitSlope(short[,] h, int[,] region, bool[,] land, RegionPlan[] plan,
                                   bool[,]? exempt = null, bool[,]? saddle = null)
    {
        int n = h.GetLength(0);
        bool any = false;
        for (int pass = 0; pass < 48; pass++)
        {
            bool changed = false;
            bool forward = (pass & 1) == 0;

            for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                int x = forward ? a : n - 1 - a;
                int z = forward ? b : n - 1 - b;
                if (!land[x, z]) continue;
                if (exempt != null && exempt[x, z]) continue;

                int r = region[x, z];
                if (plan[r].Type == LandformType.Mountain) continue;

                int limit = Landforms.SlopeLimit(plan[r].Type);
                int cap = int.MaxValue;

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!land[nx, nz]) continue;
                    if (exempt != null && exempt[nx, nz]) continue;

                    int rn = region[nx, nz];
                    // A pass exists to be walked across, so the limiter reaches over its border.
                    bool joined = saddle != null && saddle[x, z] && saddle[nx, nz];
                    if (rn != r && !joined && !BorderIsBound(plan[r], plan[rn])) continue;
                    cap = Math.Min(cap, h[nx, nz] + limit);
                }

                if (cap != int.MaxValue && cap < h[x, z]) { h[x, z] = (short)cap; changed = true; }
            }
            if (!changed) break;
            any = true;
        }
        return any;
    }

    /// <summary>
    /// Removes two-slab steps outside mountains: too tall to walk, too short to
    /// read as a cliff. A cell is never lowered into its own lake or to within a
    /// cliff of a basin floor it looks down on.
    /// </summary>
    /// <returns>Whether anything was lowered.</returns>
    internal static bool ResolveAmbiguousSteps(short[,] h, int[,] region, bool[,] land,
                                              RegionPlan[] plan, short[,]? water = null,
                                              bool[,]? exempt = null)
    {
        int n = h.GetLength(0);
        bool any = false;
        for (int pass = 0; pass < 16; pass++)
        {
            bool changed = false;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!land[x, z] || plan[region[x, z]].Type == LandformType.Mountain) continue;
                if (water != null && water[x, z] != IslandData.NoLand) continue;   // lake bed
                if (exempt != null && exempt[x, z]) continue;                       // cut on purpose

                int keepAbove = plan[region[x, z]].Type == LandformType.Basin
                    ? int.MinValue
                    : BasinFloorNear(land, h, region, plan, n, x, z);
                if (water != null)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        int wx = x + Dx[k], wz = z + Dz[k];
                        if (!InBounds(n, wx, wz)) continue;
                        if (water[wx, wz] != IslandData.NoLand)
                            keepAbove = Math.Max(keepAbove, water[wx, wz] + 1);
                    }
                }

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!land[nx, nz] || plan[region[nx, nz]].Type == LandformType.Mountain) continue;
                    if (exempt != null && exempt[nx, nz]) continue;

                    if (h[x, z] - h[nx, nz] == 2 && h[x, z] - 1 >= keepAbove)
                    {
                        h[x, z]--;
                        changed = true;
                    }
                }
            }
            if (!changed) break;
            any = true;
        }
        return any;
    }
}
