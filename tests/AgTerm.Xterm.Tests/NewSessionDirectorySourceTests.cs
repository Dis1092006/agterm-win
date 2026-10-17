namespace AgTerm.Xterm.Tests;

// MainWindow is a WinUI type the test project does not compile, so the settings it hands the sidebar
// controller are pinned in its source; SidebarControllerTests covers the resolution.
public sealed class NewSessionDirectorySourceTests
{
    [Fact]
    public void Main_window_gives_the_sidebar_controller_its_live_settings()
    {
        var window = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));
        var start = window.IndexOf("new SidebarController(", StringComparison.Ordinal);
        Assert.True(start >= 0, "new SidebarController( not found");
        var call = window[start..window.IndexOf(");", start, StringComparison.Ordinal)];

        Assert.Contains("settings: () => _settings", call, StringComparison.Ordinal);
    }

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
