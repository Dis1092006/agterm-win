using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Keymap;

/// <summary>WindowsShortcutText — upstream's helpHint ("Dashboard (⌘⇧D)") in Windows form: the chord
/// a press must actually make on this platform, written the way Windows writes it.</summary>
public class WindowsShortcutTextTests
{
    [Theory]
    [InlineData(true, false, true, "d", "Ctrl+Shift+D")]
    [InlineData(true, true, true, "k", "Ctrl+Alt+Shift+K")]
    [InlineData(false, true, false, "`", "Alt+`")]
    [InlineData(true, false, false, "return", "Ctrl+Enter")]
    [InlineData(true, false, false, "delete", "Ctrl+Backspace")]
    [InlineData(false, true, false, "tab", "Alt+Tab")]
    [InlineData(false, true, false, "space", "Alt+Space")]
    [InlineData(true, true, false, "up", "Ctrl+Alt+Up")]
    [InlineData(false, true, false, "+", "Alt+Plus")]
    [InlineData(false, true, false, "-", "Alt+Minus")]
    public void A_physical_chord_reads_the_windows_way(bool ctrl, bool alt, bool shift, string key, string expected)
    {
        Assert.Equal(expected, WindowsShortcutText.Format(new PhysicalChord(ctrl, alt, shift, key)));
    }

    [Fact]
    public void An_action_shows_the_chord_that_runs_it()
    {
        Assert.Equal("Ctrl+Shift+D", WindowsShortcutText.For(AgTerm.Core.Keymap.Keymap.Empty, BuiltinAction.Dashboard));
        Assert.Equal("Alt+D", WindowsShortcutText.For(AgTerm.Core.Keymap.Keymap.Empty, BuiltinAction.ToggleSplit));
    }

    [Fact]
    public void A_keyless_or_shadowed_action_shows_no_chord()
    {
        Assert.Null(WindowsShortcutText.For(AgTerm.Core.Keymap.Keymap.Empty, BuiltinAction.RenameWindow));
        // Ctrl+Alt+Up runs previous_session (PLATFORM-NOTES keyboard table)
        Assert.Null(WindowsShortcutText.For(AgTerm.Core.Keymap.Keymap.Empty, BuiltinAction.PreviousAttentionSession));
    }

    [Fact]
    public void A_user_rebinding_shows_its_chord()
    {
        var keymap = new AgTerm.Core.Keymap.Keymap(
            new Dictionary<BuiltinAction, Chord> { [BuiltinAction.Dashboard] = new(Modifiers.Option, "g") },
            []);

        Assert.Equal("Alt+G", WindowsShortcutText.For(keymap, BuiltinAction.Dashboard));
    }

    [Fact]
    public void Hint_appends_the_chord_like_upstreams_help_hint()
    {
        Assert.Equal("Dashboard (Ctrl+Shift+D)", WindowsShortcutText.Hint("Dashboard", AgTerm.Core.Keymap.Keymap.Empty, BuiltinAction.Dashboard));
        Assert.Equal("Rename", WindowsShortcutText.Hint("Rename", AgTerm.Core.Keymap.Keymap.Empty, BuiltinAction.RenameWindow));
    }
}
