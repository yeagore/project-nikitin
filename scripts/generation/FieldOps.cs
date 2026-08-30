using System;
using System.Collections.Generic;

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
    /// The value v such that a fraction <paramref name="q"/> of the field is ≤ v
    /// (q in [0, 1]). One sort; used to turn a target land fraction into a
    /// mask threshold without an iterative search.
    /// </summary>
    public static float Quantile(float[,] field, float q)
    {
        var flat = new float[field.Length];
        int k = 0;
        foreach (float v in field) flat[k++] = v;
        return Quantile(flat, q);
    }

    /// <summary>
    /// As <see cref="Quantile(float[,], float)"/>, over an explicit sample set —
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
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    sum += field[nx, nz];
                    taken++;
                }
                tmp[x, z] = sum / taken;
            }
            Array.Copy(tmp, field, field.Length);
        }
    }
}
