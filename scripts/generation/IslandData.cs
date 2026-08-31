using System.Collections.Generic;

using Godot;

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

    /// <summary>
    /// What the top of each column is made of — a <see cref="SurfaceMaterial"/>.
    /// Derived from height, slope, distance to water and landform once the terrain
    /// is finished; see <c>Surfaces</c>.
    /// </summary>
    public byte[,] Material { get; }

    /// <summary>
    /// Land cells with aether beside them, and cells with a face of three slabs or
    /// more. The <b>feature anchors</b>: a forest goes "on flat well-watered ground
    /// away from the coast", not at a coordinate, so generation answers the
    /// geometric questions once and the content layer reads the lists instead of
    /// each system carrying its own copy of the terrain rules.
    /// </summary>
    public List<Vector2I> CoastCells { get; } = new();

    /// <summary>Cells standing three slabs or more above a neighbour.</summary>
    public List<Vector2I> CliffCells { get; } = new();

    /// <summary>What this Domain is called.</summary>
    public string Name { get; internal set; } = "";

    /// <summary>
    /// Which way the Domain's dune fields lie, as one of eight compass points
    /// (0 = east, counting anticlockwise in eighths of a turn — the same
    /// convention as an angle on the X/Z plane).
    ///
    /// <b>One direction for the whole Domain</b>, because what makes dunes dunes
    /// is that they all lie the same way; and snapped to a compass point rather
    /// than a free angle, because a direction that cannot be named is a direction
    /// nothing can show or use. Ridges run <i>across</i> it: this is the wind.
    /// Meaningless on a Domain with no dune patches.
    /// </summary>
    public int DuneGrain { get; internal set; }

    /// <summary>The eight compass points, indexed by <see cref="DuneGrain"/>.</summary>
    private static readonly string[] Compass =
        { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };

    /// <summary>
    /// Where the wind blows from, in compass letters — the opposite of the way the
    /// grain runs, since a dune's crest faces the wind.
    /// </summary>
    public string WindFrom => Compass[(DuneGrain + 4) & 7];

    /// <summary>And the way the crest lines themselves run, across the wind.</summary>
    public string DuneRun => Compass[(DuneGrain + 2) & 7] + "-" + Compass[(DuneGrain + 6) & 7];

    /// <summary>The grain as a unit vector on the X/Z plane, for drawing it.</summary>
    public Vector2 DuneVector => Vector2.FromAngle(DuneGrain * Mathf.Tau / 8f);

    /// <summary>
    /// A name per walk area, parallel to <see cref="Areas"/>; empty for the
    /// broken ground that is not a district.
    /// </summary>
    public List<string> Districts { get; } = new();

    /// <summary>A name per body of water, parallel to <see cref="WaterBody"/> ids.</summary>
    public List<string> WaterNames { get; } = new();

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

    /// <summary>
    /// What the standing fluid in a column is, as a <see cref="FluidKind"/>.
    /// Meaningful only where <see cref="WaterLevel"/> is set; everything is
    /// water unless a pass said otherwise. Fluids never touch, even diagonally.
    /// </summary>
    public byte[,] Fluid { get; }

    /// <summary>
    /// The Domain's geyser fields. <b>Empty today</b>: the terrain-stage
    /// placement was binned, because where a jet belongs is a fact about the
    /// biome — this list and the lab's rendering are the hook that layer will
    /// fill. See <see cref="Geyser"/>.
    /// </summary>
    public List<Geyser> Geysers { get; } = new();

    /// <summary>
    /// Columns a canyon was cut through. A canyon is a deliberate exception to
    /// the step grammar — its walls are a cliff on a border the rules would
    /// otherwise forbid one on — so anything auditing or pathing the terrain has
    /// to be able to tell one from a mistake. Rivers will want it too.
    /// </summary>
    public bool[,] Canyon { get; }

    /// <summary>
    /// Columns inside a <b>pass</b> — a saddle cut where one plateau sags down to
    /// meet the next, so a cliff border has one place you can walk across. Like a
    /// canyon it is a deliberate exception, but the opposite one: a canyon breaks
    /// a connection, a pass makes one.
    /// </summary>
    public bool[,] Pass { get; }

    /// <summary>The centre of each pass. Usually none or one.</summary>
    public List<Vector2I> Passes { get; } = new();

    /// <summary>
    /// Where a bridge would go: one <see cref="Crossing"/> per link, enough to
    /// join every landmass into one. The generator levels both banks so the deck
    /// can run at a single level and be walked onto at either end; the settlement
    /// layer will use them to know where a crossing is worth building.
    /// </summary>
    public List<Crossing> Bridges { get; } = new();

    /// <summary>
    /// Cells of gap one bridge may span on this Domain, from
    /// <see cref="IslandParams.Crossings"/>. It decides how far apart an
    /// arrangement's landmasses may sit and what <see cref="Traversal"/> counts as
    /// reachable, so it is carried on the data rather than being a constant.
    /// </summary>
    public int BridgeSpan { get; internal set; } = Traversal.DefaultBridgeSpan;

    /// <summary>
    /// Ground the Domain's Gates are served by, and nothing else: the 3 × 5 strip
    /// a hanging Gate's vessel sets down on, and the 3 × 3 forecourt a land Gate
    /// stands on.
    ///
    /// It used to mark every coast that <i>would</i> take a strip, which painted
    /// most of the coastline and answered a question — "where else could a Link
    /// have come out?" — that stopped being interesting once a Gate was
    /// guaranteed. What matters now is the ground the arriving player uses.
    /// </summary>
    public bool[,] Landings { get; }

    /// <summary>
    /// Which body of water each flooded column belongs to, or <c>-1</c> for dry
    /// ground and aether. Two columns share an id exactly when a hull could go
    /// from one to the other: a <b>waterfall cuts a body in two</b>, since nothing
    /// sails up one.
    /// </summary>
    public int[,] WaterBody { get; }

    /// <summary>How many separate bodies of water the Domain has.</summary>
    public int WaterBodies { get; internal set; }

    /// <summary>
    /// Where a ferry could tie up: a quay cell and the water in front of it. See
    /// <see cref="FerryBerth"/> — this is the third kind of crossing, beside the
    /// bridge and the stair, and the only one that crosses water no bridge can
    /// span.
    /// </summary>
    public List<FerryBerth> Berths { get; } = new();

    /// <summary>The quay cell of every berth, for the lab overlay and the audit.</summary>
    public bool[,] Ferry { get; }

    /// <summary>
    /// How many berth sites the domino rule found <i>before</i> pruning — see
    /// <see cref="Traversal"/>. A diagnostic: <see cref="Berths"/> is what
    /// survived, and the two together say whether a Domain has no ferries because
    /// its water is crossable or because the pruning is too hungry.
    /// </summary>
    public int BerthSites { get; internal set; }

    /// <summary>
    /// Coast cells stepped down onto a beach: where the ground meets the aether
    /// gently rather than breaking off. One of the feature anchors — a quay, a
    /// landing, and whatever the biome layer grows on sand belong here.
    /// </summary>
    public bool[,] Beach { get; }

    /// <summary>
    /// Stream cells you can cross on foot. A stream is an obstacle everywhere
    /// else — see <see cref="Rivers.FordSpacing"/>.
    /// </summary>
    public bool[,] Ford { get; }

    /// <summary>
    /// Every column carrying more than one span: an undercut cliff, or a cell of
    /// an arch. The first of the feature anchors — biome features attach to kinds
    /// of surface rather than to coordinates, and the underside of an overhang is
    /// where vines and fungal mats belong.
    /// </summary>
    public List<Vector2I> Overhangs { get; } = new();

    /// <summary>
    /// Columns carrying a watercourse. A river column is flooded like a lake —
    /// <see cref="WaterLevel"/> holds its surface — but it behaves differently:
    /// a stream is one slab deep and can be forded, where a lake cannot.
    /// </summary>
    public bool[,] River { get; }

    /// <summary>
    /// River columns wide and deep enough to move goods on, and by the same token
    /// too wide to wade. Two cells across, which is still inside the bridge span.
    /// </summary>
    public bool[,] Navigable { get; }

    /// <summary>
    /// Drainage accumulation per column: how many cells upstream drain through
    /// this one. What decides where a river is, and how big.
    /// </summary>
    public int[,] Flow { get; }

    /// <summary>
    /// Where water falls rather than runs — a drop of three slabs or more along a
    /// channel, and every channel that reaches the rim. At the coast every river
    /// becomes one, because there is no sea to run to; those are the falls that
    /// pour off the underside of the Domain.
    /// </summary>
    public List<Fall> Falls { get; } = new();

    /// <summary>Stage 1 land mask, kept for debugging / later stages.</summary>
    public bool[,] Land { get; }

    /// <summary>
    /// Which landform region each column belongs to, or <c>-1</c> for no land.
    /// Regions are the patches the island is stitched from.
    /// </summary>
    public int[,] Region { get; }

    /// <summary>
    /// Which walkable area each column belongs to — an index into
    /// <see cref="Areas"/> — or <see cref="Traversal.Water"/> for a flooded
    /// column and <c>-1</c> for no land. Two columns share an id exactly when you
    /// can walk between them without infrastructure.
    /// </summary>
    public int[,] Walk { get; }

    /// <summary>
    /// Every walkable area, <b>largest first</b>, so <c>Areas[0]</c> is the
    /// mainland. Most are broken ground: a mountain flank of 4-slab risers is
    /// dozens of contour benches, each its own area. See
    /// <see cref="WalkArea.IsDistrict"/>.
    /// </summary>
    public List<WalkArea> Areas { get; } = new();

    /// <summary>Index of the largest walkable area, or <c>-1</c> if there is no land.</summary>
    public int Mainland { get; internal set; } = -1;

    /// <summary>
    /// As <see cref="Walk"/>, but for a player who can build: two columns share an
    /// id when a stair, a hoist or a bridge could join them. This is the
    /// connectivity the design is actually held to — a cliff is meant to cost
    /// something, not to be a wall.
    /// </summary>
    public int[,] Reach { get; }

    /// <summary>Every infrastructure-reachable area, largest first.</summary>
    public List<WalkArea> Reaches { get; } = new();

    /// <summary>Index of the largest reachable area — the island's heartland.</summary>
    public int Heartland { get; internal set; } = -1;

    /// <summary>Which <see cref="Shelf"/> a column belongs to, or <c>-1</c> for none.</summary>
    public int[,] ShelfId { get; }

    /// <summary>
    /// Flat ground, one entry per contiguous same-level patch big enough to
    /// matter. The settlement layer reads this rather than re-deriving slopes.
    /// </summary>
    public List<Shelf> Shelves { get; } = new();

    /// <summary>The style actually used, with <c>Auto</c> already resolved.</summary>
    public ReliefStyle Style { get; internal set; }

    /// <summary>
    /// The Domain's Gates: one <see cref="GateRole.Entry"/> and one to three
    /// <see cref="GateRole.Exit"/>, at most one per edge.
    /// </summary>
    public List<Gate> Gates { get; } = new();

    /// <summary>
    /// The cheapest road from the Entry Gate to each Exit Gate, one per Exit —
    /// cheapest meaning "fewest things to build". See <see cref="Passage"/>.
    /// </summary>
    public List<Passage> Passages { get; } = new();

    /// <summary>The arrangement actually used, with <c>Auto</c> already resolved.</summary>
    public IslandArrangement Arrangement { get; internal set; }

    /// <summary>
    /// How many islands were built for this seed before one was playable. One
    /// almost always; more where the first roll had no way in, nowhere to build,
    /// or too much of itself out of reach.
    /// </summary>
    public int Attempts { get; internal set; } = 1;

    /// <summary>
    /// Which Stage 6 guarantees this island still misses, or empty when it meets
    /// them all. Non-empty means the re-roll budget ran out and this was the best
    /// of the attempts — worth surfacing rather than hiding, since it says the
    /// parameters are asking for something the pipeline cannot give.
    /// </summary>
    public string Unmet { get; internal set; } = "";

    /// <summary>The character actually used, with <c>Auto</c> already resolved.</summary>
    public TerrainCharacter Character { get; internal set; }

    /// <summary>
    /// Whether any road between two Gates climbs a <b>flight</b> — five elevators
    /// or more inside fifteen cells (see <see cref="Passage.Flights"/>). It is not
    /// a fault: a Domain is allowed to be hard country. It is a fact about this
    /// Domain that the settlement and difficulty layers will want, because a
    /// Domain whose only road is a staircase costs the player something before
    /// they have built anything.
    /// </summary>
    public bool Rough { get; internal set; }

    public IslandData(int size)
    {
        Size = size;
        Spans = new Span[size, size][];
        Material = new byte[size, size];
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
    /// Top slab of the <b>lowest</b> span — the ground you stand on.
    ///
    /// For every column with one span, which is every column until Stage 4b runs,
    /// that is simply the surface. For a column with an overhang over it, the
    /// ground is underneath and the lip is a roof: reading the highest span would
    /// report the roof as the surface, and every rule in the pipeline — the step
    /// grammar, shelves, Gates, the roads — means the ground.
    /// </summary>
    public short SurfaceLevel(int x, int z)
        => HasLand(x, z) ? Spans[x, z][0].Top : NoLand;

    /// <summary>Bottom slab of the lowest span, or <see cref="NoLand"/>.</summary>
    public short KeelLevel(int x, int z)
        => HasLand(x, z) ? Spans[x, z][0].Bottom : NoLand;
}
