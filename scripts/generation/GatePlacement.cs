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
    /// the Gate. Four: a vessel needs somewhere to set down, not an aerodrome,
    /// and a strip long enough to be a runway was quietly the hardest thing on
    /// the whole island to find.
    /// </summary>
    public const int StripLength = 4;

    /// <summary>
    /// And how wide. One cell — the strip is the ground under the Gate's centre
    /// line and nothing more.
    /// </summary>
    public const int StripWidth = 1;

    /// <summary>How far off the rim a hanging Gate floats.</summary>
    public const int HangingOffset = 4;

    /// <summary>
    /// How far in front of a land Gate has to be clear of higher ground. A Gate
    /// facing a wall four cells away opens onto nothing.
    /// </summary>
    public const int Approach = 10;

    /// <summary>
    /// How far back from the outermost usable ground on its own side a Gate may
    /// stand, as a share of the island's width in that direction. A south Gate
    /// halfway up the island leaves a third of the Domain behind the player as
    /// they arrive, which is not what "south" means.
    /// </summary>
    private const float EdgeBand = 0.22f;

    /// <summary>And never less than this many cells, on a small or ragged island.</summary>
    private const int MinEdgeBand = 8;

    /// <summary>
    /// How much of each end of an edge is corner rather than edge, as a share of
    /// the island's width across that edge. A Gate in the corner faces two ways
    /// at once and crowds whichever Gate holds the next edge round.
    /// </summary>
    private const float CornerInset = 0.16f;

    /// <summary>
    /// Cells two Gates must keep between them. They already sit on separate edges;
    /// this is what stops two of them meeting near the corner where those edges
    /// join, as a share of the footprint.
    /// </summary>
    private const float GateSeparation = 0.30f;

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

    public static void Place(int seed, IslandParams p, IslandData d)
    {
        MarkAirstrips(d);

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
        // always north.
        int first = (int)(Hash01(seed, 0x3D1Fu) * 4f) & 3;
        var edges = new List<Cardinal>();
        for (int i = 0; i < 4; i++) edges.Add((Cardinal)((first + i) & 3));

        Frame bounds = Bounds(d);
        var taken = new HashSet<Cardinal>();

        // The Entry first, and on any edge that will have it. Its kind is fixed by
        // the Domain that sent you, so it gets the pick of the coast — and if no
        // coast will take it comfortably, the requirement that gives is the
        // comfort, not the kind. A Link whose two ends disagree is not a Link.
        // Comfort first, then room to land, then bare possibility.
        (int Apron, int Strip)[] tiers =
        {
            (ApronArea, StripLength),
            (ApronArea / 2, StripLength),
            (ApronArea / 2, StripLength - 1),
            (Gate.Width * Gate.Width, 2),
        };

        foreach ((int apron, int strip) in tiers)
        {
            foreach (Cardinal edge in edges)
            {
                if (!TryPlace(seed, d, bounds, edge, entryKind, GateRole.Entry, out Gate gate,
                              apron, strip))
                    continue;
                d.Gates.Add(gate);
                taken.Add(edge);
                break;
            }
            if (d.Gates.Count > 0) break;
        }
        if (d.Gates.Count == 0)
        {
            // The island genuinely cannot host that kind anywhere — no strip at
            // all, or no coast facing out. Take the other rather than leave the
            // Domain unreachable: a Domain with no way in is not a Domain.
            GateKind fallback = entryKind == GateKind.Land ? GateKind.Hanging : GateKind.Land;
            foreach (Cardinal edge in edges)
            {
                if (!TryPlace(seed, d, bounds, edge, fallback, GateRole.Entry, out Gate gate))
                    continue;
                d.Gates.Add(gate);
                taken.Add(edge);
                break;
            }
        }

        foreach (Cardinal edge in edges)
        {
            if (d.Gates.Count > exits) break;             // entry + exits
            if (taken.Contains(edge)) continue;

            // An exit hangs unless the coast will not have it, in which case it
            // stands: a Link is still better than no Link.
            GateKind want = Hash01(seed, 0x91C0u ^ (uint)edge * 2654435761u) < LandGateShare
                ? GateKind.Land
                : GateKind.Hanging;
            GateKind other = want == GateKind.Land ? GateKind.Hanging : GateKind.Land;

            if (TryPlace(seed, d, bounds, edge, want, GateRole.Exit, out Gate gate)
                || TryPlace(seed, d, bounds, edge, other, GateRole.Exit, out gate))
            {
                d.Gates.Add(gate);
                taken.Add(edge);
            }
        }
    }

    /// <summary>
    /// How often a Gate stands on the ground rather than hanging off the rim.
    /// Hanging is the norm — see the class summary.
    /// </summary>
    private const float LandGateShare = 0.25f;

    private static bool TryPlace(int seed, IslandData d, Frame bounds, Cardinal edge,
                                 GateKind kind, GateRole role, out Gate gate,
                                 int minApron = ApronArea, int strip = StripLength)
        => kind == GateKind.Land
            ? TryLand(seed, d, bounds, edge, role, minApron, out gate)
            : TryHanging(seed, d, bounds, edge, role, minApron, strip, out gate);

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
                             Vector2I outward, Vector2I across)
    {
        if (!bounds.Any) return true;

        int along = x * outward.X + z * outward.Y;
        int band = Math.Max(MinEdgeBand, (int)(EdgeBand * bounds.Extent(outward)));
        if (along < bounds.Extreme(outward) - band) return false;

        int side = x * across.X + z * across.Y;
        int span = bounds.Extent(across);
        int inset = (int)(CornerInset * span);
        int high = bounds.Extreme(across);
        if (side > high - inset || side < high - span + inset) return false;

        int apart = (int)(GateSeparation * d.Size);
        foreach (Gate g in d.Gates)
        {
            // Far enough from the others, and strictly the outermost in its own
            // direction — measured both ways, so neither Gate may overtake the
            // other on the axis the other is named for.
            if (Math.Abs(g.Center.X - x) + Math.Abs(g.Center.Z - z) < apart) return false;

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
                                GateRole role, int minApron, out Gate gate)
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
            if (!Fits(d, bounds, edge, x, z, outward, across)) continue;

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
                                   GateRole role, int minApron, int stripLength, out Gate gate)
    {
        gate = default;
        int n = d.Size;
        var probe = new Gate(GateKind.Hanging, role, edge, default, default, 0);
        Vector2I outward = probe.Outward, across = probe.Across;

        float best = float.MinValue;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Fits(d, bounds, edge, x, z, outward, across)) continue;
            if (!HasStrip(d, x, z, outward, across, stripLength)) continue;

            short level = d.SurfaceLevel(x, z);
            if (!Flyable(d, x, z, outward, across, level)) continue;

            int apron = ApronAt(d, x, z, level);
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

        short level = d.SurfaceLevel(x, z);
        int half = (StripWidth - 1) / 2;
        for (int along = 0; along < length; along++)
        for (int side = -half; side <= half; side++)
        {
            int sx = x - outward.X * along + across.X * side;
            int sz = z - outward.Y * along + across.Y * side;
            if (!Usable(d, sx, sz)) return false;
            if (Math.Abs(d.SurfaceLevel(sx, sz) - level) > 1) return false;
        }
        return true;
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

        // The portal itself still hangs clear: aether under all three cells.
        for (int side = -1; side <= 1; side++)
        {
            int gx = x + outward.X * HangingOffset + across.X * side;
            int gz = z + outward.Y * HangingOffset + across.Y * side;
            if (gx >= 0 && gz >= 0 && gx < n && gz < n && d.HasLand(gx, gz)) return false;
        }
        return true;
    }

    /// <summary>
    /// Every coast cell a vessel could set down at, in any direction: the ground
    /// a hanging Gate could be given, of which the Gates that exist took one each.
    /// The lab draws these, because "where else could a Link have come out?" is a
    /// question about the island that is otherwise invisible.
    /// </summary>
    private static void MarkAirstrips(IslandData d)
    {
        int n = d.Size;
        for (int edge = 0; edge < 4; edge++)
        {
            var probe = new Gate(GateKind.Hanging, GateRole.Exit, (Cardinal)edge,
                                 default, default, 0);
            Vector2I outward = probe.Outward, across = probe.Across;

            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!HasStrip(d, x, z, outward, across, StripLength)) continue;
                if (!Flyable(d, x, z, outward, across, d.SurfaceLevel(x, z))) continue;
                if (ApronAt(d, x, z, d.SurfaceLevel(x, z)) < ApronArea) continue;

                for (int along = 0; along < StripLength; along++)
                {
                    int sx = x - outward.X * along, sz = z - outward.Y * along;
                    if (sx < 0 || sz < 0 || sx >= n || sz >= n) break;
                    d.Airstrip[sx, sz] = true;
                }
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
