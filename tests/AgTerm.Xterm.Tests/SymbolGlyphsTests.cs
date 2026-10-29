namespace AgTerm.Xterm.Tests;

public sealed class SymbolGlyphsTests
{
    // 0xE707 is in both Segoe Fluent Icons and Segoe MDL2 Assets, so this holds on Windows 10 and 11
    [Fact]
    public void A_code_point_both_symbol_fonts_have_is_present()
    {
        Assert.True(SymbolGlyphs.Has(0xE707));
        Assert.True(SymbolGlyphs.Has(0xE707));
    }

    [Fact]
    public void A_private_use_code_point_no_symbol_font_has_is_missing()
    {
        Assert.False(SymbolGlyphs.Has(0xE000));
    }

    [Fact]
    public void A_code_point_above_the_basic_plane_is_missing_without_throwing()
    {
        Assert.False(SymbolGlyphs.Has(0x1F600));
    }
}
