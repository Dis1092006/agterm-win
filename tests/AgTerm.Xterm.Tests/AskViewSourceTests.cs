using System.Text.RegularExpressions;

namespace AgTerm.Xterm.Tests;

public sealed class AskViewSourceTests
{
    private static string Source(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { FindRepoRoot() }.Concat(path).ToArray()));

    [Fact]
    public void The_window_hosts_the_dialog_over_both_rows()
    {
        var xaml = Source("src", "AgTerm.App", "MainWindow.xaml");
        Assert.Contains("<local:AskView x:Name=\"AskOverlay\"", xaml, StringComparison.Ordinal);
        // over the title bar row too, so its pane toggle cannot run or take focus while the question is up
        Assert.Matches(new Regex(@"<local:AskView x:Name=""AskOverlay""\s+Grid\.RowSpan=""2""\s+Canvas\.ZIndex=""100"" />"), xaml);
    }

    [Fact]
    public void The_window_syncs_the_shown_modal_first_so_its_close_runs_before_the_other_opens()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Matches(new Regex(
            @"private async Task SyncModalAsync\(\)\s*\{\s*if \(_shownAskId is not null\)\s*\{\s*await SyncAskAsync\(\);\s*await SyncPickAsync\(\);\s*\}\s*else\s*\{\s*await SyncPickAsync\(\);\s*await SyncAskAsync\(\);\s*\}\s*\}"),
            source);
    }

    [Fact]
    public void No_chord_or_terminal_focus_gets_past_an_open_dialog()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Matches(new Regex(
            @"private bool HandleChord\(PhysicalChord physical, long\? generation = null, string\? sourcePaneId = null\)\s*\{\s*if \(AskOverlay\.IsOpen \|\| KeybindingsOverlay\.IsOpen\)\s*return false;"),
            source);
        Assert.Matches(new Regex(
            @"private void FocusTerminal\(string paneId\)\s*\{\s*if \(_applicationHost\.Quick\.HoldsKey\) return;\s*if \(AskOverlay\.IsOpen\)\s*\{\s*(//[^
]*\s*)*if \(_windowIsActive\)\s*AskOverlay\.FocusPanel\(\);\s*return;\s*\}"),
            source);
    }

    // manual check 16a/16d: a WebView2 focused in a background window never got the keyboard back
    // on Alt+Tab; DeferredTerminalFocusTests covers the rule, this pins that no call bypasses it
    [Fact]
    public void The_only_terminal_focus_goes_through_the_activation_deferral()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        const string deferral = "_terminalFocus.Request(_windowIsActive, () => ";
        var call = Assert.Single(Regex.Matches(source, @"TerminalView\.Focus\("));
        Assert.True(
            call.Index >= deferral.Length && source.Substring(call.Index - deferral.Length, deferral.Length) == deferral,
            "the terminal focus must go through the activation deferral");
    }

    [Fact]
    public void The_window_escapes_only_the_ask_it_still_holds()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Matches(new Regex(
            @"private async Task HandleAskEscapedAsync\(string askId\)\s*\{\s*if \(_shownAskId != askId \|\| _host is not \{ \} host \|\| host\.Host\.Actions\.Pick\.PendingAsk\?\.Id != askId\)\s*return;"),
            source);
    }

    [Fact]
    public void The_dialog_keeps_focus_and_swallows_keys_while_open()
    {
        var xaml = Source("src", "AgTerm.App", "AskView.xaml");
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Contains("IsTabStop=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"OnKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTabStop = false", code, StringComparison.Ordinal);
        Assert.Contains("AllowFocusOnInteraction = false", code, StringComparison.Ordinal);
        Assert.Contains("AskKeys.Intent(", code, StringComparison.Ordinal);
        Assert.Contains("AskLabel.Runs(", code, StringComparison.Ordinal);
        Assert.Contains("AskPanelLayout.Arrange(", code, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_presents_the_gui_ask_and_answers_it()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Contains("SyncAskAsync", source, StringComparison.Ordinal);
        Assert.Contains(".Pick.PendingAsk", source, StringComparison.Ordinal);
        Assert.Contains(".Pick.AnswerAsk(", source, StringComparison.Ordinal);
        Assert.Contains(".Pick.EscapeAsk(", source, StringComparison.Ordinal);
        Assert.Contains("AskOverlay.CancelChord = IsCloseSessionChord;", source, StringComparison.Ordinal);
        Assert.Contains("AskAnchorGeometry.Pane(", source, StringComparison.Ordinal);
        Assert.Contains("if (AskOverlay.IsOpen || KeybindingsOverlay.IsOpen) return;", source, StringComparison.Ordinal);
        Assert.Contains("_terminalFocus.Activated(modalOpen: AnyPaletteOpen || AskOverlay.IsOpen || KeybindingsOverlay.IsOpen, FocusActiveTerminal);", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AccentButtonBackgroundPointerOver")]
    [InlineData("AccentButtonBackgroundPressed")]
    [InlineData("AccentButtonForegroundPointerOver")]
    [InlineData("AccentButtonForegroundPressed")]
    [InlineData("ButtonForegroundPointerOver")]
    [InlineData("ButtonForegroundPressed")]
    public void The_destructive_button_overrides_its_hover_and_pressed_resource(string key)
    {
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Contains($"Resources[\"{key}\"]", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_destructive_button_keeps_its_colors_before_it_enters_the_tree()
    {
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Matches(new Regex(
            @"if \(AskIds\.Same\(choice\.Id, ask\.DestructiveId\)\)\s*KeepDestructiveColors\(button\);(?:\s*//[^\n]*)*\s*ButtonHost\.Children\.Add\(button\);"),
            code);
    }

    [Fact]
    public void The_dialog_takes_focus_back_while_open()
    {
        var xaml = Source("src", "AgTerm.App", "AskView.xaml");
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Contains("LosingFocus=\"OnLosingFocus\"", xaml, StringComparison.Ordinal);
        Assert.Matches(new Regex(
            @"private void OnLosingFocus\(UIElement sender, LosingFocusEventArgs args\)\s*\{\s*if \(_ask is null\)\s*return;(?:\s*//[^\n]*)*\s*if \(args\.NewFocusedElement is not \{ \} target \|\| IsInside\(target\) \|\| IsInOpenPopup\(target\)\)\s*return;\s*if \(!args\.TrySetNewFocusedElement\(this\)\)\s*args\.TryCancel\(\);\s*\}"),
            code);
    }

    [Fact]
    public void The_dialog_lets_focus_go_to_a_content_dialog_opened_in_a_popup()
    {
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Matches(new Regex(
            @"private bool IsInOpenPopup\(DependencyObject element\)\s*\{\s*var roots = OpenPopups\(\)\s*\.Select\(popup => popup\.Child\)\s*\.OfType<DependencyObject>\(\)\s*\.ToArray\(\);\s*for \(var node = element; node is not null; node = VisualTreeHelper\.GetParent\(node\)\)\s*if \(roots\.Any\(root => ReferenceEquals\(root, node\)\)\) return true;\s*return false;\s*\}"),
            code);
        Assert.Matches(new Regex(
            @"private IReadOnlyList<Popup> OpenPopups\(\) =>\s*XamlRoot is null \? \[\] : VisualTreeHelper\.GetOpenPopupsForXamlRoot\(XamlRoot\);"),
            code);
    }

    [Fact]
    public void The_dialog_leaves_focus_in_an_open_content_dialog()
    {
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        // activation and Open(takeFocus: true) both focus through FocusPanel, now and on the dispatcher
        Assert.Matches(new Regex(
            @"public void FocusPanel\(\)\s*\{\s*FocusUnlessContentDialogOpen\(\);\s*DispatcherQueue\.TryEnqueue\(FocusUnlessContentDialogOpen\);\s*\}"),
            code);
        Assert.Matches(new Regex(
            @"private void FocusUnlessContentDialogOpen\(\)\s*\{(?:\s*//[^\n]*)*\s*if \(!IsContentDialogOpen\(\)\)\s*FocusTarget\.Focus\(FocusState\.Programmatic\);\s*\}"),
            code);
        // the field takes the keys when there is one, as upstream focuses its editor; a list's free-form
        // row takes them only while the cursor is on it
        Assert.Matches(new Regex(
            @"private Control FocusTarget =>\s*InputBox\.Visibility == Visibility\.Visible \? InputBox\s*: _otherRow is \{ \} other && _draft\.CursorOnOther \? other\.Box\s*: this;"),
            code);
        Assert.Matches(new Regex(
            @"private bool IsContentDialogOpen\(\) =>\s*OpenPopups\(\)\.Any\(popup => popup\.Child is ContentDialog\);"),
            code);
        Assert.Matches(new Regex(@"if \(takeFocus\) FocusPanel\(\);"), code);
    }

    [Fact]
    public void The_window_focuses_the_open_modal_after_any_content_dialog_closes()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Contains("_contentDialogs.DialogClosed += FocusModalAfterContentDialog;", source, StringComparison.Ordinal);
        // a pick or the command palette shown under the dialog gets the keys back too
        Assert.Matches(new Regex(
            @"private void FocusModalAfterContentDialog\(\)\s*\{\s*if \(!_windowIsActive\)\s*return;\s*if \(AskOverlay\.IsOpen\)\s*AskOverlay\.FocusPanel\(\);\s*else if \(PaletteOverlay\.IsOpen\)\s*PaletteOverlay\.FocusQuery\(\);\s*else if \(IconPaletteOverlay\.IsOpen\)\s*IconPaletteOverlay\.FocusFilter\(\);\s*\}"),
            source);
        // all four dialogs (confirmation, rename, installer result, middle-click paste) go through the coordinator, so its event covers each of them
        Assert.Equal(4, Regex.Matches(source, @"await ShowDialogAsync\(dialog\)").Count);
        Assert.Equal(4, Regex.Matches(source, @"_contentDialogs\.RunAsync\(").Count);
    }

    // a fault while arming the hotkeys must not leave a drawn question without its answer path
    [Fact]
    public void The_window_subscribes_to_terminal_question_events_with_the_other_page_events()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Matches(new Regex(
            @"page\.DashboardClosed \+= OnPageDashboardClosed;\s*page\.AskIntentReceived \+= OnPageAskIntentReceived;\s*page\.AskFocusChanged \+= OnPageAskFocusChanged;"),
            source);
        var arm = Regex.Match(source, @"private async Task ArmHotkeysAsync\(LiveControlHost host\)\s*\{.*?\n    \}", RegexOptions.Singleline);
        Assert.True(arm.Success);
        Assert.DoesNotContain("AskIntentReceived", arm.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("AskFocusChanged", arm.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_draws_the_terminal_question_and_answers_it()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Contains("new TerminalAskNavigator()", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.TerminalAsk", source, StringComparison.Ordinal);
        Assert.Contains(".ApplyAskAsync(", source, StringComparison.Ordinal);
        Assert.Contains(".AskIntentReceived +=", source, StringComparison.Ordinal);
        Assert.Contains(".AskFocusChanged +=", source, StringComparison.Ordinal);
        Assert.Contains("store.ResolveAsk(", source, StringComparison.Ordinal);
        Assert.Contains("EscapeFocusedTerminalAsk", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_keeps_the_terminal_question_draft_in_the_navigator_and_the_store()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Matches(new Regex(
            @"page\.AskFocusChanged \+= OnPageAskFocusChanged;\s*page\.AskDraftReceived \+= OnPageAskDraftReceived;"),
            source);
        Assert.Matches(new Regex(
            @"if \(!_terminalAsks\.UpdateDraft\(askId, text\) \|\| _terminalAsks\.DraftSnapshot\(\) is not \{ \} draft\)\s*return;\s*SaveTerminalAskDraft\(askId, draft\);"),
            source);
        // an edit of a list's free-form row can select it, so the list redraws
        Assert.Matches(new Regex(
            @"if \(_terminalAsks\.ListRedraw\(\) is \{ \} view\)\s*ObserveTaskFault\(ApplyTerminalAskAsync\(view\), ""Apply terminal ask failed""\);"),
            source);
        // a list change saves the navigator's snapshot the same way
        Assert.Matches(new Regex(
            @"case TerminalAskResolution\.Drafted drafted:\s*SaveTerminalAskDraft\(askId, drafted\.Draft\);\s*await ApplyTerminalAskAsync\(drafted\.View\);\s*break;"),
            source);
        // one writer at a time: separate MutateAsync calls are not ordered
        Assert.Matches(new Regex(
            @"while \(_pendingAskDraft is \{ \} draft && _host is \{ \} host\)\s*\{\s*_pendingAskDraft = null;\s*await host\.StoreCoordinator\.MutateAsync\(store =>\s*store\.UpdateAskDraft\(draft\.SessionId, draft\.AskId, saved => saved\.CopyFrom\(draft\.Draft\)\)\);\s*\}"),
            source);
    }

    [Fact]
    public void The_window_answers_the_gui_question_with_its_draft()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Contains("host.Host.Actions.Pick.AnswerAsk(askId, index);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlAskResult.Answered(", source, StringComparison.Ordinal);
        Assert.Contains("AskOverlay.Open(pending, host.Host.Actions.Pick.AskDraft, _rendererSettings.FontSize, takeFocus: _windowIsActive);", source, StringComparison.Ordinal);
        Assert.Contains("AskOverlay.DraftChanged += (askId, draft) => _host?.Host.Actions.Pick.UpdateAskDraft(askId, saved => saved.CopyFrom(draft));", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_draws_a_choice_list_between_the_field_and_the_buttons()
    {
        var xaml = Source("src", "AgTerm.App", "AskView.xaml");
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Matches(new Regex(
            @"<TextBox x:Name=""InputBox""[^>]*/>\s*<ScrollViewer x:Name=""ChoiceScroll""[^>]*>\s*<StackPanel x:Name=""ChoiceHost""[^>]*/>\s*</ScrollViewer>\s*<Grid x:Name=""ButtonHost"" />"),
            xaml);
        Assert.Contains("AutomationProperties.AutomationId=\"ask-choices\"", xaml, StringComparison.Ordinal);
        // Segoe Fluent glyphs for upstream's checkbox and radio symbols
        foreach (var glyph in new[] { "\\uE739", "\\uE73A", "\\uECCA", "\\uECCB" })
            Assert.Contains(glyph, code, StringComparison.Ordinal);
        // the cursor row gets upstream's accent fill at 0.25 with rounded corners
        Assert.Contains("Opacity = 0.25", code, StringComparison.Ordinal);
        Assert.Contains("CornerRadius = new CornerRadius(5)", code, StringComparison.Ordinal);
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", code, StringComparison.Ordinal);
        Assert.Contains("Handle(new AskIntent.ToggleAt(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_draws_a_free_form_row_after_the_list()
    {
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Contains("if (list.Other is { } placeholder)", code, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAutomationId(line, \"ask-other\");", code, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAutomationId(box, \"ask-other-input\");", code, StringComparison.Ordinal);
        // the marker toggles like an item and is dimmed until the text is not blank; the field edits without toggling
        Assert.Contains("line.Tapped += (_, _) => Handle(new AskIntent.ToggleAt(index));", code, StringComparison.Ordinal);
        Assert.Contains("other.Line.Opacity = _draft.OtherEnabled ? 1 : 0.4;", code, StringComparison.Ordinal);
        Assert.Contains("var selected = _draft.OtherSelected;", code, StringComparison.Ordinal);
        // a click into the field moves the cursor onto the row
        Assert.Matches(new Regex(
            @"private void OnOtherGotFocus\(object sender, RoutedEventArgs e\)\s*\{\s*if \(!_draft\.CursorOnOther\)\s*Handle\(new AskIntent\.EditOther\(\)\);\s*\}"),
            code);
        // the field holds the keys only while the cursor is on the row, and Up and Down go back to the list
        Assert.Contains("routesVertical: ask.Form is AskForm.Choices", code, StringComparison.Ordinal);
        Assert.Matches(new Regex(
            @"if \(_draft\.Apply\(intent\)\)\s*\{\s*ApplyChoices\(\);\s*PublishDraft\(\);\s*BringCursorIntoView\(\);\s*HandFieldFocus\(\);"),
            code);
        Assert.Contains("if (!ReferenceEquals(focused, FocusTarget)) FocusTarget.Focus(FocusState.Programmatic);", code, StringComparison.Ordinal);
        Assert.Contains("other.Row.StartBringIntoView(", code, StringComparison.Ordinal);
        // the text set when the row is built comes back as TextChanged and must not select the row
        Assert.Matches(new Regex(
            @"var text = AskFieldText\.FromTextBox\(other\.Box\.Text, ControlAskInputMode\.Line\);\s*if \(text == _draft\.Text\) return false;\s*_draft\.Text = text;"),
            code);
        // Enter can come before the last TextChanged
        Assert.Matches(new Regex(
            @"case AskIntentOutcome\.Answer answer:(?:\s*//[^\n]*)*\s*TakeOtherText\(\);\s*PublishDraft\(\);"),
            code);
        Assert.Contains("if (ReferenceEquals(node, InputBox) || ReferenceEquals(node, _otherRow?.Box)) return true;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_keeps_a_draft_copy_and_publishes_each_change()
    {
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Contains("_draft = draft?.Copy() ?? new AskDraft(ask.Form);", code, StringComparison.Ordinal);
        Assert.Contains("public event Action<string, AskDraft>? DraftChanged;", code, StringComparison.Ordinal);
        Assert.Contains("DraftChanged?.Invoke(ask.Id, _draft.Copy());", code, StringComparison.Ordinal);
        // a list intent goes to the draft first; what it leaves goes to the buttons
        Assert.Matches(new Regex(
            @"if \(_draft\.Apply\(intent\)\)\s*\{\s*ApplyChoices\(\);\s*PublishDraft\(\);"),
            code);
    }

    [Fact]
    public void The_dialog_list_scrolls_in_its_own_region_and_follows_the_cursor()
    {
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Contains("ChoiceScroll.MaxHeight = ", code, StringComparison.Ordinal);
        Assert.Contains("_choiceRows[_draft.Cursor].Row.StartBringIntoView(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_shows_a_text_box_for_an_input_form()
    {
        var xaml = Source("src", "AgTerm.App", "AskView.xaml");
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Matches(new Regex(
            @"x:Name=""MessageText""[^>]*/>\s*<TextBox x:Name=""InputBox"""),
            xaml);
        foreach (var attribute in new[]
                 {
                     "AutomationProperties.AutomationId=\"ask-input\"",
                     "PreviewKeyDown=\"OnInputPreviewKeyDown\"",
                     "BeforeTextChanging=\"OnInputBeforeTextChanging\"",
                     "Paste=\"OnInputPaste\"",
                     "TextChanged=\"OnInputTextChanged\"",
                     "TextWrapping=\"Wrap\"",
                 })
            Assert.Contains(attribute, xaml, StringComparison.Ordinal);
        Assert.Contains("InputBox.AcceptsReturn = multiline;", code, StringComparison.Ordinal);
        Assert.Contains("InputBox.PlaceholderText = input.Field.Placeholder ?? string.Empty;", code, StringComparison.Ordinal);
        Assert.Contains("InputBox.Text = AskFieldText.ToTextBox(", code, StringComparison.Ordinal);
        // a multiline field shows 5 to 15 lines, as upstream
        Assert.Contains("InputBox.MinHeight = 5 * line + chrome;", code, StringComparison.Ordinal);
        Assert.Contains("InputBox.MaxHeight = 15 * line + chrome;", code, StringComparison.Ordinal);
        // a question with a field takes the full panel width, a list's free-form row too
        Assert.Contains("if (ask.Width is not null || ask.Form is AskForm.Input || _otherRow is not null) Panel.Width = maxWidth;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_field_validates_each_edit_and_each_paste()
    {
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Matches(new Regex(
            @"if \(FieldMode\(sender\) is \{ \} mode && !AskFieldText\.Accepts\(AskFieldText\.FromTextBox\(args\.NewText, mode\), mode\)\)\s*args\.Cancel = true;"),
            code);
        Assert.Matches(new Regex(@"private async void OnInputPaste\(object sender, TextControlPasteEventArgs e\)\s*\{\s*e\.Handled = true;"), code);
        Assert.Contains("AskFieldText.PasteInsertion(clipboard, mode)", code, StringComparison.Ordinal);
        // a choice list's free-form row is a line field
        Assert.Matches(new Regex(
            @"AskForm\.Input input when ReferenceEquals\(sender, InputBox\) => input\.Field\.Mode,\s*AskForm\.Choices when _otherRow is \{ \} other && ReferenceEquals\(sender, other\.Box\) => ControlAskInputMode\.Line,"),
            code);
        Assert.Contains("DraftChanged?.Invoke(ask.Id, _draft.Copy());", code, StringComparison.Ordinal);
        Assert.Contains("_draft.Text = AskFieldText.FromTextBox(InputBox.Text, input.Field.Mode);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_field_keeps_its_keys_and_answers_with_the_field_rule()
    {
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Contains("AskKeys.FieldIntent((int)e.Key,", code, StringComparison.Ordinal);
        Assert.Matches(new Regex(
            @"if \(!AskKeys\.IsFieldEditChord\(\(int\)e\.Key, control, alt\) && CancelChord\?\.Invoke\(e\.Key\) == true\)"),
            code);
        // a key the field leaves unhandled bubbles to the dialog: a letter is text there, not a hotkey
        Assert.Matches(new Regex(
            @"e\.Handled = true;\s*if \(_ask is not \{ \} ask\) return;(?:\s*//[^\n]*)*\s*if \(IsInField\(e\.OriginalSource\)\) return;"),
            code);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgTerm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
