using AgTerm.Core.Keymap;

namespace AgTerm.Core.Tests.Keymap;

/// <summary>
/// Windows moves every app shortcut off the bare Ctrl key. The shipped (macOS) defaults put 13 of
/// them there, because this port folds keymap `cmd` onto the physical Ctrl key and macOS has no
/// Command key to fold onto anything else. Ctrl in a Windows terminal belongs to the shell: Ctrl+D
/// ends input, Ctrl+Z suspends, Ctrl+W deletes a word, Ctrl+P walks history. An app that eats those
/// does not inconvenience the user, it breaks the program running in the pane.
///
/// The parity surface still owns <see cref="BuiltinActionExtensions.DefaultChord"/>; this is a
/// platform layer on top of it, the same place the cmd-to-Ctrl fold already lives.
/// </summary>
public class WindowsDefaultChordTests
{
    private static PhysicalChord Physical(BuiltinAction action) =>
        WindowsChordDecoder.Effective(AgTerm.Core.Keymap.Keymap.Empty.EquivalentFor(action)!.Value);

    private static IEnumerable<BuiltinAction> Bound =>
        BuiltinActions.All.Where(a => AgTerm.Core.Keymap.Keymap.Empty.EquivalentFor(a) is not null);

    [Fact]
    public void No_builtin_action_holds_a_bare_ctrl_chord()
    {
        var stolen = Bound
            .Select(a => (Action: a, Physical: Physical(a)))
            .Where(x => x.Physical.Ctrl && !x.Physical.Alt && !x.Physical.Shift)
            .Select(x => $"{x.Action.WireName()} -> Ctrl+{x.Physical.Key}")
            .ToList();

        Assert.Empty(stolen);
    }

    [Fact]
    public void Shortcuts_that_already_carry_shift_or_alt_keep_the_upstream_chord()
    {
        // A shell never reads Ctrl+Shift+... or Ctrl+Alt+..., so those cost nothing and stay put:
        // every keystroke that still matches macOS is one less thing to relearn.
        foreach (var action in Bound)
        {
            var upstream = WindowsChordDecoder.Effective(action.DefaultChord()!.Value);
            if (!upstream.Shift && !upstream.Alt) continue;
            Assert.Equal(upstream, Physical(action));
        }
    }

    [Fact]
    public void Every_moved_action_lands_on_alt_with_its_original_key()
    {
        // Moving means swapping the modifier, never the letter: Ctrl+D stays a D chord. A user who
        // knows one of these can guess the rest. toggle_fullscreen is the one exception and carries
        // its reason in WindowsDefaultChords.
        foreach (var action in Bound)
        {
            if (action is BuiltinAction.ToggleFullscreen) continue;
            var upstream = WindowsChordDecoder.Effective(action.DefaultChord()!.Value);
            if (upstream.Shift || upstream.Alt) continue;

            var actual = Physical(action);
            Assert.True(actual.Alt && !actual.Ctrl,
                $"{action.WireName()}: expected a bare Alt chord, got {actual}");
            Assert.Equal(upstream.Key, actual.Key);
        }
    }

    [Fact]
    public void Moving_the_shortcuts_adds_no_new_collision()
    {
        // Two upstream pairs already share a physical chord, because cmd and ctrl fold onto the
        // same key: ⌘⌥↑ / ⌃⌥↑ and ⌘⌥↓ / ⌃⌥↓. Both carry Alt, so a shell never sees them and this
        // change leaves them alone. PLATFORM-NOTES records them. Nothing else may collide.
        var known = new[]
        {
            new PhysicalChord(Ctrl: true, Alt: true, Shift: false, Key: "up"),
            new PhysicalChord(Ctrl: true, Alt: true, Shift: false, Key: "down"),
        };

        var collisions = Bound
            .GroupBy(Physical)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} <- {string.Join(", ", g.Select(a => a.WireName()))}")
            .ToList();

        Assert.Equal(known.Length, collisions.Count);
        Assert.All(collisions, c => Assert.Contains(known, k => c.StartsWith(k.ToString(), StringComparison.Ordinal)));
    }

    [Fact]
    public void A_user_binding_still_wins_over_the_windows_default()
    {
        var keymap = new AgTerm.Core.Keymap.Keymap(
            new Dictionary<BuiltinAction, Chord> { [BuiltinAction.ToggleSplit] = new(Modifiers.Command, "e") },
            new List<CustomCommand>());

        Assert.Equal(new Chord(Modifiers.Command, "e"), keymap.EquivalentFor(BuiltinAction.ToggleSplit));
    }

    [Fact]
    public void Toggle_fullscreen_becomes_reachable()
    {
        // Upstream ⌘F (toggle_search) and ⌃⌘F (toggle_fullscreen) both fold onto Ctrl+F and the
        // search interpretation wins, so fullscreen has never been reachable on Windows.
        Assert.NotEqual(Physical(BuiltinAction.ToggleFullscreen), Physical(BuiltinAction.ToggleSearch));
    }
}
