using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>Connected pieces of the mask: components, the linker that huddles strays within bridge reach, and the bridge sites.</summary>
internal static class Landmasses
{
    /// <summary>Smallest thing that counts as an islet. Below it, it is coastline noise.</summary>
    internal const int MinIsletCells = 30;

    /// <summary>Cells a stray is looked for across before it is dragged: past the widest strait a layout makes, short enough to keep the sweep cheap.</summary>
    private const int Sightline = 48;

    /// <summary>Labels 4-connected land into <paramref name="into"/> (−1 off land) and returns each component's cells, in <see cref="Flood.Label"/> order.</summary>
    internal static List<List<Vector2I>> Components(bool[,] mask, int[,] into)
    {
        var found = new List<List<Vector2I>>();
        Flood.Label(mask.GetLength(0), (x, z) => mask[x, z], into, found);
        return found;
    }

    /// <summary>Labels the land into <paramref name="into"/> and returns the id of the largest component (the first on a tie), or −1 for an empty mask.</summary>
    internal static int LargestComponent(bool[,] mask, int[,] into) => Largest(Components(mask, into));

    /// <summary>Index of the first largest part, −1 if there are none.</summary>
    private static int Largest(List<List<Vector2I>> parts)
    {
        int best = -1;
        for (int i = 0; i < parts.Count; i++)
            if (best < 0 || parts[i].Count > parts[best].Count) best = i;
        return best;
    }

    /// <summary>Clears every component with fewer than <paramref name="minCells"/> cells.</summary>
    internal static void DropComponentsUnder(bool[,] mask, int minCells)
    {
        int n = mask.GetLength(0);
        foreach (List<Vector2I> cells in Components(mask, new int[n, n]))
            if (cells.Count < minCells) Clear(mask, cells);
    }

    /// <summary>Reduces the mask to its largest component; every component tied for largest survives.</summary>
    internal static void KeepLargestComponent(bool[,] mask)
    {
        int n = mask.GetLength(0);
        List<List<Vector2I>> parts = Components(mask, new int[n, n]);
        if (parts.Count <= 1) return;

        int largest = parts[Largest(parts)].Count;
        foreach (List<Vector2I> cells in parts)
            if (cells.Count < largest) Clear(mask, cells);
    }

    /// <summary>
    /// Fills the corner where two cells of the same component touch only diagonally, since a corner
    /// is not a join you can walk. Only within one component: welding two islands merges them.
    /// </summary>
    internal static void CloseDiagonalJoins(bool[,] mask)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        Components(mask, comp);

        for (int x = 1; x + 2 < n; x++)
        for (int z = 1; z + 2 < n; z++)
        {
            bool a = mask[x, z], b = mask[x + 1, z + 1];
            bool c = mask[x + 1, z], e = mask[x, z + 1];

            if (a && b && !c && !e && comp[x, z] == comp[x + 1, z + 1])
                Fill(x + 1, z, x, z + 1);
            else if (c && e && !a && !b && comp[x + 1, z] == comp[x, z + 1])
                Fill(x, z, x + 1, z + 1);
        }

        // The filled cell is the one with more land around it, so the coast stays plausible.
        void Fill(int ax, int az, int bx, int bz)
        {
            bool first = Neighbours(ax, az) >= Neighbours(bx, bz);
            mask[first ? ax : bx, first ? az : bz] = true;
        }

        int Neighbours(int x, int z)
        {
            int found = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int nx = x + dx, nz = z + dz;
                if (!InBounds(n, nx, nz) || (dx == 0 && dz == 0)) continue;
                if (mask[nx, nz]) found++;
            }
            return found;
        }
    }

    /// <summary>
    /// Nudges landmasses together until each faces the linked body across at most <paramref name="span"/>
    /// cells, cardinally: a stray is translated bodily toward it, which keeps its shape. Whatever still
    /// cannot be linked when the round budget runs out is deleted.
    /// </summary>
    internal static void LinkLandmasses(bool[,] mask, int span)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];

        for (int round = 0; round < 40; round++)
        {
            List<List<Vector2I>> parts = Components(mask, comp);
            if (parts.Count <= 1) return;

            HashSet<int> linked = LinkedSet(FacingPairs(mask, comp, span), Largest(parts));
            if (linked.Count == parts.Count) return;

            // The closest stray over the long sightline is dragged the whole way at once (a handful of
            // rounds, not hundreds); with no cardinal sightline a stray slides until it has one.
            var far = FacingPairs(mask, comp, Sightline);
            long bestKey = ClosestStray(far, linked);
            bool progressed = bestKey < 0
                ? SlideStrays(mask, parts, linked)
                : DragStray(mask, comp, parts, linked, far[bestKey], span);
            if (!progressed) return;
        }
        PruneUnlinked(mask, comp, span);
    }

    /// <summary>The key of the closest pair in <paramref name="far"/> with one side linked and one not, −1 if there is none; a tie goes to the earlier pair.</summary>
    private static long ClosestStray(Dictionary<long, (Vector2I A, Vector2I B, int Gap)> far, HashSet<int> linked)
    {
        long bestKey = -1;
        int bestGap = int.MaxValue;
        foreach (var (key, v) in far)
        {
            var (a, b) = Unpair(key);
            if (linked.Contains(a) == linked.Contains(b)) continue;
            if (v.Gap >= bestGap) continue;
            bestGap = v.Gap;
            bestKey = key;
        }
        return bestKey;
    }

    /// <summary>Translates the stray side of <paramref name="pair"/> toward the linked bank until it is within <paramref name="span"/> or blocked; false if it could not move at all.</summary>
    private static bool DragStray(bool[,] mask, int[,] comp, List<List<Vector2I>> parts, HashSet<int> linked,
                                  (Vector2I A, Vector2I B, int Gap) pair, int span)
    {
        var (from, toward, gap) = pair;
        bool fromLinked = linked.Contains(comp[from.X, from.Y]);
        int strayId = fromLinked ? comp[toward.X, toward.Y] : comp[from.X, from.Y];
        Vector2I anchor = fromLinked ? toward : from;
        Vector2I goal = fromLinked ? from : toward;

        int dx = Math.Sign(goal.X - anchor.X);
        int dz = Math.Sign(goal.Y - anchor.Y);
        int want = gap - span;

        int moved = 0;
        while (moved < want && Translate(mask, parts[strayId], dx, dz))
        {
            for (int i = 0; i < parts[strayId].Count; i++)
                parts[strayId][i] = new Vector2I(parts[strayId][i].X + dx, parts[strayId][i].Y + dz);
            moved++;
        }
        return moved > 0;
    }

    /// <summary>
    /// With no cardinal sightline, slides one stray a cell along whichever axis it is closer to aligned
    /// on (the other axis if that is blocked). Every stray is tried, nearest first, so an islet pinned
    /// on the wall does not block the others; false when none can move.
    /// </summary>
    private static bool SlideStrays(bool[,] mask, List<List<Vector2I>> parts, HashSet<int> linked)
    {
        var strays = new List<(int Part, Vector2 Mine, Vector2 Theirs, float Dist)>();
        for (int i = 0; i < parts.Count; i++)
        {
            if (linked.Contains(i)) continue;
            Vector2 mid = Centroid(parts[i]);
            foreach (int j in linked)
            {
                Vector2 other = Centroid(parts[j]);
                strays.Add((i, mid, other, mid.DistanceTo(other)));
            }
        }
        strays.Sort((a, b) => a.Dist.CompareTo(b.Dist));

        foreach (var (part, mine, theirs, _) in strays)
        {
            float ax = theirs.X - mine.X, az = theirs.Y - mine.Y;
            int sx = MathF.Abs(ax) <= MathF.Abs(az) ? Math.Sign(ax) : 0;
            int sz = sx == 0 ? Math.Sign(az) : 0;
            if (sx == 0 && sz == 0) continue;

            if (Translate(mask, parts[part], sx, sz)
                || Translate(mask, parts[part], Math.Sign(ax) - sx, Math.Sign(az) - sz))
                return true;
        }
        return false;
    }

    /// <summary>Deletes every landmass the largest cannot reach by bridge, so the guarantee holds rather than usually holding.</summary>
    private static void PruneUnlinked(bool[,] mask, int[,] comp, int span)
    {
        List<List<Vector2I>> last = Components(mask, comp);
        if (last.Count <= 1) return;

        HashSet<int> survivors = LinkedSet(FacingPairs(mask, comp, span), Largest(last));
        for (int i = 0; i < last.Count; i++)
            if (!survivors.Contains(i)) Clear(mask, last[i]);
    }

    /// <summary>The crossings that hold the archipelago together: one cell pair per bridge, enough to join every landmass to the largest, on the layout as it finally stands.</summary>
    internal static List<(Vector2I A, Vector2I B)> FindBridgeSites(bool[,] mask, int span)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        List<List<Vector2I>> parts = Components(mask, comp);
        var found = new List<(Vector2I A, Vector2I B)>();
        if (parts.Count <= 1) return found;

        LinkedSet(FacingPairs(mask, comp, span), Largest(parts), found);
        return found;
    }

    /// <summary>
    /// Every pair of landmasses facing each other across at most <paramref name="span"/> empty cells,
    /// cardinally, the way a bridge sees it; one sweep for all pairs at once, since pairwise is cubic.
    /// Keyed by <see cref="Pair"/> in sweep order, each holding its narrowest crossing.
    /// </summary>
    private static Dictionary<long, (Vector2I A, Vector2I B, int Gap)> FacingPairs(bool[,] mask, int[,] comp, int span)
    {
        int n = mask.GetLength(0);
        var found = new Dictionary<long, (Vector2I, Vector2I, int)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!mask[x, z]) continue;
            int a = comp[x, z];

            // Only +X and +Z: the opposite ray is the same crossing seen from the far bank.
            for (int k = 0; k < 2; k++)
            {
                int dx = k == 0 ? 1 : 0, dz = k == 0 ? 0 : 1;
                for (int step = 2; step <= span + 1; step++)
                {
                    int nx = x + dx * step, nz = z + dz * step;
                    if (nx >= n || nz >= n) break;
                    // The first solid cell ends the ray: the far bank, or a third island in the way.
                    if (!mask[nx, nz]) continue;

                    int b = comp[nx, nz];
                    if (b != a)
                    {
                        long key = Pair(a, b);
                        int gap = step - 1;
                        if (!found.TryGetValue(key, out var had) || gap < had.Item3)
                            found[key] = (new Vector2I(x, z), new Vector2I(nx, nz), gap);
                    }
                    break;
                }
            }
        }
        return found;
    }

    /// <summary>The landmasses joined into one linkable whole, grown from <paramref name="seedPart"/> over <paramref name="facing"/> to a fixed point; each pair that joined is appended to <paramref name="record"/>.</summary>
    private static HashSet<int> LinkedSet(Dictionary<long, (Vector2I A, Vector2I B, int Gap)> facing, int seedPart,
                                          List<(Vector2I A, Vector2I B)>? record = null)
    {
        var linked = new HashSet<int> { seedPart };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (key, v) in facing)
            {
                var (a, b) = Unpair(key);
                if (linked.Contains(a) == linked.Contains(b)) continue;
                record?.Add((v.A, v.B));
                linked.Add(a);
                linked.Add(b);
                grew = true;
            }
        }
        return linked;
    }

    /// <summary>Order-independent key for two component ids: the smaller in the high word.</summary>
    private static long Pair(int a, int b)
        => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    private static (int A, int B) Unpair(long key) => ((int)(key >> 32), (int)(key & 0xFFFFFFFF));

    /// <summary>Mean of the cells, summed in list order; the sum feeds the strays' sort.</summary>
    private static Vector2 Centroid(List<Vector2I> cells)
    {
        float x = 0f, z = 0f;
        foreach (Vector2I c in cells) { x += c.X; z += c.Y; }
        return new Vector2(x / cells.Count, z / cells.Count);
    }

    /// <summary>Moves one landmass by a cell, refusing if it would leave the field or collide.</summary>
    private static bool Translate(bool[,] mask, List<Vector2I> cells, int dx, int dz)
    {
        if (dx == 0 && dz == 0) return false;
        int n = mask.GetLength(0);

        var moving = new HashSet<Vector2I>(cells);
        foreach (Vector2I c in cells)
        {
            int nx = c.X + dx, nz = c.Y + dz;
            // The one-cell border stays empty so every land cell has a coast.
            if (nx < 1 || nz < 1 || nx >= n - 1 || nz >= n - 1) return false;
            if (mask[nx, nz] && !moving.Contains(new Vector2I(nx, nz))) return false;
        }

        foreach (Vector2I c in cells) mask[c.X, c.Y] = false;
        foreach (Vector2I c in cells) mask[c.X + dx, c.Y + dz] = true;
        return true;
    }

    private static void Clear(bool[,] mask, List<Vector2I> cells)
    {
        foreach (Vector2I c in cells) mask[c.X, c.Y] = false;
    }
}
