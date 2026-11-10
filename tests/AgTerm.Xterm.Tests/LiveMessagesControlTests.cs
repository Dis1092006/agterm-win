using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

// msg.* on the live application host (upstream b0839692, #847, ControlServerMessagesTests): an address
// resolves its session across every open window, a broadcast without --workspace spans them all, and a
// window that closes or is deleted takes its mail.
public sealed class LiveMessagesControlTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-msg-live-").FullName;
    private LiveApplicationHost? _application;

    public void Dispose()
    {
        _application?.Dispose();
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<(LiveApplicationHost Application, LiveWindowHost First, LiveWindowHost Second)> TwoWindowsAsync()
    {
        var application = _application = new LiveApplicationHost(new LiveApplicationHostOptions
        {
            StateDir = _stateDir,
            PipeName = "agterm-msg-live-" + Guid.NewGuid().ToString("N"),
            HomeDirectory = _stateDir,
            WindowController = new ImmediateWindowController(),
        });
        var firstId = application.LaunchWindowIds.Single();
        var created = await application.CreateWindowAsync();
        Assert.True(created.Ok, created.Error);
        var first = application.GetOpenWindow(firstId);
        var second = application.GetOpenWindow(Guid.Parse(created.Result!.Id!));
        application.Start();
        return (application, first, second);
    }

    private static ControlResponse Send(LiveApplicationHost application, Command cmd, ControlArgs? args = null,
                                        string? target = null) =>
        new ControlPipeClient(application.PipeName).Send(new ControlRequest { Cmd = cmd, Target = target, Args = args });

    /// <summary>A new session in the window's first workspace whose main pane is a live surface.</summary>
    private Session LiveSession(LiveWindowHost window)
    {
        Session session = null!;
        window.RunLocked(() =>
        {
            session = window.Store.AddSession(window.Store.Workspaces[0].Id, _stateDir)!;
            session.Surface = new FakeSurface(Guid.NewGuid().ToString("N"));
        });
        return session;
    }

    private static Guid Pane(Session session) => Guid.Parse(session.Surface!.PaneToken!);

    private static string Address(Session session) => MessageAddress.Canonical(session.Id, Pane(session));

    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    [Fact]
    public async Task An_address_and_an_identity_resolve_their_session_in_any_open_window()
    {
        var (application, first, second) = await TwoWindowsAsync();
        var sender = LiveSession(first);
        var target = LiveSession(second);

        var sent = Send(application, Command.MsgSend, new ControlArgs
        {
            Body = "hi", Recipients = [Address(target)], Identity = Address(sender),
        });

        Assert.True(sent.Ok, sent.Error);
        Assert.Equal([Address(target)], sent.Result!.Delivered!);
        var read = Send(application, Command.MsgRead, new ControlArgs { Identity = Address(target) });
        Assert.True(read.Ok, read.Error);
        var message = Assert.Single(read.Result!.Messages!);
        Assert.Equal(Address(sender), message.From);
    }

    [Fact]
    public async Task A_stranger_pane_names_the_session_it_resolved()
    {
        var (application, _, second) = await TwoWindowsAsync();
        var target = LiveSession(second);
        var stranger = Guid.NewGuid();

        var sent = Send(application, Command.MsgRead, new ControlArgs
        {
            Identity = MessageAddress.Canonical(target.Id, stranger),
        });

        Assert.False(sent.Ok);
        Assert.Equal($"pane {stranger:N} is not a live pane of session {Upper(target.Id)}", sent.Error);
    }

    [Fact]
    public async Task A_broadcast_without_a_workspace_reaches_every_open_window()
    {
        var (application, first, second) = await TwoWindowsAsync();
        var near = LiveSession(first);
        var far = LiveSession(second);

        var sent = Send(application, Command.MsgSend, new ControlArgs { Body = "hi", All = true });

        Assert.True(sent.Ok, sent.Error);
        Assert.Contains(Address(near), sent.Result!.Delivered!);
        Assert.Contains(Address(far), sent.Result.Delivered!);
    }

    [Fact]
    public async Task A_workspace_in_another_window_narrows_the_broadcast()
    {
        var (application, first, second) = await TwoWindowsAsync();
        var near = LiveSession(first);
        var far = LiveSession(second);
        Guid workspace = default;
        second.RunLocked(() => workspace = second.Store.Workspaces[0].Id);

        var sent = Send(application, Command.MsgSend, new ControlArgs
        {
            Body = "hi", All = true, Workspace = Upper(workspace),
        });

        Assert.True(sent.Ok, sent.Error);
        Assert.Contains(Address(far), sent.Result!.Delivered!);
        Assert.DoesNotContain(Address(near), sent.Result.Delivered!);
        Assert.Equal($"no such workspace: nope",
            Send(application, Command.MsgSend, new ControlArgs { Body = "hi", All = true, Workspace = "nope" }).Error);
    }

    [Fact]
    public async Task The_message_event_carries_the_recipients_window()
    {
        var (application, _, second) = await TwoWindowsAsync();
        var target = LiveSession(second);
        var bootstrap = Send(application, Command.EventsRead).Result!.Events!;

        var sent = Send(application, Command.MsgSend, new ControlArgs { Body = "hi", Recipients = [Address(target)] });
        var read = Send(application, Command.EventsRead, new ControlArgs
        {
            After = bootstrap.Next.ToString(), Run = Upper(bootstrap.Run), Kinds = ["message"],
        });

        var ev = Assert.Single(read.Result!.Events!.Items);
        Assert.Equal(Upper(second.WindowId), ev.Window);
        Assert.Equal(Upper(target.Id), ev.Session);
        Assert.Equal(target.Surface!.PaneToken, ev.Payload.PaneId);
        Assert.Equal(sent.Result!.MessageId, ev.Payload.MessageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_or_deleting_a_window_disposes_its_mail(bool delete)
    {
        var (application, _, second) = await TwoWindowsAsync();
        var session = LiveSession(second);
        Assert.True(Send(application, Command.MsgSend, new ControlArgs { Body = "hi", Recipients = [Address(session)] }).Ok);
        Assert.Equal(1, MessageBus.Shared.Pending(Pane(session)));

        if (delete)
            Assert.True(Send(application, Command.WindowDelete, target: Upper(second.WindowId)).Ok);
        else
            await application.CloseWindowAsync(second.WindowId);

        Assert.Equal(0, MessageBus.Shared.Pending(Pane(session)));
    }

    private sealed class FakeSurface(string paneToken) : IHeadlessSurface
    {
        public string? PaneToken => paneToken;
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => string.Empty;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }

    private sealed class ImmediateWindowController : ILiveWindowController
    {
        public Task OpenWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ActivateWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CloseWindowAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<LiveWindowPresentation?> CaptureWindowAsync(
            Guid windowId, CancellationToken cancellationToken) => Task.FromResult<LiveWindowPresentation?>(null);
        public Task<int> GetDisplayCountAsync(CancellationToken cancellationToken) => Task.FromResult(1);
        public Task<LiveWindowSize?> ResizeWindowAsync(
            Guid windowId, int width, int height, CancellationToken cancellationToken) => Task.FromResult<LiveWindowSize?>(null);
        public Task<LiveWindowMoveResult> MoveWindowAsync(
            Guid windowId, int x, int y, int? display, CancellationToken cancellationToken) =>
            Task.FromResult(new LiveWindowMoveResult(true, 1));
        public Task ToggleZoomAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ToggleFullscreenAsync(Guid windowId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
