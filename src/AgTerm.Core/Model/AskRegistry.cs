using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>Who holds a pending question: a window's modal slot (GUI) or a session (terminal).
/// Both carry the owning window, which scopes result and cancel lookups.</summary>
public abstract record AskOwner(Guid WindowId)
{
    public sealed record Window(Guid WindowId) : AskOwner(WindowId);

    public sealed record Session(Guid SessionId, Guid WindowId) : AskOwner(WindowId);
}

/// <summary>A question found by id. LiveOwner is null once the question has finished.</summary>
public sealed record AskLookup(ControlAskResult Result, Guid WindowId, AskOwner? LiveOwner);

/// <summary>Port of upstream AskRegistry: every pending question's owner, and finished results
/// after their owners are gone, so a CLI polling by id finds its question wherever it went.
/// Upstream asks the owner whether it still holds the id; here the owner's model is behind the
/// window lock and polls come on the pipe thread, so the registry keeps the pending ids itself.
/// Owners register and fill their slot in one step and retain before they clear it.</summary>
public sealed class AskRegistry
{
    /// <summary>One store for both styles, ordered by resolution; nothing frees an entry for a
    /// window that never reopens.</summary>
    public const int RetainedResultLimit = 32;

    private readonly object _gate = new();
    private readonly Dictionary<string, AskOwner> _live = new(StringComparer.Ordinal);
    private readonly List<(string Id, ControlAskResult Result, Guid WindowId)> _finished = new();

    public bool Register(string id, AskOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_gate)
        {
            if (_live.ContainsKey(id) || _finished.Exists(entry => entry.Id == id)) return false;
            _live[id] = owner;
            return true;
        }
    }

    /// <summary>The first terminal outcome wins; pending ids are never evicted.</summary>
    public bool Retain(string id, ControlAskResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Result == ControlAskOutcome.Pending) return false;
        lock (_gate)
        {
            if (!_live.Remove(id, out var owner)) return false;
            _finished.Add((id, result, owner.WindowId));
            if (_finished.Count > RetainedResultLimit) _finished.RemoveAt(0);
            return true;
        }
    }

    public AskOwner? Owner(string id)
    {
        lock (_gate) return _live.GetValueOrDefault(id);
    }

    public AskLookup? Find(string id)
    {
        lock (_gate)
        {
            if (_live.TryGetValue(id, out var owner))
                return new AskLookup(ControlAskResult.Pending(), owner.WindowId, owner);
            var index = _finished.FindLastIndex(entry => entry.Id == id);
            return index < 0 ? null : new AskLookup(_finished[index].Result, _finished[index].WindowId, null);
        }
    }

    /// <summary>ask.result: an explicit window must be the owner's, for a pending question and for
    /// a retained result alike.</summary>
    public ControlResponse Result(string id, Guid? windowId)
    {
        if (Find(id) is not { } found || (windowId is { } wanted && found.WindowId != wanted))
            return Unknown(id);
        return new ControlResponse { Ok = true, Result = new ControlResult { Ask = found.Result } };
    }

    public static ControlResponse Unknown(string id) => new() { Ok = false, Error = $"unknown ask: {id}" };
}
