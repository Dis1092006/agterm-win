using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// A page in the overlay slot covers the session without being a terminal (upstream 11fe64eb,
// Session+HtmlOverlay.swift and TerminalZoom.swift).
public class SessionHtmlOverlayTests
{
    private static (AppStore Store, Session Session) WithPage(bool hidden = false)
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        session.Surface = new SpySurface("left-token");
        store.SelectSession(session.Id);
        Assert.Null(store.OpenHtmlOverlay(session.Id, new HtmlOverlay(new HtmlSource.File(@"C:\work\r.html", null)), sizePercent: null));
        if (hidden) Assert.Null(store.SetOverlaysHidden(session.Id, null, hidden: true));
        return (store, session);
    }

    [Fact]
    public void APageIsACoverButNotAProgram()
    {
        var (_, session) = WithPage();

        Assert.True(session.HtmlOverlayActive);
        Assert.True(session.HtmlOverlayVisible);
        Assert.False(session.ProgramOverlayActive);
        Assert.False(session.ProgramOverlayVisible);
        Assert.True(session.CoverOverlayActive);
        Assert.True(session.HtmlCovers(null));
        Assert.False(session.HtmlCovers(OverlayPane.Left));
    }

    [Fact]
    public void AProgramIsACoverButNotAPage()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        Assert.True(store.OpenOverlay(session.Id, "htop"));

        Assert.False(session.HtmlOverlayActive);
        Assert.False(session.HtmlCovers(null));
        Assert.True(session.ProgramOverlayActive);
        Assert.True(session.ProgramOverlayVisible);
        Assert.True(session.CoverOverlayActive);
    }

    [Fact]
    public void AHiddenPageIsActiveButNotVisible()
    {
        var (_, session) = WithPage(hidden: true);

        Assert.True(session.HtmlOverlayActive);
        Assert.False(session.HtmlOverlayVisible);
    }

    [Fact]
    public void NoTerminalIsOnTopOfAShownPage()
    {
        var (_, session) = WithPage();
        session.ScratchActive = true;
        session.ScratchSurface = new SpySurface("scratch-token");

        Assert.Null(session.TopmostSurface);
    }

    [Fact]
    public void APageIsNothingToZoom()
    {
        var (store, session) = WithPage();

        Assert.Null(TerminalZoomController.ResolveTarget(store, quickTerminalVisible: false));
        Assert.False(TerminalZoomSurface.Overlay.IsAvailable(session));
        Assert.False(TerminalZoomSurface.Overlay.IsActive(session));
        Assert.False(TerminalZoomSurface.Overlay.IsVisible(session));
        Assert.False(TerminalZoomSurface.Primary.IsActive(session));
    }

    [Fact]
    public void AHiddenPageLeavesThePaneTheZoomTarget()
    {
        var (store, session) = WithPage(hidden: true);

        Assert.Equal(new TerminalZoomTarget.Session(session.Id, TerminalZoomSurface.Primary),
                     TerminalZoomController.ResolveTarget(store, quickTerminalVisible: false));
    }
}
