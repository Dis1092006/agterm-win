using System.Text.Json.Serialization;
using AgTerm.App;
using AgTerm.ControlServer;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class LiveWindowControlE2ETests : IDisposable
{
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-window-control-").FullName;

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
    public void Window_list_projects_every_registry_entry_in_persisted_order()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteWindowSnapshot(closedId, "gamma");
        WriteIndex(secondId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true),
            new IndexEntry(closedId, "closed", IsOpen: false));

        using var application = CreateApplication();
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(
            new ControlRequest { Cmd = Command.WindowList });

        Assert.True(response.Ok, response.Error);
        var windows = Assert.IsAssignableFrom<IReadOnlyList<ControlWindowNode>>(
            response.Result?.Windows);
        Assert.Equal(
            new[] { firstId, secondId, closedId }
                .Select(id => id.ToString("D").ToUpperInvariant()),
            windows.Select(window => window.Id));
        Assert.Equal(new[] { "first", "second", "closed" }, windows.Select(window => window.Name));
        Assert.Equal(new[] { true, true, false }, windows.Select(window => window.Open));
        Assert.Equal(new[] { false, true, false }, windows.Select(window => window.Active));
        Assert.Equal(new bool?[] { true, true, null }, windows.Select(window => window.SidebarVisible));
        Assert.All(windows, window => Assert.Null(window.Geometry));
        Assert.All(windows, window => Assert.Null(window.Fullscreen));
        Assert.All(windows, window => Assert.Null(window.Zoomed));
    }

    [Fact]
    public void Window_new_creates_an_independent_active_store_and_requests_one_open()
    {
        var windows = new FakeWindowController();
        using var application = CreateApplication(windows);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var originalId = Assert.Single(application.Windows).Id;

        var response = client.Send(new ControlRequest
        {
            Cmd = Command.WindowNew,
            Args = new ControlArgs { Name = "work" },
        });

        Assert.True(response.Ok, response.Error);
        var newId = Guid.Parse(response.Result!.Id!);
        Assert.NotEqual(originalId, newId);
        Assert.Equal(newId, application.ActiveWindowId);
        Assert.Equal(new[] { originalId, newId }, application.Windows.Select(window => window.Id));
        Assert.Equal("work", application.Windows[^1].Name);
        Assert.True(application.Windows[^1].IsOpen);
        Assert.Equal(new[] { newId }, windows.Opened);
        var newHost = application.GetOpenWindow(newId);
        Assert.NotSame(application.GetOpenWindow(originalId).Store, newHost.Store);
        var workspace = Assert.Single(newHost.Store.Workspaces);
        Assert.Single(workspace.Sessions);
        Assert.Equal(_stateDir, workspace.Sessions[0].InitialCwd);

        var list = client.Send(new ControlRequest { Cmd = Command.WindowList });
        Assert.Equal(2, list.Result!.Windows!.Length);
    }

    [Fact]
    public void Window_select_activates_open_windows_and_reopens_closed_windows()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: false));
        var windows = new FakeWindowController();

        using var application = CreateApplication(windows);
        windows.Activated = application.ReportActivated;
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var reopened = client.Send(new ControlRequest
        {
            Cmd = Command.WindowSelect,
            Target = secondId.ToString("D"),
        });
        Assert.True(reopened.Ok, reopened.Error);
        Assert.Equal(secondId.ToString("D").ToUpperInvariant(), reopened.Result?.Id);
        Assert.Equal(new[] { secondId }, windows.Opened);
        Assert.True(application.Windows.Single(window => window.Id == secondId).IsOpen);
        Assert.Equal("beta", Assert.Single(application.GetOpenWindow(secondId).Store.Workspaces).Name);
        Assert.Equal(secondId, application.ActiveWindowId);

        var selected = client.Send(new ControlRequest
        {
            Cmd = Command.WindowSelect,
            Target = firstId.ToString("D"),
        });
        Assert.True(selected.Ok, selected.Error);
        Assert.Equal(new[] { firstId }, windows.ActivationRequests);
        Assert.Equal(firstId, application.ActiveWindowId);
        Assert.Single(windows.Opened);
    }

    [Fact]
    public void Window_close_waits_for_the_registry_transition_and_is_idempotent_when_closed()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(secondId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        var windows = new FakeWindowController();

        using var application = CreateApplication(windows);
        windows.Closed = id => application.CloseWindowAsync(id);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var closed = client.Send(new ControlRequest
        {
            Cmd = Command.WindowClose,
            Target = secondId.ToString("D"),
        });
        Assert.True(closed.Ok, closed.Error);
        Assert.Equal(secondId.ToString("D").ToUpperInvariant(), closed.Result?.Id);
        Assert.Equal(new[] { secondId }, windows.ClosedRequests);
        Assert.False(application.Windows.Single(window => window.Id == secondId).IsOpen);
        Assert.Equal(firstId, application.ActiveWindowId);

        var repeated = client.Send(new ControlRequest
        {
            Cmd = Command.WindowClose,
            Target = secondId.ToString("D"),
        });
        Assert.True(repeated.Ok, repeated.Error);
        Assert.Equal(secondId.ToString("D").ToUpperInvariant(), repeated.Result?.Id);
        Assert.Single(windows.ClosedRequests);
    }

    [Fact]
    public void Window_rename_strips_control_characters_from_the_window_name()
    {
        var id = Guid.NewGuid();
        WriteWindowSnapshot(id, "alpha");
        WriteIndex(id, new IndexEntry(id, "first", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var renamed = client.Send(new ControlRequest { Cmd = Command.WindowRename, Target = id.ToString("D"), Args = new ControlArgs { Name = "prod\nwhoami" } });
        Assert.True(renamed.Ok, renamed.Error);
        Assert.Equal("prodwhoami", Assert.Single(application.Windows).Name);

        var controlOnly = client.Send(new ControlRequest { Cmd = Command.WindowRename, Target = id.ToString("D"), Args = new ControlArgs { Name = "\u0007" } });
        Assert.True(controlOnly.Ok, controlOnly.Error);
        Assert.Equal("prodwhoami", Assert.Single(application.Windows).Name);
    }

    [Fact]
    public void Window_rename_updates_open_and_closed_records_and_persists_names()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: false));
        var windows = new FakeWindowController();

        using (var application = CreateApplication(windows))
        {
            application.Start();
            var client = new ControlPipeClient(application.PipeName);
            var open = client.Send(new ControlRequest
            {
                Cmd = Command.WindowRename,
                Target = firstId.ToString("D"),
                Args = new ControlArgs { Name = "open renamed" },
            });
            var closed = client.Send(new ControlRequest
            {
                Cmd = Command.WindowRename,
                Target = secondId.ToString("D"),
                Args = new ControlArgs { Name = "closed renamed" },
            });

            Assert.True(open.Ok, open.Error);
            Assert.True(closed.Ok, closed.Error);
            Assert.Equal(firstId.ToString("D").ToUpperInvariant(), open.Result?.Id);
            Assert.Equal(secondId.ToString("D").ToUpperInvariant(), closed.Result?.Id);
            Assert.Equal(
                new[] { "open renamed", "closed renamed" },
                application.Windows.Select(window => window.Name));
            Assert.Equal("open renamed", application.GetOpenWindow(firstId).WindowName);
            Assert.Equal("open renamed", application.GetOpenWindow(firstId).Actions.WindowName);
        }

        using var reloaded = CreateApplication();
        Assert.Equal(
            new[] { "open renamed", "closed renamed" },
            reloaded.Windows.Select(window => window.Name));
    }

    [Fact]
    public void Window_delete_removes_open_or_closed_records_and_protects_the_last_entry()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteWindowSnapshot(thirdId, "gamma");
        WriteIndex(thirdId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: false),
            new IndexEntry(thirdId, "third", IsOpen: true));
        var windows = new FakeWindowController();

        using var application = CreateApplication(windows);
        windows.Closed = id => application.CloseWindowAsync(id);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var closed = client.Send(new ControlRequest
        {
            Cmd = Command.WindowDelete,
            Target = secondId.ToString("D"),
        });
        Assert.True(closed.Ok, closed.Error);
        Assert.False(File.Exists(WindowSnapshotPath(secondId)));
        Assert.DoesNotContain(application.Windows, window => window.Id == secondId);
        Assert.Empty(windows.ClosedRequests);

        var open = client.Send(new ControlRequest
        {
            Cmd = Command.WindowDelete,
            Target = thirdId.ToString("D"),
        });
        Assert.True(open.Ok, open.Error);
        Assert.Equal(new[] { thirdId }, windows.ClosedRequests);
        Assert.False(File.Exists(WindowSnapshotPath(thirdId)));
        Assert.DoesNotContain(application.Windows, window => window.Id == thirdId);
        Assert.Equal(firstId, application.ActiveWindowId);

        var last = client.Send(new ControlRequest
        {
            Cmd = Command.WindowDelete,
            Target = firstId.ToString("D"),
        });
        Assert.False(last.Ok);
        Assert.Equal("cannot delete last window", last.Error);
        Assert.True(File.Exists(WindowSnapshotPath(firstId)));
        Assert.Single(application.Windows);
    }

    [Fact]
    public void Window_list_projects_live_geometry_and_presenter_flags_for_open_windows_only()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteWindowSnapshot(closedId, "gamma");
        WriteIndex(secondId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true),
            new IndexEntry(closedId, "closed", IsOpen: false));
        var windows = new FakeWindowController();
        windows.Presentations[firstId] = new LiveWindowPresentation(
            new ControlWindowFrame { X = 10, Y = 20, Width = 800, Height = 600, Display = 0 },
            Fullscreen: false,
            Zoomed: true);
        windows.Presentations[secondId] = new LiveWindowPresentation(
            new ControlWindowFrame { X = -40, Y = 50, Width = 900, Height = 700, Display = 1 },
            Fullscreen: true,
            Zoomed: false);

        using var application = CreateApplication(windows);
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(
            new ControlRequest { Cmd = Command.WindowList });

        Assert.True(response.Ok, response.Error);
        var listed = response.Result!.Windows!;
        Assert.Equal(windows.Presentations[firstId].Geometry, listed[0].Geometry);
        Assert.False(listed[0].Fullscreen);
        Assert.True(listed[0].Zoomed);
        Assert.Equal(windows.Presentations[secondId].Geometry, listed[1].Geometry);
        Assert.True(listed[1].Fullscreen);
        Assert.False(listed[1].Zoomed);
        Assert.Null(listed[2].Geometry);
        Assert.Null(listed[2].Fullscreen);
        Assert.Null(listed[2].Zoomed);
        Assert.Equal(new[] { firstId, secondId }, windows.PresentationRequests);
    }

    [Fact]
    public void Window_resize_and_move_route_to_the_target_and_preserve_exact_errors()
    {
        var firstId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(closedId, "beta");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(closedId, "closed", IsOpen: false));
        var windows = new FakeWindowController { DisplayCount = 2 };
        windows.Presentations[firstId] = new LiveWindowPresentation(
            new ControlWindowFrame { X = 1, Y = 2, Width = 640, Height = 480, Display = 0 },
            Fullscreen: false,
            Zoomed: false);

        using var application = CreateApplication(windows);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var resize = client.Send(new ControlRequest
        {
            Cmd = Command.WindowResize,
            Target = firstId.ToString("D"),
            Args = new ControlArgs { Width = 900, Height = 700 },
        });
        var move = client.Send(new ControlRequest
        {
            Cmd = Command.WindowMove,
            Target = firstId.ToString("D"),
            Args = new ControlArgs { X = -40, Y = 50, Display = 1 },
        });

        Assert.True(resize.Ok, resize.Error);
        Assert.True(move.Ok, move.Error);
        Assert.Equal(new[] { (firstId, 900, 700) }, windows.ResizeRequests);
        Assert.Equal(new[] { (firstId, -40, 50, (int?)1) }, windows.MoveRequests);
        var listed = client.Send(new ControlRequest { Cmd = Command.WindowList });
        Assert.Equal(
            new ControlWindowFrame { X = -40, Y = 50, Width = 900, Height = 700, Display = 1 },
            listed.Result!.Windows![0].Geometry);

        var invalidDisplay = client.Send(new ControlRequest
        {
            Cmd = Command.WindowMove,
            Target = firstId.ToString("D"),
            Args = new ControlArgs { X = 0, Y = 0, Display = 2 },
        });
        Assert.False(invalidDisplay.Ok);
        Assert.Equal("display 2 out of range (have 2)", invalidDisplay.Error);

        var closed = client.Send(new ControlRequest
        {
            Cmd = Command.WindowResize,
            Target = closedId.ToString("D"),
            Args = new ControlArgs { Width = 800, Height = 600 },
        });
        Assert.False(closed.Ok);
        Assert.Equal("window not open — window.select it first", closed.Error);

        var invalidResize = client.Send(new ControlRequest
        {
            Cmd = Command.WindowResize,
            Args = new ControlArgs { Width = 800 },
        });
        var invalidMove = client.Send(new ControlRequest
        {
            Cmd = Command.WindowMove,
            Args = new ControlArgs { X = 10 },
        });
        Assert.Equal("window.resize requires positive width and height", invalidResize.Error);
        Assert.Equal("window.move requires x and y", invalidMove.Error);
    }

    [Fact]
    public void Window_zoom_and_fullscreen_toggle_independently_and_round_trip_through_list()
    {
        var windowId = Guid.NewGuid();
        WriteWindowSnapshot(windowId, "alpha");
        WriteIndex(windowId, new IndexEntry(windowId, "first", IsOpen: true));
        var windows = new FakeWindowController();
        windows.Presentations[windowId] = new LiveWindowPresentation(
            new ControlWindowFrame { X = 0, Y = 0, Width = 800, Height = 600, Display = 0 },
            Fullscreen: false,
            Zoomed: false);

        using var application = CreateApplication(windows);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowZoom }).Ok);
        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowFullscreen }).Ok);
        var enabled = Assert.Single(client.Send(
            new ControlRequest { Cmd = Command.WindowList }).Result!.Windows!);
        Assert.True(enabled.Zoomed);
        Assert.True(enabled.Fullscreen);

        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowZoom }).Ok);
        var restored = Assert.Single(client.Send(
            new ControlRequest { Cmd = Command.WindowList }).Result!.Windows!);
        Assert.False(restored.Zoomed);
        Assert.True(restored.Fullscreen);
        Assert.Equal(new[] { windowId, windowId }, windows.ZoomRequests);
        Assert.Equal(new[] { windowId }, windows.FullscreenRequests);
    }

    [Fact]
    public void Winui_application_binds_the_live_controller_and_all_window_router_actions()
    {
        Assert.True(WindowCommandRouter.ActionImplementations[BuiltinAction.NewWindow]);
        Assert.True(WindowCommandRouter.ActionImplementations[BuiltinAction.RenameWindow]);
        Assert.True(WindowCommandRouter.ActionImplementations[BuiltinAction.DeleteWindow]);

        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "App.xaml.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "src", "AgTerm.App", "MainWindow.xaml.cs"));
        Assert.Contains("App : Application, ILiveWindowController", app, StringComparison.Ordinal);
        Assert.Contains("Dictionary<Guid, MainWindow>", app, StringComparison.Ordinal);
        Assert.Contains("WindowController = this", app, StringComparison.Ordinal);
        Assert.Contains("PipeName = ControlPipeName.Resolve()", app, StringComparison.Ordinal);
        Assert.Contains("Task OpenWindowAsync(", app, StringComparison.Ordinal);
        Assert.DoesNotContain("window.Ready.WaitAsync(cancellationToken)", app, StringComparison.Ordinal);
        Assert.Contains("await window.Ready;", app, StringComparison.Ordinal);
        Assert.Contains("window.CloseFailedOpenAsync()", app, StringComparison.Ordinal);
        Assert.Contains("Task ActivateWindowAsync(", app, StringComparison.Ordinal);
        Assert.Contains("Task CloseWindowAsync(", app, StringComparison.Ordinal);
        Assert.Contains("Task<LiveWindowPresentation?> CaptureWindowAsync(", app, StringComparison.Ordinal);
        Assert.Contains("Task ResizeWindowAsync(", app, StringComparison.Ordinal);
        Assert.Contains("Task<LiveWindowMoveResult> MoveWindowAsync(", app, StringComparison.Ordinal);
        Assert.Contains("Task ToggleZoomAsync(", app, StringComparison.Ordinal);
        Assert.Contains("Task ToggleFullscreenAsync(", app, StringComparison.Ordinal);
        Assert.DoesNotContain("applicationHost.Dispose();", app, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.NewWindow]", mainWindow, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.RenameWindow]", mainWindow, StringComparison.Ordinal);
        Assert.Contains("[BuiltinAction.DeleteWindow]", mainWindow, StringComparison.Ordinal);
        Assert.Contains("_closeCoordinator.ShouldCancelClose();", mainWindow, StringComparison.Ordinal);
        Assert.Contains("return _closedCompletion.Task;", mainWindow, StringComparison.Ordinal);
        Assert.Contains("CloseFailedOpenAsync", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_window_reopen_does_not_arm_the_launch_only_restore_override()
    {
        var openId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        var closedSessionId = Guid.NewGuid();
        WriteWindowSnapshot(openId, "alpha");
        WriteWindowSnapshot(closedId, SnapshotWithCommands("beta", closedSessionId));
        WriteIndex(openId,
            new IndexEntry(openId, "first", IsOpen: true),
            new IndexEntry(closedId, "second", IsOpen: false));
        var windows = new FakeWindowController();

        using var application = CreateApplication(windows);
        windows.Activated = application.ReportActivated;
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.WindowSelect,
            Target = closedId.ToString("D"),
        });

        Assert.True(response.Ok, response.Error);
        var session = application.GetOpenWindow(closedId).Store.SessionWithId(closedSessionId)!;
        Assert.Null(session.PendingRestoreCommand);
        Assert.Equal(new[] { "pwsh.exe", "-NoLogo" }, session.ForegroundCommand);
    }

    [Fact]
    public void Multi_window_lifecycle_restores_order_names_open_set_and_independent_stores()
    {
        Guid firstId;
        Guid secondId;
        var windows = new FakeWindowController();
        using (var application = CreateApplication(windows))
        {
            windows.Activated = application.ReportActivated;
            windows.Closed = id => application.CloseWindowAsync(id);
            application.Start();
            var client = new ControlPipeClient(application.PipeName);
            firstId = Assert.Single(application.Windows).Id;
            var created = client.Send(new ControlRequest
            {
                Cmd = Command.WindowNew,
                Args = new ControlArgs { Name = "second" },
            });
            Assert.True(created.Ok, created.Error);
            secondId = Guid.Parse(created.Result!.Id!);

            Assert.True(client.Send(new ControlRequest
            {
                Cmd = Command.WindowRename,
                Target = firstId.ToString("D"),
                Args = new ControlArgs { Name = "first renamed" },
            }).Ok);
            Assert.True(client.Send(new ControlRequest
            {
                Cmd = Command.WorkspaceNew,
                Args = new ControlArgs
                {
                    Window = firstId.ToString("D"),
                    Name = "first-only",
                },
            }).Ok);
            Assert.True(client.Send(new ControlRequest
            {
                Cmd = Command.WorkspaceNew,
                Args = new ControlArgs
                {
                    Window = secondId.ToString("D"),
                    Name = "second-only",
                },
            }).Ok);
            Assert.True(client.Send(new ControlRequest
            {
                Cmd = Command.WindowClose,
                Target = firstId.ToString("D"),
            }).Ok);
        }

        using var restored = CreateApplication();
        Assert.Equal(new[] { firstId, secondId }, restored.Windows.Select(window => window.Id));
        Assert.Equal(new[] { "first renamed", "second" }, restored.Windows.Select(window => window.Name));
        Assert.Equal(new[] { false, true }, restored.Windows.Select(window => window.IsOpen));
        Assert.Equal(secondId, restored.ActiveWindowId);
        var second = restored.GetOpenWindow(secondId);
        Assert.Equal("second-only", second.Store.Workspaces[^1].Name);
        Assert.Throws<KeyNotFoundException>(() => restored.GetOpenWindow(firstId));
        Assert.Contains(
            "first-only",
            new PersistenceStore(
                Path.Combine(_stateDir, "windows"),
                $"{firstId:D}.json").Load().Workspaces.Select(workspace => workspace.Name));
    }

    [Fact]
    public void Window_close_and_runtime_reopen_emit_balanced_application_events()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, SnapshotWithSession("beta", sessionId));
        WriteIndex(secondId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true));
        var windows = new FakeWindowController();

        using var application = CreateApplication(windows);
        windows.Activated = application.ReportActivated;
        windows.Closed = id => application.CloseWindowAsync(id);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var anchor = client.Send(new ControlRequest { Cmd = Command.EventsRead }).Result!.Events!;

        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.WindowClose,
            Target = secondId.ToString("D"),
        }).Ok);
        var closed = ReadEvents(client, anchor);
        Assert.Equal(
            new[] { ControlEventKind.SessionClosed, ControlEventKind.TreeChanged },
            closed.Items.Select(item => item.Kind));
        Assert.All(closed.Items, item => Assert.Equal(
            secondId.ToString("D").ToUpperInvariant(), item.Window));
        Assert.Equal(sessionId.ToString("D").ToUpperInvariant(), closed.Items[0].Session);

        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.WindowSelect,
            Target = secondId.ToString("D"),
        }).Ok);
        var reopened = ReadEvents(client, closed);
        Assert.Equal(
            new[] { ControlEventKind.SessionCreated, ControlEventKind.TreeChanged },
            reopened.Items.Select(item => item.Kind));
        Assert.All(reopened.Items, item => Assert.Equal(
            secondId.ToString("D").ToUpperInvariant(), item.Window));
        Assert.Equal(sessionId.ToString("D").ToUpperInvariant(), reopened.Items[0].Session);
    }

    [Fact]
    public void Window_delete_resolves_the_target_before_enforcing_the_last_entry_rule()
    {
        var windowId = Guid.NewGuid();
        WriteWindowSnapshot(windowId, "alpha");
        WriteIndex(windowId, new IndexEntry(windowId, "first", IsOpen: true));
        var windows = new FakeWindowController();

        using var application = CreateApplication(windows);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var missingId = Guid.NewGuid();

        var missing = client.Send(new ControlRequest
        {
            Cmd = Command.WindowDelete,
            Target = missingId.ToString("D"),
        });
        var last = client.Send(new ControlRequest
        {
            Cmd = Command.WindowDelete,
            Target = windowId.ToString("D"),
        });

        Assert.Equal($"no such window: {missingId:D}", missing.Error);
        Assert.Equal("cannot delete last window", last.Error);
    }

    [Fact]
    public void Window_move_validates_display_before_resolving_the_window()
    {
        var windowId = Guid.NewGuid();
        WriteWindowSnapshot(windowId, "alpha");
        WriteIndex(windowId, new IndexEntry(windowId, "first", IsOpen: false));
        var windows = new FakeWindowController { DisplayCount = 2 };

        using var application = CreateApplication(windows);
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.WindowMove,
            Target = Guid.NewGuid().ToString("D"),
            Args = new ControlArgs { X = 0, Y = 0, Display = 2 },
        });

        Assert.False(response.Ok);
        Assert.Equal("display 2 out of range (have 2)", response.Error);
    }

    [Fact]
    public void Window_new_and_closed_delete_emit_application_lifecycle_events()
    {
        var windows = new FakeWindowController();
        using var application = CreateApplication(windows);
        windows.Activated = application.ReportActivated;
        windows.Closed = id => application.CloseWindowAsync(id);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var anchor = client.Send(new ControlRequest { Cmd = Command.EventsRead }).Result!.Events!;

        var created = client.Send(new ControlRequest { Cmd = Command.WindowNew });
        Assert.True(created.Ok, created.Error);
        var createdId = Guid.Parse(created.Result!.Id!);
        var creationEvents = ReadEvents(client, anchor);
        Assert.Equal(
            new[] { ControlEventKind.SessionCreated, ControlEventKind.TreeChanged },
            creationEvents.Items.Select(item => item.Kind));
        Assert.All(creationEvents.Items, item => Assert.Equal(
            createdId.ToString("D").ToUpperInvariant(), item.Window));

        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.WindowClose,
            Target = createdId.ToString("D"),
        }).Ok);
        var afterClose = ReadEvents(client, creationEvents);
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.WindowDelete,
            Target = createdId.ToString("D"),
        }).Ok);
        var deletionEvents = ReadEvents(client, afterClose);
        var deleted = Assert.Single(deletionEvents.Items);
        Assert.Equal(ControlEventKind.TreeChanged, deleted.Kind);
        Assert.Equal(createdId.ToString("D").ToUpperInvariant(), deleted.Window);
    }

    [Fact]
    public void Window_rename_emits_one_structural_event_for_open_and_closed_records()
    {
        var openId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        WriteWindowSnapshot(openId, "alpha");
        WriteWindowSnapshot(closedId, "beta");
        WriteIndex(openId,
            new IndexEntry(openId, "open", IsOpen: true),
            new IndexEntry(closedId, "closed", IsOpen: false));
        var windows = new FakeWindowController();

        using var application = CreateApplication(windows);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var cursor = client.Send(new ControlRequest { Cmd = Command.EventsRead }).Result!.Events!;

        foreach (var windowId in new[] { openId, closedId })
        {
            var response = client.Send(new ControlRequest
            {
                Cmd = Command.WindowRename,
                Target = windowId.ToString("D"),
                Args = new ControlArgs { Name = $"renamed-{windowId:N}" },
            });
            Assert.True(response.Ok, response.Error);
            var events = ReadEvents(client, cursor);
            var changed = Assert.Single(events.Items);
            Assert.Equal(ControlEventKind.TreeChanged, changed.Kind);
            Assert.Equal(windowId.ToString("D").ToUpperInvariant(), changed.Window);
            cursor = events;
        }
    }

    [Fact]
    public void Window_geometry_round_trips_in_points_on_a_scaled_display()
    {
        var display = new LiveDisplayMetrics(
            X: 1920,
            Y: 0,
            Width: 3840,
            Height: 2160,
            Scale: 1.5);

        var moved = LiveWindowGeometry.ClampMove(
            x: 100,
            y: 50,
            windowWidthPixels: 1200,
            windowHeightPixels: 900,
            display);
        var captured = LiveWindowGeometry.Capture(
            moved.X,
            moved.Y,
            widthPixels: 1200,
            heightPixels: 900,
            displayIndex: 1,
            display);

        Assert.Equal((2070, 75), moved);
        Assert.Equal(
            new ControlWindowFrame
            {
                X = 100,
                Y = 50,
                Width = 800,
                Height = 600,
                Display = 1,
            },
            captured);

        var clamped = LiveWindowGeometry.ClampMove(
            x: -5000,
            y: -5000,
            windowWidthPixels: 1200,
            windowHeightPixels: 900,
            display);
        Assert.Equal(display.X - 1200 + 120, clamped.X);
        Assert.Equal(display.Y - 900 + 120, clamped.Y);
    }

    [Fact]
    public async Task Failed_window_new_rolls_back_registry_snapshot_frontmost_and_events()
    {
        var windows = new FakeWindowController { OpenError = new InvalidOperationException("open failed") };
        using var application = CreateApplication(windows);
        var original = Assert.Single(application.Windows);
        var cursor = application.Ring.Read(cursor: null).Batch;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.CreateWindowAsync("failed"));

        Assert.Equal("open failed", error.Message);
        Assert.Equal(new[] { original }, application.Windows);
        Assert.Equal(original.Id, application.ActiveWindowId);
        Assert.Single(Directory.GetFiles(Path.Combine(_stateDir, "windows"), "*.json"));
        Assert.Empty(application.Ring.Read(
            new ControlEventCursor(cursor.Run, cursor.Next)).Batch.Items);
    }

    [Fact]
    public async Task Window_close_drains_an_accepted_dispatch_completion_before_lifecycle_events()
    {
        using var application = CreateApplication();
        var windowId = Assert.Single(application.Windows).Id;
        var window = application.GetOpenWindow(windowId);
        var scopes = new BlockingDispatchScopeFactory();
        window.AttachLive(window.Actions, _ => null, scopes);
        var cursor = application.Ring.Read(cursor: null).Batch;

        var dispatch = window.DispatchAsync(new ControlRequest { Cmd = Command.Tree });
        await scopes.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var close = application.CloseWindowAsync(windowId);

        Assert.False(close.IsCompleted);
        Assert.Empty(application.Ring.Read(
            new ControlEventCursor(cursor.Run, cursor.Next)).Batch.Items);

        scopes.Release.TrySetResult(null);
        Assert.True((await dispatch)!.Ok);
        await close;
        Assert.Equal(
            new[] { ControlEventKind.SessionClosed, ControlEventKind.TreeChanged },
            application.Ring.Read(
                new ControlEventCursor(cursor.Run, cursor.Next)).Batch.Items.Select(item => item.Kind));
    }

    [Fact]
    public void Failed_closed_window_select_rolls_back_to_closed_without_events()
    {
        var openId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        WriteWindowSnapshot(openId, "alpha");
        WriteWindowSnapshot(closedId, "beta");
        WriteIndex(openId,
            new IndexEntry(openId, "open", IsOpen: true),
            new IndexEntry(closedId, "closed", IsOpen: false));
        var windows = new FakeWindowController { OpenError = new InvalidOperationException("open failed") };

        using var application = CreateApplication(windows);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var cursor = client.Send(new ControlRequest { Cmd = Command.EventsRead }).Result!.Events!;
        var response = client.Send(new ControlRequest
        {
            Cmd = Command.WindowSelect,
            Target = closedId.ToString("D"),
        });

        Assert.False(response.Ok);
        Assert.False(application.Windows.Single(window => window.Id == closedId).IsOpen);
        Assert.Throws<KeyNotFoundException>(() => application.GetOpenWindow(closedId));
        Assert.Equal(openId, application.ActiveWindowId);
        Assert.Empty(ReadEvents(client, cursor).Items);
    }

    [Fact]
    public void Unchanged_window_rename_is_success_without_a_structural_event()
    {
        var windowId = Guid.NewGuid();
        WriteWindowSnapshot(windowId, "alpha");
        WriteIndex(windowId, new IndexEntry(windowId, "same", IsOpen: true));
        var windows = new FakeWindowController();

        using var application = CreateApplication(windows);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var cursor = client.Send(new ControlRequest { Cmd = Command.EventsRead }).Result!.Events!;
        var response = client.Send(new ControlRequest
        {
            Cmd = Command.WindowRename,
            Target = windowId.ToString("D"),
            Args = new ControlArgs { Name = " same " },
        });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(windowId.ToString("D").ToUpperInvariant(), response.Result?.Id);
        Assert.Empty(ReadEvents(client, cursor).Items);
    }

    private static ControlEventBatch ReadEvents(
        ControlPipeClient client,
        ControlEventBatch cursor)
    {
        var response = client.Send(new ControlRequest
        {
            Cmd = Command.EventsRead,
            Args = new ControlArgs
            {
                Run = cursor.Run.ToString("D"),
                After = cursor.Next.ToString(),
            },
        });
        Assert.True(response.Ok, response.Error);
        return response.Result!.Events!;
    }

    private LiveApplicationHost CreateApplication(ILiveWindowController? windows = null) =>
        new(new LiveApplicationHostOptions
        {
            StateDir = _stateDir,
            HomeDirectory = _stateDir,
            PipeName = "agterm-window-control-" + Guid.NewGuid().ToString("N"),
            WindowController = windows,
        });

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
        WriteWindowSnapshot(windowId, new Snapshot
        {
            Workspaces =
            [
                new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid(),
                    Name = workspaceName,
                    Sessions = Array.Empty<SessionSnapshot>(),
                },
            ],
        });
    }

    private void WriteWindowSnapshot(Guid windowId, Snapshot snapshot)
    {
        new PersistenceStore(
            Path.Combine(_stateDir, "windows"),
            $"{windowId:D}.json").Save(snapshot);
    }

    private static Snapshot SnapshotWithCommands(string workspaceName, Guid sessionId) => new()
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
                        ForegroundCommand = ["pwsh.exe", "-NoLogo"],
                        RestoreCommand = "echo restored",
                    },
                ],
            },
        ],
    };

    private static Snapshot SnapshotWithSession(string workspaceName, Guid sessionId) => new()
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

    private string WindowSnapshotPath(Guid windowId) =>
        Path.Combine(_stateDir, "windows", $"{windowId:D}.json");

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

    private sealed class FakeWindowController : ILiveWindowController
    {
        public List<Guid> Opened { get; } = new();
        public List<Guid> ActivationRequests { get; } = new();
        public List<Guid> ClosedRequests { get; } = new();
        public List<Guid> PresentationRequests { get; } = new();
        public List<(Guid WindowId, int Width, int Height)> ResizeRequests { get; } = new();
        public List<(Guid WindowId, int X, int Y, int? Display)> MoveRequests { get; } = new();
        public List<Guid> ZoomRequests { get; } = new();
        public List<Guid> FullscreenRequests { get; } = new();
        public Dictionary<Guid, LiveWindowPresentation> Presentations { get; } = new();
        public int DisplayCount { get; set; } = 1;
        public Exception? OpenError { get; set; }
        public Action<Guid>? Activated { get; set; }
        public Func<Guid, Task>? Closed { get; set; }

        public Task OpenWindowAsync(Guid windowId, CancellationToken cancellationToken)
        {
            Opened.Add(windowId);
            if (OpenError is not null)
                return Task.FromException(OpenError);
            Activated?.Invoke(windowId);
            return Task.CompletedTask;
        }

        public Task ActivateWindowAsync(Guid windowId, CancellationToken cancellationToken)
        {
            ActivationRequests.Add(windowId);
            Activated?.Invoke(windowId);
            return Task.CompletedTask;
        }

        public async Task CloseWindowAsync(Guid windowId, CancellationToken cancellationToken)
        {
            ClosedRequests.Add(windowId);
            if (Closed is not null)
                await Closed(windowId);
        }

        public Task<LiveWindowPresentation?> CaptureWindowAsync(
            Guid windowId,
            CancellationToken cancellationToken)
        {
            PresentationRequests.Add(windowId);
            return Task.FromResult(Presentations.GetValueOrDefault(windowId));
        }

        public Task<int> GetDisplayCountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DisplayCount);

        public Task ResizeWindowAsync(
            Guid windowId,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            ResizeRequests.Add((windowId, width, height));
            var presentation = Presentations[windowId];
            Presentations[windowId] = presentation with
            {
                Geometry = presentation.Geometry with { Width = width, Height = height },
            };
            return Task.CompletedTask;
        }

        public Task<LiveWindowMoveResult> MoveWindowAsync(
            Guid windowId,
            int x,
            int y,
            int? display,
            CancellationToken cancellationToken)
        {
            MoveRequests.Add((windowId, x, y, display));
            if (display is < 0 || display >= DisplayCount)
                return Task.FromResult(new LiveWindowMoveResult(false, DisplayCount));
            var presentation = Presentations[windowId];
            Presentations[windowId] = presentation with
            {
                Geometry = presentation.Geometry with
                {
                    X = x,
                    Y = y,
                    Display = display ?? presentation.Geometry.Display,
                },
            };
            return Task.FromResult(new LiveWindowMoveResult(true, DisplayCount));
        }

        public Task ToggleZoomAsync(Guid windowId, CancellationToken cancellationToken)
        {
            ZoomRequests.Add(windowId);
            var presentation = Presentations[windowId];
            Presentations[windowId] = presentation with { Zoomed = !presentation.Zoomed };
            return Task.CompletedTask;
        }

        public Task ToggleFullscreenAsync(Guid windowId, CancellationToken cancellationToken)
        {
            FullscreenRequests.Add(windowId);
            var presentation = Presentations[windowId];
            Presentations[windowId] = presentation with { Fullscreen = !presentation.Fullscreen };
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingDispatchScopeFactory : IControlDispatchScopeFactory
    {
        public TaskCompletionSource<object?> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IControlDispatchScope Begin() => new Scope(this);

        private sealed class Scope(BlockingDispatchScopeFactory owner) : IControlDispatchScope
        {
            public async Task CompleteAsync(CancellationToken cancellationToken)
            {
                owner.Entered.TrySetResult(null);
                await owner.Release.Task.WaitAsync(cancellationToken);
            }

            public void Dispose()
            {
            }
        }
    }
}
