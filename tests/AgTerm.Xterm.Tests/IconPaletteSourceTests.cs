namespace AgTerm.Xterm.Tests;

// The views are WinUI types the test project does not compile, so these tests pin the wiring in
// their source.
public sealed class IconPaletteSourceTests
{
    [Fact]
    public void Workspace_menu_offers_Icon_right_after_Theme()
    {
        var sidebar = Source("SidebarView.xaml.cs");

        Assert.Contains("public event Action<Guid>? WorkspaceIconRequested;", sidebar, StringComparison.Ordinal);
        Assert.Contains(
            "flyout.Items.Add(Item(\"Theme…\", () => WorkspaceThemeRequested?.Invoke(row.Id)));\n" +
            "        flyout.Items.Add(Item(\"Icon…\", () => WorkspaceIconRequested?.Invoke(row.Id)));",
            sidebar.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Window_opens_the_palette_through_the_palette_transitions()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("Sidebar.WorkspaceIconRequested += workspaceId => ObserveTaskFault(", window, StringComparison.Ordinal);
        Assert.Contains("_paletteTransitions.RunAsync(() => OpenWorkspaceIconPaletteAsync(workspaceId))", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Palette_does_not_open_while_a_pane_is_zoomed_or_a_pick_is_modal()
    {
        var open = MethodBody(Source("MainWindow.xaml.cs"), "private async Task OpenWorkspaceIconPaletteAsync(");

        Assert.Contains("host.Host.Actions.Pick.ModalPending", open, StringComparison.Ordinal);
        Assert.Contains("host.Host.Actions.Zoom.Target is null", open, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_previews_and_every_close_ends_the_preview_on_the_sidebar()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("Sidebar.PreviewWorkspaceIcon(workspaceId, iconName)",
            MethodBody(window, "private void OnIconPaletteSelectionChanged("), StringComparison.Ordinal);
        Assert.Contains("Sidebar.EndWorkspaceIconPreview()",
            MethodBody(window, "private async Task FinishPaletteCloseAsync("), StringComparison.Ordinal);
    }

    [Fact]
    public void Save_goes_through_the_application_host_and_shows_the_saved_icon_on_success()
    {
        var save = MethodBody(Source("MainWindow.xaml.cs"), "private async Task<bool> SaveWorkspaceIconAsync(");

        Assert.Contains("_applicationHost.SetWorkspaceIconAsync(_windowId, workspaceId, iconName)", save, StringComparison.Ordinal);
        var okBranch = save[save.IndexOf("if (response.Ok)", StringComparison.Ordinal)..save.IndexOf("return true;", StringComparison.Ordinal)];
        Assert.Contains("Sidebar.WorkspaceIconSaved(workspaceId, iconName)", okBranch, StringComparison.Ordinal);
        Assert.Contains(
            "new LiveFailureNotice(\"Icon could not be applied\", response.Error ?? \"workspace.icon failed\")",
            save.Replace("\r\n", "\n").Replace("\n", "").Replace("  ", ""),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_checks_CanAccept_before_reading_SelectedName()
    {
        var view = Source("IconPaletteView.xaml.cs");
        var accept = MethodBody(view, "private void Accept()");

        var canAccept = accept.IndexOf("_model.CanAccept", StringComparison.Ordinal);
        var name = accept.IndexOf("_model.SelectedName", StringComparison.Ordinal);
        Assert.True(canAccept >= 0 && name > canAccept, "Enter with an empty filter result must do nothing");
    }

    [Fact]
    public void View_builds_its_cells_from_the_catalog_and_the_machine_glyph_probe()
    {
        var view = Source("IconPaletteView.xaml.cs");

        Assert.Contains("SegoeIconCatalog.All", view, StringComparison.Ordinal);
        Assert.Contains("SymbolGlyphs.Has", view, StringComparison.Ordinal);
        Assert.Contains("_model.Move(", view, StringComparison.Ordinal);
    }

    [Fact]
    public void Window_treats_the_icon_palette_as_a_palette_for_focus_and_window_commands()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("private bool AnyPaletteOpen => PaletteOverlay.IsOpen || IconPaletteOverlay.IsOpen;", window, StringComparison.Ordinal);
        Assert.Contains("AnyPaletteOpen && _shownPickId is null && PaletteKeys.ClosesPaletteFirst(action)", window, StringComparison.Ordinal);
        Assert.Contains("IconPaletteOverlay.FocusFilter()", MethodBody(window, "private void OnWindowActivated("), StringComparison.Ordinal);
    }

    [Fact]
    public void Palette_closes_with_its_workspace()
    {
        var window = Source("MainWindow.xaml.cs");

        var present = MethodBody(window, "private void PresentPaneThemes(");
        Assert.Contains("IconPaletteOverlay.IsOpen", present, StringComparison.Ordinal);
        Assert.Contains("!_paneThemes.HasWorkspace(iconWorkspaceId)", present, StringComparison.Ordinal);
        Assert.Contains("_paletteTransitions.RunAsync(() => CloseWorkspaceIconPaletteAsync(iconWorkspaceId))", present, StringComparison.Ordinal);
        var close = MethodBody(window, "private async Task CloseWorkspaceIconPaletteAsync(");
        Assert.Contains("_workspaceIconTarget != workspaceId", close, StringComparison.Ordinal);
        Assert.Contains("FinishPaletteCloseAsync()", close, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_replaces_any_open_palette()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("IconPaletteOverlay.Close();",
            MethodBody(window, "private async Task PresentPaletteAsync("), StringComparison.Ordinal);
        Assert.Contains("ClosePalette();",
            MethodBody(window, "private async Task OpenWorkspaceIconPaletteAsync("), StringComparison.Ordinal);
    }

    [Fact]
    public void Grid_cells_never_keep_the_keyboard_focus_from_the_filter_box()
    {
        var xaml = Source("IconPaletteView.xaml");

        Assert.Contains("<Setter Property=\"AllowFocusOnInteraction\" Value=\"False\" />", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Columns_come_from_the_realized_wrap_grid_and_the_scroll_to_current_waits_for_layout()
    {
        var view = Source("IconPaletteView.xaml.cs");

        Assert.Contains("IconGrid.ItemsPanelRoot is ItemsWrapGrid panel", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollBarAllowance", view, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue(ScrollSelectedIntoView)", MethodBody(view, "public void Open("), StringComparison.Ordinal);
        Assert.Contains("\"\\uE711\"", view, StringComparison.Ordinal);
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
