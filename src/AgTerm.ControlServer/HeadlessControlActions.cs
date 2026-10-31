using AgTerm.Core.Icons;
using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.ControlServer;

/// <summary>The app-level quick panel state. Its host owns the retained surface, not a window.</summary>
public sealed class HeadlessQuickTerminal
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _clock;
    private bool _visible;
    private bool _holdsKey;
    private bool _isZoomed;
    private bool _dismissesOnFocusLoss = true;
    private long? _autoHiddenAt;
    private IHeadlessSurface? _surface;

    public HeadlessQuickTerminal(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    public event Action? Changed;

    public Func<bool> CanShow { get; set; } = static () => true;

    public bool Visible
    {
        get { lock (_gate) return _visible; }
        set { if (value) Show(false); else Hide(); }
    }

    public bool HoldsKey { get { lock (_gate) return _holdsKey; } }
    public bool DismissesOnFocusLoss { get { lock (_gate) return _dismissesOnFocusLoss; } }
    public bool IsZoomed { get { lock (_gate) return _isZoomed; } }
    public IHeadlessSurface? Surface
    {
        get { lock (_gate) return _surface; }
        set { lock (_gate) _surface = value; }
    }

    public void Show(bool dismissOnFocusLoss = true)
    {
        if (!CanShow()) return;
        lock (_gate)
        {
            _dismissesOnFocusLoss &= dismissOnFocusLoss;
            _autoHiddenAt = null;
            _visible = true;
        }
        Changed?.Invoke();
    }

    public void Toggle(bool fromGlobalHotkey = false)
    {
        lock (_gate)
        {
            if (!_visible && !fromGlobalHotkey && _autoHiddenAt is { } hidden
                && _clock.GetElapsedTime(hidden) < TimeSpan.FromMilliseconds(300))
            {
                _autoHiddenAt = null;
                return;
            }
        }
        if (Visible) Hide(); else Show();
    }

    public void Hide()
    {
        lock (_gate)
        {
            _visible = _holdsKey = _isZoomed = false;
            _dismissesOnFocusLoss = true;
        }
        Changed?.Invoke();
    }

    public void SetKeyOwnership(bool holdsKey)
    {
        var hide = false;
        lock (_gate)
        {
            _holdsKey = holdsKey && _visible;
            hide = !holdsKey && _visible && _dismissesOnFocusLoss;
        }
        if (hide)
        {
            Hide();
            lock (_gate) _autoHiddenAt = _clock.GetTimestamp();
        }
        else Changed?.Invoke();
    }

    public bool SetZoom(ControlToggleMode mode)
    {
        lock (_gate)
        {
            if (!_visible) return false;
            _isZoomed = mode.DesiredValue(_isZoomed);
        }
        Changed?.Invoke();
        return true;
    }

    public void ShellExited()
    {
        lock (_gate)
        {
            _surface = null;
            _autoHiddenAt = null;
        }
        Hide();
    }
}

/// <summary>Headless port of the upstream DashboardController (agtermCore): the picked pane cells,
/// the keyboard highlight, the font mode, and the applied font size for the tree read-back.
/// isOpen derives from a non-empty member set, so Close() is the single source of open/closed.</summary>
public sealed class HeadlessDashboard
{
    public IReadOnlyList<DashboardMember> Members { get; private set; } = Array.Empty<DashboardMember>();

    public DashboardMember? Highlighted { get; private set; }

    public DashboardFontMode FontMode { get; private set; } = new DashboardFontMode.Untouched();

    private readonly Dictionary<DashboardMember, double> _cellFontSizes = [];
    private double? _appliedFontSize;

    /// <summary>Setting it drops every tile's chord size: upstream re-applies the dashboard
    /// override to each member when the applied size is set.</summary>
    public double? AppliedFontSize
    {
        get => _appliedFontSize;
        set
        {
            _appliedFontSize = value;
            _cellFontSizes.Clear();
        }
    }

    /// <summary>Tiles a font chord resized on a sized dashboard (upstream dashboardFontOverride
    /// changed on that surface). Not saved, and dropped when the overrides are re-applied.</summary>
    public IReadOnlyDictionary<DashboardMember, double> CellFontSizes => _cellFontSizes;

    public bool IsOpen => Members.Count > 0;

    /// <summary>The highlight starts on the first member, mirroring DashboardController.open.</summary>
    public void Open(IReadOnlyList<DashboardMember> members, DashboardFontMode fontMode)
    {
        Members = members;
        FontMode = fontMode;
        Highlighted = members.Count > 0 ? members[0] : null;
    }

    public void Close()
    {
        Members = Array.Empty<DashboardMember>();
        Highlighted = null;
        FontMode = new DashboardFontMode.Untouched();
        AppliedFontSize = null;
    }

    public bool Highlight(DashboardMember member)
    {
        if (!Members.Contains(member) || Highlighted == member)
            return false;
        Highlighted = member;
        return true;
    }

    public bool Reconcile(IReadOnlySet<DashboardMember> existing)
    {
        var survivors = Members.Where(existing.Contains).ToArray();
        if (survivors.Length == Members.Count)
            return false;
        if (survivors.Length == 0)
        {
            Close();
            return true;
        }

        Members = survivors;
        _cellFontSizes.Clear();
        if (Highlighted is not { } highlighted || !survivors.Contains(highlighted))
            Highlighted = survivors[0];
        return true;
    }

    /// <summary>Apply a font binding action to a member's tile, from its chord size or the applied
    /// size. A reset drops the chord size, so the tile shows the applied size again. The size stays
    /// at least one point, as ghostty clamps it.</summary>
    public bool AdjustCellFontSize(DashboardMember member, string action)
    {
        if (AppliedFontSize is not { } applied || !Members.Contains(member))
            return false;
        if (action == "reset_font_size")
        {
            _cellFontSizes.Remove(member);
            return true;
        }
        var current = _cellFontSizes.GetValueOrDefault(member, applied);
        double target;
        if (ParseFontStep(action, "increase_font_size:") is { } up)
            target = current + up;
        else if (ParseFontStep(action, "decrease_font_size:") is { } down)
            target = current - down;
        else
            return false;
        _cellFontSizes[member] = Math.Max(1, target);
        return true;
    }

    private static double? ParseFontStep(string action, string prefix) =>
        action.StartsWith(prefix, StringComparison.Ordinal) &&
        double.TryParse(action.AsSpan(prefix.Length), System.Globalization.CultureInfo.InvariantCulture,
            out var step)
            ? step
            : null;
}

/// <summary>In-memory theme slots — the headless stand-in for the upstream SettingsModel theme
/// state. Sync (macOS-appearance syncing upstream) derives from a set dark slot; while syncing the
/// single "current" theme is absent and the state rides sync/light/dark, matching the wire shape.</summary>
public sealed class HeadlessThemeSlots
{
    /// <summary>The light/single slot; null = ghostty's built-in default colors.</summary>
    public string? Light { get; set; }

    /// <summary>The dark slot; non-null turns appearance syncing on.</summary>
    public string? Dark { get; set; }

    public bool Sync => Dark is not null;

    public string? Current => Sync ? null : Light;
}

public sealed record HeadlessThemeSnapshot(string? Light, string? Dark, bool Sync);

/// <summary>A validated theme command's intent, replayed against the latest saved slots.</summary>
public sealed record HeadlessThemeUpdate(string? Light, string? Dark)
{
    public HeadlessThemeSnapshot Apply(HeadlessThemeSnapshot current)
    {
        if (string.Equals(Dark, "none", StringComparison.OrdinalIgnoreCase))
            return new(Light ?? current.Light, null, false);
        if (Dark is not null)
            return new(Light ?? (string.IsNullOrEmpty(current.Light) ? "Builtin Light" : current.Light), Dark, true);
        var dark = Light is null ? null : current.Dark;
        return new(Light, dark, dark is not null);
    }
}

public sealed record LiveNotificationRequest(Guid SessionId, string Title, string Body);

public sealed record LiveStatusNotificationRequest(Guid SessionId, AgentStatus Status, string? SoundName);

/// <summary>
/// The headless host's IControlActions: the port of the upstream app-side ControlServer action arms
/// (ControlServer+SessionActions/+SurfaceIO/+AppCommands/+WorkspaceCommands/+WindowCommands and the
/// setDashboard/clearRestoreCommands arms of ControlServer.swift) collapsed onto ONE window: one
/// AppStore, one event ring, one window identity. Target resolution ports ControlTargetResolver with
/// the cross-window search collapsed to the single store — the pinned "no such …"/"ambiguous …"
/// wire error strings come from ControlResolve.ErrorMessage unchanged. Terminal-engine behavior goes
/// through the IHeadlessSurface seam; where upstream would bounded-poll for a surface to realize,
/// the headless host checks once (surfaces attach externally and never realize asynchronously) and
/// answers with the upstream error string for the closest state.
/// </summary>
public sealed record HeadlessSearchUpdate(
    Guid SessionId,
    IHeadlessSurface Surface,
    bool Active,
    string Needle,
    string DisplayText,
    int? Selected,
    bool FocusInput,
    string? RestorePaneId);

public sealed class HeadlessControlActions : IControlActions
{
    /// <summary>The pinned wire error for the window arms that need real on-screen windows
    /// (new/close/resize/move/zoom/fullscreen) — a documented headless divergence (PLATFORM-NOTES):
    /// the headless host owns exactly one virtual window and no compositor.</summary>
    public const string WindowManagementUnavailable = "window management is not available in the headless host";

    /// <summary>Upstream DashboardLayout.maxCells — the dashboard grid's pane-cell cap.</summary>
    public const int DashboardMaxCells = 9;

    /// <summary>Upstream DashboardLayout.minFontSize — the auto-size floor.</summary>
    public const double DashboardMinFontSize = 6;

    private const int OnItemIndex = -1; // upstream SidebarDrop.onItemIndex (a drop ON a row)

    private readonly AppStore _store;
    private readonly ControlEventRing _ring;
    private KeymapStore _keymap;
    private readonly Func<int> _configReload;
    private readonly IReadOnlyList<string> _availableThemes;
    private readonly string _homeDirectory;
    private readonly bool _restoreRunningCommand;
    private readonly bool _closeGraceUndoEnabled;
    private double _dashboardBaseFontSize;
    private readonly Action<string>? _paneFocusRequested;
    private readonly Action<HeadlessSearchUpdate>? _searchUpdated;
    private readonly Action? _quickChanged;
    private readonly Action<bool>? _zoomActiveChanged;
    private readonly Action? _zoomChanged;
    private readonly Action? _dashboardChanged;
    private readonly Action<HeadlessThemeSnapshot>? _themeChanged;
    private readonly Func<LiveNotificationRequest, bool>? _notificationRequested;
    private readonly Action<LiveStatusNotificationRequest>? _statusNotificationRequested;
    private readonly SoundCatalog _sounds;
    private readonly Func<TerminalZoomTarget, bool>? _zoomTargetAvailable;

    public HeadlessControlActions(AppStore store, ControlEventRing ring, Guid windowId,
                                  string windowName, string configDirectory,
                                  IReadOnlyList<string>? availableThemes = null,
                                  Func<int>? configReload = null,
                                  bool restoreRunningCommand = false,
                                  bool closeGraceUndoEnabled = false,
                                  double dashboardBaseFontSize = 13,
                                  string? homeDirectory = null,
                                  Action<string>? paneFocusRequested = null,
                                  Action<HeadlessSearchUpdate>? searchUpdated = null,
                                  Action? quickChanged = null,
                                  Action<bool>? zoomActiveChanged = null,
                                  Action? zoomChanged = null,
                                  Action? dashboardChanged = null,
                                  Action<HeadlessThemeSnapshot>? themeChanged = null,
                                  Func<LiveNotificationRequest, bool>? notificationRequested = null,
                                  Action<LiveStatusNotificationRequest>? statusNotificationRequested = null,
                                  Func<TerminalZoomTarget, bool>? zoomTargetAvailable = null,
                                  PickController? pick = null,
                                  PickRegistry? picks = null,
                                  SoundCatalog? sounds = null,
                                  HeadlessQuickTerminal? quick = null)
    {
        Quick = quick ?? new HeadlessQuickTerminal();
        Picks = picks ?? new PickRegistry();
        Pick = pick ?? new PickController(Picks.Asks);
        if (pick is null) Picks.Register(windowId, Pick);
        _store = store;
        // one registry per application: terminal asks in any window and GUI asks share it
        _store.Asks = Picks.Asks;
        _ring = ring;
        WindowId = windowId;
        WindowName = windowName;
        _keymap = new KeymapStore(configDirectory);
        _availableThemes = availableThemes ?? Array.Empty<string>();
        // engine-less adaptation: upstream config.reload re-feeds libghostty and reports its
        // diagnostic count; the headless host has no engine, so the count is ctor-injectable.
        _configReload = configReload ?? (() => 0);
        _restoreRunningCommand = restoreRunningCommand;
        _closeGraceUndoEnabled = closeGraceUndoEnabled;
        _dashboardBaseFontSize = dashboardBaseFontSize;
        _homeDirectory = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _paneFocusRequested = paneFocusRequested;
        _searchUpdated = searchUpdated;
        _quickChanged = quickChanged;
        _zoomActiveChanged = zoomActiveChanged;
        _zoomChanged = zoomChanged;
        _dashboardChanged = dashboardChanged;
        _themeChanged = themeChanged;
        _notificationRequested = notificationRequested;
        _statusNotificationRequested = statusNotificationRequested;
        _sounds = sounds ?? new SoundCatalog(store.StateDirectory ?? configDirectory);
        _zoomTargetAvailable = zoomTargetAvailable;
        Zoom.TargetResolver = () => TerminalZoomController.ResolveTarget(_store, Quick.Visible);
    }

    public Guid WindowId { get; }

    /// <summary>The single window's name; window.rename mutates it.</summary>
    public string WindowName { get; private set; }

    public HeadlessQuickTerminal Quick { get; }

    public TerminalZoomController Zoom { get; } = new();

    public HeadlessDashboard Dashboard { get; } = new();

    public PickController Pick { get; }

    public PickRegistry Picks { get; }

    public AskRegistry Asks => Picks.Asks;

    public HeadlessThemeSlots Theme { get; } = new();

    // MARK: - Response helpers

    private static ControlResponse Err(string error) => new() { Ok = false, Error = error };

    private static ControlResponse Ok() => new() { Ok = true };

    private static ControlResponse OkId(Guid id) =>
        new() { Ok = true, Result = new ControlResult { Id = Uuid(id) } };

    /// <summary>The canonical uppercase UUID string (Swift Foundation's uuidString).</summary>
    private static string Uuid(Guid id) => id.ToString("D").ToUpperInvariant();

    private static string? Trimmed(string? value)
    {
        var result = value?.Trim();
        return string.IsNullOrEmpty(result) ? null : result;
    }

    // MARK: - Target resolution (ControlTargetResolver collapsed onto one store)

    private static IReadOnlyList<Guid> AllSessionIds(AppStore store) =>
        store.Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToArray();

    /// <summary>The window gate: null/blank window targets the (only) store; a set value resolves
    /// against the single window id with the pinned resolution errors. The single window is always
    /// open, so the upstream "window not open — window.select it first" arm is unreachable here.</summary>
    private ControlResponse? ResolveWindowGate(string? window)
    {
        if (Trimmed(window) is not { } target) return null;
        var resolution = ControlResolve.Resolve(target, new[] { WindowId }, WindowId);
        if (resolution.Kind == TargetResolution.Outcome.Resolved) return null;
        return Err(ControlResolve.ErrorMessage("window", target, resolution));
    }

    private ControlResponse ResolvePlacementStore(string? window, Func<AppStore, ControlResponse> body) =>
        ResolveWindowGate(window) is { } error ? error : body(_store);

    private static ControlResponse Resolve(string target, IReadOnlyList<Guid> candidates, Guid? active,
                                           string noun, Func<Guid, ControlResponse> body)
    {
        var resolution = ControlResolve.Resolve(target, candidates, active);
        if (resolution.Kind == TargetResolution.Outcome.Resolved) return body(resolution.Id);
        return Err(ControlResolve.ErrorMessage(noun, target, resolution));
    }

    private ControlResponse ResolveSession(string? target, string? window,
                                           Func<AppStore, Guid, ControlResponse> body)
    {
        if (ResolveWindowGate(window) is { } error) return error;
        return Resolve(target ?? "active", AllSessionIds(_store), _store.SelectedSessionId,
            "session", id => body(_store, id));
    }

    private ControlResponse ResolveWorkspace(string? target, string? window,
                                             Func<AppStore, Guid, ControlResponse> body)
    {
        if (ResolveWindowGate(window) is { } error) return error;
        return Resolve(target ?? "active", _store.Workspaces.Select(w => w.Id).ToArray(),
            _store.CurrentWorkspaceId, "workspace", id => body(_store, id));
    }

    private ControlResponse ResolveWindowId(string? target, Func<Guid, ControlResponse> body)
    {
        var t = target ?? "active";
        var resolution = ControlResolve.Resolve(t, new[] { WindowId }, WindowId);
        if (resolution.Kind == TargetResolution.Outcome.Resolved) return body(resolution.Id);
        return Err(ControlResolve.ErrorMessage("window", t, resolution));
    }

    /// <summary>Port of the upstream resolveBatchSessions: the first target fixes the store (the
    /// single one here), the rest resolve against it, deduping by resolved id; any miss propagates.</summary>
    private ControlResponse ResolveBatchSessions(IReadOnlyList<string> targets, string? window,
                                                 Func<AppStore, List<Guid>, ControlResponse> body)
    {
        if (targets.Count == 0) return Err("session command requires at least one --target");
        if (ResolveWindowGate(window) is { } error) return error;
        var candidates = AllSessionIds(_store);
        var ids = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var target in targets)
        {
            var resolution = ControlResolve.Resolve(target, candidates, _store.SelectedSessionId);
            if (resolution.Kind != TargetResolution.Outcome.Resolved)
                return Err(ControlResolve.ErrorMessage("session", target, resolution));
            if (seen.Add(resolution.Id)) ids.Add(resolution.Id);
        }
        return body(_store, ids);
    }

    /// <summary>Port of the upstream resolveAnchorLocation: resolve a --after/--before anchor over
    /// the store's whole session set and hand its (workspace, index, count) location to body.</summary>
    private ControlResponse ResolveAnchorLocation(string anchor, AppStore store,
        Func<(Guid Workspace, int Index, int Count), ControlResponse> body)
    {
        return Resolve(anchor, AllSessionIds(store), store.SelectedSessionId, "session", anchorId =>
        {
            if (store.SessionLocation(anchorId) is not { } location)
                return Err("no such session");
            return body(location);
        });
    }

    // MARK: - tree / events

    public ControlResponse ControlTree(string? window)
    {
        return ResolvePlacementStore(window, store =>
        {
            // headless extras: foreground stays null (no live process to inspect); the font fields
            // read each pane's live size (omitted when unrealized); quick, zoom, and dashboard read
            // the headless controllers.
            var tree = store.ControlTree(
                fontSize: session => LiveFontSize(session, session.AddressableSurface),
                splitFontSize: session => LiveFontSize(session, session.SplitSurface),
                scratchFontSize: session => LiveFontSize(session, session.ScratchSurface),
                quickVisible: () => Quick.Visible,
                zoomedSurface: () => Quick.IsZoomed ? "quick" : Zoom.Target?.ControlId,
                dashboardMembers: () => Dashboard.IsOpen
                    ? Dashboard.Members.Select(m => m.ControlRef).ToArray() : null,
                dashboardHighlighted: () => Dashboard.IsOpen ? Dashboard.Highlighted?.ControlRef : null,
                dashboardFontSize: () => Dashboard.IsOpen ? Dashboard.AppliedFontSize : null,
                dashboardFontMode: () => Dashboard.IsOpen ? FontModeWire(Dashboard.FontMode) : null,
                pickPending: () => Pick.PendingId,
                askPending: () => Pick.PendingAsk?.Id);
            return new ControlResponse { Ok = true, Result = new ControlResult { Tree = tree } };
        });
    }

    private static string FontModeWire(DashboardFontMode mode) => mode switch
    {
        DashboardFontMode.Auto => "auto",
        DashboardFontMode.Fixed => "fixed",
        _ => "untouched",
    };

    /// <summary>Port of WindowLibrary.readEvents: a page maps to ok, a cursor failure to ok:false
    /// with the pinned error string AND the ring's current anchor in result.events so the caller can
    /// decide whether to rebaseline.</summary>
    public ControlResponse ReadEvents(ControlEventReadOptions options)
    {
        var read = _ring.Read(options.Cursor, options.Kinds, options.Limit);
        if (read.Error is { } error)
        {
            return new ControlResponse
            {
                Ok = false,
                Error = error.Message(),
                Result = new ControlResult { Events = read.Batch },
            };
        }
        return new ControlResponse { Ok = true, Result = new ControlResult { Events = read.Batch } };
    }

    // MARK: - session.new / duplicate / select / go / close / rename / reveal

    public ControlResponse CreateSession(ControlSessionCreateOptions options)
    {
        return ResolvePlacementStore(options.Window, store =>
        {
            // anchor-relative placement: the anchor sid names its own workspace.
            if ((options.After ?? options.Before) is { } anchor)
            {
                var placeBefore = options.Before is not null;
                return ResolveAnchorLocation(anchor, store, location =>
                {
                    var index = placeBefore ? location.Index : location.Index + 1;
                    return MakeSessionResponse(store, location.Workspace, options, index);
                });
            }
            // name addressing: reuse-or-create with createWorkspace, else require an existing match.
            if (options.WorkspaceName is { } name)
            {
                if (Trimmed(name) is null)
                    return Err("workspace name must not be blank");
                // a --no-select create must not clear the workspace-focus filter (addWorkspace's
                // auto-reveal), matching the upstream background-create behavior.
                var workspace = options.CreateWorkspace == true
                    ? store.EnsureWorkspace(name, clearFocus: !options.NoSelect)
                    : store.WorkspaceNamed(name);
                if (workspace is null)
                    return Err($"no workspace named \"{name}\" (pass --create-workspace to add it)");
                return MakeSessionResponse(store, workspace.Id, options);
            }
            // id addressing (default active): the canonical prefix/active resolver.
            var target = options.Workspace ?? "active";
            return Resolve(target, store.Workspaces.Select(w => w.Id).ToArray(),
                store.CurrentWorkspaceId, "workspace",
                workspaceId => MakeSessionResponse(store, workspaceId, options));
        });
    }

    /// <summary>Port of ControlServer.makeSessionResponse (cwd defaults to the home directory).
    /// The upstream focusActiveSession keyboard-focus hop is a headless no-op.</summary>
    private ControlResponse MakeSessionResponse(AppStore store, Guid workspaceId,
                                                ControlSessionCreateOptions options, int? at = null)
    {
        var cwd = options.Cwd ?? _homeDirectory;
        var session = store.AddSession(workspaceId, cwd, command: options.Command, name: options.Name,
            wait: options.Wait ?? false, at: at, select: !options.NoSelect);
        if (session is null) return Err("could not create session");
        return OkId(session.Id);
    }

    public ControlResponse DuplicateSession(string? target, string? window)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            var session = store.DuplicateSession(id);
            if (session is null) return Err("could not duplicate session");
            return OkId(session.Id);
        });
    }

    public ControlResponse SelectSession(string? target, string? window)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            store.SelectSession(id);
            return OkId(id);
        });
    }

    public ControlResponse GoSession(string? window, SessionNavigation direction)
    {
        return ResolvePlacementStore(window, store =>
        {
            store.NavigateSession(direction);
            if (store.SelectedSessionId is not { } id)
                return Err("no session to navigate");
            return OkId(id);
        });
    }

    public ControlResponse CloseSession(string? target, string? window)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            store.CloseSession(id);
            return OkId(id);
        });
    }

    public ControlResponse CloseSessions(IReadOnlyList<string> targets, string? window)
    {
        return ResolveBatchSessions(targets, window, (store, ids) =>
        {
            if (ids.Count <= 1)
            {
                if (ids.Count == 0) return Err("session.close requires at least one --target");
                store.CloseSession(ids[0]);
                return OkId(ids[0]);
            }
            int affected;
            if (_closeGraceUndoEnabled)
            {
                // one grouped grace record, the upstream default. The headless default is OFF (see
                // PLATFORM-NOTES): the grace timer finalizes off the host's dispatch lock.
                affected = store.SoftCloseSessions(ids) ? ids.Count : 0;
            }
            else
            {
                affected = 0;
                foreach (var id in ids)
                {
                    if (store.SessionWithId(id) is null) continue;
                    store.CloseSession(id);
                    affected += 1;
                }
            }
            return new ControlResponse { Ok = true, Result = new ControlResult { Affected = affected } };
        });
    }

    public ControlResponse RenameSession(string? target, string? window, string name)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            store.RenameSession(id, name);
            return OkId(id);
        });
    }

    /// <summary>Upstream reveals the session's focused-pane cwd in Finder; there is no file-viewer
    /// hop headless, so this keeps the upstream cwd gate and selects the session instead — the tree
    /// read-back shows it active (documented adaptation).</summary>
    public ControlResponse RevealSession(string? target, string? window)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err($"no such session: {target ?? "active"}");
            if (!Directory.Exists(session.FocusedCwd))
                return Err("session cwd is not an existing directory");
            store.SelectSession(id);
            return OkId(id);
        });
    }

    // MARK: - workspace commands

    public ControlResponse CreateWorkspace(string? window, string? name, bool collapsed)
    {
        return ResolvePlacementStore(window, store =>
        {
            var workspace = store.AddWorkspace(Trimmed(name) ?? store.DefaultWorkspaceName, collapsed, holdsTarget: !collapsed);
            return OkId(workspace.Id);
        });
    }

    public ControlResponse SelectWorkspace(string? target, string? window)
    {
        return ResolveWorkspace(target, window, (store, id) =>
        {
            store.SelectWorkspace(id);
            return OkId(id);
        });
    }

    public ControlResponse RenameWorkspace(string? target, string? window, string name)
    {
        return ResolveWorkspace(target, window, (store, id) =>
        {
            store.RenameWorkspace(id, name);
            return OkId(id);
        });
    }

    /// <summary>workspace.theme (Windows-only). The name is checked against the same list
    /// theme.set uses, after the target resolves, so a missing workspace reports first.</summary>
    public ControlResponse SetWorkspaceTheme(string? target, string? window, string? themeName)
    {
        var name = Trimmed(themeName);
        return ResolveWorkspace(target, window, (store, id) =>
        {
            if (name is not null && !_availableThemes.Contains(name))
                return Err($"unknown theme: {name}");
            store.SetWorkspaceTheme(id, name);
            return OkId(id);
        });
    }

    /// <summary>workspace.icon (Windows-only). The icon is checked after the target resolves, so a
    /// missing workspace reports first.</summary>
    public ControlResponse SetWorkspaceIcon(string? target, string? window, string? iconName)
    {
        var name = Trimmed(iconName);
        return ResolveWorkspace(target, window, (store, id) =>
        {
            if (name is not null && WorkspaceIconError(name) is { } error)
                return Err(error);
            store.SetWorkspaceIcon(id, name);
            return OkId(id);
        });
    }

    private static string? WorkspaceIconError(string icon) =>
        WorkspaceIconFile.IsFile(icon) ? WorkspaceIconFile.Error(icon, File.Exists)
        : SegoeIconCatalog.TryGet(icon, out _) ? null
        : $"unknown icon: {icon}";

    public ControlResponse DeleteWorkspace(string? target, string? window)
    {
        return ResolveWorkspace(target, window, (store, id) =>
        {
            if (!store.CanRemoveWorkspace)
                return Err("cannot delete last workspace");
            store.RemoveWorkspace(id);
            return OkId(id);
        });
    }

    public ControlResponse MoveWorkspace(string? target, string? window, ReorderDirection direction)
    {
        return ResolveWorkspace(target, window, (store, id) =>
        {
            store.ReorderWorkspace(id, direction);
            return OkId(id);
        });
    }

    public ControlResponse FocusWorkspace(string? target, string? window, string? mode)
    {
        var effectiveMode = mode ?? "toggle";
        return ResolveWorkspace(target, window, (store, id) =>
        {
            Guid? want;
            switch (effectiveMode)
            {
                case "on": want = id; break;
                case "off": want = store.FocusedWorkspaceId == id ? null : store.FocusedWorkspaceId; break;
                case "toggle": want = store.FocusedWorkspaceId == id ? null : id; break;
                default: return Err($"invalid focus mode: {effectiveMode}");
            }
            store.SetFocusedWorkspace(want); // no-op + no save when unchanged (idempotent)
            return OkId(id);
        });
    }

    public ControlResponse SetWorkspaceExpansion(string? target, string? window, bool expanded)
    {
        return ResolveWorkspace(target, window, (store, id) =>
        {
            store.SetWorkspaceExpanded(id, expanded);
            return OkId(id);
        });
    }

    // MARK: - session.move

    public ControlResponse MoveSession(string? target, string? window, ControlSessionMove move)
    {
        switch (move)
        {
            case ControlSessionMove.Reorder reorder:
                return ResolveSession(target, window, (store, id) =>
                {
                    store.ReorderSession(id, reorder.Direction);
                    return OkId(id);
                });
            case ControlSessionMove.Workspace workspace:
                // the session and the destination workspace live in the same store: resolve the
                // session first (fixing the store), then the workspace within it.
                return ResolveSession(target, window, (store, sessionId) =>
                    Resolve(workspace.Name, store.Workspaces.Select(w => w.Id).ToArray(),
                        store.CurrentWorkspaceId, "workspace", workspaceId =>
                        {
                            store.MoveSession(sessionId, workspaceId);
                            return OkId(sessionId);
                        }));
            case ControlSessionMove.Place place:
                return PlaceSession(target, window, place.Anchor, place.After);
            default:
                throw new ArgumentOutOfRangeException(nameof(move));
        }
    }

    public ControlResponse MoveSessions(IReadOnlyList<string> targets, string? window, ControlSessionMove move)
    {
        switch (move)
        {
            case ControlSessionMove.Reorder:
                return Err("session.move --target can be repeated only with a workspace or --after/--before");
            case ControlSessionMove.Workspace workspace:
                return ResolveBatchSessions(targets, window, (store, ids) =>
                    Resolve(workspace.Name, store.Workspaces.Select(w => w.Id).ToArray(),
                        store.CurrentWorkspaceId, "workspace", workspaceId =>
                        {
                            var affected = store.MoveSessions(ids, workspaceId);
                            return new ControlResponse
                            {
                                Ok = true, Result = new ControlResult { Affected = affected },
                            };
                        }));
            case ControlSessionMove.Place place:
                return PlaceSessions(targets, window, place.Anchor, place.After);
            default:
                throw new ArgumentOutOfRangeException(nameof(move));
        }
    }

    private ControlResponse PlaceSession(string? target, string? window, string anchor, bool after)
    {
        return ResolveSession(target, window, (store, sessionId) =>
        {
            if (store.SessionLocation(sessionId) is not { } source)
                return Err("no such session");
            return ResolveAnchorLocation(anchor, store, anchorLoc =>
            {
                // a null resolution (anchor==self / already in place) is a successful no-op.
                if (ResolveRelativeDrop((source.Workspace, source.Index), anchorLoc, after) is { } resolution)
                    store.MoveSession(sessionId, resolution.Workspace, resolution.Destination);
                return OkId(sessionId);
            });
        });
    }

    private ControlResponse PlaceSessions(IReadOnlyList<string> targets, string? window,
                                          string anchor, bool after)
    {
        return ResolveBatchSessions(targets, window, (store, ids) =>
        {
            var sources = new List<(Guid Workspace, int Index)>();
            foreach (var id in ids)
            {
                if (store.SessionLocation(id) is not { } location)
                    return Err("no such session");
                sources.Add((location.Workspace, location.Index));
            }
            return ResolveAnchorLocation(anchor, store, anchorLoc =>
            {
                var affected = ResolveSessionsDrop(sources, anchorLoc, after) is { } resolution
                    ? store.MoveSessions(ids, resolution.Workspace, resolution.Destination)
                    : 0;
                return new ControlResponse { Ok = true, Result = new ControlResult { Affected = affected } };
            });
        });
    }

    /// <summary>Port of SidebarDrop.resolveRelative → resolveSession for a session-row target: the
    /// post-removal off-by-one, cross-workspace index space, and anchor==source no-op verbatim.</summary>
    private static (Guid Workspace, int Destination)? ResolveRelativeDrop(
        (Guid Workspace, int Index) source, (Guid Workspace, int Index, int Count) anchor, bool placeAfter)
    {
        var childIndex = placeAfter ? OnItemIndex : anchor.Index;
        var dropChildIndex = childIndex == OnItemIndex ? anchor.Index + 1 : childIndex;
        var sameWorkspace = source.Workspace == anchor.Workspace;
        var destination = dropChildIndex;
        if (sameWorkspace && source.Index < dropChildIndex) destination = dropChildIndex - 1;
        if (sameWorkspace)
        {
            var landed = Math.Max(0, Math.Min(destination, anchor.Count - 1));
            if (landed == source.Index) return null;
        }
        return (anchor.Workspace, destination);
    }

    /// <summary>Port of SidebarDrop.resolveSessions for a session-row target (the batch place).</summary>
    private static (Guid Workspace, int Destination)? ResolveSessionsDrop(
        IReadOnlyList<(Guid Workspace, int Index)> sources,
        (Guid Workspace, int Index, int Count) anchor, bool placeAfter)
    {
        if (sources.Count == 0) return null;
        var childIndex = placeAfter ? OnItemIndex : anchor.Index;
        var dropChildIndex = childIndex == OnItemIndex ? anchor.Index + 1 : childIndex;
        var rawDestination = dropChildIndex;
        var sourceIndicesInTarget = sources.Where(s => s.Workspace == anchor.Workspace)
            .Select(s => s.Index).OrderBy(i => i).ToList();
        var removedBeforeDestination = sourceIndicesInTarget.Count(i => i < rawDestination);
        var destination = rawDestination - removedBeforeDestination;
        if (sourceIndicesInTarget.Count > 0 && sourceIndicesInTarget.Count == sources.Count)
        {
            var first = sourceIndicesInTarget[0];
            var contiguous = !sourceIndicesInTarget.Where((value, offset) => value != first + offset).Any();
            if (contiguous && destination == first) return null;
        }
        return (anchor.Workspace, destination);
    }

    // MARK: - session.flag / seen / status / restore

    public ControlResponse SetSessionFlag(string? target, string? window, string? mode)
    {
        var effectiveMode = mode ?? "toggle";
        if (effectiveMode == "clear")
        {
            return ResolvePlacementStore(window, store =>
            {
                store.ClearFlags();
                return Ok();
            });
        }
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err($"no such session: {target ?? "active"}");
            bool want;
            switch (effectiveMode)
            {
                case "on": want = true; break;
                case "off": want = false; break;
                case "toggle": want = !session.Flagged; break;
                default: return Err($"invalid flag mode: {effectiveMode}");
            }
            store.SetFlag(want, id); // no-op + no save when unchanged (idempotent)
            return OkId(id);
        });
    }

    public ControlResponse MarkSessionSeen(string? target, string? window)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            store.ClearUnseen(id);
            return OkId(id);
        });
    }

    /// <summary>Validate explicit sounds before resolving targets or changing the indicator.</summary>
    public ControlResponse SetSessionStatus(string? target, string? window, ControlSessionStatusUpdate update)
    {
        if (!string.IsNullOrEmpty(update.Sound) && _sounds.Resolve(update.Sound) is null)
            return Err(_sounds.UnknownSoundError(update.Sound));
        return ResolveSession(target, window, (store, id) =>
        {
            var session = store.SessionWithId(id);
            var wasBlocked = session?.AgentIndicator.Status == AgentStatus.Blocked;
            // a --pane-id that resolves against the session's LIVE surfaces overrides the stale role
            // --pane; an absent/unknown token falls back to the baked --pane value.
            var resolvedPane = (update.PaneId is { } token ? session?.PaneRole(token) : null) ?? update.Pane;
            store.SetAgentIndicator(new AgentIndicator(update.Status, update.Blink ?? false,
                update.AutoReset ?? false, update.Color, update.Shape, resolvedPane), id);
            if (!string.IsNullOrEmpty(update.Sound) || (update.Status == AgentStatus.Blocked && !wasBlocked))
            {
                _statusNotificationRequested?.Invoke(
                    new LiveStatusNotificationRequest(id, update.Status, update.Sound));
            }
            return OkId(id);
        });
    }

    public ControlResponse SetSessionRestore(string? target, string? window, ControlSessionRestoreUpdate update)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err($"no such session: {target ?? "active"}");
            StatusPane pane;
            // an EMPTY token counts as absent (an older shell exporting no AGTERM_PANE_ID); a
            // non-empty unresolvable token WITHOUT an explicit --pane is an error here (unlike
            // session.status): a bad fallback would overwrite the MAIN pane's persisted command.
            if (update.PaneId is { Length: > 0 } token)
            {
                if ((session.PaneRole(token) ?? update.Pane) is not { } resolved)
                    return Err($"unknown pane id: {token}");
                pane = resolved;
            }
            else
            {
                pane = update.Pane ?? StatusPane.Left;
            }
            if (pane == StatusPane.Scratch)
                return Err("the scratch terminal is never restored");
            if (pane == StatusPane.Right && !session.HasSplit)
                return Err("session has no split");
            string? value = update.Pin switch
            {
                ControlRestoreOverride.Pin pin => pin.Command,
                ControlRestoreOverride.PinNone => "",
                _ => null,
            };
            // the ack must not outrun the disk: a failed save is rolled back store-side and refused.
            if (!store.SetRestoreCommand(value, pane, id))
                return Err("failed to save the restore override, the previous value is still in effect");
            var result = new ControlResult { Id = Uuid(id) };
            if (update.Pin is ControlRestoreOverride.Pin && !_restoreRunningCommand)
            {
                result = result with
                {
                    Text = "saved, but \"Restore running commands on restart\" is off, so the override will not run",
                };
            }
            return new ControlResponse { Ok = true, Result = result };
        });
    }

    // MARK: - split / scratch / focus / resize / zoom

    public ControlResponse SplitSession(string? target, string? window, string? mode)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err($"no such session: {target ?? "active"}");
            if (ControlToggleModes.Parse(mode) is not { } parsedMode)
                return Err($"invalid split mode: {mode ?? "toggle"}");
            var want = parsedMode.DesiredValue(session.IsSplit);
            if (want != session.IsSplit)
                store.ToggleSplit(id); // keep-alive hide/show; never destroys the hidden pane
            // upstream then moves keyboard focus (AppActions.focusSplitPane) — a headless no-op;
            // Session.SplitFocused is already maintained by ToggleSplit.
            return OkId(id);
        });
    }

    public ControlResponse ScratchSession(string? target, string? window, string? mode, string? command)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err($"no such session: {target ?? "active"}");
            if (ControlToggleModes.Parse(mode) is not { } parsedMode)
                return Err($"invalid scratch mode: {mode ?? "toggle"}");
            var want = parsedMode.DesiredValue(session.ScratchActive);
            if (want && command is { Length: > 0 })
            {
                // a scratch is expendable: respawn an already-alive one so the command is never
                // silently ignored (run-once, consumed by the surface factory upstream).
                if (session.ScratchSurface is not null) store.CloseScratch(id);
                session.ScratchCommand = command;
            }
            if (want && store.SelectedSessionId != id)
            {
                // the scratch is a full-coverage focus-grabbing surface: select the target first.
                store.SelectSession(id);
            }
            if (want != session.ScratchActive)
                store.ToggleScratch(id); // keep-alive hide/show; never tears the shell down
            return OkId(id);
        });
    }

    public ControlResponse FocusSessionPane(string? target, string? window, string? pane)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err($"no such session: {target ?? "active"}");
            if (!session.HasSplit)
                return Err("session has no split");
            if (ControlPaneFocusModes.Parse(pane) is not { } parsedPane)
                return Err($"invalid pane: {pane ?? "other"}");
            // upstream AppActions.setSplitFocus updates the model and asks the live adapter to move
            // keyboard focus. Headless hosts have no subscriber for the pane-token request.
            var splitFocused = parsedPane.WantsSplit(session.SplitFocused);
            if (session.SplitFocused != splitFocused)
            {
                session.SplitFocused = splitFocused;
                store.NotifySidebar();
            }
            var paneId = splitFocused
                ? session.SplitSurface?.PaneToken
                : session.Surface?.PaneToken;
            if (!string.IsNullOrEmpty(paneId))
            {
                _paneFocusRequested?.Invoke(paneId);
            }
            return OkId(id);
        });
    }

    public ControlResponse ResizeSplit(string? target, string? window, ControlSplitResize resize)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err($"no such session: {target ?? "active"}");
            if (!session.HasSplit)
                return Err("session has no split");
            var requested = resize switch
            {
                ControlSplitResize.Ratio ratio => ratio.Value,
                ControlSplitResize.Delta delta => (session.SplitRatio ?? AppStore.SplitRatioDefault) + delta.Value,
                _ => throw new ArgumentOutOfRangeException(nameof(resize)),
            };
            if (store.ApplySplitRatio(requested, id) is not { } applied)
                return Err($"no such session: {target ?? "active"}");
            // upstream also pokes the live divider view; headless keeps only the stored fraction.
            return new ControlResponse
            {
                Ok = true, Result = new ControlResult { Id = Uuid(id), Ratio = applied },
            };
        });
    }

    public ControlResponse SetSurfaceZoom(string? target, string? window, ControlToggleMode mode)
    {
        var rawTarget = Trimmed(target) ?? "active";
        if (rawTarget == "active")
            return SetActiveSurfaceZoom(window, mode);
        if (rawTarget == "quick")
        {
            if (!Quick.SetZoom(mode))
                return Err("surface not available: quick");
            _quickChanged?.Invoke();
            return new ControlResponse { Ok = true, Result = new ControlResult { Id = "quick" } };
        }
        if (TerminalSurfaceId.FromRawValue(rawTarget) is not { } surfaceId)
            return Err($"invalid surface: {rawTarget}");
        if (ResolveWindowGate(window) is { } error) return error;
        if (_store.SessionWithId(surfaceId.SessionId) is null)
            return Err($"no such surface: {surfaceId.RawValue}");
        var zoomTarget = new TerminalZoomTarget.Session(surfaceId.SessionId, surfaceId.Surface);
        var want = mode.DesiredValue(Zoom.Target == zoomTarget);
        if (want && Zoom.Target != zoomTarget && Pick.PendingModalError is { } pickError) return Err(pickError);
        // hide is idempotent: skip the availability check for off — the desired end state holds.
        if (mode != ControlToggleMode.Off
            && !IsZoomTargetAvailable(zoomTarget))
            return Err($"surface not available: {surfaceId.RawValue}");
        ApplyZoom(mode, zoomTarget);
        return new ControlResponse { Ok = true, Result = new ControlResult { Id = surfaceId.RawValue } };
    }

    private ControlResponse SetActiveSurfaceZoom(string? window, ControlToggleMode mode)
    {
        if (ResolveWindowGate(window) is { } error) return error;
        if (Zoom.Target is null && mode != ControlToggleMode.Off && Pick.PendingModalError is { } pickError)
            return Err(pickError);
        TerminalZoomTarget effectiveTarget;
        if (Zoom.Target is { } current)
        {
            effectiveTarget = current;
        }
        else
        {
            if (mode == ControlToggleMode.Off) return Ok(); // already un-zoomed: bare ok
            if (TerminalZoomController.ResolveTarget(_store, Quick.Visible) is not { } zoomTarget)
                return Err("no active surface");
            if (!IsZoomTargetAvailable(zoomTarget))
                return Err($"surface not available: {zoomTarget.ControlId}");
            effectiveTarget = zoomTarget;
        }
        ApplyZoom(mode, effectiveTarget);
        return new ControlResponse { Ok = true, Result = new ControlResult { Id = effectiveTarget.ControlId } };
    }

    private void ApplyZoom(ControlToggleMode mode, TerminalZoomTarget target)
    {
        var previousTarget = Zoom.Target;
        var wasActive = Zoom.Target is not null;
        Zoom.Set(mode, target);
        if (Zoom.Target != previousTarget)
        {
            if (Zoom.Target is not null && _store.ActiveSession is { } activeSession)
                CloseSessionSearch(_store, activeSession);
            if (Zoom.Target is TerminalZoomTarget.Session zoomed
                && zoomed.SessionId != _store.SelectedSessionId
                && _store.SessionWithId(zoomed.SessionId) is { } session)
            {
                CloseSessionSearch(_store, session);
            }
            _zoomChanged?.Invoke();
        }
        PublishZoomActiveChange(wasActive);
    }

    private void ClearZoom()
    {
        var previousTarget = Zoom.Target;
        var wasActive = Zoom.Target is not null;
        Zoom.Clear();
        if (Zoom.Target != previousTarget)
            _zoomChanged?.Invoke();
        PublishZoomActiveChange(wasActive);
    }

    private void PublishZoomActiveChange(bool wasActive)
    {
        var isActive = Zoom.Target is not null;
        if (wasActive != isActive)
            _zoomActiveChanged?.Invoke(isActive);
    }

    public void ReconcileSurfaceZoom()
    {
        if (Zoom.Target is { } target
            && !IsZoomTargetAvailable(target))
        {
            ClearZoom();
        }
    }

    private bool IsZoomTargetAvailable(TerminalZoomTarget target) =>
        TerminalZoomController.IsTargetValid(target, _store, Quick.Visible)
        && (_zoomTargetAvailable?.Invoke(target) ?? true);

    public void SetDashboardBaseFontSize(double fontSize)
    {
        if (!double.IsFinite(fontSize) || fontSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        _dashboardBaseFontSize = fontSize;
        RecomputeDashboardFontSize();
    }

    public void RecomputeDashboardFontSize()
    {
        if (Dashboard.IsOpen)
        {
            Dashboard.AppliedFontSize = AppliedDashboardFontSize(
                Dashboard.FontMode,
                Dashboard.Members.Count,
                _dashboardBaseFontSize);
        }
    }

    // MARK: - dashboard

    /// <summary>Port of ControlServer.setDashboard onto the headless dashboard state. The 9-cell cap
    /// counts PANES (after split expansion); unresolved ids and dropped panes are reported in
    /// result.text, never silently.</summary>
    public ControlResponse SetDashboard(IReadOnlyList<string> targets, string? window, bool close,
                                        DashboardFontMode fontMode, bool mru)
    {
        return ResolvePlacementStore(window, store =>
        {
            if (close)
            {
                var wasOpen = Dashboard.IsOpen;
                Dashboard.Close();
                if (wasOpen)
                    _dashboardChanged?.Invoke();
                return Ok();
            }
            if (Pick.PendingModalError is { } pickError) return Err(pickError);
            var sessionIds = new List<Guid>();
            var unresolved = new List<string>();
            if (mru)
            {
                sessionIds = store.RecentSessions(DashboardMaxCells);
                if (sessionIds.Count == 0)
                    return Err("no recent sessions");
            }
            else
            {
                var candidates = AllSessionIds(store);
                var seen = new HashSet<Guid>();
                foreach (var target in targets)
                {
                    var resolution = ControlResolve.Resolve(target, candidates, store.SelectedSessionId);
                    if (resolution.Kind != TargetResolution.Outcome.Resolved
                        || store.SessionWithId(resolution.Id) is null)
                    {
                        unresolved.Add(target);
                        continue;
                    }
                    if (seen.Add(resolution.Id)) sessionIds.Add(resolution.Id);
                }
                if (sessionIds.Count == 0)
                    return Err("no dashboard sessions resolved");
            }
            var (members, droppedPanes) = store.DashboardMembers(sessionIds, DashboardMaxCells);
            // zoom and dashboard are mutually exclusive: drop any active zoom on open.
            ClearZoom();
            Dashboard.Open(members, fontMode);
            // set the applied size synchronously so the dashboardFontSize tree read-back is
            // authoritative at command return (the upstream setAppliedFontSize call).
            Dashboard.AppliedFontSize = AppliedDashboardFontSize(fontMode, members.Count, _dashboardBaseFontSize);
            _dashboardChanged?.Invoke();
            var notes = new List<string>();
            if (unresolved.Count > 0) notes.Add($"unresolved: {string.Join(", ", unresolved)}");
            if (droppedPanes > 0)
                notes.Add($"dropped {droppedPanes} pane(s) beyond the {DashboardMaxCells}-cell limit");
            if (notes.Count == 0) return Ok();
            return new ControlResponse
            {
                Ok = true, Result = new ControlResult { Text = string.Join("; ", notes) },
            };
        });
    }

    /// <summary>Port of DashboardFontMode.appliedFontSize + DashboardLayout's grid/auto-size math
    /// (agtermCore has them app-free; the C# core does not carry them yet, so the headless host owns
    /// this small copy).</summary>
    internal static double? AppliedDashboardFontSize(DashboardFontMode mode, int memberCount, double baseSize)
    {
        switch (mode)
        {
            case DashboardFontMode.Untouched:
                return null;
            case DashboardFontMode.Fixed fixedMode:
                return fixedMode.Value;
            case DashboardFontMode.Auto:
                var n = Math.Min(Math.Max(memberCount, 1), DashboardMaxCells);
                var cols = (int)Math.Ceiling(Math.Sqrt(n));
                var rows = (int)Math.Ceiling(n / (double)cols);
                var factor = (cols, rows) switch
                {
                    (1, 1) => 1.00,
                    (2, 1) => 0.85,
                    (2, 2) => 0.75,
                    (3, 2) => 0.62,
                    (3, 3) => 0.55,
                    _ => 0.55,
                };
                return Math.Max(DashboardMinFontSize,
                    Math.Round(baseSize * factor, MidpointRounding.AwayFromZero));
            default:
                return null;
        }
    }

    // MARK: - surface I/O (font / type / copy / paste / selectall / search / text)

    public ControlResponse Font(string? target, string? window, string? pane, string action)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err("session not realized");
            ITerminalSurface? chosen;
            switch (pane)
            {
                case null or "left":
                    chosen = session.AddressableSurface;
                    break;
                case "right":
                    if (session.SplitSurface is not { } split)
                        return Err("session has no split pane");
                    chosen = split;
                    break;
                case "scratch":
                    if (session.ScratchSurface is not { } scratch)
                        return Err("session has no scratch terminal");
                    chosen = scratch;
                    break;
                default:
                    return Err($"invalid pane: {pane}");
            }
            var member = DashboardMemberFor(id, session, chosen);
            // upstream: a dashboard member carries dashboardFontOverride, and a font change on it
            // resizes the tile without reporting the size to the session.
            if (member is { } tile && Dashboard.AdjustCellFontSize(tile, action))
            {
                _dashboardChanged?.Invoke();
                return OkId(id);
            }
            if (chosen is not IHeadlessSurface surface)
                return Err("session not realized");
            if (!surface.PerformBindingAction(action))
                return Err("session not realized");
            return OkId(id);
        });
    }

    /// <summary>The dashboard cell a pane surface would occupy: the main slot is Primary, the split
    /// slot is Split, anything else (scratch, overlay, none) has no cell.</summary>
    private static DashboardMember? DashboardMemberFor(Guid id, Session session, ITerminalSurface? surface) =>
        surface is null ? null
        : ReferenceEquals(surface, session.Surface) ? new DashboardMember(id, TerminalZoomSurface.Primary)
        : ReferenceEquals(surface, session.SplitSurface) ? new DashboardMember(id, TerminalZoomSurface.Split)
        : null;

    /// <summary>The size a pane renders at, as the tree reports it (upstream currentFontSize reads
    /// ghostty's font_size): a tile on a sized dashboard shows its chord size or the applied size,
    /// otherwise the pane's own size. Null when the pane is missing or not realized.</summary>
    private double? LiveFontSize(Session session, ITerminalSurface? surface)
    {
        if (surface is not IHeadlessSurface pane || !pane.IsRealized)
            return null;
        if (Dashboard.IsOpen && Dashboard.AppliedFontSize is { } applied &&
            DashboardMemberFor(session.Id, session, surface) is { } member &&
            Dashboard.Members.Contains(member))
            return Dashboard.CellFontSizes.GetValueOrDefault(member, applied);
        return pane.FontSize;
    }

    /// <summary>Async in the interface because the upstream arm bounded-polls for surface
    /// realization; headless surfaces attach externally and never realize asynchronously, so this
    /// checks once (after the select, when asked for) and completes synchronously.</summary>
    public Task<ControlResponse> TypeSession(string? target, string? window, ControlSessionTypeOptions options)
    {
        var response = ResolveSession(target, window, (store, id) =>
            InjectText(options.Text, id, store, options.Select, options.Pane));
        return Task.FromResult(response);
    }

    private ControlResponse InjectText(string text, Guid id, AppStore store, bool select, string? pane)
    {
        var session = store.SessionWithId(id);
        switch (pane)
        {
            case null or "left":
                break;
            case "right":
            {
                if (session?.SplitSurface is not { } split)
                    return Err("session has no split pane");
                if (split is not IHeadlessSurface surface || !surface.Inject(text))
                    return Err("session not realized");
                return OkId(id);
            }
            case "scratch":
            {
                if (session?.ScratchSurface is not { } scratch)
                    return Err("session has no scratch terminal");
                if (scratch is not IHeadlessSurface surface || !surface.Inject(text))
                    return Err("session not realized");
                return OkId(id);
            }
            default:
                return Err($"invalid pane: {pane}");
        }
        // main pane: inject if realized; otherwise select when asked and check once more. Upstream polls
        // ~360 ms here; the headless dispatch holds the host gate, so a sleep would see no progress.
        if (session?.Surface is IHeadlessSurface main && main.Inject(text))
            return OkId(id);
        if (select)
            store.SelectSession(id);
        if (store.SessionWithId(id)?.Surface is IHeadlessSurface selected && selected.Inject(text))
            return OkId(id);
        return Err("session not realized");
    }

    public ControlResponse CopySessionSelection(string? target, string? window)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            // addressableSurface, not Surface: it must resolve the SAME pane session.selectall acted
            // on, including a promoted split survivor.
            if (store.SessionWithId(id)?.AddressableSurface is not IHeadlessSurface surface || !surface.IsRealized)
                return Err("session not realized");
            var text = surface.SelectedText;
            if (string.IsNullOrEmpty(text))
                return Err("no selection");
            return new ControlResponse { Ok = true, Result = new ControlResult { Text = text } };
        });
    }

    public ControlResponse PasteSession(string? target, string? window) =>
        SurfaceBindingAction(target, window, surface => surface.Paste());

    public ControlResponse SelectAllSession(string? target, string? window) =>
        SurfaceBindingAction(target, window, surface =>
        {
            if (!surface.IsRealized) return false;
            surface.SelectAll();
            return true;
        });

    /// <summary>Port of the upstream surfaceBindingAction: run the action on the session's
    /// addressable surface. A never-shown session (no attached surface) and an action that
    /// reports false both answer "session not realized".</summary>
    private ControlResponse SurfaceBindingAction(string? target, string? window, Func<IHeadlessSurface, bool> action)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id)?.AddressableSurface is not IHeadlessSurface surface || !action(surface))
                return Err("session not realized");
            return OkId(id);
        });
    }

    /// <summary>session.search. The headless adaptation of the async libghostty search: the match
    /// count is computed synchronously by counting needle occurrences in the search surface's full
    /// buffer (no SEARCH_TOTAL callback to await), and next/prev step Session.SearchSelected with
    /// wraparound. Open/pin/close semantics and every error string mirror the upstream arm.</summary>
    public Task<ControlResponse> SearchSession(string? target, string? window, string? text, string? to)
    {
        // resolve first, matching the upstream error ORDER: target lookup wins over `to` validation.
        var response = ResolveSession(target, window, (store, id) => SearchSessionResolved(store, id, text, to));
        return Task.FromResult(response);
    }

    private void CloseSessionSearch(AppStore store, Session session)
    {
        if (session.SearchSurface is not IHeadlessSurface searchOwner)
            return;
        var restorePaneId = store.SelectedSessionId == session.Id
            ? session.TopmostSurface?.PaneToken
            : null;
        session.ClearSearch();
        store.NotifySidebar();
        _searchUpdated?.Invoke(new HeadlessSearchUpdate(
            session.Id,
            searchOwner,
            Active: false,
            Needle: "",
            DisplayText: "",
            Selected: null,
            FocusInput: false,
            RestorePaneId: restorePaneId));
    }

    private ControlResponse SearchSessionResolved(AppStore store, Guid id, string? text, string? to)
    {
        if (to is not null && to is not ("next" or "prev" or "close"))
            return Err("session.search --to must be next|prev|close");
        if (store.SessionWithId(id) is not { } session)
            return Err("no such session");

        // close exits search without selecting; with no open search there is no owner — clean no-op.
        if (to == "close")
        {
            CloseSessionSearch(store, session);
            return OkId(id);
        }
        if (Pick.PendingModalError is { } pickError) return Err(pickError);
        if (Zoom.Target is not null)
            return Err("terminal zoom active");

        // open/needle/navigate select the target; a covering scratch wins (OnScreenSurface), like
        // upstream. No realize poll: an unattached surface errors at once.
        store.SelectSession(id);
        if (session.OnScreenSurface is not IHeadlessSurface openSurface)
            return Err("session not realized");
        var opened = !session.SearchActive;
        if (opened)
        {
            // startSearch: upstream's async START callback pins the owner; headless pins directly.
            session.SearchActive = true;
            session.SearchSurface = openSurface;
        }
        var surface = session.SearchSurface as IHeadlessSurface ?? openSurface;
        if (text is not null)
        {
            session.SearchNeedle = text;
            if (text.Length == 0)
            {
                // an explicitly-empty needle clears the query; nothing to count.
                session.SearchTotal = null;
                session.SearchSelected = null;
                store.NotifySidebar();
                _searchUpdated?.Invoke(new HeadlessSearchUpdate(
                    id,
                    surface,
                    Active: true,
                    Needle: "",
                    DisplayText: "",
                    Selected: null,
                    FocusInput: opened,
                    RestorePaneId: null));
                return OkId(id);
            }
            var buffer = surface.ReadScreenText(all: true, lines: null) ?? "";
            session.SearchTotal = CountOccurrences(buffer, text);
            session.SearchSelected = null;
        }
        if (session.SearchTotal is { } total && total > 0)
        {
            switch (to)
            {
                case "next":
                    session.SearchSelected = session.SearchSelected is { } selected ? selected % total + 1 : 1;
                    break;
                case "prev":
                    session.SearchSelected = session.SearchSelected is { } current
                        ? (current <= 1 ? total : current - 1)
                        : total;
                    break;
            }
        }
        var display = session.SearchDisplayText;
        store.NotifySidebar();
        _searchUpdated?.Invoke(new HeadlessSearchUpdate(
            id,
            surface,
            Active: true,
            Needle: session.SearchNeedle,
            DisplayText: display,
            Selected: session.SearchSelected,
            FocusInput: opened,
            RestorePaneId: null));
        return new ControlResponse
        {
            Ok = true,
            Result = new ControlResult
            {
                Text = display.Length == 0 ? null : display,
                Count = session.SearchTotal,
            },
        };
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (needle.Length == 0) return 0;
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count += 1;
            index += needle.Length;
        }
        return count;
    }

    public ControlResponse ReadSessionText(string? target, string? window, ControlSessionTextOptions options)
    {
        // validated here too, not only in the dispatcher/CLI, so a raw client can't bypass it.
        if (options.All && options.Lines is not null)
            return Err("use either --all or --lines, not both");
        if (options.Lines is <= 0)
            return Err("--lines must be greater than 0");
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err("session not realized");
            ITerminalSurface? chosen;
            switch (options.Pane)
            {
                case null:
                    // omitted = the surface ON SCREEN (scratch-aware), same as session.search.
                    chosen = session.OnScreenSurface;
                    break;
                case "left":
                    chosen = session.Surface;
                    break;
                case "right":
                    if (session.SplitSurface is not { } split)
                        return Err("session has no split pane");
                    chosen = split;
                    break;
                case "scratch":
                    // kept alive while hidden, so readable whether or not it is on screen.
                    if (session.ScratchSurface is not { } scratch)
                        return Err("session has no scratch terminal");
                    chosen = scratch;
                    break;
                default:
                    return Err($"invalid pane: {options.Pane}");
            }
            if (chosen is not IHeadlessSurface surface)
                return Err("session not realized");
            if (surface.ReadScreenText(options.All, options.Lines) is not { } text)
                return Err("failed to read surface buffer");
            return new ControlResponse { Ok = true, Result = new ControlResult { Text = text } };
        });
    }

    // MARK: - overlay

    public ControlResponse OpenSessionOverlay(string? target, string? window,
                                              ControlSessionOverlayOpenOptions options)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (!store.OpenOverlay(id, options.Command, options.Cwd, options.Wait,
                    options.SizePercent, options.BackgroundColor))
                return Err("overlay already open");
            // the overlay runs without an implicit select; --follow is the user-facing jump.
            if (options.Follow) store.SelectSession(id);
            return OkId(id);
        });
    }

    public ControlResponse CloseSessionOverlay(string? target, string? window)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            // the courtesy path for the whole slot, so it takes a HUD down too
            if (store.CloseHud(id) || store.CloseOverlay(id))
                return OkId(id);
            return Err("no overlay");
        });
    }

    public ControlResponse ResizeSessionOverlay(string? target, string? window, int? sizePercent)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is { HudActive: true })
            {
                // a HUD must never cover the session it is a message about
                if (sizePercent is not { } percent) return Err(HudErrors.FullResize);
                store.ResizeHud(id, percent);
                return OkId(id);
            }
            if (!store.ResizeOverlay(id, sizePercent))
                return Err("no overlay");
            return OkId(id);
        });
    }

    public ControlResponse SessionOverlayResult(string? target, string? window)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err("no such session");
            if (session.OverlayActive)
                return Err(OverlayResultError.StillRunning);
            // the slot holds a message, not a program that could report a status
            if (session.HudActive)
                return Err(HudErrors.NoOverlayResult);
            if (session.OverlayExitCode is not { } code)
                return Err(OverlayResultError.NoResult);
            return new ControlResponse
            {
                Ok = true, Result = new ControlResult { Id = Uuid(id), ExitCode = code },
            };
        });
    }

    // MARK: - hud

    public ControlResponse OpenHud(string? target, string? window, HudSpec spec, ControlHudPlacement placement) =>
        ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session) return Err("no such session");
            if (!PanePlacement.TryResolve(placement.Pane, placement.PaneId, session, requireVisible: true,
                    HudErrors.InvalidPane, out var resolved, out var error))
                return Err(error!);
            return store.OpenHud(id, spec, resolved.Identity) ? OkId(id) : Err("overlay already open");
        });

    /// <summary>Opens a failed custom command's panel over its session. An explicit pane the HUD
    /// would refuse falls back to the whole session, since the message matters more than its place.
    /// Runs under the host gate.</summary>
    public CommandFailureHudResult OpenCommandFailureHud(Guid sessionId, HudSpec spec, OverlayPane? pane)
    {
        if (_store.SessionWithId(sessionId) is not { } session)
            return new CommandFailureHudResult(Err("no such session"), null);
        // a rejection leaves `resolved` session-wide, which is the fallback
        PanePlacement.TryResolve(pane, null, session, requireVisible: true,
            HudErrors.InvalidPane, out var resolved, out var rejection);
        var response = _store.OpenHud(sessionId, spec, resolved.Identity) ? OkId(sessionId) : Err("overlay already open");
        return new CommandFailureHudResult(response, rejection);
    }

    public ControlResponse UpdateHud(string? target, string? window, HudSpec spec, ControlHudPlacement placement) =>
        ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session || !session.HudActive) return Err(HudErrors.NoHud);
            // a hidden pane keeps its HUD, so an update may still address it
            if (!PanePlacement.TryResolve(placement.Pane, placement.PaneId, session, requireVisible: false,
                    HudErrors.InvalidPane, out var resolved, out var error))
                return Err(error!);
            store.UpdateHud(id, spec, resolved.Identity);
            return OkId(id);
        });

    public ControlResponse CloseHud(string? target, string? window) =>
        ResolveSession(target, window, (store, id) => store.CloseHud(id) ? OkId(id) : Err(HudErrors.NoHud));

    // MARK: - background

    public ControlResponse SetSessionBackground(string? target, string? window,
                                                ControlSessionBackgroundOptions options)
    {
        var watermark = options.Watermark;
        if (watermark is { Kind: WatermarkKind.Image })
        {
            if (watermark.ImagePath is not { Length: > 0 } path)
                return Err("session.background image requires a path");
            // upstream WatermarkRenderer.isSupportedImage: extension in png|jpg|jpeg.
            var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (extension is not ("png" or "jpg" or "jpeg"))
                return Err($"unsupported image (PNG or JPEG only): {path}");
            if (!File.Exists(path))
                return Err($"no such image file: {path}");
        }
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is null)
                return Err("no such session");
            // gate on a real change like upstream; an unchanged spec is an idempotent ok.
            if (!store.SetBackgroundWatermark(watermark, id))
                return OkId(id);
            // clearing drops any rendered .text PNG so the state dir doesn't accumulate.
            if (watermark is null) WatermarkStorage.RemoveRenderedText(id, _store.StateDirectory);
            // upstream then applies the spec to the realized surfaces — no renderer headless.
            return OkId(id);
        });
    }

    // MARK: - app commands (keymap / config / notify / theme / sidebar / quick / restore.clear)

    /// <summary>The parsed keymap after a keymap.reload (fired under the dispatch gate) — the
    /// live window re-arms its hotkey router from it (headless has no key handling).</summary>
    public event Action<Keymap>? KeymapReloaded;
    public event Action<HeadlessThemeUpdate>? ThemeUpdateRequested;

    /// <summary>The keymap.conf the actions load; ApplySettings can move it to a new config directory.</summary>
    public string KeymapPath => Volatile.Read(ref _keymap).Path;

    /// <summary>Called under the host gate. Restart-only and deferred settings are not applied here.</summary>
    public void ApplySettings(SettingsSnapshot snapshot, string defaultConfigDirectory)
    {
        var settings = snapshot.Settings;
        Theme.Light = settings.Theme;
        Theme.Dark = settings.FollowSystemAppearance == true ? settings.DarkTheme : null;
        var keymap = new KeymapStore(settings.ConfigDirectory ?? defaultConfigDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetFullPath(_keymap.Path), Path.GetFullPath(keymap.Path), comparison))
        {
            _keymap = keymap;
            ReloadKeymap();
        }
    }

    private sealed record LoadedKeymap(Keymap Keymap, IReadOnlyList<KeymapDiagnostic> Diagnostics);

    // Written by the window thread at bootstrap and by the dispatch gate on reload, read under
    // the gate: one immutable snapshot keeps the keymap and its diagnostics from tearing apart.
    private volatile LoadedKeymap _loadedKeymap = new(Keymap.Empty, []);

    /// <summary>Starts a custom command in a session and returns null, or the reason it did not
    /// start. Runs inside the dispatch gate: it must not wait on UI or store-coordinator work,
    /// which would deadlock.</summary>
    public Func<AppStore, Guid, CustomCommand, string?>? CustomCommandRunner { get; set; }

    /// <summary>The current keymap for the window's hotkey bootstrap (same file the reload arm
    /// reads; diagnostics are the arm's concern).</summary>
    internal IReadOnlyList<KeymapDiagnostic> KeymapDiagnostics => _loadedKeymap.Diagnostics;

    public Keymap LoadKeymap() => Load().Keymap;

    private LoadedKeymap Load()
    {
        var (keymap, diagnostics) = _keymap.Load();
        return _loadedKeymap = new LoadedKeymap(keymap, diagnostics.AsReadOnly());
    }

    public ControlResponse ListKeymap()
    {
        var loaded = _loadedKeymap;
        return new ControlResponse
        {
            Ok = true,
            Result = new ControlResult
            {
                Keymap = ControlKeymapProjection.Project(loaded.Keymap, loaded.Diagnostics, _keymap.Path),
            },
        };
    }

    public Task<ControlResponse> RunCustomCommand(string name, string? target, string? window)
    {
        if (_loadedKeymap.Keymap.Commands.FirstOrDefault(c => c.Name == name) is not { } command)
            return Task.FromResult(Err($"no custom command named {name}"));
        if (CustomCommandRunner is not { } runner)
            return Task.FromResult(Err("custom commands are not available"));

        var response = ResolveSession(target, window, (store, id) =>
            runner(store, id, command) is { } reason
                ? Err($"{name} did not start: {reason}")
                : OkId(id));
        return Task.FromResult(response);
    }

    public ControlResponse ReloadKeymap()
    {
        var loaded = Load();
        KeymapReloaded?.Invoke(loaded.Keymap);
        return new ControlResponse { Ok = true, Result = new ControlResult { Count = loaded.Diagnostics.Count } };
    }

    public ControlResponse ReloadGhosttyConfig() =>
        new() { Ok = true, Result = new ControlResult { Count = _configReload() } };

    /// <summary>notify: record the event in the ring, bump the unseen badge. The headless host has
    /// no banner surface at all, so every successful post takes the upstream banners-off path and
    /// returns the shared BannersOffNote in result.text (the badge tracked, no banner appeared).</summary>
    public ControlResponse SendNotification(string? target, string? window, string? title, string body)
    {
        return ResolveSession(target, window, (store, id) =>
        {
            if (store.SessionWithId(id) is not { } session)
                return Err($"no such session: {target ?? "active"}");
            var resolvedTitle = string.IsNullOrEmpty(title) ? session.DisplayName : title;
            if (store.RecordNotificationEvent(id, resolvedTitle, body) is null)
                return Err("session's window is not open");
            session.UnseenCount += 1;
            var bannerRequested = _notificationRequested?.Invoke(
                new LiveNotificationRequest(id, resolvedTitle, body)) == true;
            return new ControlResponse
            {
                Ok = true,
                Result = new ControlResult
                {
                    Id = Uuid(id),
                    Text = bannerRequested ? null : ControlNotify.BannersOffNote,
                },
            };
        });
    }

    /// <summary>Port of ControlServer+AppCommands.setTheme over the in-memory slots (no live config
    /// feed headless; the slots are the read-back).</summary>
    public ControlResponse SetTheme(ControlArgs? args)
    {
        var name = ResolvedThemeName(args?.Name);
        var light = ResolvedThemeName(args?.Light);
        var dark = ResolvedThemeName(args?.Dark);
        if (name is not null && light is not null)
            return Err("theme.set takes either a name or --light, not both");
        var lightSlot = name ?? light;
        var clearDark = dark?.ToLowerInvariant() == "none";
        foreach (var theme in new[] { lightSlot, clearDark ? null : dark })
        {
            if (theme is null) continue;
            if (!_availableThemes.Contains(theme))
                return Err($"unknown theme: {theme}");
        }
        var update = new HeadlessThemeUpdate(lightSlot, dark);
        var next = update.Apply(new(Theme.Light, Theme.Dark, Theme.Sync));
        Theme.Light = next.Light;
        Theme.Dark = next.Dark;
        _themeChanged?.Invoke(next);
        ThemeUpdateRequested?.Invoke(update);
        return ThemeStateResponse(themes: null);
    }

    public ControlResponse ListIcons() => new()
    {
        Ok = true,
        Result = new ControlResult { Icons = SegoeIconCatalog.All.Select(icon => icon.Name).ToArray() },
    };

    public ControlResponse ListThemes() => ThemeStateResponse(themes: _availableThemes.ToArray());

    private ControlResponse ThemeStateResponse(string[]? themes) => new()
    {
        Ok = true,
        Result = new ControlResult
        {
            Theme = Theme.Current,
            Themes = themes,
            Sync = Theme.Sync,
            Light = Theme.Sync ? Theme.Light : null,
            Dark = Theme.Sync ? Theme.Dark : null,
        },
    };

    /// <summary>ThemeCatalog.resolvedName: nil/whitespace-only selects the built-in default (null);
    /// otherwise the trimmed name for exact matching.</summary>
    private static string? ResolvedThemeName(string? name) => Trimmed(name);

    public ControlResponse SetSidebarVisibility(ControlToggleMode mode)
    {
        // the headless window always exists, so the upstream "no open window" arm is unreachable.
        _store.SetSidebarVisible(mode.DesiredValue(_store.SidebarVisible));
        return Ok();
    }

    public ControlResponse SetSidebarViewMode(ControlSidebarViewMode mode)
    {
        var want = mode switch
        {
            ControlSidebarViewMode.Tree => SidebarMode.Tree,
            ControlSidebarViewMode.Flagged => SidebarMode.Flagged,
            ControlSidebarViewMode.Toggle =>
                _store.SidebarMode == SidebarMode.Tree ? SidebarMode.Flagged : SidebarMode.Tree,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        _store.SetSidebarMode(want); // no-op + no save when unchanged (idempotent)
        return Ok();
    }

    /// <summary>sidebar.expand: expand every workspace. Upstream drives the live outline through a
    /// notification; headless mirrors the outcome directly on the store. A graceful no-op in flagged
    /// mode (no workspace rows), like upstream.</summary>
    public ControlResponse ExpandSidebar(string? window)
    {
        return ResolvePlacementStore(window, store =>
        {
            store.ExpandAllWorkspaces();
            return Ok();
        });
    }

    /// <summary>sidebar.collapse: collapse every workspace except the current one (the same
    /// resolution as --target active). Graceful no-op in flagged mode.</summary>
    public ControlResponse CollapseSidebar(string? window)
    {
        return ResolvePlacementStore(window, store =>
        {
            store.CollapseOtherWorkspaces();
            return Ok();
        });
    }

    public ControlResponse SetQuickTerminal(string? mode)
    {
        if (ControlToggleModes.Parse(mode, onToken: "show", offToken: "hide") is not { } parsedMode)
            return Err($"invalid quick mode: {mode ?? "toggle"}");
        var want = parsedMode.DesiredValue(Quick.Visible);
        if (want && Pick.PendingModalError is { } pickError) return Err(pickError);
        if (want) Quick.Show(dismissOnFocusLoss: false);
        else Quick.Hide();
        _quickChanged?.Invoke();
        return Ok();
    }

    /// <summary>quick.type. The upstream mount/realize poll collapses to a single check: an attached
    /// quick surface acts; no surface while hidden = never shown ("quick terminal not open"); no
    /// surface while visible = the mounted-but-unrealized state ("quick terminal not realized").</summary>
    public Task<ControlResponse> TypeQuick(string text)
    {
        ControlResponse response;
        if (Quick.Surface is { } surface)
        {
            response = surface.Inject(text) ? Ok() : Err("quick terminal not realized");
        }
        else
        {
            response = Quick.Visible ? Err("quick terminal not realized") : Err("quick terminal not open");
        }
        return Task.FromResult(response);
    }

    /// <summary>quick.text — the single-check twin of TypeQuick with the read-side error strings.</summary>
    public Task<ControlResponse> ReadQuickText(bool all, int? lines)
    {
        ControlResponse response;
        if (Quick.Surface is { } surface)
        {
            response = surface.ReadScreenText(all, lines) is { } text
                ? new ControlResponse { Ok = true, Result = new ControlResult { Text = text } }
                : Err("failed to read surface buffer");
        }
        else
        {
            response = Quick.Visible ? Err("failed to read surface buffer") : Err("quick terminal not open");
        }
        return Task.FromResult(response);
    }

    /// <summary>restore.clear: wipe every session's captured foreground command and persist, so the
    /// next launch restores plain shells (also closes the force-quit re-fire window). App-global —
    /// the single store here is "every open window".</summary>
    public ControlResponse ClearRestoreCommands()
    {
        foreach (var session in _store.Workspaces.SelectMany(w => w.Sessions))
        {
            session.ForegroundCommand = null;
            session.SplitForegroundCommand = null;
        }
        _store.Save();
        return Ok();
    }

    // MARK: - pick

    public ControlResponse OpenPick(PendingPick pick, string? window, bool follow) =>
        // follow (raise + take frontmost) needs the window controller, so the live application host
        // applies it after this succeeds; the headless host has nothing to raise
        ResolvePlacementStore(window, _ =>
        {
            if (Pick.Open(pick))
                return new ControlResponse { Ok = true, Result = new ControlResult { Id = pick.Id } };
            return Err(Pick.IsClosed ? PickRegistry.WindowNotOpenError : Pick.SlotTakenError);
        });

    public ControlResponse PickResult(string target, string? window) =>
        window is null ? Picks.Result(target, null) : ResolveWindowId(window, id => Picks.Result(target, id));

    public ControlResponse CancelPick(string target, string? window) =>
        window is null ? Picks.Cancel(target, null) : ResolveWindowId(window, id => Picks.Cancel(target, id));

    // MARK: - ask

    public ControlResponse OpenAsk(PendingAsk ask, string? target, string? window, ControlAskPlacement placement, bool follow)
    {
        // follow is applied by the live application host after this succeeds, as for pick.open
        if (ask.Style == ControlAskStyle.Terminal)
        {
            return ResolveSession(target, window, (store, id) => OpenTerminalAsk(store, id, ask, placement));
        }
        if (target is not null)
            return ResolveSession(target, window, (store, id) => OpenGuiAsk(store, id, ask, placement));
        return ResolvePlacementStore(window, store => OpenGuiAsk(store, null, ask, placement));
    }

    private ControlResponse OpenTerminalAsk(AppStore store, Guid sessionId, PendingAsk ask, ControlAskPlacement placement)
    {
        if (store.SessionWithId(sessionId) is not { } session) return Err("no such session");
        if (!PanePlacement.TryResolve(placement.Pane, placement.PaneId, session, requireVisible: true,
                PanePlacement.AskInvalidPaneError, out var resolved, out var error))
            return Err(error!);
        if (!store.OpenAsk(sessionId, ask, resolved.Identity, WindowId))
            return Err(PickController.AskAlreadyPendingError);
        return OkAsk(ask.Id, resolved.Pane);
    }

    private ControlResponse OpenGuiAsk(AppStore store, Guid? sessionId, PendingAsk ask, ControlAskPlacement placement)
    {
        AskAnchor? anchor = null;
        if (sessionId is { } id)
        {
            if (store.SessionWithId(id) is not { } session) return Err("no such session");
            if (store.SelectedSessionId != id || Zoom.Target is not null || Dashboard.IsOpen)
                return Err("session not visible");
            if (!PanePlacement.TryResolve(placement.Pane, placement.PaneId, session, requireVisible: true,
                    PanePlacement.AskInvalidPaneError, out var resolved, out var error))
                return Err(error!);
            anchor = new AskAnchor(id, resolved.Pane, resolved.Identity);
        }
        if (!Pick.OpenAsk(ask with { Anchor = anchor }, WindowId))
            return Err(Pick.IsClosed ? PickRegistry.WindowNotOpenError : Pick.SlotTakenError);
        return OkAsk(ask.Id, anchor?.Pane);
    }

    private static ControlResponse OkAsk(string id, OverlayPane? pane) => new()
    {
        Ok = true,
        Result = new ControlResult { Id = id, Pane = pane is { } role ? OverlayPanes.Wire(role) : null },
    };

    public ControlResponse AskResult(string target, string? window) =>
        window is null ? Asks.Result(target, null) : ResolveWindowId(window, id => Asks.Result(target, id));

    public ControlResponse CancelAsk(string target, string? window) =>
        window is null ? CancelAskIn(target, null) : ResolveWindowId(window, id => CancelAskIn(target, id));

    private ControlResponse CancelAskIn(string target, Guid? windowId)
    {
        if (Asks.Find(target) is not { } found || (windowId is { } wanted && found.WindowId != wanted))
            return AskRegistry.Unknown(target);
        if (found.Result.Result != ControlAskOutcome.Pending) return Ok();
        // the live application host routes a cancel to the owner's window; this window cannot
        // reach another window's model
        if (found.WindowId != WindowId) return AskRegistry.Unknown(target);
        switch (found.LiveOwner)
        {
            case AskOwner.Window:
                Pick.CancelAsk(target);
                break;
            case AskOwner.Session owner:
                _store.CancelAsk(owner.SessionId, target);
                break;
        }
        return Ok();
    }

    /// <summary>A GUI question anchored to a session ends once its session or pane leaves the
    /// screen, as upstream's view cancels it. Runs under the host lock after model changes.</summary>
    public void ReconcileAskAnchor()
    {
        if (Pick.PendingAsk is { Anchor: { } anchor } ask && !AskAnchors.IsShown(_store, anchor))
            Pick.CancelAsk(ask.Id);
    }

    // MARK: - window commands
    //
    // The headless host owns exactly ONE virtual window (never on screen). Arms that only need the
    // window IDENTITY keep full upstream semantics: list, select (the one window is already
    // frontmost), rename, delete (pinned "cannot delete last window" — exactly what upstream answers
    // for the last window). Arms that need a real compositor window (new/close/resize/move/zoom/
    // fullscreen) resolve the target first (so the pinned "no such window: …" errors stay intact)
    // and then answer the documented headless refusal. Each divergence goes to PLATFORM-NOTES.

    public ControlResponse WindowNew(string? name) =>
        // upstream opens a second on-screen window; the headless host is single-window by design.
        Err(WindowManagementUnavailable);

    public ControlResponse WindowList()
    {
        var node = new ControlWindowNode
        {
            Id = Uuid(WindowId),
            Name = WindowName,
            Open = true,   // the headless window is always "open" (its store is always loaded)
            Active = true, // and always frontmost
            // AutoFollowMs is projected through the public tree builder (the same value upstream's
            // window.list reads off the store).
            AutoFollowMs = _store.ControlTree().AutoFollowMs,
            SidebarVisible = _store.SidebarVisible,
            // no on-screen frame headless: geometry/fullscreen/zoomed stay null, the same shape an
            // upstream CLOSED window reports (documented adaptation).
        };
        return new ControlResponse { Ok = true, Result = new ControlResult { Windows = new[] { node } } };
    }

    public Task<ControlResponse> WindowSelect(string? target) =>
        // the single window is already frontmost, so a resolved select is an immediate ok+id.
        Task.FromResult(ResolveWindowId(target, OkId));

    public Task<ControlResponse> WindowClose(string? target) =>
        // upstream closes the on-screen window; headless cannot close its only virtual window.
        Task.FromResult(ResolveWindowId(target, _ => Err(WindowManagementUnavailable)));

    public ControlResponse WindowRename(string? target, string name)
    {
        return ResolveWindowId(target, id =>
        {
            if (Session.TrimmedOrNull(TerminalText.Sanitized(name)) is { } cleaned) WindowName = cleaned;
            return OkId(id);
        });
    }

    public ControlResponse WindowDelete(string? target) =>
        // keep-at-least-one applies verbatim: with exactly one window upstream answers this too.
        ResolveWindowId(target, _ => Err("cannot delete last window"));

    public ControlResponse WindowResize(string? target, int width, int height) =>
        ResolveWindowId(target, _ => Err(WindowManagementUnavailable));

    public ControlResponse WindowMove(string? target, int x, int y, int? display) =>
        // upstream validates the display index against the screen list first; headless has no
        // screens to enumerate, so the whole arm is the documented refusal.
        ResolveWindowId(target, _ => Err(WindowManagementUnavailable));

    public ControlResponse WindowZoom(string? target) =>
        ResolveWindowId(target, _ => Err(WindowManagementUnavailable));

    public ControlResponse WindowFullscreen(string? target) =>
        ResolveWindowId(target, _ => Err(WindowManagementUnavailable));
}

/// <summary>The outcome of opening a command failure panel; <see cref="Fallback"/> is the
/// placement rejection text when the panel went over the whole session instead.</summary>
public sealed record CommandFailureHudResult(ControlResponse Response, string? Fallback);
