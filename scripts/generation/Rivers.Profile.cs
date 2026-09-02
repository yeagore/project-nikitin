using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>Profile: every course forced downhill, navigable pairs held level, reaches pooled, banks cut.</summary>
internal static partial class Rivers
{
    /// <summary>
    /// Makes every course run downhill. The routing guarantees a downstream
    /// neighbour, not a lower one. Walking the routing order backwards visits
    /// every cell before the one it drains into, so one pass settles it; only
    /// ever lowers, and only inside a cut channel. Returns whether anything moved.
    /// </summary>
    private static bool Descend(int n, List<Vector2I> order, Vector2I[,] down,
                                bool[,] river, short[,] water, short[,] surface)
    {
        bool moved = false;
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
            moved = true;
        }
        return moved;
    }

    /// <summary>
    /// Holds each navigable pair to one water level: the higher cell comes down
    /// to the other, bed and all, so the draught survives.
    /// </summary>
    private static bool LevelPairs(int n, Vector2I[,] twin, bool[,] river,
                                   bool[,] navigable, short[,] water, short[,] surface)
    {
        bool moved = false;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Vector2I a = twin[x, z];
            if (a.X < 0) continue;
            if (!river[x, z] || !river[a.X, a.Y]) continue;
            if (!navigable[x, z] || !navigable[a.X, a.Y]) continue;

            short m = Math.Min(water[x, z], water[a.X, a.Y]);
            if (water[x, z] > m)
            {
                surface[x, z] = (short)(surface[x, z] - (water[x, z] - m));
                water[x, z] = m;
                moved = true;
            }
            if (water[a.X, a.Y] > m)
            {
                surface[a.X, a.Y] = (short)(surface[a.X, a.Y] - (water[a.X, a.Y] - m));
                water[a.X, a.Y] = m;
                moved = true;
            }
        }
        return moved;
    }

    /// <summary>
    /// Runs <see cref="Descend"/> and <see cref="LevelPairs"/> against each other
    /// until both hold at once — each can undo the other, and each only lowers,
    /// so it converges; capped at six passes.
    /// </summary>
    private static void Settle(int n, List<Vector2I> order, Vector2I[,] down,
                               bool[,] river, bool[,] navigable, short[,] water,
                               short[,] surface, Vector2I[,] twin)
    {
        for (int pass = 0; pass < 6; pass++)
        {
            bool moved = Descend(n, order, down, river, water, surface);
            moved |= LevelPairs(n, twin, river, navigable, water, surface);
            if (!moved) break;
        }
    }

    /// <summary>
    /// Makes a navigable river a stair of pools: a step down to the next navigable
    /// cell smaller than <see cref="FallDepth"/> is flattened to the downstream
    /// pool, bed and water together; a step that size or more is kept as a fall.
    /// Walks outlets first, so every cell reads its downstream pool already settled.
    /// </summary>
    private static void FlattenReaches(int n, List<Vector2I> order, Vector2I[,] down,
                                       bool[,] river, bool[,] navigable,
                                       short[,] water, short[,] surface)
    {
        for (int i = 0; i < order.Count; i++)
        {
            Vector2I c = order[i];
            if (!river[c.X, c.Y] || !navigable[c.X, c.Y]) continue;
            Vector2I to = down[c.X, c.Y];
            if (to.X < 0 || !river[to.X, to.Y] || !navigable[to.X, to.Y]) continue;

            int step = water[c.X, c.Y] - water[to.X, to.Y];
            if (step <= 0 || step >= FallDepth) continue;
            water[c.X, c.Y] = water[to.X, to.Y];
            surface[c.X, c.Y] = (short)(surface[c.X, c.Y] - step);
        }
    }

    /// <summary>
    /// Brings the banks down to the free step: a dry cell standing exactly two
    /// above the water beside it comes down one slab, and the correction walks
    /// outward against the same test. Only that step and only by that slab — a
    /// bank three or more above the water is a gorge wall, which the grammar allows.
    /// </summary>
    private static void CutBanks(int n, bool[,] land, short[,] surface, short[,] water,
                                 bool[,] river, byte[,] form, bool[,] keep)
    {
        var queue = new Queue<Vector2I>();

        // Cuttable ground: dry, not a bridgehead, not a landform whose height is the point of it.
        bool Dry(int x, int z)
        {
            if (!InBounds(n, x, z)) return false;
            if (!land[x, z] || water[x, z] != IslandData.NoLand) return false;
            if (keep[x, z]) return false;
            var type = (LandformType)form[x, z];
            return type is LandformType.Plain or LandformType.Hills;
        }

        // How low a cell may go: never into standing water beside it, never within a cliff of a basin floor.
        int Floor(int x, int z)
        {
            int floor = int.MinValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (water[nx, nz] != IslandData.NoLand)
                    floor = Math.Max(floor, water[nx, nz] + 1);
                if ((LandformType)form[nx, nz] == LandformType.Basin)
                    floor = Math.Max(floor, surface[nx, nz] + 3);
            }
            return floor;
        }

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
}
