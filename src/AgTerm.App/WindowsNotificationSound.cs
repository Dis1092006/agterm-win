using System.Runtime.InteropServices;

namespace AgTerm.App;

public static class WindowsNotificationSound
{
    private const uint MbOk = 0x00000000;

    public static bool IsSupported(string? name) =>
        string.Equals(name, "default", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "beep", StringComparison.OrdinalIgnoreCase);

    public static bool TryPlay(string? name)
    {
        if (!IsSupported(name))
            return false;

        return MessageBeep(MbOk);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);
}