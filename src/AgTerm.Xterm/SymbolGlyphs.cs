using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace AgTerm.Xterm;

/// <summary>
/// Whether the machine's symbol fonts draw a code point. SymbolThemeFontFamily resolves to Segoe Fluent
/// Icons, then Segoe MDL2 Assets, and draws a missing glyph as an empty box, so the sidebar asks first.
/// </summary>
public static class SymbolGlyphs
{
    private static readonly string[] Fonts = ["Segoe Fluent Icons", "Segoe MDL2 Assets"];

    private static readonly ConcurrentDictionary<int, bool> Cache = new();

    public static bool Has(int codePoint) => Cache.GetOrAdd(codePoint, Probe);

    private static bool Probe(int codePoint)
    {
        if (codePoint is < 0 or > 0xFFFF) return false;
        var text = ((char)codePoint).ToString();
        foreach (var font in Fonts)
        {
            if (FontHasGlyph(font, text)) return true;
        }
        return false;
    }

    private static bool FontHasGlyph(string family, string text)
    {
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return false;
        var font = IntPtr.Zero;
        var previous = IntPtr.Zero;
        try
        {
            font = CreateFontW(-16, 0, 0, 0, 400, 0, 0, 0, DefaultCharset, 0, 0, 0, 0, family);
            if (font == IntPtr.Zero) return false;
            previous = SelectObject(dc, font);

            // CreateFontW substitutes another font when the family is not installed
            if (!string.Equals(SelectedFace(dc), family, StringComparison.OrdinalIgnoreCase)) return false;

            var indices = new ushort[1];
            var count = GetGlyphIndicesW(dc, text, text.Length, indices, MarkNonexistingGlyphs);
            return count == text.Length && indices[0] != MissingGlyph;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(dc, previous);
            if (font != IntPtr.Zero) DeleteObject(font);
            DeleteDC(dc);
        }
    }

    private static string SelectedFace(IntPtr dc)
    {
        var face = new StringBuilder(LfFaceSize);
        return GetTextFaceW(dc, face.Capacity, face) > 0 ? face.ToString() : "";
    }

    private const uint DefaultCharset = 1;
    private const uint MarkNonexistingGlyphs = 0x0001;
    private const ushort MissingGlyph = 0xFFFF;
    private const int LfFaceSize = 32;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(
        int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision,
        uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetTextFaceW(IntPtr hdc, int count, StringBuilder face);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetGlyphIndicesW(IntPtr hdc, string text, int count, ushort[] indices, uint flags);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
}
