using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.ControlServer;
using AgTerm.Xterm;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
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
    private readonly AppWindow _appWindow;
    private readonly CancellationTokenSource _bootstrapCancellation = new();
    private readonly TaskCompletionSource<LiveControlHost> _hostReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task<XtermPage> _pageTask;
    private readonly TerminalLayoutCoordinator _layoutCoordinator; // UI-thread confined
    private readonly WindowCloseCoordinator _closeCoordinator;
    private readonly SettingsStore _settingsStore;
    private readonly SettingsReloadCoordinator _settingsReloadCoordinator;
    private AppSettings _settings; // UI-thread confined
    private RendererSettingsSnapshot _rendererSettings; // UI-thread confined
    private LiveControlHost? _host;
    private SidebarController? _sidebar;
    private WindowCommandRouter? _commandRouter; // UI-thread confined
    private LiveSelectionSnapshot _selection = new(null, null); // UI-thread confined
    private SidebarSnapshot? _latestSidebarSnapshot; // UI-thread confined
    private string? _acceptedPaneId; // UI-thread confined
    private Guid? _selectedSessionId; // UI-thread confined
    private long _layoutGeneration; // UI-thread confined
    private bool _terminalOnlyPresentation;
    private bool _draggingDivider;

    private sealed record StartupState(
        LiveSelectionSnapshot Selection,
        IReadOnlyList<(Guid SessionId, XtermPaneSurface Surface)> CommittedMainSurfaces);

    public MainWindow()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _rendererSettings = RendererSettingsSnapshot.From(_settings);

        InitializeComponent();
        _settingsReloadCoordinator = new SettingsReloadCoordinator(
            load: cancellationToken => Task.Run(() => _settingsStore.Load(), cancellationToken),
            runOnUi: work =>
            {
                if (!DispatcherQueue.TryEnqueue(() => work()))
                    throw new InvalidOperationException("Failed to enqueue settings reload.");
            },
            apply: ApplySettingsAsync,
            error: ex => System.Diagnostics.Trace.TraceError(
                $"Settings reload failed: {ex.GetBaseException().Message}"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppTitleBar.PaneToggleRequested += (_, _) =>
        {
            var router = _commandRouter;
            if (router is null)
                return;

            ObserveTaskFault(
                router.ExecuteAsync(BuiltinAction.ToggleSidebar),
                "Title bar toggle sidebar failed");
        };
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(
            WinRT.Interop.WindowNative.GetWindowHandle(this));
        _appWindow = AppWindow.GetFromWindowId(windowId);
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
                    AppTitleBar.Title = single.Title;
                    AppTitleBar.Subtitle = single.WorkspaceName ?? string.Empty;
                    if (_acceptedPaneId != single.PaneId)
                    {
                        _acceptedPaneId = single.PaneId;
                        FocusActiveTerminal();
                    }
                    break;
                case TerminalLayout.Empty:
                    _acceptedPaneId = null;
                    Title = "agterm";
                    AppTitleBar.Title = "agterm";
                    AppTitleBar.Subtitle = string.Empty;
                    break;
            }
        };
        _closeCoordinator = new WindowCloseCoordinator(
            ShutdownForCloseAsync,
            work =>
            {
                if (!DispatcherQueue.TryEnqueue(() => work()))
                    throw new InvalidOperationException("Failed to enqueue final window close.");
            },
            Close,
            ex => System.Diagnostics.Debug.WriteLine($"Window shutdown failed: {ex}"));
        _appWindow.Closing += OnAppWindowClosing;
        Closed += (_, _) =>
        {
            _appWindow.Closing -= OnAppWindowClosing;
            _bootstrapCancellation.Cancel();
            _settingsReloadCoordinator.Dispose();
            _layoutCoordinator.Dispose();
            ObserveTaskFault(
                Task.Run(() => _host?.Dispose()),
                "Window closed disposal failed");
        };
        ObserveTaskFault(StartHostAsync(), "StartHostAsync failed");
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = _closeCoordinator.ShouldCancelClose();
    }

    private async Task ShutdownForCloseAsync()
    {
        _bootstrapCancellation.Cancel();
        var host = await _hostReady.Task.ConfigureAwait(false);
        await host.ShutdownAsync().ConfigureAwait(false);
    }

    /// <summary>Boot the window's single terminal page: one WebView2, one terminal.html, N panes.</summary>
    private async Task<XtermPage> BootPageAsync()
    {
        await TerminalView.EnsureCoreWebView2Async();
        var page = new XtermPage(new WinRtScriptHost(TerminalView.CoreWebView2));
        await page.InitializeAsync();
        page.PasteRequested += OnPagePasteRequested;
        ApplyComposition(_rendererSettings);
        await page.ApplyRendererSettingsAsync(_rendererSettings);
        return page;
    }

    private async Task StartHostAsync()
    {
        // constructed off the UI thread: the bootstrap realizes the restored/default selected
        // session, which blocks the constructing thread on UI-thread pane creation
        LiveControlHost host;
        try
        {
            host = await Task.Run(() => new LiveControlHost(new LiveControlHostOptions
            {
                OpenSurface = OpenSurfaceAsync,
                ReadClipboard = ReadClipboardText,
                BaseFontSize = _rendererSettings.FontSize,
                ConfigDir = _settings.ConfigDirectory,
                ConfigReload = OnConfigReload,
                IsUiThread = () => DispatcherQueue.HasThreadAccess,
                BootstrapCancellationToken = _bootstrapCancellation.Token,
            }));
        }
        catch (Exception ex)
        {
            _hostReady.TrySetException(ex);
            throw;
        }

        _host = host;
        _hostReady.TrySetResult(host);
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
            string? selectedDisplayName = null;
            string? selectedWorkspaceName = null;
            if (selectedSessionId is { } selectedId)
            {
                selectedDisplayName = store.SessionWithId(selectedId)?.DisplayName;
                selectedWorkspaceName = store.WorkspaceForSession(selectedId)?.Name;
            }
            var committedSurfaces = store.Workspaces
                .SelectMany(workspace => workspace.Sessions)
                .Where(session => session.Surface is XtermPaneSurface)
                .Select(session => (session.Id, (XtermPaneSurface)session.Surface!))
                .ToList();
            return new StartupState(
                new LiveSelectionSnapshot(selectedSessionId, selectedDisplayName, selectedWorkspaceName),
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

        static WindowCommandHandler Sync(Func<bool> canExecute, Action execute)
        {
            return new WindowCommandHandler(canExecute, () =>
            {
                execute();
                return Task.CompletedTask;
            });
        }

        static WindowCommandHandler Async(Func<bool> canExecute, Func<Task> executeAsync)
        {
            return new WindowCommandHandler(canExecute, executeAsync);
        }

        bool HasSelectedSession() => _selectedSessionId is not null;

        _commandRouter = new WindowCommandRouter(new Dictionary<BuiltinAction, WindowCommandHandler>
        {
            [BuiltinAction.NewWorkspace] = Sync(() => true, () => sidebar.NewWorkspace()),
            [BuiltinAction.NewSession] = Sync(() => true, () => sidebar.NewSessionInActiveWorkspace()),
            [BuiltinAction.OpenDirectory] = Async(() => true, OpenDirectoryIntoActiveAsync),
            [BuiltinAction.RenameSession] = Sync(HasSelectedSession, Sidebar.BeginRenameSelected),
            [BuiltinAction.DuplicateSession] = Sync(HasSelectedSession, sidebar.DuplicateActiveSession),
            [BuiltinAction.CloseSession] = Sync(HasSelectedSession, sidebar.CloseActiveSession),
            [BuiltinAction.UndoClose] = Sync(() => true, sidebar.UndoClose),
            [BuiltinAction.ClearStatus] = Sync(HasSelectedSession, sidebar.ClearActiveStatus),
            [BuiltinAction.IncreaseFontSize] = Async(HasSelectedSession, () => host.PerformFontActionAsync("increase_font_size:1")),
            [BuiltinAction.DecreaseFontSize] = Async(HasSelectedSession, () => host.PerformFontActionAsync("decrease_font_size:1")),
            [BuiltinAction.ResetFontSize] = Async(HasSelectedSession, () => host.PerformFontActionAsync("reset_font_size")),
            [BuiltinAction.ToggleSidebar] = Sync(() => true, sidebar.ToggleSidebarVisible),
            [BuiltinAction.ToggleFlaggedView] = Sync(() => true, sidebar.ToggleFlaggedView),
            [BuiltinAction.ToggleFlag] = Sync(HasSelectedSession, sidebar.ToggleActiveFlag),
            [BuiltinAction.FocusWorkspace] = Sync(HasSelectedSession, sidebar.FocusActiveWorkspaceToggle),
            [BuiltinAction.PreviousSession] = Sync(() => true, () => sidebar.Navigate(SessionNavigation.Previous)),
            [BuiltinAction.NextSession] = Sync(() => true, () => sidebar.Navigate(SessionNavigation.Next)),
            [BuiltinAction.PreviousAttentionSession] = Sync(() => true, () => sidebar.Navigate(SessionNavigation.PreviousAttention)),
            [BuiltinAction.NextAttentionSession] = Sync(() => true, () => sidebar.Navigate(SessionNavigation.NextAttention)),
            [BuiltinAction.FirstSession] = Sync(() => true, () => sidebar.Navigate(SessionNavigation.First)),
            [BuiltinAction.LastSession] = Sync(() => true, () => sidebar.Navigate(SessionNavigation.Last)),
        });

        DispatcherQueue.TryEnqueue(() =>
        {
            Sidebar.Attach(sidebar, _settings);
            Sidebar.RowActivated += FocusActiveTerminal;
            sidebar.SnapshotChanged += ApplyChrome;
            sidebar.Start();
        });
        host.Start();
        ObserveTaskFault(ArmHotkeysAsync(host), "Arm hotkeys failed");
    }

    /// <summary>Window-level chrome from a sidebar snapshot: column width and visibility.</summary>
    private void ApplyChrome(SidebarSnapshot snapshot)
    {
        _latestSidebarSnapshot = snapshot;
        if (_terminalOnlyPresentation)
            return;

        ApplySidebarChrome(snapshot);
    }

    private void ApplySidebarChrome(SidebarSnapshot snapshot)
    {
        var width = Math.Clamp(snapshot.SidebarWidth, AppStore.SidebarWidthMin, AppStore.SidebarWidthMax);
        if (!_draggingDivider)
        {
            SidebarColumn.Width = new GridLength(width);
            ShellSplitView.OpenPaneLength = width;
        }

        ShellSplitView.IsPaneOpen = snapshot.SidebarVisible;
    }

    internal void SetTerminalOnlyPresentation(bool enabled)
    {
        if (_terminalOnlyPresentation == enabled)
            return;

        _terminalOnlyPresentation = enabled;
        VisualStateManager.GoToState(
            ShellSplitView,
            enabled ? "TerminalOnlyPresentation" : "StandardPresentation",
            useTransitions: false);

        if (!enabled && _latestSidebarSnapshot is { } snapshot)
            ApplySidebarChrome(snapshot);
    }

    /// <summary>Keyboard focus never lives in the sidebar (upstream): after a row gesture, return
    /// it to the visible terminal pane.</summary>
    private void FocusActiveTerminal()
    {
        TerminalView.Focus(FocusState.Programmatic);
        if (_selectedSessionId is { } id && _surfaces.TryGetValue(id, out var surface))
        {
            ObserveTaskFault(
                surface.Pane.Terminal.FocusAsync(),
                "Focus active terminal failed");
        }
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
        var x = e.GetCurrentPoint(ShellSplitView).Position.X;
        var width = Math.Clamp(x, AppStore.SidebarWidthMin, AppStore.SidebarWidthMax);
        SidebarColumn.Width = new GridLength(width);
        ShellSplitView.OpenPaneLength = width;
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
            foreach (var diagnostic in WindowCommandRouter.EffectiveWindowsChordDiagnostics(keymap))
                System.Diagnostics.Trace.TraceWarning($"keymap: {diagnostic.Message}");
            ObserveTaskFault(
                page.SetInterceptChordsAsync(WindowCommandRouter.ImplementedInterceptChords(_hotkeys)),
                "Set intercept chords failed");
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
        if (_hotkeys?.Handle(physical) is not { } action)
            return false;

        var router = _commandRouter;
        if (router is null || !router.CanExecute(action))
            return false;

        ObserveTaskFault(
            router.ExecuteAsync(action),
            $"Hotkey action {action} failed");
        return true;
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
        ObserveTaskFault(
            UnmountOnExitAsync(sessionId, surface),
            $"Unmount on exit failed for session {sessionId}");
    }

    private void SubmitLayout()
    {
        var generation = ++_layoutGeneration;
        if (_selection.SessionId is { } sessionId &&
            _selection.DisplayName is { } displayName &&
            _surfaces.TryGetValue(sessionId, out var surface) &&
            surface.PaneToken is { } paneToken)
        {
            _layoutCoordinator.Submit(new TerminalLayout.Single(generation, paneToken, displayName, _selection.WorkspaceName));
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

    private async Task<string?> ReadClipboardTextOnUiAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            return content.Contains(StandardDataFormats.Text)
                ? await content.GetTextAsync()
                : null;
        }
        catch (Exception)
        {
            return null; // an unreadable clipboard pastes nothing, upstream-style
        }
    }

    /// <summary>Sync clipboard read for session.paste, called on the host's dispatch thread —
    /// marshals to the UI thread and waits, bounded.</summary>
    private string? ReadClipboardText()
    {
        if (DispatcherQueue.HasThreadAccess)
            return null; // UI callers use the asynchronous right-click path below.

        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            done.TrySetResult(await ReadClipboardTextOnUiAsync());
        }))
            return null;
        try
        {
            return done.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnPagePasteRequested(string paneToken)
    {
        var surface = _surfaces.Values.FirstOrDefault(s =>
            string.Equals(s.PaneToken, paneToken, StringComparison.Ordinal));
        if (surface is null)
        {
            return;
        }

        ObserveTaskFault(
            PasteIntoSurfaceAsync(surface),
            "Right-click paste failed");
    }

    private async Task PasteIntoSurfaceAsync(XtermPaneSurface surface)
    {
        var text = await ReadClipboardTextOnUiAsync();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        await surface.Pane.Terminal.PasteAsync(text);
    }

    private int OnConfigReload()
    {
        _settingsReloadCoordinator.Request();
        return 0;
    }

    private async Task ApplySettingsAsync(AppSettings settings)
    {
        var snapshot = RendererSettingsSnapshot.From(settings);

        if (_host is { } host)
        {
            // Upstream's explicit config reload clears per-session zoom even when the configured
            // base font is unchanged; only automatic appearance flips preserve zoom.
            await host.ApplyBaseFontSizeAsync(snapshot.FontSize);
        }

        var page = await _pageTask;
        await page.ApplyRendererSettingsAsync(snapshot);

        _settings = settings;
        _rendererSettings = snapshot;
        Sidebar.ApplySettings(settings);
        ApplyComposition(snapshot);
    }

    private void ApplyComposition(RendererSettingsSnapshot snapshot)
    {
        var translucent = snapshot.BackgroundOpacity < 1.0;
        TerminalView.DefaultBackgroundColor = translucent
            ? Color.FromArgb(0x00, 0x00, 0x00, 0x00)
            : ParseHexColor(snapshot.BackgroundColor, Color.FromArgb(0xFF, 0x0C, 0x0C, 0x0C));

        if (!translucent)
        {
            if (SystemBackdrop is not MicaBackdrop)
                SystemBackdrop = new MicaBackdrop();
            return;
        }

        if (snapshot.BackgroundBlur > 0)
        {
            if (SystemBackdrop is not DesktopAcrylicBackdrop)
                SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        else if (SystemBackdrop is not null)
        {
            SystemBackdrop = null;
        }
    }

    private static Color ParseHexColor(string hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        var trimmed = hex.Trim();
        if (trimmed.Length == 7 && trimmed[0] == '#')
        {
            if (TryHexByte(trimmed.AsSpan(1, 2), out var r) &&
                TryHexByte(trimmed.AsSpan(3, 2), out var g) &&
                TryHexByte(trimmed.AsSpan(5, 2), out var b))
            {
                return Color.FromArgb(0xFF, r, g, b);
            }
        }

        return fallback;
    }

    private static bool TryHexByte(ReadOnlySpan<char> hex, out byte value) =>
        byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out value);

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
