namespace AgTerm.Xterm.Tests;

// upstream bottomBar: a New Session menu beside New Workspace — "New Session" in the active workspace
// at the newSessionDirectory cwd, or "Open Directory…" — hidden by the newSession interface element.
// SidebarView is a WinUI type the test project does not compile, so the wiring is pinned in source.
public sealed class SidebarFooterNewSessionSourceTests
{
    [Fact]
    public void Footer_has_a_new_session_menu_with_upstreams_two_items()
    {
        var xaml = Source("SidebarView.xaml");
        var start = xaml.IndexOf("<Button x:Name=\"NewSessionButton\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "NewSessionButton not found");
        var button = xaml[start..xaml.IndexOf("</Button>", start, StringComparison.Ordinal)];

        Assert.Contains("AutomationProperties.Name=\"Add session\"", button, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"add-session\"", button, StringComparison.Ordinal);
        Assert.Contains("<MenuFlyoutItem Text=\"New Session\" Click=\"OnFooterNewSessionClick\" />", button, StringComparison.Ordinal);
        Assert.Contains("<MenuFlyoutItem Text=\"Open Directory…\" Click=\"OnFooterOpenDirectoryClick\" />", button, StringComparison.Ordinal);
    }

    [Fact]
    public void Menu_items_act_on_the_active_workspace()
    {
        var view = Source("SidebarView.xaml.cs");

        Assert.Contains("_controller?.NewSessionInActiveWorkspace();",
            MethodBody(view, "private void OnFooterNewSessionClick("), StringComparison.Ordinal);
        var open = MethodBody(view, "private void OnFooterOpenDirectoryClick(");
        Assert.Contains("_snapshot?.CurrentWorkspaceId is { } workspaceId", open, StringComparison.Ordinal);
        Assert.Contains("OpenDirectoryIntoAsync(workspaceId)", open, StringComparison.Ordinal);
    }

    [Fact]
    public void The_new_session_element_hides_the_menu()
    {
        Assert.Contains("NewSessionButton.Visibility = VisibleUnless(settings, InterfaceElement.NewSession);",
            MethodBody(Source("SidebarView.xaml.cs"), "public void ApplySettings(AppSettings settings)"),
            StringComparison.Ordinal);
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
