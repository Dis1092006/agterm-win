using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The AGTERM_* set every surface kind is spawned with (PLATFORM-NOTES, "Session environment").
/// Observed through the OpenSurface callback: the request carries the environment as a function of
/// the spawn token, so no real shell has to run.
/// </summary>
public sealed class LiveSessionEnvironmentWiringTests : IDisposable
{
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-envwire-").FullName;
    private readonly List<LiveControlHost> _hosts = new();
    private readonly List<SurfaceOpenRequest> _requests = new();

    private string SocketPath => Path.Combine(_stateDir, "agterm.sock");

    private LiveControlHost StartHost(string? pipeName, string? socketPath)
    {
        var host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = pipeName,
            SocketPath = socketPath,
            DefaultShell = "test-shell.exe",
            HomeDirectory = _stateDir,
            OpenSurface = (request, _) =>
            {
                lock (_requests) _requests.Add(request);
                return Task.FromResult<IHeadlessSurface>(new RecordingSurface());
            },
        });
        _hosts.Add(host);
        host.Start();
        return host;
    }

    private LiveControlHost StartDerivedHost() => StartHost(ControlPipeName.Derive(SocketPath), SocketPath);

    private static IReadOnlyDictionary<string, string> EnvOf(SurfaceOpenRequest request) =>
        request.Options.ChildEnvironment!("tok");

    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    private SurfaceOpenRequest RequestOf(LiveSurfaceKind kind, Guid? sessionId)
    {
        lock (_requests)
        {
            return Assert.Single(_requests, r => r.Kind == kind && r.SessionId == sessionId);
        }
    }

    private static void AssertOk(ControlResponse response) => Assert.True(response.Ok, response.Error);

    private static void OpenSplitScratchOverlayAndQuick(LiveControlHost host)
    {
        var client = new ControlPipeClient(host.PipeName);
        AssertOk(client.Send(new ControlRequest { Cmd = Command.SessionSplit, Args = new ControlArgs { Mode = "on" } }));
        AssertOk(client.Send(new ControlRequest { Cmd = Command.SessionScratch, Args = new ControlArgs { Mode = "on" } }));
        AssertOk(client.Send(new ControlRequest
        {
            Cmd = Command.SessionOverlayOpen,
            Args = new ControlArgs { Command = "overlay-tool" },
        }));
        AssertOk(client.Send(new ControlRequest { Cmd = Command.Quick, Args = new ControlArgs { Mode = "show" } }));
    }

    [Fact]
    public async Task Main_split_and_scratch_get_the_full_session_set_with_their_pane()
    {
        var host = StartDerivedHost();
        var (sessionId, workspaceId) = await host.StoreCoordinator.ReadAsync(
            store => (store.SelectedSessionId!.Value, store.WorkspaceForSession(store.SelectedSessionId!.Value)!.Id));
        OpenSplitScratchOverlayAndQuick(host);

        foreach (var (kind, pane) in new[]
                 {
                     (LiveSurfaceKind.Main, "left"),
                     (LiveSurfaceKind.Split, "right"),
                     (LiveSurfaceKind.Scratch, "scratch"),
                 })
        {
            var env = EnvOf(RequestOf(kind, sessionId));

            Assert.Equal(new Dictionary<string, string>
            {
                ["AGTERM_ENABLED"] = "1",
                ["AGTERM_SESSION_ID"] = Upper(sessionId),
                ["AGTERM_WINDOW_ID"] = Upper(host.WindowId),
                ["AGTERM_WORKSPACE_ID"] = Upper(workspaceId),
                ["AGTERM_SOCKET"] = SocketPath,
                ["AGTERM_PANE"] = pane,
                ["AGTERM_PANE_ID"] = "tok",
            }, env);
            Assert.Equal(host.PipeName, ControlPipeName.Derive(env["AGTERM_SOCKET"]));
        }
    }

    [Fact]
    public async Task Overlay_gets_the_session_set_without_pane_or_token()
    {
        var host = StartDerivedHost();
        var (sessionId, workspaceId) = await host.StoreCoordinator.ReadAsync(
            store => (store.SelectedSessionId!.Value, store.WorkspaceForSession(store.SelectedSessionId!.Value)!.Id));
        OpenSplitScratchOverlayAndQuick(host);

        var env = EnvOf(RequestOf(LiveSurfaceKind.Overlay, sessionId));

        Assert.Equal(new Dictionary<string, string>
        {
            ["AGTERM_ENABLED"] = "1",
            ["AGTERM_SESSION_ID"] = Upper(sessionId),
            ["AGTERM_WINDOW_ID"] = Upper(host.WindowId),
            ["AGTERM_WORKSPACE_ID"] = Upper(workspaceId),
            ["AGTERM_SOCKET"] = SocketPath,
        }, env);
    }

    [Fact]
    public void Quick_terminal_gets_only_enabled_window_and_socket()
    {
        var host = StartDerivedHost();
        OpenSplitScratchOverlayAndQuick(host);

        var env = EnvOf(RequestOf(LiveSurfaceKind.Quick, null));

        Assert.Equal(new Dictionary<string, string>
        {
            ["AGTERM_ENABLED"] = "1",
            ["AGTERM_WINDOW_ID"] = Upper(host.WindowId),
            ["AGTERM_SOCKET"] = SocketPath,
        }, env);
    }

    [Fact]
    public async Task A_session_in_a_second_workspace_reports_that_workspace()
    {
        var host = StartDerivedHost();
        Guid sessionId = default, workspaceId = default;
        await host.StoreCoordinator.MutateAsync(store =>
        {
            var workspace = store.AddWorkspace("second");
            workspaceId = workspace.Id;
            sessionId = store.AddSession(workspace.Id, _stateDir)!.Id;
        });

        var request = await WaitForRequestAsync(LiveSurfaceKind.Main, sessionId);

        Assert.Equal(Upper(workspaceId), EnvOf(request)["AGTERM_WORKSPACE_ID"]);
    }

    private async Task<SurfaceOpenRequest> WaitForRequestAsync(LiveSurfaceKind kind, Guid? sessionId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (_requests)
            {
                if (_requests.FirstOrDefault(r => r.Kind == kind && r.SessionId == sessionId) is { } request)
                    return request;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"no {kind} surface was requested for session {sessionId}");
        return null!;
    }

    [Fact]
    public async Task A_literal_pipe_name_advertises_no_socket()
    {
        var host = StartHost("agterm-envwire-" + Guid.NewGuid().ToString("N"), socketPath: null);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);
        OpenSplitScratchOverlayAndQuick(host);

        Assert.Null(host.AdvertisedSocketPath);
        Assert.DoesNotContain("AGTERM_SOCKET", EnvOf(RequestOf(LiveSurfaceKind.Main, sessionId)).Keys);
        Assert.DoesNotContain("AGTERM_SOCKET", EnvOf(RequestOf(LiveSurfaceKind.Quick, null)).Keys);
    }

    [Fact]
    public async Task A_socket_path_that_does_not_derive_to_the_pipe_is_not_advertised()
    {
        var host = StartHost("agterm-envwire-" + Guid.NewGuid().ToString("N"), SocketPath);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);

        var request = await WaitForRequestAsync(LiveSurfaceKind.Main, sessionId);

        Assert.Null(host.AdvertisedSocketPath);
        Assert.DoesNotContain("AGTERM_SOCKET", EnvOf(request).Keys);
    }

    [Fact]
    public async Task Without_a_pipe_name_the_host_advertises_the_path_it_derived_the_pipe_from()
    {
        var host = StartHost(pipeName: null, socketPath: null);
        var sessionId = await host.StoreCoordinator.ReadAsync(store => store.SelectedSessionId!.Value);

        var env = EnvOf(await WaitForRequestAsync(LiveSurfaceKind.Main, sessionId));

        Assert.Equal(SocketPath, env["AGTERM_SOCKET"]);
        Assert.Equal(host.PipeName, ControlPipeName.Derive(env["AGTERM_SOCKET"]));
    }

    public void Dispose()
    {
        foreach (var host in _hosts) host.Dispose();
        try { Directory.Delete(_stateDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
