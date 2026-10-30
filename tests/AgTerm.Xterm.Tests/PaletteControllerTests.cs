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

    private static (PaletteController Controller, List<string?> Seen) OpenThree()
    {
        var controller = new PaletteController();
        var seen = new List<string?>();
        controller.Open(
            [Item("a", "Alpha"), Item("b", "Bravo"), Item("c", "Charlie")],
            new PaletteOpenOptions { InitialSelectionId = "b" });
        controller.SelectionChanged += item => seen.Add(item?.Id);
        return (controller, seen);
    }

    [Fact]
    public void Open_does_not_report_a_selection_change()
    {
        var controller = new PaletteController();
        var seen = new List<string?>();
        controller.SelectionChanged += item => seen.Add(item?.Id);

        controller.Open([Item("a", "Alpha")]);

        Assert.Empty(seen);
    }

    [Fact]
    public void Moving_and_clicking_report_the_new_row()
    {
        var (controller, seen) = OpenThree();

        controller.MoveSelection(1);
        controller.SelectIndex(0);

        Assert.Equal(["c", "a"], seen);
    }

    [Fact]
    public void A_move_that_stays_on_the_same_row_reports_nothing()
    {
        var (controller, seen) = OpenThree();

        controller.SelectIndex(1);
        controller.MoveSelection(5);
        controller.MoveSelection(1);

        Assert.Equal(["c"], seen);
    }

    [Fact]
    public void A_query_that_changes_the_top_row_reports_it()
    {
        var (controller, seen) = OpenThree();

        controller.SetQuery("char");

        Assert.Equal(["c"], seen);
    }

    [Fact]
    public void A_query_that_matches_nothing_reports_no_row()
    {
        var (controller, seen) = OpenThree();

        controller.SetQuery("zzzz");

        Assert.Equal([null], seen);
    }

    [Fact]
    public void Close_does_not_report_a_selection_change()
    {
        var (controller, seen) = OpenThree();

        controller.Close();

        Assert.Empty(seen);
    }

    [Fact]
    public void Moving_the_selection_keeps_the_filtered_list_instance()
    {
        var (controller, _) = OpenThree();
        var shown = controller.FilteredItems;

        controller.MoveSelection(1);
        controller.MoveSelection(-2);
        controller.SelectIndex(1);

        Assert.Same(shown, controller.FilteredItems);
    }

    [Fact]
    public void A_new_query_or_a_new_open_gives_a_new_filtered_list_instance()
    {
        var (controller, _) = OpenThree();
        var opened = controller.FilteredItems;

        controller.SetQuery("a");
        var queried = controller.FilteredItems;
        controller.Open([Item("a", "Alpha"), Item("b", "Bravo"), Item("c", "Charlie")]);

        Assert.NotSame(opened, queried);
        Assert.NotSame(queried, controller.FilteredItems);
        Assert.NotSame(opened, controller.FilteredItems);
    }

    [Fact]
    public void Close_then_open_gives_an_empty_list_then_a_new_list_instance()
    {
        var (controller, _) = OpenThree();
        var opened = controller.FilteredItems;

        controller.Close();
        var closed = controller.FilteredItems;
        controller.Open([Item("a", "Alpha")]);

        Assert.Empty(closed);
        Assert.NotSame(opened, closed);
        Assert.NotSame(closed, controller.FilteredItems);
        Assert.Equal(["a"], controller.FilteredItems.Select(item => item.Id));
    }

    [Fact]
    public void Opening_with_no_items_gives_a_list_instance_other_than_the_closed_one()
    {
        var controller = new PaletteController();
        var closed = controller.FilteredItems;

        controller.Open([]);

        Assert.Empty(controller.FilteredItems);
        Assert.NotSame(closed, controller.FilteredItems);
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
    public void The_query_box_leaves_focus_in_an_open_content_dialog()
    {
        var code = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "PaletteView.xaml.cs"));

        // Open(takeFocus: true) and window activation both focus through FocusQuery, now and on the dispatcher
        Assert.Matches(
            @"public void FocusQuery\(\)\s*\{\s*FocusQueryUnlessContentDialogOpen\(\);\s*DispatcherQueue\.TryEnqueue\(FocusQueryUnlessContentDialogOpen\);\s*\}",
            code);
        Assert.Matches(
            @"private void FocusQueryUnlessContentDialogOpen\(\)\s*\{(?:\s*//[^\n]*)*\s*if \(!IsContentDialogOpen\(\)\)\s*QueryBox\.Focus\(FocusState\.Programmatic\);\s*\}",
            code);
        Assert.Matches(
            @"private bool IsContentDialogOpen\(\) =>\s*XamlRoot is not null\s*&& VisualTreeHelper\.GetOpenPopupsForXamlRoot\(XamlRoot\)\.Any\(popup => popup\.Child is ContentDialog\);",
            code);
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
        var arm = source.IndexOf("await ArmHotkeysAsync(host)", StringComparison.Ordinal);
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

        Assert.Contains("_terminalFocus.Activated(modalOpen: AnyPaletteOpen ||", source, StringComparison.Ordinal);
        var reset = source.IndexOf("_terminalFocus.Cancel();\n        PaletteOverlay.Open(PickPalette", StringComparison.Ordinal);
        var resetCrlf = source.IndexOf("_terminalFocus.Cancel();\r\n        PaletteOverlay.Open(PickPalette", StringComparison.Ordinal);
        Assert.True(reset >= 0 || resetCrlf >= 0, "the flag must be cleared right before the pick opens.");
    }

    // manual check (workspace theme step 8): a layout accepted after the palette opened moved the
    // keyboard to the WebView, so Esc and typing reached the terminal instead of the palette
    [Fact]
    public void MainWindow_moves_no_terminal_focus_while_a_palette_is_open()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Matches(
            @"private void FocusTerminal\(string paneId\)\s*\{\s*if \(_applicationHost\.Quick\.HoldsKey\) return;\s*if \(AskOverlay\.IsOpen\)\s*\{[^}]*\}\s*(//[^\r\n]*\s*)*if \(AnyPaletteOpen \|\| KeybindingsOverlay\.IsOpen\)\s*return;\s*_terminalFocus\.Request\(",
            source);
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
        Assert.Contains("local:SessionStatusGlyph", source, StringComparison.Ordinal);
        Assert.Contains("ColorHex=\"{x:Bind StatusColor, Mode=OneWay}\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Loaded=\"OnStatusGlyphLoaded\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WebView2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Code_behind_accepts_presentation_snapshots_without_mutable_store_access_and_handles_keyboard_and_focus()
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
        Assert.DoesNotMatch(@"\bSession\b", source);
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