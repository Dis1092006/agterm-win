namespace AgTerm.Xterm;

/// <summary>session.type keystroke semantics (§6): every \r\n / \n / \r is ONE Enter, and Enter
/// on a terminal is CR. Deliberately not a paste — bracketed-paste markers would wrap those.</summary>
public static class KeystrokeText
{
    public static string ToPtyInput(string text) =>
        text.Replace("\r\n", "\r").Replace('\n', '\r');
}
