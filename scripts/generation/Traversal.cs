using System;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>
/// Reads walkability off finished terrain and never changes it: what connects on
/// foot (a one-slab step is free), what connects once built, and what is level
/// enough to build on. The floods live in the partial files beside this one.
/// </summary>
public static partial class Traversal
{
    /// <summary>Below this, a walk area is broken ground rather than a place.</summary>
    public const int MinDistrictArea = 20;

    /// <summary>Smallest shelf a settlement could use, in cells.</summary>
    public const int MinShelfArea = 24;

    /// <summary>Narrowest shelf a settlement could use, in cells.</summary>
    public const int MinShelfWidth = 3;

    /// <summary>Neighbours a shelf cell may step against; two or more is a hillside.</summary>
    private const int ShelfSteps = 1;

    /// <summary>Value in <see cref="IslandData.Walk"/> / <see cref="IslandData.Reach"/> for a flooded column.</summary>
    public const int Water = -2;

    /// <summary>Tallest face a stair or hoist spans, in slabs: clears a mesa or basin rim, not a mountain flank.</summary>
    public const int InfrastructureStep = 8;

    /// <summary>Bridge span when the Domain says nothing; the real figure is <see cref="IslandData.BridgeSpan"/>.</summary>
    public const int DefaultBridgeSpan = (int)BridgeEase.Medium;

    /// <summary>Slabs a bridge's two ends may differ by: one free step onto a level deck and one off it.</summary>
    public const int MaxBridgeRise = 2;

    /// <summary>Widest gap a bridge spans over water, in cells: a deck over water has piers, and past three the thing you build is a ferry.</summary>
    private const int WaterBridgeSpan = 3;

    /// <summary>Ground this many slabs or more below a deck is a chasm, and spans like aether.</summary>
    private const int ChasmDrop = 5;

    /// <summary>Slabs a quay may stand above its water; higher is a cliff, not a landing.</summary>
    public const int MaxQuayRise = 2;

    /// <summary>Fills walk areas, water bodies, ferry berths, reach areas and shelves on <paramref name="d"/>, in that order.</summary>
    public static void Analyse(IslandData d)
    {
        BuildWalkAreas(d);
        BuildWaterBodies(d);
        BuildBerths(d);
        PruneBerths(d);
        BuildReachAreas(d);
        BuildShelves(d);
    }

    /// <summary>In-bounds land that is dry, or a ford (see <c>Rivers.MarkFords</c>); a stream is crossed nowhere else.</summary>
    public static bool Walkable(IslandData d, int x, int z)
    {
        int n = d.Size;
        if (!InBounds(n, x, z)) return false;
        if (!d.HasLand(x, z)) return false;
        if (d.WaterLevel[x, z] == IslandData.NoLand) return true;
        return d.Ford[x, z];
    }

    /// <summary>
    /// The level a column is crossed at: a stream's water surface, else the ground.
    /// Measuring a stream's bed would read a two-slab step at every bank.
    /// </summary>
    public static short CrossLevel(IslandData d, int x, int z)
    {
        if (d.River[x, z] && !d.Navigable[x, z]
            && d.WaterLevel[x, z] != IslandData.NoLand)
            return d.WaterLevel[x, z];
        return d.SurfaceLevel(x, z);
    }

    /// <summary>Water a ferry works on: standing water or a navigable river, never goo. A stream is forded for free.</summary>
    public static bool Sailable(IslandData d, int x, int z)
    {
        int n = d.Size;
        if (!InBounds(n, x, z)) return false;
        if (!d.HasLand(x, z) || d.WaterLevel[x, z] == IslandData.NoLand) return false;
        if (d.Fluid[x, z] != (byte)FluidKind.Water) return false;
        return !d.River[x, z] || d.Navigable[x, z];
    }

    /// <summary>
    /// Whether a level deck could run <paramref name="reach"/> cells from (x, z) in a
    /// cardinal direction. Aether and a chasm (ground <see cref="ChasmDrop"/> or more
    /// below the deck) are free; walkable ground in the way refuses the deck; any
    /// water under it caps the gap at min(<paramref name="span"/>, <see cref="WaterBridgeSpan"/>).
    /// </summary>
    public static bool DeckFits(IslandData d, int x, int z, int dx, int dz, int reach, int span)
    {
        int n = d.Size;
        int gap = reach - 1;
        if (gap < 1) return true;
        if (gap > span) return false;

        int fx = x + dx * reach, fz = z + dz * reach;
        int deck = Math.Min(CrossLevel(d, x, z), CrossLevel(d, fx, fz));
        bool overWater = false;

        for (int step = 1; step < reach; step++)
        {
            int mx = x + dx * step, mz = z + dz * step;
            if (!InBounds(n, mx, mz)) continue;
            if (!d.HasLand(mx, mz)) continue;                       // aether

            int head = d.WaterLevel[mx, mz] != IslandData.NoLand
                ? d.WaterLevel[mx, mz]
                : d.SurfaceLevel(mx, mz);
            if (head <= deck - ChasmDrop) continue;                 // a chasm under the deck

            if (Walkable(d, mx, mz)) return false;                  // ground in the way
            overWater = true;
        }
        return !overWater || gap <= Math.Min(span, WaterBridgeSpan);
    }

    /// <summary>
    /// Re-points <see cref="IslandData.Mainland"/> / <see cref="IslandData.Heartland"/> to
    /// the ground under the Entry apron: the mainland is where you land, not the biggest
    /// piece. Ids keep their area order. Run after <c>GatePlacement</c>.
    /// </summary>
    public static void AnchorOn(IslandData d, Vector2I cell)
    {
        if (!InBounds(d.Size, cell.X, cell.Y)) return;
        if (!Walkable(d, cell.X, cell.Y)) return;

        int walk = d.Walk[cell.X, cell.Y];
        int reach = d.Reach[cell.X, cell.Y];
        if (walk >= 0) d.Mainland = walk;
        if (reach >= 0) d.Heartland = reach;
    }
}
