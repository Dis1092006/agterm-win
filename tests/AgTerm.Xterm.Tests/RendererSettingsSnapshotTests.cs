using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Validates the mapping rules of RendererSettingsSnapshot.From(AppSettings):
/// null-guards, per-field defaults, clamping, and value equality.
/// Fixed palette and cursor fields (no AppSettings backing) are tested as constants.
/// Theme/DarkTheme color resolution is NOT tested here — D3 owns the catalog.
/// </summary>
public class RendererSettingsSnapshotTests
{
    // ── Null guard ────────────────────────────────────────────────────────────

    [Fact]
    public void From_null_settings_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RendererSettingsSnapshot.From(null!));
    }

    // ── FontFamily ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void FontFamily_null_or_blank_resolves_to_default_stack(string? input)
    {
        var s = RendererSettingsSnapshot.From(new AppSettings { FontFamily = input });
        Assert.Equal("Cascadia Mono, Consolas, monospace", s.FontFamily);
    }

    [Theory]
    [InlineData("Fira Code",        "Fira Code")]
    [InlineData("  JetBrains Mono  ", "JetBrains Mono")]
    [InlineData("Consolas",         "Consolas")]
    public void FontFamily_non_blank_is_trimmed(string input, string expected)
    {
        var s = RendererSettingsSnapshot.From(new AppSettings { FontFamily = input });
        Assert.Equal(expected, s.FontFamily);
    }

    // ── FontSize ──────────────────────────────────────────────────────────────

    [Fact]
    public void FontSize_null_resolves_to_14()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.Equal(14.0, s.FontSize);
    }

    [Theory]
    [InlineData(0,   6)]
    [InlineData(5,   6)]
    [InlineData(6,   6)]
    [InlineData(14, 14)]
    [InlineData(36, 36)]
    [InlineData(72, 72)]
    [InlineData(73, 72)]
    [InlineData(200, 72)]
    public void FontSize_clamps_to_6_through_72(double input, double expected)
    {
        var s = RendererSettingsSnapshot.From(new AppSettings { FontSize = input });
        Assert.Equal(expected, s.FontSize);
    }

    // ── Windows base palette (fixed — no AppSettings fields) ─────────────────

    [Fact]
    public void ForegroundColor_is_windows_default()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.Equal("#CCCCCC", s.ForegroundColor);
    }

    [Fact]
    public void BackgroundColor_is_windows_default()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.Equal("#0C0C0C", s.BackgroundColor);
    }

    [Fact]
    public void CursorColor_is_windows_default()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.Equal("#FFFFFF", s.CursorColor);
    }

    [Fact]
    public void SelectionBackgroundColor_is_windows_default()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.Equal("#FFFFFF40", s.SelectionBackgroundColor);
    }

    // ── BackgroundOpacity ─────────────────────────────────────────────────────

    [Fact]
    public void BackgroundOpacity_null_resolves_to_1()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.Equal(1.0, s.BackgroundOpacity);
    }

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.0,  0.0)]
    [InlineData(0.5,  0.5)]
    [InlineData(1.0,  1.0)]
    [InlineData(1.5,  1.0)]
    public void BackgroundOpacity_clamps_to_0_through_1(double input, double expected)
    {
        var s = RendererSettingsSnapshot.From(new AppSettings { BackgroundOpacity = input });
        Assert.Equal(expected, s.BackgroundOpacity);
    }

    // ── BackgroundBlur ────────────────────────────────────────────────────────

    [Fact]
    public void BackgroundBlur_null_resolves_to_0()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.Equal(0, s.BackgroundBlur);
    }

    [Theory]
    [InlineData(-10,   0)]
    [InlineData(0,     0)]
    [InlineData(50,   50)]
    [InlineData(100, 100)]
    [InlineData(200, 100)]
    public void BackgroundBlur_clamps_to_0_through_100(int input, int expected)
    {
        var s = RendererSettingsSnapshot.From(new AppSettings { BackgroundBlur = input });
        Assert.Equal(expected, s.BackgroundBlur);
    }

    // ── Cursor (fixed behavior — no AppSettings fields) ──────────────────────

    [Fact]
    public void CursorBlink_is_always_true()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.True(s.CursorBlink);
    }

    [Fact]
    public void CursorStyle_is_always_block()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.Equal("block", s.CursorStyle);
    }

    // ── ScrollSensitivity (MouseScrollMultiplier) ─────────────────────────────

    [Fact]
    public void ScrollSensitivity_null_resolves_to_3()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.Equal(3.0, s.ScrollSensitivity);
    }

    [Theory]
    [InlineData(0.0,  0.1)]
    [InlineData(0.1,  0.1)]
    [InlineData(3.0,  3.0)]
    [InlineData(10.0, 10.0)]
    [InlineData(15.0, 10.0)]
    public void ScrollSensitivity_clamps_to_0p1_through_10(double input, double expected)
    {
        var s = RendererSettingsSnapshot.From(new AppSettings { MouseScrollMultiplier = input });
        Assert.Equal(expected, s.ScrollSensitivity);
    }

    // ── Scrollback ────────────────────────────────────────────────────────────

    [Fact]
    public void Scrollback_is_always_10000()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.Equal(10000, s.Scrollback);
    }

    // ── RightClickPaste ───────────────────────────────────────────────────────

    [Fact]
    public void RightClickPaste_null_defaults_to_true()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.True(s.RightClickPaste);
    }

    [Fact]
    public void RightClickPaste_explicit_false_maps_false()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings { RightClickPaste = false });
        Assert.False(s.RightClickPaste);
    }

    [Fact]
    public void RightClickPaste_explicit_true_maps_true()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings { RightClickPaste = true });
        Assert.True(s.RightClickPaste);
    }

    // ── ScreenReaderMode ──────────────────────────────────────────────────────

    [Fact]
    public void ScreenReaderMode_is_always_false()
    {
        // No AppSettings field: enablement is host accessibility state, resolved later.
        var s = RendererSettingsSnapshot.From(new AppSettings());
        Assert.False(s.ScreenReaderMode);
    }

    // ── Value equality ────────────────────────────────────────────────────────

    [Fact]
    public void Equal_settings_produce_equal_snapshots()
    {
        var settings = new AppSettings { FontFamily = "Fira Code", FontSize = 16 };
        Assert.Equal(
            RendererSettingsSnapshot.From(settings),
            RendererSettingsSnapshot.From(settings));
    }

    [Fact]
    public void Different_font_size_produces_unequal_snapshots()
    {
        var a = RendererSettingsSnapshot.From(new AppSettings { FontSize = 12 });
        var b = RendererSettingsSnapshot.From(new AppSettings { FontSize = 16 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_font_family_produces_unequal_snapshots()
    {
        var a = RendererSettingsSnapshot.From(new AppSettings { FontFamily = "Fira Code" });
        var b = RendererSettingsSnapshot.From(new AppSettings { FontFamily = "JetBrains Mono" });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_background_opacity_produces_unequal_snapshots()
    {
        var a = RendererSettingsSnapshot.From(new AppSettings { BackgroundOpacity = 0.5 });
        var b = RendererSettingsSnapshot.From(new AppSettings { BackgroundOpacity = 0.8 });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_right_click_paste_produces_unequal_snapshots()
    {
        var a = RendererSettingsSnapshot.From(new AppSettings { RightClickPaste = true });
        var b = RendererSettingsSnapshot.From(new AppSettings { RightClickPaste = false });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_scroll_sensitivity_produces_unequal_snapshots()
    {
        var a = RendererSettingsSnapshot.From(new AppSettings { MouseScrollMultiplier = 2.0 });
        var b = RendererSettingsSnapshot.From(new AppSettings { MouseScrollMultiplier = 5.0 });
        Assert.NotEqual(a, b);
    }

    // ── Theme text colors ─────────────────────────────────────────────────────

    private static TerminalTheme TextColorTheme() => new(
        "text-colors", "#010101", "#020202", "#030303", "#040404", "#050505", "#060606",
        Enumerable.Repeat("#070707", 16).ToArray());

    [Fact]
    public void From_a_theme_carries_the_cursor_text_and_selection_text_colors()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings(), TextColorTheme());

        Assert.Equal("#040404", s.CursorTextColor);
        Assert.Equal("#060606", s.SelectionForegroundColor);
    }

    [Fact]
    public void Without_a_theme_the_text_colors_are_null()
    {
        var s = RendererSettingsSnapshot.From(new AppSettings());

        Assert.Null(s.CursorTextColor);
        Assert.Null(s.SelectionForegroundColor);
    }
}
