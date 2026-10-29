namespace AgTerm.Xterm.Tests;

// SidebarRowViewModel and the views are WinUI types the test project does not compile, so these
// tests pin the wiring in their source. Upstream draws every workspace row alike, with a
// square.grid.2x2 icon, and its outline indents the sessions one level.
public sealed class SidebarRowNestingSourceTests
{
    [Fact]
    public void An_inactive_workspace_row_is_not_dimmed()
    {
        Assert.DoesNotContain("LabelOpacity", Source("SidebarRowViewModel.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("LabelOpacity", Source("SidebarView.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Row_content_is_indented_by_the_kind_of_row_and_the_sidebar_mode()
    {
        var model = Source("SidebarRowViewModel.cs");
        var view = Source("SidebarView.xaml.cs");

        Assert.Contains("new(8 + SidebarRowIndent.Of(Kind, _mode), 0, 8, 0)",
            Between(model, "public Thickness ContentMargin =>", ";"), StringComparison.Ordinal);
        Assert.Contains("new SidebarRowViewModel(row, _settings, snapshot.Mode,", view, StringComparison.Ordinal);
    }

    [Fact]
    public void A_workspace_row_shows_a_grid_icon_before_its_name_in_the_theme_foreground()
    {
        var xaml = Source("SidebarView.xaml");
        var label = Between(xaml, "<Grid Grid.Column=\"1\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\">",
            "Background=\"{x:Bind LabelBlockBackground, Mode=OneWay}\"");
        var icon = Between(label, "<Path Data=\"" + WorkspaceIconData + "\"", "</Path>");

        Assert.Contains("local:SidebarRowBrushes.Foreground=\"{x:Bind RowForeground, Mode=OneWay}\"", icon, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind GridIconVisibility, Mode=OneWay}\"", icon, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,0,6,0\"", icon, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Stroke\" Value=\"{ThemeResource TextFillColorSecondaryBrush}\" />", icon, StringComparison.Ordinal);
    }

    [Fact]
    public void A_workspace_icon_draws_in_the_slot_of_the_grid_with_the_secondary_text_color()
    {
        var xaml = Source("SidebarView.xaml");
        var label = Between(xaml, "<Grid Grid.Column=\"1\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\">",
            "Background=\"{x:Bind LabelBlockBackground, Mode=OneWay}\"");
        var icon = Between(label, "<FontIcon", "</FontIcon>");

        Assert.Contains("Glyph=\"{x:Bind WorkspaceGlyph, Mode=OneWay}\"", icon, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind WorkspaceGlyphVisibility, Mode=OneWay}\"", icon, StringComparison.Ordinal);
        Assert.Contains("FontFamily=\"{ThemeResource SymbolThemeFontFamily}\"", icon, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"14\"", icon, StringComparison.Ordinal);
        Assert.Contains("Width=\"16\"", icon, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,0,6,0\"", icon, StringComparison.Ordinal);
        Assert.Contains("local:SidebarRowBrushes.Foreground=\"{x:Bind RowForeground, Mode=OneWay}\"", icon, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{ThemeResource TextFillColorSecondaryBrush}\" />", icon, StringComparison.Ordinal);
    }

    [Fact]
    public void The_view_model_asks_the_symbol_fonts_for_the_glyph_and_raises_the_three_names()
    {
        var model = Source("SidebarRowViewModel.cs");

        Assert.Contains("SidebarWorkspaceIcon.Glyph(_iconName, SymbolGlyphs.Has)", model, StringComparison.Ordinal);
        var raised = Between(model, "public void Update(", "private static SolidColorBrush BrushFrom");
        Assert.Contains("nameof(WorkspaceGlyph)", raised, StringComparison.Ordinal);
        Assert.Contains("nameof(WorkspaceGlyphVisibility)", raised, StringComparison.Ordinal);
        Assert.Contains("nameof(GridIconVisibility)", raised, StringComparison.Ordinal);
    }

    [Fact]
    public void A_workspace_image_file_draws_in_the_slot_of_the_grid_at_16_px()
    {
        var xaml = Source("SidebarView.xaml");
        var label = Between(xaml, "<Grid Grid.Column=\"1\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\">",
            "Background=\"{x:Bind LabelBlockBackground, Mode=OneWay}\"");
        var image = Between(label, "<Image", "/>");

        Assert.Contains("Source=\"{x:Bind WorkspaceImage, Mode=OneWay}\"", image, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind WorkspaceImageVisibility, Mode=OneWay}\"", image, StringComparison.Ordinal);
        Assert.Contains("Width=\"16\"", image, StringComparison.Ordinal);
        Assert.Contains("Height=\"16\"", image, StringComparison.Ordinal);
        Assert.Contains("Margin=\"0,0,6,0\"", image, StringComparison.Ordinal);
    }

    [Fact]
    public void The_view_model_loads_the_image_at_16_px_and_draws_the_grid_when_it_fails()
    {
        var model = Source("SidebarRowViewModel.cs");

        Assert.Contains("SidebarWorkspaceIcon.KindOf(_iconName, SymbolGlyphs.Has, _imageFailed)", model, StringComparison.Ordinal);
        Assert.Contains("DecodePixelWidth = 16", model, StringComparison.Ordinal);
        Assert.Contains("RasterizePixelWidth = 16", model, StringComparison.Ordinal);
        Assert.Contains(".ImageFailed +=", model, StringComparison.Ordinal);
        Assert.Contains(".OpenFailed +=", model, StringComparison.Ordinal);
        var raised = Between(model, "public void Update(", "private static SolidColorBrush BrushFrom");
        Assert.Contains("nameof(WorkspaceImage)", raised, StringComparison.Ordinal);
        Assert.Contains("nameof(WorkspaceImageVisibility)", raised, StringComparison.Ordinal);
    }

    [Fact]
    public void The_session_icon_fill_follows_the_sidebar_mode()
    {
        Assert.Contains("SidebarSessionIcon.IsFilled(_row.Flagged, _mode)",
            Between(Source("SidebarRowViewModel.cs"), "public Brush IconFill =>", ";"), StringComparison.Ordinal);
    }

    private const string WorkspaceIconData =
        "M1.5,1.5 H6.5 V6.5 H1.5 Z M9.5,1.5 H14.5 V6.5 H9.5 Z M1.5,8.5 H6.5 V13.5 H1.5 Z M9.5,8.5 H14.5 V13.5 H9.5 Z";

    // the text from the first marker to the first end marker after it
    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"{start} not found");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to >= 0, $"{end} not found after {start}");
        return source[from..to];
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
