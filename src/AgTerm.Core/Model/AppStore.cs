using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

/// <summary>
/// The whole per-window app state: the workspace tree and the current selection. Selection is a
/// single session id — workspace rows are non-selectable disclosure headers, so one id is enough;
/// the owning workspace is derived. Not thread-safe; the host confines it to its model context
/// (upstream is @MainActor). Split into partial files mirroring the Swift AppStore+*.swift
/// extensions.
/// </summary>
public sealed partial class AppStore
{
    public List<Workspace> Workspaces { get; set; }

    private Guid? _selectedSessionId;

    /// <summary>A CHANGE drops the fresh workspace: close, removal, undo and reopen all reselect by
    /// assigning here. Writing the same id again keeps it, because single-session navigation and
    /// overlay --follow do that without moving the user.</summary>
    public Guid? SelectedSessionId
    {
        get => _selectedSessionId;
        set
        {
            if (_selectedSessionId != value) _freshWorkspaceId = null;
            _selectedSessionId = value;
        }
    }

    /// <summary>The workspace that holds the target without owning the selection: a foreground
    /// create, or SelectWorkspace on an empty one. Live state, never saved.</summary>
    private Guid? _freshWorkspaceId;

    internal void ForgetFreshWorkspace(Guid workspaceId)
    {
        if (_freshWorkspaceId == workspaceId) _freshWorkspaceId = null;
    }

    /// <summary>Transient sidebar multi-selection. Not persisted: it is UI command state, while
    /// SelectedSessionId remains the durable active terminal target.</summary>
    internal List<Guid> SidebarSelectionRaw = new();

    /// <summary>Whether this window's sidebar is shown. Per-window UI state, persisted in the
    /// snapshot; flipped by the sidebar control command and the host UI.</summary>
    public bool SidebarVisible { get; set; } = true;

    /// <summary>Which view this window's sidebar renders: the normal workspace tree or a flat list
    /// of the flagged working-set. Persisted.</summary>
    public SidebarMode SidebarMode { get; set; } = SidebarMode.Tree;

    /// <summary>The workspace the sidebar tree is focused (zoomed) on, or null for the full tree.
    /// Persisted. Auto-cleared when the focused workspace is removed or when a session outside it
    /// becomes selected.</summary>
    public Guid? FocusedWorkspaceId { get; set; }

    /// <summary>This window's sidebar width in points. Persisted; clamped on restore.</summary>
    public double SidebarWidth { get; set; } = SidebarWidthDefault;

    /// <summary>The sidebar width default and drag/restore bounds, shared so a hand-edited snapshot
    /// can't drive an out-of-range frame.</summary>
    public const double SidebarWidthDefault = 220;
    public const double SidebarWidthMin = 160;
    public const double SidebarWidthMax = 560;

    /// <summary>The persisted split-divider left-pane fraction bounds. The live capture skips
    /// degenerate extremes outside this range and Restore clamps to it.</summary>
    public const double SplitRatioMin = 0.05;
    public const double SplitRatioMax = 0.95;

    /// <summary>The even split fraction a never-moved divider renders at; the base for a relative
    /// session.resize when Session.SplitRatio is still null.</summary>
    public const double SplitRatioDefault = 0.5;

    /// <summary>Clamp a left-pane split fraction to SplitRatioMin...SplitRatioMax.</summary>
    public static double ClampSplitRatio(double ratio) =>
        Math.Min(SplitRatioMax, Math.Max(SplitRatioMin, ratio));

    /// <summary>Most-recently-selected session ids, front = current. Drives the Ctrl-Tab switcher
    /// (Items[1] is the previous session). Persisted in the snapshot.</summary>
    public RecencyStack<Guid> SessionRecency { get; private set; } = new();

    /// <summary>The latest session/workspace close that can still be undone. The hidden
    /// sessions/workspaces live in the pending-close records; this observed summary is the host-free
    /// state the host presents.</summary>
    public PendingCloseSummary? PendingCloseSummary { get; set; }

    internal readonly Dictionary<Guid, PendingCloseRecord> PendingCloseRecords = new();
    internal readonly List<Guid> PendingCloseOrder = new();
    internal readonly Dictionary<Guid, CancellationTokenSource> PendingCloseTasks = new();

    public Action<Action>? PendingCloseFinalizationScheduler { get; set; }

    private readonly PersistenceStore _persistence;
    internal readonly RecentClosedStore? RecentClosedStore;
    internal Action? RecentClosedDidChange;
    internal readonly Action<ControlEventDraft>? ControlEventSink;

    /// <summary>Coalesces the high-frequency selection/font saves: a click-storm or a font ramp
    /// schedules one write ~0.3 s after the burst settles instead of hitting disk per event. Save
    /// cancels any pending scheduled save, so the quit-flush still captures the latest.</summary>
    private readonly Debouncer _saveDebouncer = new();

    /// <summary>The quiet window before a scheduled (selection/font) save writes to disk.</summary>
    private const double SaveDebounceInterval = 0.3;

    /// <summary>The idle timeout in seconds after which the window auto-jumps its selection to the
    /// oldest blocked session, or null when auto-follow is off (the default).</summary>
    internal double? AutoFollowTimeout;

    /// <summary>Whether auto-follow suppresses the jump while the current session is active (the
    /// opt-in "don't auto-follow away from a running session" toggle). Default false.</summary>
    internal bool AutoFollowStayOnActive;

    /// <summary>The last time the user interacted with this window (a keystroke or a manual
    /// selection), stamped unconditionally by NoteUserActivity so the idle metric is independent of
    /// the feature being on. Null until the first interaction.</summary>
    internal DateTimeOffset? LastActivityAt;

    /// <summary>Coalesces user activity into a single deferred AutoFollowFire: each NoteUserActivity
    /// reschedules, so the fire runs only once the user has been idle for AutoFollowTimeout.
    /// Internal so tests can drive its Flush seam.</summary>
    internal readonly Debouncer AutoFollowDebouncer = new();

    /// <summary>Non-zero while a non-terminal editor or transient overlay in this window owns first
    /// responder. AutoFollowFire no-ops while this is positive. A COUNT (not a bool) so the several
    /// independent suppressors can overlap safely.</summary>
    internal int AutoFollowSuppressionCount;

    /// <summary>Diagnostics sink for swallowed save failures; null writes nowhere.</summary>
    internal Action<string>? LogSink;

    /// <summary>
    /// The host's surface factory — the port's stand-in for "SwiftUI renders the shown pane and its
    /// surface realizes" (upstream realizes on show via Observation; the port has no runloop, so
    /// the two show paths call this directly — same pattern as the auto-follow re-arm seam,
    /// PLATFORM-NOTES). Invoked when a session is SHOWN (selected, or created selected) and its
    /// main pane has no surface yet; returning null leaves it unrealized (the arms report the
    /// pinned "session not realized" errors). Null realizer = headless behavior: surfaces only
    /// attach externally.
    /// </summary>
    public Func<Session, ITerminalSurface?>? SurfaceRealizer;

    /// <summary>The live app's "eager deck": every session gets its surface AT CREATION (upstream:
    /// each session's view exists from the moment it appears in the deck), so session.type reaches
    /// background sessions without a select. Default false = headless behavior, which preserves
    /// the pinned "session not realized" parity error (PLATFORM-NOTES, phase 4).</summary>
    public bool EagerSurfaceRealization { get; set; }

    /// <summary>Selection moved (select, deselect, or a session created selected) — the C# event
    /// replacing upstream's Observation of selectedSessionID for the window chrome (which pane is
    /// visible). Same precedent as AutoFollowed / SidebarVisibilityChanged (PLATFORM-NOTES).</summary>
    public event Action? SelectionChanged;

    /// <summary>Coarse sidebar invalidation: SOMETHING a sidebar row renders (structure, names,
    /// selection, flags, collapse/focus/mode/visibility, indicators, unseen counts) changed — the
    /// C# event replacing upstream's per-field Observation for the sidebar view (same precedent as
    /// SelectionChanged, PLATFORM-NOTES). Fired by every sidebar-relevant mutator; the view snaps a
    /// row projection under the host gate and diffs. Deliberately NOT tied to the event ring —
    /// flag/collapse/focus/mode do not emit tree.changed (wire parity).</summary>
    public event Action? SidebarChanged;

    /// <summary>Raise SidebarChanged — public so the HOST can invalidate for out-of-band row
    /// changes the store never sees as mutators (live OSC title/cwd reports feeding displayName,
    /// the notify arm's unseen bump).</summary>
    public void NotifySidebar() => SidebarChanged?.Invoke();

    /// <summary>Realize the shown session's main pane through the host seam, once.</summary>
    private void RealizeIfShown(Session session)
    {
        if (session.Surface is null && SurfaceRealizer is { } realize)
            session.Surface = realize(session);
    }

    /// <summary>Realize the CURRENTLY selected session — the launch-bootstrap show: Restore()
    /// installs the persisted selection without going through SelectSession, but the window
    /// renders that session immediately (upstream realizes it on first draw). Host calls this
    /// once after a snapshot restore.</summary>
    public void RealizeActiveSession()
    {
        if (ActiveSession is { } shown) RealizeIfShown(shown);
    }

    /// <summary>Realize EVERY session's main pane (the eager-deck bootstrap: the live host calls
    /// this after a snapshot restore instead of RealizeActiveSession). Already-realized panes are
    /// left alone; a null realizer makes this a no-op.</summary>
    public void RealizeAllSessions()
    {
        foreach (var workspace in Workspaces)
        {
            foreach (var session in workspace.Sessions) RealizeIfShown(session);
        }
    }

    public AppStore(List<Workspace>? workspaces = null, Guid? selectedSessionId = null,
                    PersistenceStore? persistence = null,
                    RecentClosedStore? recentClosedStore = null,
                    Action? recentClosedDidChange = null,
                    Action<ControlEventDraft>? controlEventSink = null,
                    string? stateDirectory = null)
    {
        Workspaces = workspaces ?? new List<Workspace>();
        SelectedSessionId = selectedSessionId;
        _persistence = persistence ?? new PersistenceStore();
        _stateDirectory = stateDirectory;
        RecentClosedStore = recentClosedStore;
        RecentClosedDidChange = recentClosedDidChange;
        ControlEventSink = controlEventSink;
    }

    /// <summary>The state dir this store's side files (rendered .text watermarks) live under. Null
    /// falls back to the process default — the app passes its own root so a window never touches
    /// another window's files.</summary>
    private readonly string? _stateDirectory;

    /// <summary>The state dir this store resolves its side files against, for hosts that write the
    /// same files (the rendered .text PNG) and must land in the same place.</summary>
    public string? StateDirectory => _stateDirectory;

    /// <summary>The currently selected session, derived from SelectedSessionId.</summary>
    public Session? ActiveSession =>
        SelectedSessionId is { } id ? SessionWithId(id) : null;

    /// <summary>The workspace a new session should land in: a freshly created workspace, else the
    /// selected session's workspace, else the last workspace (null when there are no workspaces).</summary>
    public Guid? CurrentWorkspaceId
    {
        get
        {
            if (_freshWorkspaceId is { } fresh && Workspaces.Any(w => w.Id == fresh))
                return fresh;
            if (SelectedSessionId is { } id && WorkspaceForSession(id) is { } workspace)
                return workspace.Id;
            return Workspaces.Count > 0 ? Workspaces[^1].Id : null;
        }
    }

    /// <summary>The auto-generated name for the next new workspace (workspace 1, workspace 2, …).</summary>
    public string DefaultWorkspaceName => $"workspace {Workspaces.Count + 1}";

    /// <summary>
    /// Projects this store's workspace/session model into the control-channel tree payload.
    /// Foreground command lookup is supplied by the host because live process inspection is
    /// platform-specific.
    /// </summary>
    public ControlTree ControlTree(Func<Session, string[]?>? foreground = null,
                                   Func<Session, string[]?>? splitForeground = null,
                                   Func<Session, double?>? fontSize = null,
                                   Func<Session, double?>? splitFontSize = null,
                                   Func<Session, double?>? scratchFontSize = null,
                                   Func<bool?>? quickVisible = null,
                                   Func<string?>? zoomedSurface = null,
                                   Func<string[]?>? dashboardMembers = null,
                                   Func<string?>? dashboardHighlighted = null,
                                   Func<double?>? dashboardFontSize = null,
                                   Func<string?>? dashboardFontMode = null)
    {
        var activeId = SelectedSessionId;
        var activeWorkspaceId = activeId is { } id ? WorkspaceForSession(id)?.Id : null;
        var nodes = Workspaces.Select(workspace =>
        {
            var sessions = workspace.Sessions.Select(session =>
            {
                var idle = session.AgentIndicator.Status == AgentStatus.Idle;
                var status = idle ? null : StatusWire(session.AgentIndicator.Status);
                var statusPane = idle ? null : session.AgentIndicator.StatusPane is { } p ? PaneWire(p) : null;
                var surfaces = TerminalZoomSurfaces.AllCases
                    .Where(surface => surface.IsAvailable(session))
                    .Select(surface => new ControlSurfaceNode
                    {
                        Id = new TerminalSurfaceId(session.Id, surface).RawValue,
                        Kind = surface.WireName(),
                        Active = surface.IsActive(session),
                        Visible = surface.IsVisible(session),
                    })
                    .ToArray();
                return new ControlSessionNode
                {
                    Id = session.Id.ToString("D").ToUpperInvariant(),
                    Name = session.DisplayName,
                    Cwd = session.EffectiveCwd,
                    Title = session.OscTitle,
                    Active = session.Id == activeId,
                    Split = session.IsSplit,
                    SplitRatio = session.HasSplit ? session.SplitRatio : null,
                    SplitFocused = session.HasSplit ? session.SplitFocused : null,
                    Overlay = session.OverlayActive,
                    OverlaySizePercent = session.OverlayActive ? session.OverlaySizePercent : null,
                    Scratch = session.ScratchActive,
                    Flagged = session.Flagged,
                    CommandWait = session.InitialCommand is not null && session.CommandWait ? true : null,
                    Foreground = foreground?.Invoke(session),
                    SplitForeground = splitForeground?.Invoke(session),
                    // the PERSISTED overrides, never the transient pending payloads, so a read after
                    // one fired still reports what stays pinned.
                    RestoreCommand = session.RestoreCommand,
                    SplitRestoreCommand = session.SplitRestoreCommand,
                    Status = status,
                    StatusPane = statusPane,
                    StatusBlink = idle ? null : session.AgentIndicator.Blink ? true : null,
                    StatusColor = idle ? null : session.AgentIndicator.Color,
                    StatusShape = idle ? null
                        : session.AgentIndicator.Shape is { } shape ? ShapeWire(shape) : null,
                    Background = session.BackgroundWatermark,
                    Unseen = session.UnseenCount > 0 ? session.UnseenCount : null,
                    FontSize = fontSize?.Invoke(session),
                    SplitFontSize = splitFontSize?.Invoke(session),
                    ScratchFontSize = scratchFontSize?.Invoke(session),
                    Surfaces = surfaces,
                };
            }).ToArray();
            return new ControlWorkspaceNode
            {
                Id = workspace.Id.ToString("D").ToUpperInvariant(),
                Name = workspace.Name,
                Active = workspace.Id == activeWorkspaceId,
                Focused = workspace.Id == FocusedWorkspaceId ? true : null,
                Collapsed = workspace.IsExpanded ? null : true,
                Sessions = sessions,
            };
        }).ToArray();
        return new ControlTree
        {
            Workspaces = nodes,
            IdleMs = IdleMs(),
            AutoFollowMs = AutoFollowMs,
            SidebarVisible = SidebarVisible,
            SidebarMode = SidebarMode == SidebarMode.Flagged ? "flagged" : "tree",
            QuickVisible = quickVisible?.Invoke(),
            ZoomedSurface = zoomedSurface?.Invoke(),
            DashboardMembers = dashboardMembers?.Invoke(),
            DashboardHighlighted = dashboardHighlighted?.Invoke(),
            DashboardFontSize = dashboardFontSize?.Invoke(),
            DashboardFontMode = dashboardFontMode?.Invoke(),
        };
    }

    private static string StatusWire(AgentStatus status) => status switch
    {
        AgentStatus.Idle => "idle",
        AgentStatus.Active => "active",
        AgentStatus.Completed => "completed",
        AgentStatus.Blocked => "blocked",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string PaneWire(StatusPane pane) => pane switch
    {
        StatusPane.Left => "left",
        StatusPane.Right => "right",
        StatusPane.Scratch => "scratch",
        _ => throw new ArgumentOutOfRangeException(nameof(pane)),
    };

    private static string ShapeWire(StatusShape shape) => shape switch
    {
        StatusShape.Circle => "circle",
        StatusShape.Square => "square",
        StatusShape.Triangle => "triangle",
        StatusShape.Diamond => "diamond",
        StatusShape.Capsule => "capsule",
        StatusShape.Star => "star",
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>Creates a workspace and appends it. When clearFocus (the default) it clears any
    /// active focus so the new (empty) workspace is immediately visible. Pass collapsed: true to
    /// create it already collapsed in the sidebar (backs workspace.new --collapsed). The new
    /// workspace becomes the current one until the selection moves when holdsTarget is true; null
    /// follows clearFocus.</summary>
    public Workspace AddWorkspace(string name, bool collapsed = false, bool clearFocus = true, bool? holdsTarget = null)
    {
        var workspace = new Workspace(TerminalText.Sanitized(name), isExpanded: !collapsed);
        Workspaces.Add(workspace);
        if (clearFocus) FocusedWorkspaceId = null;
        if (holdsTarget ?? clearFocus) _freshWorkspaceId = workspace.Id;
        ScheduleTreeChanged();
        Save();
        return workspace;
    }

    /// <summary>Backs workspace.select. A workspace with sessions selects its first one and takes
    /// the target back from a fresh workspace (the first session may already be the selection, which
    /// alone would not move it). An empty workspace has nothing to select: the focus filter is
    /// cleared if it hides it, and it becomes the target.</summary>
    public bool SelectWorkspace(Guid workspaceId)
    {
        if (Workspaces.FirstOrDefault(w => w.Id == workspaceId) is not { } workspace) return false;
        if (workspace.Sessions.FirstOrDefault() is { } first)
        {
            SelectSession(first.Id);
            _freshWorkspaceId = null;
            return true;
        }
        if (FocusedWorkspaceId is { } focused && focused != workspaceId)
            SetFocusedWorkspace(null);
        _freshWorkspaceId = workspaceId;
        return true;
    }

    /// <summary>The first workspace whose name exactly equals name (case-sensitive, trimmed), or
    /// null when none matches or the name is blank. Backs session.new --workspace-name.</summary>
    public Workspace? WorkspaceNamed(string name)
    {
        if (Session.TrimmedOrNull(TerminalText.Sanitized(name)) is not { } needle) return null;
        return Workspaces.FirstOrDefault(w => w.Name == needle);
    }

    /// <summary>The workspace named name, created if none exists (idempotent); clearFocus (default
    /// true) is forwarded to AddWorkspace on the create path. Null only when blank. Backs
    /// --workspace-name --create-workspace.</summary>
    public Workspace? EnsureWorkspace(string name, bool clearFocus = true)
    {
        if (Session.TrimmedOrNull(TerminalText.Sanitized(name)) is not { } needle) return null;
        return WorkspaceNamed(needle) ?? AddWorkspace(needle, clearFocus: clearFocus, holdsTarget: clearFocus);
    }

    /// <summary>Creates a session in the given workspace and, when select is true (the default),
    /// selects it; select: false appends it in the background, leaving selection/focus/recency
    /// untouched (backs session.new --no-select). An optional name seeds CustomName (trimmed; blank
    /// = the auto basename). index null appends (default); set inserts at the clamped position,
    /// backing session.new --after/--before. Returns null if no workspace matches.</summary>
    public Session? AddSession(Guid toWorkspace, string cwd, string? command = null,
                               string? name = null, bool wait = false, int? at = null, bool select = true)
    {
        var wsIndex = Workspaces.FindIndex(w => w.Id == toWorkspace);
        if (wsIndex < 0) return null;
        var session = new Session(TerminalText.Sanitized(cwd),
            customName: name is null ? null : Session.TrimmedOrNull(TerminalText.Sanitized(name)))
        {
            InitialCommand = command,
            CommandWait = wait,
        };
        if (at is { } index)
        {
            var destination = Math.Max(0, Math.Min(index, Workspaces[wsIndex].Sessions.Count));
            Workspaces[wsIndex].Sessions.Insert(destination, session);
        }
        else
        {
            Workspaces[wsIndex].Sessions.Add(session);
        }
        // a background add (session.new --no-select) leaves selection/focus/recency untouched —
        // and, headless, stays UNREALIZED (upstream: no view is shown, so no surface exists until
        // a select). The live host's eager deck realizes it right here instead.
        if (select)
        {
            SelectedSessionId = session.Id;
            AutoUnfocusIfOutsideFocus(session.Id); // a control-driven add into another workspace must reveal it
            RecordRecency();
            RealizeIfShown(session);
            SelectionChanged?.Invoke();
        }
        else if (EagerSurfaceRealization)
        {
            RealizeIfShown(session);
        }
        EmitSessionCreated(session, toWorkspace);
        Save();
        return session;
    }

    /// <summary>
    /// Selects a session (or clears the selection when passed null) and persists. A non-null id
    /// that matches no session is ignored, leaving the current selection untouched; null always
    /// deselects. Visiting a session clears its unseen badge. An autoReset agent indicator (the
    /// one-time completed flash) is reset to idle on BOTH the session moved to and the one moved
    /// from; a non-autoReset indicator is left untouched (keep-state).
    /// </summary>
    public void SelectSession(Guid? sessionId, IReadOnlyList<Guid>? sidebarSelection = null)
    {
        if (sessionId is { } wanted && SessionWithId(wanted) is null) return;
        var previous = SelectedSessionId;
        SelectedSessionId = sessionId;
        if (sessionId is { } shown && SessionWithId(shown) is { } visible) RealizeIfShown(visible);
        if (sidebarSelection is not null)
        {
            SetSidebarSelection(sidebarSelection);
        }
        else
        {
            ReplaceSidebarSelection(sessionId);
        }
        AutoUnfocusIfOutsideFocus(sessionId);
        if (sessionId is { } visited) ClearUnseen(visited);
        ClearAutoResetIndicator(sessionId); // visit: you've seen it
        ClearAutoResetIndicator(previous);  // leave: a one-time status must not linger on the row you left
        RecordRecency();
        ScheduleSave(); // selection fires on every click/keystroke — coalesce the writes
        SelectionChanged?.Invoke();
        NotifySidebar(); // the debounced save must not delay the row-highlight refresh
    }

    /// <summary>Clears focus when the newly selected session lives outside the focused workspace, so
    /// an explicit cross-set select reveals its target — the active session is then always inside
    /// the visible set. No-op when unfocused, when nothing is selected, or when the selection is
    /// inside the focused workspace.</summary>
    internal void AutoUnfocusIfOutsideFocus(Guid? sessionId)
    {
        if (FocusedWorkspaceId is not { } focused || sessionId is not { } id) return;
        if (WorkspaceForSession(id)?.Id != focused) FocusedWorkspaceId = null;
    }

    /// <summary>Reset a session's agent indicator to idle when it is marked autoReset (the one-time
    /// completed flash). No-op for null / an unknown id / a non-autoReset indicator.</summary>
    private void ClearAutoResetIndicator(Guid? id)
    {
        if (id is not { } target) return;
        if (SessionWithId(target) is not { } session || !session.AgentIndicator.AutoReset) return;
        SetAgentIndicator(new AgentIndicator(), target);
    }

    /// <summary>Clears a session's unseen-notification badge — it's been looked at. No-op for an
    /// unknown id. Not persisted (the count is ephemeral), so it never triggers a Save.</summary>
    public void ClearUnseen(Guid sessionId)
    {
        if (SessionWithId(sessionId) is not { } session) return;
        session.UnseenCount = 0;
        NotifySidebar();
    }

    /// <summary>Pushes the current selection to the front of the recency stack (the Ctrl-Tab
    /// order). No-op when nothing is selected.</summary>
    internal void RecordRecency()
    {
        if (SelectedSessionId is { } id) SessionRecency.Push(id);
    }

    internal void RemoveFromRecency(Guid id) => SessionRecency.Remove(id);

    /// <summary>Removes a session, tears down its surfaces, and — if it was the active session —
    /// reselects the most-recently-active surviving session in scope, falling back to the
    /// positional neighbor.</summary>
    public void CloseSession(Guid sessionId)
    {
        if (LocationOfSession(sessionId) is not { } location) return;
        var wasActive = SelectedSessionId == sessionId;
        var workspace = Workspaces[location.WorkspaceIndex];
        var removed = workspace.Sessions[location.SessionIndex];
        workspace.Sessions.RemoveAt(location.SessionIndex);
        EmitSessionClosed(removed, workspace.Id);
        RecordRecentClosedSession(removed, workspace.Id, workspace.Name,
            location.WorkspaceIndex, location.SessionIndex);
        removed.Surface?.Teardown();
        removed.SplitSurface?.Teardown();
        removed.OverlaySurface?.Teardown();
        removed.ScratchSurface?.Teardown();
        WatermarkStorage.RemoveRenderedText(sessionId, _stateDirectory); // drop any rendered .text PNG; the session is gone
        SessionRecency.Remove(sessionId);
        if (wasActive)
        {
            SelectedSessionId = CloseReselectionTarget(location);
            ReplaceSidebarSelection(SelectedSessionId);
            AutoUnfocusIfOutsideFocus(SelectedSessionId); // the reselected session may live outside the focused workspace
            RecordRecency();
        }
        else
        {
            PruneSidebarSelection();
        }
        Save();
    }

    /// <summary>Whether a workspace may be removed: one workspace is always kept, so removal is
    /// allowed only when more than one exists.</summary>
    public bool CanRemoveWorkspace => Workspaces.Count > 1;

    /// <summary>Removes a workspace and every session in it, tearing down each session's surfaces
    /// and pruning them from the recency stack. No-ops unless more than one workspace exists. If
    /// the active session lived in the removed workspace, reselects the first session of a
    /// remaining workspace, or null when no sessions remain.</summary>
    public void RemoveWorkspace(Guid workspaceId)
    {
        if (!CanRemoveWorkspace) return;
        var index = Workspaces.FindIndex(w => w.Id == workspaceId);
        if (index < 0) return;
        var workspace = Workspaces[index];
        var removingActive = SelectedSessionId is { } active
            && workspace.Sessions.Any(s => s.Id == active);
        RecordRecentClosedWorkspace(workspace, removingActive ? SelectedSessionId : null);
        foreach (var session in workspace.Sessions) EmitSessionClosed(session, workspace.Id);
        if (workspace.Sessions.Count == 0) ScheduleTreeChanged();
        foreach (var session in workspace.Sessions)
        {
            session.Surface?.Teardown();
            session.SplitSurface?.Teardown();
            session.OverlaySurface?.Teardown();
            session.ScratchSurface?.Teardown();
            WatermarkStorage.RemoveRenderedText(session.Id, _stateDirectory); // drop any rendered .text PNG; the session is gone
            SessionRecency.Remove(session.Id);
        }
        if (FocusedWorkspaceId == workspaceId) FocusedWorkspaceId = null; // the focused root is gone
        Workspaces.RemoveAt(index);
        ForgetFreshWorkspace(workspaceId);
        if (removingActive)
        {
            var fallbackIndex = Math.Min(index, Workspaces.Count - 1);
            SelectedSessionId = Workspaces[fallbackIndex].Sessions.FirstOrDefault()?.Id
                ?? Workspaces.FirstOrDefault(w => w.Sessions.Count > 0)?.Sessions[0].Id;
            ReplaceSidebarSelection(SelectedSessionId);
            AutoUnfocusIfOutsideFocus(SelectedSessionId); // the reselected session may live outside the focused workspace
            RecordRecency();
        }
        else
        {
            PruneSidebarSelection();
        }
        Save();
    }

    /// <summary>
    /// Moves a session to another workspace (or reorders within the same one), keeping the SAME
    /// Session instance so its attached surface and live shell survive. index is the destination
    /// position in the target's session list AFTER the move's removal (clamped to bounds); null
    /// appends. SelectedSessionId is unaffected. No-ops if the session or target workspace is
    /// unknown. Moving the ACTIVE session out of the focused workspace auto-unfocuses.
    /// </summary>
    public void MoveSession(Guid sessionId, Guid toWorkspace, int? at = null)
    {
        if (LocationOfSession(sessionId) is not { } source) return;
        var targetIndex = Workspaces.FindIndex(w => w.Id == toWorkspace);
        if (targetIndex < 0) return;
        var before = Workspaces.Select(w => w.Sessions.Select(s => s.Id).ToArray()).ToArray();

        var session = Workspaces[source.WorkspaceIndex].Sessions[source.SessionIndex];
        Workspaces[source.WorkspaceIndex].Sessions.RemoveAt(source.SessionIndex);
        var targetSessions = Workspaces[targetIndex].Sessions;
        var destination = Math.Max(0, Math.Min(at ?? targetSessions.Count, targetSessions.Count));
        targetSessions.Insert(destination, session);
        if (sessionId == SelectedSessionId) AutoUnfocusIfOutsideFocus(sessionId);
        PruneSidebarSelection();
        if (TreeOrderChanged(before)) ScheduleTreeChanged();
        Save();
    }

    /// <summary>Moves selected sessions in their current tree order. With index == null, a
    /// multi-session context move appends cross-workspace sessions and leaves sessions already in
    /// the target in place; a one-session call matches MoveSession and appends even within that
    /// workspace. With an explicit index, this is a drag drop: remove every dragged session first,
    /// then insert the dragged block at that post-removal target index. Returns the number of
    /// sessions actually moved.</summary>
    public int MoveSessions(IReadOnlyList<Guid> sessionIds, Guid toWorkspace, int? at = null)
    {
        if (!Workspaces.Any(w => w.Id == toWorkspace)) return 0;
        var before = Workspaces.Select(w => w.Sessions.Select(s => s.Id).ToArray()).ToArray();
        var movingIds = OrderedSessionIds(new HashSet<Guid>(sessionIds));
        // A one-element batch is wire-equivalent to MoveSession: even within the destination
        // workspace it moves to the end. Multi-selection context moves leave existing members in place.
        if (at is null && movingIds.Count > 1)
        {
            movingIds = movingIds.Where(id => WorkspaceForSession(id)?.Id != toWorkspace).ToList();
        }
        if (movingIds.Count == 0) return 0;

        var moving = new List<Session>();
        foreach (var id in movingIds)
        {
            if (LocationOfSession(id) is not { } location) continue;
            moving.Add(Workspaces[location.WorkspaceIndex].Sessions[location.SessionIndex]);
            Workspaces[location.WorkspaceIndex].Sessions.RemoveAt(location.SessionIndex);
        }
        var targetIndex = Workspaces.FindIndex(w => w.Id == toWorkspace);
        if (targetIndex < 0 || moving.Count == 0)
        {
            PruneSidebarSelection();
            return 0;
        }
        var targetSessions = Workspaces[targetIndex].Sessions;
        var destination = Math.Max(0, Math.Min(at ?? targetSessions.Count, targetSessions.Count));
        targetSessions.InsertRange(destination, moving);
        if (SelectedSessionId is { } selected && movingIds.Contains(selected))
            AutoUnfocusIfOutsideFocus(selected);
        PruneSidebarSelection();
        if (TreeOrderChanged(before)) ScheduleTreeChanged();
        Save();
        return moving.Count;
    }

    private bool TreeOrderChanged(Guid[][] before)
    {
        if (before.Length != Workspaces.Count) return true;
        for (var i = 0; i < before.Length; i++)
        {
            if (!before[i].SequenceEqual(Workspaces[i].Sessions.Select(s => s.Id))) return true;
        }
        return false;
    }

    /// <summary>Reorders a session one relative step within its own workspace (up/down/top/bottom),
    /// reusing MoveSession with the same workspace id. No-op (no write) on an unknown id or when
    /// the move would leave order unchanged.</summary>
    public void ReorderSession(Guid id, ReorderDirection direction)
    {
        if (LocationOfSession(id) is not { } loc) return;
        var count = Workspaces[loc.WorkspaceIndex].Sessions.Count;
        if (direction.DestinationIndex(loc.SessionIndex, count) is not { } dest) return;
        MoveSession(id, Workspaces[loc.WorkspaceIndex].Id, dest);
    }

    /// <summary>Moves a workspace to index among its siblings, mirroring MoveSession's
    /// remove/clamp/insert/save shape. No-op on an unknown id.</summary>
    public void MoveWorkspace(Guid id, int at)
    {
        var current = Workspaces.FindIndex(w => w.Id == id);
        if (current < 0) return;
        var before = Workspaces.Select(w => w.Id).ToArray();
        var workspace = Workspaces[current];
        Workspaces.RemoveAt(current);
        var dest = Math.Max(0, Math.Min(at, Workspaces.Count));
        Workspaces.Insert(dest, workspace);
        if (!before.SequenceEqual(Workspaces.Select(w => w.Id))) ScheduleTreeChanged();
        Save();
    }

    /// <summary>Reorders a workspace one relative step among its siblings, reusing MoveWorkspace.
    /// No-op (no write) on an unknown id or when the move would leave order unchanged.</summary>
    public void ReorderWorkspace(Guid id, ReorderDirection direction)
    {
        var current = Workspaces.FindIndex(w => w.Id == id);
        if (current < 0) return;
        if (direction.DestinationIndex(current, Workspaces.Count) is not { } dest) return;
        MoveWorkspace(id, dest);
    }

    /// <summary>The owning workspace id, the session's index within it, and that workspace's session
    /// count, or null for an unknown id.</summary>
    public (Guid Workspace, int Index, int Count)? SessionLocation(Guid ofSession)
    {
        if (LocationOfSession(ofSession) is not { } loc) return null;
        var workspace = Workspaces[loc.WorkspaceIndex];
        return (workspace.Id, loc.SessionIndex, workspace.Sessions.Count);
    }

    /// <summary>
    /// Steps the selection through the flattened VISIBLE/FILTERED session list (NavigableSessions),
    /// in the sidebar's visual order. Next/Previous move one and WRAP at the ends WITHIN the
    /// filtered set; First/Last jump to the ends of the filtered list. With no/invalid current
    /// selection, Next/Previous land on its first session. No-op when the filtered list is empty.
    /// Routes through SelectSession, inheriting recency, badge clearing, persistence, and workspace
    /// derivation.
    /// </summary>
    public void NavigateSession(SessionNavigation direction)
    {
        var sessions = NavigableSessions;
        var ids = sessions.Select(s => s.Id).ToList();
        if (ids.Count == 0) return;
        var first = ids[0];
        var last = ids[^1];
        Guid target;
        switch (direction)
        {
            case SessionNavigation.First:
                target = first;
                break;
            case SessionNavigation.Last:
                target = last;
                break;
            case SessionNavigation.Next:
            case SessionNavigation.Previous:
                if (SelectedSessionId is { } current && ids.IndexOf(current) is var i and >= 0)
                {
                    var step = direction == SessionNavigation.Next ? 1 : -1;
                    target = ids[((i + step) % ids.Count + ids.Count) % ids.Count]; // cycle within the filtered set
                }
                else
                {
                    target = first; // no/invalid selection -> first
                }
                break;
            case SessionNavigation.NextAttention:
            case SessionNavigation.PreviousAttention:
                if (AttentionTarget(sessions, forward: direction == SessionNavigation.NextAttention)
                    is not { } found) return;
                target = found;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(direction));
        }
        SelectSession(target);
    }

    /// <summary>The next/previous session needing attention (status blocked or completed) in the
    /// flattened order, scanning from the current selection and WRAPPING around. The current session
    /// is excluded, so repeated steps cycle through the others. With no/invalid selection the scan
    /// starts from the tree end opposite the direction. Null when no other attention session exists.</summary>
    private Guid? AttentionTarget(IReadOnlyList<Session> sessions, bool forward)
    {
        var ids = sessions.Select(s => s.Id).ToList();
        var count = ids.Count;
        if (count == 0) return null;
        var step = forward ? 1 : -1;
        int? curIndex = SelectedSessionId is { } current && ids.IndexOf(current) is var found and >= 0
            ? found : null;
        var start = curIndex ?? (forward ? -1 : count);
        for (var k = 1; k <= count; k++)
        {
            var idx = ((start + step * k) % count + count) % count;
            if (curIndex is { } cur && idx == cur) break; // wrapped back to the current session, none other
            if (sessions[idx].AgentIndicator.Status.NeedsAttention()) return ids[idx];
        }
        return null;
    }

    /// <summary>Records a session's terminal font size (points) and persists it. No-ops when
    /// unchanged. The save is debounced (~0.3 s) so a font ramp coalesces into one write.</summary>
    public void SetFontSize(Guid sessionId, double size)
    {
        if (SessionWithId(sessionId) is not { } session || session.FontSize == size) return;
        session.FontSize = size;
        ScheduleSave();
    }

    /// <summary>Clears every session's per-session font-size override (back to the app default).
    /// No-ops (no write) when nothing was overridden.</summary>
    public void ResetSessionFontSizes()
    {
        var changed = false;
        foreach (var workspace in Workspaces)
        {
            foreach (var session in workspace.Sessions.Where(s => s.FontSize is not null))
            {
                session.FontSize = null;
                changed = true;
            }
        }
        if (changed) Save();
    }

    /// <summary>Raised whenever the sidebar visibility actually changes — the analogue of the
    /// upstream .agtermSidebarVisibilityChanged notification the control server observes to refresh
    /// its window.list cache.</summary>
    public event Action? SidebarVisibilityChanged;

    /// <summary>Sets this window's sidebar visibility and persists it. Clean no-op (no write) when
    /// unchanged, so callers can delta-compute their desired state without duplicating the
    /// persistence gate.</summary>
    public void SetSidebarVisible(bool visible)
    {
        if (SidebarVisible == visible) return;
        SidebarVisible = visible;
        Save();
        SidebarVisibilityChanged?.Invoke();
    }

    /// <summary>Flips this window's sidebar visibility and persists the new state.</summary>
    public void ToggleSidebarVisible() => SetSidebarVisible(!SidebarVisible);

    /// <summary>Sets the sidebar mode and persists it. Clean no-op (no write) when unchanged.</summary>
    public void SetSidebarMode(SidebarMode mode)
    {
        if (SidebarMode == mode) return;
        SidebarMode = mode;
        PruneSidebarSelection();
        Save();
    }

    /// <summary>Sets (or clears) the focused workspace and persists it. Clean no-op (no write) when
    /// unchanged. Passing null unfocuses.</summary>
    public void SetFocusedWorkspace(Guid? id)
    {
        if (FocusedWorkspaceId == id) return;
        FocusedWorkspaceId = id;
        if (id is { } shown && _freshWorkspaceId is { } fresh && fresh != shown) _freshWorkspaceId = null;
        PruneSidebarSelection();
        Save();
    }

    /// <summary>Sets one workspace's expand/collapse state and persists it. Clean no-op (no write)
    /// for an unknown id or when unchanged.</summary>
    public void SetWorkspaceExpanded(Guid id, bool expanded)
    {
        var index = Workspaces.FindIndex(w => w.Id == id);
        if (index < 0 || Workspaces[index].IsExpanded == expanded) return;
        Workspaces[index].IsExpanded = expanded;
        Save();
    }

    /// <summary>Marks each workspace expanded iff its id is in expandedIds and persists the collapse
    /// state. One Save for the whole diff; a clean no-op (no write) when nothing changed.</summary>
    public void SetWorkspacesExpanded(IReadOnlySet<Guid> expandedIds)
    {
        var changed = false;
        foreach (var workspace in Workspaces)
        {
            var expanded = expandedIds.Contains(workspace.Id);
            if (workspace.IsExpanded != expanded)
            {
                workspace.IsExpanded = expanded;
                changed = true;
            }
        }
        if (changed) Save();
    }

    /// <summary>The focused workspace, resolved from FocusedWorkspaceId — null when unfocused OR
    /// when the id is stale (its workspace no longer exists).</summary>
    public Workspace? FocusedWorkspace =>
        FocusedWorkspaceId is { } id ? Workspaces.FirstOrDefault(w => w.Id == id) : null;

    /// <summary>The workspaces the sidebar tree should render: just the focused workspace when
    /// FocusedWorkspaceId is set AND that workspace still exists, else all workspaces.</summary>
    public List<Workspace> VisibleWorkspaces =>
        FocusedWorkspace is { } focused ? new List<Workspace> { focused } : Workspaces;

    /// <summary>Sets (or clears) a session's flag — the durable flagged working-set membership.
    /// Persists the change. Clean no-op (no write) for an unknown id or when the flag is already in
    /// the requested state.</summary>
    public void SetFlag(bool on, Guid forSession)
    {
        if (SessionWithId(forSession) is not { } session || session.Flagged == on) return;
        session.Flagged = on;
        PruneSidebarSelection();
        Save();
    }

    /// <summary>Sets (or clears) multiple sessions' flags in one save. Unknown ids are ignored.</summary>
    public void SetFlag(bool on, IReadOnlyList<Guid> forSessions)
    {
        var targetIds = new HashSet<Guid>(forSessions);
        if (targetIds.Count == 0) return;
        var changed = false;
        foreach (var workspace in Workspaces)
        {
            foreach (var session in workspace.Sessions
                         .Where(s => targetIds.Contains(s.Id) && s.Flagged != on))
            {
                session.Flagged = on;
                changed = true;
            }
        }
        if (changed)
        {
            PruneSidebarSelection();
            Save();
        }
    }

    /// <summary>Sets (or clears) a session's background watermark and persists it. Clean no-op (no
    /// write) for an unknown id or when the spec is unchanged, so a repeated session.background set
    /// is idempotent. Returns whether the spec actually CHANGED, so the host can gate the
    /// per-surface config apply on a real change.</summary>
    public bool SetBackgroundWatermark(BackgroundWatermark? watermark, Guid forSession)
    {
        if (SessionWithId(forSession) is not { } session || session.BackgroundWatermark == watermark)
            return false;
        var previous = session.BackgroundWatermark;
        session.BackgroundWatermark = watermark;
        // a .text watermark owns a rendered <id>.png; switching to anything that isn't .text leaves
        // it id-keyed but unreferenced, so drop it here.
        if (previous?.Kind == WatermarkKind.Text && watermark?.Kind != WatermarkKind.Text)
        {
            WatermarkStorage.RemoveRenderedText(forSession, _stateDirectory);
        }
        Save();
        return true;
    }

    /// <summary>Unflags every session across all workspaces in one Save. No-ops (no write) when
    /// nothing is flagged. Backs the Clear Flagged action and the session.flag clear control mode.</summary>
    public void ClearFlags()
    {
        var changed = false;
        foreach (var workspace in Workspaces)
        {
            foreach (var session in workspace.Sessions.Where(s => s.Flagged))
            {
                session.Flagged = false;
                changed = true;
            }
        }
        if (changed)
        {
            PruneSidebarSelection();
            Save();
        }
    }

    /// <summary>The flagged sessions across all workspaces in tree order. A pure derived
    /// projection — the flat sidebar view renders this directly.</summary>
    public List<Session> FlaggedSessions =>
        Workspaces.SelectMany(w => w.Sessions).Where(s => s.Flagged).ToList();

    /// <summary>The session set navigation operates over — the VISIBLE/FILTERED set, not the whole
    /// tree: the flagged sessions in flagged sidebar mode, the focused workspace's sessions when a
    /// workspace is focused, else all sessions.</summary>
    public List<Session> NavigableSessions =>
        SidebarMode == SidebarMode.Flagged
            ? FlaggedSessions
            : VisibleWorkspaces.SelectMany(w => w.Sessions).ToList();

    // MARK: - Persistence

    /// <summary>Builds a Snapshot value of the current tree. Each session captures its live
    /// CurrentCwd (or InitialCwd if no PWD report has arrived).</summary>
    public Snapshot Snapshot()
    {
        var workspaceSnapshots = Workspaces.Select(workspace => new WorkspaceSnapshot
        {
            Id = workspace.Id,
            Name = workspace.Name,
            Sessions = workspace.Sessions.Select(SessionSnapshot).ToArray(),
            // only a collapsed workspace writes the flag; an expanded one omits it (null) so an
            // all-expanded tree serializes identically to a legacy snapshot.
            Collapsed = workspace.IsExpanded ? null : true,
        }).ToArray();
        return new Snapshot
        {
            SelectedSessionId = SelectedSessionId,
            Workspaces = workspaceSnapshots,
            SidebarWidth = SidebarWidth,
            SidebarVisible = SidebarVisible,
            SidebarMode = SidebarMode,
            FocusedWorkspaceId = FocusedWorkspaceId,
            SessionRecency = SessionRecency.Items.ToArray(),
        };
    }

    /// <summary>
    /// Rebuilds the tree from a snapshot: fresh Sessions (surfaces and shells spawn lazily on first
    /// display) keyed by the persisted ids so the restored SelectedSessionId still resolves.
    /// Replaces the current state wholesale. Deliberately does NOT call Save: it loads what was
    /// just read from disk. launchRestore marks an APP-BOOTSTRAP restore and is the only thing that
    /// arms a persisted session.restore override for this launch; it defaults to false because a
    /// mid-process window reload must not execute anything.
    /// </summary>
    public void Restore(Snapshot snapshot, bool launchRestore = false)
    {
        _freshWorkspaceId = null;
        // fold workspaces sharing an id into the first occurrence, and keep only the first snapshot
        // of any repeated session id, wherever it sits: a file written by a build that could
        // duplicate either stays unreachable past the first match otherwise, and re-saves the corruption.
        var seenSessionIds = new HashSet<Guid>();
        var restored = new List<Workspace>();
        foreach (var workspaceSnapshot in snapshot.Workspaces)
        {
            var sessions = workspaceSnapshot.Sessions
                .Where(s => seenSessionIds.Add(s.Id))
                .Select(s => SessionFrom(s, launchRestore))
                .ToList();
            var existing = restored.FindIndex(w => w.Id == workspaceSnapshot.Id);
            if (existing >= 0)
            {
                restored[existing].Sessions.AddRange(sessions);
                continue;
            }
            // absent/null collapsed → expanded (back-compat with snapshots written before the field existed).
            restored.Add(new Workspace(workspaceSnapshot.Id, workspaceSnapshot.Name, sessions,
                isExpanded: !(workspaceSnapshot.Collapsed ?? false)));
        }
        Workspaces = restored;
        // clamp on restore (not just null-default) so a corrupt or hand-edited snapshot can't drive
        // an out-of-range frame width; the drag path clamps to the same bounds.
        SidebarWidth = Math.Min(SidebarWidthMax,
            Math.Max(SidebarWidthMin, snapshot.SidebarWidth ?? SidebarWidthDefault));
        SidebarVisible = snapshot.SidebarVisible ?? true;
        SidebarMode = snapshot.SidebarMode ?? SidebarMode.Tree;
        // a stale focus id (its workspace not in the restored tree) is harmless — VisibleWorkspaces
        // falls back to the full tree — so restore it verbatim; null stays unfocused.
        FocusedWorkspaceId = snapshot.FocusedWorkspaceId;
        if (snapshot.SelectedSessionId is { } id && SessionWithId(id) is null)
        {
            SelectedSessionId = null;
        }
        else
        {
            SelectedSessionId = snapshot.SelectedSessionId;
        }
        ReplaceSidebarSelection(SelectedSessionId);
        // re-seed the Ctrl-Tab order from the persisted list (dropping ids not in the restored tree)
        // so the switcher works right after relaunch; the restored selection floats to the front.
        var restoredIds = Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).ToHashSet();
        SessionRecency = new RecencyStack<Guid>(
            items: (snapshot.SessionRecency ?? Array.Empty<Guid>()).Where(restoredIds.Contains));
        RecordRecency();
    }

    /// <summary>Persists the current state eagerly. Called after every structural mutation and on
    /// terminate. Cancels any pending debounced save first. A write failure is logged and
    /// swallowed — a transient disk error must not bring down the model.</summary>
    public void Save()
    {
        SaveChecked();
        // every persisted mutation is (at least potentially) a sidebar-visible change; the event
        // is a coarse invalidation and the view's diff makes over-notification cheap
        NotifySidebar();
    }

    /// <summary>Save that REPORTS whether the write landed instead of swallowing the failure, for a
    /// caller whose acknowledgement must not outrun the disk (SetRestoreCommand). A failed save is
    /// rolled back by that caller so memory keeps matching disk.</summary>
    internal bool SaveChecked()
    {
        _saveDebouncer.Cancel();
        try
        {
            _persistence.Save(Snapshot());
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log($"save failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Debounces a Save ~0.3 s out, coalescing the rapid selection/font writes. Used only
    /// by SelectSession/SetFontSize; structural mutations call Save immediately.</summary>
    private void ScheduleSave()
    {
        _saveDebouncer.Schedule(SaveDebounceInterval, Save);
    }

    /// <summary>Drops any pending debounced save WITHOUT writing — unlike Save, which cancels then
    /// writes. Used when the owning window is being deleted: the per-window file is about to be
    /// removed, so a scheduled save must be dropped rather than flushed.</summary>
    public void CancelPendingSave() => _saveDebouncer.Cancel();

    private void Log(string message) => LogSink?.Invoke(message);

    // MARK: - Derivation

    /// <summary>The workspace that owns the given session, if any.</summary>
    public Workspace? WorkspaceForSession(Guid sessionId)
    {
        if (LocationOfSession(sessionId) is not { } location) return null;
        return Workspaces[location.WorkspaceIndex];
    }

    /// <summary>The session with the given id across all workspaces, if any.</summary>
    public Session? SessionWithId(Guid sessionId)
    {
        foreach (var workspace in Workspaces)
        {
            if (workspace.Sessions.FirstOrDefault(s => s.Id == sessionId) is { } session) return session;
        }
        return null;
    }

    internal (int WorkspaceIndex, int SessionIndex)? LocationOfSession(Guid sessionId)
    {
        for (var wi = 0; wi < Workspaces.Count; wi++)
        {
            var si = Workspaces[wi].Sessions.FindIndex(s => s.Id == sessionId);
            if (si >= 0) return (wi, si);
        }
        return null;
    }

    private List<Guid> OrderedSessionIds(IReadOnlySet<Guid> matching) =>
        Workspaces.SelectMany(w => w.Sessions).Select(s => s.Id).Where(matching.Contains).ToList();

    /// <summary>Picks the next selection after removing the session at location. Prefers the
    /// session that shifted into the removed slot, then the previous one in that workspace, then
    /// the first session of any remaining workspace.</summary>
    internal Guid? ReselectionTarget((int WorkspaceIndex, int SessionIndex) after)
    {
        var sessions = Workspaces[after.WorkspaceIndex].Sessions;
        if (after.SessionIndex < sessions.Count) return sessions[after.SessionIndex].Id;
        if (after.SessionIndex > 0 && sessions.Count > 0)
        {
            return sessions[Math.Min(after.SessionIndex - 1, sessions.Count - 1)].Id;
        }
        foreach (var workspace in Workspaces)
        {
            if (workspace.Sessions.FirstOrDefault() is { } first) return first.Id;
        }
        return null;
    }

    internal SessionSnapshot SessionSnapshot(Session session) => session.ToSnapshot();

    internal WorkspaceSnapshot WorkspaceSnapshot(Workspace workspace) => workspace.ToSnapshot();

    /// <summary>Rebuilds one session from its snapshot. launchRestore marks an APP-BOOTSTRAP
    /// restore, the only path allowed to arm a persisted restoreCommand override for this launch by
    /// copying it into the transient PendingRestoreCommand the surface factory consumes.
    ///
    /// A split hidden at the last quit is NOT rebuilt (HasSplit follows IsSplit), so its pinned
    /// override describes a pane that no longer exists: it is DROPPED here, the same rule
    /// CloseSplit applies when the pane goes away.
    ///
    /// Captured foreground commands are armed only by the launch restore, like the override.</summary>
    internal Session SessionFrom(SessionSnapshot snapshot, bool launchRestore = false)
    {
        var session = new Session(snapshot.Cwd, snapshot.Id, snapshot.CustomName)
        {
            IsSplit = snapshot.IsSplit ?? false,
            FontSize = snapshot.FontSize,
            InitialSplitCwd = snapshot.SplitCwd,
            SplitRatio = snapshot.SplitRatio is { } ratio ? ClampSplitRatio(ratio) : null,
            Flagged = snapshot.Flagged ?? false,
            InitialCommand = snapshot.InitialCommand,
            CommandWait = snapshot.CommandWait ?? false,
            WasRestored = true,
            BackgroundWatermark = snapshot.BackgroundWatermark,
            RestoreCommand = snapshot.RestoreCommand,
        };
        session.HasSplit = session.IsSplit;
        session.SplitRestoreCommand = session.IsSplit ? snapshot.SplitRestoreCommand : null;
        if (launchRestore)
        {
            session.PendingRestoreCommand = snapshot.RestoreCommand;
            if (session.IsSplit) session.PendingSplitRestoreCommand = session.SplitRestoreCommand;
            session.ForegroundCommand = snapshot.ForegroundCommand;
            if (session.IsSplit) session.SplitForegroundCommand = snapshot.SplitForegroundCommand;
        }
        return session;
    }

    internal Workspace WorkspaceFrom(WorkspaceSnapshot snapshot) =>
        new(snapshot.Id, snapshot.Name,
            snapshot.Sessions.Select(s => SessionFrom(s)).ToList(),
            isExpanded: !(snapshot.Collapsed ?? false));
}
