using System.Text.Json;
using System.Text.Json.Nodes;
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
        var deferred = new[] { "toolbarMode", "compactToolbar", "autoHideSidebarInactiveWindows", "dockBounce",
            "autoFollowAttention", "autoFollowStayOnActive" };
        var fields = typeof(AppSettings).GetProperties()
            .Select(property => property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                .OfType<System.Text.Json.Serialization.JsonPropertyNameAttribute>().SingleOrDefault()?.Name)
            .OfType<string>().Except(deferred).Order();
        Assert.Equal(fields, SettingsEditor.Fields.Select(field => field.Key).Order());
        Assert.False(SettingsEditor.Fields.Single(field => field.Key == "inheritGlobalGhosttyConfig").Editable);
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
        Assert.Equal("""[{"Value":null,"Label":"None"},{"Value":"default","Label":"Windows default"},{"Value":"beep","Label":"Beep"}]""",
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
    [InlineData("sidebarThemeStyle", "row", "Row")]
    [InlineData("sidebarThemeStyle", "label", "Label")]
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
