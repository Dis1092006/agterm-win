namespace AgTerm.Xterm.Tests;

public sealed class WebViewZoomSourceTests
{
    [Fact]
    public void MainWindow_turns_off_page_zoom_before_the_page_boots()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        Assert.Matches(
            @"await TerminalView\.EnsureCoreWebView2Async\(environment\);\s*(//[^\n]*\n\s*)*TerminalView\.CoreWebView2\.Settings\.IsZoomControlEnabled = false;",
            source);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
