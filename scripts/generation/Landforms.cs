using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// What each region is — landform quotas, adjacency rules, plateau rungs — and the
/// per-landform tables the surface stages read.
/// </summary>
internal static class Landforms
{
    /// <summary>
    /// Disjoint sets over region ids. <see cref="Union"/> hangs b's root under a's, and
    /// the surviving root id is both a hash salt and <see cref="RegionPlan.RungGroup"/>,
    /// so the direction and the call order are part of the output.
    /// </summary>
    private sealed class UnionFind
    {
        private readonly int[] parent;

        public UnionFind(int count)
        {
            parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
        }

        /// <summary>The set's root, halving the path on the way up.</summary>
        public int Find(int a)
        {
            while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
            return a;
        }

        public void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) parent[rb] = ra;
        }
    }

    /// <summary>
    /// Hands each region a <see cref="LandformType"/> by quota, not by dice: the character's
    /// weights become counts, every landform it names gets at least one region, and the counts go
    /// out by rank on the relief envelope with a per-region jitter — none for a cordillera.
    /// </summary>
    internal static LandformType[] AssignTypes(int seed, IslandParams p, bool[,] land, int[,] region,
                                              int count, float[,] envelope, float[,] toCoast)
    {
        float[] env = Regions.RegionMean(land, region, count, envelope);
        float[] inland = Regions.RegionMean(land, region, count, toCoast);
        TerrainCharacter character = Roster.ResolveCharacter(seed, p);
        float[] weights = MixedWeights(character, p.LandformMix);

        int[] quota = Apportion(weights, count);
        var type = new LandformType[count];
        for (int r = 0; r < count; r++) type[r] = LandformType.Plain;

        var free = new List<int>(count);
        for (int r = 0; r < count; r++) free.Add(r);

        // Without jitter the top band is contiguous, and MergeAdjacentOfType welds it into
        // one range.
        bool cordillera = quota[(int)LandformType.Mountain] > 1
                          && Hash01(seed, 0x2B7F) < (Roster.ResolveStyle(seed, p) == ReliefStyle.Ridge ? 0.9f : 0.55f);

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

        // Highest ground first, lowest last; each Take sorts what the last one left.
        Take(LandformType.Mountain, r => env[r] + (cordillera ? 0f : Jitter(r, 0xA1B2u, 0.30f)));
        Take(LandformType.Massif, r => env[r] + Jitter(r, 0xD3A9u, 0.25f));
        Take(LandformType.Mesa, r => env[r] + Jitter(r, 0xC5D6u, 0.35f));
        Take(LandformType.Karst, r => env[r] + Jitter(r, 0xB4E2u, 0.40f));
        Take(LandformType.Badlands, r => env[r] + Jitter(r, 0xF10Cu, 0.40f));
        Take(LandformType.Sinkholes, r => -env[r] + Jitter(r, 0x77B1u, 0.45f));
        // Basins: low and sheltered — the mean rim distance, not the minimum, since almost
        // every patch touches the coast somewhere.
        Take(LandformType.Basin, r => -env[r] + 0.35f * FieldOps.SmoothStep(2f, 9f, inland[r])
                                      + Jitter(r, 0xE7F8u, 0.30f));
        Take(LandformType.Hills, r => env[r] + Jitter(r, 0x9AB4u, 0.40f));
        Take(LandformType.Dunes, r => -env[r] + Jitter(r, 0x5C3Du, 0.40f));

        return type;
    }

    /// <summary>
    /// Largest-remainder apportionment of the weights into <paramref name="count"/> regions;
    /// then every landform with weight but no seat takes one from the largest holder, as
    /// long as that holder keeps more than one.
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

        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] <= 0f || quota[i] > 0) continue;
            int donor = 0;
            for (int j = 1; j < weights.Length; j++) if (quota[j] > quota[donor]) donor = j;
            if (quota[donor] <= 1) break;
            quota[donor]--;
            quota[i]++;
        }
        return quota;
    }

    /// <summary>
    /// The character's weights tilted by <c>LandformMix</c>: 0 toward its low landforms,
    /// 1 toward its high ones, 0.5 as authored.
    /// </summary>
    private static float[] MixedWeights(TerrainCharacter c, float mix)
    {
        float[] w = (float[])TypeWeights(c).Clone();
        float t = (Math.Clamp(mix, 0f, 1f) - 0.5f) * 2f;        // -1 .. 1

        // How high each landform reads, indexed by LandformType; a basin is low ground.
        ReadOnlySpan<float> rank = stackalloc float[]
            { -0.6f, 0.2f, 1f, 0.8f, -0.8f, 0.3f, 0.5f, 0.95f, 0f, -0.2f };
        for (int i = 0; i < w.Length; i++) w[i] *= MathF.Exp(t * 1.9f * rank[i]);
        return w;
    }

    /// <summary>
    /// Mesa or basin: the landforms that take a level of their own and may only touch
    /// plains or their own kind.
    /// </summary>
    internal static bool IsTable(LandformType t)
        => t == LandformType.Mesa || t == LandformType.Basin;

    /// <summary>
    /// The table rules, in two ordered passes over <paramref name="type"/>: a mesa or basin
    /// beside a mountain gives way and becomes a plain; then every neighbour of a table that is
    /// neither a plain nor its own kind is flattened. In place, r ascending — order-dependent.
    /// </summary>
    internal static void RepairAdjacency(int[,] region, int count, HashSet<int>[] neighbours,
                                        LandformType[] type)
    {
        for (int r = 0; r < count; r++)
        {
            if (!IsTable(type[r])) continue;
            foreach (int nb in neighbours[r])
                if (type[nb] == LandformType.Mountain) { type[r] = LandformType.Plain; break; }
        }

        for (int r = 0; r < count; r++)
        {
            if (!IsTable(type[r])) continue;
            foreach (int nb in neighbours[r])
                if (type[nb] != LandformType.Plain && type[nb] != type[r])
                    type[nb] = LandformType.Plain;
        }
    }

    /// <summary>
    /// <see cref="RepairAdjacency"/> can delete the last region of a promised landform; put one
    /// back on the largest plain whose neighbours already satisfy the adjacency rules, since nothing
    /// repairs them afterwards. Bridgeheads last: flattened on purpose, but the quota outranks them.
    /// </summary>
    internal static void RestoreMissingLandforms(IslandParams p, int seed, int[,] region, int count,
                                                HashSet<int>[] neighbours, LandformType[] type,
                                                int[] cells, HashSet<int> bridgeheads)
    {
        float[] weights = TypeWeights(Roster.ResolveCharacter(seed, p));

        for (int t = 0; t < weights.Length; t++)
        {
            var want = (LandformType)t;
            if (weights[t] <= 0f || want == LandformType.Plain) continue;
            if (Array.IndexOf(type, want) >= 0) continue;

            int best = Candidate(r => !bridgeheads.Contains(r));
            if (best < 0) best = Candidate(_ => true);
            if (best >= 0) type[best] = want;

            int Candidate(Func<int, bool> allowed)
            {
                int found = -1;
                for (int r = 0; r < count; r++)
                {
                    if (type[r] != LandformType.Plain || cells[r] <= 0) continue;
                    if (found >= 0 && cells[r] <= cells[found]) continue;
                    if (!allowed(r)) continue;

                    bool clear = true;
                    foreach (int nb in neighbours[r])
                    {
                        bool ok = IsTable(want)
                            ? type[nb] == LandformType.Plain
                            : !IsTable(type[nb]);
                        if (!ok) { clear = false; break; }
                    }
                    if (clear) found = r;
                }
                return found;
            }
        }
    }

    /// <summary>
    /// Merges adjacent Mountain regions into one and renumbers by first-encountered root.
    /// Mountains only: mesas stay separate so two can neighbour at different heights.
    /// </summary>
    internal static int[,] MergeAdjacentOfType(bool[,] land, int[,] region,
                                              HashSet<int>[] neighbours, ref int count,
                                              ref LandformType[] types)
    {
        var sets = new UnionFind(count);
        for (int r = 0; r < count; r++)
        {
            if (types[r] != LandformType.Mountain) continue;
            foreach (int nb in neighbours[r])
            {
                if (types[nb] != types[r]) continue;
                sets.Union(r, nb);
            }
        }

        var rootId = new int[count];
        Array.Fill(rootId, -1);
        var mapped = new int[count];
        var merged = new List<LandformType>();

        for (int r = 0; r < count; r++)
        {
            int root = sets.Find(r);
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

    /// <summary>
    /// Hands each region a plateau level: neighbours that may not have a cliff between them (and
    /// both ends of every bridge) share a rung group, each group takes one rung off its mean
    /// envelope, then mesas are raised above and basins sunk below what they touch.
    /// </summary>
    internal static RegionPlan[] AssignPlateaus(int seed, IslandParams p, bool[,] land, int[,] region,
                                               int count, float[,] envelope,
                                               HashSet<int>[] neighbours, LandformType[] type,
                                               List<(Vector2I A, Vector2I B)> bridges)
    {
        float[] env = Regions.RegionMean(land, region, count, envelope);
        var cells = Regions.RegionCells(land, region, count);
        int levels = Math.Max(1, p.PlateauLevels);
        float scale = ReliefScale(p);

        UnionFind groups = BuildRungGroups(land, region, count, neighbours, type, bridges);
        int[] plateau = AssignRungs(seed, p, count, levels, env, cells, groups);
        RaiseMesas(p, count, scale, env, neighbours, type, plateau);
        SinkBasins(p, count, env, neighbours, type, plateau);

        // Mountains take no rung: the surface hangs them off the ground at their border.
        var plan = new RegionPlan[count];
        for (int r = 0; r < count; r++) plan[r] = new RegionPlan(type[r], plateau[r], groups.Find(r));
        return plan;
    }

    /// <summary>
    /// A rung difference between neighbours is a cliff, and a cliff may only fall between two
    /// plains, two mesas or two basins: every other pair is unioned into one rung group. So are
    /// the two ends of a bridge — nothing else would make banks with aether between them agree.
    /// </summary>
    private static UnionFind BuildRungGroups(bool[,] land, int[,] region, int count,
                                             HashSet<int>[] neighbours, LandformType[] type,
                                             List<(Vector2I A, Vector2I B)> bridges)
    {
        var groups = new UnionFind(count);

        for (int r = 0; r < count; r++)
        foreach (int nb in neighbours[r])
        {
            bool cliffAllowed =
                (type[r] == LandformType.Plain && type[nb] == LandformType.Plain) ||
                (type[r] == LandformType.Mesa && type[nb] == LandformType.Mesa) ||
                (type[r] == LandformType.Basin && type[nb] == LandformType.Basin);
            if (cliffAllowed) continue;

            groups.Union(r, nb);
        }

        foreach (var (ca, cb) in bridges)
        {
            if (!land[ca.X, ca.Y] || !land[cb.X, cb.Y]) continue;
            groups.Union(region[ca.X, ca.Y], region[cb.X, cb.Y]);
        }
        return groups;
    }

    /// <summary>
    /// One rung per group off its cell-weighted mean envelope, nudged by a small roll on the
    /// group's root id — a large nudge makes groups disagree, and every disagreement is a cliff.
    /// </summary>
    private static int[] AssignRungs(int seed, IslandParams p, int count, int levels,
                                     float[] env, int[] cells, UnionFind groups)
    {
        var groupEnv = new float[count];
        var groupCells = new int[count];
        for (int r = 0; r < count; r++)
        {
            int g = groups.Find(r);
            groupEnv[g] += env[r] * cells[r];
            groupCells[g] += cells[r];
        }

        var plateau = new int[count];
        for (int r = 0; r < count; r++)
        {
            int g = groups.Find(r);
            float e = groupCells[g] > 0 ? groupEnv[g] / groupCells[g] : 0f;
            float rung = e * levels
                         + (Hash01(seed, 0xC3D4u ^ (uint)g * 2654435761u) - 0.5f) * 0.5f;
            plateau[r] = Math.Clamp((int)MathF.Round(rung), 0, levels) * p.CliffHeight;
        }
        return plateau;
    }

    /// <summary>
    /// Raises each mesa <c>MesaHeight</c> above the highest neighbouring surface, relief included
    /// (a rung alone would let a hill rise to meet the top), lowest envelope first so a run of
    /// mesas steps up in turn.
    /// </summary>
    private static void RaiseMesas(IslandParams p, int count, float scale, float[] env,
                                   HashSet<int>[] neighbours, LandformType[] type, int[] plateau)
    {
        var mesas = new List<int>();
        for (int r = 0; r < count; r++) if (type[r] == LandformType.Mesa) mesas.Add(r);
        mesas.Sort((a, b) => env[a].CompareTo(env[b]));

        var placed = new bool[count];
        foreach (int r in mesas)
        {
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
                groundTop = Math.Max(groundTop,
                    plateau[nb] + (int)MathF.Round(Amplitude(type[nb], p) * scale));
            }

            int step = Math.Max(3, p.MesaHeight);
            int level;
            if (groundTop != int.MinValue)
            {
                level = groundTop + step;
                // Half a step over a placed mesa, and never more than two steps above the ground.
                if (mesaTop >= level) level = mesaTop + Math.Max(2, step / 2);
                level = Math.Min(level, groundTop + 2 * step);
            }
            else level = (mesaTop != int.MinValue ? mesaTop + Math.Max(2, step / 2)
                                                  : plateau[r] + step);

            plateau[r] = level;
            placed[r] = true;
        }
    }

    /// <summary>
    /// <see cref="RaiseMesas"/> inverted: each basin sunk <c>BasinDepth</c> below its lowest
    /// neighbouring rung, highest envelope first so a run of basins steps down in turn.
    /// </summary>
    private static void SinkBasins(IslandParams p, int count, float[] env,
                                   HashSet<int>[] neighbours, LandformType[] type, int[] plateau)
    {
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
    }

    /// <summary>
    /// Relief amplitude in slabs, before <see cref="ReliefScale"/>. Hills and dunes are the
    /// only landforms with a knob: <c>Hilliness</c> scales their height, never their slope
    /// limit. Mountains bypass this.
    /// </summary>
    internal static float Amplitude(LandformType type, IslandParams p) => type switch
    {
        LandformType.Plain => 1.4f,
        LandformType.Hills => 3f + 12f * Math.Clamp(p.Hilliness, 0f, 1f),
        LandformType.Dunes => 3f + 6f * Math.Clamp(p.Hilliness, 0f, 1f),
        LandformType.Badlands => 2.2f,          // a little relief on top of each finger
        LandformType.Karst => 1.4f,
        LandformType.Massif => 0f,              // the terraces are the shape; flat like a mesa
        LandformType.Sinkholes => 1.4f,
        _ => 1.4f,                              // mesa and basin floors are flat
    };

    /// <summary>Largest step allowed between neighbours inside a region.</summary>
    internal static int SlopeLimit(LandformType type) => type switch
    {
        // Unbounded: the mountain's S-curve is its shape, and a clamp would shave the steep band.
        LandformType.Mountain => 1 << 20,
        _ => 1,
    };

    /// <summary>Multiplier on every landform's amplitude, from the <c>Relief</c> knob.</summary>
    internal static float ReliefScale(IslandParams p) => 0.4f + 1.3f * Math.Clamp(p.Relief, 0f, 1f);

    /// <summary>
    /// Landform weights per character, indexed by <see cref="LandformType"/>; 0 means never here.
    /// </summary>
    private static float[] TypeWeights(TerrainCharacter c) => c switch
    {
        TerrainCharacter.Plains => new[] { 1.00f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },
        TerrainCharacter.Tablelands => new[] { 0.56f, 0f, 0f, 0.24f, 0.20f, 0f, 0f, 0f, 0f, 0f },
        // The basin is a tarn — the only place standing water collects among hills.
        TerrainCharacter.Downs => new[] { 0.42f, 0.48f, 0f, 0f, 0.10f, 0f, 0f, 0f, 0f, 0f },
        TerrainCharacter.Highlands => new[] { 0.26f, 0.42f, 0.25f, 0f, 0.07f, 0f, 0f, 0f, 0f, 0f },
        TerrainCharacter.Badlands => new[] { 0.40f, 0f, 0f, 0.16f, 0f, 0.44f, 0f, 0f, 0f, 0f },
        // Towers and dolines are the same limestone read from two sides, so karst gets both.
        TerrainCharacter.Karst => new[] { 0.30f, 0.14f, 0f, 0f, 0.04f, 0f, 0.30f, 0f, 0f, 0.22f },
        TerrainCharacter.Massif => new[] { 0.28f, 0.24f, 0.16f, 0f, 0f, 0f, 0f, 0.32f, 0f, 0f },
        TerrainCharacter.Dunes => new[] { 0.44f, 0.10f, 0f, 0f, 0.04f, 0f, 0f, 0f, 0.42f, 0f },
        _ => new[] { 1.00f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },
    };

    /// <summary>
    /// The landforms cut into a settled surface rather than generated as relief. They carry
    /// cliffs inside a patch, so anything that flattens a region must treat them like a mountain.
    /// </summary>
    internal static bool IsSculpted(LandformType t)
        => t is LandformType.Badlands or LandformType.Karst or LandformType.Massif
             or LandformType.Sinkholes;
}
