using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

/// <summary>msg.send, msg.read and msg.ack over the pipe against the headless host (upstream b0839692, #847):
/// address resolution, broadcast scope, identity agreement and mailbox disposal.</summary>
public sealed class HeadlessMessagesTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessMessagesTests()
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

    private ControlResponse Send(Command cmd, ControlArgs? args = null, string? target = null) =>
        _client.Send(new ControlRequest { Cmd = cmd, Target = target, Args = args });

    private static string Token() => Guid.NewGuid().ToString("N");

    /// <summary>A new session in the given workspace whose main pane is a live surface.</summary>
    private Session LiveSession(string? workspace = null)
    {
        var created = Send(Command.SessionNew, workspace is null ? null : new ControlArgs { Workspace = workspace });
        Assert.True(created.Ok, created.Error);
        Session session = null!;
        _host.RunLocked(() =>
        {
            session = _host.Store.SessionWithId(Guid.Parse(created.Result!.Id!))!;
            session.Surface = new FakeSurface { PaneToken = Token() };
        });
        return session;
    }

    private static Guid Pane(ITerminalSurface? surface) => Guid.Parse(surface!.PaneToken!);

    private static string Address(Session session, ITerminalSurface? surface = null) =>
        MessageAddress.Canonical(session.Id, Pane(surface ?? session.Surface));

    private static string Upper(Guid id) => id.ToString("D").ToUpperInvariant();

    [Fact]
    public void BroadcastReachesEveryLivePaneAndSkipsTheSender()
    {
        var sender = LiveSession();
        var near = LiveSession();

        var response = Send(Command.MsgSend, new ControlArgs { Body = "hi", All = true, Identity = Address(sender) });

        Assert.True(response.Ok, response.Error);
        Assert.Contains(Address(near), response.Result!.Delivered!);
        Assert.DoesNotContain(Address(sender), response.Result.Delivered!);
        Assert.Equal(0, MessageBus.Shared.Pending(Pane(sender.Surface)));
    }

    [Fact]
    public void AnAnonymousBroadcastReachesEveryPane()
    {
        var panes = new[] { LiveSession(), LiveSession() };

        var response = Send(Command.MsgSend, new ControlArgs { Body = "hi", All = true });

        Assert.Subset(response.Result!.Delivered!.ToHashSet(), panes.Select(s => Address(s)).ToHashSet());
    }

    [Fact]
    public void WorkspaceNarrowsTheBroadcast()
    {
        var created = Send(Command.WorkspaceNew, new ControlArgs { Name = "inside" });
        Assert.True(created.Ok, created.Error);
        var inside = LiveSession(created.Result!.Id);
        LiveSession(Send(Command.WorkspaceNew, new ControlArgs { Name = "elsewhere" }).Result!.Id);

        var response = Send(Command.MsgSend, new ControlArgs { Workspace = created.Result.Id, Body = "hi", All = true });

        Assert.Equal([Address(inside)], response.Result!.Delivered!);
    }

    [Fact]
    public void AnUnknownWorkspaceFailsTheBroadcast()
    {
        var response = Send(Command.MsgSend, new ControlArgs { Workspace = "nope", Body = "hi", All = true });

        Assert.False(response.Ok);
        Assert.Contains("nope", response.Error);
    }

    [Fact]
    public void AliasesOfOnePaneAreDeliveredOnce()
    {
        var session = LiveSession();

        var response = Send(Command.MsgSend, new ControlArgs
        {
            Body = "hi", Recipients = [$"{Upper(session.Id)}:left", Address(session), $"{session.Id:D}:{Pane(session.Surface):D}"],
        });

        Assert.Equal([Address(session)], response.Result!.Delivered!);
        Assert.Equal(1, MessageBus.Shared.Pending(Pane(session.Surface)));
    }

    [Fact]
    public void TheScratchPaneIsARecipientAndAnIdentity()
    {
        var session = LiveSession();
        var scratch = new FakeSurface { PaneToken = Token() };
        _host.RunLocked(() => session.ScratchSurface = scratch);

        var sent = Send(Command.MsgSend, new ControlArgs { Body = "hi", Recipients = [$"{Upper(session.Id)}:scratch"] });
        var read = Send(Command.MsgRead, new ControlArgs { Identity = Address(session, scratch) });

        Assert.Equal([Address(session, scratch)], sent.Result!.Delivered!);
        Assert.Equal(["hi"], read.Result!.Messages!.Select(m => m.Body));
    }

    [Fact]
    public void ASplitWithoutASurfaceIsNotALivePane()
    {
        var session = LiveSession();
        var stranger = Guid.NewGuid();
        _host.RunLocked(() => _host.Store.SetSplitVisibility(session.Id, shown: true));

        var sent = Send(Command.MsgSend, new ControlArgs { Body = "hi", Recipients = [$"{Upper(session.Id)}:right"] });
        var asStranger = Send(Command.MsgSend, new ControlArgs
        {
            Body = "hi", All = true, Identity = MessageAddress.Canonical(session.Id, stranger),
        });

        Assert.Equal([new MessageRejection($"{Upper(session.Id)}:right", MessageRejectionReason.UnknownPane)], sent.Result!.Rejected!);
        Assert.Equal($"pane {stranger:N} is not a live pane of session {Upper(session.Id)}", asStranger.Error);
    }

    [Fact]
    public void AnUnrealizedSurfaceIsNotALivePane()
    {
        var session = LiveSession();
        _host.RunLocked(() => ((FakeSurface)session.Surface!).Realized = false);

        var sent = Send(Command.MsgSend, new ControlArgs { Body = "hi", Recipients = [Address(session)] });

        Assert.Empty(sent.Result!.Delivered!);
        Assert.Equal(MessageRejectionReason.UnknownPane, Assert.Single(sent.Result.Rejected!).Reason);
    }

    [Fact]
    public void AnIdentityNamingAnotherSessionsPaneIsRefusedOnEveryCommand()
    {
        var mine = LiveSession();
        var theirs = LiveSession();
        var forged = MessageAddress.Canonical(mine.Id, Pane(theirs.Surface));
        var expected = $"pane {Pane(theirs.Surface):N} is not a live pane of session {Upper(mine.Id)}";

        foreach (var cmd in new[] { Command.MsgSend, Command.MsgRead, Command.MsgAck })
        {
            var response = Send(cmd, new ControlArgs { Body = "hi", All = true, Identity = forged, Ids = [Upper(Guid.NewGuid())] });
            Assert.Equal(expected, response.Error);
        }
        Assert.Equal(0, MessageBus.Shared.Pending(Pane(mine.Surface)));
    }

    [Fact]
    public void AStaleIdentityFailsAndNeverSendsAnonymously()
    {
        var target = LiveSession();
        var stale = MessageAddress.Canonical(Guid.NewGuid(), Guid.NewGuid());

        var response = Send(Command.MsgSend, new ControlArgs { Body = "hi", Recipients = [Address(target)], Identity = stale });

        Assert.False(response.Ok);
        Assert.Equal(0, MessageBus.Shared.Pending(Pane(target.Surface)));
    }

    [Fact]
    public void ReadAckAndTheTreeCountWorkEndToEnd()
    {
        var session = LiveSession();
        var me = Address(session);
        var first = Send(Command.MsgSend, new ControlArgs { Body = "one", Recipients = [me] }).Result!.MessageId!;
        Send(Command.MsgSend, new ControlArgs { Body = "two", Recipients = [me] });

        var tree = Send(Command.Tree);
        var node = tree.Result!.Tree!.Workspaces.SelectMany(w => w.Sessions).First(s => s.Id == Upper(session.Id));
        var acked = Send(Command.MsgAck, new ControlArgs { Identity = me, Ids = [first] });
        var rest = Send(Command.MsgRead, new ControlArgs { Identity = me, Ack = true });

        Assert.Equal(2, node.Surfaces!.First(s => s.Kind == "left").PendingMessages);
        Assert.Equal([first], acked.Result!.Acked!);
        Assert.Equal(["two"], rest.Result!.Messages!.Select(m => m.Body));
        Assert.Equal(0, MessageBus.Shared.Pending(Pane(session.Surface)));
    }

    [Fact]
    public void ClosingASessionDisposesItsMail()
    {
        var session = LiveSession();
        Send(Command.MsgSend, new ControlArgs { Body = "hi", Recipients = [Address(session)] });
        Assert.Equal(1, MessageBus.Shared.Pending(Pane(session.Surface)));

        Assert.True(Send(Command.SessionClose, target: Upper(session.Id)).Ok);

        Assert.Equal(0, MessageBus.Shared.Pending(Pane(session.Surface)));
    }

    [Fact]
    public void TheMessageEventCarriesWindowWorkspaceAndPane()
    {
        var session = LiveSession();
        var bootstrap = Send(Command.EventsRead).Result!.Events!;

        var sent = Send(Command.MsgSend, new ControlArgs { Body = "hi", Recipients = [Address(session)], Identity = null });
        var read = Send(Command.EventsRead, new ControlArgs
        {
            After = bootstrap.Next.ToString(), Run = bootstrap.Run.ToString("D").ToUpperInvariant(), Kinds = ["message"],
        });

        var ev = Assert.Single(read.Result!.Events!.Items);
        Assert.Equal(ControlEventKind.Message, ev.Kind);
        Assert.Equal(Upper(session.Id), ev.Session);
        Assert.NotNull(ev.Workspace);
        Assert.NotNull(ev.Window);
        Assert.Equal(session.Surface!.PaneToken, ev.Payload.PaneId);
        Assert.Equal(sent.Result!.MessageId, ev.Payload.MessageId);
        Assert.Null(ev.Payload.From);
    }
}
