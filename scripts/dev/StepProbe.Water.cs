using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

/// <summary>
/// TEMPORARY, with the rest of the probe. Two questions about water: whether a bed
/// ever hangs clear of the columns round it (<c>mode=detached</c>), and how often two
/// wet cells side by side stand at different levels with no fall recorded between
/// them (<c>mode=watersteps</c>), and what cutting the sailable bodies there would do.
/// <c>seed=N</c> pins one island and lists its sites.
/// </summary>
public partial class StepProbe
{
    private int _pinned = int.MinValue;

    /// <summary>The islands a water mode walks: the pinned seed at the preset's own size, or the sweep at all three.</summary>
    private IEnumerable<(int Seed, IslandData Data)> WaterIslands()
    {
        if (_pinned != int.MinValue)
        {
            yield return (_pinned, IslandGenerator.Generate(_pinned, (IslandParams)Params.Duplicate(true)));
            yield break;
        }
        foreach (int size in IslandParams.SupportedSizes)
        {
            IslandParams p = Copy(size);
            for (int i = 0; i < Seeds; i++) yield return (SeedAt(i), IslandGenerator.Generate(SeedAt(i), p));
        }
    }

    private static bool Wet(IslandData d, int x, int z)
        => InBounds(d.Size, x, z) && d.HasLand(x, z) && d.WaterLevel[x, z] != IslandData.NoLand
           && d.WaterLevel[x, z] > d.SurfaceLevel(x, z);

    /// <summary>L a lake, N a navigable reach, S a stream, G goo.</summary>
    private static char WaterKind(IslandData d, int x, int z)
        => d.Fluid[x, z] == (byte)FluidKind.Goo ? 'G' : !d.River[x, z] ? 'L' : d.Navigable[x, z] ? 'N' : 'S';

    // ---- detached beds -----------------------------------------------------

    /// <summary>
    /// A column hangs clear of a neighbour when their ground spans share no slab: one's
    /// top is under the other's keel. Water leaks where a wet column's water stands
    /// beside the air under a neighbour's keel. Both should be nought.
    /// </summary>
    private void Detached()
    {
        int islands = 0, badIslands = 0, leakIslands = 0;
        long pairs = 0, leakCells = 0;
        int worstGap = 0;
        var examples = new List<string>();

        foreach ((int seed, IslandData d) in WaterIslands())
        {
            islands++;
            int n = d.Size;
            int here = 0, leaks = 0, gapHere = 0;
            var sites = new List<string>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.HasLand(x, z)) continue;
                Span s = d.Spans[x, z][0];
                bool leaked = false;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                    Span q = d.Spans[nx, nz][0];
                    if (q.Bottom > s.Top)
                    {
                        here++;
                        int gap = q.Bottom - s.Top;
                        gapHere = Math.Max(gapHere, gap);
                        if (sites.Count < 12)
                            sites.Add($"{x},{z} [{s.Bottom}..{s.Top}]{(Wet(d, x, z) ? $" {WaterKind(d, x, z)} water to {d.WaterLevel[x, z]}" : " dry")}"
                                + $" under {nx},{nz} [{q.Bottom}..{q.Top}] by {gap}");
                    }
                    if (Wet(d, x, z) && q.Bottom > s.Top + 1) leaked = true;
                }
                if (leaked) leaks++;
            }
            pairs += here;
            leakCells += leaks;
            worstGap = Math.Max(worstGap, gapHere);
            if (here > 0) badIslands++;
            if (leaks > 0) leakIslands++;
            if (here > 0 && examples.Count < 12)
                examples.Add($"seed {seed} {n}²: {here} pairs, worst {gapHere} slabs, {leaks} wet cells open underneath");
            if (_pinned != int.MinValue)
            {
                GD.Print($"seed {seed} {n}² {d.Arrangement} {d.Character}: {here} hanging pairs, worst gap {gapHere} slabs, "
                    + $"{leaks} wet cells with water against the air under a neighbour's keel; deeps {d.Deeps.Count}, great lakes {d.GreatLakes.Count}");
                foreach (string s in sites) GD.Print("   " + s);
            }
        }

        GD.Print($"detached: {islands} islands, {badIslands} with a column hanging clear of a neighbour "
            + $"({pairs} pairs, worst gap {worstGap} slabs), {leakIslands} with water open underneath ({leakCells} cells)");
        foreach (string e in examples) GD.Print("   " + e);
    }

    // ---- water steps -------------------------------------------------------

    /// <summary>
    /// Every pair of wet cells side by side at different levels, by how far apart, what
    /// the two are, and whether a fall is recorded between them. Then the sailable
    /// bodies as they are and as they would be if every unrecorded step cut one too.
    /// </summary>
    private void WaterSteps()
    {
        int islands = 0, steppedIslands = 0;
        // [marked 0/1][diff 1, 2, 3, 4, 5+]
        var byDiff = new long[2, 5];
        var byKind = new Dictionary<string, long[]>();          // key -> unmarked by diff 1, 2, 3+
        long wetPairs = 0, along = 0, across = 0;
        long insideBody = 0, betweenBodies = 0, unsailable = 0;
        long lakeOverReach = 0, reachOverLake = 0, reachOverReach = 0, lakeOverLake = 0;
        int specks1 = 0, specks2 = 0, pieces1All = 0, pieces2All = 0;
        var examples = new Dictionary<string, List<string>>();

        // What a cut at every unrecorded step would do, at one slab and at two.
        var before = new List<int>();
        var after1 = new List<int>();
        var after2 = new List<int>();
        int bodiesCut1 = 0, bodiesCut2 = 0, bodiesAll = 0;
        var worst = new List<(int Pieces, string Text)>();

        foreach ((int seed, IslandData d) in WaterIslands())
        {
            islands++;
            int n = d.Size;
            var fall = new HashSet<(int, int, int, int)>();
            foreach (Fall f in d.Falls)
                if (!f.OffRim) fall.Add((f.Cell.X, f.Cell.Y, f.Cell.X + f.Flow.X, f.Cell.Y + f.Flow.Y));

            bool any = false;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!Wet(d, x, z)) continue;
                for (int k = 0; k < 2; k++)
                {
                    int nx = x + (k == 0 ? 1 : 0), nz = z + (k == 0 ? 0 : 1);
                    if (!Wet(d, nx, nz)) continue;
                    wetPairs++;
                    int a = d.WaterLevel[x, z], b = d.WaterLevel[nx, nz];
                    if (a == b) continue;

                    bool aHigh = a > b;
                    int hx = aHigh ? x : nx, hz = aHigh ? z : nz, lx = aHigh ? nx : x, lz = aHigh ? nz : z;
                    int diff = Math.Abs(a - b);
                    bool marked = fall.Contains((hx, hz, lx, lz));
                    byDiff[marked ? 1 : 0, Math.Min(diff, 5) - 1]++;
                    if (marked) continue;

                    any = true;
                    char ka = WaterKind(d, hx, hz), kb = WaterKind(d, lx, lz);
                    string key = ka <= kb ? $"{ka}-{kb}" : $"{kb}-{ka}";
                    if (!byKind.TryGetValue(key, out long[]? row)) byKind[key] = row = new long[3];
                    row[Math.Min(diff, 3) - 1]++;

                    // Along the water's own course, or two courses side by side: the high
                    // cell's flow count carried into the low one reads as along.
                    if (d.River[hx, hz] && d.River[lx, lz] && d.Flow[lx, lz] > d.Flow[hx, hz]) along++;
                    else across++;

                    int ba = d.WaterBody[hx, hz], bb = d.WaterBody[lx, lz];
                    if (ba >= 0 && bb >= 0 && ba == bb)
                    {
                        if (ka == 'L' && kb == 'N') lakeOverReach++;
                        else if (ka == 'N' && kb == 'L') reachOverLake++;
                        else if (ka == 'N') reachOverReach++;
                        else lakeOverLake++;
                    }
                    if (ba >= 0 && bb >= 0 && ba == bb) insideBody++;
                    else if (ba >= 0 && bb >= 0) betweenBodies++;
                    else unsailable++;

                    string tag = key + (ba >= 0 && ba == bb ? " in one body" : "");
                    if (!examples.TryGetValue(tag, out List<string>? list)) examples[tag] = list = new List<string>();
                    if (list.Count < 6)
                        list.Add($"seed {seed} {n}² at={hx},{hz}: {ka} {Math.Max(a, b)} over {kb} {Math.Min(a, b)} ({diff} slab{(diff == 1 ? "" : "s")})");
                }
            }
            if (any) steppedIslands++;

            // The bodies as they are, and cut at every step of one slab or more, and of two.
            int[] sizes = BodySizes(d, d.WaterBody, d.WaterBodies);
            int[,] cut1 = CutBodies(d, 1, out int count1);
            int[,] cut2 = CutBodies(d, 2, out int count2);
            int[] sizes1 = BodySizes(d, cut1, count1);
            int[] sizes2 = BodySizes(d, cut2, count2);
            foreach (int s in sizes) if (s >= 2) before.Add(s);
            foreach (int s in sizes1) if (s >= 2) after1.Add(s);
            foreach (int s in sizes2) if (s >= 2) after2.Add(s);

            var pieces1 = new HashSet<int>[d.WaterBodies];
            var pieces2 = new HashSet<int>[d.WaterBodies];
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                int id = d.WaterBody[x, z];
                if (id < 0) continue;
                (pieces1[id] ??= new HashSet<int>()).Add(cut1[x, z]);
                (pieces2[id] ??= new HashSet<int>()).Add(cut2[x, z]);
            }
            for (int id = 0; id < d.WaterBodies; id++)
            {
                if (sizes[id] < 2) continue;
                bodiesAll++;
                int p1 = pieces1[id]?.Count ?? 1, p2 = pieces2[id]?.Count ?? 1;
                if (p1 > 1)
                {
                    bodiesCut1++;
                    foreach (int piece in pieces1[id]!) { pieces1All++; if (sizes1[piece] < 4) specks1++; }
                }
                if (p2 > 1)
                {
                    bodiesCut2++;
                    foreach (int piece in pieces2[id]!) { pieces2All++; if (sizes2[piece] < 4) specks2++; }
                }
                if (p1 > 1)
                {
                    var parts = new List<int>();
                    foreach (int piece in pieces1[id]!) parts.Add(sizes1[piece]);
                    parts.Sort((u, v) => v.CompareTo(u));
                    string name = id < d.WaterNames.Count ? d.WaterNames[id] : $"body {id}";
                    worst.Add((p1, $"seed {seed} {n}² {name}: {sizes[id]} cells -> {p1} pieces ({string.Join(", ", parts.GetRange(0, Math.Min(8, parts.Count)))}{(parts.Count > 8 ? ", ..." : "")})"));
                }
            }
        }

        long unmarked = 0, markedAll = 0;
        for (int i = 0; i < 5; i++) { unmarked += byDiff[0, i]; markedAll += byDiff[1, i]; }
        GD.Print($"watersteps: {islands} islands, {wetPairs} wet pairs side by side; {steppedIslands} islands "
            + $"({100.0 * steppedIslands / Math.Max(1, islands):0}%) have a step with no fall on it");
        GD.Print($"   with a fall:    1: {byDiff[1, 0]}  2: {byDiff[1, 1]}  3: {byDiff[1, 2]}  4: {byDiff[1, 3]}  5+: {byDiff[1, 4]}   (n={markedAll})");
        GD.Print($"   with no fall:   1: {byDiff[0, 0]}  2: {byDiff[0, 1]}  3: {byDiff[0, 2]}  4: {byDiff[0, 3]}  5+: {byDiff[0, 4]}   (n={unmarked}, "
            + $"{(double)unmarked / Math.Max(1, islands):0.0} an island, {100.0 * unmarked / Math.Max(1, wetPairs):0.00}% of wet pairs)");
        GD.Print($"   the unrecorded ones run along one course {along}, between two waters side by side {across}");
        GD.Print($"   sailable both and in ONE body {insideBody}; sailable both, two bodies {betweenBodies}; a stream or goo in the pair {unsailable}");
        GD.Print($"   inside one body: a lake over a reach {lakeOverReach}, a reach over a lake {reachOverLake}, "
            + $"a reach over a reach {reachOverReach}, a lake over a lake {lakeOverLake}");
        GD.Print("   by what the two cells are (L lake, N navigable, S stream), unrecorded steps of 1 / 2 / 3+ slabs:");
        var keys = new List<string>(byKind.Keys);
        keys.Sort(string.CompareOrdinal);
        foreach (string key in keys)
            GD.Print($"      {key}: {byKind[key][0]} / {byKind[key][1]} / {byKind[key][2]}");

        GD.Print($"   bodies of 2+ cells: {before.Count} as they are (median {Med(before)}, mean {Mean(before):0.0} cells); "
            + $"cut at every step: {after1.Count} (median {Med(after1)}, mean {Mean(after1):0.0}); "
            + $"cut at steps of 2+: {after2.Count} (median {Med(after2)}, mean {Mean(after2):0.0})");
        GD.Print($"   of {bodiesAll} bodies, {bodiesCut1} ({100.0 * bodiesCut1 / Math.Max(1, bodiesAll):0.0}%) break at a one-slab cut, "
            + $"{bodiesCut2} ({100.0 * bodiesCut2 / Math.Max(1, bodiesAll):0.0}%) at a two-slab cut");

        GD.Print($"   the broken bodies' pieces: {pieces1All} at a one-slab cut, {specks1} of them under 4 cells; "
            + $"{pieces2All} at a two-slab cut, {specks2} under 4 cells");
        worst.Sort((u, v) => v.Pieces.CompareTo(u.Pieces));
        GD.Print("   the bodies a one-slab cut breaks worst:");
        for (int i = 0; i < Math.Min(10, worst.Count); i++) GD.Print("      " + worst[i].Text);

        GD.Print("   examples (the lab frames one with: seed=N at=X,Z zoom=6):");
        var tags = new List<string>(examples.Keys);
        tags.Sort(string.CompareOrdinal);
        foreach (string tag in tags)
        {
            GD.Print($"      {tag}");
            foreach (string e in examples[tag]) GD.Print("         " + e);
        }
    }

    private static double Mean(List<int> values)
    {
        if (values.Count == 0) return 0;
        long sum = 0;
        foreach (int v in values) sum += v;
        return (double)sum / values.Count;
    }

    private static int[] BodySizes(IslandData d, int[,] body, int count)
    {
        var sizes = new int[count];
        for (int x = 0; x < d.Size; x++)
        for (int z = 0; z < d.Size; z++)
            if (body[x, z] >= 0) sizes[body[x, z]]++;
        return sizes;
    }

    /// <summary>The sailable bodies again, cut at the falls as they are and also wherever two sailable cells differ by <paramref name="atLeast"/> slabs or more.</summary>
    private static int[,] CutBodies(IslandData d, int atLeast, out int count)
    {
        int n = d.Size;
        var body = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) body[x, z] = -1;

        var queue = new Queue<(int X, int Z)>();
        count = 0;
        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Traversal.Sailable(d, sx, sz) || body[sx, sz] >= 0) continue;
            int id = count++;
            body[sx, sz] = id;
            queue.Enqueue((sx, sz));
            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!Traversal.Sailable(d, nx, nz) || body[nx, nz] >= 0) continue;
                    if (d.WaterBody[nx, nz] != d.WaterBody[x, z]) continue;                    // a fall cut it already
                    if (Math.Abs(d.WaterLevel[nx, nz] - d.WaterLevel[x, z]) >= atLeast) continue;
                    body[nx, nz] = id;
                    queue.Enqueue((nx, nz));
                }
            }
        }
        return body;
    }
}
