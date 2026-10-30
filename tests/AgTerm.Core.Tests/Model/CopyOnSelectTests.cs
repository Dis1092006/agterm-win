using System.Text.Json;
using System.Text.Json.Nodes;
using AgTerm.Core.Model;
using AgTerm.Core.Protocol;

namespace AgTerm.Core.Tests.Model;

/// <summary>copyOnSelect, a Windows-only setting: where a mouse selection is copied
/// (docs/PLATFORM-NOTES.md "Selection buffer and middle-click paste").</summary>
public sealed class CopyOnSelectTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-copy-on-select-").FullName;
    private SettingsStore Store => new(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Theory]
    [InlineData(null, CopyOnSelectMode.Selection)]
    [InlineData("selection", CopyOnSelectMode.Selection)]
    [InlineData("clipboard", CopyOnSelectMode.Clipboard)]
    [InlineData("off", CopyOnSelectMode.Off)]
    [InlineData("someFutureMode", CopyOnSelectMode.Selection)]
    [InlineData("Clipboard", CopyOnSelectMode.Selection)]
    public void The_mode_reads_exact_values_and_falls_back_to_the_selection_buffer(string? raw, CopyOnSelectMode mode)
    {
        Assert.Equal(mode, new AppSettings { CopyOnSelect = raw }.EffectiveCopyOnSelect());
    }

    [Fact]
    public void The_mode_round_trips_and_is_omitted_when_absent()
    {
        var settings = ControlJson.Deserialize<AppSettings>("""{"copyOnSelect":"clipboard","fontSize":15}""")!;

        Assert.Equal("clipboard", settings.CopyOnSelect);
        Assert.Contains("\"copyOnSelect\":\"clipboard\"", ControlJson.Serialize(settings));
        Assert.DoesNotContain("copyOnSelect", ControlJson.Serialize(new AppSettings()));
        Assert.NotEqual(new AppSettings(), new AppSettings { CopyOnSelect = "off" });
    }

    [Theory]
    [InlineData("true", "selection")]
    [InlineData("false", "off")]
    public void A_json_bool_reads_as_a_mode_so_a_windows_terminal_value_does_not_break_the_file(string json, string mode)
    {
        File.WriteAllText(Store.FilePath, $$"""{"copyOnSelect":{{json}},"fontSize":15}""");

        var settings = Store.ReadStrict().Settings;

        Assert.Equal(mode, settings.CopyOnSelect);
        Assert.Equal(15, settings.FontSize);
    }

    [Fact]
    public void Another_json_type_still_fails_the_file_like_every_other_field()
    {
        File.WriteAllText(Store.FilePath, """{"copyOnSelect":3}""");

        Assert.Throws<JsonException>(() => Store.ReadStrict());
    }

    [Fact]
    public void The_settings_field_follows_right_click_paste_in_general()
    {
        var keys = SettingsEditor.Fields.Select(field => field.Key).ToList();
        var field = Assert.Single(SettingsEditor.Fields, field => field.Key == "copyOnSelect");

        Assert.Equal(keys.IndexOf("rightClickPaste") + 1, keys.IndexOf("copyOnSelect"));
        Assert.Equal("General", field.Tab);
        Assert.Equal("Copy on select", field.Label);
        Assert.Equal(SettingControl.Choice, field.Control);
        Assert.Equal("selection", field.Default);
        Assert.Equal([null, "selection", "clipboard", "off"], field.Choices!.Select(choice => choice.Value));
        Assert.Equal(["Default (Selection buffer)", "Selection buffer", "Selection buffer and clipboard", "Off"],
            field.Choices!.Select(choice => choice.Label));
        Assert.Equal("Middle-click pastes the selection buffer, or the clipboard when the selection is also copied there. Ctrl+C and right-click use the clipboard.",
            field.Hint);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("selection", "selection")]
    [InlineData("clipboard", "clipboard")]
    [InlineData("off", "off")]
    [InlineData("future", null)]
    public async Task The_picker_shows_a_known_value_and_reads_an_unknown_one_as_default(string? raw, string? selected)
    {
        Store.Save(new AppSettings { CopyOnSelect = raw });
        var original = File.ReadAllText(Store.FilePath);
        var editor = new SettingsEditor(new SettingsCoordinator(Store));

        Assert.Equal(selected, editor.ChoiceValue("copyOnSelect"));
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllText(Store.FilePath));
    }

    [Fact]
    public async Task The_editor_saves_a_mode_and_removes_the_key_for_the_default()
    {
        var editor = new SettingsEditor(new SettingsCoordinator(Store));

        await editor.SetAsync("copyOnSelect", "clipboard");
        Assert.Equal("clipboard", Store.ReadStrict().Settings.CopyOnSelect);
        await editor.SetAsync("copyOnSelect", "selection");
        Assert.False(JsonNode.Parse(File.ReadAllText(Store.FilePath))!.AsObject().ContainsKey("copyOnSelect"));
        await Assert.ThrowsAsync<ArgumentException>(() => editor.SetAsync("copyOnSelect", "primary"));
    }
}
