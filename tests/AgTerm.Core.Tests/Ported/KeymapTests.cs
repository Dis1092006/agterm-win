using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Ported;

/// <summary>Ported 1:1 from agtermCore/Tests/agtermCoreTests/KeymapTests.swift.</summary>
public class KeymapTests
{
    private static (AgTerm.Core.Keymap.Keymap Keymap, List<KeymapDiagnostic> Diagnostics) ParseKeymap(string text) =>
        KeymapParser.Parse(text);

    private static AgTerm.Core.Keymap.Keymap KeymapWith(
        Dictionary<BuiltinAction, Chord>? overrides = null, List<CustomCommand>? commands = null) =>
        new(overrides ?? new Dictionary<BuiltinAction, Chord>(), commands ?? new List<CustomCommand>());

    [Fact]
    public void OverrideWinsOverDefault()
    {
        var overrideChord = new Chord(Modifiers.Command | Modifiers.Shift, "e");
        var keymap = KeymapWith(new Dictionary<BuiltinAction, Chord> { [BuiltinAction.ToggleSplit] = overrideChord });
        Assert.Equal(overrideChord, keymap.EquivalentFor(BuiltinAction.ToggleSplit));
        // sanity: the shipped default differs from the override, so this is a real win.
        Assert.NotEqual(overrideChord, BuiltinAction.ToggleSplit.DefaultChord());
    }

    [Fact]
    public void AbsentOverrideFallsBackToDefault()
    {
        var keymap = KeymapWith();
        Assert.Equal(BuiltinAction.ToggleSplit.DefaultChord(), keymap.EquivalentFor(BuiltinAction.ToggleSplit));
        Assert.Equal(new Chord(Modifiers.Command, "n"), keymap.EquivalentFor(BuiltinAction.NewSession));
    }

    [Fact]
    public void KeylessActionWithOverrideReturnsOverride()
    {
        // firstSession has no default chord; an override gives it one.
        Assert.Null(BuiltinAction.FirstSession.DefaultChord());
        var overrideChord = new Chord(Modifiers.Command, "1");
        var keymap = KeymapWith(new Dictionary<BuiltinAction, Chord> { [BuiltinAction.FirstSession] = overrideChord });
        Assert.Equal(overrideChord, keymap.EquivalentFor(BuiltinAction.FirstSession));
    }

    [Fact]
    public void KeylessActionWithoutOverrideReturnsNil()
    {
        var keymap = KeymapWith();
        Assert.Null(keymap.EquivalentFor(BuiltinAction.FirstSession));
        // an arrow-bound action resolves to its shipped default, like any other keyed action.
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Option, "left"),
            keymap.EquivalentFor(BuiltinAction.FocusLeftPane));
    }

    [Fact]
    public void ParseMapHappyPath()
    {
        var (keymap, diagnostics) = ParseKeymap("map cmd+shift+e toggle_split");
        Assert.Empty(diagnostics);
        var overrideEntry = Assert.Single(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSplit, overrideEntry.Key);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "e"), overrideEntry.Value);
        Assert.Empty(keymap.Commands);
    }

    // -- glyphHint — the shortcut shown in the palette and toolbar tooltips --

    [Fact]
    public void GlyphHintRendersDefaultChordAsGlyphs()
    {
        var keymap = KeymapWith();
        Assert.Equal("⌘N", keymap.GlyphHint(BuiltinAction.NewSession));
        Assert.Equal("⌃⌘S", keymap.GlyphHint(BuiltinAction.ToggleSidebar));
        Assert.Equal("⌘D", keymap.GlyphHint(BuiltinAction.ToggleSplit));
    }

    [Fact]
    public void GlyphHintUsesOverrideWhenPresent()
    {
        var keymap = KeymapWith(new Dictionary<BuiltinAction, Chord>
        {
            [BuiltinAction.ToggleSidebar] = new(Modifiers.Command, "k"),
        });
        Assert.Equal("⌘K", keymap.GlyphHint(BuiltinAction.ToggleSidebar));
    }

    [Fact]
    public void GlyphHintRendersArrowDefaultsAsGlyphs()
    {
        // the arrow-bound actions resolve through defaultChord like any other keyed action.
        var keymap = KeymapWith();
        Assert.Equal("⌥⌘↑", keymap.GlyphHint(BuiltinAction.PreviousSession));
        Assert.Equal("⌥⌘←", keymap.GlyphHint(BuiltinAction.FocusLeftPane));
    }

    [Fact]
    public void GlyphHintOverrideWinsOverArrowDefault()
    {
        // a user-mapped chord beats the shipped arrow default.
        var keymap = KeymapWith(new Dictionary<BuiltinAction, Chord>
        {
            [BuiltinAction.PreviousSession] = new(Modifiers.Command, "p"),
        });
        Assert.Equal("⌘P", keymap.GlyphHint(BuiltinAction.PreviousSession));
    }

    [Fact]
    public void GlyphHintIsNilForUnconfiguredAction()
    {
        // a keyless, non-arrow action shows nothing until the user maps a chord.
        var keymap = KeymapWith();
        Assert.Null(keymap.GlyphHint(BuiltinAction.ToggleFlaggedView));
        Assert.Null(keymap.GlyphHint(BuiltinAction.FirstSession));
    }

    [Fact]
    public void RebindToggleSearchResolvesThroughGenericPath()
    {
        var (keymap, diagnostics) = ParseKeymap("map cmd+shift+l toggle_search");
        Assert.Empty(diagnostics);
        var overrideChord = new Chord(Modifiers.Command | Modifiers.Shift, "l");
        var entry = Assert.Single(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSearch, entry.Key);
        Assert.Equal(overrideChord, entry.Value);
        Assert.Equal(overrideChord, keymap.EquivalentFor(BuiltinAction.ToggleSearch));
        // sanity: the default is the shipped cmd+f, so the override is a real rebind.
        Assert.Equal(new Chord(Modifiers.Command, "f"), BuiltinAction.ToggleSearch.DefaultChord());
    }

    [Fact]
    public void ParseCommandHappyPath()
    {
        var (keymap, diagnostics) = ParseKeymap("command \"Lazygit\" ctrl+a>g lazygit");
        Assert.Empty(diagnostics);
        Assert.Empty(keymap.BuiltinOverrides);
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("Lazygit", command.Name);
        Assert.Equal("ctrl+a>g", command.Shortcut);
        Assert.Equal("lazygit", command.Command);
    }

    [Fact]
    public void ParseCommandQuotedNameWithSpaces()
    {
        var (keymap, diagnostics) = ParseKeymap("command \"Open in Zed\" cmd+shift+e open -a Zed {AGT_SESSION_PWD}");
        Assert.Empty(diagnostics);
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("Open in Zed", command.Name);
        Assert.Equal("cmd+shift+e", command.Shortcut);
        Assert.Equal("open -a Zed {AGT_SESSION_PWD}", command.Command);
    }

    [Fact]
    public void ParseCommandPaletteOnlyWhenNoChord()
    {
        var (keymap, diagnostics) = ParseKeymap("command \"Deploy\" ./deploy.sh");
        Assert.Empty(diagnostics);
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("Deploy", command.Name);
        Assert.Equal("", command.Shortcut);
        Assert.Equal("./deploy.sh", command.Command);
    }

    [Fact]
    public void ParseCommandBareKeyRejectedAsShortcut()
    {
        // a modifier-less first token (a bare key) is NOT consumed as a shortcut: it would shadow
        // that key in the terminal, so the line stays palette-only with the token kept in the shell
        // line and a diagnostic is emitted.
        var (keymap, diagnostics) = ParseKeymap("command \"X\" a echo hi");
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("", command.Shortcut);
        Assert.Equal("a echo hi", command.Command);
        Assert.Contains("must include a modifier", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void ParseCommandModifierShortcutAccepted()
    {
        // a single chord WITH a modifier is a valid custom shortcut.
        var (keymap, diagnostics) = ParseKeymap("command \"X\" cmd+e echo hi");
        Assert.Empty(diagnostics);
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("cmd+e", command.Shortcut);
        Assert.Equal("echo hi", command.Command);
    }

    [Fact]
    public void ParseCommandPaletteOnlyShellTokenNotSwallowed()
    {
        // the trap: a palette-only command whose shell line starts with a single-char token must NOT
        // have that token silently bound as a shortcut.
        var (keymap, diagnostics) = ParseKeymap("command \"Check\" [ -f /tmp ] && echo ok");
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("", command.Shortcut);
        Assert.Equal("[ -f /tmp ] && echo ok", command.Command);
        Assert.Single(diagnostics);
    }

    [Fact]
    public void ParseCommandPreservesAgtTokens()
    {
        var (keymap, diagnostics) = ParseKeymap("command \"Notify\" echo {AGT_SELECTION} > {AGT_SOCKET}");
        Assert.Empty(diagnostics);
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("echo {AGT_SELECTION} > {AGT_SOCKET}", command.Command);
    }

    [Fact]
    public void ParseSkipsCommentsAndBlankLines()
    {
        var text = "# leading comment\n\nmap cmd+shift+e toggle_split  # inline comment\n" +
                   "# another comment\ncommand \"Deploy\" ./deploy.sh";
        var (keymap, diagnostics) = ParseKeymap(text);
        Assert.Empty(diagnostics);
        var entry = Assert.Single(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSplit, entry.Key);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "e"), entry.Value);
        Assert.Equal("Deploy", Assert.Single(keymap.Commands).Name);
    }

    [Fact]
    public void InlineCommentInsideQuotedNameIsKept()
    {
        var (keymap, diagnostics) = ParseKeymap("command \"name # not a comment\" echo hi");
        Assert.Empty(diagnostics);
        Assert.Equal("name # not a comment", Assert.Single(keymap.Commands).Name);
    }

    [Fact]
    public void HashInsideSingleQuotedShellArgIsKept()
    {
        // a # in a single-quoted arg (a commit message) must survive — it is not an inline comment.
        var (keymap, diagnostics) = ParseKeymap("command \"Commit\" cmd+shift+c git commit -m 'fix #42'");
        Assert.Empty(diagnostics);
        Assert.Equal("git commit -m 'fix #42'", Assert.Single(keymap.Commands).Command);
    }

    [Fact]
    public void TrailingCommentAfterSingleQuotedArgIsStripped()
    {
        // once the single quote closes, a whitespace-preceded # is still a real inline comment.
        var (keymap, diagnostics) = ParseKeymap("command \"X\" cmd+shift+x echo 'hi' # real comment");
        Assert.Empty(diagnostics);
        Assert.Equal("echo 'hi'", Assert.Single(keymap.Commands).Command);
    }

    [Fact]
    public void DoubleQuoteInsideSingleQuotesDoesNotOpenAQuotedSpan()
    {
        // a " inside '...' is literal, so the # after the single-quoted arg still starts a comment.
        var (keymap, diagnostics) = ParseKeymap("command \"Y\" cmd+shift+y echo 'a\"b' # c");
        Assert.Empty(diagnostics);
        Assert.Equal("echo 'a\"b'", Assert.Single(keymap.Commands).Command);
    }

    [Fact]
    public void ParseUnknownVerbDiagnostic()
    {
        var (keymap, diagnostics) = ParseKeymap("bind cmd+d toggle_split");
        Assert.Empty(keymap.BuiltinOverrides);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(1, diagnostic.Line);
        Assert.Contains("unknown verb", diagnostic.Message);
    }

    [Fact]
    public void ParseUnknownActionDiagnostic()
    {
        var (keymap, diagnostics) = ParseKeymap("map cmd+d not_an_action");
        Assert.Empty(keymap.BuiltinOverrides);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(1, diagnostic.Line);
        Assert.Contains("unknown action", diagnostic.Message);
    }

    [Fact]
    public void ParseLeaderOnBuiltinDiagnostic()
    {
        var (keymap, diagnostics) = ParseKeymap("map ctrl+a>g toggle_split");
        Assert.Empty(keymap.BuiltinOverrides);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal("built-in shortcut cannot be a leader sequence", diagnostic.Message);
    }

    [Fact]
    public void ParseInvalidChordDiagnostic()
    {
        var (keymap, diagnostics) = ParseKeymap("map cmd+f1 toggle_split");
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Contains("invalid chord", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void ParseArrowChordMapLines()
    {
        // the exact lines from #278: arrows are part of the chord grammar, so these parse cleanly.
        var text = "map cmd+shift+left previous_session\nmap cmd+shift+right next_session";
        var (keymap, diagnostics) = ParseKeymap(text);
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "left"),
            keymap.EquivalentFor(BuiltinAction.PreviousSession));
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "right"),
            keymap.EquivalentFor(BuiltinAction.NextSession));
    }

    [Fact]
    public void ParseBareArrowMapIsRejected()
    {
        var (keymap, diagnostics) = ParseKeymap("map left previous_session");
        Assert.Empty(keymap.BuiltinOverrides);
        // the action keeps its shipped default.
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Option, "up"),
            keymap.EquivalentFor(BuiltinAction.PreviousSession));
        Assert.Contains("needs a modifier", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void ParseModifiedArrowMapIsAcceptedForEveryArrow()
    {
        // only the BARE form is rejected — any modifier makes an arrow bindable.
        foreach (var key in new[] { "left", "right", "up", "down" })
        {
            var (keymap, diagnostics) = ParseKeymap($"map cmd+shift+{key} new_session");
            Assert.Empty(diagnostics);
            Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, key),
                keymap.EquivalentFor(BuiltinAction.NewSession));
        }
    }

    [Fact]
    public void MapOntoAnArrowActionsUnmovedDefaultIsRejected()
    {
        // cmd+opt+up is previous_session's UNMOVED default, so claiming it for another action must be
        // diagnosed, not silently double-bound.
        var (keymap, diagnostics) = ParseKeymap("map cmd+opt+up new_session");
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Equal(new Chord(Modifiers.Command, "n"), keymap.EquivalentFor(BuiltinAction.NewSession));
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Option, "up"),
            keymap.EquivalentFor(BuiltinAction.PreviousSession));
        Assert.Contains("conflicts with built-in 'previous_session'", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void CustomCommandOnAnArrowDefaultIsDropped()
    {
        // cross-section validation sees the arrow defaults too, so a custom command can't shadow one.
        var (keymap, diagnostics) = ParseKeymap("command \"Nav\" cmd+opt+down echo hi");
        var command = Assert.Single(keymap.Commands);
        // the keybind is dropped but the palette entry survives.
        Assert.Equal("Nav", command.Name);
        Assert.Equal("", command.Shortcut);
        Assert.Contains("built-in", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void ParseBareNonArrowMapIsStillAccepted()
    {
        // the modifier requirement is arrow-ONLY — a bare non-arrow map stays legal.
        var (keymap, diagnostics) = ParseKeymap("map a new_session");
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.None, "a"), keymap.EquivalentFor(BuiltinAction.NewSession));
    }

    [Fact]
    public void ParseDuplicateBuiltinChordDiagnostic()
    {
        // two maps to the same chord for DIFFERENT actions: the later one (line 2) is skipped.
        var text = "map cmd+shift+e toggle_split\nmap cmd+shift+e new_session";
        var (keymap, diagnostics) = ParseKeymap(text);
        var entry = Assert.Single(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSplit, entry.Key);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "e"), entry.Value);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(2, diagnostic.Line);
        Assert.Contains("conflicts with built-in 'toggle_split'", diagnostic.Message);
    }

    [Fact]
    public void MapToOtherBuiltinUnmovedDefaultIsRejected()
    {
        // cmd+d is toggle_split's UNMOVED default; mapping new_session to it must be diagnosed and
        // the override dropped. The CRITICAL case: override-vs-other-default, not just override-vs-override.
        var (keymap, diagnostics) = ParseKeymap("map cmd+d new_session");
        Assert.Empty(keymap.BuiltinOverrides);
        // new_session keeps its own default (the map was skipped, not applied).
        Assert.Equal(new Chord(Modifiers.Command, "n"), keymap.EquivalentFor(BuiltinAction.NewSession));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(1, diagnostic.Line);
        Assert.Contains("conflicts with built-in 'toggle_split'", diagnostic.Message);
    }

    [Fact]
    public void MapToFreedDefaultOfMovedBuiltinSucceeds()
    {
        // moving toggle_split off cmd+d frees that chord; new_session may then take it.
        var text = "map cmd+shift+e toggle_split\nmap cmd+d new_session";
        var (keymap, diagnostics) = ParseKeymap(text);
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "e"),
            keymap.BuiltinOverrides[BuiltinAction.ToggleSplit]);
        Assert.Equal(new Chord(Modifiers.Command, "d"), keymap.BuiltinOverrides[BuiltinAction.NewSession]);
    }

    [Fact]
    public void MapToOtherBuiltinDefaultThenMoveThatBuiltinBothSucceed()
    {
        // the reverse-order case: take cmd+d FIRST, then move toggle_split off it. Resolution is
        // order-independent against the final state, so both succeed with NO diagnostic.
        var text = "map cmd+d new_session\nmap cmd+shift+e toggle_split";
        var (keymap, diagnostics) = ParseKeymap(text);
        Assert.Empty(diagnostics);
        Assert.Equal(new Chord(Modifiers.Command, "d"), keymap.BuiltinOverrides[BuiltinAction.NewSession]);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "e"),
            keymap.BuiltinOverrides[BuiltinAction.ToggleSplit]);
    }

    [Fact]
    public void MapTabSeparatedLineParses()
    {
        // a tab between the chord and the action must parse the same as a space.
        var (keymap, diagnostics) = ParseKeymap("map\tcmd+shift+e\ttoggle_split");
        Assert.Empty(diagnostics);
        var entry = Assert.Single(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSplit, entry.Key);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "e"), entry.Value);
    }

    [Fact]
    public void MapCascadingCollisionDropsBothRevertsToDefaults()
    {
        // cascade: toggle_split's override (cmd+o) loses to open_directory's UNMOVED default cmd+o →
        // dropped → reverts to its OWN default cmd+d → which then collides with new_session's
        // accepted override cmd+d. Resolution must iterate to a fixpoint and drop BOTH overrides.
        var text = "map cmd+o toggle_split\nmap cmd+d new_session";
        var (keymap, diagnostics) = ParseKeymap(text);
        // both overrides dropped → every action sits on its shipped default.
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSplit.DefaultChord(), keymap.EquivalentFor(BuiltinAction.ToggleSplit));
        Assert.Equal(BuiltinAction.NewSession.DefaultChord(), keymap.EquivalentFor(BuiltinAction.NewSession));
        Assert.Equal(BuiltinAction.OpenDirectory.DefaultChord(), keymap.EquivalentFor(BuiltinAction.OpenDirectory));
        // the final state is collision-free: no two distinct actions resolve to the same chord.
        var chords = BuiltinActions.All
            .Select(keymap.EquivalentFor).Where(c => c is not null).Select(c => c!.Value).ToList();
        Assert.Equal(chords.Count, chords.Distinct().Count());
        // two diagnostics, one per dropped override, in file order.
        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(1, diagnostics[0].Line);
        Assert.Equal(2, diagnostics[1].Line);
        Assert.All(diagnostics, d => Assert.Contains("conflicts with built-in", d.Message));
    }

    [Fact]
    public void MapBuiltinToReservedMonitorChordIsRejected()
    {
        // ctrl+1 is owned by the Ctrl-1/2 pane monitor; a built-in map to it must be diagnosed +
        // skipped, so the action keeps its default.
        var (keymap, diagnostics) = ParseKeymap("map ctrl+1 new_session");
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Equal(new Chord(Modifiers.Command, "n"), keymap.EquivalentFor(BuiltinAction.NewSession));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(1, diagnostic.Line);
        Assert.Contains("reserved", diagnostic.Message);
    }

    [Fact]
    public void MapBuiltinToReservedCtrlTabIsRejected()
    {
        // ctrl+tab is the Ctrl-Tab switcher's chord; a built-in map to it is reserved and skipped.
        var (keymap, diagnostics) = ParseKeymap("map ctrl+tab quick_terminal");
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Contains("reserved", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void ParseHandlesCRLFLineEndings()
    {
        // a CRLF file leaves a trailing \r; normalization must let the line parse normally rather
        // than reading `toggle_split\r` as an unknown action.
        var text = "map cmd+shift+e toggle_split\r\ncommand \"Deploy\" ./deploy.sh\r\n";
        var (keymap, diagnostics) = ParseKeymap(text);
        Assert.Empty(diagnostics);
        var entry = Assert.Single(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSplit, entry.Key);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "e"), entry.Value);
        Assert.Equal("Deploy", Assert.Single(keymap.Commands).Name);
    }

    [Fact]
    public void MapSameActionTwiceIsLastWins()
    {
        // re-mapping the SAME action can't collide with itself: the later chord wins, no diagnostic.
        var text = "map cmd+shift+e toggle_split\nmap cmd+shift+x toggle_split";
        var (keymap, diagnostics) = ParseKeymap(text);
        Assert.Empty(diagnostics);
        var entry = Assert.Single(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSplit, entry.Key);
        Assert.Equal(new Chord(Modifiers.Command | Modifiers.Shift, "x"), entry.Value);
    }

    [Fact]
    public void ParseMapMissingActionDiagnostic()
    {
        var (keymap, diagnostics) = ParseKeymap("map cmd+d");
        Assert.Empty(keymap.BuiltinOverrides);
        Assert.Contains("map requires", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void ParseCommandMissingQuotedNameDiagnostic()
    {
        var (keymap, diagnostics) = ParseKeymap("command Deploy ./deploy.sh");
        Assert.Empty(keymap.Commands);
        Assert.Contains("quoted name", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void ParseDiagnosticLineNumbersWhileGoodLinesParse()
    {
        var text = "map cmd+shift+e toggle_split\nbogus line here\n" +
                   "command \"Deploy\" ./deploy.sh\nmap ctrl+a>g new_session";
        var (keymap, diagnostics) = ParseKeymap(text);
        // the two valid lines still parse.
        var entry = Assert.Single(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSplit, entry.Key);
        Assert.Equal("Deploy", Assert.Single(keymap.Commands).Name);
        // the two bad lines each yield a diagnostic at the correct line number.
        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(2, diagnostics[0].Line);
        Assert.Contains("unknown verb", diagnostics[0].Message);
        Assert.Equal(4, diagnostics[1].Line);
        Assert.Equal("built-in shortcut cannot be a leader sequence", diagnostics[1].Message);
    }

    [Fact]
    public void CustomChordEqualsBuiltinDefaultIsDropped()
    {
        // cmd+d is toggle_split's shipped default; the custom command keeps its palette entry but
        // loses the keybind.
        var (keymap, diagnostics) = ParseKeymap("command \"Boom\" cmd+d echo boom");
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("Boom", command.Name);
        Assert.Equal("echo boom", command.Command);
        Assert.Equal("", command.Shortcut);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(0, diagnostic.Line);
        Assert.Contains("built-in", diagnostic.Message);
    }

    [Fact]
    public void CustomChordEqualsOverriddenBuiltinChordIsDropped()
    {
        // toggle_split is moved to cmd+shift+e; a custom command bound to the NEW built-in chord
        // collides and is dropped.
        var text = "map cmd+shift+e toggle_split\ncommand \"Boom\" cmd+shift+e echo boom";
        var (keymap, diagnostics) = ParseKeymap(text);
        var entry = Assert.Single(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSplit, entry.Key);
        Assert.Equal("", Assert.Single(keymap.Commands).Shortcut);
        Assert.Contains("built-in", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void FreedDefaultChordIsUsableByCustomAfterBuiltinMoves()
    {
        // moving toggle_split off cmd+d frees that chord; a custom command may then take it. Order
        // is intentionally custom-before-map to prove validation is a single FINAL pass.
        var text = "command \"Boom\" cmd+d echo boom\nmap cmd+shift+e toggle_split";
        var (keymap, diagnostics) = ParseKeymap(text);
        var entry = Assert.Single(keymap.BuiltinOverrides);
        Assert.Equal(BuiltinAction.ToggleSplit, entry.Key);
        Assert.Equal("cmd+d", Assert.Single(keymap.Commands).Shortcut);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CustomLeaderWhoseFirstChordEqualsBuiltinIsDropped()
    {
        // toggle_split's default is cmd+d; a custom leader STARTING with cmd+d is shadowed by the menu.
        var (keymap, diagnostics) = ParseKeymap("command \"Boom\" cmd+d>g echo boom");
        Assert.Equal("", Assert.Single(keymap.Commands).Shortcut);
        Assert.Contains("built-in", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void CustomBoundToReservedMonitorChordIsDropped()
    {
        // ctrl+1 is owned by the Ctrl-1/2 pane monitor; the custom keeps its palette entry but loses
        // the keybind, with a diagnostic.
        var (keymap, diagnostics) = ParseKeymap("command \"Boom\" ctrl+1 echo boom");
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("Boom", command.Name);
        Assert.Equal("echo boom", command.Command);
        Assert.Equal("", command.Shortcut);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(0, diagnostic.Line);
        Assert.Contains("reserved", diagnostic.Message);
    }

    [Fact]
    public void CustomLeaderStartingWithReservedMonitorChordIsDropped()
    {
        // ctrl+tab is the Ctrl-Tab switcher's chord; a custom leader STARTING with it is reserved.
        var (keymap, diagnostics) = ParseKeymap("command \"Boom\" ctrl+tab>g echo boom");
        Assert.Equal("", Assert.Single(keymap.Commands).Shortcut);
        Assert.Contains("reserved", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void CustomLeaderWhoseLaterChordIsReservedMonitorChordIsDropped()
    {
        // ctrl+a>ctrl+1 — the FIRST chord is not reserved, but ctrl+1 is owned by the monitor, which
        // consumes it wherever it lands in the leader, so the leader can never complete.
        var (keymap, diagnostics) = ParseKeymap("command \"Boom\" ctrl+a>ctrl+1 echo boom");
        var command = Assert.Single(keymap.Commands);
        Assert.Equal("Boom", command.Name);
        Assert.Equal("echo boom", command.Command);
        Assert.Equal("", command.Shortcut);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(0, diagnostic.Line);
        Assert.Contains("reserved", diagnostic.Message);
    }

    [Fact]
    public void CustomLeaderWithNoReservedChordIsAccepted()
    {
        // a normal leader with no reserved chord at any position keeps its keybind, no diagnostic.
        var (keymap, diagnostics) = ParseKeymap("command \"Lazygit\" ctrl+a>g lazygit");
        Assert.Equal("ctrl+a>g", Assert.Single(keymap.Commands).Shortcut);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CommandWithEmptyShellLineIsDiagnosed()
    {
        // a name with no shell line is a no-op; skip it + diagnose.
        var (keymap, diagnostics) = ParseKeymap("command \"Empty\"");
        Assert.Empty(keymap.Commands);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(1, diagnostic.Line);
        Assert.Contains("no shell line", diagnostic.Message);
    }

    [Fact]
    public void CommandWithChordButEmptyShellLineIsDiagnosed()
    {
        var (keymap, diagnostics) = ParseKeymap("command \"Empty\" cmd+shift+e");
        Assert.Empty(keymap.Commands);
        Assert.Contains("no shell line", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void CustomVsCustomDuplicateDropsBoth()
    {
        var text = "command \"First\" cmd+shift+e echo one\ncommand \"Second\" cmd+shift+e echo two";
        var (keymap, diagnostics) = ParseKeymap(text);
        Assert.Equal(2, keymap.Commands.Count);
        Assert.Equal("", keymap.Commands[0].Shortcut);
        Assert.Equal("", keymap.Commands[1].Shortcut);
        // both commands stay in the palette, just unkeyed.
        Assert.Equal("echo one", keymap.Commands[0].Command);
        Assert.Equal("echo two", keymap.Commands[1].Command);
        Assert.Equal(2, diagnostics.Count);
        // each diagnostic names the OTHER offending command by name.
        Assert.Contains(diagnostics, d => d.Message.Contains("'First'") && d.Message.Contains("'Second'"));
        Assert.Contains(diagnostics, d => d.Message.Contains("'Second'") && d.Message.Contains("'First'"));
    }

    [Fact]
    public void CustomVsCustomPrefixDropsBoth()
    {
        // a single chord that is a prefix of a leader sequence is the wait-or-fire ambiguity.
        var text = "command \"Lead\" ctrl+a echo lead\ncommand \"Leader\" ctrl+a>g echo leader";
        var (keymap, diagnostics) = ParseKeymap(text);
        Assert.Equal(2, keymap.Commands.Count);
        Assert.Equal("", keymap.Commands[0].Shortcut);
        Assert.Equal("", keymap.Commands[1].Shortcut);
        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, d => d.Message.Contains("'Lead'") && d.Message.Contains("'Leader'"));
        Assert.Contains(diagnostics, d => d.Message.Contains("'Leader'") && d.Message.Contains("'Lead'"));
    }

    [Fact]
    public void KeymapStoreLoadsFileAndRecoversWhenMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agterm-keymap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new KeymapStore(dir);

            Assert.Equal(Path.Combine(dir, "keymap.conf"), store.Path);
            Assert.Empty(store.Load().Keymap.Commands);
            Assert.Empty(store.Load().Diagnostics);

            File.WriteAllText(store.Path, "command \"Greet\" ctrl+shift+y echo hi\n");
            var loaded = store.Load();
            Assert.Contains(loaded.Keymap.Commands, c => c.Name == "Greet");
            Assert.Empty(loaded.Diagnostics);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KeymapStoreReportsMalformedKeymapDiagnostics()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agterm-keymap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new KeymapStore(dir);
            File.WriteAllText(store.Path, "map cmd+d not_an_action\ncommand \"Good\" ctrl+shift+y echo ok\n");

            var loaded = store.Load();

            Assert.Contains(loaded.Keymap.Commands, c => c.Name == "Good");
            Assert.Contains(loaded.Diagnostics, d => d.Line == 1 && d.Message.Contains("unknown action"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KeymapStoreReportsUnreadableTextFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agterm-keymap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new KeymapStore(dir);
            File.WriteAllBytes(store.Path, new byte[] { 0xff, 0xfe, 0xfd });

            var loaded = store.Load();

            Assert.Empty(loaded.Keymap.BuiltinOverrides);
            Assert.Empty(loaded.Keymap.Commands);
            var diagnostic = Assert.Single(loaded.Diagnostics);
            Assert.Equal(0, diagnostic.Line);
            Assert.Contains("could not read keymap.conf", diagnostic.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
