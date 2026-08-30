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

        var hillsRelief = new List<int>();
        var hillsSpan = new List<int>();
        var mountainRise = new List<int>();
        int footPairs = 0, footDrops = 0;
        var stepByBand = new Dictionary<int, List<int>>();

        int lakes = 0, lakeCells = 0, leaks = 0, waterAtVoid = 0, islandsWithLake = 0;
        var shoreSteps = new List<int>();

        int landmasses = 0, diagonalLand = 0, diagonalWater = 0;

        // Which landforms each character actually delivered, island by island.
        var charIslands = new Dictionary<TerrainCharacter, int>();
        var charHas = new Dictionary<TerrainCharacter, int[]>();

        long walkLand = 0, walkMainland = 0, walkBroken = 0;
        long mesaCells = 0, mesaOnMainland = 0;
        int districts = 0, scraps = 0;
        var mainlandShare = new List<int>();
        var strandedShare = new List<int>();
        var reachShare = new List<int>();
        long reachHeartland = 0;
        int islandsFullyReachable = 0;
        long mesaReachable = 0;
        var strandedByForm = new long[5];
        int passes = 0, passIslands = 0, passesJoined = 0;
        long passCells = 0;
        var passGrade = new List<int>();

        int buildableShelves = 0, islandsWithShelf = 0;
        var widestShelf = new List<int>();
        var shelfOffMainland = new List<int>();

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
                        // A canyon wall is a cliff no rule forbids: the trench is cut
                        // deliberately, and across any pair of patches. Bucket it as
                        // itself, or it reads as a leak in the landform rules.
                        string key = d.Canyon[x, z] || d.Canyon[nx, nz]
                            ? "canyon (any pair)"
                            : $"{TypeName[Math.Min(a, b)]}-{TypeName[Math.Max(a, b)]}";
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
                    if (t == LandformType.Basin && -delta < 2)
                        GD.Print($"PROBE seed {FirstSeed + i} ({x},{z}) basin {Top(x, z)} vs {o} "
                            + $"{Top(nx, nz)} w{d.WaterLevel[x, z]},{d.WaterLevel[nx, nz]} "
                            + $"shelf{d.ShelfId[x, z]}");
                    var into = t == LandformType.Mesa ? worstMesa : worstBasin;
                    int signed = t == LandformType.Mesa ? delta : -delta;
                    if (!into.TryGetValue(r, out int cur) || signed < cur) into[r] = signed;
                }
            }
            mesaClear.AddRange(worstMesa.Values);
            basinDrop.AddRange(worstBasin.Values);

            // ---- hills: how much relief a mound actually carries ---------------
            // Amplitude is only half the story. Hills keep a slope limit of one
            // slab, so a patch can never be taller than about half its own width
            // in slabs however high Hilliness is set — the width is reported
            // alongside so the ceiling is visible rather than inferred.
            var hiOf = new Dictionary<int, int>();
            var loOf = new Dictionary<int, int>();
            var wideOf = new Dictionary<int, int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Land(x, z) || Form(x, z) != LandformType.Hills) continue;
                int r = d.Region[x, z];
                if (!hiOf.TryGetValue(r, out int hi) || Top(x, z) > hi) hiOf[r] = Top(x, z);
                if (!loOf.TryGetValue(r, out int lo) || Top(x, z) < lo) loOf[r] = Top(x, z);
                wideOf.TryGetValue(r, out int c);
                wideOf[r] = c + 1;
            }
            foreach (var (r, hi) in hiOf)
            {
                hillsRelief.Add(hi - loOf[r]);
                hillsSpan.Add((int)Math.Sqrt(wideOf[r]));
            }

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

            // ---- what the character delivered ----------------------------------
            charIslands.TryGetValue(d.Character, out int seen);
            charIslands[d.Character] = seen + 1;
            if (!charHas.TryGetValue(d.Character, out int[] has))
                charHas[d.Character] = has = new int[5];

            var present = new bool[5];
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (Land(x, z)) present[(int)Form(x, z)] = true;
            for (int t = 0; t < 5; t++) if (present[t]) has[t]++;

            // ---- walkability ---------------------------------------------------
            // The traversal rule made visible: how much of the island is one piece
            // you can cross on foot, and how much is broken ground — the contour
            // benches of a mountain flank, each its own connected set.
            long islandLand = 0, islandMainland = 0, islandHeart = 0;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Land(x, z)) continue;
                islandLand++;
                int w = d.Walk[x, z];
                bool onMain = w == d.Mainland && w >= 0;
                if (onMain) islandMainland++;
                if (w >= 0 && !d.Areas[w].IsDistrict) walkBroken++;

                if (d.Reach[x, z] == d.Heartland && d.Heartland >= 0) islandHeart++;
                else if (d.WaterLevel[x, z] == IslandData.NoLand) strandedByForm[(int)Form(x, z)]++;

                if (Form(x, z) != LandformType.Mesa) continue;
                mesaCells++;
                if (onMain) mesaOnMainland++;
                if (d.Reach[x, z] == d.Heartland && d.Heartland >= 0) mesaReachable++;
            }
            walkLand += islandLand;
            walkMainland += islandMainland;
            reachHeartland += islandHeart;
            if (islandLand > 0)
            {
                mainlandShare.Add((int)(100 * islandMainland / islandLand));
                strandedShare.Add((int)(100 * (islandLand - islandMainland) / islandLand));
                reachShare.Add((int)(100 * islandHeart / islandLand));
                // Flooded columns are not ground, so a lake's own cells never join
                // the heartland; the land around it is what has to.
                long dry = 0;
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    if (Land(x, z) && d.WaterLevel[x, z] == IslandData.NoLand) dry++;
                if (islandHeart >= dry) islandsFullyReachable++;
            }
            foreach (WalkArea a in d.Areas) { if (a.IsDistrict) districts++; else scraps++; }

            // ---- passes --------------------------------------------------------
            // Did the saddle actually do its job? A pass works when the two patches
            // it straddles end up in ONE walk area — walkable, not merely lower.
            passes += d.Passes.Count;
            if (d.Passes.Count > 0) passIslands++;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.Pass[x, z]) continue;
                passCells++;
                // Steepest step out of a pass cell: the whole point is that it is 1.
                int worst = 0;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Land(nx, nz) || !d.Pass[nx, nz]) continue;
                    worst = Math.Max(worst, Math.Abs(Top(x, z) - Top(nx, nz)));
                }
                passGrade.Add(worst);
            }
            foreach (Vector2I site in d.Passes)
            {
                var across = new HashSet<int>();
                for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    int x = site.X + dx, z = site.Y + dz;
                    if (Land(x, z) && d.Walk[x, z] >= 0) across.Add(d.Region[x, z]);
                }
                // Two patches meeting at the site, one walk area covering both.
                if (across.Count < 2) continue;
                var walks = new HashSet<int>();
                for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    int x = site.X + dx, z = site.Y + dz;
                    if (Land(x, z) && d.Walk[x, z] >= 0) walks.Add(d.Walk[x, z]);
                }
                if (walks.Count == 1) passesJoined++;
            }

            // ---- shelves -------------------------------------------------------
            int islandShelves = 0, widest = 0, offMain = 0;
            foreach (Shelf shelf in d.Shelves)
            {
                widest = Math.Max(widest, shelf.Width);
                if (!shelf.Buildable) continue;
                islandShelves++;
                if (d.Walk[shelf.Center.X, shelf.Center.Y] != d.Mainland) offMain++;
            }
            buildableShelves += islandShelves;
            if (islandShelves > 0) islandsWithShelf++;
            widestShelf.Add(widest);
            shelfOffMainland.Add(offMain);

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

        Report($"hills relief per patch (Hilliness {Params.Hilliness:0.00})", hillsRelief, "slabs");
        Report("  that patch's width", hillsSpan, "cells");

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

        GD.Print("landforms delivered, by character (share of that character's islands)");
        foreach (var (c, islands) in charIslands.OrderBy(k => k.Key.ToString()))
        {
            int[] has = charHas[c];
            var parts = new List<string>();
            for (int t = 0; t < 5; t++)
                if (has[t] > 0) parts.Add($"{TypeName[t]} {100 * has[t] / islands}%");
            GD.Print($"  {c,-10} {islands,3} islands   {string.Join(", ", parts)}");
        }
        GD.Print("");

        GD.Print("walkability (one-slab step free, 2+ a wall; water is not ground)");
        GD.Print($"  land on the mainland        {100.0 * walkMainland / walkLand,6:0.0}%");
        Report("  mainland share per island", mainlandShare, "%");
        Report("  stranded off the mainland", strandedShare, "%");
        GD.Print($"  broken ground               {100.0 * walkBroken / walkLand,6:0.0}%"
            + $"  in {scraps} scraps, against {districts} districts");
        GD.Print($"\n  with stairs, hoists and bridges ("
            + $"face <= {Traversal.InfrastructureStep} slabs, span <= {Traversal.MaxBridgeSpan} cells)");
        GD.Print($"  land on the heartland       {100.0 * reachHeartland / walkLand,6:0.0}%");
        Report("  heartland share per island", reachShare, "%");
        GD.Print($"  islands whose dry land is ONE reachable whole: {islandsFullyReachable} of {Seeds}");
        long stranded = 0;
        foreach (long v in strandedByForm) stranded += v;
        if (stranded > 0)
        {
            var bits = new List<string>();
            for (int t = 0; t < 5; t++)
                if (strandedByForm[t] > 0) bits.Add($"{TypeName[t]} {100 * strandedByForm[t] / stranded}%");
            GD.Print($"  what stays out of reach: {string.Join(", ", bits)}");
        }
        GD.Print($"  mesa top reachable at all   "
            + (mesaCells > 0 ? $"{100.0 * mesaReachable / mesaCells,6:0.0}% of mesa cells"
                             : "no mesas"));

        GD.Print($"  mesa top reachable on foot  "
            + (mesaCells > 0 ? $"{100.0 * mesaOnMainland / mesaCells,6:0.0}% of mesa cells"
                             : "no mesas")
            + "\n");

        GD.Print($"passes: {passes} cut on {passIslands} of {Seeds} islands, "
            + $"{passesJoined} joining their two patches into one walk area, "
            + $"{(passes > 0 ? passCells / passes : 0)} cells each");
        Report("  steepest step inside a pass", passGrade, "slabs");
        GD.Print("");

        GD.Print($"shelves (flat, >= {Traversal.MinShelfArea} cells and "
            + $">= {Traversal.MinShelfWidth} wide): {buildableShelves} buildable, "
            + $"on {islandsWithShelf} of {Seeds} islands");
        Report("  widest square of flat ground", widestShelf, "cells");
        Report("  buildable shelves off the mainland", shelfOffMainland, "per island");
        GD.Print("");

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
