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
    private PendingAsk? _ask;
    private AskNavigation? _navigation;
    private AskRect _anchor = new(0, 0, 0, 0);
    private double _fontSize = 13;

    public AskView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyLayout();
    }

    public event Action<string, int>? Answered;

    public event Action<string>? Escaped;

    /// <summary>The field's text after each accepted edit.</summary>
    public event Action<string, string>? DraftChanged;

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
        _fontSize = terminalFontSize;
        TitleText.Text = ask.Title;
        MessageText.Text = ask.Message ?? string.Empty;
        MessageText.Visibility = string.IsNullOrEmpty(ask.Message) ? Visibility.Collapsed : Visibility.Visible;
        Visibility = Visibility.Visible;
        BuildInput(ask, draft);
        BuildButtons(ask);
        ApplyHighlight();
        ApplyLayout();
        if (takeFocus) FocusPanel();
    }

    public void Close()
    {
        _ask = null;
        _navigation = null;
        _buttons.Clear();
        ButtonHost.Children.Clear();
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

    private void BuildInput(PendingAsk ask, AskDraft? draft)
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
        InputBox.Text = AskFieldText.ToTextBox((draft ?? new AskDraft(ask.Form)).Text, input.Field.Mode);
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
        PanelScroll.MaxHeight = Math.Max(
            0, AskPanelLayout.MaxPanelHeight(_anchor.Height) - 2 * AskPanelLayout.PanelPadding - border.Top - border.Bottom);
        Panel.Measure(new Size(maxWidth, double.PositiveInfinity));
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

    private void OnInputTextChanged(object sender, TextChangedEventArgs e) => PublishDraft();

    private void PublishDraft()
    {
        if (_ask is { Form: AskForm.Input input } ask)
            DraftChanged?.Invoke(ask.Id, AskFieldText.FromTextBox(InputBox.Text, input.Field.Mode));
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
