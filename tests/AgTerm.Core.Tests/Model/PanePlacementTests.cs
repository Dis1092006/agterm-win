using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// Ported from agterm ControlServerAskTests.swift placement cases (testPaneTokenOverridesRole...,
// testHiddenPaneRejects..., testUnknownAndScratchTokens..., testUnresolvedTokenUses...) (upstream ed831ab).
public class PanePlacementTests
{
    private static (AppStore Store, Session Session) SplitSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        session.Surface = new SpySurface("left-token");
        store.ToggleSplit(session.Id);
        session.SplitSurface = new SpySurface("right-token");
        session.ScratchSurface = new SpySurface("scratch-token");
        return (store, session);
    }

    private static (PanePlacementResult? Placement, string? Error) Resolve(Session session, OverlayPane? pane, string? paneId, bool requireVisible = true)
    {
        var ok = PanePlacement.TryResolve(pane, paneId, session, requireVisible, PanePlacement.AskInvalidPaneError, out var placement, out var error);
        return ok ? (placement, null) : (null, error);
    }

    [Fact]
    public void NoSelectorIsSessionWide() =>
        Assert.Equal((PanePlacementResult.SessionWide, (string?)null), Resolve(SplitSession().Session, null, null));

    [Fact]
    public void ALiveTokenOverridesTheRole()
    {
        var (_, session) = SplitSession();
        Assert.Equal(new PanePlacementResult(OverlayPane.Right, session.SplitPaneIdentity), Resolve(session, OverlayPane.Left, "right-token").Placement);
    }

    [Fact]
    public void AnUnresolvedTokenFallsBackToTheRole()
    {
        var (_, session) = SplitSession();
        Assert.Equal(new PanePlacementResult(OverlayPane.Left, session.PaneIdentity), Resolve(session, OverlayPane.Left, "old").Placement);
    }

    [Fact]
    public void AnEmptyTokenCountsAsAbsent()
    {
        var (_, session) = SplitSession();
        Assert.Equal(PanePlacementResult.SessionWide, Resolve(session, null, "").Placement);
    }

    [Fact]
    public void AnUnknownTokenWithoutARoleAndAScratchTokenAreRefused()
    {
        var (_, session) = SplitSession();
        Assert.Equal("unknown pane id: missing", Resolve(session, null, "missing").Error);
        Assert.Equal("ask pane must be left or right", Resolve(session, OverlayPane.Left, "scratch-token").Error);
    }

    [Fact]
    public void AHiddenPaneIsRefusedButAVisibleMaximizedPaneResolves()
    {
        var (_, session) = SplitSession();
        session.IsSplit = false;
        session.SplitFocused = false;
        Assert.Equal("pane not visible", Resolve(session, OverlayPane.Right, null).Error);

        session.SplitFocused = true;
        Assert.Equal(new PanePlacementResult(OverlayPane.Right, session.SplitPaneIdentity), Resolve(session, OverlayPane.Right, null).Placement);
    }

    [Fact]
    public void TheRightPaneWithoutASplitIsNotVisibleOrHasNoSplit()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        session.SplitFocused = true;
        Assert.Equal("pane not visible", Resolve(session, OverlayPane.Right, null).Error);
        Assert.Equal("session has no split", Resolve(session, OverlayPane.Right, null, requireVisible: false).Error);
    }

    [Fact]
    public void AnAnchorIsShownWhileItsSessionIsSelectedAndItsPaneIsLaidOut()
    {
        var (store, session) = SplitSession();
        var other = store.AddSession(store.Workspaces[0].Id, "/tmp")!;
        store.SelectSession(session.Id);
        var sessionWide = new AskAnchor(session.Id);
        var right = new AskAnchor(session.Id, OverlayPane.Right, session.SplitPaneIdentity);

        Assert.True(AskAnchors.IsShown(store, sessionWide));
        Assert.True(AskAnchors.IsShown(store, right));

        session.SplitFocused = false;
        store.ToggleSplit(session.Id);
        Assert.False(AskAnchors.IsShown(store, right));
        Assert.True(AskAnchors.IsShown(store, sessionWide));

        store.ToggleSplit(session.Id);
        store.SelectSession(other.Id);
        Assert.False(AskAnchors.IsShown(store, sessionWide));
    }

    [Fact]
    public void AnAnchorIsLostWhenItsPaneChangesRole()
    {
        var (store, session) = SplitSession();
        store.SelectSession(session.Id);
        var right = new AskAnchor(session.Id, OverlayPane.Right, session.SplitPaneIdentity);
        var left = new AskAnchor(session.Id, OverlayPane.Left, session.PaneIdentity);

        store.ClosePrimaryPane(session.Id);

        Assert.False(AskAnchors.IsShown(store, right));
        Assert.False(AskAnchors.IsShown(store, left));
    }
}
