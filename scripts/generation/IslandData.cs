namespace ProjectNikitin.Generation;

/// <summary>
/// Output of <see cref="IslandGenerator"/>: terrain as a per-column list of
/// solid <see cref="Span"/> runs over a square footprint, plus metadata that
/// later stages fill in. See docs/island-generation.md §2.
/// </summary>
public sealed class IslandData
{
    /// <summary>Sentinel returned by the level accessors for an empty column.</summary>
    public const short NoLand = short.MinValue;

    public int Size { get; }

    /// <summary>
    /// <c>[x, z]</c> → the column's spans, bottom-up, disjoint, non-touching.
    /// <c>null</c> or empty means no land in that column.
    /// </summary>
    public Span[,][] Spans { get; }

    /// <summary>Surface material id of the top span. Single tier for now.</summary>
    public byte[,] Material { get; }

    /// <summary>Stage 1 land mask, kept for debugging / later stages.</summary>
    public bool[,] Land { get; }

    public IslandData(int size)
    {
        Size = size;
        Spans = new Span[size, size][];
        Material = new byte[size, size];
        Land = new bool[size, size];
    }

    public bool HasLand(int x, int z) => Spans[x, z] is { Length: > 0 };

    /// <summary>Top of the highest span, or <see cref="NoLand"/>.</summary>
    public short SurfaceLevel(int x, int z)
        => HasLand(x, z) ? Spans[x, z][^1].Top : NoLand;

    /// <summary>Bottom of the lowest span, or <see cref="NoLand"/>.</summary>
    public short KeelLevel(int x, int z)
        => HasLand(x, z) ? Spans[x, z][0].Bottom : NoLand;
}
