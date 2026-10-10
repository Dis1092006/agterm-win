namespace AgTerm.Xterm;

/// <summary>session.type keystroke semantics (§6): every \r\n / \n / \r is ONE Enter, and Enter
/// on a terminal is CR. Deliberately not a paste — bracketed-paste markers would wrap those.</summary>
public static class KeystrokeText
{
    public static string ToPtyInput(string text) =>
        text.Replace("\r\n", "\r").Replace('\n', '\r');

    /// <summary>The gap before a final Return. Without it a TUI such as Claude Code can read a long
    /// line and its Return as one paste and leave the line unsubmitted (upstream #681).</summary>
    public static readonly TimeSpan SubmitGap = TimeSpan.FromMilliseconds(10);

    /// <summary>Splits off a final Return that follows text. Returns inside the text stay in Head.</summary>
    public static (string Head, bool PacedReturn) Paced(string text)
    {
        var pty = ToPtyInput(text);
        var hasText = pty.Any(c => c != '\r');
        return pty.EndsWith('\r') && hasText ? (pty[..^1], true) : (pty, false);
    }

    public static void Write(string text, Action<string> write, Action<TimeSpan> sleep)
    {
        var (head, pacedReturn) = Paced(text);
        if (head.Length > 0)
            write(head);
        if (!pacedReturn)
            return;
        sleep(SubmitGap);
        write("\r");
    }
}
