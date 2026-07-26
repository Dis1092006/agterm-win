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

    /// <summary>The UI half of realization: create a renderer (a WebView2 pane) and open the
    /// surface over it, ON the WebView2 UI thread. The host calls it from the dispatch thread and
    /// blocks (bounded) — the implementation must marshal internally and must NOT touch the host
    /// gate (RunLocked) on the UI thread.</summary>
    public required Func<Session, XtermSurfaceOptions, Task<XtermPaneSurface>> OpenSurface { get; init; }

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

    public LiveControlHost(LiveControlHostOptions options)
    {
        _options = options;
        Host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = options.StateDir,
            ConfigDir = options.ConfigDir,
            PipeName = options.PipeName,
            WindowName = options.WindowName,
            AvailableThemes = options.AvailableThemes,
            HomeDirectory = options.HomeDirectory,
            Log = options.Log,
            SurfaceRealizer = Realize,
        });
    }

    public HeadlessHost Host { get; }

    public string PipeName => Host.PipeName;

    public void Start() => Host.Start();

    /// <summary>A surface realized after this host's own bootstrap (i.e. any control- or UI-driven
    /// show). The app uses it to mount the pane's view; fired on the realizing (dispatch) thread.</summary>
    public event Action<Session, XtermPaneSurface>? SurfaceRealized;

    /// <summary>The dispatch-gate half of realization; the UI half is the injected OpenSurface.
    /// Runs under the host gate on the dispatch thread — blocking here is safe because the UI
    /// thread never takes the gate (surface callbacks hop to the pool first).</summary>
    private ITerminalSurface? Realize(Session session)
    {
        try
        {
            var argv = session.InitialCommand is { } command
                ? CommandTokenizer.Tokenize(command)
                : new[] { ShellResolver.Resolve(_options.DefaultShell) };
            if (argv.Count == 0) return null;
            var surface = _options.OpenSurface(session, new XtermSurfaceOptions
                {
                    Argv = argv,
                    Cwd = ExistingDirectoryOrNull(session.EffectiveCwd),
                    BaseFontSize = session.FontSize ?? 14,
                    ReadClipboard = _options.ReadClipboard,
                })
                .WaitAsync(_options.RealizeBudget).GetAwaiter().GetResult();
            Bind(session, surface);
            SurfaceRealized?.Invoke(session, surface);
            return surface;
        }
        catch (Exception ex)
        {
            // a failed realization is the upstream "surface never realized" shape: the arm
            // reports the pinned wire error; the app stays alive
            _options.Log?.Invoke($"surface realization failed for {session.Id}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Live reports → session fields. Every mutation hops to the thread pool and takes
    /// the host gate — NEVER the gate on the UI thread (deadlock against a blocking Realize).</summary>
    private void Bind(Session session, XtermPaneSurface surface)
    {
        surface.Pane.TitleChanged += title => Task.Run(() => Host.RunLocked(() =>
        {
            if (OwnsMainPane(session, surface)) session.OscTitle = title;
        }));
        surface.Pane.CwdChanged += cwd => Task.Run(() => Host.RunLocked(() =>
        {
            if (OwnsMainPane(session, surface)) session.CurrentCwd = cwd;
        }));
        _ = CloseOnExitAsync(session, surface);
    }

    /// <summary>The surface still is the session's main pane (not torn down / replaced) and the
    /// session is still in the tree. Surface null covers the realize window before the store
    /// assigns the returned surface.</summary>
    private bool OwnsMainPane(Session session, XtermPaneSurface surface) =>
        Host.Store.SessionWithId(session.Id) is { } live &&
        (ReferenceEquals(live.Surface, surface) || live.Surface is null);

    private async Task CloseOnExitAsync(Session session, XtermPaneSurface surface)
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
            if (!OwnsMainPane(session, surface)) return; // closed via control, or pane replaced
            if (Host.Store.SessionWithId(session.Id) is not { } live) return;
            // session.new --command --wait: hold the pane open after the command exits
            if (live.InitialCommand is not null && live.CommandWait) return;
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
        Host.Stop();
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
