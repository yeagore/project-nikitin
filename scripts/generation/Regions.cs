using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// The relief envelope and the patchwork of regions: where the high ground lies,
/// which cells form one patch, which patches touch, and how far inside its patch
/// a cell sits.
/// </summary>
internal static class Regions
{
    /// <summary>Relief left at the shoreline, as a fraction of the cell's inland relief.</summary>
    private const float CoastLow = 0.45f;

    /// <summary>Cells inland over which <see cref="CoastLow"/> recovers to full relief.</summary>
    private const float CoastTaperCells = 3.5f;

    /// <summary>
    /// Per-cell envelope in <c>[0, 1]</c> saying where this island's high ground lies.
    /// It never shapes elevation directly: it only biases which rung a region lands
    /// on and where mountains cluster.
    /// </summary>
    internal static float[,] ReliefEnvelope(int seed, IslandParams p, bool[,] land, float[,] toCoast)
    {
        int n = p.Size;
        float radius = Footprint.AutoRadius(p);
        var centre = new Vector2((n - 1) * 0.5f, (n - 1) * 0.5f);
        ReliefStyle style = Roster.ResolveStyle(seed, p);

        float a1 = TerrainHash01(seed, 0x7A11) * Mathf.Tau;
        float a2 = TerrainHash01(seed, 0x1B93) * Mathf.Tau;
        var axis = new Vector2(MathF.Cos(a1), MathF.Sin(a1));
        var p1 = centre + axis * radius * (0.30f + 0.20f * TerrainHash01(seed, 0x44D2));
        var p2 = centre + new Vector2(MathF.Cos(a2), MathF.Sin(a2))
                          * radius * (0.30f + 0.25f * TerrainHash01(seed, 0x6E05));

        var drift = new Noise(seed + 606, frequency: 0.02f, octaves: 3);

        var envelope = new float[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            var cell = new Vector2(x, z);

            float v = style switch
            {
                ReliefStyle.OffsetPeak => Dome(cell, p1, radius * 0.85f),
                ReliefStyle.TwinPeaks => MathF.Max(Dome(cell, p1, radius * 0.65f),
                                                   Dome(cell, p2, radius * 0.55f) * 0.85f),
                ReliefStyle.Ridge => Spine(cell, centre, axis, radius),
                ReliefStyle.Plateau => FieldOps.SmoothStep(1f, 0.55f, centre.DistanceTo(cell) / radius),
                ReliefStyle.Tilted => 0.18f + 0.82f * (0.5f + 0.5f * axis.Dot(cell - centre) / radius),
                _ => Dome(cell, centre, radius),
            };

            v = v * 0.7f + drift.At(x, z) * 0.3f;
            float taper = Mathf.Lerp(CoastLow, 1f,
                FieldOps.SmoothStep(0f, CoastTaperCells, toCoast[x, z]));
            envelope[x, z] = Math.Clamp(v, 0f, 1f) * taper;
        }
        return envelope;
    }

    private static float Dome(Vector2 cell, Vector2 c, float r)
    {
        float d = MathF.Min(1f, cell.DistanceTo(c) / MathF.Max(r, 1e-3f));
        return 1f - d * d;
    }

    /// <summary>
    /// A narrow ridge along an axis; under a Ridge envelope it becomes a mountain
    /// chain crossing the isle.
    /// </summary>
    private static float Spine(Vector2 cell, Vector2 c, Vector2 axis, float radius)
    {
        Vector2 rel = cell - c;
        float along = axis.Dot(rel);
        float perp = (rel - axis * along).Length();
        float flank = 1f - MathF.Min(1f, perp / (radius * 0.30f));
        float ends = 1f - MathF.Min(1f, MathF.Abs(along) / (radius * 1.45f));
        return flank * flank * ends;
    }

    /// <summary>
    /// Jittered-grid Voronoi with a domain-warped lookup, split into connected components,
    /// then every component under <see cref="IslandParams.MinRegionArea"/> folded into the
    /// neighbour it shares the most border with: the coastline slices off slivers too small to read.
    /// </summary>
    internal static int[,] BuildRegions(int seed, IslandParams p, bool[,] land, out int count)
    {
        int n = p.Size;
        int[,] raw = Partition(seed, p, land);

        var comp = new int[n, n];
        var members = LabelComponents(n, land, raw, comp);
        MergeSlivers(n, land, Math.Max(4, p.MinRegionArea), comp, members);
        return Reindex(n, land, comp, members, out count);
    }

    /// <summary>
    /// Labels the connected components of equal Voronoi id by depth-first search, ids in
    /// scan order into <paramref name="comp"/>. Each member list is in pop order, which is
    /// the insertion order of <see cref="MergeSlivers"/>'s shared-border dictionary.
    /// </summary>
    private static List<List<(int X, int Z)>> LabelComponents(int n, bool[,] land, int[,] raw, int[,] comp)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) comp[x, z] = -1;

        var members = new List<List<(int X, int Z)>>();
        var stack = new Stack<(int X, int Z)>();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z] || comp[x, z] >= 0) continue;

            int id = members.Count;
            var cells = new List<(int X, int Z)>();
            members.Add(cells);
            int key = raw[x, z];

            comp[x, z] = id;
            stack.Push((x, z));
            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Pop();
                cells.Add((cx, cz));
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!land[nx, nz] || comp[nx, nz] >= 0 || raw[nx, nz] != key) continue;
                    comp[nx, nz] = id;
                    stack.Push((nx, nz));
                }
            }
        }
        return members;
    }

    /// <summary>
    /// Repeatedly folds the smallest component under <paramref name="minArea"/> into the
    /// neighbour with the longest shared border (ties: the larger neighbour, then the first
    /// met). An islet with no neighbour is locked and left as it is.
    /// </summary>
    private static void MergeSlivers(int n, bool[,] land, int minArea, int[,] comp,
                                     List<List<(int X, int Z)>> members)
    {
        var locked = new bool[members.Count];

        for (int guard = 0; guard < 4096; guard++)
        {
            int worst = -1;
            for (int i = 0; i < members.Count; i++)
            {
                if (locked[i] || members[i].Count == 0 || members[i].Count >= minArea) continue;
                if (worst < 0 || members[i].Count < members[worst].Count) worst = i;
            }
            if (worst < 0) break;

            var shared = new Dictionary<int, int>();
            foreach (var (x, z) in members[worst])
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (!land[nx, nz]) continue;
                int other = comp[nx, nz];
                if (other == worst) continue;
                shared.TryGetValue(other, out int c);
                shared[other] = c + 1;
            }

            if (shared.Count == 0) { locked[worst] = true; continue; }

            int target = -1, bestShared = -1;
            foreach (var (other, c) in shared)
                if (c > bestShared || (c == bestShared && members[other].Count > members[target].Count))
                {
                    bestShared = c;
                    target = other;
                }

            foreach (var (x, z) in members[worst]) comp[x, z] = target;
            members[target].AddRange(members[worst]);
            members[worst].Clear();
        }
    }

    /// <summary>Renumbers the surviving components densely; −1 on aether.</summary>
    private static int[,] Reindex(int n, bool[,] land, int[,] comp,
                                  List<List<(int X, int Z)>> members, out int count)
    {
        var remap = new int[members.Count];
        Array.Fill(remap, -1);
        count = 0;
        for (int i = 0; i < members.Count; i++)
            if (members[i].Count > 0) remap[i] = count++;

        var region = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            region[x, z] = land[x, z] ? remap[comp[x, z]] : -1;
        return region;
    }

    /// <summary>
    /// Raw Voronoi id per land cell: jittered sites on a step-sized grid, looked up at a
    /// domain-warped position; the nearest of the 3×3 surrounding sites wins, the first on a tie.
    /// </summary>
    private static int[,] Partition(int seed, IslandParams p, bool[,] land)
    {
        int n = p.Size;
        int step = Math.Max(4, p.RegionScale);
        int cols = (n + step - 1) / step + 2;

        var sx = new float[cols, cols];
        var sz = new float[cols, cols];
        for (int i = 0; i < cols; i++)
        for (int j = 0; j < cols; j++)
        {
            uint key = (uint)i * 73856093u ^ (uint)j * 19349663u;
            sx[i, j] = (i - 0.5f + 0.2f + 0.6f * TerrainHash01(seed, key)) * step;
            sz[i, j] = (j - 0.5f + 0.2f + 0.6f * TerrainHash01(seed, key ^ 0x9E3779B9u)) * step;
        }

        var warpX = new Noise(seed + 707, frequency: 0.035f, octaves: 2);
        var warpZ = new Noise(seed + 808, frequency: 0.035f, octaves: 2);
        float warpAmp = step * 0.5f;

        var raw = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            raw[x, z] = -1;
            if (!land[x, z]) continue;

            float wx = x + (warpX.At(x, z) - 0.5f) * 2f * warpAmp;
            float wz = z + (warpZ.At(x, z) - 0.5f) * 2f * warpAmp;

            int gi = Math.Clamp((int)MathF.Floor(wx / step) + 1, 0, cols - 1);
            int gj = Math.Clamp((int)MathF.Floor(wz / step) + 1, 0, cols - 1);

            float best = float.MaxValue;
            int bi = gi, bj = gj;
            for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
            {
                int i = gi + di, j = gj + dj;
                if (i < 0 || j < 0 || i >= cols || j >= cols) continue;
                float ddx = wx - sx[i, j], ddz = wz - sz[i, j];
                float d2 = ddx * ddx + ddz * ddz;
                if (d2 < best) { best = d2; bi = i; bj = j; }
            }
            raw[x, z] = bi * cols + bj;
        }
        return raw;
    }

    /// <summary>
    /// Border cells per unordered region pair, plus each region's neighbour set. Both are
    /// read downstream in insertion order, so the scan order here is part of the result.
    /// </summary>
    internal static Dictionary<long, List<(int X, int Z)>> BuildBorders(
        bool[,] land, int[,] region, int count, out HashSet<int>[] neighbours)
    {
        int n = land.GetLength(0);
        var borders = new Dictionary<long, List<(int X, int Z)>>();
        neighbours = new HashSet<int>[count];
        for (int i = 0; i < count; i++) neighbours[i] = new HashSet<int>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int a = region[x, z];
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (!land[nx, nz]) continue;
                int b = region[nx, nz];
                if (b == a) continue;

                neighbours[a].Add(b);
                long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                if (!borders.TryGetValue(key, out var list))
                    borders[key] = list = new List<(int X, int Z)>();
                list.Add((x, z));
            }
        }
        return borders;
    }

    /// <summary>Mean of a field over each region's cells, summed in scan order.</summary>
    internal static float[] RegionMean(bool[,] land, int[,] region, int count, float[,] field)
    {
        var sum = new float[count];
        var seen = new int[count];
        int n = land.GetLength(0);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int r = region[x, z];
            if (r < 0 || r >= count) continue;
            sum[r] += field[x, z];
            seen[r]++;
        }
        for (int r = 0; r < count; r++) if (seen[r] > 0) sum[r] /= seen[r];
        return sum;
    }

    /// <summary>Cell count per region.</summary>
    internal static int[] RegionCells(bool[,] land, int[,] region, int count)
    {
        int n = land.GetLength(0);
        var cells = new int[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z]) cells[region[x, z]]++;
        return cells;
    }

    /// <summary>
    /// Distance from each cell to its own region's border, normalised by the region's
    /// deepest cell to <c>[0, 1]</c>.
    /// </summary>
    internal static float[,] InwardDistance(bool[,] land, int[,] region, int count)
    {
        int n = land.GetLength(0);
        int[,] dist = Flood.Distance(n,
            (x, z) => land[x, z] && OnRegionEdge(n, land, region, x, z),
            (x, z, nx, nz) => land[nx, nz] && region[nx, nz] == region[x, z]);

        var peak = new int[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && dist[x, z] > peak[region[x, z]]) peak[region[x, z]] = dist[x, z];

        var u = new float[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z]) u[x, z] = dist[x, z] / (float)Math.Max(1, peak[region[x, z]]);
        return u;
    }

    /// <summary>A land cell with the grid edge, aether or another region beside it.</summary>
    private static bool OnRegionEdge(int n, bool[,] land, int[,] region, int x, int z)
    {
        for (int k = 0; k < 4; k++)
        {
            int nx = x + Dx[k], nz = z + Dz[k];
            if (!InBounds(n, nx, nz) || !land[nx, nz] || region[nx, nz] != region[x, z])
                return true;
        }
        return false;
    }
}
