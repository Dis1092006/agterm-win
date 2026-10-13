namespace AgTerm.Core.Model;

/// <summary>The control-character rules of HUD text. Upstream prints the text into a terminal,
/// where an escape would paint outside the panel. The port draws HTML, but the refusal is part of
/// the wire contract. Checking UTF-16 units is the scalar rule: every scalar below 0x20 or at 0x7F
/// is one unit, and no surrogate half falls in that range.</summary>
public static class HudText
{
    public static bool HasControlCharacters(string text) => CommandRestore.HasControlCharacter(text);

    /// <summary>Markdown needs LF for its blocks and TAB for its indentation; CR stays refused.</summary>
    public static bool HasMarkdownControlCharacters(string text) =>
        text.Any(c => (c < 0x20 && c != '\n' && c != '\t') || c == 0x7F);
}
