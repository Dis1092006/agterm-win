using AgTerm.Core.Install;
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
    WorkspaceTheme,
}

public sealed record WindowPaletteSnapshot(
    WindowPaletteMode Mode,
    string Placeholder,
    IReadOnlyList<PaletteItemViewModel> Items,
    bool PreserveInputOrderForEmptyQuery = false,
    string? InitialSelectionId = null)
{
    // Windows-only: a theme preview shows the real window colors, so the theme palettes open
    // without the scrim the other palettes use.
    public bool Dimmed => Mode is not (WindowPaletteMode.Themes or WindowPaletteMode.WorkspaceTheme);
}

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

    /// <summary>The installer items of the Help menu. They are app commands, not builtin actions, so
    /// keymaps and the control protocol do not see them.</summary>
    public static string? ParseInstallerCommand(string itemId) => itemId switch
    {
        InstallerCommands.InstallCommandLineToolId or InstallerCommands.InstallAgentSkillId
            or InstallerCommands.InstallAgentStatusHooksId => itemId,
        _ => null,
    };

    public static PaletteAppCommand? ParseAppCommand(string itemId) => PaletteAppCommands.Parse(itemId);

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

    /// <summary>The workspace row's Theme… palette. The current row is the one on screen, so a
    /// stored name the catalog lacks opens on the global row, which is what that workspace shows.</summary>
    public static WindowPaletteSnapshot WorkspaceThemes(
        ThemeCatalog catalog,
        string workspaceName,
        string? currentTheme)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var shown = currentTheme is not null && catalog.Resolve(currentTheme) is not null ? currentTheme : null;
        var items = new List<PaletteItemViewModel>
        {
            new(ThemeItemId(null), "Use global theme", badge: shown is null ? "Current" : null),
        };
        items.AddRange(catalog.Names.Select(name => new PaletteItemViewModel(
            ThemeItemId(name),
            name,
            badge: name == shown ? "Current" : null)));
        return new(
            WindowPaletteMode.WorkspaceTheme,
            $"Select a theme for {workspaceName}…",
            items,
            PreserveInputOrderForEmptyQuery: true,
            InitialSelectionId: ThemeItemId(shown));
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
            .Concat(PaletteAppCommands.All
                .Where(command => PaletteAppCommands.IsVisible(command, store))
                .Select(command => new PaletteItemViewModel(
                    PaletteAppCommands.Id(command),
                    PaletteAppCommands.Title(command))))
            .Concat(
            [
                new PaletteItemViewModel(InstallerCommands.InstallCommandLineToolId, InstallerCommands.InstallCommandLineTool),
                new PaletteItemViewModel(InstallerCommands.InstallAgentSkillId, InstallerCommands.InstallAgentSkill),
                new PaletteItemViewModel(InstallerCommands.InstallAgentStatusHooksId, InstallerCommands.InstallAgentStatusHooks),
            ])
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
        BuiltinAction.ToggleFlaggedView => store.SidebarMode == SidebarMode.Flagged
            ? "Show All Sessions"
            : "Show Flagged Sessions",
        BuiltinAction.ToggleFlag => store.ActiveSession?.Flagged == true
            ? "Unflag Session"
            : "Flag Session",
        _ => KeybindingsCatalog.TitleFor(action),
    };
}