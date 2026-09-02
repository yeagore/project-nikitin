namespace ProjectNikitin.Generation;

/// <summary>
/// The grid's neighbourhoods. The order of the four steps (+X, −X, +Z, −Z) is
/// the tie-breaker in every flood, sweep and scan that walks them — keep it.
/// </summary>
internal static class Grid
{
    public static readonly int[] Dx = { 1, -1, 0, 0 };
    public static readonly int[] Dz = { 0, 0, 1, -1 };

    /// <summary>King moves in compass order: E, NE, N, NW, W, SW, S, SE.</summary>
    public static readonly int[] Dx8 = { 1, 1, 0, -1, -1, -1, 0, 1 };
    public static readonly int[] Dz8 = { 0, 1, 1, 1, 0, -1, -1, -1 };

    public static bool InBounds(int n, int x, int z) => x >= 0 && z >= 0 && x < n && z < n;
}
