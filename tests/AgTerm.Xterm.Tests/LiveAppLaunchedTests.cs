using AgTerm.Core.Protocol;
using Xunit;

namespace AgTerm.Xterm.Tests;

// app.launched (upstream 8c5ac7a0): once per run, when the control pipe starts accepting connections.
public sealed class LiveAppLaunchedTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-live-launched-").FullName;

    public void Dispose()
    {
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

    private LiveApplicationHost CreateApplication() =>
        new(new LiveApplicationHostOptions
        {
            StateDir = _stateDir,
            HomeDirectory = _stateDir,
            PipeName = "agterm-live-launched-" + Guid.NewGuid().ToString("N"),
        });

    private static ControlEvent[] Launched(LiveApplicationHost application)
    {
        var run = application.Ring.Read(null).Batch.Run;
        return application.Ring.Read(new ControlEventCursor(run, 0), new HashSet<ControlEventKind> { ControlEventKind.AppLaunched })
            .Batch.Items;
    }

    [Fact]
    public void StartingThePipeAnnouncesTheLaunchOnceWithNoWindow()
    {
        using var application = CreateApplication();
        Assert.Empty(Launched(application));

        application.Start();
        application.Start();

        var launched = Assert.Single(Launched(application));
        Assert.Null(launched.Window);
        Assert.Null(launched.Session);
        Assert.Equal(new ControlEventPayload(), launched.Payload);
    }
}
