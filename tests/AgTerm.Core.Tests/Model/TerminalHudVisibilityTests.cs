using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// Which HUD the window draws: after upstream hudCanMount (ed831ab, WindowContentView+Detail.swift).
public class TerminalHudVisibilityTests
{
    private static readonly HudPanelSize Unmeasured = new(HudLayout.MaxSizePercent, 10);

    private static (AppStore Store, Session Session) Selected()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        session.Surface = new SpySurface("left-token");
        store.ToggleSplit(session.Id);
        session.SplitSurface = new SpySurface("right-token");
        store.SelectSession(session.Id);
        return (store, session);
    }

    private static TerminalHudPlacement? Shown(AppStore store) => TerminalHudVisibility.For(store, false, false, false);

    [Fact]
    public void ASessionWideHudOfTheSelectedSessionIsShown()
    {
        var (store, session) = Selected();
        var spec = new HudSpec("building");
        Assert.True(store.OpenHud(session.Id, spec, null));

        var placement = Shown(store)!;

        Assert.Equal(session.Id, placement.SessionId);
        Assert.Equal(spec, placement.Spec);
        Assert.Equal(Unmeasured, placement.Size);
        Assert.Null(placement.Pane);
        Assert.Null(placement.PaneId);
        Assert.Equal(session.HudRevision, placement.Revision);
        Assert.Equal(session.HudFontSize, placement.FontSize);
        Assert.Equal(placement, Shown(store));
    }

    // the panel's font is fixed at open, as upstream reads it once when the panel is created
    [Fact]
    public void TheFontFixedAtOpenSurvivesAnUpdate()
    {
        var (store, session) = Selected();
        store.OpenHud(session.Id, new HudSpec("building", FontSize: 18), null);
        Assert.Equal(18, Shown(store)!.FontSize);

        store.UpdateHud(session.Id, new HudSpec("built"), null);
        Assert.Equal(18, Shown(store)!.FontSize);
    }

    [Fact]
    public void APaneHudCarriesThePagePaneId()
    {
        var (store, session) = Selected();
        Assert.True(store.OpenHud(session.Id, new HudSpec("building"), session.SplitPaneIdentity));

        var placement = Shown(store)!;

        Assert.Equal(OverlayPane.Right, placement.Pane);
        Assert.Equal("right-token", placement.PaneId);
    }

    [Fact]
    public void WindowCoversHideTheHudButTheDetachedQuickTerminalDoesNot()
    {
        var (store, session) = Selected();
        store.OpenHud(session.Id, new HudSpec("building"), null);

        Assert.Null(TerminalHudVisibility.For(store, zoomActive: true, dashboardOpen: false, quickTerminalVisible: false));
        Assert.Null(TerminalHudVisibility.For(store, zoomActive: false, dashboardOpen: true, quickTerminalVisible: false));
        Assert.NotNull(TerminalHudVisibility.For(store, zoomActive: false, dashboardOpen: false, quickTerminalVisible: true));
        Assert.True(session.HudActive);
    }

    [Fact]
    public void NoHudShowsNothing()
    {
        var (store, _) = Selected();
        Assert.Null(Shown(store));
    }

    [Fact]
    public void OnlyTheSelectedSessionsHudIsShown()
    {
        var (store, session) = Selected();
        var other = store.AddSession(store.Workspaces[0].Id, "/tmp")!;
        store.OpenHud(session.Id, new HudSpec("building"), null);

        store.SelectSession(other.Id);

        Assert.Null(Shown(store));
        Assert.True(session.HudActive);
        store.SelectSession(session.Id);
        Assert.NotNull(Shown(store));
    }

    [Fact]
    public void APaneHudHidesWhileItsPaneIsNotLaidOutAndComesBack()
    {
        var (store, session) = Selected();
        store.OpenHud(session.Id, new HudSpec("building"), session.SplitPaneIdentity);
        session.SplitFocused = false;
        store.ToggleSplit(session.Id);

        Assert.Null(Shown(store));
        Assert.True(session.HudActive);

        // review focus 3: an update while hidden is kept for the pane's return
        Assert.True(store.UpdateHud(session.Id, new HudSpec("still building"), session.SplitPaneIdentity));
        Assert.Null(Shown(store));

        store.ToggleSplit(session.Id);
        var shown = Shown(store)!;
        Assert.Equal("right-token", shown.PaneId);
        Assert.Equal("still building", shown.Spec.Message);
    }

    [Fact]
    public void AHiddenSplitShowingTheRightPaneShowsItsHudAndHidesTheLeftOne()
    {
        var (store, session) = Selected();
        session.SplitFocused = true;
        store.ToggleSplit(session.Id);

        store.OpenHud(session.Id, new HudSpec("right"), session.SplitPaneIdentity);
        Assert.Equal("right-token", Shown(store)!.PaneId);

        store.UpdateHud(session.Id, new HudSpec("left"), session.PaneIdentity);
        Assert.Null(Shown(store));
    }

    [Fact]
    public void APaneWithoutAPageTokenShowsNothing()
    {
        var (store, session) = Selected();
        store.OpenHud(session.Id, new HudSpec("building"), session.PaneIdentity);

        session.Surface = new SpySurface("");
        Assert.Null(Shown(store));
        session.Surface = null;
        Assert.Null(Shown(store));
    }

    // upstream draws the HUD layer above the scratch, so a HUD stays on screen while the scratch is up
    [Fact]
    public void TheHudDrawsOverTheScratch()
    {
        var (store, session) = Selected();
        session.ScratchActive = true;

        store.OpenHud(session.Id, new HudSpec("session"), null);
        Assert.NotNull(Shown(store));

        store.UpdateHud(session.Id, new HudSpec("pane"), session.SplitPaneIdentity);
        Assert.Equal("right-token", Shown(store)!.PaneId);
    }

    [Fact]
    public void TheForcedWidthIsTheDrawnWidthUntilTheNextUpdate()
    {
        var (store, session) = Selected();
        store.OpenHud(session.Id, new HudSpec("building"), null);

        Assert.True(store.ResizeHud(session.Id, 35));
        Assert.Equal(35, Shown(store)!.Size.WidthPercent);

        store.UpdateHud(session.Id, new HudSpec("built"), null);
        Assert.Equal(HudLayout.MaxSizePercent, Shown(store)!.Size.WidthPercent);
    }

    [Fact]
    public void TheRevisionTellsANewSpecFromARedraw()
    {
        var (store, session) = Selected();
        store.OpenHud(session.Id, new HudSpec("building"), null);
        var opened = Shown(store)!.Revision;

        store.ResizeHud(session.Id, 35);
        Assert.Equal(opened, Shown(store)!.Revision);

        store.UpdateHud(session.Id, new HudSpec("built"), null);
        Assert.NotEqual(opened, Shown(store)!.Revision);
    }

    [Fact]
    public void AClosedHudShowsNothing()
    {
        var (store, session) = Selected();
        store.OpenHud(session.Id, new HudSpec("building"), null);
        store.CloseHud(session.Id);
        Assert.Null(Shown(store));
    }

    // the panel's own font size measures it, as AppStore does when it sizes the HUD
    [Fact]
    public void APlacementCarriesThePaneMetricsAtTheHudsFontSize()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        session.Surface = new SpySurface("left-token");
        store.SelectSession(session.Id);
        var metrics = new HudSessionMetrics(14, 8, 18, new HudRect(1000, 800), new HudRect(1000, 800), null, 14);
        store.SetHudMetrics(session.Id, metrics);
        store.OpenHud(session.Id, new HudSpec("**hi**", Markdown: true, FontSize: 28), null);

        var placement = TerminalHudVisibility.For(store, false, false, false)!;

        Assert.Equal(metrics.For(null, 28), placement.Metrics);
        Assert.Equal(16, placement.Metrics!.CellWidth);
    }

    [Fact]
    public void APlacementOfAnUnmeasuredSessionHasNoMetrics()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        session.Surface = new SpySurface("left-token");
        store.SelectSession(session.Id);
        store.OpenHud(session.Id, new HudSpec("hi"), null);

        Assert.Null(TerminalHudVisibility.For(store, false, false, false)!.Metrics);
    }
}
