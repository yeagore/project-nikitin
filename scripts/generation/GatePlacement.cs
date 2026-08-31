using System;
using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Places the Domain's Gates: one <see cref="GateRole.Entry"/> the player emerges
/// from, and one to three <see cref="GateRole.Exit"/> Links onward.
///
/// <para><b>Four hanging Gates first, then take away.</b> This is the whole
/// shape of the pass and it is the opposite of what it used to be. Every Domain
/// is given a hanging Gate on each of its four edges — the maximum — and the
/// parameters then <i>reduce</i> that: an Exit the Domain does not need is
/// deleted, a Gate asked to be a <see cref="GateKind.Land"/> one is moved from
/// the end of its flight path down onto its own landing strip, and the Entry is
/// whichever of the four the world-tree says it is.</para>
///
/// <para>Placing each Gate greedily and hoping four would fit was the old way,
/// and it delivered four hanging Gates on a quarter of Domains: each Gate has to
/// out-reach every other on both axes, so the first one placed moved the line the
/// next had to beat, and by the third there was nowhere left. Choosing the four
/// as a <b>set</b> — a small backtracking search over the best sites per edge —
/// makes the maximum the default and everything else a subtraction from it.</para>
///
/// <para><b>Nothing about the coast is allowed to veto a Link.</b> The strip a
/// vessel lands on is <see cref="StripLength"/> cells of ground running inland,
/// one cell wide, and it is <b>levelled</b> once the site is chosen rather than
/// having to be found level — see <see cref="LevelStrips"/>. A Gate is a built
/// structure; so is the ground it lands on.</para>
///
/// <para><b>One Gate per edge, and on that edge.</b> Domains sit on a plane at
/// their world-tree position — a Domain linked north is found by scrolling
/// north — so two Gates facing the same way would be two Links to the same
/// place, and a Gate facing east that is not the easternmost thing on the map is
/// a Link pointing back over the Domain it leaves.</para>
///
/// <para>Runs after <see cref="Traversal"/>, because every rule here is about
/// ground the player can actually use: a Gate on a stranded ledge, or opening
/// onto a cliff face, is not a place to start a run. It levels the strips it
/// chose, so the analysis is run again afterwards.</para>
/// </summary>
internal static class GatePlacement
{
    /// <summary>
    /// Level cells the ground by a Gate offers, as a target rather than a
    /// requirement: it ranks candidate sites, and nothing is refused for missing
    /// it. Roughly an 8×8 yard — the working figure from the spec's
    /// <c>MinSettlementArea</c>.
    /// </summary>
    public const int ApronArea = 60;

    /// <summary>
    /// Cells of landing strip, running inland from the coast under the Gate.
    /// <b>Three</b>, and one cell across — the width of the portal itself, so what
    /// comes through the Gate has ground under all of it and no more. An aethership
    /// sets down on it, so it is a berth for a vessel rather than the footprint of
    /// a doorway, but a berth for a one-block portal and not a runway.
    /// </summary>
    public const int StripLength = 3;

    /// <summary>
    /// Slabs of height a strip may span before levelling it would be vandalism
    /// rather than groundwork.
    ///
    /// It is a <i>ranking</i> threshold and a last-rung limit, not the gate it
    /// used to be: the strip is levelled once chosen, so what this really says is
    /// "do not carve a shelf out of a hillside to land on". A site inside the
    /// tolerance needs at most a slab or two moved.
    /// </summary>
    private const int StripTolerance = 3;

    /// <summary>
    /// How far off the rim a hanging Gate floats, in cells. <b>Ten.</b> Four put
    /// the portal close enough to the coast to read as a doorway standing just
    /// off the step; at ten it hangs in the aether, which is the whole point of
    /// it — you fly to a hanging Gate, and the flight is meant to be visible.
    /// </summary>
    public const int HangingOffset = 10;

    /// <summary>
    /// How much of that offset has to be clear air under the flight path. The
    /// whole run out to the portal is checked against the sill, but only the last
    /// few cells have to be over nothing: a spit of low ground two cells off the
    /// coast is scenery a vessel flies over, and refusing it would put the Gate
    /// rules back to where a ragged coastline could veto a Link.
    /// </summary>
    public const int HangingClearance = 4;

    /// <summary>
    /// How far back from the outermost usable ground on its own side a Gate may
    /// stand, as a share of the island's width in that direction. A south Gate
    /// halfway up the island leaves a third of the Domain behind the player as
    /// they arrive, which is not what "south" means.
    /// </summary>
    private const float EdgeBand = 0.22f;

    /// <summary>
    /// And the widest that band ever gets, once the rules start giving.
    ///
    /// <b>The band widens; it never disappears.</b> Dropping it outright let a
    /// Gate stand anywhere on the island so long as it out-reached the other
    /// Gates, and measured over 60 seeds that put up to 73% of the Domain behind
    /// the player as they arrived — at which point "the south Gate" names nothing.
    /// At 0.45 the Gate is still on its own half of the island.
    /// </summary>
    private const float RelaxedEdgeBand = 0.45f;

    /// <summary>And never less than this many cells, on a small or ragged island.</summary>
    private const int MinEdgeBand = 8;

    /// <summary>
    /// How much of each end of an edge is corner rather than edge, as a share of
    /// the island's width across that edge. A Gate in the corner faces two ways
    /// at once and crowds whichever Gate holds the next edge round.
    /// </summary>
    private const float CornerInset = 0.22f;

    /// <summary>
    /// Cells two Gates must keep between them, as a share of the footprint. They
    /// already sit on separate edges; this is what stops two of them meeting near
    /// the corner where those edges join.
    /// </summary>
    private const float GateSeparation = 0.42f;

    /// <summary>
    /// And the floor under it once the rules have given as far as they give. A
    /// third of the footprint is still a long walk, and it is what a Gate on a
    /// coast that will not take one has to find. Public so the audit can check
    /// the rule that is actually in force rather than a number of its own.
    /// </summary>
    public const float CrowdedSeparation = 0.32f;

    /// <summary>
    /// And the floor under <i>that</i>, on the last rung, where the choice is
    /// between two Gates closer than anyone would like and a Domain short of a
    /// Link. This is the number a Gate may never be inside of, so it is the one
    /// the audit checks.
    /// </summary>
    public const float MinSeparation = CrowdedSeparation * 0.5f;

    /// <summary>
    /// Cells by which a Gate has to out-reach every other Gate in its own
    /// direction. Small — the point is a strict order, not a wide berth.
    /// </summary>
    private const int DominanceMargin = 2;

    /// <summary>
    /// How often a Gate stands on the ground rather than hanging off the rim,
    /// where the seed is choosing. Hanging is the norm: crossing a Link is a
    /// flight, and a Gate you walk through is the local exception.
    /// </summary>
    private const float LandGateShare = 0.25f;

    /// <summary>Candidate sites kept per edge for the set-wise search.</summary>
    private const int CandidatesPerEdge = 16;

    /// <summary>
    /// How far the rules may be bent to get four Gates placed.
    ///
    /// <b>Four hanging Gates is the invariant.</b> The rungs give one thing at a
    /// time and in a fixed order, worst-founded first; the last of them gives up
    /// even the dominance rule, because a Domain with three Links out and one Gate
    /// facing awkwardly is a better Domain than one with three Links.
    /// </summary>
    private enum Ease
    {
        /// <summary>Every rule: the band, the corners, the separation, the order.</summary>
        Full = 0,

        /// <summary>The edge band widens to <see cref="RelaxedEdgeBand"/>.</summary>
        Band = 1,

        /// <summary>The corners go too: a coast may only offer its ends.</summary>
        Anywhere = 2,

        /// <summary>Separation falls to <see cref="CrowdedSeparation"/>.</summary>
        Crowded = 3,

        /// <summary>
        /// Separation halves again and the dominance order goes. Reached only on a
        /// coast that would otherwise refuse a Link outright.
        /// </summary>
        Desperate = 4,
    }

    /// <summary>One place a Gate could go, before it is known what kind it is.</summary>
    private readonly record struct Site(Cardinal Edge, int X, int Z, short Level,
                                        int Apron, float Score)
    {
        public Vector2I Head => new(X, Z);
    }

    /// <summary>
    /// Places the Domain's Gates, and levels the ground they landed on.
    ///
    /// Returns whether the terrain was changed, so the caller knows to run the
    /// traversal analysis again — <see cref="LevelStrips"/> moves slabs, and every
    /// number the analysis produced was measured before it did.
    /// </summary>
    public static bool Place(int seed, IslandParams p, IslandData d)
    {
        d.Gates.Clear();

        // ---- 1. four sites, chosen as a set ------------------------------------
        Site[] chosen = ChooseSites(seed, d);

        // ---- 2. which of them is the Entry -------------------------------------
        // The world-tree's decision where it made one; otherwise the seed's. If the
        // named edge got no site at all, the Entry falls to whichever edge did —
        // a Domain with no way in would be worse than one entered from the side.
        int entry = -1;
        if (p.EntryEdge != GateEdge.Auto)
        {
            var want = (Cardinal)((int)p.EntryEdge - 1);
            for (int i = 0; i < chosen.Length; i++)
                if (chosen[i].Edge == want && chosen[i].Level != IslandData.NoLand) entry = i;
        }
        if (entry < 0)
        {
            int first = (int)(Hash01(seed, 0x3D1Fu) * chosen.Length);
            for (int k = 0; k < chosen.Length && entry < 0; k++)
            {
                int i = (first + k) % chosen.Length;
                if (chosen[i].Level != IslandData.NoLand) entry = i;
            }
        }
        if (entry < 0) return false;                      // no coast at all: nothing to do

        // ---- 3. how many Exits are wanted, and which sites keep them -----------
        int exits = p.ExitGates > 0
            ? Math.Clamp(p.ExitGates, 1, 3)
            : 1 + (int)(Hash01(seed, 0x6A7Eu) * 3f);

        // Take away, best first: an Exit the Domain does not need is deleted, and
        // the ones deleted are the worst-founded of the four.
        var order = new List<int>();
        for (int i = 0; i < chosen.Length; i++)
            if (i != entry && chosen[i].Level != IslandData.NoLand) order.Add(i);
        order.Sort((a, b) => chosen[b].Score.CompareTo(chosen[a].Score));
        if (order.Count > exits) order.RemoveRange(exits, order.Count - exits);

        // ---- 4. and what kind each one is --------------------------------------
        GateKind entryKind = p.EntryGate != GateKind.Auto
            ? p.EntryGate
            : Hash01(seed, 0xE47Eu) < LandGateShare ? GateKind.Land : GateKind.Hanging;

        d.Gates.Add(Build(chosen[entry], GateRole.Entry, entryKind));
        foreach (int i in order)
        {
            GateKind kind = p.ExitGate != GateKind.Auto
                ? p.ExitGate
                : Hash01(seed, 0x91C0u ^ (uint)chosen[i].Edge * 2654435761u) < LandGateShare
                    ? GateKind.Land
                    : GateKind.Hanging;
            d.Gates.Add(Build(chosen[i], GateRole.Exit, kind));
        }

        // ---- 5. build the ground they stand on ---------------------------------
        bool moved = LevelStrips(d);
        MarkLandings(d);
        return moved;
    }

    /// <summary>
    /// A Gate from a site. <b>A land Gate is the same site with the portal moved
    /// down onto its own landing strip</b> — the flight path becomes a doorway,
    /// and the ground a vessel would have set down on is the ground you walk out
    /// onto. That is the whole difference between the two kinds, which is why
    /// there is one site search rather than two.
    /// </summary>
    private static Gate Build(Site site, GateRole role, GateKind kind)
    {
        var probe = new Gate(kind, role, site.Edge, default, default, 0);
        Vector2I outward = probe.Outward;
        Vector2I apron = site.Head - outward * (StripLength - 1);

        Vector3I centre = kind == GateKind.Land
            ? new Vector3I(site.X, site.Level, site.Z)
            : new Vector3I(site.X + outward.X * HangingOffset, site.Level + 2,
                           site.Z + outward.Y * HangingOffset);

        return new Gate(kind, role, site.Edge, centre, apron, site.Apron, StripLength);
    }

    // ---- choosing the four -------------------------------------------------

    /// <summary>
    /// One site per edge, chosen together rather than one after another.
    ///
    /// <para>Each edge offers its best <see cref="CandidatesPerEdge"/> sites in
    /// score order, and a small backtracking search takes the first combination
    /// where every pair is far enough apart and in the right order. That is the
    /// fix for the old greedy pass: a Gate that takes the far corner of its own
    /// side does not merely sit oddly, it moves the line the next Gate round has
    /// to beat, and four placed one at a time paint themselves into a corner.</para>
    ///
    /// <para>The rungs are walked until a combination exists. An edge with no
    /// candidate at all comes back empty — a Domain whose heartland simply has no
    /// north-facing coast cannot have a north Gate, and no rule can conjure one.</para>
    /// </summary>
    private static Site[] ChooseSites(int seed, IslandData d)
    {
        var edges = Enum.GetValues<Cardinal>();
        var best = new Site[edges.Length];
        for (int i = 0; i < best.Length; i++)
            best[i] = new Site(edges[i], -1, -1, IslandData.NoLand, 0, 0f);

        Frame bounds = Bounds(d);

        foreach (Ease ease in Enum.GetValues<Ease>())
        {
            var pool = new List<Site>[edges.Length];
            for (int i = 0; i < edges.Length; i++)
                pool[i] = Candidates(seed, d, bounds, edges[i], ease);

            var picked = new Site[edges.Length];
            int budget = 20000;
            if (!Assign(d, pool, picked, 0, ease, ref budget)) continue;

            // Keep the fullest answer any rung produced: a later rung is looser,
            // so it can only ever offer more Gates, but it offers worse-founded
            // ones — and there is no point taking those if the strict rung already
            // filled every edge.
            int had = 0, got = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                if (best[i].Level != IslandData.NoLand) had++;
                if (picked[i].Level != IslandData.NoLand) got++;
            }
            if (got > had) best = picked;
            if (got == edges.Length) break;
        }
        return best;
    }

    /// <summary>
    /// Depth-first over the edges, taking each edge's candidates in score order
    /// and keeping any that agree with what is already chosen. An edge with no
    /// workable candidate is left empty rather than failing the whole assignment,
    /// so three good Gates beat four impossible ones.
    /// </summary>
    private static bool Assign(IslandData d, List<Site>[] pool, Site[] picked, int edge,
                               Ease ease, ref int budget)
    {
        if (edge == pool.Length) return true;
        if (budget-- <= 0) return false;

        foreach (Site site in pool[edge])
        {
            bool ok = true;
            for (int i = 0; i < edge && ok; i++)
            {
                if (picked[i].Level == IslandData.NoLand) continue;
                ok = Compatible(picked[i], site, ease, d);
            }
            if (!ok) continue;

            picked[edge] = site;
            if (Assign(d, pool, picked, edge + 1, ease, ref budget)) return true;
        }

        // Nothing on this edge fits. Leave it empty and carry on: the Domain loses
        // one Link, not all of them.
        picked[edge] = new Site(pool[edge].Count > 0 ? pool[edge][0].Edge : default,
                                -1, -1, IslandData.NoLand, 0, 0f);
        return Assign(d, pool, picked, edge + 1, ease, ref budget);
    }

    /// <summary>
    /// Whether two chosen sites can both be Gates: far enough apart on the ground,
    /// and each the outermost of the two in its own direction.
    ///
    /// Distance is measured between the two <b>ground</b> positions. A hanging
    /// Gate's portal is ten cells out in the aether, and comparing that to a coast
    /// cell measures partly along a flight path.
    /// </summary>
    private static bool Compatible(Site a, Site b, Ease ease, IslandData d)
    {
        float share = ease < Ease.Crowded ? GateSeparation
                    : ease < Ease.Desperate ? CrowdedSeparation
                    : MinSeparation;
        int apart = (int)(share * d.Size);
        if (Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z) < apart) return false;

        if (ease >= Ease.Desperate) return true;         // the order is the last to give

        Vector2I da = Outward(a.Edge), db = Outward(b.Edge);
        int aOnA = a.X * da.X + a.Z * da.Y, bOnA = b.X * da.X + b.Z * da.Y;
        int bOnB = b.X * db.X + b.Z * db.Y, aOnB = a.X * db.X + a.Z * db.Y;
        return aOnA >= bOnA + DominanceMargin && bOnB >= aOnB + DominanceMargin;
    }

    /// <summary>Outward normal of an edge, without needing a whole Gate.</summary>
    private static Vector2I Outward(Cardinal edge) => edge switch
    {
        Cardinal.North => new Vector2I(0, -1),
        Cardinal.East => new Vector2I(1, 0),
        Cardinal.South => new Vector2I(0, 1),
        _ => new Vector2I(-1, 0),
    };

    /// <summary>
    /// Every place on one edge a hanging Gate could go, best first.
    ///
    /// A site is a coast cell with aether directly outward of it, a strip of
    /// usable ground running inland behind it, and a flight path in. It does
    /// <i>not</i> have to be level: the strip is levelled once chosen.
    /// </summary>
    private static List<Site> Candidates(int seed, IslandData d, Frame bounds, Cardinal edge,
                                         Ease ease)
    {
        int n = d.Size;
        Vector2I outward = Outward(edge);
        var across = new Vector2I(-outward.Y, outward.X);

        var found = new List<Site>();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Usable(d, x, z)) continue;
            if (!OnEdge(d, bounds, x, z, outward, across, ease)) continue;
            if (!HasStrip(d, x, z, outward, ease)) continue;

            short level = StripTop(d, x, z, outward);
            if (!Flyable(d, x, z, outward, level)) continue;

            found.Add(new Site(edge, x, z, level, ApronAt(d, x, z),
                               Score(seed, bounds, d, x, z, outward, across)));
        }

        found.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (found.Count > CandidatesPerEdge) found.RemoveRange(CandidatesPerEdge,
                                                               found.Count - CandidatesPerEdge);
        return found;
    }

    /// <summary>
    /// The two rules that are about one Gate on its own: near its own edge, and
    /// clear of the corners. Everything about a Gate's relationship to the *other*
    /// Gates is in <see cref="Compatible"/>.
    /// </summary>
    private static bool OnEdge(IslandData d, Frame bounds, int x, int z,
                               Vector2I outward, Vector2I across, Ease ease)
    {
        if (!bounds.Any) return true;

        int along = x * outward.X + z * outward.Y;
        float share = ease < Ease.Band ? EdgeBand : RelaxedEdgeBand;
        int floor = ease < Ease.Band ? MinEdgeBand : MinEdgeBand * 2;
        int band = Math.Max(floor, (int)(share * bounds.Extent(outward)));
        if (along < bounds.Extreme(outward) - band) return false;

        if (ease < Ease.Anywhere)
        {
            int side = x * across.X + z * across.Y;
            int span = bounds.Extent(across);
            int inset = (int)(CornerInset * span);
            int high = bounds.Extreme(across);
            if (side > high - inset || side < high - span + inset) return false;
        }
        return true;
    }

    /// <summary>
    /// Ground for a landing strip: <see cref="StripLength"/> usable cells running
    /// inland from a coast cell, one wide.
    ///
    /// <b>It does not have to be level, and it is never short.</b> The old test
    /// asked for fifteen cells that already agreed with each other to within three
    /// slabs, which is why a Domain could offer four or five sites in total and
    /// four hanging Gates were a coincidence. What it asks now is that the ground
    /// exists and is not a hillside; <see cref="LevelStrips"/> makes it flat.
    /// </summary>
    private static bool HasStrip(IslandData d, int x, int z, Vector2I outward, Ease ease)
    {
        int n = d.Size;
        if (!Usable(d, x, z)) return false;

        // The head of the strip: land with aether directly outward of it.
        int hx = x + outward.X, hz = z + outward.Y;
        if (hx >= 0 && hz >= 0 && hx < n && hz < n && d.HasLand(hx, hz)) return false;

        short lowest = short.MaxValue, highest = short.MinValue;
        for (int along = 0; along < StripLength; along++)
        {
            int sx = x - outward.X * along, sz = z - outward.Y * along;
            if (!Usable(d, sx, sz)) return false;

            short here = d.SurfaceLevel(sx, sz);
            lowest = Math.Min(lowest, here);
            highest = Math.Max(highest, here);
        }

        // Levelling a strip means moving a slab or two, not quarrying a shelf out
        // of a slope. The allowance doubles on the last rungs, where the choice is
        // between a dug-out berth and no Link at all.
        int tolerance = ease < Ease.Crowded ? StripTolerance : StripTolerance * 2;
        return highest - lowest <= tolerance;
    }

    /// <summary>
    /// The level a strip will be levelled to: the ground at its <b>inner</b> end.
    ///
    /// The inner cell is the one that joins the rest of the island, so levelling to
    /// it leaves that join exactly as the terrain made it and moves only the cells
    /// running out toward the rim — where the outward neighbour is aether and
    /// nothing can be stepped off onto.
    /// </summary>
    private static short StripTop(IslandData d, int x, int z, Vector2I outward)
    {
        int ix = x - outward.X * (StripLength - 1), iz = z - outward.Y * (StripLength - 1);
        return Usable(d, ix, iz) ? d.SurfaceLevel(ix, iz) : d.SurfaceLevel(x, z);
    }

    /// <summary>
    /// Whether a vessel could actually fly in to that strip, and whether the
    /// portal itself would hang clear.
    ///
    /// The approach is tested against the <i>sill height</i>, not against land as
    /// such: a vessel comes in two slabs above the strip, so a low spit under the
    /// flight path is scenery and only ground that reaches the sill is an
    /// obstruction. The portal is one cell wide now, so the corridor is one cell
    /// wide with a cell of margin either side.
    /// </summary>
    private static bool Flyable(IslandData d, int x, int z, Vector2I outward, short level)
    {
        int n = d.Size;
        int sill = level + 2;
        var across = new Vector2I(-outward.Y, outward.X);

        for (int step = 1; step <= HangingOffset; step++)
        for (int side = -1; side <= 1; side++)
        {
            int gx = x + outward.X * step + across.X * side;
            int gz = z + outward.Y * step + across.Y * side;
            if (gx < 0 || gz < 0 || gx >= n || gz >= n || !d.HasLand(gx, gz)) continue;
            if (d.SurfaceLevel(gx, gz) >= sill) return false;
        }

        // The portal itself hangs clear, and so does the air immediately behind it.
        for (int step = HangingOffset - HangingClearance + 1; step <= HangingOffset; step++)
        {
            int gx = x + outward.X * step, gz = z + outward.Y * step;
            if (gx >= 0 && gz >= 0 && gx < n && gz < n && d.HasLand(gx, gz)) return false;
        }
        return true;
    }

    // ---- building the ground -----------------------------------------------

    /// <summary>
    /// Flattens every Gate's landing strip, and returns whether anything moved.
    ///
    /// <para><b>A strip is built, not found.</b> Requiring a coast to arrive
    /// already flat over the length of a berth is what made hanging Gates scarce,
    /// and it is the wrong requirement in the first place: a Gate is a built
    /// structure and so is the ground under it. Three cells are set to the level of
    /// the innermost one — the end that joins the island — so the walk off the
    /// strip is exactly what the terrain made it, and only the cells running out
    /// toward the rim move.</para>
    ///
    /// <para>This is the one place the Gate pass touches terrain, which is why
    /// <see cref="Place"/> reports it: every number <see cref="Traversal"/>
    /// produced was measured before these slabs moved.</para>
    /// </summary>
    private static bool LevelStrips(IslandData d)
    {
        bool moved = false;
        foreach (Gate g in d.Gates)
        {
            Vector2I outward = g.Outward;
            Vector2I head = g.Kind == GateKind.Hanging
                ? new Vector2I(g.Center.X, g.Center.Z) - outward * HangingOffset
                : new Vector2I(g.Center.X, g.Center.Z);

            short level = StripTop(d, head.X, head.Y, outward);
            if (level == IslandData.NoLand) continue;

            for (int along = 0; along < StripLength; along++)
            {
                Vector2I cell = head - outward * along;
                moved |= SetSurface(d, cell.X, cell.Y, level);
            }
        }
        return moved;
    }

    /// <summary>
    /// Raises or lowers one column's surface to a given slab, by moving the top of
    /// its lowest span. Returns whether anything changed.
    ///
    /// It refuses to cut below the keel — a column has to stay solid — and it
    /// refuses to raise into a second span, though at this point in the pipeline
    /// there are none: overhangs are carved after the Gates.
    /// </summary>
    private static bool SetSurface(IslandData d, int x, int z, short level)
    {
        if (x < 0 || z < 0 || x >= d.Size || z >= d.Size) return false;
        Span[] spans = d.Spans[x, z];
        if (spans == null || spans.Length == 0) return false;

        Span low = spans[0];
        if (low.Top == level) return false;
        if (level <= low.Bottom) return false;
        if (spans.Length > 1 && level >= spans[1].Bottom - 1) return false;

        spans[0] = low with { Top = level };
        return true;
    }

    /// <summary>
    /// The ground the Domain's Gates are served by: the strip a hanging Gate's
    /// vessel sets down on, and the same cells under a land Gate, which stands on
    /// its own strip.
    /// </summary>
    private static void MarkLandings(IslandData d)
    {
        int n = d.Size;
        foreach (Gate g in d.Gates)
        {
            Vector2I outward = g.Outward;
            Vector2I head = g.Kind == GateKind.Hanging
                ? new Vector2I(g.Center.X, g.Center.Z) - outward * HangingOffset
                : new Vector2I(g.Center.X, g.Center.Z);

            for (int along = 0; along < StripLength; along++)
            {
                Vector2I cell = head - outward * along;
                if (cell.X < 0 || cell.Y < 0 || cell.X >= n || cell.Y >= n) continue;
                if (!d.HasLand(cell.X, cell.Y)) continue;
                d.Landings[cell.X, cell.Y] = true;
            }
        }
    }

    // ---- the supporting cast -----------------------------------------------

    /// <summary>The bounds of the ground a Gate may use, for the edge rules.</summary>
    private readonly struct Frame
    {
        public readonly int MinX, MaxX, MinZ, MaxZ;
        public readonly bool Any;

        public Frame(int minX, int maxX, int minZ, int maxZ, bool any)
        {
            MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ; Any = any;
        }

        /// <summary>The outermost usable cell in a direction, as a dot product.</summary>
        public int Extreme(Vector2I dir)
            => dir.X > 0 ? MaxX : dir.X < 0 ? -MinX : dir.Y > 0 ? MaxZ : -MinZ;

        /// <summary>How wide the usable ground is along a direction, in cells.</summary>
        public int Extent(Vector2I dir)
            => dir.X != 0 ? MaxX - MinX : MaxZ - MinZ;
    }

    /// <summary>Bounding box of the ground a Gate may use.</summary>
    private static Frame Bounds(IslandData d)
    {
        int n = d.Size;
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Usable(d, x, z)) continue;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }
        return maxX < minX ? new Frame(0, n - 1, 0, n - 1, false)
            : new Frame(minX, maxX, minZ, maxZ, true);
    }

    /// <summary>
    /// How good a site is, once it is allowed at all: as far out on its own side
    /// as possible, near the middle of that side, with the apron as a tie-break
    /// and a little noise so two equal coasts do not always resolve the same way.
    ///
    /// A flat strip is preferred to one that has to be dug, but only as a
    /// tie-break — it is groundwork either way.
    /// </summary>
    private static float Score(int seed, Frame bounds, IslandData d, int x, int z,
                               Vector2I outward, Vector2I across)
    {
        int along = x * outward.X + z * outward.Y;
        int side = x * across.X + z * across.Y;
        float middle = bounds.Extreme(across) - bounds.Extent(across) * 0.5f;

        short lowest = short.MaxValue, highest = short.MinValue;
        for (int i = 0; i < StripLength; i++)
        {
            int sx = x - outward.X * i, sz = z - outward.Y * i;
            if (!Usable(d, sx, sz)) continue;
            short here = d.SurfaceLevel(sx, sz);
            lowest = Math.Min(lowest, here);
            highest = Math.Max(highest, here);
        }
        int roughness = highest > lowest ? highest - lowest : 0;

        return along
               - MathF.Abs(side - middle) * 0.35f
               - roughness * 1.5f
               + ApronAt(d, x, z) * 0.01f
               + Hash01(seed, 0x2200u ^ (uint)(x * 733 + z)) * 0.5f;
    }

    /// <summary>
    /// Level, usable ground at a point: the yard a company would build on. Read
    /// off <see cref="IslandData.ShelfId"/> rather than flooded per candidate —
    /// flooding was the same answer computed tens of thousands of times an island.
    /// </summary>
    private static int ApronAt(IslandData d, int x, int z)
    {
        // <b>Near the Gate, not under it.</b> A Gate stands on a coast cell, and a
        // coast cell is almost never itself part of a shelf — so reading the shelf
        // at the portal returned 0 for nearly every Gate and the score stopped
        // preferring somewhere to start a company at all. What matters is what the
        // player can walk to when they step off the strip, so the search runs a few
        // cells inland from the landing and takes the best shelf it finds.
        int best = 0;
        for (int dx = -ApronSearch; dx <= ApronSearch; dx++)
        for (int dz = -ApronSearch; dz <= ApronSearch; dz++)
        {
            int ax = x + dx, az = z + dz;
            if (ax < 0 || az < 0 || ax >= d.Size || az >= d.Size) continue;

            int id = d.ShelfId[ax, az];
            if (id < 0 || id >= d.Shelves.Count) continue;

            Shelf shelf = d.Shelves[id];
            // Wide as well as big: a company cannot work a ledge, however long.
            if (shelf.Width >= Traversal.MinShelfWidth) best = Math.Max(best, shelf.Area);
        }
        return best;
    }

    /// <summary>How far from a Gate's landing to look for ground worth building on.</summary>
    private const int ApronSearch = 4;

    /// <summary>
    /// Ground a Gate may be built on or served by: dry, and part of the heartland
    /// — everything a player can get to once they have built stairs and bridges.
    /// A Gate opening onto a stranded ledge would strand the run with it.
    /// </summary>
    private static bool Usable(IslandData d, int x, int z)
    {
        int n = d.Size;
        if (x < 0 || z < 0 || x >= n || z >= n) return false;
        if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) return false;
        return d.Heartland >= 0 && d.Reach[x, z] == d.Heartland;
    }

    /// <summary>
    /// Why a coast will or will not take a hanging Gate, counted stage by stage —
    /// the funnel from "usable ground" down to "a Gate could stand here".
    ///
    /// <paramref name="loose"/> counts at the bottom rung of the ladder instead of
    /// the top. The Gates already on the Domain do <b>not</b> count against it:
    /// sites are now chosen as a set, so "where could the next Gate go" is not a
    /// question the pass ever asks.
    /// </summary>
    public static (int Usable, int Fits, int Strip, int Flyable) Funnel(
        IslandData d, Cardinal edge, bool loose = false)
    {
        int n = d.Size;
        Frame bounds = Bounds(d);
        Vector2I outward = Outward(edge);
        var across = new Vector2I(-outward.Y, outward.X);
        Ease ease = loose ? Ease.Desperate : Ease.Full;

        int usable = 0, fits = 0, strip = 0, flyable = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Usable(d, x, z)) continue;
            usable++;
            if (!OnEdge(d, bounds, x, z, outward, across, ease)) continue;
            fits++;
            if (!HasStrip(d, x, z, outward, ease)) continue;
            strip++;
            if (!Flyable(d, x, z, outward, StripTop(d, x, z, outward))) continue;
            flyable++;
        }
        return (usable, fits, strip, flyable);
    }

    private static float Hash01(int seed, uint salt)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u ^ salt;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
