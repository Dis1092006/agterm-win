using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests;

public sealed class GlobalHotkeyTests
{
    [Fact]
    public void Global_hotkey_is_optional_and_independent_of_local_conflicts()
    {
        Assert.Null(AgTerm.Core.Keymap.Keymap.Empty.GlobalHotkey);
        var (keymap, diagnostics) = KeymapParser.Parse("global-hotkey opt+`\nmap opt+` quick_terminal");
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Option, "`"), keymap.GlobalHotkey);
        Assert.Equal(keymap.GlobalHotkey, keymap.EquivalentFor(BuiltinAction.QuickTerminal));
    }

    [Theory]
    [InlineData("", "global-hotkey needs a chord")]
    [InlineData("ctrl+a extra", "global-hotkey takes one chord, found 'ctrl+a extra'")]
    [InlineData("ctrl+a>b", "invalid global-hotkey chord 'ctrl+a>b'")]
    [InlineData("ctrl+a|ctrl+b", "invalid global-hotkey chord 'ctrl+a|ctrl+b'")]
    [InlineData("a", "global-hotkey 'a' must include a modifier")]
    [InlineData("ctrl+é", "global-hotkey 'ctrl+é' names no key position; write a shifted symbol as shift+<base>")]
    [InlineData("ctrl+!", "global-hotkey 'ctrl+!' names no key position; write a shifted symbol as shift+<base>")]
    public void Invalid_line_preserves_the_last_valid_chord(string token, string message)
    {
        var (keymap, diagnostics) = KeymapParser.Parse($"global-hotkey ctrl+opt+space\nglobal-hotkey {token}");
        Assert.Equal(new Chord(Modifiers.Control | Modifiers.Option, "space"), keymap.GlobalHotkey);
        Assert.Equal(new KeymapDiagnostic(2, message), Assert.Single(diagnostics));
    }

    [Fact]
    public void Last_valid_line_wins_and_comments_are_stripped()
    {
        var (keymap, diagnostics) = KeymapParser.Parse("global-hotkey ctrl+a\r\nglobal-hotkey opt+shift+` # quick");
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Option | Modifiers.Shift, "`"), keymap.GlobalHotkey);
    }
}
