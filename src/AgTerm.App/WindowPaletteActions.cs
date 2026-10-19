using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.App;

public sealed record WindowPaletteAcceptanceResult(bool RestoreTerminalFocus);

public static class WindowPaletteAcceptance
{
    public static async Task<WindowPaletteAcceptanceResult> AcceptAsync(
        WindowPaletteMode mode,
        PaletteItemViewModel item,
        Func<Guid, CancellationToken, Task<bool>> selectSessionAsync,
        Func<BuiltinAction, CancellationToken, Task<bool>> executeActionAsync,
        Func<Guid, CancellationToken, Task> executeCustomCommandAsync,
        Func<string?, CancellationToken, Task> setThemeAsync,
        CancellationToken cancellationToken = default,
        Func<string, CancellationToken, Task>? executeInstallerCommandAsync = null)
    {
        switch (mode)
        {
            case WindowPaletteMode.Sessions:
            case WindowPaletteMode.Attention:
                if (WindowPaletteProjection.ParseSessionId(item.Id) is { } sessionId)
                {
                    var selectionChanged = await selectSessionAsync(sessionId, cancellationToken);
                    return new(!selectionChanged);
                }
                break;
            case WindowPaletteMode.Commands:
                if (WindowPaletteProjection.ParseAction(item.Id) is { } action)
                {
                    var executed = await executeActionAsync(action, cancellationToken);
                    return new(!executed || WindowPaletteActions.ShouldRestoreTerminalFocus(action));
                }
                if (WindowPaletteProjection.ParseInstallerCommand(item.Id) is { } installerId &&
                    executeInstallerCommandAsync is not null)
                {
                    await executeInstallerCommandAsync(installerId, cancellationToken);
                }
                break;
            case WindowPaletteMode.CustomCommands:
                if (WindowPaletteProjection.ParseCustomCommandId(item.Id) is { } commandId)
                    await executeCustomCommandAsync(commandId, cancellationToken);
                break;
            case WindowPaletteMode.Themes:
                if (WindowPaletteProjection.TryParseThemeName(item.Id, out var themeName))
                    await setThemeAsync(themeName, cancellationToken);
                break;
        }

        return new(true);
    }
}

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
        BuiltinAction.RenameWorkspace or
        BuiltinAction.ToggleSearch or
        BuiltinAction.ShowAttention or
        BuiltinAction.SelectTheme or
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