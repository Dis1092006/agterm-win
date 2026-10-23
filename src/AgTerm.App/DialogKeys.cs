namespace AgTerm.App;

public static class DialogKeys
{
    private const int VirtualKeyEnter = 0x0D;

    /// <summary>A dialog clicks its default button on Enter key down, auto-repeats included. A dialog
    /// opened by an Enter (a palette item) would then confirm itself while that Enter is still held.
    /// The first press of a new Enter is never a repeat, so ignoring repeats loses no confirmation.</summary>
    public static bool IgnoresKey(int virtualKey, bool wasKeyDown) =>
        virtualKey == VirtualKeyEnter && wasKeyDown;
}
