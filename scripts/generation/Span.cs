namespace ProjectNikitin.Generation;

/// <summary>
/// One contiguous vertical run of solid blocks in a terrain column,
/// <see cref="Bottom"/>..<see cref="Top"/> inclusive (block levels on the Y axis).
/// A column holds these bottom-up, disjoint and non-touching; the air gap
/// between two spans is an overhang / arch underside.
/// See docs/island-generation.md §2.
/// </summary>
public readonly record struct Span(short Bottom, short Top)
{
    public int Height => Top - Bottom + 1;
}
