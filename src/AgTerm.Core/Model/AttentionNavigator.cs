namespace AgTerm.Core.Model;

public sealed record AttentionCandidate(Guid SessionId, bool Stops);

/// <summary>One window's navigable sessions in sidebar order. <paramref name="Eligible"/> is false
/// while the window is under terminal zoom, the dashboard or a pending pick.</summary>
public sealed record AttentionWindow(Guid WindowId, bool Eligible, IReadOnlyList<AttentionCandidate> Sessions);

/// <summary>Pure cross-window attention stepping (upstream WindowLibrary.attentionStep).</summary>
public static class AttentionNavigator
{
    /// <summary>The next stop after <paramref name="selectedSessionId"/> of the source window, scanning
    /// every window in library order and wrapping. The current session is never returned.</summary>
    public static (Guid WindowId, Guid SessionId)? Step(
        IReadOnlyList<AttentionWindow> windows, Guid sourceWindowId, Guid? selectedSessionId, bool forward)
    {
        var candidates = new List<(Guid Window, AttentionCandidate Session)>();
        foreach (var window in windows)
        {
            if (window.WindowId != sourceWindowId && !window.Eligible) continue;
            foreach (var session in window.Sessions) candidates.Add((window.WindowId, session));
        }
        if (candidates.Count == 0) return null;

        var current = selectedSessionId is { } selected
            ? candidates.FindIndex(c => c.Window == sourceWindowId && c.Session.SessionId == selected)
            : -1;
        var start = current >= 0 ? current : forward ? -1 : candidates.Count;
        var steps = current >= 0 ? candidates.Count - 1 : candidates.Count;
        for (var i = 1; i <= steps; i++)
        {
            var index = forward ? start + i : start - i;
            index = ((index % candidates.Count) + candidates.Count) % candidates.Count;
            if (candidates[index].Session.Stops) return (candidates[index].Window, candidates[index].Session.SessionId);
        }
        return null;
    }
}
