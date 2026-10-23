namespace AgTerm.Xterm.Tests;

// upstream helpHint: a chrome button's tooltip names the shortcut that runs its action. MainWindow and
// SidebarView are WinUI types the test project does not compile, so the wiring is pinned in source;
// WindowsShortcutTextTests covers the text.
public sealed class ShortcutHintsSourceTests
{
    [Fact]
    public void The_window_keeps_the_armed_keymap_and_reapplies_the_hints()
    {
        var window = Source("MainWindow.xaml.cs");
        var arm = MethodBody(window, "private async Task PublishHotkeyManifestAsync(");

        Assert.Contains("_keymap = keymap;", arm, StringComparison.Ordinal);
        Assert.Contains("ApplyShortcutHints();", arm, StringComparison.Ordinal);
        Assert.True(arm.IndexOf("_hotkeyStates.Commit(pending.Generation)", StringComparison.Ordinal) <
            arm.IndexOf("_keymap = keymap;", StringComparison.Ordinal));
        var apply = MethodBody(window, "private void ApplyShortcutHints()");
        Assert.Contains("Sidebar.ApplyKeymap(_keymap);", apply, StringComparison.Ordinal);
        Assert.Contains("RefreshTitleBarActions(_settings);", apply, StringComparison.Ordinal);
        Assert.Contains("\"PART_PaneToggleButton\"", apply, StringComparison.Ordinal);
        Assert.Contains("WindowsShortcutText.Hint(\"Toggle Sidebar\", _keymap, BuiltinAction.ToggleSidebar)", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Title_bar_buttons_name_their_shortcuts()
    {
        var refresh = MethodBody(Source("MainWindow.xaml.cs"), "private void RefreshTitleBarActions(AppSettings settings)");

        foreach (var action in new[] { "ShowAttention", "ToggleScratch", "ToggleSplit", "Dashboard", "QuickTerminal" })
            Assert.Contains($"_keymap, BuiltinAction.{action})", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void Sidebar_footer_buttons_name_their_shortcuts()
    {
        var view = Source("SidebarView.xaml.cs");

        var apply = MethodBody(view, "public void ApplyKeymap(Keymap keymap)");
        Assert.Contains("WindowsShortcutText.Hint(\"New Workspace\", keymap, BuiltinAction.NewWorkspace)", apply, StringComparison.Ordinal);
        Assert.Contains("WindowsShortcutText.Hint(\"New Session\", keymap, BuiltinAction.NewSession)", apply, StringComparison.Ordinal);
        Assert.Contains("ApplyFlaggedHint();", apply, StringComparison.Ordinal);
        Assert.Contains("BuiltinAction.ToggleFlaggedView", MethodBody(view, "private void ApplyFlaggedHint()"), StringComparison.Ordinal);
        Assert.Contains("ApplyFlaggedHint();", MethodBody(view, "public void Apply(SidebarSnapshot snapshot)"), StringComparison.Ordinal);
    }

    // from the signature to the first line that closes a member at class indentation
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static string Source(string appFile) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", appFile));

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgTerm.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the repository root.");
    }
}
