namespace AgTerm.Xterm.Tests;

// upstream WindowContentView.shows(_:): hiddenInterfaceElements hides the chrome the port draws —
// the title bar's sidebar toggle and session name, the sidebar footer's new-workspace and flagged
// buttons. MainWindow and SidebarView are WinUI types the test project does not compile, so the
// wiring is pinned in source; AppSettingsTests covers IsInterfaceElementHidden.
public sealed class InterfaceElementsSourceTests
{
    [Fact]
    public void Title_bar_hides_the_sidebar_toggle_and_reapplies_on_every_settings_change()
    {
        var window = Source("MainWindow.xaml.cs");

        var apply = MethodBody(window, "private void ApplyInterfaceElements(AppSettings settings)");
        Assert.Contains(
            "_sidebarToggleShown = !settings.IsInterfaceElementHidden(InterfaceElement.SidebarToggle);",
            apply, StringComparison.Ordinal);
        Assert.Contains("ApplyModalChrome();", apply, StringComparison.Ordinal);
        Assert.Contains("ApplyInterfaceElements(settings);",
            MethodBody(window, "private async Task ShowThemePresentationAsync("), StringComparison.Ordinal);
        Assert.Contains("ApplyInterfaceElements(_settings);", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Title_bar_text_goes_through_the_session_and_window_name_elements()
    {
        var window = Source("MainWindow.xaml.cs");

        var refresh = MethodBody(window, "private void RefreshTitles(AppSettings settings)");
        Assert.Contains("AppTitleBar.Title = TitleBarText.Visible(_sessionTitle ?? ProductIdentity.Name, _customWindowName, settings);",
            refresh, StringComparison.Ordinal);
        Assert.Contains("Title = TitleBarText.OsTitle(_sessionTitle, _customWindowName);", refresh, StringComparison.Ordinal);
        Assert.Contains("RefreshTitles(settings);",
            MethodBody(window, "private void ApplyInterfaceElements(AppSettings settings)"), StringComparison.Ordinal);
        Assert.Contains("ShowSessionTitle(title);",
            MethodBody(window, "private void ApplyAcceptedBaseLayout("), StringComparison.Ordinal);
        Assert.DoesNotContain("AppTitleBar.Title = title;", window, StringComparison.Ordinal);
        Assert.DoesNotContain("AppTitleBar.Title = ProductIdentity.Name;", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Title = ProductIdentity.Name;", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Title_bar_follows_window_rename()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("_customWindowName = TitleBarText.CustomWindowName(host.Host.WindowName);", window, StringComparison.Ordinal);
        Assert.Contains("host.Host.Renamed += name => DispatcherQueue.TryEnqueue(() =>", window, StringComparison.Ordinal);
        Assert.Contains("_customWindowName = TitleBarText.CustomWindowName(name);", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_footer_hides_the_new_workspace_and_flagged_buttons()
    {
        var xaml = Source("SidebarView.xaml");
        var apply = MethodBody(Source("SidebarView.xaml.cs"), "public void ApplySettings(AppSettings settings)");

        Assert.Contains("x:Name=\"NewWorkspaceButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("NewWorkspaceButton.Visibility = VisibleUnless(settings, InterfaceElement.NewWorkspace);",
            apply, StringComparison.Ordinal);
        Assert.Contains("FlaggedToggle.Visibility = VisibleUnless(settings, InterfaceElement.FlaggedView);",
            apply, StringComparison.Ordinal);
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
