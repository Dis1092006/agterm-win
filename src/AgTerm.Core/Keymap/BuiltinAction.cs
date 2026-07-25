namespace AgTerm.Core.Keymap;

/// <summary>
/// A rebindable built-in action. Each member has a canonical kitty-style raw name (the token the
/// user writes after map in keymap.conf) and a default chord — the shortcut shipped out of the box.
/// Parity pin: agterm 7df70fe, 41 actions.
/// </summary>
public enum BuiltinAction
{
    NewWindow, RenameWindow, DeleteWindow,
    NewWorkspace, RenameWorkspace, DeleteWorkspace,
    NewSession, OpenDirectory, RenameSession, DuplicateSession,
    CloseSession, ReopenRecent, UndoClose, ClearStatus,
    IncreaseFontSize, DecreaseFontSize, ResetFontSize,
    ToggleSplit, ToggleScratch, ToggleTerminalZoom, ToggleSearch,
    ToggleSidebar, SelectTheme, ToggleFullscreen,
    ToggleFlaggedView, ToggleFlag, FocusWorkspace,
    FocusLeftPane, FocusRightPane,
    PreviousSession, NextSession,
    PreviousAttentionSession, NextAttentionSession,
    FirstSession, LastSession,
    QuickTerminal, SessionPalette, CommandPalette,
    CustomCommandPalette, ShowAttention,
    Dashboard,
}

public static class BuiltinActions
{
    private static readonly (BuiltinAction Action, string Wire)[] Map =
    {
        (BuiltinAction.NewWindow, "new_window"),
        (BuiltinAction.RenameWindow, "rename_window"),
        (BuiltinAction.DeleteWindow, "delete_window"),
        (BuiltinAction.NewWorkspace, "new_workspace"),
        (BuiltinAction.RenameWorkspace, "rename_workspace"),
        (BuiltinAction.DeleteWorkspace, "delete_workspace"),
        (BuiltinAction.NewSession, "new_session"),
        (BuiltinAction.OpenDirectory, "open_directory"),
        (BuiltinAction.RenameSession, "rename_session"),
        (BuiltinAction.DuplicateSession, "duplicate_session"),
        (BuiltinAction.CloseSession, "close_session"),
        (BuiltinAction.ReopenRecent, "reopen_recent"),
        (BuiltinAction.UndoClose, "undo_close"),
        (BuiltinAction.ClearStatus, "clear_status"),
        (BuiltinAction.IncreaseFontSize, "increase_font_size"),
        (BuiltinAction.DecreaseFontSize, "decrease_font_size"),
        (BuiltinAction.ResetFontSize, "reset_font_size"),
        (BuiltinAction.ToggleSplit, "toggle_split"),
        (BuiltinAction.ToggleScratch, "toggle_scratch"),
        (BuiltinAction.ToggleTerminalZoom, "toggle_terminal_zoom"),
        (BuiltinAction.ToggleSearch, "toggle_search"),
        (BuiltinAction.ToggleSidebar, "toggle_sidebar"),
        (BuiltinAction.SelectTheme, "select_theme"),
        (BuiltinAction.ToggleFullscreen, "toggle_fullscreen"),
        (BuiltinAction.ToggleFlaggedView, "toggle_flagged_view"),
        (BuiltinAction.ToggleFlag, "toggle_flag"),
        (BuiltinAction.FocusWorkspace, "focus_workspace"),
        (BuiltinAction.FocusLeftPane, "focus_left_pane"),
        (BuiltinAction.FocusRightPane, "focus_right_pane"),
        (BuiltinAction.PreviousSession, "previous_session"),
        (BuiltinAction.NextSession, "next_session"),
        (BuiltinAction.PreviousAttentionSession, "previous_attention_session"),
        (BuiltinAction.NextAttentionSession, "next_attention_session"),
        (BuiltinAction.FirstSession, "first_session"),
        (BuiltinAction.LastSession, "last_session"),
        (BuiltinAction.QuickTerminal, "quick_terminal"),
        (BuiltinAction.SessionPalette, "session_palette"),
        (BuiltinAction.CommandPalette, "command_palette"),
        (BuiltinAction.CustomCommandPalette, "custom_command_palette"),
        (BuiltinAction.ShowAttention, "show_attention"),
        (BuiltinAction.Dashboard, "dashboard"),
    };

    private static readonly Dictionary<BuiltinAction, string> ToWire = Map.ToDictionary(p => p.Action, p => p.Wire);
    private static readonly Dictionary<string, BuiltinAction> FromWire =
        Map.ToDictionary(p => p.Wire, p => p.Action, StringComparer.Ordinal);

    /// <summary>All actions in declaration order.</summary>
    public static IReadOnlyList<BuiltinAction> All { get; } = Map.Select(p => p.Action).ToArray();

    /// <summary>The canonical raw name written after map in keymap.conf.</summary>
    public static string WireName(this BuiltinAction action) => ToWire[action];

    public static bool TryParse(string wire, out BuiltinAction action) => FromWire.TryGetValue(wire, out action);

    /// <summary>The shortcut the action ships with, or null for the keyless actions. Every shipped
    /// default is distinct (the conflict resolution relies on it).</summary>
    public static Chord? DefaultChord(this BuiltinAction action) => action switch
    {
        BuiltinAction.NewWindow => new Chord(Modifiers.Command | Modifiers.Option, "n"),
        BuiltinAction.NewWorkspace => new Chord(Modifiers.Command | Modifiers.Shift, "n"),
        BuiltinAction.NewSession => new Chord(Modifiers.Command, "n"),
        BuiltinAction.OpenDirectory => new Chord(Modifiers.Command, "o"),
        BuiltinAction.CloseSession => new Chord(Modifiers.Command, "w"),
        BuiltinAction.ReopenRecent => new Chord(Modifiers.Command | Modifiers.Shift, "t"),
        BuiltinAction.UndoClose => new Chord(Modifiers.Command, "z"),
        BuiltinAction.IncreaseFontSize => new Chord(Modifiers.Command, "+"),
        BuiltinAction.DecreaseFontSize => new Chord(Modifiers.Command, "-"),
        BuiltinAction.ResetFontSize => new Chord(Modifiers.Command, "0"),
        BuiltinAction.ToggleSplit => new Chord(Modifiers.Command, "d"),
        BuiltinAction.ToggleScratch => new Chord(Modifiers.Command, "j"),
        BuiltinAction.ToggleTerminalZoom => new Chord(Modifiers.Command | Modifiers.Shift, "return"),
        BuiltinAction.ToggleSearch => new Chord(Modifiers.Command, "f"),
        BuiltinAction.ToggleSidebar => new Chord(Modifiers.Command | Modifiers.Control, "s"),
        BuiltinAction.ToggleFullscreen => new Chord(Modifiers.Command | Modifiers.Control, "f"),
        BuiltinAction.ToggleFlag => new Chord(Modifiers.Command | Modifiers.Shift, "f"),
        BuiltinAction.QuickTerminal => new Chord(Modifiers.Control, "`"),
        BuiltinAction.SessionPalette => new Chord(Modifiers.Control, "p"),
        BuiltinAction.CommandPalette => new Chord(Modifiers.Control | Modifiers.Shift, "p"),
        BuiltinAction.CustomCommandPalette => new Chord(Modifiers.Control | Modifiers.Shift, "o"),
        BuiltinAction.ShowAttention => new Chord(Modifiers.Control | Modifiers.Shift, "i"),
        BuiltinAction.Dashboard => new Chord(Modifiers.Command | Modifiers.Shift, "d"),
        BuiltinAction.FocusLeftPane => new Chord(Modifiers.Command | Modifiers.Option, "left"),
        BuiltinAction.FocusRightPane => new Chord(Modifiers.Command | Modifiers.Option, "right"),
        BuiltinAction.PreviousSession => new Chord(Modifiers.Command | Modifiers.Option, "up"),
        BuiltinAction.NextSession => new Chord(Modifiers.Command | Modifiers.Option, "down"),
        BuiltinAction.PreviousAttentionSession => new Chord(Modifiers.Control | Modifiers.Option, "up"),
        BuiltinAction.NextAttentionSession => new Chord(Modifiers.Control | Modifiers.Option, "down"),
        _ => null,
    };
}
