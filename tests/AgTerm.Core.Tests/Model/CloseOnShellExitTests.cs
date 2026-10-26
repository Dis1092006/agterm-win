using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

/// <summary>closeOnShellExit, a Windows-only setting: whether a shell pane closes when its shell
/// exits with a given code (docs/PLATFORM-NOTES.md).</summary>
public class CloseOnShellExitTests
{
    [Theory]
    [InlineData(null, 0, true)]
    [InlineData(null, 1, false)]
    [InlineData(null, -1, false)]
    [InlineData("graceful", 0, true)]
    [InlineData("graceful", 3, false)]
    [InlineData("always", 0, true)]
    [InlineData("always", 3, true)]
    [InlineData("never", 0, false)]
    [InlineData("never", 3, false)]
    [InlineData("someFutureMode", 0, true)]
    [InlineData("someFutureMode", 3, false)]
    [InlineData("Always", 3, false)]
    public void A_shell_pane_closes_by_mode_and_exit_code(string? mode, int exitCode, bool closes)
    {
        Assert.Equal(closes, new AppSettings { CloseOnShellExit = mode }.ClosesPaneOnShellExit(exitCode));
    }

    [Fact]
    public void The_mode_round_trips_and_is_omitted_when_absent()
    {
        var settings = ControlJson.Deserialize<AppSettings>("""{"closeOnShellExit":"never","fontSize":15}""")!;

        Assert.Equal("never", settings.CloseOnShellExit);
        Assert.Contains("\"closeOnShellExit\":\"never\"", ControlJson.Serialize(settings));
        Assert.DoesNotContain("closeOnShellExit", ControlJson.Serialize(new AppSettings()));
        Assert.NotEqual(new AppSettings(), new AppSettings { CloseOnShellExit = "never" });
    }
}
