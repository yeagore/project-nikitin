using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

/// <summary>
/// TEMPORARY. Where the two-slab step — neither a free step nor a cliff — comes
/// from, and what it costs. Read-only: it generates islands and measures them.
/// Modes: names, bodies, steps, matrix, sizes, knobs, stages.
/// </summary>
public partial class StepProbe : Node
{
    [Export] public IslandParams Params { get; set; } = null!;
    [Export] public int Seeds { get; set; } = 60;
    [Export] public int FirstSeed { get; set; } = 5000;

    private string _mode = "steps";

    private int SeedAt(int i) => FirstSeed + i * 6151;

    public override void _Ready()
    {
        Params ??= new IslandParams();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("mode=")) _mode = arg[5..];
            else if (arg.StartsWith("seeds=") && int.TryParse(arg.AsSpan(6), out int n)) Seeds = n;
            else if (arg.StartsWith("size=") && int.TryParse(arg.AsSpan(5), out int s)) Params.Size = s;
        }

        ulong t0 = Time.GetTicksMsec();
        switch (_mode)
        {
            case "names": Names(); break;
            case "bodies": Bodies(); break;
            case "matrix": Matrix(); break;
            case "sizes": Sizes(); break;
            case "knobs": Knobs(); break;
            case "stages": Stages(); break;
            case "banks": Banks(); break;
            case "inland": Inland(); break;
            case "works": Works(); break;
            case "whatif": WhatIf(); break;
            case "threes": Threes(); break;
            case "whatif3": WhatIfThrees(); break;
            case "whatifwater": WhatIfWater(); break;
            case "borders": Borders(); break;
            case "fingerprint": Fingerprint(); break;
            case "anchorcheck": AnchorCheck(); break;
            default: Steps(); break;
        }
        GD.Print($"[{_mode}] {Time.GetTicksMsec() - t0} ms");
        GetTree().Quit();
    }

    // ---- 1. names ----------------------------------------------------------

    private void Names()
    {
        int islands = 0, clashIslands = 0, clashes = 0, names = 0;
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            for (int i = 0; i < Seeds; i++)
            {
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                var seen = new HashSet<string>();
                int here = 0;
                if (!seen.Add(d.Name)) here++;
                foreach (string s in d.Districts) { if (s.Length == 0) continue; names++; if (!seen.Add(s)) here++; }
                foreach (string s in d.WaterNames) { names++; if (!seen.Add(s)) here++; }
                islands++;
                clashes += here;
                if (here > 0) clashIslands++;
            }
        }
        GD.Print($"names: {islands} islands, {names} district and water names, "
            + $"{clashes} repeats on {clashIslands} islands");
    }

    // ---- 2. bodies of water ------------------------------------------------

    private void Bodies()
    {
        var sizes = new List<int>();
        var perIsland = new List<int>();
        int islands = 0, one = 0, stillOne = 0, reachOne = 0, oneByFall = 0;
        var buckets = new int[] { 1, 2, 4, 8, 16, 32, 64, 128, int.MaxValue };
        var histogram = new int[buckets.Length];
        int usable8 = 0, usable20 = 0, islandsWithUsable = 0;

        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            for (int i = 0; i < Seeds; i++)
            {
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                int n = d.Size;
                var cells = new int[Math.Max(1, d.WaterBodies)];
                var still = new int[Math.Max(1, d.WaterBodies)];
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    int id = d.WaterBody[x, z];
                    if (id < 0) continue;
                    cells[id]++;
                    if (!d.River[x, z]) still[id]++;
                }

                var lips = new HashSet<Vector2I>();
                foreach (Fall f in d.Falls) lips.Add(f.Cell);

                bool any = false;
                for (int id = 0; id < d.WaterBodies; id++)
                {
                    sizes.Add(cells[id]);
                    for (int b = 0; b < buckets.Length; b++)
                        if (cells[id] <= buckets[b]) { histogram[b]++; break; }
                    if (cells[id] >= 8) { usable8++; any = true; }
                    if (cells[id] >= 20) usable20++;
                    if (cells[id] != 1) continue;
                    one++;
                    if (still[id] == 1) stillOne++; else reachOne++;
                    for (int x = 0; x < n && cells[id] == 1; x++)
                    for (int z = 0; z < n; z++)
                        if (d.WaterBody[x, z] == id && lips.Contains(new Vector2I(x, z))) { oneByFall++; x = n; break; }
                }
                perIsland.Add(d.WaterBodies);
                if (any) islandsWithUsable++;
                islands++;
            }
        }

        sizes.Sort();
        perIsland.Sort();
        GD.Print($"bodies: {sizes.Count} over {islands} islands at all three footprints "
            + $"({Med(perIsland)} per island, max {perIsland[^1]})");
        string[] names = { "1 cell", "2", "3-4", "5-8", "9-16", "17-32", "33-64", "65-128", "129+" };
        for (int b = 0; b < buckets.Length; b++)
            GD.Print($"  {names[b],8}: {histogram[b],5}  {100f * histogram[b] / sizes.Count:0.0}%");
        GD.Print($"  one-cell bodies: {one} ({stillOne} standing water, {reachOne} a reach), "
            + $"{oneByFall} of them carrying a fall's lip");
        GD.Print($"  8+ cells: {usable8} ({100f * usable8 / sizes.Count:0}%), "
            + $"20+: {usable20} ({100f * usable20 / sizes.Count:0}%); "
            + $"islands with a body of 8+: {islandsWithUsable} of {islands}");
    }

    // ---- 3. the two-slab taxonomy -----------------------------------------

    /// <summary>What a two-slab step is standing on, first match winning.</summary>
    private static readonly string[] Causes =
    {
        "mountain", "massif", "karst", "badlands", "sinkholes",
        "gate landing", "bridgehead", "pass", "canyon",
        "stream step (a rapid)", "stream bank", "beach", "delta or estuary",
        "region border", "interior (unexplained)",
    };

    private sealed class StepTally
    {
        public long Free, Two, Cliff;
        public readonly long[] Cause = new long[Causes.Length];
        public readonly Dictionary<string, int> InteriorForms = new();
        public readonly Dictionary<string, int> BorderForms = new();
        public readonly List<int> PerIsland = new();
        public long Walls, WallsBetweenAreas, WallsBetweenDistricts;
        public long AreasJoined, CellsJoined, DistrictsMade;
        public long WatersideBare, Waterside;
        public long InteriorNearWater, InteriorNearWorks, InteriorNowhere;
        public long InteriorByLake, InteriorByStream, InteriorByNavigable, InteriorTouching;
        public readonly List<int> OffMountainPerIsland = new();
        public long OffMountain, OffMountainWalls, OffMountainAreasJoined, OffMountainCellsJoined;
        public readonly long[] MountainSteps = new long[6];
        public readonly long[] LimitedSteps = new long[6];
        public int Islands;
        public int WorstIsland;
        public int WorstSeed;
    }

    private void Steps()
    {
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            var t = new StepTally();
            for (int i = 0; i < Seeds; i++) Measure(SeedAt(i), IslandGenerator.Generate(SeedAt(i), p), t);
            Report($"{size}²", t);
        }
    }

    private void Sizes()
    {
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            var t = new StepTally();
            for (int i = 0; i < Seeds; i++) Measure(SeedAt(i), IslandGenerator.Generate(SeedAt(i), p), t);
            GD.Print($"{size,4}²: two-slab {100.0 * t.Two / Math.Max(1, t.Free + t.Two + t.Cliff):0.00}%"
                + $"   {t.Two} steps, {Med(t.PerIsland)} per island (max {t.WorstIsland}, seed {t.WorstSeed})"
                + $"   walls between walk areas {t.WallsBetweenAreas}");
        }
    }

    private static void Measure(int seed, IslandData d, StepTally t)
    {
        int n = d.Size;
        t.Islands++;
        int here = 0;

        var landing = d.Landings;
        var heads = new HashSet<Vector2I>();
        foreach (Crossing c in d.Bridges) { heads.Add(c.A); heads.Add(c.B); }

        // Walk areas joined by a two-slab step, as a union-find over area ids.
        var parent = new int[Math.Max(1, d.Areas.Count)];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
        void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }

        var off = new int[Math.Max(1, d.Areas.Count)];
        for (int i = 0; i < off.Length; i++) off[i] = i;
        int FindOff(int a) { while (off[a] != a) { off[a] = off[off[a]]; a = off[a]; } return a; }
        void JoinOff(int a, int b) { a = FindOff(a); b = FindOff(b); if (a != b) off[a] = b; }
        int offHere = 0;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Ground(d, x, z)) continue;
            for (int k = 0; k < 2; k++)
            {
                int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                if (!InBounds(n, nx, nz) || !Ground(d, nx, nz)) continue;

                int diff = Math.Abs(Cross(d, x, z) - Cross(d, nx, nz));
                var fa = (LandformType)d.Landform[x, z];
                var fb = (LandformType)d.Landform[nx, nz];
                bool onMountain = fa == LandformType.Mountain || fb == LandformType.Mountain;
                bool limited = Limited(fa) && Limited(fb) && d.Region[x, z] == d.Region[nx, nz];
                int bucket = Math.Min(5, diff);
                if (onMountain) t.MountainSteps[bucket]++;
                else if (limited) t.LimitedSteps[bucket]++;

                if (diff <= 1) { t.Free++; continue; }
                if (diff >= 3) { t.Cliff++; continue; }

                t.Two++;
                here++;
                int cause = Cause(d, x, z, nx, nz, landing, heads);
                t.Cause[cause]++;

                string pair = FormPair(d, x, z, nx, nz);
                if (Causes[cause].StartsWith("interior"))
                {
                    Bump(t.InteriorForms, pair);
                    if (NearWater(d, x, z) || NearWater(d, nx, nz)) t.InteriorNearWater++;
                    else if (NearWorks(d, x, z, landing, heads) || NearWorks(d, nx, nz, landing, heads)) t.InteriorNearWorks++;
                    else t.InteriorNowhere++;
                    var (kind, touching) = WaterBeside(d, x, z, nx, nz);
                    if (kind == 1) t.InteriorByStream++;
                    else if (kind == 2) t.InteriorByNavigable++;
                    else if (kind == 3) t.InteriorByLake++;
                    if (touching) t.InteriorTouching++;
                }
                if (!onMountain)
                {
                    t.OffMountain++;
                    offHere++;
                }
                if (Causes[cause] == "region border") Bump(t.BorderForms, pair);

                // What it costs: a two-slab step is a wall to walking.
                t.Walls++;
                int a = d.Walk[x, z], b = d.Walk[nx, nz];
                if (a >= 0 && b >= 0 && a < d.Areas.Count && b < d.Areas.Count && a != b)
                {
                    t.WallsBetweenAreas++;
                    if (d.Areas[a].IsDistrict && d.Areas[b].IsDistrict) t.WallsBetweenDistricts++;
                    Join(a, b);
                    if (!onMountain) { t.OffMountainWalls++; JoinOff(a, b); }
                }
            }
        }

        // How much ground the two-slab steps keep apart: the areas they would join.
        var groups = new Dictionary<int, int>();
        var madeDistrict = new Dictionary<int, bool>();
        for (int i = 0; i < d.Areas.Count; i++)
        {
            if (d.Areas[i].Id == Traversal.Water) continue;
            int root = Find(i);
            groups.TryGetValue(root, out int had);
            groups[root] = had + d.Areas[i].Area;
            madeDistrict[root] = madeDistrict.GetValueOrDefault(root) || d.Areas[i].IsDistrict;
        }
        foreach (var (root, area) in groups)
        {
            int members = 0, biggest = 0;
            for (int i = 0; i < d.Areas.Count; i++)
            {
                if (d.Areas[i].Id == Traversal.Water || Find(i) != root) continue;
                members++;
                biggest = Math.Max(biggest, d.Areas[i].Area);
            }
            if (members <= 1) continue;
            t.AreasJoined += members - 1;
            t.CellsJoined += area - biggest;      // the ground absorbed into something bigger
            if (!madeDistrict[root] && area >= Traversal.MinDistrictArea) t.DistrictsMade++;
        }

        var offGroups = new Dictionary<int, int>();
        for (int i = 0; i < d.Areas.Count; i++)
        {
            if (d.Areas[i].Id == Traversal.Water) continue;
            int root = FindOff(i);
            offGroups.TryGetValue(root, out int had);
            offGroups[root] = had + d.Areas[i].Area;
        }
        foreach (var (root, area) in offGroups)
        {
            int members = 0, biggest = 0;
            for (int i = 0; i < d.Areas.Count; i++)
            {
                if (d.Areas[i].Id == Traversal.Water || FindOff(i) != root) continue;
                members++;
                biggest = Math.Max(biggest, d.Areas[i].Area);
            }
            if (members <= 1) continue;
            t.OffMountainAreasJoined += members - 1;
            t.OffMountainCellsJoined += area - biggest;
        }
        t.OffMountainPerIsland.Add(offHere);

        // The waterside case the step grammar cannot see: a dry cell beside a lake
        // or a navigable reach, which are not ground and so never form a pair.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
            bool beside = false;
            int gap = 0;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                if (d.WaterLevel[nx, nz] == IslandData.NoLand) continue;
                if (d.Fluid[nx, nz] == (byte)FluidKind.Goo) continue;
                beside = true;
                gap = Math.Max(gap, d.Spans[x, z][0].Top - d.WaterLevel[nx, nz]);
            }
            if (!beside) continue;
            t.Waterside++;
            if (gap == 2) t.WatersideBare++;
        }

        t.PerIsland.Add(here);
        if (here > t.WorstIsland) { t.WorstIsland = here; t.WorstSeed = seed; }
    }

    /// <summary>A landform the slope limit binds: a step of two inside one of these patches is not one the grammar asked for.</summary>
    private static bool Limited(LandformType t)
        => t is LandformType.Plain or LandformType.Hills or LandformType.Dunes
             or LandformType.Mesa or LandformType.Basin;

    /// <summary>What water is beside either cell of a pair, and whether it touches one of them: 1 stream, 2 navigable, 3 standing.</summary>
    private static (int Kind, bool Touching) WaterBeside(IslandData d, int x, int z, int nx, int nz)
    {
        int n = d.Size;
        int kind = 0;
        bool touching = false;
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        foreach (var (cx, cz) in new[] { (x, z), (nx, nz) })
        {
            int ax = cx + dx, az = cz + dz;
            if (!InBounds(n, ax, az) || !d.HasLand(ax, az)) continue;
            if (d.WaterLevel[ax, az] == IslandData.NoLand) continue;
            int here = d.Navigable[ax, az] ? 2 : d.River[ax, az] ? 1 : 3;
            if (kind == 0 || Math.Abs(dx) + Math.Abs(dz) <= 1) kind = here;
            if (Math.Abs(dx) + Math.Abs(dz) <= 1) touching = true;
        }
        return (kind, touching);
    }

    private static bool Ground(IslandData d, int x, int z)
        => d.HasLand(x, z) && !d.Navigable[x, z]
           && (d.River[x, z] || d.WaterLevel[x, z] == IslandData.NoLand);

    private static short Cross(IslandData d, int x, int z)
        => d.River[x, z] && !d.Navigable[x, z] ? d.WaterLevel[x, z] : d.SurfaceLevel(x, z);

    private static int Cause(IslandData d, int x, int z, int nx, int nz, bool[,] landing, HashSet<Vector2I> heads)
    {
        var a = (LandformType)d.Landform[x, z];
        var b = (LandformType)d.Landform[nx, nz];
        if (a == LandformType.Mountain || b == LandformType.Mountain) return 0;
        if (a == LandformType.Massif || b == LandformType.Massif) return 1;
        if (a == LandformType.Karst || b == LandformType.Karst) return 2;
        if (a == LandformType.Badlands || b == LandformType.Badlands) return 3;
        if (a == LandformType.Sinkholes || b == LandformType.Sinkholes) return 4;
        if (landing[x, z] || landing[nx, nz]) return 5;
        if (heads.Contains(new Vector2I(x, z)) || heads.Contains(new Vector2I(nx, nz))) return 6;
        if (d.Pass[x, z] || d.Pass[nx, nz]) return 7;
        if (d.Canyon[x, z] || d.Canyon[nx, nz]) return 8;
        bool wetA = d.River[x, z], wetB = d.River[nx, nz];
        if (wetA && wetB) return 9;                      // the water's own step: a rapid
        if (wetA || wetB) return 10;                     // a bank standing over its stream
        if (d.Beach[x, z] || d.Beach[nx, nz]) return 11;
        if (d.Delta[x, z] || d.Delta[nx, nz] || d.Estuary[x, z] || d.Estuary[nx, nz]) return 12;
        if (d.Region[x, z] != d.Region[nx, nz]) return 13;
        return 14;
    }

    /// <summary>Within two cells of any water, which is what the river and lake passes reach.</summary>
    private static bool NearWater(IslandData d, int x, int z)
    {
        int n = d.Size;
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        {
            int nx = x + dx, nz = z + dz;
            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
            if (d.WaterLevel[nx, nz] != IslandData.NoLand) return true;
        }
        return false;
    }

    /// <summary>Within two cells of a levelled thing: a Gate's strip, a bridgehead, a beach.</summary>
    private static bool NearWorks(IslandData d, int x, int z, bool[,] landing, HashSet<Vector2I> heads)
    {
        int n = d.Size;
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        {
            int nx = x + dx, nz = z + dz;
            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
            if (landing[nx, nz] || d.Beach[nx, nz] || heads.Contains(new Vector2I(nx, nz))) return true;
        }
        return false;
    }

    private static string FormPair(IslandData d, int x, int z, int nx, int nz)
    {
        var a = (LandformType)d.Landform[x, z];
        var b = (LandformType)d.Landform[nx, nz];
        string first = (a <= b ? a : b).ToString().ToLowerInvariant();
        string second = (a <= b ? b : a).ToString().ToLowerInvariant();
        return $"{first}-{second}";
    }

    private static void Report(string label, StepTally t)
    {
        long pairs = t.Free + t.Two + t.Cliff;
        t.PerIsland.Sort();
        GD.Print($"=== {label}: {t.Islands} islands, {pairs} ground pairs ===");
        GD.Print($"  free {100.0 * t.Free / pairs:0.00}%   two-slab {100.0 * t.Two / pairs:0.00}%   cliff {100.0 * t.Cliff / pairs:0.00}%");
        GD.Print($"  two-slab steps: {t.Two} total, per island min {t.PerIsland[0]} median {Med(t.PerIsland)} max {t.WorstIsland} (seed {t.WorstSeed})");
        for (int i = 0; i < Causes.Length; i++)
            if (t.Cause[i] > 0)
                GD.Print($"    {Causes[i],-24} {t.Cause[i],6}  {100.0 * t.Cause[i] / t.Two:0.0}%");
        t.OffMountainPerIsland.Sort();
        GD.Print($"  off the mountains: {t.OffMountain} steps, per island median {Med(t.OffMountainPerIsland)} "
            + $"max {t.OffMountainPerIsland[^1]}; {t.OffMountainWalls} separate two walk areas, and ironing "
            + $"those out would join {t.OffMountainAreasJoined} areas and absorb {t.OffMountainCellsJoined} cells");
        GD.Print($"  the interior ones sit: {t.InteriorNearWater} within two cells of water "
            + $"({t.InteriorTouching} touching it: {t.InteriorByStream} a stream, {t.InteriorByNavigable} a "
            + $"navigable reach, {t.InteriorByLake} standing water), "
            + $"{t.InteriorNearWorks} of a levelled work, {t.InteriorNowhere} nowhere near either");
        GD.Print($"  steps inside mountain ground:  " + Histogram(t.MountainSteps));
        GD.Print($"  steps inside one limited patch: " + Histogram(t.LimitedSteps));
        GD.Print("    interior by landform pair: " + Top(t.InteriorForms, 8));
        GD.Print("    border by landform pair:   " + Top(t.BorderForms, 8));
        GD.Print($"  what they cost: {t.WallsBetweenAreas} of {t.Two} separate two walk areas "
            + $"({t.WallsBetweenDistricts} two districts); ironing every one out would join "
            + $"{t.AreasJoined} areas and absorb {t.CellsJoined} cells of stranded ground, making {t.DistrictsMade} new districts");
        GD.Print($"  waterside (invisible to the pair measure): {t.WatersideBare} of {t.Waterside} dry cells "
            + $"beside water stand two above it ({100.0 * t.WatersideBare / Math.Max(1, t.Waterside):0.00}%)");
    }

    // ---- 3b. why a bank still stands two above its water -------------------

    /// <summary>
    /// Every dry cell standing exactly two above water beside it, against the guards
    /// <c>Rivers.CutBanks</c> would have applied — read off the finished island, so a
    /// cell no guard explains is one the pass never had the chance to cut.
    /// </summary>
    private void Banks()
    {
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            long two = 0, pinned = 0, keep = 0, basin = 0, unexplained = 0, bare = 0, stream = 0, still = 0, nav = 0;
            long waterside = 0;
            var anchors = new Dictionary<string, int>();
            var examplesBare = new List<string>();
            var examplesNothing = new List<string>();

            for (int i = 0; i < Seeds; i++)
            {
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                int n = d.Size;

                var held = new bool[n, n];
                foreach (Crossing c in d.Bridges)
                {
                    held[c.A.X, c.A.Y] = true;
                    held[c.B.X, c.B.Y] = true;
                }
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (d.WaterLevel[x, z] == IslandData.NoLand || d.Fluid[x, z] != (byte)FluidKind.Goo) continue;
                    for (int ox = -1; ox <= 1; ox++)
                    for (int oz = -1; oz <= 1; oz++)
                        if (InBounds(n, x + ox, z + oz)) held[x + ox, z + oz] = true;
                }

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                    short top = d.Spans[x, z][0].Top;
                    short highest = IslandData.NoLand, lowest = IslandData.NoLand;
                    int kind = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                        short w = d.WaterLevel[nx, nz];
                        if (w == IslandData.NoLand || d.Fluid[nx, nz] == (byte)FluidKind.Goo) continue;
                        if (highest == IslandData.NoLand || w > highest) highest = w;
                        if (lowest == IslandData.NoLand || w < lowest) lowest = w;
                        if (top - w == 2) kind = d.Navigable[nx, nz] ? 2 : d.River[nx, nz] ? 1 : 3;
                    }
                    if (highest == IslandData.NoLand) continue;
                    waterside++;
                    if (top - lowest != 2) continue;

                    two++;
                    if (kind == 1) stream++; else if (kind == 2) nav++; else still++;
                    if (top - 1 < highest + 1) pinned++;
                    else if (held[x, z]) keep++;
                    else if (BasinPins(d, x, z, top)) basin++;
                    else { unexplained++; if (examplesBare.Count < 6) examplesBare.Add($"seed {SeedAt(i)} at {x},{z}"); }

                    string anchor = AnchorOf(d, x, z);
                    Bump(anchors, anchor);
                    if (anchor == "nothing")
                    {
                        bare++;
                        if (examplesNothing.Count < 6) examplesNothing.Add($"seed {SeedAt(i)} at {x},{z}");
                    }
                }
            }

            GD.Print($"=== {size}²: {two} dry cells stand two above the water beside them, of {waterside} waterside cells "
                + $"({100.0 * two / waterside:0.00}%) ===");
            GD.Print($"  the water is: {stream} a stream, {nav} a navigable reach, {still} standing water");
            GD.Print($"  why the bank cut left them: {pinned} pinned by higher water on their other side, "
                + $"{keep} a bridgehead or goo's neighbour, {basin} a basin rim, {unexplained} nothing explains");
            GD.Print($"  what the content layer sees there: " + Top(anchors, 10) + $"   ({bare} anchored to nothing)");
            if (examplesNothing.Count > 0) GD.Print("  anchored to nothing, to look at: " + string.Join(";  ", examplesNothing));
            if (examplesBare.Count > 0) GD.Print("  unexplained, to look at: " + string.Join(";  ", examplesBare));
        }
    }

    /// <summary>
    /// The two-slab steps between two dry cells near water — the ones a cut leaves
    /// behind it — against the guards the correction would have applied to the higher
    /// cell of each. A cell no guard explains is one the correction never reached.
    /// </summary>
    private void Inland()
    {
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            long steps = 0, pinned = 0, keep = 0, basin = 0, unlimited = 0, never = 0;
            long touching = 0, oneBack = 0, further = 0;
            var forms = new Dictionary<string, int>();
            var neverBy = new Dictionary<string, int>();
            var examples = new List<string>();

            for (int i = 0; i < Seeds; i++)
            {
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                int n = d.Size;
                var held = Held(d);

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!Dry(d, x, z)) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                        if (!InBounds(n, nx, nz) || !Dry(d, nx, nz)) continue;

                        short a = d.Spans[x, z][0].Top, b = d.Spans[nx, nz][0].Top;
                        if (Math.Abs(a - b) != 2) continue;

                        int hx = a > b ? x : nx, hz = a > b ? z : nz;      // the cell a cut would take
                        int lx = a > b ? nx : x, lz = a > b ? nz : z;
                        int reach = Math.Min(ToWater(d, hx, hz), ToWater(d, lx, lz));
                        if (reach > 2) continue;                           // not the water's doing

                        steps++;
                        if (reach == 1) touching++; else if (reach == 2) oneBack++; else further++;
                        Bump(forms, FormPair(d, hx, hz, lx, lz));

                        short top = d.Spans[hx, hz][0].Top;
                        var form = (LandformType)d.Landform[hx, hz];
                        if (!Limited(form)) unlimited++;
                        else if (top - 1 < HighestWater(d, hx, hz) + 1) pinned++;
                        else if (held[hx, hz]) keep++;
                        else if (BasinPins(d, hx, hz, top)) basin++;
                        else
                        {
                            never++;
                            if (examples.Count < 8) examples.Add($"seed {SeedAt(i)} at {hx},{hz} over {lx},{lz}");
                            short lowWater = HighestWater(d, lx, lz);
                            string beside = lowWater != IslandData.NoLand
                                ? (d.Spans[lx, lz][0].Top - lowWater <= 1 ? "the low cell is a bank at the free step" : "the low cell stands over its water")
                                : "neither cell touches water";
                            string kind = WaterKindNear(d, hx, hz, lx, lz);
                            Bump(neverBy, $"{beside}, {kind}");
                        }
                    }
                }
            }

            GD.Print($"=== {size}²: {steps} two-slab steps between dry cells within two of water ===");
            GD.Print($"  {touching} with a cell on the water, {oneBack} a cell back");
            GD.Print($"  the higher cell is: {pinned} pinned by water beside it, {keep} a bridgehead or goo's neighbour, "
                + $"{basin} a basin rim, {unlimited} ground the limit does not bind, {never} none of those");
            GD.Print("  by landform pair: " + Top(forms, 8));
            GD.Print("  the ones nothing explains: " + Top(neverBy, 6));
            if (examples.Count > 0) GD.Print("  to look at: " + string.Join(";  ", examples));
        }
    }

    /// <summary>The nearest water to either cell of a pair, named.</summary>
    private static string WaterKindNear(IslandData d, int hx, int hz, int lx, int lz)
    {
        int n = d.Size;
        for (int r = 1; r <= 2; r++)
        foreach (var (cx, cz) in new[] { (lx, lz), (hx, hz) })
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (Math.Abs(dx) + Math.Abs(dz) != r) continue;
            int nx = cx + dx, nz = cz + dz;
            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
            if (d.WaterLevel[nx, nz] == IslandData.NoLand) continue;
            if (d.Fluid[nx, nz] == (byte)FluidKind.Goo) return "beside goo";
            return d.Navigable[nx, nz] ? "beside a navigable reach"
                 : d.River[nx, nz] ? "beside a stream" : "beside standing water";
        }
        return "no water within two";
    }

    private static bool Dry(IslandData d, int x, int z)
        => d.HasLand(x, z) && d.WaterLevel[x, z] == IslandData.NoLand;

    /// <summary>Cells from water, 1 where a cell touches it cardinally; 99 past two cells.</summary>
    private static int ToWater(IslandData d, int x, int z)
    {
        int n = d.Size;
        for (int r = 1; r <= 2; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (Math.Abs(dx) + Math.Abs(dz) != r) continue;
            int nx = x + dx, nz = z + dz;
            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
            if (d.WaterLevel[nx, nz] != IslandData.NoLand && d.Fluid[nx, nz] != (byte)FluidKind.Goo) return r;
        }
        return 99;
    }

    private static short HighestWater(IslandData d, int x, int z)
    {
        int n = d.Size;
        short highest = IslandData.NoLand;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + Dx[k], nz = z + Dz[k];
            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
            short w = d.WaterLevel[nx, nz];
            if (w != IslandData.NoLand && (highest == IslandData.NoLand || w > highest)) highest = w;
        }
        return highest;
    }

    /// <summary>The cells the river stage may not touch: a bridgehead, and goo's king's-move neighbourhood.</summary>
    private static bool[,] Held(IslandData d)
    {
        int n = d.Size;
        var held = new bool[n, n];
        foreach (Crossing c in d.Bridges)
        {
            held[c.A.X, c.A.Y] = true;
            held[c.B.X, c.B.Y] = true;
        }
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (d.WaterLevel[x, z] == IslandData.NoLand || d.Fluid[x, z] != (byte)FluidKind.Goo) continue;
            for (int ox = -1; ox <= 1; ox++)
            for (int oz = -1; oz <= 1; oz++)
                if (InBounds(n, x + ox, z + oz)) held[x + ox, z + oz] = true;
        }
        return held;
    }

    /// <summary>The basin rule in <c>CutBanks.Floor</c>: a cell outside a basin may not come within a cliff of its floor.</summary>
    private static bool BasinPins(IslandData d, int x, int z, short top)
    {
        if ((LandformType)d.Landform[x, z] == LandformType.Basin) return false;
        int n = d.Size;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + Dx[k], nz = z + Dz[k];
            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
            if ((LandformType)d.Landform[nx, nz] != LandformType.Basin) continue;
            if (top - 1 < d.Spans[nx, nz][0].Top + 3) return true;
        }
        return false;
    }

    /// <summary>What the anchors call a cell — the lab's view, flattened the same way.</summary>
    private static string AnchorOf(IslandData d, int x, int z)
    {
        var at = new Vector2I(x, z);
        if (d.Landings[x, z]) return "gate landing";
        if (d.Ford[x, z]) return "ford";
        if (d.Beach[x, z]) return "beach";
        foreach (Vector2I c in d.Springs) if (c == at) return "spring";
        foreach (Fall f in d.Falls) if (f.Cell == at) return "fall";
        foreach (Vector2I c in d.BankCells) if (c == at) return "bank";
        foreach (Vector2I c in d.CliffCells) if (c == at) return "cliff brink";
        foreach (Vector2I c in d.CliffFootCells) if (c == at) return "cliff foot";
        foreach (Vector2I c in d.ImpasseCells) if (c == at) return "impasse brink";
        foreach (Vector2I c in d.ImpasseFootCells) if (c == at) return "impasse foot";
        foreach (Vector2I c in d.CoastCells) if (c == at) return "coast";
        foreach (Vector2I c in d.Summits) if (c == at) return "summit";
        return "nothing";
    }

    // ---- 3c. the two places a two-slab step would actually bite -------------

    /// <summary>
    /// A pass is cut to join two patches on foot and a Gate's landing strip is levelled
    /// to be stood on: a two-slab step on either is a work that does not work. Counts
    /// the steps on each, and whether the ground either serves is one walk area.
    /// </summary>
    private void Works()
    {
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            int passes = 0, passesStepped = 0, passesSplit = 0;
            int landings = 0, landingsStepped = 0, landingsOffHeart = 0;
            int islands = 0;

            for (int i = 0; i < Seeds; i++)
            {
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                int n = d.Size;
                islands++;

                // Pass ground, as connected components.
                var seen = new bool[n, n];
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.Pass[x, z] || seen[x, z] || !d.HasLand(x, z)) continue;
                    var cells = new List<Vector2I>();
                    var queue = new Queue<Vector2I>();
                    queue.Enqueue(new Vector2I(x, z));
                    seen[x, z] = true;
                    while (queue.Count > 0)
                    {
                        Vector2I c = queue.Dequeue();
                        cells.Add(c);
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                            if (!InBounds(n, nx, nz) || seen[nx, nz] || !d.Pass[nx, nz] || !d.HasLand(nx, nz)) continue;
                            seen[nx, nz] = true;
                            queue.Enqueue(new Vector2I(nx, nz));
                        }
                    }

                    passes++;
                    int steps = 0;
                    var areas = new HashSet<int>();
                    foreach (Vector2I c in cells)
                    {
                        if (d.Walk[c.X, c.Y] >= 0) areas.Add(d.Walk[c.X, c.Y]);
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                            if (!InBounds(n, nx, nz) || !Ground(d, nx, nz) || !Ground(d, c.X, c.Y)) continue;
                            if (Math.Abs(Cross(d, c.X, c.Y) - Cross(d, nx, nz)) == 2) steps++;
                        }
                    }
                    if (steps > 0) passesStepped++;
                    if (areas.Count > 1) passesSplit++;
                }

                // Gate landings.
                foreach (Gate g in d.Gates)
                {
                    landings++;
                    int steps = 0;
                    var areas = new HashSet<int>();
                    for (int x = 0; x < n; x++)
                    for (int z = 0; z < n; z++)
                    {
                        if (!d.Landings[x, z]) continue;
                        if (Math.Abs(x - g.Apron.X) + Math.Abs(z - g.Apron.Y) > 3) continue;
                        if (d.Walk[x, z] >= 0) areas.Add(d.Walk[x, z]);
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + Dx[k], nz = z + Dz[k];
                            if (!InBounds(n, nx, nz) || !Ground(d, nx, nz) || !Ground(d, x, z)) continue;
                            if (Math.Abs(Cross(d, x, z) - Cross(d, nx, nz)) == 2) steps++;
                        }
                    }
                    if (steps > 0) landingsStepped++;
                    bool onHeart = false;
                    foreach (int a in areas)
                        if (a >= 0 && a < d.Areas.Count && d.Heartland >= 0
                            && d.Reach[d.Areas[a].Seat.X, d.Areas[a].Seat.Y] == d.Heartland) onHeart = true;
                    if (!onHeart) landingsOffHeart++;
                }
            }

            GD.Print($"=== {size}², {islands} islands: the works a two-slab step would spoil ===");
            GD.Print($"  passes: {passes} cut, {passesStepped} with a two-slab step on their own ground, "
                + $"{passesSplit} whose ground is more than one walk area");
            GD.Print($"  gate landings: {landings}, {landingsStepped} with a two-slab step at the strip's edge, "
                + $"{landingsOffHeart} whose strip is not on the heartland");
        }
    }

    // ---- 3d. what a second run of the resolver would take out --------------

    /// <summary>
    /// <c>StepGrammar.ResolveAmbiguousSteps</c>, replayed read-only over the finished
    /// ground: its own guards (never a mountain, never sculpted ground, never into the
    /// water beside it, never within a cliff of a basin floor) plus the river stage's
    /// <c>keep</c>, which it would need. Says how many two-slab steps a second run
    /// would remove and how much ground it would move to do it.
    /// </summary>
    private void WhatIf()
    {
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            long before = 0, after = 0, moved = 0, slabs = 0;
            long beforeOff = 0, afterOff = 0;
            var wouldGo = new Dictionary<string, int>();

            for (int i = 0; i < Seeds; i++)
            {
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                int n = d.Size;
                var held = Held(d);
                var h = new short[n, n];
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    h[x, z] = d.HasLand(x, z) ? d.Spans[x, z][0].Top : IslandData.NoLand;

                var was = (short[,])h.Clone();

                bool Subject(int x, int z)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) return false;
                    var form = (LandformType)d.Landform[x, z];
                    if (form == LandformType.Mountain || !Limited(form)) return false;   // sculpted ground is exempt
                    if (d.Canyon[x, z] || d.Pass[x, z] || held[x, z] || d.Landings[x, z]) return false;
                    return true;
                }

                for (int pass = 0; pass < 16; pass++)
                {
                    bool changed = false;
                    for (int x = 0; x < n; x++)
                    for (int z = 0; z < n; z++)
                    {
                        if (!Subject(x, z)) continue;
                        int keepAbove = BasinFloorNear(d, x, z);
                        for (int k = 0; k < 4; k++)
                        {
                            int wx = x + Dx[k], wz = z + Dz[k];
                            if (!InBounds(n, wx, wz) || !d.HasLand(wx, wz)) continue;
                            if (d.WaterLevel[wx, wz] != IslandData.NoLand)
                                keepAbove = Math.Max(keepAbove, d.WaterLevel[wx, wz] + 1);
                        }
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + Dx[k], nz = z + Dz[k];
                            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                            var form = (LandformType)d.Landform[nx, nz];
                            if (form == LandformType.Mountain || !Limited(form)) continue;
                            if (h[x, z] - h[nx, nz] == 2 && h[x, z] - 1 >= keepAbove)
                            {
                                h[x, z]--;
                                changed = true;
                            }
                        }
                    }
                    if (!changed) break;
                }

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (was[x, z] == IslandData.NoLand || h[x, z] == was[x, z]) continue;
                    moved++;
                    slabs += was[x, z] - h[x, z];
                }

                // Two-slab pairs before and after, the ground read the same way both times.
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!Ground(d, x, z)) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                        if (!InBounds(n, nx, nz) || !Ground(d, nx, nz)) continue;
                        bool onMountain = (LandformType)d.Landform[x, z] == LandformType.Mountain
                                          || (LandformType)d.Landform[nx, nz] == LandformType.Mountain;

                        int had = Math.Abs(Level(d, was, x, z) - Level(d, was, nx, nz));
                        int now = Math.Abs(Level(d, h, x, z) - Level(d, h, nx, nz));
                        if (had == 2) { before++; if (!onMountain) beforeOff++; }
                        if (now == 2) { after++; if (!onMountain) afterOff++; }
                        if (had == 2 && now != 2) Bump(wouldGo, FormPair(d, x, z, nx, nz));
                    }
                }
            }

            GD.Print($"=== {size}²: a second run of the resolver, over {Seeds} islands ===");
            GD.Print($"  two-slab steps {before} -> {after} (off the mountains {beforeOff} -> {afterOff})");
            GD.Print($"  ground moved: {moved} cells, {slabs} slabs in all");
            GD.Print("  what goes, by landform pair: " + Top(wouldGo, 8));
        }
    }

    /// <summary>A stream reads as its water, everything else as the ground in <paramref name="h"/>.</summary>
    private static int Level(IslandData d, short[,] h, int x, int z)
        => d.River[x, z] && !d.Navigable[x, z] ? d.WaterLevel[x, z] : h[x, z];

    private static int BasinFloorNear(IslandData d, int x, int z)
    {
        if ((LandformType)d.Landform[x, z] == LandformType.Basin) return int.MinValue;
        int n = d.Size, floor = int.MinValue;
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int nx = x + dx, nz = z + dz;
            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
            if ((LandformType)d.Landform[nx, nz] != LandformType.Basin) continue;
            floor = Math.Max(floor, d.Spans[nx, nz][0].Top + 3);
        }
        return floor;
    }

    // ---- 3e. the proposed ladder: 1 free, 2-3 an impasse, 4+ a cliff ---------

    /// <summary>Rough country, where multi-slab faces are the point: mountains, the sculpted landforms, canyons.</summary>
    private static bool Rough(IslandData d, int x, int z)
    {
        var f = (LandformType)d.Landform[x, z];
        return f is LandformType.Mountain or LandformType.Massif or LandformType.Karst
                 or LandformType.Badlands or LandformType.Sinkholes
               || d.Canyon[x, z];
    }

    /// <summary>
    /// Every step of 2, 3 and 4+ slabs, split rough and not, with the causes of the
    /// 3s outside rough country, the stage each first stood at, and the waterside
    /// case: dry ground exactly three over the water beside it.
    /// </summary>
    private void Threes()
    {
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            long pairs = 0;
            var rough = new long[6];
            var soft = new long[6];
            var threeCause = new long[Causes.Length];
            var threeForms = new Dictionary<string, int>();
            var threeStage = new Dictionary<string, int>();
            long wetThree = 0, wetTwo = 0, waterside = 0, wetThreeNav = 0, wetThreeStream = 0, wetThreeStill = 0;
            var perIsland = new List<int>();
            var snapshots = new List<(string Stage, short[,] Level)>();

            for (int i = 0; i < Seeds; i++)
            {
                snapshots.Clear();
                IslandGenerator.OnStage = (name, view) =>
                {
                    int n = view.Size;
                    var level = new short[n, n];
                    for (int x = 0; x < n; x++)
                    for (int z = 0; z < n; z++)
                    {
                        level[x, z] = IslandData.NoLand;
                        if (!view.Land[x, z]) continue;
                        if (view.Surface != null)
                        {
                            short w = view.Water?[x, z] ?? IslandData.NoLand;
                            level[x, z] = w != IslandData.NoLand ? w : view.Surface[x, z];
                        }
                        else if (view.Data.Spans?[x, z] is { Length: > 0 } spans)
                        {
                            short w = view.Data.WaterLevel[x, z];
                            level[x, z] = w != IslandData.NoLand ? w : spans[0].Top;
                        }
                    }
                    if (name == "footprint") snapshots.Clear();
                    snapshots.Add((name, level));
                };
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                IslandGenerator.OnStage = null;

                int n2 = d.Size;
                var landing = d.Landings;
                var heads = new HashSet<Vector2I>();
                foreach (Crossing c in d.Bridges) { heads.Add(c.A); heads.Add(c.B); }
                int here = 0;

                for (int x = 0; x < n2; x++)
                for (int z = 0; z < n2; z++)
                {
                    if (!Ground(d, x, z)) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                        if (!InBounds(n2, nx, nz) || !Ground(d, nx, nz)) continue;
                        pairs++;
                        int diff = Math.Abs(Cross(d, x, z) - Cross(d, nx, nz));
                        bool isRough = Rough(d, x, z) || Rough(d, nx, nz);
                        (isRough ? rough : soft)[Math.Min(5, diff)]++;
                        if (isRough || diff != 3) continue;

                        here++;
                        int cause = Cause(d, x, z, nx, nz, landing, heads);
                        threeCause[cause]++;
                        Bump(threeForms, FormPair(d, x, z, nx, nz));

                        string when = "before the surface";
                        for (int s2 = snapshots.Count - 1; s2 >= 0; s2--)
                        {
                            var (stage, level) = snapshots[s2];
                            short a = level[x, z], b = level[nx, nz];
                            if (a == IslandData.NoLand || b == IslandData.NoLand) break;
                            if (Math.Abs(a - b) != 3) { when = s2 == snapshots.Count - 1 ? "after the last stage" : snapshots[s2 + 1].Stage; break; }
                            when = stage;
                        }
                        Bump(threeStage, when);
                    }
                }
                perIsland.Add(here);

                // Dry ground over the water beside it: 2 and 3, the navigable banks the pair measure cannot see.
                for (int x = 0; x < n2; x++)
                for (int z = 0; z < n2; z++)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                    short top = d.Spans[x, z][0].Top;
                    short highest = IslandData.NoLand;
                    int kind = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        if (!InBounds(n2, nx, nz) || !d.HasLand(nx, nz)) continue;
                        short w = d.WaterLevel[nx, nz];
                        if (w == IslandData.NoLand || d.Fluid[nx, nz] == (byte)FluidKind.Goo) continue;
                        if (highest == IslandData.NoLand || w > highest)
                        {
                            highest = w;
                            kind = d.Navigable[nx, nz] ? 2 : d.River[nx, nz] ? 1 : 3;
                        }
                    }
                    if (highest == IslandData.NoLand || Rough(d, x, z)) continue;
                    waterside++;
                    if (top - highest == 2) wetTwo++;
                    if (top - highest == 3)
                    {
                        wetThree++;
                        if (kind == 2) wetThreeNav++; else if (kind == 1) wetThreeStream++; else wetThreeStill++;
                    }
                }
            }

            GD.Print($"=== {size}², {Seeds} islands, {pairs} ground pairs ===");
            GD.Print("  rough country: " + Histogram(rough));
            GD.Print("  everywhere else: " + Histogram(soft));
            perIsland.Sort();
            long threes = 0; foreach (long v in threeCause) threes += v;
            GD.Print($"  3-slab steps outside rough country: {threes}, per island median {Med(perIsland)} max {perIsland[^1]}");
            for (int c = 0; c < Causes.Length; c++)
                if (threeCause[c] > 0) GD.Print($"    {Causes[c],-24} {threeCause[c],6}  {100.0 * threeCause[c] / Math.Max(1, threes):0.0}%");
            GD.Print("    by landform pair: " + Top(threeForms, 8));
            GD.Print("    first stood three at: " + Top(threeStage, 8));
            GD.Print($"  waterside, outside rough country: {waterside} dry cells; {wetTwo} stand two over their highest water, "
                + $"{wetThree} three ({wetThreeNav} a navigable reach, {wetThreeStream} a stream, {wetThreeStill} standing water)");
        }
    }

    // ---- 3f. the cheap road to the new ladder: lower every 2 and 3 outside rough country ----

    /// <summary>
    /// The resolver widened to the proposed ladder and replayed read-only: outside rough
    /// country, a dry cell standing 2 or 3 over a neighbour's effective level (the water
    /// surface where it is flooded) comes down a slab, again and again, never into the
    /// water beside it, never within a full cliff (4) of a basin floor, never on a
    /// bridgehead, landing, pass or canyon. Only lowers, as every such pass does today.
    /// </summary>
    private void WhatIfThrees()
    {
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            var before = new long[6];
            var after = new long[6];
            long wet2Before = 0, wet3Before = 0, wet2After = 0, wet3After = 0;
            long moved = 0, slabs = 0, deepest = 0;
            var residue = new Dictionary<string, int>();

            for (int i = 0; i < Seeds; i++)
            {
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                int n = d.Size;
                var held = Held(d);
                var h = new short[n, n];
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    h[x, z] = d.HasLand(x, z) ? d.Spans[x, z][0].Top : IslandData.NoLand;
                var was = (short[,])h.Clone();

                int Eff(short[,] g, int x, int z)
                    => d.WaterLevel[x, z] != IslandData.NoLand ? d.WaterLevel[x, z] : g[x, z];

                bool Subject(int x, int z)
                    => d.HasLand(x, z) && d.WaterLevel[x, z] == IslandData.NoLand && !Rough(d, x, z)
                       && !d.Pass[x, z] && !held[x, z] && !d.Landings[x, z];

                for (int pass = 0; pass < 48; pass++)
                {
                    bool changed = false;
                    for (int x = 0; x < n; x++)
                    for (int z = 0; z < n; z++)
                    {
                        if (!Subject(x, z)) continue;
                        int floor = int.MinValue;
                        bool inBasin = (LandformType)d.Landform[x, z] == LandformType.Basin;
                        for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int nx = x + dx, nz = z + dz;
                            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                            if (!inBasin && (LandformType)d.Landform[nx, nz] == LandformType.Basin)
                                floor = Math.Max(floor, h[nx, nz] + 4);
                            if ((dx == 0 || dz == 0) && d.WaterLevel[nx, nz] != IslandData.NoLand)
                                floor = Math.Max(floor, d.WaterLevel[nx, nz] + 1);
                        }
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + Dx[k], nz = z + Dz[k];
                            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                            if (Rough(d, nx, nz)) continue;
                            int gap = h[x, z] - Eff(h, nx, nz);
                            if ((gap == 2 || gap == 3) && h[x, z] - 1 >= floor)
                            {
                                h[x, z]--;
                                changed = true;
                                break;
                            }
                        }
                    }
                    if (!changed) break;
                }

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (was[x, z] == IslandData.NoLand || h[x, z] == was[x, z]) continue;
                    moved++;
                    slabs += was[x, z] - h[x, z];
                    deepest = Math.Max(deepest, was[x, z] - h[x, z]);
                }

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!Ground(d, x, z)) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                        if (!InBounds(n, nx, nz) || !Ground(d, nx, nz)) continue;
                        if (Rough(d, x, z) || Rough(d, nx, nz)) continue;
                        int had = Math.Abs(Level(d, was, x, z) - Level(d, was, nx, nz));
                        int now = Math.Abs(Level(d, h, x, z) - Level(d, h, nx, nz));
                        before[Math.Min(5, had)]++;
                        after[Math.Min(5, now)]++;
                        if (now == 2 || now == 3)
                        {
                            string why = d.Pass[x, z] || d.Pass[nx, nz] ? "pass"
                                : held[x, z] || held[nx, nz] ? "bridgehead or goo"
                                : d.Landings[x, z] || d.Landings[nx, nz] ? "gate landing"
                                : d.River[x, z] || d.River[nx, nz] ? "stream"
                                : "pinned (water or basin)";
                            Bump(residue, $"{now}: {why}");
                        }
                    }
                }

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand || Rough(d, x, z)) continue;
                    int top = HighestWaterLevel(d, x, z);
                    if (top == int.MinValue) continue;
                    int g0 = was[x, z] - top, g1 = h[x, z] - top;
                    if (g0 == 2) wet2Before++; if (g0 == 3) wet3Before++;
                    if (g1 == 2) wet2After++; if (g1 == 3) wet3After++;
                }
            }

            GD.Print($"=== {size}², {Seeds} islands: every 2 and 3 outside rough country lowered ===");
            GD.Print($"  steps outside rough, before: 2s {before[2]}, 3s {before[3]}, 4s {before[4]}, 5+ {before[5]}");
            GD.Print($"  steps outside rough, after:  2s {after[2]}, 3s {after[3]}, 4s {after[4]}, 5+ {after[5]}");
            GD.Print($"  dry ground over its water: 2 over {wet2Before} -> {wet2After}, 3 over {wet3Before} -> {wet3After}");
            GD.Print($"  ground moved: {moved} cells, {slabs} slabs, the deepest cut {deepest} slabs");
            GD.Print("  what is left: " + Top(residue, 8));
        }
    }

    /// <summary>
    /// The river half alone: <c>CutBanks</c> widened to the proposed ladder. A dry cell
    /// 2 or 3 over the water beside it comes down to the free step; the correction walks
    /// outward from the cells it cut, taking any neighbour left 2 or 3 above one, and
    /// touches nothing it did not reach. Outside rough country, never a bridgehead,
    /// landing or pass, never into water. Also counts the falls by drop, for FallDepth.
    /// </summary>
    private void WhatIfWater()
    {
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            var before = new long[6];
            var after = new long[6];
            long wet2Before = 0, wet3Before = 0, wet2After = 0, wet3After = 0;
            long moved = 0, slabs = 0, deepest = 0;
            var drops = new long[6];
            long falls = 0;

            for (int i = 0; i < Seeds; i++)
            {
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                int n = d.Size;
                var held = Held(d);
                var h = new short[n, n];
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    h[x, z] = d.HasLand(x, z) ? d.Spans[x, z][0].Top : IslandData.NoLand;
                var was = (short[,])h.Clone();

                bool Subject(int x, int z)
                    => InBounds(n, x, z) && d.HasLand(x, z) && d.WaterLevel[x, z] == IslandData.NoLand
                       && !Rough(d, x, z) && !d.Pass[x, z] && !held[x, z] && !d.Landings[x, z];

                int Floor(int x, int z)
                {
                    int floor = int.MinValue;
                    bool inBasin = (LandformType)d.Landform[x, z] == LandformType.Basin;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                        if (!inBasin && (LandformType)d.Landform[nx, nz] == LandformType.Basin)
                            floor = Math.Max(floor, h[nx, nz] + 4);
                        if ((dx == 0 || dz == 0) && d.WaterLevel[nx, nz] != IslandData.NoLand)
                            floor = Math.Max(floor, d.WaterLevel[nx, nz] + 1);
                    }
                    return floor;
                }

                var queue = new Queue<Vector2I>();
                void Cut(int x, int z)
                {
                    int floor = Floor(x, z);
                    if (h[x, z] - 1 < floor) return;
                    h[x, z]--;
                    int top = HighestWaterLevel(d, x, z);
                    while (top != int.MinValue && h[x, z] - top is 2 or 3 && h[x, z] - 1 >= floor) h[x, z]--;
                    queue.Enqueue(new Vector2I(x, z));
                }

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!Subject(x, z)) continue;
                    int top = HighestWaterLevel(d, x, z);
                    if (top != int.MinValue && h[x, z] - top is 2 or 3) Cut(x, z);
                }
                while (queue.Count > 0)
                {
                    Vector2I c = queue.Dequeue();
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                        if (!Subject(nx, nz)) continue;
                        if (h[nx, nz] - h[c.X, c.Y] is 2 or 3) Cut(nx, nz);
                    }
                }

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (was[x, z] == IslandData.NoLand || h[x, z] == was[x, z]) continue;
                    moved++;
                    slabs += was[x, z] - h[x, z];
                    deepest = Math.Max(deepest, was[x, z] - h[x, z]);
                }

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!Ground(d, x, z)) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                        if (!InBounds(n, nx, nz) || !Ground(d, nx, nz)) continue;
                        if (Rough(d, x, z) || Rough(d, nx, nz)) continue;
                        before[Math.Min(5, Math.Abs(Level(d, was, x, z) - Level(d, was, nx, nz)))]++;
                        after[Math.Min(5, Math.Abs(Level(d, h, x, z) - Level(d, h, nx, nz)))]++;
                    }
                }

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand || Rough(d, x, z)) continue;
                    int top = HighestWaterLevel(d, x, z);
                    if (top == int.MinValue) continue;
                    int g0 = was[x, z] - top, g1 = h[x, z] - top;
                    if (g0 == 2) wet2Before++; if (g0 == 3) wet3Before++;
                    if (g1 == 2) wet2After++; if (g1 == 3) wet3After++;
                }

                foreach (Fall f in d.Falls)
                {
                    if (f.OffRim) continue;
                    int tx = f.Cell.X + f.Flow.X, tz = f.Cell.Y + f.Flow.Y;
                    if (!InBounds(n, tx, tz) || d.WaterLevel[tx, tz] == IslandData.NoLand) continue;
                    falls++;
                    drops[Math.Min(5, d.WaterLevel[f.Cell.X, f.Cell.Y] - d.WaterLevel[tx, tz])]++;
                }
            }

            GD.Print($"=== {size}², {Seeds} islands: the river half only ===");
            GD.Print($"  steps outside rough, before: 2s {before[2]}, 3s {before[3]}, 4s {before[4]}, 5+ {before[5]}");
            GD.Print($"  steps outside rough, after:  2s {after[2]}, 3s {after[3]}, 4s {after[4]}, 5+ {after[5]}");
            GD.Print($"  dry ground over its water: 2 over {wet2Before} -> {wet2After}, 3 over {wet3Before} -> {wet3After}");
            GD.Print($"  ground moved: {moved} cells, {slabs} slabs, the deepest cut {deepest} slabs");
            GD.Print($"  on-Domain falls into water: {falls}; by drop 3: {drops[3]}, 4: {drops[4]}, 5+: {drops[5]}");
        }
    }

    /// <summary>
    /// The 2- and 3-slab steps across a region border outside rough country, by the
    /// landform pair either side, split into mesa or basin beside its own kind (the
    /// "half a step over a placed mesa" rule) and everything else.
    /// </summary>
    private void Borders()
    {
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            var two = new Dictionary<string, int>();
            var three = new Dictionary<string, int>();
            long cluster2 = 0, cluster3 = 0, other2 = 0, other3 = 0, border4 = 0;
            for (int i = 0; i < Seeds; i++)
            {
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                int n = d.Size;
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!Ground(d, x, z) || d.River[x, z]) continue;
                    for (int k = 0; k < 2; k++)
                    {
                        int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                        if (!InBounds(n, nx, nz) || !Ground(d, nx, nz) || d.River[nx, nz]) continue;
                        if (d.Region[x, z] == d.Region[nx, nz]) continue;
                        if (Rough(d, x, z) || Rough(d, nx, nz) || d.Pass[x, z] || d.Pass[nx, nz]) continue;
                        int diff = Math.Abs(d.Spans[x, z][0].Top - d.Spans[nx, nz][0].Top);
                        if (diff >= 4) { border4++; continue; }
                        if (diff < 2) continue;
                        var a = (LandformType)d.Landform[x, z];
                        var b = (LandformType)d.Landform[nx, nz];
                        bool cluster = a == b && a is LandformType.Mesa or LandformType.Basin;
                        string pair = FormPair(d, x, z, nx, nz);
                        if (diff == 2) { Bump(two, pair); if (cluster) cluster2++; else other2++; }
                        else { Bump(three, pair); if (cluster) cluster3++; else other3++; }
                    }
                }
            }
            GD.Print($"=== {size}²: region borders outside rough country ({border4} are 4+) ===");
            GD.Print($"  2s: {cluster2} mesa or basin beside its own kind, {other2} other   " + Top(two, 8));
            GD.Print($"  3s: {cluster3} mesa or basin beside its own kind, {other3} other   " + Top(three, 8));
        }
    }

    private static int HighestWaterLevel(IslandData d, int x, int z)
    {
        int n = d.Size, top = int.MinValue;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + Dx[k], nz = z + Dz[k];
            if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
            if (d.WaterLevel[nx, nz] == IslandData.NoLand || d.Fluid[nx, nz] == (byte)FluidKind.Goo) continue;
            top = Math.Max(top, d.WaterLevel[nx, nz]);
        }
        return top;
    }

    // ---- 4. every arrangement x character ----------------------------------

    private void Matrix()
    {
        GD.Print($"=== arrangement x character at {Params.Size}², {Seeds} seeds each: two-slab % and the interior share ===");
        var byArrangement = new Dictionary<string, (long Two, long Pairs, long Interior)>();
        var byCharacter = new Dictionary<string, (long Two, long Pairs, long Interior)>();

        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            foreach (TerrainCharacter c in Enum.GetValues<TerrainCharacter>())
            {
                if (c == TerrainCharacter.Auto) continue;
                var p = Copy(Params.Size);
                p.Arrangement = how;
                p.Character = c;
                var t = new StepTally();
                for (int i = 0; i < Seeds; i++) Measure(SeedAt(i), IslandGenerator.Generate(SeedAt(i), p), t);
                long pairs = t.Free + t.Two + t.Cliff;
                long interior = t.Cause[14];
                Add(byArrangement, how.ToString(), t.Two, pairs, interior);
                Add(byCharacter, c.ToString(), t.Two, pairs, interior);
            }
        }

        GD.Print("  by character:");
        foreach (var (name, v) in Sorted(byCharacter))
            GD.Print($"    {name,-12} two-slab {100.0 * v.Two / v.Pairs:0.00}%   interior {v.Interior,5} ({100.0 * v.Interior / Math.Max(1, v.Two):0.0}% of them)");
        GD.Print("  by arrangement:");
        foreach (var (name, v) in Sorted(byArrangement))
            GD.Print($"    {name,-14} two-slab {100.0 * v.Two / v.Pairs:0.00}%   interior {v.Interior,5} ({100.0 * v.Interior / Math.Max(1, v.Two):0.0}% of them)");
    }

    private static void Add(Dictionary<string, (long Two, long Pairs, long Interior)> into, string key,
                            long two, long pairs, long interior)
    {
        into.TryGetValue(key, out var had);
        into[key] = (had.Two + two, had.Pairs + pairs, had.Interior + interior);
    }

    private static List<(string, (long Two, long Pairs, long Interior))> Sorted(
        Dictionary<string, (long Two, long Pairs, long Interior)> from)
    {
        var list = new List<(string, (long Two, long Pairs, long Interior))>();
        foreach (var kv in from) list.Add((kv.Key, kv.Value));
        list.Sort((a, b) => (100.0 * b.Item2.Two / b.Item2.Pairs).CompareTo(100.0 * a.Item2.Two / a.Item2.Pairs));
        return list;
    }

    // ---- 5. the knobs ------------------------------------------------------

    private void Knobs()
    {
        var knobs = new (string Name, Action<IslandParams, float> Set)[]
        {
            ("fjords", (p, v) => p.Fjords = v),
            ("relief", (p, v) => p.Relief = v),
            ("hilliness", (p, v) => p.Hilliness = v),
            ("mix", (p, v) => p.LandformMix = v),
            ("rivers", (p, v) => p.Rivers = v),
            ("lakes", (p, v) => p.Lakes = v),
            ("valleys", (p, v) => p.Valleys = v),
            ("moisture", (p, v) => p.Moisture = v),
            ("warmth", (p, v) => p.Warmth = v),
            ("wind", (p, v) => p.Wind = v),
            ("overhangs", (p, v) => p.OverhangDensity = v),
            ("magick", (p, v) => p.MagickDensity = v),
        };

        GD.Print($"=== the two-slab rate as each knob is swept, {Seeds} seeds at 128², others rolled ===");
        foreach (var (name, set) in knobs)
        {
            var cells = new List<string>();
            foreach (float v in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                var t = new StepTally();
                for (int i = 0; i < Seeds; i++)
                {
                    var p = Copy(128);
                    set(p, v);
                    Measure(SeedAt(i), IslandGenerator.Generate(SeedAt(i), p), t);
                }
                long pairs = t.Free + t.Two + t.Cliff;
                cells.Add($"{100.0 * t.Two / pairs:0.00}");
            }
            GD.Print($"  {name,-10} {string.Join("  ", cells)}");
        }
    }

    // ---- 6. which stage makes them ----------------------------------------

    private void Stages()
    {
        var order = new List<string>();
        var made = new Dictionary<string, int>();
        var snapshots = new List<(string Stage, short[,] Level)>();
        int total = 0;

        var p = Copy(128);
        for (int i = 0; i < Seeds; i++)
        {
            snapshots.Clear();
            IslandGenerator.OnStage = (name, view) =>
            {
                int n = view.Size;
                var level = new short[n, n];
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    level[x, z] = IslandData.NoLand;
                    if (!view.Land[x, z]) continue;
                    if (view.Surface != null)
                    {
                        short water = view.Water?[x, z] ?? IslandData.NoLand;
                        level[x, z] = water != IslandData.NoLand ? water : view.Surface[x, z];
                    }
                    else if (view.Data.Spans?[x, z] is { Length: > 0 } spans)
                    {
                        short water = view.Data.WaterLevel[x, z];
                        level[x, z] = water != IslandData.NoLand ? water : spans[0].Top;
                    }
                }
                // A re-rolled seed starts over at "footprint": keep the last attempt only.
                if (name == "footprint") snapshots.Clear();
                snapshots.Add((name, level));
            };

            IslandData d = IslandGenerator.Generate(SeedAt(i), p);
            IslandGenerator.OnStage = null;

            int n = d.Size;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Ground(d, x, z)) continue;
                for (int k = 0; k < 2; k++)
                {
                    int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                    if (!InBounds(n, nx, nz) || !Ground(d, nx, nz)) continue;
                    if (Math.Abs(Cross(d, x, z) - Cross(d, nx, nz)) != 2) continue;

                    total++;
                    string when = "before the surface";   // there from the first stage that had one
                    for (int s = snapshots.Count - 1; s >= 0; s--)
                    {
                        var (stage, level) = snapshots[s];
                        short a = level[x, z], b = level[nx, nz];
                        if (a == IslandData.NoLand || b == IslandData.NoLand) break;
                        if (Math.Abs(a - b) != 2) { when = stage == snapshots[^1].Stage ? "after the last stage" : snapshots[s + 1].Stage; break; }
                        when = stage;
                    }
                    if (!made.ContainsKey(when)) order.Add(when);
                    made.TryGetValue(when, out int had);
                    made[when] = had + 1;
                }
            }
        }

        GD.Print($"=== the stage each two-slab step first stood two at, {Seeds} islands at 128² ===");
        var list = new List<(string, int)>();
        foreach (var kv in made) list.Add((kv.Key, kv.Value));
        list.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        foreach (var (stage, count) in list)
            GD.Print($"  {stage,-22} {count,6}  {100.0 * count / Math.Max(1, total):0.0}%");
    }

    // ---- the two anchor triplets, recomputed from the geometry ---------------

    /// <summary>
    /// Rebuilds the four face lists from the columns alone — every dry cell, every
    /// cardinal land neighbour, the effective surface either side — and compares them
    /// to what <c>Surfaces</c> wrote, list for list and in order; checks every ladder
    /// and stair on every road against the rise it climbs; and reports what the new
    /// triplet claims that the old ladder left to nobody.
    /// </summary>
    private void AnchorCheck()
    {
        long wrongCliff = 0, wrongCliffFoot = 0, wrongImpasse = 0, wrongImpasseFoot = 0;
        long cliff = 0, cliffFoot = 0, impasse = 0, impasseFoot = 0;
        long cliffLedge = 0, impasseLedge = 0, bothBrinks = 0, bothFeet = 0, crossLedge = 0;
        long ladders = 0, stairs = 0, badLadder = 0, badStair = 0;
        long waterside = 0, watersideImpasse = 0, watersideMissed = 0, bareWaterside = 0;
        long oldCliffNowImpasse = 0;
        int islands = 0;
        var examples = new List<string>();

        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            for (int i = 0; i < Seeds; i++)
            {
                int seed = SeedAt(i);
                IslandData d = IslandGenerator.Generate(seed, p);
                int n = d.Size;
                islands++;

                var eCliff = new List<Vector2I>();
                var eCliffFoot = new List<Vector2I>();
                var eImpasse = new List<Vector2I>();
                var eImpasseFoot = new List<Vector2I>();

                int Eff(int x, int z) => d.WaterLevel[x, z] != IslandData.NoLand ? d.WaterLevel[x, z] : d.Spans[x, z][0].Top;

                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                    int here = Eff(x, z);
                    bool cb = false, cf = false, ib = false, iff = false;
                    int maxDown = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                        int down = here - Eff(nx, nz);
                        maxDown = Math.Max(maxDown, down);
                        if (down >= 4) cb = true;
                        if (-down >= 4) cf = true;
                        if (down is 2 or 3) ib = true;
                        if (-down is 2 or 3) iff = true;
                    }
                    var c = new Vector2I(x, z);
                    if (cb) eCliff.Add(c);
                    if (cf) eCliffFoot.Add(c);
                    if (ib) eImpasse.Add(c);
                    if (iff) eImpasseFoot.Add(c);
                    if (maxDown == 3) oldCliffNowImpasse++;   // a brink under the old ladder, only an impasse now
                    if (cb && ib) bothBrinks++;
                    if (cf && iff) bothFeet++;
                    if (cb && cf) cliffLedge++;
                    if (ib && iff && !cb && !cf) impasseLedge++;
                    if ((cb && iff && !cf) || (ib && cf && !cb)) crossLedge++;
                }

                long Diff(List<Vector2I> expected, List<Vector2I> got, string name)
                {
                    long wrong = Math.Abs(expected.Count - got.Count);
                    int m = Math.Min(expected.Count, got.Count);
                    for (int j = 0; j < m; j++) if (expected[j] != got[j]) wrong++;
                    if (wrong > 0 && examples.Count < 8) examples.Add($"{name} on seed {seed} at {size}²");
                    return wrong;
                }
                wrongCliff += Diff(eCliff, d.CliffCells, "cliff brink");
                wrongCliffFoot += Diff(eCliffFoot, d.CliffFootCells, "cliff foot");
                wrongImpasse += Diff(eImpasse, d.ImpasseCells, "impasse brink");
                wrongImpasseFoot += Diff(eImpasseFoot, d.ImpasseFootCells, "impasse foot");
                cliff += d.CliffCells.Count; cliffFoot += d.CliffFootCells.Count;
                impasse += d.ImpasseCells.Count; impasseFoot += d.ImpasseFootCells.Count;

                foreach (Passage road in d.Passages)
                foreach (Works w in road.Built)
                {
                    if (w.Kind == WorksKind.Bridge) continue;
                    int rise = Math.Abs(Traversal.CrossLevel(d, w.To.X, w.To.Y) - Traversal.CrossLevel(d, w.From.X, w.From.Y));
                    if (w.Kind == WorksKind.Ladder) { ladders++; if (rise is < 2 or > 3) badLadder++; }
                    else { stairs++; if (rise < 4 || rise > Traversal.InfrastructureStep) badStair++; }
                }

                // Dry ground two or three over the water beside it: an impasse brink now, every one.
                var impasseSet = new HashSet<Vector2I>(d.ImpasseCells);
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                    int top = d.Spans[x, z][0].Top;
                    bool beside = false, twoOrThree = false;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                        if (d.WaterLevel[nx, nz] == IslandData.NoLand || d.Fluid[nx, nz] == (byte)FluidKind.Goo) continue;
                        beside = true;
                        if (top - d.WaterLevel[nx, nz] is 2 or 3) twoOrThree = true;
                    }
                    if (!beside) continue;
                    waterside++;
                    if (twoOrThree)
                    {
                        if (impasseSet.Contains(new Vector2I(x, z))) watersideImpasse++;
                        else watersideMissed++;
                    }
                    if (AnchorOf(d, x, z) == "nothing") bareWaterside++;
                }
            }
        }

        GD.Print($"=== anchors on {islands} islands (all three footprints), recomputed from the columns ===");
        GD.Print($"  mismatches: cliff brink {wrongCliff}, cliff foot {wrongCliffFoot}, "
            + $"impasse brink {wrongImpasse}, impasse foot {wrongImpasseFoot}");
        if (examples.Count > 0) GD.Print("  first: " + string.Join(";  ", examples));
        GD.Print($"  counts: {cliff} cliff brinks, {cliffFoot} cliff feet, {impasse} impasse brinks, {impasseFoot} impasse feet");
        GD.Print($"  overlaps: {bothBrinks} cells both a cliff and an impasse brink, {bothFeet} both kinds of foot; "
            + $"{cliffLedge} cliff ledges, {impasseLedge} impasse-only ledges, {crossLedge} a brink of one kind and a foot of the other");
        GD.Print($"  dry cells whose tallest drop is exactly three (a cliff brink before, an impasse brink now): {oldCliffNowImpasse}");
        GD.Print($"  roads: {ladders} ladders ({badLadder} not climbing 2-3), {stairs} stairs ({badStair} not climbing 4-{Traversal.InfrastructureStep})");
        GD.Print($"  waterside: {waterside} dry cells beside water; of those two or three over it, {watersideImpasse} are impasse brinks "
            + $"and {watersideMissed} are not; {bareWaterside} waterside cells anchored to nothing");
    }

    // ---- fingerprints: what a relabel must leave alone ------------------------

    /// <summary>
    /// One line per island of hashes by concern, written to <c>out=</c>, so a change meant
    /// to relabel can be diffed against the build before it: terrain, traversal, roads
    /// without the kind of work and with it, materials, and each anchor list.
    /// </summary>
    private void Fingerprint()
    {
        string path = "";
        foreach (string arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("out=")) path = arg[4..];
        var lines = new List<string>();
        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = Copy(size);
            for (int i = 0; i < Seeds; i++)
            {
                int seed = SeedAt(i);
                IslandData d = IslandGenerator.Generate(seed, p);
                int n = d.Size;
                ulong terrain = Fnv0, water = Fnv0, walk = Fnv0, material = Fnv0;
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    Span[] spans = d.Spans[x, z];
                    terrain = Mix(terrain, spans?.Length ?? -1);
                    if (spans != null) foreach (Span sp in spans) { terrain = Mix(terrain, sp.Bottom); terrain = Mix(terrain, sp.Top); }
                    water = Mix(water, d.WaterLevel[x, z]); water = Mix(water, d.Fluid[x, z]);
                    water = Mix(water, d.River[x, z] ? 1 : 0); water = Mix(water, d.Navigable[x, z] ? 1 : 0);
                    water = Mix(water, d.Ford[x, z] ? 1 : 0); water = Mix(water, d.Beach[x, z] ? 1 : 0);
                    water = Mix(water, d.Landings[x, z] ? 1 : 0); water = Mix(water, d.Hot[x, z] ? 1 : 0);
                    walk = Mix(walk, d.Walk[x, z]); walk = Mix(walk, d.Reach[x, z]); walk = Mix(walk, d.WaterBody[x, z]);
                    material = Mix(material, d.Material[x, z]);
                }
                foreach (Fall f in d.Falls) { water = Mix(water, f.Cell.X); water = Mix(water, f.Cell.Y); }

                ulong roads = Fnv0, kinds = Fnv0;
                foreach (Passage r in d.Passages)
                {
                    roads = Mix(roads, r.Exit); roads = Mix(roads, r.Cost); roads = Mix(roads, r.Flights);
                    foreach (Vector2I c in r.Path) { roads = Mix(roads, c.X); roads = Mix(roads, c.Y); }
                    foreach (Works w in r.Built)
                    {
                        roads = Mix(roads, w.From.X); roads = Mix(roads, w.From.Y);
                        roads = Mix(roads, w.To.X); roads = Mix(roads, w.To.Y);
                        roads = Mix(roads, w.Kind == WorksKind.Bridge ? 1 : 0);
                        kinds = Mix(kinds, (int)w.Kind);
                    }
                }
                roads = Mix(roads, d.Rough ? 1 : 0);

                string Cells(List<Vector2I> list)
                {
                    ulong h = Fnv0;
                    foreach (Vector2I c in list) { h = Mix(h, c.X); h = Mix(h, c.Y); }
                    return $"{h:x16}";
                }

                lines.Add($"{size} {seed} terrain={terrain:x16} water={water:x16} walk={walk:x16} "
                    + $"roads={roads:x16} kinds={kinds:x16} material={material:x16} "
                    + $"coast={Cells(d.CoastCells)} bank={Cells(d.BankCells)} riverbed={Cells(d.RiverBedCells)} "
                    + $"lakebed={Cells(d.LakeBedCells)} summits={Cells(d.Summits)} springs={Cells(d.Springs)} "
                    + $"overhangs={Cells(d.Overhangs)} names={string.Join("|", d.WaterNames).GetHashCode() & 0:x} "
                    + $"cliff={Cells(d.CliffCells)} foot={Cells(d.CliffFootCells)}");
            }
        }
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        foreach (string line in lines) file.StoreLine(line);
        GD.Print($"fingerprints: {lines.Count} islands to {path}");
    }

    private const ulong Fnv0 = 14695981039346656037ul;

    private static ulong Mix(ulong h, int v)
    {
        unchecked
        {
            for (int b = 0; b < 4; b++) { h ^= (byte)(v >> (8 * b)); h *= 1099511628211ul; }
            return h;
        }
    }

    // ---- helpers -----------------------------------------------------------

    private IslandParams Copy(int size)
    {
        var p = (IslandParams)Params.Duplicate(true);
        p.Size = size;
        return p;
    }

    private static void Bump(Dictionary<string, int> into, string key)
    {
        into.TryGetValue(key, out int had);
        into[key] = had + 1;
    }

    private static string Top(Dictionary<string, int> from, int count)
    {
        var list = new List<(string Key, int Value)>();
        foreach (var kv in from) list.Add((kv.Key, kv.Value));
        list.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
        var bits = new List<string>();
        for (int i = 0; i < Math.Min(count, list.Count); i++) bits.Add($"{list[i].Key} {list[i].Value}");
        return bits.Count == 0 ? "none" : string.Join(", ", bits);
    }

    /// <summary>A step histogram as shares: 0, 1, 2, 3, 4, 5+ slabs.</summary>
    private static string Histogram(long[] steps)
    {
        long total = 0;
        foreach (long v in steps) total += v;
        if (total == 0) return "none";
        var bits = new List<string>();
        string[] names = { "0", "1", "2", "3", "4", "5+" };
        for (int i = 0; i < steps.Length; i++) bits.Add($"{names[i]} {100.0 * steps[i] / total:0.0}%");
        return string.Join("  ", bits) + $"   (n={total})";
    }

    private static int Med(List<int> values)
    {
        if (values.Count == 0) return 0;
        var copy = new List<int>(values);
        copy.Sort();
        return copy[copy.Count / 2];
    }
}
