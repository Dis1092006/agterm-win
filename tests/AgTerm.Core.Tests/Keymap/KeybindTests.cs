using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Keymap;

public class KeybindTests
{
    [Fact]
    public void SimpleChordParses()
    {
        var keybind = Keybinds.Parse("cmd+shift+e");
        Assert.NotNull(keybind);
        Assert.Single(keybind!);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "e"), keybind[0]);
    }

    [Fact]
    public void LeaderSequenceParsesWithSpacesAndMixedCase()
    {
        var keybind = Keybinds.Parse("Ctrl + A > b");
        Assert.Equal(new[]
        {
            new Chord(Modifiers.Control, "a"),
            new Chord(Modifiers.None, "b"),
        }, keybind);
    }

    [Theory]
    [InlineData("control+x", Modifiers.Control)]
    [InlineData("ctrl+x", Modifiers.Control)]
    [InlineData("command+x", Modifiers.Command)]
    [InlineData("cmd+x", Modifiers.Command)]
    [InlineData("opt+x", Modifiers.Option)]
    [InlineData("option+x", Modifiers.Option)]
    [InlineData("alt+x", Modifiers.Option)]
    [InlineData("shift+x", Modifiers.Shift)]
    public void ModifierAliasesAreRecognized(string text, Modifiers expected)
    {
        Assert.Equal(expected, Keybinds.Parse(text)![0].Mods);
    }

    [Theory]
    [InlineData("tab")]
    [InlineData("space")]
    [InlineData("return")]
    [InlineData("delete")]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("up")]
    [InlineData("down")]
    public void NamedKeysParse(string key)
    {
        Assert.Equal(key, Keybinds.Parse("cmd+" + key)![0].Key);
    }

    [Fact]
    public void BareSingleCharacterParses()
    {
        Assert.Equal(new Chord(Modifiers.None, "x"), Keybinds.Parse("x")![0]);
    }

    [Theory]
    [InlineData("")] // empty
    [InlineData("cmd+a>")] // trailing leader separator → empty chord
    [InlineData("cmd+")] // trailing joiner → empty token
    [InlineData("cmd")] // no base key
    [InlineData("cmd+a+b")] // two base keys
    [InlineData("hyper+a")] // unknown modifier word becomes a 5-char base key → rejected
    [InlineData("esc")] // reserved as the leader abort, not bindable
    [InlineData("f1")] // a named key the runner can't produce
    public void MalformedKeybindsAreRejected(string text)
    {
        Assert.Null(Keybinds.Parse(text));
    }

    [Fact]
    public void DisplayStringUsesFixedModifierOrder()
    {
        var chord = new Chord(Modifiers.Shift | Modifiers.Option | Modifiers.Command | Modifiers.Control, "e");
        Assert.Equal("ctrl+cmd+opt+shift+e", chord.DisplayString);
    }

    [Fact]
    public void GlyphStringUsesMacOsOrderAndSymbols()
    {
        Assert.Equal("⌘N", new Chord(Modifiers.Command, "n").GlyphString);
        Assert.Equal("⌃⌥⇧⌘E", new Chord(
            Modifiers.Control | Modifiers.Option | Modifiers.Shift | Modifiers.Command, "e").GlyphString);
        Assert.Equal("⇧⌘↩", new Chord(Modifiers.Command | Modifiers.Shift, "return").GlyphString);
        Assert.Equal("⌥⌘←", new Chord(Modifiers.Command | Modifiers.Option, "left").GlyphString);
    }

    [Theory]
    [InlineData(Modifiers.Control, "tab", true)] // the MRU switcher owns ctrl+tab...
    [InlineData(Modifiers.Control | Modifiers.Shift, "tab", true)] // ...with ANY extra modifiers
    [InlineData(Modifiers.Control | Modifiers.Command, "tab", true)]
    [InlineData(Modifiers.Command, "tab", false)] // tab without ctrl is free
    [InlineData(Modifiers.Control, "1", true)] // pane focus: ctrl as the SOLE modifier
    [InlineData(Modifiers.Control, "2", true)]
    [InlineData(Modifiers.Control | Modifiers.Shift, "1", false)] // extra modifier frees it
    [InlineData(Modifiers.Control, "3", false)]
    public void ReservedMonitorChordsMirrorTheMonitors(Modifiers mods, string key, bool reserved)
    {
        Assert.Equal(reserved, Keybinds.IsReservedMonitorChord(new Chord(mods, key)));
    }

    [Fact]
    public void ConflictsCoverDuplicatesAndPrefixes()
    {
        var duplicate1 = new CustomCommand("a", "echo", "cmd+shift+e");
        var duplicate2 = new CustomCommand("b", "echo", "cmd+shift+e");
        var leader = new CustomCommand("c", "echo", "ctrl+a>b");
        var prefix = new CustomCommand("d", "echo", "ctrl+a");
        var paletteOnly = new CustomCommand("e", "echo", "");

        var conflicts = Keybinds.Conflicts(new[] { duplicate1, duplicate2, leader, prefix, paletteOnly });

        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, c => c.First == duplicate1.Id && c.Second == duplicate2.Id);
        Assert.Contains(conflicts, c => c.First == leader.Id && c.Second == prefix.Id);
    }
}
