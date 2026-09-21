using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// Deeps in the running water: the pool a fall digs where it lands, and the deep
/// middle of a long navigable reach. Both move the bed and never the water, so
/// the profile — every course downhill, every pair level — is untouched; a bed
/// dug below its kind's depth is what the traversal reads as not fordable.
/// </summary>
internal static partial class Rivers
{
    /// <summary>Share of inner falls that dig a pool where they land.</summary>
    private const float PlungeChance = 0.75f;

    /// <summary>Slabs a plunge pool is dug below the bed of its kind: half the fall's drop, between these.</summary>
    private const int PlungeMin = 2, PlungeMax = 4;

    /// <summary>Cells a navigable reach needs before its middle may deepen.</summary>
    private const int DeepReachCells = 8;

    /// <summary>Share of reaches that long which deepen.</summary>
    private const float DeepReachChance = 0.5f;

    /// <summary>
    /// Under most inner falls, a plunge pool: the cell the sheet lands on, where that
    /// is water at the level the fall reaches (the course below, a lake, the river
    /// under a lake's spill), has its bed dug half the drop deeper, two slabs to
    /// four; the next cell down the course, at the same level, half that again, so
    /// the pool tails off. Never a rim fall — there is nothing under it. Each pool
    /// is a deep.
    /// </summary>
    private static void DigPlungePools(int seed, int n, bool[,] river, short[,] water, short[,] surface,
                                       Vector2I[,] down, List<Fall> falls, List<Vector2I> deeps)
    {
        var dug = new bool[n, n];
        foreach (Fall f in falls)
        {
            if (f.OffRim) continue;
            int lx = f.Cell.X + f.Flow.X, lz = f.Cell.Y + f.Flow.Y;
            if (!InBounds(n, lx, lz) || dug[lx, lz]) continue;
            if (water[lx, lz] == IslandData.NoLand || water[lx, lz] != f.Bottom) continue;
            if (Hash01(seed, 0x91A6u ^ (uint)(lx * 73856093 ^ lz * 19349663)) >= PlungeChance) continue;

            int extra = Math.Clamp(f.Drop / 2, PlungeMin, PlungeMax);
            surface[lx, lz] = Terrain.SlabClamp(surface[lx, lz] - extra);
            dug[lx, lz] = true;
            deeps.Add(new Vector2I(lx, lz));

            if (!river[lx, lz]) continue;
            Vector2I to = down[lx, lz];
            if (to.X < 0 || !river[to.X, to.Y] || dug[to.X, to.Y]) continue;
            if (water[to.X, to.Y] != water[lx, lz]) continue;
            surface[to.X, to.Y] = Terrain.SlabClamp(surface[to.X, to.Y] - Math.Max(1, extra / 2));
        }
    }

    /// <summary>
    /// Half the navigable reaches of <see cref="DeepReachCells"/> cells or more
    /// deepen in the middle: a reach is the navigable cells at one water level,
    /// its ends the cells with water or aether beside them that is not the reach —
    /// the stream it came from, the pool below its step, a lake, the rim — and
    /// every cell two or more from an end goes a slab or two deeper. Runs before
    /// the falls are found, and reads only the water, so it moves nothing they read.
    /// </summary>
    private static void DeepenReaches(int seed, int n, bool[,] land, bool[,] river, bool[,] navigable,
                                      short[,] water, short[,] surface)
    {
        var reach = new int[n, n];
        var dist = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) reach[x, z] = -1;

        var members = new List<Vector2I>();
        var queue = new Queue<Vector2I>();
        int id = 0;
        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!navigable[sx, sz] || reach[sx, sz] >= 0) continue;

            members.Clear();
            short level = water[sx, sz];
            reach[sx, sz] = id;
            queue.Enqueue(new Vector2I(sx, sz));
            while (queue.Count > 0)
            {
                Vector2I c = queue.Dequeue();
                members.Add(c);
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    if (!InBounds(n, nx, nz) || reach[nx, nz] >= 0) continue;
                    if (!navigable[nx, nz] || water[nx, nz] != level) continue;
                    reach[nx, nz] = id;
                    queue.Enqueue(new Vector2I(nx, nz));
                }
            }
            int here = id++;
            if (members.Count < DeepReachCells) continue;
            if (Hash01(seed, 0x0DEEu ^ (uint)here * 2654435761u) >= DeepReachChance) continue;
            int extra = Hash01(seed, 0x0DEFu ^ (uint)here * 40503u) < 0.4f ? 2 : 1;

            // Cells from the reach's ends, inside it.
            foreach (Vector2I c in members)
            {
                dist[c.X, c.Y] = -1;
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    bool outside = !InBounds(n, nx, nz) || !land[nx, nz]
                                   || (water[nx, nz] != IslandData.NoLand && reach[nx, nz] != here);
                    if (!outside) continue;
                    dist[c.X, c.Y] = 0;
                    queue.Enqueue(c);
                    break;
                }
            }
            while (queue.Count > 0)
            {
                Vector2I c = queue.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    if (!InBounds(n, nx, nz) || reach[nx, nz] != here || dist[nx, nz] >= 0) continue;
                    dist[nx, nz] = dist[c.X, c.Y] + 1;
                    queue.Enqueue(new Vector2I(nx, nz));
                }
            }
            foreach (Vector2I c in members)
            {
                if (dist[c.X, c.Y] >= 0 && dist[c.X, c.Y] < 2) continue;
                surface[c.X, c.Y] = Terrain.SlabClamp(surface[c.X, c.Y] - extra);
            }
        }
    }
}
