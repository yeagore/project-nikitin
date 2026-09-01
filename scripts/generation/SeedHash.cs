namespace ProjectNikitin.Generation;

/// <summary>
/// Deterministic rolls from a seed and a salt. Two mixers coexist on purpose:
/// every salt in the terrain stages was tuned against <see cref="TerrainHash"/>,
/// every salt in the feature stages (rivers, Gates, overhangs, names) against
/// <see cref="FeatureHash"/>. Routing a call to the other mixer re-rolls that stage.
/// </summary>
internal static class SeedHash
{
    public static uint TerrainHash(int seed, uint salt)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u ^ salt * 2246822519u;
            h ^= h >> 15; h *= 2246822519u;
            h ^= h >> 13; h *= 3266489917u;
            h ^= h >> 16;
            return h;
        }
    }

    /// <summary>A terrain roll in [0, 1).</summary>
    public static float TerrainHash01(int seed, uint salt) => (TerrainHash(seed, salt) & 0xFFFFFF) / 16777216f;

    public static uint FeatureHash(int seed, uint salt)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u ^ salt;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
    }

    /// <summary>A feature roll in [0, 1).</summary>
    public static float FeatureHash01(int seed, uint salt) => (FeatureHash(seed, salt) & 0xFFFFFF) / 16777216f;
}
