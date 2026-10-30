namespace AgTerm.Xterm.Tests;

// Full screen hides the title bar row (the title bar and the hairline under it); the sidebar keeps
// following sidebarVisible. The WinUI types are not compiled by the test project, so the wiring is
// pinned in source.
public sealed class FullscreenChromeSourceTests
{
    [Fact]
    public void The_window_hands_the_fullscreen_controller_a_chrome_callback()
    {
        var code = Source("MainWindow.xaml.cs");

        Assert.Contains("_fullscreen = new FullscreenController(ApplyFullscreen, PresentFullscreenChrome);",
            code, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_screen_collapses_the_title_bar_and_the_hairline_and_leaving_it_shows_them()
    {
        var code = Source("MainWindow.xaml.cs");

        var present = MethodBody(code, "private void PresentFullscreenChrome(bool isFullscreen)");
        Assert.Contains("var titleBar = isFullscreen ? Visibility.Collapsed : Visibility.Visible;", present,
            StringComparison.Ordinal);
        Assert.Contains("AppTitleBar.Visibility = titleBar;", present, StringComparison.Ordinal);
        Assert.Contains("TitleBarHairline.Visibility = titleBar;", present, StringComparison.Ordinal);
        Assert.DoesNotContain("IsPaneOpen", present, StringComparison.Ordinal);
    }

    [Fact]
    public void A_presenter_change_from_outside_the_toggle_goes_through_the_controller()
    {
        var code = Source("MainWindow.xaml.cs");

        var changed = MethodBody(code, "private void OnAppWindowChanged(");
        Assert.Contains("_fullscreen.Synchronize(sender.Presenter.Kind == AppWindowPresenterKind.FullScreen);",
            changed, StringComparison.Ordinal);
        Assert.DoesNotContain("AppTitleBar", changed, StringComparison.Ordinal);
        Assert.DoesNotContain("AppTitleBar", MethodBody(code, "private void ApplyFullscreen(bool enabled)"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Zoom_and_dashboard_no_longer_drive_the_window_chrome()
    {
        var xaml = Source("MainWindow.xaml");
        var code = Source("MainWindow.xaml.cs");

        Assert.DoesNotContain("PresentationStates", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TerminalOnlyPresentation", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SetTerminalOnlyPresentation", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetTerminalCompositionOnly", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_terminalCompositionOnly", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_terminalOnlyPresentation", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sidebar_follows_its_snapshot_in_and_out_of_full_screen()
    {
        var code = Source("MainWindow.xaml.cs");

        var apply = MethodBody(code, "private void ApplyChrome(SidebarSnapshot snapshot)");
        Assert.Contains("ApplySidebarChrome(snapshot);", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("return;", apply, StringComparison.Ordinal);
    }

    // from the signature to the first line that closes a member at class indentation
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static string Source(string appFile) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", appFile));

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("AgTerm.slnx not found above the test output");
    }
}
