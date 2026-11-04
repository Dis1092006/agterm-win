using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

/// <summary>session.split.close (79852e71) and session.swap (db62080d) over a real named pipe.</summary>
public sealed class HeadlessSplitPaneTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;
    private readonly List<string> _focusRequests = new();
    private readonly List<Guid> _swapped = new();

    public HeadlessSplitPaneTests()
    {
        _stateDir = Path.Combine(Path.GetTempPath(), "agterm-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateDir);
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
            PaneFocusRequested = _focusRequests.Add,
            SessionPanesSwapped = _swapped.Add,
        });
        _host.Start();
        _client = new ControlPipeClient(_host.PipeName);
    }

    public void Dispose()
    {
        _host.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private ControlResponse Send(ControlRequest request) => _client.Send(request);

    private Session NewSession(out string id)
    {
        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { WorkspaceName = "work", CreateWorkspace = true, Cwd = _stateDir },
        });
        Assert.True(response.Ok, response.Error);
        id = response.Result!.Id!;
        return _host.Store.SessionWithId(Guid.Parse(id))!;
    }

    private Session NewSplitSession(out string id, out FakeSurface left, out FakeSurface right, bool shown = true)
    {
        var session = NewSession(out id);
        left = new FakeSurface { PaneToken = "left-tok" };
        right = new FakeSurface { PaneToken = "right-tok" };
        session.Surface = left;
        session.SplitSurface = right;
        session.HasSplit = true;
        session.IsSplit = shown;
        session.SplitFocused = true;
        return session;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SplitCloseTearsDownAShownOrHiddenSplitAndFocusesTheMainPane(bool shown)
    {
        var session = NewSplitSession(out var id, out var left, out var right, shown);

        var response = Send(new ControlRequest { Cmd = Command.SessionSplitClose, Target = id });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(id, response.Result?.Id);
        Assert.False(session.HasSplit);
        Assert.False(session.IsSplit);
        Assert.False(session.SplitFocused);
        Assert.Null(session.SplitSurface);
        Assert.Same(left, session.Surface);
        Assert.Equal(1, right.TeardownCount);
        Assert.Equal(0, left.TeardownCount);
        Assert.Equal(new[] { "left-tok" }, _focusRequests);
    }

    [Fact]
    public void SplitCloseOnASessionWithoutASplitIsAnIdempotentOk()
    {
        var session = NewSession(out var id);
        var main = new FakeSurface { PaneToken = "main-tok" };
        session.Surface = main;

        var response = Send(new ControlRequest { Cmd = Command.SessionSplitClose, Target = id });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(id, response.Result?.Id);
        Assert.Equal(0, main.TeardownCount);
        Assert.Empty(_focusRequests);
    }

    [Fact]
    public void SplitCloseTwiceSecondCallIsANoOp()
    {
        NewSplitSession(out var id, out _, out var right, shown: true);

        Assert.True(Send(new ControlRequest { Cmd = Command.SessionSplitClose, Target = id }).Ok);
        var second = Send(new ControlRequest { Cmd = Command.SessionSplitClose, Target = id });

        Assert.True(second.Ok, second.Error);
        Assert.Equal(1, right.TeardownCount);
        Assert.Single(_focusRequests);
    }

    [Fact]
    public void SplitCloseRejectsAnUnknownTarget()
    {
        NewSession(out _);

        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionSplitClose, Target = Guid.NewGuid().ToString("D").ToUpperInvariant(),
        });

        Assert.False(response.Ok);
        Assert.StartsWith("no such session", response.Error);
    }

    [Fact]
    public void SwapExchangesThePanesTellsEachItsRoleAndKeepsTheFocusedPaneFocused()
    {
        var session = NewSplitSession(out var id, out var left, out var right);

        var response = Send(new ControlRequest { Cmd = Command.SessionSwap, Target = id });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(id, response.Result?.Id);
        Assert.Same(right, session.Surface);
        Assert.Same(left, session.SplitSurface);
        Assert.Equal(new[] { SwappablePaneRole.Split }, left.Roles);
        Assert.Equal(new[] { SwappablePaneRole.Primary }, right.Roles);
        Assert.False(session.SplitFocused);
        Assert.Equal(new[] { "right-tok" }, _focusRequests);
        Assert.Equal(new[] { Guid.Parse(id) }, _swapped);
    }

    [Fact]
    public void SwapOnASessionWithoutASplitIsRefused()
    {
        NewSession(out var id);

        var response = Send(new ControlRequest { Cmd = Command.SessionSwap, Target = id });

        Assert.False(response.Ok);
        Assert.Equal("session has no split pane", response.Error);
        Assert.Empty(_focusRequests);
        Assert.Empty(_swapped);
    }

    [Fact]
    public void SwapWhileASlotIsStillRealizingAnswersSessionNotRealizedAndChangesNothing()
    {
        var session = NewSplitSession(out var id, out var left, out var right);
        right.Realized = false;

        var response = Send(new ControlRequest { Cmd = Command.SessionSwap, Target = id });

        Assert.False(response.Ok);
        Assert.Equal("session not realized", response.Error);
        Assert.Same(left, session.Surface);
        Assert.Same(right, session.SplitSurface);
        Assert.Empty(left.Roles);
        Assert.Empty(_swapped);
    }

    [Fact]
    public void SwapRejectsAnUnknownTarget()
    {
        NewSession(out _);

        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionSwap, Target = Guid.NewGuid().ToString("D").ToUpperInvariant(),
        });

        Assert.False(response.Ok);
        Assert.StartsWith("no such session", response.Error);
    }

    [Fact]
    public void SwapOfAHiddenSplitStillExchangesThePanes()
    {
        var session = NewSplitSession(out var id, out var left, out var right, shown: false);

        var response = Send(new ControlRequest { Cmd = Command.SessionSwap, Target = id });

        Assert.True(response.Ok, response.Error);
        Assert.Same(right, session.Surface);
        Assert.Same(left, session.SplitSurface);
    }
}
