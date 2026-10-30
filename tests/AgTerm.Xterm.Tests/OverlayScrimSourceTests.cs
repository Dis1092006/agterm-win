using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AgTerm.Xterm.Tests;

// upstream WindowContentView at 7df70fe keeps its overlays below the title bar, so their 20% black
// scrim never darkens it. The port keeps a click on the title bar closing the overlay, so the hit
// surface still spans the window and only the dimmed part starts below the title bar.
public sealed class OverlayScrimSourceTests
{
    private static readonly XNamespace Names = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("PaletteView")]
    [InlineData("IconPaletteView")]
    [InlineData("KeybindingsView")]
    public void The_scrim_is_a_separate_layer_that_takes_no_clicks(string view)
    {
        var xaml = XDocument.Parse(Source(view + ".xaml"));
        var scrim = xaml.Descendants().Single(e => e.Attribute(Names + "Name")?.Value == "Scrim");

        Assert.Equal("#33000000", scrim.Attribute("Background")?.Value);
        Assert.Equal("False", scrim.Attribute("IsHitTestVisible")?.Value);
        var surface = scrim.Parent!;
        Assert.Equal("Transparent", surface.Attribute("Background")?.Value);
        Assert.DoesNotContain(xaml.Descendants(), e => e.Attribute("Background")?.Value == "#66000000");
    }

    [Theory]
    [InlineData("PaletteView")]
    [InlineData("IconPaletteView")]
    [InlineData("KeybindingsView")]
    public void The_scrim_starts_below_the_title_bar(string view)
    {
        var code = Source(view + ".xaml.cs");

        Assert.Matches(
            @"public void SetScrimTopInset\(double inset\) =>\s*Scrim\.Margin = new Thickness\(0, inset, 0, 0\);",
            code);
    }

    [Fact]
    public void The_palette_shows_the_scrim_only_when_the_options_ask_for_it()
    {
        var code = Source("PaletteView.xaml.cs");

        Assert.Contains(
            "Scrim.Visibility = options.Dimmed ? Visibility.Visible : Visibility.Collapsed;",
            code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_passes_the_mode_scrim_and_the_title_bar_height_to_the_overlays()
    {
        var window = Source("MainWindow.xaml.cs");
        var xaml = Source("MainWindow.xaml");

        Assert.Contains("Dimmed = snapshot.Dimmed,", window, StringComparison.Ordinal);
        Assert.Contains("SizeChanged=\"OnShellSizeChanged\"", xaml, StringComparison.Ordinal);
        var handler = Regex.Match(
            window,
            @"private void OnShellSizeChanged\(object sender, SizeChangedEventArgs e\)\s*\{(?<body>[^}]*)\}");
        Assert.True(handler.Success, "MainWindow must update the scrim inset when the content row moves.");
        var body = handler.Groups["body"].Value;
        Assert.Contains("RootGrid.RowDefinitions[0].ActualHeight", body, StringComparison.Ordinal);
        foreach (var overlay in new[] { "PaletteOverlay", "IconPaletteOverlay", "KeybindingsOverlay" })
            Assert.Contains($"{overlay}.SetScrimTopInset(", body, StringComparison.Ordinal);
    }

    private static string Source(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "AgTerm.App", file));
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
