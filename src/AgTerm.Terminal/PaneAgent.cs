using AgTerm.Core.Model;
using Microsoft.Win32.SafeHandles;

namespace AgTerm.Terminal;

/// <summary>
/// Pure classification and selection of an interactive agent CLI running in a terminal pane, plus
/// the event-driven monitor that watches the winning process handle until it exits.
/// </summary>
internal static class PaneAgent
{
    internal readonly record struct Candidate(
        int Pid,
        long CreationTime,
        long? ConsoleHostValue,
        SessionAgent Agent);

    internal static SessionAgent? ClassifyImage(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return null;
        var name = Path.GetFileName(imagePath.AsSpan());
        if (name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase)) return SessionAgent.Claude;
        if (name.Equals("copilot.exe", StringComparison.OrdinalIgnoreCase)) return SessionAgent.Copilot;
        if (name.Equals("codex.exe", StringComparison.OrdinalIgnoreCase)) return SessionAgent.Codex;
        return null;
    }

    internal static Candidate? Pick(IEnumerable<Candidate> candidates, long? paneConsoleHostValue)
    {
        bool requireHost;
        long expectedHost = 0;
        if (paneConsoleHostValue is not { } raw)
        {
            // Query unavailable (e.g. test seam): accept any live member in the pane's job.
            requireHost = false;
        }
        else if ((raw & 3) == 1 && (raw & ~3L) != 0)
        {
            requireHost = true;
            expectedHost = raw;
        }
        else
        {
            // Direct child has not finished attaching to its console host yet.
            return null;
        }

        Candidate? best = null;
        foreach (var candidate in candidates)
        {
            if (requireHost && candidate.ConsoleHostValue != expectedHost) continue;
            if (best is null
                || candidate.CreationTime < best.Value.CreationTime
                || (candidate.CreationTime == best.Value.CreationTime && candidate.Pid < best.Value.Pid))
            {
                best = candidate;
            }
        }
        return best;
    }
}

internal sealed class PaneAgentMonitor : IDisposable
{
    private const int InitialProbeDelayMs = 50;
    private const int TrailingProbeDelayMs = 350;

    private sealed class ProcessWaitHandle : WaitHandle
    {
        public ProcessWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    private readonly Func<(SessionAgent Agent, SafeWaitHandle ProcessHandle)?> _probe;
    private readonly Action<SessionAgent?> _onChanged;
    private readonly Lock _gate = new();
    private readonly Lock _probeGate = new();
    private readonly Timer _timer;

    private SessionAgent? _current;
    private ProcessWaitHandle? _trackedHandle;
    private RegisteredWaitHandle? _trackedWait;
    private int _hasTrackedProcess;
    private int _stopped;
    private bool _timerArmed;
    private bool _trailingProbePending;

    internal PaneAgentMonitor(
        Func<(SessionAgent Agent, SafeWaitHandle ProcessHandle)?> probe,
        Action<SessionAgent?> onChanged)
    {
        _probe = probe;
        _onChanged = onChanged;
        _timer = new Timer(_ => RunProbe(fromTimer: true), null, Timeout.Infinite, Timeout.Infinite);
    }

    internal SessionAgent? Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    internal void NotifyActivity()
    {
        if (Volatile.Read(ref _stopped) != 0 || Volatile.Read(ref _hasTrackedProcess) != 0) return;
        lock (_gate)
        {
            if (_stopped != 0 || _hasTrackedProcess != 0) return;
            _trailingProbePending = true;
            if (_timerArmed) return;
            _timerArmed = true;
            try
            {
                _timer.Change(InitialProbeDelayMs, Timeout.Infinite);
            }
            catch (ObjectDisposedException)
            {
                _timerArmed = false;
            }
        }
    }

    private void RunProbe(bool fromTimer)
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        lock (_probeGate)
        {
            if (Volatile.Read(ref _stopped) != 0) return;
            bool hadTrailing;
            lock (_gate)
            {
                if (_stopped != 0 || _hasTrackedProcess != 0)
                {
                    _timerArmed = false;
                    _trailingProbePending = false;
                    return;
                }
                hadTrailing = _trailingProbePending;
                _trailingProbePending = false;
                _timerArmed = false;
            }

            var found = _probe();
            if (found is { } active)
            {
                bool changed;
                lock (_gate)
                {
                    if (_stopped != 0)
                    {
                        active.ProcessHandle.Dispose();
                        return;
                    }
                    var waitHandle = new ProcessWaitHandle(active.ProcessHandle);
                    var registration = ThreadPool.RegisterWaitForSingleObject(
                        waitHandle,
                        (_, _) => OnTrackedProcessExited(waitHandle),
                        null,
                        Timeout.Infinite,
                        executeOnlyOnce: true);
                    _trackedHandle = waitHandle;
                    _trackedWait = registration;
                    Volatile.Write(ref _hasTrackedProcess, 1);
                    _trailingProbePending = false;
                    if (_timerArmed)
                    {
                        try { _timer.Change(Timeout.Infinite, Timeout.Infinite); }
                        catch (ObjectDisposedException) { }
                        _timerArmed = false;
                    }
                    changed = _current != active.Agent;
                    _current = active.Agent;
                }
                if (changed) _onChanged(active.Agent);
                return;
            }

            bool cleared;
            lock (_gate)
            {
                cleared = _current is not null;
                _current = null;
                if (_stopped == 0 && fromTimer && hadTrailing && !_timerArmed)
                {
                    _timerArmed = true;
                    try { _timer.Change(TrailingProbeDelayMs, Timeout.Infinite); }
                    catch (ObjectDisposedException) { _timerArmed = false; }
                }
            }
            if (cleared) _onChanged(null);
        }
    }

    private void OnTrackedProcessExited(ProcessWaitHandle exitedHandle)
    {
        RegisteredWaitHandle? registration;
        lock (_gate)
        {
            if (!ReferenceEquals(_trackedHandle, exitedHandle)) return;
            registration = _trackedWait;
            _trackedWait = null;
            _trackedHandle = null;
            Volatile.Write(ref _hasTrackedProcess, 0);
        }
        registration?.Unregister(null);
        exitedHandle.Dispose();
        if (Volatile.Read(ref _stopped) == 0)
        {
            RunProbe(fromTimer: false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        RegisteredWaitHandle? registration;
        ProcessWaitHandle? handle;
        bool cleared;
        lock (_gate)
        {
            registration = _trackedWait;
            handle = _trackedHandle;
            _trackedWait = null;
            _trackedHandle = null;
            Volatile.Write(ref _hasTrackedProcess, 0);
            _timerArmed = false;
            _trailingProbePending = false;
            cleared = _current is not null;
            _current = null;
        }
        _timer.Dispose();
        registration?.Unregister(null);
        handle?.Dispose();
        if (cleared) _onChanged(null);
    }
}
