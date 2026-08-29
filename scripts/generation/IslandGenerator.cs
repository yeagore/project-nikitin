using System;
using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Deterministic island generator: <see cref="Generate"/> is a pure function of
/// <c>(seed, params)</c>. All Y values it works in are <b>slab indices</b>.
/// Pipeline stages are documented in docs/island-generation.md §4. Implemented:
/// 1 (mask), 2 (height), 3 (terracing — without the minimum-width morphological
/// open yet), 4 (keel + one span per column). Overhangs (4b), feature anchors
/// and guarantees are still to come.
/// </summary>
public sealed class IslandGenerator
{
    public IslandData Generate(int seed, IslandParams p)
    {
        int n = p.Size;
        var data = new IslandData(n);

        bool[,] land = BuildMask(seed, p);
        float[,] height = BuildHeight(seed, p, land);
        short[,] surface = Terrace(seed, p, land, height);
        short[,] keel = BuildKeel(seed, p, land, surface);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            data.Land[x, z] = land[x, z];
            if (!land[x, z]) continue;

            short top = surface[x, z];
            short bottom = keel[x, z];
            if (bottom > top) bottom = top;                 // safety
            data.Spans[x, z] = new[] { new Span(bottom, top) };
            data.Material[x, z] = 0;
        }
        return data;
    }

    // ---- Stage 1: footprint mask ----------------------------------------------

    private static bool[,] BuildMask(int seed, IslandParams p)
    {
        int n = p.Size;
        float radius = AutoRadius(p);
        float cx = (n - 1) * 0.5f, cz = (n - 1) * 0.5f;

        var shape = new Noise(seed, frequency: 0.05f, octaves: 4)
            .WithWarp(amplitude: 0.35f * n, frequency: 0.6f / n);
        var blobs = new Noise(seed + 17, frequency: 0.09f, octaves: 3, ridged: true);

        var field = new float[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            float d = NormRadius(x, z, cx, cz, radius);
            float fall = 1f - FieldOps.SmoothStep(0.45f, 1f, d);
            float body = 0.35f + 0.65f * shape.At(x, z);
            float frag = Mathf.Lerp(1f, blobs.At(x, z), p.Fragmentation);
            field[x, z] = fall * body * frag;
        }

        float threshold = FieldOps.Quantile(field, 1f - Math.Clamp(p.Coverage, 0.01f, 0.99f));

        var mask = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            bool insideBox = NormRadius(x, z, cx, cz, radius) < 1.15f;
            mask[x, z] = insideBox && field[x, z] > threshold;
        }
        return mask; // connected-component cleanup is a later stage
    }

    // ---- Stage 2: raw height (slab units) -----------------------------------

    private static float[,] BuildHeight(int seed, IslandParams p, bool[,] land)
    {
        int n = p.Size;
        float radius = AutoRadius(p);
        float cx = (n - 1) * 0.5f, cz = (n - 1) * 0.5f;
        float gain = 0.35f + 0.30f * p.Roughness;

        var baseNoise = new Noise(seed + 101, frequency: 0.04f, octaves: 5, gain: gain);
        var ridge = new Noise(seed + 202, frequency: 0.03f, octaves: 4, ridged: true);

        float peak = 1f + p.HeightScale * p.Relief;         // slabs

        var h = new float[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            float d = Math.Min(1f, NormRadius(x, z, cx, cz, radius));
            float bias = 1f - d * d;                         // relief rises inland
            float b = baseNoise.At(x, z);
            float m = ridge.At(x, z);
            float mix = Mathf.Lerp(b, 0.4f * b + 0.6f * m, p.Relief);
            h[x, z] = mix * bias * peak;
        }
        return h;
    }

    // ---- Stage 3: terracing → slab levels ---------------------------------

    private static short[,] Terrace(int seed, IslandParams p, bool[,] land, float[,] h)
    {
        int n = p.Size;
        int tc = Math.Max(0, p.TerraceCount);

        float hmax = 0f;
        foreach (float v in h) if (v > hmax) hmax = v;

        var shelves = new float[Math.Max(tc, 1)];
        if (tc > 0)
        {
            var jitter = new Noise(seed + 303, frequency: 1f, octaves: 1);
            float step = hmax / tc;
            for (int i = 0; i < tc; i++)
                shelves[i] = (i + 0.5f) * step + (jitter.At(i * 7.3f, 0.5f) - 0.5f) * step * 0.6f;
            Array.Sort(shelves, 0, tc);
        }
        // Snap band, in slabs. Wider band = flatter, more terrace-dominated.
        float band = p.TerraceGrip * (tc > 0 ? hmax / tc : 1f) * 0.5f;

        var surf = new short[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) { surf[x, z] = IslandData.NoLand; continue; }

            float hv = h[x, z];
            float target = Mathf.Round(hv);                  // 1-slab risers on slopes
            if (tc > 0)
            {
                float nearest = shelves[0];
                for (int i = 1; i < tc; i++)
                    if (Math.Abs(shelves[i] - hv) < Math.Abs(nearest - hv)) nearest = shelves[i];
                if (Math.Abs(hv - nearest) <= band) target = Mathf.Round(nearest);
            }
            surf[x, z] = SlabClamp(target);
        }
        return surf;
        // TODO Stage 3 finish: morphological open at MinShelfWidth, re-flatten,
        // gentle descent.
    }

    // ---- Stage 4: keel / underside → one span per column -----------------

    private static short[,] BuildKeel(int seed, IslandParams p, bool[,] land, short[,] surface)
    {
        int n = p.Size;
        int[,] toCoast = DistanceToCoast(land);
        var noise = new Noise(seed + 404, frequency: 0.06f, octaves: 3);

        // How many cells inland the thick rim reaches.
        float reach = 2f + p.RimFalloff * 20f;

        var keel = new short[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) { keel[x, z] = IslandData.NoLand; continue; }

            float coast = Math.Clamp(1f - toCoast[x, z] / reach, 0f, 1f);   // 1 at edge, 0 inland
            float thick = 2f + p.RimDepth * coast + noise.At(x, z) * 4f;    // slabs
            int k = surface[x, z] - Mathf.RoundToInt(thick);
            keel[x, z] = SlabClamp(Math.Min(k, surface[x, z] - 1));
        }
        return keel;
    }

    /// <summary>Chebyshev-ish BFS distance in cells from each land cell to the nearest non-land cell.</summary>
    private static int[,] DistanceToCoast(bool[,] land)
    {
        int n = land.GetLength(0);
        var dist = new int[n, n];
        var q = new Queue<(int x, int z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) { dist[x, z] = 0; q.Enqueue((x, z)); }
            else dist[x, z] = int.MaxValue;
        }

        var dirs = new[] { (dx: 1, dz: 0), (dx: -1, dz: 0), (dx: 0, dz: 1), (dx: 0, dz: -1) };
        while (q.Count > 0)
        {
            (int x, int z) = q.Dequeue();
            foreach (var (dx, dz) in dirs)
            {
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (dist[nx, nz] > dist[x, z] + 1)
                {
                    dist[nx, nz] = dist[x, z] + 1;
                    q.Enqueue((nx, nz));
                }
            }
        }
        return dist;
    }

    // ---- shared --------------------------------------------------------------

    private static float AutoRadius(IslandParams p)
        => p.Radius > 0f ? p.Radius : p.Size * 0.45f;

    private static float NormRadius(int x, int z, float cx, float cz, float radius)
    {
        float dx = (x - cx) / radius, dz = (z - cz) / radius;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static short SlabClamp(float level)
        => (short)Math.Clamp((int)MathF.Round(level), short.MinValue + 1, short.MaxValue);

    private static short SlabClamp(int level)
        => (short)Math.Clamp(level, short.MinValue + 1, short.MaxValue);
}
