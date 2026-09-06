using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

public partial class GenerationAudit
{
    /// <summary>
    /// The climate grid as an area chart: warmth across, moisture down, every
    /// byte pair coloured with the ground it gives. Two panels — open ground away
    /// from water, and flat ground beside it, where the floodplain and the marsh
    /// can be — with the band lines drawn on the axes and the knobs' own range
    /// bracketed. The patches (bog, marsh) are a checker of their colour over the
    /// ground they sit in, since a noise field decides them cell by cell. Drawn
    /// straight from <see cref="Surfaces.Climate"/>, so it is the rule, not a
    /// picture of the rule.
    /// </summary>
    private void WriteClimateChart()
    {
        DirAccess.MakeDirRecursiveAbsolute(ClimateChart);
        string path = $"{ClimateChart}/climate_chart.png";
        DrawClimateChart().SavePng(path);
        GD.Print($"climate chart: {path}");
    }

    /// <summary>The material at one climate, snow first, with the patch noises both open or both shut.</summary>
    private static SurfaceMaterial ChartMaterial(int warmth, int moist, int near, bool patches)
    {
        if (warmth < Surfaces.SnowBelow) return SurfaceMaterial.Snow;
        float noise = patches ? 1f : 0f;
        return Surfaces.Climate((byte)warmth, (byte)moist, near, 0, noise, noise);
    }

    private static Image DrawClimateChart()
    {
        const int Zoom = 2, Pad = 16, Gutter = 84, Gap = 56, AxisRoom = 44, Lead = 26;
        const int Side = 256 * Zoom;
        var panels = new (string Title, string Note, int Near)[]
        {
            ("OPEN GROUND, AWAY FROM WATER", "THE GRID ON ITS OWN. THE BOG IS A PATCH.", int.MaxValue),
            ("FLAT GROUND BESIDE THE WATER", "WITHIN TWO CELLS: THE FLOODPLAIN, AND THE MARSH AS A PATCH.", 1),
        };

        string title = "CLIMATE CHART";
        string subtitle = "WARMTH ACROSS, MOISTURE DOWN, 0 TO 255 EACH. THE GROUND AT EVERY PAIR, "
            + "FROM THE SURFACE STAGE'S OWN RULE.";
        string rangeNote = $"THE KNOBS: OPEN LOWLAND READS WARMTH {Surfaces.LowlandWarmthAt0} AT 0 AND "
            + $"{Surfaces.LowlandWarmthAt1} AT 1 (BRACKETED); MOISTURE IS THE KNOB TIMES 255, PLUS UP TO 192 BESIDE WATER.";
        string outsideNote = "OUTSIDE THE BRACKET ONLY THE MODIFIERS REACH: THE LAPSE, THE SUN, THE HOLLOWS, "
            + "THE LEE, THE RIM, THE HOT WATER, THE WATER'S TEMPERING.";

        int textW = 0;
        foreach (string line in new[] { subtitle, rangeNote, outsideNote })
            textW = Math.Max(textW, TinyFont.Width(line, 2));
        int panelsW = panels.Length * Side + (panels.Length - 1) * Gap;
        int width = Math.Max(Gutter + panelsW, Gutter + textW) + Pad + 8;

        int titleY = Pad;
        int subY = titleY + TinyFont.Height(3) + 9;
        int rangeY = subY + TinyFont.Height(2) + 7;
        int outsideY = rangeY + TinyFont.Height(2) + 7;
        int panelTitleY = outsideY + TinyFont.Height(2) + 18;
        int panelNoteY = panelTitleY + TinyFont.Height(2) + 6;
        int axisTopY = panelNoteY + TinyFont.Height(2) + 12;      // warmth band names, over the panel, on two rows
        int top = axisTopY + 2 * TinyFont.Height(1) + 16;
        int bottom = top + Side;
        int knobY = bottom + 8;                                    // knob ticks under the panel
        int bracketY = knobY + TinyFont.Height(2) + 8;

        (string Head, (string Name, Color C, bool Patch)[] Rows)[] legend = ChartLegend();
        int legendRows = 0;
        foreach (var column in legend) legendRows = Math.Max(legendRows, column.Rows.Length);
        int ruleY = bracketY + TinyFont.Height(2) + AxisRoom;
        int legendTitleY = ruleY + 12;
        int legendTop = legendTitleY + TinyFont.Height(2) + 12;
        int height = legendTop + (legendRows + 1) * Lead + Pad;

        var img = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        img.Fill(Page);
        TinyFont.Draw(img, title, Gutter, titleY, 3, Ink);
        TinyFont.Draw(img, subtitle, Gutter, subY, 2, Dim);
        TinyFont.Draw(img, rangeNote, Gutter, rangeY, 2, Dim);
        TinyFont.Draw(img, outsideNote, Gutter, outsideY, 2, Dim);

        for (int i = 0; i < panels.Length; i++)
        {
            var (panelTitle, note, near) = panels[i];
            int left = Gutter + i * (Side + Gap);
            TinyFont.Draw(img, panelTitle, left, panelTitleY, 2, Ink);
            TinyFont.Draw(img, note, left, panelNoteY, 2, Dim);

            // The field: warmth across (cold left), moisture down (dry at the top, as the collage has it).
            for (int w = 0; w < 256; w++)
            for (int m = 0; m < 256; m++)
            {
                SurfaceMaterial ground = ChartMaterial(w, m, near, false);
                SurfaceMaterial patch = ChartMaterial(w, m, near, true);
                Color c = DevPalette.Material(patch != ground && ((w + m) & 1) == 0 ? patch : ground);
                Fill(img, left + w * Zoom, top + m * Zoom, Zoom, Zoom, c);
            }
            Frame(img, left - 1, top - 1, Side + 2, Side + 2, Rule);

            // The band lines: a hairline across the field and the name at the edge — the
            // warmth names on two alternating rows over the panel, since two lines can be
            // twenty-five bytes apart; the moisture names once, right of the last panel.
            for (int b = 0; b < Surfaces.WarmthLines.Length; b++)
            {
                var (name, at) = Surfaces.WarmthLines[b];
                int x = left + at * Zoom;
                for (int y = top; y < bottom; y += 3) Fill(img, x, y, 1, 2, Ink);
                int row = axisTopY + (b % 2) * (TinyFont.Height(1) + 3);
                TinyFont.DrawCentered(img, $"{name} {at}", x, row, 1, Ink);
                Fill(img, x, row + TinyFont.Height(1) + 1, 1, top - row - TinyFont.Height(1) - 1, Rule);
            }
            foreach (var (name, at) in Surfaces.MoistureLines)
            {
                int y = top + at * Zoom;
                for (int x = left; x < left + Side; x += 3) Fill(img, x, y, 2, 1, Ink);
                if (i == panels.Length - 1)
                    TinyFont.Draw(img, $"{name} {at}", left + Side + 8, y - TinyFont.Height(1) / 2, 1, Ink);
            }

            // The knobs: where a warmth knob puts the open lowland, and a moisture knob the background.
            for (int k = 0; k <= 4; k++)
            {
                float knob = k / 4f;
                int x = left + Mathf.RoundToInt(Mathf.Lerp(Surfaces.LowlandWarmthAt0, Surfaces.LowlandWarmthAt1, knob)) * Zoom;
                Fill(img, x, bottom, 1, 6, Ink);
                TinyFont.DrawCentered(img, Label(knob), x, knobY, 2, Ink);
                int y = top + Mathf.RoundToInt(255f * knob) * Zoom;
                if (y >= bottom) y = bottom - 1;
                if (i == 0)
                {
                    Fill(img, left - 8, y, 6, 1, Ink);
                    TinyFont.DrawRight(img, Label(knob), left - 12, y - TinyFont.Height(2) / 2 + (k == 0 ? 6 : k == 4 ? -6 : 0), 2, Dim);
                }
            }
            int bx0 = left + Surfaces.LowlandWarmthAt0 * Zoom, bx1 = left + Surfaces.LowlandWarmthAt1 * Zoom;
            HLine(img, bx0, bracketY + 4, bx1 - bx0, Ink);
            Fill(img, bx0, bracketY, 1, 5, Ink);
            Fill(img, bx1, bracketY, 1, 5, Ink);
            TinyFont.DrawCentered(img, "WARMTH KNOB 0 TO 1", (bx0 + bx1) / 2, bracketY + 9, 2, Dim);
        }

        // The axis words.
        TinyFont.DrawCentered(img, "WARMTH >", Gutter + panelsW / 2, bracketY + 9 + TinyFont.Height(2) + 8, 2, Ink);
        const string Down = "MOISTURE";
        int wordTop = top + Side / 2 - Down.Length * 18 / 2;
        for (int c = 0; c < Down.Length; c++)
            TinyFont.Draw(img, Down[c].ToString(), 10, wordTop + c * 18, 2, Ink);
        TinyFont.Draw(img, "V", 10, wordTop + Down.Length * 18 + 12, 2, Ink);

        HLine(img, Gutter, ruleY, panelsW, Rule);
        TinyFont.Draw(img, "LEGEND", Gutter, legendTitleY, 2, Ink);
        int columnW = (panelsW + Gap) / legend.Length;
        foreach (var column in legend)
        {
            columnW = Math.Max(columnW, TinyFont.Width(column.Head, 2) + Gap);
            foreach (var (name, _, _) in column.Rows)
                columnW = Math.Max(columnW, 26 + TinyFont.Width(name, 2) + Gap);
        }
        for (int i = 0; i < legend.Length; i++)
        {
            int left = Gutter + i * columnW;
            TinyFont.Draw(img, legend[i].Head, left, legendTop, 2, Dim);
            for (int r = 0; r < legend[i].Rows.Length; r++)
            {
                var (name, c, patch) = legend[i].Rows[r];
                int y = legendTop + (r + 1) * Lead;
                Fill(img, left, y - 2, 18, 18, c);
                if (patch)
                    for (int px = 0; px < 18; px++)
                    for (int py = 0; py < 18; py++)
                        if (((px / 2 + py / 2) & 1) == 0) img.SetPixel(left + px, y - 2 + py, Page);
                Frame(img, left, y - 2, 18, 18, Rule);
                TinyFont.Draw(img, name, left + 26, y + 2, 2, Ink);
            }
        }
        return img;
    }

    /// <summary>The chart's legend, in the shape of the grid, the patches marked as such.</summary>
    private static (string Head, (string, Color, bool)[] Rows)[] ChartLegend()
    {
        static (string, Color, bool) Of(string name, SurfaceMaterial m, bool patch = false)
            => (name, DevPalette.Material(m), patch);

        return new (string, (string, Color, bool)[])[]
        {
            ("FRIGID AND COLD", new[]
            {
                Of("TUNDRA", SurfaceMaterial.Tundra),
                Of("HEATH", SurfaceMaterial.Heath),
                Of("MOORLAND", SurfaceMaterial.Moorland),
                Of("BOG (A PATCH)", SurfaceMaterial.Bog, true),
            }),
            ("TEMPERATE", new[]
            {
                Of("STEPPE", SurfaceMaterial.Steppe),
                Of("MEADOW", SurfaceMaterial.Meadow),
                Of("GRASS", SurfaceMaterial.Grass),
                Of("MARSH (A PATCH, BY WATER)", SurfaceMaterial.Marsh, true),
            }),
            ("HOT", new[]
            {
                Of("DUST", SurfaceMaterial.Dust),
                Of("SAVANNA", SurfaceMaterial.Savanna),
                Of("VERDURE", SurfaceMaterial.Verdure),
                Of("FLOODPLAIN (BY WATER)", SurfaceMaterial.Floodplain),
            }),
            ("THE ENDS", new[]
            {
                Of("SNOW", SurfaceMaterial.Snow),
                Of("SAND", SurfaceMaterial.Sand),
            }),
        };
    }
}
