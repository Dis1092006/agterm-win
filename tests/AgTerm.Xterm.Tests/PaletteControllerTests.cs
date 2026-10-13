using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class PaletteControllerTests
{
    private static PaletteItemViewModel Item(
        string id,
        string title,
        string? subtitle = null,
        string? shortcut = null,
        string? badge = null) =>
        new(id, title, subtitle, shortcut, badge);

    [Fact]
    public void Open_copies_the_fresh_item_snapshot_and_alphabetizes_empty_query()
    {
        var source = new List<PaletteItemViewModel>
        {
            Item("beta", "beta"),
            Item("alpha", "Alpha"),
        };
        var controller = new PaletteController();

        controller.Open(source);
        source.Clear();

        Assert.True(controller.IsOpen);
        Assert.Equal(["alpha", "beta"], controller.FilteredItems.Select(item => item.Id));
        Assert.Equal("alpha", controller.SelectedItem?.Id);
    }

    [Fact]
    public void Open_can_preserve_caller_order_for_empty_query_but_typed_query_still_ranks()
    {
        var controller = new PaletteController();

        controller.Open(
        [
            Item("blocked", "Zulu Blocked"),
            Item("active", "Alpha Active"),
            Item("completed", "Beta Completed"),
        ], preserveInputOrderForEmptyQuery: true);

        Assert.Equal(["blocked", "active", "completed"], controller.FilteredItems.Select(item => item.Id));

        controller.SetQuery("a");

        Assert.Equal(["active", "completed"], controller.FilteredItems.Select(item => item.Id));
    }

    [Fact]
    public void SetQuery_matches_title_or_subtitle_and_resets_selection_to_first_result()
    {
        var controller = new PaletteController();
        controller.Open(
        [
            Item("toggle", "Toggle Split", "panes"),
            Item("reload", "Reload", "split config"),
            Item("close", "Close Session"),
        ]);
        controller.MoveSelection(2);

        controller.SetQuery("split");

        Assert.Equal(["reload", "toggle"], controller.FilteredItems.Select(item => item.Id));
        Assert.Equal(0, controller.SelectedIndex);
        Assert.Equal("reload", controller.SelectedItem?.Id);
    }

    [Fact]
    public void MoveSelection_clamps_at_both_ends_and_does_nothing_for_empty_results()
    {
        var controller = new PaletteController();
        controller.Open([Item("a", "Alpha"), Item("b", "Beta")]);

        controller.MoveSelection(-1);
        Assert.Equal(0, controller.SelectedIndex);

        controller.MoveSelection(10);
        Assert.Equal(1, controller.SelectedIndex);

        controller.SetQuery("no match");
        controller.MoveSelection(1);
        Assert.Equal(0, controller.SelectedIndex);
        Assert.Null(controller.SelectedItem);
    }

    [Fact]
    public void SelectIndex_uses_the_same_filtered_array_as_keyboard_acceptance()
    {
        var controller = new PaletteController();
        controller.Open([Item("a", "Alpha"), Item("b", "Beta")]);

        controller.SelectIndex(1);

        Assert.Equal("b", controller.SelectedItem?.Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SelectIndex(2));
    }

    [Fact]
    public void AcceptSelected_returns_the_rendered_selection_and_closes()
    {
        var controller = new PaletteController();
        controller.Open([Item("a", "Alpha"), Item("b", "Beta")]);
        controller.MoveSelection(1);

        var accepted = controller.AcceptSelected();

        Assert.Equal("b", accepted?.Id);
        Assert.False(controller.IsOpen);
        Assert.Empty(controller.FilteredItems);
        Assert.Null(controller.SelectedItem);
    }

    [Fact]
    public void AcceptSelected_with_no_results_stays_open_and_Close_clears_transient_state()
    {
        var controller = new PaletteController();
        controller.Open([Item("a", "Alpha")]);
        controller.SetQuery("missing");

        Assert.Null(controller.AcceptSelected());
        Assert.True(controller.IsOpen);

        controller.Close();

        Assert.False(controller.IsOpen);
        Assert.Equal(string.Empty, controller.Query);
        Assert.Empty(controller.FilteredItems);
    }

    [Fact]
    public void Open_rejects_duplicate_ids_but_accepts_blank_ids_and_whitespace_titles()
    {
        var controller = new PaletteController();
        Assert.Throws<ArgumentException>(() => controller.Open([Item("same", "One"), Item("same", "Two")]));

        controller.Open([Item("", "Blank id"), Item("ws", "   ")]);

        Assert.Equal(2, controller.FilteredItems.Count);
    }

    [Fact]
    public void Item_exposes_concise_automation_name_and_immutable_search_keys()
    {
        var item = Item("build", "Build", "Current workspace", "Ctrl+B", "custom");

        Assert.Equal("Build, Current workspace, custom, Ctrl+B", item.AutomationName);
        Assert.Equal(["Build", "Current workspace"], item.SearchKeys);
    }
}

public sealed class PaletteLayoutTests
{
    [Theory]
    [InlineData(800, 320)]
    [InlineData(459, 320)]
    [InlineData(400, 301)]
    [InlineData(219, 120)]
    [InlineData(160, 61)]
    [InlineData(100, 48)]
    public void ResultHeight_is_stable_until_the_window_requires_it_to_shrink(
        double availableHeight,
        double expected)
    {
        Assert.Equal(expected, PaletteLayout.ResultHeight(availableHeight));
    }

    [Theory]
    [InlineData(459, 64)]
    [InlineData(458, 24)]
    public void PanelTopMargin_compacts_below_full_panel_height(
        double availableHeight,
        double expected)
    {
        Assert.Equal(expected, PaletteLayout.PanelTopMargin(availableHeight));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ResultHeight_rejects_invalid_available_height(double availableHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaletteLayout.ResultHeight(availableHeight));
    }
}

public sealed class PaletteViewSourceTests
{
    [Fact]
    public void Code_behind_supports_pick_options_ctrl_j_k_and_a_cancel_chord()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "PaletteView.xaml.cs"));

        Assert.Contains("PaletteOpenOptions options", source, StringComparison.Ordinal);
        Assert.Contains("PaletteKeys.SelectionStep(", source, StringComparison.Ordinal);
        Assert.Contains("QueryBox.SelectAll()", source, StringComparison.Ordinal);
        Assert.Contains("public Func<VirtualKey, bool>? CancelChord", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Scrim_ignores_the_tap_of_a_press_that_activated_the_window()
    {
        var root = FindRepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "PaletteView.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "PaletteView.xaml.cs"));

        Assert.Contains("PointerPressed=\"OnDismissSurfacePointerPressed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("public void NoteActivation()", code, StringComparison.Ordinal);
        Assert.Contains("_activationClicks.Activated(Environment.TickCount64)", code, StringComparison.Ordinal);
        Assert.Contains("_activationClicks.Pressed(Environment.TickCount64)", code, StringComparison.Ordinal);
        Assert.Matches(@"OnDismissSurfaceTapped\([^)]*\)\s*\{\s*if \(_activationClicks\.ShouldIgnoreTap\(\)\)\s*return;\s*Cancel\(\);", code);
    }

    [Fact]
    public void MainWindow_wires_the_pick_before_the_page_is_awaited()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        var subscribe = source.IndexOf(".Pick.Changed +=", StringComparison.Ordinal);
        var arm = source.IndexOf("ObserveTaskFault(ArmHotkeysAsync(host)", StringComparison.Ordinal);
        var armMethod = source.IndexOf("private async Task ArmHotkeysAsync", StringComparison.Ordinal);
        Assert.True(subscribe >= 0 && arm >= 0 && subscribe < arm, "the pick subscription must come before ArmHotkeysAsync is started.");
        Assert.True(subscribe < armMethod);
        // WinUI 3 reports a click activation as CodeActivated, so every activation is noted
        Assert.Matches(@"WindowActivationState\.Deactivated\)\s*return;\s*PaletteOverlay\.NoteActivation\(\);", source);
        // a pick shown in a background window must not take focus (that activates the window)
        Assert.Contains("PickPalette.Options(pending), takeFocus: _windowIsActive)", source, StringComparison.Ordinal);
        Assert.Contains("PaletteOverlay.FocusQuery();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_restores_focus_on_activation_only_without_an_open_palette()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("_terminalFocus.Activated(modalOpen: PaletteOverlay.IsOpen ||", source, StringComparison.Ordinal);
        var reset = source.IndexOf("_terminalFocus.Cancel();\n        PaletteOverlay.Open(PickPalette", StringComparison.Ordinal);
        var resetCrlf = source.IndexOf("_terminalFocus.Cancel();\r\n        PaletteOverlay.Open(PickPalette", StringComparison.Ordinal);
        Assert.True(reset >= 0 || resetCrlf >= 0, "the flag must be cleared right before the pick opens.");
    }

    [Fact]
    public void MainWindow_answers_the_pick_before_awaiting_the_palette_close()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Matches(@"_shownPickId = null;\s+pickHost\.Host\.Actions\.Pick\.Resolve\(pickId", source);
        Assert.Matches(@"_shownPickId = null;\s+host\.Host\.Actions\.Pick\.Cancel\(pickId", source);
    }

    [Fact]
    public void MainWindow_presents_the_windows_pick_and_resolves_it()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains(".Pick.Changed +=", source, StringComparison.Ordinal);
        Assert.Contains("SyncPickAsync", source, StringComparison.Ordinal);
        Assert.Contains("PickPalette.Outcome(", source, StringComparison.Ordinal);
        Assert.Contains(".Pick.Resolve(", source, StringComparison.Ordinal);
        Assert.Contains(".Pick.Cancel(", source, StringComparison.Ordinal);
        Assert.Contains(".Pick.ModalPending", source, StringComparison.Ordinal);
        Assert.Contains("BuiltinAction.CloseSession", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_hosts_palette_after_its_single_WebView_as_a_full_window_sibling()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml"));
        var webViewIndex = source.IndexOf("<WebView2", StringComparison.Ordinal);
        var paletteIndex = source.IndexOf("<local:PaletteView", StringComparison.Ordinal);

        Assert.True(webViewIndex >= 0);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, "<WebView2").Cast<object>());
        Assert.True(paletteIndex > webViewIndex, "PaletteView must be a later sibling so it renders over WebView2.");
        Assert.Contains("x:Name=\"PaletteOverlay\"", source, StringComparison.Ordinal);
        Assert.Contains("Grid.RowSpan=\"2\"", source, StringComparison.Ordinal);
        Assert.Contains("Canvas.ZIndex=\"100\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Xaml_defines_a_stable_accessible_overlay_without_a_second_WebView()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "PaletteView.xaml"));

        Assert.Contains("x:Class=\"AgTerm.App.PaletteView\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DismissSurface\"", source, StringComparison.Ordinal);
        Assert.Contains("Tapped=\"OnDismissSurfaceTapped\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PalettePanel\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"520\"", source, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"8\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"QueryBox\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Filter palette items\"", source, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"OnQueryBoxKeyDown\"", source, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ResultsList\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight=\"320\"", source, StringComparison.Ordinal);
        Assert.Contains("SizeChanged=\"OnPaletteSizeChanged\"", source, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Palette results\"", source, StringComparison.Ordinal);
        Assert.Contains("IsItemClickEnabled=\"True\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind StatusGlyph, Mode=OneWay}\"", source, StringComparison.Ordinal);
        Assert.Contains("Converter={StaticResource PaletteStatusBrushConverter}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded=\"OnStatusGlyphLoaded\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WebView2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Code_behind_accepts_only_immutable_presentation_items_and_handles_keyboard_and_focus()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "PaletteView.xaml.cs"));

        Assert.Contains(
            "public void Open(string placeholder, IReadOnlyList<PaletteItemViewModel> items)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("VirtualKey.Down", source, StringComparison.Ordinal);
        Assert.Contains("VirtualKey.Up", source, StringComparison.Ordinal);
        Assert.Contains("VirtualKey.Enter", source, StringComparison.Ordinal);
        Assert.Contains("VirtualKey.Escape", source, StringComparison.Ordinal);
        Assert.Contains("QueryBox.Focus(FocusState.Programmatic)", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue", source, StringComparison.Ordinal);
        Assert.Contains("ItemAccepted", source, StringComparison.Ordinal);
        Assert.Contains("Cancelled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OnStatusGlyphLoaded", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Session", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuiltinAction", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ILiveStoreCoordinator", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_palette_opening_materializes_store_and_keymap_projection_through_coordinator()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("OpenPaletteAsync", source, StringComparison.Ordinal);
        Assert.Contains("host.StoreCoordinator.ReadAsync", source, StringComparison.Ordinal);
        Assert.Contains("var keymap = _hotkeyStates.Active.Keymap", source, StringComparison.Ordinal);
        Assert.Matches(@"WindowPaletteProjection\.Create\(\s*mode,\s*store,\s*keymap,", source);
        Assert.Contains("PaletteOverlay.Open(", source, StringComparison.Ordinal);
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