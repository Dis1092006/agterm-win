namespace AgTerm.Xterm.Tests;

public sealed class AutoFollowWiringTests
{
    [Fact]
    public void Background_DOM_focus_feedback_does_not_change_the_saved_pane()
    {
        var source = Source("MainWindow.xaml.cs");
        var feedback = source[source.IndexOf("private void OnPagePaneFocusChanged(", StringComparison.Ordinal)..];
        feedback = feedback[..feedback.IndexOf("\n    }", StringComparison.Ordinal)];
        var guard = feedback.IndexOf("WindowsTaskbarFlash.IsForegroundWindow(", StringComparison.Ordinal);
        var apply = feedback.IndexOf("host.ApplyPaneFocusAsync(paneId)", StringComparison.Ordinal);
        Assert.True(guard >= 0 && apply > guard, "Background browser focus must not overwrite the selected pane.");
    }

    [Fact]
    public void Every_accepted_pane_requests_deferred_focus_and_background_requests_do_not_focus_Chromium()
    {
        var source = Source("MainWindow.xaml.cs");
        var accepted = source[source.IndexOf("private void ApplyAcceptedBaseLayout(", StringComparison.Ordinal)..];
        accepted = accepted[..accepted.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("FocusActiveTerminal();", accepted);
        Assert.DoesNotContain("if (_windowIsActive", accepted);
        var focus = source[source.IndexOf("private void FocusTerminal(", StringComparison.Ordinal)..];
        focus = focus[..focus.IndexOf("\n    }", StringComparison.Ordinal)];
        Assert.Contains("modalOpen: UiAutoFollowSuppressed", focus);
        Assert.Contains("WindowsTaskbarFlash.IsForegroundWindow(", focus);
        Assert.Contains("if (!foreground) return;", focus);
        var defer = focus.IndexOf("_terminalFocus.Request(", StringComparison.Ordinal);
        var guard = focus.IndexOf("if (!_windowIsActive || UiAutoFollowSuppressed) return;", StringComparison.Ordinal);
        var chromium = focus.IndexOf("surface.Pane.Terminal.FocusAsync()", StringComparison.Ordinal);
        Assert.True(defer >= 0 && guard > defer && chromium > guard);
        Assert.Contains("DispatcherQueue.TryEnqueue(RestorePendingTerminalFocus)", source);
    }

    [Fact]
    public void Settings_uses_effective_toggle_values_inversion_and_enabled_state()
    {
        var source = Source("SettingsWindow.cs");
        Assert.Contains("editor.ToggleValue(field.Key)", source);
        Assert.Contains("editor.SetToggleAsync(field.Key, toggle.IsOn)", source);
        Assert.Contains("editor.IsEnabled(field.Key)", source);
    }

    [Fact]
    public void Terminal_intercepted_chords_and_session_switcher_keys_note_activity()
    {
        var source = Source("MainWindow.xaml.cs");
        var chord = source[source.IndexOf("private bool HandleChord(", StringComparison.Ordinal)..];
        Assert.Contains("NoteUserActivity();", chord[..chord.IndexOf("\n    }", StringComparison.Ordinal)]);
        var switcher = source[source.IndexOf("private void HandleSwitcherKey(", StringComparison.Ordinal)..];
        Assert.Contains("NoteUserActivity();", switcher[..switcher.IndexOf("\n    }", StringComparison.Ordinal)]);
    }

    [Fact]
    public void App_bridges_follow_without_activating_a_background_window_and_suppresses_nonterminal_editors()
    {
        var source = Source("MainWindow.xaml.cs");
        Assert.Contains("host.AutoFollowed +=", source);
        Assert.Contains("RevealAutoFollowedAsync(", source);
        Assert.Contains("WindowsTaskbarFlash.IsForegroundWindow(", source);
        Assert.Contains("SetUiAutoFollowSuppressedAsync(", source);
        Assert.Contains("Sidebar.RenameStateChanged +=", source);
        Assert.Contains("RecentSessionsFlyout.Closed +=", source);
        Assert.Contains("Content.GettingFocus +=", source);
        Assert.Contains("NoteUserActivityAsync(", source);
    }

    private static string Source(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return File.ReadAllText(Path.Combine(directory.FullName, "src", "AgTerm.App", name));
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
