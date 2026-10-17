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
            "AppTitleBar.IsPaneToggleButtonVisible = !settings.IsInterfaceElementHidden(InterfaceElement.SidebarToggle);",
            apply, StringComparison.Ordinal);
        Assert.Contains("ApplyInterfaceElements(settings);",
            MethodBody(window, "private async Task ShowThemePresentationAsync("), StringComparison.Ordinal);
        Assert.Contains("ApplyInterfaceElements(_settings);", window, StringComparison.Ordinal);
    }

    [Fact]
    public void Title_bar_session_name_goes_through_the_session_name_element()
    {
        var window = Source("MainWindow.xaml.cs");

        Assert.Contains("InterfaceElement.SessionName",
            MethodBody(window, "private string VisibleSessionTitle(AppSettings settings)"), StringComparison.Ordinal);
        Assert.Contains("SetTitleBarSessionTitle(title);",
            MethodBody(window, "private void ApplyAcceptedBaseLayout("), StringComparison.Ordinal);
        Assert.DoesNotContain("AppTitleBar.Title = title;", window, StringComparison.Ordinal);
        Assert.DoesNotContain("AppTitleBar.Title = ProductIdentity.Name;", window, StringComparison.Ordinal);
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
