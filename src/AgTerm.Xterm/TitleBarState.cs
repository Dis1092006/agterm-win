using AgTerm.Core.Model;
using System.Collections.Frozen;

namespace AgTerm.Xterm;

/// <summary>The split button's glyph state (upstream's split-toggle accessibility value): no split,
/// both panes shown, or collapsed to the primary (left or top) or split (right or bottom) pane.</summary>
public enum TitleBarSplit
{
    None,
    Both,
    Left,
    Right,
}

/// <summary>The attention bell's state: no session needs attention, some do, or one is blocked.</summary>
public enum TitleBarAttention
{
    None,
    Attention,
    Blocked,
}

/// <summary>The overlay button's state: the active session has no overlay, or has one that is shown
/// or hidden. Upstream overlayToggleButton's accessibility value: none, shown, hidden.</summary>
public enum TitleBarOverlay
{
    None,
    Shown,
    Hidden,
}

/// <summary>One row of the recent-sessions menu: the session's name and "workspace · detail".</summary>
public sealed record TitleBarRecentSession(Guid Id, string Title, string Subtitle)
{
    public AgentIndicator Indicator { get; init; }
}

/// <summary>
/// What the title bar's trailing buttons draw, read from the window's store under the host gate:
/// upstream WindowContentView's recent-sessions, attention, scratch, split and overlay buttons, and the
/// workspace name in the title. The dashboard
/// and quick-terminal buttons need only whether a session is active.
/// </summary>
public sealed record TitleBarState(
    bool HasActiveSession,
    bool ScratchActive,
    TitleBarSplit Split,
    TitleBarAttention Attention,
    IReadOnlyList<TitleBarRecentSession> RecentSessions,
    IReadOnlyList<TitleBarRecentSession> SwitcherSessions)
{
    public string SubtitleDetail { get; init; } = string.Empty;

    /// <summary>The split's axis: Left/Right read as top/bottom when it is TopBottom.</summary>
    public SplitAxis SplitAxis { get; init; } = SplitAxis.LeftRight;

    /// <summary>The name of the active session's workspace, for the title (null without a session).</summary>
    public string? WorkspaceName { get; init; }

    public TitleBarOverlay Overlay { get; init; }

    /// <summary>The active session's context, shown in the title bar (null when unset).</summary>
    public string? Context { get; init; }

    public IReadOnlyDictionary<Guid, AgentIndicator> SessionIndicators { get; init; } =
        FrozenDictionary<Guid, AgentIndicator>.Empty;

    /// <summary>Upstream SessionSwitcher.maxCandidates: the recent list is capped like Ctrl-Tab's.</summary>
    public const int MaxRecentSessions = 10;

    public static TitleBarState Empty { get; } =
        new(false, false, TitleBarSplit.None, TitleBarAttention.None, [], []);

    public static TitleBarState Project(AppStore store)
    {
        var active = store.ActiveSession;
        return new(
            active is not null,
            active?.ScratchActive == true,
            SplitOf(active),
            AttentionOf(store.AttentionSessions),
            RecentOf(store, active),
            SwitcherOf(store))
        {
            SubtitleDetail = active?.SubtitleDetail ?? string.Empty,
            SplitAxis = active?.SplitAxis ?? SplitAxis.LeftRight,
            Context = active?.Context,
            WorkspaceName = active is null ? null : store.WorkspaceForSession(active.Id)?.Name,
            Overlay = OverlayOf(active),
            SessionIndicators = store.Workspaces.SelectMany(w => w.Sessions)
                .ToFrozenDictionary(s => s.Id, s => s.AgentIndicator),
        };
    }

    private static TitleBarSplit SplitOf(Session? session) => session switch
    {
        null or { HasSplit: false } => TitleBarSplit.None,
        { IsSplit: true } => TitleBarSplit.Both,
        { SplitFocused: true } => TitleBarSplit.Right,
        _ => TitleBarSplit.Left,
    };

    private static TitleBarOverlay OverlayOf(Session? session) => session switch
    {
        { OverlayActive: true, OverlayHidden: true } => TitleBarOverlay.Hidden,
        { OverlayActive: true } => TitleBarOverlay.Shown,
        _ => TitleBarOverlay.None,
    };

    private static TitleBarAttention AttentionOf(IReadOnlyList<Session> sessions) =>
        sessions.Count == 0 ? TitleBarAttention.None
        : sessions.Any(s => s.AgentIndicator.Status == AgentStatus.Blocked) ? TitleBarAttention.Blocked
        : TitleBarAttention.Attention;

    // upstream recentSessions: the window's MRU sessions inside the visible/filtered set, without the
    // current one (it is not a jump target)
    private static IReadOnlyList<TitleBarRecentSession> RecentOf(AppStore store, Session? active)
    {
        var valid = store.NavigableSessions.Select(s => s.Id).ToHashSet();
        if (active is not null) valid.Remove(active.Id);
        return Rows(store, valid);
    }

    // upstream SessionSwitcher.begin: the same MRU order with the current session kept, so it comes
    // first and the switch starts on the previous one
    private static IReadOnlyList<TitleBarRecentSession> SwitcherOf(AppStore store) =>
        Rows(store, store.NavigableSessions.Select(s => s.Id).ToHashSet());

    private static IReadOnlyList<TitleBarRecentSession> Rows(AppStore store, HashSet<Guid> valid) =>
        store.SessionRecency.Top(MaxRecentSessions, valid)
            .Select(id => store.SessionWithId(id) is { } session
                ? new TitleBarRecentSession(
                    id,
                    session.DisplayName,
                    $"{store.WorkspaceForSession(id)?.Name ?? ""} · {session.SubtitleDetail}")
                    { Indicator = session.AgentIndicator }
                : null)
            .OfType<TitleBarRecentSession>()
            .ToList();
}
