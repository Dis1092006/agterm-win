using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

// The store changes a page under the host gate; the app's UI thread works on a copy.
public class HtmlOverlayCopyTests
{
    [Fact]
    public void ACopyCarriesEveryFieldAndStandsApart()
    {
        var page = new HtmlOverlay(new HtmlSource.File(@"C:\a\r.html", @"C:\a"))
        {
            Navigation = true,
            Javascript = true,
            Chromeless = true,
            Persistent = true,
            Browse = true,
            LoadState = HtmlLoadState.Failed,
            LoadError = "boom",
            Current = new HtmlPageInfo(@"C:\a\b.html", "B", true, false),
            ReloadRevision = 3,
            ReloadTarget = HtmlReloadTarget.Current,
        };

        var copy = page.Copy();
        page.ReloadRevision = 4;
        page.Navigation = false;

        Assert.NotSame(page, copy);
        Assert.Equal(page.Id, copy.Id);
        Assert.Equal(page.Source, copy.Source);
        Assert.True(copy.Navigation);
        Assert.True(copy.Javascript);
        Assert.True(copy.Chromeless);
        Assert.True(copy.Persistent);
        Assert.True(copy.Browse);
        Assert.Equal(HtmlLoadState.Failed, copy.LoadState);
        Assert.Equal("boom", copy.LoadError);
        Assert.Equal(page.Current, copy.Current);
        Assert.Equal(3, copy.ReloadRevision);
        Assert.Equal(HtmlReloadTarget.Current, copy.ReloadTarget);
    }
}
