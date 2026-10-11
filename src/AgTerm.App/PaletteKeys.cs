namespace AgTerm.App;

public static class PaletteKeys
{
    private const int VirtualKeyJ = 0x4A;
    private const int VirtualKeyK = 0x4B;

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
