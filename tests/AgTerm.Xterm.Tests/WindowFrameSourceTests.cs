namespace AgTerm.Xterm.Tests;

// MainWindow is a WinUI type the test project does not compile, so its frame wiring is pinned in its
// source; the frame logic itself is covered in AgTerm.Core.Tests.
public sealed class WindowFrameSourceTests
{
    [Fact]
    public void The_window_restores_its_frame_before_it_is_first_shown()
    {
        var code = MainWindowSource();

        var subscribe = code.IndexOf("_appWindow.Changed += OnAppWindowChanged;", StringComparison.Ordinal);
        var restore = code.IndexOf("RestoreWindowFrame();", StringComparison.Ordinal);
        Assert.True(subscribe >= 0 && restore > subscribe, "RestoreWindowFrame(); must follow the Changed subscription");
        var body = Method(code, "private void RestoreWindowFrame(");
        Assert.Contains("_frames.Load(_windowId)", body, StringComparison.Ordinal);
        Assert.Contains("WindowFrameRestore.Constrain(", body, StringComparison.Ordinal);
        Assert.Contains("MoveAndResizeOnTargetDisplay(", body, StringComparison.Ordinal);
        Assert.Contains("Maximize()", body, StringComparison.Ordinal);
    }

    // a window created on a display with another scale is resized by the DPI change on its way to
    // the saved display, so the saved pixels are applied again once it is there
    [Fact]
    public void A_frame_restored_across_display_scales_is_applied_again_on_the_target_display()
    {
        var body = Method(MainWindowSource(), "private void MoveAndResizeOnTargetDisplay(");

        var first = body.IndexOf("_appWindow.MoveAndResize(", StringComparison.Ordinal);
        Assert.True(first >= 0, "MoveAndResize not found");
        Assert.True(body.IndexOf("_appWindow.MoveAndResize(", first + 1, StringComparison.Ordinal) > first);
        Assert.Contains("_appWindow.Size", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Entering_full_screen_tells_the_frame_tracker_first()
    {
        var body = Method(MainWindowSource(), "private void ApplyFullscreen(");

        var entering = body.IndexOf("_frameTracker.EnteringFullScreen();", StringComparison.Ordinal);
        Assert.True(entering >= 0, "EnteringFullScreen not found");
        Assert.True(entering < body.IndexOf("SetPresenter(AppWindowPresenterKind.FullScreen)", StringComparison.Ordinal));
        Assert.Contains("_frameTracker.FullScreenCancelled();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_move_resize_and_state_change_reaches_the_frame_tracker()
    {
        var body = Method(MainWindowSource(), "private void OnAppWindowChanged(");

        var observe = body.IndexOf("ObserveWindowFrame();", StringComparison.Ordinal);
        Assert.True(observe >= 0, "ObserveWindowFrame(); not found");
        Assert.True(observe < body.IndexOf("if (!args.DidPresenterChange)", StringComparison.Ordinal));
    }

    [Fact]
    public void Both_close_paths_save_the_frame_before_the_shutdown()
    {
        var body = Method(MainWindowSource(), "private async Task ShutdownForCloseAsync(");

        var save = body.IndexOf("SaveWindowFrame();", StringComparison.Ordinal);
        Assert.True(save >= 0, "SaveWindowFrame(); not found");
        Assert.True(save < body.IndexOf("await ", StringComparison.Ordinal));
        Assert.Contains("_frames.Save(_windowId,", Method(MainWindowSource(), "private void SaveWindowFrame("), StringComparison.Ordinal);
    }

    private static string MainWindowSource() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

    private static string Method(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var body = code[start..];
        return body[..body.IndexOf("\n    }", StringComparison.Ordinal)];
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
