namespace AgTerm.Xterm.Tests;

// SidebarRowViewModel and the views are WinUI types the test project does not compile, so these
// tests pin the wiring in their source.
public sealed class SidebarThemeColorsSourceTests
{
    [Fact]
    public void Row_template_draws_the_theme_colors_on_an_inset_layer_with_a_2px_gap_and_small_corners()
    {
        var xaml = Source("SidebarView.xaml");

        Assert.Contains("<Setter Property=\"Padding\" Value=\"0\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{x:Bind RowBackground, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{x:Bind RowFrame, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"2\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("CornerRadius=\"{ThemeResource ListViewItemCornerRadius}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Height=\"{x:Bind RowHeight, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Margin=\"4,1,4,1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BackgroundSizing=\"OuterBorderEdge\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid ColumnSpacing=\"6\" Margin=\"{x:Bind ContentMargin, Mode=OneWay}\">", xaml, StringComparison.Ordinal);
        Assert.True(Count(xaml, "local:SidebarRowBrushes.Foreground=\"{x:Bind RowForeground, Mode=OneWay}\"") >= 5,
            "label, chevron, session icon, workspace icon and focus icon take the theme foreground");
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
        // the list keeps an extended (Ctrl+click) selection across snapshots, as upstream re-selects
        // sidebarSelectionIDs after each update
        Assert.Contains("snapshot.ListSelection()", MethodBody(view, "private void SyncSelection("), StringComparison.Ordinal);
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

    [Fact]
    public void Sidebar_view_feeds_every_row_its_icon_name_through_the_icon_holder()
    {
        var view = Source("SidebarView.xaml.cs");
        var model = Source("SidebarRowViewModel.cs");

        Assert.Contains("_rowIcons.Observe(snapshot.Generation)", view, StringComparison.Ordinal);
        Assert.Contains("_rowIcons.Saved(workspaceId, iconName, _controller?.RequestSnapshot() ?? 0)", view, StringComparison.Ordinal);
        Assert.Contains("public void PreviewWorkspaceIcon(Guid workspaceId, string? iconName)", view, StringComparison.Ordinal);
        Assert.Contains("public void EndWorkspaceIconPreview()", view, StringComparison.Ordinal);
        Assert.Contains("public void WorkspaceIconSaved(Guid workspaceId, string? iconName)", view, StringComparison.Ordinal);
        Assert.Contains("_rowIcons.IconNameFor(row)", view, StringComparison.Ordinal);
        Assert.Contains("SidebarWorkspaceIcon.Glyph(_iconName, SymbolGlyphs.Has)", model, StringComparison.Ordinal);
        Assert.DoesNotContain("_row.IconName", model, StringComparison.Ordinal);
        Assert.Contains("_iconName == iconName", MethodBody(model, "public void Update("), StringComparison.Ordinal);
    }

    [Fact]
    public void Label_area_is_a_left_aligned_grid_so_a_long_name_trims_before_the_focus_icon()
    {
        var xaml = Source("SidebarView.xaml");

        // a horizontal StackPanel measures its children with unlimited width, so TextTrimming never ran
        Assert.DoesNotContain("<StackPanel Grid.Column=\"1\" Orientation=\"Horizontal\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Grid Grid.Column=\"1\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\">", xaml, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Label_style_draws_a_block_around_the_name_that_hides_with_it()
    {
        var xaml = Source("SidebarView.xaml");
        var block = Between(xaml, "Background=\"{x:Bind LabelBlockBackground, Mode=OneWay}\"", "</Border>");

        Assert.Contains("CornerRadius=\"2\"", block, StringComparison.Ordinal);
        Assert.Contains("Padding=\"{x:Bind LabelBlockPadding, Mode=OneWay}\"", block, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{x:Bind LabelVisibility, Mode=OneWay}\"", block, StringComparison.Ordinal);
        Assert.Contains("local:SidebarRowBrushes.Foreground=\"{x:Bind LabelForeground, Mode=OneWay}\"", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_unseen_badge_is_the_last_column_and_no_swatch_square_is_left()
    {
        var xaml = Source("SidebarView.xaml");
        var model = Source("SidebarRowViewModel.cs");

        Assert.Contains("<Border Grid.Column=\"4\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<StackPanel Grid.Column=\"4\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Swatch", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Swatch", model, StringComparison.Ordinal);
    }

    [Fact]
    public void Row_view_model_reads_the_look_of_the_style()
    {
        var model = Source("SidebarRowViewModel.cs");
        var view = Source("SidebarView.xaml.cs");

        Assert.Contains("SidebarRowLook.From(_rowThemes?.ColorsFor(row, _settings.ShowSidebarThemeColors()), _settings.EffectiveSidebarThemeStyle())", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ColorsOf(", view, StringComparison.Ordinal);
        Assert.DoesNotContain("_colors", model, StringComparison.Ordinal);
        Assert.Contains("private bool IsColoredSelection => _look.Row is not null && IsListSelected;", model, StringComparison.Ordinal);
        Assert.Contains("_look.Row is { } colors", Between(model, "public Brush IconFill", ";"), StringComparison.Ordinal);
        var update = MethodBody(model, "public void Update(");
        foreach (var name in new[] { "LabelWeight", "RowFrame", "LabelForeground", "LabelBlockBackground", "LabelBlockPadding" })
            Assert.Contains($"nameof({name})", update, StringComparison.Ordinal);
    }

    [Fact]
    public void A_selected_session_row_is_bold_with_or_without_theme_colors_and_framed_only_in_row()
    {
        var model = Source("SidebarRowViewModel.cs");

        var weight = Between(model, "public FontWeight LabelWeight =>", ";");
        Assert.Contains(": IsListSelected ? Microsoft.UI.Text.FontWeights.Bold", weight, StringComparison.Ordinal);
        Assert.DoesNotContain("IsBoldSelection", model, StringComparison.Ordinal);
        Assert.Contains("public Brush RowFrame => IsColoredSelection", model, StringComparison.Ordinal);
    }

    // the text from the first marker to the first end marker after it
    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"{start} not found");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
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
