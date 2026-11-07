using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.App;

public static class AskKeys
{
    private const int Tab = 0x09, Enter = 0x0D, Escape = 0x1B, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;
    private const int A = 0x41, Z = 0x5A;

    /// <summary>Upstream ignores every key with Command, Control or Option; here Ctrl, Alt and Win.
    /// A letter comes from the virtual key, which keeps the Latin key position on a non-Latin
    /// layout, so a hotkey works without switching the layout.</summary>
    public static AskIntent? Intent(int virtualKey, bool shift, bool control, bool alt, bool windows)
    {
        if (control || alt || windows) return null;
        return virtualKey switch
        {
            Tab => shift ? new AskIntent.Backward() : new AskIntent.Forward(),
            Right or Down => new AskIntent.Forward(),
            Left or Up => new AskIntent.Backward(),
            Enter => new AskIntent.Activate(),
            Escape => new AskIntent.Cancel(),
            >= A and <= Z => new AskIntent.Hotkey(((char)('a' + (virtualKey - A))).ToString()),
            _ => null,
        };
    }

    private const int Backspace = 0x08, End = 0x23, Home = 0x24, Insert = 0x2D, Delete = 0x2E;
    private const int C = 0x43, V = 0x56, X = 0x58, Y = 0x59;

    /// <summary>A key in the dialog's text field; null leaves it to the field. Tab and Shift+Tab move
    /// the highlight, Esc escapes. Enter answers a line field; a multiline field takes Enter as a
    /// newline and answers on Ctrl+Enter, upstream's Cmd-Return.</summary>
    public static AskIntent? FieldIntent(int virtualKey, bool shift, bool control, bool alt, bool windows, ControlAskInputMode mode)
    {
        if (alt || windows) return null;
        return virtualKey switch
        {
            Tab when !control => shift ? new AskIntent.Backward() : new AskIntent.Forward(),
            Escape when !control => new AskIntent.Cancel(),
            Enter when mode == ControlAskInputMode.Line && !control => new AskIntent.Activate(),
            Enter when mode == ControlAskInputMode.Multiline && control && !shift => new AskIntent.Activate(),
            _ => null,
        };
    }

    /// <summary>A Ctrl edit chord the field keeps (clipboard, undo, select all, word moves and
    /// deletes), even when the keymap binds it.</summary>
    public static bool IsFieldEditChord(int virtualKey, bool control, bool alt) =>
        control && !alt && virtualKey is C or V or X or Z or Y or A or Insert or Backspace or Delete
            or Left or Up or Right or Down or Home or End;
}
