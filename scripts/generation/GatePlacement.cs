using System;
using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Places the Domain's Gates: one <see cref="GateRole.Entry"/> the player emerges
/// from, and one to three <see cref="GateRole.Exit"/> Links onward.
///
/// <para><b>One Gate per edge.</b> Domains sit on a plane at their world-tree
/// position — a Domain linked north is found by scrolling north — so two Gates
/// facing the same way would be two Links to the same place.</para>
///
/// <para><b>The Entry's kind is an input, not a choice.</b> A Link joins two
/// Gates, so the Gate you arrive at has to match the one you left: land to land,
/// hanging to hanging. That is why <see cref="IslandParams.EntryGate"/> exists —
/// the Domain that sent you sets it, and this Domain grows around it.</para>
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

    /// <summary>Cells of level ground a landing strip needs, running inland from the coast.</summary>
    public const int StripLength = 8;

    /// <summary>And how wide. Narrower than the Gate would make the approach a needle.</summary>
    public const int StripWidth = 3;

    /// <summary>How far off the rim a hanging Gate floats.</summary>
    public const int HangingOffset = 4;

    /// <summary>
    /// How far in front of a land Gate has to be clear of higher ground. A Gate
    /// facing a wall four cells away opens onto nothing.
    /// </summary>
    public const int Approach = 10;

    public static void Place(int seed, IslandParams p, IslandData d)
    {
        GateKind entryKind = p.EntryGate != GateKind.Auto
            ? p.EntryGate
            : (Hash01(seed, 0xE47Eu) < 0.5f ? GateKind.Land : GateKind.Hanging);

        int exits = p.ExitGates > 0
            ? Math.Clamp(p.ExitGates, 1, 3)
            : 1 + (int)(Hash01(seed, 0x6A7Eu) * 3f);

        // The order the edges are tried in, rotated per seed so the entry is not
        // always north.
        int first = (int)(Hash01(seed, 0x3D1Fu) * 4f) & 3;
        var edges = new List<Cardinal>();
        for (int i = 0; i < 4; i++) edges.Add((Cardinal)((first + i) & 3));

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
            (ApronArea / 2, StripLength - 2),
            (Gate.Width * Gate.Width, Gate.Width + 1),
        };

        foreach ((int apron, int strip) in tiers)
        {
            foreach (Cardinal edge in edges)
            {
                if (!TryPlace(seed, d, edge, entryKind, GateRole.Entry, out Gate gate,
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
                if (!TryPlace(seed, d, edge, fallback, GateRole.Entry, out Gate gate)) continue;
                d.Gates.Add(gate);
                taken.Add(edge);
                break;
            }
        }

        foreach (Cardinal edge in edges)
        {
            if (d.Gates.Count > exits) break;             // entry + exits
            if (taken.Contains(edge)) continue;

            // An exit is free to be either kind; try the one the seed prefers and
            // fall back, so a coast that only suits a landing strip still gets a
            // Link instead of none.
            GateKind want = Hash01(seed, 0x91C0u ^ (uint)edge * 2654435761u) < 0.5f
                ? GateKind.Land
                : GateKind.Hanging;
            GateKind other = want == GateKind.Land ? GateKind.Hanging : GateKind.Land;

            if (TryPlace(seed, d, edge, want, GateRole.Exit, out Gate gate)
                || TryPlace(seed, d, edge, other, GateRole.Exit, out gate))
            {
                d.Gates.Add(gate);
                taken.Add(edge);
            }
        }
    }

    private static bool TryPlace(int seed, IslandData d, Cardinal edge, GateKind kind,
                                 GateRole role, out Gate gate,
                                 int minApron = ApronArea, int strip = StripLength)
        => kind == GateKind.Land
            ? TryLand(seed, d, edge, role, minApron, out gate)
            : TryHanging(seed, d, edge, role, minApron, strip, out gate);

    /// <summary>
    /// A Gate standing on the ground: three level cells to stand on, a clear
    /// outlook over the rim, and enough level ground behind it to start a company.
    /// </summary>
    private static bool TryLand(int seed, IslandData d, Cardinal edge, GateRole role,
                                int minApron, out Gate gate)
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

            // Further out along this edge is better — a Gate belongs on the coast
            // it faces — with the apron as the tie-break.
            float outness = x * outward.X + z * outward.Y;
            float score = outness + apron * 0.02f
                          + Hash01(seed, 0x2200u ^ (uint)(x * 733 + z)) * 0.5f;
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
    /// the coast, along the way a vessel would come in.
    /// </summary>
    private static bool TryHanging(int seed, IslandData d, Cardinal edge, GateRole role,
                                   int minApron, int stripLength, out Gate gate)
    {
        gate = default;
        int n = d.Size;
        var probe = new Gate(GateKind.Hanging, role, edge, default, default, 0);
        Vector2I outward = probe.Outward, across = probe.Across;

        float best = float.MinValue;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            // The head of the strip: land with aether directly outward of it.
            if (!Usable(d, x, z)) continue;
            int hx = x + outward.X, hz = z + outward.Y;
            if (hx >= 0 && hz >= 0 && hx < n && hz < n && d.HasLand(hx, hz)) continue;

            short level = d.SurfaceLevel(x, z);

            // A slab of tolerance along the strip, not dead level. Requiring one
            // exact level over eight cells by three, on ground that is tapering
            // toward a coast, left 24 of 60 islands unable to host a hanging Gate
            // at all — and the Entry's kind is not ours to refuse. One slab is the
            // free step anyway: a vessel setting down across it is not troubled by
            // what a walker would not notice.
            bool strip = true;
            for (int along = 0; along < stripLength && strip; along++)
            for (int side = -(StripWidth / 2); side <= StripWidth / 2 && strip; side++)
            {
                int sx = x - outward.X * along + across.X * side;
                int sz = z - outward.Y * along + across.Y * side;
                strip = Usable(d, sx, sz) && Math.Abs(d.SurfaceLevel(sx, sz) - level) <= 1;
            }
            if (!strip) continue;

            // The approach has to be flyable. A coast that curves back can put a
            // "hanging" Gate over a headland four cells along the bay, which is a
            // Gate standing on land by another name.
            //
            // The test is against the <i>sill height</i>, not against land as
            // such: a vessel comes in two slabs above the strip, so a low spit
            // under the flight path is scenery, and only ground that reaches the
            // sill is an obstruction. Testing for any land at all was tried first
            // and left 12 hanging Gates in 182 — a coastline is never that tidy.
            int sill = level + 2;
            bool flyable = true;
            for (int step = 1; step <= HangingOffset && flyable; step++)
            for (int side = -1; side <= 1 && flyable; side++)
            {
                int gx = x + outward.X * step + across.X * side;
                int gz = z + outward.Y * step + across.Y * side;
                if (gx < 0 || gz < 0 || gx >= n || gz >= n || !d.HasLand(gx, gz)) continue;
                flyable = d.SurfaceLevel(gx, gz) < sill;
            }
            // The portal itself still hangs clear: aether under all three cells.
            for (int side = -1; side <= 1 && flyable; side++)
            {
                int gx = x + outward.X * HangingOffset + across.X * side;
                int gz = z + outward.Y * HangingOffset + across.Y * side;
                flyable = gx < 0 || gz < 0 || gx >= n || gz >= n || !d.HasLand(gx, gz);
            }
            if (!flyable) continue;

            int apron = ApronAt(d, x, z, level);
            if (apron < minApron) continue;

            float outness = x * outward.X + z * outward.Y;
            float score = outness + apron * 0.02f
                          + Hash01(seed, 0x3300u ^ (uint)(x * 733 + z)) * 0.5f;
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
    /// Level, usable ground at a point: the yard a company would build on.
    ///
    /// This is exactly what a <see cref="Shelf"/> is — 4-connected ground all at
    /// one slab level — so it is read off <see cref="IslandData.ShelfId"/> rather
    /// than flooded per candidate. Flooding was the same answer computed tens of
    /// thousands of times an island, and it doubled generation.
    /// </summary>
    private static int ApronAt(IslandData d, int x, int z, short level)
    {
        int id = d.ShelfId[x, z];
        if (id < 0 || id >= d.Shelves.Count) return 0;

        Shelf shelf = d.Shelves[id];
        // Wide as well as big: a company cannot work a ledge, however long.
        return shelf.Level == level && shelf.Width >= Traversal.MinShelfWidth ? shelf.Area : 0;
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
