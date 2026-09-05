using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

public static partial class Traversal
{
    /// <summary>Floods <see cref="IslandData.Walk"/> under the free-step rule, king's moves included (<see cref="DiagonalOpen"/>), and fills <see cref="IslandData.Areas"/>, largest first.</summary>
    private static void BuildWalkAreas(IslandData d)
    {
        int n = d.Size;
        var areas = new List<WalkArea>();
        var queue = new Queue<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            d.Walk[x, z] = d.HasLand(x, z) && !Walkable(d, x, z) ? Water : -1;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Walkable(d, sx, sz) || d.Walk[sx, sz] != -1) continue;

            int id = areas.Count;
            int area = 0;
            short low = short.MaxValue, high = short.MinValue;
            var min = new Vector2I(sx, sz);
            var max = new Vector2I(sx, sz);

            d.Walk[sx, sz] = id;
            queue.Enqueue((sx, sz));

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                short top = CrossLevel(d, x, z);
                area++;
                if (top < low) low = top;
                if (top > high) high = top;
                min = new Vector2I(Math.Min(min.X, x), Math.Min(min.Y, z));
                max = new Vector2I(Math.Max(max.X, x), Math.Max(max.Y, z));

                for (int k = 0; k < 8; k++)
                {
                    int nx = x + Dx8[k], nz = z + Dz8[k];
                    if (!Walkable(d, nx, nz) || d.Walk[nx, nz] != -1) continue;
                    if (Math.Abs(CrossLevel(d, nx, nz) - top) > 1) continue;
                    // The odd entries of the eight-neighbourhood are the diagonals.
                    if ((k & 1) == 1 && !DiagonalOpen(d, x, z, Dx8[k], Dz8[k])) continue;
                    d.Walk[nx, nz] = id;
                    queue.Enqueue((nx, nz));
                }
            }
            areas.Add(new WalkArea(id, area, low, high, min, max, new Vector2I(sx, sz)));
        }

        List<WalkArea> order = RankByArea(areas, d.Walk, n);
        d.Areas.Clear();
        for (int i = 0; i < order.Count; i++) d.Areas.Add(order[i] with { Id = i });
        d.Mainland = d.Areas.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// The same flood for a player who can build: a face of at most
    /// <see cref="InfrastructureStep"/> slabs, a level deck <see cref="DeckFits"/> allows
    /// within <see cref="MaxBridgeRise"/>, and every quay on a body of water once one of
    /// them is reached. With <paramref name="into"/> it is a scratch pass: ranked the same
    /// way, but <see cref="IslandData.Reaches"/> and Heartland are left alone.
    /// </summary>
    private static void BuildReachAreas(IslandData d, bool ferries = true,
                                        int[,]? into = null)
    {
        int n = d.Size;
        int span = Math.Max(1, d.BridgeSpan);
        int[,] label = into ?? d.Reach;
        var areas = new List<WalkArea>();
        var queue = new Queue<(int X, int Z)>();
        var berths = new BerthIndex(d, ferries);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            label[x, z] = d.HasLand(x, z) && !Walkable(d, x, z) ? Water : -1;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Walkable(d, sx, sz) || label[sx, sz] != -1) continue;

            int id = areas.Count;
            int area = 0;
            short low = short.MaxValue, high = short.MinValue;
            var min = new Vector2I(sx, sz);
            var max = new Vector2I(sx, sz);

            label[sx, sz] = id;
            queue.Enqueue((sx, sz));

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                short top = CrossLevel(d, x, z);
                area++;
                if (top < low) low = top;
                if (top > high) high = top;
                min = new Vector2I(Math.Min(min.X, x), Math.Min(min.Y, z));
                max = new Vector2I(Math.Max(max.X, x), Math.Max(max.Y, z));

                for (int k = 0; k < 4; k++)
                {
                    // Reach 1 is the neighbour; beyond it is a bridge over that many cells.
                    for (int reach = 1; reach <= span + 1; reach++)
                    {
                        int nx = x + Dx[k] * reach, nz = z + Dz[k] * reach;
                        if (!Walkable(d, nx, nz)) continue;

                        bool bridged = reach > 1;
                        if (bridged && !DeckFits(d, x, z, Dx[k], Dz[k], reach, span)) continue;

                        int rise = Math.Abs(CrossLevel(d, nx, nz) - top);
                        if (rise > (bridged ? MaxBridgeRise : InfrastructureStep)) continue;
                        if (label[nx, nz] != -1) continue;

                        label[nx, nz] = id;
                        queue.Enqueue((nx, nz));
                    }
                }

                // The diagonals are walked, never built across: a free step or nothing.
                for (int k = 1; k < 8; k += 2)
                {
                    int nx = x + Dx8[k], nz = z + Dz8[k];
                    if (!Walkable(d, nx, nz) || label[nx, nz] != -1) continue;
                    if (Math.Abs(CrossLevel(d, nx, nz) - top) > 1) continue;
                    if (!DiagonalOpen(d, x, z, Dx8[k], Dz8[k])) continue;
                    label[nx, nz] = id;
                    queue.Enqueue((nx, nz));
                }

                if (!d.Ferry[x, z]) continue;
                List<Vector2I>? far = berths.Open(new Vector2I(x, z));
                if (far == null) continue;

                foreach (Vector2I quay in far)
                {
                    if (label[quay.X, quay.Y] != -1) continue;
                    label[quay.X, quay.Y] = id;
                    queue.Enqueue((quay.X, quay.Y));
                }
            }
            areas.Add(new WalkArea(id, area, low, high, min, max, new Vector2I(sx, sz)));
        }

        List<WalkArea> order = RankByArea(areas, label, n);
        if (into != null) return;

        d.Reaches.Clear();
        for (int i = 0; i < order.Count; i++) d.Reaches.Add(order[i] with { Id = i });
        d.Heartland = d.Reaches.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// The areas sorted largest first (an unstable <c>List.Sort</c>; ties fall as it
    /// leaves them), with <paramref name="label"/> rewritten to match so id 0 is the
    /// largest. The returned list still carries the provisional ids.
    /// </summary>
    private static List<WalkArea> RankByArea(List<WalkArea> areas, int[,] label, int n)
    {
        var order = new List<WalkArea>(areas);
        order.Sort((a, b) => b.Area.CompareTo(a.Area));

        var remap = new int[areas.Count];
        for (int i = 0; i < order.Count; i++) remap[order[i].Id] = i;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (label[x, z] >= 0) label[x, z] = remap[label[x, z]];
        return order;
    }
}
