namespace AgTerm.Xterm.Tests;

// SidebarRowViewModel and the views are WinUI types the test project does not compile, so these
// tests pin the wiring in their source.
public sealed class SidebarThemeColorsSourceTests
{
    [Fact]
    public void Row_template_draws_the_theme_colors_on_a_layer_inset_like_the_selection_backplate()
    {
        var xaml = Source("SidebarView.xaml");

        Assert.Contains("<Setter Property=\"Padding\" Value=\"0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{x:Bind RowBackground, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{x:Bind RowFrame, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"{ThemeResource ListViewItemCornerRadius}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Height=\"{x:Bind RowHeight, Mode=OneWay}\">", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"4,2,4,2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BackgroundSizing=\"OuterBorderEdge\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid ColumnSpacing=\"6\" Margin=\"8,0,8,0\">", xaml, StringComparison.Ordinal);
        Assert.True(Count(xaml, "local:SidebarRowBrushes.Foreground=\"{x:Bind RowForeground, Mode=OneWay}\"") >= 4,
            "label, chevron, session icon and focus icon take the theme foreground");
        Assert.DoesNotContain("Stroke=\"{ThemeResource TextFillColorSecondaryBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_view_feeds_every_row_its_colors_and_its_list_selection()
    {
        var view = Source("SidebarView.xaml.cs");
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("_rowThemes?.Observe(snapshot.Generation)", view, StringComparison.Ordinal);
        Assert.Contains("_rowThemes?.Saved(workspaceId, themeName, _controller?.RequestSnapshot() ?? 0)", view, StringComparison.Ordinal);
        Assert.Contains("_settings.ShowSidebarThemeColors()", view, StringComparison.Ordinal);
        Assert.Contains("IsListSelected = RowsList.SelectedItems.Contains(", view, StringComparison.Ordinal);
        Assert.Contains("public void PreviewWorkspaceTheme(Guid workspaceId, string? themeName)", view, StringComparison.Ordinal);
        Assert.Contains("public void EndWorkspaceThemePreview()", view, StringComparison.Ordinal);
        Assert.Contains("public void WorkspaceThemeSaved(Guid workspaceId, string? themeName)", view, StringComparison.Ordinal);
        Assert.Contains("Sidebar.Attach(sidebar, _settings, this, _themeCatalog)", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_palette_preview_cancel_and_save_reach_the_sidebar()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("Sidebar.PreviewWorkspaceTheme(workspaceId, themeName)",
            MethodBody(window, "private Task ShowWorkspaceThemePreviewAsync("), StringComparison.Ordinal);
        Assert.Contains("Sidebar.EndWorkspaceThemePreview()",
            MethodBody(window, "private Task ShowSavedWorkspaceThemesAsync()"), StringComparison.Ordinal);
        var save = MethodBody(window, "private async Task<bool> SaveWorkspaceThemeAsync(");
        var okBranch = save[save.IndexOf("if (response.Ok)", StringComparison.Ordinal)..save.IndexOf("return true;", StringComparison.Ordinal)];
        Assert.Contains("Sidebar.WorkspaceThemeSaved(workspaceId, themeName)", okBranch, StringComparison.Ordinal);
    }

    // from the signature to the first line that closes a member at class indentation
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        return source[start..end];
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
