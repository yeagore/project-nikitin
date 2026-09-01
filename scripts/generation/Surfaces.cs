using System;
using System.Collections.Generic;

using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// What the top of a column is made of. One byte per column, in
/// <see cref="IslandData.Material"/>.
///
/// <b>Not a biome.</b> A biome is a climate and a set of living things, and it
/// belongs to the Domain layer above this one. This is the ground itself — a
/// provisional reading of the habitat vector (<see cref="Habitat"/>), kept so
/// the island reads as a place in the lab before the biome layer exists. The
/// biome branch is expected to replace this mapping; the vectors are the part
/// meant to last.
/// </summary>
public enum SurfaceMaterial : byte
{
    /// <summary>Bare rock: a cliff face and its brink, and the cold high ground.</summary>
    Stone = 0,

    /// <summary>Loose broken rock: rugged country, and the alpine band.</summary>
    Scree = 1,

    /// <summary>The frozen top of what a mountain can be — see <see cref="IslandData.Warmth"/>.</summary>
    Snow = 2,

    /// <summary>A beach, and the crest of a dune.</summary>
    Sand = 3,

    /// <summary>River margin, lake shore, and the bed under standing water.</summary>
    Silt = 4,

    /// <summary>Well-watered low ground, within a few cells of water. What you farm.</summary>
    Grass = 5,

    /// <summary>Drier open country away from the water.</summary>
    Heath = 6,

    /// <summary>Dry, eroded ground: badlands, karst, sinkhole country — and parched interior.</summary>
    Dust = 7,

    /// <summary>
    /// Ordinary green country between grass and heath: watered, but not a river
    /// margin. Last because the byte is stored by value and the earlier members
    /// had numbers before this band existed.
    /// </summary>
    Meadow = 8,
}

/// <summary>
/// Classifies the finished surface, and collects the <b>feature anchors</b> —
/// the lists the content layer attaches things to.
///
/// <para>The anchors are the point of the exercise. A forest does not go "at
/// (43, 71)", it goes "on flat well-watered ground away from the coast"; coral
/// goes on a rim; vines go under an overhang; reeds go on a bank. If the
/// feature layer had to re-derive those conditions from the height field, every
/// content system would carry its own copy of the terrain rules and they would
/// drift. So generation answers the geometric questions once — where is the
/// coast, where are the brinks and the feet of the cliffs, where can you stand
/// at the water, where is a summit — and content reads the lists.</para>
///
/// <para>Every geometric answer is measured against
/// <see cref="IslandData.EffectiveLevel"/> — the water surface where a column
/// is flooded — because the anchors describe what a place <i>looks like</i>.
/// Measured against the bare ground, every bank of a navigable river was a
/// "cliff" on the strength of a bed three slabs down, and half the cliff list
/// was river margin.</para>
/// </summary>
internal static class Surfaces
{
    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };

    /// <summary>Slabs of visible face that make a cliff — the traversal's own "needs a hoist".</summary>
    private const int CliffFace = 3;

    // The provisional material thresholds, all against Habitat's byte axes.
    // Tuned by the audit's share table and the field PNGs, not by eye alone.

    /// <summary>Warmth below which ground is frozen: the top ~fifth of the mountain cap.</summary>
    private const int SnowAt = 64;

    /// <summary>Warmth below which nothing ordinary grows — the alpine band.</summary>
    private const int ColdAt = 110;

    /// <summary>Moisture above which dry ground is a wet margin even off a bank.</summary>
    private const int SiltAt = 205;

    /// <summary>Moisture above which ground is grass — roughly within three cells of water.</summary>
    private const int GrassAt = 140;

    /// <summary>Moisture above which ground is meadow — ordinary watered country.</summary>
    private const int MeadowAt = 60;

    /// <summary>Moisture above which ground is heath; below it, parched dust.</summary>
    private const int HeathAt = 10;

    /// <summary>Ruggedness at which temperate ground turns to scree (~5 slabs of local relief).</summary>
    private const int BrokenAt = 160;

    public static void Classify(IslandData d)
    {
        int n = d.Size;

        d.CoastCells.Clear();
        d.CliffCells.Clear();
        d.CliffFootCells.Clear();
        d.BankCells.Clear();
        d.Summits.Clear();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            short eff = d.EffectiveLevel(x, z);
            bool dry = d.WaterLevel[x, z] == IslandData.NoLand;

            bool coast = false, bank = false;
            int drop = 0, face = 0;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !d.HasLand(nx, nz))
                {
                    coast = true;
                    continue;
                }
                short ne = d.EffectiveLevel(nx, nz);
                drop = Math.Max(drop, eff - ne);
                face = Math.Max(face, ne - eff);

                if (dry && d.WaterLevel[nx, nz] != IslandData.NoLand
                    && d.Fluid[nx, nz] != (byte)FluidKind.Goo
                    && eff - d.WaterLevel[nx, nz] is >= 0 and <= 1)
                    bank = true;
            }

            if (coast) d.CoastCells.Add(new Vector2I(x, z));
            if (dry && drop >= CliffFace) d.CliffCells.Add(new Vector2I(x, z));
            if (dry && face >= CliffFace) d.CliffFootCells.Add(new Vector2I(x, z));
            if (bank && !d.Beach[x, z] && !d.Landings[x, z])
                d.BankCells.Add(new Vector2I(x, z));

            d.Material[x, z] = (byte)Pick(d, x, z, drop, face, bank);
        }

        FindSummits(d);
    }

    /// <summary>
    /// The highest dry cells of the genuinely high country, greedily spaced so a
    /// single massif does not spend the whole list on one ridge. "Genuinely
    /// high" is absolute — at least half of this footprint's mountain cap
    /// above the lowest ground — so a flat island honestly has no summits
    /// rather than a crown of local bumps.
    /// </summary>
    private static void FindSummits(IslandData d)
    {
        int n = d.Size;
        short low = short.MaxValue;
        var peaks = new List<Vector2I>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            short eff = d.EffectiveLevel(x, z);
            if (eff == IslandData.NoLand) continue;
            if (eff < low) low = eff;
            if (d.WaterLevel[x, z] == IslandData.NoLand) peaks.Add(new Vector2I(x, z));
        }
        if (peaks.Count == 0) return;

        float cap = Math.Max(8f, d.Size * (40f / 128f));
        int minRise = Math.Max(8, Mathf.RoundToInt(cap / 2f));
        int spacing = Math.Max(8, n / 8);

        peaks.Sort((a, b) => d.SurfaceLevel(b.X, b.Y).CompareTo(d.SurfaceLevel(a.X, a.Y)));

        foreach (Vector2I c in peaks)
        {
            if (d.SurfaceLevel(c.X, c.Y) - low < minRise) break;
            bool crowded = false;
            foreach (Vector2I had in d.Summits)
                if (Math.Abs(had.X - c.X) + Math.Abs(had.Y - c.Y) < spacing)
                {
                    crowded = true;
                    break;
                }
            if (!crowded) d.Summits.Add(c);
        }
    }

    /// <summary>
    /// The ground at one cell, in order of what overrides what: built and wet
    /// ground first; then rock, because a face is what it is made of, not what
    /// grows on it; then the cold, which silences everything below it; then the
    /// landform's own character; then moisture, which decides everything
    /// ordinary.
    /// </summary>
    private static SurfaceMaterial Pick(IslandData d, int x, int z, int drop, int face, bool bank)
    {
        if (d.Beach[x, z]) return SurfaceMaterial.Sand;
        if (d.WaterLevel[x, z] != IslandData.NoLand) return SurfaceMaterial.Silt;

        byte warmth = d.Warmth[x, z];

        // A visible face and the ground at its lip or foot is rock; frozen rock
        // wears snow. Measured on the effective surface, so a river bank is not
        // "rock" for standing over its own bed.
        if (drop >= CliffFace || face >= CliffFace)
            return warmth < SnowAt ? SurfaceMaterial.Snow : SurfaceMaterial.Stone;

        if (warmth < SnowAt) return SurfaceMaterial.Snow;
        if (warmth < ColdAt)
            return d.Ruggedness[x, z] >= 64 ? SurfaceMaterial.Scree : SurfaceMaterial.Stone;

        var form = (LandformType)d.Landform[x, z];
        if (form == LandformType.Dunes) return SurfaceMaterial.Sand;
        if (form is LandformType.Badlands or LandformType.Karst or LandformType.Sinkholes)
            return SurfaceMaterial.Dust;

        if (bank) return SurfaceMaterial.Silt;
        if (d.Ruggedness[x, z] >= BrokenAt) return SurfaceMaterial.Scree;

        byte moist = d.Moisture[x, z];
        if (moist >= SiltAt) return SurfaceMaterial.Silt;
        if (moist >= GrassAt) return SurfaceMaterial.Grass;
        if (moist >= MeadowAt) return SurfaceMaterial.Meadow;
        if (moist >= HeathAt) return SurfaceMaterial.Heath;
        return SurfaceMaterial.Dust;
    }
}
