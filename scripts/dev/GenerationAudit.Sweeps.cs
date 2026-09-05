using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

public partial class GenerationAudit
{
    /// <summary>A copy of the preset with one sweep's change applied.</summary>
    private IslandParams Variant(Action<IslandParams> tweak)
    {
        var p = (IslandParams)Params.Duplicate();
        tweak(p);
        return p;
    }

    /// <summary>The islands of one sweep setting, generated in <see cref="SeedAt"/> order as they are read.</summary>
    private IEnumerable<IslandData> Sweep(IslandParams p, int seeds)
    {
        for (int i = 0; i < seeds; i++) yield return IslandGenerator.Generate(SeedAt(i), p);
    }

    /// <summary>
    /// Exits on the island and Gates of the wrong kind: every Exit against
    /// <paramref name="exit"/> (Auto matches anything), the Entry against
    /// <paramref name="entry"/> when one is given.
    /// </summary>
    private static (int Exits, int Wrong) CountExits(IslandData d, GateKind? entry, GateKind exit)
    {
        int exits = 0, wrong = 0;
        foreach (Gate g in d.Gates)
        {
            if (g.Role == GateRole.Entry)
            {
                if (entry is GateKind e && g.Kind != e) wrong++;
                continue;
            }
            exits++;
            if (exit != GateKind.Auto && g.Kind != exit) wrong++;
        }
        return (exits, wrong);
    }

    /// <summary>
    /// Every arrangement x character, FeasibilitySeeds each: attempts > 1 is the pipeline
    /// fighting, unmet is it giving up, reach% (heartland over all land cells, water
    /// included) is whether the island is one place.
    /// </summary>
    private void PrintFeasibility()
    {
        var rows = new List<(string Combo, float Attempts, int Unmet, float Reach,
                             float Masses, float Ms)>();
        int hard = 0, broken = 0;

        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            foreach (TerrainCharacter what in Enum.GetValues<TerrainCharacter>())
            {
                if (what == TerrainCharacter.Auto) continue;

                IslandParams p = Variant(q => { q.Arrangement = how; q.Character = what; });

                int attempts = 0, unmet = 0;
                float reach = 0f, masses = 0f;
                ulong t0 = Time.GetTicksMsec();

                foreach (IslandData d in Sweep(p, FeasibilitySeeds))
                {
                    attempts += d.Attempts;
                    if (d.Unmet.Length > 0) unmet++;

                    long land = 0, heart = 0;
                    for (int x = 0; x < d.Size; x++)
                    for (int z = 0; z < d.Size; z++)
                    {
                        if (!d.HasLand(x, z)) continue;
                        land++;
                        if (d.Reach[x, z] == d.Heartland && d.Heartland >= 0) heart++;
                    }
                    if (land > 0) reach += 100f * heart / land;
                    masses += LabelLandmasses(d, d.Size, new int[d.Size, d.Size]);
                }

                float ms = (Time.GetTicksMsec() - t0) / (float)FeasibilitySeeds;
                rows.Add(($"{how} / {what}", attempts / (float)FeasibilitySeeds, unmet,
                          reach / FeasibilitySeeds, masses / FeasibilitySeeds, ms));
                if (unmet > 0) broken++;
                else if (attempts > FeasibilitySeeds) hard++;
            }
        }

        GD.Print($"\n=== feasibility: every arrangement x character, {FeasibilitySeeds} "
            + $"seeds each ({rows.Count} combinations) ===");
        GD.Print("  attempts > 1.0 means the generator rejected its own first island");
        GD.Print($"  {"combination",-34} {"attempts",8} {"unmet",6} {"reach%",7} "
            + $"{"masses",7} {"ms",6}");

        rows.Sort((a, b) => b.Attempts != a.Attempts
            ? b.Attempts.CompareTo(a.Attempts)
            : b.Unmet.CompareTo(a.Unmet));

        foreach (var (combo, att, un, re, ma, ms) in rows)
        {
            bool flag = un > 0 || att > 1.001f || re < 75f;
            GD.Print($"  {combo,-34} {att,8:0.00} {un,6} {re,7:0.0} {ma,7:0.0} {ms,6:0}"
                + (flag ? "   <-- look" : ""));
        }
        GD.Print($"\n  combinations that never met the guarantees: {broken} of {rows.Count}");
        GD.Print($"  combinations that needed a re-roll:          {hard} of {rows.Count}");
    }

    /// <summary>
    /// Each Entry edge x kind, then each Exit count x kind, against what came out. These
    /// are set by the neighbouring Domain, so "usually" is a bug report, not a result.
    /// </summary>
    private void PrintGateRequests()
    {
        GD.Print($"\n=== gate requests: what was asked for, and what came out "
            + $"({SweepSeeds} seeds each) ===");

        GD.Print($"\n  {"entry asked for",-24} {"on that edge",13} {"of that kind",13} "
            + $"{"both",7} {"re-rolls",9}");
        foreach (GateEdge edge in new[] { GateEdge.North, GateEdge.East, GateEdge.South, GateEdge.West })
        foreach (GateKind kind in new[] { GateKind.Hanging, GateKind.Land })
        {
            IslandParams p = Variant(q => { q.EntryEdge = edge; q.EntryGate = kind; });

            int rightEdge = 0, rightKind = 0, both = 0, attempts = 0;
            foreach (IslandData d in Sweep(p, SweepSeeds))
            {
                attempts += d.Attempts;
                foreach (Gate g in d.Gates)
                {
                    if (g.Role != GateRole.Entry) continue;
                    bool e = (int)g.Facing == (int)edge - 1;   // Cardinal North..West = 0..3, GateEdge has Auto = 0 first
                    bool k = g.Kind == kind;
                    if (e) rightEdge++;
                    if (k) rightKind++;
                    if (e && k) both++;
                }
            }
            GD.Print($"  {$"{edge} {kind}",-24} {Pct(rightEdge, SweepSeeds),13} "
                + $"{Pct(rightKind, SweepSeeds),13} {Pct(both, SweepSeeds),7} "
                + $"{attempts / (float)SweepSeeds,9:0.00}"
                + (both < SweepSeeds ? "   <-- look" : ""));
        }

        GD.Print($"\n  {"exits asked for",-24} {"count met",13} {"all that kind",14} "
            + $"{"median got",11}");
        foreach (int count in new[] { 1, 2, 3 })
        foreach (GateKind kind in new[] { GateKind.Auto, GateKind.Hanging, GateKind.Land })
        {
            IslandParams p = Variant(q => { q.ExitGates = count; q.ExitGate = kind; });

            int met = 0, allKind = 0;
            var got = new List<int>();
            foreach (IslandData d in Sweep(p, SweepSeeds))
            {
                var (exits, wrong) = CountExits(d, null, kind);
                got.Add(exits);
                if (exits >= count) met++;
                if (wrong == 0) allKind++;
            }
            got.Sort();
            GD.Print($"  {$"{count} x {kind}",-24} {Pct(met, SweepSeeds),13} "
                + $"{Pct(allKind, SweepSeeds),14} {got[got.Count / 2],11}"
                + (met < SweepSeeds ? "   <-- look" : ""));
        }
    }

    /// <summary>
    /// Four hanging Gates, one per edge — the maximum request — against every arrangement
    /// x character; then why a coast refuses one, per character; then the reductions,
    /// measured because a rule may only fire when the pool of edges is full.
    /// </summary>
    private void PrintGateMatrix()
    {
        PrintFourHanging();
        PrintGateFunnel();
        PrintGateReductions();
    }

    /// <summary>Four hanging Gates per arrangement and per character, and the combinations that could not.</summary>
    private void PrintFourHanging()
    {
        GD.Print($"\n=== four hanging gates, every arrangement x character "
            + $"({FeasibilitySeeds} seeds each) ===");
        GD.Print("  asked: entry hanging, 3 exits hanging. 'four' = all four placed AND all hanging.");

        var byArrangement = new Dictionary<IslandArrangement, (int Four, int Gates, int Hanging, int Runs)>();
        var byCharacter = new Dictionary<TerrainCharacter, (int Four, int Runs)>();
        var worst = new List<(string Combo, int Four, float Gates, float Hanging)>();

        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            foreach (TerrainCharacter what in Enum.GetValues<TerrainCharacter>())
            {
                if (what == TerrainCharacter.Auto) continue;

                IslandParams p = Variant(q =>
                {
                    q.Arrangement = how;
                    q.Character = what;
                    q.EntryGate = GateKind.Hanging;
                    q.ExitGate = GateKind.Hanging;
                    q.ExitGates = 3;
                });

                int four = 0, gates = 0, hanging = 0;
                foreach (IslandData d in Sweep(p, FeasibilitySeeds))
                {
                    int here = d.Gates.Count, air = 0;
                    foreach (Gate g in d.Gates) if (g.Kind == GateKind.Hanging) air++;
                    gates += here;
                    hanging += air;
                    if (here == 4 && air == 4) four++;
                }

                var a = byArrangement.GetValueOrDefault(how);
                byArrangement[how] = (a.Four + four, a.Gates + gates, a.Hanging + hanging,
                                      a.Runs + FeasibilitySeeds);
                var c = byCharacter.GetValueOrDefault(what);
                byCharacter[what] = (c.Four + four, c.Runs + FeasibilitySeeds);

                if (four < FeasibilitySeeds)
                    worst.Add(($"{how} / {what}", four, gates / (float)FeasibilitySeeds,
                               hanging / (float)FeasibilitySeeds));
            }
        }

        GD.Print($"\n  {"arrangement",-16} {"four hanging",13} {"gates",7} {"hanging",8}");
        foreach (var (how, v) in byArrangement.OrderBy(k => k.Value.Four / (float)k.Value.Runs))
            GD.Print($"  {how,-16} {Pct(v.Four, v.Runs),13} "
                + $"{v.Gates / (float)v.Runs,7:0.0} {v.Hanging / (float)v.Runs,8:0.0}"
                + (v.Four < v.Runs ? "   <-- look" : ""));

        GD.Print($"\n  {"character",-16} {"four hanging",13}");
        foreach (var (what, v) in byCharacter.OrderBy(k => k.Value.Four / (float)k.Value.Runs))
            GD.Print($"  {what,-16} {Pct(v.Four, v.Runs),13}");

        int combos = 0, clean = 0;
        foreach (var (_, v) in byArrangement) { combos += v.Runs; clean += v.Four; }
        GD.Print($"\n  overall: {Pct(clean, combos)} of runs gave four hanging gates "
            + $"({combos} runs over {byArrangement.Count * byCharacter.Count} combinations)");

        worst.Sort((a, b) => a.Four != b.Four ? a.Four.CompareTo(b.Four)
                                              : a.Hanging.CompareTo(b.Hanging));
        GD.Print($"\n  the combinations that could not: {worst.Count}");
        for (int i = 0; i < Math.Min(worst.Count, 20); i++)
            GD.Print($"    {worst[i].Combo,-34} four on {worst[i].Four}/{FeasibilitySeeds} seeds,"
                + $" {worst[i].Gates:0.0} gates of which {worst[i].Hanging:0.0} hanging");
    }

    /// <summary>The placement funnel per character: cells surviving each of GatePlacement.Funnel's tests, strict and loose rungs.</summary>
    private void PrintGateFunnel()
    {
        GD.Print($"\n  why a coast will not take a hanging gate, per character"
            + $"  ({FeasibilitySeeds} seeds, all four edges)");
        GD.Print($"  {"character",-14} {"rung",7} {"usable",8} {"on its edge",12} "
            + $"{"has strip",10} {"flyable",9}   (cells surviving each test)");

        foreach (TerrainCharacter what in Enum.GetValues<TerrainCharacter>())
        {
            if (what == TerrainCharacter.Auto) continue;
            IslandParams p = Variant(q =>
            {
                q.Character = what;
                q.EntryGate = GateKind.Hanging;
                q.ExitGate = GateKind.Hanging;
                q.ExitGates = 3;
            });

            List<IslandData> data = Sweep(p, FeasibilitySeeds).ToList();

            (string Label, bool Loose)[] rungs = { ("strict", false), ("loose", true) };
            foreach (var (label, loose) in rungs)
            {
                long usable = 0, fits = 0, strip = 0, flyable = 0;
                int edgesOffering = 0;
                foreach (IslandData d in data)
                foreach (Cardinal edge in Enum.GetValues<Cardinal>())
                {
                    var (u, f, s, y) = GatePlacement.Funnel(d, edge, loose);
                    usable += u; fits += f; strip += s; flyable += y;
                    if (y > 0) edgesOffering++;
                }
                float runs = FeasibilitySeeds;
                GD.Print($"  {(label == "strict" ? what.ToString() : ""),-14} {label,7} "
                    + $"{usable / runs,8:0} {fits / runs,12:0} "
                    + $"{strip / runs,10:0} {flyable / runs,9:0} "
                    + $"{edgesOffering / runs,8:0.0} of 4 edges");
            }
        }
    }

    /// <summary>Asking for less than the maximum, on Auto x Auto: count met, kind met, median got.</summary>
    private void PrintGateReductions()
    {
        GD.Print("\n  reductions, on Auto x Auto — asking for less has to work too");
        GD.Print($"  {"asked",-30} {"count met",11} {"kind met",10} {"median got",11}");
        (string Label, GateKind Entry, GateKind Exit, int Count)[] cases =
        {
            ("entry hanging + 3 hanging", GateKind.Hanging, GateKind.Hanging, 3),
            ("entry hanging + 2 hanging", GateKind.Hanging, GateKind.Hanging, 2),
            ("entry hanging + 1 hanging", GateKind.Hanging, GateKind.Hanging, 1),
            ("entry land + 3 land", GateKind.Land, GateKind.Land, 3),
            ("entry land + 2 land", GateKind.Land, GateKind.Land, 2),
            ("entry land + 1 land", GateKind.Land, GateKind.Land, 1),
            ("entry land + 3 hanging", GateKind.Land, GateKind.Hanging, 3),
            ("entry hanging + 3 land", GateKind.Hanging, GateKind.Land, 3),
        };
        foreach (var (label, entry, exit, count) in cases)
        {
            IslandParams p = Variant(q => { q.EntryGate = entry; q.ExitGate = exit; q.ExitGates = count; });

            int met = 0, kind = 0;
            var got = new List<int>();
            foreach (IslandData d in Sweep(p, SweepSeeds))
            {
                var (exits, wrong) = CountExits(d, entry, exit);
                got.Add(exits);
                if (exits >= count) met++;
                if (wrong == 0) kind++;
            }
            got.Sort();
            GD.Print($"  {label,-30} {Pct(met, SweepSeeds),11} {Pct(kind, SweepSeeds),10} "
                + $"{got[got.Count / 2],11}"
                + (met < SweepSeeds || kind < SweepSeeds ? "   <-- look" : ""));
        }
    }

    /// <summary>
    /// Every arrangement at 64² / 96² / 128², hardest-pressed first: att is the mean attempts,
    /// short the islands under the masses the shape names, unmet the seeds that shipped broken.
    /// </summary>
    private void PrintStrain()
    {
        GD.Print($"\n=== strain at the small footprints ({SweepSeeds} seeds each; "
            + "att = attempts, short = islands under the masses the shape names) ===");
        GD.Print($"  {"arrangement",-14} {"64:att",7} {"unmet",6} {"short",6} "
            + $"{"96:att",7} {"unmet",6} {"short",6} {"128:att",8} {"unmet",6} {"short",6}");

        var rows = new List<(string Name, float Att64, string Cells)>();
        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            int wanted = MassesTheShapeNames(how);
            var bits = new List<string>();
            float att64 = 0;

            foreach (int size in new[] { 64, 96, 128 })
            {
                IslandParams p = Variant(q => { q.Arrangement = how; q.Size = size; });

                float attempts = 0;
                int unmet = 0, shortfall = 0;
                foreach (IslandData d in Sweep(p, SweepSeeds))
                {
                    attempts += d.Attempts;
                    if (d.Unmet.Length > 0) unmet++;
                    int masses = LabelLandmasses(d, d.Size, new int[d.Size, d.Size]);
                    if (masses < wanted) shortfall++;
                }
                float att = attempts / SweepSeeds;
                if (size == 64) att64 = att;
                bits.Add($"{att,7:0.00} {unmet,6} {shortfall,6}");
            }
            rows.Add((how.ToString(), att64, string.Join(" ", bits)));
        }

        rows.Sort((a, b) => b.Att64.CompareTo(a.Att64));
        foreach (var r in rows) GD.Print($"  {r.Name,-14} {r.Cells}");
    }

    /// <summary>
    /// The debutants at every footprint over SweepSeeds (land% of the footprint, heart%
    /// over dry cells, water and box faults), then against every character at the preset size.
    /// </summary>
    private void PrintDebut()
    {
        GD.Print($"\n=== the debutants at every footprint ({SweepSeeds} seeds each) ===");
        GD.Print($"  {"arrangement",-10} {"size",4} {"attempts",8} {"unmet",6} {"land%",6} "
            + $"{"masses",7} {"heart%",7} {"waterFault",11} {"outBox",7} {"ms",5}");

        foreach (IslandArrangement how in Debutants)
        {
            foreach (int size in IslandParams.SupportedSizes)
            {
                IslandParams p = Variant(q => { q.Arrangement = how; q.Size = size; });

                float attempts = 0, masses = 0;
                int unmet = 0, waterFault = 0, outBox = 0;
                long landCells = 0;
                double heartShare = 0;
                ulong t0 = Time.GetTicksMsec();

                foreach (IslandData d in Sweep(p, SweepSeeds))
                {
                    int n = d.Size;
                    attempts += d.Attempts;
                    if (d.Unmet.Length > 0) unmet++;
                    masses += LabelLandmasses(d, n, new int[n, n]);

                    long land = 0, heart = 0;
                    for (int x = 0; x < n; x++)
                    for (int z = 0; z < n; z++)
                    {
                        if (!d.HasLand(x, z)) continue;
                        landCells++;
                        short w = d.WaterLevel[x, z];
                        if (w == IslandData.NoLand)
                        {
                            land++;
                            if (d.Heartland >= 0 && d.Reach[x, z] == d.Heartland) heart++;
                        }
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + Dx[k], nz = z + Dz[k];
                            if (!InBounds(n, nx, nz)) continue;
                            if (!d.HasLand(nx, nz) || w == IslandData.NoLand) continue;
                            if (d.WaterLevel[nx, nz] == IslandData.NoLand
                                && d.SurfaceLevel(nx, nz) < w) waterFault++;
                            if (d.WaterLevel[nx, nz] != IslandData.NoLand
                                && d.Fluid[nx, nz] != d.Fluid[x, z]) waterFault++;
                        }
                    }
                    if (land > 0) heartShare += 100.0 * heart / land;

                    foreach (Gate g in d.Gates)
                        if (OutOfBox(g, n)) outBox++;
                }

                float ms = (Time.GetTicksMsec() - t0) / (float)SweepSeeds;
                GD.Print($"  {how,-10} {size,4} {attempts / SweepSeeds,8:0.00} {unmet,6} "
                    + $"{100.0 * landCells / SweepSeeds / (size * (double)size),6:0.0} "
                    + $"{masses / SweepSeeds,7:0.0} {heartShare / SweepSeeds,7:0.0} "
                    + $"{waterFault,11} {outBox,7} {ms,5:0}");
            }
        }

        // A shape that only works on plains fails the moment the world-tree names a biome.
        GD.Print("\n  against every character, 128², 3 seeds each: attempts, ! = unmet");
        foreach (IslandArrangement how in Debutants)
        {
            var bits = new List<string>();
            foreach (TerrainCharacter c in Enum.GetValues<TerrainCharacter>())
            {
                if (c == TerrainCharacter.Auto) continue;
                IslandParams p = Variant(q => { q.Arrangement = how; q.Character = c; });

                float att = 0;
                bool bad = false;
                foreach (IslandData d in Sweep(p, 3))
                {
                    att += d.Attempts;
                    bad |= d.Unmet.Length > 0;
                }
                bits.Add($"{c.ToString()[..2]} {att / 3f:0.0}{(bad ? "!" : " ")}");
            }
            GD.Print($"  {how,-10} {string.Join("  ", bits)}");
        }
    }

    /// <summary>Land per arrangement over SweepSeeds each, thinnest first: share, cells, masses and bounding extent.</summary>
    private void PrintBulk()
    {
        GD.Print($"\n=== land per arrangement ({SweepSeeds} seeds each, "
            + $"footprint {Params.Size}²) ===");

        var rows = new List<(string Name, float Share, float Cells, float Masses,
                             float Extent)>();
        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            IslandParams p = Variant(q => q.Arrangement = how);

            long cells = 0;
            float masses = 0, extent = 0;
            foreach (IslandData d in Sweep(p, SweepSeeds))
            {
                int n = d.Size;
                int xLo = n, xHi = -1, zLo = n, zHi = -1;
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z)) continue;
                    cells++;
                    if (x < xLo) xLo = x;
                    if (x > xHi) xHi = x;
                    if (z < zLo) zLo = z;
                    if (z > zHi) zHi = z;
                }
                if (xHi >= 0)
                    extent += 100f * (xHi - xLo + 1) * (zHi - zLo + 1) / (n * (float)n);
                masses += LabelLandmasses(d, n, new int[n, n]);
            }
            float mean = cells / (float)SweepSeeds;
            rows.Add((how.ToString(), 100f * mean / (Params.Size * Params.Size), mean,
                      masses / SweepSeeds, extent / SweepSeeds));
        }

        rows.Sort((a, b) => a.Share.CompareTo(b.Share));
        GD.Print($"  {"arrangement",-14} {"land%",6} {"cells",9} {"masses",7} {"extent%",8}"
            + "   (thinnest first; extent wants 55-85)");
        foreach (var r in rows)
            GD.Print($"  {r.Name,-14} {r.Share,6:0.0} {r.Cells,9:0} {r.Masses,7:0.0}"
                + $" {r.Extent,8:0.0}");
    }

    /// <summary>
    /// The guarantee set at every supported footprint over SweepSeeds each: re-rolls,
    /// connectivity over dry cells, Gate roles and box, rivers reaching the rim, water
    /// physics, sealed gorges and the altitude cap.
    /// </summary>
    private void PrintSizes()
    {
        GD.Print($"\n=== the guarantee set at every footprint ({SweepSeeds} seeds each) ===");
        // snow% and snowy: the share of land under snow, and how many islands with a
        // mountain carry any — the lapse is meant to reach the snow at every footprint.
        GD.Print($"  {"size",4} {"attempts",8} {"unmet",6} {"main%",6} {"heart%",7} "
            + $"{"gateFault",10} {"outBox",7} {"rimMiss",8} {"waterFault",11} "
            + $"{"sealed",7} {"altMax",7} {"altOver",8} {"snow%",6} {"snowy",9} {"ms",6}");

        foreach (int size in IslandParams.SupportedSizes)
        {
            IslandParams p = Variant(q => q.Size = size);

            float attempts = 0;
            int unmet = 0, gateFault = 0, outBox = 0, rimMiss = 0, waterFault = 0;
            int sealedGorges = 0, altMax = 0, altOver = 0;
            double mainShare = 0, heartShare = 0;
            long snowCells = 0, allLand = 0;
            int mountainous = 0, snowy = 0;
            ulong t0 = Time.GetTicksMsec();

            foreach (IslandData d in Sweep(p, SweepSeeds))
            {
                int n = d.Size;
                attempts += d.Attempts;
                if (d.Unmet.Length > 0) unmet++;

                long land = 0, main = 0, heart = 0, snowHere = 0;
                bool hasRiver = false, reachedRim = false, hasMountain = false;
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z)) continue;
                    allLand++;
                    if (d.Material[x, z] == (byte)SurfaceMaterial.Snow) snowHere++;
                    if ((LandformType)d.Landform[x, z] == LandformType.Mountain) hasMountain = true;
                    short w = d.WaterLevel[x, z];
                    if (w == IslandData.NoLand)
                    {
                        land++;
                        if (d.Mainland >= 0 && d.Walk[x, z] == d.Mainland) main++;
                        if (d.Heartland >= 0 && d.Reach[x, z] == d.Heartland) heart++;
                    }

                    if (d.River[x, z]) hasRiver = true;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        bool off = !InBounds(n, nx, nz)
                                   || !d.HasLand(nx, nz);
                        if (d.River[x, z] && off) reachedRim = true;
                        if (w == IslandData.NoLand || off) continue;
                        // A dry neighbour under this water is a leak; a neighbouring fluid of another kind is a mix.
                        if (d.WaterLevel[nx, nz] == IslandData.NoLand
                            && d.SurfaceLevel(nx, nz) < w) waterFault++;
                        if (d.WaterLevel[nx, nz] != IslandData.NoLand
                            && d.Fluid[nx, nz] != d.Fluid[x, z]) waterFault++;
                    }
                }
                if (hasRiver && !reachedRim) rimMiss++;
                snowCells += snowHere;
                if (hasMountain) mountainous++;
                if (hasMountain && snowHere > 0) snowy++;
                if (land > 0)
                {
                    mainShare += 100.0 * main / land;
                    heartShare += 100.0 * heart / land;
                }

                int entries = 0, exits = 0;
                foreach (Gate g in d.Gates)
                {
                    if (g.Role == GateRole.Entry) entries++; else exits++;
                    if (OutOfBox(g, n)) outBox++;
                }
                if (entries != 1 || exits < 1 || exits > 3) gateFault++;

                sealedGorges += AnalyseGorges(d).Sealed;

                var (peak, bilge) = CubeLid(d);
                if (peak > short.MinValue)
                {
                    altMax = Math.Max(altMax, peak - bilge);
                    if (peak - bilge > n) altOver++;
                }
            }

            float ms = (Time.GetTicksMsec() - t0) / (float)SweepSeeds;
            string snowyOf = $"{snowy} of {mountainous}";
            GD.Print($"  {size,4} {attempts / SweepSeeds,8:0.00} {unmet,6} "
                + $"{mainShare / SweepSeeds,6:0.0} {heartShare / SweepSeeds,7:0.0} "
                + $"{gateFault,10} {outBox,7} {rimMiss,8} {waterFault,11} "
                + $"{sealedGorges,7} {altMax,7} {altOver,8} "
                + $"{100.0 * snowCells / Math.Max(1, allLand),6:0.0} {snowyOf,9} {ms,6:0}");
        }
    }

    /// <summary>
    /// Each water knob stepped from 0 to 1 with everything else held at the preset, so a
    /// slider whose column does not climb is caught; Crossings at each BridgeEase.
    /// </summary>
    private void PrintKnobs()
    {
        GD.Print($"\n=== water knobs, swept 0 to 1 ({SweepSeeds} seeds each, "
            + "everything else held) ===");

        float[] steps = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        PrintLakesSweep(steps);
        PrintRiversSweep(steps);
        PrintCrossingsSweep();
        PrintValleysSweep(steps);
        PrintWindSweep(steps);
    }

    /// <summary>
    /// Wind 0..1: what exposure moves. On flat ground (rugged under 64), mean
    /// moisture and warmth in the lee (exposure under 128) against the open (224 and
    /// over): the rain shadow and the milder lee. On sheltered broken ground (rugged
    /// 128 and over): the gorge damp. Then the marsh and bog shares, which read the
    /// moisture. At 0 the lee and the open should agree but for the sun and the
    /// water; at 1 the flat lee should be markedly drier and milder and the gorge floors wetter.
    /// </summary>
    private void PrintWindSweep(float[] steps)
    {
        GD.Print("  wind   flat lee moist  flat open moist   gorge moist   flat lee warm  flat open warm   marsh%   bog%");
        foreach (float v in steps)
        {
            IslandParams p = Variant(q => q.Wind = v);
            long leeM = 0, leeW = 0, lee = 0, openM = 0, openW = 0, open = 0, gorgeM = 0, gorge = 0;
            long land = 0, marsh = 0, bog = 0;
            foreach (IslandData d in Sweep(p, SweepSeeds))
                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (!d.HasLand(x, z)) continue;
                    land++;
                    if (d.Material[x, z] == (byte)SurfaceMaterial.Marsh) marsh++;
                    if (d.Material[x, z] == (byte)SurfaceMaterial.Bog) bog++;
                    bool flat = d.Ruggedness[x, z] < 64;
                    if (d.Exposure[x, z] < 128 && flat) { leeM += d.Moisture[x, z]; leeW += d.Warmth[x, z]; lee++; }
                    else if (d.Exposure[x, z] < 128 && d.Ruggedness[x, z] >= 128) { gorgeM += d.Moisture[x, z]; gorge++; }
                    else if (d.Exposure[x, z] >= 224 && flat) { openM += d.Moisture[x, z]; openW += d.Warmth[x, z]; open++; }
                }
            GD.Print($"  {v,4:0.00} {(lee > 0 ? leeM / (double)lee : 0),15:0.0} {(open > 0 ? openM / (double)open : 0),16:0.0} "
                + $"{(gorge > 0 ? gorgeM / (double)gorge : 0),13:0.0} "
                + $"{(lee > 0 ? leeW / (double)lee : 0),14:0.0} {(open > 0 ? openW / (double)open : 0),15:0.0} "
                + $"{100.0 * marsh / Math.Max(1, land),8:0.00} {100.0 * bog / Math.Max(1, land),6:0.00}");
        }
    }

    /// <summary>
    /// The four climate corners and the preset, as material shares: what the
    /// ladder makes of dry cold, dry warm, wet cold and wet warm country.
    /// </summary>
    private void PrintClimate()
    {
        GD.Print($"\n=== the climate grid: material shares over {SweepSeeds} seeds each ===");
        var corners = new List<(string Name, float Moisture, float Warmth)>();
        foreach (var (warmName, warmth) in new[] { ("cold", 0.15f), ("temperate", 0.5f), ("hot", 0.85f) })
        foreach (var (wetName, moisture) in new[] { ("dry", 0.15f), ("balanced", 0.45f), ("wet", 0.75f) })
            corners.Add(($"{warmName} {wetName}", moisture, warmth));
        corners.Add(("sand end", 0.45f, 1f));
        corners.Add(("snow end", 0.45f, 0f));
        if (Params.Moisture >= 0f && Params.Warmth >= 0f)
            corners.Add(("preset", Params.Moisture, Params.Warmth));
        else GD.Print("  (the preset rolls moisture and warmth per seed, so it has no corner of its own)");
        foreach (var (name, moisture, warmth) in corners)
        {
            IslandParams p = Variant(q => { q.Moisture = moisture; q.Warmth = warmth; });
            var cells = new long[Enum.GetValues<SurfaceMaterial>().Length];
            long land = 0;
            foreach (IslandData d in Sweep(p, SweepSeeds))
                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (!d.HasLand(x, z)) continue;
                    cells[d.Material[x, z]]++;
                    land++;
                }

            var parts = new List<(string Name, long Cells)>();
            foreach (SurfaceMaterial m in Enum.GetValues<SurfaceMaterial>())
                parts.Add((m.ToString().ToLowerInvariant(), cells[(int)m]));
            parts.Sort((a, b) => b.Cells.CompareTo(a.Cells));
            var bits = new List<string>();
            foreach (var (material, count) in parts)
                if (count > 0) bits.Add($"{material} {100.0 * count / Math.Max(1, land):0.0}%");
            GD.Print($"  {name,-18} (moisture {moisture:0.00}, warmth {warmth:0.00}): {string.Join(", ", bits)}");
        }
    }

    /// <summary>Lakes 0..1: water-only lake cells, bodies as distinct regions, the biggest.</summary>
    private void PrintLakesSweep(float[] steps)
    {
        GD.Print($"\n  {"lakes",6} {"lake cells",11} {"lakes",7} {"biggest",8}   "
            + "(area, not just how many)");
        foreach (float v in steps)
        {
            IslandParams p = Variant(q => q.Lakes = v);
            long cells = 0, bodies = 0, biggest = 0;
            foreach (IslandData d in Sweep(p, SweepSeeds))
            {
                var perRegion = new Dictionary<int, int>();
                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (d.WaterLevel[x, z] == IslandData.NoLand || d.River[x, z]) continue;
                    if (d.Fluid[x, z] != (byte)FluidKind.Water) continue;   // goo ignores this knob
                    cells++;
                    int r = d.Region[x, z];
                    perRegion[r] = perRegion.GetValueOrDefault(r) + 1;
                }
                bodies += perRegion.Count;
                foreach (int area in perRegion.Values) biggest = Math.Max(biggest, area);
            }
            GD.Print($"  {v,6:0.00} {cells / (float)SweepSeeds,11:0.0} "
                + $"{bodies / (float)SweepSeeds,7:0.0} {biggest,8}");
        }
    }

    /// <summary>Rivers 0..1: river cells, navigable cells, falls.</summary>
    private void PrintRiversSweep(float[] steps)
    {
        GD.Print($"\n  {"rivers",6} {"river cells",12} {"navigable",10} {"falls",7}");
        foreach (float v in steps)
        {
            IslandParams p = Variant(q => q.Rivers = v);
            long cells = 0, navigable = 0, falls = 0;
            foreach (IslandData d in Sweep(p, SweepSeeds))
            {
                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (!d.River[x, z]) continue;
                    cells++;
                    if (d.Navigable[x, z]) navigable++;
                }
                falls += d.Falls.Count;
            }
            GD.Print($"  {v,6:0.00} {cells / (float)SweepSeeds,12:0.0} "
                + $"{navigable / (float)SweepSeeds,10:0.0} {falls / (float)SweepSeeds,7:0.0}");
        }
    }

    /// <summary>The gorge question at every bridge span; Easy spans one cell, so a two-cell channel is a wall there by rule.</summary>
    private void PrintCrossingsSweep()
    {
        GD.Print($"\n  {"crossings",-10} {"reaches",8} {"sealed",7} {"misaligned",11} "
            + $"{"worst walk",11}");
        foreach (BridgeEase ease in new[] { BridgeEase.Easy, BridgeEase.Medium, BridgeEase.Hard })
        {
            IslandParams p = Variant(q => q.Crossings = ease);
            int sealedUp = 0, skew = 0, reaches = 0;
            var walks = new List<int>();
            foreach (IslandData d in Sweep(p, SweepSeeds))
            {
                GorgeStats g = AnalyseGorges(d);
                reaches += g.Reaches;
                sealedUp += g.Sealed;
                skew += g.Skew;
                walks.AddRange(g.Detours);
            }
            int worst = 0;
            foreach (int w in walks) worst = Math.Max(worst, w);
            GD.Print($"  {ease,-10} {reaches,8} {sealedUp,7} {skew,11} {worst,11}");
        }
    }

    /// <summary>
    /// Valleys 0..1, per river rather than per island since the knob slides a window across
    /// the courses: the rise, how many courses have a valley, the deepest, and what it costs
    /// in two-slab steps and walkability.
    /// </summary>
    private void PrintValleysSweep(float[] steps)
    {
        GD.Print($"\n  {"valleys",7} {"rise 1->5",10} {"valleyed",12} {"deepest",8} "
            + $"{"2-slab",7} {"walk%",7} {"berths",7}");
        foreach (float v in steps)
        {
            IslandParams p = Variant(q => q.Valleys = v);
            double total = 0;
            int counted = 0;
            long steep = 0, berths = 0, walk = 0, dry = 0;
            var each = new List<double>();
            foreach (IslandData d in Sweep(p, SweepSeeds))
            {
                if (ValleyRise(d, out double rise, each)) { total += rise; counted++; }
                berths += d.Berths.Count;

                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                    dry++;
                    if (d.Mainland >= 0 && d.Walk[x, z] == d.Mainland) walk++;
                    for (int k = 0; k < 4; k += 2)     // +X and +Z once each: every pair counted once
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        if (!d.HasLand(nx, nz)) continue;
                        if (Math.Abs(d.SurfaceLevel(x, z) - d.SurfaceLevel(nx, nz)) == 2) steep++;
                    }
                }
            }
            // The row at 0.00 is the control: read the column as a rise above it, not as an absolute.
            int withValley = 0;
            double deepest = 0;
            foreach (double one in each)
            {
                if (one >= 1.0) withValley++;
                deepest = Math.Max(deepest, one);
            }

            GD.Print($"  {v,7:0.00} {(counted > 0 ? total / counted : 0),10:0.00} "
                + $"{$"{withValley}/{each.Count}",12} {deepest,8:0.0} "
                + $"{steep / (float)SweepSeeds,7:0.0} {(dry > 0 ? 100.0 * walk / dry : 0),7:0.0} "
                + $"{berths / (float)SweepSeeds,7:0.0}");
        }
    }
}
