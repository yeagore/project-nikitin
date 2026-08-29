using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Tunable inputs to <see cref="IslandGenerator"/>. A <c>[GlobalClass]</c>
/// resource so it can be authored in the inspector (e.g. from the island lab).
/// Only the fields used by pipeline stages 1–3 are present for now; the rest
/// (rim, shelf width, overhangs) arrive with their stages — see
/// docs/island-generation.md §3.
/// </summary>
[GlobalClass]
public partial class IslandParams : Resource
{
    /// <summary>Footprint edge length in cells.</summary>
    [Export(PropertyHint.Range, "16,128,1")] public int Size { get; set; } = 96;

    /// <summary>Land-mask radius in cells. 0 = auto (Size * 0.45).</summary>
    [Export(PropertyHint.Range, "0,128,1")] public float Radius { get; set; } = 0f;

    /// <summary>Fraction of the bounding disc that becomes land.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Coverage { get; set; } = 0.55f;

    /// <summary>Single blob (0) to many separated islets (1).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Fragmentation { get; set; } = 0f;

    /// <summary>Flat plains (0) to mountains (1): height amplitude + ridge weight.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Relief { get; set; } = 0.4f;

    /// <summary>Smooth (0) to jagged (1) surface (noise gain).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Roughness { get; set; } = 0.5f;

    /// <summary>Peak surface height in blocks at <see cref="Relief"/> = 1.</summary>
    [Export(PropertyHint.Range, "4,120,1")] public int HeightScale { get; set; } = 48;

    /// <summary>Number of habitable shelf levels (0 = free slope only).</summary>
    [Export(PropertyHint.Range, "0,6,1")] public int TerraceCount { get; set; } = 3;

    /// <summary>How strongly the surface snaps to shelf levels vs. free slope.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float TerraceGrip { get; set; } = 0.5f;
}
