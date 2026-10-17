using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm.Tests;

// window.rename reaches the window's title bar through LiveWindowHost.Renamed
public sealed class LiveWindowHostRenameTests : IDisposable
{
    private readonly string _stateDir = Directory.CreateTempSubdirectory("agterm-lwh-rename-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_stateDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Rename_reports_the_new_name_after_it_is_stored()
    {
        var host = new LiveWindowHost(
            Guid.NewGuid(), "window 1", _stateDir, _stateDir,
            new ControlEventRing(), new RecentClosedStore(_stateDir),
            seedIfEmpty: true, launchRestore: false);
        string? reported = null;
        string? storedWhenReported = null;
        host.Renamed += name =>
        {
            reported = name;
            storedWhenReported = host.WindowName;
        };

        host.Rename("dev");

        Assert.Equal("dev", reported);
        Assert.Equal("dev", storedWhenReported);
    }

    [Fact]
    public void The_window_runtime_forwards_rename_to_its_subscribers()
    {
        var host = new LiveWindowHost(
            Guid.NewGuid(), "window 1", _stateDir, _stateDir,
            new ControlEventRing(), new RecentClosedStore(_stateDir),
            seedIfEmpty: true, launchRestore: false);
        ILiveControlRuntime runtime = new WindowControlRuntime(host, "agterm-test-pipe");
        string? reported = null;
        runtime.Renamed += name => reported = name;

        host.Rename("dev");

        Assert.Equal("dev", reported);
    }
}
