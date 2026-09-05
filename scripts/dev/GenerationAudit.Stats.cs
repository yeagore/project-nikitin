using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

public partial class GenerationAudit
{
    /// <summary>Cells of dry land a material needs on an island to count as present there, for the co-occurrence: a district's worth, not a tor.</summary>
    private const int PresentCells = 20;

    /// <summary>The materials the statistics read, in the grid's order: the cold row, the temperate, the hot, then the ends and the rock. Silt is a bed and goes with the water.</summary>
    private static readonly SurfaceMaterial[] StatMaterials =
    {
        SurfaceMaterial.Tundra, SurfaceMaterial.Heath, SurfaceMaterial.Moorland, SurfaceMaterial.Bog,
        SurfaceMaterial.Steppe, SurfaceMaterial.Meadow, SurfaceMaterial.Grass, SurfaceMaterial.Marsh,
        SurfaceMaterial.Dust, SurfaceMaterial.Savanna, SurfaceMaterial.Verdure, SurfaceMaterial.Floodplain,
        SurfaceMaterial.Snow, SurfaceMaterial.Sand, SurfaceMaterial.Stone, SurfaceMaterial.Scree,
    };

    /// <summary>
    /// Two counts and two sheets. First, the mean share of dry land each material
    /// takes at every one of the twenty-five knob positions (moisture and warmth in
    /// quarters), over <see cref="StatsSeeds"/> seeds each with everything else
    /// rolled. Second, over <see cref="StatsIslands"/> rolled seeds, how often each
    /// material is present at all and, given one, how often each other is: the
    /// co-occurrence a Domain's ground actually has.
    /// </summary>
    private void WriteClimateStats()
    {
        DirAccess.MakeDirRecursiveAbsolute(ClimateStats);
        int steps = ClimateSteps.Length;
        int mats = Enum.GetValues<SurfaceMaterial>().Length;

        // ---- shares at the knob positions
        ulong t0 = Time.GetTicksMsec();
        var share = new double[steps, steps, mats];
        for (int row = 0; row < steps; row++)
        for (int col = 0; col < steps; col++)
        {
            float moisture = ClimateSteps[row], warmth = ClimateSteps[col];
            IslandParams p = Variant(q => { q.Moisture = moisture; q.Warmth = warmth; });
            for (int i = 0; i < StatsSeeds; i++)
            {
                IslandData d = IslandGenerator.Generate(SeedAt(i), p);
                long[] cells = DryCellsByMaterial(d, out long dry);
                if (dry == 0) continue;
                for (int m = 0; m < mats; m++) share[row, col, m] += cells[m] / (double)dry / StatsSeeds;
            }
        }
        GD.Print($"\n=== surface shares at the 25 knob positions: {StatsSeeds} seeds each at "
            + $"{Params.Size}², everything else rolled, {Time.GetTicksMsec() - t0} ms ===");
        GD.Print($"  {"moist",5} {"warm",5}  mean share of dry land");
        for (int row = 0; row < steps; row++)
        for (int col = 0; col < steps; col++)
        {
            var bits = new List<string>();
            foreach (var (m, v) in Ranked(share, row, col, mats))
                if (v >= 0.005) bits.Add($"{m.ToString().ToLowerInvariant()} {100 * v:0.0}%");
            GD.Print($"  {ClimateSteps[row],5:0.00} {ClimateSteps[col],5:0.00}  {string.Join(", ", bits)}");
        }
        string sharesPath = $"{ClimateStats}/surface_shares.png";
        DrawShares(share).SavePng(sharesPath);
        GD.Print($"  sheet: {sharesPath}");

        // ---- co-occurrence over rolled seeds
        t0 = Time.GetTicksMsec();
        int k = StatMaterials.Length;
        var present = new int[k];
        var both = new int[k, k];
        int islands = 0;
        for (int i = 0; i < StatsIslands; i++)
        {
            IslandData d = IslandGenerator.Generate(SeedAt(i), Params);
            long[] cells = DryCellsByMaterial(d, out long dry);
            if (dry == 0) continue;
            islands++;
            var has = new bool[k];
            for (int j = 0; j < k; j++) has[j] = cells[(int)StatMaterials[j]] >= PresentCells;
            for (int a = 0; a < k; a++)
            {
                if (!has[a]) continue;
                present[a]++;
                for (int b = 0; b < k; b++) if (has[b]) both[a, b]++;
            }
        }
        GD.Print($"\n=== surface co-occurrence over {islands} rolled seeds at {Params.Size}² "
            + $"(present = {PresentCells}+ cells of dry land), {Time.GetTicksMsec() - t0} ms ===");
        var head = new System.Text.StringBuilder($"  {"given",-11} {"any",4}");
        foreach (SurfaceMaterial m in StatMaterials) head.Append($" {Abbrev(m),4}");
        GD.Print(head.ToString());
        for (int a = 0; a < k; a++)
        {
            var line = new System.Text.StringBuilder($"  {StatMaterials[a].ToString().ToLowerInvariant(),-11} "
                + $"{100 * present[a] / Math.Max(1, islands),4}");
            for (int b = 0; b < k; b++)
                line.Append(present[a] > 0 ? $" {100 * both[a, b] / present[a],4}" : $" {"-",4}");
            GD.Print(line.ToString());
        }
        string coPath = $"{ClimateStats}/surface_cooccurrence.png";
        DrawCooccurrence(present, both, islands).SavePng(coPath);
        GD.Print($"  sheet: {coPath}");
    }

    private static long[] DryCellsByMaterial(IslandData d, out long dry)
    {
        var cells = new long[Enum.GetValues<SurfaceMaterial>().Length];
        dry = 0;
        for (int x = 0; x < d.Size; x++)
        for (int z = 0; z < d.Size; z++)
        {
            if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
            cells[d.Material[x, z]]++;
            dry++;
        }
        return cells;
    }

    /// <summary>The materials at one knob position, largest share first; ties in enum order.</summary>
    private static List<(SurfaceMaterial M, double V)> Ranked(double[,,] share, int row, int col, int mats)
    {
        var ranked = new List<(SurfaceMaterial, double)>();
        for (int m = 0; m < mats; m++) ranked.Add(((SurfaceMaterial)m, share[row, col, m]));
        // A stable order: insertion sort, largest first.
        for (int i = 1; i < ranked.Count; i++)
        {
            var it = ranked[i];
            int j = i - 1;
            while (j >= 0 && ranked[j].Item2 < it.Item2) { ranked[j + 1] = ranked[j]; j--; }
            ranked[j + 1] = it;
        }
        return ranked;
    }

    private static string Abbrev(SurfaceMaterial m) => m switch
    {
        SurfaceMaterial.Floodplain => "flod",
        SurfaceMaterial.Moorland => "moor",
        SurfaceMaterial.Savanna => "sava",
        SurfaceMaterial.Verdure => "verd",
        SurfaceMaterial.Steppe => "step",
        SurfaceMaterial.Meadow => "mead",
        SurfaceMaterial.Tundra => "tund",
        SurfaceMaterial.Marsh => "mars",
        SurfaceMaterial.Heath => "heat",
        SurfaceMaterial.Grass => "gras",
        SurfaceMaterial.Stone => "ston",
        SurfaceMaterial.Scree => "scre",
        _ => m.ToString().ToLowerInvariant()[..Math.Min(4, m.ToString().Length)],
    };

    /// <summary>
    /// The shares sheet: the knob grid as the climate collage lays it out, each tile
    /// a stacked bar of the ground by share and the names and figures beside it, the
    /// materials under half a percent folded into "other".
    /// </summary>
    private Image DrawShares(double[,,] share)
    {
        int steps = ClimateSteps.Length;
        int mats = Enum.GetValues<SurfaceMaterial>().Length;
        const int TileW = 252, TileH = 196, Gap = 12, Pad = 16, Gutter = 96, Lead = 26;
        const int BarW = 30, BarH = 160, Rows = 8, RowLead = 19;
        int gridW = steps * TileW + (steps - 1) * Gap;

        string subtitle = $"MEAN SHARE OF DRY LAND OVER {StatsSeeds} SEEDS PER TILE AT {Params.Size}X{Params.Size}. "
            + "EVERY OTHER KNOB, THE SHAPE AND THE CHARACTER ROLL PER SEED.";
        string note = "WARMTH ACROSS, MOISTURE DOWN. STONE, SCREE, SNOW AND SAND ARE THE ROCK, THE MOUNTAIN TOPS "
            + "AND THE DUNES: THE KNOBS DO NOT MOVE THEM.";
        int textW = Math.Max(TinyFont.Width(subtitle, 2), TinyFont.Width(note, 2));
        int width = Math.Max(Gutter + gridW, Gutter + textW) + Pad + 8;

        int titleY = Pad;
        int subY = titleY + TinyFont.Height(3) + 9;
        int noteY = subY + TinyFont.Height(2) + 7;
        int axisY = noteY + TinyFont.Height(2) + 15;
        int headY = axisY + TinyFont.Height(2) + 6;
        int gridTop = headY + TinyFont.Height(2) + 10;
        int gridBottom = gridTop + steps * TileH + (steps - 1) * Gap;

        (string Head, (string Name, Color C)[] Rows)[] legend = SharesLegend();
        int legendRows = 0;
        foreach (var column in legend) legendRows = Math.Max(legendRows, column.Rows.Length);
        int ruleY = gridBottom + 22;
        int legendTitleY = ruleY + 12;
        int legendTop = legendTitleY + TinyFont.Height(2) + 12;
        int height = legendTop + (legendRows + 1) * Lead + Pad;

        var img = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        img.Fill(Page);
        TinyFont.Draw(img, "SURFACE SHARES BY KNOB", Gutter, titleY, 3, Ink);
        TinyFont.Draw(img, subtitle, Gutter, subY, 2, Dim);
        TinyFont.Draw(img, note, Gutter, noteY, 2, Dim);

        TinyFont.DrawCentered(img, "WARMTH >", Gutter + gridW / 2, axisY, 2, Ink);
        for (int col = 0; col < steps; col++)
            TinyFont.DrawCentered(img, Label(ClimateSteps[col]), Gutter + col * (TileW + Gap) + TileW / 2, headY, 2, Ink);
        const string Down = "MOISTURE";
        int wordTop = gridTop + (gridBottom - gridTop) / 2 - Down.Length * 18 / 2;
        for (int i = 0; i < Down.Length; i++)
            TinyFont.Draw(img, Down[i].ToString(), 10, wordTop + i * 18, 2, Ink);
        TinyFont.Draw(img, "V", 10, wordTop + Down.Length * 18 + 12, 2, Ink);
        for (int row = 0; row < steps; row++)
            TinyFont.DrawRight(img, Label(ClimateSteps[row]), Gutter - 12,
                gridTop + row * (TileH + Gap) + TileH / 2 - TinyFont.Height(2) / 2, 2, Ink);

        for (int row = 0; row < steps; row++)
        for (int col = 0; col < steps; col++)
        {
            int left = Gutter + col * (TileW + Gap), top = gridTop + row * (TileH + Gap);
            Frame(img, left - 1, top - 1, TileW + 2, TileH + 2, Rule);

            List<(SurfaceMaterial M, double V)> ranked = Ranked(share, row, col, mats);
            // The bar, top down in share order.
            int barLeft = left + 10, barTop = top + (TileH - BarH) / 2;
            double filled = 0;
            foreach (var (m, v) in ranked)
            {
                if (v <= 0) continue;
                int y0 = barTop + (int)Math.Round(filled * BarH);
                int y1 = barTop + (int)Math.Round((filled + v) * BarH);
                if (y1 > y0) Fill(img, barLeft, y0, BarW, y1 - y0, DevPalette.Material(m));
                filled += v;
            }
            Frame(img, barLeft - 1, barTop - 1, BarW + 2, BarH + 2, Rule);

            // The names and figures: the largest first, the small ones folded.
            int textLeft = barLeft + BarW + 14;
            int rowY = barTop;
            int shown = 0;
            double other = 0;
            foreach (var (m, v) in ranked)
            {
                if (v < 0.005) { other += v; continue; }
                if (shown >= Rows - 1) { other += v; continue; }
                Fill(img, textLeft, rowY + 2, 10, 10, DevPalette.Material(m));
                Frame(img, textLeft, rowY + 2, 10, 10, Rule);
                TinyFont.Draw(img, $"{m.ToString().ToUpperInvariant()} {100 * v:0.0}%", textLeft + 16, rowY, 2, Ink);
                rowY += RowLead;
                shown++;
            }
            if (other >= 0.005)
                TinyFont.Draw(img, $"OTHER {100 * other:0.0}%", textLeft + 16, rowY, 2, Dim);
        }

        HLine(img, Gutter, ruleY, gridW, Rule);
        TinyFont.Draw(img, "LEGEND", Gutter, legendTitleY, 2, Ink);
        int columnW = (gridW + Gap) / legend.Length;
        for (int i = 0; i < legend.Length; i++)
        {
            int left = Gutter + i * columnW;
            TinyFont.Draw(img, legend[i].Head, left, legendTop, 2, Dim);
            for (int r = 0; r < legend[i].Rows.Length; r++)
            {
                var (name, c) = legend[i].Rows[r];
                int y = legendTop + (r + 1) * Lead;
                Fill(img, left, y - 2, 18, 18, c);
                Frame(img, left, y - 2, 18, 18, Rule);
                TinyFont.Draw(img, name, left + 26, y + 2, 2, Ink);
            }
        }
        return img;
    }

    private static (string Head, (string, Color)[] Rows)[] SharesLegend()
    {
        static (string, Color) Of(string name, SurfaceMaterial m) => (name, DevPalette.Material(m));
        return new (string, (string, Color)[])[]
        {
            ("FRIGID AND COLD", new[] { Of("TUNDRA", SurfaceMaterial.Tundra), Of("HEATH", SurfaceMaterial.Heath),
                Of("MOORLAND", SurfaceMaterial.Moorland), Of("BOG", SurfaceMaterial.Bog) }),
            ("TEMPERATE", new[] { Of("STEPPE", SurfaceMaterial.Steppe), Of("MEADOW", SurfaceMaterial.Meadow),
                Of("GRASS", SurfaceMaterial.Grass), Of("MARSH", SurfaceMaterial.Marsh) }),
            ("HOT", new[] { Of("DUST", SurfaceMaterial.Dust), Of("SAVANNA", SurfaceMaterial.Savanna),
                Of("VERDURE", SurfaceMaterial.Verdure), Of("FLOODPLAIN", SurfaceMaterial.Floodplain) }),
            ("THE ENDS AND THE ROCK", new[] { Of("SNOW", SurfaceMaterial.Snow), Of("SAND", SurfaceMaterial.Sand),
                Of("STONE", SurfaceMaterial.Stone), Of("SCREE", SurfaceMaterial.Scree) }),
        };
    }

    /// <summary>The co-occurrence ramp: the page at nothing, a pale teal at everything.</summary>
    private static readonly Color CoLow = new(0.15f, 0.16f, 0.20f), CoHigh = new(0.62f, 0.88f, 0.90f);

    /// <summary>
    /// The co-occurrence sheet: a row per material A and a column per material B,
    /// the cell the share of islands with A that also have B, coloured on a ramp and
    /// figured; a first column for how many islands have A at all.
    /// </summary>
    private Image DrawCooccurrence(int[] present, int[,] both, int islands)
    {
        int k = StatMaterials.Length;
        const int CellW = 50, CellH = 30, Pad = 16, RowHead = 160, Lead = 26;
        int colHead = 0;
        foreach (SurfaceMaterial m in StatMaterials) colHead = Math.Max(colHead, m.ToString().Length);
        int colHeadH = colHead * (TinyFont.Height(1) + 2) + 26;

        string subtitle = $"OVER {islands} ROLLED SEEDS AT {Params.Size}X{Params.Size}: ROW A, COLUMN B, THE SHARE OF ISLANDS "
            + "WITH A THAT ALSO HAVE B, IN PERCENT.";
        string note = $"ANY: THE SHARE OF ALL ISLANDS WITH A. PRESENT MEANS {PresentCells} CELLS OR MORE OF DRY LAND, "
            + "A DISTRICT'S WORTH, NOT A TOR.";
        int gridW = (k + 1) * CellW;
        int textW = Math.Max(TinyFont.Width(subtitle, 2), TinyFont.Width(note, 2));
        int width = Math.Max(Pad + RowHead + gridW, Pad + textW) + Pad + 8;

        int titleY = Pad;
        int subY = titleY + TinyFont.Height(3) + 9;
        int noteY = subY + TinyFont.Height(2) + 7;
        int headTop = noteY + TinyFont.Height(2) + 18;
        int gridTop = headTop + colHeadH;
        int gridBottom = gridTop + k * CellH;
        int rampY = gridBottom + 24;
        int height = rampY + 14 + TinyFont.Height(2) + Pad + Lead;

        var img = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        img.Fill(Page);
        TinyFont.Draw(img, "SURFACE CO-OCCURRENCE", Pad, titleY, 3, Ink);
        TinyFont.Draw(img, subtitle, Pad, subY, 2, Dim);
        TinyFont.Draw(img, note, Pad, noteY, 2, Dim);

        int gridLeft = Pad + RowHead;
        // Column heads: "ANY", then each material spelt downward with its swatch under it.
        TinyFont.DrawCentered(img, "ANY", gridLeft + CellW / 2, gridTop - TinyFont.Height(2) - 8, 2, Ink);
        for (int b = 0; b < k; b++)
        {
            int cx = gridLeft + (b + 1) * CellW + CellW / 2;
            string name = StatMaterials[b].ToString().ToUpperInvariant();
            int y = gridTop - 22 - name.Length * (TinyFont.Height(1) + 2);
            for (int c = 0; c < name.Length; c++)
                TinyFont.DrawCentered(img, name[c].ToString(), cx, y + c * (TinyFont.Height(1) + 2), 1, Ink);
            Fill(img, cx - 7, gridTop - 18, 14, 12, DevPalette.Material(StatMaterials[b]));
            Frame(img, cx - 7, gridTop - 18, 14, 12, Rule);
        }

        for (int a = 0; a < k; a++)
        {
            int top = gridTop + a * CellH;
            Fill(img, Pad, top + 8, 14, 14, DevPalette.Material(StatMaterials[a]));
            Frame(img, Pad, top + 8, 14, 14, Rule);
            TinyFont.Draw(img, StatMaterials[a].ToString().ToUpperInvariant(), Pad + 22, top + 9, 2, Ink);

            // "Any": the presence rate over all islands.
            Cell(img, gridLeft, top, CellW, CellH, present[a] / (double)Math.Max(1, islands), false);
            for (int b = 0; b < k; b++)
            {
                double v = present[a] > 0 ? both[a, b] / (double)present[a] : 0;
                Cell(img, gridLeft + (b + 1) * CellW, top, CellW, CellH, v, a == b);
            }
        }
        Frame(img, gridLeft - 1, gridTop - 1, gridW + 2, k * CellH + 2, Rule);
        Fill(img, gridLeft + CellW - 1, gridTop, 1, k * CellH, Ink);

        // The ramp, with its ends.
        int rampW = 200;
        for (int px = 0; px < rampW; px++)
            Fill(img, Pad + px, rampY, 1, 12, CoLow.Lerp(CoHigh, px / (float)(rampW - 1)));
        Frame(img, Pad, rampY, rampW, 12, Rule);
        TinyFont.Draw(img, "0%", Pad, rampY + 16, 2, Dim);
        TinyFont.DrawRight(img, "100%", Pad + rampW, rampY + 16, 2, Dim);
        TinyFont.Draw(img, "THE DIAGONAL IS A GIVEN AND LEFT BLANK.", Pad + rampW + 24, rampY + 2, 2, Dim);
        return img;
    }

    /// <summary>One cell of the co-occurrence: the ramp behind the figure, the figure dark on a bright cell.</summary>
    private static void Cell(Image img, int x, int y, int w, int h, double v, bool diagonal)
    {
        Color back = diagonal ? Page : CoLow.Lerp(CoHigh, (float)v);
        Fill(img, x, y, w, h, back);
        Frame(img, x, y, w, h, Rule);
        if (diagonal) return;
        string text = $"{Math.Round(100 * v)}";
        TinyFont.DrawCentered(img, text, x + w / 2, y + (h - TinyFont.Height(2)) / 2, 2, v > 0.55 ? Page : Ink);
    }
}
