using AgTerm.Core.Model;

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
}
