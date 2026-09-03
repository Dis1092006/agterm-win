using System.Text.Json.Serialization;
using AgTerm.ControlServer;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Xterm;

public sealed record LiveApplicationHostOptions
{
    public string? StateDir { get; init; }

    public string? PipeName { get; init; }

    public string? HomeDirectory { get; init; }
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
    private readonly object _gate = new();
    private readonly string _stateDirectory;
    private readonly Dictionary<Guid, LiveWindowHost> _openWindows = new();
    private readonly Dictionary<Guid, LiveWindowHost> _closingWindows = new();
    private readonly List<LiveWindowEntry> _windows = new();
    private readonly ControlPipeServer _server;
    private readonly Dictionary<Guid, LiveControlHost> _attachments = new();
    private readonly HashSet<Guid> _attachingWindows = new();
    private Guid? _frontmostWindowId;
    private bool _disposed;

    public LiveApplicationHost(LiveApplicationHostOptions? options = null)
    {
        options ??= new LiveApplicationHostOptions();
        _stateDirectory = options.StateDir ?? PersistenceStore.DefaultDirectory;
        Ring = new ControlEventRing();

        var homeDirectory = options.HomeDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var index = LoadIndex();
        if (index is not null)
        {
            _windows.AddRange(index.Windows);
            foreach (var entry in index.Windows.Where(window => window.IsOpen))
            {
                _openWindows.Add(entry.Id, CreateWindowHost(entry, homeDirectory, seedIfEmpty: false));
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
                    CreateWindowHost(fallback, homeDirectory, seedIfEmpty: false));
            }
        }
        else
        {
            if (!RecoverOrphanedWindows(homeDirectory)
                && !MigrateLegacy(homeDirectory))
            {
                SeedFreshWindow(homeDirectory);
            }
        }
        SaveIndex();

        PipeName = options.PipeName
            ?? ControlPipeName.Derive(ControlResolve.SocketPath(
                options.StateDir,
                PersistenceStore.DefaultDirectory));
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

    public string PipeName { get; }

    public bool IsListening => _server.IsListening;

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

    public void ReportActivated(Guid windowId)
    {
        lock (_gate)
        {
            if (_disposed || !_openWindows.ContainsKey(windowId) || _frontmostWindowId == windowId)
                return;

            _frontmostWindowId = windowId;
            SaveIndexLocked();
        }
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
            if (attachment is not null)
                await attachment.ShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            window.Save();
            lock (_gate) _closingWindows.Remove(windowId);
        }
    }

    public void Dispose()
    {
        LiveControlHost[] attachments;
        LiveWindowHost[] windows;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            attachments = _attachments.Values.ToArray();
            windows = _openWindows.Values.ToArray();
            foreach (var window in windows) window.StopDispatching();
            _attachments.Clear();
            _attachingWindows.Clear();
        }

        _server.Stop();
        foreach (var attachment in attachments) attachment.Dispose();
        foreach (var window in windows) window.Save();
        SaveIndex();
    }

    private async Task<ControlResponse?> DispatchAsync(
        ControlRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsWindowCommand(request.Cmd))
            return await DispatchActiveWindowAdapterAsync(request, cancellationToken).ConfigureAwait(false);
        if (IsAppGlobalMutation(request.Cmd))
            return await DispatchAppGlobalAsync(request, cancellationToken).ConfigureAwait(false);

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
                    Error = "no such window: active",
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
        return await window.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
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
        or Command.WorkspaceExpand;

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
        bool seedIfEmpty) =>
        new(
            entry.Id,
            entry.Name,
            _stateDirectory,
            homeDirectory,
            Ring,
            seedIfEmpty);

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
                CreateWindowHost(entry, homeDirectory, seedIfEmpty: false));
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
            CreateWindowHost(entry, homeDirectory, seedIfEmpty: false));
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
            CreateWindowHost(entry, homeDirectory, seedIfEmpty: true));
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
        File.Move(temp, path, overwrite: true);
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