using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>Channels: the second cell of a navigable river, the eyots it splits round, and their beaches.</summary>
internal static partial class Rivers
{
    /// <summary>
    /// Puts a second cell alongside every navigable channel cell. Two-phase: the
    /// partners are collected during the scan and applied after it, so a cell
    /// widened this pass is never seen as channel by a later cell of the same scan.
    /// The lower perpendicular side wins; ties go to side −1.
    /// </summary>
    private static void Widen(int n, bool[,] land, short[,] water, short[,] surface,
                              int[,] flow, Vector2I[,] down, bool[,] channel,
                              bool[,] navigable, int navigableAt, Vector2I[,] twin,
                              bool[,] keep)
    {
        var added = new List<Vector2I>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!channel[x, z] || !navigable[x, z]) continue;

            Vector2I to = down[x, z];
            if (to.X < 0) continue;

            // Perpendicular to the way the water is going.
            int fx = to.X - x, fz = to.Y - z;
            int px = fz, pz = -fx;

            int bestX = -1, bestZ = -1, bestTop = int.MaxValue;
            for (int side = -1; side <= 1; side += 2)
            {
                int nx = x + px * side, nz = z + pz * side;
                if (!InBounds(n, nx, nz)) continue;
                if (!land[nx, nz] || channel[nx, nz]) continue;
                if (water[nx, nz] != IslandData.NoLand) continue;
                if (keep[nx, nz]) continue;
                // Never onto ground above the channel: that is a bank, and cutting it leaves a notch.
                if (surface[nx, nz] > surface[x, z]) continue;
                if (surface[nx, nz] >= bestTop) continue;

                bestTop = surface[nx, nz];
                bestX = nx;
                bestZ = nz;
            }
            if (bestX < 0) continue;

            added.Add(new Vector2I(bestX, bestZ));
            flow[bestX, bestZ] = Math.Max(flow[bestX, bestZ], navigableAt);
            // The pair is one river and holds one surface: the partner takes the axis's level.
            twin[bestX, bestZ] = new Vector2I(x, z);
        }

        foreach (Vector2I c in added)
        {
            channel[c.X, c.Y] = true;
            navigable[c.X, c.Y] = true;
        }
    }

    /// <summary>How often a navigable reach splits round an island, per candidate cell.</summary>
    private const float EyotChance = 0.22f;

    /// <summary>
    /// Splits a navigable river round an eyot: the reach is widened once more on
    /// the far side of the axis and the partner in the middle left dry, so the
    /// water runs both ways round a strip of land lying along the course. The
    /// first and last cell of the reach stay water, where the channels part and rejoin.
    /// </summary>
    private static void Braid(int seed, int n, bool[,] land, short[,] water, short[,] surface,
                              Vector2I[,] down, bool[,] channel, bool[,] navigable,
                              Vector2I[,] twin, bool[,] keep, bool[,] eyot)
    {
        // Widen records the partner's debt to the axis; the braid looks it up the other way round.
        var mate = new Vector2I[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) mate[x, z] = new Vector2I(-1, -1);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Vector2I a = twin[x, z];
            if (a.X >= 0) mate[a.X, a.Y] = new Vector2I(x, z);
        }

        var axis = new List<Vector2I>();
        var isle = new List<Vector2I>();
        var far = new List<Vector2I>();
        var spent = new bool[n, n];

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!channel[sx, sz] || !navigable[sx, sz]) continue;
            if (mate[sx, sz].X < 0) continue;                    // not an axis cell
            if (Hash01(seed, 0xE70Au ^ (uint)(sx * 73856093 ^ sz * 19349663)) > EyotChance)
                continue;

            axis.Clear();
            isle.Clear();
            far.Clear();
            var c = new Vector2I(sx, sz);
            int want = 4 + (int)(Hash01(seed, 0xE70Bu ^ (uint)(sx * 31 + sz)) * 4f);

            for (int step = 0; step < want; step++)
            {
                if (!InBounds(n, c.X, c.Y)) break;
                if (!channel[c.X, c.Y] || !navigable[c.X, c.Y] || spent[c.X, c.Y]) break;

                Vector2I t = mate[c.X, c.Y];
                if (t.X < 0 || spent[t.X, t.Y]) break;
                // The far bank, directly opposite the partner across the axis.
                var f = new Vector2I(2 * c.X - t.X, 2 * c.Y - t.Y);
                if (!InBounds(n, f.X, f.Y)) break;
                if (!land[f.X, f.Y] || channel[f.X, f.Y] || keep[f.X, f.Y]) break;
                if (water[f.X, f.Y] != IslandData.NoLand) break;
                // Never into a bank standing over the river: a notch, not a second channel.
                if (surface[f.X, f.Y] > surface[c.X, c.Y]) break;
                // The island is one piece: a course that turns can put two partners diagonally apart.
                if (isle.Count > 0)
                {
                    Vector2I had = isle[^1];
                    if (Math.Abs(had.X - t.X) + Math.Abs(had.Y - t.Y) != 1) break;
                }

                axis.Add(c);
                isle.Add(t);
                far.Add(f);
                c = down[c.X, c.Y];
            }

            // Two cells of island and a cell of water at each end, at least.
            if (isle.Count < 4) continue;

            for (int i = 0; i < isle.Count; i++)
            {
                spent[axis[i].X, axis[i].Y] = true;
                spent[isle[i].X, isle[i].Y] = true;
                if (i == 0 || i == isle.Count - 1) continue;

                Vector2I t = isle[i], f = far[i];
                channel[t.X, t.Y] = false;
                navigable[t.X, t.Y] = false;
                twin[t.X, t.Y] = new Vector2I(-1, -1);
                eyot[t.X, t.Y] = true;

                channel[f.X, f.Y] = true;
                navigable[f.X, f.Y] = true;
                twin[f.X, f.Y] = axis[i];
                spent[f.X, f.Y] = true;
            }
        }
    }

    /// <summary>
    /// Stands every eyot one slab clear of the river water round it. Its ground was
    /// floodplain, level with what became the bed, so left alone it would be a shoal.
    /// </summary>
    private static void Beach(int n, short[,] water, bool[,] river, short[,] surface,
                              bool[,] eyot)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!eyot[x, z]) continue;

            int around = int.MinValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (river[nx, nz] && water[nx, nz] != IslandData.NoLand)
                    around = Math.Max(around, water[nx, nz]);
            }
            if (around == int.MinValue) continue;
            surface[x, z] = (short)(around + 1);
        }
    }
}
