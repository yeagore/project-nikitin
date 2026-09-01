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
    /// The lowest a cell beside a basin may be cut to and leave the escarpment
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
    /// Projects each region's surface onto the largest field that never rises more
    /// than its slope limit between neighbours (a Lipschitz projection from above:
    /// it only lowers cells, so it converges). Region borders are excluded, which
    /// is what leaves the plateau gaps standing as cliffs.
    /// </summary>
    /// <summary>
    /// Whether the step between two regions is bound by the slope limit — that is,
    /// whether a cliff is forbidden here.
    ///
    /// Sharing a rung group <i>is</i> the statement "no cliff belongs on this
    /// border", so that is the test. Everything else is a cliff somebody asked
    /// for: two rung groups are the plateau ladder, a mesa or basin border is its
    /// own escarpment, and a mountain flank is the mountain.
    /// </summary>
    private static bool BorderIsBound(RegionPlan a, RegionPlan b)
    {
        if (a.Type == LandformType.Mountain || b.Type == LandformType.Mountain) return false;
        if (a.Type is LandformType.Mesa or LandformType.Basin) return false;
        if (b.Type is LandformType.Mesa or LandformType.Basin) return false;
        return a.RungGroup == b.RungGroup;
    }

    /// <summary>
    /// Lipschitz projection from above: repeatedly lower any cell standing more
    /// than its region's slope limit above a neighbour. It only ever lowers, so
    /// it converges.
    ///
    /// It reaches <b>across</b> a region border wherever <see cref="BorderIsBound"/>
    /// allows. Sharing a rung equalises a border's <i>base</i>, but a hills patch
    /// carries more relief than the plain beside it, and blurring the amplitude
    /// field narrows that gap without closing it — which is where the handful of
    /// hills cliffs the rules forbid were coming from. Enforcing the limit on the
    /// border itself closes it by construction rather than by tuning.
    ///
    /// Cells flagged in <paramref name="exempt"/> are neither lowered nor used as
    /// a bound. Two features need that: a lake bed sits three or four slabs under
    /// its own shore, and a canyon floor seven under its lip — take either as a
    /// bound and the limiter drags the whole rung group down into it a slab per
    /// cell, which is how plains ended up below the basins they border.
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
                    // A pass is the one place a cliff border is deliberately bound:
                    // the saddle exists precisely so you can walk across it, so the
                    // limiter has to reach over the border there.
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
    /// Removes two-slab steps outside mountains. Two is the worst height a step
    /// can be: too tall to walk, too short to read as a cliff, so it is neither
    /// free movement nor a deliberate obstacle.
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
                // A gully floor, a tower top, a canyon bed: cut on purpose, and
                // neither resolved away nor measured against.
                if (exempt != null && exempt[x, z]) continue;

                // A shore may not be lowered into its own lake, and ground beside a
                // basin may not be lowered to within a cliff of the floor it looks
                // down on — an escarpment resolved away is a basin deleted.
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
