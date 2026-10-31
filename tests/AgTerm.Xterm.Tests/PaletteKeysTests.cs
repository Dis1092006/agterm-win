using AgTerm.App;
using AgTerm.Core.Keymap;

namespace AgTerm.Xterm.Tests;

public sealed class PaletteKeysTests
{
    [Theory]
    [InlineData(false, true, false, "n")]
    [InlineData(false, true, false, "s")]
    [InlineData(false, true, false, "z")]
    [InlineData(true, false, true, "p")]
    [InlineData(true, false, true, "t")]
    [InlineData(true, true, false, "n")]
    [InlineData(true, true, false, "up")]
    [InlineData(true, false, false, "`")]
    public void Window_chords_with_ctrl_or_alt_pass_from_the_palette_query(bool ctrl, bool alt, bool shift, string key)
    {
        Assert.True(PaletteKeys.PassesToWindow(new PhysicalChord(ctrl, alt, shift, key)));
    }

    [Theory]
    [InlineData(true, false, false, "a")]
    [InlineData(true, false, false, "c")]
    [InlineData(true, false, false, "v")]
    [InlineData(true, false, false, "x")]
    [InlineData(true, false, false, "z")]
    [InlineData(true, false, true, "z")]
    [InlineData(true, false, false, "y")]
    [InlineData(true, false, false, "delete")]
    [InlineData(true, false, false, "left")]
    [InlineData(true, false, true, "right")]
    [InlineData(true, false, false, "up")]
    [InlineData(true, false, true, "down")]
    [InlineData(false, false, false, "n")]
    [InlineData(false, false, true, "n")]
    [InlineData(false, false, false, "return")]
    public void Text_editing_and_plain_keys_stay_in_the_palette_query(bool ctrl, bool alt, bool shift, string key)
    {
        Assert.False(PaletteKeys.PassesToWindow(new PhysicalChord(ctrl, alt, shift, key)));
    }

    [Theory]
    [InlineData(BuiltinAction.SessionPalette)]
    [InlineData(BuiltinAction.CommandPalette)]
    [InlineData(BuiltinAction.CustomCommandPalette)]
    [InlineData(BuiltinAction.ShowAttention)]
    [InlineData(BuiltinAction.SelectTheme)]
    public void A_palette_launcher_keeps_the_open_palette_for_its_own_toggle(BuiltinAction action)
    {
        Assert.False(PaletteKeys.ClosesPaletteFirst(action));
    }

    [Theory]
    [InlineData(BuiltinAction.NewSession)]
    [InlineData(BuiltinAction.ToggleSidebar)]
    [InlineData(BuiltinAction.RenameWindow)]
    [InlineData(BuiltinAction.NewWindow)]
    [InlineData(BuiltinAction.Dashboard)]
    public void Any_other_action_closes_the_palette_before_it_runs(BuiltinAction action)
    {
        Assert.True(PaletteKeys.ClosesPaletteFirst(action));
    }

    private const int Enter = 0x0D;
    private const int Escape = 0x1B;
    private const int Up = 0x26;
    private const int Down = 0x28;

    [Theory]
    [InlineData(Enter, false, true, false)]
    [InlineData(Enter, true, false, true)]
    [InlineData(Up, true, true, false)]
    [InlineData(Down, true, true, false)]
    public void A_window_chord_on_enter_or_an_arrow_leaves_the_palette_list_to_the_window(
        int key, bool control, bool alt, bool shift)
    {
        Assert.True(PaletteKeys.LeavesToWindow(key, control, alt, shift));
    }

    [Theory]
    [InlineData(Enter, false, false, false)]
    [InlineData(Enter, false, false, true)]
    [InlineData(Up, false, false, false)]
    [InlineData(Down, false, false, false)]
    [InlineData(Up, true, false, false)]
    [InlineData(Escape, true, true, false)]
    public void Enter_arrows_and_escape_without_a_window_chord_stay_with_the_palette(
        int key, bool control, bool alt, bool shift)
    {
        Assert.False(PaletteKeys.LeavesToWindow(key, control, alt, shift));
    }

    [Fact]
    public void Palette_view_lets_a_window_chord_on_its_list_keys_bubble()
    {
        var code = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "PaletteView.xaml.cs"));

        var keyDown = Method(code, "private void OnQueryBoxKeyDown(");
        var leaves = keyDown.IndexOf("PaletteKeys.LeavesToWindow(", StringComparison.Ordinal);
        Assert.True(leaves >= 0, "PaletteKeys.LeavesToWindow( not found");
        Assert.True(leaves < keyDown.IndexOf("switch (e.Key)", StringComparison.Ordinal));
    }

    // MainWindow is a WinUI type the test project does not compile, so its use of the rules is
    // pinned in its source.
    [Fact]
    public void Main_window_routes_palette_query_chords_through_the_palette_rules()
    {
        var code = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "AgTerm.App", "MainWindow.xaml.cs"));

        var keyDown = Method(code, "private void OnRootKeyDown(");
        Assert.Contains("PaletteKeys.PassesToWindow(", keyDown, StringComparison.Ordinal);
        Assert.Contains("_shownPickId", keyDown, StringComparison.Ordinal);
        var handleChord = Method(code, "private bool HandleChord(");
        Assert.Contains("ExecuteBuiltinHotkey(action, sourcePaneId)", handleChord, StringComparison.Ordinal);
        var executeBuiltin = Method(code, "internal bool ExecuteBuiltinHotkey(");
        Assert.Contains("PaletteKeys.ClosesPaletteFirst(", executeBuiltin, StringComparison.Ordinal);
    }

    private static string Method(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found");
        var body = code[start..];
        return body[..body.IndexOf("\n    }", StringComparison.Ordinal)];
    }

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
