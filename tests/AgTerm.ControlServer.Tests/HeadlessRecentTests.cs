using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

/// <summary>recent.list, recent.open and recent.clear over the pipe against the headless host
/// (upstream 6b564749, #744).</summary>
public sealed class HeadlessRecentTests : IDisposable
{
    private readonly string _stateDir;
    private readonly HeadlessHost _host;
    private readonly ControlPipeClient _client;

    public HeadlessRecentTests()
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

    private ControlResponse Send(Command cmd, string? target = null, string? window = null) =>
        _client.Send(new ControlRequest
        {
            Cmd = cmd, Target = target, Args = window is null ? null : new ControlArgs { Window = window },
        });

    private Guid CloseANewSession()
    {
        var created = Send(Command.SessionNew);
        Assert.True(created.Ok, created.Error);
        var id = Guid.Parse(created.Result!.Id!);
        var closed = Send(Command.SessionClose, target: id.ToString("D"));
        Assert.True(closed.Ok, closed.Error);
        return id;
    }

    [Fact]
    public void AFreshHostHasNothingToListOrOpen()
    {
        var list = Send(Command.RecentList);
        var open = Send(Command.RecentOpen);

        Assert.True(list.Ok, list.Error);
        Assert.Empty(list.Result?.Recent ?? Array.Empty<ControlRecentItem>());
        Assert.False(open.Ok);
        Assert.Equal("no recently closed items", open.Error);
    }

    [Fact]
    public void ListAndClearRefuseATargetOrWindow()
    {
        Assert.Equal("recent.list takes no target or --window", Send(Command.RecentList, target: "x").Error);
        Assert.Equal("recent.clear takes no target or --window", Send(Command.RecentClear, window: "w").Error);
    }

    [Fact]
    public void AClosedSessionIsListedAndOpenReopensAndConsumesIt()
    {
        var closed = CloseANewSession();

        var list = Send(Command.RecentList);
        Assert.True(list.Ok, list.Error);
        var item = Assert.Single(list.Result!.Recent!);
        Assert.Equal("session", item.Kind);
        Assert.Equal(new[] { closed.ToString("D").ToUpperInvariant() }, item.SessionIds);

        var open = Send(Command.RecentOpen);

        Assert.True(open.Ok, open.Error);
        Assert.Equal(item.Id, open.Result!.RecentOpen!.Item.Id);
        Assert.Equal(_host.WindowId.ToString("D").ToUpperInvariant(), open.Result.RecentOpen.Window);
        Assert.True(open.Result.RecentOpen.Complete);
        Assert.NotNull(_host.Store.SessionWithId(closed));
        Assert.Empty(Send(Command.RecentList).Result?.Recent ?? Array.Empty<ControlRecentItem>());
    }

    [Fact]
    public void OpenResolvesAnEntryByPrefixAndRefusesAnUnknownOne()
    {
        CloseANewSession();
        var item = Send(Command.RecentList).Result!.Recent![0];

        var unknown = Send(Command.RecentOpen, target: "zzzz");
        var byPrefix = Send(Command.RecentOpen, target: item.Id[..12]);

        Assert.False(unknown.Ok);
        Assert.Equal("no such recent item: zzzz", unknown.Error);
        Assert.True(byPrefix.Ok, byPrefix.Error);
    }

    [Fact]
    public void ClearEmptiesTheList()
    {
        CloseANewSession();

        var clear = Send(Command.RecentClear);

        Assert.True(clear.Ok, clear.Error);
        Assert.Null(clear.Result);
        Assert.Empty(Send(Command.RecentList).Result?.Recent ?? Array.Empty<ControlRecentItem>());
    }

    [Fact]
    public void AnUnknownWindowRestoresNothing()
    {
        CloseANewSession();

        var open = Send(Command.RecentOpen, window: "nope");

        Assert.False(open.Ok);
        Assert.Equal("no such window: nope", open.Error);
        Assert.Single(Send(Command.RecentList).Result!.Recent!);
    }
}
