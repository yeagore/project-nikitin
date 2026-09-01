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
    /// The high-ground shape that suits a character. Plains want a gentle tilt or
    /// a broad flat; a Highland wants a spine or a pair of masses to hang its
    /// mountains on.
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
            // Badlands and dunes are country, not relief: they want a broad even
            // ground to spread over rather than a peak to climb.
            TerrainCharacter.Badlands => new[] { ReliefStyle.Plateau, ReliefStyle.Tilted },
            TerrainCharacter.Dunes => new[] { ReliefStyle.Tilted, ReliefStyle.Plateau },
            TerrainCharacter.Karst => new[]
                { ReliefStyle.Plateau, ReliefStyle.Tilted, ReliefStyle.OffsetPeak },
            _ => new[] { ReliefStyle.Ridge, ReliefStyle.TwinPeaks, ReliefStyle.OffsetPeak },
        };
        return pool[(int)(TerrainHash(seed, 0x5EED) % (uint)pool.Length)];
    }

    internal static ReliefStyle ResolveStyle(int seed, IslandParams p)
        => StyleFor(seed, ResolveCharacter(seed, p));

    /// <summary>
    /// The layouts <c>Auto</c> may roll, and how often. Weighted toward a single
    /// landmass: an archipelago is the interesting case, not the common one.
    ///
    /// The first six are the set the generator was built and audited on; the rest
    /// are the newer shapes, and <see cref="IslandParams.NewArrangements"/> takes
    /// them out of the pool in one move without taking them out of the code — a
    /// layout you can no longer roll is still a layout you can ask for by name in
    /// the lab.
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

    /// <summary>
    /// What <see cref="IslandParams.NewArrangements"/> and
    /// <see cref="IslandParams.NewLandforms"/> actually change, in numbers.
    ///
    /// Both flags gate <c>Auto</c>'s dice and nothing else, which is exactly why
    /// they read in the lab as a checkbox that does nothing: with an arrangement
    /// and a character named by hand there is no dice roll left to gate. These
    /// exist so the lab can say so — see <c>IslandLab.PoolNote</c>.
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

    /// <summary>How many characters are the four the pipeline was first audited on.</summary>
    private const int ClassicCharacters = 4;

    /// <summary>
    /// Which character an island is, with <c>Auto</c> resolved.
    /// <see cref="IslandParams.NewLandforms"/> keeps the sculpted ones out of the
    /// dice without keeping them out of the game — asking for one by name still
    /// builds it.
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
