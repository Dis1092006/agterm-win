using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.Xterm.Tests;

/// <summary>A workspace-targeted command without a window goes to the window that owns the workspace.</summary>
public class LiveApplicationHostRoutingTests
{
    [Theory]
    [InlineData(Command.WorkspaceFlag)]
    [InlineData(Command.WorkspaceTint)]
    public void WorkspaceCommandsRouteByTheirWorkspaceTarget(Command command)
    {
        Assert.True(LiveApplicationHost.UsesWorkspaceTarget(command));
    }

    [Theory]
    [InlineData(Command.SessionOverlayHide)]
    [InlineData(Command.SessionOverlayShow)]
    [InlineData(Command.SessionOverlayToggle)]
    public void OverlayVisibilityCommandsRouteByTheirSessionTarget(Command command)
    {
        Assert.True(LiveApplicationHost.UsesSingleSessionTarget(command));
    }
}
