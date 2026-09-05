using System.Runtime.InteropServices;

namespace AgTerm.App;

/// <summary>Raw shell32 surface for pulling the icon already embedded in the running exe's own
/// resources (via ApplicationIcon), so the WinUI 3 window can be given a taskbar/title-bar icon
/// without depending on a loose .ico file surviving single-file publish.</summary>
internal static class ShellIconNative
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        IntPtr[] phiconLarge,
        IntPtr[]? phiconSmall,
        uint nIcons);
}
