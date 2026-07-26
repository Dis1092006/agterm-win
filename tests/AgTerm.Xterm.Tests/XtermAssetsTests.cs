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
}
