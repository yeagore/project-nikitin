using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>Estuaries: the lower reach of a navigable river opened into a funnel at the rim.</summary>
internal static partial class Rivers
{
    /// <summary>How often a navigable mouth over gentle ground opens into an estuary; the rest are the deltas' to try.</summary>
    private const float EstuaryChance = 0.5f;

    /// <summary>Cells across at the rim, rolled per mouth: past the three a deck over water spans, so the lower reach is crossed nowhere.</summary>
    private const int EstuaryMouthMin = 4, EstuaryMouthMax = 6;

    /// <summary>Axis cells the funnel runs upstream, at 96²; scaled by the footprint.</summary>
    private const int EstuaryLengthMin = 8, EstuaryLengthMax = 24;

    /// <summary>Share of the land a stamp wants that must lie within a slab of the axis for the funnel to go on: a gorge mouth has no estuary.</summary>
    private const float EstuaryGentleShare = 0.6f;

    /// <summary>Slabs above the axis's ground an estuary still floods; higher is a bank, and the funnel narrows round it.</summary>
    private const int EstuaryRise = 1;

    /// <summary>Axis cells a funnel must run to be one; shorter is a wide mouth and is put back.</summary>
    private const int EstuaryMinSteps = 3;

    /// <summary>
    /// Opens some navigable mouths into estuaries: from the rim, the funnel walks the
    /// main stem upstream, stamping the land within a half-width of each axis cell
    /// into the river — the half-width from the rolled mouth down to the pair's own
    /// two cells over the rolled length. Each cell taken is twinned to its axis cell,
    /// so the pair machinery levels and cuts it as it does a partner. Ground more
    /// than <see cref="EstuaryRise"/> above the axis is a bank the funnel narrows
    /// round, and where too little of a stamp is gentle the funnel ends; an eyot in
    /// the funnel is drowned. The funnel stops below a fall. Runs after the braid and
    /// before the deltas, whose arms find the funnel's water and give up, so a mouth
    /// is an estuary or a delta and never both.
    /// </summary>
    private static void Estuaries(int seed, int n, bool[,] land, short[,] water, short[,] surface,
                                  Vector2I[,] down, int[,] flow, bool[,] channel, bool[,] navigable,
                                  Vector2I[,] twin, bool[,] keep, bool[,] eyot, int navigableAt,
                                  bool[,] estuary, List<Vector2I> estuaries)
    {
        bool Axis(int x, int z) => channel[x, z] && navigable[x, z] && twin[x, z].X < 0;

        // The main stem: for each axis cell, the upstream axis cell carrying the most water.
        var stem = new Vector2I[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) stem[x, z] = new Vector2I(-1, -1);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Axis(x, z)) continue;
            Vector2I to = down[x, z];
            if (to.X < 0 || !Axis(to.X, to.Y)) continue;
            Vector2I had = stem[to.X, to.Y];
            if (had.X < 0 || flow[x, z] > flow[had.X, had.Y]) stem[to.X, to.Y] = new Vector2I(x, z);
        }

        var taken = new List<(Vector2I Cell, int Flow)>();
        var drowned = new List<Vector2I>();
        var marked = new List<Vector2I>();
        var here = new List<Vector2I>();

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            // A mouth: an axis cell with nothing downstream.
            if (!Axis(sx, sz) || down[sx, sz].X >= 0) continue;
            uint salt = (uint)(sx * 73856093 ^ sz * 19349663);
            if (Hash01(seed, 0xE5A0u ^ salt) > EstuaryChance) continue;

            int mouth = Math.Min(EstuaryMouthMax, EstuaryMouthMin
                + (int)(Hash01(seed, 0xE5A1u ^ salt) * (EstuaryMouthMax - EstuaryMouthMin + 1)));
            int length = Math.Max(4, (EstuaryLengthMin + (int)(Hash01(seed, 0xE5A2u ^ salt)
                * (EstuaryLengthMax - EstuaryLengthMin + 1))) * n / 96);

            taken.Clear();
            drowned.Clear();
            marked.Clear();
            var c = new Vector2I(sx, sz);
            int steps = 0;
            for (int s = 0; s <= length; s++)
            {
                float width = 2f + (mouth - 2) * MathF.Pow(1f - s / (float)length, 1.3f);
                float r = width * 0.5f;
                int reach = (int)MathF.Ceiling(r);
                int ground = surface[c.X, c.Y];
                int gentle = 0, steep = 0;
                here.Clear();
                for (int x = c.X - reach; x <= c.X + reach; x++)
                for (int z = c.Y - reach; z <= c.Y + reach; z++)
                {
                    if (!InBounds(n, x, z) || !land[x, z]) continue;
                    float dx = x - c.X, dz = z - c.Y;
                    if (dx * dx + dz * dz > r * r) continue;
                    if (channel[x, z])
                    {
                        gentle++;
                        if (!estuary[x, z]) { estuary[x, z] = true; marked.Add(new Vector2I(x, z)); }
                        continue;
                    }
                    if (keep[x, z] || water[x, z] != IslandData.NoLand) continue;
                    if (surface[x, z] > ground + EstuaryRise) { steep++; continue; }
                    gentle++;
                    here.Add(new Vector2I(x, z));
                }
                // The valley closes: the funnel ends here.
                if (gentle < EstuaryGentleShare * (gentle + steep)) break;

                foreach (Vector2I a in here)
                {
                    taken.Add((a, flow[a.X, a.Y]));
                    channel[a.X, a.Y] = true;
                    navigable[a.X, a.Y] = true;
                    twin[a.X, a.Y] = c;
                    flow[a.X, a.Y] = Math.Max(flow[a.X, a.Y], navigableAt);
                    estuary[a.X, a.Y] = true;
                    marked.Add(a);
                    if (eyot[a.X, a.Y]) { eyot[a.X, a.Y] = false; drowned.Add(a); }
                }
                steps++;

                // Up the stem; a drop of a fall's depth between two axis cells is a fall, and the funnel stops below it.
                Vector2I up = stem[c.X, c.Y];
                if (up.X < 0 || surface[up.X, up.Y] - surface[c.X, c.Y] >= FallDepth) break;
                c = up;
            }

            if (steps >= EstuaryMinSteps && taken.Count >= 4)
            {
                estuaries.Add(new Vector2I(sx, sz));
                continue;
            }
            // A wide mouth and no funnel: put it all back.
            foreach ((Vector2I a, int had) in taken)
            {
                channel[a.X, a.Y] = false;
                navigable[a.X, a.Y] = false;
                twin[a.X, a.Y] = new Vector2I(-1, -1);
                flow[a.X, a.Y] = had;
            }
            foreach (Vector2I a in drowned) eyot[a.X, a.Y] = true;
            foreach (Vector2I a in marked) estuary[a.X, a.Y] = false;
        }
    }
}
