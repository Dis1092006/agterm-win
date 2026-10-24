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

    // Ported from upstream ControlSidebarStatusUITests.testNotificationBadgeToggleHidesAndShowsBadge.
    // Covers render policy and host state, not rendered XAML or OS activation.
    [Fact]
    public void Notification_badge_toggle_hides_and_shows_badge_without_losing_unseen_count()
    {
        using var application = CreateApplication();
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var windowId = Assert.Single(application.Windows).Id;
        var seeded = application.GetOpenWindow(windowId).Store.ActiveSession!.Id;
        var created = client.Send(new ControlRequest { Cmd = Command.SessionNew });
        Assert.True(created.Ok, created.Error);
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.Notify,
            Target = seeded.ToString("D"),
            Args = new ControlArgs { Body = "first" },
        }).Ok);
        var settings = new AppSettings();
        var initial = BadgeSession(client, windowId, seeded);
        Assert.Equal(1, initial.Unseen);
        Assert.False(initial.Active);
        Assert.True(SidebarBadge.IsShown(initial.Unseen ?? 0, settings));

        settings = settings with { NotificationBadgeEnabled = false };
        Assert.False(SidebarBadge.IsShown(BadgeSession(client, windowId, seeded).Unseen ?? 0, settings));
        Assert.Equal(1, BadgeSession(client, windowId, seeded).Unseen);
        settings = settings with { NotificationBadgeEnabled = true };
        Assert.True(SidebarBadge.IsShown(BadgeSession(client, windowId, seeded).Unseen ?? 0, settings));
        Assert.Equal(1, BadgeSession(client, windowId, seeded).Unseen);

        settings = settings with { NotificationBadgeEnabled = false };
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.Notify,
            Target = seeded.ToString("D"),
            Args = new ControlArgs { Body = "while hidden" },
        }).Ok);
        Assert.Equal(2, BadgeSession(client, windowId, seeded).Unseen);
        Assert.False(SidebarBadge.IsShown(BadgeSession(client, windowId, seeded).Unseen ?? 0, settings));
        settings = settings with { NotificationBadgeEnabled = true };
        Assert.True(SidebarBadge.IsShown(BadgeSession(client, windowId, seeded).Unseen ?? 0, settings));
        Assert.False(SidebarBadge.IsShown(0, settings));
        Assert.True(BadgeSession(client, windowId, Guid.Parse(created.Result!.Id!)).Active);
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "AgTerm.App", "SidebarRowViewModel.cs"));
        Assert.Contains("SidebarBadge.IsShown(_row.Unseen, _settings)", source, StringComparison.Ordinal);
    }

    // Ported from upstream ControlSidebarStatusUITests.testRefocusingWindowClearsOnScreenSessionBadge.
    // Covers activation policy and host state, not the OS activation event or rendered XAML.
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public async Task Refocusing_window_clears_shown_session_badge_without_a_session_switch(
        bool sameWindow, bool split, bool zoomed)
    {
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var firstId = Assert.Single(application.Windows).Id;
        var first = application.GetOpenWindow(firstId);
        var seeded = first.Store.ActiveSession!.Id;
        first.RunLocked(() =>
        {
            first.Store.ActiveSession!.Surface = new BadgeSurface("main");
            first.Store.ActiveSession.SplitSurface = new BadgeSurface("split");
            first.Store.ActiveSession.SplitFocused = split;
            if (zoomed)
                first.Actions.Zoom.Set(ControlToggleMode.On, new TerminalZoomTarget.Session(
                    seeded, split ? TerminalZoomSurface.Split : TerminalZoomSurface.Primary));
        });
        application.ReportAcceptedPane(firstId, split ? "split" : "main");
        if (!sameWindow)
            Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowNew }).Ok);
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.Notify,
            Target = seeded.ToString("D"),
            Args = new ControlArgs { Body = "unseen" },
        }).Ok);
        Assert.Equal(1, BadgeSession(client, firstId, seeded).Unseen);
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.SessionStatus,
            Target = seeded.ToString("D"),
            Args = new ControlArgs { Status = "blocked", AutoReset = true },
        }).Ok);
        var selectionChanges = 0;
        first.Store.SelectionChanged += () => selectionChanges++;

        await application.ReportActivatedAsync(firstId).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(firstId, application.ActiveWindowId);
        Assert.Null(BadgeSession(client, firstId, seeded).Unseen);
        Assert.True(BadgeSession(client, firstId, seeded).Active);
        Assert.Equal("blocked", BadgeSession(client, firstId, seeded).Status);
        Assert.Equal(0, selectionChanges);
    }

    // Ported from upstream ControlSidebarStatusUITests.testRefocusingWindowKeepsNonFocusedSessionBadge.
    // Covers activation policy and host state, not the OS activation event or rendered XAML.
    [Fact]
    public async Task Refocusing_window_keeps_nonfocused_session_badge()
    {
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var firstId = Assert.Single(application.Windows).Id;
        var first = application.GetOpenWindow(firstId);
        var seeded = first.Store.ActiveSession!.Id;
        first.RunLocked(() => first.Store.ActiveSession!.Surface = new BadgeSurface("main"));
        application.ReportAcceptedPane(firstId, "main");
        var created = client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { NoSelect = true },
        });
        Assert.True(created.Ok, created.Error);
        var other = Guid.Parse(created.Result!.Id!);
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.Notify,
            Target = other.ToString("D"),
            Args = new ControlArgs { Body = "not shown" },
        }).Ok);
        Assert.Equal(1, BadgeSession(client, firstId, other).Unseen);
        var second = client.Send(new ControlRequest { Cmd = Command.WindowNew });
        Assert.True(second.Ok, second.Error);
        var secondId = Guid.Parse(second.Result!.Id!);
        var secondSession = application.GetOpenWindow(secondId).Store.ActiveSession!.Id;
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.Notify,
            Target = secondSession.ToString("D"),
            Args = new ControlArgs { Body = "other window" },
        }).Ok);
        var selectionChanges = 0;
        first.Store.SelectionChanged += () => selectionChanges++;

        await application.ReportActivatedAsync(firstId).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(firstId, application.ActiveWindowId);
        Assert.True(BadgeSession(client, firstId, seeded).Active);
        Assert.Equal(1, BadgeSession(client, firstId, other).Unseen);
        Assert.Equal(1, BadgeSession(client, secondId, secondSession).Unseen);
        Assert.Equal(0, selectionChanges);
    }

    [Theory]
    [InlineData("overlay")]
    [InlineData("scratch")]
    [InlineData("quick")]
    [InlineData(null)]
    public async Task Activation_only_marks_the_accepted_main_or_split_pane_owner_seen(string? acceptedPane)
    {
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var windowId = Assert.Single(application.Windows).Id;
        var window = application.GetOpenWindow(windowId);
        var session = window.Store.ActiveSession!;
        window.RunLocked(() =>
        {
            session.Surface = new BadgeSurface("main");
            session.SplitSurface = new BadgeSurface("split");
            session.OverlaySurface = new BadgeSurface("overlay");
            session.ScratchSurface = new BadgeSurface("scratch");
            session.OverlayActive = acceptedPane == "overlay";
            session.ScratchActive = acceptedPane == "scratch";
        });
        application.ReportAcceptedPane(windowId, acceptedPane);
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.Notify,
            Target = session.Id.ToString("D"),
            Args = new ControlArgs { Body = "main is covered" },
        }).Ok);

        await application.ReportActivatedAsync(windowId).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, BadgeSession(client, windowId, session.Id).Unseen);
        Assert.True(BadgeSession(client, windowId, session.Id).Active);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Activation_clears_background_focused_pane_owner_not_selected_session(
        bool dashboard, bool split)
    {
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var firstId = Assert.Single(application.Windows).Id;
        var first = application.GetOpenWindow(firstId);
        var selected = first.Store.ActiveSession!.Id;
        var created = client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { NoSelect = true },
        });
        Assert.True(created.Ok, created.Error);
        var other = Guid.Parse(created.Result!.Id!);
        first.RunLocked(() =>
        {
            first.Store.ActiveSession!.Surface = new BadgeSurface("selected-main");
            var background = first.Store.SessionWithId(other)!;
            background.Surface = new BadgeSurface("background-main");
            background.SplitSurface = new BadgeSurface("background-split");
            var surface = split ? TerminalZoomSurface.Split : TerminalZoomSurface.Primary;
            if (dashboard)
                first.Actions.Dashboard.Open(
                    [new DashboardMember(other, surface), new DashboardMember(selected, TerminalZoomSurface.Primary)],
                    new DashboardFontMode.Untouched());
            else
                first.Actions.Zoom.Set(ControlToggleMode.On, new TerminalZoomTarget.Session(other, surface));
        });
        application.ReportAcceptedPane(firstId, split ? "background-split" : "background-main");
        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowNew }).Ok);
        foreach (var sessionId in new[] { selected, other })
        {
            Assert.True(client.Send(new ControlRequest
            {
                Cmd = Command.Notify,
                Target = sessionId.ToString("D"),
                Args = new ControlArgs { Body = "unseen" },
            }).Ok);
            Assert.Equal(1, BadgeSession(client, firstId, sessionId).Unseen);
        }
        var selectionChanges = 0;
        first.Store.SelectionChanged += () => selectionChanges++;

        await application.ReportActivatedAsync(firstId).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(firstId, application.ActiveWindowId);
        Assert.Null(BadgeSession(client, firstId, other).Unseen);
        Assert.Equal(1, BadgeSession(client, firstId, selected).Unseen);
        Assert.True(BadgeSession(client, firstId, selected).Active);
        Assert.False(BadgeSession(client, firstId, other).Active);
        Assert.Equal(0, selectionChanges);
    }

    [Fact]
    public async Task Window_select_awaits_badge_clear_without_an_os_activation_callback()
    {
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var firstId = Assert.Single(application.Windows).Id;
        var first = application.GetOpenWindow(firstId);
        var sessionId = first.Store.ActiveSession!.Id;
        first.RunLocked(() => first.Store.ActiveSession!.Surface = new BadgeSurface("main"));
        application.ReportAcceptedPane(firstId, "main");
        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowNew }).Ok);
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.Notify,
            Target = sessionId.ToString("D"),
            Args = new ControlArgs { Body = "unseen" },
        }).Ok);
        Assert.Equal(1, BadgeSession(client, firstId, sessionId).Unseen);

        var selected = await Task.Run(() => client.Send(new ControlRequest
        {
            Cmd = Command.WindowSelect,
            Target = firstId.ToString("D"),
        })).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(selected.Ok, selected.Error);
        Assert.Null(BadgeSession(client, firstId, sessionId).Unseen);
    }

    private static ControlSessionNode BadgeSession(ControlPipeClient client, Guid windowId, Guid sessionId)
    {
        var response = client.Send(new ControlRequest
        {
            Cmd = Command.Tree,
            Args = new ControlArgs { Window = windowId.ToString("D") },
        });
        Assert.True(response.Ok, response.Error);
        return Assert.Single(response.Result!.Tree!.Workspaces.SelectMany(workspace => workspace.Sessions),
            session => Guid.Parse(session.Id) == sessionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Activation_waits_off_ui_and_ignores_a_replaced_accepted_layout(bool layoutChanges)
    {
        using var application = CreateApplication();
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var windowId = Assert.Single(application.Windows).Id;
        var window = application.GetOpenWindow(windowId);
        var sessionId = window.Store.ActiveSession!.Id;
        window.RunLocked(() => window.Store.ActiveSession!.Surface = new BadgeSurface("main"));
        application.ReportAcceptedPane(windowId, "main");
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.Notify,
            Target = sessionId.ToString("D"),
            Args = new ControlArgs { Body = "unseen" },
        }).Ok);
        using var acquired = new ManualResetEventSlim();
        using var returned = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Factory.StartNew(() => window.RunLocked(() =>
        {
            acquired.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)), "Store gate was not released.");
        }), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task? caller = null;
        Task? activation = null;
        Exception? primaryFailure = null;
        try
        {
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)), "Store gate was not acquired.");
            caller = Task.Factory.StartNew(() =>
            {
                var pending = application.ReportActivatedAsync(windowId);
                activation = pending;
                returned.Set();
                return pending;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            Assert.True(returned.Wait(TimeSpan.FromSeconds(5)),
                "Activation did not return while the store gate was held.");
            Assert.NotNull(activation);
            Assert.False(activation.IsCompleted);
            if (layoutChanges)
                application.ReportAcceptedPane(windowId, "overlay");
        }
        catch (Exception error)
        {
            primaryFailure = error;
            throw;
        }
        finally
        {
            release.Set();
            try
            {
                await Task.WhenAll(holder, caller ?? Task.CompletedTask)
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception cleanupFailure) when (primaryFailure is not null)
            {
                throw new AggregateException("Activation test and cleanup both failed.",
                    primaryFailure, cleanupFailure);
            }
        }
        Assert.Equal(layoutChanges ? 1 : (int?)null, BadgeSession(client, windowId, sessionId).Unseen);
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
        windows.Activated = application.ReportActivatedAsync;
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
    public void Window_select_takes_frontmost_without_an_activation_report()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var closedId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteWindowSnapshot(secondId, "beta");
        WriteWindowSnapshot(closedId, "gamma");
        WriteIndex(firstId,
            new IndexEntry(firstId, "first", IsOpen: true),
            new IndexEntry(secondId, "second", IsOpen: true),
            new IndexEntry(closedId, "closed", IsOpen: false));
        var windows = new FakeWindowController(); // Activated stays null: the OS never reports activation

        using var application = CreateApplication(windows);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowSelect, Target = secondId.ToString("D") }).Ok);
        Assert.Equal(secondId, application.ActiveWindowId);

        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowSelect, Target = closedId.ToString("D") }).Ok);
        Assert.Equal(closedId, application.ActiveWindowId);
        var list = client.Send(new ControlRequest { Cmd = Command.WindowList });
        Assert.Equal(new[] { false, false, true }, list.Result!.Windows!.Select(w => w.Active));
        var index = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(_stateDir, "windows.json")))!;
        Assert.Equal(closedId, Guid.Parse(index["frontmost"]!.GetValue<string>()));
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
    public void Window_delete_removes_saved_pane_output_of_the_closed_window_sessions()
    {
        var keptId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        WriteWindowSnapshot(keptId, "alpha");
        WriteWindowSnapshot(deletedId, SnapshotWithCommands("beta", sessionId));
        WriteIndex(keptId,
            new IndexEntry(keptId, "kept", IsOpen: true),
            new IndexEntry(deletedId, "deleted", IsOpen: false));
        ScrollbackStorage.Write(sessionId, StatusPane.Left, "saved", _stateDir);
        var path = ScrollbackStorage.FilePath(sessionId, StatusPane.Left, _stateDir);

        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.WindowDelete,
            Target = deletedId.ToString("D"),
        });

        Assert.True(response.Ok, response.Error);
        Assert.False(File.Exists(path));
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
        windows.Activated = application.ReportActivatedAsync;
        application.Start();
        var response = new ControlPipeClient(application.PipeName).Send(new ControlRequest
        {
            Cmd = Command.WindowSelect,
            Target = closedId.ToString("D"),
        });

        Assert.True(response.Ok, response.Error);
        var session = application.GetOpenWindow(closedId).Store.SessionWithId(closedSessionId)!;
        Assert.Null(session.PendingRestoreCommand);
        Assert.Null(session.ForegroundCommand);
    }

    [Fact]
    public void Multi_window_lifecycle_restores_order_names_open_set_and_independent_stores()
    {
        Guid firstId;
        Guid secondId;
        var windows = new FakeWindowController();
        using (var application = CreateApplication(windows))
        {
            windows.Activated = application.ReportActivatedAsync;
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

    // Ported from upstream AppStoreEventTests.swift runtimeCloseAndReopenKeepsCursorContinuity.
    [Fact]
    public void Runtime_close_and_reopen_keeps_cursor_continuity()
    {
        var windows = new FakeWindowController();
        using var application = CreateApplication(windows);
        windows.Activated = application.ReportActivatedAsync;
        windows.Closed = id => application.CloseWindowAsync(id);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var firstId = Assert.Single(application.Windows).Id;
        var first = application.GetOpenWindow(firstId);
        var session = Assert.Single(Assert.Single(first.Store.Workspaces).Sessions);
        application.FlushTreeEvents();
        var anchor = client.Send(new ControlRequest { Cmd = Command.EventsRead }).Result!.Events!;

        first.RunLocked(() => first.Store.SetAgentIndicator(
            new AgentIndicator(AgentStatus.Active), session.Id));
        first.Save();
        var created = client.Send(new ControlRequest
        {
            Cmd = Command.WindowNew,
            Args = new ControlArgs { Name = "keep-open" },
        });
        Assert.True(created.Ok, created.Error);
        var secondId = Guid.Parse(created.Result!.Id!);
        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.WindowClose,
            Target = firstId.ToString("D"),
        }).Ok);
        var closed = ReadEvents(client, anchor);
        Assert.Equal(anchor.Run, closed.Run);

        Assert.True(client.Send(new ControlRequest
        {
            Cmd = Command.WindowSelect,
            Target = firstId.ToString("D"),
        }).Ok);
        var reopened = application.GetOpenWindow(firstId);
        var restoredSession = Assert.Single(Assert.Single(reopened.Store.Workspaces).Sessions);
        Assert.Equal(session.Id, restoredSession.Id);
        var added = client.Send(new ControlRequest { Cmd = Command.SessionNew });
        Assert.True(added.Ok, added.Error);
        application.FlushTreeEvents();
        var afterReopen = ReadEvents(client, closed);
        Assert.Equal(anchor.Run, afterReopen.Run);
        var events = closed.Items.Concat(afterReopen.Items).ToArray();

        // tree.changed also consumes sequence numbers; verify continuity before filtering it.
        Assert.Equal(
            Enumerable.Range(1, events.Length).Select(offset => anchor.Next + (ulong)offset),
            events.Select(item => item.Seq));
        Assert.Equal(events[^1].Seq, afterReopen.Next);
        var lifecycle = events.Where(item => item.Kind != ControlEventKind.TreeChanged).ToArray();
        Assert.Equal(new[]
        {
            ControlEventKind.Status,
            ControlEventKind.SessionCreated,
            ControlEventKind.SessionClosed,
            ControlEventKind.SessionCreated,
            ControlEventKind.SessionCreated,
        }, lifecycle.Select(item => item.Kind));
        Assert.Equal(new[] { firstId, secondId, firstId, firstId, firstId }
            .Select(id => id.ToString("D").ToUpperInvariant()),
            lifecycle.Select(item => item.Window));
        Assert.All(lifecycle.Take(4).Where(item => item.Window == firstId.ToString("D").ToUpperInvariant()),
            item => Assert.Equal(session.Id.ToString("D").ToUpperInvariant(), item.Session));
        Assert.Equal(added.Result!.Id, lifecycle[^1].Session);
        Assert.Empty(ReadEvents(client, afterReopen).Items);
    }

    // Ported from upstream AppStoreEventTests.swift runtimeWindowCloseAndReopenEmitBalancedSessionEdges
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
        windows.Activated = application.ReportActivatedAsync;
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
        application.FlushTreeEvents();
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

    // Ported from upstream AppStoreEventTests.swift deletingClosedWindowStillEmitsStructuralInvalidation
    [Fact]
    public void Window_new_and_closed_delete_emit_application_lifecycle_events()
    {
        var windows = new FakeWindowController();
        using var application = CreateApplication(windows);
        windows.Activated = application.ReportActivatedAsync;
        windows.Closed = id => application.CloseWindowAsync(id);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var anchor = client.Send(new ControlRequest { Cmd = Command.EventsRead }).Result!.Events!;

        var created = client.Send(new ControlRequest { Cmd = Command.WindowNew });
        Assert.True(created.Ok, created.Error);
        var createdId = Guid.Parse(created.Result!.Id!);
        application.FlushTreeEvents();
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
            application.FlushTreeEvents();
            var events = ReadEvents(client, cursor);
            var changed = Assert.Single(events.Items);
            Assert.Equal(ControlEventKind.TreeChanged, changed.Kind);
            Assert.Equal(windowId.ToString("D").ToUpperInvariant(), changed.Window);
            cursor = events;
        }
    }

    [Fact]
    public void DeletingOpenWindowEmitsClosedEdgeAndStructuralInvalidation()
    {
        // Ported from upstream AppStoreEventTests.swift deletingOpenWindowEmitsClosedEdgeAndStructuralInvalidation.
        var keptId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        WriteWindowSnapshot(keptId, "keep");
        WriteWindowSnapshot(deletedId, SnapshotWithSession("delete", sessionId));
        WriteIndex(keptId,
            new IndexEntry(keptId, "keep", IsOpen: true),
            new IndexEntry(deletedId, "delete", IsOpen: true));
        var windows = new FakeWindowController();
        using var application = CreateApplication(windows);
        windows.Closed = id => application.CloseWindowAsync(id);
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var anchor = application.Ring.Read(cursor: null).Batch;

        var response = client.Send(new ControlRequest
        {
            Cmd = Command.WindowDelete,
            Target = deletedId.ToString("D"),
        });

        Assert.True(response.Ok, response.Error);
        Assert.DoesNotContain(application.Windows, window => window.Id == deletedId);
        var events = ReadEvents(client, anchor);
        Assert.Equal(new[] { ControlEventKind.SessionClosed, ControlEventKind.TreeChanged },
            events.Items.Select(item => item.Kind));
        Assert.All(events.Items, item => Assert.Equal(deletedId.ToString("D").ToUpperInvariant(), item.Window));
        Assert.Equal(sessionId.ToString("D").ToUpperInvariant(), events.Items[0].Session);
        application.FlushTreeEvents();
        Assert.Empty(ReadEvents(client, events).Items);
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

    private static ControlRequest OpenPickRequest(string? window = null, bool follow = false) => new()
    {
        Cmd = Command.PickOpen,
        Args = new ControlArgs
        {
            Items = new[] { new ControlPickItem { Id = "a", Label = "A" } },
            Window = window,
            Follow = follow ? true : null,
        },
    };

    [Fact]
    public void Pick_result_finds_the_pick_by_id_after_the_frontmost_window_changes()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var id = client.Send(OpenPickRequest()).Result!.Id!;
        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowSelect, Target = secondId.ToString("D") }).Ok);

        var result = client.Send(new ControlRequest { Cmd = Command.PickResult, Target = id });
        Assert.Equal(ControlPickResult.Pending(), result.Result!.Pick);
    }

    [Fact]
    public async Task Closing_the_window_cancels_its_pick_and_keeps_the_result_readable()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var id = client.Send(OpenPickRequest(window: firstId.ToString("D"))).Result!.Id!;

        await application.CloseWindowAsync(firstId);

        Assert.Equal(ControlPickResult.Cancelled(), client.Send(new ControlRequest { Cmd = Command.PickResult, Target = id }).Result!.Pick);
        Assert.Equal(ControlPickResult.Cancelled(), client.Send(new ControlRequest
        {
            Cmd = Command.PickResult, Target = id, Args = new ControlArgs { Window = firstId.ToString("D") },
        }).Result!.Pick);
    }

    [Fact]
    public void Disposing_the_application_cancels_every_pending_pick()
    {
        var firstId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true));
        var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        client.Send(OpenPickRequest());
        var pick = application.GetOpenWindow(firstId).Pick;

        application.Dispose();

        Assert.Null(pick.Pending);
        Assert.True(pick.IsClosed, "a pick.open still in flight must find the window closed.");
    }

    [Fact]
    public void Pick_open_with_follow_takes_frontmost()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        Assert.True(client.Send(OpenPickRequest(window: secondId.ToString("D"), follow: true)).Ok);

        Assert.Equal(secondId, application.ActiveWindowId);
    }

    [Fact]
    public void Pick_open_with_follow_still_answers_when_the_activation_is_refused()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController { ActivateError = new InvalidOperationException("refused") });
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var response = client.Send(OpenPickRequest(window: secondId.ToString("D"), follow: true));

        Assert.True(response.Ok, response.Error);
        Assert.NotNull(response.Result!.Id);
        Assert.Equal(secondId, application.ActiveWindowId);
    }

    [Fact]
    public async Task Pick_open_with_no_open_window_answers_no_open_window()
    {
        var onlyId = Guid.NewGuid();
        WriteWindowSnapshot(onlyId, "alpha");
        WriteIndex(onlyId, new IndexEntry(onlyId, "only", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        await application.CloseWindowAsync(onlyId);

        Assert.Equal("no open window", new ControlPipeClient(application.PipeName).Send(OpenPickRequest()).Error);
    }

    private static ControlRequest OpenAskRequest(string style = "gui", string? window = null, string? target = null, bool follow = false) => new()
    {
        Cmd = Command.AskOpen,
        Target = target,
        Args = new ControlArgs
        {
            Title = "Continue?",
            Buttons = new[] { new ControlAskButton { Id = "yes", Label = "Yes" } },
            Style = style,
            Window = window,
            Follow = follow ? true : null,
        },
    };

    private static ControlAskResult? AskResult(ControlPipeClient client, string id, string? window = null) =>
        client.Send(new ControlRequest
        {
            Cmd = Command.AskResult, Target = id,
            Args = window is null ? null : new ControlArgs { Window = window },
        }).Result?.Ask;

    private static string FirstSessionId(ControlPipeClient client, Guid windowId) =>
        client.Send(new ControlRequest { Cmd = Command.Tree, Args = new ControlArgs { Window = windowId.ToString("D") } })
            .Result!.Tree!.Workspaces.SelectMany(w => w.Sessions).First().Id;

    [Fact]
    public void Ask_result_finds_the_ask_by_id_after_the_frontmost_window_changes()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        var id = client.Send(OpenAskRequest()).Result!.Id!;
        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowSelect, Target = secondId.ToString("D") }).Ok);

        Assert.Equal(ControlAskResult.Pending(), AskResult(client, id));
        Assert.Equal($"unknown ask: {id}", client.Send(new ControlRequest
        {
            Cmd = Command.AskResult, Target = id, Args = new ControlArgs { Window = secondId.ToString("D") },
        }).Error);
    }

    [Fact]
    public void Ask_cancel_reaches_a_terminal_ask_in_a_background_window()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshotWithSession(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var opened = client.Send(OpenAskRequest("terminal", window: firstId.ToString("D")));
        Assert.True(opened.Ok, opened.Error);
        var id = opened.Result!.Id!;
        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowSelect, Target = secondId.ToString("D") }).Ok);

        Assert.Equal($"unknown ask: {id}", client.Send(new ControlRequest
        {
            Cmd = Command.AskCancel, Target = id, Args = new ControlArgs { Window = secondId.ToString("D") },
        }).Error);
        Assert.True(client.Send(new ControlRequest { Cmd = Command.AskCancel, Target = id }).Ok);

        Assert.Equal(ControlAskResult.Cancelled(), AskResult(client, id));
        var tree = client.Send(new ControlRequest { Cmd = Command.Tree, Args = new ControlArgs { Window = firstId.ToString("D") } }).Result!.Tree!;
        Assert.All(tree.Workspaces.SelectMany(w => w.Sessions), session => Assert.Null(session.Ask));
    }

    [Fact]
    public void Ask_open_with_a_session_target_routes_to_the_owning_window()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshotWithSession(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var session = FirstSessionId(client, firstId);
        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowSelect, Target = secondId.ToString("D") }).Ok);

        var opened = client.Send(OpenAskRequest("terminal", target: session));

        Assert.True(opened.Ok, opened.Error);
        Assert.Equal(ControlAskResult.Pending(), AskResult(client, opened.Result!.Id!, firstId.ToString("D")));
    }

    [Fact]
    public void Hud_commands_with_a_session_target_route_to_the_owning_window()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshotWithSession(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var session = FirstSessionId(client, firstId);
        Assert.True(client.Send(new ControlRequest { Cmd = Command.WindowSelect, Target = secondId.ToString("D") }).Ok);
        ControlHudNode? Hud() =>
            client.Send(new ControlRequest { Cmd = Command.Tree, Args = new ControlArgs { Window = firstId.ToString("D") } })
                .Result!.Tree!.Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == session).Hud;

        var opened = client.Send(new ControlRequest
        {
            Cmd = Command.SessionHudOpen, Target = session, Args = new ControlArgs { Message = "building" },
        });
        Assert.True(opened.Ok, opened.Error);
        Assert.Equal(session, opened.Result!.Id);
        Assert.Equal("building", Hud()?.Message);

        var updated = client.Send(new ControlRequest
        {
            Cmd = Command.SessionHudUpdate, Target = session, Args = new ControlArgs { Message = "done" },
        });
        Assert.True(updated.Ok, updated.Error);
        Assert.Equal("done", Hud()?.Message);

        var closed = client.Send(new ControlRequest { Cmd = Command.SessionHudClose, Target = session });
        Assert.True(closed.Ok, closed.Error);
        Assert.Null(Hud());
        Assert.Equal(secondId, application.ActiveWindowId);
    }

    [Fact]
    public async Task Closing_the_window_cancels_its_asks_and_keeps_the_results_readable()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshotWithSession(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var gui = client.Send(OpenAskRequest(window: firstId.ToString("D"))).Result!.Id!;
        var terminal = client.Send(OpenAskRequest("terminal", window: firstId.ToString("D"))).Result!.Id!;

        await application.CloseWindowAsync(firstId);

        foreach (var id in new[] { gui, terminal })
        {
            Assert.Equal(ControlAskResult.Cancelled(), AskResult(client, id));
            Assert.Equal(ControlAskResult.Cancelled(), AskResult(client, id, firstId.ToString("D")));
            Assert.True(client.Send(new ControlRequest { Cmd = Command.AskCancel, Target = id }).Ok);
        }
    }

    [Fact]
    public void Disposing_the_application_cancels_every_pending_ask()
    {
        var firstId = Guid.NewGuid();
        WriteWindowSnapshotWithSession(firstId, "alpha");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true));
        var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var gui = client.Send(OpenAskRequest()).Result!.Id!;
        var terminal = client.Send(OpenAskRequest("terminal")).Result!.Id!;
        var window = application.GetOpenWindow(firstId);

        application.Dispose();

        Assert.Null(window.Pick.PendingAsk);
        Assert.Equal(ControlAskResult.Cancelled(), window.Pick.Asks.Find(gui)!.Result);
        Assert.Equal(ControlAskResult.Cancelled(), window.Pick.Asks.Find(terminal)!.Result);
    }

    [Fact]
    public async Task Closing_the_window_takes_its_huds_down_and_cancels_their_timers()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshotWithSession(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var window = application.GetOpenWindow(firstId);
        var timer = new HudTimerSpy();
        window.RunLocked(() => window.Store.HudAutoHideScheduler = (_, _) => timer);
        var opened = client.Send(new ControlRequest
        {
            Cmd = Command.SessionHudOpen,
            Args = new ControlArgs { Message = "deploying", HideAfter = 30, Window = firstId.ToString("D") },
        });
        Assert.True(opened.Ok, opened.Error);
        var session = window.Store.SessionWithId(Guid.Parse(opened.Result!.Id!))!;

        await application.CloseWindowAsync(firstId);

        Assert.False(session.HudActive);
        Assert.True(timer.Disposed);
    }

    [Fact]
    public void Disposing_the_application_takes_every_hud_down()
    {
        var firstId = Guid.NewGuid();
        WriteWindowSnapshotWithSession(firstId, "alpha");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true));
        var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);
        var opened = client.Send(new ControlRequest { Cmd = Command.SessionHudOpen, Args = new ControlArgs { Message = "working" } });
        Assert.True(opened.Ok, opened.Error);
        var session = application.GetOpenWindow(firstId).Store.SessionWithId(Guid.Parse(opened.Result!.Id!))!;

        application.Dispose();

        Assert.False(session.HudActive);
    }

    private sealed class HudTimerSpy : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Ask_open_with_follow_takes_frontmost()
    {
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        WriteWindowSnapshot(firstId, "alpha"); WriteWindowSnapshot(secondId, "beta");
        WriteIndex(firstId, new IndexEntry(firstId, "first", IsOpen: true), new IndexEntry(secondId, "second", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        var client = new ControlPipeClient(application.PipeName);

        Assert.True(client.Send(OpenAskRequest(window: secondId.ToString("D"), follow: true)).Ok);

        Assert.Equal(secondId, application.ActiveWindowId);
    }

    [Fact]
    public async Task Ask_open_with_no_open_window_answers_no_open_window()
    {
        var onlyId = Guid.NewGuid();
        WriteWindowSnapshot(onlyId, "alpha");
        WriteIndex(onlyId, new IndexEntry(onlyId, "only", IsOpen: true));
        using var application = CreateApplication(new FakeWindowController());
        application.Start();
        await application.CloseWindowAsync(onlyId);

        Assert.Equal("no open window", new ControlPipeClient(application.PipeName).Send(OpenAskRequest()).Error);
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

    private void WriteWindowSnapshotWithSession(Guid windowId, string workspaceName)
    {
        var sessionId = Guid.NewGuid();
        WriteWindowSnapshot(windowId, new Snapshot
        {
            SelectedSessionId = sessionId,
            Workspaces =
            [
                new WorkspaceSnapshot
                {
                    Id = Guid.NewGuid(),
                    Name = workspaceName,
                    Sessions = [new SessionSnapshot { Id = sessionId, Cwd = Path.GetTempPath() }],
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

    private sealed class BadgeSurface(string token) : IHeadlessSurface
    {
        public string? PaneToken => token;
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => string.Empty;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
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
        public Exception? ActivateError { get; set; }
        public Func<Guid, Task>? Activated { get; set; }
        public Func<Guid, Task>? Closed { get; set; }

        public Task OpenWindowAsync(Guid windowId, CancellationToken cancellationToken)
        {
            Opened.Add(windowId);
            if (OpenError is not null)
                return Task.FromException(OpenError);
            return Activated?.Invoke(windowId) ?? Task.CompletedTask;
        }

        public Task ActivateWindowAsync(Guid windowId, CancellationToken cancellationToken)
        {
            ActivationRequests.Add(windowId);
            if (ActivateError is not null)
                return Task.FromException(ActivateError);
            return Activated?.Invoke(windowId) ?? Task.CompletedTask;
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
