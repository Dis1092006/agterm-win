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
    ControlResponse CloseSession(string? target, string? window);
    ControlResponse CloseSessions(IReadOnlyList<string> targets, string? window);
    ControlResponse RenameSession(string? target, string? window, string name);
    ControlResponse RevealSession(string? target, string? window);
    ControlResponse CreateWorkspace(string? window, string? name, bool collapsed);
    ControlResponse SelectWorkspace(string? target, string? window);
    ControlResponse RenameWorkspace(string? target, string? window, string name);
    ControlResponse DeleteWorkspace(string? target, string? window);
    ControlResponse MoveSession(string? target, string? window, ControlSessionMove move);
    ControlResponse MoveSessions(IReadOnlyList<string> targets, string? window, ControlSessionMove move);
    ControlResponse MoveWorkspace(string? target, string? window, ReorderDirection direction);
    ControlResponse FocusWorkspace(string? target, string? window, string? mode);
    ControlResponse SetWorkspaceExpansion(string? target, string? window, bool expanded);
    /// <summary>Set or clear a workspace's own terminal theme (the Windows-only workspace.theme); a
    /// null name clears it. The host resolves the target like RenameWorkspace and checks the name
    /// against its theme list.</summary>
    ControlResponse SetWorkspaceTheme(string? target, string? window, string? themeName);
    ControlResponse SetSessionFlag(string? target, string? window, string? mode);
    ControlResponse MarkSessionSeen(string? target, string? window);
    ControlResponse SetSessionStatus(string? target, string? window, ControlSessionStatusUpdate update);

    /// <summary>Write a pane's PERSISTED restore-command override (consumed on the NEXT launch, never
    /// this run). The host resolves the target session and the live pane slot, then stores the
    /// tri-state value; it also owns the pane rejections that need a session (scratch, right without a
    /// split, an unresolvable paneID given without an explicit pane).</summary>
    ControlResponse SetSessionRestore(string? target, string? window, ControlSessionRestoreUpdate update);

    ControlResponse SplitSession(string? target, string? window, string? mode);
    ControlResponse ScratchSession(string? target, string? window, string? mode, string? command);
    ControlResponse FocusSessionPane(string? target, string? window, string? pane);
    ControlResponse ResizeSplit(string? target, string? window, ControlSplitResize resize);
    ControlResponse SetSurfaceZoom(string? target, string? window, ControlToggleMode mode);
    ControlResponse SetDashboard(IReadOnlyList<string> targets, string? window, bool close,
                                 DashboardFontMode fontMode, bool mru);
    ControlResponse Font(string? target, string? window, string? pane, string action);
    ControlResponse ReloadKeymap();
    ControlResponse ReloadGhosttyConfig();
    ControlResponse SendNotification(string? target, string? window, string? title, string body);
    ControlResponse SetTheme(ControlArgs? args);
    ControlResponse ListThemes();
    ControlResponse SetSidebarVisibility(ControlToggleMode mode);
    ControlResponse SetSidebarViewMode(ControlSidebarViewMode mode);
    ControlResponse ExpandSidebar(string? window);
    ControlResponse CollapseSidebar(string? window);
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

public sealed record ControlSessionTypeOptions(string Text, bool Select, string? Pane);

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

public sealed record ControlSessionTextOptions(string? Pane, bool All, int? Lines);
