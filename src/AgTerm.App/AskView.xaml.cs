using AgTerm.Core.Model;
using AgTerm.Core.Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace AgTerm.App;

public sealed partial class AskView : UserControl
{
    private readonly List<Button> _buttons = new();
    private readonly List<ChoiceRow> _choiceRows = new();
    private PendingAsk? _ask;
    private AskNavigation? _navigation;
    private AskDraft _draft = new(null);
    private AskRect _anchor = new(0, 0, 0, 0);
    private double _fontSize = 13;

    // upstream's gui cell; the list spacing and paddings are fractions of it
    private const double Cell = 8;
    private const double MarkerWidth = 16;

    private sealed record ChoiceRow(StackPanel Row, Border Line, FontIcon Marker);

    public AskView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyLayout();
    }

    public event Action<string, int>? Answered;

    public event Action<string>? Escaped;

    /// <summary>A copy of the draft after each accepted field edit and each list change.</summary>
    public event Action<string, AskDraft>? DraftChanged;

    /// <summary>The window's close_session chord: it dismisses the question instead of closing a session.</summary>
    public Func<VirtualKey, bool>? CancelChord { get; set; }

    public bool IsOpen => _ask is not null;

    public string? AskId => _ask?.Id;

    public void SetPanelBackground(Brush? background)
    {
        if (background is null)
            Panel.ClearValue(Border.BackgroundProperty);
        else
            Panel.Background = background;
    }

    /// <summary>takeFocus is false for a question sent to a background window: focusing would
    /// activate the window. The host calls FocusPanel when the user activates it.</summary>
    public void Open(PendingAsk ask, AskDraft? draft, double terminalFontSize, bool takeFocus)
    {
        ArgumentNullException.ThrowIfNull(ask);
        _ask = ask;
        _navigation = AskNavigation.For(ask);
        // the store keeps the draft; the dialog edits its own copy and publishes each change
        _draft = draft?.Copy() ?? new AskDraft(ask.Form);
        _fontSize = terminalFontSize;
        TitleText.Text = ask.Title;
        MessageText.Text = ask.Message ?? string.Empty;
        MessageText.Visibility = string.IsNullOrEmpty(ask.Message) ? Visibility.Collapsed : Visibility.Visible;
        Visibility = Visibility.Visible;
        BuildInput(ask);
        BuildChoices(ask);
        BuildButtons(ask);
        ApplyHighlight();
        ApplyChoices();
        ApplyLayout();
        if (_choiceRows.Count > 0) DispatcherQueue.TryEnqueue(BringCursorIntoView);
        if (takeFocus) FocusPanel();
    }

    public void Close()
    {
        _ask = null;
        _navigation = null;
        _draft = new AskDraft(null);
        _buttons.Clear();
        ButtonHost.Children.Clear();
        _choiceRows.Clear();
        ChoiceHost.Children.Clear();
        ChoiceScroll.Visibility = Visibility.Collapsed;
        InputBox.Visibility = Visibility.Collapsed;
        InputBox.Text = string.Empty;
        Visibility = Visibility.Collapsed;
    }

    public void SetAnchor(AskRect anchor)
    {
        _anchor = anchor;
        ApplyLayout();
    }

    public void FocusPanel()
    {
        FocusUnlessContentDialogOpen();
        DispatcherQueue.TryEnqueue(FocusUnlessContentDialogOpen);
    }

    private void FocusUnlessContentDialogOpen()
    {
        // the dialog is modal for the window: focus moved under its backdrop gets no keys; the host
        // focuses the question again when the dialog closes
        if (!IsContentDialogOpen())
            FocusTarget.Focus(FocusState.Programmatic);
    }

    private Control FocusTarget => InputBox.Visibility == Visibility.Visible ? InputBox : this;

    private void BuildInput(PendingAsk ask)
    {
        if (ask.Form is not AskForm.Input input)
        {
            InputBox.Visibility = Visibility.Collapsed;
            InputBox.Text = string.Empty;
            return;
        }
        var multiline = input.Field.Mode == ControlAskInputMode.Multiline;
        InputBox.AcceptsReturn = multiline;
        InputBox.PlaceholderText = input.Field.Placeholder ?? string.Empty;
        InputBox.Text = AskFieldText.ToTextBox(_draft.Text, input.Field.Mode);
        InputBox.Select(InputBox.Text.Length, 0);
        if (multiline)
        {
            // a TextBox line is about 1.33 of its font size; the padding and the border come on top
            var line = Math.Ceiling(InputBox.FontSize * 1.33);
            var chrome = InputBox.Padding.Top + InputBox.Padding.Bottom
                + InputBox.BorderThickness.Top + InputBox.BorderThickness.Bottom;
            InputBox.MinHeight = 5 * line + chrome;
            InputBox.MaxHeight = 15 * line + chrome;
        }
        else
        {
            InputBox.ClearValue(MinHeightProperty);
            InputBox.ClearValue(MaxHeightProperty);
        }
        InputBox.Visibility = Visibility.Visible;
    }

    private void BuildChoices(PendingAsk ask)
    {
        _choiceRows.Clear();
        ChoiceHost.Children.Clear();
        if (ask.Form is not AskForm.Choices { List: var list })
        {
            ChoiceScroll.Visibility = Visibility.Collapsed;
            return;
        }
        var secondary = ThemeBrush("TextFillColorSecondaryBrush");
        for (var index = 0; index < list.Items.Count; index++)
        {
            var choice = list.Items[index];
            var marker = new FontIcon
            {
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                FontSize = MarkerWidth,
                Width = MarkerWidth,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var label = new TextBlock
            {
                Text = choice.Label,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            // a star column, not a horizontal StackPanel, so a long label is cut at the panel width
            var content = new Grid { ColumnSpacing = Cell };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(label, 1);
            content.Children.Add(marker);
            content.Children.Add(label);
            var line = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(Cell * 0.5, Cell * 0.15, Cell * 0.5, Cell * 0.15),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = content,
            };
            // a transparent background takes the taps between the line and the panel edge too
            var row = new StackPanel { Spacing = Cell * 0.2, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            row.Children.Add(line);
            if (!string.IsNullOrEmpty(choice.Subtitle))
                row.Children.Add(new TextBlock
                {
                    Text = choice.Subtitle,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = secondary,
                    Margin = new Thickness(Cell * 0.5 + MarkerWidth + Cell, 0, 0, 0),
                });
            AutomationProperties.SetName(row, choice.Label);
            AutomationProperties.SetAutomationId(row, $"ask-choice-{choice.Id}");
            var tapped = index;
            row.Tapped += (_, _) => Handle(new AskIntent.ToggleAt(tapped));
            _choiceRows.Add(new ChoiceRow(row, line, marker));
            ChoiceHost.Children.Add(row);
        }
        ChoiceScroll.Visibility = Visibility.Visible;
    }

    /// <summary>Upstream's gui markers in Segoe Fluent glyphs: the accent color when checked and the
    /// secondary color otherwise, with the cursor line on an accent fill.</summary>
    private void ApplyChoices()
    {
        if (_ask?.Form is not AskForm.Choices { List: var list }) return;
        var accent = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
        for (var index = 0; index < _choiceRows.Count; index++)
        {
            var row = _choiceRows[index];
            var isChecked = _draft.IsChecked(index);
            row.Marker.Glyph = list.Multiple
                ? (isChecked ? "\uE73A" : "\uE739") // CheckboxComposite, Checkbox
                : (isChecked ? "\uECCB" : "\uECCA"); // RadioBtnOn, RadioBtnOff
            row.Marker.Foreground = ThemeBrush(isChecked ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            if (index == _draft.Cursor)
                row.Line.Background = new SolidColorBrush(accent) { Opacity = 0.25 };
            else
                row.Line.ClearValue(Border.BackgroundProperty);
            AutomationProperties.SetItemStatus(row.Row, isChecked ? "checked" : string.Empty);
        }
    }

    private void BringCursorIntoView()
    {
        if (_draft.Cursor < _choiceRows.Count)
            _choiceRows[_draft.Cursor].Row.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
    }

    private static Brush ThemeBrush(string key) => (Brush)Application.Current.Resources[key];

    private void BuildButtons(PendingAsk ask)
    {
        _buttons.Clear();
        ButtonHost.Children.Clear();
        for (var index = 0; index < ask.Buttons.Count; index++)
        {
            var choice = ask.Buttons[index];
            var label = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
            foreach (var run in AskLabel.Runs(choice.Label, choice.Hotkey, destructivePrefix: false))
            {
                var inline = new Run { Text = run.Text };
                label.Inlines.Add(run.Underline ? new Underline { Inlines = { inline } } : inline);
            }
            var button = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                // the highlight is AskNavigation state; focus stays on the view so keys reach it
                IsTabStop = false,
                AllowFocusOnInteraction = false,
            };
            AutomationProperties.SetName(button, choice.Label);
            AutomationProperties.SetAutomationId(button, $"ask-button-{choice.Id}");
            var pressed = index;
            button.Click += (_, _) => Handle(new AskIntent.Press(pressed));
            _buttons.Add(button);
            if (AskIds.Same(choice.Id, ask.DestructiveId))
                KeepDestructiveColors(button);
            // in the live tree before the first measure, so the button template is applied
            ButtonHost.Children.Add(button);
        }
    }

    /// <summary>The button templates take the PointerOver and Pressed visuals from theme resources, not
    /// from the local Background and Foreground. The overrides must be in place before the template's
    /// first apply, because a ThemeResource resolves against the button's own resources only then.</summary>
    private static void KeepDestructiveColors(Button button)
    {
        var critical = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        var white = new SolidColorBrush(Microsoft.UI.Colors.White);
        button.Resources["AccentButtonBackgroundPointerOver"] = critical;
        button.Resources["AccentButtonBackgroundPressed"] = critical;
        button.Resources["AccentButtonForegroundPointerOver"] = white;
        button.Resources["AccentButtonForegroundPressed"] = white;
        button.Resources["ButtonForegroundPointerOver"] = critical;
        button.Resources["ButtonForegroundPressed"] = critical;
    }

    private void ApplyHighlight()
    {
        if (_ask is not { } ask || _navigation is null) return;
        for (var index = 0; index < _buttons.Count; index++)
        {
            var destructive = AskIds.Same(ask.Buttons[index].Id, ask.DestructiveId);
            var highlighted = _navigation.Highlighted == index;
            var button = _buttons[index];
            if (highlighted)
                button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            else
                button.ClearValue(StyleProperty);
            if (destructive)
            {
                var critical = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                if (highlighted)
                {
                    button.Background = critical;
                    button.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
                }
                else
                {
                    button.ClearValue(Control.BackgroundProperty);
                    button.Foreground = critical;
                }
            }
            AutomationProperties.SetItemStatus(button, highlighted ? "selected" : string.Empty);
        }
    }

    private void ApplyLayout()
    {
        if (_ask is not { } ask) return;
        Canvas.SetLeft(Scrim, _anchor.X);
        Canvas.SetTop(Scrim, _anchor.Y);
        Scrim.Width = _anchor.Width;
        Scrim.Height = _anchor.Height;

        var maxWidth = AskPanelLayout.MaxPanelWidth(_anchor.Width, _fontSize, ask.Width);
        var border = Panel.BorderThickness;
        var available = Math.Max(0, maxWidth - 2 * AskPanelLayout.PanelPadding - border.Left - border.Right);
        var natural = _buttons.Select(NaturalWidth).ToArray();
        var arrangement = AskPanelLayout.Arrange(natural, available);
        ArrangeButtons(ask.Align, arrangement);

        Panel.Width = double.NaN;
        Panel.MaxWidth = maxWidth;
        if (ask.Width is not null || ask.Form is AskForm.Input) Panel.Width = maxWidth;
        var limit = Math.Max(
            0, AskPanelLayout.MaxPanelHeight(_anchor.Height) - 2 * AskPanelLayout.PanelPadding - border.Top - border.Bottom);
        PanelScroll.MaxHeight = limit;
        ChoiceScroll.MaxHeight = double.PositiveInfinity;
        Panel.Measure(new Size(maxWidth, double.PositiveInfinity));
        // a long list scrolls in its own region down to 3 lines, as upstream, so the title and the
        // buttons stay in view; below that the whole panel scrolls
        if (_choiceRows.Count > 0 && ContentStack.DesiredSize.Height > limit)
        {
            var minimum = 3 * Math.Ceiling(FontSize * 1.33);
            ChoiceScroll.MaxHeight = Math.Max(minimum, ChoiceScroll.DesiredSize.Height - (ContentStack.DesiredSize.Height - limit));
            Panel.Measure(new Size(maxWidth, double.PositiveInfinity));
        }
        var size = Panel.DesiredSize;
        Canvas.SetLeft(Panel, _anchor.X + (_anchor.Width - size.Width) / 2);
        Canvas.SetTop(Panel, _anchor.Y + (_anchor.Height - size.Height) / 2);
    }

    private static double NaturalWidth(Button button)
    {
        // a previous arrangement fixed the width; a resize must see the label's own width again
        button.Width = double.NaN;
        button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return button.DesiredSize.Width;
    }

    private void ArrangeButtons(ControlAskAlignment align, AskButtonArrangement arrangement)
    {
        ButtonHost.Children.Clear();
        ButtonHost.RowDefinitions.Clear();
        ButtonHost.ColumnDefinitions.Clear();
        ButtonHost.ColumnSpacing = AskPanelLayout.ButtonSpacing;
        ButtonHost.RowSpacing = AskPanelLayout.ButtonSpacing;
        ButtonHost.HorizontalAlignment = align switch
        {
            ControlAskAlignment.Left => HorizontalAlignment.Left,
            ControlAskAlignment.Center => HorizontalAlignment.Center,
            _ => HorizontalAlignment.Right,
        };
        for (var index = 0; index < _buttons.Count; index++)
        {
            var button = _buttons[index];
            button.Width = arrangement.ButtonWidth;
            if (arrangement.Row)
            {
                ButtonHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(button, index);
                Grid.SetRow(button, 0);
            }
            else
            {
                ButtonHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(button, index);
                Grid.SetColumn(button, 0);
            }
            ButtonHost.Children.Add(button);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // a GUI question is modal for the window: no key reaches the window's chords or focus navigation
        e.Handled = true;
        if (_ask is not { } ask) return;
        // a key the field leaves unhandled is text there, not a hotkey
        if (IsInField(e.OriginalSource)) return;
        if (CancelChord?.Invoke(e.Key) == true)
        {
            Escaped?.Invoke(ask.Id);
            return;
        }
        var intent = AskKeys.Intent((int)e.Key, IsDown(VirtualKey.Shift), IsDown(VirtualKey.Control),
            IsDown(VirtualKey.Menu), IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows));
        if (intent is not null) Handle(intent);
    }

    private void OnInputPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_ask is not { Form: AskForm.Input input } ask) return;
        var control = IsDown(VirtualKey.Control);
        var alt = IsDown(VirtualKey.Menu);
        if (!AskKeys.IsFieldEditChord((int)e.Key, control, alt) && CancelChord?.Invoke(e.Key) == true)
        {
            e.Handled = true;
            Escaped?.Invoke(ask.Id);
            return;
        }
        // during IME composition the key comes as VK_PROCESSKEY, which no rule takes
        var intent = AskKeys.FieldIntent((int)e.Key, IsDown(VirtualKey.Shift), control, alt,
            IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows), input.Field.Mode);
        if (intent is null) return;
        e.Handled = true;
        Handle(intent);
    }

    private void OnInputBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        if (_ask?.Form is AskForm.Input input && !AskFieldText.Accepts(AskFieldText.FromTextBox(args.NewText, input.Field.Mode), input.Field.Mode))
            args.Cancel = true;
    }

    private async void OnInputPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true;
        if (_ask is not { Form: AskForm.Input input } ask) return;
        string? clipboard;
        try
        {
            var content = Clipboard.GetContent();
            clipboard = content.Contains(StandardDataFormats.Text) ? await content.GetTextAsync() : null;
        }
        catch (Exception)
        {
            return; // an unreadable clipboard pastes nothing
        }
        if (clipboard is null || _ask?.Id != ask.Id || AskFieldText.PasteInsertion(clipboard, input.Field.Mode) is not { } text)
            return;
        var inserted = AskFieldText.ToTextBox(text, input.Field.Mode);
        var start = InputBox.SelectionStart;
        var before = InputBox.Text;
        InputBox.SelectedText = inserted;
        if (InputBox.Text != before)
            InputBox.Select(start + inserted.Length, 0);
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ask?.Form is AskForm.Input) PublishDraft();
    }

    private void PublishDraft()
    {
        if (_ask is not { } ask) return;
        if (ask.Form is AskForm.Input input)
            _draft.Text = AskFieldText.FromTextBox(InputBox.Text, input.Field.Mode);
        DraftChanged?.Invoke(ask.Id, _draft.Copy());
    }

    private bool IsInField(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, InputBox)) return true;
        return false;
    }

    private void OnLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        if (_ask is null)
            return;
        // focus restored on window activation goes to the last focused element, the terminal, not the dialog;
        // a ContentDialog opens in a popup, and pulling focus back would leave keys under its backdrop
        if (args.NewFocusedElement is not { } target || IsInside(target) || IsInOpenPopup(target))
            return;
        if (!args.TrySetNewFocusedElement(this))
            args.TryCancel();
    }

    private bool IsInside(DependencyObject element)
    {
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, this)) return true;
        return false;
    }

    private IReadOnlyList<Popup> OpenPopups() =>
        XamlRoot is null ? [] : VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot);

    private bool IsContentDialogOpen() =>
        OpenPopups().Any(popup => popup.Child is ContentDialog);

    private bool IsInOpenPopup(DependencyObject element)
    {
        var roots = OpenPopups()
            .Select(popup => popup.Child)
            .OfType<DependencyObject>()
            .ToArray();
        for (var node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (roots.Any(root => ReferenceEquals(root, node))) return true;
        return false;
    }

    private void Handle(AskIntent intent)
    {
        if (_ask is not { } ask || _navigation is null) return;
        // a list intent changes the draft; any other, or one without a list, goes to the buttons
        if (_draft.Apply(intent))
        {
            ApplyChoices();
            PublishDraft();
            BringCursorIntoView();
            return;
        }
        switch (_navigation.Apply(intent))
        {
            case AskIntentOutcome.Moved:
                ApplyHighlight();
                // Tab must not leave the highlight off screen in a scrolled panel
                if (_navigation.Highlighted is { } highlighted && highlighted < _buttons.Count)
                    _buttons[highlighted].StartBringIntoView();
                break;
            case AskIntentOutcome.Answer answer:
                // TextChanged comes later on the dispatcher; the answer must read the last edit
                PublishDraft();
                Answered?.Invoke(ask.Id, answer.Index);
                break;
            case AskIntentOutcome.Escape:
                Escaped?.Invoke(ask.Id);
                break;
        }
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
