namespace ProjectNikitin.Generation;

/// <summary>
/// What a flooded column holds, per column. Water is the default and the only fluid that
/// behaves (rivers, ferries, fords); fluids never touch, even diagonally.
/// </summary>
public enum FluidKind : byte
{
    Water = 0,

    /// <summary>A thick violet standing ooze: no rivers, nothing sails, fords or drinks it. Inert until the biome layer says what it is for.</summary>
    Goo = 1,
}
