using AgTerm.Core.Protocol;

namespace AgTerm.Cli;

/// <summary>
/// Response rendering — the port of the upstream SocketClient print/format helpers (the transport
/// itself lives in AgTerm.ControlServer.ControlPipeClient). Human-readable output is byte-for-byte
/// the upstream format; --json prints the raw response line.
/// </summary>
internal static class ClientOutput
{
    /// <summary>Print a response: the raw JSON line with json: true, otherwise a human-readable
    /// summary. An error response (ok == false, non---json) goes to stderr; everything else to stdout.</summary>
    public static void PrintResponse(CliEnvironment env, ControlResponse response, bool json,
                                     bool echoId = false)
    {
        if (!json && !response.Ok)
        {
            env.Error.WriteLine(FormatResponse(response, json: false));
            return;
        }
        env.Out.WriteLine(FormatResponse(response, json, echoId));
    }

    /// <summary>Render a response to a single string (no trailing newline): the raw JSON line with
    /// json: true, otherwise a human-readable summary — an error: line, the tree listing, the
    /// selected text, the affected id (only when echoId, i.e. for the create commands), or a bare ok.</summary>
    public static string FormatResponse(ControlResponse response, bool json, bool echoId = false)
    {
        if (json)
        {
            return ControlJson.Serialize(response);
        }
        if (!response.Ok)
        {
            return "error: " + (response.Error ?? "unknown error");
        }
        if (response.Result?.Tree is { } tree)
        {
            return FormatTree(tree);
        }
        if (response.Result?.Windows is { } windows)
        {
            return FormatWindows(windows);
        }
        if (response.Result?.Themes is { } themes)
        {
            return FormatThemes(themes, current: response.Result?.Theme,
                sync: response.Result?.Sync ?? false,
                light: response.Result?.Light, dark: response.Result?.Dark);
        }
        if (response.Result?.Keymap is { } keymap)
        {
            return FormatKeymap(keymap);
        }
        if (response.Result?.Hooks is { } hooks)
        {
            return FormatHooks(hooks);
        }
        if (response.Result?.Icons is { } icons)
        {
            return string.Join("\n", icons);
        }
        if (response.Result?.Recent is { } recent)
        {
            return FormatRecent(recent);
        }
        if (response.Result?.RecentOpen is { } open)
        {
            var reopened = $"reopened {open.Item.Kind} {open.Item.Title}";
            return open.Complete ? reopened : reopened + " (incomplete: the entry stays in the list)";
        }
        if (response.Result?.Text is { } text)
        {
            return text;
        }
        if (response.Result?.ExitCode is { } exitCode)
        {
            return $"exit {exitCode}";
        }
        if (response.Result?.Undoable is { } undoable)
        {
            var closed = response.Result.Affected is { } closedCount ? FormatAffected(closedCount) : "ok";
            return $"{closed} ({(undoable ? "undoable" : "not undoable")})";
        }
        if (response.Result?.Affected is { } affected)
        {
            return FormatAffected(affected);
        }
        if (response.Result?.Count is { } count)
        {
            // keymap.reload reports its parse-diagnostic count; 0 reads as a clean reload.
            return count == 0 ? "ok" : $"{count} diagnostic(s)";
        }
        if (response.Result?.Cursor is { } cursor)
        {
            // the bare column, scriptable as a command substitution; a second field belongs under --json.
            return cursor.Column.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (response.Result?.Ratio is { } ratio)
        {
            // session.resize echoes the applied (clamped) left-pane fraction, scriptable as a bare number.
            return ratio.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }
        if (response.Result?.SidebarWidth is { } sidebarWidth)
        {
            // sidebar.width echoes the stored (clamped) points with the double's own digits, so a caller
            // comparing its request against the echo reads a difference as a clamp. An integral width keeps
            // the ".0" upstream prints.
            var digits = sidebarWidth.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            return digits.Contains('.') || digits.Contains('E') ? digits : digits + ".0";
        }
        if (response.Result?.App is { } app)
        {
            var line = FormatIdentity(app);
            return response.Result.Installed is { } installed && installed != app
                ? line + "\ninstalled: " + FormatIdentity(installed)
                : line;
        }
        if (echoId && response.Result?.Id is { } id)
        {
            return id;
        }
        return "ok";
    }

    /// <summary>Render recent.list one entry per row with its full id, which `recent open` takes as is.</summary>
    public static string FormatRecent(IReadOnlyList<ControlRecentItem> items)
    {
        if (items.Count == 0) return RecentClosedTarget.EmptyError;
        return string.Join("\n", items.Select(item =>
        {
            var subtitle = item.Subtitle is { } s ? $" ({s})" : "";
            var companions = item.Kind == "session" && item.SessionIds.Length > 1
                ? $" +{item.SessionIds.Length - 1} companion(s)" : "";
            return $"{item.Id}  {item.Kind}  {item.Title}{subtitle}{companions}";
        }));
    }

    private static string FormatAffected(int affected) =>
        affected == 1 ? "1 session" : $"{affected} sessions";

    private static string FormatIdentity(ControlAppIdentity identity) =>
        string.IsNullOrEmpty(identity.Commit) ? identity.Version : $"{identity.Version} ({identity.Commit})";

    /// <summary>Render the hooks.list payload: the file path, one row per hook with its live state
    /// (running child, queue depth, dropped count, last failure, retired), then the parse diagnostics
    /// (no trailing newline).</summary>
    public static string FormatHooks(ControlHooks hooks)
    {
        var lines = new List<string> { $"hooks: {hooks.Path}" };
        if (hooks.Hooks.Length == 0) lines.Add("  (no hooks)");
        foreach (var hook in hooks.Hooks)
        {
            var row = $"  line {hook.Line}: on {hook.Kind} {hook.Command}";
            if (hook.RunningPid is { } pid)
            {
                row += $"  running pid {pid}";
                if (hook.ElapsedSeconds is { } elapsed)
                    row += $" for {(long)elapsed}s";
            }
            if (hook.Pending > 0) row += $"  pending {hook.Pending}";
            if (hook.Dropped > 0) row += $"  dropped {hook.Dropped}";
            if (hook.LastFailure is { } failure) row += $"  last failure: {failure}";
            if (hook.Retired == true) row += "  (retired, removed from the file)";
            lines.Add(row);
        }
        if (hooks.Diagnostics.Length > 0)
        {
            lines.Add("");
            lines.Add("diagnostics:");
            lines.AddRange(hooks.Diagnostics.Select(d => $"    line {d.Line}: {d.Message}"));
        }
        return string.Join("\n", lines);
    }

    /// <summary>Render the keymap.list payload: the file path, one row per action with its chord
    /// (`*` marks one the keymap file moved), then the custom commands and the diagnostics, each block
    /// left out when empty (no trailing newline).</summary>
    public static string FormatKeymap(ControlKeymap keymap)
    {
        var lines = new List<string> { $"keymap: {keymap.Path}", "", "actions:" };
        var width = keymap.Actions.Select(a => a.Action.Length).DefaultIfEmpty(0).Max();
        foreach (var action in keymap.Actions)
        {
            var mark = action.Overridden == true ? "*" : " ";
            lines.Add($"  {mark} {action.Action.PadRight(width)}  {action.Chord ?? "-"}");
        }
        if (keymap.Commands.Length > 0)
        {
            lines.Add("");
            lines.Add("commands:");
            lines.AddRange(keymap.Commands.Select(CommandRow));
        }
        if (keymap.Diagnostics.Length > 0)
        {
            lines.Add("");
            lines.Add("diagnostics:");
            // Line 0 stands for the whole file, not a real line, so no number is printed for it.
            lines.AddRange(keymap.Diagnostics.Select(d => d.Line > 0 ? $"    line {d.Line}: {d.Message}" : $"    {d.Message}"));
        }
        return string.Join("\n", lines);
    }

    private static string CommandRow(ControlKeymapCommand command)
    {
        var row = $"    {command.Name}  {command.Shortcut ?? "(palette only)"}";
        if (!command.ErrorHud)
        {
            return row;
        }
        row += $"  --error-hud --error-position {command.ErrorPosition}";
        return command.ErrorPane is null ? row : row + $" --error-pane {command.ErrorPane}";
    }

    /// <summary>Render the theme.list payload as one theme name per line, the active theme(s)
    /// marked with "* " and a leading "default terminal" entry for the no-theme case (no trailing
    /// newline). When sync is on, both the light and dark themes are marked and a header notes the
    /// appearance pair; otherwise the single current theme is marked.</summary>
    public static string FormatThemes(IReadOnlyList<string> themes, string? current, bool sync = false,
                                      string? light = null, string? dark = null)
    {
        Func<string?, bool> active = sync
            ? name => name is not null && (name == light || name == dark)
            : name => name == current;
        string Line(string? name, string label) => (active(name) ? "* " : "  ") + label;
        var body = string.Join("\n",
            new[] { Line(null, "default terminal") }.Concat(themes.Select(t => Line(t, t))));
        if (!sync) return body;
        var header = "syncing with Windows appearance — light: " + (light ?? "default terminal")
            + ", dark: " + (dark ?? "default terminal");
        return header + "\n" + body;
    }

    /// <summary>Render the window.list payload as one "id  name  [open]  [active]" line per window
    /// (no trailing newline). Closed/inactive windows still list, with the bracket tag absent.</summary>
    public static string FormatWindows(IReadOnlyList<ControlWindowNode> windows) =>
        string.Join("\n", windows.Select(window =>
        {
            var tags = (window.Open ? " [open]" : "") + (window.Active ? " [active]" : "");
            return $"{window.Id}  {window.Name}{tags}";
        }));

    /// <summary>Render a tree as an indented workspace → session listing (no trailing newline).</summary>
    public static string FormatTree(ControlTree tree)
    {
        var lines = new List<string>();
        foreach (var workspace in tree.Workspaces)
        {
            var mark = workspace.Active ? "*" : " ";
            lines.Add($"{mark} {workspace.Name}  [{workspace.Id}]");
            foreach (var session in workspace.Sessions)
            {
                var smark = session.Active ? "*" : " ";
                var tags = (session.Split ? " (split)" : session.HasSplit == true ? " (split hidden)" : "")
                    + (session.Overlay
                        ? session.HiddenOverlays?.Contains("session") == true ? " (overlay hidden)" : " (overlay)"
                        : "")
                    + (session.Scratch ? " (scratch)" : "");
                var titleSuffix = session.Title is { } title ? $"  title: {title}" : "";
                lines.Add($"  {smark} {session.Name}{tags}  [{session.Id}]  {session.Cwd}{titleSuffix}");
            }
        }
        return string.Join("\n", lines);
    }
}
