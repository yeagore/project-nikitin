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

    /// <summary>Slabs from one massif terrace to the next.</summary>
    private const int TerraceRiser = 4;

    /// <summary>Slabs a sinkhole drops below the ground it is punched out of.</summary>
    private const int SinkDepth = 6;

    /// <summary>
    /// Cuts and raises the sculpted landforms into the settled surface. The cells
    /// touched are exempted from the limiter afterwards, like a canyon's; nothing
    /// is sculpted on a patch's outer ring, so every border stays bound.
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

        // One field per landform; the gullies are ridged noise, its creases the drainage lines.
        var gully = new Noise(seed + 611, frequency: 0.16f, octaves: 3, ridged: true)
            .WithWarp(amplitude: 6f, frequency: 0.05f);
        // Low enough that a tower is a few cells across rather than a chimney.
        var towers = new Noise(seed + 733, frequency: 0.18f, octaves: 2);
        var terrace = new Noise(seed + 857, frequency: 0.055f, octaves: 3);

        // Share of a patch's half-width left unsculpted; `inward` is 0 at the border, 1 at the middle.
        const float Rim = 0.18f;

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!land[x, z]) continue;
            int r = region[x, z];
            RegionPlan rp = plan[r];
            if (!Landforms.IsSculpted(rp.Type)) continue;

            float u = inward[x, z];
            if (u <= Rim) continue;

            bool cut = rp.Type switch
            {
                LandformType.Badlands => CutGully(h, x, z, scale, gully),
                LandformType.Karst => RaiseTower(h, x, z, scale, towers),
                LandformType.Massif => RaiseTerrace(h, x, z, u, scale, terrace),
                LandformType.Sinkholes => PunchSinkhole(h, x, z, scale, towers),
                _ => false,
            };
            if (cut) carved[x, z] = true;
        }

        Despeckle(land, region, h, carved);
        return carved;
    }

    /// <summary>
    /// A gully wherever the ridged field creases, cut a fixed depth: a tapered
    /// gully has a two-slab step somewhere along its length by construction.
    /// </summary>
    private static bool CutGully(short[,] h, int x, int z, float scale, Noise gully)
    {
        if (gully.At(x, z) > 0.62f) return false;
        h[x, z] = Terrain.SlabClamp(h[x, z] - (int)MathF.Round(GullyDepth * (0.7f + 0.5f * scale)));
        return true;
    }

    /// <summary>
    /// A tower on the high ground of the blobby field, raised bodily off the floor:
    /// taller where the field is stronger, one height throughout so the sides are sheer.
    /// </summary>
    private static bool RaiseTower(short[,] h, int x, int z, float scale, Noise towers)
    {
        float t = towers.At(x, z);
        if (t < 0.62f) return false;
        int rise = (int)MathF.Round(TowerRise * (0.6f + 0.9f * scale)
                                    * (0.75f + 0.5f * towers.At(x * 0.13f, z * 0.13f)));
        h[x, z] = Terrain.SlabClamp(h[x, z] + Math.Max(4, rise));
        return true;
    }

    /// <summary>
    /// A terrace ring from the patch's own inward distance warped by noise, so the
    /// rings follow the massif's shape rather than being circles.
    /// </summary>
    private static bool RaiseTerrace(short[,] h, int x, int z, float u, float scale, Noise terrace)
    {
        float warped = Math.Clamp(u + (terrace.At(x, z) - 0.5f) * 0.34f, 0f, 1f);
        int rings = 3 + (int)(scale * 2.5f);
        int ring = (int)(warped * rings);
        if (ring <= 0) return false;
        h[x, z] = Terrain.SlabClamp(h[x, z] + ring * TerraceRiser);
        return true;
    }

    /// <summary>
    /// A round pit punched out of open ground: the karst field read from above,
    /// sampled far away and thresholded low so what drops out is isolated holes.
    /// </summary>
    private static bool PunchSinkhole(short[,] h, int x, int z, float scale, Noise towers)
    {
        if (towers.At(x + 512f, z - 512f) > 0.30f) return false;
        h[x, z] = Terrain.SlabClamp(h[x, z] - (int)MathF.Round(
            SinkDepth * (0.7f + 0.6f * scale)));
        return true;
    }

    /// <summary>
    /// Undoes any sculpted cell with no sculpted neighbour at its own level: a lone
    /// pit or pillar reads as a mistake at orchard scale. Decides from a snapshot,
    /// then applies.
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

    /// <summary>Whether this island gets a canyon: one in five.</summary>
    internal static bool WantsCanyon(int seed, IslandParams p) => Hash01(seed, 0x4C17) < 0.20f;

    /// <summary>
    /// Cuts up to two passes: a broad wobbled sag where one plateau comes down to
    /// meet the next, so a cliff border has one place you can walk across. Only
    /// rung-ladder borders qualify (a mesa with a pass is not a mesa), and most
    /// islands get none or one: passes are flavour, connectivity is infrastructure.
    /// </summary>
    /// <returns>The cells the saddles touched, or <c>null</c> if no pass was cut.</returns>
    internal static bool[,]? CutPasses(int seed, IslandParams p, bool[,] land, int[,] region,
                                      RegionPlan[] plan, short[,] h,
                                      Dictionary<long, List<(int X, int Z)>> borders,
                                      List<Vector2I> sites)
    {
        float roll = Hash01(seed, 0x9E15);
        int want = roll < 0.35f ? 0 : roll < 0.80f ? 1 : 2;
        if (want == 0) return null;

        int n = p.Size;
        var options = RankPassSites(seed, p, land, region, plan, h, borders, n);
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

            if (!SagPass(land, region, plan, h, mask, wobble, px, pz, drop, n)) continue;
            sites.Add(new Vector2I(px, pz));
            cut++;
        }
        return cut > 0 ? mask : null;
    }

    /// <summary>
    /// Every ladder border that could take a pass, scored count × jitter / drop at
    /// its cheapest crossing, in border order; the caller sorts.
    /// </summary>
    private static List<(float Score, int X, int Z, int Drop)> RankPassSites(
        int seed, IslandParams p, bool[,] land, int[,] region, RegionPlan[] plan, short[,] h,
        Dictionary<long, List<(int X, int Z)>> borders, int n)
    {
        int maxDrop = Math.Max(6, p.CliffHeight * 2);
        var options = new List<(float Score, int X, int Z, int Drop)>();

        foreach (var (key, cells) in borders)
        {
            if (cells.Count < 8) continue;
            int a = (int)(key >> 32), b = (int)(key & 0xFFFFFFFF);
            if (!LadderPair(plan[a], plan[b])) continue;

            // The cheapest crossing on the border: least ground to move, least scar.
            int bestDrop = int.MaxValue;
            int bx = -1, bz = -1;
            foreach (var (x, z) in cells)
            {
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Dx[k], nz = z + Dz[k];
                    if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
                    if (region[nx, nz] == region[x, z]) continue;

                    // Under three is no cliff: the grammar resolves a two-slab step away anyway.
                    int drop = Math.Abs(h[x, z] - h[nx, nz]);
                    if (drop < 3 || drop > maxDrop || drop >= bestDrop) continue;
                    bestDrop = drop;
                    bx = x;
                    bz = z;
                }
            }
            if (bx < 0) continue;

            float jitter = 0.6f + 0.8f * Hash01(seed, 0x5A11u ^ (uint)key * 2654435761u);
            options.Add((cells.Count * jitter / bestDrop, bx, bz, bestDrop));
        }
        return options;
    }

    /// <summary>
    /// Sags a wobbled disc round one border point toward the floor across it. The
    /// radius is drop + 4 so the grade stays under a slab per cell; tables and
    /// mountains are never sagged; the sag stops a cliff's height above a basin floor.
    /// </summary>
    /// <returns>Whether there was a floor across the border to sag toward.</returns>
    private static bool SagPass(bool[,] land, int[,] region, RegionPlan[] plan, short[,] h,
                                bool[,] mask, Noise wobble, int px, int pz, int drop, int n)
    {
        float radius = drop + 4f;
        int floor = int.MaxValue;
        for (int k = 0; k < 4; k++)
        {
            int nx = px + Dx[k], nz = pz + Dz[k];
            if (!InBounds(n, nx, nz) || !land[nx, nz]) continue;
            if (region[nx, nz] != region[px, pz]) floor = Math.Min(floor, h[nx, nz]);
        }
        if (floor == int.MaxValue) return false;

        int span = (int)MathF.Ceiling(radius) + 2;
        for (int x = Math.Max(0, px - span); x <= Math.Min(n - 1, px + span); x++)
        for (int z = Math.Max(0, pz - span); z <= Math.Min(n - 1, pz + span); z++)
        {
            if (!land[x, z]) continue;
            // A col is cut through the rung ladder, never through a landform that is its own height.
            if (plan[region[x, z]].Type is LandformType.Mountain
                or LandformType.Mesa or LandformType.Basin) continue;

            float dx = x - px, dz = z - pz;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist < 0.001f) dist = 0.001f;

            // Wobble sampled on the unit circle, so the outline is seamless where the angle wraps.
            float rEff = radius * (0.75f + 0.5f * wobble.At(dx / dist, dz / dist));
            if (dist > rEff) continue;

            float w = 1f - FieldOps.SmoothStep(0f, 1f, dist / rEff);
            int target = (int)MathF.Round(h[x, z] + (floor - h[x, z]) * w);
            // Sagging to a basin's floor would invert its escarpment.
            if (plan[region[x, z]].Type != LandformType.Basin)
                target = Math.Max(target, StepGrammar.BasinFloorNear(land, h, region, plan, n, x, z));
            if (target < h[x, z]) h[x, z] = Terrain.SlabClamp(target);
            mask[x, z] = true;
        }
        return true;
    }

    /// <summary>Whether a border's drop is the plateau ladder (plain or hills both sides, different rungs) rather than a landform.</summary>
    private static bool LadderPair(RegionPlan a, RegionPlan b)
    {
        static bool Soft(LandformType t) => t is LandformType.Plain or LandformType.Hills;
        return Soft(a.Type) && Soft(b.Type) && a.RungGroup != b.RungGroup;
    }

    /// <summary>
    /// Cuts a trench along one region border, preferring a border that is
    /// otherwise invisible (same rung, same landform): a canyon is a boundary made
    /// legible. Never beside a mesa or basin, whose rim is already an escarpment.
    /// </summary>
    /// <returns>The cells the trench took, or <c>null</c> if none was cut.</returns>
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
            if (Landforms.IsTable(plan[a].Type) || Landforms.IsTable(plan[b].Type)) continue;

            int score = cells.Count;
            if (plan[a].Plateau == plan[b].Plateau) score *= 4;   // otherwise invisible
            if (plan[a].Type == plan[b].Type) score *= 2;
            if (score > bestScore) { bestScore = score; chosen = cells; }
        }
        if (chosen == null) return null;

        int n = p.Size;
        // The seed set covers both sides of the border, so the trench is two cells wide before it grows.
        int halfWidth = Hash01(seed, 0x3B71) < 0.7f ? 0 : 1;        // 2 or 4 cells across
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
            // A trench beside a table rim would drop the plain below the floor it looks down on.
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
