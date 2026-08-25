using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// Contract pins for the live orchestration path (no WebView2 required): isolated temp state dir
/// + unique named pipe per test; OpenSurface is a fake delegate backed by TaskCompletionSource.
/// O0.3 is an intentional red test — it describes the desired post-O3 behavior and fails today
/// because DispatchLocked holds the gate while OpenSurface awaits.
/// </summary>
public sealed class LiveOrchestrationContractTests : IDisposable
{
    private readonly string _stateDir;

    public LiveOrchestrationContractTests()
    {
        _stateDir = Directory.CreateTempSubdirectory("agterm-o0-").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ---------------------------------------------------------------------------
    // Helper: build a LiveControlHost whose OpenSurface:
    //   - call 1 (bootstrap): throws immediately so construction does not block
    //   - call 2+: signals enteredTcs, then awaits gateTcs
    // Returns the host, a pipe client, the entry signal, and the release gate.
    // ---------------------------------------------------------------------------
    private (LiveControlHost Host, ControlPipeClient Client,
             Task Entered, TaskCompletionSource<XtermPaneSurface> Gate)
        BuildHost()
    {
        var callCount = 0;
        var enteredTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateTcs = new TaskCompletionSource<XtermPaneSurface>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-o0-" + Guid.NewGuid().ToString("N"),
            OpenSurface = (_, _) =>
            {
                var n = Interlocked.Increment(ref callCount);
                if (n == 1) throw new InvalidOperationException("bootstrap: no surface in test");
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
    // O0.3 — RunLocked_can_enter_while_surface_attempt_is_pending
    // INTENTIONAL RED TEST: describes the desired post-O3 behavior.
    // Today DispatchLocked holds _gate while OpenSurface awaits, so RunLocked
    // cannot enter within the short timeout → this test fails with TimeoutException.
    // O1 adds the outside-gate scope; O3 moves OpenSurface work into that scope.
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

            // Post-O3 assertion: RunLocked must complete within 200 ms while OpenSurface is pending.
            // Currently fails with TimeoutException because the gate is held by DispatchLocked.
            await runLockedTask.WaitAsync(TimeSpan.FromMilliseconds(200));
            Assert.True(ran, "RunLocked action must have executed");
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
            PipeName = "agterm-o0-" + Guid.NewGuid().ToString("N"),
            OpenSurface = (_, _) => Task.FromException<XtermPaneSurface>(
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
}
