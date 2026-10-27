using AgTerm.Core.Model;

namespace AgTerm.Xterm.Tests;

public sealed class CustomCommandFailureReporterTests
{
    private static CustomCommandInvocation Invocation(
        bool errorHud = true,
        Guid? sessionId = null,
        HudPosition position = HudPosition.BottomRight,
        OverlayPane? pane = OverlayPane.Right) =>
        new(Guid.NewGuid(), "Build", "make", "", new Dictionary<string, string>(),
            sessionId, errorHud, position, pane);

    [Fact]
    public void A_non_zero_exit_of_an_opted_in_command_gives_a_ten_second_panel()
    {
        var session = Guid.NewGuid();

        var panel = CustomCommandFailureReporter.ForExit(
            Invocation(sessionId: session), new CustomCommandExit(3, "build broke"));

        Assert.NotNull(panel);
        Assert.Equal(session, panel!.SessionId);
        Assert.Equal("Build: exit 3", panel.Spec.Message);
        Assert.Equal("build broke", panel.Spec.Detail);
        Assert.Equal(HudPosition.BottomRight, panel.Spec.Position);
        Assert.Equal(10, panel.Spec.HideAfter);
        Assert.Equal(OverlayPane.Right, panel.Pane);
    }

    [Fact]
    public void Exit_zero_gives_no_panel() =>
        Assert.Null(CustomCommandFailureReporter.ForExit(
            Invocation(sessionId: Guid.NewGuid()), new CustomCommandExit(0, "warning: noisy")));

    [Fact]
    public void A_command_without_error_hud_gives_no_panel() =>
        Assert.Null(CustomCommandFailureReporter.ForExit(
            Invocation(errorHud: false, sessionId: Guid.NewGuid()), new CustomCommandExit(1, null)));

    [Fact]
    public void A_sessionless_command_gives_no_panel() =>
        Assert.Null(CustomCommandFailureReporter.ForExit(Invocation(), new CustomCommandExit(1, "x")));

    [Fact]
    public void A_failure_with_no_stderr_has_no_detail()
    {
        var panel = CustomCommandFailureReporter.ForExit(
            Invocation(sessionId: Guid.NewGuid()), new CustomCommandExit(1, null));

        Assert.Null(panel!.Spec.Detail);
    }

    [Fact]
    public void A_command_that_never_started_gives_the_reason_as_the_message()
    {
        var panel = CustomCommandFailureReporter.ForNotStarted(
            Invocation(sessionId: Guid.NewGuid()), @"session directory C:\gone no longer exists");

        Assert.Equal(@"Build: session directory C:\gone no longer exists", panel!.Spec.Message);
        Assert.Null(panel.Spec.Detail);
        Assert.Equal(10, panel.Spec.HideAfter);
    }

    [Fact]
    public void A_never_started_command_that_did_not_opt_in_gives_no_panel() =>
        Assert.Null(CustomCommandFailureReporter.ForNotStarted(
            Invocation(errorHud: false, sessionId: Guid.NewGuid()), "boom"));
}
