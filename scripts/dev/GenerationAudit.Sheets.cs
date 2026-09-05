using System;
using System.Collections.Generic;
using Godot;
using ProjectNikitin.Generation;
using static ProjectNikitin.Generation.Grid;

namespace ProjectNikitin.Dev;

/// <summary>The three sheets the design page carries: every arrangement, every knob, and the pipeline stage by stage.</summary>
public partial class GenerationAudit
{
    private static readonly Color CaptionInk = new(0.85f, 0.85f, 0.80f);

    // ---- every arrangement ------------------------------------------------------

    /// <summary>One seed at <see cref="ArrangementSize"/>² in every arrangement, six to a row, each captioned with its name.</summary>
    private void WriteArrangementSheet()
    {
        DirAccess.MakeDirRecursiveAbsolute(ArrangementSheet);
        var shapes = new List<IslandArrangement>();
        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
            if (how != IslandArrangement.Auto) shapes.Add(how);

        const int Columns = 6, Zoom = 3, Gap = 10, Pad = 16;
        int n = ArrangementSize, tile = n * Zoom;
        int cap = TinyFont.Height(2) + 6;
        int rows = (shapes.Count + Columns - 1) / Columns;
        int titleH = TinyFont.Height(3) + 8 + TinyFont.Height(2) + 10;
        string subtitle = $"SEED {FirstSeed} IN EVERY LAYOUT AT {n}X{n}. LAND BY HEIGHT, WATER BY KIND, GATES RED. "
            + "EVERY PIECE IS WITHIN A BRIDGE OF THE NEXT.";
        int width = Math.Max(Pad * 2 + Columns * tile + (Columns - 1) * Gap, Pad * 2 + TinyFont.Width(subtitle, 2));
        int height = Pad + titleH + rows * (tile + cap + Gap) + Pad;

        var sheet = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        sheet.Fill(Page);
        TinyFont.Draw(sheet, "THE ARRANGEMENTS", Pad, Pad, 3, Ink);
        TinyFont.Draw(sheet, subtitle, Pad, Pad + TinyFont.Height(3) + 8, 2, Dim);

        for (int i = 0; i < shapes.Count; i++)
        {
            IslandParams p = Variant(q => { q.Arrangement = shapes[i]; q.Size = n; });
            IslandData d = IslandGenerator.Generate(FirstSeed, p);
            Image img = Portrait(d);
            img.Resize(tile, tile, Image.Interpolation.Nearest);
            int px = Pad + (i % Columns) * (tile + Gap);
            int py = Pad + titleH + (i / Columns) * (tile + cap + Gap);
            sheet.BlitRect(img, new Rect2I(0, 0, tile, tile), new Vector2I(px, py));
            Frame(sheet, px - 1, py - 1, tile + 2, tile + 2, Rule);
            TinyFont.Draw(sheet, shapes[i].ToString().ToUpperInvariant(), px, py + tile + 4, 2, CaptionInk);
        }
        string path = $"{ArrangementSheet}/arrangements_{FirstSeed}_{n}.png";
        sheet.SavePng(path);
        GD.Print($"arrangement sheet: {path}");
    }

    // ---- every knob ------------------------------------------------------------

    /// <summary>
    /// One seed, a row per 0–1 knob, the knob at 0, ¼, ½, ¾, 1 across, everything
    /// else rolled by the seed as the preset has it: the terrain knobs in the
    /// height-and-water view, the wind in the moisture view where its shadow is.
    /// </summary>
    private void WriteKnobSheet()
    {
        DirAccess.MakeDirRecursiveAbsolute(KnobSheet);
        var knobs = new (string Name, Action<IslandParams, float> Set, string Note, bool Moisture)[]
        {
            ("LANDFORM MIX", (p, v) => p.LandformMix = v, "LOW GROUND TO HIGH", false),
            ("RELIEF", (p, v) => p.Relief = v, "FLAT TO EXAGGERATED", false),
            ("HILLINESS", (p, v) => p.Hilliness = v, "SWELLS TO MOUNDS", false),
            ("RIVERS", (p, v) => p.Rivers = v, "THE BAR FOR A RIVER", false),
            ("LAKES", (p, v) => p.Lakes = v, "NONE TO ONE PER FLAT PATCH", false),
            ("VALLEYS", (p, v) => p.Valleys = v, "AN INCISION TO A VALE", false),
            ("WIND", (p, v) => p.Wind = v, "MOISTURE VIEW: THE LEE DRIES", true),
        };
        float[] steps = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        const int Zoom = 3, Gap = 10, Pad = 16, Gutter = 200;
        int n = KnobSize, tile = n * Zoom;
        int titleH = TinyFont.Height(3) + 8 + TinyFont.Height(2) + 12;
        int headH = TinyFont.Height(2) + 10;
        string subtitle = $"SEED {FirstSeed} AT {n}X{n}. EACH ROW PINS ONE KNOB AND LETS THE SEED ROLL THE REST, "
            + "SO A ROW DIFFERS ONLY IN THAT KNOB. HEIGHT IS ON ONE SCALE ACROSS A ROW.";
        int width = Math.Max(Gutter + steps.Length * tile + (steps.Length - 1) * Gap + Pad, Pad * 2 + TinyFont.Width(subtitle, 2));
        int height = Pad + titleH + headH + knobs.Length * (tile + Gap) + Pad;

        var sheet = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        sheet.Fill(Page);
        TinyFont.Draw(sheet, "THE KNOBS", Pad, Pad, 3, Ink);
        TinyFont.Draw(sheet, subtitle, Pad, Pad + TinyFont.Height(3) + 8, 2, Dim);
        int gridTop = Pad + titleH + headH;
        for (int c = 0; c < steps.Length; c++)
            TinyFont.DrawCentered(sheet, Label(steps[c]), Gutter + c * (tile + Gap) + tile / 2, gridTop - headH + 2, 2, Ink);

        for (int r = 0; r < knobs.Length; r++)
        {
            var (name, set, note, moisture) = knobs[r];
            int top = gridTop + r * (tile + Gap);
            TinyFont.Draw(sheet, name, Pad, top + tile / 2 - TinyFont.Height(2) - 4, 2, Ink);
            TinyFont.Draw(sheet, note, Pad, top + tile / 2 + 4, 1, Dim);
            // The row's islands first, so the height ramp can share one scale across them:
            // per-island scaling hid the relief knob entirely.
            var islands = new IslandData[steps.Length];
            short lo = short.MaxValue, hi = short.MinValue;
            for (int c = 0; c < steps.Length; c++)
            {
                float v = steps[c];
                IslandParams p = Variant(q => { q.Size = n; set(q, v); });
                islands[c] = IslandGenerator.Generate(FirstSeed, p);
                var (islandLo, islandHi) = HeightRange(islands[c]);
                lo = Math.Min(lo, islandLo);
                hi = Math.Max(hi, islandHi);
            }
            // The wind's row reads moisture on the row's own range: the shadow is thirty
            // points on a scale of 255, invisible on the full ramp.
            byte mLo = 255, mHi = 0;
            if (moisture)
                foreach (IslandData d in islands)
                for (int x = 0; x < d.Size; x++)
                for (int z = 0; z < d.Size; z++)
                {
                    if (!d.HasLand(x, z) || d.WaterLevel[x, z] != IslandData.NoLand) continue;
                    mLo = Math.Min(mLo, d.Moisture[x, z]);
                    mHi = Math.Max(mHi, d.Moisture[x, z]);
                }
            for (int c = 0; c < steps.Length; c++)
            {
                IslandData d = islands[c];
                Image img = moisture ? MoistureView(d, mLo, mHi) : Portrait(d, lo, hi);
                img.Resize(tile, tile, Image.Interpolation.Nearest);
                int left = Gutter + c * (tile + Gap);
                sheet.BlitRect(img, new Rect2I(0, 0, tile, tile), new Vector2I(left, top));
                Frame(sheet, left - 1, top - 1, tile + 2, tile + 2, Rule);
            }
        }
        string path = $"{KnobSheet}/knobs_{FirstSeed}_{n}.png";
        sheet.SavePng(path);
        GD.Print($"knob sheet: {path}");
    }

    /// <summary>The moisture axis as the lab draws it, water by kind over it, the ramp stretched over <paramref name="lo"/>..<paramref name="hi"/>.</summary>
    private static Image MoistureView(IslandData d, byte lo, byte hi)
    {
        int n = d.Size;
        float span = Math.Max(1, hi - lo);
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Color c;
            if (!d.HasLand(x, z)) c = DevPalette.Aether;
            else if (d.WaterLevel[x, z] != IslandData.NoLand) c = DevPalette.Water(d, x, z);
            else c = DevPalette.MoistureRamp.Lo.Lerp(DevPalette.MoistureRamp.Hi,
                Mathf.Clamp((d.Moisture[x, z] - lo) / span, 0f, 1f));
            img.SetPixel(x, z, c);
        }
        return img;
    }

    // ---- the pipeline, stage by stage ----------------------------------------

    /// <summary>The stages the sheet shows, in order, with a caption each.</summary>
    private static readonly (string Name, string Caption, string Short)[] StageCaptions =
    {
        ("footprint", "1 FOOTPRINT: THE MASK", "1 FOOTPRINT"),
        ("landforms", "2 REGIONS AND LANDFORMS", "2 LANDFORMS"),
        ("relief", "3 RELIEF AND SCULPTING", "3 RELIEF"),
        ("lakes", "4 STANDING WATER", "4 LAKES"),
        ("settled", "5 BEACHES AND SETTLING", "5 SETTLED"),
        ("rivers", "6 RIVERS", "6 RIVERS"),
        ("traversal", "8 WALK AREAS AND GATES", "8 WALK, GATES"),
        ("roads", "10 ROADS", "10 ROADS"),
        ("climate", "CLIMATE: WARMTH", "WARMTH"),
        ("surface", "SURFACES", "SURFACES"),
    };

    /// <summary>What each stage's colours mean, as swatch-and-name pairs; the sheet and the single tiles both draw them.</summary>
    private static (string Name, Color C)[] StageLegend(string name)
    {
        var heightLow = new Color(0.2f, 0.32f, 0.16f);
        var heightHigh = new Color(0.85f, 0.8f, 0.66f);
        var beach = heightLow.Lerp(heightHigh, 0.3f).Lerp(new Color(0.9f, 0.85f, 0.55f), 0.5f);
        switch (name)
        {
            case "footprint":
                return new[] { ("LAND", new Color(0.80f, 0.80f, 0.72f)), ("AETHER", DevPalette.Aether) };
            case "landforms":
            {
                var rows = new List<(string, Color)>();
                foreach (LandformType t in Enum.GetValues<LandformType>())
                    rows.Add((t.ToString().ToUpperInvariant(), DevPalette.Landform(t)));
                rows.Add(("A BORDER (DARKENED)", DevPalette.Landform(LandformType.Plain).Darkened(0.5f)));
                return rows.ToArray();
            }
            case "relief":
                return new[] { ("LOW GROUND", heightLow), ("HIGH GROUND", heightHigh), ("SHADED: A RISE TO THE SOUTH-EAST", heightLow.Lerp(heightHigh, 0.5f) * 0.7f) };
            case "lakes":
                return new[] { ("LOW GROUND", heightLow), ("HIGH GROUND", heightHigh), ("LAKE", DevPalette.LakeTint), ("GOO", DevPalette.Goo) };
            case "settled":
                return new[] { ("LOW GROUND", heightLow), ("HIGH GROUND", heightHigh), ("BEACH (A SLAB DOWN)", beach), ("LAKE", DevPalette.LakeTint) };
            case "rivers":
                return new[] { ("STREAM", DevPalette.StreamTint), ("NAVIGABLE", DevPalette.ReachTint), ("LAKE", DevPalette.LakeTint), ("HOT WATER", DevPalette.HotTint), ("GOO", DevPalette.Goo) };
            case "traversal":
                return new[] { ("MAINLAND (UNDER THE ENTRY)", DevPalette.Mainland), ("ANOTHER DISTRICT", DevPalette.District(1)),
                    ("BROKEN GROUND, UNDER 20 CELLS", DevPalette.Broken), ("WATER", DevPalette.WalkWater),
                    ("GATE LANDING", new Color(0.98f, 0.78f, 0.15f)), ("HANGING GATE", new Color(1f, 0.2f, 0.2f)) };
            case "roads":
                return new[] { ("ROAD", new Color(0.98f, 0.95f, 0.62f)), ("A STAIR", new Color(1f, 0.45f, 0.25f)), ("A BRIDGE", new Color(1f, 0.80f, 0.20f)),
                    ("GATE LANDING", new Color(0.95f, 0.82f, 0.25f)), ("HANGING GATE", new Color(1f, 0.2f, 0.2f)) };
            case "climate":
                return new[] { ("FROZEN", DevPalette.WarmthTint(20)), ("COLD", DevPalette.WarmthTint(100)), ("TEMPERATE", DevPalette.WarmthTint(150)),
                    ("HOT", DevPalette.WarmthTint(200)), ("SAND", DevPalette.WarmthTint(240)) };
            default:
            {
                var rows = new List<(string, Color)>();
                foreach (SurfaceMaterial m in Enum.GetValues<SurfaceMaterial>())
                    rows.Add((m.ToString().ToUpperInvariant(), DevPalette.Material(m)));
                return rows.ToArray();
            }
        }
    }

    /// <summary>How a legend lays out in a width: as many columns as the widest name allows, the rows shared between them, and the height that takes.</summary>
    private static (int ColumnWidth, int PerColumn, int Height) LegendLayout((string Name, Color C)[] rows, int width, int font)
    {
        int lead = TinyFont.Height(font) + 8;
        int colW = 0;
        foreach (var (name, _) in rows) colW = Math.Max(colW, 16 + TinyFont.Width(name, font) + 14);
        int columns = Math.Max(1, width / colW);
        int perColumn = (rows.Length + columns - 1) / columns;
        return (colW, perColumn, perColumn * lead);
    }

    /// <summary>Draws swatch-and-name pairs in columns from (left, top) within a width, as <see cref="LegendLayout"/> lays them; returns the height used.</summary>
    private static int DrawLegendRows(Image img, (string Name, Color C)[] rows, int left, int top, int width, int font)
    {
        int lead = TinyFont.Height(font) + 8;
        var (colW, perColumn, height) = LegendLayout(rows, width, font);
        for (int i = 0; i < rows.Length; i++)
        {
            int cx = left + (i / perColumn) * colW, cy = top + (i % perColumn) * lead;
            Fill(img, cx, cy + 1, 10, 10, rows[i].C);
            Frame(img, cx, cy + 1, 10, 10, Rule);
            TinyFont.Draw(img, rows[i].Name, cx + 16, cy + (font == 1 ? 3 : 0), font, Ink);
        }
        return height;
    }

    /// <summary>
    /// One island drawn after every stage of the pipeline, through the generator's
    /// stage hook, as one sheet and as a tile per stage. The keel is the one stage
    /// with nothing to show from above.
    /// </summary>
    private void WriteStageSheet()
    {
        DirAccess.MakeDirRecursiveAbsolute(StageSheet);
        var tiles = new List<(string Name, Image Img)>();
        IslandGenerator.OnStage = (name, view) =>
        {
            // A re-rolled seed starts over: keep the island that shipped.
            if (name == "footprint") tiles.Clear();
            tiles.Add((name, DrawStage(name, view)));
        };
        IslandParams p = Variant(q => q.Size = StageSize);
        IslandData d;
        try { d = IslandGenerator.Generate(FirstSeed, p); }
        finally { IslandGenerator.OnStage = null; }

        const int Columns = 5, Zoom = 2, Gap = 12, Pad = 16;
        int n = StageSize, tile = n * Zoom;
        int cap = TinyFont.Height(2) + 8;
        int count = StageCaptions.Length;
        // The legend band under each row is as tall as the tallest legend on the sheet.
        int legendH = 0;
        foreach (var (stageName, _, _) in StageCaptions)
            legendH = Math.Max(legendH, LegendLayout(StageLegend(stageName), tile, 1).Height + 6);
        int LegendH = legendH;
        int rows = (count + Columns - 1) / Columns;
        int titleH = TinyFont.Height(3) + 8 + TinyFont.Height(2) + 12;
        string subtitle = $"{d.Name.ToUpperInvariant()}, SEED {FirstSeed}, {n}X{n}, {d.Arrangement.ToString().ToUpperInvariant()}, "
            + $"{d.Character.ToString().ToUpperInvariant()}: THE SAME ISLAND AFTER EACH STAGE. THE KEEL HAS NO TOP VIEW.";
        int width = Math.Max(Pad * 2 + Columns * tile + (Columns - 1) * Gap, Pad * 2 + TinyFont.Width(subtitle, 2));
        int height = Pad + titleH + rows * (tile + cap + LegendH + Gap) + Pad;

        var sheet = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        sheet.Fill(Page);
        TinyFont.Draw(sheet, "THE PIPELINE, SEEN", Pad, Pad, 3, Ink);
        TinyFont.Draw(sheet, subtitle, Pad, Pad + TinyFont.Height(3) + 8, 2, Dim);

        for (int i = 0; i < count; i++)
        {
            var (name, caption, brief) = StageCaptions[i];
            int at = tiles.FindIndex(t => t.Name == name);
            if (at < 0) continue;
            Image img = tiles[at].Img;
            (string, Color)[] legend = StageLegend(name);

            // The tile on its own, captioned and keyed, for the page's stage headings.
            int singleW = n * 3 + 8;
            int singleLegendH = LegendLayout(legend, singleW - 8, 1).Height + 12;
            var single = Image.CreateEmpty(singleW, n * 3 + 8 + cap + singleLegendH, false, Image.Format.Rgb8);
            single.Fill(Page);
            Image big = (Image)img.Duplicate();
            big.Resize(n * 3, n * 3, Image.Interpolation.Nearest);
            single.BlitRect(big, new Rect2I(0, 0, n * 3, n * 3), new Vector2I(4, 4));
            Frame(single, 3, 3, n * 3 + 2, n * 3 + 2, Rule);
            TinyFont.Draw(single, caption, 4, n * 3 + 10, 2, CaptionInk);
            DrawLegendRows(single, legend, 4, n * 3 + 10 + cap, singleW - 8, 1);
            single.SavePng($"{StageSheet}/stage_{i + 1:00}_{name}.png");

            img.Resize(tile, tile, Image.Interpolation.Nearest);
            int px = Pad + (i % Columns) * (tile + Gap);
            int py = Pad + titleH + (i / Columns) * (tile + cap + LegendH + Gap);
            sheet.BlitRect(img, new Rect2I(0, 0, tile, tile), new Vector2I(px, py));
            Frame(sheet, px - 1, py - 1, tile + 2, tile + 2, Rule);
            TinyFont.Draw(sheet, brief, px, py + tile + 4, 2, CaptionInk);
            DrawLegendRows(sheet, legend, px, py + tile + cap + 2, tile, 1);
        }
        string path = $"{StageSheet}/pipeline_{FirstSeed}_{n}.png";
        sheet.SavePng(path);
        GD.Print($"stage sheet: {path} and {count} tiles");
    }

    /// <summary>One stage as a picture, from what the draft has at that point.</summary>
    private static Image DrawStage(string name, IslandGenerator.StageView v)
    {
        int n = v.Size;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);
        img.Fill(DevPalette.Aether);
        IslandData d = v.Data;

        switch (name)
        {
            case "footprint":
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    if (v.Land[x, z]) img.SetPixel(x, z, new Color(0.80f, 0.80f, 0.72f));
                break;

            case "landforms":
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!v.Land[x, z] || v.Region == null || v.Plan == null) continue;
                    int r = v.Region[x, z];
                    Color c = DevPalette.Landform(v.Plan[r].Type);
                    bool border = false;
                    for (int k = 0; k < 4 && !border; k++)
                    {
                        int nx = x + Dx[k], nz = z + Dz[k];
                        border = InBounds(n, nx, nz) && v.Land[nx, nz] && v.Region[nx, nz] != r;
                    }
                    img.SetPixel(x, z, border ? c.Darkened(0.5f) : c);
                }
                break;

            case "relief":
            case "lakes":
            case "settled":
            case "rivers":
                DrawDraft(img, v, name);
                break;

            case "traversal":
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z)) continue;
                    int id = d.Walk[x, z];
                    Color c = id == Traversal.Water ? DevPalette.WalkWater
                        : id < 0 || id >= d.Areas.Count || !d.Areas[id].IsDistrict ? DevPalette.Broken
                        : id == d.Mainland ? DevPalette.Mainland : DevPalette.District(id);
                    if (d.Landings[x, z]) c = new Color(0.98f, 0.78f, 0.15f);
                    img.SetPixel(x, z, c);
                }
                MarkGates(img, d);
                break;

            case "roads":
            {
                Image portrait = Portrait(d);
                img.BlitRect(portrait, new Rect2I(0, 0, n, n), Vector2I.Zero);
                var road = new Color(0.98f, 0.95f, 0.62f);
                var stair = new Color(1f, 0.45f, 0.25f);
                var span = new Color(1f, 0.80f, 0.20f);
                foreach (Passage path in d.Passages)
                {
                    foreach (Vector2I c in path.Path) img.SetPixel(c.X, c.Y, road);
                    foreach (Works w in path.Built)
                    {
                        Color c = w.Kind == WorksKind.Stair ? stair : span;
                        img.SetPixel(w.From.X, w.From.Y, c);
                        img.SetPixel(w.To.X, w.To.Y, c);
                    }
                }
                MarkGates(img, d);
                break;
            }

            case "climate":
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z)) continue;
                    img.SetPixel(x, z, d.WaterLevel[x, z] != IslandData.NoLand
                        ? DevPalette.Water(d, x, z) : DevPalette.WarmthTint(d.Warmth[x, z]));
                }
                break;

            default:
                for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                {
                    if (!d.HasLand(x, z)) continue;
                    img.SetPixel(x, z, d.WaterLevel[x, z] != IslandData.NoLand
                        ? DevPalette.Water(d, x, z) : DevPalette.Material((SurfaceMaterial)d.Material[x, z]));
                }
                break;
        }
        return img;
    }

    /// <summary>Height by ramp over the draft's surface, water by kind where the draft has it, beaches tinted once they exist.</summary>
    private static void DrawDraft(Image img, IslandGenerator.StageView v, string name)
    {
        int n = v.Size;
        if (v.Surface == null) return;
        short lo = short.MaxValue, hi = short.MinValue;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!v.Land[x, z]) continue;
            lo = Math.Min(lo, v.Surface[x, z]);
            hi = Math.Max(hi, v.Surface[x, z]);
        }
        float span = Math.Max(1, hi - lo);
        IslandData d = v.Data;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!v.Land[x, z]) continue;
            Color c;
            bool wet = v.Water != null && v.Water[x, z] != IslandData.NoLand;
            if (wet && v.Fluid != null && v.Fluid[x, z] == (byte)FluidKind.Goo) c = DevPalette.Goo;
            else if (wet && name == "rivers" && d.River[x, z])
                c = d.Navigable[x, z] ? DevPalette.ReachTint : DevPalette.StreamTint;
            else if (wet) c = DevPalette.LakeTint;
            else
            {
                c = DevPalette.Height((v.Surface[x, z] - lo) / span);
                if (name != "relief" && name != "lakes" && d.Beach[x, z])
                    c = c.Lerp(new Color(0.9f, 0.85f, 0.55f), 0.5f);
            }
            img.SetPixel(x, z, c);
        }
    }

    private static void MarkGates(Image img, IslandData d)
    {
        int n = d.Size;
        foreach (Gate g in d.Gates)
        {
            var tint = g.Kind == GateKind.Hanging ? new Color(1f, 0.2f, 0.2f) : new Color(1f, 0.5f, 0.15f);
            img.SetPixel(Math.Clamp(g.Center.X, 0, n - 1), Math.Clamp(g.Center.Z, 0, n - 1), tint);
        }
    }
}
