using AgTerm.Core.Keymap;
using KeymapTable = AgTerm.Core.Keymap.Keymap;

namespace AgTerm.Core.Tests.Keymap;

/// <summary>
/// HotkeyRouter — physical Windows presses → BuiltinAction fires, through the candidate decoding
/// (cmd interpretation first) and the leader state machine. Also produces the interception list
/// the terminal page needs (which physical chords must NOT reach the shell).
/// </summary>
public class HotkeyRouterTests
{
    private static PhysicalChord Press(string key, bool ctrl = false, bool alt = false, bool shift = false) =>
        new(ctrl, alt, shift, key);

    [Fact]
    public void Default_cmd_chords_fire_from_physical_ctrl()
    {
        // only the cmd chords that keep a Ctrl default arrive this way: the ones that would sit on
        // BARE Ctrl ship on Alt instead (WindowsDefaultChords).
        var router = new HotkeyRouter(KeymapTable.Empty);
        Assert.Equal(BuiltinAction.ToggleFlag, router.Handle(Press("f", ctrl: true, shift: true)));
        Assert.Equal(BuiltinAction.PreviousSession, router.Handle(Press("up", ctrl: true, alt: true)));
        Assert.Equal(BuiltinAction.NewWindow, router.Handle(Press("n", ctrl: true, alt: true)));
    }

    [Fact]
    public void Shortcuts_moved_off_bare_ctrl_fire_from_alt()
    {
        var router = new HotkeyRouter(KeymapTable.Empty);
        Assert.Equal(BuiltinAction.NewSession, router.Handle(Press("n", alt: true)));
        Assert.Equal(BuiltinAction.CloseSession, router.Handle(Press("w", alt: true)));
        Assert.Equal(BuiltinAction.ToggleSplit, router.Handle(Press("d", alt: true)));
        Assert.Equal(BuiltinAction.SessionPalette, router.Handle(Press("p", alt: true)));
    }

    [Fact]
    public void Bare_ctrl_belongs_to_the_shell()
    {
        // the point of the Windows defaults: Ctrl+D ends input, Ctrl+W deletes a word, Ctrl+P walks
        // history. The router must claim none of them.
        var router = new HotkeyRouter(KeymapTable.Empty);
        foreach (var key in new[] { "n", "o", "w", "z", "d", "j", "f", "s", "p", "0", "-" })
            Assert.Null(router.Handle(Press(key, ctrl: true)));
    }

    [Fact]
    public void Ctrl_only_defaults_fire_through_the_ctrl_fallback()
    {
        // the second candidate interpretation. session_palette and quick_terminal used to prove it
        // and now ship on Alt, so the keymap-ctrl actions that keep their chord stand in.
        var router = new HotkeyRouter(KeymapTable.Empty);
        Assert.Equal(BuiltinAction.CommandPalette, router.Handle(Press("p", ctrl: true, shift: true)));
        Assert.Equal(BuiltinAction.ShowAttention, router.Handle(Press("i", ctrl: true, shift: true)));
    }

    [Fact]
    public void Ctrl_cmd_defaults_fire_through_the_fold()
    {
        // the third candidate interpretation, ctrl+cmd. toggle_sidebar proved it on Ctrl+S and now
        // ships on Alt+S, so no shipped default exercises the fold any more; an override does.
        var (keymap, diagnostics) = KeymapParser.Parse("map ctrl+cmd+k toggle_sidebar");
        Assert.Empty(diagnostics);
        Assert.Equal(BuiltinAction.ToggleSidebar,
            new HotkeyRouter(keymap).Handle(Press("k", ctrl: true)));
    }

    [Fact]
    public void The_documented_collision_resolves_to_the_cmd_interpretation()
    {
        var router = new HotkeyRouter(KeymapTable.Empty);
        // ⌘F (toggle_search) and ⌃⌘F (toggle_fullscreen) both landed on Ctrl+F and cmd won, which
        // left fullscreen unreachable; the Windows defaults separate them.
        Assert.Equal(BuiltinAction.ToggleSearch, router.Handle(Press("f", alt: true)));
        Assert.Equal(BuiltinAction.ToggleFullscreen, router.Handle(Press("return", alt: true)));
        // ⌘⌥↑↓ (prev/next session) still shadow ⌃⌥↑↓ (attention navigation): both carry Alt, so a
        // shell never sees them and the Windows defaults leave the pair alone — remap in
        // keymap.conf to reach it (the PLATFORM-NOTES collision table)
        Assert.Equal(BuiltinAction.NextSession, router.Handle(Press("down", ctrl: true, alt: true)));

        // ...and a remap makes the shadowed action reachable again
        var (remapped, diagnostics) = KeymapParser.Parse("map ctrl+shift+down next_attention_session");
        Assert.Empty(diagnostics);
        Assert.Equal(BuiltinAction.NextAttentionSession,
            new HotkeyRouter(remapped).Handle(Press("down", ctrl: true, shift: true)));
    }

    [Fact]
    public void Unbound_presses_fall_through_as_null()
    {
        var router = new HotkeyRouter(KeymapTable.Empty);
        Assert.Null(router.Handle(Press("q", ctrl: true, alt: true, shift: true)));
        Assert.Null(router.Handle(Press("a")));
    }

    [Fact]
    public void An_override_replaces_the_default()
    {
        var (keymap, diagnostics) = KeymapParser.Parse("map ctrl+shift+z new_session");
        Assert.Empty(diagnostics);
        var router = new HotkeyRouter(keymap);
        Assert.Equal(BuiltinAction.NewSession, router.Handle(Press("z", ctrl: true, shift: true)));
        Assert.Null(router.Handle(Press("n", alt: true))); // the default chord no longer fires it
    }

    [Fact]
    public void Intercept_chords_cover_every_bound_first_chord_exactly_once()
    {
        var router = new HotkeyRouter(KeymapTable.Empty);
        var chords = router.InterceptChords;
        Assert.Contains(new PhysicalChord(false, true, false, "n"), chords);   // Alt+N (upstream ⌘N)
        Assert.Contains(new PhysicalChord(false, true, false, "p"), chords);   // Alt+P (upstream ⌃P)
        Assert.Contains(new PhysicalChord(true, true, false, "up"), chords);   // ⌘⌥↑, unmoved
        Assert.Equal(chords.Count, chords.Distinct().Count());
        Assert.DoesNotContain(chords, c => c is { Ctrl: false, Alt: false });  // nothing unmodified
        // the shell keeps every bare Ctrl chord: none of them is intercepted on the way
        Assert.DoesNotContain(chords, c => c is { Ctrl: true, Alt: false, Shift: false });
    }

    [Fact]
    public void CreateManifest_maps_custom_single_and_leader_with_generation_and_timeout()
    {
        var single = new CustomCommand("single", "echo single", "ctrl+k", Guid.NewGuid());
        var leader = new CustomCommand("leader", "echo leader", "ctrl+a>b", Guid.NewGuid());
        var paletteOnly = new CustomCommand("palette", "echo palette", "", Guid.NewGuid());
        var keymap = new KeymapTable(
            new Dictionary<BuiltinAction, Chord>(),
            [single, leader, paletteOnly]);

        var manifest = new HotkeyRouter(keymap).CreateManifest(
            generation: 9,
            canExecute: action => action == BuiltinAction.NewSession,
            leaderTimeoutMs: 275);

        Assert.Equal(9, manifest.Generation);
        Assert.Equal(275, manifest.LeaderTimeoutMs);
        Assert.Equal([Press("n", alt: true)], manifest.BuiltinSingles);
        Assert.Equal(2, manifest.CustomBindings.Count);
        Assert.Equal([Press("k", ctrl: true)],
            manifest.CustomBindings.Single(binding => binding.CommandId == single.Id).Chords);
        Assert.Equal([Press("a", ctrl: true), Press("b")],
            manifest.CustomBindings.Single(binding => binding.CommandId == leader.Id).Chords);
        Assert.DoesNotContain(manifest.CustomBindings, binding => binding.CommandId == paletteOnly.Id);
    }

    [Fact]
    public void CreateManifest_prunes_custom_first_chord_shadowed_by_active_builtin_after_Windows_fold()
    {
        // new_session ships on opt+n, so opt+n is the chord a custom command gets shadowed on.
        var command = new CustomCommand("shadowed", "echo shadowed", "opt+n", Guid.NewGuid());
        var keymap = new KeymapTable(new Dictionary<BuiltinAction, Chord>(), [command]);

        var manifest = new HotkeyRouter(keymap).CreateManifest(
            1,
            action => action == BuiltinAction.NewSession);

        Assert.Empty(manifest.CustomBindings);
        Assert.Equal([Press("n", alt: true)], manifest.BuiltinSingles);
    }

    [Fact]
    public void CreateManifest_prunes_later_custom_binding_with_effective_prefix_conflict()
    {
        var first = new CustomCommand("first", "echo first", "ctrl+a>b", Guid.NewGuid());
        var later = new CustomCommand("later", "echo later", "cmd+a", Guid.NewGuid());
        var keymap = new KeymapTable(new Dictionary<BuiltinAction, Chord>(), [first, later]);

        var manifest = new HotkeyRouter(keymap).CreateManifest(1, _ => false);

        var binding = Assert.Single(manifest.CustomBindings);
        Assert.Equal(first.Id, binding.CommandId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Plus_fires_from_the_equals_key_with_or_without_shift(bool shift)
    {
        var router = new HotkeyRouter(KeymapTable.Empty);

        Assert.Equal(BuiltinAction.IncreaseFontSize, router.Handle(Press("=", alt: true, shift: shift)));
    }

    [Fact]
    public void Plus_fires_from_the_numpad_plus()
    {
        var router = new HotkeyRouter(KeymapTable.Empty);

        Assert.Equal(BuiltinAction.IncreaseFontSize, router.Handle(Press("+", alt: true)));
        Assert.Equal(BuiltinAction.DecreaseFontSize, router.Handle(Press("-", alt: true)));
    }

    [Fact]
    public void A_map_on_the_equals_key_wins_over_plus()
    {
        var (keymap, _) = KeymapParser.Parse("map alt+= toggle_split");
        var router = new HotkeyRouter(keymap);

        Assert.Equal(BuiltinAction.ToggleSplit, router.Handle(Press("=", alt: true)));
        Assert.Equal(BuiltinAction.IncreaseFontSize, router.Handle(Press("=", alt: true, shift: true)));
    }

    [Fact]
    public void The_page_intercepts_every_press_plus_stands_for()
    {
        var manifest = new HotkeyRouter(KeymapTable.Empty).CreateManifest(1, _ => true);

        Assert.Contains(Press("=", alt: true), manifest.BuiltinSingles);
        Assert.Contains(Press("=", alt: true, shift: true), manifest.BuiltinSingles);
        Assert.Contains(Press("+", alt: true), manifest.BuiltinSingles);
    }

    [Fact]
    public void A_custom_command_on_the_equals_key_keeps_it_from_plus()
    {
        var (keymap, _) = KeymapParser.Parse("command \"Probe\" alt+= echo probe");
        var command = Assert.Single(keymap.Commands);

        var manifest = new HotkeyRouter(keymap).CreateManifest(1, _ => true);

        var binding = Assert.Single(manifest.CustomBindings);
        Assert.Equal(command.Id, binding.CommandId);
        Assert.Equal([Press("=", alt: true)], binding.Chords);
        Assert.DoesNotContain(Press("=", alt: true), manifest.BuiltinSingles);
        Assert.Contains(Press("=", alt: true, shift: true), manifest.BuiltinSingles);
    }
}
