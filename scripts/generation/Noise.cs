using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Thin wrapper over <see cref="FastNoiseLite"/> that returns values in
/// <c>[0, 1]</c> and keeps the fractal / warp setup in one place. One instance
/// per purpose; seed each with <c>baseSeed + fixedOffset</c> so generation stays
/// a pure function of the base seed.
/// </summary>
public sealed class Noise
{
    private readonly FastNoiseLite _n = new();

    public Noise(int seed, float frequency, int octaves = 4, float gain = 0.5f, bool ridged = false)
    {
        _n.Seed = seed;
        _n.Frequency = frequency;
        _n.NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth;
        _n.FractalType = ridged
            ? FastNoiseLite.FractalTypeEnum.Ridged
            : FastNoiseLite.FractalTypeEnum.Fbm;
        _n.FractalOctaves = octaves;
        _n.FractalGain = gain;
    }

    /// <summary>Warp the sample coordinates before fractal evaluation (organic coastlines).</summary>
    public Noise WithWarp(float amplitude, float frequency)
    {
        _n.DomainWarpEnabled = true;
        _n.DomainWarpAmplitude = amplitude;
        _n.DomainWarpFrequency = frequency;
        return this;
    }

    /// <summary>2D sample in <c>[0, 1]</c>.</summary>
    public float At(float x, float z) => _n.GetNoise2D(x, z) * 0.5f + 0.5f;
}
