namespace AgTerm.Xterm.Tests;

// SidebarRowViewModel and SidebarView are WinUI types the test project does not compile, so the
// workspace row "+" wiring is pinned in their source; SidebarAddSessionButtonTests covers the rule.
public sealed class SidebarAddSessionSourceTests
{
    [Fact]
    public void Row_template_has_a_hover_revealed_new_session_button()
    {
        var xaml = Source("SidebarView.xaml");

        Assert.Contains("PointerEntered=\"OnRowPointerEntered\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PointerExited=\"OnRowPointerExited\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PointerCanceled=\"OnRowPointerExited\"", xaml, StringComparison.Ordinal);
        var button = Element(xaml, "<Button Grid.Column=\"3\"");
        Assert.Contains("Visibility=\"{x:Bind AddSessionVisibility, Mode=OneWay}\"", button, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAddSessionClick\"", button, StringComparison.Ordinal);
        Assert.Contains("IsTabStop=\"False\"", button, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"New Session\"", button, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.AutomationId=\"workspace-add-session\"", button, StringComparison.Ordinal);
    }

    [Fact]
    public void Row_view_model_derives_the_button_from_pointer_over_and_settings()
    {
        var model = Source("SidebarRowViewModel.cs");

        Assert.Contains("SidebarAddSessionButton.IsShown(Kind, IsPointerOver, _settings)", model, StringComparison.Ordinal);
        Assert.Contains("Raise(nameof(IsPointerOver), nameof(AddSessionVisibility))", model, StringComparison.Ordinal);
        Assert.Contains("nameof(AddSessionVisibility)", MethodBody(model, "public void Update("), StringComparison.Ordinal);
    }

    [Fact]
    public void Clicking_the_button_adds_a_session_like_the_context_menu_without_toggling_the_row()
    {
        var view = Source("SidebarView.xaml.cs");

        Assert.Contains("_controller?.NewSession(row.Id)",
            MethodBody(view, "private void OnAddSessionClick("), StringComparison.Ordinal);
        Assert.Contains("if (FromButton(e.OriginalSource)) return;",
            MethodBody(view, "private void OnRowTapped("), StringComparison.Ordinal);
    }

    [Fact]
    public void A_recycled_row_forgets_the_pointer()
    {
        var body = MethodBody(Source("SidebarView.xaml.cs"), "private void OnRowsListContainerContentChanging(");

        Assert.Contains("args.InRecycleQueue", body, StringComparison.Ordinal);
        Assert.Contains("IsPointerOver = false", body, StringComparison.Ordinal);
    }

    // from the opening tag to the button's closing tag
    private static string Element(string xaml, string openTag)
    {
        var start = xaml.IndexOf(openTag, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{openTag} not found");
        var end = xaml.IndexOf("</Button>", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "</Button> not found");
        return xaml[start..end];
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
