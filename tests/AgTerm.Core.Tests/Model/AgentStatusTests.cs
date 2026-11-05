using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

public class AgentStatusTests
{
    [Theory]
    [InlineData(AgentStatus.Blocked, true)]
    [InlineData(AgentStatus.Completed, true)]
    [InlineData(AgentStatus.Idle, false)]
    [InlineData(AgentStatus.Active, false)]
    public void NeedsAttentionCoversBlockedAndCompleted(AgentStatus status, bool expected)
    {
        Assert.Equal(expected, status.NeedsAttention());
    }

    [Theory]
    [InlineData(AgentStatus.Blocked, 0)]
    [InlineData(AgentStatus.Active, 1)]
    [InlineData(AgentStatus.Completed, 2)]
    [InlineData(AgentStatus.Idle, 3)]
    public void AttentionRankOrdersBlockedFirst(AgentStatus status, int rank)
    {
        Assert.Equal(rank, status.AttentionRank());
    }

    [Theory]
    [InlineData("idle", AgentStatus.Idle)]
    [InlineData("active", AgentStatus.Active)]
    [InlineData("completed", AgentStatus.Completed)]
    [InlineData("blocked", AgentStatus.Blocked)]
    public void RawValuesRoundTripOnTheWire(string wire, AgentStatus status)
    {
        Assert.Equal($"\"{wire}\"", ControlJson.Serialize(status));
        Assert.Equal(status, ControlJson.Deserialize<AgentStatus>($"\"{wire}\""));
    }

    [Fact]
    public void EffectiveSoundPrefersPerCallThenBlockedDefault()
    {
        Assert.Equal("Glass", AgentStatus.Active.EffectiveSound(perCall: "Glass", blockedDefault: "Boop"));
        Assert.Equal("Boop", AgentStatus.Blocked.EffectiveSound(perCall: null, blockedDefault: "Boop"));
        Assert.Equal("Boop", AgentStatus.Blocked.EffectiveSound(perCall: "", blockedDefault: "Boop"));
        Assert.Null(AgentStatus.Completed.EffectiveSound(perCall: null, blockedDefault: "Boop"));
        Assert.Null(AgentStatus.Blocked.EffectiveSound(perCall: null, blockedDefault: ""));
    }

    [Fact]
    public void StatusShapeValidNameListsMatchUpstreamFormat()
    {
        Assert.Equal("circle|square|triangle|diamond|capsule|star", StatusShapes.ValidNamesList);
        Assert.Equal("circle, square, triangle, diamond, capsule, star", StatusShapes.ValidNamesPhrase);
    }

    [Fact]
    public void IndicatorClearsOnlyWhenTheKeystrokesPaneOwnsTheStatus()
    {
        var indicator = new AgentIndicator { Status = AgentStatus.Blocked, StatusPane = StatusPane.Right };

        Assert.True(indicator.ClearedBy(StatusPane.Right, StatusKeystroke.Other, StatusReset.FirstKey));
        Assert.False(indicator.ClearedBy(StatusPane.Left, StatusKeystroke.Other, StatusReset.FirstKey));
    }

    [Fact]
    public void NilStatusPaneIsTreatedAsLeft()
    {
        var indicator = new AgentIndicator { Status = AgentStatus.Completed };
        Assert.True(indicator.ClearedBy(StatusPane.Left, StatusKeystroke.Other, StatusReset.FirstKey));
        Assert.False(indicator.ClearedBy(StatusPane.Right, StatusKeystroke.Other, StatusReset.FirstKey));
    }

    [Fact]
    public void ActiveClearsOnlyOnAnInterruptKeystroke()
    {
        var indicator = new AgentIndicator { Status = AgentStatus.Active };
        Assert.False(indicator.ClearedBy(StatusPane.Left, StatusKeystroke.Other, StatusReset.FirstKey));
        Assert.True(indicator.ClearedBy(StatusPane.Left, StatusKeystroke.Interrupt, StatusReset.FirstKey));
    }

    [Fact]
    public void IdleIsNeverClearedByKeystroke()
    {
        var indicator = new AgentIndicator();
        Assert.False(indicator.ClearedBy(StatusPane.Left, StatusKeystroke.Interrupt, StatusReset.FirstKey));
    }

    [Theory]
    [InlineData(AgentStatus.Blocked, StatusReset.FirstKey, StatusKeystroke.Other, true)]
    [InlineData(AgentStatus.Blocked, StatusReset.FirstKey, StatusKeystroke.Submit, true)]
    [InlineData(AgentStatus.Blocked, StatusReset.FirstKey, StatusKeystroke.Interrupt, true)]
    [InlineData(AgentStatus.Completed, StatusReset.FirstKey, StatusKeystroke.Other, true)]
    [InlineData(AgentStatus.Completed, StatusReset.FirstKey, StatusKeystroke.Submit, true)]
    [InlineData(AgentStatus.Completed, StatusReset.FirstKey, StatusKeystroke.Interrupt, true)]
    [InlineData(AgentStatus.Blocked, StatusReset.Enter, StatusKeystroke.Other, false)]
    [InlineData(AgentStatus.Completed, StatusReset.Enter, StatusKeystroke.Other, false)]
    [InlineData(AgentStatus.Blocked, StatusReset.Enter, StatusKeystroke.Submit, true)]
    [InlineData(AgentStatus.Completed, StatusReset.Enter, StatusKeystroke.Submit, true)]
    [InlineData(AgentStatus.Blocked, StatusReset.Enter, StatusKeystroke.Interrupt, true)] // cancelling a prompt fires no hook
    [InlineData(AgentStatus.Completed, StatusReset.Enter, StatusKeystroke.Interrupt, false)]
    [InlineData(AgentStatus.Blocked, StatusReset.Never, StatusKeystroke.Other, false)]
    [InlineData(AgentStatus.Blocked, StatusReset.Never, StatusKeystroke.Submit, false)]
    [InlineData(AgentStatus.Blocked, StatusReset.Never, StatusKeystroke.Interrupt, false)]
    [InlineData(AgentStatus.Completed, StatusReset.Never, StatusKeystroke.Submit, false)]
    [InlineData(AgentStatus.Completed, StatusReset.Never, StatusKeystroke.Interrupt, false)]
    public void BlockedAndCompletedClearAsTheResetModeSays(
        AgentStatus status, StatusReset reset, StatusKeystroke keystroke, bool cleared) =>
        Assert.Equal(cleared, new AgentIndicator { Status = status }.ClearedBy(StatusPane.Left, keystroke, reset));

    [Theory]
    [InlineData(StatusReset.FirstKey)]
    [InlineData(StatusReset.Enter)]
    [InlineData(StatusReset.Never)]
    public void ActiveClearsOnAnInterruptInEveryMode(StatusReset reset)
    {
        var indicator = new AgentIndicator { Status = AgentStatus.Active };
        Assert.True(indicator.ClearedBy(StatusPane.Left, StatusKeystroke.Interrupt, reset));
        Assert.False(indicator.ClearedBy(StatusPane.Left, StatusKeystroke.Submit, reset));
        Assert.False(indicator.ClearedBy(StatusPane.Left, StatusKeystroke.Other, reset));
    }

    [Fact]
    public void ResetModesParseFromTheirRawValues()
    {
        Assert.Equal(StatusReset.FirstKey, StatusResets.FromWire("firstKey"));
        Assert.Equal(StatusReset.Enter, StatusResets.FromWire("enter"));
        Assert.Equal(StatusReset.Never, StatusResets.FromWire("never"));
        Assert.Null(StatusResets.FromWire("bogus"));
    }
}
