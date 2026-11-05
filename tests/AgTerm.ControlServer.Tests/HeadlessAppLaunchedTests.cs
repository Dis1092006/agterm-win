using AgTerm.ControlServer;
using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.ControlServer.Tests;

// app.launched (upstream 8c5ac7a0): once per run, when the control pipe starts accepting connections.
public sealed class HeadlessAppLaunchedTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-launched-").FullName;
    private readonly HeadlessHost _host;

    public HeadlessAppLaunchedTests() =>
        _host = new HeadlessHost(new HeadlessHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-test-" + Guid.NewGuid().ToString("N"),
        });

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

    private ControlEvent[] Launched()
    {
        var run = _host.Ring.Read(null).Batch.Run;
        return _host.Ring.Read(new ControlEventCursor(run, 0), new HashSet<ControlEventKind> { ControlEventKind.AppLaunched })
            .Batch.Items;
    }

    [Fact]
    public void NothingIsAnnouncedBeforeThePipeStarts() => Assert.Empty(Launched());

    [Fact]
    public void StartingThePipeAnnouncesTheLaunchWithNoWindowOrSession()
    {
        _host.Start();

        var launched = Assert.Single(Launched());
        Assert.Null(launched.Window);
        Assert.Null(launched.Workspace);
        Assert.Null(launched.Session);
        Assert.Equal(new ControlEventPayload(), launched.Payload);
    }

    [Fact]
    public void ARepeatedStartAndARestartNeverAnnounceAgain()
    {
        _host.Start();
        _host.Start();
        _host.Stop();
        _host.Start();

        Assert.Single(Launched());
    }
}
