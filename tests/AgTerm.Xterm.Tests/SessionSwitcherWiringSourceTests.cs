namespace AgTerm.Xterm.Tests;

// upstream SessionSwitcher + SessionSwitcherOverlay in the WinUI window. MainWindow and the overlay are
// WinUI types the test project does not compile, so the wiring is pinned in source; SessionSwitcherTests,
// TitleBarStateTests and LiveSessionSwitcherKeysTests cover the state, the candidates and the page keys.
public sealed class SessionSwitcherWiringSourceTests
{
    [Fact]
    public void The_window_hosts_a_keyboard_only_switcher_overlay()
    {
        var window = Source("MainWindow.xaml");
        var view = Source("SessionSwitcherView.xaml");

        Assert.Contains("<local:SessionSwitcherView x:Name=\"SwitcherOverlay\"", window, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"session-switcher\"", view, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible=\"False\"", view, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"Recent sessions (Ctrl+Tab)\"", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_keys_and_the_window_keys_drive_one_switcher()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("page.SwitcherKeyPressed += key => DispatcherQueue.TryEnqueue(() => HandleSwitcherKey(key));",
            window, StringComparison.Ordinal);
        var handle = MethodBody(window, "private void HandleSwitcherKey(SessionSwitcherKey key)");
        Assert.Contains("_switcher.Tab(key == SessionSwitcherKey.ReverseTab, CanSwitchSessions(), () => _titleBarState.SwitcherSessions)",
            handle, StringComparison.Ordinal);
        Assert.Contains("_switcher.Escape()", handle, StringComparison.Ordinal);
        Assert.Contains("_switcher.ControlReleased(CanSwitchSessions())", handle, StringComparison.Ordinal);
        Assert.Contains("SelectRecentSessionAsync(", handle, StringComparison.Ordinal);
        Assert.Contains("SyncPageSwitcher();", handle, StringComparison.Ordinal);

        var down = MethodBody(window, "private void OnRootPreviewKeyDown(");
        Assert.Contains("IsTerminalFocused()", down, StringComparison.Ordinal);
        Assert.Contains("VirtualKey.Tab", down, StringComparison.Ordinal);
        Assert.Contains("VirtualKey.Escape", down, StringComparison.Ordinal);
        Assert.Contains("VirtualKey.Control", MethodBody(window, "private void OnRootPreviewKeyUp("), StringComparison.Ordinal);
        var root = Source("MainWindow.xaml");
        Assert.Contains("PreviewKeyDown=\"OnRootPreviewKeyDown\"", root, StringComparison.Ordinal);
        Assert.Contains("PreviewKeyUp=\"OnRootPreviewKeyUp\"", root, StringComparison.Ordinal);
    }

    [Fact]
    public void A_switch_is_abandoned_when_the_window_loses_the_keyboard()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("_switcher.Cancel();", MethodBody(window, "private void OnWindowActivated("), StringComparison.Ordinal);
        Assert.Contains("if (snapshot.DashboardOpen) _switcher.Cancel();", window, StringComparison.Ordinal);
        var changed = MethodBody(window, "private void OnSwitcherChanged()");
        Assert.Contains("SwitcherOverlay.Show(_switcher.Candidates, _switcher.Index);", changed, StringComparison.Ordinal);
        Assert.Contains("SwitcherOverlay.Hide();", changed, StringComparison.Ordinal);
    }

    // from the signature to the first line that closes a member at class indentation
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static string Source(string appFile)
    {
        var path = Path.Combine(FindRepoRoot(), "src", "AgTerm.App", appFile);
        Assert.True(File.Exists(path), $"{appFile} not found");
        return File.ReadAllText(path);
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
