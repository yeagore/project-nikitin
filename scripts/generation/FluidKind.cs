namespace ProjectNikitin.Generation;

/// <summary>
/// What a body of standing fluid is made of.
///
/// <para><b>Per body, not per Domain.</b> The first `FluidKind` was a
/// Domain-wide dropdown — every watercourse on the island was water, or all of
/// it was lava — and it was removed 2026-08-31 because it was two `if`s with
/// nothing visible behind them. This one is a property of each flooded column,
/// so one Domain can hold water and something else at once, which is what the
/// biome layer will actually want.</para>
///
/// <para>Water is the default and the only fluid that behaves: rivers are
/// water, ferries sail water, fords cross water. Everything else is scenery
/// with a chemistry the content layer will give meaning to later. <b>Fluids do
/// not mix</b> — a goo pool is placed so that no water stands within a king's
/// move of it, and the rivers are routed around it.</para>
/// </summary>
public enum FluidKind : byte
{
    Water = 0,

    /// <summary>
    /// A thick, violet, standing ooze. It makes no rivers — nothing flows —
    /// and nothing sails, fords or drinks it. The first non-water fluid, kept
    /// deliberately inert until the biome layer says what it is for.
    /// </summary>
    Goo = 1,
}
