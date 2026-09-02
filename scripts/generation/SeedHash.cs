namespace ProjectNikitin.Generation;

/// <summary>
/// Deterministic rolls from a seed and a salt. One mixer for every stage; a salt
/// is a literal at the call site and is what keeps two rolls apart.
/// </summary>
internal static class SeedHash
{
    public static uint Hash(int seed, uint salt)
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

    /// <summary>A roll in [0, 1).</summary>
    public static float Hash01(int seed, uint salt) => (Hash(seed, salt) & 0xFFFFFF) / 16777216f;
}
