namespace AgTerm.Core.Model;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Command = 2,
    Option = 4,
    Shift = 8,
}

/// <summary>Escape or bare Ctrl+C. Browser KeyboardEvent.code identifies the physical C key
/// even on non-Latin layouts; the produced key identifies C on remapped Latin layouts.</summary>
public static class InterruptKeystroke
{
    public static bool IsInterrupt(string? code, string? character, KeyModifiers modifiers)
    {
        if (code == "Escape" || character == "Escape") return true;
        if (modifiers != KeyModifiers.Control) return false;
        var letter = character?.ToLowerInvariant();
        if (letter == "c") return true;
        return code == "KeyC" && (letter is null or { Length: not 1 } || letter[0] is < 'a' or > 'z');
    }
}
