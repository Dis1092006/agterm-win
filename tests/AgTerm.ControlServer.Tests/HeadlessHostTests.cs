using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

/// <summary>
/// End-to-end tests for the headless control host: real requests over a REAL named pipe via
/// ControlPipeClient into the dispatcher and the live AppStore. Each test gets its own host with a
/// unique pipe name and an isolated temp state dir (nothing reads AGTERM_* from the environment —
/// the pipe name and state dir are passed explicitly).
/// </summary>
public sealed class HeadlessHostTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessHostTests()
    {
        _stateDir = Path.Combine(Path.GetTempPath(), "agterm-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateDir);
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
        });
        _host.Start();
        _client = new ControlPipeClient(_host.PipeName);
    }

    public void Dispose()
    {
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ControlResponse Send(ControlRequest request) => _client.Send(request);

    private static ControlRequest OpenPick(params string[] labels) => new()
    {
        Cmd = Command.PickOpen,
        Args = new ControlArgs { Items = labels.Select(l => new ControlPickItem { Id = l, Label = l }).ToArray() },
    };

    [Fact]
    public void PickOpenIsPendingAndShowsInTheTree()
    {
        var opened = Send(OpenPick("a", "b"));
        Assert.True(opened.Ok, opened.Error);
        var id = opened.Result!.Id!;

        Assert.Equal(ControlPickResult.Pending(), Send(new ControlRequest { Cmd = Command.PickResult, Target = id }).Result!.Pick);
        Assert.Equal(id, Send(new ControlRequest { Cmd = Command.Tree }).Result!.Tree!.PickPending);
    }

    [Fact]
    public void SecondPickOpenIsRefusedAndKeepsTheFirst()
    {
        var first = Send(OpenPick("a")).Result!.Id!;
        var second = Send(OpenPick("b"));

        Assert.False(second.Ok);
        Assert.Equal("pick already pending", second.Error);
        Assert.Equal(first, Send(new ControlRequest { Cmd = Command.Tree }).Result!.Tree!.PickPending);
    }

    [Fact]
    public void PickCancelResolvesCancelledAndClearsTheTree()
    {
        var id = Send(OpenPick("a")).Result!.Id!;

        Assert.True(Send(new ControlRequest { Cmd = Command.PickCancel, Target = id }).Ok);

        Assert.Equal(ControlPickResult.Cancelled(), Send(new ControlRequest { Cmd = Command.PickResult, Target = id }).Result!.Pick);
        Assert.Null(Send(new ControlRequest { Cmd = Command.Tree }).Result!.Tree!.PickPending);
    }

    [Fact]
    public void UnknownPickIdIsAnError()
    {
        Assert.Equal("unknown pick: nope", Send(new ControlRequest { Cmd = Command.PickResult, Target = "nope" }).Error);
        Assert.Equal("unknown pick: nope", Send(new ControlRequest { Cmd = Command.PickCancel, Target = "nope" }).Error);
    }

    [Fact]
    public void TheHostCanAnswerForTheUser()
    {
        var id = Send(OpenPick("a", "b")).Result!.Id!;

        Assert.True(_host.Actions.Pick.Resolve(id, ControlPickResult.Picked("b", "b", 1)));

        Assert.Equal(ControlPickResult.Picked("b", "b", 1), Send(new ControlRequest { Cmd = Command.PickResult, Target = id }).Result!.Pick);
    }

    [Fact]
    public void PickOpenOnAnUnregisteredWindowSaysTheWindowIsNotOpen()
    {
        _host.Actions.Picks.Unregister(_host.WindowId);

        var response = Send(OpenPick("a"));

        Assert.False(response.Ok);
        Assert.Equal(PickRegistry.WindowNotOpenError, response.Error);
    }

    [Fact]
    public void PendingPickRefusesCoversButAllowsDismissals()
    {
        var session = NewSession();
        Send(OpenPick("a"));

        Assert.Equal("pick pending", Send(new ControlRequest { Cmd = Command.Quick, Args = new ControlArgs { Mode = "show" } }).Error);
        Assert.True(Send(new ControlRequest { Cmd = Command.Quick, Args = new ControlArgs { Mode = "hide" } }).Ok);

        Assert.Equal("pick pending", Send(new ControlRequest { Cmd = Command.SurfaceZoom, Args = new ControlArgs { Mode = "show" } }).Error);
        Assert.True(Send(new ControlRequest { Cmd = Command.SurfaceZoom, Args = new ControlArgs { Mode = "hide" } }).Ok);

        Assert.Equal("pick pending", Send(new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { session } } }).Error);
        Assert.True(Send(new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Close = true } }).Ok);
    }

    [Fact]
    public void PendingPickRefusesSearchNavigationButAllowsClose()
    {
        var session = NewSession();
        _host.Store.SessionWithId(Guid.Parse(session))!.Surface = new FakeSurface { ScreenText = "needle" };
        Send(OpenPick("a"));

        Assert.Equal("pick pending", Send(new ControlRequest { Cmd = Command.SessionSearch, Target = session, Args = new ControlArgs { Text = "needle" } }).Error);
        Assert.Equal("pick pending", Send(new ControlRequest { Cmd = Command.SessionSearch, Target = session, Args = new ControlArgs { To = "next" } }).Error);
        Assert.True(Send(new ControlRequest { Cmd = Command.SessionSearch, Target = session, Args = new ControlArgs { To = "close" } }).Ok);
    }

    /// <summary>Creates a session over the pipe (creating its workspace by name when needed) and
    /// returns the new session id exactly as it came off the wire.</summary>
    private string NewSession(string workspaceName = "work")
    {
        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs
            {
                WorkspaceName = workspaceName,
                CreateWorkspace = true,
                Cwd = _stateDir,
            },
        });
        Assert.True(response.Ok, response.Error);
        Assert.NotNull(response.Result?.Id);
        return response.Result!.Id!;
    }

    [Fact]
    public void ZoomActiveChanged_reports_only_successful_active_state_transitions()
    {
        var changes = new List<bool>();
        var stateDir = Directory.CreateTempSubdirectory("agterm-zoom-callback-").FullName;
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-zoom-callback-" + Guid.NewGuid().ToString("N"),
                ZoomActiveChanged = changes.Add,
            });
            host.Start();
            var client = new ControlPipeClient(host.PipeName);

            Assert.True(client.Send(new ControlRequest { Cmd = Command.SurfaceZoom }).Ok);
            Assert.True(client.Send(new ControlRequest { Cmd = Command.SurfaceZoom }).Ok);

            Assert.Equal([true, false], changes);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void SessionNewReturnsAnUppercaseIdAndTheSessionExists()
    {
        var id = NewSession();

        // the wire id is the canonical uppercase uuid string and resolves in the live store.
        Assert.Equal(id, id.ToUpperInvariant());
        var guid = Guid.Parse(id);
        Assert.NotNull(_host.Store.SessionWithId(guid));
        Assert.Equal(guid, _host.Store.SelectedSessionId);
    }

    [Fact]
    public void TreeRoundTripsOverTheRealPipe()
    {
        var id = NewSession("alpha");

        var response = Send(new ControlRequest { Cmd = Command.Tree });

        Assert.True(response.Ok, response.Error);
        var tree = response.Result?.Tree;
        Assert.NotNull(tree);
        // the host bootstraps the app-launch default workspace; "alpha" is the one this test added.
        var workspace = Assert.Single(tree!.Workspaces, w => w.Name == "alpha");
        Assert.True(workspace.Active);
        var session = Assert.Single(workspace.Sessions);
        Assert.Equal(id, session.Id);
        Assert.True(session.Active);
        Assert.Equal(_stateDir, session.Cwd);
        // headless extras: the quick terminal starts hidden, nothing is zoomed, no dashboard.
        Assert.False(tree.QuickVisible);
        Assert.Null(tree.ZoomedSurface);
        Assert.Null(tree.DashboardMembers);
    }

    [Fact]
    public void SessionStatusPaneRightWithoutSplitNormalizesToLeftInTheTree()
    {
        var id = NewSession();

        var status = Send(new ControlRequest
        {
            Cmd = Command.SessionStatus,
            Target = id,
            Args = new ControlArgs { Status = "blocked", Pane = "right" },
        });
        Assert.True(status.Ok, status.Error);

        var tree = Send(new ControlRequest { Cmd = Command.Tree });
        var session = tree.Result!.Tree!.Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == id);
        Assert.Equal("blocked", session.Status);
        // no split → the stale right tag is normalized to left (the store's promoted-survivor rule).
        Assert.Equal("left", session.StatusPane);
    }

    [Fact]
    public void EventsReadBootstrapThenAfterCursorFlowOverThePipe()
    {
        // bootstrap: no cursor anchors at the tail and intentionally returns no history.
        var bootstrap = Send(new ControlRequest { Cmd = Command.EventsRead });
        Assert.True(bootstrap.Ok, bootstrap.Error);
        var anchor = bootstrap.Result?.Events;
        Assert.NotNull(anchor);
        Assert.Empty(anchor!.Items);

        var id = NewSession();

        var page = Send(new ControlRequest
        {
            Cmd = Command.EventsRead,
            Args = new ControlArgs
            {
                Run = anchor.Run.ToString("D"),
                After = anchor.Next.ToString(),
            },
        });
        Assert.True(page.Ok, page.Error);
        var batch = page.Result?.Events;
        Assert.NotNull(batch);
        Assert.Equal(anchor.Run, batch!.Run);
        Assert.True(batch.Next > anchor.Next);
        var created = Assert.Single(batch.Items, e => e.Kind == ControlEventKind.SessionCreated);
        Assert.Equal(id, created.Session);
        // the host's sink stamps the single window's id (uppercase) on every event.
        var windowTag = _host.WindowId.ToString("D").ToUpperInvariant();
        Assert.All(batch.Items, e => Assert.Equal(windowTag, e.Window));

        // a run mismatch is the loud pinned cursor failure, still carrying the current anchor.
        var stale = Send(new ControlRequest
        {
            Cmd = Command.EventsRead,
            Args = new ControlArgs { Run = Guid.NewGuid().ToString("D"), After = "0" },
        });
        Assert.False(stale.Ok);
        Assert.Equal("event run changed", stale.Error);
        Assert.NotNull(stale.Result?.Events);
    }

    [Fact]
    public void SessionTypeInjectsIntoAnAttachedFakeSurface()
    {
        var id = NewSession();
        var fake = new FakeSurface();
        _host.Store.SessionWithId(Guid.Parse(id))!.Surface = fake;

        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Target = id,
            Args = new ControlArgs { Text = "echo hello\n" },
        });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(id, response.Result?.Id);
        Assert.Equal(new[] { "echo hello\n" }, fake.Injected);
    }

    [Fact]
    public void SessionTypeWithoutASurfaceAndWithoutSelectReportsNotRealizedAndKeepsTheSelection()
    {
        var target = NewSession();
        var other = NewSession(); // created selected: holds the selection

        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionType, Target = target, Args = new ControlArgs { Text = "x" },
        });

        Assert.False(response.Ok);
        Assert.Equal("session not realized", response.Error);
        Assert.Equal(Guid.Parse(other), _host.Store.SelectedSessionId);
    }

    [Fact]
    public void SessionTypeWithSelectSelectsTheUnrealizedTarget()
    {
        var target = NewSession();
        NewSession();

        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionType, Target = target, Args = new ControlArgs { Text = "x", Select = true },
        });

        Assert.Equal("session not realized", response.Error);
        Assert.Equal(Guid.Parse(target), _host.Store.SelectedSessionId);
    }

    [Fact]
    public void SessionTypeWithSelectOnARealizedSessionKeepsTheSelection()
    {
        var target = NewSession();
        var other = NewSession();
        _host.Store.SessionWithId(Guid.Parse(target))!.Surface = new FakeSurface();

        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionType, Target = target, Args = new ControlArgs { Text = "x", Select = true },
        });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(Guid.Parse(other), _host.Store.SelectedSessionId);
    }

    [Fact]
    public void SessionTextReturnsTheFakeScreenText()
    {
        var id = NewSession();
        var fake = new FakeSurface { ScreenText = "PS> dir\nfile.txt" };
        _host.Store.SessionWithId(Guid.Parse(id))!.Surface = fake;

        var response = Send(new ControlRequest { Cmd = Command.SessionText, Target = id });

        Assert.True(response.Ok, response.Error);
        Assert.Equal("PS> dir\nfile.txt", response.Result?.Text);

        // --lines trims to the last N lines, like the upstream surface read.
        var lastLine = Send(new ControlRequest
        {
            Cmd = Command.SessionText,
            Target = id,
            Args = new ControlArgs { Lines = 1 },
        });
        Assert.True(lastLine.Ok, lastLine.Error);
        Assert.Equal("file.txt", lastLine.Result?.Text);
    }

    [Fact]
    public void SelectAllThenCopyReadsBackTheFakeScreen()
    {
        var id = NewSession();
        var fake = new FakeSurface { ScreenText = "selected content" };
        _host.Store.SessionWithId(Guid.Parse(id))!.Surface = fake;

        // copy with no selection is the pinned upstream error.
        var empty = Send(new ControlRequest { Cmd = Command.SessionCopy, Target = id });
        Assert.False(empty.Ok);
        Assert.Equal("no selection", empty.Error);

        var selectAll = Send(new ControlRequest { Cmd = Command.SessionSelectAll, Target = id });
        Assert.True(selectAll.Ok, selectAll.Error);
        var copy = Send(new ControlRequest { Cmd = Command.SessionCopy, Target = id });
        Assert.True(copy.Ok, copy.Error);
        Assert.Equal("selected content", copy.Result?.Text);
    }

    [Fact]
    public void PasteSelectAllAndCopyOnAnUnrealizedPaneReportNotRealized()
    {
        var id = NewSession();
        var parked = new FakeSurface { Realized = false, ScreenText = "x", Selection = "stale" };
        _host.Store.SessionWithId(Guid.Parse(id))!.Surface = parked;

        foreach (var cmd in new[] { Command.SessionPaste, Command.SessionSelectAll, Command.SessionCopy })
        {
            var response = Send(new ControlRequest { Cmd = cmd, Target = id });
            Assert.False(response.Ok, $"{cmd} must not answer ok for a pane with no terminal");
            Assert.Equal("session not realized", response.Error);
        }
        Assert.Equal(0, parked.PasteCount);
        Assert.Equal(0, parked.SelectAllCount);
    }

    [Fact]
    public void WindowZoomReturnsThePinnedHeadlessError()
    {
        var response = Send(new ControlRequest { Cmd = Command.WindowZoom });

        Assert.False(response.Ok);
        Assert.Equal("window management is not available in the headless host", response.Error);
    }

    [Fact]
    public void WindowListReportsTheSingleAlwaysOpenWindowAndRenameSticks()
    {
        var list = Send(new ControlRequest { Cmd = Command.WindowList });
        Assert.True(list.Ok, list.Error);
        var node = Assert.Single(list.Result!.Windows!);
        Assert.Equal(_host.WindowId.ToString("D").ToUpperInvariant(), node.Id);
        Assert.Equal("main", node.Name);
        Assert.True(node.Open);
        Assert.True(node.Active);
        Assert.True(node.SidebarVisible);

        var rename = Send(new ControlRequest
        {
            Cmd = Command.WindowRename,
            Args = new ControlArgs { Name = "renamed" },
        });
        Assert.True(rename.Ok, rename.Error);
        Assert.Equal(node.Id, rename.Result?.Id);
        Assert.Equal("renamed", _host.WindowName);

        // window.select resolving to the one window is ok+id — it is already frontmost.
        var select = Send(new ControlRequest { Cmd = Command.WindowSelect, Target = node.Id });
        Assert.True(select.Ok, select.Error);
        Assert.Equal(node.Id, select.Result?.Id);
    }

    [Fact]
    public void UnresolvedTargetsKeepThePinnedResolutionErrors()
    {
        NewSession();

        var session = Send(new ControlRequest { Cmd = Command.SessionSelect, Target = "zz" });
        Assert.False(session.Ok);
        Assert.Equal("no such session: zz", session.Error);

        var window = Send(new ControlRequest
        {
            Cmd = Command.Tree,
            Args = new ControlArgs { Window = "zz" },
        });
        Assert.False(window.Ok);
        Assert.Equal("no such window: zz", window.Error);
    }

    // O0.5 — headless host (no SurfaceRealizer): session model has null Surface, pinned wire
    // errors on session.text / selected session.type, and the session node exists in the tree
    // without live-surface fields.
    [Fact]
    public void No_surface_realizer_preserves_pinned_errors_and_omits_live_tree_fields()
    {
        var id = NewSession();
        var guid = Guid.Parse(id);

        // model surface is null — headless default, no SurfaceRealizer configured
        var modelSession = _host.Store.SessionWithId(guid);
        Assert.NotNull(modelSession);
        Assert.Null(modelSession!.Surface);

        var text = Send(new ControlRequest { Cmd = Command.SessionText, Target = id });
        Assert.False(text.Ok);
        Assert.Equal("session not realized", text.Error);

        var type = Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Target = id,
            Args = new ControlArgs { Text = "x", Select = true },
        });
        Assert.False(type.Ok);
        Assert.Equal("session not realized", type.Error);

        var tree = Send(new ControlRequest { Cmd = Command.Tree });
        Assert.True(tree.Ok, tree.Error);
        var node = tree.Result!.Tree!.Workspaces
            .SelectMany(w => w.Sessions)
            .Single(s => s.Id == id);
        Assert.Null(node.Title);
        Assert.Null(node.FontSize);
        var surface = Assert.Single(node.Surfaces!);
        Assert.Equal($"surface:{id}:left", surface.Id);
        Assert.Equal("left", surface.Kind);
        Assert.True(surface.Active);
        Assert.True(surface.Visible);
    }

    // O1.1 — IControlDispatchScopeFactory: gate is released before CompleteAsync is awaited;
    // response stays pending until the scope is released; scope is opened and disposed once.
    [Fact]
    public async Task DispatchScope_gate_is_released_before_CompleteAsync_and_response_returns_unchanged()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-scope-").FullName;
        var factory = new FakeScopeFactory();
        try
        {
            using var scopedHost = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
                DispatchScopeFactory = factory,
            });
            scopedHost.Start();
            var client = new ControlPipeClient(scopedHost.PipeName);

            // dispatch is in-flight; the scope holds the response after the gate is released
            var dispatchTask = Task.Run(() => client.Send(new ControlRequest
            {
                Cmd = Command.SessionNew,
                Args = new ControlArgs { CreateWorkspace = true, WorkspaceName = "sc", Cwd = stateDir },
            }));

            // CompleteAsync is entered only after the gate is released; wait for that signal
            await factory.GateReleasedTask.WaitAsync(TimeSpan.FromSeconds(5));

            // response is still pending while CompleteAsync has not resolved
            Assert.False(dispatchTask.IsCompleted, "response returned before scope was released");

            // gate is free — another task enters RunLocked without blocking
            await Task.Run(() => scopedHost.RunLocked(() => { })).WaitAsync(TimeSpan.FromSeconds(5));

            // releasing the scope delivers the original dispatch result, unchanged
            factory.Release();
            var response = await dispatchTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(response.Ok, response.Error);
            Assert.NotNull(response.Result?.Id);

            Assert.Equal(1, factory.BeginCount);
            Assert.Equal(1, factory.DisposeCount);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // O1.3 — Stop must propagate cancellation to CompleteAsync; dispatch must unblock promptly.
    // Under O1.2 (CancellationToken.None), Stop cannot reach the in-flight CompleteAsync wait
    // and the bounded WaitAsync times out — this is the expected RED.
    [Fact]
    public async Task Stop_cancels_active_dispatch_scope_CompleteAsync()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-stop-").FullName;
        var factory = new FakeCancellableScopeFactory();
        Task<ControlResponse>? dispatchTask = null;
        try
        {
            using var scopedHost = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
                DispatchScopeFactory = factory,
            });
            scopedHost.Start();
            var client = new ControlPipeClient(scopedHost.PipeName);

            // issue a request; CompleteAsync will block until the gate is released or token cancelled
            dispatchTask = Task.Run(() => client.Send(new ControlRequest
            {
                Cmd = Command.SessionNew,
                Args = new ControlArgs { CreateWorkspace = true, WorkspaceName = "stop", Cwd = stateDir },
            }));

            // wait for CompleteAsync to be entered (gate has been released by the dispatch)
            await factory.EnteredTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(dispatchTask.IsCompleted, "dispatch returned before Stop was called");

            // stopping the host must cancel the in-flight CompleteAsync
            scopedHost.Stop();

            var response = await dispatchTask.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.False(response.Ok);
            Assert.StartsWith("internal error:", response.Error);
            Assert.Equal(1, factory.DisposeCount);
        }
        finally
        {
            // release the fake gate so the blocked CompleteAsync can return (cleanup after red)
            factory.ReleaseGate();
            if (dispatchTask is not null)
                try { await dispatchTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // O2.1 — DeferBootstrap / Bootstrap: deferred construction, explicit bootstrap, repeated no-op.

    [Fact]
    public void Default_construction_seeds_one_selected_session()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-bootstrap-default-").FullName;
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
            });

            var session = Assert.Single(host.Store.Workspaces.SelectMany(w => w.Sessions));
            Assert.NotNull(host.Store.SelectedSessionId);
            Assert.Equal(session.Id, host.Store.SelectedSessionId);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void DeferBootstrap_leaves_store_empty_and_no_selection()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-bootstrap-deferred-").FullName;
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
                DeferBootstrap = true,
            });

            Assert.Empty(host.Store.Workspaces);
            Assert.Null(host.Store.SelectedSessionId);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Bootstrap_after_deferred_construction_creates_default_selected_session()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-bootstrap-explicit-").FullName;
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
                DeferBootstrap = true,
            });

            Assert.Empty(host.Store.Workspaces);

            host.Bootstrap();

            var session = Assert.Single(host.Store.Workspaces.SelectMany(w => w.Sessions));
            Assert.NotNull(host.Store.SelectedSessionId);
            Assert.Equal(session.Id, host.Store.SelectedSessionId);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void Repeated_Bootstrap_is_a_noop_preserving_ids_and_invoking_realizer_once()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-bootstrap-idempotent-").FullName;
        var realizedIds = new List<Guid>();
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
                DeferBootstrap = true,
                SurfaceRealizer = session =>
                {
                    realizedIds.Add(session.Id);
                    return new FakeSurface { PaneToken = "tok-" + session.Id.ToString("N")[..8] };
                },
            });

            host.Bootstrap();
            var workspaceId = Assert.Single(host.Store.Workspaces).Id;
            var sessionId = Assert.Single(host.Store.Workspaces.SelectMany(w => w.Sessions)).Id;

            host.Bootstrap(); // second call must be a no-op

            Assert.Single(host.Store.Workspaces);
            Assert.Equal(workspaceId, Assert.Single(host.Store.Workspaces).Id);
            Assert.Equal(sessionId, Assert.Single(host.Store.Workspaces.SelectMany(w => w.Sessions)).Id);
            // realizer invoked exactly once across two Bootstrap calls
            Assert.Equal(new[] { sessionId }, realizedIds);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // O2.x — DeferBootstrap restore: a second host with DeferBootstrap=true pointed at a
    // persisted state dir restores the same IDs and name after Bootstrap, and a second Bootstrap
    // call is a no-op.
    [Fact]
    public void DeferBootstrap_restore_rebuilds_same_workspace_session_ids_and_second_call_is_noop()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-deferred-restore-").FullName;
        try
        {
            Guid workspaceId;
            Guid sessionId;
            string workspaceName;

            // Seed: eager default host creates and persists one workspace/session.
            using (var seed = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
            }))
            {
                var seedWorkspace = Assert.Single(seed.Store.Workspaces);
                workspaceId = seedWorkspace.Id;
                workspaceName = seedWorkspace.Name;
                sessionId = Assert.Single(seedWorkspace.Sessions).Id;
                seed.Store.Save();
            }

            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
                DeferBootstrap = true,
            });

            // before Bootstrap: store is empty, no selection
            Assert.Empty(host.Store.Workspaces);
            Assert.Null(host.Store.SelectedSessionId);

            // first Bootstrap: restores persisted workspace and session
            host.Bootstrap();

            var restoredWorkspace = Assert.Single(host.Store.Workspaces);
            Assert.Equal(workspaceId, restoredWorkspace.Id);
            Assert.Equal(workspaceName, restoredWorkspace.Name);
            Assert.Equal(sessionId, Assert.Single(restoredWorkspace.Sessions).Id);
            Assert.Equal(sessionId, host.Store.SelectedSessionId);

            // second Bootstrap: no-op — IDs and count unchanged
            host.Bootstrap();

            Assert.Single(host.Store.Workspaces);
            Assert.Equal(workspaceId, Assert.Single(host.Store.Workspaces).Id);
            Assert.Equal(sessionId, Assert.Single(host.Store.Workspaces.SelectMany(w => w.Sessions)).Id);
            Assert.Equal(sessionId, host.Store.SelectedSessionId);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void DeferBootstrap_restore_realizes_selected_session_once_after_model_restore()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-deferred-realize-").FullName;
        try
        {
            Guid sessionId;
            using (var seed = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
            }))
            {
                sessionId = Assert.Single(seed.Store.Workspaces.SelectMany(w => w.Sessions)).Id;
                seed.Store.Save();
            }

            var realizedIds = new List<Guid>();
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
                DeferBootstrap = true,
                SurfaceRealizer = session =>
                {
                    realizedIds.Add(session.Id);
                    return new FakeSurface { PaneToken = "tok-" + session.Id.ToString("N")[..8] };
                },
            });

            host.Bootstrap();
            host.Bootstrap();

            var restored = Assert.Single(host.Store.Workspaces.SelectMany(w => w.Sessions));
            Assert.Equal(sessionId, restored.Id);
            Assert.NotNull(restored.Surface);
            Assert.Equal(new[] { sessionId }, realizedIds);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // O2.x — throwing realizer: a failed first realization must not cause a second seed when
    // Bootstrap is retried. The workspace/session from the first attempt survive; the realizer
    // is invoked exactly once. This was red before bootstrap split model setup from realization.
    [Fact]
    public void Bootstrap_throwing_realizer_does_not_reseed_on_retry()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-throw-realizer-").FullName;
        var realizerCallCount = 0;
        var thrownException = new InvalidOperationException("surface-unavailable");
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
                DeferBootstrap = true,
                SurfaceRealizer = _ =>
                {
                    realizerCallCount++;
                    if (realizerCallCount == 1) throw thrownException;
                    return new FakeSurface();
                },
            });

            // first Bootstrap: realizer throws on the first realization; Bootstrap propagates it
            var caught = Assert.Throws<InvalidOperationException>(() => host.Bootstrap());
            Assert.Same(thrownException, caught);

            // capture the workspace/session created during the failed first attempt
            var firstWorkspace = Assert.Single(host.Store.Workspaces);
            var firstWorkspaceId = firstWorkspace.Id;
            var firstSessionId = Assert.Single(firstWorkspace.Sessions).Id;

            // second Bootstrap: must not re-seed — the session already exists
            host.Bootstrap();

            // exactly one workspace/session, with the IDs from the first attempt
            Assert.Single(host.Store.Workspaces);
            Assert.Equal(firstWorkspaceId, Assert.Single(host.Store.Workspaces).Id);
            Assert.Equal(firstSessionId,
                Assert.Single(host.Store.Workspaces.SelectMany(w => w.Sessions)).Id);

            // realizer was invoked exactly once (the failed call from Bootstrap #1)
            Assert.Equal(1, realizerCallCount);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

// Controllable dispatch scope factory for O1.1: signals when CompleteAsync is entered (gate free).
file sealed class FakeScopeFactory : IControlDispatchScopeFactory
{
    private readonly TaskCompletionSource _completeCalled =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseGate =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int BeginCount { get; private set; }
    public int DisposeCount { get; private set; }

    // Completes when CompleteAsync is entered — i.e., after the dispatch gate has been released.
    public Task GateReleasedTask => _completeCalled.Task;

    public IControlDispatchScope Begin()
    {
        BeginCount++;
        return new FakeScope(_completeCalled, _releaseGate.Task, () => DisposeCount++);
    }

    public void Release() => _releaseGate.TrySetResult();

    private sealed class FakeScope(
        TaskCompletionSource completeCalled,
        Task completion,
        Action onDispose) : IControlDispatchScope
    {
        public Task CompleteAsync(CancellationToken cancellationToken)
        {
            completeCalled.TrySetResult();
            return completion;
        }

        public void Dispose() => onDispose();
    }
}

// Controllable dispatch scope for O1.3: signals entry, then waits on a gate that respects the
// supplied cancellation token. On cancellation, OperationCanceledException propagates out.
file sealed class FakeCancellableScopeFactory : IControlDispatchScopeFactory
{
    private readonly TaskCompletionSource _entered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseGate =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int DisposeCount { get; private set; }

    public Task EnteredTask => _entered.Task;

    public void ReleaseGate() => _releaseGate.TrySetResult();

    public IControlDispatchScope Begin() =>
        new FakeCancellableScope(_entered, _releaseGate.Task, () => DisposeCount++);

    private sealed class FakeCancellableScope(
        TaskCompletionSource entered,
        Task gate,
        Action onDispose) : IControlDispatchScope
    {
        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => onDispose();
    }
}
