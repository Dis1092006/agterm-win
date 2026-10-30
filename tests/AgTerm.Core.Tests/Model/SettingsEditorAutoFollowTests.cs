using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class SettingsEditorAutoFollowTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-follow-settings-").FullName;
    private SettingsStore Store => new(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Agent_status_offers_pinned_timeouts_and_the_inverted_running_session_toggle()
    {
        var timeout = Assert.Single(SettingsEditor.Fields, field => field.Key == "autoFollowAttention");
        Assert.Equal("Agent Status", timeout.Tab);
        Assert.Equal("Auto-follow blocked sessions", timeout.Label);
        Assert.Equal(SettingControl.Choice, timeout.Control);
        Assert.Null(timeout.Default);
        Assert.Equal([null, "s5", "s10", "s30", "s60", "m5"],
            timeout.Choices!.Select(choice => choice.Value));
        Assert.Equal(["Disabled", "5 sec idle", "10 sec idle", "30 sec idle", "60 sec idle", "5 min idle"],
            timeout.Choices!.Select(choice => choice.Label));
        var stay = Assert.Single(SettingsEditor.Fields, field => field.Key == "autoFollowStayOnActive");
        Assert.Equal("Agent Status", stay.Tab);
        Assert.Equal("Auto-follow away from a running session", stay.Label);
        Assert.Equal(SettingControl.Toggle, stay.Control);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("off", null)]
    [InlineData("s5", "s5")]
    [InlineData("s10", "s10")]
    [InlineData("s30", "s30")]
    [InlineData("s60", "s60")]
    [InlineData("m5", "m5")]
    [InlineData("future", null)]
    [InlineData("S5", null)]
    public async Task Timeout_picker_reads_effectively_without_rewriting_raw_values(string? raw, string? selected)
    {
        Store.Save(new AppSettings { AutoFollowAttention = raw, AutoFollowStayOnActive = true });
        var original = File.ReadAllText(Store.FilePath);
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        Assert.Equal(selected, editor.ChoiceValue("autoFollowAttention"));
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(original, File.ReadAllText(Store.FilePath));
        await editor.SetAsync("fontSize", 18d);
        Assert.Equal(raw, Store.ReadStrict().Settings.AutoFollowAttention);
        Assert.True(Store.ReadStrict().Settings.AutoFollowStayOnActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("s5")]
    [InlineData("s10")]
    [InlineData("s30")]
    [InlineData("s60")]
    [InlineData("m5")]
    public async Task Picking_a_timeout_preserves_the_independent_running_session_override(string? value)
    {
        Store.Save(new AppSettings { AutoFollowAttention = "future", AutoFollowStayOnActive = true });
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        await editor.SetAsync("autoFollowAttention", value);
        Assert.Equal(value, Store.ReadStrict().Settings.AutoFollowAttention);
        Assert.True(Store.ReadStrict().Settings.AutoFollowStayOnActive);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Running_session_toggle_is_inverted_and_default_removes_the_override(bool? raw, bool shown)
    {
        Store.Save(new AppSettings { AutoFollowAttention = "s5", AutoFollowStayOnActive = raw });
        var editor = new SettingsEditor(new SettingsCoordinator(Store));
        Assert.True(editor.IsEnabled("autoFollowStayOnActive"));
        Assert.Equal(shown, editor.ToggleValue("autoFollowStayOnActive"));
        await editor.SetToggleAsync("autoFollowStayOnActive", false);
        Assert.True(Store.ReadStrict().Settings.AutoFollowStayOnActive);
        await editor.SetToggleAsync("autoFollowStayOnActive", true);
        Assert.Null(Store.ReadStrict().Settings.AutoFollowStayOnActive);
        await editor.SetAsync("autoFollowAttention", null);
        Assert.False(editor.IsEnabled("autoFollowStayOnActive"));
        Assert.True(editor.ToggleValue("autoFollowStayOnActive"));
    }
}
