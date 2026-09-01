using System;
using System.Collections.Generic;

using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>
/// Measures the <b>growing conditions</b>, one byte per column per axis: how
/// damp the ground is, how cold, how broken, how windswept, and how near the
/// aether. Fills <see cref="IslandData.Moisture"/> and its four siblings.
///
/// <para><b>Not a biome.</b> A biome is what lives somewhere, and choosing one
/// is the next branch's work. These are the measurable facts that choice will
/// be a function of — kept as separate axes rather than combined into a score,
/// so the biome layer can compose them (rain shadow = dry side of
/// exposure, treeline = a warmth threshold, essencecoral = small rim distance)
/// instead of unpicking someone else's blend. The provisional
/// <see cref="SurfaceMaterial"/> mapping in <c>Surfaces</c> reads the same
/// vectors, which is how the two stay consistent.</para>
///
/// <para>Everything here is derived from what the terrain already knows —
/// heights, water, the wind the dunes lie along — plus one noise field to keep
/// the moisture bands from being contour lines of the water network. There is
/// no climate simulation and no weather; a Domain floats in aether, and what
/// its air does is a design question nobody has answered. When someone does,
/// this is the one file that turns the answer into numbers.</para>
/// </summary>
internal static class Habitat
{

    /// <summary>Cells of distance over which moisture decays to 1/e of itself.</summary>
    private const float MoistureFalloff = 6.5f;

    /// <summary>How far (±) the noise wobbles a cell's effective water distance.</summary>
    private const float MoistureWobble = 0.3f;

    /// <summary>
    /// Warmth lost per slab climbed, as a share of the full range over the
    /// tallest mountain this footprint allows (<c>Size × 40/128</c> slabs — the
    /// <c>BoundAltitude</c> cap). Anchoring the lapse to the cap rather than to
    /// the island's own range is the point: the top of what a mountain <i>can
    /// be</i> is always frozen, and a flat island is warm to its highest hill.
    /// </summary>
    private const float LapseShare = 235f;

    /// <summary>How many cells upwind a cell looks for cover.</summary>
    private const int WindScan = 10;

    /// <summary>Slabs of upwind rise that count as full shelter from the wind.</summary>
    private const float FullCover = 8f;

    public static void Measure(int seed, IslandData d)
    {
        MeasureMoisture(seed, d);
        MeasureWarmth(d);
        MeasureRuggedness(d);
        MeasureExposure(d);
        MeasureRimDistance(d);
    }

    /// <summary>
    /// Nearness to fresh water: a breadth-first distance from every watered
    /// column (goo waters nothing — it never mixes with water and nothing grows
    /// on it), wobbled by a noise field so equal distance does not mean equal
    /// moisture, then decayed exponentially. Ground the flood never reaches — a
    /// dry islet with no water of its own — is parched.
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

    /// <summary>
    /// A fixed lapse per slab above the island's lowest visible ground, scaled so
    /// warmth reaches its floor at the top of the tallest mountain this footprint
    /// allows. See <see cref="LapseShare"/> for why it is absolute.
    /// </summary>
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

        float cap = Math.Max(8f, d.Size * (40f / 128f));
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            float rise = d.EffectiveLevel(x, z) - low;
            d.Warmth[x, z] = (byte)Mathf.Clamp(
                Mathf.RoundToInt(255f - LapseShare * rise / cap), 0, 255);
        }
    }

    /// <summary>
    /// Local relief: the spread of the effective surface within two cells,
    /// saturating at eight slabs. A cliff brink and its foot both read as broken
    /// country; a plain reads as zero however high it sits.
    /// </summary>
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
    /// Openness to the Domain's one wind. A cell walks upwind — toward
    /// <see cref="IslandData.WindFrom"/> — looking for ground that stands above
    /// it; the tallest rise found is its cover, and full cover is
    /// <see cref="FullCover"/> slabs. A cell whose upwind walk leaves the island
    /// entirely gets the wind straight off the aether. Windward rims and summits
    /// come out scoured, the lee of a massif comes out calm — which is the axis
    /// the dune fields already lie along, extended to every column.
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
