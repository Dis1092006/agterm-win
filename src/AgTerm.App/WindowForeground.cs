using System.Runtime.InteropServices;

namespace AgTerm.App;

/// <summary>Brings a window to the front. Window.Activate does not do that for a window that is
/// already open, so this asks Windows directly. Windows refuses the request while another
/// application is in front, and then the taskbar button flashes instead.</summary>
public static class WindowForeground
{
    private const uint FlashTray = 0x2;
    private const uint FlashUntilForeground = 0xC;

    public static void Raise(IntPtr hwnd) =>
        Raise(() => SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd, () => Flash(hwnd));

    public static void Raise(Func<bool> bringToFront, Action flash)
    {
        if (!bringToFront())
            flash();
    }

    private static void Flash(IntPtr hwnd)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = hwnd,
            Flags = FlashTray | FlashUntilForeground,
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
