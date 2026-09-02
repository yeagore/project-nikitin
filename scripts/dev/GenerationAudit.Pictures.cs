using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectNikitin.Generation;

namespace ProjectNikitin.Dev;

public partial class GenerationAudit
{
    /// <summary>Near-black aether, the same in every picture.</summary>
    private static readonly Color Aether = new(0.07f, 0.08f, 0.11f);

    /// <summary>A flooded column's colour by kind: goo violet, ford pale, navigable deep, stream mid, lake dark.</summary>
    private static Color WaterTint(IslandData d, int x, int z)
    {
        if (d.Fluid[x, z] == (byte)FluidKind.Goo) return new Color(0.58f, 0.16f, 0.74f);
        if (d.Ford[x, z]) return new Color(0.55f, 0.82f, 0.78f);
        if (d.Navigable[x, z]) return new Color(0.12f, 0.42f, 0.68f);
        if (d.River[x, z]) return new Color(0.3f, 0.58f, 0.8f);
        return new Color(0.1f, 0.28f, 0.55f);
    }

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

    /// <summary>Top view, 3x nearest: land an elevation ramp with beach tint and gold landings, water by kind, Gates one red (hanging) or orange (land) pixel.</summary>
    private static void SavePortrait(IslandData d, string path)
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
            if (!d.HasLand(x, z)) c = Aether;
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

        img.Resize(n * 3, n * 3, Image.Interpolation.Nearest);
        img.SavePng(path);
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
                    d.HasLand(x, z) ? lo.Lerp(hi, value(x, z)) : Aether);
        }

        Panel(0, (x, z) => d.Moisture[x, z] / 255f,
              new Color(0.55f, 0.45f, 0.30f), new Color(0.10f, 0.52f, 0.62f));
        Panel(1, (x, z) => d.Warmth[x, z] / 255f,
              new Color(0.88f, 0.92f, 1.00f), new Color(0.85f, 0.48f, 0.18f));
        Panel(2, (x, z) => d.Ruggedness[x, z] / 255f,
              new Color(0.10f, 0.11f, 0.13f), new Color(0.95f, 0.88f, 0.70f));
        Panel(3, (x, z) => d.Exposure[x, z] / 255f,
              new Color(0.14f, 0.30f, 0.20f), new Color(0.92f, 0.93f, 0.85f));
        Panel(4, (x, z) => Math.Min(1f, d.RimDistance[x, z] / 40f),
              new Color(0.85f, 0.55f, 0.90f), new Color(0.10f, 0.12f, 0.22f));

        img.Resize((5 * n + 4 * gap) * 3, n * 3, Image.Interpolation.Nearest);
        img.SavePng(path);
    }

    /// <summary>The ten anchor kinds over a dimmed base, rarer and more built kinds painted last.</summary>
    private static void SaveAnchors(IslandData d, string path)
    {
        int n = d.Size;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgb8);

        for (int x = 0; x < n; x++)
        for (int z = 0; z < n; z++)
        {
            Color c;
            if (!d.HasLand(x, z)) c = Aether;
            else if (d.WaterLevel[x, z] != IslandData.NoLand)
                c = d.Fluid[x, z] == (byte)FluidKind.Goo
                    ? new Color(0.35f, 0.10f, 0.45f)
                    : new Color(0.10f, 0.20f, 0.34f);
            else c = new Color(0.20f, 0.20f, 0.21f);
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

        Mark(d.CoastCells, new Color(0.30f, 0.82f, 0.88f));
        Mark(d.CliffFootCells, new Color(0.90f, 0.55f, 0.20f));
        Mark(d.CliffCells, new Color(0.88f, 0.28f, 0.24f));
        Mark(d.BankCells, new Color(0.30f, 0.75f, 0.55f));
        MarkMask(d.Beach, new Color(0.90f, 0.82f, 0.55f));
        MarkMask(d.Ford, new Color(0.55f, 0.92f, 0.45f));
        MarkMask(d.Landings, new Color(0.98f, 0.86f, 0.25f));
        MarkMask(d.Ferry, new Color(0.35f, 0.55f, 0.95f));
        Mark(d.Overhangs, new Color(0.88f, 0.35f, 0.85f));
        Mark(d.Summits, new Color(1f, 1f, 1f));

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
            if (!d.HasLand(x, z)) c = Aether;
            else if (d.WaterLevel[x, z] != IslandData.NoLand) c = WaterTint(d, x, z);
            else c = (SurfaceMaterial)d.Material[x, z] switch
            {
                SurfaceMaterial.Stone => new Color(0.46f, 0.46f, 0.48f),
                SurfaceMaterial.Scree => new Color(0.62f, 0.60f, 0.55f),
                SurfaceMaterial.Snow => new Color(0.92f, 0.94f, 0.96f),
                SurfaceMaterial.Sand => new Color(0.85f, 0.78f, 0.55f),
                SurfaceMaterial.Silt => new Color(0.52f, 0.44f, 0.32f),
                SurfaceMaterial.Grass => new Color(0.36f, 0.56f, 0.26f),
                SurfaceMaterial.Meadow => new Color(0.50f, 0.64f, 0.30f),
                SurfaceMaterial.Heath => new Color(0.52f, 0.52f, 0.32f),
                _ => new Color(0.68f, 0.58f, 0.42f),
            };
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
