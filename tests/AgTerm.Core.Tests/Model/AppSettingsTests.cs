using AgTerm.Core.Model;

namespace AgTerm.Core.Tests.Model;

/// <summary>
/// AppSettings + SettingsStore — the port of agtermCore AppSettings.swift / SettingsStore.swift
/// (spec §8, parity/surface.json#settings): all-optional camelCase fields, null omitted from the
/// JSON, no version field (optionality IS the forward-compat mechanism), seeded default theme on a
/// fresh install, and the host-free formulas the sidebar consumes.
/// </summary>
public class AppSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("agterm-settings-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FilePath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Load_with_no_file_seeds_the_default_theme()
    {
        var settings = new SettingsStore(_dir).Load();
        Assert.Equal("agterm", settings.Theme);
        Assert.Null(settings.FontFamily);
        Assert.Null(settings.SidebarFontSize);
    }

    [Fact]
    public void Load_with_corrupt_json_recovers_the_seeded_default()
    {
        File.WriteAllText(FilePath, "{ not json");
        var settings = new SettingsStore(_dir).Load();
        Assert.Equal("agterm", settings.Theme);
    }

    [Fact]
    public void An_existing_file_without_theme_is_not_silently_rethemed()
    {
        File.WriteAllText(FilePath, """{"fontSize":15}""");
        var settings = new SettingsStore(_dir).Load();
        Assert.Null(settings.Theme);
        Assert.Equal(15, settings.FontSize);
    }

    [Fact]
    public void Unknown_keys_do_not_fail_the_load()
    {
        File.WriteAllText(FilePath, """{"fontSize":15,"someFutureKey":{"nested":true}}""");
        Assert.Equal(15, new SettingsStore(_dir).Load().FontSize);
    }

    [Fact]
    public void Save_omits_null_fields_and_uses_camelCase()
    {
        var store = new SettingsStore(_dir);
        store.Save(new AppSettings { Theme = "agterm", SidebarBackgroundShift = 7 });
        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"theme\":\"agterm\"", json);
        Assert.Contains("\"sidebarBackgroundShift\":7", json);
        Assert.DoesNotContain("fontFamily", json);
        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain("version", json);
    }

    [Fact]
    public void Save_writes_sidebar_theme_colors_only_when_set()
    {
        var store = new SettingsStore(_dir);
        store.Save(new AppSettings { SidebarThemeColors = false });
        Assert.Contains("\"sidebarThemeColors\":false", File.ReadAllText(FilePath));

        store.Save(new AppSettings());
        Assert.DoesNotContain("sidebarThemeColors", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Sidebar_theme_colors_are_on_unless_turned_off()
    {
        Assert.True(new AppSettings().ShowSidebarThemeColors());
        Assert.True(new AppSettings { SidebarThemeColors = true }.ShowSidebarThemeColors());
        Assert.False(new AppSettings { SidebarThemeColors = false }.ShowSidebarThemeColors());
    }

    [Fact]
    public void Settings_that_differ_only_in_sidebar_theme_colors_are_not_equal()
    {
        Assert.NotEqual(new AppSettings(), new AppSettings { SidebarThemeColors = false });
    }

    [Theory]
    [InlineData(null, SidebarThemeStyle.Row)]
    [InlineData("row", SidebarThemeStyle.Row)]
    [InlineData("swatch", SidebarThemeStyle.Swatch)]
    [InlineData("label", SidebarThemeStyle.Label)]
    [InlineData("Label", SidebarThemeStyle.Row)]
    [InlineData("swatch ", SidebarThemeStyle.Row)]
    [InlineData("stripe", SidebarThemeStyle.Row)]
    [InlineData("", SidebarThemeStyle.Row)]
    public void Sidebar_theme_style_reads_exact_values_and_falls_back_to_row(string? raw, SidebarThemeStyle expected) =>
        Assert.Equal(expected, new AppSettings { SidebarThemeStyle = raw }.EffectiveSidebarThemeStyle());

    [Fact]
    public void Settings_that_differ_only_in_sidebar_theme_style_are_not_equal() =>
        Assert.NotEqual(new AppSettings(), new AppSettings { SidebarThemeStyle = "swatch" });

    [Fact]
    public void Save_writes_sidebar_theme_style_only_when_set()
    {
        var store = new SettingsStore(_dir);
        store.Save(new AppSettings { SidebarThemeStyle = "label" });
        Assert.Contains("\"sidebarThemeStyle\":\"label\"", File.ReadAllText(FilePath));

        store.Save(new AppSettings());
        Assert.DoesNotContain("sidebarThemeStyle", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Round_trip_preserves_every_field_kind()
    {
        var store = new SettingsStore(_dir);
        var settings = new AppSettings
        {
            FontFamily = "Cascadia Mono",
            FontSize = 13.5,
            Theme = "3024 Night",
            DarkTheme = "Adwaita Dark",
            FollowSystemAppearance = true,
            BackgroundOpacity = 0.9,
            BackgroundBlur = 20,
            NotificationsEnabled = false,
            NotificationBadgeEnabled = false,
            ToolbarMode = "hidden",
            CompactToolbar = false,
            ActiveStatusColorHex = "#112233",
            BlockedStatusColorHex = "#445566",
            CompletedStatusColorHex = "#778899",
            ActiveStatusShape = "star",
            BlockedStatusShape = "triangle",
            CompletedStatusShape = "diamond",
            ConfigDirectory = "C:\\cfg",
            MouseScrollMultiplier = 2,
            InactivePaneMuteStrength = 3,
            SidebarBackgroundShift = 8,
            RestoreRunningCommand = true,
            InheritGlobalGhosttyConfig = true,
            AttentionButtonEnabled = true,
            DockBounce = "once",
            NotificationSoundName = "Glass",
            BlockedStatusSoundName = "Basso",
            RightClickPaste = false,
            NewSessionDirectory = "custom",
            NewSessionCustomDirectory = "C:\\work",
            ConfirmCloseSession = true,
            CloseGraceUndoEnabled = false,
            AutoFollowAttention = "s30",
            AutoFollowStayOnActive = true,
            SidebarFontSize = 15,
            HiddenInterfaceElements = new[] { "flaggedView", "someFutureElement" },
            AutoHideSidebarInactiveWindows = true,
            SidebarThemeColors = false,
            SidebarThemeStyle = "swatch",
            RestoreScrollback = false,
        };
        store.Save(settings);
        Assert.Equal(settings, new SettingsStore(_dir).Load());
    }

    [Fact]
    public void Restore_scrollback_is_on_unless_explicitly_disabled()
    {
        Assert.True(new AppSettings().EffectiveRestoreScrollback);
        Assert.True(new AppSettings { RestoreScrollback = true }.EffectiveRestoreScrollback);
        Assert.False(new AppSettings { RestoreScrollback = false }.EffectiveRestoreScrollback);
        Assert.NotEqual(new AppSettings(), new AppSettings { RestoreScrollback = false });

        var store = new SettingsStore(_dir);
        store.Save(new AppSettings { RestoreScrollback = false });
        Assert.Contains("\"restoreScrollback\":false", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Mute_opacity_scales_and_clamps()
    {
        Assert.Equal(0.0, AppSettings.MuteOpacity(0));
        Assert.Equal(0.4, AppSettings.MuteOpacity(5), 10);
        Assert.Equal(0.8, AppSettings.MuteOpacity(10), 10);
        Assert.Equal(0.0, AppSettings.MuteOpacity(-3));
        Assert.Equal(0.8, AppSettings.MuteOpacity(42), 10);
    }

    [Fact]
    public void Sidebar_shift_is_signed_from_the_neutral_center()
    {
        Assert.Equal(0.0, AppSettings.SidebarShiftAmount(5));
        Assert.Equal(-0.30, AppSettings.SidebarShiftAmount(0), 10);
        Assert.Equal(0.30, AppSettings.SidebarShiftAmount(10), 10);
        Assert.Equal(-0.30, AppSettings.SidebarShiftAmount(-9), 10);
    }

    [Fact]
    public void Sidebar_font_size_clamps_and_maps_to_row_height()
    {
        Assert.Equal(9, AppSettings.ClampSidebarFontSize(1));
        Assert.Equal(20, AppSettings.ClampSidebarFontSize(99));
        Assert.Equal(28, AppSettings.SidebarRowHeight(13)); // the historical default row
        Assert.Equal(35, AppSettings.SidebarRowHeight(20));
    }

    [Fact]
    public void Effective_status_shape_resolves_known_raws_and_ignores_unknown()
    {
        var settings = new AppSettings
        {
            ActiveStatusShape = "star",
            BlockedStatusShape = "not-a-shape",
        };
        Assert.Equal(StatusShape.Star, settings.EffectiveStatusShape(AgentStatus.Active));
        Assert.Null(settings.EffectiveStatusShape(AgentStatus.Blocked));
        Assert.Null(settings.EffectiveStatusShape(AgentStatus.Completed));
        Assert.Null(settings.EffectiveStatusShape(AgentStatus.Idle));
    }

    [Fact]
    public void Status_color_prefers_override_then_settings_then_defaults()
    {
        var settings = new AppSettings { BlockedStatusColorHex = "#123456" };
        Assert.Equal("#ABCDEF", settings.EffectiveStatusColorHex(AgentStatus.Blocked, "#ABCDEF"));
        Assert.Equal("#123456", settings.EffectiveStatusColorHex(AgentStatus.Blocked, "no-hex"));
        Assert.Equal("#123456", settings.EffectiveStatusColorHex(AgentStatus.Blocked, null));
        Assert.Equal("#DBD9E6", settings.EffectiveStatusColorHex(AgentStatus.Active, null));
        Assert.Equal("#FF9500", new AppSettings().EffectiveStatusColorHex(AgentStatus.Blocked, null));
        Assert.Equal("#28CD41", new AppSettings().EffectiveStatusColorHex(AgentStatus.Completed, null));
    }

    [Fact]
    public void New_session_cwd_resolves_the_mode_with_home_fallbacks()
    {
        var home = "C:\\home";
        Assert.Equal(home, new AppSettings().ResolveNewSessionCwd("C:\\cur", home));
        Assert.Equal("C:\\cur", new AppSettings { NewSessionDirectory = "currentSession" }
            .ResolveNewSessionCwd("C:\\cur", home));
        Assert.Equal(home, new AppSettings { NewSessionDirectory = "currentSession" }
            .ResolveNewSessionCwd(null, home));
        Assert.Equal("C:\\work", new AppSettings
        {
            NewSessionDirectory = "custom",
            NewSessionCustomDirectory = "C:\\work",
        }.ResolveNewSessionCwd(null, home));
        Assert.Equal(home, new AppSettings { NewSessionDirectory = "future-mode" }
            .ResolveNewSessionCwd("C:\\cur", home));
    }

    [Fact]
    public void Interface_elements_carry_upstreams_raw_values_in_order()
    {
        Assert.Equal(
            ["sidebarToggle", "sessionName", "windowName", "recentSessions", "scratch", "split",
             "dashboard", "quickTerminal", "newWorkspace", "newSession", "flaggedView", "workspaceAddSession"],
            Enum.GetValues<InterfaceElement>().Select(e => e.RawValue()));
    }

    [Fact]
    public void Every_interface_element_is_shown_by_default()
    {
        Assert.All(Enum.GetValues<InterfaceElement>(),
            e => Assert.False(new AppSettings().IsInterfaceElementHidden(e)));
    }

    [Fact]
    public void A_listed_raw_value_hides_only_its_element()
    {
        var settings = new AppSettings { HiddenInterfaceElements = ["workspaceAddSession", "someFutureElement"] };

        Assert.True(settings.IsInterfaceElementHidden(InterfaceElement.WorkspaceAddSession));
        Assert.False(settings.IsInterfaceElementHidden(InterfaceElement.NewSession));
    }

    // upstream InterfaceElement.titlebarGroupDividers, with upstream's cases: A = recent sessions +
    // attention, B = scratch + split, C = dashboard + quick terminal
    [Theory]
    [InlineData(1, 2, 2, false, true)]
    [InlineData(1, 1, 2, false, false)]
    [InlineData(2, 2, 2, true, true)]
    [InlineData(2, 0, 2, false, true)]
    [InlineData(2, 1, 2, false, false)]
    [InlineData(2, 2, 1, true, false)]
    [InlineData(0, 2, 2, false, true)]
    [InlineData(0, 0, 2, false, false)]
    [InlineData(2, 2, 0, true, false)]
    [InlineData(0, 0, 0, false, false)]
    public void Title_bar_dividers_sit_only_between_full_groups(
        int countA, int countB, int countC, bool afterA, bool afterB)
    {
        Assert.Equal((afterA, afterB), InterfaceElementExtensions.TitlebarGroupDividers(countA, countB, countC));
    }

    [Fact]
    public void Hidden_interface_elements_match_the_raw_value_exactly()
    {
        var settings = new AppSettings { HiddenInterfaceElements = ["WorkspaceAddSession"] };

        Assert.False(settings.IsInterfaceElementHidden(InterfaceElement.WorkspaceAddSession));
    }
}
