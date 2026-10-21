namespace AgTerm.Xterm.Tests;

public sealed class WebViewZoomSourceTests
{
    [Fact]
    public void MainWindow_turns_off_page_zoom_before_the_page_boots()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        var ensure = source.IndexOf("await view.EnsureCoreWebView2Async(environment);", StringComparison.Ordinal);
        var zoom = source.IndexOf("view.CoreWebView2.Settings.IsZoomControlEnabled = false;", StringComparison.Ordinal);
        var page = source.IndexOf("new XtermPage(new WinRtScriptHost(view.CoreWebView2))", StringComparison.Ordinal);
        Assert.True(ensure >= 0 && ensure < zoom && zoom < page);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
