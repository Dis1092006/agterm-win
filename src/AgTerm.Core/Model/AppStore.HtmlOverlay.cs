using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

// A page in the overlay slot (upstream AppStore+HtmlOverlay.swift, 11fe64eb). The port has only
// the session-wide slot, so a pane never holds a page.
public sealed partial class AppStore
{
    /// <summary>Told once per page that it left its slot for good, so the app's web view goes with it
    /// (upstream HtmlOverlayReleases.onRelease).</summary>
    public Action<Guid>? HtmlOverlayReleased { get; set; }

    /// <summary>Opens a page in the session-wide slot with the slot rules of OpenOverlay: a HUD yields,
    /// an occupied slot refuses. NOT persisted.</summary>
    public HtmlOverlayOpenFailure? OpenHtmlOverlay(Guid sessionId, HtmlOverlay overlay, int? sizePercent,
                                                   OverlayPointSize points = default, string? backgroundColor = null)
    {
        if (SessionWithId(sessionId) is not { } session) return HtmlOverlayOpenFailure.UnknownSession;
        if (session.HudActive) CloseHudOf(session);
        if (session.OverlayActive) return HtmlOverlayOpenFailure.AlreadyOpen;
        session.OverlayExitCode = null;
        session.OverlaySizePercent = sizePercent is { } percent ? Math.Min(100, Math.Max(1, percent)) : null;
        session.OverlayPoints = sizePercent is null ? default : points;
        session.OverlayBackgroundColor = backgroundColor;
        session.HtmlOverlay = overlay;
        session.OverlayActive = true;
        NotifySidebar();
        ScheduleTreeChanged();
        return null;
    }

    /// <summary>Why the slot pane addresses cannot take a page command; null when it holds a page.</summary>
    public HtmlOverlayCommandFailure? HtmlOverlayCommandFailure(Guid sessionId, OverlayPane? pane) =>
        PageIn(sessionId, pane).Failure;

    /// <summary>Asks the app to reload the page, keeping the slot, its identity and the web view.</summary>
    public HtmlOverlayCommandFailure? ReloadHtmlOverlay(Guid sessionId, OverlayPane? pane,
                                                        HtmlReloadTarget target = HtmlReloadTarget.Original)
    {
        var (page, failure) = PageIn(sessionId, pane);
        if (page is null) return failure;
        page.ReloadRevision += 1;
        page.ReloadTarget = target;
        page.LoadState = HtmlLoadState.Loading;
        page.LoadError = null;
        if (target == HtmlReloadTarget.Original) page.Current = null;
        return null;
    }

    /// <summary>Records what the web view reported for page id wherever its slot now is, a session
    /// hidden by an undoable close included. No-op once the page has left its slot.</summary>
    public void SetHtmlLoadState(Guid id, HtmlLoadState state, string? error)
    {
        if (PageWithId(id) is not { } page) return;
        page.LoadState = state;
        page.LoadError = error;
    }

    /// <summary>Records what the web view now shows, as SetHtmlLoadState does.</summary>
    public void SetHtmlPage(Guid id, HtmlPageInfo? info)
    {
        if (PageWithId(id) is { } page) page.Current = info;
    }

    /// <summary>The visible session whose slot holds page id; null once the page left it.</summary>
    public Session? HtmlOverlaySlot(Guid id) =>
        Workspaces.SelectMany(w => w.Sessions).FirstOrDefault(s => s.HtmlOverlayActive && s.HtmlOverlay!.Id == id);

    /// <summary>Closes page id wherever its slot now is.</summary>
    public bool CloseHtmlOverlay(Guid id) => HtmlOverlaySlot(id) is { } session && CloseOverlay(session.Id);

    /// <summary>The tree's htmlOverlays for session: its page as the web view last reported it.</summary>
    private static ControlHtmlOverlayNode[]? HtmlOverlayNodes(Session session)
    {
        if (!session.HtmlOverlayActive || session.HtmlOverlay is not { } page) return null;
        string? file = null, cwd = null;
        switch (page.Source)
        {
            case HtmlSource.File source:
                (file, cwd) = (source.Path, source.GrantRoot);
                break;
        }
        return
        [
            new ControlHtmlOverlayNode
            {
                File = file,
                Cwd = cwd,
                State = page.LoadState.ToString().ToLowerInvariant(),
                Error = page.LoadError,
                Page = page.Current?.Page,
                Title = page.Current?.Title,
                CanGoBack = page.Current?.CanGoBack,
                CanGoForward = page.Current?.CanGoForward,
                Navigation = page.Navigation ? true : null,
                Javascript = page.Javascript,
                Chromeless = page.Chromeless,
                Persistent = page.Persistent,
                Browse = page.Browse,
                Id = page.Id.ToString("D").ToUpperInvariant(),
                Hidden = session.OverlayHidden ? true : null,
            },
        ];
    }

    /// <summary>Empties the slot of its page, telling HtmlOverlayReleased once.</summary>
    private void ReleaseHtmlOverlayOf(Session session)
    {
        if (session.HtmlOverlay is not { } page) return;
        session.HtmlOverlay = null;
        HtmlOverlayReleased?.Invoke(page.Id);
    }

    private HtmlOverlay? PageWithId(Guid id) =>
        Workspaces.SelectMany(w => w.Sessions)
                  .Concat(PendingCloseMemberSessions())
                  .Select(s => s.HtmlOverlay)
                  .FirstOrDefault(page => page?.Id == id);

    private (HtmlOverlay? Page, HtmlOverlayCommandFailure? Failure) PageIn(Guid sessionId, OverlayPane? pane)
    {
        if (SessionWithId(sessionId) is not { } session) return (null, Model.HtmlOverlayCommandFailure.UnknownSession);
        if (pane is not null || !session.OverlayActive || session.HudActive)
            return (null, Model.HtmlOverlayCommandFailure.NoOverlay);
        return session.HtmlOverlay is { } page ? (page, null) : (null, Model.HtmlOverlayCommandFailure.NotHtml);
    }
}
