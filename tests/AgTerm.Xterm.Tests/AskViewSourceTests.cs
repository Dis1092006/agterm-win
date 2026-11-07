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
        // the field takes the keys when there is one, as upstream focuses its editor
        Assert.Contains("private Control FocusTarget => InputBox.Visibility == Visibility.Visible ? InputBox : this;", code, StringComparison.Ordinal);
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
            @"if \(!_terminalAsks\.UpdateDraft\(askId, text\) \|\| _terminalAsks\.CurrentSessionId is not \{ \} sessionId\)\s*return;"),
            source);
        // one writer at a time: separate MutateAsync calls are not ordered
        Assert.Matches(new Regex(
            @"while \(_pendingAskDraft is \{ \} draft && _host is \{ \} host\)\s*\{\s*_pendingAskDraft = null;\s*await host\.StoreCoordinator\.MutateAsync\(store =>\s*store\.UpdateAskDraft\(draft\.SessionId, draft\.AskId, saved => saved\.Text = draft\.Text\)\);\s*\}"),
            source);
    }

    [Fact]
    public void The_window_answers_the_gui_question_with_its_draft()
    {
        var source = Source("src", "AgTerm.App", "MainWindow.xaml.cs");
        Assert.Contains("host.Host.Actions.Pick.AnswerAsk(askId, index);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlAskResult.Answered(", source, StringComparison.Ordinal);
        Assert.Contains("AskOverlay.Open(pending, host.Host.Actions.Pick.AskDraft, _rendererSettings.FontSize, takeFocus: _windowIsActive);", source, StringComparison.Ordinal);
        Assert.Contains("AskOverlay.DraftChanged += (askId, text) => _host?.Host.Actions.Pick.UpdateAskDraft(askId, draft => draft.Text = text);", source, StringComparison.Ordinal);
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
        // a question with a field takes the full panel width
        Assert.Contains("if (ask.Width is not null || ask.Form is AskForm.Input) Panel.Width = maxWidth;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_field_validates_each_edit_and_each_paste()
    {
        var code = Source("src", "AgTerm.App", "AskView.xaml.cs");
        Assert.Matches(new Regex(
            @"if \(_ask\?\.Form is AskForm\.Input input && !AskFieldText\.Accepts\(AskFieldText\.FromTextBox\(args\.NewText, input\.Field\.Mode\), input\.Field\.Mode\)\)\s*args\.Cancel = true;"),
            code);
        Assert.Matches(new Regex(@"private async void OnInputPaste\(object sender, TextControlPasteEventArgs e\)\s*\{\s*e\.Handled = true;"), code);
        Assert.Contains("AskFieldText.PasteInsertion(clipboard, input.Field.Mode)", code, StringComparison.Ordinal);
        Assert.Contains("DraftChanged?.Invoke(ask.Id, AskFieldText.FromTextBox(InputBox.Text, input.Field.Mode));", code, StringComparison.Ordinal);
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
