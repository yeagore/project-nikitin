using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

public partial class GenerationAudit
{
    /// <summary>A flooded column's colour by kind: goo violet, ford pale, navigable deep, stream mid, lake dark.</summary>
    private static Color WaterTint(IslandData d, int x, int z) => DevPalette.Water(d, x, z);

    /// <summary>Land / water ASCII of one island per arrangement, n/64 cells to the character so 128² fits a terminal.</summary>
    private void PrintSilhouettes()
    {
        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;

            IslandParams p = Variant(q => q.Arrangement = how);
            IslandData d = IslandGenerator.Generate(FirstSeed, p);
            int n = d.Size, step = Math.Max(1, n / 64);

            GD.Print($"--- {how} (seed {FirstSeed}) ---");
            for (int z = 0; z < n; z += step)
            {
                var row = new System.Text.StringBuilder();
                for (int x = 0; x < n; x += step)
                {
                    // Inside a sample: land over water, water over aether.
                    bool land = false, wet = false;
                    for (int dx = 0; dx < step; dx++)
                    for (int dz = 0; dz < step; dz++)
                    {
                        int cx = x + dx, cz = z + dz;
                        if (cx >= n || cz >= n || !d.HasLand(cx, cz)) continue;
                        if (d.WaterLevel[cx, cz] != IslandData.NoLand) wet = true;
                        else land = true;
                    }
                    row.Append(land ? '#' : wet ? '~' : '.');
                }
                GD.Print(row.ToString());
            }
        }
    }

    /// <summary>The first few islands' water at full resolution, a character to the cell.</summary>
    private void PrintWaterways()
    {
        for (int i = 0; i < Math.Min(3, Seeds); i++)
        {
            int seed = SeedAt(i);
            IslandData d = IslandGenerator.Generate(seed, Params);
            int n = d.Size;

            var falls = new HashSet<Vector2I>();
            foreach (Fall f in d.Falls) falls.Add(f.Cell);

            GD.Print($"--- waterways, seed {seed} ({d.Character}, {d.Arrangement}) ---");
            GD.Print("    . aether   , land   ~ stream   = navigable   O lake   v fall   o eyot");
            for (int z = 0; z < n; z++)
            {
                var row = new System.Text.StringBuilder(n);
                for (int x = 0; x < n; x++)
                {
                    if (!d.HasLand(x, z)) { row.Append('.'); continue; }
                    bool wet = d.WaterLevel[x, z] != IslandData.NoLand;
                    char c = !wet ? ',' :
                             !d.River[x, z] ? 'O' :
                             d.Navigable[x, z] ? '=' : '~';
                    if (!wet && Wet(d, x - 1, z) && Wet(d, x + 1, z)) c = 'o';
                    if (!wet && Wet(d, x, z - 1) && Wet(d, x, z + 1)) c = 'o';
                    if (falls.Contains(new Vector2I(x, z))) c = 'v';
                    row.Append(c);
                }
                GD.Print(row.ToString());
            }
        }
    }

    /// <summary>
    /// Two top-view PNGs per arrangement at the preset size, then the debutants and
    /// ThousandIsles at 64² — a shape that only reads at 128 is a shape that lies.
    /// </summary>
    private void WritePortraits()
    {
        DirAccess.MakeDirRecursiveAbsolute(Portraits);
        int wrote = 0;
        foreach (IslandArrangement how in Enum.GetValues<IslandArrangement>())
        {
            if (how == IslandArrangement.Auto) continue;
            IslandParams p = Variant(q => q.Arrangement = how);
            for (int i = 0; i < 2; i++)
            {
                int seed = SeedAt(i);
                IslandData d = IslandGenerator.Generate(seed, p);
                SavePortrait(d, $"{Portraits}/{how}_{p.Size}_{seed}.png");
                wrote++;
            }
        }

        foreach (IslandArrangement how in Debutants.Append(IslandArrangement.ThousandIsles))
        {
            IslandParams p = Variant(q => { q.Arrangement = how; q.Size = 64; });
            IslandData d = IslandGenerator.Generate(FirstSeed, p);
            SavePortrait(d, $"{Portraits}/{how}_64_{FirstSeed}.png");
            wrote++;
        }
        GD.Print($"portraits: {wrote} written to {Portraits}");
    }

    private static string Masses(int masses) => $"{masses} mass{(masses == 1 ? "" : "es")}";
    private static string Caption(int seed, int masses) => $"{seed}  {Masses(masses)}";

    /// <summary>
    /// One sheet per arrangement: GallerySeeds consecutive seeds at GallerySize², four
    /// to a row, each tile captioned with its seed and how many landmasses it came out
    /// as. Prints the landmass histogram per shape, so "often merges" is a number.
    /// </summary>
    private void WriteGallery()
    {
        DirAccess.MakeDirRecursiveAbsolute(Gallery);
        IEnumerable<IslandArrangement> shapes = GalleryShapes.Length == 0
            ? Enum.GetValues<IslandArrangement>().Where(h => h != IslandArrangement.Auto)
            : GalleryShapes.Split(',').Select(s => Enum.Parse<IslandArrangement>(s.Trim(), true));

        const int columns = 4, scale = 2, gap = 6;
        int n = GallerySize;
        int tile = n * scale;
        int font = tile >= 160 ? 2 : 1;      // a 48² tile is too narrow for the big caption
        // The widest caption the sheet will carry; if it does not fit under a tile the
        // seed and the count go on two lines instead of into the next tile.
        int widest = TinyFont.Width(Caption(FirstSeed + GallerySeeds - 1, 10), font);
        int captionLines = widest > tile ? 2 : 1;
        int caption = captionLines * (TinyFont.Height(font) + 4);
        int rows = (GallerySeeds + columns - 1) / columns;
        int titleH = TinyFont.Height(3) + 8;
        int width = gap + columns * (tile + gap);
        int height = titleH + gap + rows * (tile + caption + gap);

        foreach (IslandArrangement how in shapes)
        {
            IslandParams p = Variant(q => { q.Arrangement = how; q.Size = n; });
            var sheet = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
            sheet.Fill(new Color(0.12f, 0.12f, 0.14f));
            TinyFont.Draw(sheet, $"{how} {n}", gap, 4, 3, new Color(0.9f, 0.9f, 0.85f));

            var histogram = new SortedDictionary<int, int>();
            var flagged = new List<string>();
            int attempts = 0, unmet = 0;
            long landCells = 0;
            float extent = 0;
            for (int i = 0; i < GallerySeeds; i++)
            {
                int seed = FirstSeed + i;
                IslandData d = IslandGenerator.Generate(seed, p);
                int masses = LabelLandmasses(d, n, new int[n, n]);
                histogram.TryGetValue(masses, out int had);
                histogram[masses] = had + 1;

                attempts += d.Attempts;
                if (d.Unmet.Length > 0) unmet++;
                if (d.Attempts > 1 || d.Unmet.Length > 0)
                    flagged.Add($"{seed}:{d.Attempts}{(d.Unmet.Length > 0 ? " unmet " + d.Unmet : "")}");
                landCells += LandCount(d);
                extent += ExtentPercent(d);

                Image img = Portrait(d);
                img.Resize(tile, tile, Image.Interpolation.Nearest);
                int px = gap + (i % columns) * (tile + gap);
                int pz = titleH + gap + (i / columns) * (tile + caption + gap);
                sheet.BlitRect(img, new Rect2I(0, 0, tile, tile), new Vector2I(px, pz));
                var inkC = new Color(0.85f, 0.85f, 0.8f);
                if (captionLines == 1)
                    TinyFont.Draw(sheet, Caption(seed, masses), px, pz + tile + 2, font, inkC);
                else
                {
                    TinyFont.Draw(sheet, seed.ToString(), px, pz + tile + 2, font, inkC);
                    TinyFont.Draw(sheet, Masses(masses), px, pz + tile + 2 + TinyFont.Height(font) + 4,
                                  font, inkC);
                }
            }

            sheet.SavePng($"{Gallery}/{how}_{n}.png");
            if (GalleryMasks) WriteMaskSheet(how, p, width, height, tile, font, caption, titleH);
            string counts = string.Join(", ", histogram.Select(kv => $"{kv.Value} x {kv.Key}"));
            float landShare = 100f * landCells / (GallerySeeds * (float)n * n);
            GD.Print($"gallery: {how,-14} {GallerySeeds} seeds at {n}²: landmasses {counts}"
                + $" | attempts {attempts / (float)GallerySeeds:0.00} unmet {unmet}"
                + $" land% {landShare:0.0} extent% {extent / GallerySeeds:0.0}"
                + (flagged.Count > 0 ? " | rerolled/unmet: " + string.Join(", ", flagged) : ""));
        }
    }

    /// <summary>
    /// The gallery's twin: the same seeds' raw footprint masks, land white on aether,
    /// captioned with the landmass count and the extent share of the mask alone.
    /// </summary>
    private void WriteMaskSheet(IslandArrangement how, IslandParams p, int width, int height,
                                int tile, int font, int caption, int titleH)
    {
        const int columns = 4, gap = 6;
        int n = p.Size;
        var sheet = Image.CreateEmpty(width, height, false, Image.Format.Rgb8);
        sheet.Fill(new Color(0.12f, 0.12f, 0.14f));
        TinyFont.Draw(sheet, $"{how} {n} MASK", gap, 4, 3, new Color(0.9f, 0.9f, 0.85f));

        var histogram = new SortedDictionary<int, int>();
        float extentSum = 0, extentLo = float.MaxValue, extentHi = 0;
        for (int i = 0; i < GallerySeeds; i++)
        {
            int seed = FirstSeed + i;
            bool[,] mask = Footprint.BuildMask(seed, p, how);
            int masses = Label(n, (x, z) => mask[x, z], new int[n, n]);
            float extent = 100f * Footprint.ExtentShare(mask);
            histogram.TryGetValue(masses, out int had);
            histogram[masses] = had + 1;
            extentSum += extent;
            extentLo = MathF.Min(extentLo, extent);
            extentHi = MathF.Max(extentHi, extent);

            var img = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                img.SetPixel(x, z, mask[x, z] ? new Color(0.8f, 0.8f, 0.72f) : DevPalette.Aether);
            img.Resize(tile, tile, Image.Interpolation.Nearest);
            int px = gap + (i % columns) * (tile + gap);
            int pz = titleH + gap + (i / columns) * (tile + caption + gap);
            sheet.BlitRect(img, new Rect2I(0, 0, tile, tile), new Vector2I(px, pz));
            TinyFont.Draw(sheet, $"{seed} {masses}m {extent:0}%", px, pz + tile + 2, font,
                          new Color(0.85f, 0.85f, 0.8f));
        }
        sheet.SavePng($"{Gallery}/{how}_{n}_mask.png");
        string counts = string.Join(", ", histogram.Select(kv => $"{kv.Value} x {kv.Key}"));
        GD.Print($"masks:   {how,-14} {GallerySeeds} seeds at {n}²: landmasses {counts}"
            + $" | extent% mean {extentSum / GallerySeeds:0.0} range {extentLo:0}-{extentHi:0}"
            + " (the fit band wants 55-85)");
    }

    /// <summary>Land columns, wet or dry.</summary>
    private static long LandCount(IslandData d)
    {
        int n = d.Size;
        long land = 0;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            if (d.HasLand(x, z)) land++;
        return land;
    }

    /// <summary>The landmass's bounding box as a percentage of the grid (the footprint wants 55-85).</summary>
    private static float ExtentPercent(IslandData d)
    {
        int n = d.Size;
        int xLo = n, xHi = -1, zLo = n, zHi = -1;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            if (x < xLo) xLo = x;
            if (x > xHi) xHi = x;
            if (z < zLo) zLo = z;
            if (z > zHi) zHi = z;
        }
        return xHi < 0 ? 0 : 100f * (xHi - xLo + 1) * (zHi - zLo + 1) / (n * (float)n);
    }

    /// <summary>Top view, 3x nearest: land an elevation ramp with beach tint and gold landings, water by kind, Gates one red (hanging) or orange (land) pixel.</summary>
    private static void SavePortrait(IslandData d, string path)
    {
        Image img = Portrait(d);
        int n = d.Size;
        img.Resize(n * 3, n * 3, Image.Interpolation.Nearest);
        img.SavePng(path);
    }

    /// <summary>The portrait at one pixel a cell, before any scaling.</summary>
    private static Image Portrait(IslandData d)
    {
        int n = d.Size;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);

        short lo = short.MaxValue, hi = short.MinValue;
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            if (!d.HasLand(x, z)) continue;
            short top = d.SurfaceLevel(x, z);
            lo = Math.Min(lo, top);
            hi = Math.Max(hi, top);
        }

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Color c;
            if (!d.HasLand(x, z)) c = DevPalette.Aether;
            else if (d.WaterLevel[x, z] != IslandData.NoLand) c = WaterTint(d, x, z);
            else
            {
                float t = hi > lo ? (d.SurfaceLevel(x, z) - lo) / (float)(hi - lo) : 0.5f;
                c = new Color(0.2f, 0.32f, 0.16f).Lerp(new Color(0.85f, 0.8f, 0.66f), t);
                if (d.Beach[x, z]) c = c.Lerp(new Color(0.9f, 0.85f, 0.55f), 0.5f);
                if (d.Landings[x, z]) c = new Color(0.95f, 0.82f, 0.25f);
            }
            img.SetPixel(x, z, c);
        }

        foreach (Gate g in d.Gates)
        {
            var tint = g.Kind == GateKind.Hanging
                ? new Color(1f, 0.2f, 0.2f)
                : new Color(1f, 0.5f, 0.15f);
            int gx = Math.Clamp(g.Center.X, 0, n - 1);
            int gz = Math.Clamp(g.Center.Z, 0, n - 1);
            img.SetPixel(gx, gz, tint);
        }
        return img;
    }

    /// <summary>Habitat, anchor and surface PNGs for seeds FirstSeed .. FirstSeed + 5 (consecutive, not the sweep stride).</summary>
    private void WriteFieldMaps()
    {
        DirAccess.MakeDirRecursiveAbsolute(FieldMaps);
        int wrote = 0;
        for (int i = 0; i < 6; i++)
        {
            int seed = FirstSeed + i;
            IslandData d = IslandGenerator.Generate(seed, Params);
            SaveHabitat(d, $"{FieldMaps}/habitat_{seed}.png");
            SaveAnchors(d, $"{FieldMaps}/anchors_{seed}.png");
            SaveSurface(d, $"{FieldMaps}/surface_{seed}.png");
            wrote += 3;
        }
        GD.Print($"field maps: {wrote} written to {FieldMaps}");
    }

    /// <summary>The five habitat axes as two-colour ramps side by side; rim distance clamps at 40 cells.</summary>
    private static void SaveHabitat(IslandData d, string path)
    {
        int n = d.Size;
        const int gap = 2;
        var img = Image.CreateEmpty(5 * n + 4 * gap, n, false, Image.Format.Rgb8);
        img.Fill(new Color(0.05f, 0.05f, 0.07f));

        void Panel(int index, Func<int, int, float> value, Color lo, Color hi)
        {
            int left = index * (n + gap);
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                img.SetPixel(left + x, z,
                    d.HasLand(x, z) ? lo.Lerp(hi, value(x, z)) : DevPalette.Aether);
        }

        Panel(0, (x, z) => d.Moisture[x, z] / 255f, DevPalette.MoistureRamp.Lo, DevPalette.MoistureRamp.Hi);
        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
            img.SetPixel(n + gap + x, z,
                d.HasLand(x, z) ? DevPalette.WarmthTint(d.Warmth[x, z]) : DevPalette.Aether);
        Panel(2, (x, z) => d.Ruggedness[x, z] / 255f, DevPalette.RuggedRamp.Lo, DevPalette.RuggedRamp.Hi);
        Panel(3, (x, z) => d.Exposure[x, z] / 255f, DevPalette.ExposureRamp.Lo, DevPalette.ExposureRamp.Hi);
        Panel(4, (x, z) => Math.Min(1f, d.RimDistance[x, z] / 40f), DevPalette.RimRamp.Lo, DevPalette.RimRamp.Hi);

        img.Resize((5 * n + 4 * gap) * 3, n * 3, Image.Interpolation.Nearest);
        img.SavePng(path);
    }

    /// <summary>The anchor kinds over a dimmed base, rarer and more built kinds painted last; top-down, so an overhang is its lip.</summary>
    private static void SaveAnchors(IslandData d, string path)
    {
        int n = d.Size;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Color c;
            if (!d.HasLand(x, z)) c = DevPalette.Aether;
            else if (d.WaterLevel[x, z] != IslandData.NoLand && d.Fluid[x, z] == (byte)FluidKind.Goo)
                c = DevPalette.Anchor(DevPalette.GooBed);
            else c = DevPalette.Anchor(0);
            img.SetPixel(x, z, c);
        }

        void Mark(IEnumerable<Vector2I> cells, Color c)
        {
            foreach (Vector2I p in cells) img.SetPixel(p.X, p.Y, c);
        }
        void MarkMask(bool[,] mask, Color c)
        {
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
                if (mask[x, z]) img.SetPixel(x, z, c);
        }

        Mark(d.RiverBedCells, DevPalette.Anchor(DevPalette.RiverBed));
        Mark(d.LakeBedCells, DevPalette.Anchor(DevPalette.LakeBed));
        Mark(d.CoastCells, DevPalette.Anchor(DevPalette.Coast));
        Mark(d.CliffFootCells, DevPalette.Anchor(DevPalette.CliffFoot));
        var feet = new HashSet<Vector2I>(d.CliffFootCells);
        foreach (Vector2I p in d.CliffCells)
            img.SetPixel(p.X, p.Y, DevPalette.Anchor(feet.Contains(p) ? DevPalette.Ledge : DevPalette.Brink));
        Mark(d.BankCells, DevPalette.Anchor(DevPalette.Bank));
        MarkMask(d.Beach, DevPalette.Anchor(DevPalette.Beach));
        MarkMask(d.Ford, DevPalette.Anchor(DevPalette.Ford));
        MarkMask(d.Landings, DevPalette.Anchor(DevPalette.Landing));
        MarkMask(d.Ferry, DevPalette.Anchor(DevPalette.Quay));
        Mark(d.Overhangs, DevPalette.Anchor(DevPalette.Overhang));
        Mark(d.Summits, DevPalette.Anchor(DevPalette.Summit));

        img.Resize(n * 3, n * 3, Image.Interpolation.Nearest);
        img.SavePng(path);
    }

    /// <summary>The surface mapping in the lab's material palette; water by kind.</summary>
    private static void SaveSurface(IslandData d, string path)
    {
        int n = d.Size;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Color c;
            if (!d.HasLand(x, z)) c = DevPalette.Aether;
            else if (d.WaterLevel[x, z] != IslandData.NoLand) c = WaterTint(d, x, z);
            else c = DevPalette.Material((SurfaceMaterial)d.Material[x, z]);
            img.SetPixel(x, z, c);
        }

        img.Resize(n * 3, n * 3, Image.Interpolation.Nearest);
        img.SavePng(path);
    }

    /// <summary>A digit height map (tenths of the local range) of the biggest patch of each sculpted landform on a Single island.</summary>
    private void PrintSculpts()
    {
        var wanted = new (TerrainCharacter Character, LandformType Form)[]
        {
            (TerrainCharacter.Badlands, LandformType.Badlands),
            (TerrainCharacter.Karst, LandformType.Karst),
            (TerrainCharacter.Massif, LandformType.Massif),
            (TerrainCharacter.Dunes, LandformType.Dunes),
            (TerrainCharacter.Karst, LandformType.Sinkholes),
        };

        foreach ((TerrainCharacter character, LandformType form) in wanted)
        {
            IslandParams p = Variant(q => { q.Character = character; q.Arrangement = IslandArrangement.Single; });
            IslandData d = IslandGenerator.Generate(FirstSeed, p);
            int n = d.Size;

            // The biggest patch of the landform, ties to the first found (x-major), and its middle.
            var area = new Dictionary<int, (int Cells, int SumX, int SumZ)>();
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (!d.HasLand(x, z) || (LandformType)d.Landform[x, z] != form) continue;
                area.TryGetValue(d.Region[x, z], out var had);
                area[d.Region[x, z]] = (had.Cells + 1, had.SumX + x, had.SumZ + z);
            }
            if (area.Count == 0) { GD.Print($"--- {form}: none on seed {FirstSeed} ---"); continue; }

            int best = -1;
            foreach (var (r, v) in area) if (best < 0 || v.Cells > area[best].Cells) best = r;
            var (cells, sumX, sumZ) = area[best];
            int cx = sumX / cells, cz = sumZ / cells;

            const int Half = 26;
            int x0 = Math.Max(0, cx - Half), x1 = Math.Min(n - 1, cx + Half);
            int z0 = Math.Max(0, cz - Half), z1 = Math.Min(n - 1, cz + Half);

            short low = short.MaxValue, high = short.MinValue;
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!d.HasLand(x, z)) continue;
                low = Math.Min(low, d.SurfaceLevel(x, z));
                high = Math.Max(high, d.SurfaceLevel(x, z));
            }
            if (low > high) continue;

            GD.Print($"--- {form} on a {character} island, seed {FirstSeed}: "
                + $"{cells} cells, heights {low}..{high} slabs ---");
            GD.Print("    each digit is a tenth of that range; ':' is off the patch, '.' is aether");
            for (int z = z0; z <= z1; z++)
            {
                var row = new System.Text.StringBuilder();
                for (int x = x0; x <= x1; x++)
                {
                    if (!d.HasLand(x, z)) { row.Append('.'); continue; }
                    int step = Math.Clamp((d.SurfaceLevel(x, z) - low) * 10 / Math.Max(1, high - low),
                                          0, 9);
                    row.Append((LandformType)d.Landform[x, z] == form
                        ? (char)('0' + step)
                        : ':');
                }
                GD.Print(row.ToString());
            }
        }
    }
}
