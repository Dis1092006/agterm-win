using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

/// <summary>session.split --axis (upstream eb29d556) over a real named pipe.</summary>
public sealed class HeadlessSplitAxisTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessSplitAxisTests()
    {
        _stateDir = Path.Combine(Path.GetTempPath(), "agterm-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateDir);
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
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

    private Session NewSession(out string id)
    {
        var response = _client.Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs { WorkspaceName = "work", CreateWorkspace = true, Cwd = _stateDir },
        });
        Assert.True(response.Ok, response.Error);
        id = response.Result!.Id!;
        return _host.Store.SessionWithId(Guid.Parse(id))!;
    }

    private ControlResponse Split(string id, string? mode, string? axis) => _client.Send(new ControlRequest
    {
        Cmd = Command.SessionSplit, Target = id, Args = new ControlArgs { Mode = mode, Axis = axis },
    });

    [Fact]
    public void OnWithAHorizontalAxisCreatesATopBottomSplit()
    {
        var session = NewSession(out var id);

        var response = Split(id, "on", "horizontal");

        Assert.True(response.Ok, response.Error);
        Assert.True(session.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, session.SplitAxis);
    }

    [Fact]
    public void OnWithAnotherAxisTransposesAShownSplit()
    {
        var session = NewSession(out var id);
        Assert.True(Split(id, "on", "vertical").Ok);

        var response = Split(id, "on", "horizontal");

        Assert.True(response.Ok, response.Error);
        Assert.True(session.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, session.SplitAxis);
    }

    [Fact]
    public void OnWithTheCurrentAxisIsIdempotent()
    {
        var session = NewSession(out var id);
        Assert.True(Split(id, "on", "horizontal").Ok);

        Assert.True(Split(id, "on", "horizontal").Ok);

        Assert.True(session.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, session.SplitAxis);
    }

    [Fact]
    public void ToggleWithAnAxisHidesOnlyWhenTheAxisMatches()
    {
        var session = NewSession(out var id);
        Assert.True(Split(id, "toggle", "horizontal").Ok);
        Assert.True(session.IsSplit);

        Assert.True(Split(id, "toggle", "vertical").Ok);
        Assert.True(session.IsSplit);
        Assert.Equal(SplitAxis.LeftRight, session.SplitAxis);

        Assert.True(Split(id, "toggle", "vertical").Ok);
        Assert.False(session.IsSplit);
        Assert.True(session.HasSplit);
    }

    [Fact]
    public void OffHidesTheSplitAndKeepsItsAxis()
    {
        var session = NewSession(out var id);
        Assert.True(Split(id, "on", "horizontal").Ok);

        var response = Split(id, "off", null);

        Assert.True(response.Ok, response.Error);
        Assert.False(session.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, session.SplitAxis);
    }

    [Fact]
    public void WithoutAnAxisTheStoredOneIsKept()
    {
        var session = NewSession(out var id);
        Assert.True(Split(id, "on", "horizontal").Ok);
        Assert.True(Split(id, "off", null).Ok);

        Assert.True(Split(id, "on", null).Ok);

        Assert.True(session.IsSplit);
        Assert.Equal(SplitAxis.TopBottom, session.SplitAxis);
    }

    [Fact]
    public void AnUnknownAxisIsRejectedOverThePipeAndChangesNothing()
    {
        var session = NewSession(out var id);

        var response = Split(id, "on", "diagonal");

        Assert.False(response.Ok);
        Assert.Equal("invalid split axis: diagonal (vertical|horizontal)", response.Error);
        Assert.False(session.IsSplit);
    }

    [Fact]
    public void TheTreeReportsTheAxis()
    {
        NewSession(out var id);
        Assert.True(Split(id, "on", "horizontal").Ok);

        var tree = _client.Send(new ControlRequest { Cmd = Command.Tree }).Result!.Tree!;

        var node = tree.Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == id);
        Assert.Equal("horizontal", node.SplitAxis);
    }

    [Fact]
    public void ABadModeStillRejectsBeforeAnAxisIsApplied()
    {
        var session = NewSession(out var id);

        var response = Split(id, "sideways", "horizontal");

        Assert.False(response.Ok);
        Assert.Equal("invalid split mode: sideways", response.Error);
        Assert.Equal(SplitAxis.LeftRight, session.SplitAxis);
    }

    [Fact]
    public void AnAliasPaneReachesTheRightSurface()
    {
        var session = NewSession(out var id);
        var left = new FakeSurface { PaneToken = "left-tok" };
        var right = new FakeSurface { PaneToken = "right-tok" };
        session.Surface = left;
        session.SplitSurface = right;
        session.HasSplit = true;
        session.IsSplit = true;

        var response = _client.Send(new ControlRequest
        {
            Cmd = Command.FontInc, Target = id, Args = new ControlArgs { Pane = "bottom" },
        });

        Assert.True(response.Ok, response.Error);
        Assert.Single(right.BindingActions);
        Assert.Empty(left.BindingActions);
    }
}
