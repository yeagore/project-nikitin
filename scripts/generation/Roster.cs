using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>What Auto rolls: the arrangement pool, the characters, the relief style.</summary>
internal static class Roster
{
    /// <summary>
    /// The high-ground shape that suits a character: plains want a tilt or a broad
    /// flat, a highland a spine or a pair of masses to hang its mountains on.
    /// </summary>
    private static ReliefStyle StyleFor(int seed, TerrainCharacter character)
    {
        ReliefStyle[] pool = character switch
        {
            TerrainCharacter.Plains => new[] { ReliefStyle.Tilted, ReliefStyle.Plateau },
            TerrainCharacter.Tablelands => new[]
                { ReliefStyle.Plateau, ReliefStyle.CentralPeak, ReliefStyle.Tilted },
            TerrainCharacter.Downs => new[]
                { ReliefStyle.OffsetPeak, ReliefStyle.TwinPeaks, ReliefStyle.Tilted },
            // Badlands and dunes are country, not relief: broad even ground, not a peak.
            TerrainCharacter.Badlands => new[] { ReliefStyle.Plateau, ReliefStyle.Tilted },
            TerrainCharacter.Dunes => new[] { ReliefStyle.Tilted, ReliefStyle.Plateau },
            TerrainCharacter.Karst => new[]
                { ReliefStyle.Plateau, ReliefStyle.Tilted, ReliefStyle.OffsetPeak },
            _ => new[] { ReliefStyle.Ridge, ReliefStyle.TwinPeaks, ReliefStyle.OffsetPeak },
        };
        return pool[(int)(TerrainHash(seed, 0x5EED) % (uint)pool.Length)];
    }

    /// <summary>The relief style for this island's character, with <c>Auto</c> resolved.</summary>
    internal static ReliefStyle ResolveStyle(int seed, IslandParams p)
        => StyleFor(seed, ResolveCharacter(seed, p));

    /// <summary>
    /// The layouts <c>Auto</c> may roll, and how often; weighted toward a single
    /// landmass. The first <see cref="ClassicArrangements"/> are the pool without
    /// <see cref="IslandParams.NewArrangements"/>. Order is load-bearing.
    /// </summary>
    private static readonly (IslandArrangement How, float Weight)[] ArrangementPool =
    {
        (IslandArrangement.Single, 34f),
        (IslandArrangement.Satellites, 10f),
        (IslandArrangement.Twins, 8f),
        (IslandArrangement.Triplets, 6f),
        (IslandArrangement.Archipelago, 6f),
        (IslandArrangement.BrokenRing, 5f),
        // --- newer shapes, gated by NewArrangements -------------------------
        (IslandArrangement.Ring, 4f),
        (IslandArrangement.Arc, 4f),
        (IslandArrangement.BrokenArc, 4f),
        (IslandArrangement.Atoll, 4f),
        (IslandArrangement.ThousandIsles, 4f),
        (IslandArrangement.Cross, 4f),
        (IslandArrangement.Fractal, 4f),
        (IslandArrangement.Shards, 3f),
        (IslandArrangement.TShape, 3f),
        (IslandArrangement.LShape, 3f),
        (IslandArrangement.BrokenCross, 3f),
        (IslandArrangement.BrokenT, 3f),
        (IslandArrangement.BrokenL, 3f),
        (IslandArrangement.BrokenFractal, 3f),
        (IslandArrangement.Rosette, 3f),
        (IslandArrangement.Star, 3f),
        (IslandArrangement.Square, 3f),
        (IslandArrangement.Rhomb, 3f),
        (IslandArrangement.NShape, 2f),
        (IslandArrangement.Quarters, 3f),
        (IslandArrangement.Halves, 3f),
        (IslandArrangement.Harmony, 2f),
        (IslandArrangement.Isthmus, 3f),
        (IslandArrangement.Reef, 3f),
    };

    /// <summary>How many of <see cref="ArrangementPool"/> are the audited originals.</summary>
    private const int ClassicArrangements = 6;

    /// <summary>How many characters, from 1, are the four the pipeline was first audited on.</summary>
    private const int ClassicCharacters = 4;

    /// <summary>
    /// How many layouts <c>Auto</c> can roll with <see cref="IslandParams.NewArrangements"/>
    /// on or off. The flag gates the dice and nothing else, which the lab's pool note says.
    /// </summary>
    public static int AutoArrangements(bool newer)
        => newer ? ArrangementPool.Length : ClassicArrangements;

    /// <inheritdoc cref="AutoArrangements"/>
    public static int AutoCharacters(bool newer)
        => newer ? Enum.GetValues<TerrainCharacter>().Length - 1 : ClassicCharacters;

    /// <summary>Whether <c>Auto</c> could only have rolled this layout with the flag on.</summary>
    public static bool IsNewerShape(IslandArrangement how)
    {
        for (int i = 0; i < ClassicArrangements; i++)
            if (ArrangementPool[i].How == how) return false;
        return how != IslandArrangement.Auto;
    }

    /// <inheritdoc cref="IsNewerShape(IslandArrangement)"/>
    public static bool IsNewerShape(TerrainCharacter c)
        => c != TerrainCharacter.Auto && (int)c > ClassicCharacters;

    /// <summary>The island's arrangement, with <c>Auto</c> a weighted pick over the pool.</summary>
    internal static IslandArrangement ResolveArrangement(int seed, IslandParams p)
    {
        if (p.Arrangement != IslandArrangement.Auto) return p.Arrangement;

        int upto = p.NewArrangements ? ArrangementPool.Length : ClassicArrangements;
        float total = 0f;
        for (int i = 0; i < upto; i++) total += ArrangementPool[i].Weight;

        float pick = TerrainHash01(seed, 0x7A1Du) * total;
        for (int i = 0; i < upto; i++)
        {
            pick -= ArrangementPool[i].Weight;
            if (pick <= 0f) return ArrangementPool[i].How;
        }
        return IslandArrangement.Single;
    }

    /// <summary>
    /// The island's character, with <c>Auto</c> resolved. <see cref="IslandParams.NewLandforms"/>
    /// keeps the sculpted characters out of the dice, not out of the game.
    /// </summary>
    internal static TerrainCharacter ResolveCharacter(int seed, IslandParams p)
    {
        if (p.Character != TerrainCharacter.Auto) return p.Character;
        int upto = p.NewLandforms
            ? Enum.GetValues<TerrainCharacter>().Length - 1      // minus Auto
            : ClassicCharacters;
        return (TerrainCharacter)(1 + (int)(TerrainHash(seed, 0xC7A2) % (uint)upto));
    }
}
