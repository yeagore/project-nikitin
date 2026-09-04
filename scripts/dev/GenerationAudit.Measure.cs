using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

public partial class GenerationAudit
{
    /// <summary>One generated island and the views the measures read it through.</summary>
    private readonly struct Island
    {
        public readonly int Seed;
        public readonly IslandData D;
        public readonly int N;

        public Island(int seed, IslandData d)
        {
            Seed = seed;
            D = d;
            N = d.Size;
        }

        public short Top(int x, int z) => D.SurfaceLevel(x, z);
        public bool Land(int x, int z) => InBounds(N, x, z) && D.HasLand(x, z);

        /// <summary>The level a column is crossed at: a stream is forded at the water, not at its cut bed.</summary>
        public short Cross(int x, int z)
            => D.River[x, z] && !D.Navigable[x, z] ? D.WaterLevel[x, z] : D.SurfaceLevel(x, z);

        /// <summary>Ground you step on; a navigable river is a gap to bridge, not a step to take.</summary>
        public bool Ground(int x, int z) => Land(x, z) && !D.Navigable[x, z]
                                            && (D.River[x, z] || D.WaterLevel[x, z] == IslandData.NoLand);

        public LandformType Form(int x, int z) => (LandformType)D.Landform[x, z];
    }

    /// <summary>
    /// The summary's accumulators, filled by one Measure* per section for every island.
    /// Every field is a commutative sum or a list Report sorts, except the dictionaries,
    /// whose insertion order (seed order, then cell scan order) is the print order of ties.
    /// </summary>
    private sealed class Tally
    {
        private readonly IslandParams _p;

        public Tally(IslandParams p) { _p = p; }

        // ---- step grammar
        public long Free, Ambiguous, Cliff;
        public long AmbiguousOffMountain, PairsOffMountain;
        public readonly Dictionary<string, int> CliffByBorder = new();
        public readonly Dictionary<string, int> AmbiguousWhere = new();
        public long Pairs => Free + Ambiguous + Cliff;

        // ---- patches, mesas and basins, hills, mountains
        public readonly List<int> PatchSizes = new();
        public int PatchesUndersized;
        public readonly List<int> MesaClear = new();
        public readonly List<int> BasinDrop = new();
        public int MesaTouchesMountain, MesaTouchesOther;
        public readonly List<int> HillsRelief = new();
        public readonly List<int> HillsSpan = new();
        public readonly List<int> MountainRise = new();
        public int FootPairs, FootDrops;
        public readonly Dictionary<int, List<int>> StepByBand = new();

        // ---- rivers
        public int RiverCells, NavigableCells, FallCells, RimFalls;
        public readonly List<int> InnerFalls = new();
        public int IslandsWithRiver, RiverIslandsReachingRim;
        public int RiverUphill, RiverDry;
        public readonly List<int> RiverPerIsland = new();
        public int RiverStraight, RiverBends, EyotCells;
        public readonly List<int> StraightRuns = new();
        public int ReachCells => RiverStraight + RiverBends;

        // ---- ferries, surfaces, anchors, habitat
        public const int RuggedBins = 7;
        public int Berths, WaterBodies, IslandsWithBerth, BadQuay, BerthSites;
        public readonly long[] MaterialCells = new long[Enum.GetValues<SurfaceMaterial>().Length];
        public long CoastAnchors, CliffAnchors, BeachCells, FordCells, LandingCells;
        public long CliffFootAnchors, BankAnchors, SummitAnchors, RiverBedAnchors, LakeBedAnchors;
        public long BrinksBesideWater, BeachedCoast;
        public int IslandsWithoutBeach;
        public readonly List<int> MoistureMeans = new();
        public readonly List<int> WarmthMeans = new();
        public readonly List<int> RuggedMeans = new();
        public readonly List<int> ExposureMeans = new();
        public readonly List<int> RimMeans = new();
        public readonly List<int> QuayRise = new();

        /// <summary>Ruggedness summed over dry land by cells from fresh water: 0 is the bank, the last bin is everything further.</summary>
        public readonly long[] RuggedByWater = new long[RuggedBins];
        public readonly long[] LandByWater = new long[RuggedBins];

        // ---- roads
        public int ExitsWithoutRoad, RoadsFree, RoadJumps, RoughIslands, Flights;
        public int RoadStairs, RoadBridges, RoadFerries;
        public readonly List<int> RoadCosts = new();
        public readonly List<int> RoadLengths = new();

        // ---- sculpted landforms
        public readonly List<int> GullyDepths = new();
        public readonly List<int> TowerRises = new();
        public readonly List<int> TerraceSteps = new();
        public readonly List<int> SinkDepths = new();

        // ---- lakes, goo, the cube's lid, gorges
        public int Lakes, LakeCells, Leaks, WaterAtVoid, IslandsWithLake;
        public readonly List<int> ShoreSteps = new();
        public readonly List<int> LakeBodySizes = new();
        public int GooCells, GooIslands, GooTouchesWater;
        public readonly List<int> AltSpans = new();
        public int AltOverCap;
        public int GorgeCells, GorgeReaches, GorgeCrossable, GorgeSealed;
        public int GorgeMisaligned, GorgeIslands;
        public readonly List<int> GorgeLengths = new();
        public readonly List<int> GorgeSealedLengths = new();
        public readonly List<int> GorgeDetours = new();

        // ---- continuity, overhangs
        public int Landmasses, DiagonalLand, DiagonalWater;
        public int OverhangCells, OverhangIslands;
        public readonly List<int> LipAir = new();
        public readonly Dictionary<IslandArrangement, (int Islands, int Masses, int Linked)> ByArrangement = new();

        // ---- gates, crossings, shelves, guarantees
        public int NoEntry, BadExitCount, SharedEdge, WrongEntryKind;
        public int GateOffHeartland, GateApronShort, GateInWater, GateOutOfBox;
        public int LandGates, HangingGates, StripMissing, HangingOnLand;
        public int GateInCorner, GateNotOutermost, GatesCrowded;
        public readonly List<int> GateBehind = new();
        public int Crossings, DeckSteep, DeckOffBank;
        public readonly List<int> CrossingSpans = new();
        public readonly List<int> ShelfDrops = new();
        public readonly List<int> Attempts = new();
        public int Unplayable;
        public int AirstripIslands;
        public readonly List<int> AirstripCells = new();
        public readonly List<int> ExitCounts = new();
        public readonly List<int> GateSpacing = new();
        public readonly List<int> ApronSizes = new();
        public readonly List<int> StripLengths = new();

        // ---- what the character delivered
        public readonly Dictionary<TerrainCharacter, int> CharIslands = new();
        public readonly Dictionary<TerrainCharacter, int[]> CharHas = new();

        // ---- walkability, passes, shelves
        public long WalkLand, WalkMainland, WalkBroken;
        public long MesaCells, MesaOnMainland;
        public int Districts, Scraps;
        public readonly List<int> MainlandShare = new();
        public readonly List<int> StrandedShare = new();
        public readonly List<int> ReachShare = new();
        public long ReachHeartland;
        public int IslandsFullyReachable;
        public long MesaReachable;
        public readonly long[] StrandedByForm = new long[Forms];
        public int Passes, PassIslands, PassesJoined;
        public long PassCells;
        public readonly List<int> PassGrade = new();
        public int BuildableShelves, IslandsWithShelf;
        public readonly List<int> WidestShelf = new();
        public readonly List<int> ShelfOffMainland = new();

        /// <summary>Every section, in the order the summary was always measured in.</summary>
        public void Measure(Island v)
        {
            MeasureSteps(v);
            MeasurePatches(v);
            MeasureMesasAndBasins(v);
            MeasureHills(v);
            MeasureMountains(v);
            MeasureLakes(v);
            MeasureGoo(v);
            MeasureLid(v);
            MeasureGorges(v);
            MeasureCharacter(v);
            MeasureWalkability(v);
            MeasureRivers(v);
            MeasureStraightness(v);
            MeasureEyots(v);
            MeasureOverhangs(v);
            MeasureFerries(v);
            MeasureSurfaces(v);
            MeasureRoads(v);
            MeasureSculpts(v);
            MeasurePasses(v);
            MeasureShelves(v);
            MeasureGates(v);
            MeasureCrossings(v);
            MeasureGuarantees(v);
            MeasureAirstrips(v);
            MeasureContinuity(v);
        }

        /// <summary>The step grammar over every +X / +Z ground pair, and where the cliffs fall.</summary>
        private void MeasureSteps(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!v.Ground(x, z)) continue;
                for (int k = 0; k < 2; k++)                     // +X and +Z: each pair once
                {
                    int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                    if (!v.Ground(nx, nz)) continue;

                    int diff = Math.Abs(v.Cross(x, z) - v.Cross(nx, nz));
                    if (diff <= 1) Free++;
                    else if (diff == 2) Ambiguous++;
                    else Cliff++;

                    bool mountain = v.Form(x, z) == LandformType.Mountain
                                    || v.Form(nx, nz) == LandformType.Mountain;
                    if (!mountain)
                    {
                        PairsOffMountain++;
                        if (diff == 2)
                        {
                            AmbiguousOffMountain++;
                            int a2 = (int)v.Form(x, z), b2 = (int)v.Form(nx, nz);
                            string where = d.River[x, z] || d.River[nx, nz]
                                ? "riverbank"
                                : $"{TypeName[Math.Min(a2, b2)]}-{TypeName[Math.Max(a2, b2)]}";
                            AmbiguousWhere.TryGetValue(where, out int had);
                            AmbiguousWhere[where] = had + 1;
                        }
                    }

                    if (diff >= 3 && d.Region[x, z] != d.Region[nx, nz])
                    {
                        int a = (int)v.Form(x, z), b = (int)v.Form(nx, nz);
                        // Canyon walls and mountain flanks are deliberate cliffs, bucketed
                        // apart so they do not read as leaks in the landform rules.
                        string key = d.Canyon[x, z] || d.Canyon[nx, nz]
                            ? "canyon (any pair)"
                            : a == (int)LandformType.Mountain || b == (int)LandformType.Mountain
                            ? "mountain flank"
                            : $"{TypeName[Math.Min(a, b)]}-{TypeName[Math.Max(a, b)]}";
                        CliffByBorder.TryGetValue(key, out int c);
                        CliffByBorder[key] = c + 1;
                    }
                }
            }
        }

        private void MeasurePatches(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            var area = new Dictionary<int, int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!v.Land(x, z)) continue;
                area.TryGetValue(d.Region[x, z], out int c);
                area[d.Region[x, z]] = c + 1;
            }
            foreach (int a in area.Values)
            {
                PatchSizes.Add(a);
                if (a < _p.MinRegionArea) PatchesUndersized++;
            }
        }

        /// <summary>Worst clearance of each mesa and drop of each basin against the patches it meets.</summary>
        private void MeasureMesasAndBasins(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            var worstMesa = new Dictionary<int, int>();
            var worstBasin = new Dictionary<int, int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!v.Land(x, z)) continue;
                LandformType t = v.Form(x, z);
                if (t != LandformType.Mesa && t != LandformType.Basin) continue;
                int r = d.Region[x, z];

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!v.Land(nx, nz) || d.Region[nx, nz] == r) continue;
                    LandformType o = v.Form(nx, nz);

                    if (o == LandformType.Mountain) MesaTouchesMountain++;
                    else if (o != LandformType.Plain && o != t) MesaTouchesOther++;

                    if (o == t) continue;                       // stepped mesas / basins are fine
                    // Against the ground, not a channel: a river beside a basin runs below its floor by design.
                    if (d.River[nx, nz] || d.River[x, z]) continue;
                    int delta = v.Top(x, z) - v.Top(nx, nz);
                    var into = t == LandformType.Mesa ? worstMesa : worstBasin;
                    int signed = t == LandformType.Mesa ? delta : -delta;
                    if (!into.TryGetValue(r, out int cur) || signed < cur) into[r] = signed;
                }
            }
            MesaClear.AddRange(worstMesa.Values);
            BasinDrop.AddRange(worstBasin.Values);
        }

        /// <summary>Relief per Hills patch, with its width: the one-slab slope limit caps relief at about half the width.</summary>
        private void MeasureHills(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            var hiOf = new Dictionary<int, int>();
            var loOf = new Dictionary<int, int>();
            var wideOf = new Dictionary<int, int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!v.Land(x, z) || v.Form(x, z) != LandformType.Hills) continue;
                int r = d.Region[x, z];
                if (!hiOf.TryGetValue(r, out int hi) || v.Top(x, z) > hi) hiOf[r] = v.Top(x, z);
                if (!loOf.TryGetValue(r, out int lo) || v.Top(x, z) < lo) loOf[r] = v.Top(x, z);
                wideOf.TryGetValue(r, out int c);
                wideOf[r] = c + 1;
            }
            foreach (var (r, hi) in hiOf)
            {
                HillsRelief.Add(hi - loOf[r]);
                HillsSpan.Add((int)Math.Sqrt(wideOf[r]));
            }
        }

        /// <summary>Rise of each massif above its lowest foot, and its step size by ten inward bands.</summary>
        private void MeasureMountains(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            int[,] inward = InwardDistance(d, n);
            var peak = new Dictionary<int, int>();
            var footOf = new Dictionary<int, int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!v.Land(x, z) || v.Form(x, z) != LandformType.Mountain) continue;
                int r = d.Region[x, z];

                if (!peak.TryGetValue(r, out int hi) || v.Top(x, z) > hi) peak[r] = v.Top(x, z);

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!v.Land(nx, nz) || v.Form(nx, nz) == LandformType.Mountain) continue;
                    FootPairs++;
                    if (v.Top(nx, nz) > v.Top(x, z)) FootDrops++;   // massif below the ground it meets
                    if (!footOf.TryGetValue(r, out int lo) || v.Top(nx, nz) < lo) footOf[r] = v.Top(nx, nz);
                }
            }
            foreach (var (r, hi) in peak)
                if (footOf.TryGetValue(r, out int lo)) MountainRise.Add(hi - lo);

            int[] bandMax = MaxInwardPerRegion(d, inward, n);
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!v.Land(x, z) || v.Form(x, z) != LandformType.Mountain) continue;
                int r = d.Region[x, z];
                if (bandMax[r] <= 0) continue;
                int band = Math.Min(9, inward[x, z] * 10 / (bandMax[r] + 1));

                for (int k = 0; k < 2; k++)
                {
                    int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                    if (!v.Land(nx, nz) || d.Region[nx, nz] != r) continue;
                    if (!StepByBand.TryGetValue(band, out var list)) StepByBand[band] = list = new List<int>();
                    list.Add(Math.Abs(v.Top(x, z) - v.Top(nx, nz)));
                }
            }
        }

        /// <summary>Lake cells, regions and bodies, and the physics every standing fluid must obey.</summary>
        private void MeasureLakes(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            // Goo gets the same leak checks (a leak is a leak) but is not a lake and stays out of the lake counts.
            var lakeRegions = new HashSet<int>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                short w = d.WaterLevel[x, z];
                if (w == IslandData.NoLand || d.River[x, z]) continue;
                bool watery = d.Fluid[x, z] == (byte)FluidKind.Water;

                if (watery)
                {
                    LakeCells++;
                    lakeRegions.Add(d.Region[x, z]);
                }

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!v.Land(nx, nz)) { WaterAtVoid++; continue; }
                    if (d.WaterLevel[nx, nz] != IslandData.NoLand) continue;
                    if (v.Top(nx, nz) < w) Leaks++;               // dry ground *under* the water
                    else if (watery) ShoreSteps.Add(v.Top(nx, nz) - w);
                }
            }
            Lakes += lakeRegions.Count;
            if (lakeRegions.Count > 0) IslandsWithLake++;

            // Distinct bodies: a shaped lake is not one patch, so this and the region count can disagree.
            var bodyOf = new int[n, n];
            int bodies = Label(n, (x, z) => d.WaterLevel[x, z] != IslandData.NoLand && !d.River[x, z]
                                            && d.Fluid[x, z] == (byte)FluidKind.Water, bodyOf);
            var size = new int[bodies];
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (bodyOf[x, z] >= 0) size[bodyOf[x, z]]++;
            LakeBodySizes.AddRange(size);
        }

        /// <summary>Goo cells, and whether any stands within a king's move of water — it never mixes.</summary>
        private void MeasureGoo(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            int gooHere = 0;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (d.WaterLevel[x, z] == IslandData.NoLand
                    || d.Fluid[x, z] != (byte)FluidKind.Goo) continue;
                gooHere++;
                for (int ox = -1; ox <= 1; ox++)
                for (int oz = -1; oz <= 1; oz++)
                {
                    int nx = x + ox, nz = z + oz;
                    if (!InBounds(n, nx, nz)) continue;
                    if (d.WaterLevel[nx, nz] != IslandData.NoLand
                        && d.Fluid[nx, nz] == (byte)FluidKind.Water) GooTouchesWater++;
                }
            }
            GooCells += gooHere;
            if (gooHere > 0) GooIslands++;
        }

        /// <summary>Keel to peak against the cube's lid: a Domain is Size cells across and Size slabs tall.</summary>
        private void MeasureLid(Island v)
        {
            var (crest, bilge) = CubeLid(v.D);
            if (crest > short.MinValue)
            {
                AltSpans.Add(crest - bilge);
                if (crest - bilge > v.N) AltOverCap++;
            }
        }

        private void MeasureGorges(Island v)
        {
            GorgeStats g = AnalyseGorges(v.D);
            GorgeCells += g.Cells;
            GorgeLengths.AddRange(g.Lengths);
            GorgeSealedLengths.AddRange(g.SealedLengths);
            GorgeDetours.AddRange(g.Detours);
            GorgeCrossable += g.Crossable;
            GorgeSealed += g.Sealed;
            GorgeMisaligned += g.Skew;
            GorgeReaches += g.Reaches;
            if (g.Reaches > 0) GorgeIslands++;
        }

        /// <summary>Which landforms the character actually delivered on this island.</summary>
        private void MeasureCharacter(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            CharIslands.TryGetValue(d.Character, out int seen);
            CharIslands[d.Character] = seen + 1;
            if (!CharHas.TryGetValue(d.Character, out int[]? has) || has == null)
                CharHas[d.Character] = has = new int[Forms];

            var present = new bool[Forms];
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (v.Land(x, z)) present[(int)v.Form(x, z)] = true;
            for (int t = 0; t < Forms; t++) if (present[t]) has[t]++;
        }

        /// <summary>How much of the island is one piece on foot, and once built; what stays stranded.</summary>
        private void MeasureWalkability(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            long islandLand = 0, islandMainland = 0, islandHeart = 0;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!v.Land(x, z)) continue;
                islandLand++;
                int w = d.Walk[x, z];
                bool onMain = w == d.Mainland && w >= 0;
                if (onMain) islandMainland++;
                if (w >= 0 && !d.Areas[w].IsDistrict) WalkBroken++;

                if (d.Reach[x, z] == d.Heartland && d.Heartland >= 0) islandHeart++;
                else if (d.WaterLevel[x, z] == IslandData.NoLand) StrandedByForm[(int)v.Form(x, z)]++;

                if (v.Form(x, z) != LandformType.Mesa) continue;
                MesaCells++;
                if (onMain) MesaOnMainland++;
                if (d.Reach[x, z] == d.Heartland && d.Heartland >= 0) MesaReachable++;
            }
            WalkLand += islandLand;
            WalkMainland += islandMainland;
            ReachHeartland += islandHeart;
            if (islandLand > 0)
            {
                MainlandShare.Add((int)(100 * islandMainland / islandLand));
                StrandedShare.Add((int)(100 * (islandLand - islandMainland) / islandLand));
                ReachShare.Add((int)(100 * islandHeart / islandLand));
                // Flooded columns never join the heartland; the dry land around them is what has to.
                if (islandHeart >= DryCells(d)) IslandsFullyReachable++;
            }
            foreach (WalkArea a in d.Areas) { if (a.IsDistrict) Districts++; else Scraps++; }
        }

        /// <summary>River cells, cut channels, uphill flow, falls, and whether the water reaches the rim (there is no sea).</summary>
        private void MeasureRivers(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            int here = 0;
            bool reachedRim = false;
            var pours = new HashSet<(Vector2I, Vector2I)>();
            foreach (Fall f in d.Falls) pours.Add((f.Cell, f.Flow));
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.River[x, z]) continue;
                here++;
                RiverCells++;
                if (d.Navigable[x, z]) NavigableCells++;

                short level = d.WaterLevel[x, z];
                if (v.Land(x, z) && v.Top(x, z) >= level) RiverDry++;      // channel not cut

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!v.Land(nx, nz)) { reachedRim = true; continue; }
                    // Uphill: a downstream cell more than a slab above this one. Excused where it
                    // pours a drawn fall into this cell — Flow order is a heuristic, a fall is proof.
                    if (d.River[nx, nz] && d.WaterLevel[nx, nz] > level + 1
                        && d.Flow[nx, nz] > d.Flow[x, z]
                        && !pours.Contains((new Vector2I(nx, nz), new Vector2I(x - nx, z - nz))))
                        RiverUphill++;
                }
            }
            foreach (Fall f in d.Falls)
            {
                FallCells++;
                if (f.OffRim) RimFalls++;
                else InnerFalls.Add(f.Drop);
            }
            if (here > 0)
            {
                IslandsWithRiver++;
                RiverPerIsland.Add(here);
                if (reachedRim) RiverIslandsReachingRim++;
            }
        }

        /// <summary>How much of a course runs straight, and the longest run held in one direction.</summary>
        private void MeasureStraightness(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            // A cell with two river neighbours is on a reach: opposite means straight, at right angles a turn.
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.River[x, z] || d.Navigable[x, z]) continue;   // a 2-wide reach is not a line
                bool east = x + 1 < n && d.River[x + 1, z], west = x > 0 && d.River[x - 1, z];
                bool south = z + 1 < n && d.River[x, z + 1], north = z > 0 && d.River[x, z - 1];
                int touching = (east ? 1 : 0) + (west ? 1 : 0) + (south ? 1 : 0) + (north ? 1 : 0);
                if (touching != 2) continue;                        // a source, a mouth, a junction
                if ((east && west) || (north && south)) RiverStraight++;
                else RiverBends++;
            }

            foreach (bool alongZ in new[] { true, false })
            for (int a = 0; a < n; a++)
            {
                int run = 0;
                for (int b = 0; b <= n; b++)
                {
                    int x = alongZ ? a : b, z = alongZ ? b : a;
                    bool on = b < n && d.River[x, z] && !d.Navigable[x, z];
                    if (on) { run++; continue; }
                    if (run >= 3) StraightRuns.Add(run);
                    run = 0;
                }
            }
        }

        /// <summary>Dry cells with the river on both opposite sides: what a braided reach parts around.</summary>
        private void MeasureEyots(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            for (int x = 1; x + 1 < n; x++)
            for (int z = 1; z + 1 < n; z++)
            {
                if (!v.Land(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                bool acrossX = d.River[x - 1, z] && d.River[x + 1, z];
                bool acrossZ = d.River[x, z - 1] && d.River[x, z + 1];
                if (acrossX || acrossZ) EyotCells++;
            }
        }

        /// <summary>Columns carrying a second span, and the air between spans (zero would be one span written twice).</summary>
        private void MeasureOverhangs(Island v)
        {
            IslandData d = v.D;
            if (d.Overhangs.Count > 0) OverhangIslands++;
            foreach (Vector2I c in d.Overhangs)
            {
                Span[] spans = d.Spans[c.X, c.Y];
                OverhangCells++;
                for (int s = 1; s < spans.Length; s++)
                    LipAir.Add(spans[s].Bottom - spans[s - 1].Top - 1);
            }
        }

        private void MeasureFerries(Island v)
        {
            IslandData d = v.D;
            Berths += d.Berths.Count;
            BerthSites += d.BerthSites;
            WaterBodies += d.WaterBodies;
            if (d.Berths.Count > 0) IslandsWithBerth++;
            foreach (FerryBerth berth in d.Berths)
            {
                int rise = v.Cross(berth.Land.X, berth.Land.Y) - berth.Level;
                if (rise < 0 || rise > Traversal.MaxQuayRise) BadQuay++;
                if (!Traversal.Sailable(d, berth.Water.X, berth.Water.Y)) BadQuay++;
                QuayRise.Add(rise);
            }
        }

        /// <summary>The anchor lists, the material tally and the habitat means: what the biome layer will read.</summary>
        private void MeasureSurfaces(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            CoastAnchors += d.CoastCells.Count;
            CliffAnchors += d.CliffCells.Count;
            CliffFootAnchors += d.CliffFootCells.Count;
            BankAnchors += d.BankCells.Count;
            SummitAnchors += d.Summits.Count;
            RiverBedAnchors += d.RiverBedCells.Count;
            LakeBedAnchors += d.LakeBedCells.Count;
            foreach (Vector2I c in d.CoastCells) if (d.Beach[c.X, c.Y]) BeachedCoast++;

            // Brinks that are gorge rims: dry ground three slabs over the water itself.
            foreach (Vector2I c in d.CliffCells)
            {
                for (int k = 0; k < 4; k++)
                {
                    int bx = c.X + Dx[k], bz = c.Y + Dz[k];
                    if (!InBounds(n, bx, bz)) continue;
                    if (d.WaterLevel[bx, bz] == IslandData.NoLand) continue;
                    BrinksBesideWater++;
                    break;
                }
            }

            int beachHere = 0;
            long moistSum = 0, warmSum = 0, rugSum = 0, expSum = 0, rimSum = 0;
            int landHere = 0;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.HasLand(x, z)) continue;
                MaterialCells[d.Material[x, z]]++;
                if (d.Beach[x, z]) beachHere++;
                if (d.Ford[x, z]) FordCells++;
                if (d.Landings[x, z]) LandingCells++;
                landHere++;
                moistSum += d.Moisture[x, z];
                warmSum += d.Warmth[x, z];
                rugSum += d.Ruggedness[x, z];
                expSum += d.Exposure[x, z];
                rimSum += d.RimDistance[x, z];
            }
            BeachCells += beachHere;
            if (beachHere == 0) IslandsWithoutBeach++;

            // Ruggedness against distance from fresh water: a bank that reads broken is the water, not the country.
            int[,] toWater = Flood.Distance(n,
                (x, z) => d.HasLand(x, z) && d.WaterLevel[x, z] != IslandData.NoLand
                          && d.Fluid[x, z] != (byte)FluidKind.Goo,
                (_, _, nx, nz) => d.HasLand(nx, nz),
                cap: RuggedBins);
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                int bin = toWater[x, z] < 0 ? RuggedBins - 1 : Math.Min(RuggedBins - 1, toWater[x, z] - 1);
                RuggedByWater[bin] += d.Ruggedness[x, z];
                LandByWater[bin]++;
            }
            if (landHere > 0)
            {
                MoistureMeans.Add((int)(moistSum / landHere));
                WarmthMeans.Add((int)(warmSum / landHere));
                RuggedMeans.Add((int)(rugSum / landHere));
                ExposureMeans.Add((int)(expSum / landHere));
                RimMeans.Add((int)(rimSum / landHere));
            }
        }

        /// <summary>The roads from the Entry to each Exit: cost, length, works, and hops no work explains.</summary>
        private void MeasureRoads(Island v)
        {
            IslandData d = v.D;
            if (d.Rough) RoughIslands++;
            int exitCount = 0;
            foreach (Gate g in d.Gates) if (g.Role == GateRole.Exit) exitCount++;
            if (d.Passages.Count < exitCount) ExitsWithoutRoad += exitCount - d.Passages.Count;
            foreach (Passage road in d.Passages)
            {
                RoadCosts.Add(road.Cost);
                RoadLengths.Add(road.Path.Count);
                Flights += road.Flights;
                if (road.Cost == 0) RoadsFree++;
                foreach (Works w in road.Built)
                {
                    if (w.Kind == WorksKind.Stair) RoadStairs++;
                    else if (w.Kind == WorksKind.Bridge) RoadBridges++;
                    else RoadFerries++;
                }
                // A ferry is the one hop that may cover any distance, so hops are checked
                // against the Works the passage recorded, not guessed from the geometry.
                var sailed = new HashSet<(Vector2I, Vector2I)>();
                foreach (Works w in road.Built)
                    if (w.Kind == WorksKind.Ferry) sailed.Add((w.From, w.To));

                // A road walks by king's moves, so a one-cell diagonal is a step; works
                // stay cardinal, so anything longer must be straight and within a bridge.
                for (int hop = 1; hop < road.Path.Count; hop++)
                {
                    Vector2I a = road.Path[hop - 1], b = road.Path[hop];
                    if (sailed.Contains((a, b))) continue;
                    int dx = Math.Abs(a.X - b.X), dz = Math.Abs(a.Y - b.Y);
                    int reach = Math.Max(dx, dz);
                    bool diagonal = dx != 0 && dz != 0;
                    if (diagonal ? reach > 1 : reach > d.BridgeSpan + 1) RoadJumps++;
                }
            }
        }

        /// <summary>The cliffs inside a sculpted patch: how tall its gully walls, tower sides, risers and pit walls are.</summary>
        private void MeasureSculpts(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!v.Ground(x, z)) continue;
                LandformType t = v.Form(x, z);
                if (t is not (LandformType.Badlands or LandformType.Karst
                              or LandformType.Massif or LandformType.Sinkholes)) continue;

                for (int k = 0; k < 2; k++)
                {
                    int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                    if (!v.Ground(nx, nz) || v.Form(nx, nz) != t) continue;
                    if (d.Region[x, z] != d.Region[nx, nz]) continue;

                    int step = Math.Abs(v.Cross(x, z) - v.Cross(nx, nz));
                    if (step < 2) continue;
                    if (t == LandformType.Badlands) GullyDepths.Add(step);
                    else if (t == LandformType.Karst) TowerRises.Add(step);
                    else if (t == LandformType.Massif) TerraceSteps.Add(step);
                    else SinkDepths.Add(step);
                }
            }
        }

        /// <summary>Pass cells, the steepest step inside a pass, and whether a pass joined its two patches into one walk area.</summary>
        private void MeasurePasses(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            Passes += d.Passes.Count;
            if (d.Passes.Count > 0) PassIslands++;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.Pass[x, z]) continue;
                PassCells++;

                // Lake beds and canyon floors are skipped: a pass disc over one measures that feature's drop.
                if (d.WaterLevel[x, z] != IslandData.NoLand || d.Canyon[x, z]) continue;
                int worst = 0;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!v.Land(nx, nz) || !d.Pass[nx, nz]) continue;
                    if (d.WaterLevel[nx, nz] != IslandData.NoLand || d.Canyon[nx, nz]) continue;
                    worst = Math.Max(worst, Math.Abs(v.Top(x, z) - v.Top(nx, nz)));
                }
                PassGrade.Add(worst);
            }
            foreach (Vector2I site in d.Passes)
            {
                var across = new HashSet<int>();
                var walks = new HashSet<int>();
                for (int dx = -2; dx <= 2; dx++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    int x = site.X + dx, z = site.Y + dz;
                    if (!v.Land(x, z) || d.Walk[x, z] < 0) continue;
                    across.Add(d.Region[x, z]);
                    walks.Add(d.Walk[x, z]);
                }
                // Two patches meeting at the site, one walk area covering both.
                if (across.Count >= 2 && walks.Count == 1) PassesJoined++;
            }
        }

        private void MeasureShelves(Island v)
        {
            IslandData d = v.D;
            int islandShelves = 0, widest = 0, offMain = 0;
            foreach (Shelf shelf in d.Shelves)
            {
                widest = Math.Max(widest, shelf.Width);
                if (!shelf.Buildable) continue;
                islandShelves++;
                ShelfDrops.Add(shelf.Drop);
                if (d.Walk[shelf.Center.X, shelf.Center.Y] != d.Mainland) offMain++;
            }
            BuildableShelves += islandShelves;
            if (islandShelves > 0) IslandsWithShelf++;
            WidestShelf.Add(widest);
            ShelfOffMainland.Add(offMain);
        }

        /// <summary>Every Gate against its own rules: kind, box, apron, strip, edge, spacing; then the roles per island.</summary>
        private void MeasureGates(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            int entries = 0, exits = 0;
            var edges = new HashSet<Cardinal>();
            long dry = DryCells(d);

            foreach (Gate g in d.Gates)
            {
                if (g.Role == GateRole.Entry) entries++; else exits++;
                if (!edges.Add(g.Facing)) SharedEdge++;
                if (OutOfBox(g, n)) GateOutOfBox++;

                ApronSizes.Add(g.ApronArea);
                if (g.ApronArea < GatePlacement.ApronArea) GateApronShort++;

                if (g.Kind == GateKind.Land)
                {
                    LandGates++;
                    if (!v.Land(g.Center.X, g.Center.Z)) GateOffHeartland++;
                    else if (d.WaterLevel[g.Center.X, g.Center.Z] != IslandData.NoLand) GateInWater++;
                    else if (d.Reach[g.Center.X, g.Center.Z] != d.Heartland) GateOffHeartland++;
                }
                else
                {
                    HangingGates++;
                    if (v.Land(g.Center.X, g.Center.Z)) HangingOnLand++;
                }

                // The strip is built, so full length and dead level are the rule, not a tolerance.
                if (!StripIntact(v, g)) StripMissing++; else StripLengths.Add(GatePlacement.StripLength);

                MeasureGateAxes(v, g, dry);

                if (g.Role == GateRole.Entry && _p.EntryGate != GateKind.Auto
                    && g.Kind != _p.EntryGate) WrongEntryKind++;
            }

            if (entries != 1) NoEntry++;
            if (exits < 1 || exits > 3) BadExitCount++;
            ExitCounts.Add(exits);
        }

        /// <summary>The Gate's 1 x 3 landing strip: full length, dead level, dry, on the heartland.</summary>
        private static bool StripIntact(Island v, Gate g)
        {
            IslandData d = v.D;
            Vector2I outward = g.Outward;
            Vector2I head = g.Kind == GateKind.Hanging
                ? new Vector2I(g.Center.X, g.Center.Z)
                  - outward * GatePlacement.HangingOffset
                : new Vector2I(g.Center.X, g.Center.Z);

            bool strip = v.Land(head.X, head.Y);
            if (strip)
            {
                short level = v.Top(head.X, head.Y);
                for (int along = 0; along < GatePlacement.StripLength && strip; along++)
                {
                    int sx = head.X - outward.X * along;
                    int sz = head.Y - outward.Y * along;
                    strip = v.Land(sx, sz) && v.Top(sx, sz) == level
                            && d.WaterLevel[sx, sz] == IslandData.NoLand
                            && d.Reach[sx, sz] == d.Heartland;
                }
            }
            return strip;
        }

        /// <summary>
        /// Is the Gate on the side of the map it claims? Three separate questions: dry land
        /// left behind it, whether it slid into a corner, and whether it is the outermost on
        /// its axis; plus apron-to-apron spacing against GatePlacement.MinSeparation.
        /// </summary>
        private void MeasureGateAxes(Island v, Gate g, long dry)
        {
            IslandData d = v.D;
            int n = v.N;
            Vector2I outAxis = g.Outward, sideAxis = g.Across;
            int gateAlong = g.Center.X * outAxis.X + g.Center.Z * outAxis.Y;
            long beyond = 0;
            int sideMin = int.MaxValue, sideMax = int.MinValue;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!v.Land(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                if (x * outAxis.X + z * outAxis.Y > gateAlong) beyond++;
                int s = x * sideAxis.X + z * sideAxis.Y;
                if (s < sideMin) sideMin = s;
                if (s > sideMax) sideMax = s;
            }
            if (dry > 0) GateBehind.Add((int)(100 * beyond / dry));

            int gateSide = g.Center.X * sideAxis.X + g.Center.Z * sideAxis.Y;
            int width = sideMax - sideMin;
            if (width > 0 && (gateSide > sideMax - width * 0.12f
                              || gateSide < sideMin + width * 0.12f)) GateInCorner++;

            foreach (Gate o in d.Gates)
            {
                if (o.Facing == g.Facing) continue;
                if (o.Center.X * outAxis.X + o.Center.Z * outAxis.Y >= gateAlong)
                    GateNotOutermost++;

                // Apron to apron, like the rule: a hanging Gate's Center is out in the aether.
                int apart = Math.Abs(o.Apron.X - g.Apron.X)
                          + Math.Abs(o.Apron.Y - g.Apron.Y);
                GateSpacing.Add(apart);
                if (apart < GatePlacement.MinSeparation * n) GatesCrowded++;
            }
        }

        /// <summary>A bridge is a level deck: what matters is that you can walk onto it, one slab at each end.</summary>
        private void MeasureCrossings(Island v)
        {
            IslandData d = v.D;
            foreach (Crossing c in d.Bridges)
            {
                Crossings++;
                CrossingSpans.Add(c.Span);
                int a = Traversal.CrossLevel(d, c.A.X, c.A.Y);
                int b = Traversal.CrossLevel(d, c.B.X, c.B.Y);
                bool steep = Math.Abs(a - b) > Traversal.MaxBridgeRise;
                bool offBank = Math.Abs(a - c.Deck) > 1 || Math.Abs(b - c.Deck) > 1;
                if (steep) DeckSteep++;
                if (offBank) DeckOffBank++;
                // Named as it happens: a "want 0" with no seed behind it cannot be looked at.
                if (steep || offBank)
                    GD.Print($"  seed {v.Seed}: crossing {c.A}-{c.B} banks {a}/{b}, deck {c.Deck}"
                        + $"{(steep ? " (steep)" : "")}{(offBank ? " (deck off bank)" : "")}"
                        + $"; landing strip under {(d.Landings[c.A.X, c.A.Y] ? "A" : "")}"
                        + $"{(d.Landings[c.B.X, c.B.Y] ? "B" : "")}, water under "
                        + $"{(d.WaterLevel[c.A.X, c.A.Y] != IslandData.NoLand ? "A" : "")}"
                        + $"{(d.WaterLevel[c.B.X, c.B.Y] != IslandData.NoLand ? "B" : "")}, beach under "
                        + $"{(d.Beach[c.A.X, c.A.Y] ? "A" : "")}{(d.Beach[c.B.X, c.B.Y] ? "B" : "")}"
                        + $"; landform {v.Form(c.A.X, c.A.Y)}/{v.Form(c.B.X, c.B.Y)}"
                        + $", water within a cell of {(WaterNear(d, c.A) ? "A" : "")}{(WaterNear(d, c.B) ? "B" : "")}"
                        + $", canyon/pass {d.Canyon[c.A.X, c.A.Y]}{d.Pass[c.A.X, c.A.Y]}/{d.Canyon[c.B.X, c.B.Y]}{d.Pass[c.B.X, c.B.Y]}");
            }
        }

        /// <summary>Whether standing water lies under a cell or any of its eight neighbours.</summary>
        private static bool WaterNear(IslandData d, Vector2I c)
        {
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int x = c.X + dx, z = c.Y + dz;
                if (InBounds(d.Size, x, z) && d.HasLand(x, z) && d.WaterLevel[x, z] != IslandData.NoLand) return true;
            }
            return false;
        }

        /// <summary>The re-roll verdict; a seed that gave up is printed as it happens, before the summary.</summary>
        private void MeasureGuarantees(Island v)
        {
            IslandData d = v.D;
            Attempts.Add(d.Attempts);
            if (d.Unmet.Length > 0)
            {
                Unplayable++;
                GD.Print($"  seed {v.Seed} gave up after {d.Attempts}: {d.Unmet}");
            }
        }

        private void MeasureAirstrips(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            int strips = 0;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++) if (d.Landings[x, z]) strips++;
            if (strips > 0) AirstripIslands++;
            AirstripCells.Add(strips);
        }

        /// <summary>
        /// Landmasses, whether every one of them has somewhere the heartland reaches, and
        /// corner-only joins within a landmass (two islands a corner apart are two islands).
        /// </summary>
        private void MeasureContinuity(Island v)
        {
            IslandData d = v.D;
            int n = v.N;
            var massOf = new int[n, n];
            int masses = LabelLandmasses(d, n, massOf);
            Landmasses += masses;

            var reached = new bool[masses];
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (massOf[x, z] >= 0 && d.Reach[x, z] == d.Heartland) reached[massOf[x, z]] = true;

            bool allLinked = true;
            foreach (bool r in reached) allLinked &= r;

            ByArrangement.TryGetValue(d.Arrangement, out var acc);
            ByArrangement[d.Arrangement] =
                (acc.Islands + 1, acc.Masses + masses, acc.Linked + (allLinked ? 1 : 0));

            DiagonalLand += DiagonalOnly(n, (x, z) =>
                InBounds(n, x, z) && massOf[x, z] >= 0, massOf);
            DiagonalWater += DiagonalOnly(n, (x, z) =>
                InBounds(n, x, z) && d.WaterLevel[x, z] != IslandData.NoLand);
        }
    }
}
