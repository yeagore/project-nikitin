using System;
using System.Collections.Generic;

using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// What the top of a column is made of. One byte per column, in
/// <see cref="IslandData.Material"/>.
///
/// <b>Not a biome.</b> A biome is a climate and a set of living things, and it
/// belongs to the Domain layer above this one. This is the ground itself, derived
/// from what the terrain already knows — how high it is, how steep, how far from
/// water, and what landform it belongs to — so that the island reads as a place
/// rather than as a height field, and so the feature layer has something to
/// attach to.
/// </summary>
public enum SurfaceMaterial : byte
{
    /// <summary>Bare rock: a cliff top, a face, anything too steep to hold soil.</summary>
    Stone = 0,

    /// <summary>Loose broken rock on a steep slope.</summary>
    Scree = 1,

    /// <summary>The high, cold ground.</summary>
    Snow = 2,

    /// <summary>A beach, and the crest of a dune.</summary>
    Sand = 3,

    /// <summary>River margin and lake shore: the wet ground water leaves behind.</summary>
    Silt = 4,

    /// <summary>Well-watered low ground, within a few cells of water. What you farm.</summary>
    Grass = 5,

    /// <summary>Drier open country away from the water.</summary>
    Heath = 6,

    /// <summary>Dry, eroded ground: badlands, karst, sinkhole country.</summary>
    Dust = 7,

    /// <summary>
    /// Ordinary green country between the two: watered, but not a river margin.
    ///
    /// It is last because the byte is stored and read by value, and the earlier
    /// members had numbers before this band existed. It exists because
    /// <c>Damp</c> and <c>Dry</c> both used to return <see cref="Grass"/> — two
    /// thresholds, one answer, and an island with two kinds of ordinary ground
    /// rather than three.
    /// </summary>
    Meadow = 8,
}

/// <summary>
/// Classifies the finished surface, and collects the <b>feature anchors</b> —
/// the lists the content layer attaches things to.
///
/// <para>The anchors are the point of the exercise. A forest does not go "at
/// (43, 71)", it goes "on flat well-watered ground away from the coast"; coral
/// goes on a rim; vines go under an overhang. If the feature layer had to
/// re-derive those conditions from the height field, every content system would
/// carry its own copy of the terrain rules and they would drift. So generation
/// answers the geometric questions once — where is the coast, where are the
/// cliffs, where is there an overhang to hang from — and content reads the
/// lists.</para>
/// </summary>
internal static class Surfaces
{
    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };

    /// <summary>Cells from water before ground stops counting as well-watered.</summary>
    private const int Damp = 3;

    /// <summary>And before it stops counting as green at all.</summary>
    private const int Dry = 9;

    public static void Classify(IslandData d)
    {
        int n = d.Size;

        d.CoastCells.Clear();
        d.CliffCells.Clear();

        // Distance to the nearest standing water or watercourse, in cells.
        var wet = new int[n, n];
        var q = new Queue<Vector2I>();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            wet[x, z] = -1;
            if (!d.HasLand(x, z) || d.WaterLevel[x, z] == IslandData.NoLand) continue;
            wet[x, z] = 0;
            q.Enqueue(new Vector2I(x, z));
        }
        while (q.Count > 0)
        {
            Vector2I c = q.Dequeue();
            if (wet[c.X, c.Y] >= Dry) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!d.HasLand(nx, nz) || wet[nx, nz] >= 0) continue;
                wet[nx, nz] = wet[c.X, c.Y] + 1;
                q.Enqueue(new Vector2I(nx, nz));
            }
        }

        // The island's own height range, so "high" means high for this Domain
        // rather than high in slabs — a Plains island has a treeline too.
        short low = short.MaxValue, high = short.MinValue;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            short top = d.SurfaceLevel(x, z);
            if (top < low) low = top;
            if (top > high) high = top;
        }
        float range = Math.Max(1, high - low);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            short top = d.SurfaceLevel(x, z);

            int slope = 0;
            bool coast = false;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !d.HasLand(nx, nz))
                {
                    coast = true;
                    continue;
                }
                slope = Math.Max(slope, Math.Abs(d.SurfaceLevel(nx, nz) - top));
            }

            if (coast) d.CoastCells.Add(new Vector2I(x, z));
            if (slope >= 3) d.CliffCells.Add(new Vector2I(x, z));

            float height = (top - low) / range;
            var form = (LandformType)d.Landform[x, z];

            // <b>Unreached is drier than dry, not exactly dry.</b> The flood stops
            // expanding at `Dry`, so no cell ever comes back with a distance above
            // it — and a cell the flood never reached at all was being given
            // exactly `Dry`, which the classifier reads as still-green. Between
            // them those two facts made `Heath` unreachable: it was 0.0% of every
            // island in the audit, and the driest ground on a Domain came out the
            // same colour as a water meadow.
            int damp = wet[x, z] < 0 ? Dry + 1 : wet[x, z];

            d.Material[x, z] = (byte)Pick(d, x, z, form, height, slope, damp);
        }
    }

    /// <summary>
    /// The ground at one cell, in order of what overrides what: standing water and
    /// bare rock first, because neither cares how wet or how high it is; then the
    /// cold; then the landform's own character; then moisture, which is what
    /// decides everything ordinary.
    /// </summary>
    private static SurfaceMaterial Pick(IslandData d, int x, int z, LandformType form,
                                        float height, int slope, int damp)
    {
        if (d.Beach[x, z]) return SurfaceMaterial.Sand;
        if (d.WaterLevel[x, z] != IslandData.NoLand) return SurfaceMaterial.Silt;

        // Anything steep is what it is made of, not what grows on it. High rock
        // wears snow; a face lower down is bare.
        if (slope >= 3) return height > 0.80f ? SurfaceMaterial.Snow : SurfaceMaterial.Stone;
        if (slope == 2) return SurfaceMaterial.Scree;

        if (height > 0.80f) return SurfaceMaterial.Snow;
        if (height > 0.62f) return SurfaceMaterial.Scree;

        if (form == LandformType.Dunes) return SurfaceMaterial.Sand;
        if (form is LandformType.Badlands or LandformType.Karst or LandformType.Sinkholes)
            return SurfaceMaterial.Dust;

        // <b>Three bands, not two.</b> Both of the middle arms used to return
        // Grass, so `Damp` did nothing and the island had exactly two kinds of
        // ordinary ground: green within nine cells of water, heath beyond. Meadow
        // is the band the constant was named for — well-watered but not a margin.
        if (damp <= 1) return SurfaceMaterial.Silt;
        if (damp <= Damp) return SurfaceMaterial.Grass;
        if (damp <= Dry) return SurfaceMaterial.Meadow;
        return SurfaceMaterial.Heath;
    }
}
