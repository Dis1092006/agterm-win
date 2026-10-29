using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class WindowForegroundTests
{
    [Fact]
    public void A_window_that_came_to_the_front_does_not_flash()
    {
        var flashed = false;

        WindowForeground.Raise(bringToFront: () => true, flash: () => flashed = true);

        Assert.False(flashed);
    }

    [Fact]
    public void A_window_that_Windows_kept_in_the_background_flashes()
    {
        var flashed = false;

        WindowForeground.Raise(bringToFront: () => false, flash: () => flashed = true);

        Assert.True(flashed);
    }

    [Fact]
    public void MainWindow_asks_Windows_for_the_front_after_activating()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Matches(@"internal void ActivateWindow\(\)\s*\{\s*Activate\(\);\s*WindowForeground\.Raise\(WinRT\.Interop\.WindowNative\.GetWindowHandle\(this\)\);\s*\(\(App\)Application\.Current\)\.NoteTaskbarWindowRaised\(\s*WinRT\.Interop\.WindowNative\.GetWindowHandle\(this\)\);\s*\}", source);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
