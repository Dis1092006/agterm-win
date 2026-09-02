using AgTerm.App;
using AgTerm.Core.Keymap;

namespace AgTerm.Xterm.Tests;

public class WindowCommandRouterTests
{
    // Actions implemented in the current MainWindow.HandleChord switch (phase 4.5C).
    private static readonly HashSet<BuiltinAction> ImplementedActions =
    [
        BuiltinAction.NewWorkspace,
        BuiltinAction.NewSession,
        BuiltinAction.OpenDirectory,
        BuiltinAction.RenameSession,
        BuiltinAction.DuplicateSession,
        BuiltinAction.CloseSession,
        BuiltinAction.UndoClose,
        BuiltinAction.ClearStatus,
        BuiltinAction.IncreaseFontSize,
        BuiltinAction.DecreaseFontSize,
        BuiltinAction.ResetFontSize,
        BuiltinAction.ToggleSidebar,
        BuiltinAction.ToggleSplit,
        BuiltinAction.ToggleScratch,
        BuiltinAction.ToggleTerminalZoom,
        BuiltinAction.ToggleSearch,
        BuiltinAction.QuickTerminal,
        BuiltinAction.SessionPalette,
        BuiltinAction.CommandPalette,
        BuiltinAction.CustomCommandPalette,
        BuiltinAction.ShowAttention,
        BuiltinAction.Dashboard,
        BuiltinAction.FocusLeftPane,
        BuiltinAction.FocusRightPane,
        BuiltinAction.ToggleFlaggedView,
        BuiltinAction.ToggleFlag,
        BuiltinAction.FocusWorkspace,
        BuiltinAction.PreviousSession,
        BuiltinAction.NextSession,
        BuiltinAction.PreviousAttentionSession,
        BuiltinAction.NextAttentionSession,
        BuiltinAction.FirstSession,
        BuiltinAction.LastSession,
    ];

    // Actions not yet implemented; arrive in phase 5.
    private static readonly HashSet<BuiltinAction> UnimplementedActions =
    [
        BuiltinAction.NewWindow,
        BuiltinAction.RenameWindow,
        BuiltinAction.DeleteWindow,
        BuiltinAction.RenameWorkspace,
        BuiltinAction.DeleteWorkspace,
        BuiltinAction.ReopenRecent,
        BuiltinAction.SelectTheme,
        BuiltinAction.ToggleFullscreen,
    ];

    [Fact]
    public void ActionImplementations_covers_all_builtin_actions_with_correct_implemented_state()
    {
        // --- all 41 distinct enum members ---
        var all = BuiltinActions.All;
        Assert.Equal(41, all.Count);
        Assert.Equal(41, all.Distinct().Count());

        // --- test data covers exactly those 41 members ---
        var expected = ImplementedActions.Union(UnimplementedActions).ToHashSet();
        Assert.Equal(41, expected.Count);
        Assert.Equal(expected, all.ToHashSet());

        // --- ActionImplementations has exactly the 41 keys, each once ---
        var map = WindowCommandRouter.ActionImplementations;
        Assert.Equal(41, map.Count);
        Assert.Equal(expected, map.Keys.ToHashSet());

        // --- each expected Implemented value matches ---
        foreach (var action in all)
        {
            var expectedValue = ImplementedActions.Contains(action);
            Assert.True(map.TryGetValue(action, out var actual),
                $"ActionImplementations is missing {action}");
            Assert.Equal(expectedValue, actual);
        }
    }

    // ---------------------------------------------------------------------------
    // S3.2 — WindowCommandHandler + instance router execution contract
    // ---------------------------------------------------------------------------

    private static Dictionary<BuiltinAction, WindowCommandHandler> AllNoOpHandlers(
        BuiltinAction? replace = null, WindowCommandHandler? replacement = null)
    {
        var handlers = ImplementedActions
            .ToDictionary(a => a, _ => new WindowCommandHandler(() => true, () => Task.CompletedTask));
        if (replace is { } a && replacement is { } h)
            handlers[a] = h;
        return handlers;
    }

    // --- constructor completeness ---

    [Theory]
    [MemberData(nameof(ImplementedActionsCases))]
    public void Constructor_throws_ArgumentException_when_implemented_handler_is_missing(BuiltinAction missing)
    {
        var handlers = AllNoOpHandlers();
        handlers.Remove(missing);
        Assert.Throws<ArgumentException>(() => new WindowCommandRouter(handlers));
    }

    public static IEnumerable<object[]> ImplementedActionsCases =>
        ImplementedActions.Select(a => new object[] { a });

    [Fact]
    public void Constructor_throws_ArgumentException_when_unimplemented_action_handler_is_present()
    {
        var handlers = AllNoOpHandlers();
        handlers[BuiltinAction.NewWindow] = new WindowCommandHandler(() => true, () => Task.CompletedTask);
        Assert.Throws<ArgumentException>(() => new WindowCommandRouter(handlers));
    }

    [Fact]
    public void Constructor_succeeds_with_exactly_the_currently_implemented_handlers()
    {
        var router = new WindowCommandRouter(AllNoOpHandlers());
        Assert.NotNull(router);
    }

    // --- GetState ---

    [Fact]
    public void GetState_returns_Implemented_true_and_CanExecute_true_for_enabled_handler()
    {
        var router = new WindowCommandRouter(AllNoOpHandlers());
        var state = router.GetState(BuiltinAction.NewWorkspace);
        Assert.True(state.Implemented);
        Assert.True(state.CanExecute);
    }

    [Fact]
    public void GetState_returns_Implemented_true_and_CanExecute_false_when_predicate_false()
    {
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.NewWorkspace,
            replacement: new WindowCommandHandler(() => false, () => Task.CompletedTask)));
        var state = router.GetState(BuiltinAction.NewWorkspace);
        Assert.True(state.Implemented);
        Assert.False(state.CanExecute);
    }

    [Fact]
    public void GetState_returns_Implemented_false_for_unimplemented_action()
    {
        var router = new WindowCommandRouter(AllNoOpHandlers());
        var state = router.GetState(BuiltinAction.NewWindow);
        Assert.False(state.Implemented);
        Assert.False(state.CanExecute);
    }

    // --- CanExecute ---

    [Fact]
    public void CanExecute_returns_true_when_implemented_and_predicate_true()
    {
        var router = new WindowCommandRouter(AllNoOpHandlers());
        Assert.True(router.CanExecute(BuiltinAction.NewWorkspace));
    }

    [Fact]
    public void CanExecute_returns_false_when_predicate_false()
    {
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.NewWorkspace,
            replacement: new WindowCommandHandler(() => false, () => Task.CompletedTask)));
        Assert.False(router.CanExecute(BuiltinAction.NewWorkspace));
    }

    [Theory]
    [InlineData(BuiltinAction.NewWindow)]
    [InlineData(BuiltinAction.SelectTheme)]
    public void CanExecute_returns_false_for_unimplemented_action(BuiltinAction action)
    {
        var router = new WindowCommandRouter(AllNoOpHandlers());
        Assert.False(router.CanExecute(action));
    }

    [Fact]
    public void CanExecute_returns_false_for_unknown_enum_value()
    {
        var router = new WindowCommandRouter(AllNoOpHandlers());
        Assert.False(router.CanExecute((BuiltinAction)999));
    }

    // --- ExecuteAsync ---

    [Fact]
    public async Task ExecuteAsync_returns_true_and_invokes_handler_once_when_executable()
    {
        var calls = 0;
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.NewWorkspace,
            replacement: new WindowCommandHandler(() => true, () => { calls++; return Task.CompletedTask; })));
        var result = await router.ExecuteAsync(BuiltinAction.NewWorkspace);
        Assert.True(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteAsync_returns_false_and_does_not_invoke_when_predicate_false()
    {
        var calls = 0;
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.NewWorkspace,
            replacement: new WindowCommandHandler(() => false, () => { calls++; return Task.CompletedTask; })));
        var result = await router.ExecuteAsync(BuiltinAction.NewWorkspace);
        Assert.False(result);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(BuiltinAction.NewWindow)]
    [InlineData(BuiltinAction.SelectTheme)]
    public async Task ExecuteAsync_returns_false_for_unimplemented_action(BuiltinAction action)
    {
        var router = new WindowCommandRouter(AllNoOpHandlers());
        Assert.False(await router.ExecuteAsync(action));
    }

    [Fact]
    public async Task ExecuteAsync_returns_false_for_unknown_enum_value()
    {
        var router = new WindowCommandRouter(AllNoOpHandlers());
        Assert.False(await router.ExecuteAsync((BuiltinAction)999));
    }

    [Fact]
    public async Task ExecuteAsync_propagates_handler_exception()
    {
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.NewWorkspace,
            replacement: new WindowCommandHandler(
                () => true,
                () => Task.FromException(new InvalidOperationException("handler fault")))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.ExecuteAsync(BuiltinAction.NewWorkspace));
    }

    // ---------------------------------------------------------------------------
    // S3.3 — ExecuteChordAsync: physical chord dispatch
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteChordAsync_default_ctrl_n_resolves_new_session_invokes_once_returns_true()
    {
        // cmd+n is the default chord for NewSession; Ctrl+N decodes cmd+n first (cmd priority).
        var calls = 0;
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.NewSession,
            replacement: new WindowCommandHandler(() => true, () => { calls++; return Task.CompletedTask; })));
        var hotkeys = new HotkeyRouter(Keymap.Empty);
        var physical = new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "n");

        var result = await router.ExecuteChordAsync(hotkeys, physical);

        Assert.True(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteChordAsync_unbound_physical_chord_returns_false_invokes_none()
    {
        // Ctrl+Q decodes to cmd+q / ctrl+q / ctrl+cmd+q — none are default or overridden binds.
        var totalCalls = new[] { 0 };
        var router = new WindowCommandRouter(ImplementedActions
            .ToDictionary(a => a, _ =>
                new WindowCommandHandler(() => true, () => { totalCalls[0]++; return Task.CompletedTask; })));
        var hotkeys = new HotkeyRouter(Keymap.Empty);
        var physical = new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "q");

        var result = await router.ExecuteChordAsync(hotkeys, physical);

        Assert.False(result);
        Assert.Equal(0, totalCalls[0]);
    }

    [Fact]
    public async Task ExecuteChordAsync_ctrl_d_resolves_toggle_split_returns_true_and_invokes_once()
    {
        // cmd+d is the default chord for ToggleSplit; Ctrl+D decodes cmd+d first → ToggleSplit (implemented in P3).
        var calls = 0;
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.ToggleSplit,
            replacement: new WindowCommandHandler(() => true, () => { calls++; return Task.CompletedTask; })));
        var hotkeys = new HotkeyRouter(Keymap.Empty);
        var physical = new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "d");

        var result = await router.ExecuteChordAsync(hotkeys, physical);

        Assert.True(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteChordAsync_ctrl_j_resolves_toggle_scratch_returns_true_and_invokes_once()
    {
        var calls = 0;
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.ToggleScratch,
            replacement: new WindowCommandHandler(() => true, () => { calls++; return Task.CompletedTask; })));
        var hotkeys = new HotkeyRouter(Keymap.Empty);
        var physical = new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "j");

        var result = await router.ExecuteChordAsync(hotkeys, physical);

        Assert.True(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteChordAsync_ctrl_backtick_resolves_quick_terminal_invokes_once_returns_true()
    {
        // ctrl+` is the default chord for QuickTerminal; physical Ctrl+` decodes ctrl+` as second
        // candidate (cmd+` has no default bind), so QuickTerminal wins.
        var calls = 0;
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.QuickTerminal,
            replacement: new WindowCommandHandler(() => true, () => { calls++; return Task.CompletedTask; })));
        var hotkeys = new HotkeyRouter(Keymap.Empty);
        var physical = new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "`");

        var result = await router.ExecuteChordAsync(hotkeys, physical);

        Assert.True(result);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(BuiltinAction.SessionPalette, false, "p")]
    [InlineData(BuiltinAction.CommandPalette, true, "p")]
    [InlineData(BuiltinAction.CustomCommandPalette, true, "o")]
    [InlineData(BuiltinAction.ShowAttention, true, "i")]
    public async Task ExecuteChordAsync_routes_C2_palette_chords(
        BuiltinAction action,
        bool shift,
        string key)
    {
        var calls = 0;
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: action,
            replacement: new WindowCommandHandler(() => true, () =>
            {
                calls++;
                return Task.CompletedTask;
            })));

        var result = await router.ExecuteChordAsync(
            new HotkeyRouter(Keymap.Empty),
            new PhysicalChord(Ctrl: true, Alt: false, Shift: shift, Key: key));

        Assert.True(result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecuteChordAsync_implemented_but_disabled_new_session_returns_false_invokes_none()
    {
        // Ctrl+N resolves NewSession (implemented), but CanExecute returns false → handler must not fire.
        var calls = 0;
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.NewSession,
            replacement: new WindowCommandHandler(() => false, () => { calls++; return Task.CompletedTask; })));
        var hotkeys = new HotkeyRouter(Keymap.Empty);
        var physical = new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "n");

        var result = await router.ExecuteChordAsync(hotkeys, physical);

        Assert.False(result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ExecuteChordAsync_collision_ctrl_f_resolves_toggle_search_via_cmd_priority_not_new_session()
    {
        // map ctrl+f new_session: HotkeyRouter binds ctrl+f→NewSession and cmd+f→ToggleSearch (default).
        // Physical Ctrl+F candidates: cmd+f first (cmd priority) → ToggleSearch (implemented).
        // The NewSession handler must not fire.
        var newSessionCalls = 0;
        var router = new WindowCommandRouter(AllNoOpHandlers(
            replace: BuiltinAction.NewSession,
            replacement: new WindowCommandHandler(() => true, () => { newSessionCalls++; return Task.CompletedTask; })));
        var (keymap, _) = KeymapParser.Parse("map ctrl+f new_session");
        var hotkeys = new HotkeyRouter(keymap);
        var physical = new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "f");

        var result = await router.ExecuteChordAsync(hotkeys, physical);

        Assert.True(result);
        Assert.Equal(0, newSessionCalls);
    }

    // ---------------------------------------------------------------------------
    // S3.3 — ImplementedInterceptChords: publication filter
    // ---------------------------------------------------------------------------

    [Fact]
    public void ImplementedInterceptChords_default_keymap_includes_ctrl_n_excludes_unimplemented_chords()
    {
        var hotkeys = new HotkeyRouter(Keymap.Empty);
        var chords = WindowCommandRouter.ImplementedInterceptChords(hotkeys);

        // Ctrl+N → cmd+n → NewSession (implemented): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "n"), chords);

        // Ctrl+D → cmd+d → ToggleSplit (implemented in P3): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "d"), chords);
        // Ctrl+Alt+Left → cmd+opt+left → FocusLeftPane (implemented in P3): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: true, Shift: false, Key: "left"), chords);
        // Ctrl+Alt+Right → cmd+opt+right → FocusRightPane (implemented in P3): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: true, Shift: false, Key: "right"), chords);
        // Ctrl+J → cmd+j → ToggleScratch (implemented in Q2): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "j"), chords);
        // Ctrl+F → cmd+f → ToggleSearch (implemented in Q4): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "f"), chords);
        // Ctrl+` → ctrl+` → QuickTerminal (implemented in Q5): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "`"), chords);
        // Ctrl+Shift+Enter → cmd+shift+return → ToggleTerminalZoom (implemented in D1): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: true, Key: "return"), chords);
        // Ctrl+Shift+T → cmd+shift+t → ReopenRecent (unimplemented): must be absent
        Assert.DoesNotContain(new PhysicalChord(Ctrl: true, Alt: false, Shift: true, Key: "t"), chords);
        // Ctrl+P → ctrl+p → SessionPalette (implemented in C2): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "p"), chords);
        // Ctrl+Shift+P → ctrl+shift+p → CommandPalette (implemented in C2): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: true, Key: "p"), chords);
        // Ctrl+Shift+O → ctrl+shift+o → CustomCommandPalette (implemented in C2): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: true, Key: "o"), chords);
        // Ctrl+Shift+I → ctrl+shift+i → ShowAttention (implemented in C2): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: true, Key: "i"), chords);
        // Ctrl+Shift+D → cmd+shift+d → Dashboard (implemented in D2): must be present
        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: true, Key: "d"), chords);

        // returned chords are distinct
        Assert.Equal(chords.Count, chords.Distinct().Count());
    }

    [Fact]
    public void ImplementedInterceptChords_every_returned_chord_winner_is_implemented()
    {
        var hotkeys = new HotkeyRouter(Keymap.Empty);
        var chords = WindowCommandRouter.ImplementedInterceptChords(hotkeys);

        foreach (var chord in chords)
        {
            var winner = hotkeys.Handle(chord);
            Assert.NotNull(winner);
            Assert.True(
                WindowCommandRouter.ActionImplementations.TryGetValue(winner.Value, out var isImpl) && isImpl,
                $"chord Ctrl={chord.Ctrl} Alt={chord.Alt} Shift={chord.Shift} Key={chord.Key} resolves to {winner} which is not implemented");
        }
    }

    [Fact]
    public void ImplementedInterceptChords_excludes_palette_actions_when_they_cannot_execute()
    {
        var hotkeys = new HotkeyRouter(Keymap.Empty);
        var paletteActions = new HashSet<BuiltinAction>
        {
            BuiltinAction.SessionPalette,
            BuiltinAction.CommandPalette,
            BuiltinAction.CustomCommandPalette,
            BuiltinAction.ShowAttention,
        };

        var chords = WindowCommandRouter.ImplementedInterceptChords(
            hotkeys,
            action => !paletteActions.Contains(action));

        Assert.DoesNotContain(new PhysicalChord(true, false, false, "p"), chords);
        Assert.DoesNotContain(new PhysicalChord(true, false, true, "p"), chords);
        Assert.DoesNotContain(new PhysicalChord(true, false, true, "o"), chords);
        Assert.DoesNotContain(new PhysicalChord(true, false, true, "i"), chords);
        Assert.Contains(new PhysicalChord(true, false, false, "`"), chords);
    }

    [Fact]
    public void ImplementedInterceptChords_collision_ctrl_f_included_when_toggle_search_wins_over_new_session()
    {
        // map ctrl+f new_session: ctrl+f→NewSession; cmd+f→ToggleSearch (default, implemented) wins first.
        var (keymap, _) = KeymapParser.Parse("map ctrl+f new_session");
        var hotkeys = new HotkeyRouter(keymap);

        var chords = WindowCommandRouter.ImplementedInterceptChords(hotkeys);

        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "f"), chords);
    }

    [Fact]
    public void ImplementedInterceptChords_ctrl_n_included_when_new_session_wins_over_remapped_toggle_split()
    {
        // map ctrl+n toggle_split: ctrl+n→ToggleSplit; cmd+n→NewSession (default, implemented) wins first.
        var (keymap, _) = KeymapParser.Parse("map ctrl+n toggle_split");
        var hotkeys = new HotkeyRouter(keymap);

        var chords = WindowCommandRouter.ImplementedInterceptChords(hotkeys);

        Assert.Contains(new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "n"), chords);
    }

    [Fact]
    public void ImplementedInterceptChords_throws_ArgumentNullException_for_null_hotkeys()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WindowCommandRouter.ImplementedInterceptChords(null!));
    }

    // ---------------------------------------------------------------------------
    // S3.5 — EffectiveWindowsChordDiagnostics: collision surface
    // ---------------------------------------------------------------------------

    [Fact]
    public void EffectiveWindowsChordDiagnostics_null_keymap_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WindowCommandRouter.EffectiveWindowsChordDiagnostics(null!));
    }

    [Fact]
    public void EffectiveWindowsChordDiagnostics_empty_keymap_returns_exactly_three_line0_diagnostics_in_order()
    {
        var diagnostics = WindowCommandRouter.EffectiveWindowsChordDiagnostics(Keymap.Empty);

        Assert.Equal(3, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(0, d.Line));

        Assert.Equal(
            "effective Windows chord 'ctrl+f' resolves to built-in 'toggle_search'; built-in 'toggle_fullscreen' is shadowed",
            diagnostics[0].Message);
        Assert.Equal(
            "effective Windows chord 'ctrl+alt+up' resolves to built-in 'previous_session'; built-in 'previous_attention_session' is shadowed",
            diagnostics[1].Message);
        Assert.Equal(
            "effective Windows chord 'ctrl+alt+down' resolves to built-in 'next_session'; built-in 'next_attention_session' is shadowed",
            diagnostics[2].Message);
    }

    [Fact]
    public void EffectiveWindowsChordDiagnostics_remap_ctrl_shift_down_next_attention_removes_ctrl_alt_down_collision()
    {
        // map ctrl+shift+down next_attention_session: next_attention_session moves off ctrl+opt+down
        // → physical Ctrl+Alt+Down has only next_session left → no collision for that chord.
        var (keymap, parserDiagnostics) = KeymapParser.Parse("map ctrl+shift+down next_attention_session");

        Assert.Empty(parserDiagnostics);

        var diagnostics = WindowCommandRouter.EffectiveWindowsChordDiagnostics(keymap);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(0, d.Line));

        Assert.Equal(
            "effective Windows chord 'ctrl+f' resolves to built-in 'toggle_search'; built-in 'toggle_fullscreen' is shadowed",
            diagnostics[0].Message);
        Assert.Equal(
            "effective Windows chord 'ctrl+alt+up' resolves to built-in 'previous_session'; built-in 'previous_attention_session' is shadowed",
            diagnostics[1].Message);

        Assert.DoesNotContain(diagnostics, d => d.Message.Contains("ctrl+alt+down"));
    }

    [Fact]
    public void EffectiveWindowsChordDiagnostics_remap_ctrl_f_new_session_adds_new_session_as_additional_shadow_ordered_by_BuiltinActions_All()
    {
        // map ctrl+f new_session: logical ctrl+f ≠ cmd+f, so no parser collision.
        // Physical Ctrl+F now has three binds: ctrl+f→new_session, cmd+f→toggle_search, cmd+ctrl+f→toggle_fullscreen.
        // Candidate priority: cmd+f wins → toggle_search; new_session and toggle_fullscreen are both shadowed.
        // Diagnostics order: shadowed actions sorted by their position in BuiltinActions.All.
        // NewSession (6) < ToggleFullscreen (23) < PreviousAttentionSession (31) < NextAttentionSession (32).
        var (keymap, parserDiagnostics) = KeymapParser.Parse("map ctrl+f new_session");

        Assert.Empty(parserDiagnostics);

        var diagnostics = WindowCommandRouter.EffectiveWindowsChordDiagnostics(keymap);

        Assert.Equal(4, diagnostics.Count);
        Assert.All(diagnostics, d => Assert.Equal(0, d.Line));

        Assert.Equal(
            "effective Windows chord 'ctrl+f' resolves to built-in 'toggle_search'; built-in 'new_session' is shadowed",
            diagnostics[0].Message);
        Assert.Equal(
            "effective Windows chord 'ctrl+f' resolves to built-in 'toggle_search'; built-in 'toggle_fullscreen' is shadowed",
            diagnostics[1].Message);
        Assert.Equal(
            "effective Windows chord 'ctrl+alt+up' resolves to built-in 'previous_session'; built-in 'previous_attention_session' is shadowed",
            diagnostics[2].Message);
        Assert.Equal(
            "effective Windows chord 'ctrl+alt+down' resolves to built-in 'next_session'; built-in 'next_attention_session' is shadowed",
            diagnostics[3].Message);
    }
}
