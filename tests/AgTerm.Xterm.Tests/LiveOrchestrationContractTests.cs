using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Contract pins for the live orchestration path (no WebView2 required): isolated temp state dir
/// + unique named pipe per test; OpenSurface is a fake delegate backed by TaskCompletionSource.
/// O3 acceptance: after session.new moves OpenSurface outside the gate, RunLocked enters while
/// the opener is pending, the gate resolves with a FakeSurface, and session.type reaches it
/// immediately.
/// </summary>
public sealed class LiveOrchestrationContractTests : IDisposable
{
    private readonly string _stateDir;
    private readonly string _bootstrapDir;

    public LiveOrchestrationContractTests()
    {
        _stateDir = Directory.CreateTempSubdirectory("agterm-o0-").FullName;
        _bootstrapDir = Directory.CreateDirectory(Path.Combine(_stateDir, "bootstrap")).FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ---------------------------------------------------------------------------
    // Helper: build a LiveControlHost whose OpenSurface:
    //   - bootstrap (Cwd == _bootstrapDir): fails immediately (no surface in test)
    //   - session.new (Cwd == _stateDir): signals Entered, then awaits Gate
    // Returns the host, a pipe client, the entry signal, and the release gate.
    // ---------------------------------------------------------------------------
    private (LiveControlHost Host, ControlPipeClient Client,
             Task Entered, TaskCompletionSource<IHeadlessSurface> Gate)
        BuildHost()
    {
        var enteredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateTcs = new TaskCompletionSource<IHeadlessSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            HomeDirectory = _bootstrapDir,
            PipeName = "agterm-o0-" + Guid.NewGuid().ToString("N"),
            OpenSurface = (SurfaceOpenRequest request, CancellationToken ct) =>
            {
                if (request.Options.Cwd == _bootstrapDir)
                    return Task.FromException<IHeadlessSurface>(
                        new InvalidOperationException("bootstrap: no surface in test"));
                enteredTcs.TrySetResult();
                return gateTcs.Task;
            },
        });
        host.Start();
        return (host, new ControlPipeClient(host.PipeName), enteredTcs.Task, gateTcs);
    }

    // ---------------------------------------------------------------------------
    // O0.2 — session.new response waits for the surface attempt to complete.
    // The response task is pending while OpenSurface holds the gate; it becomes
    // Ok after the gate faults (surface never materialized but session IS created).
    // ---------------------------------------------------------------------------
    [Fact]
    public async Task SessionNew_response_waits_for_surface_attempt()
    {
        var (host, client, entered, gate) = BuildHost();
        using (host)
        {
            var sessionNewTask = Task.Run(() => client.Send(new ControlRequest
            {
                Cmd = Command.SessionNew,
                Args = new ControlArgs { Cwd = _stateDir },
            }));

            try
            {
                await entered.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.False(sessionNewTask.IsCompleted,
                    "session.new response must be pending while OpenSurface is blocking");

                gate.TrySetException(new Exception("test: fault surface attempt"));

                var response = await sessionNewTask.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(response.Ok, $"expected ok, got error: {response.Error}");
                var id = response.Result?.Id;
                Assert.NotNull(id);
                Assert.Equal(id, id!.ToUpperInvariant());
                Assert.True(Guid.TryParse(id, out _), "Result.Id must be a valid GUID");
            }
            finally
            {
                gate.TrySetException(new Exception("test cleanup"));
                await sessionNewTask.WaitAsync(TimeSpan.FromSeconds(5))
                    .ContinueWith(_ => { }, TaskContinuationOptions.None);
            }
        }
    }

    // ---------------------------------------------------------------------------
    // O0.3 — O3 acceptance: RunLocked enters while OpenSurface is pending.
    // After the gate opens, session.new returns the session id and session.type
    // immediately reaches the realized surface.
    // ---------------------------------------------------------------------------
    [Fact]
    public async Task RunLocked_can_enter_while_surface_attempt_is_pending()
    {
        var (host, client, entered, gate) = BuildHost();
        var sessionNewTask = Task.Run(() => client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Cwd = _stateDir },
        }));
        Task runLockedTask = Task.CompletedTask;

        try
        {
            await entered.WaitAsync(TimeSpan.FromSeconds(10));

            var ran = false;
            runLockedTask = Task.Run(() => host.Host.RunLocked(() => ran = true));
            await runLockedTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(ran, "RunLocked action must have executed");

            var fake = new FakeSurface();
            gate.TrySetResult(fake);

            var response = await sessionNewTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(response.Ok, $"expected ok after gate opened, got: {response.Error}");

            var typeResponse = client.Send(new ControlRequest
            {
                Cmd = Command.SessionType,
                Target = response.Result!.Id,
                Args = new ControlArgs { Text = "o3-immediate-type" },
            });
            Assert.True(typeResponse.Ok, $"expected session.type ok, got: {typeResponse.Error}");
            Assert.Equal(new[] { "o3-immediate-type" }, fake.Injected);
        }
        finally
        {
            gate.TrySetException(new Exception("test cleanup"));
            await sessionNewTask.WaitAsync(TimeSpan.FromSeconds(5))
                .ContinueWith(_ => { }, TaskContinuationOptions.None);
            await runLockedTask.WaitAsync(TimeSpan.FromSeconds(5))
                .ContinueWith(_ => { }, TaskContinuationOptions.None);
            host.Dispose();
        }
    }

    // ---------------------------------------------------------------------------
    // O0.4 — Both bootstrap and session.new surface attempts fault immediately.
    // session.new is still Ok (session IS created), subsequent session.text returns
    // the pinned "session not realized" error, session.type without select returns
    // "session not realized; use select", and tree remains Ok.
    // ---------------------------------------------------------------------------
    [Fact]
    public void SessionNew_ok_and_pinned_errors_when_surface_always_faults()
    {
        var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            HomeDirectory = _bootstrapDir,
            PipeName = "agterm-o0-" + Guid.NewGuid().ToString("N"),
            OpenSurface = (SurfaceOpenRequest request, CancellationToken ct) =>
                Task.FromException<IHeadlessSurface>(
                    new InvalidOperationException("test: always fault")),
        });
        using (host)
        {
            host.Start();
            var client = new ControlPipeClient(host.PipeName);

            var created = client.Send(new ControlRequest
            {
                Cmd = Command.SessionNew,
                Args = new ControlArgs { Cwd = _stateDir },
            });
            Assert.True(created.Ok, $"expected ok, got error: {created.Error}");
            var id = created.Result?.Id;
            Assert.NotNull(id);
            Assert.Equal(id, id!.ToUpperInvariant());
            Assert.True(Guid.TryParse(id, out _), "Result.Id must be a valid GUID");

            var text = client.Send(new ControlRequest
            {
                Cmd = Command.SessionText,
                Target = id,
            });
            Assert.False(text.Ok);
            Assert.Equal("session not realized", text.Error);

            var type = client.Send(new ControlRequest
            {
                Cmd = Command.SessionType,
                Target = id,
                Args = new ControlArgs { Text = "x" },
            });
            Assert.False(type.Ok);
            Assert.Equal("session not realized; use select", type.Error);

            var tree = client.Send(new ControlRequest { Cmd = Command.Tree });
            Assert.True(tree.Ok, $"tree must be Ok after surface fault, got: {tree.Error}");
        }
    }

    [Fact]
    public async Task Dispose_cancels_pending_open_and_tears_down_late_surface()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<IHeadlessSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken openerToken = default;
        var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            HomeDirectory = _bootstrapDir,
            PipeName = "agterm-o3-shutdown-" + Guid.NewGuid().ToString("N"),
            RealizeBudget = TimeSpan.FromSeconds(30),
            OpenSurface = (request, cancellationToken) =>
            {
                if (request.Options.Cwd == _bootstrapDir)
                    return Task.FromException<IHeadlessSurface>(new Exception("bootstrap failure"));
                openerToken = cancellationToken;
                entered.TrySetResult();
                return gate.Task;
            },
        });
        host.Start();
        var client = new ControlPipeClient(host.PipeName);
        var requestTask = Task.Run(() => client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Cwd = _stateDir },
        }));

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            host.Dispose();

            Assert.True(openerToken.IsCancellationRequested);
            await requestTask.WaitAsync(TimeSpan.FromSeconds(5));

            var late = new FakeSurface();
            gate.TrySetResult(late);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (late.TeardownCount == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.Equal(1, late.TeardownCount);
        }
        finally
        {
            gate.TrySetException(new Exception("test cleanup"));
            await requestTask.WaitAsync(TimeSpan.FromSeconds(5))
                .ContinueWith(_ => { }, TaskContinuationOptions.None);
            host.Dispose();
        }
    }

    [Fact]
    public async Task StoreCoordinator_is_available_after_construction_and_reads_bootstrap_selection()
    {
        using var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            HomeDirectory = _bootstrapDir,
            PipeName = "agterm-o4-store-" + Guid.NewGuid().ToString("N"),
            OpenSurface = (_, _) => Task.FromException<IHeadlessSurface>(
                new InvalidOperationException("test bootstrap failure")),
        });

        Assert.IsAssignableFrom<ILiveStoreCoordinator>(host.StoreCoordinator);
        var snapshot = await host.StoreCoordinator.ReadAsync(store => (
            store.SelectedSessionId,
            WorkspaceCount: store.Workspaces.Count,
            SessionCount: store.Workspaces.Sum(workspace => workspace.Sessions.Count)));

        Assert.NotNull(snapshot.SelectedSessionId);
        Assert.Equal(1, snapshot.WorkspaceCount);
        Assert.Equal(1, snapshot.SessionCount);
    }

    [Fact]
    public async Task StoreCoordinator_forwards_control_selection_and_sidebar_invalidation()
    {
        using var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            HomeDirectory = _bootstrapDir,
            PipeName = "agterm-o4-events-" + Guid.NewGuid().ToString("N"),
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
        });
        var selection = new TaskCompletionSource<Guid?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sidebar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.StoreCoordinator.SelectionChanged += id => selection.TrySetResult(id);
        host.StoreCoordinator.SidebarInvalidated += () => sidebar.TrySetResult();
        host.Start();
        var client = new ControlPipeClient(host.PipeName);

        var response = client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { Cwd = _stateDir },
        });

        Assert.True(response.Ok, response.Error);
        var id = Guid.Parse(response.Result!.Id!);
        Assert.Equal(id, await selection.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await sidebar.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LiveHost_dispose_disposes_StoreCoordinator()
    {
        var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            HomeDirectory = _bootstrapDir,
            PipeName = "agterm-o4-dispose-" + Guid.NewGuid().ToString("N"),
            OpenSurface = (_, _) => Task.FromException<IHeadlessSurface>(
                new InvalidOperationException("test bootstrap failure")),
        });
        var coordinator = host.StoreCoordinator;

        host.Dispose();
        host.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            coordinator.ReadAsync(store => store.Workspaces.Count));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            coordinator.MutateAsync(store => store.NotifySidebar()));
    }
}
