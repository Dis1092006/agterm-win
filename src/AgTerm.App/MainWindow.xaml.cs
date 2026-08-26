using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.ControlServer;
using AgTerm.Xterm;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

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
    private readonly TerminalLayoutCoordinator _layoutCoordinator; // UI-thread confined
    private readonly AppSettings _settings = new SettingsStore().Load();
    private LiveControlHost? _host;
    private SidebarController? _sidebar;
    private LiveSelectionSnapshot _selection = new(null, null); // UI-thread confined
    private Guid? _selectedSessionId; // UI-thread confined
    private long _layoutGeneration; // UI-thread confined
    private bool _draggingDivider;

    private sealed record StartupState(
        LiveSelectionSnapshot Selection,
        IReadOnlyList<(Guid SessionId, XtermPaneSurface Surface)> CommittedMainSurfaces);

    public MainWindow()
    {
        InitializeComponent();
        _pageTask = BootPageAsync(); // starts on the UI thread; surfaces await it
        _layoutCoordinator = new TerminalLayoutCoordinator(async layout =>
        {
            var page = await _pageTask;
            return await page.ApplyLayoutAsync(layout);
        });
        _layoutCoordinator.LayoutAccepted += (_, layout) =>
        {
            switch (layout)
            {
                case TerminalLayout.Single single:
                    Title = $"{single.Title} — agterm";
                    break;
                case TerminalLayout.Empty:
                    Title = "agterm";
                    break;
            }
        };
        Closed += (_, _) =>
        {
            _layoutCoordinator.Dispose();
            _ = Task.Run(() => _host?.Dispose()); // teardown blocks; keep it off the UI thread
        };
        ObserveTaskFault(StartHostAsync(), "StartHostAsync failed");
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
            IsUiThread = () => DispatcherQueue.HasThreadAccess,
        }));
        _host = host;
        host.StoreCoordinator.SelectionSnapshotChanged += snapshot =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (snapshot == _selection)
                    return;

                _selection = snapshot;
                _selectedSessionId = snapshot.SessionId;
                SubmitLayout();
            });
        };

        host.SurfaceRealized += (sessionId, surface) =>
            DispatcherQueue.TryEnqueue(() => MountSurface(sessionId, surface));

        var startup = await host.StoreCoordinator.ReadAsync(store =>
        {
            var selectedSessionId = store.SelectedSessionId;
            var selectedDisplayName = selectedSessionId is { } id
                ? store.SessionWithId(id)?.DisplayName
                : null;
            var committedSurfaces = store.Workspaces
                .SelectMany(workspace => workspace.Sessions)
                .Where(session => session.Surface is XtermPaneSurface)
                .Select(session => (session.Id, (XtermPaneSurface)session.Surface!))
                .ToList();
            return new StartupState(
                new LiveSelectionSnapshot(selectedSessionId, selectedDisplayName),
                committedSurfaces);
        });

        DispatcherQueue.TryEnqueue(() =>
        {
            _selection = startup.Selection;
            _selectedSessionId = startup.Selection.SessionId;
            foreach (var (sessionId, surface) in startup.CommittedMainSurfaces)
                MountSurface(sessionId, surface);
            SubmitLayout();
        });

        var sidebar = new SidebarController(
            host.StoreCoordinator,
            runOnUi: work => DispatcherQueue.TryEnqueue(() => work()),
            log: message => System.Diagnostics.Debug.WriteLine(message));
        _sidebar = sidebar;
        DispatcherQueue.TryEnqueue(() =>
        {
            Sidebar.Attach(sidebar, _settings);
            Sidebar.RowActivated += FocusActiveTerminal;
            sidebar.SnapshotChanged += ApplyChrome;
            sidebar.Start();
        });
        host.Start();
        _ = ArmHotkeysAsync(host);
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
        if (_selectedSessionId is { } id && _surfaces.TryGetValue(id, out var surface))
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

    // MARK: - hotkeys (keymap → HotkeyRouter → the same store/controller paths as the socket)

    private HotkeyRouter? _hotkeys;

    /// <summary>Load keymap.conf, arm the router, and push the interception table into the page
    /// (in-terminal presses come back as chord messages). keymap.reload re-arms both.</summary>
    private async Task ArmHotkeysAsync(LiveControlHost host)
    {
        var page = await _pageTask;
        void Arm(Keymap keymap)
        {
            _hotkeys = new HotkeyRouter(keymap);
            _ = page.SetInterceptChordsAsync(_hotkeys.InterceptChords);
        }
        Arm(host.Host.Actions.LoadKeymap());
        page.ChordPressed += (_, chord) => DispatcherQueue.TryEnqueue(() => HandleChord(chord));
        host.Host.Actions.KeymapReloaded += keymap => DispatcherQueue.TryEnqueue(() => Arm(keymap));
    }

    private void OnRootKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (PhysicalFromKey(e.Key) is { } physical && HandleChord(physical)) e.Handled = true;
    }

    /// <summary>A WinUI key event as a keymap-space physical chord (null = not hotkey material).</summary>
    private static PhysicalChord? PhysicalFromKey(VirtualKey key)
    {
        static bool Down(VirtualKey k) =>
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(k)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var name = key switch
        {
            >= VirtualKey.A and <= VirtualKey.Z => char.ToLowerInvariant((char)key).ToString(),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((char)('0' + (key - VirtualKey.Number0))).ToString(),
            VirtualKey.Tab => "tab",
            VirtualKey.Space => "space",
            VirtualKey.Enter => "return",
            VirtualKey.Back => "delete",
            VirtualKey.Left => "left",
            VirtualKey.Right => "right",
            VirtualKey.Up => "up",
            VirtualKey.Down => "down",
            (VirtualKey)0xBB => "+",   // VK_OEM_PLUS
            (VirtualKey)0xBD => "-",   // VK_OEM_MINUS
            (VirtualKey)0xC0 => "`",   // VK_OEM_3
            _ => null,
        };
        if (name is null) return null;
        return new PhysicalChord(Down(VirtualKey.Control), Down(VirtualKey.Menu),
            Down(VirtualKey.Shift), name);
    }

    /// <summary>Dispatch a physical chord through the router to the same mutators the control
    /// socket drives. Returns whether an action fired.</summary>
    private bool HandleChord(PhysicalChord physical)
    {
        if (_hotkeys?.Handle(physical) is not { } action) return false;
        var sidebar = _sidebar;
        var host = _host;
        if (sidebar is null || host is null) return false;
        switch (action)
        {
            case BuiltinAction.NewSession: sidebar.NewSessionInActiveWorkspace(); break;
            case BuiltinAction.CloseSession: sidebar.CloseActiveSession(); break;
            case BuiltinAction.DuplicateSession: sidebar.DuplicateActiveSession(); break;
            case BuiltinAction.RenameSession: Sidebar.BeginRenameSelected(); break;
            case BuiltinAction.NewWorkspace: sidebar.NewWorkspace(); break;
            case BuiltinAction.ToggleFlag: sidebar.ToggleActiveFlag(); break;
            case BuiltinAction.ToggleFlaggedView: sidebar.ToggleFlaggedView(); break;
            case BuiltinAction.ToggleSidebar: sidebar.ToggleSidebarVisible(); break;
            case BuiltinAction.FocusWorkspace: sidebar.FocusActiveWorkspaceToggle(); break;
            case BuiltinAction.ClearStatus: sidebar.ClearActiveStatus(); break;
            case BuiltinAction.UndoClose: sidebar.UndoClose(); break;
            case BuiltinAction.PreviousSession: sidebar.Navigate(SessionNavigation.Previous); break;
            case BuiltinAction.NextSession: sidebar.Navigate(SessionNavigation.Next); break;
            case BuiltinAction.FirstSession: sidebar.Navigate(SessionNavigation.First); break;
            case BuiltinAction.LastSession: sidebar.Navigate(SessionNavigation.Last); break;
            case BuiltinAction.PreviousAttentionSession: sidebar.Navigate(SessionNavigation.PreviousAttention); break;
            case BuiltinAction.NextAttentionSession: sidebar.Navigate(SessionNavigation.NextAttention); break;
            case BuiltinAction.OpenDirectory: _ = OpenDirectoryIntoActiveAsync(); break;
            case BuiltinAction.IncreaseFontSize: FontAction("increase_font_size:1"); break;
            case BuiltinAction.DecreaseFontSize: FontAction("decrease_font_size:1"); break;
            case BuiltinAction.ResetFontSize: FontAction("reset_font_size"); break;
            default:
                // palettes / split / scratch / zoom / fullscreen / quick / dashboard / theme /
                // window management arrive with phase 5 — swallow the chord (it IS bound), log it
                System.Diagnostics.Debug.WriteLine($"hotkey {action} is not implemented yet (phase 5)");
                break;
        }
        return true;
    }

    private void FontAction(string action)
    {
        var host = _host;
        if (host is null) return;
        ObserveTaskFault(
            host.PerformFontActionAsync(action),
            "Font action failed");
    }

    private async Task OpenDirectoryIntoActiveAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (await picker.PickSingleFolderAsync() is { } folder)
            _sidebar?.NewSessionInActiveWorkspace(folder.Path);
    }

    /// <summary>The UI half of surface realization: open a pane on the shared page for the session
    /// and drop the handle when its shell exits. Called from the host's dispatch thread.</summary>
    private Task<IHeadlessSurface> OpenSurfaceAsync(SurfaceOpenRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IHeadlessSurface>(cancellationToken);
        if (request.Kind == LiveSurfaceKind.Main && request.SessionId is null)
            return Task.FromException<IHeadlessSurface>(
                new InvalidOperationException("Main surface requires a session id."));

        var done = new TaskCompletionSource<IHeadlessSurface>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = await _pageTask;
                cancellationToken.ThrowIfCancellationRequested();

                var surface = await XtermPaneSurface.OpenAsync(page, request.Options, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    surface.Teardown();
                    done.TrySetCanceled(cancellationToken);
                    return;
                }

                done.TrySetResult(surface);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                done.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }))
        {
            if (cancellationToken.IsCancellationRequested)
                done.TrySetCanceled(cancellationToken);
            else
                done.TrySetException(new InvalidOperationException("Failed to enqueue surface open on the UI thread."));
        }

        return done.Task;
    }

    private void MountSurface(Guid sessionId, XtermPaneSurface surface)
    {
        if (_surfaces.TryGetValue(sessionId, out var current) && ReferenceEquals(current, surface))
            return;
        _surfaces[sessionId] = surface;
        SubmitLayout();
        _ = UnmountOnExitAsync(sessionId, surface);
    }

    private void SubmitLayout()
    {
        var generation = ++_layoutGeneration;
        if (_selection.SessionId is { } sessionId &&
            _selection.DisplayName is { } displayName &&
            _surfaces.TryGetValue(sessionId, out var surface) &&
            surface.PaneToken is { } paneToken)
        {
            _layoutCoordinator.Submit(new TerminalLayout.Single(generation, paneToken, displayName));
            return;
        }

        _layoutCoordinator.Submit(new TerminalLayout.Empty(generation));
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
            // the window's handle and refresh layout selection state
            if (_surfaces.TryGetValue(sessionId, out var current) && ReferenceEquals(current, surface))
            {
                _surfaces.Remove(sessionId);
                SubmitLayout();
            }
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

    private static void ObserveTaskFault(Task task, string context)
    {
        if (task.IsCompleted)
        {
            if (task.IsFaulted && task.Exception is { } ex)
                System.Diagnostics.Debug.WriteLine($"{context}: {ex.GetBaseException().Message}");
            return;
        }

        _ = task.ContinueWith(
            static (completedTask, state) =>
            {
                if (completedTask.IsFaulted && completedTask.Exception is { } ex)
                {
                    var message = (string)state!;
                    System.Diagnostics.Debug.WriteLine($"{message}: {ex.GetBaseException().Message}");
                }
            },
            context,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
