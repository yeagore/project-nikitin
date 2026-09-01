using System.Collections.Generic;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// Gives the Domain and its parts names.
///
/// <para>Regions, districts and bodies of water are integers everywhere else in
/// the pipeline, which is right for the generator and wrong for everyone who has
/// to talk about the output. "The stranded piece is area 7" is a sentence nobody
/// can check against a picture; "the stranded piece is Harrowmere" is. It costs
/// nothing, it makes every debugging conversation shorter, and it is the first
/// place the setting shows up in the tools.</para>
///
/// <para>Deterministic, like everything else: the same seed names the same
/// island. The syllables are deliberately plain — this is scaffolding for the
/// culture layer to replace, not an attempt at the culture layer.</para>
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

    /// <summary>
    /// Names the Domain, every walk area big enough to be a district, and every
    /// body of water. Runs after the traversal analysis, which is what decides
    /// how many of each there are.
    /// </summary>
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
                             + Waters[(int)(FeatureHash(seed, 0x77Fu + (uint)i) % (uint)Waters.Length)]);
    }

    private static string Compose(int seed, uint salt, bool tail = true)
    {
        string head = Heads[(int)(FeatureHash(seed, salt) % (uint)Heads.Length)];
        if (!tail) return head;
        return head + Tails[(int)(FeatureHash(seed, salt ^ 0x5Bu) % (uint)Tails.Length)];
    }
}
