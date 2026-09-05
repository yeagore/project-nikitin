using System;
using System.Collections.Generic;

using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// The habitat vector: six bytes per column (moisture, warmth, ruggedness,
/// exposure, rim distance, water distance), kept as separate axes so the biome
/// layer composes them. Derived from the finished terrain plus a few noise
/// fields; no climate sim. Two things are rolled per Domain and read here: the
/// wind (rolled with the dunes, its strength a knob) and the sun.
/// </summary>
internal static class Habitat
{
    /// <summary>Cells of level walk over which the water's share of moisture decays to 1/e.</summary>
    private const float MoistureFalloff = 5f;

    /// <summary>Cells of walk one slab of climb costs the water: it spreads along and down, not up.</summary>
    private const int ClimbCost = 2;

    /// <summary>Walk cost beyond which the water adds nothing worth counting (under e^-7 of its share).</summary>
    private const int MoistureReach = 60;

    /// <summary>Furthest walk cost the water flood records: the byte's own end, <see cref="IslandData.WaterDistance"/>.</summary>
    private const int WaterReach = 255;

    /// <summary>How far (±) noise wobbles a cell's effective water distance.</summary>
    private const float MoistureWobble = 0.3f;

    /// <summary>Moisture fresh water adds at its bank, over the Domain's background (<see cref="IslandParams.Moisture"/>).</summary>
    private const float WaterMoisture = 200f;

    /// <summary>Taken off the water's share so it ends instead of trailing: nothing past sixteen cells of level walk.</summary>
    private const float WaterFloor = 8f;

    /// <summary>Moisture (±) the background wobbles by, so a climate is patchy rather than one flat value.</summary>
    private const float BackgroundWobble = 25f;

    /// <summary>
    /// Moisture a fully sheltered cell loses at the nominal wind: the rain falls on
    /// the windward side and the lee is its shadow. (It was the other way round —
    /// the lee "holding its damp" — until the rain shadow was pointed out.)
    /// </summary>
    private const float RainShadow = 30f;

    /// <summary>
    /// Moisture a fully sheltered, fully broken cell gains at the nominal wind: a
    /// gorge floor keeps its damp under its walls while the plateau above dries.
    /// Scaled by both shelter and ruggedness, so flat sheltered ground gains little
    /// and a windswept brink nothing.
    /// </summary>
    private const float GorgeDamp = 70f;

    /// <summary>Moisture a dry patch loses, on a rock landform or within <see cref="RockFringe"/> cells of one, where its noise clears <see cref="RockDroughtBar"/>.</summary>
    private const float RockDrought = 60f;
    private const float RockDroughtBar = 0.58f;
    private const int RockFringe = 3;

    /// <summary>Warmth lost at the full lapse: from the warmest lowland to well under the snow.</summary>
    private const float LapseShare = 255f;

    /// <summary>
    /// Share of the mountain cap a mountain must rise above its own foot before the
    /// cold starts. Measured from the foot, not from the island's lowest ground or
    /// the parameters: no rung, mesa or massif is ever cold at any footprint, and a
    /// mountain's upper part is, whatever it stands on and however small the Domain.
    /// </summary>
    private const float ColdFrom = 0.4f;

    /// <summary>
    /// Share of the mountain cap over which the lapse then runs to its full loss, so
    /// a mountain of the full cap is snow at its top in any climate and one of half
    /// the cap is merely cold.
    /// </summary>
    private const float LapseReach = 0.6f;

    /// <summary>
    /// Where <see cref="IslandParams.Warmth"/> lands on the byte: 0 is a lowland of
    /// 60 (cold, but its water still thaws), 1 is 240 (sand). The offset keeps the
    /// whole knob liveable: the extreme cold is a slider you cannot quite reach.
    /// The label is the open flat lowland: the chills below are small, the lee
    /// warms rather than the wind cooling, and a slope's sun is as often for as
    /// against, so an island's mean warmth reads at its knob.
    /// </summary>
    private const float WarmthFloor = 60f, WarmthSpan = 180f;

    /// <summary>Warmth a fully sheltered cell gains at the nominal wind: the lee is milder than the open ground.</summary>
    private const float LeeWarmth = 10f;

    /// <summary>Warmth a slope turned full to the sun gains, and one turned full away loses.</summary>
    private const float SunWarmth = 8f;

    /// <summary>Slabs per cell of slope at which a face counts as turned full to or from the sun.</summary>
    private const float SunSlope = 2f;

    /// <summary>Warmth a frost hollow loses: a basin's floor, or a sinkhole's pit, is colder than its rung.</summary>
    private const float HollowChill = 8f;

    /// <summary>Slabs a sinkhole cell must lie under the ground within two cells of it to be the pit and not the country round it.</summary>
    private const int HollowDrop = 3;

    /// <summary>
    /// Warmth the rim loses, fading to nothing <see cref="RimChillReach"/> cells
    /// inland: a colder strand along the aether, not a colder Domain. Rim distance
    /// is a median five cells even at 128², so a long fade never faded anywhere.
    /// </summary>
    private const float RimChill = 6f;
    private const int RimChillReach = 4;

    /// <summary>Warmth's middle — the temperate band's centre — which wet ground is pulled toward: water tempers both the heat and the cold.</summary>
    private const float Temperate = 135f;

    /// <summary>How far waterside ground is pulled toward <see cref="Temperate"/>.</summary>
    private const float MoistTemper = 0.3f;

    /// <summary>
    /// Below this warmth knob a Domain may have hot water, the chance growing as
    /// the knob falls toward 0: a hot spring in a temperate country is a curiosity,
    /// in a frigid one the only meadow there is.
    /// </summary>
    private const float HotClimateBelow = 0.35f;

    /// <summary>Chance, at the coldest, that a spring runs hot, and that a small pool does.</summary>
    private const float HotSpringChance = 0.4f, HotPoolChance = 0.35f;

    /// <summary>Cells of standing water a pool may have and still be hot: a tarn, not a lake.</summary>
    private const int HotPoolMax = 60;

    /// <summary>Warmth hot water adds at its source, decaying to 1/e over <see cref="HotFalloff"/> cells of walk cost, and the cost past which it adds nothing.</summary>
    private const float HotBloom = 90f, HotFalloff = 4f;
    private const int HotReach = 24;

    /// <summary>Cells upwind a cell looks for cover.</summary>
    private const int WindScan = 10;

    /// <summary>Slabs of upwind rise that count as full shelter.</summary>
    private const float FullCover = 8f;

    /// <summary>Slabs of local relief at which ruggedness saturates.</summary>
    private const int FullRelief = 8;

    /// <summary>The tallest mountain a footprint allows, in slabs.</summary>
    internal static float MountainCap(int size) => Math.Max(8f, size * (40f / 128f));

    /// <summary>
    /// How hard the wind blows, from <see cref="IslandParams.Wind"/>: 0 still, 1 the
    /// nominal figures (the knob's middle), 2 twice them. Every modifier exposure
    /// drives is multiplied by it; the exposure byte itself is geometry and is not.
    /// </summary>
    private static float Gust(IslandParams p) => 2f * Math.Clamp(p.Wind, 0f, 1f);

    /// <summary>
    /// Rolls the sun, then fills the six axes in a fixed order: the shape axes
    /// first, then moisture (which reads the lee), then warmth (which reads
    /// everything, and rolls the hot water on a cold Domain).
    /// </summary>
    public static void Measure(int seed, IslandParams p, IslandData d)
    {
        d.Sun = (int)(Hash(seed, 0x5A4Eu) % 8);
        MeasureRuggedness(d);
        MeasureExposure(d);
        MeasureRimDistance(d);
        MeasureMoisture(seed, p, d);
        MeasureWarmth(seed, p, d);
    }

    /// <summary>
    /// The Domain's background moisture, wobbled by noise into patches; the rain
    /// shadow, drier in the lee by how sheltered the cell is; the gorge damp, a gain
    /// where the ground is both sheltered and broken; plus what its fresh water
    /// adds: a walk cost from watered columns (goo waters nothing), a cell per cell
    /// along or down and <see cref="ClimbCost"/> more per slab up, so a river waters
    /// the plain it crosses and not the mountain or the canyon wall it passes,
    /// decayed over <see cref="MoistureFalloff"/> cells. The walk cost is kept as
    /// <see cref="IslandData.WaterDistance"/>. A rock landform and its fringe carry
    /// patches of drought.
    /// </summary>
    private static void MeasureMoisture(int seed, IslandParams p, IslandData d)
    {
        int n = d.Size;
        var watered = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            watered[x, z] = d.HasLand(x, z) && d.WaterLevel[x, z] != IslandData.NoLand
                            && d.Fluid[x, z] != (byte)FluidKind.Goo;
        int[,] cost = WalkCost(d, watered, WaterReach);

        // Rock and its fringe: where the drought patches may fall.
        int[,] toRock = Flood.Distance(n,
            (x, z) => d.HasLand(x, z) && (Surfaces.Rocky((LandformType)d.Landform[x, z]) || d.Canyon[x, z]),
            (_, _, nx, nz) => d.HasLand(nx, nz),
            cap: RockFringe);

        var wobble = new Noise(seed + 71_003, 0.05f, octaves: 3);
        var background = new Noise(seed + 71_007, 0.03f, octaves: 2);
        var drought = new Noise(seed + 71_011, 0.06f, octaves: 2);
        float baseline = 255f * Math.Clamp(p.Moisture, 0f, 1f);
        float gust = Gust(p);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            d.WaterDistance[x, z] = (byte)(cost[x, z] < 0 ? WaterReach : cost[x, z]);

            float shelter = 1f - d.Exposure[x, z] / 255f;
            float moisture = baseline + BackgroundWobble * (background.At(x, z) * 2f - 1f);
            moisture -= RainShadow * gust * shelter;
            moisture += GorgeDamp * gust * shelter * (d.Ruggedness[x, z] / 255f);
            if (cost[x, z] >= 0 && cost[x, z] <= MoistureReach)
            {
                float sway = 1f + MoistureWobble * (wobble.At(x, z) * 2f - 1f);
                moisture += Math.Max(0f, WaterMoisture * MathF.Exp(-cost[x, z] * sway / MoistureFalloff) - WaterFloor);
            }
            if (toRock[x, z] >= 0 && drought.At(x, z) > RockDroughtBar) moisture -= RockDrought;

            d.Moisture[x, z] = (byte)Mathf.Clamp(Mathf.RoundToInt(moisture), 0, 255);
        }
    }

    /// <summary>
    /// Walk cost out from <paramref name="source"/> cells over the land, as water
    /// spreads: a cell per cell along or down, <see cref="ClimbCost"/> more per slab
    /// up, the first step up off the source free (that is the bank). A bucket per
    /// unit of cost (Dial's queue): integer costs, scan order kept. −1 where nothing
    /// is reached within <paramref name="reach"/>.
    /// </summary>
    private static int[,] WalkCost(IslandData d, bool[,] source, int reach)
    {
        int n = d.Size;
        var cost = new int[n, n];
        var buckets = new List<Vector2I>[reach + 1];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            cost[x, z] = -1;
            if (!source[x, z]) continue;
            cost[x, z] = 0;
            (buckets[0] ??= new List<Vector2I>()).Add(new Vector2I(x, z));
        }

        for (int c = 0; c <= reach; c++)
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
                    if (next > reach) continue;
                    if (cost[nx, nz] >= 0 && cost[nx, nz] <= next) continue;
                    cost[nx, nz] = next;
                    (buckets[next] ??= new List<Vector2I>()).Add(new Vector2I(nx, nz));
                }
            }
        }
        return cost;
    }

    /// <summary>
    /// On a cold Domain, some springs and small pools run hot. The chance is
    /// <see cref="HotSpringChance"/> / <see cref="HotPoolChance"/> at a warmth knob
    /// of 0, falling to nothing at <see cref="HotClimateBelow"/>; a pool is a body of
    /// standing water with no watercourse in it and at most <see cref="HotPoolMax"/>
    /// cells. Fills <see cref="IslandData.Hot"/> and <see cref="IslandData.HotWater"/>.
    /// </summary>
    private static bool FindHotWater(int seed, IslandParams p, IslandData d)
    {
        float coldness = Mathf.Clamp((HotClimateBelow - Math.Clamp(p.Warmth, 0f, 1f)) / HotClimateBelow, 0f, 1f);
        if (coldness <= 0f) return false;
        int n = d.Size;

        foreach (Vector2I c in d.Springs)
            if (Hash01(seed, 0x4075u ^ (uint)(c.X * 733 + c.Y * 7919)) < HotSpringChance * coldness)
                d.Hot[c.X, c.Y] = true;

        int bodies = Math.Max(1, d.WaterBodies);
        var size = new int[bodies];
        var flows = new bool[bodies];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            int id = d.WaterBody[x, z];
            if (id < 0 || id >= bodies) continue;
            size[id]++;
            if (d.River[x, z] || d.Fluid[x, z] != (byte)FluidKind.Water) flows[id] = true;
        }
        var hotPool = new bool[bodies];
        for (int id = 0; id < bodies; id++)
            hotPool[id] = !flows[id] && size[id] > 0 && size[id] <= HotPoolMax
                          && Hash01(seed, 0x4076u ^ (uint)id * 2654435761u) < HotPoolChance * coldness;

        bool any = false;
        d.HotWater.Clear();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            int id = d.WaterBody[x, z];
            if (id >= 0 && id < bodies && hotPool[id]) d.Hot[x, z] = true;
            if (!d.Hot[x, z]) continue;
            d.HotWater.Add(new Vector2I(x, z));
            any = true;
        }
        return any;
    }

    /// <summary>
    /// Warmth: the Domain's background (<see cref="IslandParams.Warmth"/>) over the
    /// whole island, then a lapse on mountains alone, measured from each mountain's
    /// own foot (<see cref="Relief.MountainFoot"/> read off the finished surface):
    /// nothing up to <see cref="ColdFrom"/> of the mountain cap above the foot,
    /// then the full loss over <see cref="LapseReach"/> more, so a mountain's upper
    /// part is cold and its top is snow at every footprint and whatever it stands
    /// on, and no plateau, mesa or massif is ever cold. Then the small modifiers:
    /// the sun, a slope descending toward it warmer and one descending away
    /// colder; the frost hollows, a basin floor or a sinkhole's pit colder than its
    /// rung; the lee a little warmer; the rim a little colder; the bloom of any
    /// hot water (<see cref="FindHotWater"/>), <see cref="HotBloom"/> at the source
    /// decaying over <see cref="HotFalloff"/> cells of walk cost, so a frigid Domain
    /// keeps a meadow round its hot spring; and wet ground pulled toward
    /// <see cref="Temperate"/> from either side. No land leaves it all zero.
    /// </summary>
    private static void MeasureWarmth(int seed, IslandParams p, IslandData d)
    {
        int n = d.Size;
        var land = new bool[n, n];
        var eff = new short[n, n];
        var isMountain = new bool[n, n];
        var regionLow = new Dictionary<int, float>();
        bool any = false;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            any = true;
            land[x, z] = true;
            eff[x, z] = d.EffectiveLevel(x, z);
            isMountain[x, z] = (LandformType)d.Landform[x, z] == LandformType.Mountain;
            int r = d.Region[x, z];
            if (!regionLow.TryGetValue(r, out float low) || eff[x, z] < low) regionLow[r] = eff[x, z];
        }
        if (!any) return;

        // A mountain that meets only the aether starts from its own lowest ground.
        float[,] foot = Relief.MountainFoot(land, eff, isMountain, (x, z) => regionLow[d.Region[x, z]]);

        float cap = MountainCap(d.Size);
        float coldFrom = cap * ColdFrom;
        float reach = Math.Max(1f, cap * LapseReach);
        float baseline = WarmthFloor + WarmthSpan * Math.Clamp(p.Warmth, 0f, 1f);
        float gust = Gust(p);
        Vector2 sun = d.SunVector;
        int[,]? hot = FindHotWater(seed, p, d) ? WalkCost(d, d.Hot, HotReach) : null;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            float warmth = baseline;
            if (isMountain[x, z])
            {
                float t = Mathf.Clamp((eff[x, z] - foot[x, z] - coldFrom) / reach, 0f, 1f);
                warmth -= LapseShare * t;
            }

            warmth += SunWarmth * SunFacing(eff, land, n, x, z, sun);
            if (Hollow(d, eff, land, n, x, z)) warmth -= HollowChill;
            warmth += LeeWarmth * gust * (1f - d.Exposure[x, z] / 255f);
            warmth -= RimChill * (1f - Math.Min((int)d.RimDistance[x, z], RimChillReach) / (float)RimChillReach);
            if (hot != null && hot[x, z] >= 0) warmth += HotBloom * MathF.Exp(-hot[x, z] / HotFalloff);
            warmth = Temperate + (warmth - Temperate) * (1f - MoistTemper * d.Moisture[x, z] / 255f);

            d.Warmth[x, z] = (byte)Mathf.Clamp(Mathf.RoundToInt(warmth), 0, 255);
        }
    }

    /// <summary>
    /// How far a cell's slope is turned to the sun, −1 … 1: the downhill direction
    /// of the effective surface dotted with the way to the sun, over
    /// <see cref="SunSlope"/>. Flat ground is 0, so the label is untouched; water
    /// is flat by construction.
    /// </summary>
    private static float SunFacing(short[,] eff, bool[,] land, int n, int x, int z, Vector2 sun)
    {
        float gx = Gradient(eff, land, n, x, z, 1, 0);
        float gz = Gradient(eff, land, n, x, z, 0, 1);
        // Downhill is minus the gradient; facing the sun is downhill toward it.
        return Mathf.Clamp(-(gx * sun.X + gz * sun.Y) / SunSlope, -1f, 1f);
    }

    /// <summary>The same reading off the finished data, for the audit: how far a cell's slope is turned to the Domain's sun, −1 … 1.</summary>
    internal static float SunFacing(IslandData d, int x, int z)
    {
        int n = d.Size;
        float Along(int dx, int dz)
        {
            bool fore = InBounds(n, x + dx, z + dz) && d.HasLand(x + dx, z + dz);
            bool back = InBounds(n, x - dx, z - dz) && d.HasLand(x - dx, z - dz);
            if (fore && back) return (d.EffectiveLevel(x + dx, z + dz) - d.EffectiveLevel(x - dx, z - dz)) * 0.5f;
            if (fore) return d.EffectiveLevel(x + dx, z + dz) - d.EffectiveLevel(x, z);
            if (back) return d.EffectiveLevel(x, z) - d.EffectiveLevel(x - dx, z - dz);
            return 0f;
        }
        Vector2 sun = d.SunVector;
        return Mathf.Clamp(-(Along(1, 0) * sun.X + Along(0, 1) * sun.Y) / SunSlope, -1f, 1f);
    }

    /// <summary>Slabs per cell the surface rises along (dx, dz): central where both neighbours are land, one-sided at a coast.</summary>
    private static float Gradient(short[,] eff, bool[,] land, int n, int x, int z, int dx, int dz)
    {
        bool fore = InBounds(n, x + dx, z + dz) && land[x + dx, z + dz];
        bool back = InBounds(n, x - dx, z - dz) && land[x - dx, z - dz];
        if (fore && back) return (eff[x + dx, z + dz] - eff[x - dx, z - dz]) * 0.5f;
        if (fore) return eff[x + dx, z + dz] - eff[x, z];
        if (back) return eff[x, z] - eff[x - dx, z - dz];
        return 0f;
    }

    /// <summary>A frost hollow: any cell of a basin, or a sinkhole cell lying <see cref="HollowDrop"/> slabs or more under the ground within two cells of it — the pit, not the field between the pits.</summary>
    private static bool Hollow(IslandData d, short[,] eff, bool[,] land, int n, int x, int z)
    {
        var form = (LandformType)d.Landform[x, z];
        if (form == LandformType.Basin) return true;
        if (form != LandformType.Sinkholes) return false;

        short hi = eff[x, z];
        for (int ox = -2; ox <= 2; ox++)
        for (int oz = -2; oz <= 2; oz++)
        {
            int nx = x + ox, nz = z + oz;
            if (InBounds(n, nx, nz) && land[nx, nz] && eff[nx, nz] > hi) hi = eff[nx, nz];
        }
        return hi - eff[x, z] >= HollowDrop;
    }

    /// <summary>Spread of the surface within two cells, saturating at <see cref="FullRelief"/> slabs (<see cref="LocalRelief"/>).</summary>
    private static void MeasureRuggedness(IslandData d)
    {
        int n = d.Size;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            d.Ruggedness[x, z] = (byte)Math.Min(255, LocalRelief(d, x, z) * (256 / FullRelief));
        }
    }

    /// <summary>
    /// Slabs between the lowest and highest effective surface within two cells,
    /// with water read as its bank (<see cref="BankLevel"/>): a stream through a
    /// plain is flat country, and a gorge is still its walls. What ruggedness is
    /// made of, and what spaces the fords (<c>Rivers.MarkFords</c>).
    /// </summary>
    internal static int LocalRelief(IslandData d, int x, int z)
    {
        int n = d.Size;
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
        return hi < lo ? 0 : hi - lo;
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
    /// gets the wind off the aether. Geometry only: how much the shelter is worth
    /// is the wind knob's business, read where the modifiers are applied.
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
