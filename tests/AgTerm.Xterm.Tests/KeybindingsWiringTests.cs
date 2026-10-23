namespace AgTerm.Xterm.Tests;

public sealed class KeybindingsWiringTests
{
    [Fact]
    public void Read_only_overlay_has_grouped_rows_search_keyboard_close_and_contained_focus()
    {
        var xaml = Source("KeybindingsView.xaml");
        Assert.Contains("TextChanged=\"OnQueryChanged\"", xaml);
        Assert.Contains("PreviewKeyDown=\"OnPreviewKeyDown\"", xaml);
        Assert.Contains("TabFocusNavigation=\"Cycle\"", xaml);
        Assert.Contains("IsSourceGrouped=\"True\"", xaml);
        Assert.Contains("SelectionMode=\"None\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"Search keyboard shortcuts\"", xaml);
        var code = Source("KeybindingsView.xaml.cs");
        Assert.Contains("VirtualKey.Escape", code);
        Assert.Contains("QueryBox.Focus(FocusState.Programmatic)", code);
        Assert.Contains("_controller.Refresh(groups)", code);
        Assert.Contains("_controller.SetQuery(QueryBox.Text)", code);
        Assert.DoesNotContain("ItemAccepted", code);
    }

    [Fact]
    public void Window_wires_help_fixed_hotkey_modal_guards_reload_and_focus_return()
    {
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("Click=\"OnKeyboardShortcutsClick\"", xaml);
        Assert.Contains("KeyboardAcceleratorTextOverride=\"Ctrl+Shift+F1\"", xaml);
        Assert.Contains("x:Name=\"KeybindingsOverlay\"", xaml);
        var window = Source("MainWindow.xaml.cs");
        Assert.Contains("page.KeyboardShortcutsRequested +=", window);
        Assert.Contains("VirtualKey.F1", window);
        Assert.Contains("KeybindingsOverlay.IsOpen", window);
        Assert.Contains("KeybindingsOverlay.FocusQuery()", window);
        Assert.Contains("private async Task CloseKeybindingsAsync", window);
        Assert.Contains("KeybindingsOverlay.Close();", window);
        Assert.Contains("FocusActiveTerminal();", window);
        Assert.Contains("KeybindingsOverlay.Refresh(", window);
        Assert.Contains("host.Host.Actions.KeymapDiagnostics", window);
    }

    [Fact]
    public void Terminal_questions_close_the_shortcuts_view_and_restore_terminal_focus_before_drawing()
    {
        var window = Source("MainWindow.xaml.cs");
        var start = window.IndexOf("private async Task ApplyTerminalAskAsync(", StringComparison.Ordinal);
        var method = window[start..window.IndexOf("\n    }", start, StringComparison.Ordinal)];
        Assert.Contains("if (view is not null && KeybindingsOverlay.IsOpen)", method);
        Assert.Contains("CloseKeybindingsAsync()", method);
        Assert.True(method.IndexOf("CloseKeybindingsAsync", StringComparison.Ordinal) <
            method.IndexOf("page.ApplyAskAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Deferred_search_focus_cannot_run_after_the_overlay_closes()
    {
        var code = Source("KeybindingsView.xaml.cs");
        Assert.Matches(@"private void FocusSearch\(\)\s*\{\s*if \(IsOpen\)\s*QueryBox.Focus\(FocusState.Programmatic\);", code);
        Assert.Matches(@"public void Close\(\)\s*\{\s*_controller.Close\(\);\s*Visibility = Visibility.Collapsed;", code);
        Assert.Contains("if (!IsOpen || args.NewFocusedElement is not { } target)", code);
    }

    private static string Source(string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "AgTerm.App", file));
        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
