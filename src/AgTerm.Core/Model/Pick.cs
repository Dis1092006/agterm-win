using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>One picker request a window presents. Selection is already resolved to the matching
/// item's own id.</summary>
public sealed record PendingPick(
    string Id,
    IReadOnlyList<ControlPickItem> Items,
    string? Prompt,
    string? Query,
    bool AllowCustom,
    string? Selection);

/// <summary>A finished pick. Sequence orders answers across windows so app-wide retention drops the
/// oldest answer, not the oldest transfer.</summary>
public sealed record ResolvedPick(string Id, ControlPickResult Result, long Sequence);

/// <summary>Port of upstream PickController: the window's modal slot, held by a pick or a GUI
/// question, and the retained pick results.
/// The control dispatch thread opens and reads it, the UI thread resolves it.</summary>
public sealed class PickController
{
    /// <summary>A blocking client polls every 500 ms, so an answer survives any plausible number of
    /// pickers opened in the window before the poll lands.</summary>
    public const int RetainedResultLimit = 8;

    public const string AlreadyPendingError = "pick already pending";
    public const string PendingError = "pick pending";
    public const string AskAlreadyPendingError = "ask already pending";
    public const string AskPendingError = "ask pending";

    private static long s_sequence;
    private readonly object _gate = new();
    private readonly List<ResolvedPick> _recent = new();
    private PendingPick? _pending;
    private PendingAsk? _pendingAsk;
    private bool _closed;

    public PickController() : this(new AskRegistry())
    {
    }

    /// <summary>asks is the app-wide registry a GUI question registers in.</summary>
    public PickController(AskRegistry asks)
    {
        ArgumentNullException.ThrowIfNull(asks);
        Asks = asks;
    }

    public event Action? Changed;

    public AskRegistry Asks { get; }

    public PendingAsk? PendingAsk
    {
        get { lock (_gate) return _pendingAsk; }
    }

    public PendingPick? Pending
    {
        get { lock (_gate) return _pending; }
    }

    /// <summary>True once the window that owned this controller has closed. Open then fails for good,
    /// so a request queued on the closing window cannot get an id no registry path finds.</summary>
    public bool IsClosed
    {
        get { lock (_gate) return _closed; }
    }

    public string? PendingId => Pending?.Id;

    public bool ModalPending
    {
        get { lock (_gate) return _pending is not null || _pendingAsk is not null; }
    }

    /// <summary>The error a control command gets while the slot is taken, or null when free.</summary>
    public string? PendingModalError
    {
        get
        {
            lock (_gate)
                return _pendingAsk is not null ? AskPendingError : _pending is not null ? PendingError : null;
        }
    }

    /// <summary>The error a refused pick.open or ask.open answers: upstream names what holds the
    /// slot, not what the caller asked for.</summary>
    public string SlotTakenError
    {
        get { lock (_gate) return _pendingAsk is not null ? AskAlreadyPendingError : AlreadyPendingError; }
    }

    public bool Open(PendingPick pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        lock (_gate)
        {
            if (_closed || _pending is not null || _pendingAsk is not null) return false;
            _pending = pick;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Records the outcome when pickId is still the pending pick. A stale id (already
    /// answered or cancelled from the CLI) is ignored, so a late UI answer cannot overwrite it.</summary>
    public bool Resolve(string pickId, ControlPickResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        lock (_gate)
        {
            if (_pending?.Id != pickId) return false;
            FinishLocked(pickId, outcome);
        }
        Changed?.Invoke();
        return true;
    }

    public bool Cancel(string pickId) => Resolve(pickId, ControlPickResult.Cancelled());

    /// <summary>Registers the question and takes the slot in one step, so a poll can never find the
    /// id in neither place.</summary>
    public bool OpenAsk(PendingAsk ask, Guid windowId)
    {
        ArgumentNullException.ThrowIfNull(ask);
        lock (_gate)
        {
            if (_closed || _pending is not null || _pendingAsk is not null) return false;
            if (!Asks.Register(ask.Id, new AskOwner.Window(windowId))) return false;
            _pendingAsk = ask;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Records the outcome when askId is still the pending question; a stale id or a
    /// pending outcome is ignored, so a late UI answer cannot overwrite an ask.cancel.</summary>
    public bool ResolveAsk(string askId, ControlAskResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        lock (_gate)
        {
            if (!FinishAskLocked(askId, outcome)) return false;
        }
        Changed?.Invoke();
        return true;
    }

    public bool CancelAsk(string askId) => ResolveAsk(askId, ControlAskResult.Cancelled());

    /// <summary>The user's dismissal (Esc, the close_session chord), kept apart from an
    /// administrative cancel.</summary>
    public bool EscapeAsk(string askId) => ResolveAsk(askId, ControlAskResult.Escaped());

    private bool FinishAskLocked(string askId, ControlAskResult outcome)
    {
        if (_pendingAsk?.Id != askId || outcome.Result == ControlAskOutcome.Pending) return false;
        Asks.Retain(askId, outcome);
        _pendingAsk = null;
        return true;
    }

    public ControlPickResult? Result(string pickId)
    {
        lock (_gate)
        {
            if (_pending?.Id == pickId) return ControlPickResult.Pending();
            return _recent.LastOrDefault(r => r.Id == pickId)?.Result;
        }
    }

    internal void CancelPending()
    {
        if (PendingId is { } id) Cancel(id);
        if (PendingAsk is { } ask) CancelAsk(ask.Id);
    }

    /// <summary>Closes the controller and cancels its pending pick in one step. Changed is not raised
    /// here: the registry calls it only after releasing its own lock, because a handler may read the
    /// registry.</summary>
    internal bool CloseAndCancelPending()
    {
        lock (_gate)
        {
            _closed = true;
            var changed = false;
            if (_pending is { } pending)
            {
                FinishLocked(pending.Id, ControlPickResult.Cancelled());
                changed = true;
            }
            if (_pendingAsk is { } ask)
                changed |= FinishAskLocked(ask.Id, ControlAskResult.Cancelled());
            return changed;
        }
    }

    internal void RaiseChanged() => Changed?.Invoke();

    private void FinishLocked(string pickId, ControlPickResult outcome)
    {
        _recent.RemoveAll(r => r.Id == pickId);
        _recent.Add(new ResolvedPick(pickId, outcome, Interlocked.Increment(ref s_sequence)));
        if (_recent.Count > RetainedResultLimit)
            _recent.RemoveRange(0, _recent.Count - RetainedResultLimit);
        _pending = null;
    }

    internal IReadOnlyList<ResolvedPick> RecentResults
    {
        get { lock (_gate) return _recent.ToArray(); }
    }
}

/// <summary>Port of upstream PickRegistry: every open window's controller plus the results of
/// windows that have closed, so a CLI polling by id finds its pick wherever it went.</summary>
public sealed class PickRegistry
{
    /// <summary>Nothing frees an entry for a window that never reopens, so window churn would grow
    /// this without a cap.</summary>
    public const int RetainedResultLimit = 32;

    public const string WindowNotOpenError = "window not open — window.select it first";

    private readonly object _gate = new();
    private readonly Dictionary<Guid, PickController> _controllers = new();
    private readonly List<(Guid WindowId, ResolvedPick Pick)> _retained = new();

    public PickRegistry() : this(new AskRegistry())
    {
    }

    public PickRegistry(AskRegistry asks)
    {
        ArgumentNullException.ThrowIfNull(asks);
        Asks = asks;
    }

    /// <summary>The app-wide question registry. Every window's PickController and AppStore use this
    /// one instance, so ask.result finds a question in any window.</summary>
    public AskRegistry Asks { get; }

    public void Register(Guid windowId, PickController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        lock (_gate) _controllers[windowId] = controller;
    }

    /// <summary>Cancels the window's pending pick and keeps its results readable after the window
    /// is gone.</summary>
    public void Unregister(Guid windowId)
    {
        PickController controller;
        bool cancelled;
        lock (_gate)
        {
            // one lock for remove, cancel and copy: a poll in between would find the pick nowhere
            if (!_controllers.Remove(windowId, out var removed)) return;
            controller = removed;
            cancelled = controller.CloseAndCancelPending();
            _retained.AddRange(controller.RecentResults.Select(pick => (windowId, pick)));
            TrimRetainedLocked();
        }
        if (cancelled) controller.RaiseChanged();
    }

    /// <summary>Keeps the newest answers app-wide. A window closing later arrives with a whole batch,
    /// so appending alone would evict a newer answer an earlier-closing window held.</summary>
    private void TrimRetainedLocked()
    {
        if (_retained.Count <= RetainedResultLimit) return;
        _retained.Sort((x, y) => x.Pick.Sequence.CompareTo(y.Pick.Sequence));
        _retained.RemoveRange(0, _retained.Count - RetainedResultLimit);
    }

    public void CancelAll()
    {
        PickController[] controllers;
        lock (_gate) controllers = _controllers.Values.ToArray();
        foreach (var controller in controllers) controller.CancelPending();
    }

    public PickController? ControllerFor(Guid windowId)
    {
        lock (_gate) return _controllers.GetValueOrDefault(windowId);
    }

    public ControlResponse Result(string pickId, Guid? windowId)
    {
        if (windowId is null)
        {
            if (LiveController(pickId)?.Result(pickId) is { } live) return Ok(live);
            return RetainedFor(pickId) is { } retained ? Ok(retained.Pick.Result) : Unknown(pickId);
        }
        if (RetainedFor(pickId) is { } kept)
            return kept.WindowId == windowId ? Ok(kept.Pick.Result) : Unknown(pickId);
        if (ControllerFor(windowId.Value) is not { } controller) return Err(WindowNotOpenError);
        return controller.Result(pickId) is { } result ? Ok(result) : Unknown(pickId);
    }

    public ControlResponse Cancel(string pickId, Guid? windowId)
    {
        if (windowId is null)
        {
            if (LiveController(pickId) is { } live)
            {
                live.Cancel(pickId);
                return new ControlResponse { Ok = true };
            }
            return RetainedFor(pickId) is not null ? new ControlResponse { Ok = true } : Unknown(pickId);
        }
        if (RetainedFor(pickId) is { } kept)
            return kept.WindowId == windowId ? new ControlResponse { Ok = true } : Unknown(pickId);
        if (ControllerFor(windowId.Value) is not { } controller) return Err(WindowNotOpenError);
        if (controller.Result(pickId) is null) return Unknown(pickId);
        controller.Cancel(pickId);
        return new ControlResponse { Ok = true };
    }

    private PickController? LiveController(string pickId)
    {
        PickController[] controllers;
        lock (_gate) controllers = _controllers.Values.ToArray();
        return controllers.FirstOrDefault(c => c.Result(pickId) is not null);
    }

    private (Guid WindowId, ResolvedPick Pick)? RetainedFor(string pickId)
    {
        lock (_gate)
        {
            for (var i = _retained.Count - 1; i >= 0; i--)
                if (_retained[i].Pick.Id == pickId) return _retained[i];
            return null;
        }
    }

    private static ControlResponse Ok(ControlPickResult result) =>
        new() { Ok = true, Result = new ControlResult { Pick = result } };

    private static ControlResponse Unknown(string pickId) => Err($"unknown pick: {pickId}");

    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };
}
