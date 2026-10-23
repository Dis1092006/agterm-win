using AgTerm.ControlServer;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using System.Text.Json;

namespace AgTerm.Xterm.Tests;

public sealed class LiveDashboardE2ETests
{
    [Fact]
    public void Dashboard_presentation_projects_exact_existing_panes_and_highlight()
    {
        var sessionId = Guid.NewGuid();
        var leftRef = $"{sessionId:D}:left".ToUpperInvariant();
        var rightRef = $"{sessionId:D}:right".ToUpperInvariant();
        var snapshot = new LiveSelectionSnapshot(
            sessionId,
            "session",
            MainPaneId: "main-token",
            SplitPaneId: "split-token",
            IsSplit: true,
            DashboardCells:
            [
                new TerminalDashboardCell("main-token", leftRef),
                new TerminalDashboardCell("split-token", rightRef),
            ],
            DashboardHighlighted: rightRef,
            DashboardFontSize: 9);

        var dashboard = Assert.IsType<TerminalLayout.Dashboard>(
            snapshot.ToTerminalLayout(12));

        Assert.Equal(12, dashboard.Generation);
        Assert.Equal(["main-token", "split-token"], dashboard.Cells.Select(cell => cell.PaneId));
        Assert.Equal(rightRef, dashboard.HighlightedControlRef);
        Assert.Equal(9, dashboard.FontSize);
    }

    [Fact]
    public void Dashboard_layout_copies_cells_and_rejects_invalid_shapes()
    {
        var mutable = new List<TerminalDashboardCell>
        {
            new("one", "A:left"),
        };
        var layout = new TerminalLayout.Dashboard(1, mutable, "A:left");
        mutable.Add(new TerminalDashboardCell("two", "B:left"));
        Assert.Single(layout.Cells);

        Assert.Throws<ArgumentException>(() =>
            new TerminalLayout.Dashboard(1, [], "A:left"));
        Assert.Throws<ArgumentException>(() =>
            new TerminalLayout.Dashboard(
                1,
                Enumerable.Range(0, 10)
                    .Select(index => new TerminalDashboardCell($"pane-{index}", $"{index}:left"))
                    .ToArray(),
                "0:left"));
        Assert.Throws<ArgumentException>(() =>
            new TerminalLayout.Dashboard(
                1,
                [new("same", "A:left"), new("same", "B:left")],
                "A:left"));
        Assert.Throws<ArgumentException>(() =>
            new TerminalLayout.Dashboard(
                1,
                [new("one", "same"), new("two", "same")],
                "same"));
        Assert.Throws<ArgumentException>(() =>
            new TerminalLayout.Dashboard(1, [new("one", "A:left")], "B:left"));
        Assert.Throws<ArgumentException>(() => new TerminalDashboardCell("", "A:left"));
        Assert.Throws<ArgumentException>(() => new TerminalDashboardCell("one", ""));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Dashboard_layout_rejects_invalid_font_size(double fontSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TerminalLayout.Dashboard(
                1,
                [new TerminalDashboardCell("one", "A:left")],
                "A:left",
                fontSize));
    }

    [Fact]
    public async Task Explicit_targets_publish_ordered_split_expansion_highlight_and_fixed_font()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-dashboard-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            Guid firstId = default;
            Guid secondId = default;
            host.Host.RunLocked(() =>
            {
                var first = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                var workspace = Assert.Single(host.Host.Store.Workspaces);
                var second = host.Host.Store.AddSession(
                    workspace.Id,
                    stateDir,
                    name: "second",
                    select: false)!;
                firstId = first.Id;
                secondId = second.Id;
                first.Surface = new FakeSurface("first-main");
                first.SplitSurface = new FakeSurface("first-split");
                first.HasSplit = true;
                second.Surface = new FakeSurface("second-main");
            });

            var published = new TaskCompletionSource<LiveSelectionSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
            {
                if (snapshot.DashboardOpen)
                    published.TrySetResult(snapshot);
            };

            host.Host.RunLocked(() =>
            {
                var response = host.Host.Actions.SetDashboard(
                    [firstId.ToString("D"), secondId.ToString("D")],
                    window: null,
                    close: false,
                    new DashboardFontMode.Fixed(11),
                    mru: false);
                Assert.True(response.Ok, response.Error);
            });

            var snapshot = await published.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var cells = Assert.IsAssignableFrom<IReadOnlyList<TerminalDashboardCell>>(
                snapshot.DashboardCells);
            Assert.Equal(
                ["first-main", "first-split", "second-main"],
                cells.Select(cell => cell.PaneId));
            Assert.Equal(
                [ControlRef(firstId, "left"), ControlRef(firstId, "right"), ControlRef(secondId, "left")],
                cells.Select(cell => cell.ControlRef));
            Assert.Equal(ControlRef(firstId, "left"), snapshot.DashboardHighlighted);
            Assert.Equal(11, snapshot.DashboardFontSize);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Live_interactions_update_highlight_activate_exact_split_and_balance_transient_state()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-dashboard-actions-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            Guid firstId = default;
            Guid secondId = default;
            host.Host.RunLocked(() =>
            {
                var first = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                var workspace = Assert.Single(host.Host.Store.Workspaces);
                var second = host.Host.Store.AddSession(workspace.Id, stateDir, select: false)!;
                firstId = first.Id;
                secondId = second.Id;
                first.Surface = new FakeSurface("first-main");
                first.SplitSurface = new FakeSurface("first-split");
                first.HasSplit = true;
                second.Surface = new FakeSurface("second-main");
                host.Host.Actions.Quick.Visible = true;
                host.Host.Actions.Quick.Surface = new FakeSurface("quick");
                var response = host.Host.Actions.SetDashboard(
                    [firstId.ToString("D"), secondId.ToString("D")],
                    window: null,
                    close: false,
                    new DashboardFontMode.Auto(),
                    mru: false);
                Assert.True(response.Ok, response.Error);
            });
            Assert.False(ReadLocked(host, () => host.Host.Actions.Quick.Visible));
            Assert.Equal(1, ReadLocked(host, () => AutoFollowSuppressionCount(host.Host.Store)));

            var secondRef = ControlRef(secondId, "left");
            Assert.True(await host.HighlightDashboardMemberAsync(secondRef));
            Assert.Equal(secondRef, ReadLocked(host,
                () => host.Host.Actions.Dashboard.Highlighted?.ControlRef));

            var rightRef = ControlRef(firstId, "right");
            Assert.True(await host.ActivateDashboardMemberAsync(rightRef));
            Assert.False(ReadLocked(host, () => host.Host.Actions.Dashboard.IsOpen));
            Assert.Equal(firstId, ReadLocked(host, () => host.Host.Store.SelectedSessionId));
            Assert.True(ReadLocked(host, () => host.Host.Store.SessionWithId(firstId)!.SplitFocused));
            Assert.Equal(0, ReadLocked(host, () => AutoFollowSuppressionCount(host.Host.Store)));

            await host.ToggleDashboardAsync();
            Assert.True(ReadLocked(host, () => host.Host.Actions.Dashboard.IsOpen));
            var selectedBeforeClose = ReadLocked(host, () => host.Host.Store.SelectedSessionId);
            await host.CloseDashboardAsync();
            Assert.False(ReadLocked(host, () => host.Host.Actions.Dashboard.IsOpen));
            Assert.Equal(selectedBeforeClose, ReadLocked(host, () => host.Host.Store.SelectedSessionId));
            Assert.Equal(0, ReadLocked(host, () => AutoFollowSuppressionCount(host.Host.Store)));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Dashboard_reconciles_closed_split_and_sessions_and_closes_when_empty()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-dashboard-reconcile-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            Guid firstId = default;
            Guid secondId = default;
            host.Host.RunLocked(() =>
            {
                var first = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                var workspace = Assert.Single(host.Host.Store.Workspaces);
                var second = host.Host.Store.AddSession(workspace.Id, stateDir, select: false)!;
                firstId = first.Id;
                secondId = second.Id;
                first.Surface = new FakeSurface("first-main");
                first.SplitSurface = new FakeSurface("first-split");
                first.HasSplit = true;
                second.Surface = new FakeSurface("second-main");
                var open = host.Host.Actions.SetDashboard(
                    [firstId.ToString("D"), secondId.ToString("D")],
                    window: null,
                    close: false,
                    new DashboardFontMode.Untouched(),
                    mru: false);
                Assert.True(open.Ok, open.Error);
            });
            Assert.True(await host.HighlightDashboardMemberAsync(ControlRef(firstId, "right")));

            await host.StoreCoordinator.MutateAsync(store => store.CloseSplitPane(firstId));

            Assert.Equal(
                [ControlRef(firstId, "left"), ControlRef(secondId, "left")],
                ReadLocked(host, () => host.Host.Actions.Dashboard.Members.Select(member => member.ControlRef)));
            Assert.Equal(
                ControlRef(firstId, "left"),
                ReadLocked(host, () => host.Host.Actions.Dashboard.Highlighted?.ControlRef));

            await host.StoreCoordinator.MutateAsync(store => store.CloseSession(firstId));
            await host.StoreCoordinator.MutateAsync(store => store.CloseSession(secondId));

            Assert.False(ReadLocked(host, () => host.Host.Actions.Dashboard.IsOpen));
            Assert.Equal(0, ReadLocked(host, () => AutoFollowSuppressionCount(host.Host.Store)));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Dashboard_reconcile_drops_a_surface_without_a_realized_pane_token()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-dashboard-unrealized-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            Guid firstId = default;
            Guid secondId = default;
            host.Host.RunLocked(() =>
            {
                var first = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                var workspace = Assert.Single(host.Host.Store.Workspaces);
                var second = host.Host.Store.AddSession(workspace.Id, stateDir, select: false)!;
                firstId = first.Id;
                secondId = second.Id;
                first.Surface = new FakeSurface("first-main");
                second.Surface = new FakeSurface(null);
                var open = host.Host.Actions.SetDashboard(
                    [firstId.ToString("D"), secondId.ToString("D")],
                    window: null,
                    close: false,
                    new DashboardFontMode.Untouched(),
                    mru: false);
                Assert.True(open.Ok, open.Error);
            });

            await host.StoreCoordinator.MutateAsync(_ => { });

            Assert.Equal(
                [ControlRef(firstId, "left")],
                ReadLocked(host, () => host.Host.Actions.Dashboard.Members.Select(member => member.ControlRef)));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Dashboard_open_prunes_unrealized_initial_highlight_before_first_publish()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-dashboard-initial-prune-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            Guid firstId = default;
            Guid secondId = default;
            host.Host.RunLocked(() =>
            {
                var first = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                var workspace = Assert.Single(host.Host.Store.Workspaces);
                var second = host.Host.Store.AddSession(workspace.Id, stateDir, select: false)!;
                firstId = first.Id;
                secondId = second.Id;
                first.Surface = new FakeSurface(null);
                second.Surface = new FakeSurface("second-main");
            });
            var published = new TaskCompletionSource<LiveSelectionSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
            {
                if (snapshot.DashboardOpen)
                    published.TrySetResult(snapshot);
            };

            host.Host.RunLocked(() =>
            {
                var open = host.Host.Actions.SetDashboard(
                    [firstId.ToString("D"), secondId.ToString("D")],
                    window: null,
                    close: false,
                    new DashboardFontMode.Untouched(),
                    mru: false);
                Assert.True(open.Ok, open.Error);
            });

            var snapshot = await published.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var cell = Assert.Single(snapshot.DashboardCells!);
            Assert.Equal(ControlRef(secondId, "left"), cell.ControlRef);
            Assert.Equal(cell.ControlRef, snapshot.DashboardHighlighted);
            Assert.Equal(
                [cell.ControlRef],
                ReadLocked(host, () => host.Host.Actions.Dashboard.Members.Select(member => member.ControlRef)));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Auto_font_uses_live_base_size_and_recomputes_after_member_prune()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-dashboard-auto-font-").FullName;
        try
        {
            using var host = CreateHost(stateDir, baseFontSize: 20);
            Guid firstId = default;
            Guid secondId = default;
            host.Host.RunLocked(() =>
            {
                var first = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                var workspace = Assert.Single(host.Host.Store.Workspaces);
                var second = host.Host.Store.AddSession(workspace.Id, stateDir, select: false)!;
                firstId = first.Id;
                secondId = second.Id;
                first.Surface = new FakeSurface("first-main");
                first.SplitSurface = new FakeSurface("first-split");
                first.HasSplit = true;
                second.Surface = new FakeSurface("second-main");
                second.SplitSurface = new FakeSurface("second-split");
                second.HasSplit = true;
                var open = host.Host.Actions.SetDashboard(
                    [firstId.ToString("D"), secondId.ToString("D")],
                    window: null,
                    close: false,
                    new DashboardFontMode.Auto(),
                    mru: false);
                Assert.True(open.Ok, open.Error);
            });
            Assert.Equal(15, ReadLocked(host, () => host.Host.Actions.Dashboard.AppliedFontSize));

            await host.StoreCoordinator.MutateAsync(store => store.CloseSession(secondId));
            await host.StoreCoordinator.MutateAsync(store => store.CloseSplitPane(firstId));

            Assert.Single(ReadLocked(host, () => host.Host.Actions.Dashboard.Members));
            Assert.Equal(20, ReadLocked(host, () => host.Host.Actions.Dashboard.AppliedFontSize));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Open_auto_dashboard_republishes_font_after_live_base_size_reload()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-dashboard-reload-font-").FullName;
        try
        {
            using var host = CreateHost(stateDir, baseFontSize: 20);
            host.Host.RunLocked(() =>
            {
                var session = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                session.Surface = new FakeSurface("main");
                var open = host.Host.Actions.SetDashboard(
                    [session.Id.ToString("D")],
                    window: null,
                    close: false,
                    new DashboardFontMode.Auto(),
                    mru: false);
                Assert.True(open.Ok, open.Error);
            });
            var published = new TaskCompletionSource<LiveSelectionSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
            {
                if (snapshot.DashboardFontSize == 24)
                    published.TrySetResult(snapshot);
            };

            await host.ApplyBaseFontSizeAsync(24);

            var snapshot = await published.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(24, snapshot.DashboardFontSize);
            Assert.Equal(24, ReadLocked(host, () => host.Host.Actions.Dashboard.AppliedFontSize));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void MainWindow_routes_dashboard_page_events_and_builtin_action()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("page.DashboardHighlighted +=", source, StringComparison.Ordinal);
        Assert.Contains("page.DashboardActivated +=", source, StringComparison.Ordinal);
        Assert.Contains("page.DashboardClosed +=", source, StringComparison.Ordinal);
        Assert.Contains("host.HighlightDashboardMemberAsync", source, StringComparison.Ordinal);
        Assert.Contains("host.ActivateDashboardMemberAsync", source, StringComparison.Ordinal);
        Assert.Contains("host.CloseDashboardAsync", source, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.Dashboard] = Async(", source, StringComparison.Ordinal);
        Assert.Contains("host.ToggleDashboardAsync()", source, StringComparison.Ordinal);
        Assert.Contains("case TerminalLayout.Dashboard dashboard:", source, StringComparison.Ordinal);
        Assert.Contains("|| snapshot.DashboardOpen", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Named_pipe_explicit_targets_expand_splits_cap_cells_and_report_all_font_modes()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-dashboard-wire-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var sessionIds = CreateSessions(host, stateDir, count: 6, split: true);
            host.Start();
            var client = new ControlPipeClient(host.PipeName);
            var missing = Guid.Empty.ToString("D");

            var fixedOpen = client.Send(new ControlRequest
            {
                Cmd = Command.Dashboard,
                Args = new ControlArgs
                {
                    Targets = sessionIds.Select(id => id.ToString("D")).Append(missing).ToArray(),
                    FontSize = 12,
                },
            });
            Assert.True(fixedOpen.Ok, fixedOpen.Error);
            Assert.Contains($"unresolved: {missing}", fixedOpen.Result?.Text);
            Assert.Contains("dropped 3 pane(s) beyond the 9-cell limit", fixedOpen.Result?.Text);
            var fixedTree = Tree(client);
            Assert.Equal(9, fixedTree.DashboardMembers?.Length);
            Assert.Equal(ControlRef(sessionIds[0], "left"), fixedTree.DashboardMembers?[0]);
            Assert.Equal(ControlRef(sessionIds[0], "right"), fixedTree.DashboardMembers?[1]);
            Assert.Equal(fixedTree.DashboardMembers?[0], fixedTree.DashboardHighlighted);
            Assert.Equal(12, fixedTree.DashboardFontSize);
            Assert.Equal("fixed", fixedTree.DashboardFontMode);

            Assert.True(client.Send(new ControlRequest
            {
                Cmd = Command.Dashboard,
                Args = new ControlArgs { Close = true },
            }).Ok);
            var closedTree = Tree(client);
            Assert.Null(closedTree.DashboardMembers);
            Assert.Null(closedTree.DashboardHighlighted);
            Assert.Null(closedTree.DashboardFontSize);
            Assert.Null(closedTree.DashboardFontMode);

            Assert.True(client.Send(new ControlRequest
            {
                Cmd = Command.Dashboard,
                Args = new ControlArgs { Targets = [sessionIds[0].ToString("D")] },
            }).Ok);
            var untouchedTree = Tree(client);
            Assert.Equal("untouched", untouchedTree.DashboardFontMode);
            Assert.Null(untouchedTree.DashboardFontSize);

            Assert.True(client.Send(new ControlRequest
            {
                Cmd = Command.Dashboard,
                Args = new ControlArgs
                {
                    Targets = [sessionIds[0].ToString("D"), sessionIds[1].ToString("D")],
                    AutoSize = true,
                },
            }).Ok);
            var autoTree = Tree(client);
            Assert.Equal("auto", autoTree.DashboardFontMode);
            Assert.Equal(11, autoTree.DashboardFontSize);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Named_pipe_mru_order_and_dashboard_open_clear_zoom()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-dashboard-mru-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var sessionIds = CreateSessions(host, stateDir, count: 3, split: false);
            host.Host.RunLocked(() =>
            {
                host.Host.Store.SelectSession(sessionIds[1]);
                host.Host.Store.SelectSession(sessionIds[2]);
                var zoom = host.Host.Actions.SetSurfaceZoom(
                    new TerminalSurfaceId(sessionIds[2], TerminalZoomSurface.Primary).RawValue,
                    window: null,
                    ControlToggleMode.On);
                Assert.True(zoom.Ok, zoom.Error);
            });
            host.Start();
            var client = new ControlPipeClient(host.PipeName);

            var response = client.Send(new ControlRequest
            {
                Cmd = Command.Dashboard,
                Args = new ControlArgs { Mru = true, AutoSize = true },
            });

            Assert.True(response.Ok, response.Error);
            var tree = Tree(client);
            Assert.Equal(
                [ControlRef(sessionIds[2], "left"), ControlRef(sessionIds[1], "left")],
                tree.DashboardMembers?.Take(2));
            Assert.Equal(tree.DashboardMembers?[0], tree.DashboardHighlighted);
            Assert.Null(tree.ZoomedSurface);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Dashboard_layout_serializes_the_exact_bridge_contract()
    {
        var scriptHost = new FakeScriptHost { ScriptResult = _ => "true" };
        var page = new XtermPage(scriptHost);
        var cells = new[]
        {
            new TerminalDashboardCell("first-pane", "A:left"),
            new TerminalDashboardCell("second-pane", "B:right"),
        };

        Assert.True(await page.ApplyLayoutAsync(
            new TerminalLayout.Dashboard(6, cells, "B:right", 10)));

        var script = Assert.Single(scriptHost.Scripts, item => item.Contains("agterm.applyLayout"));
        Assert.Contains(
            """{"type":"dashboard","generation":6,"cells":[{"paneId":"first-pane","controlRef":"A:left"},{"paneId":"second-pane","controlRef":"B:right"}],"highlightedControlRef":"B:right","fontSize":10}""",
            script);
    }

    private static LiveControlHost CreateHost(string stateDir, double baseFontSize = 14) => new(new LiveControlHostOptions
    {
        StateDir = stateDir,
        ConfigDir = stateDir,
        PipeName = "agterm-live-dashboard-" + Guid.NewGuid().ToString("N"),
        BaseFontSize = baseFontSize,
        OpenSurface = (request, _) => Task.FromResult<IHeadlessSurface>(
            new FakeSurface($"{request.Kind}-{request.SessionId:N}")),
    });

    private static string ControlRef(Guid sessionId, string surface) =>
        $"{sessionId.ToString("D").ToUpperInvariant()}:{surface}";

    private static IReadOnlyList<Guid> CreateSessions(
        LiveControlHost host,
        string cwd,
        int count,
        bool split)
    {
        var ids = new List<Guid>(count);
        host.Host.RunLocked(() =>
        {
            var workspace = Assert.Single(host.Host.Store.Workspaces);
            var first = Assert.IsType<Session>(host.Host.Store.ActiveSession);
            for (var index = 0; index < count; index++)
            {
                var session = index == 0
                    ? first
                    : host.Host.Store.AddSession(workspace.Id, cwd, select: false)!;
                session.Surface = new FakeSurface($"main-{index}");
                session.HasSplit = split;
                session.SplitSurface = split ? new FakeSurface($"split-{index}") : null;
                ids.Add(session.Id);
            }
        });
        return ids;
    }

    private static ControlTree Tree(ControlPipeClient client) =>
        Assert.IsType<ControlTree>(client.Send(new ControlRequest { Cmd = Command.Tree }).Result?.Tree);

    private static T ReadLocked<T>(LiveControlHost host, Func<T> read)
    {
        var result = default(T)!;
        host.Host.RunLocked(() => result = read());
        return result;
    }

    private static int AutoFollowSuppressionCount(AppStore store) =>
        (int)(typeof(AppStore).GetField(
            "AutoFollowSuppressionCount",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(store) ?? -1);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FakeSurface(string? paneToken) : IHeadlessSurface
    {
        public string? PaneToken => paneToken;
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => string.Empty;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }
}

[Collection("webview2")]
public sealed class LiveDashboardPageTests(WebView2Fixture webView)
{
    [Fact]
    public async Task Clicking_initially_highlighted_cell_activates_it()
    {
        var page = await webView.CreatePageAsync();
        await webView.RunAsync(_ => page.OpenTerminalAsync("initial-click"));
        var activated = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        page.DashboardActivated += controlRef => activated.TrySetResult(controlRef);
        Assert.True(await ApplyAsync(page, new TerminalLayout.Dashboard(
            1,
            [new TerminalDashboardCell("initial-click", "A:left")],
            "A:left")));

        await webView.RunAsync(_ => page.ExecuteScriptAsync(
            "document.querySelector('[data-dashboard-control-ref=\"A:left\"]').click()"));

        Assert.Equal("A:left", await activated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Dashboard_grid_uses_existing_panes_highlights_one_cell_and_restores_single()
    {
        var page = await webView.CreatePageAsync();
        foreach (var paneId in new[] { "dash-one", "dash-two", "dash-three" })
            await webView.RunAsync(_ => page.OpenTerminalAsync(paneId));
        var cells = new[]
        {
            new TerminalDashboardCell("dash-one", "A:left"),
            new TerminalDashboardCell("dash-two", "A:right"),
            new TerminalDashboardCell("dash-three", "B:left"),
        };

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Dashboard(1, cells, "A:right", 9)));

        var dashboard = await ReadDomAsync(page);
        Assert.Equal("grid", dashboard.ContainerDisplay);
        Assert.Equal(3, dashboard.VisibleCellCount);
        Assert.Equal(1, dashboard.HighlightedCellCount);
        Assert.Equal("A:right", dashboard.HighlightedControlRef);
        Assert.All(dashboard.PaneDisplays, display => Assert.Equal("block", display));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Single(2, "dash-one", "session")));

        var restored = await ReadDomAsync(page);
        Assert.Equal(0, restored.VisibleCellCount);
        Assert.Equal(0, restored.HighlightedCellCount);
        Assert.Equal(["block", "none", "none"], restored.PaneDisplays);
        Assert.Equal(3, restored.RegisteredPaneCount);
    }

    [Fact]
    public async Task Dashboard_font_is_temporary_and_untouched_mode_preserves_session_fonts()
    {
        var page = await webView.CreatePageAsync();
        await webView.RunAsync(_ => page.OpenTerminalAsync("font-one", fontSize: 17));
        await webView.RunAsync(_ => page.OpenTerminalAsync("font-two", fontSize: 21));
        var cells = new[]
        {
            new TerminalDashboardCell("font-one", "A:left"),
            new TerminalDashboardCell("font-two", "B:left"),
        };

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Dashboard(1, cells, "A:left", 9)));
        Assert.Equal(9, await FontSizeAsync(page, "font-one"));
        Assert.Equal(9, await FontSizeAsync(page, "font-two"));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Single(2, "font-one", "session")));
        Assert.Equal(17, await FontSizeAsync(page, "font-one"));
        Assert.Equal(21, await FontSizeAsync(page, "font-two"));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Dashboard(3, cells, "B:left")));
        Assert.Equal(17, await FontSizeAsync(page, "font-one"));
        Assert.Equal(21, await FontSizeAsync(page, "font-two"));
    }

    [Fact]
    public async Task Dashboard_keyboard_and_click_report_highlight_activation_and_close()
    {
        var page = await webView.CreatePageAsync();
        var terminalData = new EventRecorder<string>();
        foreach (var paneId in new[] { "nav-one", "nav-two", "nav-three" })
        {
            var terminal = await webView.RunAsync(_ => page.OpenTerminalAsync(paneId));
            terminal.DataReceived += terminalData.Add;
        }
        var cells = new[]
        {
            new TerminalDashboardCell("nav-one", "A:left"),
            new TerminalDashboardCell("nav-two", "A:right"),
            new TerminalDashboardCell("nav-three", "B:left"),
        };
        var highlights = new EventRecorder<string>();
        var activated = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var builtinHits = new EventRecorder<PhysicalChord>();
        page.DashboardHighlighted += highlights.Add;
        page.DashboardActivated += controlRef => activated.TrySetResult(controlRef);
        page.DashboardClosed += () => closed.TrySetResult();
        page.BuiltinHotkeyPressed += hit => builtinHits.Add(hit.Chord);

        await webView.RunAsync(async _ =>
        {
            Assert.True(await page.SetHotkeyManifestAsync(
                new HotkeyManifest(
                    1,
                    1500,
                    [
                        new PhysicalChord(true, false, false, "d"),
                        new PhysicalChord(true, false, true, "d"),
                    ],
                    []),
                dashboardToggleSingles: [new PhysicalChord(true, false, true, "d")]));
            return true;
        });

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Dashboard(1, cells, "A:left")));

        await webView.RunAsync(_ => page.ExecuteScriptAsync(
            "agterm.dispatchKey('nav-one', {key:'x'})"));
        await Task.Delay(150);
        Assert.Empty(terminalData);
        await webView.RunAsync(_ => page.ExecuteScriptAsync(
            "agterm.dispatchKey('nav-one', {key:'d',ctrlKey:true})"));
        await Task.Delay(150);
        Assert.Empty(builtinHits);
        await webView.RunAsync(_ => page.ExecuteScriptAsync(
            "agterm.dispatchKey('nav-one', {key:'d',ctrlKey:true,shiftKey:true})"));
        await WaitUntilAsync(() => builtinHits.Count == 1, TimeSpan.FromSeconds(5));
        Assert.Equal(new PhysicalChord(true, false, true, "d"), builtinHits[0]);

                var pointerTargetHits = await webView.RunAsync(_ => page.ExecuteScriptAsync(
                        """
                        (function() {
                            const pane = document.querySelector('[data-dashboard-control-ref="A:left"]');
                            window.dashboardPointerTargetHits = 0;
                            pane.addEventListener('pointerdown', () => window.dashboardPointerTargetHits++);
                            pane.dispatchEvent(new PointerEvent('pointerdown', {bubbles:true,cancelable:true,pointerId:7}));
                            return window.dashboardPointerTargetHits;
                        })()
                        """));
                Assert.Equal("0", pointerTargetHits);

        await DispatchDocumentKeyAsync(page, "ArrowRight");
        await WaitUntilAsync(() => highlights.Count >= 1, TimeSpan.FromSeconds(5));
        Assert.Equal("A:right", highlights[0]);
        Assert.Equal("A:right", (await ReadNavStateAsync(page)).HighlightedControlRef);

        await webView.RunAsync(_ => page.ExecuteScriptAsync(
            "document.querySelector('[data-dashboard-control-ref=\"B:left\"]').click()"));
        Assert.Equal("B:left", await activated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("B:left", highlights[^1]);

        await DispatchDocumentKeyAsync(page, "Escape");
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Stale_and_invalid_dashboard_payloads_do_not_replace_visible_grid()
    {
        var page = await webView.CreatePageAsync();
        await webView.RunAsync(_ => page.OpenTerminalAsync("reject-one"));
        await webView.RunAsync(_ => page.OpenTerminalAsync("reject-two"));
        Assert.True(await ApplyAsync(page, new TerminalLayout.Dashboard(
            2,
            [
                new TerminalDashboardCell("reject-one", "A:left"),
                new TerminalDashboardCell("reject-two", "B:left"),
            ],
            "A:left")));

        Assert.False(await ApplyAsync(page,
            new TerminalLayout.Single(1, "reject-one", "stale")));
        var invalidPayloads = new[]
        {
            "{type:'dashboard',generation:3,cells:[{paneId:'missing',controlRef:'A:left'}],highlightedControlRef:'A:left'}",
            "{type:'dashboard',generation:3,cells:[{paneId:'reject-one',controlRef:'A:left'}],highlightedControlRef:'B:left'}",
            "{type:'dashboard',generation:3,cells:[{paneId:'reject-one',controlRef:'A:left'},{paneId:'reject-one',controlRef:'B:left'}],highlightedControlRef:'A:left'}",
            "{type:'dashboard',generation:3,cells:[{paneId:'reject-one',controlRef:'A:left'}],highlightedControlRef:'A:left',fontSize:0}",
            "{type:'dashboard',generation:3,cells:[{paneId:'reject-one',controlRef:'A:left'}],highlightedControlRef:'A:left',extra:true}",
        };
        foreach (var payload in invalidPayloads)
        {
            var accepted = await webView.RunAsync(_ => page.ExecuteScriptAsync(
                $"agterm.applyLayout({payload})"));
            Assert.Equal("false", accepted);
        }

        var raw = await webView.RunAsync(_ => page.ExecuteScriptAsync(
            """
            (function() {
              return {
                cells: document.querySelectorAll('.dashboard-cell').length,
                highlighted: document.querySelector('.dashboard-highlighted')?.dataset.dashboardControlRef
              };
            })()
            """));
        var state = JsonSerializer.Deserialize<RejectedDashboardState>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal(2, state.Cells);
        Assert.Equal("A:left", state.Highlighted);
    }

    private Task<bool> ApplyAsync(XtermPage page, TerminalLayout layout) =>
        webView.RunAsync(_ => page.ApplyLayoutAsync(layout));

    private async Task<double> FontSizeAsync(XtermPage page, string paneId)
    {
        var raw = await webView.RunAsync(_ => page.ExecuteScriptAsync(
            $"agterm.getFontSize({XtermPage.PaneLiteral(paneId)})"));
        return JsonSerializer.Deserialize<double>(raw);
    }

    private Task DispatchDocumentKeyAsync(XtermPage page, string key) =>
        webView.RunAsync(_ => page.ExecuteScriptAsync(
            $"document.dispatchEvent(new KeyboardEvent('keydown', {{key:{XtermPage.PaneLiteral(key)},bubbles:true,cancelable:true}}))"));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition not met within the timeout.");
            await Task.Delay(25);
        }
    }

    private async Task<DashboardNavState> ReadNavStateAsync(XtermPage page)
    {
        var raw = await webView.RunAsync(_ => page.ExecuteScriptAsync(
            """
            (function() {
              const highlighted = document.querySelector('.dashboard-highlighted');
              return { highlightedControlRef: highlighted ? highlighted.dataset.dashboardControlRef : null };
            })()
            """));
        return JsonSerializer.Deserialize<DashboardNavState>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private async Task<DashboardDomState> ReadDomAsync(XtermPage page)
    {
        var raw = await webView.RunAsync(_ => page.ExecuteScriptAsync(
            """
            (function() {
              const ids = ['dash-one', 'dash-two', 'dash-three'];
              const panes = ids.map(id => document.querySelector(`[data-pane-id="${id}"]`));
              const highlighted = document.querySelector('.dashboard-highlighted');
              return {
                containerDisplay: getComputedStyle(document.getElementById('panes')).display,
                visibleCellCount: document.querySelectorAll('.dashboard-cell').length,
                highlightedCellCount: document.querySelectorAll('.dashboard-highlighted').length,
                highlightedControlRef: highlighted ? highlighted.dataset.dashboardControlRef : null,
                paneDisplays: panes.map(pane => pane ? getComputedStyle(pane).display : null),
                registeredPaneCount: ids.filter(id => agterm.text(id, true, null) !== null).length
              };
            })()
            """));
        return JsonSerializer.Deserialize<DashboardDomState>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private sealed record DashboardDomState(
        string ContainerDisplay,
        int VisibleCellCount,
        int HighlightedCellCount,
        string? HighlightedControlRef,
        string?[] PaneDisplays,
        int RegisteredPaneCount);

    private sealed record DashboardNavState(string? HighlightedControlRef);

    private sealed record RejectedDashboardState(int Cells, string? Highlighted);
}