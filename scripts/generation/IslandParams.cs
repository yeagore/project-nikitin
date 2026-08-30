using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Tunable inputs to <see cref="IslandGenerator"/>. A <c>[GlobalClass]</c>
/// resource so it can be authored in the inspector (e.g. from the island lab).
/// Heights are in <b>slabs</b> (see <see cref="Terrain.SlabHeight"/>). Only the
/// fields used by pipeline stages 1–4 are present — see
/// docs/island-generation.md §3.
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

    /// <summary>Single blob (0) to many separated islets (1).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Fragmentation { get; set; } = 0f;

    // ---- relief -------------------------------------------------------------

    /// <summary>
    /// Which landforms the island is built from. <c>Auto</c> picks one per seed.
    /// Where the high ground sits follows from this — see <see cref="ReliefStyle"/>.
    /// </summary>
    [Export] public TerrainCharacter Character { get; set; } = TerrainCharacter.Auto;

    /// <summary>Overall vertical exaggeration of every landform's relief.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Relief { get; set; } = 0.5f;

    /// <summary>Smooth (0) to jagged (1) surface (noise gain).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Roughness { get; set; } = 0.5f;

    /// <summary>
    /// Typical width of one landform region, in cells. Small values give a busy
    /// patchwork; large ones give a few broad provinces.
    /// </summary>
    [Export(PropertyHint.Range, "6,40,1")] public int RegionScale { get; set; } = 16;

    /// <summary>
    /// Smallest region the island may contain, in cells. Anything under this is
    /// merged into the neighbour it shares the most border with, so the island
    /// reads as a blanket of legible patches rather than a scatter of slivers.
    /// </summary>
    [Export(PropertyHint.Range, "12,400,1")] public int MinRegionArea { get; set; } = 55;

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
    /// <b>slabs</b>, taken literally. A mesa is a step up, not a peak.
    /// </summary>
    [Export(PropertyHint.Range, "3,24,1")] public int MesaHeight { get; set; } = 5;

    /// <summary>
    /// How far a basin's flat floor sits below the ground around it, in
    /// <b>slabs</b>. The mesa rule inverted.
    /// </summary>
    [Export(PropertyHint.Range, "3,24,1")] public int BasinDepth { get; set; } = 5;

    // ---- underside / keel ---------------------------------------------------
    // The island hangs in aether as a spinning top: a thin lip at the coastline
    // thickening inland to a deep keel under the interior.

    /// <summary>Column depth at the coastline, in <b>slabs</b>. The thin outer lip.</summary>
    [Export(PropertyHint.Range, "1,32,1")] public int EdgeThickness { get; set; } = 3;

    /// <summary>Extra depth under the deepest interior, in <b>slabs</b>.</summary>
    [Export(PropertyHint.Range, "4,256,1")] public int KeelDepth { get; set; } = 34;

    /// <summary>
    /// Keel profile exponent. 1 is a straight cone; below 1 bulges out under the
    /// shoulders; above 1 keeps the flanks thin and drives a sharper point.
    /// </summary>
    [Export(PropertyHint.Range, "0.3,3,0.05")] public float KeelTaper { get; set; } = 0.85f;

    /// <summary>How craggy the underside is. Scales with depth, so the lip stays clean.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float KeelRoughness { get; set; } = 0.45f;
}
