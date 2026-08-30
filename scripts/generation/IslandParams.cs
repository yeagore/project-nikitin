using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Tunable inputs to <see cref="IslandGenerator"/>. A <c>[GlobalClass]</c>
/// resource so it can be authored in the inspector, or saved as a <c>.tres</c>
/// preset (see <c>resources/island_default.tres</c>). Heights are in <b>slabs</b>
/// (see <see cref="Terrain.SlabHeight"/>) — see docs/island-generation.md §3.
///
/// Every field here is a knob a Domain's biome / archetype is expected to set.
/// Things that define the terrain <i>grammar</i> rather than an island's looks
/// are deliberately <b>not</b> here: the free-step size, the minimum patch area
/// (derived from <see cref="RegionScale"/>), and the keel taper are constants,
/// because a biome that changed them would change what a cliff <i>means</i>.
/// </summary>
[GlobalClass]
public partial class IslandParams : Resource
{
    // ---- footprint ----------------------------------------------------------

    /// <summary>Footprint edge length in cells.</summary>
    [Export(PropertyHint.Range, "16,128,1")] public int Size { get; set; } = 96;

    /// <summary>Land-mask radius in cells. 0 = auto (Size * 0.45).</summary>
    [Export(PropertyHint.Range, "0,128,1")] public float Radius { get; set; } = 0f;

    /// <summary>Fraction of the bounding disc that becomes land.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Coverage { get; set; } = 0.62f;

    /// <summary>
    /// How far the silhouette departs from a circle: 0 is a disc, 1 is a strongly
    /// elongated, deeply lobed coastline with bays and peninsulas.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Irregularity { get; set; } = 0.55f;

    /// <summary>
    /// How the land is laid out: one mass, twins, an atoll, and so on.
    /// <c>Auto</c> picks one per seed.
    ///
    /// This replaces the old <c>Fragmentation</c> float, which asked one number
    /// to mean both "how broken up" and "into how many pieces" and delivered
    /// neither reliably. Whatever the arrangement, the pieces are guaranteed
    /// linkable by bridge — see <see cref="IslandArrangement"/>.
    /// </summary>
    [Export] public IslandArrangement Arrangement { get; set; } = IslandArrangement.Auto;

    // ---- what the island is made of -----------------------------------------

    /// <summary>
    /// Which landforms the island is built from. <c>Auto</c> picks one per seed.
    /// Where the high ground sits follows from this — see <see cref="ReliefStyle"/>.
    /// </summary>
    [Export] public TerrainCharacter Character { get; set; } = TerrainCharacter.Auto;

    /// <summary>
    /// How the character's landforms are shared out, from <b>low</b> (0 — mostly
    /// plains, and basins where the character has them) to <b>high</b> (1 — as
    /// much mountain / mesa / hill as the character allows). 0.5 is the
    /// character's own balance.
    ///
    /// Proportions are a <i>quota</i>, not a per-region dice roll: every landform
    /// a character names is guaranteed to appear, so a <c>Highland</c> can no
    /// longer come out with no mountains, or with mountains and no hills.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float LandformMix { get; set; } = 0.5f;

    /// <summary>Overall vertical exaggeration of every landform's relief.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Relief { get; set; } = 0.5f;

    /// <summary>
    /// What hills do: 0 is barely-there swells, 1 is mounds — steep-sided humps
    /// that still step one slab at a time, so they stay walkable everywhere. Also
    /// drives how jagged the shared surface noise is, since a rolling down and a
    /// field of mounds do not differ only in height.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Hilliness { get; set; } = 0.5f;

    /// <summary>
    /// Typical width of one landform region, in cells. Small values give a busy
    /// patchwork; large ones give a few broad provinces. The smallest patch
    /// allowed follows from this (see <see cref="MinRegionArea"/>).
    /// </summary>
    [Export(PropertyHint.Range, "6,40,1")] public int RegionScale { get; set; } = 16;

    /// <summary>
    /// Smallest region the island may contain, in cells — derived, not authored.
    /// A patch under this is merged into the neighbour it shares the most border
    /// with, so the island reads as a blanket of legible patches rather than a
    /// scatter of slivers. It is a fixed share of a full region's area because
    /// the two are not independent: what counts as a sliver depends entirely on
    /// how big a patch is meant to be.
    /// </summary>
    public int MinRegionArea => Mathf.Max(12, Mathf.RoundToInt(RegionScale * RegionScale * 0.215f));

    // ---- elevation ----------------------------------------------------------

    /// <summary>
    /// Height of one step on the plateau ladder, in <b>slabs</b>. Regions sit on
    /// multiples of this, so every border between two levels is an unambiguous
    /// cliff. Keep it ≥ 3: at 2 it reads as an accident rather than a decision.
    /// </summary>
    [Export(PropertyHint.Range, "3,16,1")] public int CliffHeight { get; set; } = 4;

    /// <summary>
    /// Number of rungs on the plateau ladder above the coastal level. Few rungs
    /// means neighbouring regions usually share one, so cliffs stay occasional
    /// and plains run together into broad ones.
    /// </summary>
    [Export(PropertyHint.Range, "1,8,1")] public int PlateauLevels { get; set; } = 2;

    /// <summary>
    /// Rise of a mountain from its foot to its summit, in <b>slabs</b>, taken
    /// literally: the summit stands this far above the ground the foothills meet.
    /// </summary>
    [Export(PropertyHint.Range, "8,160,1")] public int MountainHeight { get; set; } = 40;

    /// <summary>
    /// How far a mesa's flat top stands above the ground around it, in
    /// <b>slabs</b>, taken literally. A mesa is a step up, not a peak — and a
    /// chain of stepped mesas is capped at twice this above the plain it stands
    /// on, so a tableland cannot compound itself into a tower.
    /// </summary>
    [Export(PropertyHint.Range, "3,24,1")] public int MesaHeight { get; set; } = 5;

    /// <summary>
    /// How far a basin's flat floor sits below the ground around it, in
    /// <b>slabs</b>. The mesa rule inverted, and capped the same way.
    /// </summary>
    [Export(PropertyHint.Range, "3,24,1")] public int BasinDepth { get; set; } = 5;

    /// <summary>
    /// How wet the Domain is: 0 leaves it dry, 1 gives it a full drainage network.
    /// It sets how much catchment a channel needs before it counts as a river, so
    /// a wetter island has more of them and they start higher up.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Rivers { get; set; } = 0.5f;

    // ---- crossings ----------------------------------------------------------

    /// <summary>
    /// How far one bridge may reach, and so how hard the Domain is to build your
    /// way across: <c>Easy</c> spans a single cell, <c>Medium</c> three,
    /// <c>Hard</c> six.
    ///
    /// It is not only an analysis setting — the arrangement's landmasses are
    /// nudged together until each faces the next across at most this many cells,
    /// so an <c>Easy</c> Domain is an archipelago you can almost step between and
    /// a <c>Hard</c> one leaves real straits. A deck is level and has to be walked
    /// onto at both ends, so the two banks of every crossing are levelled whatever
    /// the span (see <see cref="Crossing"/>).
    /// </summary>
    [Export] public BridgeEase Crossings { get; set; } = BridgeEase.Medium;

    // ---- Gates --------------------------------------------------------------

    /// <summary>
    /// What kind of Gate the player arrives through. <b>An input, not a
    /// preference:</b> a Link joins two Gates, so the far end has to match the one
    /// they left — land to land, hanging to hanging. The Domain that sends them
    /// sets this, and this Domain is generated around it. <c>Auto</c> is for a
    /// Home Domain, which has nothing to match.
    /// </summary>
    [Export] public GateKind EntryGate { get; set; } = GateKind.Auto;

    /// <summary>Links onward, 1 to 3. 0 picks a count from the seed.</summary>
    [Export(PropertyHint.Range, "0,3,1")] public int ExitGates { get; set; } = 0;

    // ---- underside / keel ---------------------------------------------------
    // The island hangs in aether as a spinning top: a thin lip at the coastline
    // thickening inland to a deep keel under the interior.

    /// <summary>Column depth at the coastline, in <b>slabs</b>. The thin outer lip.</summary>
    [Export(PropertyHint.Range, "1,32,1")] public int EdgeThickness { get; set; } = 3;

    /// <summary>Extra depth under the deepest interior, in <b>slabs</b>.</summary>
    [Export(PropertyHint.Range, "4,256,1")] public int KeelDepth { get; set; } = 34;

    /// <summary>How craggy the underside is. Scales with depth, so the lip stays clean.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float KeelRoughness { get; set; } = 0.45f;
}
