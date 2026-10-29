using AgTerm.Core.Model;
using AgTerm.App;

namespace AgTerm.Xterm.Tests;

public sealed class SettingsWindowWiringTests
{
    [Fact]
    public void App_owns_one_nonpersisted_window_and_every_opener_uses_it()
    {
        var app = Source("App.xaml.cs");
        Assert.Contains("SettingsWindow? _settingsWindow", app);
        Assert.Contains("_settingsWindow ??=", app);
        Assert.Contains("_settingsWindow.ShowGeneral();", app);
        Assert.Contains("_settingsWindow?.Close();", app);
        var window = Source("MainWindow.xaml.cs");
        Assert.Contains("SettingsShortcuts.AddToManifest(keymap, manifest)", window);
        Assert.Contains("SettingsShortcuts.Opens(hotkeyState.Keymap, physical)", window);
        Assert.Contains("((App)Application.Current).OpenSettings", window);
        Assert.Contains("Click=\"OnSettingsClick\"", Source("MainWindow.xaml"));
    }

    [Fact]
    public void Settings_controls_cover_the_schema_and_use_shared_editor_and_shortcut_catalog()
    {
        var code = Source("SettingsWindow.cs");
        Assert.Contains("SettingsEditor.Tabs", code);
        Assert.Contains("SettingsEditor.Fields", code);
        foreach (var control in Enum.GetValues<SettingControl>())
            Assert.Contains($"SettingControl.{control}", code);
        Assert.Contains("KeybindingsController", code);
        Assert.Contains("FolderPicker", code);
        Assert.Contains("ColorPicker", code);
        Assert.Contains("editor.SetFollowingAsync", code);
        Assert.Contains("editor.ResetAgentStatusAsync", code);
        Assert.Contains("coordinator.Changed -= OnSettingsChanged", code);
        Assert.Contains("SelectedIndex = 0", code);
        Assert.Contains("new SettingsEditor(coordinator)", code);
        Assert.DoesNotContain("new SettingsStore()", code);
        Assert.Contains("WindowsNotificationSound.TryPlay", code);
        Assert.Contains("InstalledFonts.MonospacedFamilies()", code);
    }

    [Fact]
    public void Settings_combo_boxes_align_left_with_other_field_controls()
    {
        Assert.Contains("var combo = new ComboBox { MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left,",
            Source("SettingsWindow.cs"));
    }

    [Fact]
    public void Shortcuts_show_native_global_hotkey_errors_without_changing_keymap_diagnostics()
    {
        Assert.Contains("global-hotkey-error", Source("SettingsWindow.cs"));
        Assert.Contains("() => _quickWindow?.GlobalHotkeyError", Source("App.xaml.cs"));
    }

    [Fact]
    public void Quick_settings_preserve_font_zoom_until_the_base_font_changes_and_failed_pages_are_released()
    {
        var code = Source("QuickTerminalWindow.cs");
        Assert.Contains("if (resetFontSize || _appliedBaseFontSize != fontSize)", code);
        Assert.Contains("SettingsApplyKind.Edit or SettingsApplyKind.Reload", code);
        Assert.Contains("preservePaneFontSizes: !resetFontSize", code);
        Assert.DoesNotContain("token, presentation.Theme", code);
        Assert.Contains("ResetPage();", code);
        Assert.Contains("ReferenceEquals(_view, view)", code);
    }

    [Fact]
    public void Detached_quick_dispatches_the_application_bindings_its_manifest_intercepts()
    {
        Assert.Contains("page.CustomCommandPressed +=", Source("QuickTerminalWindow.cs"));
        Assert.Contains("WindowCommandRouter.ActionImplementations", Source("QuickTerminalWindow.cs"));
        Assert.Contains("page.SwitcherKeyPressed +=", Source("QuickTerminalWindow.cs"));
        Assert.Contains("page.KeyboardShortcutsRequested +=", Source("QuickTerminalWindow.cs"));
        Assert.Contains("ExecuteQuickCustomCommandAsync", Source("App.xaml.cs"));
        Assert.Contains("ExecuteBuiltinHotkey", Source("MainWindow.xaml.cs"));
        Assert.Contains("CustomCommandInvocationResolver.Resolve", Source("MainWindow.xaml.cs"));
    }

    [Fact]
    public void Installed_font_choices_are_sorted_unique_and_include_windows_monospaced_fonts()
    {
        var fonts = InstalledFonts.MonospacedFamilies();
        Assert.NotEmpty(fonts);
        Assert.Equal(fonts.Order(StringComparer.CurrentCultureIgnoreCase), fonts);
        Assert.Equal(fonts.Distinct(StringComparer.CurrentCultureIgnoreCase).Count(), fonts.Count);
        Assert.Contains("Consolas", fonts);
        Assert.Contains("Courier New", fonts);
        Assert.DoesNotContain("Arial", fonts);
        Assert.DoesNotContain(fonts, font => font.StartsWith('@'));
    }

    [Fact]
    public void Settings_chrome_uses_applied_global_theme_and_observes_windows_appearance()
    {
        var code = Source("SettingsWindow.cs");
        Assert.Contains("LiveThemeSettings.Resolve(coordinator.State.Snapshot.Settings", code);
        Assert.Contains("presentation.UseDarkChrome", code);
        Assert.Contains("root.RequestedTheme", code);
        Assert.Contains("TitleBar.PreferredTheme", code);
        Assert.Contains("uiSettings.ColorValuesChanged += OnSystemAppearanceChanged", code);
        Assert.Contains("uiSettings.ColorValuesChanged -= OnSystemAppearanceChanged", code);
    }

    [Fact]
    public void Detached_quick_applies_the_focus_failure_policy_instead_of_reporting_native_denials_directly()
    {
        var code = Source("QuickTerminalWindow.cs");
        Assert.Contains("QuickPanelFocus.ActivationCompleted(", code);
        Assert.Contains("QuickPanelFocus.RestoreDenied(", code);
        Assert.DoesNotContain("Report(\"Quick terminal focus\", \"Windows denied", code);
    }

    private static string Source(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, "src", "AgTerm.App", file));
    }
}
