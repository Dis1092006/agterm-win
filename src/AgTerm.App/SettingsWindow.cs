using AgTerm.Core.Keymap;
using AgTerm.Core.Model;
using AgTerm.Xterm;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace AgTerm.App;

internal sealed class SettingsWindow : Window
{
    private readonly SettingsCoordinator coordinator;
    private readonly SettingsEditor editor;
    private readonly SoundCatalog soundCatalog;
    private readonly WindowsNotificationSound notificationSounds;
    private IReadOnlyList<CatalogSound> soundChoices = [];
    private readonly UISettings uiSettings = new();
    private readonly Grid root = new() { Background = new SolidColorBrush(Colors.Transparent) };
    private readonly AppWindow appWindow;
    private readonly Func<Task<IReadOnlyList<KeybindingGroup>>> shortcuts;
    private readonly Func<Task> reloadKeymap;
    private readonly Func<string?> globalHotkeyError;
    private readonly TabView tabs = new() { IsAddTabButtonVisible = false, CanDragTabs = false,
        CanReorderTabs = false, TabWidthMode = TabViewWidthMode.SizeToContent };
    private readonly InfoBar error = new() { IsClosable = false, Severity = InfoBarSeverity.Error };
    private readonly TextBlock status = new() { Text = "Changes save and apply automatically.", TextWrapping = TextWrapping.Wrap };
    private readonly Button retry = new() { Content = "Retry unsaved changes" };
    private readonly Button reload = new() { Content = "Reload from file" };
    private StackPanel shortcutRows = new() { Spacing = 8 };
    private readonly KeybindingsController keybindings = new();
    private IReadOnlyList<string>? fontFamilies;
    private bool busy;
    private bool closed;

    public SettingsWindow(SettingsCoordinator coordinator,
        Func<Task<IReadOnlyList<KeybindingGroup>>> shortcuts, Func<Task> reloadKeymap,
        Func<string?> globalHotkeyError)
    {
        this.coordinator = coordinator;
        this.shortcuts = shortcuts;
        this.reloadKeymap = reloadKeymap;
        this.globalHotkeyError = globalHotkeyError;
        editor = new SettingsEditor(coordinator);
        soundCatalog = new SoundCatalog(coordinator.StateDirectory);
        notificationSounds = new WindowsNotificationSound(soundCatalog);
        Title = "Settings - agterm-win";
        SystemBackdrop = new MicaBackdrop();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(error);
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);
        foreach (var name in SettingsEditor.Tabs)
            tabs.TabItems.Add(new TabViewItem { Header = name, IsClosable = false, Tag = name,
                Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
        tabs.SelectionChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderTab);
        tabs.Loaded += async (_, _) => await LoadSoundsAsync();
        tabs.SelectedIndex = 0;
        var footer = new Grid { ColumnSpacing = 12, Margin = new Thickness(20) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.Children.Add(reload);
        Grid.SetColumn(retry, 1);
        footer.Children.Add(retry);
        Grid.SetColumn(status, 2);
        footer.Children.Add(status);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        reload.Click += async (_, _) => await RunAsync(editor.ReloadAsync);
        retry.Click += async (_, _) => await RunAsync(editor.SaveAsync);
        Identify(reload, "settings-reload", "Reload from file");
        Identify(retry, "settings-retry", "Retry unsaved changes");
        Content = root;
        coordinator.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            closed = true;
            coordinator.Changed -= OnSettingsChanged;
            uiSettings.ColorValuesChanged -= OnSystemAppearanceChanged;
        };
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle));
        appWindow.Resize(new SizeInt32(1000, 850));
        ApplyThemeChrome();
        uiSettings.ColorValuesChanged += OnSystemAppearanceChanged;
        RenderTab();
        ShowError();
    }

    private async Task LoadSoundsAsync()
    {
        try
        {
            var choices = await Task.Run(() => soundCatalog.Sounds);
            if (closed) return;
            soundChoices = choices;
            RenderTab();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning($"Could not load sound choices: {ex.Message}");
            error.Message = ex.Message;
            error.IsOpen = true;
        }
    }

    public void ShowGeneral()
    {
        tabs.SelectedIndex = 0;
        _ = LoadSoundsAsync();
        Activate();
        WindowForeground.Raise(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    private void OnSettingsChanged(SettingsApplicationState state)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (closed) return;
            if (!state.IsApplying) ApplyThemeChrome();
            if (busy) return;
            editor.Observe(state);
            if (!state.IsApplying) RenderTab();
            ShowError();
        }))
            System.Diagnostics.Trace.TraceError("Settings window could not receive the shared settings state.");
    }

    private void RenderTab() => RenderTab(FocusedSettingId());

    private void RenderTab(string? focusId)
    {
        if (closed || tabs.SelectedIndex < 0 ||
            tabs.TabItems[tabs.SelectedIndex] is not TabViewItem tab) return;
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(24), MaxWidth = 850,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var field in SettingsEditor.Fields.Where(field => field.Tab == (string)tab.Tag))
            panel.Children.Add(CreateField(field));
        if ((string)tab.Tag == "Agent Status")
        {
            var reset = Button("Reset to defaults", "settings-status-reset");
            reset.Click += async (_, _) => await RunAsync(editor.ResetAgentStatusAsync);
            panel.Children.Add(reset);
        }
        if ((string)tab.Tag == "Key Mapping")
        {
            shortcutRows = new StackPanel { Spacing = 8 };
            var refresh = Button("Reload keymap", "settings-keymap-reload");
            refresh.Click += async (_, _) => await RunAsync(async () => { await reloadKeymap(); await LoadShortcutsAsync(); });
            panel.Children.Add(refresh);
            var search = new TextBox { PlaceholderText = "Search keyboard shortcuts", Text = keybindings.Query };
            Identify(search, "settings-shortcut-search", "Search keyboard shortcuts");
            search.TextChanged += (_, _) => { keybindings.SetQuery(search.Text); RenderShortcuts(); };
            panel.Children.Add(search);
            panel.Children.Add(shortcutRows);
            _ = LoadShortcutsWithErrorAsync();
        }
        ((ScrollViewer)tab.Content).Content = panel;
        if (focusId is not null)
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!closed && FindControl((DependencyObject)Content, focusId) is { IsEnabled: true } control)
                    control.Focus(FocusState.Keyboard);
            });
    }

    private FrameworkElement CreateField(SettingField field)
    {
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(new TextBlock { Text = field.Label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Children.Add(row);
        var value = editor.Value(field.Key);
        var id = "settings-" + field.Key;
        switch (field.Control)
        {
            case SettingControl.Toggle:
                var toggle = new ToggleSwitch { IsOn = editor.ToggleValue(field.Key),
                    IsEnabled = editor.IsEnabled(field.Key),
                    HorizontalAlignment = HorizontalAlignment.Left };
                Identify(toggle, id, field.Label);
                toggle.Toggled += async (_, _) =>
                {
                    if (closed || !toggle.IsLoaded) return;
                    await RunAsync(() => field.Key == "followSystemAppearance"
                        ? editor.SetFollowingAsync(toggle.IsOn, SystemDark())
                        : editor.SetToggleAsync(field.Key, toggle.IsOn));
                };
                row.Children.Add(toggle);
                break;
            case SettingControl.Number:
                var number = new NumberBox { Value = value?.GetValue<double>() ?? Convert.ToDouble(field.Default ??
                    double.NaN), Minimum = field.Minimum, Maximum = field.Maximum, MaxWidth = 220,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    SmallChange = field.Maximum == 1 ? 0.05 : 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                    PlaceholderText = "Theme default" };
                Identify(number, id, field.Label);
                async Task CommitNumber()
                {
                    if (busy || closed) return;
                    var original = editor.Value(field.Key)?.GetValue<double>() ??
                        Convert.ToDouble(field.Default ?? double.NaN);
                    if (number.Value.Equals(original)) return;
                    await RunAsync(() => editor.SetAsync(field.Key, double.IsNaN(number.Value) ? null : number.Value));
                }
                number.ValueChanged += async (_, _) => await CommitNumber();
                row.Children.Add(number);
                break;
            case SettingControl.Choice:
            case SettingControl.Font:
            case SettingControl.Theme:
                var choices = field.Control switch
                {
                    SettingControl.Font => (fontFamilies ??= InstalledFonts.MonospacedFamilies())
                        .Select(name => new SettingChoice(name, name)).ToArray(),
                    SettingControl.Theme => AgTerm.Xterm.ThemeCatalog.Default.Names
                        .Select(name => new SettingChoice(name, name)).ToArray(),
                    _ when field.Key is "notificationSoundName" or "blockedStatusSoundName" =>
                        (field.Choices ?? []).Concat(soundChoices.Select(sound =>
                            new SettingChoice(sound.Name, sound.Name + (sound.IsUserSound ? " (user)" : "")))).ToArray(),
                    _ => field.Choices ?? [],
                };
                var selected = editor.ChoiceValue(field.Key);
                var combo = new ComboBox { MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left,
                    IsEditable = field.Control == SettingControl.Font,
                    IsTextSearchEnabled = true, PlaceholderText = "Default" };
                var options = new List<SettingChoice>();
                if (field.Control != SettingControl.Choice &&
                    (field.Control != SettingControl.Theme || editor.Draft.FollowSystemAppearance != true))
                    options.Add(new(null, field.Default is null ? "Default" : $"Default ({field.Default})"));
                options.AddRange(choices);
                var isSound = field.Key is "notificationSoundName" or "blockedStatusSoundName";
                var canonicalSound = soundChoices.FirstOrDefault(sound =>
                    string.Equals(sound.Name, selected, StringComparison.OrdinalIgnoreCase))?.Name
                    ?? (selected?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true
                        ? soundChoices.FirstOrDefault(sound =>
                            string.Equals(sound.Name, selected[..^4], StringComparison.OrdinalIgnoreCase))?.Name
                        : null)
                    ?? selected;
                bool Matches(SettingChoice choice) => isSound
                    ? string.Equals(choice.Value, canonicalSound, StringComparison.OrdinalIgnoreCase)
                    : choice.Value == selected;
                if (selected is not null && !choices.Any(Matches))
                    options.Add(new(selected, selected + (isSound ? " (unavailable)" : " (stored)")));
                combo.ItemsSource = options;
                combo.DisplayMemberPath = nameof(SettingChoice.Label);
                combo.SelectedItem = options.FirstOrDefault(Matches);
                Identify(combo, id, field.Label);
                combo.SelectionChanged += async (_, _) =>
                {
                    if (closed || !combo.IsLoaded) return;
                    if (combo.SelectedItem is SettingChoice choice)
                        await RunAsync(() => editor.SetAsync(field.Key, choice.Value), id);
                };
                if (combo.IsEditable)
                    combo.TextSubmitted += async (_, args) =>
                    {
                        args.Handled = true;
                        if (closed || !combo.IsLoaded) return;
                        var text = args.Text.Trim();
                        var option = options.FirstOrDefault(choice => choice.Label == text);
                        var family = option is null ? text : option.Value;
                        if (family != editor.Value(field.Key)?.GetValue<string>())
                            await RunAsync(() => editor.SetAsync(field.Key, family), id);
                    };
                if (field.Key == "darkTheme") combo.IsEnabled = editor.Draft.FollowSystemAppearance == true;
                if (field.Key is "notificationSoundName" or "blockedStatusSoundName")
                {
                    var sounds = new Grid { ColumnSpacing = 12 };
                    sounds.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    sounds.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    sounds.Children.Add(combo);
                    var preview = Button("Preview", id + "-preview");
                    preview.IsEnabled = selected is not null && choices.Any(Matches);
                    preview.Click += async (_, _) => await RunAsync(async () =>
                    {
                        if (!await notificationSounds.TryPlayAsync(editor.Value(field.Key)?.GetValue<string>(),
                            NotificationSoundKind.Preview))
                            throw new InvalidOperationException("Windows could not play the selected sound.");
                    });
                    Grid.SetColumn(preview, 1);
                    sounds.Children.Add(preview);
                    row.Children.Add(sounds);
                }
                else row.Children.Add(combo);
                break;
            case SettingControl.Color:
                var color = Color(value?.GetValue<string>() ?? (string)field.Default!);
                var colorButton = Button(value?.GetValue<string>() ?? $"Default ({field.Default})", id);
                colorButton.HorizontalAlignment = HorizontalAlignment.Left;
                colorButton.Background = new SolidColorBrush(color);
                var colorPicker = new ColorPicker { Color = color, IsAlphaEnabled = false,
                    IsAlphaSliderVisible = false, IsAlphaTextInputVisible = false };
                Identify(colorPicker, id + "-picker", field.Label);
                var colorPanel = new StackPanel { Spacing = 10 };
                colorPanel.Children.Add(colorPicker);
                var chooseColor = Button("Use color", id + "-accept");
                colorPanel.Children.Add(chooseColor);
                var flyout = new Flyout { Content = colorPanel };
                colorButton.Flyout = flyout;
                chooseColor.Click += async (_, _) =>
                {
                    flyout.Hide();
                    var picked = colorPicker.Color;
                    await RunAsync(() => editor.SetAsync(field.Key, $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}"));
                };
                row.Children.Add(colorButton);
                break;
            case SettingControl.Directory:
                var directory = new Grid { ColumnSpacing = 12 };
                directory.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                directory.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var path = new TextBox { Text = value?.GetValue<string>() ?? "Default", IsReadOnly = true, MaxWidth = 470 };
                Identify(path, id, field.Label);
                directory.Children.Add(path);
                var choose = Button("Choose...", id + "-choose");
                choose.Click += async (_, _) => await RunAsync(async () =>
                {
                    var picker = new Windows.Storage.Pickers.FolderPicker();
                    picker.FileTypeFilter.Add("*");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    if (await picker.PickSingleFolderAsync() is { } folder)
                        await editor.SetAsync(field.Key, folder.Path);
                });
                Grid.SetColumn(choose, 1);
                directory.Children.Add(choose);
                row.Children.Add(directory);
                break;
            case SettingControl.Interface:
                var interfaceChoices = new StackPanel { Spacing = 8 };
                foreach (var element in Enum.GetValues<InterfaceElement>())
                {
                    var check = new CheckBox { Content = KeyLabel(element.ToString()), IsChecked = !editor.Draft.IsInterfaceElementHidden(element) };
                    Identify(check, id + "-" + element.RawValue(), element.ToString());
                    check.Click += async (_, _) => await RunAsync(() => editor.SetInterfaceVisibleAsync(element, check.IsChecked == true));
                    interfaceChoices.Children.Add(check);
                }
                row.Children.Add(interfaceChoices);
                break;
            case SettingControl.Unavailable:
                row.Children.Add(new ToggleSwitch { IsEnabled = false, IsOn = editor.Draft.InheritGlobalGhosttyConfig == true });
                break;
        }
        if (field.Editable && field.Control != SettingControl.Interface &&
            (field.Control != SettingControl.Theme || editor.Draft.FollowSystemAppearance != true))
        {
            var reset = Button("Use default", id + "-default");
            reset.Click += async (_, _) => await RunAsync(() => field.Key == "followSystemAppearance"
                ? editor.SetFollowingAsync(false, SystemDark()) : editor.SetAsync(field.Key, null));
            Grid.SetColumn(reset, 1);
            row.Children.Add(reset);
        }
        if (field.Hint is { } hint)
            panel.Children.Add(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        return panel;
    }

    private async Task RunAsync(Func<Task> operation, string? focusId = null)
    {
        if (busy || closed) return;
        focusId ??= FocusedSettingId();
        busy = true;
        tabs.IsEnabled = reload.IsEnabled = retry.IsEnabled = false;
        status.Text = "Saving and applying...";
        Exception? failure = null;
        try { await operation(); }
        catch (Exception ex) { failure = ex; System.Diagnostics.Trace.TraceError($"Settings window: {ex}"); }
        finally
        {
            busy = false;
            if (!closed)
            {
                tabs.IsEnabled = reload.IsEnabled = retry.IsEnabled = true;
                ApplyThemeChrome();
                RenderTab(focusId);
                ShowError(failure);
            }
        }
    }

    private void ShowError(Exception? failure = null)
    {
        failure ??= editor.Error;
        error.IsOpen = failure is not null;
        error.Title = failure is SettingsApplyException ? "Saved, but not applied to every window" : "Settings could not be updated";
        error.Message = failure?.GetBaseException().Message ?? "";
        retry.Visibility = editor.HasUnsavedChanges ? Visibility.Visible : Visibility.Collapsed;
        status.Text = editor.HasUnsavedChanges ? "Unsaved changes. Retry or reload to discard them." :
            failure is null ? "Changes save and apply automatically." : "Fix the problem, then reload from file.";
    }

    private async Task LoadShortcutsWithErrorAsync()
    {
        try { await LoadShortcutsAsync(); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Settings keymap list: {ex}");
            if (!closed) ShowError(ex);
        }
    }

    private async Task LoadShortcutsAsync()
    {
        var groups = await shortcuts();
        if (closed) return;
        keybindings.Refresh(groups);
        RenderShortcuts();
    }

    private void RenderShortcuts()
    {
        shortcutRows.Children.Clear();
        if (globalHotkeyError() is { } message)
        {
            var diagnostic = new InfoBar { IsOpen = true, IsClosable = false,
                Severity = InfoBarSeverity.Error, Title = "Global hotkey unavailable", Message = message };
            Identify(diagnostic, "global-hotkey-error", "Global hotkey unavailable");
            shortcutRows.Children.Add(diagnostic);
        }
        foreach (var group in keybindings.Groups)
        {
            shortcutRows.Children.Add(new TextBlock { Text = group.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            foreach (var item in group.Items)
                shortcutRows.Children.Add(new TextBlock { Text = $"{item.Title}    {item.Shortcut}    {item.Status}", TextWrapping = TextWrapping.Wrap });
        }
    }

    private static Button Button(string label, string id)
    {
        var button = new Button { Content = label };
        Identify(button, id, label);
        return button;
    }
    private static void Identify(DependencyObject control, string id, string label)
    {
        AutomationProperties.SetAutomationId(control, id);
        AutomationProperties.SetName(control, label);
    }
    private string? FocusedSettingId()
    {
        if (Content is not FrameworkElement { XamlRoot: { } root }) return null;
        for (var element = FocusManager.GetFocusedElement(root) as DependencyObject;
             element is not null; element = VisualTreeHelper.GetParent(element))
        {
            var id = AutomationProperties.GetAutomationId(element);
            if (id.StartsWith("settings-", StringComparison.Ordinal)) return id;
        }
        return null;
    }
    private static Control? FindControl(DependencyObject root, string id)
    {
        if (root is Control control && AutomationProperties.GetAutomationId(root) == id) return control;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindControl(VisualTreeHelper.GetChild(root, i), id) is { } child) return child;
        return null;
    }
    private bool SystemDark()
    {
        var color = uiSettings.GetColorValue(UIColorType.Background);
        return TerminalTheme.IsDarkColor($"#{color.R:X2}{color.G:X2}{color.B:X2}");
    }

    private void ApplyThemeChrome()
    {
        var presentation = LiveThemeSettings.Resolve(coordinator.State.Snapshot.Settings,
            ThemeCatalog.Default, SystemDark());
        root.RequestedTheme = presentation.UseDarkChrome ? ElementTheme.Dark : ElementTheme.Light;
        appWindow.TitleBar.PreferredTheme = presentation.UseDarkChrome ? TitleBarTheme.Dark : TitleBarTheme.Light;
    }

    private void OnSystemAppearanceChanged(UISettings sender, object args)
    {
        if (!DispatcherQueue.TryEnqueue(() => { if (!closed) ApplyThemeChrome(); }))
            System.Diagnostics.Trace.TraceError("Settings window could not receive the Windows appearance change.");
    }
    private static Windows.UI.Color Color(string hex) => AppSettings.IsHexColor(hex)
        ? ColorHelper.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16))
        : Colors.Gray;
    private static string KeyLabel(string text) => System.Text.RegularExpressions.Regex.Replace(text, "(?<!^)([A-Z])", " $1");
}
