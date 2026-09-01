using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>Standing water: lakes sunk into flat patches, their shapes, and the goo puddles.</summary>
internal static class Lakes
{
    /// <summary>
    /// Fills basins with standing water. A basin is already a flat floor ringed by
    /// an inward-facing cliff — a bowl — so nothing needs carving: the lake is a
    /// level, and the terrain is untouched. That keeps the step grammar and the
    /// keel exactly as verified.
    ///
    /// A lake keeps at least this many cells of the patch's own rim dry, all
    /// the way round.
    private const int ShoreMargin = 2;

    /// <summary>
    /// And how many further cells the shore may wander in, per cell of coast.
    /// This is what keeps a lake from being a scale copy of the polygon it sits
    /// in — see the noise field in <see cref="PlaceLakes"/>.
    /// </summary>
    private const float ShoreWander = 3.4f;

    /// <summary>
    /// Sinks a lake into the interior of a flat patch — plain, mesa or basin —
    /// leaving a <see cref="ShoreMargin"/>-cell ring of the patch's original
    /// ground dry around it. <b>That ring is the containment</b>, which is what
    /// makes this work anywhere: it needs no rim of higher ground, no distance
    /// from the coast, and no particular landform, so lakes stop being a rarity
    /// confined to inland basins.
    ///
    /// Water can never touch anything outside the patch, because a flooded cell
    /// is at least two cells from the patch border and so is surrounded by the
    /// patch's own dry ring. The step from ring down to water is one slab —
    /// a walkable shore — while the terrain beneath drops three or four, well
    /// clear of the ambiguous two.
    ///
    /// <b>One lake, not a chain.</b> A patch beside one that already holds water
    /// stays dry: each lake fills to its own patch's rim, so a row of neighbouring
    /// patches flooding at slightly different levels steps across the island and
    /// reads as flooding rather than as lakes. Joining such a pair into one body —
    /// one level, a channel notched between them — was the previous answer, and it
    /// spreads the same sheet of water over more of the island instead.
    /// </summary>
    internal static short[,] PlaceLakes(int seed, IslandParams p, bool[,] land, int[,] region,
                                       int count, RegionPlan[] plan, short[,] surface,
                                       bool[,]? canyon)
    {
        int n = p.Size;

        // <b>How wet, once, because it drives three separate things.</b> Lakes used
        // to be a count and nothing else: the knob changed how many patches held
        // water and never how much water a patch held, and since a patch beside a
        // lake stays dry the count saturates — over the top quarter of the slider
        // the island gained 10% more water and looked identical. It now also sets
        // which patches are big enough to bother with and how far the shore stands
        // in, so the top of the range is a Domain of broad lakes rather than the
        // same tarns counted again.
        float wet = Math.Clamp(p.Lakes, 0f, 1f);

        var water = new short[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) water[x, z] = IslandData.NoLand;

        int[,] inset = PatchInset(land, region);

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

        // A canyon is a drain. It cuts seven slabs through whatever it crosses,
        // including a patch's rim — and the rim is what sets the water level, so
        // a patch with a trench through it would fill to the bottom of the trench
        // and swallow the surrounding country. A cut patch holds no water.
        var drained = new bool[count];
        if (canyon != null)
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (canyon[x, z] && land[x, z]) drained[region[x, z]] = true;

        // How much interior a patch needs before it is worth flooding. A dry Domain
        // only puts water in a patch with room for a lake; a wet one puts a pool in
        // anything that will hold one.
        int minInterior = Mathf.RoundToInt(Mathf.Lerp(40f, 12f, wet));

        var wants = new bool[count];
        for (int r = 0; r < count; r++)
        {
            if (drained[r]) continue;
            LandformType t = plan[r].Type;
            if (t != LandformType.Plain && t != LandformType.Mesa && t != LandformType.Basin) continue;
            if (interior[r] < minInterior || shore[r] == int.MaxValue) continue;

            // Rare on mesas, and a tarn rather than a lake when it happens.
            // Flooding a whole mesa interior turns the landform into a bowl: the
            // bed lands near the surrounding plain and the mesa reads as a wall
            // around a pit rather than as a tableland.
            // `Lakes` slides the whole thing: 0 leaves the Domain dry, 1 fills
            // every flat patch that could hold water. 0.5 is the old fixed rate —
            // for a patch of ordinary size; see the boost below.
            float chance = (t == LandformType.Mesa ? 0.10f : 0.22f) * wet * 2f;
            // <b>Broad country holds more water.</b> A large interior lifts the
            // roll by up to half again, so an island of big connected flats comes
            // out wetter — which is also where the shaped lakes below have room
            // to be shapes. A fragmented island's small interiors get the old
            // chance exactly, so it stays as dry as it ever was.
            chance *= 1f + Math.Min(interior[r], 320) / 320f * 0.5f;
            wants[r] = TerrainHash01(seed, 0xB10Au ^ (uint)r * 2654435761u) < chance;
        }

        // <b>Occasional smaller lakes.</b> A patch that lost the main roll can
        // still take a tarn — a pool a few cells wide, cropped from its own
        // interior. It adds variety on country that already qualifies for a lake
        // and nothing at all on fragmented maps, whose interiors fail the same
        // size test the main roll uses.
        var tarn = new bool[count];
        for (int r = 0; r < count; r++)
        {
            if (wants[r] || drained[r]) continue;
            LandformType t = plan[r].Type;
            if (t != LandformType.Plain && t != LandformType.Basin) continue;
            if (interior[r] < minInterior || shore[r] == int.MaxValue) continue;
            if (TerrainHash01(seed, 0x7AB0u ^ (uint)r * 2654435761u) >= 0.12f * wet * 2f) continue;
            wants[r] = true;
            tarn[r] = true;
        }

        // <b>No chains of lakes.</b> Each patch fills to its own rim, so a row of
        // neighbouring patches that all hold water is a row of pools at slightly
        // different levels stepping across the island — which reads as flooding,
        // not as lakes. A patch beside one that already holds water therefore
        // stays dry.
        //
        // Linking such a pair instead — one level, a channel notched between
        // them — was the previous answer, and it makes the two pools one body:
        // the same sheet of water spread over more of the island, which is the
        // look this removes.
        DropNeighbouringLakes(land, region, wants, count);

        var level = new int[count];
        var bed = new int[count];
        for (int r = 0; r < count; r++)
        {
            if (!wants[r]) continue;
            level[r] = shore[r] - 1;
            // Two or three slabs of water; the bed therefore sits three or four
            // below the ring, never the ambiguous two.
            bed[r] = level[r] - (2 + (int)(TerrainHash01(seed, 0x1A4Eu ^ (uint)r * 40503u) * 2f));
        }

        // <b>How far in the water starts, per cell, not per island.</b> A lake
        // used to be exactly the patch's interior at a fixed inset, which makes
        // its outline a scale copy of the patch border — and a patch border is a
        // Voronoi edge, so lakes came out as polygons with long straight sides.
        // Wandering the inset instead means the shore is the patch's shape read
        // through a noise field: bays where the margin runs wide, points where it
        // runs narrow. The minimum is still ShoreMargin, so the dry ring that
        // holds the water in is exactly as thick as it ever was.
        // A wet Domain's shore wanders less far in, so each lake fills more of the
        // patch that holds it: the same outline, drawn closer to the rim. The
        // minimum is still ShoreMargin whatever the setting, so the dry ring that
        // holds the water in is exactly as thick as it ever was.
        var ragged = new Noise(seed + 4242, frequency: 0.13f, octaves: 3);
        // The floor at the wet end used to be 0.45, and at that amplitude the
        // integer truncation below flattens the wander to nearly nothing — the
        // margin comes out a constant, and a constant inset from a Voronoi edge
        // is a straight shoreline. 0.85 keeps two to three cells of swing at
        // full wet, so a big lake's shore still reads as a shore.
        float wander = ShoreWander * Mathf.Lerp(1.35f, 0.85f, wet);
        var margin = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            margin[x, z] = ShoreMargin + (int)(ragged.At(x, z) * wander);

        // Which interior cells actually become water: the largest 4-connected
        // component of each patch's interior. A pinched patch can otherwise leave
        // two pools meeting only at a corner, which reads as a broken lake.
        bool[,] pool = LakeBody(land, region, inset, wants, count, margin);

        // Mesa tarns are kept to a few cells around their centre rather than
        // taking the whole interior.
        for (int r = 0; r < count; r++)
        {
            if (!wants[r] || plan[r].Type != LandformType.Mesa) continue;
            (int cx, int cz) = DeepestCell(region, inset, pool, r, n);
            if (cx < 0) continue;
            float capped = 1.6f + TerrainHash01(seed, 0x7A2Bu ^ (uint)r * 40503u) * 1.2f;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!pool[x, z] || region[x, z] != r) continue;
                float dx = x - cx, dz = z - cz;
                if (MathF.Sqrt(dx * dx + dz * dz) > capped) pool[x, z] = false;
            }
        }

        // A big pool need not be one big lake — see ShapeLakes.
        LakeStyle[] style = ShapeLakes(seed, n, region, count, plan, wants, tarn, pool);

        // A few lakes get an islet: cells left uncarved, raised if need be so they
        // break the surface. Round, not the square a Chebyshev radius would give.
        // Only the plain single lakes: the shaped ones carry their own dry ground.
        var islet = new bool[n, n];
        var wobble = new Noise(seed + 1212, frequency: 0.45f, octaves: 2);
        for (int r = 0; r < count; r++)
        {
            if (!wants[r] || style[r] != LakeStyle.Single) continue;
            if (TerrainHash01(seed, 0x15EDu ^ (uint)r * 2654435761u) > 0.35f) continue;

            (int cx, int cz) = DeepestCell(region, inset, pool, r, n);
            if (cx < 0) continue;

            float rad = 0.9f + TerrainHash01(seed, 0x0DDu ^ (uint)r * 40503u) * 0.9f;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!pool[x, z] || region[x, z] != r) continue;
                float dx = x - cx, dz = z - cz;
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d <= rad * (0.75f + 0.5f * wobble.At(x, z))) islet[x, z] = true;
            }
        }

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!pool[x, z]) continue;
            int r = region[x, z];

            if (islet[x, z]) { surface[x, z] = Terrain.SlabClamp(level[r] + 1); continue; }
            surface[x, z] = Terrain.SlabClamp(bed[r]);
            water[x, z] = (short)level[r];
        }

        RemoveDiagonalWater(surface, water, region, level);
        RaiseSunkenShores(land, surface, water);
        LevelShores(land, surface, water);
        return water;
    }

    /// <summary>How a patch's pool is drawn — see <see cref="ShapeLakes"/>.</summary>
    private enum LakeStyle : byte { Single, Tarn, Thousand, Ring, Crescent, Cross }

    /// <summary>Cells of annulus a ring or crescent lake keeps at the shore.</summary>
    private const int RingWidth = 2;

    /// <summary>
    /// Gives a big pool a shape other than "all of it".
    ///
    /// <para>Left alone, every lake is Just One Big Lake: the patch's interior,
    /// filled. Where the pool is large enough to have an inside, it now rolls a
    /// style — still one big lake more often than not, else a <b>thousand-lakes</b>
    /// scatter of separate pools, a <b>ring</b> round a dry island of its own
    /// floor, a <b>crescent</b> (the same ring with a bite taken out of one
    /// side), a ragged <b>cross</b>, or a <b>tarn</b> cropped small. Every shape
    /// is a subset of the pool the containment rules already approved, so the
    /// dry ring that holds the water in is untouched — and the ground a shape
    /// leaves dry is the patch's own floor, which
    /// <see cref="RaiseSunkenShores"/> lifts to a walkable slab above the water
    /// exactly as it does for a wandering shoreline.</para>
    ///
    /// <para><b>Small pools keep the old behaviour to the cell.</b> A shape
    /// needs room — a ring wants an inside, a scatter wants gaps — so anything
    /// under the size floor stays a single body, which is why a fragmented
    /// island's little lakes come out exactly as they always did.</para>
    /// </summary>
    private static LakeStyle[] ShapeLakes(int seed, int n, int[,] region, int count,
                                          RegionPlan[] plan, bool[] wants, bool[] tarn,
                                          bool[,] pool)
    {
        var style = new LakeStyle[count];

        // The pool's own inset: distance to the nearest cell outside it. This is
        // what a ring is drawn from, and its maximum is how much "inside" the
        // pool has to spend.
        var depth = new int[n, n];
        var q = new Queue<(int X, int Z)>();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            depth[x, z] = -1;
            if (!pool[x, z]) continue;
            bool edge = false;
            for (int k = 0; k < 4 && !edge; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                edge = !InBounds(n, nx, nz) || !pool[nx, nz];
            }
            if (!edge) continue;
            depth[x, z] = 0;
            q.Enqueue((x, z));
        }
        while (q.Count > 0)
        {
            var (cx, cz) = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + Dx[k], nz = cz + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (!pool[nx, nz] || depth[nx, nz] >= 0) continue;
                depth[nx, nz] = depth[cx, cz] + 1;
                q.Enqueue((nx, nz));
            }
        }

        var area = new int[count];
        var deep = new int[count];
        Array.Fill(deep, -1);
        var sumX = new long[count];
        var sumZ = new long[count];
        var deepAt = new (int X, int Z)[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!pool[x, z]) continue;
            int r = region[x, z];
            area[r]++;
            sumX[r] += x;
            sumZ[r] += z;
            if (depth[x, z] > deep[r]) { deep[r] = depth[x, z]; deepAt[r] = (x, z); }
        }

        for (int r = 0; r < count; r++)
        {
            if (!wants[r] || plan[r].Type == LandformType.Mesa) continue;
            if (tarn[r]) { style[r] = LakeStyle.Tarn; continue; }
            if (area[r] < 40) continue;                       // too small to shape

            float roll = TerrainHash01(seed, 0x5A9Eu ^ (uint)r * 2654435761u);
            style[r] = roll switch
            {
                < 0.40f => LakeStyle.Single,
                < 0.56f => LakeStyle.Thousand,
                < 0.70f => LakeStyle.Ring,
                < 0.84f => LakeStyle.Crescent,
                < 0.92f => LakeStyle.Cross,
                _ => LakeStyle.Tarn,
            };
            // A ring or a crescent is drawn from the pool's inset, so the pool
            // has to be deep enough to have an inside at all.
            if (style[r] is LakeStyle.Ring or LakeStyle.Crescent && deep[r] < RingWidth + 2)
                style[r] = LakeStyle.Single;
        }

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
                    // The scatter: pools where a chunky noise field runs low,
                    // dry ground between them. The field's wavelength is what
                    // keeps the pools pool-sized rather than salt-and-pepper.
                    drain = scatter.At(x, z) >= 0.52f;
                    break;
                case LakeStyle.Ring:
                    drain = depth[x, z] > RingWidth;
                    break;
                case LakeStyle.Crescent:
                {
                    // The ring, with its core stamped out again off-centre: the
                    // overlap of the two discs is the bite, and what survives is
                    // the moon.
                    int dir = (int)(TerrainHash01(seed, 0xC3E5u ^ (uint)r * 40503u) * 8f) & 7;
                    int ox = x - ((RingWidth + 1) * Dx8[dir]);
                    int oz = z - ((RingWidth + 1) * Dz8[dir]);
                    drain = InBounds(n, ox, oz)
                            && region[ox, oz] == r && depth[ox, oz] > RingWidth;
                    break;
                }
                case LakeStyle.Cross:
                {
                    long cx = sumX[r] / area[r], cz = sumZ[r] / area[r];
                    // The bars bend and breathe. Two straight three-cell bars
                    // through the centroid read as a stamp, not as water: so
                    // each arm's centreline drifts along its length on the
                    // scatter field, and its half-width wanders between one
                    // and two and a half cells. Sampled at offsets so the two
                    // arms and the width move independently.
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
                    float radius = 2.0f + TerrainHash01(seed, 0x7A2Cu ^ (uint)r * 40503u) * 1.4f;
                    drain = MathF.Sqrt(dx * dx + dz * dz) > radius;
                    break;
                }
            }
            if (drain) pool[x, z] = false;
        }

        // Specks read as noise, not as lakes: any shaped remnant under a few
        // cells goes dry. The seen-marker doubles as the component id.
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
        return style;
    }

    /// <summary>Islands that get goo at all. Most get none — it is a find.</summary>
    private const float GooIslandChance = 0.30f;

    /// <summary>
    /// Puddles of the other fluid — see <see cref="FluidKind.Goo"/>.
    ///
    /// <para>Placed like small tarns: a blob a few cells wide sunk into the
    /// interior of a dry flat patch, the patch's own ground as containment, the
    /// blob's ring setting its level. The rules that make it a different fluid
    /// rather than purple water live elsewhere: the routing treats goo as
    /// not-land so no river ever drains through it, its king's-move
    /// neighbourhood is barred to channels, and <c>Traversal.Sailable</c> says
    /// nothing sails it. Here it only has to be placed where no water stands
    /// within a king's move — which the patch's own dry interior guarantees, and
    /// a cell-level guard enforces anyway.</para>
    /// </summary>
    internal static void PlaceGoo(int seed, IslandParams p, bool[,] land, int[,] region,
                                 int count, RegionPlan[] plan, short[,] surface,
                                 short[,] water, byte[,] fluid)
    {
        int n = p.Size;
        if (TerrainHash01(seed, 0x600A11u) >= GooIslandChance) return;

        int[,] inset = PatchInset(land, region);

        var holdsWater = new bool[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && water[x, z] != IslandData.NoLand) holdsWater[region[x, z]] = true;

        // Dry flat patches with enough interior for a puddle and its ring.
        var interior = new int[count];
        var deep = new int[count];
        var deepAt = new (int X, int Z)[count];
        Array.Fill(deep, -1);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (inset[x, z] < 2) continue;
            int r = region[x, z];
            interior[r]++;
            if (inset[x, z] > deep[r]) { deep[r] = inset[x, z]; deepAt[r] = (x, z); }
        }

        var takers = new List<int>();
        for (int r = 0; r < count; r++)
        {
            if (holdsWater[r] || interior[r] < 16) continue;
            LandformType t = plan[r].Type;
            if (t != LandformType.Plain && t != LandformType.Basin) continue;
            takers.Add(r);
        }
        if (takers.Count == 0) return;

        // One to three puddles, in hash order so the choice is the seed's.
        takers.Sort((a, b) => TerrainHash01(seed, 0x60011u ^ (uint)a * 40503u)
            .CompareTo(TerrainHash01(seed, 0x60011u ^ (uint)b * 40503u)));
        int puddles = 1 + (int)(TerrainHash01(seed, 0x600C7u) * 3f);

        var wobble = new Noise(seed + 3434, frequency: 0.4f, octaves: 2);
        var cells = new List<(int X, int Z)>();

        foreach (int r in takers)
        {
            if (puddles <= 0) break;

            var (cx, cz) = deepAt[r];
            float radius = 1.4f + TerrainHash01(seed, 0x600D3u ^ (uint)r * 2654435761u) * 1.6f;

            cells.Clear();
            int reach = (int)radius + 1;
            for (int x = Math.Max(0, cx - reach); x <= Math.Min(n - 1, cx + reach); x++)
            for (int z = Math.Max(0, cz - reach); z <= Math.Min(n - 1, cz + reach); z++)
            {
                if (region[x, z] != r || inset[x, z] < 2) continue;
                if (water[x, z] != IslandData.NoLand) continue;
                float dx = x - cx, dz = z - cz;
                if (MathF.Sqrt(dx * dx + dz * dz) > radius * (0.75f + 0.5f * wobble.At(x, z)))
                    continue;

                // The guard that makes "never mixes" a fact rather than a hope.
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
            if (cells.Count < 3) continue;

            // The ring round the puddle sets its level, exactly as a patch's rim
            // sets a lake's.
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
            if (shore == int.MaxValue) continue;

            short level = (short)(shore - 1);
            foreach (var (x, z) in cells)
            {
                surface[x, z] = Terrain.SlabClamp(level - 2);
                water[x, z] = level;
                fluid[x, z] = (byte)FluidKind.Goo;
            }
            puddles--;
        }

        // The same shoreline corrections the lakes end on, over what goo left.
        RaiseSunkenShores(land, surface, water);
        LevelShores(land, surface, water);
    }

    /// <summary>
    /// Lifts any dry cell beside a lake that sits at or below its surface.
    ///
    /// The shore ring is what holds a lake in, and it holds because the patch is
    /// flat give or take a slab — but "give or take a slab" is not "never below",
    /// and a wandering shoreline leaves more of the patch's own interior dry than
    /// a fixed inset did. A dry cell standing under the water beside it is a hole
    /// in the bank, so it is brought up to the free step above the surface, which
    /// is where <see cref="LevelShores"/> would have put it coming the other way.
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

    /// <summary>
    /// Keeps lakes from forming a chain. Patches are visited in a fixed order and
    /// any patch that borders one already holding water is refused, so what
    /// survives is single bodies of water with dry country between them.
    /// </summary>
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
    /// Brings every dry cell that touches water down to exactly one slab above
    /// it. Left at its natural height a shore stands one <i>or two</i> above, and
    /// a two-slab shore is the one step height the grammar exists to avoid — a
    /// beach you cannot walk onto.
    ///
    /// It runs <b>last</b>, over the water that actually ended up there, and it
    /// does not care which patch a cell belongs to. Both matter: levelling before
    /// the channels were cut left every channel rim unhandled, and the same-patch
    /// test skipped the far bank of a channel by construction. That is where the
    /// four-slab shores were coming from.
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
    /// Drops water cells that join the rest of the lake only at a corner. A
    /// diagonal touch is not a join you can swim or walk through, and channel
    /// cutting can leave one. The cell is raised to shore height rather than
    /// simply drained — left at bed height it would be dry ground standing below
    /// the water beside it.
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
    /// The largest 4-connected component of each lake patch's interior. A pinched
    /// patch can leave two interior blobs meeting only at a corner; flooding both
    /// reads as one broken lake, so only the main body is kept.
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

    /// <summary>The pool cell of a region furthest from its shore, or (-1,-1).</summary>
    private static (int X, int Z) DeepestCell(int[,] region, int[,] inset, bool[,] pool, int r, int n)
    {
        int bx = -1, bz = -1, deepest = -1;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!pool[x, z] || region[x, z] != r || inset[x, z] <= deepest) continue;
            deepest = inset[x, z];
            bx = x; bz = z;
        }
        return (bx, bz);
    }

    /// <summary>Distance from each land cell to the nearest cell outside its own region.</summary>
    private static int[,] PatchInset(bool[,] land, int[,] region)
    {
        int n = land.GetLength(0);
        var inset = new int[n, n];
        var q = new Queue<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            inset[x, z] = -1;
            if (!land[x, z]) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                bool outside = !InBounds(n, nx, nz)
                               || !land[nx, nz] || region[nx, nz] != region[x, z];
                if (!outside) continue;
                inset[x, z] = 0;
                q.Enqueue((x, z));
                break;
            }
        }
        while (q.Count > 0)
        {
            var (x, z) = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                if (region[nx, nz] != region[x, z] || inset[nx, nz] >= 0) continue;
                inset[nx, nz] = inset[x, z] + 1;
                q.Enqueue((nx, nz));
            }
        }
        return inset;
    }
}
