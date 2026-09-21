using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>The underside: a thin lip at the coastline descending inland to a deep keel.</summary>
internal static class Keel
{
    /// <summary>Slabs the underside may rise per cell away from ground it was pushed down under; half as much again on a diagonal.</summary>
    private const int RootRise = 3;

    /// <summary>
    /// Hangs the underside below the surface as an absolute level, not a thickness
    /// subtracted from the surface (that would mirror the relief downward); a
    /// minimum-thickness clamp keeps every column solid, and <see cref="Root"/> keeps
    /// every column in the rock of the ones beside it.
    /// </summary>
    internal static short[,] BuildKeel(int seed, IslandParams p, bool[,] land, short[,] surface,
                                      float[,] toCoast)
    {
        int n = p.Size;
        var crag = new Noise(seed + 404, frequency: 0.05f, octaves: 3);
        var sway = new Noise(seed + 505, frequency: 0.015f, octaves: 2);
        var warpX = new Noise(seed + 811, frequency: 0.028f, octaves: 3);
        var warpZ = new Noise(seed + 822, frequency: 0.028f, octaves: 3);

        // Warping where the distance field is sampled bends its contours; noise added
        // to the depth afterwards only ripples a surface of revolution.
        float warpAmp = Footprint.AutoRadius(p) * (0.25f + 0.45f * Math.Clamp(p.KeelRoughness, 0f, 1f));

        float maxCoast = 1f;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && toCoast[x, z] > maxCoast) maxCoast = toCoast[x, z];

        float scale = Math.Clamp(maxCoast / MathF.Max(3f, Footprint.AutoRadius(p) * 0.75f), 0.25f, 1f);
        float edge = MathF.Max(1f, p.EdgeThickness);
        const float taper = 0.85f;   // a constant, not a knob: the player never stands on it

        var keel = new short[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) { keel[x, z] = IslandData.NoLand; continue; }

            float wx = x + (warpX.At(x, z) - 0.5f) * 2f * warpAmp;
            float wz = z + (warpZ.At(x, z) - 0.5f) * 2f * warpAmp;
            float inland = FieldOps.Sample(toCoast, wx, wz);

            float t = Math.Clamp(inland / maxCoast * (0.72f + 0.56f * sway.At(x, z)), 0f, 1f);
            float depth = edge + p.KeelDepth * scale * MathF.Pow(t, taper);

            // Crag scales with depth: a ragged keel, a clean lip.
            depth += (crag.At(x, z) - 0.5f) * 2f * p.KeelRoughness * (2f + depth * 0.35f);

            int floorY = -Mathf.RoundToInt(MathF.Max(1f, depth));
            int k = Math.Min(floorY, surface[x, z] - (int)edge);          // keep columns solid
            keel[x, z] = Terrain.SlabClamp(Math.Min(k, surface[x, z] - 1));
        }
        Root(n, land, surface, keel, (int)edge);
        return keel;
    }

    /// <summary>
    /// Keeps the landmass in one piece under deep-cut ground. The keel is a level, and a
    /// bed dug below it — a deep lake, a plunge pool, a canyon floor near a thin rim —
    /// took its own column down and left the ones beside it where they hung, so the bed
    /// stood clear of the island with its water open to the aether. Every column's
    /// underside is brought to at least <paramref name="edge"/> slabs under the lowest
    /// ground beside it (king's moves), so the rock round a bed reaches under the bed;
    /// then the push spreads outward through the land, rising <see cref="RootRise"/>
    /// slabs a cell, so the bed sits in a tapering root rather than on a sheer plug.
    /// Only lowers, and the result does not depend on the order cells are visited in:
    /// each ends at the least of its own level and every push's level plus its walk.
    /// </summary>
    private static void Root(int n, bool[,] land, short[,] surface, short[,] keel, int edge)
    {
        var queue = new Queue<Vector2I>();
        var queued = new bool[n, n];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int low = keel[x, z];
            for (int k = 0; k < 8; k++)
            {
                int nx = x + Dx8[k], nz = z + Dz8[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                low = Math.Min(low, surface[nx, nz] - edge);
            }
            if (low >= keel[x, z]) continue;
            keel[x, z] = Terrain.SlabClamp(low);
            queue.Enqueue(new Vector2I(x, z));
            queued[x, z] = true;
        }

        while (queue.Count > 0)
        {
            Vector2I c = queue.Dequeue();
            queued[c.X, c.Y] = false;
            for (int k = 0; k < 8; k++)
            {
                int nx = c.X + Dx8[k], nz = c.Y + Dz8[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                int rise = Dx8[k] != 0 && Dz8[k] != 0 ? RootRise + RootRise / 2 : RootRise;
                int level = keel[c.X, c.Y] + rise;
                if (level >= keel[nx, nz]) continue;
                keel[nx, nz] = Terrain.SlabClamp(level);
                if (queued[nx, nz]) continue;
                queue.Enqueue(new Vector2I(nx, nz));
                queued[nx, nz] = true;
            }
        }
    }

    /// <summary>
    /// Distance in cells from each land cell to the nearest non-land cell as a
    /// smooth field: a chamfer (3,4) transform approximates the Euclidean metric
    /// (4-neighbour BFS would give diamonds), and a blur removes the integer steps.
    /// </summary>
    internal static float[,] DistanceToCoast(bool[,] land)
    {
        int n = land.GetLength(0);
        const int Far = 1 << 20;
        var d = new int[n, n];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            d[x, z] = land[x, z] ? Far : 0;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            int best = d[x, z];
            best = Math.Min(best, Probe(d, n, x - 1, z, 3));
            best = Math.Min(best, Probe(d, n, x, z - 1, 3));
            best = Math.Min(best, Probe(d, n, x - 1, z - 1, 4));
            best = Math.Min(best, Probe(d, n, x + 1, z - 1, 4));
            d[x, z] = best;
        }
        for (int x = n - 1; x >= 0; x--)
        for (int z = n - 1; z >= 0; z--)
        {
            int best = d[x, z];
            best = Math.Min(best, Probe(d, n, x + 1, z, 3));
            best = Math.Min(best, Probe(d, n, x, z + 1, 3));
            best = Math.Min(best, Probe(d, n, x + 1, z + 1, 4));
            best = Math.Min(best, Probe(d, n, x - 1, z + 1, 4));
            d[x, z] = best;
        }

        var f = new float[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            f[x, z] = d[x, z] / 3f;

        FieldOps.Blur(f, land, passes: 3);
        return f;
    }

    /// <summary>The saturating chamfer step: a neighbour's distance plus its cost, or the maximum off-grid.</summary>
    private static int Probe(int[,] d, int n, int x, int z, int cost)
    {
        if (!InBounds(n, x, z)) return int.MaxValue;
        int v = d[x, z];
        return v >= int.MaxValue - cost ? int.MaxValue : v + cost;
    }
}
