namespace AgTerm.Core.Keymap;

/// <summary>
/// The effective Windows chord of every built-in action, and which actions lose it to another.
/// Lives in Core so the app's router and the keymap.list payload report the same text.
/// </summary>
public static class WindowsChordShadowing
{
    public static IReadOnlyList<KeymapDiagnostic> Diagnostics(Keymap keymap)
    {
        ArgumentNullException.ThrowIfNull(keymap);

        var grouped = new Dictionary<PhysicalChord, List<BuiltinAction>>();
        var physicalOrder = new List<PhysicalChord>();

        foreach (var action in BuiltinActions.All)
        {
            var active = keymap.EquivalentFor(action);
            if (active is null)
            {
                continue;
            }

            var physical = WindowsChordDecoder.Effective(active.Value);
            if (!grouped.TryGetValue(physical, out var actions))
            {
                actions = new List<BuiltinAction>();
                grouped[physical] = actions;
                physicalOrder.Add(physical);
            }

            actions.Add(action);
        }

        var hotkeys = new HotkeyRouter(keymap);
        var diagnostics = new List<KeymapDiagnostic>();

        foreach (var physical in physicalOrder)
        {
            var actions = grouped[physical];
            if (actions.Count <= 1)
            {
                continue;
            }

            var winner = hotkeys.Handle(physical);
            if (winner is null)
            {
                continue;
            }

            var winnerAction = winner.Value;
            var physicalText = Spelling(physical);
            var winnerWire = winnerAction.WireName();

            foreach (var action in actions)
            {
                if (action == winnerAction)
                {
                    continue;
                }

                diagnostics.Add(new KeymapDiagnostic(
                    0,
                    $"effective Windows chord '{physicalText}' resolves to built-in '{winnerWire}'; built-in '{action.WireName()}' is shadowed"));
            }
        }

        return diagnostics;
    }

    public static string Spelling(PhysicalChord chord)
    {
        var parts = new List<string>(4);
        if (chord.Ctrl)
        {
            parts.Add("ctrl");
        }

        if (chord.Alt)
        {
            parts.Add("alt");
        }

        if (chord.Shift)
        {
            parts.Add("shift");
        }

        parts.Add(chord.Key.ToLowerInvariant());
        return string.Join("+", parts);
    }
}
