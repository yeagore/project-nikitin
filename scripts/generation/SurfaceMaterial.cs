namespace ProjectNikitin.Generation;

/// <summary>
/// What the top of a column is made of — a provisional reading of the habitat
/// vector, one byte per column in <see cref="IslandData.Material"/>. Not a biome.
/// The living ground is a three-by-three of warmth (cold, temperate, hot) against
/// moisture (dry, balanced, wet), with a cell past wet in each row — bog, marsh,
/// floodplain — where the water is in excess; the rest is rock, water, sand and
/// snow. The byte is stored by value, so new members are appended and nothing is
/// renumbered.
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

    /// <summary>The bed under a river or a lake, and nothing else.</summary>
    Silt = 4,

    /// <summary>Temperate and wet: what you farm.</summary>
    Grass = 5,

    /// <summary>Cold and balanced: heather and rough grazing.</summary>
    Moorland = 6,

    /// <summary>Hot and dry, and the sculpted dry landforms: badlands, karst, sinkhole country.</summary>
    Dust = 7,

    /// <summary>Temperate and balanced.</summary>
    Meadow = 8,

    /// <summary>Cold and wet, occasionally: peat and standing water in the moor.</summary>
    Bog = 9,

    /// <summary>Hot and wet, and only within a few cells of a river or a lake: the lush flat beside the water.</summary>
    Floodplain = 10,

    /// <summary>Temperate and dry: short grass and thin soil.</summary>
    Steppe = 11,

    /// <summary>Cold and dry.</summary>
    Tundra = 12,

    /// <summary>Hot and balanced, and hot and wet away from any water.</summary>
    Savanna = 13,

    /// <summary>Temperate and past wet, occasionally: flat low ground within two cells of fresh water, with moisture in excess — reeds and standing puddles where the grass gives out.</summary>
    Marsh = 14,
}
