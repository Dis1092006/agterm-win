using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Keymap;

public class KeymapParserTests
{
    private static (AgTerm.Core.Keymap.Keymap Keymap, List<KeymapDiagnostic> Diagnostics) Parse(string text) =>
        KeymapParser.Parse(text);

    // -- lines, comments, normalization -------------------------------------

    [Fact]
    public void EmptyTextYieldsEmptyKeymapWithNoDiagnostics()
    {
        var (keymap, diagnostics) = Parse("");
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Empty(keymap.Commands);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void BlankLinesAndFullLineCommentsAreSkipped()
    {
        var (keymap, diagnostics) = Parse("\n   \n# a comment\n  # indented comment\n");
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void InlineCommentIsStrippedOnlyWhenPrecededByWhitespace()
    {
        var (keymap, diagnostics) = Parse("map cmd+e toggle_split  # rebind\n");
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Command, "e"), keymap.BuiltinOverrides[BuiltinAction.ToggleSplit]);
    }

    [Fact]
    public void HashInsideQuotesIsKept()
    {
        var (keymap, diagnostics) = Parse(
            "command \"tag\" echo \"issue #42\"\n" +
            "command \"note\" git commit -m 'fix #42'\n");
        Assert.Empty(diagnostics);
        Assert.Equal("echo \"issue #42\"", keymap.Commands[0].Command);
        Assert.Equal("git commit -m 'fix #42'", keymap.Commands[1].Command);
    }

    [Fact]
    public void HashWithoutPrecedingWhitespaceIsKept()
    {
        var (keymap, diagnostics) = Parse("command \"x\" echo a#b\n");
        Assert.Empty(diagnostics);
        Assert.Equal("echo a#b", keymap.Commands[0].Command);
    }

    [Fact]
    public void CrlfAndLoneCrLineEndingsAreNormalized()
    {
        // without normalization `toggle_split\r` would read as an unknown action, and a lone-CR
        // file would collapse into one line.
        var (crlf, crlfDiags) = Parse("map cmd+e toggle_split\r\nmap cmd+g toggle_search\r\n");
        Assert.Empty(crlfDiags);
        Assert.Equal(2, crlf.BuiltinOverrides.Count);

        var (cr, crDiags) = Parse("map cmd+e toggle_split\rmap cmd+g toggle_search\r");
        Assert.Empty(crDiags);
        Assert.Equal(2, cr.BuiltinOverrides.Count);
    }

    [Fact]
    public void TabSeparatedTokensParse()
    {
        var (keymap, diagnostics) = Parse("map\tcmd+e\ttoggle_split\n");
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Command, "e"), keymap.BuiltinOverrides[BuiltinAction.ToggleSplit]);
    }

    [Fact]
    public void UnknownVerbIsDiagnosedAndSkipped()
    {
        var (keymap, diagnostics) = Parse("bind cmd+e toggle_split\n");
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Equal(new KeymapDiagnostic(1, "unknown verb 'bind'"), Assert.Single(diagnostics));
    }

    [Fact]
    public void BadLineDoesNotDiscardTheRestOfTheFile()
    {
        var (keymap, diagnostics) = Parse("map broken\nmap cmd+e toggle_split\n");
        Assert.Single(diagnostics);
        Assert.Equal(1, diagnostics[0].Line);
        Assert.True(keymap.BuiltinOverrides.ContainsKey(BuiltinAction.ToggleSplit));
    }

    // -- map lines -----------------------------------------------------------

    [Theory]
    [InlineData("map cmd+e", "map requires a chord and an action")]
    [InlineData("map", "map requires a chord and an action")]
    [InlineData("map xyz+q toggle_split", "invalid chord 'xyz+q'")]
    [InlineData("map ctrl+a>g toggle_split", "built-in shortcut cannot be a leader sequence")]
    [InlineData("map ctrl+tab toggle_split", "chord 'ctrl+tab' is a reserved shortcut; map skipped")]
    [InlineData("map ctrl+1 toggle_split", "chord 'ctrl+1' is a reserved shortcut; map skipped")]
    [InlineData("map left toggle_split", "bare arrow chord 'left' needs a modifier; map skipped")]
    [InlineData("map cmd+e explode", "unknown action 'explode'")]
    public void MapLineFailuresProduceVerbatimDiagnostics(string line, string message)
    {
        var (keymap, diagnostics) = Parse(line + "\n");
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Equal(new KeymapDiagnostic(1, message), Assert.Single(diagnostics));
    }

    [Fact]
    public void ArrowWithModifierIsMappable()
    {
        var (keymap, diagnostics) = Parse("map ctrl+shift+left focus_left_pane\n");
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Control | Modifiers.Shift, "left"),
            keymap.BuiltinOverrides[BuiltinAction.FocusLeftPane]);
    }

    [Fact]
    public void RemappingTheSameActionIsLastWins()
    {
        var (keymap, diagnostics) = Parse("map cmd+e toggle_split\nmap cmd+g toggle_split\n");
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Command, "g"), keymap.BuiltinOverrides[BuiltinAction.ToggleSplit]);
    }

    // -- order-independent builtin conflict resolution ------------------------

    [Theory]
    [InlineData("map cmd+d new_session\nmap cmd+shift+d toggle_split\n")]
    [InlineData("map cmd+shift+d toggle_split\nmap cmd+d new_session\n")]
    public void SwapSucceedsInEitherOrderBecauseResolutionIsFinalState(string text)
    {
        // cmd+d is toggle_split's default and cmd+shift+d is dashboard's… no: cmd+shift+d IS
        // dashboard's default, so move dashboard too to keep the final state conflict-free.
        var (keymap, diagnostics) = Parse("map cmd+shift+g dashboard\n" + text);
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Command, "d"), keymap.BuiltinOverrides[BuiltinAction.NewSession]);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "d"),
            keymap.BuiltinOverrides[BuiltinAction.ToggleSplit]);
    }

    [Fact]
    public void OverrideCollidingWithAnUnmovedDefaultLoses()
    {
        // cmd+n is new_session's default and new_session is not remapped → the map is dropped.
        var (keymap, diagnostics) = Parse("map cmd+n toggle_split\n");
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Equal(new KeymapDiagnostic(1, "chord conflicts with built-in 'new_session'; map skipped"),
            Assert.Single(diagnostics));
    }

    [Fact]
    public void TwoOverridesOnTheSameChordDropTheLaterOne()
    {
        var (keymap, diagnostics) = Parse("map cmd+e new_session\nmap cmd+e toggle_split\n");
        Assert.Equal(new Chord(Modifiers.Command, "e"), keymap.BuiltinOverrides[BuiltinAction.NewSession]);
        Assert.False(keymap.BuiltinOverrides.ContainsKey(BuiltinAction.ToggleSplit));
        Assert.Equal(new KeymapDiagnostic(2, "chord conflicts with built-in 'new_session'; map skipped"),
            Assert.Single(diagnostics));
    }

    // -- command lines ---------------------------------------------------------

    [Fact]
    public void CommandWithChordAndShellLineParses()
    {
        var (keymap, diagnostics) = Parse("command \"Deploy\" cmd+shift+e ./deploy.sh {AGT_SESSION_PWD}\n");
        Assert.Empty(diagnostics);
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("Deploy", command.Name);
        Assert.Equal("cmd+shift+e", command.Shortcut);
        Assert.Equal("./deploy.sh {AGT_SESSION_PWD}", command.Command); // tokens kept verbatim
    }

    [Fact]
    public void CommandNameMayContainSpaces()
    {
        var (keymap, diagnostics) = Parse("command \"Open PR page\" gh pr view --web\n");
        Assert.Empty(diagnostics);
        Assert.Equal("Open PR page", keymap.Commands[0].Name);
        Assert.Equal("", keymap.Commands[0].Shortcut);
        Assert.Equal("gh pr view --web", keymap.Commands[0].Command);
    }

    [Fact]
    public void FirstTokenThatDoesNotParseAsAChordBelongsToTheShellLine()
    {
        var (keymap, diagnostics) = Parse("command \"List\" ls -la\n");
        Assert.Empty(diagnostics);
        Assert.Equal("", keymap.Commands[0].Shortcut);
        Assert.Equal("ls -la", keymap.Commands[0].Command);
    }

    [Fact]
    public void ModifierlessChordTokenIsDiagnosedAndTheLineBecomesPaletteOnly()
    {
        // "g" parses as a chord but has no modifier: a custom shortcut may not shadow a bare
        // terminal key. The whole remainder stays the shell line.
        var (keymap, diagnostics) = Parse("command \"Git status\" g status\n");
        Assert.Equal(new KeymapDiagnostic(1,
                "command 'Git status' shortcut 'g' must include a modifier; treating the line as palette-only"),
            Assert.Single(diagnostics));
        Assert.Equal("", keymap.Commands[0].Shortcut);
        Assert.Equal("g status", keymap.Commands[0].Command);
    }

    [Theory]
    [InlineData("command Deploy cmd+e echo", "command requires a quoted name")]
    [InlineData("command \"Deploy", "command requires a quoted name")]
    [InlineData("command \"Deploy\"", "command 'Deploy' has no shell line")]
    [InlineData("command \"Deploy\" cmd+shift+e", "command 'Deploy' has no shell line")]
    public void CommandLineFailuresProduceVerbatimDiagnostics(string line, string message)
    {
        var (keymap, diagnostics) = Parse(line + "\n");
        Assert.Empty(keymap.Commands);
        Assert.Equal(new KeymapDiagnostic(1, message), Assert.Single(diagnostics));
    }

    [Fact]
    public void LeaderShortcutsAreAllowedForCustomCommands()
    {
        var (keymap, diagnostics) = Parse("command \"Grep\" ctrl+a>g rg TODO\n");
        Assert.Empty(diagnostics);
        Assert.Equal("ctrl+a>g", keymap.Commands[0].Shortcut);
    }

    // -- cross-section validation -----------------------------------------------

    [Fact]
    public void CustomShortcutCollidingWithAnActiveBuiltinIsDroppedButStaysInThePalette()
    {
        var (keymap, diagnostics) = Parse("command \"Dup\" cmd+d echo hi\n");
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("", command.Shortcut); // dropped: cmd+d is toggle_split's active default
        Assert.Equal("echo hi", command.Command); // …but the command stays, palette-only
        Assert.Equal(new KeymapDiagnostic(0,
                "custom command 'Dup' shortcut 'cmd+d' conflicts with a built-in; keybind dropped"),
            Assert.Single(diagnostics));
    }

    [Fact]
    public void CustomLeaderStartingWithABuiltinChordIsEquallyShadowed()
    {
        var (_, diagnostics) = Parse("command \"Seq\" cmd+d>x echo hi\n");
        Assert.Equal(new KeymapDiagnostic(0,
                "custom command 'Seq' shortcut 'cmd+d>x' conflicts with a built-in; keybind dropped"),
            Assert.Single(diagnostics));
    }

    [Fact]
    public void CustomMayReuseADefaultTheUserMovedOff()
    {
        // final-state validation: the command line comes BEFORE the map that frees cmd+d.
        var (keymap, diagnostics) = Parse(
            "command \"Dup\" cmd+d echo hi\n" +
            "map cmd+shift+g toggle_split\n");
        Assert.Empty(diagnostics);
        Assert.Equal("cmd+d", keymap.Commands[0].Shortcut);
    }

    [Fact]
    public void ReservedMonitorChordAtAnyLeaderPositionDropsTheKeybind()
    {
        var (keymap, diagnostics) = Parse("command \"Dead\" ctrl+a>ctrl+1 echo hi\n");
        Assert.Equal("", keymap.Commands[0].Shortcut);
        Assert.Equal(new KeymapDiagnostic(0,
                "custom command 'Dead' shortcut 'ctrl+a>ctrl+1' conflicts with a reserved shortcut; keybind dropped"),
            Assert.Single(diagnostics));
    }

    [Fact]
    public void CustomVersusCustomConflictDropsBothKeybinds()
    {
        var (keymap, diagnostics) = Parse(
            "command \"One\" cmd+shift+e echo one\n" +
            "command \"Two\" cmd+shift+e echo two\n");
        Assert.Equal("", keymap.Commands[0].Shortcut);
        Assert.Equal("", keymap.Commands[1].Shortcut);
        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(new KeymapDiagnostic(0,
            "custom command 'One' shortcut 'cmd+shift+e' conflicts with custom command 'Two'; keybind dropped"),
            diagnostics[0]);
        Assert.Equal(new KeymapDiagnostic(0,
            "custom command 'Two' shortcut 'cmd+shift+e' conflicts with custom command 'One'; keybind dropped"),
            diagnostics[1]);
    }

    [Fact]
    public void CustomPrefixConflictDropsBothKeybinds()
    {
        var (keymap, _) = Parse(
            "command \"Long\" ctrl+a>b echo one\n" +
            "command \"Short\" ctrl+a echo two\n");
        Assert.All(keymap.Commands, c => Assert.Equal("", c.Shortcut));
    }
}

public class KeymapStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "agterm-keymap-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void MissingFileLoadsEmptyWithNoDiagnostics()
    {
        var (keymap, diagnostics) = new KeymapStore(_dir).Load();
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Empty(keymap.Commands);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ExistingFileIsParsed()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "keymap.conf"), "map cmd+e toggle_split\r\n");

        var (keymap, diagnostics) = new KeymapStore(_dir).Load();

        Assert.Empty(diagnostics);
        Assert.True(keymap.BuiltinOverrides.ContainsKey(BuiltinAction.ToggleSplit));
    }
}
