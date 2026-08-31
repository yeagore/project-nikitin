using System;
using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Places the Domain's Gates: one <see cref="GateRole.Entry"/> the player emerges
/// from, and one to three <see cref="GateRole.Exit"/> Links onward.
///
/// <para><b>One Gate per edge, and on that edge.</b> Domains sit on a plane at
/// their world-tree position — a Domain linked north is found by scrolling
/// north — so two Gates facing the same way would be two Links to the same
/// place, and a Gate facing east that is not the easternmost thing on the map is
/// a Link pointing back over the Domain it leaves. Hence the three rules in
/// <see cref="Fits"/>: near its own edge, clear of the corners, and further out
/// in its own direction than every other Gate.</para>
///
/// <para><b>The Entry's kind is an input, not a choice.</b> A Link joins two
/// Gates, so the Gate you arrive at has to match the one you left: land to land,
/// hanging to hanging. That is why <see cref="IslandParams.EntryGate"/> exists —
/// the Domain that sent you sets it, and this Domain grows around it. Left to
/// itself the Domain hangs its Gates: flying through is the normal way to cross
/// a Link, and a Gate you walk through is the exception.</para>
///
/// <para>Runs after <see cref="Traversal"/>, because every rule here is about
/// ground the player can actually use: a Gate on a stranded ledge, or opening
/// onto a cliff face, is not a place to start a run.</para>
/// </summary>
internal static class GatePlacement
{
    /// <summary>
    /// Level cells the ground by a Gate must offer before a company can start
    /// there. Roughly an 8×8 yard — the working figure from the spec's
    /// <c>MinSettlementArea</c>.
    /// </summary>
    public const int ApronArea = 60;

    /// <summary>
    /// Cells of landing strip, running inland from the coast directly opposite
    /// the Gate. Five long and <see cref="StripWidth"/> across: an aethership sets
    /// down on it, so it is a berth for a vessel rather than the footprint of a
    /// doorway.
    /// </summary>
    public const int StripLength = 5;

    /// <summary>
    /// And how wide. Three — the width of the portal itself, so what comes
    /// through the Gate has ground under all of it.
    /// </summary>
    public const int StripWidth = 3;

    /// <summary>
    /// Slabs of height the strip may span end to end. Three, not two: a coast that
    /// steps down onto a beach spends a slab of the allowance before the terrain
    /// has said anything, and at two that alone was enough to stop most beached
    /// coasts hosting a hanging Gate. Three still refuses a hillside — a Shelf
    /// would too — and it is measured across the whole strip, so the ground under
    /// a vessel is even whichever cell it touches down on.
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
    /// How far in front of a land Gate has to be clear of higher ground. A Gate
    /// facing a wall four cells away opens onto nothing.
    /// </summary>
    public const int Approach = 10;

    /// <summary>
    /// Cells of apron a land Gate has to stand on: <see cref="StripWidth"/> across
    /// by this many running <i>inland</i>, the portal's own row included, level
    /// within the free step.
    ///
    /// <b>A land Gate needs a forecourt for the same reason a hanging one needs a
    /// strip.</b> A hanging Gate has owed the Domain a 3 × 5 landing since it
    /// existed; a land Gate owed it nothing but three level cells to stand on, so
    /// one could be planted on a three-cell ledge with a cliff a step behind it —
    /// the ground a shelf-area apron of sixty cells was meant to guarantee, and
    /// does not, because a shelf is measured somewhere on the island rather than
    /// here. Three by three is the smallest thing you can walk out onto and turn
    /// round in.
    /// </summary>
    public const int LandApron = 3;

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
    ///
    /// <b>Raised from 0.16.</b> At a sixth of the edge, "the middle two thirds"
    /// still reached close enough to a corner that a north Gate and an east Gate
    /// could end up looking at each other across a headland — which is what two
    /// Links out of one bay reads as.
    /// </summary>
    private const float CornerInset = 0.22f;

    /// <summary>
    /// Cells two Gates must keep between them. They already sit on separate edges;
    /// this is what stops two of them meeting near the corner where those edges
    /// join, as a share of the footprint.
    ///
    /// <b>Raised from 0.30.</b> Measured as a Manhattan distance, 0.30 of a 128²
    /// Domain is 38 cells — which two Gates on adjacent edges reach by sitting
    /// 19 cells either side of the corner they share, and 19 cells is not a
    /// separate part of the island.
    /// </summary>
    private const float GateSeparation = 0.42f;

    /// <summary>
    /// And the floor under it once the rules have given as far as they give.
    ///
    /// <b>This is the rule that was actually letting Gates crowd.</b> The last
    /// tier used to drop the separation to four cells — the width of the portal
    /// and one — which is not a relaxation of "keep your distance" but a repeal of
    /// it. A third of the footprint is still a long walk, and it is what a Gate
    /// on a coast that will not take one has to find. It is public so the audit can
    /// check the rule that is actually in force rather than a number of its own.
    /// </summary>
    public const float CrowdedSeparation = 0.32f;

    /// <summary>
    /// Cells by which a Gate has to out-reach every other Gate in its own
    /// direction. Small — the point is a strict order, not a wide berth.
    /// </summary>
    private const int DominanceMargin = 2;

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

    /// <summary>
    /// How far the rules may be bent to get a Gate placed at all.
    ///
    /// <b>A Domain must have a way in and a way out.</b> That is not negotiable —
    /// a Domain with no Exit is a leaf of a tree that is supposed to be the whole
    /// map — so when a coast will not take a Gate under the full rules, the rules
    /// give one at a time and in a fixed order, worst-founded first. What never
    /// gives is <see cref="Dominant"/>: a Gate that is not the outermost thing on
    /// its own axis points back over the Domain it leaves, which would be a Link
    /// to the wrong place rather than a Link in an awkward place.
    /// </summary>
    private enum Ease
    {
        /// <summary>Every rule: the band, the corners, the separation, the order.</summary>
        Full = 0,

        /// <summary>
        /// The edge band <b>widens</b> to <see cref="RelaxedEdgeBand"/> — the Gate
        /// may sit further back from the outermost ground on its own side, but is
        /// still on that side. The corners do <b>not</b> go with it.
        ///
        /// These used to be one step, and relaxing "not far enough out" therefore
        /// also repealed "not in a corner", which are different complaints: the
        /// first is about how much Domain is behind you as you arrive and the
        /// second is about two Links sharing a headland. A ragged coast usually
        /// only needs the first.
        /// </summary>
        Band = 1,

        /// <summary>The corners go too: a coast may only offer its ends.</summary>
        Anywhere = 2,

        /// <summary>
        /// The separation from other Gates falls to <see cref="CrowdedSeparation"/>.
        /// Still outermost on its own axis, and still a quarter of the map from
        /// the next Gate.
        /// </summary>
        Crowded = 3,
    }

    public static void Place(int seed, IslandParams p, IslandData d)
    {
        // Hanging by default, both for the Entry the seed is free to choose and
        // for every Exit: crossing a Link is a flight, and a Gate you walk through
        // is the local exception a coast happens to allow.
        GateKind entryKind = p.EntryGate != GateKind.Auto
            ? p.EntryGate
            : (Hash01(seed, 0xE47Eu) < LandGateShare ? GateKind.Land : GateKind.Hanging);

        int exits = p.ExitGates > 0
            ? Math.Clamp(p.ExitGates, 1, 3)
            : 1 + (int)(Hash01(seed, 0x6A7Eu) * 3f);

        // The order the edges are tried in, rotated per seed so the entry is not
        // always north — unless the Domain that sent the player named an edge, in
        // which case it goes first and the rest are only a fallback.
        int first = (int)(Hash01(seed, 0x3D1Fu) * 4f) & 3;
        var edges = new List<Cardinal>();
        for (int i = 0; i < 4; i++) edges.Add((Cardinal)((first + i) & 3));

        Frame bounds = Bounds(d);
        var taken = new HashSet<Cardinal>();

        // What a Gate has to find, from comfortable down to bare possibility. Room
        // to start a company, then room to land, then somewhere on the coast at
        // all. Each step gives one thing: the ladder is walked in order, so what
        // an awkward coast costs is legible in which rung the Gate came off.
        (int Apron, int Strip, Ease Ease)[] tiers =
        {
            (ApronArea, StripLength, Ease.Full),
            (ApronArea / 2, StripLength, Ease.Full),
            (ApronArea / 2, StripLength - 1, Ease.Full),
            (ApronArea / 2, StripLength - 1, Ease.Band),
            (ApronArea / 2, StripLength - 2, Ease.Anywhere),
            (Gate.Width * Gate.Width, 2, Ease.Anywhere),
            (Gate.Width * Gate.Width, 1, Ease.Crowded),
        };

        // <b>A named kind is held; a rolled one is not.</b> Where the caller passes
        // Auto the kind is this Domain's own dice — the coast refusing a hanging
        // Gate is simply how a land Gate comes to exist, so the other kind is tried
        // on the spot. Where the caller names a kind it is a request, and a request
        // is held across every rung of the ladder rather than traded at the first
        // one that says no; Unmet is what reports it if the coast never obliges.
        bool PlaceGate(GateRole role, IEnumerable<Cardinal> pool, GateKind kind)
        {
            bool rolled = kind == GateKind.Auto;

            foreach ((int apron, int strip, Ease ease) in tiers)
            {
                foreach (Cardinal edge in pool)
                {
                    if (taken.Contains(edge)) continue;
                    GateKind want = !rolled
                        ? kind
                        : Hash01(seed, 0x91C0u ^ (uint)edge * 2654435761u) < LandGateShare
                            ? GateKind.Land
                            : GateKind.Hanging;
                    GateKind fallback = want == GateKind.Land ? GateKind.Hanging : GateKind.Land;

                    if (!TryPlace(seed, d, bounds, edge, want, role, out Gate gate,
                                  apron, strip, ease)
                        && !(rolled && TryPlace(seed, d, bounds, edge, fallback, role, out gate,
                                                apron, strip, ease)))
                        continue;
                    d.Gates.Add(gate);
                    taken.Add(edge);
                    return true;
                }
            }
            return false;
        }

        // ---- the Entry ---------------------------------------------------------
        // Both its kind and its edge are the *sending* Domain's decision, so both
        // are held across the whole tier ladder before either is given up — and
        // giving either up is now reported to IslandGenerator.Unmet, which re-rolls
        // the seed rather than quietly handing back a Gate that is not the one
        // asked for. That is the fix: the fallbacks below are the fourth attempt's
        // last resort, not the first attempt's shrug.
        //
        // The kind is held longer than the edge. A Link joins two Gates of the same
        // kind, so an Entry of the wrong kind is a Domain you cannot actually
        // arrive at; an Entry on the wrong edge is a Domain you arrive at from an
        // odd side, which is worse geometry and still a Link.
        GateKind otherKind = entryKind == GateKind.Land ? GateKind.Hanging : GateKind.Land;
        if (p.EntryEdge != GateEdge.Auto)
        {
            var only = new[] { (Cardinal)((int)p.EntryEdge - 1) };
            _ = PlaceGate(GateRole.Entry, only, entryKind)               // as asked
             || PlaceGate(GateRole.Entry, edges, entryKind)              // kind kept
             || PlaceGate(GateRole.Entry, only, otherKind)               // edge kept
             || PlaceGate(GateRole.Entry, edges, otherKind);             // neither
        }
        else
        {
            _ = PlaceGate(GateRole.Entry, edges, entryKind)
             || PlaceGate(GateRole.Entry, edges, otherKind);
        }

        // ---- the Exits ---------------------------------------------------------
        // <b>One ladder per Exit, not one ladder for all of them.</b> The ladder
        // used to be the outer loop and it stopped at the first tier that produced
        // any Exit at all — so a Domain asked for three got one whenever the strict
        // tier only allowed one, whatever the other three edges would have taken a
        // rung lower. Measured over 60 seeds that was the common case: the median
        // island had one Exit. Each Exit now walks the ladder on its own, so the
        // count asked for is delivered wherever the coast can deliver it.
        int made = 0;
        while (made < exits && PlaceGate(GateRole.Exit, edges, p.ExitGate)) made++;

        // A way out is a guarantee; the kind of way out is a preference. So the
        // named kind is held across the whole ladder above — no more trading it
        // away at the first tier that says no — and only a Domain left with no Link
        // at all gives it up.
        if (made == 0 && p.ExitGate != GateKind.Auto)
            while (made < exits && PlaceGate(GateRole.Exit, edges, GateKind.Auto)) made++;

        // And the ground each Gate is served by: the strip a hanging Gate's vessel
        // sets down on, and the forecourt a land Gate stands on. Only what the
        // Gates actually took — marking every coast that would have done painted
        // most of the coastline and answered a question nobody was asking.
        MarkLandings(d);
    }

    /// <summary>
    /// How often a Gate stands on the ground rather than hanging off the rim.
    /// Hanging is the norm — see the class summary.
    /// </summary>
    private const float LandGateShare = 0.25f;

    /// <summary>
    /// Why a coast will not take a hanging Gate, counted stage by stage — the
    /// funnel from "usable ground" down to "a Gate could stand here".
    ///
    /// <b>This exists because "only a quarter of Domains can host four hanging
    /// Gates" is a fact without a cause.</b> Four separate tests stand between a
    /// coast cell and a hanging Gate, and a summary that says how many Gates were
    /// placed cannot say which of the four did the refusing — so tuning any of
    /// them is guesswork. Run over a character that fails, the funnel names the
    /// stage in one line.
    ///
    /// The apron test is left out, since the apron is a property of the island
    /// rather than of the coast. <paramref name="loose"/> counts at the bottom rung
    /// of the ladder instead of the top — a one-cell strip and the crowded edge
    /// rules — which is what separates "this coast is too rough for a full strip"
    /// from "this coast cannot host a Gate at all".
    ///
    /// Note that the Gates already on the Domain count against the separation and
    /// dominance rules, so this answers "where could the *next* Gate go", which is
    /// the question when four are wanted and three were placed.
    /// </summary>
    public static (int Usable, int Fits, int Strip, int Flyable) Funnel(
        IslandData d, Cardinal edge, bool loose = false, bool alone = false)
    {
        int n = d.Size;
        Frame bounds = Bounds(d);
        var probe = new Gate(GateKind.Hanging, GateRole.Exit, edge, default, default, 0);
        Vector2I outward = probe.Outward, across = probe.Across;

        int length = loose ? 1 : StripLength;
        Ease ease = loose ? Ease.Crowded : Ease.Full;

        // `alone` asks the question the other way round: could this edge host a
        // Gate if no other Gate existed? The difference between that and the
        // ordinary count is exactly what the Gates already placed are costing —
        // which is the difference between "this coast cannot" and "the rules will
        // not let a fourth Gate in beside the other three".
        var others = alone ? new List<Gate>(d.Gates) : null;
        if (alone) d.Gates.Clear();

        int usable = 0, fits = 0, strip = 0, flyable = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Usable(d, x, z)) continue;
            usable++;
            if (!Fits(d, bounds, edge, x, z, outward, across, ease)) continue;
            fits++;
            if (!HasStrip(d, x, z, outward, across, length)) continue;
            strip++;
            short level = StripTop(d, x, z, outward, across, length);
            if (!Flyable(d, x, z, outward, across, level)) continue;
            flyable++;
        }

        if (others != null) { d.Gates.Clear(); d.Gates.AddRange(others); }
        return (usable, fits, strip, flyable);
    }

    private static bool TryPlace(int seed, IslandData d, Frame bounds, Cardinal edge,
                                 GateKind kind, GateRole role, out Gate gate,
                                 int minApron = ApronArea, int strip = StripLength,
                                 Ease ease = Ease.Full)
        => kind == GateKind.Land
            ? TryLand(seed, d, bounds, edge, role, minApron, ease, out gate)
            : TryHanging(seed, d, bounds, edge, role, minApron, strip, ease, out gate);

    /// <summary>
    /// The three rules that keep a Gate on the side of the map it names.
    ///
    /// <b>In its own band:</b> within <see cref="EdgeBand"/> of the outermost
    /// ground on that side, so a south Gate is not halfway up the island with a
    /// third of the Domain behind it. <b>Off the corners:</b> inside the middle of
    /// the edge, since a Gate in a corner faces two ways at once. <b>Outermost:</b>
    /// further east than every other Gate, if it is the east Gate — which is the
    /// whole meaning of facing east, and does not follow from the other two on a
    /// ragged coast.
    /// </summary>
    private static bool Fits(IslandData d, Frame bounds, Cardinal edge, int x, int z,
                             Vector2I outward, Vector2I across, Ease ease = Ease.Full)
    {
        if (!bounds.Any) return true;

        int along = x * outward.X + z * outward.Y;
        {
            float share = ease < Ease.Band ? EdgeBand : RelaxedEdgeBand;
            int floor = ease < Ease.Band ? MinEdgeBand : MinEdgeBand * 2;
            int band = Math.Max(floor, (int)(share * bounds.Extent(outward)));
            if (along < bounds.Extreme(outward) - band) return false;
        }

        if (ease < Ease.Anywhere)
        {
            int side = x * across.X + z * across.Y;
            int span = bounds.Extent(across);
            int inset = (int)(CornerInset * span);
            int high = bounds.Extreme(across);
            if (side > high - inset || side < high - span + inset) return false;
        }

        int apart = (int)((ease < Ease.Crowded ? GateSeparation : CrowdedSeparation) * d.Size);
        foreach (Gate g in d.Gates)
        {
            // Far enough from the others, and strictly the outermost in its own
            // direction — measured both ways, so neither Gate may overtake the
            // other on the axis the other is named for.
            //
            // <b>Distance is measured on the ground, not through the aether.</b>
            // A hanging Gate's Center is its portal, ten cells off the rim, while
            // the candidate here is a coast cell — so comparing the two measured
            // partly along a flight path and gave an answer up to ten cells out
            // from the walk it is meant to stand for. Apron is the ground each
            // Gate is served by, which is the thing that can be too close.
            if (Math.Abs(g.Apron.X - x) + Math.Abs(g.Apron.Y - z) < apart) return false;

            int mine = along;
            int theirs = g.Center.X * outward.X + g.Center.Z * outward.Y;
            if (mine < theirs + DominanceMargin) return false;

            Vector2I other = g.Outward;
            int itsAxis = g.Center.X * other.X + g.Center.Z * other.Y;
            int mineOnIts = x * other.X + z * other.Y;
            if (mineOnIts > itsAxis - DominanceMargin) return false;
        }
        return true;
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
    /// A Gate standing on the ground: three level cells to stand on, a clear
    /// outlook over the rim, and enough level ground behind it to start a company.
    /// </summary>
    private static bool TryLand(int seed, IslandData d, Frame bounds, Cardinal edge,
                                GateRole role, int minApron, Ease ease, out Gate gate)
    {
        gate = default;
        int n = d.Size;
        var probe = new Gate(GateKind.Land, role, edge, default, default, 0);
        Vector2I outward = probe.Outward, across = probe.Across;

        float best = float.MinValue;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Usable(d, x, z)) continue;
            if (!Fits(d, bounds, edge, x, z, outward, across, ease)) continue;

            short level = d.SurfaceLevel(x, z);

            // The portal's own three cells, level and usable.
            bool footing = true;
            for (int i = -1; i <= 1 && footing; i++)
            {
                int fx = x + across.X * i, fz = z + across.Y * i;
                footing = Usable(d, fx, fz) && d.SurfaceLevel(fx, fz) == level;
            }
            if (!footing) continue;

            // A clear outlook: nothing in front stands over the sill. It may run
            // out into aether — that is the view we want — but it may not be a
            // wall.
            bool clear = true;
            int overAether = 0;
            for (int step = 1; step <= Approach && clear; step++)
            {
                int fx = x + outward.X * step, fz = z + outward.Y * step;
                if (fx < 0 || fz < 0 || fx >= n || fz >= n || !d.HasLand(fx, fz))
                {
                    overAether++;
                    continue;
                }
                clear = d.SurfaceLevel(fx, fz) <= level + 1;
            }
            if (!clear || overAether == 0) continue;      // must face the rim, not inland

            // And somewhere to stand once you are through it. See LandApron.
            if (!HasApron(d, x, z, outward, across, LandApron, level)) continue;

            int apron = ApronAt(d, x, z, level);
            if (apron < minApron) continue;

            float score = Score(seed, bounds, x, z, outward, across, apron, 0x2200u);
            if (score <= best) continue;

            best = score;
            gate = new Gate(GateKind.Land, role, edge, new Vector3I(x, level, z),
                            new Vector2I(x, z), apron);
        }
        return best > float.MinValue;
    }

    /// <summary>
    /// A Gate hanging in the aether, and the strip of level ground opposite it
    /// that makes flying through worth anything. The strip runs <i>inland</i> from
    /// the coast, one cell wide and four long, along the way a vessel would come
    /// in — the ground under the Gate's centre line, and nothing more.
    /// </summary>
    private static bool TryHanging(int seed, IslandData d, Frame bounds, Cardinal edge,
                                   GateRole role, int minApron, int stripLength, Ease ease,
                                   out Gate gate)
    {
        gate = default;
        int n = d.Size;
        var probe = new Gate(GateKind.Hanging, role, edge, default, default, 0);
        Vector2I outward = probe.Outward, across = probe.Across;

        float best = float.MinValue;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Fits(d, bounds, edge, x, z, outward, across, ease)) continue;
            if (!HasStrip(d, x, z, outward, across, stripLength)) continue;

            // The sill is measured from the **highest** ground on the strip, not
            // from the coast cell. A beach steps the coast down by up to two
            // slabs, so measuring at the head drops the sill by the same two and
            // the approach then collides with ordinary ground three cells inland —
            // which is how beaches quietly turned most hanging Gates into land
            // ones. A vessel comes in over the strip, so the strip is the datum.
            short level = StripTop(d, x, z, outward, across, stripLength);
            if (!Flyable(d, x, z, outward, across, level)) continue;

            int apron = ApronAt(d, x, z, d.SurfaceLevel(x, z));
            if (apron < minApron) continue;

            float score = Score(seed, bounds, x, z, outward, across, apron, 0x3300u);
            if (score <= best) continue;

            best = score;

            // The portal floats clear of the rim, its sill a little above the
            // strip so a vessel comes in level rather than climbing.
            var centre = new Vector3I(x + outward.X * HangingOffset, level + 2,
                                      z + outward.Y * HangingOffset);
            var inner = new Vector2I(x - outward.X * (stripLength - 1),
                                     z - outward.Y * (stripLength - 1));
            gate = new Gate(GateKind.Hanging, role, edge, centre, inner, apron, stripLength);
        }
        return best > float.MinValue;
    }

    /// <summary>
    /// Level ground running inland from a coast cell: the landing strip.
    ///
    /// A slab of tolerance along it, not dead level. Requiring one exact level on
    /// ground that is tapering toward a coast left 24 of 60 islands unable to host
    /// a hanging Gate at all — and the Entry's kind is not ours to refuse. One
    /// slab is the free step anyway: a vessel setting down across it is not
    /// troubled by what a walker would not notice.
    /// </summary>
    private static bool HasStrip(IslandData d, int x, int z, Vector2I outward, Vector2I across,
                                 int length)
    {
        int n = d.Size;
        if (!Usable(d, x, z)) return false;

        // The head of the strip: land with aether directly outward of it.
        int hx = x + outward.X, hz = z + outward.Y;
        if (hx >= 0 && hz >= 0 && hx < n && hz < n && d.HasLand(hx, hz)) return false;

        // <b>A slab between neighbours, not a slab across the whole strip.</b>
        // Measuring every cell against the head refuses any strip that ramps —
        // and a coast that steps down onto a beach ramps by construction, which
        // is what started pushing Gates onto the relaxed placement rules the
        // moment beaches existed. A vessel setting down cares that the ground is
        // even underfoot, which is what the two-slab range across the whole strip
        // says. Testing each cell against the ones beside it as well was tried and
        // is too strict: the cells at the edge of the strip are then held to the
        // ground *outside* it, and hanging Gates fell from 105 to 42.
        int half = (StripWidth - 1) / 2;
        short lowest = short.MaxValue, highest = short.MinValue;

        for (int along = 0; along < length; along++)
        for (int side = -half; side <= half; side++)
        {
            int sx = x - outward.X * along + across.X * side;
            int sz = z - outward.Y * along + across.Y * side;
            if (!Usable(d, sx, sz)) return false;

            short here = d.SurfaceLevel(sx, sz);
            lowest = Math.Min(lowest, here);
            highest = Math.Max(highest, here);
            if (highest - lowest > StripTolerance) return false;
        }
        return true;
    }

    /// <summary>
    /// The forecourt a land Gate stands on: <see cref="StripWidth"/> cells across
    /// by <paramref name="depth"/> running <i>inland</i>, the portal's own row
    /// included, every cell usable and within the free step of the sill.
    ///
    /// The free step and not dead level, for the same reason the landing strip
    /// allows a slab: ground a walker would not notice is not a reason to refuse a
    /// Link. What it does refuse is the case this was written for — a portal on a
    /// three-cell ledge with a cliff one step behind it, which the sixty-cell
    /// shelf test passed because a shelf is measured somewhere on the island
    /// rather than here.
    /// </summary>
    private static bool HasApron(IslandData d, int x, int z, Vector2I outward, Vector2I across,
                                 int depth, short level)
    {
        int half = (StripWidth - 1) / 2;
        for (int along = 0; along < depth; along++)
        for (int side = -half; side <= half; side++)
        {
            int ax = x - outward.X * along + across.X * side;
            int az = z - outward.Y * along + across.Y * side;
            if (!Usable(d, ax, az)) return false;
            if (Math.Abs(d.SurfaceLevel(ax, az) - level) > 1) return false;
        }
        return true;
    }

    /// <summary>The highest ground anywhere on the landing strip.</summary>
    private static short StripTop(IslandData d, int x, int z, Vector2I outward,
                                  Vector2I across, int length)
    {
        int half = (StripWidth - 1) / 2;
        short top = d.SurfaceLevel(x, z);
        for (int along = 0; along < length; along++)
        for (int side = -half; side <= half; side++)
        {
            int sx = x - outward.X * along + across.X * side;
            int sz = z - outward.Y * along + across.Y * side;
            if (!Usable(d, sx, sz)) continue;
            top = Math.Max(top, d.SurfaceLevel(sx, sz));
        }
        return top;
    }

    /// <summary>
    /// Whether a vessel could actually fly in to that strip, and whether the
    /// portal itself would hang clear.
    ///
    /// The approach is tested against the <i>sill height</i>, not against land as
    /// such: a vessel comes in two slabs above the strip, so a low spit under the
    /// flight path is scenery and only ground that reaches the sill is an
    /// obstruction. Testing for any land at all was tried first and left 12
    /// hanging Gates in 182 — a coastline is never that tidy.
    /// </summary>
    private static bool Flyable(IslandData d, int x, int z, Vector2I outward, Vector2I across,
                                short level)
    {
        int n = d.Size;
        int sill = level + 2;

        for (int step = 1; step <= HangingOffset; step++)
        for (int side = -1; side <= 1; side++)
        {
            int gx = x + outward.X * step + across.X * side;
            int gz = z + outward.Y * step + across.Y * side;
            if (gx < 0 || gz < 0 || gx >= n || gz >= n || !d.HasLand(gx, gz)) continue;
            if (d.SurfaceLevel(gx, gz) >= sill) return false;
        }

        // The portal itself hangs clear, and so does the air immediately behind
        // it: aether under all three cells for the last stretch of the approach.
        for (int step = HangingOffset - HangingClearance + 1; step <= HangingOffset; step++)
        for (int side = -1; side <= 1; side++)
        {
            int gx = x + outward.X * step + across.X * side;
            int gz = z + outward.Y * step + across.Y * side;
            if (gx >= 0 && gz >= 0 && gx < n && gz < n && d.HasLand(gx, gz)) return false;
        }
        return true;
    }

    /// <summary>
    /// The ground the Domain's Gates are actually served by: the strip a hanging
    /// Gate's vessel sets down on, and the forecourt a land Gate stands on. Both
    /// are <see cref="StripWidth"/> across and run inland from the portal; only
    /// their length differs, and only because a vessel needs a berth and a walker
    /// needs somewhere to turn round.
    ///
    /// It used to mark every coast that <i>would</i> take a strip — which painted
    /// most of the coastline, and answered "where else could a Link have come
    /// out?" rather than "where does this one land?". With a Gate now guaranteed
    /// on every Domain, the second question is the one worth drawing.
    /// </summary>
    private static void MarkLandings(IslandData d)
    {
        int n = d.Size;
        int half = (StripWidth - 1) / 2;

        foreach (Gate g in d.Gates)
        {
            Vector2I outward = g.Outward, across = g.Across;

            // A hanging Gate's strip starts at the coast cell the portal hangs off;
            // a land Gate's apron starts under the portal itself.
            Vector2I head = g.Kind == GateKind.Hanging
                ? new Vector2I(g.Center.X, g.Center.Z) - outward * HangingOffset
                : new Vector2I(g.Center.X, g.Center.Z);
            int length = g.Kind == GateKind.Hanging ? Math.Max(1, g.Landing) : LandApron;

            for (int along = 0; along < length; along++)
            for (int side = -half; side <= half; side++)
            {
                Vector2I cell = head - outward * along + across * side;
                if (cell.X < 0 || cell.Y < 0 || cell.X >= n || cell.Y >= n) continue;
                if (!d.HasLand(cell.X, cell.Y)) continue;
                d.Landings[cell.X, cell.Y] = true;
            }
        }
    }

    /// <summary>
    /// How good a candidate is, once it is allowed at all: as far out on its own
    /// side as possible, near the middle of that side, with the apron as a
    /// tie-break and a little noise so two equal coasts do not always resolve the
    /// same way.
    /// </summary>
    private static float Score(int seed, Frame bounds, int x, int z,
                               Vector2I outward, Vector2I across, int apron, uint salt)
    {
        int along = x * outward.X + z * outward.Y;
        int side = x * across.X + z * across.Y;
        float middle = bounds.Extreme(across) - bounds.Extent(across) * 0.5f;

        // Weighting the middle harder was tried, on the theory that a Gate at the
        // far corner of its own side moves the line the next Gate round has to
        // beat. It does — but at 1.0 four hanging Gates got *rarer* (three hanging
        // Exits fell from 41% of seeds to 33%), because centring every Gate also
        // pulls each one off whatever coast could actually host it. The blocker is
        // the mutual dominance rule and the arrangement's geometry, not this.
        return along
               - MathF.Abs(side - middle) * 0.35f
               + apron * 0.01f
               + Hash01(seed, salt ^ (uint)(x * 733 + z)) * 0.5f;
    }

    /// <summary>
    /// Level, usable ground at a point: the yard a company would build on.
    ///
    /// This is exactly what a <see cref="Shelf"/> is — ground level enough to lay
    /// a settlement out on — so it is read off <see cref="IslandData.ShelfId"/>
    /// rather than flooded per candidate. Flooding was the same answer computed
    /// tens of thousands of times an island, and it doubled generation.
    /// </summary>
    private static int ApronAt(IslandData d, int x, int z, short level)
    {
        if (x < 0 || z < 0 || x >= d.Size || z >= d.Size) return 0;
        int id = d.ShelfId[x, z];
        if (id < 0 || id >= d.Shelves.Count) return 0;

        Shelf shelf = d.Shelves[id];
        // Wide as well as big: a company cannot work a ledge, however long.
        return shelf.Width >= Traversal.MinShelfWidth ? shelf.Area : 0;
    }

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
