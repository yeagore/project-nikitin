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
    /// <summary>Cells of level walk over which the water's share of moisture decays to 1/e.</summary>
    private const float MoistureFalloff = 5f;

    /// <summary>Cells of walk one slab of climb costs the water: it spreads along and down, not up.</summary>
    private const int ClimbCost = 2;

    /// <summary>Walk cost beyond which the water adds nothing worth counting (under e^-7 of its share), so the flood stops.</summary>
    private const int MoistureReach = 60;

    /// <summary>How far (±) noise wobbles a cell's effective water distance.</summary>
    private const float MoistureWobble = 0.3f;

    /// <summary>Moisture fresh water adds at its bank, over the Domain's background (<see cref="IslandParams.Moisture"/>).</summary>
    private const float WaterMoisture = 200f;

    /// <summary>Taken off the water's share so it ends instead of trailing: nothing past sixteen cells of level walk.</summary>
    private const float WaterFloor = 8f;

    /// <summary>Moisture (±) the background wobbles by, so a climate is patchy rather than one flat value.</summary>
    private const float BackgroundWobble = 25f;

    /// <summary>Moisture a fully sheltered cell gains: the lee holds its damp.</summary>
    private const float LeeMoisture = 20f;

    /// <summary>Moisture a dry patch loses, on a rock landform or within <see cref="RockFringe"/> cells of one, where its noise clears <see cref="RockDroughtBar"/>.</summary>
    private const float RockDrought = 60f;
    private const float RockDroughtBar = 0.58f;
    private const int RockFringe = 3;

    /// <summary>
    /// Warmth lost at the full mountain cap (<see cref="MountainCap"/>). Anchored
    /// to the cap, not the island's own range, so a flat island stays warm to its top.
    /// </summary>
    private const float LapseShare = 235f;

    /// <summary>Share of the cap below which altitude costs no warmth: the lowland and the middle heights are one climate.</summary>
    private const float LapseKnee = 0.3f;

    /// <summary>Curve of the lapse above the knee; over 1 so the cold gathers at the top.</summary>
    private const float LapseCurve = 1.3f;

    /// <summary>Warmth a fully windswept cell loses.</summary>
    private const float WindChill = 25f;

    /// <summary>Warmth the rim loses, fading to nothing <see cref="RimChillReach"/> cells inland.</summary>
    private const float RimChill = 20f;
    private const int RimChillReach = 16;

    /// <summary>Warmth's middle, which wet ground is pulled toward: water tempers both the heat and the cold.</summary>
    private const float Temperate = 190f;

    /// <summary>How far waterside ground is pulled toward <see cref="Temperate"/>.</summary>
    private const float MoistTemper = 0.3f;

    /// <summary>Cells upwind a cell looks for cover.</summary>
    private const int WindScan = 10;

    /// <summary>Slabs of upwind rise that count as full shelter.</summary>
    private const float FullCover = 8f;

    /// <summary>The tallest mountain a footprint allows, in slabs.</summary>
    internal static float MountainCap(int size) => Math.Max(8f, size * (40f / 128f));

    /// <summary>
    /// Fills the five axes, in a fixed order: the shape axes first, then moisture
    /// (which reads the lee), then warmth (which reads everything).
    /// </summary>
    public static void Measure(int seed, IslandParams p, IslandData d)
    {
        MeasureRuggedness(d);
        MeasureExposure(d);
        MeasureRimDistance(d);
        MeasureMoisture(seed, p, d);
        MeasureWarmth(p, d);
    }

    /// <summary>
    /// The Domain's background moisture, wobbled by noise into patches, plus what
    /// its fresh water adds: a walk cost from watered columns (goo waters nothing),
    /// a cell per cell along or down and <see cref="ClimbCost"/> more per slab up,
    /// so a river waters the plain it crosses and not the mountain or the canyon
    /// wall it passes, decayed over <see cref="MoistureFalloff"/> cells. The lee
    /// holds a little damp; a rock landform and its fringe carry patches of drought.
    /// </summary>
    private static void MeasureMoisture(int seed, IslandParams p, IslandData d)
    {
        int n = d.Size;
        var cost = new int[n, n];
        // A bucket per unit of cost (Dial's queue): integer costs, scan order kept.
        var buckets = new List<Vector2I>[MoistureReach + 1];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            cost[x, z] = -1;
            if (!d.HasLand(x, z) || d.WaterLevel[x, z] == IslandData.NoLand) continue;
            if (d.Fluid[x, z] == (byte)FluidKind.Goo) continue;
            cost[x, z] = 0;
            (buckets[0] ??= new List<Vector2I>()).Add(new Vector2I(x, z));
        }

        for (int c = 0; c <= MoistureReach; c++)
        {
            List<Vector2I>? bucket = buckets[c];
            if (bucket == null) continue;
            for (int i = 0; i < bucket.Count; i++)
            {
                Vector2I at = bucket[i];
                if (cost[at.X, at.Y] != c) continue;             // relaxed since it was queued
                short here = d.EffectiveLevel(at.X, at.Y);
                for (int k = 0; k < 4; k++)
                {
                    int nx = at.X + Dx[k], nz = at.Y + Dz[k];
                    if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                    // The step up out of the water onto its bank is the free one.
                    int climb = Math.Max(0, d.EffectiveLevel(nx, nz) - here - (c == 0 ? 1 : 0));
                    int next = c + 1 + climb * ClimbCost;
                    if (next > MoistureReach) continue;
                    if (cost[nx, nz] >= 0 && cost[nx, nz] <= next) continue;
                    cost[nx, nz] = next;
                    (buckets[next] ??= new List<Vector2I>()).Add(new Vector2I(nx, nz));
                }
            }
        }

        // Rock and its fringe: where the drought patches may fall.
        int[,] toRock = Flood.Distance(n,
            (x, z) => d.HasLand(x, z) && (Surfaces.Rocky((LandformType)d.Landform[x, z]) || d.Canyon[x, z]),
            (_, _, nx, nz) => d.HasLand(nx, nz),
            cap: RockFringe);

        var wobble = new Noise(seed + 71_003, 0.05f, octaves: 3);
        var background = new Noise(seed + 71_007, 0.03f, octaves: 2);
        var drought = new Noise(seed + 71_011, 0.06f, octaves: 2);
        float baseline = 255f * Math.Clamp(p.Moisture, 0f, 1f);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;

            float moisture = baseline + BackgroundWobble * (background.At(x, z) * 2f - 1f);
            moisture += LeeMoisture * (1f - d.Exposure[x, z] / 255f);
            if (cost[x, z] >= 0)
            {
                float sway = 1f + MoistureWobble * (wobble.At(x, z) * 2f - 1f);
                moisture += Math.Max(0f, WaterMoisture * MathF.Exp(-cost[x, z] * sway / MoistureFalloff) - WaterFloor);
            }
            if (toRock[x, z] >= 0 && drought.At(x, z) > RockDroughtBar) moisture -= RockDrought;

            d.Moisture[x, z] = (byte)Mathf.Clamp(Mathf.RoundToInt(moisture), 0, 255);
        }
    }

    /// <summary>
    /// Warmth: the Domain's background (<see cref="IslandParams.Warmth"/>) from the
    /// lowest ground up to <see cref="LapseKnee"/> of the mountain cap, then a lapse
    /// that steepens toward the cap; the wind, the rim and dry ground each make it
    /// colder — wet ground is pulled toward <see cref="Temperate"/> from either
    /// side. No land leaves it all zero.
    /// </summary>
    private static void MeasureWarmth(IslandParams p, IslandData d)
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
        float baseline = 255f * Math.Clamp(p.Warmth, 0f, 1f);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            float rise = d.EffectiveLevel(x, z) - low;
            float t = Mathf.Clamp((rise / cap - LapseKnee) / (1f - LapseKnee), 0f, 1f);
            float warmth = baseline - LapseShare * MathF.Pow(t, LapseCurve);

            warmth -= WindChill * d.Exposure[x, z] / 255f;
            warmth -= RimChill * (1f - Math.Min((int)d.RimDistance[x, z], RimChillReach) / (float)RimChillReach);
            warmth = Temperate + (warmth - Temperate) * (1f - MoistTemper * d.Moisture[x, z] / 255f);

            d.Warmth[x, z] = (byte)Mathf.Clamp(Mathf.RoundToInt(warmth), 0, 255);
        }
    }

    /// <summary>
    /// Spread of the surface within two cells, saturating at eight slabs. Water is
    /// read as its bank (<see cref="BankLevel"/>): a stream through a plain is flat
    /// country, and a gorge is still its walls.
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
                short eff = BankLevel(d, nx, nz);
                if (eff == IslandData.NoLand) continue;
                if (eff < lo) lo = eff;
                if (eff > hi) hi = eff;
            }
            int relief = hi - lo;
            d.Ruggedness[x, z] = (byte)Math.Min(255, relief * 32);
        }
    }

    /// <summary>
    /// The level ruggedness sees: the ground, or a slab over standing fluid — the
    /// bank a free step leads down from, since every lake sits a slab under its
    /// shore and every stream a slab under its bank.
    /// </summary>
    private static short BankLevel(IslandData d, int x, int z)
    {
        if (!d.HasLand(x, z)) return IslandData.NoLand;
        short water = d.WaterLevel[x, z];
        return water != IslandData.NoLand ? (short)(water + 1) : d.SurfaceLevel(x, z);
    }

    /// <summary>
    /// Openness to the Domain's wind — rolled for every Domain, dunes or not: the
    /// tallest rise found walking upwind is cover; a walk that leaves the island
    /// gets the wind off the aether.
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
