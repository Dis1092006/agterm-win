using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public class XtermPageLinkTests
{
    [Theory]
    [InlineData("""{"type":"terminalLink","paneId":"known","url":"https://example.com"}""", true)]
    [InlineData("""{"type":"terminalLink","paneId":"known","url":"C:\\files\\x.txt"}""", true)]
    [InlineData("""{"type":"terminalLink","paneId":"other","url":"https://example.com"}""", false)]
    [InlineData("""{"type":"terminalLink","url":"https://example.com"}""", false)]
    [InlineData("""{"type":"terminalLink","paneId":"known","url":""}""", false)]
    [InlineData("""{"type":"terminalLink","paneId":"known","url":null}""", false)]
    [InlineData("""{"type":"terminalLink","paneId":"known","url":123}""", false)]
    public async Task A_terminal_link_routes_only_for_a_known_pane(string json, bool expected)
    {
        var host = new FakeScriptHost();
        var page = new XtermPage(host);
        var ready = page.InitializeAsync();
        host.RaiseMessage("""{"type":"ready"}""");
        await ready;
        await page.OpenTerminalAsync("known");
        var seen = new List<(string PaneId, string Url)>();
        page.TerminalLinkRequested += (pane, url, fallback) =>
        {
            Assert.False(fallback);
            seen.Add((pane, url));
        };

        host.RaiseMessage(json);

        Assert.Equal(expected ? 1 : 0, seen.Count);
        if (expected)
            Assert.Equal("known", seen[0].PaneId);
        await page.ClosePaneAsync("known");
        seen.Clear();
        host.RaiseMessage(json);
        Assert.Empty(seen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_internal_path_fallback_flag_routes_with_the_target(bool allowPathFallback)
    {
        var host = new FakeScriptHost();
        var page = new XtermPage(host);
        var ready = page.InitializeAsync();
        host.RaiseMessage("""{"type":"ready"}""");
        await ready;
        await page.OpenTerminalAsync("known");
        var seen = new List<(string PaneId, string Target, bool AllowPathFallback)>();
        page.TerminalLinkRequested += (pane, target, fallback) => seen.Add((pane, target, fallback));
        host.RaiseMessage(System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "terminalLink", paneId = "known", url = @"C:\Program Files\x status", allowPathFallback,
        }));
        Assert.Equal(("known", @"C:\Program Files\x status", allowPathFallback), Assert.Single(seen));
        await page.ClosePaneAsync("known");
    }

    [Fact]
    public void The_app_wires_terminal_links_through_the_checked_launcher_and_failure_notice()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
            directory = directory.Parent;
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "src", "AgTerm.App", "MainWindow.xaml.cs"));
        Assert.Contains("page.TerminalLinkRequested +=", source, StringComparison.Ordinal);
        Assert.Contains("new TerminalLinkOpener().Open(target, allowPathFallback)", source, StringComparison.Ordinal);
        Assert.Contains("\"Open terminal link failed\"", source, StringComparison.Ordinal);
        Assert.Contains("QueueFailureNotice(notice)", source, StringComparison.Ordinal);
    }
}
