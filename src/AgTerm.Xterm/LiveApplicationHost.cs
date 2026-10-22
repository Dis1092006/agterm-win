using System.Text.Json.Serialization;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

public sealed record LiveApplicationHostOptions
{
    public string? StateDir { get; init; }

    public string? PipeName { get; init; }

    /// <summary>The control socket path behind PipeName (see LiveControlHostOptions.SocketPath).</summary>
    public string? SocketPath { get; init; }

    public string? HomeDirectory { get; init; }

    public ILiveWindowController? WindowController { get; init; }

    internal TimeProvider TreeEventTimeProvider { get; init; } = TimeProvider.System;
}

public sealed record LiveWindowEntry(
    [property: JsonPropertyName("id")]
    [property: JsonConverter(typeof(UppercaseGuidConverter))]
    Guid Id,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("isOpen")]
    bool IsOpen);

public sealed class LiveApplicationHost : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Lock _treeEventGate = new();
    private readonly Dictionary<Guid, ITimer> _treeEventTimers = new();
    private readonly TimeProvider _treeEventTimeProvider;
    private readonly string _stateDirectory;
    private readonly string _homeDirectory;
    private readonly ILiveWindowController? _windowController;
    private readonly RecentClosedStore _recentClosedStore;
    private readonly SemaphoreSlim _recentClosedGate = new(1, 1);
    private readonly Dictionary<Guid, LiveWindowHost> _openWindows = new();
    private readonly Dictionary<Guid, LiveWindowHost> _closingWindows = new();
    private readonly List<LiveWindowEntry> _windows = new();
    private readonly ControlPipeServer _server;
    private readonly Dictionary<Guid, LiveControlHost> _attachments = new();
    private readonly HashSet<Guid> _attachingWindows = new();
    private readonly PickRegistry _picks = new();
    private Guid? _frontmostWindowId;
    private bool _disposed;

    public LiveApplicationHost(LiveApplicationHostOptions? options = null)
    {
        options ??= new LiveApplicationHostOptions();
        _treeEventTimeProvider = options.TreeEventTimeProvider;
        _stateDirectory = options.StateDir ?? PersistenceStore.DefaultDirectory;
        _homeDirectory = options.HomeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _windowController = options.WindowController;
        _recentClosedStore = new RecentClosedStore(_stateDirectory);
        Ring = new ControlEventRing();

        var index = LoadIndex();
        if (index is not null)
        {
            _windows.AddRange(index.Windows);
            foreach (var entry in index.Windows.Where(window => window.IsOpen))
            {
                _openWindows.Add(entry.Id, CreateWindowHost(
                    entry, _homeDirectory, seedIfEmpty: false, launchRestore: true));
            }
            FrontmostWindowId = index.Frontmost;
            if (_openWindows.Count == 0)
            {
                var fallback = index.Frontmost is { } frontmost
                    ? _windows.FirstOrDefault(window => window.Id == frontmost)
                    : null;
                fallback ??= _windows[0];
                SetOpen(fallback.Id, isOpen: true);
                _openWindows.Add(
                    fallback.Id,
                    CreateWindowHost(
                        fallback, _homeDirectory, seedIfEmpty: false, launchRestore: true));
            }
        }
        else
        {
            if (!RecoverOrphanedWindows(_homeDirectory)
                && !MigrateLegacy(_homeDirectory))
            {
                SeedFreshWindow(_homeDirectory);
            }
        }
        SaveIndex();

        // _stateDirectory is options.StateDir ?? PersistenceStore.DefaultDirectory, the same fallback
        // the pipe name has always been derived with.
        var socketPath = options.PipeName is null
            ? ControlResolve.SocketPath(options.StateDir, _stateDirectory)
            : options.SocketPath;
        PipeName = options.PipeName ?? ControlPipeName.Derive(socketPath!);
        SocketPath = ControlPipeName.AdvertisedSocketPath(PipeName, socketPath);
        _server = new ControlPipeServer(PipeName, DispatchAsync);
    }

    public IReadOnlyList<LiveWindowEntry> Windows
    {
        get
        {
            lock (_gate) return _windows.ToArray();
        }
    }

    public Guid? FrontmostWindowId
    {
        get
        {
            lock (_gate) return _frontmostWindowId;
        }
        private set
        {
            lock (_gate) _frontmostWindowId = value;
        }
    }

    public Guid? ActiveWindowId
    {
        get
        {
            lock (_gate) return ActiveWindowIdLocked();
        }
    }

    public IReadOnlyList<Guid> LaunchWindowIds
    {
        get
        {
            lock (_gate)
            {
                var active = ActiveWindowIdLocked();
                return _windows
                    .Where(window => window.IsOpen)
                    .Select(window => window.Id)
                    .OrderBy(windowId => windowId == active ? 1 : 0)
                    .ToArray();
            }
        }
    }

    public ControlEventRing Ring { get; }

    public IReadOnlyList<RecentClosedItem> RecentClosedItems => _recentClosedStore.Load();

    public string PipeName { get; }

    /// <summary>The socket path shells get as AGTERM_SOCKET, or null when none derives to
    /// <see cref="PipeName"/> (the AGTERM_CONTROL_SOCKET override).</summary>
    public string? SocketPath { get; }

    public bool IsListening => _server.IsListening;

    public async Task<bool> ReopenLatestRecentClosedAsync(
        Guid targetWindowId,
        CancellationToken cancellationToken = default)
    {
        await _recentClosedGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                LiveWindowHost window;
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (!_openWindows.TryGetValue(targetWindowId, out window!)) return false;
                }

                var item = _recentClosedStore.Load().FirstOrDefault();
                if (item is null) return false;
                var restored = false;
                window.RunLocked(() => restored = window.Store.RestoreRecentClosed(item));
                if (restored) _recentClosedStore.Remove(item.Id);
                return restored;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _recentClosedGate.Release();
        }
    }

    public void Start()
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
        _server.Start();
    }

    public async Task<ControlResponse> SetThemeAsync(
        ControlArgs args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        return await DispatchAppGlobalAsync(
            new ControlRequest { Cmd = Command.ThemeSet, Args = args },
            cancellationToken).ConfigureAwait(false)
            ?? new ControlResponse { Ok = false, Error = "no such window: active" };
    }

    /// <summary>Sends workspace.theme to one window, the way its sidebar's theme palette saves, so
    /// the palette gets the same answer and errors agtermctl would.</summary>
    public async Task<ControlResponse> SetWorkspaceThemeAsync(
        Guid windowId,
        Guid workspaceId,
        string? themeName,
        CancellationToken cancellationToken = default) =>
        await DispatchAsync(
            new ControlRequest
            {
                Cmd = Command.WorkspaceTheme,
                Target = workspaceId.ToString("D"),
                Args = new ControlArgs { Name = themeName, Window = windowId.ToString("D") },
            },
            cancellationToken).ConfigureAwait(false)
        ?? new ControlResponse { Ok = false, Error = "no such window: active" };

    public Task<ControlResponse> CreateWindowAsync(
        string? name = null,
        CancellationToken cancellationToken = default) =>
        _windowController is null
            ? Task.FromResult(new ControlResponse
            {
                Ok = false,
                Error = HeadlessControlActions.WindowManagementUnavailable,
            })
            : NewWindowAsync(name, cancellationToken);

    public ControlResponse RenameWindow(Guid windowId, string name) =>
        RenameWindow(windowId.ToString("D"), name);

    public Task<ControlResponse> DeleteWindowAsync(
        Guid windowId,
        CancellationToken cancellationToken = default) =>
        _windowController is null
            ? Task.FromResult(new ControlResponse
            {
                Ok = false,
                Error = HeadlessControlActions.WindowManagementUnavailable,
            })
            : DeleteWindowAsync(windowId.ToString("D"), cancellationToken);

    public LiveWindowHost GetOpenWindow(Guid windowId)
    {
        lock (_gate)
        {
            return _openWindows.TryGetValue(windowId, out var window)
                ? window
                : throw new KeyNotFoundException($"window is not open: {windowId:D}");
        }
    }

    public LiveControlHost AttachWindow(Guid windowId, LiveControlHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        LiveWindowHost window;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_attachments.ContainsKey(windowId) || !_attachingWindows.Add(windowId))
                throw new InvalidOperationException($"window is already attached: {windowId:D}");
            if (!_openWindows.TryGetValue(windowId, out window!))
            {
                _attachingWindows.Remove(windowId);
                throw new KeyNotFoundException($"window is not open: {windowId:D}");
            }
        }

        LiveControlHost? attached = null;
        try
        {
            attached = new LiveControlHost(
                options with
                {
                    StateDir = options.StateDir ?? _stateDirectory,
                    PipeName = PipeName,
                    SocketPath = SocketPath,
                    WindowName = window.WindowName,
                },
                window,
                PipeName);
            lock (_gate)
            {
                if (_disposed || !_openWindows.ContainsKey(windowId))
                    throw new InvalidOperationException($"window closed while attaching: {windowId:D}");
                _attachments.Add(windowId, attached);
                _attachingWindows.Remove(windowId);
            }
            return attached;
        }
        catch
        {
            lock (_gate) _attachingWindows.Remove(windowId);
            attached?.Dispose();
            throw;
        }
    }

    public void ReportAcceptedPane(Guid windowId, string? paneId)
    {
        lock (_gate)
        {
            if (!_disposed && _openWindows.TryGetValue(windowId, out var window))
                window.AcceptedPaneId = paneId;
        }
    }

    public Task ReportActivatedAsync(Guid windowId)
    {
        LiveWindowHost window;
        string? acceptedPaneId;
        lock (_gate)
        {
            if (_disposed || !_openWindows.TryGetValue(windowId, out window!))
                return Task.CompletedTask;

            if (_frontmostWindowId != windowId)
            {
                _frontmostWindowId = windowId;
                SaveIndexLocked();
            }
            acceptedPaneId = window.AcceptedPaneId;
        }
        return Task.Run(() => window.RunLocked(() =>
        {
            // The accepted layout may change while this callback waits for the store gate.
            if (acceptedPaneId is null || window.AcceptedPaneId != acceptedPaneId)
                return;
            var session = window.Store.Workspaces.SelectMany(workspace => workspace.Sessions)
                .FirstOrDefault(candidate => candidate.Surface?.PaneToken == acceptedPaneId
                    || candidate.SplitSurface?.PaneToken == acceptedPaneId);
            if (session is { UnseenCount: > 0 })
                window.Store.ClearUnseen(session.Id);
        }));
    }

    public async Task CloseWindowAsync(
        Guid windowId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LiveWindowHost window;
        LiveControlHost? attachment;
        lock (_gate)
        {
            if (_disposed || !_openWindows.Remove(windowId, out window!))
                return;

            _picks.Unregister(windowId);
            window.StopDispatching();
            _closingWindows.Add(windowId, window);
            _attachments.Remove(windowId, out attachment);
            SetOpen(windowId, isOpen: false);
            if (_frontmostWindowId == windowId)
                _frontmostWindowId = ActiveWindowIdLocked();
            SaveIndexLocked();
        }

        try
        {
            await window.WaitForDispatchQuiescenceAsync().ConfigureAwait(false);
            window.RunLocked(window.Store.CancelAllAsks);
            window.RunLocked(window.Store.CloseAllHuds);
            EmitWindowLifecycleEvents(
                windowId,
                window.CaptureLifecycleSessions(),
                ControlEventKind.SessionClosed);
            if (attachment is not null)
                await attachment.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            window.Save();
            FlushTreeEvents(windowId);
            lock (_gate) _closingWindows.Remove(windowId);
        }
    }

    public void Dispose()
    {
        LiveControlHost[] attachments;
        LiveWindowHost[] windows;
        Guid[] windowIds;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            attachments = _attachments.Values.ToArray();
            windows = _openWindows.Values.ToArray();
            windowIds = _openWindows.Keys.ToArray();
            foreach (var window in windows) window.StopDispatching();
            _attachments.Clear();
            _attachingWindows.Clear();
        }

        // Unregister closes each controller as well as cancelling it, so a pick.open already in flight fails.
        foreach (var windowId in windowIds) _picks.Unregister(windowId);
        foreach (var window in windows)
        {
            window.RunLocked(window.Store.CancelAllAsks);
            // a closed window's store is never shown again; a live auto-hide must not fire into it
            window.RunLocked(window.Store.CloseAllHuds);
        }
        _server.Stop();
        foreach (var attachment in attachments) attachment.Dispose();
        foreach (var window in windows) window.Save();
        SaveIndex();
        FlushTreeEvents();
    }

    private async Task<ControlResponse?> DispatchAsync(
        ControlRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Cmd == Command.WindowList)
            return await ListWindowsAsync(cancellationToken).ConfigureAwait(false);
        if (request.Cmd == Command.WindowNew && _windowController is not null)
            return await NewWindowAsync(request.Args?.Name, cancellationToken).ConfigureAwait(false);
        if (request.Cmd == Command.WindowSelect && _windowController is not null)
            return await SelectWindowAsync(request.Target, cancellationToken).ConfigureAwait(false);
        if (request.Cmd == Command.WindowClose && _windowController is not null)
            return await CloseWindowCommandAsync(request.Target, cancellationToken).ConfigureAwait(false);
        if (request.Cmd == Command.WindowRename && _windowController is not null)
            return RenameWindow(request.Target, request.Args?.Name);
        if (request.Cmd == Command.WindowDelete && _windowController is not null)
            return await DeleteWindowAsync(request.Target, cancellationToken).ConfigureAwait(false);
        if (request.Cmd == Command.WindowResize && _windowController is not null)
            return await ResizeWindowAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.Cmd == Command.WindowMove && _windowController is not null)
            return await MoveWindowAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.Cmd == Command.WindowZoom && _windowController is not null)
            return await ToggleWindowZoomAsync(request.Target, cancellationToken).ConfigureAwait(false);
        if (request.Cmd == Command.WindowFullscreen && _windowController is not null)
            return await ToggleWindowFullscreenAsync(request.Target, cancellationToken).ConfigureAwait(false);
        if (IsWindowCommand(request.Cmd))
            return await DispatchActiveWindowAdapterAsync(request, cancellationToken).ConfigureAwait(false);
        if (IsAppGlobalMutation(request.Cmd))
            return await DispatchAppGlobalAsync(request, cancellationToken).ConfigureAwait(false);

        // pick.result/pick.cancel follow the pick id across every window and past a window's close, so a
        // frontmost change while a CLI polls cannot orphan its pick
        if (request.Cmd is Command.PickResult or Command.PickCancel && request.Target is { } pickId)
            return RoutePickLookup(request.Cmd, pickId, request.Args?.Window);
        // ask ids are global: a lookup follows the id to its owner window, open or closed, so a
        // frontmost change while a CLI polls cannot orphan the question
        if (request.Cmd == Command.AskResult && request.Target is { } askId)
            return RouteAskResult(askId, request.Args?.Window);
        if (request.Cmd == Command.AskCancel && request.Target is { } cancelId)
            return await RouteAskCancelAsync(request, cancelId, cancellationToken).ConfigureAwait(false);

        var target = request.Args?.Window?.Trim();
        LiveWindowEntry[] entries;
        Dictionary<Guid, LiveWindowHost> openWindows;
        Guid? activeWindowId;
        lock (_gate)
        {
            entries = _windows.ToArray();
            openWindows = new Dictionary<Guid, LiveWindowHost>(_openWindows);
            activeWindowId = ActiveWindowIdLocked();
        }
        Guid windowId;
        if (string.IsNullOrEmpty(target))
        {
            if (activeWindowId is not { } active)
                return new ControlResponse
                {
                    Ok = false,
                    Error = request.Cmd is Command.PickOpen or Command.AskOpen ? "no open window" : "no such window: active",
                };
            windowId = active;
        }
        else
        {
            var resolution = ControlResolve.Resolve(
                target,
                entries.Select(window => window.Id).ToArray(),
                activeWindowId);
            if (resolution.Kind != TargetResolution.Outcome.Resolved)
                return new ControlResponse
                {
                    Ok = false,
                    Error = ControlResolve.ErrorMessage("window", target, resolution),
                };
            windowId = resolution.Id;
        }

        if (string.IsNullOrEmpty(target)
            && UsesSingleSessionTarget(request.Cmd)
            && request.Target is { } sessionTarget
            && sessionTarget != "active")
        {
            var owners = entries
                .Where(entry => entry.IsOpen)
                .Select(entry => (Entry: entry, Host: openWindows[entry.Id]))
                .SelectMany(pair => pair.Host.CaptureSessionIds()
                    .Select(sessionId => (pair.Host, SessionId: sessionId)))
                .ToArray();
            var resolution = ControlResolve.Resolve(
                sessionTarget,
                owners.Select(owner => owner.SessionId).ToArray(),
                active: null);
            if (resolution.Kind != TargetResolution.Outcome.Resolved)
                return new ControlResponse
                {
                    Ok = false,
                    Error = ControlResolve.ErrorMessage("session", sessionTarget, resolution),
                };
            windowId = owners.First(owner => owner.SessionId == resolution.Id).Host.WindowId;
        }
        else if (string.IsNullOrEmpty(target)
            && request.Cmd is Command.SessionClose or Command.SessionMove
            && request.Args?.Targets is [var firstTarget, ..]
            && firstTarget != "active")
        {
            var owners = entries
                .Where(entry => entry.IsOpen)
                .Select(entry => (Entry: entry, Host: openWindows[entry.Id]))
                .SelectMany(pair => pair.Host.CaptureSessionIds()
                    .Select(sessionId => (pair.Host, SessionId: sessionId)))
                .ToArray();
            var resolution = ControlResolve.Resolve(
                firstTarget,
                owners.Select(owner => owner.SessionId).ToArray(),
                active: null);
            if (resolution.Kind != TargetResolution.Outcome.Resolved)
                return new ControlResponse
                {
                    Ok = false,
                    Error = ControlResolve.ErrorMessage("session", firstTarget, resolution),
                };
            windowId = owners.First(owner => owner.SessionId == resolution.Id).Host.WindowId;
        }
        else if (string.IsNullOrEmpty(target)
            && request.Cmd == Command.Dashboard
            && request.Args?.Close != true
            && request.Args?.Mru != true
            && request.Args?.Targets is [var dashboardTarget, ..]
            && dashboardTarget is not null
            && dashboardTarget != "active")
        {
            var owners = entries
                .Where(entry => entry.IsOpen)
                .Select(entry => (Entry: entry, Host: openWindows[entry.Id]))
                .SelectMany(pair => pair.Host.CaptureSessionIds()
                    .Select(sessionId => (pair.Host, SessionId: sessionId)))
                .ToArray();
            var resolution = ControlResolve.Resolve(
                dashboardTarget,
                owners.Select(owner => owner.SessionId).ToArray(),
                active: null);
            if (resolution.Kind != TargetResolution.Outcome.Resolved)
                return new ControlResponse
                {
                    Ok = false,
                    Error = ControlResolve.ErrorMessage("session", dashboardTarget, resolution),
                };
            windowId = owners.First(owner => owner.SessionId == resolution.Id).Host.WindowId;
        }
        else if (string.IsNullOrEmpty(target)
            && UsesWorkspaceTarget(request.Cmd)
            && request.Target is { } workspaceTarget
            && workspaceTarget != "active")
        {
            var owners = entries
                .Where(entry => entry.IsOpen)
                .Select(entry => (Entry: entry, Host: openWindows[entry.Id]))
                .SelectMany(pair => pair.Host.CaptureWorkspaceIds()
                    .Select(workspaceId => (pair.Host, WorkspaceId: workspaceId)))
                .ToArray();
            var resolution = ControlResolve.Resolve(
                workspaceTarget,
                owners.Select(owner => owner.WorkspaceId).ToArray(),
                active: null);
            if (resolution.Kind != TargetResolution.Outcome.Resolved)
                return new ControlResponse
                {
                    Ok = false,
                    Error = ControlResolve.ErrorMessage("workspace", workspaceTarget, resolution),
                };
            windowId = owners.First(owner => owner.WorkspaceId == resolution.Id).Host.WindowId;
        }

        if (!openWindows.TryGetValue(windowId, out var window))
            return new ControlResponse
            {
                Ok = false,
                Error = "window not open — window.select it first",
            };
        var response = await window.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.Cmd is Command.PickOpen or Command.AskOpen && request.Args?.Follow == true && response?.Ok == true
            && _windowController is not null)
        {
            try
            {
                await _windowController.ActivateWindowAsync(windowId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The modal is open and the client needs its id, like window.select with a refused activation.
            }
            await ReportActivatedAsync(windowId).ConfigureAwait(false);
        }
        return response;
    }

    /// <summary>A pick or ask lookup's --window: any known window, open or closed, so a retained
    /// result of a closed window stays readable.</summary>
    private ControlResponse? ResolveLookupWindow(string? window, out Guid? windowId)
    {
        windowId = null;
        var selector = window?.Trim();
        if (string.IsNullOrEmpty(selector)) return null;
        LiveWindowEntry[] entries;
        Guid? active;
        lock (_gate)
        {
            entries = _windows.ToArray();
            active = ActiveWindowIdLocked();
        }
        var resolution = ControlResolve.Resolve(selector, entries.Select(entry => entry.Id).ToArray(), active);
        if (resolution.Kind != TargetResolution.Outcome.Resolved)
            return new ControlResponse { Ok = false, Error = ControlResolve.ErrorMessage("window", selector, resolution) };
        windowId = resolution.Id;
        return null;
    }

    private ControlResponse RoutePickLookup(Command command, string pickId, string? window)
    {
        if (ResolveLookupWindow(window, out var windowId) is { } error) return error;
        return command == Command.PickResult ? _picks.Result(pickId, windowId) : _picks.Cancel(pickId, windowId);
    }

    private ControlResponse RouteAskResult(string askId, string? window) =>
        ResolveLookupWindow(window, out var windowId) ?? _picks.Asks.Result(askId, windowId);

    /// <summary>A pending question lives under its owner window's model lock, so the cancel runs
    /// in that window, not in the frontmost one.</summary>
    private async Task<ControlResponse?> RouteAskCancelAsync(ControlRequest request, string askId, CancellationToken cancellationToken)
    {
        if (ResolveLookupWindow(request.Args?.Window, out var windowId) is { } error) return error;
        if (_picks.Asks.Find(askId) is not { } found || (windowId is { } wanted && found.WindowId != wanted))
            return AskRegistry.Unknown(askId);
        if (found.LiveOwner is null) return new ControlResponse { Ok = true };
        LiveWindowHost? owner;
        lock (_gate) _openWindows.TryGetValue(found.WindowId, out owner);
        // a window mid-close has left the open set or stopped dispatching, but its close cancels the
        // question: the caller asked for exactly that outcome
        if (owner is null) return new ControlResponse { Ok = true };
        var routed = request with { Args = (request.Args ?? new ControlArgs()) with { Window = null } };
        var response = await owner.DispatchAsync(routed, cancellationToken).ConfigureAwait(false);
        return response is { Ok: false, Error: LiveWindowHost.WindowNotOpenError }
            ? new ControlResponse { Ok = true }
            : response;
    }

    private async Task<ControlResponse> NewWindowAsync(
        string? requestedName,
        CancellationToken cancellationToken)
    {
        Guid windowId;
        LiveWindowHost window;
        Guid? previousFrontmost;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previousFrontmost = _frontmostWindowId;
            windowId = Guid.NewGuid();
            var cleaned = requestedName is null
                ? null
                : Session.TrimmedOrNull(TerminalText.Sanitized(requestedName));
            var name = cleaned ?? $"window {_windows.Count + 1}";
            var entry = new LiveWindowEntry(windowId, name, IsOpen: true);
            _windows.Add(entry);
            window = CreateWindowHost(
                entry, _homeDirectory, seedIfEmpty: true, launchRestore: false);
            _openWindows.Add(windowId, window);
            _frontmostWindowId = windowId;
            SaveIndexLocked();
        }

        try
        {
            await _windowController!.OpenWindowAsync(windowId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackFailedOpenAsync(
                windowId,
                window,
                removeEntry: true,
                previousFrontmost).ConfigureAwait(false);
            throw;
        }
        EmitWindowLifecycleEvents(
            windowId,
            window.CaptureLifecycleSessions(),
            ControlEventKind.SessionCreated);
        return new ControlResponse
        {
            Ok = true,
            Result = new ControlResult
            {
                Id = windowId.ToString("D").ToUpperInvariant(),
            },
        };
    }

    private async Task<ControlResponse> SelectWindowAsync(
        string? target,
        CancellationToken cancellationToken)
    {
        Guid windowId;
        bool wasOpen;
        Guid? previousFrontmost;
        LiveWindowHost? reopenedWindow = null;
        lock (_gate)
        {
            var resolution = ResolveWindowLocked(target);
            if (resolution.Error is not null)
                return resolution.Error;

            var entry = resolution.Entry!;
            windowId = entry.Id;
            previousFrontmost = _frontmostWindowId;
            wasOpen = _openWindows.ContainsKey(windowId);
            if (!wasOpen)
            {
                SetOpen(windowId, isOpen: true);
                reopenedWindow = CreateWindowHost(
                    entry,
                    _homeDirectory,
                    seedIfEmpty: false,
                    launchRestore: false);
                _openWindows.Add(
                    windowId,
                    reopenedWindow);
                SaveIndexLocked();
            }
        }

        if (wasOpen)
            await _windowController!.ActivateWindowAsync(windowId, cancellationToken).ConfigureAwait(false);
        else
        {
            try
            {
                await _windowController!.OpenWindowAsync(windowId, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await RollbackFailedOpenAsync(
                    windowId,
                    reopenedWindow!,
                    removeEntry: false,
                    previousFrontmost).ConfigureAwait(false);
                throw;
            }
            EmitWindowLifecycleEvents(
                windowId,
                reopenedWindow!.CaptureLifecycleSessions(),
                ControlEventKind.SessionCreated);
        }
        // The OS may refuse to activate a window for a background caller, and then no Activated event
        // arrives. The control reply promises the window is frontmost, so take it here.
        await ReportActivatedAsync(windowId).ConfigureAwait(false);
        return OkWindowId(windowId);
    }

    private async Task<ControlResponse> CloseWindowCommandAsync(
        string? target,
        CancellationToken cancellationToken)
    {
        Guid windowId;
        bool isOpen;
        lock (_gate)
        {
            var resolution = ResolveWindowLocked(target);
            if (resolution.Error is not null)
                return resolution.Error;
            windowId = resolution.Entry!.Id;
            isOpen = _openWindows.ContainsKey(windowId);
        }

        if (isOpen)
            await _windowController!.CloseWindowAsync(windowId, cancellationToken).ConfigureAwait(false);
        return OkWindowId(windowId);
    }

    private ControlResponse RenameWindow(string? target, string? requestedName)
    {
        var name = string.IsNullOrWhiteSpace(requestedName) ? null : requestedName.Trim();
        if (name is null)
        {
            return new ControlResponse
            {
                Ok = false,
                Error = "window.rename requires a name",
            };
        }

        var cleaned = Session.TrimmedOrNull(TerminalText.Sanitized(name));
        Guid windowId;
        LiveWindowHost? openWindow;
        lock (_gate)
        {
            var resolution = ResolveWindowLocked(target);
            if (resolution.Error is not null)
                return resolution.Error;

            windowId = resolution.Entry!.Id;
            if (cleaned is null)
                return OkWindowId(windowId);
            name = cleaned;
            var index = _windows.FindIndex(window => window.Id == windowId);
            if (_windows[index].Name == name)
                return OkWindowId(windowId);
            _windows[index] = _windows[index] with { Name = name };
            _openWindows.TryGetValue(windowId, out openWindow);
            SaveIndexLocked();
        }
        openWindow?.Rename(name);
        EmitTreeChanged(windowId);
        return OkWindowId(windowId);
    }

    private async Task RollbackFailedOpenAsync(
        Guid windowId,
        LiveWindowHost window,
        bool removeEntry,
        Guid? previousFrontmost)
    {
        LiveControlHost? attachment;
        _picks.Unregister(windowId);
        window.StopDispatching();
        lock (_gate)
        {
            _openWindows.Remove(windowId);
            _attachments.Remove(windowId, out attachment);
            _attachingWindows.Remove(windowId);
            if (removeEntry)
                _windows.RemoveAll(entry => entry.Id == windowId);
            else
                SetOpen(windowId, isOpen: false);
            _frontmostWindowId = previousFrontmost is { } previous
                && _openWindows.ContainsKey(previous)
                    ? previous
                    : ActiveWindowIdLocked();
            SaveIndexLocked();
        }

        await window.WaitForDispatchQuiescenceAsync().ConfigureAwait(false);
        window.RunLocked(window.Store.CancelAllAsks);
        window.RunLocked(window.Store.CloseAllHuds);
        if (attachment is not null)
            await attachment.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        window.CancelPendingSave();
        if (removeEntry)
            File.Delete(WindowSnapshotPath(windowId));
    }

    private async Task<ControlResponse> DeleteWindowAsync(
        string? target,
        CancellationToken cancellationToken)
    {
        Guid windowId;
        bool isOpen;
        LiveWindowHost? loadedWindow;
        lock (_gate)
        {
            var resolution = ResolveWindowLocked(target);
            if (resolution.Error is not null)
                return resolution.Error;
            if (_windows.Count == 1)
            {
                return new ControlResponse
                {
                    Ok = false,
                    Error = "cannot delete last window",
                };
            }
            windowId = resolution.Entry!.Id;
            isOpen = _openWindows.TryGetValue(windowId, out loadedWindow);
        }
        var sessionIds = loadedWindow?.CaptureSessionIds()
            ?? LoadWindowSessionIds(windowId);

        if (isOpen)
        {
            await _windowController!.CloseWindowAsync(windowId, cancellationToken).ConfigureAwait(false);
            await CloseWindowAsync(windowId, cancellationToken).ConfigureAwait(false);
        }
        loadedWindow?.CancelPendingSave();

        lock (_gate)
        {
            var index = _windows.FindIndex(window => window.Id == windowId);
            if (index >= 0)
                _windows.RemoveAt(index);
            if (_frontmostWindowId == windowId)
                _frontmostWindowId = ActiveWindowIdLocked();
            SaveIndexLocked();
        }

        File.Delete(WindowSnapshotPath(windowId));
        foreach (var sessionId in sessionIds)
        {
            WatermarkStorage.RemoveRenderedText(sessionId, _stateDirectory);
            ScrollbackStorage.Remove(sessionId, _stateDirectory);
        }
        if (!isOpen)
            EmitTreeChanged(windowId);
        FlushTreeEvents(windowId);
        return OkWindowId(windowId);
    }

    private Guid[] LoadWindowSessionIds(Guid windowId) =>
        new PersistenceStore(
            Path.Combine(_stateDirectory, "windows"),
            $"{windowId:D}.json")
            .Load()
            .Workspaces
            .SelectMany(workspace => workspace.Sessions)
            .Select(session => session.Id)
            .ToArray();

    private string WindowSnapshotPath(Guid windowId) =>
        Path.Combine(_stateDirectory, "windows", $"{windowId:D}.json");

    private void EmitWindowLifecycleEvents(
        Guid windowId,
        IReadOnlyList<LiveWindowSessionEvent> sessions,
        ControlEventKind kind)
    {
        var window = windowId.ToString("D").ToUpperInvariant();
        foreach (var session in sessions)
        {
            Ring.Append(new ControlEventDraft
            {
                Kind = kind,
                Window = window,
                Workspace = session.WorkspaceId.ToString("D").ToUpperInvariant(),
                Session = session.SessionId.ToString("D").ToUpperInvariant(),
                Payload = new ControlEventPayload { Name = session.Name },
            });
        }
        EmitTreeChanged(windowId);
    }

    private void EmitTreeChanged(Guid windowId)
    {
        lock (_treeEventGate)
        {
            if (_treeEventTimers.Remove(windowId, out var previous)) previous.Dispose();
            ITimer? timer = null;
            timer = _treeEventTimeProvider.CreateTimer(_ =>
            {
                lock (_treeEventGate)
                {
                    // A disposed timer may already have queued its callback before a reschedule.
                    if (_treeEventTimers.TryGetValue(windowId, out var pending) && pending == timer)
                        FlushTreeEvents(windowId);
                }
            }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _treeEventTimers.Add(windowId, timer);
            timer.Change(TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Synchronously publishes pending invalidations, including deleted windows.</summary>
    public void FlushTreeEvents()
    {
        lock (_treeEventGate)
        {
            foreach (var windowId in _treeEventTimers.Keys.ToArray())
                FlushTreeEvents(windowId);
        }
    }

    private void FlushTreeEvents(Guid windowId)
    {
        lock (_treeEventGate)
        {
            if (!_treeEventTimers.Remove(windowId, out var timer)) return;
            timer.Dispose();
            Ring.Append(new ControlEventDraft
            {
                Kind = ControlEventKind.TreeChanged,
                Window = windowId.ToString("D").ToUpperInvariant(),
            });
        }
    }

    private async Task<ControlResponse> ResizeWindowAsync(
        ControlRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Args?.Width is not { } width || request.Args?.Height is not { } height
            || width <= 0 || height <= 0)
        {
            return new ControlResponse
            {
                Ok = false,
                Error = "window.resize requires positive width and height",
            };
        }

        var resolution = ResolveOpenWindow(request.Target);
        if (resolution.Error is not null)
            return resolution.Error;
        await _windowController!.ResizeWindowAsync(
            resolution.WindowId!.Value,
            width,
            height,
            cancellationToken).ConfigureAwait(false);
        return OkWindowId(resolution.WindowId.Value);
    }

    private async Task<ControlResponse> MoveWindowAsync(
        ControlRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Args?.X is not { } x || request.Args?.Y is not { } y)
        {
            return new ControlResponse
            {
                Ok = false,
                Error = "window.move requires x and y",
            };
        }

        if (request.Args.Display is { } display)
        {
            var displayCount = await _windowController!.GetDisplayCountAsync(cancellationToken)
                .ConfigureAwait(false);
            if (display < 0 || display >= displayCount)
            {
                return new ControlResponse
                {
                    Ok = false,
                    Error = $"display {display} out of range (have {displayCount})",
                };
            }
        }

        var resolution = ResolveOpenWindow(request.Target);
        if (resolution.Error is not null)
            return resolution.Error;
        var windowId = resolution.WindowId!.Value;
        var moved = await _windowController!.MoveWindowAsync(
            windowId,
            x,
            y,
            request.Args.Display,
            cancellationToken).ConfigureAwait(false);
        if (!moved.Applied)
        {
            return new ControlResponse
            {
                Ok = false,
                Error = $"display {request.Args.Display} out of range (have {moved.DisplayCount})",
            };
        }
        return OkWindowId(windowId);
    }

    private async Task<ControlResponse> ToggleWindowZoomAsync(
        string? target,
        CancellationToken cancellationToken)
    {
        var resolution = ResolveOpenWindow(target);
        if (resolution.Error is not null)
            return resolution.Error;
        var windowId = resolution.WindowId!.Value;
        await _windowController!.ToggleZoomAsync(windowId, cancellationToken).ConfigureAwait(false);
        return OkWindowId(windowId);
    }

    private async Task<ControlResponse> ToggleWindowFullscreenAsync(
        string? target,
        CancellationToken cancellationToken)
    {
        var resolution = ResolveOpenWindow(target);
        if (resolution.Error is not null)
            return resolution.Error;
        var windowId = resolution.WindowId!.Value;
        await _windowController!.ToggleFullscreenAsync(windowId, cancellationToken).ConfigureAwait(false);
        return OkWindowId(windowId);
    }

    private async Task<ControlResponse> ListWindowsAsync(CancellationToken cancellationToken)
    {
        LiveWindowEntry[] entries;
        Dictionary<Guid, LiveWindowHost> openWindows;
        Guid? activeWindowId;
        lock (_gate)
        {
            entries = _windows.ToArray();
            openWindows = new Dictionary<Guid, LiveWindowHost>(_openWindows);
            activeWindowId = ActiveWindowIdLocked();
        }

        var windows = new List<ControlWindowNode>(entries.Length);
        foreach (var entry in entries)
        {
            var isOpen = openWindows.TryGetValue(entry.Id, out var window);
            var state = isOpen ? window!.CaptureWindowListState() : default;
            var presentation = isOpen && _windowController is not null
                ? await _windowController.CaptureWindowAsync(entry.Id, cancellationToken)
                    .ConfigureAwait(false)
                : null;
            windows.Add(new ControlWindowNode
            {
                Id = entry.Id.ToString("D").ToUpperInvariant(),
                Name = entry.Name,
                Open = isOpen,
                Active = isOpen && entry.Id == activeWindowId,
                AutoFollowMs = isOpen ? state.AutoFollowMs : null,
                SidebarVisible = isOpen ? state.SidebarVisible : null,
                Geometry = presentation?.Geometry,
                Fullscreen = presentation?.Fullscreen,
                Zoomed = presentation?.Zoomed,
            });
        }
        return new ControlResponse
        {
            Ok = true,
            Result = new ControlResult { Windows = windows.ToArray() },
        };
    }

    private async Task<ControlResponse?> DispatchActiveWindowAdapterAsync(
        ControlRequest request,
        CancellationToken cancellationToken)
    {
        LiveWindowHost? window;
        lock (_gate)
        {
            window = ActiveWindowIdLocked() is { } active
                && _openWindows.TryGetValue(active, out var activeWindow)
                    ? activeWindow
                    : null;
        }
        if (window is null)
        {
            return new ControlResponse
            {
                Ok = false,
                Error = "no such window: active",
            };
        }
        return await window.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private (LiveWindowEntry? Entry, ControlResponse? Error) ResolveWindowLocked(string? target)
    {
        var normalized = target is null ? "active" : target.Trim();
        var resolution = ControlResolve.Resolve(
            normalized,
            _windows.Select(window => window.Id).ToArray(),
            ActiveWindowIdLocked());
        if (resolution.Kind != TargetResolution.Outcome.Resolved)
        {
            return (null, new ControlResponse
            {
                Ok = false,
                Error = ControlResolve.ErrorMessage("window", normalized, resolution),
            });
        }
        return (_windows.First(window => window.Id == resolution.Id), null);
    }

    private (Guid? WindowId, ControlResponse? Error) ResolveOpenWindow(string? target)
    {
        lock (_gate)
        {
            var resolution = ResolveWindowLocked(target);
            if (resolution.Error is not null)
                return (null, resolution.Error);
            var windowId = resolution.Entry!.Id;
            if (!_openWindows.ContainsKey(windowId))
            {
                return (null, new ControlResponse
                {
                    Ok = false,
                    Error = "window not open — window.select it first",
                });
            }
            return (windowId, null);
        }
    }

    private static ControlResponse OkWindowId(Guid windowId) => new()
    {
        Ok = true,
        Result = new ControlResult
        {
            Id = windowId.ToString("D").ToUpperInvariant(),
        },
    };

    private async Task<ControlResponse?> DispatchAppGlobalAsync(
        ControlRequest request,
        CancellationToken cancellationToken)
    {
        LiveWindowHost[] windows;
        lock (_gate)
        {
            var activeWindowId = ActiveWindowIdLocked();
            windows = _windows
                .Where(entry => entry.IsOpen)
                .Select(entry => _openWindows[entry.Id])
                .OrderBy(window => window.WindowId == activeWindowId ? 0 : 1)
                .ToArray();
        }
        ControlResponse? firstResponse = null;
        foreach (var window in windows)
        {
            var response = await window.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
            firstResponse ??= response;
            if (response is { Ok: false })
                return response;
        }
        if (request.Cmd == Command.ConfigReload)
            ResetSessionFontSizes(cancellationToken);
        return firstResponse ?? new ControlResponse
        {
            Ok = false,
            Error = "no such window: active",
        };
    }

    private static bool IsAppGlobalMutation(Command command) => command is
        Command.KeymapReload
        or Command.ConfigReload
        or Command.ThemeSet
        or Command.RestoreClear;

    private static bool IsWindowCommand(Command command) => command is
        Command.WindowNew
        or Command.WindowList
        or Command.WindowSelect
        or Command.WindowClose
        or Command.WindowRename
        or Command.WindowDelete
        or Command.WindowResize
        or Command.WindowMove
        or Command.WindowZoom
        or Command.WindowFullscreen;

    private static bool UsesSingleSessionTarget(Command command) => command is
        Command.SessionDuplicate
        or Command.AskOpen
        or Command.SessionHudOpen
        or Command.SessionHudUpdate
        or Command.SessionHudClose
        or Command.SessionSelect
        or Command.SessionClose
        or Command.SessionRename
        or Command.SessionReveal
        or Command.SessionMove
        or Command.SessionFlag
        or Command.SessionSeen
        or Command.SessionStatus
        or Command.SessionRestore
        or Command.SessionSplit
        or Command.SessionScratch
        or Command.SessionFocus
        or Command.SessionResize
        or Command.SurfaceZoom
        or Command.SessionType
        or Command.SessionCopy
        or Command.SessionPaste
        or Command.SessionSelectAll
        or Command.SessionSearch
        or Command.SessionOverlayOpen
        or Command.SessionOverlayClose
        or Command.SessionOverlayResize
        or Command.SessionOverlayResult
        or Command.SessionBackground
        or Command.SessionText
        or Command.FontInc
        or Command.FontDec
        or Command.FontReset
        or Command.Notify;

    private static bool UsesWorkspaceTarget(Command command) => command is
        Command.WorkspaceSelect
        or Command.WorkspaceRename
        or Command.WorkspaceDelete
        or Command.WorkspaceMove
        or Command.WorkspaceFocus
        or Command.WorkspaceCollapse
        or Command.WorkspaceExpand
        or Command.WorkspaceTheme;

    private void ResetSessionFontSizes(CancellationToken cancellationToken)
    {
        LiveWindowHost[] loadedWindows;
        Guid[] unloadedWindowIds;
        lock (_gate)
        {
            loadedWindows = _openWindows.Values
                .Concat(_closingWindows.Values)
                .Distinct()
                .ToArray();
            unloadedWindowIds = _windows
                .Where(entry => !entry.IsOpen && !_closingWindows.ContainsKey(entry.Id))
                .Select(entry => entry.Id)
                .ToArray();
        }

        foreach (var window in loadedWindows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            window.ResetSessionFontSizes();
        }
        foreach (var windowId in unloadedWindowIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetPersistedSessionFontSizes(windowId);
        }
    }

    private void ResetPersistedSessionFontSizes(Guid windowId)
    {
        var persistence = new PersistenceStore(
            Path.Combine(_stateDirectory, "windows"),
            $"{windowId:D}.json");
        var snapshot = persistence.Load();
        var changed = false;
        var workspaces = snapshot.Workspaces.Select(workspace => workspace with
        {
            Sessions = workspace.Sessions.Select(session =>
            {
                if (session.FontSize is null)
                    return session;
                changed = true;
                return session with { FontSize = null };
            }).ToArray(),
        }).ToArray();
        if (changed)
            persistence.Save(snapshot with { Workspaces = workspaces });
    }

    private LiveWindowHost CreateWindowHost(
        LiveWindowEntry entry,
        string homeDirectory,
        bool seedIfEmpty,
        bool launchRestore) =>
        new(
            entry.Id,
            entry.Name,
            _stateDirectory,
            homeDirectory,
            Ring,
            _recentClosedStore,
            seedIfEmpty,
            launchRestore,
            _picks,
            EmitTreeChanged);

    private void SetOpen(Guid windowId, bool isOpen)
    {
        var index = _windows.FindIndex(window => window.Id == windowId);
        if (index >= 0) _windows[index] = _windows[index] with { IsOpen = isOpen };
    }

    private Guid? ActiveWindowIdLocked() =>
        _frontmostWindowId is { } frontmost && _openWindows.ContainsKey(frontmost)
            ? frontmost
            : _windows.FirstOrDefault(window => window.IsOpen)?.Id;

    private bool RecoverOrphanedWindows(string homeDirectory)
    {
        var windowsDirectory = Path.Combine(_stateDirectory, "windows");
        if (!Directory.Exists(windowsDirectory))
            return false;

        var windowIds = Directory.EnumerateFiles(windowsDirectory, "*.json")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(path => Guid.TryParse(Path.GetFileNameWithoutExtension(path), out var id)
                ? id
                : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (windowIds.Length == 0)
            return false;

        for (var index = 0; index < windowIds.Length; index++)
        {
            var entry = new LiveWindowEntry(
                windowIds[index],
                $"window {index + 1}",
                IsOpen: true);
            _windows.Add(entry);
            _openWindows.Add(
                entry.Id,
                CreateWindowHost(
                    entry, homeDirectory, seedIfEmpty: false, launchRestore: true));
        }
        FrontmostWindowId = _windows[0].Id;
        return true;
    }

    private bool MigrateLegacy(string homeDirectory)
    {
        var snapshot = new PersistenceStore(_stateDirectory).Load();
        if (snapshot.Workspaces.Length == 0)
            return false;

        var windowId = Guid.NewGuid();
        new PersistenceStore(
            Path.Combine(_stateDirectory, "windows"),
            $"{windowId:D}.json").Save(snapshot);
        var entry = new LiveWindowEntry(windowId, "window 1", IsOpen: true);
        _windows.Add(entry);
        _openWindows.Add(
            windowId,
            CreateWindowHost(
                entry, homeDirectory, seedIfEmpty: false, launchRestore: true));
        FrontmostWindowId = windowId;
        return true;
    }

    private void SeedFreshWindow(string homeDirectory)
    {
        var windowId = Guid.NewGuid();
        var entry = new LiveWindowEntry(windowId, "window 1", IsOpen: true);
        _windows.Add(entry);
        _openWindows.Add(
            windowId,
            CreateWindowHost(
                entry, homeDirectory, seedIfEmpty: true, launchRestore: false));
        FrontmostWindowId = windowId;
    }

    private WindowsIndex? LoadIndex()
    {
        try
        {
            var path = Path.Combine(_stateDirectory, "windows.json");
            var index = ControlJson.Deserialize<WindowsIndex>(File.ReadAllText(path));
            return index is { Version: 1 } && index.Windows.Length > 0 ? index : null;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Text.Json.JsonException
                or NotSupportedException)
        {
            return null;
        }
    }

    private void SaveIndex()
    {
        lock (_gate) SaveIndexLocked();
    }

    private void SaveIndexLocked()
    {
        Directory.CreateDirectory(_stateDirectory);
        var path = Path.Combine(_stateDirectory, "windows.json");
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, ControlJson.Serialize(new WindowsIndex
        {
            Frontmost = _frontmostWindowId,
            Windows = _windows.ToArray(),
        }));
        AgTerm.Core.Model.AtomicFile.Replace(temp, path);
    }

    private sealed record WindowsIndex
    {
        [JsonPropertyName("version")]
        public int Version { get; init; } = 1;

        [JsonPropertyName("frontmost")]
        [JsonConverter(typeof(UppercaseGuidConverter))]
        public Guid? Frontmost { get; init; }

        [JsonPropertyName("windows")]
        public required LiveWindowEntry[] Windows { get; init; }
    }
}