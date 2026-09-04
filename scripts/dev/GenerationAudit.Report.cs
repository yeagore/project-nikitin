using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

public partial class GenerationAudit
{
    /// <summary>Step grammar shares, where the two-slab steps are, and cliffs by the landforms either side.</summary>
    private static void PrintSteps(Tally t)
    {
        long pairs = t.Pairs;
        GD.Print($"step grammar ({pairs} adjacent pairs)");
        GD.Print($"  free (0-1 slabs)          {100.0 * t.Free / pairs,6:0.0}%");
        GD.Print($"  two-slab                  {100.0 * t.Ambiguous / pairs,6:0.0}%");
        GD.Print($"  cliff (3+ slabs)          {100.0 * t.Cliff / pairs,6:0.0}%");
        GD.Print($"  two-slab off mountains    {t.AmbiguousOffMountain} of {t.PairsOffMountain}");
        foreach (var (k, v) in t.AmbiguousWhere.OrderByDescending(e => e.Value))
            GD.Print($"    {k,-20} {v,6}");
        GD.Print("");

        GD.Print("cliffs by the landforms either side (rule: plain-plain, mesa-mesa, basin-basin)");
        foreach (var kv in t.CliffByBorder.OrderByDescending(k => k.Value))
            GD.Print($"  {kv.Key,-20} {kv.Value,6}");
        GD.Print("");
    }

    private void PrintPatches(Tally t)
    {
        t.PatchSizes.Sort();
        GD.Print($"patches: {t.PatchSizes.Count}, min {t.PatchSizes[0]}, median "
            + $"{t.PatchSizes[t.PatchSizes.Count / 2]}, max {t.PatchSizes[^1]}"
            + $"  (target min {Params.MinRegionArea}); undersized {t.PatchesUndersized}");

        Report("mesa clearance above neighbours", t.MesaClear, "slabs");
        Report("basin drop below neighbours", t.BasinDrop, "slabs");
        GD.Print($"  mesa/basin touching a mountain (want 0): {t.MesaTouchesMountain}");
        GD.Print($"  mesa/basin touching another kind (want 0): {t.MesaTouchesOther}\n");
    }

    /// <summary>Hills, the sculpted landforms (their steps must never be two), mountains and the step profile.</summary>
    private void PrintLandforms(Tally t)
    {
        string hilliness = Params.Hilliness < 0f ? "rolled per seed" : $"Hilliness {Params.Hilliness:0.00}";
        Report($"hills relief per patch ({hilliness})", t.HillsRelief, "slabs");
        Report("  that patch's width", t.HillsSpan, "cells");

        Report("badlands: gully wall", t.GullyDepths, "slabs");
        Report("karst: tower side", t.TowerRises, "slabs");
        Report("massif: terrace riser", t.TerraceSteps, "slabs");
        Report("sinkholes: pit wall", t.SinkDepths, "slabs");

        Report($"mountain rise above foot (MountainHeight {Params.MountainHeight})", t.MountainRise, "slabs");
        GD.Print($"  border cells where a massif sits below the ground it meets: "
            + $"{t.FootDrops} of {t.FootPairs}\n");

        GD.Print("mountain step profile, by distance into the massif");
        foreach (int band in t.StepByBand.Keys.OrderBy(k => k))
        {
            var list = t.StepByBand[band];
            if (list.Count < 20) continue;
            GD.Print($"  {band / 10.0:0.0}-{(band + 1) / 10.0:0.0}   mean {list.Average(),5:0.00}   max {list.Max(),3}");
        }
        GD.Print("");
    }

    private void PrintRivers(Tally t)
    {
        GD.Print($"rivers: {t.RiverCells} cells on {t.IslandsWithRiver} of {Seeds} islands, "
            + $"{t.NavigableCells} of them navigable");
        Report("  river cells per island", t.RiverPerIsland, "cells");
        GD.Print($"  islands whose rivers reach the rim: {t.RiverIslandsReachingRim}"
            + $" of {t.IslandsWithRiver}   (there is no sea; they must)");
        GD.Print($"  falls: {t.FallCells}, of which {t.RimFalls} pour off the rim");
        GD.Print($"  channel not cut below its own water (want 0): {t.RiverDry}");
        GD.Print($"  water running uphill (want 0):                {t.RiverUphill}");

        int reachCells = t.ReachCells;
        if (reachCells > 0)
            GD.Print($"  how the courses run: {100.0 * t.RiverStraight / reachCells:0}% straight, "
                + $"{100.0 * t.RiverBends / reachCells:0}% turning  (n={reachCells})");
        Report("  longest run held in one direction", t.StraightRuns, "cells");
        GD.Print($"  eyots: {t.EyotCells} cells of island parted by a braided reach\n");
    }

    /// <summary>Berths against the sites the domino rule found: a low share is the pruning working unless the sites are low too.</summary>
    private void PrintFerries(Tally t)
    {
        GD.Print($"ferries: {t.Berths} berths on {t.WaterBodies} bodies of water, "
            + $"over {t.IslandsWithBerth} of {Seeds} islands");
        GD.Print($"  of {t.BerthSites} sites the domino rule found "
            + $"({(t.BerthSites > 0 ? 100 * t.Berths / t.BerthSites : 0)}% load-bearing)");
        GD.Print($"  islands with water a bridge cannot span: {t.IslandsWithBerth} of {Seeds}");
        Report("  quay above the water", t.QuayRise, "slabs");
        GD.Print($"  berth that is not a quay on sailable water (want 0): {t.BadQuay}\n");
    }

    private void PrintOverhangs(Tally t)
    {
        GD.Print($"overhangs and arches: {t.OverhangCells} columns carrying a second span, "
            + $"on {t.OverhangIslands} of {Seeds} islands");
        Report("  air under a lip", t.LipAir, "slabs");
        GD.Print("");
    }

    /// <summary>Material shares and anchor counts: the lists nothing else reads until the biome layer does.</summary>
    private void PrintSurfaces(Tally t)
    {
        GD.Print("surface: what the ground is made of, as a share of land");
        {
            var parts = new List<string>();
            long land = 0;
            foreach (long v in t.MaterialCells) land += v;
            foreach (SurfaceMaterial m in Enum.GetValues<SurfaceMaterial>())
            {
                long cells = t.MaterialCells[(int)m];
                parts.Add($"{m.ToString().ToLowerInvariant()} "
                    + (land > 0 ? $"{100.0 * cells / land:0.0}%" : "-")
                    + (cells == 0 ? " NEVER" : ""));
            }
            GD.Print("  " + string.Join(", ", parts));
        }
        GD.Print($"anchors: {t.CoastAnchors} coast, {t.CliffAnchors} cliff brink, "
            + $"{t.CliffFootAnchors} cliff foot, {t.BankAnchors} bank, {t.SummitAnchors} summit, "
            + $"{t.OverhangCells} overhang, {t.BeachCells} beach, {t.FordCells} ford, "
            + $"{t.LandingCells} gate landing, {t.Berths} quay, "
            + $"{t.RiverBedAnchors} river bed, {t.LakeBedAnchors} lake bed");
        GD.Print($"  brinks that are gorge rims (3+ slabs over the water itself): {t.BrinksBesideWater}");
        GD.Print($"  islands with no beach at all: {t.IslandsWithoutBeach} of {Seeds}");
        // Against the coast ring, not the beach's own cells: a beach is two deep, so that ratio reads 151%.
        GD.Print($"  coast that steps down onto a beach: "
            + (t.CoastAnchors > 0 ? $"{100.0 * t.BeachedCoast / t.CoastAnchors:0}%" : "-")
            + "   (the rest breaks off to the keel)\n");
    }

    /// <summary>Per-island means of the habitat axes: a mean pinned at 0 or 255 is an axis that stopped measuring.</summary>
    private static void PrintHabitat(Tally t)
    {
        GD.Print("habitat: per-island mean of each axis, 0-255");
        Report("  moisture (0 parched - 255 waterside)", t.MoistureMeans, "");
        Report("  warmth   (0 frozen - 255 warm)", t.WarmthMeans, "");
        Report("  rugged   (0 flat - 255 broken)", t.RuggedMeans, "");
        Report("  exposure (0 lee - 255 windswept)", t.ExposureMeans, "");
        Report("  rim distance", t.RimMeans, "cells");
        {
            var bins = new List<string>();
            for (int i = 0; i < Tally.RuggedBins; i++)
            {
                string label = i == Tally.RuggedBins - 1 ? $"{i + 1}+" : $"{i + 1}";
                bins.Add($"{label}: " + (t.LandByWater[i] > 0
                    ? $"{t.RuggedByWater[i] / (double)t.LandByWater[i]:0}" : "-"));
            }
            GD.Print("  rugged by cells from fresh water (dry land; 1 is the bank)   "
                + string.Join("   ", bins));
        }
        GD.Print("");
    }

    private void PrintWater(Tally t)
    {
        GD.Print($"lakes: {t.Lakes} over {t.LakeCells} cells, on {t.IslandsWithLake} of {Seeds} islands");
        Report("  shore step above water", t.ShoreSteps, "slabs");
        GD.Print($"  dry land BELOW a water surface (want 0): {t.Leaks}");
        GD.Print($"  water touching the void (want 0):        {t.WaterAtVoid}");
        Report("  lake bodies", t.LakeBodySizes, "cells");

        GD.Print($"goo: {t.GooCells} cells of puddle on {t.GooIslands} of {Seeds} islands");
        GD.Print($"  goo within a king's move of water (want 0): {t.GooTouchesWater}\n");

        Report("altitude, keel to peak", t.AltSpans, "slabs");
        GD.Print($"  islands taller than their own size in slabs (want 0): {t.AltOverCap}\n");
    }

    private void PrintGorges(Tally t)
    {
        GD.Print($"gorges (a course walled 3+ slabs on both sides): {t.GorgeCells} cells, "
            + $"{t.GorgeReaches} reaches of 3+ cells, on {t.GorgeIslands} of {Seeds} islands");
        Report("  reach length", t.GorgeLengths, "cells");
        GD.Print($"  reaches a bridge could cross somewhere along them: "
            + $"{t.GorgeCrossable} of {t.GorgeReaches}");
        GD.Print($"  sealed reaches — no legal deck anywhere on their length: {t.GorgeSealed}"
            + $", of which {t.GorgeMisaligned} misaligned rims (a deck fits, banks disagree 3+)");
        Report("  sealed reach length", t.GorgeSealedLengths, "cells");
        Report("  walk to the nearest deck, worst cell per reach", t.GorgeDetours, "cells");
    }

    private static void PrintCharacters(Tally t)
    {
        GD.Print("landforms delivered, by character (share of that character's islands)");
        foreach (var (c, islands) in t.CharIslands.OrderBy(k => k.Key.ToString()))
        {
            int[] has = t.CharHas[c];
            var parts = new List<string>();
            for (int f = 0; f < Forms; f++)
                if (has[f] > 0) parts.Add($"{TypeName[f]} {100 * has[f] / islands}%");
            GD.Print($"  {c,-10} {islands,3} islands   {string.Join(", ", parts)}");
        }
        GD.Print("");
    }

    private void PrintWalkability(Tally t)
    {
        GD.Print("walkability (one-slab step free, 2+ a wall; water is not ground)");
        GD.Print($"  land on the mainland        {100.0 * t.WalkMainland / t.WalkLand,6:0.0}%");
        Report("  mainland share per island", t.MainlandShare, "%");
        Report("  stranded off the mainland", t.StrandedShare, "%");
        GD.Print($"  broken ground               {100.0 * t.WalkBroken / t.WalkLand,6:0.0}%"
            + $"  in {t.Scraps} scraps, against {t.Districts} districts");
        GD.Print($"\n  with stairs, hoists and bridges ("
            + $"face <= {Traversal.InfrastructureStep} slabs, span <= {(int)Params.Crossings} cells)");
        GD.Print($"  land on the heartland       {100.0 * t.ReachHeartland / t.WalkLand,6:0.0}%");
        Report("  heartland share per island", t.ReachShare, "%");
        GD.Print($"  islands whose dry land is ONE reachable whole: {t.IslandsFullyReachable} of {Seeds}");
        long stranded = 0;
        foreach (long v in t.StrandedByForm) stranded += v;
        if (stranded > 0)
        {
            var bits = new List<string>();
            for (int f = 0; f < Forms; f++)
                if (t.StrandedByForm[f] > 0) bits.Add($"{TypeName[f]} {100 * t.StrandedByForm[f] / stranded}%");
            GD.Print($"  what stays out of reach: {string.Join(", ", bits)}");
        }
        GD.Print($"  mesa top reachable at all   "
            + (t.MesaCells > 0 ? $"{100.0 * t.MesaReachable / t.MesaCells,6:0.0}% of mesa cells"
                               : "no mesas"));

        GD.Print($"  mesa top reachable on foot  "
            + (t.MesaCells > 0 ? $"{100.0 * t.MesaOnMainland / t.MesaCells,6:0.0}% of mesa cells"
                               : "no mesas")
            + "\n");
    }

    private void PrintPasses(Tally t)
    {
        GD.Print($"passes: {t.Passes} cut on {t.PassIslands} of {Seeds} islands, "
            + $"{t.PassesJoined} joining their two patches into one walk area, "
            + $"{(t.Passes > 0 ? t.PassCells / t.Passes : 0)} cells each");
        Report("  steepest step inside a pass", t.PassGrade, "slabs");
        GD.Print("");
    }

    private void PrintShelves(Tally t)
    {
        GD.Print($"shelves (flat, >= {Traversal.MinShelfArea} cells and "
            + $">= {Traversal.MinShelfWidth} wide): {t.BuildableShelves} buildable, "
            + $"on {t.IslandsWithShelf} of {Seeds} islands");
        Report("  widest square of flat ground", t.WidestShelf, "cells");
        Report("  buildable shelves off the mainland", t.ShelfOffMainland, "per island");
        Report("  descent across one shelf", t.ShelfDrops, "slabs");
        GD.Print("");
    }

    private void PrintCrossings(Tally t)
    {
        GD.Print($"crossings: {t.Crossings} bridge sites over {Seeds} islands"
            + $"  (span <= {(int)Params.Crossings} cells)");
        Report("  span", t.CrossingSpans, "cells");
        GD.Print($"  banks disagreeing by more than {Traversal.MaxBridgeRise} slabs (want 0): {t.DeckSteep}");
        GD.Print($"  deck more than a slab off a bank (want 0):   {t.DeckOffBank}\n");
    }

    private void PrintGates(Tally t)
    {
        GD.Print($"gates: {t.LandGates} standing on land, {t.HangingGates} hanging in the aether");
        GD.Print($"  islands without exactly one entry (want 0): {t.NoEntry}");
        GD.Print($"  islands whose exits are not 1-3 (want 0):   {t.BadExitCount}");
        Report("  exits per island", t.ExitCounts, "");
        GD.Print($"  two gates on one edge (want 0):             {t.SharedEdge}");
        GD.Print($"  entry gate not the kind asked for (want 0): {t.WrongEntryKind}");
        Report("  buildable ground within 4 cells of the landing", t.ApronSizes, "cells");
        GD.Print($"  gate off the heartland or in water (want 0): "
            + $"{t.GateOffHeartland + t.GateInWater}");
        GD.Print($"  gate outside the bounding box (want 0):     {t.GateOutOfBox}");
        GD.Print($"  hanging gate standing on land (want 0):     {t.HangingOnLand}");
        Report("  landing strip", t.StripLengths, "cells");
        GD.Print($"  gate with a short or sloped landing (want 0):  {t.StripMissing}");
        GD.Print($"  gate that is not the outermost on its own axis (want 0): {t.GateNotOutermost}");
        GD.Print($"  gate in a corner of its own edge (want 0):   {t.GateInCorner}");
        Report("  how far apart two gates are", t.GateSpacing, "cells");
        GD.Print($"  two gates closer than the {GatePlacement.MinSeparation:P0} floor"
            + $" (want 0): {t.GatesCrowded / 2}");
        Report("  dry land left behind a gate", t.GateBehind, "%");
        GD.Print($"  islands with a landing strip: {t.AirstripIslands} of {Seeds}"
            + "   (only the strips the hanging gates took are marked)");
        Report("  ground marked as strip", t.AirstripCells, "cells");
        GD.Print("");
    }

    /// <summary>The road from the Entry to each Exit, priced in works; an Exit without one is a Domain with one Link wearing several.</summary>
    private void PrintRoads(Tally t)
    {
        GD.Print($"roads from the entry gate to the exits: {t.RoadCosts.Count} over {Seeds} islands");
        GD.Print($"  exits with no road at all (want 0): {t.ExitsWithoutRoad}");
        Report("  works to build on one road", t.RoadCosts, "crossings");
        Report("  length of one road", t.RoadLengths, "cells");
        GD.Print($"  roads you can simply walk: {t.RoadsFree} of {t.RoadCosts.Count}");
        GD.Print($"  what they need built: {t.RoadStairs} stairs, {t.RoadBridges} bridges, "
            + $"{t.RoadFerries} ferries");
        GD.Print($"  a road hop no step, bridge or ferry explains (want 0): {t.RoadJumps}");
        GD.Print($"  flights of five-plus elevators: {t.Flights}, on {t.RoughIslands} of {Seeds} "
            + "islands (marked Rough — hard country, not a fault)\n");
    }

    private static void PrintArrangements(Tally t)
    {
        GD.Print("arrangements: landmasses per island, and whether all of it links up");
        foreach (var (a, v) in t.ByArrangement.OrderBy(k => k.Key.ToString()))
            GD.Print($"  {a,-12} {v.Islands,3} islands   {(float)v.Masses / v.Islands,4:0.0} masses each"
                + $"   fully linked {100 * v.Linked / v.Islands,3}%");
        GD.Print("");
    }

    private static void PrintRerolls(Tally t)
    {
        Report("re-rolls: islands built per seed", t.Attempts, "");
        GD.Print($"  seeds that never met the guarantees (want 0): {t.Unplayable}\n");
    }

    private void PrintContinuity(Tally t)
    {
        GD.Print($"continuity: {t.Landmasses} landmasses over {Seeds} islands "
            + $"(more than one is the arrangement's doing, not a fault); "
            + $"diagonal-only joins within a landmass: land {t.DiagonalLand}, water {t.DiagonalWater}");
    }

    /// <summary>
    /// The headline numbers docs/audit-baseline.json holds. Insertion order is the order
    /// moved lines print in; Math.Round (banker's) and the 100.0 * x / y forms are part of the file.
    /// </summary>
    private static Godot.Collections.Dictionary<string, Variant> BaselineNumbers(Tally t)
    {
        long pairs = t.Pairs;
        int reachCells = t.ReachCells;
        return new Godot.Collections.Dictionary<string, Variant>
        {
            ["free%"] = Math.Round(100.0 * t.Free / pairs, 1),
            ["twoSlab%"] = Math.Round(100.0 * t.Ambiguous / pairs, 1),
            ["cliff%"] = Math.Round(100.0 * t.Cliff / pairs, 1),
            ["twoSlabOffMountain"] = t.AmbiguousOffMountain,
            ["patchesUndersized"] = t.PatchesUndersized,
            ["riverCells"] = t.RiverCells,
            ["navigableCells"] = t.NavigableCells,
            ["riverStraight%"] = reachCells > 0 ? Math.Round(100.0 * t.RiverStraight / reachCells) : 0,
            ["falls"] = t.FallCells,
            ["lakes"] = t.Lakes,
            ["waterLeaks"] = t.Leaks,
            ["riverUphill"] = t.RiverUphill,
            ["gooCells"] = t.GooCells,
            ["gooTouchesWater"] = t.GooTouchesWater,
            ["gorgeReaches"] = t.GorgeReaches,
            ["gorgeSealed"] = t.GorgeSealed,
            ["gateOutOfBox"] = t.GateOutOfBox,
            ["altOverCap"] = t.AltOverCap,
            ["berths"] = t.Berths,
            ["overhangColumns"] = t.OverhangCells,
            ["mainland%"] = Math.Round(100.0 * t.WalkMainland / t.WalkLand, 1),
            ["heartland%"] = Math.Round(100.0 * t.ReachHeartland / t.WalkLand, 1),
            ["islandsOneWhole"] = t.IslandsFullyReachable,
            ["buildableShelves"] = t.BuildableShelves,
            ["crossings"] = t.Crossings,
            ["deckSteep"] = t.DeckSteep,
            ["noEntry"] = t.NoEntry,
            ["exitsWithoutRoad"] = t.ExitsWithoutRoad,
            ["roadsFree"] = t.RoadsFree,
            ["unplayable"] = t.Unplayable,
        };
    }
}
