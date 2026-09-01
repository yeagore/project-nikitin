using System;
using System.Collections.Generic;
using Godot;
using static ProjectNikitin.Generation.Grid;
using static ProjectNikitin.Generation.SeedHash;

namespace ProjectNikitin.Generation;

/// <summary>The sculpted landforms cut into a settled surface, and the deliberate breaches: passes and canyons.</summary>
internal static class Sculpting
{
    /// <summary>Slabs a badlands gully is cut below the fingers either side of it.</summary>
    private const int GullyDepth = 5;

    /// <summary>Slabs a karst tower stands above the floor it grows out of.</summary>
    private const int TowerRise = 13;

    /// <summary>Slabs from one ziggurat terrace to the next.</summary>
    private const int TerraceRiser = 4;

    /// <summary>Slabs a sinkhole drops below the ground it is punched out of.</summary>
    private const int SinkDepth = 6;

    /// <summary>
    /// Cuts and raises the sculpted landforms into the finished plain.
    ///
    /// <para><b>Why this is a separate pass.</b> Every other landform is relief
    /// under a slope limit, which is what makes the step grammar hold by
    /// construction — and it is also why the ladder can only put a cliff at a
    /// patch <i>border</i>. A gully, a tower and a terrace riser are cliffs
    /// <i>inside</i> a patch, so they cannot come from relief at all. They are
    /// cut into a surface the limiter has already settled, and the cells they
    /// touch are then exempted from it, exactly as a canyon is — that is the
    /// mechanism the pipeline already had for "a cliff somebody asked for".</para>
    ///
    /// <para><b>Nothing is sculpted on a patch border.</b> The outermost ring of
    /// every patch is left at the level the limiter agreed with the neighbours,
    /// so a badlands beside a plain still meets it at a walkable step and the
    /// cliff rule holds at every border it has. All the drama is interior.</para>
    /// </summary>
    /// <returns>The cells that were cut or raised, to be exempted from the limiter.</returns>
    internal static bool[,] Sculpt(int seed, IslandParams p, bool[,] land, int[,] region,
                                  RegionPlan[] plan, short[,] h, float[,] inward)
    {
        int n = p.Size;
        var carved = new bool[n, n];
        float scale = Landforms.ReliefScale(p);

        bool any = false;
        foreach (RegionPlan rp in plan) if (Landforms.IsSculpted(rp.Type)) { any = true; break; }
        if (!any) return carved;

        // One field per landform, so two of them on one island do not share a
        // pattern. The gullies are ridged noise — its creases are the drainage
        // lines a badlands erodes along.
        var gully = new Noise(seed + 611, frequency: 0.16f, octaves: 3, ridged: true)
            .WithWarp(amplitude: 6f, frequency: 0.05f);
        // Low enough that a tower is a few cells across rather than a needle: one
        // cell is an orchard, and a column an orchard wide is a chimney.
        var towers = new Noise(seed + 733, frequency: 0.18f, octaves: 2);
        var terrace = new Noise(seed + 857, frequency: 0.055f, octaves: 3);

        // How far into a patch the sculpting starts, as a share of its half-width.
        // `inward` is 0 at the border and 1 at the middle.
        const float Rim = 0.18f;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int r = region[x, z];
            RegionPlan rp = plan[r];
            if (!Landforms.IsSculpted(rp.Type)) continue;

            float u = inward[x, z];
            if (u <= Rim) continue;                     // the rim is the patch's word to its neighbours

            switch (rp.Type)
            {
                // A maze of gullies: wherever the ridged field creases, the ground
                // drops a fixed depth. Fixed, not tapered — a tapering gully has a
                // two-slab step somewhere along its length by construction, and
                // two slabs is the one height the grammar forbids.
                case LandformType.Badlands:
                    if (gully.At(x, z) > 0.62f) continue;
                    h[x, z] = Terrain.SlabClamp(h[x, z] - (int)MathF.Round(GullyDepth * (0.7f + 0.5f * scale)));
                    carved[x, z] = true;
                    break;

                // Towers: the high ground of a blobby field, raised bodily off the
                // floor. The threshold is high, so what is left is columns of a
                // few cells rather than a plateau with holes in it.
                case LandformType.Karst:
                {
                    float t = towers.At(x, z);
                    if (t < 0.62f) continue;
                    // Taller where the field is stronger, but each tower is one
                    // height throughout: the sides are meant to be sheer.
                    int rise = (int)MathF.Round(TowerRise * (0.6f + 0.9f * scale)
                                                * (0.75f + 0.5f * towers.At(x * 0.13f, z * 0.13f)));
                    h[x, z] = Terrain.SlabClamp(h[x, z] + Math.Max(4, rise));
                    carved[x, z] = true;
                    break;
                }

                // Concentric terraces. The contour is the patch's own inward
                // distance warped by noise, so the rings follow the shape of the
                // massif and wander in and out of it rather than being circles.
                case LandformType.Massif:
                {
                    float warped = Math.Clamp(u + (terrace.At(x, z) - 0.5f) * 0.34f, 0f, 1f);
                    int rings = 3 + (int)(scale * 2.5f);
                    int ring = (int)(warped * rings);
                    if (ring <= 0) continue;
                    h[x, z] = Terrain.SlabClamp(h[x, z] + ring * TerraceRiser);
                    carved[x, z] = true;
                    break;
                }

                // Round pits punched out of open ground: the same limestone as the
                // karst, read from above instead of from the side. The threshold is
                // low and the field is smooth, so what drops out is isolated holes
                // rather than the connected maze a badlands makes.
                case LandformType.Sinkholes:
                {
                    if (towers.At(x + 512f, z - 512f) > 0.30f) continue;
                    h[x, z] = Terrain.SlabClamp(h[x, z] - (int)MathF.Round(
                        SinkDepth * (0.7f + 0.6f * scale)));
                    carved[x, z] = true;
                    break;
                }
            }
        }

        // A one-cell terrace is a ledge, and a one-cell gully is a hole. Anything
        // the fields left isolated is filled back in, which is cheaper than
        // tuning the thresholds to never produce one.
        Despeckle(land, region, h, carved);
        return carved;
    }

    /// <summary>
    /// Undoes any sculpted cell with no sculpted neighbour at its own level. A
    /// lone pit or pillar reads as a mistake at this scale — one cell is an
    /// orchard — and it is also the shape most likely to leave an ambiguous step
    /// behind it.
    /// </summary>
    private static void Despeckle(bool[,] land, int[,] region, short[,] h, bool[,] carved)
    {
        int n = land.GetLength(0);
        var lone = new List<(int X, int Z, int To)>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!carved[x, z]) continue;

            int kin = 0, floor = int.MinValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                if (carved[nx, nz] && h[nx, nz] == h[x, z]) kin++;
                else if (!carved[nx, nz]) floor = Math.Max(floor, (int)h[nx, nz]);
            }
            if (kin == 0 && floor != int.MinValue) lone.Add((x, z, floor));
        }

        foreach (var (x, z, to) in lone)
        {
            h[x, z] = Terrain.SlabClamp(to);
            carved[x, z] = false;
        }
    }

    internal static bool WantsCanyon(int seed, IslandParams p) => TerrainHash01(seed, 0x4C17) < 0.20f;

    /// <summary>
    /// Cuts a <b>pass</b>: a saddle where one plateau sags down to meet the next,
    /// so a cliff border has exactly one place you can walk across.
    ///
    /// <para>Not a ramp. A ramp was tried and removed (docs §4c): a mesa stands
    /// five or six slabs, a one-slab-per-cell grade covers that in five or six
    /// cells, and five risers in a row against flat open ground is a staircase by
    /// any reading. The failure was the <i>shape</i>, not the grade — a narrow
    /// causeway sticking out into a plain shows every riser in profile.</para>
    ///
    /// <para>A pass is instead a broad radial sag, some fifteen to twenty cells
    /// across, centred on a point of the border. The ground either side of the
    /// path descends with it, so the eye reads a valley rather than a stair, and
    /// the same grade that failed as a causeway works as a col. Its outline is a
    /// noise-wobbled radius, so it is not a disc.</para>
    ///
    /// <para><b>Occasional on purpose.</b> Passes are flavour, not the
    /// connectivity answer — that is infrastructure (see <see cref="Traversal"/>).
    /// Cutting one on every border would flatten the island into a single
    /// walkable district and throw away the plateau ladder. Most islands get
    /// none or one.</para>
    ///
    /// <para>Only rung-ladder cliffs qualify: both sides plain or hills, neither a
    /// mesa, basin or mountain. A mesa with a pass cut into it stops being a
    /// mesa — the landform <i>is</i> "flat top, cliff all round" — and a mesa top
    /// is reachable with a stair anyway.</para>
    /// </summary>
    /// <returns>The cells the saddle touched, or <c>null</c> if no pass was cut.</returns>
    internal static bool[,]? CutPasses(int seed, IslandParams p, bool[,] land, int[,] region,
                                      RegionPlan[] plan, short[,] h,
                                      Dictionary<long, List<(int X, int Z)>> borders,
                                      List<Vector2I> sites)
    {
        float roll = TerrainHash01(seed, 0x9E15);
        int want = roll < 0.35f ? 0 : roll < 0.80f ? 1 : 2;
        if (want == 0) return null;

        int n = p.Size;
        int maxDrop = Math.Max(6, p.CliffHeight * 2);

        // Rank the borders that could take one: a real drop, room to sag into, and
        // a pair of patches whose difference is the ladder rather than a landform.
        var options = new List<(float Score, int X, int Z, int Drop)>();

        foreach (var (key, cells) in borders)
        {
            if (cells.Count < 8) continue;
            int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
            if (!LadderPair(plan[a], plan[b])) continue;

            // The cheapest crossing on this border, which is where a pass would
            // form: least ground to move, least scar.
            int bestDrop = int.MaxValue;
            int bx = -1, bz = -1;
            foreach (var (x, z) in cells)
            {
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                    if (region[nx, nz] == region[x, z]) continue;

                    // Three, not two: a two-slab step is not a cliff — the grammar
                    // pass that runs after this one resolves it to a walkable step
                    // anyway, so a pass cut there does nothing but scar the ground.
                    int drop = Math.Abs(h[x, z] - h[nx, nz]);
                    if (drop < 3 || drop > maxDrop || drop >= bestDrop) continue;
                    bestDrop = drop;
                    bx = x;
                    bz = z;
                }
            }
            if (bx < 0) continue;

            float jitter = 0.6f + 0.8f * TerrainHash01(seed, 0x5A11u ^ (uint)key * 2654435761u);
            options.Add((cells.Count * jitter / bestDrop, bx, bz, bestDrop));
        }
        if (options.Count == 0) return null;

        options.Sort((u, v) => v.Score.CompareTo(u.Score));

        var mask = new bool[n, n];
        var wobble = new Noise(seed + 4242, frequency: 1.1f, octaves: 2);
        int cut = 0;

        foreach (var (_, px, pz, drop) in options)
        {
            if (cut >= want) break;

            // Don't stack two passes on top of each other.
            bool tooClose = false;
            foreach (Vector2I had in sites)
                if (Math.Abs(had.X - px) + Math.Abs(had.Y - pz) < 24) { tooClose = true; break; }
            if (tooClose) continue;

            // Radius from the drop, so the grade stays under a slab per cell: the
            // sag has to be longer than it is deep, or it is a staircase again.
            float radius = drop + 4f;
            int floor = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                int nx = px + Dx[k], nz = pz + Dz[k];
                if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                if (region[nx, nz] != region[px, pz]) floor = Math.Min(floor, h[nx, nz]);
            }
            if (floor == int.MaxValue) continue;

            int span = (int)MathF.Ceiling(radius) + 2;
            for (int x = Math.Max(0, px - span); x <= Math.Min(n - 1, px + span); x++)
            for (int z = Math.Max(0, pz - span); z <= Math.Min(n - 1, pz + span); z++)
            {
                if (!land[x, z]) continue;
                // A col is cut through the rung ladder, never through a landform
                // that *is* its own height. Sagging a mesa or a basin takes the
                // landform away — and marking one as pass ground is worse still,
                // because the slope limiter is told to reach across a pass border,
                // which then drags the plain down to meet the basin floor it is
                // supposed to look down on.
                if (plan[region[x, z]].Type is LandformType.Mountain
                    or LandformType.Mesa or LandformType.Basin) continue;

                float dx = x - px, dz = z - pz;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist < 0.001f) dist = 0.001f;

                // A wobbled radius, sampled on the unit circle so it is seamless
                // where the angle wraps. A perfect disc reads as a crater.
                float rEff = radius * (0.75f + 0.5f * wobble.At(dx / dist, dz / dist));
                if (dist > rEff) continue;

                float w = 1f - FieldOps.SmoothStep(0f, 1f, dist / rEff);
                int target = (int)MathF.Round(h[x, z] + (floor - h[x, z]) * w);
                // A sag reaching the rim of a basin would sink the ground to meet
                // the floor it is supposed to look down on — the escarpment
                // inverted, which is the same bug a canyon cut beside a basin used
                // to have. The col stops at a cliff's height above the floor.
                if (plan[region[x, z]].Type != LandformType.Basin)
                    target = Math.Max(target, StepGrammar.BasinFloorNear(land, h, region, plan, n, x, z));
                if (target < h[x, z]) h[x, z] = Terrain.SlabClamp(target);
                mask[x, z] = true;
            }

            sites.Add(new Vector2I(px, pz));
            cut++;
        }
        return cut > 0 ? mask : null;
    }

    /// <summary>
    /// Whether a border's drop is the plateau ladder rather than a landform. A
    /// mesa or basin escarpment and a mountain flank are the landform itself, and
    /// notching them would delete it.
    /// </summary>
    private static bool LadderPair(RegionPlan a, RegionPlan b)
    {
        static bool Soft(LandformType t) => t is LandformType.Plain or LandformType.Hills;
        return Soft(a.Type) && Soft(b.Type) && a.RungGroup != b.RungGroup;
    }

    /// <summary>
    /// Cuts a trench along the border between two regions, preferring a border
    /// that is otherwise invisible — same landform, same rung. A canyon is a
    /// boundary made legible, so cutting one straight across a region would
    /// undo the very distinction the patchwork exists to draw.
    /// </summary>
    /// <summary>Returns the cells the trench actually took, or <c>null</c> if none was cut.</summary>
    internal static bool[,]? CarveCanyon(int seed, IslandParams p, bool[,] land, int[,] region,
                                        RegionPlan[] plan, short[,] h,
                                        Dictionary<long, List<(int X, int Z)>> borders)
    {
        List<(int X, int Z)>? chosen = null;
        int bestScore = 0;

        foreach (var (key, cells) in borders)
        {
            if (cells.Count < 10) continue;
            int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);

            // Any pair of patches may be split by a canyon — unlike a cliff, which
            // is restricted to plain-plain and mesa-mesa. The exception is a mesa
            // or basin rim: that border is already an escarpment, so a trench adds
            // nothing there and only compounds the drop — a canyon cut along a
            // basin's edge leaves the plain outside it standing *below* the basin
            // floor, which reads as the escarpment pointing the wrong way.
            if (Landforms.IsTable(plan[a].Type) || Landforms.IsTable(plan[b].Type)) continue;

            int score = cells.Count;
            if (plan[a].Plateau == plan[b].Plateau) score *= 4;   // otherwise invisible
            if (plan[a].Type == plan[b].Type) score *= 2;
            if (score > bestScore) { bestScore = score; chosen = cells; }
        }
        if (chosen == null) return null;

        int n = p.Size;
        // The seed set already covers both sides of the border, so it is two cells
        // wide before the BFS grows it at all. A canyon is a crack, not a valley.
        int halfWidth = TerrainHash01(seed, 0x3B71) < 0.7f ? 0 : 1;        // 2 or 4 cells across
        int depth = Math.Max(4, (int)MathF.Round(p.CliffHeight * 1.8f));

        var dist = new int[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++) dist[x, z] = -1;

        var q = new Queue<(int X, int Z)>();
        foreach (var (x, z) in chosen) { dist[x, z] = 0; q.Enqueue((x, z)); }

        while (q.Count > 0)
        {
            var (x, z) = q.Dequeue();
            if (dist[x, z] >= halfWidth) continue;
            for (int k = 0; k < 4; k++)
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz)) continue;
                if (!land[nx, nz] || dist[nx, nz] >= 0) continue;
                dist[nx, nz] = dist[x, z] + 1;
                q.Enqueue((nx, nz));
            }
        }

        var cut = new bool[n, n];
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z] || dist[x, z] < 0) continue;
            // Stop at an escarpment. A trench cut alongside a basin rim drops the
            // plain *below* the basin floor, and the landform's whole read — a
            // hollow sunk into the ground around it — inverts. A canyon that ends
            // where it meets a cliff is what a canyon does anyway.
            if (TouchesTable(region, plan, land, x, z, n)) continue;
            h[x, z] = Terrain.SlabClamp(h[x, z] - depth);
            cut[x, z] = true;
        }
        return cut;
    }

    /// <summary>Whether a cell is in, or borders, a mesa or basin.</summary>
    private static bool TouchesTable(int[,] region, RegionPlan[] plan, bool[,] land,
                                     int x, int z, int n)
    {
        if (Landforms.IsTable(plan[region[x, z]].Type)) return true;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + Dx[k], nz = z + Dz[k];
            if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
            if (Landforms.IsTable(plan[region[nx, nz]].Type)) return true;
        }
        return false;
    }
}
