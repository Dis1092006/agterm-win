using AgTerm.App;

namespace AgTerm.Xterm.Tests;

// upstream titlebarTrailingActions: recent sessions + attention | scratch + split + overlays | dashboard + quick
// terminal + custom commands, each gated by its interface element (the bell by attentionButtonEnabled), with a divider only
// between two full groups. MainWindow is a WinUI type the test project does not compile, so the wiring is
// pinned in source; TitleBarStateTests and AppSettingsTests cover the state and the divider rule.
public sealed class TitleBarActionsSourceTests
{
    [Fact]
    public void Title_bar_right_header_holds_upstreams_buttons_in_order()
    {
        var xaml = Source("MainWindow.xaml");
        var start = xaml.IndexOf("<TitleBar.RightHeader>", StringComparison.Ordinal);
        Assert.True(start >= 0, "TitleBar.RightHeader not found");
        var header = xaml[start..xaml.IndexOf("</TitleBar.RightHeader>", start, StringComparison.Ordinal)];

        string[] order =
        [
            "recent-sessions-button", "attention-button", "x:Name=\"DividerAfterA\"",
            "scratch-toggle", "split-toggle", "overlay-toggle", "x:Name=\"DividerAfterB\"",
            "dashboard-toggle-button", "quick-terminal-toggle", "custom-commands-button",
        ];
        var positions = order.Select(token => header.IndexOf(token, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order(), positions);
        Assert.Contains("Opening=\"OnRecentSessionsFlyoutOpening\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_button_is_last_and_opens_the_two_installer_items()
    {
        var xaml = Source("MainWindow.xaml");
        var start = xaml.IndexOf("<TitleBar.RightHeader>", StringComparison.Ordinal);
        var header = xaml[start..xaml.IndexOf("</TitleBar.RightHeader>", start, StringComparison.Ordinal)];
        var help = header.IndexOf("AutomationProperties.AutomationId=\"help-button\"", StringComparison.Ordinal);
        Assert.True(help > header.IndexOf("custom-commands-button", StringComparison.Ordinal), "help-button must follow custom-commands-button");
        // the modal exit buttons sit after the TitleBarActions panel, so Help stays last inside it
        var actionsEnd = header.IndexOf("</StackPanel>", help, StringComparison.Ordinal);
        Assert.Equal(
            header.IndexOf("<Button x:Name=\"TerminalZoomExitButton\"", StringComparison.Ordinal),
            header.IndexOf("<Button x:Name=", help, StringComparison.Ordinal));
        Assert.True(actionsEnd < header.IndexOf("<Button x:Name=", help, StringComparison.Ordinal));

        var button = header[header.LastIndexOf("<Button", help, StringComparison.Ordinal)..];
        Assert.Contains("x:Name=\"HelpButton\"", button, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Help\"", button, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"Help\"", button, StringComparison.Ordinal);
        Assert.Contains("Placement=\"BottomEdgeAlignedRight\"", button, StringComparison.Ordinal);
        string[] items =
        [
            "<MenuFlyoutItem x:Name=\"InstallCommandLineToolItem\"",
            "Click=\"OnInstallCommandLineToolClick\"",
            "<MenuFlyoutItem x:Name=\"InstallAgentSkillItem\"",
            "Click=\"OnInstallAgentSkillClick\"",
            "<MenuFlyoutItem x:Name=\"InstallAgentStatusHooksItem\"",
            "Click=\"OnInstallAgentStatusHooksClick\"",
        ];
        var positions = items.Select(token => button.IndexOf(token, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order(), positions);

        // the item texts come from the constants the palette and the skill text use
        var window = Source("MainWindow.xaml.cs");
        Assert.Contains("InstallCommandLineToolItem.Text = InstallerCommands.InstallCommandLineTool;", window, StringComparison.Ordinal);
        Assert.Contains("InstallAgentSkillItem.Text = InstallerCommands.InstallAgentSkill;", window, StringComparison.Ordinal);
        Assert.Contains("InstallAgentStatusHooksItem.Text = InstallerCommands.InstallAgentStatusHooks;", window, StringComparison.Ordinal);
        Assert.Contains("RunInstallerAsync(InstallerCommands.InstallCommandLineToolId)",
            MethodBody(window, "private void OnInstallCommandLineToolClick("), StringComparison.Ordinal);
        Assert.Contains("RunInstallerAsync(InstallerCommands.InstallAgentSkillId)",
            MethodBody(window, "private void OnInstallAgentSkillClick("), StringComparison.Ordinal);
        Assert.Contains("RunInstallerAsync(InstallerCommands.InstallAgentStatusHooksId)",
            MethodBody(window, "private void OnInstallAgentStatusHooksClick("), StringComparison.Ordinal);
        Assert.Contains("executeInstallerCommandAsync:", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Palette_app_rows_reach_the_same_paths_as_the_sidebar_and_the_wire()
    {
        var window = Source("MainWindow.xaml.cs");
        Assert.Contains("executeAppCommandAsync: ExecutePaletteAppCommandAsync", window, StringComparison.Ordinal);

        var body = MethodBody(window, "private async Task ExecutePaletteAppCommandAsync(");
        foreach (var command in PaletteAppCommands.All)
            Assert.Contains($"case PaletteAppCommand.{command}:", body, StringComparison.Ordinal);
        Assert.Contains("editor.OpenAsync(", body, StringComparison.Ordinal);
        Assert.Contains("ConfigEditor.Command(Path.GetFileName(keymapPath))", body, StringComparison.Ordinal);
        Assert.Contains("_applicationHost.ReloadKeymapAsync(", body, StringComparison.Ordinal);
        Assert.Contains("_applicationHost.ReloadConfigAsync(", body, StringComparison.Ordinal);
        Assert.Contains("_applicationHost.ReloadHooksAsync(", window, StringComparison.Ordinal);
        Assert.Contains("ReloadHooksReportingAsync(", body, StringComparison.Ordinal);
        Assert.Contains("_applicationHost.Hooks", body, StringComparison.Ordinal);
        Assert.Contains("EnsureStarter()", body, StringComparison.Ordinal);
        Assert.Contains("_sidebar?.RequestClearFlags()", body, StringComparison.Ordinal);
        Assert.Contains("_sidebar?.ClearFocus()", body, StringComparison.Ordinal);
        Assert.Contains("_sidebar?.AddActiveWorkspaceToFocus()", body, StringComparison.Ordinal);
        Assert.Contains("store.ExpandAllWorkspaces()", body, StringComparison.Ordinal);
        Assert.Contains("store.CollapseOtherWorkspaces()", body, StringComparison.Ordinal);

        // closing the keymap editor reloads the keymap in every window, like Reload Keymap
        Assert.Contains("() => _applicationHost.ReloadKeymapAsync()", window, StringComparison.Ordinal);
        Assert.Contains("_keymapEditOverlay?.Dispose();", window, StringComparison.Ordinal);
        // and closing the hooks editor reloads the hooks
        Assert.Contains("() => ReloadHooksReportingAsync()", window, StringComparison.Ordinal);
        Assert.Contains("_hooksEditOverlay?.Dispose();", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Hook_banners_reach_the_ui_thread_from_one_place()
    {
        // upstream SettingsModel.reloadHooks posts the diagnostics banner for every reload (palette, editor
        // close, Settings, agtermctl hooks reload, a moved config directory, launch); the controller raises
        // Reloaded and HookFailed off the UI thread, so App hops to the dispatcher before touching a window
        var app = Source("App.xaml.cs");
        Assert.Contains("hooks.Reloaded += count =>", app, StringComparison.Ordinal);
        Assert.Contains("hooks.HookFailed += (kind, command, detail) =>", app, StringComparison.Ordinal);
        var report = MethodBody(app, "private void ReportHooksNotice(");
        Assert.Contains("_dispatcherQueue?.TryEnqueue(", report, StringComparison.Ordinal);
        Assert.Equal(1, Count(app, "HooksNotices.Diagnostics("));

        var window = Source("MainWindow.xaml.cs");
        Assert.DoesNotContain("HooksNotices.Diagnostics(", window, StringComparison.Ordinal);
        Assert.Contains("QueueFailureNotice(", MethodBody(window, "private void ReportPaletteReload("), StringComparison.Ordinal);
    }

    private static int Count(string source, string token)
    {
        var count = 0;
        for (var at = source.IndexOf(token, StringComparison.Ordinal); at >= 0; at = source.IndexOf(token, at + 1, StringComparison.Ordinal))
            count++;
        return count;
    }

    [Fact]
    public void Header_content_keeps_the_title_bar_at_its_compact_height()
    {
        // TitleBar switches to TitleBarExpandedHeight (48) once it has header content; the buttons fit
        // the compact 32, so the expanded height is overridden on the control itself
        var xaml = Source("MainWindow.xaml");
        var start = xaml.IndexOf("<TitleBar.Resources>", StringComparison.Ordinal);
        Assert.True(start >= 0, "TitleBar.Resources not found");
        var resources = xaml[start..xaml.IndexOf("</TitleBar.Resources>", start, StringComparison.Ordinal)];

        Assert.Contains("<x:Double x:Key=\"TitleBarExpandedHeight\">32</x:Double>", resources, StringComparison.Ordinal);
    }

    [Fact]
    public void Buttons_follow_the_snapshot_and_the_settings()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("ApplyTitleBarState(snapshot.TitleBar);",
            MethodBody(window, "private void ApplyChrome(SidebarSnapshot snapshot)"), StringComparison.Ordinal);
        Assert.Contains("RefreshTitleBarActions(settings);",
            MethodBody(window, "private void ApplyInterfaceElements(AppSettings settings)"), StringComparison.Ordinal);
        var refresh = MethodBody(window, "private void RefreshTitleBarActions(AppSettings settings)");
        Assert.Contains("InterfaceElementExtensions.TitlebarGroupDividers(", refresh, StringComparison.Ordinal);
        Assert.Contains("settings.AttentionButtonEnabled == true", refresh, StringComparison.Ordinal);
        foreach (var element in new[] { "RecentSessions", "Scratch", "Split", "OverlayToggle", "Dashboard", "QuickTerminal", "CustomCommands" })
            Assert.Contains($"InterfaceElement.{element})", refresh, StringComparison.Ordinal);
        Assert.Contains("(showScratch ? 1 : 0) + (showSplit ? 1 : 0) + (showOverlay ? 1 : 0)", refresh, StringComparison.Ordinal);
        Assert.Contains("(showDashboard ? 1 : 0) + (showQuick ? 1 : 0) + (showCustom ? 1 : 0)", refresh, StringComparison.Ordinal);
    }

    // upstream 7f450d17 (#790) overlayToggleButton: disabled with no overlay, crossed out while one is
    // hidden, and the tooltip names what a click does. Zoom and the dashboard hide the whole row.
    [Fact]
    public void Overlay_button_follows_the_active_sessions_overlay()
    {
        var refresh = MethodBody(Source("MainWindow.xaml.cs"), "private void RefreshTitleBarActions(AppSettings settings)");

        Assert.Contains("OverlayButton.Visibility = VisibleIf(showOverlay);", refresh, StringComparison.Ordinal);
        Assert.Contains("OverlayButton.IsEnabled = state.Overlay != TitleBarOverlay.None;", refresh, StringComparison.Ordinal);
        Assert.Contains("OverlaySlash.Visibility = VisibleIf(state.Overlay == TitleBarOverlay.Hidden);", refresh, StringComparison.Ordinal);
        Assert.Contains("state.Overlay == TitleBarOverlay.Shown ? \"Hide Overlays\" : \"Show Overlays\",", refresh, StringComparison.Ordinal);
        Assert.Contains("_keymap, BuiltinAction.ToggleOverlay));", refresh, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OverlaySlash\"", Source("MainWindow.xaml"), StringComparison.Ordinal);
    }

    // a Windows extension: upstream draws no open state on these two buttons
    [Fact]
    public void Dashboard_and_quick_terminal_buttons_mark_an_open_surface()
    {
        var xaml = Source("MainWindow.xaml");
        foreach (var indicator in new[] { "DashboardOpenIndicator", "QuickTerminalOpenIndicator" })
            Assert.Contains($"<Rectangle x:Name=\"{indicator}\" Style=\"{{StaticResource TitleBarOpenIndicatorStyle}}\" />",
                xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Fill\" Value=\"{ThemeResource AccentFillColorDefaultBrush}\" />", xaml, StringComparison.Ordinal);

        var window = Source("MainWindow.xaml.cs");
        var refresh = MethodBody(window, "private void RefreshTitleBarActions(AppSettings settings)");
        Assert.Contains("ShowOpen(DashboardButton, DashboardOpenIndicator, _selection.DashboardOpen);", refresh, StringComparison.Ordinal);
        Assert.Contains("ShowOpen(QuickTerminalButton, QuickTerminalOpenIndicator, _selection.QuickVisible);", refresh, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetItemStatus(button, open ? \"Open\" : string.Empty);",
            MethodBody(window, "private static void ShowOpen("), StringComparison.Ordinal);

        var selectionUpdate = window[window.IndexOf("_selection = snapshot;", StringComparison.Ordinal)..];
        selectionUpdate = selectionUpdate[..selectionUpdate.IndexOf("SubmitLayout();", StringComparison.Ordinal)];
        Assert.Contains("RefreshTitleBarActions(_settings);", selectionUpdate, StringComparison.Ordinal);
    }

    // upstream 28f79f17 (#582) and 68e17937 (#584) customCommandsButton: a menu of the keymap's commands, the
    // most-used ones first past five, enabled with commands and an active session. A row runs the command
    // the way the palette does, then focuses the terminal. The menu holds auto-follow while it is open.
    [Fact]
    public void Custom_commands_button_opens_the_keymap_commands()
    {
        var xaml = Source("MainWindow.xaml");
        var start = xaml.IndexOf("<TitleBar.RightHeader>", StringComparison.Ordinal);
        var header = xaml[start..xaml.IndexOf("</TitleBar.RightHeader>", start, StringComparison.Ordinal)];
        var id = header.IndexOf("AutomationProperties.AutomationId=\"custom-commands-button\"", StringComparison.Ordinal);
        Assert.True(id >= 0, "custom-commands-button not found");
        var button = header[header.LastIndexOf("<Button", id, StringComparison.Ordinal)..header.IndexOf("</Button>", id, StringComparison.Ordinal)];
        Assert.Contains("x:Name=\"CustomCommandsButton\"", button, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Custom commands\"", button, StringComparison.Ordinal);
        Assert.Contains("<MenuFlyout x:Name=\"CustomCommandsFlyout\"", button, StringComparison.Ordinal);
        Assert.Contains("Opening=\"OnCustomCommandsFlyoutOpening\"", button, StringComparison.Ordinal);

        var window = Source("MainWindow.xaml.cs");
        var refresh = MethodBody(window, "private void RefreshTitleBarActions(AppSettings settings)");
        Assert.Contains("CustomCommandsButton.Visibility = VisibleIf(showCustom);", refresh, StringComparison.Ordinal);
        Assert.Contains("CustomCommandsButton.IsEnabled = _keymap.Commands.Count > 0 && state.HasActiveSession;", refresh, StringComparison.Ordinal);
        Assert.Contains("CustomCommandsFlyout.Hide();", refresh, StringComparison.Ordinal);
        Assert.Contains("WindowsShortcutText.Hint(\"Custom commands\", _keymap, BuiltinAction.CustomCommandPalette)", refresh, StringComparison.Ordinal);

        var opening = MethodBody(window, "private void OnCustomCommandsFlyoutOpening(");
        Assert.Contains("_customCommandsOpen = true;", opening, StringComparison.Ordinal);
        Assert.Contains("RefreshUiAutoFollowSuppression();", opening, StringComparison.Ordinal);
        Assert.Contains("CustomCommandMenuGroups.From(_keymap.Commands, _applicationHost.CustomCommandUsage.Load())", opening, StringComparison.Ordinal);
        Assert.Contains("new MenuFlyoutSeparator()", opening, StringComparison.Ordinal);
        Assert.Contains("\"custom-command-top-row\"", opening, StringComparison.Ordinal);
        Assert.Contains("\"custom-command-row\"", opening, StringComparison.Ordinal);
        Assert.Contains("KeyboardAcceleratorTextOverride = command.Shortcut", window, StringComparison.Ordinal);
        Assert.Contains("_customCommandsOpen = false;", window, StringComparison.Ordinal);
        Assert.Contains("|| _customCommandsOpen", window, StringComparison.Ordinal);

        var run = MethodBody(window, "private async Task RunCustomCommandFromMenuAsync(");
        string[] steps =
        [
            "NoteUserActivity();",
            "ExecuteCustomCommandAsync(new CustomCommandExecutionRequest(commandId, CustomCommandSource.Palette))",
            "FocusActiveTerminal();",
        ];
        var positions = steps.Select(step => run.IndexOf(step, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order(), positions);

        // every window counts runs in the one store the menu reads
        Assert.Contains("new CustomCommandProcessRunner(usage: _applicationHost.CustomCommandUsage)", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Buttons_run_the_same_builtin_actions_as_the_hotkeys()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("ExecuteTitleBarAction(BuiltinAction.ShowAttention)", window, StringComparison.Ordinal);
        Assert.Contains("ExecuteTitleBarAction(BuiltinAction.ToggleScratch)", window, StringComparison.Ordinal);
        Assert.Contains("ExecuteTitleBarAction(SplitToggleAction(_titleBarState))", window, StringComparison.Ordinal);
        Assert.Contains("BuiltinAction.ToggleHorizontalSplit : BuiltinAction.ToggleSplit", window, StringComparison.Ordinal);
        Assert.Contains("ExecuteTitleBarAction(BuiltinAction.Dashboard)", window, StringComparison.Ordinal);
        Assert.Contains("ExecuteTitleBarAction(BuiltinAction.QuickTerminal)", window, StringComparison.Ordinal);
        Assert.Contains("ExecuteTitleBarAction(BuiltinAction.ToggleOverlay)", window, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteActions.SelectSession(store, sessionId)",
            MethodBody(window, "private async Task SelectRecentSessionAsync(Guid sessionId)"), StringComparison.Ordinal);
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
