using AgTerm.Core.Keymap;
using AgTerm.Xterm;

namespace AgTerm.App;

// upstream AppActions.uiActionsEnabled at 7df70fe. Terminal zoom and the open dashboard disable the
// window's UI actions. The control API is not gated.
public static class ModalActionGate
{
    public static bool UiActionsEnabled(bool zoomActive, bool dashboardOpen) => !zoomActive && !dashboardOpen;

    public static bool Allows(BuiltinAction action, bool zoomActive, bool dashboardOpen) => action switch
    {
        BuiltinAction.CloseSession or
        BuiltinAction.ToggleTerminalZoom or
        BuiltinAction.ToggleFullscreen or
        BuiltinAction.IncreaseFontSize or
        BuiltinAction.DecreaseFontSize or
        BuiltinAction.ResetFontSize => true,
        BuiltinAction.Dashboard => !zoomActive,
        _ => UiActionsEnabled(zoomActive, dashboardOpen),
    };

    // a chord whose action is disabled in zoom reaches the terminal instead (upstream menu items are
    // disabled, so AppKit hands the key to the terminal view)
    public static bool PassesThroughInZoom(BuiltinAction action) =>
        !Allows(action, zoomActive: true, dashboardOpen: false);

    // the dashboard page swallows plain keys, but a chord whose action stays enabled on the dashboard
    // reaches the window (upstream menu key equivalents run before the key catcher's keyDown)
    public static bool PassesThroughOnDashboard(BuiltinAction action) =>
        Allows(action, zoomActive: false, dashboardOpen: true);

    // the published chords the dashboard page hands to the window: an enabled action's chord and the
    // Settings chord. A custom command's chord stays swallowed (docs/PLATFORM-NOTES.md).
    public static IReadOnlyList<PhysicalChord> DashboardPassthroughChords(
        HotkeyRouter router, Keymap keymap, HotkeyManifest manifest) =>
        manifest.BuiltinSingles
            .Where(chord => SettingsShortcuts.Opens(keymap, chord) ||
                router.Handle(chord) is { } action && PassesThroughOnDashboard(action))
            .ToArray();
}

public enum ModalChromeMode
{
    None,
    Zoom,
    Dashboard,
}

public sealed record ModalChromePresentation(
    bool TitleBarActionsVisible,
    bool PaneToggleVisible,
    bool ZoomExitVisible,
    bool DashboardExitVisible,
    bool SidebarPaneOpen);

// upstream WindowContentView: zoomTitlebar and dashboardTitlebar replace the normal title bar, and the
// zoom layer and the dashboard cover the sidebar. On Windows the title bar row stays for the caption
// buttons and dragging; only its content changes.
public static class ModalWindowChrome
{
    public static ModalChromeMode ModeOf(TerminalLayout layout) => layout switch
    {
        TerminalLayout.Zoomed => ModalChromeMode.Zoom,
        TerminalLayout.Dashboard => ModalChromeMode.Dashboard,
        _ => ModalChromeMode.None,
    };

    public static ModalChromePresentation Present(ModalChromeMode mode, bool sidebarToggleShown, bool sidebarVisible) =>
        mode switch
        {
            ModalChromeMode.Zoom => new(false, false, true, false, false),
            ModalChromeMode.Dashboard => new(false, false, false, true, false),
            _ => new(true, sidebarToggleShown, false, false, sidebarVisible),
        };
}
