namespace AgTerm.Core.Keymap;

public sealed record KeybindingRow(string Id, string Title, string Shortcut, string Status);

public sealed record KeybindingGroup(string Name, IReadOnlyList<KeybindingRow> Items);

public static class KeybindingsCatalog
{
    public const string OpenerShortcut = "Ctrl+Shift+F1";

    public static IReadOnlyList<KeybindingGroup> Create(
        Keymap keymap,
        IReadOnlyList<KeymapDiagnostic>? diagnostics = null,
        HotkeyManifest? manifest = null)
    {
        ArgumentNullException.ThrowIfNull(keymap);
        var router = new HotkeyRouter(keymap);
        manifest ??= router.CreateManifest(0, _ => true);
        var rows = new List<(string Group, KeybindingRow Row)>();
        foreach (var action in BuiltinActions.All)
        {
            var chord = keymap.EquivalentFor(action);
            var shortcut = "";
            var status = "Unbound";
            if (chord is { } bound)
            {
                var physical = WindowsChordDecoder.Effective(bound);
                shortcut = WindowsShortcutText.Format(physical);
                status = router.Handle(physical) is { } winner && winner != action
                    ? $"Conflict: runs {winner.WireName()}"
                    : keymap.BuiltinOverrides.ContainsKey(action) ? "Override" : "Default";
            }
            rows.Add((GroupFor(action), new(action.WireName(), TitleFor(action), shortcut, status)));
        }
        foreach (var command in keymap.Commands)
        {
            var binding = manifest.CustomBindings.FirstOrDefault(item => item.CommandId == command.Id);
            var parsed = Keybinds.Parse(command.Shortcut);
            var chords = parsed?.Select(WindowsChordDecoder.Effective).ToArray();
            var shortcut = chords is null ? "" : string.Join(" > ", chords.Select(WindowsShortcutText.Format));
            var status = binding is not null ? "Custom" : "Unbound";
            if (binding is null && chords is { Length: > 0 })
            {
                if (router.Handle(chords[0]) is { } winner)
                    status = $"Conflict: runs {winner.WireName()}";
                else
                {
                    var owner = manifest.CustomBindings.FirstOrDefault(item =>
                        item.Chords.Take(Math.Min(item.Chords.Count, chords.Length))
                            .SequenceEqual(chords.Take(Math.Min(item.Chords.Count, chords.Length))));
                    status = owner is null
                        ? "Shortcut unavailable"
                        : $"Conflict: runs {keymap.Commands.First(item => item.Id == owner.CommandId).Name}";
                }
            }
            rows.Add(("Custom Commands", new($"custom-{command.Id:D}", command.Name, shortcut, status)));
        }
        rows.Add(("Application", new("keyboard-shortcuts", "Keyboard Shortcuts", OpenerShortcut, "Fixed app shortcut")));
        if (diagnostics is not null)
        {
            foreach (var diagnostic in diagnostics)
                rows.Add(("Diagnostics", new(diagnostic.Line > 0 ? $"line {diagnostic.Line}" : "",
                    diagnostic.Message, "", "Diagnostic")));
        }
        string[] order = ["Windows", "Workspaces", "Sessions", "Panes", "Navigation", "Appearance", "Palettes",
            "Custom Commands", "Application", "Diagnostics"];
        return order.Select(name => new KeybindingGroup(name,
                rows.Where(item => item.Group == name).Select(item => item.Row).ToArray()))
            .Where(group => group.Items.Count > 0).ToArray();
    }

    public static IReadOnlyList<KeybindingGroup> Filter(IReadOnlyList<KeybindingGroup> groups, string query)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return groups.Select(group => new KeybindingGroup(group.Name, group.Items.Where(row =>
                terms.All(term => $"{group.Name} {row.Id} {row.Title} {row.Shortcut} {row.Status}"
                    .Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray()))
            .Where(group => group.Items.Count > 0).ToArray();
    }

    private static string GroupFor(BuiltinAction action) => action switch
    {
        BuiltinAction.NewWindow or BuiltinAction.RenameWindow or BuiltinAction.DeleteWindow => "Windows",
        BuiltinAction.NewWorkspace or BuiltinAction.RenameWorkspace or BuiltinAction.DeleteWorkspace
            or BuiltinAction.FocusWorkspace => "Workspaces",
        BuiltinAction.ToggleSplit or BuiltinAction.ToggleScratch or BuiltinAction.ToggleTerminalZoom
            or BuiltinAction.FocusLeftPane or BuiltinAction.FocusRightPane or BuiltinAction.QuickTerminal => "Panes",
        BuiltinAction.PreviousSession or BuiltinAction.NextSession or BuiltinAction.PreviousAttentionSession
            or BuiltinAction.NextAttentionSession or BuiltinAction.FirstSession or BuiltinAction.LastSession => "Navigation",
        BuiltinAction.IncreaseFontSize or BuiltinAction.DecreaseFontSize or BuiltinAction.ResetFontSize
            or BuiltinAction.ToggleSidebar or BuiltinAction.SelectTheme or BuiltinAction.ToggleFullscreen => "Appearance",
        BuiltinAction.SessionPalette or BuiltinAction.CommandPalette or BuiltinAction.CustomCommandPalette
            or BuiltinAction.ShowAttention => "Palettes",
        _ => "Sessions",
    };

    public static string TitleFor(BuiltinAction action) => action switch
    {
        BuiltinAction.NewWindow => "New Window",
        BuiltinAction.RenameWindow => "Rename Window",
        BuiltinAction.DeleteWindow => "Delete Window",
        BuiltinAction.NewWorkspace => "New Workspace",
        BuiltinAction.RenameWorkspace => "Rename Workspace",
        BuiltinAction.DeleteWorkspace => "Delete Workspace",
        BuiltinAction.NewSession => "New Session",
        BuiltinAction.OpenDirectory => "Open Directory…",
        BuiltinAction.RenameSession => "Rename Session",
        BuiltinAction.DuplicateSession => "Duplicate Session",
        BuiltinAction.CloseSession => "Close Session",
        BuiltinAction.ReopenRecent => "Reopen Last Closed Item",
        BuiltinAction.UndoClose => "Reopen Closed Item",
        BuiltinAction.ClearStatus => "Clear Status",
        BuiltinAction.IncreaseFontSize => "Increase Font Size",
        BuiltinAction.DecreaseFontSize => "Decrease Font Size",
        BuiltinAction.ResetFontSize => "Actual Font Size",
        BuiltinAction.ToggleSplit => "Toggle Split",
        BuiltinAction.ToggleScratch => "Toggle Scratch",
        BuiltinAction.ToggleTerminalZoom => "Toggle Terminal Zoom",
        BuiltinAction.ToggleSearch => "Find…",
        BuiltinAction.ToggleSidebar => "Toggle Sidebar",
        BuiltinAction.SelectTheme => "Select Theme…",
        BuiltinAction.ToggleFullscreen => "Toggle Full Screen",
        BuiltinAction.ToggleFlaggedView => "Show Flagged Sessions",
        BuiltinAction.ToggleFlag => "Flag Session",
        BuiltinAction.FocusWorkspace => "Focus Workspace",
        BuiltinAction.FocusLeftPane => "Focus Left Pane",
        BuiltinAction.FocusRightPane => "Focus Right Pane",
        BuiltinAction.PreviousSession => "Previous Session",
        BuiltinAction.NextSession => "Next Session",
        BuiltinAction.PreviousAttentionSession => "Previous Attention Session",
        BuiltinAction.NextAttentionSession => "Next Attention Session",
        BuiltinAction.FirstSession => "First Session",
        BuiltinAction.LastSession => "Last Session",
        BuiltinAction.QuickTerminal => "Quick Terminal",
        BuiltinAction.ShowAttention => "Show Attention",
        BuiltinAction.Dashboard => "Dashboard",
        BuiltinAction.SessionPalette => "Go to Session…",
        BuiltinAction.CommandPalette => "Run an Action…",
        BuiltinAction.CustomCommandPalette => "Run a Custom Command…",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}
