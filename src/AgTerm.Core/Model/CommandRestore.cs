namespace AgTerm.Core.Model;

/// <summary>Which saved commands a restore may type into a new shell (upstream CommandRestore).
/// A control character reaches the line editor before anything parses it: Tab completes, Esc
/// reverts, LF submits half a line.</summary>
public static class CommandRestore
{
    public static bool HasControlCharacter(string text) => text.Any(c => c < 0x20 || c == 0x7F);

    /// <summary>U+FFFD is refused too: it marks text decoded with loss, so the replay would not be
    /// the command that ran.</summary>
    public static bool IsReplayable(IReadOnlyList<string> argv) =>
        argv.Count > 0 && !argv.Any(arg => HasControlCharacter(arg) || arg.Contains('�'));
}
