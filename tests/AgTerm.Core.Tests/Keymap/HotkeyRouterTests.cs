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
        var router = new HotkeyRouter(KeymapTable.Empty);
        Assert.Equal(BuiltinAction.NewSession, router.Handle(Press("n", ctrl: true)));
        Assert.Equal(BuiltinAction.CloseSession, router.Handle(Press("w", ctrl: true)));
        Assert.Equal(BuiltinAction.ToggleFlag, router.Handle(Press("f", ctrl: true, shift: true)));
        Assert.Equal(BuiltinAction.PreviousSession, router.Handle(Press("up", ctrl: true, alt: true)));
    }

    [Fact]
    public void Ctrl_only_defaults_fire_through_the_ctrl_fallback()
    {
        var router = new HotkeyRouter(KeymapTable.Empty);
        Assert.Equal(BuiltinAction.SessionPalette, router.Handle(Press("p", ctrl: true)));
        Assert.Equal(BuiltinAction.QuickTerminal, router.Handle(Press("`", ctrl: true)));
    }

    [Fact]
    public void Ctrl_cmd_defaults_fire_through_the_fold()
    {
        var router = new HotkeyRouter(KeymapTable.Empty);
        Assert.Equal(BuiltinAction.ToggleSidebar, router.Handle(Press("s", ctrl: true)));
    }

    [Fact]
    public void The_documented_collision_resolves_to_the_cmd_interpretation()
    {
        var router = new HotkeyRouter(KeymapTable.Empty);
        // ⌘F (toggle_search) and ⌃⌘F (toggle_fullscreen) both land on Ctrl+F — cmd wins
        Assert.Equal(BuiltinAction.ToggleSearch, router.Handle(Press("f", ctrl: true)));
        // ⌘⌥↑↓ (prev/next session) shadow ⌃⌥↑↓ (attention navigation) the same way — remap in
        // keymap.conf to reach the attention pair (the PLATFORM-NOTES collision table)
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
        Assert.Null(router.Handle(Press("n", ctrl: true))); // the default chord no longer fires it
    }

    [Fact]
    public void Intercept_chords_cover_every_bound_first_chord_exactly_once()
    {
        var router = new HotkeyRouter(KeymapTable.Empty);
        var chords = router.InterceptChords;
        Assert.Contains(new PhysicalChord(true, false, false, "n"), chords);   // ⌘N
        Assert.Contains(new PhysicalChord(true, false, false, "p"), chords);   // ⌃P
        Assert.Contains(new PhysicalChord(true, true, false, "up"), chords);   // ⌘⌥↑
        Assert.Equal(chords.Count, chords.Distinct().Count());                 // ⌘F/⌃⌘F fold once
        Assert.DoesNotContain(chords, c => c is { Ctrl: false, Alt: false });  // nothing unmodified
    }
}
