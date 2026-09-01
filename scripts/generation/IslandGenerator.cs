using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// The island generator: <see cref="Generate"/> is a pure function of
/// <c>(seed, params)</c>, and every Y it works in is a slab index. The island is
/// a blanket of regions, each with a <see cref="LandformType"/> and a rung on a
/// plateau ladder, each built under its own slope limit — not a smooth field
/// that gets quantised. The stages are the static classes this file calls, in
/// the order it calls them; docs/island-generation.md describes each.
/// </summary>
public static class IslandGenerator
{
    /// <summary>Islands built for one seed before the best failure ships.</summary>
    private const int Attempts = 4;

    /// <summary>Share of the dry land that must be reachable, once built, from the heartland.</summary>
    private const float MinHeartlandShare = 0.75f;

    /// <summary>
    /// Generates the Domain, re-rolling one that comes out unplayable. Still a
    /// pure function of (seed, params): a rejected island is rebuilt from a seed
    /// derived from the one asked for.
    /// </summary>
    public static IslandData Generate(int seed, IslandParams p)
    {
        p = BoundAltitude(p);
        IslandData? best = null;

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            int use = attempt == 0 ? seed : unchecked((int)TerrainHash(seed, 0x5E1Fu + (uint)attempt));
            IslandData d = Build(use, p);
            d.Attempts = attempt + 1;
            d.Unmet = Unmet(d, p);

            if (d.Unmet.Length == 0) return d;
            // The best failure, not the last: short of one guarantee beats short of three.
            if (best == null || d.Unmet.Length < best.Unmet.Length) best = d;
        }
        return best!;
    }

    /// <summary>
    /// Which guarantees this island misses; empty means playable. The message
    /// texts are data: <see cref="Generate"/> ranks failures by the joined length.
    /// </summary>
    private static string Unmet(IslandData d, IslandParams p)
    {
        var missing = new List<string>();

        // The Entry is checked against what was asked for: its kind and edge are
        // the sending Domain's decision, so a wrong one is a Domain built to the
        // wrong specification.
        int entries = 0, exits = 0, wrongExitKind = 0;
        bool rightKind = true, rightEdge = true;
        foreach (Gate g in d.Gates)
        {
            if (g.Role == GateRole.Exit)
            {
                exits++;
                if (p.ExitGate != GateKind.Auto && g.Kind != p.ExitGate) wrongExitKind++;
                continue;
            }
            entries++;
            if (p.EntryGate != GateKind.Auto && g.Kind != p.EntryGate) rightKind = false;
            if (p.EntryEdge != GateEdge.Auto && (int)g.Facing != (int)p.EntryEdge - 1)
                rightEdge = false;
        }
        if (entries != 1 || !rightKind) missing.Add("entry gate");
        if (!rightEdge) missing.Add("entry on the edge asked for");

        if (exits < 1) missing.Add("way out");
        else if (d.Passages.Count < exits) missing.Add("a road to every exit");

        if (p.ExitGates > 0 && exits < Math.Clamp(p.ExitGates, 1, 3))
            missing.Add("the exits asked for");
        if (wrongExitKind > 0) missing.Add("exits of the kind asked for");

        bool buildable = false;
        foreach (Shelf shelf in d.Shelves)
        {
            if (!shelf.Buildable) continue;
            if (d.Heartland >= 0 && d.Reach[shelf.Center.X, shelf.Center.Y] != d.Heartland) continue;
            buildable = true;
            break;
        }
        if (!buildable) missing.Add("somewhere to build");

        int dry = 0;
        for (int x = 0; x < d.Size; x++)
        for (int z = 0; z < d.Size; z++)
            if (d.HasLand(x, z) && d.WaterLevel[x, z] == IslandData.NoLand) dry++;

        int heart = d.Heartland >= 0 ? d.Reaches[d.Heartland].Area : 0;
        if (dry > 0 && heart < dry * MinHeartlandShare) missing.Add("one island");

        return string.Join(", ", missing);
    }

    /// <summary>
    /// The working state one island is built through. Every stage reads and
    /// writes the same array instances, so nothing may be reordered: later stages
    /// read what earlier ones left.
    /// </summary>
    private sealed class Draft
    {
        public readonly int Seed;
        public readonly IslandParams P;
        public readonly int N;
        public readonly IslandData Data;
        public readonly IslandArrangement How;
        public readonly int Span;

        public bool[,] Land = null!;
        public List<(Vector2I A, Vector2I B)> Bridges = null!;
        public int[,] Region = null!;
        public int RegionCount;
        public float[,] ToCoast = null!;
        public float[,] Envelope = null!;
        public Dictionary<long, List<(int X, int Z)>> Borders = null!;
        public RegionPlan[] Plan = null!;
        public float[,] Inward = null!;
        public short[,] Surface = null!;
        public bool[,]? Canyon;
        public bool[,]? Pass;
        public bool[,] Exempt = null!;
        public short[,] Water = null!;
        public byte[,] Fluid = null!;

        public Draft(int seed, IslandParams p)
        {
            Seed = seed;
            P = p;
            N = p.Size;
            Data = new IslandData(N)
            {
                Style = Roster.ResolveStyle(seed, p),
                Character = Roster.ResolveCharacter(seed, p),
            };
            How = Roster.ResolveArrangement(seed, p);
            Data.Arrangement = How;
            Span = Math.Max(1, (int)p.Crossings);
            Data.BridgeSpan = Span;
        }
    }

    private static IslandData Build(int seed, IslandParams p)
    {
        var d = new Draft(seed, p);
        FitFootprint(d);
        PlanRegions(d);
        ShapeSurface(d);
        PlaceStandingWater(d);
        Settle(d);
        CarveRivers(d);
        Pack(d);
        ReadBack(seed, p, d.Data);
        return d.Data;
    }

    /// <summary>
    /// Stage 1. The landmass should cover 55–85% of the grid, measured after the
    /// bites, the islet filter and the linker (which shrinks every scattered
    /// layout), so the whole mask stage runs inside the fit loop.
    /// </summary>
    private static void FitFootprint(Draft d)
    {
        float scale = 1f;
        for (int fit = 0; fit < 3; fit++)
        {
            d.Land = Footprint.BuildMask(d.Seed, d.P, d.How, scale);

            // Bites are for a single landmass; on Twins a bite ate a twin.
            if (d.How == IslandArrangement.Single || d.How == IslandArrangement.Satellites)
            {
                int[,] draft = Regions.BuildRegions(d.Seed, d.P, d.Land, out int draftCount);
                Footprint.BiteRegions(d.Seed, d.P, d.Land, draft, draftCount);
            }
            Landmasses.CloseDiagonalJoins(d.Land);

            // Every arrangement but Single keeps its pieces, and then has to earn
            // them: the linker nudges them until each is within one bridge of the next.
            if (d.How == IslandArrangement.Single) Landmasses.KeepLargestComponent(d.Land);
            else
            {
                Landmasses.DropComponentsUnder(d.Land, Landmasses.MinIsletCells);
                Landmasses.LinkLandmasses(d.Land, d.Span);
            }
            Landmasses.CloseDiagonalJoins(d.Land);

            float share = Footprint.ExtentShare(d.Land);
            if (share <= 0f || (share >= Footprint.ExtentFloor && share <= Footprint.ExtentCeiling)) break;
            float target = share < Footprint.ExtentFloor ? 0.68f : 0.78f;
            float factor = Math.Clamp(MathF.Sqrt(target / share), 0.8f, 1.35f);
            if (MathF.Abs(factor - 1f) < 0.03f) break;
            scale *= factor;
        }
        d.Bridges = Landmasses.FindBridgeSites(d.Land, d.Span);
    }

    /// <summary>Stage 2. The patchwork, what each patch is, and the rung it stands on.</summary>
    private static void PlanRegions(Draft d)
    {
        int seed = d.Seed;
        IslandParams p = d.P;
        bool[,] land = d.Land;

        int[,] region = Regions.BuildRegions(seed, p, land, out int regionCount);
        d.ToCoast = Keel.DistanceToCoast(land);
        d.Envelope = Regions.ReliefEnvelope(seed, p, land, d.ToCoast);
        Regions.BuildBorders(land, region, regionCount, out HashSet<int>[] firstPass);
        LandformType[] types = Landforms.AssignTypes(seed, p, land, region, regionCount, d.Envelope, d.ToCoast);

        // Adjacent mountains become one massif: penned in one region a mountain
        // has no room for a foot and can only be a wall.
        region = Landforms.MergeAdjacentOfType(land, region, firstPass, ref regionCount, ref types);

        d.Borders = Regions.BuildBorders(land, region, regionCount, out HashSet<int>[] neighbours);
        Landforms.RepairAdjacency(region, regionCount, neighbours, types);

        // A bridgehead lands on a plain: a mesa, basin or mountain would ignore
        // the rung agreement between the two banks.
        var bridgeheads = new HashSet<int>();
        foreach (var (ca, cb) in d.Bridges)
        {
            foreach (Vector2I c in new[] { ca, cb })
            {
                if (!land[c.X, c.Y]) continue;
                int r = region[c.X, c.Y];
                bridgeheads.Add(r);
                if (Landforms.IsTable(types[r]) || types[r] == LandformType.Mountain
                    || Landforms.IsSculpted(types[r]))
                    types[r] = LandformType.Plain;
            }
        }
        Landforms.RepairAdjacency(region, regionCount, neighbours, types);

        // The quota is restored last, after everything that flattens a region has had its say.
        Landforms.RestoreMissingLandforms(p, seed, region, regionCount, neighbours, types,
                                          Regions.RegionCells(land, region, regionCount), bridgeheads);
        d.Plan = Landforms.AssignPlateaus(seed, p, land, region, regionCount, d.Envelope,
                                          neighbours, types, d.Bridges);
        d.Inward = Regions.InwardDistance(land, region, regionCount);
        d.Region = region;
        d.RegionCount = regionCount;
    }

    /// <summary>
    /// Stage 3. Relief under each landform's slope limit, settled once; then the
    /// sculpted landforms, a canyon and the passes are cut into the settled
    /// surface and exempted from the limiter, which is how they carry cliffs
    /// inside a patch. A pass is the opposite of exempt: the limiter reaches
    /// across its border so it can be walked.
    /// </summary>
    private static void ShapeSurface(Draft d)
    {
        int n = d.N;
        d.Surface = Relief.BuildSurface(d.Seed, d.P, d.Land, d.Region, d.Plan, d.Inward, out int duneGrain);
        d.Data.DuneGrain = duneGrain;
        StepGrammar.LimitSlope(d.Surface, d.Region, d.Land, d.Plan);

        bool[,] sculpted = Sculpting.Sculpt(d.Seed, d.P, d.Land, d.Region, d.Plan, d.Surface, d.Inward);

        d.Canyon = Sculpting.WantsCanyon(d.Seed, d.P)
            ? Sculpting.CarveCanyon(d.Seed, d.P, d.Land, d.Region, d.Plan, d.Surface, d.Borders)
            : null;
        d.Pass = Sculpting.CutPasses(d.Seed, d.P, d.Land, d.Region, d.Plan, d.Surface, d.Borders, d.Data.Passes);

        d.Exempt = new bool[n, n];
        Array.Copy(sculpted, d.Exempt, sculpted.Length);
        if (d.Canyon != null)
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++) d.Exempt[x, z] |= d.Canyon[x, z];
        StepGrammar.LimitSlope(d.Surface, d.Region, d.Land, d.Plan, d.Exempt, d.Pass);
        StepGrammar.ResolveAmbiguousSteps(d.Surface, d.Region, d.Land, d.Plan, null, d.Exempt);
    }

    /// <summary>
    /// Stage 4a. Lakes sink into the surface after every grammar pass (which they
    /// must not undo) and before the keel measures thickness. A patch a canyon or
    /// pass cuts through would fill to the bottom of the cut and pour out, so it
    /// holds no water. Goo comes after the lakes so it can keep its distance.
    /// </summary>
    private static void PlaceStandingWater(Draft d)
    {
        int n = d.N;
        bool[,]? drains = d.Canyon;
        if (d.Pass != null)
        {
            drains = new bool[n, n];
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                drains[x, z] = d.Pass[x, z] || (d.Canyon != null && d.Canyon[x, z]);
        }
        d.Water = Lakes.PlaceLakes(d.Seed, d.P, d.Land, d.Region, d.RegionCount, d.Plan, d.Surface, drains);
        d.Fluid = new byte[n, n];
        Lakes.PlaceGoo(d.Seed, d.P, d.Land, d.Region, d.RegionCount, d.Plan, d.Surface, d.Water, d.Fluid);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.Water[x, z] != IslandData.NoLand) d.Exempt[x, z] = true;
    }

    /// <summary>
    /// Beaches, then the three lowering passes cycled together until nothing
    /// moves: resolving a two-slab step can expose a three, closing that can
    /// expose a new two, and the bridgeheads have to be re-levelled after either.
    /// All three only ever lower, so the cycle terminates.
    /// </summary>
    private static void Settle(Draft d)
    {
        Beaches.MakeBeaches(d.Land, d.Surface, d.Water, d.Region, d.Plan, d.Data.Beach);

        for (int settle = 0; settle < 6; settle++)
        {
            bool moved = Bridgeheads.LevelBridgeheads(d.Land, d.Surface, d.Water, d.Region, d.Plan, d.Bridges);
            moved |= StepGrammar.LimitSlope(d.Surface, d.Region, d.Land, d.Plan, d.Exempt, d.Pass);
            moved |= StepGrammar.ResolveAmbiguousSteps(d.Surface, d.Region, d.Land, d.Plan, d.Water, d.Exempt);
            if (!moved) break;
        }
    }

    /// <summary>
    /// Stage 4b. Rivers are cut across the finished patchwork and carry their own
    /// step grammar. The bridgeheads are off limits to the water (a channel
    /// through one un-levels the crossing), and so is goo with its whole
    /// king's-move neighbourhood: fluids never touch, even diagonally.
    /// </summary>
    private static void CarveRivers(Draft d)
    {
        int n = d.N;
        var form = new byte[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            form[x, z] = d.Land[x, z] ? (byte)d.Plan[d.Region[x, z]].Type : (byte)0;

        var keep = new bool[n, n];
        foreach (var (ca, cb) in d.Bridges)
        foreach (Vector2I c in new[] { ca, cb })
            if (InBounds(n, c.X, c.Y)) keep[c.X, c.Y] = true;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (d.Fluid[x, z] != (byte)FluidKind.Goo) continue;
            for (int ox = -1; ox <= 1; ox++)
            for (int oz = -1; oz <= 1; oz++)
            {
                int nx = x + ox, nz = z + oz;
                if (InBounds(n, nx, nz)) keep[nx, nz] = true;
            }
        }

        Rivers.Carve(d.Seed, d.P, d.Land, d.Surface, d.Water, d.Data.River, d.Data.Navigable,
                     d.Data.Flow, d.Data.Falls, d.Span, form, keep, d.Fluid);

        // The valley and bank passes only lower; a cell can end up under the water beside it.
        Lakes.RaiseSunkenShores(d.Land, d.Surface, d.Water);
    }

    /// <summary>Stage 5. The keel, then the columns written into the IslandData.</summary>
    private static void Pack(Draft d)
    {
        int n = d.N;
        IslandData data = d.Data;
        short[,] keel = Keel.BuildKeel(d.Seed, d.P, d.Land, d.Surface, d.ToCoast);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            data.Land[x, z] = d.Land[x, z];
            data.Region[x, z] = d.Region[x, z];
            if (!d.Land[x, z]) continue;

            short top = d.Surface[x, z];
            short bottom = keel[x, z];
            if (bottom > top) bottom = top;
            data.Spans[x, z] = new[] { new Span(bottom, top) };
            data.Material[x, z] = 0;
            data.Landform[x, z] = (byte)d.Plan[d.Region[x, z]].Type;
            data.WaterLevel[x, z] = d.Water[x, z];
            data.Fluid[x, z] = d.Fluid[x, z];
            data.Canyon[x, z] = d.Canyon != null && d.Canyon[x, z];
            data.Pass[x, z] = d.Pass != null && d.Pass[x, z];
        }

        Bridgeheads.RecordCrossings(data, d.Bridges);
        Rivers.DropFallsPastTheKeel(data);      // a rim fall pours past the keel, only known now
        Rivers.MarkFords(data);                 // read by the traversal analysis
    }

    /// <summary>
    /// Stages 7–10: read the finished terrain back. Gate placement is the one
    /// pass that both reads the analysis and moves slabs (it levels its landing
    /// strips), so the analysis runs again when it did. Overhangs come last: the
    /// lip of an overhang is a roof, not ground.
    /// </summary>
    private static void ReadBack(int seed, IslandParams p, IslandData data)
    {
        Traversal.Analyse(data);
        if (GatePlacement.Place(seed, p, data)) Traversal.Analyse(data);

        // The mainland is the ground the Entry lands you on, not the largest piece.
        foreach (Gate g in data.Gates)
        {
            if (g.Role != GateRole.Entry) continue;
            Traversal.AnchorOn(data, g.Apron);
            break;
        }

        Passages.Find(data);
        Habitat.Measure(seed, data);
        Surfaces.Classify(data);
        Names.Give(seed, data);
        Overhangs.Carve(seed, p, data);
    }

    /// <summary>
    /// The bounding cube is Size cells across and Size slabs tall. The mountain's
    /// rise and the keel's depth are capped at the share of the cube they take on
    /// a 128 Domain (40 and 34 slabs), so a smaller island is proportionally
    /// lower rather than a scale model of a mountain in a shoebox.
    /// </summary>
    private static IslandParams BoundAltitude(IslandParams p)
    {
        int mountainCap = Mathf.RoundToInt(p.Size * (40f / 128f));
        int keelCap = Mathf.RoundToInt(p.Size * (34f / 128f));
        if (p.MountainHeight <= mountainCap && p.KeelDepth <= keelCap) return p;

        var bounded = (IslandParams)p.Duplicate();
        bounded.MountainHeight = Math.Min(p.MountainHeight, mountainCap);
        bounded.KeelDepth = Math.Min(p.KeelDepth, keelCap);
        return bounded;
    }
}
