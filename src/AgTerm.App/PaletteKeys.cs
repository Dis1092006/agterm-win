using AgTerm.Core.Keymap;

namespace AgTerm.App;

public static class PaletteKeys
{
    private static readonly HashSet<string> TextEditingCtrlKeys =
        ["a", "c", "v", "x", "z", "y", "delete", "left", "right", "up", "down"];

    /// <summary>Upstream runs menu shortcuts over an open palette. A chord needs Ctrl or Alt to leave the
    /// query box, so typed text never runs a command, and the box keeps the Ctrl keys it edits text
    /// with even when the keymap binds them.</summary>
    public static bool PassesToWindow(PhysicalChord chord)
    {
        if (!chord.Ctrl && !chord.Alt) return false;
        return chord.Alt || !TextEditingCtrlKeys.Contains(chord.Key);
    }

    /// <summary>Enter and the arrows run the palette's list, except in a window chord such as
    /// Alt+Enter, which goes on to the window as any other chord from the query box does.</summary>
    public static bool LeavesToWindow(int virtualKey, bool control, bool alt, bool shift)
    {
        var name = virtualKey switch
        {
            VirtualKeyEnter => "return",
            VirtualKeyUp => "up",
            VirtualKeyDown => "down",
            _ => null,
        };
        return name is not null && PassesToWindow(new PhysicalChord(control, alt, shift, name));
    }

    /// <summary>A palette launcher toggles or replaces the open palette itself.</summary>
    public static bool ClosesPaletteFirst(BuiltinAction action) => action is not (
        BuiltinAction.SessionPalette or
        BuiltinAction.CommandPalette or
        BuiltinAction.CustomCommandPalette or
        BuiltinAction.ShowAttention or
        BuiltinAction.SelectTheme);

    private const int VirtualKeyJ = 0x4A;
    private const int VirtualKeyK = 0x4B;
    private const int VirtualKeyEnter = 0x0D;
    private const int VirtualKeyUp = 0x26;
    private const int VirtualKeyDown = 0x28;

    /// <summary>Ctrl+J moves down and Ctrl+K up, with Control as the only modifier, so Ctrl+Shift
    /// and Ctrl+Alt chords stay free for other commands (upstream #695).</summary>
    public static int SelectionStep(int virtualKey, bool control, bool shift, bool alt)
    {
        if (!control || shift || alt) return 0;
        return virtualKey switch
        {
            VirtualKeyJ => 1,
            VirtualKeyK => -1,
            _ => 0,
        };
    }
}
