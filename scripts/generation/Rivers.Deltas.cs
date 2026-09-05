using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Generation;

/// <summary>Deltas and springs: where a navigable river parts into mouths over a gentle coast, and where a stream begins.</summary>
internal static partial class Rivers
{
    /// <summary>Axis cells upstream of a navigable mouth at which the distributaries part.</summary>
    private const int DeltaLength = 4;

    /// <summary>Cells an arm may walk looking for the rim before it is given up.</summary>
    private const int DeltaReach = 9;

    /// <summary>Forward cells an arm must make before the rim: a mouth that is one notch beside the pair is no delta.</summary>
    private const int MinArmForward = 2;

    /// <summary>
    /// Splits every navigable river that meets the rim over a gentle coast into two
    /// or three mouths. From the axis cell <see cref="DeltaLength"/> upstream of the
    /// mouth, an arm leaves each side of the pair — a step sideways, two forward,
    /// and again, so it parts from the river at a low angle, cardinal all the way —
    /// until it reaches the rim, as a stream of its own with the pair's cell as its
    /// head. An arm that would climb, drop more than a step, run beside standing
    /// water, or take a bridgehead, an eyot or a cell the river already holds, or
    /// that finds no rim inside <see cref="DeltaReach"/> cells, is not cut: a cliff
    /// coast has no delta. The dry ground between the mouths, apex to rim, is the
    /// fan, and the surface stage makes it floodplain whatever the climate.
    /// </summary>
    private static void Fan(int n, bool[,] land, short[,] water, short[,] surface,
                            Vector2I[,] down, int[,] flow, bool[,] channel, bool[,] navigable,
                            Vector2I[,] twin, bool[,] keep, bool[,] eyot, int riverAt,
                            bool[,] delta, List<Vector2I> deltas, bool[,] arm, Vector2I[,] branch)
    {
        // Widen recorded the partner's debt to the axis; the delta reads it the other way round.
        var mate = new Vector2I[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) mate[x, z] = new Vector2I(-1, -1);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Vector2I a = twin[x, z];
            if (a.X >= 0) mate[a.X, a.Y] = new Vector2I(x, z);
        }

        var cells = new List<Vector2I>();
        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            // A mouth: an axis cell (never a partner) with nothing downstream. Widen
            // gives the last cell no partner — it needs a downstream cell to find the
            // side — so the mouth is known by what it is not.
            if (!channel[sx, sz] || !navigable[sx, sz] || twin[sx, sz].X >= 0) continue;
            if (down[sx, sz].X >= 0 || arm[sx, sz]) continue;

            // Back up the axis to the apex; a junction or a braid on the way is no delta.
            var apex = new Vector2I(sx, sz);
            bool clean = true;
            for (int step = 0; step < DeltaLength && clean; step++)
            {
                var up = new Vector2I(-1, -1);
                int found = 0;
                for (int k = 0; k < 4; k++)
                {
                    int nx = apex.X + Dx[k], nz = apex.Y + Dz[k];
                    if (!InBounds(n, nx, nz) || !channel[nx, nz] || !navigable[nx, nz]) continue;
                    if (mate[nx, nz].X < 0 || down[nx, nz] != apex) continue;
                    up = new Vector2I(nx, nz);
                    found++;
                }
                if (found != 1) clean = false;
                else apex = up;
            }
            if (!clean) continue;

            Vector2I f = down[apex.X, apex.Y] - apex;
            if (Math.Abs(f.X) + Math.Abs(f.Y) != 1) continue;
            var p = new Vector2I(f.Y, -f.X);

            int reach = 0;
            bool left = false, right = false;
            for (int side = -1; side <= 1; side += 2)
            {
                // The arm leaves the pair's outer cell on its side: the partner if it lies there, else the axis.
                Vector2I start = apex + p * side;
                if (!InBounds(n, start.X, start.Y) || !channel[start.X, start.Y]
                    || twin[start.X, start.Y] != apex)
                    start = apex;

                int forward = WalkArm(n, land, water, surface, channel, keep, eyot,
                                      start, f, p * side, cells);
                if (forward < 0) continue;

                for (int i = 0; i < cells.Count; i++)
                {
                    Vector2I a = cells[i];
                    channel[a.X, a.Y] = true;
                    navigable[a.X, a.Y] = false;
                    arm[a.X, a.Y] = true;
                    flow[a.X, a.Y] = Math.Max(flow[a.X, a.Y], riverAt);
                    down[a.X, a.Y] = i + 1 < cells.Count ? cells[i + 1] : new Vector2I(-1, -1);
                }
                branch[cells[0].X, cells[0].Y] = start;
                reach = Math.Max(reach, forward);
                if (side < 0) left = true; else right = true;
            }
            if (!left && !right) continue;

            deltas.Add(apex);
            for (int along = 0; along <= reach + 1; along++)
            for (int across = -(along / 2 + 1); across <= along / 2 + 1; across++)
            {
                if ((across < 0 && !left) || (across > 0 && !right)) continue;
                Vector2I c = apex + f * along + p * across;
                if (!InBounds(n, c.X, c.Y) || !land[c.X, c.Y]) continue;
                if (channel[c.X, c.Y] || water[c.X, c.Y] != IslandData.NoLand) continue;
                delta[c.X, c.Y] = true;
            }
        }
    }

    /// <summary>
    /// Walks one arm from <paramref name="start"/> — sideways, forward, forward, and
    /// round — over gentle dry ground until a rim cell, filling <paramref name="cells"/>
    /// with the arm. Returns how many cells forward it got, or −1 where it could not
    /// reach the rim.
    /// </summary>
    private static int WalkArm(int n, bool[,] land, short[,] water, short[,] surface,
                               bool[,] channel, bool[,] keep, bool[,] eyot,
                               Vector2I start, Vector2I f, Vector2I side, List<Vector2I> cells)
    {
        cells.Clear();
        Vector2I c = start;
        int forward = 0;
        for (int i = 0; i < DeltaReach; i++)
        {
            Vector2I next = c + (i % 3 == 0 ? side : f);
            if (!InBounds(n, next.X, next.Y) || !land[next.X, next.Y]) return -1;
            if (channel[next.X, next.Y] || keep[next.X, next.Y] || eyot[next.X, next.Y]) return -1;
            if (water[next.X, next.Y] != IslandData.NoLand) return -1;
            // Never climbs, never drops more than the free step: the coast is gentle or there is no delta.
            if (surface[next.X, next.Y] > surface[c.X, c.Y]) return -1;
            if (surface[c.X, c.Y] - surface[next.X, next.Y] > 1) return -1;
            for (int k = 0; k < 4; k++)
            {
                int nx = next.X + Dx[k], nz = next.Y + Dz[k];
                if (InBounds(n, nx, nz) && land[nx, nz] && water[nx, nz] != IslandData.NoLand) return -1;
            }

            cells.Add(next);
            if (i % 3 != 0) forward++;
            c = next;
            // The rim before two cells forward is a river running along the coast: a notch, not a mouth.
            if (Rim(n, land, c)) return forward >= MinArmForward ? forward : -1;
        }
        return -1;
    }

    /// <summary>Whether a land cell has aether beside it.</summary>
    private static bool Rim(int n, bool[,] land, Vector2I c)
    {
        for (int k = 0; k < 4; k++)
        {
            int nx = c.X + Dx[k], nz = c.Y + Dz[k];
            if (!InBounds(n, nx, nz) || !land[nx, nz]) return true;
        }
        return false;
    }

    /// <summary>
    /// Where a stream begins: a stream cell no other channel cell drains into, not
    /// beside standing water (that is a lake's outflow, and the lake is the source)
    /// and not a delta's arm (that is a river's mouth). A navigable head is a
    /// widened pair's artefact, not a spring.
    /// </summary>
    private static void FindSprings(int n, bool[,] river, bool[,] navigable, short[,] water,
                                    Vector2I[,] down, bool[,] arm, List<Vector2I> springs)
    {
        var fed = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!river[x, z]) continue;
            Vector2I to = down[x, z];
            if (to.X >= 0 && river[to.X, to.Y]) fed[to.X, to.Y] = true;
        }

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!river[x, z] || navigable[x, z] || arm[x, z] || fed[x, z]) continue;
            bool shore = false;
            for (int k = 0; k < 4 && !shore; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                shore = InBounds(n, nx, nz) && !river[nx, nz] && water[nx, nz] != IslandData.NoLand;
            }
            if (!shore) springs.Add(new Vector2I(x, z));
        }
    }
}
