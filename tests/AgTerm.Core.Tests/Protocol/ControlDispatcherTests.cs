using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Core.Tests.Ported;

namespace AgTerm.Core.Tests.Protocol;

/// <summary>Own dispatcher tests on top of the ported Swift suite: edge cases the upstream tests
/// leave to app-side code or cover only implicitly.</summary>
public class ControlDispatcherTests
{
    [Fact]
    public async Task DispatchReturnsNullForDebugAppearance()
    {
        // debug.appearance is a UI-test-only seam handled outside the dispatcher; the dispatcher
        // must decline it (null) instead of erroring or crashing.
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.DebugAppearance });

        Assert.Null(response);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task EventsReadAcceptsLowercaseRunUuid()
    {
        // Swift UUID(uuidString:) is case-insensitive; the cursor run id must parse either casing.
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);
        var run = Guid.Parse("CBB5E3D0-7A9B-4C96-9EA2-18B14380DDB1");

        _ = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.EventsRead,
            Args = new ControlArgs { Run = run.ToString(), After = "7" },
        });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.EventsRead(
                new ControlEventReadOptions(new ControlEventCursor(run, 7), null, 100)),
        }, actions.Calls);
    }

    [Fact]
    public async Task SessionResizeRoutesAbsoluteRatioForm()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        var response = await dispatcher.Dispatch(new ControlRequest
        {
            Cmd = Command.SessionResize, Target = "session", Args = new ControlArgs { Ratio = 0.7 },
        });

        Assert.True(response?.Ok);
        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.SessionResize("session", null, new ControlSplitResize.Ratio(0.7)),
        }, actions.Calls);
    }

    [Fact]
    public async Task QuickTextWithoutFlagsRoutesDefaultScreenRead()
    {
        var actions = new MockControlActions();
        var dispatcher = new ControlDispatcher(actions);

        _ = await dispatcher.Dispatch(new ControlRequest { Cmd = Command.QuickText });

        Assert.Equal(new MockControlActions.Call[]
        {
            new MockControlActions.Call.QuickText(All: false, Lines: null),
        }, actions.Calls);
    }

    [Theory]
    [InlineData(null, ControlToggleMode.Toggle)]
    [InlineData("toggle", ControlToggleMode.Toggle)]
    [InlineData("on", ControlToggleMode.On)]
    [InlineData("off", ControlToggleMode.Off)]
    public void ControlToggleModeParsesDefaultTokens(string? mode, ControlToggleMode expected)
        => Assert.Equal(expected, ControlToggleModes.Parse(mode));

    [Fact]
    public void ControlToggleModeHonorsCustomTokensAndRejectsUnknown()
    {
        Assert.Equal(ControlToggleMode.On, ControlToggleModes.Parse("show", "show", "hide"));
        Assert.Equal(ControlToggleMode.Off, ControlToggleModes.Parse("hide", "show", "hide"));
        Assert.Null(ControlToggleModes.Parse("on", "show", "hide"));
    }

    [Theory]
    [InlineData(ControlToggleMode.On, false, true)]
    [InlineData(ControlToggleMode.Off, true, false)]
    [InlineData(ControlToggleMode.Toggle, true, false)]
    [InlineData(ControlToggleMode.Toggle, false, true)]
    public void ControlToggleModeDesiredValue(ControlToggleMode mode, bool current, bool expected)
        => Assert.Equal(expected, mode.DesiredValue(current));

    [Theory]
    [InlineData(null, ControlPaneFocusMode.Toggle)]
    [InlineData("other", ControlPaneFocusMode.Toggle)]
    [InlineData("toggle", ControlPaneFocusMode.Toggle)]
    [InlineData("left", ControlPaneFocusMode.Primary)]
    [InlineData("primary", ControlPaneFocusMode.Primary)]
    [InlineData("right", ControlPaneFocusMode.Split)]
    [InlineData("split", ControlPaneFocusMode.Split)]
    public void ControlPaneFocusModeParsesAliases(string? pane, ControlPaneFocusMode expected)
        => Assert.Equal(expected, ControlPaneFocusModes.Parse(pane));

    [Fact]
    public void ControlPaneFocusModeRejectsUnknownToken()
        => Assert.Null(ControlPaneFocusModes.Parse("middle"));

    [Theory]
    [InlineData(ReorderDirection.Up, 0, 3, null)]
    [InlineData(ReorderDirection.Up, 2, 3, 1)]
    [InlineData(ReorderDirection.Down, 2, 3, null)]
    [InlineData(ReorderDirection.Down, 0, 3, 1)]
    [InlineData(ReorderDirection.Top, 0, 3, null)]
    [InlineData(ReorderDirection.Top, 2, 3, 0)]
    [InlineData(ReorderDirection.Bottom, 2, 3, null)]
    [InlineData(ReorderDirection.Bottom, 0, 3, 2)]
    public void ReorderDirectionDestinationIndexHasNoWraparound(
        ReorderDirection direction, int current, int count, int? expected)
        => Assert.Equal(expected, direction.DestinationIndex(current, count));
}
