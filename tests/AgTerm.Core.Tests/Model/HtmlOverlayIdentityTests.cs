using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// What the page's strip names (upstream HtmlOverlay.identity, c63e5158).
public class HtmlOverlayIdentityTests
{
    [Fact]
    public void FileIdentityIsTheFileShownNeverTheTitle()
    {
        var overlay = new HtmlOverlay(new HtmlSource.File(@"C:\a\docs\index.html", @"C:\a\docs"));
        Assert.Equal("index.html", overlay.Identity);
        overlay.Current = new HtmlPageInfo(@"C:\a\docs\b.html", "agterm: enter your password", true, false);
        Assert.Equal("b.html", overlay.Identity);
    }
}
