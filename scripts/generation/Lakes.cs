using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// Standing fluids: lakes sunk into flat patches, the shapes big pools take, and
/// the goo puddles. A lake's containment is its patch's own dry rim, so it needs
/// no basin: the shore steps down one slab, the bed three or four.
/// </summary>
internal static class Lakes
{
    /// <summary>Cells of its patch's rim a lake always leaves dry, all the way round.</summary>
    private const int ShoreMargin = 2;

    /// <summary>Further cells the shore may wander in, so a lake is not a scale copy of its Voronoi patch.</summary>
    private const float ShoreWander = 3.4f;

    /// <summary>Cells of annulus a ring or crescent lake keeps at the shore.</summary>
    private const int RingWidth = 2;

    /// <summary>Share of islands that get goo at all.</summary>
    private const float GooIslandChance = 0.30f;

    /// <summary>How a patch's pool is drawn — see <see cref="ShapeLakes"/>.</summary>
    private enum LakeStyle : byte { Single, Tarn, Thousand, Ring, Crescent, Cross }

    /// <summary>
    /// Sinks lakes into the interiors of flat patches (plain, mesa, basin) and returns the
    /// water level per column, <see cref="IslandData.NoLand"/> where dry. One lake per patch
    /// and none beside another, so water never reads as flooding; <c>p.Lakes</c> drives chance, size floor and shore inset.
    /// </summary>
    internal static short[,] PlaceLakes(int seed, IslandParams p, bool[,] land, int[,] region,
                                       int count, RegionPlan[] plan, short[,] surface,
                                       bool[,]? canyon)
    {
        int n = p.Size;
        float wet = Math.Clamp(p.Lakes, 0f, 1f);

        var water = new short[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) water[x, z] = IslandData.NoLand;

        int[,] inset = PatchInset(land, region);
        var (interior, shore, drained) = MeasurePatches(n, land, region, count, inset, surface, canyon);
        var (wants, tarn) = RollLakes(seed, wet, count, plan, interior, shore, drained);
        DropNeighbouringLakes(land, region, wants, count);
        var (level, bed) = LakeLevels(seed, count, wants, shore);

        int[,] margin = ShoreMargins(seed, n, wet);
        bool[,] pool = LakeBody(land, region, inset, wants, count, margin);
        CropMesaTarns(seed, n, region, count, plan, wants, inset, pool);
        LakeStyle[] style = ShapeLakes(seed, n, region, count, plan, wants, tarn, pool);
        bool[,] islet = AddIslets(seed, n, region, count, wants, style, inset, pool);
        FloodPools(n, region, pool, islet, level, bed, surface, water);

        RemoveDiagonalWater(surface, water, region, level);
        RaiseSunkenShores(land, surface, water);
        LevelShores(land, surface, water);
        return water;
    }

    /// <summary>
    /// Per patch: interior cell count, lowest rim cell (which sets the level) and whether a
    /// canyon cuts it — a cut rim would fill to the trench floor, so that patch holds no water.
    /// </summary>
    private static (int[] Interior, int[] Shore, bool[] Drained) MeasurePatches(
        int n, bool[,] land, int[,] region, int count, int[,] inset, short[,] surface, bool[,]? canyon)
    {
        var interior = new int[count];
        var shore = new int[count];
        Array.Fill(shore, int.MaxValue);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (inset[x, z] < 0) continue;
            int r = region[x, z];
            if (inset[x, z] >= ShoreMargin) interior[r]++;
            else shore[r] = Math.Min(shore[r], surface[x, z]);
        }

        var drained = new bool[count];
        if (canyon != null)
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (canyon[x, z] && land[x, z]) drained[region[x, z]] = true;

        return (interior, shore, drained);
    }

    /// <summary>
    /// Which patches hold water: a main roll on plains, mesas and basins (rarer on a mesa,
    /// lifted by up to half again on a broad interior), then a tarn roll on the plains and basins that lost it.
    /// </summary>
    private static (bool[] Wants, bool[] Tarn) RollLakes(int seed, float wet, int count, RegionPlan[] plan,
                                                        int[] interior, int[] shore, bool[] drained)
    {
        int minInterior = Mathf.RoundToInt(Mathf.Lerp(40f, 12f, wet));

        var wants = new bool[count];
        for (int r = 0; r < count; r++)
        {
            if (drained[r]) continue;
            LandformType t = plan[r].Type;
            if (t != LandformType.Plain && t != LandformType.Mesa && t != LandformType.Basin) continue;
            if (interior[r] < minInterior || shore[r] == int.MaxValue) continue;

            float chance = (t == LandformType.Mesa ? 0.10f : 0.22f) * wet * 2f;
            chance *= 1f + Math.Min(interior[r], 320) / 320f * 0.5f;
            wants[r] = Hash01(seed, 0xB10Au ^ (uint)r * 2654435761u) < chance;
        }

        var tarn = new bool[count];
        for (int r = 0; r < count; r++)
        {
            if (wants[r] || drained[r]) continue;
            LandformType t = plan[r].Type;
            if (t != LandformType.Plain && t != LandformType.Basin) continue;
            if (interior[r] < minInterior || shore[r] == int.MaxValue) continue;
            if (Hash01(seed, 0x7AB0u ^ (uint)r * 2654435761u) >= 0.12f * wet * 2f) continue;
            wants[r] = true;
            tarn[r] = true;
        }
        return (wants, tarn);
    }

    /// <summary>Surface one slab under the rim, bed two or three under that — never the ambiguous two-slab drop.</summary>
    private static (int[] Level, int[] Bed) LakeLevels(int seed, int count, bool[] wants, int[] shore)
    {
        var level = new int[count];
        var bed = new int[count];
        for (int r = 0; r < count; r++)
        {
            if (!wants[r]) continue;
            level[r] = shore[r] - 1;
            bed[r] = level[r] - (2 + (int)(Hash01(seed, 0x1A4Eu ^ (uint)r * 40503u) * 2f));
        }
        return (level, bed);
    }

    /// <summary>
    /// Shore inset per cell: <see cref="ShoreMargin"/> plus a noise wander, so a shore is bays
    /// and points rather than a Voronoi edge; the minimum never moves, so the dry ring stays as thick.
    /// </summary>
    private static int[,] ShoreMargins(int seed, int n, float wet)
    {
        var ragged = new Noise(seed + 4242, frequency: 0.13f, octaves: 3);
        // 0.85 at full wet keeps two to three cells of swing; lower and the truncation flattens it.
        float wander = ShoreWander * Mathf.Lerp(1.35f, 0.85f, wet);
        var margin = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            margin[x, z] = ShoreMargin + (int)(ragged.At(x, z) * wander);
        return margin;
    }

    /// <summary>A mesa's lake is a tarn round its deepest point; flooding the whole interior turns the tableland into a wall round a pit.</summary>
    private static void CropMesaTarns(int seed, int n, int[,] region, int count, RegionPlan[] plan,
                                      bool[] wants, int[,] inset, bool[,] pool)
    {
        for (int r = 0; r < count; r++)
        {
            if (!wants[r] || plan[r].Type != LandformType.Mesa) continue;
            (int cx, int cz) = DeepestCell(region, inset, (i, j) => pool[i, j], r, n);
            if (cx < 0) continue;
            float capped = 1.6f + Hash01(seed, 0x7A2Bu ^ (uint)r * 40503u) * 1.2f;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!pool[x, z] || region[x, z] != r) continue;
                float dx = x - cx, dz = z - cz;
                if (MathF.Sqrt(dx * dx + dz * dz) > capped) pool[x, z] = false;
            }
        }
    }

    /// <summary>
    /// An islet in about a third of the single lakes: a wobbly disc round the deepest point,
    /// left dry and raised above the water. Shaped lakes carry their own dry ground.
    /// </summary>
    private static bool[,] AddIslets(int seed, int n, int[,] region, int count, bool[] wants,
                                     LakeStyle[] style, int[,] inset, bool[,] pool)
    {
        var islet = new bool[n, n];
        var wobble = new Noise(seed + 1212, frequency: 0.45f, octaves: 2);
        for (int r = 0; r < count; r++)
        {
            if (!wants[r] || style[r] != LakeStyle.Single) continue;
            if (Hash01(seed, 0x15EDu ^ (uint)r * 2654435761u) > 0.35f) continue;

            (int cx, int cz) = DeepestCell(region, inset, (i, j) => pool[i, j], r, n);
            if (cx < 0) continue;

            float rad = 0.9f + Hash01(seed, 0x0DDu ^ (uint)r * 40503u) * 0.9f;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!pool[x, z] || region[x, z] != r) continue;
                float dx = x - cx, dz = z - cz;
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d <= rad * (0.75f + 0.5f * wobble.At(x, z))) islet[x, z] = true;
            }
        }
        return islet;
    }

    /// <summary>Sinks each pool cell to its bed under its level; an islet cell rises to the free step above the water instead.</summary>
    private static void FloodPools(int n, int[,] region, bool[,] pool, bool[,] islet,
                                   int[] level, int[] bed, short[,] surface, short[,] water)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!pool[x, z]) continue;
            int r = region[x, z];

            if (islet[x, z]) { surface[x, z] = Terrain.SlabClamp(level[r] + 1); continue; }
            surface[x, z] = Terrain.SlabClamp(bed[r]);
            water[x, z] = (short)level[r];
        }
    }

    /// <summary>
    /// Gives a pool with room for an inside (40+ cells) a style: single more often than not,
    /// else a thousand-lakes scatter, a ring, a crescent, a ragged cross or a tarn. Every shape
    /// is a subset of the approved pool, so the containment ring is untouched; smaller pools stay single.
    /// </summary>
    private static LakeStyle[] ShapeLakes(int seed, int n, int[,] region, int count,
                                          RegionPlan[] plan, bool[] wants, bool[] tarn,
                                          bool[,] pool)
    {
        int[,] depth = PoolDepth(n, pool);
        var (area, sumX, sumZ, deep) = PoolStats(n, region, count, pool, depth);
        LakeStyle[] style = RollStyles(seed, count, plan, wants, tarn, area, deep);
        DrainByStyle(seed, n, region, count, style, pool, depth, area, sumX, sumZ);
        DropSpecks(n, region, style, pool);
        return style;
    }

    /// <summary>Each pool cell's distance from the pool's edge (−1 off the pool) — what a ring is drawn from.</summary>
    private static int[,] PoolDepth(int n, bool[,] pool)
    {
        bool AtEdge(int x, int z)
        {
            if (!pool[x, z]) return false;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !pool[nx, nz]) return true;
            }
            return false;
        }
        return Flood.Distance(n, AtEdge, (_, _, nx, nz) => pool[nx, nz]);
    }

    /// <summary>Per patch: pool area, coordinate sums (for the centroid) and greatest depth.</summary>
    private static (int[] Area, long[] SumX, long[] SumZ, int[] Deep) PoolStats(
        int n, int[,] region, int count, bool[,] pool, int[,] depth)
    {
        var area = new int[count];
        var sumX = new long[count];
        var sumZ = new long[count];
        var deep = new int[count];
        Array.Fill(deep, -1);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!pool[x, z]) continue;
            int r = region[x, z];
            area[r]++;
            sumX[r] += x;
            sumZ[r] += z;
            if (depth[x, z] > deep[r]) deep[r] = depth[x, z];
        }
        return (area, sumX, sumZ, deep);
    }

    /// <summary>Rolls a style for each lake big enough to shape; mesa lakes stay single and rolled tarns stay tarns.</summary>
    private static LakeStyle[] RollStyles(int seed, int count, RegionPlan[] plan, bool[] wants,
                                          bool[] tarn, int[] area, int[] deep)
    {
        var style = new LakeStyle[count];
        for (int r = 0; r < count; r++)
        {
            if (!wants[r] || plan[r].Type == LandformType.Mesa) continue;
            if (tarn[r]) { style[r] = LakeStyle.Tarn; continue; }
            if (area[r] < 40) continue;

            float roll = Hash01(seed, 0x5A9Eu ^ (uint)r * 2654435761u);
            style[r] = roll switch
            {
                < 0.40f => LakeStyle.Single,
                < 0.56f => LakeStyle.Thousand,
                < 0.70f => LakeStyle.Ring,
                < 0.84f => LakeStyle.Crescent,
                < 0.92f => LakeStyle.Cross,
                _ => LakeStyle.Tarn,
            };
            // A ring or a crescent is drawn from the pool's inset, so the pool needs an inside at all.
            if (style[r] is LakeStyle.Ring or LakeStyle.Crescent && deep[r] < RingWidth + 2)
                style[r] = LakeStyle.Single;
        }
        return style;
    }

    /// <summary>Dries the pool cells each lake's style leaves out; a single lake keeps all of them.</summary>
    private static void DrainByStyle(int seed, int n, int[,] region, int count, LakeStyle[] style,
                                     bool[,] pool, int[,] depth, int[] area, long[] sumX, long[] sumZ)
    {
        // A tarn is cropped round its deepest cell, found before the draining thins the pool.
        var deepAt = new (int X, int Z)[count];
        for (int r = 0; r < count; r++)
            if (style[r] == LakeStyle.Tarn) deepAt[r] = DeepestCell(region, depth, (i, j) => pool[i, j], r, n);

        var scatter = new Noise(seed + 9917, frequency: 0.17f, octaves: 2);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!pool[x, z]) continue;
            int r = region[x, z];
            bool drain = false;
            switch (style[r])
            {
                case LakeStyle.Thousand:
                    // Pools where a chunky noise field runs low; its wavelength keeps them pool-sized.
                    drain = scatter.At(x, z) >= 0.52f;
                    break;
                case LakeStyle.Ring:
                    drain = depth[x, z] > RingWidth;
                    break;
                case LakeStyle.Crescent:
                {
                    // The ring with its core stamped out again off-centre; the overlap is the bite.
                    int dir = (int)(Hash01(seed, 0xC3E5u ^ (uint)r * 40503u) * 8f) & 7;
                    int ox = x - ((RingWidth + 1) * Dx8[dir]);
                    int oz = z - ((RingWidth + 1) * Dz8[dir]);
                    drain = InBounds(n, ox, oz)
                            && region[ox, oz] == r && depth[ox, oz] > RingWidth;
                    break;
                }
                case LakeStyle.Cross:
                {
                    long cx = sumX[r] / area[r], cz = sumZ[r] / area[r];
                    // Each arm's centreline drifts along its length and its half-width wanders,
                    // sampled at offsets so the two arms and the width move independently.
                    float driftX = (scatter.At(z * 1.1f, 900f + r * 13f) - 0.5f) * 6f;
                    float driftZ = (scatter.At(700f + r * 13f, x * 1.1f) - 0.5f) * 6f;
                    float width = 1.0f + scatter.At(x * 1.7f + 300f, z * 1.7f) * 1.5f;
                    drain = Math.Abs(x - cx - driftX) > width
                            && Math.Abs(z - cz - driftZ) > width;
                    break;
                }
                case LakeStyle.Tarn:
                {
                    var (tx, tz) = deepAt[r];
                    float dx = x - tx, dz = z - tz;
                    float radius = 2.0f + Hash01(seed, 0x7A2Cu ^ (uint)r * 40503u) * 1.4f;
                    drain = MathF.Sqrt(dx * dx + dz * dz) > radius;
                    break;
                }
            }
            if (drain) pool[x, z] = false;
        }
    }

    /// <summary>Dries any shaped remnant under four cells; specks read as noise, not as lakes.</summary>
    private static void DropSpecks(int n, int[,] region, LakeStyle[] style, bool[,] pool)
    {
        var seen = new bool[n, n];
        var members = new List<(int X, int Z)>();
        var stack = new Stack<(int X, int Z)>();
        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!pool[sx, sz] || seen[sx, sz] || style[region[sx, sz]] == LakeStyle.Single)
                continue;

            members.Clear();
            seen[sx, sz] = true;
            stack.Push((sx, sz));
            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Pop();
                members.Add((cx, cz));
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (!InBounds(n, nx, nz) || seen[nx, nz]) continue;
                    if (!pool[nx, nz]) continue;
                    seen[nx, nz] = true;
                    stack.Push((nx, nz));
                }
            }
            if (members.Count >= 4) continue;
            foreach (var (mx, mz) in members) pool[mx, mz] = false;
        }
    }

    /// <summary>
    /// Sinks one to three goo puddles into dry flat patches, on the islands that roll goo at
    /// all. A puddle is placed like a small tarn — a blob round the patch's deepest cell, its own
    /// ring setting the level — and never within a king's move of water; the rest of what makes goo a fluid lives in the routing and traversal.
    /// </summary>
    internal static void PlaceGoo(int seed, IslandParams p, bool[,] land, int[,] region,
                                 int count, RegionPlan[] plan, short[,] surface,
                                 short[,] water, byte[,] fluid)
    {
        int n = p.Size;
        if (!p.Goo || Hash01(seed, 0x600A11u) >= GooIslandChance) return;

        int[,] inset = PatchInset(land, region);

        var holdsWater = new bool[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && water[x, z] != IslandData.NoLand) holdsWater[region[x, z]] = true;

        var interior = new int[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (inset[x, z] >= 2) interior[region[x, z]]++;

        // Dry flat patches with enough interior for a puddle and its ring.
        var takers = new List<int>();
        for (int r = 0; r < count; r++)
        {
            if (holdsWater[r] || interior[r] < 16) continue;
            LandformType t = plan[r].Type;
            if (t != LandformType.Plain && t != LandformType.Basin) continue;
            takers.Add(r);
        }
        if (takers.Count == 0) return;

        // In hash order, so which patches get a puddle is the seed's choice.
        takers.Sort((a, b) => Hash01(seed, 0x60011u ^ (uint)a * 40503u)
            .CompareTo(Hash01(seed, 0x60011u ^ (uint)b * 40503u)));
        int puddles = 1 + (int)(Hash01(seed, 0x600C7u) * 3f);

        var wobble = new Noise(seed + 3434, frequency: 0.4f, octaves: 2);
        foreach (int r in takers)
        {
            if (puddles <= 0) break;
            if (TryPlacePuddle(seed, n, r, land, region, inset, surface, water, fluid, wobble)) puddles--;
        }

        RaiseSunkenShores(land, surface, water);
        LevelShores(land, surface, water);
    }

    /// <summary>
    /// Floods a wobbly disc of patch <paramref name="r"/>'s interior round its deepest cell; false
    /// when fewer than three cells qualify or no dry ring surrounds them. The king's-move guard is what makes "goo never mixes" a fact.
    /// </summary>
    private static bool TryPlacePuddle(int seed, int n, int r, bool[,] land, int[,] region, int[,] inset,
                                       short[,] surface, short[,] water, byte[,] fluid, Noise wobble)
    {
        var (cx, cz) = DeepestCell(region, inset, (i, j) => inset[i, j] >= 2, r, n);
        float radius = 1.4f + Hash01(seed, 0x600D3u ^ (uint)r * 2654435761u) * 1.6f;

        var cells = new List<(int X, int Z)>();
        int reach = (int)radius + 1;
        for (int x = Math.Max(0, cx - reach); x <= Math.Min(n - 1, cx + reach); x++)
        for (int z = Math.Max(0, cz - reach); z <= Math.Min(n - 1, cz + reach); z++)
        {
            if (region[x, z] != r || inset[x, z] < 2) continue;
            if (water[x, z] != IslandData.NoLand) continue;
            float dx = x - cx, dz = z - cz;
            if (MathF.Sqrt(dx * dx + dz * dz) > radius * (0.75f + 0.5f * wobble.At(x, z)))
                continue;

            bool nearWater = false;
            for (int ox = -1; ox <= 1 && !nearWater; ox++)
            for (int oz = -1; oz <= 1 && !nearWater; oz++)
            {
                int nx = x + ox, nz = z + oz;
                if (!InBounds(n, nx, nz)) continue;
                nearWater = water[nx, nz] != IslandData.NoLand
                            && fluid[nx, nz] != (byte)FluidKind.Goo;
            }
            if (!nearWater) cells.Add((x, z));
        }
        if (cells.Count < 3) return false;

        // The ring round the puddle sets its level, as a patch's rim sets a lake's.
        int shore = int.MaxValue;
        foreach (var (x, z) in cells)
        for (int ox = -1; ox <= 1; ox++)
        for (int oz = -1; oz <= 1; oz++)
        {
            int nx = x + ox, nz = z + oz;
            if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
            if (cells.Contains((nx, nz))) continue;
            if (water[nx, nz] != IslandData.NoLand) continue;
            shore = Math.Min(shore, surface[nx, nz]);
        }
        if (shore == int.MaxValue) return false;

        short level = (short)(shore - 1);
        foreach (var (x, z) in cells)
        {
            surface[x, z] = Terrain.SlabClamp(level - 2);
            water[x, z] = level;
            fluid[x, z] = (byte)FluidKind.Goo;
        }
        return true;
    }

    /// <summary>
    /// Lifts any dry cell beside water that stands at or below its surface to the free step
    /// above it, in up to four in-place sweeps: a cell under the water beside it is a hole in the bank.
    /// </summary>
    internal static void RaiseSunkenShores(bool[,] land, short[,] surface, short[,] water)
    {
        int n = land.GetLength(0);
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!land[x, z] || water[x, z] != IslandData.NoLand) continue;

                int floor = int.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (water[nx, nz] != IslandData.NoLand)
                        floor = Math.Max(floor, water[nx, nz] + 1);
                }
                if (floor == int.MinValue || surface[x, z] >= floor) continue;
                surface[x, z] = Terrain.SlabClamp(floor);
                changed = true;
            }
            if (!changed) break;
        }
    }

    /// <summary>Refuses a lake in any patch bordering one already kept, in ascending patch order, so lakes never chain into stepped sheets of water.</summary>
    private static void DropNeighbouringLakes(bool[,] land, int[,] region, bool[] wants, int count)
    {
        int n = land.GetLength(0);
        var neighbours = new HashSet<int>[count];
        for (int i = 0; i < count; i++) neighbours[i] = new HashSet<int>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int r = region[x, z];
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                int o = region[nx, nz];
                if (o != r) neighbours[r].Add(o);
            }
        }

        var kept = new bool[count];
        for (int r = 0; r < count; r++)
        {
            if (!wants[r]) continue;
            bool beside = false;
            foreach (int nb in neighbours[r]) if (kept[nb]) { beside = true; break; }
            if (beside) wants[r] = false;
            else kept[r] = true;
        }
    }

    /// <summary>
    /// Caps every dry cell touching water at one slab above it, so no shore is a two-slab step.
    /// Runs last, over the water actually there, and ignores patches — a channel's far bank is a shore too.
    /// </summary>
    private static void LevelShores(bool[,] land, short[,] surface, short[,] water)
    {
        int n = land.GetLength(0);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z] || water[x, z] != IslandData.NoLand) continue;

            int cap = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (water[nx, nz] != IslandData.NoLand) cap = Math.Min(cap, water[nx, nz] + 1);
            }
            if (cap != int.MaxValue && surface[x, z] > cap) surface[x, z] = Terrain.SlabClamp(cap);
        }
    }

    /// <summary>
    /// Drops water cells that join the rest only at a corner, in up to four sweeps over 2×2 windows
    /// that read their own writes; the cell is raised to shore height, or it would be dry ground below the water beside it.
    /// </summary>
    private static void RemoveDiagonalWater(short[,] surface, short[,] water, int[,] region,
                                            int[] level)
    {
        int n = water.GetLength(0);
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            for (int x = 0; x + 1 < n; x++)
            for (int z = 0; z + 1 < n; z++)
            {
                bool a = water[x, z] != IslandData.NoLand;
                bool b = water[x + 1, z + 1] != IslandData.NoLand;
                bool c = water[x + 1, z] != IslandData.NoLand;
                bool d = water[x, z + 1] != IslandData.NoLand;

                int dx = -1, dz = -1;
                if (a && b && !c && !d) { dx = x + 1; dz = z + 1; }
                else if (c && d && !a && !b) { dx = x; dz = z + 1; }
                if (dx < 0) continue;

                water[dx, dz] = IslandData.NoLand;
                surface[dx, dz] = Terrain.SlabClamp(level[region[dx, dz]] + 1);
                changed = true;
            }
            if (!changed) break;
        }
    }

    /// <summary>
    /// The largest 4-connected component of each lake patch's interior (inset at least the local
    /// margin), kept only at twelve cells or more; flooding two blobs that meet at a corner reads as one broken lake.
    /// </summary>
    private static bool[,] LakeBody(bool[,] land, int[,] region, int[,] inset, bool[] wants,
                                    int count, int[,] margin)
    {
        int n = land.GetLength(0);
        var body = new bool[n, n];
        var seen = new bool[n, n];
        var stack = new Stack<(int X, int Z)>();
        var current = new List<(int X, int Z)>();
        var bestOf = new List<(int X, int Z)>[count];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (seen[x, z] || inset[x, z] < margin[x, z]) continue;
            int r = region[x, z];
            if (!wants[r]) continue;

            current.Clear();
            seen[x, z] = true;
            stack.Push((x, z));
            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Pop();
                current.Add((cx, cz));
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (!InBounds(n, nx, nz) || seen[nx, nz]) continue;
                    if (inset[nx, nz] < margin[nx, nz] || region[nx, nz] != r) continue;
                    seen[nx, nz] = true;
                    stack.Push((nx, nz));
                }
            }
            if (bestOf[r] == null || current.Count > bestOf[r].Count)
                bestOf[r] = new List<(int X, int Z)>(current);
        }

        for (int r = 0; r < count; r++)
        {
            if (bestOf[r] == null || bestOf[r].Count < 12) continue;
            foreach (var (x, z) in bestOf[r]) body[x, z] = true;
        }
        return body;
    }

    /// <summary>
    /// The first cell in scan order among region <paramref name="r"/>'s cells passing
    /// <paramref name="inSet"/> with the strictly greatest field value; (−1, −1) when there is none.
    /// </summary>
    private static (int X, int Z) DeepestCell(int[,] region, int[,] field, Func<int, int, bool> inSet, int r, int n)
    {
        int bx = -1, bz = -1, deepest = -1;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!inSet(x, z) || region[x, z] != r || field[x, z] <= deepest) continue;
            deepest = field[x, z];
            bx = x; bz = z;
        }
        return (bx, bz);
    }

    /// <summary>Distance from each land cell to the nearest cell outside its own region; −1 off land.</summary>
    private static int[,] PatchInset(bool[,] land, int[,] region)
    {
        int n = land.GetLength(0);
        bool OnBorder(int x, int z)
        {
            if (!land[x, z]) return false;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz] || region[nx, nz] != region[x, z]) return true;
            }
            return false;
        }
        return Flood.Distance(n, OnBorder, (x, z, nx, nz) => land[nx, nz] && region[nx, nz] == region[x, z]);
    }
}
