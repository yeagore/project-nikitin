using System.Collections.Generic;

using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// A piece of built infrastructure a route depends on. Each one costs the player
/// a project — which is why the routing counts them rather than counting cells.
/// </summary>
public enum WorksKind
{
    /// <summary>A stair or a hoist up a cliff face, within <see cref="Traversal.InfrastructureStep"/>.</summary>
    Stair = 0,

    /// <summary>A deck across aether or water — see <see cref="Crossing"/>.</summary>
    Bridge = 1,

    /// <summary>A ferry between two quays on one body of water — see <see cref="FerryBerth"/>.</summary>
    Ferry = 2,
}

/// <summary>One crossing on a route that has to be built before it can be used.</summary>
/// <param name="Kind">Stair, bridge or ferry.</param>
/// <param name="From">The cell you leave.</param>
/// <param name="To">The cell you arrive at.</param>
public readonly record struct Works(WorksKind Kind, Vector2I From, Vector2I To);

/// <summary>
/// The cheapest way from the Entry Gate to one Exit Gate, where <b>cheap</b>
/// means "needs the least building".
///
/// <para>Walking is free: a one-slab step costs nothing, however many of them
/// there are. Everything else — a stair up a cliff, a bridge over a strait, a
/// ferry across a lake — costs exactly one point, because each is one thing the
/// company has to build before anyone can use the road. So <see cref="Cost"/> is
/// the number of works on the route, and a Cost of 0 means you can walk from the
/// Gate you arrived by to the Gate you are leaving by on the day you land.</para>
///
/// <para>A Domain always has one of these per Exit. If it did not, the Domain
/// would be a dead end wearing a Gate — see <c>GatePlacement</c>, which will not
/// put an Exit anywhere the Entry cannot reach.</para>
/// </summary>
/// <param name="Exit">Index into <see cref="IslandData.Gates"/> of the Exit this reaches.</param>
/// <param name="From">Where it starts: the Entry Gate's apron.</param>
/// <param name="To">Where it ends: the Exit Gate's apron.</param>
/// <param name="Cost">Works on the route. Zero means you can simply walk it.</param>
/// <param name="Path">Every cell of the route, in order, start to end.</param>
/// <param name="Built">The works themselves, in order along the route.</param>
/// <param name="Flights">
/// Runs of five or more elevators inside fifteen cells: the road climbing like a
/// staircase, which is a sign it is crossing country it should be going round.
/// </param>
public sealed record Passage(int Exit, Vector2I From, Vector2I To, int Cost,
                             List<Vector2I> Path, List<Works> Built, int Flights);

/// <summary>
/// Finds the least-infrastructure route from the Entry Gate to every Exit Gate.
///
/// <para>The move set is exactly <see cref="Traversal"/>'s reach rule, priced:
/// a free step costs 0, and a stair, a bridge or a ferry costs 1. All the routes
/// come out of one sweep from the Entry, since they share a source.</para>
///
/// <para>The cost is <b>works first, then cells</b>, packed into one number. Works
/// alone is the question being asked — and it leaves thousands of equally cheap
/// answers, so the search returns whichever it happened to reach first, which on
/// a 128² Domain was a road of 1500 cells wandering the whole island to save
/// nothing. Ranking equal-works roads by length costs nothing and gives the road
/// a player would actually walk.</para>
///
/// <para>The two rules have to stay the same rule: if this could cross something
/// the reach analysis could not, a Gate would be "reachable" by a road nobody
/// can build. The edges are therefore enumerated here against the same constants
/// (<see cref="Traversal.InfrastructureStep"/>,
/// <see cref="Traversal.MaxBridgeRise"/>, <see cref="Traversal.WaterBridgeSpan"/>)
/// and the same water bodies.</para>
/// </summary>
internal static class Passages
{
    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };

    /// <summary>
    /// What one work is worth against one cell of walking, in the packed cost.
    /// Larger than any road could ever be long, so a road with fewer works always
    /// wins however far round it goes, and length only ever breaks a tie.
    /// </summary>
    private const long WorksWeight = 1 << 20;

    /// <summary>
    /// What one cell of wading is worth in cells of walking. Eight: enough that a
    /// road crosses a stream at right angles rather than following it, and not so
    /// much that it walks a mile to avoid a ford.
    /// </summary>
    private const long WadeLength = 8;

    /// <summary>
    /// How many elevators in how short a stretch of road make a <b>flight</b> —
    /// the sign that the road is climbing through country it has no business in.
    /// Five within fifteen cells: fewer than that is a road picking its way up a
    /// terrace, and more is a ladder pretending to be a road.
    /// </summary>
    public const int FlightStairs = 5;

    /// <summary>And within how many cells of road they have to fall.</summary>
    public const int FlightWindow = 15;

    /// <summary>Fills <see cref="IslandData.Passages"/>, one per Exit Gate.</summary>
    public static void Find(IslandData d)
    {
        d.Passages.Clear();

        int entry = -1;
        for (int i = 0; i < d.Gates.Count; i++)
            if (d.Gates[i].Role == GateRole.Entry) { entry = i; break; }
        if (entry < 0) return;

        Vector2I start = d.Gates[entry].Apron;
        if (!Traversal.Walkable(d, start.X, start.Y)) return;

        int n = d.Size;
        int span = Mathf.Max(1, d.BridgeSpan);

        // Ground already spoken for by something built on it: a ferry quay, the
        // bank of a crossing, a Gate's landing strip and the cells under the
        // portal itself. An elevator may not take one of these for its footing.
        var reserved = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            reserved[x, z] = d.Ferry[x, z] || d.Landings[x, z];
        foreach (Crossing bank in d.Bridges)
        foreach (Vector2I cell in new[] { bank.A, bank.B })
            if (cell.X >= 0 && cell.Y >= 0 && cell.X < n && cell.Y < n)
                reserved[cell.X, cell.Y] = true;
        // The cells under the portal itself. A Gate is one cell wide now, so this
        // is one cell — and for a hanging Gate it is out in the aether anyway,
        // where the ground that matters is its landing strip and that is already
        // reserved through `Landings` above.
        foreach (Gate g in d.Gates)
        {
            var cell = new Vector2I(g.Center.X, g.Center.Z);
            if (cell.X >= 0 && cell.Y >= 0 && cell.X < n && cell.Y < n)
                reserved[cell.X, cell.Y] = true;
        }

        var berthsByBody = new Dictionary<int, List<Vector2I>>();
        var bodyAt = new Dictionary<Vector2I, int>();
        foreach (FerryBerth berth in d.Berths)
        {
            if (berth.Body < 0) continue;
            if (!berthsByBody.TryGetValue(berth.Body, out List<Vector2I>? list))
                berthsByBody[berth.Body] = list = new List<Vector2I>();
            list.Add(berth.Land);
            bodyAt[berth.Land] = berth.Body;
        }
        var sailed = new HashSet<int>();

        var cost = new long[n, n];
        var from = new Vector2I[n, n];
        var how = new WorksKind[n, n];
        var built = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            cost[x, z] = long.MaxValue;
            from[x, z] = new Vector2I(-1, -1);
        }

        var queue = new PriorityQueue<Vector2I, long>();
        cost[start.X, start.Y] = 0;
        queue.Enqueue(start, 0);

        while (queue.TryDequeue(out Vector2I c, out long key))
        {
            long here = cost[c.X, c.Y];
            if (key > here) continue;                        // a stale entry
            short top = Traversal.CrossLevel(d, c.X, c.Y);

            void Offer(Vector2I to, int price, WorksKind kind)
            {
                // Wading is free to *build* and unpleasant to do, so a ford costs
                // nothing in works and several cells in length. Without that, a
                // stream bed is exactly as good as the bank beside it and a road
                // will happily run down the middle of one for twenty cells, which
                // is the one thing a road never does.
                long step = d.River[to.X, to.Y] ? 1 + WadeLength : 1;
                long next = here + (long)price * WorksWeight + step;
                if (next >= cost[to.X, to.Y]) return;
                cost[to.X, to.Y] = next;
                from[to.X, to.Y] = c;
                how[to.X, to.Y] = kind;
                built[to.X, to.Y] = price > 0;
                queue.Enqueue(to, next);
            }

            for (int k = 0; k < 4; k++)
            {
                for (int reach = 1; reach <= span + 1; reach++)
                {
                    int nx = c.X + Dx[k] * reach, nz = c.Y + Dz[k] * reach;
                    if (!Traversal.Walkable(d, nx, nz)) continue;

                    bool bridged = reach > 1;
                    if (bridged && !Traversal.DeckFits(d, c.X, c.Y, Dx[k], Dz[k], reach, span))
                        continue;

                    int rise = Mathf.Abs(Traversal.CrossLevel(d, nx, nz) - top);
                    if (rise > (bridged ? Traversal.MaxBridgeRise
                                        : Traversal.InfrastructureStep)) continue;

                    var to = new Vector2I(nx, nz);
                    if (bridged) Offer(to, 1, WorksKind.Bridge);
                    else if (rise <= 1) Offer(to, 0, WorksKind.Stair);
                    // A cliffside elevator stands on two cells — the one below and
                    // the one above — and neither may be ground something else is
                    // already built on. A hoist through a quay, a bridgehead or the
                    // ground under a Gate is two structures in one place.
                    else if (!reserved[c.X, c.Y] && !reserved[nx, nz])
                        Offer(to, 1, WorksKind.Stair);
                }
            }

            // A ferry is worth one crossing however far it goes, so a body is only
            // ever opened once — from whichever quay the search reached first,
            // which is by construction the cheapest one.
            if (!d.Ferry[c.X, c.Y]) continue;
            if (!bodyAt.TryGetValue(c, out int body)) continue;
            if (!sailed.Add(body)) continue;
            if (!berthsByBody.TryGetValue(body, out List<Vector2I>? quays)) continue;
            foreach (Vector2I quay in quays)
                if (quay != c) Offer(quay, 1, WorksKind.Ferry);
        }

        for (int i = 0; i < d.Gates.Count; i++)
        {
            if (d.Gates[i].Role != GateRole.Exit) continue;
            Vector2I goal = d.Gates[i].Apron;
            if (goal.X < 0 || goal.Y < 0 || goal.X >= n || goal.Y >= n) continue;
            if (cost[goal.X, goal.Y] == long.MaxValue) continue;

            var path = new List<Vector2I>();
            var works = new List<Works>();
            Vector2I at = goal;
            while (at.X >= 0)
            {
                path.Add(at);
                Vector2I back = from[at.X, at.Y];
                if (back.X >= 0 && built[at.X, at.Y])
                    works.Add(new Works(how[at.X, at.Y], back, at));
                at = back;
            }
            path.Reverse();
            works.Reverse();
            int flights = Flights(path, works);
            d.Passages.Add(new Passage(i, start, goal, works.Count, path, works, flights));
            if (flights > 0) d.Rough = true;
        }
    }

    /// <summary>
    /// How many <b>flights</b> the road climbs: runs of <see cref="FlightStairs"/>
    /// elevators or more inside <see cref="FlightWindow"/> cells of each other.
    ///
    /// One elevator is a cliff. Five in a row is the road telling you it is going
    /// the wrong way — through broken country that a longer route round would
    /// avoid, or through country the Domain should not have put between two Gates
    /// at all. It is worth knowing about at the Domain level (see
    /// <see cref="IslandData.Rough"/>), because it is the kind of thing that makes
    /// a run feel like work before anything is even built.
    /// </summary>
    private static int Flights(List<Vector2I> path, List<Works> works)
    {
        // Where along the road each cell falls, so a work can be placed on it.
        var at = new Dictionary<Vector2I, int>();
        for (int i = 0; i < path.Count; i++) at[path[i]] = i;

        var steps = new List<int>();
        foreach (Works w in works)
            if (w.Kind == WorksKind.Stair && at.TryGetValue(w.To, out int where))
                steps.Add(where);
        steps.Sort();

        int flights = 0;
        for (int i = 0; i + FlightStairs - 1 < steps.Count; )
        {
            int last = i + FlightStairs - 1;
            if (steps[last] - steps[i] <= FlightWindow)
            {
                flights++;
                i = last + 1;                       // one flight is counted once
            }
            else i++;
        }
        return flights;
    }
}
