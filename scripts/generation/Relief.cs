using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>Stage 3: the surface within regions — rung plus relief under each landform's slope limit, dunes, mountains.</summary>
internal static class Relief
{
    internal static short[,] BuildSurface(int seed, IslandParams p, bool[,] land, int[,] region,
                                         RegionPlan[] plan, float[,] inward, out int duneGrain)
    {
        int n = p.Size;
        // Hilliness is not only height: a rolling down and a field of mounds also
        // differ in how much of the relief is high-frequency. Gain sets the fBm
        // octave falloff, and the blend below leans on the detail octaves as
        // hilliness rises, so mounds come out as distinct humps rather than one
        // broad swell scaled up.
        float hilly = Math.Clamp(p.Hilliness, 0f, 1f);
        float gain = 0.35f + 0.30f * hilly;
        var detail = new Noise(seed + 101, frequency: 0.05f, octaves: 4, gain: gain);
        var coarse = new Noise(seed + 202, frequency: 0.018f, octaves: 2);
        var summit = new Noise(seed + 303, frequency: 0.09f, octaves: 3, gain: gain);
        float scale = Landforms.ReliefScale(p);

        var h = new short[n, n];
        var isMountain = new bool[n, n];

        // Relief amplitude as a blurred *field*, not a per-region constant. The
        // noise is already shared across regions, but a hills patch swinging over
        // nine slabs beside a plain swinging over one still steps several slabs at
        // their border — a cliff where the rules do not allow one. Blurring the
        // amplitude makes hills subside into plains instead.
        var amp = new float[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z]) amp[x, z] = Landforms.Amplitude(plan[region[x, z]].Type, p) * scale;
        FieldOps.Blur(amp, land, passes: 6);

        // The grain of a dune field: one direction for the whole Domain, because
        // what makes dunes dunes is that they all lie the same way.
        //
        // <b>Snapped to a compass point.</b> It used to be a free angle, which is
        // more natural and unnameable: nothing on screen or in the data said which
        // way the wind blew, and a field of ridges at 37° reads as noise with a
        // bias. On one of the eight compass points it is a fact about the Domain —
        // "the wind is from the north-east" — that the readout can say, the
        // compass overlay can draw, and the content layer can use.
        int point = (int)(TerrainHash01(seed, 0xD0E5u) * 8f) & 7;
        float grain = point * (Mathf.Tau / 8f);
        float gcos = MathF.Cos(grain), gsin = MathF.Sin(grain);
        duneGrain = point;
        var drift = new Noise(seed + 404, frequency: 0.035f, octaves: 2);

        // Pass 1 — everything that sits on a rung.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) { h[x, z] = IslandData.NoLand; continue; }
            RegionPlan rp = plan[region[x, z]];
            if (rp.Type == LandformType.Mountain) { isMountain[x, z] = true; continue; }

            float t;
            if (rp.Type == LandformType.Dunes)
            {
                // A wave along the grain rather than a blob field: the crest line
                // is what a dune has and a hill does not. The phase wanders, so
                // the ridges bend and occasionally fork instead of ruling the
                // patch into stripes.
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

        // Pass 2 — mountains hang off the ground actually present at their border,
        // not off a rung. A rung is the region's *base* level; the neighbouring
        // surface sits on top of its own relief, so starting a mountain from the
        // rung drops it below the plains it rises out of.
        float[,] foot = MountainFoot(land, region, plan, h, isMountain);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!isMountain[x, z]) continue;

            // Elevation follows an S-curve in distance from the massif's edge.
            // Rounding that to slabs *is* the step profile: the gradient is
            // fractional at the foot (one-slab foothills), steep through the
            // middle (consecutive multi-slab risers), and flat at the summit.
            float u = inward[x, z];
            float s = u * u * (3f - 2f * u);
            float rugged = (summit.At(x, z) - 0.5f) * 2f * 5f
                           * FieldOps.SmoothStep(0.45f, 1f, u);
            h[x, z] = Terrain.SlabClamp(foot[x, z] + p.MountainHeight * s + rugged);
        }
        return h;
    }

    /// <summary>Cells from one dune crest to the next, across the grain.</summary>
    private const float DuneWavelength = 15f;

    /// <summary>
    /// The height a massif rises from, per cell: seeded from the real surface of
    /// the ground each border cell touches, propagated inward, then blurred so
    /// fronts meeting inside the massif do not leave a seam. Blurring reads the
    /// surrounding terrain too, so the foot joins it flush.
    /// </summary>
    private static float[,] MountainFoot(bool[,] land, int[,] region, RegionPlan[] plan,
                                         short[,] h, bool[,] isMountain)
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
            // A massif meeting only the coastline has no landward ground to start
            // from; fall back to its own rung.
            if (best == float.MinValue && atCoast) best = plan[region[x, z]].Plateau;

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

        // The blur is an average, so a border cell whose own neighbour stands
        // above the local mean would be pulled under it — the mountain would
        // start below the ground it meets. Restore each border cell to at least
        // the height it was anchored to; the S-curve contributes nothing there,
        // so this is exactly what removes the drop at the foot.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (anchored[x, z]) foot[x, z] = MathF.Max(foot[x, z], anchor[x, z]);

        return foot;
    }
}
