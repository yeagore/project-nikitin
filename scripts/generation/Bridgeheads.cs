using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>Levelling the two banks of every crossing, and recording the crossings as built.</summary>
internal static class Bridgeheads
{
    /// <summary>Slabs of disagreement between two banks that levelling will still close; beyond it the crossing is left alone rather than notching the coast.</summary>
    private const int MaxBridgeheadDrop = 8;

    /// <summary>Cells either side of a bridgehead that come down with it.</summary>
    private const int BridgeheadPad = 1;

    /// <summary>
    /// Brings the two ends of every crossing to one level: a bridge is a run of slabs at a single level.
    /// It only ever lowers, so the settle loop cleans up the step it leaves, and never touches ground
    /// beside water, since cutting a shore down is how you empty a lake. Returns whether anything moved.
    /// </summary>
    internal static bool LevelBridgeheads(bool[,] land, short[,] surface, short[,] water,
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

            short target = Terrain.SlabClamp(Math.Min(la, lb));
            moved |= FlattenPad(land, surface, water, region, plan, a, target, n);
            moved |= FlattenPad(land, surface, water, region, plan, b, target, n);
        }
        return moved;
    }

    /// <summary>
    /// Lowers the pad round a bridgehead to <paramref name="target"/>: only Plain,
    /// Hills or Dunes ground (the one-slab landforms; a dune bridgehead left alone
    /// stood three slabs over its partner), never below a basin's escarpment.
    /// </summary>
    private static bool FlattenPad(bool[,] land, short[,] surface, short[,] water,
                                   int[,] region, RegionPlan[] plan,
                                   Vector2I c, short target, int n)
    {
        bool moved = false;
        for (int dx = -BridgeheadPad; dx <= BridgeheadPad; dx++)
        for (int dz = -BridgeheadPad; dz <= BridgeheadPad; dz++)
        {
            int x = c.X + dx, z = c.Y + dz;
            if (!InBounds(n, x, z)) continue;
            if (!land[x, z] || surface[x, z] <= target) continue;
            if (NearWater(water, n, x, z)) continue;
            if (plan[region[x, z]].Type is not (LandformType.Plain or LandformType.Hills or LandformType.Dunes))
                continue;
            if (target < StepGrammar.BasinFloorNear(land, surface, region, plan, n, x, z)) continue;
            surface[x, z] = target;
            moved = true;
        }
        return moved;
    }

    /// <summary>Whether a cell or any of its eight neighbours holds standing water.</summary>
    private static bool NearWater(short[,] water, int n, int x, int z)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int nx = x + dx, nz = z + dz;
            if (!InBounds(n, nx, nz)) continue;
            if (water[nx, nz] != IslandData.NoLand) return true;
        }
        return false;
    }

    /// <summary>Records each crossing as it finally stands: the deck halfway between the banks (so each end is a one-slab step) and the cells of nothing it covers.</summary>
    internal static void RecordCrossings(IslandData d, List<(Vector2I A, Vector2I B)> pairs)
    {
        foreach (var (a, b) in pairs)
        {
            if (!d.HasLand(a.X, a.Y) || !d.HasLand(b.X, b.Y)) continue;

            int la = Traversal.CrossLevel(d, a.X, a.Y);
            int lb = Traversal.CrossLevel(d, b.X, b.Y);
            short deck = Terrain.SlabClamp(Mathf.RoundToInt((la + lb) * 0.5f));
            int span = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)) - 1;
            d.Bridges.Add(new Crossing(a, b, deck, span));
        }
    }
}
