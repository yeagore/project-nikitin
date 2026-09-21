namespace ProjectNikitin.Generation;

/// <summary>
/// What the top of a column is made of — a provisional reading of the habitat
/// vector, one byte per column in <see cref="IslandData.Material"/>. Not a biome.
/// The living ground is a grid of warmth (frigid, cold, temperate, hot) against
/// moisture (dry, balanced, wet), with two cells for water in excess — murkearth on the
/// cold-to-cool half, muckearth on the warm-to-hot half — and floodearth along
/// hot water; the rest is rock, water, sand and snow. The byte is stored by
/// value, so new members are appended and nothing is renumbered.
/// </summary>
public enum SurfaceMaterial : byte
{
    /// <summary>Bare rock: the brink of a tall face, the broken parts of a rock landform, and the bed and shore of a goo pool.</summary>
    Stone = 0,

    /// <summary>Loose broken rock: talus under a tall face, the rougher parts of a rock landform.</summary>
    Scree = 1,

    /// <summary>Frozen ground: the extreme cold, and a mountain's top above its stone.</summary>
    Snow = 2,

    /// <summary>A beach, the crest of a dune, and ground in extreme heat.</summary>
    Sand = 3,

    /// <summary>The bed under a river or a lake, and nothing else — where the water over it is shallower than <see cref="Ooze"/> asks.</summary>
    Silt = 4,

    /// <summary>Temperate and wet: what you farm.</summary>
    Brownearth = 5,

    /// <summary>Cold and wet: sodden peat and coarse grass, the wettest ground of the cold row short of murkearth.</summary>
    Shadowearth = 6,

    /// <summary>Hot and dry, and the sculpted dry landforms: badlands, karst, sinkhole country.</summary>
    Dustearth = 7,

    /// <summary>Temperate and balanced.</summary>
    Blackearth = 8,

    /// <summary>Water in excess on cold-to-cool ground (warmth under 140, moisture 190 or more), in patches: peat and standing water.</summary>
    Murkearth = 9,

    /// <summary>Hot and wet, and only within a few cells of a river or a lake: the lush flat beside the water.</summary>
    Floodearth = 10,

    /// <summary>Temperate and dry: short grass and thin soil.</summary>
    Dryearth = 11,

    /// <summary>Cold and dry — and everything but murkearth where it is frigid (warmth under 85).</summary>
    Frostearth = 12,

    /// <summary>Hot and balanced, and hot and wet away from any water.</summary>
    Yellowearth = 13,

    /// <summary>Water in excess on warm-to-hot ground (warmth 140 or more, moisture 230 or more), on flat low ground within two cells of fresh water, in patches: reeds and standing puddles.</summary>
    Muckearth = 14,

    /// <summary>Cold and balanced: heather and rough grazing, the tier between frostearth and shadowearth.</summary>
    Bleachearth = 15,

    /// <summary>Hot and wet (moisture 200 or more, a higher bar than brownearth, since heat is the less forgiving side): lush ground away from the water, what yellowearth becomes when the wet holds.</summary>
    Redearth = 16,

    /// <summary>The bed under deep water: dark ooze wherever <c>Surfaces.DeepBed</c> slabs of water or more stand over a column, lake or river, and nowhere else.</summary>
    Ooze = 17,
}
