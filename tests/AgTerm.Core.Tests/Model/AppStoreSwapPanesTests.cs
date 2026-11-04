using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Model;

// session.swap (upstream db62080d): the model exchange of a split session's two panes
public class AppStoreSwapPanesTests
{
    private readonly AppStore _store = AppStoreFixtures.MakeStore();
    private readonly Session _session;
    private readonly SpySurface _primary = new("p");
    private readonly SpySurface _split = new("s");

    public AppStoreSwapPanesTests()
    {
        _session = _store.AddSession(_store.AddWorkspace("work").Id, "/a")!;
        _session.Surface = _primary;
        _session.SplitSurface = _split;
        _session.IsSplit = true;
        _session.HasSplit = true;
    }

    private sealed class PlainSurface : ITerminalSurface
    {
        public string? PaneToken => "plain";
        public void Teardown() { }
        public void PromoteToPrimaryPane() { }
    }

    [Fact]
    public void AnUnknownSessionIsRefused() =>
        Assert.Equal(SwapRefusal.NoSession, _store.SwapPanes(Guid.NewGuid()));

    [Fact]
    public void ASessionWithoutASplitIsRefused()
    {
        var single = _store.AddSession(_store.Workspaces[0].Id, "/b")!;
        single.Surface = new SpySurface();

        Assert.Equal(SwapRefusal.NoSplit, _store.SwapPanes(single.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnrealizedSlotIsRefusedAndNothingMoves(bool primaryMissing)
    {
        if (primaryMissing) _session.Surface = null;
        else _session.SplitSurface = null;
        _session.SplitTitle = "keep";

        Assert.Equal(SwapRefusal.SlotNotRealized, _store.SwapPanes(_session.Id));
        Assert.Equal("keep", _session.SplitTitle);
        Assert.Equal(primaryMissing ? null : _primary, _session.Surface);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASurfaceThatCannotChangeRoleIsRefusedAndNothingMoves(bool primaryPlain)
    {
        var plain = new PlainSurface();
        if (primaryPlain) _session.Surface = plain;
        else _session.SplitSurface = plain;
        var before = _session.PaneIdentity;
        _session.OscTitle = "keep";

        Assert.Equal(SwapRefusal.RoleNotMutable, _store.SwapPanes(_session.Id));
        Assert.Equal(before, _session.PaneIdentity);
        Assert.Equal("keep", _session.OscTitle);
        Assert.Empty(_primary.Roles);
        Assert.Empty(_split.Roles);
    }

    [Fact]
    public void ASwapExchangesTheSurfacesAndTellsEachItsNewRole()
    {
        Assert.Null(_store.SwapPanes(_session.Id));

        Assert.Same(_split, _session.Surface);
        Assert.Same(_primary, _session.SplitSurface);
        Assert.Equal([SwappablePaneRole.Split], _primary.Roles);
        Assert.Equal([SwappablePaneRole.Primary], _split.Roles);
        Assert.Equal(0, _primary.TeardownCount);
        Assert.Equal(0, _split.TeardownCount);
    }

    [Fact]
    public void ThePaneTokenResolvesToTheRoleItHasNow()
    {
        Assert.Equal(StatusPane.Left, _session.PaneRole("p"));
        Assert.Equal(StatusPane.Right, _session.PaneRole("s"));

        _store.SwapPanes(_session.Id);

        Assert.Equal(StatusPane.Right, _session.PaneRole("p"));
        Assert.Equal(StatusPane.Left, _session.PaneRole("s"));
    }

    [Fact]
    public void TheIdentitiesFollowTheirTerminals()
    {
        var left = _session.PaneIdentity;
        var right = _session.SplitPaneIdentity!.Value;

        _store.SwapPanes(_session.Id);

        Assert.Equal(right, _session.PaneIdentity);
        Assert.Equal(left, _session.SplitPaneIdentity);
    }

    [Fact]
    public void AnAskAnchoredToAPaneStaysWithThatTerminal()
    {
        var right = _session.SplitPaneIdentity!.Value;
        _session.AskPaneIdentity = right;
        _session.HudPaneIdentity = right;

        _store.SwapPanes(_session.Id);

        Assert.Equal(OverlayPane.Left, _session.AskTargetPane);
        Assert.Equal(OverlayPane.Left, _session.HudTargetPane);
    }

    [Fact]
    public void TheCwdPairSwapsAndTheSplitSeedFollowsThePrimary()
    {
        _session.CurrentCwd = "/live-primary";
        _session.SplitCwd = "/live-split";

        _store.SwapPanes(_session.Id);

        Assert.Equal("/live-split", _session.CurrentCwd);
        Assert.Equal("/live-primary", _session.SplitCwd);
        Assert.Equal("/live-primary", _session.InitialSplitCwd);
    }

    [Fact]
    public void TheCwdFallsBackToTheSeedsWhenNothingWasReported()
    {
        _session.CurrentCwd = null;
        _session.SplitCwd = null;
        _session.InitialSplitCwd = null;

        _store.SwapPanes(_session.Id);

        Assert.Equal("/a", _session.CurrentCwd);
        Assert.Equal("/a", _session.SplitCwd);
        Assert.Equal("/a", _session.InitialSplitCwd);
    }

    [Fact]
    public void ARestoredSplitSeedBecomesThePrimaryCwd()
    {
        _session.CurrentCwd = null;
        _session.InitialSplitCwd = "/restored-split";

        _store.SwapPanes(_session.Id);

        Assert.Equal("/restored-split", _session.CurrentCwd);
        Assert.Equal("/a", _session.SplitCwd);
        Assert.Equal("/a", _session.InitialSplitCwd);
    }

    [Fact]
    public void TitlesForegroundAndRestoreFieldsSwapAsPairs()
    {
        _session.OscTitle = "left title";
        _session.SplitTitle = "right title";
        _session.ForegroundCommand = ["vim"];
        _session.SplitForegroundCommand = ["top"];
        _session.RestoreCommand = "make left";
        _session.SplitRestoreCommand = "make right";
        _session.PendingRestoreCommand = "pending left";
        _session.PendingSplitRestoreCommand = "pending right";

        _store.SwapPanes(_session.Id);

        Assert.Equal("right title", _session.OscTitle);
        Assert.Equal("left title", _session.SplitTitle);
        Assert.Equal(["top"], _session.ForegroundCommand);
        Assert.Equal(["vim"], _session.SplitForegroundCommand);
        Assert.Equal("make right", _session.RestoreCommand);
        Assert.Equal("make left", _session.SplitRestoreCommand);
        Assert.Equal("pending right", _session.PendingRestoreCommand);
        Assert.Equal("pending left", _session.PendingSplitRestoreCommand);
    }

    [Fact]
    public void ACommandAndItsWaitPolicyTravelWithTheirTerminal()
    {
        _session.InitialCommand = "make left";
        _session.CommandWait = true;
        _session.SplitInitialCommand = null;
        _session.SplitCommandWait = false;

        _store.SwapPanes(_session.Id);

        Assert.Null(_session.InitialCommand);
        Assert.False(_session.CommandWait);
        Assert.Equal("make left", _session.SplitInitialCommand);
        Assert.True(_session.SplitCommandWait);

        _store.SwapPanes(_session.Id);

        Assert.Equal("make left", _session.InitialCommand);
        Assert.True(_session.CommandWait);
        Assert.Null(_session.SplitInitialCommand);
        Assert.False(_session.SplitCommandWait);
    }

    [Fact]
    public void FocusFollowsTheTerminalAndTheRatioStays()
    {
        _session.SplitFocused = true;
        _session.SplitRatio = 0.3;

        _store.SwapPanes(_session.Id);

        Assert.False(_session.SplitFocused);
        Assert.Equal(0.3, _session.SplitRatio);
        Assert.True(_session.IsSplit);

        _store.SwapPanes(_session.Id);

        Assert.True(_session.SplitFocused);
    }

    [Fact]
    public void AHiddenSplitSwapsToo()
    {
        _session.IsSplit = false;
        _session.SplitFocused = false;
        _session.OscTitle = "left";
        _session.SplitTitle = "right";

        Assert.Null(_store.SwapPanes(_session.Id));

        Assert.False(_session.IsSplit);
        Assert.True(_session.HasSplit);
        Assert.True(_session.SplitFocused);
        Assert.Equal("right", _session.OscTitle);
        Assert.Same(_split, _session.Surface);
    }

    [Theory]
    [InlineData(null, StatusPane.Right)]
    [InlineData(StatusPane.Left, StatusPane.Right)]
    [InlineData(StatusPane.Right, StatusPane.Left)]
    [InlineData(StatusPane.Scratch, StatusPane.Scratch)]
    public void ThePaneTagOfAStatusFollowsItsTerminal(StatusPane? before, StatusPane after)
    {
        _store.SetAgentIndicator(new AgentIndicator(AgentStatus.Blocked, StatusPane: before), _session.Id);

        _store.SwapPanes(_session.Id);

        Assert.Equal(AgentStatus.Blocked, _session.AgentIndicator.Status);
        Assert.Equal(after, _session.AgentIndicator.StatusPane);
    }

    [Fact]
    public void AnIdleStatusGetsNoPane()
    {
        _store.SwapPanes(_session.Id);

        Assert.Equal(new AgentIndicator(), _session.AgentIndicator);
    }

    [Fact]
    public void ASearchBarStaysOnItsSurface()
    {
        _session.SearchSurface = _split;
        _session.SearchNeedle = "x";

        _store.SwapPanes(_session.Id);

        Assert.Same(_split, _session.SearchSurface);
        Assert.Equal("x", _session.SearchNeedle);
    }

    [Fact]
    public void TwoSwapsRestoreTheModelAndTheSnapshot()
    {
        _session.CurrentCwd = "/lp";
        _session.SplitCwd = "/ls";
        _session.OscTitle = "l";
        _session.SplitTitle = "r";
        _session.InitialCommand = "run";
        _session.CommandWait = true;
        var before = _session.ToSnapshot();
        var identity = _session.PaneIdentity;

        _store.SwapPanes(_session.Id);
        _store.SwapPanes(_session.Id);

        Assert.Same(_primary, _session.Surface);
        Assert.Equal(identity, _session.PaneIdentity);
        Assert.Equal(ControlJson.Serialize(before), ControlJson.Serialize(_session.ToSnapshot()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSplitCommandTwinIsRestoredOnlyForAShownSplit(bool shown)
    {
        var snapshot = new SessionSnapshot
        {
            Id = Guid.NewGuid(), Cwd = "/a", IsSplit = shown, SplitCwd = "/b",
            InitialCommand = "left", SplitInitialCommand = "right", SplitCommandWait = true,
        };

        var restored = _store.SessionFrom(snapshot);

        Assert.Equal("left", restored.InitialCommand);
        Assert.Equal(shown ? "right" : null, restored.SplitInitialCommand);
        Assert.Equal(shown, restored.SplitCommandWait);
    }

    [Fact]
    public void ASwappedCommandSessionSurvivesARelaunchOnTheOtherSide()
    {
        var (store, _, persistence) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/a", command: "make", wait: true)!;
        session.Surface = new SpySurface("p");
        session.SplitSurface = new SpySurface("s");
        session.IsSplit = true;
        session.HasSplit = true;

        store.SwapPanes(session.Id);

        var saved = Assert.Single(Assert.Single(persistence.Load().Workspaces).Sessions);
        Assert.Null(saved.InitialCommand);
        Assert.Null(saved.CommandWait);
        Assert.Equal("make", saved.SplitInitialCommand);
        Assert.True(saved.SplitCommandWait);
    }

    [Fact]
    public void ASwapNotifiesTheSidebarAndSavesToDisk()
    {
        var (store, _, persistence) = AppStoreFixtures.MakeStoreWithRecentClosed();
        var session = store.AddSession(store.AddWorkspace("work").Id, "/a")!;
        session.Surface = new SpySurface("p");
        session.SplitSurface = new SpySurface("s");
        session.IsSplit = true;
        session.HasSplit = true;
        session.CurrentCwd = "/lp";
        session.SplitCwd = "/ls";
        var raised = 0;
        store.SidebarChanged += () => raised++;

        store.SwapPanes(session.Id);

        var saved = Assert.Single(Assert.Single(persistence.Load().Workspaces).Sessions);
        Assert.Equal("/ls", saved.Cwd);
        Assert.Equal("/lp", saved.SplitCwd);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ARefusedSwapSavesAndNotifiesNothing()
    {
        var raised = 0;
        _store.SidebarChanged += () => raised++;
        _session.SplitSurface = null;

        _store.SwapPanes(_session.Id);

        Assert.Equal(0, raised);
    }
}
