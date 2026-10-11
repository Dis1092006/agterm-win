using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

public sealed class HeadlessAskTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessAskTests()
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

    private static ControlRequest OpenAsk(string style = "terminal", string? target = null, string? pane = null) => new()
    {
        Cmd = Command.AskOpen,
        Target = target,
        Args = new ControlArgs
        {
            Title = "Continue?",
            Buttons = new[] { new ControlAskButton { Id = "yes", Label = "Yes" }, new ControlAskButton { Id = "no", Label = "No" } },
            Style = style,
            Pane = pane,
        },
    };

    private static ControlRequest OpenPick() => new()
    {
        Cmd = Command.PickOpen,
        Args = new ControlArgs { Items = new[] { new ControlPickItem { Id = "a", Label = "A" } } },
    };

    private ControlAskResult? Result(string id) => Send(new ControlRequest { Cmd = Command.AskResult, Target = id }).Result?.Ask;

    private ControlTree Tree() => Send(new ControlRequest { Cmd = Command.Tree }).Result!.Tree!;

    private ControlSessionAsk? SessionAsk(string sessionId) =>
        Tree().Workspaces.SelectMany(w => w.Sessions).Single(s => s.Id == sessionId).Ask;

    private string SelectedSession() =>
        Tree().Workspaces.SelectMany(w => w.Sessions).Single(s => s.Active).Id;

    [Fact]
    public void ATerminalAskOpensOnTheSelectedSessionAndShowsOnItsNode()
    {
        var session = SelectedSession();
        var opened = Send(OpenAsk());

        Assert.True(opened.Ok, opened.Error);
        var id = opened.Result!.Id!;
        Assert.Null(opened.Result.Pane);
        Assert.Equal(ControlAskResult.Pending(), Result(id));
        Assert.Equal(new ControlSessionAsk { Id = id }, SessionAsk(session));
        Assert.Null(Tree().AskPending);
        Assert.Equal("ask already pending", Send(OpenAsk()).Error);
    }

    [Fact]
    public void ATargetedTerminalAskOnABackgroundSessionKeepsTheSelection()
    {
        var background = SelectedSession();
        var selected = NewSession();

        var opened = Send(OpenAsk(target: background));

        Assert.True(opened.Ok, opened.Error);
        Assert.Equal(selected, SelectedSession());
        Assert.Equal(opened.Result!.Id, SessionAsk(background)!.Id);
    }

    [Fact]
    public void ATerminalPaneAskNeedsThePaneLaidOutAndEchoesItsRole()
    {
        var session = SelectedSession();
        Assert.Equal("pane not visible", Send(OpenAsk(pane: "right")).Error);

        Assert.True(Send(new ControlRequest { Cmd = Command.SessionSplit, Target = session, Args = new ControlArgs { Mode = "on" } }).Ok);
        var opened = Send(OpenAsk(pane: "right"));

        Assert.True(opened.Ok, opened.Error);
        Assert.Equal("right", opened.Result!.Pane);
        Assert.Equal(new ControlSessionAsk { Id = opened.Result.Id!, Pane = "right" }, SessionAsk(session));
    }

    [Fact]
    public void AGuiAskTakesTheWindowSlotThatPicksShare()
    {
        var opened = Send(OpenAsk("gui"));
        Assert.True(opened.Ok, opened.Error);
        var id = opened.Result!.Id!;
        Assert.Equal(id, Tree().AskPending);
        Assert.Null(Tree().PickPending);
        Assert.Equal("ask already pending", Send(OpenPick()).Error);
        Assert.Equal("ask already pending", Send(OpenAsk("gui")).Error);

        Assert.True(Send(new ControlRequest { Cmd = Command.AskCancel, Target = id }).Ok);
        Assert.True(Send(OpenPick()).Ok);
        Assert.Equal("pick already pending", Send(OpenAsk("gui")).Error);
    }

    [Fact]
    public void ATerminalAskCoexistsWithAGuiAskAndAPick()
    {
        var gui = Send(OpenAsk("gui")).Result!.Id!;
        Assert.True(Send(OpenAsk()).Ok);
        Assert.True(Send(new ControlRequest { Cmd = Command.AskCancel, Target = gui }).Ok);
        Assert.True(Send(OpenPick()).Ok);
        Assert.True(Send(OpenAsk(target: NewSession())).Ok);
    }

    [Fact]
    public void AnAnchoredGuiAskNeedsTheSelectedSession()
    {
        var hidden = SelectedSession();
        NewSession();

        Assert.Equal("session not visible", Send(OpenAsk("gui", target: hidden)).Error);
        Assert.Null(Tree().AskPending);
    }

    [Fact]
    public void AnAnchoredGuiAskEchoesItsPane()
    {
        var opened = Send(OpenAsk("gui", target: SelectedSession(), pane: "left"));
        Assert.True(opened.Ok, opened.Error);
        Assert.Equal("left", opened.Result!.Pane);
    }

    [Fact]
    public void AnAnchoredGuiAskIsCancelledWhenItsSessionIsDeselected()
    {
        var anchored = SelectedSession();
        var other = NewSession();
        Assert.True(Send(new ControlRequest { Cmd = Command.SessionSelect, Target = anchored }).Ok);
        var id = Send(OpenAsk("gui", target: anchored)).Result!.Id!;

        Assert.True(Send(new ControlRequest { Cmd = Command.SessionSelect, Target = other }).Ok);

        Assert.Equal(ControlAskResult.Cancelled(), Result(id));
        Assert.Null(Tree().AskPending);
    }

    [Fact]
    public void APaneAnchoredGuiAskIsCancelledWhenItsPaneIsNoLongerLaidOut()
    {
        var session = SelectedSession();
        Assert.True(Send(new ControlRequest { Cmd = Command.SessionSplit, Target = session, Args = new ControlArgs { Mode = "on" } }).Ok);
        var id = Send(OpenAsk("gui", target: session, pane: "right")).Result!.Id!;
        Assert.True(Send(new ControlRequest { Cmd = Command.SessionFocus, Target = session, Args = new ControlArgs { Pane = "left" } }).Ok);
        Assert.Equal(id, Tree().AskPending);

        Assert.True(Send(new ControlRequest { Cmd = Command.SessionSplit, Target = session, Args = new ControlArgs { Mode = "off" } }).Ok);

        Assert.Equal(ControlAskResult.Cancelled(), Result(id));
        Assert.Null(Tree().AskPending);
    }

    [Fact]
    public void AnUnanchoredGuiAskSurvivesASelectionChange()
    {
        var first = SelectedSession();
        NewSession();
        var id = Send(OpenAsk("gui")).Result!.Id!;

        Assert.True(Send(new ControlRequest { Cmd = Command.SessionSelect, Target = first }).Ok);

        Assert.Equal(ControlAskResult.Pending(), Result(id));
    }

    [Fact]
    public void APendingGuiAskRefusesCoversButAllowsDismissals()
    {
        var session = NewSession();
        Send(OpenAsk("gui"));

        Assert.Equal("ask pending", Send(new ControlRequest { Cmd = Command.Quick, Args = new ControlArgs { Mode = "show" } }).Error);
        Assert.True(Send(new ControlRequest { Cmd = Command.Quick, Args = new ControlArgs { Mode = "hide" } }).Ok);
        Assert.Equal("ask pending", Send(new ControlRequest { Cmd = Command.SurfaceZoom, Args = new ControlArgs { Mode = "show" } }).Error);
        Assert.True(Send(new ControlRequest { Cmd = Command.SurfaceZoom, Args = new ControlArgs { Mode = "hide" } }).Ok);
        Assert.Equal("ask pending", Send(new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Targets = new[] { session } } }).Error);
        Assert.True(Send(new ControlRequest { Cmd = Command.Dashboard, Args = new ControlArgs { Close = true } }).Ok);
    }

    [Fact]
    public void APendingGuiAskRefusesSearchNavigationButAllowsClose()
    {
        var session = NewSession();
        _host.Store.SessionWithId(Guid.Parse(session))!.Surface = new FakeSurface { ScreenText = "needle" };
        Send(OpenAsk("gui"));

        Assert.Equal("ask pending", Send(new ControlRequest { Cmd = Command.SessionSearch, Target = session, Args = new ControlArgs { Text = "needle" } }).Error);
        Assert.Equal("ask pending", Send(new ControlRequest { Cmd = Command.SessionSearch, Target = session, Args = new ControlArgs { To = "next" } }).Error);
        Assert.True(Send(new ControlRequest { Cmd = Command.SessionSearch, Target = session, Args = new ControlArgs { To = "close" } }).Ok);
    }

    [Fact]
    public void ATerminalAskGatesNothingAtWindowLevel()
    {
        Send(OpenAsk());
        Assert.True(Send(new ControlRequest { Cmd = Command.Quick, Args = new ControlArgs { Mode = "show" } }).Ok);
    }

    [Fact]
    public void ClosingTheSessionCancelsItsAsk()
    {
        var doomed = SelectedSession();
        NewSession();
        var id = Send(OpenAsk(target: doomed)).Result!.Id!;

        Assert.True(Send(new ControlRequest { Cmd = Command.SessionClose, Target = doomed }).Ok);

        Assert.Equal(ControlAskResult.Cancelled(), Result(id));
    }

    [Fact]
    public void CancellingAnAnsweredAskLeavesTheNextPendingAskAlone()
    {
        var first = Send(OpenAsk("gui")).Result!.Id!;
        Assert.True(_host.Actions.Pick.ResolveAsk(first, ControlAskResult.Answered("yes", "Yes", 0)));
        var next = Send(OpenAsk("gui")).Result!.Id!;

        Assert.True(Send(new ControlRequest { Cmd = Command.AskCancel, Target = first }).Ok);

        Assert.Equal(ControlAskResult.Answered("yes", "Yes", 0), Result(first));
        Assert.Equal(next, Tree().AskPending);
    }

    [Fact]
    public void AnUnknownAskIdIsAnError()
    {
        Assert.Equal("unknown ask: nope", Send(new ControlRequest { Cmd = Command.AskResult, Target = "nope" }).Error);
        Assert.Equal("unknown ask: nope", Send(new ControlRequest { Cmd = Command.AskCancel, Target = "nope" }).Error);
    }

    [Fact]
    public void TheHostCanAnswerAndEscapeForTheUser()
    {
        var gui = Send(OpenAsk("gui")).Result!.Id!;
        Assert.True(_host.Actions.Pick.EscapeAsk(gui));
        Assert.Equal(ControlAskResult.Escaped(), Result(gui));

        var session = SelectedSession();
        var terminal = Send(OpenAsk()).Result!.Id!;
        _host.RunLocked(() => _host.Store.ResolveAsk(Guid.Parse(session), terminal, ControlAskResult.Answered("no", "No", 1)));
        Assert.Equal(ControlAskResult.Answered("no", "No", 1), Result(terminal));
        Assert.Null(SessionAsk(session));
    }

    [Fact]
    public void AHostThatCannotDrawTerminalAsksRefusesThemButKeepsGuiAsks()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-ask-").FullName;
        try
        {
            using var host = new HeadlessHost(new HeadlessHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
                TerminalAskSupported = false,
            });
            host.Start();
            var client = new ControlPipeClient(host.PipeName);

            Assert.Equal("terminal ask is not supported yet, use --style gui", client.Send(OpenAsk()).Error);
            Assert.True(client.Send(OpenAsk("gui")).Ok);
        }
        finally
        {
            Directory.Delete(stateDir, recursive: true);
        }
    }
}
