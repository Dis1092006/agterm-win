using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// Where focus, coverage or input is decided, a hidden overlay counts as absent (upstream 7f450d17,
// TerminalZoom.swift and AppActions.swift).
public class OverlayVisibilitySitesTests
{
    private static (AppStore Store, Session Session) WithOverlay(bool hidden)
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        session.Surface = new SpySurface("left-token");
        store.SelectSession(session.Id);
        Assert.True(store.OpenOverlay(session.Id, "less"));
        session.OverlaySurface = new SpySurface("overlay-token");
        if (hidden) Assert.Null(store.SetOverlaysHidden(session.Id, null, hidden: true));
        return (store, session);
    }

    [Fact]
    public void AShownOverlayIsTheZoomTarget()
    {
        var (store, session) = WithOverlay(hidden: false);

        Assert.Equal(new TerminalZoomTarget.Session(session.Id, TerminalZoomSurface.Overlay),
                     TerminalZoomController.ResolveTarget(store, quickTerminalVisible: false));
    }

    [Fact]
    public void AHiddenOverlayIsNeitherTheZoomTargetNorAvailable()
    {
        var (store, session) = WithOverlay(hidden: true);

        Assert.Equal(new TerminalZoomTarget.Session(session.Id, TerminalZoomSurface.Primary),
                     TerminalZoomController.ResolveTarget(store, quickTerminalVisible: false));
        Assert.False(TerminalZoomController.IsTargetValid(
            new TerminalZoomTarget.Session(session.Id, TerminalZoomSurface.Overlay), store, quickTerminalVisible: false));
        Assert.False(TerminalZoomSurface.Overlay.IsActive(session));
        Assert.False(TerminalZoomSurface.Overlay.IsVisible(session));
        Assert.True(TerminalZoomSurface.Primary.IsActive(session));
        Assert.True(TerminalZoomSurface.Primary.IsVisible(session));
    }

    [Fact]
    public void AHiddenOverlayLeavesAShownScratchActiveAndVisible()
    {
        var (store, session) = WithOverlay(hidden: true);
        store.ToggleScratch(session.Id);
        session.ScratchSurface = new SpySurface("scratch-token");

        Assert.True(TerminalZoomSurface.Scratch.IsActive(session));
        Assert.True(TerminalZoomSurface.Scratch.IsVisible(session));
        Assert.Equal(new TerminalZoomTarget.Session(session.Id, TerminalZoomSurface.Scratch),
                     TerminalZoomController.ResolveTarget(store, quickTerminalVisible: false));
    }

    [Fact]
    public void AShownFullOverlayBlocksSearch()
    {
        var (store, session) = WithOverlay(hidden: false);

        Assert.False(store.OpenSearch(session.Id));
    }

    [Fact]
    public void SearchOpensOnThePaneUnderAHiddenFullOverlay()
    {
        var (store, session) = WithOverlay(hidden: true);

        Assert.True(store.OpenSearch(session.Id));
        Assert.Same(session.Surface, session.SearchSurface);
    }
}
