using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

/// <summary>
/// Measures the <b>real</b> generator over many seeds and prints a report, so the
/// guarantees in docs/island-generation.md can be checked rather than asserted.
///
/// Run it headless — no rendering needed, it only reads <see cref="IslandData"/>:
/// <code>
/// godot --path . --headless --quit-after 2 scenes/dev/generation_audit.tscn
/// </code>
///
/// This exists because the numbers in the spec were originally produced by a
/// stand-alone harness that re-implemented the pipeline against substitute noise
/// (FastNoiseLite needs the engine). That validated the architecture but not the
/// shipped output. Measuring <c>IslandData</c> directly needs no re-implementation
/// at all, so there is nothing to drift out of sync.
/// </summary>
public partial class GenerationAudit : Node
{
    [Export] public int Seeds { get; set; } = 60;
    [Export] public int FirstSeed { get; set; } = 5000;
    [Export] public IslandParams Params { get; set; } = null!;

    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };
    private static readonly string[] TypeName = { "plain", "hills", "mountain", "mesa", "basin" };

    public override void _Ready()
    {
        Params ??= new IslandParams();

        long free = 0, ambiguous = 0, cliff = 0;
        long ambiguousOffMountain = 0, pairsOffMountain = 0;
        var cliffByBorder = new Dictionary<string, int>();

        var patchSizes = new List<int>();
        int patchesUndersized = 0;

        var mesaClear = new List<int>();
        var basinDrop = new List<int>();
        int mesaTouchesMountain = 0, mesaTouchesOther = 0;

        var mountainRise = new List<int>();
        int footPairs = 0, footDrops = 0;
        var stepByBand = new Dictionary<int, List<int>>();

        int lakes = 0, lakeCells = 0, leaks = 0, waterAtVoid = 0, islandsWithLake = 0;
        var shoreSteps = new List<int>();

        int landmasses = 0, diagonalLand = 0, diagonalWater = 0;

        ulong t0 = Time.GetTicksMsec();

        for (int i = 0; i < Seeds; i++)
        {
            int seed = FirstSeed + i * 6151;
            IslandData d = new IslandGenerator().Generate(seed, Params);
            int n = d.Size;

            short Top(int x, int z) => d.SurfaceLevel(x, z);
            bool Land(int x, int z) => x >= 0 && z >= 0 && x < n && z < n && d.HasLand(x, z);
            LandformType Form(int x, int z) => (LandformType)d.Landform[x, z];

            // ---- step grammar, and where cliffs fall -------------------------
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Land(x, z)) continue;
                for (int k = 0; k < 2; k++)                     // +X and +Z: each pair once
                {
                    int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                    if (!Land(nx, nz)) continue;

                    int diff = Math.Abs(Top(x, z) - Top(nx, nz));
                    if (diff <= 1) free++;
                    else if (diff == 2) ambiguous++;
                    else cliff++;

                    bool mountain = Form(x, z) == LandformType.Mountain
                                    || Form(nx, nz) == LandformType.Mountain;
                    if (!mountain)
                    {
                        pairsOffMountain++;
                        if (diff == 2) ambiguousOffMountain++;
                    }

                    if (diff >= 3 && d.Region[x, z] != d.Region[nx, nz])
                    {
                        int a = (int)Form(x, z), b = (int)Form(nx, nz);
                        string key = $"{TypeName[Math.Min(a, b)]}-{TypeName[Math.Max(a, b)]}";
                        cliffByBorder.TryGetValue(key, out int c);
                        cliffByBorder[key] = c + 1;
                    }
                }
            }

            // ---- patches ------------------------------------------------------
            var area = new Dictionary<int, int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Land(x, z)) continue;
                area.TryGetValue(d.Region[x, z], out int c);
                area[d.Region[x, z]] = c + 1;
            }
            foreach (int a in area.Values)
            {
                patchSizes.Add(a);
                if (a < Params.MinRegionArea) patchesUndersized++;
            }

            // ---- mesas, basins, and their adjacency ---------------------------
            var worstMesa = new Dictionary<int, int>();
            var worstBasin = new Dictionary<int, int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Land(x, z)) continue;
                LandformType t = Form(x, z);
                if (t != LandformType.Mesa && t != LandformType.Basin) continue;
                int r = d.Region[x, z];

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Land(nx, nz) || d.Region[nx, nz] == r) continue;
                    LandformType o = Form(nx, nz);

                    if (o == LandformType.Mountain) mesaTouchesMountain++;
                    else if (o != LandformType.Plain && o != t) mesaTouchesOther++;

                    if (o == t) continue;                       // stepped mesas / basins are fine
                    int delta = Top(x, z) - Top(nx, nz);
                    var into = t == LandformType.Mesa ? worstMesa : worstBasin;
                    int signed = t == LandformType.Mesa ? delta : -delta;
                    if (!into.TryGetValue(r, out int cur) || signed < cur) into[r] = signed;
                }
            }
            mesaClear.AddRange(worstMesa.Values);
            basinDrop.AddRange(worstBasin.Values);

            // ---- mountains: rise above the foot, and the step profile ---------
            int[,] inward = InwardDistance(d, n);
            var peak = new Dictionary<int, int>();
            var footOf = new Dictionary<int, int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Land(x, z) || Form(x, z) != LandformType.Mountain) continue;
                int r = d.Region[x, z];

                if (!peak.TryGetValue(r, out int hi) || Top(x, z) > hi) peak[r] = Top(x, z);

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Land(nx, nz) || Form(nx, nz) == LandformType.Mountain) continue;
                    footPairs++;
                    if (Top(nx, nz) > Top(x, z)) footDrops++;   // massif below the ground it meets
                    if (!footOf.TryGetValue(r, out int lo) || Top(nx, nz) < lo) footOf[r] = Top(nx, nz);
                }
            }
            foreach (var (r, hi) in peak)
                if (footOf.TryGetValue(r, out int lo)) mountainRise.Add(hi - lo);

            int[] bandMax = MaxInwardPerRegion(d, inward, n);
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Land(x, z) || Form(x, z) != LandformType.Mountain) continue;
                int r = d.Region[x, z];
                if (bandMax[r] <= 0) continue;
                int band = Math.Min(9, inward[x, z] * 10 / (bandMax[r] + 1));

                for (int k = 0; k < 2; k++)
                {
                    int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                    if (!Land(nx, nz) || d.Region[nx, nz] != r) continue;
                    if (!stepByBand.TryGetValue(band, out var list)) stepByBand[band] = list = new List<int>();
                    list.Add(Math.Abs(Top(x, z) - Top(nx, nz)));
                }
            }

            // ---- lakes ---------------------------------------------------------
            var lakeRegions = new HashSet<int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                short w = d.WaterLevel[x, z];
                if (w == IslandData.NoLand) continue;

                lakeCells++;
                lakeRegions.Add(d.Region[x, z]);

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Land(nx, nz)) { waterAtVoid++; continue; }
                    if (d.WaterLevel[nx, nz] != IslandData.NoLand) continue;
                    if (Top(nx, nz) < w) leaks++;               // dry ground *under* the water
                    else shoreSteps.Add(Top(nx, nz) - w);
                }
            }
            lakes += lakeRegions.Count;
            if (lakeRegions.Count > 0) islandsWithLake++;

            // ---- continuity ----------------------------------------------------
            landmasses += CountComponents(d, n);
            diagonalLand += DiagonalOnly(n, (x, z) => Land(x, z));
            diagonalWater += DiagonalOnly(n, (x, z) =>
                x >= 0 && z >= 0 && x < n && z < n && d.WaterLevel[x, z] != IslandData.NoLand);
        }

        ulong ms = Time.GetTicksMsec() - t0;
        long pairs = free + ambiguous + cliff;

        GD.Print($"=== generation audit: {Seeds} islands, {Params.Size}², {ms} ms total ===\n");

        GD.Print($"step grammar ({pairs} adjacent pairs)");
        GD.Print($"  free (0-1 slabs)          {100.0 * free / pairs,6:0.0}%");
        GD.Print($"  two-slab                  {100.0 * ambiguous / pairs,6:0.0}%");
        GD.Print($"  cliff (3+ slabs)          {100.0 * cliff / pairs,6:0.0}%");
        GD.Print($"  two-slab off mountains    {ambiguousOffMountain} of {pairsOffMountain}\n");

        GD.Print("cliffs by the landforms either side (rule: plain-plain, mesa-mesa, basin-basin)");
        foreach (var kv in cliffByBorder.OrderByDescending(k => k.Value))
            GD.Print($"  {kv.Key,-20} {kv.Value,6}");
        GD.Print("");

        patchSizes.Sort();
        GD.Print($"patches: {patchSizes.Count}, min {patchSizes[0]}, median "
            + $"{patchSizes[patchSizes.Count / 2]}, max {patchSizes[^1]}"
            + $"  (target min {Params.MinRegionArea}); undersized {patchesUndersized}");

        Report("mesa clearance above neighbours", mesaClear, "slabs");
        Report("basin drop below neighbours", basinDrop, "slabs");
        GD.Print($"  mesa/basin touching a mountain (want 0): {mesaTouchesMountain}");
        GD.Print($"  mesa/basin touching another kind (want 0): {mesaTouchesOther}\n");

        Report($"mountain rise above foot (MountainHeight {Params.MountainHeight})", mountainRise, "slabs");
        GD.Print($"  border cells where a massif sits below the ground it meets: "
            + $"{footDrops} of {footPairs}\n");

        GD.Print("mountain step profile, by distance into the massif");
        foreach (int band in stepByBand.Keys.OrderBy(k => k))
        {
            var list = stepByBand[band];
            if (list.Count < 20) continue;
            GD.Print($"  {band / 10.0:0.0}-{(band + 1) / 10.0:0.0}   mean {list.Average(),5:0.00}   max {list.Max(),3}");
        }
        GD.Print("");

        GD.Print($"lakes: {lakes} over {lakeCells} cells, on {islandsWithLake} of {Seeds} islands");
        Report("  shore step above water", shoreSteps, "slabs");
        GD.Print($"  dry land BELOW a water surface (want 0): {leaks}");
        GD.Print($"  water touching the void (want 0):        {waterAtVoid}\n");

        GD.Print($"continuity: {landmasses} landmasses for {Seeds} islands"
            + $"  (want {Seeds}); diagonal-only joins: land {diagonalLand}, water {diagonalWater}");
    }

    private static void Report(string label, List<int> values, string unit)
    {
        if (values.Count == 0) { GD.Print($"{label}: none"); return; }
        values.Sort();
        GD.Print($"{label}: min {values[0]}, median {values[values.Count / 2]}, "
            + $"max {values[^1]} {unit}  (n={values.Count})");
    }

    private static int[,] InwardDistance(IslandData d, int n)
    {
        var dist = new int[n, n];
        var q = new Queue<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            dist[x, z] = -1;
            if (!d.HasLand(x, z)) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                bool outside = nx < 0 || nz < 0 || nx >= n || nz >= n
                               || !d.HasLand(nx, nz) || d.Region[nx, nz] != d.Region[x, z];
                if (!outside) continue;
                dist[x, z] = 0;
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
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !d.HasLand(nx, nz)) continue;
                if (d.Region[nx, nz] != d.Region[x, z] || dist[nx, nz] >= 0) continue;
                dist[nx, nz] = dist[x, z] + 1;
                q.Enqueue((nx, nz));
            }
        }
        return dist;
    }

    private static int[] MaxInwardPerRegion(IslandData d, int[,] inward, int n)
    {
        int highest = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.HasLand(x, z)) highest = Math.Max(highest, d.Region[x, z]);

        var max = new int[highest + 1];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.HasLand(x, z)) max[d.Region[x, z]] = Math.Max(max[d.Region[x, z]], inward[x, z]);
        return max;
    }

    private static int CountComponents(IslandData d, int n)
    {
        var seen = new bool[n, n];
        var stack = new Stack<(int X, int Z)>();
        int found = 0;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z) || seen[x, z]) continue;
            found++;
            seen[x, z] = true;
            stack.Push((x, z));
            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Pop();
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!d.HasLand(nx, nz) || seen[nx, nz]) continue;
                    seen[nx, nz] = true;
                    stack.Push((nx, nz));
                }
            }
        }
        return found;
    }

    /// <summary>Corner-only touches: a join you can neither walk nor swim through.</summary>
    private static int DiagonalOnly(int n, Func<int, int, bool> inSet)
    {
        int bad = 0;
        for (int x = 0; x + 1 < n; x++)
        for (int z = 0; z + 1 < n; z++)
        {
            bool a = inSet(x, z), b = inSet(x + 1, z + 1);
            bool c = inSet(x + 1, z), e = inSet(x, z + 1);
            if (a && b && !c && !e) bad++;
            if (c && e && !a && !b) bad++;
        }
        return bad;
    }
}
