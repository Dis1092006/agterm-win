using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AgTerm.App;

/// <summary>The app icon that suits the taskbar color, for a window. A WinUI 3 window does not take the
/// exe's ApplicationIcon on its own. The icons are embedded in the assembly rather than read from loose
/// .ico files, because single-file publish does not promise to keep loose files beside the exe.</summary>
internal static class WindowAppIcon
{
    private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int SmCxIcon = 11;

    // the window keeps the handle instead of copying the image, and every window shares these, so
    // they live as long as the process
    private static readonly ConcurrentDictionary<(string Resource, int Size), IntPtr> Handles = new();

    /// <summary>One icon at the large icon size for the window's DPI, or zero. One icon fills both of the
    /// window's slots: an icon at the small size (24 px at 150%) came out blurred on the taskbar and in
    /// Alt+Tab, which draw it at 36 px.</summary>
    public static IntPtr For(IntPtr hwnd)
    {
        var resource = TaskbarAppIcon.ResourceFor(Registry.GetValue(PersonalizeKey, "SystemUsesLightTheme", null));
        var size = NativeMethods.GetSystemMetricsForDpi(SmCxIcon, NativeMethods.GetDpiForWindow(hwnd));
        var icon = Handles.GetOrAdd((resource, size), key => Create(key.Resource, key.Size));
        if (icon == IntPtr.Zero)
            System.Diagnostics.Trace.TraceWarning($"No {size} px icon from {resource}.");
        return icon;
    }

    private static IntPtr Create(string resource, int size)
    {
        using var stream = typeof(WindowAppIcon).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
            return IntPtr.Zero;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var ico = buffer.ToArray();
        if (IconDirectory.FrameFor(ico, size) is not { } frame)
            return IntPtr.Zero;

        const uint iconFormatVersion = 0x00030000;
        return NativeMethods.CreateIconFromResourceEx(
            ico.AsSpan(frame.Offset, frame.Length).ToArray(), (uint)frame.Length, true, iconFormatVersion, size, size, 0);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr CreateIconFromResourceEx(
            byte[] presbits, uint dwResSize, bool fIcon, uint dwVer, int cxDesired, int cyDesired, uint flags);

        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);
    }
}
