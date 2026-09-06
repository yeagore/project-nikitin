namespace ProjectNikitin.Generation;

/// <summary>
/// What a Domain's magick <em>looks like</em>: the shape the Turing reaction in
/// <c>Magicks</c> settles into, which is a matter of where the reaction sits on
/// the Gray–Scott plane rather than of anything drawn afterwards. The pattern
/// kind and <see cref="IslandParams.MagickDensity"/> are the whole public face of
/// the layer; the reaction's six coefficients live behind them.
/// </summary>
public enum MagickPattern
{
    /// <summary>Choose one of the concrete patterns from the seed, evenly.</summary>
    Auto = 0,

    /// <summary>A fine dusting of small points, none of them more than a few cells across.</summary>
    Motes = 1,

    /// <summary>Round wells of magick standing well apart, each a landmark in its own right.</summary>
    Wells = 2,

    /// <summary>Filaments: worms of magick winding across the country, mostly unjoined.</summary>
    Veins = 3,

    /// <summary>A labyrinth — the veins joined up into one long convoluted corridor with inert walls.</summary>
    Labyrinth = 4,

    /// <summary>An open reticulated net, fine-strutted, with inert cells caught in its mesh.</summary>
    Lace = 5,

    /// <summary>The inverse: a Domain saturated with magick, with inert hollows punched through it.</summary>
    Hollows = 6,
}
