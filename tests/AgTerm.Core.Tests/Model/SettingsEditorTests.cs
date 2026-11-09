using System.Text.Json;
using System.Text.Json.Nodes;
using AgTerm.Core.Install;
using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class SettingsEditorTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-settings-editor-").FullName;
    private SettingsStore Store => new(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Six_tabs_cover_every_persisted_field_except_the_explicitly_deferred_fields()
    {
        Assert.Equal(["General", "Appearance", "Interface", "Notifications", "Agent Status", "Key Mapping"],
            SettingsEditor.Tabs);
        // shownInterfaceElements has no row of its own: the hiddenInterfaceElements control edits both lists
        var deferred = new[] { "compactToolbar", "shownInterfaceElements" };
        var fields = typeof(AppSettings).GetProperties()
            .Select(property => property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                .OfType<System.Text.Json.Serialization.JsonPropertyNameAttribute>().SingleOrDefault()?.Name)
            .OfType<string>().Except(deferred).Order();
        Assert.Equal(fields, SettingsEditor.Fields.Select(field => field.Key).Order());
        Assert.False(SettingsEditor.Fields.Single(field => field.Key == "inheritGlobalGhosttyConfig").Editable);
    }

    [Fact]
    public void Taskbar_flash_picker_offers_only_off_and_once_in_notifications()
    {
        var field = Assert.Single(SettingsEditor.Fields, field => field.Key == "dockBounce");
        Assert.Equal("Notifications", field.Tab);
        Assert.Equal("Flash taskbar button", field.Label);
        Assert.Equal(SettingControl.Choice, field.Control);
        Assert.Null(field.Default);
        Assert.Equal([null, "once"], field.Choices!.Select(choice => choice.Value));
        Assert.Equal(["Off", "Once"], field.Choices!.Select(choice => choice.Label));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("off", null)]
    [InlineData("once", "once")]
    [InlineData("untilFocused", "once")]
    [InlineData("future", null)]
    [InlineData("ONCE", null)]
    public async Task Taskbar_flash_picker_reads_effective_value_without_rewriting(string? raw, string? selected)
    {
        Store.Save(new AppSettings { DockBounce = raw });
        var original = File.ReadAllText(Store.FilePath);
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        Assert.Equal(selected, editor.ChoiceValue("dockBounce"));
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllText(Store.FilePath));
        await editor.SetAsync("fontSize", 18d);
        Assert.Equal(raw, Store.ReadStrict().Settings.DockBounce);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("once")]
    public async Task Writing_taskbar_flash_replaces_legacy_value_and_off_removes_override(string? selected)
    {
        File.WriteAllText(Store.FilePath, """{"dockBounce":"untilFocused","future":42}""");
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await editor.SetAsync("dockBounce", selected);
        var saved = JsonNode.Parse(File.ReadAllText(Store.FilePath))!;
        Assert.Equal(selected, Store.ReadStrict().Settings.DockBounce);
        Assert.Equal(42, saved["future"]!.GetValue<int>());
        if (selected is null) Assert.Null(saved["dockBounce"]);
    }

    [Theory]
    [InlineData("untilFocused")]
    [InlineData("off")]
    [InlineData("future")]
    public async Task Legacy_taskbar_values_are_not_writable_picker_options(string value)
    {
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await Assert.ThrowsAsync<ArgumentException>(() => editor.SetAsync("dockBounce", value));
        Assert.False(File.Exists(Store.FilePath));
    }

    [Fact]
    public async Task Active_window_sidebar_toggle_defaults_off_and_preserves_other_settings()
    {
        var field = Assert.Single(SettingsEditor.Fields, field => field.Key == "autoHideSidebarInactiveWindows");
        Assert.Equal("Interface", field.Tab);
        Assert.Equal("Show sidebar only in the active window", field.Label);
        Assert.Equal(SettingControl.Toggle, field.Control);
        Assert.Equal(false, field.Default);
        File.WriteAllText(Store.FilePath, """{"toolbarMode":"hidden","future":42}""");
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await editor.SetAsync(field.Key, true);
        Assert.True(Store.ReadStrict().Settings.AutoHideSidebarInactiveWindows);
        await editor.SetAsync(field.Key, false);
        var saved = JsonNode.Parse(File.ReadAllText(Store.FilePath))!;
        Assert.Null(saved[field.Key]);
        Assert.Equal("hidden", saved["toolbarMode"]!.GetValue<string>());
        Assert.Equal(42, saved["future"]!.GetValue<int>());
    }

    [Fact]
    public void Session_info_is_an_interface_choice_that_defaults_to_always()
    {
        var field = Assert.Single(SettingsEditor.Fields, field => field.Key == "sessionInfoDisplay");
        Assert.Equal("Interface", field.Tab);
        Assert.Equal("Session info", field.Label);
        Assert.Equal(SettingControl.Choice, field.Control);
        Assert.Null(field.Default);
        Assert.Equal([null, "flagged", "off"], field.Choices!.Select(choice => choice.Value));
        Assert.Equal(["Always", "Flagged", "Off"], field.Choices!.Select(choice => choice.Label));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("always", null)]
    [InlineData("flagged", "flagged")]
    [InlineData("off", "off")]
    [InlineData("sometimes", null)]
    public void The_session_info_picker_shows_the_effective_value(string? stored, string? shown)
    {
        File.WriteAllText(Store.FilePath, stored is null ? "{}" : $$"""{"sessionInfoDisplay":"{{stored}}"}""");

        Assert.Equal(shown, new SettingsEditor(new SettingsCoordinator(Store)).ChoiceValue("sessionInfoDisplay"));
    }

    [Fact]
    public async Task Choosing_flagged_persists_it_and_always_removes_the_key()
    {
        var editor = new SettingsEditor(new SettingsCoordinator(Store));

        await editor.SetAsync("sessionInfoDisplay", "flagged");
        Assert.Equal("flagged", JsonNode.Parse(File.ReadAllText(Store.FilePath))!["sessionInfoDisplay"]?.GetValue<string>());

        await editor.SetAsync("sessionInfoDisplay", null);
        Assert.False(JsonNode.Parse(File.ReadAllText(Store.FilePath))!.AsObject().ContainsKey("sessionInfoDisplay"));
    }

    [Fact]
    public void Flagged_view_layout_is_a_general_choice_that_defaults_to_the_flat_list()
    {
        var field = Assert.Single(SettingsEditor.Fields, field => field.Key == "flaggedViewLayout");
        Assert.Equal("General", field.Tab);
        Assert.Equal("Flagged view layout", field.Label);
        Assert.Equal(SettingControl.Choice, field.Control);
        Assert.Null(field.Default);
        Assert.Equal([null, "plain", "tree"], field.Choices!.Select(choice => choice.Value));
        Assert.Equal(["Flat list", "Flat list, names only", "Workspace tree"], field.Choices!.Select(choice => choice.Label));
        var general = SettingsEditor.Fields.Where(field => field.Tab == "General").Select(field => field.Key).ToList();
        Assert.Equal(general.IndexOf("closeGraceUndoEnabled") + 1, general.IndexOf("flaggedViewLayout"));
    }

    [Fact]
    public async Task Resume_agent_sessions_sits_under_restore_running_commands_and_needs_it()
    {
        var field = Assert.Single(SettingsEditor.Fields, field => field.Key == "resumeAgentSessions");
        Assert.Equal("General", field.Tab);
        Assert.Equal("Resume agent sessions after restart", field.Label);
        Assert.Equal(SettingControl.Toggle, field.Control);
        Assert.Equal(false, field.Default);
        Assert.Equal("restoreRunningCommand", field.Parent);
        var general = SettingsEditor.Fields.Where(field => field.Tab == "General").Select(field => field.Key).ToList();
        Assert.Equal(general.IndexOf("restoreRunningCommand") + 1, general.IndexOf("resumeAgentSessions"));

        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        Assert.False(editor.IsEnabled("resumeAgentSessions"));
        await editor.SetToggleAsync("restoreRunningCommand", true);
        Assert.True(editor.IsEnabled("resumeAgentSessions"));
        await editor.SetToggleAsync("resumeAgentSessions", true);
        Assert.True(editor.ToggleValue("resumeAgentSessions"));
        Assert.Contains("\"resumeAgentSessions\":true", File.ReadAllText(Store.FilePath));
    }

    [Fact]
    public async Task Resume_agent_sessions_hint_says_what_is_missing_or_what_the_hooks_did()
    {
        var field = SettingsEditor.Fields.Single(field => field.Key == "resumeAgentSessions");
        var report = new AgentResumeReport([new("Claude Code", AgentResumeState.On), new("Codex", AgentResumeState.NotFound)]);
        var editor = new SettingsEditor(new SettingsCoordinator(Store));

        Assert.Equal("Turn on Restore running commands on restart first.", editor.HintFor(field, report));
        await editor.SetToggleAsync("restoreRunningCommand", true);
        Assert.Equal(field.Hint, editor.HintFor(field, report));
        await editor.SetToggleAsync("resumeAgentSessions", true);
        Assert.Equal(field.Hint, editor.HintFor(field, null));
        Assert.Equal(report.Describe(), editor.HintFor(field, report));

        var other = SettingsEditor.Fields.Single(field => field.Key == "restoreRunningCommand");
        Assert.Equal(other.Hint, editor.HintFor(other, report));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("flat", null)]
    [InlineData("plain", "plain")]
    [InlineData("tree", "tree")]
    [InlineData("grid", null)]
    public void The_flagged_view_layout_picker_shows_the_effective_value(string? stored, string? shown)
    {
        File.WriteAllText(Store.FilePath, stored is null ? "{}" : $$"""{"flaggedViewLayout":"{{stored}}"}""");

        Assert.Equal(shown, new SettingsEditor(new SettingsCoordinator(Store)).ChoiceValue("flaggedViewLayout"));
    }

    [Fact]
    public async Task Choosing_the_tree_layout_persists_it_and_the_flat_list_removes_the_key()
    {
        var editor = new SettingsEditor(new SettingsCoordinator(Store));

        await editor.SetAsync("flaggedViewLayout", "tree");
        Assert.Equal("tree", JsonNode.Parse(File.ReadAllText(Store.FilePath))!["flaggedViewLayout"]?.GetValue<string>());

        await editor.SetAsync("flaggedViewLayout", null);
        Assert.False(JsonNode.Parse(File.ReadAllText(Store.FilePath))!.AsObject().ContainsKey("flaggedViewLayout"));
    }

    [Fact]
    public void Sidebar_theme_colors_are_one_choice_that_defaults_to_off()
    {
        Assert.DoesNotContain(SettingsEditor.Fields, field => field.Key == "sidebarThemeColors");
        var field = Assert.Single(SettingsEditor.Fields, field => field.Key == "sidebarThemeStyle");
        Assert.Equal("Appearance", field.Tab);
        Assert.Equal("Workspace theme colors in sidebar", field.Label);
        Assert.Equal(SettingControl.Choice, field.Control);
        Assert.Null(field.Default);
        Assert.Equal([null, "row", "label"], field.Choices!.Select(choice => choice.Value));
        Assert.Equal(["Off", "Whole row", "Name label"], field.Choices!.Select(choice => choice.Label));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("row")]
    [InlineData("label")]
    public async Task Writing_the_sidebar_theme_style_removes_the_retired_key(string? style)
    {
        File.WriteAllText(Store.FilePath, """{"sidebarThemeColors":false,"sidebarThemeStyle":"label","future":42}""");
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        if (style == "label") await editor.SetAsync("sidebarThemeStyle", "row");

        await editor.SetAsync("sidebarThemeStyle", style);

        var saved = JsonNode.Parse(File.ReadAllText(Store.FilePath))!.AsObject();
        Assert.False(saved.ContainsKey("sidebarThemeColors"));
        Assert.Equal(42, saved["future"]!.GetValue<int>());
        Assert.Equal(style, saved["sidebarThemeStyle"]?.GetValue<string>());
        Assert.Equal(style, editor.ChoiceValue("sidebarThemeStyle"));
    }

    [Fact]
    public async Task Editing_another_field_keeps_the_retired_key_in_the_file()
    {
        File.WriteAllText(Store.FilePath, """{"sidebarThemeColors":false}""");
        var editor = new SettingsEditor(new SettingsCoordinator(Store));

        await editor.SetAsync("fontSize", 18d);

        var saved = JsonNode.Parse(File.ReadAllText(Store.FilePath))!.AsObject();
        Assert.False(saved["sidebarThemeColors"]!.GetValue<bool>());
    }

    [Fact]
    public void Toolbar_choices_offer_only_normal_and_compact()
    {
        var field = Assert.Single(SettingsEditor.Fields, field => field.Key == "toolbarMode");
        Assert.Equal("Appearance", field.Tab);
        Assert.Equal(SettingControl.Choice, field.Control);
        Assert.Equal("compact", field.Default);
        Assert.Equal(["normal", "compact"], field.Choices!.Select(choice => choice.Value));
        Assert.Equal(["Normal", "Compact"], field.Choices!.Select(choice => choice.Label));
    }

    [Theory]
    [InlineData("normal", "normal")]
    [InlineData("compact", null)]
    [InlineData(null, null)]
    public async Task Writing_toolbar_mode_clears_the_legacy_override(string? mode, string? stored)
    {
        File.WriteAllText(Store.FilePath, """{"toolbarMode":"hidden","compactToolbar":false,"future":42}""");
        var editor = new SettingsEditor(new SettingsCoordinator(Store));

        await editor.SetAsync("toolbarMode", mode);

        Assert.Equal(stored, editor.Draft.ToolbarMode);
        Assert.Null(editor.Draft.CompactToolbar);
        var saved = JsonNode.Parse(File.ReadAllText(Store.FilePath))!.AsObject();
        Assert.False(saved.ContainsKey("compactToolbar"));
        Assert.Equal(42, saved["future"]!.GetValue<int>());
        if (stored is null) Assert.False(saved.ContainsKey("toolbarMode"));
    }

    [Fact]
    public async Task Hidden_is_preserved_when_opening_settings_or_editing_another_field()
    {
        var original = """{"toolbarMode":"hidden","compactToolbar":false}""";
        File.WriteAllText(Store.FilePath, original);
        var editor = new SettingsEditor(new SettingsCoordinator(Store));

        Assert.Equal("hidden", editor.Draft.ToolbarMode);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllText(Store.FilePath));
        await editor.SetAsync("fontSize", 18d);
        Assert.Equal("hidden", Store.Load().ToolbarMode);
        Assert.False(Store.Load().CompactToolbar);
    }

    [Fact]
    public async Task Hidden_is_not_a_writable_toolbar_choice()
    {
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await Assert.ThrowsAsync<ArgumentException>(() => editor.SetAsync("toolbarMode", "hidden"));
        Assert.False(File.Exists(Store.FilePath));
    }

    [Theory]
    [InlineData("hidden", false, "compact")]
    [InlineData("normal", true, "normal")]
    [InlineData(null, false, "normal")]
    [InlineData(null, null, "compact")]
    [InlineData("future", false, "normal")]
    public void Toolbar_picker_shows_the_effective_mode_without_editing(
        string? raw, bool? legacy, string selected)
    {
        Store.Save(new AppSettings { ToolbarMode = raw, CompactToolbar = legacy });
        var original = File.ReadAllText(Store.FilePath);
        var editor = new SettingsEditor(new SettingsCoordinator(Store));

        Assert.Equal(selected, editor.ChoiceValue("toolbarMode"));
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllText(Store.FilePath));
    }

    [Fact]
    public async Task Edits_apply_immediately_preserve_external_and_deferred_values_and_remove_defaults()
    {
        File.WriteAllText(Store.FilePath, """{"toolbarMode":"future","dockBounce":"once","future":true}""");
        var coordinator = new SettingsCoordinator(Store);
        var editor = new SettingsEditor(coordinator);
        var calls = 0;
        using var subscription = await coordinator.SubscribeAsync((_, _) => { calls++; return Task.CompletedTask; });
        var external = JsonNode.Parse(File.ReadAllText(Store.FilePath))!.AsObject();
        external["sidebarFontSize"] = 17;
        File.WriteAllText(Store.FilePath, external.ToJsonString());
        await editor.SetAsync("fontSize", 22d);
        Assert.Equal(22, editor.Draft.FontSize);
        Assert.Equal(17, editor.Draft.SidebarFontSize);
        Assert.Equal(2, calls);
        await editor.SetAsync("fontSize", null);
        var actual = JsonNode.Parse(File.ReadAllText(Store.FilePath))!.AsObject();
        Assert.False(actual.ContainsKey("fontSize"));
        Assert.Equal("future", actual["toolbarMode"]!.GetValue<string>());
        Assert.True(actual["future"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Conflict_keeps_the_unsaved_draft_until_explicit_reload()
    {
        Store.Save(new AppSettings { FontSize = 14 });
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        File.WriteAllText(Store.FilePath, """{"fontSize":20}""");
        await Assert.ThrowsAsync<SettingsConflictException>(() => editor.SetAsync("fontSize", 18d));
        Assert.Equal(18, editor.Draft.FontSize);
        Assert.NotNull(editor.Error);
        Assert.True(editor.HasUnsavedChanges);
        await editor.ReloadAsync();
        Assert.Equal(20, editor.Draft.FontSize);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Null(editor.Error);
    }

    [Theory]
    [InlineData("fontSize", 7)]
    [InlineData("fontSize", 33)]
    [InlineData("backgroundOpacity", 2)]
    [InlineData("backgroundBlur", 101)]
    [InlineData("mouseScrollMultiplier", 0)]
    [InlineData("sidebarFontSize", 21)]
    public async Task Invalid_control_values_do_not_reach_disk(string field, double value)
    {
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await Assert.ThrowsAsync<ArgumentException>(() => editor.SetAsync(field, value));
        Assert.False(File.Exists(Store.FilePath));
    }

    [Fact]
    public async Task Corrupt_file_is_visible_and_cannot_be_overwritten()
    {
        File.WriteAllText(Store.FilePath, "{broken");
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        Assert.NotNull(editor.Error);
        await Assert.ThrowsAnyAsync<JsonException>(() => editor.SetAsync("fontSize", 18d));
        Assert.Equal("{broken", File.ReadAllText(Store.FilePath));
    }

    [Fact]
    public async Task Status_reset_clears_only_the_seven_status_fields()
    {
        Store.Save(new AppSettings { ActiveStatusColorHex = "#123456", BlockedStatusShape = "square",
            BlockedStatusSoundName = "beep", AutoFollowAttention = "s30", AutoFollowStayOnActive = true,
            NotificationSoundName = "default", FontSize = 19 });
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await editor.ResetAgentStatusAsync();
        Assert.Null(editor.Draft.ActiveStatusColorHex);
        Assert.Null(editor.Draft.BlockedStatusShape);
        Assert.Null(editor.Draft.BlockedStatusSoundName);
        Assert.Equal("s30", editor.Draft.AutoFollowAttention);
        Assert.True(editor.Draft.AutoFollowStayOnActive);
        Assert.Equal("default", editor.Draft.NotificationSoundName);
        Assert.Equal(19, editor.Draft.FontSize);
    }

    // upstream SettingsModel.setInterfaceElementVisible: an element hidden by default is toggled in the
    // shown list, written sorted and dropped when empty; the hidden list is left alone
    [Fact]
    public async Task An_opt_in_element_is_toggled_in_the_shown_list()
    {
        Store.Save(new AppSettings { HiddenInterfaceElements = ["scratch"], ShownInterfaceElements = ["zfuture"] });
        var editor = new SettingsEditor(new SettingsCoordinator(Store));

        await editor.SetInterfaceVisibleAsync(InterfaceElement.WorkspaceName, true);
        await editor.SetInterfaceVisibleAsync(InterfaceElement.OverlayToggle, true);
        Assert.Equal(["overlayToggle", "workspaceName", "zfuture"], editor.Draft.ShownInterfaceElements);
        Assert.Equal(["scratch"], editor.Draft.HiddenInterfaceElements);

        await editor.SetInterfaceVisibleAsync(InterfaceElement.OverlayToggle, false);
        Assert.Equal(["workspaceName", "zfuture"], editor.Draft.ShownInterfaceElements);

        Store.Save(new AppSettings { ShownInterfaceElements = ["workspaceName"] });
        editor = new SettingsEditor(new SettingsCoordinator(Store));
        await editor.SetInterfaceVisibleAsync(InterfaceElement.WorkspaceName, false);
        Assert.Null(editor.Draft.ShownInterfaceElements);
        Assert.True(editor.Draft.IsInterfaceElementHidden(InterfaceElement.WorkspaceName));
    }

    [Fact]
    public async Task Interface_changes_keep_unknown_names_and_follow_appearance_seeds_both_slots()
    {
        Store.Save(new AppSettings { Theme = "Dracula", HiddenInterfaceElements = ["future"] });
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await editor.SetInterfaceVisibleAsync(InterfaceElement.Scratch, false);
        Assert.Equal(["future", "scratch"], editor.Draft.HiddenInterfaceElements);
        await editor.SetFollowingAsync(true, true);
        Assert.True(editor.Draft.FollowSystemAppearance);
        Assert.Equal("Dracula", editor.Draft.Theme);
        Assert.Equal("Dracula", editor.Draft.DarkTheme);
        await editor.SetAsync("darkTheme", "agterm");
        await editor.SetFollowingAsync(false, true);
        Assert.Equal("agterm", editor.Draft.Theme);
        Assert.Null(editor.Draft.DarkTheme);
    }

    [Fact]
    public async Task Apply_failure_is_saved_not_an_unsaved_draft_and_reload_can_retry()
    {
        var coordinator = new SettingsCoordinator(Store);
        var fail = false;
        using var subscription = await coordinator.SubscribeAsync((_, _) =>
            fail ? Task.FromException(new IOException("apply failed")) : Task.CompletedTask);
        var editor = new SettingsEditor(coordinator);
        fail = true;
        await Assert.ThrowsAsync<SettingsApplyException>(() => editor.SetAsync("fontSize", 20d));
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(20, Store.Load().FontSize);
        fail = false;
        await editor.ReloadAsync();
        Assert.Null(editor.Error);
    }

    [Theory]
    [InlineData("theme")]
    [InlineData("darkTheme")]
    public async Task Following_appearance_requires_named_theme_slots(string key)
    {
        Store.Save(new AppSettings { Theme = "agterm", DarkTheme = "Dracula", FollowSystemAppearance = true });
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        var before = File.ReadAllText(Store.FilePath);
        await Assert.ThrowsAsync<ArgumentException>(() => editor.SetAsync(key, null));
        Assert.Equal(before, File.ReadAllText(Store.FilePath));
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task Integer_controls_validate_before_conversion_and_omit_their_default()
    {
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await Assert.ThrowsAsync<ArgumentException>(() => editor.SetAsync("backgroundBlur", 3.5d));
        Assert.False(File.Exists(Store.FilePath));
        await editor.SetAsync("backgroundBlur", 20d);
        Assert.Equal(20, Store.Load().BackgroundBlur);
        await editor.SetAsync("backgroundBlur", 0d);
        Assert.False(JsonNode.Parse(File.ReadAllText(Store.FilePath))!.AsObject().ContainsKey("backgroundBlur"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Resetting_disabled_appearance_following_keeps_the_active_single_theme(bool? follow)
    {
        Store.Save(new AppSettings { Theme = "Dracula", DarkTheme = "agterm", FollowSystemAppearance = follow });
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await editor.SetFollowingAsync(false, true);
        Assert.Equal("Dracula", editor.Draft.Theme);
        Assert.Null(editor.Draft.DarkTheme);
        Assert.Null(editor.Draft.FollowSystemAppearance);
    }

    [Theory]
    [InlineData("notificationSoundName")]
    [InlineData("blockedStatusSoundName")]
    public async Task Sound_choices_label_silence_and_windows_sounds_without_changing_values(string key)
    {
        var choices = JsonSerializer.SerializeToNode(SettingsEditor.Fields.Single(field => field.Key == key).Choices)!.AsArray();
        Assert.Equal("""[{"Value":null,"Label":"None"},{"Value":"default","Label":"Windows default"},{"Value":"beep","Label":"Beep (same as Windows default)"}]""",
            choices.ToJsonString());
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await editor.SetAsync(key, "default");
        Assert.Equal("default", editor.Value(key)!.GetValue<string>());
        await editor.SetAsync(key, null);
        Assert.Null(editor.Value(key));
        Assert.Null(AgentStatus.Blocked.EffectiveSound(null, editor.Draft.BlockedStatusSoundName));
    }

    [Theory]
    [InlineData("newSessionDirectory", "home", "Home")]
    [InlineData("newSessionDirectory", "currentSession", "Current session")]
    [InlineData("newSessionDirectory", "custom", "Custom")]
    [InlineData("closeOnShellExit", "graceful", "Successful exit only")]
    [InlineData("closeOnShellExit", "always", "Always")]
    [InlineData("closeOnShellExit", "never", "Never")]
    [InlineData("sidebarThemeStyle", "row", "Whole row")]
    [InlineData("sidebarThemeStyle", "label", "Name label")]
    [InlineData("activeStatusShape", "circle", "Circle")]
    [InlineData("blockedStatusShape", "square", "Square")]
    [InlineData("completedStatusShape", "triangle", "Triangle")]
    [InlineData("activeStatusShape", "diamond", "Diamond")]
    [InlineData("blockedStatusShape", "capsule", "Capsule")]
    [InlineData("completedStatusShape", "star", "Star")]
    public async Task Choice_schema_has_human_labels_and_saves_wire_values(string key, string value, string label)
    {
        var choices = JsonSerializer.SerializeToNode(SettingsEditor.Fields.Single(field => field.Key == key).Choices)!.AsArray();
        Assert.Contains(choices.OfType<JsonObject>(), choice =>
            choice["Value"]?.GetValue<string>() == value && choice["Label"]?.GetValue<string>() == label);
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await editor.SetAsync(key, value);
        Assert.Equal(value, editor.Value(key)?.GetValue<string>() ??
            SettingsEditor.Fields.Single(field => field.Key == key).Default);
    }

    [Fact]
    public async Task Editor_uses_the_coordinator_store_for_fresh_reads_and_writes()
    {
        var otherStore = new SettingsStore(Path.Combine(directory, "other"));
        otherStore.Save(new AppSettings { FontSize = 12 });
        var coordinator = new SettingsCoordinator(Store);
        Store.Save(new AppSettings { FontSize = 23 });
        var editor = new SettingsEditor(coordinator);
        Assert.Equal(23, editor.Draft.FontSize);
        await editor.SetAsync("fontSize", 24d);
        Assert.Equal(24, Store.ReadStrict().Settings.FontSize);
        Assert.Equal(12, otherStore.ReadStrict().Settings.FontSize);
    }

    [Fact]
    public async Task Unsupported_stored_sound_names_survive_other_edits()
    {
        Store.Save(new AppSettings { NotificationSoundName = "Glass", BlockedStatusSoundName = "Submarine" });
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await editor.SetAsync("fontSize", 18d);
        Assert.Equal("Glass", Store.ReadStrict().Settings.NotificationSoundName);
        Assert.Equal("Submarine", Store.ReadStrict().Settings.BlockedStatusSoundName);
        await editor.SetAsync("notificationSoundName", null);
        Assert.Null(Store.ReadStrict().Settings.NotificationSoundName);
        Assert.Equal("Submarine", Store.ReadStrict().Settings.BlockedStatusSoundName);
    }
}
