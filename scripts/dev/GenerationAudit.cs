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

    /// <summary>
    /// Print an ASCII silhouette of one island per arrangement as well. Shape is
    /// measurable headless where appearance is not, so this is how a change to the
    /// footprint can be checked without opening the lab.
    /// </summary>
    [Export] public bool Silhouettes { get; set; } = false;

    /// <summary>
    /// Print one island's water at full resolution as well: lakes, streams,
    /// navigable reaches and falls, a character to the cell. How a river
    /// <i>runs</i> is a fact about the routing, so it can be checked headless.
    /// </summary>
    [Export] public bool Waterways { get; set; } = false;

    /// <summary>
    /// Print a close-up height map of one patch of each sculpted landform —
    /// badlands, karst, ziggurat, dunes. Their shape is the point of them, and a
    /// median step height cannot tell a maze of gullies from one trench.
    /// </summary>
    [Export] public bool Sculpts { get; set; } = false;

    /// <summary>
    /// Run every arrangement against every character and report which combinations
    /// the pipeline finds hard — see <see cref="PrintFeasibility"/>. Slower than
    /// the rest of the audit put together, so it is opt-in.
    /// </summary>
    [Export] public bool Feasibility { get; set; } = false;

    /// <summary>Seeds per combination in the feasibility sweep.</summary>
    [Export] public int FeasibilitySeeds { get; set; } = 3;

    /// <summary>
    /// Ask for each Entry edge and kind in turn, and each Exit count and kind, and
    /// report how often the Domain delivered what was asked for.
    ///
    /// The Gate parameters are the ones a *neighbouring* Domain sets, so "usually"
    /// is not an answer — a Link whose far end came out on the wrong edge is a
    /// Link that points somewhere else. Nothing else in the audit tests a
    /// parameter against its own request; the rest measures islands generated with
    /// everything on Auto, where every Gate is trivially the one that was asked
    /// for.
    /// </summary>
    [Export] public bool GateRequests { get; set; } = false;

    /// <summary>
    /// Ask every arrangement x character for the hardest Gate request there is —
    /// four hanging Gates, one per edge — and then check that asking for less
    /// works too. See <see cref="PrintGateMatrix"/>.
    /// </summary>
    [Export] public bool GateMatrix { get; set; } = false;

    /// <summary>
    /// Sweep the water knobs — Lakes, Rivers, Valleys — from 0 to 1 and report what
    /// each one actually moves.
    ///
    /// A slider that does not change the island is worse than one that is not
    /// there, and a summary over seeds at one setting cannot tell you which is
    /// which. This holds everything else at the preset and steps one knob, so the
    /// column either climbs or it does not.
    /// </summary>
    [Export] public bool Knobs { get; set; } = false;

    /// <summary>Seeds per setting in the <see cref="GateRequests"/> and <see cref="Knobs"/> sweeps.</summary>
    [Export] public int SweepSeeds { get; set; } = 12;

    /// <summary>
    /// Write this run's headline numbers to <c>docs/audit-baseline.json</c> as the
    /// new accepted answer. Off by default: the audit reports what moved, and
    /// accepting the move is a decision.
    /// </summary>
    [Export] public bool AcceptBaseline { get; set; } = false;

    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };
    private static readonly string[] TypeName =
    {
        "plain", "hills", "mountain", "mesa", "basin", "badlands", "karst",
        "massif", "dunes", "sinkholes",
    };

    /// <summary>How many landform types there are — the audit buckets by all of them.</summary>
    private static readonly int Forms = TypeName.Length;

    public override void _Ready()
    {
        Params ??= new IslandParams();

        long free = 0, ambiguous = 0, cliff = 0;
        long ambiguousOffMountain = 0, pairsOffMountain = 0;
        var cliffByBorder = new Dictionary<string, int>();
        var ambiguousWhere = new Dictionary<string, int>();

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

        int riverCells = 0, navigableCells = 0, fallCells = 0, rimFalls = 0;
        var innerFalls = new List<int>();
        var riverDepth = new List<int>();
        int islandsWithRiver = 0, riverIslandsReachingRim = 0;
        int riverUphill = 0, riverDry = 0;
        var riverPerIsland = new List<int>();
        int riverStraight = 0, riverBends = 0, eyotCells = 0;
        var straightRuns = new List<int>();

        int berths = 0, waterBodies = 0, islandsWithBerth = 0, badQuay = 0;
        int berthSites = 0, islandsNeedingFerry = 0;
        var materialCells = new long[Enum.GetValues<SurfaceMaterial>().Length];
        long coastAnchors = 0, cliffAnchors = 0, beachCells = 0, fordCells = 0, landingCells = 0;
        long beachedCoast = 0;
        int islandsWithoutBeach = 0;
        var quayRise = new List<int>();

        int exitsWithoutRoad = 0, roadsFree = 0, roadJumps = 0, roughIslands = 0, flights = 0;
        int roadStairs = 0, roadBridges = 0, roadFerries = 0;
        var roadCosts = new List<int>();
        var roadLengths = new List<int>();

        var gullyDepths = new List<int>();
        var towerRises = new List<int>();
        var terraceSteps = new List<int>();
        var sinkDepths = new List<int>();

        int lakes = 0, lakeCells = 0, leaks = 0, waterAtVoid = 0, islandsWithLake = 0;
        var shoreSteps = new List<int>();
        var lakeBodySizes = new List<int>();

        int gooCells = 0, gooIslands = 0, gooTouchesWater = 0;

        int gorgeCells = 0, gorgeReaches = 0, gorgeCrossable = 0, gorgeSealed = 0;
        int gorgeMisaligned = 0, gorgeIslands = 0;
        var gorgeLengths = new List<int>();
        var gorgeSealedLengths = new List<int>();
        var gorgeDetours = new List<int>();

        int landmasses = 0, diagonalLand = 0, diagonalWater = 0;
        int overhangCells = 0, overhangIslands = 0;
        var lipAir = new List<int>();

        int noEntry = 0, badExitCount = 0, sharedEdge = 0, wrongEntryKind = 0;
        int gateOffHeartland = 0, gateApronShort = 0, gateInWater = 0;
        int landGates = 0, hangingGates = 0, stripMissing = 0, hangingOnLand = 0;
        int gateInCorner = 0, gateNotOutermost = 0, gatesCrowded = 0;
        var gateBehind = new List<int>();
        int crossings = 0, deckSteep = 0, deckOffBank = 0;
        var crossingSpans = new List<int>();
        var shelfDrops = new List<int>();
        var attempts = new List<int>();
        int unplayable = 0;
        int airstripIslands = 0;
        var airstripCells = new List<int>();
        var exitCounts = new List<int>();
        var gateSpacing = new List<int>();
        var apronSizes = new List<int>();
        var stripLengths = new List<int>();
        var byArrangement = new Dictionary<IslandArrangement, (int Islands, int Masses, int Linked)>();

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
        var strandedByForm = new long[Forms];
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

            // The height you actually cross a column at. A stream's channel is cut
            // one slab below its banks and filled to the old level, so you ford it
            // at the water, not at the bed — measuring the bed would report a
            // two-slab step at every bank that happened to stand a slab proud.
            short Cross(int x, int z)
                => d.River[x, z] && !d.Navigable[x, z] ? d.WaterLevel[x, z] : d.SurfaceLevel(x, z);

            // A navigable river is not ground: two cells wide and meant for
            // barges, it is a gap you bridge, not a step you take.
            bool Ground(int x, int z) => Land(x, z) && !d.Navigable[x, z]
                                         && (d.River[x, z] || d.WaterLevel[x, z] == IslandData.NoLand);
            LandformType Form(int x, int z) => (LandformType)d.Landform[x, z];

            // ---- step grammar, and where cliffs fall -------------------------
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Ground(x, z)) continue;
                for (int k = 0; k < 2; k++)                     // +X and +Z: each pair once
                {
                    int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                    if (!Ground(nx, nz)) continue;

                    int diff = Math.Abs(Cross(x, z) - Cross(nx, nz));
                    if (diff <= 1) free++;
                    else if (diff == 2) ambiguous++;
                    else cliff++;

                    bool mountain = Form(x, z) == LandformType.Mountain
                                    || Form(nx, nz) == LandformType.Mountain;
                    if (!mountain)
                    {
                        pairsOffMountain++;
                        if (diff == 2)
                        {
                            ambiguousOffMountain++;
                            // Where the ambiguous step is, rather than only how
                            // many there are: a bank the river was not allowed to
                            // cut reads very differently from one the terrain rules
                            // left behind.
                            int a2 = (int)Form(x, z), b2 = (int)Form(nx, nz);
                            string where = d.River[x, z] || d.River[nx, nz]
                                ? "riverbank"
                                : $"{TypeName[Math.Min(a2, b2)]}-{TypeName[Math.Max(a2, b2)]}";
                            ambiguousWhere.TryGetValue(where, out int had);
                            ambiguousWhere[where] = had + 1;
                        }
                    }

                    if (diff >= 3 && d.Region[x, z] != d.Region[nx, nz])
                    {
                        int a = (int)Form(x, z), b = (int)Form(nx, nz);
                        // A canyon wall is a cliff no rule forbids: the trench is cut
                        // deliberately, and across any pair of patches. Bucket it as
                        // itself, or it reads as a leak in the landform rules.
                        // A mountain flank is the mountain: massifs take no rung, so
                        // the slope limiter never binds their borders and a steep
                        // face there is the landform, not a leak.
                        string key = d.Canyon[x, z] || d.Canyon[nx, nz]
                            ? "canyon (any pair)"
                            : a == (int)LandformType.Mountain || b == (int)LandformType.Mountain
                            ? "mountain flank"
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
                    // Measured against the ground, not against a channel cut
                    // through it: a river beside a basin runs below the basin
                    // floor by design, and counting its bed would report the
                    // escarpment as inverted.
                    if (d.River[nx, nz] || d.River[x, z]) continue;
                    int delta = Top(x, z) - Top(nx, nz);
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
            // Goo is standing fluid and gets the same physics checks — a leak is
            // a leak whatever stands over it — but it is not a lake, ignores the
            // Lakes knob, and does not belong in the lake counts.
            var lakeRegions = new HashSet<int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                short w = d.WaterLevel[x, z];
                if (w == IslandData.NoLand || d.River[x, z]) continue;
                bool watery = d.Fluid[x, z] == (byte)FluidKind.Water;

                if (watery)
                {
                    lakeCells++;
                    lakeRegions.Add(d.Region[x, z]);
                }

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Land(nx, nz)) { waterAtVoid++; continue; }
                    if (d.WaterLevel[nx, nz] != IslandData.NoLand) continue;
                    if (Top(nx, nz) < w) leaks++;               // dry ground *under* the water
                    else if (watery) shoreSteps.Add(Top(nx, nz) - w);
                }
            }
            lakes += lakeRegions.Count;
            if (lakeRegions.Count > 0) islandsWithLake++;

            // Distinct bodies and their sizes — a patch is no longer one lake by
            // definition, so the region count above and this can disagree, and
            // the gap between them is the shaped lakes working.
            var lakeSeen = new bool[n, n];
            var lakeStack = new Stack<(int X, int Z)>();
            for (int sx = 0; sx < n; sx++)
            for (int sz = 0; sz < n; sz++)
            {
                if (lakeSeen[sx, sz] || d.WaterLevel[sx, sz] == IslandData.NoLand
                    || d.River[sx, sz]) continue;
                if (d.Fluid[sx, sz] != (byte)FluidKind.Water) continue;

                int size = 0;
                lakeSeen[sx, sz] = true;
                lakeStack.Push((sx, sz));
                while (lakeStack.Count > 0)
                {
                    var (cx, cz) = lakeStack.Pop();
                    size++;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + Dx[k], nz = cz + Dz[k];
                        if (nx < 0 || nz < 0 || nx >= n || nz >= n || lakeSeen[nx, nz]) continue;
                        if (d.WaterLevel[nx, nz] == IslandData.NoLand || d.River[nx, nz]) continue;
                        if (d.Fluid[nx, nz] != (byte)FluidKind.Water) continue;
                        lakeSeen[nx, nz] = true;
                        lakeStack.Push((nx, nz));
                    }
                }
                lakeBodySizes.Add(size);
            }

            // ---- goo -----------------------------------------------------------
            int gooHere = 0;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (d.WaterLevel[x, z] == IslandData.NoLand
                    || d.Fluid[x, z] != (byte)FluidKind.Goo) continue;
                gooHere++;
                // Never mixes, including diagonally: no water within a king's move.
                for (int ox = -1; ox <= 1; ox++)
                for (int oz = -1; oz <= 1; oz++)
                {
                    int nx = x + ox, nz = z + oz;
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (d.WaterLevel[nx, nz] != IslandData.NoLand
                        && d.Fluid[nx, nz] == (byte)FluidKind.Water) gooTouchesWater++;
                }
            }
            gooCells += gooHere;
            if (gooHere > 0) gooIslands++;

            // ---- gorges: can the walled reaches actually be bridged? -----------
            int reachesHere = AnalyseGorges(d, ref gorgeCells, gorgeLengths,
                                            gorgeSealedLengths, gorgeDetours,
                                            ref gorgeCrossable, ref gorgeSealed,
                                            ref gorgeMisaligned);
            gorgeReaches += reachesHere;
            if (reachesHere > 0) gorgeIslands++;

            // ---- what the character delivered ----------------------------------
            charIslands.TryGetValue(d.Character, out int seen);
            charIslands[d.Character] = seen + 1;
            if (!charHas.TryGetValue(d.Character, out int[]? has) || has == null)
                charHas[d.Character] = has = new int[Forms];

            var present = new bool[Forms];
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (Land(x, z)) present[(int)Form(x, z)] = true;
            for (int t = 0; t < Forms; t++) if (present[t]) has[t]++;

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

            // ---- rivers --------------------------------------------------------
            // There is no sea, so the one thing every watercourse must do is reach
            // the rim. A river that stops inland is water with nowhere to go.
            int here = 0;
            bool reachedRim = false;
            var pours = new HashSet<(Vector2I, Vector2I)>();
            foreach (Fall f in d.Falls) pours.Add((f.Cell, f.Flow));
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.River[x, z]) continue;
                here++;
                riverCells++;
                if (d.Navigable[x, z]) navigableCells++;

                short level = d.WaterLevel[x, z];
                if (Land(x, z) && Top(x, z) >= level) riverDry++;      // channel not cut

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Land(nx, nz)) { reachedRim = true; continue; }
                    // Water running uphill: a downstream cell standing above this
                    // one by more than a slab of noise.
                    // Excused where the higher neighbour pours a drawn fall into
                    // this cell: that is water falling, which is the opposite of
                    // climbing. The flow comparison is a heuristic for "more
                    // downstream", and two chains running side by side at
                    // different levels can trip it — the fall is the proof of
                    // which way the water actually goes.
                    if (d.River[nx, nz] && d.WaterLevel[nx, nz] > level + 1
                        && d.Flow[nx, nz] > d.Flow[x, z]
                        && !pours.Contains((new Vector2I(nx, nz), new Vector2I(x - nx, z - nz))))
                        riverUphill++;
                }
            }
            foreach (Fall f in d.Falls)
            {
                fallCells++;
                if (f.OffRim) rimFalls++;
                else innerFalls.Add(f.Drop);
            }
            if (here > 0)
            {
                islandsWithRiver++;
                riverPerIsland.Add(here);
                if (reachedRim) riverIslandsReachingRim++;
            }

            // ---- how straight the water runs -----------------------------------
            // The one measurable proxy for "rivers should bend". A cell with two
            // river neighbours is on a reach; if they are opposite each other the
            // reach is running straight through it, and if they are at right
            // angles the course turns there. A breadth-first flood produced a tree
            // of straight cardinal rays and scored near 100% straight; anything
            // that meanders spends far more of its length turning.
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.River[x, z] || d.Navigable[x, z]) continue;   // a 2-wide reach is not a line
                bool east = x + 1 < n && d.River[x + 1, z], west = x > 0 && d.River[x - 1, z];
                bool south = z + 1 < n && d.River[x, z + 1], north = z > 0 && d.River[x, z - 1];
                int touching = (east ? 1 : 0) + (west ? 1 : 0) + (south ? 1 : 0) + (north ? 1 : 0);
                if (touching != 2) continue;                        // a source, a mouth, a junction
                if ((east && west) || (north && south)) riverStraight++;
                else riverBends++;
            }

            // The longest run a course holds one direction for, which is the thing
            // that reads as ruled-with-a-ruler when it is long.
            for (int x = 0; x < n; x++)
            {
                int run = 0;
                for (int z = 0; z <= n; z++)
                {
                    bool on = z < n && d.River[x, z] && !d.Navigable[x, z];
                    if (on) { run++; continue; }
                    if (run >= 3) straightRuns.Add(run);
                    run = 0;
                }
            }
            for (int z = 0; z < n; z++)
            {
                int run = 0;
                for (int x = 0; x <= n; x++)
                {
                    bool on = x < n && d.River[x, z] && !d.Navigable[x, z];
                    if (on) { run++; continue; }
                    if (run >= 3) straightRuns.Add(run);
                    run = 0;
                }
            }

            // ---- eyots ---------------------------------------------------------
            // Dry land with the same river either side of it: the island a braided
            // reach parts around.
            for (int x = 1; x + 1 < n; x++)
            for (int z = 1; z + 1 < n; z++)
            {
                if (!Land(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                bool acrossX = d.River[x - 1, z] && d.River[x + 1, z];
                bool acrossZ = d.River[x, z - 1] && d.River[x, z + 1];
                if (acrossX || acrossZ) eyotCells++;
            }

            // ---- overhangs and arches ------------------------------------------
            // A column with two spans is the one thing the span model exists for.
            // What is worth checking is that the two never touch — a gap of zero
            // is one span written twice — and that an arch has nothing under it.
            if (d.Overhangs.Count > 0) overhangIslands++;
            foreach (Vector2I c in d.Overhangs)
            {
                Span[] spans = d.Spans[c.X, c.Y];
                overhangCells++;
                for (int s = 1; s < spans.Length; s++)
                    lipAir.Add(spans[s].Bottom - spans[s - 1].Top - 1);
            }
            // ---- ferries -------------------------------------------------------
            berths += d.Berths.Count;
            berthSites += d.BerthSites;

            // ---- surface and anchors ------------------------------------------
            coastAnchors += d.CoastCells.Count;
            cliffAnchors += d.CliffCells.Count;
            foreach (Vector2I c in d.CoastCells) if (d.Beach[c.X, c.Y]) beachedCoast++;
            int beachHere = 0;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.HasLand(x, z)) continue;
                materialCells[d.Material[x, z]]++;
                if (d.Beach[x, z]) beachHere++;
                if (d.Ford[x, z]) fordCells++;
                if (d.Landings[x, z]) landingCells++;
            }
            beachCells += beachHere;
            if (beachHere == 0) islandsWithoutBeach++;
            waterBodies += d.WaterBodies;
            if (d.Berths.Count > 0) { islandsWithBerth++; islandsNeedingFerry++; }
            foreach (FerryBerth berth in d.Berths)
            {
                int rise = Cross(berth.Land.X, berth.Land.Y) - berth.Level;
                if (rise < 0 || rise > Traversal.MaxQuayRise) badQuay++;
                if (!Traversal.Sailable(d, berth.Water.X, berth.Water.Y)) badQuay++;
                quayRise.Add(rise);
            }

            // ---- roads between the Gates ---------------------------------------
            if (d.Rough) roughIslands++;
            int exitCount = 0;
            foreach (Gate g in d.Gates) if (g.Role == GateRole.Exit) exitCount++;
            if (d.Passages.Count < exitCount) exitsWithoutRoad += exitCount - d.Passages.Count;
            foreach (Passage road in d.Passages)
            {
                roadCosts.Add(road.Cost);
                roadLengths.Add(road.Path.Count);
                flights += road.Flights;
                if (road.Cost == 0) roadsFree++;
                foreach (Works w in road.Built)
                {
                    if (w.Kind == WorksKind.Stair) roadStairs++;
                    else if (w.Kind == WorksKind.Bridge) roadBridges++;
                    else roadFerries++;
                }
                // The road has to be a road: every step of it either a neighbour,
                // a bridge inside the span, or a ferry — and a ferry is the only
                // move that may cover any distance at all, so it is checked
                // against what the passage actually recorded rather than guessed
                // at from the geometry.
                var sailed = new HashSet<(Vector2I, Vector2I)>();
                foreach (Works w in road.Built)
                    if (w.Kind == WorksKind.Ferry) sailed.Add((w.From, w.To));

                for (int hop = 1; hop < road.Path.Count; hop++)
                {
                    Vector2I a = road.Path[hop - 1], b = road.Path[hop];
                    if (sailed.Contains((a, b))) continue;
                    int gap = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
                    if (gap > d.BridgeSpan + 1 || (a.X != b.X && a.Y != b.Y)) roadJumps++;
                }
            }

            // ---- the sculpted landforms ----------------------------------------
            // Their cliffs are *inside* a patch, which is the whole point of them,
            // so what is worth measuring is how tall those are: a gully wall, a
            // tower side and a terrace riser all have to clear the ambiguous two.
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Ground(x, z)) continue;
                LandformType t = Form(x, z);
                if (t is not (LandformType.Badlands or LandformType.Karst
                              or LandformType.Massif or LandformType.Sinkholes)) continue;

                for (int k = 0; k < 2; k++)
                {
                    int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                    if (!Ground(nx, nz) || Form(nx, nz) != t) continue;
                    if (d.Region[x, z] != d.Region[nx, nz]) continue;

                    int step = Math.Abs(Cross(x, z) - Cross(nx, nz));
                    if (step < 2) continue;
                    if (t == LandformType.Badlands) gullyDepths.Add(step);
                    else if (t == LandformType.Karst) towerRises.Add(step);
                    else if (t == LandformType.Massif) terraceSteps.Add(step);
                    else sinkDepths.Add(step);
                }
            }

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

                // Steepest step out of a pass cell: the whole point is that it is
                // 1. Lake beds and canyon floors are skipped — a pass whose disc
                // happens to take in one is measuring that feature's drop, not the
                // saddle's grade.
                if (d.WaterLevel[x, z] != IslandData.NoLand || d.Canyon[x, z]) continue;
                int worst = 0;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Land(nx, nz) || !d.Pass[nx, nz]) continue;
                    if (d.WaterLevel[nx, nz] != IslandData.NoLand || d.Canyon[nx, nz]) continue;
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
                shelfDrops.Add(shelf.Drop);
                if (d.Walk[shelf.Center.X, shelf.Center.Y] != d.Mainland) offMain++;
            }
            buildableShelves += islandShelves;
            if (islandShelves > 0) islandsWithShelf++;
            widestShelf.Add(widest);
            shelfOffMainland.Add(offMain);

            // ---- Gates ---------------------------------------------------------
            int entries = 0, exits = 0;
            var edges = new HashSet<Cardinal>();

            foreach (Gate g in d.Gates)
            {
                if (g.Role == GateRole.Entry) entries++; else exits++;
                if (!edges.Add(g.Facing)) sharedEdge++;

                apronSizes.Add(g.ApronArea);
                if (g.ApronArea < GatePlacement.ApronArea) gateApronShort++;

                if (g.Kind == GateKind.Land)
                {
                    landGates++;
                    // Standing on ground: dry, and part of the heartland.
                    if (!Land(g.Center.X, g.Center.Z)) gateOffHeartland++;
                    else if (d.WaterLevel[g.Center.X, g.Center.Z] != IslandData.NoLand) gateInWater++;
                    else if (d.Reach[g.Center.X, g.Center.Z] != d.Heartland) gateOffHeartland++;
                }
                else
                {
                    hangingGates++;
                    // Hanging in the aether: there must be nothing under it.
                    if (Land(g.Center.X, g.Center.Z)) hangingOnLand++;
                }

                // <b>Every Gate's landing, held to the letter.</b> Full length, no
                // exceptions, and <i>dead</i> level rather than level to within the
                // free step — the strips are built now, so "sometimes short" and
                // "sometimes sloped" are not tolerances, they are bugs. Both kinds
                // of Gate own one: a land Gate stands on the strip a vessel would
                // otherwise have landed on.
                {
                    Vector2I outward = g.Outward;
                    Vector2I head = g.Kind == GateKind.Hanging
                        ? new Vector2I(g.Center.X, g.Center.Z)
                          - outward * GatePlacement.HangingOffset
                        : new Vector2I(g.Center.X, g.Center.Z);

                    bool strip = Land(head.X, head.Y)
                              && g.Landing == GatePlacement.StripLength;
                    if (strip)
                    {
                        short level = Top(head.X, head.Y);
                        for (int along = 0; along < GatePlacement.StripLength && strip; along++)
                        {
                            int sx = head.X - outward.X * along;
                            int sz = head.Y - outward.Y * along;
                            strip = Land(sx, sz) && Top(sx, sz) == level
                                    && d.WaterLevel[sx, sz] == IslandData.NoLand
                                    && d.Reach[sx, sz] == d.Heartland;
                        }
                    }
                    if (!strip) stripMissing++; else stripLengths.Add(g.Landing);
                }

                // ---- is it on the side of the map it claims? -------------------
                // Three separate questions, because they fail separately: how much
                // of the Domain is left behind the player as they arrive, whether
                // the Gate has slid into a corner, and whether the east Gate is in
                // fact the easternmost thing on the island.
                Vector2I outAxis = g.Outward, sideAxis = g.Across;
                int gateAlong = g.Center.X * outAxis.X + g.Center.Z * outAxis.Y;
                long beyond = 0, dry = 0;
                int sideMin = int.MaxValue, sideMax = int.MinValue;
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!Land(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                    dry++;
                    if (x * outAxis.X + z * outAxis.Y > gateAlong) beyond++;
                    int s = x * sideAxis.X + z * sideAxis.Y;
                    if (s < sideMin) sideMin = s;
                    if (s > sideMax) sideMax = s;
                }
                if (dry > 0) gateBehind.Add((int)(100 * beyond / dry));

                int gateSide = g.Center.X * sideAxis.X + g.Center.Z * sideAxis.Y;
                int width = sideMax - sideMin;
                if (width > 0 && (gateSide > sideMax - width * 0.12f
                                  || gateSide < sideMin + width * 0.12f)) gateInCorner++;

                foreach (Gate o in d.Gates)
                {
                    if (o.Facing == g.Facing) continue;
                    if (o.Center.X * outAxis.X + o.Center.Z * outAxis.Y >= gateAlong)
                        gateNotOutermost++;

                    // Against the rule that is actually in force, not a number of
                    // the audit's own: GatePlacement.CrowdedSeparation is the floor
                    // the last rung of the placement ladder still has to clear, so
                    // a pair under it is a rule broken rather than a coast that was
                    // awkward. The spread is reported beside it, because "none
                    // broke the floor" and "half of them are sitting on it" are
                    // different islands.
                    //
                    // Apron to apron, like the rule: a hanging Gate's Center is out
                    // in the aether, and the distance that matters is the one on the
                    // ground.
                    int apart = Math.Abs(o.Apron.X - g.Apron.X)
                              + Math.Abs(o.Apron.Y - g.Apron.Y);
                    gateSpacing.Add(apart);
                    if (apart < GatePlacement.MinSeparation * n) gatesCrowded++;
                }
            }

            if (entries != 1) noEntry++;
            if (exits < 1 || exits > 3) badExitCount++;
            exitCounts.Add(exits);
            foreach (Gate g in d.Gates)
                if (g.Role == GateRole.Entry && Params.EntryGate != GateKind.Auto
                    && g.Kind != Params.EntryGate) wrongEntryKind++;

            // ---- crossings -----------------------------------------------------
            // A bridge is a level deck, so the only thing worth measuring is
            // whether you can walk onto it: one slab at each end, no more.
            foreach (Crossing c in d.Bridges)
            {
                crossings++;
                crossingSpans.Add(c.Span);
                int a = Traversal.CrossLevel(d, c.A.X, c.A.Y);
                int b = Traversal.CrossLevel(d, c.B.X, c.B.Y);
                if (Math.Abs(a - b) > Traversal.MaxBridgeRise) deckSteep++;
                if (Math.Abs(a - c.Deck) > 1 || Math.Abs(b - c.Deck) > 1) deckOffBank++;
            }

            // ---- the Stage 6 guarantees ----------------------------------------
            attempts.Add(d.Attempts);
            if (d.Unmet.Length > 0)
            {
                unplayable++;
                GD.Print($"  seed {seed} gave up after {d.Attempts}: {d.Unmet}");
            }

            // ---- airstrips -----------------------------------------------------
            int strips = 0;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++) if (d.Landings[x, z]) strips++;
            if (strips > 0) airstripIslands++;
            airstripCells.Add(strips);

            // ---- continuity ----------------------------------------------------
            int masses = CountComponents(d, n);
            landmasses += masses;

            // The archipelago guarantee, asked of the *landmasses* rather than of
            // every cell: does every piece of land have somewhere the heartland
            // can bridge to? A summit nobody can climb is a separate question and
            // would otherwise drown this one.
            var massOf = new int[n, n];
            int massCount = LabelLandmasses(d, n, massOf);
            var reached = new bool[massCount];
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (massOf[x, z] >= 0 && d.Reach[x, z] == d.Heartland) reached[massOf[x, z]] = true;

            bool allLinked = true;
            foreach (bool r in reached) allLinked &= r;

            byArrangement.TryGetValue(d.Arrangement, out var acc);
            byArrangement[d.Arrangement] =
                (acc.Islands + 1, acc.Masses + masses, acc.Linked + (allLinked ? 1 : 0));

            // Within a landmass only. Two separate islands a corner apart are not
            // a broken join, they are two islands — and every arrangement but
            // Single has them by design.
            diagonalLand += DiagonalOnly(n, (x, z) =>
                x >= 0 && z >= 0 && x < n && z < n && massOf[x, z] >= 0, massOf);
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
        GD.Print($"  two-slab off mountains    {ambiguousOffMountain} of {pairsOffMountain}");
        foreach (var (k, v) in ambiguousWhere.OrderByDescending(e => e.Value))
            GD.Print($"    {k,-20} {v,6}");
        GD.Print("");

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

        // The sculpted landforms carry their cliffs inside a patch, so the steps
        // below are the landform rather than a leak in the rules. What matters is
        // that none of them is two slabs — that height is neither a step nor a
        // cliff, and it is the one thing the grammar forbids everywhere.
        Report("badlands: gully wall", gullyDepths, "slabs");
        Report("karst: tower side", towerRises, "slabs");
        Report("massif: terrace riser", terraceSteps, "slabs");
        Report("sinkholes: pit wall", sinkDepths, "slabs");

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

        GD.Print($"rivers: {riverCells} cells on {islandsWithRiver} of {Seeds} islands, "
            + $"{navigableCells} of them navigable");
        Report("  river cells per island", riverPerIsland, "cells");
        GD.Print($"  islands whose rivers reach the rim: {riverIslandsReachingRim}"
            + $" of {islandsWithRiver}   (there is no sea; they must)");
        GD.Print($"  falls: {fallCells}, of which {rimFalls} pour off the rim");
        GD.Print($"  channel not cut below its own water (want 0): {riverDry}");
        GD.Print($"  water running uphill (want 0):                {riverUphill}");

        // How much of a course's length is spent going straight. A flood that
        // breaks its ties first-in-first-out gives a tree of straight cardinal
        // rays and scores in the high nineties; the meander field is what pulls
        // this down, and the longest run is the thing the eye reads as ruled.
        int reachCells = riverStraight + riverBends;
        if (reachCells > 0)
            GD.Print($"  how the courses run: {100.0 * riverStraight / reachCells:0}% straight, "
                + $"{100.0 * riverBends / reachCells:0}% turning  (n={reachCells})");
        Report("  longest run held in one direction", straightRuns, "cells");
        GD.Print($"  eyots: {eyotCells} cells of island parted by a braided reach\n");

        GD.Print($"ferries: {berths} berths on {waterBodies} bodies of water, "
            + $"over {islandsWithBerth} of {Seeds} islands");
        // Sites against berths is the number that says whether the pruning is
        // right. Nearly every lake shore fits the domino rule; a berth survives
        // only where the water actually separates two pieces of the reach graph,
        // so a low count is the pruning working unless the *sites* are low too.
        GD.Print($"  of {berthSites} sites the domino rule found "
            + $"({(berthSites > 0 ? 100 * berths / berthSites : 0)}% load-bearing)");
        GD.Print($"  islands with water a bridge cannot span: {islandsNeedingFerry} of {Seeds}");
        Report("  quay above the water", quayRise, "slabs");
        GD.Print($"  berth that is not a quay on sailable water (want 0): {badQuay}\n");

        GD.Print($"overhangs and arches: {overhangCells} columns carrying a second span, "
            + $"on {overhangIslands} of {Seeds} islands");
        Report("  air under a lip", lipAir, "slabs");
        GD.Print("");

        // What the ground is made of, and what the content layer can hang off it.
        // Both are lists nothing else in the audit reads, which is exactly how a
        // material that never gets picked or an anchor list that quietly empties
        // would go unnoticed until the biome layer was built on top of them.
        GD.Print("surface: what the ground is made of, as a share of land");
        {
            var parts = new List<string>();
            long land = 0;
            foreach (long v in materialCells) land += v;
            foreach (SurfaceMaterial m in Enum.GetValues<SurfaceMaterial>())
            {
                long cells = materialCells[(int)m];
                parts.Add($"{m.ToString().ToLowerInvariant()} "
                    + (land > 0 ? $"{100.0 * cells / land:0.0}%" : "-")
                    + (cells == 0 ? " NEVER" : ""));
            }
            GD.Print("  " + string.Join(", ", parts));
        }
        GD.Print($"anchors: {coastAnchors} coast, {cliffAnchors} cliff, {overhangCells} overhang, "
            + $"{beachCells} beach, {fordCells} ford, {landingCells} gate landing, {berths} quay");
        GD.Print($"  islands with no beach at all: {islandsWithoutBeach} of {Seeds}");
        // Against the coast *ring*, not against the beach's own cell count: a
        // beach is two cells deep, so beach-cells-over-coast-cells reads 151% and
        // means nothing. What is worth knowing is how much of the shoreline
        // arrives gently.
        GD.Print($"  coast that steps down onto a beach: "
            + (coastAnchors > 0 ? $"{100.0 * beachedCoast / coastAnchors:0}%" : "-")
            + "   (the rest breaks off to the keel)\n");

        GD.Print($"lakes: {lakes} over {lakeCells} cells, on {islandsWithLake} of {Seeds} islands");
        Report("  shore step above water", shoreSteps, "slabs");
        GD.Print($"  dry land BELOW a water surface (want 0): {leaks}");
        GD.Print($"  water touching the void (want 0):        {waterAtVoid}");
        Report("  lake bodies", lakeBodySizes, "cells");

        GD.Print($"goo: {gooCells} cells of puddle on {gooIslands} of {Seeds} islands");
        GD.Print($"  goo within a king's move of water (want 0): {gooTouchesWater}\n");

        GD.Print($"gorges (a course walled 3+ slabs on both sides): {gorgeCells} cells, "
            + $"{gorgeReaches} reaches of 3+ cells, on {gorgeIslands} of {Seeds} islands");
        Report("  reach length", gorgeLengths, "cells");
        GD.Print($"  reaches a bridge could cross somewhere along them: "
            + $"{gorgeCrossable} of {gorgeReaches}");
        GD.Print($"  sealed reaches — no legal deck anywhere on their length: {gorgeSealed}"
            + $", of which {gorgeMisaligned} misaligned rims (a deck fits, banks disagree 3+)");
        Report("  sealed reach length", gorgeSealedLengths, "cells");
        Report("  walk to the nearest deck, worst cell per reach", gorgeDetours, "cells");

        GD.Print("landforms delivered, by character (share of that character's islands)");
        foreach (var (c, islands) in charIslands.OrderBy(k => k.Key.ToString()))
        {
            int[] has = charHas[c];
            var parts = new List<string>();
            for (int t = 0; t < Forms; t++)
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
            + $"face <= {Traversal.InfrastructureStep} slabs, span <= {(int)Params.Crossings} cells)");
        GD.Print($"  land on the heartland       {100.0 * reachHeartland / walkLand,6:0.0}%");
        Report("  heartland share per island", reachShare, "%");
        GD.Print($"  islands whose dry land is ONE reachable whole: {islandsFullyReachable} of {Seeds}");
        long stranded = 0;
        foreach (long v in strandedByForm) stranded += v;
        if (stranded > 0)
        {
            var bits = new List<string>();
            for (int t = 0; t < Forms; t++)
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
        Report("  descent across one shelf", shelfDrops, "slabs");
        GD.Print("");

        GD.Print($"crossings: {crossings} bridge sites over {Seeds} islands"
            + $"  (span <= {(int)Params.Crossings} cells)");
        Report("  span", crossingSpans, "cells");
        GD.Print($"  banks disagreeing by more than {Traversal.MaxBridgeRise} slabs (want 0): {deckSteep}");
        GD.Print($"  deck more than a slab off a bank (want 0):   {deckOffBank}\n");

        GD.Print($"gates: {landGates} standing on land, {hangingGates} hanging in the aether");
        GD.Print($"  islands without exactly one entry (want 0): {noEntry}");
        GD.Print($"  islands whose exits are not 1-3 (want 0):   {badExitCount}");
        Report("  exits per island", exitCounts, "");
        GD.Print($"  two gates on one edge (want 0):             {sharedEdge}");
        GD.Print($"  entry gate not the kind asked for (want 0): {wrongEntryKind}");
        Report("  buildable ground within 4 cells of the landing", apronSizes, "cells");
        GD.Print($"  gate off the heartland or in water (want 0): "
            + $"{gateOffHeartland + gateInWater}");
        GD.Print($"  hanging gate standing on land (want 0):     {hangingOnLand}");
        Report("  landing strip", stripLengths, "cells");
        GD.Print($"  gate with a short or sloped landing (want 0):  {stripMissing}");
        GD.Print($"  gate that is not the outermost on its own axis (want 0): {gateNotOutermost}");
        GD.Print($"  gate in a corner of its own edge (want 0):   {gateInCorner}");
        Report("  how far apart two gates are", gateSpacing, "cells");
        GD.Print($"  two gates closer than the {GatePlacement.MinSeparation:P0} floor"
            + $" (want 0): {gatesCrowded / 2}");
        Report("  dry land left behind a gate", gateBehind, "%");
        GD.Print($"  islands with a landing strip: {airstripIslands} of {Seeds}"
            + "   (only the strips the hanging gates took are marked)");
        Report("  ground marked as strip", airstripCells, "cells");
        GD.Print("");

        // The road from the Gate the player arrives by to each Gate they can leave
        // by, priced in things that have to be built. Every Exit must have one:
        // an Exit you cannot get to is a Domain with one Link, wearing several.
        GD.Print($"roads from the entry gate to the exits: {roadCosts.Count} over {Seeds} islands");
        GD.Print($"  exits with no road at all (want 0): {exitsWithoutRoad}");
        Report("  works to build on one road", roadCosts, "crossings");
        Report("  length of one road", roadLengths, "cells");
        GD.Print($"  roads you can simply walk: {roadsFree} of {roadCosts.Count}");
        GD.Print($"  what they need built: {roadStairs} stairs, {roadBridges} bridges, "
            + $"{roadFerries} ferries");
        GD.Print($"  a step on a road longer than one bridge (want 0): {roadJumps}");
        GD.Print($"  flights of five-plus elevators: {flights}, on {roughIslands} of {Seeds} "
            + "islands (marked Rough — hard country, not a fault)\n");

        GD.Print("arrangements: landmasses per island, and whether all of it links up");
        foreach (var (a, v) in byArrangement.OrderBy(k => k.Key.ToString()))
            GD.Print($"  {a,-12} {v.Islands,3} islands   {(float)v.Masses / v.Islands,4:0.0} masses each"
                + $"   fully linked {100 * v.Linked / v.Islands,3}%");
        GD.Print("");

        Report("re-rolls: islands built per seed", attempts, "");
        GD.Print($"  seeds that never met the guarantees (want 0): {unplayable}\n");

        if (Silhouettes) PrintSilhouettes();
        if (Waterways) PrintWaterways();
        if (Sculpts) PrintSculpts();
        if (Feasibility) PrintFeasibility();
        if (GateRequests) PrintGateRequests();
        if (GateMatrix) PrintGateMatrix();
        if (Knobs) PrintKnobs();

        GD.Print($"continuity: {landmasses} landmasses over {Seeds} islands "
            + $"(more than one is the arrangement's doing, not a fault); "
            + $"diagonal-only joins within a landmass: land {diagonalLand}, water {diagonalWater}");

        Baseline(new Godot.Collections.Dictionary<string, Variant>
        {
            ["free%"] = Math.Round(100.0 * free / pairs, 1),
            ["twoSlab%"] = Math.Round(100.0 * ambiguous / pairs, 1),
            ["cliff%"] = Math.Round(100.0 * cliff / pairs, 1),
            ["twoSlabOffMountain"] = ambiguousOffMountain,
            ["patchesUndersized"] = patchesUndersized,
            ["riverCells"] = riverCells,
            ["navigableCells"] = navigableCells,
            ["riverStraight%"] = reachCells > 0 ? Math.Round(100.0 * riverStraight / reachCells) : 0,
            ["falls"] = fallCells,
            ["lakes"] = lakes,
            ["waterLeaks"] = leaks,
            ["riverUphill"] = riverUphill,
            ["gooCells"] = gooCells,
            ["gooTouchesWater"] = gooTouchesWater,
            ["gorgeReaches"] = gorgeReaches,
            ["gorgeSealed"] = gorgeSealed,
            ["berths"] = berths,
            ["overhangColumns"] = overhangCells,
            ["mainland%"] = Math.Round(100.0 * walkMainland / walkLand, 1),
            ["heartland%"] = Math.Round(100.0 * reachHeartland / walkLand, 1),
            ["islandsOneWhole"] = islandsFullyReachable,
            ["buildableShelves"] = buildableShelves,
            ["crossings"] = crossings,
            ["deckSteep"] = deckSteep,
            ["noEntry"] = noEntry,
            ["exitsWithoutRoad"] = exitsWithoutRoad,
            ["roadsFree"] = roadsFree,
            ["unplayable"] = unplayable,
        });
    }

    /// <summary>
    /// Where the numbers from the last accepted run are kept, so a change that
    /// moves one is noticed rather than read past.
    /// </summary>
    private const string BaselinePath = "res://docs/audit-baseline.json";

    /// <summary>
    /// Compares this run's headline numbers against the last accepted ones and
    /// prints what moved.
    ///
    /// <para>The audit prints sixty numbers and a human compares them to the last
    /// run by eye, which works right up until it does not: when the lake outflow
    /// was fixed, navigable river cells fell from 1,642 to 146 and it was very
    /// nearly read past. A file of the accepted values and a diff costs nothing
    /// and catches exactly that.</para>
    ///
    /// <para>It is a <b>diff, not a test</b> — every number here is expected to
    /// move when the generator changes, and the point is to see it move and decide
    /// whether you meant it. Set <c>AcceptBaseline</c> on the scene to write the
    /// current run as the new accepted answer.</para>
    /// </summary>
    private void Baseline(Godot.Collections.Dictionary<string, Variant> now)
    {
        string json = Json.Stringify(now, "  ", sortKeys: true);

        if (AcceptBaseline)
        {
            using var write = FileAccess.Open(BaselinePath, FileAccess.ModeFlags.Write);
            if (write == null)
            {
                GD.Print($"\nbaseline: could not write {BaselinePath} "
                    + $"({FileAccess.GetOpenError()})");
                return;
            }
            write.StoreString(json + "\n");
            GD.Print($"\nbaseline: accepted this run as {BaselinePath}");
            return;
        }

        using var read = FileAccess.Open(BaselinePath, FileAccess.ModeFlags.Read);
        if (read == null)
        {
            GD.Print($"\nbaseline: none yet — run with AcceptBaseline to write "
                + $"{BaselinePath}");
            return;
        }

        var was = Json.ParseString(read.GetAsText()).AsGodotDictionary();
        var moved = new List<string>();
        foreach (var (key, value) in now)
        {
            if (!was.ContainsKey(key)) { moved.Add($"    {key,-22} new  {value}"); continue; }
            double before = was[key].AsDouble(), after = value.AsDouble();
            if (Mathf.IsEqualApprox((float)before, (float)after)) continue;
            double delta = after - before;
            moved.Add($"    {key,-22} {before,12:0.##} -> {after,-12:0.##} ({delta:+0.##;-0.##})");
        }

        GD.Print($"\nbaseline: {moved.Count} of {now.Count} numbers moved since the last "
            + "accepted run");
        foreach (string line in moved) GD.Print(line);
        if (moved.Count > 0)
            GD.Print("    (a diff, not a failure — decide whether you meant it, then "
                + "re-run with AcceptBaseline)");
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

    /// <summary>Labels each 4-connected landmass; returns how many there are.</summary>
    private static int LabelLandmasses(IslandData d, int n, int[,] into)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) into[x, z] = -1;

        var stack = new Stack<(int X, int Z)>();
        int found = 0;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!d.HasLand(sx, sz) || into[sx, sz] >= 0) continue;
            int id = found++;
            into[sx, sz] = id;
            stack.Push((sx, sz));
            while (stack.Count > 0)
            {
                var (x, z) = stack.Pop();
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!d.HasLand(nx, nz) || into[nx, nz] >= 0) continue;
                    into[nx, nz] = id;
                    stack.Push((nx, nz));
                }
            }
        }
        return found;
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

    /// <summary>
    /// A coarse ASCII silhouette of one island per arrangement.
    ///
    /// Headless gives no rendering, so how terrain <i>looks</i> needs a human at
    /// the editor — but the <b>shape</b> of a footprint is a fact about the mask,
    /// and printing it is how a change to the arrangements can be checked at all
    /// without opening the lab. Water and land only; two cells to the character,
    /// so a 128² island fits in a terminal.
    /// </summary>
    private void PrintSilhouettes()
    {
        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;

            var p = (IslandParams)Params.Duplicate();
            p.Arrangement = how;
            IslandData d = new IslandGenerator().Generate(FirstSeed, p);
            int n = d.Size, step = Math.Max(1, n / 64);

            GD.Print($"--- {how} (seed {FirstSeed}) ---");
            for (int z = 0; z < n; z += step)
            {
                var row = new System.Text.StringBuilder();
                for (int x = 0; x < n; x += step)
                {
                    // Whatever the sample lands on: land wins over water, water
                    // over aether, so a one-cell strait still shows as a strait
                    // only where it really is one.
                    bool land = false, wet = false;
                    for (int dx = 0; dx < step; dx++)
                    for (int dz = 0; dz < step; dz++)
                    {
                        int cx = x + dx, cz = z + dz;
                        if (cx >= n || cz >= n || !d.HasLand(cx, cz)) continue;
                        if (d.WaterLevel[cx, cz] != IslandData.NoLand) wet = true;
                        else land = true;
                    }
                    row.Append(land ? '#' : wet ? '~' : '.');
                }
                GD.Print(row.ToString());
            }
        }
    }

    /// <summary>
    /// One island's water at full resolution: lakes, streams, navigable reaches,
    /// eyots and falls, a character to the cell.
    ///
    /// The silhouette printer samples two cells to the character, which is enough
    /// for a footprint and loses a one-cell stream entirely — and how a river
    /// <i>runs</i> is exactly the thing that needed checking when the routing was
    /// made to meander. This is the same trick at the scale the water lives at.
    /// </summary>
    private void PrintWaterways()
    {
        for (int i = 0; i < Math.Min(3, Seeds); i++)
        {
            int seed = FirstSeed + i * 6151;
            IslandData d = new IslandGenerator().Generate(seed, Params);
            int n = d.Size;

            var falls = new HashSet<Vector2I>();
            foreach (Fall f in d.Falls) falls.Add(f.Cell);

            GD.Print($"--- waterways, seed {seed} ({d.Character}, {d.Arrangement}) ---");
            GD.Print("    . aether   , land   ~ stream   = navigable   O lake   v fall   o eyot");
            for (int z = 0; z < n; z++)
            {
                var row = new System.Text.StringBuilder(n);
                for (int x = 0; x < n; x++)
                {
                    if (!d.HasLand(x, z)) { row.Append('.'); continue; }
                    bool wet = d.WaterLevel[x, z] != IslandData.NoLand;
                    char c = !wet ? ',' :
                             !d.River[x, z] ? 'O' :
                             d.Navigable[x, z] ? '=' : '~';
                    // An eyot is dry land with water on two sides of it.
                    if (!wet && Wet(d, x - 1, z) && Wet(d, x + 1, z)) c = 'o';
                    if (!wet && Wet(d, x, z - 1) && Wet(d, x, z + 1)) c = 'o';
                    if (falls.Contains(new Vector2I(x, z))) c = 'v';
                    row.Append(c);
                }
                GD.Print(row.ToString());
            }
        }
    }

    /// <summary>
    /// Every combination of arrangement and character, a few seeds each: which
    /// ones the pipeline finds hard.
    ///
    /// <para>The ordinary audit rolls sixty islands from <c>Auto</c>, so it
    /// measures the combinations the weights happen to produce and says nothing
    /// about the ones they rarely do. But an <c>Auto</c> Domain is not how the
    /// game will ask for islands — a Domain's biome and world-tree position will
    /// name both, and if <c>Atoll</c> + <c>Karst</c> takes four attempts and comes
    /// out unplayable, that is a bug nobody would ever see from the summary.</para>
    ///
    /// <para>What it looks for: <b>re-rolls</b> (the generator rejecting its own
    /// output), <b>unmet guarantees</b> (it giving up), and the reachable share
    /// (whether the island is one place). A combination that averages more than
    /// one attempt is one the pipeline is fighting.</para>
    /// </summary>
    private void PrintFeasibility()
    {
        var rows = new List<(string Combo, float Attempts, int Unmet, float Reach,
                             float Masses, float Ms)>();
        int hard = 0, broken = 0;

        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            foreach (TerrainCharacter what in Enum.GetValues<TerrainCharacter>())
            {
                if (what == TerrainCharacter.Auto) continue;

                var p = (IslandParams)Params.Duplicate();
                p.Arrangement = how;
                p.Character = what;

                int attempts = 0, unmet = 0;
                float reach = 0f, masses = 0f;
                ulong t0 = Time.GetTicksMsec();

                for (int i = 0; i < FeasibilitySeeds; i++)
                {
                    IslandData d = new IslandGenerator().Generate(FirstSeed + i * 6151, p);
                    attempts += d.Attempts;
                    if (d.Unmet.Length > 0) unmet++;

                    long land = 0, heart = 0;
                    for (int x = 0; x < d.Size; x++)
                    for (int z = 0; z < d.Size; z++)
                    {
                        if (!d.HasLand(x, z)) continue;
                        land++;
                        if (d.Reach[x, z] == d.Heartland && d.Heartland >= 0) heart++;
                    }
                    if (land > 0) reach += 100f * heart / land;
                    masses += LabelLandmasses(d, d.Size, new int[d.Size, d.Size]);
                }

                float ms = (Time.GetTicksMsec() - t0) / (float)FeasibilitySeeds;
                rows.Add(($"{how} / {what}", attempts / (float)FeasibilitySeeds, unmet,
                          reach / FeasibilitySeeds, masses / FeasibilitySeeds, ms));
                if (unmet > 0) broken++;
                else if (attempts > FeasibilitySeeds) hard++;
            }
        }

        GD.Print($"\n=== feasibility: every arrangement x character, {FeasibilitySeeds} "
            + $"seeds each ({rows.Count} combinations) ===");
        GD.Print("  attempts > 1.0 means the generator rejected its own first island");
        GD.Print($"  {"combination",-34} {"attempts",8} {"unmet",6} {"reach%",7} "
            + $"{"masses",7} {"ms",6}");

        rows.Sort((a, b) => b.Attempts != a.Attempts
            ? b.Attempts.CompareTo(a.Attempts)
            : b.Unmet.CompareTo(a.Unmet));

        foreach (var (combo, att, un, re, ma, ms) in rows)
        {
            bool flag = un > 0 || att > 1.001f || re < 75f;
            GD.Print($"  {combo,-34} {att,8:0.00} {un,6} {re,7:0.0} {ma,7:0.0} {ms,6:0}"
                + (flag ? "   <-- look" : ""));
        }
        GD.Print($"\n  combinations that never met the guarantees: {broken} of {rows.Count}");
        GD.Print($"  combinations that needed a re-roll:          {hard} of {rows.Count}");
    }

    /// <summary>
    /// Does the Domain come out the way it was asked for? One row per Gate
    /// request: every Entry edge crossed with every Entry kind, then every Exit
    /// count crossed with every Exit kind.
    ///
    /// These are the only parameters set by something outside the Domain — the
    /// world-tree decides which edge you arrive on and which kind of Gate you
    /// arrive through — so they are the only ones where "it usually works" is a
    /// bug report rather than a result.
    /// </summary>
    private void PrintGateRequests()
    {
        GD.Print($"\n=== gate requests: what was asked for, and what came out "
            + $"({SweepSeeds} seeds each) ===");

        GD.Print($"\n  {"entry asked for",-24} {"on that edge",13} {"of that kind",13} "
            + $"{"both",7} {"re-rolls",9}");
        foreach (GateEdge edge in new[] { GateEdge.North, GateEdge.East, GateEdge.South, GateEdge.West })
        foreach (GateKind kind in new[] { GateKind.Hanging, GateKind.Land })
        {
            var p = (IslandParams)Params.Duplicate();
            p.EntryEdge = edge;
            p.EntryGate = kind;

            int rightEdge = 0, rightKind = 0, both = 0, attempts = 0;
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = new IslandGenerator().Generate(FirstSeed + i * 6151, p);
                attempts += d.Attempts;
                foreach (Gate g in d.Gates)
                {
                    if (g.Role != GateRole.Entry) continue;
                    bool e = (int)g.Facing == (int)edge - 1;
                    bool k = g.Kind == kind;
                    if (e) rightEdge++;
                    if (k) rightKind++;
                    if (e && k) both++;
                }
            }
            GD.Print($"  {$"{edge} {kind}",-24} {Pct(rightEdge, SweepSeeds),13} "
                + $"{Pct(rightKind, SweepSeeds),13} {Pct(both, SweepSeeds),7} "
                + $"{attempts / (float)SweepSeeds,9:0.00}"
                + (both < SweepSeeds ? "   <-- look" : ""));
        }

        GD.Print($"\n  {"exits asked for",-24} {"count met",13} {"all that kind",14} "
            + $"{"median got",11}");
        foreach (int count in new[] { 1, 2, 3 })
        foreach (GateKind kind in new[] { GateKind.Auto, GateKind.Hanging, GateKind.Land })
        {
            var p = (IslandParams)Params.Duplicate();
            p.ExitGates = count;
            p.ExitGate = kind;

            int met = 0, allKind = 0;
            var got = new List<int>();
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = new IslandGenerator().Generate(FirstSeed + i * 6151, p);
                int exits = 0, wrong = 0;
                foreach (Gate g in d.Gates)
                {
                    if (g.Role != GateRole.Exit) continue;
                    exits++;
                    if (kind != GateKind.Auto && g.Kind != kind) wrong++;
                }
                got.Add(exits);
                if (exits >= count) met++;
                if (wrong == 0) allKind++;
            }
            got.Sort();
            GD.Print($"  {$"{count} x {kind}",-24} {Pct(met, SweepSeeds),13} "
                + $"{Pct(allKind, SweepSeeds),14} {got[got.Count / 2],11}"
                + (met < SweepSeeds ? "   <-- look" : ""));
        }
    }

    private static string Pct(int part, int whole)
        => whole == 0 ? "-" : $"{100 * part / whole}%";

    /// <summary>
    /// The hardest Gate request there is, against every shape the generator can
    /// build: <b>four hanging Gates</b> — an Entry and three Exits, one per edge,
    /// every one of them flown to.
    ///
    /// <para>It is the right thing to test because it is the maximum. A hanging
    /// Gate needs a coast that will give it a 3 × 5 landing strip with clear air
    /// off the rim, on the right side of the island, a third of the footprint from
    /// every other Gate — four times over, once per edge. Anything that can do
    /// that can do fewer Gates and can do land Gates, which need a forecourt but
    /// no flight path.</para>
    ///
    /// <para>"Can do fewer" is a claim and not a fact, though, so the reductions
    /// are measured too rather than assumed: three, two and one Exit, and land
    /// Gates at both ends. A rule that only fires when the pool of edges is full
    /// would pass the maximum and fail the middle.</para>
    /// </summary>
    private void PrintGateMatrix()
    {
        GD.Print($"\n=== four hanging gates, every arrangement x character "
            + $"({FeasibilitySeeds} seeds each) ===");
        GD.Print("  asked: entry hanging, 3 exits hanging. 'four' = all four placed AND all hanging.");

        var byArrangement = new Dictionary<IslandArrangement, (int Four, int Gates, int Hanging, int Runs)>();
        var byCharacter = new Dictionary<TerrainCharacter, (int Four, int Runs)>();
        var worst = new List<(string Combo, int Four, float Gates, float Hanging)>();

        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            foreach (TerrainCharacter what in Enum.GetValues<TerrainCharacter>())
            {
                if (what == TerrainCharacter.Auto) continue;

                var p = (IslandParams)Params.Duplicate();
                p.Arrangement = how;
                p.Character = what;
                p.EntryGate = GateKind.Hanging;
                p.ExitGate = GateKind.Hanging;
                p.ExitGates = 3;

                int four = 0, gates = 0, hanging = 0;
                for (int i = 0; i < FeasibilitySeeds; i++)
                {
                    IslandData d = new IslandGenerator().Generate(FirstSeed + i * 6151, p);
                    int here = d.Gates.Count, air = 0;
                    foreach (Gate g in d.Gates) if (g.Kind == GateKind.Hanging) air++;
                    gates += here;
                    hanging += air;
                    if (here == 4 && air == 4) four++;
                }

                var a = byArrangement.GetValueOrDefault(how);
                byArrangement[how] = (a.Four + four, a.Gates + gates, a.Hanging + hanging,
                                      a.Runs + FeasibilitySeeds);
                var c = byCharacter.GetValueOrDefault(what);
                byCharacter[what] = (c.Four + four, c.Runs + FeasibilitySeeds);

                if (four < FeasibilitySeeds)
                    worst.Add(($"{how} / {what}", four, gates / (float)FeasibilitySeeds,
                               hanging / (float)FeasibilitySeeds));
            }
        }

        GD.Print($"\n  {"arrangement",-16} {"four hanging",13} {"gates",7} {"hanging",8}");
        foreach (var (how, v) in byArrangement.OrderBy(k => k.Value.Four / (float)k.Value.Runs))
            GD.Print($"  {how,-16} {Pct(v.Four, v.Runs),13} "
                + $"{v.Gates / (float)v.Runs,7:0.0} {v.Hanging / (float)v.Runs,8:0.0}"
                + (v.Four < v.Runs ? "   <-- look" : ""));

        GD.Print($"\n  {"character",-16} {"four hanging",13}");
        foreach (var (what, v) in byCharacter.OrderBy(k => k.Value.Four / (float)k.Value.Runs))
            GD.Print($"  {what,-16} {Pct(v.Four, v.Runs),13}");

        int combos = 0, clean = 0;
        foreach (var (_, v) in byArrangement) { combos += v.Runs; clean += v.Four; }
        GD.Print($"\n  overall: {Pct(clean, combos)} of runs gave four hanging gates "
            + $"({combos} runs over {byArrangement.Count * byCharacter.Count} combinations)");

        worst.Sort((a, b) => a.Four != b.Four ? a.Four.CompareTo(b.Four)
                                              : a.Hanging.CompareTo(b.Hanging));
        GD.Print($"\n  the combinations that could not: {worst.Count}");
        for (int i = 0; i < Math.Min(worst.Count, 20); i++)
            GD.Print($"    {worst[i].Combo,-34} four on {worst[i].Four}/{FeasibilitySeeds} seeds,"
                + $" {worst[i].Gates:0.0} gates of which {worst[i].Hanging:0.0} hanging");

        // ---- and where the hanging Gates actually die --------------------------
        // Four tests stand between a coast cell and a hanging Gate. Knowing how
        // many Gates were placed does not say which test refused, so the funnel is
        // counted per character: usable ground, then the edge rules, then a
        // landing strip, then a flight path to it.
        GD.Print($"\n  why a coast will not take a hanging gate, per character"
            + $"  ({FeasibilitySeeds} seeds, all four edges)");
        GD.Print($"  {"character",-14} {"rung",7} {"usable",8} {"on its edge",12} "
            + $"{"has strip",10} {"flyable",9}   (cells surviving each test)");

        foreach (TerrainCharacter what in Enum.GetValues<TerrainCharacter>())
        {
            if (what == TerrainCharacter.Auto) continue;
            var p = (IslandParams)Params.Duplicate();
            p.Character = what;
            p.EntryGate = GateKind.Hanging;
            p.ExitGate = GateKind.Hanging;
            p.ExitGates = 3;

            var data = new List<IslandData>();
            for (int i = 0; i < FeasibilitySeeds; i++)
                data.Add(new IslandGenerator().Generate(FirstSeed + i * 6151, p));

            (string Label, bool Loose)[] rungs = { ("strict", false), ("loose", true) };
            foreach (var (label, loose) in rungs)
            {
                long usable = 0, fits = 0, strip = 0, flyable = 0;
                int edgesOffering = 0;
                foreach (IslandData d in data)
                foreach (Cardinal edge in Enum.GetValues<Cardinal>())
                {
                    var (u, f, s, y) = GatePlacement.Funnel(d, edge, loose);
                    usable += u; fits += f; strip += s; flyable += y;
                    if (y > 0) edgesOffering++;
                }
                float runs = FeasibilitySeeds;
                GD.Print($"  {(label == "strict" ? what.ToString() : ""),-14} {label,7} "
                    + $"{usable / runs,8:0} {fits / runs,12:0} "
                    + $"{strip / runs,10:0} {flyable / runs,9:0} "
                    + $"{edgesOffering / runs,8:0.0} of 4 edges");
            }
        }

        // ---- and the reductions, which are not free just because the maximum is -
        GD.Print("\n  reductions, on Auto x Auto — asking for less has to work too");
        GD.Print($"  {"asked",-30} {"count met",11} {"kind met",10} {"median got",11}");
        (string Label, GateKind Entry, GateKind Exit, int Count)[] cases =
        {
            ("entry hanging + 3 hanging", GateKind.Hanging, GateKind.Hanging, 3),
            ("entry hanging + 2 hanging", GateKind.Hanging, GateKind.Hanging, 2),
            ("entry hanging + 1 hanging", GateKind.Hanging, GateKind.Hanging, 1),
            ("entry land + 3 land", GateKind.Land, GateKind.Land, 3),
            ("entry land + 2 land", GateKind.Land, GateKind.Land, 2),
            ("entry land + 1 land", GateKind.Land, GateKind.Land, 1),
            ("entry land + 3 hanging", GateKind.Land, GateKind.Hanging, 3),
            ("entry hanging + 3 land", GateKind.Hanging, GateKind.Land, 3),
        };
        foreach (var (label, entry, exit, count) in cases)
        {
            var p = (IslandParams)Params.Duplicate();
            p.EntryGate = entry;
            p.ExitGate = exit;
            p.ExitGates = count;

            int met = 0, kind = 0;
            var got = new List<int>();
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = new IslandGenerator().Generate(FirstSeed + i * 6151, p);
                int exits = 0, wrong = 0;
                foreach (Gate g in d.Gates)
                {
                    if (g.Role == GateRole.Entry) { if (g.Kind != entry) wrong++; continue; }
                    exits++;
                    if (g.Kind != exit) wrong++;
                }
                got.Add(exits);
                if (exits >= count) met++;
                if (wrong == 0) kind++;
            }
            got.Sort();
            GD.Print($"  {label,-30} {Pct(met, SweepSeeds),11} {Pct(kind, SweepSeeds),10} "
                + $"{got[got.Count / 2],11}"
                + (met < SweepSeeds || kind < SweepSeeds ? "   <-- look" : ""));
        }
    }

    /// <summary>
    /// What each water knob is worth, stepped from 0 to 1 with everything else
    /// held at the preset. A slider whose column does not climb is a slider that
    /// does nothing, and that is a thing a summary at one setting cannot say.
    /// </summary>
    private void PrintKnobs()
    {
        GD.Print($"\n=== water knobs, swept 0 to 1 ({SweepSeeds} seeds each, "
            + "everything else held) ===");

        float[] steps = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        GD.Print($"\n  {"lakes",6} {"lake cells",11} {"lakes",7} {"biggest",8}   "
            + "(area, not just how many)");
        foreach (float v in steps)
        {
            var p = (IslandParams)Params.Duplicate();
            p.Lakes = v;
            long cells = 0, bodies = 0, biggest = 0;
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = new IslandGenerator().Generate(FirstSeed + i * 6151, p);
                var perRegion = new Dictionary<int, int>();
                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (d.WaterLevel[x, z] == IslandData.NoLand || d.River[x, z]) continue;
                    // Not the goo: it ignores this knob, being no kind of lake.
                    if (d.Fluid[x, z] != (byte)FluidKind.Water) continue;
                    cells++;
                    int r = d.Region[x, z];
                    perRegion[r] = perRegion.GetValueOrDefault(r) + 1;
                }
                bodies += perRegion.Count;
                foreach (int area in perRegion.Values) biggest = Math.Max(biggest, area);
            }
            GD.Print($"  {v,6:0.00} {cells / (float)SweepSeeds,11:0.0} "
                + $"{bodies / (float)SweepSeeds,7:0.0} {biggest,8}");
        }

        GD.Print($"\n  {"rivers",6} {"river cells",12} {"navigable",10} {"falls",7}");
        foreach (float v in steps)
        {
            var p = (IslandParams)Params.Duplicate();
            p.Rivers = v;
            long cells = 0, navigable = 0, falls = 0;
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = new IslandGenerator().Generate(FirstSeed + i * 6151, p);
                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (!d.River[x, z]) continue;
                    cells++;
                    if (d.Navigable[x, z]) navigable++;
                }
                falls += d.Falls.Count;
            }
            GD.Print($"  {v,6:0.00} {cells / (float)SweepSeeds,12:0.0} "
                + $"{navigable / (float)SweepSeeds,10:0.0} {falls / (float)SweepSeeds,7:0.0}");
        }

        // The gorge question at every bridge span. The preset's Medium answers
        // "how often can a walled river not be bridged" with zero — but Easy
        // Domains only span one cell, so a two-cell channel is a wall there by
        // rule, and misalignment is the one thing that can seal a stream gorge.
        // This is where the frustration would live if it lived anywhere.
        GD.Print($"\n  {"crossings",-10} {"reaches",8} {"sealed",7} {"misaligned",11} "
            + $"{"worst walk",11}");
        foreach (BridgeEase ease in new[] { BridgeEase.Easy, BridgeEase.Medium, BridgeEase.Hard })
        {
            var p = (IslandParams)Params.Duplicate();
            p.Crossings = ease;
            int cells = 0, cross = 0, sealedUp = 0, skew = 0, reaches = 0;
            var lens = new List<int>();
            var sealedLens = new List<int>();
            var walks = new List<int>();
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = new IslandGenerator().Generate(FirstSeed + i * 6151, p);
                reaches += AnalyseGorges(d, ref cells, lens, sealedLens, walks,
                                         ref cross, ref sealedUp, ref skew);
            }
            int worst = 0;
            foreach (int w in walks) worst = Math.Max(worst, w);
            GD.Print($"  {ease,-10} {reaches,8} {sealedUp,7} {skew,11} {worst,11}");
        }

        // The rise is the valley; the rest is what deepening every channel on the
        // island costs. A knob that makes the terrain prettier and the Domain
        // unwalkable is not a knob that works.
        // Per river, not per island. `Valleys` slides a window across the courses,
        // so what matters is how many of them have a valley at all and how much
        // they differ — an island-wide mean would read the same whether every
        // river had half a valley or half the rivers had a whole one.
        GD.Print($"\n  {"valleys",7} {"rise 1->5",10} {"valleyed",12} {"deepest",8} "
            + $"{"2-slab",7} {"walk%",7} {"berths",7}");
        foreach (float v in steps)
        {
            var p = (IslandParams)Params.Duplicate();
            p.Valleys = v;
            double total = 0;
            int counted = 0;
            long steep = 0, berths = 0, walk = 0, dry = 0;
            var each = new List<double>();
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = new IslandGenerator().Generate(FirstSeed + i * 6151, p);
                if (ValleyRise(d, out double rise, each)) { total += rise; counted++; }
                berths += d.Berths.Count;

                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                    dry++;
                    if (d.Mainland >= 0 && d.Walk[x, z] == d.Mainland) walk++;
                    for (int k = 0; k < 2; k++)
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        if (!d.HasLand(nx, nz)) continue;
                        if (Math.Abs(d.SurfaceLevel(x, z) - d.SurfaceLevel(nx, nz)) == 2) steep++;
                    }
                }
            }
            // A course counts as having a valley when the ground gains a slab or
            // more over the five cells out from it. The row at 0.00 is the
            // control: a river runs in low ground anyway, so some courses clear
            // the bar on natural relief alone and the column is worth reading as
            // a rise above that row rather than as an absolute.
            int withValley = 0;
            double deepest = 0;
            foreach (double one in each)
            {
                if (one >= 1.0) withValley++;
                deepest = Math.Max(deepest, one);
            }

            GD.Print($"  {v,7:0.00} {(counted > 0 ? total / counted : 0),10:0.00} "
                + $"{$"{withValley}/{each.Count}",12} {deepest,8:0.0} "
                + $"{steep / (float)SweepSeeds,7:0.0} {(dry > 0 ? 100.0 * walk / dry : 0),7:0.0} "
                + $"{berths / (float)SweepSeeds,7:0.0}");
        }
    }

    /// <summary>
    /// How many slabs the ground gains between one cell from a watercourse and
    /// five, averaged over the island. This is what a valley <i>is</i> — the land
    /// falling toward its river for a long way before it reaches it — and it is
    /// measurable where "does the valley pass look right" is not.
    /// </summary>
    /// <summary>
    /// Whether the island's walled river reaches can actually be bridged.
    ///
    /// A river running between two cliffs is fine — the grammar makes gorges on
    /// purpose, and not every river should be crossable everywhere. But a gorge
    /// whose two rims never line up within a deck's tolerance <i>anywhere along
    /// its length</i> is a wall with water at the bottom: the only way across is
    /// to walk the whole reach round. This measures how often that happens,
    /// using the exact rule the reach flood builds bridges with —
    /// <see cref="Traversal.Walkable"/> endpoints, <see cref="Traversal.DeckFits"/>
    /// over the gap, levels within <see cref="Traversal.MaxBridgeRise"/> — so
    /// what it reports is what the game would let you build, not a re-derivation.
    ///
    /// A <b>gorge cell</b> is a river cell with dry ground three slabs or more
    /// above its water on both sides of one axis; a <b>reach</b> is a
    /// 4-connected run of them, counted from three cells long, since a one-cell
    /// gorge is a doorway rather than a wall. A reach is <b>sealed</b> when no
    /// legal deck crosses any of its cells on either axis, and <b>misaligned</b>
    /// when, additionally, a deck's geometry fit somewhere along it and only the
    /// rims' disagreement refused it — the pure frustration case the analysis
    /// exists to count.
    /// </summary>
    private static int AnalyseGorges(IslandData d, ref int cells, List<int> lengths,
                                     List<int> sealedLengths, List<int> detours,
                                     ref int crossable, ref int shut, ref int skew)
    {
        int n = d.Size;
        int span = Math.Max(1, d.BridgeSpan);

        var walled = new bool[n, n];
        var canCross = new bool[n, n];
        var riseOnly = new bool[n, n];

        // The first dry ground out from the water on this side, looked for
        // through the channel itself — a navigable river is two cells across,
        // and its gorge wall stands beyond its partner, not beside each cell.
        bool Rim(int x, int z, int dx, int dz, short w)
        {
            for (int step = 1; step <= 3; step++)
            {
                int nx = x + dx * step, nz = z + dz * step;
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) return false;
                if (!d.HasLand(nx, nz)) return false;              // the island's rim
                if (d.WaterLevel[nx, nz] != IslandData.NoLand) continue;
                return d.SurfaceLevel(nx, nz) - w >= 3;
            }
            return false;
        }

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.River[x, z]) continue;
            short w = d.WaterLevel[x, z];

            for (int axis = 0; axis < 2; axis++)
            {
                int dx = axis == 0 ? 1 : 0, dz = axis == 0 ? 0 : 1;
                if (Rim(x, z, -dx, -dz, w) && Rim(x, z, dx, dz, w))
                    walled[x, z] = true;

                // Every deck whose run crosses this cell on this axis: near end
                // i cells back, far end j cells on, the whole thing inside the
                // span the reach flood would allow.
                for (int i = 1; i <= span && !canCross[x, z]; i++)
                for (int j = 1; i + j <= span + 1; j++)
                {
                    int ax = x - dx * i, az = z - dz * i;
                    int bx = x + dx * j, bz = z + dz * j;
                    if (ax < 0 || az < 0 || bx >= n || bz >= n) continue;
                    if (!Traversal.Walkable(d, ax, az)
                        || !Traversal.Walkable(d, bx, bz)) continue;
                    if (!Traversal.DeckFits(d, ax, az, dx, dz, i + j, span)) continue;
                    int rise = Math.Abs(Traversal.CrossLevel(d, ax, az)
                                        - Traversal.CrossLevel(d, bx, bz));
                    if (rise > Traversal.MaxBridgeRise) { riseOnly[x, z] = true; continue; }
                    canCross[x, z] = true;
                    break;
                }
            }
        }

        var seen = new bool[n, n];
        var stack = new Stack<(int X, int Z)>();
        var members = new List<(int X, int Z)>();
        int reaches = 0;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!walled[sx, sz] || seen[sx, sz]) continue;

            members.Clear();
            bool anyCross = false, anySkew = false;
            seen[sx, sz] = true;
            stack.Push((sx, sz));
            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Pop();
                members.Add((cx, cz));
                anyCross |= canCross[cx, cz];
                anySkew |= riseOnly[cx, cz];
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n || seen[nx, nz]) continue;
                    if (!walled[nx, nz]) continue;
                    seen[nx, nz] = true;
                    stack.Push((nx, nz));
                }
            }

            cells += members.Count;
            if (members.Count < 3) continue;
            reaches++;
            lengths.Add(members.Count);
            if (!anyCross)
            {
                shut++;
                sealedLengths.Add(members.Count);
                if (anySkew) skew++;
                continue;
            }
            crossable++;

            // How far the walk to the nearest deck is from the worst cell of
            // the reach — one site on a fifty-cell gorge is still a detour, and
            // this is the number that says how long a one.
            var dist = new Dictionary<(int X, int Z), int>();
            var q = new Queue<(int X, int Z)>();
            foreach (var m in members)
                if (canCross[m.X, m.Z]) { dist[m] = 0; q.Enqueue(m); }
            while (q.Count > 0)
            {
                var (cx, cz) = q.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    var next = (X: cx + Dx[k], Z: cz + Dz[k]);
                    if (next.X < 0 || next.Z < 0 || next.X >= n || next.Z >= n) continue;
                    if (!walled[next.X, next.Z] || dist.ContainsKey(next)) continue;
                    dist[next] = dist[(cx, cz)] + 1;
                    q.Enqueue(next);
                }
            }
            int worst = 0;
            foreach (var m in members)
                if (dist.TryGetValue(m, out int got)) worst = Math.Max(worst, got);
            detours.Add(worst);
        }
        return reaches;
    }

    private static bool ValleyRise(IslandData d, out double rise, List<double>? perRiver = null)
    {
        rise = 0;
        int n = d.Size;
        var dist = new int[n, n];
        var basin = new int[n, n];
        var q = new Queue<(int X, int Z)>();

        // Which watercourse each cell belongs to, carried out with the distance —
        // `Valleys` now acts per river, so a single island-wide average would hide
        // exactly the thing the knob is for: at a half, some courses should have a
        // narrow valley and some a wide one.
        int rivers = LabelRivers(d, basin);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            dist[x, z] = -1;
            if (!d.River[x, z]) continue;
            dist[x, z] = 0;
            q.Enqueue((x, z));
        }
        if (q.Count == 0) return false;

        const int Far = 5;
        while (q.Count > 0)
        {
            (int cx, int cz) = q.Dequeue();
            if (dist[cx, cz] >= Far) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + Dx[k], nz = cz + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (dist[nx, nz] >= 0 || !d.HasLand(nx, nz)) continue;
                dist[nx, nz] = dist[cx, cz] + 1;
                basin[nx, nz] = basin[cx, cz];
                q.Enqueue((nx, nz));
            }
        }

        var near = new double[rivers];
        var far = new double[rivers];
        var nearN = new int[rivers];
        var farN = new int[rivers];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (d.WaterLevel[x, z] != IslandData.NoLand) continue;
            int b = basin[x, z];
            if (b < 0 || b >= rivers) continue;
            if (dist[x, z] == 1) { near[b] += d.SurfaceLevel(x, z); nearN[b]++; }
            else if (dist[x, z] == Far) { far[b] += d.SurfaceLevel(x, z); farN[b]++; }
        }

        double total = 0;
        int counted = 0;
        for (int b = 0; b < rivers; b++)
        {
            if (nearN[b] == 0 || farN[b] == 0) continue;
            double one = far[b] / farN[b] - near[b] / nearN[b];
            perRiver?.Add(one);
            total += one;
            counted++;
        }
        if (counted == 0) return false;

        rise = total / counted;
        return true;
    }

    /// <summary>4-connected components of the channel network: one river each.</summary>
    private static int LabelRivers(IslandData d, int[,] basin)
    {
        int n = d.Size;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) basin[x, z] = -1;

        int count = 0;
        var stack = new Stack<(int X, int Z)>();
        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!d.River[sx, sz] || basin[sx, sz] >= 0) continue;
            int id = count++;
            basin[sx, sz] = id;
            stack.Push((sx, sz));
            while (stack.Count > 0)
            {
                (int cx, int cz) = stack.Pop();
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!d.River[nx, nz] || basin[nx, nz] >= 0) continue;
                    basin[nx, nz] = id;
                    stack.Push((nx, nz));
                }
            }
        }
        return count;
    }

    private static bool Wet(IslandData d, int x, int z)
        => x >= 0 && z >= 0 && x < d.Size && z < d.Size
           && d.WaterLevel[x, z] != IslandData.NoLand;

    /// <summary>
    /// A close-up height map of one patch of each sculpted landform, as digits.
    ///
    /// The shape of a gully field, a tower field and a terrace stack is the whole
    /// point of those landforms, and it is exactly the kind of thing that cannot
    /// be checked from a summary statistic — "gully wall: median 5 slabs" is true
    /// of a maze and of a single trench alike. A window of the height field says
    /// which one it is without opening the lab.
    /// </summary>
    private void PrintSculpts()
    {
        var wanted = new (TerrainCharacter Character, LandformType Form)[]
        {
            (TerrainCharacter.Badlands, LandformType.Badlands),
            (TerrainCharacter.Karst, LandformType.Karst),
            (TerrainCharacter.Massif, LandformType.Massif),
            (TerrainCharacter.Dunes, LandformType.Dunes),
            (TerrainCharacter.Karst, LandformType.Sinkholes),
        };

        foreach ((TerrainCharacter character, LandformType form) in wanted)
        {
            var p = (IslandParams)Params.Duplicate();
            p.Character = character;
            p.Arrangement = IslandArrangement.Single;
            IslandData d = new IslandGenerator().Generate(FirstSeed, p);
            int n = d.Size;

            // The biggest patch of the landform in question, and its middle.
            var area = new Dictionary<int, (int Cells, int SumX, int SumZ)>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.HasLand(x, z) || (LandformType)d.Landform[x, z] != form) continue;
                area.TryGetValue(d.Region[x, z], out var had);
                area[d.Region[x, z]] = (had.Cells + 1, had.SumX + x, had.SumZ + z);
            }
            if (area.Count == 0) { GD.Print($"--- {form}: none on seed {FirstSeed} ---"); continue; }

            int best = -1;
            foreach (var (r, v) in area) if (best < 0 || v.Cells > area[best].Cells) best = r;
            var (cells, sumX, sumZ) = area[best];
            int cx = sumX / cells, cz = sumZ / cells;

            const int Half = 26;
            int x0 = Math.Max(0, cx - Half), x1 = Math.Min(n - 1, cx + Half);
            int z0 = Math.Max(0, cz - Half), z1 = Math.Min(n - 1, cz + Half);

            short low = short.MaxValue, high = short.MinValue;
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!d.HasLand(x, z)) continue;
                low = Math.Min(low, d.SurfaceLevel(x, z));
                high = Math.Max(high, d.SurfaceLevel(x, z));
            }
            if (low > high) continue;

            GD.Print($"--- {form} on a {character} island, seed {FirstSeed}: "
                + $"{cells} cells, heights {low}..{high} slabs ---");
            GD.Print("    each digit is a tenth of that range; ':' is off the patch, '.' is aether");
            for (int z = z0; z <= z1; z++)
            {
                var row = new System.Text.StringBuilder();
                for (int x = x0; x <= x1; x++)
                {
                    if (!d.HasLand(x, z)) { row.Append('.'); continue; }
                    int step = Math.Clamp((d.SurfaceLevel(x, z) - low) * 10 / Math.Max(1, high - low),
                                          0, 9);
                    row.Append((LandformType)d.Landform[x, z] == form
                        ? (char)('0' + step)
                        : ':');
                }
                GD.Print(row.ToString());
            }
        }
    }

    /// <summary>Corner-only touches: a join you can neither walk nor swim through.</summary>
    private static int DiagonalOnly(int n, Func<int, int, bool> inSet, int[,]? sameAs = null)
    {
        bool Same(int ax, int az, int bx, int bz)
            => sameAs == null || sameAs[ax, az] == sameAs[bx, bz];

        int bad = 0;
        for (int x = 0; x + 1 < n; x++)
        for (int z = 0; z + 1 < n; z++)
        {
            bool a = inSet(x, z), b = inSet(x + 1, z + 1);
            bool c = inSet(x + 1, z), e = inSet(x, z + 1);
            if (a && b && !c && !e && Same(x, z, x + 1, z + 1)) bad++;
            if (c && e && !a && !b && Same(x + 1, z, x, z + 1)) bad++;
        }
        return bad;
    }
}
