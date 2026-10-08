using System.Text;

namespace AgTerm.Core.Keymap;

/// <summary>
/// Parses the text of a keymap.conf into a Keymap plus diagnostics. Never throws: a bad line
/// becomes a diagnostic and is skipped, so a single malformed line never discards the rest of the
/// file.
///
/// Grammar (kitty-flavored): line-based; blank lines and #-comment lines are ignored; a trailing
/// inline comment is stripped when a # is preceded by whitespace AND lies outside any quoted span
/// (single or double). Verbs:
/// - map &lt;chord&gt; &lt;action&gt; — rebind a built-in (single chord only). Conflict resolution is
///   order-INDEPENDENT against the FINAL resolved built-in set.
/// - command "&lt;name&gt;" [chord] &lt;shell...&gt; — a custom command; the token after the closing quote
///   is the chord iff it parses AND carries a modifier; the shell line keeps {AGT_X} tokens verbatim.
/// After all lines, a single cross-section validation pass drops custom keybinds that collide with
/// an active built-in chord, a reserved monitor chord, or each other (the command stays palette-only).
/// </summary>
public static class KeymapParser
{
    public static (Keymap Keymap, List<KeymapDiagnostic> Diagnostics) Parse(string text)
    {
        // overrides are collected in file order (not folded into a dict yet), so the final
        // cross-builtin duplicate pass can resolve them against the FULLY-resolved active chord set.
        var parsedOverrides = new List<ParsedOverride>();
        var commands = new List<CustomCommand>();
        var diagnostics = new List<KeymapDiagnostic>();

        // normalize line endings so CRLF and lone-CR files split into lines correctly: without this
        // a CRLF line leaves a trailing \r ("toggle_split\r" reads as an unknown action) and a
        // lone-CR file collapses into one line.
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var rawLines = normalized.Split('\n');
        for (var index = 0; index < rawLines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = StripComment(rawLines[index]).Trim();
            if (line.Length == 0) continue;

            var verbEnd = 0;
            while (verbEnd < line.Length && !char.IsWhiteSpace(line[verbEnd])) verbEnd++;
            var verb = line[..verbEnd];
            var rest = line[verbEnd..].Trim();

            switch (verb)
            {
                case "map":
                    ParseMapLine(rest, lineNumber, parsedOverrides, diagnostics);
                    break;
                case "command":
                    ParseCommandLine(rest, lineNumber, commands, diagnostics);
                    break;
                default:
                    diagnostics.Add(new KeymapDiagnostic(lineNumber, $"unknown verb '{verb}'"));
                    break;
            }
        }

        var builtinOverrides = ResolveBuiltinOverrides(parsedOverrides, diagnostics);

        // cross-section validation is a SINGLE final pass over the fully-resolved built-in set, not
        // incremental: a custom line parsed before a later map must still be validated against the
        // override that map installs.
        var keymap = new Keymap(builtinOverrides, commands);
        var validated = ValidateCommands(commands, keymap, diagnostics);

        return (new Keymap(builtinOverrides, validated), diagnostics);
    }

    /// <summary>A single valid map line, retained in file order until the final duplicate pass.</summary>
    private readonly record struct ParsedOverride(BuiltinAction Action, Chord Chord, int Line);

    /// <summary>
    /// Fold the file-order overrides into the final map, rejecting only TRUE final-state collisions
    /// (two DISTINCT actions resolving to the same chord). Iterates to a fixpoint: each pass drops
    /// one collision's loser (an override colliding with an unmoved default loses; of two colliding
    /// overrides the later-in-file one loses), which reverts the loser to its own default and may
    /// collide afresh. Re-mapping the SAME action is last-wins.
    /// </summary>
    private static Dictionary<BuiltinAction, Chord> ResolveBuiltinOverrides(
        List<ParsedOverride> overrides, List<KeymapDiagnostic> diagnostics)
    {
        var candidates = new Dictionary<BuiltinAction, Chord>();
        var overrideLine = new Dictionary<BuiltinAction, int>();
        foreach (var o in overrides)
        {
            candidates[o.Action] = o.Chord;
            overrideLine[o.Action] = o.Line;
        }

        var pending = new List<(BuiltinAction Loser, BuiltinAction Keeper, int Line)>();
        while (FirstBuiltinCollision(candidates, overrideLine) is { } drop)
        {
            candidates.Remove(drop.Loser);
            pending.Add(drop);
        }

        foreach (var drop in pending.OrderBy(d => d.Line))
        {
            diagnostics.Add(new KeymapDiagnostic(drop.Line,
                $"chord conflicts with built-in '{drop.Keeper.WireName()}'; map skipped"));
        }

        return candidates;
    }

    /// <summary>One iteration of the fixpoint: find the first chord two distinct actions resolve to
    /// and return the loser/keeper/line, picking the colliding chord deterministically by its
    /// earliest-line loser. Null when the candidate set is collision-free.</summary>
    private static (BuiltinAction Loser, BuiltinAction Keeper, int Line)? FirstBuiltinCollision(
        Dictionary<BuiltinAction, Chord> candidates, Dictionary<BuiltinAction, int> overrideLine)
    {
        var ownersByChord = new Dictionary<Chord, List<BuiltinAction>>();
        foreach (var action in BuiltinActions.All)
        {
            // the same resolution Keymap.EquivalentFor performs, against the candidate overrides
            // being validated: a collision is about the chords that will actually be active, and on
            // Windows an unmoved default is the WindowsDefaultChords one.
            var chord = candidates.TryGetValue(action, out var overridden)
                ? overridden
                : WindowsDefaultChords.ShippedFor(action);
            if (chord is not { } c) continue;
            if (!ownersByChord.TryGetValue(c, out var owners))
                ownersByChord[c] = owners = new List<BuiltinAction>();
            owners.Add(action);
        }

        (BuiltinAction Loser, BuiltinAction Keeper, int Line)? best = null;
        foreach (var owners in ownersByChord.Values.Where(o => o.Count > 1))
        {
            var overriddenOwners = owners.Where(candidates.ContainsKey).ToList();
            var defaultOwners = owners.Where(o => !candidates.ContainsKey(o)).ToList();
            (BuiltinAction Loser, BuiltinAction Keeper)? decision;
            if (defaultOwners.Count > 0 && overriddenOwners.Count > 0)
            {
                // an unmoved default keeps the chord; an override that collided with it loses.
                decision = (overriddenOwners[0], defaultOwners[0]);
            }
            else if (overriddenOwners.Count > 1)
            {
                // two (or more) overrides claim the same chord: keep the earliest, drop the latest.
                var sorted = overriddenOwners.OrderBy(o => overrideLine.GetValueOrDefault(o)).ToList();
                decision = (sorted[^1], sorted[0]);
            }
            else
            {
                decision = null;
            }
            if (decision is not { } d) continue;
            var line = overrideLine.GetValueOrDefault(d.Loser);
            if (best is null || line < best.Value.Line)
                best = (d.Loser, d.Keeper, line);
        }
        return best;
    }

    /// <summary>
    /// Cross-section validation: drop a custom keybind whose FIRST chord equals any active built-in
    /// chord (built-ins are single-chord, so they shadow anything starting with that chord) OR that
    /// contains a reserved monitor chord at ANY position; then drop BOTH keybinds of any
    /// custom-vs-custom duplicate/prefix conflict. A dropped keybind clears the shortcut to "" (the
    /// command stays in the palette) and adds a line-0 diagnostic.
    /// </summary>
    private static List<CustomCommand> ValidateCommands(List<CustomCommand> commands, Keymap keymap,
        List<KeymapDiagnostic> diagnostics)
    {
        // active built-in chords: override when present, else the shipped default — so a custom
        // command may freely reuse a default chord the user moved a built-in off of.
        var builtinChords = new HashSet<Chord>(
            BuiltinActions.All.Select(keymap.EquivalentFor).Where(c => c is not null).Select(c => c!.Value));

        foreach (var command in commands)
        {
            if (command.Shortcut.Length == 0 || Keybinds.Parse(command.Shortcut) is not { Length: > 0 } keybind)
                continue;
            string? conflictKind;
            if (builtinChords.Contains(keybind[0]))
                conflictKind = "a built-in";
            else if (keybind.Any(Keybinds.IsReservedMonitorChord))
                conflictKind = "a reserved shortcut";
            else
                conflictKind = null;
            if (conflictKind is null) continue;
            diagnostics.Add(new KeymapDiagnostic(0,
                $"custom command '{command.Name}' shortcut '{command.Shortcut}' conflicts with {conflictKind}; keybind dropped"));
            command.Shortcut = "";
        }

        // drop BOTH keybinds of any custom-vs-custom conflict (computed over the post-pass-1 set).
        // One diagnostic per command names the OTHER offender so the user can find the pair.
        var conflicts = Keybinds.Conflicts(commands);
        var nameById = commands.ToDictionary(c => c.Id, c => c.Name);
        var otherOffender = new Dictionary<Guid, string>();
        foreach (var conflict in conflicts)
        {
            otherOffender[conflict.First] = nameById[conflict.Second];
            otherOffender[conflict.Second] = nameById[conflict.First];
        }
        foreach (var command in commands)
        {
            if (!otherOffender.TryGetValue(command.Id, out var other)) continue;
            diagnostics.Add(new KeymapDiagnostic(0,
                $"custom command '{command.Name}' shortcut '{command.Shortcut}' conflicts with custom command '{other}'; keybind dropped"));
            command.Shortcut = "";
        }

        return commands;
    }

    /// <summary>Strip a trailing inline comment: a # is a comment when preceded by whitespace AND
    /// outside a quoted span (single OR double — a shell line like git commit -m 'fix #42' keeps
    /// its #). Start-of-line counts as preceded-by-whitespace.</summary>
    private static string StripComment(string line)
    {
        var inSingleQuotes = false;
        var inDoubleQuotes = false;
        var previousWasSpace = true;
        var result = new StringBuilder(line.Length);
        foreach (var ch in line)
        {
            if (ch == '"' && !inSingleQuotes)
            {
                inDoubleQuotes = !inDoubleQuotes;
                result.Append(ch);
                previousWasSpace = false;
                continue;
            }
            if (ch == '\'' && !inDoubleQuotes)
            {
                inSingleQuotes = !inSingleQuotes;
                result.Append(ch);
                previousWasSpace = false;
                continue;
            }
            if (ch == '#' && !inSingleQuotes && !inDoubleQuotes && previousWasSpace)
                break;
            result.Append(ch);
            previousWasSpace = char.IsWhiteSpace(ch);
        }
        return result.ToString();
    }

    /// <summary>Parse the remainder of a map line: &lt;chord&gt; &lt;action&gt;. On failure appends a
    /// diagnostic and leaves the overrides untouched; duplicate detection is deferred to the final pass.</summary>
    private static void ParseMapLine(string rest, int line, List<ParsedOverride> overrides,
        List<KeymapDiagnostic> diagnostics)
    {
        // split on the first run of general whitespace (space OR tab).
        var chordEnd = 0;
        while (chordEnd < rest.Length && !char.IsWhiteSpace(rest[chordEnd])) chordEnd++;
        var chordText = rest[..chordEnd];
        var actionName = rest[chordEnd..].Trim();
        if (chordText.Length == 0 || actionName.Length == 0)
        {
            diagnostics.Add(new KeymapDiagnostic(line, "map requires a chord and an action"));
            return;
        }

        if (Keybinds.Parse(chordText) is not { } keybind)
        {
            diagnostics.Add(new KeymapDiagnostic(line, $"invalid chord '{chordText}'"));
            return;
        }
        if (keybind.Length != 1)
        {
            diagnostics.Add(new KeymapDiagnostic(line, "built-in shortcut cannot be a leader sequence"));
            return;
        }
        var chord = keybind[0];
        // a chord owned by an always-on monitor can't be a key-equivalent without dead-racing it.
        if (Keybinds.IsReservedMonitorChord(chord))
        {
            diagnostics.Add(new KeymapDiagnostic(line, $"chord '{chordText}' is a reserved shortcut; map skipped"));
            return;
        }
        // a modifier-less arrow would install an always-on key-equivalent that swallows the key
        // everywhere at once (terminal, palettes, dashboard grid, every text field).
        if (Keybinds.BindableArrowKeys.Contains(chord.Key) && chord.Mods == Modifiers.None)
        {
            diagnostics.Add(new KeymapDiagnostic(line,
                $"bare arrow chord '{chordText}' needs a modifier; map skipped"));
            return;
        }
        if (!BuiltinActions.TryParse(actionName, out var action))
        {
            diagnostics.Add(new KeymapDiagnostic(line, $"unknown action '{actionName}'"));
            return;
        }
        overrides.Add(new ParsedOverride(action, chord, line));
    }

    /// <summary>Parse the remainder of a command line: "&lt;name&gt;" [chord] &lt;shell...&gt;. On failure
    /// appends a diagnostic and leaves the commands untouched.</summary>
    private static void ParseCommandLine(string rest, int line, List<CustomCommand> commands,
        List<KeymapDiagnostic> diagnostics)
    {
        var closeQuote = rest.Length > 1 ? rest.IndexOf('"', 1) : -1;
        if (rest.Length == 0 || rest[0] != '"' || closeQuote < 0)
        {
            diagnostics.Add(new KeymapDiagnostic(line, "command requires a quoted name"));
            return;
        }
        var name = rest[1..closeQuote];
        var afterName = rest[(closeQuote + 1)..].Trim();

        // the token right after the closing quote is the chord iff it parses AND carries a modifier;
        // otherwise the whole remainder is the shell line (palette-only). A modifier is required so a
        // custom shortcut can't shadow a bare terminal key — and so a palette-only shell line that
        // happens to start with a single-char token isn't silently swallowed as a binding.
        var tokenEnd = 0;
        while (tokenEnd < afterName.Length && !char.IsWhiteSpace(afterName[tokenEnd])) tokenEnd++;
        var firstToken = afterName[..tokenEnd];
        var shortcut = "";
        var shellLine = afterName;
        if (firstToken.Length > 0 && Keybinds.Parse(firstToken) is { } keybind)
        {
            if (keybind[0].Mods != Modifiers.None)
            {
                shortcut = firstToken;
                shellLine = afterName[firstToken.Length..].Trim();
            }
            else
            {
                diagnostics.Add(new KeymapDiagnostic(line,
                    $"command '{name}' shortcut '{firstToken}' must include a modifier; treating the line as palette-only"));
            }
        }

        // an empty shell line (just a name, or a name + chord with no command) is a no-op binding.
        if (shellLine.Trim().Length == 0)
        {
            diagnostics.Add(new KeymapDiagnostic(line, $"command '{name}' has no shell line"));
            return;
        }

        commands.Add(new CustomCommand(name, shellLine, shortcut));
    }
}
