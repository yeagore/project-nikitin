namespace ProjectNikitin.Generation;

/// <summary>
/// One contiguous run of solid slabs in a column, <see cref="Bottom"/>..<see cref="Top"/> inclusive
/// (slab indices). A column's spans are bottom-up, disjoint and non-touching; the gap between two is an overhang.
/// </summary>
public readonly record struct Span(short Bottom, short Top)
{
    public int Height => Top - Bottom + 1;
}
