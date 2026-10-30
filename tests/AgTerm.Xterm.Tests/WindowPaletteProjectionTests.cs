using AgTerm.App;
using AgTerm.Core.Install;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class WindowPaletteProjectionTests
{
    [Fact]
    public void Sessions_uses_navigable_sessions_with_workspace_and_subtitle_context()
    {
        var store = TestStores.Scratch();
        var visibleWorkspace = store.AddWorkspace("Visible");
        var hiddenWorkspace = store.AddWorkspace("Hidden");
        var visible = store.AddSession(visibleWorkspace.Id, @"C:\visible", select: false)!;
        var hidden = store.AddSession(hiddenWorkspace.Id, @"C:\hidden", select: false)!;
        visible.CustomName = "Visible Session";
        hidden.CustomName = "Hidden Session";
        store.SetAgentIndicator(
            new AgentIndicator(AgentStatus.Blocked, Color: "#123456", Shape: StatusShape.Diamond),
            visible.Id);
        store.FocusedWorkspaceId = visibleWorkspace.Id;

        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.Sessions,
            store,
            Keymap.Empty,
            StateProvider());

        var item = Assert.Single(snapshot.Items);
        Assert.Equal(visible.Id.ToString("D"), item.Id);
        Assert.Equal("Visible Session", item.Title);
        Assert.Equal(@"Visible · C:\visible", item.Subtitle);
        Assert.Equal(AgentStatus.Blocked, item.Status);
        Assert.Equal("#123456", item.StatusColor);
        Assert.Equal(StatusShape.Diamond, item.StatusShape);
        Assert.False(snapshot.PreserveInputOrderForEmptyQuery);
    }

    [Fact]
    public void Attention_uses_all_non_idle_sessions_and_preserves_attention_order_for_empty_query()
    {
        var store = TestStores.Scratch();
        var firstWorkspace = store.AddWorkspace("One");
        var secondWorkspace = store.AddWorkspace("Two");
        var completed = store.AddSession(firstWorkspace.Id, @"C:\completed", select: false)!;
        var active = store.AddSession(firstWorkspace.Id, @"C:\active", select: false)!;
        var blocked = store.AddSession(secondWorkspace.Id, @"C:\blocked", select: false)!;
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Completed), completed.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), active.Id);
        store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), blocked.Id);
        store.FocusedWorkspaceId = firstWorkspace.Id;

        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.Attention,
            store,
            Keymap.Empty,
            StateProvider());

        Assert.Equal([blocked.Id, active.Id, completed.Id], snapshot.Items.Select(ParseSessionId));
        Assert.True(snapshot.PreserveInputOrderForEmptyQuery);
        Assert.Equal(AgentStatus.Blocked, snapshot.Items[0].Status);
    }

    [Fact]
    public void Commands_filters_by_router_state_and_uses_dynamic_titles_and_live_shortcuts()
    {
        var store = TestStores.Scratch();
        store.SidebarMode = SidebarMode.Flagged;
        var workspace = store.AddWorkspace("Work");
        var session = store.AddSession(workspace.Id, @"C:\work")!;
        session.Flagged = true;
        var states = StateProvider(
            enabled: [BuiltinAction.ToggleFlag, BuiltinAction.ToggleFlaggedView, BuiltinAction.NewSession]);
        var (keymap, diagnostics) = KeymapParser.Parse("map alt+n new_session");
        Assert.Empty(diagnostics);

        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.Commands,
            store,
            keymap,
            states);

        Assert.Equal(
            ["New Session", "Show All Sessions", "Unflag Session"],
            snapshot.Items
                .Where(item => WindowPaletteProjection.ParseAction(item.Id) is not null)
                .Select(item => item.Title)
                .OrderBy(title => title, StringComparer.Ordinal));
        var newSession = Assert.Single(snapshot.Items, item => item.Id == BuiltinAction.NewSession.WireName());
        Assert.Equal(keymap.GlyphHint(BuiltinAction.NewSession), newSession.Shortcut);
        Assert.DoesNotContain(snapshot.Items, item => item.Id == BuiltinAction.CommandPalette.WireName());
    }

    [Fact]
    public void Commands_end_with_the_app_rows_and_the_installer_items_whatever_the_router_state()
    {
        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.Commands,
            TestStores.Scratch(),
            Keymap.Empty,
            StateProvider());

        Assert.Equal(
            [
                ("app:edit-keymap", "Edit Keymap"),
                ("app:reload-keymap", "Reload Keymap"),
                ("app:reload-config", "Reload Config"),
                ("app:expand-workspaces", "Expand Workspaces"),
                ("app:collapse-workspaces", "Collapse Workspaces"),
                (InstallerCommands.InstallCommandLineToolId, InstallerCommands.InstallCommandLineTool),
                (InstallerCommands.InstallAgentSkillId, InstallerCommands.InstallAgentSkill),
                (InstallerCommands.InstallAgentStatusHooksId, InstallerCommands.InstallAgentStatusHooks),
            ],
            snapshot.Items.Select(item => (item.Id, item.Title)));
        Assert.All(snapshot.Items, item => Assert.Null(item.Shortcut));
    }

    [Fact]
    public void Commands_offer_clear_flagged_and_clear_focus_only_when_there_is_something_to_clear()
    {
        var store = TestStores.Scratch();
        var workspace = store.AddWorkspace("Work");
        var session = store.AddSession(workspace.Id, @"C:\work")!;

        static string[] AppRowIds(AppStore store) => WindowPaletteProjection.Create(
                WindowPaletteMode.Commands, store, Keymap.Empty, StateProvider()).Items
            .Where(item => item.Id.StartsWith("app:clear-", StringComparison.Ordinal))
            .Select(item => item.Id)
            .ToArray();

        Assert.Empty(AppRowIds(store));

        session.Flagged = true;
        Assert.Equal(["app:clear-flagged"], AppRowIds(store));

        session.Flagged = false;
        store.FocusedWorkspaceId = workspace.Id;
        Assert.Equal(["app:clear-focus"], AppRowIds(store));

        session.Flagged = true;
        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.Commands, store, Keymap.Empty, StateProvider());
        Assert.Equal(
            [("app:clear-flagged", "Clear Flagged"), ("app:clear-focus", "Clear Focus")],
            snapshot.Items
                .Where(item => item.Id.StartsWith("app:clear-", StringComparison.Ordinal))
                .Select(item => (item.Id, item.Title)));
    }

    [Fact]
    public void Commands_offer_expand_and_collapse_only_in_the_workspace_tree()
    {
        var store = TestStores.Scratch();
        store.SidebarMode = SidebarMode.Flagged;

        var ids = WindowPaletteProjection.Create(
            WindowPaletteMode.Commands, store, Keymap.Empty, StateProvider()).Items.Select(item => item.Id);

        Assert.DoesNotContain("app:expand-workspaces", ids);
        Assert.DoesNotContain("app:collapse-workspaces", ids);
        Assert.Contains("app:edit-keymap", ids);
        Assert.Contains("app:reload-keymap", ids);
        Assert.Contains("app:reload-config", ids);
    }

    [Fact]
    public void App_rows_are_neither_builtin_actions_nor_installer_items()
    {
        var rows = new (string Id, PaletteAppCommand Command)[]
        {
            ("app:edit-keymap", PaletteAppCommand.EditKeymap),
            ("app:reload-keymap", PaletteAppCommand.ReloadKeymap),
            ("app:reload-config", PaletteAppCommand.ReloadConfig),
            ("app:clear-flagged", PaletteAppCommand.ClearFlagged),
            ("app:clear-focus", PaletteAppCommand.ClearFocus),
            ("app:expand-workspaces", PaletteAppCommand.ExpandWorkspaces),
            ("app:collapse-workspaces", PaletteAppCommand.CollapseWorkspaces),
        };
        Assert.Equal(rows.Select(row => row.Command), PaletteAppCommands.All);
        foreach (var (id, command) in rows)
        {
            Assert.Equal(id, PaletteAppCommands.Id(command));
            Assert.Equal(command, WindowPaletteProjection.ParseAppCommand(id));
            Assert.Null(WindowPaletteProjection.ParseAction(id));
            Assert.Null(WindowPaletteProjection.ParseInstallerCommand(id));
        }
        Assert.Null(WindowPaletteProjection.ParseAppCommand(InstallerCommands.InstallAgentSkillId));
        Assert.Null(WindowPaletteProjection.ParseAppCommand(BuiltinAction.ToggleFlaggedView.WireName()));
        Assert.Null(WindowPaletteProjection.ParseAppCommand("app:edit-ghostty-config"));
    }

    [Fact]
    public void Installer_items_are_not_builtin_actions()
    {
        foreach (var id in new[] { InstallerCommands.InstallCommandLineToolId, InstallerCommands.InstallAgentSkillId, InstallerCommands.InstallAgentStatusHooksId })
        {
            Assert.Null(WindowPaletteProjection.ParseAction(id));
            Assert.Equal(id, WindowPaletteProjection.ParseInstallerCommand(id));
        }
        Assert.Null(WindowPaletteProjection.ParseInstallerCommand(BuiltinAction.ToggleSplit.WireName()));
        Assert.Null(WindowPaletteProjection.ParseInstallerCommand("app:something-else"));
        Assert.Equal(41, BuiltinActions.All.Count);
    }

    [Fact]
    public void CustomCommands_includes_palette_only_rows_and_uses_raw_shortcuts()
    {
        var bound = new CustomCommand("Bound", "echo bound", "ctrl+b", Guid.NewGuid());
        var paletteOnly = new CustomCommand("Palette Only", "echo palette", "", Guid.NewGuid());
        var keymap = new Keymap(new Dictionary<BuiltinAction, Chord>(), [paletteOnly, bound]);

        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.CustomCommands,
            TestStores.Scratch(),
            keymap,
            StateProvider());

        Assert.Equal(2, snapshot.Items.Count);
        Assert.Equal($"custom-{bound.Id:D}", snapshot.Items.Single(item => item.Title == "Bound").Id);
        Assert.Equal("ctrl+b", snapshot.Items.Single(item => item.Title == "Bound").Shortcut);
        Assert.Null(snapshot.Items.Single(item => item.Title == "Palette Only").Shortcut);
        Assert.All(snapshot.Items, item => Assert.Null(item.Badge));
    }

    [Fact]
    public void Themes_uses_stable_catalog_ids_and_marks_the_current_theme()
    {
        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.Themes,
            TestStores.Scratch(),
            Keymap.Empty,
            StateProvider(),
            ThemeCatalog.FromSources(
            [
                ("agterm", ThemeTexts.Valid()),
                ("Builtin Light", ThemeTexts.Valid("#ffffff")),
                ("Dracula", ThemeTexts.Valid()),
            ]),
            currentTheme: "agterm");

        Assert.Equal("Select a theme…", snapshot.Placeholder);
        Assert.Equal(
            ["theme:__default__", "theme:agterm", "theme:Builtin Light", "theme:Dracula"],
            snapshot.Items.Select(item => item.Id));
        Assert.Equal("Current", snapshot.Items.Single(item => item.Id == "theme:agterm").Badge);
        Assert.True(WindowPaletteProjection.TryParseThemeName("theme:__default__", out var defaultTheme));
        Assert.Null(defaultTheme);
        Assert.True(WindowPaletteProjection.TryParseThemeName("theme:Dracula", out var dracula));
        Assert.Equal("Dracula", dracula);
        Assert.False(WindowPaletteProjection.TryParseThemeName("Dracula", out _));
    }

    [Fact]
    public void Open_palette_snapshot_does_not_change_after_keymap_reload_but_reopen_uses_new_commands()
    {
        var first = new CustomCommand("First", "echo first", "", Guid.NewGuid());
        var second = new CustomCommand("Second", "echo second", "ctrl+s", Guid.NewGuid());
        var store = TestStores.Scratch();
        var controller = new PaletteController();
        var firstSnapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.CustomCommands,
            store,
            new Keymap(new Dictionary<BuiltinAction, Chord>(), [first]),
            StateProvider());
        controller.Open(firstSnapshot.Items);

        var reloadedSnapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.CustomCommands,
            store,
            new Keymap(new Dictionary<BuiltinAction, Chord>(), [second]),
            StateProvider());

        Assert.Equal(["First"], controller.FilteredItems.Select(item => item.Title));
        controller.Open(reloadedSnapshot.Items);
        Assert.Equal(["Second"], controller.FilteredItems.Select(item => item.Title));
    }

    [Theory]
    [InlineData(WindowPaletteMode.Sessions, "Go to session…")]
    [InlineData(WindowPaletteMode.Commands, "Run an action…")]
    [InlineData(WindowPaletteMode.CustomCommands, "Run a custom command…")]
    [InlineData(WindowPaletteMode.Attention, "Go to a session that needs attention…")]
    [InlineData(WindowPaletteMode.Themes, "Select a theme…")]
    public void Mode_defines_the_expected_placeholder(WindowPaletteMode mode, string placeholder)
    {
        var snapshot = WindowPaletteProjection.Create(mode, TestStores.Scratch(), Keymap.Empty, StateProvider());

        Assert.Equal(placeholder, snapshot.Placeholder);
    }

    // the theme palettes preview colors on the window, so a scrim would show them darker than they are
    [Theory]
    [InlineData(WindowPaletteMode.Sessions, true)]
    [InlineData(WindowPaletteMode.Commands, true)]
    [InlineData(WindowPaletteMode.CustomCommands, true)]
    [InlineData(WindowPaletteMode.Attention, true)]
    [InlineData(WindowPaletteMode.Themes, false)]
    public void Only_the_theme_palette_opens_without_a_scrim(WindowPaletteMode mode, bool dimmed)
    {
        var snapshot = WindowPaletteProjection.Create(mode, TestStores.Scratch(), Keymap.Empty, StateProvider());

        Assert.Equal(dimmed, snapshot.Dimmed);
    }

    [Fact]
    public void The_workspace_theme_palette_opens_without_a_scrim()
    {
        var snapshot = WindowPaletteProjection.WorkspaceThemes(SmallCatalog(), "servers", currentTheme: null);

        Assert.False(snapshot.Dimmed);
    }

    [Fact]
    public void Stable_ids_round_trip_and_reject_other_item_kinds()
    {
        var sessionId = Guid.NewGuid();
        var customId = Guid.NewGuid();

        Assert.Equal(sessionId, WindowPaletteProjection.ParseSessionId(sessionId.ToString("D")));
        Assert.Null(WindowPaletteProjection.ParseSessionId($"custom-{customId:D}"));
        Assert.Equal(BuiltinAction.ToggleSplit, WindowPaletteProjection.ParseAction("toggle_split"));
        Assert.Null(WindowPaletteProjection.ParseAction("custom-command"));
        Assert.Equal(customId, WindowPaletteProjection.ParseCustomCommandId($"custom-{customId:D}"));
        Assert.Null(WindowPaletteProjection.ParseCustomCommandId(sessionId.ToString("D")));
    }

    private static ThemeCatalog SmallCatalog() => ThemeCatalog.FromSources(
    [
        ("agterm", ThemeTexts.Valid()),
        ("Builtin Light", ThemeTexts.Valid("#ffffff")),
        ("Dracula", ThemeTexts.Valid()),
    ]);

    [Fact]
    public void Themes_open_on_the_theme_on_screen()
    {
        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.Themes, TestStores.Scratch(), Keymap.Empty, StateProvider(),
            SmallCatalog(), currentTheme: "Dracula");

        Assert.Equal("theme:Dracula", snapshot.InitialSelectionId);
    }

    [Fact]
    public void Themes_open_on_the_default_row_when_no_theme_is_on_screen()
    {
        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.Themes, TestStores.Scratch(), Keymap.Empty, StateProvider(),
            SmallCatalog(), currentTheme: null);

        Assert.Equal("theme:__default__", snapshot.InitialSelectionId);
    }

    [Fact]
    public void Themes_have_no_default_row_while_following_the_system_appearance()
    {
        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.Themes, TestStores.Scratch(), Keymap.Empty, StateProvider(),
            SmallCatalog(), currentTheme: "agterm", followsSystemAppearance: true);

        Assert.Equal(["theme:agterm", "theme:Builtin Light", "theme:Dracula"], snapshot.Items.Select(item => item.Id));
    }

    [Fact]
    public void Workspace_themes_list_the_global_theme_first_and_keep_that_order()
    {
        var snapshot = WindowPaletteProjection.WorkspaceThemes(SmallCatalog(), "servers", currentTheme: "Dracula");

        Assert.Equal(WindowPaletteMode.WorkspaceTheme, snapshot.Mode);
        Assert.Equal("Select a theme for servers…", snapshot.Placeholder);
        Assert.True(snapshot.PreserveInputOrderForEmptyQuery);
        Assert.Equal(
            ["theme:__default__", "theme:agterm", "theme:Builtin Light", "theme:Dracula"],
            snapshot.Items.Select(item => item.Id));
        Assert.Equal("Use global theme", snapshot.Items[0].Title);
    }

    [Fact]
    public void Workspace_themes_open_on_the_workspace_theme()
    {
        var snapshot = WindowPaletteProjection.WorkspaceThemes(SmallCatalog(), "servers", currentTheme: "Dracula");

        Assert.Equal("theme:Dracula", snapshot.InitialSelectionId);
        Assert.Equal("Current", snapshot.Items.Single(item => item.Id == "theme:Dracula").Badge);
        Assert.Null(snapshot.Items[0].Badge);
    }

    [Fact]
    public void Workspace_themes_open_on_the_global_row_without_a_workspace_theme()
    {
        var snapshot = WindowPaletteProjection.WorkspaceThemes(SmallCatalog(), "servers", currentTheme: null);

        Assert.Equal("theme:__default__", snapshot.InitialSelectionId);
        Assert.Equal("Current", snapshot.Items[0].Badge);
    }

    [Fact]
    public void A_stored_theme_the_catalog_lacks_opens_on_the_global_row_which_is_what_shows()
    {
        var snapshot = WindowPaletteProjection.WorkspaceThemes(SmallCatalog(), "servers", currentTheme: "Gone");

        Assert.Equal("theme:__default__", snapshot.InitialSelectionId);
        Assert.Equal("Current", snapshot.Items[0].Badge);
        Assert.DoesNotContain(snapshot.Items, item => item.Id == "theme:Gone");
    }

    [Fact]
    public void Other_palettes_have_no_initial_selection()
    {
        var snapshot = WindowPaletteProjection.Create(
            WindowPaletteMode.Commands, TestStores.Scratch(), Keymap.Empty, StateProvider());

        Assert.Null(snapshot.InitialSelectionId);
    }

    private static Guid ParseSessionId(PaletteItemViewModel item) =>
        WindowPaletteProjection.ParseSessionId(item.Id)!.Value;

    private static Func<BuiltinAction, WindowCommandState> StateProvider(
        IReadOnlyCollection<BuiltinAction>? enabled = null)
    {
        var enabledSet = enabled?.ToHashSet() ?? [];
        return action => new WindowCommandState(
            Implemented: enabledSet.Contains(action),
            CanExecute: enabledSet.Contains(action));
    }
}

public sealed class WindowPaletteAcceptanceTests
{
    [Theory]
    [InlineData(WindowPaletteMode.Sessions, true, false)]
    [InlineData(WindowPaletteMode.Sessions, false, true)]
    [InlineData(WindowPaletteMode.Attention, true, false)]
    [InlineData(WindowPaletteMode.Attention, false, true)]
    public async Task Session_modes_restore_focus_only_when_selection_did_not_change(
        WindowPaletteMode mode,
        bool selectionChanged,
        bool expectedRestoreTerminalFocus)
    {
        var sessionId = Guid.NewGuid();
        using var cancellationSource = new CancellationTokenSource();
        var calls = 0;
        var result = await WindowPaletteAcceptance.AcceptAsync(
            mode,
            Item(sessionId.ToString("D")),
            (actualSessionId, cancellationToken) =>
            {
                calls++;
                Assert.Equal(sessionId, actualSessionId);
                Assert.Equal(cancellationSource.Token, cancellationToken);
                return Task.FromResult(selectionChanged);
            },
            UnexpectedActionAsync,
            UnexpectedCustomCommandAsync,
            UnexpectedThemeAsync,
            cancellationSource.Token);

        Assert.Equal(1, calls);
        Assert.Equal(expectedRestoreTerminalFocus, result.RestoreTerminalFocus);
    }

    [Theory]
    [InlineData(BuiltinAction.ToggleSidebar, true, true)]
    [InlineData(BuiltinAction.RenameSession, true, false)]
    [InlineData(BuiltinAction.RenameWorkspace, true, false)]
    [InlineData(BuiltinAction.RenameSession, false, true)]
    public async Task Commands_restore_focus_when_execution_failed_or_action_requires_it(
        BuiltinAction action,
        bool executed,
        bool expectedRestoreTerminalFocus)
    {
        using var cancellationSource = new CancellationTokenSource();
        var calls = 0;
        var result = await WindowPaletteAcceptance.AcceptAsync(
            WindowPaletteMode.Commands,
            Item(action.WireName()),
            UnexpectedSessionAsync,
            (actualAction, cancellationToken) =>
            {
                calls++;
                Assert.Equal(action, actualAction);
                Assert.Equal(cancellationSource.Token, cancellationToken);
                return Task.FromResult(executed);
            },
            UnexpectedCustomCommandAsync,
            UnexpectedThemeAsync,
            cancellationSource.Token);

        Assert.Equal(1, calls);
        Assert.Equal(expectedRestoreTerminalFocus, result.RestoreTerminalFocus);
    }

    [Fact]
    public async Task Custom_commands_execute_the_parsed_id_and_restore_focus()
    {
        var commandId = Guid.NewGuid();
        using var cancellationSource = new CancellationTokenSource();
        var calls = 0;
        var result = await WindowPaletteAcceptance.AcceptAsync(
            WindowPaletteMode.CustomCommands,
            Item($"custom-{commandId:D}"),
            UnexpectedSessionAsync,
            UnexpectedActionAsync,
            (actualCommandId, cancellationToken) =>
            {
                calls++;
                Assert.Equal(commandId, actualCommandId);
                Assert.Equal(cancellationSource.Token, cancellationToken);
                return Task.CompletedTask;
            },
            UnexpectedThemeAsync,
            cancellationSource.Token);

        Assert.Equal(1, calls);
        Assert.True(result.RestoreTerminalFocus);
    }

    [Theory]
    [InlineData("theme:__default__", null)]
    [InlineData("theme:Dracula", "Dracula")]
    public async Task Themes_set_the_parsed_name_and_restore_focus(string itemId, string? expectedName)
    {
        using var cancellationSource = new CancellationTokenSource();
        var calls = 0;
        var result = await WindowPaletteAcceptance.AcceptAsync(
            WindowPaletteMode.Themes,
            Item(itemId),
            UnexpectedSessionAsync,
            UnexpectedActionAsync,
            UnexpectedCustomCommandAsync,
            (actualName, cancellationToken) =>
            {
                calls++;
                Assert.Equal(expectedName, actualName);
                Assert.Equal(cancellationSource.Token, cancellationToken);
                return Task.CompletedTask;
            },
            cancellationSource.Token);

        Assert.Equal(1, calls);
        Assert.True(result.RestoreTerminalFocus);
    }

    [Theory]
    [InlineData(InstallerCommands.InstallCommandLineToolId)]
    [InlineData(InstallerCommands.InstallAgentSkillId)]
    [InlineData(InstallerCommands.InstallAgentStatusHooksId)]
    public async Task Installer_items_run_the_installer_handler_and_restore_focus(string itemId)
    {
        using var cancellationSource = new CancellationTokenSource();
        var calls = 0;
        var result = await WindowPaletteAcceptance.AcceptAsync(
            WindowPaletteMode.Commands,
            Item(itemId),
            UnexpectedSessionAsync,
            UnexpectedActionAsync,
            UnexpectedCustomCommandAsync,
            UnexpectedThemeAsync,
            cancellationSource.Token,
            executeInstallerCommandAsync: (actualId, cancellationToken) =>
            {
                calls++;
                Assert.Equal(itemId, actualId);
                Assert.Equal(cancellationSource.Token, cancellationToken);
                return Task.CompletedTask;
            });

        Assert.Equal(1, calls);
        Assert.True(result.RestoreTerminalFocus);
    }

    [Theory]
    [InlineData("app:edit-keymap", PaletteAppCommand.EditKeymap)]
    [InlineData("app:reload-keymap", PaletteAppCommand.ReloadKeymap)]
    [InlineData("app:reload-config", PaletteAppCommand.ReloadConfig)]
    [InlineData("app:clear-flagged", PaletteAppCommand.ClearFlagged)]
    [InlineData("app:clear-focus", PaletteAppCommand.ClearFocus)]
    [InlineData("app:expand-workspaces", PaletteAppCommand.ExpandWorkspaces)]
    [InlineData("app:collapse-workspaces", PaletteAppCommand.CollapseWorkspaces)]
    public async Task App_rows_run_the_app_command_handler_and_restore_focus(
        string itemId,
        PaletteAppCommand expected)
    {
        using var cancellationSource = new CancellationTokenSource();
        var calls = 0;
        var result = await WindowPaletteAcceptance.AcceptAsync(
            WindowPaletteMode.Commands,
            Item(itemId),
            UnexpectedSessionAsync,
            UnexpectedActionAsync,
            UnexpectedCustomCommandAsync,
            UnexpectedThemeAsync,
            cancellationSource.Token,
            executeInstallerCommandAsync: (_, _) =>
                throw new InvalidOperationException("The installer handler should not be called."),
            executeAppCommandAsync: (actual, cancellationToken) =>
            {
                calls++;
                Assert.Equal(expected, actual);
                Assert.Equal(cancellationSource.Token, cancellationToken);
                return Task.CompletedTask;
            });

        Assert.Equal(1, calls);
        Assert.True(result.RestoreTerminalFocus);
    }

    [Theory]
    [InlineData(WindowPaletteMode.Sessions, "app:clear-flagged")]
    [InlineData(WindowPaletteMode.CustomCommands, "app:edit-keymap")]
    [InlineData(WindowPaletteMode.Themes, "app:reload-config")]
    [InlineData(WindowPaletteMode.Commands, "app:edit-ghostty-config")]
    public async Task App_ids_outside_the_commands_mode_or_unknown_do_not_run_the_app_handler(
        WindowPaletteMode mode,
        string itemId)
    {
        var calls = 0;
        var result = await WindowPaletteAcceptance.AcceptAsync(
            mode,
            Item(itemId),
            (_, _) =>
            {
                calls++;
                return Task.FromResult(true);
            },
            (_, _) =>
            {
                calls++;
                return Task.FromResult(true);
            },
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            },
            CancellationToken.None,
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            });

        Assert.Equal(0, calls);
        Assert.True(result.RestoreTerminalFocus);
    }

    [Theory]
    [InlineData(WindowPaletteMode.Sessions, "not-a-session")]
    [InlineData(WindowPaletteMode.Attention, "not-a-session")]
    [InlineData(WindowPaletteMode.Commands, "not_an_action")]
    [InlineData(WindowPaletteMode.CustomCommands, "not-a-custom-command")]
    [InlineData(WindowPaletteMode.Themes, "not-a-theme")]
    [InlineData((WindowPaletteMode)999, "toggle_sidebar")]
    [InlineData(WindowPaletteMode.Sessions, InstallerCommands.InstallAgentSkillId)]
    [InlineData(WindowPaletteMode.CustomCommands, InstallerCommands.InstallCommandLineToolId)]
    public async Task Malformed_ids_and_unknown_modes_do_not_invoke_handlers_and_restore_focus(
        WindowPaletteMode mode,
        string itemId)
    {
        var calls = 0;
        var result = await WindowPaletteAcceptance.AcceptAsync(
            mode,
            Item(itemId),
            (_, _) =>
            {
                calls++;
                return Task.FromResult(true);
            },
            (_, _) =>
            {
                calls++;
                return Task.FromResult(true);
            },
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            },
            CancellationToken.None,
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            });

        Assert.Equal(0, calls);
        Assert.True(result.RestoreTerminalFocus);
    }

    [Fact]
    public async Task Handler_cancellation_is_propagated()
    {
        var sessionId = Guid.NewGuid();
        using var cancellationSource = new CancellationTokenSource();
        var calls = 0;
        CancellationToken observedToken = default;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await WindowPaletteAcceptance.AcceptAsync(
                WindowPaletteMode.Sessions,
                Item(sessionId.ToString("D")),
                (_, cancellationToken) =>
                {
                    calls++;
                    observedToken = cancellationToken;
                    cancellationSource.Cancel();
                    return Task.FromCanceled<bool>(cancellationToken);
                },
                UnexpectedActionAsync,
                UnexpectedCustomCommandAsync,
                UnexpectedThemeAsync,
                cancellationSource.Token));

        Assert.Equal(1, calls);
        Assert.Equal(cancellationSource.Token, observedToken);
    }

    private static PaletteItemViewModel Item(string id) => new(id, "Item");

    private static Task<bool> UnexpectedSessionAsync(Guid _, CancellationToken __) =>
        throw new InvalidOperationException("The session handler should not be called.");

    private static Task<bool> UnexpectedActionAsync(BuiltinAction _, CancellationToken __) =>
        throw new InvalidOperationException("The action handler should not be called.");

    private static Task UnexpectedCustomCommandAsync(Guid _, CancellationToken __) =>
        throw new InvalidOperationException("The custom-command handler should not be called.");

    private static Task UnexpectedThemeAsync(string? _, CancellationToken __) =>
        throw new InvalidOperationException("The theme handler should not be called.");
}

public sealed class WindowPaletteActionsTests
{
    [Fact]
    public void SelectSession_resolves_current_target_notes_activity_and_selects_across_focus()
    {
        var store = TestStores.Scratch();
        var firstWorkspace = store.AddWorkspace("First");
        var secondWorkspace = store.AddWorkspace("Second");
        var first = store.AddSession(firstWorkspace.Id, @"C:\first")!;
        var second = store.AddSession(secondWorkspace.Id, @"C:\second", select: false)!;
        store.FocusedWorkspaceId = firstWorkspace.Id;

        var selected = WindowPaletteActions.SelectSession(store, second.Id);

        Assert.True(selected);
        Assert.Equal(second.Id, store.SelectedSessionId);
        Assert.Null(store.FocusedWorkspaceId);
    }

    [Fact]
    public void SelectSession_noops_when_palette_target_was_deleted_while_open()
    {
        var store = TestStores.Scratch();
        var workspace = store.AddWorkspace("Work");
        var current = store.AddSession(workspace.Id, @"C:\current")!;
        var staleId = Guid.NewGuid();

        var selected = WindowPaletteActions.SelectSession(store, staleId);

        Assert.False(selected);
        Assert.Equal(current.Id, store.SelectedSessionId);
    }

    [Fact]
    public void SelectSession_reports_no_selection_change_for_the_already_selected_target()
    {
        var store = TestStores.Scratch();
        var workspace = store.AddWorkspace("Work");
        var current = store.AddSession(workspace.Id, @"C:\current")!;

        var changed = WindowPaletteActions.SelectSession(store, current.Id);

        Assert.False(changed);
        Assert.Equal(current.Id, store.SelectedSessionId);
    }

    [Theory]
    [InlineData(BuiltinAction.ToggleSidebar, true)]
    [InlineData(BuiltinAction.ToggleFlag, true)]
    [InlineData(BuiltinAction.RenameSession, false)]
    [InlineData(BuiltinAction.OpenDirectory, true)]
    [InlineData(BuiltinAction.ToggleSearch, false)]
    [InlineData(BuiltinAction.ShowAttention, false)]
    [InlineData(BuiltinAction.SelectTheme, false)]
    [InlineData(BuiltinAction.FocusLeftPane, false)]
    [InlineData(BuiltinAction.FocusRightPane, false)]
    public void Command_focus_policy_preserves_follow_on_UI_owners(
        BuiltinAction action,
        bool expected)
    {
        Assert.Equal(expected, WindowPaletteActions.ShouldRestoreTerminalFocus(action));
    }

    [Fact]
    public void Custom_command_context_uses_exact_terminal_panes_and_falls_back_from_quick_or_overlay()
    {
        var selection = new LiveSelectionSnapshot(
            Guid.NewGuid(),
            "session",
            MainPaneId: "main",
            SplitPaneId: "split",
            IsSplit: true,
            SplitFocused: true,
            ScratchPaneId: "scratch",
            ScratchActive: true,
            OverlayPaneId: "overlay",
            OverlayActive: true,
            QuickPaneId: "quick",
            QuickVisible: true);

        Assert.Equal("split", WindowPaletteActions.CustomCommandPaneId(
            selection, CustomCommandSource.Palette, null));
        Assert.Equal("main", WindowPaletteActions.CustomCommandPaneId(
            selection with { SplitPaneId = null }, CustomCommandSource.Palette, null));
        Assert.Equal("main", WindowPaletteActions.CustomCommandPaneId(
            selection, CustomCommandSource.Hotkey, "main"));
        Assert.Equal("split", WindowPaletteActions.CustomCommandPaneId(
            selection, CustomCommandSource.Hotkey, "split"));
        Assert.Equal("scratch", WindowPaletteActions.CustomCommandPaneId(
            selection, CustomCommandSource.Hotkey, "scratch"));
        Assert.Equal("split", WindowPaletteActions.CustomCommandPaneId(
            selection, CustomCommandSource.Hotkey, "quick"));
        Assert.Equal("split", WindowPaletteActions.CustomCommandPaneId(
            selection, CustomCommandSource.Hotkey, "overlay"));
    }

    [Fact]
    public async Task Command_acceptance_rechecks_router_CanExecute_after_palette_open()
    {
        var enabled = true;
        var calls = 0;
        var handlers = WindowCommandRouter.ActionImplementations
            .Where(pair => pair.Value)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Key == BuiltinAction.ToggleSplit
                    ? new WindowCommandHandler(() => enabled, () =>
                    {
                        calls++;
                        return Task.CompletedTask;
                    })
                    : new WindowCommandHandler(() => true, () => Task.CompletedTask));
        var router = new WindowCommandRouter(handlers);
        Assert.True(router.GetState(BuiltinAction.ToggleSplit).CanExecute);

        enabled = false;
        var executed = await router.ExecuteAsync(BuiltinAction.ToggleSplit);

        Assert.False(executed);
        Assert.Equal(0, calls);
    }
}

public sealed class MainWindowPaletteWiringTests
{
    [Fact]
    public void MainWindow_wires_all_palette_modes_acceptance_cancellation_and_fresh_projection()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("PaletteOverlay.ItemAccepted +=", source, StringComparison.Ordinal);
        Assert.Contains("PaletteOverlay.Cancelled +=", source, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteProjection.Create", source, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteAcceptance.AcceptAsync", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.PreserveInputOrderForEmptyQuery", source, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteActions.SelectSession", source, StringComparison.Ordinal);
        Assert.Contains("FocusActiveTerminal", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.SessionPalette]", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.CommandPalette]", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.CustomCommandPalette]", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.ShowAttention]", source, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteMode.Sessions", source, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteMode.Commands", source, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteMode.CustomCommands", source, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteMode.Attention", source, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(
            source,
            "private async Task OpenPaletteAsync").Cast<object>());
    }

    [Fact]
    public void Palette_open_and_close_balance_auto_follow_suppression()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("store.SuppressAutoFollow()", source, StringComparison.Ordinal);
        Assert.Contains("store.ResumeAutoFollow()", source, StringComparison.Ordinal);
        Assert.Contains("FinishPaletteCloseAsync(restoreFocus: false)", source, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteAcceptance.AcceptAsync", source, StringComparison.Ordinal);
        Assert.Contains("_paletteTransitions.RunAsync", source, StringComparison.Ordinal);
        Assert.Contains("() => !host.ZoomActive", source, StringComparison.Ordinal);
        Assert.Contains("host.ZoomActiveChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("RefreshInterceptChords", source, StringComparison.Ordinal);
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

public sealed class PaletteTransitionCoordinatorTests
{
    [Fact]
    public async Task RunAsync_serializes_parallel_palette_transitions()
    {
        var coordinator = new PaletteTransitionCoordinator();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;
        var active = 0;
        var maximumActive = 0;

        var first = coordinator.RunAsync(async () =>
        {
            maximumActive = Math.Max(maximumActive, Interlocked.Increment(ref active));
            firstEntered.TrySetResult();
            await releaseFirst.Task;
            Interlocked.Decrement(ref active);
        });
        await firstEntered.Task;

        var second = coordinator.RunAsync(() =>
        {
            secondEntered = true;
            maximumActive = Math.Max(maximumActive, Interlocked.Increment(ref active));
            Interlocked.Decrement(ref active);
            return Task.CompletedTask;
        });

        Assert.False(secondEntered);
        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.True(secondEntered);
        Assert.Equal(1, maximumActive);
    }

    [Fact]
    public async Task RunAsync_does_not_let_a_child_task_inherit_transition_ownership()
    {
        var coordinator = new PaletteTransitionCoordinator();
        var childAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseParent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var childEntered = false;
        Task? child = null;

        var parent = coordinator.RunAsync(async () =>
        {
            child = Task.Run(async () =>
            {
                childAttempted.TrySetResult();
                await coordinator.RunAsync(() =>
                {
                    childEntered = true;
                    return Task.CompletedTask;
                });
            });
            await releaseParent.Task;
        });

        await childAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(childEntered);

        releaseParent.TrySetResult();
        await parent;
        await child!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(childEntered);
    }
}

public sealed class PaletteAutoFollowLeaseTests
{
    [Fact]
    public async Task Acquire_is_idempotent_and_successful_release_clears_ownership()
    {
        var lease = new PaletteAutoFollowLease();
        var suppressionCount = 0;

        await lease.AcquireAsync(() =>
        {
            suppressionCount++;
            return Task.CompletedTask;
        });
        await lease.AcquireAsync(() =>
        {
            suppressionCount++;
            return Task.CompletedTask;
        });
        await lease.ReleaseAsync(() =>
        {
            suppressionCount--;
            return Task.CompletedTask;
        });

        Assert.False(lease.IsHeld);
        Assert.Equal(0, suppressionCount);
    }

    [Fact]
    public async Task Failed_release_keeps_ownership_so_a_later_retry_can_balance_the_count()
    {
        var lease = new PaletteAutoFollowLease();
        var suppressionCount = 0;
        await lease.AcquireAsync(() =>
        {
            suppressionCount++;
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ReleaseAsync(() =>
            Task.FromException(new InvalidOperationException("resume failed"))));

        Assert.True(lease.IsHeld);
        Assert.Equal(1, suppressionCount);

        await lease.ReleaseAsync(() =>
        {
            suppressionCount--;
            return Task.CompletedTask;
        });

        Assert.False(lease.IsHeld);
        Assert.Equal(0, suppressionCount);
    }
}

public sealed class PaletteStatusColorTests
{
    [Theory]
    [InlineData("#1122AA", 0xFF1122AAu)]
    [InlineData("#abcdef", 0xFFABCDEFu)]
    public void Resolve_returns_per_item_argb(string value, uint expected)
    {
        Assert.Equal(expected, PaletteStatusColor.Resolve(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#GGGGGG")]
    public void Resolve_returns_null_for_default_or_invalid_color(string? value)
    {
        Assert.Null(PaletteStatusColor.Resolve(value));
    }
}