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

    /// <summary>A bare Return or keypad Enter submits; Shift- or Option-Return is plain typing (a newline
    /// inside the prompt).</summary>
    public static bool IsSubmit(string? code, KeyModifiers modifiers) =>
        modifiers == KeyModifiers.None && code is "Enter" or "NumpadEnter";

    /// <summary>Interrupt first, then submit, else other.</summary>
    public static StatusKeystroke Classify(string? code, string? character, KeyModifiers modifiers) =>
        IsInterrupt(code, character, modifiers) ? StatusKeystroke.Interrupt
        : IsSubmit(code, modifiers) ? StatusKeystroke.Submit
        : StatusKeystroke.Other;

    /// <summary>Injected text counts as a submit when it holds a line break, else as other. It is never an
    /// interrupt.</summary>
    public static StatusKeystroke Classify(string text) =>
        text.AsSpan().IndexOfAny('\n', '\r') >= 0 ? StatusKeystroke.Submit : StatusKeystroke.Other;
}
