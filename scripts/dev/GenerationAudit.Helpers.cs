using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

public partial class GenerationAudit
{
    private static void Report(string label, List<int> values, string unit)
    {
        if (values.Count == 0) { GD.Print($"{label}: none"); return; }
        values.Sort();
        GD.Print($"{label}: min {values[0]}, median {values[values.Count / 2]}, "
            + $"max {values[^1]} {unit}  (n={values.Count})");
    }

    private static int[,] InwardDistance(IslandData d, int n)
    {
        var dist = new int[n, n];
        var q = new Queue<(int X, int Z)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            dist[x, z] = -1;
            if (!d.HasLand(x, z)) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                bool outside = !InBounds(n, nx, nz)
                               || !d.HasLand(nx, nz) || d.Region[nx, nz] != d.Region[x, z];
                if (!outside) continue;
                dist[x, z] = 0;
                q.Enqueue((x, z));
                break;
            }
        }
        while (q.Count > 0)
        {
            var (x, z) = q.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                if (d.Region[nx, nz] != d.Region[x, z] || dist[nx, nz] >= 0) continue;
                dist[nx, nz] = dist[x, z] + 1;
                q.Enqueue((nx, nz));
            }
        }
        return dist;
    }

    private static int[] MaxInwardPerRegion(IslandData d, int[,] inward, int n)
    {
        int highest = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.HasLand(x, z)) highest = Math.Max(highest, d.Region[x, z]);

        var max = new int[highest + 1];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.HasLand(x, z)) max[d.Region[x, z]] = Math.Max(max[d.Region[x, z]], inward[x, z]);
        return max;
    }

    /// <summary>Labels each 4-connected landmass; returns how many there are.</summary>
    private static int LabelLandmasses(IslandData d, int n, int[,] into)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) into[x, z] = -1;

        var stack = new Stack<(int X, int Z)>();
        int found = 0;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!d.HasLand(sx, sz) || into[sx, sz] >= 0) continue;
            int id = found++;
            into[sx, sz] = id;
            stack.Push((sx, sz));
            while (stack.Count > 0)
            {
                var (x, z) = stack.Pop();
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!d.HasLand(nx, nz) || into[nx, nz] >= 0) continue;
                    into[nx, nz] = id;
                    stack.Push((nx, nz));
                }
            }
        }
        return found;
    }

    private static int CountComponents(IslandData d, int n)
    {
        var seen = new bool[n, n];
        var stack = new Stack<(int X, int Z)>();
        int found = 0;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z) || seen[x, z]) continue;
            found++;
            seen[x, z] = true;
            stack.Push((x, z));
            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Pop();
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!d.HasLand(nx, nz) || seen[nx, nz]) continue;
                    seen[nx, nz] = true;
                    stack.Push((nx, nz));
                }
            }
        }
        return found;
    }

    private static string Pct(int part, int whole)
        => whole == 0 ? "-" : $"{100 * part / whole}%";

    /// <summary>
    /// The landmass count an arrangement's name promises — the audit's copy of
    /// the generator's own bar, for measuring shortfall from outside.
    /// </summary>
    private static int MassesTheShapeNames(IslandArrangement how) => how switch
    {
        IslandArrangement.Twins => 2,
        IslandArrangement.Triplets => 3,
        IslandArrangement.Satellites => 3,
        IslandArrangement.Archipelago => 4,
        IslandArrangement.BrokenRing => 4,
        IslandArrangement.BrokenArc => 3,
        IslandArrangement.Atoll => 5,
        IslandArrangement.ThousandIsles => 8,
        IslandArrangement.Shards => 4,
        IslandArrangement.BrokenCross => 4,
        IslandArrangement.BrokenT => 3,
        IslandArrangement.BrokenL => 2,
        IslandArrangement.BrokenFractal => 4,
        IslandArrangement.Quarters => 4,
        IslandArrangement.Halves => 2,
        IslandArrangement.Harmony => 2,
        IslandArrangement.Reef => 3,
        _ => 1,
    };

    /// <summary>The arrangements still on probation — see <see cref="Debut"/>.</summary>
    private static readonly IslandArrangement[] Debutants =
    {
        IslandArrangement.Square, IslandArrangement.Rhomb, IslandArrangement.NShape,
        IslandArrangement.Quarters, IslandArrangement.Halves, IslandArrangement.Harmony,
        IslandArrangement.Isthmus, IslandArrangement.Reef,
    };

    /// <summary>
    /// How many slabs the ground gains between one cell from a watercourse and
    /// five, averaged over the island. This is what a valley <i>is</i> — the land
    /// falling toward its river for a long way before it reaches it — and it is
    /// measurable where "does the valley pass look right" is not.
    /// </summary>
    /// <summary>
    /// Whether the island's walled river reaches can actually be bridged.
    ///
    /// A river running between two cliffs is fine — the grammar makes gorges on
    /// purpose, and not every river should be crossable everywhere. But a gorge
    /// whose two rims never line up within a deck's tolerance <i>anywhere along
    /// its length</i> is a wall with water at the bottom: the only way across is
    /// to walk the whole reach round. This measures how often that happens,
    /// using the exact rule the reach flood builds bridges with —
    /// <see cref="Traversal.Walkable"/> endpoints, <see cref="Traversal.DeckFits"/>
    /// over the gap, levels within <see cref="Traversal.MaxBridgeRise"/> — so
    /// what it reports is what the game would let you build, not a re-derivation.
    ///
    /// A <b>gorge cell</b> is a river cell with dry ground three slabs or more
    /// above its water on both sides of one axis; a <b>reach</b> is a
    /// 4-connected run of them, counted from three cells long, since a one-cell
    /// gorge is a doorway rather than a wall. A reach is <b>sealed</b> when no
    /// legal deck crosses any of its cells on either axis, and <b>misaligned</b>
    /// when, additionally, a deck's geometry fit somewhere along it and only the
    /// rims' disagreement refused it — the pure frustration case the analysis
    /// exists to count.
    /// </summary>
    private static int AnalyseGorges(IslandData d, ref int cells, List<int> lengths,
                                     List<int> sealedLengths, List<int> detours,
                                     ref int crossable, ref int shut, ref int skew)
    {
        int n = d.Size;
        int span = Math.Max(1, d.BridgeSpan);

        var walled = new bool[n, n];
        var canCross = new bool[n, n];
        var riseOnly = new bool[n, n];

        // The first dry ground out from the water on this side, looked for
        // through the channel itself — a navigable river is two cells across,
        // and its gorge wall stands beyond its partner, not beside each cell.
        bool Rim(int x, int z, int dx, int dz, short w)
        {
            for (int step = 1; step <= 3; step++)
            {
                int nx = x + dx * step, nz = z + dz * step;
                if (!InBounds(n, nx, nz)) return false;
                if (!d.HasLand(nx, nz)) return false;              // the island's rim
                if (d.WaterLevel[nx, nz] != IslandData.NoLand) continue;
                return d.SurfaceLevel(nx, nz) - w >= 3;
            }
            return false;
        }

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.River[x, z]) continue;
            short w = d.WaterLevel[x, z];

            for (int axis = 0; axis < 2; axis++)
            {
                int dx = axis == 0 ? 1 : 0, dz = axis == 0 ? 0 : 1;
                if (Rim(x, z, -dx, -dz, w) && Rim(x, z, dx, dz, w))
                    walled[x, z] = true;

                // Every deck whose run crosses this cell on this axis: near end
                // i cells back, far end j cells on, the whole thing inside the
                // span the reach flood would allow.
                for (int i = 1; i <= span && !canCross[x, z]; i++)
                for (int j = 1; i + j <= span + 1; j++)
                {
                    int ax = x - dx * i, az = z - dz * i;
                    int bx = x + dx * j, bz = z + dz * j;
                    if (ax < 0 || az < 0 || bx >= n || bz >= n) continue;
                    if (!Traversal.Walkable(d, ax, az)
                        || !Traversal.Walkable(d, bx, bz)) continue;
                    if (!Traversal.DeckFits(d, ax, az, dx, dz, i + j, span)) continue;
                    int rise = Math.Abs(Traversal.CrossLevel(d, ax, az)
                                        - Traversal.CrossLevel(d, bx, bz));
                    if (rise > Traversal.MaxBridgeRise) { riseOnly[x, z] = true; continue; }
                    canCross[x, z] = true;
                    break;
                }
            }
        }

        var seen = new bool[n, n];
        var stack = new Stack<(int X, int Z)>();
        var members = new List<(int X, int Z)>();
        int reaches = 0;

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!walled[sx, sz] || seen[sx, sz]) continue;

            members.Clear();
            bool anyCross = false, anySkew = false;
            seen[sx, sz] = true;
            stack.Push((sx, sz));
            while (stack.Count > 0)
            {
                var (cx, cz) = stack.Pop();
                members.Add((cx, cz));
                anyCross |= canCross[cx, cz];
                anySkew |= riseOnly[cx, cz];
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (!InBounds(n, nx, nz) || seen[nx, nz]) continue;
                    if (!walled[nx, nz]) continue;
                    seen[nx, nz] = true;
                    stack.Push((nx, nz));
                }
            }

            cells += members.Count;
            if (members.Count < 3) continue;
            reaches++;
            lengths.Add(members.Count);
            if (!anyCross)
            {
                shut++;
                sealedLengths.Add(members.Count);
                if (anySkew) skew++;
                continue;
            }
            crossable++;

            // How far the walk to the nearest deck is from the worst cell of
            // the reach — one site on a fifty-cell gorge is still a detour, and
            // this is the number that says how long a one.
            var dist = new Dictionary<(int X, int Z), int>();
            var q = new Queue<(int X, int Z)>();
            foreach (var m in members)
                if (canCross[m.X, m.Z]) { dist[m] = 0; q.Enqueue(m); }
            while (q.Count > 0)
            {
                var (cx, cz) = q.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    var next = (X: cx + Dx[k], Z: cz + Dz[k]);
                    if (!InBounds(n, next.X, next.Z)) continue;
                    if (!walled[next.X, next.Z] || dist.ContainsKey(next)) continue;
                    dist[next] = dist[(cx, cz)] + 1;
                    q.Enqueue(next);
                }
            }
            int worst = 0;
            foreach (var m in members)
                if (dist.TryGetValue(m, out int got)) worst = Math.Max(worst, got);
            detours.Add(worst);
        }
        return reaches;
    }

    private static bool ValleyRise(IslandData d, out double rise, List<double>? perRiver = null)
    {
        rise = 0;
        int n = d.Size;
        var dist = new int[n, n];
        var basin = new int[n, n];
        var q = new Queue<(int X, int Z)>();

        // Which watercourse each cell belongs to, carried out with the distance —
        // `Valleys` now acts per river, so a single island-wide average would hide
        // exactly the thing the knob is for: at a half, some courses should have a
        // narrow valley and some a wide one.
        int rivers = LabelRivers(d, basin);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            dist[x, z] = -1;
            if (!d.River[x, z]) continue;
            dist[x, z] = 0;
            q.Enqueue((x, z));
        }
        if (q.Count == 0) return false;

        const int Far = 5;
        while (q.Count > 0)
        {
            (int cx, int cz) = q.Dequeue();
            if (dist[cx, cz] >= Far) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = cx + Dx[k], nz = cz + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (dist[nx, nz] >= 0 || !d.HasLand(nx, nz)) continue;
                dist[nx, nz] = dist[cx, cz] + 1;
                basin[nx, nz] = basin[cx, cz];
                q.Enqueue((nx, nz));
            }
        }

        var near = new double[rivers];
        var far = new double[rivers];
        var nearN = new int[rivers];
        var farN = new int[rivers];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (d.WaterLevel[x, z] != IslandData.NoLand) continue;
            int b = basin[x, z];
            if (b < 0 || b >= rivers) continue;
            if (dist[x, z] == 1) { near[b] += d.SurfaceLevel(x, z); nearN[b]++; }
            else if (dist[x, z] == Far) { far[b] += d.SurfaceLevel(x, z); farN[b]++; }
        }

        double total = 0;
        int counted = 0;
        for (int b = 0; b < rivers; b++)
        {
            if (nearN[b] == 0 || farN[b] == 0) continue;
            double one = far[b] / farN[b] - near[b] / nearN[b];
            perRiver?.Add(one);
            total += one;
            counted++;
        }
        if (counted == 0) return false;

        rise = total / counted;
        return true;
    }

    /// <summary>4-connected components of the channel network: one river each.</summary>
    private static int LabelRivers(IslandData d, int[,] basin)
    {
        int n = d.Size;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) basin[x, z] = -1;

        int count = 0;
        var stack = new Stack<(int X, int Z)>();
        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!d.River[sx, sz] || basin[sx, sz] >= 0) continue;
            int id = count++;
            basin[sx, sz] = id;
            stack.Push((sx, sz));
            while (stack.Count > 0)
            {
                (int cx, int cz) = stack.Pop();
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (!InBounds(n, nx, nz)) continue;
                    if (!d.River[nx, nz] || basin[nx, nz] >= 0) continue;
                    basin[nx, nz] = id;
                    stack.Push((nx, nz));
                }
            }
        }
        return count;
    }

    private static bool Wet(IslandData d, int x, int z)
        => x >= 0 && z >= 0 && x < d.Size && z < d.Size
           && d.WaterLevel[x, z] != IslandData.NoLand;

    /// <summary>Corner-only touches: a join you can neither walk nor swim through.</summary>
    private static int DiagonalOnly(int n, Func<int, int, bool> inSet, int[,]? sameAs = null)
    {
        bool Same(int ax, int az, int bx, int bz)
            => sameAs == null || sameAs[ax, az] == sameAs[bx, bz];

        int bad = 0;
        for (int x = 0; x + 1 < n; x++)
        for (int z = 0; z + 1 < n; z++)
        {
            bool a = inSet(x, z), b = inSet(x + 1, z + 1);
            bool c = inSet(x + 1, z), e = inSet(x, z + 1);
            if (a && b && !c && !e && Same(x, z, x + 1, z + 1)) bad++;
            if (c && e && !a && !b && Same(x + 1, z, x, z + 1)) bad++;
        }
        return bad;
    }
}
