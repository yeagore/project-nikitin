using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// The bed under a lake. Flat, two or three slabs under the surface, as every
/// lake once was; or a bathymetry rolled per lake — a bowl that deepens toward
/// the middle, a shelf of shallows with a drop-off to a deep floor, a plunge
/// that is deep a cell from the shore — the floor set by how wide the pool is,
/// so a broad lake can be deep and a puddle cannot, and capped by the footprint.
/// Where the water is deepest is the lake's deep, an anchor. Small pools and
/// most ordinary lakes stay flat: not every lake is special.
/// </summary>
internal static partial class Lakes
{
    /// <summary>How a lake's bed runs from the shore to the middle.</summary>
    private enum Bathymetry : byte { Flat, Bowl, Shelf, Plunge }

    /// <summary>Slabs of water at the shore of a lake with a bathymetry, and under a flat lake at the least.</summary>
    private const int ShoreDepth = 2;

    /// <summary>The deepest a lake gets at 128², in slabs: five metres of water.</summary>
    private const int MaxLakeDepth = 20;

    /// <summary>The deepest a lake gets on this footprint: <see cref="MaxLakeDepth"/> scaled by the footprint, six at the least — 10, 15 and 20 on the three footprints.</summary>
    private static int DepthCap(int size) => Math.Max(6, size * MaxLakeDepth / 128);

    /// <summary>
    /// The profile a lake takes. A pool without an inside (nothing two cells from its
    /// edge) is flat; a great lake is never flat; a tarn is a plunge more often than
    /// not; the rest are flat two times in five.
    /// </summary>
    private static Bathymetry RollBathymetry(int seed, int r, LakeStyle style, int deepest, bool great)
    {
        if (deepest < 2) return Bathymetry.Flat;
        float roll = Hash01(seed, 0xBA7Eu ^ (uint)r * 2654435761u);
        if (great) return roll < 0.45f ? Bathymetry.Bowl : roll < 0.85f ? Bathymetry.Shelf : Bathymetry.Plunge;
        if (style == LakeStyle.Tarn)
            return roll < 0.30f ? Bathymetry.Flat : roll < 0.80f ? Bathymetry.Plunge : Bathymetry.Bowl;
        return roll < 0.40f ? Bathymetry.Flat : roll < 0.70f ? Bathymetry.Bowl
             : roll < 0.88f ? Bathymetry.Shelf : Bathymetry.Plunge;
    }

    /// <summary>
    /// The bed under every pool cell, as a level; <paramref name="profile"/> gets each
    /// site's roll. The pool's depth field (cells from its edge, an islet counting as
    /// edge) is what a profile is drawn on: a bowl runs from <see cref="ShoreDepth"/>
    /// at the edge to its floor at the middle; a shelf holds one slab on its outer
    /// ring and two over the shelf's width, then drops sheer to the floor; a plunge
    /// is <see cref="ShoreDepth"/> on the edge ring and the floor from the next. The
    /// floor grows two slabs per cell of the pool's inset (three for a plunge), so a
    /// bowl falls a slab every half cell, and is capped by <see cref="DepthCap"/>;
    /// noise breaks the contours where the bed is deep, more the deeper. A flat lake
    /// keeps the two-or-three roll it always had, so it is the lake it was.
    /// </summary>
    private static short[,] LakeBeds(int seed, int n, int[,] site, int count, bool[] wants, LakeStyle[] style,
                                     bool[,] pool, bool[,] islet, int[] level, int great, Bathymetry[] profile)
    {
        var wet = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) wet[x, z] = pool[x, z] && !islet[x, z];
        int[,] depth = PoolDepth(n, wet);

        var deepest = new int[count];
        Array.Fill(deepest, -1);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (wet[x, z]) deepest[site[x, z]] = Math.Max(deepest[site[x, z]], depth[x, z]);

        int cap = DepthCap(n);
        int Bound(int slabs, int atLeast) => Math.Min(cap, Math.Max(atLeast, slabs));

        var floor = new int[count];
        var shelf = new int[count];
        for (int r = 0; r < count; r++)
        {
            if (!wants[r] || deepest[r] < 0) continue;
            profile[r] = RollBathymetry(seed, r, style[r], deepest[r], r == great);
            switch (profile[r])
            {
                case Bathymetry.Flat:
                    floor[r] = ShoreDepth + (int)(Hash01(seed, 0x1A4Eu ^ (uint)r * 40503u) * 2f);
                    break;
                case Bathymetry.Bowl:
                    floor[r] = Bound(ShoreDepth + 2 * deepest[r], ShoreDepth + 2);
                    break;
                case Bathymetry.Shelf:
                    shelf[r] = Math.Clamp(1 + (int)(Hash01(seed, 0x5E1Fu ^ (uint)r * 40503u) * 3f), 1, deepest[r] - 1);
                    floor[r] = Bound(4 + 2 * deepest[r], ShoreDepth + 3);
                    break;
                case Bathymetry.Plunge:
                    floor[r] = Bound(4 + 3 * deepest[r], ShoreDepth + 4);
                    break;
            }
        }

        var relief = new Noise(seed + 2323, frequency: 0.22f, octaves: 2);
        var bed = new short[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!wet[x, z]) continue;
            int r = site[x, z];
            int d = depth[x, z];
            int slabs;
            bool wobble;
            switch (profile[r])
            {
                case Bathymetry.Bowl:
                    slabs = ShoreDepth + Mathf.RoundToInt((floor[r] - ShoreDepth) * (float)d / deepest[r]);
                    wobble = d >= 1;
                    break;
                case Bathymetry.Shelf:
                    slabs = d == 0 ? 1 : d <= shelf[r] ? ShoreDepth : floor[r];
                    wobble = d > shelf[r];
                    break;
                case Bathymetry.Plunge:
                    slabs = d == 0 ? ShoreDepth : floor[r];
                    wobble = d >= 2;
                    break;
                default:
                    slabs = floor[r];
                    wobble = false;
                    break;
            }
            if (wobble)
            {
                float amp = 1f + floor[r] / 10f;         // ±1 on a shallow floor, ±3 on the deepest
                slabs = Math.Clamp(slabs + Mathf.RoundToInt((relief.At(x, z) - 0.5f) * 2f * amp), 1, cap);
            }
            bed[x, z] = Terrain.SlabClamp(level[r] - slabs);
        }
        return bed;
    }

    /// <summary>
    /// The deepest cell of every lake with a bathymetry — the first in scan order where
    /// the bed ties — listed in site order. Read after the diagonal and shore passes, so
    /// it is water that stayed.
    /// </summary>
    private static void RecordDeeps(int n, int[,] site, int count, Bathymetry[] profile,
                                    short[,] water, short[,] surface, List<Vector2I> deeps)
    {
        var at = new Vector2I[count];
        var low = new int[count];
        Array.Fill(low, int.MaxValue);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (water[x, z] == IslandData.NoLand) continue;
            int r = site[x, z];
            if (profile[r] == Bathymetry.Flat || surface[x, z] >= low[r]) continue;
            low[r] = surface[x, z];
            at[r] = new Vector2I(x, z);
        }
        for (int r = 0; r < count; r++)
            if (low[r] != int.MaxValue) deeps.Add(at[r]);
    }
}
