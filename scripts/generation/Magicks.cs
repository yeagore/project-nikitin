using System;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// The magickal density layer: one byte per column, <see cref="IslandData.Magick"/>,
/// 0 inert … 255 saturated. For now it is noise and nothing else — no landform,
/// water or climate reads into it — laid down as soft waves rather than grain:
/// two octaves of warped simplex at a wavelength of about forty cells, the warp
/// bending the crests so they flow rather than tile, stretched so the byte uses
/// its range. What the Magicks system makes of it is its own design; the layer
/// exists so the lab, the audit and the collages carry it from the start.
/// </summary>
internal static class Magicks
{
    /// <summary>Base frequency: about forty cells from one crest to the next.</summary>
    private const float Wavelength = 0.024f;

    /// <summary>How far the crests are bent, in cells, and the frequency of the bending.</summary>
    private const float WarpAmplitude = 14f, WarpFrequency = 0.02f;

    /// <summary>
    /// Contrast about the middle, applied through a tanh so the ends saturate softly:
    /// the raw field rarely leaves 0.2–0.8, the byte should use more of its range,
    /// and a hard clip made flat plateaus of 0 and 255, which is not soft.
    /// </summary>
    private const float Stretch = 3.2f;

    /// <summary>Fills <see cref="IslandData.Magick"/> over the land; nothing else is read.</summary>
    public static void Measure(int seed, IslandData d)
    {
        int n = d.Size;
        var waves = new Noise(seed + 71_041, Wavelength, octaves: 2, gain: 0.35f)
            .WithWarp(WarpAmplitude, WarpFrequency);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            float v = 0.5f + 0.5f * MathF.Tanh((waves.At(x, z) - 0.5f) * Stretch);
            d.Magick[x, z] = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
        }
    }
}
