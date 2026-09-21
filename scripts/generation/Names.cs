using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// Names the Domain, its districts and its water — deterministic per seed.
/// Placeholder syllables for the culture layer to replace.
/// </summary>
internal static class Names
{
    private static readonly string[] Heads =
    {
        "Ash", "Bram", "Cair", "Dun", "El", "Far", "Grim", "Hal", "Ing", "Kel",
        "Lin", "Mar", "Nor", "Orm", "Pel", "Quen", "Rath", "Sel", "Tor", "Ul",
        "Ver", "Wold", "Yr", "Zan",
    };

    private static readonly string[] Tails =
    {
        "bury", "combe", "dale", "fell", "ford", "garth", "holm", "hope", "keld",
        "mere", "moor", "ness", "reach", "scar", "stead", "thwaite", "vale", "wick",
    };

    private static readonly string[] Waters =
    {
        "Tarn", "Mere", "Water", "Loch", "Pool", "Flood", "Lade", "Race",
    };

    /// <summary>What a great lake is called instead: an inland sea, or a word with room in it.</summary>
    private static readonly string[] GreatWaters = { "Sea", "Sea", "Deep", "Broad", "Loch" };

    /// <summary>Rolls a name may take before a Domain settles for a repeat: the pool is finite, the patience is not free.</summary>
    private const int Tries = 16;

    /// <summary>
    /// Names the Domain, each district-sized walk area (parallel to Areas) and each
    /// water body. No two names on one Domain are the same: a water name is a head and
    /// a water word, 24 by 8, so on a Domain with ten bodies a plain roll repeats itself
    /// about one time in five — and two lakes in sight of each other called the same
    /// thing is the first thing the eye finds.
    /// </summary>
    public static void Give(int seed, IslandData d)
    {
        var taken = new HashSet<string>();
        d.Name = Compose(seed, 0x4E1u);
        taken.Add(d.Name);

        d.Districts.Clear();
        for (int i = 0; i < d.Areas.Count; i++)
        {
            if (!d.Areas[i].IsDistrict) { d.Districts.Add(""); continue; }
            int at = i;
            d.Districts.Add(Unique(taken, bump => Compose(seed, 0x9A0u + (uint)at * 2654435761u + bump * 0x1F1u)));
        }

        // A great lake takes a grander word; its body is read off the cell that lists it.
        var great = new HashSet<int>();
        foreach (Vector2I c in d.GreatLakes)
            if (d.WaterBody[c.X, c.Y] >= 0) great.Add(d.WaterBody[c.X, c.Y]);

        d.WaterNames.Clear();
        for (int i = 0; i < d.WaterBodies; i++)
        {
            int at = i;
            d.WaterNames.Add(Unique(taken, bump => Water(seed, (uint)at, bump, great.Contains(at))));
        }
    }

    /// <summary>
    /// The first roll nothing else answers to, each attempt salted afresh; the first
    /// roll again where the Domain has more water than the pool has names, which is a
    /// repeat and better than a loop.
    /// </summary>
    private static string Unique(HashSet<string> taken, Func<uint, string> roll)
    {
        for (uint bump = 0; bump < Tries; bump++)
        {
            string name = roll(bump);
            if (taken.Add(name)) return name;
        }
        return roll(0);
    }

    /// <summary>A body of water: a head and a water word, both salted by the attempt; a great lake's word from the grander list.</summary>
    private static string Water(int seed, uint i, uint bump, bool great = false)
    {
        string[] words = great ? GreatWaters : Waters;
        return $"{Compose(seed, 0x77Eu + i * 40503u + bump * 0x2B3u, tail: false)} "
               + words[(int)(Hash(seed, 0x77Fu + i + bump * 0x2B3u) % (uint)words.Length)];
    }

    private static string Compose(int seed, uint salt, bool tail = true)
    {
        string head = Heads[(int)(Hash(seed, salt) % (uint)Heads.Length)];
        if (!tail) return head;
        return head + Tails[(int)(Hash(seed, salt ^ 0x5Bu) % (uint)Tails.Length)];
    }
}
