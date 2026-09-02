using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>Falls and fords: where the water drops, and where a stream can be crossed.</summary>
internal static partial class Rivers
{
    /// <summary>
    /// Records where the water falls rather than runs. It pours every way it
    /// plausibly can: off every aether edge beside a river cell, toward any
    /// adjacent water <see cref="FallDepth"/> or more below it, and onto dry
    /// ground only along its own course — nothing new gets wet. A lake pours too,
    /// where a channel leaves it well below its surface. The list order (scan
    /// order; per cell rim sheets, then the course, then the extras) is consumed by index.
    /// </summary>
    private static void FindFalls(int n, bool[,] land, short[,] surface, short[,] water,
                                  bool[,] river, Vector2I[,] down, List<Fall> falls)
    {
        // One cell wide, always: both cells of a navigable pair pour their own sheet.
        void Pour(int x, int z, int dx, int dz, short bottom, bool offRim)
            => falls.Add(new Fall(new Vector2I(x, z), water[x, z], bottom, new Vector2I(dx, dz), offRim));

        Span<bool> spilt = stackalloc bool[4];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!river[x, z])
            {
                if (!land[x, z] || water[x, z] == IslandData.NoLand) continue;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!river[nx, nz] || water[nx, nz] == IslandData.NoLand) continue;
                    if (water[x, z] - water[nx, nz] < FallDepth) continue;
                    Pour(x, z, Dx[k], Dz[k], water[nx, nz], false);
                }
                continue;
            }

            bool lip = false;
            spilt.Clear();

            // Off the rim, every way the aether is; Bottom is provisional until the keel is known.
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (InBounds(n, nx, nz) && land[nx, nz]) continue;
                Pour(x, z, Dx[k], Dz[k], (short)(water[x, z] - RimFallTail), true);
                spilt[k] = true;
                lip = true;
            }

            // Down the course: the one sheet allowed onto dry ground. A rim cell's course has already left.
            Vector2I to = down[x, z];
            if (!lip && to.X >= 0)
            {
                int below = water[to.X, to.Y] != IslandData.NoLand
                    ? water[to.X, to.Y]
                    : surface[to.X, to.Y];
                if (water[x, z] - below >= FallDepth)
                {
                    Pour(x, z, to.X - x, to.Y - z, (short)below, false);
                    for (int k = 0; k < 4; k++)
                        if (x + Dx[k] == to.X && z + Dz[k] == to.Y) spilt[k] = true;
                }
            }

            // Toward any other water FallDepth or more below.
            for (int k = 0; k < 4; k++)
            {
                if (spilt[k]) continue;
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                if (water[nx, nz] == IslandData.NoLand) continue;
                if (water[x, z] - water[nx, nz] < FallDepth) continue;
                Pour(x, z, Dx[k], Dz[k], water[nx, nz], false);
            }
        }
    }

    /// <summary>
    /// Sends every rim fall past the underside of the Domain. The keel is only
    /// known once the columns are built, so this runs after the rest of the water.
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

    /// <summary>Cells of stream between one ford and the next.</summary>
    public const int FordSpacing = 11;

    /// <summary>
    /// Marks where a stream can be crossed on foot: a ford at the head of each
    /// course and one every <see cref="FordSpacing"/> cells along it, sliding past
    /// any cell that will not take one, and a short course still gets one. A ford
    /// has both banks across the flow dry, walkable and within a slab of the water.
    /// Runs on the finished columns; read by <see cref="Traversal"/>.
    /// </summary>
    public static void MarkFords(IslandData d)
    {
        int n = d.Size;

        var seen = new bool[n, n];
        var queue = new Queue<Vector2I>();
        var order = new List<Vector2I>();

        bool Stream(int x, int z)
            => InBounds(n, x, z) && d.River[x, z] && !d.Navigable[x, z];

        bool Crossable(int x, int z)
        {
            short level = d.WaterLevel[x, z];
            if (level == IslandData.NoLand) return false;

            for (int axis = 0; axis < 2; axis++)
            {
                int dx = axis == 0 ? 1 : 0, dz = axis == 0 ? 0 : 1;
                if (Bank(x - dx, z - dz, level) && Bank(x + dx, z + dz, level)) return true;
            }
            return false;
        }

        bool Bank(int x, int z, short level)
        {
            if (!InBounds(n, x, z)) return false;
            if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) return false;
            return Math.Abs(d.SurfaceLevel(x, z) - level) <= 1;
        }

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Stream(sx, sz) || seen[sx, sz]) continue;

            // One course at a time, in breadth-first order, so the spacing is measured along the water.
            order.Clear();
            seen[sx, sz] = true;
            queue.Enqueue(new Vector2I(sx, sz));
            while (queue.Count > 0)
            {
                Vector2I c = queue.Dequeue();
                order.Add(c);
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    if (!Stream(nx, nz) || seen[nx, nz]) continue;
                    seen[nx, nz] = true;
                    queue.Enqueue(new Vector2I(nx, nz));
                }
            }

            int since = FordSpacing;
            bool any = false;
            foreach (Vector2I c in order)
            {
                since++;
                if (since < FordSpacing) continue;
                if (!Crossable(c.X, c.Y)) continue;
                d.Ford[c.X, c.Y] = true;
                since = 0;
                any = true;
            }
            if (any) continue;

            foreach (Vector2I c in order)
                if (Crossable(c.X, c.Y)) { d.Ford[c.X, c.Y] = true; break; }
        }
    }
}
