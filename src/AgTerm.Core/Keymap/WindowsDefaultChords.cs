namespace AgTerm.Core.Keymap;

/// <summary>
/// The shortcuts agterm-win ships with, where they differ from the upstream macOS ones.
///
/// macOS puts the app's shortcuts on Command and leaves Ctrl to the shell. Windows has no Command
/// key, so <see cref="WindowsChordDecoder"/> folds keymap `cmd` onto the physical Ctrl key, and
/// thirteen app shortcuts land on bare Ctrl. That is not a comfort problem: Ctrl+D ends input,
/// Ctrl+Z suspends, Ctrl+W deletes a word and Ctrl+P walks history, so an app holding those breaks
/// the program running in the pane.
///
/// Every shortcut that lands on bare Ctrl moves to the same key under Alt. Shortcuts that already
/// carry Shift or Alt stay exactly where upstream put them: a shell never reads those, and a
/// keystroke that still matches macOS is one less thing to relearn. The upstream modifier is not a
/// criterion - a user cannot see it, and on a keyboard with no Command key it describes nothing.
///
/// <see cref="BuiltinActionExtensions.DefaultChord"/> stays as the parity surface declares it;
/// this is a platform layer above it, and a user `map` line still overrides both.
/// </summary>
public static class WindowsDefaultChords
{
    /// <summary>The Windows shortcut for an action, or null when it keeps the upstream one.</summary>
    public static Chord? For(BuiltinAction action) => action switch
    {
        BuiltinAction.NewSession => Alt("n"),
        BuiltinAction.OpenDirectory => Alt("o"),
        BuiltinAction.CloseSession => Alt("w"),
        BuiltinAction.UndoClose => Alt("z"),
        BuiltinAction.IncreaseFontSize => Alt("+"),
        BuiltinAction.DecreaseFontSize => Alt("-"),
        BuiltinAction.ResetFontSize => Alt("0"),
        BuiltinAction.ToggleSplit => Alt("d"),
        BuiltinAction.ToggleScratch => Alt("j"),
        BuiltinAction.ToggleSearch => Alt("f"),
        BuiltinAction.ToggleSidebar => Alt("s"),
        BuiltinAction.QuickTerminal => Alt("`"),
        BuiltinAction.SessionPalette => Alt("p"),

        // Upstream ⌘F (toggle_search) and ⌃⌘F (toggle_fullscreen) both fold onto Ctrl+F, and the
        // cmd candidate is tried first, so fullscreen has never been reachable on Windows. Alt+F
        // now belongs to search, and Alt+Enter is the Windows convention for fullscreen anyway.
        BuiltinAction.ToggleFullscreen => Alt("return"),

        _ => null,
    };

    /// <summary>The chord an action ships with on this platform, before any user override.</summary>
    public static Chord? ShippedFor(BuiltinAction action) => For(action) ?? action.DefaultChord();

    private static Chord Alt(string key) => new(Modifiers.Option, key);
}
