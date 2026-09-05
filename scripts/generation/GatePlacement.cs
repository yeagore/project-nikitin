using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// Places the Domain's Gates. Four hanging sites, one per edge and each the outermost thing
/// on its edge, are chosen as a set (<see cref="ChooseSites"/>); the parameters then subtract:
/// an Exit the Domain does not need is dropped, and a Land Gate is the same site with its
/// portal moved down onto the strip. Runs after <see cref="Traversal"/>, levels the strips it
/// chose, and reports whether terrain moved so the analysis can be run again.
/// </summary>
internal static partial class GatePlacement
{
    /// <summary>Level cells the ground by a Gate should offer — a ranking target, never a refusal. The audit reads it.</summary>
    public const int ApronArea = 60;

    /// <summary>Cells of landing strip running inland from the coast under a Gate, one cell across. The audit reads it.</summary>
    public const int StripLength = 3;

    /// <summary>Slabs a strip may span before levelling it means quarrying a hillside; doubled on the last rungs.</summary>
    private const int StripTolerance = 3;

    /// <summary>Cells off the rim a hanging portal floats; small enough to stay inside the bounding cube on every size. The audit reads it.</summary>
    public const int HangingOffset = 5;

    /// <summary>The last cells of the flight path that must be pure aether; the rest may pass over ground below the sill.</summary>
    public const int HangingClearance = 3;

    /// <summary>How far back from the outermost usable ground on its side a Gate may stand, as a share of the extent that way.</summary>
    private const float EdgeBand = 0.22f;

    /// <summary>The band once relaxed. It widens but never goes, so the Gate stays on its own half of the island.</summary>
    private const float RelaxedEdgeBand = 0.45f;

    /// <summary>Floor on the band, in cells, for a small or ragged island.</summary>
    private const int MinEdgeBand = 8;

    /// <summary>Share of each end of an edge that counts as corner rather than edge.</summary>
    private const float CornerInset = 0.22f;

    /// <summary>Manhattan cells two Gates keep between them, as a share of the footprint.</summary>
    private const float GateSeparation = 0.42f;

    /// <summary>Separation on the Crowded rung. Public so the audit checks the rule in force.</summary>
    public const float CrowdedSeparation = 0.32f;

    /// <summary>Separation on the last rung: the distance no two Gates may ever be inside of. The audit checks it.</summary>
    public const float MinSeparation = CrowdedSeparation * 0.5f;

    /// <summary>Cells a Gate must out-reach every other in its own direction: a strict order, not a berth.</summary>
    private const int DominanceMargin = 2;

    /// <summary>Share of seeded Gates that stand on the ground; hanging is the norm.</summary>
    private const float LandGateShare = 0.25f;

    /// <summary>Candidate sites kept per edge for the set-wise search.</summary>
    private const int CandidatesPerEdge = 16;

    /// <summary>Cells around a strip head to look for a district; a coast cell can be a scrap beside the one it serves.</summary>
    private const int ApronSearch = 4;

    /// <summary>Places the Gates and levels their strips. Returns whether terrain changed, so the caller re-runs the traversal analysis.</summary>
    public static bool Place(int seed, IslandParams p, IslandData d)
    {
        d.Gates.Clear();
        Site[] chosen = ChooseSites(seed, d);

        int entry = PickEntry(seed, p, chosen);
        if (entry < 0) return false;                      // no coast at all: nothing to do

        d.Gates.Add(Build(chosen[entry], GateRole.Entry, RollKind(seed, p.EntryGate, 0xE47Eu)));
        foreach (int i in ExitOrder(seed, p, chosen, entry))
            d.Gates.Add(Build(chosen[i], GateRole.Exit,
                              RollKind(seed, p.ExitGate, 0x91C0u ^ (uint)chosen[i].Edge * 2654435761u)));

        bool moved = LevelStrips(d);
        MarkLandings(d);
        return moved;
    }

    /// <summary>Index of the Entry site: the named edge if it got one, else the seed's rotation over the four; -1 when no edge has a site.</summary>
    private static int PickEntry(int seed, IslandParams p, Site[] chosen)
    {
        int entry = -1;
        if (p.EntryEdge != GateEdge.Auto)
        {
            var want = (Cardinal)((int)p.EntryEdge - 1);
            for (int i = 0; i < chosen.Length; i++)
                if (chosen[i].Edge == want && !chosen[i].IsEmpty) entry = i;
        }
        if (entry < 0)
        {
            int first = (int)(Hash01(seed, 0x3D1Fu) * chosen.Length);
            for (int k = 0; k < chosen.Length && entry < 0; k++)
            {
                int i = (first + k) % chosen.Length;
                if (!chosen[i].IsEmpty) entry = i;
            }
        }
        return entry;
    }

    /// <summary>The Exit sites to keep, best first: as many as asked for, or one to three from the seed. The worst-founded are the ones dropped.</summary>
    private static List<int> ExitOrder(int seed, IslandParams p, Site[] chosen, int entry)
    {
        int exits = p.ExitGates > 0
            ? Math.Clamp(p.ExitGates, 1, 3)
            : 1 + (int)(Hash01(seed, 0x6A7Eu) * 3f);

        var order = new List<int>();
        for (int i = 0; i < chosen.Length; i++)
            if (i != entry && !chosen[i].IsEmpty) order.Add(i);
        order.Sort((a, b) => chosen[b].Score.CompareTo(chosen[a].Score));
        if (order.Count > exits) order.RemoveRange(exits, order.Count - exits);
        return order;
    }

    /// <summary>The asked-for kind, or a seeded roll at <see cref="LandGateShare"/> when it is Auto.</summary>
    private static GateKind RollKind(int seed, GateKind asked, uint salt)
        => asked != GateKind.Auto
            ? asked
            : Hash01(seed, salt) < LandGateShare ? GateKind.Land : GateKind.Hanging;

    /// <summary>A Gate from a site. A land Gate is the same site with the portal on the strip's head instead of at the end of the flight path.</summary>
    private static Gate Build(Site site, GateRole role, GateKind kind)
    {
        Vector2I outward = site.Edge.Outward();
        Vector2I apron = site.Head - outward * (StripLength - 1);

        Vector3I centre = kind == GateKind.Land
            ? new Vector3I(site.X, site.Level, site.Z)
            : new Vector3I(site.X + outward.X * HangingOffset, site.Level + 2,
                           site.Z + outward.Y * HangingOffset);

        return new Gate(kind, role, site.Edge, centre, apron, site.Apron);
    }

    /// <summary>
    /// Levels every Gate's strip to the surface of its inner cell, so the join to the island
    /// is untouched and only the cells running out to the rim move. The pass's only terrain
    /// mutation, hence the bool.
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

    /// <summary>Moves one column's surface to a slab by resizing its lowest span; refuses a no-op, a cut to the keel, or a rise into a second span.</summary>
    private static bool SetSurface(IslandData d, int x, int z, short level)
    {
        if (!InBounds(d.Size, x, z)) return false;
        Span[] spans = d.Spans[x, z];
        if (spans == null || spans.Length == 0) return false;

        Span low = spans[0];
        if (low.Top == level) return false;
        if (level <= low.Bottom) return false;
        if (spans.Length > 1 && level >= spans[1].Bottom - 1) return false;

        spans[0] = low with { Top = level };
        return true;
    }

    /// <summary>Marks the strip cells under every Gate, whichever kind, in <see cref="IslandData.Landings"/>.</summary>
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
                if (!InBounds(n, cell.X, cell.Y)) continue;
                if (!d.HasLand(cell.X, cell.Y)) continue;
                d.Landings[cell.X, cell.Y] = true;
            }
        }
    }
}
