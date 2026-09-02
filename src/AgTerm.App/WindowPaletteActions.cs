using AgTerm.Core.Keymap;
using AgTerm.Core.Model;

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
}