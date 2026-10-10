namespace AgTerm.Core.Model;

/// <summary>Port of upstream TerminalText.sanitized. Names and directories expand unquoted into a
/// custom command's shell line through the {AGT_X} tokens, where a newline starts a new statement.</summary>
public static class TerminalText
{
    public static string Sanitized(string value)
    {
        if (!value.Any(IsControl)) return value;
        return new string(value.Where(c => !IsControl(c)).ToArray());
    }

    private static bool IsControl(char c) => c < 0x20 || c == 0x7F;
}
