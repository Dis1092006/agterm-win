using AgTerm.Core.Keymap;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Hooks;

/// <summary>
/// The app-wide hooks owner: the hooks.conf store, the scheduler and the last parse diagnostics.
/// Hosts feed it every ring event and call Reload for hooks.reload; the app supplies the launcher.
/// Nothing runs until the first Reload, so a host chooses when hooks switch on.
/// </summary>
public sealed class HooksController : IDisposable
{
    private readonly object _gate = new();
    private readonly HookScheduler _scheduler;
    private readonly System.Threading.Channels.Channel<Action> _pump =
        System.Threading.Channels.Channel.CreateUnbounded<Action>(
            new System.Threading.Channels.UnboundedChannelOptions { SingleReader = true });
    private HooksStore _store;
    private IReadOnlyList<KeymapDiagnostic> _diagnostics = Array.Empty<KeymapDiagnostic>();
    private volatile bool _armed;

    public HooksController(HooksStore store, IHookLauncher launcher, Func<DateTimeOffset>? now = null)
    {
        _store = store;
        _scheduler = new HookScheduler(launcher, now);
        _scheduler.OnFailure = (entry, detail) =>
            HookFailed?.Invoke(entry.Kind.ToWire(), entry.Command, detail);
        _ = Task.Run(RunPumpAsync);
    }

    /// <summary>Raised once per failing hook (kind wire name, command, detail) until it succeeds or the
    /// file reloads. May come from a process thread, so a subscriber must hop to its own thread.</summary>
    public event Action<string, string, string>? HookFailed;

    public string Path
    {
        get { lock (_gate) return _store.Path; }
    }

    /// <summary>Creates the commented starter hooks.conf when none exists (Edit Hooks).</summary>
    public bool EnsureStarter()
    {
        lock (_gate) return _store.EnsureStarter();
    }

    /// <summary>Re-reads the file and applies it; returns the diagnostic count.</summary>
    public int Reload()
    {
        int count;
        lock (_gate) count = ReloadLocked();
        Reloaded?.Invoke(count);
        return count;
    }

    /// <summary>Raised after every reload, outside the lock, with its diagnostic count (upstream
    /// SettingsModel.reloadHooks posts the banner from there). May come from any thread.</summary>
    public event Action<int>? Reloaded;

    private int ReloadLocked()
    {
        var (hooks, diagnostics) = _store.Load();
        _diagnostics = diagnostics;
        _scheduler.Apply(hooks);
        _armed = true;
        return diagnostics.Count;
    }

    /// <summary>The last reload's diagnostics, for a banner.</summary>
    public IReadOnlyList<KeymapDiagnostic> Diagnostics
    {
        get { lock (_gate) return _diagnostics; }
    }

    /// <summary>Moves hooks.conf to another config directory and reloads, unless it is already there.</summary>
    public void UseConfigDirectory(string configDirectory)
    {
        int count;
        lock (_gate)
        {
            var next = new HooksStore(configDirectory);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(System.IO.Path.GetFullPath(_store.Path), System.IO.Path.GetFullPath(next.Path), comparison))
                return;
            _store = next;
            count = ReloadLocked();
        }
        Reloaded?.Invoke(count);
    }

    public void Dispatch(ControlEvent controlEvent) => _scheduler.Dispatch(controlEvent);

    /// <summary>Queues an event for a single consumer that calls Dispatch in order. Cheap and
    /// non-blocking, so the ring can call it under its lock while a spawn happens elsewhere. Events
    /// posted before the first Reload are dropped, so none fires late against hooks loaded after it.</summary>
    public void Post(ControlEvent controlEvent)
    {
        if (_armed) _pump.Writer.TryWrite(() => Dispatch(controlEvent));
    }

    /// <summary>Completes once every event posted before this call has been dispatched.</summary>
    public Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pump.Writer.TryWrite(() => done.SetResult());
        return done.Task;
    }

    public void Dispose()
    {
        _pump.Writer.TryComplete();
        _scheduler.Dispose();
    }

    private async Task RunPumpAsync()
    {
        await foreach (var work in _pump.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try { work(); }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                System.Diagnostics.Trace.TraceError($"Hook dispatch failed: {e}");
            }
        }
    }

    public ControlHooks List()
    {
        lock (_gate)
        {
            return new ControlHooks
            {
                Path = _store.Path,
                Diagnostics = _diagnostics.Select(d => new ControlKeymapDiagnostic { Line = d.Line, Message = d.Message }).ToArray(),
                Hooks = _scheduler.Status(),
            };
        }
    }
}
