using System;
using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>One connected set of ground you can walk without infrastructure.</summary>
/// <param name="Id">Index into <see cref="IslandData.Areas"/>; also the value in <see cref="IslandData.Walk"/>.</param>
/// <param name="Area">Cells.</param>
/// <param name="Low">Lowest surface slab in the set.</param>
/// <param name="High">Highest surface slab in the set.</param>
/// <param name="Min">Bounding box corner, cells.</param>
/// <param name="Max">Bounding box corner, cells.</param>
public readonly record struct WalkArea(int Id, int Area, short Low, short High,
                                       Vector2I Min, Vector2I Max)
{
    /// <summary>
    /// Big enough to be somewhere, rather than a ledge. Anything under this is
    /// <b>broken ground</b>: the contour benches of a mountain flank, a shelf on
    /// an escarpment. They are real terrain, but treating each as a place would
    /// drown the map in hundreds of one-cell "districts".
    /// </summary>
    public bool IsDistrict => Area >= Traversal.MinDistrictArea;
}

/// <summary>
/// A flat shelf: contiguous ground all at one slab level. What the settlement
/// layer will place on. <c>Width</c> is the largest square that fits inside it,
/// in cells — a long one-cell ledge has a big <c>Area</c> and a <c>Width</c> of 1.
/// </summary>
public readonly record struct Shelf(int Id, short Level, int Area, int Width,
                                    Vector2I Min, Vector2I Max, Vector2I Center)
{
    /// <summary>Somewhere a settlement could actually prosper — see docs §1.4.</summary>
    public bool Buildable => Area >= Traversal.MinShelfArea && Width >= Traversal.MinShelfWidth;
}

/// <summary>
/// Reads walkability off finished terrain: which ground connects to which under
/// the traversal rule, and which of it is flat enough to build on. Pure analysis
/// — it never changes the terrain, so it can run on any <see cref="IslandData"/>.
///
/// The traversal rule (CLAUDE.md): a <b>one-slab</b> step is free, a face of two
/// or more is an obstacle. Standing water is not walkable ground.
/// </summary>
public static class Traversal
{
    /// <summary>Below this, a walk area is broken ground rather than a place.</summary>
    public const int MinDistrictArea = 20;

    /// <summary>Smallest shelf a settlement could use, in cells.</summary>
    public const int MinShelfArea = 24;

    /// <summary>
    /// Narrowest shelf a settlement could use. A one-cell ledge is not something
    /// anyone prospers on, however long it runs.
    /// </summary>
    public const int MinShelfWidth = 3;

    /// <summary>Value in <see cref="IslandData.Walk"/> for a flooded column.</summary>
    public const int Water = -2;

    /// <summary>
    /// The tallest face a built stair or hoist is assumed to span, in slabs — the
    /// line between "needs infrastructure" and "cannot be reached at all". Eight
    /// slabs is two world units; it clears any mesa (max 7) and any basin rim,
    /// and does not clear a mountain's mid-flank risers, which run to 15.
    /// </summary>
    public const int InfrastructureStep = 8;

    /// <summary>
    /// Cells of gap a bridge spans. Two, and cardinal only — a diagonal crossing
    /// is not a thing you can build on a square grid without it reading as a
    /// mistake. So two banks are linkable when land faces land across at most two
    /// cells of water or aether.
    /// </summary>
    public const int MaxBridgeSpan = 2;

    /// <summary>
    /// How far a bridge's two ends may differ in height, in slabs. The same as
    /// <see cref="InfrastructureStep"/>, and for the same reason: a bridge is a
    /// built thing and so are its approaches, so a deck that meets a stair at one
    /// end is a perfectly ordinary crossing. Holding bridges to a level deck was
    /// tried and it made most archipelagos unlinkable — two islets have no reason
    /// to agree on a rung.
    /// </summary>
    public const int MaxBridgeRise = InfrastructureStep;

    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };

    /// <summary>
    /// Fills <c>Walk</c> / <c>Areas</c> / <c>Mainland</c> (what you can cross on
    /// foot), <c>Reach</c> / <c>Reaches</c> / <c>Heartland</c> (what you could
    /// cross once stairs and bridges are built), and <c>ShelfId</c> /
    /// <c>Shelves</c> (what you could build on).
    /// </summary>
    public static void Analyse(IslandData d)
    {
        BuildWalkAreas(d);
        BuildReachAreas(d);
        BuildShelves(d);
    }

    /// <summary>
    /// True where a column is ground you could stand on — or wade across.
    ///
    /// A stream is one slab deep, which is the same step that makes a hillside
    /// free, so it is <b>fordable</b>: a watercourse running the length of an
    /// island should not cut it in two. A navigable river is two cells wide and
    /// meant for barges, and a lake has a bed three or four slabs down; neither is
    /// something you walk through.
    /// </summary>
    private static bool Walkable(IslandData d, int x, int z)
    {
        int n = d.Size;
        if (x < 0 || z < 0 || x >= n || z >= n) return false;
        if (!d.HasLand(x, z)) return false;
        if (d.WaterLevel[x, z] == IslandData.NoLand) return true;
        return d.River[x, z] && !d.Navigable[x, z];
    }

    private static void BuildWalkAreas(IslandData d)
    {
        int n = d.Size;
        var areas = new List<WalkArea>();
        var queue = new Queue<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            d.Walk[x, z] = d.HasLand(x, z) && !Walkable(d, x, z) ? Water : -1;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Walkable(d, sx, sz) || d.Walk[sx, sz] != -1) continue;

            int id = areas.Count;
            int area = 0;
            short low = short.MaxValue, high = short.MinValue;
            var min = new Vector2I(sx, sz);
            var max = new Vector2I(sx, sz);

            d.Walk[sx, sz] = id;
            queue.Enqueue((sx, sz));

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                short top = d.SurfaceLevel(x, z);
                area++;
                if (top < low) low = top;
                if (top > high) high = top;
                min = new Vector2I(Math.Min(min.X, x), Math.Min(min.Y, z));
                max = new Vector2I(Math.Max(max.X, x), Math.Max(max.Y, z));

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Walkable(d, nx, nz) || d.Walk[nx, nz] != -1) continue;
                    // The rule, and the only edge test there is: one slab is free.
                    if (Math.Abs(d.SurfaceLevel(nx, nz) - top) > 1) continue;
                    d.Walk[nx, nz] = id;
                    queue.Enqueue((nx, nz));
                }
            }
            areas.Add(new WalkArea(id, area, low, high, min, max));
        }

        // Ranked by area, so the lab can give the biggest places the clearest
        // colours and lump the shrapnel together. Ids are rewritten to match, so
        // area 0 is always the mainland.
        var order = new List<WalkArea>(areas);
        order.Sort((a, b) => b.Area.CompareTo(a.Area));

        var remap = new int[areas.Count];
        for (int i = 0; i < order.Count; i++) remap[order[i].Id] = i;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.Walk[x, z] >= 0) d.Walk[x, z] = remap[d.Walk[x, z]];

        d.Areas.Clear();
        for (int i = 0; i < order.Count; i++) d.Areas.Add(order[i] with { Id = i });
        d.Mainland = d.Areas.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// The same connectivity question asked of a player who can build. Two ground
    /// cells join when the face between them is at most
    /// <see cref="InfrastructureStep"/> slabs — a stair or a hoist — or when land
    /// faces land across at most <see cref="MaxBridgeSpan"/> cells of water or
    /// aether, which is a bridge.
    ///
    /// This is the connectivity the design should actually be held to. Walking is
    /// the *free* case; a cliff is meant to be an obstacle that costs something,
    /// not a wall. What matters is whether the cost can be paid at all.
    /// </summary>
    private static void BuildReachAreas(IslandData d)
    {
        int n = d.Size;
        var areas = new List<WalkArea>();
        var queue = new Queue<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            d.Reach[x, z] = d.HasLand(x, z) && !Walkable(d, x, z) ? Water : -1;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Walkable(d, sx, sz) || d.Reach[sx, sz] != -1) continue;

            int id = areas.Count;
            int area = 0;
            short low = short.MaxValue, high = short.MinValue;
            var min = new Vector2I(sx, sz);
            var max = new Vector2I(sx, sz);

            d.Reach[sx, sz] = id;
            queue.Enqueue((sx, sz));

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                short top = d.SurfaceLevel(x, z);
                area++;
                if (top < low) low = top;
                if (top > high) high = top;
                min = new Vector2I(Math.Min(min.X, x), Math.Min(min.Y, z));
                max = new Vector2I(Math.Max(max.X, x), Math.Max(max.Y, z));

                for (int k = 0; k < 4; k++)
                {
                    // Step 1 is the neighbour; steps 2 and 3 are a bridge over one
                    // or two cells of nothing. Anything solid in between is not a
                    // gap, so it is the neighbour case or nothing.
                    for (int reach = 1; reach <= MaxBridgeSpan + 1; reach++)
                    {
                        int nx = x + Dx[k] * reach, nz = z + Dz[k] * reach;
                        if (!Walkable(d, nx, nz)) continue;

                        bool bridged = reach > 1;
                        if (bridged)
                        {
                            bool clear = true;
                            for (int step = 1; step < reach && clear; step++)
                                clear = !Walkable(d, x + Dx[k] * step, z + Dz[k] * step);
                            if (!clear) continue;
                        }

                        int rise = Math.Abs(d.SurfaceLevel(nx, nz) - top);
                        if (rise > (bridged ? MaxBridgeRise : InfrastructureStep)) continue;
                        if (d.Reach[nx, nz] != -1) continue;

                        d.Reach[nx, nz] = id;
                        queue.Enqueue((nx, nz));
                    }
                }
            }
            areas.Add(new WalkArea(id, area, low, high, min, max));
        }

        var order = new List<WalkArea>(areas);
        order.Sort((a, b) => b.Area.CompareTo(a.Area));

        var remap = new int[areas.Count];
        for (int i = 0; i < order.Count; i++) remap[order[i].Id] = i;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.Reach[x, z] >= 0) d.Reach[x, z] = remap[d.Reach[x, z]];

        d.Reaches.Clear();
        for (int i = 0; i < order.Count; i++) d.Reaches.Add(order[i] with { Id = i });
        d.Heartland = d.Reaches.Count > 0 ? 0 : -1;
    }

    private static void BuildShelves(IslandData d)
    {
        int n = d.Size;
        var shelves = new List<Shelf>();
        var queue = new Queue<(int X, int Z)>();
        var cells = new List<Vector2I>();
        var claimed = new bool[n, n];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) d.ShelfId[x, z] = -1;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Walkable(d, sx, sz) || claimed[sx, sz]) continue;

            short level = d.SurfaceLevel(sx, sz);
            cells.Clear();
            claimed[sx, sz] = true;
            queue.Enqueue((sx, sz));

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                cells.Add(new Vector2I(x, z));
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Walkable(d, nx, nz) || claimed[nx, nz]) continue;
                    if (d.SurfaceLevel(nx, nz) != level) continue;
                    claimed[nx, nz] = true;
                    queue.Enqueue((nx, nz));
                }
            }

            // Below half the settlement minimum a shelf is not worth carrying;
            // the list exists for placement, not for a census of every flat cell.
            if (cells.Count < MinShelfArea / 2) continue;

            int id = shelves.Count;
            Vector2I min = cells[0], max = cells[0];
            foreach (Vector2I c in cells)
            {
                min = new Vector2I(Math.Min(min.X, c.X), Math.Min(min.Y, c.Y));
                max = new Vector2I(Math.Max(max.X, c.X), Math.Max(max.Y, c.Y));
                d.ShelfId[c.X, c.Y] = id;
            }

            (int width, Vector2I center) = WidestSquare(n, cells);
            shelves.Add(new Shelf(id, level, cells.Count, width, min, max, center));
        }

        d.Shelves.Clear();
        d.Shelves.AddRange(shelves);
    }

    /// <summary>
    /// The largest square of shelf that fits inside it, by repeated 8-way erosion,
    /// plus the cell at that square's centre. Erosion rather than area is what the
    /// requirement actually asks for: a ledge fifty cells long and one deep has
    /// ample area and is still not somewhere anyone can settle.
    /// </summary>
    private static (int Width, Vector2I Center) WidestSquare(int n, List<Vector2I> cells)
    {
        var alive = new HashSet<Vector2I>(cells);
        Vector2I best = cells[0];
        int rings = 0;

        while (alive.Count > 0)
        {
            var next = new HashSet<Vector2I>();
            foreach (Vector2I c in alive)
            {
                bool solid = true;
                for (int dx = -1; dx <= 1 && solid; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = c.X + dx, nz = c.Y + dz;
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n
                        || !alive.Contains(new Vector2I(nx, nz)))
                    {
                        solid = false;
                        break;
                    }
                }
                if (solid) next.Add(c);
            }
            if (next.Count == 0) break;

            rings++;
            // Eroding against the previous ring is what makes the count a radius
            // rather than merely "has eight neighbours".
            foreach (Vector2I c in next) { best = c; break; }
            alive = next;
        }
        return (2 * rings + 1, best);
    }
}
