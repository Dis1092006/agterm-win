using AgTerm.Core.Model;

namespace AgTerm.App;

/// <summary>
/// Command-palette rows with no builtin action, ported from upstream PaletteCatalog.swift
/// (PaletteCommand). Upstream also has "Edit ghostty.conf"; the port has no ghostty.conf.
/// </summary>
public enum PaletteAppCommand
{
    EditKeymap,
    ReloadKeymap,
    ReloadConfig,
    ClearFlagged,
    ClearFocus,
    AddWorkspaceToFocus,
    ExpandWorkspaces,
    CollapseWorkspaces,
    CloseSplit,
    SwapPanes,
}

public static class PaletteAppCommands
{
    public static IReadOnlyList<PaletteAppCommand> All { get; } =
    [
        PaletteAppCommand.EditKeymap,
        PaletteAppCommand.ReloadKeymap,
        PaletteAppCommand.ReloadConfig,
        PaletteAppCommand.ClearFlagged,
        PaletteAppCommand.ClearFocus,
        PaletteAppCommand.AddWorkspaceToFocus,
        PaletteAppCommand.ExpandWorkspaces,
        PaletteAppCommand.CollapseWorkspaces,
        PaletteAppCommand.CloseSplit,
        PaletteAppCommand.SwapPanes,
    ];

    public static string Id(PaletteAppCommand command) => command switch
    {
        PaletteAppCommand.EditKeymap => "app:edit-keymap",
        PaletteAppCommand.ReloadKeymap => "app:reload-keymap",
        PaletteAppCommand.ReloadConfig => "app:reload-config",
        PaletteAppCommand.ClearFlagged => "app:clear-flagged",
        PaletteAppCommand.ClearFocus => "app:clear-focus",
        PaletteAppCommand.AddWorkspaceToFocus => "app:add-workspace-to-focus",
        PaletteAppCommand.ExpandWorkspaces => "app:expand-workspaces",
        PaletteAppCommand.CollapseWorkspaces => "app:collapse-workspaces",
        PaletteAppCommand.CloseSplit => "app:close-split",
        PaletteAppCommand.SwapPanes => "app:swap-panes",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    public static string Title(PaletteAppCommand command) => command switch
    {
        PaletteAppCommand.EditKeymap => "Edit Keymap",
        PaletteAppCommand.ReloadKeymap => "Reload Keymap",
        PaletteAppCommand.ReloadConfig => "Reload Config",
        PaletteAppCommand.ClearFlagged => "Clear Flagged",
        PaletteAppCommand.ClearFocus => "Clear Focus",
        PaletteAppCommand.AddWorkspaceToFocus => "Add Workspace to Focus",
        PaletteAppCommand.ExpandWorkspaces => "Expand Workspaces",
        PaletteAppCommand.CollapseWorkspaces => "Collapse Workspaces",
        PaletteAppCommand.CloseSplit => "Close Split",
        PaletteAppCommand.SwapPanes => "Swap Panes",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    public static bool IsVisible(PaletteAppCommand command, AppStore store) => command switch
    {
        PaletteAppCommand.ClearFlagged => store.FlaggedSessions.Count > 0,
        // the marked set, not the applied filter: it is what Clear Focus empties
        PaletteAppCommand.ClearFocus => store.FocusedWorkspaceIds.Count > 0,
        // hidden where it would change nothing; offered in either sidebar mode
        PaletteAppCommand.AddWorkspaceToFocus =>
            store.CurrentWorkspaceId is not null && !store.IsCurrentWorkspaceFocusMember,
        PaletteAppCommand.ExpandWorkspaces or PaletteAppCommand.CollapseWorkspaces =>
            store.SidebarMode == SidebarMode.Tree,
        // gated on hasSplit, so the hidden split a toggle leaves behind is reachable
        PaletteAppCommand.CloseSplit => store.ActiveSession?.HasSplit == true,
        // upstream gates it on hasSplit and only a picker cover (the port has none)
        PaletteAppCommand.SwapPanes => store.ActiveSession?.HasSplit == true,
        _ => true,
    };

    public static PaletteAppCommand? Parse(string id)
    {
        foreach (var command in All)
        {
            if (string.Equals(Id(command), id, StringComparison.Ordinal))
                return command;
        }
        return null;
    }
}
