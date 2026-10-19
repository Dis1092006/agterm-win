using Microsoft.Win32;

namespace AgTerm.Xterm.Tests;

/// <summary>A scratch key under <c>HKCU\Software\agterm-win-tests</c>, so tests never write the real
/// <c>HKCU\Environment</c>. Dispose removes the key, and the parent once it is empty.</summary>
internal sealed class ScratchRegistryKey : IDisposable
{
    private const string Parent = @"Software\agterm-win-tests";

    public string Path { get; } = Parent + @"\" + Guid.NewGuid().ToString("N");

    public RegistryKey Open() => Registry.CurrentUser.CreateSubKey(Path, writable: true);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(Path, throwOnMissingSubKey: false);
        try
        {
            // fails while a test in another class still has its own key under the parent
            Registry.CurrentUser.DeleteSubKey(Parent, throwOnMissingSubKey: false);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
