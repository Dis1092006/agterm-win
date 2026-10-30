using System.Xml.Linq;

namespace AgTerm.Xterm.Tests;

public sealed class OverlayPanelSourceTests
{
    [Theory]
    [InlineData("KeybindingsView", "KeybindingsPanel", "#33000000")]
    [InlineData("PaletteView", "PalettePanel", "#33000000")]
    [InlineData("IconPaletteView", "PalettePanel", "#33000000")]
    [InlineData("SessionSwitcherView", "SwitcherPanel", "#33000000")]
    [InlineData("AskView", "Panel", "#33000000")]
    public void Every_panel_uses_the_same_background_policy_without_fading_contents(
        string view, string panel, string scrim)
    {
        var source = Source(view + ".xaml");
        var xaml = XDocument.Parse(source);
        XNamespace names = "http://schemas.microsoft.com/winfx/2006/xaml";
        var border = xaml.Descendants().Single(e => e.Attribute(names + "Name")?.Value == panel);
        Assert.Equal("{StaticResource OverlayPanelStyle}", border.Attribute("Style")?.Value);
        Assert.Null(border.Attribute("Background"));
        Assert.Null(border.Attribute("Opacity"));
        Assert.Contains($"Background=\"{scrim}\"", source);
        var code = Source(view + ".xaml.cs");
        Assert.Contains("public void SetPanelBackground(Brush? background)", code);
        Assert.Contains($"{panel}.ClearValue(Border.BackgroundProperty)", code);
        Assert.Contains($"{panel}.Background = background", code);
    }

    [Fact]
    public void Default_style_keeps_the_exact_current_theme_brush()
    {
        var app = Source("App.xaml");
        Assert.Contains("x:Key=\"OverlayPanelStyle\"", app);
        Assert.Contains("Value=\"{ThemeResource LayerOnMicaBaseAltFillColorDefaultBrush}\"", app);
        var window = Source("MainWindow.xaml");
        Assert.Contains("x:Key=\"OverlayOpaqueBackgroundBrush\"", window);
        Assert.Contains("Color=\"{ThemeResource SolidBackgroundFillColorBase}\"", window);
    }

    [Fact]
    public void Startup_reload_and_system_events_update_all_four_panels_on_the_ui_thread()
    {
        var window = Source("MainWindow.xaml.cs");
        Assert.Contains("_uiSettings.AdvancedEffectsEnabledChanged += OnAdvancedEffectsEnabledChanged", window);
        Assert.Contains("_uiSettings.AdvancedEffectsEnabledChanged -= OnAdvancedEffectsEnabledChanged", window);
        Assert.Contains("ApplyOverlayPanelBackground(_settings);", window);
        Assert.Contains("ApplyOverlayPanelBackground(settings);", window);
        Assert.Contains("settings.EffectiveOverlayOpacity(_uiSettings.AdvancedEffectsEnabled)", window);
        foreach (var view in new[] { "KeybindingsOverlay", "PaletteOverlay", "SwitcherOverlay", "AskOverlay" })
            Assert.Contains($"{view}.SetPanelBackground(background)", window);
        var handler = window[window.IndexOf("private void OnAdvancedEffectsEnabledChanged", StringComparison.Ordinal)..];
        Assert.Contains("DispatcherQueue.TryEnqueue", handler[..handler.IndexOf("\n    }", StringComparison.Ordinal)]);
    }

    private static string Source(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "AgTerm.App", file));
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
