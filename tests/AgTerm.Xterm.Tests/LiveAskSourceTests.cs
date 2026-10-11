namespace AgTerm.Xterm.Tests;

public sealed class LiveAskSourceTests
{
    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(path).ToArray()));

    [Fact]
    public void The_live_control_host_refuses_terminal_asks_until_the_page_draws_them()
    {
        var source = Source("src", "AgTerm.Xterm", "LiveControlHost.cs");
        Assert.Contains("TerminalAskSupported = false", source, StringComparison.Ordinal);
        Assert.Contains("terminalAskSupported: false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_live_control_host_reconciles_a_gui_ask_anchor_after_every_change()
    {
        var source = Source("src", "AgTerm.Xterm", "LiveControlHost.cs");
        var reconcile = source.IndexOf("private void ReconcileSurfacesLocked()", StringComparison.Ordinal);
        Assert.True(reconcile >= 0);
        var body = source.Substring(reconcile, 400);
        Assert.Contains("Host.Actions.ReconcileAskAnchor();", body, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
