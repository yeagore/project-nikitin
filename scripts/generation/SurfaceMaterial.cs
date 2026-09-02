namespace ProjectNikitin.Generation;

/// <summary>
/// What the top of a column is made of — a provisional reading of the habitat
/// vector, one byte per column in <see cref="IslandData.Material"/>. Not a biome.
/// </summary>
public enum SurfaceMaterial : byte
{
    /// <summary>Bare rock: the brink of a tall face, the broken parts of a rock landform, and the cold high ground.</summary>
    Stone = 0,

    /// <summary>Loose broken rock: talus under a tall face, the rougher parts of a rock landform, the alpine band.</summary>
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

    /// <summary>Watered ground between grass and heath. Appended last: the byte is stored by value.</summary>
    Meadow = 8,
}
