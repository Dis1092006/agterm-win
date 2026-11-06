namespace AgTerm.Xterm.Tests;

// SidebarRowViewModel and the views are WinUI types the test project does not compile, so these
// tests pin the info line's wiring in their source.
public sealed class SidebarInfoLineSourceTests
{
    [Fact]
    public void The_title_keeps_the_plain_row_band_and_the_line_sits_under_it()
    {
        var xaml = Source("SidebarView.xaml");
        var line = Between(xaml, "local:SidebarInfoText.Runs=", "/>");

        Assert.Contains("<Grid ColumnSpacing=\"6\" Margin=\"{x:Bind ContentMargin, Mode=OneWay}\" Height=\"{x:Bind TitleBandHeight, Mode=OneWay}\" VerticalAlignment=\"Top\">",
            xaml, StringComparison.Ordinal);
        Assert.Contains("\"{x:Bind InfoRuns, Mode=OneWay}\"", line, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind InfoVisibility, Mode=OneWay}\"", line, StringComparison.Ordinal);
        Assert.Contains("Margin=\"{x:Bind InfoMargin, Mode=OneWay}\"", line, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"{x:Bind InfoFontSize, Mode=OneWay}\"", line, StringComparison.Ordinal);
        Assert.Contains("LineHeight=\"{x:Bind InfoLineHeight, Mode=OneWay}\"", line, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Top\"", line, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"NoWrap\"", line, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", line, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"session-info\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_row_model_gates_the_line_and_grows_the_row_with_it()
    {
        var model = Source("SidebarRowViewModel.cs");

        Assert.Contains("SessionInfoDisplays.Shows(_settings.EffectiveSessionInfoDisplay, _mode)", model, StringComparison.Ordinal);
        Assert.Contains("ShowsInfo ? SidebarInfoMetrics.RowHeight(FontSize) : AppSettings.SidebarRowHeight(FontSize)",
            model, StringComparison.Ordinal);
        Assert.Contains("SidebarInfoLine.Runs(info.Spans, _look.Info, IsListSelected)", model, StringComparison.Ordinal);
        Assert.Contains("SidebarInfoMetrics.Top(FontSize)", Between(model, "public Thickness InfoMargin", ";"), StringComparison.Ordinal);
        var update = MethodBody(model, "public void Update(");
        foreach (var name in new[] { "TitleBandHeight", "InfoVisibility", "InfoRuns", "InfoMargin", "InfoFontSize", "InfoLineHeight" })
            Assert.Contains($"nameof({name})", update, StringComparison.Ordinal);
        Assert.Contains("nameof(InfoRuns)", Between(model, "public bool IsListSelected", "public Brush? RowBackground"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_runs_property_builds_styled_inlines_and_a_tooltip_only_when_trimmed()
    {
        var text = Source("SidebarInfoText.cs");

        Assert.Contains("IsTextTrimmedChanged", text, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip(", text, StringComparison.Ordinal);
        Assert.Contains("FontWeights.SemiBold", text, StringComparison.Ordinal);
        Assert.Contains("FontStyle.Italic", text, StringComparison.Ordinal);
        Assert.Contains("\"Cascadia Mono, Consolas\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_view_gives_each_row_the_inks_under_its_line_and_recolors_on_a_foreground_change()
    {
        var view = Source("SidebarView.xaml.cs");

        Assert.Contains("Info = SidebarInfoInks.For(look, _sidebarForeground, _sidebarInks)", MethodBody(view, "private SidebarRowLook LookOf("),
            StringComparison.Ordinal);
        var chrome = MethodBody(view, "public void ApplyChrome(");
        Assert.Contains("palette.Foreground.Hex", chrome, StringComparison.Ordinal);
        Assert.Contains("RecolorRows();", chrome, StringComparison.Ordinal);
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"{start} not found");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to >= 0, $"{end} not found after {start}");
        return source[from..to];
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static string Source(string appFile)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "AgTerm.App", appFile);
        Assert.True(File.Exists(path), $"{appFile} not found");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
