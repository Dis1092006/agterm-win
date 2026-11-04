using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgTerm.Terminal;

/// <summary>
/// The processes a pane started. A job object only lists them: it has no kill-on-close limit
/// and is never terminated. When the pseudoconsole closes, conhost sends CTRL_CLOSE to the
/// processes attached to it, and a background sweep ends the ones that would never get it:
/// console programs whose creator died before they connected to a console, and programs that
/// attach to the pane's console after the close. Detached programs, programs with their own
/// console and GUI programs keep running. A failed query keeps the process.
/// </summary>
internal sealed class PaneDescendants
{
    internal enum Verdict
    {
        Keep,
        Attached,
        Kill,
    }

    private static readonly TimeSpan SweepLimit = TimeSpan.FromSeconds(30);

    private readonly IntPtr _job;
    private readonly Lock _jobGate = new();
    private readonly Func<IntPtr, long?> _query;
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<int, IntPtr> _members = [];
    // attached to the pane's console at close, or not known then: never a late attacher
    private readonly HashSet<int> _attachedAtClose = [];
    private readonly HashSet<int> _attached = [];
    private readonly HashSet<int> _killed = [];
    private int _consoleHostPid;
    private IntPtr _consoleHost;
    private int _released;

    private PaneDescendants(IntPtr job, Func<IntPtr, long?> query)
    {
        _job = job;
        _query = query;
    }

    /// <summary>Completes when the sweep has ended and every handle is closed.</summary>
    internal Task Completed => _completed.Task;

    internal static PaneDescendants? TryCreate(Func<IntPtr, long?>? query)
    {
        var job = ConPtyNative.CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return null;
        // a program that asks to leave the job (CREATE_BREAKAWAY_FROM_JOB) may; no other limit
        var limits = new ConPtyNative.JobObjectExtendedLimitInformation
        {
            LimitFlags = ConPtyNative.JobObjectLimitBreakawayOk,
        };
        if (!ConPtyNative.SetInformationJobObject(
                job, ConPtyNative.JobObjectExtendedLimitInformationClass, ref limits,
                Marshal.SizeOf<ConPtyNative.JobObjectExtendedLimitInformation>()))
        {
            ConPtyNative.CloseHandle(job);
            return null;
        }
        return new PaneDescendants(job, query ?? ConPtyNative.QueryConsoleHost);
    }

    internal bool TryAssign(IntPtr process) => ConPtyNative.AssignProcessToJobObject(_job, process);

    /// <summary>The job's LimitFlags, or null if they cannot be read.</summary>
    internal uint? LimitFlags
    {
        get
        {
            lock (_jobGate)
            {
                if (Volatile.Read(ref _released) != 0) return null;
                var info = new ConPtyNative.JobObjectExtendedLimitInformation();
                return ConPtyNative.QueryInformationJobObject(
                    _job, ConPtyNative.JobObjectExtendedLimitInformationClass, ref info,
                    Marshal.SizeOf<ConPtyNative.JobObjectExtendedLimitInformation>(), out _)
                    ? info.LimitFlags
                    : null;
            }
        }
    }

    /// <summary>Gives up on the sweep: closes the job and completes <see cref="Completed"/>.
    /// </summary>
    internal void Abandon()
    {
        ReleaseHandles();
        _completed.TrySetResult();
    }

    /// <summary>Called just before ClosePseudoConsole, while the direct child's handle is still
    /// open. Records which processes are attached to the pane's console now, then starts the
    /// sweep. If the console host cannot be found, nothing is swept.</summary>
    internal void BeginClose(IntPtr directChild)
    {
        try
        {
            var value = SafeQuery(directChild);
            if (value is not long host || (host & 3) != 1 || (host & ~3L) == 0)
            {
                Abandon();
                return;
            }
            _consoleHostPid = (int)(host & ~3L);
            _consoleHost = ConPtyNative.OpenProcess(ConPtyNative.Synchronize, false, _consoleHostPid);

            if (ConPtyNative.DuplicateHandle(
                    ConPtyNative.GetCurrentProcess(), directChild, ConPtyNative.GetCurrentProcess(),
                    out var child, 0, false, ConPtyNative.DuplicateSameAccess))
            {
                var childPid = ConPtyNative.GetProcessId(child);
                if (childPid != 0 && _members.TryAdd(childPid, child)) _killed.Add(childPid);
                else ConPtyNative.CloseHandle(child);
            }

            foreach (var pid in ListMembers())
            {
                // a member that cannot be opened or queried now may be attached; it is never
                // taken for a late attacher, so it is left to CTRL_CLOSE as before the sweep
                if (!TryOpenMember(pid, out var handle))
                {
                    _attachedAtClose.Add(pid);
                    continue;
                }
                var state = SafeQuery(handle);
                if (state is not long raw || (raw & 3) == 3) _attachedAtClose.Add(pid);
                else if (IsAttachedToPaneConsole(state))
                {
                    _attachedAtClose.Add(pid);
                    _attached.Add(pid);
                }
            }

            var sweeper = new Thread(Sweep) { IsBackground = true, Name = "conpty-sweep" };
            sweeper.Start();
        }
        catch (Exception)
        {
            Abandon();
        }
    }

    /// <summary>What to do with one pane process after the console closed. <paramref name="value"/>
    /// is its raw ProcessConsoleHostProcess value; null means the query failed.</summary>
    internal static Verdict Decide(
        long? value, int consoleHostPid, bool attachedAtClose,
        Func<int, bool> isBoundAndDead, Func<bool?> isConsoleSubsystem)
    {
        if (value is not long raw) return Verdict.Keep;
        var flags = raw & 3;
        var target = (int)(raw & ~3L);
        switch (flags)
        {
            case 1 when target == consoleHostPid && target != 0:
                // attached to the pane's console: conhost sent it CTRL_CLOSE, unless it came late
                return attachedAtClose ? Verdict.Attached : Verdict.Kill;
            case 0:
            case 2:
                // never connected (0) or still pending (2) on its creator: when the creator died
                // as part of this pane, a console program would wait forever
                if (target == 0 || !isBoundAndDead(target)) return Verdict.Keep;
                return isConsoleSubsystem() == true ? Verdict.Kill : Verdict.Keep;
            default:
                return Verdict.Keep;
        }
    }

    private void Sweep()
    {
        try
        {
            var clock = Stopwatch.StartNew();
            var consoleHostGone = false;
            while (true)
            {
                var killedAny = SweepOnce();
                if (consoleHostGone && !killedAny && AllKilledExited()) break;
                if (clock.Elapsed > SweepLimit) break;
                consoleHostGone = _consoleHost == IntPtr.Zero
                    || ConPtyNative.WaitForSingleObject(_consoleHost, 20) == 0;
                if (consoleHostGone) Thread.Sleep(20);
            }
        }
        catch (Exception)
        {
            // the sweep only adds to what conhost does; a failure leaves the rest to conhost
        }
        finally
        {
            ReleaseHandles();
            _completed.TrySetResult();
        }
    }

    private bool SweepOnce()
    {
        var killedAny = false;
        foreach (var pid in ListMembers())
        {
            if (_killed.Contains(pid)) continue;
            if (!TryOpenMember(pid, out var handle)) continue;
            if (ConPtyNative.WaitForSingleObject(handle, 0) == 0) continue;

            Verdict verdict;
            try
            {
                verdict = Decide(
                    SafeQuery(handle), _consoleHostPid, _attachedAtClose.Contains(pid),
                    IsBoundAndDead, () => IsConsoleSubsystem(handle));
            }
            catch (Exception)
            {
                verdict = Verdict.Keep;
            }

            if (verdict == Verdict.Attached) _attached.Add(pid);
            else if (verdict == Verdict.Kill)
            {
                ConPtyNative.TerminateProcess(handle, 1);
                _killed.Add(pid);
                killedAny = true;
            }
        }
        return killedAny;
    }

    private bool IsAttachedToPaneConsole(long? value) =>
        value is long raw && (raw & 3) == 1 && (int)(raw & ~3L) == _consoleHostPid;

    private bool IsBoundAndDead(int pid) =>
        (_killed.Contains(pid) || _attached.Contains(pid))
        && _members.TryGetValue(pid, out var handle)
        && ConPtyNative.WaitForSingleObject(handle, 0) == 0;

    private bool AllKilledExited()
    {
        foreach (var pid in _killed)
        {
            if (_members.TryGetValue(pid, out var handle)
                && ConPtyNative.WaitForSingleObject(handle, 0) != 0)
                return false;
        }
        return true;
    }

    private long? SafeQuery(IntPtr process)
    {
        try
        {
            return _query(process);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Opens a listed pid once and keeps the handle, so the pid cannot be reused while the
    /// sweep runs. A process that is not in the job (its pid was reused) is skipped.</summary>
    private bool TryOpenMember(int pid, out IntPtr handle)
    {
        if (_members.TryGetValue(pid, out handle)) return true;
        handle = ConPtyNative.OpenProcess(
            ConPtyNative.ProcessTerminate | ConPtyNative.ProcessQueryLimitedInformation
            | ConPtyNative.Synchronize, false, pid);
        if (handle == IntPtr.Zero) return false;
        if (!ConPtyNative.IsProcessInJob(handle, _job, out var inJob) || !inJob)
        {
            ConPtyNative.CloseHandle(handle);
            handle = IntPtr.Zero;
            return false;
        }
        _members[pid] = handle;
        return true;
    }

    private List<int> ListMembers()
    {
        var capacity = 64;
        while (true)
        {
            var length = 8 + capacity * IntPtr.Size;
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (ConPtyNative.QueryInformationJobObject(
                        _job, ConPtyNative.JobObjectBasicProcessIdList, buffer, length, out _))
                {
                    var count = Marshal.ReadInt32(buffer, 4);
                    var pids = new List<int>(count);
                    for (var i = 0; i < count; i++)
                        pids.Add((int)Marshal.ReadIntPtr(buffer, 8 + i * IntPtr.Size));
                    return pids;
                }
                if (Marshal.GetLastWin32Error() != ConPtyNative.ErrorMoreData || capacity >= 65536)
                    return [];
                capacity *= 4;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>True for a console-subsystem image, false for any other, null if unknown.</summary>
    private static bool? IsConsoleSubsystem(IntPtr process)
    {
        var name = new char[32768];
        var size = name.Length;
        if (!ConPtyNative.QueryFullProcessImageNameW(process, 0, name, ref size)) return null;
        return ReadSubsystem(new string(name, 0, size)) switch
        {
            null => null,
            var subsystem => subsystem == ImageSubsystemConsole,
        };
    }

    private const int ImageSubsystemConsole = 3;

    /// <summary>The PE optional header's Subsystem field, or null if the file cannot be read.
    /// </summary>
    internal static int? ReadSubsystem(string imagePath)
    {
        try
        {
            using var file = new FileStream(
                imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(file);
            if (reader.ReadUInt16() != 0x5A4D) return null;
            file.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            file.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550) return null;
            // the 20-byte file header, then Subsystem at offset 68 of the optional header in both
            // PE32 and PE32+
            file.Position = peOffset + 4 + 20 + 68;
            return reader.ReadUInt16();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ReleaseHandles()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0) return;
        foreach (var handle in _members.Values) ConPtyNative.CloseHandle(handle);
        _members.Clear();
        if (_consoleHost != IntPtr.Zero)
        {
            ConPtyNative.CloseHandle(_consoleHost);
            _consoleHost = IntPtr.Zero;
        }
        ReleaseJob();
    }

    private void ReleaseJob()
    {
        lock (_jobGate) ConPtyNative.CloseHandle(_job);
    }
}
