using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.App;

public static class WindowPaletteActions
{
    public static bool SelectSession(AppStore store, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store.SessionWithId(sessionId) is null)
            return false;

        var changed = store.SelectedSessionId != sessionId;
        store.NoteUserActivity();
        store.SelectSession(sessionId);
        return changed;
    }

    public static bool ShouldRestoreTerminalFocus(BuiltinAction action) => action is not (
        BuiltinAction.RenameSession or
        BuiltinAction.ToggleSearch or
        BuiltinAction.ShowAttention or
        BuiltinAction.FocusLeftPane or
        BuiltinAction.FocusRightPane);

    public static string? CustomCommandPaneId(
        LiveSelectionSnapshot selection,
        CustomCommandSource source,
        string? firedPaneId)
    {
        if (source == CustomCommandSource.Hotkey &&
            firedPaneId is not null &&
            (firedPaneId == selection.MainPaneId ||
             firedPaneId == selection.SplitPaneId ||
             firedPaneId == selection.ScratchPaneId))
        {
            return firedPaneId;
        }

        return selection.SplitFocused && !string.IsNullOrWhiteSpace(selection.SplitPaneId)
            ? selection.SplitPaneId
            : selection.MainPaneId;
    }
}