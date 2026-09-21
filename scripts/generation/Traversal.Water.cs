using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

public static partial class Traversal
{
    /// <summary>
    /// Labels every sailable column with its body of water, cutting a body at every
    /// on-Domain fall: nothing sails up one. Read by the names and the lab; a ferry
    /// between two shores of one body was a work here once, and was removed as one
    /// that all but never bore a load (see the appendix).
    /// </summary>
    private static void BuildWaterBodies(IslandData d)
    {
        int n = d.Size;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) d.WaterBody[x, z] = -1;

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
}
