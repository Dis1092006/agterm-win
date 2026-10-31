using AgTerm.ControlServer;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using System.Text.Json;

namespace AgTerm.Xterm.Tests;

public sealed class LiveSurfaceZoomE2ETests
{
    [Fact]
    public void Zoom_presentation_selects_one_existing_pane()
    {
        var snapshot = new LiveSelectionSnapshot(
            Guid.NewGuid(),
            "session",
            MainPaneId: "main-token",
            SplitPaneId: "split-token",
            IsSplit: true,
            SplitFocused: false,
            ZoomPaneId: "split-token");

        var zoomed = Assert.IsType<TerminalLayout.Zoomed>(snapshot.ToTerminalLayout(7));

        Assert.Equal("split-token", zoomed.PaneId);
        Assert.Equal(7, zoomed.Generation);
    }

    [Fact]
    public async Task Zoom_layout_serializes_the_exact_bridge_contract()
    {
        var scriptHost = new FakeScriptHost { ScriptResult = _ => "true" };
        var page = new XtermPage(scriptHost);

        Assert.True(await page.ApplyLayoutAsync(
            new TerminalLayout.Zoomed(9, "zoom-pane", "session", "workspace")));

        var script = Assert.Single(scriptHost.Scripts, item => item.Contains("agterm.applyLayout"));
        Assert.Contains(
            """{"type":"zoomed","generation":9,"paneId":"zoom-pane"}""",
            script);
        Assert.DoesNotContain("title", script);
        Assert.DoesNotContain("workspaceName", script);
    }

    [Theory]
    [InlineData("left", "main-token")]
    [InlineData("right", "split-token")]
    [InlineData("scratch", "scratch-token")]
    [InlineData("overlay", "overlay-token")]
    public async Task Zoom_transition_publishes_the_exact_existing_pane(
        string surface,
        string expectedPaneId)
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-zoom-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var sessionId = Guid.Empty;
            host.Host.RunLocked(() =>
            {
                var session = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                sessionId = session.Id;
                session.Surface = new FakeSurface("main-token");
                session.SplitSurface = new FakeSurface("split-token");
                session.ScratchSurface = new FakeSurface("scratch-token");
                session.OverlaySurface = new FakeSurface("overlay-token");
                session.HasSplit = true;
                session.OverlayActive = true;
                host.Host.Actions.Quick.Visible = true;
                host.Host.Actions.Quick.Surface = new FakeSurface("quick-token");
            });

            var published = new TaskCompletionSource<LiveSelectionSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
            {
                if (snapshot.ZoomPaneId == expectedPaneId)
                    published.TrySetResult(snapshot);
            };

            var target = surface == "quick"
                ? "quick"
                : new TerminalSurfaceId(
                    sessionId,
                    TerminalZoomSurfaces.FromControlName(surface)!.Value).RawValue;
            ControlResponse response = null!;
            host.Host.RunLocked(() =>
                response = host.Host.Actions.SetSurfaceZoom(
                    target,
                    window: null,
                    ControlToggleMode.On));

            Assert.True(response.Ok, response.Error);
            var snapshot = await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(expectedPaneId, snapshot.ZoomPaneId);
            Assert.True(snapshot.QuickVisible);
            Assert.True(ReadLocked(host, () => host.Host.Actions.Quick.Visible));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Zoom_transition_closes_an_open_dashboard_before_publishing_presentation()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-zoom-dashboard-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var sessionId = Guid.Empty;
            host.Host.RunLocked(() =>
            {
                var session = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                sessionId = session.Id;
                session.Surface = new FakeSurface("main-token");
                host.Host.Actions.Dashboard.Open(
                    [new DashboardMember(sessionId, TerminalZoomSurface.Primary)],
                    new DashboardFontMode.Untouched());
            });

            var published = new TaskCompletionSource<LiveSelectionSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
            {
                if (snapshot.ZoomPaneId == "main-token")
                    published.TrySetResult(snapshot);
            };

            host.Host.RunLocked(() =>
            {
                var response = host.Host.Actions.SetSurfaceZoom(
                    new TerminalSurfaceId(sessionId, TerminalZoomSurface.Primary).RawValue,
                    window: null,
                    ControlToggleMode.On);
                Assert.True(response.Ok, response.Error);
            });

            var snapshot = await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(snapshot.DashboardOpen);
            Assert.False(ReadLocked(host, () => host.Host.Actions.Dashboard.IsOpen));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Dashboard_open_clears_zoom_before_publishing_presentation()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-dashboard-zoom-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var sessionId = Guid.Empty;
            host.Host.RunLocked(() =>
            {
                var session = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                sessionId = session.Id;
                session.Surface = new FakeSurface("main-token");
                var zoom = host.Host.Actions.SetSurfaceZoom(
                    new TerminalSurfaceId(sessionId, TerminalZoomSurface.Primary).RawValue,
                    window: null,
                    ControlToggleMode.On);
                Assert.True(zoom.Ok, zoom.Error);
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
                    [sessionId.ToString("D")],
                    window: null,
                    close: false,
                    new DashboardFontMode.Untouched(),
                    mru: false);
                Assert.True(response.Ok, response.Error);
            });

            var snapshot = await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(snapshot.ZoomPaneId);
            Assert.True(snapshot.DashboardOpen);
            Assert.Null(ReadLocked(host, () => host.Host.Actions.Zoom.Target));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ToggleTerminalZoomAsync_uses_the_active_surface_and_publishes_it()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-zoom-toggle-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            host.Host.RunLocked(() =>
                Assert.IsType<Session>(host.Host.Store.ActiveSession).Surface =
                    new FakeSurface("main-token"));
            var published = new TaskCompletionSource<LiveSelectionSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
            {
                if (snapshot.ZoomPaneId == "main-token")
                    published.TrySetResult(snapshot);
            };

            await host.ToggleTerminalZoomAsync();

            var snapshot = await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("main-token", snapshot.ZoomPaneId);
            Assert.IsType<TerminalZoomTarget.Session>(
                ReadLocked(host, () => host.Host.Actions.Zoom.Target));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task ExitTerminalZoomAsync_clears_the_session_zoom()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-zoom-exit-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            host.Host.RunLocked(() =>
                Assert.IsType<Session>(host.Host.Store.ActiveSession).Surface =
                    new FakeSurface("main-token"));
            await host.ToggleTerminalZoomAsync();
            Assert.NotNull(ReadLocked(host, () => host.Host.Actions.Zoom.Target));

            await host.ExitTerminalZoomAsync();

            Assert.Null(ReadLocked(host, () => host.Host.Actions.Zoom.Target));
            await host.ExitTerminalZoomAsync();
            Assert.Null(ReadLocked(host, () => host.Host.Actions.Zoom.Target));
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Named_pipe_zoom_modes_tree_readback_and_errors_match_the_wire_contract()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-zoom-wire-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var sessionId = Guid.Empty;
            host.Host.RunLocked(() =>
            {
                var session = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                sessionId = session.Id;
                session.Surface = new FakeSurface("main-token");
                session.SplitSurface = null;
                session.HasSplit = false;
            });
            host.Start();
            var client = new ControlPipeClient(host.PipeName);
            var mainId = new TerminalSurfaceId(sessionId, TerminalZoomSurface.Primary).RawValue;

            var show = client.Send(new ControlRequest
            {
                Cmd = Command.SurfaceZoom,
                Target = mainId,
                Args = new ControlArgs { Mode = "show" },
            });
            Assert.True(show.Ok, show.Error);
            Assert.Equal(mainId, show.Result?.Id);
            Assert.Equal(mainId, ReadZoomedSurface(client));

            Assert.True(client.Send(new ControlRequest { Cmd = Command.SurfaceZoom }).Ok);
            Assert.Null(ReadZoomedSurface(client));
            Assert.True(client.Send(new ControlRequest { Cmd = Command.SurfaceZoom }).Ok);
            Assert.Equal(mainId, ReadZoomedSurface(client));

            var hide = client.Send(new ControlRequest
            {
                Cmd = Command.SurfaceZoom,
                Target = mainId,
                Args = new ControlArgs { Mode = "hide" },
            });
            Assert.True(hide.Ok, hide.Error);
            Assert.Null(ReadZoomedSurface(client));

            var invalid = client.Send(new ControlRequest
            {
                Cmd = Command.SurfaceZoom,
                Target = "not-a-surface",
                Args = new ControlArgs { Mode = "show" },
            });
            Assert.False(invalid.Ok);
            Assert.Equal("invalid surface: not-a-surface", invalid.Error);

            var missingId = new TerminalSurfaceId(Guid.Empty, TerminalZoomSurface.Primary).RawValue;
            var missing = client.Send(new ControlRequest
            {
                Cmd = Command.SurfaceZoom,
                Target = missingId,
                Args = new ControlArgs { Mode = "show" },
            });
            Assert.False(missing.Ok);
            Assert.Equal($"no such surface: {missingId}", missing.Error);

            var splitId = new TerminalSurfaceId(sessionId, TerminalZoomSurface.Split).RawValue;
            var unavailable = client.Send(new ControlRequest
            {
                Cmd = Command.SurfaceZoom,
                Target = splitId,
                Args = new ControlArgs { Mode = "show" },
            });
            Assert.False(unavailable.Ok);
            Assert.Equal($"surface not available: {splitId}", unavailable.Error);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Theory]
    [InlineData("quick")]
    [InlineData("session")]
    public void Zooming_a_new_target_ends_active_search_and_the_background_targets_search(string targetKind)
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-zoom-search-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            host.Host.RunLocked(() =>
            {
                var store = host.Store;
                var active = Assert.IsType<Session>(store.ActiveSession);
                var workspace = store.WorkspaceForSession(active.Id)!;
                var background = Assert.IsType<Session>(store.AddSession(workspace.Id, stateDir, select: false));
                var unrelated = Assert.IsType<Session>(store.AddSession(workspace.Id, stateDir, select: false));
                foreach (var session in new[] { active, background, unrelated })
                {
                    session.Surface = new FakeSurface(session.Id.ToString("N"));
                    Assert.True(store.OpenSearch(session.Id));
                    Assert.True(store.UpdateSearch(session.Id, session.Surface.PaneToken!, "needle", 2, 1));
                    Assert.True(session.SearchActive);
                    Assert.Same(session.Surface, session.SearchSurface);
                }
                host.Host.Actions.Quick.Visible = true;
                host.Host.Actions.Quick.Surface = new FakeSurface("quick-token");
                var target = targetKind == "quick"
                    ? "quick"
                    : new TerminalSurfaceId(background.Id, TerminalZoomSurface.Primary).RawValue;

                var response = host.Host.Actions.SetSurfaceZoom(target, null, ControlToggleMode.On);

                Assert.True(response.Ok, response.Error);
                Assert.Equal(active.Id, store.SelectedSessionId);
                Assert.Equal(targetKind == "quick", active.SearchActive);
                Assert.Equal(targetKind == "quick" ? active.Surface : null, active.SearchSurface);
                Assert.Equal(targetKind == "quick" ? "needle" : "", active.SearchNeedle);
                Assert.Equal(targetKind == "quick" ? 2 : (int?)null, active.SearchTotal);
                Assert.Equal(targetKind == "quick" ? 1 : (int?)null, active.SearchSelected);
                Assert.Equal(targetKind == "quick", background.SearchActive);
                Assert.Equal(targetKind == "quick" ? background.Surface : null, background.SearchSurface);
                Assert.True(unrelated.SearchActive);
                Assert.Same(unrelated.Surface, unrelated.SearchSurface);
                Assert.Equal("needle", unrelated.SearchNeedle);
            });
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Closing_the_zoomed_surface_clears_controller_hotkey_state_and_presentation()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-zoom-close-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var sessionId = Guid.Empty;
            host.Host.RunLocked(() =>
            {
                var session = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                sessionId = session.Id;
                session.ScratchActive = true;
                session.ScratchSurface = new FakeSurface("scratch-token");
                var response = host.Host.Actions.SetSurfaceZoom(
                    new TerminalSurfaceId(sessionId, TerminalZoomSurface.Scratch).RawValue,
                    window: null,
                    ControlToggleMode.On);
                Assert.True(response.Ok, response.Error);
            });
            Assert.True(host.ZoomActive);

            var published = new TaskCompletionSource<LiveSelectionSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
            {
                if (snapshot.ZoomPaneId is null)
                    published.TrySetResult(snapshot);
            };

            await host.StoreCoordinator.MutateAsync(store => store.CloseScratch(sessionId));

            var snapshot = await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(snapshot.ZoomPaneId);
            Assert.Null(ReadLocked(host, () => host.Host.Actions.Zoom.Target));
            Assert.False(host.ZoomActive);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("scratch")]
    [InlineData("overlay")]
    [InlineData("quick")]
    public void Session_zoom_requires_a_realized_pane_but_panel_zoom_can_precede_realization(string surface)
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-live-zoom-unrealized-").FullName;
        try
        {
            using var host = CreateHost(stateDir);
            var target = "quick";
            host.Host.RunLocked(() =>
            {
                var session = Assert.IsType<Session>(host.Host.Store.ActiveSession);
                session.Surface = surface == "left" ? null : session.Surface;
                session.HasSplit = surface == "right";
                session.SplitSurface = null;
                session.ScratchActive = surface == "scratch";
                session.ScratchSurface = null;
                session.OverlayActive = surface == "overlay";
                session.OverlaySurface = null;
                host.Host.Actions.Quick.Visible = surface == "quick";
                host.Host.Actions.Quick.Surface = null;
                if (surface != "quick")
                {
                    target = new TerminalSurfaceId(
                        session.Id,
                        TerminalZoomSurfaces.FromControlName(surface)!.Value).RawValue;
                }
            });

            ControlResponse response = null!;
            host.Host.RunLocked(() =>
                response = host.Host.Actions.SetSurfaceZoom(
                    target,
                    window: null,
                    ControlToggleMode.On));

            Assert.Equal(surface == "quick", response.Ok);
            Assert.Equal(surface == "quick" ? null : $"surface not available: {target}", response.Error);
            Assert.Equal(surface == "quick", host.Host.Actions.Quick.IsZoomed);
            Assert.Null(ReadLocked(host, () => host.Host.Actions.Zoom.Target));
            Assert.False(host.ZoomActive);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void MainWindow_routes_zoom_action_and_layout()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("[BuiltinAction.ToggleTerminalZoom] = Async(", source, StringComparison.Ordinal);
        Assert.Contains("host.ToggleTerminalZoomAsync()", source, StringComparison.Ordinal);
        Assert.Contains("case TerminalLayout.Zoomed zoomed:", source, StringComparison.Ordinal);
    }

    private static LiveControlHost CreateHost(string stateDir) => new(new LiveControlHostOptions
    {
        StateDir = stateDir,
        ConfigDir = stateDir,
        PipeName = "agterm-live-zoom-" + Guid.NewGuid().ToString("N"),
        OpenSurface = (request, _) => Task.FromResult<IHeadlessSurface>(
            new FakeSurface(request.Kind + "-bootstrap")),
    });

    private static T ReadLocked<T>(LiveControlHost host, Func<T> read)
    {
        var result = default(T)!;
        host.Host.RunLocked(() => result = read());
        return result;
    }

    private static string? ReadZoomedSurface(ControlPipeClient client) =>
        client.Send(new ControlRequest { Cmd = Command.Tree }).Result?.Tree?.ZoomedSurface;

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FakeSurface(string paneToken) : IHeadlessSurface
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
public sealed class LiveSurfaceZoomPageTests(WebView2Fixture webView)
{
    [Fact]
    public async Task Zoom_shows_one_existing_pane_and_split_restore_reuses_both_panes()
    {
        var page = await webView.CreatePageAsync();
        await webView.RunAsync(_ => page.OpenTerminalAsync("zoom-left"));
        await webView.RunAsync(_ => page.OpenTerminalAsync("zoom-right"));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(1, "zoom-left", "zoom-right", 0.4, "zoom-left")));
        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Zoomed(2, "zoom-right", "session", "workspace")));

        var zoomed = await ReadDomAsync(page);
        Assert.Equal("none", zoomed.LeftDisplay);
        Assert.Equal("block", zoomed.RightDisplay);
        Assert.True(zoomed.LeftRegistered);
        Assert.True(zoomed.RightRegistered);
        Assert.Equal(0, zoomed.DividerCount);

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Split(3, "zoom-left", "zoom-right", 0.4, "zoom-left")));

        var restored = await ReadDomAsync(page);
        Assert.Equal("block", restored.LeftDisplay);
        Assert.Equal("block", restored.RightDisplay);
        Assert.True(restored.LeftRegistered);
        Assert.True(restored.RightRegistered);
        Assert.Equal(1, restored.DividerCount);
    }

    [Fact]
    public async Task Accepted_zoom_layout_releases_palette_chords_without_waiting_for_manifest_refresh()
    {
        var page = await webView.CreatePageAsync();
        var terminal = await webView.RunAsync(_ => page.OpenTerminalAsync("zoom-hotkeys"));
        var paletteChord = new PhysicalChord(true, false, false, "p");
        var builtinHits = 0;
        var terminalData = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        page.BuiltinHotkeyPressed += _ => Interlocked.Increment(ref builtinHits);
        terminal.DataReceived += data => terminalData.TrySetResult(data);

        await webView.RunAsync(async _ =>
        {
            Assert.True(await page.SetHotkeyManifestAsync(
                new HotkeyManifest(1, 1500, [paletteChord], []),
                zoomPassthroughSingles: [paletteChord]));
            Assert.True(await page.ApplyLayoutAsync(
                new TerminalLayout.Single(1, "zoom-hotkeys", "session")));
            return true;
        });
        await DispatchAsync(page, "p", ctrl: true);
        await WaitUntilAsync(() => Volatile.Read(ref builtinHits) == 1, TimeSpan.FromSeconds(5));

        Assert.True(await ApplyAsync(page,
            new TerminalLayout.Zoomed(2, "zoom-hotkeys", "session")));
        await DispatchAsync(page, "p", ctrl: true);

        Assert.NotEmpty(await terminalData.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref builtinHits));
    }

    private Task<bool> ApplyAsync(XtermPage page, TerminalLayout layout) =>
        webView.RunAsync(_ => page.ApplyLayoutAsync(layout));

    private Task DispatchAsync(XtermPage page, string key, bool ctrl = false) =>
        webView.RunAsync(_ => page.ExecuteScriptAsync(
            $"agterm.dispatchKey('zoom-hotkeys', {{key:'{key}',ctrlKey:{ctrl.ToString().ToLowerInvariant()}}})"));

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

    private async Task<ZoomDomState> ReadDomAsync(XtermPage page)
    {
        var raw = await webView.RunAsync(_ => page.ExecuteScriptAsync(
            """
            (function() {
              const container = document.getElementById('panes');
              const left = document.querySelector('[data-pane-id="zoom-left"]');
              const right = document.querySelector('[data-pane-id="zoom-right"]');
              return {
                leftDisplay: left ? getComputedStyle(left).display : null,
                rightDisplay: right ? getComputedStyle(right).display : null,
                leftRegistered: agterm.text('zoom-left', true, null) !== null,
                rightRegistered: agterm.text('zoom-right', true, null) !== null,
                dividerCount: container.querySelectorAll('.split-divider').length
              };
            })()
            """));
        return JsonSerializer.Deserialize<ZoomDomState>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private sealed record ZoomDomState(
        string? LeftDisplay,
        string? RightDisplay,
        bool LeftRegistered,
        bool RightRegistered,
        int DividerCount);
}