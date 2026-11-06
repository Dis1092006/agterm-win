using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

// The session info line under a sidebar row's name (upstream 80f822bf SidebarInfoCell and RowRendering).
public sealed class SidebarInfoLineTests
{
    private static readonly string[] Ansi =
    [
        "#000000", "#cc0000", "#4e9a06", "#c4a000", "#3465a4", "#75507b", "#06989a", "#d3d7cf",
        "#555753", "#ef2929", "#8ae234", "#fce94f", "#729fcf", "#ad7fa8", "#34e2e2", "#eeeeec",
    ];

    private static readonly SidebarTintInks ThemeInks = SidebarTintInks.FromAnsi(Ansi)!;
    private static readonly SidebarInfoInks Inks = new("#d0d0d0", ThemeInks);

    [Theory]
    [InlineData(13, 11.5, 16, 44, 22)]
    [InlineData(9, 9, 12, 36, 18)]
    [InlineData(10, 9, 12, 37, 19)]
    [InlineData(20, 18.5, 25, 60, 29)]
    public void The_line_is_a_smaller_font_and_the_row_grows_by_its_line_height(
        double baseSize, double font, double line, double row, double top)
    {
        Assert.Equal(font, SidebarInfoMetrics.FontSize(baseSize));
        Assert.Equal(line, SidebarInfoMetrics.LineHeight(baseSize));
        Assert.Equal(row, SidebarInfoMetrics.RowHeight(baseSize));
        Assert.Equal(AppSettings.SidebarRowHeight(baseSize) + line, SidebarInfoMetrics.RowHeight(baseSize));
        Assert.Equal(top, SidebarInfoMetrics.Top(baseSize));
    }

    [Fact]
    public void A_row_style_line_takes_the_row_theme_and_any_other_the_sidebar()
    {
        var row = new SidebarRowColors("#101010", "#e0e0e0", ThemeInks);
        var sidebarInks = SidebarTintInks.Light;

        Assert.Equal(new SidebarInfoInks("#e0e0e0", ThemeInks),
            SidebarInfoInks.For(new SidebarRowLook(row, null), "#222222", sidebarInks));
        Assert.Equal(new SidebarInfoInks("#222222", sidebarInks),
            SidebarInfoInks.For(new SidebarRowLook(null, row), "#222222", sidebarInks));
        Assert.Equal(new SidebarInfoInks("#222222", sidebarInks),
            SidebarInfoInks.For(SidebarRowLook.Default, "#222222", sidebarInks));
        Assert.Equal(new SidebarInfoInks("#e0e0e0", sidebarInks),
            SidebarInfoInks.For(new SidebarRowLook(row with { Inks = null }, null), "#222222", sidebarInks));
    }

    [Fact]
    public void A_name_tint_never_reaches_the_line()
    {
        var look = new SidebarRowLook(null, null) { Tint = new SidebarRowTint("#ff0000", "#ff0000") };

        Assert.Equal("#222222", SidebarInfoInks.For(look, "#222222", ThemeInks).Foreground);
    }

    [Theory]
    [InlineData(false, false, 0.62)]
    [InlineData(false, true, 0.38)]
    [InlineData(true, false, 0.85)]
    [InlineData(true, true, 0.6)]
    public void Uncolored_text_is_the_foreground_at_the_selection_and_dim_opacity(bool selected, bool dim, double opacity)
    {
        var run = Assert.Single(SidebarInfoLine.Runs([new SessionInfoSpan("idle", Dim: dim)], Inks, selected));

        Assert.Equal(new SidebarInfoRun("idle", "#d0d0d0", opacity), run);
    }

    [Theory]
    [InlineData(false, false, 1.0)]
    [InlineData(false, true, 0.55)]
    [InlineData(true, false, 1.0)]
    [InlineData(true, true, 0.55)]
    public void A_colored_span_keeps_its_ink_selected_or_not(bool selected, bool dim, double opacity)
    {
        var run = Assert.Single(SidebarInfoLine.Runs(
            [new SessionInfoSpan("fail", Dim: dim, Color: TintColor.Red)], Inks, selected));

        Assert.Equal(new SidebarInfoRun("fail", "#cc0000", opacity), run);
    }

    [Fact]
    public void The_runs_carry_the_span_styles_in_order()
    {
        var info = SessionInfo.Make("**3** _agents_ `ci` ^[ok](color: \"green\")", true, 0, out _)!;

        var runs = SidebarInfoLine.Runs(info.Spans, Inks, selected: false);

        Assert.Equal(
            [
                new SidebarInfoRun("3", "#d0d0d0", 0.62, Bold: true),
                new SidebarInfoRun(" ", "#d0d0d0", 0.62),
                new SidebarInfoRun("agents", "#d0d0d0", 0.62, Italic: true),
                new SidebarInfoRun(" ", "#d0d0d0", 0.62),
                new SidebarInfoRun("ci", "#d0d0d0", 0.62, Code: true),
                new SidebarInfoRun(" ", "#d0d0d0", 0.62),
                new SidebarInfoRun("ok", "#4e9a06", 1.0),
            ],
            runs);
    }

    [Fact]
    public void A_chrome_color_writes_its_hex()
    {
        Assert.Equal("#0a0b0c", new ChromeColor(0x80, 0x0A, 0x0B, 0x0C).Hex);
    }
}
