using System.Runtime.InteropServices;
using AgTerm.ControlServer;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using AgTerm.Terminal;
using AgTerm.Xterm;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace AgTerm.App;

/// <summary>Application-owned tool window. Neither its HWND nor its page belongs to a saved window.</summary>
internal sealed class QuickTerminalWindow : Window, IDisposable
{
    private readonly LiveApplicationHost _application;
    private readonly Action<LiveFailureNotice> _report;
    private readonly Func<CustomCommand, string, Task> _runCustom;
    private readonly IntPtr _hwnd;
    private readonly GlobalHotkey _hotkey;
    private readonly UISettings _appearance = new();
    private readonly SemaphoreSlim _pageGate = new(1, 1);
    private WebView2? _view;
    private XtermPage? _page;
    private XtermPaneSurface? _surface;
    private double? _appliedBaseFontSize;
    private HotkeyRouter _router = new(AgTerm.Core.Keymap.Keymap.Empty);
    private AgTerm.Core.Keymap.Keymap _keymap = AgTerm.Core.Keymap.Keymap.Empty;
    private long _generation;
    private bool _shown;
    private bool _zoomed;
    private bool _disposed;
    private IntPtr _previousForeground;
    private IntPtr _monitor;
    private IDisposable? _settingsSubscription;

    public QuickTerminalWindow(LiveApplicationHost application, Action<LiveFailureNotice> report,
        Func<CustomCommand, string, Task> runCustom)
    {
        _application = application;
        _report = report;
        _runCustom = runCustom;
        Title = "agterm quick terminal";
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.IsShownInSwitchers = false;
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        SetWindowLongPtr(_hwnd, -20, (GetWindowLongPtr(_hwnd, -20) & ~0x40000L) | 0x80);
        _hotkey = new(_hwnd, () => Observe(_application.ToggleQuickAsync(true)),
            error =>
            {
                if (error is not null) Report("Global hotkey unavailable", error);
                GlobalHotkeyError = error;
            }, _ => DispatcherQueue.TryEnqueue(() =>
            {
                try { if (_shown) PositionPanel(); }
                catch (Exception ex) { Report("Quick terminal placement failed", ex.Message); }
            }));
        Activated += (_, args) =>
        {
            if (!_disposed && _shown)
                _application.Quick.SetKeyOwnership(args.WindowActivationState != WindowActivationState.Deactivated);
        };
        AppWindow.Closing += (_, args) =>
        {
            if (_disposed) return;
            args.Cancel = true;
            _application.Quick.Hide();
        };
        application.Quick.Changed += OnQuickChanged;
        _appearance.ColorValuesChanged += OnAppearanceChanged;
        application.ConfigureQuickTerminal(new LiveControlHostOptions
        {
            OpenSurface = OpenSurfaceAsync,
            SocketPath = application.SocketPath,
            ReadClipboard = ReadClipboard,
            BaseFontSize = Settings.FontSize ?? 14,
            ShellIntegrationDirectory = ShellIntegration.ResolveScriptDirectory(),
            FailureReported = notice => DispatcherQueue.TryEnqueue(() => _report(notice)),
            Log = message => System.Diagnostics.Trace.TraceError(message),
        });
    }

    public string? GlobalHotkeyError { get; private set; }
    private AppSettings Settings => _application.Settings.State.Snapshot.Settings;

    public async Task InitializeAsync()
    {
        _settingsSubscription = await _application.Settings.SubscribeAsync((_, kind) =>
            RunOnUiAsync(() => ApplySettingsAsync(kind is SettingsApplyKind.Edit or SettingsApplyKind.Reload)));
        _application.KeymapReloaded += OnKeymapReloaded;
        var directory = Settings.ConfigDirectory ?? PersistenceStore.DefaultDirectory;
        await ApplyKeymapAsync(new KeymapStore(directory).Load().Keymap);
    }

    private void OnKeymapReloaded(AgTerm.Core.Keymap.Keymap keymap) =>
        Observe(RunOnUiAsync(() => ApplyKeymapAsync(keymap)));

    public async Task ApplyKeymapAsync(AgTerm.Core.Keymap.Keymap keymap)
    {
        _hotkey.Apply(keymap.GlobalHotkey);
        _keymap = keymap;
        _router = new(keymap);
        _generation++;
        if (_page is not null) await PublishChordsAsync();
    }

    private Task PublishChordsAsync() => _page!.SetHotkeyManifestAsync(SettingsShortcuts.AddToManifest(
        _keymap, _router.CreateManifest(_generation,
            action => WindowCommandRouter.ActionImplementations.GetValueOrDefault(action))));

    private void OnQuickChanged() => DispatcherQueue.TryEnqueue(() => Observe(SynchronizeAsync()));
    private void OnAppearanceChanged(UISettings _, object args) =>
        DispatcherQueue.TryEnqueue(() => Observe(ApplySettingsAsync()));

    private async Task SynchronizeAsync()
    {
        if (_disposed) return;
        var quick = _application.Quick;
        if (!quick.Visible)
        {
            if (!_shown) return;
            var restore = GetForegroundWindow() == _hwnd;
            _shown = false;
            AppWindow.Hide();
            if (restore && _previousForeground != IntPtr.Zero && IsWindow(_previousForeground)
                && !SetForegroundWindow(_previousForeground))
                QuickPanelFocus.RestoreDenied(message => System.Diagnostics.Trace.TraceWarning(message));
            return;
        }
        if (!_shown)
        {
            _previousForeground = GetForegroundWindow();
            if (!GetCursorPos(out var cursor))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            _monitor = MonitorFromPoint(cursor, 2);
            _shown = true;
            PositionPanel();
            Activate();
            SetForegroundWindow(_hwnd);
            QuickPanelFocus.ActivationCompleted(quick, GetForegroundWindow() == _hwnd,
                message => System.Diagnostics.Trace.TraceWarning(message),
                message => Report("Quick terminal focus", message));
            await ApplySettingsAsync();
            await _application.EnsureQuickSurfaceAsync();
            if (_shown && quick.HoldsKey && _surface is { } surface)
            {
                _view!.Focus(FocusState.Programmatic);
                await surface.Pane.Terminal.FocusAsync();
            }
        }
        else if (_zoomed != quick.IsZoomed) PositionPanel();
    }

    private void PositionPanel()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(_monitor, ref info))
        {
            _monitor = MonitorFromWindow(_hwnd, 2);
            if (!GetMonitorInfo(_monitor, ref info))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        var result = GetDpiForMonitor(_monitor, 0, out var dpiX, out _);
        if (result != 0) Marshal.ThrowExceptionForHR(result);
        _zoomed = _application.Quick.IsZoomed;
        var rect = info.Work;
        var frame = QuickPanelGeometry.Frame(new(rect.Left, rect.Top,
            rect.Right - rect.Left, rect.Bottom - rect.Top), dpiX / 96.0, _zoomed);
        AppWindow.MoveAndResize(new RectInt32(frame.X, frame.Y, frame.Width, frame.Height));
    }

    private Task<IHeadlessSurface> OpenSurfaceAsync(SurfaceOpenRequest request, CancellationToken token) =>
        RunOnUiAsync<IHeadlessSurface>(async () =>
        {
            await _pageGate.WaitAsync(token);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_page is null)
                {
                    var view = new WebView2();
                    _view = view;
                    Content = view;
                    var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                        string.Empty, WebViewProfile.Directory(), new CoreWebView2EnvironmentOptions());
                    token.ThrowIfCancellationRequested();
                    await view.EnsureCoreWebView2Async(environment);
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    view.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    view.CoreWebView2.ProcessFailed += (_, args) => DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_disposed || !ReferenceEquals(_view, view)) return;
                        Report("Quick terminal renderer failed", args.ProcessFailedKind.ToString());
                        _application.Quick.ShellExited();
                        ResetPage();
                    });
                    var page = new XtermPage(new WinRtScriptHost(view.CoreWebView2));
                    await page.InitializeAsync().WaitAsync(token);
                    _page = page;
                    page.BuiltinHotkeyPressed += hit => DispatcherQueue.TryEnqueue(() => OnChord(hit));
                    page.CustomCommandPressed += hit => DispatcherQueue.TryEnqueue(() => Observe(OnCustomAsync(hit)));
                    page.KeyboardShortcutsRequested += () => DispatcherQueue.TryEnqueue(() =>
                        ((App)Application.Current).ShowQuickKeybindings());
                    page.SwitcherKeyPressed += key => DispatcherQueue.TryEnqueue(() =>
                    {
                        var active = ((App)Application.Current).DispatchQuickSwitcher(key);
                        Observe(page.SetSwitcherActiveAsync(active));
                    });
                    page.CopyRequested += (_, text) =>
                    {
                        try
                        {
                            var data = new DataPackage();
                            data.SetText(text);
                            Clipboard.SetContent(data);
                        }
                        catch (Exception ex) { Report("Quick terminal copy failed", ex.Message); }
                    };
                    page.PasteRequested += _ => Observe(Task.Run(() => _surface?.PerformBindingAction("paste_from_clipboard")));
                    await PublishChordsAsync();
                }
                await ApplySettingsAsync();
                var surface = await XtermPaneSurface.OpenAsync(_page,
                    request.Options, token);
                _surface = surface;
                _appliedBaseFontSize = request.Options.BaseFontSize;
                surface.Pane.Terminal.UserKeystroke += _ => Observe(_application.NoteQuickUserActivityAsync());
                await _page.ApplyLayoutAsync(new TerminalLayout.Single(1, surface.PaneToken!, "Quick terminal"));
                return surface;
            }
            catch
            {
                ResetPage();
                throw;
            }
            finally { _pageGate.Release(); }
        });

    private void ResetPage()
    {
        _surface?.Teardown();
        _surface = null;
        _appliedBaseFontSize = null;
        _page = null;
        _view?.Close();
        _view = null;
        Content = null;
    }

    private LiveThemePresentation Presentation(bool preservePaneFontSizes = true) => LiveThemeSettings.Resolve(
        Settings, ThemeCatalog.Default,
        _appearance.GetColorValue(UIColorType.Background) is { } color && color.R + color.G + color.B < 384,
        preservePaneFontSizes);

    private Task ApplySettingsAsync() => ApplySettingsAsync(resetFontSize: false);

    private async Task ApplySettingsAsync(bool resetFontSize)
    {
        if (_page is null || _disposed) return;
        await _page.ApplyRendererSettingsAsync(Presentation(preservePaneFontSizes: !resetFontSize).RendererSettings);
        if (_surface is { } surface && !surface.Pane.Exited.IsCompleted)
        {
            var fontSize = Settings.FontSize ?? 14;
            if (resetFontSize || _appliedBaseFontSize != fontSize)
            {
                _appliedBaseFontSize = fontSize;
                await Task.Run(() => surface.ApplyBaseFontSize(fontSize));
            }
        }
    }

    private void OnChord(BuiltinHotkeyEvent hit)
    {
        if (hit.Generation != _generation || !_application.Quick.HoldsKey) return;
        if (SettingsShortcuts.Opens(_keymap, hit.Chord))
        {
            ((App)Application.Current).OpenSettings();
            return;
        }
        var quick = _application.Quick;
        switch (_router.Handle(hit.Chord))
        {
            case BuiltinAction.CloseSession:
                if (quick.IsZoomed) quick.SetZoom(ControlToggleMode.Off);
                else quick.Hide();
                break;
            case BuiltinAction.ToggleTerminalZoom: quick.SetZoom(ControlToggleMode.Toggle); break;
            case BuiltinAction.QuickTerminal: quick.Hide(); break;
            case BuiltinAction.IncreaseFontSize: Bind("increase_font_size:1"); break;
            case BuiltinAction.DecreaseFontSize: Bind("decrease_font_size:1"); break;
            case BuiltinAction.ResetFontSize: Bind("reset_font_size"); break;
            case { } action: ((App)Application.Current).DispatchQuickBuiltin(action); break;
        }
    }

    private void Bind(string action) => Observe(Task.Run(() => _surface?.PerformBindingAction(action)));

    private async Task OnCustomAsync(CustomHotkeyEvent hit)
    {
        if (hit.Generation != _generation || !_application.Quick.HoldsKey || _surface is not { } surface)
            return;
        var command = _keymap.Commands.FirstOrDefault(command => command.Id == hit.CommandId);
        if (command is null) return;
        var selection = await surface.Pane.Terminal.GetSelectionAsync() ?? string.Empty;
        await _runCustom(command, selection);
    }

    private Task RunOnUiAsync(Func<Task> work) => RunOnUiAsync(async () => { await work(); return true; });
    private Task<T> RunOnUiAsync<T>(Func<Task<T>> work)
    {
        if (DispatcherQueue.HasThreadAccess) return work();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { completion.TrySetResult(await work()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        })) completion.TrySetException(new InvalidOperationException("The quick terminal dispatcher is unavailable."));
        return completion.Task;
    }

    private async void Observe(Task task)
    {
        try { await task; }
        catch (Exception ex)
        {
            if (!_disposed) Report("Quick terminal failed", ex.GetBaseException().Message);
            _application.Quick.Hide();
        }
    }

    private void Report(string title, string message)
    {
        System.Diagnostics.Trace.TraceError($"{title}: {message}");
        _report(new(title, message));
    }

    private string? ReadClipboard()
    {
        if (DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("Quick clipboard reads must not block the UI thread.");
        return RunOnUiAsync(async () =>
        {
            var content = Clipboard.GetContent();
            return content.Contains(StandardDataFormats.Text) ? await content.GetTextAsync() : null;
        }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settingsSubscription?.Dispose();
        _application.KeymapReloaded -= OnKeymapReloaded;
        _appearance.ColorValuesChanged -= OnAppearanceChanged;
        _application.Quick.Changed -= OnQuickChanged;
        _hotkey.Dispose();
        _application.Quick.Hide();
        ResetPage();
        Close();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern long GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern long SetWindowLongPtr(IntPtr window, int index, long value);
}
