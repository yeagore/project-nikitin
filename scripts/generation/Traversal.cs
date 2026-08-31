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
/// A <b>shelf</b>: ground level enough to lay a settlement out on, and the
/// generator's whole answer to "where could a town go?". The settlement layer
/// reads this instead of re-deriving slopes, and a Gate is only allowed where
/// one of these serves it (its apron).
///
/// It is not merely flat ground. Requirement §1.4 asks for terrain "mostly flat
/// with an occasional single-slab step" — a yard, a terrace, a river meadow that
/// loses a slab as it runs — so a shelf is a run of ground where <i>each</i>
/// cell is flat or sits at one lone step, and no two neighbours differ by more
/// than a slab. That admits a gently descending terrace and still rejects a
/// hillside, where every cell steps against most of its neighbours.
///
/// <see cref="Width"/> is the largest square that fits inside, in cells: a
/// fifty-cell ledge one cell deep has ample <see cref="Area"/> and is nowhere
/// anyone can settle, which is exactly the distinction placement needs.
/// </summary>
public readonly record struct Shelf(int Id, short Level, short Top, int Area, int Width,
                                    Vector2I Min, Vector2I Max, Vector2I Center)
{
    /// <summary>Slabs from the shelf's lowest cell to its highest — its total descent.</summary>
    public int Drop => Top - Level;

    /// <summary>Somewhere a settlement could actually prosper — see docs §1.4.</summary>
    public bool Buildable => Area >= Traversal.MinShelfArea && Width >= Traversal.MinShelfWidth;
}

/// <summary>
/// Reads walkability off finished terrain: which ground connects to which under
/// the traversal rule, and which of it is level enough to build on. Pure
/// analysis — it never changes the terrain, so it can run on any
/// <see cref="IslandData"/>.
///
/// The traversal rule (CLAUDE.md): a <b>one-slab</b> step is free, a face of two
/// or more is an obstacle. Standing water is not walkable ground, but a stream
/// one slab deep is forded — see <see cref="CrossLevel"/>.
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

    /// <summary>
    /// How many of a cell's four neighbours may stand at a different level before
    /// it stops being shelf. One: a cell may sit at the top or the foot of a
    /// single step and still be ground you would lay a yard on, but a cell
    /// stepping against two or more of its neighbours is a hillside.
    /// </summary>
    public const int ShelfSteps = 1;

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
    /// Cells of gap a bridge spans when nothing says otherwise. The real figure
    /// is per-Domain — <see cref="IslandData.BridgeSpan"/>, set from
    /// <see cref="IslandParams.Crossings"/> — because how far you can build
    /// across is a difficulty knob rather than a constant of the world.
    /// </summary>
    public const int DefaultBridgeSpan = (int)BridgeEase.Medium;

    /// <summary>
    /// How far a bridge's two ends may differ in height, in slabs. <b>Two.</b>
    ///
    /// A bridge is a run of slabs at one level, so its deck cannot climb: you
    /// step one slab onto it at one end and one slab off it at the other, and
    /// that is the whole allowance. (It used to be the full stair height, which
    /// quietly meant a "bridge" whose far end stood eight slabs up — a lift with
    /// a deck attached. The generator now levels both bridgeheads instead, so the
    /// crossings an arrangement depends on are level by construction.)
    /// </summary>
    public const int MaxBridgeRise = 2;

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
        BuildWaterBodies(d);
        BuildBerths(d);
        PruneBerths(d);
        BuildReachAreas(d);
        BuildShelves(d);
    }

    /// <summary>
    /// Throws away every ferry berth that would not join anything.
    ///
    /// <para>A quay beside water is easy to find — the domino rule fits most of
    /// every lake shore, and the first version recorded three thousand of them per
    /// audit. Nearly all were on water you could simply walk round, which makes
    /// them scenery: a ferry from a place to itself.</para>
    ///
    /// <para>So the reach flood is run <b>once without ferries</b>, and a body of
    /// water keeps its berths only if they land in two or more different pieces of
    /// that answer — the definition of the ferry being load-bearing. What is left
    /// is the crossings that exist because the water is genuinely in the way, which
    /// is the only kind worth drawing, costing or building.</para>
    /// </summary>
    private static void PruneBerths(IslandData d)
    {
        // Kept as a diagnostic, because "48 berths" and "48 berths out of 3,000
        // sites, on the one island in sixty where water is genuinely in the way"
        // are the same number saying two very different things — and the second is
        // the one that tells you whether the pruning is right.
        d.BerthSites = d.Berths.Count;
        if (d.Berths.Count == 0) return;

        int n = d.Size;
        var dry = new int[n, n];
        BuildReachAreas(d, ferries: false, into: dry);

        // Which pieces of the ferry-less answer each body of water touches.
        var touches = new Dictionary<int, HashSet<int>>();
        foreach (FerryBerth berth in d.Berths)
        {
            if (berth.Body < 0) continue;
            int piece = dry[berth.Land.X, berth.Land.Y];
            if (piece < 0) continue;
            if (!touches.TryGetValue(berth.Body, out HashSet<int>? seen))
                touches[berth.Body] = seen = new HashSet<int>();
            seen.Add(piece);
        }

        var kept = new List<FerryBerth>();
        foreach (FerryBerth berth in d.Berths)
            if (berth.Body >= 0 && touches.TryGetValue(berth.Body, out HashSet<int>? seen)
                && seen.Count > 1)
                kept.Add(berth);

        d.Berths.Clear();
        d.Berths.AddRange(kept);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) d.Ferry[x, z] = false;
        foreach (FerryBerth berth in d.Berths) d.Ferry[berth.Land.X, berth.Land.Y] = true;
    }

    /// <summary>
    /// Widest gap a bridge may span over <b>water</b>, in cells. Three, whatever
    /// the Domain's <see cref="IslandData.BridgeSpan"/> allows over aether — and
    /// never more than that span, since a Domain where one cell is the limit does
    /// not get a longer bridge for being wet.
    ///
    /// A bridge over aether is a deck between two rims and can be built out from
    /// either end; a bridge over water has piers in it, and past three cells the
    /// thing you build is a ferry.
    /// </summary>
    public const int WaterBridgeSpan = 3;

    /// <summary>
    /// How far below a deck the ground under it must lie before that ground counts
    /// as a <b>chasm</b> rather than as something in the way, in slabs.
    ///
    /// Five. A bridge from one cliff top to another across a canyon is the same
    /// structure as a bridge between two islands — a level deck between two banks
    /// — and the only thing that makes it a bridge rather than a detour is that
    /// what is under it is far enough down to be worth spanning. Below five you
    /// would walk down and up; at five the descent already costs a stair each way,
    /// so one deck is the cheaper answer as well as the truer one.
    /// </summary>
    public const int ChasmDrop = 5;

    /// <summary>
    /// Whether a level deck could run from one cell to another <paramref name="reach"/>
    /// cells away in a cardinal direction — the one place the three kinds of gap
    /// are told apart.
    ///
    /// <list type="bullet">
    /// <item><b>Aether</b> — no column at all. Spans up to the Domain's bridge span.</item>
    /// <item><b>Water</b> — a flooded column whose surface is near the deck. Spans
    /// up to <see cref="WaterBridgeSpan"/>, because a deck over water has piers in
    /// it.</item>
    /// <item><b>Chasm</b> — ground (wet or dry) lying <see cref="ChasmDrop"/> or
    /// more below the deck: a canyon, a gorge, the plain between two mesas. Spans
    /// like aether, since nothing has to stand in it either.</item>
    /// </list>
    ///
    /// Anything else — ground at about deck height — is not a gap at all, and the
    /// deck is refused.
    /// </summary>
    public static bool DeckFits(IslandData d, int x, int z, int dx, int dz, int reach, int span)
    {
        int n = d.Size;
        int gap = reach - 1;
        if (gap < 1) return true;
        if (gap > span) return false;

        int fx = x + dx * reach, fz = z + dz * reach;
        int deck = Math.Min(CrossLevel(d, x, z), CrossLevel(d, fx, fz));
        bool overWater = false;

        for (int step = 1; step < reach; step++)
        {
            int mx = x + dx * step, mz = z + dz * step;
            if (mx < 0 || mz < 0 || mx >= n || mz >= n) continue;
            if (!d.HasLand(mx, mz)) continue;                       // aether

            // What the deck has to clear: a water surface where there is one, the
            // ground where there is not.
            int head = d.WaterLevel[mx, mz] != IslandData.NoLand
                ? d.WaterLevel[mx, mz]
                : d.SurfaceLevel(mx, mz);
            if (head <= deck - ChasmDrop) continue;                 // a chasm under the deck

            if (Walkable(d, mx, mz)) return false;                  // ground in the way
            overWater = true;
        }
        return !overWater || gap <= Math.Min(span, WaterBridgeSpan);
    }

    /// <summary>
    /// How far above the water a quay may stand, in slabs. One is the shore the
    /// terrain actually leaves — <c>LevelShores</c> brings every bank down to the
    /// free step — and two is the slack for a bank a river cut and left proud.
    /// Anything higher is a cliff over the water, and you do not land a barge at
    /// the foot of one.
    /// </summary>
    public const int MaxQuayRise = 2;

    /// <summary>
    /// Water a ferry works on: standing water, or a river too deep to wade.
    ///
    /// A stream is forded for nothing, so a ferry across one would be
    /// infrastructure bought to replace a free step — see <see cref="FerryBerth"/>.
    /// </summary>
    public static bool Sailable(IslandData d, int x, int z)
    {
        int n = d.Size;
        if (x < 0 || z < 0 || x >= n || z >= n) return false;
        if (!d.HasLand(x, z) || d.WaterLevel[x, z] == IslandData.NoLand) return false;
        return !d.River[x, z] || d.Navigable[x, z];
    }

    /// <summary>
    /// Labels every flooded column with the body of water it belongs to, and
    /// <b>cuts a body at every waterfall</b>: the pool above a fall and the pool
    /// below it are the same river and not the same water, because nothing sails
    /// up a fall. That is the whole content of "two ferries are linked if they
    /// connect through water without falls in between".
    /// </summary>
    private static void BuildWaterBodies(IslandData d)
    {
        int n = d.Size;

        // The links a fall severs, both ways round, keyed by the two cells.
        var cut = new HashSet<(int, int, int, int)>();
        foreach (Fall f in d.Falls)
        {
            if (f.OffRim) continue;                       // it leaves the Domain
            int tx = f.Cell.X + f.Flow.X, tz = f.Cell.Y + f.Flow.Y;
            cut.Add((f.Cell.X, f.Cell.Y, tx, tz));
            cut.Add((tx, tz, f.Cell.X, f.Cell.Y));
        }

        var queue = new Queue<(int X, int Z)>();
        int bodies = 0;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Sailable(d, sx, sz) || d.WaterBody[sx, sz] >= 0) continue;

            int id = bodies++;
            d.WaterBody[sx, sz] = id;
            queue.Enqueue((sx, sz));

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Sailable(d, nx, nz) || d.WaterBody[nx, nz] >= 0) continue;
                    if (cut.Contains((x, z, nx, nz))) continue;
                    d.WaterBody[nx, nz] = id;
                    queue.Enqueue((nx, nz));
                }
            }
        }
        d.WaterBodies = bodies;
    }

    /// <summary>
    /// Every place a ferry station could stand: a walkable quay cell within
    /// <see cref="MaxQuayRise"/> slabs of the water beside it, with somewhere to
    /// unload behind it.
    ///
    /// That last clause is what a domino on its own does not say. A single slab of
    /// ground surrounded by water is a rock, not a landing — and a chain of them
    /// across a lagoon was exactly the thing that used to read as one connected
    /// place. A quay needs a neighbour you can walk to.
    /// </summary>
    private static void BuildBerths(IslandData d)
    {
        int n = d.Size;
        d.Berths.Clear();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            d.Ferry[x, z] = false;
            if (!Walkable(d, x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;

            short level = CrossLevel(d, x, z);

            // Somewhere to unload: one neighbour of dry, walkable ground at a free
            // step. Without it the "quay" is a rock in the water.
            bool yard = false;
            for (int k = 0; k < 4 && !yard; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                yard = Walkable(d, nx, nz) && d.WaterLevel[nx, nz] == IslandData.NoLand
                       && Math.Abs(CrossLevel(d, nx, nz) - level) <= 1;
            }
            if (!yard) continue;

            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!Sailable(d, nx, nz)) continue;

                short surface = d.WaterLevel[nx, nz];
                int rise = level - surface;
                if (rise < 0 || rise > MaxQuayRise) continue;

                d.Ferry[x, z] = true;
                d.Berths.Add(new FerryBerth(new Vector2I(x, z), new Vector2I(nx, nz),
                                            surface, d.WaterBody[nx, nz]));
                break;
            }
        }
    }

    /// <summary>
    /// Re-anchors <see cref="IslandData.Mainland"/> and
    /// <see cref="IslandData.Heartland"/> on the ground the player actually
    /// arrives on, rather than on whichever piece happens to be biggest.
    ///
    /// <b>The mainland is where you land.</b> A run starts at the Entry Gate's
    /// apron — the landing strip, or the Gate's own yard — and everything else is
    /// somewhere you have to get to from there. Ranking by area answered a
    /// different question ("what is the largest connected thing?") and could name
    /// a mainland on the far side of a strait from the only way in, which makes
    /// every number derived from it a number about somewhere else.
    ///
    /// Areas keep their area order, so <c>Areas[0]</c> is still the largest and
    /// the ids are stable; only which one is <i>the</i> mainland moves. Run after
    /// <c>GatePlacement</c>, since it is the Gate that decides.
    /// </summary>
    public static void AnchorOn(IslandData d, Vector2I cell)
    {
        if (cell.X < 0 || cell.Y < 0 || cell.X >= d.Size || cell.Y >= d.Size) return;
        if (!Walkable(d, cell.X, cell.Y)) return;

        int walk = d.Walk[cell.X, cell.Y];
        int reach = d.Reach[cell.X, cell.Y];
        if (walk >= 0) d.Mainland = walk;
        if (reach >= 0) d.Heartland = reach;
    }

    /// <summary>
    /// True where a column is ground you could stand on — or wade across.
    ///
    /// A stream is one slab of water in a bed cut two slabs below its banks, so
    /// you step down a slab into the water and up a slab out of it: it is
    /// <b>fordable</b>, and a watercourse running the length of an island does
    /// not cut it in two. A navigable river is two cells wide and deeper, meant
    /// for barges; a lake has a bed three or four slabs down. Neither is
    /// something you walk through.
    /// </summary>
    public static bool Walkable(IslandData d, int x, int z)
    {
        int n = d.Size;
        if (x < 0 || z < 0 || x >= n || z >= n) return false;
        if (!d.HasLand(x, z)) return false;
        if (d.WaterLevel[x, z] == IslandData.NoLand) return true;
        // A stream is crossed at a ford and nowhere else: fordable-everywhere made
        // a watercourse a line on the map rather than a feature of it, and let a
        // road walk down the bed. See Rivers.MarkFords.
        return d.Ford[x, z];
    }

    /// <summary>
    /// The level you actually cross a column at: the surface of a ford, or the
    /// ground anywhere else.
    ///
    /// A stream's bed is cut below its banks so that a river reads as a channel
    /// rather than as water poured over the ground — but nobody walks the bed,
    /// they wade the shallow water above it. Measuring the bed instead reports a
    /// two-slab step at every bank and turns every stream into a wall it is not.
    /// </summary>
    public static short CrossLevel(IslandData d, int x, int z)
    {
        if (d.River[x, z] && !d.Navigable[x, z]
            && d.WaterLevel[x, z] != IslandData.NoLand)
            return d.WaterLevel[x, z];
        return d.SurfaceLevel(x, z);
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
                short top = CrossLevel(d, x, z);
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
                    if (Math.Abs(CrossLevel(d, nx, nz) - top) > 1) continue;
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
    /// faces land across at most <see cref="IslandData.BridgeSpan"/> cells of
    /// water or aether, which is a bridge: a level deck, so the two banks have to
    /// agree to within <see cref="MaxBridgeRise"/>.
    ///
    /// This is the connectivity the design should actually be held to. Walking is
    /// the *free* case; a cliff is meant to be an obstacle that costs something,
    /// not a wall. What matters is whether the cost can be paid at all.
    /// </summary>
    private static void BuildReachAreas(IslandData d, bool ferries = true,
                                        int[,]? into = null)
    {
        int n = d.Size;
        int span = Math.Max(1, d.BridgeSpan);
        int[,] label = into ?? d.Reach;
        var areas = new List<WalkArea>();
        var queue = new Queue<(int X, int Z)>();

        // Every quay on one body of water is one ferry ride from every other.
        var berthsByBody = new Dictionary<int, List<Vector2I>>();
        var bodyAt = new Dictionary<Vector2I, int>();
        if (ferries)
            foreach (FerryBerth berth in d.Berths)
            {
                if (berth.Body < 0) continue;
                if (!berthsByBody.TryGetValue(berth.Body, out List<Vector2I>? list))
                    berthsByBody[berth.Body] = list = new List<Vector2I>();
                list.Add(berth.Land);
                bodyAt[berth.Land] = berth.Body;
            }
        var sailed = new HashSet<int>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            label[x, z] = d.HasLand(x, z) && !Walkable(d, x, z) ? Water : -1;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Walkable(d, sx, sz) || label[sx, sz] != -1) continue;

            int id = areas.Count;
            int area = 0;
            short low = short.MaxValue, high = short.MinValue;
            var min = new Vector2I(sx, sz);
            var max = new Vector2I(sx, sz);

            label[sx, sz] = id;
            queue.Enqueue((sx, sz));

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                short top = CrossLevel(d, x, z);
                area++;
                if (top < low) low = top;
                if (top > high) high = top;
                min = new Vector2I(Math.Min(min.X, x), Math.Min(min.Y, z));
                max = new Vector2I(Math.Max(max.X, x), Math.Max(max.Y, z));

                for (int k = 0; k < 4; k++)
                {
                    // Step 1 is the neighbour; anything beyond it is a bridge over
                    // that many cells of nothing. Anything solid in between is not
                    // a gap, so it is the neighbour case or nothing.
                    for (int reach = 1; reach <= span + 1; reach++)
                    {
                        int nx = x + Dx[k] * reach, nz = z + Dz[k] * reach;
                        if (!Walkable(d, nx, nz)) continue;

                        bool bridged = reach > 1;
                        if (bridged && !DeckFits(d, x, z, Dx[k], Dz[k], reach, span)) continue;

                        int rise = Math.Abs(CrossLevel(d, nx, nz) - top);
                        if (rise > (bridged ? MaxBridgeRise : InfrastructureStep)) continue;
                        if (label[nx, nz] != -1) continue;

                        label[nx, nz] = id;
                        queue.Enqueue((nx, nz));
                    }
                }

                // And the ferry: a quay reaches every other quay on its own water,
                // however far off it is. This is what makes a lagoon a place you
                // cross rather than a wall with stepping stones in it.
                if (!d.Ferry[x, z]) continue;
                if (!bodyAt.TryGetValue(new Vector2I(x, z), out int body)) continue;
                if (!sailed.Add(body)) continue;
                if (!berthsByBody.TryGetValue(body, out List<Vector2I>? far)) continue;

                foreach (Vector2I quay in far)
                {
                    if (label[quay.X, quay.Y] != -1) continue;
                    label[quay.X, quay.Y] = id;
                    queue.Enqueue((quay.X, quay.Y));
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
            if (label[x, z] >= 0) label[x, z] = remap[label[x, z]];

        if (into != null) return;                   // a scratch pass; nothing else to fill

        d.Reaches.Clear();
        for (int i = 0; i < order.Count; i++) d.Reaches.Add(order[i] with { Id = i });
        d.Heartland = d.Reaches.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Ground level enough to lay a settlement out on: flat, or sitting at one
    /// lone step. A cell that steps against two or more of its neighbours is a
    /// hillside — walkable, but not a yard.
    ///
    /// Water is never shelf: a ford is walkable and a lake bed is dry ground in
    /// the data, and neither is somewhere anyone would build.
    /// </summary>
    private static bool ShelfGround(IslandData d, int x, int z)
    {
        if (!Walkable(d, x, z)) return false;
        if (d.WaterLevel[x, z] != IslandData.NoLand) return false;

        short level = CrossLevel(d, x, z);
        int steps = 0;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + Dx[k], nz = z + Dz[k];
            if (!Walkable(d, nx, nz)) continue;          // a coast is not a step
            int delta = Math.Abs(CrossLevel(d, nx, nz) - level);
            if (delta == 0) continue;
            if (delta > 1) return false;                 // a cliff edge is not shelf
            steps++;
        }
        return steps <= ShelfSteps;
    }

    private static void BuildShelves(IslandData d)
    {
        int n = d.Size;
        var shelves = new List<Shelf>();
        var queue = new Queue<(int X, int Z)>();
        var cells = new List<Vector2I>();
        var claimed = new bool[n, n];
        var ground = new bool[n, n];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            d.ShelfId[x, z] = -1;
            ground[x, z] = ShelfGround(d, x, z);
        }

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!ground[sx, sz] || claimed[sx, sz]) continue;

            cells.Clear();
            claimed[sx, sz] = true;
            queue.Enqueue((sx, sz));
            short low = CrossLevel(d, sx, sz), high = low;

            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                cells.Add(new Vector2I(x, z));
                short level = CrossLevel(d, x, z);
                if (level < low) low = level;
                if (level > high) high = level;

                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!ground[nx, nz] || claimed[nx, nz]) continue;
                    // A shelf may descend, a slab at a time. What it may not do is
                    // step twice at once.
                    if (Math.Abs(CrossLevel(d, nx, nz) - level) > 1) continue;
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
            shelves.Add(new Shelf(id, low, high, cells.Count, width, min, max, center));
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
