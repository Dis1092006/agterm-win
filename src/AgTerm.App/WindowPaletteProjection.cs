using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Xterm;

namespace AgTerm.App;

public enum WindowPaletteMode
{
    Sessions,
    Commands,
    CustomCommands,
    Attention,
    Themes,
}

public sealed record WindowPaletteSnapshot(
    WindowPaletteMode Mode,
    string Placeholder,
    IReadOnlyList<PaletteItemViewModel> Items,
    bool PreserveInputOrderForEmptyQuery = false,
    string? InitialSelectionId = null);

public static class WindowPaletteProjection
{
    private static readonly HashSet<BuiltinAction> PaletteLaunchers =
    [
        BuiltinAction.SessionPalette,
        BuiltinAction.CommandPalette,
        BuiltinAction.CustomCommandPalette,
    ];

    public static WindowPaletteSnapshot Create(
        WindowPaletteMode mode,
        AppStore store,
        Keymap keymap,
        Func<BuiltinAction, WindowCommandState> stateProvider,
        ThemeCatalog? themeCatalog = null,
        string? currentTheme = null,
        bool followsSystemAppearance = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(keymap);
        ArgumentNullException.ThrowIfNull(stateProvider);

        return mode switch
        {
            WindowPaletteMode.Sessions => new(
                mode,
                "Go to session…",
                SessionItems(store.NavigableSessions, store, includeStatus: true)),
            WindowPaletteMode.Commands => new(
                mode,
                "Run an action…",
                CommandItems(store, keymap, stateProvider)),
            WindowPaletteMode.CustomCommands => new(
                mode,
                "Run a custom command…",
                CustomCommandItems(keymap)),
            WindowPaletteMode.Attention => new(
                mode,
                "Go to a session that needs attention…",
                SessionItems(store.AttentionSessions, store, includeStatus: true),
                PreserveInputOrderForEmptyQuery: true),
            WindowPaletteMode.Themes => new(
                mode,
                "Select a theme…",
                ThemeItems(themeCatalog ?? ThemeCatalog.Default, currentTheme, followsSystemAppearance),
                InitialSelectionId: ThemeItemId(currentTheme)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    public static Guid? ParseSessionId(string itemId) =>
        Guid.TryParseExact(itemId, "D", out var id) ? id : null;

    public static BuiltinAction? ParseAction(string itemId) =>
        BuiltinActions.TryParse(itemId, out var action) ? action : null;

    public static Guid? ParseCustomCommandId(string itemId)
    {
        const string prefix = "custom-";
        return itemId.StartsWith(prefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(itemId[prefix.Length..], "D", out var id)
                ? id
                : null;
    }

    public static string ThemeItemId(string? themeName) =>
        themeName is null ? "theme:__default__" : $"theme:{themeName}";

    public static bool TryParseThemeName(string itemId, out string? name)
    {
        const string prefix = "theme:";
        const string defaultId = "theme:__default__";
        if (itemId == defaultId)
        {
            name = null;
            return true;
        }

        if (itemId.StartsWith(prefix, StringComparison.Ordinal) && itemId.Length > prefix.Length)
        {
            name = itemId[prefix.Length..];
            return true;
        }

        name = null;
        return false;
    }

    private static IReadOnlyList<PaletteItemViewModel> SessionItems(
        IReadOnlyList<Session> sessions,
        AppStore store,
        bool includeStatus = false) =>
        sessions.Select(session =>
        {
            var workspaceName = store.WorkspaceForSession(session.Id)?.Name ?? string.Empty;
            var indicator = session.AgentIndicator;
            return new PaletteItemViewModel(
                session.Id.ToString("D"),
                session.DisplayName,
                $"{workspaceName} · {session.SubtitleDetail}",
                status: includeStatus ? indicator.Status : null,
                statusColor: includeStatus ? indicator.Color : null,
                statusShape: includeStatus ? indicator.Shape : null);
        }).ToArray();

    private static IReadOnlyList<PaletteItemViewModel> CommandItems(
        AppStore store,
        Keymap keymap,
        Func<BuiltinAction, WindowCommandState> stateProvider) =>
        BuiltinActions.All
            .Where(action => !PaletteLaunchers.Contains(action))
            .Where(action => stateProvider(action) is { Implemented: true, CanExecute: true })
            .Select(action => new PaletteItemViewModel(
                action.WireName(),
                CommandTitle(action, store),
                shortcut: keymap.GlyphHint(action)))
            .ToArray();

    private static IReadOnlyList<PaletteItemViewModel> CustomCommandItems(Keymap keymap) =>
        keymap.Commands.Select(command => new PaletteItemViewModel(
            $"custom-{command.Id:D}",
            command.Name,
            shortcut: string.IsNullOrWhiteSpace(command.Shortcut) ? null : command.Shortcut))
        .ToArray();

    private static IReadOnlyList<PaletteItemViewModel> ThemeItems(
        ThemeCatalog catalog,
        string? currentTheme,
        bool followsSystemAppearance)
    {
        var items = new List<PaletteItemViewModel>();
        // a pair of light and dark themes needs two named themes (upstream AppActions+Palette.swift)
        if (!followsSystemAppearance)
        {
            items.Add(new(
                ThemeItemId(null),
                "Default terminal",
                badge: currentTheme is null ? "Current" : null));
        }

        items.AddRange(catalog.Names.Select(name => new PaletteItemViewModel(
            ThemeItemId(name),
            name,
            badge: name == currentTheme ? "Current" : null)));
        return items;
    }

    private static string CommandTitle(BuiltinAction action, AppStore store) => action switch
    {
        BuiltinAction.NewWindow => "New Window",
        BuiltinAction.RenameWindow => "Rename Window",
        BuiltinAction.DeleteWindow => "Delete Window",
        BuiltinAction.NewWorkspace => "New Workspace",
        BuiltinAction.RenameWorkspace => "Rename Workspace",
        BuiltinAction.DeleteWorkspace => "Delete Workspace",
        BuiltinAction.NewSession => "New Session",
        BuiltinAction.OpenDirectory => "Open Directory…",
        BuiltinAction.RenameSession => "Rename Session",
        BuiltinAction.DuplicateSession => "Duplicate Session",
        BuiltinAction.CloseSession => "Close Session",
        BuiltinAction.ReopenRecent => "Reopen Last Closed Item",
        BuiltinAction.UndoClose => "Reopen Closed Item",
        BuiltinAction.ClearStatus => "Clear Status",
        BuiltinAction.IncreaseFontSize => "Increase Font Size",
        BuiltinAction.DecreaseFontSize => "Decrease Font Size",
        BuiltinAction.ResetFontSize => "Actual Font Size",
        BuiltinAction.ToggleSplit => "Toggle Split",
        BuiltinAction.ToggleScratch => "Toggle Scratch",
        BuiltinAction.ToggleTerminalZoom => "Toggle Terminal Zoom",
        BuiltinAction.ToggleSearch => "Find…",
        BuiltinAction.ToggleSidebar => "Toggle Sidebar",
        BuiltinAction.SelectTheme => "Select Theme…",
        BuiltinAction.ToggleFullscreen => "Toggle Full Screen",
        BuiltinAction.ToggleFlaggedView => store.SidebarMode == SidebarMode.Flagged
            ? "Show All Sessions"
            : "Show Flagged Sessions",
        BuiltinAction.ToggleFlag => store.ActiveSession?.Flagged == true
            ? "Unflag Session"
            : "Flag Session",
        BuiltinAction.FocusWorkspace => "Focus Workspace",
        BuiltinAction.FocusLeftPane => "Focus Left Pane",
        BuiltinAction.FocusRightPane => "Focus Right Pane",
        BuiltinAction.PreviousSession => "Previous Session",
        BuiltinAction.NextSession => "Next Session",
        BuiltinAction.PreviousAttentionSession => "Previous Attention Session",
        BuiltinAction.NextAttentionSession => "Next Attention Session",
        BuiltinAction.FirstSession => "First Session",
        BuiltinAction.LastSession => "Last Session",
        BuiltinAction.QuickTerminal => "Quick Terminal",
        BuiltinAction.ShowAttention => "Show Attention",
        BuiltinAction.Dashboard => "Dashboard",
        BuiltinAction.SessionPalette => "Go to Session…",
        BuiltinAction.CommandPalette => "Run an Action…",
        BuiltinAction.CustomCommandPalette => "Run a Custom Command…",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}