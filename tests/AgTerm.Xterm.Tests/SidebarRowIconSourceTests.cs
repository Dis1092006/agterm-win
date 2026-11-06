namespace AgTerm.Xterm.Tests;

// SidebarRowViewModel and SidebarView are WinUI types the test project does not compile, so the row
// icon wiring is pinned in their source; SidebarRowIconTests and SidebarRowIndentTests cover the rules.
public sealed class SidebarRowIconSourceTests
{
    [Fact]
    public void The_session_icon_binds_its_own_visibility()
    {
        var xaml = Source("SidebarView.xaml");
        var start = xaml.IndexOf("<!-- session icon:", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var path = xaml[start..xaml.IndexOf("</Path>", start, StringComparison.Ordinal)];

        Assert.Contains("Visibility=\"{x:Bind SessionIconVisibility, Mode=OneWay}\"", path, StringComparison.Ordinal);
    }

    [Fact]
    public void The_row_icons_follow_the_interface_setting()
    {
        var model = Source("SidebarRowViewModel.cs");

        Assert.Contains("IsSession && SidebarRowIcon.IsShown(Kind, _settings)", model, StringComparison.Ordinal);
        Assert.Contains("IsWorkspace && SidebarRowIcon.IsShown(Kind, _settings)", MethodBody(model, "private Visibility IconVisibility("),
            StringComparison.Ordinal);
        Assert.Contains("SidebarRowIndent.Of(Kind, _nested, SidebarRowIcon.IsShown(Kind, _settings))", model, StringComparison.Ordinal);
    }

    [Fact]
    public void A_settings_change_reapplies_the_icons_and_the_margin()
    {
        var update = MethodBody(Source("SidebarRowViewModel.cs"), "public void Update(");

        Assert.Contains("nameof(SessionIconVisibility)", update, StringComparison.Ordinal);
        Assert.Contains("nameof(ContentMargin)", update, StringComparison.Ordinal);
        Assert.Contains("nameof(GridIconVisibility)", update, StringComparison.Ordinal);
        Assert.Contains("nameof(WorkspaceGlyphVisibility)", update, StringComparison.Ordinal);
        Assert.Contains("nameof(WorkspaceImageVisibility)", update, StringComparison.Ordinal);
    }

    // from the signature to the first line that closes a member at class indentation
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
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
