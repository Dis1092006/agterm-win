namespace AgTerm.Xterm.Tests;

public sealed class AskViewSourceTests
{
    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(path).ToArray()));

    [Fact]
    public void The_window_hosts_the_dialog_over_the_content_row()
    {
        var xaml = Source("src", "AgTerm.App", "MainWindow.xaml");
        Assert.Contains("<local:AskView x:Name=\"AskOverlay\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_keeps_focus_and_swallows_keys_while_open()
    {
        var xaml = Source("src", "AgTerm.App", "AskView.xaml");
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Contains("IsTabStop=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"OnKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTabStop = false", code, StringComparison.Ordinal);
        Assert.Contains("AllowFocusOnInteraction = false", code, StringComparison.Ordinal);
        Assert.Contains("AskKeys.Intent(", code, StringComparison.Ordinal);
        Assert.Contains("AskLabel.Runs(", code, StringComparison.Ordinal);
        Assert.Contains("AskPanelLayout.Arrange(", code, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_presents_the_gui_ask_and_answers_it()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Contains("SyncAskAsync", source, StringComparison.Ordinal);
        Assert.Contains(".Pick.PendingAsk", source, StringComparison.Ordinal);
        Assert.Contains(".Pick.ResolveAsk(", source, StringComparison.Ordinal);
        Assert.Contains(".Pick.EscapeAsk(", source, StringComparison.Ordinal);
        Assert.Contains("AskOverlay.CancelChord = IsCloseSessionChord;", source, StringComparison.Ordinal);
        Assert.Contains("AskAnchorGeometry.Pane(", source, StringComparison.Ordinal);
        Assert.Contains("if (AskOverlay.IsOpen) return;", source, StringComparison.Ordinal);
        Assert.Contains("_restoreFocusWhenActivated && !PaletteOverlay.IsOpen && !AskOverlay.IsOpen", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
