using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>Connected pieces of the mask: components, the linker that huddles strays within bridge reach, and the bridge sites.</summary>
internal static class Landmasses
{
    /// <summary>
    /// Deletes landmasses smaller than <paramref name="keepFraction"/> of the
    /// largest. A deep bite can sever a cape from the mainland, which reads as a
    /// generation accident rather than an archipelago; a deliberate one comes
    /// from <see cref="IslandParams.Fragmentation"/> and survives if it is of
    /// comparable size.
    /// </summary>
    /// <summary>Reduces the mask to its single largest 4-connected component.</summary>
    /// <summary>
    /// Fills the corner where two cells <b>of the same landmass</b> touch only
    /// diagonally. A corner is not a join you can walk, so left alone it is a
    /// hairline break the component filter cannot see — both sides are already
    /// one component, so nothing else will notice.
    ///
    /// <b>Only within a landmass.</b> Welding a diagonal touch between two
    /// separate islands does not heal anything, it deletes an island: two of them
    /// become one. That is what left a third of Twins with a single landmass, and
    /// no amount of pushing the blobs apart fixed it, because the merge happened
    /// after the footprint was drawn. Two islands a corner apart are two islands,
    /// and the bridge rule is what joins them.
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

        // The filled cell is the one with more land around it, so the coast stays
        // plausible.
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

    internal static void KeepLargestComponent(bool[,] mask) => DropSmallComponents(mask, 1f);

    /// <summary>
    /// Labels 4-connected land into <paramref name="into"/> and returns the label
    /// of the largest piece, or -1 for an empty mask.
    /// </summary>
    internal static int LargestComponent(bool[,] mask, int[,] into)
    {
        List<List<Vector2I>> comps = Components(mask, into);
        int best = -1, bestSize = -1;
        for (int i = 0; i < comps.Count; i++)
            if (comps[i].Count > bestSize)
            {
                bestSize = comps[i].Count;
                best = i;
            }
        return best;
    }

    /// <summary>Smallest thing that counts as an islet. Below it, it is coastline noise.</summary>
    internal const int MinIsletCells = 30;

    /// <summary>Labels 4-connected land; returns per-component cell lists.</summary>
    internal static List<List<Vector2I>> Components(bool[,] mask, int[,] into)
    {
        int n = mask.GetLength(0);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) into[x, z] = -1;

        var found = new List<List<Vector2I>>();
        var stack = new Stack<Vector2I>();

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!mask[sx, sz] || into[sx, sz] >= 0) continue;

            int id = found.Count;
            var cells = new List<Vector2I>();
            into[sx, sz] = id;
            stack.Push(new Vector2I(sx, sz));

            while (stack.Count > 0)
            {
                Vector2I c = stack.Pop();
                cells.Add(c);
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!mask[nx, nz] || into[nx, nz] >= 0) continue;
                    into[nx, nz] = id;
                    stack.Push(new Vector2I(nx, nz));
                }
            }
            found.Add(cells);
        }
        return found;
    }

    internal static void DropComponentsUnder(bool[,] mask, int minCells)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        List<List<Vector2I>> parts = Components(mask, comp);
        foreach (List<Vector2I> cells in parts)
        {
            if (cells.Count >= minCells) continue;
            foreach (Vector2I c in cells) mask[c.X, c.Y] = false;
        }
    }

    /// <summary>
    /// Every pair of landmasses that face each other across at most
    /// <paramref name="span"/> empty cells, <b>cardinally</b> — the way a bridge
    /// sees it. A diagonal near-miss is not a crossing.
    ///
    /// One sweep of the grid for all pairs at once. Asking pair by pair is the
    /// obvious shape and it is cubic: a Triplets layout spent 400 ms per island
    /// re-scanning the whole field for every candidate on every nudge.
    /// </summary>
    private static Dictionary<long, (Vector2I A, Vector2I B, int Gap)> FacingPairs(
        bool[,] mask, int[,] comp, int span)
    {
        int n = mask.GetLength(0);
        var found = new Dictionary<long, (Vector2I, Vector2I, int)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!mask[x, z]) continue;
            int a = comp[x, z];

            // Only +X and +Z: the opposite ray is the same crossing seen from the
            // far bank.
            for (int k = 0; k < 2; k++)
            {
                int dx = k == 0 ? 1 : 0, dz = k == 0 ? 0 : 1;
                for (int step = 2; step <= span + 1; step++)
                {
                    int nx = x + dx * step, nz = z + dz * step;
                    if (nx >= n || nz >= n) break;
                    // The first solid cell ends the ray: it is either the far bank
                    // or a third island in the way.
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

    private static Vector2 Centroid(List<Vector2I> cells)
    {
        float x = 0f, z = 0f;
        foreach (Vector2I c in cells) { x += c.X; z += c.Y; }
        return new Vector2(x / cells.Count, z / cells.Count);
    }

    private static long Pair(int a, int b)
        => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    /// <summary>
    /// Which landmasses are already joined into one linkable whole, growing out
    /// from the largest.
    /// </summary>
    private static HashSet<int> LinkedSet(Dictionary<long, (Vector2I A, Vector2I B, int Gap)> facing,
                                          int seedPart, int span)
    {
        var linked = new HashSet<int> { seedPart };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (key, v) in facing)
            {
                if (v.Gap > span) continue;
                int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
                if (linked.Contains(a) == linked.Contains(b)) continue;
                linked.Add(a);
                linked.Add(b);
                grew = true;
            }
        }
        return linked;
    }

    /// <summary>
    /// Nudges landmasses together until every one can be reached from the next by
    /// a bridge — land facing land across at most
    /// <see cref="IslandParams.Crossings"/> cells, cardinally.
    ///
    /// An archipelago is meant to be an island you build your way across, not a
    /// set of separate worlds. A layout that is *nearly* linkable is the common
    /// case — the lobes are placed to nearly touch and the coastline noise decides
    /// whether they do — so rather than rejecting and re-rolling the whole
    /// footprint, the offending piece is translated bodily toward the body it
    /// should be joined to. That preserves its shape, where widening it or filling
    /// the gap with a spit would leave a visible causeway.
    ///
    /// Anything that still cannot be linked is deleted. A piece nobody can ever
    /// reach is not content.
    /// </summary>
    internal static void LinkLandmasses(bool[,] mask, int span)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        // Far enough to cross the widest strait a lobe layout produces, and short
        // enough that the sweep stays cheap.
        const int Sightline = 48;

        for (int round = 0; round < 40; round++)
        {
            List<List<Vector2I>> parts = Components(mask, comp);
            if (parts.Count <= 1) return;

            int biggest = 0;
            for (int i = 1; i < parts.Count; i++)
                if (parts[i].Count > parts[biggest].Count) biggest = i;

            var near = FacingPairs(mask, comp, span);
            HashSet<int> linked = LinkedSet(near, biggest, span);
            if (linked.Count == parts.Count) return;

            // The closest stray, over a long sightline this time, and how far it
            // has to travel. Moving the whole distance at once is what keeps this
            // to a handful of rounds instead of hundreds.
            var far = FacingPairs(mask, comp, Sightline);
            long bestKey = -1;
            int bestGap = int.MaxValue;

            foreach (var (key, v) in far)
            {
                int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
                if (linked.Contains(a) == linked.Contains(b)) continue;
                if (v.Gap >= bestGap) continue;
                bestGap = v.Gap;
                bestKey = key;
            }

            if (bestKey < 0)
            {
                // Nothing adrift has a cardinal sightline to the linked body — two
                // islands set diagonally apart can miss each other entirely on both
                // axes. Deleting one was the first answer and it was wrong: it is
                // how a third of Twins came out with a single island. Slide the
                // stray sideways instead, along whichever axis it is *closer* to
                // aligned on, until it does have a line of sight.
                // Every stray, nearest first — not just the nearest one. An islet
                // pinned against the footprint wall cannot move toward a linked
                // body that lies further out, and giving up there abandoned every
                // *other* stray with it, which is how a seventeen-island layout
                // came out with one piece adrift.
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

                bool slid = false;
                foreach (var (part, mine, theirs, _) in strays)
                {
                    float ax = theirs.X - mine.X, az = theirs.Y - mine.Y;
                    int sx = MathF.Abs(ax) <= MathF.Abs(az) ? Math.Sign(ax) : 0;
                    int sz = sx == 0 ? Math.Sign(az) : 0;
                    if (sx == 0 && sz == 0) continue;

                    // If the slide is blocked, try the other axis before moving on.
                    // Deleting a stray here was costing whole islands: an
                    // arrangement that promised two landmasses would quietly
                    // deliver one.
                    if (Translate(mask, parts[part], sx, sz)
                        || Translate(mask, parts[part], Math.Sign(ax) - sx, Math.Sign(az) - sz))
                    {
                        slid = true;
                        break;
                    }
                }
                if (!slid) return;              // nothing adrift can move at all
                continue;
            }

            var (from, toward, gap) = far[bestKey];
            int strayId = linked.Contains(comp[from.X, from.Y])
                ? comp[toward.X, toward.Y]
                : comp[from.X, from.Y];
            Vector2I anchor = linked.Contains(comp[from.X, from.Y]) ? toward : from;
            Vector2I goal = linked.Contains(comp[from.X, from.Y]) ? from : toward;

            int dx = Math.Sign(goal.X - anchor.X);
            int dz = Math.Sign(goal.Y - anchor.Y);
            int want = gap - span;

            int moved = 0;
            while (moved < want && Translate(mask, parts[strayId], dx, dz))
            {
                for (int i = 0; i < parts[strayId].Count; i++)
                    parts[strayId][i] = new Vector2I(parts[strayId][i].X + dx,
                                                     parts[strayId][i].Y + dz);
                moved++;
            }
            // Blocked before it could move at all: the pieces are already as close
            // as the field allows. Leave it — the final sweep decides whether it
            // is linked, and deleting here would silently drop a landmass the
            // arrangement promised.
            if (moved == 0) return;
        }

        // Budget spent. Whatever is still adrift goes, so the guarantee holds
        // rather than merely usually holding.
        List<List<Vector2I>> last = Components(mask, comp);
        if (last.Count <= 1) return;

        int keep = 0;
        for (int i = 1; i < last.Count; i++) if (last[i].Count > last[keep].Count) keep = i;

        HashSet<int> survivors = LinkedSet(FacingPairs(mask, comp, span), keep, span);
        for (int i = 0; i < last.Count; i++)
            if (!survivors.Contains(i))
                foreach (Vector2I c in last[i]) mask[c.X, c.Y] = false;
    }

    /// <summary>
    /// The crossings that hold an archipelago together: one cell pair per bridge,
    /// enough to join every landmass into a single linked set. Found after the
    /// nudging, on the layout as it finally stands.
    /// </summary>
    internal static List<(Vector2I A, Vector2I B)> FindBridgeSites(bool[,] mask, int span)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        List<List<Vector2I>> parts = Components(mask, comp);
        var found = new List<(Vector2I, Vector2I)>();
        if (parts.Count <= 1) return found;

        int biggest = 0;
        for (int i = 1; i < parts.Count; i++)
            if (parts[i].Count > parts[biggest].Count) biggest = i;

        var facing = FacingPairs(mask, comp, span);
        var linked = new HashSet<int> { biggest };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (key, v) in facing)
            {
                int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
                if (linked.Contains(a) == linked.Contains(b)) continue;
                found.Add((v.A, v.B));
                linked.Add(a);
                linked.Add(b);
                grew = true;
            }
        }
        return found;
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

    private static void DropSmallComponents(bool[,] mask, float keepFraction)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) comp[x, z] = -1;

        var sizes = new List<int>();
        var stack = new Stack<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!mask[x, z] || comp[x, z] >= 0) continue;
            int id = sizes.Count, area = 0;
            comp[x, z] = id;
            stack.Push((x, z));
            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Pop();
                area++;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!mask[nx, nz] || comp[nx, nz] >= 0) continue;
                    comp[nx, nz] = id;
                    stack.Push((nx, nz));
                }
            }
            sizes.Add(area);
        }

        if (sizes.Count <= 1) return;

        int largest = 0;
        foreach (int a in sizes) largest = Math.Max(largest, a);
        // At keepFraction 1 only a component matching the largest survives, which
        // is how KeepLargestComponent reduces the island to one piece.
        int floor = (int)(largest * keepFraction);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (mask[x, z] && sizes[comp[x, z]] < floor) mask[x, z] = false;
    }
}
