using System;
using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Cuts the watercourses across finished terrain, after the lakes and before the
/// keel. There is no sea, so every course ends at the rim and pours off it — all
/// but the one, occasionally, that a lake swallows. The routing is a priority
/// flood inward from the rim with ties broken on noise: that gives every land
/// cell a downstream neighbour by construction, and the noise is what makes
/// rivers bend. Split by sub-stage across the partial files.
/// </summary>
internal static partial class Rivers
{
    /// <summary>Upstream land share a channel needs before it is a river rather than a trickle.</summary>
    private const float SourceShare = 0.055f;

    /// <summary>
    /// Upstream land share at which a river is navigable: two cells across, still
    /// inside the bridge span, and no longer fordable.
    /// </summary>
    private const float NavigableShare = 0.11f;

    /// <summary>A drop this deep along a watercourse is a fall rather than a rapid.</summary>
    public const int FallDepth = 3;

    /// <summary>
    /// Slabs a stream's bed is cut below the ground it crosses. Two, so the banks
    /// stand one slab proud of the water; it stays fordable — see <see cref="Traversal.CrossLevel"/>.
    /// </summary>
    private const int StreamDepth = 2;

    /// <summary>The same for a navigable river: two slabs of water for the draught, and not fordable.</summary>
    private const int NavigableDepth = 3;

    /// <summary>Slabs a rim fall is drawn past the underside of the Domain before it is left to the aether.</summary>
    private const int RimFallTail = 16;

    /// <summary>
    /// Routes drainage and carves what qualifies: a bed <see cref="StreamDepth"/>
    /// slabs into <paramref name="surface"/> (<see cref="NavigableDepth"/> where
    /// navigable) filled to one slab below the ground it crosses, the valley sunk
    /// round it, every course forced downhill, and the banks brought down to the
    /// free step. Leaves every output untouched when <c>Rivers</c> is off or there is no land.
    /// </summary>
    /// <param name="keep">Cells the water may not touch: bridgeheads, and the king's-move neighbourhood of every goo puddle.</param>
    /// <param name="form">Landform per column, so a bank that is a mesa rim is left alone, and a basin can swallow a river.</param>
    /// <param name="fluid">What stands in each flooded column; anything that is not water is not-land to the routing.</param>
    /// <param name="terminal">Out: one cell of each lake that keeps its river.</param>
    /// <param name="delta">Out: the fan of every delta.</param>
    /// <param name="deltas">Out: the apex of every delta.</param>
    /// <param name="springs">Out: where each stream begins.</param>
    public static void Carve(int seed, IslandParams p, bool[,] land, short[,] surface,
                             short[,] water, bool[,] river, bool[,] navigable,
                             int[,] flow, List<Fall> falls, int bridgeSpan, byte[,] form,
                             bool[,] keep, byte[,] fluid, List<Vector2I> terminal,
                             bool[,] delta, List<Vector2I> deltas, List<Vector2I> springs)
    {
        int n = p.Size;
        float strength = Math.Clamp(p.Rivers, 0f, 1f);
        if (strength <= 0.001f) return;

        var order = new List<Vector2I>(n * n);
        var down = new Vector2I[n, n];
        Route(seed, n, land, surface, water, order, down, fluid);
        if (order.Count == 0) return;

        Accumulate(order, down, flow);

        // A wetter island lowers the bar for what counts as a river.
        int landCells = order.Count;
        float ease = Mathf.Lerp(2.2f, 0.45f, strength);
        int riverAt = Math.Max(24, (int)(landCells * SourceShare * ease));
        int navigableAt = Math.Max(riverAt * 2, (int)(landCells * NavigableShare * ease));

        // A navigable river cannot be waded: where a bridge spans one cell it would cut the country in half.
        if (bridgeSpan < 2) navigableAt = int.MaxValue;

        // Accumulation alone gives almost nothing under a slope limit (every rim cell is
        // an outlet), so the sources are named — summits and lake outflows — and traced to the rim.
        foreach (Vector2I src in Sources(n, land, surface, water, down, strength))
            Trace(n, src, down, flow, riverAt);

        // Occasionally a river-fed lake keeps its river: the lake becomes a sink, and
        // the drainage is summed again so that nothing downstream inherits its water.
        if (SwallowRiver(seed, n, land, water, form, down, flow, riverAt, terminal))
        {
            Accumulate(order, down, flow);
            foreach (Vector2I src in Sources(n, land, surface, water, down, strength))
                Trace(n, src, down, flow, riverAt);
        }

        var channel = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z] || flow[x, z] < riverAt) continue;
            if (water[x, z] != IslandData.NoLand) continue;      // already a lake
            if (keep[x, z]) continue;                            // a bridgehead
            channel[x, z] = true;
            navigable[x, z] = flow[x, z] >= navigableAt;
        }

        var twin = new Vector2I[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) twin[x, z] = new Vector2I(-1, -1);

        Widen(n, land, water, surface, flow, down, channel, navigable, navigableAt, twin, keep);

        var eyot = new bool[n, n];
        Braid(seed, n, land, water, surface, down, channel, navigable, twin, keep, eyot);

        // A delta's arms branch off the pair; each arm's head is held to the cell it leaves.
        var arm = new bool[n, n];
        var branch = new Vector2I[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) branch[x, z] = new Vector2I(-1, -1);
        Fan(n, land, water, surface, down, flow, channel, navigable, twin, keep, eyot,
            riverAt, delta, deltas, arm, branch);

        CutBeds(n, channel, navigable, twin, river, water, surface);

        Descend(n, order, down, river, water, surface, branch);
        Beach(n, water, river, surface, eyot);
        CutValleys(seed, p, n, land, surface, water, river, navigable, form, keep, twin);
        // The valley can leave water climbing at a pinned cell and sink one cell of a
        // pair without the other; Settle only ever lowers, so it converges.
        Settle(n, order, down, river, navigable, water, surface, twin, branch);
        FlattenReaches(n, order, down, river, navigable, water, surface);
        Settle(n, order, down, river, navigable, water, surface, twin, branch);
        // Flattening a reach lowers the water round an eyot Beach had already stood clear of it.
        Beach(n, water, river, surface, eyot);
        CutBanks(n, land, surface, water, river, form, keep);
        FindFalls(n, land, surface, water, river, down, falls);
        FindSprings(n, river, navigable, water, down, arm, springs);
    }

    /// <summary>
    /// Upstream cell counts: one per cell, summed downstream. The routing order
    /// reversed visits every cell before the one it drains into.
    /// </summary>
    private static void Accumulate(List<Vector2I> order, Vector2I[,] down, int[,] flow)
    {
        for (int i = 0; i < order.Count; i++)
        {
            Vector2I c = order[i];
            flow[c.X, c.Y] = 1;
        }
        for (int i = order.Count - 1; i >= 0; i--)
        {
            Vector2I c = order[i];
            Vector2I to = down[c.X, c.Y];
            if (to.X >= 0) flow[to.X, to.Y] += flow[c.X, c.Y];
        }
    }

    /// <summary>
    /// Cuts every channel cell's bed and fills it to one slab below the ground it
    /// crosses. A widened cell takes its partner's ground, so a navigable pair is
    /// one surface; the ground is read from a snapshot because cutting a cut is a trench.
    /// </summary>
    private static void CutBeds(int n, bool[,] channel, bool[,] navigable, Vector2I[,] twin,
                                bool[,] river, short[,] water, short[,] surface)
    {
        var before = (short[,])surface.Clone();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!channel[x, z]) continue;

            int depth = navigable[x, z] ? NavigableDepth : StreamDepth;
            river[x, z] = true;

            Vector2I pair = twin[x, z];
            int ground = pair.X >= 0 ? before[pair.X, pair.Y] : before[x, z];

            water[x, z] = (short)(ground - 1);
            surface[x, z] = (short)(ground - depth);
        }
    }
}
