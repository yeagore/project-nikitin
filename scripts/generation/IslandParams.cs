using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Tunable inputs to <see cref="IslandGenerator"/>: a <c>[GlobalClass]</c> resource, so a preset
/// (<c>resources/island_default.tres</c>) binds by property name. Heights are in slabs.
/// Grammar constants (the free step, the keel taper) are deliberately not knobs.
/// </summary>
[GlobalClass]
public partial class IslandParams : Resource
{
    // ---- footprint ----------------------------------------------------------

    /// <summary>The footprints a Domain may have; altitude is bounded by the same number in slabs.</summary>
    public static readonly int[] SupportedSizes = { 48, 64, 72, 96, 128 };

    /// <summary>Footprint edge length in cells.</summary>
    [Export(PropertyHint.Range, "48,128,1")] public int Size { get; set; } = 96;

    /// <summary>Land-mask radius in cells. 0 = auto (Size * 0.45).</summary>
    [Export(PropertyHint.Range, "0,128,1")] public float Radius { get; set; } = 0f;

    /// <summary>Fraction of the bounding disc that becomes land.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Coverage { get; set; } = 0.62f;

    /// <summary>How far the silhouette departs from a circle: 0 a disc, 1 elongated and deeply lobed.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Irregularity { get; set; } = 0.55f;

    /// <summary>How the land is laid out; <c>Auto</c> picks one per seed. Every arrangement is linkable by bridge.</summary>
    [Export] public IslandArrangement Arrangement { get; set; } = IslandArrangement.Auto;

    /// <summary>Whether <c>Auto</c> may roll the newer layouts. Gates Auto's dice only; naming a shape still builds it.</summary>
    [Export] public bool NewArrangements { get; set; } = true;

    /// <summary>Whether <c>Auto</c> may roll the sculpted characters (Badlands, Karst, Massif, Dunes). Gates Auto's dice only; naming one still builds it.</summary>
    [Export] public bool NewLandforms { get; set; } = true;

    // ---- what the island is made of -----------------------------------------

    /// <summary>Which landforms the island is built from; <c>Auto</c> picks one per seed. The <see cref="ReliefStyle"/> follows from it.</summary>
    [Export] public TerrainCharacter Character { get; set; } = TerrainCharacter.Auto;

    /// <summary>
    /// How the character's landforms are shared out, 0 mostly plains … 1 as much high ground as
    /// the character allows. A quota, not a dice roll: every landform a character names appears.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float LandformMix { get; set; } = 0.5f;

    /// <summary>Overall vertical exaggeration of every landform's relief.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Relief { get; set; } = 0.5f;

    /// <summary>What hills do: 0 barely-there swells, 1 steep mounds, still one slab at a time. Also drives how jagged the surface noise is.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Hilliness { get; set; } = 0.5f;

    /// <summary>Typical width of one landform region, in cells. <see cref="MinRegionArea"/> follows from it.</summary>
    [Export(PropertyHint.Range, "6,40,1")] public int RegionScale { get; set; } = 16;

    /// <summary>Smallest region allowed, in cells — derived: max(12, 0.215 × RegionScale²). Smaller patches are merged into a neighbour.</summary>
    public int MinRegionArea => Mathf.Max(12, Mathf.RoundToInt(RegionScale * RegionScale * 0.215f));

    // ---- elevation ----------------------------------------------------------

    /// <summary>Height of one step on the plateau ladder, in slabs. Keep it ≥ 3, so every ladder border is an unambiguous cliff.</summary>
    [Export(PropertyHint.Range, "3,16,1")] public int CliffHeight { get; set; } = 4;

    /// <summary>Rungs on the plateau ladder above the coastal level. Few rungs keep cliffs occasional.</summary>
    [Export(PropertyHint.Range, "1,8,1")] public int PlateauLevels { get; set; } = 2;

    /// <summary>Rise of a mountain from its foot to its summit, in slabs.</summary>
    [Export(PropertyHint.Range, "8,160,1")] public int MountainHeight { get; set; } = 40;

    /// <summary>How far a mesa's top stands above the ground around it, in slabs. A chain of stepped mesas is capped at twice this.</summary>
    [Export(PropertyHint.Range, "3,24,1")] public int MesaHeight { get; set; } = 5;

    /// <summary>How far a basin's floor sits below the ground around it, in slabs. The mesa rule inverted, capped the same way.</summary>
    [Export(PropertyHint.Range, "3,24,1")] public int BasinDepth { get; set; } = 5;

    /// <summary>How wet the Domain is: sets the catchment a channel needs before it counts as a river.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Rivers { get; set; } = 0.5f;

    /// <summary>How readily standing water collects: 0 no lakes, 1 one in every flat patch that could hold it.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Lakes { get; set; } = 0.5f;

    /// <summary>How far the ground falls toward a watercourse: 0 a bare incision, 1 five cells of valley either side.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Valleys { get; set; } = 0.4f;

    // ---- crossings ----------------------------------------------------------

    /// <summary>
    /// Cells one bridge may span: <c>Easy</c> one, <c>Medium</c> three, <c>Hard</c> six. Also
    /// nudges the arrangement's landmasses together until each faces the next within this span.
    /// </summary>
    [Export] public BridgeEase Crossings { get; set; } = BridgeEase.Medium;

    // ---- Gates --------------------------------------------------------------

    /// <summary>The kind of Gate the player arrives through. An input: it must match the sending Domain's Gate. <c>Auto</c> is for a Home Domain.</summary>
    [Export] public GateKind EntryGate { get; set; } = GateKind.Auto;

    /// <summary>The edge the player arrives on. An input for the same reason; <c>Auto</c> tries each edge, and the others are still tried if the named one cannot host a Gate.</summary>
    [Export] public GateEdge EntryEdge { get; set; } = GateEdge.Auto;

    /// <summary>Links onward, 1 to 3. 0 picks a count from the seed.</summary>
    [Export(PropertyHint.Range, "0,3,1")] public int ExitGates { get; set; } = 0;

    /// <summary>What kind the Exits are. <c>Auto</c> hangs them unless a coast will not have it; a named kind applies where the coast allows.</summary>
    [Export] public GateKind ExitGate { get; set; } = GateKind.Auto;

    // ---- underside / keel ---------------------------------------------------
    // A thin lip at the coastline thickening inland to a deep keel.

    /// <summary>Column depth at the coastline, in slabs.</summary>
    [Export(PropertyHint.Range, "1,32,1")] public int EdgeThickness { get; set; } = 3;

    /// <summary>Extra depth under the deepest interior, in slabs.</summary>
    [Export(PropertyHint.Range, "4,256,1")] public int KeelDepth { get; set; } = 34;

    /// <summary>How craggy the underside is. Scales with depth, so the lip stays clean.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float KeelRoughness { get; set; } = 0.45f;

    // ---- overhangs and arches -----------------------------------------------

    /// <summary>How often a tall face is undercut and a short gap arched over. Added after the traversal analysis, so rendered and collidable but not walkable.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float OverhangDensity { get; set; } = 0.35f;

    /// <summary>How far a lip reaches out from the face it hangs off, in cells.</summary>
    [Export(PropertyHint.Range, "1,8,1")] public int OverhangDepth { get; set; } = 2;

    /// <summary>Widest gap a natural bridge will span, in cells.</summary>
    [Export(PropertyHint.Range, "2,10,1")] public int ArchSpan { get; set; } = 4;
}
