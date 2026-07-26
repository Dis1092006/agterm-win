using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace AgTerm.App;

/// <summary>
/// The phase-4 window: the LIVE control host (named-pipe control plane over a real AppStore) with
/// ONE WebView2 hosting every session's xterm.js pane (bridge v2) — the selected session's pane is
/// shown, the rest stay mounted in the shared page (keep-alive, like upstream). All control/host
/// logic lives in AgTerm.Xterm / AgTerm.ControlServer and is covered by tests; this shell owns
/// only WinUI wiring.
///
/// Threading: the host is constructed and disposed OFF the UI thread — realization blocks its
/// calling thread while the pane is created on the UI thread, so those calls must never run there.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly Dictionary<Guid, XtermPaneSurface> _surfaces = new(); // UI-thread confined
    private readonly Task<XtermPage> _pageTask;
    private readonly AppSettings _settings = new SettingsStore().Load();
    private LiveControlHost? _host;
    private SidebarController? _sidebar;
    private bool _draggingDivider;

    public MainWindow()
    {
        InitializeComponent();
        _pageTask = BootPageAsync(); // starts on the UI thread; surfaces await it
        Closed += (_, _) => Task.Run(() => _host?.Dispose()); // teardown blocks; keep it off the UI thread
        _ = StartHostAsync();
    }

    /// <summary>Boot the window's single terminal page: one WebView2, one terminal.html, N panes.</summary>
    private async Task<XtermPage> BootPageAsync()
    {
        await TerminalView.EnsureCoreWebView2Async();
        var page = new XtermPage(new WinRtScriptHost(TerminalView.CoreWebView2));
        await page.InitializeAsync();
        return page;
    }

    private async Task StartHostAsync()
    {
        // constructed off the UI thread: the bootstrap realizes the restored/default selected
        // session, which blocks the constructing thread on UI-thread pane creation
        var host = await Task.Run(() => new LiveControlHost(new LiveControlHostOptions
        {
            OpenSurface = OpenSurfaceAsync,
            ReadClipboard = ReadClipboardText,
        }));
        _host = host;
        host.Host.Store.SelectionChanged += () =>
        {
            // fired under the host gate; capture the id there, hop to the UI thread to switch panes
            var selected = host.Host.Store.SelectedSessionId;
            DispatcherQueue.TryEnqueue(() => ShowPane(selected));
        };
        // live OSC title/cwd reports change displayName — nudge the sidebar rows (host OOB hook)
        host.SurfaceRealized += (_, surface) =>
        {
            surface.Pane.TitleChanged += _ => Task.Run(() =>
                host.Host.RunLocked(host.Host.Store.NotifySidebar));
            surface.Pane.CwdChanged += _ => Task.Run(() =>
                host.Host.RunLocked(host.Host.Store.NotifySidebar));
        };
        var sidebar = new SidebarController(host.Host,
            runOnUi: work => DispatcherQueue.TryEnqueue(() => work()),
            runOffUi: work => Task.Run(work));
        _sidebar = sidebar;
        DispatcherQueue.TryEnqueue(() =>
        {
            Sidebar.Attach(sidebar, _settings);
            Sidebar.RowActivated += FocusActiveTerminal;
            sidebar.SnapshotChanged += ApplyChrome;
            sidebar.Start();
        });
        host.Start();
        DispatcherQueue.TryEnqueue(() => ShowPane(host.Host.Store.SelectedSessionId));
    }

    /// <summary>Window-level chrome from a sidebar snapshot: column width and visibility.</summary>
    private void ApplyChrome(SidebarSnapshot snapshot)
    {
        if (!_draggingDivider)
            SidebarColumn.Width = new GridLength(snapshot.SidebarVisible ? snapshot.SidebarWidth : 0);
        Sidebar.Visibility = snapshot.SidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        SidebarDivider.Visibility = Sidebar.Visibility;
    }

    /// <summary>Keyboard focus never lives in the sidebar (upstream): after a row gesture, return
    /// it to the visible terminal pane.</summary>
    private void FocusActiveTerminal()
    {
        TerminalView.Focus(FocusState.Programmatic);
        if (_host?.Host.Store.SelectedSessionId is { } id && _surfaces.TryGetValue(id, out var surface))
            _ = surface.Pane.Terminal.FocusAsync();
    }

    // MARK: - sidebar divider drag (persist on release, clamp 160…560 in the controller)

    private void OnDividerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _draggingDivider = true;
        SidebarDivider.CapturePointer(e.Pointer);
    }

    private void OnDividerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_draggingDivider) return;
        var x = e.GetCurrentPoint(RootGrid).Position.X;
        SidebarColumn.Width = new GridLength(Math.Clamp(x, 160, 560));
    }

    private void OnDividerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_draggingDivider) return;
        _draggingDivider = false;
        SidebarDivider.ReleasePointerCapture(e.Pointer);
        _sidebar?.SetSidebarWidth(SidebarColumn.Width.Value);
    }

    // MARK: - hotkeys (wired fully in the keymap step; F5 of the phase plan)

    private void OnRootKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
    }

    /// <summary>The UI half of surface realization: open a pane on the shared page for the session
    /// and drop the handle when its shell exits. Called from the host's dispatch thread.</summary>
    private Task<XtermPaneSurface> OpenSurfaceAsync(Session session, XtermSurfaceOptions options)
    {
        var done = new TaskCompletionSource<XtermPaneSurface>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var page = await _pageTask;
                var surface = await XtermPaneSurface.OpenAsync(page, options);
                _surfaces[session.Id] = surface;
                ShowPane(_host?.Host.Store.SelectedSessionId ?? session.Id);
                WireTitle(session, surface);
                _ = UnmountOnExitAsync(session.Id, surface);
                done.TrySetResult(surface);
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        });
        return done.Task;
    }

    /// <summary>Show exactly the selected session's pane in the shared page. UI thread only.</summary>
    private void ShowPane(Guid? selected)
    {
        if (selected is not { } id || !_surfaces.TryGetValue(id, out var surface)) return;
        _ = surface.ShowAsync();
        if (_host?.Host.Store.SessionWithId(id) is { } session)
            Title = $"{session.DisplayName} — agterm";
    }

    private void WireTitle(Session session, XtermPaneSurface surface)
    {
        surface.Pane.TitleChanged += title => DispatcherQueue.TryEnqueue(() =>
        {
            if (_host?.Host.Store.SelectedSessionId == session.Id)
                Title = $"{title} — agterm";
        });
    }

    private async Task UnmountOnExitAsync(Guid sessionId, XtermPaneSurface surface)
    {
        try
        {
            await surface.Pane.Exited;
        }
        catch (Exception)
        {
            // a torn-down pane may fault its exit task — unmount regardless
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            // the surface's Teardown (host close path) removes the page pane; here we only drop
            // the window's handle so ShowPane stops addressing it
            if (_surfaces.TryGetValue(sessionId, out var current) && ReferenceEquals(current, surface))
                _surfaces.Remove(sessionId);
        });
    }

    /// <summary>Sync clipboard read for session.paste, called on the host's dispatch thread —
    /// marshals to the UI thread and waits, bounded.</summary>
    private string? ReadClipboardText()
    {
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var content = Clipboard.GetContent();
                done.TrySetResult(content.Contains(StandardDataFormats.Text)
                    ? await content.GetTextAsync()
                    : null);
            }
            catch (Exception)
            {
                done.TrySetResult(null); // an unreadable clipboard pastes nothing, upstream-style
            }
        });
        try
        {
            return done.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
