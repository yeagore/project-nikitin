using System;
using System.Collections.Generic;

using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// Undercut lips and arches — the only stage that gives a column a second span.
/// Runs after <see cref="Traversal"/>, so a lip is rendered and collidable but not
/// walkable; <see cref="IslandData.SurfaceLevel"/> keeps reading the lowest span.
/// </summary>
internal static class Overhangs
{
    /// <summary>Slabs of face before it is worth undercutting (a stair's height).</summary>
    private const int MinFace = 8;

    /// <summary>Slabs of clear air a lip must leave under itself.</summary>
    private const int Headroom = 4;

    /// <summary>Slabs of rock in a lip.</summary>
    private const int LipThickness = 2;

    /// <summary>
    /// Whether a cliff top is part of a mass: not a thin landform (a lip off a
    /// two-cell tower reads as a hole) and two other neighbours within a slab of it.
    /// Neighbours are unchecked: the mask's empty one-cell border makes that safe.
    /// </summary>
    private static bool Backed(IslandData d, int x, int z, int away)
    {
        var form = (LandformType)d.Landform[x, z];
        if (form is LandformType.Karst or LandformType.Sinkholes
                 or LandformType.Basin or LandformType.Badlands) return false;

        short top = d.SurfaceLevel(x, z);
        int solid = 0;
        for (int k = 0; k < 4; k++)
        {
            if (k == away) continue;
            int nx = x + Dx[k], nz = z + Dz[k];
            if (!d.HasLand(nx, nz)) continue;
            if (Math.Abs(d.SurfaceLevel(nx, nz) - top) <= 1) solid++;
        }
        return solid >= 2;
    }

    /// <summary>
    /// Hangs lips off tall faces and throws arches over short gaps, both gated by
    /// one noise field so they come in runs. Zero density returns before the
    /// Overhangs list is cleared.
    /// </summary>
    public static void Carve(int seed, IslandParams p, IslandData d)
    {
        float density = Math.Clamp(p.OverhangDensity, 0f, 1f);
        if (density <= 0.001f) return;

        int n = d.Size;
        var where = new Noise(seed + 9091, frequency: 0.09f, octaves: 2);
        // High bar at low density: a few undercuts rather than a thin scatter.
        float bar = Mathf.Lerp(0.78f, 0.34f, density);

        Undercut(seed, p, d, n, where, bar);
        Arch(seed, p, d, n, where, bar);

        d.Overhangs.Clear();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.Spans[x, z] is { Length: > 1 }) d.Overhangs.Add(new Vector2I(x, z));
    }

    /// <summary>
    /// A lip off a cliff top: the columns in front of the face get a second span
    /// at the cliff's height, with air under it. One face per column.
    /// </summary>
    private static void Undercut(int seed, IslandParams p, IslandData d, int n,
                                 Noise where, float bar)
    {
        int reach = Math.Max(1, p.OverhangDepth);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z) || d.Spans[x, z].Length > 1) continue;
            short high = d.SurfaceLevel(x, z);

            for (int k = 0; k < 4; k++)
            {
                int lx = x + Dx[k], lz = z + Dz[k];
                if (!d.HasLand(lx, lz)) continue;
                if (high - d.SurfaceLevel(lx, lz) < MinFace) continue;
                if (where.At(x, z) < bar) continue;
                // k is the way out over the low ground; k^1 is the way back into the rock.
                if (!Backed(d, x, z, k ^ 1)) continue;

                int depth = 1 + (int)(Hash01(seed, 0x0EA5u ^ (uint)(x * 73856093 ^ z * 19349663))
                                      * reach);
                int thick = LipThickness
                            + (int)(Hash01(seed, 0x11Fu ^ (uint)(x * 31 + z)) * 2f);
                short bottom = (short)(high - thick + 1);

                LayLip(d, n, lx, lz, k, depth, bottom, high);
                break;                      // one face per column is enough
            }
        }
    }

    /// <summary>Lays a lip outward from (lx, lz) along k, stopping at the first column it cannot roof.</summary>
    private static void LayLip(IslandData d, int n, int lx, int lz, int k, int depth, short bottom, short high)
    {
        for (int step = 0; step < depth; step++)
        {
            int cx = lx + Dx[k] * step, cz = lz + Dz[k] * step;
            if (!InBounds(n, cx, cz)) break;
            if (!d.HasLand(cx, cz) || d.Spans[cx, cz].Length > 1) break;
            // Two spans in a column must not touch.
            if (bottom - d.SurfaceLevel(cx, cz) < Headroom) break;

            d.Spans[cx, cz] = new[]
            {
                d.Spans[cx, cz][0],
                new Span(bottom, high),
            };
        }
    }

    /// <summary>
    /// A natural bridge between two cliff tops of about the same height. Over a
    /// gorge, never aether: every arch cell is an existing column with a region,
    /// a landform and a keel, and only gains a second span.
    /// </summary>
    private static void Arch(int seed, IslandParams p, IslandData d, int n,
                             Noise where, float bar)
    {
        int span = Math.Max(2, p.ArchSpan);

        for (int k = 0; k < 2; k++)                 // +X and +Z; each gap once
        {
            int dx = k == 0 ? 1 : 0, dz = k == 0 ? 0 : 1;

            // The inner index runs along the span, so a placed arch is stepped over within its own row.
            for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                int x = k == 0 ? b : a, z = k == 0 ? a : b;
                if (!d.HasLand(x, z) || d.Spans[x, z].Length > 1) continue;

                for (int gap = 2; gap <= span; gap++)
                {
                    int fx = x + dx * (gap + 1), fz = z + dz * (gap + 1);
                    if (!InBounds(n, fx, fz)) break;
                    if (!d.HasLand(fx, fz)) break;

                    short here = d.SurfaceLevel(x, z), far = d.SurfaceLevel(fx, fz);
                    if (Math.Abs(here - far) > 2) break;
                    if (!Backed(d, x, z, -1) || !Backed(d, fx, fz, -1)) break;
                    short top = Math.Min(here, far);

                    // Every column under the deck is untouched and leaves daylight.
                    bool hollow = true;
                    for (int step = 1; step <= gap && hollow; step++)
                    {
                        int mx = x + dx * step, mz = z + dz * step;
                        hollow = d.Spans[mx, mz] is { Length: 1 }
                                 && top - LipThickness - d.SurfaceLevel(mx, mz) >= Headroom;
                    }
                    if (!hollow) continue;
                    if (where.At(x + dx * gap * 0.5f, z + dz * gap * 0.5f) < bar) continue;

                    for (int step = 1; step <= gap; step++)
                    {
                        int mx = x + dx * step, mz = z + dz * step;
                        d.Spans[mx, mz] = new[]
                        {
                            d.Spans[mx, mz][0],
                            new Span((short)(top - LipThickness), top),
                        };
                    }

                    b += gap + 1;
                    break;
                }
            }
        }
    }
}
