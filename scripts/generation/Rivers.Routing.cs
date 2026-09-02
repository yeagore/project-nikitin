using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>Routing: the priority flood inward from the rim, the named sources, and the trace down.</summary>
internal static partial class Rivers
{
    /// <summary>
    /// Gives every land cell a downstream neighbour by flooding inward from the
    /// void: <paramref name="order"/> is the order cells were reached in, outlets
    /// first, and <paramref name="down"/> the neighbour each was reached from.
    /// The priority is max(own level, the level water had to clear to get here),
    /// so a depression fills and spills at its lowest rim and a lake needs no
    /// special case. Ties are broken on a noise field, jittered strictly below one
    /// slab — a first-in-first-out tie-break is a plain BFS whose tree is a fan of
    /// straight cardinal rays, and that is what made rivers run in straight lines.
    /// </summary>
    private static void Route(int seed, int n, bool[,] land, short[,] surface, short[,] water,
                              List<Vector2I> order, Vector2I[,] down, byte[,] fluid)
    {
        var seen = new bool[n, n];
        var lifted = new int[n, n];
        // Wavelength about fourteen cells: a bend a river takes, not a wobble.
        var meander = new Noise(seed + 5701, frequency: 0.07f, octaves: 3);
        var queue = new PriorityQueue<Vector2I, long>();
        long tick = 0;

        // A column of any other fluid is not-land here: goo makes no rivers.
        bool Ground(int x, int z) => land[x, z] && fluid[x, z] == 0;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            down[x, z] = new Vector2I(-1, -1);
            if (!Ground(x, z)) continue;

            // An outlet is a cell with aether beside it: the rim, and nothing else.
            bool rim = false;
            for (int k = 0; k < 4 && !rim; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                rim = !InBounds(n, nx, nz) || !land[nx, nz];
            }
            if (!rim) continue;

            seen[x, z] = true;
            int level = Level(surface, water, x, z);
            lifted[x, z] = level;
            queue.Enqueue(new Vector2I(x, z), Key(level, meander, x, z, tick++));
        }

        while (queue.TryDequeue(out Vector2I c, out _))
        {
            order.Add(c);
            int reached = lifted[c.X, c.Y];

            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (!Ground(nx, nz) || seen[nx, nz]) continue;

                seen[nx, nz] = true;
                down[nx, nz] = c;
                int lift = Math.Max(reached, Level(surface, water, nx, nz));
                lifted[nx, nz] = lift;
                queue.Enqueue(new Vector2I(nx, nz), Key(lift, meander, nx, nz, tick++));
            }
        }
    }

    /// <summary>The level water sits at in a column: a lake's surface, or the ground.</summary>
    private static int Level(short[,] surface, short[,] water, int x, int z)
        => water[x, z] != IslandData.NoLand ? water[x, z] : surface[x, z];

    /// <summary>Keeps the packed level positive; slab indices run below zero.</summary>
    private const int LevelBias = 4096;

    /// <summary>
    /// Flood priority: height in the high bits, the meander field in the middle,
    /// insertion order in the low ones — terrain outranks the wander, the wander
    /// outranks arrival, and nothing is left to chance.
    /// </summary>
    private static long Key(int level, Noise meander, int x, int z, long tick)
    {
        long wander = (long)(meander.At(x, z) * 0xFFFFFF) & 0xFFFFFF;
        return ((long)(level + LevelBias) << 40) | (wander << 16) | (tick & 0xFFFF);
    }

    /// <summary>
    /// Where watercourses begin: one spill per lake — the shore cell whose
    /// downstream ground is lowest, rather than every shore cell — then summits
    /// in order of height, each well inland and spaced clear of the ones already
    /// chosen so a single massif does not spend the whole budget.
    /// </summary>
    private static List<Vector2I> Sources(int n, bool[,] land, short[,] surface, short[,] water,
                                          Vector2I[,] down, float strength)
    {
        var found = new List<Vector2I>();

        var seen = new bool[n, n];
        var stack = new Stack<Vector2I>();

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (water[sx, sz] == IslandData.NoLand || seen[sx, sz]) continue;

            var spill = new Vector2I(-1, -1);
            int lowest = int.MaxValue;

            seen[sx, sz] = true;
            stack.Push(new Vector2I(sx, sz));
            while (stack.Count > 0)
            {
                Vector2I c = stack.Pop();
                Vector2I to = down[c.X, c.Y];
                if (to.X >= 0 && water[to.X, to.Y] == IslandData.NoLand
                    && surface[to.X, to.Y] < lowest)
                {
                    lowest = surface[to.X, to.Y];
                    spill = c;
                }

                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    if (!InBounds(n, nx, nz) || seen[nx, nz]) continue;
                    if (water[nx, nz] == IslandData.NoLand) continue;
                    seen[nx, nz] = true;
                    stack.Push(new Vector2I(nx, nz));
                }
            }
            if (spill.X >= 0) found.Add(spill);
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
            bool crowded = false;
            foreach (Vector2I had in found)
            {
                if (Math.Abs(had.X - c.X) + Math.Abs(had.Y - c.Y) < spacing) { crowded = true; break; }
            }
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
            if (!InBounds(n, nx, nz) || !land[nx, nz]) return false;
        }
        return true;
    }

    /// <summary>
    /// Follows the water down from a source to the rim, adding a river's worth of
    /// flow to every cell on the way, so two traced courses meeting make one wider than either.
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
}
