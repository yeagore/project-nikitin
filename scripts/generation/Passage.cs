using System.Collections.Generic;

using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>
/// The least-infrastructure road from the Entry Gate to one Exit: walking is free,
/// every stair, bridge or ferry is one point, so <see cref="Cost"/> is the count of
/// <see cref="Built"/>. <c>GatePlacement</c> puts no Exit the Entry cannot reach, so
/// a Domain has one of these per Exit.
/// </summary>
/// <param name="Exit">Index into <see cref="IslandData.Gates"/> of the Exit this reaches.</param>
/// <param name="From">The Entry Gate's apron.</param>
/// <param name="To">The Exit Gate's apron.</param>
/// <param name="Cost">Works on the route; zero means you can simply walk it.</param>
/// <param name="Path">Every cell of the route, in order, start to end.</param>
/// <param name="Built">The works, in order along the route.</param>
/// <param name="Flights">Runs of five stairs inside fifteen cells: a road climbing country it should go round.</param>
public sealed record Passage(int Exit, Vector2I From, Vector2I To, int Cost,
                             List<Vector2I> Path, List<Works> Built, int Flights);

/// <summary>
/// One Dijkstra from the Entry apron over exactly <see cref="Traversal"/>'s reach rule
/// — same constants, same water bodies, so a road never crosses what reach cannot —
/// priced works first, then cells, so among equally cheap roads the shortest wins.
/// </summary>
internal static class Passages
{
    /// <summary>One work against one cell of walking in the packed cost; larger than any road is long, so length only breaks ties.</summary>
    private const long WorksWeight = 1 << 20;

    /// <summary>One cell of wading in cells of walking: enough that a road crosses a stream rather than following its bed.</summary>
    private const long WadeLength = 8;

    /// <summary>Stairs that make a flight...</summary>
    private const int FlightStairs = 5;

    /// <summary>...inside this many cells of road.</summary>
    private const int FlightWindow = 15;

    /// <summary>The Dijkstra answer: packed cost per cell, the cell it was reached from, and the work (if any) that reached it.</summary>
    private sealed class Route
    {
        public readonly long[,] Cost;
        public readonly Vector2I[,] From;
        public readonly WorksKind[,] How;
        public readonly bool[,] Built;

        public Route(int n)
        {
            Cost = new long[n, n];
            From = new Vector2I[n, n];
            How = new WorksKind[n, n];
            Built = new bool[n, n];
        }
    }

    /// <summary>Fills <see cref="IslandData.Passages"/>, one per Exit Gate, and marks the Domain Rough when a road has a flight.</summary>
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
        bool[,] reserved = ReservedGround(d);
        var berths = new Traversal.BerthIndex(d);
        Route route = BuildRoute(d, start, span, reserved, berths);

        for (int i = 0; i < d.Gates.Count; i++)
        {
            if (d.Gates[i].Role != GateRole.Exit) continue;
            Vector2I goal = d.Gates[i].Apron;
            if (!InBounds(n, goal.X, goal.Y)) continue;
            if (route.Cost[goal.X, goal.Y] == long.MaxValue) continue;

            Passage passage = TracePassage(route, i, start, goal);
            d.Passages.Add(passage);
            if (passage.Flights > 0) d.Rough = true;
        }
    }

    /// <summary>Ground something is already built on — quays, landing strips, bridge banks, the column under each Gate — which a stair may not take for its footing.</summary>
    private static bool[,] ReservedGround(IslandData d)
    {
        int n = d.Size;
        var reserved = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            reserved[x, z] = d.Ferry[x, z] || d.Landings[x, z];
        foreach (Crossing bank in d.Bridges)
        foreach (Vector2I cell in new[] { bank.A, bank.B })
            if (InBounds(n, cell.X, cell.Y))
                reserved[cell.X, cell.Y] = true;
        foreach (Gate g in d.Gates)
        {
            var cell = new Vector2I(g.Center.X, g.Center.Z);
            if (InBounds(n, cell.X, cell.Y))
                reserved[cell.X, cell.Y] = true;
        }
        return reserved;
    }

    /// <summary>
    /// The Dijkstra sweep from <paramref name="start"/>: a free step 0, a bridge 1, a stair 1
    /// unless either end is reserved, and every quay on a body once the first quay is reached.
    /// </summary>
    private static Route BuildRoute(IslandData d, Vector2I start, int span,
                                    bool[,] reserved, Traversal.BerthIndex berths)
    {
        int n = d.Size;
        var route = new Route(n);
        long[,] cost = route.Cost;
        Vector2I[,] from = route.From;
        WorksKind[,] how = route.How;
        bool[,] built = route.Built;
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
                // Wading builds nothing and costs WadeLength cells, so a road crosses a stream, not runs down it.
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
                    // A stair stands on two cells, and neither may already carry a structure.
                    else if (!reserved[c.X, c.Y] && !reserved[nx, nz])
                        Offer(to, 1, WorksKind.Stair);
                }
            }

            if (!d.Ferry[c.X, c.Y]) continue;
            List<Vector2I>? quays = berths.Open(c);
            if (quays == null) continue;
            foreach (Vector2I quay in quays)
                if (quay != c) Offer(quay, 1, WorksKind.Ferry);
        }
        return route;
    }

    /// <summary>Walks the route back from <paramref name="goal"/> to the start and packages it, path and works in road order.</summary>
    private static Passage TracePassage(Route route, int exit, Vector2I start, Vector2I goal)
    {
        var path = new List<Vector2I>();
        var works = new List<Works>();
        Vector2I at = goal;
        while (at.X >= 0)
        {
            path.Add(at);
            Vector2I back = route.From[at.X, at.Y];
            if (back.X >= 0 && route.Built[at.X, at.Y])
                works.Add(new Works(route.How[at.X, at.Y], back, at));
            at = back;
        }
        path.Reverse();
        works.Reverse();
        return new Passage(exit, start, goal, works.Count, path, works, Flights(path, works));
    }

    /// <summary>Non-overlapping runs of <see cref="FlightStairs"/> stairs inside <see cref="FlightWindow"/> cells of road — a ladder pretending to be a road.</summary>
    private static int Flights(List<Vector2I> path, List<Works> works)
    {
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
