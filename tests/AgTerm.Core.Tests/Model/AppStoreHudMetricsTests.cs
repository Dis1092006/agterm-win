using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// The geometry half of ControlServerHudAutoHideTests.swift and the measured cases of AppStoreHudTests.swift
// (upstream ed831ab), on the port's metrics from the page. Numbers: cell 8x18 at 12 pt, panel padding
// 8 and 6 px; "working" is a box of 11 columns and 3 rows, 104 x 66 px.
public class AppStoreHudMetricsTests
{
    private static HudSessionMetrics Metrics(double sessionHeight = 640, double fontSize = 12, bool right = true,
                                             double? baseFontSize = null) =>
        new(fontSize, CellWidth: 8, CellHeight: 18, Session: new HudRect(1000, sessionHeight),
            Left: new HudRect(500, 640), Right: right ? new HudRect(480, 320) : null,
            BaseFontSize: baseFontSize ?? fontSize);

    private static (AppStore Store, Session Session) NewSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/repo")!;
        return (store, session);
    }

    private static (AppStore Store, Session Session) SplitSession()
    {
        var (store, session) = NewSession();
        store.ToggleSplit(session.Id);
        return (store, session);
    }

    [Fact]
    public void ForPicksTheRectAndAddsThePanelPadding()
    {
        var metrics = Metrics();

        Assert.Equal(new PaneMetrics(8, 18, 1000, 640, 8, 6), metrics.For(null, 12));
        Assert.Equal(new PaneMetrics(8, 18, 500, 640, 8, 6), metrics.For(OverlayPane.Left, 12));
        Assert.Equal(new PaneMetrics(8, 18, 480, 320, 8, 6), metrics.For(OverlayPane.Right, 12));
    }

    [Fact]
    public void ForScalesTheCellWithTheFontSize()
    {
        Assert.Equal(new PaneMetrics(16, 36, 1000, 640, 8, 6), Metrics().For(null, 24));
    }

    [Fact]
    public void ForAPaneWithNoRectIsUnmeasured()
    {
        Assert.Equal(PaneMetrics.Unmeasured, Metrics(right: false).For(OverlayPane.Right, 12));
    }

    [Fact]
    public void ForAProbeWithNoFontSizeKeepsTheCell()
    {
        Assert.Equal(new PaneMetrics(8, 18, 1000, 640, 8, 6), Metrics(fontSize: 0).For(null, 24));
    }

    [Fact]
    public void ForAProbeWithAnInfiniteFontSizeKeepsTheCell()
    {
        Assert.Equal(new PaneMetrics(8, 18, 1000, 640, 8, 6), Metrics(fontSize: double.PositiveInfinity).For(null, 24));
    }

    [Fact]
    public void MetricsWithNoHudAreOnlyStoredAndTheNextOpenMeasuresAgainstThem()
    {
        var (store, session) = NewSession();

        Assert.False(store.SetHudMetrics(session.Id, Metrics()));
        Assert.Equal(Metrics(), session.HudMetrics);

        store.OpenHud(session.Id, new HudSpec("working"), null);

        Assert.Equal(new HudPanelSize(11, 11, 66), session.HudPanelSize);
    }

    [Fact]
    public void APaneHudMeasuresAgainstItsPane()
    {
        var (store, session) = SplitSession();
        store.SetHudMetrics(session.Id, Metrics());

        store.OpenHud(session.Id, new HudSpec("working"), session.PaneIdentity);
        Assert.Equal(new HudPanelSize(21, 11, 66), session.HudPanelSize);

        store.OpenHud(session.Id, new HudSpec("working"), session.SplitPaneIdentity);
        Assert.Equal(new HudPanelSize(22, 21, 66), session.HudPanelSize);
    }

    [Fact]
    public void TheHudsOwnFontWinsThenTheSessionsThenTheBaseFont()
    {
        var (store, session) = NewSession();
        store.SetHudMetrics(session.Id, Metrics());

        store.OpenHud(session.Id, new HudSpec("working", FontSize: 24), null);
        Assert.Equal(new HudPanelSize(20, 19, 120), session.HudPanelSize);

        session.FontSize = 18;
        store.OpenHud(session.Id, new HudSpec("working"), null);
        Assert.Equal(new HudPanelSize(15, 15, 93), session.HudPanelSize);

        session.FontSize = null;
        store.OpenHud(session.Id, new HudSpec("working"), null);
        Assert.Equal(12, session.HudFontSize);
        Assert.Equal(new HudPanelSize(11, 11, 66), session.HudPanelSize);
    }

    [Fact]
    public void ASessionZoomAfterOpenDoesNotResizeTheHud()
    {
        var (store, session) = NewSession();
        session.FontSize = 13;
        // a 6.5 x 13 cell at 13 pt is 10 x 20 at 20 pt, and 13 / 20 scales it back with no rounding
        store.SetHudMetrics(session.Id, new HudSessionMetrics(13, 6.5, 13, new HudRect(1000, 640), null, null, 14));
        store.OpenHud(session.Id, new HudSpec("working"), null);
        var opened = session.HudPanelSize;
        Assert.Equal(new HudPanelSize(10, 8, 51), opened);

        session.FontSize = 20;
        Assert.False(store.SetHudMetrics(session.Id, new HudSessionMetrics(20, 10, 20, new HudRect(1000, 640), null, null, 14)));

        Assert.Equal(13, session.HudFontSize);
        Assert.Equal(opened, session.HudPanelSize);
        store.UpdateHud(session.Id, new HudSpec("working"), null);
        Assert.Equal(opened, session.HudPanelSize);
    }

    // While a panel is drawn the page probes the cell at the panel's font and posts nothing when it
    // closes, so the probe font is the last panel's font. A 20 x 45 cell at 30 pt is the 8 x 18 cell
    // of Metrics() at 12 pt, scaled.
    private static HudSessionMetrics ProbedUnderA30PointPanel() =>
        new(30, 20, 45, new HudRect(1000, 640), null, null, BaseFontSize: 13);

    private static readonly HudPanelSize WorkingAt13 =
        HudLayout.PanelSize(new HudSpec("working"), ProbedUnderA30PointPanel().For(null, 13));

    [Fact]
    public void AHudWithNoFontAfterAClosedHudWithItsOwnTakesTheBaseFont()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("working", FontSize: 30), null);
        store.SetHudMetrics(session.Id, ProbedUnderA30PointPanel());
        Assert.NotEqual(WorkingAt13, session.HudPanelSize);
        store.CloseHud(session.Id);

        store.OpenHud(session.Id, new HudSpec("working"), null);

        Assert.Equal(13, session.HudFontSize);
        Assert.Equal(WorkingAt13, session.HudPanelSize);
    }

    [Fact]
    public void AHudWithNoFontReplacingAHudWithItsOwnTakesTheBaseFont()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("working", FontSize: 30), null);
        store.SetHudMetrics(session.Id, ProbedUnderA30PointPanel());

        store.OpenHud(session.Id, new HudSpec("working"), null);

        Assert.Equal(13, session.HudFontSize);
        Assert.Equal(WorkingAt13, session.HudPanelSize);
    }

    // a session switch lays out the new session while the page still draws the other session's
    // panel, so the new session's first metrics carry that panel's probe font
    [Fact]
    public void AHudOpenedBeforeAnyMetricsTakesTheBaseFontOfMetricsProbedUnderAnotherSessionsPanel()
    {
        var (store, other) = NewSession();
        store.OpenHud(other.Id, new HudSpec("working", FontSize: 30), null);
        var session = store.AddSession(store.Workspaces[0].Id, "/repo")!;
        store.OpenHud(session.Id, new HudSpec("working"), null);

        store.SetHudMetrics(session.Id, ProbedUnderA30PointPanel());

        Assert.Equal(13, session.HudFontSize);
        Assert.Equal(WorkingAt13, session.HudPanelSize);
    }

    [Fact]
    public void AHudOpenedAfterMetricsProbedUnderAnotherSessionsPanelTakesTheBaseFont()
    {
        var (store, other) = NewSession();
        store.OpenHud(other.Id, new HudSpec("working", FontSize: 30), null);
        var session = store.AddSession(store.Workspaces[0].Id, "/repo")!;
        store.SetHudMetrics(session.Id, ProbedUnderA30PointPanel());

        store.OpenHud(session.Id, new HudSpec("working"), null);

        Assert.Equal(13, session.HudFontSize);
        Assert.Equal(WorkingAt13, session.HudPanelSize);
    }

    [Fact]
    public void AHudOpenedBeforeAnyFontIsKnownTakesTheFirstMeasuredOne()
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("working"), null);
        Assert.Null(session.HudFontSize);

        Assert.True(store.SetHudMetrics(session.Id, Metrics(fontSize: 15)));

        Assert.Equal(15, session.HudFontSize);
        Assert.Equal(new HudPanelSize(11, 11, 66), session.HudPanelSize);

        Assert.False(store.SetHudMetrics(session.Id, Metrics(fontSize: 30) with { CellWidth = 16, CellHeight = 36 }));
        Assert.Equal(15, session.HudFontSize);
        Assert.Equal(new HudPanelSize(11, 11, 66), session.HudPanelSize);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ABaseFontThatIsNotUsableDoesNotFixTheHudsFont(double baseFont)
    {
        var (store, session) = NewSession();
        store.OpenHud(session.Id, new HudSpec("working"), null);

        store.SetHudMetrics(session.Id, Metrics(baseFontSize: baseFont));
        Assert.Null(session.HudFontSize);

        store.SetHudMetrics(session.Id, Metrics());
        Assert.Equal(12, session.HudFontSize);
        Assert.Equal(new HudPanelSize(11, 11, 66), session.HudPanelSize);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void AnOpenOverAStoredBaseFontThatIsNotUsableLeavesTheFontOpen(double baseFont)
    {
        var (store, session) = NewSession();
        store.SetHudMetrics(session.Id, Metrics(baseFontSize: baseFont));

        store.OpenHud(session.Id, new HudSpec("working"), null);
        Assert.Null(session.HudFontSize);

        store.SetHudMetrics(session.Id, Metrics());
        Assert.Equal(12, session.HudFontSize);
        Assert.Equal(new HudPanelSize(11, 11, 66), session.HudPanelSize);
    }

    [Fact]
    public void NewMetricsMeasureTheLiveHudAgainAndRaiseHudChangedOnlyOnAChange()
    {
        var (store, session) = NewSession();
        store.SetHudMetrics(session.Id, Metrics());
        store.OpenHud(session.Id, new HudSpec("working"), null);
        var changes = 0;
        store.HudChanged += () => changes++;

        Assert.False(store.SetHudMetrics(session.Id, Metrics()));
        Assert.Equal(0, changes);

        Assert.True(store.SetHudMetrics(session.Id, Metrics(sessionHeight: 320)));

        Assert.Equal(new HudPanelSize(11, 21, 66), session.HudPanelSize);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AReMeasureKeepsTheForcedWidthTheSpecTheScopeAndTheRevision()
    {
        var (store, session) = NewSession();
        store.SetHudMetrics(session.Id, Metrics());
        var spec = new HudSpec("working", HideAfter: 30);
        store.OpenHud(session.Id, spec, null);
        store.ResizeHud(session.Id, 50);
        var revision = session.HudRevision;

        Assert.True(store.SetHudMetrics(session.Id, Metrics(sessionHeight: 320)));

        Assert.Equal(new HudPanelSize(50, 21, 66), session.HudPanelSize);
        Assert.Equal(50, session.HudForcedWidthPercent);
        Assert.Equal(spec, session.Hud);
        Assert.Null(session.HudPaneIdentity);
        Assert.Equal(revision, session.HudRevision);

        store.UpdateHud(session.Id, new HudSpec("working"), null);

        Assert.Equal(new HudPanelSize(11, 21, 66), session.HudPanelSize);
    }

    [Fact]
    public void AHiddenPaneKeepsItsLastRectSoItsHudKeepsItsSize()
    {
        var (store, session) = SplitSession();
        store.SetHudMetrics(session.Id, Metrics());
        store.OpenHud(session.Id, new HudSpec("working"), session.SplitPaneIdentity);

        Assert.False(store.SetHudMetrics(session.Id, Metrics(right: false)));

        Assert.Equal(new HudPanelSize(22, 21, 66), session.HudPanelSize);
        Assert.Equal(new HudRect(480, 320), session.HudMetrics!.Right);
    }

    [Fact]
    public void ASessionNeverMeasuredGetsTheUnmeasuredSizes()
    {
        var (store, session) = NewSession();

        store.OpenHud(session.Id, new HudSpec("working"), null);
        Assert.Equal(new HudPanelSize(HudLayout.MaxSizePercent, 10), session.HudPanelSize);

        store.OpenHud(session.Id, new HudSpec("working", SizePercent: 35), null);
        Assert.Equal(new HudPanelSize(35, 10), session.HudPanelSize);

        store.OpenHud(session.Id, new HudSpec("working", SizePercent: 100, Position: HudPosition.TopCenter, Sticky: true), null);
        Assert.Equal(new HudPanelSize(100, 10), session.HudPanelSize);
    }

    [Fact]
    public void MetricsForAnUnknownSessionAreRefused()
    {
        var store = AppStoreFixtures.MakeStore();

        Assert.False(store.SetHudMetrics(Guid.NewGuid(), Metrics()));
    }

    private static HudSessionMetrics SessionWide(double width) =>
        new(14, 8, 18, new HudRect(width, 800), null, null, 14);

    // upstream rewrites a markdown body on every re-measure (WindowContentView+Detail.swift:358-367)
    [Fact]
    public void ANewGridRedrawsAMarkdownHudWhoseSizeStaysTheSame()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        store.SetHudMetrics(session.Id, SessionWide(1000));
        store.OpenHud(session.Id, new HudSpec("**ok**", SizePercent: 50, Markdown: true), null);
        var raised = 0;
        store.HudChanged += () => raised++;

        var sizeChanged = store.SetHudMetrics(session.Id, SessionWide(1200));

        Assert.False(sizeChanged);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ANewGridLeavesAPlainHudAlone()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        store.SetHudMetrics(session.Id, SessionWide(1000));
        store.OpenHud(session.Id, new HudSpec("ok", SizePercent: 50), null);
        var raised = 0;
        store.HudChanged += () => raised++;

        Assert.False(store.SetHudMetrics(session.Id, SessionWide(1200)));
        Assert.Equal(0, raised);
    }
}
