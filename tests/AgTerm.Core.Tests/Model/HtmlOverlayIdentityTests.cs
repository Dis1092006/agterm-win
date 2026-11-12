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

    [Theory]
    [InlineData("https://trusted.example@attacker.example/path", "https://attacker.example")]
    [InlineData("http://127.0.0.1:5173/", "http://127.0.0.1:5173")]
    [InlineData("HTTPS://Example.COM:443/a", "https://example.com")]
    [InlineData("http://[::1]:8080/", "http://[::1]:8080")]
    public void OriginNamesTheRealHost(string address, string origin) =>
        Assert.Equal(origin, HtmlSource.Origin(new Uri(address)));

    [Theory]
    [InlineData("file:///C:/a/report.html")]
    [InlineData("mailto:a@example.com")]
    public void OriginIsNullWithoutAWebOrigin(string address) =>
        Assert.Null(HtmlSource.Origin(new Uri(address)));
}
