using System.Text.Json;
using System.Text.Json.Nodes;
using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

public sealed class SettingsUpdateTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("agterm-settings-update-").FullName;
    private string FilePath => Path.Combine(directory, "settings.json");
    private SettingsStore Store => new(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Missing_file_has_a_seeded_snapshot_but_an_existing_empty_object_does_not()
    {
        Assert.Equal("agterm", Store.ReadStrict().Settings.Theme);
        File.WriteAllText(FilePath, "{}");
        Assert.Null(Store.ReadStrict().Settings.Theme);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"fontSize":"large"}""")]
    public void Strict_read_and_save_do_not_recover_or_overwrite_invalid_settings(string content)
    {
        File.WriteAllText(FilePath, content);
        Assert.ThrowsAny<JsonException>(() => Store.ReadStrict());
        Assert.ThrowsAny<JsonException>(() => Store.Save(new AppSettings { FontSize = 18 }));
        Assert.Equal(content, File.ReadAllText(FilePath));
        Assert.Equal("agterm", Store.Load().Theme);
    }

    [Fact]
    public void Update_merges_unrelated_external_changes_and_preserves_unknown_and_deferred_fields()
    {
        File.WriteAllText(FilePath, """
            {"fontSize":14,"theme":"agterm","toolbarMode":"future","compactToolbar":false,
             "autoHideSidebarInactiveWindows":true,"dockBounce":"once","autoFollowAttention":"s30",
             "autoFollowStayOnActive":true,"future":{"nested":[1,true]}}
            """);
        var baseline = Store.ReadStrict();
        var external = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        external["sidebarFontSize"] = 17;
        File.WriteAllText(FilePath, external.ToJsonString());

        var saved = Store.Update(baseline, settings => settings with { FontSize = 19 });

        Assert.Equal(19, saved.Settings.FontSize);
        Assert.Equal(17, saved.Settings.SidebarFontSize);
        var actual = JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
        external["fontSize"] = 19;
        Assert.True(JsonNode.DeepEquals(external, actual));
    }

    [Fact]
    public void An_external_change_to_the_same_field_is_a_conflict_and_writes_nothing()
    {
        Store.Save(new AppSettings { FontSize = 14 });
        var baseline = Store.ReadStrict();
        File.WriteAllText(FilePath, """{"fontSize":20,"future":true}""");

        var error = Assert.Throws<SettingsConflictException>(() =>
            Store.Update(baseline, settings => settings with { FontSize = 18 }));

        Assert.Contains("fontSize", error.Fields);
        Assert.Equal(20, error.Current.Settings.FontSize);
        Assert.Equal("""{"fontSize":20,"future":true}""", File.ReadAllText(FilePath));
    }

    [Fact]
    public void The_three_theme_fields_are_one_conflict_group()
    {
        Store.Save(new AppSettings { Theme = "light", DarkTheme = "dark", FollowSystemAppearance = true });
        var baseline = Store.ReadStrict();
        File.WriteAllText(FilePath, """{"theme":"external","darkTheme":"dark","followSystemAppearance":true}""");

        Assert.Throws<SettingsConflictException>(() =>
            Store.Update(baseline, settings => settings with { DarkTheme = "other" }));
        Assert.Equal("dark", Store.ReadStrict().Settings.DarkTheme);
    }

    [Fact]
    public void An_idempotent_edit_accepts_the_same_external_value()
    {
        Store.Save(new AppSettings { FontSize = 14 });
        var baseline = Store.ReadStrict();
        File.WriteAllText(FilePath, """{"fontSize":18,"future":true}""");
        var saved = Store.Update(baseline, settings => settings with { FontSize = 18 });
        Assert.Equal(18, saved.Settings.FontSize);
        Assert.True(JsonNode.Parse(File.ReadAllText(FilePath))!["future"]!.GetValue<bool>());
    }

    [Fact]
    public void A_corrupt_external_edit_is_not_overwritten_by_an_open_editor()
    {
        var baseline = Store.ReadStrict();
        File.WriteAllText(FilePath, "{broken");
        Assert.ThrowsAny<JsonException>(() => Store.Update(baseline, settings => settings with { FontSize = 18 }));
        Assert.Equal("{broken", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Clearing_an_optional_removes_the_key_and_keeps_unknown_interface_names()
    {
        Store.Save(new AppSettings { FontSize = 18, HiddenInterfaceElements = ["futureElement", "scratch"] });
        var saved = Store.Update(Store.ReadStrict(), settings => settings with { FontSize = null });
        Assert.Null(saved.Settings.FontSize);
        Assert.DoesNotContain("fontSize", File.ReadAllText(FilePath));
        Assert.Equal(["futureElement", "scratch"], saved.Settings.HiddenInterfaceElements);
    }

    [Fact]
    public void The_snapshot_cannot_be_used_to_edit_another_settings_file()
    {
        var other = Path.Combine(directory, "other");
        Assert.Throws<ArgumentException>(() =>
            new SettingsStore(other).Update(Store.ReadStrict(), settings => settings with { FontSize = 18 }));
        Assert.False(Directory.Exists(other));
    }

    [Fact]
    public async Task Store_instances_serialize_updates_without_losing_independent_changes()
    {
        Store.Save(new AppSettings { FontSize = 14 });
        var baseline = Store.ReadStrict();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var first = Task.Run(() => Store.Update(baseline, settings =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            return settings with { FontSize = 18 };
        }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var second = Task.Run(() => Store.Update(baseline, settings => settings with { SidebarFontSize = 16 }));
        release.Set();
        await Task.WhenAll(first, second);
        Assert.Equal(18, Store.ReadStrict().Settings.FontSize);
        Assert.Equal(16, Store.ReadStrict().Settings.SidebarFontSize);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void An_external_edit_during_the_change_callback_is_merged_instead_of_overwritten()
    {
        var baseline = Store.ReadStrict();
        var saved = Store.Update(baseline, settings =>
        {
            File.WriteAllText(FilePath, """{"theme":"external","fontSize":15}""");
            return settings with { SidebarFontSize = 16 };
        });
        Assert.Equal("external", saved.Settings.Theme);
        Assert.Equal(15, saved.Settings.FontSize);
        Assert.Equal(16, saved.Settings.SidebarFontSize);
    }

    [Fact]
    public void The_first_edit_preserves_the_seeded_theme()
    {
        var saved = Store.Update(Store.ReadStrict(), settings => settings with { FontSize = 18 });
        Assert.Equal("agterm", saved.Settings.Theme);
        Assert.Equal("agterm", Store.Load().Theme);
    }

    [Fact]
    public void Save_omits_known_nulls_but_preserves_unknown_fields()
    {
        File.WriteAllText(FilePath, """{"fontSize":null,"future":null}""");
        Store.Save(new AppSettings { FontSize = 18 });
        Store.Save(new AppSettings());
        Assert.Equal("""{"future":null}""", File.ReadAllText(FilePath));
    }
}
