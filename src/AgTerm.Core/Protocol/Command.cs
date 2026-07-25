using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgTerm.Core.Protocol;

/// <summary>
/// A control command name, the <c>cmd</c> field of a <see cref="ControlRequest"/>. Wire strings are
/// the ones the CLI and the control server agree on (parity pin: agterm 7df70fe, 66 commands). An
/// unknown string fails to decode, which the server turns into an "unknown command" error rather
/// than a crash.
/// </summary>
[JsonConverter(typeof(CommandJsonConverter))]
public enum Command
{
    Tree,
    EventsRead,
    WorkspaceNew,
    WorkspaceRename,
    WorkspaceDelete,
    WorkspaceSelect,
    SessionNew,
    SessionDuplicate,
    SessionClose,
    SessionSelect,
    SessionGo,
    SessionRename,
    SessionReveal,
    SessionMove,
    WorkspaceMove,
    WorkspaceFocus,
    WorkspaceCollapse,
    WorkspaceExpand,
    SessionType,
    SessionStatus,
    SessionFlag,
    SessionSeen,
    SessionRestore,
    SessionBackground,
    SessionSplit,
    SessionScratch,
    SessionFocus,
    SessionResize,
    SurfaceZoom,
    Dashboard,
    SessionCopy,
    SessionPaste,
    SessionSelectAll,
    SessionText,
    SessionSearch,
    SessionOverlayOpen,
    SessionOverlayClose,
    SessionOverlayResize,
    SessionOverlayResult,
    Quick,
    QuickType,
    QuickText,
    Sidebar,
    SidebarMode,
    SidebarExpand,
    SidebarCollapse,
    Notify,
    FontInc,
    FontDec,
    FontReset,
    WindowNew,
    WindowList,
    WindowSelect,
    WindowClose,
    WindowRename,
    WindowDelete,
    WindowResize,
    WindowMove,
    WindowZoom,
    WindowFullscreen,
    KeymapReload,
    ConfigReload,
    ThemeSet,
    ThemeList,
    RestoreClear,
    /// <summary>
    /// UI-TEST-ONLY upstream (XCUITest appearance flip). Kept in the enum so the wire string decodes,
    /// but NOT implemented in the Windows port — recorded as an intentional skip in the parity manifest.
    /// </summary>
    DebugAppearance,
}

/// <summary>Two-way map between <see cref="Command"/> and its wire string.</summary>
public static class CommandWire
{
    private static readonly (Command Command, string Wire)[] Map =
    {
        (Command.Tree, "tree"),
        (Command.EventsRead, "events.read"),
        (Command.WorkspaceNew, "workspace.new"),
        (Command.WorkspaceRename, "workspace.rename"),
        (Command.WorkspaceDelete, "workspace.delete"),
        (Command.WorkspaceSelect, "workspace.select"),
        (Command.SessionNew, "session.new"),
        (Command.SessionDuplicate, "session.duplicate"),
        (Command.SessionClose, "session.close"),
        (Command.SessionSelect, "session.select"),
        (Command.SessionGo, "session.go"),
        (Command.SessionRename, "session.rename"),
        (Command.SessionReveal, "session.reveal"),
        (Command.SessionMove, "session.move"),
        (Command.WorkspaceMove, "workspace.move"),
        (Command.WorkspaceFocus, "workspace.focus"),
        (Command.WorkspaceCollapse, "workspace.collapse"),
        (Command.WorkspaceExpand, "workspace.expand"),
        (Command.SessionType, "session.type"),
        (Command.SessionStatus, "session.status"),
        (Command.SessionFlag, "session.flag"),
        (Command.SessionSeen, "session.seen"),
        (Command.SessionRestore, "session.restore"),
        (Command.SessionBackground, "session.background"),
        (Command.SessionSplit, "session.split"),
        (Command.SessionScratch, "session.scratch"),
        (Command.SessionFocus, "session.focus"),
        (Command.SessionResize, "session.resize"),
        (Command.SurfaceZoom, "surface.zoom"),
        (Command.Dashboard, "dashboard"),
        (Command.SessionCopy, "session.copy"),
        (Command.SessionPaste, "session.paste"),
        (Command.SessionSelectAll, "session.selectall"),
        (Command.SessionText, "session.text"),
        (Command.SessionSearch, "session.search"),
        (Command.SessionOverlayOpen, "session.overlay.open"),
        (Command.SessionOverlayClose, "session.overlay.close"),
        (Command.SessionOverlayResize, "session.overlay.resize"),
        (Command.SessionOverlayResult, "session.overlay.result"),
        (Command.Quick, "quick"),
        (Command.QuickType, "quick.type"),
        (Command.QuickText, "quick.text"),
        (Command.Sidebar, "sidebar"),
        (Command.SidebarMode, "sidebar.mode"),
        (Command.SidebarExpand, "sidebar.expand"),
        (Command.SidebarCollapse, "sidebar.collapse"),
        (Command.Notify, "notify"),
        (Command.FontInc, "font.inc"),
        (Command.FontDec, "font.dec"),
        (Command.FontReset, "font.reset"),
        (Command.WindowNew, "window.new"),
        (Command.WindowList, "window.list"),
        (Command.WindowSelect, "window.select"),
        (Command.WindowClose, "window.close"),
        (Command.WindowRename, "window.rename"),
        (Command.WindowDelete, "window.delete"),
        (Command.WindowResize, "window.resize"),
        (Command.WindowMove, "window.move"),
        (Command.WindowZoom, "window.zoom"),
        (Command.WindowFullscreen, "window.fullscreen"),
        (Command.KeymapReload, "keymap.reload"),
        (Command.ConfigReload, "config.reload"),
        (Command.ThemeSet, "theme.set"),
        (Command.ThemeList, "theme.list"),
        (Command.RestoreClear, "restore.clear"),
        (Command.DebugAppearance, "debug.appearance"),
    };

    private static readonly Dictionary<Command, string> ToWireMap = Map.ToDictionary(p => p.Command, p => p.Wire);
    private static readonly Dictionary<string, Command> FromWireMap =
        Map.ToDictionary(p => p.Wire, p => p.Command, StringComparer.Ordinal);

    public static string ToWire(this Command command) => ToWireMap[command];

    public static bool TryParse(string wire, out Command command) => FromWireMap.TryGetValue(wire, out command);
}

/// <summary>Encodes a <see cref="Command"/> as its wire string; an unknown string throws.</summary>
public sealed class CommandJsonConverter : JsonConverter<Command>
{
    public override Command Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = reader.GetString();
        if (wire is null || !CommandWire.TryParse(wire, out var command))
            throw new JsonException($"unknown command: {wire}");
        return command;
    }

    public override void Write(Utf8JsonWriter writer, Command value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToWire());
}
