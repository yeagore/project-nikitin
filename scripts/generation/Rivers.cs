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

    /// <summary>
    /// How far a stream's bed is cut below the ground it runs through, in slabs.
    /// <b>Two.</b> One was enough to make the step grammar work and it looked
    /// wrong — filled to the level of the ground beside it, the water read as a
    /// sheet poured over the terrain rather than as a river in a channel. At two,
    /// the banks stand a slab proud of the water and the course has a bed.
    ///
    /// It stays fordable: you step down a slab to the water and up a slab out of
    /// it, and <see cref="Traversal.CrossLevel"/> is what makes the analysis
    /// measure that rather than the bed.
    /// </summary>
    public const int StreamDepth = 2;

    /// <summary>
    /// The same for a navigable river, which carries two slabs of water because a
    /// barge needs the draught — and which is not fordable at all.
    /// </summary>
    public const int NavigableDepth = 3;

    /// <summary>
    /// Slabs a rim fall is drawn falling past the underside of the Domain before
    /// it is left to the aether. There is nothing below to catch it.
    /// </summary>
    public const int RimFallTail = 16;

    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };

    /// <summary>
    /// Routes drainage and carves what qualifies: a bed two slabs into
    /// <paramref name="surface"/> (three where the river is navigable), filled to
    /// one slab below the ground it crosses, with the banks brought down to meet
    /// it. A stream is therefore a channel you can see and still a ford you can
    /// walk — see <see cref="StreamDepth"/> and <see cref="CutBanks"/>.
    /// </summary>
    /// <param name="keep">Cells the water may not touch — the bridgeheads.</param>
    /// <param name="form">Landform per column, so a bank that is a mesa rim is left alone.</param>
    public static void Carve(int seed, IslandParams p, bool[,] land, short[,] surface,
                             short[,] water, bool[,] river, bool[,] navigable,
                             int[,] flow, List<Fall> falls, int bridgeSpan, byte[,] form,
                             bool[,] keep)
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

        // A navigable river is two cells across and cannot be waded, so on a
        // Domain where a bridge only spans one cell it would cut the country in
        // half with nothing to be done about it. There, every watercourse stays a
        // stream: an easy Domain is one you can always get across.
        if (bridgeSpan < 2) navigableAt = int.MaxValue;

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
            if (keep[x, z]) continue;                            // a bridgehead
            channel[x, z] = true;
            navigable[x, z] = flow[x, z] >= navigableAt;
        }

        var twin = new Vector2I[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) twin[x, z] = new Vector2I(-1, -1);

        Widen(n, land, water, surface, flow, down, channel, navigable, navigableAt, twin);

        var before = (short[,])surface.Clone();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!channel[x, z]) continue;

            // <b>A river has a bed.</b> The channel is cut two slabs below the
            // ground it crosses and filled to one below it, so the banks stand a
            // slab proud of the water and the course reads as a channel rather
            // than as water poured over the terrain. A navigable river is cut a
            // slab deeper again: two slabs of water, which is the draught a barge
            // wants and more than anyone wades.
            //
            // A stream stays free to cross — down a slab into the water, up a slab
            // out of it — and Traversal.CrossLevel is what makes the analysis
            // measure that step rather than the bed under it.
            int depth = navigable[x, z] ? NavigableDepth : StreamDepth;
            river[x, z] = true;

            // A widened cell takes the level of the channel it was widened from,
            // so the two cells of a navigable river are one surface rather than a
            // step down its own length. Read from the snapshot: that cell may
            // already have been cut by this same loop, and cutting a cut is a
            // trench.
            Vector2I pair = twin[x, z];
            int ground = pair.X >= 0 ? before[pair.X, pair.Y] : before[x, z];

            water[x, z] = (short)(ground - 1);
            surface[x, z] = (short)(ground - depth);
        }

        Descend(n, order, down, river, water, surface);
        CutBanks(n, land, surface, water, river, form, keep);
        FindFalls(n, land, surface, water, river, navigable, down, falls);
    }

    /// <summary>
    /// Brings the banks down to the water.
    ///
    /// Cutting the bed two slabs deep is what stops a river reading as water
    /// poured over the ground — and on its own it also puts a <b>two-slab step</b>
    /// wherever the bank beside the channel happened to stand a slab proud, which
    /// is the one step height the whole grammar exists to avoid. So the river cuts
    /// its banks as well as its bed: a dry cell standing exactly two above the
    /// water comes down one slab, to the free step, and a ford stays a ford.
    ///
    /// <b>Only that step, and only by that slab.</b> A bank three or more above
    /// the water is a gorge wall — a cliff, which the grammar allows and the eye
    /// reads — and slamming it down to the waterline would cut a trench across the
    /// island wherever a river passed a rise. The correction then walks outward
    /// against the same test, so it dies out within a cell or two of the water,
    /// except up a steady hillside where it walks the whole slope down one slab
    /// and changes nothing about how the slope reads.
    /// </summary>
    private static void CutBanks(int n, bool[,] land, short[,] surface, short[,] water,
                                 bool[,] river, byte[,] form, bool[,] keep)
    {
        var queue = new Queue<Vector2I>();

        // Cuttable ground: dry, and not part of a landform whose whole point is
        // the height it stands at. A mesa's rim, a basin's wall and a mountain's
        // flank are shapes the terrain rules built deliberately; a stream running
        // over one leaves a cliff, which is a waterfall and not an ambiguity.
        bool Dry(int x, int z)
        {
            if (x < 0 || z < 0 || x >= n || z >= n) return false;
            if (!land[x, z] || water[x, z] != IslandData.NoLand) return false;
            if (keep[x, z]) return false;                        // a bridgehead is level already
            var type = (LandformType)form[x, z];
            return type is LandformType.Plain or LandformType.Hills;
        }

        // How low a cell may go. No cell may be cut into standing water beside it —
        // a lake's shore is its containment, and a channel's own bank holds the
        // channel in — and none may be cut down to within a cliff's height of a
        // basin floor beside it, which would turn the escarpment the wrong way up.
        int Floor(int x, int z)
        {
            int floor = int.MinValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (water[nx, nz] != IslandData.NoLand)
                    floor = Math.Max(floor, water[nx, nz] + 1);
                if ((LandformType)form[nx, nz] == LandformType.Basin)
                    floor = Math.Max(floor, surface[nx, nz] + 3);
            }
            return floor;
        }

        // Only the ambiguous bank is cut, and only by the one slab that makes it
        // ambiguous. A bank standing three or more above the water is a gorge
        // wall — a cliff, which the grammar allows and the eye reads — and
        // slamming it down to the waterline would carve a trench across the
        // island wherever a river happened to pass a rise.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!river[x, z]) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!Dry(nx, nz) || surface[nx, nz] - water[x, z] != 2) continue;
                if (surface[nx, nz] - 1 < Floor(nx, nz)) continue;
                surface[nx, nz]--;
                queue.Enqueue(new Vector2I(nx, nz));
            }
        }

        while (queue.Count > 0)
        {
            Vector2I c = queue.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (!Dry(nx, nz)) continue;
                if (surface[nx, nz] - surface[c.X, c.Y] != 2) continue;
                if (surface[nx, nz] - 1 < Floor(nx, nz)) continue;
                surface[nx, nz]--;
                queue.Enqueue(new Vector2I(nx, nz));
            }
        }
    }

    /// <summary>
    /// Makes every course run downhill.
    ///
    /// The routing guarantees a downstream neighbour, not a lower one: a priority
    /// flood carries the level water had to clear forward, so at a confluence or
    /// along a lake margin a channel could be left a slab above the one it drains
    /// into. It is a handful of cells an island and it is still water running
    /// uphill, which is the one thing a river may not do.
    ///
    /// Walking the routing order backwards visits every cell before the one it
    /// drains into, so pushing the minimum downstream settles in a single pass.
    /// It only ever lowers, and only inside a channel that is already cut.
    /// </summary>
    private static void Descend(int n, List<Vector2I> order, Vector2I[,] down,
                                bool[,] river, short[,] water, short[,] surface)
    {
        for (int i = order.Count - 1; i >= 0; i--)
        {
            Vector2I c = order[i];
            if (!river[c.X, c.Y]) continue;

            Vector2I to = down[c.X, c.Y];
            if (to.X < 0 || !river[to.X, to.Y]) continue;
            if (water[to.X, to.Y] <= water[c.X, c.Y]) continue;

            int drop = water[to.X, to.Y] - water[c.X, c.Y];
            water[to.X, to.Y] = water[c.X, c.Y];
            surface[to.X, to.Y] = (short)(surface[to.X, to.Y] - drop);
        }
    }

    /// <summary>
    /// Sends every rim fall past the underside of the Domain. The keel is only
    /// known once the columns have been built, which is after the water is cut,
    /// so this runs at the end of the pipeline rather than with the rest of it.
    /// </summary>
    public static void DropFallsPastTheKeel(IslandData d)
    {
        for (int i = 0; i < d.Falls.Count; i++)
        {
            Fall f = d.Falls[i];
            if (!f.OffRim) continue;
            short keel = d.KeelLevel(f.Cell.X, f.Cell.Y);
            if (keel == IslandData.NoLand) continue;
            d.Falls[i] = f with { Bottom = (short)(keel - RimFallTail) };
        }
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
                              bool[,] navigable, int navigableAt, Vector2I[,] twin)
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
            // Which cell it was widened from. The pair is one river and has to
            // hold one surface: left to take its own ground level, the second cell
            // sits a slab below the first and the audit reads a river flowing
            // sideways into itself.
            twin[bestX, bestZ] = new Vector2I(x, z);
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
    private static void FindFalls(int n, bool[,] land, short[,] surface, short[,] water,
                                  bool[,] river, bool[,] navigable, Vector2I[,] down,
                                  List<Fall> falls)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!river[x, z]) continue;

            // Off the rim: whichever way the aether is. Bottom is filled in once
            // the keel is known — see DropFallsPastTheKeel — because what is under
            // a rim fall is the underside of the Domain and then nothing.
            int rim = -1;
            for (int k = 0; k < 4 && rim < 0; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) rim = k;
            }

            int width = navigable[x, z] ? 2 : 1;

            if (rim >= 0)
            {
                falls.Add(new Fall(new Vector2I(x, z), water[x, z],
                                   (short)(water[x, z] - RimFallTail),
                                   new Vector2I(Dx[rim], Dz[rim]), true, width));
                continue;
            }

            // Inland: a step of FallDepth or more onto whatever is below, which is
            // the next pool along a mountain course.
            Vector2I to = down[x, z];
            if (to.X < 0) continue;

            int below = water[to.X, to.Y] != IslandData.NoLand
                ? water[to.X, to.Y]
                : surface[to.X, to.Y];
            if (water[x, z] - below < FallDepth) continue;

            falls.Add(new Fall(new Vector2I(x, z), water[x, z], (short)below,
                               new Vector2I(to.X - x, to.Y - z), false, width));
        }
    }
}
