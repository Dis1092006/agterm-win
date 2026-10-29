namespace AgTerm.Core.Keymap;

/// <summary>The keymap key name of a Windows virtual-key code, for chords the window itself sees
/// (focus outside the terminal page). Punctuation keys take the unshifted US character, the name
/// keymap.conf uses and the terminal page gives the same key, so a chord means one key in both places.
/// The OEM virtual keys follow the US positions on the US and Cyrillic layouts.</summary>
public static class WindowsVirtualKeyNames
{
    public static int? KeyCode(string key)
    {
        for (var code = 0; code <= 0xFF; code++)
            if (KeyName(code) == key) return code;
        return null;
    }

    public static string? KeyName(int virtualKey) => virtualKey switch
    {
        >= 0x41 and <= 0x5A => ((char)(virtualKey + ('a' - 'A'))).ToString(),
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        0x09 => "tab",
        0x20 => "space",
        0x0D => "return",
        0x08 => "delete",
        0x25 => "left",
        0x26 => "up",
        0x27 => "right",
        0x28 => "down",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        0x6B => "+",
        0x6D => "-",
        _ => null,
    };
}
