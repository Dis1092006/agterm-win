using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// SidebarController — the UI-free control plane of the WinUI sidebar: it snapshots the row
/// projection UNDER the host gate, marshals it to the UI via the injected dispatcher, and turns
/// every sidebar gesture into the same AppStore mutations the control socket performs (the third
/// caller, upstream-style). No WebView2 involved — driven against a HeadlessHost.
/// </summary>
public sealed class SidebarControllerTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-sbc-").FullName;
    private readonly HeadlessHost _host;
    private readonly LiveStoreCoordinator _coordinator;
    private readonly SidebarController _controller;
    private readonly List<SidebarSnapshot> _snapshots = new();

    public SidebarControllerTests()
    {
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-sbc-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
        });
        // inline scheduler — coordinator runs store work synchronously on the calling thread
        _coordinator = new LiveStoreCoordinator(
            _host,
            isUiThread: () => false,
            scheduler: (work, ct) => { ct.ThrowIfCancellationRequested(); work(); return Task.CompletedTask; });
        // the "UI dispatcher" runs inline — tests observe snapshots synchronously
        _controller = new SidebarController(_coordinator, runOnUi: work => work());
        _controller.SnapshotChanged += _snapshots.Add;
        _controller.Start();
    }

    public void Dispose()
    {
        _controller.Dispose();
        _coordinator.Dispose();
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private SidebarSnapshot Latest => _snapshots[^1];

    private void Mutate(Action<AppStore> action) =>
        _coordinator.MutateAsync(action).GetAwaiter().GetResult();

    [Fact]
    public void Manual_selection_notes_user_activity()
    {
        Assert.Null(_host.Store.ControlTree().IdleMs);
        _controller.Select(Latest.SelectedSessionId!.Value);
        Assert.NotNull(_host.Store.ControlTree().IdleMs);
    }

    [Fact]
    public void Focus_set_gestures_build_a_set_and_switch_the_filter_without_losing_it()
    {
        Guid w1 = Latest.Workspaces[0].Id, w2 = default, w3 = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id;
            w3 = store.AddWorkspace("w3", revealNewWorkspace: false).Id;
        });

        _controller.ToggleFocusMembership(w2);
        _controller.ToggleFocusMembership(w3);
        Assert.Equal(new[] { w2, w3 }.Order(), Latest.FocusMemberIds.Order());
        Assert.False(Latest.FocusEnabled);
        Assert.Null(Latest.FocusedWorkspaceId);
        Assert.Equal(3, Latest.Rows.Count(r => r.Kind == SidebarRowKind.Workspace));

        _controller.ToggleWorkspaceFilter();
        Assert.True(Latest.FocusEnabled);
        Assert.Equal(new[] { w2, w3 }.Order(),
            Latest.Rows.Where(r => r.Kind == SidebarRowKind.Workspace).Select(r => r.Id).Order());
        Assert.DoesNotContain(Latest.Rows, r => r.Id == w1);

        _controller.ToggleWorkspaceFilter();
        Assert.False(Latest.FocusEnabled);
        Assert.Equal(new[] { w2, w3 }.Order(), Latest.FocusMemberIds.Order());

        _controller.ToggleFocusMembership(w2);
        _controller.ToggleFocusMembership(w3);
        _controller.ToggleWorkspaceFilter();
        Assert.False(Latest.FocusEnabled);
        Assert.Empty(Latest.FocusMemberIds);
    }

    [Fact]
    public void Add_active_workspace_to_focus_marks_it_without_turning_the_filter_on()
    {
        var active = Latest.Workspaces[0].Id;

        _controller.AddActiveWorkspaceToFocus();

        Assert.Equal([active], Latest.FocusMemberIds);
        Assert.False(Latest.FocusEnabled);
    }

    [Fact]
    public void Start_broadcasts_the_bootstrap_snapshot()
    {
        Assert.NotEmpty(_snapshots);
        var row = Assert.Single(Latest.Rows, r => r.Kind == SidebarRowKind.Session);
        Assert.Equal(Latest.SelectedSessionId, row.Id);
        Assert.True(Latest.SidebarVisible);
        Assert.Equal(SidebarMode.Tree, Latest.Mode);
        Assert.Single(Latest.Workspaces);
        Assert.False(Latest.CanRemoveWorkspace);
    }

    [Fact]
    public void A_flagged_layout_change_rebroadcasts_the_layout_and_its_rows()
    {
        Guid w2 = default, session = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id;
            session = store.AddSession(w2, _stateDir, select: false)!.Id;
            store.RenameSession(session, "api");
            store.SetFlag(true, [session]);
            store.SetSidebarMode(SidebarMode.Flagged);
        });
        Assert.Equal(FlaggedViewLayout.Flat, Latest.FlaggedLayout);
        Assert.False(Latest.RendersWorkspaceRows);

        Mutate(store => store.FlaggedLayout = FlaggedViewLayout.Tree);

        Assert.Equal(FlaggedViewLayout.Tree, Latest.FlaggedLayout);
        Assert.True(Latest.RendersWorkspaceRows);
        Assert.Equal(new[] { w2, session }, Latest.Rows.Select(r => r.Id));
        Assert.Equal("api", Latest.Rows[1].Label);
    }

    [Fact]
    public void The_ordinary_tree_renders_workspace_rows_whatever_the_flagged_layout()
    {
        Mutate(store => store.FlaggedLayout = FlaggedViewLayout.Plain);

        Assert.Equal(SidebarMode.Tree, Latest.Mode);
        Assert.True(Latest.RendersWorkspaceRows);
    }

    [Fact]
    public void Store_mutations_rebroadcast_through_the_injected_dispatcher()
    {
        var before = _snapshots.Count;
        Mutate(store => store.AddWorkspace("w2"));
        Assert.True(_snapshots.Count > before);
        Assert.Equal(2, Latest.Workspaces.Count);
        Assert.True(Latest.CanRemoveWorkspace);
    }

    [Fact]
    public void A_requested_snapshot_carries_the_generation_the_request_returned()
    {
        var bootstrap = Latest.Generation;

        var requested = _controller.RequestSnapshot();

        Assert.True(requested > bootstrap);
        Assert.Equal(requested, Latest.Generation);
        Mutate(store => store.AddWorkspace("w2"));
        Assert.True(Latest.Generation > requested);
    }

    [Fact]
    public void Tint_menu_gestures_tint_the_sessions_and_the_workspace_row_alone()
    {
        Guid w2 = default, first = default, second = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2").Id;
            first = store.AddSession(w2, _stateDir, select: false)!.Id;
            second = store.AddSession(w2, _stateDir, select: false)!.Id;
            store.SetSessionTint(first, new SidebarTint(TintColor.Red, TintParts.Name));
        });
        SidebarTint? TintOf(Guid id) => Latest.Rows.Single(r => r.Id == id).Tint;

        _controller.SetTint([first, second], SidebarTintChoice.Of(TintColor.Blue));
        Assert.Equal(new SidebarTint(TintColor.Blue, TintParts.Name), TintOf(first));
        Assert.Equal(new SidebarTint(TintColor.Blue), TintOf(second));

        _controller.SetTint([first, second], SidebarTintChoice.Show(TintParts.Flag, true));
        Assert.Equal(new SidebarTint(TintColor.Blue, TintParts.Icon | TintParts.Flag), TintOf(second));
        _controller.SetTint([first, second], SidebarTintChoice.Show(TintParts.Name, false));
        Assert.Equal(new SidebarTint(TintColor.Blue, TintParts.Flag), TintOf(first));

        _controller.SetWorkspaceTint(w2, SidebarTintChoice.Of(TintColor.Purple));
        _controller.SetWorkspaceTint(w2, SidebarTintChoice.Show(TintParts.Name, true));
        Assert.Equal(new SidebarTint(TintColor.Purple, TintParts.Icon | TintParts.Name), TintOf(w2));
        Assert.Equal(new SidebarTint(TintColor.Blue, TintParts.Icon | TintParts.Flag), TintOf(second));
        _controller.SetWorkspaceTint(w2, SidebarTintChoice.Show(TintParts.Icon, false));
        _controller.SetWorkspaceTint(w2, SidebarTintChoice.Show(TintParts.Name, false));
        Assert.Null(TintOf(w2));

        _controller.SetTint([first], SidebarTintChoice.Of(null));
        _controller.SetWorkspaceTint(w2, SidebarTintChoice.Of(TintColor.Gray));
        _controller.SetWorkspaceTint(w2, SidebarTintChoice.Of(null));
        Assert.Null(TintOf(first));
        Assert.Null(TintOf(w2));
    }

    [Fact]
    public void Flag_all_on_a_mixed_workspace_flags_every_session_then_unflags_them()
    {
        Guid w2 = default, first = default, second = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id;
            first = store.AddSession(w2, _stateDir, select: false)!.Id;
            second = store.AddSession(w2, _stateDir, select: false)!.Id;
            store.SetFlag(true, [first]);
        });
        SidebarWorkspaceInfo Info() => Latest.Workspaces.Single(w => w.Id == w2);
        Assert.Equal("Flag All Sessions", Info().FlagAllTitle);
        Assert.True(Info().CanFlagAll);

        _controller.ToggleWorkspaceFlag(w2);

        Assert.True(Latest.Rows.Single(r => r.Id == first).Flagged);
        Assert.True(Latest.Rows.Single(r => r.Id == second).Flagged);
        Assert.Equal("Unflag All Sessions", Info().FlagAllTitle);

        _controller.ToggleWorkspaceFlag(w2);

        Assert.Equal(0, Latest.FlaggedCount);
        Assert.Equal("Flag All Sessions", Info().FlagAllTitle);
    }

    [Fact]
    public void Flag_all_is_disabled_on_an_empty_workspace()
    {
        Guid w2 = default;
        Mutate(store => w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id);

        var info = Latest.Workspaces.Single(w => w.Id == w2);
        Assert.False(info.CanFlagAll);
        Assert.Equal("Flag All Sessions", info.FlagAllTitle);
    }

    [Fact]
    public void Flag_all_in_the_flagged_tree_flags_the_sessions_it_hides()
    {
        Guid w2 = default, shown = default, hidden = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id;
            shown = store.AddSession(w2, _stateDir, select: false)!.Id;
            hidden = store.AddSession(w2, _stateDir, select: false)!.Id;
            store.SetFlag(true, [shown]);
            store.FlaggedLayout = FlaggedViewLayout.Tree;
            store.SetSidebarMode(SidebarMode.Flagged);
        });
        Assert.DoesNotContain(Latest.Rows, r => r.Id == hidden);
        Assert.Equal("Flag All Sessions", Latest.Workspaces.Single(w => w.Id == w2).FlagAllTitle);

        _controller.ToggleWorkspaceFlag(w2);

        Assert.True(Latest.Rows.Single(r => r.Id == hidden).Flagged);
        Assert.Equal(2, Latest.FlaggedCount);
    }

    // upstream a2552826: the flagged view with nothing flagged says so and offers the editor
    [Fact]
    public void The_flagged_view_with_nothing_flagged_shows_the_empty_state()
    {
        Guid session = default;
        Mutate(store => session = store.AddSession(store.Workspaces[0].Id, _stateDir, select: false)!.Id);
        Assert.False(Latest.ShowsFlaggedEmptyState);

        _controller.ToggleFlaggedView();
        Assert.Equal(SidebarMode.Flagged, Latest.Mode);
        Assert.True(Latest.ShowsFlaggedEmptyState);

        _controller.SetFlag(new[] { session }, on: true);
        Assert.False(Latest.ShowsFlaggedEmptyState);
    }

    // the open Edit Flagged Sessions palette redraws its marks from this set
    [Fact]
    public void The_snapshot_carries_every_flagged_session_the_rows_may_hide()
    {
        Guid w2 = default, shown = default, hidden = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id;
            shown = store.AddSession(w2, _stateDir, select: false)!.Id;
            hidden = store.AddSession(w2, _stateDir, select: false)!.Id;
            store.SetFlag(true, [shown, hidden]);
            store.SetFocusMembership(store.Workspaces[0].Id, true);
            store.SetFocusEnabled(true);
        });
        Assert.Empty(Latest.Rows.Where(r => r.Id == hidden));

        Assert.Equal(new HashSet<Guid> { shown, hidden }, Latest.FlaggedSessionIds);

        _controller.SetFlag(new[] { shown }, on: false);
        Assert.Equal(new HashSet<Guid> { hidden }, Latest.FlaggedSessionIds);
    }

    [Fact]
    public void Controller_gestures_mutate_the_store_like_the_socket_would()
    {
        Guid w2 = default, background = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id;
            background = store.AddSession(w2, _stateDir, select: false)!.Id;
        });

        _controller.Select(background);
        Assert.Equal(background, Latest.SelectedSessionId);

        _controller.RenameSession(background, "api");
        Assert.Equal("api", Latest.Rows.Single(r => r.Id == background).Label);

        _controller.SetFlag(new[] { background }, on: true);
        Assert.True(Latest.Rows.Single(r => r.Id == background).Flagged);
        Assert.Equal(1, Latest.FlaggedCount);

        _controller.ToggleFlaggedView();
        Assert.Equal(SidebarMode.Flagged, Latest.Mode);
        Assert.Equal("api : w2", Assert.Single(Latest.Rows).Label);
        _controller.ToggleFlaggedView();

        _controller.ToggleWorkspaceCollapse(w2);
        Assert.True(Latest.Rows.Single(r => r.Id == w2).Collapsed);
        Assert.DoesNotContain(Latest.Rows, r => r.Id == background);
        _controller.ToggleWorkspaceCollapse(w2); // expand it back for the row asserts below
        Assert.False(Latest.Rows.Single(r => r.Id == w2).Collapsed);

        _controller.FocusWorkspaceToggle(w2);
        Assert.Equal(w2, Latest.FocusedWorkspaceId);
        Assert.DoesNotContain(Latest.Rows, r => r.Kind == SidebarRowKind.Workspace && r.Id != w2);
        _controller.ClearFocus();
        Assert.Null(Latest.FocusedWorkspaceId);

        _controller.ClearStatuses(new[] { background });
        Assert.Equal(AgentStatus.Idle, Latest.Rows.Single(r => r.Id == background).Indicator.Status);
    }

    [Fact]
    public void The_row_toggle_folds_a_workspace_the_reveal_opened()
    {
        Guid w2 = default, background = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id;
            background = store.AddSession(w2, _stateDir, select: false)!.Id;
            store.SetWorkspaceExpanded(w2, false);
        });
        var generation = Latest.RevealGeneration;

        _controller.Select(background);
        Assert.False(Latest.Rows.Single(r => r.Id == w2).Collapsed);
        Assert.Contains(Latest.Rows, r => r.Id == background);
        Assert.True(Latest.RevealGeneration > generation);

        _controller.ToggleWorkspaceCollapse(w2);
        Assert.True(Latest.Rows.Single(r => r.Id == w2).Collapsed);
        Assert.DoesNotContain(Latest.Rows, r => r.Id == background);

        _controller.ToggleWorkspaceCollapse(w2);
        Assert.False(Latest.Rows.Single(r => r.Id == w2).Collapsed);
        Assert.True(_host.Store.Workspaces.Single(w => w.Id == w2).IsExpanded);
    }

    [Fact]
    public void A_drag_opens_a_collapsed_workspace_and_its_end_folds_it_back()
    {
        Guid w2 = default, background = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id;
            background = store.AddSession(w2, _stateDir, select: false)!.Id;
            store.SetWorkspaceExpanded(w2, false);
        });

        _controller.SpringOpenWorkspace(w2);
        Assert.False(Latest.Rows.Single(r => r.Id == w2).Collapsed);
        Assert.Contains(Latest.Rows, r => r.Id == background);

        _controller.EndSpringLoad(keepOpen: false);
        Assert.True(Latest.Rows.Single(r => r.Id == w2).Collapsed);
        Assert.False(_host.Store.Workspaces.Single(w => w.Id == w2).IsExpanded);
    }

    [Fact]
    public void Mark_for_attention_completes_idle_sessions_and_leaves_statused_ones()
    {
        var w1 = Latest.Workspaces.Single().Id;
        _controller.NewSession(w1);
        var ids = Latest.Rows.Where(r => r.Kind == SidebarRowKind.Session).Select(r => r.Id).ToList();
        Assert.True(ids.Count >= 2);
        var blocked = ids[0];
        var idle = ids[1];
        Mutate(store => store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), blocked));

        _controller.MarkForAttention(new[] { blocked, idle });

        Assert.Equal(AgentStatus.Blocked, Latest.Rows.Single(r => r.Id == blocked).Indicator.Status);
        Assert.Equal(AgentStatus.Completed, Latest.Rows.Single(r => r.Id == idle).Indicator.Status);
    }

    [Fact]
    public void New_duplicate_close_and_delete_follow_the_upstream_paths()
    {
        var w1 = Latest.Workspaces.Single().Id;
        var initial = Latest.SelectedSessionId!.Value;

        _controller.NewSession(w1);
        var created = Latest.SelectedSessionId!.Value;
        Assert.NotEqual(initial, created);

        _controller.DuplicateSession(created);
        var duplicated = Latest.SelectedSessionId!.Value;
        Assert.NotEqual(created, duplicated);
        // the duplicate sits directly after its source, in the same workspace
        var ids = Latest.Rows.Where(r => r.Kind == SidebarRowKind.Session).Select(r => r.Id).ToList();
        Assert.Equal(ids.IndexOf(created) + 1, ids.IndexOf(duplicated));

        _controller.CloseSessions(new[] { duplicated, created });
        Assert.DoesNotContain(Latest.Rows, r => r.Id == created || r.Id == duplicated);

        Guid w2 = default;
        Mutate(store => w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id);
        _controller.DeleteWorkspace(w2);
        Assert.DoesNotContain(Latest.Workspaces, w => w.Id == w2);
    }

    [Fact]
    public async Task ImportDirectories_adds_one_ordered_session_per_request()
    {
        var workspaceId = Latest.Workspaces.Single().Id;
        var first = Directory.CreateDirectory(Path.Combine(_stateDir, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(_stateDir, "second")).FullName;
        var requests = DirectoryImportPolicy.CreateRequests(workspaceId, new[] { first, second });

        _controller.ImportDirectories(requests);

        var importedCwds = await _coordinator.ReadAsync(store => store.Workspaces.Single().Sessions
            .Skip(1).Select(session => session.InitialCwd).ToArray());
        Assert.Equal(new[] { first, second }, importedCwds);
    }

    // upstream AppActions.resolvedNewSessionCwd: the workspace-row New Session, its "+" and the
    // new_session hotkey all open where newSessionDirectory says, read when the session is made
    [Fact]
    public async Task New_session_without_a_directory_honours_the_new_session_directory_setting()
    {
        var home = Directory.CreateDirectory(Path.Combine(_stateDir, "home")).FullName;
        var custom = Directory.CreateDirectory(Path.Combine(_stateDir, "custom")).FullName;
        var current = Directory.CreateDirectory(Path.Combine(_stateDir, "current")).FullName;
        var settings = new AppSettings();
        using var controller = new SidebarController(
            _coordinator, runOnUi: work => work(), homeDirectory: home, settings: () => settings);
        var workspace = Latest.Workspaces.Single().Id;

        controller.NewSession(workspace);
        settings = new AppSettings { NewSessionDirectory = "custom", NewSessionCustomDirectory = custom };
        controller.NewSession(workspace);
        settings = new AppSettings { NewSessionDirectory = "currentSession" };
        Mutate(store => store.ActiveSession!.CurrentCwd = current);
        controller.NewSessionInActiveWorkspace();

        var cwds = await _coordinator.ReadAsync(store => store.Workspaces.Single().Sessions
            .Skip(1).Select(session => session.InitialCwd).ToArray());
        Assert.Equal(new[] { home, custom, current }, cwds);
    }

    [Fact]
    public async Task An_explicit_directory_wins_over_the_new_session_directory_setting()
    {
        var custom = Directory.CreateDirectory(Path.Combine(_stateDir, "custom")).FullName;
        var picked = Directory.CreateDirectory(Path.Combine(_stateDir, "picked")).FullName;
        var settings = new AppSettings { NewSessionDirectory = "custom", NewSessionCustomDirectory = custom };
        using var controller = new SidebarController(
            _coordinator, runOnUi: work => work(), settings: () => settings);

        controller.NewSession(Latest.Workspaces.Single().Id, picked);
        controller.NewSessionInActiveWorkspace(picked);

        var cwds = await _coordinator.ReadAsync(store => store.Workspaces.Single().Sessions
            .Skip(1).Select(session => session.InitialCwd).ToArray());
        Assert.Equal(new[] { picked, picked }, cwds);
    }

    [Fact]
    public void Snapshot_carries_an_extended_sidebar_selection_so_the_list_keeps_every_row()
    {
        var first = Latest.SelectedSessionId!.Value;
        Guid second = default;
        Mutate(store => second = store.AddSession(store.Workspaces[0].Id, _stateDir, select: false)!.Id);

        _controller.Select(second, new[] { first, second });

        Assert.Equal(second, Latest.SelectedSessionId);
        Assert.Equal(new[] { first, second }, Latest.SidebarSelectionIds);
        Assert.Equal(new[] { first, second }, Latest.ListSelection());

        _controller.Select(first);

        Assert.Equal(new[] { first }, Latest.ListSelection());
    }

    [Fact]
    public void List_selection_falls_back_to_the_selected_session_when_the_sidebar_selection_is_empty()
    {
        var selected = Guid.NewGuid();
        var snapshot = Latest with { SelectedSessionId = selected, SidebarSelectionIds = Array.Empty<Guid>() };

        Assert.Equal(new[] { selected }, snapshot.ListSelection());
        Assert.Empty((snapshot with { SelectedSessionId = null }).ListSelection());
    }

    [Fact]
    public void Snapshot_carries_the_title_bar_state_of_the_same_capture()
    {
        var session = Latest.SelectedSessionId!.Value;

        Mutate(store => store.ToggleScratch(session));

        Assert.True(Latest.TitleBar.HasActiveSession);
        Assert.True(Latest.TitleBar.ScratchActive);
    }

    [Fact]
    public void Hotkey_variants_act_on_the_active_session_and_workspace()
    {
        var initial = Latest.SelectedSessionId!.Value;
        _controller.NewSessionInActiveWorkspace();
        var created = Latest.SelectedSessionId!.Value;
        Assert.NotEqual(initial, created);

        _controller.ToggleActiveFlag();
        Assert.True(Latest.Rows.Single(r => r.Id == created).Flagged);
        _controller.ToggleActiveFlag();
        Assert.False(Latest.Rows.Single(r => r.Id == created).Flagged);

        _controller.Navigate(SessionNavigation.First);
        Assert.Equal(initial, Latest.SelectedSessionId);
        _controller.Navigate(SessionNavigation.Last);
        Assert.Equal(created, Latest.SelectedSessionId);

        _controller.FocusActiveWorkspaceToggle();
        Assert.NotNull(Latest.FocusedWorkspaceId);
        _controller.FocusActiveWorkspaceToggle();
        Assert.Null(Latest.FocusedWorkspaceId);

        _controller.DuplicateActiveSession();
        var duplicated = Latest.SelectedSessionId!.Value;
        Assert.NotEqual(created, duplicated);
        _controller.CloseActiveSession();
        Assert.DoesNotContain(Latest.Rows, r => r.Id == duplicated);
    }

    [Fact]
    public async Task Width_and_visibility_persist_through_the_store()
    {
        _controller.SetSidebarWidth(300);
        Assert.Equal(300, Latest.SidebarWidth);
        _controller.SetSidebarVisible(false);
        Assert.False(Latest.SidebarVisible);
        // survives a restart from the same state dir
        _controller.Dispose();
        await _coordinator.MutateAsync(store => store.Save());
        using var second = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-sbc2-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
        });
        Assert.Equal(300, second.Store.SidebarWidth);
        Assert.False(second.Store.SidebarVisible);
    }

    [Fact]
    public void Drop_gestures_apply_the_sidebar_drop_math()
    {
        Guid w1 = default, a = default, b = default, c = default;
        Mutate(store =>
        {
            w1 = store.Workspaces[0].Id;
            a = store.Workspaces[0].Sessions[0].Id;
            b = store.AddSession(w1, _stateDir, select: false)!.Id;
            c = store.AddSession(w1, _stateDir, select: false)!.Id;
        });

        // drag a into the slot before c (slot 2) → lands between b and c
        _controller.DropSessions(new[] { a }, new SidebarSessionDrop.Gap(w1, 2));
        Assert.Equal(new[] { b, a, c },
            Latest.Rows.Where(r => r.Kind == SidebarRowKind.Session).Select(r => r.Id));

        // drop c ON b → lands right after b
        _controller.DropSessions(new[] { c }, new SidebarSessionDrop.OnSession(b));
        Assert.Equal(new[] { b, c, a },
            Latest.Rows.Where(r => r.Kind == SidebarRowKind.Session).Select(r => r.Id));

        Guid w2 = default;
        Mutate(store => w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id);
        var count = _snapshots.Count;
        _controller.DropWorkspace(w2, slot: 2); // already last → no-op, no reorder broadcastworthy
        Assert.Equal(new[] { w1, w2 }, Latest.Workspaces.Select(w => w.Id));
        _ = count;

        _controller.DropWorkspace(w2, slot: 0);
        Assert.Equal(new[] { w2, w1 }, Latest.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void A_flagged_list_drop_reorders_the_flagged_order()
    {
        Guid w1 = default, a = default, b = default, c = default;
        Mutate(store =>
        {
            w1 = store.Workspaces[0].Id;
            a = store.Workspaces[0].Sessions[0].Id;
            b = store.AddSession(w1, _stateDir, select: false)!.Id;
            c = store.AddSession(w1, _stateDir, select: false)!.Id;
            store.SetFlag(true, [a, b, c]);
            store.SetSidebarMode(SidebarMode.Flagged);
        });
        Assert.Equal(new[] { a, b, c }, Latest.DropModel.FlaggedOrder);

        _controller.DropSessions(new[] { c }, new SidebarSessionDrop.ListGap(0));
        Assert.Equal(new[] { c, a, b }, Latest.Rows.Select(r => r.Id));

        _controller.DropSessions(new[] { c }, new SidebarSessionDrop.OnSession(b));
        Assert.Equal(new[] { a, b, c }, Latest.Rows.Select(r => r.Id));
        Assert.Equal(new[] { a, b, c }, Latest.DropModel.FlaggedOrder);
    }

    [Fact]
    public void A_flagged_tree_drop_moves_the_session_past_its_visible_neighbour()
    {
        Guid w1 = default, a = default, h = default, b = default;
        Mutate(store =>
        {
            w1 = store.Workspaces[0].Id;
            a = store.Workspaces[0].Sessions[0].Id;
            h = store.AddSession(w1, _stateDir, select: false)!.Id;
            b = store.AddSession(w1, _stateDir, select: false)!.Id;
            store.SetFlag(true, [a, b]);
            store.SetSidebarMode(SidebarMode.Flagged);
            store.FlaggedLayout = FlaggedViewLayout.Tree;
        });

        // the gap between a and b only crosses the hidden h: nothing changes on screen, nothing moves
        _controller.DropSessions(new[] { a }, new SidebarSessionDrop.Gap(w1, 1));
        Assert.Equal(new[] { a, h, b }, Latest.DropModel.Workspaces[0].Sessions);

        _controller.DropSessions(new[] { b }, new SidebarSessionDrop.Gap(w1, 0));
        Assert.Equal(new[] { b, a, h }, Latest.DropModel.Workspaces[0].Sessions);
        Assert.Equal(new[] { b, a }, Latest.Rows.Where(r => r.Kind == SidebarRowKind.Session).Select(r => r.Id));
    }

    [Fact]
    public void Workspace_drop_under_the_filter_lands_next_to_the_row_it_was_aimed_at()
    {
        Guid w1 = Latest.Workspaces[0].Id, w2 = default, w3 = default, w4 = default;
        Mutate(store =>
        {
            w2 = store.AddWorkspace("w2", revealNewWorkspace: false).Id;
            w3 = store.AddWorkspace("w3", revealNewWorkspace: false).Id;
            w4 = store.AddWorkspace("w4", revealNewWorkspace: false).Id;
        });
        _controller.ToggleFocusMembership(w2);
        _controller.ToggleFocusMembership(w4);
        _controller.ToggleWorkspaceFilter();
        Assert.Equal([w2, w4], Latest.Rows.Where(r => r.Kind == SidebarRowKind.Workspace).Select(r => r.Id));

        // slot 0 is above the first rendered row: w4 lands just before w2, not before the hidden w1
        _controller.DropWorkspace(w4, slot: 0);

        Assert.Equal([w1, w4, w2, w3], Latest.Workspaces.Select(w => w.Id));
    }

    [Fact]
    public void Surface_lifecycle_states_map_to_SurfaceStatuses_in_snapshot()
    {
        var sessionId = Latest.SelectedSessionId!.Value;

        Mutate(store =>
        {
            store.SessionWithId(sessionId)!.Surface = new PendingTerminalSurface();
            store.NotifySidebar();
        });
        Assert.Equal(SidebarSurfaceStatus.Pending, Latest.SurfaceStatuses[sessionId]);

        Mutate(store =>
        {
            store.SessionWithId(sessionId)!.Surface = new FakeSurface();
            store.NotifySidebar();
        });
        Assert.Equal(SidebarSurfaceStatus.Ready, Latest.SurfaceStatuses[sessionId]);

        Mutate(store =>
        {
            store.SessionWithId(sessionId)!.Surface = null;
            store.NotifySidebar();
        });
        Assert.Equal(SidebarSurfaceStatus.Failed, Latest.SurfaceStatuses[sessionId]);
    }

    // =========================================================================
    // Lifecycle and async snapshot ordering — driven by a fake ILiveStoreCoordinator
    // =========================================================================

    [Fact]
    public void Lifecycle_Start_requests_initial_ReadAsync_and_subscribes_SidebarInvalidated()
    {
        var fake = new FakeCoordinator();
        fake.EnqueuePendingRead(); // consumed by Start
        fake.EnqueuePendingRead(); // consumed by the SidebarInvalidated-triggered read

        using var controller = new SidebarController(fake, runOnUi: work => work());
        controller.Start();
        Assert.Equal(1, fake.ReadAsyncCallCount);

        fake.RaiseSidebarInvalidated();
        Assert.Equal(2, fake.ReadAsyncCallCount);
    }

    [Fact]
    public void Lifecycle_stale_generation_dropped_when_newer_read_completes_first()
    {
        var fake = new FakeCoordinator();
        var snapshots = new List<SidebarSnapshot>();

        using var controller = new SidebarController(fake, runOnUi: work => work());
        controller.SnapshotChanged += snapshots.Add;

        var tcsInit = fake.EnqueuePendingRead(); // generation 0 (Start's initial read)
        var tcs1 = fake.EnqueuePendingRead();    // generation 1 (first SidebarInvalidated)
        var tcs2 = fake.EnqueuePendingRead();    // generation 2 (second SidebarInvalidated)

        controller.Start();
        fake.RaiseSidebarInvalidated();
        fake.RaiseSidebarInvalidated();

        var snap2 = MakeFakeSnapshot();
        var snap1 = MakeFakeSnapshot();

        // Newest (gen 2) completes first; controller uses ContinueWith(ExecuteSynchronously)
        // so SetResult drives the continuation synchronously on this thread.
        tcs2.SetResult(snap2);
        Assert.Single(snapshots);
        Assert.Same(snap2, snapshots[0]);

        // Oldest (gen 1) arrives late — generation check drops it
        tcs1.SetResult(snap1);
        Assert.Single(snapshots);

        // Initial read (gen 0) also dropped
        tcsInit.SetResult(MakeFakeSnapshot());
        Assert.Single(snapshots);
    }

    [Fact]
    public void Lifecycle_Dispose_suppresses_pending_read_delivery_and_unsubscribes()
    {
        var fake = new FakeCoordinator();
        var snapshots = new List<SidebarSnapshot>();

        using var controller = new SidebarController(fake, runOnUi: work => work());
        controller.SnapshotChanged += snapshots.Add;

        var tcsInit = fake.EnqueuePendingRead();
        controller.Start();
        Assert.Equal(1, fake.ReadAsyncCallCount);

        controller.Dispose();

        // Completing the pending read after Dispose must not deliver SnapshotChanged
        tcsInit.SetResult(MakeFakeSnapshot());
        Assert.Empty(snapshots);

        // SidebarInvalidated is unsubscribed; raising it must not trigger ReadAsync
        var callsBefore = fake.ReadAsyncCallCount;
        fake.RaiseSidebarInvalidated();
        Assert.Equal(callsBefore, fake.ReadAsyncCallCount);
    }

    private static SidebarSnapshot MakeFakeSnapshot() =>
        new SidebarSnapshot
        {
            Rows = Array.Empty<SidebarRow>(),
            ShapeKey = string.Empty,
            Workspaces = Array.Empty<SidebarWorkspaceInfo>(),
        };

    /// <summary>
    /// Minimal ILiveStoreCoordinator stub for lifecycle and async-ordering tests.
    /// ReadAsync expects T == SidebarSnapshot and returns pre-staged controllable tasks;
    /// MutateAsync records calls but is a no-op; events are manually raisable; Dispose is idempotent.
    /// </summary>
    private sealed class FakeCoordinator : ILiveStoreCoordinator
    {
        private int _disposed;
        private readonly Queue<TaskCompletionSource<SidebarSnapshot>> _pendingReads = new();

        public int ReadAsyncCallCount { get; private set; }
        public int MutateAsyncCallCount { get; private set; }

        public event Action? SidebarInvalidated;
        public event Action<Guid?>? SelectionChanged;
        public event Action<LiveSelectionSnapshot>? SelectionSnapshotChanged
        {
            add { }
            remove { }
        }

        public TaskCompletionSource<SidebarSnapshot> EnqueuePendingRead()
        {
            var tcs = new TaskCompletionSource<SidebarSnapshot>();
            _pendingReads.Enqueue(tcs);
            return tcs;
        }

        public Task<T> ReadAsync<T>(Func<AppStore, T> read, CancellationToken cancellationToken = default)
        {
            ReadAsyncCallCount++;
            var tcs = _pendingReads.Dequeue();
            return (Task<T>)(object)tcs.Task;
        }

        public Task<TResult> ReadAsync<TSnapshot, TResult>(
            Func<TSnapshot> captureOutsideGate,
            Func<AppStore, TSnapshot, TResult> readUnderGate,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task MutateAsync(Action<AppStore> mutate, CancellationToken cancellationToken = default)
        {
            MutateAsyncCallCount++;
            return Task.CompletedTask;
        }

        public void RaiseSidebarInvalidated() => SidebarInvalidated?.Invoke();
        public void RaiseSelectionChanged(Guid? id) => SelectionChanged?.Invoke(id);

        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }
}

public sealed class SidebarGesturePolicyTests
{
    public static IEnumerable<object[]> GestureCases => new[]
    {
        // workspace Tap: a workspace row folds wherever it renders (the tree and the flagged tree layout)
        new object[] { SidebarRowKind.Workspace, SidebarMode.Tree,    SidebarPointerGesture.Tap,       SidebarGestureAction.ToggleWorkspace },
        new object[] { SidebarRowKind.Workspace, SidebarMode.Flagged, SidebarPointerGesture.Tap,       SidebarGestureAction.ToggleWorkspace },
        // session Tap: tree or flagged, selected or unselected → ActivateSession
        new object[] { SidebarRowKind.Session,   SidebarMode.Tree,    SidebarPointerGesture.Tap,       SidebarGestureAction.ActivateSession  },
        new object[] { SidebarRowKind.Session,   SidebarMode.Flagged, SidebarPointerGesture.Tap,       SidebarGestureAction.ActivateSession  },
        // DoubleTap → None on both kinds and both modes (no rename, no duplicate single-tap)
        new object[] { SidebarRowKind.Session,   SidebarMode.Tree,    SidebarPointerGesture.DoubleTap, SidebarGestureAction.None            },
        new object[] { SidebarRowKind.Session,   SidebarMode.Flagged, SidebarPointerGesture.DoubleTap, SidebarGestureAction.None            },
        new object[] { SidebarRowKind.Workspace, SidebarMode.Tree,    SidebarPointerGesture.DoubleTap, SidebarGestureAction.None            },
        new object[] { SidebarRowKind.Workspace, SidebarMode.Flagged, SidebarPointerGesture.DoubleTap, SidebarGestureAction.None            },
    };

    [Theory]
    [MemberData(nameof(GestureCases))]
    public void Decide_maps_gesture_to_expected_action(
        SidebarRowKind kind, SidebarMode mode, SidebarPointerGesture gesture, SidebarGestureAction expected)
    {
        Assert.Equal(expected, SidebarGesturePolicy.Decide(kind, mode, gesture));
    }
}

public sealed class SidebarKeyboardGesturePolicyTests
{
    public static IEnumerable<object[]> KeyboardCases => new[]
    {
        // tree workspace: Enter → ToggleWorkspace, Space → ToggleWorkspace
        new object[] { SidebarRowKind.Workspace, SidebarMode.Tree,    SidebarKeyboardGesture.Enter, SidebarGestureAction.ToggleWorkspace },
        new object[] { SidebarRowKind.Workspace, SidebarMode.Tree,    SidebarKeyboardGesture.Space, SidebarGestureAction.ToggleWorkspace },
        // flagged tree layout workspace: Enter → ToggleWorkspace, Space → ToggleWorkspace
        new object[] { SidebarRowKind.Workspace, SidebarMode.Flagged, SidebarKeyboardGesture.Enter, SidebarGestureAction.ToggleWorkspace },
        new object[] { SidebarRowKind.Workspace, SidebarMode.Flagged, SidebarKeyboardGesture.Space, SidebarGestureAction.ToggleWorkspace },
        // session Enter: tree or flagged → ActivateSession
        new object[] { SidebarRowKind.Session,   SidebarMode.Tree,    SidebarKeyboardGesture.Enter, SidebarGestureAction.ActivateSession  },
        new object[] { SidebarRowKind.Session,   SidebarMode.Flagged, SidebarKeyboardGesture.Enter, SidebarGestureAction.ActivateSession  },
        // session Space: tree or flagged → None (ListView selection; SelectionChanged owns activation)
        new object[] { SidebarRowKind.Session,   SidebarMode.Tree,    SidebarKeyboardGesture.Space, SidebarGestureAction.None            },
        new object[] { SidebarRowKind.Session,   SidebarMode.Flagged, SidebarKeyboardGesture.Space, SidebarGestureAction.None            },
        // F2 on a session or a workspace row → Rename
        new object[] { SidebarRowKind.Session,   SidebarMode.Tree,    SidebarKeyboardGesture.F2,    SidebarGestureAction.Rename           },
        new object[] { SidebarRowKind.Session,   SidebarMode.Flagged, SidebarKeyboardGesture.F2,    SidebarGestureAction.Rename           },
        new object[] { SidebarRowKind.Workspace, SidebarMode.Tree,    SidebarKeyboardGesture.F2,    SidebarGestureAction.Rename           },
        new object[] { SidebarRowKind.Workspace, SidebarMode.Flagged, SidebarKeyboardGesture.F2,    SidebarGestureAction.Rename           },
        // unsupported enum values → None
        new object[] { (SidebarRowKind)99,       SidebarMode.Tree,    SidebarKeyboardGesture.Enter, SidebarGestureAction.None            },
        new object[] { SidebarRowKind.Session,   (SidebarMode)99,     SidebarKeyboardGesture.Enter, SidebarGestureAction.None            },
        new object[] { SidebarRowKind.Session,   SidebarMode.Tree,    (SidebarKeyboardGesture)99,   SidebarGestureAction.None            },
    };

    [Theory]
    [MemberData(nameof(KeyboardCases))]
    public void DecideKeyboard_maps_gesture_to_expected_action(
        SidebarRowKind kind, SidebarMode mode, SidebarKeyboardGesture gesture, SidebarGestureAction expected)
    {
        Assert.Equal(expected, SidebarGesturePolicy.DecideKeyboard(kind, mode, gesture));
    }
}

public sealed class SidebarKeyboardGesturePolicyIsSelectedTests
{
    // Session + Enter + not selected → SelectSession (select without activate)
    // Session + Enter + already selected → ActivateSession (refocus/activate)
    // Non-session: Tree Workspace + Enter ignores selected flag → ToggleWorkspace

    public static IEnumerable<object[]> IsSelectedCases => new[]
    {
        // Session, Enter, not selected: Tree → SelectSession
        new object[] { SidebarRowKind.Session,   SidebarMode.Tree,    SidebarKeyboardGesture.Enter, false, SidebarGestureAction.SelectSession  },
        // Session, Enter, not selected: Flagged → SelectSession
        new object[] { SidebarRowKind.Session,   SidebarMode.Flagged, SidebarKeyboardGesture.Enter, false, SidebarGestureAction.SelectSession  },
        // Session, Enter, already selected: Tree → ActivateSession
        new object[] { SidebarRowKind.Session,   SidebarMode.Tree,    SidebarKeyboardGesture.Enter, true,  SidebarGestureAction.ActivateSession },
        // Session, Enter, already selected: Flagged → ActivateSession
        new object[] { SidebarRowKind.Session,   SidebarMode.Flagged, SidebarKeyboardGesture.Enter, true,  SidebarGestureAction.ActivateSession },
        // Workspace, Tree, Enter, not selected → ToggleWorkspace (selected flag is irrelevant for non-session)
        new object[] { SidebarRowKind.Workspace, SidebarMode.Tree,    SidebarKeyboardGesture.Enter, false, SidebarGestureAction.ToggleWorkspace },
        // Workspace, Tree, Enter, selected → ToggleWorkspace (selected flag is irrelevant for non-session)
        new object[] { SidebarRowKind.Workspace, SidebarMode.Tree,    SidebarKeyboardGesture.Enter, true,  SidebarGestureAction.ToggleWorkspace },
    };

    [Theory]
    [MemberData(nameof(IsSelectedCases))]
    public void DecideKeyboard_with_isSelected_maps_gesture_to_expected_action(
        SidebarRowKind kind, SidebarMode mode, SidebarKeyboardGesture gesture, bool isSelected, SidebarGestureAction expected)
    {
        Assert.Equal(expected, SidebarGesturePolicy.DecideKeyboard(kind, mode, gesture, isSelected));
    }
}

public sealed class SidebarAutomationTextTests
{
    // -------------------------------------------------------------------------
    // Surface(SidebarSurfaceStatus)
    // -------------------------------------------------------------------------

    [Fact]
    public void Surface_Pending_returns_terminal_starting()
    {
        Assert.Equal("Terminal starting", SidebarAutomationText.Surface(SidebarSurfaceStatus.Pending));
    }

    [Fact]
    public void Surface_Failed_returns_terminal_failed_to_start()
    {
        Assert.Equal("Terminal failed to start", SidebarAutomationText.Surface(SidebarSurfaceStatus.Failed));
    }

    [Fact]
    public void Surface_Ready_returns_empty_string()
    {
        Assert.Equal(string.Empty, SidebarAutomationText.Surface(SidebarSurfaceStatus.Ready));
    }

    // -------------------------------------------------------------------------
    // Agent(AgentStatus)
    // -------------------------------------------------------------------------

    [Fact]
    public void Agent_Blocked_returns_agent_status_blocked()
    {
        Assert.Equal("Agent status blocked", SidebarAutomationText.Agent(AgentStatus.Blocked));
    }

    [Fact]
    public void Agent_Idle_returns_empty_string()
    {
        Assert.Equal(string.Empty, SidebarAutomationText.Agent(AgentStatus.Idle));
    }

    // -------------------------------------------------------------------------
    // Unseen(int)
    // -------------------------------------------------------------------------

    [Fact]
    public void Unseen_zero_returns_empty_string()
    {
        Assert.Equal(string.Empty, SidebarAutomationText.Unseen(0));
    }

    [Fact]
    public void Unseen_one_returns_singular_notification()
    {
        Assert.Equal("1 unseen notification", SidebarAutomationText.Unseen(1));
    }

    [Fact]
    public void Unseen_many_returns_plural_notifications()
    {
        Assert.Equal("3 unseen notifications", SidebarAutomationText.Unseen(3));
    }

    // -------------------------------------------------------------------------
    // Chevron(SidebarRow)
    // -------------------------------------------------------------------------

    [Fact]
    public void Chevron_expanded_workspace_returns_collapse_label()
    {
        var row = new SidebarRow(SidebarRowKind.Workspace, Guid.NewGuid(), "api")
        {
            Collapsed = false,
        };
        Assert.Equal("Collapse workspace api", SidebarAutomationText.Chevron(row));
    }

    [Fact]
    public void Chevron_collapsed_workspace_returns_expand_label()
    {
        var row = new SidebarRow(SidebarRowKind.Workspace, Guid.NewGuid(), "api")
        {
            Collapsed = true,
        };
        Assert.Equal("Expand workspace api", SidebarAutomationText.Chevron(row));
    }

    // -------------------------------------------------------------------------
    // Row(SidebarRow, SidebarSurfaceStatus)
    // -------------------------------------------------------------------------

    [Fact]
    public void Row_workspace_expanded_focused_with_unseen_includes_all_parts()
    {
        var row = new SidebarRow(SidebarRowKind.Workspace, Guid.NewGuid(), "api")
        {
            Collapsed = false,
            Focused = true,
            Unseen = 2,
        };
        Assert.Equal(
            "Workspace api, expanded, focused, 2 unseen notifications",
            SidebarAutomationText.Row(row, SidebarSurfaceStatus.Ready));
    }

    [Fact]
    public void Row_workspace_collapsed_unfocused_no_unseen_omits_optional_parts()
    {
        var row = new SidebarRow(SidebarRowKind.Workspace, Guid.NewGuid(), "api")
        {
            Collapsed = true,
            Focused = false,
            Unseen = 0,
        };
        Assert.Equal(
            "Workspace api, collapsed",
            SidebarAutomationText.Row(row, SidebarSurfaceStatus.Ready));
    }

    [Fact]
    public void Row_session_pending_flagged_blocked_with_unseen_includes_all_parts()
    {
        var row = new SidebarRow(SidebarRowKind.Session, Guid.NewGuid(), "shell")
        {
            Flagged = true,
            Indicator = new AgentIndicator(AgentStatus.Blocked),
            Unseen = 3,
        };
        Assert.Equal(
            "Session shell, terminal starting, flagged, agent status blocked, 3 unseen notifications",
            SidebarAutomationText.Row(row, SidebarSurfaceStatus.Pending));
    }

    [Fact]
    public void Row_session_failed_surface_uses_terminal_failed_to_start()
    {
        var row = new SidebarRow(SidebarRowKind.Session, Guid.NewGuid(), "shell")
        {
            Flagged = true,
            Indicator = new AgentIndicator(AgentStatus.Blocked),
            Unseen = 3,
        };
        Assert.Equal(
            "Session shell, terminal failed to start, flagged, agent status blocked, 3 unseen notifications",
            SidebarAutomationText.Row(row, SidebarSurfaceStatus.Failed));
    }

    [Fact]
    public void Row_session_ready_idle_unflagged_no_unseen_returns_label_only()
    {
        var row = new SidebarRow(SidebarRowKind.Session, Guid.NewGuid(), "shell")
        {
            Flagged = false,
            Indicator = new AgentIndicator(AgentStatus.Idle),
            Unseen = 0,
        };
        Assert.Equal(
            "Session shell",
            SidebarAutomationText.Row(row, SidebarSurfaceStatus.Ready));
    }

    [Fact]
    public void Row_names_its_tint_color_as_upstream_names_it_on_the_icon()
    {
        var session = new SidebarRow(SidebarRowKind.Session, Guid.NewGuid(), "shell")
        {
            Flagged = true,
            Tint = new SidebarTint(TintColor.Orange, TintParts.Name),
        };
        var workspace = new SidebarRow(SidebarRowKind.Workspace, Guid.NewGuid(), "api")
        {
            Focused = true,
            Tint = new SidebarTint(TintColor.Red),
        };

        Assert.Equal("Session shell, flagged, orange tint", SidebarAutomationText.Row(session, SidebarSurfaceStatus.Ready));
        Assert.Equal("Workspace api, expanded, focused, red tint", SidebarAutomationText.Row(workspace, SidebarSurfaceStatus.Ready));
    }
}
