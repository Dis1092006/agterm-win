using AgTerm.Core.Model;

namespace AgTerm.Core.Protocol;

/// <summary>
/// App-facing operations a host must provide for commands routed through ControlDispatcher.
/// The dispatcher owns command parsing and response shape; the host keeps target resolution and
/// platform-specific side effects. The 1:1 port of the Swift ControlActions protocol.
/// </summary>
public interface IControlActions
{
    ControlResponse ControlTree(string? window);
    ControlResponse ReadEvents(ControlEventReadOptions options);
    ControlResponse CreateSession(ControlSessionCreateOptions options);
    ControlResponse DuplicateSession(string? target, string? window);
    ControlResponse SelectSession(string? target, string? window);
    ControlResponse GoSession(string? window, SessionNavigation direction);
    ControlResponse CloseSession(string? target, string? window, bool undoable = false);
    ControlResponse CloseSessions(IReadOnlyList<string> targets, string? window, bool undoable = false);
    ControlResponse RenameSession(string? target, string? window, string name);
    ControlResponse RevealSession(string? target, string? window);
    ControlResponse CreateWorkspace(string? window, string? name, bool collapsed);
    ControlResponse SelectWorkspace(string? target, string? window);
    ControlResponse RenameWorkspace(string? target, string? window, string name);
    ControlResponse DeleteWorkspace(string? target, string? window);
    ControlResponse MoveSession(string? target, string? window, ControlSessionMove move);
    ControlResponse MoveSessions(IReadOnlyList<string> targets, string? window, ControlSessionMove move);
    ControlResponse MoveWorkspace(string? target, string? window, ReorderDirection direction);
    ControlResponse FocusWorkspace(string? target, string? window, ControlWorkspaceFocusMode mode);

    /// <summary>workspace.filter: apply or release the sidebar focus filter over the marked set. No
    /// target; the answer is always ok.</summary>
    ControlResponse SetWorkspaceFilter(string? window, ControlToggleMode mode);
    ControlResponse SetWorkspaceExpansion(string? target, string? window, bool expanded);
    /// <summary>Set or clear a workspace's own terminal theme (the Windows-only workspace.theme); a
    /// null name clears it. The host resolves the target like RenameWorkspace and checks the name
    /// against its theme list.</summary>
    ControlResponse SetWorkspaceTheme(string? target, string? window, string? themeName);
    /// <summary>Set or clear a workspace's own icon (the Windows-only workspace.icon); a null or
    /// blank name clears it. The host resolves the target like RenameWorkspace and checks the name
    /// against the icon catalog.</summary>
    ControlResponse SetWorkspaceIcon(string? target, string? window, string? iconName);
    ControlResponse SetSessionFlag(string? target, string? window, string? mode);

    /// <summary>Sets (trimmed, validated text) or clears (null) what a session is about.</summary>
    ControlResponse SetSessionContext(string? target, string? window, string? context);
    ControlResponse MarkSessionSeen(string? target, string? window);
    ControlResponse SetSessionStatus(string? target, string? window, ControlSessionStatusUpdate update);

    /// <summary>Write a pane's PERSISTED restore-command override (consumed on the NEXT launch, never
    /// this run). The host resolves the target session and the live pane slot, then stores the
    /// tri-state value; it also owns the pane rejections that need a session (scratch, right without a
    /// split, an unresolvable paneID given without an explicit pane).</summary>
    ControlResponse SetSessionRestore(string? target, string? window, ControlSessionRestoreUpdate update);

    ControlResponse SplitSession(string? target, string? window, string? mode, SplitAxis? axis = null);

    /// <summary>Closes a session's split pane for good (shown or hidden), tearing its terminal down;
    /// a session without a split answers ok. Keyboard focus goes to the main pane.</summary>
    ControlResponse CloseSessionSplit(string? target, string? window);

    /// <summary>Exchanges a split session's two panes with their live roles and state. A session without
    /// a split, a pane still being realized or a surface that cannot change role answers an error.</summary>
    Task<ControlResponse> SwapSessionPanes(string? target, string? window);
    ControlResponse ScratchSession(string? target, string? window, string? mode, string? command, bool follow = false);
    ControlResponse FocusSessionPane(string? target, string? window, string? pane);
    ControlResponse ResizeSplit(string? target, string? window, ControlSplitResize resize);
    ControlResponse SetSurfaceZoom(string? target, string? window, ControlToggleMode mode);
    /// <summary>Reports a surface's zero-based cursor column. Takes surface.zoom's target vocabulary
    /// (surface:&lt;id&gt;:&lt;kind&gt;, quick, active); a non-empty paneId requires a session target and
    /// picks the pane by its token.</summary>
    ControlResponse ReadSurfaceCursor(string? target, string? window, string? paneId);
    ControlResponse SetDashboard(IReadOnlyList<string> targets, string? window, bool close,
                                 DashboardFontMode fontMode, bool mru);
    ControlResponse Font(string? target, string? window, string? pane, string action);
    ControlResponse ReloadKeymap();
    ControlResponse ListKeymap();
    Task<ControlResponse> RunCustomCommand(string name, string? target, string? window);
    ControlResponse ReloadGhosttyConfig();
    ControlResponse SendNotification(string? target, string? window, string? title, string body);
    ControlResponse SetTheme(ControlArgs? args);
    ControlResponse ListThemes();
    /// <summary>The icon names workspace.icon accepts (the Windows-only icon.list).</summary>
    ControlResponse ListIcons();
    ControlResponse SetSidebarVisibility(ControlToggleMode mode);
    ControlResponse SetSidebarViewMode(ControlSidebarViewMode mode);
    ControlResponse ExpandSidebar(string? window);
    ControlResponse CollapseSidebar(string? window);

    /// <summary>version: the running app's identity and, when different, the one installed on disk.</summary>
    ControlResponse AppVersion();

    /// <summary>recent.list: the recently closed entries, newest first.</summary>
    ControlResponse ListRecentClosed();

    /// <summary>recent.clear: empty the recently closed list.</summary>
    ControlResponse ClearRecentClosed();

    /// <summary>recent.open: reopen an entry (the newest when target is null) into a window.</summary>
    ControlResponse OpenRecentClosed(string? target, string? window);

    /// <summary>sidebar.width: set the sidebar width in points (clamped); the result echoes the stored width.</summary>
    ControlResponse SetSidebarWidth(double points, string? window);

    /// <summary>workspace.go: step to the next or previous visible workspace of a window, wrapping.</summary>
    ControlResponse GoWorkspace(string? window, WorkspaceNavigation direction);

    /// <summary>window.go: raise the next or previous open window in library order, wrapping.</summary>
    Task<ControlResponse> WindowGo(WorkspaceNavigation direction);
    ControlResponse SetQuickTerminal(string? mode);
    Task<ControlResponse> TypeQuick(string text);
    Task<ControlResponse> ReadQuickText(bool all, int? lines);
    Task<ControlResponse> TypeSession(string? target, string? window, ControlSessionTypeOptions options);
    ControlResponse CopySessionSelection(string? target, string? window);
    ControlResponse PasteSession(string? target, string? window);
    ControlResponse SelectAllSession(string? target, string? window);
    Task<ControlResponse> SearchSession(string? target, string? window, string? text, string? to);
    ControlResponse OpenSessionOverlay(string? target, string? window, ControlSessionOverlayOpenOptions options);
    ControlResponse CloseSessionOverlay(string? target, string? window);
    ControlResponse ResizeSessionOverlay(string? target, string? window, int? sizePercent);
    ControlResponse SessionOverlayResult(string? target, string? window);
    ControlResponse SetSessionBackground(string? target, string? window, ControlSessionBackgroundOptions options);
    ControlResponse ReadSessionText(string? target, string? window, ControlSessionTextOptions options);
    ControlResponse WindowNew(string? name);
    ControlResponse WindowList();
    Task<ControlResponse> WindowSelect(string? target);
    Task<ControlResponse> WindowClose(string? target);
    ControlResponse WindowRename(string? target, string name);
    ControlResponse WindowDelete(string? target);
    ControlResponse WindowResize(string? target, int width, int height);
    ControlResponse WindowMove(string? target, int x, int y, int? display);
    ControlResponse WindowZoom(string? target);
    ControlResponse WindowFullscreen(string? target);
    ControlResponse ClearRestoreCommands();
    ControlResponse OpenPick(PendingPick pick, string? window, bool follow);
    ControlResponse PickResult(string target, string? window);
    ControlResponse CancelPick(string target, string? window);
    ControlResponse OpenAsk(PendingAsk ask, string? target, string? window, ControlAskPlacement placement, bool follow);
    ControlResponse AskResult(string target, string? window);
    ControlResponse CancelAsk(string target, string? window);
    ControlResponse OpenHud(string? target, string? window, HudSpec spec, ControlHudPlacement placement);
    ControlResponse UpdateHud(string? target, string? window, HudSpec spec, ControlHudPlacement placement);
    ControlResponse CloseHud(string? target, string? window);
}

public sealed record ControlSessionTypeOptions(string Text, bool Select, string? Pane, string? PaneId = null);

/// <summary>The pane selectors of ask.open, resolved by the host against the target session:
/// PaneId is a live pane token that overrides Pane.</summary>
public sealed record ControlAskPlacement(OverlayPane? Pane = null, string? PaneId = null);

/// <summary>The pane selectors of session.hud.open and update, resolved by the host against the
/// target session: PaneId is a live pane token that overrides Pane.</summary>
public sealed record ControlHudPlacement(OverlayPane? Pane = null, string? PaneId = null);

public sealed record ControlSessionOverlayOpenOptions(
    string Command, string? Cwd, bool Wait, int? SizePercent, string? BackgroundColor,
    bool Follow = false);

public sealed record ControlSessionBackgroundOptions(BackgroundWatermark? Watermark);

public sealed record ControlSessionTextOptions(string? Pane, bool All, int? Lines, string? PaneId = null);
