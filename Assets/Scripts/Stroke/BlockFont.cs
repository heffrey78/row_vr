using System.Collections.Generic;

namespace RowVR.Stroke
{
    /// <summary>
    /// The sky numbers' lettering (#99): digits built from chunky blocks on a 3x5 grid, the
    /// way a child would stack them. Only what the numbers need: 0-9, ':', '.', '-', and a
    /// space. A thousands comma is a one-column gap ("1 234"): a comma block at that size
    /// reads as a decimal point. Anything else is a space.
    /// </summary>
    public static class BlockFont
    {
        public const int Rows = 5;

        /// <summary>Columns of empty space between glyphs.</summary>
        public const int Gap = 1;

        static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['0'] = new[] { "###", "#.#", "#.#", "#.#", "###" },
            ['1'] = new[] { ".#.", "##.", ".#.", ".#.", "###" },
            ['2'] = new[] { "###", "..#", "###", "#..", "###" },
            ['3'] = new[] { "###", "..#", ".##", "..#", "###" },
            ['4'] = new[] { "#.#", "#.#", "###", "..#", "..#" },
            ['5'] = new[] { "###", "#..", "###", "..#", "###" },
            ['6'] = new[] { "###", "#..", "###", "#.#", "###" },
            ['7'] = new[] { "###", "..#", "..#", ".#.", ".#." },
            ['8'] = new[] { "###", "#.#", "###", "#.#", "###" },
            ['9'] = new[] { "###", "#.#", "###", "..#", "###" },
            [':'] = new[] { ".", "#", ".", "#", "." },
            ['.'] = new[] { ".", ".", ".", ".", "#" },
            ['-'] = new[] { "...", "...", "###", "...", "..." },
            [','] = new[] { ".", ".", ".", ".", "." },
            [' '] = new[] { "..", "..", "..", "..", ".." },
        };

        /// <summary>Whether the font draws this character rather than falling back to a space.</summary>
        public static bool Has(char c) => Glyphs.ContainsKey(c);

        /// <summary>Width of a glyph, columns.</summary>
        public static int Width(char c) => Glyph(c)[0].Length;

        /// <summary>Whether a cell of a glyph is a block; row 0 is the top.</summary>
        public static bool Filled(char c, int column, int row)
        {
            var glyph = Glyph(c);
            if (row < 0 || row >= Rows || column < 0 || column >= glyph[row].Length) return false;
            return glyph[row][column] == '#';
        }

        /// <summary>Width of a whole string, columns, gaps included.</summary>
        public static int Measure(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var columns = 0;
            foreach (var c in text) columns += Width(c);
            return columns + Gap * (text.Length - 1);
        }

        static string[] Glyph(char c) => Glyphs.TryGetValue(c, out var glyph) ? glyph : Glyphs[' '];
    }
}
