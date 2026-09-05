using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

public partial class GenerationAudit
{
    /// <summary>The five settings each climate knob is swept over, low to high.</summary>
    private static readonly float[] ClimateSteps = { 0f, 0.25f, 0.5f, 0.75f, 1f };

    /// <summary>
    /// One seed, twenty-five climates: the surface view at every pair of background
    /// moisture and warmth, laid out warmth across and moisture down, with a legend.
    /// The knobs only reach the Habitat stage, so every tile is the same terrain and
    /// the picture is the climate model on its own.
    /// </summary>
    private void WriteClimateGrid()
    {
        DirAccess.MakeDirRecursiveAbsolute(ClimateGrid);
        int seed = FirstSeed;
        int steps = ClimateSteps.Length;

        // Generate first, so the layout can be measured against the real footprint.
        var tiles = new IslandData[steps, steps];
        ulong t0 = Time.GetTicksMsec();
        for (int row = 0; row < steps; row++)
        for (int col = 0; col < steps; col++)
        {
            float moisture = ClimateSteps[row], warmth = ClimateSteps[col];
            IslandParams p = Variant(q =>
            {
                q.Size = ClimateGridSize;
                q.Moisture = moisture;
                q.Warmth = warmth;
            });
            tiles[row, col] = IslandGenerator.Generate(seed, p);
        }
        ulong ms = Time.GetTicksMsec() - t0;

        IslandData first = tiles[0, 0];
        GD.Print($"\n=== climate grid: seed {seed}, {first.Size}², "
            + $"{steps * steps} islands, {ms} ms ===");
        GD.Print($"  {first.Name}: {first.Arrangement}, {first.Character}, "
            + $"{first.Areas.Count} walk areas, {first.WaterBodies} water bodies, "
            + $"{first.Gates.Count} gates");
        GD.Print("  the terrain is one island: moisture and warmth are read by Habitat "
            + "alone, after everything that moves ground");
        PrintClimateHeld(tiles);
        PrintClimateShares(tiles);

        string path = $"{ClimateGrid}/climate_grid_{seed}_{first.Size}.png";
        DrawClimateGrid(tiles, seed).SavePng(path);
        GD.Print($"  collage: {path}");

        for (int row = 0; row < steps; row++)
        for (int col = 0; col < steps; col++)
            SaveSurface(tiles[row, col],
                $"{ClimateGrid}/tile_m{ClimateSteps[row]:0.00}_w{ClimateSteps[col]:0.00}.png"
                    .Replace(",", "."));
    }

    /// <summary>
    /// Checks the claim the strip makes: that surface height, exposure, rim distance,
    /// water distance and magick are the same in all twenty-five, so drawing them once
    /// is honest. Counts the cells that differ from the first tile rather than asserting it.
    /// </summary>
    private static void PrintClimateHeld(IslandData[,] tiles)
    {
        IslandData first = tiles[0, 0];
        int steps = ClimateSteps.Length;
        long height = 0, exposure = 0, rim = 0, rugged = 0, water = 0, magick = 0;

        for (int row = 0; row < steps; row++)
        for (int col = 0; col < steps; col++)
        {
            IslandData d = tiles[row, col];
            for (int x = 0; x < d.Size; x++)
            for (int z = 0; z < d.Size; z++)
            {
                if (!d.HasLand(x, z)) continue;
                if (d.SurfaceLevel(x, z) != first.SurfaceLevel(x, z)) height++;
                if (d.Exposure[x, z] != first.Exposure[x, z]) exposure++;
                if (d.RimDistance[x, z] != first.RimDistance[x, z]) rim++;
                if (d.Ruggedness[x, z] != first.Ruggedness[x, z]) rugged++;
                if (d.WaterDistance[x, z] != first.WaterDistance[x, z]) water++;
                if (d.Magick[x, z] != first.Magick[x, z]) magick++;
            }
        }
        GD.Print($"  held across all {steps * steps}: height {height} cells differ, "
            + $"exposure {exposure}, rim {rim}, rugged {rugged}, water distance {water}, "
            + $"magick {magick} (0 each is the claim the field strip makes)");
    }

    /// <summary>The two or three materials that carry each cell of the grid, so the picture has numbers behind it.</summary>
    private static void PrintClimateShares(IslandData[,] tiles)
    {
        int steps = ClimateSteps.Length;
        GD.Print($"  {"moist",5} {"warm",5}  the ground, by share of dry land");
        for (int row = 0; row < steps; row++)
        for (int col = 0; col < steps; col++)
        {
            IslandData d = tiles[row, col];
            var cells = new long[Enum.GetValues<SurfaceMaterial>().Length];
            long land = 0;
            for (int x = 0; x < d.Size; x++)
            for (int z = 0; z < d.Size; z++)
            {
                if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                cells[d.Material[x, z]]++;
                land++;
            }

            var parts = new List<(string Name, long Cells)>();
            foreach (SurfaceMaterial m in Enum.GetValues<SurfaceMaterial>())
                if (cells[(int)m] > 0) parts.Add((m.ToString().ToLowerInvariant(), cells[(int)m]));
            parts.Sort((a, b) => b.Cells.CompareTo(a.Cells));

            var bits = new List<string>();
            foreach (var (name, count) in parts)
                bits.Add($"{name} {100.0 * count / Math.Max(1, land):0}%");
            GD.Print($"  {ClimateSteps[row],5:0.00} {ClimateSteps[col],5:0.00}  "
                + string.Join(", ", bits));
        }
    }

    /// <summary>Ink, page and rules of the collage; the tiles themselves come from the shared palette.</summary>
    private static readonly Color Page = new(0.11f, 0.115f, 0.135f);
    private static readonly Color Ink = new(0.90f, 0.91f, 0.94f);
    private static readonly Color Dim = new(0.58f, 0.60f, 0.66f);
    private static readonly Color Rule = new(0.28f, 0.29f, 0.34f);

    /// <summary>
    /// The sheet: title, a warmth axis across the top, a moisture axis down the left,
    /// the twenty-five surface views, then the legend of every colour they can show.
    /// </summary>
    private static Image DrawClimateGrid(IslandData[,] tiles, int seed)
    {
        int steps = ClimateSteps.Length;
        int n = tiles[0, 0].Size;
        const int Zoom = 3, Gap = 10, Pad = 16, Gutter = 96, Lead = 26;
        int tile = n * Zoom;
        int gridW = steps * tile + (steps - 1) * Gap;

        // The header lines are written before the width is fixed: a line wider than the
        // grid would otherwise be clipped at the edge with nothing to say it had been.
        IslandData sample = tiles[0, 0];
        string subtitle = $"SEED {seed}, {n}X{n}, "
            + $"{sample.Arrangement.ToString().ToUpperInvariant()}, "
            + $"{sample.Character.ToString().ToUpperInvariant()}. SURFACE VIEW.";
        string SameNote = $"SAME TERRAIN IN ALL {steps * steps} TILES. ONLY MOISTURE AND WARMTH CHANGE.";
        string middleLabel = Label(ClimateSteps[steps / 2]);
        string fieldNote = $"HEIGHT, EXPOSURE, RIM, WATER, MAGICK: SAME IN ALL {steps * steps}. "
            + $"WARMTH, MOISTURE: FROM THE {middleLabel} / {middleLabel} TILE. "
            + $"WIND FROM {sample.WindFrom}, SUN FROM {sample.SunFrom}.";

        (string Head, (string Name, Color C)[] Rows)[] legend = LegendColumns();
        int textW = 0;
        foreach (string line in new[] { subtitle, SameNote, fieldNote })
            textW = Math.Max(textW, TinyFont.Width(line, 2));
        int legendW = 0;
        foreach (var column in legend)
        {
            legendW = Math.Max(legendW, TinyFont.Width(column.Head, 2) + Gap);
            foreach (var (name, _) in column.Rows)
                legendW = Math.Max(legendW, 26 + TinyFont.Width(name, 2) + Gap);
        }
        legendW = Math.Max(legendW, (gridW + Gap) / legend.Length) * legend.Length;
        int width = Math.Max(Math.Max(Gutter + gridW, Gutter + textW), Gutter + legendW) + Pad + 8;

        int titleY = Pad;
        int subY = titleY + TinyFont.Height(3) + 9;
        int noteY = subY + TinyFont.Height(2) + 7;
        int axisY = noteY + TinyFont.Height(2) + 15;
        int headY = axisY + TinyFont.Height(2) + 6;
        int gridTop = headY + TinyFont.Height(2) + 10;
        int gridBottom = gridTop + steps * tile + (steps - 1) * Gap;

        // The context strip: the fields the surface is read from, panels the width of
        // a tile, as many to a row as the grid has columns, and a second row for the rest.
        int stripHeadY = gridBottom + 24;
        int stripNoteY = stripHeadY + TinyFont.Height(2) + 7;
        int panelTitleY = stripNoteY + TinyFont.Height(2) + 14;
        int panelTop = panelTitleY + TinyFont.Height(2) + 8;
        const int BarHeight = 12;
        int barTop = panelTop + tile + 8;
        int barLabelY = barTop + BarHeight + 7;
        // A narrow tile puts the bar's two labels on two lines rather than over each other.
        int barLines = FieldLabelsFit(tile) ? 1 : 2;
        int rowBottom = barLabelY + barLines * (TinyFont.Height(2) + 4);
        int rowHeight = rowBottom - panelTitleY + 18;
        int fieldRows = (FieldPanels + steps - 1) / steps;
        int stripBottom = rowBottom + (fieldRows - 1) * rowHeight;

        int legendRows = 0;
        foreach (var column in legend) legendRows = Math.Max(legendRows, column.Rows.Length);
        int ruleY = stripBottom + 22;
        int legendTitleY = ruleY + 12;
        int legendTop = legendTitleY + TinyFont.Height(2) + 12;
        int height = legendTop + (legendRows + 1) * Lead + Pad;

        var img = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        img.Fill(Page);

        TinyFont.Draw(img, "CLIMATE GRID", Gutter, titleY, 3, Ink);
        TinyFont.Draw(img, subtitle, Gutter, subY, 2, Dim);
        TinyFont.Draw(img, SameNote, Gutter, noteY, 2, Dim);

        // The warmth axis, across.
        TinyFont.DrawCentered(img, "WARMTH >", Gutter + gridW / 2, axisY, 2, Ink);
        for (int col = 0; col < steps; col++)
        {
            int left = Gutter + col * (tile + Gap);
            TinyFont.DrawCentered(img, Label(ClimateSteps[col]), left + tile / 2, headY, 2, Ink);
        }

        // The moisture axis, down: the word stacked a letter to a line, then the values.
        const string Down = "MOISTURE";
        int wordTop = gridTop + (gridBottom - gridTop) / 2 - Down.Length * 18 / 2;
        for (int i = 0; i < Down.Length; i++)
            TinyFont.Draw(img, Down[i].ToString(), 10, wordTop + i * 18, 2, Ink);
        TinyFont.Draw(img, "V", 10, wordTop + Down.Length * 18 + 12, 2, Ink);

        for (int row = 0; row < steps; row++)
        {
            int top = gridTop + row * (tile + Gap);
            TinyFont.DrawRight(img, Label(ClimateSteps[row]), Gutter - 12,
                top + tile / 2 - TinyFont.Height(2) / 2, 2, Ink);
        }

        // The tiles, each in a hairline so one island's aether does not run into the next.
        for (int row = 0; row < steps; row++)
        for (int col = 0; col < steps; col++)
        {
            int left = Gutter + col * (tile + Gap), top = gridTop + row * (tile + Gap);
            Frame(img, left - 1, top - 1, tile + 2, tile + 2, Rule);
            Paint(img, tiles[row, col], left, top, Zoom);
        }

        DrawFieldStrip(img, tiles, Gutter, fieldNote, tile, Zoom, Gap,
            stripHeadY, stripNoteY, panelTitleY, panelTop, barTop, BarHeight, barLabelY,
            steps, rowHeight);

        HLine(img, Gutter, ruleY, gridW, Rule);
        TinyFont.Draw(img, "LEGEND", Gutter, legendTitleY, 2, Ink);

        // Columns at least as wide as their widest entry, so a 48-cell grid does not
        // run one column's names into the next.
        int columnW = (gridW + Gap) / legend.Length;
        foreach (var column in legend)
        {
            columnW = Math.Max(columnW, TinyFont.Width(column.Head, 2) + Gap);
            foreach (var (name, _) in column.Rows)
                columnW = Math.Max(columnW, 26 + TinyFont.Width(name, 2) + Gap);
        }
        for (int i = 0; i < legend.Length; i++)
        {
            int left = Gutter + i * columnW;
            TinyFont.Draw(img, legend[i].Head, left, legendTop, 2, Dim);
            for (int r = 0; r < legend[i].Rows.Length; r++)
            {
                var (name, c) = legend[i].Rows[r];
                int top = legendTop + (r + 1) * Lead;
                Fill(img, left, top - 2, 18, 18, c);
                Frame(img, left, top - 2, 18, 18, Rule);
                TinyFont.Draw(img, name, left + 26, top + 2, 2, Ink);
            }
        }
        return img;
    }

    /// <summary>How many field panels the strip carries; the layout wraps them at the grid's width.</summary>
    private const int FieldPanels = 7;

    /// <summary>
    /// The fields the surface view is read from, a panel each under the grid with its
    /// own ramp: the height the climate is draped over, the two knobs as they end up
    /// per column, the two that shift them — openness to the wind and how near the
    /// aether a column is — then the walk cost to fresh water the moisture strip
    /// reads, and the magick layer, which reads nothing and is read by nothing yet.
    /// Height, exposure, rim, water and magick are the same island in all
    /// twenty-five, so they are drawn once; warmth and moisture come from the middle
    /// tile, since those two are the thing the grid is sweeping.
    /// </summary>
    private static void DrawFieldStrip(Image img, IslandData[,] tiles, int gutter,
        string note, int tile, int zoom, int gap, int headY, int noteY, int titleY,
        int top, int barTop, int barHeight, int barLabelY, int perRow, int rowHeight)
    {
        IslandData terrain = tiles[0, 0];
        IslandData middle = tiles[ClimateSteps.Length / 2, ClimateSteps.Length / 2];

        short lo = short.MaxValue, hi = short.MinValue;
        for (int x = 0; x < terrain.Size; x++)
        for (int z = 0; z < terrain.Size; z++)
        {
            if (!terrain.HasLand(x, z)) continue;
            lo = Math.Min(lo, terrain.SurfaceLevel(x, z));
            hi = Math.Max(hi, terrain.SurfaceLevel(x, z));
        }
        float span = Math.Max(1, hi - lo);

        // Each panel: what it is called, where it is read, its ramp, and the two ends.
        var panels = new (string Title, IslandData From, Func<IslandData, int, int, Color> Of,
                          Func<float, Color> Bar, string Low, string High)[]
        {
            ("HEIGHT", terrain, (d, x, z) => DevPalette.Height((d.SurfaceLevel(x, z) - lo) / span),
                DevPalette.Height, FieldLabels[0].Low, FieldLabels[0].High),
            ("WARMTH", middle, (d, x, z) => DevPalette.WarmthTint(d.Warmth[x, z]),
                t => DevPalette.WarmthTint((byte)(t * 255f)), FieldLabels[1].Low, FieldLabels[1].High),
            ("MOISTURE", middle, (d, x, z) => Ramp(d.Moisture[x, z], DevPalette.MoistureRamp),
                t => DevPalette.MoistureRamp.Lo.Lerp(DevPalette.MoistureRamp.Hi, t),
                FieldLabels[2].Low, FieldLabels[2].High),
            ("EXPOSURE", terrain, (d, x, z) => Ramp(d.Exposure[x, z], DevPalette.ExposureRamp),
                t => DevPalette.ExposureRamp.Lo.Lerp(DevPalette.ExposureRamp.Hi, t),
                FieldLabels[3].Low, FieldLabels[3].High),
            ("RIM", terrain,
                (d, x, z) => Ramp((byte)Math.Min(255, d.RimDistance[x, z] * 6), DevPalette.RimRamp),
                t => DevPalette.RimRamp.Lo.Lerp(DevPalette.RimRamp.Hi, t),
                FieldLabels[4].Low, FieldLabels[4].High),
            ("WATER DISTANCE", terrain,
                (d, x, z) => Ramp((byte)Math.Min(255, d.WaterDistance[x, z] * 4), DevPalette.WaterRamp),
                t => DevPalette.WaterRamp.Lo.Lerp(DevPalette.WaterRamp.Hi, t),
                FieldLabels[5].Low, FieldLabels[5].High),
            ("MAGICK", terrain, (d, x, z) => Ramp(d.Magick[x, z], DevPalette.MagickRamp),
                t => DevPalette.MagickRamp.Lo.Lerp(DevPalette.MagickRamp.Hi, t),
                FieldLabels[6].Low, FieldLabels[6].High),
        };

        TinyFont.Draw(img, "INPUT FIELDS", gutter, headY, 2, Ink);
        TinyFont.Draw(img, note, gutter, noteY, 2, Dim);

        // The ramp's ends are inset, so one panel's "HIGH" does not read into the
        // next panel's "COLD" across a ten-pixel gap.
        bool oneLine = FieldLabelsFit(tile);

        for (int i = 0; i < panels.Length; i++)
        {
            var (title, from, of, bar, low, high) = panels[i];
            int left = gutter + (i % perRow) * (tile + gap);
            int down = (i / perRow) * rowHeight;

            TinyFont.Draw(img, title, left, titleY + down, 2, Ink);
            Frame(img, left - 1, top + down - 1, tile + 2, tile + 2, Rule);
            for (int x = 0; x < from.Size; x++)
            for (int z = 0; z < from.Size; z++)
                Fill(img, left + x * zoom, top + down + z * zoom, zoom, zoom,
                    from.HasLand(x, z) ? of(from, x, z) : DevPalette.Aether);

            for (int px = 0; px < tile; px++)
                Fill(img, left + px, barTop + down, 1, barHeight, bar(px / (float)(tile - 1)));
            Frame(img, left, barTop + down, tile, barHeight, Rule);

            TinyFont.Draw(img, low, left + FieldInset, barLabelY + down, 2, Dim);
            TinyFont.DrawRight(img, high, left + tile - FieldInset,
                (oneLine ? barLabelY : barLabelY + TinyFont.Height(2) + 4) + down, 2, Dim);
        }
    }

    /// <summary>The two ends of each field panel's ramp, in the panels' order.</summary>
    private static readonly (string Low, string High)[] FieldLabels =
    {
        ("LOW", "HIGH"), ("COLD", "HOT"), ("DRY", "WET"), ("LEE", "OPEN"), ("EDGE", "INLAND"),
        ("BANK", "FAR"), ("INERT", "SATURATED"),
    };

    /// <summary>Pixels the ramp labels stand in from each end of a panel.</summary>
    private const int FieldInset = 8;

    /// <summary>Whether every panel's two ramp labels fit side by side under a tile this wide.</summary>
    private static bool FieldLabelsFit(int tile)
    {
        foreach (var (low, high) in FieldLabels)
            if (2 * FieldInset + TinyFont.Width(low, 2) + 12 + TinyFont.Width(high, 2) > tile)
                return false;
        return true;
    }

    /// <summary>A habitat byte on a two-colour ramp, the way the lab draws its field views.</summary>
    private static Color Ramp(byte v, (Color Lo, Color Hi) ramp) => ramp.Lo.Lerp(ramp.Hi, v / 255f);

    /// <summary>A knob setting as a label; this machine prints decimals with a comma.</summary>
    private static string Label(float v) => $"{v:0.00}".Replace(",", ".");

    /// <summary>
    /// The legend, in the shape of the model: the three warmth bands as columns of
    /// dry / balanced / wet, then the ground that is not living, then the water.
    /// </summary>
    private static (string Head, (string, Color)[] Rows)[] LegendColumns()
    {
        static (string, Color) Of(string name, SurfaceMaterial m)
            => (name, DevPalette.Material(m));

        return new (string, (string, Color)[])[]
        {
            ("COLD", new[]
            {
                Of("TUNDRA (DRY)", SurfaceMaterial.Tundra),
                Of("MOORLAND (MID, WET)", SurfaceMaterial.Moorland),
                Of("BOG (EXCESS)", SurfaceMaterial.Bog),
            }),
            ("TEMPERATE", new[]
            {
                Of("STEPPE (DRY)", SurfaceMaterial.Steppe),
                Of("MEADOW (MID)", SurfaceMaterial.Meadow),
                Of("GRASS (WET)", SurfaceMaterial.Grass),
                Of("MARSH (EXCESS)", SurfaceMaterial.Marsh),
            }),
            ("HOT", new[]
            {
                Of("DUST (DRY)", SurfaceMaterial.Dust),
                Of("SAVANNA (MID)", SurfaceMaterial.Savanna),
                Of("FLOODPLAIN (WET, BY WATER)", SurfaceMaterial.Floodplain),
            }),
            ("BARE", new[]
            {
                Of("STONE", SurfaceMaterial.Stone),
                Of("SCREE", SurfaceMaterial.Scree),
                Of("SNOW", SurfaceMaterial.Snow),
                Of("SAND", SurfaceMaterial.Sand),
                Of("SILT (BED)", SurfaceMaterial.Silt),
            }),
            ("WATER", new[]
            {
                ("LAKE", DevPalette.LakeTint),
                ("STREAM", DevPalette.StreamTint),
                ("NAVIGABLE", DevPalette.ReachTint),
                ("FORD", DevPalette.FordTint),
                ("GOO", DevPalette.Goo),
                ("AETHER", DevPalette.Aether),
            }),
        };
    }

    /// <summary>One island's surface view straight into the sheet, nearest-neighbour at <paramref name="zoom"/>.</summary>
    private static void Paint(Image img, IslandData d, int left, int top, int zoom)
    {
        for (int x = 0; x < d.Size; x++)
        for (int z = 0; z < d.Size; z++)
        {
            Color c;
            if (!d.HasLand(x, z)) c = DevPalette.Aether;
            else if (d.WaterLevel[x, z] != IslandData.NoLand) c = DevPalette.Water(d, x, z);
            else c = DevPalette.Material((SurfaceMaterial)d.Material[x, z]);
            Fill(img, left + x * zoom, top + z * zoom, zoom, zoom, c);
        }
    }

    private static void Fill(Image img, int x, int z, int w, int h, Color c)
    {
        for (int i = 0; i < w; i++)
        for (int j = 0; j < h; j++)
        {
            int px = x + i, pz = z + j;
            if (px < 0 || pz < 0 || px >= img.GetWidth() || pz >= img.GetHeight()) continue;
            img.SetPixel(px, pz, c);
        }
    }

    private static void Frame(Image img, int x, int z, int w, int h, Color c)
    {
        Fill(img, x, z, w, 1, c);
        Fill(img, x, z + h - 1, w, 1, c);
        Fill(img, x, z, 1, h, c);
        Fill(img, x + w - 1, z, 1, h, c);
    }

    private static void HLine(Image img, int x, int z, int w, Color c) => Fill(img, x, z, w, 1, c);

    /// <summary>
    /// Scores <see cref="ClimateScout"/> seeds at the collage's footprint for the two
    /// things the picture needs — terrain that varies and water you can see — and
    /// prints them best first, so the seed the collage runs on is a measurement.
    /// </summary>
    private void PrintClimateScout()
    {
        GD.Print($"\n=== climate scout: {ClimateScout} seeds at {ClimateGridSize}², "
            + "diverse terrain and visible water first ===");
        GD.Print($"  {"seed",7} {"score",5} {"forms",5} {"mats",4} {"high%",5} {"land%",5} "
            + $"{"lake",5} {"river",5} {"nav",4} {"bodies",6} {"walk%",5}  "
            + "arrangement / character");

        var rows = new List<(int Score, string Line)>();
        for (int i = 0; i < ClimateScout; i++)
        {
            int seed = FirstSeed + i;
            IslandParams p = Variant(q => q.Size = ClimateGridSize);
            IslandData d = IslandGenerator.Generate(seed, p);

            var forms = new HashSet<byte>();
            var materials = new HashSet<byte>();
            long land = 0, lake = 0, river = 0, nav = 0, walk = 0, high = 0;
            for (int x = 0; x < d.Size; x++)
            for (int z = 0; z < d.Size; z++)
            {
                if (!d.HasLand(x, z)) continue;
                land++;
                forms.Add(d.Landform[x, z]);
                materials.Add(d.Material[x, z]);
                // Ground that can climb past the plateau ceiling, so the lapse is visible.
                if ((LandformType)d.Landform[x, z] is LandformType.Mountain
                    or LandformType.Massif) high++;
                if (d.Walk[x, z] == d.Mainland) walk++;
                if (d.WaterLevel[x, z] == IslandData.NoLand) continue;
                if (d.River[x, z]) { river++; if (d.Navigable[x, z]) nav++; }
                else lake++;
            }

            long cells = (long)d.Size * d.Size;
            // Diversity first, then water you can pick out at a glance, then a lake.
            int score = forms.Count * 6 + materials.Count * 3
                + (int)Math.Min(40, lake) + (int)Math.Min(30, river / 3)
                + (int)Math.Min(20, nav / 2) + Math.Min(12, d.WaterBodies * 3)
                + (lake > 0 ? 10 : 0);

            rows.Add((score, $"  {seed,7} {score,5} {forms.Count,5} {materials.Count,4} "
                + $"{100 * high / Math.Max(1, land),5} {100 * land / cells,5} {lake,5} "
                + $"{river,5} {nav,4} {d.WaterBodies,6} "
                + $"{100 * walk / Math.Max(1, land),5}  {d.Arrangement} / {d.Character}"));
        }

        rows.Sort((a, b) => b.Score.CompareTo(a.Score));
        foreach (var (_, line) in rows) GD.Print(line);
    }
}
