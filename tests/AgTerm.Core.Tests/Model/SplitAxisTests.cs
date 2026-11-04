using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// horizontal split (upstream eb29d556): the axis on the model, the store, the snapshot and the tree
public class SplitAxisTests
{
    private readonly AppStore _store = AppStoreFixtures.MakeStore();
    private readonly Session _session;

    public SplitAxisTests()
    {
        _session = _store.AddSession(_store.AddWorkspace("work").Id, "/a")!;
    }

    [Fact]
    public void ASessionStartsLeftRight() => Assert.Equal(SplitAxis.LeftRight, _session.SplitAxis);

    [Fact]
    public void TheWireNamesAreTheDividerDirection()
    {
        Assert.Equal("vertical", SplitAxes.Wire(SplitAxis.LeftRight));
        Assert.Equal("horizontal", SplitAxes.Wire(SplitAxis.TopBottom));
        Assert.True(SplitAxes.TryParse("vertical", out var vertical));
        Assert.Equal(SplitAxis.LeftRight, vertical);
        Assert.True(SplitAxes.TryParse("horizontal", out var horizontal));
        Assert.Equal(SplitAxis.TopBottom, horizontal);
        Assert.False(SplitAxes.TryParse("diagonal", out _));
        Assert.False(SplitAxes.TryParse("Vertical", out _));
        Assert.False(SplitAxes.TryParse(null, out _));
    }

    [Fact]
    public void AnAxisToggleFollowsCreateHideTransposeAndReshow()
    {
        _store.ToggleSplit(_session.Id, SplitAxis.TopBottom);
        Assert.True(_session.IsSplit && _session.HasSplit);
        Assert.Equal(SplitAxis.TopBottom, _session.SplitAxis);
        Assert.True(_session.SplitFocused);

        _session.SplitFocused = false;
        _store.ToggleSplit(_session.Id, SplitAxis.LeftRight);
        Assert.True(_session.IsSplit);
        Assert.Equal(SplitAxis.LeftRight, _session.SplitAxis);
        Assert.False(_session.SplitFocused); // transposing keeps the focused pane

        _store.ToggleSplit(_session.Id, SplitAxis.LeftRight);
        Assert.False(_session.IsSplit);
        Assert.True(_session.HasSplit);
        Assert.Equal(SplitAxis.LeftRight, _session.SplitAxis);

        _store.ToggleSplit(_session.Id, SplitAxis.TopBottom);
        Assert.True(_session.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, _session.SplitAxis);
        Assert.False(_session.SplitFocused); // re-showing a hidden split keeps the focused pane
    }

    [Fact]
    public void AGenericToggleKeepsTheCurrentAxis()
    {
        _session.SplitAxis = SplitAxis.TopBottom;

        _store.ToggleSplit(_session.Id);
        Assert.True(_session.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, _session.SplitAxis);

        _store.ToggleSplit(_session.Id);
        Assert.False(_session.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, _session.SplitAxis);
    }

    [Fact]
    public void SetSplitVisibilityIsIdempotentAndHidingKeepsTheAxis()
    {
        _store.SetSplitVisibility(_session.Id, shown: true, SplitAxis.TopBottom);
        _store.SetSplitVisibility(_session.Id, shown: true, SplitAxis.TopBottom);
        Assert.True(_session.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, _session.SplitAxis);
        Assert.True(_session.SplitFocused);

        _session.SplitFocused = false;
        _store.SetSplitVisibility(_session.Id, shown: true, SplitAxis.LeftRight);
        Assert.Equal(SplitAxis.LeftRight, _session.SplitAxis);
        Assert.False(_session.SplitFocused);

        _store.SetSplitVisibility(_session.Id, shown: false, SplitAxis.TopBottom);
        Assert.False(_session.IsSplit);
        Assert.Equal(SplitAxis.LeftRight, _session.SplitAxis); // hiding never picks an axis
        _store.SetSplitVisibility(_session.Id, shown: false);
        Assert.False(_session.IsSplit);
    }

    [Fact]
    public void ShowingWithoutAnAxisKeepsTheStoredOne()
    {
        _session.SplitAxis = SplitAxis.TopBottom;

        _store.SetSplitVisibility(_session.Id, shown: true);

        Assert.True(_session.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, _session.SplitAxis);
    }

    [Fact]
    public void AnUnknownSessionIsIgnored()
    {
        _store.ToggleSplit(Guid.NewGuid(), SplitAxis.TopBottom);
        _store.SetSplitVisibility(Guid.NewGuid(), shown: true, SplitAxis.TopBottom);

        Assert.False(_session.IsSplit);
    }

    [Fact]
    public void ClosingTheSplitResetsTheAxis()
    {
        _store.ToggleSplit(_session.Id, SplitAxis.TopBottom);

        _store.CloseSplit(_session.Id);

        Assert.Equal(SplitAxis.LeftRight, _session.SplitAxis);
    }

    [Fact]
    public void PromotingTheSplitPaneResetsTheAxis()
    {
        _session.Surface = new SpySurface("p");
        _session.SplitSurface = new SpySurface("s");
        _store.ToggleSplit(_session.Id, SplitAxis.TopBottom);

        _store.ClosePrimaryPane(_session.Id);

        Assert.False(_session.HasSplit);
        Assert.Equal(SplitAxis.LeftRight, _session.SplitAxis);
    }

    [Fact]
    public void ASwapKeepsTheAxis()
    {
        _session.Surface = new SpySurface("p");
        _session.SplitSurface = new SpySurface("s");
        _store.ToggleSplit(_session.Id, SplitAxis.TopBottom);

        Assert.Null(_store.SwapPanes(_session.Id));

        Assert.Equal(SplitAxis.TopBottom, _session.SplitAxis);
    }

    [Fact]
    public void ASnapshotCarriesTheAxisOfAShownSplitOnly()
    {
        Assert.Null(_session.ToSnapshot().SplitAxis);

        _store.ToggleSplit(_session.Id, SplitAxis.TopBottom);
        Assert.Equal(SplitAxis.TopBottom, _session.ToSnapshot().SplitAxis);

        _store.ToggleSplit(_session.Id); // hidden: the axis is not persisted
        Assert.Null(_session.ToSnapshot().SplitAxis);
    }

    [Fact]
    public void ARestoredShownSplitKeepsItsAxis()
    {
        _store.ToggleSplit(_session.Id, SplitAxis.TopBottom);

        var restored = _store.SessionFrom(_session.ToSnapshot());

        Assert.True(restored.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, restored.SplitAxis);
    }

    [Fact]
    public void ALegacySnapshotRestoresLeftRight()
    {
        var restored = _store.SessionFrom(new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/a", IsSplit = true });

        Assert.Equal(SplitAxis.LeftRight, restored.SplitAxis);
    }

    [Fact]
    public void AnAxisWithoutAShownSplitIsDroppedOnRestore()
    {
        var restored = _store.SessionFrom(new SessionSnapshot
        {
            Id = Guid.NewGuid(), Cwd = "/a", IsSplit = false, SplitAxis = SplitAxis.TopBottom,
        });

        Assert.Equal(SplitAxis.LeftRight, restored.SplitAxis);
    }

    [Fact]
    public void ASnapshotWritesTheAxisUnderItsWireName()
    {
        var json = ControlJson.Serialize(new SessionSnapshot
        {
            Id = Guid.NewGuid(), Cwd = "/a", IsSplit = true, SplitAxis = SplitAxis.TopBottom,
        });

        Assert.Contains("\"splitAxis\":\"horizontal\"", json);
        Assert.DoesNotContain("splitAxis", ControlJson.Serialize(new SessionSnapshot { Id = Guid.NewGuid(), Cwd = "/a" }));
    }

    [Theory]
    [InlineData("\"horizontal\"", SplitAxis.TopBottom)]
    [InlineData("\"vertical\"", SplitAxis.LeftRight)]
    [InlineData("\"diagonal\"", null)]
    [InlineData("3", null)]
    [InlineData("true", null)]
    public void AHandEditedAxisDecodesLossyAndKeepsTheSession(string value, SplitAxis? expected)
    {
        var json = "{\"id\":\"" + Guid.NewGuid() + "\",\"cwd\":\"/h\",\"customName\":\"keep\",\"splitAxis\":" + value + "}";

        var session = ControlJson.Deserialize<SessionSnapshot>(json)!;

        Assert.Equal(expected, session.SplitAxis);
        Assert.Equal("keep", session.CustomName);
    }

    [Fact]
    public void TheTreeReportsTheAxisOfASessionThatHasASplit()
    {
        Assert.Null(Node().SplitAxis);

        _store.ToggleSplit(_session.Id);
        Assert.Equal("vertical", Node().SplitAxis);

        _store.ToggleSplit(_session.Id, SplitAxis.TopBottom);
        Assert.Equal("horizontal", Node().SplitAxis);

        _store.ToggleSplit(_session.Id, SplitAxis.TopBottom); // hidden, still has a split
        Assert.False(Node().Split);
        Assert.Equal("horizontal", Node().SplitAxis);
        Assert.Contains("\"splitAxis\":\"horizontal\"", ControlJson.Serialize(Node()));
    }

    [Fact]
    public void TheTreeOmitsTheAxisWithoutASplit()
    {
        Assert.DoesNotContain("splitAxis", ControlJson.Serialize(Node()));
    }

    private ControlSessionNode Node() => _store.ControlTree().Workspaces[0].Sessions[0];
}
