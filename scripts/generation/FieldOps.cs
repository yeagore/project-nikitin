using System;

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
        Array.Sort(flat);
        int idx = Math.Clamp((int)(q * (flat.Length - 1)), 0, flat.Length - 1);
        return flat[idx];
    }
}
