namespace AgTerm.Xterm.Tests;

// MainWindow is a WinUI type the test project does not compile, so the pointer wiring is pinned in
// its source.
public sealed class WindowPointerFocusSourceTests
{
    [Fact]
    public void Root_grid_handles_a_pointer_release_no_element_took_so_the_window_root_keeps_off_the_focus()
    {
        var xaml = Source("MainWindow.xaml");
        var code = Source("MainWindow.xaml.cs");

        var rootGrid = xaml[xaml.IndexOf("<Grid x:Name=\"RootGrid\"", StringComparison.Ordinal)..];
        rootGrid = rootGrid[..rootGrid.IndexOf('>')];
        Assert.Contains("PointerReleased=\"OnRootPointerReleased\"", rootGrid, StringComparison.Ordinal);
        var start = code.IndexOf("private void OnRootPointerReleased(", StringComparison.Ordinal);
        Assert.True(start >= 0, "OnRootPointerReleased not found");
        var handler = code[start..];
        handler = handler[..handler.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("e.Handled = true;", handler, StringComparison.Ordinal);
    }

    private static string Source(string appFile) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", appFile));

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
