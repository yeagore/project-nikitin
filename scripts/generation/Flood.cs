using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>
/// Floods over the grid in the one traversal order every stage shares: scan x
/// outer / z inner, neighbours in <see cref="Grid"/> order. Ids and cell lists
/// come out in that order, which is what makes a site's ties reproducible.
/// </summary>
internal static class Flood
{
    /// <summary>
    /// Labels the 4-connected components of <paramref name="inSet"/> by depth-first
    /// search: ids in scan order into <paramref name="into"/>, −1 elsewhere. Each
    /// component's cells, if asked for, are in pop order.
    /// </summary>
    public static int Label(int n, Func<int, int, bool> inSet, int[,] into, List<List<Vector2I>>? cells = null)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) into[x, z] = -1;

        var stack = new Stack<Vector2I>();
        int count = 0;
        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!inSet(sx, sz) || into[sx, sz] >= 0) continue;

            int id = count++;
            List<Vector2I>? list = cells == null ? null : new List<Vector2I>();
            into[sx, sz] = id;
            stack.Push(new Vector2I(sx, sz));

            while (stack.Count > 0)
            {
                Vector2I c = stack.Pop();
                list?.Add(c);
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!inSet(nx, nz) || into[nx, nz] >= 0) continue;
                    into[nx, nz] = id;
                    stack.Push(new Vector2I(nx, nz));
                }
            }
            cells?.Add(list!);
        }
        return count;
    }

    /// <summary>
    /// Multi-source breadth-first distance: 0 at every seed, −1 where unreached.
    /// Seeds are enqueued in scan order, a step from one cell to the next is taken
    /// only where <paramref name="step"/> allows it, and a cell at <paramref name="cap"/>
    /// is set but not expanded.
    /// </summary>
    public static int[,] Distance(int n, Func<int, int, bool> seed, Func<int, int, int, int, bool> step,
                                  int cap = int.MaxValue)
    {
        var dist = new int[n, n];
        var q = new Queue<Vector2I>();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (seed(x, z)) { dist[x, z] = 0; q.Enqueue(new Vector2I(x, z)); }
            else dist[x, z] = -1;
        }

        while (q.Count > 0)
        {
            Vector2I c = q.Dequeue();
            if (dist[c.X, c.Y] >= cap) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (dist[nx, nz] >= 0) continue;
                if (!step(c.X, c.Y, nx, nz)) continue;
                dist[nx, nz] = dist[c.X, c.Y] + 1;
                q.Enqueue(new Vector2I(nx, nz));
            }
        }
        return dist;
    }
}
