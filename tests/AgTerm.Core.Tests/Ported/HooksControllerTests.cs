using AgTerm.Core.Hooks;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Ported;

public sealed class HooksControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agterm-hooks-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHookLauncher _launcher = new();

    public HooksControllerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private HooksController Make() => new(new HooksStore(_dir), _launcher);

    private static ControlEvent Event(ControlEventKind kind) => new()
    {
        Seq = 1, Ts = 1, Kind = kind, Payload = new ControlEventPayload(),
    };

    [Fact]
    public void ReloadOfMissingFileReportsZeroDiagnosticsAndNoHooks()
    {
        var controller = Make();
        Assert.Equal(0, controller.Reload());
        var list = controller.List();
        Assert.Equal(Path.Combine(_dir, "hooks.conf"), list.Path);
        Assert.Empty(list.Hooks);
        Assert.Empty(list.Diagnostics);
    }

    [Fact]
    public void ReloadAppliesHooksAndCountsDiagnostics()
    {
        File.WriteAllText(Path.Combine(_dir, "hooks.conf"), "on status echo hi\non bogus x\n");
        var controller = Make();
        Assert.Equal(1, controller.Reload());
        var list = controller.List();
        var hook = Assert.Single(list.Hooks);
        Assert.Equal("status", hook.Kind);
        Assert.Equal("echo hi", hook.Command);
        Assert.Equal(1, hook.Line);
        var diagnostic = Assert.Single(list.Diagnostics);
        Assert.Equal(2, diagnostic.Line);
    }

    [Fact]
    public void DispatchReachesTheLaunchersOnlyAfterReload()
    {
        File.WriteAllText(Path.Combine(_dir, "hooks.conf"), "on status echo hi\n");
        var controller = Make();
        controller.Dispatch(Event(ControlEventKind.Status));
        Assert.Empty(_launcher.Launches);
        controller.Reload();
        controller.Dispatch(Event(ControlEventKind.Status));
        Assert.Single(_launcher.Launches);
        controller.Dispatch(Event(ControlEventKind.SessionCreated));
        Assert.Single(_launcher.Launches);
    }

    [Fact]
    public void MovingTheConfigDirectoryReloadsFromTheNewFile()
    {
        var other = Path.Combine(_dir, "other");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "hooks.conf"), "on status echo moved\n");
        var controller = Make();
        controller.Reload();
        Assert.Empty(controller.List().Hooks);

        controller.UseConfigDirectory(other);

        Assert.Equal("echo moved", Assert.Single(controller.List().Hooks).Command);
        controller.UseConfigDirectory(other);
        Assert.Equal("echo moved", Assert.Single(controller.List().Hooks).Command);
    }

    [Fact]
    public async Task PostedEventsAreDispatchedInOrderOffTheCallingThread()
    {
        File.WriteAllText(Path.Combine(_dir, "hooks.conf"), "on status echo hi\n");
        var controller = Make();
        controller.Reload();
        controller.Post(Event(ControlEventKind.Status) with { Seq = 1 });
        controller.Post(Event(ControlEventKind.Status) with { Seq = 2 });
        await controller.FlushAsync();
        Assert.Single(_launcher.Launches);
        Assert.Equal(1UL, _launcher.Launches[0].Event.Seq);
        _launcher.Launches[0].OnExit(0);
        Assert.Equal(2, _launcher.Launches.Count);
        Assert.Equal(2UL, _launcher.Launches[1].Event.Seq);
    }

    [Fact]
    public void FailureBannerIsForwardedOncePerHook()
    {
        File.WriteAllText(Path.Combine(_dir, "hooks.conf"), "on status echo hi\n");
        var controller = Make();
        var failures = new List<(string Kind, string Command, string Detail)>();
        controller.HookFailed += (kind, command, detail) => failures.Add((kind, command, detail));
        controller.Reload();
        controller.Dispatch(Event(ControlEventKind.Status));
        _launcher.Launches[0].OnExit(3);
        Assert.Equal([("status", "echo hi", "exit 3")], failures);
    }
}
