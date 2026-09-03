using AgTerm.ControlServer;
using AgTerm.Xterm;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace AgTerm.App;

public partial class App : Application, ILiveWindowController
{
    private readonly Dictionary<Guid, MainWindow> _windows = new();
    private LiveApplicationHost? _applicationHost;
    private DispatcherQueue? _dispatcherQueue;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        var applicationHost = new LiveApplicationHost(new LiveApplicationHostOptions
        {
            PipeName = ControlPipeName.Resolve(),
            WindowController = this,
        });
        _applicationHost = applicationHost;
        foreach (var windowId in applicationHost.LaunchWindowIds)
        {
            CreateWindow(windowId).ActivateWindow();
        }

        await Task.WhenAll(_windows.Values.Select(window => window.Ready));
        applicationHost.Start();
    }

    public async Task OpenWindowAsync(Guid windowId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MainWindow? window = null;
        try
        {
            await RunOnUiAsync(async () =>
            {
                window = CreateWindow(windowId);
                window.ActivateWindow();
                await window.Ready;
            }, CancellationToken.None);
        }
        catch
        {
            if (window is not null)
            {
                await RunOnUiAsync(
                    async () => await window.CloseFailedOpenAsync(),
                    CancellationToken.None);
            }
            throw;
        }
    }

    public Task ActivateWindowAsync(Guid windowId, CancellationToken cancellationToken) =>
        RunOnUiAsync(() =>
        {
            if (_windows.TryGetValue(windowId, out var window))
                window.ActivateWindow();
            return Task.CompletedTask;
        }, cancellationToken);

    public Task CloseWindowAsync(Guid windowId, CancellationToken cancellationToken) =>
        RunOnUiAsync(async () =>
        {
            if (_windows.TryGetValue(windowId, out var window))
                await window.RequestManagedCloseAsync().WaitAsync(cancellationToken);
            else if (_applicationHost is { } applicationHost)
                await applicationHost.CloseWindowAsync(windowId, cancellationToken);
        }, cancellationToken);

    public Task<LiveWindowPresentation?> CaptureWindowAsync(
        Guid windowId,
        CancellationToken cancellationToken) =>
        RunOnUiAsync(
            () => Task.FromResult(_windows.TryGetValue(windowId, out var window)
                ? window.CaptureWindowPresentation()
                : null),
            cancellationToken);

    public Task<int> GetDisplayCountAsync(CancellationToken cancellationToken) =>
        RunOnUiAsync(
            () => Task.FromResult(DisplayArea.FindAll().Count),
            cancellationToken);

    public Task ResizeWindowAsync(
        Guid windowId,
        int width,
        int height,
        CancellationToken cancellationToken) =>
        RunOnUiAsync(() =>
        {
            if (_windows.TryGetValue(windowId, out var window))
                window.ResizeWindow(width, height);
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<LiveWindowMoveResult> MoveWindowAsync(
        Guid windowId,
        int x,
        int y,
        int? display,
        CancellationToken cancellationToken) =>
        RunOnUiAsync(
            () => Task.FromResult(_windows.TryGetValue(windowId, out var window)
                ? window.MoveWindow(x, y, display)
                : new LiveWindowMoveResult(false, 0)),
            cancellationToken);

    public Task ToggleZoomAsync(Guid windowId, CancellationToken cancellationToken) =>
        RunOnUiAsync(() =>
        {
            if (_windows.TryGetValue(windowId, out var window))
                window.ToggleWindowZoom();
            return Task.CompletedTask;
        }, cancellationToken);

    public Task ToggleFullscreenAsync(Guid windowId, CancellationToken cancellationToken) =>
        RunOnUiAsync(() =>
        {
            if (_windows.TryGetValue(windowId, out var window))
                window.ToggleWindowFullscreen();
            return Task.CompletedTask;
        }, cancellationToken);

    private MainWindow CreateWindow(Guid windowId)
    {
        if (_windows.TryGetValue(windowId, out var existing))
            return existing;
        var applicationHost = _applicationHost
            ?? throw new InvalidOperationException("Application host is not initialized.");
        var window = new MainWindow(applicationHost, windowId);
        _windows.Add(windowId, window);
        window.Closed += (_, _) => OnWindowClosed(windowId);
        return window;
    }

    private void OnWindowClosed(Guid windowId)
    {
        _windows.Remove(windowId);
    }

    private Task RunOnUiAsync(Func<Task> work, CancellationToken cancellationToken) =>
        RunOnUiAsync(async () =>
        {
            await work();
            return true;
        }, cancellationToken);

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dispatcher = _dispatcherQueue
            ?? throw new InvalidOperationException("Application dispatcher is not initialized.");
        if (dispatcher.HasThreadAccess)
            return work();

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));
        if (!dispatcher.TryEnqueue(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(await work());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                registration.Dispose();
            }
        }))
        {
            registration.Dispose();
            completion.TrySetException(new InvalidOperationException(
                "Failed to enqueue live window operation."));
        }
        return completion.Task;
    }
}
