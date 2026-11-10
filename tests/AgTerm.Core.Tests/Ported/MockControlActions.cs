using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Ported;

/// <summary>
/// Ported 1:1 from agtermCore MockControlActions.swift: the shared IControlActions test double for
/// the dispatcher suites — it records every routed call as a Call value and hands back a per-command
/// canned ControlResponse, so a dispatcher test asserts on WHAT was routed (and with which arguments)
/// without any app host. Lives in its own file because it is a fixture shared by
/// ControlDispatcherTests and ControlDispatcherDashboardTests, not a test suite.
/// </summary>
public sealed class MockControlActions : IControlActions
{
    public abstract record Call
    {
        public sealed record Tree(string? Window) : Call;
        public sealed record EventsRead(ControlEventReadOptions Options) : Call;
        public sealed record SessionNew(ControlSessionCreateOptions Options) : Call;
        public sealed record SessionDuplicate(string? Target, string? Window) : Call;
        public sealed record SessionSelect(string? Target, string? Window) : Call;
        public sealed record SessionGo(string? Window, SessionNavigation Direction) : Call;
        public sealed record SessionClose(string? Target, string? Window, bool Undoable = false) : Call;
        public sealed record SessionCloseBatch(IReadOnlyList<string> Targets, string? Window, bool Undoable = false) : Call
        {
            public bool Equals(SessionCloseBatch? other) =>
                other is not null && Targets.SequenceEqual(other.Targets) && Window == other.Window
                && Undoable == other.Undoable;
            public override int GetHashCode() => HashCode.Combine(Targets.Count, Window, Undoable);
        }
        public sealed record SessionRename(string? Target, string? Window, string Name) : Call;
        public sealed record SessionReveal(string? Target, string? Window) : Call;
        public sealed record WorkspaceNew(string? Window, string? Name, bool Collapsed) : Call;
        public sealed record WorkspaceSelect(string? Target, string? Window) : Call;
        public sealed record WorkspaceRename(string? Target, string? Window, string Name) : Call;
        public sealed record WorkspaceTheme(string? Target, string? Window, string? ThemeName) : Call;
        public sealed record WorkspaceDelete(string? Target, string? Window) : Call;
        public sealed record SessionMove(string? Target, string? Window, ControlSessionMove Move) : Call;
        public sealed record SessionMoveBatch(IReadOnlyList<string> Targets, string? Window, ControlSessionMove Move) : Call
        {
            public bool Equals(SessionMoveBatch? other) =>
                other is not null && Targets.SequenceEqual(other.Targets) && Window == other.Window
                && Move == other.Move;
            public override int GetHashCode() => HashCode.Combine(Targets.Count, Window, Move);
        }
        public sealed record WorkspaceMove(string? Target, string? Window, ReorderDirection Direction) : Call;
        public sealed record WorkspaceFocus(string? Target, string? Window, ControlWorkspaceFocusMode Mode) : Call;
        public sealed record WorkspaceFilter(string? Window, ControlToggleMode Mode) : Call;
        public sealed record WorkspaceExpansion(string? Target, string? Window, bool Expanded) : Call;
        public sealed record WorkspaceFlag(string? Target, string? Window, ControlToggleMode Mode) : Call;
        public sealed record SessionFlag(string? Target, string? Window, string? Mode) : Call;
        public sealed record FlaggedMove(string? Target, string? Window, string Anchor, FlaggedOrderPlacement Placement) : Call;
        public sealed record SessionContext(string? Target, string? Window, string? Context) : Call;
        public sealed record SessionInfo(string? Target, string? Window, AgTerm.Core.Model.SessionInfo? Info) : Call;
        public sealed record SessionTint(string? Target, string? Window, ControlTintUpdate Update) : Call;
        public sealed record WorkspaceTint(string? Target, string? Window, ControlTintUpdate Update) : Call;
        public sealed record MarkSessionSeen(string? Target, string? Window) : Call;
        public sealed record SessionStatus(string? Target, string? Window, ControlSessionStatusUpdate Update) : Call;
        public sealed record SessionRestore(string? Target, string? Window, ControlSessionRestoreUpdate Update) : Call;
        public sealed record SessionSplit(string? Target, string? Window, string? Mode, SplitAxis? Axis = null) : Call;
        public sealed record SessionSplitClose(string? Target, string? Window) : Call;
        public sealed record SessionSwap(string? Target, string? Window) : Call;
        public sealed record SessionScratch(string? Target, string? Window, string? Mode, string? Command, bool Follow = false) : Call;
        public sealed record SessionFocus(string? Target, string? Window, string? Pane) : Call;
        public sealed record SessionResize(string? Target, string? Window, ControlSplitResize Resize) : Call;
        public sealed record SurfaceZoom(string? Target, string? Window, ControlToggleMode Mode) : Call;
        public sealed record SurfaceCursor(string? Target, string? Window, string? PaneId) : Call;
        public sealed record Dashboard(IReadOnlyList<string> Targets, string? Window, bool Close,
                                       DashboardFontMode FontMode, bool Mru) : Call
        {
            public bool Equals(Dashboard? other) =>
                other is not null && Targets.SequenceEqual(other.Targets) && Window == other.Window
                && Close == other.Close && FontMode == other.FontMode && Mru == other.Mru;
            public override int GetHashCode() => HashCode.Combine(Targets.Count, Window, Close, FontMode, Mru);
        }
        public sealed record Font(string? Target, string? Window, string? Pane, string Action) : Call;
        public sealed record KeymapReload : Call;
        public sealed record KeymapList : Call;
        public sealed record HooksReload : Call;
        public sealed record HooksList : Call;
        public sealed record KeymapRun(string Name, string? Target, string? Window) : Call;
        public sealed record ConfigReload : Call;
        public sealed record Notify(string? Target, string? Window, string? Title, string Body) : Call;
        public sealed record ThemeSet(string? Name) : Call;
        public sealed record ThemeList : Call;
        public sealed record WorkspaceIcon(string? Target, string? Window, string? IconName) : Call;
        public sealed record IconList : Call;
        public sealed record SidebarVisibility(ControlToggleMode Mode) : Call;
        public sealed record SidebarViewMode(ControlSidebarViewMode Mode) : Call;
        public sealed record FlaggedLayout(ControlFlaggedLayoutMode Mode) : Call;
        public sealed record Expand(string? Window) : Call;
        public sealed record Collapse(string? Window) : Call;
        public sealed record AppVersion : Call;
        public sealed record RecentList : Call;
        public sealed record RecentClear : Call;
        public sealed record RecentOpen(string? Target, string? Window) : Call;
        public sealed record SidebarWidth(double Points, string? Window) : Call;
        public sealed record WorkspaceGo(string? Window, WorkspaceNavigation Direction) : Call;
        public sealed record WindowGo(WorkspaceNavigation Direction) : Call;
        public sealed record Quick(string? Mode) : Call;
        public sealed record QuickType(string Text) : Call;
        public sealed record QuickText(bool All, int? Lines) : Call;
        public sealed record SessionType(string? Target, string? Window, ControlSessionTypeOptions Options) : Call;
        public sealed record SessionCopy(string? Target, string? Window) : Call;
        public sealed record SessionPaste(string? Target, string? Window) : Call;
        public sealed record SessionSelectAll(string? Target, string? Window) : Call;
        public sealed record SessionSearch(string? Target, string? Window, string? Text, string? To) : Call;
        public sealed record OverlayOpen(string? Target, string? Window, ControlSessionOverlayOpenOptions Options) : Call;
        public sealed record OverlayClose(string? Target, string? Window) : Call;
        public sealed record OverlayResize(string? Target, string? Window, int? SizePercent) : Call;
        public sealed record OverlayPointResize(string? Target, string? Window, int? SizePercent, OverlayPointSize Points) : Call;
        public sealed record OverlayResult(string? Target, string? Window) : Call;
    public sealed record OverlayVisibility(string? Target, string? Window, OverlayPane? Pane, OverlayVisibilityMode Mode) : Call;
        public sealed record SessionBackground(string? Target, string? Window, ControlSessionBackgroundOptions Options) : Call;
        public sealed record SessionText(string? Target, string? Window, ControlSessionTextOptions Options) : Call;
        public sealed record WindowNew(string? Name, bool Minimized) : Call;
        public sealed record WindowList : Call;
        public sealed record WindowSelect(string? Target) : Call;
        public sealed record WindowClose(string? Target) : Call;
        public sealed record WindowRename(string? Target, string Name) : Call;
        public sealed record WindowDelete(string? Target) : Call;
        public sealed record WindowResize(string? Target, int Width, int Height) : Call;
        public sealed record WindowMove(string? Target, int X, int Y, int? Display) : Call;
        public sealed record WindowZoom(string? Target) : Call;
        public sealed record WindowFullscreen(string? Target) : Call;
        public sealed record WindowMinimize(string? Target, ControlToggleMode Mode) : Call;
        public sealed record RestoreClear : Call;
        public sealed record PickOpen(PendingPick Pick, string? Window, bool Follow) : Call;
        public sealed record PickResult(string Target, string? Window) : Call;
        public sealed record PickCancel(string Target, string? Window) : Call;
        public sealed record AskOpen(PendingAsk Ask, string? Target, string? Window, ControlAskPlacement Placement, bool Follow) : Call;
        public sealed record AskResult(string Target, string? Window) : Call;
        public sealed record AskCancel(string Target, string? Window) : Call;
        public sealed record HudOpen(string? Target, string? Window, HudSpec Spec, ControlHudPlacement Placement) : Call;
        public sealed record HudUpdate(string? Target, string? Window, HudSpec Spec, ControlHudPlacement Placement) : Call;
        public sealed record HudClose(string? Target, string? Window) : Call;
    }

    public List<Call> Calls { get; } = new();

    /// <summary>The store the workspace.filter / workspace.focus arms drive when set (null = record-only,
    /// the default for every other dispatcher test). It lets a test run the real command path (the
    /// dispatcher's parse plus the host-free ApplyWorkspaceFilter/ApplyFocusMode the app-side arms call)
    /// against a live AppStore. The double supplies only the id-spelling half of target resolution, so a
    /// test driving this store must address workspaces by full id and stay single-window.</summary>
    public AppStore? FocusStore { get; set; }

    /// <summary>Targets that reached a FocusStore-backed arm but could not be resolved, so a "the store
    /// did not change" assertion cannot pass vacuously on a typo'd or sugar-spelled target.</summary>
    public List<string> UnresolvedFocusTargets { get; } = new();

    public ControlResponse NextTreeResponse = new() { Ok = false, Error = "tree not stubbed" };
    public ControlResponse NextEventsReadResponse = new() { Ok = false, Error = "events.read not stubbed" };
    public ControlResponse NextSessionNewResponse = new() { Ok = true };
    public ControlResponse NextSessionDuplicateResponse = new() { Ok = true };
    public ControlResponse NextSidebarVisibilityResponse = new() { Ok = true };
    public ControlResponse NextSidebarViewModeResponse = new() { Ok = true };
    public ControlResponse NextFlaggedLayoutResponse = new() { Ok = true };
    public ControlResponse NextExpandResponse = new() { Ok = true };
    public ControlResponse NextCollapseResponse = new() { Ok = true };
    public ControlResponse NextAppVersionResponse = new() { Ok = true };
    public ControlResponse NextRecentResponse = new() { Ok = true };
    public ControlResponse NextSidebarWidthResponse = new() { Ok = true };
    public ControlResponse NextWorkspaceGoResponse = new() { Ok = true };
    public ControlResponse NextWindowGoResponse = new() { Ok = true };
    public ControlResponse NextFontResponse = new() { Ok = true };
    public ControlResponse NextNotifyResponse = new() { Ok = true };
    public ControlResponse NextKeymapResponse = new() { Ok = true };
    public ControlResponse NextKeymapListResponse = new() { Ok = true };
    public ControlResponse NextHooksReloadResponse = new() { Ok = true };
    public ControlResponse NextHooksListResponse = new() { Ok = true };
    public ControlResponse NextKeymapRunResponse = new() { Ok = true };
    public ControlResponse NextConfigResponse = new() { Ok = true };
    public ControlResponse NextThemeSetResponse = new() { Ok = true };
    public ControlResponse NextThemeListResponse = new() { Ok = true };
    public ControlResponse NextQuickResponse = new() { Ok = true };
    public ControlResponse NextQuickTypeResponse = new() { Ok = true };
    public ControlResponse NextQuickTextResponse = new() { Ok = true };
    public ControlResponse NextSessionTypeResponse = new() { Ok = true };
    public ControlResponse NextSessionCopyResponse = new() { Ok = true };
    public ControlResponse NextSessionPasteResponse = new() { Ok = true };
    public ControlResponse NextSessionSelectAllResponse = new() { Ok = true };
    public ControlResponse NextSessionSearchResponse = new() { Ok = true };
    public ControlResponse NextOverlayOpenResponse = new() { Ok = true };
    public ControlResponse NextOverlayCloseResponse = new() { Ok = true };
    public ControlResponse NextOverlayResizeResponse = new() { Ok = true };
    public ControlResponse NextOverlayResultResponse = new() { Ok = true };
    public ControlResponse NextOverlayVisibilityResponse = new() { Ok = true };
    public ControlResponse NextSessionBackgroundResponse = new() { Ok = true };
    public ControlResponse NextSessionTextResponse = new() { Ok = true };
    public ControlResponse NextSurfaceZoomResponse = new() { Ok = true };
    public ControlResponse NextSurfaceCursorResponse = new() { Ok = true };
    public ControlResponse NextDashboardResponse = new() { Ok = true };
    public ControlResponse NextWindowNewResponse = new() { Ok = true };
    public ControlResponse NextWindowListResponse = new() { Ok = true };
    public ControlResponse NextWindowSelectResponse = new() { Ok = true };
    public ControlResponse NextWindowCloseResponse = new() { Ok = true };
    public ControlResponse NextWindowRenameResponse = new() { Ok = true };
    public ControlResponse NextWindowDeleteResponse = new() { Ok = true };
    public ControlResponse NextWindowResizeResponse = new() { Ok = true };
    public ControlResponse NextWindowMoveResponse = new() { Ok = true };
    public ControlResponse NextWindowZoomResponse = new() { Ok = true };
    public ControlResponse NextWindowFullscreenResponse = new() { Ok = true };
    public ControlResponse NextWindowMinimizeResponse = new() { Ok = true };
    public ControlResponse NextRestoreClearResponse = new() { Ok = true };
    public ControlResponse NextPickOpenResponse = new() { Ok = true };
    public ControlResponse NextPickResultResponse = new() { Ok = true };
    public ControlResponse NextPickCancelResponse = new() { Ok = true };
    public ControlResponse NextAskOpenResponse = new() { Ok = true };
    public ControlResponse NextAskResultResponse = new() { Ok = true };
    public ControlResponse NextAskCancelResponse = new() { Ok = true };
    public ControlResponse NextHudOpenResponse = new() { Ok = true };
    public ControlResponse NextHudUpdateResponse = new() { Ok = true };
    public ControlResponse NextHudCloseResponse = new() { Ok = true };
    public ControlResponse NextSessionRestoreResponse = new() { Ok = true };

    public ControlResponse ControlTree(string? window)
    {
        Calls.Add(new Call.Tree(window));
        return NextTreeResponse;
    }

    public ControlResponse ReadEvents(ControlEventReadOptions options)
    {
        Calls.Add(new Call.EventsRead(options));
        return NextEventsReadResponse;
    }

    public ControlResponse CreateSession(ControlSessionCreateOptions options)
    {
        Calls.Add(new Call.SessionNew(options));
        return NextSessionNewResponse;
    }

    public ControlResponse DuplicateSession(string? target, string? window)
    {
        Calls.Add(new Call.SessionDuplicate(target, window));
        return NextSessionDuplicateResponse;
    }

    public ControlResponse SelectSession(string? target, string? window)
    {
        Calls.Add(new Call.SessionSelect(target, window));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse GoSession(string? window, SessionNavigation direction)
    {
        Calls.Add(new Call.SessionGo(window, direction));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse CloseSession(string? target, string? window, bool undoable = false)
    {
        Calls.Add(new Call.SessionClose(target, window, undoable));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse CloseSessions(IReadOnlyList<string> targets, string? window, bool undoable = false)
    {
        Calls.Add(new Call.SessionCloseBatch(targets, window, undoable));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse RenameSession(string? target, string? window, string name)
    {
        Calls.Add(new Call.SessionRename(target, window, name));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse RevealSession(string? target, string? window)
    {
        Calls.Add(new Call.SessionReveal(target, window));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse CreateWorkspace(string? window, string? name, bool collapsed)
    {
        Calls.Add(new Call.WorkspaceNew(window, name, collapsed));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SelectWorkspace(string? target, string? window)
    {
        Calls.Add(new Call.WorkspaceSelect(target, window));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse RenameWorkspace(string? target, string? window, string name)
    {
        Calls.Add(new Call.WorkspaceRename(target, window, name));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetWorkspaceTheme(string? target, string? window, string? themeName)
    {
        Calls.Add(new Call.WorkspaceTheme(target, window, themeName));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetWorkspaceIcon(string? target, string? window, string? iconName)
    {
        Calls.Add(new Call.WorkspaceIcon(target, window, iconName));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse ListIcons()
    {
        Calls.Add(new Call.IconList());
        return new ControlResponse { Ok = true };
    }

    public ControlResponse DeleteWorkspace(string? target, string? window)
    {
        Calls.Add(new Call.WorkspaceDelete(target, window));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse MoveSession(string? target, string? window, ControlSessionMove move)
    {
        Calls.Add(new Call.SessionMove(target, window, move));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse MoveSessions(IReadOnlyList<string> targets, string? window, ControlSessionMove move)
    {
        Calls.Add(new Call.SessionMoveBatch(targets, window, move));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse MoveWorkspace(string? target, string? window, ReorderDirection direction)
    {
        Calls.Add(new Call.WorkspaceMove(target, window, direction));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse FocusWorkspace(string? target, string? window, ControlWorkspaceFocusMode mode)
    {
        Calls.Add(new Call.WorkspaceFocus(target, window, mode));
        if (FocusStore is { } store)
        {
            if (Guid.TryParse(target, out var id)) store.ApplyFocusMode(mode, id);
            else UnresolvedFocusTargets.Add(target ?? "active");
        }
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetWorkspaceFilter(string? window, ControlToggleMode mode)
    {
        Calls.Add(new Call.WorkspaceFilter(window, mode));
        FocusStore?.ApplyWorkspaceFilter(mode);
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetWorkspaceExpansion(string? target, string? window, bool expanded)
    {
        Calls.Add(new Call.WorkspaceExpansion(target, window, expanded));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetWorkspaceFlag(string? target, string? window, ControlToggleMode mode)
    {
        Calls.Add(new Call.WorkspaceFlag(target, window, mode));
        return new ControlResponse { Ok = true, Result = new ControlResult { Id = target } };
    }

    public ControlResponse SetSessionFlag(string? target, string? window, string? mode)
    {
        Calls.Add(new Call.SessionFlag(target, window, mode));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse MoveFlaggedSession(string? target, string? window, string anchor, FlaggedOrderPlacement placement)
    {
        Calls.Add(new Call.FlaggedMove(target, window, anchor, placement));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetSessionContext(string? target, string? window, string? context)
    {
        Calls.Add(new Call.SessionContext(target, window, context));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetSessionInfo(string? target, string? window, AgTerm.Core.Model.SessionInfo? info)
    {
        Calls.Add(new Call.SessionInfo(target, window, info));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetSessionTint(string? target, string? window, ControlTintUpdate update)
    {
        Calls.Add(new Call.SessionTint(target, window, update));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetWorkspaceTint(string? target, string? window, ControlTintUpdate update)
    {
        Calls.Add(new Call.WorkspaceTint(target, window, update));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse MarkSessionSeen(string? target, string? window)
    {
        Calls.Add(new Call.MarkSessionSeen(target, window));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetSessionStatus(string? target, string? window, ControlSessionStatusUpdate update)
    {
        Calls.Add(new Call.SessionStatus(target, window, update));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetSessionRestore(string? target, string? window, ControlSessionRestoreUpdate update)
    {
        Calls.Add(new Call.SessionRestore(target, window, update));
        return NextSessionRestoreResponse;
    }

    public ControlResponse SplitSession(string? target, string? window, string? mode, SplitAxis? axis = null)
    {
        Calls.Add(new Call.SessionSplit(target, window, mode, axis));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse CloseSessionSplit(string? target, string? window)
    {
        Calls.Add(new Call.SessionSplitClose(target, window));
        return new ControlResponse { Ok = true };
    }

    public Task<ControlResponse> SwapSessionPanes(string? target, string? window)
    {
        Calls.Add(new Call.SessionSwap(target, window));
        return Task.FromResult(new ControlResponse { Ok = true });
    }

    public ControlResponse ScratchSession(string? target, string? window, string? mode, string? command, bool follow = false)
    {
        Calls.Add(new Call.SessionScratch(target, window, mode, command, follow));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse FocusSessionPane(string? target, string? window, string? pane)
    {
        Calls.Add(new Call.SessionFocus(target, window, pane));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse ResizeSplit(string? target, string? window, ControlSplitResize resize)
    {
        Calls.Add(new Call.SessionResize(target, window, resize));
        return new ControlResponse { Ok = true };
    }

    public ControlResponse SetSurfaceZoom(string? target, string? window, ControlToggleMode mode)
    {
        Calls.Add(new Call.SurfaceZoom(target, window, mode));
        return NextSurfaceZoomResponse;
    }

    public ControlResponse ReadSurfaceCursor(string? target, string? window, string? paneId)
    {
        Calls.Add(new Call.SurfaceCursor(target, window, paneId));
        return NextSurfaceCursorResponse;
    }

    public ControlResponse SetDashboard(IReadOnlyList<string> targets, string? window, bool close,
                                        DashboardFontMode fontMode, bool mru)
    {
        Calls.Add(new Call.Dashboard(targets, window, close, fontMode, mru));
        return NextDashboardResponse;
    }

    public ControlResponse Font(string? target, string? window, string? pane, string action)
    {
        Calls.Add(new Call.Font(target, window, pane, action));
        return NextFontResponse;
    }

    public ControlResponse ListKeymap()
    {
        Calls.Add(new Call.KeymapList());
        return NextKeymapListResponse;
    }

    public Task<ControlResponse> RunCustomCommand(string name, string? target, string? window)
    {
        Calls.Add(new Call.KeymapRun(name, target, window));
        return Task.FromResult(NextKeymapRunResponse);
    }

    public ControlResponse ReloadKeymap()
    {
        Calls.Add(new Call.KeymapReload());
        return NextKeymapResponse;
    }

    public ControlResponse ReloadHooks()
    {
        Calls.Add(new Call.HooksReload());
        return NextHooksReloadResponse;
    }

    public ControlResponse ListHooks()
    {
        Calls.Add(new Call.HooksList());
        return NextHooksListResponse;
    }

    public ControlResponse ReloadGhosttyConfig()
    {
        Calls.Add(new Call.ConfigReload());
        return NextConfigResponse;
    }

    public ControlResponse SendNotification(string? target, string? window, string? title, string body)
    {
        Calls.Add(new Call.Notify(target, window, title, body));
        return NextNotifyResponse;
    }

    public ControlResponse SetTheme(ControlArgs? args)
    {
        Calls.Add(new Call.ThemeSet(args?.Name));
        return NextThemeSetResponse;
    }

    public ControlResponse ListThemes()
    {
        Calls.Add(new Call.ThemeList());
        return NextThemeListResponse;
    }

    public ControlResponse SetSidebarVisibility(ControlToggleMode mode)
    {
        Calls.Add(new Call.SidebarVisibility(mode));
        return NextSidebarVisibilityResponse;
    }

    public ControlResponse SetSidebarViewMode(ControlSidebarViewMode mode)
    {
        Calls.Add(new Call.SidebarViewMode(mode));
        return NextSidebarViewModeResponse;
    }

    public ControlResponse SetFlaggedViewLayout(ControlFlaggedLayoutMode mode)
    {
        Calls.Add(new Call.FlaggedLayout(mode));
        return NextFlaggedLayoutResponse;
    }

    public ControlResponse ExpandSidebar(string? window)
    {
        Calls.Add(new Call.Expand(window));
        return NextExpandResponse;
    }

    public ControlResponse CollapseSidebar(string? window)
    {
        Calls.Add(new Call.Collapse(window));
        return NextCollapseResponse;
    }

    public ControlResponse AppVersion()
    {
        Calls.Add(new Call.AppVersion());
        return NextAppVersionResponse;
    }

    public ControlResponse ListRecentClosed()
    {
        Calls.Add(new Call.RecentList());
        return NextRecentResponse;
    }

    public ControlResponse ClearRecentClosed()
    {
        Calls.Add(new Call.RecentClear());
        return NextRecentResponse;
    }

    public ControlResponse OpenRecentClosed(string? target, string? window)
    {
        Calls.Add(new Call.RecentOpen(target, window));
        return NextRecentResponse;
    }

    public ControlResponse SetSidebarWidth(double points, string? window)
    {
        Calls.Add(new Call.SidebarWidth(points, window));
        return NextSidebarWidthResponse;
    }

    public ControlResponse GoWorkspace(string? window, WorkspaceNavigation direction)
    {
        Calls.Add(new Call.WorkspaceGo(window, direction));
        return NextWorkspaceGoResponse;
    }

    public Task<ControlResponse> WindowGo(WorkspaceNavigation direction)
    {
        Calls.Add(new Call.WindowGo(direction));
        return Task.FromResult(NextWindowGoResponse);
    }

    public ControlResponse SetQuickTerminal(string? mode)
    {
        Calls.Add(new Call.Quick(mode));
        return NextQuickResponse;
    }

    public Task<ControlResponse> TypeQuick(string text)
    {
        Calls.Add(new Call.QuickType(text));
        return Task.FromResult(NextQuickTypeResponse);
    }

    public Task<ControlResponse> ReadQuickText(bool all, int? lines)
    {
        Calls.Add(new Call.QuickText(all, lines));
        return Task.FromResult(NextQuickTextResponse);
    }

    public Task<ControlResponse> TypeSession(string? target, string? window, ControlSessionTypeOptions options)
    {
        Calls.Add(new Call.SessionType(target, window, options));
        return Task.FromResult(NextSessionTypeResponse);
    }

    public ControlResponse CopySessionSelection(string? target, string? window)
    {
        Calls.Add(new Call.SessionCopy(target, window));
        return NextSessionCopyResponse;
    }

    public ControlResponse PasteSession(string? target, string? window)
    {
        Calls.Add(new Call.SessionPaste(target, window));
        return NextSessionPasteResponse;
    }

    public ControlResponse SelectAllSession(string? target, string? window)
    {
        Calls.Add(new Call.SessionSelectAll(target, window));
        return NextSessionSelectAllResponse;
    }

    public Task<ControlResponse> SearchSession(string? target, string? window, string? text, string? to)
    {
        Calls.Add(new Call.SessionSearch(target, window, text, to));
        return Task.FromResult(NextSessionSearchResponse);
    }

    public ControlResponse OpenSessionOverlay(string? target, string? window, ControlSessionOverlayOpenOptions options)
    {
        Calls.Add(new Call.OverlayOpen(target, window, options));
        return NextOverlayOpenResponse;
    }

    public ControlResponse CloseSessionOverlay(string? target, string? window)
    {
        Calls.Add(new Call.OverlayClose(target, window));
        return NextOverlayCloseResponse;
    }

    public ControlResponse ResizeSessionOverlay(string? target, string? window, int? sizePercent)
    {
        Calls.Add(new Call.OverlayResize(target, window, sizePercent));
        return NextOverlayResizeResponse;
    }

    public ControlResponse ResizeSessionOverlay(string? target, string? window, int? sizePercent, OverlayPointSize points)
    {
        Calls.Add(new Call.OverlayPointResize(target, window, sizePercent, points));
        return NextOverlayResizeResponse;
    }

    public ControlResponse SessionOverlayResult(string? target, string? window)
    {
        Calls.Add(new Call.OverlayResult(target, window));
        return NextOverlayResultResponse;
    }

    public ControlResponse SetSessionOverlayVisibility(string? target, string? window, OverlayPane? pane, OverlayVisibilityMode mode)
    {
        Calls.Add(new Call.OverlayVisibility(target, window, pane, mode));
        return NextOverlayVisibilityResponse;
    }

    public ControlResponse SetSessionBackground(string? target, string? window, ControlSessionBackgroundOptions options)
    {
        Calls.Add(new Call.SessionBackground(target, window, options));
        return NextSessionBackgroundResponse;
    }

    public ControlResponse ReadSessionText(string? target, string? window, ControlSessionTextOptions options)
    {
        Calls.Add(new Call.SessionText(target, window, options));
        return NextSessionTextResponse;
    }

    public ControlResponse WindowNew(string? name, bool minimized)
    {
        Calls.Add(new Call.WindowNew(name, minimized));
        return NextWindowNewResponse;
    }

    public ControlResponse WindowList()
    {
        Calls.Add(new Call.WindowList());
        return NextWindowListResponse;
    }

    public Task<ControlResponse> WindowSelect(string? target)
    {
        Calls.Add(new Call.WindowSelect(target));
        return Task.FromResult(NextWindowSelectResponse);
    }

    public Task<ControlResponse> WindowClose(string? target)
    {
        Calls.Add(new Call.WindowClose(target));
        return Task.FromResult(NextWindowCloseResponse);
    }

    public ControlResponse WindowRename(string? target, string name)
    {
        Calls.Add(new Call.WindowRename(target, name));
        return NextWindowRenameResponse;
    }

    public ControlResponse WindowDelete(string? target)
    {
        Calls.Add(new Call.WindowDelete(target));
        return NextWindowDeleteResponse;
    }

    public ControlResponse WindowResize(string? target, int width, int height)
    {
        Calls.Add(new Call.WindowResize(target, width, height));
        return NextWindowResizeResponse;
    }

    public ControlResponse WindowMove(string? target, int x, int y, int? display)
    {
        Calls.Add(new Call.WindowMove(target, x, y, display));
        return NextWindowMoveResponse;
    }

    public ControlResponse WindowZoom(string? target)
    {
        Calls.Add(new Call.WindowZoom(target));
        return NextWindowZoomResponse;
    }

    public ControlResponse WindowFullscreen(string? target)
    {
        Calls.Add(new Call.WindowFullscreen(target));
        return NextWindowFullscreenResponse;
    }

    public ControlResponse WindowMinimize(string? target, ControlToggleMode mode)
    {
        Calls.Add(new Call.WindowMinimize(target, mode));
        return NextWindowMinimizeResponse;
    }

    public ControlResponse ClearRestoreCommands()
    {
        Calls.Add(new Call.RestoreClear());
        return NextRestoreClearResponse;
    }

    public ControlResponse OpenPick(PendingPick pick, string? window, bool follow)
    {
        Calls.Add(new Call.PickOpen(pick, window, follow));
        return NextPickOpenResponse;
    }

    public ControlResponse PickResult(string target, string? window)
    {
        Calls.Add(new Call.PickResult(target, window));
        return NextPickResultResponse;
    }

    public ControlResponse CancelPick(string target, string? window)
    {
        Calls.Add(new Call.PickCancel(target, window));
        return NextPickCancelResponse;
    }

    public ControlResponse OpenAsk(PendingAsk ask, string? target, string? window, ControlAskPlacement placement, bool follow)
    {
        Calls.Add(new Call.AskOpen(ask, target, window, placement, follow));
        return NextAskOpenResponse;
    }

    public ControlResponse AskResult(string target, string? window)
    {
        Calls.Add(new Call.AskResult(target, window));
        return NextAskResultResponse;
    }

    public ControlResponse CancelAsk(string target, string? window)
    {
        Calls.Add(new Call.AskCancel(target, window));
        return NextAskCancelResponse;
    }

    public ControlResponse OpenHud(string? target, string? window, HudSpec spec, ControlHudPlacement placement)
    {
        Calls.Add(new Call.HudOpen(target, window, spec, placement));
        return NextHudOpenResponse;
    }

    public ControlResponse UpdateHud(string? target, string? window, HudSpec spec, ControlHudPlacement placement)
    {
        Calls.Add(new Call.HudUpdate(target, window, spec, placement));
        return NextHudUpdateResponse;
    }

    public ControlResponse CloseHud(string? target, string? window)
    {
        Calls.Add(new Call.HudClose(target, window));
        return NextHudCloseResponse;
    }

    public sealed record Queued(string Id, string? From, IReadOnlyList<MessageRecipient> Recipients)
    {
        public bool Equals(Queued? other) =>
            other is not null && Id == other.Id && From == other.From && Recipients.SequenceEqual(other.Recipients);
        public override int GetHashCode() => HashCode.Combine(Id, From, Recipients.Count);
    }

    public sealed record Scope(string? Workspace, string? Window);

    public Dictionary<MessageAddress, MessageRecipient> MessagePaneMap { get; } = new();
    public List<MessageRecipient> BroadcastPanes { get; } = new();
    public List<Queued> QueuedMessages { get; } = new();
    public List<Scope> BroadcastScopes { get; } = new();

    /// <summary>Answers what the IControlActions default answers, for a host without the bus.</summary>
    public bool MessagesUnsupported { get; set; }

    public MessagePaneResult<MessageRecipient> ResolveMessagePane(MessageAddress address)
    {
        if (MessagesUnsupported) return MessagePaneError.Unsupported;
        return MessagePaneMap.TryGetValue(address, out var recipient)
            ? MessagePaneResult<MessageRecipient>.Ok(recipient)
            : new MessagePaneError($"pane not found: {address.Session}:{address.Pane.Text}");
    }

    public MessagePaneResult<IReadOnlyList<MessageRecipient>> MessagePanes(string? workspace, string? window)
    {
        BroadcastScopes.Add(new Scope(workspace, window));
        if (MessagesUnsupported) return MessagePaneError.Unsupported;
        return MessagePaneResult<IReadOnlyList<MessageRecipient>>.Ok(BroadcastPanes.ToArray());
    }

    public void MessageQueued(Guid messageId, string? from, IReadOnlyList<MessageRecipient> recipients) =>
        QueuedMessages.Add(new Queued(messageId.ToString("D").ToUpperInvariant(), from, recipients));
}
