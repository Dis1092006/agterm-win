using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The spawn path attaching a cwd-reporting integration (PLATFORM-NOTES). Observed through the
/// OpenSurface callback, which receives the final argv the ConPTY child would be started with, so
/// no real shell has to run.
///
/// The regression this guards is narrow and expensive: LiveControlHost puts a user's own program
/// in the SAME argv field for session.restore / --command / a captured foreground command, so an
/// injection that is not shape-aware would silently replace that program with a shell.
/// </summary>
public sealed class LiveShellIntegrationWiringTests : IDisposable
{
    private const string Cmd = "cmd.exe";

    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-shellint-").FullName;
    private readonly List<LiveControlHost> _hosts = new();
    private readonly List<SurfaceOpenRequest> _requests = new();

    private LiveControlHost StartHost(string? integrationDirectory)
    {
        var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-shellint-" + Guid.NewGuid().ToString("N"),
            DefaultShell = Cmd,
            HomeDirectory = _stateDir,
            ShellIntegrationDirectory = integrationDirectory,
            OpenSurface = (request, _) =>
            {
                lock (_requests) _requests.Add(request);
                return Task.FromResult<IHeadlessSurface>(new RecordingSurface());
            },
        });
        _hosts.Add(host);
        return host;
    }

    /// <summary>
    /// The argv of the surface for a specific session. Indexing by position would be wrong: the
    /// host bootstraps its own default session, whose plain-shell surface is requested first.
    /// </summary>
    private async Task<IReadOnlyList<string>> ArgvOfSessionAsync(Guid sessionId, TimeSpan? waitBudget = null)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var budget = waitBudget ?? TimeSpan.FromSeconds(10);
        while (true)
        {
            lock (_requests)
            {
                if (_requests.FirstOrDefault(r => r.SessionId == sessionId) is { } request)
                    return request.Options.Argv;
            }

            // A delayed continuation must read the result before treating its deadline as a failure.
            if (elapsed.Elapsed >= budget) break;
            await Task.Delay(25);
        }

        Assert.Fail($"no surface was requested for session {sessionId}");
        return Array.Empty<string>();
    }

    private async Task<Guid> AddRealizedSessionAsync(LiveControlHost host, string? command = null)
    {
        Guid sessionId = default;
        PendingTerminalSurface? pending = null;
        host.Host.RunLocked(() =>
        {
            var session = host.Host.Store.AddSession(
                host.Host.Store.Workspaces[0].Id, _stateDir, command: command)!;
            sessionId = session.Id;
            pending = Assert.IsType<PendingTerminalSurface>(session.Surface);
        });
        Assert.NotNull(pending);
        try
        {
            Assert.NotNull(await pending.Ready.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        catch (TimeoutException ex) when (!pending.Ready.IsCompleted)
        {
            throw new TimeoutException(
                $"surface realization did not complete for session {sessionId} within 30 seconds", ex);
        }

        return sessionId;
    }

    [Fact]
    public async Task A_recorded_request_is_returned_even_when_the_wait_budget_has_elapsed()
    {
        var host = StartHost(integrationDirectory: null);
        var sessionId = await AddRealizedSessionAsync(host);

        Assert.Equal([Cmd], await ArgvOfSessionAsync(sessionId, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_plain_shell_session_is_spawned_with_the_integration_attached()
    {
        var host = StartHost(@"C:\app\integration");
        var sessionId = await AddRealizedSessionAsync(host);

        var argv = await ArgvOfSessionAsync(sessionId);

        Assert.Equal([Cmd, "/k", "call", @"C:\app\integration\integration.cmd"], argv);
    }

    [Fact]
    public async Task Without_an_integration_directory_the_shell_is_spawned_bare()
    {
        // A published layout that is missing its integration resources must still open a working
        // terminal — just one that does not report its cwd.
        var host = StartHost(integrationDirectory: null);
        var sessionId = await AddRealizedSessionAsync(host);

        Assert.Equal([Cmd], await ArgvOfSessionAsync(sessionId));
    }

    [Fact]
    public async Task A_session_started_with_a_command_keeps_its_own_argv()
    {
        // THE regression guard. A session carrying an InitialCommand puts the user's program in
        // argv instead of the shell; attaching /k call <script> would run cmd instead of it.
        var host = StartHost(@"C:\app\integration");
        var sessionId = await AddRealizedSessionAsync(host, command: "npm run dev");

        var argv = await ArgvOfSessionAsync(sessionId);

        Assert.DoesNotContain("/k", argv);
        Assert.DoesNotContain(argv, a => a.Contains("integration.cmd"));
    }

    public void Dispose()
    {
        foreach (var host in _hosts) host.Dispose();
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
    }

    /// <summary>A surface that records nothing: these tests assert on the REQUEST, not the pane.</summary>
    private sealed class RecordingSurface : IHeadlessSurface
    {
        public string? PaneToken => null;
        public bool Inject(string text) => false;
        public string? ReadScreenText(bool all, int? lines) => null;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => false;
        public bool PerformBindingAction(string action) => false;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }
}
