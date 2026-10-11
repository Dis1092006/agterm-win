using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

public sealed partial class AppStore
{
    /// <summary>The app-wide question registry; a host sets the instance it shares with every
    /// window, so ask.result finds a terminal question in any window.</summary>
    public AskRegistry Asks { get; set; } = new();

    /// <summary>One terminal question per session. Registers the id and fills the slot in one step
    /// under the host's model lock.</summary>
    public bool OpenAsk(Guid sessionId, PendingAsk ask, Guid? paneIdentity, Guid windowId)
    {
        ArgumentNullException.ThrowIfNull(ask);
        if (SessionWithId(sessionId) is not { } session || session.AskPending is not null) return false;
        if (!Asks.Register(ask.Id, new AskOwner.Session(sessionId, windowId))) return false;
        session.AskPending = ask;
        session.AskPaneIdentity = paneIdentity;
        return true;
    }

    public bool ResolveAsk(Guid sessionId, string askId, ControlAskResult outcome) =>
        SessionWithId(sessionId) is { } session && Resolve(session, askId, outcome);

    public bool CancelAsk(Guid sessionId, string askId) =>
        ResolveAsk(sessionId, askId, ControlAskResult.Cancelled());

    /// <summary>Window close and application exit: no session keeps a question its window can no
    /// longer show.</summary>
    public void CancelAllAsks()
    {
        foreach (var session in Workspaces.SelectMany(workspace => workspace.Sessions).ToArray())
            CancelPendingAsk(session);
    }

    internal void CancelPendingAsk(Session session)
    {
        if (session.AskPending is { } ask) Resolve(session, ask.Id, ControlAskResult.Cancelled());
    }

    private bool Resolve(Session session, string askId, ControlAskResult outcome)
    {
        if (session.AskPending?.Id != askId || outcome.Result == ControlAskOutcome.Pending) return false;
        Asks.Retain(askId, outcome);
        session.AskPending = null;
        session.AskPaneIdentity = null;
        return true;
    }
}
