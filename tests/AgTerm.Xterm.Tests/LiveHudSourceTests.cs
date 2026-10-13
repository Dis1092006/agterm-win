using System.Text.RegularExpressions;

namespace AgTerm.Xterm.Tests;

public sealed class LiveHudSourceTests
{
    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(path).ToArray()));

    [Fact]
    public void The_window_draws_the_hud_of_each_snapshot_and_feeds_the_page_metrics_back()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Matches(new Regex(@"PresentTerminalAsk\(snapshot\.TerminalAsk\);\s*PresentHud\(snapshot\.Hud\);"), source);
        Assert.Contains("page.HudMetricsReceived += OnPageHudMetricsReceived;", source, StringComparison.Ordinal);
        Assert.Contains("TerminalHudViews.From(", source, StringComparison.Ordinal);
        Assert.Contains("await page.ApplyHudAsync(view);", source, StringComparison.Ordinal);
        Assert.Contains("await host.ApplyHudMetricsAsync(paneId, metrics, singlePane);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Presenting_a_hud_moves_no_focus()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        var start = source.IndexOf("private void PresentHud(", StringComparison.Ordinal);
        var end = source.IndexOf("private void OnPageHudMetricsReceived(", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "PresentHud and ApplyHudAsync come before OnPageHudMetricsReceived");
        Assert.DoesNotContain("Focus", source[start..end], StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
