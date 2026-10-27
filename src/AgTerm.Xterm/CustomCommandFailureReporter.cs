using AgTerm.Core.Keymap;
using AgTerm.Core.Model;

namespace AgTerm.Xterm;

/// <summary>Where and what to paint for a failed custom command; the host opens it.</summary>
public sealed record CustomCommandFailurePanel(Guid SessionId, HudSpec Spec, OverlayPane? Pane);

public static class CustomCommandFailureReporter
{
    public const double PanelSeconds = 10;

    public static CustomCommandFailurePanel? ForExit(CustomCommandInvocation invocation, CustomCommandExit exit)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(exit);
        return exit.ExitCode == 0 ? null : Panel(invocation, $"exit {exit.ExitCode}", exit.Detail);
    }

    public static CustomCommandFailurePanel? ForNotStarted(CustomCommandInvocation invocation, string reason)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(reason);
        return Panel(invocation, reason, null);
    }

    private static CustomCommandFailurePanel? Panel(CustomCommandInvocation invocation, string reason, string? detail)
    {
        if (!invocation.ErrorHud || invocation.SessionId is not { } session)
            return null;
        var spec = new HudSpec(
            CommandFailure.Message(invocation.Name, reason),
            detail,
            Position: invocation.ErrorPosition,
            HideAfter: PanelSeconds);
        return new CustomCommandFailurePanel(session, spec, invocation.ErrorPane);
    }
}
