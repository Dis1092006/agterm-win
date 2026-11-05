using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// statusChangedAt on every accepted set (upstream 5fae4f68, 48dabf83) and the pane-precedence rule of
// session.status (upstream c8860a91)
public class AppStoreStatusWriteTests
{
    private readonly AppStore _store;
    private readonly Session _session;
    private readonly List<ControlEventDraft> _events = [];

    public AppStoreStatusWriteTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agterm-tests-" + Guid.NewGuid());
        _store = new AppStore(persistence: new PersistenceStore(dir), recentClosedStore: new RecentClosedStore(dir),
            controlEventSink: _events.Add, stateDirectory: dir);
        _session = _store.AddSession(_store.AddWorkspace("work").Id, "/a")!;
    }

    private ControlSessionNode Node() => _store.ControlTree().Workspaces[0].Sessions[0];

    private static AgentIndicator Ind(AgentStatus status, StatusPane? pane = null) =>
        new(status, StatusPane: pane);

    [Fact]
    public void AnIdleSetStampsStatusChangedAt()
    {
        var before = DateTimeOffset.UtcNow;
        _store.SetAgentIndicator(Ind(AgentStatus.Idle), _session.Id);

        Assert.True(_session.StatusChangedAt >= before);
    }

    [Fact]
    public void ReturningToIdleKeepsTheStamp()
    {
        _store.SetAgentIndicator(Ind(AgentStatus.Active), _session.Id);
        var stamp = _session.StatusChangedAt;
        _store.SetAgentIndicator(Ind(AgentStatus.Idle), _session.Id);

        Assert.True(_session.StatusChangedAt >= stamp);
        Assert.NotNull(_session.StatusChangedAt);
    }

    [Fact]
    public void TheTreeOmitsStatusChangedAtForASessionThatNeverHadAStatus() =>
        Assert.Null(Node().StatusChangedAt);

    [Fact]
    public void TheTreeReportsStatusChangedAtAsEpochSecondsOnTheEventClock()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        _store.SetAgentIndicator(Ind(AgentStatus.Active), _session.Id);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        Assert.InRange(Node().StatusChangedAt!.Value, before - 0.002, after + 0.002);
    }

    [Fact]
    public void TheTreeKeepsReportingStatusChangedAtAfterIdle()
    {
        _store.SetAgentIndicator(Ind(AgentStatus.Blocked), _session.Id);
        _store.SetAgentIndicator(Ind(AgentStatus.Idle), _session.Id);

        Assert.Null(Node().Status);
        Assert.NotNull(Node().StatusChangedAt);
    }

    [Fact]
    public void ARepeatedValueRestampsWithoutAnEvent()
    {
        _store.SetAgentIndicator(Ind(AgentStatus.Active), _session.Id);
        _session.StatusChangedAt = DateTimeOffset.FromUnixTimeSeconds(0);
        var count = _events.Count;
        _store.SetAgentIndicator(Ind(AgentStatus.Active), _session.Id);

        Assert.True(_session.StatusChangedAt > DateTimeOffset.FromUnixTimeSeconds(1));
        Assert.Equal(count, _events.Count);
    }

    private void SplitSession() => _session.HasSplit = true;

    [Fact]
    public void AWriteFromAnotherPaneCannotReplaceABlockedStatus()
    {
        SplitSession();
        _store.SetAgentIndicator(Ind(AgentStatus.Blocked, StatusPane.Left), _session.Id);
        var stamp = _session.StatusChangedAt;
        var count = _events.Count;

        var result = _store.ApplyControlStatus(Ind(AgentStatus.Active, StatusPane.Right), _session.Id);

        Assert.Equal(StatusWriteResult.Refused(StatusPane.Left), result);
        Assert.Equal(AgentStatus.Blocked, _session.AgentIndicator.Status);
        Assert.Equal(stamp, _session.StatusChangedAt);
        Assert.Equal(count, _events.Count);
    }

    [Theory]
    [InlineData(AgentStatus.Completed)]
    [InlineData(AgentStatus.Idle)]
    public void NoNonBlockedWriteFromAnotherPaneReplacesTheBlock(AgentStatus status)
    {
        SplitSession();
        _store.SetAgentIndicator(Ind(AgentStatus.Blocked, StatusPane.Left), _session.Id);

        Assert.NotNull(_store.ApplyControlStatus(Ind(status, StatusPane.Right), _session.Id).RefusedOwner);
    }

    [Fact]
    public void ASecondPanesOwnBlockedReplaces()
    {
        SplitSession();
        _store.SetAgentIndicator(Ind(AgentStatus.Blocked, StatusPane.Left), _session.Id);

        Assert.Equal(StatusWriteResult.Applied,
            _store.ApplyControlStatus(Ind(AgentStatus.Blocked, StatusPane.Right), _session.Id));
        Assert.Equal(StatusPane.Right, _session.AgentIndicator.StatusPane);
    }

    [Theory]
    [InlineData(AgentStatus.Active)]
    [InlineData(AgentStatus.Idle)]
    public void TheOwningPaneWritesFreely(AgentStatus status)
    {
        SplitSession();
        _store.SetAgentIndicator(Ind(AgentStatus.Blocked, StatusPane.Right), _session.Id);

        Assert.Equal(StatusWriteResult.Applied,
            _store.ApplyControlStatus(Ind(status, StatusPane.Right), _session.Id));
        Assert.Equal(status, _session.AgentIndicator.Status);
    }

    [Fact]
    public void AnUntaggedWriteReadsAsLeft()
    {
        SplitSession();
        _store.SetAgentIndicator(Ind(AgentStatus.Blocked, StatusPane.Right), _session.Id);

        Assert.Equal(StatusWriteResult.Refused(StatusPane.Right),
            _store.ApplyControlStatus(Ind(AgentStatus.Idle), _session.Id));
    }

    [Fact]
    public void APromotedSurvivorsStaleRightTagMatchesTheLeftItIsStoredAs()
    {
        _store.SetAgentIndicator(Ind(AgentStatus.Blocked, StatusPane.Right), _session.Id);
        Assert.Equal(StatusPane.Left, _session.AgentIndicator.StatusPane); // no split: folded to left

        Assert.Equal(StatusWriteResult.Applied,
            _store.ApplyControlStatus(Ind(AgentStatus.Active, StatusPane.Right), _session.Id));
    }

    [Fact]
    public void AWriteIsUnrestrictedWhileNotBlocked()
    {
        SplitSession();
        _store.SetAgentIndicator(Ind(AgentStatus.Active, StatusPane.Left), _session.Id);

        Assert.Equal(StatusWriteResult.Applied,
            _store.ApplyControlStatus(Ind(AgentStatus.Completed, StatusPane.Right), _session.Id));
    }

    [Fact]
    public void InternalWritesBypassTheRule()
    {
        SplitSession();
        _store.SetAgentIndicator(Ind(AgentStatus.Blocked, StatusPane.Left), _session.Id);
        _store.SetAgentIndicator(new AgentIndicator(), _session.Id); // Clear Status

        Assert.Equal(AgentStatus.Idle, _session.AgentIndicator.Status);
    }
}
