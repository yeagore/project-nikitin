using System.Collections.Generic;
using Godot;

namespace ProjectNikitin.Dev;

/// <summary>
/// A 5 x 7 bitmap font drawn straight into an <see cref="Image"/>. Headless Godot
/// has no rendering device, so a label on a generated PNG cannot go through a
/// <c>Font</c> or a viewport; the glyphs are pixels here instead. Uppercase only —
/// lowercase is folded up — plus digits and the punctuation a legend needs.
/// </summary>
internal static class TinyFont
{
    /// <summary>Glyph cell: 5 columns, 7 rows, one blank column of advance between glyphs.</summary>
    public const int GlyphWidth = 5, GlyphHeight = 7, Advance = 6;

    /// <summary>
    /// Each glyph as seven rows of five, '#' ink and '.' blank, '/' between rows so a
    /// miscounted row is visible in the source rather than in the picture.
    /// </summary>
    private static readonly Dictionary<char, string> Source = new()
    {
        ['A'] = ".###./#...#/#...#/#####/#...#/#...#/#...#",
        ['B'] = "####./#...#/#...#/####./#...#/#...#/####.",
        ['C'] = ".###./#...#/#..../#..../#..../#...#/.###.",
        ['D'] = "####./#...#/#...#/#...#/#...#/#...#/####.",
        ['E'] = "#####/#..../#..../####./#..../#..../#####",
        ['F'] = "#####/#..../#..../####./#..../#..../#....",
        ['G'] = ".###./#...#/#..../#.###/#...#/#...#/.###.",
        ['H'] = "#...#/#...#/#...#/#####/#...#/#...#/#...#",
        ['I'] = ".###./..#../..#../..#../..#../..#../.###.",
        ['J'] = "..###/...#./...#./...#./...#./#..#./.##..",
        ['K'] = "#...#/#..#./#.#../##.../#.#../#..#./#...#",
        ['L'] = "#..../#..../#..../#..../#..../#..../#####",
        ['M'] = "#...#/##.##/#.#.#/#.#.#/#...#/#...#/#...#",
        ['N'] = "#...#/##..#/#.#.#/#..##/#...#/#...#/#...#",
        ['O'] = ".###./#...#/#...#/#...#/#...#/#...#/.###.",
        ['P'] = "####./#...#/#...#/####./#..../#..../#....",
        ['Q'] = ".###./#...#/#...#/#...#/#.#.#/#..#./.##.#",
        ['R'] = "####./#...#/#...#/####./#.#../#..#./#...#",
        ['S'] = ".####/#..../#..../.###./....#/....#/####.",
        ['T'] = "#####/..#../..#../..#../..#../..#../..#..",
        ['U'] = "#...#/#...#/#...#/#...#/#...#/#...#/.###.",
        ['V'] = "#...#/#...#/#...#/#...#/#...#/.#.#./..#..",
        ['W'] = "#...#/#...#/#...#/#.#.#/#.#.#/##.##/#...#",
        ['X'] = "#...#/#...#/.#.#./..#../.#.#./#...#/#...#",
        ['Y'] = "#...#/#...#/.#.#./..#../..#../..#../..#..",
        ['Z'] = "#####/....#/...#./..#../.#.../#..../#####",
        ['0'] = ".###./#...#/#..##/#.#.#/##..#/#...#/.###.",
        ['1'] = "..#../.##../..#../..#../..#../..#../.###.",
        ['2'] = ".###./#...#/....#/...#./..#../.#.../#####",
        ['3'] = "#####/...#./..##./....#/....#/#...#/.###.",
        ['4'] = "...#./..##./.#.#./#..#./#####/...#./...#.",
        ['5'] = "#####/#..../####./....#/....#/#...#/.###.",
        ['6'] = "..##./.#.../#..../####./#...#/#...#/.###.",
        ['7'] = "#####/....#/...#./..#../.#.../.#.../.#...",
        ['8'] = ".###./#...#/#...#/.###./#...#/#...#/.###.",
        ['9'] = ".###./#...#/#...#/.####/....#/...#./.##..",
        [' '] = "...../...../...../...../...../...../.....",
        ['.'] = "...../...../...../...../...../..##./..##.",
        [','] = "...../...../...../...../..##./..#../.#...",
        [':'] = "...../..##./..##./...../..##./..##./.....",
        ['-'] = "...../...../...../#####/...../...../.....",
        ['/'] = "....#/....#/...#./..#../.#.../#..../#....",
        ['('] = "...#./..#../.#.../.#.../.#.../..#../...#.",
        [')'] = ".#.../..#../...#./...#./...#./..#../.#...",
        ['%'] = "##..#/##..#/...#./..#../.#.../#..##/#..##",
        ['>'] = "#..../.#.../..#../...#./..#../.#.../#....",
        ['<'] = "....#/...#./..#../.#.../..#../...#./....#",
        ['='] = "...../...../#####/...../#####/...../.....",
        ['+'] = "...../..#../..#../#####/..#../..#../.....",
        ['?'] = ".###./#...#/....#/...#./..#../...../..#..",
        ['\''] = "..#../..#../...../...../...../...../.....",
    };

    /// <summary>The rows without their separators, so <see cref="Blit"/> can index straight in.</summary>
    private static readonly Dictionary<char, string> Glyphs = Compile();

    private static Dictionary<char, string> Compile()
    {
        var packed = new Dictionary<char, string>(Source.Count);
        foreach (var (ch, rows) in Source)
        {
            string bits = rows.Replace("/", "");
            if (bits.Length != GlyphWidth * GlyphHeight)
                GD.PushWarning($"TinyFont: glyph '{ch}' is {bits.Length} cells, not "
                    + $"{GlyphWidth * GlyphHeight}");
            packed[ch] = bits;
        }
        return packed;
    }

    /// <summary>How wide <paramref name="text"/> comes out at <paramref name="scale"/>, trailing gap trimmed.</summary>
    public static int Width(string text, int scale)
        => text.Length == 0 ? 0 : (text.Length * Advance - 1) * scale;

    /// <summary>How tall a line is at <paramref name="scale"/>.</summary>
    public static int Height(int scale) => GlyphHeight * scale;

    /// <summary>Draws <paramref name="text"/> with its top-left at (x, z); anything unmapped is a blank.</summary>
    public static void Draw(Image img, string text, int x, int z, int scale, Color c)
    {
        int pen = x;
        foreach (char raw in text)
        {
            char ch = char.ToUpperInvariant(raw);
            if (Glyphs.TryGetValue(ch, out string? bits)) Blit(img, bits, pen, z, scale, c);
            pen += Advance * scale;
        }
    }

    /// <summary>Draws <paramref name="text"/> centred on <paramref name="cx"/>.</summary>
    public static void DrawCentered(Image img, string text, int cx, int z, int scale, Color c)
        => Draw(img, text, cx - Width(text, scale) / 2, z, scale, c);

    /// <summary>Draws <paramref name="text"/> ending at <paramref name="right"/>.</summary>
    public static void DrawRight(Image img, string text, int right, int z, int scale, Color c)
        => Draw(img, text, right - Width(text, scale), z, scale, c);

    /// <summary>One glyph, clipped to the image.</summary>
    private static void Blit(Image img, string bits, int x, int z, int scale, Color c)
    {
        for (int row = 0; row < GlyphHeight; row++)
        for (int col = 0; col < GlyphWidth; col++)
        {
            if (bits[row * GlyphWidth + col] != '#') continue;
            for (int sx = 0; sx < scale; sx++)
            for (int sz = 0; sz < scale; sz++)
            {
                int px = x + col * scale + sx, pz = z + row * scale + sz;
                if (px < 0 || pz < 0 || px >= img.GetWidth() || pz >= img.GetHeight()) continue;
                img.SetPixel(px, pz, c);
            }
        }
    }
}
