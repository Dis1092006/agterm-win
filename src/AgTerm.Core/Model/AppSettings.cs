using System.Text.Json.Serialization;

namespace AgTerm.Core.Model;

/// <summary>
/// User-facing appearance settings, persisted independently of the workspace tree (spec §8; port
/// of agtermCore AppSettings.swift, field list pinned by parity/surface.json#settings).
///
/// Every field is optional: null means "use the engine default", and a settings file written
/// before a field existed still decodes — that optionality IS the forward-compat mechanism, so
/// there is no version field (a version bump would only add a discard-on-mismatch path that wipes
/// the user's settings). Raw-string enum fields (toolbarMode, dockBounce, the status shapes,
/// newSessionDirectory, autoFollowAttention, hiddenInterfaceElements) decode tolerantly: an
/// unknown future value resolves to the default at the effective* read point instead of failing
/// the whole decode.
/// </summary>
public sealed record AppSettings
{
    /// <summary>The app's out-of-the-box theme — seeded by SettingsStore.Load() on a fresh install
    /// (no saved settings). Distinct from Theme == null, which means the engine's built-in default.</summary>
    public const string DefaultTheme = "agterm";

    public const int DefaultInactivePaneMuteStrength = 5;
    public const int DefaultSidebarBackgroundShift = 5;
    public const double DefaultSidebarFontSize = 13;
    public const double SidebarFontSizeMin = 9;
    public const double SidebarFontSizeMax = 20;

    /// <summary>The built-in status glyph tints: active = muted lavender-grey; blocked/completed
    /// pin the macOS systemOrange/systemGreen light-appearance values (the Windows port has no
    /// dynamic system colors — documented adaptation).</summary>
    public const string DefaultActiveStatusColorHex = "#DBD9E6";
    public const string DefaultBlockedStatusColorHex = "#FF9500";
    public const string DefaultCompletedStatusColorHex = "#28CD41";

    [JsonPropertyName("fontFamily")] public string? FontFamily { get; init; }
    [JsonPropertyName("fontSize")] public double? FontSize { get; init; }
    [JsonPropertyName("theme")] public string? Theme { get; init; }
    [JsonPropertyName("darkTheme")] public string? DarkTheme { get; init; }
    [JsonPropertyName("followSystemAppearance")] public bool? FollowSystemAppearance { get; init; }
    [JsonPropertyName("backgroundOpacity")] public double? BackgroundOpacity { get; init; }
    [JsonPropertyName("backgroundBlur")] public int? BackgroundBlur { get; init; }
    [JsonPropertyName("notificationsEnabled")] public bool? NotificationsEnabled { get; init; }
    [JsonPropertyName("notificationBadgeEnabled")] public bool? NotificationBadgeEnabled { get; init; }
    [JsonPropertyName("toolbarMode")] public string? ToolbarMode { get; init; }
    [JsonPropertyName("compactToolbar")] public bool? CompactToolbar { get; init; }
    [JsonPropertyName("activeStatusColorHex")] public string? ActiveStatusColorHex { get; init; }
    [JsonPropertyName("blockedStatusColorHex")] public string? BlockedStatusColorHex { get; init; }
    [JsonPropertyName("completedStatusColorHex")] public string? CompletedStatusColorHex { get; init; }
    [JsonPropertyName("activeStatusShape")] public string? ActiveStatusShape { get; init; }
    [JsonPropertyName("blockedStatusShape")] public string? BlockedStatusShape { get; init; }
    [JsonPropertyName("completedStatusShape")] public string? CompletedStatusShape { get; init; }
    [JsonPropertyName("configDirectory")] public string? ConfigDirectory { get; init; }
    [JsonPropertyName("mouseScrollMultiplier")] public double? MouseScrollMultiplier { get; init; }
    [JsonPropertyName("inactivePaneMuteStrength")] public int? InactivePaneMuteStrength { get; init; }
    [JsonPropertyName("sidebarBackgroundShift")] public int? SidebarBackgroundShift { get; init; }
    [JsonPropertyName("restoreRunningCommand")] public bool? RestoreRunningCommand { get; init; }
    [JsonPropertyName("inheritGlobalGhosttyConfig")] public bool? InheritGlobalGhosttyConfig { get; init; }
    [JsonPropertyName("attentionButtonEnabled")] public bool? AttentionButtonEnabled { get; init; }
    [JsonPropertyName("dockBounce")] public string? DockBounce { get; init; }
    [JsonPropertyName("notificationSoundName")] public string? NotificationSoundName { get; init; }
    [JsonPropertyName("blockedStatusSoundName")] public string? BlockedStatusSoundName { get; init; }
    [JsonPropertyName("rightClickPaste")] public bool? RightClickPaste { get; init; }
    [JsonPropertyName("newSessionDirectory")] public string? NewSessionDirectory { get; init; }
    [JsonPropertyName("newSessionCustomDirectory")] public string? NewSessionCustomDirectory { get; init; }
    [JsonPropertyName("confirmCloseSession")] public bool? ConfirmCloseSession { get; init; }
    [JsonPropertyName("closeGraceUndoEnabled")] public bool? CloseGraceUndoEnabled { get; init; }
    [JsonPropertyName("autoFollowAttention")] public string? AutoFollowAttention { get; init; }
    [JsonPropertyName("autoFollowStayOnActive")] public bool? AutoFollowStayOnActive { get; init; }
    [JsonPropertyName("sidebarFontSize")] public double? SidebarFontSize { get; init; }
    [JsonPropertyName("hiddenInterfaceElements")] public IReadOnlyList<string>? HiddenInterfaceElements { get; init; }
    [JsonPropertyName("autoHideSidebarInactiveWindows")] public bool? AutoHideSidebarInactiveWindows { get; init; }
    // Windows-only (parity/windows-only.json): absent means on
    [JsonPropertyName("sidebarThemeColors")] public bool? SidebarThemeColors { get; init; }
    // Windows-only (parity/windows-only.json): absent or unknown means row
    [JsonPropertyName("sidebarThemeStyle")] public string? SidebarThemeStyle { get; init; }

    /// <summary>Windows-only (not in the upstream surface): re-render each main/split pane's saved
    /// output after a relaunch. Absent means on; see docs/PLATFORM-NOTES.md.</summary>
    [JsonPropertyName("restoreScrollback")] public bool? RestoreScrollback { get; init; }
    // Windows-only: absent keeps the theme brush rather than choosing a numeric default.
    [JsonPropertyName("overlayOpacity"), JsonConverter(typeof(Protocol.FiniteDoubleConverter))]
    public double? OverlayOpacity { get; init; }
    /// <summary>Windows-only: whether a shell pane closes when its shell exits, "always", "graceful"
    /// (on exit code 0 only) or "never". Absent means graceful; see docs/PLATFORM-NOTES.md.</summary>
    [JsonPropertyName("closeOnShellExit")] public string? CloseOnShellExit { get; init; }

    [JsonIgnore] public bool EffectiveRestoreScrollback => RestoreScrollback ?? true;

    /// <summary>Windows keeps its title bar for hidden mode. Unknown values retain the upstream
    /// legacy fallback; an explicit hidden value always runs as compact.</summary>
    public Model.ToolbarMode EffectiveToolbarMode() => ToolbarMode switch
    {
        "normal" => Model.ToolbarMode.Normal,
        "compact" or "hidden" => Model.ToolbarMode.Compact,
        _ => CompactToolbar == false ? Model.ToolbarMode.Normal : Model.ToolbarMode.Compact,
    };

    /// <summary>Whether a shell pane closes when its shell exits with this code. A value is matched
    /// exactly, like upstream's enum decode of other settings; an unknown one reads as graceful.</summary>
    public bool ClosesPaneOnShellExit(int exitCode) => CloseOnShellExit switch
    {
        "always" => true,
        "never" => false,
        _ => exitCode == 0,
    };

    /// <summary>Null keeps the theme brush; a number selects the opaque theme base at that opacity.</summary>
    public double? EffectiveOverlayOpacity(bool advancedEffectsEnabled) =>
        !advancedEffectsEnabled ? 1 :
        OverlayOpacity is { } opacity && double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : null;

    /// <summary>Records compare by value; a list property needs an explicit sequence comparison.</summary>
    public bool Equals(AppSettings? other) =>
        other is not null &&
        (FontFamily, FontSize, Theme, DarkTheme, FollowSystemAppearance, BackgroundOpacity,
         BackgroundBlur, NotificationsEnabled, NotificationBadgeEnabled, ToolbarMode, CompactToolbar,
         ActiveStatusColorHex, BlockedStatusColorHex, CompletedStatusColorHex) ==
        (other.FontFamily, other.FontSize, other.Theme, other.DarkTheme, other.FollowSystemAppearance,
         other.BackgroundOpacity, other.BackgroundBlur, other.NotificationsEnabled,
         other.NotificationBadgeEnabled, other.ToolbarMode, other.CompactToolbar,
         other.ActiveStatusColorHex, other.BlockedStatusColorHex, other.CompletedStatusColorHex) &&
        (ActiveStatusShape, BlockedStatusShape, CompletedStatusShape, ConfigDirectory,
         MouseScrollMultiplier, InactivePaneMuteStrength, SidebarBackgroundShift,
         RestoreRunningCommand, InheritGlobalGhosttyConfig, AttentionButtonEnabled, DockBounce,
         NotificationSoundName, BlockedStatusSoundName, RightClickPaste) ==
        (other.ActiveStatusShape, other.BlockedStatusShape, other.CompletedStatusShape,
         other.ConfigDirectory, other.MouseScrollMultiplier, other.InactivePaneMuteStrength,
         other.SidebarBackgroundShift, other.RestoreRunningCommand, other.InheritGlobalGhosttyConfig,
         other.AttentionButtonEnabled, other.DockBounce, other.NotificationSoundName,
         other.BlockedStatusSoundName, other.RightClickPaste) &&
        (NewSessionDirectory, NewSessionCustomDirectory, ConfirmCloseSession, CloseGraceUndoEnabled,
         AutoFollowAttention, AutoFollowStayOnActive, SidebarFontSize,
         AutoHideSidebarInactiveWindows, SidebarThemeColors, SidebarThemeStyle, RestoreScrollback, OverlayOpacity,
         CloseOnShellExit) ==
        (other.NewSessionDirectory, other.NewSessionCustomDirectory, other.ConfirmCloseSession,
         other.CloseGraceUndoEnabled, other.AutoFollowAttention, other.AutoFollowStayOnActive,
         other.SidebarFontSize, other.AutoHideSidebarInactiveWindows, other.SidebarThemeColors,
         other.SidebarThemeStyle, other.RestoreScrollback, other.OverlayOpacity,
         other.CloseOnShellExit) &&
        (HiddenInterfaceElements is null
            ? other.HiddenInterfaceElements is null
            : other.HiddenInterfaceElements is not null &&
              HiddenInterfaceElements.SequenceEqual(other.HiddenInterfaceElements));

    public override int GetHashCode() => HashCode.Combine(FontFamily, FontSize, Theme, SidebarFontSize);

    /// <summary>Whether sidebar rows of a workspace with its own theme show that theme's colors. A
    /// method, not a property, so it is never written to settings.json.</summary>
    public bool ShowSidebarThemeColors() => SidebarThemeColors != false;

    /// <summary>The style of the sidebar theme colors. A value is matched exactly, like upstream's
    /// enum decode of other settings.</summary>
    // Model. qualifies the enum: inside this record the string property of the same name hides it
    public Model.SidebarThemeStyle EffectiveSidebarThemeStyle() => SidebarThemeStyle switch
    {
        "label" => Model.SidebarThemeStyle.Label,
        _ => Model.SidebarThemeStyle.Row,
    };

    /// <summary>Whether a chrome element is hidden. Everything is shown by default, so an element
    /// absent from the persisted list reads as visible; a raw value matches exactly, as upstream's
    /// enum decode does.</summary>
    public bool IsInterfaceElementHidden(InterfaceElement element) =>
        HiddenInterfaceElements?.Contains(element.RawValue(), StringComparer.Ordinal) == true;

    /// <summary>The SwiftUI-overlay opacity for an inactive-pane mute strength: clamped to 0…10,
    /// scaled by 0.08 (0 → 0, 5 → 0.4 — the historical default, 10 → 0.8).</summary>
    public static double MuteOpacity(int strength) => Math.Clamp(strength, 0, 10) * 0.08;

    /// <summary>The signed sidebar background shift: clamped to 0…10 and measured from the neutral
    /// center 5 (5 → 0, 0 → -0.30 full lighten, 10 → +0.30 full darken). The magnitude is the wash
    /// opacity; positive darkens (black wash), negative lightens (white wash).</summary>
    public static double SidebarShiftAmount(int strength) => (Math.Clamp(strength, 0, 10) - 5) * 0.06;

    /// <summary>The clamped sidebar row-text point size (9…20) — a stray persisted value can't
    /// produce a degenerate row.</summary>
    public static double ClampSidebarFontSize(double size) =>
        Math.Clamp(size, SidebarFontSizeMin, SidebarFontSizeMax);

    /// <summary>The sidebar row height for a font size: the clamped point size plus a fixed 15pt of
    /// vertical padding (the default 13pt maps to the historical 28pt row).</summary>
    public static double SidebarRowHeight(double fontSize) =>
        Math.Round(ClampSidebarFontSize(fontSize), MidpointRounding.AwayFromZero) + 15;

    /// <summary>The configured glyph silhouette for one agent status: the raw name when it is a
    /// KNOWN shape, else null (= the default plain circle at the glyph site). Idle renders no
    /// glyph and so has no shape. A per-call session.status --shape overrides this upstream of
    /// here (AgentIndicator.Shape).</summary>
    public StatusShape? EffectiveStatusShape(AgentStatus status) => status switch
    {
        AgentStatus.Active => ActiveStatusShape is { } raw ? StatusShapes.FromWire(raw) : null,
        AgentStatus.Blocked => BlockedStatusShape is { } raw ? StatusShapes.FromWire(raw) : null,
        AgentStatus.Completed => CompletedStatusShape is { } raw ? StatusShapes.FromWire(raw) : null,
        _ => null,
    };

    /// <summary>The glyph tint for one agent status: a VALID per-call #rrggbb override wins, then
    /// the configured hex, then the built-in default. Idle never renders a glyph; it resolves to
    /// the active default harmlessly.</summary>
    public string EffectiveStatusColorHex(AgentStatus status, string? overrideHex)
    {
        if (IsHexColor(overrideHex)) return overrideHex!;
        var configured = status switch
        {
            AgentStatus.Blocked => BlockedStatusColorHex,
            AgentStatus.Completed => CompletedStatusColorHex,
            _ => ActiveStatusColorHex,
        };
        if (IsHexColor(configured)) return configured!;
        return status switch
        {
            AgentStatus.Blocked => DefaultBlockedStatusColorHex,
            AgentStatus.Completed => DefaultCompletedStatusColorHex,
            _ => DefaultActiveStatusColorHex,
        };
    }

    /// <summary>The working directory a new session opens in, resolving the newSessionDirectory
    /// mode against the active session's focused-pane cwd and home. An unknown/nil mode, a blank
    /// current cwd, or a blank custom path all fall back to home.</summary>
    public string ResolveNewSessionCwd(string? currentSessionCwd, string home) =>
        NewSessionDirectory switch
        {
            "currentSession" => string.IsNullOrEmpty(currentSessionCwd) ? home : currentSessionCwd,
            "custom" => string.IsNullOrEmpty(NewSessionCustomDirectory) ? home : NewSessionCustomDirectory,
            _ => home,
        };

    /// <summary>#rrggbb, case-insensitive — the same shape session.status --color validates.</summary>
    public static bool IsHexColor(string? hex) =>
        hex is { Length: 7 } && hex[0] == '#' && hex.Skip(1).All(Uri.IsHexDigit);
}
