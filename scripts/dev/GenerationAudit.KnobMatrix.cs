using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

/// <summary>
/// The knob matrix: every 0–1 knob stepped 0, ¼, ½, ¾, 1 over the same seeds, the
/// other knobs rolled by each seed as the preset rolls them, and one vector of
/// outcomes measured on every island. For each knob: does what it promises climb
/// with it, does anything reverse along the way, and what else moves. The comparison
/// is paired — each seed against itself at 0 — so the spread the rolled knobs put
/// between seeds cancels, and an effect a fraction of that spread is still seen.
/// </summary>
public partial class GenerationAudit
{
    private static readonly float[] MatrixSteps = { 0f, 0.25f, 0.5f, 0.75f, 1f };

    /// <summary>A knob to sweep, and the outcomes it promises to move.</summary>
    private sealed record MatrixKnob(string Name, Action<IslandParams, float> Set, string[] Own);

    private static readonly MatrixKnob[] MatrixKnobs =
    {
        new("mix", (p, v) => p.LandformMix = v, new[] { "high%" }),
        new("relief", (p, v) => p.Relief = v, new[] { "spread", "slope" }),
        new("hills", (p, v) => p.Hilliness = v, new[] { "hillrel" }),
        new("rivers", (p, v) => p.Rivers = v, new[] { "river", "navig" }),
        new("lakes", (p, v) => p.Lakes = v, new[] { "lake", "lakes" }),
        new("valleys", (p, v) => p.Valleys = v, new[] { "valley" }),
        new("moisture", (p, v) => p.Moisture = v, new[] { "moist", "wet%" }),
        new("warmth", (p, v) => p.Warmth = v, new[] { "warm", "snow%" }),
        new("wind", (p, v) => p.Wind = v, new[] { "leegap" }),
        new("overhangs", (p, v) => p.OverhangDensity = v, new[] { "overh" }),
        new("magick", (p, v) => p.MagickDensity = v, new[] { "magick" }),
        new("fjords", (p, v) => p.Fjords = v, new[] { "fjord" }),
    };

    /// <summary>The outcomes, in the order <see cref="MatrixMeasure"/> fills them.</summary>
    private static readonly string[] MatrixMetrics =
    {
        "land%", "high%", "spread", "slope", "cliff%", "2slab%", "hillrel",
        "river", "navig", "falls", "spring", "ford", "lake", "lakes", "valley",
        "moist", "warm", "wet%", "snow%", "leegap", "overh", "magick", "fjord",
        "main%", "heart%", "distr", "attempt",
    };

    /// <summary>An effect this many spreads-between-seeds or more is worth a word.</summary>
    private const double MatrixSpeaks = 0.25;

    /// <summary>
    /// One island's outcomes. Percentages are of the grid (land) or of land; spread
    /// is crest to lowest dry ground in slabs; slope is the mean step between
    /// neighbouring land cells, cliff and two-slab the share of such steps; hillrel
    /// the same mean step inside the Hills; the water counts are cells; valley is the
    /// audit's rise from one cell off a river to five; moist, warm and magick are
    /// mean bytes; fjord the cells the fjords took; leegap is flat open ground's moisture over the flat lee's; main
    /// and heart the walk and reach shares of dry land; distr the districts on the
    /// heartland; attempt how many islands the seed built.
    /// </summary>
    private static double[] MatrixMeasure(IslandData d)
    {
        int n = d.Size;
        double land = 0, high = 0, dry = 0, main = 0, heart = 0;
        double pairs = 0, slopeSum = 0, cliff = 0, two = 0, hillPairs = 0, hillSum = 0;
        int lowest = int.MaxValue, crest = int.MinValue;
        double river = 0, navig = 0, ford = 0, lake = 0, overh = 0, fjord = 0;
        double moist = 0, warm = 0, magick = 0, wet = 0, snow = 0;
        double leeM = 0, leeN = 0, openM = 0, openN = 0;
        var lakeRegions = new HashSet<int>();

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (d.Fjord[x, z]) fjord++;
            if (!d.HasLand(x, z)) continue;
            land++;
            var form = (LandformType)d.Landform[x, z];
            if (form is LandformType.Mountain or LandformType.Mesa or LandformType.Massif
                or LandformType.Karst or LandformType.Badlands) high++;
            if (d.Spans[x, z].Length > 1) overh++;
            moist += d.Moisture[x, z];
            warm += d.Warmth[x, z];
            magick += d.Magick[x, z];

            var m = (SurfaceMaterial)d.Material[x, z];
            if (m is SurfaceMaterial.Grass or SurfaceMaterial.Moorland or SurfaceMaterial.Verdure
                or SurfaceMaterial.Floodplain or SurfaceMaterial.Bog or SurfaceMaterial.Marsh) wet++;
            if (m == SurfaceMaterial.Snow) snow++;

            bool flat = d.Ruggedness[x, z] < 64;
            if (flat && d.Exposure[x, z] < 128) { leeM += d.Moisture[x, z]; leeN++; }
            else if (flat && d.Exposure[x, z] >= 224) { openM += d.Moisture[x, z]; openN++; }

            if (d.Ford[x, z]) ford++;
            short water = d.WaterLevel[x, z];
            if (water != IslandData.NoLand && water > d.SurfaceLevel(x, z))
            {
                if (d.River[x, z]) { river++; if (d.Navigable[x, z]) navig++; }
                else if (d.Fluid[x, z] == (byte)FluidKind.Water) { lake++; lakeRegions.Add(d.Region[x, z]); }
            }
            else
            {
                dry++;
                int s = d.SurfaceLevel(x, z);
                if (s < lowest) lowest = s;
                if (s > crest) crest = s;
                if (d.Mainland >= 0 && d.Walk[x, z] == d.Mainland) main++;
                if (d.Heartland >= 0 && d.Reach[x, z] == d.Heartland) heart++;
            }

            for (int k = 0; k < 4; k += 2)      // +X and +Z once each: every pair counted once
            {
                int nx = x + Dx[k], nz = z + Dz[k];
                if (!InBounds(n, nx, nz) || !d.HasLand(nx, nz)) continue;
                int step = Math.Abs(d.EffectiveLevel(x, z) - d.EffectiveLevel(nx, nz));
                pairs++;
                slopeSum += step;
                if (step >= 3) cliff++;
                if (step == 2) two++;
                if (form == LandformType.Hills && (LandformType)d.Landform[nx, nz] == LandformType.Hills)
                {
                    hillPairs++;
                    hillSum += step;
                }
            }
        }

        if (!ValleyRise(d, out double valley)) valley = 0;
        int districts = 0;
        foreach (WalkArea a in d.Areas)
            if (a.IsDistrict && d.Heartland >= 0 && d.Reach[a.Seat.X, a.Seat.Y] == d.Heartland) districts++;

        double Pct(double part, double whole) => whole > 0 ? 100.0 * part / whole : 0;
        return new[]
        {
            Pct(land, (double)n * n), Pct(high, land), land > 0 ? crest - lowest : 0,
            pairs > 0 ? slopeSum / pairs : 0, Pct(cliff, pairs), Pct(two, pairs),
            hillPairs > 0 ? hillSum / hillPairs : 0,
            river, navig, d.Falls.Count, d.Springs.Count, ford, lake, lakeRegions.Count, valley,
            land > 0 ? moist / land : 0, land > 0 ? warm / land : 0, Pct(wet, land), Pct(snow, land),
            leeN > 0 && openN > 0 ? openM / openN - leeM / leeN : 0,
            overh, land > 0 ? magick / land : 0, fjord,
            Pct(main, dry), Pct(heart, dry), districts, d.Attempts,
        };
    }

    private void PrintKnobMatrix()
    {
        int seeds = SweepSeeds;
        int knobs = MatrixKnobs.Length, steps = MatrixSteps.Length, metrics = MatrixMetrics.Length;
        var value = new double[knobs, steps, seeds][];
        GD.Print($"\n=== the knob matrix: every knob at 0, ¼, ½, ¾, 1 over {seeds} seeds at {Params.Size}², "
            + "paired against the same seed at 0, the other knobs rolled by the seed ===");
        ulong t0 = Time.GetTicksMsec();
        for (int k = 0; k < knobs; k++)
        for (int j = 0; j < steps; j++)
        {
            MatrixKnob knob = MatrixKnobs[k];
            float at = MatrixSteps[j];
            IslandParams p = Variant(q => knob.Set(q, at));
            for (int s = 0; s < seeds; s++)
                value[k, j, s] = MatrixMeasure(IslandGenerator.Generate(SeedAt(s), p));
        }
        GD.Print($"  {knobs * steps * seeds} islands in {(Time.GetTicksMsec() - t0) / 1000.0:0} s\n");

        // The spread between seeds of each outcome over the whole run: the unit an effect is read in.
        var spread = new double[metrics];
        for (int m = 0; m < metrics; m++)
        {
            double sum = 0, sq = 0;
            int count = 0;
            for (int k = 0; k < knobs; k++)
            for (int j = 0; j < steps; j++)
            for (int s = 0; s < seeds; s++)
            {
                double v = value[k, j, s][m];
                sum += v;
                sq += v * v;
                count++;
            }
            double mean = sum / count;
            spread[m] = Math.Sqrt(Math.Max(0, sq / count - mean * mean));
        }

        // Per knob and outcome: the paired mean change from 0 to 1 in spreads, whether it
        // is more than noise, and whether any step along the way runs the other way.
        var effect = new double[knobs, metrics];
        var reversal = new bool[knobs, metrics];
        var significant = new bool[knobs, metrics];
        for (int k = 0; k < knobs; k++)
        for (int m = 0; m < metrics; m++)
        {
            (double end, double endSe) = PairedChange(value, k, 0, steps - 1, m, seeds);
            effect[k, m] = spread[m] > 0 ? end / spread[m] : 0;
            significant[k, m] = Math.Abs(end) > 2 * endSe;
            int trend = significant[k, m] ? Math.Sign(end) : 0;
            for (int j = 1; j < steps; j++)
            {
                (double step, double se) = PairedChange(value, k, j - 1, j, m, seeds);
                if (Math.Abs(step) <= 2 * se) continue;
                if (trend == 0) trend = Math.Sign(step);           // a flat end with a real step: a hump or a dip
                else if (Math.Sign(step) != trend) reversal[k, m] = true;
            }
            if (!significant[k, m] && trend != 0) reversal[k, m] = true;
        }

        for (int k = 0; k < knobs; k++)
        {
            MatrixKnob knob = MatrixKnobs[k];
            GD.Print($"  {knob.Name}  (promises {string.Join(", ", knob.Own)})");
            var head = new StringBuilder("    at      ");
            foreach (string own in knob.Own) head.Append($"{own,10}");
            GD.Print(head.ToString());
            for (int j = 0; j < steps; j++)
            {
                var row = new StringBuilder($"    {MatrixSteps[j],4:0.00}    ");
                foreach (string own in knob.Own)
                {
                    int m = Array.IndexOf(MatrixMetrics, own);
                    double sum = 0;
                    for (int s = 0; s < seeds; s++) sum += value[k, j, s][m];
                    row.Append($"{sum / seeds,10:0.0}");
                }
                GD.Print(row.ToString());
            }

            var moves = new List<(int M, double D)>();
            for (int m = 0; m < metrics; m++)
                if (significant[k, m] && Math.Abs(effect[k, m]) >= MatrixSpeaks || reversal[k, m])
                    moves.Add((m, effect[k, m]));
            moves.Sort((a, b) => Math.Abs(b.D).CompareTo(Math.Abs(a.D)));
            var bits = new List<string>();
            foreach (var (m, dd) in moves)
            {
                string name = MatrixMetrics[m];
                bool own = Array.IndexOf(knob.Own, name) >= 0;
                string word = $"{name} {dd:+0.0;-0.0}" + (reversal[k, m] ? "!" : "");
                bits.Add(own ? $"[{word}]" : word);
            }
            GD.Print("    moves: " + (bits.Count > 0 ? string.Join("  ", bits) : "nothing")
                + "\n");
        }

        GD.Print("  the matrix: change from 0 to 1 in spreads between seeds; [own promise], ! a reversal "
            + $"along the way, · under {MatrixSpeaks:0.00} or within noise");
        var header = new StringBuilder($"  {"knob",-10}");
        foreach (string m in MatrixMetrics) header.Append($"{m,8}");
        GD.Print(header.ToString());
        for (int k = 0; k < knobs; k++)
        {
            var row = new StringBuilder($"  {MatrixKnobs[k].Name,-10}");
            for (int m = 0; m < metrics; m++)
            {
                bool own = Array.IndexOf(MatrixKnobs[k].Own, MatrixMetrics[m]) >= 0;
                bool speaks = significant[k, m] && Math.Abs(effect[k, m]) >= MatrixSpeaks || reversal[k, m];
                string cell = !speaks ? "·" : $"{effect[k, m]:+0.0;-0.0}" + (reversal[k, m] ? "!" : "");
                if (own) cell = speaks ? $"[{cell}]" : "[·]";
                row.Append($"{cell,8}");
            }
            GD.Print(row.ToString());
        }
        GD.Print("");
    }

    /// <summary>The mean over seeds of an outcome's change between two steps of one knob, and that mean's standard error.</summary>
    private static (double Mean, double StandardError) PairedChange(double[,,][] value, int k, int from, int to, int m, int seeds)
    {
        double sum = 0, sq = 0;
        for (int s = 0; s < seeds; s++)
        {
            double diff = value[k, to, s][m] - value[k, from, s][m];
            sum += diff;
            sq += diff * diff;
        }
        double mean = sum / seeds;
        double variance = Math.Max(0, sq / seeds - mean * mean);
        return (mean, seeds > 1 ? Math.Sqrt(variance / (seeds - 1)) : 0);
    }
}
