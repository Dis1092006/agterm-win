namespace AgTerm.Core.Keymap;

/// <summary>
/// Upstream's helpHint ("Dashboard (⌘⇧D)") for Windows: the physical chord that actually runs an
/// action on this platform, written the way Windows writes shortcuts ("Ctrl+Shift+D").
/// </summary>
public static class WindowsShortcutText
{
    public static string Format(PhysicalChord chord)
    {
        var parts = new List<string>(4);
        if (chord.Ctrl) parts.Add("Ctrl");
        if (chord.Alt) parts.Add("Alt");
        if (chord.Shift) parts.Add("Shift");
        parts.Add(chord.Key switch
        {
            "return" => "Enter",
            "delete" => "Backspace", // the macOS delete key is the one Windows calls Backspace
            "+" => "Plus",
            "-" => "Minus",
            { Length: 1 } key => key.ToUpperInvariant(),
            var key => char.ToUpperInvariant(key[0]) + key[1..],
        });
        return string.Join("+", parts);
    }

    /// <summary>The chord a press must make to run the action, or null when the action is keyless or
    /// its chord resolves to another action (the cmd and ctrl fold onto one Ctrl key).</summary>
    public static string? For(Keymap keymap, BuiltinAction action)
    {
        if (keymap.EquivalentFor(action) is not { } chord)
            return null;
        var physical = WindowsChordDecoder.Effective(chord);
        return new HotkeyRouter(keymap).Handle(physical) == action ? Format(physical) : null;
    }

    public static string Hint(string text, Keymap keymap, BuiltinAction action) =>
        For(keymap, action) is { } shortcut ? $"{text} ({shortcut})" : text;
}
