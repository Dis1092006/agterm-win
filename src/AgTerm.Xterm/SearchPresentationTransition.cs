namespace AgTerm.Xterm;

public abstract record SearchPresentationTransition
{
    private SearchPresentationTransition()
    {
    }

    public sealed record Show(
        string PaneId,
        string Needle,
        string DisplayText,
        int? Selected,
        bool FocusInput) : SearchPresentationTransition;

    public sealed record Hide(
        string? ClearOwnerPaneId,
        string? RestorePaneId) : SearchPresentationTransition;

    public static SearchPresentationTransition Resolve(
        LiveSelectionSnapshot previous,
        LiveSelectionSnapshot current,
        string? acceptedPaneId)
    {
        if (current.SearchActive && !string.IsNullOrWhiteSpace(current.SearchOwnerPaneId))
        {
            var focusInput = !previous.SearchActive ||
                previous.SessionId != current.SessionId ||
                previous.SearchOwnerPaneId != current.SearchOwnerPaneId;
            return new Show(
                current.SearchOwnerPaneId,
                current.SearchNeedle,
                current.SearchDisplayText,
                current.SearchSelected,
                focusInput);
        }

        var closedOwnerPaneId = previous.SessionId == current.SessionId && previous.SearchActive
            ? previous.SearchOwnerPaneId
            : null;
        return new Hide(
            closedOwnerPaneId,
            closedOwnerPaneId is null ? null : acceptedPaneId);
    }
}
