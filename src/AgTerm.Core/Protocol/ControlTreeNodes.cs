using System.Text.Json.Serialization;
using AgTerm.Core.Model;

namespace AgTerm.Core.Protocol;

/// <summary>
/// A terminal surface as projected into the tree response. Id is the stable control address for
/// surface.zoom; Kind is the user-facing surface name (left, right, scratch, overlay).
/// </summary>
public sealed record ControlSurfaceNode
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }

    [JsonPropertyName("visible")]
    public bool Visible { get; init; }
}

/// <summary>A session as projected into the tree response. Field order mirrors the Swift struct.</summary>
public sealed record ControlSessionNode
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The derived sidebar label (displayName), not the raw OSC title.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("cwd")]
    public required string Cwd { get; init; }

    /// <summary>The raw terminal title from the latest OSC 0/1/2; null when none was reported.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }

    [JsonPropertyName("split")]
    public bool Split { get; init; }

    /// <summary>The left-pane fraction (0.05..0.95) of a session that HAS a split; null when no split
    /// or never explicitly set (divider at the default 0.5).</summary>
    [JsonPropertyName("splitRatio")]
    public double? SplitRatio { get; init; }

    /// <summary>For a session that HAS a split: true = the split (right) pane holds focus; null when
    /// the session has no split.</summary>
    [JsonPropertyName("splitFocused")]
    public bool? SplitFocused { get; init; }

    [JsonPropertyName("overlay")]
    public bool Overlay { get; init; }

    /// <summary>For an OPEN overlay: null = full-pane, else the floating panel's percent (1..100).</summary>
    [JsonPropertyName("overlaySizePercent")]
    public int? OverlaySizePercent { get; init; }

    /// <summary>The message panel in the session's overlay slot; overlay reads false beside it.</summary>
    [JsonPropertyName("hud")]
    public ControlHudNode? Hud { get; init; }

    /// <summary>The pending terminal question; GUI questions are reported at the tree's top level.</summary>
    [JsonPropertyName("ask")]
    public ControlSessionAsk? Ask { get; init; }

    [JsonPropertyName("scratch")]
    public bool Scratch { get; init; }

    [JsonPropertyName("flagged")]
    public bool Flagged { get; init; }

    /// <summary>Whether a --command session holds its surface after the command exits.</summary>
    [JsonPropertyName("commandWait")]
    public bool? CommandWait { get; init; }

    /// <summary>The main pane's live foreground argv; null at the shell prompt.</summary>
    [JsonPropertyName("foreground")]
    public string[]? Foreground { get; init; }

    /// <summary>The split (right) pane's live foreground argv.</summary>
    [JsonPropertyName("splitForeground")]
    public string[]? SplitForeground { get; init; }

    /// <summary>The main pane's persisted restore override. Tri-state: null = auto-capture,
    /// "" = pinned to nothing, a command = that shell line runs on next launch.</summary>
    [JsonPropertyName("restoreCommand")]
    public string? RestoreCommand { get; init; }

    [JsonPropertyName("splitRestoreCommand")]
    public string? SplitRestoreCommand { get; init; }

    /// <summary>The agent status (active/completed/blocked); null when idle.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>Which pane set the status (left|right|scratch); null when idle or unspecified.</summary>
    [JsonPropertyName("statusPane")]
    public string? StatusPane { get; init; }

    [JsonPropertyName("statusBlink")]
    public bool? StatusBlink { get; init; }

    /// <summary>Per-call #rrggbb glyph tint; null = the settings color.</summary>
    [JsonPropertyName("statusColor")]
    public string? StatusColor { get; init; }

    /// <summary>Per-call glyph silhouette; null = the settings shape / plain circle.</summary>
    [JsonPropertyName("statusShape")]
    public string? StatusShape { get; init; }

    /// <summary>The background watermark spec; null when none is set.</summary>
    [JsonPropertyName("background")]
    public BackgroundWatermark? Background { get; init; }

    /// <summary>Unseen-notification badge count; null when zero. Ephemeral.</summary>
    [JsonPropertyName("unseen")]
    public int? Unseen { get; init; }

    /// <summary>The default/left pane's live font size in points; null when not realized.</summary>
    [JsonPropertyName("fontSize")]
    public double? FontSize { get; init; }

    [JsonPropertyName("splitFontSize")]
    public double? SplitFontSize { get; init; }

    [JsonPropertyName("scratchFontSize")]
    public double? ScratchFontSize { get; init; }

    /// <summary>Addressable terminal surfaces owned by this session (hidden-but-alive included).</summary>
    [JsonPropertyName("surfaces")]
    public ControlSurfaceNode[]? Surfaces { get; init; }
}

/// <summary>A workspace and its sessions as projected into the tree response.</summary>
public sealed record ControlWorkspaceNode
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; }

    /// <summary>Whether the sidebar tree is FOCUSED to this workspace; null when not.</summary>
    [JsonPropertyName("focused")]
    public bool? Focused { get; init; }

    /// <summary>Whether this workspace is collapsed in the sidebar; null when expanded (the default).</summary>
    [JsonPropertyName("collapsed")]
    public bool? Collapsed { get; init; }

    [JsonPropertyName("sessions")]
    public required ControlSessionNode[] Sessions { get; init; }

    /// <summary>The workspace's own terminal theme as stored, also when the theme catalog lacks
    /// it; null when it has none. Windows-only (parity/windows-only.json); written last so the
    /// upstream fields keep upstream order.</summary>
    [JsonPropertyName("theme")]
    public string? Theme { get; init; }
}

/// <summary>The whole workspace tree, the payload of a tree response.</summary>
public sealed record ControlTree
{
    [JsonPropertyName("workspaces")]
    public required ControlWorkspaceNode[] Workspaces { get; init; }

    /// <summary>Milliseconds since the last user input in the projected window; null before any activity.</summary>
    [JsonPropertyName("idleMs")]
    public int? IdleMs { get; init; }

    /// <summary>The window's auto-follow-blocked timeout in ms; null when disabled.</summary>
    [JsonPropertyName("autoFollowMs")]
    public int? AutoFollowMs { get; init; }

    /// <summary>Whether the projected window's sidebar is visible (live).</summary>
    [JsonPropertyName("sidebarVisible")]
    public bool? SidebarVisible { get; init; }

    /// <summary>The sidebar view mode (tree | flagged), live.</summary>
    [JsonPropertyName("sidebarMode")]
    public string? SidebarMode { get; init; }

    /// <summary>Whether the projected window's quick terminal is visible (live).</summary>
    [JsonPropertyName("quickVisible")]
    public bool? QuickVisible { get; init; }

    /// <summary>The control id of the zoomed surface (surface:&lt;session-id&gt;:&lt;kind&gt; or quick);
    /// null when nothing is zoomed.</summary>
    [JsonPropertyName("zoomedSurface")]
    public string? ZoomedSurface { get; init; }

    /// <summary>Pane refs (&lt;uuid&gt;:left|right, grid order) of the open dashboard's cells.</summary>
    [JsonPropertyName("dashboardMembers")]
    public string[]? DashboardMembers { get; init; }

    /// <summary>Pane ref of the dashboard's highlighted cell.</summary>
    [JsonPropertyName("dashboardHighlighted")]
    public string? DashboardHighlighted { get; init; }

    /// <summary>Absolute font size applied to dashboard cells; null when closed or untouched.</summary>
    [JsonPropertyName("dashboardFontSize")]
    public double? DashboardFontSize { get; init; }

    /// <summary>The dashboard's font mode: auto | fixed | untouched; null when no dashboard is open.</summary>
    [JsonPropertyName("dashboardFontMode")]
    public string? DashboardFontMode { get; init; }

    /// <summary>The pending pick id of the projected window; null when none.</summary>
    [JsonPropertyName("pickPending")]
    public string? PickPending { get; init; }

    /// <summary>The pending GUI question id of the projected window; null when none.</summary>
    [JsonPropertyName("askPending")]
    public string? AskPending { get; init; }
}

/// <summary>
/// An open window's on-screen frame, in the SAME coordinate system window.move/window.resize accept:
/// x/y are the top-left relative to display's top-left (y down), width/height in points, display the
/// index into the screen list.
/// </summary>
public sealed record ControlWindowFrame
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("display")]
    public int Display { get; init; }
}

/// <summary>A window as projected into the window.list response.</summary>
public sealed record ControlWindowNode
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Whether its on-screen window is up.</summary>
    [JsonPropertyName("open")]
    public bool Open { get; init; }

    /// <summary>Whether it is the frontmost window.</summary>
    [JsonPropertyName("active")]
    public bool Active { get; init; }

    /// <summary>Auto-follow-blocked timeout in ms; null when disabled.</summary>
    [JsonPropertyName("autoFollowMs")]
    public int? AutoFollowMs { get; init; }

    /// <summary>Sidebar visibility; null for a closed window with no live store.</summary>
    [JsonPropertyName("sidebarVisible")]
    public bool? SidebarVisible { get; init; }

    /// <summary>The current on-screen frame; null for a closed window.</summary>
    [JsonPropertyName("geometry")]
    public ControlWindowFrame? Geometry { get; init; }

    /// <summary>Native fullscreen state; null for a closed window.</summary>
    [JsonPropertyName("fullscreen")]
    public bool? Fullscreen { get; init; }

    /// <summary>Zoomed (maximized-to-screen) state; null for a closed window.</summary>
    [JsonPropertyName("zoomed")]
    public bool? Zoomed { get; init; }
}
