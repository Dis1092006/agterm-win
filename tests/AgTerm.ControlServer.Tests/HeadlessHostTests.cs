using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

/// <summary>
/// End-to-end tests for the headless control host: real requests over a REAL named pipe via
/// ControlPipeClient into the dispatcher and the live AppStore. Each test gets its own host with a
/// unique pipe name and an isolated temp state dir (nothing reads AGTERM_* from the environment —
/// the pipe name and state dir are passed explicitly).
/// </summary>
public sealed class HeadlessHostTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessHostTests()
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

    private ControlResponse Send(ControlRequest request) => _client.Send(request);

    /// <summary>Creates a session over the pipe (creating its workspace by name when needed) and
    /// returns the new session id exactly as it came off the wire.</summary>
    private string NewSession(string workspaceName = "work")
    {
        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionNew,
            Args = new ControlArgs
            {
                WorkspaceName = workspaceName,
                CreateWorkspace = true,
                Cwd = _stateDir,
            },
        });
        Assert.True(response.Ok, response.Error);
        Assert.NotNull(response.Result?.Id);
        return response.Result!.Id!;
    }

    [Fact]
    public void SessionNewReturnsAnUppercaseIdAndTheSessionExists()
    {
        var id = NewSession();

        // the wire id is the canonical uppercase uuid string and resolves in the live store.
        Assert.Equal(id, id.ToUpperInvariant());
        var guid = Guid.Parse(id);
        Assert.NotNull(_host.Store.SessionWithId(guid));
        Assert.Equal(guid, _host.Store.SelectedSessionId);
    }

    [Fact]
    public void TreeRoundTripsOverTheRealPipe()
    {
        var id = NewSession("alpha");

        var response = Send(new ControlRequest { Cmd = Command.Tree });

        Assert.True(response.Ok, response.Error);
        var tree = response.Result?.Tree;
        Assert.NotNull(tree);
        // the host bootstraps the app-launch default workspace; "alpha" is the one this test added.
        var workspace = Assert.Single(tree!.Workspaces, w => w.Name == "alpha");
        Assert.True(workspace.Active);
        var session = Assert.Single(workspace.Sessions);
        Assert.Equal(id, session.Id);
        Assert.True(session.Active);
        Assert.Equal(_stateDir, session.Cwd);
        // headless extras: the quick terminal starts hidden, nothing is zoomed, no dashboard.
        Assert.False(tree.QuickVisible);
        Assert.Null(tree.ZoomedSurface);
        Assert.Null(tree.DashboardMembers);
    }

    [Fact]
    public void SessionStatusPaneRightWithoutSplitNormalizesToLeftInTheTree()
    {
        var id = NewSession();

        var status = Send(new ControlRequest
        {
            Cmd = Command.SessionStatus,
            Target = id,
            Args = new ControlArgs { Status = "blocked", Pane = "right" },
        });
        Assert.True(status.Ok, status.Error);

        var tree = Send(new ControlRequest { Cmd = Command.Tree });
        var session = tree.Result!.Tree!.Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == id);
        Assert.Equal("blocked", session.Status);
        // no split → the stale right tag is normalized to left (the store's promoted-survivor rule).
        Assert.Equal("left", session.StatusPane);
    }

    [Fact]
    public void EventsReadBootstrapThenAfterCursorFlowOverThePipe()
    {
        // bootstrap: no cursor anchors at the tail and intentionally returns no history.
        var bootstrap = Send(new ControlRequest { Cmd = Command.EventsRead });
        Assert.True(bootstrap.Ok, bootstrap.Error);
        var anchor = bootstrap.Result?.Events;
        Assert.NotNull(anchor);
        Assert.Empty(anchor!.Items);

        var id = NewSession();

        var page = Send(new ControlRequest
        {
            Cmd = Command.EventsRead,
            Args = new ControlArgs
            {
                Run = anchor.Run.ToString("D"),
                After = anchor.Next.ToString(),
            },
        });
        Assert.True(page.Ok, page.Error);
        var batch = page.Result?.Events;
        Assert.NotNull(batch);
        Assert.Equal(anchor.Run, batch!.Run);
        Assert.True(batch.Next > anchor.Next);
        var created = Assert.Single(batch.Items, e => e.Kind == ControlEventKind.SessionCreated);
        Assert.Equal(id, created.Session);
        // the host's sink stamps the single window's id (uppercase) on every event.
        var windowTag = _host.WindowId.ToString("D").ToUpperInvariant();
        Assert.All(batch.Items, e => Assert.Equal(windowTag, e.Window));

        // a run mismatch is the loud pinned cursor failure, still carrying the current anchor.
        var stale = Send(new ControlRequest
        {
            Cmd = Command.EventsRead,
            Args = new ControlArgs { Run = Guid.NewGuid().ToString("D"), After = "0" },
        });
        Assert.False(stale.Ok);
        Assert.Equal("event run changed", stale.Error);
        Assert.NotNull(stale.Result?.Events);
    }

    [Fact]
    public void SessionTypeInjectsIntoAnAttachedFakeSurface()
    {
        var id = NewSession();
        var fake = new FakeSurface();
        _host.Store.SessionWithId(Guid.Parse(id))!.Surface = fake;

        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Target = id,
            Args = new ControlArgs { Text = "echo hello\n" },
        });

        Assert.True(response.Ok, response.Error);
        Assert.Equal(id, response.Result?.Id);
        Assert.Equal(new[] { "echo hello\n" }, fake.Injected);
    }

    [Fact]
    public void SessionTypeWithoutASurfaceAndWithoutSelectIsThePinnedError()
    {
        var id = NewSession();

        var response = Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Target = id,
            Args = new ControlArgs { Text = "x" },
        });

        Assert.False(response.Ok);
        Assert.Equal("session not realized; use select", response.Error);
    }

    [Fact]
    public void SessionTextReturnsTheFakeScreenText()
    {
        var id = NewSession();
        var fake = new FakeSurface { ScreenText = "PS> dir\nfile.txt" };
        _host.Store.SessionWithId(Guid.Parse(id))!.Surface = fake;

        var response = Send(new ControlRequest { Cmd = Command.SessionText, Target = id });

        Assert.True(response.Ok, response.Error);
        Assert.Equal("PS> dir\nfile.txt", response.Result?.Text);

        // --lines trims to the last N lines, like the upstream surface read.
        var lastLine = Send(new ControlRequest
        {
            Cmd = Command.SessionText,
            Target = id,
            Args = new ControlArgs { Lines = 1 },
        });
        Assert.True(lastLine.Ok, lastLine.Error);
        Assert.Equal("file.txt", lastLine.Result?.Text);
    }

    [Fact]
    public void SelectAllThenCopyReadsBackTheFakeScreen()
    {
        var id = NewSession();
        var fake = new FakeSurface { ScreenText = "selected content" };
        _host.Store.SessionWithId(Guid.Parse(id))!.Surface = fake;

        // copy with no selection is the pinned upstream error.
        var empty = Send(new ControlRequest { Cmd = Command.SessionCopy, Target = id });
        Assert.False(empty.Ok);
        Assert.Equal("no selection", empty.Error);

        var selectAll = Send(new ControlRequest { Cmd = Command.SessionSelectAll, Target = id });
        Assert.True(selectAll.Ok, selectAll.Error);
        var copy = Send(new ControlRequest { Cmd = Command.SessionCopy, Target = id });
        Assert.True(copy.Ok, copy.Error);
        Assert.Equal("selected content", copy.Result?.Text);
    }

    [Fact]
    public void WindowZoomReturnsThePinnedHeadlessError()
    {
        var response = Send(new ControlRequest { Cmd = Command.WindowZoom });

        Assert.False(response.Ok);
        Assert.Equal("window management is not available in the headless host", response.Error);
    }

    [Fact]
    public void WindowListReportsTheSingleAlwaysOpenWindowAndRenameSticks()
    {
        var list = Send(new ControlRequest { Cmd = Command.WindowList });
        Assert.True(list.Ok, list.Error);
        var node = Assert.Single(list.Result!.Windows!);
        Assert.Equal(_host.WindowId.ToString("D").ToUpperInvariant(), node.Id);
        Assert.Equal("main", node.Name);
        Assert.True(node.Open);
        Assert.True(node.Active);
        Assert.True(node.SidebarVisible);

        var rename = Send(new ControlRequest
        {
            Cmd = Command.WindowRename,
            Args = new ControlArgs { Name = "renamed" },
        });
        Assert.True(rename.Ok, rename.Error);
        Assert.Equal(node.Id, rename.Result?.Id);
        Assert.Equal("renamed", _host.WindowName);

        // window.select resolving to the one window is ok+id — it is already frontmost.
        var select = Send(new ControlRequest { Cmd = Command.WindowSelect, Target = node.Id });
        Assert.True(select.Ok, select.Error);
        Assert.Equal(node.Id, select.Result?.Id);
    }

    [Fact]
    public void UnresolvedTargetsKeepThePinnedResolutionErrors()
    {
        NewSession();

        var session = Send(new ControlRequest { Cmd = Command.SessionSelect, Target = "zz" });
        Assert.False(session.Ok);
        Assert.Equal("no such session: zz", session.Error);

        var window = Send(new ControlRequest
        {
            Cmd = Command.Tree,
            Args = new ControlArgs { Window = "zz" },
        });
        Assert.False(window.Ok);
        Assert.Equal("no such window: zz", window.Error);
    }

    // O0.5 — headless host (no SurfaceRealizer): session model has null Surface, pinned wire
    // errors on session.text / selected session.type, and the session node exists in the tree
    // without live-surface fields.
    [Fact]
    public void No_surface_realizer_preserves_pinned_errors_and_omits_live_tree_fields()
    {
        var id = NewSession();
        var guid = Guid.Parse(id);

        // model surface is null — headless default, no SurfaceRealizer configured
        var modelSession = _host.Store.SessionWithId(guid);
        Assert.NotNull(modelSession);
        Assert.Null(modelSession!.Surface);

        var text = Send(new ControlRequest { Cmd = Command.SessionText, Target = id });
        Assert.False(text.Ok);
        Assert.Equal("session not realized", text.Error);

        var type = Send(new ControlRequest
        {
            Cmd = Command.SessionType,
            Target = id,
            Args = new ControlArgs { Text = "x", Select = true },
        });
        Assert.False(type.Ok);
        Assert.Equal("session not realized", type.Error);

        var tree = Send(new ControlRequest { Cmd = Command.Tree });
        Assert.True(tree.Ok, tree.Error);
        var node = tree.Result!.Tree!.Workspaces
            .SelectMany(w => w.Sessions)
            .Single(s => s.Id == id);
        Assert.Null(node.Title);
        Assert.Null(node.FontSize);
        var surface = Assert.Single(node.Surfaces!);
        Assert.Equal($"surface:{id}:left", surface.Id);
        Assert.Equal("left", surface.Kind);
        Assert.True(surface.Active);
        Assert.True(surface.Visible);
    }

}
