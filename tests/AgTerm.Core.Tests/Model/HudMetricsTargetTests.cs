using AgTerm.Core.Model;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

public class HudMetricsTargetTests
{
    private static readonly HudRect Area = new(1000, 600);

    private static HudSessionMetrics Single() => new(14, 8, 17, Area, null, null);

    private static HudSessionMetrics Split() =>
        new(14, 8, 17, Area, new HudRect(498, 600), new HudRect(498, 600));

    private static (AppStore Store, Session Session) SplitSession()
    {
        var store = AppStoreFixtures.MakeStore();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/tmp")!;
        session.Surface = new SpySurface("left-token");
        store.ToggleSplit(session.Id);
        session.SplitSurface = new SpySurface("right-token");
        return (store, session);
    }

    [Fact]
    public void ASinglePaneNamedByTheMainTokenIsTheLeftPane()
    {
        var (store, session) = SplitSession();

        HudMetricsTarget.Apply(store, "left-token", Single(), Area);

        Assert.Equal(Single() with { Left = Area }, session.HudMetrics);
    }

    // a hidden split with the right pane focused shows that pane alone
    [Fact]
    public void ASinglePaneNamedByTheSplitTokenIsTheRightPane()
    {
        var (store, session) = SplitSession();
        store.ToggleSplit(session.Id);
        Assert.True(session.SplitFocused);

        HudMetricsTarget.Apply(store, "right-token", Single(), Area);

        Assert.Equal(Single() with { Right = Area }, session.HudMetrics);
    }

    [Fact]
    public void ASplitReportNamedByTheMainTokenIsStoredAsItCame()
    {
        var (store, session) = SplitSession();

        HudMetricsTarget.Apply(store, "left-token", Split(), null);

        Assert.Equal(Split(), session.HudMetrics);
    }

    // a shown split is laid out main pane first, so a split report under the split token is malformed
    [Fact]
    public void ASplitReportNamedByTheSplitTokenIsDropped()
    {
        var (store, session) = SplitSession();

        Assert.False(HudMetricsTarget.Apply(store, "right-token", Split(), null));

        Assert.Null(session.HudMetrics);
    }

    // the Task 5 rule: a pane the page no longer reports keeps its last rect
    [Fact]
    public void HidingTheSplitKeepsTheLeftRectTheSplitReported()
    {
        var (store, session) = SplitSession();
        HudMetricsTarget.Apply(store, "left-token", Split(), null);
        store.ToggleSplit(session.Id);

        HudMetricsTarget.Apply(store, "right-token", Single(), Area);

        Assert.Equal(new HudRect(498, 600), session.HudMetrics!.Left);
        Assert.Equal(Area, session.HudMetrics.Right);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-token")]
    public void AnUnknownOrBlankTokenChangesNothing(string token)
    {
        var (store, session) = SplitSession();

        Assert.False(HudMetricsTarget.Apply(store, token, Single(), Area));

        Assert.Null(session.HudMetrics);
    }

    [Fact]
    public void TheAnswerSaysWhetherTheHudSizeChanged()
    {
        var (store, session) = SplitSession();
        Assert.True(store.OpenHud(session.Id, new HudSpec("building"), null));

        Assert.True(HudMetricsTarget.Apply(store, "left-token", Split(), null));
        Assert.False(HudMetricsTarget.Apply(store, "left-token", Split(), null));
    }
}
