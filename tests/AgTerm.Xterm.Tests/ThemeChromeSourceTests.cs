using System.Text.RegularExpressions;

namespace AgTerm.Xterm.Tests;

// upstream WindowContentView: the title bar and the sidebar are clear over the window background,
// which is the global terminal background; text and lines are the terminal foreground. The WinUI
// types are not compiled by the test project, so the wiring is pinned in source.
public sealed class ThemeChromeSourceTests
{
    [Fact]
    public void Main_window_declares_one_brush_per_chrome_color()
    {
        var xaml = Source("MainWindow.xaml");

        foreach (var key in new[]
                 {
                     "ChromeBackgroundBrush", "ChromeForegroundBrush", "ChromeSecondaryBrush",
                     "ChromeHairlineBrush", "ChromeGroupDividerBrush",
                 })
        {
            Assert.Contains($"<SolidColorBrush x:Key=\"{key}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Sidebar_and_title_bar_paint_the_theme_background_not_mica_layers()
    {
        var window = Source("MainWindow.xaml");
        var sidebar = Source("SidebarView.xaml");

        Assert.DoesNotContain("LayerOnMicaBaseAlt", window, StringComparison.Ordinal);
        Assert.DoesNotContain("LayerOnMicaBaseAlt", sidebar, StringComparison.Ordinal);
        Assert.Contains("PaneBackground=\"{StaticResource ChromeBackgroundBrush}\"", window, StringComparison.Ordinal);
        Assert.Contains("Background=\"{StaticResource ChromeBackgroundBrush}\"",
            Between(window, "<TitleBar x:Name=\"AppTitleBar\"", ">"), StringComparison.Ordinal);
        // one background layer per area: a translucent theme color stacked twice would be darker
        Assert.Contains("<Grid Background=\"Transparent\">", Between(window, "<SplitView.Pane>", "</SplitView.Pane>"),
            StringComparison.Ordinal);
        Assert.Contains("<Grid x:Name=\"SidebarRoot\" Background=\"Transparent\">", sidebar, StringComparison.Ordinal);
    }

    [Fact]
    public void Title_bar_text_glyphs_and_dividers_use_the_chrome_brushes()
    {
        var xaml = Source("MainWindow.xaml");
        var titleBarResources = Between(xaml, "<TitleBar.Resources>", "</TitleBar.Resources>");

        foreach (var key in new[]
                 {
                     "TitleBarForegroundBrush",
                     "TitleBarPaneToggleButtonForeground", "TitleBarPaneToggleButtonForegroundPointerOver",
                     "TitleBarPaneToggleButtonForegroundPressed", "ButtonForeground",
                     "ButtonForegroundPointerOver", "ButtonForegroundPressed",
                 })
        {
            Assert.Contains($"<StaticResource x:Key=\"{key}\" ResourceKey=\"ChromeForegroundBrush\" />",
                titleBarResources, StringComparison.Ordinal);
        }
        foreach (var key in new[]
                 {
                     "TitleBarSubtitleForegroundBrush", "TitleBarDeactivatedForegroundBrush",
                     "TitleBarSubtitleDeactivatedForegroundBrush",
                 })
        {
            Assert.Contains($"<StaticResource x:Key=\"{key}\" ResourceKey=\"ChromeSecondaryBrush\" />",
                titleBarResources, StringComparison.Ordinal);
        }

        Assert.Contains("<Setter Property=\"Stroke\" Value=\"{StaticResource ChromeForegroundBrush}\" />",
            Between(xaml, "<Style x:Key=\"TitleBarGlyphStyle\"", "</Style>"), StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Fill\" Value=\"{StaticResource ChromeGroupDividerBrush}\" />",
            Between(xaml, "<Style x:Key=\"TitleBarDividerStyle\"", "</Style>"), StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource ChromeSecondaryBrush}\"",
            Between(xaml, "<TextBlock x:Name=\"ToolbarSubtitle\"", "/>"), StringComparison.Ordinal);
        Assert.DoesNotContain("Fill=\"{ThemeResource TextFillColorPrimaryBrush}\"", xaml, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(xaml, Regex.Escape("Fill=\"{StaticResource ChromeForegroundBrush}\"")).Count);
    }

    [Fact]
    public void A_hairline_under_the_title_bar_and_the_sidebar_divider_use_the_hairline_brush()
    {
        var xaml = Source("MainWindow.xaml");

        var hairline = Between(xaml, "<Border x:Name=\"TitleBarHairline\"", "/>");
        Assert.Contains("Grid.Row=\"0\"", hairline, StringComparison.Ordinal);
        Assert.Contains("Height=\"1\"", hairline, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment=\"Bottom\"", hairline, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", hairline, StringComparison.Ordinal);
        Assert.Contains("Background=\"{StaticResource ChromeHairlineBrush}\"", hairline, StringComparison.Ordinal);
        Assert.Contains("<Setter Target=\"TitleBarHairline.Visibility\" Value=\"Collapsed\" />",
            Between(xaml, "<VisualState x:Name=\"TerminalOnlyPresentation\">", "</VisualState>"), StringComparison.Ordinal);

        Assert.Contains("Background=\"{StaticResource ChromeHairlineBrush}\"",
            Between(xaml, "<local:SidebarResizeHandle x:Name=\"SidebarDivider\"", "</local:SidebarResizeHandle>"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("DividerStrokeColorDefaultBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_theme_presentation_recolors_the_chrome()
    {
        var code = Source("MainWindow.xaml.cs");

        var show = MethodBody(code, "private async Task ShowThemePresentationAsync(");
        Assert.Contains("ApplyChromeColors(presentation, settings);", show, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyThemeChrome(", code, StringComparison.Ordinal);

        // the first paint, once the AppWindow exists for the caption buttons
        var constructor = code[code.IndexOf("public MainWindow(", StringComparison.Ordinal)..];
        var appWindow = constructor.IndexOf("_appWindow = AppWindow.GetFromWindowId(", StringComparison.Ordinal);
        var firstApply = constructor.IndexOf("ApplyChromeColors(initialTheme, _settings);", StringComparison.Ordinal);
        Assert.True(appWindow >= 0 && firstApply > appWindow, "the first ApplyChromeColors runs after _appWindow is set");
    }

    [Fact]
    public void Apply_chrome_sets_the_brushes_the_sidebar_and_the_caption_buttons()
    {
        var apply = MethodBody(Source("MainWindow.xaml.cs"), "private void ApplyChromeColors(");

        Assert.Contains("ChromePalette.From(presentation, settings)", apply, StringComparison.Ordinal);
        Assert.Contains("ChromePalette.HighContrast(", apply, StringComparison.Ordinal);
        Assert.Contains("_accessibilitySettings.HighContrast", apply, StringComparison.Ordinal);
        Assert.Contains("RootGrid.RequestedTheme = palette.UseDarkChrome ? ElementTheme.Dark : ElementTheme.Light;",
            apply, StringComparison.Ordinal);
        foreach (var (key, color) in new[]
                 {
                     ("ChromeBackgroundBrush", "palette.Background"),
                     ("ChromeForegroundBrush", "palette.Foreground"),
                     ("ChromeSecondaryBrush", "palette.Secondary"),
                     ("ChromeHairlineBrush", "palette.Hairline"),
                     ("ChromeGroupDividerBrush", "palette.GroupDivider"),
                 })
        {
            Assert.Contains($"SetChromeBrush(\"{key}\", {color});", apply, StringComparison.Ordinal);
        }
        Assert.Contains("Sidebar.ApplyChrome(palette);", apply, StringComparison.Ordinal);
        Assert.Contains("_appWindow.TitleBar.PreferredTheme = palette.UseDarkChrome ? TitleBarTheme.Dark : TitleBarTheme.Light;",
            apply, StringComparison.Ordinal);
        Assert.Contains("_appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;", apply, StringComparison.Ordinal);
        Assert.Contains("_appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void High_contrast_changes_reapply_the_chrome_on_the_ui_thread_until_close()
    {
        var code = Source("MainWindow.xaml.cs");

        // AccessibilitySettings.HighContrastChanged throws 0x80070490 outside a CoreWindow app; a
        // high contrast switch changes the system colors, so UISettings.ColorValuesChanged carries it
        Assert.DoesNotContain("HighContrastChanged", code, StringComparison.Ordinal);
        var handler = MethodBody(code, "private void OnSystemColorValuesChanged(");
        Assert.Contains("DispatcherQueue.TryEnqueue(", handler, StringComparison.Ordinal);
        Assert.Contains("_closedCompletion.Task.IsCompleted", handler, StringComparison.Ordinal);
        Assert.Contains("ApplyChromeColors(_chromePresentation, _chromeSettings);", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_takes_the_chrome_palette_with_its_shift_aware_theme_and_wash()
    {
        var view = Source("SidebarView.xaml.cs");
        var apply = MethodBody(view, "public void ApplyChrome(ChromePalette palette)");

        Assert.Contains("RequestedTheme = palette.SidebarIsDark ? ElementTheme.Dark : ElementTheme.Light;", apply,
            StringComparison.Ordinal);
        Assert.Contains("SetBrush(\"SidebarForegroundBrush\", palette.Foreground);", apply, StringComparison.Ordinal);
        Assert.Contains("SetBrush(\"SidebarSecondaryBrush\", palette.Secondary);", apply, StringComparison.Ordinal);
        Assert.Contains("SetBrush(\"SidebarHoverBrush\", palette.Hairline);", apply, StringComparison.Ordinal);
        Assert.Contains("SetBrush(\"SidebarPillBrush\", palette.PillFill);", apply, StringComparison.Ordinal);
        Assert.Contains("SetBrush(\"SidebarWashBrush\", palette.SidebarWash);", apply, StringComparison.Ordinal);
        // the wash comes from the palette now, not from a second read of the settings
        Assert.DoesNotContain("ApplyBackgroundShiftOverlay", view, StringComparison.Ordinal);
        Assert.Contains("Background=\"{StaticResource SidebarWashBrush}\"",
            Between(Source("SidebarView.xaml"), "<Border x:Name=\"BackgroundShiftOverlay\"", "/>"), StringComparison.Ordinal);
    }

    [Fact]
    public void Default_rows_draw_text_and_icons_in_the_theme_foreground()
    {
        var xaml = Source("SidebarView.xaml");
        var resources = Between(xaml, "<UserControl.Resources>", "</UserControl.Resources>");

        foreach (var key in new[]
                 {
                     "ListViewItemForeground", "ListViewItemForegroundPointerOver", "ListViewItemForegroundSelected",
                     "ListViewItemForegroundPressed", "ListViewItemForegroundSelectedPointerOver",
                     "ListViewItemForegroundSelectedPressed",
                 })
        {
            Assert.Contains($"<StaticResource x:Key=\"{key}\" ResourceKey=\"SidebarForegroundBrush\" />", resources,
                StringComparison.Ordinal);
        }
        Assert.DoesNotContain("TextFillColorSecondaryBrush", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TextFillColorPrimaryBrush", xaml, StringComparison.Ordinal);

        // a flagged default row fills its icon in the theme foreground, not white
        var view = Source("SidebarView.xaml.cs");
        Assert.Contains("_rowIconFill.Color = ToColor(palette.Foreground with { A = 0x60 });",
            MethodBody(view, "public void ApplyChrome(ChromePalette palette)"), StringComparison.Ordinal);
        Assert.Contains("DefaultIconFill = _rowIconFill", view, StringComparison.Ordinal);
        Assert.Contains("DefaultIconFill", Between(Source("SidebarRowViewModel.cs"), "public Brush IconFill =>", ";"),
            StringComparison.Ordinal);
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"{start} not found");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to >= 0, $"{end} not found after {start}");
        return source[from..to];
    }

    // from the signature to the first line that closes a member at class indentation
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static string Source(string appFile) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", appFile));

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
