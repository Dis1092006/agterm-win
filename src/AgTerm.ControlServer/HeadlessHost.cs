using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.ControlServer;

/// <summary>Construction options for <see cref="HeadlessHost"/>. Everything defaults to a working
/// zero-config host; tests pass an isolated StateDir and a unique PipeName.</summary>
public sealed record HeadlessHostOptions
{
    /// <summary>Where the store persists workspaces.json (and where the default pipe name derives
    /// from, via the shared socket-path rendezvous). Null = the platform app-support default.</summary>
    public string? StateDir { get; init; }

    /// <summary>Where keymap.conf lives for keymap.reload. Null = StateDir, then app support.</summary>
    public string? ConfigDir { get; init; }

    /// <summary>Explicit pipe name; null derives it from the state dir like ControlPipeName.Resolve
    /// (but WITHOUT reading the environment — the host is env-free by construction).</summary>
    public string? PipeName { get; init; }

    /// <summary>The single window's initial name (upstream's first window is "main").</summary>
    public string WindowName { get; init; } = "main";

    /// <summary>The bundled theme names theme.set validates against and theme.list reports.</summary>
    public IReadOnlyList<string> AvailableThemes { get; init; } = Array.Empty<string>();

    /// <summary>config.reload's diagnostic count source. Engine-less adaptation: upstream re-feeds
    /// libghostty; the headless host has no engine, so the count is injectable (default 0).</summary>
    public Func<int>? ConfigReload { get; init; }

    /// <summary>The upstream "Restore running commands on restart" setting, which gates the advisory
    /// note session.restore set returns. Default false = unset, matching upstream's != true check.</summary>
    public bool RestoreRunningCommand { get; init; }

    /// <summary>Whether a batch session.close takes the grace-undo soft-close path (the upstream
    /// setting defaults ON). The headless default is OFF: the grace timer would finalize the close
    /// on a thread-pool thread OUTSIDE the host's dispatch lock (documented in PLATFORM-NOTES).</summary>
    public bool CloseGraceUndoEnabled { get; init; }

    /// <summary>The dashboard auto-size base font (upstream: settings fontSize, default ghostty's
    /// built-in 13 pt).</summary>
    public double DashboardBaseFontSize { get; init; } = 13;

    /// <summary>session.new's default cwd. Null = the user profile directory (upstream: $HOME).</summary>
    public string? HomeDirectory { get; init; }

    /// <summary>Diagnostics sink for the pipe server; null writes nowhere.</summary>
    public Action<string>? Log { get; init; }

    /// <summary>The store's surface factory (AppStore.SurfaceRealizer): a session being SHOWN —
    /// selected, or created selected — realizes its main pane through this. Installed before the
    /// bootstrap so the default first session realizes too. Null = headless default (surfaces
    /// attach externally). Invoked under the host gate; implementations must not RunLocked back.</summary>
    public Func<Session, ITerminalSurface?>? SurfaceRealizer { get; init; }

    /// <summary>The live app's eager deck (phase 4): realize EVERY session at creation and every
    /// restored session at bootstrap, so session.type reaches background sessions without a
    /// select. Default false = headless parity — background sessions stay unrealized and the
    /// pinned "session not realized; use select" error survives (PLATFORM-NOTES).</summary>
    public bool EagerSurfaceRealization { get; init; }

    /// <summary>Optional per-request completion scope. Null preserves the legacy dispatch timing.</summary>
    public IControlDispatchScopeFactory? DispatchScopeFactory { get; init; }
}

/// <summary>
/// The headless control host — the Windows port's answer to the upstream app-side ControlServer
/// running WITHOUT any UI or terminal engine, backing the Phase-2 E2E suite
/// (agtermctl → named pipe → dispatcher → AppStore). It owns:
///  - one AppStore (one virtual window) whose control-event sink stamps the window id on every
///    draft and appends it to the app-run event ring;
///  - one ControlEventRing (events.read);
///  - the window identity (WindowId / WindowName, default "main");
///  - the HeadlessControlActions seam behind a ControlDispatcher behind a ControlPipeServer.
///
/// Threading: AppStore and ControlEventRing are not thread-safe (upstream is @MainActor). The pipe
/// server's dispatch delegate serializes every request through <c>_gate</c>; all headless action
/// arms complete synchronously (there are no engine realize-polls), so draining the dispatcher's
/// Task inside the lock cannot block on I/O or deadlock.
/// </summary>
public sealed class HeadlessHost : IDisposable
{
    private readonly object _gate = new();
    private readonly ControlDispatcher _dispatcher;
    private readonly ControlPipeServer _server;

    public HeadlessHost(HeadlessHostOptions? options = null)
    {
        options ??= new HeadlessHostOptions();
        WindowId = Guid.NewGuid();
        Ring = new ControlEventRing();
        var windowTag = WindowId.ToString("D").ToUpperInvariant();
        // the store's control-event sink is the upstream WindowLibrary seam collapsed to one
        // window: stamp the window id (uppercase uuid string) and append to the app-run ring.
        var persistence = new PersistenceStore(options.StateDir);
        Store = new AppStore(
            persistence: persistence,
            controlEventSink: draft => Ring.Append(draft with { Window = windowTag }))
        {
            // installed before the bootstrap: the default first session is created SELECTED and
            // must realize like any control-driven show
            SurfaceRealizer = options.SurfaceRealizer,
            EagerSurfaceRealization = options.EagerSurfaceRealization,
        };
        // the app-launch bootstrap: restore the persisted window snapshot (arming the persisted
        // session.restore overrides, like a real launch); an empty/fresh state dir seeds the same
        // default the app creates — one workspace holding one session at the home directory.
        var snapshot = persistence.Load();
        if (snapshot.Workspaces.Length > 0)
        {
            Store.Restore(snapshot, launchRestore: true);
            // the restored selection is shown immediately (upstream: the window's first draw
            // realizes it); Restore() itself bypasses SelectSession, so realize explicitly.
            // The eager deck realizes the WHOLE restored tree instead (every deck member's
            // view exists from launch).
            if (options.EagerSurfaceRealization) Store.RealizeAllSessions();
            else Store.RealizeActiveSession();
        }
        else
        {
            var home = options.HomeDirectory
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var workspace = Store.AddWorkspace(Store.DefaultWorkspaceName);
            Store.AddSession(workspace.Id, home);
        }
        var configDir = options.ConfigDir ?? options.StateDir ?? PersistenceStore.DefaultDirectory;
        Actions = new HeadlessControlActions(Store, Ring, WindowId,
            windowName: options.WindowName,
            configDirectory: configDir,
            availableThemes: options.AvailableThemes,
            configReload: options.ConfigReload,
            restoreRunningCommand: options.RestoreRunningCommand,
            closeGraceUndoEnabled: options.CloseGraceUndoEnabled,
            dashboardBaseFontSize: options.DashboardBaseFontSize,
            homeDirectory: options.HomeDirectory);
        _dispatcher = new ControlDispatcher(Actions);
        // same rendezvous derivation as ControlPipeName.Resolve, without reading the environment:
        // the host's state dir IS the socket-path input, so app and CLI meet on the same name.
        PipeName = options.PipeName
            ?? ControlPipeName.Derive(ControlResolve.SocketPath(options.StateDir,
                PersistenceStore.DefaultDirectory));
        _server = options.DispatchScopeFactory is not { } dispatchScopeFactory
            ? new ControlPipeServer(PipeName, DispatchLocked, options.Log)
            : new ControlPipeServer(PipeName,
                (request, cancellationToken) =>
                    DispatchWithScopeAsync(request, dispatchScopeFactory, cancellationToken),
                options.Log);
    }

    /// <summary>The single window's stable identity (events carry it uppercase).</summary>
    public Guid WindowId { get; }

    /// <summary>The single window's live name (window.rename mutates it).</summary>
    public string WindowName => Actions.WindowName;

    public AppStore Store { get; }

    public ControlEventRing Ring { get; }

    public HeadlessControlActions Actions { get; }

    public string PipeName { get; }

    public bool IsListening => _server.IsListening;

    /// <summary>Bind the pipe and start serving. Idempotent.</summary>
    public void Start() => _server.Start();

    /// <summary>Stop the listener; a connection currently being served finishes best-effort.</summary>
    public void Stop() => _server.Stop();

    /// <summary>Serialize an out-of-band store/session mutation (surface-thread title/cwd/exit
    /// reports) with the dispatch gate. NEVER call from a thread a realizer marshals work onto
    /// (the WebView2 UI thread) — realization runs under this same gate and would deadlock;
    /// surface callbacks hop to a worker first.</summary>
    public void RunLocked(Action action)
    {
        lock (_gate) action();
    }

    public void Dispose() => Stop();

    /// <summary>The pipe server's dispatch delegate: every request runs under the host gate because
    /// the AppStore/ring are confined, not thread-safe (the upstream server is @MainActor). With no
    /// dispatch scope configured, this preserves the legacy fully synchronous path.</summary>
    private Task<ControlResponse?> DispatchLocked(ControlRequest request)
    {
        lock (_gate)
        {
            return Task.FromResult(_dispatcher.Dispatch(request).GetAwaiter().GetResult());
        }
    }

    private async Task<ControlResponse?> DispatchWithScopeAsync(
        ControlRequest request,
        IControlDispatchScopeFactory dispatchScopeFactory,
        CancellationToken cancellationToken)
    {
        using var scope = dispatchScopeFactory.Begin();
        ControlResponse? response;
        lock (_gate)
        {
            response = _dispatcher.Dispatch(request).GetAwaiter().GetResult();
        }

        await scope.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return response;
    }
}
