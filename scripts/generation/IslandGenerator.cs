using System;
using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Deterministic island generator: <see cref="Generate"/> is a pure function of
/// <c>(seed, params)</c>. All Y values it works in are <b>slab indices</b>.
/// Pipeline stages are documented in docs/island-generation.md §4.
///
/// Elevation is <b>not</b> a smooth field that gets quantised — that makes step
/// sizes an accident of the gradient, so terrain comes out uniformly rugged, and
/// under a radial envelope its contours are rings. The island is instead a
/// blanket of <b>regions</b>, each with a <see cref="LandformType"/> and a rung
/// on a plateau ladder, each generated under its own slope limit.
/// </summary>
public sealed class IslandGenerator
{
    /// <summary>Relief left at the shoreline, as a fraction of the cell's inland relief.</summary>
    private const float CoastLow = 0.45f;

    /// <summary>Cells inland over which <see cref="CoastLow"/> recovers to full relief.</summary>
    private const float CoastTaperCells = 3.5f;

    /// <summary>Turns around the circumference sampled for coastline lobes.</summary>
    private const float LobeRings = 1.7f;

    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };

    /// <summary>A region's assignment: what it is, and the level it is built from.</summary>
    private readonly struct RegionPlan
    {
        public readonly LandformType Type;
        public readonly int Plateau;        // slabs

        /// <summary>
        /// The rung group this region was unioned into. Neighbours in one group
        /// share a rung, which is exactly the statement "no cliff belongs here" —
        /// so the slope limiter can enforce it <i>across</i> the border instead of
        /// hoping a blurred amplitude field closes the gap on its own.
        /// </summary>
        public readonly int RungGroup;

        public RegionPlan(LandformType type, int plateau, int rungGroup)
        {
            Type = type;
            Plateau = plateau;
            RungGroup = rungGroup;
        }
    }

    public IslandData Generate(int seed, IslandParams p)
    {
        int n = p.Size;
        var data = new IslandData(n)
        {
            Style = ResolveStyle(seed, p),
            Character = ResolveCharacter(seed, p.Character),
        };

        bool[,] land = BuildMask(seed, p);

        // Bites are taken patch by patch, so the coast they leave runs along
        // region borders. Regions are rebuilt afterwards rather than re-indexed:
        // the partition is deterministic, so this simply re-derives it over the
        // land that remains.
        int[,] draft = BuildRegions(seed, p, land, out int draftCount);
        BiteRegions(seed, p, land, draft, draftCount);
        // Bites and the mask itself can leave two lobes meeting at a corner, which
        // is not a join you can walk. Filling the corner is done before the
        // component filter, so what it measures is what you can actually reach.
        CloseDiagonalJoins(land);

        // Unless the island is deliberately an archipelago, reduce it to a single
        // 4-connected landmass. Two comparable pieces that survive the size filter
        // may still meet only at a corner, which is not a join you can walk.
        if (p.Fragmentation < 0.25f) KeepLargestComponent(land);
        else DropSmallComponents(land, 0.2f);
        int[,] region = BuildRegions(seed, p, land, out int regionCount);

        // Smooth, sub-cell distance to coast. Shared by the coastal taper and the
        // keel — an integer field here is what made the underside a staircase.
        float[,] toCoast = DistanceToCoast(land);
        float[,] envelope = ReliefEnvelope(seed, p, land, toCoast);
        BuildBorders(land, region, regionCount, out HashSet<int>[] firstPass);
        LandformType[] types = AssignTypes(seed, p, land, region, regionCount, envelope, toCoast);

        // Adjacent mountains (and mesas) become one massif. A mountain penned
        // inside a single region has only a few cells of run for its whole rise,
        // which leaves no room for a foot — it can only be a wall.
        region = MergeAdjacentOfType(land, region, firstPass, ref regionCount, ref types);

        var borders = BuildBorders(land, region, regionCount, out HashSet<int>[] neighbours);
        RepairAdjacency(region, regionCount, neighbours, types);
        RestoreMissingLandforms(p, seed, region, regionCount, neighbours, types,
                                RegionCells(land, region, regionCount));
        RegionPlan[] plan = AssignPlateaus(seed, p, land, region, regionCount, envelope,
                                           neighbours, types);
        float[,] inward = InwardDistance(land, region, regionCount);

        short[,] surface = BuildSurface(seed, p, land, region, plan, inward);
        LimitSlope(surface, region, land, plan);
        bool[,]? canyon = WantsCanyon(seed, p)
            ? CarveCanyon(seed, p, land, region, plan, surface, borders)
            : null;
        ResolveAmbiguousSteps(surface, region, land, plan);

        // Lakes sink into the surface, so they run before the keel measures column
        // thickness — and after every step-grammar pass, which they must not undo.
        short[,] water = PlaceLakes(seed, p, land, region, regionCount, plan, surface, canyon);
        // Lakes cut the surface after the grammar passes, so both run once more
        // over what they left. Levelling a shore leaves the bank behind it
        // standing a few slabs proud, and an islet edge can land on the
        // ambiguous two.
        var exempt = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            exempt[x, z] = water[x, z] != IslandData.NoLand || (canyon != null && canyon[x, z]);

        // Settled together, not once each. Resolving a two-slab step lowers a
        // cell, which can leave a *three*-slab one behind it on a border the rules
        // forbid a cliff on — and the limiter closing that can in turn expose a
        // new two. Both passes only ever lower, so alternating them terminates.
        for (int settle = 0; settle < 6; settle++)
        {
            bool moved = LimitSlope(surface, region, land, plan, exempt);
            moved |= ResolveAmbiguousSteps(surface, region, land, plan, water);
            if (!moved) break;
        }
        short[,] keel = BuildKeel(seed, p, land, surface, toCoast);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            data.Land[x, z] = land[x, z];
            data.Region[x, z] = region[x, z];
            if (!land[x, z]) continue;

            short top = surface[x, z];
            short bottom = keel[x, z];
            if (bottom > top) bottom = top;                 // safety
            data.Spans[x, z] = new[] { new Span(bottom, top) };
            data.Material[x, z] = 0;
            data.Landform[x, z] = (byte)plan[region[x, z]].Type;
            data.WaterLevel[x, z] = water[x, z];
            data.Canyon[x, z] = canyon != null && canyon[x, z];
        }

        // Stage 5: read back what the terrain turned out to be. Pure analysis —
        // it changes nothing, so it stays outside the pipeline proper.
        Traversal.Analyse(data);
        return data;
    }

    // ---- Lakes ---------------------------------------------------------------

    /// <summary>
    /// Fills basins with standing water. A basin is already a flat floor ringed by
    /// an inward-facing cliff — a bowl — so nothing needs carving: the lake is a
    /// level, and the terrain is untouched. That keeps the step grammar and the
    /// keel exactly as verified.
    ///
    /// A lake keeps this many cells of the patch's own rim dry, all the way round.
    private const int ShoreMargin = 2;

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
    /// Adjacent lake patches whose shores agree to within a slab share one level
    /// and get a channel cut between them; a channel cell is only carved if every
    /// one of its neighbours also belongs to the pair, so linking two lakes can
    /// never open one to the outside.
    /// </summary>
    private static short[,] PlaceLakes(int seed, IslandParams p, bool[,] land, int[,] region,
                                       int count, RegionPlan[] plan, short[,] surface,
                                       bool[,]? canyon)
    {
        int n = p.Size;
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

        var wants = new bool[count];
        for (int r = 0; r < count; r++)
        {
            if (drained[r]) continue;
            LandformType t = plan[r].Type;
            if (t != LandformType.Plain && t != LandformType.Mesa && t != LandformType.Basin) continue;
            if (interior[r] < 25 || shore[r] == int.MaxValue) continue;

            // Rare on mesas, and a tarn rather than a lake when it happens.
            // Flooding a whole mesa interior turns the landform into a bowl: the
            // bed lands near the surrounding plain and the mesa reads as a wall
            // around a pit rather than as a tableland.
            float chance = t == LandformType.Mesa ? 0.10f : 0.22f;
            wants[r] = Hash01(seed, 0xB10Au ^ (uint)r * 2654435761u) < chance;
        }

        // Group neighbouring lakes that sit at the same height, so a channel
        // between them joins two surfaces rather than stepping between them.
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (inset[x, z] != 0) continue;
            int r = region[x, z];
            if (!wants[r]) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                int o = region[nx, nz];
                // Equal, not merely close: a slab of disagreement between two
                // grouped patches becomes a two-slab shore step on the higher one.
                if (o == r || !wants[o] || shore[r] != shore[o]) continue;
                int a = Find(r), b = Find(o);
                if (a != b) parent[b] = a;
            }
        }

        var groupShore = new int[count];
        Array.Fill(groupShore, int.MaxValue);
        for (int r = 0; r < count; r++)
            if (wants[r]) groupShore[Find(r)] = Math.Min(groupShore[Find(r)], shore[r]);

        var level = new int[count];
        var bed = new int[count];
        for (int r = 0; r < count; r++)
        {
            if (!wants[r]) continue;
            int g = Find(r);
            level[r] = groupShore[g] - 1;
            // Two or three slabs of water; the bed therefore sits three or four
            // below the ring, never the ambiguous two.
            bed[r] = level[r] - (2 + (int)(Hash01(seed, 0x1A4Eu ^ (uint)g * 40503u) * 2f));
        }

        // Which interior cells actually become water: the largest 4-connected
        // component of each patch's interior. A pinched patch can otherwise leave
        // two pools meeting only at a corner, which reads as a broken lake.
        bool[,] pool = LakeBody(land, region, inset, wants, count);

        // Mesa tarns are kept to a few cells around their centre rather than
        // taking the whole interior.
        for (int r = 0; r < count; r++)
        {
            if (!wants[r] || plan[r].Type != LandformType.Mesa) continue;
            (int cx, int cz) = DeepestCell(region, inset, pool, r, n);
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

        // A few lakes get an islet: cells left uncarved, raised if need be so they
        // break the surface. Round, not the square a Chebyshev radius would give.
        var islet = new bool[n, n];
        var wobble = new Noise(seed + 1212, frequency: 0.45f, octaves: 2);
        for (int r = 0; r < count; r++)
        {
            if (!wants[r]) continue;
            if (Hash01(seed, 0x15EDu ^ (uint)r * 2654435761u) > 0.35f) continue;

            (int cx, int cz) = DeepestCell(region, inset, pool, r, n);
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

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!pool[x, z]) continue;
            int r = region[x, z];

            if (islet[x, z]) { surface[x, z] = SlabClamp(level[r] + 1); continue; }
            surface[x, z] = SlabClamp(bed[r]);
            water[x, z] = (short)level[r];
        }

        CutLakeChannels(land, region, inset, wants, parent, level, bed, surface, water, Find);
        RemoveDiagonalWater(surface, water, region, level);
        LevelShores(land, surface, water);
        return water;
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
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (water[nx, nz] != IslandData.NoLand) cap = Math.Min(cap, water[nx, nz] + 1);
            }
            if (cap != int.MaxValue && surface[x, z] > cap) surface[x, z] = SlabClamp(cap);
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
                surface[dx, dz] = SlabClamp(level[region[dx, dz]] + 1);
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
    private static bool[,] LakeBody(bool[,] land, int[,] region, int[,] inset, bool[] wants, int count)
    {
        int n = land.GetLength(0);
        var body = new bool[n, n];
        var seen = new bool[n, n];
        var stack = new Stack<(int X, int Z)>();
        var best = new List<(int X, int Z)>();
        var current = new List<(int X, int Z)>();
        var bestOf = new List<(int X, int Z)>[count];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (seen[x, z] || inset[x, z] < ShoreMargin) continue;
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
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n || seen[nx, nz]) continue;
                    if (inset[nx, nz] < ShoreMargin || region[nx, nz] != r) continue;
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
        _ = best;
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
                bool outside = nx < 0 || nz < 0 || nx >= n || nz >= n
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
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                if (region[nx, nz] != region[x, z] || inset[nx, nz] >= 0) continue;
                inset[nx, nz] = inset[x, z] + 1;
                q.Enqueue((nx, nz));
            }
        }
        return inset;
    }

    /// <summary>
    /// Notches through the dry rings separating two lakes of the same group, so
    /// they read as one body of water. A cell is only carved when every one of its
    /// neighbours belongs to the same pair of patches — otherwise the notch would
    /// breach the ring outward and drain the lake.
    /// </summary>
    private static void CutLakeChannels(bool[,] land, int[,] region, int[,] inset, bool[] wants,
                                        int[] parent, int[] level, int[] bed,
                                        short[,] surface, short[,] water, Func<int, int> find)
    {
        int n = land.GetLength(0);
        var seam = new Dictionary<long, List<(int X, int Z)>>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int r = region[x, z];
            if (!wants[r]) continue;

            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                int o = region[nx, nz];
                if (o == r || !wants[o] || find(o) != find(r)) continue;

                long key = ((long)Math.Min(r, o) << 32) | (uint)Math.Max(r, o);
                if (!seam.TryGetValue(key, out var list)) seam[key] = list = new List<(int X, int Z)>();
                list.Add((x, z));
            }
        }

        foreach (var (key, cells) in seam)
        {
            int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
            var mid = cells[cells.Count / 2];

            // Radius two, because the ring is two cells thick on each side: a
            // shorter notch would not reach open water on either.
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                int x = mid.X + dx, z = mid.Z + dz;
                if (x < 0 || z < 0 || x >= n || z >= n || !land[x, z]) continue;
                int r = region[x, z];
                if (r != a && r != b) continue;
                if (inset[x, z] >= ShoreMargin) continue;               // already water

                bool safe = true;
                for (int k = 0; k < 4 && safe; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    safe = nx >= 0 && nz >= 0 && nx < n && nz < n && land[nx, nz]
                           && (region[nx, nz] == a || region[nx, nz] == b);
                }
                if (!safe) continue;

                surface[x, z] = SlabClamp(bed[r]);
                water[x, z] = (short)level[r];
            }
        }
    }

    // ---- Stage 1: footprint mask ----------------------------------------------

    private static bool[,] BuildMask(int seed, IslandParams p)
    {
        int n = p.Size;
        float radius = AutoRadius(p);
        float cx = (n - 1) * 0.5f, cz = (n - 1) * 0.5f;
        float irr = Math.Clamp(p.Irregularity, 0f, 1f);

        // Per-island silhouette: an ellipse at an arbitrary angle whose radius is
        // then modulated around the circumference, which is what produces bays,
        // capes and a long axis instead of a disc.
        float aspect = Mathf.Lerp(1f, 1.8f, irr * Hash01(seed, 0x51E1));
        float rot = Hash01(seed, 0x2C0F) * Mathf.Tau;
        float cosR = MathF.Cos(rot), sinR = MathF.Sin(rot);

        var lobes = new Noise(seed + 23, frequency: 1f, octaves: 2);
        var shape = new Noise(seed, frequency: 0.05f, octaves: 4)
            .WithWarp(amplitude: (0.25f + 0.55f * irr) * n, frequency: 0.6f / n);
        var blobs = new Noise(seed + 17, frequency: 0.09f, octaves: 3, ridged: true);

        // Bites taken out near the rim. A lobed disc is still a disc — one large
        // bite makes a crescent, smaller ones make bays and a harbour mouth.
        // Bites are not taken here: cutting a shape out of the raw mask leaves an
        // arc across whatever patches it crosses. They are applied to whole
        // regions once those exist — see BiteRegions.

        var field = new float[n, n];
        var norm = new float[n, n];
        var candidates = new List<float>(n * n / 4);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            float dx = x - cx, dz = z - cz;
            float rx = (dx * cosR + dz * sinR) * aspect;
            float rz = (-dx * sinR + dz * cosR) / aspect;
            float dist = MathF.Sqrt(rx * rx + rz * rz);

            // Sample the lobe field on the unit circle so it is seamless in angle
            // — sampling the angle itself would seam at ±π.
            float ang = MathF.Atan2(rz, rx);
            float lobe = lobes.At(MathF.Cos(ang) * LobeRings, MathF.Sin(ang) * LobeRings);
            float rEff = MathF.Max(1e-3f, radius * (1f + irr * 0.55f * (lobe * 2f - 1f)));

            float d = dist / rEff;
            norm[x, z] = d;

            float fall = 1f - FieldOps.SmoothStep(0.40f, 1f, d);
            float body = 0.35f + 0.65f * shape.At(x, z);
            float frag = Mathf.Lerp(1f, blobs.At(x, z), p.Fragmentation);
            field[x, z] = fall * body * frag;

            // `fall` is already 0 at d >= 1, so only the disc itself can be land.
            // Sampling wider would pad the quantile with guaranteed zeroes and
            // drag the threshold to 0, which is what made Coverage inert.
            if (d < 1f) candidates.Add(field[x, z]);
        }

        // Coverage is a fraction of the *candidate disc*, not of the whole grid —
        // most of the grid is empty aether and would otherwise swamp the quantile.
        float threshold = FieldOps.Quantile(candidates, 1f - Math.Clamp(p.Coverage, 0.01f, 0.99f));

        var mask = new bool[n, n];
        // Leave a one-cell border empty so every land cell has a reachable coast.
        for (int x = 1; x < n - 1; x++)
        for (int z = 1; z < n - 1; z++)
            mask[x, z] = norm[x, z] < 1f && field[x, z] > threshold;

        DropSmallComponents(mask, 0.2f);
        return mask;
    }

    /// <summary>
    /// Takes bites out of the island by deleting whole regions, not by cutting a
    /// shape out of the mask.
    ///
    /// Erasing a shape leaves that shape's outline on the coast — an arc, however
    /// the edge is softened — and slices in half whatever patches it crosses. A
    /// region that is mostly inside the bite is removed entirely instead, so the
    /// new coastline runs along region borders, which are already organic. It
    /// also makes the two bites on an island differ in size, since what each
    /// removes depends on the patches it happens to land on rather than on its
    /// own radius. A bite well inside the island punches a hole through it.
    /// </summary>
    private static void BiteRegions(int seed, IslandParams p, bool[,] land, int[,] region, int count)
    {
        float irr = Math.Clamp(p.Irregularity, 0f, 1f);
        if (irr < 0.15f || count == 0) return;

        int n = p.Size;
        float radius = AutoRadius(p);
        float cx = (n - 1) * 0.5f, cz = (n - 1) * 0.5f;

        var cells = new int[count];
        int remaining = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z]) { cells[region[x, z]]++; remaining++; }
        int original = remaining;

        int bites = 1 + (int)(Hash01(seed, 0x77A3) * (0.5f + 2.7f * irr));
        for (int i = 0; i < bites; i++)
        {
            uint salt = 0x9100u + (uint)i * 977u;
            float ang = Hash01(seed, salt) * Mathf.Tau;

            // Some bites are placed well inside and kept small, which takes out
            // interior patches and leaves a hole through the island rather than a
            // notch in its coast.
            bool interior = i == 0 && Hash01(seed, salt ^ 0xA5u) < 0.35f;
            float from = radius * (interior ? 0.10f + 0.35f * Hash01(seed, salt ^ 0x31u)
                                            : 0.25f + 0.85f * Hash01(seed, salt ^ 0x31u));
            float reach = radius * (interior ? 0.20f + 0.25f * Hash01(seed, salt ^ 0x57u)
                                             : 0.30f + 0.75f * Hash01(seed, salt ^ 0x57u));
            var at = new Vector2(cx + MathF.Cos(ang) * from, cz + MathF.Sin(ang) * from);

            // The bite's own outline is lobed too, so which patches fall inside is
            // not decided by a circle.
            var lobe = new Noise(seed + 3300 + i, frequency: 1f, octaves: 2);

            var inside = new int[count];
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!land[x, z]) continue;
                Vector2 d = new Vector2(x, z) - at;
                float a = MathF.Atan2(d.Y, d.X);
                float rEff = reach * (1f + 0.45f * (lobe.At(MathF.Cos(a) * 1.9f, MathF.Sin(a) * 1.9f) * 2f - 1f));
                if (d.Length() < rEff) inside[region[x, z]]++;
            }

            var doomed = new bool[count];
            int loss = 0;
            for (int r = 0; r < count; r++)
                if (cells[r] > 0 && inside[r] >= cells[r] * 0.5f) { doomed[r] = true; loss += cells[r]; }

            // Never eat the island. Two guards: no single bite may take a third of
            // what is left, and the bites together may not drop the island below
            // 60% of the land it started with. The per-bite cap alone is not
            // enough — three bites each under it still compound.
            if (loss == 0) continue;
            if (loss > remaining * 0.33f) continue;
            if (remaining - loss < original * 0.60f) continue;

            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (land[x, z] && doomed[region[x, z]]) land[x, z] = false;

            for (int r = 0; r < count; r++) if (doomed[r]) cells[r] = 0;
            remaining -= loss;
        }
    }

    /// <summary>
    /// Deletes landmasses smaller than <paramref name="keepFraction"/> of the
    /// largest. A deep bite can sever a cape from the mainland, which reads as a
    /// generation accident rather than an archipelago; a deliberate one comes
    /// from <see cref="IslandParams.Fragmentation"/> and survives if it is of
    /// comparable size.
    /// </summary>
    /// <summary>Reduces the mask to its single largest 4-connected component.</summary>
    /// <summary>
    /// Fills the corner where two land cells touch only diagonally. A corner is
    /// not a join you can walk, so left alone it is either a hairline break in a
    /// landmass the component filter cannot see (both sides are already one
    /// component) or a false connection in the audit. The filled cell is the one
    /// with more land around it, so the coast stays plausible.
    /// </summary>
    private static void CloseDiagonalJoins(bool[,] mask)
    {
        int n = mask.GetLength(0);
        for (int x = 1; x + 2 < n; x++)
        for (int z = 1; z + 2 < n; z++)
        {
            bool a = mask[x, z], b = mask[x + 1, z + 1];
            bool c = mask[x + 1, z], e = mask[x, z + 1];
            if (a && b && !c && !e) Fill(x + 1, z, x, z + 1);
            else if (c && e && !a && !b) Fill(x, z, x + 1, z + 1);
        }

        void Fill(int ax, int az, int bx, int bz)
            => mask[Neighbours(ax, az) >= Neighbours(bx, bz) ? ax : bx,
                    Neighbours(ax, az) >= Neighbours(bx, bz) ? az : bz] = true;

        int Neighbours(int x, int z)
        {
            int found = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || (dx == 0 && dz == 0)) continue;
                if (mask[nx, nz]) found++;
            }
            return found;
        }
    }

    private static void KeepLargestComponent(bool[,] mask) => DropSmallComponents(mask, 1f);

    private static void DropSmallComponents(bool[,] mask, float keepFraction)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) comp[x, z] = -1;

        var sizes = new List<int>();
        var stack = new Stack<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!mask[x, z] || comp[x, z] >= 0) continue;
            int id = sizes.Count, area = 0;
            comp[x, z] = id;
            stack.Push((x, z));
            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Pop();
                area++;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!mask[nx, nz] || comp[nx, nz] >= 0) continue;
                    comp[nx, nz] = id;
                    stack.Push((nx, nz));
                }
            }
            sizes.Add(area);
        }

        if (sizes.Count <= 1) return;

        int largest = 0;
        foreach (int a in sizes) largest = Math.Max(largest, a);
        // At keepFraction 1 only a component matching the largest survives, which
        // is how KeepLargestComponent reduces the island to one piece.
        int floor = (int)(largest * keepFraction);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (mask[x, z] && sizes[comp[x, z]] < floor) mask[x, z] = false;
    }

    // ---- Stage 2a: relief envelope (macro trend only) ------------------------

    /// <summary>
    /// Per-cell envelope in <c>[0, 1]</c> saying where this island's high ground
    /// lies. It does not shape elevation directly — doing that is what made the
    /// terrain radial. It only biases which rung each region lands on, and where
    /// mountains cluster.
    /// </summary>
    private static float[,] ReliefEnvelope(int seed, IslandParams p, bool[,] land, float[,] toCoast)
    {
        int n = p.Size;
        float radius = AutoRadius(p);
        var centre = new Vector2((n - 1) * 0.5f, (n - 1) * 0.5f);
        ReliefStyle style = ResolveStyle(seed, p);

        float a1 = Hash01(seed, 0x7A11) * Mathf.Tau;
        float a2 = Hash01(seed, 0x1B93) * Mathf.Tau;
        var axis = new Vector2(MathF.Cos(a1), MathF.Sin(a1));
        var p1 = centre + axis * radius * (0.30f + 0.20f * Hash01(seed, 0x44D2));
        var p2 = centre + new Vector2(MathF.Cos(a2), MathF.Sin(a2))
                          * radius * (0.30f + 0.25f * Hash01(seed, 0x6E05));

        var drift = new Noise(seed + 606, frequency: 0.02f, octaves: 3);

        var envelope = new float[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            var cell = new Vector2(x, z);

            float v = style switch
            {
                ReliefStyle.OffsetPeak => Dome(cell, p1, radius * 0.85f),
                ReliefStyle.TwinPeaks => MathF.Max(Dome(cell, p1, radius * 0.65f),
                                                   Dome(cell, p2, radius * 0.55f) * 0.85f),
                ReliefStyle.Ridge => Spine(cell, centre, axis, radius),
                ReliefStyle.Plateau => FieldOps.SmoothStep(1f, 0.55f, centre.DistanceTo(cell) / radius),
                ReliefStyle.Tilted => 0.18f + 0.82f * (0.5f + 0.5f * axis.Dot(cell - centre) / radius),
                _ => Dome(cell, centre, radius),
            };

            v = v * 0.7f + drift.At(x, z) * 0.3f;
            float taper = Mathf.Lerp(CoastLow, 1f,
                FieldOps.SmoothStep(0f, CoastTaperCells, toCoast[x, z]));
            envelope[x, z] = Math.Clamp(v, 0f, 1f) * taper;
        }
        return envelope;
    }

    private static float Dome(Vector2 cell, Vector2 c, float r)
    {
        float d = MathF.Min(1f, cell.DistanceTo(c) / MathF.Max(r, 1e-3f));
        return 1f - d * d;
    }

    /// <summary>
    /// A narrow spine running the length of the island. Narrow and long on
    /// purpose: with landform choice now keyed to the envelope, this is what
    /// turns into a mountain chain crossing the isle.
    /// </summary>
    private static float Spine(Vector2 cell, Vector2 c, Vector2 axis, float radius)
    {
        Vector2 rel = cell - c;
        float along = axis.Dot(rel);
        float perp = (rel - axis * along).Length();
        float flank = 1f - MathF.Min(1f, perp / (radius * 0.30f));
        float ends = 1f - MathF.Min(1f, MathF.Abs(along) / (radius * 1.45f));
        return flank * flank * ends;
    }

    // ---- Stage 2b: the patchwork ---------------------------------------------

    /// <summary>
    /// Jittered-grid Voronoi with a domain-warped lookup, split into connected
    /// components, then every component under <see cref="IslandParams.MinRegionArea"/>
    /// merged into the neighbour it shares the most border with. Without the
    /// merge, the coastline slices regions into slivers too small to read.
    /// </summary>
    private static int[,] BuildRegions(int seed, IslandParams p, bool[,] land, out int count)
    {
        int n = p.Size;
        int[,] raw = Partition(seed, p, land);

        var comp = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) comp[x, z] = -1;

        // Connected components of equal Voronoi id: one region must be one patch.
        var members = new List<List<(int X, int Z)>>();
        var stack = new Stack<(int X, int Z)>();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z] || comp[x, z] >= 0) continue;

            int id = members.Count;
            var cells = new List<(int X, int Z)>();
            members.Add(cells);
            int key = raw[x, z];

            comp[x, z] = id;
            stack.Push((x, z));
            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Pop();
                cells.Add((cx, cz));
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!land[nx, nz] || comp[nx, nz] >= 0 || raw[nx, nz] != key) continue;
                    comp[nx, nz] = id;
                    stack.Push((nx, nz));
                }
            }
        }

        int minArea = Math.Max(4, p.MinRegionArea);
        var locked = new bool[members.Count];       // isolated islets: nothing to merge into

        for (int guard = 0; guard < 4096; guard++)
        {
            int worst = -1;
            for (int i = 0; i < members.Count; i++)
            {
                if (locked[i] || members[i].Count == 0 || members[i].Count >= minArea) continue;
                if (worst < 0 || members[i].Count < members[worst].Count) worst = i;
            }
            if (worst < 0) break;

            var shared = new Dictionary<int, int>();
            foreach (var (x, z) in members[worst])
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!land[nx, nz]) continue;
                int other = comp[nx, nz];
                if (other == worst) continue;
                shared.TryGetValue(other, out int c);
                shared[other] = c + 1;
            }

            if (shared.Count == 0) { locked[worst] = true; continue; }

            int target = -1, bestShared = -1;
            foreach (var (other, c) in shared)
                if (c > bestShared || (c == bestShared && members[other].Count > members[target].Count))
                {
                    bestShared = c;
                    target = other;
                }

            foreach (var (x, z) in members[worst]) comp[x, z] = target;
            members[target].AddRange(members[worst]);
            members[worst].Clear();
        }

        // Re-index to a dense range.
        var remap = new int[members.Count];
        Array.Fill(remap, -1);
        count = 0;
        for (int i = 0; i < members.Count; i++)
            if (members[i].Count > 0) remap[i] = count++;

        var region = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            region[x, z] = land[x, z] ? remap[comp[x, z]] : -1;
        return region;
    }

    private static int[,] Partition(int seed, IslandParams p, bool[,] land)
    {
        int n = p.Size;
        int step = Math.Max(4, p.RegionScale);
        int cols = (n + step - 1) / step + 2;

        var sx = new float[cols, cols];
        var sz = new float[cols, cols];
        for (int i = 0; i < cols; i++)
        for (int j = 0; j < cols; j++)
        {
            uint key = (uint)i * 73856093u ^ (uint)j * 19349663u;
            sx[i, j] = (i - 0.5f + 0.2f + 0.6f * Hash01(seed, key)) * step;
            sz[i, j] = (j - 0.5f + 0.2f + 0.6f * Hash01(seed, key ^ 0x9E3779B9u)) * step;
        }

        var warpX = new Noise(seed + 707, frequency: 0.035f, octaves: 2);
        var warpZ = new Noise(seed + 808, frequency: 0.035f, octaves: 2);
        float warpAmp = step * 0.5f;

        var raw = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            raw[x, z] = -1;
            if (!land[x, z]) continue;

            float wx = x + (warpX.At(x, z) - 0.5f) * 2f * warpAmp;
            float wz = z + (warpZ.At(x, z) - 0.5f) * 2f * warpAmp;

            int gi = Math.Clamp((int)MathF.Floor(wx / step) + 1, 0, cols - 1);
            int gj = Math.Clamp((int)MathF.Floor(wz / step) + 1, 0, cols - 1);

            float best = float.MaxValue;
            int bi = gi, bj = gj;
            for (int di = -1; di <= 1; di++)
            for (int dj = -1; dj <= 1; dj++)
            {
                int i = gi + di, j = gj + dj;
                if (i < 0 || j < 0 || i >= cols || j >= cols) continue;
                float ddx = wx - sx[i, j], ddz = wz - sz[i, j];
                float d2 = ddx * ddx + ddz * ddz;
                if (d2 < best) { best = d2; bi = i; bj = j; }
            }
            raw[x, z] = bi * cols + bj;
        }
        return raw;
    }

    /// <summary>Border cells per unordered region pair, plus each region's neighbour set.</summary>
    private static Dictionary<long, List<(int X, int Z)>> BuildBorders(
        bool[,] land, int[,] region, int count, out HashSet<int>[] neighbours)
    {
        int n = land.GetLength(0);
        var borders = new Dictionary<long, List<(int X, int Z)>>();
        neighbours = new HashSet<int>[count];
        for (int i = 0; i < count; i++) neighbours[i] = new HashSet<int>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int a = region[x, z];
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!land[nx, nz]) continue;
                int b = region[nx, nz];
                if (b == a) continue;

                neighbours[a].Add(b);
                long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                if (!borders.TryGetValue(key, out var list))
                    borders[key] = list = new List<(int X, int Z)>();
                list.Add((x, z));
            }
        }
        return borders;
    }

    // ---- Stage 2c: what each region is ---------------------------------------

    private static float[] RegionEnvelope(bool[,] land, int[,] region, int count, float[,] envelope)
    {
        int n = land.GetLength(0);
        var sum = new float[count];
        var cells = new int[count];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int r = region[x, z];
            sum[r] += envelope[x, z];
            cells[r]++;
        }

        var env = new float[count];
        for (int r = 0; r < count; r++) env[r] = cells[r] > 0 ? sum[r] / cells[r] : 0f;
        return env;
    }

    /// <summary>The smallest value the field takes anywhere in each region.</summary>
    private static float[] RegionMin(bool[,] land, int[,] region, int count, float[,] field)
    {
        int n = land.GetLength(0);
        var min = new float[count];
        Array.Fill(min, float.MaxValue);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z]) min[region[x, z]] = MathF.Min(min[region[x, z]], field[x, z]);
        for (int r = 0; r < count; r++) if (min[r] == float.MaxValue) min[r] = 0f;
        return min;
    }

    /// <summary>Mean of a field over each region's cells.</summary>
    private static float[] RegionMean(bool[,] land, int[,] region, int count, float[,] field)
    {
        var sum = new float[count];
        var seen = new int[count];
        int n = land.GetLength(0);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int r = region[x, z];
            if (r < 0 || r >= count) continue;
            sum[r] += field[x, z];
            seen[r]++;
        }
        for (int r = 0; r < count; r++) if (seen[r] > 0) sum[r] /= seen[r];
        return sum;
    }

    private static int[] RegionCells(bool[,] land, int[,] region, int count)
    {
        int n = land.GetLength(0);
        var cells = new int[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z]) cells[region[x, z]]++;
        return cells;
    }

    /// <summary>
    /// Hands each region a <see cref="LandformType"/>.
    ///
    /// <b>By quota, not by dice.</b> Independent per-region draws over ten-odd
    /// regions have enormous variance: a <c>Highland</c> would come out with no
    /// mountains on one seed and with mountains but no hills on the next, which
    /// makes the character an unreliable promise. Instead the weights are turned
    /// into <i>counts</i>, every landform the character names is guaranteed at
    /// least one region, and the counts are then handed out by rank on the relief
    /// envelope — mountains to the high ground, basins to the low and inland,
    /// hills to what is left in the middle.
    ///
    /// Rank alone would band the island by elevation like a contour map, so the
    /// sort key carries a per-region jitter. The exception is a cordillera, where
    /// the band being contiguous is the whole point.
    /// </summary>
    private static LandformType[] AssignTypes(int seed, IslandParams p, bool[,] land, int[,] region,
                                              int count, float[,] envelope, float[,] toCoast)
    {
        float[] env = RegionEnvelope(land, region, count, envelope);
        float[] inland = RegionMean(land, region, count, toCoast);
        TerrainCharacter character = ResolveCharacter(seed, p.Character);
        float[] weights = MixedWeights(character, p.LandformMix);

        int[] quota = Apportion(weights, count);
        var type = new LandformType[count];
        for (int r = 0; r < count; r++) type[r] = LandformType.Plain;

        var free = new List<int>(count);
        for (int r = 0; r < count; r++) free.Add(r);

        // A range rather than a scatter of solitary peaks: taking the top band of
        // the envelope *without* jitter makes the chosen regions adjacent, and the
        // massif merge then welds them into one. Under a Ridge envelope that band
        // is a spine, so the chain crosses the isle.
        bool cordillera = quota[(int)LandformType.Mountain] > 1
                          && Hash01(seed, 0x2B7F) < (ResolveStyle(seed, p) == ReliefStyle.Ridge ? 0.9f : 0.55f);

        float Jitter(int r, uint salt, float amount)
            => (Hash01(seed, salt ^ (uint)r * 2654435761u) - 0.5f) * amount;

        void Take(LandformType t, Func<int, float> score)
        {
            int want = quota[(int)t];
            if (want <= 0) return;
            free.Sort((a, b) => score(b).CompareTo(score(a)));
            int take = Math.Min(want, free.Count);
            for (int i = 0; i < take; i++) type[free[i]] = t;
            free.RemoveRange(0, take);
        }

        // Highest ground first, lowest last; hills then fall out in the middle.
        Take(LandformType.Mountain, r => env[r] + (cordillera ? 0f : Jitter(r, 0xA1B2u, 0.30f)));
        Take(LandformType.Mesa, r => env[r] + Jitter(r, 0xC5D6u, 0.35f));
        // Basins want low ground that is also sheltered. The measure is the
        // region's *mean* distance from the void, not its minimum: almost every
        // patch touches the coast somewhere, so gating on the minimum is what
        // made basins all but extinct — the weight was multiplied by zero.
        Take(LandformType.Basin, r => -env[r] + 0.35f * FieldOps.SmoothStep(2f, 9f, inland[r])
                                      + Jitter(r, 0xE7F8u, 0.30f));
        Take(LandformType.Hills, r => env[r] + Jitter(r, 0x9AB4u, 0.40f));

        return type;
    }

    /// <summary>
    /// Turns landform shares into whole region counts (largest remainder), then
    /// guarantees that anything the character names actually appears — the point
    /// of the quota. The seats come out of the largest holding, which is plains.
    /// </summary>
    private static int[] Apportion(float[] weights, int count)
    {
        var quota = new int[weights.Length];
        if (count <= 0) return quota;

        float total = 0f;
        foreach (float w in weights) total += w;
        if (total <= 0f) { quota[(int)LandformType.Plain] = count; return quota; }

        var frac = new float[weights.Length];
        int given = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            float raw = weights[i] / total * count;
            quota[i] = (int)raw;
            frac[i] = raw - quota[i];
            given += quota[i];
        }

        for (; given < count; given++)
        {
            int best = 0;
            for (int i = 1; i < weights.Length; i++) if (frac[i] > frac[best]) best = i;
            quota[best]++;
            frac[best] = -1f;
        }

        // The guarantee. A character that names a landform gets one, as long as
        // there are enough regions to go round at all.
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0f || quota[i] > 0) continue;
            int donor = 0;
            for (int j = 1; j < weights.Length; j++) if (quota[j] > quota[donor]) donor = j;
            if (quota[donor] <= 1) break;                // nothing left to spare
            quota[donor]--;
            quota[i]++;
        }
        return quota;
    }

    /// <summary>
    /// The character's own balance, tilted by <c>LandformMix</c>. 0 pushes the
    /// island toward its low landforms (plains, and basins where it has them),
    /// 1 toward its high ones; 0.5 leaves the character as authored.
    /// </summary>
    private static float[] MixedWeights(TerrainCharacter c, float mix)
    {
        float[] w = (float[])TypeWeights(c).Clone();
        float t = (Math.Clamp(mix, 0f, 1f) - 0.5f) * 2f;        // -1 .. 1

        // How "high" each landform reads, which is what the mix slides along.
        // Basins sit with the plains: a sunken floor is low ground.
        ReadOnlySpan<float> rank = stackalloc float[] { -0.6f, 0.2f, 1f, 0.8f, -0.8f };
        for (int i = 0; i < w.Length; i++) w[i] *= MathF.Exp(t * 1.9f * rank[i]);
        return w;
    }

    /// <summary>
    /// Enforces the adjacency rules: a mesa may only touch plains. Where one
    /// abuts a mountain the mesa gives way — a massif is the larger feature —
    /// and any other neighbour is flattened to a plain, which is what puts the
    /// apron of open ground around a mesa that makes it read as one.
    /// </summary>
    private static bool IsTable(LandformType t)
        => t == LandformType.Mesa || t == LandformType.Basin;

    private static void RepairAdjacency(int[,] region, int count, HashSet<int>[] neighbours,
                                        LandformType[] type)
    {
        for (int r = 0; r < count; r++)
        {
            if (!IsTable(type[r])) continue;
            foreach (int nb in neighbours[r])
                if (type[nb] == LandformType.Mountain) { type[r] = LandformType.Plain; break; }
        }

        // A mesa or basin may touch plains, or more of its own kind — never the
        // other. A mesa raised five slabs beside a basin sunk five is a ten-slab
        // compound step neither landform asked for.
        for (int r = 0; r < count; r++)
        {
            if (!IsTable(type[r])) continue;
            foreach (int nb in neighbours[r])
                if (type[nb] != LandformType.Plain && type[nb] != type[r])
                    type[nb] = LandformType.Plain;
        }
    }

    /// <summary>
    /// The adjacency repair flattens whatever sits beside a mesa or basin, and
    /// that can take out the last region of a landform the character promised —
    /// a <c>Downs</c> island whose single hills patch happened to touch a basin
    /// came out as plains. The quota exists so a character means something, so
    /// put one back: the largest plain that touches no mesa or basin, which is
    /// exactly a region the repair would not object to.
    /// </summary>
    private static void RestoreMissingLandforms(IslandParams p, int seed, int[,] region, int count,
                                                HashSet<int>[] neighbours, LandformType[] type,
                                                int[] cells)
    {
        float[] weights = TypeWeights(ResolveCharacter(seed, p.Character));

        for (int t = 0; t < weights.Length; t++)
        {
            var want = (LandformType)t;
            if (weights[t] <= 0f || want == LandformType.Plain) continue;
            if (Array.IndexOf(type, want) >= 0) continue;

            int best = -1;
            for (int r = 0; r < count; r++)
            {
                if (type[r] != LandformType.Plain || cells[r] <= 0) continue;
                if (best >= 0 && cells[r] <= cells[best]) continue;

                // The restored region has to satisfy the adjacency rules on its
                // own, because nothing repairs them afterwards: a mesa or basin
                // may only touch plains, and nothing else may touch a mesa or
                // basin. Restoring blind is how a basin ends up beside a massif.
                bool clear = true;
                foreach (int nb in neighbours[r])
                {
                    bool ok = IsTable(want)
                        ? type[nb] == LandformType.Plain
                        : !IsTable(type[nb]);
                    if (!ok) { clear = false; break; }
                }
                if (clear) best = r;
            }
            if (best >= 0) type[best] = want;
        }
    }

    /// <summary>Unions neighbouring regions that share one of the given types.</summary>
    private static int[,] MergeAdjacentOfType(bool[,] land, int[,] region,
                                              HashSet<int>[] neighbours, ref int count,
                                              ref LandformType[] types)
    {
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = i;

        int Find(int a)
        {
            while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
            return a;
        }

        // Mountains only. Mesas are left separate so two of them can neighbour at
        // different heights — a stepped tableland, and one of the two borders
        // where a cliff is allowed.
        for (int r = 0; r < count; r++)
        {
            if (types[r] != LandformType.Mountain) continue;
            foreach (int nb in neighbours[r])
            {
                if (types[nb] != types[r]) continue;
                int a = Find(r), b = Find(nb);
                if (a != b) parent[b] = a;
            }
        }

        var rootId = new int[count];
        Array.Fill(rootId, -1);
        var mapped = new int[count];
        var merged = new List<LandformType>();

        for (int r = 0; r < count; r++)
        {
            int root = Find(r);
            if (rootId[root] < 0) { rootId[root] = merged.Count; merged.Add(types[root]); }
            mapped[r] = rootId[root];
        }

        int n = land.GetLength(0);
        var result = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            result[x, z] = land[x, z] ? mapped[region[x, z]] : -1;

        count = merged.Count;
        types = merged.ToArray();
        return result;
    }

    private static RegionPlan[] AssignPlateaus(int seed, IslandParams p, bool[,] land, int[,] region,
                                               int count, float[,] envelope,
                                               HashSet<int>[] neighbours, LandformType[] type)
    {
        float[] env = RegionEnvelope(land, region, count, envelope);
        var cells = RegionCells(land, region, count);
        int levels = Math.Max(1, p.PlateauLevels);
        float scale = ReliefScale(p);
        var plateau = new int[count];

        // A rung difference between two regions *is* a cliff, so the rule that
        // cliffs may only fall between two plains or two mesas is enforced here,
        // by making every other pair of neighbours share a rung. Union those
        // pairs and give each resulting group one rung.
        var parent = new int[count];
        for (int i = 0; i < count; i++) parent[i] = i;

        int Find(int a)
        {
            while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
            return a;
        }

        for (int r = 0; r < count; r++)
        foreach (int nb in neighbours[r])
        {
            bool cliffAllowed =
                (type[r] == LandformType.Plain && type[nb] == LandformType.Plain) ||
                (type[r] == LandformType.Mesa && type[nb] == LandformType.Mesa) ||
                (type[r] == LandformType.Basin && type[nb] == LandformType.Basin);
            if (cliffAllowed) continue;

            int a = Find(r), b = Find(nb);
            if (a != b) parent[b] = a;
        }

        var groupEnv = new float[count];
        var groupCells = new int[count];
        for (int r = 0; r < count; r++)
        {
            int g = Find(r);
            groupEnv[g] += env[r] * cells[r];
            groupCells[g] += cells[r];
        }

        for (int r = 0; r < count; r++)
        {
            int g = Find(r);
            float e = groupCells[g] > 0 ? groupEnv[g] / groupCells[g] : 0f;
            // A small nudge only: a large one makes groups disagree constantly,
            // and every disagreement is a cliff.
            float rung = e * levels
                         + (Hash01(seed, 0xC3D4u ^ (uint)g * 2654435761u) - 0.5f) * 0.5f;
            plateau[r] = Math.Clamp((int)MathF.Round(rung), 0, levels) * p.CliffHeight;
        }

        // Mesas stand clear above everything they touch. Assigned lowest-envelope
        // first, so a run of neighbouring mesas steps up one after another instead
        // of each measuring against an unassigned neighbour. MesaHeight is the
        // literal clearance over the neighbouring *surface*, relief included —
        // measuring against a rung alone would let a hill rise to meet the top.
        var mesas = new List<int>();
        for (int r = 0; r < count; r++) if (type[r] == LandformType.Mesa) mesas.Add(r);
        mesas.Sort((a, b) => env[a].CompareTo(env[b]));

        var placed = new bool[count];
        foreach (int r in mesas)
        {
            // The ground a mesa stands on and the mesas beside it are measured
            // separately. Lumping them together is what let a chain compound:
            // each mesa cleared the last one by a full MesaHeight, and five slabs
            // at a time a stepped tableland turns into a tower.
            int groundTop = int.MinValue;       // highest neighbour that is not a mesa
            int mesaTop = int.MinValue;         // highest mesa already raised
            foreach (int nb in neighbours[r])
            {
                if (type[nb] == LandformType.Mountain) continue;
                if (type[nb] == LandformType.Mesa)
                {
                    if (placed[nb]) mesaTop = Math.Max(mesaTop, plateau[nb]);
                    continue;
                }
                // Against the neighbour's *surface*, relief included — measuring
                // against its rung alone would let a hill rise to meet the top.
                groundTop = Math.Max(groundTop,
                    plateau[nb] + (int)MathF.Round(Amplitude(type[nb], p) * scale));
            }

            int step = Math.Max(3, p.MesaHeight);
            int level;
            if (groundTop != int.MinValue)
            {
                level = groundTop + step;
                // Still clear a neighbouring mesa, but by half a step — the
                // tableland is meant to read as terraced, not as a staircase of
                // full escarpments — and never more than two steps above the
                // plain the whole group stands on.
                if (mesaTop >= level) level = mesaTop + Math.Max(2, step / 2);
                level = Math.Min(level, groundTop + 2 * step);
            }
            else level = (mesaTop != int.MinValue ? mesaTop + Math.Max(2, step / 2)
                                                  : plateau[r] + step);

            plateau[r] = level;
            placed[r] = true;
        }

        // Basins are the same rule inverted, assigned highest-envelope first so a
        // run of them steps down one after another.
        var basins = new List<int>();
        for (int r = 0; r < count; r++) if (type[r] == LandformType.Basin) basins.Add(r);
        basins.Sort((a, b) => env[b].CompareTo(env[a]));

        var sunk = new bool[count];
        foreach (int r in basins)
        {
            int groundFloor = int.MaxValue;     // lowest neighbour that is not a basin
            int basinFloor = int.MaxValue;      // lowest basin already sunk
            foreach (int nb in neighbours[r])
            {
                if (type[nb] == LandformType.Mountain) continue;
                if (type[nb] == LandformType.Basin)
                {
                    if (sunk[nb]) basinFloor = Math.Min(basinFloor, plateau[nb]);
                    continue;
                }
                groundFloor = Math.Min(groundFloor, plateau[nb]);
            }

            int drop = Math.Max(3, p.BasinDepth);
            int level;
            if (groundFloor != int.MaxValue)
            {
                level = groundFloor - drop;
                if (basinFloor <= level) level = basinFloor - Math.Max(2, drop / 2);
                level = Math.Max(level, groundFloor - 2 * drop);
            }
            else level = (basinFloor != int.MaxValue ? basinFloor - Math.Max(2, drop / 2)
                                                     : plateau[r] - drop);

            plateau[r] = level;
            sunk[r] = true;
        }

        // Mountains take no rung: BuildSurface hangs them off the actual height of
        // the ground at their border. Giving them one put a step at the foot.
        var plan = new RegionPlan[count];
        for (int r = 0; r < count; r++) plan[r] = new RegionPlan(type[r], plateau[r], Find(r));
        return plan;
    }

    /// <summary>Normalised distance from each cell to its own region's border, in [0,1].</summary>
    private static float[,] InwardDistance(bool[,] land, int[,] region, int count)
    {
        int n = land.GetLength(0);
        var dist = new int[n, n];
        var q = new Queue<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            dist[x, z] = -1;
            if (!land[x, z]) continue;

            bool edge = false;
            for (int k = 0; k < 4 && !edge; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                edge = nx < 0 || nz < 0 || nx >= n || nz >= n
                       || !land[nx, nz] || region[nx, nz] != region[x, z];
            }
            if (edge) { dist[x, z] = 0; q.Enqueue((x, z)); }
        }

        while (q.Count > 0)
        {
            var (x, z) = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!land[nx, nz] || region[nx, nz] != region[x, z]) continue;
                if (dist[nx, nz] >= 0) continue;
                dist[nx, nz] = dist[x, z] + 1;
                q.Enqueue((nx, nz));
            }
        }

        var peak = new int[count];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && dist[x, z] > peak[region[x, z]]) peak[region[x, z]] = dist[x, z];

        var u = new float[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z]) u[x, z] = dist[x, z] / (float)Math.Max(1, peak[region[x, z]]);
        return u;
    }

    /// <summary>Relief amplitude in slabs for the region-fill landforms.</summary>
    /// <summary>
    /// Relief amplitude in slabs, before <see cref="ReliefScale"/>. Hills are the
    /// only landform with a knob of their own: at <c>Hilliness</c> 0 they are
    /// swells barely distinguishable from a plain, at 1 they are mounds. The
    /// slope limit stays 1 either way — a mound is taller and steeper-sided, not
    /// less walkable.
    /// </summary>
    private static float Amplitude(LandformType type, IslandParams p) => type switch
    {
        LandformType.Plain => 1.4f,
        LandformType.Hills => 3f + 12f * Math.Clamp(p.Hilliness, 0f, 1f),
        _ => 1.4f,          // mesa and basin floors are flat; mountains bypass this
    };

    /// <summary>Largest step allowed between neighbours inside a region.</summary>
    private static int SlopeLimit(LandformType type) => type switch
    {
        // Unbounded: the mountain's S-curve *is* its shape, and clamping it would
        // shave exactly the steep band the profile exists to produce.
        LandformType.Mountain => 1 << 20,
        _ => 1,
    };

    private static float ReliefScale(IslandParams p) => 0.4f + 1.3f * Math.Clamp(p.Relief, 0f, 1f);

    /// <summary>Plain / Hills / Mountain / Mesa / Basin weights. Zero means "never here".</summary>
    private static float[] TypeWeights(TerrainCharacter c) => c switch
    {
        TerrainCharacter.Plains => new[] { 1.00f, 0f, 0f, 0f, 0f },
        TerrainCharacter.Tableland => new[] { 0.56f, 0f, 0f, 0.24f, 0.20f },
        // A hollow among hills is a tarn, and it is the only place standing water
        // can collect — without one, three islands in four have no lake at all.
        TerrainCharacter.Downs => new[] { 0.42f, 0.48f, 0f, 0f, 0.10f },
        TerrainCharacter.Highland => new[] { 0.26f, 0.42f, 0.25f, 0f, 0.07f },
        _ => new[] { 1.00f, 0f, 0f, 0f, 0f },
    };

    private static LandformType PickWeighted(float[] w, float u)
    {
        float total = 0f;
        foreach (float v in w) total += v;
        if (total <= 0f) return LandformType.Plain;

        float pick = u * total;
        for (int i = 0; i < w.Length; i++)
        {
            pick -= w[i];
            if (pick <= 0f) return (LandformType)i;
        }
        return LandformType.Plain;
    }

    // ---- Stage 3: surface within regions --------------------------------------

    private static short[,] BuildSurface(int seed, IslandParams p, bool[,] land, int[,] region,
                                         RegionPlan[] plan, float[,] inward)
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
        float scale = ReliefScale(p);

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
            if (land[x, z]) amp[x, z] = Amplitude(plan[region[x, z]].Type, p) * scale;
        FieldOps.Blur(amp, land, passes: 6);

        // Pass 1 — everything that sits on a rung.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) { h[x, z] = IslandData.NoLand; continue; }
            RegionPlan rp = plan[region[x, z]];
            if (rp.Type == LandformType.Mountain) { isMountain[x, z] = true; continue; }

            float dw = 0.5f + 0.3f * hilly;
            float t = dw * detail.At(x, z) + (1f - dw) * coarse.At(x, z);
            h[x, z] = SlabClamp(rp.Plateau + t * amp[x, z]);
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
            h[x, z] = SlabClamp(foot[x, z] + p.MountainHeight * s + rugged);
        }
        return h;
    }

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
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) { atCoast = true; continue; }
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
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
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

    /// <summary>
    /// Projects each region's surface onto the largest field that never rises more
    /// than its slope limit between neighbours (a Lipschitz projection from above:
    /// it only lowers cells, so it converges). Region borders are excluded, which
    /// is what leaves the plateau gaps standing as cliffs.
    /// </summary>
    /// <summary>
    /// Whether the step between two regions is bound by the slope limit — that is,
    /// whether a cliff is forbidden here.
    ///
    /// Sharing a rung group <i>is</i> the statement "no cliff belongs on this
    /// border", so that is the test. Everything else is a cliff somebody asked
    /// for: two rung groups are the plateau ladder, a mesa or basin border is its
    /// own escarpment, and a mountain flank is the mountain.
    /// </summary>
    private static bool BorderIsBound(RegionPlan a, RegionPlan b)
    {
        if (a.Type == LandformType.Mountain || b.Type == LandformType.Mountain) return false;
        if (a.Type is LandformType.Mesa or LandformType.Basin) return false;
        if (b.Type is LandformType.Mesa or LandformType.Basin) return false;
        return a.RungGroup == b.RungGroup;
    }

    /// <summary>
    /// Lipschitz projection from above: repeatedly lower any cell standing more
    /// than its region's slope limit above a neighbour. It only ever lowers, so
    /// it converges.
    ///
    /// It reaches <b>across</b> a region border wherever <see cref="BorderIsBound"/>
    /// allows. Sharing a rung equalises a border's <i>base</i>, but a hills patch
    /// carries more relief than the plain beside it, and blurring the amplitude
    /// field narrows that gap without closing it — which is where the handful of
    /// hills cliffs the rules forbid were coming from. Enforcing the limit on the
    /// border itself closes it by construction rather than by tuning.
    ///
    /// Cells flagged in <paramref name="exempt"/> are neither lowered nor used as
    /// a bound. Two features need that: a lake bed sits three or four slabs under
    /// its own shore, and a canyon floor seven under its lip — take either as a
    /// bound and the limiter drags the whole rung group down into it a slab per
    /// cell, which is how plains ended up below the basins they border.
    /// </summary>
    /// <returns>Whether anything was lowered.</returns>
    private static bool LimitSlope(short[,] h, int[,] region, bool[,] land, RegionPlan[] plan,
                                   bool[,]? exempt = null)
    {
        int n = h.GetLength(0);
        bool any = false;
        for (int pass = 0; pass < 48; pass++)
        {
            bool changed = false;
            bool forward = (pass & 1) == 0;

            for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                int x = forward ? a : n - 1 - a;
                int z = forward ? b : n - 1 - b;
                if (!land[x, z]) continue;
                if (exempt != null && exempt[x, z]) continue;

                int r = region[x, z];
                if (plan[r].Type == LandformType.Mountain) continue;

                int limit = SlopeLimit(plan[r].Type);
                int cap = int.MaxValue;

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!land[nx, nz]) continue;
                    if (exempt != null && exempt[nx, nz]) continue;

                    int rn = region[nx, nz];
                    if (rn != r && !BorderIsBound(plan[r], plan[rn])) continue;
                    cap = Math.Min(cap, h[nx, nz] + limit);
                }

                if (cap != int.MaxValue && cap < h[x, z]) { h[x, z] = (short)cap; changed = true; }
            }
            if (!changed) break;
            any = true;
        }
        return any;
    }

    /// <summary>
    /// Removes two-slab steps outside mountains. Two is the worst height a step
    /// can be: too tall to walk, too short to read as a cliff, so it is neither
    /// free movement nor a deliberate obstacle.
    /// </summary>
    /// <returns>Whether anything was lowered.</returns>
    private static bool ResolveAmbiguousSteps(short[,] h, int[,] region, bool[,] land,
                                              RegionPlan[] plan, short[,]? water = null)
    {
        int n = h.GetLength(0);
        bool any = false;
        for (int pass = 0; pass < 16; pass++)
        {
            bool changed = false;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!land[x, z] || plan[region[x, z]].Type == LandformType.Mountain) continue;
                if (water != null && water[x, z] != IslandData.NoLand) continue;   // lake bed

                // A shore may not be lowered into its own lake.
                int keepAbove = int.MinValue;
                if (water != null)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        int wx = x + Dx[k], wz = z + Dz[k];
                        if (wx < 0 || wz < 0 || wx >= n || wz >= n) continue;
                        if (water[wx, wz] != IslandData.NoLand)
                            keepAbove = Math.Max(keepAbove, water[wx, wz] + 1);
                    }
                }

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!land[nx, nz] || plan[region[nx, nz]].Type == LandformType.Mountain) continue;

                    if (h[x, z] - h[nx, nz] == 2 && h[x, z] - 1 >= keepAbove)
                    {
                        h[x, z]--;
                        changed = true;
                    }
                }
            }
            if (!changed) break;
            any = true;
        }
        return any;
    }

    private static bool WantsCanyon(int seed, IslandParams p) => Hash01(seed, 0x4C17) < 0.20f;

    /// <summary>
    /// Cuts a trench along the border between two regions, preferring a border
    /// that is otherwise invisible — same landform, same rung. A canyon is a
    /// boundary made legible, so cutting one straight across a region would
    /// undo the very distinction the patchwork exists to draw.
    /// </summary>
    /// <summary>Returns the cells the trench actually took, or <c>null</c> if none was cut.</summary>
    private static bool[,]? CarveCanyon(int seed, IslandParams p, bool[,] land, int[,] region,
                                        RegionPlan[] plan, short[,] h,
                                        Dictionary<long, List<(int X, int Z)>> borders)
    {
        List<(int X, int Z)>? chosen = null;
        int bestScore = 0;

        foreach (var (key, cells) in borders)
        {
            if (cells.Count < 10) continue;
            int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);

            // Any pair of patches may be split by a canyon — unlike a cliff, which
            // is restricted to plain-plain and mesa-mesa. The exception is a mesa
            // or basin rim: that border is already an escarpment, so a trench adds
            // nothing there and only compounds the drop — a canyon cut along a
            // basin's edge leaves the plain outside it standing *below* the basin
            // floor, which reads as the escarpment pointing the wrong way.
            if (IsTable(plan[a].Type) || IsTable(plan[b].Type)) continue;

            int score = cells.Count;
            if (plan[a].Plateau == plan[b].Plateau) score *= 4;   // otherwise invisible
            if (plan[a].Type == plan[b].Type) score *= 2;
            if (score > bestScore) { bestScore = score; chosen = cells; }
        }
        if (chosen == null) return null;

        int n = p.Size;
        // The seed set already covers both sides of the border, so it is two cells
        // wide before the BFS grows it at all. A canyon is a crack, not a valley.
        int halfWidth = Hash01(seed, 0x3B71) < 0.7f ? 0 : 1;        // 2 or 4 cells across
        int depth = Math.Max(4, (int)MathF.Round(p.CliffHeight * 1.8f));

        var dist = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) dist[x, z] = -1;

        var q = new Queue<(int X, int Z)>();
        foreach (var (x, z) in chosen) { dist[x, z] = 0; q.Enqueue((x, z)); }

        while (q.Count > 0)
        {
            var (x, z) = q.Dequeue();
            if (dist[x, z] >= halfWidth) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!land[nx, nz] || dist[nx, nz] >= 0) continue;
                dist[nx, nz] = dist[x, z] + 1;
                q.Enqueue((nx, nz));
            }
        }

        var cut = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z] || dist[x, z] < 0) continue;
            // Stop at an escarpment. A trench cut alongside a basin rim drops the
            // plain *below* the basin floor, and the landform's whole read — a
            // hollow sunk into the ground around it — inverts. A canyon that ends
            // where it meets a cliff is what a canyon does anyway.
            if (TouchesTable(region, plan, land, x, z, n)) continue;
            h[x, z] = SlabClamp(h[x, z] - depth);
            cut[x, z] = true;
        }
        return cut;
    }

    /// <summary>Whether a cell is in, or borders, a mesa or basin.</summary>
    private static bool TouchesTable(int[,] region, RegionPlan[] plan, bool[,] land,
                                     int x, int z, int n)
    {
        if (IsTable(plan[region[x, z]].Type)) return true;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + Dx[k], nz = z + Dz[k];
            if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
            if (IsTable(plan[region[nx, nz]].Type)) return true;
        }
        return false;
    }

    // ---- Stage 4: keel / underside → one span per column -----------------

    /// <summary>
    /// Hangs the underside below the surface as a spinning top: a thin lip at the
    /// coastline descending inland to a deep keel.
    ///
    /// The underside is an <b>absolute</b> level, not a thickness subtracted from
    /// the surface — offsetting the surface would mirror its relief downwards and
    /// re-create a concave bottom under any high ground. A minimum-thickness clamp
    /// keeps every column solid.
    /// </summary>
    private static short[,] BuildKeel(int seed, IslandParams p, bool[,] land, short[,] surface,
                                      float[,] toCoast)
    {
        int n = p.Size;
        var crag = new Noise(seed + 404, frequency: 0.05f, octaves: 3);
        var sway = new Noise(seed + 505, frequency: 0.015f, octaves: 2);
        var warpX = new Noise(seed + 811, frequency: 0.028f, octaves: 3);
        var warpZ = new Noise(seed + 822, frequency: 0.028f, octaves: 3);

        // Displacing where the distance field is *sampled* bends its contours;
        // adding noise to the depth afterwards only ripples a shape that is still
        // a surface of revolution. Measured on a test island, warping roughly
        // quadruples the spread of keel depth within a radial band while leaving
        // the rim-to-centre trend untouched.
        float warpAmp = AutoRadius(p) * (0.25f + 0.45f * Math.Clamp(p.KeelRoughness, 0f, 1f));

        float maxCoast = 1f;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && toCoast[x, z] > maxCoast) maxCoast = toCoast[x, z];

        float scale = Math.Clamp(maxCoast / MathF.Max(3f, AutoRadius(p) * 0.75f), 0.25f, 1f);
        float edge = MathF.Max(1f, p.EdgeThickness);
        // The taper is a constant, not a knob: it shapes a surface the player
        // essentially never stands on, and every value in its old range read as
        // the same spinning top from above.
        const float taper = 0.85f;

        var keel = new short[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) { keel[x, z] = IslandData.NoLand; continue; }

            float wx = x + (warpX.At(x, z) - 0.5f) * 2f * warpAmp;
            float wz = z + (warpZ.At(x, z) - 0.5f) * 2f * warpAmp;
            float inland = FieldOps.Sample(toCoast, wx, wz);

            float t = Math.Clamp(inland / maxCoast * (0.72f + 0.56f * sway.At(x, z)), 0f, 1f);
            float depth = edge + p.KeelDepth * scale * MathF.Pow(t, taper);

            // Crag scales with depth: a ragged keel, a clean lip.
            depth += (crag.At(x, z) - 0.5f) * 2f * p.KeelRoughness * (2f + depth * 0.35f);

            int floorY = -Mathf.RoundToInt(MathF.Max(1f, depth));
            int k = Math.Min(floorY, surface[x, z] - (int)edge);          // keep columns solid
            keel[x, z] = SlabClamp(Math.Min(k, surface[x, z] - 1));
        }
        return keel;
    }

    /// <summary>
    /// Distance in cells from each land cell to the nearest non-land cell, as a
    /// smooth float field. A chamfer (3,4) transform approximates the Euclidean
    /// metric — plain 4-neighbour BFS is Manhattan, whose contours are diamonds —
    /// and a blur removes the integer steps.
    /// </summary>
    private static float[,] DistanceToCoast(bool[,] land)
    {
        int n = land.GetLength(0);
        const int Far = 1 << 20;
        var d = new int[n, n];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            d[x, z] = land[x, z] ? Far : 0;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            int best = d[x, z];
            best = Math.Min(best, Probe(d, n, x - 1, z, 3));
            best = Math.Min(best, Probe(d, n, x, z - 1, 3));
            best = Math.Min(best, Probe(d, n, x - 1, z - 1, 4));
            best = Math.Min(best, Probe(d, n, x + 1, z - 1, 4));
            d[x, z] = best;
        }
        for (int x = n - 1; x >= 0; x--)
        for (int z = n - 1; z >= 0; z--)
        {
            int best = d[x, z];
            best = Math.Min(best, Probe(d, n, x + 1, z, 3));
            best = Math.Min(best, Probe(d, n, x, z + 1, 3));
            best = Math.Min(best, Probe(d, n, x + 1, z + 1, 4));
            best = Math.Min(best, Probe(d, n, x - 1, z + 1, 4));
            d[x, z] = best;
        }

        var f = new float[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            f[x, z] = d[x, z] / 3f;

        FieldOps.Blur(f, land, passes: 3);
        return f;
    }

    private static int Probe(int[,] d, int n, int x, int z, int cost)
    {
        if (x < 0 || z < 0 || x >= n || z >= n) return int.MaxValue;
        int v = d[x, z];
        return v >= int.MaxValue - cost ? int.MaxValue : v + cost;
    }

    // ---- shared --------------------------------------------------------------

    private static float AutoRadius(IslandParams p)
        => p.Radius > 0f ? p.Radius : p.Size * 0.45f;

    /// <summary>
    /// The high-ground shape that suits a character. Plains want a gentle tilt or
    /// a broad flat; a Highland wants a spine or a pair of masses to hang its
    /// mountains on.
    /// </summary>
    private static ReliefStyle StyleFor(int seed, TerrainCharacter character)
    {
        ReliefStyle[] pool = character switch
        {
            TerrainCharacter.Plains => new[] { ReliefStyle.Tilted, ReliefStyle.Plateau },
            TerrainCharacter.Tableland => new[]
                { ReliefStyle.Plateau, ReliefStyle.CentralPeak, ReliefStyle.Tilted },
            TerrainCharacter.Downs => new[]
                { ReliefStyle.OffsetPeak, ReliefStyle.TwinPeaks, ReliefStyle.Tilted },
            _ => new[] { ReliefStyle.Ridge, ReliefStyle.TwinPeaks, ReliefStyle.OffsetPeak },
        };
        return pool[(int)(Hash(seed, 0x5EED) % (uint)pool.Length)];
    }

    private static ReliefStyle ResolveStyle(int seed, IslandParams p)
        => StyleFor(seed, ResolveCharacter(seed, p.Character));

    private static TerrainCharacter ResolveCharacter(int seed, TerrainCharacter requested)
        => requested != TerrainCharacter.Auto
            ? requested
            : (TerrainCharacter)(1 + (int)(Hash(seed, 0xC7A2) % 4u));

    /// <summary>Deterministic per-island scalar in <c>[0, 1)</c> for a given salt.</summary>
    private static float Hash01(int seed, uint salt) => (Hash(seed, salt) & 0xFFFFFF) / 16777216f;

    private static uint Hash(int seed, uint salt)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u ^ salt * 2246822519u;
            h ^= h >> 15; h *= 2246822519u;
            h ^= h >> 13; h *= 3266489917u;
            h ^= h >> 16;
            return h;
        }
    }

    private static short SlabClamp(float level)
        => (short)Math.Clamp((int)MathF.Round(level), short.MinValue + 1, short.MaxValue);

    private static short SlabClamp(int level)
        => (short)Math.Clamp(level, short.MinValue + 1, short.MaxValue);
}
