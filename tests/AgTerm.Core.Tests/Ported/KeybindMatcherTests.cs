using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore/Tests/agtermCoreTests/KeybindMatcherTests.swift.</summary>
public class KeybindMatcherTests
{
    private static readonly Chord CtrlA = new(Modifiers.Control, "a");
    private static readonly Chord B = new(Modifiers.None, "b");
    private static readonly Chord C = new(Modifiers.None, "c");
    private static readonly Chord CmdShiftU = new(Modifiers.Command | Modifiers.Shift, "u");

    private static KeybindMatcher Matcher(params (Chord[] Keybind, Guid Id)[] binds) => new(binds);

    [Fact]
    public void SimpleChordFires()
    {
        var id = Guid.NewGuid();
        var matcher = Matcher((new[] { CmdShiftU }, id));
        Assert.Equal(MatchResult.Fired(id), matcher.Advance(CmdShiftU));
        Assert.False(matcher.IsArmed);
    }

    [Fact]
    public void UnmatchedSingleChord()
    {
        var id = Guid.NewGuid();
        var matcher = Matcher((new[] { CmdShiftU }, id));
        Assert.Equal(MatchResult.Unmatched, matcher.Advance(CtrlA));
        Assert.False(matcher.IsArmed);
    }

    [Fact]
    public void SequenceFiresOnSecondChord()
    {
        var id = Guid.NewGuid();
        var matcher = Matcher((new[] { CtrlA, B }, id));
        Assert.Equal(MatchResult.Armed, matcher.Advance(CtrlA));
        Assert.True(matcher.IsArmed);
        Assert.Equal(MatchResult.Fired(id), matcher.Advance(B));
        Assert.False(matcher.IsArmed);
    }

    [Fact]
    public void WrongSecondChordResetsAndUnmatches()
    {
        var id = Guid.NewGuid();
        var matcher = Matcher((new[] { CtrlA, B }, id));
        Assert.Equal(MatchResult.Armed, matcher.Advance(CtrlA));
        Assert.Equal(MatchResult.Unmatched, matcher.Advance(C));
        Assert.False(matcher.IsArmed);
        // after reset the leader can be re-armed.
        Assert.Equal(MatchResult.Armed, matcher.Advance(CtrlA));
    }

    [Fact]
    public void ResetClearsPending()
    {
        var id = Guid.NewGuid();
        var matcher = Matcher((new[] { CtrlA, B }, id));
        Assert.Equal(MatchResult.Armed, matcher.Advance(CtrlA));
        matcher.Reset();
        Assert.False(matcher.IsArmed);
        // the follow-key alone now no longer completes the sequence.
        Assert.Equal(MatchResult.Unmatched, matcher.Advance(B));
    }

    [Fact]
    public void TwoSequencesSharingLeader()
    {
        var idB = Guid.NewGuid();
        var idC = Guid.NewGuid();
        var matcher = Matcher((new[] { CtrlA, B }, idB), (new[] { CtrlA, C }, idC));
        Assert.Equal(MatchResult.Armed, matcher.Advance(CtrlA));
        Assert.Equal(MatchResult.Fired(idB), matcher.Advance(B));

        Assert.Equal(MatchResult.Armed, matcher.Advance(CtrlA));
        Assert.Equal(MatchResult.Fired(idC), matcher.Advance(C));
    }

    [Fact]
    public void SimpleAndSequenceCoexist()
    {
        var simpleId = Guid.NewGuid();
        var seqId = Guid.NewGuid();
        var matcher = Matcher((new[] { CmdShiftU }, simpleId), (new[] { CtrlA, B }, seqId));
        Assert.Equal(MatchResult.Fired(simpleId), matcher.Advance(CmdShiftU));
        Assert.Equal(MatchResult.Armed, matcher.Advance(CtrlA));
        Assert.Equal(MatchResult.Fired(seqId), matcher.Advance(B));
    }

    [Fact]
    public void RePressingLeaderWhileArmedReArms()
    {
        // armed on ctrl+a, a second ctrl+a is not a valid continuation but IS a fresh leader: it
        // restarts the sequence rather than abandoning it, so the next 'b' still completes.
        var id = Guid.NewGuid();
        var matcher = Matcher((new[] { CtrlA, B }, id));
        Assert.Equal(MatchResult.Armed, matcher.Advance(CtrlA));
        Assert.Equal(MatchResult.Armed, matcher.Advance(CtrlA));
        Assert.True(matcher.IsArmed);
        Assert.Equal(MatchResult.Fired(id), matcher.Advance(B));
    }

    [Fact]
    public void WrongChordWhileArmedThatIsItselfASimpleBindFires()
    {
        // armed on ctrl+a, a chord that completes no sequence but is itself a simple bind fires it
        // (the press is not dropped just because a leader was pending).
        var seqId = Guid.NewGuid();
        var simpleId = Guid.NewGuid();
        var matcher = Matcher((new[] { CtrlA, B }, seqId), (new[] { CmdShiftU }, simpleId));
        Assert.Equal(MatchResult.Armed, matcher.Advance(CtrlA));
        Assert.Equal(MatchResult.Fired(simpleId), matcher.Advance(CmdShiftU));
        Assert.False(matcher.IsArmed);
    }

    [Fact]
    public void EmptyMatcherUnmatches()
    {
        var matcher = Matcher();
        Assert.Equal(MatchResult.Unmatched, matcher.Advance(CtrlA));
        Assert.False(matcher.IsArmed);
    }
}
