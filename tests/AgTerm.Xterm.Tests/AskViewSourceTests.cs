using System.Text.RegularExpressions;

namespace AgTerm.Xterm.Tests;

public sealed class AskViewSourceTests
{
    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(path).ToArray()));

    [Fact]
    public void The_window_hosts_the_dialog_over_both_rows()
    {
        var xaml = Source("src", "AgTerm.App", "MainWindow.xaml");
        Assert.Contains("<local:AskView x:Name=\"AskOverlay\"", xaml, StringComparison.Ordinal);
        // over the title bar row too, so its pane toggle cannot run or take focus while the question is up
        Assert.Matches(new Regex(@"<local:AskView x:Name=""AskOverlay""\s+Grid\.RowSpan=""2""\s+Canvas\.ZIndex=""100"" />"), xaml);
    }

    [Fact]
    public void The_window_syncs_the_shown_modal_first_so_its_close_runs_before_the_other_opens()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Matches(new Regex(
            @"private async Task SyncModalAsync\(\)\s*\{\s*if \(_shownAskId is not null\)\s*\{\s*await SyncAskAsync\(\);\s*await SyncPickAsync\(\);\s*\}\s*else\s*\{\s*await SyncPickAsync\(\);\s*await SyncAskAsync\(\);\s*\}\s*\}"),
            source);
    }

    [Fact]
    public void No_chord_or_terminal_focus_gets_past_an_open_dialog()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Matches(new Regex(
            @"private bool HandleChord\(PhysicalChord physical, long\? generation = null\)\s*\{\s*if \(AskOverlay\.IsOpen\)\s*return false;"),
            source);
        Assert.Matches(new Regex(
            @"private void FocusTerminal\(string paneId\)\s*\{\s*if \(AskOverlay\.IsOpen\)\s*\{\s*(//[^
]*\s*)*if \(_windowIsActive\)\s*AskOverlay\.FocusPanel\(\);\s*return;\s*\}"),
            source);
    }

    [Fact]
    public void The_window_escapes_only_the_ask_it_still_holds()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Matches(new Regex(
            @"private async Task HandleAskEscapedAsync\(string askId\)\s*\{\s*if \(_shownAskId != askId \|\| _host is not \{ \} host \|\| host\.Host\.Actions\.Pick\.PendingAsk\?\.Id != askId\)\s*return;"),
            source);
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
