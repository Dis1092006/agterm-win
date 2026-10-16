namespace AgTerm.Xterm.Tests;

// SidebarRowViewModel and the views are WinUI types the test project does not compile, so these
// tests pin the wiring in their source.
public sealed class SidebarThemeColorsSourceTests
{
    [Fact]
    public void Row_template_draws_the_theme_colors_inside_an_opaque_border()
    {
        var xaml = Source("SidebarView.xaml");

        Assert.Contains("<Setter Property=\"Padding\" Value=\"0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{x:Bind RowBackground, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{x:Bind RowFrame, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"{ThemeResource ListViewItemCornerRadius}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Padding=\"7,0,7,0\"", xaml, StringComparison.Ordinal);
        Assert.True(Count(xaml, "local:SidebarRowBrushes.Foreground=\"{x:Bind RowForeground, Mode=OneWay}\"") >= 4,
            "label, chevron, session icon and focus icon take the theme foreground");
        Assert.DoesNotContain("Stroke=\"{ThemeResource TextFillColorSecondaryBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_view_feeds_every_row_its_colors_and_its_list_selection()
    {
        var view = Source("SidebarView.xaml.cs");
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("_rowThemes?.Observe(snapshot.Rows)", view, StringComparison.Ordinal);
        Assert.Contains("_settings.ShowSidebarThemeColors()", view, StringComparison.Ordinal);
        Assert.Contains("IsListSelected = RowsList.SelectedItems.Contains(", view, StringComparison.Ordinal);
        Assert.Contains("public void PreviewWorkspaceTheme(Guid workspaceId, string? themeName)", view, StringComparison.Ordinal);
        Assert.Contains("public void EndWorkspaceThemePreview()", view, StringComparison.Ordinal);
        Assert.Contains("public void WorkspaceThemeSaved(Guid workspaceId, string? themeName)", view, StringComparison.Ordinal);
        Assert.Contains("Sidebar.Attach(sidebar, _settings, this, _themeCatalog)", window, StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
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
