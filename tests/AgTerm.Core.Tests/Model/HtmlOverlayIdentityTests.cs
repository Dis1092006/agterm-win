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
    [InlineData("http://localhost:5173/app?x=1", "http://localhost:5173")]
    [InlineData("https://Example.com:443/docs", "https://example.com")]
    [InlineData("http://example.com:8080/", "http://example.com:8080")]
    [InlineData("http://[::1]:8080/", "http://[::1]:8080")]
    public void UrlIdentityIsTheOrigin(string address, string identity)
    {
        var overlay = new HtmlOverlay(new HtmlSource.Url(new Uri(address)));
        Assert.Equal(identity, overlay.Identity);
        overlay.Current = new HtmlPageInfo("http://localhost:5173/other", "agterm: enter your password", false, false);
        Assert.Equal(identity, overlay.Identity);
    }

    [Fact]
    public void ABrowsingUrlPageIsNamedByTheOriginItShows()
    {
        var overlay = new HtmlOverlay(new HtmlSource.Url(new Uri("http://localhost:5173/"))) { Browse = true };
        Assert.Equal("http://localhost:5173", overlay.Identity);
        overlay.Current = new HtmlPageInfo("https://example.com/x", null, true, false);
        Assert.Equal("https://example.com", overlay.Identity);
        overlay.Current = new HtmlPageInfo("about:blank", null, true, false);
        Assert.Equal("about:blank", overlay.Identity);
    }

    [Fact]
    public void AUrlPageIsTheAddressAsGivenUntilTheViewReports()
    {
        var overlay = new HtmlOverlay(new HtmlSource.Url(new Uri("HTTP://Example.com:80/a")));
        Assert.Equal("HTTP://Example.com:80/a", overlay.PageAt(null));
        Assert.Equal("http://example.com/b", overlay.PageAt(new HtmlPageInfo("http://example.com/b", null, true, false)));
    }

    [Theory]
    [InlineData("http://example.com/", "http://EXAMPLE.com:80/a", true)]
    [InlineData("https://example.com/", "https://example.com:443/b", true)]
    [InlineData("HTTPS://example.com/", "https://example.com/", true)]
    [InlineData("http://example.com/", "https://example.com/", false)]
    [InlineData("http://example.com/", "http://example.com:8080/", false)]
    [InlineData("http://example.com/", "http://www.example.com/", false)]
    [InlineData("http://[::1]:8080/", "http://[::1]:8080/x", true)]
    [InlineData("file:///C:/a.html", "file:///C:/a.html", false)]
    public void SameOriginComparesSchemeHostAndEffectivePort(string left, string right, bool same) =>
        Assert.Equal(same, HtmlSource.SameOrigin(new Uri(left), new Uri(right)));

    [Theory]
    [InlineData("http://localhost:5173/", true)]
    [InlineData("https://example.com/docs?q=1#top", true)]
    [InlineData("HTTPS://example.com", true)]
    [InlineData("http://127.0.0.1:8000", true)]
    [InlineData("http://[::1]:8080/", true)]
    [InlineData("ftp://example.com/", false)]
    [InlineData("file:///tmp/r.html", false)]
    [InlineData("localhost:5173", false)]
    [InlineData("/tmp/r.html", false)]
    [InlineData(@"C:\tmp\r.html", false)]
    [InlineData("http://", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    public void WebUrlAcceptsOnlyAbsoluteHttpWithAHost(string text, bool valid) =>
        Assert.Equal(valid, HtmlSource.WebUrl(text) is not null);

    [Fact]
    public void WebUrlKeepsTheTextAsGiven() =>
        Assert.Equal("HTTP://Example.com:80", HtmlSource.WebUrl("HTTP://Example.com:80")!.OriginalString);

    [Theory]
    [InlineData("file:///C:/a/report.html")]
    [InlineData("mailto:a@example.com")]
    public void OriginIsNullWithoutAWebOrigin(string address) =>
        Assert.Null(HtmlSource.Origin(new Uri(address)));
}
