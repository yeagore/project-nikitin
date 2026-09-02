using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>
/// The site search: one hanging-Gate site per edge, chosen as a set by a backtracking search
/// over each edge's best-scored coast cells, relaxing the rules one rung at a time.
/// </summary>
internal static partial class GatePlacement
{
    /// <summary>
    /// How far the rules may bend to get four Gates placed. Rungs relax one rule at a time in
    /// declared order; <see cref="Desperate"/> drops the dominance order too.
    /// </summary>
    private enum Ease
    {
        /// <summary>Every rule: the band, the corners, the separation, the order.</summary>
        Full = 0,

        /// <summary>The edge band widens to <see cref="RelaxedEdgeBand"/>.</summary>
        Band = 1,

        /// <summary>The corners go too: a coast may only offer its ends.</summary>
        Anywhere = 2,

        /// <summary>Separation falls to <see cref="CrowdedSeparation"/>.</summary>
        Crowded = 3,

        /// <summary>Separation halves again and the dominance order goes.</summary>
        Desperate = 4,
    }

    /// <summary>One place a Gate could go, before it is known what kind it is.</summary>
    private readonly record struct Site(Cardinal Edge, int X, int Z, short Level,
                                        int Apron, float Score)
    {
        public Vector2I Head => new(X, Z);

        /// <summary>No site on this edge; <see cref="IslandData.NoLand"/> as the level is the sentinel.</summary>
        public bool IsEmpty => Level == IslandData.NoLand;

        public static Site None(Cardinal edge) => new(edge, -1, -1, IslandData.NoLand, 0, 0f);
    }

    /// <summary>
    /// One site per edge, chosen together: each edge offers its best <see cref="CandidatesPerEdge"/>
    /// sites and the search takes the first combination where every pair is apart and in order.
    /// An edge with no candidate at all comes back empty.
    /// </summary>
    private static Site[] ChooseSites(int seed, IslandData d)
    {
        var edges = Enum.GetValues<Cardinal>();
        var best = new Site[edges.Length];
        for (int i = 0; i < best.Length; i++)
            best[i] = Site.None(edges[i]);

        Frame bounds = Bounds(d);

        foreach (Ease ease in Enum.GetValues<Ease>())
        {
            var pool = new List<Site>[edges.Length];
            for (int i = 0; i < edges.Length; i++)
                pool[i] = Candidates(seed, d, bounds, edges[i], ease);

            var picked = new Site[edges.Length];
            int budget = 20000;
            if (!Assign(d, pool, picked, 0, ease, ref budget)) continue;

            // A looser rung can only offer more Gates, but worse-founded ones: keep its
            // answer only if it filled an edge the stricter rungs could not.
            int had = 0, got = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                if (!best[i].IsEmpty) had++;
                if (!picked[i].IsEmpty) got++;
            }
            if (got > had) best = picked;
            if (got == edges.Length) break;
        }
        return best;
    }

    /// <summary>
    /// Depth-first over the edges, taking each edge's candidates in score order and keeping any
    /// that agree with what is already chosen. An edge with no workable candidate is left empty
    /// rather than failing the whole assignment.
    /// </summary>
    private static bool Assign(IslandData d, List<Site>[] pool, Site[] picked, int edge,
                               Ease ease, ref int budget)
    {
        if (edge == pool.Length) return true;
        if (budget-- <= 0) return false;

        foreach (Site site in pool[edge])
        {
            bool ok = true;
            for (int i = 0; i < edge && ok; i++)
            {
                if (picked[i].IsEmpty) continue;
                ok = Compatible(picked[i], site, ease, d);
            }
            if (!ok) continue;

            picked[edge] = site;
            if (Assign(d, pool, picked, edge + 1, ease, ref budget)) return true;
        }

        picked[edge] = Site.None(pool[edge].Count > 0 ? pool[edge][0].Edge : default);
        return Assign(d, pool, picked, edge + 1, ease, ref budget);
    }

    /// <summary>
    /// Whether two sites can both be Gates: far enough apart, and each the outermost of the two
    /// in its own direction. Distance is measured on the ground, not portal to portal.
    /// </summary>
    private static bool Compatible(Site a, Site b, Ease ease, IslandData d)
    {
        float share = ease < Ease.Crowded ? GateSeparation
                    : ease < Ease.Desperate ? CrowdedSeparation
                    : MinSeparation;
        int apart = (int)(share * d.Size);
        if (Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z) < apart) return false;

        if (ease >= Ease.Desperate) return true;         // the order is the last to give

        Vector2I da = a.Edge.Outward(), db = b.Edge.Outward();
        int aOnA = a.X * da.X + a.Z * da.Y, bOnA = b.X * da.X + b.Z * da.Y;
        int bOnB = b.X * db.X + b.Z * db.Y, aOnB = a.X * db.X + a.Z * db.Y;
        return aOnA >= bOnA + DominanceMargin && bOnB >= aOnB + DominanceMargin;
    }

    /// <summary>
    /// Every place on one edge a hanging Gate could go, best first: a coast cell with aether
    /// directly outward, a strip of usable ground behind it and a flight path in. It need not
    /// be level; the strip is levelled once chosen.
    /// </summary>
    private static List<Site> Candidates(int seed, IslandData d, Frame bounds, Cardinal edge,
                                         Ease ease)
    {
        int n = d.Size;
        Vector2I outward = edge.Outward();
        Vector2I across = edge.Across();

        var found = new List<Site>();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Usable(d, x, z)) continue;
            if (!OnEdge(d, bounds, x, z, outward, across, ease)) continue;
            if (!HasStrip(d, x, z, outward, ease)) continue;

            short level = StripTop(d, x, z, outward);
            if (!Flyable(d, x, z, outward, level)) continue;

            found.Add(new Site(edge, x, z, level, ApronAt(d, x, z),
                               Score(seed, bounds, d, x, z, outward, across)));
        }

        found.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (found.Count > CandidatesPerEdge) found.RemoveRange(CandidatesPerEdge,
                                                               found.Count - CandidatesPerEdge);
        return found;
    }

    /// <summary>The rules about one Gate alone: near its own edge, and clear of the corners. Its relation to the other Gates is <see cref="Compatible"/>.</summary>
    private static bool OnEdge(IslandData d, Frame bounds, int x, int z,
                               Vector2I outward, Vector2I across, Ease ease)
    {
        if (!bounds.Any) return true;

        int along = x * outward.X + z * outward.Y;
        float share = ease < Ease.Band ? EdgeBand : RelaxedEdgeBand;
        int floor = ease < Ease.Band ? MinEdgeBand : MinEdgeBand * 2;
        int band = Math.Max(floor, (int)(share * bounds.Extent(outward)));
        if (along < bounds.Extreme(outward) - band) return false;

        if (ease < Ease.Anywhere)
        {
            int side = x * across.X + z * across.Y;
            int span = bounds.Extent(across);
            int inset = (int)(CornerInset * span);
            int high = bounds.Extreme(across);
            if (side > high - inset || side < high - span + inset) return false;
        }
        return true;
    }

    /// <summary>
    /// Ground for a landing strip: <see cref="StripLength"/> usable cells running inland from a
    /// coast cell, one wide. It exists and is not a hillside; <see cref="LevelStrips"/> makes it flat.
    /// </summary>
    private static bool HasStrip(IslandData d, int x, int z, Vector2I outward, Ease ease)
    {
        int n = d.Size;
        if (!Usable(d, x, z)) return false;

        // The head of the strip: land with aether directly outward of it.
        int hx = x + outward.X, hz = z + outward.Y;
        if (InBounds(n, hx, hz) && d.HasLand(hx, hz)) return false;

        short lowest = short.MaxValue, highest = short.MinValue;
        for (int along = 0; along < StripLength; along++)
        {
            int sx = x - outward.X * along, sz = z - outward.Y * along;
            if (!Usable(d, sx, sz)) return false;

            short here = d.SurfaceLevel(sx, sz);
            lowest = Math.Min(lowest, here);
            highest = Math.Max(highest, here);
        }

        int tolerance = ease < Ease.Crowded ? StripTolerance : StripTolerance * 2;
        return highest - lowest <= tolerance;
    }

    /// <summary>The level a strip is levelled to: the ground at its inner end, the cell that joins the island.</summary>
    private static short StripTop(IslandData d, int x, int z, Vector2I outward)
    {
        int ix = x - outward.X * (StripLength - 1), iz = z - outward.Y * (StripLength - 1);
        return Usable(d, ix, iz) ? d.SurfaceLevel(ix, iz) : d.SurfaceLevel(x, z);
    }

    /// <summary>
    /// Whether a vessel could fly in to that strip and the portal would hang clear. The approach
    /// is tested against the sill (two slabs above the strip), so low ground under the flight
    /// path is scenery; the corridor is the portal's cell with a cell of margin either side.
    /// </summary>
    private static bool Flyable(IslandData d, int x, int z, Vector2I outward, short level)
    {
        int n = d.Size;
        int sill = level + 2;
        var across = new Vector2I(-outward.Y, outward.X);

        // The portal must stand inside the bounding box: the grid is its walls.
        int px = x + outward.X * HangingOffset, pz = z + outward.Y * HangingOffset;
        if (!InBounds(n, px, pz)) return false;

        for (int step = 1; step <= HangingOffset; step++)
        for (int side = -1; side <= 1; side++)
        {
            int gx = x + outward.X * step + across.X * side;
            int gz = z + outward.Y * step + across.Y * side;
            if (!InBounds(n, gx, gz) || !d.HasLand(gx, gz)) continue;
            if (d.SurfaceLevel(gx, gz) >= sill) return false;
        }

        // The portal itself hangs clear, and so does the air immediately behind it.
        for (int step = HangingOffset - HangingClearance + 1; step <= HangingOffset; step++)
        {
            int gx = x + outward.X * step, gz = z + outward.Y * step;
            if (InBounds(n, gx, gz) && d.HasLand(gx, gz)) return false;
        }
        return true;
    }

    /// <summary>The bounds of the ground a Gate may use, for the edge rules.</summary>
    private readonly struct Frame
    {
        public readonly int MinX, MaxX, MinZ, MaxZ;
        public readonly bool Any;

        public Frame(int minX, int maxX, int minZ, int maxZ, bool any)
        {
            MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ; Any = any;
        }

        /// <summary>The outermost usable cell in a direction, as a dot product.</summary>
        public int Extreme(Vector2I dir)
            => dir.X > 0 ? MaxX : dir.X < 0 ? -MinX : dir.Y > 0 ? MaxZ : -MinZ;

        /// <summary>How wide the usable ground is along a direction, in cells.</summary>
        public int Extent(Vector2I dir)
            => dir.X != 0 ? MaxX - MinX : MaxZ - MinZ;
    }

    /// <summary>Bounding box of the ground a Gate may use.</summary>
    private static Frame Bounds(IslandData d)
    {
        int n = d.Size;
        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Usable(d, x, z)) continue;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (z < minZ) minZ = z;
            if (z > maxZ) maxZ = z;
        }
        return maxX < minX ? new Frame(0, n - 1, 0, n - 1, false)
            : new Frame(minX, maxX, minZ, maxZ, true);
    }

    /// <summary>
    /// How good an allowed site is: far out on its own side, near the middle of it, with the
    /// apron and a little noise as tie-breaks. Roughness is a tie-break too, not a gate.
    /// </summary>
    private static float Score(int seed, Frame bounds, IslandData d, int x, int z,
                               Vector2I outward, Vector2I across)
    {
        int along = x * outward.X + z * outward.Y;
        int side = x * across.X + z * across.Y;
        float middle = bounds.Extreme(across) - bounds.Extent(across) * 0.5f;

        short lowest = short.MaxValue, highest = short.MinValue;
        for (int i = 0; i < StripLength; i++)
        {
            int sx = x - outward.X * i, sz = z - outward.Y * i;
            if (!Usable(d, sx, sz)) continue;
            short here = d.SurfaceLevel(sx, sz);
            lowest = Math.Min(lowest, here);
            highest = Math.Max(highest, here);
        }
        int roughness = highest > lowest ? highest - lowest : 0;

        return along
               - MathF.Abs(side - middle) * 0.35f
               - roughness * 1.5f
               + ApronAt(d, x, z) * 0.01f
               + Hash01(seed, 0x2200u ^ (uint)(x * 733 + z)) * 0.5f;
    }

    /// <summary>
    /// The best buildable shelf within <see cref="ApronSearch"/> of a point, read off
    /// <see cref="IslandData.ShelfId"/>; searched around rather than under, because a coast
    /// cell is rarely on a shelf itself.
    /// </summary>
    private static int ApronAt(IslandData d, int x, int z)
    {
        int best = 0;
        for (int dx = -ApronSearch; dx <= ApronSearch; dx++)
        for (int dz = -ApronSearch; dz <= ApronSearch; dz++)
        {
            int ax = x + dx, az = z + dz;
            if (!InBounds(d.Size, ax, az)) continue;

            int id = d.ShelfId[ax, az];
            if (id < 0 || id >= d.Shelves.Count) continue;

            Shelf shelf = d.Shelves[id];
            if (shelf.Width >= Traversal.MinShelfWidth) best = Math.Max(best, shelf.Area);
        }
        return best;
    }

    /// <summary>Ground a Gate may be built on or served by: dry, and part of the heartland.</summary>
    private static bool Usable(IslandData d, int x, int z)
    {
        int n = d.Size;
        if (!InBounds(n, x, z)) return false;
        if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) return false;
        return d.Heartland >= 0 && d.Reach[x, z] == d.Heartland;
    }

    /// <summary>
    /// Audit diagnostic: how many cells on one edge survive each site test in turn, at the
    /// top rung or (<paramref name="loose"/>) the bottom one. Existing Gates do not count against it.
    /// </summary>
    public static (int Usable, int Fits, int Strip, int Flyable) Funnel(
        IslandData d, Cardinal edge, bool loose = false)
    {
        int n = d.Size;
        Frame bounds = Bounds(d);
        Vector2I outward = edge.Outward();
        Vector2I across = edge.Across();
        Ease ease = loose ? Ease.Desperate : Ease.Full;

        int usable = 0, fits = 0, strip = 0, flyable = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!Usable(d, x, z)) continue;
            usable++;
            if (!OnEdge(d, bounds, x, z, outward, across, ease)) continue;
            fits++;
            if (!HasStrip(d, x, z, outward, ease)) continue;
            strip++;
            if (!Flyable(d, x, z, outward, StripTop(d, x, z, outward))) continue;
            flyable++;
        }
        return (usable, fits, strip, flyable);
    }
}
