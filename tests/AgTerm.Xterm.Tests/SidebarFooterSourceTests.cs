using System.Xml.Linq;

namespace AgTerm.Xterm.Tests;

// upstream WindowContentView.bottomBar (7df70fe, WindowContentView.swift:741-830): new workspace and
// the New Session menu on the left, a spacer, then the workspace filter toggle and the flagged-view toggle, all
// borderless in the chrome foreground, no line above. Clear Flagged is a command-palette row, as
// upstream, not a footer button. SidebarView is a WinUI type the test project does not compile, so it
// is pinned in source.
public sealed class SidebarFooterSourceTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Footer_orders_the_add_buttons_left_and_the_view_controls_right()
    {
        var footer = Footer();
        var groups = footer.Elements(Xaml + "StackPanel").ToList();

        Assert.Equal(2, groups.Count);
        Assert.Equal(["NewWorkspaceButton", "NewSessionButton"], Names(groups[0]));
        Assert.Equal(["FocusFilterToggle", "FlaggedToggle"], Names(groups[1]));
        Assert.Equal("2", (string?)groups[0].Attribute("Spacing"));
        Assert.Equal("2", (string?)groups[1].Attribute("Spacing"));
        Assert.Equal("1", (string?)groups[1].Attribute("Grid.Column"));
        Assert.Equal("6,4,6,4", (string?)footer.Attribute("Padding"));
    }

    [Fact]
    public void Footer_has_no_line_above_it()
    {
        var footer = Footer();

        Assert.Null(footer.Attribute("BorderThickness"));
        Assert.Null(footer.Attribute("BorderBrush"));
    }

    [Fact]
    public void Footer_buttons_are_borderless_in_the_chrome_foreground()
    {
        var style = Style("SidebarFooterButtonStyle");

        Assert.Equal("Transparent", Setter(style, "Background"));
        Assert.Equal("0", Setter(style, "BorderThickness"));
        Assert.Equal("24", Setter(style, "MinWidth"));
        Assert.Equal("22", Setter(style, "MinHeight"));
        Assert.Equal("0", Setter(style, "Padding"));
        Assert.Equal("{StaticResource SidebarForegroundBrush}", Setter(style, "Foreground"));

        foreach (var name in new[] { "NewWorkspaceButton", "NewSessionButton" })
            Assert.Equal("{StaticResource SidebarFooterButtonStyle}", (string?)Named(name).Attribute("Style"));
        foreach (var name in new[] { "FocusFilterToggle", "FlaggedToggle" })
            Assert.Equal("{StaticResource SidebarFooterToggleStyle}", (string?)Named(name).Attribute("Style"));
        var toggle = Style("SidebarFooterToggleStyle");
        Assert.Equal("Transparent", Setter(toggle, "Background"));
        Assert.Equal("0", Setter(toggle, "BorderThickness"));
    }

    [Fact]
    public void Hover_press_and_checked_states_stay_in_the_chrome_colors()
    {
        var resources = Resources();

        foreach (var key in new[]
                 {
                     "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed",
                     "ButtonForegroundDisabled", "ToggleButtonForeground", "ToggleButtonForegroundPointerOver",
                     "ToggleButtonForegroundPressed", "ToggleButtonForegroundDisabled", "ToggleButtonForegroundChecked",
                     "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed",
                     "ToggleButtonForegroundCheckedDisabled",
                 })
        {
            Assert.Equal("SidebarForegroundBrush", Alias(resources, key));
        }

        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ToggleButtonBackgroundPointerOver",
                     "ToggleButtonBackgroundCheckedPointerOver" })
            Assert.Equal("SidebarHoverBrush", Alias(resources, key));
        foreach (var key in new[] { "ButtonBackgroundPressed", "ToggleButtonBackgroundPressed",
                     "ToggleButtonBackgroundCheckedPressed" })
            Assert.Equal("SidebarPillBrush", Alias(resources, key));
        foreach (var key in new[] { "ButtonBackground", "ButtonBackgroundDisabled", "ToggleButtonBackground",
                     "ToggleButtonBackgroundDisabled", "ToggleButtonBackgroundChecked",
                     "ToggleButtonBackgroundCheckedDisabled" })
            Assert.Equal("SidebarTransparentBrush", Alias(resources, key));
        foreach (var key in new[] { "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed",
                     "ButtonBorderBrushDisabled", "ToggleButtonBorderBrush", "ToggleButtonBorderBrushPointerOver",
                     "ToggleButtonBorderBrushPressed", "ToggleButtonBorderBrushDisabled", "ToggleButtonBorderBrushChecked",
                     "ToggleButtonBorderBrushCheckedPointerOver", "ToggleButtonBorderBrushCheckedPressed",
                     "ToggleButtonBorderBrushCheckedDisabled" })
            Assert.Equal("SidebarTransparentBrush", Alias(resources, key));
    }

    [Fact]
    public void Flag_toggle_is_an_outline_flag_in_the_tree_and_a_filled_flag_in_the_flagged_view()
    {
        var toggle = Named("FlaggedToggle");
        var glyph = toggle.Descendants(Xaml + "Path").Single();

        Assert.Equal("FlaggedGlyph", (string?)glyph.Attribute(X + "Name"));
        Assert.Equal("{StaticResource SidebarForegroundBrush}", (string?)glyph.Attribute("Stroke"));
        Assert.Equal("Flagged sessions", (string?)toggle.Attribute("AutomationProperties.Name"));

        var apply = MethodBody(Source("SidebarView.xaml.cs"), "public void Apply(SidebarSnapshot snapshot)");
        Assert.Contains("var flagged = snapshot.Mode == SidebarMode.Flagged;", apply, StringComparison.Ordinal);
        // a ToggleButton keeps the UI Automation toggle state; IsChecked is what it reports
        Assert.Contains("FlaggedToggle.IsChecked = flagged;", apply, StringComparison.Ordinal);
        Assert.Contains("FlaggedGlyph.Fill = flagged ? FlaggedGlyph.Stroke : null;", apply, StringComparison.Ordinal);
        Assert.Contains("FlaggedToggle.IsEnabled = flagged || snapshot.FlaggedCount > 0;", apply, StringComparison.Ordinal);
        Assert.Contains("FlaggedToggle.Opacity = FlaggedToggle.IsEnabled ? 1 : 0.35;", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void New_workspace_draws_a_stack_of_rectangles_with_a_plus()
    {
        var button = Named("NewWorkspaceButton");

        Assert.Empty(button.Descendants(Xaml + "FontIcon"));
        var glyph = button.Descendants(Xaml + "Path").Single();
        Assert.Equal("M3.5,0.5 H12.5 M1.5,2.5 H14.5 M0.5,4.5 H15.5 V14.5 H0.5 Z M8,7 V12 M5.5,9.5 H10.5",
            (string?)glyph.Attribute("Data"));
        Assert.Equal("{StaticResource SidebarForegroundBrush}", (string?)glyph.Attribute("Stroke"));
        Assert.Equal("New workspace", (string?)button.Attribute("AutomationProperties.Name"));
        Assert.Equal("{StaticResource SidebarForegroundBrush}",
            (string?)Named("NewSessionButton").Descendants(Xaml + "Path").Single().Attribute("Stroke"));
    }

    [Fact]
    public void Workspace_filter_toggle_is_an_outline_grid_that_fills_while_the_filter_applies()
    {
        var toggle = Named("FocusFilterToggle");
        var glyph = toggle.Descendants(Xaml + "Path").Single();

        Assert.Equal("FocusFilterGlyph", (string?)glyph.Attribute(X + "Name"));
        Assert.Equal("M1.5,1.5 H6.5 V6.5 H1.5 Z M9.5,1.5 H14.5 V6.5 H9.5 Z M1.5,8.5 H6.5 V13.5 H1.5 Z M9.5,8.5 H14.5 V13.5 H9.5 Z",
            (string?)glyph.Attribute("Data"));
        Assert.Equal("{StaticResource SidebarForegroundBrush}", (string?)glyph.Attribute("Stroke"));
        Assert.Equal("Toggle Workspace Filter", (string?)toggle.Attribute("AutomationProperties.Name"));
        Assert.Equal("focus-filter-toggle", (string?)toggle.Attribute("AutomationProperties.AutomationId"));

        var apply = MethodBody(Source("SidebarView.xaml.cs"), "public void Apply(SidebarSnapshot snapshot)");
        Assert.Contains("FocusFilterToggle.IsChecked = snapshot.FocusEnabled;", apply, StringComparison.Ordinal);
        Assert.Contains("FocusFilterGlyph.Fill = snapshot.FocusEnabled ? FocusFilterGlyph.Stroke : null;", apply, StringComparison.Ordinal);
        // nothing marked means nothing to filter to
        Assert.Contains("FocusFilterToggle.IsEnabled = snapshot.FocusMemberIds.Count > 0;", apply, StringComparison.Ordinal);
        Assert.Contains("FocusFilterToggle.Opacity = FocusFilterToggle.IsEnabled ? 1 : 0.35;", apply, StringComparison.Ordinal);
    }

    // upstream a2552826 flaggedEmptyState: a note and an "Add Sessions…" link that runs edit_flagged_sessions
    [Fact]
    public void Empty_flagged_view_offers_the_flagged_editor()
    {
        var state = Named("FlaggedEmptyState");
        Assert.Equal("0", (string?)state.Attribute("Grid.Row"));
        Assert.Equal("Collapsed", (string?)state.Attribute("Visibility"));
        Assert.Equal("0.6", (string?)state.Attribute("Opacity"));
        Assert.Contains(state.Descendants(Xaml + "TextBlock"), text => (string?)text.Attribute("Text") == "No flagged sessions.");
        var link = Named("FlaggedEmptyAddLink");
        Assert.Equal("flagged-empty-add", (string?)link.Attribute("AutomationProperties.AutomationId"));
        Assert.Equal("Add Sessions…", (string?)link.Attribute("AutomationProperties.Name"));
        Assert.Equal("OnFlaggedEmptyAddClick", (string?)link.Attribute("Click"));
        Assert.Contains(link.Descendants(Xaml + "Underline"), _ => true);

        var view = Source("SidebarView.xaml.cs");
        var apply = MethodBody(view, "public void Apply(SidebarSnapshot snapshot)");
        Assert.Contains("FlaggedEmptyState.Visibility = snapshot.ShowsFlaggedEmptyState ? Visibility.Visible : Visibility.Collapsed;",
            apply, StringComparison.Ordinal);
        Assert.Contains("EditFlaggedSessionsRequested?.Invoke()", MethodBody(view, "private void OnFlaggedEmptyAddClick"), StringComparison.Ordinal);
        Assert.Contains("Sidebar.EditFlaggedSessionsRequested +=", Source("MainWindow.xaml.cs"), StringComparison.Ordinal);
        Assert.Contains("router.ExecuteAsync(BuiltinAction.EditFlaggedSessions)", Source("MainWindow.xaml.cs"), StringComparison.Ordinal);
    }

    private static XElement Footer() =>
        Document().Root!.Element(Xaml + "Grid")!.Elements(Xaml + "Grid")
            .Single(grid => (string?)grid.Attribute(X + "Name") == "SidebarFooter");

    private static XElement Named(string name) =>
        Document().Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    private static XElement Resources() => Document().Root!.Element(Xaml + "UserControl.Resources")!;

    private static XElement Style(string key) =>
        Resources().Elements(Xaml + "Style").Single(style => (string?)style.Attribute(X + "Key") == key);

    private static string? Setter(XElement style, string property) =>
        (string?)style.Elements(Xaml + "Setter").Single(setter => (string?)setter.Attribute("Property") == property)
            .Attribute("Value");

    private static string? Alias(XElement resources, string key) =>
        (string?)resources.Elements(Xaml + "StaticResource")
            .Single(alias => (string?)alias.Attribute(X + "Key") == key).Attribute("ResourceKey");

    private static string[] Names(XElement group) =>
        group.Elements().Select(element => (string?)element.Attribute(X + "Name") ?? "").ToArray();

    private static XDocument Document() => XDocument.Parse(Source("SidebarView.xaml"));

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
