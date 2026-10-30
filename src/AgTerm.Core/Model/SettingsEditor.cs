using System.Text.Json;
using System.Text.Json.Nodes;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Model;

public enum SettingControl { Toggle, Number, Choice, Font, Theme, Color, Directory, Interface, Unavailable }

public sealed record SettingChoice(string? Value, string Label);

public sealed record SettingField(string Key, string Tab, string Label, SettingControl Control,
    object? Default = null, double Minimum = 0, double Maximum = 0, SettingChoice[]? Choices = null, string? Hint = null)
{
    public bool Editable => Control != SettingControl.Unavailable;
}

/// <summary>UI-thread-owned editor. Failed saves retain the draft and its original merge baseline.</summary>
public sealed class SettingsEditor
{
    private readonly SettingsCoordinator coordinator;
    private SettingsSnapshot baseline;
    public AppSettings Draft { get; private set; }
    public Exception? Error { get; private set; }
    public bool HasUnsavedChanges => !Draft.Equals(baseline.Settings);
    public static IReadOnlyList<string> Tabs { get; } =
        ["General", "Appearance", "Interface", "Notifications", "Agent Status", "Key Mapping"];

    private static readonly SettingChoice[] SoundChoices =
        [new(null, "None"), new("default", "Windows default"), new("beep", "Beep (same as Windows default)")];
    private static readonly SettingChoice[] ShapeChoices =
        [new(null, "Default (Circle)"), new("circle", "Circle"), new("square", "Square"),
            new("triangle", "Triangle"), new("diamond", "Diamond"), new("capsule", "Capsule"), new("star", "Star")];

    public static IReadOnlyList<SettingField> Fields { get; } =
    [
        new("mouseScrollMultiplier", "General", "Scroll speed", SettingControl.Number, 3d, 1, 10),
        new("rightClickPaste", "General", "Right-click pastes", SettingControl.Toggle, true),
        new("copyOnSelect", "General", "Copy on select", SettingControl.Choice, "selection",
            Choices: [new(null, "Default (Selection buffer)"), new("selection", "Selection buffer"),
                new("clipboard", "Selection buffer and clipboard"), new("off", "Off")],
            Hint: "Middle-click pastes the selection buffer, or the clipboard when the selection is also copied there. Ctrl+C and right-click use the clipboard."),
        new("newSessionDirectory", "General", "New sessions open in", SettingControl.Choice, "home",
            Choices: [new(null, "Default (Home)"), new("home", "Home"),
                new("currentSession", "Current session"), new("custom", "Custom")]),
        new("newSessionCustomDirectory", "General", "Custom directory", SettingControl.Directory),
        new("restoreRunningCommand", "General", "Restore running commands on restart", SettingControl.Toggle, false,
            Hint: "Requires an app restart. Open windows keep their current restore behavior."),
        new("confirmCloseSession", "General", "Confirm before closing a session", SettingControl.Toggle, false),
        new("closeGraceUndoEnabled", "General", "Allow undo after closing sessions and workspaces", SettingControl.Toggle, true),
        new("restoreScrollback", "General", "Restore saved pane output", SettingControl.Toggle, true),
        new("closeOnShellExit", "General", "Close a shell pane on exit", SettingControl.Choice, "graceful",
            Choices: [new(null, "Default (Successful exit only)"), new("graceful", "Successful exit only"),
                new("always", "Always"), new("never", "Never")]),
        new("inheritGlobalGhosttyConfig", "General", "Use my global Ghostty config", SettingControl.Unavailable,
            Hint: "Not available: Windows uses xterm.js, not Ghostty. The stored value is preserved."),
        new("fontFamily", "Appearance", "Terminal font", SettingControl.Font),
        new("fontSize", "Appearance", "Default font size", SettingControl.Number, 14d, 8, 32),
        new("toolbarMode", "Appearance", "Toolbar", SettingControl.Choice, "compact",
            Choices: [new("normal", "Normal"), new("compact", "Compact")]),
        new("theme", "Appearance", "Theme / light theme", SettingControl.Theme),
        new("followSystemAppearance", "Appearance", "Follow Windows appearance", SettingControl.Toggle, false),
        new("darkTheme", "Appearance", "Dark theme", SettingControl.Theme,
            Hint: "Used in dark mode while following Windows appearance."),
        new("backgroundOpacity", "Appearance", "Background opacity", SettingControl.Number, 1d, 0, 1),
        new("backgroundBlur", "Appearance", "Background blur", SettingControl.Number, 0, 0, 100,
            Hint: "Needs opacity below 1. Windows Acrylic controls the blur strength; this enables it above zero."),
        new("sidebarBackgroundShift", "Appearance", "Sidebar tint", SettingControl.Number, 5, 0, 10),
        new("sidebarFontSize", "Appearance", "Sidebar font size", SettingControl.Number, 13d, 9, 20),
        new("inactivePaneMuteStrength", "Appearance", "Inactive pane mute", SettingControl.Number, 5, 0, 10),
        new("overlayOpacity", "Appearance", "Panel opacity", SettingControl.Number, null, 0, 1,
            Hint: "Default uses the theme brush. Zero is transparent; one is opaque."),
        new("sidebarThemeStyle", "Appearance", "Workspace theme colors in sidebar", SettingControl.Choice,
            Choices: [new(null, "Off"), new("row", "Whole row"), new("label", "Name label")]),
        new("hiddenInterfaceElements", "Interface", "Visible interface elements", SettingControl.Interface),
        new("autoHideSidebarInactiveWindows", "Interface", "Show sidebar only in the active window",
            SettingControl.Toggle, false),
        new("notificationsEnabled", "Notifications", "Show notification banners", SettingControl.Toggle, true),
        new("notificationBadgeEnabled", "Notifications", "Show notification badges", SettingControl.Toggle, true),
        new("notificationSoundName", "Notifications", "Notification sound", SettingControl.Choice,
            Choices: SoundChoices, Hint: "None is silent. Windows default and Beep use the same system alert. Add your own WAV files to the sounds folder in the state directory."),
        new("attentionButtonEnabled", "Notifications", "Show attention indicator", SettingControl.Toggle, false),
        new("dockBounce", "Notifications", "Flash taskbar button", SettingControl.Choice,
            Choices: [new(null, "Off"), new("once", "Once")],
            Hint: "Flashes once for a notification while agterm is in the background, including during Do Not Disturb. Does not play a sound."),
        new("activeStatusColorHex", "Agent Status", "Active color", SettingControl.Color, AppSettings.DefaultActiveStatusColorHex),
        new("activeStatusShape", "Agent Status", "Active shape", SettingControl.Choice, "circle", Choices: ShapeChoices),
        new("blockedStatusColorHex", "Agent Status", "Blocked color", SettingControl.Color, AppSettings.DefaultBlockedStatusColorHex),
        new("blockedStatusShape", "Agent Status", "Blocked shape", SettingControl.Choice, "circle", Choices: ShapeChoices),
        new("completedStatusColorHex", "Agent Status", "Completed color", SettingControl.Color, AppSettings.DefaultCompletedStatusColorHex),
        new("completedStatusShape", "Agent Status", "Completed shape", SettingControl.Choice, "circle", Choices: ShapeChoices),
        new("blockedStatusSoundName", "Agent Status", "Blocked sound", SettingControl.Choice, Choices: SoundChoices,
            Hint: "Plays when a session becomes blocked. None disables this default. An explicit status sound takes priority. Add WAV files to the sounds folder in the state directory."),
        new("autoFollowAttention", "Agent Status", "Auto-follow blocked sessions", SettingControl.Choice,
            Choices: [new(null, "Disabled"), new("s5", "5 sec idle"), new("s10", "10 sec idle"),
                new("s30", "30 sec idle"), new("s60", "60 sec idle"), new("m5", "5 min idle")]),
        new("autoFollowStayOnActive", "Agent Status", "Auto-follow away from a running session",
            SettingControl.Toggle, false, Hint: "Only applies while auto-follow is on."),
        new("configDirectory", "Key Mapping", "Keymap directory", SettingControl.Directory,
            Hint: "Default is the state directory. Changing it reloads keymap.conf in every window."),
    ];

    public SettingsEditor(SettingsCoordinator coordinator)
    {
        this.coordinator = coordinator;
        baseline = coordinator.State.Snapshot;
        try { baseline = coordinator.ReadStrict(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { Error = ex; }
        Draft = baseline.Settings;
    }

    public JsonNode? Value(string key) => Document(Draft)[key]?.DeepClone();

    public string? ChoiceValue(string key) => key switch
    {
        "toolbarMode" => Draft.EffectiveToolbarMode() == ToolbarMode.Normal ? "normal" : "compact",
        "dockBounce" => Draft.TaskbarFlashEnabled() ? "once" : null,
        "autoFollowAttention" => Draft.AutoFollowTimeout() is not null ? Draft.AutoFollowAttention : null,
        "copyOnSelect" => Draft.CopyOnSelect is "selection" or "clipboard" or "off" ? Draft.CopyOnSelect : null,
        _ => Value(key)?.GetValue<string>(),
    };

    public bool ToggleValue(string key) => key == "autoFollowStayOnActive"
        ? Draft.AutoFollowStayOnActive != true
        : Value(key)?.GetValue<bool>() ?? Fields.Single(field => field.Key == key).Default is true;

    public bool IsEnabled(string key) =>
        key != "autoFollowStayOnActive" || Draft.AutoFollowTimeout() is not null;

    public Task SetToggleAsync(string key, bool value) =>
        SetAsync(key, key == "autoFollowStayOnActive" ? !value : value);

    public async Task SetAsync(string key, object? value)
    {
        try
        {
            var field = Fields.Single(field => field.Key == key && field.Editable);
            Validate(field, value);
            if (field.Control == SettingControl.Theme && Draft.FollowSystemAppearance == true && value is null)
                throw new ArgumentException("Choose a named theme while following Windows appearance.");
            if (field.Control == SettingControl.Number && field.Default is int && value is not null)
                value = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            var document = Document(Draft);
            if (value is null || Equals(value, field.Default)) document.Remove(key);
            else document[key] = JsonSerializer.SerializeToNode(value, ControlJson.Options);
            if (key == "toolbarMode") document.Remove("compactToolbar");
            Draft = ControlJson.Deserialize<AppSettings>(document.ToJsonString())!;
            await SaveAsync();
        }
        catch (Exception ex) { Error = ex; throw; }
    }

    public Task SetFollowingAsync(bool follow, bool systemDark) => ChangeAsync(settings =>
    {
        if (follow)
        {
            var light = string.IsNullOrEmpty(settings.Theme) ? "Builtin Light" : settings.Theme;
            return settings with { Theme = light, DarkTheme = settings.DarkTheme ?? light, FollowSystemAppearance = true };
        }
        return settings with { Theme = settings.FollowSystemAppearance == true && systemDark
                ? settings.DarkTheme ?? settings.Theme : settings.Theme,
            DarkTheme = null, FollowSystemAppearance = null };
    });

    public Task SetInterfaceVisibleAsync(InterfaceElement element, bool visible) => ChangeAsync(settings =>
    {
        var hidden = (settings.HiddenInterfaceElements ?? []).Where(name => name != element.RawValue()).ToList();
        if (!visible) hidden.Add(element.RawValue());
        return settings with { HiddenInterfaceElements = hidden.Count == 0 ? null : hidden };
    });

    public Task ResetAgentStatusAsync() => ChangeAsync(settings => settings with
    {
        ActiveStatusColorHex = null, BlockedStatusColorHex = null, CompletedStatusColorHex = null,
        ActiveStatusShape = null, BlockedStatusShape = null, CompletedStatusShape = null, BlockedStatusSoundName = null,
    });

    private async Task ChangeAsync(Func<AppSettings, AppSettings> change)
    {
        Draft = change(Draft);
        await SaveAsync();
    }

    public async Task SaveAsync()
    {
        try
        {
            var draft = Draft;
            baseline = await coordinator.UpdateAsync(baseline, _ => draft);
            Draft = baseline.Settings;
            Error = null;
        }
        catch (SettingsApplyException ex)
        {
            baseline = ex.Snapshot;
            Draft = baseline.Settings;
            Error = ex;
            throw;
        }
        catch (Exception ex) { Error = ex; throw; }
    }

    public async Task ReloadAsync()
    {
        try
        {
            baseline = await coordinator.ReloadAsync();
            Draft = baseline.Settings;
            Error = null;
        }
        catch (Exception ex) { Error = ex; throw; }
    }

    public void Observe(SettingsApplicationState state)
    {
        if (!HasUnsavedChanges && !state.IsApplying)
        {
            baseline = state.Snapshot;
            Draft = baseline.Settings;
            Error = state.Error;
        }
    }

    private static JsonObject Document(AppSettings settings) => JsonNode.Parse(ControlJson.Serialize(settings))!.AsObject();

    private static void Validate(SettingField field, object? value)
    {
        if (value is null) return;
        var valid = field.Control switch
        {
            SettingControl.Toggle => value is bool,
            SettingControl.Number => double.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number) && number >= field.Minimum && number <= field.Maximum
                && (field.Default is not int || number == Math.Truncate(number)),
            SettingControl.Color => value is string color && AppSettings.IsHexColor(color),
            SettingControl.Choice when field.Key is "notificationSoundName" or "blockedStatusSoundName" =>
                value is string sound && !string.IsNullOrEmpty(sound),
            SettingControl.Choice => value is string choice && field.Choices!.Any(option => option.Value == choice),
            SettingControl.Font or SettingControl.Theme => value is string name && !string.IsNullOrWhiteSpace(name),
            SettingControl.Directory => value is string path && Path.IsPathFullyQualified(path) && Directory.Exists(path),
            _ => false,
        };
        if (!valid) throw new ArgumentException($"Invalid value for {field.Label}.");
    }
}
