using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

public partial class GenerationAudit
{
    /// <summary>
    /// Every combination of arrangement and character, a few seeds each: which
    /// ones the pipeline finds hard.
    ///
    /// <para>The ordinary audit rolls sixty islands from <c>Auto</c>, so it
    /// measures the combinations the weights happen to produce and says nothing
    /// about the ones they rarely do. But an <c>Auto</c> Domain is not how the
    /// game will ask for islands — a Domain's biome and world-tree position will
    /// name both, and if <c>Atoll</c> + <c>Karst</c> takes four attempts and comes
    /// out unplayable, that is a bug nobody would ever see from the summary.</para>
    ///
    /// <para>What it looks for: <b>re-rolls</b> (the generator rejecting its own
    /// output), <b>unmet guarantees</b> (it giving up), and the reachable share
    /// (whether the island is one place). A combination that averages more than
    /// one attempt is one the pipeline is fighting.</para>
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

                var p = (IslandParams)Params.Duplicate();
                p.Arrangement = how;
                p.Character = what;

                int attempts = 0, unmet = 0;
                float reach = 0f, masses = 0f;
                ulong t0 = Time.GetTicksMsec();

                for (int i = 0; i < FeasibilitySeeds; i++)
                {
                    IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
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
    /// Does the Domain come out the way it was asked for? One row per Gate
    /// request: every Entry edge crossed with every Entry kind, then every Exit
    /// count crossed with every Exit kind.
    ///
    /// These are the only parameters set by something outside the Domain — the
    /// world-tree decides which edge you arrive on and which kind of Gate you
    /// arrive through — so they are the only ones where "it usually works" is a
    /// bug report rather than a result.
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
            var p = (IslandParams)Params.Duplicate();
            p.EntryEdge = edge;
            p.EntryGate = kind;

            int rightEdge = 0, rightKind = 0, both = 0, attempts = 0;
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
                attempts += d.Attempts;
                foreach (Gate g in d.Gates)
                {
                    if (g.Role != GateRole.Entry) continue;
                    bool e = (int)g.Facing == (int)edge - 1;
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
            var p = (IslandParams)Params.Duplicate();
            p.ExitGates = count;
            p.ExitGate = kind;

            int met = 0, allKind = 0;
            var got = new List<int>();
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
                int exits = 0, wrong = 0;
                foreach (Gate g in d.Gates)
                {
                    if (g.Role != GateRole.Exit) continue;
                    exits++;
                    if (kind != GateKind.Auto && g.Kind != kind) wrong++;
                }
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
    /// The hardest Gate request there is, against every shape the generator can
    /// build: <b>four hanging Gates</b> — an Entry and three Exits, one per edge,
    /// every one of them flown to.
    ///
    /// <para>It is the right thing to test because it is the maximum. A hanging
    /// Gate needs a coast that will give it a 3 × 5 landing strip with clear air
    /// off the rim, on the right side of the island, a third of the footprint from
    /// every other Gate — four times over, once per edge. Anything that can do
    /// that can do fewer Gates and can do land Gates, which need a forecourt but
    /// no flight path.</para>
    ///
    /// <para>"Can do fewer" is a claim and not a fact, though, so the reductions
    /// are measured too rather than assumed: three, two and one Exit, and land
    /// Gates at both ends. A rule that only fires when the pool of edges is full
    /// would pass the maximum and fail the middle.</para>
    /// </summary>
    private void PrintGateMatrix()
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

                var p = (IslandParams)Params.Duplicate();
                p.Arrangement = how;
                p.Character = what;
                p.EntryGate = GateKind.Hanging;
                p.ExitGate = GateKind.Hanging;
                p.ExitGates = 3;

                int four = 0, gates = 0, hanging = 0;
                for (int i = 0; i < FeasibilitySeeds; i++)
                {
                    IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
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

        // ---- and where the hanging Gates actually die --------------------------
        // Four tests stand between a coast cell and a hanging Gate. Knowing how
        // many Gates were placed does not say which test refused, so the funnel is
        // counted per character: usable ground, then the edge rules, then a
        // landing strip, then a flight path to it.
        GD.Print($"\n  why a coast will not take a hanging gate, per character"
            + $"  ({FeasibilitySeeds} seeds, all four edges)");
        GD.Print($"  {"character",-14} {"rung",7} {"usable",8} {"on its edge",12} "
            + $"{"has strip",10} {"flyable",9}   (cells surviving each test)");

        foreach (TerrainCharacter what in Enum.GetValues<TerrainCharacter>())
        {
            if (what == TerrainCharacter.Auto) continue;
            var p = (IslandParams)Params.Duplicate();
            p.Character = what;
            p.EntryGate = GateKind.Hanging;
            p.ExitGate = GateKind.Hanging;
            p.ExitGates = 3;

            var data = new List<IslandData>();
            for (int i = 0; i < FeasibilitySeeds; i++)
                data.Add(IslandGenerator.Generate(FirstSeed + i * 6151, p));

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

        // ---- and the reductions, which are not free just because the maximum is -
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
            var p = (IslandParams)Params.Duplicate();
            p.EntryGate = entry;
            p.ExitGate = exit;
            p.ExitGates = count;

            int met = 0, kind = 0;
            var got = new List<int>();
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
                int exits = 0, wrong = 0;
                foreach (Gate g in d.Gates)
                {
                    if (g.Role == GateRole.Entry) { if (g.Kind != entry) wrong++; continue; }
                    exits++;
                    if (g.Kind != exit) wrong++;
                }
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
    /// What each water knob is worth, stepped from 0 to 1 with everything else
    /// held at the preset. A slider whose column does not climb is a slider that
    /// does nothing, and that is a thing a summary at one setting cannot say.
    /// </summary>
    /// <summary>
    /// Where the small footprints hurt, per arrangement. Attempts are the
    /// generator fighting its own guarantees; a masses shortfall is a layout
    /// that could not stay the shape it names; unmet is a seed that shipped
    /// broken anyway. Any of the three clustering on one arrangement at one
    /// size marks it for the future size gate.
    /// </summary>
    private void PrintStrain()
    {
        GD.Print($"\n=== strain at the small footprints ({SweepSeeds} seeds each; "
            + "att = attempts, short = islands under the masses the shape names) ===");
        GD.Print($"  {"arrangement",-14} {"48:att",7} {"unmet",6} {"short",6} "
            + $"{"64:att",7} {"unmet",6} {"short",6} {"128:att",8} {"unmet",6} {"short",6}");

        var rows = new List<(string Name, float Att48, string Cells)>();
        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            int wanted = 1;
            var bits = new List<string>();
            float att48 = 0;

            foreach (int size in new[] { 48, 64, 128 })
            {
                var p = (IslandParams)Params.Duplicate();
                p.Arrangement = how;
                p.Size = size;

                float attempts = 0;
                int unmet = 0, shortfall = 0;
                for (int i = 0; i < SweepSeeds; i++)
                {
                    IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
                    attempts += d.Attempts;
                    if (d.Unmet.Length > 0) unmet++;
                    int masses = LabelLandmasses(d, d.Size, new int[d.Size, d.Size]);
                    wanted = MassesTheShapeNames(how);
                    if (masses < wanted) shortfall++;
                }
                float att = attempts / SweepSeeds;
                if (size == 48) att48 = att;
                bits.Add($"{att,7:0.00} {unmet,6} {shortfall,6}");
            }
            rows.Add((how.ToString(), att48, string.Join(" ", bits)));
        }

        rows.Sort((a, b) => b.Att48.CompareTo(a.Att48));
        foreach (var r in rows) GD.Print($"  {r.Name,-14} {r.Cells}");
    }

    private void PrintDebut()
    {
        GD.Print($"\n=== the debutants at every footprint ({SweepSeeds} seeds each) ===");
        GD.Print($"  {"arrangement",-10} {"size",4} {"attempts",8} {"unmet",6} {"land%",6} "
            + $"{"masses",7} {"heart%",7} {"waterFault",11} {"outBox",7} {"ms",5}");

        foreach (IslandArrangement how in Debutants)
        {
            foreach (int size in IslandParams.SupportedSizes)
            {
                var p = (IslandParams)Params.Duplicate();
                p.Arrangement = how;
                p.Size = size;

                float attempts = 0, masses = 0;
                int unmet = 0, waterFault = 0, outBox = 0;
                long landCells = 0;
                double heartShare = 0;
                ulong t0 = Time.GetTicksMsec();

                for (int i = 0; i < SweepSeeds; i++)
                {
                    IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
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
                        if (g.Center.X < 0 || g.Center.Z < 0
                            || g.Center.X >= n || g.Center.Z >= n) outBox++;
                }

                float ms = (Time.GetTicksMsec() - t0) / (float)SweepSeeds;
                GD.Print($"  {how,-10} {size,4} {attempts / SweepSeeds,8:0.00} {unmet,6} "
                    + $"{100.0 * landCells / SweepSeeds / (size * (double)size),6:0.0} "
                    + $"{masses / SweepSeeds,7:0.0} {heartShare / SweepSeeds,7:0.0} "
                    + $"{waterFault,11} {outBox,7} {ms,5:0}");
            }
        }

        // And against every character, since a shape that only works on plains
        // is a shape that fails the moment the world-tree names a biome.
        GD.Print("\n  against every character, 128², 3 seeds each: attempts, ! = unmet");
        foreach (IslandArrangement how in Debutants)
        {
            var bits = new List<string>();
            foreach (TerrainCharacter c in Enum.GetValues<TerrainCharacter>())
            {
                if (c == TerrainCharacter.Auto) continue;
                var p = (IslandParams)Params.Duplicate();
                p.Arrangement = how;
                p.Character = c;

                float att = 0;
                bool bad = false;
                for (int i = 0; i < 3; i++)
                {
                    IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
                    att += d.Attempts;
                    bad |= d.Unmet.Length > 0;
                }
                bits.Add($"{c.ToString()[..2]} {att / 3f:0.0}{(bad ? "!" : " ")}");
            }
            GD.Print($"  {how,-10} {string.Join("  ", bits)}");
        }
    }

    /// <summary>
    /// Land per arrangement, thinnest first. "The rings are too thin" is a claim
    /// about area, and the ordinary summary cannot test it: Auto's rolls give
    /// some arrangements one island in sixty. This forces each one in turn.
    /// </summary>
    private void PrintBulk()
    {
        GD.Print($"\n=== land per arrangement ({SweepSeeds} seeds each, "
            + $"footprint {Params.Size}²) ===");

        var rows = new List<(string Name, float Share, float Cells, float Masses,
                             float Extent)>();
        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            var p = (IslandParams)Params.Duplicate();
            p.Arrangement = how;

            long cells = 0;
            float masses = 0, extent = 0;
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
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
    /// The guarantee set at every supported footprint. Every constant tuned at
    /// 128 is a suspect at 64, and this is the table that convicts them: the
    /// re-roll verdicts (attempts, unmet), the connectivity shares, the Gate
    /// deliverables, the water physics and the gorge tripwire, per size.
    /// </summary>
    private void PrintSizes()
    {
        GD.Print($"\n=== the guarantee set at every footprint ({SweepSeeds} seeds each) ===");
        GD.Print($"  {"size",4} {"attempts",8} {"unmet",6} {"main%",6} {"heart%",7} "
            + $"{"gateFault",10} {"outBox",7} {"rimMiss",8} {"waterFault",11} "
            + $"{"sealed",7} {"altMax",7} {"altOver",8} {"ms",6}");

        foreach (int size in IslandParams.SupportedSizes)
        {
            var p = (IslandParams)Params.Duplicate();
            p.Size = size;

            float attempts = 0;
            int unmet = 0, gateFault = 0, outBox = 0, rimMiss = 0, waterFault = 0;
            int sealedGorges = 0, altMax = 0, altOver = 0;
            double mainShare = 0, heartShare = 0;
            ulong t0 = Time.GetTicksMsec();

            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
                int n = d.Size;
                attempts += d.Attempts;
                if (d.Unmet.Length > 0) unmet++;

                long land = 0, main = 0, heart = 0;
                bool hasRiver = false, reachedRim = false;
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z)) continue;
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
                        // A dry neighbour under this water is a leak; a
                        // neighbouring fluid of another kind is a mix. Both 0.
                        if (d.WaterLevel[nx, nz] == IslandData.NoLand
                            && d.SurfaceLevel(nx, nz) < w) waterFault++;
                        if (d.WaterLevel[nx, nz] != IslandData.NoLand
                            && d.Fluid[nx, nz] != d.Fluid[x, z]) waterFault++;
                    }
                }
                if (hasRiver && !reachedRim) rimMiss++;
                if (land > 0)
                {
                    mainShare += 100.0 * main / land;
                    heartShare += 100.0 * heart / land;
                }

                int entries = 0, exits = 0;
                foreach (Gate g in d.Gates)
                {
                    if (g.Role == GateRole.Entry) entries++; else exits++;
                    if (g.Center.X < 0 || g.Center.Z < 0
                        || g.Center.X >= n || g.Center.Z >= n) outBox++;
                }
                if (entries != 1 || exits < 1 || exits > 3) gateFault++;

                int gc = 0, cross = 0, sealedUp = 0, skew = 0;
                var scratch = new List<int>();
                AnalyseGorges(d, ref gc, scratch, scratch, scratch,
                              ref cross, ref sealedUp, ref skew);
                sealedGorges += sealedUp;

                short peak = short.MinValue, bilge = short.MaxValue;
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z)) continue;
                    if (d.Spans[x, z][^1].Top > peak) peak = d.Spans[x, z][^1].Top;
                    if (d.KeelLevel(x, z) < bilge) bilge = d.KeelLevel(x, z);
                }
                if (peak > short.MinValue)
                {
                    altMax = Math.Max(altMax, peak - bilge);
                    if (peak - bilge > n) altOver++;
                }
            }

            float ms = (Time.GetTicksMsec() - t0) / (float)SweepSeeds;
            GD.Print($"  {size,4} {attempts / SweepSeeds,8:0.00} {unmet,6} "
                + $"{mainShare / SweepSeeds,6:0.0} {heartShare / SweepSeeds,7:0.0} "
                + $"{gateFault,10} {outBox,7} {rimMiss,8} {waterFault,11} "
                + $"{sealedGorges,7} {altMax,7} {altOver,8} {ms,6:0}");
        }
    }

    private void PrintKnobs()
    {
        GD.Print($"\n=== water knobs, swept 0 to 1 ({SweepSeeds} seeds each, "
            + "everything else held) ===");

        float[] steps = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        GD.Print($"\n  {"lakes",6} {"lake cells",11} {"lakes",7} {"biggest",8}   "
            + "(area, not just how many)");
        foreach (float v in steps)
        {
            var p = (IslandParams)Params.Duplicate();
            p.Lakes = v;
            long cells = 0, bodies = 0, biggest = 0;
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
                var perRegion = new Dictionary<int, int>();
                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (d.WaterLevel[x, z] == IslandData.NoLand || d.River[x, z]) continue;
                    // Not the goo: it ignores this knob, being no kind of lake.
                    if (d.Fluid[x, z] != (byte)FluidKind.Water) continue;
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

        GD.Print($"\n  {"rivers",6} {"river cells",12} {"navigable",10} {"falls",7}");
        foreach (float v in steps)
        {
            var p = (IslandParams)Params.Duplicate();
            p.Rivers = v;
            long cells = 0, navigable = 0, falls = 0;
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
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

        // The gorge question at every bridge span. The preset's Medium answers
        // "how often can a walled river not be bridged" with zero — but Easy
        // Domains only span one cell, so a two-cell channel is a wall there by
        // rule, and misalignment is the one thing that can seal a stream gorge.
        // This is where the frustration would live if it lived anywhere.
        GD.Print($"\n  {"crossings",-10} {"reaches",8} {"sealed",7} {"misaligned",11} "
            + $"{"worst walk",11}");
        foreach (BridgeEase ease in new[] { BridgeEase.Easy, BridgeEase.Medium, BridgeEase.Hard })
        {
            var p = (IslandParams)Params.Duplicate();
            p.Crossings = ease;
            int cells = 0, cross = 0, sealedUp = 0, skew = 0, reaches = 0;
            var lens = new List<int>();
            var sealedLens = new List<int>();
            var walks = new List<int>();
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
                reaches += AnalyseGorges(d, ref cells, lens, sealedLens, walks,
                                         ref cross, ref sealedUp, ref skew);
            }
            int worst = 0;
            foreach (int w in walks) worst = Math.Max(worst, w);
            GD.Print($"  {ease,-10} {reaches,8} {sealedUp,7} {skew,11} {worst,11}");
        }

        // The rise is the valley; the rest is what deepening every channel on the
        // island costs. A knob that makes the terrain prettier and the Domain
        // unwalkable is not a knob that works.
        // Per river, not per island. `Valleys` slides a window across the courses,
        // so what matters is how many of them have a valley at all and how much
        // they differ — an island-wide mean would read the same whether every
        // river had half a valley or half the rivers had a whole one.
        GD.Print($"\n  {"valleys",7} {"rise 1->5",10} {"valleyed",12} {"deepest",8} "
            + $"{"2-slab",7} {"walk%",7} {"berths",7}");
        foreach (float v in steps)
        {
            var p = (IslandParams)Params.Duplicate();
            p.Valleys = v;
            double total = 0;
            int counted = 0;
            long steep = 0, berths = 0, walk = 0, dry = 0;
            var each = new List<double>();
            for (int i = 0; i < SweepSeeds; i++)
            {
                IslandData d = IslandGenerator.Generate(FirstSeed + i * 6151, p);
                if (ValleyRise(d, out double rise, each)) { total += rise; counted++; }
                berths += d.Berths.Count;

                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                    dry++;
                    if (d.Mainland >= 0 && d.Walk[x, z] == d.Mainland) walk++;
                    for (int k = 0; k < 2; k++)
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        if (!d.HasLand(nx, nz)) continue;
                        if (Math.Abs(d.SurfaceLevel(x, z) - d.SurfaceLevel(nx, nz)) == 2) steep++;
                    }
                }
            }
            // A course counts as having a valley when the ground gains a slab or
            // more over the five cells out from it. The row at 0.00 is the
            // control: a river runs in low ground anyway, so some courses clear
            // the bar on natural relief alone and the column is worth reading as
            // a rise above that row rather than as an absolute.
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
