using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace AgTerm.App;

/// <summary>
/// The phase-3 window: the LIVE control host (named-pipe control plane over a real AppStore) with
/// one WebView2 pane per realized session — the selected session's pane is visible, the rest stay
/// mounted (keep-alive, like upstream). All control/host logic lives in AgTerm.Xterm /
/// AgTerm.ControlServer and is covered by tests; this shell owns only WinUI wiring.
///
/// Threading: the host is constructed and disposed OFF the UI thread — realization blocks its
/// calling thread while the pane is created on the UI thread, so those calls must never run there.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly Dictionary<Guid, WebView2> _views = new(); // UI-thread confined
    private LiveControlHost? _host;

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) => Task.Run(() => _host?.Dispose()); // teardown blocks; keep it off the UI thread
        _ = StartHostAsync();
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
        host.Start();
        DispatcherQueue.TryEnqueue(() => ShowPane(host.Host.Store.SelectedSessionId));
    }

    /// <summary>The UI half of surface realization: mount a WebView2 for the session, open the
    /// surface over it, and unmount when its shell exits. Called from the host's dispatch thread.</summary>
    private Task<XtermPaneSurface> OpenSurfaceAsync(Session session, XtermSurfaceOptions options)
    {
        var done = new TaskCompletionSource<XtermPaneSurface>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var view = new WebView2 { Visibility = Visibility.Collapsed };
                RootGrid.Children.Add(view);
                await view.EnsureCoreWebView2Async();
                var surface = await XtermPaneSurface.OpenAsync(
                    new WinRtScriptHost(view.CoreWebView2), options);
                _views[session.Id] = view;
                ShowPane(_host?.Host.Store.SelectedSessionId ?? session.Id);
                WireTitle(session, surface);
                _ = UnmountOnExitAsync(session.Id, view, surface);
                done.TrySetResult(surface);
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        });
        return done.Task;
    }

    /// <summary>Show exactly the selected session's pane. UI thread only.</summary>
    private void ShowPane(Guid? selected)
    {
        foreach (var (id, view) in _views)
            view.Visibility = id == selected ? Visibility.Visible : Visibility.Collapsed;
        if (selected is { } id2 && _views.TryGetValue(id2, out var pane) &&
            _host?.Host.Store.SessionWithId(id2) is { } session)
        {
            Title = $"{session.DisplayName} — agterm";
            _ = pane.EnsureCoreWebView2Async(); // no-op when ready; keeps focus behavior sane
        }
    }

    private void WireTitle(Session session, XtermPaneSurface surface)
    {
        surface.Pane.TitleChanged += title => DispatcherQueue.TryEnqueue(() =>
        {
            if (_host?.Host.Store.SelectedSessionId == session.Id)
                Title = $"{title} — agterm";
        });
    }

    private async Task UnmountOnExitAsync(Guid sessionId, WebView2 view, XtermPaneSurface surface)
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
            if (_views.TryGetValue(sessionId, out var current) && ReferenceEquals(current, view))
                _views.Remove(sessionId);
            RootGrid.Children.Remove(view);
            view.Close();
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
