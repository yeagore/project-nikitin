using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// Great lakes: a few flat patches on one rung flooded as one, on a landmass big
/// enough to carry it. The stage works per site, and a site is a patch except
/// here, where it is the union of two to five; everything after the union — the
/// rim as containment, the level from the lowest rim cell, the shore's wander,
/// the islet and the bathymetry — runs on it unchanged. About one Domain in ten.
/// </summary>
internal static partial class Lakes
{
    /// <summary>The Lakes knob a Domain needs before a great lake is on the dice at all.</summary>
    private const float GreatLakeMinWet = 0.30f;

    /// <summary>Cells of landmass under a great lake's seed patch: a 96² Single, not a 64² anything.</summary>
    private const int GreatLakeMinLandmass = 3600;

    /// <summary>Patches a great lake is made of, at most.</summary>
    private const int GreatLakeMaxPatches = 5;

    /// <summary>Share of the landmass the union of patches is grown to, rolled between these.</summary>
    private const float GreatLakeShareMin = 0.10f, GreatLakeShareMax = 0.18f;

    /// <summary>Pool cells a great lake must come out at after the shore's wander; smaller is an ordinary lake that happened to span a border.</summary>
    private const int GreatLakeMinPool = 100;

    /// <summary>Interior cells a seed patch needs before it is worth growing from.</summary>
    private const int GreatLakeSeedInterior = 40;

    /// <summary>
    /// How readily each character carries a great lake, at the top of the Lakes
    /// knob: open country most, the sculpted rock less, a dune field hardly.
    /// </summary>
    private static float GreatLakeShare(TerrainCharacter c) => c switch
    {
        TerrainCharacter.Plains => 0.80f,
        TerrainCharacter.Tablelands => 0.70f,
        TerrainCharacter.Downs => 0.70f,
        TerrainCharacter.Highlands => 0.50f,
        TerrainCharacter.Massif => 0.40f,
        TerrainCharacter.Karst => 0.40f,
        TerrainCharacter.Badlands => 0.30f,
        TerrainCharacter.Dunes => 0.15f,
        _ => 0f,
    };

    /// <summary>
    /// Whether this Domain gets a great lake, and where. Copies <paramref name="region"/>
    /// into <paramref name="site"/> and, when the dice and the country allow, relabels
    /// two to <see cref="GreatLakeMaxPatches"/> adjacent patches to one seed's id and
    /// returns it; −1 leaves every site a patch. The seed is a plain or basin with a
    /// broad interior, well inland, on a landmass of <see cref="GreatLakeMinLandmass"/>
    /// cells; the union grows by the largest eligible neighbour — plain, hills or basin,
    /// on the seed's rung, on the same landmass, not cut by a canyon — until it holds
    /// its rolled share of the landmass. The chance scales with the knob from
    /// <see cref="GreatLakeMinWet"/> up and with the character (<see cref="GreatLakeShare"/>).
    /// </summary>
    private static int GreatLakeSite(int seed, IslandParams p, bool[,] land, int[,] region, int count,
                                     RegionPlan[] plan, int[] interior, bool[] drained, float[,] toCoast,
                                     TerrainCharacter character, float wet, int[,] site)
    {
        int n = p.Size;
        Array.Copy(region, site, region.Length);

        if (wet < GreatLakeMinWet) return -1;
        float chance = GreatLakeShare(character) * FieldOps.SmoothStep(GreatLakeMinWet, 1f, wet);
        if (Hash01(seed, 0x6EA7u) >= chance) return -1;

        // Each patch's cells, the landmass it stands on and how far inland it lies.
        var mass = new int[n, n];
        var masses = new List<List<Vector2I>>();
        Flood.Label(n, (x, z) => land[x, z], mass, masses);

        var cells = new int[count];
        var massOf = new int[count];
        var inland = new float[count];
        Array.Fill(massOf, -1);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int r = region[x, z];
            cells[r]++;
            inland[r] += toCoast[x, z];
            if (massOf[r] < 0) massOf[r] = mass[x, z];
        }
        float farthest = 1f;
        for (int r = 0; r < count; r++)
        {
            if (cells[r] > 0) inland[r] /= cells[r];
            farthest = Math.Max(farthest, inland[r]);
        }

        // The seed: a big flat interior well inland, on a landmass with room, with a roll on it.
        int best = -1;
        float bestScore = 0f;
        for (int r = 0; r < count; r++)
        {
            if (drained[r] || interior[r] < GreatLakeSeedInterior) continue;
            if (plan[r].Type != LandformType.Plain && plan[r].Type != LandformType.Basin) continue;
            if (massOf[r] < 0 || masses[massOf[r]].Count < GreatLakeMinLandmass) continue;
            float score = interior[r] * (0.6f + inland[r] / farthest)
                          * (0.75f + 0.5f * Hash01(seed, 0x6EA8u ^ (uint)r * 2654435761u));
            if (score > bestScore) { bestScore = score; best = r; }
        }
        if (best < 0) return -1;

        // Grow: the largest eligible neighbour of the union each time (ties to the lower
        // id, so the set's order never matters), until the union holds its share of the
        // landmass or runs out of patches.
        HashSet<int>[] neighbours = Neighbours(land, region, count);
        int target = (int)(masses[massOf[best]].Count
                           * Mathf.Lerp(GreatLakeShareMin, GreatLakeShareMax, Hash01(seed, 0x6EA9u)));
        var members = new List<int> { best };
        int have = cells[best];
        while (members.Count < GreatLakeMaxPatches && have < target)
        {
            int pick = -1;
            foreach (int m in members)
            foreach (int nb in neighbours[m])
            {
                if (members.Contains(nb) || drained[nb] || massOf[nb] != massOf[best]) continue;
                if (plan[nb].Plateau != plan[best].Plateau) continue;
                if (plan[nb].Type is not (LandformType.Plain or LandformType.Hills or LandformType.Basin)) continue;
                if (pick < 0 || cells[nb] > cells[pick] || (cells[nb] == cells[pick] && nb < pick)) pick = nb;
            }
            if (pick < 0) break;
            members.Add(pick);
            have += cells[pick];
        }
        if (members.Count < 2) return -1;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && members.Contains(region[x, z])) site[x, z] = best;
        return best;
    }

    /// <summary>Cells of pool a site holds.</summary>
    private static int PoolArea(int n, int[,] site, bool[,] pool, int r)
    {
        int area = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (pool[x, z] && site[x, z] == r) area++;
        return area;
    }

    /// <summary>The first flooded cell of a site in scan order: what a great lake is listed by.</summary>
    private static Vector2I FirstCell(int n, int[,] site, short[,] water, int r)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (site[x, z] == r && water[x, z] != IslandData.NoLand) return new Vector2I(x, z);
        return new Vector2I(-1, -1);
    }
}
