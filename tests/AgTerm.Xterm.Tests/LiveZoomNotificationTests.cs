using System.Collections.Concurrent;
using AgTerm.ControlServer;
using AgTerm.Core.Protocol;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

public sealed class LiveZoomNotificationTests : IDisposable
{
    private readonly string _stateDir =
        Directory.CreateTempSubdirectory("agterm-zoom-notification-").FullName;
    private readonly LiveControlHost _host;

    public LiveZoomNotificationTests()
    {
        _host = new LiveControlHost(new LiveControlHostOptions
        {
            StateDir = _stateDir,
            ConfigDir = _stateDir,
            PipeName = "agterm-zoom-notification-" + Guid.NewGuid().ToString("N"),
            OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
        });
    }

    public void Dispose()
    {
        _host.Dispose();
        try { Directory.Delete(_stateDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Rapid_zoom_flips_do_not_deliver_the_newer_state_concurrently_or_out_of_order()
    {
        var observed = new ConcurrentQueue<bool>();
        using var firstEntered = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var falseDelivered = new ManualResetEventSlim();
        _host.ZoomActiveChanged += active =>
        {
            if (active)
            {
                firstEntered.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }

            observed.Enqueue(active);
            if (!active)
                falseDelivered.Set();
        };

        _host.Host.RunLocked(() =>
            _host.Host.Actions.SetSurfaceZoom(null, null, ControlToggleMode.Toggle));
        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5)));

        _host.Host.RunLocked(() =>
            _host.Host.Actions.SetSurfaceZoom(null, null, ControlToggleMode.Toggle));

        Assert.False(falseDelivered.Wait(TimeSpan.FromMilliseconds(250)));
        releaseFirst.Set();
        Assert.True(falseDelivered.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal([true, false], observed);
        Assert.False(_host.ZoomActive);
    }

    [Fact]
    public void Back_to_back_zoom_edges_are_both_delivered_when_worker_starts_later()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-zoom-deferred-").FullName;
        Action? scheduled = null;
        var scheduledCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-zoom-deferred-" + Guid.NewGuid().ToString("N"),
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
                NotificationScheduler = action =>
                {
                    Assert.Null(scheduled);
                    scheduled = action;
                    return scheduledCompletion.Task;
                },
            });
            var observed = new List<bool>();
            host.ZoomActiveChanged += observed.Add;

            host.Host.RunLocked(() =>
            {
                host.Host.Actions.SetSurfaceZoom(null, null, ControlToggleMode.Toggle);
                host.Host.Actions.SetSurfaceZoom(null, null, ControlToggleMode.Toggle);
            });

            Assert.NotNull(scheduled);
            Assert.Empty(observed);
            scheduled();
            scheduledCompletion.TrySetResult();

            Assert.Equal([true, false], observed);
            Assert.False(host.ZoomActive);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Immediate_scheduler_falls_back_to_deferred_delivery_without_losing_edges()
    {
        var stateDir = Directory.CreateTempSubdirectory("agterm-zoom-immediate-").FullName;
        try
        {
            using var host = new LiveControlHost(new LiveControlHostOptions
            {
                StateDir = stateDir,
                ConfigDir = stateDir,
                PipeName = "agterm-zoom-immediate-" + Guid.NewGuid().ToString("N"),
                OpenSurface = (_, _) => Task.FromResult<IHeadlessSurface>(new FakeSurface()),
                NotificationScheduler = action =>
                {
                    action();
                    return Task.CompletedTask;
                },
            });
            var observed = new ConcurrentQueue<bool>();
            using var bothDelivered = new CountdownEvent(2);
            host.ZoomActiveChanged += active =>
            {
                observed.Enqueue(active);
                bothDelivered.Signal();
            };

            host.Host.RunLocked(() =>
            {
                host.Host.Actions.SetSurfaceZoom(null, null, ControlToggleMode.Toggle);
                host.Host.Actions.SetSurfaceZoom(null, null, ControlToggleMode.Toggle);
            });

            Assert.True(bothDelivered.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal([true, false], observed);
            Assert.False(host.ZoomActive);
        }
        finally
        {
            try { Directory.Delete(stateDir, recursive: true); } catch (IOException) { }
        }
    }

    private sealed class FakeSurface : IHeadlessSurface
    {
        public string? PaneToken => "fake";
        public bool Inject(string text) => true;
        public string? ReadScreenText(bool all, int? lines) => string.Empty;
        public string? SelectedText => null;
        public void SelectAll() { }
        public bool Paste() => true;
        public bool PerformBindingAction(string action) => true;
        public void PromoteToPrimaryPane() { }
        public void Teardown() { }
    }
}