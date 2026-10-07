using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public class XtermAssetsTests
{
    [Fact]
    public void Assets_ship_next_to_the_assembly_with_the_pinned_bundles()
    {
        var dir = XtermAssets.Directory;
        Assert.True(File.Exists(Path.Combine(dir, "terminal.html")), $"terminal.html missing in {dir}");
        Assert.True(File.Exists(Path.Combine(dir, "bridge.js")));
        foreach (var vendor in new[] { "xterm.js", "xterm.css", "addon-fit.js", "addon-search.js", "addon-serialize.js" })
            Assert.True(File.Exists(Path.Combine(dir, "vendor", vendor)), $"{vendor} missing");
    }

    /// <summary>
    /// The assets must reach a PUBLISH directory too, not only a build output. They are declared in
    /// AgTerm.Xterm while the published project is AgTerm.App, and a referenced project's None items
    /// are copied to a build output but not to a publish directory unless CopyToPublishDirectory
    /// says so. Without it `dotnet build publish.proj` produced an exe next to no Assets folder,
    /// XtermAssets.Directory pointed at a path that did not exist, and MapVirtualHostToFolder had
    /// nothing to serve — while every build-output test kept passing.
    /// </summary>
    [Fact]
    public void Assets_are_declared_to_follow_a_publish_not_only_a_build()
    {
        var csproj = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "AgTerm.Xterm", "AgTerm.Xterm.csproj"));
        var assets = csproj
            .Split('<')
            .Single(e => e.StartsWith("None Include=\"Assets", StringComparison.Ordinal));
        Assert.Contains("CopyToPublishDirectory", assets);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
