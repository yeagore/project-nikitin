using System;
using System.Collections.Generic;

using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Stage 4b: the two places a column carries <b>more than one span</b> — an
/// undercut cliff, and an arch over a gap. This is the only stage that uses the
/// span list for what it is for; everything before it leaves one solid run per
/// column, keel to surface.
///
/// <para><b>It runs last, after the analysis.</b> Walkability, shelves, Gates and
/// the roads between them are all worked out over the ground, and the lip of an
/// overhang is not ground — it is a roof. Pathing over two-level columns is a
/// real problem (it wants spans as nodes rather than columns, and nothing in
/// <see cref="Traversal"/> is written that way), and it is a separate one. Adding
/// the geometry after the fact keeps the two apart: what is here is rendered and
/// collidable, and what walks on it is a later question.</para>
///
/// <para>That is also why <see cref="IslandData.SurfaceLevel"/> reads the
/// <i>lowest</i> span's top rather than the highest. For every column that has
/// one span the two are the same; for a column with a lip over it, the ground is
/// underneath, which is what every rule in the pipeline means by "the
/// surface".</para>
/// </summary>
internal static class Overhangs
{
    /// <summary>
    /// How tall a face has to be before it is worth undercutting, in slabs.
    /// Eight — the height a stair spans, so it is already a wall rather than a
    /// step, and there is room for a lip with air under it.
    /// </summary>
    private const int MinFace = 8;

    /// <summary>Slabs of clear air a lip must leave under itself.</summary>
    private const int Headroom = 4;

    /// <summary>Slabs of rock in a lip. Thin enough to read as an overhang.</summary>
    private const int LipThickness = 2;

    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };

    /// <summary>
    /// Whether a cliff top is solid enough behind it to hang a lip off.
    ///
    /// <b>This is what keeps overhangs off the thin landforms.</b> A karst tower
    /// two cells across, a basin rim, the wall of a sinkhole: all of them are
    /// faces of eight slabs or more, all of them qualify on height alone, and a
    /// lip jutting off one reads as a hole punched through the wall rather than as
    /// an undercut — the feature is not thick enough to look like it has an
    /// underside. So the high side has to be part of a mass: two of its other
    /// neighbours within a slab of its own top, and not a landform whose whole
    /// shape is the wall.
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
    /// Hangs lips off the tall faces and throws arches over the short gaps.
    /// Both are bounded by <see cref="IslandParams.OverhangDensity"/>, and both
    /// are placed on a noise field rather than per cell, so they come in runs
    /// along a face instead of being sprinkled over the island.
    /// </summary>
    public static void Carve(int seed, IslandParams p, IslandData d)
    {
        float density = Math.Clamp(p.OverhangDensity, 0f, 1f);
        if (density <= 0.001f) return;

        int n = d.Size;
        var where = new Noise(seed + 9091, frequency: 0.09f, octaves: 2);
        // A high threshold at low density, so an island with the knob down has a
        // couple of undercuts rather than a uniform thin scatter of them.
        float bar = Mathf.Lerp(0.78f, 0.34f, density);

        Undercut(seed, p, d, n, where, bar);
        Arch(seed, p, d, n, where, bar);

        d.Overhangs.Clear();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.Spans[x, z] is { Length: > 1 }) d.Overhangs.Add(new Vector2I(x, z));
    }

    /// <summary>
    /// A lip of rock jutting out from a cliff top over the ground below it.
    ///
    /// In a columnar model an undercut cannot be cut sideways into the cliff
    /// column — a column is one place, and there is nowhere for the notch to go.
    /// It is built the other way round: the columns in front of the face get a
    /// <i>second</i> span up at the cliff top, with air between it and their own
    /// ground. Seen from below that is exactly an undercut face, and seen from
    /// above it is the cliff edge jutting out.
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
                // The cliff top has to be part of a mass — see Backed. `k` is the
                // way *out* over the low ground, so its opposite is the way back
                // into the rock, which is the neighbour that may not count.
                if (!Backed(d, x, z, k ^ 1)) continue;

                // How far the lip reaches out, and how thick it is. Both wander,
                // so a face carries a ragged eave rather than a fitted shelf.
                int depth = 1 + (int)(Hash01(seed, 0x0EA5u ^ (uint)(x * 73856093 ^ z * 19349663))
                                      * reach);
                int thick = LipThickness
                            + (int)(Hash01(seed, 0x11Fu ^ (uint)(x * 31 + z)) * 2f);
                short bottom = (short)(high - thick + 1);

                for (int step = 0; step < depth; step++)
                {
                    int cx = lx + Dx[k] * step, cz = lz + Dz[k] * step;
                    if (cx < 0 || cz < 0 || cx >= n || cz >= n) break;
                    if (!d.HasLand(cx, cz) || d.Spans[cx, cz].Length > 1) break;
                    // Real air under the lip, and a gap the span list can hold:
                    // two spans in a column must not touch.
                    if (bottom - d.SurfaceLevel(cx, cz) < Headroom) break;

                    d.Spans[cx, cz] = new[]
                    {
                        d.Spans[cx, cz][0],
                        new Span(bottom, high),
                    };
                }
                break;                      // one face per column is enough
            }
        }
    }

    /// <summary>
    /// A natural bridge: two cliff tops of about the same height with a chasm or a
    /// channel between them, joined by a deck a few slabs thick with daylight
    /// under it.
    ///
    /// <para><b>Over a gorge, not over aether.</b> An arch out into open aether
    /// would put rock in a column the land mask says is empty — no region, no
    /// landform, no keel — and every consumer that reads "this column has land,
    /// therefore it has a region" would be wrong about it. Arching a canyon or a
    /// river channel instead means every cell of the arch is a column that already
    /// exists, and the only thing that changes is that it now carries a second
    /// span. It is also the commoner form in the world: an arch spans the thing
    /// that cut it.</para>
    /// </summary>
    private static void Arch(int seed, IslandParams p, IslandData d, int n,
                             Noise where, float bar)
    {
        int span = Math.Max(2, p.ArchSpan);

        for (int k = 0; k < 2; k++)                 // +X and +Z; each gap once
        {
            int dx = k == 0 ? 1 : 0, dz = k == 0 ? 0 : 1;

            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.HasLand(x, z) || d.Spans[x, z].Length > 1) continue;

                for (int gap = 2; gap <= span; gap++)
                {
                    int fx = x + dx * (gap + 1), fz = z + dz * (gap + 1);
                    if (fx < 0 || fz < 0 || fx >= n || fz >= n) break;
                    if (!d.HasLand(fx, fz)) break;

                    short here = d.SurfaceLevel(x, z), far = d.SurfaceLevel(fx, fz);
                    if (Math.Abs(here - far) > 2) break;
                    // Both abutments have to be rock a bridge could grow out of.
                    if (!Backed(d, x, z, -1) || !Backed(d, fx, fz, -1)) break;
                    short top = Math.Min(here, far);

                    // Everything under the deck has to be a gorge — ground (or
                    // water) far enough below to leave daylight — and untouched,
                    // so two arches never share a column.
                    bool hollow = true;
                    for (int step = 1; step <= gap && hollow; step++)
                    {
                        int mx = x + dx * step, mz = z + dz * step;
                        hollow = d.Spans[mx, mz] is { Length: 1 }
                                 && top - LipThickness - d.SurfaceLevel(mx, mz) >= Headroom;
                    }
                    if (!hollow) continue;
                    if (where.At(x + dx * gap * 0.5f, z + dz * gap * 0.5f) < bar) continue;

                    // The deck sits flush with the lower of the two ends, so the
                    // arch reads as continuous with the rock it grows out of.
                    for (int step = 1; step <= gap; step++)
                    {
                        int mx = x + dx * step, mz = z + dz * step;
                        d.Spans[mx, mz] = new[]
                        {
                            d.Spans[mx, mz][0],
                            new Span((short)(top - LipThickness), top),
                        };
                    }

                    x += dx * (gap + 1);
                    z += dz * (gap + 1);
                    break;
                }
            }
        }
    }

    private static float Hash01(int seed, uint salt)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u ^ salt;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
