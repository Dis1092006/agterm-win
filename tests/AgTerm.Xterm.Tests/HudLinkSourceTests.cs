namespace AgTerm.Xterm.Tests;

public sealed class HudLinkSourceTests
{
    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(path).ToArray()));

    [Fact]
    public void The_window_opens_a_hud_link_only_through_the_policy()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Contains(".HudLinkRequested +=", source, StringComparison.Ordinal);
        Assert.Contains("HudLinkPolicy.ActionFor(", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", source, StringComparison.Ordinal);
        Assert.Contains("HudLinkPolicy.ExplorerSelectArguments(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_never_parses_html()
    {
        var bridge = Source("src", "AgTerm.Xterm", "Assets", "bridge.js");
        Assert.DoesNotContain("innerHTML", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("outerHTML", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("insertAdjacentHTML", bridge, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
