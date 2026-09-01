using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Unit tests for <c>LiveSelectionSnapshot.ToTerminalLayout(long)</c>.
/// No WebView2 or ConPTY required; all inputs are pure value objects.
/// </summary>
public sealed class LiveSelectionSnapshotLayoutTests
{
    [Fact]
    public void ToTerminalLayout_returns_Empty_when_SessionId_is_null()
    {
        var snapshot = new LiveSelectionSnapshot(null, null);
        var layout = snapshot.ToTerminalLayout(1);
        Assert.IsType<TerminalLayout.Empty>(layout);
        Assert.Equal(1, layout.Generation);
    }

    [Fact]
    public void ToTerminalLayout_returns_Empty_when_main_pane_id_is_missing()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", null,
            MainPaneId: null,
            SplitPaneId: null,
            IsSplit: false,
            SplitFocused: false,
            SplitRatio: null);
        Assert.IsType<TerminalLayout.Empty>(snapshot.ToTerminalLayout(1));
    }

    [Fact]
    public void ToTerminalLayout_Single_generation_is_preserved()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", "ws",
            MainPaneId: "main-token",
            SplitPaneId: null,
            IsSplit: false,
            SplitFocused: false,
            SplitRatio: null);
        Assert.Equal(77, snapshot.ToTerminalLayout(77).Generation);
    }

    [Fact]
    public void ToTerminalLayout_returns_Single_of_main_when_no_split_token()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", "ws",
            MainPaneId: "main-token",
            SplitPaneId: null,
            IsSplit: false,
            SplitFocused: false,
            SplitRatio: null);
        var layout = snapshot.ToTerminalLayout(5);
        var single = Assert.IsType<TerminalLayout.Single>(layout);
        Assert.Equal("main-token", single.PaneId);
        Assert.Equal(5, single.Generation);
    }

    [Fact]
    public void ToTerminalLayout_returns_Split_when_IsSplit_and_both_tokens_present_left_focused()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", null,
            MainPaneId: "left-token",
            SplitPaneId: "right-token",
            IsSplit: true,
            SplitFocused: false,
            SplitRatio: 0.5);
        var layout = snapshot.ToTerminalLayout(42);
        var split = Assert.IsType<TerminalLayout.Split>(layout);
        Assert.Equal("left-token", split.LeftPaneId);
        Assert.Equal("right-token", split.RightPaneId);
        Assert.Equal("left-token", split.FocusedPaneId);
        Assert.InRange(split.Ratio, 0.49, 0.51);
        Assert.Equal(42, split.Generation);
    }

    [Fact]
    public void ToTerminalLayout_returns_Split_right_focused_when_SplitFocused_true()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", "workspace",
            MainPaneId: "left-token",
            SplitPaneId: "right-token",
            IsSplit: true,
            SplitFocused: true,
            SplitRatio: null);
        var split = Assert.IsType<TerminalLayout.Split>(snapshot.ToTerminalLayout(1));
        Assert.Equal("right-token", split.FocusedPaneId);
        Assert.Equal("session", split.Title);
        Assert.Equal("workspace", split.WorkspaceName);
    }

    [Fact]
    public void ToTerminalLayout_returns_Single_main_when_IsSplit_but_split_token_missing()
    {
        // IsSplit=true but the split surface hasn't realized yet — fall back to Single.
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", null,
            MainPaneId: "main-token",
            SplitPaneId: null,
            IsSplit: true,
            SplitFocused: false,
            SplitRatio: null);
        var single = Assert.IsType<TerminalLayout.Single>(snapshot.ToTerminalLayout(1));
        Assert.Equal("main-token", single.PaneId);
    }

    [Fact]
    public void ToTerminalLayout_returns_Single_of_main_when_split_hidden_and_main_focused()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", null,
            MainPaneId: "left-token",
            SplitPaneId: "right-token",
            IsSplit: false,
            SplitFocused: false,
            SplitRatio: null);
        var single = Assert.IsType<TerminalLayout.Single>(snapshot.ToTerminalLayout(1));
        Assert.Equal("left-token", single.PaneId);
    }

    [Fact]
    public void ToTerminalLayout_returns_Single_of_split_when_split_hidden_and_split_focused()
    {
        // Hidden split maximizes the focused pane — if focus is on right, show the right pane.
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", null,
            MainPaneId: "left-token",
            SplitPaneId: "right-token",
            IsSplit: false,
            SplitFocused: true,
            SplitRatio: null);
        var single = Assert.IsType<TerminalLayout.Single>(snapshot.ToTerminalLayout(1));
        Assert.Equal("right-token", single.PaneId);
    }

    [Fact]
    public void ToTerminalLayout_Split_ratio_is_clamped_to_AppStore_bounds()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", null,
            MainPaneId: "l",
            SplitPaneId: "r",
            IsSplit: true,
            SplitFocused: false,
            SplitRatio: 0.0); // below minimum → should clamp
        var split = Assert.IsType<TerminalLayout.Split>(snapshot.ToTerminalLayout(1));
        Assert.True(split.Ratio >= AppStore.SplitRatioMin);
    }

    [Fact]
    public void ToTerminalLayout_Split_uses_default_ratio_when_SplitRatio_is_null()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", null,
            MainPaneId: "l",
            SplitPaneId: "r",
            IsSplit: true,
            SplitFocused: false,
            SplitRatio: null);
        var split = Assert.IsType<TerminalLayout.Split>(snapshot.ToTerminalLayout(1));
        Assert.InRange(split.Ratio, 0.49, 0.51); // default is 0.5
    }

    [Fact]
    public void ToTerminalLayout_wraps_single_in_full_layer_when_scratch_is_active_and_realized()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", "workspace",
            MainPaneId: "main-token",
            ScratchPaneId: "scratch-token",
            ScratchActive: true);

        var layered = Assert.IsType<TerminalLayout.Layered>(snapshot.ToTerminalLayout(9));
        Assert.IsType<TerminalLayout.Single>(layered.BaseLayout);
        Assert.Equal("scratch-token", layered.Layer.PaneId);
        Assert.Null(layered.Layer.SizePercent);
        Assert.Equal(9, layered.Generation);
    }

    [Fact]
    public void ToTerminalLayout_wraps_split_in_full_layer_when_scratch_is_active_and_realized()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", null,
            MainPaneId: "left-token",
            SplitPaneId: "right-token",
            IsSplit: true,
            SplitFocused: true,
            SplitRatio: 0.4,
            ScratchPaneId: "scratch-token",
            ScratchActive: true);

        var layered = Assert.IsType<TerminalLayout.Layered>(snapshot.ToTerminalLayout(10));
        var split = Assert.IsType<TerminalLayout.Split>(layered.BaseLayout);
        Assert.Equal("right-token", split.FocusedPaneId);
        Assert.Equal("scratch-token", layered.Layer.PaneId);
        Assert.Null(layered.Layer.SizePercent);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "scratch-token")]
    public void ToTerminalLayout_keeps_base_when_scratch_is_inactive_or_not_realized(
        bool scratchActive,
        string? scratchPaneId)
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session",
            MainPaneId: "main-token",
            ScratchPaneId: scratchPaneId,
            ScratchActive: scratchActive);

        Assert.IsType<TerminalLayout.Single>(snapshot.ToTerminalLayout(1));
    }

    [Fact]
    public void ToTerminalLayout_keeps_empty_when_scratch_has_no_base_session()
    {
        var snapshot = new LiveSelectionSnapshot(
            null,
            null,
            ScratchPaneId: "scratch-token",
            ScratchActive: true);

        Assert.IsType<TerminalLayout.Empty>(snapshot.ToTerminalLayout(1));
    }

    [Fact]
    public void ToTerminalLayout_wraps_split_in_configured_overlay_layer()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", "workspace",
            MainPaneId: "left-token",
            SplitPaneId: "right-token",
            IsSplit: true,
            SplitFocused: true,
            SplitRatio: 0.4,
            OverlayPaneId: "overlay-token",
            OverlayActive: true,
            OverlaySizePercent: 70,
            OverlayBackgroundColor: "#123456");

        var layered = Assert.IsType<TerminalLayout.Layered>(snapshot.ToTerminalLayout(11));
        Assert.IsType<TerminalLayout.Split>(layered.BaseLayout);
        Assert.Equal("overlay-token", layered.Layer.PaneId);
        Assert.Equal(70, layered.Layer.SizePercent);
        Assert.Equal("#123456", layered.Layer.BackgroundColor);
    }

    [Fact]
    public void ToTerminalLayout_places_overlay_above_scratch_and_falls_back_when_overlay_is_unrealized()
    {
        var withOverlay = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session",
            MainPaneId: "main-token",
            ScratchPaneId: "scratch-token",
            ScratchActive: true,
            OverlayPaneId: "overlay-token",
            OverlayActive: true);
        var overlay = Assert.IsType<TerminalLayout.Layered>(withOverlay.ToTerminalLayout(1));
        Assert.Equal("overlay-token", overlay.Layer.PaneId);

        var withoutRealizedOverlay = withOverlay with { OverlayPaneId = null };
        var scratch = Assert.IsType<TerminalLayout.Layered>(withoutRealizedOverlay.ToTerminalLayout(2));
        Assert.Equal("scratch-token", scratch.Layer.PaneId);
    }

    // Q5.2: QuickVisible=true with a realized QuickPaneId wraps the session base in a 90% centered layer.
    [Fact]
    public void ToTerminalLayout_wraps_base_in_90pct_quick_layer_when_quick_is_visible_and_realized()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", "workspace",
            MainPaneId: "main-token",
            QuickPaneId: "quick-token",
            QuickVisible: true);

        var layered = Assert.IsType<TerminalLayout.Layered>(snapshot.ToTerminalLayout(5));
        Assert.IsType<TerminalLayout.Single>(layered.BaseLayout);
        Assert.Equal("quick-token", layered.Layer.PaneId);
        Assert.Equal(90, layered.Layer.SizePercent);
        Assert.Equal(5, layered.Generation);
    }

    // Q5.2: Quick has higher priority than active Overlay and Scratch.
    [Fact]
    public void ToTerminalLayout_places_quick_above_overlay_and_scratch()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(), "session", null,
            MainPaneId: "main-token",
            ScratchPaneId: "scratch-token",
            ScratchActive: true,
            OverlayPaneId: "overlay-token",
            OverlayActive: true,
            OverlaySizePercent: 70,
            QuickPaneId: "quick-token",
            QuickVisible: true);

        var layered = Assert.IsType<TerminalLayout.Layered>(snapshot.ToTerminalLayout(1));
        Assert.Equal("quick-token", layered.Layer.PaneId);
        Assert.Equal(90, layered.Layer.SizePercent);
    }

    // Q5.2: Quick with SessionId=null (no active session) uses TerminalLayout.Empty as its base.
    [Fact]
    public void ToTerminalLayout_quick_over_empty_when_quick_visible_and_no_session()
    {
        var snapshot = new LiveSelectionSnapshot(
            null,
            null,
            QuickPaneId: "quick-token",
            QuickVisible: true);

        var layered = Assert.IsType<TerminalLayout.Layered>(snapshot.ToTerminalLayout(3));
        Assert.IsType<TerminalLayout.Empty>(layered.BaseLayout);
        Assert.Equal("quick-token", layered.Layer.PaneId);
        Assert.Equal(90, layered.Layer.SizePercent);
        Assert.Equal(3, layered.Generation);
    }
}

public sealed class MainWindowSplitWiringTests
{
    [Fact]
    public void Headless_control_actions_do_not_expose_raw_pane_focus_subscriptions()
    {
        Assert.Null(typeof(HeadlessControlActions).GetEvent("PaneFocusRequested"));
    }

    [Fact]
    public void Bridge_programmatic_focus_suppresses_nested_focusin_reports()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "AgTerm.Xterm", "Assets", "bridge.js"));

        Assert.Contains("programmaticFocusPaneId", source, StringComparison.Ordinal);
        Assert.Contains("if (programmaticFocusPaneId !== paneId) emitPaneFocus(paneId)", source, StringComparison.Ordinal);
        Assert.Contains("focus: (paneId) => focusPane(paneId, true)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_routes_split_scratch_and_overlay_presentation_through_live_owners()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("_selection.ToTerminalLayout(generation)", source, StringComparison.Ordinal);
        Assert.Contains("page.PaneFocusChanged += OnPagePaneFocusChanged", source, StringComparison.Ordinal);
        Assert.Contains("page.SearchUpdated += OnPageSearchUpdated", source, StringComparison.Ordinal);
        Assert.Contains("page.SearchClosed += OnPageSearchClosed", source, StringComparison.Ordinal);
        Assert.Contains("host.PaneFocusRequested +=", source, StringComparison.Ordinal);
        Assert.Contains(
            "request.Kind is LiveSurfaceKind.Main or LiveSurfaceKind.Split or LiveSurfaceKind.Scratch or LiveSurfaceKind.Overlay",
            source,
            StringComparison.Ordinal);
        Assert.Contains("session.OverlaySurface", source, StringComparison.Ordinal);
        Assert.Contains("await surface.ShutdownStarted", source, StringComparison.Ordinal);
        Assert.Contains("case TerminalLayout.Layered layered:", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.ToggleSplit]", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.ToggleScratch]", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.ToggleSearch]", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.QuickTerminal]", source, StringComparison.Ordinal);
        Assert.Contains("host.ToggleQuickTerminalAsync", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.FocusLeftPane]", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.FocusRightPane]", source, StringComparison.Ordinal);
    }

    // Q5.2: MainWindow must mount Quick surfaces and toggle terminal-only presentation from the snapshot.
    [Fact]
    public void MainWindow_mounts_quick_surface_and_drives_presentation_from_snapshot_QuickVisible()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        // Quick surfaces must be mounted in the same SurfaceRealized handler as Main/Scratch/Overlay.
        Assert.Contains("LiveSurfaceKind.Quick", source, StringComparison.Ordinal);
        // Snapshot-driven toggle: SetTerminalOnlyPresentation must be called with snapshot.QuickVisible.
        Assert.Matches(
            @"SetTerminalOnlyPresentation\(\s*snapshot\.QuickVisible && !string\.IsNullOrWhiteSpace\(snapshot\.QuickPaneId\)\);",
            source);
        Assert.Matches(
            @"_acceptedPaneId = layered\.Layer\.PaneId;\s+FocusActiveTerminal\(\);",
            source);
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}

public sealed class SearchPresentationTransitionTests
{
    private static readonly Guid SessionA = Guid.NewGuid();
    private static readonly Guid SessionB = Guid.NewGuid();

    [Fact]
    public void Same_session_close_clears_exact_owner_and_restores_accepted_pane()
    {
        var previous = SearchSnapshot(SessionA, "owner-a", active: true);
        var current = SearchSnapshot(SessionA, null, active: false);

        var hide = Assert.IsType<SearchPresentationTransition.Hide>(
            SearchPresentationTransition.Resolve(previous, current, "visible-pane"));

        Assert.Equal("owner-a", hide.ClearOwnerPaneId);
        Assert.Equal("visible-pane", hide.RestorePaneId);
    }

    [Fact]
    public void Session_switch_hides_bar_without_clearing_background_owner_or_moving_focus()
    {
        var previous = SearchSnapshot(SessionA, "owner-a", active: true);
        var current = SearchSnapshot(SessionB, null, active: false);

        var hide = Assert.IsType<SearchPresentationTransition.Hide>(
            SearchPresentationTransition.Resolve(previous, current, "visible-b"));

        Assert.Null(hide.ClearOwnerPaneId);
        Assert.Null(hide.RestorePaneId);
    }

    [Fact]
    public void Selecting_session_with_open_search_shows_and_focuses_its_pinned_owner()
    {
        var previous = SearchSnapshot(SessionB, null, active: false);
        var current = SearchSnapshot(SessionA, "owner-a", active: true);

        var show = Assert.IsType<SearchPresentationTransition.Show>(
            SearchPresentationTransition.Resolve(previous, current, "visible-a"));

        Assert.Equal("owner-a", show.PaneId);
        Assert.True(show.FocusInput);
    }

    [Fact]
    public void Same_owner_query_update_does_not_refocus_input()
    {
        var previous = SearchSnapshot(SessionA, "owner-a", active: true) with { SearchNeedle = "a" };
        var current = previous with { SearchNeedle = "ab", SearchDisplayText = "2 matches" };

        var show = Assert.IsType<SearchPresentationTransition.Show>(
            SearchPresentationTransition.Resolve(previous, current, "visible-a"));

        Assert.False(show.FocusInput);
        Assert.Equal("ab", show.Needle);
        Assert.Equal("2 matches", show.DisplayText);
    }

    private static LiveSelectionSnapshot SearchSnapshot(Guid sessionId, string? owner, bool active) =>
        new(
            sessionId,
            "session",
            MainPaneId: "main",
            SearchOwnerPaneId: owner,
            SearchActive: active);
}

/// <summary>
/// Unit tests for <c>LiveControlHost.ApplyPaneFocusAsync(string, CancellationToken)</c>.
/// Uses a fake-surface host (no ConPTY/WebView2) to verify the identity and idempotence contract.
/// </summary>
[Collection("webview2")]
public sealed class LiveControlHostApplyPaneFocusTests : IDisposable
{
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-live-focus-").FullName;

    private readonly LiveControlHost _host;
    private readonly Guid _sessionId;
    private volatile string? _mainToken;
    private volatile string? _splitToken;

    public LiveControlHostApplyPaneFocusTests()
    {
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-live-focus-" + Guid.NewGuid().ToString("N"),
            DefaultShell = "test-shell.exe",
            HomeDirectory = _stateDir,
            OpenSurface = (request, _) =>
            {
                var surface = new FakeSurface { PaneToken = request.Kind.ToString() + "-" + Guid.NewGuid().ToString("N")[..6] };
                if (request.Kind == LiveSurfaceKind.Main) _mainToken = surface.PaneToken;
                else if (request.Kind == LiveSurfaceKind.Split) _splitToken = surface.PaneToken;
                return Task.FromResult<IHeadlessSurface>(surface);
            },
        });
        _host.Start();

        // Create a session and enable the split so both Main and Split surfaces exist.
        var client = new ControlPipeClient(_host.PipeName);
        var sessionResp = client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Cwd = _stateDir },
        });
        Assert.True(sessionResp.Ok, sessionResp.Error);
        _sessionId = Guid.Parse(sessionResp.Result!.Id!);

        var splitResp = client.Send(new ControlRequest
        {
            Cmd = Command.SessionSplit,
            Args = new ControlArgs { Mode = "on" },
        });
        Assert.True(splitResp.Ok, splitResp.Error);

        // Give the surfaces a moment to realize.
        var deadline = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < deadline && (_mainToken is null || _splitToken is null))
            Thread.Sleep(50);
        Assert.NotNull(_mainToken);
        Assert.NotNull(_splitToken);
    }

    public void Dispose()
    {
        _host.Dispose();
        try { Directory.Delete(_stateDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task ApplyPaneFocusAsync_main_token_sets_SplitFocused_false()
    {
        // Arrange: ensure split is currently focused.
        _host.Host.RunLocked(() =>
        {
            var s = _host.Host.Store.ActiveSession!;
            s.SplitFocused = true;
        });

        var changed = await _host.ApplyPaneFocusAsync(_mainToken!);

        Assert.True(changed);
        bool splitFocused = true;
        _host.Host.RunLocked(() =>
        {
            var s = _host.Host.Store.ActiveSession!;
            splitFocused = s.SplitFocused;
        });
        Assert.False(splitFocused);
    }

    [Fact]
    public async Task ApplyPaneFocusAsync_split_token_sets_SplitFocused_true()
    {
        // Arrange: ensure main is currently focused.
        _host.Host.RunLocked(() =>
        {
            var s = _host.Host.Store.ActiveSession!;
            s.SplitFocused = false;
        });

        var changed = await _host.ApplyPaneFocusAsync(_splitToken!);

        Assert.True(changed);
        bool splitFocused = false;
        _host.Host.RunLocked(() =>
        {
            var s = _host.Host.Store.ActiveSession!;
            splitFocused = s.SplitFocused;
        });
        Assert.True(splitFocused);
    }

    [Fact]
    public async Task ApplyPaneFocusAsync_duplicate_report_same_pane_is_idempotent_and_returns_false()
    {
        // First call changes state → returns true.
        await _host.ApplyPaneFocusAsync(_splitToken!);

        // Second call with the same token: no state change → returns false.
        var changed = await _host.ApplyPaneFocusAsync(_splitToken!);
        Assert.False(changed);
    }

    [Fact]
    public async Task ApplyPaneFocusAsync_unknown_token_returns_false()
    {
        var changed = await _host.ApplyPaneFocusAsync("unknown-pane-xyz");
        Assert.False(changed);
    }

    [Fact]
    public async Task ApplyPaneFocusAsync_stale_token_returns_false()
    {
        // Token that was never assigned to any live surface.
        var changed = await _host.ApplyPaneFocusAsync("stale-token-" + Guid.NewGuid().ToString("N"));
        Assert.False(changed);
    }

    [Fact]
    public async Task ApplyPaneFocusAsync_switches_the_maximized_pane_while_split_is_hidden()
    {
        var client = new ControlPipeClient(_host.PipeName);
        var hidden = client.Send(new ControlRequest
        {
            Cmd = Command.SessionSplit,
            Args = new ControlArgs { Mode = "off" },
        });
        Assert.True(hidden.Ok, hidden.Error);

        var mainSnapshot = new TaskCompletionSource<LiveSelectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            if (snapshot.SessionId == _sessionId && !snapshot.SplitFocused)
                mainSnapshot.TrySetResult(snapshot);
        };
        Assert.True(await _host.ApplyPaneFocusAsync(_mainToken!));
        var mainLayout = Assert.IsType<TerminalLayout.Single>(
            (await mainSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(5))).ToTerminalLayout(1));
        Assert.Equal(_mainToken, mainLayout.PaneId);

        var splitSnapshot = new TaskCompletionSource<LiveSelectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            if (snapshot.SessionId == _sessionId && snapshot.SplitFocused)
                splitSnapshot.TrySetResult(snapshot);
        };
        Assert.True(await _host.ApplyPaneFocusAsync(_splitToken!));
        var splitLayout = Assert.IsType<TerminalLayout.Single>(
            (await splitSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(5))).ToTerminalLayout(2));
        Assert.Equal(_splitToken, splitLayout.PaneId);
    }

    [Fact]
    public async Task Session_focus_wire_publishes_updated_selection_snapshot()
    {
        var received = new TaskCompletionSource<LiveSelectionSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            if (snapshot.SessionId == _sessionId && !snapshot.SplitFocused)
            {
                received.TrySetResult(snapshot);
            }
        };

        var client = new ControlPipeClient(_host.PipeName);
        var response = client.Send(new ControlRequest
        {
            Cmd = Command.SessionFocus,
            Args = new ControlArgs { Pane = "left" },
        });

        Assert.True(response.Ok, response.Error);
        var snapshot = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(_mainToken, snapshot.MainPaneId);
        Assert.Equal(_splitToken, snapshot.SplitPaneId);
        Assert.False(snapshot.SplitFocused);
    }

    [Fact]
    public async Task Session_focus_wire_requests_keyboard_focus_even_when_model_already_matches()
    {
        _host.Host.RunLocked(() =>
            _host.Host.Store.SessionWithId(_sessionId)!.SplitFocused = true);
        var requested = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _host.PaneFocusRequested += paneId => requested.TrySetResult(paneId);

        var client = new ControlPipeClient(_host.PipeName);
        var response = client.Send(new ControlRequest
        {
            Cmd = Command.SessionFocus,
            Args = new ControlArgs { Pane = "right" },
        });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(_splitToken, await requested.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Shutdown_waits_for_an_in_flight_pane_focus_delivery()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _host.PaneFocusRequested += _ =>
        {
            entered.Set();
            release.Wait();
        };

        var client = new ControlPipeClient(_host.PipeName);
        var response = client.Send(new ControlRequest
        {
            Cmd = Command.SessionFocus,
            Args = new ControlArgs { Pane = "left" },
        });
        Assert.True(response.Ok, response.Error);
        Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(5))));

        var shutdown = _host.ShutdownAsync();
        await Task.Delay(100);
        Assert.False(shutdown.IsCompleted);

        release.Set();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
