using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

public static partial class Traversal
{
    /// <summary>
    /// Every quay on each body of water, in <see cref="IslandData.Berths"/> order, and
    /// each quay's body. A body opens once: the first quay asked yields the list, the
    /// rest nothing — a ferry is one crossing however far it goes.
    /// </summary>
    internal sealed class BerthIndex
    {
        private readonly Dictionary<int, List<Vector2I>> byBody = new();
        private readonly Dictionary<Vector2I, int> bodyAt = new();
        private readonly HashSet<int> sailed = new();

        public BerthIndex(IslandData d, bool ferries = true)
        {
            if (!ferries) return;
            foreach (FerryBerth berth in d.Berths)
            {
                if (berth.Body < 0) continue;
                if (!byBody.TryGetValue(berth.Body, out List<Vector2I>? list))
                    byBody[berth.Body] = list = new List<Vector2I>();
                list.Add(berth.Land);
                bodyAt[berth.Land] = berth.Body;
            }
        }

        /// <summary>The quays on <paramref name="quay"/>'s body, the first time that body is opened; null otherwise.</summary>
        public List<Vector2I>? Open(Vector2I quay)
        {
            if (!bodyAt.TryGetValue(quay, out int body)) return null;
            if (!sailed.Add(body)) return null;
            return byBody.TryGetValue(body, out List<Vector2I>? quays) ? quays : null;
        }
    }

    /// <summary>
    /// Labels every sailable column with its body of water, cutting a body at every
    /// on-Domain fall: nothing sails up one. Does not reset <see cref="IslandData.WaterBody"/> first.
    /// </summary>
    private static void BuildWaterBodies(IslandData d)
    {
        int n = d.Size;

        // The links a fall severs, both ways round.
        var cut = new HashSet<(int, int, int, int)>();
        foreach (Fall f in d.Falls)
        {
            if (f.OffRim) continue;
            int tx = f.Cell.X + f.Flow.X, tz = f.Cell.Y + f.Flow.Y;
            cut.Add((f.Cell.X, f.Cell.Y, tx, tz));
            cut.Add((tx, tz, f.Cell.X, f.Cell.Y));
        }

        var queue = new Queue<(int X, int Z)>();
        int bodies = 0;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Sailable(d, sx, sz) || d.WaterBody[sx, sz] >= 0) continue;

            int id = bodies++;
            d.WaterBody[sx, sz] = id;
            queue.Enqueue((sx, sz));

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Sailable(d, nx, nz) || d.WaterBody[nx, nz] >= 0) continue;
                    if (cut.Contains((x, z, nx, nz))) continue;
                    d.WaterBody[nx, nz] = id;
                    queue.Enqueue((nx, nz));
                }
            }
        }
        d.WaterBodies = bodies;
    }

    /// <summary>
    /// Every place a ferry station could stand: a dry walkable quay with a dry walkable
    /// neighbour at a free step behind it (else it is a rock, not a landing), on its first
    /// sailable neighbour within <see cref="MaxQuayRise"/> slabs below.
    /// </summary>
    private static void BuildBerths(IslandData d)
    {
        int n = d.Size;
        d.Berths.Clear();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            d.Ferry[x, z] = false;
            if (!Walkable(d, x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;

            short level = CrossLevel(d, x, z);

            bool yard = false;
            for (int k = 0; k < 4 && !yard; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                yard = Walkable(d, nx, nz) && d.WaterLevel[nx, nz] == IslandData.NoLand
                       && Math.Abs(CrossLevel(d, nx, nz) - level) <= 1;
            }
            if (!yard) continue;

            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!Sailable(d, nx, nz)) continue;

                short surface = d.WaterLevel[nx, nz];
                int rise = level - surface;
                if (rise < 0 || rise > MaxQuayRise) continue;

                d.Ferry[x, z] = true;
                d.Berths.Add(new FerryBerth(new Vector2I(x, z), new Vector2I(nx, nz),
                                            surface, d.WaterBody[nx, nz]));
                break;
            }
        }
    }

    /// <summary>
    /// Keeps only the load-bearing berths: the reach flood is run once without ferries,
    /// and a body of water keeps its berths only if they land in two or more pieces of
    /// that answer. <see cref="IslandData.BerthSites"/> records the count before pruning.
    /// </summary>
    private static void PruneBerths(IslandData d)
    {
        d.BerthSites = d.Berths.Count;
        if (d.Berths.Count == 0) return;

        int n = d.Size;
        var dry = new int[n, n];
        BuildReachAreas(d, ferries: false, into: dry);

        var touches = new Dictionary<int, HashSet<int>>();
        foreach (FerryBerth berth in d.Berths)
        {
            if (berth.Body < 0) continue;
            int piece = dry[berth.Land.X, berth.Land.Y];
            if (piece < 0) continue;
            if (!touches.TryGetValue(berth.Body, out HashSet<int>? seen))
                touches[berth.Body] = seen = new HashSet<int>();
            seen.Add(piece);
        }

        var kept = new List<FerryBerth>();
        foreach (FerryBerth berth in d.Berths)
            if (berth.Body >= 0 && touches.TryGetValue(berth.Body, out HashSet<int>? seen)
                && seen.Count > 1)
                kept.Add(berth);

        d.Berths.Clear();
        d.Berths.AddRange(kept);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) d.Ferry[x, z] = false;
        foreach (FerryBerth berth in d.Berths) d.Ferry[berth.Land.X, berth.Land.Y] = true;
    }
}
