using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// The visible predicates subtract the hidden bit (upstream Session+OverlayVisibility.swift).
public class SessionOverlayVisibilityTests
{
    private static Session WithOverlay(bool hidden, int? sizePercent = null)
    {
        var session = new Session("/tmp")
        {
            Surface = new SpySurface("main"),
            OverlayActive = true,
            OverlaySurface = new SpySurface("overlay"),
            OverlaySizePercent = sizePercent,
        };
        session.OverlayHidden = hidden;
        return session;
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, 60)]
    [InlineData(true, 60)]
    public void VisiblePredicatesSubtractTheHiddenBit(bool hidden, int? sizePercent)
    {
        var session = WithOverlay(hidden, sizePercent);

        Assert.Equal(!hidden, session.OverlayVisible);
        Assert.Equal(session.FullOverlayActive && !hidden, session.FullOverlayVisible);
        Assert.Equal(session.FloatingOverlayActive && !hidden, session.FloatingOverlayVisible);
    }

    [Fact]
    public void AnEmptySlotIsNeitherVisibleNorShown()
    {
        var session = new Session("/tmp");

        Assert.False(session.OverlayVisible);
        Assert.False(session.AnyOverlayShown(null));
        Assert.Empty(session.HiddenOverlayNames);
    }

    [Fact]
    public void AHiddenOverlayGivesTheKeyboardBackToThePane()
    {
        var session = WithOverlay(hidden: false);
        Assert.Same(session.OverlaySurface, session.TopmostSurface);

        session.OverlayHidden = true;

        Assert.Same(session.Surface, session.TopmostSurface);
    }

    [Fact]
    public void AHiddenOverlayGivesTheKeyboardToAShownScratch()
    {
        var session = WithOverlay(hidden: true);
        session.ScratchActive = true;
        session.ScratchSurface = new SpySurface("scratch");

        Assert.Same(session.ScratchSurface, session.TopmostSurface);
        Assert.Same(session.ScratchSurface, session.OnScreenSurface);
    }

    [Fact]
    public void HiddenOverlayNamesListTheSessionSlot()
    {
        Assert.Equal(["session"], WithOverlay(hidden: true).HiddenOverlayNames);
        Assert.Empty(WithOverlay(hidden: false).HiddenOverlayNames);
    }
}
