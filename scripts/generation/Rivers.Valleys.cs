using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>Valleys: the ground either side of a course sunk in whole bands, the channel one band deeper.</summary>
internal static partial class Rivers
{
    /// <summary>Cells either side of a course the valley reaches, at full strength.</summary>
    private const int ValleyReach = 5;

    /// <summary>
    /// Bands of valley a course carries: the full <paramref name="reach"/> where a
    /// barge could work it, one less for a brook.
    /// </summary>
    private static int Budget(int reach, bool navigable)
        => reach <= 0 ? 0 : navigable ? reach : Math.Max(1, reach - 1);

    /// <summary>
    /// Labels each 4-connected component of the drawn channel — one river and its
    /// tributaries down to the rim, not a true catchment. Ids are in scan order of
    /// first encounter and are a hash salt, so the order is load-bearing.
    /// </summary>
    private static int[,] LabelBasins(int n, bool[,] river, out int count)
    {
        var basin = new int[n, n];
        count = Flood.Label(n, (x, z) => river[x, z], basin);
        return basin;
    }

    /// <summary>
    /// Whether a valley may take this cell down with it: soft ground, not a
    /// bridgehead (levelled for a deck, so pinned like a mesa rim), and not under
    /// standing water. A channel counts — the river sinks with its own valley.
    /// </summary>
    private static bool Sinkable(byte[,] form, bool[,] river, short[,] water, bool[,] keep,
                                 int x, int z)
    {
        if (keep[x, z]) return false;
        if (river[x, z]) return true;
        if (water[x, z] != IslandData.NoLand) return false;
        var type = (LandformType)form[x, z];
        return type is LandformType.Plain or LandformType.Hills or LandformType.Dunes;
    }

    /// <summary>
    /// Sinks the ground either side of a watercourse so it runs along the bottom
    /// of something. It goes down in whole bands — every cell at one distance from
    /// the water drops the same amount — so the only step it makes is the one slab
    /// between bands, and the channel sinks one band deeper than its bank, which
    /// is what makes a valley rather than a moat. Ground whose height is the
    /// landform, bridgeheads and standing water are left where they are.
    /// </summary>
    private static void CutValleys(int seed, IslandParams p, int n, bool[,] land,
                                   short[,] surface, short[,] water, bool[,] river,
                                   bool[,] navigable, byte[,] form, bool[,] keep,
                                   Vector2I[,] twin)
    {
        // × 0.5: the top half of the slider's range was all trenches.
        float strength = Math.Clamp(p.Valleys, 0f, 1f) * 0.5f;
        if (strength <= 0.001f) return;

        // Per watercourse, not per island: each basin keeps a rank and `Valleys` slides a window across them.
        int[,] basin = LabelBasins(n, river, out int basins);
        float[] carve = RankBasins(seed, n, water, river, basin, basins, strength);

        BandDistances(n, land, river, navigable, basin, carve,
                      out int[,] dist, out bool[,] wide, out int[,] reachOf);

        // The deepest any course on this island cuts: how far the band pass walks.
        int reach = 0;
        for (int b = 0; b < basins; b++)
            reach = Math.Max(reach, (int)MathF.Round(ValleyReach * carve[b]));
        if (reach <= 0) return;

        int[,] want = WantDepths(n, land, surface, water, river, form, keep, dist, wide, reachOf, reach);
        // Taper only ever reduces a cell, to one more than its smallest neighbour: the profile survives.
        FieldOps.Taper(want, land);
        EqualisePairs(n, river, twin, want);
        ApplySink(n, surface, water, river, want);
        FixTwoSlabSteps(n, land, surface, water, river, form, keep);
    }

    /// <summary>
    /// How much of the full valley each basin cuts, in [0, 1]: a hashed rank,
    /// tilted by the course's descent so a river working down through relief
    /// draws from the low end of the window and one crossing a plain from the high end.
    /// </summary>
    private static float[] RankBasins(int seed, int n, short[,] water, bool[,] river,
                                      int[,] basin, int basins, float strength)
    {
        // The fall from each course's head to the rim.
        var lo = new int[basins];
        var hi = new int[basins];
        Array.Fill(lo, int.MaxValue);
        Array.Fill(hi, int.MinValue);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!river[x, z]) continue;
            int b = basin[x, z];
            lo[b] = Math.Min(lo[b], water[x, z]);
            hi[b] = Math.Max(hi[b], water[x, z]);
        }

        var carve = new float[basins];
        for (int b = 0; b < basins; b++)
        {
            float rank = Hash01(seed, 0x7A11Eu ^ (uint)b * 2654435761u);
            // The tilt is up to ±0.35 of the range.
            float relief = hi[b] < lo[b] ? 0f
                : Math.Clamp((hi[b] - lo[b] - 3) / 12f, 0f, 1f);
            rank = Math.Clamp(rank + (0.5f - relief) * 0.7f, 0f, 1f);
            // 3s − 2r so the window both slides and widens; 2s − r gave every river a valley from a half up.
            carve[b] = Math.Clamp(strength * 3f - rank * 2f, 0f, 1f);
        }
        return carve;
    }

    /// <summary>
    /// Breadth-first bands out from every channel that cuts: band 0 is the channel,
    /// −1 is unreached, and each cell carries its course's reach and whether it is
    /// navigable, which together bound how far its band walks (<see cref="Budget"/>).
    /// </summary>
    private static void BandDistances(int n, bool[,] land, bool[,] river, bool[,] navigable,
                                      int[,] basin, float[] carve,
                                      out int[,] dist, out bool[,] wide, out int[,] reachOf)
    {
        dist = new int[n, n];
        wide = new bool[n, n];
        reachOf = new int[n, n];
        var q = new Queue<Vector2I>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            dist[x, z] = -1;
            reachOf[x, z] = 0;
            if (!river[x, z]) continue;

            int cut = (int)MathF.Round(ValleyReach * carve[basin[x, z]]);
            if (cut <= 0) continue;                     // this river keeps its incision

            dist[x, z] = 0;
            wide[x, z] = navigable[x, z];
            reachOf[x, z] = cut;
            q.Enqueue(new Vector2I(x, z));
        }

        while (q.Count > 0)
        {
            Vector2I c = q.Dequeue();
            int budget = Budget(reachOf[c.X, c.Y], wide[c.X, c.Y]);
            if (dist[c.X, c.Y] >= budget) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (!land[nx, nz] || dist[nx, nz] >= 0) continue;
                dist[nx, nz] = dist[c.X, c.Y] + 1;
                wide[nx, nz] = wide[c.X, c.Y];
                reachOf[nx, nz] = reachOf[c.X, c.Y];
                q.Enqueue(new Vector2I(nx, nz));
            }
        }
    }

    /// <summary>
    /// How far each cell wants to sink, before anything is applied. The channel
    /// (band 0) sinks one further than its bank; a cell never goes into a lake
    /// beside it nor more than one slab below a neighbour that cannot sink; then
    /// each band is held to what the band inside it got, so the profile only ever
    /// falls toward the water.
    /// </summary>
    private static int[,] WantDepths(int n, bool[,] land, short[,] surface, short[,] water,
                                     bool[,] river, byte[,] form, bool[,] keep,
                                     int[,] dist, bool[,] wide, int[,] reachOf, int reach)
    {
        var want = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            int band = dist[x, z];
            if (band < 0) continue;
            int budget = Budget(reachOf[x, z], wide[x, z]);
            if (band > budget) continue;
            if (!land[x, z]) continue;

            if (!Sinkable(form, river, water, keep, x, z)) continue;

            int floor = int.MinValue;
            int room = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                if (water[nx, nz] != IslandData.NoLand && !river[nx, nz])
                    floor = Math.Max(floor, water[nx, nz] + 1);

                if (!Sinkable(form, river, water, keep, nx, nz)
                    && surface[x, z] - surface[nx, nz] <= 1)
                    room = Math.Min(room, surface[x, z] - surface[nx, nz] + 1);
            }
            if (floor != int.MinValue) room = Math.Min(room, surface[x, z] - floor);

            want[x, z] = Math.Clamp(budget - band + 1, 0, Math.Max(0, room));
        }

        // Bands walk outward in order, each reading the band inside it already final.
        for (int band = 1; band <= reach; band++)
        {
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (dist[x, z] != band || want[x, z] <= 0) continue;
                int inner = 0;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (dist[nx, nz] == band - 1) inner = Math.Max(inner, want[nx, nz]);
                }
                want[x, z] = Math.Min(want[x, z], inner);
            }
        }
        return want;
    }

    /// <summary>
    /// One cut for the two cells of a navigable pair: the per-cell caps can hold
    /// one back while its partner sinks, and the pair takes the smaller cut.
    /// </summary>
    private static void EqualisePairs(int n, bool[,] river, Vector2I[,] twin, int[,] want)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Vector2I a = twin[x, z];
            if (a.X < 0 || !river[x, z] || !river[a.X, a.Y]) continue;
            int m = Math.Min(want[x, z], want[a.X, a.Y]);
            want[x, z] = m;
            want[a.X, a.Y] = m;
        }
    }

    /// <summary>Applies the sink; a channel takes its water down with it, or the valley fills.</summary>
    private static void ApplySink(int n, short[,] surface, short[,] water, bool[,] river, int[,] want)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (want[x, z] <= 0) continue;
            surface[x, z] = (short)(surface[x, z] - want[x, z]);
            if (river[x, z]) water[x, z] = (short)(water[x, z] - want[x, z]);
        }
    }

    /// <summary>
    /// Tapering bounds the change between neighbours, not the result: a cell that
    /// stood one above its neighbour and sank one less now stands two. Lowers such
    /// cells a slab, never into water beside them, for up to eight in-place passes.
    /// </summary>
    private static void FixTwoSlabSteps(int n, bool[,] land, short[,] surface, short[,] water,
                                        bool[,] river, byte[,] form, bool[,] keep)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!land[x, z] || river[x, z] || !Sinkable(form, river, water, keep, x, z)) continue;

                int floor = int.MinValue;
                bool ambiguous = false;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                    if (water[nx, nz] != IslandData.NoLand)
                        floor = Math.Max(floor, water[nx, nz] + 1);
                    if (surface[x, z] - surface[nx, nz] == 2) ambiguous = true;
                }
                if (!ambiguous || surface[x, z] - 1 < floor) continue;
                surface[x, z]--;
                changed = true;
            }
            if (!changed) break;
        }
    }
}
