using AgTerm.ControlServer;
using AgTerm.Core.Keymap;
using AgTerm.Core.Protocol;
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
    private SettingsWindow? _settingsWindow;
    private QuickTerminalWindow? _quickWindow;

    internal void OpenSettings()
    {
        var applicationHost = _applicationHost ?? throw new InvalidOperationException("Application host is not initialized.");
        _settingsWindow ??= new SettingsWindow(
            applicationHost.Settings,
            () => _windows.Values.First().ReadSettingsShortcutsAsync(),
            async () =>
            {
                var response = await Task.Run(() => new ControlPipeClient(applicationHost.PipeName)
                    .Send(new ControlRequest { Cmd = Command.KeymapReload }));
                if (!response.Ok) throw new IOException(response.Error);
            }, () => _quickWindow?.GlobalHotkeyError);
        _settingsWindow.Closed -= OnSettingsClosed;
        _settingsWindow.Closed += OnSettingsClosed;
        _settingsWindow.ShowGeneral();
    }

    private void OnSettingsClosed(object sender, WindowEventArgs args) => _settingsWindow = null;

    private MainWindow? ActiveMainWindow =>
        _applicationHost?.ActiveWindowId is { } id && _windows.TryGetValue(id, out var window) ? window : null;

    internal void DispatchQuickBuiltin(BuiltinAction action) => ActiveMainWindow?.ExecuteBuiltinHotkey(action);
    internal bool DispatchQuickSwitcher(SessionSwitcherKey key) => ActiveMainWindow?.HandleQuickSwitcherKey(key) ?? false;
    internal void ShowQuickKeybindings() => ActiveMainWindow?.ShowQuickKeybindings();

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
            SocketPath = ControlPipeName.ResolveSocketPath(),
            WindowController = this,
        });
        _applicationHost = applicationHost;
        _quickWindow = new QuickTerminalWindow(applicationHost, notice =>
        {
            var window = applicationHost.ActiveWindowId is { } id && _windows.TryGetValue(id, out var active)
                ? active : _windows.Values.FirstOrDefault();
            window?.ReportQuickFailure(notice);
        }, (command, selection) =>
        {
            if (applicationHost.ActiveWindowId is not { } id || !_windows.TryGetValue(id, out var window))
                throw new InvalidOperationException("No open window for the quick-terminal custom command.");
            return window.ExecuteQuickCustomCommandAsync(command, selection);
        });
        await _quickWindow.InitializeAsync();
        foreach (var windowId in applicationHost.LaunchWindowIds)
        {
            CreateWindow(windowId).ActivateWindow();
        }

        await Task.WhenAll(_windows.Values.Select(window => window.Ready));
        if (_quickWindow.GlobalHotkeyError is { } hotkeyError)
            _windows.Values.First().ReportQuickFailure(new("Global hotkey unavailable", hotkeyError));
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
        if (_windows.Count == 0)
        {
            _settingsWindow?.Close();
            _quickWindow?.Dispose();
            _quickWindow = null;
        }
    }

    /// <summary>A second launch on the same state directory handed its activation here; AppInstance
    /// raises it on a background thread.</summary>
    internal void ActivateFrontmostWindow() =>
        _dispatcherQueue?.TryEnqueue(() =>
        {
            var window = _applicationHost?.FrontmostWindowId is { } id && _windows.TryGetValue(id, out var frontmost)
                ? frontmost
                : _windows.Values.FirstOrDefault();
            window?.ActivateWindow();
        });

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
