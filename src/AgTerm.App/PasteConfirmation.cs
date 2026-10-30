namespace AgTerm.App;

/// <summary>The text of the dialog a middle-click paste shows before it types in text that could
/// run a command (docs/PLATFORM-NOTES.md "Selection buffer and middle-click paste").</summary>
internal static class PasteConfirmation
{
    public const string Title = "Paste this text?";
    public const string PasteButton = "Paste";
    public const int PreviewLimit = 2000;

    public static string Message(bool bracketed) => bracketed
        ? "The text has the sequence that marks the end of a bracketed paste. The program can run what follows it as a command."
        : "The text has line breaks, and the terminal has bracketed paste off. The shell can run each line as a command.";

    // ESC shows as its control picture, so the end-of-paste sequence is visible in the preview
    public static string Preview(string text)
    {
        var shown = text.Length > PreviewLimit ? text[..PreviewLimit] + "\u2026" : text;
        return shown.Replace('\u001b', '\u241B');
    }
}
