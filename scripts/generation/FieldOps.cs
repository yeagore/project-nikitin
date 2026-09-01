using System;
using System.Collections.Generic;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>Small numeric helpers over 2D scalar fields used by the generator.</summary>
public static class FieldOps
{
    /// <summary>Hermite smoothstep; returns 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>.</summary>
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge0 == edge1) return x < edge0 ? 0f : 1f;
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The value v such that a fraction q of the samples is ≤ v: one sort, over an explicit sample set —
    /// used to measure coverage against the candidate area rather than the whole
    /// grid, most of which is empty aether.
    /// </summary>
    public static float Quantile(List<float> samples, float q)
        => samples.Count == 0 ? 0f : Quantile(samples.ToArray(), q);

    private static float Quantile(float[] flat, float q)
    {
        Array.Sort(flat);
        int idx = Math.Clamp((int)(q * (flat.Length - 1)), 0, flat.Length - 1);
        return flat[idx];
    }

    /// <summary>Bilinear sample of a field at fractional coordinates, clamped at the edges.</summary>
    public static float Sample(float[,] field, float x, float z)
    {
        int n = field.GetLength(0);
        x = Math.Clamp(x, 0f, n - 1.001f);
        z = Math.Clamp(z, 0f, n - 1.001f);

        int x0 = (int)x, z0 = (int)z;
        float fx = x - x0, fz = z - z0;
        float a = field[x0, z0], b = field[x0 + 1, z0];
        float c = field[x0, z0 + 1], d = field[x0 + 1, z0 + 1];
        float ab = a + (b - a) * fx;
        float cd = c + (d - c) * fx;
        return ab + (cd - ab) * fz;
    }

    /// <summary>
    /// Makes a field of <b>drops</b> — how far each cell is about to be lowered —
    /// safe to apply to finished terrain, by forcing it to change by at most one
    /// between neighbours.
    ///
    /// <para>Any pass that lowers some cells and not others puts a step at the
    /// edge of the set it lowered, and that step is exactly the depth of the drop.
    /// Lowering by bands does not help: a cell excluded for its own reasons — a
    /// mesa rim, a bridgehead, a channel — sits at drop 0 beside a neighbour at
    /// drop 3, and there is the cliff. (Measured, when beaches and valleys were
    /// first written this way: two-slab steps went from 0.5% of the island to
    /// 6.2%.)</para>
    ///
    /// <para>Clamping each cell to one more than its lowest neighbour makes the
    /// drop field 1-Lipschitz, so the deepest part of a valley or a beach keeps
    /// its depth and the edge tapers out a slab at a time — which is the free
    /// step, and is also what a valley side looks like.</para>
    /// </summary>
    public static void Taper(int[,] drop, bool[,] land)
    {
        int n = drop.GetLength(0);
        for (int pass = 0; pass < 32; pass++)
        {
            bool changed = false;
            bool forward = (pass & 1) == 0;

            for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                int x = forward ? a : n - 1 - a;
                int z = forward ? b : n - 1 - b;
                if (!land[x, z] || drop[x, z] <= 0) continue;

                int cap = int.MaxValue;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k];
                    int nz = z + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!land[nx, nz]) continue;                 // the rim is not a neighbour
                    cap = Math.Min(cap, drop[nx, nz] + 1);
                }
                if (cap == int.MaxValue || drop[x, z] <= cap) continue;
                drop[x, z] = cap;
                changed = true;
            }
            if (!changed) return;
        }
    }

    /// <summary>
    /// In-place 3×3 box blur over the cells flagged in <paramref name="mask"/>,
    /// repeated <paramref name="passes"/> times. Used to take the integer steps
    /// out of a distance transform before anything reads it as a height.
    /// </summary>
    public static void Blur(float[,] field, bool[,] mask, int passes)
    {
        int n = field.GetLength(0);
        var tmp = new float[n, n];

        for (int pass = 0; pass < passes; pass++)
        {
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!mask[x, z]) { tmp[x, z] = field[x, z]; continue; }

                float sum = 0f;
                int taken = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (!InBounds(n, nx, nz)) continue;
                    sum += field[nx, nz];
                    taken++;
                }
                tmp[x, z] = sum / taken;
            }
            Array.Copy(tmp, field, field.Length);
        }
    }
}
