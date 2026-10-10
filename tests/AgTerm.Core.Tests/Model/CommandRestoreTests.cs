using AgTerm.Core.Model;
using Xunit;

namespace AgTerm.Core.Tests.Model;

// Ported from agtermCore/Tests/agtermCoreTests/CommandRestoreTests.swift (upstream 131dfc61).
public class CommandRestoreTests
{
    [Fact]
    public void IsReplayableRefusesControlCharactersAndLossyText()
    {
        Assert.False(CommandRestore.IsReplayable(["sed", "s/\u001B//"]));
        Assert.False(CommandRestore.IsReplayable(["awk", "-F", "\t", "{print}"]));
        Assert.False(CommandRestore.IsReplayable(["grep", "a\u0015b"]));
        Assert.False(CommandRestore.IsReplayable(["grep", "a\nrm -rf x"]));
        Assert.False(CommandRestore.IsReplayable(["less", "note\u007F.txt"]));
        Assert.False(CommandRestore.IsReplayable(["od\u001Bd"]));
        Assert.False(CommandRestore.IsReplayable(["grep", "caf\uFFFD"]));
        Assert.False(CommandRestore.IsReplayable([]));
        Assert.True(CommandRestore.IsReplayable(["echo", "a b"]));
        Assert.True(CommandRestore.IsReplayable(["echo", "naïve — ✓"]));
    }

    [Fact]
    public void IsReplayableRefusesAnEmptyProgramName()
    {
        Assert.False(CommandRestore.IsReplayable([""]));
        Assert.False(CommandRestore.IsReplayable(["", "x"]));
    }

    [Fact]
    public void HasControlCharacterCoversC0AndDelButNotReplacementCharacter()
    {
        Assert.True(CommandRestore.HasControlCharacter("tail -f\u001Bx"));
        Assert.True(CommandRestore.HasControlCharacter("a\nrm -rf x"));
        Assert.True(CommandRestore.HasControlCharacter("x\u007F"));
        Assert.False(CommandRestore.HasControlCharacter("cd x && claude"));
        Assert.False(CommandRestore.HasControlCharacter("caf\uFFFD"));
    }
}
