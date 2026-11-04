using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

// horizontal split (upstream eb29d556): the positional aliases of the pane names
public class PaneAliasTests
{
    [Theory]
    [InlineData("left", StatusPane.Left)]
    [InlineData("top", StatusPane.Left)]
    [InlineData("primary", StatusPane.Left)]
    [InlineData("right", StatusPane.Right)]
    [InlineData("bottom", StatusPane.Right)]
    [InlineData("split", StatusPane.Right)]
    [InlineData("scratch", StatusPane.Scratch)]
    public void ControlNamesAcceptTheAxisAndRoleAliases(string name, StatusPane expected) =>
        Assert.Equal(expected, StatusPanes.FromControlName(name));

    [Theory]
    [InlineData("")]
    [InlineData("middle")]
    [InlineData("Left")]
    [InlineData("overlay")]
    public void ControlNamesRejectOtherSpellings(string name) => Assert.Null(StatusPanes.FromControlName(name));

    [Theory]
    [InlineData("top")]
    [InlineData("bottom")]
    [InlineData("primary")]
    [InlineData("split")]
    public void TheCanonicalWireStaysLeftRightScratch(string alias)
    {
        Assert.Null(StatusPanes.FromWire(alias));
        var pane = StatusPanes.FromControlName(alias)!.Value;
        Assert.Contains(StatusPanes.Wire(pane), new[] { "left", "right" });
    }

    [Theory]
    [InlineData("top", ControlPaneFocusMode.Primary)]
    [InlineData("bottom", ControlPaneFocusMode.Split)]
    [InlineData("left", ControlPaneFocusMode.Primary)]
    [InlineData("right", ControlPaneFocusMode.Split)]
    [InlineData("primary", ControlPaneFocusMode.Primary)]
    [InlineData("split", ControlPaneFocusMode.Split)]
    [InlineData("other", ControlPaneFocusMode.Toggle)]
    [InlineData("toggle", ControlPaneFocusMode.Toggle)]
    public void FocusModesAcceptTopAndBottom(string name, ControlPaneFocusMode expected) =>
        Assert.Equal(expected, ControlPaneFocusModes.Parse(name));

    [Theory]
    [InlineData("top", TerminalZoomSurface.Primary)]
    [InlineData("bottom", TerminalZoomSurface.Split)]
    public void ZoomSurfacesAcceptTopAndBottom(string name, TerminalZoomSurface expected) =>
        Assert.Equal(expected, TerminalZoomSurfaces.FromControlName(name));

    [Fact]
    public void ASurfaceIdAcceptsTheAliasButReadsBackLeftRight()
    {
        var id = Guid.Parse("5D3C67F2-4E58-4E12-9DFB-0DDD64FEC792");

        var parsed = TerminalSurfaceId.FromRawValue($"surface:{id.ToString().ToUpperInvariant()}:bottom");

        Assert.Equal(new TerminalSurfaceId(id, TerminalZoomSurface.Split), parsed);
        Assert.EndsWith(":right", parsed!.Value.RawValue);
    }
}
