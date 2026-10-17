namespace AgTerm.Xterm.Tests;

// upstream titlebarTrailingActions: recent sessions + attention | scratch + split | dashboard + quick
// terminal, each gated by its interface element (the bell by attentionButtonEnabled), with a divider only
// between two full groups. MainWindow is a WinUI type the test project does not compile, so the wiring is
// pinned in source; TitleBarStateTests and AppSettingsTests cover the state and the divider rule.
public sealed class TitleBarActionsSourceTests
{
    [Fact]
    public void Title_bar_right_header_holds_upstreams_buttons_in_order()
    {
        var xaml = Source("MainWindow.xaml");
        var start = xaml.IndexOf("<TitleBar.RightHeader>", StringComparison.Ordinal);
        Assert.True(start >= 0, "TitleBar.RightHeader not found");
        var header = xaml[start..xaml.IndexOf("</TitleBar.RightHeader>", start, StringComparison.Ordinal)];

        string[] order =
        [
            "recent-sessions-button", "attention-button", "x:Name=\"DividerAfterA\"",
            "scratch-toggle", "split-toggle", "x:Name=\"DividerAfterB\"",
            "dashboard-toggle-button", "quick-terminal-toggle",
        ];
        var positions = order.Select(token => header.IndexOf(token, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, position => Assert.True(position >= 0));
        Assert.Equal(positions.Order(), positions);
        Assert.Contains("Opening=\"OnRecentSessionsFlyoutOpening\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_content_keeps_the_title_bar_at_its_compact_height()
    {
        // TitleBar switches to TitleBarExpandedHeight (48) once it has header content; the buttons fit
        // the compact 32, so the expanded height is overridden on the control itself
        var xaml = Source("MainWindow.xaml");
        var start = xaml.IndexOf("<TitleBar.Resources>", StringComparison.Ordinal);
        Assert.True(start >= 0, "TitleBar.Resources not found");
        var resources = xaml[start..xaml.IndexOf("</TitleBar.Resources>", start, StringComparison.Ordinal)];

        Assert.Contains("<x:Double x:Key=\"TitleBarExpandedHeight\">32</x:Double>", resources, StringComparison.Ordinal);
    }

    [Fact]
    public void Buttons_follow_the_snapshot_and_the_settings()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("ApplyTitleBarState(snapshot.TitleBar);",
            MethodBody(window, "private void ApplyChrome(SidebarSnapshot snapshot)"), StringComparison.Ordinal);
        Assert.Contains("RefreshTitleBarActions(settings);",
            MethodBody(window, "private void ApplyInterfaceElements(AppSettings settings)"), StringComparison.Ordinal);
        var refresh = MethodBody(window, "private void RefreshTitleBarActions(AppSettings settings)");
        Assert.Contains("InterfaceElementExtensions.TitlebarGroupDividers(", refresh, StringComparison.Ordinal);
        Assert.Contains("settings.AttentionButtonEnabled == true", refresh, StringComparison.Ordinal);
        foreach (var element in new[] { "RecentSessions", "Scratch", "Split", "Dashboard", "QuickTerminal" })
            Assert.Contains($"InterfaceElement.{element})", refresh, StringComparison.Ordinal);
    }

    // a Windows extension: upstream draws no open state on these two buttons
    [Fact]
    public void Dashboard_and_quick_terminal_buttons_mark_an_open_surface()
    {
        var xaml = Source("MainWindow.xaml");
        foreach (var indicator in new[] { "DashboardOpenIndicator", "QuickTerminalOpenIndicator" })
            Assert.Contains($"<Rectangle x:Name=\"{indicator}\" Style=\"{{StaticResource TitleBarOpenIndicatorStyle}}\" />",
                xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Fill\" Value=\"{ThemeResource AccentFillColorDefaultBrush}\" />", xaml, StringComparison.Ordinal);

        var window = Source("MainWindow.xaml.cs");
        var refresh = MethodBody(window, "private void RefreshTitleBarActions(AppSettings settings)");
        Assert.Contains("ShowOpen(DashboardButton, DashboardOpenIndicator, _selection.DashboardOpen);", refresh, StringComparison.Ordinal);
        Assert.Contains("ShowOpen(QuickTerminalButton, QuickTerminalOpenIndicator, _selection.QuickVisible);", refresh, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetItemStatus(button, open ? \"Open\" : string.Empty);",
            MethodBody(window, "private static void ShowOpen("), StringComparison.Ordinal);

        var selectionUpdate = window[window.IndexOf("_selection = snapshot;", StringComparison.Ordinal)..];
        selectionUpdate = selectionUpdate[..selectionUpdate.IndexOf("SubmitLayout();", StringComparison.Ordinal)];
        Assert.Contains("RefreshTitleBarActions(_settings);", selectionUpdate, StringComparison.Ordinal);
    }

    [Fact]
    public void Buttons_run_the_same_builtin_actions_as_the_hotkeys()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("ExecuteTitleBarAction(BuiltinAction.ShowAttention)", window, StringComparison.Ordinal);
        Assert.Contains("ExecuteTitleBarAction(BuiltinAction.ToggleScratch)", window, StringComparison.Ordinal);
        Assert.Contains("ExecuteTitleBarAction(BuiltinAction.ToggleSplit)", window, StringComparison.Ordinal);
        Assert.Contains("ExecuteTitleBarAction(BuiltinAction.Dashboard)", window, StringComparison.Ordinal);
        Assert.Contains("ExecuteTitleBarAction(BuiltinAction.QuickTerminal)", window, StringComparison.Ordinal);
        Assert.Contains("WindowPaletteActions.SelectSession(store, sessionId)",
            MethodBody(window, "private async Task SelectRecentSessionAsync(Guid sessionId)"), StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
