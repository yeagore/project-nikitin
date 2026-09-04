using System.Collections.Generic;

using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Output of <see cref="IslandGenerator"/>: terrain as a per-column list of solid
/// <see cref="Span"/> runs over a square footprint, plus what later stages fill in.
/// All Y values are slab indices — multiply by <see cref="Terrain.SlabHeight"/> for world units.
/// </summary>
public sealed class IslandData
{
    // ---- model ----

    /// <summary>Sentinel returned by the level accessors for an empty column.</summary>
    public const short NoLand = short.MinValue;

    public int Size { get; }

    /// <summary>
    /// The parameters the island was actually built with: every knob left at
    /// <see cref="IslandParams.Auto"/> resolved to the value the seed rolled, and the
    /// altitude bounds applied. What the lab reports where the preset said Auto.
    /// </summary>
    public IslandParams Settings { get; init; } = null!;

    /// <summary>
    /// <c>[x, z]</c> → the column's spans, bottom-up, disjoint and non-touching, so
    /// <c>Spans[x, z][0]</c> is the lowest. <c>null</c> or empty means no land.
    /// </summary>
    public Span[,][] Spans { get; }

    /// <summary>The land mask the footprint stage produced.</summary>
    public bool[,] Land { get; }

    /// <summary>Which landform region (patch) each column belongs to, or <c>-1</c> for no land.</summary>
    public int[,] Region { get; }

    /// <summary>The <see cref="LandformType"/> of the region this column belongs to.</summary>
    public byte[,] Landform { get; }

    /// <summary>Columns a canyon was cut through: a deliberate cliff on a border the rules would otherwise bind.</summary>
    public bool[,] Canyon { get; }

    /// <summary>Columns inside a pass — a saddle cut so a cliff border has one place to walk across.</summary>
    public bool[,] Pass { get; }

    /// <summary>The centre of each pass. Usually none or one.</summary>
    public List<Vector2I> Passes { get; } = new();

    // ---- water ----

    /// <summary>
    /// Top slab of standing water in a column, or <see cref="NoLand"/> for dry.
    /// Water occupies <c>SurfaceLevel+1 … WaterLevel</c>: a level, not a volume.
    /// </summary>
    public short[,] WaterLevel { get; }

    /// <summary>The <see cref="FluidKind"/> of a flooded column; meaningful only where <see cref="WaterLevel"/> is set. Fluids never touch, even diagonally.</summary>
    public byte[,] Fluid { get; }

    /// <summary>Columns carrying a watercourse; <see cref="WaterLevel"/> holds the surface. A stream is one slab deep and fordable.</summary>
    public bool[,] River { get; }

    /// <summary>River columns two cells wide — navigable, and too wide to wade.</summary>
    public bool[,] Navigable { get; }

    /// <summary>Drainage accumulation: how many cells upstream drain through this one.</summary>
    public int[,] Flow { get; }

    /// <summary>Waterfalls: drops of three slabs or more along a channel, and every channel reaching the rim.</summary>
    public List<Fall> Falls { get; } = new();

    /// <summary>Stream cells crossable on foot; a stream is an obstacle everywhere else (<see cref="Rivers.FordSpacing"/>).</summary>
    public bool[,] Ford { get; }

    /// <summary>Coast cells stepped down onto a beach, where the ground meets the aether gently.</summary>
    public bool[,] Beach { get; }

    /// <summary>
    /// Body-of-water id per flooded column, or <c>-1</c>. Two columns share an id
    /// exactly when a hull could go between them: a waterfall cuts a body in two.
    /// </summary>
    public int[,] WaterBody { get; }

    /// <summary>How many separate bodies of water the Domain has.</summary>
    public int WaterBodies { get; internal set; }

    /// <summary>Dormant hook for the biome layer; nothing fills it today.</summary>
    public List<Geyser> Geysers { get; } = new();

    // ---- works ----

    /// <summary>Bridge sites: one <see cref="Crossing"/> per link, enough to join every landmass. Both banks are levelled.</summary>
    public List<Crossing> Bridges { get; } = new();

    /// <summary>Cells of gap one bridge may span on this Domain, from <see cref="IslandParams.Crossings"/>.</summary>
    public int BridgeSpan { get; internal set; } = Traversal.DefaultBridgeSpan;

    /// <summary>Ferry berths that survived pruning: a quay cell and the water in front of it.</summary>
    public List<FerryBerth> Berths { get; } = new();

    /// <summary>The quay cell of every berth, for the lab overlay and the audit.</summary>
    public bool[,] Ferry { get; }

    /// <summary>Berth sites found before pruning. Diagnostic: with <see cref="Berths"/>, says whether the pruning is too hungry.</summary>
    public int BerthSites { get; internal set; }

    // ---- traversal ----

    /// <summary>
    /// Walk-area index per column (into <see cref="Areas"/>), <see cref="Traversal.Water"/>
    /// for a flooded column, <c>-1</c> for no land. Same id ⇔ walkable between without works.
    /// </summary>
    public int[,] Walk { get; }

    /// <summary>Every walkable area, largest first, so <c>Areas[0]</c> is the mainland. See <see cref="WalkArea.IsDistrict"/>.</summary>
    public List<WalkArea> Areas { get; } = new();

    /// <summary>Index of the largest walkable area, or <c>-1</c> if there is no land.</summary>
    public int Mainland { get; internal set; } = -1;

    /// <summary>As <see cref="Walk"/>, for a player who can build: same id when a stair, hoist or bridge could join the columns.</summary>
    public int[,] Reach { get; }

    /// <summary>Every infrastructure-reachable area, largest first.</summary>
    public List<WalkArea> Reaches { get; } = new();

    /// <summary>Index of the largest reachable area — the heartland.</summary>
    public int Heartland { get; internal set; } = -1;

    /// <summary>Which <see cref="Shelf"/> a column belongs to, or <c>-1</c> for none.</summary>
    public int[,] ShelfId { get; }

    /// <summary>Level ground: one entry per contiguous same-level patch big enough to settle on.</summary>
    public List<Shelf> Shelves { get; } = new();

    // ---- gates and roads ----

    /// <summary>The Domain's Gates: one <see cref="GateRole.Entry"/> and one to three <see cref="GateRole.Exit"/>, at most one per edge.</summary>
    public List<Gate> Gates { get; } = new();

    /// <summary>Ground the Gates are served by: each Gate's levelled 1 × 3 landing strip, running inland.</summary>
    public bool[,] Landings { get; }

    /// <summary>The least-works road from the Entry to each Exit. See <see cref="Passage"/>.</summary>
    public List<Passage> Passages { get; } = new();

    /// <summary>Whether any road climbs a flight — five elevators inside fifteen cells (<see cref="Passage.Flights"/>). Hard country, not a fault.</summary>
    public bool Rough { get; internal set; }

    // ---- habitat and anchors ----
    // Five bytes per column, filled by Habitat.Measure: the measurable growing
    // conditions the biome layer will combine, kept as separate axes.

    /// <summary>0 parched … 255 waterside: nearness to fresh water (goo waters nothing), decayed over ~15 cells and wobbled by noise.</summary>
    public byte[,] Moisture { get; }

    /// <summary>255 warm lowland … 0 frozen. Absolute — a fixed lapse per slab climbed — not normalised per island.</summary>
    public byte[,] Warmth { get; }

    /// <summary>0 dead flat … 255 broken: local relief of the effective surface within two cells.</summary>
    public byte[,] Ruggedness { get; }

    /// <summary>0 sheltered … 255 windswept: how open the cell is to the wind from <see cref="WindFrom"/>.</summary>
    public byte[,] Exposure { get; }

    /// <summary>Cells of land between this column and the aether, capped at 255.</summary>
    public byte[,] RimDistance { get; }

    /// <summary>Provisional <see cref="SurfaceMaterial"/> of each column's top, mapped from the habitat vector by <c>Surfaces</c>.</summary>
    public byte[,] Material { get; }

    /// <summary>Land cells with aether beside them — the rim.</summary>
    public List<Vector2I> CoastCells { get; } = new();

    /// <summary>Cliff brinks: dry cells whose <see cref="EffectiveLevel"/> stands three slabs or more above a neighbour's.</summary>
    public List<Vector2I> CliffCells { get; } = new();

    /// <summary>Cliff feet: dry cells with a neighbour's effective surface three slabs or more above them.</summary>
    public List<Vector2I> CliffFootCells { get; } = new();

    /// <summary>Banks: dry cells beside water (never goo) at most one slab above its surface — the free-step shore.</summary>
    public List<Vector2I> BankCells { get; } = new();

    /// <summary>The highest dry cells of the high country, spaced so one massif does not claim a ridge of them.</summary>
    public List<Vector2I> Summits { get; } = new();

    /// <summary>Columns with more than one span: an undercut cliff or a cell of an arch.</summary>
    public List<Vector2I> Overhangs { get; } = new();

    /// <summary>River beds: the flooded columns carrying a watercourse, stream or navigable reach.</summary>
    public List<Vector2I> RiverBedCells { get; } = new();

    /// <summary>Lake beds: the flooded columns under standing water. Goo puddles are neither; <see cref="Fluid"/> says where they are.</summary>
    public List<Vector2I> LakeBedCells { get; } = new();

    // ---- naming ----

    /// <summary>What this Domain is called.</summary>
    public string Name { get; internal set; } = "";

    /// <summary>A name per walk area, parallel to <see cref="Areas"/>; empty for ground that is not a district.</summary>
    public List<string> Districts { get; } = new();

    /// <summary>A name per body of water, parallel to <see cref="WaterBody"/> ids.</summary>
    public List<string> WaterNames { get; } = new();

    /// <summary>
    /// The one wind direction of the Domain, as a compass index in <see cref="Grid.Dx8"/>
    /// order: 0 = east (+X), 2 = south (+Z), 4 = west, 6 = north (−Z). The wind blows
    /// along it; dune ridges run across it. Rolled for every Domain, dunes or not.
    /// </summary>
    public int DuneGrain { get; internal set; }

    /// <summary>Compass letters in <see cref="DuneGrain"/> order. North is −Z, as the lab's compass has it.</summary>
    private static readonly string[] Compass =
        { "E", "SE", "S", "SW", "W", "NW", "N", "NE" };

    /// <summary>Where the wind blows from, in compass letters — opposite the grain.</summary>
    public string WindFrom => Compass[(DuneGrain + 4) & 7];

    /// <summary>The way the crest lines run, across the wind.</summary>
    public string DuneRun => Compass[(DuneGrain + 2) & 7] + "-" + Compass[(DuneGrain + 6) & 7];

    /// <summary>The grain as a unit vector on the X/Z plane.</summary>
    public Vector2 DuneVector => Vector2.FromAngle(DuneGrain * Mathf.Tau / 8f);

    // ---- provenance ----

    /// <summary>The style actually used, with <c>Auto</c> already resolved.</summary>
    public ReliefStyle Style { get; internal set; }

    /// <summary>The arrangement actually used, with <c>Auto</c> already resolved.</summary>
    public IslandArrangement Arrangement { get; internal set; }

    /// <summary>The character actually used, with <c>Auto</c> already resolved.</summary>
    public TerrainCharacter Character { get; internal set; }

    /// <summary>How many islands were built for this seed before one was playable.</summary>
    public int Attempts { get; internal set; } = 1;

    /// <summary>Which guarantees this island still misses, or empty; non-empty means the re-roll budget ran out.</summary>
    public string Unmet { get; internal set; } = "";

    public IslandData(int size)
    {
        Size = size;
        Spans = new Span[size, size][];
        Material = new byte[size, size];
        Moisture = new byte[size, size];
        Warmth = new byte[size, size];
        Ruggedness = new byte[size, size];
        Exposure = new byte[size, size];
        RimDistance = new byte[size, size];
        Landform = new byte[size, size];
        Land = new bool[size, size];
        Region = new int[size, size];
        WaterLevel = new short[size, size];
        Fluid = new byte[size, size];
        Canyon = new bool[size, size];
        Pass = new bool[size, size];
        River = new bool[size, size];
        Navigable = new bool[size, size];
        Landings = new bool[size, size];
        Ferry = new bool[size, size];
        Ford = new bool[size, size];
        Beach = new bool[size, size];
        WaterBody = new int[size, size];
        Flow = new int[size, size];
        Walk = new int[size, size];
        Reach = new int[size, size];
        ShelfId = new int[size, size];
        for (int x = 0; x < size; x++)
        for (int z = 0; z < size; z++)
        {
            WaterLevel[x, z] = NoLand;
            Walk[x, z] = -1;
            Reach[x, z] = -1;
            ShelfId[x, z] = -1;
            WaterBody[x, z] = -1;
        }
    }

    public bool HasLand(int x, int z) => Spans[x, z] is { Length: > 0 };

    /// <summary>
    /// Top slab of the lowest span — the ground you stand on. With an overhang the
    /// ground is under the lip, and every rule in the pipeline means the ground.
    /// </summary>
    public short SurfaceLevel(int x, int z)
        => HasLand(x, z) ? Spans[x, z][0].Top : NoLand;

    /// <summary>Bottom slab of the lowest span, or <see cref="NoLand"/>.</summary>
    public short KeelLevel(int x, int z)
        => HasLand(x, z) ? Spans[x, z][0].Bottom : NoLand;

    /// <summary>
    /// The level you would see: the water surface where the column is flooded, the
    /// ground otherwise. Cliffs, banks and relief are measured against this.
    /// </summary>
    public short EffectiveLevel(int x, int z)
    {
        if (!HasLand(x, z)) return NoLand;
        short water = WaterLevel[x, z];
        return water != NoLand ? water : Spans[x, z][0].Top;
    }
}
