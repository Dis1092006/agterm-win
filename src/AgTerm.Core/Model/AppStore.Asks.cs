using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

public sealed partial class AppStore
{
    /// <summary>The app-wide question registry; a host sets the instance it shares with every
    /// window, so ask.result finds a terminal question in any window.</summary>
    public AskRegistry Asks { get; set; } = new();

    /// <summary>Raised under the host's model lock after a terminal question opens or ends, so the window redraws it.</summary>
    public event Action? AsksChanged;

    /// <summary>One terminal question per session. Registers the id and fills the slot in one step
    /// under the host's model lock.</summary>
    public bool OpenAsk(Guid sessionId, PendingAsk ask, Guid? paneIdentity, Guid windowId)
    {
        ArgumentNullException.ThrowIfNull(ask);
        if (SessionWithId(sessionId) is not { } session || session.AskPending is not null) return false;
        if (!Asks.Register(ask.Id, new AskOwner.Session(sessionId, windowId))) return false;
        session.AskPending = ask;
        session.AskDraft = new AskDraft(ask.Form);
        session.AskPaneIdentity = paneIdentity;
        AsksChanged?.Invoke();
        return true;
    }

    /// <summary>Edits the pending question's draft; a stale id, from a dialog already resolved,
    /// changes nothing. No AsksChanged: the edit comes from the dialog that shows it.</summary>
    public bool UpdateAskDraft(Guid sessionId, string askId, Action<AskDraft> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (SessionWithId(sessionId) is not { } session || session.AskPending?.Id != askId || session.AskDraft is not { } draft)
            return false;
        change(draft);
        return true;
    }

    /// <summary>Answers the pending question with the button at index and the current draft.</summary>
    public bool AnswerAsk(Guid sessionId, string askId, int index) =>
        SessionWithId(sessionId) is { AskPending: { } ask } session && ask.Id == askId
        && ask.Answer(index, session.AskDraft) is { } outcome && Resolve(session, askId, outcome);

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
        session.AskDraft = null;
        session.AskPaneIdentity = null;
        AsksChanged?.Invoke();
        return true;
    }
}
