using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>Stage 2c: what each region is — landform quotas, adjacency rules, plateau rungs — and the per-landform tables.</summary>
internal static class Landforms
{
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

        // A range rather than a scatter of solitary peaks: taking the top band of
        // the envelope *without* jitter makes the chosen regions adjacent, and the
        // massif merge then welds them into one. Under a Ridge envelope that band
        // is a spine, so the chain crosses the isle.
        bool cordillera = quota[(int)LandformType.Mountain] > 1
                          && TerrainHash01(seed, 0x2B7F) < (Roster.ResolveStyle(seed, p) == ReliefStyle.Ridge ? 0.9f : 0.55f);

        float Jitter(int r, uint salt, float amount)
            => (TerrainHash01(seed, salt ^ (uint)r * 2654435761u) - 0.5f) * amount;

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
        // A stepped massif belongs with the mountains: high ground, and adjacent
        // ones weld into one so the terraces run round the whole thing.
        Take(LandformType.Massif, r => env[r] + Jitter(r, 0xD3A9u, 0.25f));
        Take(LandformType.Mesa, r => env[r] + Jitter(r, 0xC5D6u, 0.35f));
        // Karst stands on middling ground and badlands on the tableland above the
        // plains, which is where both weather out of in the first place.
        Take(LandformType.Karst, r => env[r] + Jitter(r, 0xB4E2u, 0.40f));
        Take(LandformType.Badlands, r => env[r] + Jitter(r, 0xF10Cu, 0.40f));
        // A sinkhole field takes the low open country the water drained into.
        Take(LandformType.Sinkholes, r => -env[r] + Jitter(r, 0x77B1u, 0.45f));
        // Basins want low ground that is also sheltered. The measure is the
        // region's *mean* distance from the void, not its minimum: almost every
        // patch touches the coast somewhere, so gating on the minimum is what
        // made basins all but extinct — the weight was multiplied by zero.
        Take(LandformType.Basin, r => -env[r] + 0.35f * FieldOps.SmoothStep(2f, 9f, inland[r])
                                      + Jitter(r, 0xE7F8u, 0.30f));
        Take(LandformType.Hills, r => env[r] + Jitter(r, 0x9AB4u, 0.40f));
        // Dunes take what is left of the low ground: a dune field is what a plain
        // becomes where nothing else is happening to it.
        Take(LandformType.Dunes, r => -env[r] + Jitter(r, 0x5C3Du, 0.40f));

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
        ReadOnlySpan<float> rank = stackalloc float[]
            { -0.6f, 0.2f, 1f, 0.8f, -0.8f, 0.3f, 0.5f, 0.95f, 0f, -0.2f };
        for (int i = 0; i < w.Length; i++) w[i] *= MathF.Exp(t * 1.9f * rank[i]);
        return w;
    }

    /// <summary>
    /// Enforces the adjacency rules: a mesa may only touch plains. Where one
    /// abuts a mountain the mesa gives way — a massif is the larger feature —
    /// and any other neighbour is flattened to a plain, which is what puts the
    /// apron of open ground around a mesa that makes it read as one.
    /// </summary>
    internal static bool IsTable(LandformType t)
        => t == LandformType.Mesa || t == LandformType.Basin;

    internal static void RepairAdjacency(int[,] region, int count, HashSet<int>[] neighbours,
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

            // Two passes: any patch but a bridgehead first, and only then a
            // bridgehead. Those were made plains on purpose — a mesa or a mountain
            // takes its own level regardless of the rung its bank agreed with the
            // far side, so handing one the island's missing mountain puts a
            // bridgehead twelve slabs above the islet it is supposed to reach. But
            // the quota comes first: a Highland with no mountain on it is a worse
            // island than one with an awkward crossing, and the crossing is only
            // awkward when there was nowhere else to put the massif.
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

                    // The restored region has to satisfy the adjacency rules on
                    // its own, because nothing repairs them afterwards: a mesa or
                    // basin may only touch plains, and nothing else may touch a
                    // mesa or basin. Restoring blind is how a basin ends up beside
                    // a massif.
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

    /// <summary>Unions neighbouring regions that share one of the given types.</summary>
    internal static int[,] MergeAdjacentOfType(bool[,] land, int[,] region,
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

    internal static RegionPlan[] AssignPlateaus(int seed, IslandParams p, bool[,] land, int[,] region,
                                               int count, float[,] envelope,
                                               HashSet<int>[] neighbours, LandformType[] type,
                                               List<(Vector2I A, Vector2I B)> bridges)
    {
        float[] env = Regions.RegionMean(land, region, count, envelope);
        var cells = Regions.RegionCells(land, region, count);
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

        // The two ends of a bridge share a rung as well. They are not neighbours —
        // there is aether between them — so nothing else would make them agree,
        // and a crossing whose far bank stands eight slabs higher is not a
        // crossing. This is the same mechanism the cliff rule uses, pointed at a
        // gap instead of a border.
        foreach (var (ca, cb) in bridges)
        {
            if (!land[ca.X, ca.Y] || !land[cb.X, cb.Y]) continue;
            int a = Find(region[ca.X, ca.Y]), b = Find(region[cb.X, cb.Y]);
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
                         + (TerrainHash01(seed, 0xC3D4u ^ (uint)g * 2654435761u) - 0.5f) * 0.5f;
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

    /// <summary>Relief amplitude in slabs for the region-fill landforms.</summary>
    /// <summary>
    /// Relief amplitude in slabs, before <see cref="ReliefScale"/>. Hills are the
    /// only landform with a knob of their own: at <c>Hilliness</c> 0 they are
    /// swells barely distinguishable from a plain, at 1 they are mounds. The
    /// slope limit stays 1 either way — a mound is taller and steeper-sided, not
    /// less walkable.
    /// </summary>
    internal static float Amplitude(LandformType type, IslandParams p) => type switch
    {
        LandformType.Plain => 1.4f,
        LandformType.Hills => 3f + 12f * Math.Clamp(p.Hilliness, 0f, 1f),
        // Dunes are hills with a grain: the same one-slab grammar, less height,
        // and a wavelength that only runs one way (see BuildSurface).
        LandformType.Dunes => 3f + 6f * Math.Clamp(p.Hilliness, 0f, 1f),
        // A badlands finger has a little relief on top of it; a karst floor and a
        // ziggurat terrace are as flat as a mesa, because the shape is the cut.
        LandformType.Badlands => 2.2f,
        LandformType.Karst => 1.4f,
        LandformType.Massif => 0f,
        // A crater's apron is flat ground; a sinkhole field is a plain with holes.
        LandformType.Sinkholes => 1.4f,
        _ => 1.4f,          // mesa and basin floors are flat; mountains bypass this
    };

    /// <summary>Largest step allowed between neighbours inside a region.</summary>
    internal static int SlopeLimit(LandformType type) => type switch
    {
        // Unbounded: the mountain's S-curve *is* its shape, and clamping it would
        // shave exactly the steep band the profile exists to produce.
        LandformType.Mountain => 1 << 20,
        _ => 1,
    };

    internal static float ReliefScale(IslandParams p) => 0.4f + 1.3f * Math.Clamp(p.Relief, 0f, 1f);

    /// <summary>
    /// Landform weights per character, indexed by <see cref="LandformType"/>:
    /// plain / hills / mountain / mesa / basin / badlands / karst / ziggurat /
    /// dunes. Zero means "never here".
    /// </summary>
    private static float[] TypeWeights(TerrainCharacter c) => c switch
    {
        TerrainCharacter.Plains => new[] { 1.00f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },
        TerrainCharacter.Tablelands => new[] { 0.56f, 0f, 0f, 0.24f, 0.20f, 0f, 0f, 0f, 0f, 0f },
        // A hollow among hills is a tarn, and it is the only place standing water
        // can collect — without one, three islands in four have no lake at all.
        TerrainCharacter.Downs => new[] { 0.42f, 0.48f, 0f, 0f, 0.10f, 0f, 0f, 0f, 0f, 0f },
        TerrainCharacter.Highlands => new[] { 0.26f, 0.42f, 0.25f, 0f, 0.07f, 0f, 0f, 0f, 0f, 0f },
        // Eroded country: fingers of tableland with gullies between them, and the
        // mesas they weathered out of still standing.
        TerrainCharacter.Badlands => new[] { 0.40f, 0f, 0f, 0.16f, 0f, 0.44f, 0f, 0f, 0f, 0f },
        // Towers and dolines are the same limestone read from two sides, so a
        // karst Domain gets both: the ground you cannot climb and the ground you
        // cross watching your feet.
        TerrainCharacter.Karst => new[] { 0.30f, 0.14f, 0f, 0f, 0.04f, 0f, 0.30f, 0f, 0f, 0.22f },
        TerrainCharacter.Massif => new[] { 0.28f, 0.24f, 0.16f, 0f, 0f, 0f, 0f, 0.32f, 0f, 0f },
        TerrainCharacter.Dunes => new[] { 0.44f, 0.10f, 0f, 0f, 0.04f, 0f, 0f, 0f, 0.42f, 0f },
        _ => new[] { 1.00f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f },
    };

    /// <summary>
    /// The landforms whose shape is cut or raised into a finished plain rather
    /// than generated as relief — see <see cref="LandformType.Badlands"/>. They
    /// carry cliffs <i>inside</i> a patch, so anything that flattens a region for
    /// a reason (a bridgehead) has to treat them like a mountain.
    /// </summary>
    internal static bool IsSculpted(LandformType t)
        => t is LandformType.Badlands or LandformType.Karst or LandformType.Massif
             or LandformType.Sinkholes;
}
