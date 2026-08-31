using System;
using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Generation;

/// <summary>
/// Cuts watercourses across finished terrain.
///
/// <para><b>There is no sea.</b> A Domain floats in aether, so every drop that
/// lands on it leaves by pouring off the rim — which is the one strong image the
/// whole system is built toward. Rivers therefore have exactly one destination
/// and it is the void.</para>
///
/// <para>They are cut <b>across</b> the patchwork, after it, rather than being
/// laid down first and having regions drawn around them: a river that only ever
/// follows a border reads as a seam, and it would make the partition answer to
/// the hydrology instead of the other way round.</para>
///
/// <para>The routing is a <b>priority flood</b> from the coast inward, not a
/// steepest-descent walk. Terrain built under a slope limit is mostly flats and
/// shallow pits, so descent stalls constantly and the flat-resolver becomes the
/// whole algorithm. Flooding inward from the outlets gives every cell a
/// downstream neighbour by construction, handles flats and depressions without a
/// special case, and passes straight through a lake — so a lake's outflow is
/// wherever the terrain actually lets it out, not somewhere chosen.</para>
/// </summary>
internal static class Rivers
{
    /// <summary>
    /// Upstream cells a channel needs before it is a river rather than a trickle,
    /// as a share of the island's land. Tuned so a 96² island gets a handful of
    /// named watercourses instead of a delta of threads.
    /// </summary>
    private const float SourceShare = 0.055f;

    /// <summary>
    /// Where a river becomes wide enough to move goods on — and, at the same
    /// time, too wide to ford. A navigable river is two cells across, which is
    /// still inside the bridge span, so it divides the country without cutting
    /// it off.
    /// </summary>
    /// <remarks>
    /// Lowered from 0.30 when each lake stopped spilling from every shore cell.
    /// A dozen outflows per lake meant a dozen traced courses converging below it,
    /// and the threshold was tuned against that inflation: with one outflow apiece
    /// it took five courses meeting to make a navigable reach, and navigable rivers
    /// all but vanished (146 cells across 60 islands). At 0.16 it took three —
    /// and lowered again to 0.11 (2026-09-01, with the confluence floor going from
    /// three rivers' flow to two) because at the preset's Rivers = 0.5 that "three"
    /// left a median island 17 navigable cells: one short reach, read from the lab
    /// as no wide rivers at all. Now a course is navigable below its first real
    /// confluence, which is where a barge would in fact get in.
    /// </remarks>
    private const float NavigableShare = 0.11f;

    /// <summary>A drop this deep along a watercourse is a fall rather than a rapid.</summary>
    public const int FallDepth = 3;

    /// <summary>
    /// How far a stream's bed is cut below the ground it runs through, in slabs.
    /// <b>Two.</b> One was enough to make the step grammar work and it looked
    /// wrong — filled to the level of the ground beside it, the water read as a
    /// sheet poured over the terrain rather than as a river in a channel. At two,
    /// the banks stand a slab proud of the water and the course has a bed.
    ///
    /// It stays fordable: you step down a slab to the water and up a slab out of
    /// it, and <see cref="Traversal.CrossLevel"/> is what makes the analysis
    /// measure that rather than the bed.
    /// </summary>
    public const int StreamDepth = 2;

    /// <summary>
    /// The same for a navigable river, which carries two slabs of water because a
    /// barge needs the draught — and which is not fordable at all.
    /// </summary>
    public const int NavigableDepth = 3;

    /// <summary>
    /// Slabs a rim fall is drawn falling past the underside of the Domain before
    /// it is left to the aether. There is nothing below to catch it.
    /// </summary>
    public const int RimFallTail = 16;

    private static readonly int[] Dx = { 1, -1, 0, 0 };
    private static readonly int[] Dz = { 0, 0, 1, -1 };

    /// <summary>
    /// Routes drainage and carves what qualifies: a bed two slabs into
    /// <paramref name="surface"/> (three where the river is navigable), filled to
    /// one slab below the ground it crosses, with the banks brought down to meet
    /// it. A stream is therefore a channel you can see and still a ford you can
    /// walk — see <see cref="StreamDepth"/> and <see cref="CutBanks"/>.
    /// </summary>
    /// <param name="keep">
    /// Cells the water may not touch — the bridgeheads, and the king's-move
    /// neighbourhood of every goo puddle, because fluids never mix.
    /// </param>
    /// <param name="form">Landform per column, so a bank that is a mesa rim is left alone.</param>
    /// <param name="fluid">
    /// What stands in each flooded column. Anything that is not water is
    /// not-land to the routing: nothing drains through it, out of it, or into it.
    /// </param>
    public static void Carve(int seed, IslandParams p, bool[,] land, short[,] surface,
                             short[,] water, bool[,] river, bool[,] navigable,
                             int[,] flow, List<Fall> falls, int bridgeSpan, byte[,] form,
                             bool[,] keep, byte[,] fluid)
    {
        int n = p.Size;
        float strength = Math.Clamp(p.Rivers, 0f, 1f);
        if (strength <= 0.001f) return;

        var order = new List<Vector2I>(n * n);
        var down = new Vector2I[n, n];
        Route(seed, n, land, surface, water, order, down, fluid);
        if (order.Count == 0) return;

        // Accumulate upstream-first, which is the routing order reversed: the
        // flood reached each cell from its downstream neighbour, so walking the
        // list backwards always sees a cell before the one it drains into.
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

        // A wetter island lowers the bar for what counts as a river.
        int landCells = order.Count;
        float ease = Mathf.Lerp(2.2f, 0.45f, strength);
        int riverAt = Math.Max(24, (int)(landCells * SourceShare * ease));
        int navigableAt = Math.Max(riverAt * 2, (int)(landCells * NavigableShare * ease));

        // A navigable river is two cells across and cannot be waded, so on a
        // Domain where a bridge only spans one cell it would cut the country in
        // half with nothing to be done about it. There, every watercourse stays a
        // stream: an easy Domain is one you can always get across.
        if (bridgeSpan < 2) navigableAt = int.MaxValue;

        // Accumulation alone gives almost nothing here, and the reason is
        // structural rather than a matter of tuning. Every rim cell is an outlet,
        // so water leaves by the shortest way out, and terrain built under a
        // one-slab slope limit has no valleys to gather it — the drainage fans
        // out from each coast cell and no catchment ever grows large. Measured:
        // a median of 13 river cells an island, and 11 navigable cells over 60.
        //
        // So the sources are *named* instead of emerging. Every summit and every
        // lake outflow starts a watercourse, and it is traced to the rim whatever
        // its catchment, which is what makes a river run the length of an island
        // rather than dribble off the nearest edge. Accumulation still decides
        // how wide it gets on the way down.
        foreach (Vector2I src in Sources(seed, p, n, land, surface, water, down, strength))
            Trace(n, src, down, flow, riverAt);

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

        // Eyots: where a navigable river splits round an island of its own bank.
        var eyot = new bool[n, n];
        Braid(seed, n, land, water, surface, down, channel, navigable, twin, keep, eyot);

        var before = (short[,])surface.Clone();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!channel[x, z]) continue;

            // <b>A river has a bed.</b> The channel is cut two slabs below the
            // ground it crosses and filled to one below it, so the banks stand a
            // slab proud of the water and the course reads as a channel rather
            // than as water poured over the terrain. A navigable river is cut a
            // slab deeper again: two slabs of water, which is the draught a barge
            // wants and more than anyone wades.
            //
            // A stream stays free to cross — down a slab into the water, up a slab
            // out of it — and Traversal.CrossLevel is what makes the analysis
            // measure that step rather than the bed under it.
            int depth = navigable[x, z] ? NavigableDepth : StreamDepth;
            river[x, z] = true;

            // A widened cell takes the level of the channel it was widened from,
            // so the two cells of a navigable river are one surface rather than a
            // step down its own length. Read from the snapshot: that cell may
            // already have been cut by this same loop, and cutting a cut is a
            // trench.
            Vector2I pair = twin[x, z];
            int ground = pair.X >= 0 ? before[pair.X, pair.Y] : before[x, z];

            water[x, z] = (short)(ground - 1);
            surface[x, z] = (short)(ground - depth);
        }

        Descend(n, order, down, river, water, surface);
        Beach(n, water, river, surface, eyot);
        CutValleys(seed, p, n, land, surface, water, river, navigable, form, keep, twin);
        // <b>And again, because the valley moved the channel.</b> A course sinks
        // with its valley, and the taper holds that sink to a slab a cell — so
        // where a bridgehead or a mesa rim stops one stretch going down as far as
        // the stretch above it, the water would be climbing. And the two cells of
        // a navigable pair are one river: the valley's per-cell caps, and Descend
        // itself — which walks the axis's chain and never its partner's — can each
        // move one cell of the pair without the other, which reads as a river with
        // one side above the other. Settle runs both corrections until both hold
        // at once; each only ever lowers, so it converges in a pass or two.
        Settle(n, order, down, river, navigable, water, surface, twin);
        FlattenReaches(n, order, down, river, navigable, water, surface);
        Settle(n, order, down, river, navigable, water, surface, twin);
        // The eyots again: flattening a reach lowers the water round an eyot that
        // Beach had already stood one slab clear of it, and an island two or three
        // slabs above its river is a plinth rather than a bar of floodplain.
        Beach(n, water, river, surface, eyot);
        CutBanks(n, land, surface, water, river, form, keep);
        FindFalls(n, land, surface, water, river, navigable, down, falls);
    }

    /// <summary>Cells either side of a course the valley reaches, at full strength.</summary>
    private const int ValleyReach = 5;

    /// <summary>
    /// Bands of valley a course carries: the full <paramref name="reach"/> where a
    /// barge could work it, one less for a brook. A navigable river has cut its
    /// own valley for longer, and a stream you can step across should not read as
    /// the bottom of the same landscape.
    /// </summary>
    private static int Budget(int reach, bool navigable)
        => reach <= 0 ? 0 : navigable ? reach : Math.Max(1, reach - 1);

    /// <summary>
    /// Labels each 4-connected component of the channel network: one river and
    /// everything that drains into it, down to the rim it pours off.
    ///
    /// This is what lets `Valleys` act per watercourse rather than per island —
    /// see <see cref="CutValleys"/>. It is a component of the *drawn* channel
    /// rather than a true catchment, so two courses that happen to touch count as
    /// one river; on terrain where every course ends at the rim that is nearly
    /// always the same answer, and it is the answer the eye gives too.
    /// </summary>
    private static int[,] LabelBasins(int n, bool[,] river, out int count)
    {
        var basin = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) basin[x, z] = -1;

        count = 0;
        var stack = new Stack<Vector2I>();

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!river[sx, sz] || basin[sx, sz] >= 0) continue;

            int id = count++;
            basin[sx, sz] = id;
            stack.Push(new Vector2I(sx, sz));
            while (stack.Count > 0)
            {
                Vector2I c = stack.Pop();
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!river[nx, nz] || basin[nx, nz] >= 0) continue;
                    basin[nx, nz] = id;
                    stack.Push(new Vector2I(nx, nz));
                }
            }
        }
        return basin;
    }

    /// <summary>
    /// Whether a valley may take this cell down with it: ground whose height is
    /// not the point of it, not a bridgehead, and not already under standing
    /// water. A channel counts — the river sinks with its own valley.
    ///
    /// <b>The bridgeheads matter as much as the landforms.</b> A crossing's banks
    /// are levelled so a deck can be walked onto at either end, so they are pinned
    /// exactly like a mesa rim is — and a valley that sank the ground beside one
    /// left the bank standing over a hole it had just dug. That was most of the
    /// two-slab steps the valley pass was adding.
    /// </summary>
    private static bool Sinkable(byte[,] form, bool[,] river, short[,] water, bool[,] keep,
                                 int x, int z)
    {
        if (keep[x, z]) return false;
        if (river[x, z]) return true;
        if (water[x, z] != IslandData.NoLand) return false;
        var type = (LandformType)form[x, z];
        return type is LandformType.Plain or LandformType.Hills or LandformType.Dunes;
    }

    /// <summary>
    /// Sinks the ground either side of a watercourse, so a river runs along the
    /// bottom of something.
    ///
    /// <para>Cutting a channel two slabs into flat ground makes an <i>incision</i>
    /// — the water is lower than the land and nothing else about the land knows
    /// it. Real country falls toward its rivers for a long way before it reaches
    /// them, and that is most of what makes a river read as the lowest place
    /// around rather than as a groove.</para>
    ///
    /// <para><b>It goes down in whole bands, which is what keeps it safe.</b>
    /// Every cell the same distance from the water drops by the same amount, so
    /// two cells inside one band keep the height they had relative to each other
    /// and the only step the pass creates is the single slab between one band and
    /// the next — the free step. That is why this can run over finished terrain at
    /// all: it cannot invent a cliff. What it will not touch is ground whose
    /// height is the landform (a mesa rim, a basin wall, a gully, a tower), a
    /// bridgehead, or anything already under water.</para>
    ///
    /// <para><b>The channel sinks with its valley, and that is the whole of it.</b>
    /// This used to hold the river where it was and lower only the ground beside
    /// it — which cannot make a valley, because the bank already stands exactly one
    /// slab above the water and so has nowhere to go. The "never into standing
    /// water" guard therefore pinned the innermost band at zero, the taper read
    /// that zero as a constraint and capped each band at one more than the band
    /// inside it, and the profile came out <i>inverted</i>: a moat two or three
    /// cells out from the river with the ground rising back toward the water.
    /// Measured over twelve seeds, the ground five cells from a river stood
    /// 0.06 slabs <i>lower</i> than the ground beside it at full strength — the
    /// slider's whole range was worth nothing, and what it did do was the opposite
    /// of what it says. Sinking the channel by one band more than the bank beside
    /// it is what makes the river the lowest thing in its own valley.</para>
    /// </summary>
    private static void CutValleys(int seed, IslandParams p, int n, bool[,] land,
                                   short[,] surface, short[,] water, bool[,] river,
                                   bool[,] navigable, byte[,] form, bool[,] keep,
                                   Vector2I[,] twin)
    {
        // <b>The slider's top half was past the point of taste.</b> Full depth on
        // every course turned the country into trenches, and everything anyone
        // actually chose lived below a half — so the whole range now maps onto
        // what used to be its lower half, and 1.0 means "the most valley worth
        // having" rather than "the most valley the code can cut". 0 is still
        // exactly nothing.
        float strength = Math.Clamp(p.Valleys, 0f, 1f) * 0.5f;
        if (strength <= 0.001f) return;

        // <b>Per watercourse, not per island.</b> One reach for the whole Domain
        // made the knob all-or-nothing: below a threshold no river had a valley
        // and above it every river had the same one, which is not what a country
        // looks like. Each drainage — a 4-connected component of the channel
        // network, which is one river and its tributaries down to the rim — draws
        // a rank in [0,1) and keeps it, and `Valleys` slides a window across those
        // ranks. So 0 gives none; a quarter gives the low-ranked few a narrow
        // valley each; a half has some narrow and some wide; and 1 cuts every
        // course to its full depth.
        int[,] basin = LabelBasins(n, river, out int basins);

        // How far each course falls between its head and the rim, which is the
        // course's own measure of how uneven the country it crosses is: a river
        // dropping fifteen slabs came down through hills, one dropping three
        // crossed a plain.
        var lo = new int[basins];
        var hi = new int[basins];
        Array.Fill(lo, int.MaxValue);
        Array.Fill(hi, int.MinValue);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!river[x, z]) continue;
            int b = basin[x, z];
            lo[b] = Math.Min(lo[b], water[x, z]);
            hi[b] = Math.Max(hi[b], water[x, z]);
        }

        var carve = new float[basins];
        for (int b = 0; b < basins; b++)
        {
            float rank = Hash01(seed, 0x7A11Eu ^ (uint)b * 2654435761u);
            // <b>Valleys go where the country is uneven.</b> A valley is what a
            // river cuts working down through relief, and a plain gains nothing
            // from one but a trench — so a course's descent tilts its rank: a
            // steep course draws from the low end of the window and a flat one
            // from the high end, up to ±0.35 of the range. 0 still cuts nothing
            // anywhere; at 1 every steep course carries a valley and a plain
            // course a shallow one — the trench-everything top of the old range
            // is what the rescaling above retired.
            float relief = hi[b] < lo[b] ? 0f
                : Math.Clamp((hi[b] - lo[b] - 3) / 12f, 0f, 1f);
            rank = Math.Clamp(rank + (0.5f - relief) * 0.7f, 0f, 1f);
            // Three times the strength against twice the rank, so the window both
            // slides and widens: at a quarter about a third of the courses have a
            // shallow valley and the rest none; at a half three quarters do, at
            // depths from a nick to the full cut; by three quarters all of them do.
            // A plain (2s - rank) window gave every river a valley from a half
            // onward, which is the all-or-nothing this was meant to fix.
            carve[b] = Math.Clamp(strength * 3f - rank * 2f, 0f, 1f);
        }

        var dist = new int[n, n];
        var wide = new bool[n, n];
        var reachOf = new int[n, n];
        var q = new Queue<Vector2I>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            dist[x, z] = -1;
            reachOf[x, z] = 0;
            if (!river[x, z]) continue;

            int cut = (int)MathF.Round(ValleyReach * carve[basin[x, z]]);
            if (cut <= 0) continue;                     // this river keeps its incision

            dist[x, z] = 0;
            wide[x, z] = navigable[x, z];
            reachOf[x, z] = cut;
            q.Enqueue(new Vector2I(x, z));
        }

        while (q.Count > 0)
        {
            Vector2I c = q.Dequeue();
            int budget = Budget(reachOf[c.X, c.Y], wide[c.X, c.Y]);
            if (dist[c.X, c.Y] >= budget) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!land[nx, nz] || dist[nx, nz] >= 0) continue;
                dist[nx, nz] = dist[c.X, c.Y] + 1;
                wide[nx, nz] = wide[c.X, c.Y];
                reachOf[nx, nz] = reachOf[c.X, c.Y];
                q.Enqueue(new Vector2I(nx, nz));
            }
        }

        // The deepest any course on this island cuts, which is how far the
        // monotone pass below has to walk.
        int reach = 0;
        for (int b = 0; b < basins; b++)
            reach = Math.Max(reach, (int)MathF.Round(ValleyReach * carve[b]));
        if (reach <= 0) return;

        // How far each cell wants to sink, before anything is applied. The channel
        // itself is band 0 and sinks furthest, which is what makes the rest a
        // valley rather than a moat.
        var want = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            int band = dist[x, z];
            if (band < 0) continue;
            int budget = Budget(reachOf[x, z], wide[x, z]);
            if (band > budget) continue;
            if (!land[x, z]) continue;

            if (!Sinkable(form, river, water, keep, x, z)) continue;

            // Never into a *lake* beside it. The river is not a floor any more —
            // it is coming down with us — but standing water is.
            //
            // <b>And never a new cliff against ground that cannot come with it.</b>
            // A mesa top, a karst tower or a levelled bridgehead inside the bands
            // keeps its height — that height is the point of it — so a cell beside
            // one that sinks freely turns a border you could walk across into a
            // face you cannot. Measured: allowing it took the island's two-slab
            // steps off mountains from 727 to 1951. The cell may still end one slab
            // below such a neighbour, which is the free step and no wall.
            int floor = int.MinValue;
            int room = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                if (water[nx, nz] != IslandData.NoLand && !river[nx, nz])
                    floor = Math.Max(floor, water[nx, nz] + 1);

                if (!Sinkable(form, river, water, keep, nx, nz)
                    && surface[x, z] - surface[nx, nz] <= 1)
                    room = Math.Min(room, surface[x, z] - surface[nx, nz] + 1);
            }
            if (floor != int.MinValue) room = Math.Min(room, surface[x, z] - floor);

            // The channel is band 0 and sinks one further than the bank beside it,
            // which is what makes the rest a valley rather than a moat.
            want[x, z] = Math.Clamp(budget - band + 1, 0, Math.Max(0, room));
        }

        // <b>Never deeper than the band inside it.</b> A valley is a profile that
        // only ever falls as you walk toward the water, and the caps above are
        // per-cell — a bank with a lake behind it can take less than the band
        // outside it wants, and without this the ground would rise inward again.
        // Walking the bands outward and holding each to what its inner neighbour
        // got is what keeps the shape whatever the ground allows.
        for (int band = 1; band <= reach; band++)
        {
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (dist[x, z] != band || want[x, z] <= 0) continue;
                int inner = 0;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (dist[nx, nz] == band - 1) inner = Math.Max(inner, want[nx, nz]);
                }
                want[x, z] = Math.Min(want[x, z], inner);
            }
        }

        // Tapered, so the valley side falls a slab at a time and the edge of the
        // pass is not a cliff — see FieldOps.Taper. It only ever *reduces* a cell,
        // and it reduces to one more than its smallest neighbour, so a band can
        // never be pulled below the one outside it: the profile above survives.
        FieldOps.Taper(want, land);

        // <b>One cut for the two cells of a navigable pair.</b> The caps above
        // are per-cell — a lake or a pinned landform beside one cell of the pair
        // can hold it back while its partner sinks free — and an unequal sink is
        // a river with one side standing above the other. The pair takes the
        // smaller cut, which only ever reduces, so nothing the taper settled is
        // disturbed.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Vector2I a = twin[x, z];
            if (a.X < 0 || !river[x, z] || !river[a.X, a.Y]) continue;
            int m = Math.Min(want[x, z], want[a.X, a.Y]);
            want[x, z] = m;
            want[a.X, a.Y] = m;
        }

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (want[x, z] <= 0) continue;
            surface[x, z] = (short)(surface[x, z] - want[x, z]);
            // The channel takes its water down with it, or the valley fills.
            if (river[x, z]) water[x, z] = (short)(water[x, z] - want[x, z]);
        }

        // Tapering keeps the *change* to a slab between neighbours, which is not
        // the same as keeping the *result* under one: a cell that already stood a
        // slab above its neighbour and sinks a slab less than it does now stands
        // two. This is the same correction CutBanks makes at the water's edge,
        // walked over the valley instead — lower the ambiguous cell by one, and
        // keep going while that leaves another behind it.
        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!land[x, z] || river[x, z] || keep[x, z]) continue;
                if (water[x, z] != IslandData.NoLand) continue;
                var type = (LandformType)form[x, z];
                if (type is not (LandformType.Plain or LandformType.Hills
                                 or LandformType.Dunes)) continue;

                int floor = int.MinValue;
                bool ambiguous = false;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                    if (water[nx, nz] != IslandData.NoLand)
                        floor = Math.Max(floor, water[nx, nz] + 1);
                    if (surface[x, z] - surface[nx, nz] == 2) ambiguous = true;
                }
                if (!ambiguous || surface[x, z] - 1 < floor) continue;
                surface[x, z]--;
                changed = true;
            }
            if (!changed) break;
        }
    }

    /// <summary>Cells of stream between one ford and the next.</summary>
    public const int FordSpacing = 11;

    /// <summary>
    /// Marks the places a stream can be crossed on foot.
    ///
    /// <para><b>A stream used to be fordable everywhere</b>, which is the same as
    /// not being there: a watercourse that costs nothing to cross at any point on
    /// its length is a line drawn on the map rather than a feature of it. It also
    /// made roads walk *down* streams, since the bed was exactly as cheap as the
    /// bank. Now the crossing is a place — a ford every <see cref="FordSpacing"/>
    /// cells or so, and the stream is an obstacle between them.</para>
    ///
    /// <para>A ford has to be a ford: both banks across the flow dry, walkable,
    /// and within a slab of the water, so you step down into it and up out the
    /// other side. Where the cell the spacing picked will not do, the next one
    /// along is tried.</para>
    /// </summary>
    public static void MarkFords(IslandData d)
    {
        int n = d.Size;

        var seen = new bool[n, n];
        var queue = new Queue<Vector2I>();
        var order = new List<Vector2I>();

        bool Stream(int x, int z)
            => x >= 0 && z >= 0 && x < n && z < n && d.River[x, z] && !d.Navigable[x, z];

        // Whether you could actually get across here: dry, walkable ground within
        // a slab of the water on both sides of the channel.
        bool Crossable(int x, int z)
        {
            short level = d.WaterLevel[x, z];
            if (level == IslandData.NoLand) return false;

            for (int axis = 0; axis < 2; axis++)
            {
                int dx = axis == 0 ? 1 : 0, dz = axis == 0 ? 0 : 1;
                if (Bank(x - dx, z - dz, level) && Bank(x + dx, z + dz, level)) return true;
            }
            return false;
        }

        bool Bank(int x, int z, short level)
        {
            if (x < 0 || z < 0 || x >= n || z >= n) return false;
            if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) return false;
            return Math.Abs(d.SurfaceLevel(x, z) - level) <= 1;
        }

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!Stream(sx, sz) || seen[sx, sz]) continue;

            // One course at a time, in the order the flood reaches it, so the
            // spacing is measured along the water rather than across the grid.
            order.Clear();
            seen[sx, sz] = true;
            queue.Enqueue(new Vector2I(sx, sz));
            while (queue.Count > 0)
            {
                Vector2I c = queue.Dequeue();
                order.Add(c);
                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    if (!Stream(nx, nz) || seen[nx, nz]) continue;
                    seen[nx, nz] = true;
                    queue.Enqueue(new Vector2I(nx, nz));
                }
            }

            // A ford at the head of the course and then one every spacing along
            // it, sliding forward past any cell that will not take one. A short
            // course still gets one: a stream nobody can cross is a wall, and a
            // wall is not what a stream is.
            int since = FordSpacing;
            bool any = false;
            foreach (Vector2I c in order)
            {
                since++;
                if (since < FordSpacing) continue;
                if (!Crossable(c.X, c.Y)) continue;
                d.Ford[c.X, c.Y] = true;
                since = 0;
                any = true;
            }
            if (any) continue;

            foreach (Vector2I c in order)
                if (Crossable(c.X, c.Y)) { d.Ford[c.X, c.Y] = true; break; }
        }
    }

    /// <summary>
    /// How often a navigable reach splits round an island, per candidate cell.
    /// </summary>
    private const float EyotChance = 0.22f;

    /// <summary>
    /// Splits a navigable river round an <b>eyot</b> — an island of its own
    /// floodplain, left standing while the water goes both ways round it.
    ///
    /// A navigable river is already two cells across: an axis carrying the course
    /// and a partner beside it (<see cref="Widen"/>). A braided reach is that
    /// widened once more, on the <i>far</i> side of the axis, and the partner in
    /// the middle then left dry — so the water runs in two channels with land
    /// between them, which is what an eyot is. The island is a strip a few cells
    /// long lying <b>along</b> the course, so it inherits the river's bends and
    /// comes out as a spindle rather than as a block; the first and last cell of
    /// the reach stay water, which is where the two channels part and rejoin.
    ///
    /// It also does the other half of "wider rivers": a braided reach is three
    /// cells across, which is past the bridge span — and crossable anyway,
    /// because the island in the middle is somewhere to land a span on.
    /// </summary>
    private static void Braid(int seed, int n, bool[,] land, short[,] water, short[,] surface,
                              Vector2I[,] down, bool[,] channel, bool[,] navigable,
                              Vector2I[,] twin, bool[,] keep, bool[,] eyot)
    {
        // Which cell was widened from which: Widen records the partner's debt to
        // the axis, and the braid needs to look it up the other way round.
        var mate = new Vector2I[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) mate[x, z] = new Vector2I(-1, -1);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Vector2I a = twin[x, z];
            if (a.X >= 0) mate[a.X, a.Y] = new Vector2I(x, z);
        }

        var axis = new List<Vector2I>();
        var isle = new List<Vector2I>();
        var far = new List<Vector2I>();
        var spent = new bool[n, n];

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (!channel[sx, sz] || !navigable[sx, sz]) continue;
            if (mate[sx, sz].X < 0) continue;                    // not an axis cell
            if (Hash01(seed, 0xE70Au ^ (uint)(sx * 73856093 ^ sz * 19349663)) > EyotChance)
                continue;

            axis.Clear();
            isle.Clear();
            far.Clear();
            var c = new Vector2I(sx, sz);
            int want = 4 + (int)(Hash01(seed, 0xE70Bu ^ (uint)(sx * 31 + sz)) * 4f);

            for (int step = 0; step < want; step++)
            {
                if (c.X < 0 || c.Y < 0 || c.X >= n || c.Y >= n) break;
                if (!channel[c.X, c.Y] || !navigable[c.X, c.Y] || spent[c.X, c.Y]) break;

                Vector2I t = mate[c.X, c.Y];
                if (t.X < 0 || spent[t.X, t.Y]) break;
                // The far bank, directly opposite the partner across the axis.
                var f = new Vector2I(2 * c.X - t.X, 2 * c.Y - t.Y);
                if (f.X < 0 || f.Y < 0 || f.X >= n || f.Y >= n) break;
                if (!land[f.X, f.Y] || channel[f.X, f.Y] || keep[f.X, f.Y]) break;
                if (water[f.X, f.Y] != IslandData.NoLand) break;
                // Never widen into a bank standing over the river: that leaves a
                // notch in a hillside rather than a second channel.
                if (surface[f.X, f.Y] > surface[c.X, c.Y]) break;
                // The island has to be one piece, so each cell of it must touch
                // the last. A course that turns can put two partners diagonally
                // apart, and two corners touching is not an island.
                if (isle.Count > 0)
                {
                    Vector2I had = isle[^1];
                    if (Math.Abs(had.X - t.X) + Math.Abs(had.Y - t.Y) != 1) break;
                }

                axis.Add(c);
                isle.Add(t);
                far.Add(f);
                c = down[c.X, c.Y];
            }

            // Two cells of island and a cell of water at each end, at least.
            if (isle.Count < 4) continue;

            for (int i = 0; i < isle.Count; i++)
            {
                spent[axis[i].X, axis[i].Y] = true;
                spent[isle[i].X, isle[i].Y] = true;
                // The ends stay water: that is where the river parts and rejoins.
                if (i == 0 || i == isle.Count - 1) continue;

                Vector2I t = isle[i], f = far[i];
                channel[t.X, t.Y] = false;
                navigable[t.X, t.Y] = false;
                twin[t.X, t.Y] = new Vector2I(-1, -1);
                eyot[t.X, t.Y] = true;

                channel[f.X, f.Y] = true;
                navigable[f.X, f.Y] = true;
                twin[f.X, f.Y] = axis[i];
                spent[f.X, f.Y] = true;
            }
        }
    }

    /// <summary>
    /// Stands every eyot one slab clear of the water round it, once the channels
    /// have settled. Its own ground was floodplain — level with what became the
    /// bed — so left alone it would be a shoal under the surface rather than an
    /// island in it.
    /// </summary>
    private static void Beach(int n, short[,] water, bool[,] river, short[,] surface,
                              bool[,] eyot)
    {
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!eyot[x, z]) continue;

            int around = int.MinValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (river[nx, nz] && water[nx, nz] != IslandData.NoLand)
                    around = Math.Max(around, water[nx, nz]);
            }
            if (around == int.MinValue) continue;
            surface[x, z] = (short)(around + 1);
        }
    }

    private static float Hash01(int seed, uint salt)
    {
        unchecked
        {
            uint h = (uint)seed * 2654435761u ^ salt;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// <summary>
    /// Brings the banks down to the water.
    ///
    /// Cutting the bed two slabs deep is what stops a river reading as water
    /// poured over the ground — and on its own it also puts a <b>two-slab step</b>
    /// wherever the bank beside the channel happened to stand a slab proud, which
    /// is the one step height the whole grammar exists to avoid. So the river cuts
    /// its banks as well as its bed: a dry cell standing exactly two above the
    /// water comes down one slab, to the free step, and a ford stays a ford.
    ///
    /// <b>Only that step, and only by that slab.</b> A bank three or more above
    /// the water is a gorge wall — a cliff, which the grammar allows and the eye
    /// reads — and slamming it down to the waterline would cut a trench across the
    /// island wherever a river passed a rise. The correction then walks outward
    /// against the same test, so it dies out within a cell or two of the water,
    /// except up a steady hillside where it walks the whole slope down one slab
    /// and changes nothing about how the slope reads.
    /// </summary>
    private static void CutBanks(int n, bool[,] land, short[,] surface, short[,] water,
                                 bool[,] river, byte[,] form, bool[,] keep)
    {
        var queue = new Queue<Vector2I>();

        // Cuttable ground: dry, and not part of a landform whose whole point is
        // the height it stands at. A mesa's rim, a basin's wall and a mountain's
        // flank are shapes the terrain rules built deliberately; a stream running
        // over one leaves a cliff, which is a waterfall and not an ambiguity.
        bool Dry(int x, int z)
        {
            if (x < 0 || z < 0 || x >= n || z >= n) return false;
            if (!land[x, z] || water[x, z] != IslandData.NoLand) return false;
            if (keep[x, z]) return false;                        // a bridgehead is level already
            var type = (LandformType)form[x, z];
            return type is LandformType.Plain or LandformType.Hills;
        }

        // How low a cell may go. No cell may be cut into standing water beside it —
        // a lake's shore is its containment, and a channel's own bank holds the
        // channel in — and none may be cut down to within a cliff's height of a
        // basin floor beside it, which would turn the escarpment the wrong way up.
        int Floor(int x, int z)
        {
            int floor = int.MinValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (water[nx, nz] != IslandData.NoLand)
                    floor = Math.Max(floor, water[nx, nz] + 1);
                if ((LandformType)form[nx, nz] == LandformType.Basin)
                    floor = Math.Max(floor, surface[nx, nz] + 3);
            }
            return floor;
        }

        // Only the ambiguous bank is cut, and only by the one slab that makes it
        // ambiguous. A bank standing three or more above the water is a gorge
        // wall — a cliff, which the grammar allows and the eye reads — and
        // slamming it down to the waterline would carve a trench across the
        // island wherever a river happened to pass a rise.
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!river[x, z]) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!Dry(nx, nz) || surface[nx, nz] - water[x, z] != 2) continue;
                if (surface[nx, nz] - 1 < Floor(nx, nz)) continue;
                surface[nx, nz]--;
                queue.Enqueue(new Vector2I(nx, nz));
            }
        }

        while (queue.Count > 0)
        {
            Vector2I c = queue.Dequeue();
            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (!Dry(nx, nz)) continue;
                if (surface[nx, nz] - surface[c.X, c.Y] != 2) continue;
                if (surface[nx, nz] - 1 < Floor(nx, nz)) continue;
                surface[nx, nz]--;
                queue.Enqueue(new Vector2I(nx, nz));
            }
        }
    }

    /// <summary>
    /// Makes every course run downhill.
    ///
    /// The routing guarantees a downstream neighbour, not a lower one: a priority
    /// flood carries the level water had to clear forward, so at a confluence or
    /// along a lake margin a channel could be left a slab above the one it drains
    /// into. It is a handful of cells an island and it is still water running
    /// uphill, which is the one thing a river may not do.
    ///
    /// Walking the routing order backwards visits every cell before the one it
    /// drains into, so pushing the minimum downstream settles in a single pass.
    /// It only ever lowers, and only inside a channel that is already cut.
    /// </summary>
    private static bool Descend(int n, List<Vector2I> order, Vector2I[,] down,
                                bool[,] river, short[,] water, short[,] surface)
    {
        bool moved = false;
        for (int i = order.Count - 1; i >= 0; i--)
        {
            Vector2I c = order[i];
            if (!river[c.X, c.Y]) continue;

            Vector2I to = down[c.X, c.Y];
            if (to.X < 0 || !river[to.X, to.Y]) continue;
            if (water[to.X, to.Y] <= water[c.X, c.Y]) continue;

            int drop = water[to.X, to.Y] - water[c.X, c.Y];
            water[to.X, to.Y] = water[c.X, c.Y];
            surface[to.X, to.Y] = (short)(surface[to.X, to.Y] - drop);
            moved = true;
        }
        return moved;
    }

    /// <summary>
    /// Holds each navigable pair to one water level — the pair is one river and
    /// one surface, and whichever cell stands higher comes down to the other,
    /// bed and all, so the draught survives the correction.
    /// </summary>
    private static bool LevelPairs(int n, Vector2I[,] twin, bool[,] river,
                                   bool[,] navigable, short[,] water, short[,] surface)
    {
        bool moved = false;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Vector2I a = twin[x, z];
            if (a.X < 0) continue;
            if (!river[x, z] || !river[a.X, a.Y]) continue;
            if (!navigable[x, z] || !navigable[a.X, a.Y]) continue;

            short m = Math.Min(water[x, z], water[a.X, a.Y]);
            if (water[x, z] > m)
            {
                surface[x, z] = (short)(surface[x, z] - (water[x, z] - m));
                water[x, z] = m;
                moved = true;
            }
            if (water[a.X, a.Y] > m)
            {
                surface[a.X, a.Y] = (short)(surface[a.X, a.Y] - (water[a.X, a.Y] - m));
                water[a.X, a.Y] = m;
                moved = true;
            }
        }
        return moved;
    }

    /// <summary>
    /// Runs <see cref="Descend"/> and <see cref="LevelPairs"/> against each other
    /// until both hold at once. Levelling a pair can leave its downstream neighbour
    /// standing higher than the cell that was just brought down; descending a chain
    /// can split a pair the other pass had just joined. Each only ever lowers, so
    /// the loop terminates — in practice inside two passes.
    /// </summary>
    private static void Settle(int n, List<Vector2I> order, Vector2I[,] down,
                               bool[,] river, bool[,] navigable, short[,] water,
                               short[,] surface, Vector2I[,] twin)
    {
        for (int pass = 0; pass < 6; pass++)
        {
            bool moved = Descend(n, order, down, river, water, surface);
            moved |= LevelPairs(n, twin, river, navigable, water, surface);
            if (!moved) break;
        }
    }

    /// <summary>
    /// Makes a navigable river a stair of pools: dead level between drops, and
    /// every drop it keeps deep enough to be a fall.
    ///
    /// <para>A stream descending a slab every few cells reads as rapids and is
    /// left alone. A <i>barge</i> river wearing thirty little steps read wrong
    /// twice over: the two-cell surface broke into shingles wherever the pair
    /// straddled a step, and a valley shifting some steps and not others is most
    /// of what left one side of a river above the other. So the water is walked
    /// from the rim upstream, each cell held down to the level of the pool below
    /// it until the ground has risen <see cref="FallDepth"/> — and that step is
    /// kept, and is a fall. The bed goes down with the water, so the draught
    /// survives; the banks stand up to two slabs taller over the held end of a
    /// reach, which is a gorge, and the grammar allows a cliff over water.</para>
    /// </summary>
    private static void FlattenReaches(int n, List<Vector2I> order, Vector2I[,] down,
                                       bool[,] river, bool[,] navigable,
                                       short[,] water, short[,] surface)
    {
        // Outlets first: down[c] was reached before c, so every cell reads its
        // downstream pool's already-settled level.
        for (int i = 0; i < order.Count; i++)
        {
            Vector2I c = order[i];
            if (!river[c.X, c.Y] || !navigable[c.X, c.Y]) continue;
            Vector2I to = down[c.X, c.Y];
            if (to.X < 0 || !river[to.X, to.Y] || !navigable[to.X, to.Y]) continue;

            int step = water[c.X, c.Y] - water[to.X, to.Y];
            if (step <= 0 || step >= FallDepth) continue;
            water[c.X, c.Y] = water[to.X, to.Y];
            surface[c.X, c.Y] = (short)(surface[c.X, c.Y] - step);
        }
    }

    /// <summary>
    /// Sends every rim fall past the underside of the Domain. The keel is only
    /// known once the columns have been built, which is after the water is cut,
    /// so this runs at the end of the pipeline rather than with the rest of it.
    /// </summary>
    public static void DropFallsPastTheKeel(IslandData d)
    {
        for (int i = 0; i < d.Falls.Count; i++)
        {
            Fall f = d.Falls[i];
            if (!f.OffRim) continue;
            short keel = d.KeelLevel(f.Cell.X, f.Cell.Y);
            if (keel == IslandData.NoLand) continue;
            d.Falls[i] = f with { Bottom = (short)(keel - RimFallTail) };
        }
    }

    /// <summary>
    /// Where watercourses begin: the high ground, and each lake's outflow.
    ///
    /// Summits are taken in order of height, each having to stand clear of the
    /// ones already chosen so a single massif does not spend the whole budget,
    /// and each having to be well inland — a source a few cells from the rim is a
    /// trickle over the edge, not a river.
    ///
    /// <b>One outflow per lake, not one per shore cell.</b> The routing floods
    /// inward from the rim, so <i>every</i> cell along a lake's downstream shore
    /// has dry ground as its downstream neighbour — a dozen or more of them. Each
    /// became a source, each traced a river's worth of flow to the rim, and what
    /// came out below the lake was a fan of parallel channels a few cells apart
    /// that read as a shallow marsh rather than as an outflow. A lake has one
    /// spill point: the cell whose downstream ground is lowest, which is where the
    /// water would actually leave.
    /// </summary>
    private static List<Vector2I> Sources(int seed, IslandParams p, int n, bool[,] land,
                                          short[,] surface, short[,] water,
                                          Vector2I[,] down, float strength)
    {
        var found = new List<Vector2I>();

        // Each body of standing water, 4-connected, and the one cell it spills at.
        var seen = new bool[n, n];
        var stack = new Stack<Vector2I>();

        for (int sx = 0; sx < n; sx++)
        for (int sz = 0; sz < n; sz++)
        {
            if (water[sx, sz] == IslandData.NoLand || seen[sx, sz]) continue;

            var spill = new Vector2I(-1, -1);
            int lowest = int.MaxValue;

            seen[sx, sz] = true;
            stack.Push(new Vector2I(sx, sz));
            while (stack.Count > 0)
            {
                Vector2I c = stack.Pop();
                Vector2I to = down[c.X, c.Y];
                if (to.X >= 0 && water[to.X, to.Y] == IslandData.NoLand
                    && surface[to.X, to.Y] < lowest)
                {
                    lowest = surface[to.X, to.Y];
                    spill = c;
                }

                for (int k = 0; k < 4; k++)
                {
                    int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n || seen[nx, nz]) continue;
                    if (water[nx, nz] == IslandData.NoLand) continue;
                    seen[nx, nz] = true;
                    stack.Push(new Vector2I(nx, nz));
                }
            }
            if (spill.X >= 0) found.Add(spill);
        }

        var peaks = new List<Vector2I>();
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (land[x, z] && water[x, z] == IslandData.NoLand && Inland(n, land, x, z, 5))
                peaks.Add(new Vector2I(x, z));

        peaks.Sort((a, b) => surface[b.X, b.Y].CompareTo(surface[a.X, a.Y]));

        int want = 2 + (int)(strength * 4f);
        int spacing = Math.Max(10, n / 7);
        foreach (Vector2I c in peaks)
        {
            if (found.Count >= want + 8) break;
            int taken = 0;
            bool crowded = false;
            foreach (Vector2I had in found)
            {
                if (Math.Abs(had.X - c.X) + Math.Abs(had.Y - c.Y) < spacing) { crowded = true; break; }
                taken++;
            }
            _ = taken;
            if (crowded) continue;
            found.Add(c);
            if (found.Count >= want) break;
        }
        return found;
    }

    /// <summary>Whether a cell has land all round it out to <paramref name="reach"/> cells.</summary>
    private static bool Inland(int n, bool[,] land, int x, int z, int reach)
    {
        for (int k = 0; k < 4; k++)
        for (int step = 1; step <= reach; step++)
        {
            int nx = x + Dx[k] * step, nz = z + Dz[k] * step;
            if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) return false;
        }
        return true;
    }

    /// <summary>
    /// Follows the water down from a source to the rim, adding a river's worth of
    /// flow to every cell on the way. Confluences therefore add up, so two traced
    /// courses meeting make one wider than either.
    /// </summary>
    private static void Trace(int n, Vector2I from, Vector2I[,] down, int[,] flow, int add)
    {
        Vector2I c = from;
        for (int guard = 0; guard < n * n; guard++)
        {
            flow[c.X, c.Y] += add;
            Vector2I to = down[c.X, c.Y];
            if (to.X < 0) return;               // reached the rim
            c = to;
        }
    }

    /// <summary>
    /// Gives every land cell a downstream neighbour, by flooding inward from the
    /// void. Returns the order cells were reached in — outlets first — and the
    /// neighbour each was reached from, which is where its water goes.
    ///
    /// The priority is <c>max(own height, the height water had to clear to get
    /// here)</c>. Carrying that maximum forward is what makes a depression fill
    /// and spill at its lowest rim rather than trapping the flood, and it is why
    /// a lake needs no special handling: water enters, crosses, and leaves.
    ///
    /// <para><b>Ties are broken by a noise field, not by insertion order.</b> That
    /// one change is what stops rivers running in straight lines. Terrain built
    /// under a slope limit is mostly flats, so most of the flood is a tie — and a
    /// first-in-first-out tie-break makes the flood a plain breadth-first search,
    /// whose tree is a fan of straight cardinal rays. Every course traced down it
    /// came out as long straight runs meeting at right angles. Ordering equal
    /// ground by a smooth field instead makes the front advance along that field's
    /// low ground, so the tree — and the courses on it — bend at the field's
    /// wavelength. The jitter is strictly below one slab, so it can only reorder
    /// cells the terrain itself does not separate.</para>
    /// </summary>
    private static void Route(int seed, int n, bool[,] land, short[,] surface, short[,] water,
                              List<Vector2I> order, Vector2I[,] down, byte[,] fluid)
    {
        var seen = new bool[n, n];
        var lifted = new int[n, n];
        // Wavelength of the wander, in cells: about fourteen — a bend a river
        // takes, rather than a wobble along its length.
        var meander = new Noise(seed + 5701, frequency: 0.07f, octaves: 3);
        var queue = new PriorityQueue<Vector2I, long>();
        long tick = 0;

        // A column of the other fluid is not-land here: it never enters the
        // flood, so no cell drains through it, no course is traced across it,
        // and a body of goo has no spill — goo makes no rivers.
        bool Ground(int x, int z) => land[x, z] && fluid[x, z] == 0;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            down[x, z] = new Vector2I(-1, -1);
            if (!Ground(x, z)) continue;

            // An outlet is a cell with aether beside it: the rim, and nothing else.
            bool rim = false;
            for (int k = 0; k < 4 && !rim; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                rim = nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz];
            }
            if (!rim) continue;

            seen[x, z] = true;
            int level = Level(surface, water, x, z);
            lifted[x, z] = level;
            queue.Enqueue(new Vector2I(x, z), Key(level, meander, x, z, tick++));
        }

        while (queue.TryDequeue(out Vector2I c, out _))
        {
            order.Add(c);
            int reached = lifted[c.X, c.Y];

            for (int k = 0; k < 4; k++)
            {
                int nx = c.X + Dx[k], nz = c.Y + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!Ground(nx, nz) || seen[nx, nz]) continue;

                seen[nx, nz] = true;
                down[nx, nz] = c;
                int lift = Math.Max(reached, Level(surface, water, nx, nz));
                lifted[nx, nz] = lift;
                queue.Enqueue(new Vector2I(nx, nz), Key(lift, meander, nx, nz, tick++));
            }
        }
    }

    /// <summary>The level water sits at in a column: a lake's surface, or the ground.</summary>
    private static int Level(short[,] surface, short[,] water, int x, int z)
        => water[x, z] != IslandData.NoLand ? water[x, z] : surface[x, z];

    /// <summary>
    /// Height in the high bits, the meander field in the middle, insertion order
    /// in the low ones: terrain always outranks the wander, the wander outranks
    /// arrival order, and nothing is left to chance.
    /// </summary>
    /// <summary>Keeps the packed level positive; slab indices run below zero.</summary>
    private const int LevelBias = 4096;

    private static long Key(int level, Noise meander, int x, int z, long tick)
    {
        long wander = (long)(meander.At(x, z) * 0xFFFFFF) & 0xFFFFFF;
        return ((long)(level + LevelBias) << 40) | (wander << 16) | (tick & 0xFFFF);
    }

    /// <summary>
    /// Puts a second cell alongside a navigable channel. A barge needs two cells;
    /// a third would put the river past the bridge span and cut the island in
    /// half, which should be a deliberate choice and not a side effect of rain.
    /// </summary>
    private static void Widen(int n, bool[,] land, short[,] water, short[,] surface,
                              int[,] flow, Vector2I[,] down, bool[,] channel,
                              bool[,] navigable, int navigableAt, Vector2I[,] twin,
                              bool[,] keep)
    {
        var added = new List<Vector2I>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!channel[x, z] || !navigable[x, z]) continue;

            Vector2I to = down[x, z];
            if (to.X < 0) continue;

            // Perpendicular to the way the water is going.
            int fx = to.X - x, fz = to.Y - z;
            int px = fz, pz = -fx;

            int bestX = -1, bestZ = -1, bestTop = int.MaxValue;
            for (int side = -1; side <= 1; side += 2)
            {
                int nx = x + px * side, nz = z + pz * side;
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                if (!land[nx, nz] || channel[nx, nz]) continue;
                if (water[nx, nz] != IslandData.NoLand) continue;
                // A bridgehead, or the ground round a goo puddle: the channel
                // could not be cut there, so the widening may not reach there.
                if (keep[nx, nz]) continue;
                // Never widen onto ground that stands above the channel: that is a
                // bank, and cutting it away leaves a notch rather than a river.
                if (surface[nx, nz] > surface[x, z]) continue;
                if (surface[nx, nz] >= bestTop) continue;

                bestTop = surface[nx, nz];
                bestX = nx;
                bestZ = nz;
            }
            if (bestX < 0) continue;

            added.Add(new Vector2I(bestX, bestZ));
            flow[bestX, bestZ] = Math.Max(flow[bestX, bestZ], navigableAt);
            // Which cell it was widened from. The pair is one river and has to
            // hold one surface: left to take its own ground level, the second cell
            // sits a slab below the first and the audit reads a river flowing
            // sideways into itself.
            twin[bestX, bestZ] = new Vector2I(x, z);
        }

        foreach (Vector2I c in added)
        {
            channel[c.X, c.Y] = true;
            navigable[c.X, c.Y] = true;
        }
    }

    /// <summary>
    /// Where the water falls rather than runs: a drop of <see cref="FallDepth"/>
    /// or more to the next cell downstream, and every channel that reaches the rim
    /// — at the coast every river becomes a fall, because there is nowhere else
    /// for it to go.
    ///
    /// <para><b>Water pours every way it plausibly can.</b> It does not choose
    /// one edge: a corner cell of the island spills off both its aether sides, a
    /// fall into a canyon whose pool wraps two sides of the lip comes down on
    /// both, and the partner cell of a navigable pair — whose own chain runs
    /// level, because the pair was levelled — still pours over the same step its
    /// axis does. So every river cell throws a sheet off every aether edge beside
    /// it and toward every neighbouring <i>water</i> a <see cref="FallDepth"/> or
    /// more below it. Only water and aether, never dry ground: a sheet onto dry
    /// land would be a course the drainage never routed, and either floods the
    /// ground below or vanishes into it. The falls are drawn where the water
    /// already is, and nothing new gets wet.</para>
    /// </summary>
    private static void FindFalls(int n, bool[,] land, short[,] surface, short[,] water,
                                  bool[,] river, bool[,] navigable, Vector2I[,] down,
                                  List<Fall> falls)
    {
        // <b>One cell, always.</b> A navigable river is two cells across, and
        // giving its fall a width of two drew a sheet centred on one cell and
        // straddling both — so half of it hung over whatever was beside the
        // channel, which on a bank a slab higher looked like water pouring out
        // of solid rock. Both cells of the pair are river cells and both reach
        // this loop, so each emits its own sheet over its own cell and the two
        // together are the two-cell fall.
        const int width = 1;

        Span<bool> spilt = stackalloc bool[4];

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!river[x, z])
            {
                // A lake pours too, where a channel leaves it well below its own
                // surface — the outflow over a high shore is a fall the course
                // itself cannot report, because the lake cell is not a channel.
                if (!land[x, z] || water[x, z] == IslandData.NoLand) continue;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    if (!river[nx, nz] || water[nx, nz] == IslandData.NoLand) continue;
                    if (water[x, z] - water[nx, nz] < FallDepth) continue;
                    falls.Add(new Fall(new Vector2I(x, z), water[x, z], water[nx, nz],
                                       new Vector2I(Dx[k], Dz[k]), false, width));
                }
                continue;
            }

            bool lip = false;
            spilt.Clear();

            // Off the rim: every way the aether is, not the first found — a river
            // reaching a corner of the island pours off both edges. Bottom is
            // filled in once the keel is known — see DropFallsPastTheKeel —
            // because what is under a rim fall is the underside of the Domain and
            // then nothing.
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx >= 0 && nz >= 0 && nx < n && nz < n && land[nx, nz]) continue;
                falls.Add(new Fall(new Vector2I(x, z), water[x, z],
                                   (short)(water[x, z] - RimFallTail),
                                   new Vector2I(Dx[k], Dz[k]), true, width));
                spilt[k] = true;
                lip = true;
            }

            // Inland: a step of FallDepth or more onto whatever is below, which is
            // the next pool along a mountain course. This is the one sheet allowed
            // to land on dry ground, because the course itself carries on there.
            // A rim cell skips it — its course has already left the world.
            Vector2I to = down[x, z];
            if (!lip && to.X >= 0)
            {
                int below = water[to.X, to.Y] != IslandData.NoLand
                    ? water[to.X, to.Y]
                    : surface[to.X, to.Y];
                if (water[x, z] - below >= FallDepth)
                {
                    falls.Add(new Fall(new Vector2I(x, z), water[x, z], (short)below,
                                       new Vector2I(to.X - x, to.Y - z), false, width));
                    for (int k = 0; k < 4; k++)
                        if (x + Dx[k] == to.X && z + Dz[k] == to.Y) spilt[k] = true;
                }
            }

            // The extra sheets: toward any water FallDepth or more below that the
            // sheets above did not already cover.
            for (int k = 0; k < 4; k++)
            {
                if (spilt[k]) continue;
                int nx = x + Dx[k], nz = z + Dz[k];
                if (nx < 0 || nz < 0 || nx >= n || nz >= n || !land[nx, nz]) continue;
                if (water[nx, nz] == IslandData.NoLand) continue;
                if (water[x, z] - water[nx, nz] < FallDepth) continue;
                falls.Add(new Fall(new Vector2I(x, z), water[x, z], water[nx, nz],
                                   new Vector2I(Dx[k], Dz[k]), false, width));
            }
        }
    }
}
