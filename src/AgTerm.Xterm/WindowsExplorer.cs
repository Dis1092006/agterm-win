using System.Diagnostics;

namespace AgTerm.Xterm;

/// <summary>Starts Explorer by its full path in the Windows directory. CreateProcess looks for a bare
/// "explorer.exe" in the app directory and the current directory first, so a copy placed in either
/// one would run.</summary>
public static class WindowsExplorer
{
    public static ProcessStartInfo StartInfo(string arguments) =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), arguments)
        {
            UseShellExecute = false,
        };
}
