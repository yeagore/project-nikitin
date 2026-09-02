using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

public static partial class Traversal
{
    /// <summary>Dry walkable ground that is flat or at one lone step: no walkable neighbour more than a slab off, at most <see cref="ShelfSteps"/> off at all.</summary>
    private static bool ShelfGround(IslandData d, int x, int z)
    {
        if (!Walkable(d, x, z)) return false;
        if (d.WaterLevel[x, z] != IslandData.NoLand) return false;

        short level = CrossLevel(d, x, z);
        int steps = 0;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + Dx[k], nz = z + Dz[k];
            if (!Walkable(d, nx, nz)) continue;          // a coast is not a step
            int delta = Math.Abs(CrossLevel(d, nx, nz) - level);
            if (delta == 0) continue;
            if (delta > 1) return false;                 // a cliff edge is not shelf
            steps++;
        }
        return steps <= ShelfSteps;
    }

    /// <summary>Floods shelf ground a slab at a time into <see cref="IslandData.Shelves"/> / <see cref="IslandData.ShelfId"/>, dropping pieces under half <see cref="MinShelfArea"/>.</summary>
    private static void BuildShelves(IslandData d)
    {
        int n = d.Size;
        var shelves = new List<Shelf>();
        var queue = new Queue<(int X, int Z)>();
        var cells = new List<Vector2I>();
        var claimed = new bool[n, n];
        var ground = new bool[n, n];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            d.ShelfId[x, z] = -1;
            ground[x, z] = ShelfGround(d, x, z);
        }

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!ground[sx, sz] || claimed[sx, sz]) continue;

            cells.Clear();
            claimed[sx, sz] = true;
            queue.Enqueue((sx, sz));
            short low = CrossLevel(d, sx, sz), high = low;

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                cells.Add(new Vector2I(x, z));
                short level = CrossLevel(d, x, z);
                if (level < low) low = level;
                if (level > high) high = level;

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!ground[nx, nz] || claimed[nx, nz]) continue;
                    if (Math.Abs(CrossLevel(d, nx, nz) - level) > 1) continue;
                    claimed[nx, nz] = true;
                    queue.Enqueue((nx, nz));
                }
            }

            if (cells.Count < MinShelfArea / 2) continue;

            int id = shelves.Count;
            Vector2I min = cells[0], max = cells[0];
            foreach (Vector2I c in cells)
            {
                min = new Vector2I(Math.Min(min.X, c.X), Math.Min(min.Y, c.Y));
                max = new Vector2I(Math.Max(max.X, c.X), Math.Max(max.Y, c.Y));
                d.ShelfId[c.X, c.Y] = id;
            }

            (int width, Vector2I center) = WidestSquare(n, cells);
            shelves.Add(new Shelf(id, low, high, cells.Count, width, min, max, center));
        }

        d.Shelves.Clear();
        d.Shelves.AddRange(shelves);
    }

    /// <summary>
    /// The widest square inside a shelf by repeated 8-way erosion (rings is a radius, so
    /// Width = 2 * rings + 1), and the first cell enumerated from the last surviving ring.
    /// </summary>
    private static (int Width, Vector2I Center) WidestSquare(int n, List<Vector2I> cells)
    {
        var alive = new HashSet<Vector2I>(cells);
        Vector2I best = cells[0];
        int rings = 0;

        while (alive.Count > 0)
        {
            var next = new HashSet<Vector2I>();
            foreach (Vector2I c in alive)
            {
                bool solid = true;
                for (int dx = -1; dx <= 1 && solid; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = c.X + dx, nz = c.Y + dz;
                    if (!InBounds(n, nx, nz)
                        || !alive.Contains(new Vector2I(nx, nz)))
                    {
                        solid = false;
                        break;
                    }
                }
                if (solid) next.Add(c);
            }
            if (next.Count == 0) break;

            rings++;
            foreach (Vector2I c in next) { best = c; break; }
            alive = next;
        }
        return (2 * rings + 1, best);
    }
}
