using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>The surface within regions: rung plus relief under each landform's slope limit, dunes along one grain, mountains hung off their foot.</summary>
internal static class Relief
{
    /// <summary>Cells from one dune crest to the next, across the grain.</summary>
    private const float DuneWavelength = 15f;

    /// <summary>
    /// Builds the surface: every rung region as rung + noise relief (dunes as a wave
    /// along the Domain's grain), then mountains as an S-curve off the ground at
    /// their border. <see cref="IslandData.NoLand"/> off land.
    /// </summary>
    internal static short[,] BuildSurface(int seed, IslandParams p, bool[,] land, int[,] region,
                                         RegionPlan[] plan, float[,] inward, out int duneGrain)
    {
        int n = p.Size;
        float hilly = Math.Clamp(p.Hilliness, 0f, 1f);
        // Gain rises with hilliness so mounds come out as distinct humps, not one swell scaled up.
        float gain = 0.35f + 0.30f * hilly;
        var detail = new Noise(seed + 101, frequency: 0.05f, octaves: 4, gain: gain);
        var coarse = new Noise(seed + 202, frequency: 0.018f, octaves: 2);
        var summit = new Noise(seed + 303, frequency: 0.09f, octaves: 3, gain: gain);
        float scale = Landforms.ReliefScale(p);

        var h = new short[n, n];
        var isMountain = new bool[n, n];

        // Amplitude is a blurred field, not a per-region constant, so hills subside
        // into plains instead of stepping several slabs at the border.
        var amp = new float[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z]) amp[x, z] = Landforms.Amplitude(plan[region[x, z]].Type, p) * scale;
        FieldOps.Blur(amp, land, passes: 6);

        duneGrain = RollDuneGrain(seed, out float gcos, out float gsin);
        var drift = new Noise(seed + 404, frequency: 0.035f, octaves: 2);

        RungSurface(land, region, plan, h, isMountain, amp, hilly, gcos, gsin, detail, coarse, drift);

        float[,] foot = MountainFoot(land, h, isMountain, (x, z) => plan[region[x, z]].Plateau);
        HangMountains(p, h, isMountain, inward, foot, summit);
        return h;
    }

    /// <summary>
    /// One grain for every dune field on the Domain, snapped to one of the eight
    /// compass points so the readout and the overlay can name the wind.
    /// </summary>
    private static int RollDuneGrain(int seed, out float gcos, out float gsin)
    {
        int point = (int)(Hash01(seed, 0xD0E5u) * 8f) & 7;
        float grain = point * (Mathf.Tau / 8f);
        gcos = MathF.Cos(grain);
        gsin = MathF.Sin(grain);
        return point;
    }

    /// <summary>Everything that sits on a rung: rung + t × amplitude, t a noise blend or, for dunes, a wave along the grain.</summary>
    private static void RungSurface(bool[,] land, int[,] region, RegionPlan[] plan, short[,] h,
                                    bool[,] isMountain, float[,] amp, float hilly,
                                    float gcos, float gsin, Noise detail, Noise coarse, Noise drift)
    {
        int n = land.GetLength(0);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) { h[x, z] = IslandData.NoLand; continue; }
            RegionPlan rp = plan[region[x, z]];
            if (rp.Type == LandformType.Mountain) { isMountain[x, z] = true; continue; }

            float t;
            if (rp.Type == LandformType.Dunes)
            {
                // A wave along the grain; the phase wanders so the ridges bend and fork.
                float along = x * gcos + z * gsin;
                float phase = along * (Mathf.Tau / DuneWavelength)
                              + (drift.At(x, z) - 0.5f) * 4f;
                t = 0.5f + 0.5f * MathF.Sin(phase);
            }
            else
            {
                float dw = 0.5f + 0.3f * hilly;
                t = dw * detail.At(x, z) + (1f - dw) * coarse.At(x, z);
            }
            h[x, z] = Terrain.SlabClamp(rp.Plateau + t * amp[x, z]);
        }
    }

    /// <summary>
    /// Mountains hang off <see cref="MountainFoot"/>, not off a rung: a rung is the
    /// region's base and the neighbouring surface sits above it. The S-curve in
    /// inward distance, rounded to slabs, is the step profile.
    /// </summary>
    private static void HangMountains(IslandParams p, short[,] h, bool[,] isMountain,
                                      float[,] inward, float[,] foot, Noise summit)
    {
        int n = h.GetLength(0);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!isMountain[x, z]) continue;
            float u = inward[x, z];
            float s = u * u * (3f - 2f * u);
            float rugged = (summit.At(x, z) - 0.5f) * 2f * 5f
                           * FieldOps.SmoothStep(0.45f, 1f, u);
            h[x, z] = Terrain.SlabClamp(foot[x, z] + p.MountainHeight * s + rugged);
        }
    }

    /// <summary>
    /// The height a mountain rises from, per cell: seeded from the surface each border
    /// cell touches, propagated inward along the front (so enqueue order matters),
    /// then blurred so fronts meeting inside the mountain leave no seam. A mountain
    /// meeting only the coastline starts from <paramref name="ownRung"/>. Also read
    /// by <see cref="Habitat"/> off the finished surface, so the lapse measures a
    /// mountain from its own foot.
    /// </summary>
    internal static float[,] MountainFoot(bool[,] land, short[,] h, bool[,] isMountain,
                                          Func<int, int, float> ownRung)
    {
        int n = land.GetLength(0);
        var foot = new float[n, n];
        var known = new bool[n, n];
        var anchor = new float[n, n];
        var anchored = new bool[n, n];
        var q = new Queue<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && !isMountain[x, z]) foot[x, z] = h[x, z];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!isMountain[x, z]) continue;

            float best = float.MinValue;
            bool atCoast = false;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz]) { atCoast = true; continue; }
                if (!isMountain[nx, nz]) best = MathF.Max(best, h[nx, nz]);
            }
            // A mountain meeting only the coastline starts from its own rung.
            if (best == float.MinValue && atCoast) best = ownRung(x, z);

            if (best > float.MinValue)
            {
                foot[x, z] = best;
                anchor[x, z] = best;
                anchored[x, z] = true;
                known[x, z] = true;
                q.Enqueue((x, z));
            }
        }

        while (q.Count > 0)
        {
            var (x, z) = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (!isMountain[nx, nz] || known[nx, nz]) continue;
                foot[nx, nz] = foot[x, z];
                known[nx, nz] = true;
                q.Enqueue((nx, nz));
            }
        }

        FieldOps.Blur(foot, isMountain, passes: 5);

        // The blur averages, so restore each border cell to at least its anchor or
        // the mountain would start below the ground it meets.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (anchored[x, z]) foot[x, z] = MathF.Max(foot[x, z], anchor[x, z]);

        return foot;
    }
}
