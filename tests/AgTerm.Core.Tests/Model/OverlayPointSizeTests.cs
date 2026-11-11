using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// A floating overlay sized in points (upstream 9bbb3408, #798), with a page's strip on top of its
// content (c63e5158). The port has no overlays shown on another Mac.
public class OverlayPointSizeTests : IDisposable
{
    private static readonly (double Width, double Height) Bounds = (1_000, 800);

    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-overlay-points-").FullName;
    private readonly AppStore _store;
    private readonly Session _session;

    public OverlayPointSizeTests()
    {
        _store = new AppStore(persistence: new PersistenceStore(_dir), stateDirectory: _dir);
        var workspaceId = _store.AddWorkspace("work").Id;
        _session = _store.AddSession(workspaceId, "/a")!;
    }

    public void Dispose()
    {
        _store.PaneFocusDebouncer.Cancel();
        Directory.Delete(_dir, recursive: true);
    }

    public static TheoryData<int?, int?, double, double> PanelSizes => new()
    {
        { null, null, 600, 480 },
        { null, 240, 600, 240 },
        { 300, null, 300, 480 },
        { 5_000, 5_000, 800, 640 },
    };

    [Theory]
    [MemberData(nameof(PanelSizes))]
    public void Panel_size_overrides_only_the_point_axes_and_caps_them(int? width, int? height,
                                                                        double expectedWidth, double expectedHeight)
    {
        var size = OverlayPointSize.PanelSize(Bounds, 60, new OverlayPointSize(width, height));

        Assert.Equal(expectedWidth, size.Width);
        Assert.Equal(expectedHeight, size.Height);
    }

    [Fact]
    public void A_percent_axis_keeps_the_full_percent_range()
    {
        var size = OverlayPointSize.PanelSize(Bounds, 95, new OverlayPointSize(Height: 100));

        Assert.Equal(950, size.Width);
    }

    // upstream c63e5158: a page's strip comes on top of a point height, and the cap applies after it
    [Fact]
    public void Chrome_adds_to_a_point_height_before_the_cap()
    {
        Assert.Equal((600, 269), OverlayPointSize.PanelSize(Bounds, 60, new OverlayPointSize(Height: 240), 29));
        Assert.Equal((600, 640), OverlayPointSize.PanelSize(Bounds, 60, new OverlayPointSize(Height: 630), 29));
        Assert.Equal((600, 480), OverlayPointSize.PanelSize(Bounds, 60, default, 29));
    }

    [Fact]
    public void Content_size_takes_the_chrome_off_the_panel()
    {
        Assert.Equal((600, 240), OverlayPointSize.ContentSize(Bounds, 60, new OverlayPointSize(Height: 240), 29));
        Assert.Equal((600, 451), OverlayPointSize.ContentSize(Bounds, 60, default, 29));
        Assert.Equal((10, 0), OverlayPointSize.ContentSize((10, 10), 100, default, 29));
    }

    [Fact]
    public void A_page_draws_its_strip_and_rule_as_chrome()
    {
        Assert.Equal(28, HtmlOverlay.StripHeight);
        Assert.Equal(29, HtmlOverlay.ChromeHeight);
        Assert.Equal(29, new HtmlOverlay(new HtmlSource.File("C:\\a.html", null)).Chrome);
        Assert.Equal(0, new HtmlOverlay(new HtmlSource.File("C:\\a.html", null)) { Chromeless = true }.Chrome);
    }

    [Fact]
    public void Tree_reports_a_pages_content_under_its_strip()
    {
        LayOut();
        Assert.Null(_store.OpenHtmlOverlay(_session.Id, new HtmlOverlay(new HtmlSource.File("C:\\a.html", null)), 50));

        var node = Node();

        Assert.Equal(500, node.OverlayWidth);
        Assert.Equal(371, node.OverlayHeight);
    }

    [Fact]
    public void Merging_replaces_only_the_axes_it_sets()
    {
        var merged = new OverlayPointSize(500, 200).Merging(new OverlayPointSize(Height: 300));

        Assert.Equal(new OverlayPointSize(500, 300), merged);
    }

    [Fact]
    public void Open_keeps_points_only_beside_a_percent()
    {
        _store.OpenOverlay(_session.Id, "htop", sizePercent: 60, points: new OverlayPointSize(Height: 240));
        Assert.Equal(new OverlayPointSize(Height: 240), _session.OverlayPoints);
        _store.CloseOverlay(_session.Id);
        Assert.True(_session.OverlayPoints.IsEmpty);

        _store.OpenOverlay(_session.Id, "htop", points: new OverlayPointSize(Height: 240));
        Assert.True(_session.OverlayPoints.IsEmpty);
    }

    [Fact]
    public void A_percent_resize_replaces_the_points_whole_and_full_clears_them()
    {
        _store.OpenOverlay(_session.Id, "htop", sizePercent: 60, points: new OverlayPointSize(500, 240));

        _store.ResizeOverlay(_session.Id, 70, new OverlayPointSize(Height: 300));
        Assert.Equal(70, _session.OverlaySizePercent);
        Assert.Equal(new OverlayPointSize(Height: 300), _session.OverlayPoints);

        _store.ResizeOverlay(_session.Id, 50);
        Assert.True(_session.OverlayPoints.IsEmpty);

        _store.ResizeOverlay(_session.Id, 50, new OverlayPointSize(Height: 300));
        _store.ResizeOverlay(_session.Id, null);
        Assert.True(_session.OverlayPoints.IsEmpty);
        Assert.True(_session.FullOverlayActive);
    }

    [Fact]
    public void Points_alone_change_their_axes_and_keep_the_rest()
    {
        _store.OpenOverlay(_session.Id, "htop", sizePercent: 60, points: new OverlayPointSize(Width: 500));

        Assert.Null(_store.ResizeOverlayInPoints(_session.Id, null, new OverlayPointSize(Height: 240)));

        Assert.Equal(60, _session.OverlaySizePercent);
        Assert.Equal(new OverlayPointSize(500, 240), _session.OverlayPoints);
    }

    [Fact]
    public void Points_alone_need_a_floating_overlay()
    {
        var points = new OverlayPointSize(Height: 240);
        Assert.Equal(OverlayPointResizeFailure.NoOverlay, _store.ResizeOverlayInPoints(_session.Id, null, points));
        Assert.Equal(OverlayPointResizeFailure.NoOverlay, _store.ResizeOverlayInPoints(Guid.NewGuid(), 60, points));

        _store.OpenOverlay(_session.Id, "htop");
        Assert.Equal(OverlayPointResizeFailure.NotFloating, _store.ResizeOverlayInPoints(_session.Id, null, points));
        Assert.Null(_store.ResizeOverlayInPoints(_session.Id, 60, points));
        Assert.Equal(points, _session.OverlayPoints);
    }

    [Fact]
    public void A_hud_takes_no_point_sizes()
    {
        Assert.True(_store.OpenHud(_session.Id, new HudSpec("working"), null));

        Assert.Equal(OverlayPointResizeFailure.Hud,
            _store.ResizeOverlayInPoints(_session.Id, 40, new OverlayPointSize(Height: 240)));

        Assert.True(_session.OverlayPoints.IsEmpty);
        Assert.NotEqual(40, _session.OverlaySizePercent);
    }

    [Fact]
    public void Failure_messages_are_upstreams()
    {
        Assert.Equal("no overlay", OverlayPointError.Message(OverlayPointResizeFailure.NoOverlay));
        Assert.Equal("a hud measures its own height: --width/--height do not apply",
            OverlayPointError.Message(OverlayPointResizeFailure.Hud));
        Assert.Equal("session.overlay.resize: --width/--height need a floating overlay; pass --size-percent too",
            OverlayPointError.Message(OverlayPointResizeFailure.NotFloating));
    }

    [Fact]
    public void Tree_reports_the_content_size_from_the_last_layout()
    {
        _store.OpenOverlay(_session.Id, "htop", sizePercent: 60, points: new OverlayPointSize(Height: 240));

        var node = Node();
        Assert.Null(node.OverlayWidth);
        Assert.Null(node.OverlayHeight);

        LayOut();
        node = Node();
        Assert.Equal(600, node.OverlayWidth);
        Assert.Equal(240, node.OverlayHeight);

        _store.ResizeOverlay(_session.Id, null);
        node = Node();
        Assert.Null(node.OverlayWidth);
        Assert.Null(node.OverlayHeight);
    }

    [Fact]
    public void Tree_reports_a_percent_panels_content_too()
    {
        LayOut();
        _store.OpenOverlay(_session.Id, "htop", sizePercent: 50);

        var node = Node();

        Assert.Equal(500, node.OverlayWidth);
        Assert.Equal(400, node.OverlayHeight);
    }

    // a hidden overlay still holds the slot, so its size reads back like overlaySizePercent does
    [Fact]
    public void Tree_keeps_the_size_of_a_hidden_overlay()
    {
        LayOut();
        _store.OpenOverlay(_session.Id, "htop", sizePercent: 50);
        Assert.Null(_store.SetOverlaysHidden(_session.Id, null, hidden: true));

        Assert.Equal(500, Node().OverlayWidth);
    }

    [Fact]
    public void Tree_omits_the_size_beside_a_hud()
    {
        LayOut();
        Assert.True(_store.OpenHud(_session.Id, new HudSpec("working", SizePercent: 40), null));

        var node = Node();

        Assert.Null(node.OverlayWidth);
        Assert.Null(node.OverlayHeight);
    }

    private void LayOut() =>
        _store.SetHudMetrics(_session.Id, new HudSessionMetrics(13, 6.5, 13, new HudRect(1_000, 800), null, null, 13));

    private ControlSessionNode Node() =>
        _store.ControlTree().Workspaces.SelectMany(w => w.Sessions)
            .Single(s => s.Id == _session.Id.ToString("D").ToUpperInvariant());
}
