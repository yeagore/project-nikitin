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

    /// <summary>
    /// Narrowest a strait between two lobes may pinch to, in cells. Just over one:
    /// the water may narrow to a single step across — which is what makes a crack
    /// read as a crack rather than as a channel — but it may never close, because
    /// a strait that heals is an arrangement quietly delivering fewer landmasses
    /// than it promised.
    /// </summary>
    private const float StraitNarrowest = 1.05f;

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

    /// <summary>
    /// How many islands may be built for one seed before the best of them is
    /// taken as it stands. Four: a re-roll is for the rare island that comes out
    /// unplayable, and if four in a row fail the guarantee then the parameters are
    /// asking for something the pipeline cannot deliver, which is not a thing more
    /// dice will fix.
    /// </summary>
    private const int Attempts = 4;

    /// <summary>
    /// Generates the Domain, and re-rolls a Domain that comes out unplayable.
    ///
    /// <b>Still a pure function of (seed, params).</b> A rejected island is
    /// rebuilt from a seed derived from the one asked for, so the same seed gives
    /// the same Domain every time — it simply may not be the *first* island that
    /// seed describes. What is checked is in <see cref="Unmet"/>: somewhere to
    /// arrive, somewhere to build, and enough of the island reachable from there
    /// to be worth arriving on.
    /// </summary>
    public IslandData Generate(int seed, IslandParams p)
    {
        IslandData? best = null;

        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            int use = attempt == 0 ? seed : unchecked((int)Hash(seed, 0x5E1Fu + (uint)attempt));
            IslandData d = Build(use, p);
            d.Attempts = attempt + 1;
            d.Unmet = Unmet(d, p);

            if (d.Unmet.Length == 0) return d;
            // Keep the best failure rather than the last: an island short of one
            // guarantee still beats one short of three.
            if (best == null || d.Unmet.Length < best.Unmet.Length)
            {
                d.Attempts = attempt + 1;
                best = d;
            }
        }
        return best!;
    }

    /// <summary>
    /// Which of the Stage 6 guarantees this island misses, as a short list. Empty
    /// means it is playable.
    ///
    /// The three are the minimum a run needs: a Gate of the kind the Link
    /// promised, ground the first company can be laid out on, and an island that
    /// is mostly one place once you have built stairs and bridges. Everything else
    /// — how many lakes, how the coast reads, whether the mountains came out where
    /// the style asked — is variety, and re-rolling for variety is how a generator
    /// ends up producing one island.
    /// </summary>
    private static string Unmet(IslandData d, IslandParams p)
    {
        var missing = new List<string>();

        // <b>The Entry is checked against what was asked for, not against itself.</b>
        // Its kind and its edge are both the sending Domain's decision — a Link
        // joins two Gates of one kind, and a Domain reached by travelling east
        // comes out on its west side — so an Entry that is neither is a Domain
        // built to the wrong specification, and the answer to that is another roll
        // of the dice rather than a shrug. The edge used to be missing from here
        // entirely, which is why asking for a southern Entry so often produced a
        // northern one: GatePlacement's fallbacks fired and nothing objected.
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

        // A Domain with no Link onward is a dead end in a tree that is meant to be
        // the whole map, and an Exit the player cannot walk or build their way to
        // from where they landed is the same thing wearing a portal.
        if (exits < 1) missing.Add("way out");
        else if (d.Passages.Count < exits) missing.Add("a road to every exit");

        // The Exits asked for, in number and in kind. Unlike the Entry these are
        // this Domain's own preference rather than another Domain's decision, so
        // they are worth a re-roll and not worth failing over — Generate keeps the
        // island with the fewest unmet guarantees, so a coast that genuinely
        // cannot take three Exits still ends up with the best two it has.
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
    /// How much of the dry land has to be reachable — with stairs, hoists and
    /// bridges — from the largest reachable piece. Three quarters: a Domain where
    /// a quarter of the ground cannot be built to is a Domain with a second island
    /// on it that nobody asked for.
    /// </summary>
    private const float MinHeartlandShare = 0.75f;

    private IslandData Build(int seed, IslandParams p)
    {
        int n = p.Size;
        var data = new IslandData(n)
        {
            Style = ResolveStyle(seed, p),
            Character = ResolveCharacter(seed, p),
        };

        IslandArrangement how = ResolveArrangement(seed, p);
        data.Arrangement = how;
        // Specks are not islets: BuildMask already drops anything under
        // MinIsletCells, so what comes back is landmasses only.
        bool[,] land = BuildMask(seed, p, how);

        // Bites are taken patch by patch, so the coast they leave runs along
        // region borders. Regions are rebuilt afterwards rather than re-indexed:
        // the partition is deterministic, so this simply re-derives it over the
        // land that remains.
        // Bites are for a single landmass. A bite takes a third of what is left,
        // measured over the whole footprint, and on a Twins or Triplets layout
        // that is most of one island — which is exactly how a third of Twins came
        // out with one twin. A layout with several pieces already has all the
        // silhouette interest a bite was there to provide.
        if (how == IslandArrangement.Single || how == IslandArrangement.Satellites)
        {
            int[,] draft = BuildRegions(seed, p, land, out int draftCount);
            BiteRegions(seed, p, land, draft, draftCount);
        }
        // Bites and the mask itself can leave two lobes meeting at a corner, which
        // is not a join you can walk. Filling the corner is done before the
        // component filter, so what it measures is what you can actually reach.
        CloseDiagonalJoins(land);

        // A Single Domain is one landmass by definition. Every other arrangement
        // keeps its pieces — and then has to earn them: LinkLandmasses nudges the
        // pieces together until each can be reached from the next by a bridge.
        // How far one bridge reaches decides how far apart the pieces may sit, so
        // the linker and the reach analysis have to be told the same number.
        int span = Math.Max(1, (int)p.Crossings);
        data.BridgeSpan = span;

        if (how == IslandArrangement.Single) KeepLargestComponent(land);
        else
        {
            DropComponentsUnder(land, MinIsletCells);
            LinkLandmasses(land, span);
        }
        CloseDiagonalJoins(land);
        // The crossings themselves are recorded once the terrain has a height:
        // a bridge is a level deck, so it is not a crossing until both banks
        // agree on one — see LevelBridgeheads.
        List<(Vector2I A, Vector2I B)> bridges = FindBridgeSites(land, span);
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

        // A mesa or basin takes its own level regardless of its rung group, and a
        // mountain takes no rung at all, so a bridgehead on either would ignore
        // the agreement AssignPlateaus makes between the two banks. Plains are
        // what a landing belongs on anyway.
        var bridgeheads = new HashSet<int>();
        foreach (var (ca, cb) in bridges)
        {
            foreach (Vector2I c in new[] { ca, cb })
            {
                if (!land[c.X, c.Y]) continue;
                int r = region[c.X, c.Y];
                bridgeheads.Add(r);
                if (IsTable(types[r]) || types[r] == LandformType.Mountain
                    || IsSculpted(types[r]))
                    types[r] = LandformType.Plain;
            }
        }
        RepairAdjacency(region, regionCount, neighbours, types);

        // The quota is restored *last*, after everything that flattens a region
        // has had its say. Restoring before the bridgeheads were cleared meant a
        // character's only mountain could be the one region a crossing landed on,
        // and it was quietly deleted afterwards: Highland delivered mountains on
        // 72% of its islands instead of all of them.
        RestoreMissingLandforms(p, seed, region, regionCount, neighbours, types,
                                RegionCells(land, region, regionCount), bridgeheads);
        RegionPlan[] plan = AssignPlateaus(seed, p, land, region, regionCount, envelope,
                                           neighbours, types, bridges);
        float[,] inward = InwardDistance(land, region, regionCount);

        short[,] surface = BuildSurface(seed, p, land, region, plan, inward, out int duneGrain);
        data.DuneGrain = duneGrain;
        LimitSlope(surface, region, land, plan);

        // The sculpted landforms are cut into the settled plain, like a canyon and
        // for the same reason: their cliffs are inside a patch, where relief under
        // a slope limit cannot put one.
        bool[,] sculpted = Sculpt(seed, p, land, region, plan, surface, inward);

        bool[,]? canyon = WantsCanyon(seed, p)
            ? CarveCanyon(seed, p, land, region, plan, surface, borders)
            : null;
        bool[,]? pass = CutPasses(seed, p, land, region, plan, surface, borders, data.Passes);

        // A canyon floor is exempt from the limiter — take it as a bound and the
        // whole rung group is dragged into it. A pass is the exact opposite: it
        // exists to be walked, so the limiter is told to reach across its border.
        // A gully, a tower and a terrace are exempt on the canyon's terms.
        var exempt = new bool[n, n];
        Array.Copy(sculpted, exempt, sculpted.Length);
        if (canyon != null)
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++) exempt[x, z] |= canyon[x, z];
        LimitSlope(surface, region, land, plan, exempt, pass);
        ResolveAmbiguousSteps(surface, region, land, plan, null, exempt);

        // Lakes sink into the surface, so they run before the keel measures column
        // thickness — and after every step-grammar pass, which they must not undo.
        // Both a canyon and a pass cut a patch's rim, and the rim is what sets a
        // lake's level — a patch with either through it would fill to the bottom
        // of the cut and pour out. Neither holds water.
        bool[,]? drains = canyon;
        if (pass != null)
        {
            drains = new bool[n, n];
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                drains[x, z] = pass[x, z] || (canyon != null && canyon[x, z]);
        }
        short[,] water = PlaceLakes(seed, p, land, region, regionCount, plan, surface, drains);
        // Lakes cut the surface after the grammar passes, so both run once more
        // over what they left. Levelling a shore leaves the bank behind it
        // standing a few slabs proud, and an islet edge can land on the
        // ambiguous two.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (water[x, z] != IslandData.NoLand) exempt[x, z] = true;

        // Settled together, not once each. Resolving a two-slab step lowers a
        // cell, which can leave a *three*-slab one behind it on a border the rules
        // forbid a cliff on — and the limiter closing that can in turn expose a
        // new two. All three passes only ever lower, so cycling them terminates.
        //
        // The bridgeheads are levelled inside the loop rather than before it: a
        // bridge is several slabs at one level, and the two passes that follow are
        // free to lower one bank and not the other, which is how a crossing ended
        // up with its two ends three slabs apart after they had been made to agree.
        // Beaches, before the settle loop rather than after it. Tapering the drop
        // keeps the *change* to a slab between neighbours, which is not the same as
        // keeping the *result* under one — two one-slab steps add. Cutting them
        // here means the limiter and the ambiguous-step pass clean up behind, which
        // is what those passes are for.
        MakeBeaches(land, surface, water, region, plan, data.Beach);

        for (int settle = 0; settle < 6; settle++)
        {
            bool moved = LevelBridgeheads(land, surface, water, region, plan, bridges);
            moved |= LimitSlope(surface, region, land, plan, exempt, pass);
            moved |= ResolveAmbiguousSteps(surface, region, land, plan, water, exempt);
            if (!moved) break;
        }
        // Rivers last: they are cut across the finished patchwork, and they carry
        // their own step grammar with them — the channel goes two slabs down, the
        // banks come down to meet it, and a ford is measured at the water.
        //
        // What each column is made of, which the river needs so that cutting its
        // banks does not quietly eat the rim of a mesa or the wall of a basin.
        var form = new byte[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            form[x, z] = land[x, z] ? (byte)plan[region[x, z]].Type : (byte)0;

        // The bridgeheads are off limits to the water. A channel cut through one
        // takes two slabs off the bank that was just levelled to meet the far
        // side, and the crossing the whole arrangement hangs on stops being a
        // crossing — one island in sixty came out with an islet nobody could
        // build to. A stream that would have run over the bank pours off a cell
        // earlier instead, which is a fall either way.
        var keep = new bool[n, n];
        foreach (var (ca, cb) in bridges)
        foreach (Vector2I c in new[] { ca, cb })
            if (c.X >= 0 && c.Y >= 0 && c.X < n && c.Y < n) keep[c.X, c.Y] = true;

        Rivers.Carve(seed, p, land, surface, water, data.River, data.Navigable,
                     data.Flow, data.Falls, span, form, keep);

        // The valley and bank passes only ever lower, and a cell lowered beside a
        // channel can end up under the water next to it. The same correction the
        // lakes use, run once more over what the rivers left.
        RaiseSunkenShores(land, surface, water);

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
            data.Pass[x, z] = pass != null && pass[x, z];
        }

        // What the crossings finally are, measured off the finished terrain: the
        // deck level each one runs at, and how long it is.
        RecordCrossings(data, bridges);
        // A fall at the rim has nothing under it, so it is drawn past the keel and
        // out of the world. The keel is only known now.
        Rivers.DropFallsPastTheKeel(data);

        // Where a stream can be crossed on foot — before the traversal analysis,
        // which is what reads it.
        Rivers.MarkFords(data);

        // Stage 5: read back what the terrain turned out to be. Pure analysis —
        // it changes nothing, so it stays outside the pipeline proper.
        Traversal.Analyse(data);

        // Gates last of all: every rule about where one may go is a rule about
        // ground the player can actually use, so it needs the traversal answer.
        GatePlacement.Place(seed, p, data);

        // And then the analysis is told where the run begins. The mainland is the
        // ground the Entry Gate lands you on, not the largest piece of the island
        // — see Traversal.AnchorOn.
        foreach (Gate g in data.Gates)
        {
            if (g.Role != GateRole.Entry) continue;
            Traversal.AnchorOn(data, g.Apron);
            break;
        }

        // The roads between the Gates, now that both the Gates and what it costs
        // to cross the ground between them are known.
        Passages.Find(data);

        // What the ground is made of, and the anchors the content layer attaches
        // to. Reads the finished terrain and changes nothing.
        Surfaces.Classify(data);
        Names.Give(seed, data);

        // Stage 4b, last of all: the only stage that gives a column more than one
        // span. It runs after the analysis on purpose — the lip of an overhang is
        // a roof, not ground, and what walks on it is a later question.
        Overhangs.Carve(seed, p, data);
        return data;
    }

    // ---- Lakes ---------------------------------------------------------------

    /// <summary>
    /// Fills basins with standing water. A basin is already a flat floor ringed by
    /// an inward-facing cliff — a bowl — so nothing needs carving: the lake is a
    /// level, and the terrain is untouched. That keeps the step grammar and the
    /// keel exactly as verified.
    ///
    /// A lake keeps at least this many cells of the patch's own rim dry, all
    /// the way round.
    private const int ShoreMargin = 2;

    /// <summary>
    /// And how many further cells the shore may wander in, per cell of coast.
    /// This is what keeps a lake from being a scale copy of the polygon it sits
    /// in — see the noise field in <see cref="PlaceLakes"/>.
    /// </summary>
    private const float ShoreWander = 3.4f;

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
    /// <b>One lake, not a chain.</b> A patch beside one that already holds water
    /// stays dry: each lake fills to its own patch's rim, so a row of neighbouring
    /// patches flooding at slightly different levels steps across the island and
    /// reads as flooding rather than as lakes. Joining such a pair into one body —
    /// one level, a channel notched between them — was the previous answer, and it
    /// spreads the same sheet of water over more of the island instead.
    /// </summary>
    private static short[,] PlaceLakes(int seed, IslandParams p, bool[,] land, int[,] region,
                                       int count, RegionPlan[] plan, short[,] surface,
                                       bool[,]? canyon)
    {
        int n = p.Size;

        // <b>How wet, once, because it drives three separate things.</b> Lakes used
        // to be a count and nothing else: the knob changed how many patches held
        // water and never how much water a patch held, and since a patch beside a
        // lake stays dry the count saturates — over the top quarter of the slider
        // the island gained 10% more water and looked identical. It now also sets
        // which patches are big enough to bother with and how far the shore stands
        // in, so the top of the range is a Domain of broad lakes rather than the
        // same tarns counted again.
        float wet = Math.Clamp(p.Lakes, 0f, 1f);

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

        // How much interior a patch needs before it is worth flooding. A dry Domain
        // only puts water in a patch with room for a lake; a wet one puts a pool in
        // anything that will hold one.
        int minInterior = Mathf.RoundToInt(Mathf.Lerp(40f, 12f, wet));

        var wants = new bool[count];
        for (int r = 0; r < count; r++)
        {
            if (drained[r]) continue;
            LandformType t = plan[r].Type;
            if (t != LandformType.Plain && t != LandformType.Mesa && t != LandformType.Basin) continue;
            if (interior[r] < minInterior || shore[r] == int.MaxValue) continue;

            // Rare on mesas, and a tarn rather than a lake when it happens.
            // Flooding a whole mesa interior turns the landform into a bowl: the
            // bed lands near the surrounding plain and the mesa reads as a wall
            // around a pit rather than as a tableland.
            // `Lakes` slides the whole thing: 0 leaves the Domain dry, 1 fills
            // every flat patch that could hold water. 0.5 is the old fixed rate.
            float chance = (t == LandformType.Mesa ? 0.10f : 0.22f) * wet * 2f;
            wants[r] = Hash01(seed, 0xB10Au ^ (uint)r * 2654435761u) < chance;
        }

        // <b>No chains of lakes.</b> Each patch fills to its own rim, so a row of
        // neighbouring patches that all hold water is a row of pools at slightly
        // different levels stepping across the island — which reads as flooding,
        // not as lakes. A patch beside one that already holds water therefore
        // stays dry.
        //
        // Linking such a pair instead — one level, a channel notched between
        // them — was the previous answer, and it makes the two pools one body:
        // the same sheet of water spread over more of the island, which is the
        // look this removes.
        DropNeighbouringLakes(land, region, wants, count);

        var level = new int[count];
        var bed = new int[count];
        for (int r = 0; r < count; r++)
        {
            if (!wants[r]) continue;
            level[r] = shore[r] - 1;
            // Two or three slabs of water; the bed therefore sits three or four
            // below the ring, never the ambiguous two.
            bed[r] = level[r] - (2 + (int)(Hash01(seed, 0x1A4Eu ^ (uint)r * 40503u) * 2f));
        }

        // <b>How far in the water starts, per cell, not per island.</b> A lake
        // used to be exactly the patch's interior at a fixed inset, which makes
        // its outline a scale copy of the patch border — and a patch border is a
        // Voronoi edge, so lakes came out as polygons with long straight sides.
        // Wandering the inset instead means the shore is the patch's shape read
        // through a noise field: bays where the margin runs wide, points where it
        // runs narrow. The minimum is still ShoreMargin, so the dry ring that
        // holds the water in is exactly as thick as it ever was.
        // A wet Domain's shore wanders less far in, so each lake fills more of the
        // patch that holds it: the same outline, drawn closer to the rim. The
        // minimum is still ShoreMargin whatever the setting, so the dry ring that
        // holds the water in is exactly as thick as it ever was.
        var ragged = new Noise(seed + 4242, frequency: 0.13f, octaves: 3);
        float wander = ShoreWander * Mathf.Lerp(1.35f, 0.45f, wet);
        var margin = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            margin[x, z] = ShoreMargin + (int)(ragged.At(x, z) * wander);

        // Which interior cells actually become water: the largest 4-connected
        // component of each patch's interior. A pinched patch can otherwise leave
        // two pools meeting only at a corner, which reads as a broken lake.
        bool[,] pool = LakeBody(land, region, inset, wants, count, margin);

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

        RemoveDiagonalWater(surface, water, region, level);
        RaiseSunkenShores(land, surface, water);
        LevelShores(land, surface, water);
        return water;
    }

    /// <summary>
    /// Lifts any dry cell beside a lake that sits at or below its surface.
    ///
    /// The shore ring is what holds a lake in, and it holds because the patch is
    /// flat give or take a slab — but "give or take a slab" is not "never below",
    /// and a wandering shoreline leaves more of the patch's own interior dry than
    /// a fixed inset did. A dry cell standing under the water beside it is a hole
    /// in the bank, so it is brought up to the free step above the surface, which
    /// is where <see cref="LevelShores"/> would have put it coming the other way.
    /// </summary>
    private static void RaiseSunkenShores(bool[,] land, short[,] surface, short[,] water)
    {
        int n = land.GetLength(0);
        for (int pass = 0; pass < 4; pass++)
        {
            bool changed = false;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!land[x, z] || water[x, z] != IslandData.NoLand) continue;

                int floor = int.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (water[nx, nz] != IslandData.NoLand)
                        floor = Math.Max(floor, water[nx, nz] + 1);
                }
                if (floor == int.MinValue || surface[x, z] >= floor) continue;
                surface[x, z] = SlabClamp(floor);
                changed = true;
            }
            if (!changed) break;
        }
    }

    /// <summary>
    /// Keeps lakes from forming a chain. Patches are visited in a fixed order and
    /// any patch that borders one already holding water is refused, so what
    /// survives is single bodies of water with dry country between them.
    /// </summary>
    private static void DropNeighbouringLakes(bool[,] land, int[,] region, bool[] wants, int count)
    {
        int n = land.GetLength(0);
        var neighbours = new HashSet<int>[count];
        for (int i = 0; i < count; i++) neighbours[i] = new HashSet<int>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int r = region[x, z];
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                int o = region[nx, nz];
                if (o != r) neighbours[r].Add(o);
            }
        }

        var kept = new bool[count];
        for (int r = 0; r < count; r++)
        {
            if (!wants[r]) continue;
            bool beside = false;
            foreach (int nb in neighbours[r]) if (kept[nb]) { beside = true; break; }
            if (beside) wants[r] = false;
            else kept[r] = true;
        }
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
    private static bool[,] LakeBody(bool[,] land, int[,] region, int[,] inset, bool[] wants,
                                    int count, int[,] margin)
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
            if (seen[x, z] || inset[x, z] < margin[x, z]) continue;
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
                    if (inset[nx, nz] < margin[nx, nz] || region[nx, nz] != r) continue;
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


    // ---- Stage 1: footprint mask ----------------------------------------------

    /// <summary>One blob of the footprint: an ellipse with a wandering radius.</summary>
    private readonly struct Lobe
    {
        public readonly float Cx, Cz, Radius, Aspect, Cos, Sin;
        public readonly float Rings;      // how many wobbles go round its coast

        /// <summary>
        /// How far this lobe's radius is allowed to wander, as a share of the
        /// island's Irregularity. A lone landmass can wobble freely; a lobe placed
        /// next to another cannot, because a coast that swings by a third of its
        /// radius decides for itself whether two islands are two islands.
        /// </summary>
        public readonly float Wander;

        public Lobe(float cx, float cz, float radius, float aspect, float rot, float rings,
                    float wander)
        {
            Cx = cx;
            Cz = cz;
            Radius = radius;
            Aspect = aspect;
            Cos = MathF.Cos(rot);
            Sin = MathF.Sin(rot);
            Rings = rings;
            Wander = wander;
        }

        /// <summary>Normalised distance from this lobe's own wandering edge; &lt; 1 is inside.</summary>
        public float Distance(float x, float z, Noise lobes, float irr)
            => Distance(x, z, lobes, irr, out _);

        /// <summary>
        /// As above, and reports the wandering radius it measured against, in
        /// cells. The strait carving needs it: the seam between two lobes is where
        /// their normalised distances agree, and turning that back into a width on
        /// the ground takes the radius it was normalised by.
        /// </summary>
        public float Distance(float x, float z, Noise lobes, float irr, out float rEff)
        {
            float dx = x - Cx, dz = z - Cz;
            float rx = (dx * Cos + dz * Sin) * Aspect;
            float rz = (-dx * Sin + dz * Cos) / Aspect;
            float dist = MathF.Sqrt(rx * rx + rz * rz);

            // Sampled on the unit circle so it is seamless in angle — sampling the
            // angle itself would seam at +-pi. The offset per lobe keeps two
            // islets from having the same coastline.
            float ang = MathF.Atan2(rz, rx);
            float lobe = lobes.At(MathF.Cos(ang) * Rings + Cx, MathF.Sin(ang) * Rings + Cz);
            rEff = MathF.Max(1e-3f, Radius * (1f + irr * Wander * (lobe * 2f - 1f)));
            return dist / rEff;
        }
    }

    /// <summary>
    /// A footprint's blobs and what to do where two of them meet.
    ///
    /// <b>Straits are a property of the arrangement, not of the geometry.</b> The
    /// same ring of blobs is a <see cref="IslandArrangement.Ring"/> if the seams
    /// are left alone and a <see cref="IslandArrangement.BrokenRing"/> if they are
    /// cut, and an <see cref="IslandArrangement.Atoll"/> if they are cut narrowly
    /// enough that the islets still all but touch. So the layout says.
    /// </summary>
    private readonly struct Layout
    {
        public readonly Lobe[] Lobes;

        /// <summary>Radius of water cleared in the middle, or 0 for none.</summary>
        public readonly float Lagoon;

        /// <summary>Whether the seam between two blobs is carved into a strait.</summary>
        public readonly bool Straits;

        /// <summary>
        /// Widest that strait may open, in cells; 0 takes the Domain's bridge
        /// span, which is the width that keeps every arrangement crossable.
        /// </summary>
        public readonly float StraitWide;

        /// <summary>
        /// A floor under <see cref="IslandParams.Coverage"/> for this layout, or 0
        /// to take it as authored.
        ///
        /// Coverage is applied <i>per blob</i> — each keeps that share of its own
        /// disc — which is what stops one lobe being deleted by a low patch of the
        /// shape noise. On a thick blob the leftovers are a ragged coast; on a
        /// thin arm they are holes, and the arm stops being one landmass: a
        /// <c>Fractal</c> two cells wide came out as twenty separate islets. A
        /// layout whose shape depends on being <b>continuous</b> says so here, and
        /// takes its coastline from its wandering radius instead.
        /// </summary>
        public readonly float Solid;

        public Layout(Lobe[] lobes, float lagoon, bool straits, float straitWide = 0f,
                      float solid = 0f)
        {
            Lobes = lobes;
            Lagoon = lagoon;
            Straits = straits;
            StraitWide = straitWide;
            Solid = solid;
        }
    }

    /// <summary>
    /// Where the footprint's blobs go, per <see cref="IslandArrangement"/>. Laid
    /// out deliberately rather than thresholded out of noise: "one big island with
    /// three satellites" is a thing a Domain wants to *be*, and no single
    /// fragmentation number reliably produces it.
    ///
    /// Neighbouring blobs are placed so their edges land within a couple of cells
    /// of each other, which is what gives the bridge repair something to work
    /// with; the coastline noise then decides whether they touch, nearly touch, or
    /// need nudging.
    /// </summary>
    private static Layout PlaceLobes(int seed, IslandParams p, IslandArrangement how,
                                     float radius, float cx, float cz, float spread)
    {
        float irr = Math.Clamp(p.Irregularity, 0f, 1f);
        bool alone = how == IslandArrangement.Single;
        float lagoon = 0f;

        // <b>Separation is cut, not hoped for.</b> Where two lobes meet, the seam
        // between them is carved into a strait (see BuildMaskOnce), so a lobe with
        // a neighbour may stretch and let its coast swing exactly as far as a lone
        // one. Damping those two numbers was the previous answer — it stopped
        // Twins fusing and it also made every multi-island layout a field of
        // discs, which is the wrong trade: the point of an arrangement is where
        // the land is, and the point of the noise is that no coastline is a
        // circle. Now the layout decides the first and the noise decides the
        // second, and neither has to do the other's job.
        const float stretch = 1.8f;
        float wander = alone ? 0.55f : 0.5f;

        float Aspect(uint salt) => Mathf.Lerp(1f, stretch, irr * Hash01(seed, salt));
        float Angle(uint salt) => Hash01(seed, salt) * Mathf.Tau;

        var made = new List<Lobe>();

        void Add(float x, float z, float r, uint salt, float aspect = 0f, float rot = float.NaN)
        {
            // Keep every blob inside the footprint with a margin, or a nudge later
            // will push it into the wall.
            float pad = r + 3f;
            int n = p.Size;
            x = Math.Clamp(x, pad, n - 1 - pad);
            z = Math.Clamp(z, pad, n - 1 - pad);
            made.Add(new Lobe(x, z, r,
                              aspect > 0f ? aspect : Aspect(salt),
                              float.IsNaN(rot) ? Angle(salt ^ 0x77u) : rot,
                              LobeRings * (0.8f + 0.5f * Hash01(seed, salt ^ 0xB3u)), wander));
        }

        /// A ring of blobs at a given radius, evenly spaced then jittered.
        /// <paramref name="tangential"/> turns each blob broadside to the ring, so
        /// the ring reads as a chain of arcs rather than as a necklace of beads.
        void Ring(int count, float ringRadius, float blobRadius, float spread, uint salt,
                  float tangential = 0f)
            => Sweep(count, ringRadius, blobRadius, spread, salt, tangential, Mathf.Tau);

        /// As <c>Ring</c>, over part of the circle: <paramref name="arc"/> radians
        /// of it, starting where the seed says. A full <c>Tau</c> is the ring; less
        /// is a crescent, and the jitter is scaled with the sweep so a short arc
        /// does not shake its blobs out of line.
        void Sweep(int count, float ringRadius, float blobRadius, float spread, uint salt,
                   float tangential, float arc)
        {
            float phase = Hash01(seed, salt) * Mathf.Tau;
            float step = arc >= Mathf.Tau - 0.001f ? arc / count : arc / Math.Max(1, count - 1);
            for (int i = 0; i < count; i++)
            {
                uint s = salt ^ (uint)(i + 1) * 2654435761u;
                float a = phase + step * i + (Hash01(seed, s) - 0.5f) * step * 0.7f;
                float rr = ringRadius * (1f - spread * 0.5f + spread * Hash01(seed, s ^ 0x5u));
                float br = blobRadius * (0.75f + 0.5f * Hash01(seed, s ^ 0x9u));
                // An ellipse is squashed along its rotation and stretched across
                // it, so rotating to the radial direction elongates the blob along
                // the tangent — around the lagoon rather than into it.
                float aspect = tangential > 0f
                    ? tangential * (0.85f + 0.4f * Hash01(seed, s ^ 0x11u))
                    : 0f;
                Add(cx + MathF.Cos(a) * rr, cz + MathF.Sin(a) * rr, br, s,
                    aspect, tangential > 0f ? a : float.NaN);
            }
        }

        /// A hub with arms off it, at the given fractions of a turn. The whole
        /// cross / T / L / star family is this one shape with a different set of
        /// spokes — and the *broken* forms are the same again with the seams cut,
        /// which is why they share a case.
        ///
        /// An ellipse is squashed along its own rotation, so an arm is rotated to
        /// the *tangent* to make it point outward. The hub is deliberately wide
        /// and the arms are thick: a cross of thin arms reads as a starfish, and
        /// what is wanted is country with four ways out of it.
        void Arms(float[] spokes, uint salt)
        {
            // **Axis-aligned, always.** A cross rotated 30° is a cross that has
            // stopped meaning "four arms, one per compass point" and started
            // meaning "some arms" — and since the Gates are on the four edges, an
            // arm pointing at an edge is the whole use of the shape.
            Add(cx, cz, radius * 0.40f, salt, 1f, 0f);
            float reach = radius * 0.58f * spread;

            for (int i = 0; i < spokes.Length; i++)
            {
                float a = spokes[i] * Mathf.Tau;
                uint s = salt ^ (uint)(i + 1) * 2654435761u;
                float arm = reach * (0.82f + 0.30f * Hash01(seed, s));
                Add(cx + MathF.Cos(a) * arm, cz + MathF.Sin(a) * arm,
                    radius * 0.34f, s, 1.7f, a + Mathf.Pi * 0.5f);
            }
        }

        /// A coil of blobs from the rim inward.
        ///
        /// <paramref name="sweep"/> is how many turns it makes and
        /// <paramref name="thick"/> how fat the arm is, and those two numbers are
        /// the whole difference between a rosette and a spiral. At one and a bit
        /// turns with a thick arm the lobes overlap into a ring of round bays — a
        /// flower, which is what this produced when it was *meant* to be a spiral
        /// and was good enough to keep. At two and a half turns with a thin arm
        /// the coil stays open and the coast runs alongside itself.
        void Coil(uint salt, float sweep, float thick, int links)
        {
            const float inner = 0.08f;
            float phase = Hash01(seed, salt ^ 0x11u) * Mathf.Tau;
            float outer = radius * 0.86f * spread;

            // For the turns to stay apart, the radius has to fall faster per turn
            // than the arm is wide: (outer - inner) / sweep > 2 * thick. Stopping
            // the coil short of the centre is what buys that room — wound all the
            // way in, the last turns touch and the spiral fills itself in.
            for (int i = 0; i < links; i++)
            {
                float t = i / (float)(links - 1);
                float a = phase + t * Mathf.Tau * sweep;
                float rr = Mathf.Lerp(outer, radius * inner, t);
                uint s = salt ^ (uint)(i + 3) * 2654435761u;
                Add(cx + MathF.Cos(a) * rr, cz + MathF.Sin(a) * rr,
                    radius * thick * (0.85f + 0.3f * Hash01(seed, s)), s, 1.8f,
                    a + Mathf.Pi * 0.5f);
            }
        }

        switch (how)
        {
            // A dominant landmass with islets round it. The islets are placed
            // clear of the main blob; where one lands close enough to touch, the
            // strait carving parts them along the seam.
            case IslandArrangement.Satellites:
                Add(cx, cz, radius * 0.58f, 0x1000u);
                Ring(2 + (int)(Hash01(seed, 0x1001u) * 3f), radius * 0.84f * spread,
                     radius * 0.21f, 0.26f, 0x1002u);
                break;

            // Two halves of one irregular mass, split by the strait that runs
            // between them: a crack rather than a channel between two discs. The
            // blobs are placed close enough to overlap on purpose — what makes
            // them two islands is the cut, so the silhouette can be as ragged as
            // a lone island's.
            case IslandArrangement.Twins:
            {
                float a = Angle(0x2000u);
                float half = radius * 0.44f * spread;
                Add(cx + MathF.Cos(a) * half, cz + MathF.Sin(a) * half, radius * 0.62f, 0x2001u);
                Add(cx - MathF.Cos(a) * half, cz - MathF.Sin(a) * half, radius * 0.56f, 0x2002u);
                break;
            }

            // The same again in three, so the cracks meet at a junction inland.
            case IslandArrangement.Triplets:
                Ring(3, radius * 0.46f * spread, radius * 0.50f, 0.16f, 0x3000u);
                break;

            // Scattered and unequal: two or three near the middle, four or five
            // further out, radii varying by half. An archipelago is defined by
            // having no order to it, which is what separates it from an atoll.
            case IslandArrangement.Archipelago:
                Ring(2 + (int)(Hash01(seed, 0x4000u) * 2f), radius * 0.34f * spread,
                     radius * 0.20f, 0.55f, 0x4001u);
                Ring(3 + (int)(Hash01(seed, 0x4002u) * 3f), radius * 0.80f * spread,
                     radius * 0.19f, 0.55f, 0x4003u);
                break;

            // A ring, and the lagoon is what is *not* placed. Two things separate
            // it from an archipelago, and the old version had neither: the islets
            // are elongated along the ring, so each is an arc of a broken rim
            // rather than a bead, and the water inside is cleared outright — a
            // ring of blobs alone leaves the middle to the shape noise, which
            // fills it in about as often as not.
            case IslandArrangement.BrokenRing:
            {
                float ring = radius * 0.76f * spread;
                float blob = radius * 0.30f;
                Ring(6 + (int)(Hash01(seed, 0x5000u) * 4f), ring, blob, 0.10f, 0x5001u, 2.1f);
                lagoon = MathF.Max(4f, ring - blob * 0.55f);
                break;
            }

            // The same rim, unbroken: more arcs, overlapping, and the seams left
            // alone. What you get is one landmass with a lake of aether in the
            // middle of it — a coast on both sides, which is a thing no other
            // arrangement produces.
            case IslandArrangement.Ring:
            {
                float ring = radius * 0.74f * spread;
                float blob = radius * 0.34f;
                Ring(9 + (int)(Hash01(seed, 0x5100u) * 4f), ring, blob, 0.07f, 0x5101u, 2.2f);
                lagoon = MathF.Max(4f, ring - blob * 0.75f);
                break;
            }

            // Part of a ring: a crescent round an open bay. Two thirds of the
            // circle or so — much less reads as a fat island with a dent, much
            // more closes into a ring.
            case IslandArrangement.Arc:
            case IslandArrangement.BrokenArc:
            {
                bool whole = how == IslandArrangement.Arc;
                float ring = radius * 0.74f * spread;
                float blob = radius * (whole ? 0.34f : 0.30f);
                float arc = Mathf.Tau * (0.52f + 0.18f * Hash01(seed, 0x5200u));
                int count = (whole ? 7 : 5) + (int)(Hash01(seed, 0x5201u) * 3f);
                Sweep(count, ring, blob, whole ? 0.07f : 0.12f, 0x5202u, 2.1f, arc);
                lagoon = MathF.Max(4f, ring - blob * (whole ? 0.75f : 0.55f));
                break;
            }

            // Beads on a string. The islets are round rather than drawn out along
            // the rim, they are placed so their capes overlap, and the strait
            // between each pair is cut to a single step of water — so the ring
            // reads as a row of separate islands that very nearly touch, which is
            // the thing a real atoll looks like from above.
            case IslandArrangement.Atoll:
            {
                float ring = radius * 0.74f * spread;
                float blob = radius * 0.29f;
                Ring(7 + (int)(Hash01(seed, 0x5300u) * 3f), ring, blob, 0.05f, 0x5301u, 1.15f);
                lagoon = MathF.Max(4f, ring - blob * 0.62f);
                break;
            }

            // Too many islands to name, in three loose rings so the middle is as
            // busy as the rim. Each is small enough to be one place and large
            // enough to survive the islet filter.
            case IslandArrangement.ThousandIsles:
                Ring(3 + (int)(Hash01(seed, 0x6000u) * 2f), radius * 0.26f * spread,
                     radius * 0.13f, 0.5f, 0x6001u);
                Ring(5 + (int)(Hash01(seed, 0x6002u) * 3f), radius * 0.58f * spread,
                     radius * 0.13f, 0.45f, 0x6003u);
                Ring(6 + (int)(Hash01(seed, 0x6004u) * 4f), radius * 0.88f * spread,
                     radius * 0.12f, 0.40f, 0x6005u);
                break;

            // One mass with four arms on the cardinal axes. The arms are elongated
            // *radially* — an ellipse is squashed along its own rotation, so the
            // rotation given is the tangent — and they overlap the hub, so what
            // comes out is one landmass with four long peninsulas and four deep
            // bays between them.
            case IslandArrangement.Cross:
            case IslandArrangement.BrokenCross:
                Arms(new[] { 0f, 0.25f, 0.5f, 0.75f }, 0x7000u);
                break;

            // Three arms: a bar with a stem off the middle of it.
            case IslandArrangement.TShape:
            case IslandArrangement.BrokenT:
                Arms(new[] { 0f, 0.25f, 0.75f }, 0x7100u);
                break;

            // Two, meeting at a right angle: a corner of land round one wide bay.
            case IslandArrangement.LShape:
            case IslandArrangement.BrokenL:
                Arms(new[] { 0f, 0.25f }, 0x7200u);
                break;

            // Five or six, so no two face each other and every bay is a wedge.
            case IslandArrangement.Star:
            {
                int points = 5 + (int)(Hash01(seed, 0x7300u) * 2f);
                var spokes = new float[points];
                for (int i = 0; i < points; i++) spokes[i] = (float)i / points;
                Arms(spokes, 0x7301u);
                break;
            }

            // A snake. Each blob is placed a stride on from the last, the heading
            // turning by up to a right angle each time and bouncing off the edge of
            // the footprint, so the land doubles back on itself and the coast has
            // as much length as the island has area. The blobs overlap, so it is
            // one winding landmass rather than a row of islets.
            case IslandArrangement.Fractal:
            case IslandArrangement.BrokenFractal:
            {
                float blob = radius * 0.24f;
                float heading = Angle(0x8000u);
                float wx = cx + MathF.Cos(heading + Mathf.Pi) * radius * 0.45f;
                float wz = cz + MathF.Sin(heading + Mathf.Pi) * radius * 0.45f;
                int links = 6 + (int)(Hash01(seed, 0x8001u) * 3f);

                for (int i = 0; i < links; i++)
                {
                    uint s = 0x8002u ^ (uint)(i + 1) * 2654435761u;
                    float br = blob * (0.78f + 0.44f * Hash01(seed, s));
                    Add(wx, wz, br, s, 1.5f, heading + Mathf.Pi * 0.5f);

                    // Turn, then step. Turning first is what makes the chain wind
                    // rather than fan out from its first blob.
                    heading += (Hash01(seed, s ^ 0x3Bu) - 0.5f) * Mathf.Pi * 0.62f;
                    float stride = br * 1.35f;
                    float nx = wx + MathF.Cos(heading) * stride;
                    float nz = wz + MathF.Sin(heading) * stride;
                    // Bounce off the footprint rather than clamping into it: a
                    // clamped walk piles every remaining blob against one wall.
                    float pad = radius * 0.30f;
                    if (nx < cx - radius + pad || nx > cx + radius - pad)
                    {
                        heading = Mathf.Pi - heading;
                        nx = wx + MathF.Cos(heading) * stride;
                        nz = wz + MathF.Sin(heading) * stride;
                    }
                    if (nz < cz - radius + pad || nz > cz + radius - pad)
                    {
                        heading = -heading;
                        nx = wx + MathF.Cos(heading) * stride;
                        nz = wz + MathF.Sin(heading) * stride;
                    }
                    wx = nx;
                    wz = nz;
                }
                break;
            }

            case IslandArrangement.Rosette:
                Coil(0xA000u, sweep: 1.35f, thick: 0.23f,
                     links: 9 + (int)(Hash01(seed, 0xA000u) * 4f));
                break;

            // One island cracked. The blobs are laid over each other in a tight
            // cluster and the seams are cut narrow, so what parts the pieces reads
            // as a fracture rather than as a channel.
            case IslandArrangement.Shards:
                Add(cx, cz, radius * 0.44f, 0x9000u);
                Ring(3 + (int)(Hash01(seed, 0x9001u) * 3f), radius * 0.42f * spread,
                     radius * 0.42f, 0.18f, 0x9002u);
                break;

            default:
                Add(cx, cz, radius, 0x0001u);
                break;
        }

        // Which arrangements are one landmass with a shape, and which are several
        // pieces. The seam carving is the whole difference — see Layout.
        bool cut = how switch
        {
            IslandArrangement.Single => false,
            IslandArrangement.Ring => false,
            IslandArrangement.Arc => false,
            IslandArrangement.Cross => false,
            IslandArrangement.Fractal => false,
            IslandArrangement.TShape => false,
            IslandArrangement.LShape => false,
            IslandArrangement.Rosette => false,
            IslandArrangement.Star => false,
            _ => true,
        };
        // An atoll's islets all but touch, and a shard's crack is a crack.
        float narrow = how switch
        {
            IslandArrangement.Atoll => 1.7f,
            IslandArrangement.Shards => 1.9f,
            _ => 0f,
        };
        // The layouts that are a shape rather than a scatter: a thin arm perforated
        // by the coverage threshold stops being an arm.
        float solid = how switch
        {
            IslandArrangement.Fractal => 0.86f,
            IslandArrangement.BrokenFractal => 0.86f,
            _ => 0f,
        };
        return new Layout(made.ToArray(), lagoon, cut, narrow, solid);
    }

    /// <summary>
    /// How many separate landmasses an arrangement has to deliver to be that
    /// arrangement. Twins with one island is not Twins; an Archipelago whose
    /// islets partly merge still reads as an archipelago, so the bar is lower
    /// where merging is in character.
    /// </summary>
    private static int MassesWanted(IslandArrangement how) => how switch
    {
        IslandArrangement.Twins => 2,
        IslandArrangement.Triplets => 3,
        IslandArrangement.Satellites => 3,
        IslandArrangement.Archipelago => 4,
        IslandArrangement.BrokenRing => 4,
        IslandArrangement.BrokenArc => 3,
        IslandArrangement.Atoll => 5,
        IslandArrangement.ThousandIsles => 8,
        IslandArrangement.Shards => 4,
        IslandArrangement.BrokenCross => 4,
        IslandArrangement.BrokenT => 3,
        IslandArrangement.BrokenL => 2,
        IslandArrangement.BrokenFractal => 4,
        // Ring, Arc, Cross and Fractal are one landmass with a shape: their blobs
        // are meant to fuse, so counting pieces would push them apart until they
        // stopped being the shape they name.
        _ => 1,
    };

    /// <summary>
    /// Builds the footprint, pushing the blobs further apart and trying again if
    /// the layout did not come out as the arrangement it claims to be.
    ///
    /// Placing them "far enough apart" analytically does not work: a lobe's reach
    /// is its radius times its ellipse aspect times its coastline wander, so the
    /// spacing that never fuses is wide enough to make Twins two small islands in
    /// a large empty field. Measuring the result and widening only when it
    /// actually fused keeps the common case tight.
    /// </summary>
    private static bool[,] BuildMask(int seed, IslandParams p, IslandArrangement how)
    {
        bool[,] mask = BuildMaskOnce(seed, p, how, 1f);
        int wanted = MassesWanted(how);
        if (wanted <= 1) return mask;

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            DropComponentsUnder(mask, MinIsletCells);
            if (CountMasses(mask) >= wanted) return mask;
            mask = BuildMaskOnce(seed, p, how, 1f + 0.16f * attempt);
        }
        DropComponentsUnder(mask, MinIsletCells);
        return mask;
    }

    private static int CountMasses(bool[,] mask)
    {
        int n = mask.GetLength(0);
        return Components(mask, new int[n, n]).Count;
    }

    private static bool[,] BuildMaskOnce(int seed, IslandParams p, IslandArrangement how,
                                         float spread)
    {
        int n = p.Size;
        float radius = AutoRadius(p);
        float cx = (n - 1) * 0.5f, cz = (n - 1) * 0.5f;
        float irr = Math.Clamp(p.Irregularity, 0f, 1f);

        Layout layout = PlaceLobes(seed, p, how, radius, cx, cz, spread);
        Lobe[] lobes = layout.Lobes;
        float lagoon = layout.Lagoon;

        var wobble = new Noise(seed + 23, frequency: 1f, octaves: 2);
        var shape = new Noise(seed, frequency: 0.05f, octaves: 4)
            .WithWarp(amplitude: (0.25f + 0.55f * irr) * n, frequency: 0.6f / n);
        // How wide the water is where two lobes meet. Wandering, so the strait
        // narrows to a step across in places and opens to a channel in others.
        var strait = new Noise(seed + 907, frequency: 0.09f, octaves: 3);
        // A bridge reaches `Crossings` cells, so a strait that opens wider than
        // that would only have to be dragged shut again by the linker. Keeping the
        // widest part just inside the span means the arrangement's own geometry is
        // crossable as it stands.
        float straitCells = layout.StraitWide > 0f
            ? layout.StraitWide
            : MathF.Max(1.4f, (int)p.Crossings + 0.4f);

        // Bites are not taken here: cutting a shape out of the raw mask leaves an
        // arc across whatever patches it crosses. They are applied to whole
        // regions once those exist — see BiteRegions.

        var field = new float[n, n];
        var norm = new float[n, n];
        var owner = new int[n, n];
        var cut = new bool[n, n];
        var candidates = new List<float>[lobes.Length];
        for (int i = 0; i < lobes.Length; i++) candidates[i] = new List<float>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            // The nearest blob wins, and owns the cell. Taking the minimum rather
            // than summing keeps two islets from fusing into a peanut just because
            // they are close. The runner-up is kept as well: where the two agree
            // is the seam between them, and the seam is where the strait goes.
            float d = float.MaxValue, second = float.MaxValue;
            float rd = 1f, rSecond = 1f;
            int mine = 0;
            for (int i = 0; i < lobes.Length; i++)
            {
                float di = lobes[i].Distance(x, z, wobble, irr, out float ri);
                if (di < d)
                {
                    second = d; rSecond = rd;
                    d = di; rd = ri; mine = i;
                }
                else if (di < second) { second = di; rSecond = ri; }
            }
            norm[x, z] = d;
            owner[x, z] = mine;

            // Turn the difference between the two normalised distances back into
            // cells — a normalised unit is one lobe radius — and clear a band of
            // them either side of the seam. The band never closes completely: a
            // strait that heals is an arrangement that quietly delivered fewer
            // landmasses than it promised, which is exactly what used to happen to
            // Twins.
            if (layout.Straits && lobes.Length > 1 && second < float.MaxValue)
            {
                float seam = (second - d) * 0.5f * (rd + rSecond);
                float width = StraitNarrowest
                              + (straitCells - StraitNarrowest) * strait.At(x, z);
                cut[x, z] = seam < width;
            }

            // An atoll's lagoon is cleared outright rather than left to the shape
            // noise, which fills the middle of the ring as often as not — and a
            // filled atoll is an archipelago.
            if (lagoon > 0f)
            {
                float lx = x - cx, lz = z - cz;
                float wob = 0.86f + 0.28f * wobble.At(lx * 0.09f, lz * 0.09f);
                if (lx * lx + lz * lz < lagoon * lagoon * wob) cut[x, z] = true;
            }

            float fall = 1f - FieldOps.SmoothStep(0.40f, 1f, d);
            float body = 0.35f + 0.65f * shape.At(x, z);
            field[x, z] = fall * body;

            // `fall` is already 0 at d >= 1, so only the blobs themselves can be
            // land. Sampling wider would pad the quantile with guaranteed zeroes
            // and drag the threshold to 0, which is what made Coverage inert.
            if (d < 1f) candidates[mine].Add(field[x, z]);
        }

        // A threshold *per lobe*. One global cut makes Coverage a fraction of the
        // whole layout, so a lobe that happens to sit under a low patch of the
        // shape noise is simply deleted — which is what left a third of Twins with
        // one island. Per lobe it means what it says: this share of each blob
        // becomes land.
        float want = 1f - Math.Clamp(MathF.Max(p.Coverage, layout.Solid), 0.01f, 0.99f);
        var threshold = new float[lobes.Length];
        for (int i = 0; i < lobes.Length; i++)
            threshold[i] = FieldOps.Quantile(candidates[i], want);

        var mask = new bool[n, n];
        // Leave a one-cell border empty so every land cell has a reachable coast.
        for (int x = 1; x < n - 1; x++)
        for (int z = 1; z < n - 1; z++)
            mask[x, z] = norm[x, z] < 1f && field[x, z] > threshold[owner[x, z]]
                         && !cut[x, z];

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
    /// Fills the corner where two cells <b>of the same landmass</b> touch only
    /// diagonally. A corner is not a join you can walk, so left alone it is a
    /// hairline break the component filter cannot see — both sides are already
    /// one component, so nothing else will notice.
    ///
    /// <b>Only within a landmass.</b> Welding a diagonal touch between two
    /// separate islands does not heal anything, it deletes an island: two of them
    /// become one. That is what left a third of Twins with a single landmass, and
    /// no amount of pushing the blobs apart fixed it, because the merge happened
    /// after the footprint was drawn. Two islands a corner apart are two islands,
    /// and the bridge rule is what joins them.
    /// </summary>
    private static void CloseDiagonalJoins(bool[,] mask)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        Components(mask, comp);

        for (int x = 1; x + 2 < n; x++)
        for (int z = 1; z + 2 < n; z++)
        {
            bool a = mask[x, z], b = mask[x + 1, z + 1];
            bool c = mask[x + 1, z], e = mask[x, z + 1];

            if (a && b && !c && !e && comp[x, z] == comp[x + 1, z + 1])
                Fill(x + 1, z, x, z + 1);
            else if (c && e && !a && !b && comp[x + 1, z] == comp[x, z + 1])
                Fill(x, z, x + 1, z + 1);
        }

        // The filled cell is the one with more land around it, so the coast stays
        // plausible.
        void Fill(int ax, int az, int bx, int bz)
        {
            bool first = Neighbours(ax, az) >= Neighbours(bx, bz);
            mask[first ? ax : bx, first ? az : bz] = true;
        }

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

    /// <summary>Smallest thing that counts as an islet. Below it, it is coastline noise.</summary>
    private const int MinIsletCells = 30;

    /// <summary>Labels 4-connected land; returns per-component cell lists.</summary>
    private static List<List<Vector2I>> Components(bool[,] mask, int[,] into)
    {
        int n = mask.GetLength(0);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) into[x, z] = -1;

        var found = new List<List<Vector2I>>();
        var stack = new Stack<Vector2I>();

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!mask[sx, sz] || into[sx, sz] >= 0) continue;

            int id = found.Count;
            var cells = new List<Vector2I>();
            into[sx, sz] = id;
            stack.Push(new Vector2I(sx, sz));

            while (stack.Count > 0)
            {
                Vector2I c = stack.Pop();
                cells.Add(c);
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!mask[nx, nz] || into[nx, nz] >= 0) continue;
                    into[nx, nz] = id;
                    stack.Push(new Vector2I(nx, nz));
                }
            }
            found.Add(cells);
        }
        return found;
    }

    private static void DropComponentsUnder(bool[,] mask, int minCells)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        List<List<Vector2I>> parts = Components(mask, comp);
        foreach (List<Vector2I> cells in parts)
        {
            if (cells.Count >= minCells) continue;
            foreach (Vector2I c in cells) mask[c.X, c.Y] = false;
        }
    }

    /// <summary>
    /// Every pair of landmasses that face each other across at most
    /// <paramref name="span"/> empty cells, <b>cardinally</b> — the way a bridge
    /// sees it. A diagonal near-miss is not a crossing.
    ///
    /// One sweep of the grid for all pairs at once. Asking pair by pair is the
    /// obvious shape and it is cubic: a Triplets layout spent 400 ms per island
    /// re-scanning the whole field for every candidate on every nudge.
    /// </summary>
    private static Dictionary<long, (Vector2I A, Vector2I B, int Gap)> FacingPairs(
        bool[,] mask, int[,] comp, int span)
    {
        int n = mask.GetLength(0);
        var found = new Dictionary<long, (Vector2I, Vector2I, int)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!mask[x, z]) continue;
            int a = comp[x, z];

            // Only +X and +Z: the opposite ray is the same crossing seen from the
            // far bank.
            for (int k = 0; k < 2; k++)
            {
                int dx = k == 0 ? 1 : 0, dz = k == 0 ? 0 : 1;
                for (int step = 2; step <= span + 1; step++)
                {
                    int nx = x + dx * step, nz = z + dz * step;
                    if (nx >= n || nz >= n) break;
                    // The first solid cell ends the ray: it is either the far bank
                    // or a third island in the way.
                    if (!mask[nx, nz]) continue;

                    int b = comp[nx, nz];
                    if (b != a)
                    {
                        long key = Pair(a, b);
                        int gap = step - 1;
                        if (!found.TryGetValue(key, out var had) || gap < had.Item3)
                            found[key] = (new Vector2I(x, z), new Vector2I(nx, nz), gap);
                    }
                    break;
                }
            }
        }
        return found;
    }

    private static Vector2 Centroid(List<Vector2I> cells)
    {
        float x = 0f, z = 0f;
        foreach (Vector2I c in cells) { x += c.X; z += c.Y; }
        return new Vector2(x / cells.Count, z / cells.Count);
    }

    private static long Pair(int a, int b)
        => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    /// <summary>
    /// Which landmasses are already joined into one linkable whole, growing out
    /// from the largest.
    /// </summary>
    private static HashSet<int> LinkedSet(int count, Dictionary<long, (Vector2I A, Vector2I B, int Gap)> facing,
                                          int seedPart, int span)
    {
        var linked = new HashSet<int> { seedPart };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (key, v) in facing)
            {
                if (v.Gap > span) continue;
                int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
                if (linked.Contains(a) == linked.Contains(b)) continue;
                linked.Add(a);
                linked.Add(b);
                grew = true;
            }
        }
        return linked;
    }

    /// <summary>
    /// Nudges landmasses together until every one can be reached from the next by
    /// a bridge — land facing land across at most
    /// <see cref="IslandParams.Crossings"/> cells, cardinally.
    ///
    /// An archipelago is meant to be an island you build your way across, not a
    /// set of separate worlds. A layout that is *nearly* linkable is the common
    /// case — the lobes are placed to nearly touch and the coastline noise decides
    /// whether they do — so rather than rejecting and re-rolling the whole
    /// footprint, the offending piece is translated bodily toward the body it
    /// should be joined to. That preserves its shape, where widening it or filling
    /// the gap with a spit would leave a visible causeway.
    ///
    /// Anything that still cannot be linked is deleted. A piece nobody can ever
    /// reach is not content.
    /// </summary>
    private static void LinkLandmasses(bool[,] mask, int span)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        // Far enough to cross the widest strait a lobe layout produces, and short
        // enough that the sweep stays cheap.
        const int Sightline = 48;

        for (int round = 0; round < 40; round++)
        {
            List<List<Vector2I>> parts = Components(mask, comp);
            if (parts.Count <= 1) return;

            int biggest = 0;
            for (int i = 1; i < parts.Count; i++)
                if (parts[i].Count > parts[biggest].Count) biggest = i;

            var near = FacingPairs(mask, comp, span);
            HashSet<int> linked = LinkedSet(parts.Count, near, biggest, span);
            if (linked.Count == parts.Count) return;

            // The closest stray, over a long sightline this time, and how far it
            // has to travel. Moving the whole distance at once is what keeps this
            // to a handful of rounds instead of hundreds.
            var far = FacingPairs(mask, comp, Sightline);
            long bestKey = -1;
            int bestGap = int.MaxValue;

            foreach (var (key, v) in far)
            {
                int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
                if (linked.Contains(a) == linked.Contains(b)) continue;
                if (v.Gap >= bestGap) continue;
                bestGap = v.Gap;
                bestKey = key;
            }

            if (bestKey < 0)
            {
                // Nothing adrift has a cardinal sightline to the linked body — two
                // islands set diagonally apart can miss each other entirely on both
                // axes. Deleting one was the first answer and it was wrong: it is
                // how a third of Twins came out with a single island. Slide the
                // stray sideways instead, along whichever axis it is *closer* to
                // aligned on, until it does have a line of sight.
                // Every stray, nearest first — not just the nearest one. An islet
                // pinned against the footprint wall cannot move toward a linked
                // body that lies further out, and giving up there abandoned every
                // *other* stray with it, which is how a seventeen-island layout
                // came out with one piece adrift.
                var strays = new List<(int Part, Vector2 Mine, Vector2 Theirs, float Dist)>();
                for (int i = 0; i < parts.Count; i++)
                {
                    if (linked.Contains(i)) continue;
                    Vector2 mid = Centroid(parts[i]);
                    foreach (int j in linked)
                    {
                        Vector2 other = Centroid(parts[j]);
                        strays.Add((i, mid, other, mid.DistanceTo(other)));
                    }
                }
                strays.Sort((a, b) => a.Dist.CompareTo(b.Dist));

                bool slid = false;
                foreach (var (part, mine, theirs, _) in strays)
                {
                    float ax = theirs.X - mine.X, az = theirs.Y - mine.Y;
                    int sx = MathF.Abs(ax) <= MathF.Abs(az) ? Math.Sign(ax) : 0;
                    int sz = sx == 0 ? Math.Sign(az) : 0;
                    if (sx == 0 && sz == 0) continue;

                    // If the slide is blocked, try the other axis before moving on.
                    // Deleting a stray here was costing whole islands: an
                    // arrangement that promised two landmasses would quietly
                    // deliver one.
                    if (Translate(mask, parts[part], sx, sz)
                        || Translate(mask, parts[part], Math.Sign(ax) - sx, Math.Sign(az) - sz))
                    {
                        slid = true;
                        break;
                    }
                }
                if (!slid) return;              // nothing adrift can move at all
                continue;
            }

            var (from, toward, gap) = far[bestKey];
            int strayId = linked.Contains(comp[from.X, from.Y])
                ? comp[toward.X, toward.Y]
                : comp[from.X, from.Y];
            Vector2I anchor = linked.Contains(comp[from.X, from.Y]) ? toward : from;
            Vector2I goal = linked.Contains(comp[from.X, from.Y]) ? from : toward;

            int dx = Math.Sign(goal.X - anchor.X);
            int dz = Math.Sign(goal.Y - anchor.Y);
            int want = gap - span;

            int moved = 0;
            while (moved < want && Translate(mask, parts[strayId], dx, dz))
            {
                for (int i = 0; i < parts[strayId].Count; i++)
                    parts[strayId][i] = new Vector2I(parts[strayId][i].X + dx,
                                                     parts[strayId][i].Y + dz);
                moved++;
            }
            // Blocked before it could move at all: the pieces are already as close
            // as the field allows. Leave it — the final sweep decides whether it
            // is linked, and deleting here would silently drop a landmass the
            // arrangement promised.
            if (moved == 0) return;
        }

        // Budget spent. Whatever is still adrift goes, so the guarantee holds
        // rather than merely usually holding.
        List<List<Vector2I>> last = Components(mask, comp);
        if (last.Count <= 1) return;

        int keep = 0;
        for (int i = 1; i < last.Count; i++) if (last[i].Count > last[keep].Count) keep = i;

        HashSet<int> survivors = LinkedSet(last.Count, FacingPairs(mask, comp, span), keep, span);
        for (int i = 0; i < last.Count; i++)
            if (!survivors.Contains(i))
                foreach (Vector2I c in last[i]) mask[c.X, c.Y] = false;
    }

    /// <summary>
    /// The crossings that hold an archipelago together: one cell pair per bridge,
    /// enough to join every landmass into a single linked set. Found after the
    /// nudging, on the layout as it finally stands.
    /// </summary>
    private static List<(Vector2I A, Vector2I B)> FindBridgeSites(bool[,] mask, int span)
    {
        int n = mask.GetLength(0);
        var comp = new int[n, n];
        List<List<Vector2I>> parts = Components(mask, comp);
        var found = new List<(Vector2I, Vector2I)>();
        if (parts.Count <= 1) return found;

        int biggest = 0;
        for (int i = 1; i < parts.Count; i++)
            if (parts[i].Count > parts[biggest].Count) biggest = i;

        var facing = FacingPairs(mask, comp, span);
        var linked = new HashSet<int> { biggest };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var (key, v) in facing)
            {
                int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
                if (linked.Contains(a) == linked.Contains(b)) continue;
                found.Add((v.A, v.B));
                linked.Add(a);
                linked.Add(b);
                grew = true;
            }
        }
        return found;
    }

    /// <summary>
    /// Slabs of disagreement between two banks that levelling will still close.
    /// Beyond this the crossing is left alone: cutting a bank down by more than
    /// a stair's worth to meet the far side gouges a notch in the coast, and the
    /// two banks were meant to have been put on one rung long before this.
    /// </summary>
    private const int MaxBridgeheadDrop = 8;

    /// <summary>Cells either side of a bridgehead that come down with it.</summary>
    private const int BridgeheadPad = 1;

    /// <summary>
    /// Brings the two ends of every crossing to one level.
    ///
    /// <b>A bridge is a run of slabs at a single level.</b> It does not climb, so
    /// a deck between banks eight slabs apart is not a bridge — it is a lift with
    /// a deck on it, which is what the old <c>MaxBridgeRise</c> was quietly
    /// allowing. Levelling here, rather than relaxing the rule there, is what
    /// makes a crossing something you can walk onto at both ends.
    ///
    /// It only ever <i>lowers</i>, which is what lets the settle loop that
    /// follows clean up the step it leaves without a special case — and it will
    /// not touch ground beside a lake, since cutting a shore down is how you
    /// empty one.
    /// </summary>
    /// <returns>Whether any ground was lowered.</returns>
    private static bool LevelBridgeheads(bool[,] land, short[,] surface, short[,] water,
                                         int[,] region, RegionPlan[] plan,
                                         List<(Vector2I A, Vector2I B)> bridges)
    {
        int n = land.GetLength(0);
        bool moved = false;

        foreach (var (a, b) in bridges)
        {
            if (!land[a.X, a.Y] || !land[b.X, b.Y]) continue;

            int la = surface[a.X, a.Y], lb = surface[b.X, b.Y];
            if (Math.Abs(la - lb) > MaxBridgeheadDrop) continue;

            short target = SlabClamp(Math.Min(la, lb));
            moved |= FlattenPad(land, surface, water, region, plan, a, target, n);
            moved |= FlattenPad(land, surface, water, region, plan, b, target, n);
        }
        return moved;
    }

    private static bool FlattenPad(bool[,] land, short[,] surface, short[,] water,
                                   int[,] region, RegionPlan[] plan,
                                   Vector2I c, short target, int n)
    {
        bool moved = false;
        for (int dx = -BridgeheadPad; dx <= BridgeheadPad; dx++)
        for (int dz = -BridgeheadPad; dz <= BridgeheadPad; dz++)
        {
            int x = c.X + dx, z = c.Y + dz;
            if (x < 0 || z < 0 || x >= n || z >= n) continue;
            if (!land[x, z] || surface[x, z] <= target) continue;
            if (NearWater(water, n, x, z)) continue;
            // A landing is plains ground. Cutting a pad into a mesa's rim or a
            // mountain's foot would take the landform's own height away from it —
            // and cutting the plain beside a basin down past the basin floor turns
            // the escarpment upside down, which is how a basin came out standing
            // three slabs *above* the country around it.
            if (plan[region[x, z]].Type is not (LandformType.Plain or LandformType.Hills))
                continue;
            if (target < BasinFloorNear(land, surface, region, plan, n, x, z)) continue;
            surface[x, z] = target;
            moved = true;
        }
        return moved;
    }

    /// <summary>
    /// The lowest a cell beside a basin may be cut to and leave the escarpment
    /// facing the right way: a cliff's height above the floor it looks down on.
    /// </summary>
    private static int BasinFloorNear(bool[,] land, short[,] surface, int[,] region,
                                      RegionPlan[] plan, int n, int x, int z)
    {
        int floor = int.MinValue;
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int nx = x + dx, nz = z + dz;
            if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
            if (plan[region[nx, nz]].Type != LandformType.Basin) continue;
            floor = Math.Max(floor, surface[nx, nz] + 3);
        }
        return floor;
    }

    /// <summary>Whether a cell or any of its eight neighbours holds standing water.</summary>
    private static bool NearWater(short[,] water, int n, int x, int z)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int nx = x + dx, nz = z + dz;
            if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
            if (water[nx, nz] != IslandData.NoLand) return true;
        }
        return false;
    }

    /// <summary>
    /// Records each crossing as it finally stands: the level its deck runs at,
    /// halfway between the two banks so each end is a one-slab step, and how many
    /// cells of nothing it has to cover.
    /// </summary>
    private static void RecordCrossings(IslandData d, List<(Vector2I A, Vector2I B)> pairs)
    {
        foreach (var (a, b) in pairs)
        {
            if (!d.HasLand(a.X, a.Y) || !d.HasLand(b.X, b.Y)) continue;

            int la = Traversal.CrossLevel(d, a.X, a.Y);
            int lb = Traversal.CrossLevel(d, b.X, b.Y);
            short deck = SlabClamp(Mathf.RoundToInt((la + lb) * 0.5f));
            int span = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) - 1;
            d.Bridges.Add(new Crossing(a, b, deck, span));
        }
    }

    /// <summary>Moves one landmass by a cell, refusing if it would leave the field or collide.</summary>
    private static bool Translate(bool[,] mask, List<Vector2I> cells, int dx, int dz)
    {
        if (dx == 0 && dz == 0) return false;
        int n = mask.GetLength(0);

        var moving = new HashSet<Vector2I>(cells);
        foreach (Vector2I c in cells)
        {
            int nx = c.X + dx, nz = c.Y + dz;
            // The one-cell border stays empty so every land cell has a coast.
            if (nx < 1 || nz < 1 || nx >= n - 1 || nz >= n - 1) return false;
            if (mask[nx, nz] && !moving.Contains(new Vector2I(nx, nz))) return false;
        }

        foreach (Vector2I c in cells) mask[c.X, c.Y] = false;
        foreach (Vector2I c in cells) mask[c.X + dx, c.Y + dz] = true;
        return true;
    }

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
        TerrainCharacter character = ResolveCharacter(seed, p);
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
                                                int[] cells, HashSet<int> bridgeheads)
    {
        float[] weights = TypeWeights(ResolveCharacter(seed, p));

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
                                               HashSet<int>[] neighbours, LandformType[] type,
                                               List<(Vector2I A, Vector2I B)> bridges)
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
    private static int SlopeLimit(LandformType type) => type switch
    {
        // Unbounded: the mountain's S-curve *is* its shape, and clamping it would
        // shave exactly the steep band the profile exists to produce.
        LandformType.Mountain => 1 << 20,
        _ => 1,
    };

    private static float ReliefScale(IslandParams p) => 0.4f + 1.3f * Math.Clamp(p.Relief, 0f, 1f);

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
    private static bool IsSculpted(LandformType t)
        => t is LandformType.Badlands or LandformType.Karst or LandformType.Massif
             or LandformType.Sinkholes;

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
                                         RegionPlan[] plan, float[,] inward, out int duneGrain)
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

        // The grain of a dune field: one direction for the whole Domain, because
        // what makes dunes dunes is that they all lie the same way.
        //
        // <b>Snapped to a compass point.</b> It used to be a free angle, which is
        // more natural and unnameable: nothing on screen or in the data said which
        // way the wind blew, and a field of ridges at 37° reads as noise with a
        // bias. On one of the eight compass points it is a fact about the Domain —
        // "the wind is from the north-east" — that the readout can say, the
        // compass overlay can draw, and the content layer can use.
        int point = (int)(Hash01(seed, 0xD0E5u) * 8f) & 7;
        float grain = point * (Mathf.Tau / 8f);
        float gcos = MathF.Cos(grain), gsin = MathF.Sin(grain);
        duneGrain = point;
        var drift = new Noise(seed + 404, frequency: 0.035f, octaves: 2);

        // Pass 1 — everything that sits on a rung.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) { h[x, z] = IslandData.NoLand; continue; }
            RegionPlan rp = plan[region[x, z]];
            if (rp.Type == LandformType.Mountain) { isMountain[x, z] = true; continue; }

            float t;
            if (rp.Type == LandformType.Dunes)
            {
                // A wave along the grain rather than a blob field: the crest line
                // is what a dune has and a hill does not. The phase wanders, so
                // the ridges bend and occasionally fork instead of ruling the
                // patch into stripes.
                float along = x * gcos + z * gsin;
                float phase = along * (Mathf.Tau / DuneWavelength)
                              + (drift.At(x, z) - 0.5f) * 4f;
                t = 0.5f + 0.5f * MathF.Sin(phase);
            }
            else
            {
                float dw = 0.5f + 0.3f * hilly;
                t = dw * detail.At(x, z) + (1f - dw) * coarse.At(x, z);
            }
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

    /// <summary>Cells from one dune crest to the next, across the grain.</summary>
    private const float DuneWavelength = 15f;

    /// <summary>
    /// Steps down onto a beach, and how many cells of coast one takes.
    ///
    /// A Domain's coast is a cliff to the keel everywhere, which is why every
    /// shoreline reads the same. Where the ground arrives at the rim gently — a
    /// plain, hills or dunes, level with its neighbours — the outermost cells step
    /// down a slab instead, and that one slab is the difference between land that
    /// stops and land that *meets* the aether. It is free-step ground, so nothing
    /// about walking changes; it gives a quay somewhere natural to sit and the
    /// silhouette a softer edge where the terrain earns one.
    /// </summary>
    private const int BeachWidth = 2;

    /// <summary>
    /// Steps the outermost cells of a gentle coast down, one slab per cell.
    ///
    /// <b>Grammar-safe by construction:</b> the drop is a whole band at a time, so
    /// two cells in the same band keep the height they had relative to each other
    /// and the only new step is the one slab between one band and the next — which
    /// is the free step. Steep coasts, mesa rims, basin walls and anything already
    /// under water are left alone: a beach is what a *shallow* shore does.
    /// </summary>
    private static void MakeBeaches(bool[,] land, short[,] surface, short[,] water,
                                    int[,] region, RegionPlan[] plan, bool[,] beach)
    {
        int n = land.GetLength(0);
        var toRim = new int[n, n];
        var q = new Queue<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            toRim[x, z] = -1;
            if (!land[x, z]) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx >= 0 && nz >= 0 && nx < n && nz < n && land[nx, nz]) continue;
                toRim[x, z] = 0;
                q.Enqueue((x, z));
                break;
            }
        }
        while (q.Count > 0)
        {
            var (x, z) = q.Dequeue();
            if (toRim[x, z] >= BeachWidth) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!land[nx, nz] || toRim[nx, nz] >= 0) continue;
                toRim[nx, nz] = toRim[x, z] + 1;
                q.Enqueue((nx, nz));
            }
        }

        // Only where the coast arrives gently: soft ground, dry, and no cell in
        // the band standing more than a slab off its neighbours.
        var gentle = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z] || toRim[x, z] < 0 || toRim[x, z] >= BeachWidth) continue;
            if (water[x, z] != IslandData.NoLand) continue;

            LandformType type = plan[region[x, z]].Type;
            if (type is not (LandformType.Plain or LandformType.Hills or LandformType.Dunes))
                continue;

            bool even = true;
            for (int k = 0; k < 4 && even; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                even = Math.Abs(surface[nx, nz] - surface[x, z]) <= 1
                       && water[nx, nz] == IslandData.NoLand;
            }
            gentle[x, z] = even;
        }

        // How far each cell wants to come down, tapered so the edge of the beach
        // is a free step rather than a two-slab drop — see FieldOps.Taper.
        // <b>One slab, not a ramp.</b> A graduated beach spends two slabs of height
        // over two cells of coast, and two slabs is the entire tolerance a landing
        // strip has — so every beached coast stopped being able to host a hanging
        // Gate, and hanging Gates fell from most of them to a quarter. A flat
        // shelf a single slab down still reads as a beach and leaves the strip
        // somewhere to sit.
        var drop = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (gentle[x, z]) drop[x, z] = 1;
        FieldOps.Taper(drop, land);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (drop[x, z] <= 0) continue;
            surface[x, z] = SlabClamp(surface[x, z] - drop[x, z]);
            beach[x, z] = true;
        }
    }

    /// <summary>Slabs a badlands gully is cut below the fingers either side of it.</summary>
    private const int GullyDepth = 5;

    /// <summary>Slabs a karst tower stands above the floor it grows out of.</summary>
    private const int TowerRise = 13;

    /// <summary>Slabs from one ziggurat terrace to the next.</summary>
    private const int TerraceRiser = 4;

    /// <summary>Slabs a sinkhole drops below the ground it is punched out of.</summary>
    private const int SinkDepth = 6;

    /// <summary>
    /// Cuts and raises the sculpted landforms into the finished plain.
    ///
    /// <para><b>Why this is a separate pass.</b> Every other landform is relief
    /// under a slope limit, which is what makes the step grammar hold by
    /// construction — and it is also why the ladder can only put a cliff at a
    /// patch <i>border</i>. A gully, a tower and a terrace riser are cliffs
    /// <i>inside</i> a patch, so they cannot come from relief at all. They are
    /// cut into a surface the limiter has already settled, and the cells they
    /// touch are then exempted from it, exactly as a canyon is — that is the
    /// mechanism the pipeline already had for "a cliff somebody asked for".</para>
    ///
    /// <para><b>Nothing is sculpted on a patch border.</b> The outermost ring of
    /// every patch is left at the level the limiter agreed with the neighbours,
    /// so a badlands beside a plain still meets it at a walkable step and the
    /// cliff rule holds at every border it has. All the drama is interior.</para>
    /// </summary>
    /// <returns>The cells that were cut or raised, to be exempted from the limiter.</returns>
    private static bool[,] Sculpt(int seed, IslandParams p, bool[,] land, int[,] region,
                                  RegionPlan[] plan, short[,] h, float[,] inward)
    {
        int n = p.Size;
        var carved = new bool[n, n];
        float scale = ReliefScale(p);

        bool any = false;
        foreach (RegionPlan rp in plan) if (IsSculpted(rp.Type)) { any = true; break; }
        if (!any) return carved;

        // One field per landform, so two of them on one island do not share a
        // pattern. The gullies are ridged noise — its creases are the drainage
        // lines a badlands erodes along.
        var gully = new Noise(seed + 611, frequency: 0.16f, octaves: 3, ridged: true)
            .WithWarp(amplitude: 6f, frequency: 0.05f);
        // Low enough that a tower is a few cells across rather than a needle: one
        // cell is an orchard, and a column an orchard wide is a chimney.
        var towers = new Noise(seed + 733, frequency: 0.18f, octaves: 2);
        var terrace = new Noise(seed + 857, frequency: 0.055f, octaves: 3);

        // How far into a patch the sculpting starts, as a share of its half-width.
        // `inward` is 0 at the border and 1 at the middle.
        const float Rim = 0.18f;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int r = region[x, z];
            RegionPlan rp = plan[r];
            if (!IsSculpted(rp.Type)) continue;

            float u = inward[x, z];
            if (u <= Rim) continue;                     // the rim is the patch's word to its neighbours

            switch (rp.Type)
            {
                // A maze of gullies: wherever the ridged field creases, the ground
                // drops a fixed depth. Fixed, not tapered — a tapering gully has a
                // two-slab step somewhere along its length by construction, and
                // two slabs is the one height the grammar forbids.
                case LandformType.Badlands:
                    if (gully.At(x, z) > 0.62f) continue;
                    h[x, z] = SlabClamp(h[x, z] - (int)MathF.Round(GullyDepth * (0.7f + 0.5f * scale)));
                    carved[x, z] = true;
                    break;

                // Towers: the high ground of a blobby field, raised bodily off the
                // floor. The threshold is high, so what is left is columns of a
                // few cells rather than a plateau with holes in it.
                case LandformType.Karst:
                {
                    float t = towers.At(x, z);
                    if (t < 0.62f) continue;
                    // Taller where the field is stronger, but each tower is one
                    // height throughout: the sides are meant to be sheer.
                    int rise = (int)MathF.Round(TowerRise * (0.6f + 0.9f * scale)
                                                * (0.75f + 0.5f * towers.At(x * 0.13f, z * 0.13f)));
                    h[x, z] = SlabClamp(h[x, z] + Math.Max(4, rise));
                    carved[x, z] = true;
                    break;
                }

                // Concentric terraces. The contour is the patch's own inward
                // distance warped by noise, so the rings follow the shape of the
                // massif and wander in and out of it rather than being circles.
                case LandformType.Massif:
                {
                    float warped = Math.Clamp(u + (terrace.At(x, z) - 0.5f) * 0.34f, 0f, 1f);
                    int rings = 3 + (int)(scale * 2.5f);
                    int ring = (int)(warped * rings);
                    if (ring <= 0) continue;
                    h[x, z] = SlabClamp(h[x, z] + ring * TerraceRiser);
                    carved[x, z] = true;
                    break;
                }

                // Round pits punched out of open ground: the same limestone as the
                // karst, read from above instead of from the side. The threshold is
                // low and the field is smooth, so what drops out is isolated holes
                // rather than the connected maze a badlands makes.
                case LandformType.Sinkholes:
                {
                    if (towers.At(x + 512f, z - 512f) > 0.30f) continue;
                    h[x, z] = SlabClamp(h[x, z] - (int)MathF.Round(
                        SinkDepth * (0.7f + 0.6f * scale)));
                    carved[x, z] = true;
                    break;
                }
            }
        }

        // A one-cell terrace is a ledge, and a one-cell gully is a hole. Anything
        // the fields left isolated is filled back in, which is cheaper than
        // tuning the thresholds to never produce one.
        Despeckle(land, region, h, carved);
        return carved;
    }

    /// <summary>
    /// Undoes any sculpted cell with no sculpted neighbour at its own level. A
    /// lone pit or pillar reads as a mistake at this scale — one cell is an
    /// orchard — and it is also the shape most likely to leave an ambiguous step
    /// behind it.
    /// </summary>
    private static void Despeckle(bool[,] land, int[,] region, short[,] h, bool[,] carved)
    {
        int n = land.GetLength(0);
        var lone = new List<(int X, int Z, int To)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!carved[x, z]) continue;

            int kin = 0, floor = int.MinValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                if (carved[nx, nz] && h[nx, nz] == h[x, z]) kin++;
                else if (!carved[nx, nz]) floor = Math.Max(floor, (int)h[nx, nz]);
            }
            if (kin == 0 && floor != int.MinValue) lone.Add((x, z, floor));
        }

        foreach (var (x, z, to) in lone)
        {
            h[x, z] = SlabClamp(to);
            carved[x, z] = false;
        }
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
                                   bool[,]? exempt = null, bool[,]? saddle = null)
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
                    // A pass is the one place a cliff border is deliberately bound:
                    // the saddle exists precisely so you can walk across it, so the
                    // limiter has to reach over the border there.
                    bool joined = saddle != null && saddle[x, z] && saddle[nx, nz];
                    if (rn != r && !joined && !BorderIsBound(plan[r], plan[rn])) continue;
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
                                              RegionPlan[] plan, short[,]? water = null,
                                              bool[,]? exempt = null)
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
                // A gully floor, a tower top, a canyon bed: cut on purpose, and
                // neither resolved away nor measured against.
                if (exempt != null && exempt[x, z]) continue;

                // A shore may not be lowered into its own lake, and ground beside a
                // basin may not be lowered to within a cliff of the floor it looks
                // down on — an escarpment resolved away is a basin deleted.
                int keepAbove = plan[region[x, z]].Type == LandformType.Basin
                    ? int.MinValue
                    : BasinFloorNear(land, h, region, plan, n, x, z);
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
                    if (exempt != null && exempt[nx, nz]) continue;

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
    /// Cuts a <b>pass</b>: a saddle where one plateau sags down to meet the next,
    /// so a cliff border has exactly one place you can walk across.
    ///
    /// <para>Not a ramp. A ramp was tried and removed (docs §4c): a mesa stands
    /// five or six slabs, a one-slab-per-cell grade covers that in five or six
    /// cells, and five risers in a row against flat open ground is a staircase by
    /// any reading. The failure was the <i>shape</i>, not the grade — a narrow
    /// causeway sticking out into a plain shows every riser in profile.</para>
    ///
    /// <para>A pass is instead a broad radial sag, some fifteen to twenty cells
    /// across, centred on a point of the border. The ground either side of the
    /// path descends with it, so the eye reads a valley rather than a stair, and
    /// the same grade that failed as a causeway works as a col. Its outline is a
    /// noise-wobbled radius, so it is not a disc.</para>
    ///
    /// <para><b>Occasional on purpose.</b> Passes are flavour, not the
    /// connectivity answer — that is infrastructure (see <see cref="Traversal"/>).
    /// Cutting one on every border would flatten the island into a single
    /// walkable district and throw away the plateau ladder. Most islands get
    /// none or one.</para>
    ///
    /// <para>Only rung-ladder cliffs qualify: both sides plain or hills, neither a
    /// mesa, basin or mountain. A mesa with a pass cut into it stops being a
    /// mesa — the landform <i>is</i> "flat top, cliff all round" — and a mesa top
    /// is reachable with a stair anyway.</para>
    /// </summary>
    /// <returns>The cells the saddle touched, or <c>null</c> if no pass was cut.</returns>
    private static bool[,]? CutPasses(int seed, IslandParams p, bool[,] land, int[,] region,
                                      RegionPlan[] plan, short[,] h,
                                      Dictionary<long, List<(int X, int Z)>> borders,
                                      List<Vector2I> sites)
    {
        float roll = Hash01(seed, 0x9E15);
        int want = roll < 0.35f ? 0 : roll < 0.80f ? 1 : 2;
        if (want == 0) return null;

        int n = p.Size;
        int maxDrop = Math.Max(6, p.CliffHeight * 2);

        // Rank the borders that could take one: a real drop, room to sag into, and
        // a pair of patches whose difference is the ladder rather than a landform.
        var options = new List<(float Score, int X, int Z, int Drop)>();

        foreach (var (key, cells) in borders)
        {
            if (cells.Count < 8) continue;
            int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
            if (!LadderPair(plan[a], plan[b])) continue;

            // The cheapest crossing on this border, which is where a pass would
            // form: least ground to move, least scar.
            int bestDrop = int.MaxValue;
            int bx = -1, bz = -1;
            foreach (var (x, z) in cells)
            {
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                    if (region[nx, nz] == region[x, z]) continue;

                    // Three, not two: a two-slab step is not a cliff — the grammar
                    // pass that runs after this one resolves it to a walkable step
                    // anyway, so a pass cut there does nothing but scar the ground.
                    int drop = Math.Abs(h[x, z] - h[nx, nz]);
                    if (drop < 3 || drop > maxDrop || drop >= bestDrop) continue;
                    bestDrop = drop;
                    bx = x;
                    bz = z;
                }
            }
            if (bx < 0) continue;

            float jitter = 0.6f + 0.8f * Hash01(seed, 0x5A11u ^ (uint)key * 2654435761u);
            options.Add((cells.Count * jitter / bestDrop, bx, bz, bestDrop));
        }
        if (options.Count == 0) return null;

        options.Sort((u, v) => v.Score.CompareTo(u.Score));

        var mask = new bool[n, n];
        var wobble = new Noise(seed + 4242, frequency: 1.1f, octaves: 2);
        int cut = 0;

        foreach (var (_, px, pz, drop) in options)
        {
            if (cut >= want) break;

            // Don't stack two passes on top of each other.
            bool tooClose = false;
            foreach (Vector2I had in sites)
                if (Math.Abs(had.X - px) + Math.Abs(had.Y - pz) < 24) { tooClose = true; break; }
            if (tooClose) continue;

            // Radius from the drop, so the grade stays under a slab per cell: the
            // sag has to be longer than it is deep, or it is a staircase again.
            float radius = drop + 4f;
            int floor = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = px + Dx[k], nz = pz + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                if (region[nx, nz] != region[px, pz]) floor = Math.Min(floor, h[nx, nz]);
            }
            if (floor == int.MaxValue) continue;

            int span = (int)MathF.Ceiling(radius) + 2;
            for (int x = Math.Max(0, px - span); x <= Math.Min(n - 1, px + span); x++)
            for (int z = Math.Max(0, pz - span); z <= Math.Min(n - 1, pz + span); z++)
            {
                if (!land[x, z]) continue;
                // A col is cut through the rung ladder, never through a landform
                // that *is* its own height. Sagging a mesa or a basin takes the
                // landform away — and marking one as pass ground is worse still,
                // because the slope limiter is told to reach across a pass border,
                // which then drags the plain down to meet the basin floor it is
                // supposed to look down on.
                if (plan[region[x, z]].Type is LandformType.Mountain
                    or LandformType.Mesa or LandformType.Basin) continue;

                float dx = x - px, dz = z - pz;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist < 0.001f) dist = 0.001f;

                // A wobbled radius, sampled on the unit circle so it is seamless
                // where the angle wraps. A perfect disc reads as a crater.
                float rEff = radius * (0.75f + 0.5f * wobble.At(dx / dist, dz / dist));
                if (dist > rEff) continue;

                float w = 1f - FieldOps.SmoothStep(0f, 1f, dist / rEff);
                int target = (int)MathF.Round(h[x, z] + (floor - h[x, z]) * w);
                // A sag reaching the rim of a basin would sink the ground to meet
                // the floor it is supposed to look down on — the escarpment
                // inverted, which is the same bug a canyon cut beside a basin used
                // to have. The col stops at a cliff's height above the floor.
                if (plan[region[x, z]].Type != LandformType.Basin)
                    target = Math.Max(target, BasinFloorNear(land, h, region, plan, n, x, z));
                if (target < h[x, z]) h[x, z] = SlabClamp(target);
                mask[x, z] = true;
            }

            sites.Add(new Vector2I(px, pz));
            cut++;
        }
        return cut > 0 ? mask : null;
    }

    /// <summary>
    /// Whether a border's drop is the plateau ladder rather than a landform. A
    /// mesa or basin escarpment and a mountain flank are the landform itself, and
    /// notching them would delete it.
    /// </summary>
    private static bool LadderPair(RegionPlan a, RegionPlan b)
    {
        static bool Soft(LandformType t) => t is LandformType.Plain or LandformType.Hills;
        return Soft(a.Type) && Soft(b.Type) && a.RungGroup != b.RungGroup;
    }

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
            TerrainCharacter.Tablelands => new[]
                { ReliefStyle.Plateau, ReliefStyle.CentralPeak, ReliefStyle.Tilted },
            TerrainCharacter.Downs => new[]
                { ReliefStyle.OffsetPeak, ReliefStyle.TwinPeaks, ReliefStyle.Tilted },
            // Badlands and dunes are country, not relief: they want a broad even
            // ground to spread over rather than a peak to climb.
            TerrainCharacter.Badlands => new[] { ReliefStyle.Plateau, ReliefStyle.Tilted },
            TerrainCharacter.Dunes => new[] { ReliefStyle.Tilted, ReliefStyle.Plateau },
            TerrainCharacter.Karst => new[]
                { ReliefStyle.Plateau, ReliefStyle.Tilted, ReliefStyle.OffsetPeak },
            _ => new[] { ReliefStyle.Ridge, ReliefStyle.TwinPeaks, ReliefStyle.OffsetPeak },
        };
        return pool[(int)(Hash(seed, 0x5EED) % (uint)pool.Length)];
    }

    private static ReliefStyle ResolveStyle(int seed, IslandParams p)
        => StyleFor(seed, ResolveCharacter(seed, p));

    /// <summary>
    /// The layouts <c>Auto</c> may roll, and how often. Weighted toward a single
    /// landmass: an archipelago is the interesting case, not the common one.
    ///
    /// The first six are the set the generator was built and audited on; the rest
    /// are the newer shapes, and <see cref="IslandParams.NewArrangements"/> takes
    /// them out of the pool in one move without taking them out of the code — a
    /// layout you can no longer roll is still a layout you can ask for by name in
    /// the lab.
    /// </summary>
    private static readonly (IslandArrangement How, float Weight)[] ArrangementPool =
    {
        (IslandArrangement.Single, 34f),
        (IslandArrangement.Satellites, 10f),
        (IslandArrangement.Twins, 8f),
        (IslandArrangement.Triplets, 6f),
        (IslandArrangement.Archipelago, 6f),
        (IslandArrangement.BrokenRing, 5f),
        // --- newer shapes, gated by NewArrangements -------------------------
        (IslandArrangement.Ring, 4f),
        (IslandArrangement.Arc, 4f),
        (IslandArrangement.BrokenArc, 4f),
        (IslandArrangement.Atoll, 4f),
        (IslandArrangement.ThousandIsles, 4f),
        (IslandArrangement.Cross, 4f),
        (IslandArrangement.Fractal, 4f),
        (IslandArrangement.Shards, 3f),
        (IslandArrangement.TShape, 3f),
        (IslandArrangement.LShape, 3f),
        (IslandArrangement.BrokenCross, 3f),
        (IslandArrangement.BrokenT, 3f),
        (IslandArrangement.BrokenL, 3f),
        (IslandArrangement.BrokenFractal, 3f),
        (IslandArrangement.Rosette, 3f),
        (IslandArrangement.Star, 3f),
    };

    /// <summary>How many of <see cref="ArrangementPool"/> are the audited originals.</summary>
    private const int ClassicArrangements = 6;

    /// <summary>
    /// What <see cref="IslandParams.NewArrangements"/> and
    /// <see cref="IslandParams.NewLandforms"/> actually change, in numbers.
    ///
    /// Both flags gate <c>Auto</c>'s dice and nothing else, which is exactly why
    /// they read in the lab as a checkbox that does nothing: with an arrangement
    /// and a character named by hand there is no dice roll left to gate. These
    /// exist so the lab can say so — see <c>IslandLab.PoolNote</c>.
    /// </summary>
    public static int AutoArrangements(bool newer)
        => newer ? ArrangementPool.Length : ClassicArrangements;

    /// <inheritdoc cref="AutoArrangements"/>
    public static int AutoCharacters(bool newer)
        => newer ? Enum.GetValues<TerrainCharacter>().Length - 1 : ClassicCharacters;

    /// <summary>Whether <c>Auto</c> could only have rolled this layout with the flag on.</summary>
    public static bool IsNewerShape(IslandArrangement how)
    {
        for (int i = 0; i < ClassicArrangements; i++)
            if (ArrangementPool[i].How == how) return false;
        return how != IslandArrangement.Auto;
    }

    /// <inheritdoc cref="IsNewerShape(IslandArrangement)"/>
    public static bool IsNewerShape(TerrainCharacter c)
        => c != TerrainCharacter.Auto && (int)c > ClassicCharacters;

    private static IslandArrangement ResolveArrangement(int seed, IslandParams p)
    {
        if (p.Arrangement != IslandArrangement.Auto) return p.Arrangement;

        int upto = p.NewArrangements ? ArrangementPool.Length : ClassicArrangements;
        float total = 0f;
        for (int i = 0; i < upto; i++) total += ArrangementPool[i].Weight;

        float pick = Hash01(seed, 0x7A1Du) * total;
        for (int i = 0; i < upto; i++)
        {
            pick -= ArrangementPool[i].Weight;
            if (pick <= 0f) return ArrangementPool[i].How;
        }
        return IslandArrangement.Single;
    }

    /// <summary>How many characters are the four the pipeline was first audited on.</summary>
    private const int ClassicCharacters = 4;

    /// <summary>
    /// Which character an island is, with <c>Auto</c> resolved.
    /// <see cref="IslandParams.NewLandforms"/> keeps the sculpted ones out of the
    /// dice without keeping them out of the game — asking for one by name still
    /// builds it.
    /// </summary>
    private static TerrainCharacter ResolveCharacter(int seed, IslandParams p)
    {
        if (p.Character != TerrainCharacter.Auto) return p.Character;
        int upto = p.NewLandforms
            ? Enum.GetValues<TerrainCharacter>().Length - 1      // minus Auto
            : ClassicCharacters;
        return (TerrainCharacter)(1 + (int)(Hash(seed, 0xC7A2) % (uint)upto));
    }

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
