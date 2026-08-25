using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Terminal;

namespace AgTerm.Xterm;

/// <summary>Construction options for <see cref="LiveControlHost"/> — the HeadlessHostOptions
/// subset the app configures, plus the live-surface machinery.</summary>
public sealed record LiveControlHostOptions
{
    public string? StateDir { get; init; }

    public string? ConfigDir { get; init; }

    public string? PipeName { get; init; }

    public string WindowName { get; init; } = "main";

    public IReadOnlyList<string> AvailableThemes { get; init; } = Array.Empty<string>();

    public string? HomeDirectory { get; init; }

    /// <summary>The configured default shell; null = the ShellResolver policy (pwsh → cmd).</summary>
    public string? DefaultShell { get; init; }

    /// <summary>System clipboard reader handed to every surface (session.paste).</summary>
    public Func<string?>? ReadClipboard { get; init; }

    public Action<string>? Log { get; init; }

    /// <summary>Reports whether the current callback is running on the owning UI thread.</summary>
    public Func<bool> IsUiThread { get; init; } = static () => false;

    /// <summary>The UI half of realization: create a renderer (a WebView2 pane) and open the
    /// surface over it, ON the WebView2 UI thread. The host calls it from the dispatch thread and
    /// blocks (bounded) — the implementation must marshal internally and must NOT touch the host
    /// gate (RunLocked) on the UI thread.</summary>
    public required Func<SurfaceOpenRequest, CancellationToken, Task<IHeadlessSurface>> OpenSurface { get; init; }

    /// <summary>How long a realization may take before the show reports "session not realized".</summary>
    public TimeSpan RealizeBudget { get; init; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// The app's control host: the headless host's control plane (one window, one AppStore, named-pipe
/// server, event ring, snapshot bootstrap/persistence) with sessions realizing REAL surfaces —
/// ConPTY children rendered by xterm.js — through the store's SurfaceRealizer seam. Also owns the
/// live-surface lifecycle: OSC title/cwd reports flow into the session fields (serialized via
/// RunLocked off the UI thread), and a primary shell's exit closes its pane the upstream way
/// (ClosePrimaryPane; a --wait command session is left open, showing its final screen).
/// </summary>
public sealed class LiveControlHost : IDisposable
{
    private readonly LiveControlHostOptions _options;
    private readonly LiveDispatchScopeFactory _scopeFactory;
    private readonly LiveStoreCoordinator _storeCoordinator;
    private readonly LiveSurfaceCoordinator? _coordinator;
    private HeadlessHost? _host;
    private int _disposed;

    public LiveControlHost(LiveControlHostOptions options)
    {
        _options = options;
        _scopeFactory = new LiveDispatchScopeFactory();
        Host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = options.StateDir,
            ConfigDir = options.ConfigDir,
            PipeName = options.PipeName,
            WindowName = options.WindowName,
            AvailableThemes = options.AvailableThemes,
            HomeDirectory = options.HomeDirectory,
            Log = options.Log,
            SurfaceRealizer = ReserveSurface,
            DispatchScopeFactory = _scopeFactory,
            // the live app's eager deck: every session's pane exists from creation/restore, so
            // session.type works into background sessions without a select (phase 4 DoD)
            EagerSurfaceRealization = true,
            DeferBootstrap = true,
        });
        _storeCoordinator = new LiveStoreCoordinator(Host, options.IsUiThread);
        _coordinator = new LiveSurfaceCoordinator(
            Host,
            _scopeFactory,
            options.OpenSurface,
            options.RealizeBudget,
            (sessionId, realizedSurface) =>
            {
                if (realizedSurface is XtermPaneSurface paneSurface)
                {
                    Bind(sessionId, paneSurface);
                    SurfaceRealized?.Invoke(sessionId, paneSurface);
                }
            },
            options.Log);
        using var bootstrapScope = _scopeFactory.Begin();
        Host.Bootstrap();
        bootstrapScope.CompleteAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public HeadlessHost Host
    {
        get => _host ?? throw new InvalidOperationException(
            "LiveControlHost invariant violated: Host is not initialized.");
        private set => _host = value;
    }

    public string PipeName => Host.PipeName;

    public ILiveStoreCoordinator StoreCoordinator => _storeCoordinator;

    public void Start() => Host.Start();

    public Task PerformFontActionAsync(string action, CancellationToken cancellationToken = default) =>
        StoreCoordinator.MutateAsync(
            _ => Host.Actions.Font(null, null, null, action),
            cancellationToken);

    /// <summary>A surface realized after this host's own bootstrap (i.e. any control- or UI-driven
    /// show). The app uses it to mount the pane's view; fired on the realizing (dispatch) thread.</summary>
    public event Action<Guid, XtermPaneSurface>? SurfaceRealized;

    /// <summary>Reserve a pending surface under the host gate. The coordinator opens and commits
    /// the real surface asynchronously after bootstrap dispatch completes.</summary>
    private ITerminalSurface? ReserveSurface(Session session)
    {
        if (_host is null)
            throw new InvalidOperationException(
                "LiveControlHost invariant violated: Host is not initialized.");

        var argv = session.InitialCommand is { } command
            ? CommandTokenizer.Tokenize(command)
            : new[] { ShellResolver.Resolve(_options.DefaultShell) };
        if (argv.Count == 0)
        {
            return null;
        }

        var request = new SurfaceOpenRequest(
            session.Id,
            LiveSurfaceKind.Main,
            new XtermSurfaceOptions
            {
                Argv = argv.ToArray(),
                Cwd = ExistingDirectoryOrNull(session.EffectiveCwd),
                BaseFontSize = session.FontSize ?? 14,
                ReadClipboard = _options.ReadClipboard,
            });

        var coordinator = _coordinator ?? throw new InvalidOperationException(
            "LiveControlHost invariant violated: coordinator is not initialized.");
        return coordinator.Reserve(request);
    }

    /// <summary>Live reports → session fields. Every mutation hops to the thread pool and takes
    /// the host gate; the UI thread never enters RunLocked.</summary>
    private void Bind(Guid sessionId, XtermPaneSurface surface)
    {
        surface.Pane.TitleChanged += title => Task.Run(() => Host.RunLocked(() =>
        {
            var live = Host.Store.SessionWithId(sessionId);
            if (OwnsMainPane(live, surface))
            {
                live!.OscTitle = title;
                Host.Store.NotifySidebar();
            }
        }));
        surface.Pane.CwdChanged += cwd => Task.Run(() => Host.RunLocked(() =>
        {
            var live = Host.Store.SessionWithId(sessionId);
            if (OwnsMainPane(live, surface))
            {
                live!.CurrentCwd = cwd;
                Host.Store.NotifySidebar();
            }
        }));
        _ = CloseOnExitAsync(sessionId, surface);
    }

    /// <summary>The realized surface still is the session's current main pane.</summary>
    private static bool OwnsMainPane(Session? live, XtermPaneSurface surface) =>
        live is not null &&
        ReferenceEquals(live.Surface, surface);

    private async Task CloseOnExitAsync(Guid sessionId, XtermPaneSurface surface)
    {
        try
        {
            await surface.Pane.Exited.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return; // a torn-down pane may fault the exit task; nothing to close then
        }
        Host.RunLocked(() =>
        {
            var live = Host.Store.SessionWithId(sessionId);
            if (!OwnsMainPane(live, surface)) return; // closed via control, or pane replaced
            // session.new --command --wait: hold the pane open after the command exits
            if (live!.InitialCommand is not null && live.CommandWait) return;
            Host.Store.ClosePrimaryPane(live.Id);
        });
    }

    /// <summary>A session restored with a stale cwd must still spawn (upstream falls back to home).</summary>
    private string? ExistingDirectoryOrNull(string? cwd) =>
        cwd is not null && Directory.Exists(cwd) ? cwd : null;

    /// <summary>Stop serving, flush the snapshot, and tear down every live pane (killing the
    /// children) — the app-exit path (§3: save on exit).</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Host.Stop();
        _storeCoordinator.Dispose();
        _coordinator?.Dispose();
        Host.RunLocked(() =>
        {
            Host.Store.CancelPendingSave();
            Host.Store.Save();
            foreach (var session in Host.Store.Workspaces.SelectMany(w => w.Sessions))
            {
                session.Surface?.Teardown();
                session.SplitSurface?.Teardown();
                session.ScratchSurface?.Teardown();
                session.OverlaySurface?.Teardown();
            }
        });
    }
}
