using System;
using System.Collections.Generic;

using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>
/// The habitat vector: five bytes per column (moisture, warmth, ruggedness,
/// exposure, rim distance), kept as separate axes so the biome layer composes
/// them. Derived from the finished terrain plus one noise field; no climate sim.
/// </summary>
internal static class Habitat
{
    /// <summary>Cells over which moisture decays to 1/e.</summary>
    private const float MoistureFalloff = 6.5f;

    /// <summary>How far (±) noise wobbles a cell's effective water distance.</summary>
    private const float MoistureWobble = 0.3f;

    /// <summary>
    /// Warmth lost over the full mountain cap (<see cref="MountainCap"/>). Anchored
    /// to the cap, not the island's own range, so a flat island stays warm to its top.
    /// </summary>
    private const float LapseShare = 235f;

    /// <summary>Cells upwind a cell looks for cover.</summary>
    private const int WindScan = 10;

    /// <summary>Slabs of upwind rise that count as full shelter.</summary>
    private const float FullCover = 8f;

    /// <summary>The tallest mountain a footprint allows, in slabs.</summary>
    internal static float MountainCap(int size) => Math.Max(8f, size * (40f / 128f));

    /// <summary>Fills the five axes, in a fixed order.</summary>
    public static void Measure(int seed, IslandData d)
    {
        MeasureMoisture(seed, d);
        MeasureWarmth(d);
        MeasureRuggedness(d);
        MeasureExposure(d);
        MeasureRimDistance(d);
    }

    /// <summary>
    /// Flood distance from watered columns (goo waters nothing), wobbled by noise,
    /// decayed exponentially. Land the flood never reaches is parched (0).
    /// </summary>
    private static void MeasureMoisture(int seed, IslandData d)
    {
        int n = d.Size;
        var dist = new int[n, n];
        var q = new Queue<Vector2I>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            dist[x, z] = -1;
            if (!d.HasLand(x, z) || d.WaterLevel[x, z] == IslandData.NoLand) continue;
            if (d.Fluid[x, z] == (byte)FluidKind.Goo) continue;
            dist[x, z] = 0;
            q.Enqueue(new Vector2I(x, z));
        }
        while (q.Count > 0)
        {
            Vector2I c = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (!d.HasLand(nx, nz) || dist[nx, nz] >= 0) continue;
                dist[nx, nz] = dist[c.X, c.Y] + 1;
                q.Enqueue(new Vector2I(nx, nz));
            }
        }

        var wobble = new Noise(seed + 71_003, 0.05f, octaves: 3);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            if (dist[x, z] < 0) { d.Moisture[x, z] = 0; continue; }

            float sway = 1f + MoistureWobble * (wobble.At(x, z) * 2f - 1f);
            float cells = dist[x, z] * sway;
            d.Moisture[x, z] = (byte)Mathf.Clamp(
                Mathf.RoundToInt(255f * MathF.Exp(-cells / MoistureFalloff)), 0, 255);
        }
    }

    /// <summary>A fixed lapse per slab above the lowest visible ground; no land leaves it all zero.</summary>
    private static void MeasureWarmth(IslandData d)
    {
        int n = d.Size;
        short low = short.MaxValue;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            short eff = d.EffectiveLevel(x, z);
            if (eff != IslandData.NoLand && eff < low) low = eff;
        }
        if (low == short.MaxValue) return;

        float cap = MountainCap(d.Size);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            float rise = d.EffectiveLevel(x, z) - low;
            d.Warmth[x, z] = (byte)Mathf.Clamp(
                Mathf.RoundToInt(255f - LapseShare * rise / cap), 0, 255);
        }
    }

    /// <summary>Spread of the effective surface within two cells, saturating at eight slabs.</summary>
    private static void MeasureRuggedness(IslandData d)
    {
        int n = d.Size;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            short lo = short.MaxValue, hi = short.MinValue;
            for (int ox = -2; ox <= 2; ox++)
            for (int oz = -2; oz <= 2; oz++)
            {
                int nx = x + ox, nz = z + oz;
                if (!InBounds(n, nx, nz)) continue;
                short eff = d.EffectiveLevel(nx, nz);
                if (eff == IslandData.NoLand) continue;
                if (eff < lo) lo = eff;
                if (eff > hi) hi = eff;
            }
            int relief = hi - lo;
            d.Ruggedness[x, z] = (byte)Math.Min(255, relief * 32);
        }
    }

    /// <summary>
    /// Openness to the Domain's wind: the tallest rise found walking upwind is
    /// cover; a walk that leaves the island gets the wind off the aether.
    /// </summary>
    private static void MeasureExposure(IslandData d)
    {
        int n = d.Size;
        int wind = (d.DuneGrain + 4) & 7;
        Vector2I up = new(Dx8[wind], Dz8[wind]);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            short here = d.EffectiveLevel(x, z);

            float cover = 0f;
            for (int step = 1; step <= WindScan; step++)
            {
                int nx = x + up.X * step, nz = z + up.Y * step;
                if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) break;
                float rise = d.EffectiveLevel(nx, nz) - here;
                if (rise > cover) cover = rise;
            }

            float shelter = Mathf.Clamp(cover / FullCover, 0f, 1f);
            d.Exposure[x, z] = (byte)Mathf.RoundToInt(255f * (1f - shelter));
        }
    }

    /// <summary>Cells of land between a column and the aether, capped at 255.</summary>
    private static void MeasureRimDistance(IslandData d)
    {
        int n = d.Size;
        var dist = new int[n, n];
        var q = new Queue<Vector2I>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            dist[x, z] = -1;
            if (!d.HasLand(x, z)) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (InBounds(n, nx, nz) && d.HasLand(nx, nz)) continue;
                dist[x, z] = 0;
                q.Enqueue(new Vector2I(x, z));
                break;
            }
        }
        while (q.Count > 0)
        {
            Vector2I c = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (!d.HasLand(nx, nz) || dist[nx, nz] >= 0) continue;
                dist[nx, nz] = dist[c.X, c.Y] + 1;
                q.Enqueue(new Vector2I(nx, nz));
            }
        }

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.HasLand(x, z))
                d.RimDistance[x, z] = (byte)Math.Min(255, Math.Max(0, dist[x, z]));
    }
}
