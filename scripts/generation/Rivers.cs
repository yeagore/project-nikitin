using System;
using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Cuts watercourses across finished terrain.
///
/// <para><b>There is no sea.</b> A Domain floats in aether, so every drop that
/// lands on it leaves by pouring off the rim — which is the one strong image the
/// whole system is built toward. Rivers therefore have exactly one destination
/// and it is the void.</para>
///
/// <para>They are cut <b>across</b> the patchwork, after it, rather than being
/// laid down first and having regions drawn around them: a river that only ever
/// follows a border reads as a seam, and it would make the partition answer to
/// the hydrology instead of the other way round.</para>
///
/// <para>The routing is a <b>priority flood</b> from the coast inward, not a
/// steepest-descent walk. Terrain built under a slope limit is mostly flats and
/// shallow pits, so descent stalls constantly and the flat-resolver becomes the
/// whole algorithm. Flooding inward from the outlets gives every cell a
/// downstream neighbour by construction, handles flats and depressions without a
/// special case, and passes straight through a lake — so a lake's outflow is
/// wherever the terrain actually lets it out, not somewhere chosen.</para>
/// </summary>
internal static class Rivers
{
    /// <summary>
    /// Upstream cells a channel needs before it is a river rather than a trickle,
    /// as a share of the island's land. Tuned so a 96² island gets a handful of
    /// named watercourses instead of a delta of threads.
    /// </summary>
    private const float SourceShare = 0.055f;

    /// <summary>
    /// Where a river becomes wide enough to move goods on — and, at the same
    /// time, too wide to ford. A navigable river is two cells across, which is
    /// still inside the bridge span, so it divides the country without cutting
    /// it off.
    /// </summary>
    private const float NavigableShare = 0.30f;

    /// <summary>A drop this deep along a watercourse is a fall rather than a rapid.</summary>
    public const int FallDepth = 3;

    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };

    /// <summary>
    /// Routes drainage and carves what qualifies. Lowers <paramref name="surface"/>
    /// by one slab in each channel and fills it to the old level, so a stream is a
    /// one-slab step and therefore free to cross.
    /// </summary>
    public static void Carve(int seed, IslandParams p, bool[,] land, short[,] surface,
                             short[,] water, bool[,] river, bool[,] navigable,
                             int[,] flow, List<Vector2I> falls)
    {
        int n = p.Size;
        float strength = Math.Clamp(p.Rivers, 0f, 1f);
        if (strength <= 0.001f) return;

        var order = new List<Vector2I>(n * n);
        var down = new Vector2I[n, n];
        Route(n, land, surface, water, order, down);
        if (order.Count == 0) return;

        // Accumulate upstream-first, which is the routing order reversed: the
        // flood reached each cell from its downstream neighbour, so walking the
        // list backwards always sees a cell before the one it drains into.
        for (int i = 0; i < order.Count; i++)
        {
            Vector2I c = order[i];
            flow[c.X, c.Y] = 1;
        }
        for (int i = order.Count - 1; i >= 0; i--)
        {
            Vector2I c = order[i];
            Vector2I to = down[c.X, c.Y];
            if (to.X >= 0) flow[to.X, to.Y] += flow[c.X, c.Y];
        }

        // A wetter island lowers the bar for what counts as a river.
        int landCells = order.Count;
        float ease = Mathf.Lerp(2.2f, 0.45f, strength);
        int riverAt = Math.Max(24, (int)(landCells * SourceShare * ease));
        int navigableAt = Math.Max(riverAt * 3, (int)(landCells * NavigableShare * ease));

        // Accumulation alone gives almost nothing here, and the reason is
        // structural rather than a matter of tuning. Every rim cell is an outlet,
        // so water leaves by the shortest way out, and terrain built under a
        // one-slab slope limit has no valleys to gather it — the drainage fans
        // out from each coast cell and no catchment ever grows large. Measured:
        // a median of 13 river cells an island, and 11 navigable cells over 60.
        //
        // So the sources are *named* instead of emerging. Every summit and every
        // lake outflow starts a watercourse, and it is traced to the rim whatever
        // its catchment, which is what makes a river run the length of an island
        // rather than dribble off the nearest edge. Accumulation still decides
        // how wide it gets on the way down.
        foreach (Vector2I src in Sources(seed, p, n, land, surface, water, down, strength))
            Trace(n, src, down, flow, riverAt);

        var channel = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z] || flow[x, z] < riverAt) continue;
            if (water[x, z] != IslandData.NoLand) continue;      // already a lake
            channel[x, z] = true;
            navigable[x, z] = flow[x, z] >= navigableAt;
        }

        Widen(n, land, water, surface, flow, down, channel, navigable, navigableAt);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!channel[x, z]) continue;

            // One slab deep, filled to the level the ground was at. That depth is
            // deliberate: a one-slab step is free, so a stream is crossable
            // anywhere, which is the right default for something that runs the
            // length of an island.
            river[x, z] = true;
            water[x, z] = surface[x, z];
            surface[x, z] = (short)(surface[x, z] - 1);
        }

        FindFalls(n, land, water, river, down, falls);
    }

    /// <summary>
    /// Where watercourses begin: the high ground, and every lake's outflow.
    ///
    /// Summits are taken in order of height, each having to stand clear of the
    /// ones already chosen so a single massif does not spend the whole budget,
    /// and each having to be well inland — a source a few cells from the rim is a
    /// trickle over the edge, not a river.
    /// </summary>
    private static List<Vector2I> Sources(int seed, IslandParams p, int n, bool[,] land,
                                          short[,] surface, short[,] water,
                                          Vector2I[,] down, float strength)
    {
        var found = new List<Vector2I>();

        // A lake spills wherever the terrain lets it: the cell whose downstream
        // neighbour is the first dry ground out of the pool. That is what links
        // one lake to the next, and eventually to the rim.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (water[x, z] == IslandData.NoLand) continue;
            Vector2I to = down[x, z];
            if (to.X < 0 || water[to.X, to.Y] != IslandData.NoLand) continue;
            found.Add(new Vector2I(x, z));
        }

        var peaks = new List<Vector2I>();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && water[x, z] == IslandData.NoLand && Inland(n, land, x, z, 5))
                peaks.Add(new Vector2I(x, z));

        peaks.Sort((a, b) => surface[b.X, b.Y].CompareTo(surface[a.X, a.Y]));

        int want = 2 + (int)(strength * 4f);
        int spacing = Math.Max(10, n / 7);
        foreach (Vector2I c in peaks)
        {
            if (found.Count >= want + 8) break;
            int taken = 0;
            bool crowded = false;
            foreach (Vector2I had in found)
            {
                if (Math.Abs(had.X - c.X) + Math.Abs(had.Y - c.Y) < spacing) { crowded = true; break; }
                taken++;
            }
            _ = taken;
            if (crowded) continue;
            found.Add(c);
            if (found.Count >= want) break;
        }
        return found;
    }

    /// <summary>Whether a cell has land all round it out to <paramref name="reach"/> cells.</summary>
    private static bool Inland(int n, bool[,] land, int x, int z, int reach)
    {
        for (int k = 0; k < 4; k++)
        for (int step = 1; step <= reach; step++)
        {
            int nx = x + Dx[k] * step, nz = z + Dz[k] * step;
            if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) return false;
        }
        return true;
    }

    /// <summary>
    /// Follows the water down from a source to the rim, adding a river's worth of
    /// flow to every cell on the way. Confluences therefore add up, so two traced
    /// courses meeting make one wider than either.
    /// </summary>
    private static void Trace(int n, Vector2I from, Vector2I[,] down, int[,] flow, int add)
    {
        Vector2I c = from;
        for (int guard = 0; guard < n * n; guard++)
        {
            flow[c.X, c.Y] += add;
            Vector2I to = down[c.X, c.Y];
            if (to.X < 0) return;               // reached the rim
            c = to;
        }
    }

    /// <summary>
    /// Gives every land cell a downstream neighbour, by flooding inward from the
    /// void. Returns the order cells were reached in — outlets first — and the
    /// neighbour each was reached from, which is where its water goes.
    ///
    /// The priority is <c>max(own height, the height water had to clear to get
    /// here)</c>. Carrying that maximum forward is what makes a depression fill
    /// and spill at its lowest rim rather than trapping the flood, and it is why
    /// a lake needs no special handling: water enters, crosses, and leaves.
    /// </summary>
    private static void Route(int n, bool[,] land, short[,] surface, short[,] water,
                              List<Vector2I> order, Vector2I[,] down)
    {
        var seen = new bool[n, n];
        var queue = new PriorityQueue<Vector2I, long>();
        long tick = 0;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            down[x, z] = new Vector2I(-1, -1);
            if (!land[x, z]) continue;

            // An outlet is a cell with aether beside it: the rim, and nothing else.
            bool rim = false;
            for (int k = 0; k < 4 && !rim; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                rim = nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz];
            }
            if (!rim) continue;

            seen[x, z] = true;
            queue.Enqueue(new Vector2I(x, z), Key(Level(surface, water, x, z), tick++));
        }

        while (queue.TryDequeue(out Vector2I c, out long key))
        {
            order.Add(c);
            int reached = (int)(key >> 24) - LevelBias;

            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!land[nx, nz] || seen[nx, nz]) continue;

                seen[nx, nz] = true;
                down[nx, nz] = c;
                int lift = Math.Max(reached, Level(surface, water, nx, nz));
                queue.Enqueue(new Vector2I(nx, nz), Key(lift, tick++));
            }
        }
    }

    /// <summary>The level water sits at in a column: a lake's surface, or the ground.</summary>
    private static int Level(short[,] surface, short[,] water, int x, int z)
        => water[x, z] != IslandData.NoLand ? water[x, z] : surface[x, z];

    /// <summary>
    /// Height in the high bits, insertion order in the low ones, so equal heights
    /// come out first-in-first-out and a flat drains evenly outward instead of
    /// picking a corner.
    /// </summary>
    /// <summary>Keeps the packed level positive; slab indices run below zero.</summary>
    private const int LevelBias = 4096;

    private static long Key(int level, long tick)
        => ((long)(level + LevelBias) << 24) | (tick & 0xFFFFFF);

    /// <summary>
    /// Puts a second cell alongside a navigable channel. A barge needs two cells;
    /// a third would put the river past the bridge span and cut the island in
    /// half, which should be a deliberate choice and not a side effect of rain.
    /// </summary>
    private static void Widen(int n, bool[,] land, short[,] water, short[,] surface,
                              int[,] flow, Vector2I[,] down, bool[,] channel,
                              bool[,] navigable, int navigableAt)
    {
        var added = new List<Vector2I>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!channel[x, z] || !navigable[x, z]) continue;

            Vector2I to = down[x, z];
            if (to.X < 0) continue;

            // Perpendicular to the way the water is going.
            int fx = to.X - x, fz = to.Y - z;
            int px = fz, pz = -fx;

            int bestX = -1, bestZ = -1, bestTop = int.MaxValue;
            for (int side = -1; side <= 1; side += 2)
            {
                int nx = x + px * side, nz = z + pz * side;
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!land[nx, nz] || channel[nx, nz]) continue;
                if (water[nx, nz] != IslandData.NoLand) continue;
                // Never widen onto ground that stands above the channel: that is a
                // bank, and cutting it away leaves a notch rather than a river.
                if (surface[nx, nz] > surface[x, z]) continue;
                if (surface[nx, nz] >= bestTop) continue;

                bestTop = surface[nx, nz];
                bestX = nx;
                bestZ = nz;
            }
            if (bestX < 0) continue;

            added.Add(new Vector2I(bestX, bestZ));
            flow[bestX, bestZ] = Math.Max(flow[bestX, bestZ], navigableAt);
        }

        foreach (Vector2I c in added)
        {
            channel[c.X, c.Y] = true;
            navigable[c.X, c.Y] = true;
        }
    }

    /// <summary>
    /// Where the water falls rather than runs: a drop of <see cref="FallDepth"/>
    /// or more to the next cell downstream, and every channel that reaches the rim
    /// — at the coast every river becomes a fall, because there is nowhere else
    /// for it to go.
    /// </summary>
    private static void FindFalls(int n, bool[,] land, short[,] water, bool[,] river,
                                  Vector2I[,] down, List<Vector2I> falls)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!river[x, z]) continue;

            bool atRim = false;
            for (int k = 0; k < 4 && !atRim; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                atRim = nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz];
            }

            Vector2I to = down[x, z];
            bool steep = to.X >= 0
                         && water[to.X, to.Y] != IslandData.NoLand
                         && water[x, z] - water[to.X, to.Y] >= FallDepth;

            if (atRim || steep) falls.Add(new Vector2I(x, z));
        }
    }
}
