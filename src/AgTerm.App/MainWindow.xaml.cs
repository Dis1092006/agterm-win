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
    private readonly Dictionary<string, XtermPaneSurface> _surfaces = new(); // UI-thread confined; keyed by pane token
    private readonly AppWindow _appWindow;
    private readonly CancellationTokenSource _bootstrapCancellation = new();
    private readonly TaskCompletionSource<LiveControlHost> _hostReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task<XtermPage> _pageTask;
    private readonly TerminalLayoutCoordinator _layoutCoordinator; // UI-thread confined
    private readonly WindowCloseCoordinator _closeCoordinator;
    private readonly PaletteTransitionCoordinator _paletteTransitions = new();
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
    private long _searchPresentationGeneration; // UI-thread confined
    private bool _terminalOnlyPresentation;
    private bool _draggingDivider;
    private readonly PaletteAutoFollowLease _paletteAutoFollow = new();
    private WindowPaletteMode? _openPaletteMode;

    private sealed record StartupState(
        LiveSelectionSnapshot Selection,
        IReadOnlyList<XtermPaneSurface> CommittedSurfaces);

    public MainWindow()
    {
        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _rendererSettings = RendererSettingsSnapshot.From(_settings);

        InitializeComponent();
        PaletteOverlay.ItemAccepted += item => ObserveTaskFault(
            HandlePaletteItemAcceptedAsync(item),
            "Palette item acceptance failed");
        PaletteOverlay.Cancelled += () => ObserveTaskFault(
            _paletteTransitions.RunAsync(() => FinishPaletteCloseAsync()),
            "Palette cancellation failed");
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
                    ApplyAcceptedBaseLayout(single, single.PaneId);
                    break;
                case TerminalLayout.Split split:
                    ApplyAcceptedBaseLayout(split, split.FocusedPaneId);
                    break;
                case TerminalLayout.Layered layered:
                    if (layered.BaseLayout is TerminalLayout.Empty)
                    {
                        _acceptedPaneId = layered.Layer.PaneId;
                        FocusActiveTerminal();
                        Title = "agterm";
                        AppTitleBar.Title = "agterm";
                        AppTitleBar.Subtitle = string.Empty;
                    }
                    else
                    {
                        ApplyAcceptedBaseLayout(layered.BaseLayout, layered.Layer.PaneId);
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
        page.SplitResized += OnPageSplitResized;
        page.PaneFocusChanged += OnPagePaneFocusChanged;
        page.SearchUpdated += OnPageSearchUpdated;
        page.SearchClosed += OnPageSearchClosed;
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

                var previousSelection = _selection;
                _selection = snapshot;
                _selectedSessionId = snapshot.SessionId;
                SetTerminalOnlyPresentation(
                    snapshot.QuickVisible && !string.IsNullOrWhiteSpace(snapshot.QuickPaneId));
                SubmitLayout();
                SubmitSearchPresentation(previousSelection, snapshot);
            });
        };

        host.SurfaceRealized += (request, surface) =>
        {
            if (request.Kind is LiveSurfaceKind.Main or LiveSurfaceKind.Split or LiveSurfaceKind.Scratch or LiveSurfaceKind.Overlay or LiveSurfaceKind.Quick)
            {
                DispatcherQueue.TryEnqueue(() => MountSurface(surface));
            }
        };
        host.PaneFocusRequested += paneId =>
        {
            if (_bootstrapCancellation.IsCancellationRequested)
                return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_bootstrapCancellation.IsCancellationRequested)
                    FocusTerminal(paneId);
            });
        };
        host.ZoomActiveChanged += _ => DispatcherQueue.TryEnqueue(() =>
            ObserveTaskFault(RefreshInterceptChordsAsync(), "Refresh intercept chords failed"));

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
            var selectedSession = selectedSessionId is { } selectedIdForLayout
                ? store.SessionWithId(selectedIdForLayout)
                : null;
            var selection = new LiveSelectionSnapshot(
                selectedSessionId,
                selectedDisplayName,
                selectedWorkspaceName,
                selectedSession?.Surface?.PaneToken,
                selectedSession?.SplitSurface?.PaneToken,
                selectedSession?.IsSplit ?? false,
                selectedSession?.SplitFocused ?? false,
                selectedSession?.SplitRatio,
                selectedSession?.ScratchSurface?.PaneToken,
                selectedSession?.ScratchActive ?? false,
                selectedSession?.OverlaySurface?.PaneToken,
                selectedSession?.OverlayActive ?? false,
                selectedSession?.OverlaySizePercent,
                selectedSession?.OverlayBackgroundColor,
                selectedSession?.SearchSurface?.PaneToken,
                selectedSession?.SearchActive ?? false,
                selectedSession?.SearchNeedle ?? "",
                selectedSession?.SearchDisplayText ?? "",
                selectedSession?.SearchTotal,
                selectedSession?.SearchSelected,
                host.Host.Actions.Quick.Surface?.PaneToken,
                host.Host.Actions.Quick.Visible);

            var committedSurfaces = store.Workspaces
                .SelectMany(workspace => workspace.Sessions)
                .SelectMany(session => new[]
                {
                    session.Surface,
                    session.SplitSurface,
                    session.ScratchSurface,
                    session.OverlaySurface,
                })
                .OfType<XtermPaneSurface>()
                .ToList();
            if (host.Host.Actions.Quick.Surface is XtermPaneSurface quickSurface)
                committedSurfaces.Add(quickSurface);
            return new StartupState(
                selection,
                committedSurfaces);
        });

        DispatcherQueue.TryEnqueue(() =>
        {
            _selection = startup.Selection;
            _selectedSessionId = startup.Selection.SessionId;
            SetTerminalOnlyPresentation(
                startup.Selection.QuickVisible && !string.IsNullOrWhiteSpace(startup.Selection.QuickPaneId));
            foreach (var surface in startup.CommittedSurfaces)
                MountSurface(surface);
            SubmitLayout();
            SubmitSearchPresentation(new LiveSelectionSnapshot(null, null), startup.Selection);
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
            [BuiltinAction.ToggleSplit] = Async(
                HasSelectedSession,
                () =>
                {
                    if (_selection.SessionId is not { } selectedSessionId)
                        return Task.CompletedTask;

                    return host.StoreCoordinator.MutateAsync(store => store.ToggleSplit(selectedSessionId));
                }),
            [BuiltinAction.ToggleScratch] = Async(
                HasSelectedSession,
                () =>
                {
                    if (_selection.SessionId is not { } selectedSessionId)
                        return Task.CompletedTask;

                    return host.StoreCoordinator.MutateAsync(store => store.ToggleScratch(selectedSessionId));
                }),
            [BuiltinAction.ToggleSearch] = Async(
                HasSelectedSession,
                () =>
                {
                    if (_selection.SessionId is not { } selectedSessionId)
                        return Task.CompletedTask;

                    return host.StoreCoordinator.MutateAsync(store =>
                    {
                        if (store.SessionWithId(selectedSessionId)?.SearchActive == true)
                            store.CloseSearch(selectedSessionId);
                        else
                            store.OpenSearch(selectedSessionId);
                    });
                }),
            [BuiltinAction.QuickTerminal] = Async(
                () => true,
                () => host.ToggleQuickTerminalAsync()),
            [BuiltinAction.SessionPalette] = Async(
                () => !host.ZoomActive,
                () => ToggleWindowPaletteAsync(WindowPaletteMode.Sessions)),
            [BuiltinAction.CommandPalette] = Async(
                () => !host.ZoomActive,
                () => ToggleWindowPaletteAsync(WindowPaletteMode.Commands)),
            [BuiltinAction.CustomCommandPalette] = Async(
                () => !host.ZoomActive,
                () => ToggleWindowPaletteAsync(WindowPaletteMode.CustomCommands)),
            [BuiltinAction.ShowAttention] = Async(
                () => !host.ZoomActive,
                () => ToggleWindowPaletteAsync(WindowPaletteMode.Attention)),
            [BuiltinAction.FocusLeftPane] = Async(
                () => !string.IsNullOrWhiteSpace(_selection.MainPaneId) && !string.IsNullOrWhiteSpace(_selection.SplitPaneId),
                () =>
                {
                    var targetPaneId = _selection.MainPaneId;
                    if (string.IsNullOrWhiteSpace(targetPaneId))
                        return Task.CompletedTask;

                    FocusTerminal(targetPaneId);
                    return host.ApplyPaneFocusAsync(targetPaneId);
                }),
            [BuiltinAction.FocusRightPane] = Async(
                () => !string.IsNullOrWhiteSpace(_selection.SplitPaneId),
                () =>
                {
                    var targetPaneId = _selection.SplitPaneId;
                    if (string.IsNullOrWhiteSpace(targetPaneId))
                        return Task.CompletedTask;

                    FocusTerminal(targetPaneId);
                    return host.ApplyPaneFocusAsync(targetPaneId);
                }),
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

    /// <summary>Applies title/subtitle from a Single/Split base layout and updates the accepted
    /// pane id — for a Layered layout, the base still owns the chrome and the layer owns focus.</summary>
    private void ApplyAcceptedBaseLayout(TerminalLayout baseLayout, string paneId)
    {
        var (title, workspaceName) = baseLayout switch
        {
            TerminalLayout.Single single => (single.Title, single.WorkspaceName),
            TerminalLayout.Split split => (split.Title ?? string.Empty, split.WorkspaceName),
            _ => throw new ArgumentOutOfRangeException(nameof(baseLayout), baseLayout, "Base layout must be single or split."),
        };

        Title = $"{title} — agterm";
        AppTitleBar.Title = title;
        AppTitleBar.Subtitle = workspaceName ?? string.Empty;

        if (_acceptedPaneId != paneId)
        {
            _acceptedPaneId = paneId;
            FocusActiveTerminal();
        }
    }

    /// <summary>Keyboard focus never lives in the sidebar (upstream): after a row gesture, return
    /// it to the visible terminal pane.</summary>
    private void FocusActiveTerminal()
    {
        if (_acceptedPaneId is { Length: > 0 } paneId)
        {
            FocusTerminal(paneId);
        }
    }

    private void FocusTerminal(string paneId)
    {
        TerminalView.Focus(FocusState.Programmatic);
        if (_surfaces.TryGetValue(paneId, out var surface))
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
            PublishInterceptChords(page);
        }
        Arm(host.Host.Actions.LoadKeymap());
        page.ChordPressed += (_, chord) => DispatcherQueue.TryEnqueue(() => HandleChord(chord));
        host.Host.Actions.KeymapReloaded += keymap => DispatcherQueue.TryEnqueue(() => Arm(keymap));
    }

    private async Task RefreshInterceptChordsAsync()
    {
        var page = await _pageTask;
        PublishInterceptChords(page);
    }

    private void PublishInterceptChords(XtermPage page)
    {
        if (_hotkeys is not { } hotkeys)
            return;

        ObserveTaskFault(
            page.SetInterceptChordsAsync(WindowCommandRouter.ImplementedInterceptChords(
                hotkeys,
                action => !IsPaletteAction(action) || _host?.ZoomActive != true)),
            "Set intercept chords failed");
    }

    private static bool IsPaletteAction(BuiltinAction action) => action is
        BuiltinAction.SessionPalette or
        BuiltinAction.CommandPalette or
        BuiltinAction.CustomCommandPalette or
        BuiltinAction.ShowAttention;

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

    private async Task OpenPaletteAsync(
        WindowPaletteMode mode,
        IReadOnlyDictionary<BuiltinAction, WindowCommandState> states,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(states);

        var host = await _hostReady.Task.WaitAsync(cancellationToken);
        var snapshot = await host.StoreCoordinator.ReadAsync(
            captureOutsideGate: host.Host.Actions.LoadKeymap,
            readUnderGate: (store, keymap) => host.Host.Actions.Zoom.Target is null
                ? WindowPaletteProjection.Create(mode, store, keymap, action => states[action])
                : null,
            cancellationToken: cancellationToken);
        if (snapshot is null)
            return;

        await _paletteAutoFollow.AcquireAsync(() =>
            host.StoreCoordinator.MutateAsync(
                store => store.SuppressAutoFollow(),
                cancellationToken));
        try
        {
            _openPaletteMode = mode;
            PaletteOverlay.Open(
                snapshot.Placeholder,
                snapshot.Items,
                snapshot.PreserveInputOrderForEmptyQuery);
        }
        catch
        {
            await _paletteAutoFollow.ReleaseAsync(() =>
                host.StoreCoordinator.MutateAsync(
                    store => store.ResumeAutoFollow(),
                    CancellationToken.None));
            throw;
        }
    }

    private Task ToggleWindowPaletteAsync(WindowPaletteMode mode) =>
        _paletteTransitions.RunAsync(() => ToggleWindowPaletteCoreAsync(mode));

    private async Task ToggleWindowPaletteCoreAsync(WindowPaletteMode mode)
    {
        if (PaletteOverlay.IsOpen && _openPaletteMode == mode)
        {
            PaletteOverlay.Close();
            await FinishPaletteCloseAsync();
            return;
        }

        var router = _commandRouter;
        if (router is null)
            return;

        var states = BuiltinActions.All.ToDictionary(action => action, router.GetState);
        await OpenPaletteAsync(mode, states);
    }

    private async Task FinishPaletteCloseAsync(bool restoreFocus = true)
    {
        if (_host is { } host)
        {
            await _paletteAutoFollow.ReleaseAsync(() =>
                host.StoreCoordinator.MutateAsync(store => store.ResumeAutoFollow()));
        }

        _openPaletteMode = null;
        if (restoreFocus)
            FocusActiveTerminal();
    }

    private async Task HandlePaletteItemAcceptedAsync(PaletteItemViewModel item)
    {
        WindowPaletteMode? mode = null;
        await _paletteTransitions.RunAsync(async () =>
        {
            mode = _openPaletteMode;
            await FinishPaletteCloseAsync(restoreFocus: false);
        });

        switch (mode)
        {
            case WindowPaletteMode.Sessions:
            case WindowPaletteMode.Attention:
                if (WindowPaletteProjection.ParseSessionId(item.Id) is { } sessionId &&
                    _host is { } sessionHost)
                {
                    var selectionChanged = false;
                    await sessionHost.StoreCoordinator.MutateAsync(store =>
                        selectionChanged = WindowPaletteActions.SelectSession(store, sessionId));
                    if (!selectionChanged)
                        FocusActiveTerminal();
                }
                else
                    FocusActiveTerminal();
                break;
            case WindowPaletteMode.Commands:
                if (WindowPaletteProjection.ParseAction(item.Id) is { } action &&
                    _commandRouter is { } router)
                {
                    var executed = await router.ExecuteAsync(action);
                    if (!executed || WindowPaletteActions.ShouldRestoreTerminalFocus(action))
                        FocusActiveTerminal();
                }
                else
                    FocusActiveTerminal();
                break;
            case WindowPaletteMode.CustomCommands:
                // C3 resolves and executes the selected custom command from its stable id.
                FocusActiveTerminal();
                break;
            default:
                FocusActiveTerminal();
                break;
        }
    }

    /// <summary>The UI half of surface realization: open a pane on the shared page for the session
    /// and drop the handle when its shell exits. Called from the host's dispatch thread.</summary>
    private Task<IHeadlessSurface> OpenSurfaceAsync(SurfaceOpenRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<IHeadlessSurface>(cancellationToken);
        if (request.Kind is LiveSurfaceKind.Main or LiveSurfaceKind.Split or LiveSurfaceKind.Scratch or LiveSurfaceKind.Overlay && request.SessionId is null)
            return Task.FromException<IHeadlessSurface>(
            new InvalidOperationException("Main, split, scratch, and overlay surfaces require a session id."));

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

    private void MountSurface(XtermPaneSurface surface)
    {
        if (string.IsNullOrWhiteSpace(surface.PaneToken))
            return;

        var paneToken = surface.PaneToken;
        if (_surfaces.TryGetValue(paneToken, out var current) && ReferenceEquals(current, surface))
            return;

        _surfaces[paneToken] = surface;
        SubmitLayout();
        ObserveTaskFault(
            UnmountOnShutdownAsync(paneToken, surface),
            $"Unmount on shutdown failed for pane {paneToken}");
    }

    private void SubmitLayout()
    {
        var generation = ++_layoutGeneration;
        _layoutCoordinator.Submit(_selection.ToTerminalLayout(generation));
    }

    private void SubmitSearchPresentation(
        LiveSelectionSnapshot previous,
        LiveSelectionSnapshot current)
    {
        var generation = ++_searchPresentationGeneration;
        ObserveTaskFault(
            ApplySearchPresentationAsync(generation, previous, current),
            "Apply search presentation failed");
    }

    private async Task ApplySearchPresentationAsync(
        long generation,
        LiveSelectionSnapshot previous,
        LiveSelectionSnapshot current)
    {
        var page = await _pageTask;
        if (generation != _searchPresentationGeneration)
            return;

        switch (SearchPresentationTransition.Resolve(previous, current, _acceptedPaneId))
        {
            case SearchPresentationTransition.Show show:
                await page.ApplySearchAsync(
                    show.PaneId,
                    show.Needle,
                    show.DisplayText,
                    show.Selected,
                    show.FocusInput);
                break;
            case SearchPresentationTransition.Hide hide:
                await page.HideSearchAsync(hide.ClearOwnerPaneId, hide.RestorePaneId);
                break;
        }
    }

    private async Task UnmountOnShutdownAsync(string paneToken, XtermPaneSurface surface)
    {
        await surface.ShutdownStarted;
        DispatcherQueue.TryEnqueue(() =>
        {
            // the surface's Teardown (host close path) removes the page pane; here we only drop
            // the window's token ownership
            if (_surfaces.TryGetValue(paneToken, out var current) && ReferenceEquals(current, surface))
            {
                _surfaces.Remove(paneToken);
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
        if (!_surfaces.TryGetValue(paneToken, out var surface))
        {
            return;
        }

        ObserveTaskFault(
            PasteIntoSurfaceAsync(surface),
            "Right-click paste failed");
    }

    private void OnPageSplitResized(string leftPaneId, string rightPaneId, double ratio)
    {
        if (_host is not { } host)
        {
            return;
        }

        ObserveTaskFault(
            host.ApplySplitRatioAsync(leftPaneId, rightPaneId, ratio),
            "Split ratio persistence failed");
    }

    private void OnPagePaneFocusChanged(string paneId)
    {
        if (_host is not { } host)
        {
            return;
        }

        ObserveTaskFault(
            host.ApplyPaneFocusAsync(paneId),
            "Pane focus persistence failed");
    }

    private void OnPageSearchUpdated(string paneId, string needle, int count, int? selected)
    {
        if (_host is not { } host)
            return;

        ObserveTaskFault(
            host.StoreCoordinator.MutateAsync(store =>
            {
                var owner = store.Workspaces
                    .SelectMany(workspace => workspace.Sessions)
                    .FirstOrDefault(session => session.SearchSurface?.PaneToken == paneId);
                if (owner is not null)
                    store.UpdateSearch(owner.Id, paneId, needle, count, selected);
            }),
            "Search update failed");
    }

    private void OnPageSearchClosed(string paneId)
    {
        if (_host is not { } host)
            return;

        ObserveTaskFault(
            host.StoreCoordinator.MutateAsync(store =>
            {
                var owner = store.Workspaces
                    .SelectMany(workspace => workspace.Sessions)
                    .FirstOrDefault(session => session.SearchSurface?.PaneToken == paneId);
                if (owner is not null)
                    store.CloseSearch(owner.Id, paneId);
            }),
            "Search close failed");
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
