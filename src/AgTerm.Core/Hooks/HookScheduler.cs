using AgTerm.Core.Protocol;

namespace AgTerm.Core.Hooks;

/// <summary>Starts one hook child. Throwing means no child started and neither callback will ever
/// run. A returned pid promises exactly one onExit, delivered after input cleanup and after any
/// onDeliveryFailure, and neither callback runs inline from Start.</summary>
public interface IHookLauncher
{
    int Start(HookEntry entry, ControlEvent controlEvent, Action<string> onDeliveryFailure, Action<int> onExit);
}

/// <summary>
/// Runs hooks.conf entries against control events: one child per hook at a time, an ordered bounded
/// queue behind it, and only process exit releasing the slot. Host-free; the app supplies the
/// launcher. Callbacks arrive from process threads, so state is guarded by one re-entrant lock.
/// </summary>
public sealed class HookScheduler
{
    public const int PendingCapacity = 256;

    private sealed class Running
    {
        public required int Pid { get; init; }
        public required DateTimeOffset StartedAt { get; init; }
        public required ulong Run { get; init; }
        public bool DeliveryFailed { get; set; }
    }

    private sealed class Record
    {
        public Record(HookEntry entry) => Entry = entry;

        public HookEntry Entry { get; set; }
        public Running? Running { get; set; }
        public List<ControlEvent> Pending { get; set; } = new();
        public ulong Dropped { get; set; }
        public string? LastFailure { get; set; }
        public bool BannerShown { get; set; }
        public bool Retired { get; set; }
    }

    private readonly object _gate = new();
    private readonly IHookLauncher _launcher;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<HookIdentity, Record> _records = new();
    private List<HookIdentity> _order = new();
    private ulong _nextRun = 1;

    public HookScheduler(IHookLauncher launcher, Func<DateTimeOffset>? now = null)
    {
        _launcher = launcher;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The sole banner source: called once per hook failure until that hook succeeds or the
    /// file reloads. Runs under the scheduler lock, so it must not block.</summary>
    public Action<HookEntry, string>? OnFailure { get; set; }

    /// <summary>Replaces the definitions. An unchanged identity keeps its child, queue and counters
    /// and takes the new line; a removed one drops its queue, accepts nothing and stays until its
    /// child exits; a re-add before that exit reattaches. Every banner is re-armed.</summary>
    public void Apply(HooksConfig hooks)
    {
        lock (_gate)
        {
            var live = hooks.Entries.Select(e => e.Identity).ToHashSet();
            foreach (var (identity, record) in _records.ToArray())
            {
                if (live.Contains(identity)) continue;
                record.Retired = true;
                record.Pending = new List<ControlEvent>();
                if (record.Running is null) _records.Remove(identity);
            }
            foreach (var entry in hooks.Entries)
            {
                if (_records.TryGetValue(entry.Identity, out var record))
                {
                    record.Entry = entry;
                    record.Retired = false;
                }
                else
                {
                    _records[entry.Identity] = new Record(entry);
                }
            }
            foreach (var record in _records.Values) record.BannerShown = false;
            _order = hooks.Entries.Select(e => e.Identity).ToList();
        }
    }

    /// <summary>Fans one ring event out to every live hook of its kind.</summary>
    public void Dispatch(ControlEvent controlEvent)
    {
        lock (_gate)
        {
            foreach (var identity in _order)
            {
                if (!_records.TryGetValue(identity, out var record) || record.Retired
                    || record.Entry.Kind != controlEvent.Kind) continue;
                if (record.Running is null)
                {
                    Start(record, controlEvent);
                    continue;
                }
                record.Pending.Add(controlEvent);
                if (record.Pending.Count > PendingCapacity)
                {
                    record.Pending.RemoveAt(0);
                    record.Dropped++;
                }
            }
        }
    }

    /// <summary>The live hooks in file order, then any removed hook whose child is still running,
    /// for hooks.list.</summary>
    public ControlHookEntry[] Status()
    {
        lock (_gate)
        {
            var live = _order.Where(_records.ContainsKey).Select(id => Row(_records[id]));
            var retired = _records.Values.Where(r => r.Retired && r.Running is not null)
                .OrderBy(r => r.Entry.Kind.ToWire(), StringComparer.Ordinal)
                .ThenBy(r => r.Entry.Command, StringComparer.Ordinal)
                .Select(Row);
            return live.Concat(retired).ToArray();
        }
    }

    private ControlHookEntry Row(Record record) => new()
    {
        Kind = record.Entry.Kind.ToWire(),
        Command = record.Entry.Command,
        Line = record.Entry.Line,
        RunningPid = record.Running?.Pid,
        ElapsedSeconds = record.Running is { } running ? (_now() - running.StartedAt).TotalSeconds : null,
        Pending = record.Pending.Count,
        Dropped = record.Dropped,
        LastFailure = record.LastFailure,
        Retired = record.Retired ? true : null,
    };

    private void Start(Record record, ControlEvent controlEvent)
    {
        ControlEvent? next = controlEvent;
        while (next is { } current)
        {
            next = null;
            var identity = record.Entry.Identity;
            var run = _nextRun++;
            try
            {
                var pid = _launcher.Start(record.Entry, current,
                    message => DeliveryFailed(identity, run, message),
                    status => Exited(identity, run, status));
                record.Running = new Running { Pid = pid, StartedAt = _now(), Run = run };
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Fail(record, $"spawn failed: {e.Message}");
                if (record.Pending.Count > 0)
                {
                    next = record.Pending[0];
                    record.Pending.RemoveAt(0);
                }
            }
        }
    }

    private void DeliveryFailed(HookIdentity identity, ulong run, string message)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(identity, out var record) || record.Running is not { } running
                || running.Run != run) return;
            running.DeliveryFailed = true;
            Fail(record, $"delivery failed: {message}");
        }
    }

    private void Exited(HookIdentity identity, ulong run, int status)
    {
        lock (_gate)
        {
            if (!_records.TryGetValue(identity, out var record) || record.Running is not { } running
                || running.Run != run) return;
            record.Running = null;
            if (status != 0)
            {
                Fail(record, $"exit {status}");
            }
            else if (!running.DeliveryFailed)
            {
                record.LastFailure = null;
                record.BannerShown = false;
            }
            if (record.Retired)
            {
                _records.Remove(identity);
                return;
            }
            if (record.Pending.Count > 0)
            {
                var next = record.Pending[0];
                record.Pending.RemoveAt(0);
                Start(record, next);
            }
        }
    }

    private void Fail(Record record, string message)
    {
        record.LastFailure = message;
        if (record.BannerShown) return;
        record.BannerShown = true;
        OnFailure?.Invoke(record.Entry, message);
    }
}
