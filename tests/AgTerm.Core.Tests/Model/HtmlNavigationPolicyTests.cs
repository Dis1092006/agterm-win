using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// What a file page's web view may load (upstream HtmlNavigationPolicy.decide and its
// navigationPolicy table, c63e5158), with Windows file URLs.
public class HtmlNavigationPolicyTests
{
    [Theory]
    [InlineData("file:///C:/tmp/a/report.html", HtmlNavigationTarget.MainFrame, false, null, HtmlNavigationDecision.Cancel)]
    [InlineData("file:///C:/tmp/a/report.html", HtmlNavigationTarget.MainFrame, false, @"C:\tmp\a", HtmlNavigationDecision.Allow)]
    [InlineData("file:///C:/tmp/a/report.html#section", HtmlNavigationTarget.MainFrame, true, @"C:\tmp\a", HtmlNavigationDecision.Allow)]
    [InlineData("file:///C:/tmp/a/other.html", HtmlNavigationTarget.MainFrame, true, null, HtmlNavigationDecision.Cancel)]
    [InlineData("about:blank", HtmlNavigationTarget.MainFrame, false, null, HtmlNavigationDecision.Allow)]
    [InlineData("about:blank#section", HtmlNavigationTarget.MainFrame, true, null, HtmlNavigationDecision.Allow)]
    [InlineData("file:///C:/tmp/a/other.html", HtmlNavigationTarget.MainFrame, true, @"C:\tmp\a", HtmlNavigationDecision.Allow)]
    [InlineData("file:///C:/tmp/a/frame.html", HtmlNavigationTarget.Subframe, false, @"C:\tmp\a", HtmlNavigationDecision.Allow)]
    [InlineData("file:///C:/Windows/win.ini", HtmlNavigationTarget.MainFrame, true, @"C:\tmp\a", HtmlNavigationDecision.Cancel)]
    [InlineData("file:///C:/tmp/ab/x.html", HtmlNavigationTarget.Subframe, false, @"C:\tmp\a", HtmlNavigationDecision.Cancel)]
    [InlineData("https://example.com/", HtmlNavigationTarget.MainFrame, true, null, HtmlNavigationDecision.OpenExternal)]
    [InlineData("http://example.com/", HtmlNavigationTarget.MainFrame, true, null, HtmlNavigationDecision.OpenExternal)]
    [InlineData("https://example.com/", HtmlNavigationTarget.MainFrame, false, null, HtmlNavigationDecision.Cancel)]
    [InlineData("https://example.com/", HtmlNavigationTarget.Subframe, false, null, HtmlNavigationDecision.Cancel)]
    [InlineData("https://example.com/", HtmlNavigationTarget.Subframe, true, null, HtmlNavigationDecision.OpenExternal)]
    [InlineData("https://example.com/", HtmlNavigationTarget.NewWindow, true, null, HtmlNavigationDecision.OpenExternal)]
    [InlineData("https://example.com/", HtmlNavigationTarget.NewWindow, false, null, HtmlNavigationDecision.Cancel)]
    [InlineData("file:///C:/tmp/a/report.html", HtmlNavigationTarget.NewWindow, true, null, HtmlNavigationDecision.Cancel)]
    [InlineData("about:blank", HtmlNavigationTarget.Subframe, false, null, HtmlNavigationDecision.Allow)]
    [InlineData("mailto:a@example.com", HtmlNavigationTarget.MainFrame, true, null, HtmlNavigationDecision.Cancel)]
    [InlineData("x-custom://open", HtmlNavigationTarget.MainFrame, true, null, HtmlNavigationDecision.Cancel)]
    [InlineData("data:text/html,hi", HtmlNavigationTarget.MainFrame, false, null, HtmlNavigationDecision.Cancel)]
    public void AFilePageStaysInsideItsGrant(string url, HtmlNavigationTarget target, bool userActivated, string? grant,
                                             HtmlNavigationDecision decision)
    {
        var page = new HtmlOverlay(new HtmlSource.File(@"C:\tmp\a\report.html", grant));
        var action = new HtmlNavigationAction(new Uri(url), target, userActivated);
        Assert.Equal(decision, HtmlNavigationPolicy.Decide(action, page));
    }

    [Theory]
    [InlineData("http://localhost:5173/about", HtmlNavigationTarget.MainFrame, false, HtmlNavigationDecision.Allow)]
    [InlineData("http://LOCALHOST:5173/", HtmlNavigationTarget.MainFrame, true, HtmlNavigationDecision.Allow)]
    [InlineData("http://localhost:5174/", HtmlNavigationTarget.MainFrame, false, HtmlNavigationDecision.Cancel)]
    [InlineData("http://localhost:5174/", HtmlNavigationTarget.MainFrame, true, HtmlNavigationDecision.OpenExternal)]
    [InlineData("https://localhost:5173/", HtmlNavigationTarget.MainFrame, false, HtmlNavigationDecision.Cancel)]
    [InlineData("https://example.com/", HtmlNavigationTarget.MainFrame, true, HtmlNavigationDecision.OpenExternal)]
    [InlineData("https://example.com/", HtmlNavigationTarget.MainFrame, false, HtmlNavigationDecision.Cancel)]
    [InlineData("https://example.com/embed", HtmlNavigationTarget.Subframe, false, HtmlNavigationDecision.Allow)]
    [InlineData("http://localhost:5173/", HtmlNavigationTarget.NewWindow, true, HtmlNavigationDecision.OpenExternal)]
    [InlineData("http://localhost:5173/", HtmlNavigationTarget.NewWindow, false, HtmlNavigationDecision.Cancel)]
    [InlineData("about:blank", HtmlNavigationTarget.MainFrame, false, HtmlNavigationDecision.Allow)]
    [InlineData("about:srcdoc", HtmlNavigationTarget.Subframe, false, HtmlNavigationDecision.Allow)]
    [InlineData("file:///C:/tmp/a/report.html", HtmlNavigationTarget.MainFrame, true, HtmlNavigationDecision.Cancel)]
    [InlineData("file:///C:/tmp/a/report.html", HtmlNavigationTarget.Subframe, false, HtmlNavigationDecision.Cancel)]
    [InlineData("mailto:a@example.com", HtmlNavigationTarget.MainFrame, true, HtmlNavigationDecision.Cancel)]
    public void AUrlPageStaysOnItsOrigin(string url, HtmlNavigationTarget target, bool userActivated,
                                         HtmlNavigationDecision decision)
    {
        var page = new HtmlOverlay(new HtmlSource.Url(new Uri("http://localhost:5173/")));
        var action = new HtmlNavigationAction(new Uri(url), target, userActivated);
        Assert.Equal(decision, HtmlNavigationPolicy.Decide(action, page));
    }

    [Fact]
    public void AChromelessUrlPageKeepsItsFramesOnItsOrigin()
    {
        var page = new HtmlOverlay(new HtmlSource.Url(new Uri("http://localhost:5173/"))) { Chromeless = true };
        var action = new HtmlNavigationAction(new Uri("https://example.com/embed"), HtmlNavigationTarget.Subframe, false);
        Assert.Equal(HtmlNavigationDecision.Cancel, HtmlNavigationPolicy.Decide(action, page));
    }

    [Fact]
    public void TheSchemeIsMatchedWhateverItsCase()
    {
        var page = new HtmlOverlay(new HtmlSource.File(@"C:\tmp\a\report.html", null));
        var action = new HtmlNavigationAction(new Uri("HTTPS://example.com/"), HtmlNavigationTarget.MainFrame, true);
        Assert.Equal(HtmlNavigationDecision.OpenExternal, HtmlNavigationPolicy.Decide(action, page));
    }
}
