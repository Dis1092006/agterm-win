namespace AgTerm.Xterm.Tests;

// The port names itself agterm-win wherever a user sees it, so it never passes for upstream agterm
// (PLATFORM-NOTES). The wire names — agtermctl, AGTERM_*, the `agterm` theme — stay upstream's.
// MainWindow and the build files are not compiled by this project, so their wiring is pinned in source.
public sealed class ProductIdentityTests
{
    [Fact]
    public void Product_name_is_agterm_win()
    {
        Assert.Equal("agterm-win", ProductIdentity.Name);
    }

    [Fact]
    public void Window_title_without_context_is_the_product_name()
    {
        Assert.Equal("agterm-win", ProductIdentity.WindowTitle(null));
        Assert.Equal("agterm-win", ProductIdentity.WindowTitle(""));
    }

    [Fact]
    public void Window_title_with_context_appends_the_product_name()
    {
        Assert.Equal("Dashboard — agterm-win", ProductIdentity.WindowTitle("Dashboard"));
    }

    [Fact]
    public void Main_window_takes_every_title_from_the_product_identity()
    {
        var window = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        var xaml = Source("src", "AgTerm.App", "MainWindow.xaml");

        Assert.DoesNotContain("\"agterm\"", window, StringComparison.Ordinal);
        Assert.DoesNotContain("— agterm\"", window, StringComparison.Ordinal);
        Assert.Contains("ProductIdentity.WindowTitle(", window, StringComparison.Ordinal);
        Assert.Contains("ProductIdentity.Name", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Title=\"agterm\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"agterm-win\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void App_executable_and_its_version_resource_are_named_agterm_win()
    {
        var project = Source("src", "AgTerm.App", "AgTerm.App.csproj");
        var manifest = Source("src", "AgTerm.App", "app.manifest");

        Assert.Contains("<AssemblyName>agterm-win</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains("<Product>agterm-win</Product>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyTitle>agterm-win</AssemblyTitle>", project, StringComparison.Ordinal);
        Assert.Contains("name=\"agterm-win\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_lands_in_an_agterm_win_directory()
    {
        var publish = Source("publish.proj");

        Assert.Contains(@"artifacts\publish\agterm-win\$(_Pivot)", publish, StringComparison.Ordinal);
        Assert.DoesNotContain(@"artifacts\publish\agterm\", publish, StringComparison.Ordinal);
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepoRoot(), .. parts]));

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
