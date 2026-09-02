using System.Collections.Generic;
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

    /// <summary>Names the Domain, each district-sized walk area (parallel to Areas) and each water body.</summary>
    public static void Give(int seed, IslandData d)
    {
        d.Name = Compose(seed, 0x4E1u);

        d.Districts.Clear();
        for (int i = 0; i < d.Areas.Count; i++)
            d.Districts.Add(d.Areas[i].IsDistrict
                ? Compose(seed, 0x9A0u + (uint)i * 2654435761u)
                : "");

        d.WaterNames.Clear();
        for (int i = 0; i < d.WaterBodies; i++)
            d.WaterNames.Add($"{Compose(seed, 0x77Eu + (uint)i * 40503u, tail: false)} "
                             + Waters[(int)(Hash(seed, 0x77Fu + (uint)i) % (uint)Waters.Length)]);
    }

    private static string Compose(int seed, uint salt, bool tail = true)
    {
        string head = Heads[(int)(Hash(seed, salt) % (uint)Heads.Length)];
        if (!tail) return head;
        return head + Tails[(int)(Hash(seed, salt ^ 0x5Bu) % (uint)Tails.Length)];
    }
}
