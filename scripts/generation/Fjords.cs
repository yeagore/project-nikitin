using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// Fjords: inlets of aether cut into the largest landmass from its coast, along
/// one grain per Domain, after the bites and before the islet filter. The mouth
/// is where a line along the grain first meets the landmass marching in from
/// outside; from there the inlet walks inland on a curve it holds the whole
/// way, with bends on two noises that grow with its length, flaring at the
/// mouth and tapering toward the head, so it has narrows where a deck fits and
/// reaches where none does. Its length is drawn from a deep notch up to most of
/// the land that lay ahead of its mouth, so one island's fjords differ, and a
/// long one may throw a side arm. It stops at a neck of land before any far
/// coast or strait, and where either wall beside it would thin out; one that
/// would still part a piece of islet size from the landmass is refused. The
/// grain is the Domain's own and not the wind's. <see cref="IslandParams.Fjords"/>
/// says how many are tried. Rifts — the same crack opened inland — were tried
/// and removed (appendix).
/// </summary>
internal static class Fjords
{
    /// <summary>Cells of land a head leaves beyond itself: a fjord never severs, and the neck it leaves is walkable.</summary>
    internal const int Neck = 5;

    /// <summary>Cells of land a fjord keeps on either side once inside: it narrows to keep them, and stops where it cannot. One that broke through its own wall was a bay with two mouths, or ate the peninsula it ran down.</summary>
    private const int Wall = 3;

    /// <summary>Steps in from the mouth before the walls are checked; a coast is thin at a mouth by nature.</summary>
    private const int Entry = 6;

    /// <summary>Steps in a row with no room for even a one-cell inlet before the walk stops: one bay beside the fjord is coast, a run of them is a peninsula.</summary>
    private const int Strikes = 3;

    /// <summary>Shortest fjord that is one, mouth to head; anything shorter is a bite and is not kept.</summary>
    private const int MinLength = 7;

    /// <summary>Half-widths in cells: from a footbridge's width to past twice a Medium span, before the flare and the taper.</summary>
    private const float HalfMin = 1.0f, HalfMax = 2.8f;

    /// <summary>How much wider the mouth is than the body, and how much narrower the head.</summary>
    private const float Flare = 0.6f, Taper = 0.45f;

    /// <summary>Tries per fjord before it is given up; each try is another mouth.</summary>
    private const int Tries = 6;

    /// <summary>A fjord that walked this many steps may throw a side arm.</summary>
    private const int ArmFrom = 16;

    /// <summary>
    /// Cuts the fjords the knob asks for into <paramref name="land"/>, marking them in
    /// <paramref name="data"/>'s <see cref="IslandData.Fjord"/> and listing each mouth.
    /// Runs inside the fit loop, so it clears what an earlier pass left.
    /// </summary>
    internal static void Cut(int seed, IslandParams p, bool[,] land, IslandData data)
    {
        data.Fjords.Clear();
        Array.Clear(data.Fjord, 0, data.Fjord.Length);

        float k = Math.Clamp(p.Fjords, 0f, 1f);
        int wanted = Math.Min(3, (int)(k * 1.6f + Hash01(seed, 0xF1F0u)));
        if (wanted == 0) return;

        float grain = Hash01(seed, 0xF1F2u) * Mathf.Tau;
        var wander = new Noise(seed + 8101, frequency: 1f, octaves: 2);
        var wobble = new Noise(seed + 8102, frequency: 1f, octaves: 2);
        for (int i = 0; i < wanted; i++) TryFjord(seed, land, data, grain, i, wander, wobble);
    }

    /// <summary>
    /// One fjord: from a coast the grain meets, chosen by an offset across the grain
    /// within the landmass's own width, the inlet walks inland along the grain.
    /// </summary>
    private static void TryFjord(int seed, bool[,] land, IslandData data, float grain, int index,
                                 Noise wander, Noise wobble)
    {
        int n = land.GetLength(0);
        uint salt = 0xF200u + (uint)index * 61u;
        for (int t = 0; t < Tries; t++)
        {
            uint s = salt + (uint)t * 7u;
            if (!Largest(land, out int[,] comp, out int id, out List<Vector2I> cells, out int second)) return;

            // In along the grain, from one end of it or the other.
            float angle = grain + (Hash01(seed, s ^ 0x11u) < 0.5f ? 0f : MathF.PI)
                          + (Hash01(seed, s ^ 0x22u) - 0.5f) * 0.7f;
            var u = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var v = new Vector2(-u.Y, u.X);
            Extent(cells, u, v, out Vector2 centroid, out float along, out float across);
            Vector2 line = centroid + v * ((Hash01(seed, s ^ 0x33u) - 0.5f) * 0.8f * across);

            // March in from outside: the first cell of the landmass on the line is the mouth.
            Vector2I mouth = default;
            bool found = false;
            for (float step = -along - 2f; step <= along + 2f; step += 0.5f)
            {
                Vector2 q = line + u * step;
                int x = Mathf.RoundToInt(q.X), z = Mathf.RoundToInt(q.Y);
                if (!InBounds(n, x, z) || comp[x, z] != id) continue;
                mouth = new Vector2I(x, z);
                found = true;
                break;
            }
            if (!found) continue;

            // The land ahead of the mouth along the grain says what lengths are possible:
            // from a deep notch to most of the way across, evenly, so one island's fjords differ.
            var start = new Vector2(mouth.X, mouth.Y);
            int ahead = Ahead(comp, id, start, angle);
            if (ahead < MinLength + Neck + 2) continue;
            int length = Math.Max(MinLength + 2,
                                  (int)(ahead * (0.15f + 0.6f * Hash01(seed, s ^ 0x55u))));
            float half = HalfMin + (HalfMax - HalfMin) * Hash01(seed, s ^ 0x44u);
            // A curve held the whole way, up to a radian over the length, so a fjord can hook.
            float curvature = (Hash01(seed, s ^ 0x66u) * 2f - 1f) / length;

            var cut = new List<Vector2I>();
            var seen = new HashSet<Vector2I>();
            var trail = new List<(Vector2 Pos, float Heading)>();
            Vector2I head = Walk(comp, id, start, angle, half, length, curvature, true,
                                 wander, wobble, index * 13 + t, cut, seen, trail);

            // A long fjord may throw a side arm from partway along, at sixty to ninety
            // degrees, narrower and shorter than what is left of the trunk.
            if (trail.Count >= ArmFrom && Hash01(seed, s ^ 0x77u) < 0.5f)
            {
                int at = (int)(trail.Count * (0.35f + 0.35f * Hash01(seed, s ^ 0x88u)));
                (Vector2 from, float heading) = trail[at];
                float turn = (1.1f + 0.5f * Hash01(seed, s ^ 0x99u))
                             * (Hash01(seed, s ^ 0xAAu) < 0.5f ? 1f : -1f);
                int armLength = Math.Max(MinLength,
                                         (int)((trail.Count - at) * (0.4f + 0.4f * Hash01(seed, s ^ 0xBBu))));
                Walk(comp, id, from, heading + turn, half * 0.7f, armLength, 0f, false,
                     wander, wobble, 300 + index * 13 + t, cut, seen, null);
            }

            if (!Apply(land, comp, id, second, cut, data.Fjord, mouth, head)) continue;
            data.Fjords.Add(mouth);
            return;
        }
    }

    /// <summary>The largest landmass: its label map, id and cells, and the size of the runner-up. False on an empty mask.</summary>
    private static bool Largest(bool[,] land, out int[,] comp, out int id, out List<Vector2I> cells, out int second)
    {
        int n = land.GetLength(0);
        comp = new int[n, n];
        List<List<Vector2I>> parts = Landmasses.Components(land, comp);
        id = -1;
        cells = null!;
        second = 0;
        int best = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            int size = parts[i].Count;
            if (size > best) { second = best; best = size; id = i; cells = parts[i]; }
            else if (size > second) second = size;
        }
        return id >= 0;
    }

    /// <summary>The landmass's centroid and its half-extents along <paramref name="u"/> and <paramref name="v"/>.</summary>
    private static void Extent(List<Vector2I> cells, Vector2 u, Vector2 v,
                               out Vector2 centroid, out float along, out float across)
    {
        double sx = 0, sz = 0;
        foreach (Vector2I c in cells) { sx += c.X; sz += c.Y; }
        centroid = new Vector2((float)(sx / cells.Count), (float)(sz / cells.Count));
        along = 0f;
        across = 0f;
        foreach (Vector2I c in cells)
        {
            Vector2 d = new Vector2(c.X, c.Y) - centroid;
            along = MathF.Max(along, MathF.Abs(d.Dot(u)));
            across = MathF.Max(across, MathF.Abs(d.Dot(v)));
        }
    }

    /// <summary>
    /// Walks an inlet from <paramref name="from"/> along <paramref name="heading"/>: the
    /// heading turns by <paramref name="curvature"/> a step and bends on a slow noise
    /// (more, the longer the fjord) roughened by a quick one; the half-width flares at
    /// a mouth, tapers toward the head and wobbles between. Stamps the landmass's cells
    /// within the half-width into <paramref name="cut"/>. Once inside, it narrows so the
    /// wall on either side keeps <see cref="Wall"/> cells, and stops when the length is
    /// spent, when the land ahead runs out to within a neck, or where even a one-cell
    /// inlet would thin a wall for <see cref="Strikes"/> steps running. Records the
    /// centreline in <paramref name="trail"/> if given, and returns the head.
    /// </summary>
    private static Vector2I Walk(int[,] comp, int id, Vector2 from, float heading, float half, int length,
                                 float curvature, bool fromCoast, Noise wander, Noise wobble, int lane,
                                 List<Vector2I> cut, HashSet<Vector2I> seen,
                                 List<(Vector2 Pos, float Heading)>? trail)
    {
        int n = comp.GetLength(0);
        Vector2 pos = from;
        Vector2I head = Cell(from);
        // A short fjord bends a little, a long one a lot: half a radian more over the first forty cells.
        float bend = 0.45f + 0.5f * Math.Clamp((length - 8) / 32f, 0f, 1f);
        int thin = 0;
        for (int s = 0; s <= length; s++)
        {
            float t = s / (float)length;
            // The bend comes in over the first steps, so a mouth heads inland along the
            // grain, where the land ahead was measured, and the winding builds up inside.
            float a = heading + curvature * s
                      + bend * MathF.Min(1f, s / 8f) * Contrast(wander.At(s * 0.07f, lane * 7.3f))
                      + 0.15f * Contrast(wander.At(s * 0.3f + 200f, lane * 5.1f));
            float r = half * (1f - Taper * t)
                      * (fromCoast ? 1f + Flare * MathF.Max(0f, 1f - t / 0.25f) : 1f)
                      * (0.6f + 0.8f * wobble.At(s * 0.17f, lane * 5.1f + 40f));
            if (!fromCoast || s >= Entry)
            {
                int room = Math.Min(Side(comp, id, pos, a, 1f), Side(comp, id, pos, a, -1f)) - Wall;
                thin = room < 1 ? thin + 1 : 0;
                if (thin >= Strikes) break;
                r = MathF.Min(r, MathF.Max(1f, room));
            }
            r = MathF.Max(1f, r);
            int reach = (int)MathF.Ceiling(r);
            if (Ahead(comp, id, pos, a) < Neck + reach) break;

            Stamp(comp, id, pos, r, seen, cut);
            head = Cell(pos);
            trail?.Add((pos, a));
            pos += new Vector2(MathF.Cos(a), MathF.Sin(a));
            if (!InBounds(n, Mathf.RoundToInt(pos.X), Mathf.RoundToInt(pos.Y))) break;
        }
        return head;
    }

    /// <summary>A noise value pushed toward its extremes: simplex hugs the middle, and an inlet steered by the raw value ran straight.</summary>
    private static float Contrast(float v) => Math.Clamp((v - 0.5f) * 3.2f, -1f, 1f);

    private static Vector2I Cell(Vector2 v) => new(Mathf.RoundToInt(v.X), Mathf.RoundToInt(v.Y));

    /// <summary>Cells of the landmass in a straight line from <paramref name="pos"/> along <paramref name="a"/> before anything else.</summary>
    private static int Ahead(int[,] comp, int id, Vector2 pos, float a)
        => Run(comp, id, pos, new Vector2(MathF.Cos(a), MathF.Sin(a)));

    /// <summary>Cells of the landmass beside <paramref name="pos"/>, across the heading, on the side <paramref name="sign"/> says.</summary>
    private static int Side(int[,] comp, int id, Vector2 pos, float a, float sign)
        => Run(comp, id, pos, new Vector2(-MathF.Sin(a), MathF.Cos(a)) * sign);

    private static int Run(int[,] comp, int id, Vector2 pos, Vector2 step)
    {
        int n = comp.GetLength(0);
        Vector2 q = pos;
        for (int i = 0; i < n; i++)
        {
            q += step;
            int x = Mathf.RoundToInt(q.X), z = Mathf.RoundToInt(q.Y);
            if (!InBounds(n, x, z) || comp[x, z] != id) return i;
        }
        return n;
    }

    /// <summary>Adds the landmass's cells within <paramref name="r"/> of <paramref name="pos"/> to the cut, once each.</summary>
    private static void Stamp(int[,] comp, int id, Vector2 pos, float r, HashSet<Vector2I> seen, List<Vector2I> cut)
    {
        int n = comp.GetLength(0);
        int reach = (int)MathF.Ceiling(r);
        int cx = Mathf.RoundToInt(pos.X), cz = Mathf.RoundToInt(pos.Y);
        for (int x = cx - reach; x <= cx + reach; x++)
        for (int z = cz - reach; z <= cz + reach; z++)
        {
            if (!InBounds(n, x, z) || comp[x, z] != id) continue;
            float dx = x - pos.X, dz = z - pos.Y;
            if (dx * dx + dz * dz > r * r) continue;
            var c = new Vector2I(x, z);
            if (seen.Add(c)) cut.Add(c);
        }
    }

    /// <summary>
    /// Takes the fjord out of the mask if it is long enough and parts nothing that
    /// matters: what is left of the landmass must be one piece apart from slivers
    /// under islet size, which go with it, and must still be the largest. Marks the
    /// fjord's cells; the slivers are lost coast, not fjord. Restores the mask and
    /// refuses otherwise.
    /// </summary>
    private static bool Apply(bool[,] land, int[,] comp, int id, int second, List<Vector2I> cut,
                              bool[,] mark, Vector2I from, Vector2I to)
    {
        if (cut.Count == 0) return false;
        if (Math.Max(Math.Abs(from.X - to.X), Math.Abs(from.Y - to.Y)) < MinLength) return false;

        int n = land.GetLength(0);
        foreach (Vector2I c in cut) land[c.X, c.Y] = false;

        List<List<Vector2I>> parts = Landmasses.Components(land, new int[n, n]);
        int remainder = -1, remainderSize = 0;
        var slivers = new List<List<Vector2I>>();
        for (int i = 0; i < parts.Count; i++)
        {
            Vector2I c0 = parts[i][0];
            if (comp[c0.X, c0.Y] != id) continue;         // another landmass, untouched
            if (parts[i].Count > remainderSize)
            {
                if (remainder >= 0) slivers.Add(parts[remainder]);
                remainder = i;
                remainderSize = parts[i].Count;
            }
            else slivers.Add(parts[i]);
        }

        bool severs = remainderSize <= second;
        foreach (List<Vector2I> piece in slivers)
            if (piece.Count >= Landmasses.MinIsletCells) severs = true;
        if (severs)
        {
            foreach (Vector2I c in cut) land[c.X, c.Y] = true;
            return false;
        }

        foreach (Vector2I c in cut) mark[c.X, c.Y] = true;
        foreach (List<Vector2I> piece in slivers)
            foreach (Vector2I c in piece) land[c.X, c.Y] = false;
        return true;
    }
}
