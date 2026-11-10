using AgTerm.Cli;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;
using WireCommand = AgTerm.Core.Protocol.Command;

namespace AgTerm.Cli.Tests.Ported;

// Port of agtermctlKitTests/SessionWaitCommandsTests.swift (upstream fff13ef2, #765). The Swift
// SessionWaitDependencies (send / sleep / now) map onto CliEnvironment (Send / Sleep / Now). A
// refused connect (ECONNREFUSED) maps onto a busy pipe, a missing socket (ENOENT) onto a missing one.
public class SessionWaitCommandsTests
{
    private static readonly Guid Run = Guid.Parse("CBB5E3D0-7A9B-4C96-9EA2-18B14380DDB1");
    private static readonly Guid Worker = Guid.Parse("AAAA1111-0000-4000-8000-000000000001");
    private static readonly Guid Other = Guid.Parse("BBBB2222-0000-4000-8000-000000000002");
    private static readonly Guid Window = Guid.Parse("CCCC3333-0000-4000-8000-000000000003");

    private sealed class FakeApp
    {
        public List<Func<ControlResponse>> EventPages { get; } = new();
        public Dictionary<string, ControlTree> Trees { get; }
        public List<ControlWindowNode> Windows { get; }
        public List<ControlRequest> Requests { get; } = new();
        public List<TimeSpan> Sleeps { get; } = new();
        public double Clock { get; private set; }
        public StringWriter Out { get; } = new();
        public StringWriter Error { get; } = new();
        public List<(double Seconds, Action Fire)> Watchdogs { get; } = new();

        public FakeApp(Dictionary<string, ControlTree> trees, List<ControlWindowNode> windows)
        {
            Trees = trees;
            Windows = windows;
        }

        public CliEnvironment Environment => new()
        {
            GetEnv = _ => null,
            Out = Out,
            Error = Error,
            Send = TestReply.Replying((_, request) =>
            {
                Requests.Add(request);
                switch (request.Cmd)
                {
                    case WireCommand.WindowList:
                        return new ControlResponse { Ok = true, Result = new ControlResult { Windows = Windows.ToArray() } };
                    case WireCommand.Tree:
                        return Trees.TryGetValue(request.Args?.Window ?? "", out var tree)
                            ? new ControlResponse { Ok = true, Result = new ControlResult { Tree = tree } }
                            : new ControlResponse { Ok = false, Error = "window not open" };
                    default:
                        if (EventPages.Count == 0) return Page(Array.Empty<ControlEvent>());
                        var next = EventPages[0];
                        EventPages.RemoveAt(0);
                        return next();
                }
            }),
            Sleep = delay =>
            {
                Sleeps.Add(delay);
                Clock += delay.TotalSeconds;
            },
            Now = () => Clock,
            StartWatchdog = (seconds, fire) =>
            {
                Watchdogs.Add((seconds, fire));
                return new NoopDisposable();
            },
        };

        public static ControlResponse Page(ControlEvent[] items, ulong next = 10) => new()
        {
            Ok = true,
            Result = new ControlResult { Events = new ControlEventBatch { Run = Run, Next = next, Items = items } },
        };

        public void Pages(params ControlEvent[][] pages)
        {
            foreach (var page in pages) EventPages.Add(() => Page(page));
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private static ControlSessionNode Session(Guid id, string? status = null, bool active = false) => new()
    {
        Id = id.ToString().ToUpperInvariant(), Name = "s", Cwd = "C:\\tmp", Active = active, Status = status,
    };

    private static ControlTree Tree(params ControlSessionNode[] sessions) => new()
    {
        Workspaces = new[]
        {
            new ControlWorkspaceNode { Id = Guid.NewGuid().ToString(), Name = "w", Active = true, Sessions = sessions },
        },
    };

    private static FakeApp App(string? workerStatus = null, Guid? selected = null) => new(
        new Dictionary<string, ControlTree>
        {
            [Window.ToString().ToUpperInvariant()] = Tree(
                Session(Worker, workerStatus, selected == Worker),
                Session(Other, "completed", selected == Other)),
        },
        new List<ControlWindowNode>
        {
            new() { Id = Window.ToString().ToUpperInvariant(), Name = "main", Open = true, Active = true },
        });

    private static ControlEvent StatusEvent(ulong seq, Guid session, string status) => new()
    {
        Seq = seq, Ts = 1, Kind = ControlEventKind.Status, Session = session.ToString().ToUpperInvariant(),
        Payload = new ControlEventPayload { Name = "s", Status = status },
    };

    private static ControlEvent ClosedEvent(ulong seq, Guid session) => new()
    {
        Seq = seq, Ts = 1, Kind = ControlEventKind.SessionClosed, Session = session.ToString().ToUpperInvariant(),
    };

    private static SessionWaitCommands.WaitCommand Command(params string[] argv) =>
        (SessionWaitCommands.WaitCommand)Agtermctl.ParseAsRoot(new[] { "session", "wait" }.Concat(argv).ToArray());

    [Fact]
    public void CurrentStatusMatchesWithoutReadingEvents()
    {
        var app = App("completed");
        var outcome = Command("--target", Worker.ToString(), "--until", "completed").Wait(app.Environment);
        Assert.Equal(SessionWaitOutcome.Matched(Worker, AgentStatus.Completed), outcome);
        Assert.Equal(new[] { WireCommand.EventsRead, WireCommand.WindowList, WireCommand.Tree },
            app.Requests.Select(request => request.Cmd));
    }

    [Fact]
    public void AnchorIsTakenBeforeTheTreeIsRead()
    {
        var app = App("active");
        app.EventPages.Add(() => FakeApp.Page(Array.Empty<ControlEvent>(), next: 7));
        app.EventPages.Add(() => FakeApp.Page(new[] { StatusEvent(8, Worker, "completed") }, next: 8));
        Command("--target", Worker.ToString(), "--until", "completed").Wait(app.Environment);
        Assert.Equal(new[] { WireCommand.EventsRead, WireCommand.WindowList, WireCommand.Tree, WireCommand.EventsRead },
            app.Requests.Select(request => request.Cmd));
        Assert.Null(app.Requests.First().Args?.After);
        Assert.Equal("7", app.Requests.Last().Args?.After);
        Assert.Equal(Run.ToString().ToUpperInvariant(), app.Requests.Last().Args?.Run);
        Assert.Equal(new[] { "status", "session.closed" }, app.Requests.Last().Args?.Kinds);
    }

    [Fact]
    public void AbsentStatusReadsAsIdle()
    {
        var app = App(null);
        var outcome = Command("--target", Worker.ToString(), "--until", "idle").Wait(app.Environment);
        Assert.Equal(SessionWaitOutcome.Matched(Worker, AgentStatus.Idle), outcome);
    }

    [Fact]
    public void LaterEventForTheSessionMatchesAndOthersAreIgnored()
    {
        var app = App("active");
        app.Pages(
            Array.Empty<ControlEvent>(),
            new[] { StatusEvent(11, Other, "blocked"), StatusEvent(12, Worker, "active") },
            new[] { StatusEvent(13, Worker, "blocked") });
        var outcome = Command("--target", Worker.ToString(), "--until", "completed", "--until", "blocked")
            .Wait(app.Environment);
        Assert.Equal(SessionWaitOutcome.Matched(Worker, AgentStatus.Blocked), outcome);
    }

    [Fact]
    public void RepeatedStatusEventCountsAsReached()
    {
        var app = App("active");
        app.Pages(Array.Empty<ControlEvent>(), new[] { StatusEvent(11, Worker, "completed") });
        var outcome = Command("--target", Worker.ToString(), "--until", "completed").Wait(app.Environment);
        Assert.Equal(SessionWaitOutcome.Matched(Worker, AgentStatus.Completed), outcome);
    }

    [Fact]
    public void SessionClosedEndsTheWait()
    {
        var app = App("active");
        app.Pages(Array.Empty<ControlEvent>(), new[] { ClosedEvent(11, Other), ClosedEvent(12, Worker) });
        var outcome = Command("--target", Worker.ToString(), "--until", "completed").Wait(app.Environment);
        Assert.Equal(SessionWaitOutcome.Closed, outcome);
    }

    [Fact]
    public void DeadlinePassesWithoutAMatch()
    {
        var app = App("active");
        var outcome = Command("--target", Worker.ToString(), "--until", "completed", "--timeout", "1")
            .Wait(app.Environment);
        Assert.Equal(SessionWaitOutcome.TimedOut, outcome);
        Assert.All(app.Sleeps, delay => Assert.True(delay <= SessionWaitCommands.WaitCommand.PollDelay));
        Assert.Equal(1, app.Clock, 6);
    }

    [Fact]
    public void RunChangeFailsTheWait()
    {
        var app = App("active");
        app.Pages(Array.Empty<ControlEvent>());
        app.EventPages.Add(() => new ControlResponse { Ok = false, Error = "event run changed" });
        var error = Assert.Throws<CliValidationException>(
            () => Command("--target", Worker.ToString(), "--until", "completed").Wait(app.Environment));
        Assert.Equal("event run changed", error.Message);
    }

    [Fact]
    public void RefusedConnectRetriesWithinTheDeadline()
    {
        var app = App("active");
        app.Pages(Array.Empty<ControlEvent>());
        app.EventPages.Add(() => throw new PipeClientException("refused", PipeConnectFailure.Busy));
        app.Pages(new[] { StatusEvent(11, Worker, "completed") });
        var outcome = Command("--target", Worker.ToString(), "--until", "completed", "--timeout", "5")
            .Wait(app.Environment);
        Assert.Equal(SessionWaitOutcome.Matched(Worker, AgentStatus.Completed), outcome);
        Assert.Equal(new[] { TimeSpan.FromSeconds(EventStreamState.FirstRetryDelay) }, app.Sleeps);
    }

    [Fact]
    public void MissingSocketFailsAtOnce()
    {
        var app = App("active");
        app.Pages(Array.Empty<ControlEvent>());
        app.EventPages.Add(() => throw new PipeClientException("no pipe", PipeConnectFailure.Missing));
        Assert.Throws<PipeClientException>(
            () => Command("--target", Worker.ToString(), "--until", "completed").Wait(app.Environment));
    }

    [Fact]
    public void ActiveResolvesToTheFrontmostWindowsSelection()
    {
        var app = App("completed", selected: Worker);
        Assert.Equal(SessionWaitOutcome.Matched(Worker, AgentStatus.Completed),
            Command("--until", "completed").Wait(app.Environment));
    }

    [Fact]
    public void PrefixResolvesInABackgroundWindow()
    {
        var app = App("active", selected: Worker);
        var background = Guid.NewGuid().ToString().ToUpperInvariant();
        var hidden = Guid.Parse("EEEE5555-0000-4000-8000-000000000005");
        app.Windows.Add(new ControlWindowNode { Id = background, Name = "back", Open = true, Active = false });
        app.Trees[background] = Tree(Session(hidden, "blocked", active: true));
        Assert.Equal(SessionWaitOutcome.Matched(hidden, AgentStatus.Blocked),
            Command("--target", "eeee", "--until", "blocked").Wait(app.Environment));
    }

    [Fact]
    public void PrefixMatchingSessionsInTwoWindowsIsAmbiguous()
    {
        var app = App("completed");
        var background = Guid.NewGuid().ToString().ToUpperInvariant();
        var twin = Guid.Parse("AAAA1111-0000-4000-8000-000000000009");
        app.Windows.Add(new ControlWindowNode { Id = background, Name = "back", Open = true, Active = false });
        app.Trees[background] = Tree(Session(twin, "completed"));
        var error = Assert.Throws<CliValidationException>(
            () => Command("--target", "aaaa", "--until", "completed").Wait(app.Environment));
        Assert.StartsWith("ambiguous session prefix 'aaaa'", error.Message);
    }

    [Theory]
    [InlineData("DDDD")]
    [InlineData("")]
    public void UnresolvedTargetFails(string target)
    {
        var app = App("completed");
        var error = Assert.Throws<CliValidationException>(
            () => Command("--target", target, "--until", "completed").Wait(app.Environment));
        Assert.Equal($"no such session: {target}", error.Message);
    }

    [Fact]
    public void ClosedWindowsAreNotRead()
    {
        var app = App("completed");
        app.Windows.Add(new ControlWindowNode { Id = Guid.NewGuid().ToString(), Name = "closed", Open = false });
        Command("--target", Worker.ToString(), "--until", "completed").Wait(app.Environment);
        Assert.Equal(new[] { Window.ToString().ToUpperInvariant() },
            app.Requests.Where(request => request.Cmd == WireCommand.Tree).Select(request => request.Args?.Window));
    }

    [Fact]
    public void OutputIsTheStatusOrOneJsonObject()
    {
        Assert.Equal("completed", Command("--until", "idle").Output(Worker, AgentStatus.Completed));
        Assert.Equal($"{{\"id\":\"{Worker.ToString().ToUpperInvariant()}\",\"status\":\"completed\"}}",
            Command("--until", "idle", "--json").Output(Worker, AgentStatus.Completed));
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void RejectsInvalidArguments(string[] argv)
    {
        Assert.ThrowsAny<Exception>(() => Command(argv));
    }

    public static IEnumerable<object[]> InvalidArguments() => new[]
    {
        Array.Empty<string>(),
        new[] { "--until", "done" },
        new[] { "--until", "completed", "--timeout", "0" },
        new[] { "--until", "completed", "--timeout", "-1" },
        new[] { "--until", "completed", "--timeout", "inf" },
    }.Select(argv => new object[] { argv });

    [Fact]
    public void ValidationMessagesMatchUpstream()
    {
        Assert.Equal("provide at least one --until status",
            Assert.Throws<CliValidationException>(() => Command()).Message);
        Assert.Equal("invalid status: done (expected idle, active, completed or blocked)",
            Assert.Throws<CliValidationException>(() => Command("--until", "done")).Message);
        Assert.Equal("--timeout must be a positive number of seconds",
            Assert.Throws<CliValidationException>(() => Command("--until", "idle", "--timeout", "0")).Message);
    }

    [Fact]
    public void ExecutePrintsTheMatchedStatusAndExitsZero()
    {
        var app = App("completed");
        var code = Command("--target", Worker.ToString(), "--until", "completed").Execute(app.Environment);
        Assert.Equal(0, code);
        Assert.Equal("completed" + Environment.NewLine, app.Out.ToString());
    }

    [Fact]
    public void ExecuteExitsTwoWhenTheSessionLeavesTheTree()
    {
        var app = App("active");
        app.Pages(Array.Empty<ControlEvent>(), new[] { ClosedEvent(11, Worker) });
        var code = Command("--target", Worker.ToString(), "--until", "completed").Execute(app.Environment);
        Assert.Equal(2, code);
        Assert.Equal("session closed" + Environment.NewLine, app.Error.ToString());
        Assert.Equal("", app.Out.ToString());
    }

    [Fact]
    public void ExecuteExits124OnTimeout()
    {
        var app = App("active");
        var code = Command("--target", Worker.ToString(), "--until", "completed", "--timeout", "1")
            .Execute(app.Environment);
        Assert.Equal(124, code);
        Assert.Equal("timed out" + Environment.NewLine, app.Error.ToString());
    }

    [Fact]
    public void ExecuteArmsTheWatchdogForTheTimeoutOnly()
    {
        var app = App("completed");
        Command("--target", Worker.ToString(), "--until", "completed", "--timeout", "30").Execute(app.Environment);
        Assert.Equal(30, Assert.Single(app.Watchdogs).Seconds);

        var untimed = App("completed");
        Command("--target", Worker.ToString(), "--until", "completed").Execute(untimed.Environment);
        Assert.Empty(untimed.Watchdogs);
    }

    [Fact]
    public void WatchdogFireReportsTheTimeoutAndExits124()
    {
        var reported = new StringWriter();
        var exited = new List<int>();
        var fired = SessionWaitCommands.WaitCommand.OnWatchdogFired(reported, exited.Add);
        fired();
        Assert.Equal("timed out" + Environment.NewLine, reported.ToString());
        Assert.Equal(new[] { 124 }, exited);
    }

    [Fact]
    public void RealWatchdogFiresWhileTheCallerIsBlockedAndCancelStopsIt()
    {
        using var fired = new ManualResetEventSlim();
        using (CliEnvironmentWatchdog.Start(0.05, fired.Set))
        {
            Assert.True(fired.Wait(TimeSpan.FromSeconds(5)));
        }

        using var cancelled = new ManualResetEventSlim();
        CliEnvironmentWatchdog.Start(0.05, cancelled.Set).Dispose();
        Assert.False(cancelled.Wait(TimeSpan.FromMilliseconds(300)));
    }
}
