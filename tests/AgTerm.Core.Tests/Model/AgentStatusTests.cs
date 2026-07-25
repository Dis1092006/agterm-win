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

        Assert.True(indicator.ClearedBy(StatusPane.Right, isInterrupt: false));
        Assert.False(indicator.ClearedBy(StatusPane.Left, isInterrupt: false));
    }

    [Fact]
    public void NilStatusPaneIsTreatedAsLeft()
    {
        var indicator = new AgentIndicator { Status = AgentStatus.Completed };
        Assert.True(indicator.ClearedBy(StatusPane.Left, isInterrupt: false));
        Assert.False(indicator.ClearedBy(StatusPane.Right, isInterrupt: false));
    }

    [Fact]
    public void ActiveClearsOnlyOnAnInterruptKeystroke()
    {
        var indicator = new AgentIndicator { Status = AgentStatus.Active };
        Assert.False(indicator.ClearedBy(StatusPane.Left, isInterrupt: false));
        Assert.True(indicator.ClearedBy(StatusPane.Left, isInterrupt: true));
    }

    [Fact]
    public void IdleIsNeverClearedByKeystroke()
    {
        var indicator = new AgentIndicator();
        Assert.False(indicator.ClearedBy(StatusPane.Left, isInterrupt: true));
    }
}
