using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>Gentle coasts stepped down a slab per cell.</summary>
internal static class Beaches
{
    /// <summary>
    /// Steps down onto a beach, and how many cells of coast one takes.
    ///
    /// A Domain's coast is a cliff to the keel everywhere, which is why every
    /// shoreline reads the same. Where the ground arrives at the rim gently — a
    /// plain, hills or dunes, level with its neighbours — the outermost cells step
    /// down a slab instead, and that one slab is the difference between land that
    /// stops and land that *meets* the aether. It is free-step ground, so nothing
    /// about walking changes; it gives the silhouette a softer edge where the
    /// terrain earns one, and the content layer a shoreline anchor
    /// (<see cref="IslandData.Beach"/>). Berth placement does <b>not</b> read it
    /// — a quay goes where the water divides the island, beach or no beach.
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
    internal static void MakeBeaches(bool[,] land, short[,] surface, short[,] water,
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
                if (InBounds(n, nx, nz) && land[nx, nz]) continue;
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
                if (!InBounds(n, nx, nz)) continue;
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
                if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
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
            surface[x, z] = Terrain.SlabClamp(surface[x, z] - drop[x, z]);
            beach[x, z] = true;
        }
    }
}
