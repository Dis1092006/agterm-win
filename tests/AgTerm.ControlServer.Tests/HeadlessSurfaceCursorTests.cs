using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

/// <summary>surface.cursor (2279c722, --pane-id eab672be) over a real named pipe.</summary>
public sealed class HeadlessSurfaceCursorTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessSurfaceCursorTests()
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

    private ControlResponse Cursor(string? target, string? paneId = null) => _client.Send(new ControlRequest
    {
        Cmd = Command.SurfaceCursor, Target = target,
        Args = paneId is null ? null : new ControlArgs { PaneId = paneId },
    });

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

    private Session NewSplitSession(out string id, out FakeSurface left, out FakeSurface right)
    {
        var session = NewSession(out id);
        left = new FakeSurface { PaneToken = "left-tok", CursorColumn = 4 };
        right = new FakeSurface { PaneToken = "right-tok", CursorColumn = 9 };
        session.Surface = left;
        session.SplitSurface = right;
        session.HasSplit = true;
        session.IsSplit = true;
        session.SplitFocused = false;
        return session;
    }

    [Fact]
    public void ActiveTargetReportsTheFocusedPane()
    {
        NewSplitSession(out var id, out _, out _);

        var response = Cursor(null);

        Assert.True(response.Ok, response.Error);
        Assert.Equal($"surface:{id}:left", response.Result!.Id);
        Assert.Equal(4, response.Result.Cursor!.Column);
    }

    [Fact]
    public void ActiveTargetFollowsSplitFocus()
    {
        var session = NewSplitSession(out var id, out _, out _);
        session.SplitFocused = true;

        var response = Cursor("active");

        Assert.Equal($"surface:{id}:right", response.Result!.Id);
        Assert.Equal(9, response.Result.Cursor!.Column);
    }

    [Fact]
    public void SurfaceIdTargetReportsThatSurface()
    {
        NewSplitSession(out var id, out _, out _);

        var response = Cursor($"surface:{id}:right");

        Assert.True(response.Ok, response.Error);
        Assert.Equal($"surface:{id}:right", response.Result!.Id);
        Assert.Equal(9, response.Result.Cursor!.Column);
    }

    [Fact]
    public void ScratchSurfaceIsAddressable()
    {
        var session = NewSplitSession(out var id, out _, out _);
        session.ScratchSurface = new FakeSurface { CursorColumn = 2 };
        session.ScratchActive = true;

        var response = Cursor($"surface:{id}:scratch");

        Assert.Equal(2, response.Result!.Cursor!.Column);
    }

    [Fact]
    public void ColumnZeroIsReportedNotOmitted()
    {
        var session = NewSession(out var id);
        session.Surface = new FakeSurface { CursorColumn = 0 };

        var response = Cursor("active");

        Assert.Equal(0, response.Result!.Cursor!.Column);
    }

    [Fact]
    public void PaneIdPicksThePaneByItsToken()
    {
        NewSplitSession(out var id, out _, out _);

        var response = Cursor("active", "right-tok");

        Assert.True(response.Ok, response.Error);
        Assert.Equal($"surface:{id}:right", response.Result!.Id);
        Assert.Equal(9, response.Result.Cursor!.Column);
    }

    [Fact]
    public void PaneIdWorksWithASessionIdTarget()
    {
        NewSplitSession(out var id, out _, out _);

        var response = Cursor(id, "left-tok");

        Assert.Equal($"surface:{id}:left", response.Result!.Id);
        Assert.Equal(4, response.Result.Cursor!.Column);
    }

    [Fact]
    public void PaneIdFollowsATerminalToItsNewSlot()
    {
        var session = NewSplitSession(out var id, out var left, out var right);
        (session.Surface, session.SplitSurface) = (right, left);

        var response = Cursor("active", "right-tok");

        Assert.Equal($"surface:{id}:left", response.Result!.Id);
        Assert.Equal(9, response.Result.Cursor!.Column);
    }

    [Fact]
    public void UnknownPaneIdIsRefused()
    {
        NewSplitSession(out _, out _, out _);

        Assert.Equal("unknown pane id: nope", Cursor("active", "nope").Error);
    }

    [Theory]
    [InlineData("quick")]
    [InlineData("surface:00000000-0000-0000-0000-000000000001:left")]
    public void PaneIdWithANonSessionTargetIsRefused(string target)
    {
        NewSplitSession(out _, out _, out _);

        Assert.Equal("surface.cursor: --pane-id takes a session target", Cursor(target, "left-tok").Error);
    }

    [Fact]
    public void EmptyPaneIdCountsAsAbsent()
    {
        NewSplitSession(out var id, out _, out _);

        var response = Cursor($"surface:{id}:right", "");

        Assert.True(response.Ok, response.Error);
        Assert.Equal(9, response.Result!.Cursor!.Column);
    }

    [Fact]
    public void QuickNeedsAVisibleQuickTerminal()
    {
        _host.Actions.Quick.Surface = new FakeSurface { CursorColumn = 6 };

        Assert.Equal("surface not available: quick", Cursor("quick").Error);

        _host.Actions.Quick.Show();
        var response = Cursor("quick");

        Assert.True(response.Ok, response.Error);
        Assert.Equal("quick", response.Result!.Id);
        Assert.Equal(6, response.Result.Cursor!.Column);
    }

    [Fact]
    public void QuickWithoutASurfaceIsNotAvailable()
    {
        _host.Actions.Quick.Show();

        Assert.Equal("surface not available: quick", Cursor("quick").Error);
    }

    [Fact]
    public void InvalidSurfaceIdIsRefused()
    {
        Assert.Equal("invalid surface: bogus", Cursor("bogus").Error);
    }

    [Fact]
    public void SurfaceOfAnUnknownSessionIsRefused()
    {
        var id = new TerminalSurfaceId(Guid.NewGuid(), TerminalZoomSurface.Primary).RawValue;

        Assert.Equal($"no such surface: {id}", Cursor(id).Error);
    }

    [Fact]
    public void UnavailableSurfaceIsRefused()
    {
        NewSession(out var id).Surface = new FakeSurface { CursorColumn = 1 };

        Assert.Equal($"surface not available: surface:{id}:right", Cursor($"surface:{id}:right").Error);
    }

    [Fact]
    public void ASlotWithoutASurfaceIsNotAvailable()
    {
        NewSession(out var id);

        Assert.Equal($"surface not available: surface:{id}:left", Cursor("active").Error);
    }

    [Fact]
    public void AnUnrealizedSurfaceIsRefused()
    {
        NewSession(out _).Surface = new FakeSurface { Realized = false, CursorColumn = 1 };

        Assert.Equal("surface not realized", Cursor("active").Error);
    }

    [Fact]
    public void AnUnreadableCursorIsRefused()
    {
        NewSession(out _).Surface = new FakeSurface { CursorColumn = null };

        Assert.Equal("failed to read cursor position", Cursor("active").Error);
    }

    [Fact]
    public void NoActiveSession()
    {
        foreach (var id in _host.Store.Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToArray())
            _host.Store.CloseSession(id);

        Assert.Equal("no active surface", Cursor("active").Error);
    }
}
