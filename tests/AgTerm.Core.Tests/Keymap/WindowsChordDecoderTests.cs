using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Keymap;

/// <summary>
/// The Windows modifier mapping (PLATFORM-NOTES phase 4): keymap `cmd` and `ctrl` BOTH land on the
/// physical Ctrl key (the spec's "cmd → Win/Ctrl по вкусу платформы" UI decision). A physical
/// press therefore decodes into candidate chords in priority order — the cmd interpretation first
/// (most defaults are cmd), then ctrl, then the ctrl+cmd combination — and the first candidate the
/// KeybindMatcher knows wins. Effective() renders the reverse direction for hints/tables/the
/// in-terminal interception list.
/// </summary>
public class WindowsChordDecoderTests
{
    private static Chord C(Modifiers mods, string key) => new(mods, key);

    [Fact]
    public void Ctrl_alone_yields_cmd_then_ctrl()
    {
        var candidates = WindowsChordDecoder.Candidates(
            new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "n"));
        Assert.Equal(new[]
        {
            C(Modifiers.Command, "n"),
            C(Modifiers.Control, "n"),
            C(Modifiers.Control | Modifiers.Command, "n"),
        }, candidates);
    }

    [Fact]
    public void Ctrl_alt_yields_cmd_opt_then_ctrl_opt_then_ctrl_cmd_opt()
    {
        var candidates = WindowsChordDecoder.Candidates(
            new PhysicalChord(Ctrl: true, Alt: true, Shift: false, Key: "up"));
        Assert.Equal(new[]
        {
            C(Modifiers.Command | Modifiers.Option, "up"),
            C(Modifiers.Control | Modifiers.Option, "up"),
            C(Modifiers.Control | Modifiers.Command | Modifiers.Option, "up"),
        }, candidates);
    }

    [Fact]
    public void Shift_rides_along_on_every_candidate()
    {
        var candidates = WindowsChordDecoder.Candidates(
            new PhysicalChord(Ctrl: true, Alt: false, Shift: true, Key: "f"));
        Assert.All(candidates, chord => Assert.True(chord.Mods.HasFlag(Modifiers.Shift)));
        Assert.Equal(C(Modifiers.Command | Modifiers.Shift, "f"), candidates[0]);
    }

    [Fact]
    public void A_bare_or_shift_only_key_is_not_a_hotkey()
    {
        Assert.Empty(WindowsChordDecoder.Candidates(
            new PhysicalChord(Ctrl: false, Alt: false, Shift: false, Key: "a")));
        Assert.Empty(WindowsChordDecoder.Candidates(
            new PhysicalChord(Ctrl: false, Alt: false, Shift: true, Key: "a")));
    }

    [Fact]
    public void Alt_only_maps_to_the_opt_interpretation()
    {
        var candidates = WindowsChordDecoder.Candidates(
            new PhysicalChord(Ctrl: false, Alt: true, Shift: false, Key: "left"));
        Assert.Equal(new[] { C(Modifiers.Option, "left") }, candidates);
    }

    [Fact]
    public void Effective_folds_cmd_and_ctrl_onto_the_physical_ctrl_key()
    {
        var fromCmd = WindowsChordDecoder.Effective(C(Modifiers.Command, "f"));
        var fromCtrl = WindowsChordDecoder.Effective(C(Modifiers.Control, "f"));
        Assert.Equal(fromCmd, fromCtrl); // the documented ⌘F / ⌃F collision surface
        Assert.Equal(new PhysicalChord(Ctrl: true, Alt: false, Shift: false, Key: "f"), fromCmd);

        Assert.Equal(new PhysicalChord(Ctrl: true, Alt: true, Shift: false, Key: "n"),
            WindowsChordDecoder.Effective(C(Modifiers.Command | Modifiers.Option, "n")));
        Assert.Equal(new PhysicalChord(Ctrl: true, Alt: false, Shift: true, Key: "return"),
            WindowsChordDecoder.Effective(C(Modifiers.Command | Modifiers.Shift, "return")));
    }

    [Fact]
    public void Every_builtin_default_chord_round_trips_through_the_mapping()
    {
        foreach (var action in Enum.GetValues<BuiltinAction>())
        {
            if (action.DefaultChord() is not { } chord) continue;
            var physical = WindowsChordDecoder.Effective(chord);
            Assert.True(physical.Ctrl || physical.Alt,
                $"{action}: default {chord.DisplayString} must land on a modified Windows chord");
            var candidates = WindowsChordDecoder.Candidates(physical);
            Assert.Contains(chord, candidates);
        }
    }
}
