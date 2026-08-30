namespace ProjectNikitin.Generation;

/// <summary>
/// Output of <see cref="IslandGenerator"/>: terrain as a per-column list of
/// solid <see cref="Span"/> runs over a square footprint, plus metadata that
/// later stages fill in. All Y values are <b>slab indices</b> — multiply by
/// <see cref="Terrain.SlabHeight"/> for world units. See
/// docs/island-generation.md §2.
/// </summary>
public sealed class IslandData
{
    /// <summary>Sentinel returned by the level accessors for an empty column.</summary>
    public const short NoLand = short.MinValue;

    public int Size { get; }

    /// <summary>
    /// <c>[x, z]</c> → the column's spans (bottom-up, disjoint, non-touching),
    /// with bounds as slab indices. <c>null</c> or empty means no land.
    /// </summary>
    public Span[,][] Spans { get; }

    /// <summary>Surface material id of the top span. Single tier for now.</summary>
    public byte[,] Material { get; }

    /// <summary>
    /// The <see cref="LandformType"/> of the region this column belongs to. Drives
    /// the dev lab's landform view, and is what settlement placement and pathing
    /// will want to read rather than re-deriving slopes.
    /// </summary>
    public byte[,] Landform { get; }

    /// <summary>
    /// Top slab of standing water in a column, or <see cref="NoLand"/> for dry.
    /// Water occupies <c>SurfaceLevel+1 … WaterLevel</c>, so it is a level rather
    /// than a volume — one value per column, and no simulation.
    /// </summary>
    public short[,] WaterLevel { get; }

    /// <summary>Stage 1 land mask, kept for debugging / later stages.</summary>
    public bool[,] Land { get; }

    /// <summary>
    /// Which landform region each column belongs to, or <c>-1</c> for no land.
    /// Regions are the patches the island is stitched from.
    /// </summary>
    public int[,] Region { get; }

    /// <summary>The style actually used, with <c>Auto</c> already resolved.</summary>
    public ReliefStyle Style { get; internal set; }

    /// <summary>The character actually used, with <c>Auto</c> already resolved.</summary>
    public TerrainCharacter Character { get; internal set; }

    public IslandData(int size)
    {
        Size = size;
        Spans = new Span[size, size][];
        Material = new byte[size, size];
        Landform = new byte[size, size];
        Land = new bool[size, size];
        Region = new int[size, size];
        WaterLevel = new short[size, size];
        for (int x = 0; x < size; x++)
        for (int z = 0; z < size; z++) WaterLevel[x, z] = NoLand;
    }

    public bool HasLand(int x, int z) => Spans[x, z] is { Length: > 0 };

    /// <summary>Top slab of the highest span, or <see cref="NoLand"/>.</summary>
    public short SurfaceLevel(int x, int z)
        => HasLand(x, z) ? Spans[x, z][^1].Top : NoLand;

    /// <summary>Bottom slab of the lowest span, or <see cref="NoLand"/>.</summary>
    public short KeelLevel(int x, int z)
        => HasLand(x, z) ? Spans[x, z][0].Bottom : NoLand;
}
