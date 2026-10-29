using System.Text.Json.Serialization;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class LiveWindowRegistryTests : IDisposable
{
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-window-registry-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Enabling_active_window_sidebars_applies_immediately_and_persists_each_changed_window()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(secondId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication();
        application.GetOpenWindow(secondId).Store.SetSidebarVisible(false);

        await application.Settings.UpdateCurrentAsync(settings =>
            settings with { AutoHideSidebarInactiveWindows = true });

        Assert.False(application.GetOpenWindow(firstId).Store.SidebarVisible);
        Assert.True(application.GetOpenWindow(secondId).Store.SidebarVisible);
        Assert.False(new PersistenceStore(Path.Combine(_stateDir, "windows"), $"{firstId:D}.json").Load().SidebarVisible);
        Assert.True(new PersistenceStore(Path.Combine(_stateDir, "windows"), $"{secondId:D}.json").Load().SidebarVisible);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Default_or_disabled_policy_keeps_manual_visibility_on_activation(bool? enabled)
    {
        new SettingsStore(_stateDir).Save(new AppSettings { AutoHideSidebarInactiveWindows = enabled });
        using var application = CreateApplication();
        var id = application.ActiveWindowId!.Value;
        application.GetOpenWindow(id).Store.SetSidebarVisible(false);
        await application.ReportActivatedAsync(id);
        Assert.False(application.GetOpenWindow(id).Store.SidebarVisible);
    }

    [Fact]
    public async Task Every_activation_reconciles_even_the_same_restored_frontmost_and_a_single_window()
    {
        new SettingsStore(_stateDir).Save(new AppSettings { AutoHideSidebarInactiveWindows = true });
        using var application = CreateApplication();
        var id = application.ActiveWindowId!.Value;
        var store = application.GetOpenWindow(id).Store;
        store.SetSidebarVisible(false);

        await application.ReportActivatedAsync(id);

        Assert.True(store.SidebarVisible);
        var changes = 0;
        store.SidebarVisibilityChanged += () => changes++;
        await application.ReportActivatedAsync(id);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Switching_windows_reverses_visibility_but_disabling_does_not_restore_it()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        new SettingsStore(_stateDir).Save(new AppSettings { AutoHideSidebarInactiveWindows = true });
        using var application = CreateApplication();

        await application.ReportActivatedAsync(secondId);
        Assert.False(application.GetOpenWindow(firstId).Store.SidebarVisible);
        Assert.True(application.GetOpenWindow(secondId).Store.SidebarVisible);
        await application.ReportActivatedAsync(firstId);
        Assert.True(application.GetOpenWindow(firstId).Store.SidebarVisible);
        Assert.False(application.GetOpenWindow(secondId).Store.SidebarVisible);

        await application.Settings.UpdateCurrentAsync(settings =>
            settings with { AutoHideSidebarInactiveWindows = null });
        await application.ReportActivatedAsync(secondId);
        Assert.True(application.GetOpenWindow(firstId).Store.SidebarVisible);
        Assert.False(application.GetOpenWindow(secondId).Store.SidebarVisible);
    }

    [Fact]
    public async Task Unrelated_settings_edits_leave_manual_visibility_until_the_next_activation()
    {
        using var application = CreateApplication();
        await application.Settings.UpdateCurrentAsync(settings =>
            settings with { AutoHideSidebarInactiveWindows = true });
        var id = application.ActiveWindowId!.Value;
        var store = application.GetOpenWindow(id).Store;
        store.SetSidebarVisible(false);

        await application.Settings.UpdateCurrentAsync(settings => settings with { FontSize = 18 });

        Assert.False(store.SidebarVisible);
        await application.ReportActivatedAsync(id);
        Assert.True(store.SidebarVisible);
    }

    [Fact]
    public async Task Concurrent_activation_callbacks_leave_only_the_latest_frontmost_sidebar_visible()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication();
        await application.Settings.UpdateCurrentAsync(settings =>
            settings with { AutoHideSidebarInactiveWindows = true });

        var pending = Enumerable.Range(0, 20)
            .Select(index => application.ReportActivatedAsync(index % 2 == 0 ? firstId : secondId))
            .ToArray();
        await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(secondId, application.ActiveWindowId);
        Assert.False(application.GetOpenWindow(firstId).Store.SidebarVisible);
        Assert.True(application.GetOpenWindow(secondId).Store.SidebarVisible);
        application.GetOpenWindow(secondId).Store.SetSidebarVisible(false);
        await application.Settings.ReloadAsync();
        Assert.True(application.GetOpenWindow(secondId).Store.SidebarVisible);
    }

    [Fact]
    public async Task Closing_a_window_and_ignoring_its_late_activation_does_not_touch_the_remaining_sidebar()
    {
        using var application = CreateApplication();
        var id = application.ActiveWindowId!.Value;
        await application.Settings.UpdateCurrentAsync(settings =>
            settings with { AutoHideSidebarInactiveWindows = true });
        await application.CloseWindowAsync(id);
        await application.ReportActivatedAsync(id);
        Assert.Null(application.ActiveWindowId);
    }

    [Fact]
    public void Fresh_state_seeds_one_open_active_window_and_persists_separate_files()
    {
        using var application = new LiveApplicationHost(new LiveApplicationHostOptions
        {
            StateDir = _stateDir,
            HomeDirectory = _stateDir,
            PipeName = "agterm-window-registry-" + Guid.NewGuid().ToString("N"),
        });

        var window = Assert.Single(application.Windows);
        Assert.Equal("window 1", window.Name);
        Assert.True(window.IsOpen);
        Assert.Equal(window.Id, application.ActiveWindowId);

        var host = application.GetOpenWindow(window.Id);
        var workspace = Assert.Single(host.Store.Workspaces);
        Assert.Single(workspace.Sessions);
        Assert.Equal(_stateDir, workspace.Sessions[0].InitialCwd);

        Assert.True(File.Exists(Path.Combine(_stateDir, "windows.json")));
        Assert.True(File.Exists(Path.Combine(
            _stateDir,
            "windows",
            $"{window.Id:D}.json")));
    }

    [Fact]
    public void Persisted_open_windows_restore_distinct_stores_with_one_shared_ring()
    {
        var firstId = Guid.Parse("0A11AAAA-0000-0000-0000-000000000011");
        var secondId = Guid.Parse("7B33CCCC-0000-0000-0000-000000000013");
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(secondId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = new LiveApplicationHost(new LiveApplicationHostOptions
        {
            StateDir = _stateDir,
            HomeDirectory = _stateDir,
            PipeName = "agterm-window-registry-" + Guid.NewGuid().ToString("N"),
        });

        Assert.Equal(new[] { firstId, secondId }, application.Windows.Select(window => window.Id));
        Assert.Equal(secondId, application.ActiveWindowId);
        var first = application.GetOpenWindow(firstId);
        var second = application.GetOpenWindow(secondId);
        Assert.NotSame(first.Store, second.Store);
        Assert.Equal("alpha", Assert.Single(first.Store.Workspaces).Name);
        Assert.Equal("beta", Assert.Single(second.Store.Workspaces).Name);
        Assert.Same(application.Ring, first.Ring);
        Assert.Same(application.Ring, second.Ring);
    }

    [Fact]
    public async Task Closed_frontmost_falls_back_and_explicit_activation_persists()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(secondId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: false));

        using (var application = CreateApplication())
        {
            Assert.Equal(secondId, application.FrontmostWindowId);
            Assert.Equal(firstId, application.ActiveWindowId);

            await application.ReportActivatedAsync(firstId);

            Assert.Equal(firstId, application.FrontmostWindowId);
            Assert.Equal(firstId, application.ActiveWindowId);
        }

        using var reloaded = CreateApplication();
        Assert.Equal(firstId, reloaded.FrontmostWindowId);
        Assert.Equal(firstId, reloaded.ActiveWindowId);
    }

    [Fact]
    public void All_closed_index_reopens_the_known_frontmost_window()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(secondId,
            new IndexEntry(firstId, "first", IsOpen: false),
            new IndexEntry(secondId, "second", IsOpen: false));

        using var application = CreateApplication();

        Assert.Equal(secondId, application.ActiveWindowId);
        Assert.False(application.Windows.Single(window => window.Id == firstId).IsOpen);
        Assert.True(application.Windows.Single(window => window.Id == secondId).IsOpen);
        Assert.Same(application.GetOpenWindow(secondId).Store, application.GetOpenWindow(secondId).Store);
    }

    [Fact]
    public void Invalid_index_recovers_uuid_snapshots_in_filename_order_before_legacy()
    {
        var firstId = Guid.Parse("0A11AAAA-0000-0000-0000-000000000011");
        var secondId = Guid.Parse("7B33CCCC-0000-0000-0000-000000000013");
        WriteWindowSnapshot(secondId, "beta");
        WriteWindowSnapshot(firstId, "alpha");
        new PersistenceStore(_stateDir).Save(SnapshotWithWorkspace("legacy"));
        File.WriteAllText(Path.Combine(_stateDir, "windows.json"), "not json");
        File.WriteAllText(Path.Combine(_stateDir, "windows", "not-a-uuid.json"), "{}");

        using var application = CreateApplication();

        Assert.Equal(new[] { firstId, secondId }, application.Windows.Select(window => window.Id));
        Assert.Equal(new[] { "window 1", "window 2" }, application.Windows.Select(window => window.Name));
        Assert.All(application.Windows, window => Assert.True(window.IsOpen));
        Assert.Equal(firstId, application.FrontmostWindowId);
        Assert.Equal("alpha", Assert.Single(application.GetOpenWindow(firstId).Store.Workspaces).Name);
        Assert.Equal("beta", Assert.Single(application.GetOpenWindow(secondId).Store.Workspaces).Name);
    }

    [Fact]
    public void Missing_index_migrates_nonempty_legacy_snapshot_and_keeps_source_file()
    {
        var legacyPath = Path.Combine(_stateDir, "workspaces.json");
        new PersistenceStore(_stateDir).Save(SnapshotWithWorkspace("legacy"));

        using var application = CreateApplication();

        var window = Assert.Single(application.Windows);
        Assert.Equal("window 1", window.Name);
        Assert.True(window.IsOpen);
        Assert.Equal(window.Id, application.FrontmostWindowId);
        Assert.Equal("legacy", Assert.Single(application.GetOpenWindow(window.Id).Store.Workspaces).Name);
        Assert.True(File.Exists(legacyPath));
        Assert.True(File.Exists(Path.Combine(_stateDir, "windows", $"{window.Id:D}.json")));
    }

    [Fact]
    public async Task Normal_close_keeps_metadata_and_persists_the_closed_open_set()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(secondId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using (var application = CreateApplication())
        {
            await application.CloseWindowAsync(secondId);

            Assert.Equal(firstId, application.ActiveWindowId);
            Assert.False(application.Windows.Single(window => window.Id == secondId).IsOpen);
            Assert.Throws<KeyNotFoundException>(() => application.GetOpenWindow(secondId));
        }

        using var reloaded = CreateApplication();
        Assert.True(reloaded.Windows.Single(window => window.Id == firstId).IsOpen);
        Assert.False(reloaded.Windows.Single(window => window.Id == secondId).IsOpen);
        Assert.Equal(firstId, reloaded.ActiveWindowId);
    }

    [Fact]
    public void Application_dispose_preserves_every_open_window_for_restart()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        CreateApplication().Dispose();

        using var reloaded = CreateApplication();
        Assert.All(reloaded.Windows, window => Assert.True(window.IsOpen));
        Assert.NotSame(
            reloaded.GetOpenWindow(firstId).Store,
            reloaded.GetOpenWindow(secondId).Store);
    }

    [Fact]
    public void One_pipe_routes_explicit_window_requests_to_independent_stores()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = CreateApplication();
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var firstResponse = client.Send(new ControlRequest
        {
            Cmd = Command.WorkspaceNew,
            Args = new ControlArgs
            {
                Window = firstId.ToString("D"),
                Name = "first-only",
            },
        });
        Assert.True(firstResponse.Ok, firstResponse.Error);
        Assert.Equal(2, application.GetOpenWindow(firstId).Store.Workspaces.Count);
        Assert.Single(application.GetOpenWindow(secondId).Store.Workspaces);

        var secondResponse = client.Send(new ControlRequest
        {
            Cmd = Command.WorkspaceNew,
            Args = new ControlArgs
            {
                Window = secondId.ToString("D"),
                Name = "second-only",
            },
        });
        Assert.True(secondResponse.Ok, secondResponse.Error);
        Assert.Equal("first-only", application.GetOpenWindow(firstId).Store.Workspaces[^1].Name);
        Assert.Equal("second-only", application.GetOpenWindow(secondId).Store.Workspaces[^1].Name);
    }

    [Fact]
    public void One_pipe_resolves_an_explicit_session_target_across_open_windows()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, SnapshotWithSession("alpha", firstSessionId));
        WriteWindowSnapshot(secondId, SnapshotWithSession("beta", secondSessionId));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = CreateApplication();
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.SessionRename,
            Target = secondSessionId.ToString("D"),
            Args = new ControlArgs { Name = "second-session" },
        });

        Assert.True(response.Ok, response.Error);
        Assert.Null(application.GetOpenWindow(firstId).Store.SessionWithId(secondSessionId));
        Assert.Equal(
            "second-session",
            application.GetOpenWindow(secondId).Store.SessionWithId(secondSessionId)?.CustomName);
    }

    [Fact]
    public void Attaching_an_open_window_reuses_registry_identity_store_and_ring_once()
    {
        var windowId = Guid.NewGuid();
        WriteWindowSnapshot(windowId, "alpha");
        WriteIndex(windowId, new IndexEntry(windowId, "first", IsOpen: true));

        using var application = CreateApplication();
        using var attached = application.AttachWindow(windowId, new LiveControlHostOptions
        {
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
        });

        Assert.Equal(windowId, attached.WindowId);
        Assert.Same(application.GetOpenWindow(windowId).Store, attached.Store);
        Assert.Same(application.Ring, attached.Ring);
        Assert.Throws<InvalidOperationException>(() =>
            application.AttachWindow(windowId, new LiveControlHostOptions
            {
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
            }));
    }

    [Fact]
    public void Application_host_advertises_its_socket_path_to_attached_windows()
    {
        using var application = new LiveApplicationHost(new LiveApplicationHostOptions
        {
            StateDir = _stateDir,
            HomeDirectory = _stateDir,
        });
        var socketPath = Path.Combine(_stateDir, "agterm.sock");

        using var attached = application.AttachWindow(application.ActiveWindowId!.Value, new LiveControlHostOptions
        {
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
        });

        Assert.Equal(socketPath, application.SocketPath);
        Assert.Equal(application.PipeName, ControlPipeName.Derive(socketPath));
        Assert.Equal(socketPath, attached.AdvertisedSocketPath);
    }

    [Fact]
    public void Application_host_with_a_literal_pipe_name_advertises_nothing()
    {
        using var application = new LiveApplicationHost(new LiveApplicationHostOptions
        {
            StateDir = _stateDir,
            HomeDirectory = _stateDir,
            PipeName = "agterm-literal-" + Guid.NewGuid().ToString("N"),
        });

        using var attached = application.AttachWindow(application.ActiveWindowId!.Value, new LiveControlHostOptions
        {
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
        });

        Assert.Null(application.SocketPath);
        Assert.Null(attached.AdvertisedSocketPath);
    }

    [Fact]
    public void Winui_composition_uses_one_application_host_and_reports_bound_window_activation()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "App.xaml.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Contains("new LiveApplicationHost", app, StringComparison.Ordinal);
        Assert.Contains("foreach (var windowId in applicationHost.LaunchWindowIds)", app, StringComparison.Ordinal);
        Assert.Contains("new MainWindow(applicationHost, windowId)", app, StringComparison.Ordinal);
        Assert.Contains("applicationHost.Start();", app, StringComparison.Ordinal);
        Assert.DoesNotContain("new LiveControlHost(", mainWindow, StringComparison.Ordinal);
        Assert.Contains(
            "public MainWindow(LiveApplicationHost applicationHost, Guid windowId)",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains("_applicationHost.AttachWindow(", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_windowId,", mainWindow, StringComparison.Ordinal);
        Assert.Contains("Activated += OnWindowActivated;", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ObserveTaskFault(_applicationHost.ReportActivatedAsync(_windowId), \"Window activation failed\");",
            mainWindow, StringComparison.Ordinal);
        Assert.Contains("_applicationHost.ReportAcceptedPane(_windowId, paneId);", mainWindow, StringComparison.Ordinal);
        Assert.Contains(
            "await _applicationHost.CloseWindowAsync(_windowId).ConfigureAwait(false);",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "await host.ShutdownAsync().ConfigureAwait(false);",
            mainWindow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Two_attached_windows_realize_independent_surfaces_with_one_application_ring()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, SnapshotWithSession("alpha", firstSessionId));
        WriteWindowSnapshot(secondId, SnapshotWithSession("beta", secondSessionId));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        var firstSurfaces = new List<IHeadlessSurface>();
        var secondSurfaces = new List<IHeadlessSurface>();

        using var application = CreateApplication();
        using var first = application.AttachWindow(firstId, new LiveControlHostOptions
        {
            OpenSurface = (_, _) =>
            {
                IHeadlessSurface surface = new FakeSurface("first-pane");
                firstSurfaces.Add(surface);
                return Task.FromResult(surface);
            },
        });
        using var second = application.AttachWindow(secondId, new LiveControlHostOptions
        {
            OpenSurface = (_, _) =>
            {
                IHeadlessSurface surface = new FakeSurface("second-pane");
                secondSurfaces.Add(surface);
                return Task.FromResult(surface);
            },
        });

        Assert.Single(firstSurfaces);
        Assert.Single(secondSurfaces);
        Assert.NotSame(first.StoreCoordinator, second.StoreCoordinator);
        Assert.NotSame(first.Store, second.Store);
        Assert.Same(application.Ring, first.Ring);
        Assert.Same(application.Ring, second.Ring);
        Assert.Same(firstSurfaces[0], first.Store.SessionWithId(firstSessionId)?.Surface);
        Assert.Same(secondSurfaces[0], second.Store.SessionWithId(secondSessionId)?.Surface);
    }

    [Fact]
    public void TreeChangesCoalescePerWindowAndStayIndependent()
    {
        // Ported from upstream AppStoreEventTests.swift treeChangesCoalescePerWindowAndStayIndependent.
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        var clock = new TreeEventClock();
        using var application = CreateApplication(clock);
        var anchor = application.Ring.Read(cursor: null).Batch;

        foreach (var windowId in new[] { firstId, secondId })
        {
            var window = application.GetOpenWindow(windowId);
            window.RunLocked(() =>
            {
                window.Store.AddWorkspace("one");
                window.Store.AddWorkspace("two");
            });
        }

        Assert.Empty(application.Ring.Read(
            new ControlEventCursor(anchor.Run, anchor.Next)).Batch.Items);
        application.FlushTreeEvents();
        var events = application.Ring.Read(new ControlEventCursor(anchor.Run, anchor.Next)).Batch;
        Assert.Equal(new[] { firstId, secondId }.Select(id => id.ToString("D").ToUpperInvariant()),
            events.Items.Select(item => item.Window));
        Assert.All(events.Items, item =>
        {
            Assert.Equal(ControlEventKind.TreeChanged, item.Kind);
            Assert.Null(item.Workspace);
            Assert.Null(item.Session);
        });
        application.FlushTreeEvents();
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.FireQueuedCallbacks();
        Assert.Empty(application.Ring.Read(new ControlEventCursor(events.Run, events.Next)).Batch.Items);
    }

    [Fact]
    public void Tree_events_wait_100_ms_and_only_reset_the_changed_windows_deadline()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        var clock = new TreeEventClock();
        using var application = CreateApplication(clock);
        var anchor = application.Ring.Read(cursor: null).Batch;
        application.GetOpenWindow(firstId).Store.AddWorkspace("one");
        clock.Advance(TimeSpan.FromMilliseconds(50));
        application.GetOpenWindow(secondId).Store.AddWorkspace("two");
        clock.Advance(TimeSpan.FromMilliseconds(25));
        application.GetOpenWindow(firstId).Store.AddWorkspace("three");

        clock.FireQueuedCallbacks();
        clock.Advance(TimeSpan.FromMilliseconds(74));
        Assert.Empty(application.Ring.Read(new ControlEventCursor(anchor.Run, anchor.Next)).Batch.Items);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var second = Assert.Single(application.Ring.Read(
            new ControlEventCursor(anchor.Run, anchor.Next)).Batch.Items);
        Assert.Equal(secondId.ToString("D").ToUpperInvariant(), second.Window);
        clock.Advance(TimeSpan.FromMilliseconds(25));
        var first = Assert.Single(application.Ring.Read(
            new ControlEventCursor(anchor.Run, second.Seq)).Batch.Items);
        Assert.Equal(firstId.ToString("D").ToUpperInvariant(), first.Window);
        Assert.Equal(second.Seq + 1, first.Seq);
    }

    [Fact]
    public void Non_tree_events_are_immediate_and_status_and_selection_do_not_invalidate_the_tree()
    {
        // Ported from upstream AppStoreEventTests.swift statusAndSelectionDoNotScheduleStructuralInvalidation.
        var clock = new TreeEventClock();
        using var application = CreateApplication(clock);
        var window = application.GetOpenWindow(application.ActiveWindowId!.Value);
        var anchor = application.Ring.Read(cursor: null).Batch;
        var workspace = window.Store.Workspaces[0];
        var session = window.Store.AddSession(workspace.Id, _stateDir)!;
        window.Store.SetAgentIndicator(new AgentIndicator(AgentStatus.Active), session.Id);
        window.Store.RecordNotificationEvent(session.Id, "done", "ok");
        var immediate = application.Ring.Read(new ControlEventCursor(anchor.Run, anchor.Next)).Batch;
        Assert.Equal(new[] { ControlEventKind.SessionCreated, ControlEventKind.Status, ControlEventKind.Notify },
            immediate.Items.Select(item => item.Kind));
        application.FlushTreeEvents();
        var tree = Assert.Single(application.Ring.Read(
            new ControlEventCursor(immediate.Run, immediate.Next)).Batch.Items);
        Assert.Equal(ControlEventKind.TreeChanged, tree.Kind);
        Assert.Equal(immediate.Next + 1, tree.Seq);

        window.Store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked), session.Id);
        window.Store.SelectSession(session.Id);
        application.FlushTreeEvents();
        clock.Advance(TimeSpan.FromSeconds(1));
        var status = Assert.Single(application.Ring.Read(
            new ControlEventCursor(immediate.Run, tree.Seq)).Batch.Items);
        Assert.Equal(ControlEventKind.Status, status.Kind);
    }

    [Fact]
    public void Attached_live_window_coalesces_two_session_renames_over_the_application_pipe()
    {
        var clock = new TreeEventClock();
        using var application = CreateApplication(clock);
        var windowId = application.ActiveWindowId!.Value;
        using var attached = application.AttachWindow(windowId, new LiveControlHostOptions
        {
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
        });
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var anchor = application.Ring.Read(cursor: null).Batch;
        foreach (var name in new[] { "one", "two" })
        {
            var response = client.Send(new ControlRequest
            {
                Cmd = Command.SessionRename,
                Args = new ControlArgs { Name = name, Window = windowId.ToString("D") },
            });
            Assert.True(response.Ok, response.Error);
        }
        Assert.Same(application.Ring, attached.Ring);
        Assert.Same(application.GetOpenWindow(windowId).Actions, attached.Actions);
        Assert.Empty(application.Ring.Read(new ControlEventCursor(anchor.Run, anchor.Next)).Batch.Items);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        var events = application.Ring.Read(new ControlEventCursor(anchor.Run, anchor.Next)).Batch;
        var changed = Assert.Single(events.Items);
        Assert.Equal(ControlEventKind.TreeChanged, changed.Kind);
        Assert.Equal(windowId.ToString("D").ToUpperInvariant(), changed.Window);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Empty(application.Ring.Read(new ControlEventCursor(events.Run, events.Next)).Batch.Items);
    }

    [Fact]
    public async Task Closing_one_window_flushes_its_pending_event_without_flushing_another_window()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        var clock = new TreeEventClock();
        using var application = CreateApplication(clock);
        var anchor = application.Ring.Read(cursor: null).Batch;
        application.GetOpenWindow(firstId).Store.AddWorkspace("one");
        application.GetOpenWindow(secondId).Store.AddWorkspace("two");

        await application.CloseWindowAsync(firstId);

        var closed = application.Ring.Read(new ControlEventCursor(anchor.Run, anchor.Next)).Batch;
        Assert.Equal(firstId.ToString("D").ToUpperInvariant(), Assert.Single(closed.Items).Window);
        application.Dispose();
        var shutdown = application.Ring.Read(new ControlEventCursor(closed.Run, closed.Next)).Batch;
        Assert.Equal(secondId.ToString("D").ToUpperInvariant(), Assert.Single(shutdown.Items).Window);
        clock.Advance(TimeSpan.FromSeconds(1));
        clock.FireQueuedCallbacks();
        Assert.Empty(application.Ring.Read(new ControlEventCursor(shutdown.Run, shutdown.Next)).Batch.Items);
    }

    [Fact]
    public void Events_read_returns_events_from_both_windows_in_one_application_run()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = CreateApplication();
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var anchor = client.Send(new ControlRequest { Cmd = Command.EventsRead });
        Assert.True(anchor.Ok, anchor.Error);
        var run = anchor.Result!.Events!.Run;
        var after = anchor.Result.Events.Next;

        foreach (var windowId in new[] { firstId, secondId })
        {
            var response = client.Send(new ControlRequest
            {
                Cmd = Command.WorkspaceNew,
                Args = new ControlArgs
                {
                    Window = windowId.ToString("D"),
                    Name = $"workspace-{windowId:N}",
                },
            });
            Assert.True(response.Ok, response.Error);
        }

        application.FlushTreeEvents();
        var events = client.Send(new ControlRequest
        {
            Cmd = Command.EventsRead,
            Args = new ControlArgs
            {
                Run = run.ToString("D"),
                After = after.ToString(),
            },
        });
        Assert.True(events.Ok, events.Error);
        Assert.Equal(
            new[]
            {
                firstId.ToString("D").ToUpperInvariant(),
                secondId.ToString("D").ToUpperInvariant(),
            },
            events.Result!.Events!.Items
                .Where(item => item.Kind == ControlEventKind.TreeChanged)
                .Select(item => item.Window)
                .Distinct());
    }

    [Fact]
    public void One_pipe_resolves_an_explicit_workspace_target_across_open_windows()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstWorkspaceId = Guid.NewGuid();
        var secondWorkspaceId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, SnapshotWithWorkspace("alpha", firstWorkspaceId));
        WriteWindowSnapshot(secondId, SnapshotWithWorkspace("beta", secondWorkspaceId));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = CreateApplication();
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.WorkspaceRename,
            Target = secondWorkspaceId.ToString("D"),
            Args = new ControlArgs { Name = "second-workspace" },
        });

        Assert.True(response.Ok, response.Error);
        Assert.Equal("alpha", Assert.Single(application.GetOpenWindow(firstId).Store.Workspaces).Name);
        Assert.Equal(
            "second-workspace",
            Assert.Single(application.GetOpenWindow(secondId).Store.Workspaces).Name);
    }

    [Fact]
    public void Workspace_theme_by_id_routes_to_the_window_that_owns_the_workspace()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var secondWorkspaceId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, SnapshotWithWorkspace("alpha", Guid.NewGuid()));
        WriteWindowSnapshot(secondId, SnapshotWithWorkspace("beta", secondWorkspaceId));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication();
        application.Start();
        application.GetOpenWindow(secondId).Store.SetWorkspaceTheme(secondWorkspaceId, "Nord");

        // the window hosts here list no themes, so only a clear can succeed: the routing is what
        // this test pins
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.WorkspaceTheme,
            Target = secondWorkspaceId.ToString("D"),
        });

        Assert.True(response.Ok, response.Error);
        Assert.Null(Assert.Single(application.GetOpenWindow(secondId).Store.Workspaces).ThemeName);
    }

    [Fact]
    public async Task SetWorkspaceThemeAsync_sends_workspace_theme_to_the_named_window()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstWorkspaceId = Guid.NewGuid();
        var secondWorkspaceId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, ThemedSnapshot("alpha", firstWorkspaceId));
        WriteWindowSnapshot(secondId, ThemedSnapshot("beta", secondWorkspaceId));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication();
        application.Start();

        var cleared = await application.SetWorkspaceThemeAsync(secondId, secondWorkspaceId, themeName: null);
        // these window hosts list no themes, so any name is unknown
        var refused = await application.SetWorkspaceThemeAsync(secondId, secondWorkspaceId, "Dracula");

        Assert.True(cleared.Ok, cleared.Error);
        Assert.Equal(secondWorkspaceId.ToString("D").ToUpperInvariant(), cleared.Result!.Id);
        Assert.Null(Assert.Single(application.GetOpenWindow(secondId).Store.Workspaces).ThemeName);
        Assert.Equal("Nord", Assert.Single(application.GetOpenWindow(firstId).Store.Workspaces).ThemeName);
        Assert.False(refused.Ok);
        Assert.Equal("unknown theme: Dracula", refused.Error);
    }

    [Fact]
    public async Task SetWorkspaceIconAsync_sends_workspace_icon_to_the_named_window()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstWorkspaceId = Guid.NewGuid();
        var secondWorkspaceId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, SnapshotWithWorkspace("alpha", firstWorkspaceId));
        WriteWindowSnapshot(secondId, SnapshotWithWorkspace("beta", secondWorkspaceId));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication();
        application.Start();

        var set = await application.SetWorkspaceIconAsync(secondId, secondWorkspaceId, "MapPin");
        var refused = await application.SetWorkspaceIconAsync(secondId, secondWorkspaceId, "mappin");

        Assert.True(set.Ok, set.Error);
        Assert.Equal("MapPin", Assert.Single(application.GetOpenWindow(secondId).Store.Workspaces).IconName);
        Assert.Null(Assert.Single(application.GetOpenWindow(firstId).Store.Workspaces).IconName);
        Assert.False(refused.Ok);
        Assert.Equal("unknown icon: mappin", refused.Error);
        Assert.Equal("MapPin", Assert.Single(application.GetOpenWindow(secondId).Store.Workspaces).IconName);
    }

    [Fact]
    public void Workspace_prefix_ambiguity_is_computed_across_all_open_windows()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstWorkspaceId = Guid.Parse("0A11AAAA-0000-0000-0000-000000000011");
        var secondWorkspaceId = Guid.Parse("0A22BBBB-0000-0000-0000-000000000012");
        WriteWindowSnapshot(firstId, SnapshotWithWorkspace("alpha", firstWorkspaceId));
        WriteWindowSnapshot(secondId, SnapshotWithWorkspace("beta", secondWorkspaceId));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = CreateApplication();
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.WorkspaceRename,
            Target = "0a",
            Args = new ControlArgs { Name = "wrong" },
        });

        Assert.False(response.Ok);
        Assert.Equal(
            "ambiguous workspace prefix '0a' → 0A11AAAA, 0A22BBBB",
            response.Error);
        Assert.Equal("alpha", Assert.Single(application.GetOpenWindow(firstId).Store.Workspaces).Name);
        Assert.Equal("beta", Assert.Single(application.GetOpenWindow(secondId).Store.Workspaces).Name);
    }

    [Fact]
    public void Shared_application_ring_serializes_concurrent_window_events()
    {
        using var application = CreateApplication();
        var anchor = application.Ring.Read(cursor: null).Batch;
        const int writerCount = 8;
        const int eventsPerWriter = 400;

        Parallel.For(0, writerCount, writer =>
        {
            for (var index = 0; index < eventsPerWriter; index++)
            {
                application.Ring.Append(new ControlEventDraft
                {
                    Kind = ControlEventKind.TreeChanged,
                    Window = $"window-{writer}",
                });
            }
        });

        var read = application.Ring.Read(
            new ControlEventCursor(anchor.Run, anchor.Next),
            limit: writerCount * eventsPerWriter).Batch;
        Assert.Equal(writerCount * eventsPerWriter, read.Items.Length);
        Assert.Equal(writerCount * eventsPerWriter, read.Items.Select(item => item.Seq).Distinct().Count());
        Assert.Equal((ulong)(writerCount * eventsPerWriter), read.Next - anchor.Next);
    }

    [Fact]
    public async Task Implicit_requests_follow_the_last_explicitly_activated_window()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = CreateApplication();
        await application.ReportActivatedAsync(secondId);
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.WorkspaceNew,
            Args = new ControlArgs { Name = "frontmost-only" },
        });

        Assert.True(response.Ok, response.Error);
        Assert.Single(application.GetOpenWindow(firstId).Store.Workspaces);
        Assert.Equal(
            "frontmost-only",
            application.GetOpenWindow(secondId).Store.Workspaces[^1].Name);
    }

    [Fact]
    public void Batch_session_first_target_ambiguity_is_computed_across_open_windows()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstSessionId = Guid.Parse("0A11AAAA-0000-0000-0000-000000000011");
        var secondSessionId = Guid.Parse("0A22BBBB-0000-0000-0000-000000000012");
        WriteWindowSnapshot(firstId, SnapshotWithSession("alpha", firstSessionId));
        WriteWindowSnapshot(secondId, SnapshotWithSession("beta", secondSessionId));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = CreateApplication();
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.SessionClose,
            Args = new ControlArgs { Targets = ["0a"] },
        });

        Assert.False(response.Ok);
        Assert.Equal(
            "ambiguous session prefix '0a' → 0A11AAAA, 0A22BBBB",
            response.Error);
        Assert.NotNull(application.GetOpenWindow(firstId).Store.SessionWithId(firstSessionId));
        Assert.NotNull(application.GetOpenWindow(secondId).Store.SessionWithId(secondSessionId));
    }

    [Fact]
    public void Launch_order_activates_the_persisted_active_window_last()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteWindowSnapshot(thirdId, "gamma");
        WriteIndex(secondId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true),
            new IndexEntry(thirdId, "third", IsOpen: true));

        using var application = CreateApplication();

        Assert.Equal(new[] { firstId, thirdId, secondId }, application.LaunchWindowIds);
        Assert.Equal(secondId, application.LaunchWindowIds[^1]);
    }

    [Fact]
    public async Task Closing_window_stops_routing_before_waiting_for_surface_shutdown()
    {
        var windowId = Guid.NewGuid();
        WriteWindowSnapshot(windowId, SnapshotWithSession("alpha", Guid.NewGuid()));
        WriteIndex(windowId, new IndexEntry(windowId, "first", IsOpen: true));
        var surface = new BlockingShutdownSurface();

        using var application = CreateApplication();
        application.AttachWindow(windowId, new LiveControlHostOptions
        {
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(surface),
        });
        application.Start();
        var close = application.CloseWindowAsync(windowId);
        await surface.ShutdownStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.WorkspaceNew,
            Args = new ControlArgs
            {
                Window = windowId.ToString("D"),
                Name = "too-late",
            },
        });

        Assert.False(response.Ok);
        Assert.Equal("window not open — window.select it first", response.Error);
        surface.ReleaseShutdown.TrySetResult(null);
        await close;
    }

    [Fact]
    public void Font_and_notify_resolve_non_frontmost_session_targets()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, SnapshotWithSession("alpha", firstSessionId));
        WriteWindowSnapshot(secondId, SnapshotWithSession("beta", secondSessionId));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        var secondSurface = new FakeSurface("second-pane");

        using var application = CreateApplication();
        using var first = application.AttachWindow(firstId, new LiveControlHostOptions
        {
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface("first-pane")),
        });
        using var second = application.AttachWindow(secondId, new LiveControlHostOptions
        {
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(secondSurface),
        });
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var font = client.Send(new ControlRequest
        {
            Cmd = Command.FontInc,
            Target = secondSessionId.ToString("D"),
        });
        var notify = client.Send(new ControlRequest
        {
            Cmd = Command.Notify,
            Target = secondSessionId.ToString("D"),
            Args = new ControlArgs { Body = "done" },
        });

        Assert.True(font.Ok, font.Error);
        Assert.True(notify.Ok, notify.Error);
        Assert.Equal("increase_font_size:1", secondSurface.LastBindingAction);
        Assert.Equal(0, first.Store.SessionWithId(firstSessionId)?.UnseenCount);
        Assert.Equal(1, second.Store.SessionWithId(secondSessionId)?.UnseenCount);
    }

    [Fact]
    public void App_global_commands_apply_to_every_open_attached_window()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, SnapshotWithRestoreCommand("alpha"));
        WriteWindowSnapshot(secondId, SnapshotWithRestoreCommand("beta"));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        var firstConfigReloads = 0;
        var secondConfigReloads = 0;

        using var application = CreateApplication();
        using var first = application.AttachWindow(firstId, GlobalOptions(() => ++firstConfigReloads));
        using var second = application.AttachWindow(secondId, GlobalOptions(() => ++secondConfigReloads));
        var firstKeymapReloads = 0;
        var secondKeymapReloads = 0;
        first.Actions.KeymapReloaded += _ => firstKeymapReloads++;
        second.Actions.KeymapReloaded += _ => secondKeymapReloads++;
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        Assert.True(client.Send(new ControlRequest { Cmd = Command.RestoreClear }).Ok);
        Assert.True(client.Send(new ControlRequest { Cmd = Command.ConfigReload }).Ok);
        Assert.True(client.Send(new ControlRequest { Cmd = Command.KeymapReload }).Ok);
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.ThemeSet,
            Args = new ControlArgs { Name = "Dracula" },
        }).Ok);

        Assert.Null(Assert.Single(first.Store.Workspaces).Sessions[0].ForegroundCommand);
        Assert.Null(Assert.Single(second.Store.Workspaces).Sessions[0].ForegroundCommand);
        Assert.Equal(1, firstConfigReloads);
        Assert.Equal(1, secondConfigReloads);
        Assert.Equal(1, firstKeymapReloads);
        Assert.Equal(1, secondKeymapReloads);
        Assert.Equal("Dracula", first.Actions.Theme.Light);
        Assert.Equal("Dracula", second.Actions.Theme.Light);
    }

    [Fact]
    public async Task Local_theme_selection_uses_the_application_global_theme_path()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = CreateApplication();
        using var first = application.AttachWindow(firstId, GlobalOptions(() => 0));
        using var second = application.AttachWindow(secondId, GlobalOptions(() => 0));

        var response = await application.SetThemeAsync(new ControlArgs { Name = "Dracula" });

        Assert.True(response.Ok, response.Error);
        Assert.Equal("Dracula", first.Actions.Theme.Light);
        Assert.Equal("Dracula", second.Actions.Theme.Light);

        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));
        Assert.Contains("await _applicationHost.SetThemeAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("themeHost.Host.Actions.SetTheme", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_list_works_without_a_controller_while_mutations_keep_the_headless_fallback()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = CreateApplication();
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var list = client.Send(new ControlRequest
        {
            Cmd = Command.WindowList,
            Args = new ControlArgs { Window = secondId.ToString("D") },
        });
        var rename = client.Send(new ControlRequest
        {
            Cmd = Command.WindowRename,
            Target = secondId.ToString("D"),
            Args = new ControlArgs
            {
                Window = secondId.ToString("D"),
                Name = "not-until-W2",
            },
        });

        Assert.True(list.Ok, list.Error);
        Assert.Equal(
            new[] { firstId, secondId }.Select(id => id.ToString("D").ToUpperInvariant()),
            list.Result!.Windows!.Select(window => window.Id));
        Assert.False(rename.Ok);
        Assert.Equal($"no such window: {secondId:D}", rename.Error);
        Assert.Equal(new[] { "first", "second" }, application.Windows.Select(window => window.Name));
    }

    [Fact]
    public async Task A_captured_window_host_rejects_dispatch_after_normal_close()
    {
        var windowId = Guid.NewGuid();
        WriteWindowSnapshot(windowId, "alpha");
        WriteIndex(windowId, new IndexEntry(windowId, "first", IsOpen: true));

        using var application = CreateApplication();
        var captured = application.GetOpenWindow(windowId);

        await application.CloseWindowAsync(windowId);
        var response = await captured.DispatchAsync(new ControlRequest
        {
            Cmd = Command.WorkspaceNew,
            Args = new ControlArgs { Name = "too-late" },
        });

        Assert.False(response!.Ok);
        Assert.Equal("window not open — window.select it first", response.Error);
        Assert.Single(captured.Store.Workspaces);
    }

    [Fact]
    public void Config_reload_clears_font_overrides_in_closed_window_snapshots()
    {
        var openId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        WriteWindowSnapshot(openId, "alpha");
        WriteWindowSnapshot(closedId, SnapshotWithFontSize("beta", fontSize: 22));
        WriteIndex(openId,
            new IndexEntry(openId, "first", IsOpen: true),
            new IndexEntry(closedId, "second", IsOpen: false));

        using var application = CreateApplication();
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.ConfigReload,
        });

        Assert.True(response.Ok, response.Error);
        var snapshot = new PersistenceStore(
            Path.Combine(_stateDir, "windows"),
            $"{closedId:D}.json").Load();
        Assert.Null(Assert.Single(Assert.Single(snapshot.Workspaces).Sessions).FontSize);
    }

    [Fact]
    public async Task Stopping_dispatch_acceptance_does_not_wait_for_an_accepted_dispatch()
    {
        var windowId = Guid.NewGuid();
        WriteWindowSnapshot(windowId, "alpha");
        WriteIndex(windowId, new IndexEntry(windowId, "first", IsOpen: true));
        var dispatchEntered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseDispatch = new ManualResetEventSlim();

        using var application = CreateApplication();
        application.AttachWindow(windowId, new LiveControlHostOptions
        {
            ConfigDir = _stateDir,
            ConfigReload = () =>
            {
                dispatchEntered.TrySetResult(null);
                Assert.True(releaseDispatch.Wait(TimeSpan.FromSeconds(30)),
                    "blocked dispatch was not released");
                return 0;
            },
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
        });
        application.Start();
        var window = application.GetOpenWindow(windowId);
        var dispatch = Task.Run(() => new ControlPipeClient(application.PipeName).Send(
            new ControlRequest { Cmd = Command.ConfigReload }));
        try
        {
            await dispatchEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var stop = Task.Factory.StartNew(() =>
            {
                stopStarted.SetResult();
                window.StopDispatching();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            await stopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await stop.WaitAsync(TimeSpan.FromMilliseconds(500));
        }
        finally
        {
            releaseDispatch.Set();
        }

        Assert.True((await dispatch).Ok);
        await application.CloseWindowAsync(windowId);
        Assert.False(Assert.Single(application.Windows).IsOpen);
    }

    [Fact]
    public void Dashboard_explicit_target_resolves_across_open_windows()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, SnapshotWithSession("alpha", firstSessionId));
        WriteWindowSnapshot(secondId, SnapshotWithSession("beta", secondSessionId));
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));

        using var application = CreateApplication();
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.Dashboard,
            Args = new ControlArgs { Targets = [secondSessionId.ToString("D")] },
        });

        Assert.True(response.Ok, response.Error);
        Assert.False(application.GetOpenWindow(firstId).Actions.Dashboard.IsOpen);
        var dashboard = application.GetOpenWindow(secondId).Actions.Dashboard;
        Assert.True(dashboard.IsOpen);
        Assert.Equal(secondSessionId, Assert.Single(dashboard.Members).Session);
    }

    private LiveControlHostOptions GlobalOptions(Func<int> configReload) => new()
    {
        ConfigDir = _stateDir,
        AvailableThemes = ["Dracula"],
        ConfigReload = configReload,
        OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
    };

    private LiveApplicationHost CreateApplication(TimeProvider? clock = null) =>
        new(new LiveApplicationHostOptions
        {
            StateDir = _stateDir,
            HomeDirectory = _stateDir,
            PipeName = "agterm-window-registry-" + Guid.NewGuid().ToString("N"),
            TreeEventTimeProvider = clock ?? TimeProvider.System,
        });

    private sealed class TreeEventClock : TimeProvider
    {
        private TimeSpan _elapsed;
        private readonly List<TestTimer> _timers = new();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new TestTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            _elapsed += elapsed;
            foreach (var timer in _timers.Where(timer => !timer.Disposed && timer.Due <= _elapsed).ToArray())
                timer.Fire();
        }

        public void FireQueuedCallbacks()
        {
            foreach (var timer in _timers.Where(timer => timer.Disposed).ToArray())
                timer.Fire();
        }

        private sealed class TestTimer(TreeEventClock clock, TimerCallback callback, object? state) : ITimer
        {
            public bool Disposed { get; private set; }
            public TimeSpan Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                Due = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : clock._elapsed + dueTime;
                return !Disposed;
            }

            public void Fire() => callback(state);
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private void WriteWindowSnapshot(Guid windowId, string workspaceName)
    {
        WriteWindowSnapshot(windowId, SnapshotWithWorkspace(workspaceName));
        }

        private void WriteWindowSnapshot(Guid windowId, Snapshot snapshot)
        {
        new PersistenceStore(
            Path.Combine(_stateDir, "windows"),
            $"{windowId:D}.json").Save(snapshot);
    }

    private static Snapshot SnapshotWithWorkspace(string workspaceName) =>
        SnapshotWithWorkspace(workspaceName, Guid.NewGuid());

    private static Snapshot SnapshotWithWorkspace(string workspaceName, Guid workspaceId) =>
        new()
        {
            Workspaces =
            [
                new WorkspaceSnapshot
                {
                    Id = workspaceId,
                    Name = workspaceName,
                    Sessions = Array.Empty<SessionSnapshot>(),
                },
            ],
        };

    private static Snapshot ThemedSnapshot(string workspaceName, Guid workspaceId) =>
        new()
        {
            Workspaces =
            [
                new WorkspaceSnapshot
                {
                    Id = workspaceId,
                    Name = workspaceName,
                    Sessions = Array.Empty<SessionSnapshot>(),
                    Theme = "Nord",
                },
            ],
        };

    private static Snapshot SnapshotWithSession(string workspaceName, Guid sessionId) =>
        new()
        {
            SelectedSessionId = sessionId,
            Workspaces =
            [
                new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid(),
                    Name = workspaceName,
                    Sessions =
                    [
                        new SessionSnapshot
                        {
                            Id = sessionId,
                            Cwd = Path.GetTempPath(),
                        },
                    ],
                },
            ],
        };

    private static Snapshot SnapshotWithRestoreCommand(string workspaceName)
    {
        var sessionId = Guid.NewGuid();
        return new Snapshot
        {
            SelectedSessionId = sessionId,
            Workspaces =
            [
                new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid(),
                    Name = workspaceName,
                    Sessions =
                    [
                        new SessionSnapshot
                        {
                            Id = sessionId,
                            Cwd = Path.GetTempPath(),
                            ForegroundCommand = ["pwsh.exe"],
                        },
                    ],
                },
            ],
        };
    }

    private static Snapshot SnapshotWithFontSize(string workspaceName, double fontSize)
    {
        var sessionId = Guid.NewGuid();
        return new Snapshot
        {
            SelectedSessionId = sessionId,
            Workspaces =
            [
                new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid(),
                    Name = workspaceName,
                    Sessions =
                    [
                        new SessionSnapshot
                        {
                            Id = sessionId,
                            Cwd = Path.GetTempPath(),
                            FontSize = fontSize,
                        },
                    ],
                },
            ],
        };
    }

    private void WriteIndex(Guid? frontmost, params IndexEntry[] windows)
    {
        Directory.CreateDirectory(_stateDir);
        File.WriteAllText(
            Path.Combine(_stateDir, "windows.json"),
            ControlJson.Serialize(new TestWindowsIndex
            {
                Frontmost = frontmost,
                Windows = windows,
            }));
    }

    private sealed record IndexEntry(
        [property: JsonPropertyName("id")]
        [property: JsonConverter(typeof(UppercaseGuidConverter))]
        Guid Id,
        [property: JsonPropertyName("name")]
        string Name,
        [property: JsonPropertyName("isOpen")]
        bool IsOpen);

    private sealed record TestWindowsIndex
    {
        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("frontmost")]
        [JsonConverter(typeof(UppercaseGuidConverter))]
        public Guid? Frontmost { get; init; }

        [JsonPropertyName("windows")]
        public required IndexEntry[] Windows { get; init; }
    }

    private sealed class FakeSurface(string paneToken = "registry-fake") : IHeadlessSurface
    {
        public string? PaneToken => paneToken;
        public string? LastBindingAction { get; private set; }
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => string.Empty;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action)
        {
            LastBindingAction = action;
            return true;
        }
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }

    private sealed class BlockingShutdownSurface : IHeadlessSurface, ILiveShutdownSurface
    {
        public TaskCompletionSource<object?> ShutdownStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> ReleaseShutdown { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? PaneToken => "blocking-shutdown";
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => string.Empty;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
        public void StopOutput() { }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            ShutdownStarted.TrySetResult(null);
            await ReleaseShutdown.Task.WaitAsync(cancellationToken);
        }
    }
}